using Dapper;
using FishingMartPos.Data;
using FishingMartPos.Models;

namespace FishingMartPos.Repositories;

public sealed class SalesRepository : ISalesRepository
{
    private const string HeaderColumns = """
        sale_no AS SaleNo, pos_cd AS PosCd, sale_dt AS SaleDt, staff_cd AS StaffCd, total_amt AS TotalAmt,
        pay_type AS PayType, cash_received AS CashReceived, change_amt AS ChangeAmt,
        van_approval_no AS VanApprovalNo, van_code AS VanCode, installment_months AS InstallmentMonths,
        cash_receipt_type AS CashReceiptType, cash_receipt_merchant AS CashReceiptMerchant,
        cash_receipt_approval_no AS CashReceiptApprovalNo, cash_receipt_approval_date AS CashReceiptApprovalDate,
        status AS Status
        """;

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
                (pos_cd, sale_dt, staff_cd, total_amt, pay_type, cash_received, change_amt, van_approval_no, van_code, installment_months, cash_receipt_type, cash_receipt_merchant, cash_receipt_approval_no, cash_receipt_approval_date, status)
            VALUES
                (@PosCd, @SaleDt, @StaffCd, @TotalAmt, @PayType, @CashReceived, @ChangeAmt, @VanApprovalNo, @VanCode, @InstallmentMonths, @CashReceiptType, @CashReceiptMerchant, @CashReceiptApprovalNo, @CashReceiptApprovalDate, 'COMPLETE')
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
        string sql = $"""
            SELECT {HeaderColumns}
            FROM sales_header_tb
            WHERE sale_dt >= @From AND sale_dt < @To AND status = 'COMPLETE'
            """;
        var rows = await connection.QueryAsync<SaleHeader>(sql, new { From = from, To = to });
        return rows.ToList();
    }

    public async Task<SaleHeader?> GetLastCompletedSaleAsync(string posCd)
    {
        using var connection = await _connectionFactory.CreateOpenConnectionAsync();
        string sql = $"""
            SELECT {HeaderColumns}
            FROM sales_header_tb
            WHERE pos_cd = @PosCd AND status = 'COMPLETE'
            ORDER BY sale_no DESC
            LIMIT 1
            """;
        return await connection.QuerySingleOrDefaultAsync<SaleHeader>(sql, new { PosCd = posCd });
    }

    public async Task<IReadOnlyList<SaleHeader>> SearchSalesAsync(DateTime from, DateTime to, string? payType, string? approvalNo)
    {
        using var connection = await _connectionFactory.CreateOpenConnectionAsync();
        var conditions = new List<string> { "sale_dt >= @From", "sale_dt < @To" };
        var parameters = new DynamicParameters();
        parameters.Add("From", from);
        parameters.Add("To", to);

        if (!string.IsNullOrWhiteSpace(payType))
        {
            conditions.Add("pay_type = @PayType");
            parameters.Add("PayType", payType);
        }

        if (!string.IsNullOrWhiteSpace(approvalNo))
        {
            conditions.Add("(van_approval_no = @ApprovalNo OR cash_receipt_approval_no = @ApprovalNo)");
            parameters.Add("ApprovalNo", approvalNo);
        }

        string sql = $"""
            SELECT {HeaderColumns}
            FROM sales_header_tb
            WHERE {string.Join(" AND ", conditions)}
            ORDER BY sale_no DESC
            """;
        var rows = await connection.QueryAsync<SaleHeader>(sql, parameters);
        return rows.ToList();
    }

    public async Task<(SaleHeader Header, IReadOnlyList<SaleDetailLine> Lines)?> GetSaleWithLinesAsync(long saleNo)
    {
        using var connection = await _connectionFactory.CreateOpenConnectionAsync();
        string headerSql = $"SELECT {HeaderColumns} FROM sales_header_tb WHERE sale_no = @SaleNo";
        var header = await connection.QuerySingleOrDefaultAsync<SaleHeader>(headerSql, new { SaleNo = saleNo });
        if (header is null) return null;

        const string linesSql = """
            SELECT barcode AS Barcode, product_name AS ProductName, qty AS Qty, unit_price AS UnitPrice
            FROM sales_detail_tb WHERE sale_no = @SaleNo ORDER BY line_no
            """;
        var lines = await connection.QueryAsync<SaleDetailLine>(linesSql, new { SaleNo = saleNo });
        return (header, lines.ToList());
    }

    public async Task CancelSaleAsync(long saleNo)
    {
        using var connection = await _connectionFactory.CreateOpenConnectionAsync();
        using var transaction = connection.BeginTransaction();

        const string cancelSql = "UPDATE sales_header_tb SET status = 'CANCELLED' WHERE sale_no = @SaleNo AND status = 'COMPLETE'";
        int affected = await connection.ExecuteAsync(cancelSql, new { SaleNo = saleNo }, transaction);
        if (affected == 0)
        {
            transaction.Rollback();
            return;
        }

        const string linesSql = """
            SELECT barcode AS Barcode, product_name AS ProductName, qty AS Qty, unit_price AS UnitPrice
            FROM sales_detail_tb WHERE sale_no = @SaleNo
            """;
        var lines = await connection.QueryAsync<SaleDetailLine>(linesSql, new { SaleNo = saleNo }, transaction);

        const string restockSql = "UPDATE product_tb SET stock_qty = stock_qty + @Qty WHERE barcode = @Barcode";
        foreach (var line in lines)
        {
            await connection.ExecuteAsync(restockSql, new { line.Qty, line.Barcode }, transaction);
        }

        transaction.Commit();
    }

    public async Task UpdateCashReceiptAsync(long saleNo, string receiptType, string merchant, string approvalNo, string approvalDateYyMmDd)
    {
        using var connection = await _connectionFactory.CreateOpenConnectionAsync();
        const string sql = """
            UPDATE sales_header_tb
            SET cash_receipt_type = @ReceiptType, cash_receipt_merchant = @Merchant,
                cash_receipt_approval_no = @ApprovalNo, cash_receipt_approval_date = @ApprovalDate
            WHERE sale_no = @SaleNo
            """;
        await connection.ExecuteAsync(sql, new
        {
            SaleNo = saleNo,
            ReceiptType = receiptType,
            Merchant = merchant,
            ApprovalNo = approvalNo,
            ApprovalDate = approvalDateYyMmDd,
        });
    }
}
