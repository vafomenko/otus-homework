using System.Security.Claims;
using Blt.Users.Api.Data;
using Blt.Users.Api.Models;
using Blt.Users.Api.Observability;
using Blt.Users.Api.Security;
using Microsoft.Extensions.Options;

namespace Blt.Users.Api.Endpoints;

public static class AuthEndpoints
{
    public static void Map(WebApplication app)
    {
        var group = app.MapGroup("/auth");
        group.MapPost("/register", RegisterAsync).AllowAnonymous().RequireRateLimiting("credentials").WithMetadata(new ApiMetricRoute("/auth/register"));
        group.MapPost("/login", LoginAsync).AllowAnonymous().RequireRateLimiting("credentials").WithMetadata(new ApiMetricRoute("/auth/login"));
        group.MapPost("/logout", LogoutAsync).RequireAuthorization().WithMetadata(new ApiMetricRoute("/auth/logout"));
    }

    private static async Task<IResult> RegisterAsync(RegisterRequest? request, AuthRepository repository,
        PasswordService passwords, BillingAccounts billing, ILoggerFactory loggerFactory, CancellationToken cancellationToken)
    {
        if (AuthValidation.ValidateRegistration(request) is { } error)
            return Results.BadRequest(new ErrorResponse(400, error));
        try
        {
            var user = await repository.RegisterAsync(UserValidation.Normalize(request!.Profile),
                passwords.Hash(request.Password!), cancellationToken);
            await billing.CreateAsync(user.Id, cancellationToken);
            loggerFactory.CreateLogger("Auth").LogInformation("{event}: registered user {user_id}", "user_registered", user.Id);
            return Results.Created($"/user/{user.Id}", user);
        }
        catch (UserConflictException)
        {
            return Results.Conflict(new ErrorResponse(409, "username or email already exists."));
        }
    }

    private static async Task<IResult> LoginAsync(LoginRequest? request, AuthRepository repository,
        PasswordService passwords, TimeProvider time, IOptions<AuthSessionOptions> options,
        ILoggerFactory loggerFactory, CancellationToken cancellationToken)
    {
        if (!AuthValidation.IsValidLogin(request))
            return Results.BadRequest(new ErrorResponse(400, "username and password (up to 128 characters) are required."));
        var credential = await repository.FindCredentialAsync(request!.Username!.Trim(), cancellationToken);
        var logger = loggerFactory.CreateLogger("Auth");
        if (!passwords.Verify(credential?.PasswordHash, request.Password!))
        {
            logger.LogWarning("{event}: invalid credentials", "login_failed");
            return Results.Json(new ErrorResponse(401, "Invalid username or password."), statusCode: 401);
        }

        var token = SessionToken.Create();
        var now = time.GetUtcNow();
        var expiresAt = now.AddMinutes(options.Value.LifetimeMinutes);
        await repository.CreateSessionAsync(credential!.UserId, SessionToken.Hash(token), expiresAt, now, cancellationToken);
        logger.LogInformation("{event}: user {user_id} logged in", "login_succeeded", credential.UserId);
        return Results.Ok(new LoginResponse(token, "Bearer", expiresAt, credential.UserId));
    }

    private static async Task<IResult> LogoutAsync(ClaimsPrincipal user, AuthRepository repository,
        ILoggerFactory loggerFactory, CancellationToken cancellationToken)
    {
        await repository.RevokeSessionAsync(user.FindFirstValue(SessionAuthenticationHandler.TokenHashClaim)!, cancellationToken);
        loggerFactory.CreateLogger("Auth").LogInformation("{event}: user {user_id} logged out", "logout_succeeded", ProfileOwner.UserId(user));
        return Results.NoContent();
    }
}
