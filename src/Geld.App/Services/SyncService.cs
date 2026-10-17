using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Geld.Domain;

namespace Geld.App.Services;

public enum SyncStatus
{
    /// <summary>Asking the server who is signed in.</summary>
    Checking,

    /// <summary>No answer: offline, or the development server, which has no API.</summary>
    Unreachable,

    SignedOut,

    /// <summary>Signed in, but not on the server's members list.</summary>
    NotMember,

    /// <summary>The data on this device was synced with another account; it is never synced with this one.</summary>
    OtherAccount,

    Syncing,

    /// <summary>Signed in, and the last sync (if any, see <see cref="AppData.Sync"/>) went through.</summary>
    Ready,

    /// <summary>The last sync failed, also when tried again. The changes stay here and go with the next one.</summary>
    Failed,

    /// <summary>The server has a newer version of the app: this one must be restarted to get it.</summary>
    Outdated,
}

/// <summary>
/// Keeps the data on this device in step with the server: sends what changed here, takes what changed elsewhere.
/// Syncs at start, when the app comes back to the front, and shortly after each save.
/// Only one sync runs at a time, so answers can't arrive out of order and bring back older data.
/// </summary>
public sealed class SyncService(AppState state, HttpClient http, TimeProvider clock)
{
    /// <summary>A save is sent this long after it, so that a few quick changes go together.</summary>
    public static readonly TimeSpan AfterSave = TimeSpan.FromSeconds(2);

    /// <summary>Coming back to the app within this time of the last sync doesn't sync again, e.g. when switching apps.</summary>
    public static readonly TimeSpan Recent = TimeSpan.FromMinutes(1);

    /// <summary>A failed sync is tried once more after this; never more often, so a struggling server isn't flooded.</summary>
    public static readonly TimeSpan RetryAfter = TimeSpan.FromSeconds(30);

    private bool started, running, again, scheduled;

    public SyncStatus Status { get; private set; }

    /// <summary>The signed-in user; null if signed out or not known.</summary>
    public WhoAmI? Me { get; private set; }

    /// <summary>When <see cref="Status"/> changes.</summary>
    public event Action? Changed;

    public bool CanSync => Status is SyncStatus.Ready or SyncStatus.Syncing or SyncStatus.Failed;

    /// <summary>Once, when the app starts; later calls do nothing.</summary>
    public async Task StartAsync()
    {
        if (started) return;
        started = true;
        state.Changed += OnSaved;
        await CheckAccountAsync();
        await SyncAsync();
    }

    /// <summary>
    /// When the app comes back to the front. A phone rarely starts an installed app anew: it keeps it in memory and
    /// brings it back, so this is when the other phone's changes are fetched.
    /// </summary>
    public async Task ResumeAsync()
    {
        if (!started || running) return;
        if (Status == SyncStatus.Unreachable) await CheckAccountAsync(); // opened offline, maybe online now
        if (state.Data.Sync is { } sync && clock.GetUtcNow() - sync.LastSync < Recent) return;
        await SyncAsync();
    }

    /// <summary>"Jetzt synchronisieren".</summary>
    public Task SyncNowAsync() => SyncAsync();

    /// <summary>Deletes the data on this device, which belongs to another account, and syncs with this one.</summary>
    public async Task ResetAsync()
    {
        state.Reset();
        await CheckAccountAsync();
        await SyncAsync();
    }

    private async Task CheckAccountAsync()
    {
        Set(SyncStatus.Checking);
        try
        {
            using var response = await http.GetAsync(new Uri("api/whoami", UriKind.Relative));
            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                Me = null;
                Set(SyncStatus.SignedOut);
                return;
            }
            response.EnsureSuccessStatusCode();
            Me = await response.Content.ReadFromJsonAsync(ApiJson.Default.WhoAmI);
        }
        catch (Exception e) when (e is HttpRequestException or JsonException or TaskCanceledException)
        {
            Me = null;
        }
        Set(Me switch
        {
            null => SyncStatus.Unreachable,
            { Member: false } => SyncStatus.NotMember,
            _ when state.Data.Sync is { } sync && sync.UserId != Me.Id => SyncStatus.OtherAccount,
            _ => SyncStatus.Ready,
        });
    }

    private void OnSaved()
    {
        if (!CanSync || !state.Data.HasUnsyncedChanges) return;
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
                if (result == SyncStatus.Failed)
                {
                    await Task.Delay(RetryAfter, clock);
                    result = await SendAsync();
                }
                Set(result);
            } while (again && Status == SyncStatus.Ready);
        }
        finally
        {
            running = false;
        }
    }

    private async Task<SyncStatus> SendAsync()
    {
        var pending = state.Data.StartSync();
        try
        {
            using var response = await http.PostAsJsonAsync(new Uri("api/sync", UriKind.Relative), pending.Request, ApiJson.Default.SyncRequest);
            switch (response.StatusCode)
            {
                case HttpStatusCode.Unauthorized: // the sign-in has expired
                    Me = null;
                    return SyncStatus.SignedOut;
                case HttpStatusCode.Forbidden:
                    return SyncStatus.NotMember;
                case HttpStatusCode.Conflict:
                    return SyncStatus.Outdated;
            }
            response.EnsureSuccessStatusCode();
            var answer = await response.Content.ReadFromJsonAsync(ApiJson.Default.SyncResponse) ?? throw new JsonException("No answer.");
            state.Data.CompleteSync(pending, answer, Me!.Id, clock.GetUtcNow());
        }
        catch (Exception e) when (e is HttpRequestException or JsonException or TaskCanceledException)
        {
            return SyncStatus.Failed;
        }
        state.Save(); // if something changed meanwhile, OnSaved asks for another sync
        return SyncStatus.Ready;
    }

    private void Set(SyncStatus status)
    {
        Status = status;
        Changed?.Invoke();
    }
}
