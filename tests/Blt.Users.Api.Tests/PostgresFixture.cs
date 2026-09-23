using System.Diagnostics;
using Blt.Users.Api.Data;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using Xunit;

namespace Blt.Users.Api.Tests;

// Each test class owns a disposable database; no developer or cluster database is used.
public sealed class PostgresFixture : IAsyncLifetime
{
    private readonly string _containerName = "blt-hw7-tests-" + Guid.NewGuid().ToString("N");
    private bool _started;
    public string ConnectionString { get; private set; } = "";
    public WebApplicationFactory<Program> App { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        try
        {
            var password = Guid.NewGuid().ToString("N");
            await DockerAsync("run", "--rm", "-d", "--name", _containerName,
                "-e", "POSTGRES_USER=users", "-e", "POSTGRES_DB=users", "-e", $"POSTGRES_PASSWORD={password}",
                "-p", "127.0.0.1::5432", "postgres:17.6-alpine");
            _started = true;
            var port = (await DockerAsync("port", _containerName, "5432/tcp")).Trim().Split(':')[^1];
            ConnectionString = new NpgsqlConnectionStringBuilder
            {
                Host = "127.0.0.1", Port = int.Parse(port), Database = "users", Username = "users",
                Password = password, Timeout = 2, CommandTimeout = 5
            }.ConnectionString;
            await WaitForDatabaseAsync();
            App = CreateApp();
            // Exercise upgrading a real homework-6 schema, preserving its existing profile.
            await using (var connection = new NpgsqlConnection(ConnectionString))
            {
                await connection.OpenAsync();
                await using var stream = typeof(MigrationRunner).Assembly.GetManifestResourceStream("Migrations.001_users.sql")!;
                using var reader = new StreamReader(stream);
                await using var command = new NpgsqlCommand(await reader.ReadToEndAsync(), connection);
                await command.ExecuteNonQueryAsync();
                command.CommandText = """
                    INSERT INTO users (username, first_name, last_name, email)
                    VALUES ('legacy', 'Legacy', 'Profile', 'legacy@example.com');
                    """;
                await command.ExecuteNonQueryAsync();
            }
            await App.Services.GetRequiredService<MigrationRunner>().RunAsync();
            await App.Services.GetRequiredService<MigrationRunner>().RunAsync();
        }
        catch
        {
            await DisposeAsync();
            throw;
        }
    }

    public WebApplicationFactory<Program> CreateApp(TimeProvider? time = null) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.RemoveAll<DatabaseOptions>();
            services.AddSingleton(new DatabaseOptions(ConnectionString));
            if (time is not null)
            {
                services.RemoveAll<TimeProvider>();
                services.AddSingleton(time);
            }
        }));

    private async Task WaitForDatabaseAsync()
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                await using var connection = new NpgsqlConnection(ConnectionString);
                await connection.OpenAsync();
                return;
            }
            catch (NpgsqlException) when (attempt < 60)
            {
                await Task.Delay(250);
            }
        }
    }

    public async Task DisposeAsync()
    {
        if (App is not null) await App.DisposeAsync();
        NpgsqlConnection.ClearAllPools();
        if (_started)
        {
            await DockerAsync("rm", "-f", _containerName);
            _started = false;
        }
    }

    private static async Task<string> DockerAsync(params string[] args)
    {
        var start = new ProcessStartInfo("docker") { RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
        foreach (var arg in args) start.ArgumentList.Add(arg);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Docker must be running for PostgreSQL integration tests.");
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        if (process.ExitCode != 0) throw new InvalidOperationException("Docker integration test setup failed: " + await error);
        return await output;
    }
}
