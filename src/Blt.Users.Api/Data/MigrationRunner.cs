using Npgsql;

namespace Blt.Users.Api.Data;

public sealed class MigrationRunner(
    DbConnectionFactory connectionFactory,
    ILogger<MigrationRunner> logger)
{
    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        logger.LogInformation("{event}: migration Job started", "migration_started");
        Exception? lastException = null;

        for (var attempt = 1; attempt <= 30; attempt++)
        {
            try
            {
                await ApplyOnceAsync(cancellationToken);
                logger.LogInformation("{event}: migration Job completed", "migration_completed");
                return;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                lastException = exception;
                if (attempt == 30) break;
                logger.LogWarning(
                    exception,
                    "{event}: database unavailable; migration retry {attempt}/30.",
                    "migration_retry", attempt);
                await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
            }
        }

        throw new InvalidOperationException(
            "Database migrations failed after 30 attempts.",
            lastException);
    }

    private async Task ApplyOnceAsync(CancellationToken cancellationToken)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        await ExecuteAsync(
            connection,
            transaction,
            "SELECT pg_advisory_xact_lock(hashtext('blt-users-api-migrations'));",
            cancellationToken);

        await ExecuteAsync(
            connection,
            transaction,
            """
            CREATE TABLE IF NOT EXISTS schema_migrations
            (
                version    VARCHAR(128) PRIMARY KEY,
                applied_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
            );
            """,
            cancellationToken);

        var assembly = typeof(MigrationRunner).Assembly;
        foreach (var resource in assembly.GetManifestResourceNames()
                     .Where(name => name.StartsWith("Migrations.", StringComparison.Ordinal))
                     .Order(StringComparer.Ordinal))
        {
            var version = resource["Migrations.".Length..^".sql".Length];
            if (await ExistsAsync(connection, transaction, version, cancellationToken)) continue;
            await using var stream = assembly.GetManifestResourceStream(resource)!;
            using var reader = new StreamReader(stream);
            await ExecuteAsync(connection, transaction, await reader.ReadToEndAsync(cancellationToken), cancellationToken);
            logger.LogInformation("{event}: applied {version}", "migration_applied", version);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    private static async Task<bool> ExistsAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string version,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            "SELECT EXISTS (SELECT 1 FROM schema_migrations WHERE version = @version);";
        command.Parameters.AddWithValue("version", version);
        return (bool)(await command.ExecuteScalarAsync(cancellationToken) ?? false);
    }

    private static async Task ExecuteAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string sql,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
