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
        using var connection = await _connectionFactory.CreateOpenConnectionAsync();
        using var transaction = connection.BeginTransaction();

        const string insertHeaderSql = """
            INSERT INTO sales_header_tb
                (pos_cd, sale_dt, staff_cd, total_amt, pay_type, cash_received, change_amt, van_approval_no, van_code, installment_months, status)
            VALUES
                (@PosCd, @SaleDt, @StaffCd, @TotalAmt, @PayType, @CashReceived, @ChangeAmt, @VanApprovalNo, @VanCode, @InstallmentMonths, 'COMPLETE')
            """;
        await connection.ExecuteAsync(insertHeaderSql, header, transaction);
        long saleNo = await connection.QuerySingleAsync<long>("SELECT LAST_INSERT_ID()", transaction: transaction);

        if (lines.Count > 0)
        {
            var detailParams = new DynamicParameters();
            detailParams.Add("SaleNo", saleNo);
            var valueRows = new List<string>(lines.Count);
            int lineNo = 1;
            foreach (var line in lines)
            {
                valueRows.Add($"(@SaleNo, @LineNo{lineNo}, @Barcode{lineNo}, @ProductName{lineNo}, @Qty{lineNo}, @UnitPrice{lineNo}, @LineAmt{lineNo})");
                detailParams.Add($"LineNo{lineNo}", lineNo);
                detailParams.Add($"Barcode{lineNo}", line.Barcode);
                detailParams.Add($"ProductName{lineNo}", line.ProductName);
                detailParams.Add($"Qty{lineNo}", line.Qty);
                detailParams.Add($"UnitPrice{lineNo}", line.UnitPrice);
                detailParams.Add($"LineAmt{lineNo}", line.LineAmt);
                lineNo++;
            }

            string insertDetailSql = $"""
                INSERT INTO sales_detail_tb (sale_no, line_no, barcode, product_name, qty, unit_price, line_amt)
                VALUES {string.Join(", ", valueRows)}
                """;
            await connection.ExecuteAsync(insertDetailSql, detailParams, transaction);

            const string decrementStockSql = """
                UPDATE product_tb SET stock_qty = stock_qty - @Qty WHERE barcode = @Barcode
                """;
            foreach (var line in lines)
            {
                await connection.ExecuteAsync(decrementStockSql, new { line.Qty, line.Barcode }, transaction);
            }
        }

        transaction.Commit();
        return saleNo;
    }

    public async Task<IReadOnlyList<SaleHeader>> GetCompletedSalesAsync(DateTime from, DateTime to)
    {
        using var connection = await _connectionFactory.CreateOpenConnectionAsync();
        const string sql = """
            SELECT pos_cd AS PosCd, sale_dt AS SaleDt, staff_cd AS StaffCd, total_amt AS TotalAmt,
                   pay_type AS PayType, cash_received AS CashReceived, change_amt AS ChangeAmt,
                   van_approval_no AS VanApprovalNo, van_code AS VanCode, installment_months AS InstallmentMonths
            FROM sales_header_tb
            WHERE sale_dt >= @From AND sale_dt < @To AND status = 'COMPLETE'
            """;
        var rows = await connection.QueryAsync<SaleHeader>(sql, new { From = from, To = to });
        return rows.ToList();
    }
}
