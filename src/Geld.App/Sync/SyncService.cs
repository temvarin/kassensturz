using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Geld.Domain.Contracts;
using Geld.Domain.Sync;

namespace Geld.App.Sync;

public enum SyncStatus
{
    Ready,

    Syncing,

    /// <summary>The changes stay here and go with the next sync, which is tried once after <see cref="SyncService.RetryAfter"/>.</summary>
    Failed,

    /// <summary>The server runs a newer version; this one has to be restarted.</summary>
    Outdated,

    /// <summary>
    /// The server refused the changes (400). They stay here, but sending them again can't help, so no sync runs until
    /// the app is restarted.
    /// </summary>
    Refused,
}

/// <summary>
/// Syncs at start, when the app comes back to the front, and shortly after each save, whenever the account allows it.
/// Only one sync runs at a time, so answers can't arrive out of order and bring back older data.
/// </summary>
public sealed class SyncService
{
    /// <summary>The wait after a save, so that a few quick changes go out together.</summary>
    public static readonly TimeSpan AfterSave = TimeSpan.FromSeconds(2);

    /// <summary>Coming back within this time of the last sync, e.g. after switching apps, doesn't sync again.</summary>
    public static readonly TimeSpan Recent = TimeSpan.FromMinutes(1);

    /// <summary>A failed sync is tried once more after this, and no more often, so a struggling server isn't flooded.</summary>
    public static readonly TimeSpan RetryAfter = TimeSpan.FromSeconds(30);

    private readonly AppState state;
    private readonly AccountService account;
    private readonly HttpClient http;
    private readonly TimeProvider clock;
    // running: a sync is under way. again: something changed meanwhile, so one more sync follows right after.
    // scheduled: a save is waiting out AfterSave. retryNow: a failed sync waits to be tried again, and
    // "Jetzt synchronisieren" ends the wait.
    private bool started, running, again, scheduled;
    private TaskCompletionSource? retryNow;

    public SyncService(AppState state, AccountService account, HttpClient http, TimeProvider clock)
    {
        (this.state, this.account, this.http, this.clock) = (state, account, http, clock);
        account.Changed += () => Changed?.Invoke(); // CanSync depends on the account
    }

    public SyncStatus Status { get; private set; }

    public event Action? Changed;

    public bool CanSync => account.Status == AccountStatus.SignedIn && Status is not (SyncStatus.Outdated or SyncStatus.Refused);

    /// <summary>null if never, which also means the data never left this device.</summary>
    public DateTimeOffset? LastSync => state.Data.Synced.State?.LastSync;

    /// <summary>Later calls do nothing.</summary>
    public async Task StartAsync()
    {
        if (started) return;
        started = true;
        state.Changed += OnDataChanged;
        await account.CheckAsync();
        await SyncAsync();
    }

    /// <summary>
    /// When the app comes back to the front. Phones rarely restart an installed app; they bring it back from memory, so
    /// this is when the other phone's changes arrive.
    /// </summary>
    public async Task ResumeAsync()
    {
        if (!started || running) return;
        if (account.Status == AccountStatus.Unreachable) await account.CheckAsync(); // opened offline, maybe online now
        if (LastSync is { } last && clock.GetUtcNow() - last < Recent) return;
        await SyncAsync();
    }

    /// <summary>"Jetzt synchronisieren". After a failed sync, it ends the wait for the second try.</summary>
    public Task SyncNowAsync()
    {
        if (retryNow is not { } now) return SyncAsync();
        now.TrySetResult();
        return Task.CompletedTask;
    }

    /// <summary>Deletes the data on this device, which belongs to another account, and syncs with this one.</summary>
    public async Task ResetAsync()
    {
        state.Reset();
        await account.CheckAsync();
        await SyncAsync();
    }

    private void OnDataChanged()
    {
        if (!CanSync || !state.Data.Synced.HasUnsyncedChanges) return;
        if (running) again = true; // sent as soon as the running sync is done
        else if (!scheduled) _ = SyncSoonAsync();
    }

    private async Task SyncSoonAsync()
    {
        scheduled = true;
        await Task.Delay(AfterSave, clock);
        scheduled = false;
        await SyncAsync();
    }

    private async Task SyncAsync()
    {
        if (!CanSync) return;
        if (running)
        {
            again = true;
            return;
        }
        running = true;
        try
        {
            do
            {
                again = false;
                Set(SyncStatus.Syncing);
                var result = await SendAsync();
                if (result == SyncStatus.Failed && CanSync)
                {
                    Set(SyncStatus.Failed);
                    await WaitToRetryAsync();
                    if (!CanSync) break;
                    Set(SyncStatus.Syncing);
                    result = await SendAsync();
                }
                Set(result);
            } while (again && CanSync && Status == SyncStatus.Ready && state.Data.Synced.HasUnsyncedChanges);
        }
        finally
        {
            running = false;
        }
    }

    private async Task WaitToRetryAsync()
    {
        retryNow = new TaskCompletionSource();
        await Task.WhenAny(Task.Delay(RetryAfter, clock), retryNow.Task);
        retryNow = null;
    }

    /// <returns>
    /// Also <see cref="SyncStatus.Ready"/> when the server refused because of the account. <see cref="AccountService"/>
    /// shows that, and no sync runs until the account allows it again.
    /// </returns>
    private async Task<SyncStatus> SendAsync()
    {
        var pending = state.Data.Synced.StartSync(account.Me!.Id);
        try
        {
            using var response = await http.PostAsJsonAsync(
                new Uri("api/sync", UriKind.Relative), SyncRequestJson.From(pending.Request), ApiJson.Default.SyncRequestJson);
            switch (response.StatusCode)
            {
                case HttpStatusCode.Unauthorized: // the sign-in has expired
                    account.SignedOut();
                    return SyncStatus.Ready;
                case HttpStatusCode.Forbidden:
                    account.NotMember();
                    return SyncStatus.Ready;
                case HttpStatusCode.Conflict:
                    return SyncStatus.Outdated;
                case HttpStatusCode.BadRequest:
                    return SyncStatus.Refused;
                case HttpStatusCode.PreconditionFailed: // someone else signed in meanwhile, e.g. in another tab
                    await account.CheckAsync();
                    return SyncStatus.Ready;
            }
            response.EnsureSuccessStatusCode();
            var answer = await ReadAnswerAsync(response);
            state.Change(data => data.Synced.CompleteSync(pending, answer, clock.GetUtcNow()));
        }
        catch (Exception e) when (e is HttpRequestException or JsonException or TaskCanceledException)
        {
            // The server may have saved the changes anyway, and the next sync allows for that.
            state.Change(data => data.Synced.SyncFailed(pending));
            return SyncStatus.Failed;
        }
        return SyncStatus.Ready; // changes made meanwhile already asked for another sync through OnDataChanged
    }

    // An answer that breaks the domain's rules is never taken over; it counts as no answer.
    private static async Task<SyncResponse> ReadAnswerAsync(HttpResponseMessage response)
    {
        var json = await response.Content.ReadFromJsonAsync(ApiJson.Default.SyncResponseJson) ?? throw new JsonException("No answer.");
        try
        {
            return json.ToDomain();
        }
        catch (ArgumentException e)
        {
            throw new JsonException("The answer breaks the domain's rules.", e);
        }
    }

    private void Set(SyncStatus status)
    {
        Status = status;
        Changed?.Invoke();
    }
}
