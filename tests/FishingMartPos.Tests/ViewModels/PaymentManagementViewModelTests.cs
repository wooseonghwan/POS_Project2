using FishingMartPos.Models;
using FishingMartPos.Navigation;
using FishingMartPos.Services;
using FishingMartPos.Tests.Fakes;
using FishingMartPos.ViewModels;
using Xunit;

namespace FishingMartPos.Tests.ViewModels;

public class PaymentManagementViewModelTests
{
    private static PaymentManagementViewModel CreateViewModel(out FakeSalesRepository sales)
    {
        sales = new FakeSalesRepository();
        var session = new CurrentSession();
        session.SignIn(
            new Staff { StaffCode = "ADMIN1", StaffName = "관리자", Role = "ADMIN", UseYn = "Y" },
            new PosTerminal { PosCode = "1", PosName = "POS1" });
        var navigation = new NavigationService();
        var mainMenu = new MainMenuViewModel(session, navigation);
        var van = new FakeVanPaymentGateway(new VanApprovalResult { IsApproved = true, ResponseMessage = "ok" });
        var cashReceipt = new FakeCashReceiptGateway(new CashReceiptResult { IsIssued = true, ResponseMessage = "ok" });

        return new PaymentManagementViewModel(
            sales, van, cashReceipt, new StubReceiptPrinter(), new FakeDelayProvider(),
            session, navigation, mainMenu);
    }

    [Fact]
    public async Task LoadAsync_PopulatesRowsFromSearchResults()
    {
        var vm = CreateViewModel(out var sales);
        sales.SeedCompletedSales(new List<SaleHeader>
        {
            new()
            {
                SaleNo = 1, PosCd = "1", SaleDt = DateTime.Today, StaffCd = "ADMIN1",
                TotalAmt = 5000m, PayType = "CASH",
            },
        });

        await vm.LoadAsync();

        Assert.True(vm.HasRows);
        Assert.Single(vm.Rows);
        Assert.Equal(1, vm.Rows[0].SaleNo);
    }

    [Fact]
    public async Task SelectRow_OpensDetailPopupForThatSale()
    {
        var vm = CreateViewModel(out var sales);
        var header = new SaleHeader
        {
            SaleNo = 7, PosCd = "1", SaleDt = DateTime.Today, StaffCd = "ADMIN1",
            TotalAmt = 5000m, PayType = "CASH",
        };
        sales.SeedCompletedSales(new List<SaleHeader> { header });
        sales.SeedSaleWithLines(7, header, new List<SaleDetailLine>());
        await vm.LoadAsync();

        await vm.Rows[0].SelectCommand.ExecuteAsync(null);

        Assert.True(vm.IsDetailVisible);
        Assert.Equal("7", vm.SelectedDetail!.SaleNoStr);
    }
}
