using Blt.Billing.Api;
using Blt.Notifications.Api;
using Blt.Orders.Api;
using Blt.Streaming;
using Npgsql;
using Xunit;

namespace Blt.Users.Api.Tests;

public sealed class StreamingTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>, IAsyncLifetime
{
    private Database _db = null!;
    private BillingRepository _billing = null!;

    public async Task InitializeAsync()
    {
        _db = new Database(postgres.ConnectionString);
        await _db.MigrateAsync(typeof(BillingRepository).Assembly);
        await _db.MigrateAsync(typeof(OrderRepository).Assembly);
        await _db.MigrateAsync(typeof(NotificationRepository).Assembly);
        _billing = new BillingRepository(_db);
    }

    public async Task DisposeAsync() => await _db.DisposeAsync();

    [Fact]
    public async Task Concurrent_charges_cannot_overdraw_an_account()
    {
        const long user = 801;
        await _billing.CreateAccountAsync(user);
        await _billing.ApplyAsync(user, Guid.NewGuid(), 10000, "deposit");
        var results = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ =>
            _billing.ApplyAsync(user, Guid.NewGuid(), 3000, "charge")));
        Assert.Equal(3, results.Count(r => r!.Status == "succeeded"));
        Assert.Equal(7, results.Count(r => r!.Reason == "insufficient_funds"));
        Assert.Equal(1000, (await _billing.GetAsync(user))!.Balance);
    }

    [Theory]
    [InlineData("deposit")]
    [InlineData("withdrawal")]
    [InlineData("charge")]
    public async Task Concurrent_retries_apply_a_money_operation_once(string kind)
    {
        var user = kind switch { "deposit" => 802, "withdrawal" => 803, _ => 804 };
        await _billing.CreateAccountAsync(user);
        await _billing.ApplyAsync(user, Guid.NewGuid(), 10000, "deposit");
        var id = Guid.NewGuid();
        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => _billing.ApplyAsync(user, id, 1500, kind)));
        Assert.All(results, result => Assert.Equal(results[0], result));
        Assert.Equal(kind == "deposit" ? 11500 : 8500, (await _billing.GetAsync(user))!.Balance);
        await Assert.ThrowsAsync<OperationConflictException>(() => _billing.ApplyAsync(user, id, 1501, kind));
    }

    [Fact]
    public async Task Rejected_payment_stays_rejected_on_retry_after_a_deposit()
    {
        const long user = 805;
        await _billing.CreateAccountAsync(user);
        var id = Guid.NewGuid();
        var original = await _billing.ApplyAsync(user, id, 500, "charge");
        await _billing.ApplyAsync(user, Guid.NewGuid(), 1000, "deposit");
        var retry = await _billing.ApplyAsync(user, id, 500, "charge");
        Assert.Equal(original, retry);
        Assert.Equal("rejected", retry!.Status);
        Assert.Equal(1000, (await _billing.GetAsync(user))!.Balance);
    }

    [Fact]
    public async Task Provisioning_the_same_account_does_not_reset_its_balance()
    {
        const long user = 806;
        await _billing.CreateAccountAsync(user);
        await _billing.ApplyAsync(user, Guid.NewGuid(), 1000, "deposit");
        await _billing.CreateAccountAsync(user);
        Assert.Equal(1000, (await _billing.GetAsync(user))!.Balance);
        Assert.Null(await _billing.ApplyAsync(999999, Guid.NewGuid(), 100, "deposit"));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(1000000000001)]
    public async Task Invalid_money_does_not_change_balance(long amount)
    {
        await _billing.CreateAccountAsync(807);
        await Assert.ThrowsAsync<ArgumentException>(() => _billing.ApplyAsync(807, Guid.NewGuid(), amount, "deposit"));
        Assert.Equal(0, (await _billing.GetAsync(807))!.Balance);
    }

    [Theory]
    [InlineData("paid", "Письмо счастья")]
    [InlineData("rejected", "Письмо горя")]
    public async Task Kafka_redelivery_saves_one_letter_for_the_correct_user(string outcome, string subject)
    {
        var repository = new NotificationRepository(_db);
        var order = new Order(Guid.NewGuid(), 808, 2500, outcome, outcome == "paid" ? null : "insufficient_funds", DateTimeOffset.UtcNow);
        var message = OrderEvent.Create(order, "streaming@example.com");
        await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => repository.SaveOrderAsync(message)));
        // Even a second event ID for the same completed order must not produce a second letter.
        await repository.SaveOrderAsync(message with { Id = Guid.NewGuid() });
        var letter = Assert.Single(await repository.ListAsync(order.UserId, order.Id));
        Assert.Equal(subject, letter.Subject);
        Assert.Equal(outcome, letter.Outcome);
        Assert.Equal("streaming@example.com", letter.Email);
        Assert.Empty(await repository.ListAsync(999, order.Id));
    }

    [Fact]
    public async Task Order_completion_and_outbox_are_atomic()
    {
        var repository = new OrderRepository(_db);
        var order = await repository.CreateAsync(809, 500, default);
        await using var connection = await _db.OpenAsync();
        await using var command = new NpgsqlCommand("SELECT COUNT(*) FROM outbox WHERE order_id=@id", connection);
        command.Parameters.AddWithValue("id", order.Id);
        Assert.Equal(0L, await command.ExecuteScalarAsync());
        await repository.CompleteAsync(order with { Status = "paid" }, "outbox@example.com", default);
        Assert.Equal("paid", (await repository.GetAsync(order.Id, order.UserId, default))!.Status);
        Assert.Equal(1L, await command.ExecuteScalarAsync());
        await Assert.ThrowsAsync<InvalidOperationException>(() => repository.CompleteAsync(order with { Status = "rejected" }, "outbox@example.com", default));
        Assert.Equal("paid", (await repository.GetAsync(order.Id, order.UserId, default))!.Status);
        Assert.Equal(1L, await command.ExecuteScalarAsync());
        Assert.Null(await repository.GetAsync(order.Id, 999, default));
    }
}
