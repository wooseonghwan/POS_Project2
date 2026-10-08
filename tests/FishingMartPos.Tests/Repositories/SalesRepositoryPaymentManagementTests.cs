using Dapper;
using FishingMartPos.Configuration;
using FishingMartPos.Data;
using FishingMartPos.Models;
using FishingMartPos.Repositories;
using Xunit;

namespace FishingMartPos.Tests.Repositories;

public class SalesRepositoryPaymentManagementTests
{
    // product_tb에 실재하는 바코드 (지렁이) — 재고 복구 검증에 사용. TESTBARCODE1은 product_tb에 없어서
    // CancelSaleAsync의 재입고 UPDATE가 조용한 no-op이 되어 재고 변화를 관측할 수 없다.
    private const string RealBarcode = "8800000020001";

    private static MySqlConnectionFactory CreateConnectionFactory()
    {
        var config = AppConfig.Load(AppContext.BaseDirectory);
        return new MySqlConnectionFactory(config);
    }

    private static SaleHeader NewHeader(string payType = "CASH") => new()
    {
        PosCd = "9",
        SaleDt = DateTime.Now,
        StaffCd = "ADMIN1",
        TotalAmt = 5000m,
        PayType = payType,
        CashReceived = payType == "CASH" ? 5000m : null,
        ChangeAmt = payType == "CASH" ? 0m : null,
        VanApprovalNo = payType != "CASH" ? "TESTAPPROVAL1" : null,
        VanCode = payType != "CASH" ? "KICC" : null,
    };

    private static List<SaleDetailLine> OneLine() => new()
    {
        new SaleDetailLine { Barcode = "TESTBARCODE1", ProductName = "테스트상품", Qty = 2, UnitPrice = 1000m },
    };

    private static List<SaleDetailLine> OneLineWithRealProduct(int qty = 1) => new()
    {
        new SaleDetailLine { Barcode = RealBarcode, ProductName = "지렁이", Qty = qty, UnitPrice = 5000m },
    };

    [Fact]
    public async Task GetLastSaleAsync_ReturnsMostRecentSaleForPosCode()
    {
        var factory = CreateConnectionFactory();
        var repo = new SalesRepository(factory);
        long firstSaleNo = await repo.CreateSaleAsync(NewHeader(), OneLine());
        long secondSaleNo = await repo.CreateSaleAsync(NewHeader(), OneLine());

        try
        {
            var result = await repo.GetLastSaleAsync("9");

            Assert.NotNull(result);
            Assert.Equal(secondSaleNo, result!.SaleNo);
            Assert.True(result.SaleNo >= firstSaleNo);
        }
        finally
        {
            using var conn = await factory.CreateOpenConnectionAsync();
            await conn.ExecuteAsync(
                "DELETE FROM sales_detail_tb WHERE sale_no IN (@A, @B)", new { A = firstSaleNo, B = secondSaleNo });
            await conn.ExecuteAsync(
                "DELETE FROM sales_header_tb WHERE sale_no IN (@A, @B)", new { A = firstSaleNo, B = secondSaleNo });
        }
    }

    [Fact]
    public async Task GetLastSaleAsync_WhenMostRecentSaleWasCancelled_StillReturnsIt()
    {
        // 회귀 테스트: 과거엔 status='COMPLETE' 필터 때문에 가장 최근 거래가 취소된 경우 조회 자체가
        // 안 됐다(직전정보로 취소된 거래의 영수증을 재발행할 수 없는 버그였음).
        var factory = CreateConnectionFactory();
        var repo = new SalesRepository(factory);
        long saleNo = await repo.CreateSaleAsync(NewHeader(), OneLine());
        await repo.CancelSaleAsync(saleNo);

        try
        {
            var result = await repo.GetLastSaleAsync("9");

            Assert.NotNull(result);
            Assert.Equal(saleNo, result!.SaleNo);
            Assert.Equal("CANCELLED", result.Status);
        }
        finally
        {
            using var conn = await factory.CreateOpenConnectionAsync();
            await conn.ExecuteAsync("DELETE FROM sales_detail_tb WHERE sale_no = @SaleNo", new { SaleNo = saleNo });
            await conn.ExecuteAsync("DELETE FROM sales_header_tb WHERE sale_no = @SaleNo", new { SaleNo = saleNo });
        }
    }

    [Fact]
    public async Task GetSaleWithLinesAsync_ReturnsHeaderAndLines()
    {
        var factory = CreateConnectionFactory();
        var repo = new SalesRepository(factory);
        long saleNo = await repo.CreateSaleAsync(NewHeader(), OneLine());

        try
        {
            var result = await repo.GetSaleWithLinesAsync(saleNo);

            Assert.NotNull(result);
            Assert.Equal(saleNo, result!.Value.Header.SaleNo);
            Assert.Single(result.Value.Lines);
            Assert.Equal("TESTBARCODE1", result.Value.Lines[0].Barcode);
        }
        finally
        {
            using var conn = await factory.CreateOpenConnectionAsync();
            await conn.ExecuteAsync("DELETE FROM sales_detail_tb WHERE sale_no = @SaleNo", new { SaleNo = saleNo });
            await conn.ExecuteAsync("DELETE FROM sales_header_tb WHERE sale_no = @SaleNo", new { SaleNo = saleNo });
        }
    }

    [Fact]
    public async Task SearchSalesAsync_FiltersByPayTypeAndApprovalNo()
    {
        var factory = CreateConnectionFactory();
        var repo = new SalesRepository(factory);
        long cashSaleNo = await repo.CreateSaleAsync(NewHeader("CASH"), OneLine());
        long cardSaleNo = await repo.CreateSaleAsync(NewHeader("CARD1"), OneLine());

        try
        {
            var from = DateTime.Today;
            var to = DateTime.Today.AddDays(1);

            var cashOnly = await repo.SearchSalesAsync(from, to, "CASH", null);
            Assert.All(cashOnly, s => Assert.Equal("CASH", s.PayType));

            var byApproval = await repo.SearchSalesAsync(from, to, null, "TESTAPPROVAL1");
            Assert.All(byApproval, s => Assert.Equal("TESTAPPROVAL1", s.VanApprovalNo));
        }
        finally
        {
            using var conn = await factory.CreateOpenConnectionAsync();
            await conn.ExecuteAsync(
                "DELETE FROM sales_detail_tb WHERE sale_no IN (@A, @B)", new { A = cashSaleNo, B = cardSaleNo });
            await conn.ExecuteAsync(
                "DELETE FROM sales_header_tb WHERE sale_no IN (@A, @B)", new { A = cashSaleNo, B = cardSaleNo });
        }
    }

    [Fact]
    public async Task CancelSaleAsync_MarksCancelledAndRestocksProduct()
    {
        var factory = CreateConnectionFactory();
        var repo = new SalesRepository(factory);

        int stockBefore;
        using (var conn = await factory.CreateOpenConnectionAsync())
        {
            stockBefore = await conn.QuerySingleAsync<int>(
                "SELECT stock_qty FROM product_tb WHERE barcode = @Barcode", new { Barcode = RealBarcode });
        }

        long saleNo = await repo.CreateSaleAsync(NewHeader(), OneLineWithRealProduct());

        using var verifyConn = await factory.CreateOpenConnectionAsync();
        try
        {
            int stockAfterSale = await verifyConn.QuerySingleAsync<int>(
                "SELECT stock_qty FROM product_tb WHERE barcode = @Barcode", new { Barcode = RealBarcode });
            Assert.Equal(stockBefore - 1, stockAfterSale); // 판매로 재고 1 감소 확인

            await repo.CancelSaleAsync(saleNo);

            int stockAfterCancel = await verifyConn.QuerySingleAsync<int>(
                "SELECT stock_qty FROM product_tb WHERE barcode = @Barcode", new { Barcode = RealBarcode });
            Assert.Equal(stockBefore, stockAfterCancel); // 취소로 재고가 원래 수량으로 완전히 복구되었는지 확인

            var result = await repo.GetSaleWithLinesAsync(saleNo);
            Assert.Equal("CANCELLED", result!.Value.Header.Status);
        }
        finally
        {
            await verifyConn.ExecuteAsync("DELETE FROM sales_detail_tb WHERE sale_no = @SaleNo", new { SaleNo = saleNo });
            await verifyConn.ExecuteAsync("DELETE FROM sales_header_tb WHERE sale_no = @SaleNo", new { SaleNo = saleNo });
            await verifyConn.ExecuteAsync(
                "UPDATE product_tb SET stock_qty = @Stock WHERE barcode = @Barcode",
                new { Stock = stockBefore, Barcode = RealBarcode });
        }
    }

    [Fact]
    public async Task CancelSaleAsync_WhenAlreadyCancelled_IsNoOp()
    {
        var factory = CreateConnectionFactory();
        var repo = new SalesRepository(factory);

        int stockBefore;
        using (var conn = await factory.CreateOpenConnectionAsync())
        {
            stockBefore = await conn.QuerySingleAsync<int>(
                "SELECT stock_qty FROM product_tb WHERE barcode = @Barcode", new { Barcode = RealBarcode });
        }

        long saleNo = await repo.CreateSaleAsync(NewHeader(), OneLineWithRealProduct());

        using var verifyConn = await factory.CreateOpenConnectionAsync();
        try
        {
            await repo.CancelSaleAsync(saleNo);

            int stockAfterFirstCancel = await verifyConn.QuerySingleAsync<int>(
                "SELECT stock_qty FROM product_tb WHERE barcode = @Barcode", new { Barcode = RealBarcode });
            Assert.Equal(stockBefore, stockAfterFirstCancel); // 첫 취소로 재고 복구됨

            await repo.CancelSaleAsync(saleNo); // 두 번째 취소 — 예외 없이 조용히 무시되어야 함

            int stockAfterSecondCancel = await verifyConn.QuerySingleAsync<int>(
                "SELECT stock_qty FROM product_tb WHERE barcode = @Barcode", new { Barcode = RealBarcode });
            Assert.Equal(stockAfterFirstCancel, stockAfterSecondCancel); // 이중 재입고가 없어야 함

            var result = await repo.GetSaleWithLinesAsync(saleNo);
            Assert.Equal("CANCELLED", result!.Value.Header.Status);
        }
        finally
        {
            await verifyConn.ExecuteAsync("DELETE FROM sales_detail_tb WHERE sale_no = @SaleNo", new { SaleNo = saleNo });
            await verifyConn.ExecuteAsync("DELETE FROM sales_header_tb WHERE sale_no = @SaleNo", new { SaleNo = saleNo });
            await verifyConn.ExecuteAsync(
                "UPDATE product_tb SET stock_qty = @Stock WHERE barcode = @Barcode",
                new { Stock = stockBefore, Barcode = RealBarcode });
        }
    }

    [Fact]
    public async Task UpdateCashReceiptAsync_UpdatesHeaderFields()
    {
        var factory = CreateConnectionFactory();
        var repo = new SalesRepository(factory);
        long saleNo = await repo.CreateSaleAsync(NewHeader(), OneLine());

        try
        {
            await repo.UpdateCashReceiptAsync(saleNo, "PERSONAL", "CARD1", "APPROVAL999", "260726");

            var result = await repo.GetSaleWithLinesAsync(saleNo);
            Assert.Equal("PERSONAL", result!.Value.Header.CashReceiptType);
            Assert.Equal("CARD1", result.Value.Header.CashReceiptMerchant);
            Assert.Equal("APPROVAL999", result.Value.Header.CashReceiptApprovalNo);
        }
        finally
        {
            using var conn = await factory.CreateOpenConnectionAsync();
            await conn.ExecuteAsync("DELETE FROM sales_detail_tb WHERE sale_no = @SaleNo", new { SaleNo = saleNo });
            await conn.ExecuteAsync("DELETE FROM sales_header_tb WHERE sale_no = @SaleNo", new { SaleNo = saleNo });
        }
    }
}
