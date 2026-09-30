using Blt.Streaming;
using Npgsql;

namespace Blt.Billing.Api;

public sealed class OperationConflictException : Exception;

public sealed class BillingRepository(Database db)
{
    public async Task CreateAccountAsync(long userId, CancellationToken ct = default)
    {
        await using var connection = await db.OpenAsync(ct);
        await using var command = new NpgsqlCommand("INSERT INTO accounts(user_id) VALUES (@id) ON CONFLICT DO NOTHING", connection);
        command.Parameters.AddWithValue("id", userId);
        await command.ExecuteNonQueryAsync(ct);
    }

    public async Task<Account?> GetAsync(long userId, CancellationToken ct = default)
    {
        await using var connection = await db.OpenAsync(ct);
        await using var command = new NpgsqlCommand("SELECT balance FROM accounts WHERE user_id=@id", connection);
        command.Parameters.AddWithValue("id", userId);
        var balance = await command.ExecuteScalarAsync(ct);
        return balance is long value ? new Account(userId, value) : null;
    }

    public async Task<Operation?> ApplyAsync(long userId, Guid operationId, long amount, string kind, CancellationToken ct = default)
    {
        if (!Money.IsValid(amount) || operationId == Guid.Empty || kind is not ("deposit" or "withdrawal" or "charge"))
            throw new ArgumentException("Invalid money operation.");
        await using var connection = await db.OpenAsync(ct);
        await using var tx = await connection.BeginTransactionAsync(ct);
        // Serialize operations on this account, including duplicate request checks.
        await using var command = new NpgsqlCommand("SELECT balance FROM accounts WHERE user_id=@user FOR UPDATE", connection, tx);
        command.Parameters.AddWithValue("user", userId);
        if (await command.ExecuteScalarAsync(ct) is not long balance) return null;
        command.CommandText = "SELECT amount, kind, status, balance, reason FROM operations WHERE user_id=@user AND operation_id=@op";
        command.Parameters.AddWithValue("op", operationId);
        await using (var reader = await command.ExecuteReaderAsync(ct))
        {
            if (await reader.ReadAsync(ct))
            {
                if (reader.GetInt64(0) != amount || reader.GetString(1) != kind) throw new OperationConflictException();
                return new Operation(operationId, userId, amount, kind, reader.GetString(2), reader.GetInt64(3), reader.IsDBNull(4) ? null : reader.GetString(4));
            }
        }
        string? reason = kind == "deposit"
            ? (balance > 9_000_000_000_000_000 - amount ? "balance_limit" : null)
            : (balance < amount ? "insufficient_funds" : null);
        var status = reason is null ? "succeeded" : "rejected";
        if (reason is null)
        {
            balance += kind == "deposit" ? amount : -amount;
            command.CommandText = "UPDATE accounts SET balance=@balance WHERE user_id=@user";
            command.Parameters.AddWithValue("balance", balance);
            await command.ExecuteNonQueryAsync(ct);
        }
        else command.Parameters.AddWithValue("balance", balance);
        command.CommandText = """
            INSERT INTO operations(user_id, operation_id, amount, kind, status, balance, reason)
            VALUES (@user, @op, @amount, @kind, @status, @balance, @reason)
            """;
        command.Parameters.AddWithValue("amount", amount);
        command.Parameters.AddWithValue("kind", kind);
        command.Parameters.AddWithValue("status", status);
        command.Parameters.AddWithValue("reason", NpgsqlTypes.NpgsqlDbType.Text, (object?)reason ?? DBNull.Value);
        await command.ExecuteNonQueryAsync(ct);
        await tx.CommitAsync(ct);
        return new Operation(operationId, userId, amount, kind, status, balance, reason);
    }
}
