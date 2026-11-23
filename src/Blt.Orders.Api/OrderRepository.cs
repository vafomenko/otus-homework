using System.Text.Json;
using Blt.Streaming;
using Npgsql;

namespace Blt.Orders.Api;

public sealed class OrderConflictException : Exception;
public sealed record SagaOrder(Order Order, string Email, DateTimeOffset DeadlineAt, long RecoveryAttempts,
    int RetryCount, DateTimeOffset? LastAttemptAt, DateTimeOffset NextAttemptAt, string? LastError);

public sealed class OrderRepository(Database db)
{
    private const string Columns = "id, user_id, price, status, reason, created_at, sku, quantity, slot_id, courier_id, step";

    public async Task<(Order Order, bool Created)> CreateSagaAsync(long userId, string email, Guid key, OrderRequest request, CancellationToken ct = default)
    {
        await using var connection = await db.OpenAsync(ct);
        await using var command = new NpgsqlCommand("""
            INSERT INTO orders(id, user_id, price, status, sku, quantity, slot_id, email, idempotency_key, step)
            VALUES (@id, @user, @price, 'processing', @sku, @quantity, @slot, @email, @key, 'payment')
            ON CONFLICT (user_id, idempotency_key) DO NOTHING RETURNING id
            """, connection);
        command.Parameters.AddWithValue("id", Guid.NewGuid());
        command.Parameters.AddWithValue("user", userId);
        command.Parameters.AddWithValue("price", request.Price);
        command.Parameters.AddWithValue("sku", request.Sku);
        command.Parameters.AddWithValue("quantity", request.Quantity);
        command.Parameters.AddWithValue("slot", request.SlotId);
        command.Parameters.AddWithValue("email", email);
        command.Parameters.AddWithValue("key", key);
        var created = await command.ExecuteScalarAsync(ct) is Guid;
        command.CommandText = $"SELECT {Columns} FROM orders WHERE user_id=@user AND idempotency_key=@key";
        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) throw new InvalidOperationException("Order insert disappeared.");
        var order = Read(reader);
        if (order.Price != request.Price || order.Sku != request.Sku || order.Quantity != request.Quantity || order.SlotId != request.SlotId)
            throw new OrderConflictException();
        return (order, created);
    }

    public async Task<SagaOrder?> GetSagaAsync(Guid id, CancellationToken ct = default)
    {
        await using var connection = await db.OpenAsync(ct);
        await using var command = new NpgsqlCommand($"SELECT {Columns}, email, deadline_at, recovery_attempts, retry_count, last_attempt_at, next_attempt_at, last_error FROM orders WHERE id=@id", connection);
        command.Parameters.AddWithValue("id", id);
        await using var reader = await command.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? new SagaOrder(Read(reader), reader.IsDBNull(11) ? "" : reader.GetString(11),
            reader.GetFieldValue<DateTimeOffset>(12), reader.GetInt64(13), reader.GetInt32(14),
            reader.IsDBNull(15) ? null : reader.GetFieldValue<DateTimeOffset>(15), reader.GetFieldValue<DateTimeOffset>(16),
            reader.IsDBNull(17) ? null : reader.GetString(17)) : null;
    }

    public async Task<List<Guid>> PendingAsync(CancellationToken ct = default)
    {
        await using var connection = await db.OpenAsync(ct);
        await using var command = new NpgsqlCommand("""
            SELECT id FROM orders WHERE status IN ('processing', 'compensating') AND step<>'legacy' AND next_attempt_at<=NOW()
            ORDER BY next_attempt_at, id LIMIT 100
            """, connection);
        await using var reader = await command.ExecuteReaderAsync(ct);
        var result = new List<Guid>();
        while (await reader.ReadAsync(ct)) result.Add(reader.GetGuid(0));
        return result;
    }

    public async Task BeginAttemptAsync(Guid id, CancellationToken ct)
    {
        await using var connection = await db.OpenAsync(ct);
        await using var command = new NpgsqlCommand("""
            UPDATE orders SET recovery_attempts=recovery_attempts+1, last_attempt_at=NOW(),
              next_attempt_at=NOW()+INTERVAL '1 second'
            WHERE id=@id AND status IN ('processing', 'compensating') AND step<>'legacy'
            """, connection);
        command.Parameters.AddWithValue("id", id);
        await command.ExecuteNonQueryAsync(ct);
    }

    public async Task DeferAsync(Guid id, string error, CancellationToken ct = default)
    {
        await using var connection = await db.OpenAsync(ct);
        await using var command = new NpgsqlCommand("""
            UPDATE orders SET retry_count=LEAST(retry_count+1, 32), last_error=@error, updated_at=NOW(),
              next_attempt_at=NOW()+INTERVAL '1 second'*LEAST(30, (1 << LEAST(retry_count, 5)))
            WHERE id=@id AND status IN ('processing', 'compensating') AND step<>'legacy'
            """, connection);
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("error", error);
        await command.ExecuteNonQueryAsync(ct);
    }

    public async Task AdvanceAsync(Order order, string step, string? reason, CancellationToken ct)
    {
        await using var connection = await db.OpenAsync(ct);
        await using var command = new NpgsqlCommand("""
            UPDATE orders SET step=@step, status=@status, reason=COALESCE(@reason, reason), updated_at=NOW(),
              retry_count=0, last_error=NULL, next_attempt_at=NOW()
            WHERE id=@id AND step=@previous AND status IN ('processing', 'compensating')
            """, connection);
        command.Parameters.AddWithValue("id", order.Id);
        command.Parameters.AddWithValue("previous", order.Step);
        command.Parameters.AddWithValue("step", step);
        command.Parameters.AddWithValue("status", step.StartsWith("undo_", StringComparison.Ordinal) ? "compensating" : "processing");
        command.Parameters.AddWithValue("reason", NpgsqlTypes.NpgsqlDbType.Text, (object?)reason ?? DBNull.Value);
        if (await command.ExecuteNonQueryAsync(ct) != 1) throw new InvalidOperationException("Saga state changed concurrently.");
    }

    public async Task FinishSagaAsync(Order order, string email, bool success, string? courierId, CancellationToken ct)
    {
        var result = order with { Status = success ? "paid" : "rejected", Step = "done", CourierId = courierId };
        var message = OrderEvent.Create(result, email);
        await using var connection = await db.OpenAsync(ct);
        await using var tx = await connection.BeginTransactionAsync(ct);
        await using var command = new NpgsqlCommand("""
            UPDATE orders SET status=@status, step='done', courier_id=@courier, updated_at=NOW(),
              retry_count=0, last_error=NULL, next_attempt_at=NOW()
            WHERE id=@id AND step=@previous AND status IN ('processing', 'compensating')
            """, connection, tx);
        command.Parameters.AddWithValue("id", order.Id);
        command.Parameters.AddWithValue("status", result.Status);
        command.Parameters.AddWithValue("previous", order.Step);
        command.Parameters.AddWithValue("courier", NpgsqlTypes.NpgsqlDbType.Text, (object?)courierId ?? DBNull.Value);
        if (await command.ExecuteNonQueryAsync(ct) != 1) throw new InvalidOperationException("Saga is already completed or changed.");
        command.CommandText = "INSERT INTO outbox(event_id, order_id, payload) VALUES (@event, @id, @payload)";
        command.Parameters.AddWithValue("event", message.Id);
        command.Parameters.AddWithValue("payload", NpgsqlTypes.NpgsqlDbType.Jsonb, JsonSerializer.Serialize(message, KafkaSettings.Json));
        await command.ExecuteNonQueryAsync(ct);
        await tx.CommitAsync(ct);
    }

    public async Task<Order> CreateAsync(long userId, long price, CancellationToken ct)
    {
        var order = new Order(Guid.NewGuid(), userId, price, "processing", null, DateTimeOffset.UtcNow);
        await using var connection = await db.OpenAsync(ct);
        await using var command = new NpgsqlCommand("INSERT INTO orders(id, user_id, price, status, created_at) VALUES (@id, @user, @price, 'processing', @time)", connection);
        command.Parameters.AddWithValue("id", order.Id);
        command.Parameters.AddWithValue("user", userId);
        command.Parameters.AddWithValue("price", price);
        command.Parameters.AddWithValue("time", order.CreatedAt);
        await command.ExecuteNonQueryAsync(ct);
        return order;
    }

    public async Task CompleteAsync(Order order, string email, CancellationToken ct)
    {
        var message = OrderEvent.Create(order, email);
        await using var connection = await db.OpenAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        await using var command = new NpgsqlCommand("UPDATE orders SET status=@status, reason=@reason WHERE id=@id AND status='processing'", connection, transaction);
        command.Parameters.AddWithValue("id", order.Id);
        command.Parameters.AddWithValue("status", order.Status);
        command.Parameters.AddWithValue("reason", NpgsqlTypes.NpgsqlDbType.Text, (object?)order.Reason ?? DBNull.Value);
        if (await command.ExecuteNonQueryAsync(ct) != 1) throw new InvalidOperationException("Order is already completed or missing.");
        command.CommandText = "INSERT INTO outbox(event_id, order_id, payload) VALUES (@event, @id, @payload)";
        command.Parameters.AddWithValue("event", message.Id);
        command.Parameters.AddWithValue("payload", NpgsqlTypes.NpgsqlDbType.Jsonb, JsonSerializer.Serialize(message, KafkaSettings.Json));
        await command.ExecuteNonQueryAsync(ct);
        await transaction.CommitAsync(ct);
    }

    public async Task<Order?> GetAsync(Guid id, long userId, CancellationToken ct)
    {
        await using var connection = await db.OpenAsync(ct);
        await using var command = new NpgsqlCommand($"SELECT {Columns} FROM orders WHERE id=@id AND user_id=@user", connection);
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("user", userId);
        await using var reader = await command.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? Read(reader) : null;
    }

    private static Order Read(NpgsqlDataReader reader) => new(reader.GetGuid(0), reader.GetInt64(1), reader.GetInt64(2),
        reader.GetString(3), reader.IsDBNull(4) ? null : reader.GetString(4), reader.GetFieldValue<DateTimeOffset>(5),
        reader.IsDBNull(6) ? null : reader.GetString(6), reader.IsDBNull(7) ? 0 : reader.GetInt32(7),
        reader.IsDBNull(8) ? null : reader.GetString(8), reader.IsDBNull(9) ? null : reader.GetString(9), reader.GetString(10));
}
