using Dapper;
using FishingMartPos.Data;
using FishingMartPos.Models;

namespace FishingMartPos.Repositories;

public sealed class VanConfigRepository : IVanConfigRepository
{
    private readonly IDbConnectionFactory _connectionFactory;

    public VanConfigRepository(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<IReadOnlyList<VanConfigRow>> GetByPosCodeAsync(string posCd)
    {
        using var connection = await _connectionFactory.CreateOpenConnectionAsync();
        const string sql = """
            SELECT pos_cd AS PosCd, van_code AS VanCode, pay_type AS PayType,
                   terminal_id AS TerminalId, business_no AS BusinessNo
            FROM van_config_tb
            WHERE pos_cd = @PosCd
            """;
        var rows = await connection.QueryAsync<VanConfigRow>(sql, new { PosCd = posCd });
        return rows.ToList();
    }
}
