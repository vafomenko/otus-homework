using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace Blt.Streaming;

public static class ReservationEndpoints
{
    public static void MapReservationCommands(this WebApplication app, bool singleUnit = false)
    {
        var api = app.MapGroup("/internal").AddEndpointFilter<InternalAccess>();
        api.MapPost("/reservations", (ReservationRequest request, ReservationRepository db, CancellationToken ct) => Apply(request, db, false, singleUnit, ct));
        api.MapPost("/releases", (ReservationRequest request, ReservationRepository db, CancellationToken ct) => Apply(request, db, true, singleUnit, ct));
    }

    private static async Task<IResult> Apply(ReservationRequest request, ReservationRepository db, bool release, bool singleUnit, CancellationToken ct)
    {
        try
        {
            if (singleUnit && request.Quantity != 1) return Results.BadRequest();
            return Results.Ok(release ? await db.ReleaseAsync(request, ct) : await db.ReserveAsync(request, ct));
        }
        catch (ArgumentException) { return Results.BadRequest(); }
        catch (ReservationConflictException) { return Results.Conflict(new { error = "Order ID was already used with different reservation parameters." }); }
    }
}
