namespace Blt.Users.Api.Data;

public sealed class DatabaseReadiness(DbConnectionFactory connectionFactory)
{
    public async Task CheckAsync(CancellationToken cancellationToken)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT 1 FROM users LIMIT 1;";
        await command.ExecuteScalarAsync(cancellationToken);
    }
}
