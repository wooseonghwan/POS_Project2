using System.Data;
using FishingMartPos.Configuration;
using MySqlConnector;

namespace FishingMartPos.Data;

public sealed class MySqlConnectionFactory : IDbConnectionFactory
{
    private readonly string _connectionString;

    public MySqlConnectionFactory(AppConfig config)
    {
        _connectionString = config.ConnectionString;
    }

    public IDbConnection CreateOpenConnection()
    {
        var connection = new MySqlConnection(_connectionString);
        connection.Open();
        return connection;
    }
}
