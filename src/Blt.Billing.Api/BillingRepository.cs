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
        await using var command = new NpgsqlCommand("SELECT balance FROM accounts WHERE user_id=@user FOR UPDATE", connection, tx);
        command.Parameters.AddWithValue("user", userId);
        if (await command.ExecuteScalarAsync(ct) is not long balance) return null;
        if (kind == "charge")
        {
            command.CommandText = "SELECT amount FROM charge_refunds WHERE user_id=@user AND order_id=@op";
            command.Parameters.AddWithValue("op", operationId);
            if (await command.ExecuteScalarAsync(ct) is long refundedAmount)
            {
                if (refundedAmount != amount) throw new OperationConflictException();
                return new Operation(operationId, userId, amount, kind, "cancelled", balance, "saga_compensated");
            }
            command.Parameters.RemoveAt("op");
        }
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
        command.CommandText = """
            SELECT COALESCE(SUM(o.amount), 0)::bigint FROM operations o
            WHERE o.user_id=@user AND o.kind='charge' AND o.status='succeeded'
              AND NOT EXISTS (SELECT 1 FROM charge_refunds r WHERE r.user_id=o.user_id AND r.order_id=o.operation_id)
            """;
        var refundable = kind == "deposit" ? (long)(await command.ExecuteScalarAsync(ct))! : 0;
        string? reason = kind == "deposit"
            ? (balance > 9_000_000_000_000_000 - amount - refundable ? "balance_limit" : null)
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

    public async Task<PaymentState?> PaymentAsync(long userId, Guid orderId, CancellationToken ct = default)
    {
        await using var connection = await db.OpenAsync(ct);
        await using var command = new NpgsqlCommand("""
            SELECT COALESCE(r.amount, o.amount),
              CASE WHEN r.order_id IS NOT NULL THEN CASE WHEN r.refunded_amount>0 THEN 'refunded' ELSE 'cancelled' END ELSE o.status END,
              COALESCE(r.refunded_amount, 0)
            FROM (SELECT * FROM operations WHERE user_id=@user AND operation_id=@id AND kind='charge') o
            FULL JOIN (SELECT * FROM charge_refunds WHERE user_id=@user AND order_id=@id) r ON TRUE
            """, connection);
        command.Parameters.AddWithValue("user", userId);
        command.Parameters.AddWithValue("id", orderId);
        await using var reader = await command.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? new PaymentState(orderId, reader.GetInt64(0), reader.GetString(1), reader.GetInt64(2)) : null;
    }

    public async Task<PaymentState?> RefundAsync(ChargeRequest request, CancellationToken ct = default)
    {
        if (!Money.IsValid(request.Amount) || request.OrderId == Guid.Empty || request.UserId <= 0) throw new ArgumentException("Invalid refund.");
        await using var connection = await db.OpenAsync(ct);
        await using var tx = await connection.BeginTransactionAsync(ct);
        await using var command = new NpgsqlCommand("SELECT balance FROM accounts WHERE user_id=@user FOR UPDATE", connection, tx);
        command.Parameters.AddWithValue("user", request.UserId);
        if (await command.ExecuteScalarAsync(ct) is not long balance) return null;
        command.Parameters.AddWithValue("id", request.OrderId);
        command.CommandText = "SELECT amount, refunded_amount FROM charge_refunds WHERE user_id=@user AND order_id=@id";
        await using (var reader = await command.ExecuteReaderAsync(ct))
        {
            if (await reader.ReadAsync(ct))
            {
                if (reader.GetInt64(0) != request.Amount) throw new OperationConflictException();
                var refunded = reader.GetInt64(1);
                return new PaymentState(request.OrderId, request.Amount, refunded > 0 ? "refunded" : "cancelled", refunded);
            }
        }
        long refund = 0;
        command.CommandText = "SELECT amount, kind, status FROM operations WHERE user_id=@user AND operation_id=@id";
        await using (var reader = await command.ExecuteReaderAsync(ct))
        {
            if (await reader.ReadAsync(ct))
            {
                if (reader.GetInt64(0) != request.Amount || reader.GetString(1) != "charge") throw new OperationConflictException();
                if (reader.GetString(2) == "succeeded") refund = request.Amount;
            }
        }
        command.CommandText = "UPDATE accounts SET balance=@balance WHERE user_id=@user";
        command.Parameters.AddWithValue("balance", balance + refund);
        await command.ExecuteNonQueryAsync(ct);
        command.CommandText = "INSERT INTO charge_refunds(user_id, order_id, amount, refunded_amount) VALUES (@user, @id, @amount, @refund)";
        command.Parameters.AddWithValue("amount", request.Amount);
        command.Parameters.AddWithValue("refund", refund);
        await command.ExecuteNonQueryAsync(ct);
        await tx.CommitAsync(ct);
        return new PaymentState(request.OrderId, request.Amount, refund > 0 ? "refunded" : "cancelled", refund);
    }
}
