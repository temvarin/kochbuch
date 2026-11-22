using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Koch.Gateway.Contracts;

namespace Koch.App.Account;

public enum AccountStatus
{
    /// <summary>Not asked yet.</summary>
    Unknown,
    /// <summary>The gateway can't be reached; the app keeps working on what it has.</summary>
    Unreachable,
    SignedOut,
    /// <summary>Signed in, but the id isn't in the gateway's members list yet.</summary>
    NotMember,
    SignedIn,
}

/// <summary>Where the user stands with the gateway, and who they are if signed in.</summary>
public sealed class AccountService(HttpClient http)
{
    /// <summary>Easy Auth's sign-in page, which returns to the app afterwards. A full page load, not an app route.</summary>
    public const string SignInUrl = ".auth/login/aad?post_login_redirect_uri=/";

    public AccountStatus Status { get; private set; }

    /// <summary>null unless signed in.</summary>
    public WhoAmI? Me { get; private set; }

    public event Action? Changed;

    /// <summary>Asks the gateway. Also gives the session cookie its expiry date (the gateway's KeepSession).</summary>
    public async Task CheckAsync()
    {
        try
        {
            using var response = await http.GetAsync(new Uri("api/whoami", UriKind.Relative));
            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                Set(AccountStatus.SignedOut, null);
                return;
            }
            response.EnsureSuccessStatusCode();
            var me = await response.Content.ReadFromJsonAsync(GatewayJson.Default.WhoAmI)
                ?? throw new JsonException("The gateway answered without a user.");
            Set(me.Member ? AccountStatus.SignedIn : AccountStatus.NotMember, me);
        }
        catch (Exception e) when (e is HttpRequestException or JsonException or TaskCanceledException)
        {
            Set(AccountStatus.Unreachable, Me);
        }
    }

    /// <summary>A request was refused with 401: the sign-in has expired.</summary>
    public void SignedOut() => Set(AccountStatus.SignedOut, null);

    /// <summary>A request was refused with 403: removed from the members list.</summary>
    public void NotMember() => Set(AccountStatus.NotMember, Me);

    private void Set(AccountStatus status, WhoAmI? me)
    {
        (Status, Me) = (status, me);
        Changed?.Invoke();
    }
}
