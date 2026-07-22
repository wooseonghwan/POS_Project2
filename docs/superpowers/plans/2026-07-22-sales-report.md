# 매출관리 화면 구현 계획

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 메인메뉴의 "매출" 타일을 실제 매출관리 조회 화면(일일/월간 탭, 기간 필터, 요약 타일, 상세 테이블)에 연결한다. ADMIN 전용.

**Architecture:** `InventoryViewModel`과 동일한 "리포지토리는 원본 데이터만, ViewModel이 필터·집계" 패턴. `ISalesRepository.GetCompletedSalesAsync(from, to)`가 완료된 매출 헤더 원본을 반환하고, `SalesReportViewModel`이 탭(일일/월간)에 따라 조회 범위를 계산해 재조회하고 그룹핑·합산한다.

**Tech Stack:** WPF/.NET8, CommunityToolkit.Mvvm, Dapper+MySqlConnector, xUnit.

## Global Constraints

- `ISalesRepository.GetCompletedSalesAsync(DateTime from, DateTime to)`의 계약: `from`은 포함, `to`는 제외하는 반열린 구간이다 (`sale_dt >= @From AND sale_dt < @To`). 이 정확한 부등호 방향을 모든 태스크에서 동일하게 유지한다.
- `status = 'COMPLETE'`인 행만 조회한다 (매출취소 기능은 이번 범위 밖).
- 일일매출 탭의 DB 조회 범위: `from = DateFrom.Date`, `to = DateTo.Date.AddDays(1)`.
- 월간매출 탭의 DB 조회 범위: `from = DateFrom가 속한 달의 1일`, `to = DateTo가 속한 달의 다음달 1일`.
- 그룹 정렬은 항상 최신순(내림차순).
- 금액 표시는 기존 `FishingMartPos.Theme.CurrencyFormat.Format(decimal)` (`"N0"` + `"원"`)을 그대로 사용한다.
- ADMIN 전용 가드는 `InventoryViewModel.IsAdmin`/`GoToAddProduct`의 `if (!IsAdmin) return;` 패턴과 정확히 동일하게 적용한다.
- 화면 진입 기본값: 일일매출 탭, `DateFrom = DateTime.Today.AddDays(-6)`, `DateTo = DateTime.Today`.
- 조회 결과가 0건이면 "조회된 매출이 없습니다" 안내 문구를 보여준다.
- 탭 전환(`SelectDailyTab`/`SelectMonthlyTab`)과 날짜 변경 후 조회(`Refresh`)는 모두 명시적 async 커맨드로 노출한다 — 프로퍼티 변경 훅에서 fire-and-forget으로 트리거하지 않는다(테스트에서 `await ...Command.ExecuteAsync(null)`으로 결정적으로 검증하기 위함).

---

### Task 1: `ISalesRepository.GetCompletedSalesAsync` 추가

**Files:**
- Modify: `src/FishingMartPos/Repositories/ISalesRepository.cs`
- Modify: `src/FishingMartPos/Repositories/SalesRepository.cs`
- Modify: `tests/FishingMartPos.Tests/Repositories/SalesRepositoryTests.cs`

**Interfaces:**
- Produces: `Task<IReadOnlyList<SaleHeader>> GetCompletedSalesAsync(DateTime from, DateTime to)` — Task 2가 `FakeSalesRepository`에 이 시그니처로 페이크를 구현하고, `SalesReportViewModel`이 이 메서드를 호출한다.

- [ ] **Step 1: 인터페이스에 메서드 추가**

`src/FishingMartPos/Repositories/ISalesRepository.cs` 전체를 다음으로 교체:

```csharp
using FishingMartPos.Models;

namespace FishingMartPos.Repositories;

public interface ISalesRepository
{
    Task<long> CreateSaleAsync(SaleHeader header, IReadOnlyList<SaleDetailLine> lines);

    Task<IReadOnlyList<SaleHeader>> GetCompletedSalesAsync(DateTime from, DateTime to);
}
```

- [ ] **Step 2: 구현 추가**

`src/FishingMartPos/Repositories/SalesRepository.cs`의 `CreateSaleAsync` 메서드 뒤(클래스 닫는 `}` 바로 전)에 다음 메서드를 추가:

```csharp
    public async Task<IReadOnlyList<SaleHeader>> GetCompletedSalesAsync(DateTime from, DateTime to)
    {
        using var connection = _connectionFactory.CreateOpenConnection();
        const string sql = """
            SELECT pos_cd AS PosCd, sale_dt AS SaleDt, staff_cd AS StaffCd, total_amt AS TotalAmt,
                   pay_type AS PayType, cash_received AS CashReceived, change_amt AS ChangeAmt,
                   van_approval_no AS VanApprovalNo, van_code AS VanCode
            FROM sales_header_tb
            WHERE sale_dt >= @From AND sale_dt < @To AND status = 'COMPLETE'
            """;
        var rows = await connection.QueryAsync<SaleHeader>(sql, new { From = from, To = to });
        return rows.ToList();
    }
```

- [ ] **Step 3: 실패하는 테스트 작성**

`tests/FishingMartPos.Tests/Repositories/SalesRepositoryTests.cs`의 마지막 `}` (클래스 닫는 중괄호) 바로 앞에 다음 테스트를 추가:

```csharp
    [Fact]
    public async Task GetCompletedSalesAsync_ReturnsOnlyCompleteRowsWithinHalfOpenRange()
    {
        var config = AppConfig.Load(AppContext.BaseDirectory);
        var factory = new MySqlConnectionFactory(config);
        ISalesRepository repository = new SalesRepository(factory);

        const string barcode = "8800000020001";
        int stockBefore;
        using (var conn = factory.CreateOpenConnection())
        {
            stockBefore = await conn.QuerySingleAsync<int>(
                "SELECT stock_qty FROM product_tb WHERE barcode = @Barcode", new { Barcode = barcode });
        }

        var lines = new[] { new SaleDetailLine { Barcode = barcode, ProductName = "지렁이", Qty = 1, UnitPrice = 5000 } };
        var inRangeDate = new DateTime(2026, 3, 10, 12, 0, 0);
        var boundaryExcludedDate = new DateTime(2026, 3, 15, 0, 0, 0); // to == 이 시각이면 제외되어야 함
        var outOfRangeDate = new DateTime(2026, 3, 20, 0, 0, 0);

        long saleInRange = await repository.CreateSaleAsync(
            new SaleHeader { PosCd = "1", SaleDt = inRangeDate, StaffCd = "ADMIN1", TotalAmt = 5000, PayType = "CASH", CashReceived = 5000, ChangeAmt = 0 },
            lines);
        long saleAtBoundary = await repository.CreateSaleAsync(
            new SaleHeader { PosCd = "1", SaleDt = boundaryExcludedDate, StaffCd = "ADMIN1", TotalAmt = 7000, PayType = "CARD1" },
            lines);
        long saleOutOfRange = await repository.CreateSaleAsync(
            new SaleHeader { PosCd = "1", SaleDt = outOfRangeDate, StaffCd = "ADMIN1", TotalAmt = 9000, PayType = "CARD2" },
            lines);
        long saleCancelled = await repository.CreateSaleAsync(
            new SaleHeader { PosCd = "1", SaleDt = inRangeDate, StaffCd = "ADMIN1", TotalAmt = 4000, PayType = "CASH" },
            lines);

        using var verifyConn = factory.CreateOpenConnection();
        try
        {
            await verifyConn.ExecuteAsync(
                "UPDATE sales_header_tb SET status = 'CANCELLED' WHERE sale_no = @SaleNo",
                new { SaleNo = saleCancelled });

            var result = await repository.GetCompletedSalesAsync(new DateTime(2026, 3, 1), boundaryExcludedDate);

            Assert.Contains(result, r => r.TotalAmt == 5000);
            Assert.DoesNotContain(result, r => r.TotalAmt == 7000); // to 경계는 제외
            Assert.DoesNotContain(result, r => r.TotalAmt == 9000); // 범위 밖
            Assert.DoesNotContain(result, r => r.TotalAmt == 4000); // CANCELLED
        }
        finally
        {
            await verifyConn.ExecuteAsync(
                "DELETE FROM sales_detail_tb WHERE sale_no IN (@A, @B, @C, @D)",
                new { A = saleInRange, B = saleAtBoundary, C = saleOutOfRange, D = saleCancelled });
            await verifyConn.ExecuteAsync(
                "DELETE FROM sales_header_tb WHERE sale_no IN (@A, @B, @C, @D)",
                new { A = saleInRange, B = saleAtBoundary, C = saleOutOfRange, D = saleCancelled });
            await verifyConn.ExecuteAsync(
                "UPDATE product_tb SET stock_qty = @Stock WHERE barcode = @Barcode",
                new { Stock = stockBefore, Barcode = barcode });
        }
    }
```

- [ ] **Step 4: 테스트 실행**

Run: `dotnet test --filter "FullyQualifiedName~SalesRepositoryTests"`
Expected: 새 테스트 포함 전체 통과 (개발 DB가 `appsettings.Local.json` 설정대로 연결 가능해야 함 — 기존 파일의 다른 테스트들과 동일한 전제).

- [ ] **Step 5: 커밋**

```bash
git add src/FishingMartPos/Repositories/ISalesRepository.cs src/FishingMartPos/Repositories/SalesRepository.cs tests/FishingMartPos.Tests/Repositories/SalesRepositoryTests.cs
git commit -m "ISalesRepository에 GetCompletedSalesAsync 추가 (완료 매출 기간 조회)"
```

---

### Task 2: `SalesReportRowViewModel` + `SalesReportViewModel` 핵심 로직

**Files:**
- Modify: `tests/FishingMartPos.Tests/Fakes/FakeSalesRepository.cs`
- Create: `src/FishingMartPos/ViewModels/SalesReportRowViewModel.cs`
- Create: `src/FishingMartPos/ViewModels/SalesReportViewModel.cs`
- Create: `tests/FishingMartPos.Tests/ViewModels/SalesReportViewModelTests.cs`

**Interfaces:**
- Consumes: `ISalesRepository.GetCompletedSalesAsync(DateTime, DateTime)` (Task 1), `FishingMartPos.Theme.CurrencyFormat.Format(decimal)` (기존).
- Produces: `SalesReportViewModel` 생성자 `(ISalesRepository salesRepository, INavigationService navigation, MainMenuViewModel returnTo)`, `public async Task LoadAsync()`, `SelectDailyTabCommand`/`SelectMonthlyTabCommand`/`RefreshCommand`/`GoToMainMenuCommand`, 프로퍼티 `IsDailyTab`(bool)/`DateFrom`(DateTime?)/`DateTo`(DateTime?)/`TotalAmountStr`/`TotalCashStr`/`TotalCard1Str`/`TotalCard2Str`/`HasRows`(bool)/`Rows`(ObservableCollection<SalesReportRowViewModel>) — Task 3의 `MainMenuViewModel.SalesReportViewModelFactory`가 이 타입을 반환해야 하고, Task 4의 XAML이 이 프로퍼티/커맨드에 바인딩한다.

- [ ] **Step 1: `FakeSalesRepository`에 시딩 + 호출기록 + 새 메서드 추가**

`tests/FishingMartPos.Tests/Fakes/FakeSalesRepository.cs` 전체를 다음으로 교체:

```csharp
using FishingMartPos.Models;
using FishingMartPos.Repositories;

namespace FishingMartPos.Tests.Fakes;

public sealed class FakeSalesRepository : ISalesRepository
{
    public List<(SaleHeader Header, IReadOnlyList<SaleDetailLine> Lines)> CreatedSales { get; } = new();
    public List<(DateTime From, DateTime To)> GetCompletedSalesCalls { get; } = new();
    private long _nextSaleNo = 1;
    private IReadOnlyList<SaleHeader> _completedSales = Array.Empty<SaleHeader>();

    public Task<long> CreateSaleAsync(SaleHeader header, IReadOnlyList<SaleDetailLine> lines)
    {
        CreatedSales.Add((header, lines));
        return Task.FromResult(_nextSaleNo++);
    }

    public void SeedCompletedSales(IReadOnlyList<SaleHeader> sales)
    {
        _completedSales = sales;
    }

    public Task<IReadOnlyList<SaleHeader>> GetCompletedSalesAsync(DateTime from, DateTime to)
    {
        GetCompletedSalesCalls.Add((from, to));
        var filtered = _completedSales.Where(s => s.SaleDt >= from && s.SaleDt < to).ToList();
        return Task.FromResult<IReadOnlyList<SaleHeader>>(filtered);
    }
}
```

- [ ] **Step 2: `SalesReportRowViewModel` 작성**

`src/FishingMartPos/ViewModels/SalesReportRowViewModel.cs` (신규):

```csharp
namespace FishingMartPos.ViewModels;

public sealed class SalesReportRowViewModel
{
    public required string Label { get; init; }
    public required string CountStr { get; init; }
    public required string CashStr { get; init; }
    public required string Card1Str { get; init; }
    public required string Card2Str { get; init; }
    public required string AmountStr { get; init; }
}
```

- [ ] **Step 3: 실패하는 테스트 작성**

`tests/FishingMartPos.Tests/ViewModels/SalesReportViewModelTests.cs` (신규):

```csharp
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
```

- [ ] **Step 4: 테스트가 컴파일 실패/실패하는지 확인**

Run: `dotnet test --filter "FullyQualifiedName~SalesReportViewModelTests"`
Expected: `SalesReportViewModel` 타입이 없어 컴파일 오류 (CS0246 등).

- [ ] **Step 5: `SalesReportViewModel` 구현**

`src/FishingMartPos/ViewModels/SalesReportViewModel.cs` (신규):

```csharp
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FishingMartPos.Navigation;
using FishingMartPos.Repositories;
using FishingMartPos.Theme;

namespace FishingMartPos.ViewModels;

public sealed partial class SalesReportViewModel : ObservableObject
{
    private readonly ISalesRepository _salesRepository;
    private readonly INavigationService _navigation;
    private readonly MainMenuViewModel _returnTo;

    [ObservableProperty]
    private bool _isDailyTab = true;

    [ObservableProperty]
    private DateTime? _dateFrom;

    [ObservableProperty]
    private DateTime? _dateTo;

    [ObservableProperty]
    private string _totalAmountStr = "0원";

    [ObservableProperty]
    private string _totalCashStr = "0원";

    [ObservableProperty]
    private string _totalCard1Str = "0원";

    [ObservableProperty]
    private string _totalCard2Str = "0원";

    [ObservableProperty]
    private bool _hasRows;

    public ObservableCollection<SalesReportRowViewModel> Rows { get; } = new();

    public SalesReportViewModel(ISalesRepository salesRepository, INavigationService navigation, MainMenuViewModel returnTo)
    {
        _salesRepository = salesRepository;
        _navigation = navigation;
        _returnTo = returnTo;
        DateTo = DateTime.Today;
        DateFrom = DateTime.Today.AddDays(-6);
    }

    public async Task LoadAsync() => await RefreshAsync();

    [RelayCommand]
    private async Task SelectDailyTab()
    {
        IsDailyTab = true;
        await RefreshAsync();
    }

    [RelayCommand]
    private async Task SelectMonthlyTab()
    {
        IsDailyTab = false;
        await RefreshAsync();
    }

    [RelayCommand]
    private async Task Refresh() => await RefreshAsync();

    [RelayCommand]
    private void GoToMainMenu() => _navigation.NavigateTo(_returnTo);

    private async Task RefreshAsync()
    {
        if (DateFrom is not { } dateFrom || DateTo is not { } dateTo)
        {
            return;
        }

        DateTime queryFrom;
        DateTime queryTo;
        if (IsDailyTab)
        {
            queryFrom = dateFrom.Date;
            queryTo = dateTo.Date.AddDays(1);
        }
        else
        {
            queryFrom = new DateTime(dateFrom.Year, dateFrom.Month, 1);
            queryTo = new DateTime(dateTo.Year, dateTo.Month, 1).AddMonths(1);
        }

        var sales = await _salesRepository.GetCompletedSalesAsync(queryFrom, queryTo);

        var groups = IsDailyTab
            ? sales.GroupBy(s => s.SaleDt.Date)
            : sales.GroupBy(s => new DateTime(s.SaleDt.Year, s.SaleDt.Month, 1));

        var rows = groups
            .OrderByDescending(g => g.Key)
            .Select(g => new SalesReportRowViewModel
            {
                Label = IsDailyTab ? g.Key.ToString("yyyy-MM-dd") : g.Key.ToString("yyyy-MM") + " 월",
                CountStr = g.Count().ToString("N0") + "건",
                CashStr = CurrencyFormat.Format(g.Where(s => s.PayType == "CASH").Sum(s => s.TotalAmt)),
                Card1Str = CurrencyFormat.Format(g.Where(s => s.PayType == "CARD1").Sum(s => s.TotalAmt)),
                Card2Str = CurrencyFormat.Format(g.Where(s => s.PayType == "CARD2").Sum(s => s.TotalAmt)),
                AmountStr = CurrencyFormat.Format(g.Sum(s => s.TotalAmt)),
            })
            .ToList();

        Rows.Clear();
        foreach (var row in rows)
        {
            Rows.Add(row);
        }
        HasRows = Rows.Count > 0;

        TotalAmountStr = CurrencyFormat.Format(sales.Sum(s => s.TotalAmt));
        TotalCashStr = CurrencyFormat.Format(sales.Where(s => s.PayType == "CASH").Sum(s => s.TotalAmt));
        TotalCard1Str = CurrencyFormat.Format(sales.Where(s => s.PayType == "CARD1").Sum(s => s.TotalAmt));
        TotalCard2Str = CurrencyFormat.Format(sales.Where(s => s.PayType == "CARD2").Sum(s => s.TotalAmt));
    }
}
```

- [ ] **Step 6: 테스트 실행**

Run: `dotnet test --filter "FullyQualifiedName~SalesReportViewModelTests"`
Expected: 전체 통과 (8개 테스트).

- [ ] **Step 7: 커밋**

```bash
git add tests/FishingMartPos.Tests/Fakes/FakeSalesRepository.cs src/FishingMartPos/ViewModels/SalesReportRowViewModel.cs src/FishingMartPos/ViewModels/SalesReportViewModel.cs tests/FishingMartPos.Tests/ViewModels/SalesReportViewModelTests.cs
git commit -m "SalesReportViewModel 추가 — 일일/월간 매출 집계 핵심 로직"
```

---

### Task 3: `MainMenuViewModel`/`LoginViewModel` 배선 (ADMIN 전용 접근)

**Files:**
- Modify: `src/FishingMartPos/ViewModels/MainMenuViewModel.cs`
- Modify: `src/FishingMartPos/ViewModels/LoginViewModel.cs`
- Modify: `tests/FishingMartPos.Tests/ViewModels/MainMenuViewModelTests.cs`
- Modify: `tests/FishingMartPos.Tests/ViewModels/LoginViewModelTests.cs`

**Interfaces:**
- Consumes: `SalesReportViewModel` (Task 2).
- Produces: `MainMenuViewModel.IsAdmin`(bool), `MainMenuViewModel.SalesReportViewModelFactory`(`Func<MainMenuViewModel, Task<SalesReportViewModel>>?`) — Task 4의 `App.xaml.cs`가 이 팩토리를 배선하고, XAML이 `IsAdmin`에 바인딩한다.

- [ ] **Step 1: `MainMenuViewModel` 수정**

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

    public bool IsAdmin => _session.CurrentStaff?.IsAdmin ?? false;

    /// <summary>App.xaml.cs(또는 테스트)에서 주입하는 LoginViewModel 팩토리 — 로그아웃 시 사용.</summary>
    public Func<LoginViewModel>? LoginViewModelFactory { get; init; }

    /// <summary>App.xaml.cs(또는 테스트)에서 주입하는 PosViewModel 팩토리 — "판매" 진입 시 사용. 자신(this)을 넘겨줘 PosViewModel이 "메뉴" 버튼으로 복귀 시 재사용한다.</summary>
    public Func<MainMenuViewModel, Task<PosViewModel>>? PosViewModelFactory { get; init; }

    /// <summary>App.xaml.cs(또는 테스트)에서 주입하는 InventoryViewModel 팩토리 — "재고" 진입 시 사용. 자신(this)을 넘겨줘 InventoryViewModel이 "메인메뉴로" 복귀 시 재사용한다.</summary>
    public Func<MainMenuViewModel, Task<InventoryViewModel>>? InventoryViewModelFactory { get; init; }

    /// <summary>App.xaml.cs(또는 테스트)에서 주입하는 SalesReportViewModel 팩토리 — "매출" 진입 시 사용(ADMIN 전용). 자신(this)을 넘겨줘 SalesReportViewModel이 "메인메뉴로" 복귀 시 재사용한다.</summary>
    public Func<MainMenuViewModel, Task<SalesReportViewModel>>? SalesReportViewModelFactory { get; init; }

    [RelayCommand]
    private async Task GoToSales()
    {
        var posViewModel = await PosViewModelFactory!.Invoke(this);
        _navigation.NavigateTo(posViewModel);
    }

    [RelayCommand]
    private async Task GoToSalesReport()
    {
        if (!IsAdmin) return;
        var salesReportViewModel = await SalesReportViewModelFactory!.Invoke(this);
        _navigation.NavigateTo(salesReportViewModel);
    }

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

- [ ] **Step 2: `LoginViewModel`에 팩토리 파라미터 추가**

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
    private readonly Func<MainMenuViewModel, Task<PosViewModel>> _posViewModelFactory;
    private readonly Func<MainMenuViewModel, Task<InventoryViewModel>> _inventoryViewModelFactory;
    private readonly Func<MainMenuViewModel, Task<SalesReportViewModel>> _salesReportViewModelFactory;

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
        Func<MainMenuViewModel, Task<PosViewModel>> posViewModelFactory,
        Func<MainMenuViewModel, Task<InventoryViewModel>> inventoryViewModelFactory,
        Func<MainMenuViewModel, Task<SalesReportViewModel>> salesReportViewModelFactory)
    {
        _staffRepository = staffRepository;
        _session = session;
        _navigation = navigation;
        Terminals = terminals;
        _selectedTerminal = terminals[0];
        _posViewModelFactory = posViewModelFactory;
        _inventoryViewModelFactory = inventoryViewModelFactory;
        _salesReportViewModelFactory = salesReportViewModelFactory;
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
            LoginViewModelFactory = () => new LoginViewModel(_staffRepository, _session, _navigation, Terminals, _posViewModelFactory, _inventoryViewModelFactory, _salesReportViewModelFactory),
            PosViewModelFactory = _posViewModelFactory,
            InventoryViewModelFactory = _inventoryViewModelFactory,
            SalesReportViewModelFactory = _salesReportViewModelFactory,
        };
        _navigation.NavigateTo(mainMenuViewModel);
    }
}
```

- [ ] **Step 3: `MainMenuViewModelTests.cs` 수정**

`tests/FishingMartPos.Tests/ViewModels/MainMenuViewModelTests.cs` 전체를 다음으로 교체:

```csharp
using FishingMartPos.Models;
using FishingMartPos.Navigation;
using FishingMartPos.Services;
using FishingMartPos.Tests.Fakes;
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
        var staffRepository = new FakeStaffRepository(new Dictionary<string, Staff>());
        var terminals = new[] { new PosTerminal { PosCode = "1", PosName = "POS1" } };
        var posViewModel = new PosViewModel(
            new FakeProductRepository(Array.Empty<Product>()),
            new FakeCodeRepository(new Dictionary<string, IReadOnlyList<CodeItem>>()),
            new FakeSalesRepository(),
            new FakeHeldOrderRepository(),
            new FakeDelayProvider(),
            session,
            navigation,
            new MainMenuViewModel(session, navigation));

        Func<MainMenuViewModel, Task<PosViewModel>> posViewModelFactory = _ => Task.FromResult(posViewModel);
        Func<MainMenuViewModel, Task<InventoryViewModel>> inventoryViewModelFactory = mainMenu =>
            Task.FromResult(new InventoryViewModel(
                new FakeProductRepository(Array.Empty<Product>()),
                new FakeCodeRepository(new Dictionary<string, IReadOnlyList<CodeItem>>()),
                session,
                navigation,
                mainMenu));
        Func<MainMenuViewModel, Task<SalesReportViewModel>> salesReportViewModelFactory = mainMenu =>
            Task.FromResult(new SalesReportViewModel(new FakeSalesRepository(), navigation, mainMenu));

        var vm = new MainMenuViewModel(session, navigation)
        {
            LoginViewModelFactory = () => new LoginViewModel(staffRepository, session, navigation, terminals, posViewModelFactory, inventoryViewModelFactory, salesReportViewModelFactory),
            PosViewModelFactory = posViewModelFactory,
            InventoryViewModelFactory = inventoryViewModelFactory,
            SalesReportViewModelFactory = salesReportViewModelFactory,
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
    public async Task GoToSalesReport_AsAdmin_NavigatesToSalesReportViewModel()
    {
        var (vm, _, navigation) = Create();

        await vm.GoToSalesReportCommand.ExecuteAsync(null);

        Assert.IsType<SalesReportViewModel>(navigation.CurrentViewModel);
    }

    [Fact]
    public async Task GoToSalesReport_AsStaff_DoesNothing()
    {
        var session = new CurrentSession();
        session.SignIn(
            new Staff { StaffCode = "STAFF1", StaffName = "직원", Role = "STAFF", UseYn = "Y" },
            new PosTerminal { PosCode = "1", PosName = "POS1" });
        var navigation = new NavigationService();
        var vm = new MainMenuViewModel(session, navigation)
        {
            SalesReportViewModelFactory = mainMenu => Task.FromResult(new SalesReportViewModel(new FakeSalesRepository(), navigation, mainMenu)),
        };

        await vm.GoToSalesReportCommand.ExecuteAsync(null);

        Assert.Null(navigation.CurrentViewModel);
        Assert.False(vm.IsAdmin);
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

- [ ] **Step 4: `LoginViewModelTests.cs` 수정**

`tests/FishingMartPos.Tests/ViewModels/LoginViewModelTests.cs`에서 `CreateViewModel` 메서드와 `CreateDummyInventoryViewModelFactory` 메서드를 찾아 다음과 같이 바꾼다:

```csharp
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
            CreateDummyPosViewModelFactory(session, navigation),
            CreateDummyInventoryViewModelFactory(session, navigation),
            CreateDummySalesReportViewModelFactory(session, navigation));
    }
```

그리고 `CreateDummyInventoryViewModelFactory` 메서드 바로 뒤에 다음 메서드를 추가한다:

```csharp
    private static Func<MainMenuViewModel, Task<SalesReportViewModel>> CreateDummySalesReportViewModelFactory(
        ICurrentSession session, INavigationService navigation) =>
        mainMenu => Task.FromResult(new SalesReportViewModel(new FakeSalesRepository(), navigation, mainMenu));
```

(파일 상단 `using` 목록은 이미 `FishingMartPos.ViewModels`, `FishingMartPos.Tests.Fakes`를 포함하고 있으므로 추가 `using`은 필요 없다.)

- [ ] **Step 5: 테스트 실행**

Run: `dotnet test --filter "FullyQualifiedName~MainMenuViewModelTests|FullyQualifiedName~LoginViewModelTests"`
Expected: 전체 통과.

- [ ] **Step 6: 커밋**

```bash
git add src/FishingMartPos/ViewModels/MainMenuViewModel.cs src/FishingMartPos/ViewModels/LoginViewModel.cs tests/FishingMartPos.Tests/ViewModels/MainMenuViewModelTests.cs tests/FishingMartPos.Tests/ViewModels/LoginViewModelTests.cs
git commit -m "메인메뉴 '매출' 진입을 SalesReportViewModel로 연결, ADMIN 전용 가드 추가"
```

---

### Task 4: `SalesReportView.xaml` + DI 배선 + 메인메뉴 타일 권한 노출

**Files:**
- Create: `src/FishingMartPos/Views/SalesReportView.xaml`
- Create: `src/FishingMartPos/Views/SalesReportView.xaml.cs`
- Modify: `src/FishingMartPos/App.xaml.cs`
- Modify: `src/FishingMartPos/MainWindow.xaml`
- Modify: `src/FishingMartPos/Views/MainMenuView.xaml`
- Create: `tests/FishingMartPos.Tests/Views/SalesReportViewSmokeTests.cs`

**Interfaces:**
- Consumes: `SalesReportViewModel` (Task 2), `MainMenuViewModel.IsAdmin`/`SalesReportViewModelFactory` (Task 3).

- [ ] **Step 1: `SalesReportView.xaml.cs` 작성**

`src/FishingMartPos/Views/SalesReportView.xaml.cs` (신규):

```csharp
using System.Windows.Controls;

namespace FishingMartPos.Views;

public partial class SalesReportView : UserControl
{
    public SalesReportView()
    {
        InitializeComponent();
    }

    private void DatePicker_SelectedDateChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DataContext is ViewModels.SalesReportViewModel vm)
        {
            vm.RefreshCommand.Execute(null);
        }
    }
}
```

- [ ] **Step 2: `SalesReportView.xaml` 작성**

`src/FishingMartPos/Views/SalesReportView.xaml` (신규):

```xml
<UserControl x:Class="FishingMartPos.Views.SalesReportView"
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
            <RowDefinition Height="*" />
        </Grid.RowDefinitions>

        <!-- 상단 헤더 -->
        <Border Grid.Row="0" Background="White" BorderBrush="{DynamicResource HeaderBorder}" BorderThickness="0,0,0,1">
            <Grid Margin="16,0" Height="42">
                <TextBlock Text="매출관리" FontSize="14" FontWeight="Bold"
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

        <StackPanel Grid.Row="1" Margin="16" >
            <!-- 탭 -->
            <StackPanel Orientation="Horizontal" Margin="0,0,0,12">
                <Button Content="일일매출" Padding="18,9" FontSize="13" FontWeight="Bold" Margin="0,0,8,0"
                        Command="{Binding SelectDailyTabCommand}">
                    <Button.Style>
                        <Style TargetType="Button">
                            <Setter Property="Background" Value="{DynamicResource HeaderButtonBackground}" />
                            <Setter Property="Foreground" Value="{DynamicResource MutedText}" />
                            <Style.Triggers>
                                <DataTrigger Binding="{Binding IsDailyTab}" Value="True">
                                    <Setter Property="Background" Value="{DynamicResource Accent}" />
                                    <Setter Property="Foreground" Value="White" />
                                </DataTrigger>
                            </Style.Triggers>
                        </Style>
                    </Button.Style>
                </Button>
                <Button Content="월간매출" Padding="18,9" FontSize="13" FontWeight="Bold"
                        Command="{Binding SelectMonthlyTabCommand}">
                    <Button.Style>
                        <Style TargetType="Button">
                            <Setter Property="Background" Value="{DynamicResource HeaderButtonBackground}" />
                            <Setter Property="Foreground" Value="{DynamicResource MutedText}" />
                            <Style.Triggers>
                                <DataTrigger Binding="{Binding IsDailyTab}" Value="False">
                                    <Setter Property="Background" Value="{DynamicResource Accent}" />
                                    <Setter Property="Foreground" Value="White" />
                                </DataTrigger>
                            </Style.Triggers>
                        </Style>
                    </Button.Style>
                </Button>
            </StackPanel>

            <!-- 기간 필터 -->
            <Border Background="White" BorderBrush="{DynamicResource CardBorder}" BorderThickness="1" Padding="12,10" Margin="0,0,0,12">
                <StackPanel Orientation="Horizontal">
                    <TextBlock Text="기간" FontSize="12" FontWeight="Bold" Foreground="{DynamicResource MutedText}"
                               VerticalAlignment="Center" Margin="0,0,10,0" />
                    <DatePicker SelectedDate="{Binding DateFrom, Mode=TwoWay}"
                                SelectedDateChanged="DatePicker_SelectedDateChanged"
                                FontSize="12" Padding="6,4" />
                    <TextBlock Text="~" FontSize="12" Foreground="{DynamicResource MutedText}"
                               VerticalAlignment="Center" Margin="8,0" />
                    <DatePicker SelectedDate="{Binding DateTo, Mode=TwoWay}"
                                SelectedDateChanged="DatePicker_SelectedDateChanged"
                                FontSize="12" Padding="6,4" />
                </StackPanel>
            </Border>

            <!-- 요약 타일 -->
            <Grid Margin="0,0,0,12">
                <Grid.ColumnDefinitions>
                    <ColumnDefinition Width="*" />
                    <ColumnDefinition Width="12" />
                    <ColumnDefinition Width="*" />
                    <ColumnDefinition Width="12" />
                    <ColumnDefinition Width="*" />
                    <ColumnDefinition Width="12" />
                    <ColumnDefinition Width="*" />
                </Grid.ColumnDefinitions>
                <Border Grid.Column="0" Background="White" BorderBrush="{DynamicResource CardBorder}" BorderThickness="1" Padding="14">
                    <StackPanel>
                        <TextBlock Text="총매출액" FontSize="12" FontWeight="Bold" Foreground="{DynamicResource MutedText}" />
                        <TextBlock Text="{Binding TotalAmountStr}" FontSize="20" FontWeight="Bold" Foreground="{DynamicResource Accent}" Margin="0,6,0,0" />
                    </StackPanel>
                </Border>
                <Border Grid.Column="2" Background="White" BorderBrush="{DynamicResource CardBorder}" BorderThickness="1" Padding="14">
                    <StackPanel>
                        <TextBlock Text="현금" FontSize="12" FontWeight="Bold" Foreground="{DynamicResource MutedText}" />
                        <TextBlock Text="{Binding TotalCashStr}" FontSize="20" FontWeight="Bold" Foreground="{DynamicResource TitleText}" Margin="0,6,0,0" />
                    </StackPanel>
                </Border>
                <Border Grid.Column="4" Background="White" BorderBrush="{DynamicResource CardBorder}" BorderThickness="1" Padding="14">
                    <StackPanel>
                        <TextBlock Text="카드결제1" FontSize="12" FontWeight="Bold" Foreground="{DynamicResource MutedText}" />
                        <TextBlock Text="{Binding TotalCard1Str}" FontSize="20" FontWeight="Bold" Foreground="{DynamicResource TitleText}" Margin="0,6,0,0" />
                    </StackPanel>
                </Border>
                <Border Grid.Column="6" Background="White" BorderBrush="{DynamicResource CardBorder}" BorderThickness="1" Padding="14">
                    <StackPanel>
                        <TextBlock Text="카드결제2" FontSize="12" FontWeight="Bold" Foreground="{DynamicResource MutedText}" />
                        <TextBlock Text="{Binding TotalCard2Str}" FontSize="20" FontWeight="Bold" Foreground="{DynamicResource TitleText}" Margin="0,6,0,0" />
                    </StackPanel>
                </Border>
            </Grid>

            <!-- 상세 테이블 -->
            <Border Background="White" BorderBrush="{DynamicResource CardBorder}" BorderThickness="1">
                <StackPanel>
                    <Grid Margin="16,10">
                        <Grid.ColumnDefinitions>
                            <ColumnDefinition Width="1.4*" />
                            <ColumnDefinition Width="0.8*" />
                            <ColumnDefinition Width="1.1*" />
                            <ColumnDefinition Width="1.1*" />
                            <ColumnDefinition Width="1.1*" />
                            <ColumnDefinition Width="1.1*" />
                        </Grid.ColumnDefinitions>
                        <TextBlock Text="날짜" FontSize="12" FontWeight="Bold" Foreground="{DynamicResource MutedText}" />
                        <TextBlock Grid.Column="1" Text="건수" FontSize="12" FontWeight="Bold" Foreground="{DynamicResource MutedText}" HorizontalAlignment="Right" />
                        <TextBlock Grid.Column="2" Text="현금" FontSize="12" FontWeight="Bold" Foreground="{DynamicResource MutedText}" HorizontalAlignment="Right" />
                        <TextBlock Grid.Column="3" Text="카드결제1" FontSize="12" FontWeight="Bold" Foreground="{DynamicResource MutedText}" HorizontalAlignment="Right" />
                        <TextBlock Grid.Column="4" Text="카드결제2" FontSize="12" FontWeight="Bold" Foreground="{DynamicResource MutedText}" HorizontalAlignment="Right" />
                        <TextBlock Grid.Column="5" Text="합계" FontSize="12" FontWeight="Bold" Foreground="{DynamicResource MutedText}" HorizontalAlignment="Right" />
                    </Grid>

                    <TextBlock Text="조회된 매출이 없습니다" FontSize="13" Foreground="{DynamicResource MutedText}"
                               HorizontalAlignment="Center" Margin="0,24">
                        <TextBlock.Style>
                            <Style TargetType="TextBlock">
                                <Setter Property="Visibility" Value="Collapsed" />
                                <Style.Triggers>
                                    <DataTrigger Binding="{Binding HasRows}" Value="False">
                                        <Setter Property="Visibility" Value="Visible" />
                                    </DataTrigger>
                                </Style.Triggers>
                            </Style>
                        </TextBlock.Style>
                    </TextBlock>

                    <ScrollViewer MaxHeight="360" VerticalScrollBarVisibility="Auto"
                                  Visibility="{Binding HasRows, Converter={StaticResource BooleanToVisibilityConverter}}">
                        <ItemsControl ItemsSource="{Binding Rows}">
                            <ItemsControl.ItemTemplate>
                                <DataTemplate>
                                    <Grid Margin="16,8">
                                        <Grid.ColumnDefinitions>
                                            <ColumnDefinition Width="1.4*" />
                                            <ColumnDefinition Width="0.8*" />
                                            <ColumnDefinition Width="1.1*" />
                                            <ColumnDefinition Width="1.1*" />
                                            <ColumnDefinition Width="1.1*" />
                                            <ColumnDefinition Width="1.1*" />
                                        </Grid.ColumnDefinitions>
                                        <TextBlock Text="{Binding Label}" FontSize="13" FontWeight="Bold" Foreground="{DynamicResource TitleText}" />
                                        <TextBlock Grid.Column="1" Text="{Binding CountStr}" FontSize="12" Foreground="{DynamicResource MutedText}" HorizontalAlignment="Right" />
                                        <TextBlock Grid.Column="2" Text="{Binding CashStr}" FontSize="12" HorizontalAlignment="Right" />
                                        <TextBlock Grid.Column="3" Text="{Binding Card1Str}" FontSize="12" HorizontalAlignment="Right" />
                                        <TextBlock Grid.Column="4" Text="{Binding Card2Str}" FontSize="12" HorizontalAlignment="Right" />
                                        <TextBlock Grid.Column="5" Text="{Binding AmountStr}" FontSize="13" FontWeight="Bold" HorizontalAlignment="Right" />
                                    </Grid>
                                </DataTemplate>
                            </ItemsControl.ItemTemplate>
                        </ItemsControl>
                    </ScrollViewer>
                </StackPanel>
            </Border>
        </StackPanel>
    </Grid>
</UserControl>
```

- [ ] **Step 3: `MainWindow.xaml`에 DataTemplate 추가**

`src/FishingMartPos/MainWindow.xaml`에서 `<DataTemplate DataType="{x:Type vm:InventoryFormViewModel}">` 블록 바로 뒤(같은 `<Window.Resources>` 안, `</Window.Resources>` 앞)에 추가:

```xml
        <DataTemplate DataType="{x:Type vm:SalesReportViewModel}">
            <views:SalesReportView />
        </DataTemplate>
```

- [ ] **Step 4: `MainMenuView.xaml`에 Visibility 바인딩 추가**

`src/FishingMartPos/Views/MainMenuView.xaml`의 `<UserControl ...>` 시작 태그 바로 다음 줄에 리소스 블록 추가:

```xml
    <UserControl.Resources>
        <BooleanToVisibilityConverter x:Key="BooleanToVisibilityConverter" />
    </UserControl.Resources>
```

그리고 "매출" 타일 버튼(`Command="{Binding GoToSalesReportCommand}"`)의 여는 태그를 다음으로 교체:

```xml
                <Button Command="{Binding GoToSalesReportCommand}" Margin="10" Background="White"
                        BorderBrush="{DynamicResource MenuTileBorder}" BorderThickness="1"
                        Visibility="{Binding IsAdmin, Converter={StaticResource BooleanToVisibilityConverter}}">
```

(그 안의 `<StackPanel>` ~ `</Button>` 내용은 그대로 둔다.)

- [ ] **Step 5: `App.xaml.cs` DI 배선**

`src/FishingMartPos/App.xaml.cs`에서 `async Task<InventoryViewModel> CreateInventoryViewModelAsync(...)` 함수 바로 뒤, `LoginViewModel CreateLoginViewModel() =>` 줄 앞에 다음 함수를 추가:

```csharp
        async Task<SalesReportViewModel> CreateSalesReportViewModelAsync(MainMenuViewModel mainMenu)
        {
            var vm = new SalesReportViewModel(salesRepository, navigation, mainMenu);
            await vm.LoadAsync();
            return vm;
        }
```

그리고 `LoginViewModel CreateLoginViewModel() =>` 줄을 다음으로 교체:

```csharp
        LoginViewModel CreateLoginViewModel() =>
            new(staffRepository, session, navigation, terminals, CreatePosViewModelAsync, CreateInventoryViewModelAsync, CreateSalesReportViewModelAsync);
```

- [ ] **Step 6: 스모크 테스트 작성**

`tests/FishingMartPos.Tests/Views/SalesReportViewSmokeTests.cs` (신규):

```csharp
using FishingMartPos.Views;
using Xunit;

namespace FishingMartPos.Tests.Views;

public class SalesReportViewSmokeTests
{
    [Fact]
    public void SalesReportView_ConstructsWithoutException()
    {
        StaTestHelper.RunOnSta(() =>
        {
            var view = new SalesReportView();
            Assert.NotNull(view);
        });
    }
}
```

- [ ] **Step 7: 빌드 및 전체 테스트 실행**

Run: `dotnet build src/FishingMartPos/FishingMartPos.csproj`
Expected: 0 경고, 0 오류 (특히 XAML의 `{DynamicResource}` 키가 모두 `AppColors.cs`에 존재하는지, `{Binding}` 대상이 모두 `SalesReportViewModel`의 실제 멤버인지 확인).

Run: `dotnet test`
Expected: 기존 테스트 전부 + 이번에 추가한 테스트 전부 통과.

- [ ] **Step 8: 커밋**

```bash
git add src/FishingMartPos/Views/SalesReportView.xaml src/FishingMartPos/Views/SalesReportView.xaml.cs src/FishingMartPos/App.xaml.cs src/FishingMartPos/MainWindow.xaml src/FishingMartPos/Views/MainMenuView.xaml tests/FishingMartPos.Tests/Views/SalesReportViewSmokeTests.cs
git commit -m "매출관리 화면(SalesReportView) 추가, 메인메뉴 '매출' 타일을 ADMIN 전용으로 노출"
```

---

## 완료 후 확인 (컨트롤러가 직접 수행)

subagent는 GUI에 접근할 수 없으므로, 모든 태스크 완료 후 컨트롤러가 실행 중인 앱에서 직접 확인한다:
1. ADMIN(PIN `0000`)으로 로그인 → 메인메뉴에 "매출" 타일이 보이는지
2. "매출" 타일 클릭 → 매출관리 화면 진입, 기본이 일일매출/최근 7일인지
3. 실제 판매(POS 화면에서 결제)를 한 건 만든 뒤 매출관리에서 조회되는지 (현금/카드1/카드2 중 하나로)
4. 월간매출 탭 전환 시 정상적으로 그 달 합계가 나오는지
5. 기간을 판매가 없는 범위로 바꿨을 때 "조회된 매출이 없습니다"가 뜨는지
