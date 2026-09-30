using Blt.Streaming;
using Confluent.Kafka;
using Npgsql;

namespace Blt.Orders.Api;

public sealed class OutboxPublisher(Database db, IConfiguration config, ILogger<OutboxPublisher> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var producer = new ProducerBuilder<string, string>(KafkaSettings.Producer(config)).Build();
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (!await PublishNextAsync(producer, stoppingToken))
                    await Task.Delay(200, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception exception)
            {
                logger.LogError(exception, "Outbox publish failed; the event remains pending");
                await Task.Delay(1000, stoppingToken);
            }
        }
    }

    private async Task<bool> PublishNextAsync(IProducer<string, string> producer, CancellationToken ct)
    {
        await using var connection = await db.OpenAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        await using var command = new NpgsqlCommand("""
            SELECT event_id, order_id, payload::text FROM outbox
            WHERE published_at IS NULL ORDER BY created_at LIMIT 1 FOR UPDATE SKIP LOCKED
            """, connection, transaction);
        Guid eventId;
        string orderId, payload;
        await using (var reader = await command.ExecuteReaderAsync(ct))
        {
            if (!await reader.ReadAsync(ct)) return false;
            eventId = reader.GetGuid(0);
            orderId = reader.GetGuid(1).ToString();
            payload = reader.GetString(2);
        }
        await producer.ProduceAsync(KafkaSettings.Topic, new Message<string, string> { Key = orderId, Value = payload }, ct);
        command.CommandText = "UPDATE outbox SET published_at=NOW() WHERE event_id=@id";
        command.Parameters.AddWithValue("id", eventId);
        await command.ExecuteNonQueryAsync(ct);
        await transaction.CommitAsync(ct);
        logger.LogInformation("Published order event {EventId} for order {OrderId}", eventId, orderId);
        return true;
    }
}
