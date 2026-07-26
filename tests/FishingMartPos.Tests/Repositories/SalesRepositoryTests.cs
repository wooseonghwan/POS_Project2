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
        using (var conn = await factory.CreateOpenConnectionAsync())
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

        using var verifyConn = await factory.CreateOpenConnectionAsync();
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

    [Fact]
    public async Task CreateSale_WithMultipleLines_InsertsAllDetailsAndDecrementsEachProductStock()
    {
        var config = AppConfig.Load(AppContext.BaseDirectory);
        var factory = new MySqlConnectionFactory(config);
        ISalesRepository repository = new SalesRepository(factory);

        const string barcode1 = "8800000020001";
        const string barcode2 = "8800000020002";

        int stockBefore1;
        int stockBefore2;
        using (var conn = await factory.CreateOpenConnectionAsync())
        {
            stockBefore1 = await conn.QuerySingleAsync<int>(
                "SELECT stock_qty FROM product_tb WHERE barcode = @Barcode", new { Barcode = barcode1 });
            stockBefore2 = await conn.QuerySingleAsync<int>(
                "SELECT stock_qty FROM product_tb WHERE barcode = @Barcode", new { Barcode = barcode2 });
        }

        var header = new SaleHeader
        {
            PosCd = "1", SaleDt = DateTime.Now, StaffCd = "ADMIN1", TotalAmt = 15000,
            PayType = "CASH", CashReceived = 15000, ChangeAmt = 0, VanApprovalNo = null, VanCode = null,
        };
        var lines = new[]
        {
            new SaleDetailLine { Barcode = barcode1, ProductName = "지렁이", Qty = 1, UnitPrice = 5000 },
            new SaleDetailLine { Barcode = barcode2, ProductName = "냉동새우", Qty = 2, UnitPrice = 5000 },
        };

        long saleNo = await repository.CreateSaleAsync(header, lines);

        Assert.True(saleNo > 0);

        using var verifyConn = await factory.CreateOpenConnectionAsync();
        try
        {
            var savedHeader = await verifyConn.QuerySingleAsync<(decimal TotalAmt, string PayType)>(
                "SELECT total_amt AS TotalAmt, pay_type AS PayType FROM sales_header_tb WHERE sale_no = @SaleNo",
                new { SaleNo = saleNo });
            Assert.Equal(15000, savedHeader.TotalAmt);
            Assert.Equal("CASH", savedHeader.PayType);

            var details = (await verifyConn.QueryAsync<(int LineNo, string Barcode, int Qty)>(
                "SELECT line_no AS LineNo, barcode AS Barcode, qty AS Qty FROM sales_detail_tb WHERE sale_no = @SaleNo ORDER BY line_no",
                new { SaleNo = saleNo })).ToList();

            Assert.Equal(2, details.Count);
            Assert.Equal(1, details[0].LineNo);
            Assert.Equal(barcode1, details[0].Barcode);
            Assert.Equal(1, details[0].Qty);
            Assert.Equal(2, details[1].LineNo);
            Assert.Equal(barcode2, details[1].Barcode);
            Assert.Equal(2, details[1].Qty);

            int stockAfter1 = await verifyConn.QuerySingleAsync<int>(
                "SELECT stock_qty FROM product_tb WHERE barcode = @Barcode", new { Barcode = barcode1 });
            int stockAfter2 = await verifyConn.QuerySingleAsync<int>(
                "SELECT stock_qty FROM product_tb WHERE barcode = @Barcode", new { Barcode = barcode2 });
            Assert.Equal(stockBefore1 - 1, stockAfter1);
            Assert.Equal(stockBefore2 - 2, stockAfter2);
        }
        finally
        {
            // 정리: 테스트가 남긴 매출/재고 변화를 되돌린다
            await verifyConn.ExecuteAsync("DELETE FROM sales_detail_tb WHERE sale_no = @SaleNo", new { SaleNo = saleNo });
            await verifyConn.ExecuteAsync("DELETE FROM sales_header_tb WHERE sale_no = @SaleNo", new { SaleNo = saleNo });
            await verifyConn.ExecuteAsync(
                "UPDATE product_tb SET stock_qty = @Stock WHERE barcode = @Barcode",
                new { Stock = stockBefore1, Barcode = barcode1 });
            await verifyConn.ExecuteAsync(
                "UPDATE product_tb SET stock_qty = @Stock WHERE barcode = @Barcode",
                new { Stock = stockBefore2, Barcode = barcode2 });
        }
    }

    [Fact]
    public async Task CreateSale_WhenSecondLineFails_RollsBackHeaderAndFirstLineAndStock()
    {
        var config = AppConfig.Load(AppContext.BaseDirectory);
        var factory = new MySqlConnectionFactory(config);
        ISalesRepository repository = new SalesRepository(factory);

        const string barcode1 = "8800000020001";

        int stockBefore1;
        int saleCountBefore;
        long maxSaleNoBefore;
        using (var conn = await factory.CreateOpenConnectionAsync())
        {
            stockBefore1 = await conn.QuerySingleAsync<int>(
                "SELECT stock_qty FROM product_tb WHERE barcode = @Barcode", new { Barcode = barcode1 });
            saleCountBefore = await conn.QuerySingleAsync<int>("SELECT COUNT(*) FROM sales_header_tb");
            maxSaleNoBefore = await conn.QuerySingleAsync<long>(
                "SELECT COALESCE(MAX(sale_no), 0) FROM sales_header_tb");
        }

        var header = new SaleHeader
        {
            PosCd = "1", SaleDt = DateTime.Now, StaffCd = "ADMIN1", TotalAmt = 5000,
            PayType = "CASH", CashReceived = 5000, ChangeAmt = 0, VanApprovalNo = null, VanCode = null,
        };
        var lines = new[]
        {
            new SaleDetailLine { Barcode = barcode1, ProductName = "지렁이", Qty = 1, UnitPrice = 5000 },
            new SaleDetailLine { Barcode = new string('9', 40), ProductName = "오류유발", Qty = 1, UnitPrice = 5000 },
        };

        try
        {
            await Assert.ThrowsAnyAsync<Exception>(() => repository.CreateSaleAsync(header, lines));

            using var verifyConn = await factory.CreateOpenConnectionAsync();

            int saleCountAfter = await verifyConn.QuerySingleAsync<int>("SELECT COUNT(*) FROM sales_header_tb");
            Assert.Equal(saleCountBefore, saleCountAfter);

            int stockAfter1 = await verifyConn.QuerySingleAsync<int>(
                "SELECT stock_qty FROM product_tb WHERE barcode = @Barcode", new { Barcode = barcode1 });
            Assert.Equal(stockBefore1, stockAfter1);
        }
        finally
        {
            // 안전망: 혹시라도 커밋된 데이터가 있다면(기대와 다르게) 원상 복구한다.
            // 정상 시나리오에서는 트랜잭션이 롤백되어 새로 생긴 sale_no가 없어야 하므로 아래는 실질적으로 no-op이다.
            using var cleanupConn = await factory.CreateOpenConnectionAsync();
            await cleanupConn.ExecuteAsync(
                "DELETE FROM sales_detail_tb WHERE sale_no > @MaxSaleNoBefore", new { MaxSaleNoBefore = maxSaleNoBefore });
            await cleanupConn.ExecuteAsync(
                "DELETE FROM sales_header_tb WHERE sale_no > @MaxSaleNoBefore", new { MaxSaleNoBefore = maxSaleNoBefore });
            await cleanupConn.ExecuteAsync(
                "UPDATE product_tb SET stock_qty = @Stock WHERE barcode = @Barcode",
                new { Stock = stockBefore1, Barcode = barcode1 });
        }
    }

    [Fact]
    public async Task GetCompletedSalesAsync_ReturnsOnlyCompleteRowsWithinHalfOpenRange()
    {
        var config = AppConfig.Load(AppContext.BaseDirectory);
        var factory = new MySqlConnectionFactory(config);
        ISalesRepository repository = new SalesRepository(factory);

        const string barcode = "8800000020001";
        int stockBefore;
        using (var conn = await factory.CreateOpenConnectionAsync())
        {
            stockBefore = await conn.QuerySingleAsync<int>(
                "SELECT stock_qty FROM product_tb WHERE barcode = @Barcode", new { Barcode = barcode });
        }

        var lines = new[] { new SaleDetailLine { Barcode = barcode, ProductName = "지렁이", Qty = 1, UnitPrice = 5000 } };
        var inRangeDate = new DateTime(2026, 3, 10, 12, 0, 0);
        var boundaryExcludedDate = new DateTime(2026, 3, 15, 0, 0, 0); // to == 이 시각이면 제외되어야 함
        var outOfRangeDate = new DateTime(2026, 3, 20, 0, 0, 0);

        long saleInRange = await repository.CreateSaleAsync(
            new SaleHeader { PosCd = "1", SaleDt = inRangeDate, StaffCd = "ADMIN1", TotalAmt = 5000, PayType = "CASH", CashReceived = 5000, ChangeAmt = 0 },
            lines);
        long saleAtBoundary = await repository.CreateSaleAsync(
            new SaleHeader { PosCd = "1", SaleDt = boundaryExcludedDate, StaffCd = "ADMIN1", TotalAmt = 7000, PayType = "CARD1" },
            lines);
        long saleOutOfRange = await repository.CreateSaleAsync(
            new SaleHeader { PosCd = "1", SaleDt = outOfRangeDate, StaffCd = "ADMIN1", TotalAmt = 9000, PayType = "CARD2" },
            lines);
        long saleCancelled = await repository.CreateSaleAsync(
            new SaleHeader { PosCd = "1", SaleDt = inRangeDate, StaffCd = "ADMIN1", TotalAmt = 4000, PayType = "CASH" },
            lines);

        using var verifyConn = await factory.CreateOpenConnectionAsync();
        try
        {
            await verifyConn.ExecuteAsync(
                "UPDATE sales_header_tb SET status = 'CANCELLED' WHERE sale_no = @SaleNo",
                new { SaleNo = saleCancelled });

            var result = await repository.GetCompletedSalesAsync(new DateTime(2026, 3, 1), boundaryExcludedDate);

            Assert.Contains(result, r => r.TotalAmt == 5000);
            Assert.DoesNotContain(result, r => r.TotalAmt == 7000); // to 경계는 제외
            Assert.DoesNotContain(result, r => r.TotalAmt == 9000); // 범위 밖
            Assert.DoesNotContain(result, r => r.TotalAmt == 4000); // CANCELLED
        }
        finally
        {
            await verifyConn.ExecuteAsync(
                "DELETE FROM sales_detail_tb WHERE sale_no IN (@A, @B, @C, @D)",
                new { A = saleInRange, B = saleAtBoundary, C = saleOutOfRange, D = saleCancelled });
            await verifyConn.ExecuteAsync(
                "DELETE FROM sales_header_tb WHERE sale_no IN (@A, @B, @C, @D)",
                new { A = saleInRange, B = saleAtBoundary, C = saleOutOfRange, D = saleCancelled });
            await verifyConn.ExecuteAsync(
                "UPDATE product_tb SET stock_qty = @Stock WHERE barcode = @Barcode",
                new { Stock = stockBefore, Barcode = barcode });
        }
    }

    [Fact]
    public async Task CreateSale_WithInstallmentMonths_RoundTripsThroughDb()
    {
        var config = AppConfig.Load(AppContext.BaseDirectory);
        var factory = new MySqlConnectionFactory(config);
        ISalesRepository repository = new SalesRepository(factory);

        const string barcode = "8800000020001";
        int stockBefore;
        using (var conn = await factory.CreateOpenConnectionAsync())
        {
            stockBefore = await conn.QuerySingleAsync<int>(
                "SELECT stock_qty FROM product_tb WHERE barcode = @Barcode", new { Barcode = barcode });
        }

        var header = new SaleHeader
        {
            PosCd = "1", SaleDt = DateTime.Now, StaffCd = "ADMIN1", TotalAmt = 5000,
            PayType = "CARD1", VanApprovalNo = "TEST123", VanCode = "KICC", InstallmentMonths = 3,
        };
        var lines = new[]
        {
            new SaleDetailLine { Barcode = barcode, ProductName = "지렁이", Qty = 1, UnitPrice = 5000 },
        };

        long saleNo = await repository.CreateSaleAsync(header, lines);

        using var verifyConn = await factory.CreateOpenConnectionAsync();
        try
        {
            int savedInstallmentMonths = await verifyConn.QuerySingleAsync<int>(
                "SELECT installment_months FROM sales_header_tb WHERE sale_no = @SaleNo", new { SaleNo = saleNo });
            Assert.Equal(3, savedInstallmentMonths);
        }
        finally
        {
            await verifyConn.ExecuteAsync("DELETE FROM sales_detail_tb WHERE sale_no = @SaleNo", new { SaleNo = saleNo });
            await verifyConn.ExecuteAsync("DELETE FROM sales_header_tb WHERE sale_no = @SaleNo", new { SaleNo = saleNo });
            await verifyConn.ExecuteAsync(
                "UPDATE product_tb SET stock_qty = @Stock WHERE barcode = @Barcode",
                new { Stock = stockBefore, Barcode = barcode });
        }
    }

    [Fact]
    public async Task CreateSale_WithoutExplicitInstallmentMonths_DefaultsToZero()
    {
        var config = AppConfig.Load(AppContext.BaseDirectory);
        var factory = new MySqlConnectionFactory(config);
        ISalesRepository repository = new SalesRepository(factory);

        const string barcode = "8800000020001";
        int stockBefore;
        using (var conn = await factory.CreateOpenConnectionAsync())
        {
            stockBefore = await conn.QuerySingleAsync<int>(
                "SELECT stock_qty FROM product_tb WHERE barcode = @Barcode", new { Barcode = barcode });
        }

        var header = new SaleHeader
        {
            PosCd = "1", SaleDt = DateTime.Now, StaffCd = "ADMIN1", TotalAmt = 5000,
            PayType = "CASH", CashReceived = 5000, ChangeAmt = 0,
        };
        var lines = new[]
        {
            new SaleDetailLine { Barcode = barcode, ProductName = "지렁이", Qty = 1, UnitPrice = 5000 },
        };

        long saleNo = await repository.CreateSaleAsync(header, lines);

        using var verifyConn = await factory.CreateOpenConnectionAsync();
        try
        {
            int savedInstallmentMonths = await verifyConn.QuerySingleAsync<int>(
                "SELECT installment_months FROM sales_header_tb WHERE sale_no = @SaleNo", new { SaleNo = saleNo });
            Assert.Equal(0, savedInstallmentMonths);
        }
        finally
        {
            await verifyConn.ExecuteAsync("DELETE FROM sales_detail_tb WHERE sale_no = @SaleNo", new { SaleNo = saleNo });
            await verifyConn.ExecuteAsync("DELETE FROM sales_header_tb WHERE sale_no = @SaleNo", new { SaleNo = saleNo });
            await verifyConn.ExecuteAsync(
                "UPDATE product_tb SET stock_qty = @Stock WHERE barcode = @Barcode",
                new { Stock = stockBefore, Barcode = barcode });
        }
    }
}
