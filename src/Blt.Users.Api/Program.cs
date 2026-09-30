using System.Text.Json;
using System.Text.Json.Serialization;
using Blt.Users.Api.Data;
using Blt.Users.Api.Endpoints;
using Blt.Users.Api.Observability;
using Blt.Users.Api.Security;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using Prometheus;

var migrationMode = args.Any(static argument =>
    string.Equals(argument, "--migrate", StringComparison.OrdinalIgnoreCase));

// Do not pass the private migration switch to the ASP.NET configuration parser.
var hostArguments = args
    .Where(static argument =>
        !string.Equals(argument, "--migrate", StringComparison.OrdinalIgnoreCase))
    .ToArray();

var builder = WebApplication.CreateSlimBuilder(hostArguments);
builder.Logging.ClearProviders();
builder.Logging.AddConsole(options => options.FormatterName = "structured")
    .AddConsoleFormatter<StructuredLogFormatter, Microsoft.Extensions.Logging.Console.ConsoleFormatterOptions>();
builder.Logging.AddFilter("Microsoft.AspNetCore", LogLevel.Warning);

builder.WebHost.ConfigureKestrel(options =>
{
    options.AddServerHeader = false;
    options.Limits.MaxRequestBodySize = 16 * 1024;
    options.ListenAnyIP(8000);
});

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    options.SerializerOptions.DictionaryKeyPolicy = JsonNamingPolicy.CamelCase;
    options.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
});

builder.Services.AddProblemDetails();
builder.Services.AddSingleton(DatabaseOptions.FromConfiguration(builder.Configuration));
builder.Services.AddSingleton<DbConnectionFactory>();
builder.Services.AddSingleton<MigrationRunner>();
builder.Services.AddSingleton<DatabaseReadiness>();
builder.Services.AddSingleton<UserRepository>();
builder.Services.AddSingleton<AuthRepository>();
builder.Services.AddHttpClient<BillingAccounts>();
builder.Services.AddSingleton<PasswordService>();
builder.Services.AddOptions<AuthSessionOptions>().BindConfiguration("Session")
    .Validate(options => options.LifetimeMinutes is >= 1 and <= 1440, "Session lifetime must be between 1 and 1440 minutes.")
    .ValidateOnStart();
builder.Services.AddAuthentication(SessionAuthenticationHandler.SchemeName)
    .AddScheme<AuthenticationSchemeOptions, SessionAuthenticationHandler>(SessionAuthenticationHandler.SchemeName, _ => { });
builder.Services.AddAuthorization(options =>
{
    options.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build();
    options.AddPolicy(ProfileOwner.PolicyName, policy =>
        policy.RequireAuthenticatedUser().RequireAssertion(ProfileOwner.IsOwner));
});
builder.Services.AddRateLimiter(options =>
{
    options.AddFixedWindowLimiter("credentials", limiter =>
    {
        limiter.PermitLimit = 30;
        limiter.Window = TimeSpan.FromMinutes(1);
        limiter.QueueLimit = 0;
    });
    options.OnRejected = async (context, cancellationToken) =>
    {
        context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        context.HttpContext.Response.Headers.RetryAfter = "60";
        await context.HttpContext.Response.WriteAsJsonAsync(new ErrorResponse(429, "Too many authentication attempts."), cancellationToken);
    };
});
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<ApiMetrics>();

var app = builder.Build();

var configuredMigrationMode =
    builder.Configuration.GetValue<bool>("Database:RunMigrations") ||
    string.Equals(
        builder.Configuration["RUN_MIGRATIONS"],
        "true",
        StringComparison.OrdinalIgnoreCase);

if (migrationMode || configuredMigrationMode)
{
    try
    {
        await app.Services.GetRequiredService<MigrationRunner>().RunAsync();
    }
    catch (Exception exception)
    {
        app.Logger.LogError(exception, "{event}: migration Job failed", "migration_failed");
        Environment.ExitCode = 1;
    }
    await app.DisposeAsync();
    return;
}

app.UseRouting();
app.UseMiddleware<ApiMetricsMiddleware>();
app.UseExceptionHandler();
app.UseMiddleware<RequestLoggingMiddleware>();
app.Use(async (context, next) =>
{
    // Profile and token responses must not be retained by shared caches.
    context.Response.Headers.CacheControl = "no-store";
    context.Response.Headers.XContentTypeOptions = "nosniff";
    await next(context);
});
if (builder.Configuration.GetValue<bool>("Observability:EnableFaultInjection"))
{
    app.UseMiddleware<DemoFaultMiddleware>();
}
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

// A registry per application also isolates WebApplicationFactory test hosts.
app.MapMetrics("/metrics", app.Services.GetRequiredService<ApiMetrics>().Registry).AllowAnonymous();

// ASP.NET Core treats a trailing slash as optional, therefore each endpoint is
// deliberately mapped only once. Both /health and /health/ are accepted.
app.MapGet(
        "/health/",
        static () => TypedResults.Ok(new HealthResponse("OK")))
    .WithName("GetHealth").AllowAnonymous();

app.MapGet(
    "/ready/",
    async (DatabaseReadiness readiness, ILoggerFactory loggerFactory, CancellationToken cancellationToken) =>
    {
        try
        {
            await readiness.CheckAsync(cancellationToken);
            return Results.Ok(new HealthResponse("READY"));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            loggerFactory.CreateLogger("Readiness").LogError(exception, "{event}: database readiness failed", "database_error");
            return Results.Json(
                new ErrorResponse(503, "Database is not ready."),
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }
    }).AllowAnonymous();

AuthEndpoints.Map(app);
UserEndpoints.MapProfile(app);

// The canonical contract is /user. The aliases keep compatibility with the
// URL conventions used in the previous homework and common REST examples.
foreach (var prefix in new[]
         {
             "/user",
             "/v1/user",
             "/users",
             "/v1/users",
             "/api/v1/user",
             "/api/v1/users",
             "/otusapp/vyacheslav/user",
             "/otusapp/vyacheslav/v1/user"
         })
{
    UserEndpoints.Map(app, prefix);
}

app.Lifetime.ApplicationStarted.Register(() =>
    app.Logger.LogInformation("{event}: application started", "application_started"));
app.Lifetime.ApplicationStopping.Register(() =>
    app.Logger.LogInformation("{event}: application stopping", "application_stopping"));
app.Lifetime.ApplicationStopped.Register(() =>
    app.Logger.LogInformation("{event}: application stopped", "application_stopped"));
try { app.Run(); }
catch (Exception exception)
{
    app.Logger.LogError(exception, "{event}: application terminated unexpectedly", "application_failed");
    throw;
}

public sealed record HealthResponse(
    [property: JsonPropertyName("status")] string Status);

public sealed record ErrorResponse(int Code, string Message);

// Required by WebApplicationFactory in integration tests.
public partial class Program;
