using Dapper;
using FishingMartPos.Data;
using FishingMartPos.Models;

namespace FishingMartPos.Repositories;

public sealed class SystemInfoRepository : ISystemInfoRepository
{
    private readonly IDbConnectionFactory _connectionFactory;

    public SystemInfoRepository(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<SystemInfo?> GetAsync()
    {
        using var connection = await _connectionFactory.CreateOpenConnectionAsync();
        const string sql = """
            SELECT app_version AS AppVersion, db_version AS DbVersion
            FROM system_info_tb
            LIMIT 1
            """;
        return await connection.QuerySingleOrDefaultAsync<SystemInfo>(sql);
    }
}
