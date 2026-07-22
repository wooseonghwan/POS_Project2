using FishingMartPos.Models;
using FishingMartPos.Navigation;
using FishingMartPos.Services;
using FishingMartPos.Tests.Fakes;
using FishingMartPos.ViewModels;
using Xunit;

namespace FishingMartPos.Tests.ViewModels;

public class SalesReportViewModelTests
{
    private static (SalesReportViewModel vm, FakeSalesRepository sales, MainMenuViewModel mainMenu, INavigationService navigation) Create()
    {
        var session = new CurrentSession();
        session.SignIn(
            new Staff { StaffCode = "ADMIN1", StaffName = "관리자", Role = "ADMIN", UseYn = "Y" },
            new PosTerminal { PosCode = "1", PosName = "POS1" });
        var navigation = new NavigationService();
        var mainMenu = new MainMenuViewModel(session, navigation);
        var sales = new FakeSalesRepository();
        var vm = new SalesReportViewModel(sales, navigation, mainMenu);
        return (vm, sales, mainMenu, navigation);
    }

    private static SaleHeader Sale(DateTime saleDt, string payType, decimal amount) => new()
    {
        PosCd = "1", SaleDt = saleDt, StaffCd = "ADMIN1", TotalAmt = amount, PayType = payType,
    };

    [Fact]
    public async Task LoadAsync_DefaultsToDailyTabLast7Days()
    {
        var (vm, _, _, _) = Create();

        await vm.LoadAsync();

        Assert.True(vm.IsDailyTab);
        Assert.Equal(DateTime.Today, vm.DateTo!.Value.Date);
        Assert.Equal(DateTime.Today.AddDays(-6), vm.DateFrom!.Value.Date);
    }

    [Fact]
    public async Task LoadAsync_DailyTab_GroupsByDateAndComputesTotalsPerPayType()
    {
        var (vm, sales, _, _) = Create();
        var day1 = DateTime.Today.AddDays(-1);
        sales.SeedCompletedSales(new[]
        {
            Sale(day1, "CASH", 5000),
            Sale(day1, "CARD1", 3000),
            Sale(day1.AddHours(2), "CASH", 2000),
        });
        vm.DateFrom = day1;
        vm.DateTo = day1;

        await vm.LoadAsync();

        var row = Assert.Single(vm.Rows);
        Assert.Equal(day1.ToString("yyyy-MM-dd"), row.Label);
        Assert.Equal("3건", row.CountStr);
        Assert.Equal("7,000원", row.CashStr);
        Assert.Equal("3,000원", row.Card1Str);
        Assert.Equal("0원", row.Card2Str);
        Assert.Equal("10,000원", row.AmountStr);
        Assert.Equal("10,000원", vm.TotalAmountStr);
        Assert.Equal("7,000원", vm.TotalCashStr);
        Assert.Equal("3,000원", vm.TotalCard1Str);
        Assert.Equal("0원", vm.TotalCard2Str);
        Assert.True(vm.HasRows);
    }

    [Fact]
    public async Task RefreshCommand_DailyTab_QueriesRepositoryWithExactDayBounds()
    {
        var (vm, sales, _, _) = Create();
        vm.DateFrom = new DateTime(2026, 3, 15);
        vm.DateTo = new DateTime(2026, 3, 20);

        await vm.RefreshCommand.ExecuteAsync(null);

        var lastCall = sales.GetCompletedSalesCalls[^1];
        Assert.Equal(new DateTime(2026, 3, 15), lastCall.From);
        Assert.Equal(new DateTime(2026, 3, 21), lastCall.To);
    }

    [Fact]
    public async Task SelectMonthlyTab_QueriesRepositoryWithFullMonthBounds()
    {
        var (vm, sales, _, _) = Create();
        vm.DateFrom = new DateTime(2026, 3, 15);
        vm.DateTo = new DateTime(2026, 3, 20);
        await vm.RefreshCommand.ExecuteAsync(null);

        await vm.SelectMonthlyTabCommand.ExecuteAsync(null);

        Assert.False(vm.IsDailyTab);
        var lastCall = sales.GetCompletedSalesCalls[^1];
        Assert.Equal(new DateTime(2026, 3, 1), lastCall.From);
        Assert.Equal(new DateTime(2026, 4, 1), lastCall.To);
    }

    [Fact]
    public async Task LoadAsync_MonthlyTab_GroupsByMonthEvenWhenDatesAreMidMonth()
    {
        var (vm, sales, _, _) = Create();
        sales.SeedCompletedSales(new[]
        {
            Sale(new DateTime(2026, 3, 1), "CASH", 1000),
            Sale(new DateTime(2026, 3, 31), "CASH", 2000),
        });
        vm.DateFrom = new DateTime(2026, 3, 15);
        vm.DateTo = new DateTime(2026, 3, 15);

        await vm.SelectMonthlyTabCommand.ExecuteAsync(null);

        var row = Assert.Single(vm.Rows);
        Assert.Equal("2026-03 월", row.Label);
        Assert.Equal("3,000원", row.AmountStr);
    }

    [Fact]
    public async Task LoadAsync_MultipleDays_OrdersRowsNewestFirst()
    {
        var (vm, sales, _, _) = Create();
        var older = DateTime.Today.AddDays(-2);
        var newer = DateTime.Today.AddDays(-1);
        sales.SeedCompletedSales(new[] { Sale(older, "CASH", 1000), Sale(newer, "CASH", 2000) });
        vm.DateFrom = older;
        vm.DateTo = newer;

        await vm.LoadAsync();

        Assert.Equal(2, vm.Rows.Count);
        Assert.Equal(newer.ToString("yyyy-MM-dd"), vm.Rows[0].Label);
        Assert.Equal(older.ToString("yyyy-MM-dd"), vm.Rows[1].Label);
    }

    [Fact]
    public async Task LoadAsync_NoSalesInRange_HasRowsIsFalse()
    {
        var (vm, _, _, _) = Create();

        await vm.LoadAsync();

        Assert.False(vm.HasRows);
        Assert.Empty(vm.Rows);
    }

    [Fact]
    public async Task GoToMainMenu_NavigatesBackToMainMenu()
    {
        var (vm, _, mainMenu, navigation) = Create();

        vm.GoToMainMenuCommand.Execute(null);

        Assert.Same(mainMenu, navigation.CurrentViewModel);
    }
}
