using Blt.Streaming;

namespace Blt.Orders.Api;

public interface ISagaParticipants
{
    Task<Operation> ChargeAsync(Order order, CancellationToken ct);
    Task<Reservation> ReserveStockAsync(Order order, CancellationToken ct);
    Task<Reservation> ReserveCourierAsync(Order order, CancellationToken ct);
    Task ReleaseCourierAsync(Order order, CancellationToken ct);
    Task ReleaseStockAsync(Order order, CancellationToken ct);
    Task RefundAsync(Order order, CancellationToken ct);
}

public sealed class HttpSagaParticipants(IHttpClientFactory clients) : ISagaParticipants
{
    public Task<Operation> ChargeAsync(Order order, CancellationToken ct) =>
        PostAsync<Operation>("billing", "/internal/charges", new ChargeRequest(order.Id, order.UserId, order.Price), ct);
    public Task<Reservation> ReserveStockAsync(Order order, CancellationToken ct) =>
        PostAsync<Reservation>("warehouse", "/internal/reservations", Stock(order), ct);
    public Task<Reservation> ReserveCourierAsync(Order order, CancellationToken ct) =>
        PostAsync<Reservation>("delivery", "/internal/reservations", Courier(order), ct);
    public async Task ReleaseCourierAsync(Order order, CancellationToken ct) =>
        _ = await PostAsync<Reservation>("delivery", "/internal/releases", Courier(order), ct);
    public async Task ReleaseStockAsync(Order order, CancellationToken ct) =>
        _ = await PostAsync<Reservation>("warehouse", "/internal/releases", Stock(order), ct);
    public async Task RefundAsync(Order order, CancellationToken ct) =>
        _ = await PostAsync<PaymentState>("billing", "/internal/refunds", new ChargeRequest(order.Id, order.UserId, order.Price), ct);

    private async Task<T> PostAsync<T>(string service, string path, object request, CancellationToken ct)
    {
        using var response = await clients.CreateClient(service).PostAsJsonAsync(path, request, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<T>(ct) ?? throw new HttpRequestException($"Empty response from {service}.");
    }

    private static ReservationRequest Stock(Order order) => new(order.Id, order.UserId, order.Sku!, order.Quantity);
    private static ReservationRequest Courier(Order order) => new(order.Id, order.UserId, order.SlotId!, 1);
}
