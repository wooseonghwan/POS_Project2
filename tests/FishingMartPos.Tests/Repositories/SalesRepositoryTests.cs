using Dapper;
using FishingMartPos.Configuration;
using FishingMartPos.Data;
using FishingMartPos.Models;
using FishingMartPos.Repositories;
using Xunit;

namespace FishingMartPos.Tests.Repositories;

public class SalesRepositoryTests
{
    [Fact]
    public async Task CreateSale_InsertsHeaderDetailAndDecrementsStock()
    {
        var config = AppConfig.Load(AppContext.BaseDirectory);
        var factory = new MySqlConnectionFactory(config);
        ISalesRepository repository = new SalesRepository(factory);

        int stockBefore;
        using (var conn = factory.CreateOpenConnection())
        {
            stockBefore = await conn.QuerySingleAsync<int>(
                "SELECT stock_qty FROM product_tb WHERE barcode = @Barcode", new { Barcode = "8800000020001" });
        }

        var header = new SaleHeader
        {
            PosCd = "1", SaleDt = DateTime.Now, StaffCd = "ADMIN1", TotalAmt = 5000,
            PayType = "CASH", CashReceived = 5000, ChangeAmt = 0, VanApprovalNo = null, VanCode = null,
        };
        var lines = new[]
        {
            new SaleDetailLine { Barcode = "8800000020001", ProductName = "지렁이", Qty = 1, UnitPrice = 5000 },
        };

        long saleNo = await repository.CreateSaleAsync(header, lines);

        Assert.True(saleNo > 0);

        using var verifyConn = factory.CreateOpenConnection();
        var savedHeader = await verifyConn.QuerySingleAsync<(decimal TotalAmt, string PayType)>(
            "SELECT total_amt AS TotalAmt, pay_type AS PayType FROM sales_header_tb WHERE sale_no = @SaleNo",
            new { SaleNo = saleNo });
        Assert.Equal(5000, savedHeader.TotalAmt);
        Assert.Equal("CASH", savedHeader.PayType);

        var detailCount = await verifyConn.QuerySingleAsync<int>(
            "SELECT COUNT(*) FROM sales_detail_tb WHERE sale_no = @SaleNo", new { SaleNo = saleNo });
        Assert.Equal(1, detailCount);

        int stockAfter = await verifyConn.QuerySingleAsync<int>(
            "SELECT stock_qty FROM product_tb WHERE barcode = @Barcode", new { Barcode = "8800000020001" });
        Assert.Equal(stockBefore - 1, stockAfter);

        // 정리: 테스트가 남긴 매출/재고 변화를 되돌린다
        await verifyConn.ExecuteAsync("DELETE FROM sales_detail_tb WHERE sale_no = @SaleNo", new { SaleNo = saleNo });
        await verifyConn.ExecuteAsync("DELETE FROM sales_header_tb WHERE sale_no = @SaleNo", new { SaleNo = saleNo });
        await verifyConn.ExecuteAsync(
            "UPDATE product_tb SET stock_qty = @Stock WHERE barcode = @Barcode",
            new { Stock = stockBefore, Barcode = "8800000020001" });
    }
}
