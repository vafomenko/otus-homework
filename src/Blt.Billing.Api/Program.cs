using System.Security.Claims;
using Blt.Streaming;

namespace Blt.Billing.Api;

public partial class Program
{
    public static async Task Main(string[] args)
    {
        var builder = ServiceHost.Create(args);
        builder.Services.AddSingleton<BillingRepository>();
        var app = builder.Build();
        if (!await ServiceHost.PrepareAsync(app, args, typeof(Program).Assembly)) return;
        app.MapGet("/billing/account", async (ClaimsPrincipal user, BillingRepository db, CancellationToken ct) =>
            await db.GetAsync(CurrentUser.Id(user), ct) is { } account ? Results.Ok(account) : Results.NotFound()).RequireAuthorization();
        app.MapPost("/billing/deposits", (MoneyRequest request, ClaimsPrincipal user, BillingRepository db, CancellationToken ct) =>
            Apply(db, CurrentUser.Id(user), request.OperationId, request.Amount, "deposit", ct)).RequireAuthorization();
        app.MapPost("/billing/withdrawals", (MoneyRequest request, ClaimsPrincipal user, BillingRepository db, CancellationToken ct) =>
            Apply(db, CurrentUser.Id(user), request.OperationId, request.Amount, "withdrawal", ct)).RequireAuthorization();
        var internalApi = app.MapGroup("/internal").AddEndpointFilter<InternalAccess>();
        internalApi.MapPost("/accounts", async (AccountRequest request, BillingRepository db, CancellationToken ct) =>
        {
            if (request.UserId <= 0) return Results.BadRequest();
            await db.CreateAccountAsync(request.UserId, ct);
            return Results.Ok(await db.GetAsync(request.UserId, ct));
        });
        internalApi.MapPost("/charges", (ChargeRequest request, BillingRepository db, CancellationToken ct) =>
            Apply(db, request.UserId, request.OrderId, request.Amount, "charge", ct));
        internalApi.MapPost("/refunds", async (ChargeRequest request, BillingRepository db, CancellationToken ct) =>
        {
            if (!Money.IsValid(request.Amount) || request.OrderId == Guid.Empty || request.UserId <= 0) return Results.BadRequest();
            try
            {
                var payment = await db.RefundAsync(request, ct);
                return payment is null ? Results.NotFound() : Results.Ok(payment);
            }
            catch (OperationConflictException) { return Results.Conflict(); }
        });
        app.MapGet("/billing/payments/{id:guid}", async (Guid id, ClaimsPrincipal user, BillingRepository db, CancellationToken ct) =>
            await db.PaymentAsync(CurrentUser.Id(user), id, ct) is { } payment ? Results.Ok(payment) : Results.NotFound()).RequireAuthorization();
        await app.RunAsync();
    }

    private static async Task<IResult> Apply(BillingRepository db, long userId, Guid id, long amount, string kind, CancellationToken ct)
    {
        if (!Money.IsValid(amount) || id == Guid.Empty) return Results.BadRequest(new { error = "Positive amount in kopecks and nonempty operation ID are required." });
        try
        {
            var operation = await db.ApplyAsync(userId, id, amount, kind, ct);
            return operation is null ? Results.NotFound() : Results.Ok(operation);
        }
        catch (OperationConflictException) { return Results.Conflict(new { error = "Operation ID was already used with different parameters." }); }
    }
}
