using Dapper;
using FishingMartPos.Data;
using FishingMartPos.Models;

namespace FishingMartPos.Repositories;

public sealed class ReceiptConfigRepository : IReceiptConfigRepository
{
    private readonly IDbConnectionFactory _connectionFactory;

    public ReceiptConfigRepository(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<ReceiptConfig?> GetAsync(string posCd)
    {
        using var connection = await _connectionFactory.CreateOpenConnectionAsync();
        const string sql = """
            SELECT pos_cd AS PosCd, header_text AS HeaderText, footer_text AS FooterText
            FROM receipt_config_tb
            WHERE pos_cd = @PosCd
            LIMIT 1
            """;
        return await connection.QuerySingleOrDefaultAsync<ReceiptConfig>(sql, new { PosCd = posCd });
    }

    public async Task SaveAsync(ReceiptConfig config)
    {
        using var connection = await _connectionFactory.CreateOpenConnectionAsync();
        const string sql = """
            INSERT INTO receipt_config_tb (pos_cd, header_text, footer_text)
            VALUES (@PosCd, @HeaderText, @FooterText)
            ON DUPLICATE KEY UPDATE
                header_text = VALUES(header_text), footer_text = VALUES(footer_text)
            """;
        await connection.ExecuteAsync(sql, config);
    }
}
