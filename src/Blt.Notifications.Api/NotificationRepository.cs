using Blt.Streaming;
using Npgsql;

namespace Blt.Notifications.Api;

public sealed record Notification(Guid Id, Guid EventId, Guid? OrderId, long UserId, string Email,
    string Subject, string Body, string? Outcome, DateTimeOffset CreatedAt);
public sealed record SendNotificationRequest(string Subject, string Body);

public sealed class NotificationRepository(Database db)
{
    public Task SaveOrderAsync(OrderEvent message, CancellationToken ct = default)
    {
        var data = message.Data;
        if (message.Type != "com.blt.order.completed.v1" || data.Status is not ("paid" or "rejected") ||
            message.Id == Guid.Empty || data.OrderId == Guid.Empty || data.UserId <= 0)
            throw new InvalidDataException("Unsupported order event.");
        var happy = data.Status == "paid";
        return SaveAsync(new Notification(Guid.NewGuid(), message.Id, data.OrderId, data.UserId, data.Email,
            happy ? "Письмо счастья" : "Письмо горя",
            happy ? $"Заказ {data.OrderId} оформлен. Оплата {data.Price} коп. прошла успешно."
                  : $"Заказ {data.OrderId} не оформлен: {data.Reason}. Оплата и резервы отменены.",
            data.Status, DateTimeOffset.UtcNow), ct);
    }

    public async Task SaveAsync(Notification message, CancellationToken ct = default)
    {
        await using var connection = await db.OpenAsync(ct);
        await using var command = new NpgsqlCommand("""
            INSERT INTO notifications(id, event_id, order_id, user_id, email, subject, body, outcome, created_at)
            VALUES (@id, @event, @order, @user, @email, @subject, @body, @outcome, @time)
            ON CONFLICT DO NOTHING
            """, connection);
        command.Parameters.AddWithValue("id", message.Id);
        command.Parameters.AddWithValue("event", message.EventId);
        command.Parameters.AddWithValue("order", NpgsqlTypes.NpgsqlDbType.Uuid, (object?)message.OrderId ?? DBNull.Value);
        command.Parameters.AddWithValue("user", message.UserId);
        command.Parameters.AddWithValue("email", message.Email);
        command.Parameters.AddWithValue("subject", message.Subject);
        command.Parameters.AddWithValue("body", message.Body);
        command.Parameters.AddWithValue("outcome", NpgsqlTypes.NpgsqlDbType.Text, (object?)message.Outcome ?? DBNull.Value);
        command.Parameters.AddWithValue("time", message.CreatedAt);
        await command.ExecuteNonQueryAsync(ct);
    }

    public async Task<List<Notification>> ListAsync(long userId, Guid? orderId = null, CancellationToken ct = default)
    {
        await using var connection = await db.OpenAsync(ct);
        await using var command = new NpgsqlCommand("""
            SELECT id, event_id, order_id, email, subject, body, outcome, created_at FROM notifications
            WHERE user_id=@user AND (@order IS NULL OR order_id=@order) ORDER BY created_at, id
            """, connection);
        command.Parameters.AddWithValue("user", userId);
        command.Parameters.AddWithValue("order", NpgsqlTypes.NpgsqlDbType.Uuid, (object?)orderId ?? DBNull.Value);
        await using var reader = await command.ExecuteReaderAsync(ct);
        var result = new List<Notification>();
        while (await reader.ReadAsync(ct))
            result.Add(new Notification(reader.GetGuid(0), reader.GetGuid(1), reader.IsDBNull(2) ? null : reader.GetGuid(2),
                userId, reader.GetString(3), reader.GetString(4), reader.GetString(5), reader.IsDBNull(6) ? null : reader.GetString(6), reader.GetFieldValue<DateTimeOffset>(7)));
        return result;
    }
}
