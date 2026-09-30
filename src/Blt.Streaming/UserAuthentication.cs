using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Blt.Streaming;

// Session ownership remains in Users. Other services never read the Users database.
public sealed class UserAuthentication(IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger, UrlEncoder encoder, IHttpClientFactory clients)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!AuthenticationHeaderValue.TryParse(Request.Headers.Authorization, out var header) ||
            !header.Scheme.Equals("Bearer", StringComparison.OrdinalIgnoreCase) || string.IsNullOrEmpty(header.Parameter))
            return AuthenticateResult.NoResult();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/profile");
        request.Headers.Authorization = header;
        using var response = await clients.CreateClient("users").SendAsync(request, Context.RequestAborted);
        if (response.StatusCode == HttpStatusCode.Unauthorized) return AuthenticateResult.Fail("Invalid session.");
        response.EnsureSuccessStatusCode();
        var profile = await response.Content.ReadFromJsonAsync<Profile>(Context.RequestAborted)
            ?? throw new InvalidOperationException("Users returned an empty profile.");
        var identity = new ClaimsIdentity([
            new Claim(ClaimTypes.NameIdentifier, profile.Id.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            new Claim(ClaimTypes.Email, profile.Email)], Scheme.Name);
        return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name));
    }
    private sealed record Profile(long Id, string Email);
}

public static class CurrentUser
{
    public static long Id(ClaimsPrincipal user) => long.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!, System.Globalization.CultureInfo.InvariantCulture);
    public static string Email(ClaimsPrincipal user) => user.FindFirstValue(ClaimTypes.Email)!;
}
