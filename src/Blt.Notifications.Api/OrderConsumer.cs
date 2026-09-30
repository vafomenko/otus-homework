using System.Text.Json;
using Blt.Streaming;
using Confluent.Kafka;

namespace Blt.Notifications.Api;

public sealed class OrderConsumer(NotificationRepository db, IConfiguration config, ILogger<OrderConsumer> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var consumer = new ConsumerBuilder<string, string>(KafkaSettings.Consumer(config)).Build();
        consumer.Subscribe(KafkaSettings.Topic);
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                var record = consumer.Consume(stoppingToken);
                var message = JsonSerializer.Deserialize<OrderEvent>(record.Message.Value, KafkaSettings.Json)
                    ?? throw new InvalidDataException("Empty event.");
                await db.SaveOrderAsync(message, stoppingToken);
                // Commit only after PostgreSQL commits the notification/inbox insert.
                consumer.Commit(record);
                logger.LogInformation("Saved notification for order {OrderId}, event {EventId}", message.Data.OrderId, message.Id);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
        finally { consumer.Close(); }
    }
}
