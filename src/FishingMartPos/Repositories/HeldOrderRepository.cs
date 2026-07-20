using Dapper;
using FishingMartPos.Data;
using FishingMartPos.Models;

namespace FishingMartPos.Repositories;

public sealed class HeldOrderRepository : IHeldOrderRepository
{
    private readonly IDbConnectionFactory _connectionFactory;

    public HeldOrderRepository(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<long> HoldAsync(string posCd, string staffCd, IReadOnlyList<HeldOrderLine> lines)
    {
        using var connection = _connectionFactory.CreateOpenConnection();
        using var transaction = connection.BeginTransaction();

        const string insertHeaderSql = """
            INSERT INTO held_order_tb (pos_cd, staff_cd, held_at, status)
            VALUES (@PosCd, @StaffCd, NOW(), 'HELD')
            """;
        await connection.ExecuteAsync(insertHeaderSql, new { PosCd = posCd, StaffCd = staffCd }, transaction);
        long holdNo = await connection.QuerySingleAsync<long>("SELECT LAST_INSERT_ID()", transaction: transaction);

        const string insertLineSql = """
            INSERT INTO held_order_detail_tb (hold_no, line_no, barcode, product_name, qty, unit_price)
            VALUES (@HoldNo, @LineNo, @Barcode, @ProductName, @Qty, @UnitPrice)
            """;

        int lineNo = 1;
        foreach (var line in lines)
        {
            await connection.ExecuteAsync(insertLineSql, new
            {
                HoldNo = holdNo,
                LineNo = lineNo,
                line.Barcode,
                line.ProductName,
                line.Qty,
                line.UnitPrice,
            }, transaction);
            lineNo++;
        }

        transaction.Commit();
        return holdNo;
    }

    public async Task<IReadOnlyList<(long HoldNo, DateTime HeldAt, decimal Total)>> GetHeldAsync(string posCd)
    {
        using var connection = _connectionFactory.CreateOpenConnection();
        const string sql = """
            SELECT h.hold_no AS HoldNo, h.held_at AS HeldAt,
                   COALESCE(SUM(d.qty * d.unit_price), 0) AS Total
            FROM held_order_tb h
            LEFT JOIN held_order_detail_tb d ON d.hold_no = h.hold_no
            WHERE h.pos_cd = @PosCd AND h.status = 'HELD'
            GROUP BY h.hold_no, h.held_at
            ORDER BY h.held_at
            """;

        var rows = await connection.QueryAsync<(long HoldNo, DateTime HeldAt, decimal Total)>(sql, new { PosCd = posCd });
        return rows.ToList();
    }

    public async Task<IReadOnlyList<HeldOrderLine>> GetLinesAsync(long holdNo)
    {
        using var connection = _connectionFactory.CreateOpenConnection();
        const string sql = """
            SELECT barcode AS Barcode, product_name AS ProductName, qty AS Qty, unit_price AS UnitPrice
            FROM held_order_detail_tb
            WHERE hold_no = @HoldNo
            ORDER BY line_no
            """;

        var result = await connection.QueryAsync<HeldOrderLine>(sql, new { HoldNo = holdNo });
        return result.ToList();
    }

    public async Task DeleteAsync(long holdNo)
    {
        using var connection = _connectionFactory.CreateOpenConnection();
        using var transaction = connection.BeginTransaction();
        await connection.ExecuteAsync("DELETE FROM held_order_detail_tb WHERE hold_no = @HoldNo", new { HoldNo = holdNo }, transaction);
        await connection.ExecuteAsync("DELETE FROM held_order_tb WHERE hold_no = @HoldNo", new { HoldNo = holdNo }, transaction);
        transaction.Commit();
    }
}
