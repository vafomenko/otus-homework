namespace Blt.Streaming;

public static class Money
{
    // All amounts are integer kopecks (RUB), safe to represent exactly in JSON/JavaScript.
    public const long Maximum = 1_000_000_000_000;
    public static bool IsValid(long amount) => amount is > 0 and <= Maximum;
}

public sealed record AccountRequest(long UserId);
public sealed record Account(long UserId, long Balance, string Currency = "RUB");
public sealed record MoneyRequest(Guid OperationId, long Amount);
public sealed record ChargeRequest(Guid OrderId, long UserId, long Amount);
public sealed record Operation(Guid OperationId, long UserId, long Amount, string Kind, string Status, long Balance, string? Reason);
public sealed record OrderRequest(long Price);
public sealed record Order(Guid Id, long UserId, long Price, string Status, string? Reason, DateTimeOffset CreatedAt);
public sealed record OrderResult(Guid OrderId, long UserId, string Email, long Price, string Status, string? Reason);

// CloudEvents-like envelope and topic naming follow homework 1. JSON replaces Schema Registry in this lab.
public sealed record OrderEvent(string Specversion, Guid Id, string Source, string Type, string Subject,
    DateTimeOffset Time, string Datacontenttype, OrderResult Data)
{
    public static OrderEvent Create(Order order, string email) => new("1.0", Guid.NewGuid(), "urn:blt:order-service",
        "com.blt.order.completed.v1", $"orders/{order.Id}", DateTimeOffset.UtcNow, "application/json",
        new OrderResult(order.Id, order.UserId, email, order.Price, order.Status, order.Reason));
}
