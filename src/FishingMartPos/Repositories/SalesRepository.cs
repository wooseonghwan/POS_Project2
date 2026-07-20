using Dapper;
using FishingMartPos.Data;
using FishingMartPos.Models;

namespace FishingMartPos.Repositories;

public sealed class SalesRepository : ISalesRepository
{
    private readonly IDbConnectionFactory _connectionFactory;

    public SalesRepository(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<long> CreateSaleAsync(SaleHeader header, IReadOnlyList<SaleDetailLine> lines)
    {
        using var connection = _connectionFactory.CreateOpenConnection();
        using var transaction = connection.BeginTransaction();

        const string insertHeaderSql = """
            INSERT INTO sales_header_tb
                (pos_cd, sale_dt, staff_cd, total_amt, pay_type, cash_received, change_amt, van_approval_no, van_code, status)
            VALUES
                (@PosCd, @SaleDt, @StaffCd, @TotalAmt, @PayType, @CashReceived, @ChangeAmt, @VanApprovalNo, @VanCode, 'COMPLETE')
            """;
        await connection.ExecuteAsync(insertHeaderSql, header, transaction);
        long saleNo = await connection.QuerySingleAsync<long>("SELECT LAST_INSERT_ID()", transaction: transaction);

        const string insertDetailSql = """
            INSERT INTO sales_detail_tb (sale_no, line_no, barcode, product_name, qty, unit_price, line_amt)
            VALUES (@SaleNo, @LineNo, @Barcode, @ProductName, @Qty, @UnitPrice, @LineAmt)
            """;
        const string decrementStockSql = """
            UPDATE product_tb SET stock_qty = stock_qty - @Qty WHERE barcode = @Barcode
            """;

        int lineNo = 1;
        foreach (var line in lines)
        {
            await connection.ExecuteAsync(insertDetailSql, new
            {
                SaleNo = saleNo,
                LineNo = lineNo,
                line.Barcode,
                line.ProductName,
                line.Qty,
                UnitPrice = line.UnitPrice,
                LineAmt = line.LineAmt,
            }, transaction);

            await connection.ExecuteAsync(decrementStockSql, new { line.Qty, line.Barcode }, transaction);
            lineNo++;
        }

        transaction.Commit();
        return saleNo;
    }
}
