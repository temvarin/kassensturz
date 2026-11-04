namespace Geld.Domain.Contracts;

/// <summary>The answer of <c>/api/whoami</c>.</summary>
/// <param name="Id">The id Microsoft gives the user, as listed in the server's members setting.</param>
/// <param name="Name">For display only, usually the email address.</param>
public sealed record WhoAmI(string Id, string Name, bool Member);
