# 재고관리 화면 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 메인메뉴의 "재고" 타일이 현재 연결된 `PlaceholderViewModel("재고")`를 실제 재고관리 화면으로 대체한다. 상품 목록 조회(사진 대신 색상 스와치/대분류/소분류/POS분류/상품명/바코드/단가/재고), 상품명+바코드 검색, POS분류 드롭다운 필터, 관리자 전용 소프트 삭제(확인모달 포함)까지 실제 MariaDB 연동으로 동작하게 만든다.

**Architecture:** Phase 1/2와 동일한 3계층(View–ViewModel–Repository)을 유지한다. `InventoryViewModel`은 앱 시작 시 상품/코드 전체를 한 번 로드해 캐싱하고, 검색어·카테고리 필터는 `PosViewModel.RefreshVisibleProducts`와 동일하게 클라이언트 사이드에서 재계산한다. 삭제는 `IProductRepository.DeactivateAsync`로 `product_tb.use_yn`을 `'N'`으로 바꾸는 소프트 삭제이며, 관리자(`ICurrentSession.CurrentStaff.IsAdmin`)만 실행할 수 있다.

**Tech Stack:** Phase 1/2와 동일(WPF/.NET8/x86, CommunityToolkit.Mvvm, Dapper, MySqlConnector, xunit).

## Global Constraints

- 원본 디자인 HTML(`Fishing Mart POS.html`)이 저장소에 없어 이 화면은 새로 설계했다(`docs/superpowers/specs/2026-07-21-inventory-screen-design.md` 참고) — 기존 구현 화면(`MainMenuView`/`PosView`)의 톤앤매너(`AppColors`, 카드형 레이아웃)를 그대로 따른다.
- 이번 범위는 **조회 + 검색 + 카테고리 필터 + 소프트 삭제**만 포함한다. 상품 등록/수정, 재고 수량 직접 조정, 사진 업로드는 범위 밖(별도 "상품등록" 화면).
- 삭제는 하드 삭제가 아닌 **소프트 삭제**(`product_tb.use_yn = 'N'`)이며 **관리자만** 실행 가능하다. 삭제 시 확인 모달을 반드시 거친다.
- 목록/검색/필터는 **클라이언트 사이드**에서 처리한다(전체 활성 상품을 한 번 로드해 캐싱 후 필터링) — 상품 수 규모(43개 안팎)에서 서버 왕복 최적화는 불필요.
- 플랫폼/DB/커밋 규칙은 Phase 1 계획 문서(`docs/superpowers/plans/2026-07-21-phase1-foundation.md`)의 Global Constraints를 그대로 따른다: x86 타겟 고정, Dapper + 직접 SQL만 사용(EF Core 금지), 커밋 메시지는 한글로 자연스럽게 작성(타입 접두사 없이).
- 이 저장소는 `C:\AI\pos-project2`, 기본 브랜치 `main`, 로컬 개발 DB `fishingmart_dev`(MariaDB, `db/migrations/001_create_schema.sql` + `002_seed_products.sql` 적용됨).

---

## 파일 구조 (Phase 2 기준 추가/변경분만)

```
src/
  FishingMartPos/
    Repositories/
      IProductRepository.cs                (수정: DeactivateAsync 추가)
      ProductRepository.cs                 (수정: DeactivateAsync 구현)
    ViewModels/
      CategoryFilterOptionViewModel.cs      (신규)
      InventoryRowViewModel.cs              (신규)
      InventoryViewModel.cs                 (신규)
      MainMenuViewModel.cs                  (수정: GoToInventory가 InventoryViewModel로 이동)
      LoginViewModel.cs                     (수정: MainMenuViewModel 생성 시 InventoryViewModelFactory 전달)
    Views/
      InventoryView.xaml / InventoryView.xaml.cs  (신규)
    App.xaml.cs                            (수정: InventoryViewModel 팩토리 DI 배선)
    MainWindow.xaml                        (수정: InventoryViewModel → InventoryView DataTemplate)
tests/
  FishingMartPos.Tests/
    Fakes/FakeProductRepository.cs          (수정: DeactivateAsync 추가 + 호출 추적)
    Repositories/ProductRepositoryTests.cs  (수정: DeactivateAsync 통합 테스트 추가)
    ViewModels/InventoryViewModelTests.cs   (신규)
    ViewModels/MainMenuViewModelTests.cs    (수정: GoToInventory 기대값 변경)
    ViewModels/LoginViewModelTests.cs       (수정: LoginViewModel 생성 인자 추가)
    Views/InventoryViewSmokeTests.cs        (신규)
```

---

### Task 1: Product 소프트 삭제 — Repository 확장 (TDD, MariaDB 연동)

**Files:**
- Modify: `src/FishingMartPos/Repositories/IProductRepository.cs`
- Modify: `src/FishingMartPos/Repositories/ProductRepository.cs`
- Modify: `tests/FishingMartPos.Tests/Fakes/FakeProductRepository.cs`
- Modify: `tests/FishingMartPos.Tests/Repositories/ProductRepositoryTests.cs`

**Interfaces:**
- Consumes: `IDbConnectionFactory`(Phase 1)
- Produces: `IProductRepository.DeactivateAsync(string barcode) : Task` (해당 바코드의 `use_yn`을 `'N'`으로 갱신). `FakeProductRepository.DeactivateAsync`는 호출된 바코드를 `List<string> DeactivatedBarcodes`에 기록하고 내부 캐시에서도 해당 상품을 제거한다(이후 Task에서 `InventoryViewModel` 테스트가 이 목록/캐시를 검증).

- [ ] **Step 1: 인터페이스에 메서드 추가 + Fake를 함께 갱신 (컴파일 유지)**

`IProductRepository`에 새 메서드를 추가하면 이를 구현하는 모든 클래스(`ProductRepository`, 테스트용 `FakeProductRepository`)가 즉시 구현해야 솔루션이 컴파일된다. 두 파일을 한 스텝에서 같이 손댄다.

`src/FishingMartPos/Repositories/IProductRepository.cs` 전체를 다음으로 교체:

```csharp
using FishingMartPos.Models;

namespace FishingMartPos.Repositories;

public interface IProductRepository
{
    Task<IReadOnlyList<Product>> GetActiveAsync();
    Task DeactivateAsync(string barcode);
}
```

`tests/FishingMartPos.Tests/Fakes/FakeProductRepository.cs` 전체를 다음으로 교체:

```csharp
using FishingMartPos.Models;
using FishingMartPos.Repositories;

namespace FishingMartPos.Tests.Fakes;

public sealed class FakeProductRepository : IProductRepository
{
    private readonly List<Product> _products;

    public FakeProductRepository(IReadOnlyList<Product> products)
    {
        _products = products.ToList();
    }

    public List<string> DeactivatedBarcodes { get; } = new();

    public Task<IReadOnlyList<Product>> GetActiveAsync() =>
        Task.FromResult((IReadOnlyList<Product>)_products);

    public Task DeactivateAsync(string barcode)
    {
        DeactivatedBarcodes.Add(barcode);
        _products.RemoveAll(p => p.Barcode == barcode);
        return Task.CompletedTask;
    }
}
```

`ProductRepository`는 아직 `DeactivateAsync`를 구현하지 않은 채로 둔다(다음 스텝에서 실패를 확인하기 위함).

- [ ] **Step 2: 실패하는 통합 테스트 작성**

`tests/FishingMartPos.Tests/Repositories/ProductRepositoryTests.cs` 전체를 다음으로 교체(맨 위 `using Dapper;` 추가, 신규 테스트 추가):

```csharp
using Dapper;
using FishingMartPos.Configuration;
using FishingMartPos.Data;
using FishingMartPos.Repositories;
using Xunit;

namespace FishingMartPos.Tests.Repositories;

public class ProductRepositoryTests
{
    [Fact]
    public async Task GetActive_ReturnsSeededFortyThreeProducts()
    {
        var config = AppConfig.Load(AppContext.BaseDirectory);
        var factory = new MySqlConnectionFactory(config);
        IProductRepository repository = new ProductRepository(factory);

        var products = await repository.GetActiveAsync();

        Assert.Contains(products, p => p.Barcode == "8800000020001" && p.Name == "지렁이" && p.PosCatCd == "BAIT");
        Assert.True(products.Count >= 43);
    }

    [Fact]
    public async Task DeactivateAsync_SetsUseYnToN()
    {
        var config = AppConfig.Load(AppContext.BaseDirectory);
        var factory = new MySqlConnectionFactory(config);
        IProductRepository repository = new ProductRepository(factory);
        const string barcode = "8800000020001";

        await repository.DeactivateAsync(barcode);

        using var connection = factory.CreateOpenConnection();
        string useYn = await connection.QuerySingleAsync<string>(
            "SELECT use_yn FROM product_tb WHERE barcode = @Barcode", new { Barcode = barcode });
        Assert.Equal("N", useYn);

        // 정리: 다른 테스트(GetActive 등)에 영향 주지 않도록 원복
        await connection.ExecuteAsync(
            "UPDATE product_tb SET use_yn = 'Y' WHERE barcode = @Barcode", new { Barcode = barcode });
    }
}
```

- [ ] **Step 3: 테스트 실패 확인**

Run: `dotnet test tests\FishingMartPos.Tests --filter FullyQualifiedName~ProductRepositoryTests`
Expected: 빌드 실패 — `ProductRepository`가 `IProductRepository.DeactivateAsync`를 구현하지 않아 `CS0535` 발생

- [ ] **Step 4: 구현**

`src/FishingMartPos/Repositories/ProductRepository.cs` 전체를 다음으로 교체:

```csharp
using Dapper;
using FishingMartPos.Data;
using FishingMartPos.Models;

namespace FishingMartPos.Repositories;

public sealed class ProductRepository : IProductRepository
{
    private readonly IDbConnectionFactory _connectionFactory;

    public ProductRepository(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<IReadOnlyList<Product>> GetActiveAsync()
    {
        using var connection = _connectionFactory.CreateOpenConnection();
        const string sql = """
            SELECT barcode AS Barcode, major_cd AS MajorCd, minor_cd AS MinorCd, poscat_cd AS PosCatCd,
                   name AS Name, price AS Price, stock_qty AS StockQty
            FROM product_tb
            WHERE use_yn = 'Y'
            ORDER BY poscat_cd, name
            """;

        var result = await connection.QueryAsync<Product>(sql);
        return result.ToList();
    }

    public async Task DeactivateAsync(string barcode)
    {
        using var connection = _connectionFactory.CreateOpenConnection();
        const string sql = "UPDATE product_tb SET use_yn = 'N' WHERE barcode = @Barcode";
        await connection.ExecuteAsync(sql, new { Barcode = barcode });
    }
}
```

- [ ] **Step 5: 테스트 통과 확인**

Run: `dotnet test tests\FishingMartPos.Tests --filter FullyQualifiedName~ProductRepositoryTests`
Expected: PASS (2 tests)

- [ ] **Step 6: 전체 빌드 확인 (Fake 변경이 다른 테스트를 깨지 않는지)**

Run: `dotnet test tests\FishingMartPos.Tests`
Expected: 기존 모든 테스트 PASS (FakeProductRepository 필드 타입 변경이 `PosViewModelTests`/`MainMenuViewModelTests` 등 기존 호출부(`new FakeProductRepository(Array.Empty<Product>())` 등)에 영향 없음 — 생성자 시그니처 동일)

- [ ] **Step 7: Commit**

```bash
git add src/FishingMartPos/Repositories/IProductRepository.cs src/FishingMartPos/Repositories/ProductRepository.cs tests/FishingMartPos.Tests/Fakes/FakeProductRepository.cs tests/FishingMartPos.Tests/Repositories/ProductRepositoryTests.cs
git commit -m "Product 소프트 삭제(DeactivateAsync) Repository 추가"
```

---

### Task 2: InventoryViewModel — 목록 로드 · 검색 · 카테고리 필터 · 메인메뉴 복귀 (TDD)

**Files:**
- Create: `src/FishingMartPos/ViewModels/CategoryFilterOptionViewModel.cs`
- Create: `src/FishingMartPos/ViewModels/InventoryRowViewModel.cs`
- Create: `src/FishingMartPos/ViewModels/InventoryViewModel.cs`
- Test: `tests/FishingMartPos.Tests/ViewModels/InventoryViewModelTests.cs`

**Interfaces:**
- Consumes: `IProductRepository.GetActiveAsync()`/`DeactivateAsync(string)`(Task 1), `ICodeRepository.GetByGroupAsync(string)`(Phase 2), `ICurrentSession.CurrentStaff`(Phase 1), `INavigationService.NavigateTo(object)`(Phase 1), `MainMenuViewModel`(Phase 1, 타입 참조만)
- Produces: `CategoryFilterOptionViewModel { string Code, string Name }`, `InventoryRowViewModel { string Barcode, string Name, string MajorName, string MinorName, string PosCatName, string PriceStr, string StockQtyStr, Brush Swatch, bool CanDelete, ICommand DeleteCommand }`, `InventoryViewModel { ObservableCollection<CategoryFilterOptionViewModel> CategoryOptions, ObservableCollection<InventoryRowViewModel> Rows, string SearchText, CategoryFilterOptionViewModel? SelectedCategoryOption, bool IsAdmin, Task LoadAsync(), GoToMainMenuCommand }` (삭제 확인모달 관련 멤버는 Task 3에서 추가)

- [ ] **Step 1: 모델 작성**

`src/FishingMartPos/ViewModels/CategoryFilterOptionViewModel.cs`:

```csharp
namespace FishingMartPos.ViewModels;

public sealed class CategoryFilterOptionViewModel
{
    public required string Code { get; init; }
    public required string Name { get; init; }
}
```

`src/FishingMartPos/ViewModels/InventoryRowViewModel.cs`:

```csharp
using System.Windows.Input;
using System.Windows.Media;

namespace FishingMartPos.ViewModels;

public sealed class InventoryRowViewModel
{
    public required string Barcode { get; init; }
    public required string Name { get; init; }
    public required string MajorName { get; init; }
    public required string MinorName { get; init; }
    public required string PosCatName { get; init; }
    public required string PriceStr { get; init; }
    public required string StockQtyStr { get; init; }
    public required Brush Swatch { get; init; }
    public required bool CanDelete { get; init; }
    public required ICommand DeleteCommand { get; init; }
}
```

- [ ] **Step 2: 실패하는 테스트 작성**

`tests/FishingMartPos.Tests/ViewModels/InventoryViewModelTests.cs`:

```csharp
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
```

- [ ] **Step 3: 테스트 실패 확인**

Run: `dotnet test tests\FishingMartPos.Tests --filter FullyQualifiedName~InventoryViewModelTests`
Expected: 빌드 실패 (`InventoryViewModel` 타입 없음)

- [ ] **Step 4: 구현**

`src/FishingMartPos/ViewModels/InventoryViewModel.cs`:

```csharp
using System.Collections.ObjectModel;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FishingMartPos.Models;
using FishingMartPos.Navigation;
using FishingMartPos.Repositories;
using FishingMartPos.Services;
using FishingMartPos.Theme;

namespace FishingMartPos.ViewModels;

public sealed partial class InventoryViewModel : ObservableObject
{
    private static readonly string[] SwatchKeys =
    {
        "ProductSwatch0", "ProductSwatch1", "ProductSwatch2", "ProductSwatch3", "ProductSwatch4", "ProductSwatch5",
    };
    private static readonly Brush[] Swatches = BuildSwatches();
    private const string AllCategoriesCode = "";

    private readonly IProductRepository _productRepository;
    private readonly ICodeRepository _codeRepository;
    private readonly ICurrentSession _session;
    private readonly INavigationService _navigation;
    private readonly MainMenuViewModel _mainMenuViewModel;

    private IReadOnlyList<Product> _allProducts = Array.Empty<Product>();
    private Dictionary<string, string> _majorNames = new();
    private Dictionary<string, string> _minorNames = new();
    private Dictionary<string, string> _posCatNames = new();

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private CategoryFilterOptionViewModel? _selectedCategoryOption;

    public ObservableCollection<CategoryFilterOptionViewModel> CategoryOptions { get; } = new();
    public ObservableCollection<InventoryRowViewModel> Rows { get; } = new();

    public bool IsAdmin => _session.CurrentStaff?.IsAdmin ?? false;

    public InventoryViewModel(
        IProductRepository productRepository,
        ICodeRepository codeRepository,
        ICurrentSession session,
        INavigationService navigation,
        MainMenuViewModel mainMenuViewModel)
    {
        _productRepository = productRepository;
        _codeRepository = codeRepository;
        _session = session;
        _navigation = navigation;
        _mainMenuViewModel = mainMenuViewModel;
    }

    partial void OnSearchTextChanged(string value) => RefreshRows();

    partial void OnSelectedCategoryOptionChanged(CategoryFilterOptionViewModel? value) => RefreshRows();

    public async Task LoadAsync()
    {
        _allProducts = await _productRepository.GetActiveAsync();
        var majorCodes = await _codeRepository.GetByGroupAsync("MAJOR");
        var minorCodes = await _codeRepository.GetByGroupAsync("MINOR");
        var posCats = await _codeRepository.GetByGroupAsync("POSCAT");

        _majorNames = majorCodes.ToDictionary(c => c.Code, c => c.Name);
        _minorNames = minorCodes.ToDictionary(c => c.Code, c => c.Name);
        _posCatNames = posCats.ToDictionary(c => c.Code, c => c.Name);

        CategoryOptions.Clear();
        CategoryOptions.Add(new CategoryFilterOptionViewModel { Code = AllCategoriesCode, Name = "전체" });
        foreach (var cat in posCats)
        {
            CategoryOptions.Add(new CategoryFilterOptionViewModel { Code = cat.Code, Name = cat.Name });
        }

        SelectedCategoryOption = CategoryOptions[0];
    }

    private void RefreshRows()
    {
        Rows.Clear();
        bool isAdmin = IsAdmin;
        string categoryFilter = SelectedCategoryOption?.Code ?? AllCategoriesCode;
        string search = SearchText.Trim();

        int swatchIndex = 0;
        foreach (var product in _allProducts)
        {
            if (categoryFilter != AllCategoriesCode && product.PosCatCd != categoryFilter)
            {
                continue;
            }

            if (search.Length > 0 &&
                !product.Name.Contains(search, StringComparison.OrdinalIgnoreCase) &&
                !product.Barcode.Contains(search, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var captured = product;
            Rows.Add(new InventoryRowViewModel
            {
                Barcode = captured.Barcode,
                Name = captured.Name,
                MajorName = _majorNames.TryGetValue(captured.MajorCd, out var majorName) ? majorName : captured.MajorCd,
                MinorName = _minorNames.TryGetValue(captured.MinorCd, out var minorName) ? minorName : captured.MinorCd,
                PosCatName = _posCatNames.TryGetValue(captured.PosCatCd, out var posCatName) ? posCatName : captured.PosCatCd,
                PriceStr = Format(captured.Price),
                StockQtyStr = captured.StockQty.ToString("N0"),
                Swatch = Swatches[swatchIndex % Swatches.Length],
                CanDelete = isAdmin,
                DeleteCommand = new RelayCommand(() => RequestDelete(captured)),
            });
            swatchIndex++;
        }
    }

    private void RequestDelete(Product product)
    {
        // Task 3에서 확인모달 로직으로 대체된다.
    }

    [RelayCommand]
    private void GoToMainMenu() => _navigation.NavigateTo(_mainMenuViewModel);

    private static string Format(decimal amount) => amount.ToString("N0") + "원";

    private static Brush[] BuildSwatches()
    {
        var all = AppColors.BuildBrushes();
        return SwatchKeys.Select(key => all[key]).ToArray();
    }
}
```

- [ ] **Step 5: 테스트 통과 확인**

Run: `dotnet test tests\FishingMartPos.Tests --filter FullyQualifiedName~InventoryViewModelTests`
Expected: PASS (8 tests)

- [ ] **Step 6: Commit**

```bash
git add src/FishingMartPos/ViewModels/CategoryFilterOptionViewModel.cs src/FishingMartPos/ViewModels/InventoryRowViewModel.cs src/FishingMartPos/ViewModels/InventoryViewModel.cs tests/FishingMartPos.Tests/ViewModels/InventoryViewModelTests.cs
git commit -m "InventoryViewModel 추가: 목록 로드/검색/카테고리 필터/메인메뉴 복귀"
```

---

### Task 3: InventoryViewModel — 삭제 확인모달 (TDD)

**Files:**
- Modify: `src/FishingMartPos/ViewModels/InventoryViewModel.cs`
- Modify: `tests/FishingMartPos.Tests/ViewModels/InventoryViewModelTests.cs`

**Interfaces:**
- Consumes: Task 2의 `InventoryViewModel`, `IProductRepository.DeactivateAsync`(Task 1)
- Produces: `InventoryViewModel { bool IsDeleteConfirmVisible, string? PendingDeleteName, ConfirmDeleteCommand, CancelDeleteCommand }`

- [ ] **Step 1: 실패하는 테스트 추가**

`tests/FishingMartPos.Tests/ViewModels/InventoryViewModelTests.cs`의 `InventoryViewModelTests` 클래스 마지막(`GoToMainMenu_NavigatesToInjectedMainMenuViewModel` 테스트 뒤)에 아래 3개 테스트를 추가:

```csharp

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
```

- [ ] **Step 2: 테스트 실패 확인**

Run: `dotnet test tests\FishingMartPos.Tests --filter FullyQualifiedName~InventoryViewModelTests`
Expected: 빌드 실패 (`IsDeleteConfirmVisible`/`PendingDeleteName`/`ConfirmDeleteCommand`/`CancelDeleteCommand` 없음)

- [ ] **Step 3: 구현**

`src/FishingMartPos/ViewModels/InventoryViewModel.cs`에서 아래 필드를 클래스 상단(`_selectedCategoryOption` 필드 선언 뒤)에 추가:

```csharp
    [ObservableProperty]
    private bool _isDeleteConfirmVisible;

    [ObservableProperty]
    private string? _pendingDeleteName;

    private string? _pendingDeleteBarcode;
```

`RequestDelete` 메서드 본문을 다음으로 교체:

```csharp
    private void RequestDelete(Product product)
    {
        if (!IsAdmin) return;
        _pendingDeleteBarcode = product.Barcode;
        PendingDeleteName = product.Name;
        IsDeleteConfirmVisible = true;
    }
```

`GoToMainMenu` 메서드 뒤에 아래 두 커맨드를 추가:

```csharp
    [RelayCommand]
    private async Task ConfirmDelete()
    {
        if (_pendingDeleteBarcode is null) return;

        await _productRepository.DeactivateAsync(_pendingDeleteBarcode);
        _allProducts = _allProducts.Where(p => p.Barcode != _pendingDeleteBarcode).ToList();
        CancelDelete();
        RefreshRows();
    }

    [RelayCommand]
    private void CancelDelete()
    {
        _pendingDeleteBarcode = null;
        PendingDeleteName = null;
        IsDeleteConfirmVisible = false;
    }
```

- [ ] **Step 4: 테스트 통과 확인**

Run: `dotnet test tests\FishingMartPos.Tests --filter FullyQualifiedName~InventoryViewModelTests`
Expected: PASS (11 tests)

- [ ] **Step 5: Commit**

```bash
git add src/FishingMartPos/ViewModels/InventoryViewModel.cs tests/FishingMartPos.Tests/ViewModels/InventoryViewModelTests.cs
git commit -m "InventoryViewModel: 삭제 확인모달 + 소프트 삭제 배선"
```

---

### Task 4: InventoryView.xaml — 화면 구성 (스모크 테스트)

**Files:**
- Create: `src/FishingMartPos/Views/InventoryView.xaml` / `InventoryView.xaml.cs`
- Test: `tests/FishingMartPos.Tests/Views/InventoryViewSmokeTests.cs`

**Interfaces:**
- Consumes: Task 2/3의 `InventoryViewModel` 바인딩 표면(`SearchText`, `CategoryOptions`, `SelectedCategoryOption`, `Rows`, `IsDeleteConfirmVisible`, `PendingDeleteName`, `GoToMainMenuCommand`, `ConfirmDeleteCommand`, `CancelDeleteCommand`), `InventoryRowViewModel`(`Swatch`, `MajorName`, `MinorName`, `PosCatName`, `Name`, `Barcode`, `PriceStr`, `StockQtyStr`, `CanDelete`, `DeleteCommand`)
- Produces: `InventoryView`(`UserControl`) — Task 5에서 `MainWindow.xaml`의 `DataTemplate`에 연결

- [ ] **Step 1: 실패하는 스모크 테스트 작성**

`tests/FishingMartPos.Tests/Views/InventoryViewSmokeTests.cs`:

```csharp
using FishingMartPos.Views;
using Xunit;

namespace FishingMartPos.Tests.Views;

public class InventoryViewSmokeTests
{
    [Fact]
    public void InventoryView_ConstructsWithoutException()
    {
        StaTestHelper.RunOnSta(() =>
        {
            var view = new InventoryView();
            Assert.NotNull(view);
        });
    }
}
```

- [ ] **Step 2: 테스트 실패 확인**

Run: `dotnet test tests\FishingMartPos.Tests --filter FullyQualifiedName~InventoryViewSmokeTests`
Expected: 빌드 실패 (`InventoryView` 타입 없음)

- [ ] **Step 3: XAML 작성**

`src/FishingMartPos/Views/InventoryView.xaml`:

```xml
<UserControl x:Class="FishingMartPos.Views.InventoryView"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006"
             xmlns:d="http://schemas.microsoft.com/expression/blend/2008"
             mc:Ignorable="d">
    <UserControl.Resources>
        <BooleanToVisibilityConverter x:Key="BooleanToVisibilityConverter" />
    </UserControl.Resources>
    <Grid Background="{DynamicResource MenuBackground}">
        <Grid.RowDefinitions>
            <RowDefinition Height="Auto" />
            <RowDefinition Height="Auto" />
            <RowDefinition Height="*" />
        </Grid.RowDefinitions>

        <!-- 상단 헤더 -->
        <Border Grid.Row="0" Background="White" BorderBrush="{DynamicResource HeaderBorder}" BorderThickness="0,0,0,1">
            <Grid Margin="16,0" Height="42">
                <TextBlock Text="재고관리" FontSize="14" FontWeight="Bold"
                           Foreground="{DynamicResource TitleText}" VerticalAlignment="Center"
                           HorizontalAlignment="Left" />
                <Button Content="메인메뉴로" Padding="12,5" FontSize="12" FontWeight="Bold"
                        Background="{DynamicResource LogoutButtonBackground}"
                        Foreground="{DynamicResource MutedText}"
                        BorderBrush="{DynamicResource InputBorder}" BorderThickness="1"
                        HorizontalAlignment="Right" VerticalAlignment="Center"
                        Command="{Binding GoToMainMenuCommand}" />
            </Grid>
        </Border>

        <!-- 검색 / 카테고리 필터 -->
        <Grid Grid.Row="1" Margin="16,10">
            <Grid.ColumnDefinitions>
                <ColumnDefinition Width="*" />
                <ColumnDefinition Width="180" />
            </Grid.ColumnDefinitions>
            <TextBox Text="{Binding SearchText, UpdateSourceTrigger=PropertyChanged}"
                     Padding="8,6" FontSize="13"
                     BorderBrush="{DynamicResource InputBorder}" BorderThickness="1"
                     Background="White" />
            <ComboBox Grid.Column="1" Margin="10,0,0,0"
                      ItemsSource="{Binding CategoryOptions}"
                      SelectedItem="{Binding SelectedCategoryOption}"
                      DisplayMemberPath="Name"
                      Padding="8,6" FontSize="13" />
        </Grid>

        <!-- 목록 -->
        <Border Grid.Row="2" Margin="16,0,16,16" Background="White"
                BorderBrush="{DynamicResource CardBorder}" BorderThickness="1">
            <Grid>
                <Grid.RowDefinitions>
                    <RowDefinition Height="Auto" />
                    <RowDefinition Height="*" />
                </Grid.RowDefinitions>

                <Grid Grid.Row="0" Margin="12,8" Background="{DynamicResource HeaderButtonBackground}">
                    <Grid.ColumnDefinitions>
                        <ColumnDefinition Width="60" />
                        <ColumnDefinition Width="1*" />
                        <ColumnDefinition Width="1*" />
                        <ColumnDefinition Width="1*" />
                        <ColumnDefinition Width="2*" />
                        <ColumnDefinition Width="1.5*" />
                        <ColumnDefinition Width="1*" />
                        <ColumnDefinition Width="0.8*" />
                        <ColumnDefinition Width="60" />
                    </Grid.ColumnDefinitions>
                    <TextBlock Text="사진" FontSize="11" FontWeight="Bold" Foreground="{DynamicResource MutedText}" HorizontalAlignment="Center" />
                    <TextBlock Grid.Column="1" Text="대분류" FontSize="11" FontWeight="Bold" Foreground="{DynamicResource MutedText}" HorizontalAlignment="Center" />
                    <TextBlock Grid.Column="2" Text="소분류" FontSize="11" FontWeight="Bold" Foreground="{DynamicResource MutedText}" HorizontalAlignment="Center" />
                    <TextBlock Grid.Column="3" Text="POS분류" FontSize="11" FontWeight="Bold" Foreground="{DynamicResource MutedText}" HorizontalAlignment="Center" />
                    <TextBlock Grid.Column="4" Text="상품명" FontSize="11" FontWeight="Bold" Foreground="{DynamicResource MutedText}" />
                    <TextBlock Grid.Column="5" Text="바코드" FontSize="11" FontWeight="Bold" Foreground="{DynamicResource MutedText}" HorizontalAlignment="Center" />
                    <TextBlock Grid.Column="6" Text="단가" FontSize="11" FontWeight="Bold" Foreground="{DynamicResource MutedText}" HorizontalAlignment="Right" />
                    <TextBlock Grid.Column="7" Text="재고" FontSize="11" FontWeight="Bold" Foreground="{DynamicResource MutedText}" HorizontalAlignment="Right" />
                    <TextBlock Grid.Column="8" Text="삭제" FontSize="11" FontWeight="Bold" Foreground="{DynamicResource MutedText}" HorizontalAlignment="Center" />
                </Grid>

                <ScrollViewer Grid.Row="1" VerticalScrollBarVisibility="Auto">
                    <ItemsControl ItemsSource="{Binding Rows}">
                        <ItemsControl.ItemTemplate>
                            <DataTemplate>
                                <Grid Margin="12,6">
                                    <Grid.ColumnDefinitions>
                                        <ColumnDefinition Width="60" />
                                        <ColumnDefinition Width="1*" />
                                        <ColumnDefinition Width="1*" />
                                        <ColumnDefinition Width="1*" />
                                        <ColumnDefinition Width="2*" />
                                        <ColumnDefinition Width="1.5*" />
                                        <ColumnDefinition Width="1*" />
                                        <ColumnDefinition Width="0.8*" />
                                        <ColumnDefinition Width="60" />
                                    </Grid.ColumnDefinitions>
                                    <Border Background="{Binding Swatch}" Width="32" Height="32" CornerRadius="2" />
                                    <TextBlock Grid.Column="1" Text="{Binding MajorName}" FontSize="12" HorizontalAlignment="Center" VerticalAlignment="Center" />
                                    <TextBlock Grid.Column="2" Text="{Binding MinorName}" FontSize="12" HorizontalAlignment="Center" VerticalAlignment="Center" />
                                    <TextBlock Grid.Column="3" Text="{Binding PosCatName}" FontSize="12" HorizontalAlignment="Center" VerticalAlignment="Center" />
                                    <TextBlock Grid.Column="4" Text="{Binding Name}" FontSize="13" FontWeight="Bold" VerticalAlignment="Center" Foreground="{DynamicResource TitleText}" />
                                    <TextBlock Grid.Column="5" Text="{Binding Barcode}" FontSize="12" HorizontalAlignment="Center" VerticalAlignment="Center" Foreground="{DynamicResource MutedText}" />
                                    <TextBlock Grid.Column="6" Text="{Binding PriceStr}" FontSize="13" HorizontalAlignment="Right" VerticalAlignment="Center" />
                                    <TextBlock Grid.Column="7" Text="{Binding StockQtyStr}" FontSize="13" HorizontalAlignment="Right" VerticalAlignment="Center" />
                                    <Button Grid.Column="8" Content="삭제" FontSize="11" Padding="8,4"
                                            HorizontalAlignment="Center"
                                            Command="{Binding DeleteCommand}"
                                            Visibility="{Binding CanDelete, Converter={StaticResource BooleanToVisibilityConverter}}" />
                                </Grid>
                            </DataTemplate>
                        </ItemsControl.ItemTemplate>
                    </ItemsControl>
                </ScrollViewer>
            </Grid>
        </Border>

        <!-- 삭제 확인 모달 -->
        <Grid Grid.RowSpan="3" Background="{DynamicResource ModalOverlay}"
              Visibility="{Binding IsDeleteConfirmVisible, Converter={StaticResource BooleanToVisibilityConverter}}">
            <Border Background="White" BorderBrush="{DynamicResource CardBorder}" BorderThickness="1"
                    Padding="32,24" HorizontalAlignment="Center" VerticalAlignment="Center">
                <StackPanel HorizontalAlignment="Center">
                    <TextBlock Text="{Binding PendingDeleteName, StringFormat='{}{0} 상품을 삭제하시겠습니까?'}"
                               FontSize="15" FontWeight="Bold" Foreground="{DynamicResource TitleText}" Margin="0,0,0,16" />
                    <StackPanel Orientation="Horizontal" HorizontalAlignment="Center">
                        <Button Content="취소" Padding="16,6" Margin="0,0,8,0" Command="{Binding CancelDeleteCommand}" />
                        <Button Content="삭제" Padding="16,6" Background="{DynamicResource ErrorBorder}" Foreground="White"
                                Command="{Binding ConfirmDeleteCommand}" />
                    </StackPanel>
                </StackPanel>
            </Border>
        </Grid>
    </Grid>
</UserControl>
```

- [ ] **Step 4: code-behind 작성**

`src/FishingMartPos/Views/InventoryView.xaml.cs`:

```csharp
using System.Windows.Controls;

namespace FishingMartPos.Views;

public partial class InventoryView : UserControl
{
    public InventoryView()
    {
        InitializeComponent();
    }
}
```

- [ ] **Step 5: 테스트 통과 확인**

Run: `dotnet test tests\FishingMartPos.Tests --filter FullyQualifiedName~InventoryViewSmokeTests`
Expected: PASS (1 test)

- [ ] **Step 6: Commit**

```bash
git add src/FishingMartPos/Views/InventoryView.xaml src/FishingMartPos/Views/InventoryView.xaml.cs tests/FishingMartPos.Tests/Views/InventoryViewSmokeTests.cs
git commit -m "InventoryView 디자인 구성 (검색/카테고리필터/목록/삭제확인모달)"
```

---

### Task 5: 배선 — 메인메뉴 ↔ 재고관리 내비게이션 + DI 등록

**Files:**
- Modify: `src/FishingMartPos/ViewModels/MainMenuViewModel.cs`
- Modify: `src/FishingMartPos/ViewModels/LoginViewModel.cs`
- Modify: `src/FishingMartPos/App.xaml.cs`
- Modify: `src/FishingMartPos/MainWindow.xaml`
- Modify: `tests/FishingMartPos.Tests/ViewModels/MainMenuViewModelTests.cs`
- Modify: `tests/FishingMartPos.Tests/ViewModels/LoginViewModelTests.cs`

**Interfaces:**
- Consumes: Task 2~4의 `InventoryViewModel`/`InventoryView`
- Produces: `MainMenuViewModel.InventoryViewModelFactory : Func<MainMenuViewModel, Task<InventoryViewModel>>?`, 메인메뉴 "재고" 타일 클릭 시 실제 `InventoryViewModel`으로 내비게이트

- [ ] **Step 1: 테스트를 먼저 새 기대값으로 수정**

`tests/FishingMartPos.Tests/ViewModels/MainMenuViewModelTests.cs`의 `Create()` 헬퍼와 `GoToInventory` 테스트를 아래로 교체(파일 전체를 다음 내용으로 교체):

```csharp
using FishingMartPos.Models;
using FishingMartPos.Navigation;
using FishingMartPos.Services;
using FishingMartPos.ViewModels;
using Xunit;

namespace FishingMartPos.Tests.ViewModels;

public class MainMenuViewModelTests
{
    private static (MainMenuViewModel vm, ICurrentSession session, INavigationService navigation) Create()
    {
        var session = new CurrentSession();
        session.SignIn(
            new Staff { StaffCode = "ADMIN1", StaffName = "관리자", Role = "ADMIN", UseYn = "Y" },
            new PosTerminal { PosCode = "2", PosName = "POS2" });
        var navigation = new NavigationService();
        var staffRepository = new FishingMartPos.Tests.Fakes.FakeStaffRepository(
            new Dictionary<string, Staff>());
        var terminals = new[] { new PosTerminal { PosCode = "1", PosName = "POS1" } };
        var posViewModel = new PosViewModel(
            new FishingMartPos.Tests.Fakes.FakeProductRepository(Array.Empty<Product>()),
            new FishingMartPos.Tests.Fakes.FakeCodeRepository(new Dictionary<string, IReadOnlyList<CodeItem>>()),
            new FishingMartPos.Tests.Fakes.FakeSalesRepository(),
            new FishingMartPos.Tests.Fakes.FakeHeldOrderRepository(),
            new FishingMartPos.Tests.Fakes.FakeDelayProvider(),
            session);

        Func<Task<PosViewModel>> posViewModelFactory = () => Task.FromResult(posViewModel);
        Func<MainMenuViewModel, Task<InventoryViewModel>> inventoryViewModelFactory = mainMenu =>
            Task.FromResult(new InventoryViewModel(
                new FishingMartPos.Tests.Fakes.FakeProductRepository(Array.Empty<Product>()),
                new FishingMartPos.Tests.Fakes.FakeCodeRepository(new Dictionary<string, IReadOnlyList<CodeItem>>()),
                session,
                navigation,
                mainMenu));

        var vm = new MainMenuViewModel(session, navigation)
        {
            LoginViewModelFactory = () => new LoginViewModel(staffRepository, session, navigation, terminals, posViewModelFactory, inventoryViewModelFactory),
            PosViewModelFactory = posViewModelFactory,
            InventoryViewModelFactory = inventoryViewModelFactory,
        };
        return (vm, session, navigation);
    }

    [Fact]
    public void PosLabel_ReflectsCurrentTerminal()
    {
        var (vm, _, _) = Create();

        Assert.Equal("POS2", vm.PosLabel);
    }

    [Fact]
    public async Task GoToSales_NavigatesToPosViewModel()
    {
        var (vm, _, navigation) = Create();

        await vm.GoToSalesCommand.ExecuteAsync(null);

        Assert.IsType<PosViewModel>(navigation.CurrentViewModel);
    }

    [Fact]
    public void GoToSalesReport_NavigatesToPlaceholderWithSalesReportTitle()
    {
        var (vm, _, navigation) = Create();

        vm.GoToSalesReportCommand.Execute(null);

        var target = Assert.IsType<PlaceholderViewModel>(navigation.CurrentViewModel);
        Assert.Equal("매출", target.Title);
    }

    [Fact]
    public async Task GoToInventory_NavigatesToInventoryViewModel()
    {
        var (vm, _, navigation) = Create();

        await vm.GoToInventoryCommand.ExecuteAsync(null);

        Assert.IsType<InventoryViewModel>(navigation.CurrentViewModel);
    }

    [Fact]
    public void GoToSettings_NavigatesToPlaceholderWithSettingsTitle()
    {
        var (vm, _, navigation) = Create();

        vm.GoToSettingsCommand.Execute(null);

        var target = Assert.IsType<PlaceholderViewModel>(navigation.CurrentViewModel);
        Assert.Equal("환경설정", target.Title);
    }

    [Fact]
    public void Logout_ClearsSessionAndNavigatesToLogin()
    {
        var (vm, session, navigation) = Create();

        vm.LogoutCommand.Execute(null);

        Assert.False(session.IsSignedIn);
        Assert.IsType<LoginViewModel>(navigation.CurrentViewModel);
    }
}
```

`tests/FishingMartPos.Tests/ViewModels/LoginViewModelTests.cs`의 `CreateDummyPosViewModelFactory` 아래에 더미 인벤토리 팩토리 헬퍼를 추가하고, `CreateViewModel` 내부의 `LoginViewModel` 생성 호출에 인자를 추가한다. 파일 전체를 아래로 교체:

```csharp
using FishingMartPos.Models;
using FishingMartPos.Navigation;
using FishingMartPos.Services;
using FishingMartPos.Tests.Fakes;
using FishingMartPos.ViewModels;
using Xunit;

namespace FishingMartPos.Tests.ViewModels;

public class LoginViewModelTests
{
    private static Staff AdminStaff => new()
    {
        StaffCode = "ADMIN1", StaffName = "관리자", Role = "ADMIN", UseYn = "Y"
    };

    private static LoginViewModel CreateViewModel(
        out ICurrentSession session,
        out INavigationService navigation,
        Dictionary<string, Staff>? staffByPin = null)
    {
        var repository = new FakeStaffRepository(staffByPin ?? new Dictionary<string, Staff> { ["0000"] = AdminStaff });
        session = new CurrentSession();
        navigation = new NavigationService();
        var terminals = new[]
        {
            new PosTerminal { PosCode = "1", PosName = "POS1" },
            new PosTerminal { PosCode = "2", PosName = "POS2" },
        };

        return new LoginViewModel(
            repository, session, navigation, terminals,
            CreateDummyPosViewModelFactory(session),
            CreateDummyInventoryViewModelFactory(session, navigation));
    }

    private static Func<Task<PosViewModel>> CreateDummyPosViewModelFactory(ICurrentSession session) =>
        () => Task.FromResult(new PosViewModel(
            new FakeProductRepository(Array.Empty<Product>()),
            new FakeCodeRepository(new Dictionary<string, IReadOnlyList<CodeItem>>()),
            new FakeSalesRepository(),
            new FakeHeldOrderRepository(),
            new FakeDelayProvider(),
            session));

    private static Func<MainMenuViewModel, Task<InventoryViewModel>> CreateDummyInventoryViewModelFactory(
        ICurrentSession session, INavigationService navigation) =>
        mainMenu => Task.FromResult(new InventoryViewModel(
            new FakeProductRepository(Array.Empty<Product>()),
            new FakeCodeRepository(new Dictionary<string, IReadOnlyList<CodeItem>>()),
            session,
            navigation,
            mainMenu));

    [Fact]
    public void PressingDigits_BuildsPinString()
    {
        // 4자리를 다 채우면 자동 제출이 동기적으로(Fake는 즉시 완료) 실행되어 Pin이 초기화되므로,
        // 자동 제출 트리거 전인 3자리까지만 눌러 입력 누적 자체를 검증한다.
        var vm = CreateViewModel(out _, out _);

        vm.PressKeyCommand.Execute("1");
        vm.PressKeyCommand.Execute("2");
        vm.PressKeyCommand.Execute("3");

        Assert.Equal("123", vm.Pin);
    }

    [Fact]
    public void PressingBackspace_RemovesLastDigit()
    {
        var vm = CreateViewModel(out _, out _);
        vm.PressKeyCommand.Execute("1");
        vm.PressKeyCommand.Execute("2");

        vm.PressKeyCommand.Execute("<");

        Assert.Equal("1", vm.Pin);
    }

    [Fact]
    public void PressingClear_EmptiesPin()
    {
        var vm = CreateViewModel(out _, out _);
        vm.PressKeyCommand.Execute("1");
        vm.PressKeyCommand.Execute("2");

        vm.PressKeyCommand.Execute("CLS");

        Assert.Equal(string.Empty, vm.Pin);
    }

    [Fact]
    public async Task FourCorrectDigits_SignsInAndNavigatesToMainMenu()
    {
        var vm = CreateViewModel(out var session, out var navigation);

        vm.PressKeyCommand.Execute("0");
        vm.PressKeyCommand.Execute("0");
        vm.PressKeyCommand.Execute("0");
        vm.PressKeyCommand.Execute("0");
        await Task.Delay(50); // 4번째 입력 시 내부적으로 비동기 제출이 걸리므로 완료를 기다린다

        Assert.True(session.IsSignedIn);
        Assert.Equal("ADMIN1", session.CurrentStaff?.StaffCode);
        Assert.IsType<MainMenuViewModel>(navigation.CurrentViewModel);
    }

    [Fact]
    public async Task FourWrongDigits_ShowsErrorAndClearsPin()
    {
        var vm = CreateViewModel(out var session, out var navigation);

        vm.PressKeyCommand.Execute("9");
        vm.PressKeyCommand.Execute("9");
        vm.PressKeyCommand.Execute("9");
        vm.PressKeyCommand.Execute("9");
        await Task.Delay(50);

        Assert.False(session.IsSignedIn);
        Assert.Null(navigation.CurrentViewModel);
        Assert.True(vm.IsLoginErrorVisible);
        Assert.Equal(string.Empty, vm.Pin);
    }

    [Fact]
    public void SelectingTerminal_UpdatesSelectedTerminal()
    {
        var vm = CreateViewModel(out _, out _);

        vm.SelectTerminalCommand.Execute(vm.Terminals[1]);

        Assert.Equal("2", vm.SelectedTerminal.PosCode);
    }
}
```

- [ ] **Step 2: 테스트 실패 확인**

Run: `dotnet test tests\FishingMartPos.Tests --filter "FullyQualifiedName~MainMenuViewModelTests|FullyQualifiedName~LoginViewModelTests"`
Expected: 빌드 실패 (`MainMenuViewModel.InventoryViewModelFactory` 없음, `LoginViewModel` 생성자 인자 개수 불일치)

- [ ] **Step 3: MainMenuViewModel 수정**

`src/FishingMartPos/ViewModels/MainMenuViewModel.cs` 전체를 다음으로 교체:

```csharp
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FishingMartPos.Navigation;
using FishingMartPos.Services;

namespace FishingMartPos.ViewModels;

public sealed partial class MainMenuViewModel : ObservableObject
{
    private readonly ICurrentSession _session;
    private readonly INavigationService _navigation;

    [ObservableProperty]
    private DateTime _now = DateTime.Now;

    public MainMenuViewModel(ICurrentSession session, INavigationService navigation)
    {
        _session = session;
        _navigation = navigation;
    }

    public string PosLabel => _session.CurrentTerminal?.PosName ?? string.Empty;

    /// <summary>App.xaml.cs(또는 테스트)에서 주입하는 LoginViewModel 팩토리 — 로그아웃 시 사용.</summary>
    public Func<LoginViewModel>? LoginViewModelFactory { get; init; }

    /// <summary>App.xaml.cs(또는 테스트)에서 주입하는 PosViewModel 팩토리 — "판매" 진입 시 사용.</summary>
    public Func<Task<PosViewModel>>? PosViewModelFactory { get; init; }

    /// <summary>App.xaml.cs(또는 테스트)에서 주입하는 InventoryViewModel 팩토리 — "재고" 진입 시 사용. 자신(this)을 넘겨줘 InventoryViewModel이 "메인메뉴로" 복귀 시 재사용한다.</summary>
    public Func<MainMenuViewModel, Task<InventoryViewModel>>? InventoryViewModelFactory { get; init; }

    [RelayCommand]
    private async Task GoToSales()
    {
        var posViewModel = await PosViewModelFactory!.Invoke();
        _navigation.NavigateTo(posViewModel);
    }

    [RelayCommand]
    private void GoToSalesReport() => _navigation.NavigateTo(new PlaceholderViewModel("매출"));

    [RelayCommand]
    private async Task GoToInventory()
    {
        var inventoryViewModel = await InventoryViewModelFactory!.Invoke(this);
        _navigation.NavigateTo(inventoryViewModel);
    }

    [RelayCommand]
    private void GoToSettings() => _navigation.NavigateTo(new PlaceholderViewModel("환경설정"));

    [RelayCommand]
    private void Logout()
    {
        _session.SignOut();
        _navigation.NavigateTo(LoginViewModelFactory!.Invoke());
    }
}
```

- [ ] **Step 4: LoginViewModel 수정 (InventoryViewModelFactory를 MainMenuViewModel에 전달)**

`src/FishingMartPos/ViewModels/LoginViewModel.cs` 전체를 다음으로 교체:

```csharp
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FishingMartPos.Models;
using FishingMartPos.Navigation;
using FishingMartPos.Repositories;
using FishingMartPos.Services;

namespace FishingMartPos.ViewModels;

public sealed partial class LoginViewModel : ObservableObject
{
    private readonly IStaffRepository _staffRepository;
    private readonly ICurrentSession _session;
    private readonly INavigationService _navigation;
    private readonly Func<Task<PosViewModel>> _posViewModelFactory;
    private readonly Func<MainMenuViewModel, Task<InventoryViewModel>> _inventoryViewModelFactory;

    [ObservableProperty]
    private string _pin = string.Empty;

    [ObservableProperty]
    private bool _isLoginErrorVisible;

    [ObservableProperty]
    private PosTerminal _selectedTerminal;

    public IReadOnlyList<PosTerminal> Terminals { get; }

    public IReadOnlyList<bool> PinDots => Enumerable.Range(0, 4).Select(i => i < Pin.Length).ToArray();

    partial void OnPinChanged(string value) => OnPropertyChanged(nameof(PinDots));

    public LoginViewModel(
        IStaffRepository staffRepository,
        ICurrentSession session,
        INavigationService navigation,
        IReadOnlyList<PosTerminal> terminals,
        Func<Task<PosViewModel>> posViewModelFactory,
        Func<MainMenuViewModel, Task<InventoryViewModel>> inventoryViewModelFactory)
    {
        _staffRepository = staffRepository;
        _session = session;
        _navigation = navigation;
        Terminals = terminals;
        _selectedTerminal = terminals[0];
        _posViewModelFactory = posViewModelFactory;
        _inventoryViewModelFactory = inventoryViewModelFactory;
    }

    [RelayCommand]
    private void SelectTerminal(PosTerminal terminal)
    {
        SelectedTerminal = terminal;
    }

    [RelayCommand]
    private async Task PressKey(string key)
    {
        switch (key)
        {
            case "CLS":
                Pin = string.Empty;
                IsLoginErrorVisible = false;
                return;
            case "<":
                if (Pin.Length > 0)
                {
                    Pin = Pin[..^1];
                }
                IsLoginErrorVisible = false;
                return;
        }

        if (Pin.Length >= 4)
        {
            return;
        }

        Pin += key;
        IsLoginErrorVisible = false;

        if (Pin.Length == 4)
        {
            await SubmitAsync();
        }
    }

    private async Task SubmitAsync()
    {
        Staff? staff = await _staffRepository.FindByPinAsync(Pin);

        if (staff is null)
        {
            IsLoginErrorVisible = true;
            Pin = string.Empty;
            return;
        }

        _session.SignIn(staff, SelectedTerminal);

        var mainMenuViewModel = new MainMenuViewModel(_session, _navigation)
        {
            LoginViewModelFactory = () => new LoginViewModel(_staffRepository, _session, _navigation, Terminals, _posViewModelFactory, _inventoryViewModelFactory),
            PosViewModelFactory = _posViewModelFactory,
            InventoryViewModelFactory = _inventoryViewModelFactory,
        };
        _navigation.NavigateTo(mainMenuViewModel);
    }
}
```

- [ ] **Step 5: 테스트 통과 확인**

Run: `dotnet test tests\FishingMartPos.Tests --filter "FullyQualifiedName~MainMenuViewModelTests|FullyQualifiedName~LoginViewModelTests"`
Expected: PASS (전체)

- [ ] **Step 6: App.xaml.cs에 InventoryViewModel 팩토리 DI 배선**

`src/FishingMartPos/App.xaml.cs`에서 `using FishingMartPos.ViewModels;`는 이미 있으므로 추가 using 불필요. `LoginViewModel CreateLoginViewModel() => ...` 줄 바로 위에 아래 로컬 함수를 추가하고, `CreateLoginViewModel`가 새 인자를 전달하도록 수정한다.

`async Task<PosViewModel> CreatePosViewModelAsync() { ... }` 블록 뒤, `LoginViewModel CreateLoginViewModel()` 앞에 삽입:

```csharp
        async Task<InventoryViewModel> CreateInventoryViewModelAsync(MainMenuViewModel mainMenu)
        {
            var vm = new InventoryViewModel(productRepository, codeRepository, session, navigation, mainMenu);
            await vm.LoadAsync();
            return vm;
        }

```

`LoginViewModel CreateLoginViewModel() => ...` 줄을 다음으로 교체:

```csharp
        LoginViewModel CreateLoginViewModel() =>
            new(staffRepository, session, navigation, terminals, CreatePosViewModelAsync, CreateInventoryViewModelAsync);
```

전체 `OnStartup` 메서드는 다음 모양이 된다(변경 후 전체 파일):

```csharp
using System.Windows;
using FishingMartPos.Configuration;
using FishingMartPos.Data;
using FishingMartPos.Models;
using FishingMartPos.Navigation;
using FishingMartPos.Repositories;
using FishingMartPos.Services;
using FishingMartPos.Theme;
using FishingMartPos.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace FishingMartPos;

public partial class App : Application
{
    private ServiceProvider? _services;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        foreach (var (name, brush) in AppColors.BuildBrushes())
        {
            Resources[name] = brush;
        }

        var config = AppConfig.Load(AppContext.BaseDirectory);

        var services = new ServiceCollection();
        services.AddSingleton(config);
        services.AddSingleton<IDbConnectionFactory, MySqlConnectionFactory>();
        services.AddSingleton<IStaffRepository, StaffRepository>();
        services.AddSingleton<IPosTerminalRepository, PosTerminalRepository>();
        services.AddSingleton<ICurrentSession, CurrentSession>();
        services.AddSingleton<INavigationService, NavigationService>();
        services.AddSingleton<IProductRepository, ProductRepository>();
        services.AddSingleton<ICodeRepository, CodeRepository>();
        services.AddSingleton<ISalesRepository, SalesRepository>();
        services.AddSingleton<IHeldOrderRepository, HeldOrderRepository>();
        services.AddSingleton<IDelayProvider, DelayProvider>();
        _services = services.BuildServiceProvider();

        var terminalRepository = _services.GetRequiredService<IPosTerminalRepository>();
        IReadOnlyList<PosTerminal> terminals = await terminalRepository.GetAllAsync();

        var staffRepository = _services.GetRequiredService<IStaffRepository>();
        var session = _services.GetRequiredService<ICurrentSession>();
        var navigation = _services.GetRequiredService<INavigationService>();
        var productRepository = _services.GetRequiredService<IProductRepository>();
        var codeRepository = _services.GetRequiredService<ICodeRepository>();
        var salesRepository = _services.GetRequiredService<ISalesRepository>();
        var heldOrderRepository = _services.GetRequiredService<IHeldOrderRepository>();
        var delayProvider = _services.GetRequiredService<IDelayProvider>();

        async Task<PosViewModel> CreatePosViewModelAsync()
        {
            var vm = new PosViewModel(productRepository, codeRepository, salesRepository, heldOrderRepository, delayProvider, session);
            await vm.LoadAsync();
            return vm;
        }

        async Task<InventoryViewModel> CreateInventoryViewModelAsync(MainMenuViewModel mainMenu)
        {
            var vm = new InventoryViewModel(productRepository, codeRepository, session, navigation, mainMenu);
            await vm.LoadAsync();
            return vm;
        }

        LoginViewModel CreateLoginViewModel() =>
            new(staffRepository, session, navigation, terminals, CreatePosViewModelAsync, CreateInventoryViewModelAsync);

        navigation.NavigateTo(CreateLoginViewModel());

        var mainWindow = new MainWindow(navigation);
        mainWindow.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _services?.Dispose();
        base.OnExit(e);
    }
}
```

- [ ] **Step 7: MainWindow.xaml에 InventoryViewModel → InventoryView DataTemplate 추가**

`src/FishingMartPos/MainWindow.xaml`의 `<Window.Resources>` 안, `PosViewModel` DataTemplate 뒤에 추가:

```xml
        <DataTemplate DataType="{x:Type vm:InventoryViewModel}">
            <views:InventoryView />
        </DataTemplate>
```

- [ ] **Step 8: 빌드 및 전체 테스트 확인**

```bash
dotnet build FishingMartPos.sln
dotnet test tests\FishingMartPos.Tests
```

Expected: `Build succeeded.`, 전체 테스트 PASS

- [ ] **Step 9: 수동 실행 확인**

```bash
dotnet run --project src\FishingMartPos\FishingMartPos.csproj
```

로그인(PIN) → 메인메뉴 → "재고" 타일 클릭 → 재고관리 화면 진입(검색/카테고리 필터/목록 확인) → "메인메뉴로" 클릭 시 메인메뉴 복귀 → 관리자 계정으로 삭제 버튼 클릭 시 확인모달 → 확인 시 목록에서 사라지는지 확인. STAFF 계정으로는 삭제 버튼이 보이지 않는지 확인.

- [ ] **Step 10: Commit**

```bash
git add src/FishingMartPos/ViewModels/MainMenuViewModel.cs src/FishingMartPos/ViewModels/LoginViewModel.cs src/FishingMartPos/App.xaml.cs src/FishingMartPos/MainWindow.xaml tests/FishingMartPos.Tests/ViewModels/MainMenuViewModelTests.cs tests/FishingMartPos.Tests/ViewModels/LoginViewModelTests.cs
git commit -m "메인메뉴 재고 진입을 실제 InventoryViewModel로 연결, DI 배선 완료"
```
