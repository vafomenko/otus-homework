using System.Text.Json;
using Confluent.Kafka;
using Microsoft.Extensions.Configuration;

namespace Blt.Streaming;

public static class KafkaSettings
{
    public const string Topic = "blt.order.events.v1";
    public const string NotificationGroup = "blt.notifications.v1";
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    public static ProducerConfig Producer(IConfiguration config) => new()
    {
        BootstrapServers = config["Kafka:BootstrapServers"] ?? "kafka:9092",
        EnableIdempotence = true, Acks = Acks.All, MessageTimeoutMs = 15000,
        ClientId = "blt-orders"
    };
    public static ConsumerConfig Consumer(IConfiguration config) => new()
    {
        BootstrapServers = config["Kafka:BootstrapServers"] ?? "kafka:9092",
        GroupId = NotificationGroup, AutoOffsetReset = AutoOffsetReset.Earliest,
        EnableAutoCommit = false, EnableAutoOffsetStore = false,
        ClientId = "blt-notifications"
    };
}
