using System.Globalization;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Blt.Users.Api.Data;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;

namespace Blt.Users.Api.Security;

public sealed class SessionAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger,
    UrlEncoder encoder, AuthRepository repository, TimeProvider time)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "Session";
    public const string TokenHashClaim = "session_hash";

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (Context.GetEndpoint()?.Metadata.GetMetadata<IAllowAnonymous>() is not null ||
            !Request.Headers.ContainsKey("Authorization"))
            return AuthenticateResult.NoResult();

        if (Request.Headers.Authorization.Count != 1 ||
            !AuthenticationHeaderValue.TryParse(Request.Headers.Authorization.ToString(), out var header) ||
            !string.Equals(header.Scheme, "Bearer", StringComparison.OrdinalIgnoreCase) ||
            header.Parameter is not { } token || !SessionToken.IsValidFormat(token))
            return AuthenticateResult.Fail("Invalid bearer token.");

        var hash = SessionToken.Hash(token);
        var userId = await repository.FindSessionAsync(hash, time.GetUtcNow(), Context.RequestAborted);
        if (userId is null) return AuthenticateResult.Fail("Invalid or expired session.");

        var identity = new ClaimsIdentity([
            new Claim(ClaimTypes.NameIdentifier, userId.Value.ToString(CultureInfo.InvariantCulture)),
            new Claim(TokenHashClaim, hash)
        ], SchemeName);
        return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName));
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = StatusCodes.Status401Unauthorized;
        Response.Headers.WWWAuthenticate = "Bearer";
        return Response.WriteAsJsonAsync(new ErrorResponse(401, "Authentication required."));
    }

    protected override Task HandleForbiddenAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = StatusCodes.Status403Forbidden;
        return Response.WriteAsJsonAsync(new ErrorResponse(403, "Access to another user's profile is forbidden."));
    }
}
