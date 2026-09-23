namespace Blt.Users.Api.Data;

public sealed class DatabaseReadiness(DbConnectionFactory connectionFactory)
{
    public async Task CheckAsync(CancellationToken cancellationToken)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT 1 FROM users LIMIT 1; SELECT 1 FROM user_credentials LIMIT 1; SELECT 1 FROM user_sessions LIMIT 1;";
        await command.ExecuteScalarAsync(cancellationToken);
    }
}
