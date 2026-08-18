using Blt.Users.Api.Data;
using Blt.Users.Api.Models;

namespace Blt.Users.Api.Endpoints;

public static class UserEndpoints
{
    public static void Map(WebApplication app, string prefix)
    {
        app.MapGet(prefix, ListAsync);
        app.MapGet($"{prefix}/{{id:long}}", GetAsync);
        app.MapPost(prefix, CreateAsync);
        app.MapPut($"{prefix}/{{id:long}}", UpdateAsync);
        app.MapDelete($"{prefix}/{{id:long}}", DeleteAsync);
    }

    private static async Task<IResult> ListAsync(
        UserRepository repository,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        try
        {
            return Results.Ok(await repository.ListAsync(cancellationToken));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            loggerFactory.CreateLogger("Users").LogError(exception, "Unable to list users.");
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
            loggerFactory.CreateLogger("Users").LogError(exception, "Unable to get user {UserId}.", id);
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
            return BadRequest(validationError);
        }

        try
        {
            var user = await repository.CreateAsync(
                UserValidation.Normalize(request!),
                cancellationToken);
            return Results.Created($"/user/{user.Id}", user);
        }
        catch (UserConflictException exception)
        {
            return Results.Conflict(new ErrorResponse(409, exception.Message));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            loggerFactory.CreateLogger("Users").LogError(exception, "Unable to create user.");
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
            return BadRequest(validationError);
        }

        try
        {
            var user = await repository.UpdateAsync(
                id,
                UserValidation.Normalize(request!),
                cancellationToken);
            return user is null
                ? NotFound(id)
                : Results.Ok(user);
        }
        catch (UserConflictException exception)
        {
            return Results.Conflict(new ErrorResponse(409, exception.Message));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            loggerFactory.CreateLogger("Users").LogError(exception, "Unable to update user {UserId}.", id);
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
            return await repository.DeleteAsync(id, cancellationToken)
                ? Results.NoContent()
                : NotFound(id);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            loggerFactory.CreateLogger("Users").LogError(exception, "Unable to delete user {UserId}.", id);
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
