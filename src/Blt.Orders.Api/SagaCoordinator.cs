using System.Text.Json;
using Blt.Streaming;
using Npgsql;

namespace Blt.Orders.Api;

public sealed class SagaCoordinator(Database db, OrderRepository orders, ISagaParticipants participants, ILogger<SagaCoordinator> logger)
{
    public async Task<Order> RunAsync(Guid id, CancellationToken ct = default)
    {
        await using var connection = await db.OpenAsync(ct);
        await using var tx = await connection.BeginTransactionAsync(ct);
        await using var command = new NpgsqlCommand("SET LOCAL idle_in_transaction_session_timeout='30s'; SET LOCAL application_name='blt-orders-saga'", connection, tx);
        await command.ExecuteNonQueryAsync(ct);
        command.CommandText = "SELECT pg_try_advisory_xact_lock(hashtextextended(@id::text, 9))";
        command.Parameters.AddWithValue("id", id);
        var locked = (bool)(await command.ExecuteScalarAsync(ct))!;
        var started = false;
        while (true)
        {
            if (locked)
            {
                command.CommandText = "SELECT 1";
                await command.ExecuteScalarAsync(ct);
            }
            var saga = await orders.GetSagaAsync(id, ct) ?? throw new InvalidOperationException("Saga order not found.");
            var order = saga.Order;
            if (!locked || order.Step is "done" or "legacy") return order;
            if (!started)
            {
                await orders.BeginAttemptAsync(id, ct);
                started = true;
            }
            if (order.Status == "processing" && DateTimeOffset.UtcNow >= saga.DeadlineAt)
            {
                await orders.AdvanceAsync(order, "undo_delivery", "saga_timeout", ct);
                continue;
            }
            logger.LogInformation("Saga {OrderId}: {Step}", id, order.Step);
            try
            {
                switch (order.Step)
                {
                    case "payment":
                        var payment = await participants.ChargeAsync(order, ct);
                        await orders.AdvanceAsync(order, payment.Status == "succeeded" ? "stock" : "undo_delivery", payment.Reason, ct);
                        break;
                    case "stock":
                        var stock = await participants.ReserveStockAsync(order, ct);
                        await orders.AdvanceAsync(order, stock.Status == "reserved" ? "delivery" : "undo_delivery", stock.Reason, ct);
                        break;
                    case "delivery":
                        var courier = await participants.ReserveCourierAsync(order, ct);
                        if (DateTimeOffset.UtcNow >= saga.DeadlineAt)
                            await orders.AdvanceAsync(order, "undo_delivery", "saga_timeout", ct);
                        else if (courier.Status == "reserved") await orders.FinishSagaAsync(order, saga.Email, true, courier.CourierId, ct);
                        else await orders.AdvanceAsync(order, "undo_delivery", courier.Reason, ct);
                        break;
                    case "undo_delivery":
                        await participants.ReleaseCourierAsync(order, ct);
                        await orders.AdvanceAsync(order, "undo_stock", null, ct);
                        break;
                    case "undo_stock":
                        await participants.ReleaseStockAsync(order, ct);
                        await orders.AdvanceAsync(order, "undo_payment", null, ct);
                        break;
                    case "undo_payment":
                        await participants.RefundAsync(order, ct);
                        await orders.FinishSagaAsync(order, saga.Email, false, null, ct);
                        break;
                    default: throw new InvalidOperationException($"Unknown saga step: {order.Step}.");
                }
            }
            catch (Exception exception) when (exception is HttpRequestException or JsonException || exception is OperationCanceledException && !ct.IsCancellationRequested)
            {
                logger.LogWarning(exception, "Saga {OrderId}: participant failed at {Step}", id, order.Step);
                if (order.Status == "compensating")
                {
                    await orders.DeferAsync(id, order.Step + "_unavailable", ct);
                    return order;
                }
                await orders.AdvanceAsync(order, "undo_delivery", order.Step + "_unavailable", ct);
            }
        }
    }
}

public sealed class SagaRecovery(OrderRepository orders, SagaCoordinator coordinator, ILogger<SagaRecovery> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var pending = await orders.PendingAsync(stoppingToken);
                await Parallel.ForEachAsync(pending, new ParallelOptions { CancellationToken = stoppingToken, MaxDegreeOfParallelism = 4 },
                    async (id, ct) =>
                    {
                        try { await coordinator.RunAsync(id, ct); }
                        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
                        catch (Exception exception)
                        {
                            logger.LogError(exception, "Saga {OrderId} will be retried", id);
                            try { await orders.DeferAsync(id, "recovery_error", ct); }
                            catch (Exception deferError) when (!ct.IsCancellationRequested)
                            {
                                logger.LogError(deferError, "Could not schedule retry for saga {OrderId}", id);
                            }
                        }
                    });
                await Task.Delay(1000, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception exception)
            {
                logger.LogError(exception, "Saga recovery failed; retrying");
                await Task.Delay(1000, stoppingToken);
            }
        }
    }
}
