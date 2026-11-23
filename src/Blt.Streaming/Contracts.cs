namespace Blt.Streaming;

public static class Money
{
    public const long Maximum = 1_000_000_000_000;
    public static bool IsValid(long amount) => amount is > 0 and <= Maximum;
}

public sealed record AccountRequest(long UserId);
public sealed record Account(long UserId, long Balance, string Currency = "RUB");
public sealed record MoneyRequest(Guid OperationId, long Amount);
public sealed record ChargeRequest(Guid OrderId, long UserId, long Amount);
public sealed record Operation(Guid OperationId, long UserId, long Amount, string Kind, string Status, long Balance, string? Reason);
public sealed record OrderRequest(long Price, string Sku, int Quantity, string SlotId);
public sealed record Order(Guid Id, long UserId, long Price, string Status, string? Reason, DateTimeOffset CreatedAt,
    string? Sku = null, int Quantity = 0, string? SlotId = null, string? CourierId = null, string Step = "legacy");
public sealed record PaymentState(Guid OrderId, long Amount, string Status, long RefundedAmount);
public sealed record ReservationRequest(Guid OrderId, long UserId, string ResourceId, int Quantity);
public sealed record Reservation(Guid OrderId, long UserId, string ResourceId, int Quantity, string Status,
    string? Reason, string? CourierId);
public sealed record Resource(string Id, int Available, string? CourierId, DateTimeOffset? StartsAt, DateTimeOffset? EndsAt);
public sealed record OrderResult(Guid OrderId, long UserId, string Email, long Price, string Status, string? Reason);

public sealed record OrderEvent(string Specversion, Guid Id, string Source, string Type, string Subject,
    DateTimeOffset Time, string Datacontenttype, OrderResult Data)
{
    public static OrderEvent Create(Order order, string email) => new("1.0", Guid.NewGuid(), "urn:blt:order-service",
        "com.blt.order.completed.v1", $"orders/{order.Id}", DateTimeOffset.UtcNow, "application/json",
        new OrderResult(order.Id, order.UserId, email, order.Price, order.Status, order.Reason));
}
