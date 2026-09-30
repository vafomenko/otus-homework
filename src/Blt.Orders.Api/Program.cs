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
        builder.Services.AddHttpClient("billing", client =>
        {
            client.BaseAddress = new Uri(builder.Configuration["Billing:BaseUrl"] ?? "http://billing:8000");
            client.DefaultRequestHeaders.Add("X-Service-Key", builder.Configuration["Internal:ApiKey"]);
        });
        var app = builder.Build();
        if (!await ServiceHost.PrepareAsync(app, args, typeof(Program).Assembly)) return;
        app.MapPost("/orders", async (OrderRequest request, ClaimsPrincipal user, OrderRepository db, IHttpClientFactory clients, CancellationToken ct) =>
        {
            if (!Money.IsValid(request.Price)) return Results.BadRequest(new { error = "Price must be positive integer kopecks, at most 1000000000000." });
            var order = await db.CreateAsync(CurrentUser.Id(user), request.Price, ct);
            using var response = await clients.CreateClient("billing").PostAsJsonAsync("/internal/charges",
                new ChargeRequest(order.Id, order.UserId, order.Price), ct);
            response.EnsureSuccessStatusCode();
            var payment = await response.Content.ReadFromJsonAsync<Operation>(ct) ?? throw new InvalidOperationException("Empty billing response.");
            order = order with { Status = payment.Status == "succeeded" ? "paid" : "rejected", Reason = payment.Reason };
            await db.CompleteAsync(order, CurrentUser.Email(user), ct);
            return Results.Created($"/orders/{order.Id}", order);
        }).RequireAuthorization();
        app.MapGet("/orders/{id:guid}", async (Guid id, ClaimsPrincipal user, OrderRepository db, CancellationToken ct) =>
            await db.GetAsync(id, CurrentUser.Id(user), ct) is { } order ? Results.Ok(order) : Results.NotFound()).RequireAuthorization();
        await app.RunAsync();
    }
}
