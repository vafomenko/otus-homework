using System.Reflection;
using Microsoft.Extensions.Configuration;
using Npgsql;

namespace Blt.Streaming;

public sealed class Database : IAsyncDisposable
{
    private readonly NpgsqlDataSource _source;

    public Database(IConfiguration config) : this(config.GetConnectionString("Database") ??
        new NpgsqlConnectionStringBuilder
        {
            Host = config["Database:Host"] ?? "localhost",
            Port = config.GetValue("Database:Port", 5432),
            Database = config["Database:Name"] ?? "app",
            Username = config["Database:Username"] ?? "app",
            Password = config["Database:Password"] ?? "change-me",
            Timeout = 5, CommandTimeout = 15
        }.ConnectionString) { }

    public Database(string connectionString) => _source = NpgsqlDataSource.Create(connectionString);
    public ValueTask<NpgsqlConnection> OpenAsync(CancellationToken ct = default) => _source.OpenConnectionAsync(ct);
    public ValueTask DisposeAsync() => _source.DisposeAsync();

    public async Task MigrateAsync(Assembly assembly, CancellationToken ct = default)
    {
        await using var connection = await OpenAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT pg_advisory_xact_lock(hashtext('blt-streaming-migrations'))";
        await command.ExecuteNonQueryAsync(ct);
        foreach (var name in assembly.GetManifestResourceNames().Where(n => n.EndsWith(".sql", StringComparison.Ordinal)).Order())
        {
            await using var stream = assembly.GetManifestResourceStream(name)!;
            using var reader = new StreamReader(stream);
            command.CommandText = await reader.ReadToEndAsync(ct);
            await command.ExecuteNonQueryAsync(ct);
        }
        await transaction.CommitAsync(ct);
    }

    public async Task PingAsync(CancellationToken ct)
    {
        await using var connection = await OpenAsync(ct);
        await using var command = new NpgsqlCommand("SELECT 1", connection);
        await command.ExecuteScalarAsync(ct);
    }
}
