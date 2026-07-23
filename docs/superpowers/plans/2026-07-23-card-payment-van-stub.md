# 카드결제 VAN 승인 스텁 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 판매 화면의 카드결제1/카드결제2를 "즉시 완료" 대신 승인 요청 → 지연 → 승인/거절 응답을 받는 흐름으로 바꾸고, 실제 KICC 연동으로 나중에 교체하기 쉬운 인터페이스(`IVanPaymentGateway`)로 분리한다.

**Architecture:** `IVanPaymentGateway`(승인 요청 추상화)와 `IVanOutcomeProvider`(승인/거절 판정만 분리 — 테스트 결정성 확보용)를 새로 만들고, 스텁 구현체 `StubVanPaymentGateway`가 `IDelayProvider`로 지연을 흉내내며 85% 확률로 승인/거절을 반환한다. `PosViewModel`은 이 게이트웨이를 호출해 승인 시에만 매출을 저장하고, 거절 시 장바구니를 유지한 채 재시도 가능하게 한다.

**Tech Stack:** .NET 8 / WPF / MVVM (CommunityToolkit.Mvvm), xUnit, Dapper/MySQL(변경 없음)

## Global Constraints

- 승인율은 85% (`Random.NextDouble() < 0.85`)로 고정한다 — `docs/superpowers/specs/2026-07-23-card-payment-van-stub-design.md` 참조.
- 처리 지연은 `IDelayProvider.Delay(TimeSpan.FromMilliseconds(1500))`로 흉내낸다.
- 거절 시 사용할 문구는 정확히 이 4개 중 하나여야 한다: `"한도초과"`, `"카드 조회 실패"`, `"가맹점 정보 오류"`, `"응답 시간 초과"`.
- 승인 시 `VanCode`는 정확히 `"KICC"` 문자열이어야 한다.
- 승인번호(`ApprovalNo`)는 `yyyyMMddHHmmss` 형식(14자리 숫자 문자열)이어야 한다.
- 거절 시 `_salesRepository.CreateSaleAsync`를 호출하면 안 된다(재고 차감을 막기 위함) — 장바구니도 초기화하면 안 된다.
- `PayCash`(현금결제)는 게이트웨이를 호출하지 않는다 — 기존 동작 그대로 유지.
- 새 서비스는 기존 `IDelayProvider`/`IPhotoPicker` 등과 동일하게 `src/FishingMartPos/Services/` 아래 인터페이스+구현 파일 쌍으로 만든다.
- `[RelayCommand(CanExecute = ...)]`는 사용하지 않는다 — 이 저장소는 `IsEnabled` XAML 바인딩과 `[RelayCommand(CanExecute=...)]`를 같이 쓰면 WPF의 CanExecute 캐시가 프로그램적 클릭 후 재평가되지 않는 문제를 이미 겪었다(재고관리 페이징 버튼). 대신 `InventoryViewModel.CanGoToNextPage`와 동일한 패턴 — 계산된 `bool` 프로퍼티 + `[NotifyPropertyChangedFor]` + 커맨드 본문에서 직접 가드 — 을 그대로 따른다.

---

### Task 1: `IVanOutcomeProvider` / `RandomVanOutcomeProvider`

**Files:**
- Create: `src/FishingMartPos/Services/IVanOutcomeProvider.cs`
- Create: `src/FishingMartPos/Services/RandomVanOutcomeProvider.cs`
- Test: `tests/FishingMartPos.Tests/Services/RandomVanOutcomeProviderTests.cs`

**Interfaces:**
- Produces: `IVanOutcomeProvider.NextIsApproved(): bool` — Task 2가 이 인터페이스로 승인/거절 판정을 위임받는다.

- [ ] **Step 1: 실패하는 테스트 작성**

`tests/FishingMartPos.Tests/Services/RandomVanOutcomeProviderTests.cs`:

```csharp
using FishingMartPos.Services;
using Xunit;

namespace FishingMartPos.Tests.Services;

public class RandomVanOutcomeProviderTests
{
    [Fact]
    public void NextIsApproved_OverManyCalls_ReturnsBothOutcomes()
    {
        var provider = new RandomVanOutcomeProvider();

        var results = Enumerable.Range(0, 200).Select(_ => provider.NextIsApproved()).ToList();

        Assert.Contains(true, results);
        Assert.Contains(false, results);
    }
}
```

- [ ] **Step 2: 테스트 실패 확인**

Run: `dotnet test --filter RandomVanOutcomeProviderTests`
Expected: FAIL (컴파일 에러 — `RandomVanOutcomeProvider`/`IVanOutcomeProvider`가 아직 없음)

- [ ] **Step 3: 인터페이스와 구현 작성**

`src/FishingMartPos/Services/IVanOutcomeProvider.cs`:

```csharp
namespace FishingMartPos.Services;

public interface IVanOutcomeProvider
{
    bool NextIsApproved();
}
```

`src/FishingMartPos/Services/RandomVanOutcomeProvider.cs`:

```csharp
namespace FishingMartPos.Services;

public sealed class RandomVanOutcomeProvider : IVanOutcomeProvider
{
    private const double ApprovalRate = 0.85;
    private readonly Random _random = new();

    public bool NextIsApproved() => _random.NextDouble() < ApprovalRate;
}
```

- [ ] **Step 4: 테스트 통과 확인**

Run: `dotnet test --filter RandomVanOutcomeProviderTests`
Expected: PASS

- [ ] **Step 5: 커밋**

```bash
git add src/FishingMartPos/Services/IVanOutcomeProvider.cs src/FishingMartPos/Services/RandomVanOutcomeProvider.cs tests/FishingMartPos.Tests/Services/RandomVanOutcomeProviderTests.cs
git commit -m "IVanOutcomeProvider/RandomVanOutcomeProvider 추가 (VAN 승인 판정 분리)"
```

---

### Task 2: `IVanPaymentGateway` / `StubVanPaymentGateway`

**Files:**
- Create: `src/FishingMartPos/Services/IVanPaymentGateway.cs`
- Create: `src/FishingMartPos/Services/StubVanPaymentGateway.cs`
- Create: `tests/FishingMartPos.Tests/Fakes/FakeVanOutcomeProvider.cs`
- Test: `tests/FishingMartPos.Tests/Services/StubVanPaymentGatewayTests.cs`

**Interfaces:**
- Consumes: `IDelayProvider.Delay(TimeSpan): Task` (기존), `IVanOutcomeProvider.NextIsApproved(): bool` (Task 1)
- Produces: `IVanPaymentGateway.RequestApprovalAsync(VanApprovalRequest): Task<VanApprovalResult>`, `VanApprovalRequest(string PosCode, string PayType, decimal Amount)`, `VanApprovalResult { bool IsApproved, string? ApprovalNo, string? VanCode, string ResponseMessage }` — Task 3이 이 타입들을 그대로 사용한다.

- [ ] **Step 1: 테스트용 fake 작성**

`tests/FishingMartPos.Tests/Fakes/FakeVanOutcomeProvider.cs`:

```csharp
using FishingMartPos.Services;

namespace FishingMartPos.Tests.Fakes;

public sealed class FakeVanOutcomeProvider : IVanOutcomeProvider
{
    private readonly bool _isApproved;

    public FakeVanOutcomeProvider(bool isApproved)
    {
        _isApproved = isApproved;
    }

    public bool NextIsApproved() => _isApproved;
}
```

- [ ] **Step 2: 실패하는 테스트 작성**

`tests/FishingMartPos.Tests/Services/StubVanPaymentGatewayTests.cs`:

```csharp
using FishingMartPos.Services;
using FishingMartPos.Tests.Fakes;
using Xunit;

namespace FishingMartPos.Tests.Services;

public class StubVanPaymentGatewayTests
{
    private static readonly string[] KnownDeclineMessages =
    {
        "한도초과", "카드 조회 실패", "가맹점 정보 오류", "응답 시간 초과",
    };

    [Fact]
    public async Task RequestApprovalAsync_WhenOutcomeIsApproved_ReturnsApprovalNoAndKiccVanCode()
    {
        var gateway = new StubVanPaymentGateway(new FakeDelayProvider(), new FakeVanOutcomeProvider(isApproved: true));

        var result = await gateway.RequestApprovalAsync(new VanApprovalRequest("1", "CARD1", 10000m));

        Assert.True(result.IsApproved);
        Assert.NotNull(result.ApprovalNo);
        Assert.Matches("^\\d{14}$", result.ApprovalNo!);
        Assert.Equal("KICC", result.VanCode);
        Assert.Equal("카드 결제 완료", result.ResponseMessage);
    }

    [Fact]
    public async Task RequestApprovalAsync_WhenOutcomeIsDeclined_ReturnsNullApprovalFieldsAndKnownMessage()
    {
        var gateway = new StubVanPaymentGateway(new FakeDelayProvider(), new FakeVanOutcomeProvider(isApproved: false));

        var result = await gateway.RequestApprovalAsync(new VanApprovalRequest("1", "CARD2", 10000m));

        Assert.False(result.IsApproved);
        Assert.Null(result.ApprovalNo);
        Assert.Null(result.VanCode);
        Assert.Contains(result.ResponseMessage, KnownDeclineMessages);
    }
}
```

- [ ] **Step 3: 테스트 실패 확인**

Run: `dotnet test --filter StubVanPaymentGatewayTests`
Expected: FAIL (컴파일 에러 — `IVanPaymentGateway`/`StubVanPaymentGateway`/`VanApprovalRequest`/`VanApprovalResult`가 아직 없음)

- [ ] **Step 4: 인터페이스/모델/구현 작성**

`src/FishingMartPos/Services/IVanPaymentGateway.cs`:

```csharp
namespace FishingMartPos.Services;

public interface IVanPaymentGateway
{
    Task<VanApprovalResult> RequestApprovalAsync(VanApprovalRequest request);
}

public sealed record VanApprovalRequest(string PosCode, string PayType, decimal Amount);

public sealed class VanApprovalResult
{
    public required bool IsApproved { get; init; }
    public string? ApprovalNo { get; init; }
    public string? VanCode { get; init; }
    public required string ResponseMessage { get; init; }
}
```

`src/FishingMartPos/Services/StubVanPaymentGateway.cs`:

```csharp
namespace FishingMartPos.Services;

public sealed class StubVanPaymentGateway : IVanPaymentGateway
{
    private static readonly string[] DeclineMessages =
    {
        "한도초과", "카드 조회 실패", "가맹점 정보 오류", "응답 시간 초과",
    };

    private readonly IDelayProvider _delay;
    private readonly IVanOutcomeProvider _outcomeProvider;
    private readonly Random _messageRandom = new();

    public StubVanPaymentGateway(IDelayProvider delay, IVanOutcomeProvider outcomeProvider)
    {
        _delay = delay;
        _outcomeProvider = outcomeProvider;
    }

    public async Task<VanApprovalResult> RequestApprovalAsync(VanApprovalRequest request)
    {
        await _delay.Delay(TimeSpan.FromMilliseconds(1500));

        if (_outcomeProvider.NextIsApproved())
        {
            return new VanApprovalResult
            {
                IsApproved = true,
                ApprovalNo = DateTime.Now.ToString("yyyyMMddHHmmss"),
                VanCode = "KICC",
                ResponseMessage = "카드 결제 완료",
            };
        }

        return new VanApprovalResult
        {
            IsApproved = false,
            ApprovalNo = null,
            VanCode = null,
            ResponseMessage = DeclineMessages[_messageRandom.Next(DeclineMessages.Length)],
        };
    }
}
```

- [ ] **Step 5: 테스트 통과 확인**

Run: `dotnet test --filter StubVanPaymentGatewayTests`
Expected: PASS

- [ ] **Step 6: 커밋**

```bash
git add src/FishingMartPos/Services/IVanPaymentGateway.cs src/FishingMartPos/Services/StubVanPaymentGateway.cs tests/FishingMartPos.Tests/Fakes/FakeVanOutcomeProvider.cs tests/FishingMartPos.Tests/Services/StubVanPaymentGatewayTests.cs
git commit -m "StubVanPaymentGateway 추가 (카드결제 승인/거절 시뮬레이션)"
```

---

### Task 3: `PosViewModel` 카드결제 흐름 연결

**Files:**
- Modify: `src/FishingMartPos/ViewModels/PosViewModel.cs:13-78` (필드/생성자), `:270-305` (`PayCash`/`PayCard1`/`PayCard2`/`PayAsync`)
- Create: `tests/FishingMartPos.Tests/Fakes/FakeVanPaymentGateway.cs`
- Modify: `tests/FishingMartPos.Tests/ViewModels/PosViewModelTests.cs:21-56` (`CreateViewModel` 헬퍼), 파일 끝에 새 테스트 추가
- Modify: `tests/FishingMartPos.Tests/ViewModels/PosViewModelPaymentTests.cs` (기존에 이미 있던, `new PosViewModel(...)`를 직접 호출하는 별도 테스트 파일 — 생성자 시그니처 변경으로 컴파일이 깨지므로 이 태스크에서 같이 고친다)
- Modify: `tests/FishingMartPos.Tests/ViewModels/MainMenuViewModelTests.cs:21-29` (`new PosViewModel(...)` 호출부)
- Modify: `tests/FishingMartPos.Tests/ViewModels/LoginViewModelTests.cs:39-49` (`new PosViewModel(...)` 호출부)
- Modify: `src/FishingMartPos/App.xaml.cs:43-45,61,67` (DI 등록 + 생성자 호출부 — 생성자 시그니처가 바뀌므로 이 태스크에서 반드시 같이 고쳐야 프로덕션 어셈블리가 컴파일되고, 테스트 프로젝트가 프로덕션 어셈블리에 `ProjectReference`를 갖고 있어 전체 테스트 스위트가 빌드조차 안 되는 사태를 막는다. XAML 버튼 바인딩은 여전히 Task 4의 몫이다.)

**Interfaces:**
- Consumes: `IVanPaymentGateway.RequestApprovalAsync(VanApprovalRequest): Task<VanApprovalResult>` (Task 2), 기존 `ISalesRepository.CreateSaleAsync(SaleHeader, IReadOnlyList<SaleDetailLine>): Task<long>`, `ICurrentSession.CurrentTerminal`/`CurrentStaff`
- Produces: `PosViewModel` 생성자에 `IVanPaymentGateway vanGateway` 파라미터가 **마지막**에 추가됨(기존 8개 파라미터 뒤), `PosViewModel.IsCardProcessing: bool` observable 프로퍼티, `PosViewModel.CanPay: bool` 계산 프로퍼티(`!IsCardProcessing`) — Task 4의 XAML 배선이 `CanPay`를 그대로 사용한다.

**참고 — 계획 수립 시 놓친 부분:** 이 태스크를 처음 작성할 때 `PosViewModelTests.cs` 외에도 `new PosViewModel(...)`를 직접 호출하는 파일이 세 개 더 있다는 것(`PosViewModelPaymentTests.cs`, `MainMenuViewModelTests.cs`, `LoginViewModelTests.cs`) — 그리고 `App.xaml.cs`의 생성자 호출부(원래 Task 4로 미뤄뒀던 부분)도 생성자 시그니처가 바뀌는 이 태스크 시점에 같이 고치지 않으면 전체 빌드가 깨진다는 것 — 을 놓쳤다. 아래 Step 4로 보강한다.

- [ ] **Step 1: 실패하는 테스트 작성 — fake와 헬퍼 확장**

`tests/FishingMartPos.Tests/Fakes/FakeVanPaymentGateway.cs`:

```csharp
using FishingMartPos.Services;

namespace FishingMartPos.Tests.Fakes;

public sealed class FakeVanPaymentGateway : IVanPaymentGateway
{
    private readonly VanApprovalResult _result;
    public List<VanApprovalRequest> Requests { get; } = new();

    public FakeVanPaymentGateway(VanApprovalResult result)
    {
        _result = result;
    }

    public Task<VanApprovalResult> RequestApprovalAsync(VanApprovalRequest request)
    {
        Requests.Add(request);
        return Task.FromResult(_result);
    }
}
```

`tests/FishingMartPos.Tests/ViewModels/PosViewModelTests.cs`의 헬퍼를 아래처럼 바꾼다(파일 상단에 `using FishingMartPos.Services;`가 이미 있으므로 추가 불필요, 기존 21~56행 교체):

```csharp
    private static readonly VanApprovalResult ApprovedResult = new()
    {
        IsApproved = true,
        ApprovalNo = "20260723120000",
        VanCode = "KICC",
        ResponseMessage = "카드 결제 완료",
    };

    private static PosViewModel CreateViewModel(out FakeSalesRepository sales, out FakeHeldOrderRepository held) =>
        CreateViewModel(out sales, out held, out _, out _);

    private static PosViewModel CreateViewModel(
        out FakeSalesRepository sales,
        out FakeHeldOrderRepository held,
        out INavigationService navigation,
        out MainMenuViewModel mainMenuViewModel,
        IVanPaymentGateway? vanGateway = null)
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
        navigation = new NavigationService();
        mainMenuViewModel = new MainMenuViewModel(session, navigation);

        return new PosViewModel(
            new FakeProductRepository(new[] { Bait1, Float1 }),
            new FakeCodeRepository(codes),
            sales,
            held,
            new FakeDelayProvider(),
            session,
            navigation,
            mainMenuViewModel,
            vanGateway ?? new FakeVanPaymentGateway(ApprovedResult));
    }
```

파일 끝(마지막 `}` 앞)에 새 테스트를 추가한다:

```csharp
    [Fact]
    public void CanPay_DefaultsToTrue()
    {
        var vm = CreateViewModel(out _, out _);

        Assert.True(vm.CanPay);
    }

    [Fact]
    public async Task PayCard1_WithEmptyCart_DoesNotCallGatewayOrCreateSale()
    {
        var vanGateway = new FakeVanPaymentGateway(ApprovedResult);
        var vm = CreateViewModel(out var sales, out _, out _, out _, vanGateway);
        await vm.LoadAsync();

        await vm.PayCard1Command.ExecuteAsync(null);

        Assert.Empty(vanGateway.Requests);
        Assert.Empty(sales.CreatedSales);
    }

    [Fact]
    public async Task PayCard1_PassesPosCodePayTypeAndAmountToGateway()
    {
        var vanGateway = new FakeVanPaymentGateway(ApprovedResult);
        var vm = CreateViewModel(out _, out _, out _, out _, vanGateway);
        await vm.LoadAsync();
        vm.VisibleProducts[0].AddCommand.Execute(null); // 5,000원

        await vm.PayCard1Command.ExecuteAsync(null);

        var request = Assert.Single(vanGateway.Requests);
        Assert.Equal("1", request.PosCode);
        Assert.Equal("CARD1", request.PayType);
        Assert.Equal(5000m, request.Amount);
    }

    [Fact]
    public async Task PayCard1_WhenApproved_CreatesSaleWithVanFieldsAndResetsCart()
    {
        var vanGateway = new FakeVanPaymentGateway(new VanApprovalResult
        {
            IsApproved = true,
            ApprovalNo = "20260723999999",
            VanCode = "KICC",
            ResponseMessage = "카드 결제 완료",
        });
        var vm = CreateViewModel(out var sales, out _, out _, out _, vanGateway);
        await vm.LoadAsync();
        vm.VisibleProducts[0].AddCommand.Execute(null);

        await vm.PayCard1Command.ExecuteAsync(null);

        var (header, _) = Assert.Single(sales.CreatedSales);
        Assert.Equal("CARD1", header.PayType);
        Assert.Equal("20260723999999", header.VanApprovalNo);
        Assert.Equal("KICC", header.VanCode);
        Assert.Empty(vm.CartLines);
    }

    [Fact]
    public async Task PayCard2_WhenDeclined_DoesNotCreateSaleAndKeepsCartWithWarningToast()
    {
        var vanGateway = new FakeVanPaymentGateway(new VanApprovalResult
        {
            IsApproved = false,
            ApprovalNo = null,
            VanCode = null,
            ResponseMessage = "한도초과",
        });
        var vm = CreateViewModel(out var sales, out _, out _, out _, vanGateway);
        await vm.LoadAsync();
        vm.VisibleProducts[0].AddCommand.Execute(null);
        var toastValues = new List<string?>();
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(PosViewModel.ToastMessage) && vm.ToastMessage is not null)
                toastValues.Add(vm.ToastMessage);
        };

        await vm.PayCard2Command.ExecuteAsync(null);

        Assert.Empty(sales.CreatedSales);
        Assert.Single(vm.CartLines);
        Assert.True(vm.IsToastWarning);
        Assert.Contains("한도초과", toastValues);
    }

    [Fact]
    public async Task PayCard1_TogglesIsCardProcessingDuringPaymentAndClearsItAfterward()
    {
        var vanGateway = new FakeVanPaymentGateway(ApprovedResult);
        var vm = CreateViewModel(out _, out _, out _, out _, vanGateway);
        await vm.LoadAsync();
        vm.VisibleProducts[0].AddCommand.Execute(null);
        var processingValues = new List<bool>();
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(PosViewModel.IsCardProcessing))
                processingValues.Add(vm.IsCardProcessing);
        };

        await vm.PayCard1Command.ExecuteAsync(null);

        Assert.Contains(true, processingValues);
        Assert.False(vm.IsCardProcessing);
    }
```

- [ ] **Step 2: 테스트 실패 확인**

Run: `dotnet test --filter PosViewModelTests`
Expected: FAIL (컴파일 에러 — `PosViewModel` 생성자가 `IVanPaymentGateway`를 아직 안 받음, `CanPay`/`IsCardProcessing` 없음)

- [ ] **Step 3: `PosViewModel` 최소 구현**

`src/FishingMartPos/ViewModels/PosViewModel.cs` 상단 필드에 추가(기존 `_navigation` 필드 아래):

```csharp
    private readonly IVanPaymentGateway _vanGateway;
```

생성자를 아래처럼 바꾼다(기존 60~78행 교체 — 마지막 파라미터만 추가):

```csharp
    public PosViewModel(
        IProductRepository productRepository,
        ICodeRepository codeRepository,
        ISalesRepository salesRepository,
        IHeldOrderRepository heldOrderRepository,
        IDelayProvider delay,
        ICurrentSession session,
        INavigationService navigation,
        MainMenuViewModel mainMenuViewModel,
        IVanPaymentGateway vanGateway)
    {
        _productRepository = productRepository;
        _codeRepository = codeRepository;
        _salesRepository = salesRepository;
        _heldOrderRepository = heldOrderRepository;
        _delay = delay;
        _session = session;
        _navigation = navigation;
        _mainMenuViewModel = mainMenuViewModel;
        _vanGateway = vanGateway;
    }
```

`[ObservableProperty] private bool _isHeldListVisible;` 아래(43~44행 부근)에 추가:

```csharp
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanPay))]
    private bool _isCardProcessing;
```

`TotalAmountStr` 프로퍼티들 근처(80행 부근)에 계산 프로퍼티 추가:

```csharp
    public bool CanPay => !IsCardProcessing;
```

`PayCash`/`PayCard1`/`PayCard2`/`PayAsync`(기존 269~305행)를 아래로 교체한다:

```csharp
    [RelayCommand]
    private async Task PayCash() => await PayAsync("CASH", "현금 결제 완료");

    [RelayCommand]
    private async Task PayCard1() => await PayCardAsync("CARD1");

    [RelayCommand]
    private async Task PayCard2() => await PayCardAsync("CARD2");

    private List<SaleDetailLine> BuildDetailLines() =>
        _cart.Lines
            .Select(l => new SaleDetailLine { Barcode = l.Barcode, ProductName = l.Name, Qty = l.Qty, UnitPrice = l.Price })
            .ToList();

    private async Task PayAsync(string payType, string toastLabel)
    {
        if (!CanPay) return;
        if (_cart.Lines.Count == 0) return;

        var header = new SaleHeader
        {
            PosCd = _session.CurrentTerminal!.PosCode,
            SaleDt = DateTime.Now,
            StaffCd = _session.CurrentStaff!.StaffCode,
            TotalAmt = _cart.Total,
            PayType = payType,
            CashReceived = payType == "CASH" ? CashAmount : null,
            ChangeAmt = payType == "CASH" ? Math.Max(0, CashAmount - _cart.Total) : null,
            VanApprovalNo = null,
            VanCode = null,
        };

        await _salesRepository.CreateSaleAsync(header, BuildDetailLines());

        IsToastWarning = false;
        ToastMessage = toastLabel;
        await _delay.Delay(TimeSpan.FromMilliseconds(1200));
        ToastMessage = null;
        ResetOrder();
    }

    private async Task PayCardAsync(string payType)
    {
        if (!CanPay) return;
        if (_cart.Lines.Count == 0) return;

        IsCardProcessing = true;
        IsToastWarning = false;
        ToastMessage = "카드 결제 처리 중...";
        try
        {
            var result = await _vanGateway.RequestApprovalAsync(
                new VanApprovalRequest(_session.CurrentTerminal!.PosCode, payType, _cart.Total));

            if (result.IsApproved)
            {
                var header = new SaleHeader
                {
                    PosCd = _session.CurrentTerminal!.PosCode,
                    SaleDt = DateTime.Now,
                    StaffCd = _session.CurrentStaff!.StaffCode,
                    TotalAmt = _cart.Total,
                    PayType = payType,
                    CashReceived = null,
                    ChangeAmt = null,
                    VanApprovalNo = result.ApprovalNo,
                    VanCode = result.VanCode,
                };

                await _salesRepository.CreateSaleAsync(header, BuildDetailLines());

                IsToastWarning = false;
                ToastMessage = result.ResponseMessage;
                await _delay.Delay(TimeSpan.FromMilliseconds(1200));
                ToastMessage = null;
                ResetOrder();
            }
            else
            {
                IsToastWarning = true;
                ToastMessage = result.ResponseMessage;
                await _delay.Delay(TimeSpan.FromMilliseconds(1200));
                ToastMessage = null;
            }
        }
        finally
        {
            IsCardProcessing = false;
        }
    }
```

파일 상단 `using` 목록에 `using FishingMartPos.Services;`가 이미 있는지 확인한다(8행에 이미 있음 — 추가 불필요).

- [ ] **Step 4: 다른 `PosViewModel` 생성 호출부 수정 (컴파일 유지) + DI 등록**

생성자에 9번째 파라미터가 추가되었으므로, `PosViewModel`을 직접 생성하는 다른 모든 위치도 이 태스크에서 같이 고쳐야 한다. 그렇지 않으면 프로덕션 어셈블리가 컴파일되지 않고, 테스트 프로젝트가 `ProjectReference`로 이 어셈블리를 참조하므로 전체 테스트 스위트가 빌드조차 되지 않는다.

**4a. `tests/FishingMartPos.Tests/ViewModels/PosViewModelPaymentTests.cs`** — `CreateViewModel` 헬퍼(17~35행)를 아래로 교체:

```csharp
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
            sales, held, new FakeDelayProvider(), session, navigation, mainMenuViewModel,
            new FakeVanPaymentGateway(new VanApprovalResult
            {
                IsApproved = true,
                ApprovalNo = "20260723120000",
                VanCode = "KICC",
                ResponseMessage = "카드 결제 완료",
            }));
    }
```

그리고 이 파일에서 아래 두 테스트를 **삭제**한다(승인 시 매출이 즉시·동기적으로, `VanCode == null`로 저장된다고 단언하는 예전 스텁 동작 검증이라 이번 변경과 정면으로 모순된다 — 같은 시나리오의 더 정확한 버전이 이미 `PosViewModelTests.cs`의 `PayCard1_WhenApproved_CreatesSaleWithVanFieldsAndResetsCart`/`PayCard2_WhenDeclined_DoesNotCreateSaleAndKeepsCartWithWarningToast`로 이번 태스크에 추가되었다):

```csharp
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
```

(이 파일의 나머지 테스트 — `PayCash_*`, `HoldOrder_*`, `RecallOrder`/`ConfirmReplaceCart`/`DeleteHeldOrder` 관련 — 는 결제 로직을 건드리지 않으므로 그대로 둔다. `FakeVanPaymentGateway`/`VanApprovalResult`는 `using FishingMartPos.Services;`/`using FishingMartPos.Tests.Fakes;`로 이미 파일 상단에 임포트되어 있다.)

**4b. `tests/FishingMartPos.Tests/ViewModels/MainMenuViewModelTests.cs`** — 21~29행의 `new PosViewModel(...)` 호출 마지막에 인자를 하나 추가:

```csharp
        var posViewModel = new PosViewModel(
            new FakeProductRepository(Array.Empty<Product>()),
            new FakeCodeRepository(new Dictionary<string, IReadOnlyList<CodeItem>>()),
            new FakeSalesRepository(),
            new FakeHeldOrderRepository(),
            new FakeDelayProvider(),
            session,
            navigation,
            new MainMenuViewModel(session, navigation),
            new FakeVanPaymentGateway(new VanApprovalResult
            {
                IsApproved = true,
                ApprovalNo = "20260723120000",
                VanCode = "KICC",
                ResponseMessage = "카드 결제 완료",
            }));
```

**4c. `tests/FishingMartPos.Tests/ViewModels/LoginViewModelTests.cs`** — 39~49행의 `new PosViewModel(...)` 호출 마지막에 동일하게 인자를 추가:

```csharp
    private static Func<MainMenuViewModel, Task<PosViewModel>> CreateDummyPosViewModelFactory(
        ICurrentSession session, INavigationService navigation) =>
        mainMenu => Task.FromResult(new PosViewModel(
            new FakeProductRepository(Array.Empty<Product>()),
            new FakeCodeRepository(new Dictionary<string, IReadOnlyList<CodeItem>>()),
            new FakeSalesRepository(),
            new FakeHeldOrderRepository(),
            new FakeDelayProvider(),
            session,
            navigation,
            mainMenu,
            new FakeVanPaymentGateway(new VanApprovalResult
            {
                IsApproved = true,
                ApprovalNo = "20260723120000",
                VanCode = "KICC",
                ResponseMessage = "카드 결제 완료",
            })));
```

**4d. `src/FishingMartPos/App.xaml.cs`** — DI 등록과 프로덕션 생성자 호출부(원래 Task 4 몫이었던 DI 배선을 컴파일을 지키기 위해 여기로 앞당김). 43행(`services.AddSingleton<IDelayProvider, DelayProvider>();`) 바로 아래에 추가:

```csharp
        services.AddSingleton<IVanOutcomeProvider, RandomVanOutcomeProvider>();
        // 실제 KICC 로컬 에이전트 연동 시 IVanPaymentGateway 구현체만 교체(예: KiccVanPaymentGateway)
        services.AddSingleton<IVanPaymentGateway, StubVanPaymentGateway>();
```

61행(`var delayProvider = _services.GetRequiredService<IDelayProvider>();`) 바로 아래에 추가:

```csharp
        var vanGateway = _services.GetRequiredService<IVanPaymentGateway>();
```

`CreatePosViewModelAsync` 내부(67행)의 `new PosViewModel(...)` 호출을 아래로 교체:

```csharp
            var vm = new PosViewModel(productRepository, codeRepository, salesRepository, heldOrderRepository, delayProvider, session, navigation, mainMenu, vanGateway);
```

- [ ] **Step 5: 전체 테스트 통과 확인**

Run: `dotnet test`
Expected: PASS — 전체 스위트(기존 테스트 전부 + 이번 태스크에서 추가/수정한 테스트 포함) 통과, 실패 0건. `dotnet build`도 0경고/0오류인지 함께 확인한다(Step 4에서 App.xaml.cs를 고쳤으므로 프로덕션 어셈블리 자체의 빌드 성공이 이 태스크의 성공 조건에 포함된다).

- [ ] **Step 6: 커밋**

```bash
git add src/FishingMartPos/ViewModels/PosViewModel.cs src/FishingMartPos/App.xaml.cs tests/FishingMartPos.Tests/Fakes/FakeVanPaymentGateway.cs tests/FishingMartPos.Tests/ViewModels/PosViewModelTests.cs tests/FishingMartPos.Tests/ViewModels/PosViewModelPaymentTests.cs tests/FishingMartPos.Tests/ViewModels/MainMenuViewModelTests.cs tests/FishingMartPos.Tests/ViewModels/LoginViewModelTests.cs
git commit -m "PosViewModel 카드결제를 IVanPaymentGateway 승인/거절 흐름으로 연결"
```

---

### Task 4: XAML 배선, 전체 검증

**Files:**
- Modify: `src/FishingMartPos/Views/PosView.xaml:251-256` (결제 버튼 3개)

**Interfaces:**
- Consumes: `PosViewModel.CanPay`(Task 3)
- Produces: (없음 — 이 태스크가 조립의 마지막 단계)

(DI 등록과 `App.xaml.cs` 생성자 호출부는 컴파일을 지키기 위해 Task 3의 Step 4로 앞당겨졌다 — 이 태스크는 XAML 바인딩과 최종 검증만 다룬다.)

- [ ] **Step 1: XAML 버튼에 `IsEnabled` 바인딩 추가**

`src/FishingMartPos/Views/PosView.xaml`의 251~256행(현금결제/카드결제1/카드결제2 버튼 3개) 각각에 `IsEnabled="{Binding CanPay}"`를 추가한다:

```xml
                        <Button Content="현금결제" Command="{Binding PayCashCommand}" IsEnabled="{Binding CanPay}" Margin="3" FontSize="15" FontWeight="Bold"
                                Background="{DynamicResource Accent}" Foreground="White" BorderBrush="{DynamicResource AccentDark}" />
                        <Button Content="카드결제1" Command="{Binding PayCard1Command}" IsEnabled="{Binding CanPay}" Margin="3" FontSize="15" FontWeight="Bold"
                                Background="{DynamicResource Accent}" Foreground="White" BorderBrush="{DynamicResource AccentDark}" />
                        <Button Content="카드결제2" Command="{Binding PayCard2Command}" IsEnabled="{Binding CanPay}" Margin="3" FontSize="15" FontWeight="Bold"
                                Background="{DynamicResource AccentDark}" Foreground="White" BorderBrush="{DynamicResource AccentDark}" />
```

- [ ] **Step 2: 빌드 확인**

Run: `dotnet build`
Expected: `Build succeeded.` (경고 무관, 에러 0건)

- [ ] **Step 3: 전체 테스트 통과 확인**

Run: `dotnet test`
Expected: 기존 테스트 전부 + 이번에 추가한 테스트(Task 1~3) 전부 PASS, 실패 0건

- [ ] **Step 4: 수동 GUI 스모크 테스트**

앱을 실행해(`dotnet run --project src/FishingMartPos`) 관리자 PIN(`0000`)으로 로그인 → 판매 화면 진입 → 상품 담기 → "카드결제1" 클릭:
- "카드 결제 처리 중..." 토스트가 잠깐 보이는지
- 이후 "카드 결제 완료"(성공 토스트, 장바구니 비워짐) 또는 거절 문구(경고 토스트, 장바구니 유지)가 뜨는지 — 승인율 85%이므로 여러 번 눌러보며 두 경우 다 재현해본다
- 거절이 뜬 뒤 같은 상품으로 다시 "카드결제1"을 눌러 재시도가 되는지
- 처리 중(토스트가 "처리 중..."일 때) 세 버튼이 비활성화(회색, 클릭 안 됨) 상태인지

- [ ] **Step 5: 커밋 및 푸시**

```bash
git add src/FishingMartPos/Views/PosView.xaml
git commit -m "카드결제 버튼 처리중 비활성화(CanPay) XAML 배선"
git push
```
