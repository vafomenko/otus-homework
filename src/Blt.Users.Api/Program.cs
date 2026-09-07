using System.Text.Json;
using System.Text.Json.Serialization;
using Blt.Users.Api.Data;
using Blt.Users.Api.Endpoints;
using Blt.Users.Api.Observability;
using Prometheus;

var migrationMode = args.Any(static argument =>
    string.Equals(argument, "--migrate", StringComparison.OrdinalIgnoreCase));

// Do not pass the private migration switch to the ASP.NET configuration parser.
var hostArguments = args
    .Where(static argument =>
        !string.Equals(argument, "--migrate", StringComparison.OrdinalIgnoreCase))
    .ToArray();

var builder = WebApplication.CreateSlimBuilder(hostArguments);

builder.WebHost.ConfigureKestrel(options =>
{
    options.AddServerHeader = false;
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
    await app.Services.GetRequiredService<MigrationRunner>().RunAsync();
    return;
}

app.UseRouting();
app.UseMiddleware<ApiMetricsMiddleware>();
app.UseExceptionHandler();
if (builder.Configuration.GetValue<bool>("Observability:EnableFaultInjection"))
{
    app.UseMiddleware<DemoFaultMiddleware>();
}

// A registry per application also isolates WebApplicationFactory test hosts.
app.MapMetrics("/metrics", app.Services.GetRequiredService<ApiMetrics>().Registry);

// ASP.NET Core treats a trailing slash as optional, therefore each endpoint is
// deliberately mapped only once. Both /health and /health/ are accepted.
app.MapGet(
        "/health/",
        static () => TypedResults.Ok(new HealthResponse("OK")))
    .WithName("GetHealth");

app.MapGet(
    "/ready/",
    async (DatabaseReadiness readiness, CancellationToken cancellationToken) =>
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
        catch
        {
            return Results.Json(
                new ErrorResponse(503, "Database is not ready."),
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }
    });

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

app.Run();

public sealed record HealthResponse(
    [property: JsonPropertyName("status")] string Status);

public sealed record ErrorResponse(int Code, string Message);

// Required by WebApplicationFactory in integration tests.
public partial class Program;
