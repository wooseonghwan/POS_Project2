using FishingMartPos.Models;
using FishingMartPos.Services;
using FishingMartPos.Tests.Fakes;
using FishingMartPos.ViewModels;
using Xunit;

namespace FishingMartPos.Tests.ViewModels;

public class PosViewModelTests
{
    private static readonly Product Bait1 = new()
    {
        Barcode = "B1", MajorCd = "FISH", MinorCd = "BAIT", PosCatCd = "BAIT", Name = "지렁이", Price = 5000, StockQty = 50
    };
    private static readonly Product Float1 = new()
    {
        Barcode = "F1", MajorCd = "FISH", MinorCd = "TACKLE", PosCatCd = "FLOAT", Name = "막대찌 세트", Price = 8000, StockQty = 50
    };

    private static PosViewModel CreateViewModel(out FakeSalesRepository sales, out FakeHeldOrderRepository held)
    {
        sales = new FakeSalesRepository();
        held = new FakeHeldOrderRepository();
        var codes = new Dictionary<string, IReadOnlyList<CodeItem>>
        {
            ["POSCAT"] = new List<CodeItem>
            {
                new() { Code = "BAIT", Name = "미끼", SortNo = 1 },
                new() { Code = "FLOAT", Name = "찌세트", SortNo = 2 },
            },
        };
        var session = new FishingMartPos.Services.CurrentSession();
        session.SignIn(
            new Staff { StaffCode = "ADMIN1", StaffName = "관리자", Role = "ADMIN", UseYn = "Y" },
            new PosTerminal { PosCode = "1", PosName = "POS1" });

        return new PosViewModel(
            new FakeProductRepository(new[] { Bait1, Float1 }),
            new FakeCodeRepository(codes),
            sales,
            held,
            new FakeDelayProvider(),
            session);
    }

    [Fact]
    public async Task LoadAsync_PopulatesCategoriesAndFirstCategoryProducts()
    {
        var vm = CreateViewModel(out _, out _);

        await vm.LoadAsync();

        Assert.Equal(2, vm.Categories.Count);
        Assert.Single(vm.VisibleProducts);
        Assert.Equal("지렁이", vm.VisibleProducts[0].Name);
    }

    [Fact]
    public async Task SelectingCategory_FiltersVisibleProducts()
    {
        var vm = CreateViewModel(out _, out _);
        await vm.LoadAsync();

        vm.Categories[1].SelectCommand.Execute(null);

        Assert.Single(vm.VisibleProducts);
        Assert.Equal("막대찌 세트", vm.VisibleProducts[0].Name);
    }

    [Fact]
    public async Task AddingProductToCart_CreatesCartLineAndUpdatesTotal()
    {
        var vm = CreateViewModel(out _, out _);
        await vm.LoadAsync();

        vm.VisibleProducts[0].AddCommand.Execute(null);

        var line = Assert.Single(vm.CartLines);
        Assert.Equal("지렁이", line.Name);
        Assert.Equal(1, line.Qty);
        Assert.Equal("5,000원", vm.TotalAmountStr);
    }

    [Fact]
    public async Task AddingSameProductTwice_IncrementsQty()
    {
        var vm = CreateViewModel(out _, out _);
        await vm.LoadAsync();

        vm.VisibleProducts[0].AddCommand.Execute(null);
        vm.VisibleProducts[0].AddCommand.Execute(null);

        var line = Assert.Single(vm.CartLines);
        Assert.Equal(2, line.Qty);
    }

    [Fact]
    public async Task IncSelectedAndDecSelected_AdjustQty()
    {
        var vm = CreateViewModel(out _, out _);
        await vm.LoadAsync();
        vm.VisibleProducts[0].AddCommand.Execute(null);

        vm.IncSelectedCommand.Execute(null);
        vm.IncSelectedCommand.Execute(null);
        vm.DecSelectedCommand.Execute(null);

        Assert.Equal(2, vm.CartLines[0].Qty);
    }

    [Fact]
    public async Task RemoveSelected_DeletesLine()
    {
        var vm = CreateViewModel(out _, out _);
        await vm.LoadAsync();
        vm.VisibleProducts[0].AddCommand.Execute(null);

        vm.RemoveSelectedCommand.Execute(null);

        Assert.Empty(vm.CartLines);
    }

    [Fact]
    public async Task PressKey_WhenLineSelected_SetsExactQty()
    {
        var vm = CreateViewModel(out _, out _);
        await vm.LoadAsync();
        vm.VisibleProducts[0].AddCommand.Execute(null); // 선택 상태가 됨

        vm.PressKeyCommand.Execute("5");

        Assert.Equal(5, vm.CartLines[0].Qty);
    }

    [Fact]
    public async Task PressKey_WhenNoLineSelected_BuildsCashInput()
    {
        var vm = CreateViewModel(out _, out _);
        await vm.LoadAsync();

        vm.PressKeyCommand.Execute("1");
        vm.PressKeyCommand.Execute("0");
        vm.PressKeyCommand.Execute("0");
        vm.PressKeyCommand.Execute("0");
        vm.PressKeyCommand.Execute("0");

        Assert.Equal("10,000원", vm.CashInputStr);
    }

    [Fact]
    public async Task ResetOrder_ClearsCartAndCashInput()
    {
        var vm = CreateViewModel(out _, out _);
        await vm.LoadAsync();
        vm.VisibleProducts[0].AddCommand.Execute(null);

        // Enter some cash input to verify it gets cleared
        vm.PressKeyCommand.Execute("1");
        vm.PressKeyCommand.Execute("0");
        vm.PressKeyCommand.Execute("0");
        vm.PressKeyCommand.Execute("0");
        vm.PressKeyCommand.Execute("0");

        vm.ResetOrderCommand.Execute(null);

        Assert.Empty(vm.CartLines);
        Assert.Equal("0원", vm.TotalAmountStr);
        Assert.Equal("0원", vm.CashInputStr);
        Assert.Equal("0원", vm.ChangeStr);
    }
}
