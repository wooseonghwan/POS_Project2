using Dapper;
using FishingMartPos.Data;
using FishingMartPos.Models;

namespace FishingMartPos.Repositories;

public sealed class PosTerminalRepository : IPosTerminalRepository
{
    private readonly IDbConnectionFactory _connectionFactory;

    public PosTerminalRepository(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<IReadOnlyList<PosTerminal>> GetAllAsync()
    {
        using var connection = await _connectionFactory.CreateOpenConnectionAsync();
        const string sql = """
            SELECT pos_cd AS PosCode, pos_name AS PosName
            FROM pos_terminal_tb
            ORDER BY pos_cd
            """;

        var result = await connection.QueryAsync<PosTerminal>(sql);
        return result.ToList();
    }
}
