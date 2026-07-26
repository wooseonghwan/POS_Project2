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

    private static SalesRepository CreateRepository()
    {
        return new SalesRepository(CreateConnectionFactory());
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
    public async Task GetLastCompletedSaleAsync_ReturnsMostRecentSaleForPosCode()
    {
        var repo = CreateRepository();
        long firstSaleNo = await repo.CreateSaleAsync(NewHeader(), OneLine());
        long secondSaleNo = await repo.CreateSaleAsync(NewHeader(), OneLine());

        var result = await repo.GetLastCompletedSaleAsync("9");

        Assert.NotNull(result);
        Assert.Equal(secondSaleNo, result!.SaleNo);
        Assert.True(result.SaleNo >= firstSaleNo);
    }

    [Fact]
    public async Task GetSaleWithLinesAsync_ReturnsHeaderAndLines()
    {
        var repo = CreateRepository();
        long saleNo = await repo.CreateSaleAsync(NewHeader(), OneLine());

        var result = await repo.GetSaleWithLinesAsync(saleNo);

        Assert.NotNull(result);
        Assert.Equal(saleNo, result!.Value.Header.SaleNo);
        Assert.Single(result.Value.Lines);
        Assert.Equal("TESTBARCODE1", result.Value.Lines[0].Barcode);
    }

    [Fact]
    public async Task SearchSalesAsync_FiltersByPayTypeAndApprovalNo()
    {
        var repo = CreateRepository();
        await repo.CreateSaleAsync(NewHeader("CASH"), OneLine());
        await repo.CreateSaleAsync(NewHeader("CARD1"), OneLine());

        var from = DateTime.Today;
        var to = DateTime.Today.AddDays(1);

        var cashOnly = await repo.SearchSalesAsync(from, to, "CASH", null);
        Assert.All(cashOnly, s => Assert.Equal("CASH", s.PayType));

        var byApproval = await repo.SearchSalesAsync(from, to, null, "TESTAPPROVAL1");
        Assert.All(byApproval, s => Assert.Equal("TESTAPPROVAL1", s.VanApprovalNo));
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
        await repo.CancelSaleAsync(saleNo);

        using var verifyConn = await factory.CreateOpenConnectionAsync();
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

    [Fact]
    public async Task UpdateCashReceiptAsync_UpdatesHeaderFields()
    {
        var repo = CreateRepository();
        long saleNo = await repo.CreateSaleAsync(NewHeader(), OneLine());

        await repo.UpdateCashReceiptAsync(saleNo, "PERSONAL", "CARD1", "APPROVAL999", "260726");

        var result = await repo.GetSaleWithLinesAsync(saleNo);
        Assert.Equal("PERSONAL", result!.Value.Header.CashReceiptType);
        Assert.Equal("CARD1", result.Value.Header.CashReceiptMerchant);
        Assert.Equal("APPROVAL999", result.Value.Header.CashReceiptApprovalNo);
    }
}
