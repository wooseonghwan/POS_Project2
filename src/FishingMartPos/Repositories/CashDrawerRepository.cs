using Dapper;
using FishingMartPos.Data;
using FishingMartPos.Models;

namespace FishingMartPos.Repositories;

public sealed class CashDrawerRepository : ICashDrawerRepository
{
    private readonly IDbConnectionFactory _connectionFactory;

    public CashDrawerRepository(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<CashDrawerEntry?> GetAsync(string posCd, DateTime businessDate)
    {
        using var connection = await _connectionFactory.CreateOpenConnectionAsync();
        const string sql = """
            SELECT pos_cd AS PosCd, business_date AS BusinessDate, opening_amount AS OpeningAmount, staff_cd AS StaffCd
            FROM cash_drawer_tb
            WHERE pos_cd = @PosCd AND business_date = @BusinessDate
            LIMIT 1
            """;
        return await connection.QuerySingleOrDefaultAsync<CashDrawerEntry>(
            sql, new { PosCd = posCd, BusinessDate = businessDate.Date });
    }

    public async Task SaveOpeningAmountAsync(CashDrawerEntry entry)
    {
        using var connection = await _connectionFactory.CreateOpenConnectionAsync();
        const string sql = """
            INSERT INTO cash_drawer_tb (pos_cd, business_date, opening_amount, staff_cd)
            VALUES (@PosCd, @BusinessDate, @OpeningAmount, @StaffCd)
            ON DUPLICATE KEY UPDATE
                opening_amount = VALUES(opening_amount), staff_cd = VALUES(staff_cd)
            """;
        await connection.ExecuteAsync(sql, new
        {
            entry.PosCd,
            BusinessDate = entry.BusinessDate.Date,
            entry.OpeningAmount,
            entry.StaffCd,
        });
    }
}
