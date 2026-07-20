using FishingMartPos.Models;
using FishingMartPos.Navigation;
using FishingMartPos.Services;
using FishingMartPos.Tests.Fakes;
using FishingMartPos.ViewModels;
using Xunit;

namespace FishingMartPos.Tests.ViewModels;

public class InventoryViewModelTests
{
    private static IReadOnlyList<Product> SampleProducts() => new[]
    {
        new Product { Barcode = "B1", MajorCd = "FISH", MinorCd = "BAIT", PosCatCd = "BAIT", Name = "지렁이", Price = 5000, StockQty = 10 },
        new Product { Barcode = "B2", MajorCd = "FISH", MinorCd = "TACKLE", PosCatCd = "FLOAT", Name = "막대찌 세트", Price = 8000, StockQty = 5 },
    };

    private static Dictionary<string, IReadOnlyList<CodeItem>> SampleCodes() => new()
    {
        ["MAJOR"] = new[] { new CodeItem { Code = "FISH", Name = "낚시용품", SortNo = 1 } },
        ["MINOR"] = new[]
        {
            new CodeItem { Code = "BAIT", Name = "미끼", SortNo = 1 },
            new CodeItem { Code = "TACKLE", Name = "채비", SortNo = 2 },
        },
        ["POSCAT"] = new[]
        {
            new CodeItem { Code = "BAIT", Name = "미끼", SortNo = 1 },
            new CodeItem { Code = "FLOAT", Name = "찌세트", SortNo = 2 },
        },
    };

    private static MainMenuViewModel DummyMainMenu(ICurrentSession session, INavigationService navigation) =>
        new(session, navigation);

    private static (InventoryViewModel vm, FakeProductRepository products, INavigationService navigation, MainMenuViewModel mainMenu) CreateAdmin()
    {
        var session = new CurrentSession();
        session.SignIn(
            new Staff { StaffCode = "ADMIN1", StaffName = "관리자", Role = "ADMIN", UseYn = "Y" },
            new PosTerminal { PosCode = "1", PosName = "POS1" });
        var navigation = new NavigationService();
        var mainMenu = DummyMainMenu(session, navigation);
        var products = new FakeProductRepository(SampleProducts());
        var vm = new InventoryViewModel(products, new FakeCodeRepository(SampleCodes()), session, navigation, mainMenu);
        return (vm, products, navigation, mainMenu);
    }

    private static (InventoryViewModel vm, FakeProductRepository products) CreateStaff()
    {
        var session = new CurrentSession();
        session.SignIn(
            new Staff { StaffCode = "STAFF1", StaffName = "직원", Role = "STAFF", UseYn = "Y" },
            new PosTerminal { PosCode = "1", PosName = "POS1" });
        var navigation = new NavigationService();
        var mainMenu = DummyMainMenu(session, navigation);
        var products = new FakeProductRepository(SampleProducts());
        var vm = new InventoryViewModel(products, new FakeCodeRepository(SampleCodes()), session, navigation, mainMenu);
        return (vm, products);
    }

    [Fact]
    public async Task LoadAsync_PopulatesRowsWithCategoryNamesAndFormattedValues()
    {
        var (vm, _, _, _) = CreateAdmin();

        await vm.LoadAsync();

        Assert.Equal(2, vm.Rows.Count);
        var row = vm.Rows.Single(r => r.Barcode == "B1");
        Assert.Equal("낚시용품", row.MajorName);
        Assert.Equal("미끼", row.MinorName);
        Assert.Equal("미끼", row.PosCatName);
        Assert.Equal("5,000원", row.PriceStr);
        Assert.Equal("10", row.StockQtyStr);
    }

    [Fact]
    public async Task LoadAsync_BuildsCategoryOptionsWithAllFirst()
    {
        var (vm, _, _, _) = CreateAdmin();

        await vm.LoadAsync();

        Assert.Equal("전체", vm.CategoryOptions[0].Name);
        Assert.Equal(3, vm.CategoryOptions.Count); // 전체 + 미끼 + 찌세트
        Assert.Equal(vm.CategoryOptions[0], vm.SelectedCategoryOption);
    }

    [Fact]
    public async Task SearchText_FiltersByNameSubstring()
    {
        var (vm, _, _, _) = CreateAdmin();
        await vm.LoadAsync();

        vm.SearchText = "막대";

        Assert.Single(vm.Rows);
        Assert.Equal("B2", vm.Rows[0].Barcode);
    }

    [Fact]
    public async Task SearchText_FiltersByBarcodeSubstring()
    {
        var (vm, _, _, _) = CreateAdmin();
        await vm.LoadAsync();

        vm.SearchText = "B2";

        Assert.Single(vm.Rows);
        Assert.Equal("B2", vm.Rows[0].Barcode);
    }

    [Fact]
    public async Task SelectedCategoryOption_FiltersByPosCat()
    {
        var (vm, _, _, _) = CreateAdmin();
        await vm.LoadAsync();

        vm.SelectedCategoryOption = vm.CategoryOptions.Single(c => c.Name == "찌세트");

        Assert.Single(vm.Rows);
        Assert.Equal("B2", vm.Rows[0].Barcode);
    }

    [Fact]
    public async Task AdminSession_AllRowsAreDeletable()
    {
        var (vm, _, _, _) = CreateAdmin();
        await vm.LoadAsync();

        Assert.All(vm.Rows, r => Assert.True(r.CanDelete));
    }

    [Fact]
    public async Task StaffSession_NoRowsAreDeletable()
    {
        var (vm, _) = CreateStaff();
        await vm.LoadAsync();

        Assert.All(vm.Rows, r => Assert.False(r.CanDelete));
    }

    [Fact]
    public void GoToMainMenu_NavigatesToInjectedMainMenuViewModel()
    {
        var (vm, _, navigation, mainMenu) = CreateAdmin();

        vm.GoToMainMenuCommand.Execute(null);

        Assert.Same(mainMenu, navigation.CurrentViewModel);
    }
}
