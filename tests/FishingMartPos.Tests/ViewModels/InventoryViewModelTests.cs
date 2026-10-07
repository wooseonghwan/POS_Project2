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
        new Product { Barcode = "B1", PosCatCd = "BAIT", Name = "지렁이", Price = 5000, StockQty = 10 },
        new Product { Barcode = "B2", PosCatCd = "FLOAT", Name = "막대찌 세트", Price = 8000, StockQty = 5 },
    };

    private static IReadOnlyList<Product> ManyProducts(int count) =>
        Enumerable.Range(1, count)
            .Select(i => new Product
            {
                Barcode = $"P{i:D3}", PosCatCd = "BAIT",
                Name = $"상품{i:D3}", Price = 1000, StockQty = 1,
            })
            .ToList();

    private static Dictionary<string, IReadOnlyList<CodeItem>> SampleCodes() => new()
    {
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

    private static InventoryViewModel CreateAdminWithProducts(IReadOnlyList<Product> products)
    {
        var session = new CurrentSession();
        session.SignIn(
            new Staff { StaffCode = "ADMIN1", StaffName = "관리자", Role = "ADMIN", UseYn = "Y" },
            new PosTerminal { PosCode = "1", PosName = "POS1" });
        var navigation = new NavigationService();
        var mainMenu = DummyMainMenu(session, navigation);
        return new InventoryViewModel(new FakeProductRepository(products), new FakeCodeRepository(SampleCodes()), session, navigation, mainMenu);
    }

    private static (InventoryViewModel vm, FakeProductRepository products, INavigationService navigation) CreateStaff()
    {
        var session = new CurrentSession();
        session.SignIn(
            new Staff { StaffCode = "STAFF1", StaffName = "직원", Role = "STAFF", UseYn = "Y" },
            new PosTerminal { PosCode = "1", PosName = "POS1" });
        var navigation = new NavigationService();
        var mainMenu = DummyMainMenu(session, navigation);
        var products = new FakeProductRepository(SampleProducts());
        var vm = new InventoryViewModel(products, new FakeCodeRepository(SampleCodes()), session, navigation, mainMenu);
        return (vm, products, navigation);
    }

    [Fact]
    public async Task LoadAsync_PopulatesRowsWithCategoryNamesAndFormattedValues()
    {
        var (vm, _, _, _) = CreateAdmin();

        await vm.LoadAsync();

        Assert.Equal(2, vm.Rows.Count);
        var row = vm.Rows.Single(r => r.Barcode == "B1");
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
        var (vm, _, _) = CreateStaff();
        await vm.LoadAsync();

        Assert.All(vm.Rows, r => Assert.False(r.CanDelete));
    }

    [Fact]
    public async Task StaffSession_DeleteCommandDoesNotOpenConfirmModal()
    {
        var (vm, _, _) = CreateStaff();
        await vm.LoadAsync();
        var row = vm.Rows.First();

        row.DeleteCommand.Execute(null);

        Assert.False(vm.IsDeleteConfirmVisible);
    }

    [Fact]
    public async Task AdminSession_AllRowsAreEditable()
    {
        var (vm, _, _, _) = CreateAdmin();
        await vm.LoadAsync();

        Assert.All(vm.Rows, r => Assert.True(r.CanEdit));
    }

    [Fact]
    public async Task StaffSession_NoRowsAreEditable()
    {
        var (vm, _, _) = CreateStaff();
        await vm.LoadAsync();

        Assert.All(vm.Rows, r => Assert.False(r.CanEdit));
    }

    [Fact]
    public async Task StaffSession_GoToAddProductDoesNotNavigate()
    {
        var (vm, _, navigation) = CreateStaff();
        await vm.LoadAsync();
        bool factoryInvoked = false;
        vm.InventoryFormViewModelFactory = (inv, product) =>
        {
            factoryInvoked = true;
            return Task.FromResult<InventoryFormViewModel>(null!);
        };

        await vm.GoToAddProductCommand.ExecuteAsync(null);

        Assert.False(factoryInvoked);
        Assert.Null(navigation.CurrentViewModel);
    }

    [Fact]
    public async Task StaffSession_RowEditCommandDoesNotNavigate()
    {
        var (vm, _, navigation) = CreateStaff();
        await vm.LoadAsync();
        bool factoryInvoked = false;
        vm.InventoryFormViewModelFactory = (inv, product) =>
        {
            factoryInvoked = true;
            return Task.FromResult<InventoryFormViewModel>(null!);
        };
        var row = vm.Rows.First();

        row.EditCommand.Execute(null);
        await Task.Delay(1);

        Assert.False(factoryInvoked);
        Assert.Null(navigation.CurrentViewModel);
    }

    [Fact]
    public void GoToMainMenu_NavigatesToInjectedMainMenuViewModel()
    {
        var (vm, _, navigation, mainMenu) = CreateAdmin();

        vm.GoToMainMenuCommand.Execute(null);

        Assert.Same(mainMenu, navigation.CurrentViewModel);
    }

    [Fact]
    public async Task DeleteCommand_ShowsConfirmationModalWithProductName()
    {
        var (vm, _, _, _) = CreateAdmin();
        await vm.LoadAsync();
        var row = vm.Rows.Single(r => r.Barcode == "B1");

        row.DeleteCommand.Execute(null);

        Assert.True(vm.IsDeleteConfirmVisible);
        Assert.Equal("지렁이", vm.PendingDeleteName);
    }

    [Fact]
    public async Task ConfirmDelete_DeactivatesProductAndRemovesRowAndClosesModal()
    {
        var (vm, products, _, _) = CreateAdmin();
        await vm.LoadAsync();
        var row = vm.Rows.Single(r => r.Barcode == "B1");
        row.DeleteCommand.Execute(null);

        await vm.ConfirmDeleteCommand.ExecuteAsync(null);

        Assert.Contains("B1", products.DeactivatedBarcodes);
        Assert.DoesNotContain(vm.Rows, r => r.Barcode == "B1");
        Assert.False(vm.IsDeleteConfirmVisible);
    }

    [Fact]
    public async Task CancelDelete_LeavesRowIntactAndClosesModal()
    {
        var (vm, products, _, _) = CreateAdmin();
        await vm.LoadAsync();
        var row = vm.Rows.Single(r => r.Barcode == "B1");
        row.DeleteCommand.Execute(null);

        vm.CancelDeleteCommand.Execute(null);

        Assert.Empty(products.DeactivatedBarcodes);
        Assert.Contains(vm.Rows, r => r.Barcode == "B1");
        Assert.False(vm.IsDeleteConfirmVisible);
    }

    [Fact]
    public async Task GoToAddProduct_InvokesFactoryWithNullProductAndNavigates()
    {
        var (vm, _, navigation, _) = CreateAdmin();
        await vm.LoadAsync();
        Product? capturedProduct = new Product { Barcode = "SENTINEL", PosCatCd = "X", Name = "sentinel", Price = 1, StockQty = 0 };
        InventoryFormViewModel? formVm = null;
        vm.InventoryFormViewModelFactory = (inv, product) =>
        {
            capturedProduct = product;
            formVm = new InventoryFormViewModel(
                new FakeProductRepository(Array.Empty<Product>()),
                new FakeCodeRepository(new Dictionary<string, IReadOnlyList<CodeItem>>()),
                new FakePhotoPicker(), new FakeProductPhotoStorage(), new FakeDelayProvider(), navigation, inv, product);
            return Task.FromResult(formVm);
        };

        await vm.GoToAddProductCommand.ExecuteAsync(null);

        Assert.Null(capturedProduct);
        Assert.Same(formVm, navigation.CurrentViewModel);
    }

    [Fact]
    public async Task RefreshRows_ProductWithPhotoPath_SetsPhotoAbsolutePath()
    {
        var session = new CurrentSession();
        session.SignIn(new Staff { StaffCode = "ADMIN1", StaffName = "관리자", Role = "ADMIN", UseYn = "Y" }, new PosTerminal { PosCode = "1", PosName = "POS1" });
        var navigation = new NavigationService();
        var mainMenu = DummyMainMenu(session, navigation);
        var productsWithPhoto = new[]
        {
            new Product { Barcode = "B1", PosCatCd = "BAIT", Name = "지렁이", Price = 5000, StockQty = 10, PhotoPath = "ProductPhotos/B1.jpg" },
        };
        var vm = new InventoryViewModel(new FakeProductRepository(productsWithPhoto), new FakeCodeRepository(SampleCodes()), session, navigation, mainMenu);
        await vm.LoadAsync();

        var row = Assert.Single(vm.Rows);
        Assert.EndsWith("ProductPhotos/B1.jpg", row.PhotoAbsolutePath);
    }

    [Fact]
    public async Task RowEditCommand_InvokesFactoryWithThatProductAndNavigates()
    {
        var (vm, _, navigation, _) = CreateAdmin();
        await vm.LoadAsync();
        Product? capturedProduct = null;
        InventoryFormViewModel? formVm = null;
        vm.InventoryFormViewModelFactory = (inv, product) =>
        {
            capturedProduct = product;
            formVm = new InventoryFormViewModel(
                new FakeProductRepository(Array.Empty<Product>()),
                new FakeCodeRepository(new Dictionary<string, IReadOnlyList<CodeItem>>()),
                new FakePhotoPicker(), new FakeProductPhotoStorage(), new FakeDelayProvider(), navigation, inv, product);
            return Task.FromResult(formVm);
        };
        var row = vm.Rows.Single(r => r.Barcode == "B1");

        row.EditCommand.Execute(null);
        await Task.Delay(1);

        Assert.NotNull(capturedProduct);
        Assert.Equal("B1", capturedProduct!.Barcode);
        Assert.Same(formVm, navigation.CurrentViewModel);
    }

    [Fact]
    public async Task LoadAsync_WithMoreThanOnePageOfProducts_ShowsOnlyFirstPage()
    {
        var vm = CreateAdminWithProducts(ManyProducts(20));

        await vm.LoadAsync();

        Assert.Equal(15, vm.Rows.Count);
        Assert.Equal("P001", vm.Rows[0].Barcode);
        Assert.Equal(2, vm.TotalPages);
        Assert.False(vm.CanGoToPreviousPage);
        Assert.True(vm.CanGoToNextPage);
    }

    [Fact]
    public async Task NextPage_ShowsRemainingProducts()
    {
        var vm = CreateAdminWithProducts(ManyProducts(20));
        await vm.LoadAsync();

        vm.NextPageCommand.Execute(null);

        Assert.Equal(5, vm.Rows.Count);
        Assert.Equal("P016", vm.Rows[0].Barcode);
        Assert.True(vm.CanGoToPreviousPage);
        Assert.False(vm.CanGoToNextPage);
    }

    [Fact]
    public async Task NextPage_AtLastPage_DoesNothing()
    {
        var vm = CreateAdminWithProducts(ManyProducts(20));
        await vm.LoadAsync();
        vm.NextPageCommand.Execute(null);

        vm.NextPageCommand.Execute(null);

        Assert.Equal("P016", vm.Rows[0].Barcode);
    }

    [Fact]
    public async Task PreviousPage_AtFirstPage_DoesNothing()
    {
        var vm = CreateAdminWithProducts(ManyProducts(20));
        await vm.LoadAsync();

        vm.PreviousPageCommand.Execute(null);

        Assert.Equal("P001", vm.Rows[0].Barcode);
    }

    [Fact]
    public async Task SearchText_ResetsToFirstPage()
    {
        var vm = CreateAdminWithProducts(ManyProducts(20));
        await vm.LoadAsync();
        vm.NextPageCommand.Execute(null);

        vm.SearchText = "상품0";

        Assert.Equal("P001", vm.Rows[0].Barcode);
    }

    [Fact]
    public async Task SelectedCategoryOption_ResetsToFirstPage()
    {
        var vm = CreateAdminWithProducts(ManyProducts(20));
        await vm.LoadAsync();
        vm.NextPageCommand.Execute(null);

        vm.SelectedCategoryOption = vm.CategoryOptions.Single(c => c.Name == "미끼");

        Assert.Equal("P001", vm.Rows[0].Barcode);
    }

    [Fact]
    public async Task ShowDetail_SetsDetailProductAndShowsPopup()
    {
        var (vm, _, _, _) = CreateAdmin();
        await vm.LoadAsync();
        var row = vm.Rows.Single(r => r.Barcode == "B1");

        row.ShowDetailCommand.Execute(null);

        Assert.True(vm.IsDetailVisible);
        Assert.NotNull(vm.DetailProduct);
        Assert.Equal("지렁이", vm.DetailProduct!.Name);
        Assert.Equal("5,000원", vm.DetailProduct.PriceStr);
    }

    [Fact]
    public async Task CloseDetail_HidesPopup()
    {
        var (vm, _, _, _) = CreateAdmin();
        await vm.LoadAsync();
        var row = vm.Rows.Single(r => r.Barcode == "B1");
        row.ShowDetailCommand.Execute(null);

        vm.CloseDetailCommand.Execute(null);

        Assert.False(vm.IsDetailVisible);
    }

    private static (InventoryViewModel vm, FakeProductRepository products) CreateAdminFilteredToBait(IReadOnlyList<Product> products)
    {
        var session = new CurrentSession();
        session.SignIn(
            new Staff { StaffCode = "ADMIN1", StaffName = "관리자", Role = "ADMIN", UseYn = "Y" },
            new PosTerminal { PosCode = "1", PosName = "POS1" });
        var navigation = new NavigationService();
        var mainMenu = DummyMainMenu(session, navigation);
        var fakeProducts = new FakeProductRepository(products);
        var vm = new InventoryViewModel(fakeProducts, new FakeCodeRepository(SampleCodes()), session, navigation, mainMenu);
        return (vm, fakeProducts);
    }

    [Fact]
    public async Task CanReorder_WhenAllCategoriesSelected_IsFalse()
    {
        var (vm, _, _, _) = CreateAdmin();
        await vm.LoadAsync(); // SelectedCategoryOption은 기본값 "전체"

        Assert.All(vm.Rows, r => Assert.False(r.CanReorder));
    }

    [Fact]
    public async Task CanReorder_WhenSpecificCategorySelectedAndNoSearch_IsTrueForAdmin()
    {
        var (vm, _) = CreateAdminFilteredToBait(ManyProducts(3));
        await vm.LoadAsync();

        vm.SelectedCategoryOption = vm.CategoryOptions.Single(c => c.Name == "미끼");

        Assert.All(vm.Rows, r => Assert.True(r.CanReorder));
    }

    [Fact]
    public async Task CanReorder_WhileSearching_IsFalseEvenWithCategoryFiltered()
    {
        var (vm, _) = CreateAdminFilteredToBait(ManyProducts(3));
        await vm.LoadAsync();
        vm.SelectedCategoryOption = vm.CategoryOptions.Single(c => c.Name == "미끼");

        vm.SearchText = "상품0";

        Assert.All(vm.Rows, r => Assert.False(r.CanReorder));
    }

    [Fact]
    public async Task CanReorder_ForStaffSession_IsFalseEvenWithCategoryFiltered()
    {
        var (vm, _, _) = CreateStaff();
        await vm.LoadAsync();

        vm.SelectedCategoryOption = vm.CategoryOptions.Single(c => c.Name == "미끼");

        Assert.All(vm.Rows, r => Assert.False(r.CanReorder));
    }

    [Fact]
    public async Task MoveUpCommand_SwapsWithPreviousItemAndPersistsOrder()
    {
        var (vm, products) = CreateAdminFilteredToBait(ManyProducts(3));
        await vm.LoadAsync();
        vm.SelectedCategoryOption = vm.CategoryOptions.Single(c => c.Name == "미끼");
        var second = vm.Rows.Single(r => r.Barcode == "P002");

        await ((CommunityToolkit.Mvvm.Input.IAsyncRelayCommand)second.MoveUpCommand).ExecuteAsync(null);

        Assert.Equal(new[] { "P002", "P001", "P003" }, vm.Rows.Select(r => r.Barcode));
        var (posCatCd, barcodes) = Assert.Single(products.SortOrderUpdates);
        Assert.Equal("BAIT", posCatCd);
        Assert.Equal(new[] { "P002", "P001", "P003" }, barcodes);
    }

    [Fact]
    public async Task MoveDownCommand_SwapsWithNextItemAndPersistsOrder()
    {
        var (vm, products) = CreateAdminFilteredToBait(ManyProducts(3));
        await vm.LoadAsync();
        vm.SelectedCategoryOption = vm.CategoryOptions.Single(c => c.Name == "미끼");
        var first = vm.Rows.Single(r => r.Barcode == "P001");

        await ((CommunityToolkit.Mvvm.Input.IAsyncRelayCommand)first.MoveDownCommand).ExecuteAsync(null);

        Assert.Equal(new[] { "P002", "P001", "P003" }, vm.Rows.Select(r => r.Barcode));
        var (posCatCd, barcodes) = Assert.Single(products.SortOrderUpdates);
        Assert.Equal("BAIT", posCatCd);
        Assert.Equal(new[] { "P002", "P001", "P003" }, barcodes);
    }

    [Fact]
    public async Task MoveUpCommand_OnFirstItem_DoesNothing()
    {
        var (vm, products) = CreateAdminFilteredToBait(ManyProducts(3));
        await vm.LoadAsync();
        vm.SelectedCategoryOption = vm.CategoryOptions.Single(c => c.Name == "미끼");
        var first = vm.Rows.Single(r => r.Barcode == "P001");

        await ((CommunityToolkit.Mvvm.Input.IAsyncRelayCommand)first.MoveUpCommand).ExecuteAsync(null);

        Assert.Equal(new[] { "P001", "P002", "P003" }, vm.Rows.Select(r => r.Barcode));
        Assert.Empty(products.SortOrderUpdates);
    }

    [Fact]
    public async Task MoveDownCommand_OnLastItem_DoesNothing()
    {
        var (vm, products) = CreateAdminFilteredToBait(ManyProducts(3));
        await vm.LoadAsync();
        vm.SelectedCategoryOption = vm.CategoryOptions.Single(c => c.Name == "미끼");
        var last = vm.Rows.Single(r => r.Barcode == "P003");

        await ((CommunityToolkit.Mvvm.Input.IAsyncRelayCommand)last.MoveDownCommand).ExecuteAsync(null);

        Assert.Equal(new[] { "P001", "P002", "P003" }, vm.Rows.Select(r => r.Barcode));
        Assert.Empty(products.SortOrderUpdates);
    }
}
