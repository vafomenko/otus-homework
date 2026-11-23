using Npgsql;

namespace Blt.Streaming;

public sealed class ReservationConflictException : Exception;

public sealed class ReservationRepository(Database db, string unavailableReason)
{
    public async Task<List<Resource>> ListAsync(CancellationToken ct = default)
    {
        await using var connection = await db.OpenAsync(ct);
        await using var command = new NpgsqlCommand("SELECT id, available, courier_id, starts_at, ends_at FROM resources ORDER BY id", connection);
        await using var reader = await command.ExecuteReaderAsync(ct);
        var result = new List<Resource>();
        while (await reader.ReadAsync(ct))
            result.Add(new Resource(reader.GetString(0), reader.GetInt32(1), reader.IsDBNull(2) ? null : reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetFieldValue<DateTimeOffset>(3),
                reader.IsDBNull(4) ? null : reader.GetFieldValue<DateTimeOffset>(4)));
        return result;
    }

    public async Task<Reservation?> GetAsync(Guid orderId, long userId, CancellationToken ct = default)
    {
        await using var connection = await db.OpenAsync(ct);
        await using var command = new NpgsqlCommand("""
            SELECT resource_id, quantity, status, reason, courier_id FROM reservations
            WHERE order_id=@id AND user_id=@user
            """, connection);
        command.Parameters.AddWithValue("id", orderId);
        command.Parameters.AddWithValue("user", userId);
        await using var reader = await command.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? Read(reader, orderId, userId) : null;
    }

    public Task<Reservation> ReserveAsync(ReservationRequest request, CancellationToken ct = default) => ApplyAsync(request, false, ct);
    public Task<Reservation> ReleaseAsync(ReservationRequest request, CancellationToken ct = default) => ApplyAsync(request, true, ct);

    private async Task<Reservation> ApplyAsync(ReservationRequest request, bool release, CancellationToken ct)
    {
        if (request.OrderId == Guid.Empty || request.UserId <= 0 || string.IsNullOrWhiteSpace(request.ResourceId) ||
            request.ResourceId.Length > 120 || request.Quantity is < 1 or > 10000)
            throw new ArgumentException("Invalid reservation.");
        await using var connection = await db.OpenAsync(ct);
        await using var tx = await connection.BeginTransactionAsync(ct);
        await using var command = new NpgsqlCommand("SELECT pg_advisory_xact_lock(hashtextextended(@id::text, 9))", connection, tx);
        command.Parameters.AddWithValue("id", request.OrderId);
        await command.ExecuteNonQueryAsync(ct);
        command.CommandText = "SELECT resource_id, quantity, status, reason, courier_id, user_id FROM reservations WHERE order_id=@id";
        Reservation? previous = null;
        await using (var reader = await command.ExecuteReaderAsync(ct))
        {
            if (await reader.ReadAsync(ct))
            {
                previous = Read(reader, request.OrderId, reader.GetInt64(5));
                if (previous.UserId != request.UserId || previous.ResourceId != request.ResourceId || previous.Quantity != request.Quantity)
                    throw new ReservationConflictException();
            }
        }
        if (previous is not null && (!release || previous.Status == "released")) return previous;

        command.Parameters.AddWithValue("resource", request.ResourceId);
        command.Parameters.AddWithValue("quantity", request.Quantity);
        string? courierId = previous?.CourierId;
        var reserved = false;
        if (!release)
        {
            command.CommandText = """
                UPDATE resources SET available=available-@quantity
                WHERE id=@resource AND available>=@quantity RETURNING courier_id
                """;
            await using var reader = await command.ExecuteReaderAsync(ct);
            reserved = await reader.ReadAsync(ct);
            if (reserved && !reader.IsDBNull(0)) courierId = reader.GetString(0);
        }
        else if (previous?.Status == "reserved")
        {
            command.CommandText = "UPDATE resources SET available=available+@quantity WHERE id=@resource";
            await command.ExecuteNonQueryAsync(ct);
        }
        var result = new Reservation(request.OrderId, request.UserId, request.ResourceId, request.Quantity,
            release ? "released" : reserved ? "reserved" : "rejected", release ? "saga_compensated" : reserved ? null : unavailableReason, courierId);
        command.CommandText = """
            INSERT INTO reservations(order_id, user_id, resource_id, quantity, status, reason, courier_id)
            VALUES (@id, @user, @resource, @quantity, @status, @reason, @courier)
            ON CONFLICT (order_id) DO UPDATE SET status=EXCLUDED.status, reason=EXCLUDED.reason
            """;
        command.Parameters.AddWithValue("user", request.UserId);
        command.Parameters.AddWithValue("status", result.Status);
        command.Parameters.AddWithValue("reason", NpgsqlTypes.NpgsqlDbType.Text, (object?)result.Reason ?? DBNull.Value);
        command.Parameters.AddWithValue("courier", NpgsqlTypes.NpgsqlDbType.Text, (object?)courierId ?? DBNull.Value);
        await command.ExecuteNonQueryAsync(ct);
        await tx.CommitAsync(ct);
        return result;
    }

    private static Reservation Read(NpgsqlDataReader reader, Guid orderId, long userId) =>
        new(orderId, userId, reader.GetString(0), reader.GetInt32(1), reader.GetString(2),
            reader.IsDBNull(3) ? null : reader.GetString(3), reader.IsDBNull(4) ? null : reader.GetString(4));
}
