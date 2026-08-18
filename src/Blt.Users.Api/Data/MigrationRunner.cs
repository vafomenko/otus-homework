using Npgsql;

namespace Blt.Users.Api.Data;

public sealed class MigrationRunner(
    DbConnectionFactory connectionFactory,
    ILogger<MigrationRunner> logger)
{
    private const string MigrationVersion = "001_users";

    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        Exception? lastException = null;

        for (var attempt = 1; attempt <= 30; attempt++)
        {
            try
            {
                await ApplyOnceAsync(cancellationToken);
                logger.LogInformation("Database migrations completed.");
                return;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception) when (attempt < 30)
            {
                lastException = exception;
                logger.LogWarning(
                    exception,
                    "Database is unavailable; migration retry {Attempt}/30.",
                    attempt);
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

        var alreadyApplied = await ExistsAsync(connection, transaction, cancellationToken);
        if (!alreadyApplied)
        {
            await ExecuteAsync(
                connection,
                transaction,
                """
                CREATE TABLE IF NOT EXISTS users
                (
                    id         BIGSERIAL PRIMARY KEY,
                    username   VARCHAR(256) NOT NULL,
                    first_name VARCHAR(256) NOT NULL,
                    last_name  VARCHAR(256) NOT NULL,
                    email      VARCHAR(320) NOT NULL,
                    phone      VARCHAR(64)
                );
                """,
                cancellationToken);

            await ExecuteAsync(
                connection,
                transaction,
                "CREATE UNIQUE INDEX IF NOT EXISTS users_username_uq ON users (username);",
                cancellationToken);
            await ExecuteAsync(
                connection,
                transaction,
                "CREATE UNIQUE INDEX IF NOT EXISTS users_email_uq ON users (email);",
                cancellationToken);
            await ExecuteAsync(
                connection,
                transaction,
                "INSERT INTO schema_migrations(version) VALUES ('001_users');",
                cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    private static async Task<bool> ExistsAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            "SELECT EXISTS (SELECT 1 FROM schema_migrations WHERE version = @version);";
        command.Parameters.AddWithValue("version", MigrationVersion);
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
