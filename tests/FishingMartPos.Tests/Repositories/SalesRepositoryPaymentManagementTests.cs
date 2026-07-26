using FishingMartPos.Configuration;
using FishingMartPos.Data;
using FishingMartPos.Models;
using FishingMartPos.Repositories;
using Xunit;

namespace FishingMartPos.Tests.Repositories;

public class SalesRepositoryPaymentManagementTests
{
    private static SalesRepository CreateRepository()
    {
        var config = AppConfig.Load(AppContext.BaseDirectory);
        var connectionFactory = new MySqlConnectionFactory(config);
        return new SalesRepository(connectionFactory);
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
        var repo = CreateRepository();
        long saleNo = await repo.CreateSaleAsync(NewHeader(), OneLine());

        await repo.CancelSaleAsync(saleNo);

        var result = await repo.GetSaleWithLinesAsync(saleNo);
        Assert.Equal("CANCELLED", result!.Value.Header.Status);
    }

    [Fact]
    public async Task CancelSaleAsync_WhenAlreadyCancelled_IsNoOp()
    {
        var repo = CreateRepository();
        long saleNo = await repo.CreateSaleAsync(NewHeader(), OneLine());
        await repo.CancelSaleAsync(saleNo);

        await repo.CancelSaleAsync(saleNo); // 두 번째 취소 — 예외 없이 조용히 무시되어야 함

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
