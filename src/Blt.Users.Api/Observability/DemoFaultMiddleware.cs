namespace Blt.Users.Api.Observability;

// Registered only by an explicit opt-in on the isolated homework deployment.
public sealed class DemoFaultMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        if (context.GetEndpoint()?.Metadata.GetMetadata<ApiMetricRoute>() is not null)
        {
            if (int.TryParse(context.Request.Headers["X-Demo-Delay-Ms"], out var delay))
                await Task.Delay(Math.Clamp(delay, 0, 2000), context.RequestAborted);

            if (context.Request.Headers["X-Demo-Error"] == "500")
            {
                context.Response.StatusCode = StatusCodes.Status500InternalServerError;
                await context.Response.WriteAsJsonAsync(new ErrorResponse(500, "Injected homework failure."));
                return;
            }
        }

        await next(context);
    }
}
