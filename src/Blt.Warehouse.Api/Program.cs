using System.Security.Claims;
using Blt.Streaming;

namespace Blt.Warehouse.Api;

public partial class Program
{
    public static async Task Main(string[] args)
    {
        var builder = ServiceHost.Create(args);
        builder.Services.AddSingleton(sp => new ReservationRepository(sp.GetRequiredService<Database>(), "out_of_stock"));
        var app = builder.Build();
        if (!await ServiceHost.PrepareAsync(app, args, typeof(Program).Assembly)) return;
        app.MapGet("/warehouse/products", async (ReservationRepository db, CancellationToken ct) =>
            Results.Ok((await db.ListAsync(ct)).Select(r => new { sku = r.Id, r.Available }))).RequireAuthorization();
        app.MapGet("/warehouse/reservations/{id:guid}", async (Guid id, ClaimsPrincipal user, ReservationRepository db, CancellationToken ct) =>
            await db.GetAsync(id, CurrentUser.Id(user), ct) is { } reservation ? Results.Ok(reservation) : Results.NotFound()).RequireAuthorization();
        app.MapReservationCommands();
        await app.RunAsync();
    }
}
