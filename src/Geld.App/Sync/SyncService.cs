using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Geld.Domain.Contracts;

namespace Geld.App.Sync;

public enum SyncStatus
{
    /// <summary>The last sync (if any, see <see cref="Geld.Domain.Sync.SyncedData.State"/>) went through.</summary>
    Ready,

    Syncing,

    /// <summary>
    /// The last sync failed. The changes stay here and go with the next one, which is tried once more after
    /// <see cref="SyncService.RetryAfter"/>.
    /// </summary>
    Failed,

    /// <summary>The server has a newer version of the app: this one must be restarted to get it.</summary>
    Outdated,
}

/// <summary>
/// Keeps the data on this device in step with the server: sends what changed here, takes what changed elsewhere.
/// Syncs at start, when the app comes back to the front, and shortly after each save, whenever
/// <see cref="AccountService"/> says the signed-in account may. Only one sync runs at a time, so answers can't arrive
/// out of order and bring back older data.
/// </summary>
public sealed class SyncService
{
    /// <summary>A save is sent this long after it, so that a few quick changes go together.</summary>
    public static readonly TimeSpan AfterSave = TimeSpan.FromSeconds(2);

    /// <summary>Coming back to the app within this time of the last sync doesn't sync again, e.g. when switching apps.</summary>
    public static readonly TimeSpan Recent = TimeSpan.FromMinutes(1);

    /// <summary>A failed sync is tried once more after this; never more often, so a struggling server isn't flooded.</summary>
    public static readonly TimeSpan RetryAfter = TimeSpan.FromSeconds(30);

    private readonly AppState state;
    private readonly AccountService account;
    private readonly HttpClient http;
    private readonly TimeProvider clock;
    private bool started, running, again, scheduled;

    // Set while a failed sync waits to be tried once more; "Jetzt synchronisieren" ends the wait.
    private TaskCompletionSource? retryNow;

    public SyncService(AppState state, AccountService account, HttpClient http, TimeProvider clock)
    {
        (this.state, this.account, this.http, this.clock) = (state, account, http, clock);
        account.Changed += () => Changed?.Invoke(); // what may sync changes with the account
    }

    public SyncStatus Status { get; private set; }

    /// <summary>When <see cref="Status"/> changes, or the account (<see cref="CanSync"/> depends on it).</summary>
    public event Action? Changed;

    public bool CanSync => account.Status == AccountStatus.SignedIn && Status != SyncStatus.Outdated;

    /// <summary>When the data on this device was last synced; null if never, which also means it never left the device.</summary>
    public DateTimeOffset? LastSync => state.Data.Synced.State?.LastSync;

    /// <summary>Once, when the app starts; later calls do nothing.</summary>
    public async Task StartAsync()
    {
        if (started) return;
        started = true;
        state.Changed += OnSaved;
        await account.CheckAsync();
        await SyncAsync();
    }

    /// <summary>
    /// When the app comes back to the front. A phone rarely starts an installed app anew: it keeps it in memory and
    /// brings it back, so this is when the other phone's changes are fetched.
    /// </summary>
    public async Task ResumeAsync()
    {
        if (!started || running) return;
        if (account.Status == AccountStatus.Unreachable) await account.CheckAsync(); // opened offline, maybe online now
        if (LastSync is { } last && clock.GetUtcNow() - last < Recent) return;
        await SyncAsync();
    }

    /// <summary>"Jetzt synchronisieren". After a failed sync, it ends the wait for the next try.</summary>
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

    private void OnSaved()
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
                    Set(SyncStatus.Syncing);
                    result = await SendAsync();
                }
                Set(result);
            } while (again && CanSync && Status == SyncStatus.Ready);
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
    /// <see cref="SyncStatus.Ready"/> also when the server refused for a reason of the account (signed out, not a member,
    /// another account signed in): that is <see cref="AccountService"/>'s to show, and the sync stops until it allows.
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
                case HttpStatusCode.PreconditionFailed: // someone else signed in meanwhile, e.g. in another tab
                    await account.CheckAsync();
                    return SyncStatus.Ready;
            }
            response.EnsureSuccessStatusCode();
            var answer = (await response.Content.ReadFromJsonAsync(ApiJson.Default.SyncResponseJson) ?? throw new JsonException("No answer."))
                .ToDomain();
            state.Change(data => data.Synced.CompleteSync(pending, answer, clock.GetUtcNow()));
        }
        // An answer that breaks the rules (ArgumentException) counts as none: it is never taken over.
        catch (Exception e) when (e is HttpRequestException or JsonException or TaskCanceledException or ArgumentException)
        {
            // Without an answer, the server may have saved the changes anyway: the next sync takes that into account.
            state.Change(data => data.Synced.SyncFailed(pending));
            return SyncStatus.Failed;
        }
        return SyncStatus.Ready; // if something changed meanwhile, the change above made OnSaved ask for another sync
    }

    private void Set(SyncStatus status)
    {
        Status = status;
        Changed?.Invoke();
    }
}
