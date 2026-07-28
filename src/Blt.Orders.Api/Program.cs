using System.Text.Json.Serialization;

var builder = WebApplication.CreateSlimBuilder(args);

builder.WebHost.ConfigureKestrel(options =>
{
    options.AddServerHeader = false;
    options.ListenAnyIP(8000);
});

var app = builder.Build();

app.MapGet(
        "/health/",
        static () => TypedResults.Ok(new HealthResponse("OK")))
    .WithName("GetHealth")
    .WithSummary("Returns the service health status.");

app.Run();

public sealed record HealthResponse(
    [property: JsonPropertyName("status")] string Status);

// Required by WebApplicationFactory in integration tests.
public partial class Program;

