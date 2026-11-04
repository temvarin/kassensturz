using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Geld.Domain.Contracts;

namespace Geld.App.Sync;

/// <param name="Id">The id Microsoft gives the user, as listed in the server's members setting.</param>
/// <param name="Name">For display only, usually the email address.</param>
public sealed record Account(string Id, string Name, bool Member);

public enum AccountStatus
{
    Checking,

    /// <summary>No answer, because the phone is offline or the development server has no API.</summary>
    Unreachable,

    SignedOut,

    NotMember,

    /// <summary>The data here was synced with another account, and is never synced with this one.</summary>
    OtherAccount,

    /// <summary>A member whose data this is, or a device that never synced. The sync may run.</summary>
    SignedIn,
}

/// <summary>
/// Who is signed in, and whether that account may sync the data on this device. It belongs to the sync module because
/// in the app the account exists only for the sync. Signing in happens outside the app, at App Service's Easy Auth.
/// </summary>
public sealed class AccountService(AppState state, HttpClient http)
{
    public AccountStatus Status { get; private set; }

    /// <summary>null if signed out or not known.</summary>
    public Account? Me { get; private set; }

    public event Action? Changed;

    /// <summary>Asks <c>/api/whoami</c>.</summary>
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

    /// <summary>A sync found the sign-in expired.</summary>
    public void SignedOut() => Set(AccountStatus.SignedOut, null);

    /// <summary>A sync found the user taken off the members list.</summary>
    public void NotMember() => Set(AccountStatus.NotMember, Me);

    private void Set(AccountStatus status, Account? me)
    {
        (Status, Me) = (status, me);
        Changed?.Invoke();
    }
}
