using Blt.Users.Api.Models;
using Npgsql;

namespace Blt.Users.Api.Data;

public sealed record UserCredential(long UserId, string PasswordHash);

public sealed class AuthRepository(DbConnectionFactory connectionFactory)
{
    public async Task<UserResponse> RegisterAsync(CreateUserRequest profile, string passwordHash, CancellationToken cancellationToken)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO users (username, first_name, last_name, email, phone)
            VALUES (@username, @first_name, @last_name, @email, @phone)
            RETURNING id;
            """;
        command.Parameters.AddWithValue("username", profile.Username!);
        command.Parameters.AddWithValue("first_name", profile.FirstName!);
        command.Parameters.AddWithValue("last_name", profile.LastName!);
        command.Parameters.AddWithValue("email", profile.Email!);
        command.Parameters.AddWithValue("phone", NpgsqlTypes.NpgsqlDbType.Varchar, (object?)profile.Phone ?? DBNull.Value);
        try
        {
            var id = (long)(await command.ExecuteScalarAsync(cancellationToken))!;
            command.Parameters.Clear();
            command.CommandText = "INSERT INTO user_credentials (user_id, password_hash) VALUES (@id, @hash);";
            command.Parameters.AddWithValue("id", id);
            command.Parameters.AddWithValue("hash", passwordHash);
            await command.ExecuteNonQueryAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new UserResponse(id, profile.Username!, profile.FirstName!, profile.LastName!, profile.Email!, profile.Phone);
        }
        catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            throw new UserConflictException("username or email already exists.", exception);
        }
    }

    public async Task<UserCredential?> FindCredentialAsync(string username, CancellationToken cancellationToken)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT c.user_id, c.password_hash FROM user_credentials c
            JOIN users u ON u.id = c.user_id WHERE u.username = @username;
            """;
        command.Parameters.AddWithValue("username", username);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? new UserCredential(reader.GetInt64(0), reader.GetString(1)) : null;
    }

    public async Task CreateSessionAsync(long userId, string tokenHash, DateTimeOffset expiresAt, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            DELETE FROM user_sessions WHERE expires_at <= @now;
            INSERT INTO user_sessions (token_hash, user_id, expires_at) VALUES (@hash, @id, @expires);
            """;
        command.Parameters.AddWithValue("hash", tokenHash);
        command.Parameters.AddWithValue("id", userId);
        command.Parameters.AddWithValue("expires", expiresAt);
        command.Parameters.AddWithValue("now", now);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<long?> FindSessionAsync(string tokenHash, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT user_id FROM user_sessions WHERE token_hash = @hash AND expires_at > @now;";
        command.Parameters.AddWithValue("hash", tokenHash);
        command.Parameters.AddWithValue("now", now);
        return await command.ExecuteScalarAsync(cancellationToken) is long id ? id : null;
    }

    public async Task RevokeSessionAsync(string tokenHash, CancellationToken cancellationToken)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM user_sessions WHERE token_hash = @hash;";
        command.Parameters.AddWithValue("hash", tokenHash);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
