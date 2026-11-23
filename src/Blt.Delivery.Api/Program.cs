using System.Security.Claims;
using Blt.Streaming;

namespace Blt.Delivery.Api;

public partial class Program
{
    public static async Task Main(string[] args)
    {
        var builder = ServiceHost.Create(args);
        builder.Services.AddSingleton(sp => new ReservationRepository(sp.GetRequiredService<Database>(), "courier_unavailable"));
        var app = builder.Build();
        if (!await ServiceHost.PrepareAsync(app, args, typeof(Program).Assembly)) return;
        app.MapGet("/delivery/slots", async (ReservationRepository db, CancellationToken ct) => Results.Ok(await db.ListAsync(ct))).RequireAuthorization();
        app.MapGet("/delivery/reservations/{id:guid}", async (Guid id, ClaimsPrincipal user, ReservationRepository db, CancellationToken ct) =>
            await db.GetAsync(id, CurrentUser.Id(user), ct) is { } reservation ? Results.Ok(reservation) : Results.NotFound()).RequireAuthorization();
        app.MapReservationCommands(singleUnit: true);
        await app.RunAsync();
    }
}
