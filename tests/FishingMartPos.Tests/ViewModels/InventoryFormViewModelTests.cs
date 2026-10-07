using System.IO;
using FishingMartPos.Models;
using FishingMartPos.Navigation;
using FishingMartPos.Services;
using FishingMartPos.Tests.Fakes;
using FishingMartPos.ViewModels;
using Xunit;

namespace FishingMartPos.Tests.ViewModels;

public class InventoryFormViewModelTests
{
    private static string CreateTempFile(string extension, int sizeBytes)
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}{extension}");
        File.WriteAllBytes(path, new byte[sizeBytes]);
        return path;
    }

    private static Dictionary<string, IReadOnlyList<CodeItem>> SampleCodes() => new()
    {
        ["POSCAT"] = new List<CodeItem> { new() { Code = "BAIT", Name = "미끼", SortNo = 1 } },
    };

    private static (InventoryFormViewModel vm, FakeProductRepository products, FakeCodeRepository codes, FakeProductPhotoStorage photoStorage, FakePhotoPicker photoPicker, INavigationService navigation, InventoryViewModel inventoryVm)
        CreateForAdd()
    {
        var products = new FakeProductRepository(new List<Product>
        {
            new() { Barcode = "8800000020001", PosCatCd = "BAIT", Name = "지렁이", Price = 5000, StockQty = 10 },
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

        var vm = new InventoryFormViewModel(products, codes, photoPicker, photoStorage, new FakeDelayProvider(), navigation, inventoryVm, editingProduct: null);
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

        var vm = new InventoryFormViewModel(products, codes, photoPicker, photoStorage, new FakeDelayProvider(), navigation, inventoryVm, editingProduct: editing);
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
        var editing = new Product { Barcode = "8800000020001", PosCatCd = "BAIT", Name = "지렁이", Price = 5000, StockQty = 10 };

        var vm = new InventoryFormViewModel(products, codes, photoPicker, photoStorage, new FakeDelayProvider(), navigation, inventoryVm, editingProduct: editing);
        await vm.LoadAsync();

        Assert.True(vm.IsEditMode);
        Assert.Equal("지렁이", vm.Name);
        Assert.Equal("8800000020001", vm.BarcodeInput);
        Assert.Equal("5,000", vm.PriceInput);
        Assert.Equal("10", vm.StockInput);
        Assert.Equal(editing.PosCatCd, vm.PosCatCd);
    }

    [Fact]
    public async Task LoadAsync_AddMode_DefaultsShowInGridToTrue()
    {
        var (vm, _, _, _, _, _, _) = CreateForAdd();

        await vm.LoadAsync();

        Assert.True(vm.ShowInGrid);
    }

    [Fact]
    public async Task LoadAsync_EditMode_FillsShowInGridFromExistingProduct()
    {
        var editing = new Product
        {
            Barcode = "8800000020001", PosCatCd = "BAIT", Name = "지렁이", Price = 5000, StockQty = 10,
            ShowInGrid = false,
        };
        var (vm, _, _, _, _, _, _) = CreateForEdit(editing);

        await vm.LoadAsync();

        Assert.False(vm.ShowInGrid);
    }

    [Fact]
    public async Task Save_AddMode_DefaultsShowInGridToTrue()
    {
        var (vm, products, _, _, _, _, _) = CreateForAdd();
        await vm.LoadAsync();
        vm.Name = "새우";
        vm.PriceInput = "3000";

        await vm.SaveCommand.ExecuteAsync(null);

        var saved = Assert.Single(products.SavedProducts);
        Assert.True(saved.ShowInGrid);
    }

    [Fact]
    public async Task Save_AddMode_WhenUnchecked_PersistsShowInGridFalse()
    {
        var (vm, products, _, _, _, _, _) = CreateForAdd();
        await vm.LoadAsync();
        vm.Name = "새우";
        vm.PriceInput = "3000";
        vm.ShowInGrid = false;

        await vm.SaveCommand.ExecuteAsync(null);

        var saved = Assert.Single(products.SavedProducts);
        Assert.False(saved.ShowInGrid);
    }

    [Fact]
    public async Task Save_EditMode_WithoutTouchingShowInGrid_PreservesOriginalValue()
    {
        // 상품등록/수정 화면이 ShowInGrid를 처리하지 않던 버그: 단가 등 다른 값만 고쳐도
        // 판매화면 노출여부가 항상 꺼짐(N)으로 저장되던 문제의 회귀 테스트.
        var editing = new Product
        {
            Barcode = "8800000020001", PosCatCd = "BAIT", Name = "지렁이", Price = 5000, StockQty = 10,
            ShowInGrid = true,
        };
        var (vm, products, _, _, _, _, _) = CreateForEdit(editing);
        await vm.LoadAsync();
        vm.PriceInput = "6000"; // 노출여부 체크박스는 건드리지 않음

        await vm.SaveCommand.ExecuteAsync(null);

        var saved = Assert.Single(products.SavedProducts);
        Assert.True(saved.ShowInGrid);
    }

    [Fact]
    public async Task LoadAsync_EditMode_FillsSortNoInputFromExistingProduct()
    {
        var editing = new Product
        {
            Barcode = "8800000020001", PosCatCd = "BAIT", Name = "지렁이", Price = 5000, StockQty = 10,
            SortNo = 7,
        };
        var (vm, _, _, _, _, _, _) = CreateForEdit(editing);

        await vm.LoadAsync();

        Assert.Equal("7", vm.SortNoInput);
    }

    [Fact]
    public async Task LoadAsync_AddMode_DefaultsSortNoInputToZero()
    {
        var (vm, _, _, _, _, _, _) = CreateForAdd();

        await vm.LoadAsync();

        Assert.Equal("0", vm.SortNoInput);
    }

    [Fact]
    public async Task Save_EditMode_WithoutTouchingSortNoInput_PreservesExistingSortNo()
    {
        // 재고관리 화면 ▲▼나 직접 입력으로 바꿔둔 같은 분류 안 노출 순서가, 단가 등 다른 값만
        // 고쳐도 0으로 리셋되지 않아야 한다.
        var editing = new Product
        {
            Barcode = "8800000020001", PosCatCd = "BAIT", Name = "지렁이", Price = 5000, StockQty = 10,
            SortNo = 7,
        };
        var (vm, products, _, _, _, _, _) = CreateForEdit(editing);
        await vm.LoadAsync();
        vm.PriceInput = "6000"; // 노출순서는 건드리지 않음

        await vm.SaveCommand.ExecuteAsync(null);

        var saved = Assert.Single(products.SavedProducts);
        Assert.Equal(7, saved.SortNo);
    }

    [Fact]
    public async Task Save_EditMode_WhenSortNoInputChanged_PersistsTypedValue()
    {
        // 핵심 기능: ▲▼를 여러 번 누르지 않고 숫자를 직접 입력해서 바로 노출 순서를 정할 수 있어야 한다.
        var editing = new Product
        {
            Barcode = "8800000020001", PosCatCd = "BAIT", Name = "지렁이", Price = 5000, StockQty = 10,
            SortNo = 7,
        };
        var (vm, products, _, _, _, _, _) = CreateForEdit(editing);
        await vm.LoadAsync();

        vm.SortNoInput = "2";
        await vm.SaveCommand.ExecuteAsync(null);

        var saved = Assert.Single(products.SavedProducts);
        Assert.Equal(2, saved.SortNo);
    }

    [Fact]
    public async Task Save_AddMode_NewProductStartsAtSortNoZero()
    {
        var (vm, products, _, _, _, _, _) = CreateForAdd();
        await vm.LoadAsync();
        vm.Name = "새우";
        vm.PriceInput = "3000";

        await vm.SaveCommand.ExecuteAsync(null);

        var saved = Assert.Single(products.SavedProducts);
        Assert.Equal(0, saved.SortNo);
    }

    [Fact]
    public async Task Save_AddMode_WithTypedSortNo_PersistsThatValue()
    {
        var (vm, products, _, _, _, _, _) = CreateForAdd();
        await vm.LoadAsync();
        vm.Name = "새우";
        vm.PriceInput = "3000";
        vm.SortNoInput = "5";

        await vm.SaveCommand.ExecuteAsync(null);

        var saved = Assert.Single(products.SavedProducts);
        Assert.Equal(5, saved.SortNo);
    }

    [Fact]
    public async Task Save_WithNonNumericSortNoInput_DefaultsToZero()
    {
        var (vm, products, _, _, _, _, _) = CreateForAdd();
        await vm.LoadAsync();
        vm.Name = "새우";
        vm.PriceInput = "3000";
        vm.SortNoInput = "abc";

        await vm.SaveCommand.ExecuteAsync(null);

        var saved = Assert.Single(products.SavedProducts);
        Assert.Equal(0, saved.SortNo);
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
        vm.PosCatCd = "BAIT";

        await vm.SaveCommand.ExecuteAsync(null);

        var saved = Assert.Single(products.SavedProducts);
        Assert.Equal("70000", saved.Barcode);
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
    public async Task Save_AddMode_ShowsSavedToast()
    {
        var (vm, _, _, _, _, _, _) = CreateForAdd();
        await vm.LoadAsync();
        vm.Name = "새우";
        vm.PriceInput = "3000";
        var toastValues = new List<string?>();
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(InventoryFormViewModel.ToastMessage) && vm.ToastMessage is not null)
                toastValues.Add(vm.ToastMessage);
        };

        await vm.SaveCommand.ExecuteAsync(null);

        Assert.Equal("저장되었습니다", Assert.Single(toastValues));
    }

    [Fact]
    public async Task Save_EditMode_ShowsUpdatedToast()
    {
        var editing = new Product
        {
            Barcode = "8800000020001",
            PosCatCd = "BAIT",
            Name = "지렁이",
            Price = 5000,
            StockQty = 10,
        };
        var (vm, _, _, _, _, _, _) = CreateForEdit(editing);
        await vm.LoadAsync();
        var toastValues = new List<string?>();
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(InventoryFormViewModel.ToastMessage) && vm.ToastMessage is not null)
                toastValues.Add(vm.ToastMessage);
        };

        await vm.SaveCommand.ExecuteAsync(null);

        Assert.Equal("수정되었습니다", Assert.Single(toastValues));
    }

    [Fact]
    public async Task PickPhoto_ThenSave_StoresPhotoAndSetsPhotoPath()
    {
        var (vm, products, _, photoStorage, photoPicker, _, _) = CreateForAdd();
        await vm.LoadAsync();
        vm.Name = "새우";
        vm.PriceInput = "3000";
        var tempPhoto = CreateTempFile(".jpg", 100);
        try
        {
            photoPicker.NextPickedPath = tempPhoto;

            vm.PickPhotoCommand.Execute(null);
            await vm.SaveCommand.ExecuteAsync(null);

            var saved = Assert.Single(products.SavedProducts);
            Assert.Single(photoStorage.SaveCalls);
            Assert.Equal(saved.Barcode, photoStorage.SaveCalls[0].Barcode);
            Assert.Equal($"ProductPhotos/{saved.Barcode}.jpg", saved.PhotoPath);
        }
        finally
        {
            File.Delete(tempPhoto);
        }
    }

    [Fact]
    public async Task Save_EditMode_KeepsOriginalBarcodeAndPreservesPhotoPath()
    {
        var editing = new Product
        {
            Barcode = "8800000020001",
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
        var tempPhoto = CreateTempFile(".jpg", 100);
        try
        {
            photoPicker.NextPickedPath = tempPhoto;

            vm.PickPhotoCommand.Execute(null);

            Assert.Equal(tempPhoto, vm.PhotoPreviewPath);
        }
        finally
        {
            File.Delete(tempPhoto);
        }
    }

    [Fact]
    public async Task PickPhoto_WithDisallowedExtension_ShowsErrorAndDoesNotChangePreview()
    {
        var (vm, _, _, _, photoPicker, _, _) = CreateForAdd();
        await vm.LoadAsync();
        var tempFile = CreateTempFile(".txt", 100);
        try
        {
            photoPicker.NextPickedPath = tempFile;

            vm.PickPhotoCommand.Execute(null);

            Assert.Equal("이미지 파일(jpg, jpeg, png)만 업로드할 수 있습니다", vm.ErrorMessage);
            Assert.Null(vm.PhotoPreviewPath);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task PickPhoto_WithFileOverSizeLimit_ShowsErrorAndDoesNotChangePreview()
    {
        var (vm, _, _, _, photoPicker, _, _) = CreateForAdd();
        await vm.LoadAsync();
        var oversizedFile = CreateTempFile(".jpg", 5 * 1024 * 1024 + 1);
        try
        {
            photoPicker.NextPickedPath = oversizedFile;

            vm.PickPhotoCommand.Execute(null);

            Assert.Equal("이미지 용량은 5MB 이하만 가능합니다", vm.ErrorMessage);
            Assert.Null(vm.PhotoPreviewPath);
        }
        finally
        {
            File.Delete(oversizedFile);
        }
    }

    [Fact]
    public async Task PickPhoto_WithValidImage_ClearsPriorErrorMessage()
    {
        var (vm, _, _, _, photoPicker, _, _) = CreateForAdd();
        await vm.LoadAsync();
        var badFile = CreateTempFile(".txt", 100);
        var goodFile = CreateTempFile(".png", 100);
        try
        {
            photoPicker.NextPickedPath = badFile;
            vm.PickPhotoCommand.Execute(null);
            Assert.NotNull(vm.ErrorMessage);

            photoPicker.NextPickedPath = goodFile;
            vm.PickPhotoCommand.Execute(null);

            Assert.Null(vm.ErrorMessage);
            Assert.Equal(goodFile, vm.PhotoPreviewPath);
        }
        finally
        {
            File.Delete(badFile);
            File.Delete(goodFile);
        }
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
    public async Task AddPosCatCode_AddsToPosCatCodesAndRepository()
    {
        var (vm, _, codes, _, _, _, _) = CreateForAdd();
        await vm.LoadAsync();
        vm.NewCodeCode = "TACKLE";
        vm.NewCodeName = "채비";

        await vm.AddPosCatCodeCommand.ExecuteAsync(null);

        Assert.Contains(vm.PosCatCodes, c => c.Code == "TACKLE" && c.Name == "채비");
        Assert.Contains((await codes.GetByGroupAsync("POSCAT")), c => c.Code == "TACKLE");
    }

    [Fact]
    public async Task AddPosCatCode_WithDuplicateCode_ShowsErrorAndDoesNotAddDuplicate()
    {
        var (vm, _, codes, _, _, _, _) = CreateForAdd();
        await vm.LoadAsync();
        vm.NewCodeCode = "BAIT";
        vm.NewCodeName = "중복코드";

        await vm.AddPosCatCodeCommand.ExecuteAsync(null);

        Assert.Equal("이미 존재하는 코드입니다", vm.ErrorMessage);
        Assert.Single(vm.PosCatCodes, c => c.Code == "BAIT");
        var posCatCodesInRepo = await codes.GetByGroupAsync("POSCAT");
        Assert.Single(posCatCodesInRepo, c => c.Code == "BAIT");
    }

    [Fact]
    public async Task DeletePosCatCode_WhenUnused_RemovesFromListAndRepository()
    {
        var (vm, _, codes, _, _, _, _) = CreateForAdd();
        await vm.LoadAsync();
        var unusedCode = new CodeItem { Code = "UNUSED", Name = "미사용", SortNo = 9 };

        // Add the code to the repository first so there's something to delete
        await codes.AddAsync("POSCAT", "UNUSED", "미사용");
        vm.PosCatCodes.Add(unusedCode);

        await vm.DeletePosCatCodeCommand.ExecuteAsync(unusedCode);

        // Verify removed from local list
        Assert.DoesNotContain(vm.PosCatCodes, c => c.Code == "UNUSED");

        // Verify removed from repository
        var posCatCodesInRepo = await codes.GetByGroupAsync("POSCAT");
        Assert.DoesNotContain(posCatCodesInRepo, c => c.Code == "UNUSED");
    }

    [Fact]
    public async Task DeletePosCatCode_WhenUsedByExistingProduct_ShowsErrorAndKeepsCode()
    {
        var (vm, _, codes, _, _, _, _) = CreateForAdd(); // 샘플 상품이 PosCatCd="BAIT" 사용 중
        await vm.LoadAsync();
        var usedCode = vm.PosCatCodes.Single(c => c.Code == "BAIT");

        await vm.DeletePosCatCodeCommand.ExecuteAsync(usedCode);

        // Verify error message was set
        Assert.NotNull(vm.ErrorMessage);

        // Verify kept in local list
        Assert.Contains(vm.PosCatCodes, c => c.Code == "BAIT");

        // Verify kept in repository (delete should not have been called)
        var posCatCodesInRepo = await codes.GetByGroupAsync("POSCAT");
        Assert.Contains(posCatCodesInRepo, c => c.Code == "BAIT");
    }
}
