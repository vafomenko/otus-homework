using System.Security.Claims;
using Blt.Streaming;

namespace Blt.Orders.Api;

public partial class Program
{
    public static async Task Main(string[] args)
    {
        var builder = ServiceHost.Create(args);
        builder.Services.AddSingleton<OrderRepository>();
        builder.Services.AddHostedService<OutboxPublisher>();
        builder.Services.AddSingleton<ISagaParticipants, HttpSagaParticipants>();
        builder.Services.AddSingleton<SagaCoordinator>();
        builder.Services.AddHostedService<SagaRecovery>();
        foreach (var (service, setting) in new[] { ("billing", "Billing"), ("warehouse", "Warehouse"), ("delivery", "Delivery") })
        {
            builder.Services.AddHttpClient(service, client =>
            {
                client.BaseAddress = new Uri(builder.Configuration[setting + ":BaseUrl"] ?? $"http://{service}:8000");
                client.Timeout = TimeSpan.FromSeconds(3);
                client.DefaultRequestHeaders.Add("X-Service-Key", builder.Configuration["Internal:ApiKey"]);
            });
        }
        var app = builder.Build();
        if (!await ServiceHost.PrepareAsync(app, args, typeof(Program).Assembly)) return;
        app.MapPost("/orders", async (OrderRequest request, ClaimsPrincipal user, HttpContext context, OrderRepository db, SagaCoordinator saga, CancellationToken ct) =>
        {
            if (!Money.IsValid(request.Price) || string.IsNullOrWhiteSpace(request.Sku) || request.Sku.Length > 120 ||
                request.Quantity is < 1 or > 10000 || string.IsNullOrWhiteSpace(request.SlotId) || request.SlotId.Length > 120)
                return Results.BadRequest(new { error = "Valid price in kopecks, sku, quantity (1..10000) and slotId are required." });
            if (!Guid.TryParse(context.Request.Headers["Idempotency-Key"], out var key) || key == Guid.Empty)
                return Results.BadRequest(new { error = "Idempotency-Key must be a nonempty UUID." });
            try
            {
                var (order, created) = await db.CreateSagaAsync(CurrentUser.Id(user), CurrentUser.Email(user), key, request, ct);
                if (created) order = await saga.RunAsync(order.Id, app.Lifetime.ApplicationStopping);
                context.Response.Headers["Idempotency-Replayed"] = created ? "false" : "true";
                context.Response.Headers.Location = $"/orders/{order.Id}";
                return Results.Json(order, statusCode: order.Status is "processing" or "compensating" ? 202 : created ? 201 : 200);
            }
            catch (OrderConflictException) { return Results.Conflict(new { error = "Idempotency-Key was already used with different order parameters." }); }
        }).RequireAuthorization();
        app.MapGet("/orders/{id:guid}", async (Guid id, ClaimsPrincipal user, OrderRepository db, CancellationToken ct) =>
            await db.GetAsync(id, CurrentUser.Id(user), ct) is { } order ? Results.Ok(order) : Results.NotFound()).RequireAuthorization();
        await app.RunAsync();
    }
}
