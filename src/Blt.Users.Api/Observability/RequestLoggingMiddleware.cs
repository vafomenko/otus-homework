using Npgsql;

namespace Blt.Users.Api.Observability;

public sealed class RequestLoggingMiddleware(RequestDelegate next, ILogger<RequestLoggingMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        using var scope = logger.BeginScope(new Dictionary<string, object?>
        {
            ["request_id"] = context.TraceIdentifier,
            ["method"] = context.Request.Method,
            ["path"] = context.Request.Path.Value,
            ["ip"] = context.Connection.RemoteIpAddress?.ToString()
        });
        logger.LogInformation("{event}: incoming HTTP request", "http_request");
        try
        {
            await next(context);
        }
        catch (BadHttpRequestException exception)
        {
            context.Response.StatusCode = exception.StatusCode;
            await context.Response.WriteAsJsonAsync(new ErrorResponse(exception.StatusCode, "Invalid request."));
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            throw;
        }
        catch (NpgsqlException exception)
        {
            logger.LogError(exception, "{event}: database request failed", "database_error");
            context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            await context.Response.WriteAsJsonAsync(new ErrorResponse(503, "Database is unavailable."));
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "{event}: unhandled application error", "application_error");
            throw;
        }
        if (context.Response.StatusCode is 400 or 415 or 422)
            logger.LogWarning("{event}: request rejected ({status_code})", "validation_failed", context.Response.StatusCode);
        else if (context.Response.StatusCode is 401 or 403)
            logger.LogWarning("{event}: access denied ({status_code})", "access_denied", context.Response.StatusCode);
        else if (context.Response.StatusCode == 404)
            logger.LogWarning("{event}: resource not found", "resource_not_found");
        else if (context.Response.StatusCode >= 500)
            logger.LogError("{event}: request failed ({status_code})", "http_error", context.Response.StatusCode);
        logger.LogInformation("{event}: HTTP response {status_code}", "http_response", context.Response.StatusCode);
    }
}
