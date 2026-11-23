using System.Diagnostics;
using Blt.Billing.Api;
using Blt.Orders.Api;
using Blt.Streaming;
using Npgsql;
using Xunit;

namespace Blt.Saga.Tests;

public sealed class SagaFixture : IAsyncLifetime
{
    private readonly string _container = "blt-hw9-tests-" + Guid.NewGuid().ToString("N");
    private readonly List<Database> _databases = [];
    private bool _started;
    public Database OrdersDb { get; private set; } = null!;
    public Database BillingDb { get; private set; } = null!;
    public Database StockDb { get; private set; } = null!;
    public Database DeliveryDb { get; private set; } = null!;
    public OrderRepository Orders { get; private set; } = null!;
    public BillingRepository Billing { get; private set; } = null!;
    public ReservationRepository Stock { get; private set; } = null!;
    public ReservationRepository Delivery { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        try
        {
            var password = Guid.NewGuid().ToString("N");
            await DockerAsync("run", "--rm", "-d", "--name", _container, "-e", "POSTGRES_USER=saga", "-e",
                "POSTGRES_PASSWORD=" + password, "-p", "127.0.0.1::5432", "postgres:17.6-alpine");
            _started = true;
            var port = int.Parse((await DockerAsync("port", _container, "5432/tcp")).Trim().Split(':')[^1]);
            var settings = new NpgsqlConnectionStringBuilder
                { Host = "127.0.0.1", Port = port, Username = "saga", Password = password, Database = "postgres", Timeout = 2, CommandTimeout = 10 };
            await using var admin = new NpgsqlConnection(settings.ConnectionString);
            for (var attempt = 0; ; attempt++)
            {
                try { await admin.OpenAsync(); break; }
                catch (NpgsqlException) when (attempt < 60) { await Task.Delay(250); }
            }
            foreach (var name in new[] { "orders", "billing", "warehouse", "delivery" })
            {
                await using var command = new NpgsqlCommand($"CREATE DATABASE {name}", admin);
                await command.ExecuteNonQueryAsync();
                settings.Database = name;
                _databases.Add(new Database(settings.ConnectionString));
            }
            (OrdersDb, BillingDb, StockDb, DeliveryDb) = (_databases[0], _databases[1], _databases[2], _databases[3]);
            await OrdersDb.MigrateAsync(typeof(OrderRepository).Assembly);
            await BillingDb.MigrateAsync(typeof(BillingRepository).Assembly);
            await StockDb.MigrateAsync(typeof(Blt.Warehouse.Api.Program).Assembly);
            await DeliveryDb.MigrateAsync(typeof(Blt.Delivery.Api.Program).Assembly);
            Orders = new OrderRepository(OrdersDb);
            Billing = new BillingRepository(BillingDb);
            Stock = new ReservationRepository(StockDb, "out_of_stock");
            Delivery = new ReservationRepository(DeliveryDb, "courier_unavailable");
        }
        catch { await DisposeAsync(); throw; }
    }

    public async Task SeedAsync(string resource, int stock = 10, int delivery = 1)
    {
        foreach (var (db, available) in new[] { (StockDb, stock), (DeliveryDb, delivery) })
        {
            await using var connection = await db.OpenAsync();
            await using var command = new NpgsqlCommand("""
                INSERT INTO resources(id, available, courier_id, starts_at, ends_at)
                VALUES (@id, @available, @id, '2030-01-15 10:00:00+00', '2030-01-15 12:00:00+00')
                """, connection);
            command.Parameters.AddWithValue("id", resource);
            command.Parameters.AddWithValue("available", available);
            await command.ExecuteNonQueryAsync();
        }
    }

    public async Task DisposeAsync()
    {
        foreach (var db in _databases) await db.DisposeAsync();
        if (_started) { await DockerAsync("rm", "-f", _container); _started = false; }
    }

    private static async Task<string> DockerAsync(params string[] args)
    {
        var start = new ProcessStartInfo("docker") { RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
        foreach (var arg in args) start.ArgumentList.Add(arg);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Docker is required.");
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        if (process.ExitCode != 0) throw new InvalidOperationException(await error);
        return await output;
    }
}
