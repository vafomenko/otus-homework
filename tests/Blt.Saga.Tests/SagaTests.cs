using Blt.Orders.Api;
using Blt.Streaming;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Xunit;

namespace Blt.Saga.Tests;

public sealed class SagaTests(SagaFixture fixture) : IClassFixture<SagaFixture>
{
    private static long _nextUser = 9000;
    private readonly long _user = Interlocked.Increment(ref _nextUser);
    private readonly string _resource = Guid.NewGuid().ToString("N");
    private readonly Participants _participants = new(fixture);
    private OrderRequest Request => new(3000, _resource, 3, _resource);
    private SagaCoordinator Coordinator => new(fixture.OrdersDb, fixture.Orders, _participants, NullLogger<SagaCoordinator>.Instance);

    private async Task PrepareAsync(long balance = 10000, int stock = 10, int delivery = 1)
    {
        await fixture.SeedAsync(_resource, stock, delivery);
        await fixture.Billing.CreateAccountAsync(_user);
        if (balance > 0) await fixture.Billing.ApplyAsync(_user, Guid.NewGuid(), balance, "deposit");
    }

    private async Task<Order> CreateAsync(long balance = 10000, int stock = 10, int delivery = 1)
    {
        await PrepareAsync(balance, stock, delivery);
        return (await fixture.Orders.CreateSagaAsync(_user, "saga@example.com", Guid.NewGuid(), Request)).Order;
    }

    [Fact]
    public async Task Successful_saga_pays_reserves_product_and_specific_courier_and_writes_one_event()
    {
        var result = await Coordinator.RunAsync((await CreateAsync()).Id);
        Assert.Equal("paid", result.Status);
        Assert.Equal("done", result.Step);
        Assert.Equal(_resource, result.CourierId);
        Assert.Equal(7000, (await fixture.Billing.GetAsync(_user))!.Balance);
        Assert.Equal("reserved", (await fixture.Stock.GetAsync(result.Id, _user))!.Status);
        Assert.Equal("reserved", (await fixture.Delivery.GetAsync(result.Id, _user))!.Status);
        Assert.Equal(7, (await fixture.Stock.ListAsync()).Single(r => r.Id == _resource).Available);
        Assert.Equal(0, (await fixture.Delivery.ListAsync()).Single(r => r.Id == _resource).Available);
        Assert.Equal(1, await EventCountAsync(result.Id));
    }

    [Theory]
    [InlineData(0, 10, 1, "insufficient_funds")]
    [InlineData(10000, 0, 1, "out_of_stock")]
    [InlineData(10000, 10, 0, "courier_unavailable")]
    public async Task Every_business_failure_reverts_all_other_changes(long balance, int stock, int delivery, string reason)
    {
        var result = await Coordinator.RunAsync((await CreateAsync(balance, stock, delivery)).Id);
        Assert.Equal("rejected", result.Status);
        Assert.Equal(reason, result.Reason);
        await AssertRestoredAsync(result, balance, stock, delivery);
        Assert.Equal(1, await EventCountAsync(result.Id));
    }

    [Theory]
    [InlineData("payment")]
    [InlineData("stock")]
    [InlineData("delivery")]
    public async Task A_lost_success_response_is_compensated_including_the_uncertain_step(string step)
    {
        var order = await CreateAsync();
        _participants.LoseResponseAt = step;
        var result = await Coordinator.RunAsync(order.Id);
        Assert.Equal("rejected", result.Status);
        Assert.Equal(step + "_unavailable", result.Reason);
        await AssertRestoredAsync(result);
        Assert.Equal(3000, (await fixture.Billing.PaymentAsync(_user, result.Id))!.RefundedAmount);
    }

    [Theory]
    [InlineData("undo_delivery")]
    [InlineData("undo_stock")]
    [InlineData("undo_payment")]
    public async Task Failed_compensation_is_persisted_and_a_new_coordinator_can_resume_it(string step)
    {
        var order = await CreateAsync();
        _participants.LoseResponseAt = "delivery";
        _participants.FailBeforeAt = step;
        var pending = await Coordinator.RunAsync(order.Id);
        Assert.Equal("compensating", pending.Status);
        Assert.Equal(step, pending.Step);
        Assert.Equal(0, await EventCountAsync(order.Id));
        var result = await Coordinator.RunAsync(order.Id);
        Assert.Equal("rejected", result.Status);
        await AssertRestoredAsync(result);
        Assert.Equal(1, await EventCountAsync(order.Id));
    }

    [Theory]
    [InlineData("payment")]
    [InlineData("stock")]
    [InlineData("delivery")]
    public async Task Restart_after_remote_commit_before_saving_progress_does_not_repeat_the_effect(string step)
    {
        var order = await CreateAsync();
        await _participants.ChargeAsync(order, default);
        if (step is "stock" or "delivery")
        {
            await fixture.Orders.AdvanceAsync(order, "stock", null, default);
            order = (await fixture.Orders.GetSagaAsync(order.Id))!.Order;
            await _participants.ReserveStockAsync(order, default);
        }
        if (step == "delivery")
        {
            await fixture.Orders.AdvanceAsync(order, "delivery", null, default);
            order = (await fixture.Orders.GetSagaAsync(order.Id))!.Order;
            await _participants.ReserveCourierAsync(order, default);
        }
        var result = await Coordinator.RunAsync(order.Id);
        Assert.Equal("paid", result.Status);
        Assert.Equal(7000, (await fixture.Billing.GetAsync(_user))!.Balance);
        Assert.Equal(7, (await fixture.Stock.ListAsync()).Single(r => r.Id == _resource).Available);
        Assert.Equal(1, await EventCountAsync(order.Id));
    }

    [Theory]
    [InlineData("undo_delivery")]
    [InlineData("undo_stock")]
    [InlineData("undo_payment")]
    public async Task Lost_compensation_response_does_not_release_or_refund_twice(string step)
    {
        var order = await CreateAsync();
        _participants.FailBeforeAt = "delivery";
        _participants.LoseResponseAt = step;
        var pending = await Coordinator.RunAsync(order.Id);
        Assert.Equal(step, pending.Step);
        Assert.Equal("compensating", pending.Status);
        var result = await Coordinator.RunAsync(order.Id);
        Assert.Equal("rejected", result.Status);
        await AssertRestoredAsync(result);
        Assert.Equal(1, await EventCountAsync(order.Id));
    }

    [Fact]
    public async Task Concurrent_duplicate_order_requests_have_one_order_payment_reservation_and_event()
    {
        await PrepareAsync();
        var key = Guid.NewGuid();
        var created = await Task.WhenAll(Enumerable.Range(0, 12).Select(_ => fixture.Orders.CreateSagaAsync(_user, "saga@example.com", key, Request)));
        Assert.Single(created, r => r.Created);
        var id = created[0].Order.Id;
        Assert.All(created, r => Assert.Equal(id, r.Order.Id));
        await Task.WhenAll(created.Select(r => Coordinator.RunAsync(r.Order.Id)));
        Assert.Equal("paid", (await fixture.Orders.GetSagaAsync(id))!.Order.Status);
        Assert.Equal(7000, (await fixture.Billing.GetAsync(_user))!.Balance);
        Assert.Equal(7, (await fixture.Stock.ListAsync()).Single(r => r.Id == _resource).Available);
        Assert.Equal(1, await EventCountAsync(id));
        await Assert.ThrowsAsync<OrderConflictException>(() => fixture.Orders.CreateSagaAsync(_user, "saga@example.com", key, Request with { Price = 3001 }));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Concurrent_reservations_cannot_oversell_stock_or_double_book_a_courier(bool delivery)
    {
        await fixture.SeedAsync(_resource, 1, 1);
        var repository = delivery ? fixture.Delivery : fixture.Stock;
        var results = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => repository.ReserveAsync(new(Guid.NewGuid(), _user, _resource, 1))));
        var winner = Assert.Single(results, r => r.Status == "reserved");
        Assert.Equal(19, results.Count(r => r.Status == "rejected"));
        var release = new ReservationRequest(winner.OrderId, _user, _resource, 1);
        await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => repository.ReleaseAsync(release)));
        Assert.Equal(1, (await repository.ListAsync()).Single(r => r.Id == _resource).Available);
        Assert.Equal("released", (await repository.ReserveAsync(release)).Status);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Release_before_reservation_fences_a_delayed_request(bool delivery)
    {
        await fixture.SeedAsync(_resource, 10, 1);
        var repository = delivery ? fixture.Delivery : fixture.Stock;
        var request = new ReservationRequest(Guid.NewGuid(), _user, _resource, 1);
        await repository.ReleaseAsync(request);
        Assert.Equal("released", (await repository.ReserveAsync(request)).Status);
        Assert.Equal(delivery ? 1 : 10, (await repository.ListAsync()).Single(r => r.Id == _resource).Available);
        await Assert.ThrowsAsync<ReservationConflictException>(() => repository.ReserveAsync(request with { UserId = _user + 1 }));
        Assert.Null(await repository.GetAsync(request.OrderId, _user + 1));
    }

    [Fact]
    public async Task Concurrent_refund_and_delayed_charge_leave_the_original_balance()
    {
        var order = await CreateAsync();
        var request = new ChargeRequest(order.Id, _user, order.Price);
        await Task.WhenAll(Enumerable.Range(0, 10).Select(async n =>
        {
            if (n % 2 == 0) await fixture.Billing.RefundAsync(request);
            else await fixture.Billing.ApplyAsync(_user, order.Id, order.Price, "charge");
        }));
        Assert.Equal(10000, (await fixture.Billing.GetAsync(_user))!.Balance);
        Assert.Equal("cancelled", (await fixture.Billing.ApplyAsync(_user, order.Id, order.Price, "charge"))!.Status);
        await Assert.ThrowsAsync<Blt.Billing.Api.OperationConflictException>(() => fixture.Billing.RefundAsync(request with { Amount = 1 }));
        Assert.Null(await fixture.Billing.PaymentAsync(_user + 1, order.Id));
    }

    [Fact]
    public async Task Deposit_keeps_enough_headroom_for_a_pending_refund()
    {
        await PrepareAsync();
        var orderId = Guid.NewGuid();
        await using var connection = await fixture.BillingDb.OpenAsync();
        await using var command = new NpgsqlCommand("UPDATE accounts SET balance=9000000000000000-1000 WHERE user_id=@user", connection);
        command.Parameters.AddWithValue("user", _user);
        await command.ExecuteNonQueryAsync();
        await fixture.Billing.ApplyAsync(_user, orderId, 500, "charge");
        var rejected = await fixture.Billing.ApplyAsync(_user, Guid.NewGuid(), 1500, "deposit");
        Assert.Equal("balance_limit", rejected!.Reason);
        await fixture.Billing.ApplyAsync(_user, Guid.NewGuid(), 1000, "deposit");
        await fixture.Billing.RefundAsync(new(orderId, _user, 500));
        Assert.Equal(9_000_000_000_000_000, (await fixture.Billing.GetAsync(_user))!.Balance);
    }

    [Fact]
    public async Task Recovery_worker_finishes_a_persisted_order_without_a_client_retry()
    {
        var order = await CreateAsync();
        using var worker = new SagaRecovery(fixture.Orders, Coordinator, NullLogger<SagaRecovery>.Instance);
        await worker.StartAsync(default);
        try
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            while ((await fixture.Orders.GetSagaAsync(order.Id, deadline.Token))!.Order.Status != "paid")
                await Task.Delay(50, deadline.Token);
        }
        finally { await worker.StopAsync(default); }
        Assert.Equal(7000, (await fixture.Billing.GetAsync(_user))!.Balance);
    }

    [Fact]
    public async Task Reapplying_migrations_preserves_stock_reservations_and_orders()
    {
        var result = await Coordinator.RunAsync((await CreateAsync()).Id);
        await fixture.StockDb.MigrateAsync(typeof(Blt.Warehouse.Api.Program).Assembly);
        await fixture.DeliveryDb.MigrateAsync(typeof(Blt.Delivery.Api.Program).Assembly);
        await fixture.OrdersDb.MigrateAsync(typeof(OrderRepository).Assembly);
        Assert.Equal("paid", (await fixture.Orders.GetSagaAsync(result.Id))!.Order.Status);
        Assert.Equal(7, (await fixture.Stock.ListAsync()).Single(r => r.Id == _resource).Available);
        Assert.Equal(0, (await fixture.Delivery.ListAsync()).Single(r => r.Id == _resource).Available);
    }

    [Theory]
    [InlineData("payment")]
    [InlineData("stock")]
    [InlineData("delivery")]
    [InlineData("undo_delivery")]
    [InlineData("undo_stock")]
    [InlineData("undo_payment")]
    public async Task Worker_resumes_after_coordinator_stops_between_remote_commit_and_checkpoint(string step)
    {
        var order = await CreateAsync();
        var compensating = step.StartsWith("undo_", StringComparison.Ordinal);
        if (compensating) _participants.FailBeforeAt = "delivery";
        _participants.BlockAfterAt = step;
        using var crashedProcess = new CancellationTokenSource();
        var interrupted = Coordinator.RunAsync(order.Id, crashedProcess.Token);
        try
        {
            await _participants.Committed.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal(step, (await fixture.Orders.GetSagaAsync(order.Id))!.Order.Step);
            Assert.Equal(step, (await Coordinator.RunAsync(order.Id)).Step);
        }
        finally { crashedProcess.Cancel(); }
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => interrupted);

        using var worker = new SagaRecovery(fixture.Orders, Coordinator, NullLogger<SagaRecovery>.Instance);
        await worker.StartAsync(default);
        Order recovered;
        try
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            do
            {
                recovered = (await fixture.Orders.GetSagaAsync(order.Id, deadline.Token))!.Order;
                if (recovered.Step == "done") break;
                await Task.Delay(50, deadline.Token);
            } while (true);
        }
        finally { await worker.StopAsync(default); }
        Assert.Equal(compensating ? "rejected" : "paid", recovered.Status);
        Assert.Equal(1, await EventCountAsync(order.Id));
        Assert.True((await fixture.Orders.GetSagaAsync(order.Id))!.RecoveryAttempts >= 2);
        if (compensating) await AssertRestoredAsync(recovered);
        else
        {
            Assert.Equal(7000, (await fixture.Billing.GetAsync(_user))!.Balance);
            Assert.Equal(7, (await fixture.Stock.ListAsync()).Single(r => r.Id == _resource).Available);
            Assert.Equal(0, (await fixture.Delivery.ListAsync()).Single(r => r.Id == _resource).Available);
        }
    }

    [Theory]
    [InlineData("payment")]
    [InlineData("stock")]
    [InlineData("delivery")]
    public async Task Expired_processing_saga_compensates_every_completed_step(string step)
    {
        var order = await CreateAsync();
        await _participants.ChargeAsync(order, default);
        if (step is "stock" or "delivery")
        {
            await fixture.Orders.AdvanceAsync(order, "stock", null, default);
            order = (await fixture.Orders.GetSagaAsync(order.Id))!.Order;
            await _participants.ReserveStockAsync(order, default);
        }
        if (step == "delivery")
        {
            await fixture.Orders.AdvanceAsync(order, "delivery", null, default);
            order = (await fixture.Orders.GetSagaAsync(order.Id))!.Order;
            await _participants.ReserveCourierAsync(order, default);
        }
        await ExpireAsync(order.Id);
        var result = await Coordinator.RunAsync(order.Id);
        Assert.Equal("rejected", result.Status);
        Assert.Equal("saga_timeout", result.Reason);
        await AssertRestoredAsync(result);
        Assert.Equal(3000, (await fixture.Billing.PaymentAsync(_user, order.Id))!.RefundedAmount);
    }

    [Fact]
    public async Task Expired_compensation_is_retried_until_it_actually_finishes()
    {
        var order = await CreateAsync();
        _participants.FailBeforeAt = "delivery";
        _participants.LoseResponseAt = "undo_payment";
        var pending = await Coordinator.RunAsync(order.Id);
        Assert.Equal("compensating", pending.Status);
        var checkpoint = (await fixture.Orders.GetSagaAsync(order.Id))!;
        Assert.Equal(1, checkpoint.RetryCount);
        Assert.Equal("undo_payment_unavailable", checkpoint.LastError);
        Assert.NotNull(checkpoint.LastAttemptAt);
        Assert.DoesNotContain(order.Id, await fixture.Orders.PendingAsync());
        await ExpireAsync(order.Id);
        var result = await Coordinator.RunAsync(order.Id);
        Assert.Equal("rejected", result.Status);
        Assert.Equal("delivery_unavailable", result.Reason);
        await AssertRestoredAsync(result);
        var finished = (await fixture.Orders.GetSagaAsync(order.Id))!;
        Assert.Equal(0, finished.RetryCount);
        Assert.Null(finished.LastError);
        Assert.Equal(2, finished.RecoveryAttempts);
    }

    [Fact]
    public async Task Retry_delays_are_durable_capped_and_cleared_after_progress()
    {
        var order = await CreateAsync();
        foreach (var delay in new[] { 1, 2, 4, 8, 16, 30, 30 })
        {
            await fixture.Orders.DeferAsync(order.Id, "recovery_error");
            await using var connection = await fixture.OrdersDb.OpenAsync();
            await using var command = new NpgsqlCommand("SELECT EXTRACT(EPOCH FROM (next_attempt_at-updated_at))::double precision FROM orders WHERE id=@id", connection);
            command.Parameters.AddWithValue("id", order.Id);
            Assert.Equal((double)delay, await command.ExecuteScalarAsync());
            Assert.DoesNotContain(order.Id, await fixture.Orders.PendingAsync());
        }
        Assert.Equal(7, (await fixture.Orders.GetSagaAsync(order.Id))!.RetryCount);
        Assert.Equal("paid", (await Coordinator.RunAsync(order.Id)).Status);
        Assert.Null((await fixture.Orders.GetSagaAsync(order.Id))!.LastError);
    }

    [Fact]
    public async Task A_batch_of_100_failing_sagas_does_not_starve_a_new_order()
    {
        var order = await CreateAsync();
        await using var connection = await fixture.OrdersDb.OpenAsync();
        await using var command = new NpgsqlCommand("""
            INSERT INTO orders(id, user_id, price, status, step, next_attempt_at)
            SELECT gen_random_uuid(), @user, 1, 'processing', 'unsupported', NOW()-INTERVAL '1 hour'
            FROM generate_series(1, 100)
            """, connection);
        command.Parameters.AddWithValue("user", _user);
        await command.ExecuteNonQueryAsync();
        Assert.DoesNotContain(order.Id, await fixture.Orders.PendingAsync());
        using var worker = new SagaRecovery(fixture.Orders, Coordinator, NullLogger<SagaRecovery>.Instance);
        await worker.StartAsync(default);
        try
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            while ((await fixture.Orders.GetSagaAsync(order.Id, deadline.Token))!.Order.Status != "paid")
                await Task.Delay(50, deadline.Token);
        }
        finally
        {
            await worker.StopAsync(default);
            command.CommandText = "DELETE FROM orders WHERE user_id=@user AND step='unsupported'";
            await command.ExecuteNonQueryAsync();
        }
        Assert.Equal(7000, (await fixture.Billing.GetAsync(_user))!.Balance);
    }

    private async Task ExpireAsync(Guid id)
    {
        await using var connection = await fixture.OrdersDb.OpenAsync();
        await using var command = new NpgsqlCommand("UPDATE orders SET deadline_at=NOW()-INTERVAL '1 second' WHERE id=@id", connection);
        command.Parameters.AddWithValue("id", id);
        await command.ExecuteNonQueryAsync();
    }

    private async Task AssertRestoredAsync(Order order, long balance = 10000, int stock = 10, int delivery = 1)
    {
        Assert.Equal(balance, (await fixture.Billing.GetAsync(_user))!.Balance);
        Assert.Equal(stock, (await fixture.Stock.ListAsync()).Single(r => r.Id == _resource).Available);
        Assert.Equal(delivery, (await fixture.Delivery.ListAsync()).Single(r => r.Id == _resource).Available);
        Assert.Equal("released", (await fixture.Stock.GetAsync(order.Id, _user))!.Status);
        Assert.Equal("released", (await fixture.Delivery.GetAsync(order.Id, _user))!.Status);
        Assert.Null(await fixture.Orders.GetAsync(order.Id, _user + 1, default));
    }

    private async Task<long> EventCountAsync(Guid id)
    {
        await using var connection = await fixture.OrdersDb.OpenAsync();
        await using var command = new NpgsqlCommand("SELECT count(*) FROM outbox WHERE order_id=@id", connection);
        command.Parameters.AddWithValue("id", id);
        return (long)(await command.ExecuteScalarAsync())!;
    }

    private sealed class Participants(SagaFixture fixture) : ISagaParticipants
    {
        public string? LoseResponseAt { get; set; }
        public string? FailBeforeAt { get; set; }
        public string? BlockAfterAt { get; set; }
        public TaskCompletionSource Committed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private async Task<T> CallAsync<T>(string step, Func<Task<T>> action, CancellationToken ct)
        {
            if (FailBeforeAt == step) { FailBeforeAt = null; throw new HttpRequestException("Participant is unavailable."); }
            var result = await action();
            if (BlockAfterAt == step)
            {
                BlockAfterAt = null;
                Committed.TrySetResult();
                await Task.Delay(Timeout.Infinite, ct);
            }
            if (LoseResponseAt == step) { LoseResponseAt = null; throw new HttpRequestException("Response lost after commit."); }
            return result;
        }
        public Task<Operation> ChargeAsync(Order o, CancellationToken ct) => CallAsync("payment", async () => (await fixture.Billing.ApplyAsync(o.UserId, o.Id, o.Price, "charge", ct))!, ct);
        public Task<Reservation> ReserveStockAsync(Order o, CancellationToken ct) => CallAsync("stock", () => fixture.Stock.ReserveAsync(new(o.Id, o.UserId, o.Sku!, o.Quantity), ct), ct);
        public Task<Reservation> ReserveCourierAsync(Order o, CancellationToken ct) => CallAsync("delivery", () => fixture.Delivery.ReserveAsync(new(o.Id, o.UserId, o.SlotId!, 1), ct), ct);
        public async Task ReleaseCourierAsync(Order o, CancellationToken ct) => _ = await CallAsync("undo_delivery", () => fixture.Delivery.ReleaseAsync(new(o.Id, o.UserId, o.SlotId!, 1), ct), ct);
        public async Task ReleaseStockAsync(Order o, CancellationToken ct) => _ = await CallAsync("undo_stock", () => fixture.Stock.ReleaseAsync(new(o.Id, o.UserId, o.Sku!, o.Quantity), ct), ct);
        public async Task RefundAsync(Order o, CancellationToken ct) => _ = await CallAsync("undo_payment", () => fixture.Billing.RefundAsync(new(o.Id, o.UserId, o.Price), ct), ct);
    }
}
