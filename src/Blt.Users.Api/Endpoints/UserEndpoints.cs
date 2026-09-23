using Blt.Users.Api.Data;
using Blt.Users.Api.Models;
using Blt.Users.Api.Observability;
using Blt.Users.Api.Security;
using System.Security.Claims;

namespace Blt.Users.Api.Endpoints;

public static class UserEndpoints
{
    public static void Map(WebApplication app, string prefix)
    {
        // Aliases and concrete IDs share one bounded set of metric labels.
        var group = app.MapGroup(prefix).RequireAuthorization();
        group.MapGet("", ListAsync).WithMetadata(new ApiMetricRoute("/user"));
        group.MapGet("/{id:long}", GetAsync).RequireAuthorization(ProfileOwner.PolicyName).WithMetadata(new ApiMetricRoute("/user/{id}"));
        group.MapPut("/{id:long}", UpdateAsync).RequireAuthorization(ProfileOwner.PolicyName).WithMetadata(new ApiMetricRoute("/user/{id}"));
        group.MapDelete("/{id:long}", DeleteAsync).RequireAuthorization(ProfileOwner.PolicyName).WithMetadata(new ApiMetricRoute("/user/{id}"));
    }

    public static void MapProfile(WebApplication app)
    {
        var group = app.MapGroup("/profile").RequireAuthorization();
        group.MapGet("", (ClaimsPrincipal user, UserRepository repository, ILoggerFactory logger, CancellationToken ct) =>
            GetAsync(ProfileOwner.UserId(user), repository, logger, ct)).WithMetadata(new ApiMetricRoute("/profile"));
        group.MapPut("", (ClaimsPrincipal user, UpdateUserRequest? request, UserRepository repository, ILoggerFactory logger, CancellationToken ct) =>
            UpdateAsync(ProfileOwner.UserId(user), request, repository, logger, ct)).WithMetadata(new ApiMetricRoute("/profile"));
    }

    private static async Task<IResult> ListAsync(
        ClaimsPrincipal principal,
        UserRepository repository,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        try
        {
            var user = await repository.GetAsync(ProfileOwner.UserId(principal), cancellationToken);
            loggerFactory.CreateLogger("Users").LogInformation("{event}: listed own profile {user_id}", "users_listed", ProfileOwner.UserId(principal));
            return Results.Ok(user is null ? Array.Empty<UserResponse>() : new[] { user });
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            loggerFactory.CreateLogger("Users").LogError(exception, "{event}: unable to list users.", "database_error");
            return ServiceUnavailable();
        }
    }

    private static async Task<IResult> GetAsync(
        long id,
        UserRepository repository,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        try
        {
            var user = await repository.GetAsync(id, cancellationToken);
            loggerFactory.CreateLogger("Users").Log(user is null ? LogLevel.Warning : LogLevel.Information, "{event}: user {user_id}", user is null ? "user_not_found" : "user_read", id);
            return user is null
                ? NotFound(id)
                : Results.Ok(user);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            loggerFactory.CreateLogger("Users").LogError(exception, "{event}: unable to get user {user_id}.", "database_error", id);
            return ServiceUnavailable();
        }
    }

    private static async Task<IResult> UpdateAsync(
        long id,
        UpdateUserRequest? request,
        UserRepository repository,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        var validationError = UserValidation.ValidateUpdate(request);
        if (validationError is not null)
        {
            loggerFactory.CreateLogger("Users").LogWarning("{event}: {reason}", "validation_failed", validationError);
            return BadRequest(validationError);
        }

        try
        {
            var user = await repository.UpdateAsync(
                id,
                UserValidation.Normalize(request!),
                cancellationToken);
            loggerFactory.CreateLogger("Users").Log(user is null ? LogLevel.Warning : LogLevel.Information, "{event}: user {user_id}", user is null ? "user_not_found" : "user_updated", id);
            return user is null
                ? NotFound(id)
                : Results.Ok(user);
        }
        catch (UserConflictException exception)
        {
            loggerFactory.CreateLogger("Users").LogWarning("{event}: user already exists", "user_conflict");
            return Results.Conflict(new ErrorResponse(409, exception.Message));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            loggerFactory.CreateLogger("Users").LogError(exception, "{event}: unable to update user {user_id}.", "database_error", id);
            return ServiceUnavailable();
        }
    }

    private static async Task<IResult> DeleteAsync(
        long id,
        UserRepository repository,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        try
        {
            var deleted = await repository.DeleteAsync(id, cancellationToken);
            loggerFactory.CreateLogger("Users").Log(deleted ? LogLevel.Information : LogLevel.Warning, "{event}: user {user_id}", deleted ? "user_deleted" : "user_not_found", id);
            return deleted
                ? Results.NoContent()
                : NotFound(id);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            loggerFactory.CreateLogger("Users").LogError(exception, "{event}: unable to delete user {user_id}.", "database_error", id);
            return ServiceUnavailable();
        }
    }

    private static IResult BadRequest(string message)
    {
        return Results.BadRequest(new ErrorResponse(400, message));
    }

    private static IResult NotFound(long id)
    {
        return Results.NotFound(new ErrorResponse(404, $"User {id} was not found."));
    }

    private static IResult ServiceUnavailable()
    {
        return Results.Json(
            new ErrorResponse(503, "Database is unavailable."),
            statusCode: StatusCodes.Status503ServiceUnavailable);
    }
}
