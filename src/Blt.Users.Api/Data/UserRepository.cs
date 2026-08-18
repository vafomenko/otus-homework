using System.Data.Common;
using Blt.Users.Api.Models;
using Npgsql;
using NpgsqlTypes;

namespace Blt.Users.Api.Data;

public sealed class UserRepository(DbConnectionFactory connectionFactory)
{
    public async Task<IReadOnlyList<UserResponse>> ListAsync(CancellationToken cancellationToken)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = SelectColumns + " ORDER BY id;";

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var users = new List<UserResponse>();
        while (await reader.ReadAsync(cancellationToken))
        {
            users.Add(ReadUser(reader));
        }

        return users;
    }

    public async Task<UserResponse?> GetAsync(long id, CancellationToken cancellationToken)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = SelectColumns + " WHERE id = @id;";
        AddBigint(command, "id", id);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadUser(reader) : null;
    }

    public async Task<UserResponse> CreateAsync(
        CreateUserRequest request,
        CancellationToken cancellationToken)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO users (username, first_name, last_name, email, phone)
            VALUES (@username, @first_name, @last_name, @email, @phone)
            RETURNING id, username, first_name, last_name, email, phone;
            """;
        AddText(command, "username", request.Username!);
        AddText(command, "first_name", request.FirstName!);
        AddText(command, "last_name", request.LastName!);
        AddText(command, "email", request.Email!);
        AddNullableText(command, "phone", request.Phone);

        try
        {
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            await reader.ReadAsync(cancellationToken);
            return ReadUser(reader);
        }
        catch (PostgresException exception) when (
            exception.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            throw new UserConflictException("username or email already exists.", exception);
        }
    }

    public async Task<UserResponse?> UpdateAsync(
        long id,
        UpdateUserRequest request,
        CancellationToken cancellationToken)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            UPDATE users
            SET username = COALESCE(@username, username),
                first_name = COALESCE(@first_name, first_name),
                last_name = COALESCE(@last_name, last_name),
                email = COALESCE(@email, email),
                phone = COALESCE(@phone, phone)
            WHERE id = @id
            RETURNING id, username, first_name, last_name, email, phone;
            """;
        AddBigint(command, "id", id);
        AddNullableText(command, "username", request.Username);
        AddNullableText(command, "first_name", request.FirstName);
        AddNullableText(command, "last_name", request.LastName);
        AddNullableText(command, "email", request.Email);
        AddNullableText(command, "phone", request.Phone);

        try
        {
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            return await reader.ReadAsync(cancellationToken) ? ReadUser(reader) : null;
        }
        catch (PostgresException exception) when (
            exception.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            throw new UserConflictException("username or email already exists.", exception);
        }
    }

    public async Task<bool> DeleteAsync(long id, CancellationToken cancellationToken)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM users WHERE id = @id;";
        AddBigint(command, "id", id);
        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }

    public async Task PingAsync(CancellationToken cancellationToken)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT 1;";
        await command.ExecuteScalarAsync(cancellationToken);
    }

    private const string SelectColumns =
        "SELECT id, username, first_name, last_name, email, phone FROM users";

    private static UserResponse ReadUser(DbDataReader reader)
    {
        return new UserResponse(
            reader.GetInt64(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetString(3),
            reader.GetString(4),
            reader.IsDBNull(5) ? null : reader.GetString(5));
    }

    private static void AddBigint(NpgsqlCommand command, string name, long value)
    {
        command.Parameters.Add(new NpgsqlParameter(name, NpgsqlDbType.Bigint) { Value = value });
    }

    private static void AddText(NpgsqlCommand command, string name, string value)
    {
        command.Parameters.Add(new NpgsqlParameter(name, NpgsqlDbType.Varchar) { Value = value });
    }

    private static void AddNullableText(NpgsqlCommand command, string name, string? value)
    {
        command.Parameters.Add(
            new NpgsqlParameter(name, NpgsqlDbType.Varchar)
            {
                Value = value is null ? DBNull.Value : value
            });
    }
}

public sealed class UserConflictException(string message, Exception innerException)
    : Exception(message, innerException);
