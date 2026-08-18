using Npgsql;

namespace Blt.Users.Api.Data;

public sealed record DatabaseOptions(string ConnectionString)
{
    public static DatabaseOptions FromConfiguration(IConfiguration configuration)
    {
        var configuredConnectionString = configuration.GetConnectionString("UsersDb");
        if (!string.IsNullOrWhiteSpace(configuredConnectionString))
        {
            return new DatabaseOptions(configuredConnectionString);
        }

        var sslModeValue = configuration["Database:SslMode"] ?? "Disable";
        if (!Enum.TryParse<SslMode>(sslModeValue, ignoreCase: true, out var sslMode))
        {
            sslMode = SslMode.Disable;
        }

        var connectionString = new NpgsqlConnectionStringBuilder
        {
            Host = configuration["Database:Host"] ?? "localhost",
            Port = GetPort(configuration["Database:Port"]),
            Database = configuration["Database:Name"] ?? "users",
            Username = configuration["Database:Username"] ?? "users",
            Password = configuration["Database:Password"] ?? "users",
            SslMode = sslMode,
            Timeout = 5,
            CommandTimeout = 15,
            ApplicationName = "blt-users-api"
        }.ConnectionString;

        return new DatabaseOptions(connectionString);
    }

    private static int GetPort(string? configuredPort)
    {
        return int.TryParse(configuredPort, out var port) && port is > 0 and <= 65535
            ? port
            : 5432;
    }
}
