using System.Text.Json;
using Blt.Streaming;
using Npgsql;

namespace Blt.Orders.Api;

public sealed class OrderRepository(Database db)
{
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
        await using var command = new NpgsqlCommand("SELECT price, status, reason, created_at FROM orders WHERE id=@id AND user_id=@user", connection);
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("user", userId);
        await using var reader = await command.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? new Order(id, userId, reader.GetInt64(0), reader.GetString(1),
            reader.IsDBNull(2) ? null : reader.GetString(2), reader.GetFieldValue<DateTimeOffset>(3)) : null;
    }
}
