using Blt.Users.Api.Data;
using Blt.Users.Api.Models;
using Blt.Users.Api.Observability;

namespace Blt.Users.Api.Endpoints;

public static class UserEndpoints
{
    public static void Map(WebApplication app, string prefix)
    {
        // Aliases and concrete IDs share one bounded set of metric labels.
        app.MapGet(prefix, ListAsync).WithMetadata(new ApiMetricRoute("/user"));
        app.MapGet($"{prefix}/{{id:long}}", GetAsync).WithMetadata(new ApiMetricRoute("/user/{id}"));
        app.MapPost(prefix, CreateAsync).WithMetadata(new ApiMetricRoute("/user"));
        app.MapPut($"{prefix}/{{id:long}}", UpdateAsync).WithMetadata(new ApiMetricRoute("/user/{id}"));
        app.MapDelete($"{prefix}/{{id:long}}", DeleteAsync).WithMetadata(new ApiMetricRoute("/user/{id}"));
    }

    private static async Task<IResult> ListAsync(
        UserRepository repository,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        try
        {
            var users = await repository.ListAsync(cancellationToken);
            loggerFactory.CreateLogger("Users").LogInformation("{event}: listed users", "users_listed");
            return Results.Ok(users);
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

    private static async Task<IResult> CreateAsync(
        CreateUserRequest? request,
        UserRepository repository,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        var validationError = UserValidation.ValidateCreate(request);
        if (validationError is not null)
        {
            loggerFactory.CreateLogger("Users").LogWarning("{event}: {reason}", "validation_failed", validationError);
            return BadRequest(validationError);
        }

        try
        {
            var user = await repository.CreateAsync(
                UserValidation.Normalize(request!),
                cancellationToken);
            loggerFactory.CreateLogger("Users").LogInformation("{event}: created user {user_id}", "user_created", user.Id);
            return Results.Created($"/user/{user.Id}", user);
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
            loggerFactory.CreateLogger("Users").LogError(exception, "{event}: unable to create user.", "database_error");
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
