using Npgsql;

namespace Blt.Orders.Api.Data;

public sealed class DbConnectionFactory(DatabaseOptions options)
{
    public async Task<NpgsqlConnection> OpenAsync(CancellationToken cancellationToken)
    {
        var connection = new NpgsqlConnection(options.ConnectionString);
        try
        {
            await connection.OpenAsync(cancellationToken);
            return connection;
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }
}
