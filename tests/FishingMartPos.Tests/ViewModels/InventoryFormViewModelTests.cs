using FishingMartPos.Models;
using FishingMartPos.Navigation;
using FishingMartPos.Services;
using FishingMartPos.Tests.Fakes;
using FishingMartPos.ViewModels;
using Xunit;

namespace FishingMartPos.Tests.ViewModels;

public class InventoryFormViewModelTests
{
    private static Dictionary<string, IReadOnlyList<CodeItem>> SampleCodes() => new()
    {
        ["MAJOR"] = new List<CodeItem> { new() { Code = "FISH", Name = "낚시용품", SortNo = 1 } },
        ["MINOR"] = new List<CodeItem> { new() { Code = "BAIT", Name = "미끼", SortNo = 1 } },
        ["POSCAT"] = new List<CodeItem> { new() { Code = "BAIT", Name = "미끼", SortNo = 1 } },
    };

    private static (InventoryFormViewModel vm, FakeProductRepository products, FakeCodeRepository codes, FakeProductPhotoStorage photoStorage, FakePhotoPicker photoPicker, INavigationService navigation, InventoryViewModel inventoryVm)
        CreateForAdd()
    {
        var products = new FakeProductRepository(new List<Product>
        {
            new() { Barcode = "8800000020001", MajorCd = "FISH", MinorCd = "BAIT", PosCatCd = "BAIT", Name = "지렁이", Price = 5000, StockQty = 10 },
        });
        var codes = new FakeCodeRepository(SampleCodes());
        var photoStorage = new FakeProductPhotoStorage();
        var photoPicker = new FakePhotoPicker();
        var session = new FishingMartPos.Services.CurrentSession();
        session.SignIn(
            new Staff { StaffCode = "ADMIN1", StaffName = "관리자", Role = "ADMIN", UseYn = "Y" },
            new PosTerminal { PosCode = "1", PosName = "POS1" });
        var navigation = new NavigationService();
        var mainMenu = new MainMenuViewModel(session, navigation);
        var inventoryVm = new InventoryViewModel(products, codes, session, navigation, mainMenu);

        var vm = new InventoryFormViewModel(products, codes, photoPicker, photoStorage, navigation, inventoryVm, editingProduct: null);
        return (vm, products, codes, photoStorage, photoPicker, navigation, inventoryVm);
    }

    private static (InventoryFormViewModel vm, FakeProductRepository products, FakeCodeRepository codes, FakeProductPhotoStorage photoStorage, FakePhotoPicker photoPicker, INavigationService navigation, InventoryViewModel inventoryVm)
        CreateForEdit(Product editing)
    {
        var products = new FakeProductRepository(new List<Product> { editing });
        var codes = new FakeCodeRepository(SampleCodes());
        var photoStorage = new FakeProductPhotoStorage();
        var photoPicker = new FakePhotoPicker();
        var session = new FishingMartPos.Services.CurrentSession();
        session.SignIn(
            new Staff { StaffCode = "ADMIN1", StaffName = "관리자", Role = "ADMIN", UseYn = "Y" },
            new PosTerminal { PosCode = "1", PosName = "POS1" });
        var navigation = new NavigationService();
        var mainMenu = new MainMenuViewModel(session, navigation);
        var inventoryVm = new InventoryViewModel(products, codes, session, navigation, mainMenu);

        var vm = new InventoryFormViewModel(products, codes, photoPicker, photoStorage, navigation, inventoryVm, editingProduct: editing);
        return (vm, products, codes, photoStorage, photoPicker, navigation, inventoryVm);
    }

    [Fact]
    public async Task LoadAsync_AddMode_StartsWithEmptyForm()
    {
        var (vm, _, _, _, _, _, _) = CreateForAdd();

        await vm.LoadAsync();

        Assert.False(vm.IsEditMode);
        Assert.Equal(string.Empty, vm.Name);
        Assert.Equal(string.Empty, vm.BarcodeInput);
    }

    [Fact]
    public async Task LoadAsync_EditMode_FillsFormFromExistingProduct()
    {
        var products = new FakeProductRepository(new List<Product>());
        var codes = new FakeCodeRepository(SampleCodes());
        var photoStorage = new FakeProductPhotoStorage();
        var photoPicker = new FakePhotoPicker();
        var session = new FishingMartPos.Services.CurrentSession();
        session.SignIn(new Staff { StaffCode = "ADMIN1", StaffName = "관리자", Role = "ADMIN", UseYn = "Y" }, new PosTerminal { PosCode = "1", PosName = "POS1" });
        var navigation = new NavigationService();
        var mainMenu = new MainMenuViewModel(session, navigation);
        var inventoryVm = new InventoryViewModel(products, codes, session, navigation, mainMenu);
        var editing = new Product { Barcode = "8800000020001", MajorCd = "FISH", MinorCd = "BAIT", PosCatCd = "BAIT", Name = "지렁이", Price = 5000, StockQty = 10 };

        var vm = new InventoryFormViewModel(products, codes, photoPicker, photoStorage, navigation, inventoryVm, editingProduct: editing);
        await vm.LoadAsync();

        Assert.True(vm.IsEditMode);
        Assert.Equal("지렁이", vm.Name);
        Assert.Equal("8800000020001", vm.BarcodeInput);
        Assert.Equal("5,000", vm.PriceInput);
        Assert.Equal("10", vm.StockInput);
        Assert.Equal(editing.MajorCd, vm.MajorCd);
        Assert.Equal(editing.MinorCd, vm.MinorCd);
        Assert.Equal(editing.PosCatCd, vm.PosCatCd);
    }

    [Fact]
    public async Task Save_WithoutName_ShowsErrorAndDoesNotSave()
    {
        var (vm, products, _, _, _, _, _) = CreateForAdd();
        await vm.LoadAsync();
        vm.PriceInput = "1000";

        await vm.SaveCommand.ExecuteAsync(null);

        Assert.NotNull(vm.ErrorMessage);
        Assert.Empty(products.SavedProducts);
    }

    [Fact]
    public async Task Save_WithoutPrice_ShowsErrorAndDoesNotSave()
    {
        var (vm, products, _, _, _, _, _) = CreateForAdd();
        await vm.LoadAsync();
        vm.Name = "새우";

        await vm.SaveCommand.ExecuteAsync(null);

        Assert.NotNull(vm.ErrorMessage);
        Assert.Empty(products.SavedProducts);
    }

    [Fact]
    public async Task Save_WithBlankBarcode_GeneratesNextBarcode()
    {
        var (vm, products, _, _, _, _, _) = CreateForAdd();
        await vm.LoadAsync();
        vm.Name = "새우";
        vm.PriceInput = "3000";
        vm.MajorCd = "FISH";
        vm.MinorCd = "BAIT";
        vm.PosCatCd = "BAIT";

        await vm.SaveCommand.ExecuteAsync(null);

        var saved = Assert.Single(products.SavedProducts);
        Assert.Equal("8800000020002", saved.Barcode);
    }

    [Fact]
    public async Task Save_WithDuplicateManualBarcode_ShowsErrorAndDoesNotSave()
    {
        var (vm, products, _, _, _, _, _) = CreateForAdd();
        await vm.LoadAsync();
        vm.Name = "새우";
        vm.PriceInput = "3000";
        vm.BarcodeInput = "8800000020001"; // 이미 존재

        await vm.SaveCommand.ExecuteAsync(null);

        Assert.NotNull(vm.ErrorMessage);
        Assert.Empty(products.SavedProducts);
    }

    [Fact]
    public async Task Save_WithBlankStock_DefaultsToZero()
    {
        var (vm, products, _, _, _, _, _) = CreateForAdd();
        await vm.LoadAsync();
        vm.Name = "새우";
        vm.PriceInput = "3000";

        await vm.SaveCommand.ExecuteAsync(null);

        var saved = Assert.Single(products.SavedProducts);
        Assert.Equal(0, saved.StockQty);
    }

    [Fact]
    public async Task Save_NavigatesBackToInventoryAndRefreshesRows()
    {
        var (vm, products, _, _, _, navigation, inventoryVm) = CreateForAdd();
        await vm.LoadAsync();
        vm.Name = "새우";
        vm.PriceInput = "3000";

        await vm.SaveCommand.ExecuteAsync(null);

        Assert.Same(inventoryVm, navigation.CurrentViewModel);
        var saved = Assert.Single(products.SavedProducts);
        Assert.Contains(inventoryVm.Rows, row => row.Barcode == saved.Barcode && row.Name == "새우");
    }

    [Fact]
    public async Task PickPhoto_ThenSave_StoresPhotoAndSetsPhotoPath()
    {
        var (vm, products, _, photoStorage, photoPicker, _, _) = CreateForAdd();
        await vm.LoadAsync();
        vm.Name = "새우";
        vm.PriceInput = "3000";
        photoPicker.NextPickedPath = @"C:\temp\shrimp.jpg";

        vm.PickPhotoCommand.Execute(null);
        await vm.SaveCommand.ExecuteAsync(null);

        var saved = Assert.Single(products.SavedProducts);
        Assert.Single(photoStorage.SaveCalls);
        Assert.Equal(saved.Barcode, photoStorage.SaveCalls[0].Barcode);
        Assert.Equal($"ProductPhotos/{saved.Barcode}.jpg", saved.PhotoPath);
    }

    [Fact]
    public async Task Save_EditMode_KeepsOriginalBarcodeAndPreservesPhotoPath()
    {
        var editing = new Product
        {
            Barcode = "8800000020001",
            MajorCd = "FISH",
            MinorCd = "BAIT",
            PosCatCd = "BAIT",
            Name = "지렁이",
            Price = 5000,
            StockQty = 10,
            PhotoPath = "ProductPhotos/8800000020001.jpg",
        };
        var (vm, products, _, _, _, _, _) = CreateForEdit(editing);
        await vm.LoadAsync();

        await vm.SaveCommand.ExecuteAsync(null);

        var saved = Assert.Single(products.SavedProducts);
        Assert.Equal(editing.Barcode, saved.Barcode);
        Assert.Equal(editing.PhotoPath, saved.PhotoPath);
    }

    [Fact]
    public async Task Save_EditMode_OwnUnchangedBarcode_DoesNotTriggerDuplicateError()
    {
        var editing = new Product
        {
            Barcode = "8800000020001",
            MajorCd = "FISH",
            MinorCd = "BAIT",
            PosCatCd = "BAIT",
            Name = "지렁이",
            Price = 5000,
            StockQty = 10,
        };
        var (vm, products, _, _, _, _, _) = CreateForEdit(editing);
        await vm.LoadAsync();

        await vm.SaveCommand.ExecuteAsync(null);

        Assert.Null(vm.ErrorMessage);
        Assert.Single(products.SavedProducts);
    }

    [Fact]
    public async Task LoadAsync_EditMode_WithPhotoPath_SetsPhotoPreviewPath()
    {
        var editing = new Product
        {
            Barcode = "8800000020001",
            MajorCd = "FISH",
            MinorCd = "BAIT",
            PosCatCd = "BAIT",
            Name = "지렁이",
            Price = 5000,
            StockQty = 10,
            PhotoPath = "ProductPhotos/8800000020001.jpg",
        };
        var (vm, _, _, _, _, _, _) = CreateForEdit(editing);

        await vm.LoadAsync();

        Assert.EndsWith("ProductPhotos/8800000020001.jpg", vm.PhotoPreviewPath);
    }

    [Fact]
    public async Task LoadAsync_EditMode_WithoutPhotoPath_LeavesPhotoPreviewPathNull()
    {
        var editing = new Product
        {
            Barcode = "8800000020001",
            MajorCd = "FISH",
            MinorCd = "BAIT",
            PosCatCd = "BAIT",
            Name = "지렁이",
            Price = 5000,
            StockQty = 10,
        };
        var (vm, _, _, _, _, _, _) = CreateForEdit(editing);

        await vm.LoadAsync();

        Assert.Null(vm.PhotoPreviewPath);
    }

    [Fact]
    public async Task PickPhoto_SetsPhotoPreviewPathToPickedFile()
    {
        var (vm, _, _, _, photoPicker, _, _) = CreateForAdd();
        await vm.LoadAsync();
        photoPicker.NextPickedPath = @"C:\temp\shrimp.jpg";

        vm.PickPhotoCommand.Execute(null);

        Assert.Equal(@"C:\temp\shrimp.jpg", vm.PhotoPreviewPath);
    }

    [Fact]
    public async Task PriceInput_WhenTypedAsPlainDigits_IsReformattedWithCommas()
    {
        var (vm, _, _, _, _, _, _) = CreateForAdd();
        await vm.LoadAsync();

        vm.PriceInput = "1234000";

        Assert.Equal("1,234,000", vm.PriceInput);
    }

    [Fact]
    public async Task Save_WithCommaFormattedPrice_ParsesCorrectDecimalValue()
    {
        var (vm, products, _, _, _, _, _) = CreateForAdd();
        await vm.LoadAsync();
        vm.Name = "새우";
        vm.PriceInput = "3000";

        await vm.SaveCommand.ExecuteAsync(null);

        var saved = Assert.Single(products.SavedProducts);
        Assert.Equal(3000m, saved.Price);
    }

    [Fact]
    public async Task AddMajorCode_AddsToMajorCodesAndRepository()
    {
        var (vm, _, codes, _, _, _, _) = CreateForAdd();
        await vm.LoadAsync();
        vm.NewCodeCode = "TACKLE";
        vm.NewCodeName = "채비";

        await vm.AddMajorCodeCommand.ExecuteAsync(null);

        Assert.Contains(vm.MajorCodes, c => c.Code == "TACKLE" && c.Name == "채비");
        Assert.Contains((await codes.GetByGroupAsync("MAJOR")), c => c.Code == "TACKLE");
    }

    [Fact]
    public async Task AddMajorCode_WithDuplicateCode_ShowsErrorAndDoesNotAddDuplicate()
    {
        var (vm, _, codes, _, _, _, _) = CreateForAdd();
        await vm.LoadAsync();
        vm.NewCodeCode = "FISH";
        vm.NewCodeName = "중복코드";

        await vm.AddMajorCodeCommand.ExecuteAsync(null);

        Assert.Equal("이미 존재하는 코드입니다", vm.ErrorMessage);
        Assert.Single(vm.MajorCodes, c => c.Code == "FISH");
        var majorCodesInRepo = await codes.GetByGroupAsync("MAJOR");
        Assert.Single(majorCodesInRepo, c => c.Code == "FISH");
    }

    [Fact]
    public async Task DeleteMajorCode_WhenUnused_RemovesFromListAndRepository()
    {
        var (vm, _, codes, _, _, _, _) = CreateForAdd();
        await vm.LoadAsync();
        var unusedCode = new CodeItem { Code = "UNUSED", Name = "미사용", SortNo = 9 };

        // Add the code to the repository first so there's something to delete
        await codes.AddAsync("MAJOR", "UNUSED", "미사용");
        vm.MajorCodes.Add(unusedCode);

        await vm.DeleteMajorCodeCommand.ExecuteAsync(unusedCode);

        // Verify removed from local list
        Assert.DoesNotContain(vm.MajorCodes, c => c.Code == "UNUSED");

        // Verify removed from repository
        var majorCodesInRepo = await codes.GetByGroupAsync("MAJOR");
        Assert.DoesNotContain(majorCodesInRepo, c => c.Code == "UNUSED");
    }

    [Fact]
    public async Task DeleteMajorCode_WhenUsedByExistingProduct_ShowsErrorAndKeepsCode()
    {
        var (vm, _, codes, _, _, _, _) = CreateForAdd(); // 샘플 상품이 MajorCd="FISH" 사용 중
        await vm.LoadAsync();
        var usedCode = vm.MajorCodes.Single(c => c.Code == "FISH");

        await vm.DeleteMajorCodeCommand.ExecuteAsync(usedCode);

        // Verify error message was set
        Assert.NotNull(vm.ErrorMessage);

        // Verify kept in local list
        Assert.Contains(vm.MajorCodes, c => c.Code == "FISH");

        // Verify kept in repository (delete should not have been called)
        var majorCodesInRepo = await codes.GetByGroupAsync("MAJOR");
        Assert.Contains(majorCodesInRepo, c => c.Code == "FISH");
    }
}
