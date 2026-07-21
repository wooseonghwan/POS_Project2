using FishingMartPos.Models;
using FishingMartPos.Navigation;
using FishingMartPos.Services;
using FishingMartPos.Tests.Fakes;
using FishingMartPos.ViewModels;
using Xunit;

namespace FishingMartPos.Tests.ViewModels;

public class PosViewModelPaymentTests
{
    private static readonly Product Bait1 = new()
    {
        Barcode = "B1", MajorCd = "FISH", MinorCd = "BAIT", PosCatCd = "BAIT", Name = "지렁이", Price = 5000, StockQty = 50
    };

    private static PosViewModel CreateViewModel(out FakeSalesRepository sales, out FakeHeldOrderRepository held)
    {
        sales = new FakeSalesRepository();
        held = new FakeHeldOrderRepository();
        var codes = new Dictionary<string, IReadOnlyList<CodeItem>>
        {
            ["POSCAT"] = new List<CodeItem> { new() { Code = "BAIT", Name = "미끼", SortNo = 1 } },
        };
        var session = new CurrentSession();
        session.SignIn(
            new Staff { StaffCode = "ADMIN1", StaffName = "관리자", Role = "ADMIN", UseYn = "Y" },
            new PosTerminal { PosCode = "1", PosName = "POS1" });
        var navigation = new NavigationService();
        var mainMenuViewModel = new MainMenuViewModel(session, navigation);

        return new PosViewModel(
            new FakeProductRepository(new[] { Bait1 }), new FakeCodeRepository(codes),
            sales, held, new FakeDelayProvider(), session, navigation, mainMenuViewModel);
    }

    [Fact]
    public async Task PayCash_WithEmptyCart_DoesNothing()
    {
        var vm = CreateViewModel(out var sales, out _);
        await vm.LoadAsync();

        await vm.PayCashCommand.ExecuteAsync(null);

        Assert.Empty(sales.CreatedSales);
    }

    [Fact]
    public async Task PayCash_WithItemsInCart_CreatesSaleAndClearsCart()
    {
        var vm = CreateViewModel(out var sales, out _);
        await vm.LoadAsync();
        vm.VisibleProducts[0].AddCommand.Execute(null);

        await vm.PayCashCommand.ExecuteAsync(null);

        var sale = Assert.Single(sales.CreatedSales);
        Assert.Equal("CASH", sale.Header.PayType);
        Assert.Equal(5000, sale.Header.TotalAmt);
        Assert.Empty(vm.CartLines);
    }

    [Fact]
    public async Task PayCard1_RecordsCard1PayTypeWithNullVanCode()
    {
        var vm = CreateViewModel(out var sales, out _);
        await vm.LoadAsync();
        vm.VisibleProducts[0].AddCommand.Execute(null);

        await vm.PayCard1Command.ExecuteAsync(null);

        var sale = Assert.Single(sales.CreatedSales);
        Assert.Equal("CARD1", sale.Header.PayType);
        Assert.Null(sale.Header.VanCode);
    }

    [Fact]
    public async Task PayCard2_RecordsCard2PayType()
    {
        var vm = CreateViewModel(out var sales, out _);
        await vm.LoadAsync();
        vm.VisibleProducts[0].AddCommand.Execute(null);

        await vm.PayCard2Command.ExecuteAsync(null);

        var sale = Assert.Single(sales.CreatedSales);
        Assert.Equal("CARD2", sale.Header.PayType);
    }

    [Fact]
    public async Task HoldOrder_WithEmptyCart_DoesNothing()
    {
        var vm = CreateViewModel(out _, out var held);
        await vm.LoadAsync();

        await vm.HoldOrderCommand.ExecuteAsync(null);

        Assert.Empty(vm.HeldOrders);
    }

    [Fact]
    public async Task HoldOrder_ThenRecall_RestoresCartAndRemovesFromHeldList()
    {
        var vm = CreateViewModel(out _, out var held);
        await vm.LoadAsync();
        vm.VisibleProducts[0].AddCommand.Execute(null);
        vm.IncSelectedCommand.Execute(null); // qty=2

        await vm.HoldOrderCommand.ExecuteAsync(null);

        Assert.Empty(vm.CartLines);
        var heldItem = Assert.Single(vm.HeldOrders);

        await ((CommunityToolkit.Mvvm.Input.IAsyncRelayCommand)heldItem.RecallCommand).ExecuteAsync(null);

        var restored = Assert.Single(vm.CartLines);
        Assert.Equal(2, restored.Qty);
        Assert.Empty(vm.HeldOrders);
    }

    [Fact]
    public async Task HoldOrder_UpToTwo_BothSucceed()
    {
        var vm = CreateViewModel(out _, out var held);
        await vm.LoadAsync();

        vm.VisibleProducts[0].AddCommand.Execute(null);
        await vm.HoldOrderCommand.ExecuteAsync(null);
        Assert.Single(vm.HeldOrders);

        vm.VisibleProducts[0].AddCommand.Execute(null);
        await vm.HoldOrderCommand.ExecuteAsync(null);
        Assert.Equal(2, vm.HeldOrders.Count);
    }

    [Fact]
    public async Task HoldOrder_WhenAlreadyTwoHeld_ShowsMessageAndKeepsCurrentCartIntact()
    {
        var vm = CreateViewModel(out _, out var held);
        await vm.LoadAsync();
        vm.VisibleProducts[0].AddCommand.Execute(null);
        await vm.HoldOrderCommand.ExecuteAsync(null);
        vm.VisibleProducts[0].AddCommand.Execute(null);
        await vm.HoldOrderCommand.ExecuteAsync(null);
        Assert.Equal(2, vm.HeldOrders.Count);

        vm.VisibleProducts[0].AddCommand.Execute(null);
        var toastValues = new List<string?>();
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(PosViewModel.ToastMessage) && vm.ToastMessage is not null)
                toastValues.Add(vm.ToastMessage);
        };
        await vm.HoldOrderCommand.ExecuteAsync(null);

        Assert.Equal(2, vm.HeldOrders.Count);
        var restoredCartLine = Assert.Single(vm.CartLines);
        Assert.Equal(1, restoredCartLine.Qty);
        Assert.Equal("보류는 2건만 가능합니다", Assert.Single(toastValues));
    }
}
