using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Geld.Domain.Contracts;

namespace Geld.App.Sync;

/// <summary>The signed-in user, as the app shows it; the server says it as Contracts.WhoAmI.</summary>
/// <param name="Id">The id Microsoft gives the user, which the server's members list holds.</param>
/// <param name="Name">For display only, usually the email address.</param>
/// <param name="Member">Whether the user is one of the family's members.</param>
public sealed record Account(string Id, string Name, bool Member);

public enum AccountStatus
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

    /// <summary>Signed in as a member whose data this is (or a device that never synced): the sync may run.</summary>
    SignedIn,
}

/// <summary>
/// Who is signed in on this device, and whether that account may sync this device's data. Part of the sync module: in
/// the app, the account exists only for the sync. Signing in itself happens outside the app, at App Service's Easy
/// Auth.
/// </summary>
public sealed class AccountService(AppState state, HttpClient http)
{
    public AccountStatus Status { get; private set; }

    /// <summary>The signed-in user; null if signed out or not known.</summary>
    public Account? Me { get; private set; }

    /// <summary>When <see cref="Status"/> or <see cref="Me"/> changes.</summary>
    public event Action? Changed;

    /// <summary>Asks the server who is signed in (<c>/api/whoami</c>).</summary>
    public async Task CheckAsync()
    {
        Set(AccountStatus.Checking, Me);
        try
        {
            using var response = await http.GetAsync(new Uri("api/whoami", UriKind.Relative));
            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                SignedOut();
                return;
            }
            response.EnsureSuccessStatusCode();
            var me = await response.Content.ReadFromJsonAsync(ApiJson.Default.WhoAmI) is { } who ? new Account(who.Id, who.Name, who.Member) : null;
            Set(me switch
            {
                null => AccountStatus.Unreachable,
                { Member: false } => AccountStatus.NotMember,
                _ when state.Data.Synced.State is { } sync && sync.UserId != me.Id => AccountStatus.OtherAccount,
                _ => AccountStatus.SignedIn,
            }, me);
        }
        catch (Exception e) when (e is HttpRequestException or JsonException or TaskCanceledException)
        {
            Set(AccountStatus.Unreachable, null);
        }
    }

    /// <summary>The server said so during a sync: the sign-in has expired.</summary>
    public void SignedOut() => Set(AccountStatus.SignedOut, null);

    /// <summary>The server said so during a sync: the user was taken off the members list.</summary>
    public void NotMember() => Set(AccountStatus.NotMember, Me);

    private void Set(AccountStatus status, Account? me)
    {
        (Status, Me) = (status, me);
        Changed?.Invoke();
    }
}
