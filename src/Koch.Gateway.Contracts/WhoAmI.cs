namespace Koch.Gateway.Contracts;

/// <summary>The answer of <c>/api/whoami</c>.</summary>
/// <param name="Id">The id Microsoft Entra gives the user, as listed in the gateway's members setting.</param>
/// <param name="Name">For display only, usually the email address.</param>
/// <param name="Member">Whether the user may use the household's recipes and lists.</param>
public sealed record WhoAmI(string Id, string Name, bool Member);
