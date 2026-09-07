using System.Diagnostics;

namespace Blt.Users.Api.Observability;

public sealed class ApiMetricsMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, ApiMetrics metrics)
    {
        var route = context.GetEndpoint()?.Metadata.GetMetadata<ApiMetricRoute>();
        if (route is null)
        {
            await next(context);
            return;
        }

        var started = Stopwatch.GetTimestamp();
        try
        {
            await next(context);
        }
        finally
        {
            // Includes fault injection, exception handling and response serialization.
            // Probes, /metrics and arbitrary unknown paths do not enter API statistics.
            metrics.Observe(context.Request.Method, route.Template, context.Response.StatusCode,
                Stopwatch.GetElapsedTime(started).TotalSeconds);
        }
    }
}
