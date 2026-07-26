# 카드결제 서명 (Feature B) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 카드결제 5만원 이상 시 POS 화면에서 서명을 캡처해 KICC 승인요청(D1)의 S30/S31 필드에 실어 보내고, 5만원 미만은 기존과 동일하게 서명 없이 바로 승인 진행한다.

**Architecture:** `ISignatureConverter`(Stub/실제 `Kicc.dll` 구현)를 신설해 `IVanPaymentGateway`의 승인 흐름에 선택적 서명 헥스 필드를 추가하고, `PosViewModel`의 카드결제 팝업에 3번째 상태(서명입력)를 추가한다. 서명 캡처 자체는 WPF `InkCanvas` + 코드비하인드(비트맵 렌더링)로 처리.

**Tech Stack:** .NET 8 WPF/MVVM(CommunityToolkit.Mvvm), WPF InkCanvas, P/Invoke(Kicc.dll), xUnit.

## Global Constraints

- 카드결제 합계금액이 **5만원 이상**이면 서명 **필수** — 서명 없이는 승인 진행 불가(할부 여부/카드결제1·2 구분 없이 동일 적용).
- 5만원 미만은 서명 단계 없이 기존과 동일하게 바로 승인 진행.
- 서명 이미지는 KICC 승인요청에 실어 보내는 것으로 끝 — **POS 쪽에는 파일/DB 어디에도 저장하지 않는다**(변환용 임시 파일도 전송 직후 삭제).
- `Kicc_Bmp2SignDataN()`의 정확한 P/Invoke 시그니처는 벤더 문서에 없음 — `KiccPos.dll`의 자매 함수들(`KGetBmp`/`KSaveToBmp`/`KBmpFileToEpsonPrtData`)과 동일한 패턴(파일 경로 입력 → byte[] 출력 버퍼 → 길이 또는 -1 반환)으로 최선의 추정 구현을 하되, 실제 물리 단말기 없이는 검증 불가능하다는 점을 코드 주석으로 명시한다.
- 기존 메서드 시그니처는 변경하지 않고 새 선택적 파라미터/메서드만 추가한다(하위 호환).

---

## Task 1: ISignatureConverter (Stub + 실제 Kicc.dll 구현)

**Files:**
- Create: `src/FishingMartPos/Services/ISignatureConverter.cs`
- Create: `src/FishingMartPos/Services/Kicc/KiccSignatureConverter.cs`
- Test: `tests/FishingMartPos.Tests/Services/StubSignatureConverterTests.cs`

**Interfaces:**
- Produces: `ISignatureConverter.ConvertToHexAsync(byte[] bmpBytes) : Task<string?>`, `StubSignatureConverter`(개발/테스트용, 항상 고정 헥스 반환), `KiccSignatureConverter`(실제 구현).

- [ ] **Step 1: 실패하는 테스트 작성**

`tests/FishingMartPos.Tests/Services/StubSignatureConverterTests.cs` 신규 생성:

```csharp
using FishingMartPos.Services;
using Xunit;

namespace FishingMartPos.Tests.Services;

public class StubSignatureConverterTests
{
    [Fact]
    public async Task ConvertToHexAsync_ReturnsFixedDummyHex()
    {
        var converter = new StubSignatureConverter();

        var result = await converter.ConvertToHexAsync(new byte[] { 1, 2, 3 });

        Assert.Equal("00", result);
    }
}
```

- [ ] **Step 2: 테스트 실행 (실패 확인)**

Run: `dotnet test tests/FishingMartPos.Tests --filter StubSignatureConverterTests`
Expected: FAIL (`ISignatureConverter`/`StubSignatureConverter` 없음)

- [ ] **Step 3: `ISignatureConverter` + `StubSignatureConverter` 구현**

`src/FishingMartPos/Services/ISignatureConverter.cs` 신규 생성:

```csharp
namespace FishingMartPos.Services;

public interface ISignatureConverter
{
    Task<string?> ConvertToHexAsync(byte[] bmpBytes);
}

public sealed class StubSignatureConverter : ISignatureConverter
{
    public Task<string?> ConvertToHexAsync(byte[] bmpBytes) => Task.FromResult<string?>("00");
}
```

- [ ] **Step 4: 테스트 실행 (통과 확인)**

Run: `dotnet test tests/FishingMartPos.Tests --filter StubSignatureConverterTests`
Expected: PASS

- [ ] **Step 5: 실제 `KiccSignatureConverter` 구현**

`src/FishingMartPos/Services/Kicc/KiccSignatureConverter.cs` 신규 생성:

```csharp
using System.Runtime.InteropServices;
using System.Text;

namespace FishingMartPos.Services.Kicc;

// Kicc_Bmp2SignDataN()의 정확한 파라미터 시그니처는 벤더 문서(전문스펙)에 없다.
// KiccPos.dll의 자매 함수들(KGetBmp/KSaveToBmp/KBmpFileToEpsonPrtData)이 전부
// "파일 경로 입력 → byte[] 출력 버퍼 → 길이(또는 -1) 반환" 패턴을 따르므로 동일하게
// 구현했다 — 실제 물리 단말기/Kicc.dll 없이는 이 시그니처를 검증할 수 없다.
public sealed class KiccSignatureConverter : ISignatureConverter
{
    [DllImport("Kicc.dll", EntryPoint = "Kicc_Bmp2SignDataN", CharSet = CharSet.Ansi)]
    private static extern int Kicc_Bmp2SignDataN(string bmpFilePath, byte[] signData);

    public Task<string?> ConvertToHexAsync(byte[] bmpBytes) => Task.Run(() =>
    {
        string tempFile = Path.Combine(Path.GetTempPath(), $"sign_{Guid.NewGuid():N}.bmp");
        try
        {
            File.WriteAllBytes(tempFile, bmpBytes);
            var signData = new byte[8192];
            int len = Kicc_Bmp2SignDataN(tempFile, signData);
            if (len <= 0) return (string?)null;
            return Encoding.ASCII.GetString(signData, 0, len);
        }
        catch (Exception)
        {
            return (string?)null;
        }
        finally
        {
            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }
        }
    });
}
```

`KiccSignatureConverter`는 실제 `Kicc.dll`(물리 단말기 환경) 없이는 자동 테스트로 검증할 수 없다 — 이 프로젝트의 다른 KICC 실연동 클래스(`KiccPosClient` 등)와 동일한 한계이므로 이 클래스 자체에는 단위 테스트를 추가하지 않는다.

- [ ] **Step 6: 빌드 + 테스트 실행**

Run: `dotnet build src/FishingMartPos/FishingMartPos.csproj`
Run: `dotnet test tests/FishingMartPos.Tests`
Expected: 전체 통과

- [ ] **Step 7: 커밋**

```bash
git add src/FishingMartPos/Services/ISignatureConverter.cs src/FishingMartPos/Services/Kicc/KiccSignatureConverter.cs tests/FishingMartPos.Tests/Services/StubSignatureConverterTests.cs
git commit -m "서명 비트맵을 KICC 헥스 문자열로 변환하는 ISignatureConverter 추가"
```

---

## Task 2: IVanPaymentGateway/KiccMessageBuilder 서명 필드(S30/S31) 확장

**Files:**
- Modify: `src/FishingMartPos/Services/IVanPaymentGateway.cs`
- Modify: `src/FishingMartPos/Services/Kicc/KiccMessageBuilder.cs`
- Modify: `src/FishingMartPos/Services/Kicc/KiccVanPaymentGateway.cs`
- Test: `tests/FishingMartPos.Tests/Services/Kicc/KiccMessageBuilderTests.cs`
- Test: `tests/FishingMartPos.Tests/Services/Kicc/KiccVanPaymentGatewayTests.cs`

**Interfaces:**
- Consumes: 없음(독립적인 확장).
- Produces: `VanApprovalRequest`에 선택적 `SignatureHex` 필드, `KiccMessageBuilder.BuildApprovalRequest`의 `signatureHex` 선택적 파라미터. Task 3이 `PosViewModel`에서 이 필드를 채워 넘긴다.

- [ ] **Step 1: `VanApprovalRequest`에 선택적 필드 추가**

`src/FishingMartPos/Services/IVanPaymentGateway.cs`에서 다음 줄을:

```csharp
public sealed record VanApprovalRequest(string PosCode, string PayType, decimal Amount, int InstallmentMonths = 0);
```

다음으로 교체:

```csharp
public sealed record VanApprovalRequest(string PosCode, string PayType, decimal Amount, int InstallmentMonths = 0, string? SignatureHex = null);
```

- [ ] **Step 2: `BuildApprovalRequest`에 서명 필드 추가하는 실패 테스트 작성**

`tests/FishingMartPos.Tests/Services/Kicc/KiccMessageBuilderTests.cs`에 기존 `BuildApprovalRequest_ComputesVatAndFormatsFields_MatchingKiccDocSample` 테스트 근처에 추가:

```csharp
    [Fact]
    public void BuildApprovalRequest_WithSignatureHex_AppendsS30AndS31Fields()
    {
        var merchant = new KiccMerchantConfig("CARD1", "0788888", "1234567890");

        var sendData = KiccMessageBuilder.BuildApprovalRequest(merchant, 60000m, "POSTRAN123", installmentMonths: 0, signatureHex: "AB12CD");

        Assert.EndsWith("S23=POSTRAN123;S30=1;S31=AB12CD;", sendData);
    }

    [Fact]
    public void BuildApprovalRequest_WithoutSignatureHex_OmitsS30AndS31Fields()
    {
        var merchant = new KiccMerchantConfig("CARD1", "0788888", "1234567890");

        var sendData = KiccMessageBuilder.BuildApprovalRequest(merchant, 1004m, "POSTRAN123", installmentMonths: 0);

        Assert.Equal("S00=002;S01=D1;S02=40;S03=0788888;S04=1234567890;S09=00;S10=1004;S15=0;S16=91;S23=POSTRAN123;", sendData);
        Assert.DoesNotContain("S30", sendData);
        Assert.DoesNotContain("S31", sendData);
    }
```

- [ ] **Step 3: 테스트 실행 (실패 확인)**

Run: `dotnet test tests/FishingMartPos.Tests --filter KiccMessageBuilderTests`
Expected: `BuildApprovalRequest_WithSignatureHex_AppendsS30AndS31Fields`는 FAIL(현재 시그니처에 `signatureHex` 파라미터가 없어 컴파일 실패), 나머지는 기존대로 PASS

- [ ] **Step 4: `BuildApprovalRequest`에 서명 파라미터 추가**

`src/FishingMartPos/Services/Kicc/KiccMessageBuilder.cs`에서 `BuildApprovalRequest` 메서드를 다음으로 교체:

```csharp
    public static string BuildApprovalRequest(KiccMerchantConfig merchant, decimal amount, string posTranNo, int installmentMonths = 0, string? signatureHex = null)
    {
        var vat = (int)Math.Round(amount / 11m, MidpointRounding.AwayFromZero);
        string installmentCode = installmentMonths.ToString("00");
        string signatureFields = signatureHex is not null ? $"S30=1;S31={signatureHex};" : string.Empty;
        return $"S00=002;S01=D1;S02=40;S03={merchant.Tid};S04={merchant.BusinessNo};" +
               $"S09={installmentCode};S10={(int)amount};S15=0;S16={vat};S23={posTranNo};{signatureFields}";
    }
```

- [ ] **Step 5: 테스트 실행 (통과 확인)**

Run: `dotnet test tests/FishingMartPos.Tests --filter KiccMessageBuilderTests`
Expected: 전체 PASS(기존 테스트도 그대로 통과 — `signatureHex`가 없으면 빈 문자열이라 기존 리터럴과 동일)

- [ ] **Step 6: `KiccVanPaymentGateway`가 서명 필드를 전달하도록 수정**

`src/FishingMartPos/Services/Kicc/KiccVanPaymentGateway.cs`의 `RequestApprovalAsync`에서 다음 줄을:

```csharp
        var sendData = KiccMessageBuilder.BuildApprovalRequest(merchant, request.Amount, posTranNo, request.InstallmentMonths);
```

다음으로 교체:

```csharp
        var sendData = KiccMessageBuilder.BuildApprovalRequest(merchant, request.Amount, posTranNo, request.InstallmentMonths, request.SignatureHex);
```

- [ ] **Step 7: `KiccVanPaymentGateway` 서명 전달 테스트 추가**

`tests/FishingMartPos.Tests/Services/Kicc/KiccVanPaymentGatewayTests.cs`의 클래스 안, 마지막 `[Fact]` 다음에 추가:

```csharp
    [Fact]
    public async Task RequestApprovalAsync_WithSignatureHex_ForwardsToSendData()
    {
        var client = new FakeKiccPosClient(KiccRawResponse.Success("R04=0000;R09=1;"));
        var gateway = new KiccVanPaymentGateway(client, Merchants);

        await gateway.RequestApprovalAsync(new VanApprovalRequest("1", "CARD1", 60000m, SignatureHex: "AB12CD"));

        var sendData = Assert.Single(client.Requests).SendData;
        Assert.Contains("S30=1;S31=AB12CD;", sendData);
    }
```

- [ ] **Step 8: 빌드 + 테스트 실행**

Run: `dotnet build src/FishingMartPos/FishingMartPos.csproj`
Run: `dotnet test tests/FishingMartPos.Tests`
Expected: 전체 통과

- [ ] **Step 9: 커밋**

```bash
git add src/FishingMartPos/Services/IVanPaymentGateway.cs src/FishingMartPos/Services/Kicc/KiccMessageBuilder.cs src/FishingMartPos/Services/Kicc/KiccVanPaymentGateway.cs tests/FishingMartPos.Tests/Services/Kicc/KiccMessageBuilderTests.cs tests/FishingMartPos.Tests/Services/Kicc/KiccVanPaymentGatewayTests.cs
git commit -m "카드승인 요청(D1)에 서명 필드(S30/S31) 선택적 추가"
```

---

## Task 3: PosViewModel 서명 캡처 상태/커맨드 + 5만원 기준 분기

**Files:**
- Modify: `src/FishingMartPos/ViewModels/PosViewModel.cs`
- Modify: `src/FishingMartPos/App.xaml.cs`
- Create: `tests/FishingMartPos.Tests/Fakes/FakeSignatureConverter.cs`
- Modify: `tests/FishingMartPos.Tests/ViewModels/PosViewModelTests.cs`
- Modify: `tests/FishingMartPos.Tests/ViewModels/PosViewModelPaymentTests.cs`
- Modify: `tests/FishingMartPos.Tests/ViewModels/MainMenuViewModelTests.cs`
- Modify: `tests/FishingMartPos.Tests/ViewModels/LoginViewModelTests.cs`

**Interfaces:**
- Consumes: `ISignatureConverter`(Task 1), `VanApprovalRequest.SignatureHex`(Task 2).
- Produces: `PosViewModel` 생성자에 새 파라미터(`ISignatureConverter signatureConverter`, `receiptPrinter`와 `paymentManagementViewModelFactory` 사이에 삽입), `IsSignatureCaptureVisible`, `IsInstallmentPanelVisible`, `ConfirmSignatureCommand(byte[]? bmpBytes)`, `CancelSignatureCommand`. Task 4(PosView.xaml)가 이 상태/커맨드에 바인딩한다.

**주의(반복 패턴):** `PosViewModel` 생성자가 또 바뀐다. 시작 전에 실제 호출부를 전부 확인할 것:

```bash
grep -rn "new PosViewModel(" src tests
```
최소 5곳(App.xaml.cs, `PosViewModelTests.cs`, `PosViewModelPaymentTests.cs`, `MainMenuViewModelTests.cs`, `LoginViewModelTests.cs`)이 나온다 — 전부 수정 대상이다. 삽입 위치는 **`receiptPrinter` 바로 다음, `paymentManagementViewModelFactory`(또는 그 팩토리 람다) 바로 앞**이다.

- [ ] **Step 1: `FakeSignatureConverter` 테스트 더블 생성**

`tests/FishingMartPos.Tests/Fakes/FakeSignatureConverter.cs` 신규 생성:

```csharp
using FishingMartPos.Services;

namespace FishingMartPos.Tests.Fakes;

public sealed class FakeSignatureConverter : ISignatureConverter
{
    private readonly string? _hexResult;
    public List<byte[]> ConvertedBytes { get; } = new();

    public FakeSignatureConverter(string? hexResult = "00")
    {
        _hexResult = hexResult;
    }

    public Task<string?> ConvertToHexAsync(byte[] bmpBytes)
    {
        ConvertedBytes.Add(bmpBytes);
        return Task.FromResult(_hexResult);
    }
}
```

- [ ] **Step 2: `PosViewModel` 생성자에 파라미터 추가**

`src/FishingMartPos/ViewModels/PosViewModel.cs`의 필드 목록(`private readonly IReceiptPrinter _receiptPrinter;` 아래)에 추가:

```csharp
    private readonly ISignatureConverter _signatureConverter;
```

생성자 시그니처에서 `IReceiptPrinter receiptPrinter,` 다음 줄에 추가:

```csharp
        ISignatureConverter signatureConverter,
```

(즉 `receiptPrinter` 파라미터와 `Func<MainMenuViewModel, Task<PaymentManagementViewModel>> paymentManagementViewModelFactory` 파라미터 사이에 삽입)

생성자 본문에서 `_receiptPrinter = receiptPrinter;` 다음 줄에 추가:

```csharp
        _signatureConverter = signatureConverter;
```

- [ ] **Step 3: 서명 캡처 상태(`ObservableProperty`) 추가**

같은 파일에서 `private const decimal InstallmentMinimumAmount = 50000m;` 다음 줄에 추가:

```csharp
    private const decimal CardSignatureMinimumAmount = 50000m;
```

`[ObservableProperty] private bool _isCardApprovalInProgress;`를 다음으로 교체(속성 자체는 그대로, 알림 대상만 추가):

```csharp
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsInstallmentPanelVisible))]
    private bool _isCardApprovalInProgress;
```

그 바로 아래에 새 프로퍼티 추가:

```csharp
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsInstallmentPanelVisible))]
    private bool _isSignatureCaptureVisible;
```

`public bool IsInstallmentEligible => _cart.Total >= InstallmentMinimumAmount;` 다음 줄에 추가:

```csharp
    public bool IsInstallmentPanelVisible => !IsCardApprovalInProgress && !IsSignatureCaptureVisible;
```

- [ ] **Step 4: `OpenCardPaymentPopupAsync`/`CancelCardPayment`가 서명 상태를 초기화하도록 수정**

`OpenCardPaymentPopupAsync`에서 `IsCardApprovalInProgress = false;` 다음 줄에 추가:

```csharp
        IsSignatureCaptureVisible = false;
```

`CancelCardPayment`를 다음으로 교체:

```csharp
    [RelayCommand]
    private void CancelCardPayment()
    {
        IsCardPaymentVisible = false;
        IsSignatureCaptureVisible = false;
        _pendingCardPayType = null;
    }
```

- [ ] **Step 5: `RequestCardApproval`을 분기 로직으로 교체하고 실제 승인 처리를 `ProceedWithCardApprovalAsync`로 분리**

`RequestCardApproval` 메서드 전체를 다음으로 교체:

```csharp
    [RelayCommand]
    private async Task RequestCardApproval()
    {
        if (!CanPay) return;
        if (_pendingCardPayType is null) return;

        if (_cart.Total >= CardSignatureMinimumAmount)
        {
            IsSignatureCaptureVisible = true;
            return;
        }

        await ProceedWithCardApprovalAsync(null);
    }

    [RelayCommand]
    private async Task ConfirmSignature(byte[]? bmpBytes)
    {
        if (bmpBytes is null || bmpBytes.Length == 0)
        {
            IsToastWarning = true;
            ToastMessage = "서명을 입력해주세요";
            await _delay.Delay(TimeSpan.FromMilliseconds(1200));
            ToastMessage = null;
            return;
        }

        string? signatureHex = await _signatureConverter.ConvertToHexAsync(bmpBytes);
        IsSignatureCaptureVisible = false;
        await ProceedWithCardApprovalAsync(signatureHex);
    }

    [RelayCommand]
    private void CancelSignature()
    {
        IsSignatureCaptureVisible = false;
    }

    private async Task ProceedWithCardApprovalAsync(string? signatureHex)
    {
        if (!CanPay) return;
        if (_pendingCardPayType is not string payType) return;

        string capturedPayType = payType;
        int installmentMonths = SelectedInstallmentMonths;
        IsCardApprovalInProgress = true;
        IsCardProcessing = true;
        try
        {
            var result = await _vanGateway.RequestApprovalAsync(
                new VanApprovalRequest(_session.CurrentTerminal!.PosCode, capturedPayType, _cart.Total, installmentMonths, signatureHex));

            IsCardApprovalInProgress = false;
            IsCardPaymentVisible = false;

            if (result.IsApproved)
            {
                var header = new SaleHeader
                {
                    PosCd = _session.CurrentTerminal!.PosCode,
                    SaleDt = DateTime.Now,
                    StaffCd = _session.CurrentStaff!.StaffCode,
                    TotalAmt = _cart.Total,
                    PayType = capturedPayType,
                    CashReceived = null,
                    ChangeAmt = null,
                    VanApprovalNo = result.ApprovalNo,
                    VanCode = result.VanCode,
                    InstallmentMonths = installmentMonths,
                };

                await _salesRepository.CreateSaleAsync(header, BuildDetailLines());

                IsToastWarning = false;
                ToastMessage = result.ResponseMessage;
                await _delay.Delay(TimeSpan.FromMilliseconds(1200));
                ToastMessage = null;
                PreviewedReceipt = BuildReceiptDocument(
                    capturedPayType == "CARD1" ? "카드결제1" : "카드결제2", result.ApprovalNo, installmentMonths,
                    null, null);
                IsReceiptPreviewVisible = true;
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
            IsCardApprovalInProgress = false;
            IsCardPaymentVisible = false;
            IsSignatureCaptureVisible = false;
            _pendingCardPayType = null;
        }
    }
```

- [ ] **Step 6: `App.xaml.cs`의 `CreatePosViewModelAsync` 호출에 인자 추가(임시 — Task 5에서 실제 DI 배선 완성)**

`src/FishingMartPos/App.xaml.cs`의 `CreatePosViewModelAsync` 로컬 함수를 다음으로 교체(지금은 항상 `StubSignatureConverter`를 새로 생성 — 실제 DI 등록/실구현 교체는 Task 5에서 완성):

```csharp
        async Task<PosViewModel> CreatePosViewModelAsync(MainMenuViewModel mainMenu)
        {
            var vm = new PosViewModel(productRepository, codeRepository, salesRepository, heldOrderRepository, delayProvider, session, navigation, mainMenu, vanGateway, cashReceiptGateway, receiptPrinter, new StubSignatureConverter(), CreatePaymentManagementViewModelAsync, kiccPosClient);
            await vm.LoadAsync();
            return vm;
        }
```

- [ ] **Step 7: 테스트 파일들의 `new PosViewModel(...)` 호출부 수정**

`tests/FishingMartPos.Tests/ViewModels/PosViewModelTests.cs`의 `CreateViewModel` 헬퍼에서, 파라미터 목록에 `IReceiptPrinter? receiptPrinter = null` 다음에 추가:

```csharp
        ISignatureConverter? signatureConverter = null,
```

`return new PosViewModel(...)` 호출에서 `receiptPrinter ?? new StubReceiptPrinter(),` 다음 줄에 추가:

```csharp
            signatureConverter ?? new FakeSignatureConverter(),
```

`tests/FishingMartPos.Tests/ViewModels/PosViewModelPaymentTests.cs`의 `return new PosViewModel(...)` 호출에서 `receiptPrinter ?? new StubReceiptPrinter(),` 다음 줄에 추가:

```csharp
            new FakeSignatureConverter(),
```

`tests/FishingMartPos.Tests/ViewModels/MainMenuViewModelTests.cs`의 `var posViewModel = new PosViewModel(...)` 호출에서 `new StubReceiptPrinter(),` 다음 줄에 추가:

```csharp
            new FakeSignatureConverter(),
```

`tests/FishingMartPos.Tests/ViewModels/LoginViewModelTests.cs`의 `CreateDummyPosViewModelFactory` 안 `new PosViewModel(...)` 호출에서 `new StubReceiptPrinter(),` 다음 줄에 추가:

```csharp
            new FakeSignatureConverter(),
```

- [ ] **Step 8: 기존 카드승인 테스트 하나가 새 서명 흐름과 충돌 — 수정**

`tests/FishingMartPos.Tests/ViewModels/PosViewModelTests.cs`의 `PayCard1_WhenApproved_CreatesSaleWithVanFieldsAndResetsCart` 테스트를 다음으로 교체(장바구니가 50,000원이라 이제 서명이 필수이므로, 승인요청 후 서명 완료 단계를 거치도록 수정):

```csharp
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
        for (int i = 0; i < 9; i++) vm.IncSelectedCommand.Execute(null); // 5,000원 x 10 = 50,000원 — 할부 가능 & 서명 필요 최소금액

        await vm.PayCard1Command.ExecuteAsync(null);
        vm.SelectInstallmentCommand.Execute("3");
        await vm.RequestCardApprovalCommand.ExecuteAsync(null);
        Assert.True(vm.IsSignatureCaptureVisible); // 5만원 이상이라 서명 단계로 진입, 아직 게이트웨이 호출 안 됨
        Assert.Empty(vanGateway.Requests);
        await vm.ConfirmSignatureCommand.ExecuteAsync(new byte[] { 1, 2, 3 });

        var (header, _) = Assert.Single(sales.CreatedSales);
        Assert.Equal("CARD1", header.PayType);
        Assert.Equal("20260723999999", header.VanApprovalNo);
        Assert.Equal("KICC", header.VanCode);
        Assert.Equal(3, header.InstallmentMonths);
        Assert.Empty(vm.CartLines);
        Assert.False(vm.IsCardPaymentVisible);
    }
```

- [ ] **Step 9: 새 서명 흐름 테스트 추가**

같은 파일(`PosViewModelTests.cs`)에 추가:

```csharp
    [Fact]
    public async Task RequestCardApproval_WhenAmountBelowSignatureThreshold_SkipsSignatureAndCallsGatewayDirectly()
    {
        var vanGateway = new FakeVanPaymentGateway(ApprovedResult);
        var vm = CreateViewModel(out _, out _, out _, out _, vanGateway);
        await vm.LoadAsync();
        vm.VisibleProducts[0].AddCommand.Execute(null); // 5,000원 — 5만원 미만

        await vm.PayCard1Command.ExecuteAsync(null);
        await vm.RequestCardApprovalCommand.ExecuteAsync(null);

        Assert.False(vm.IsSignatureCaptureVisible);
        Assert.Single(vanGateway.Requests);
    }

    [Fact]
    public async Task RequestCardApproval_WhenAmountAtSignatureThreshold_ShowsSignatureCaptureWithoutCallingGateway()
    {
        var vanGateway = new FakeVanPaymentGateway(ApprovedResult);
        var vm = CreateViewModel(out _, out _, out _, out _, vanGateway);
        await vm.LoadAsync();
        vm.VisibleProducts[0].AddCommand.Execute(null);
        for (int i = 0; i < 9; i++) vm.IncSelectedCommand.Execute(null); // 50,000원

        await vm.PayCard1Command.ExecuteAsync(null);
        await vm.RequestCardApprovalCommand.ExecuteAsync(null);

        Assert.True(vm.IsSignatureCaptureVisible);
        Assert.Empty(vanGateway.Requests);
    }

    [Fact]
    public async Task ConfirmSignature_WithEmptyBytes_ShowsWarningAndStaysInSignatureState()
    {
        var vanGateway = new FakeVanPaymentGateway(ApprovedResult);
        var vm = CreateViewModel(out _, out _, out _, out _, vanGateway);
        await vm.LoadAsync();
        vm.VisibleProducts[0].AddCommand.Execute(null);
        for (int i = 0; i < 9; i++) vm.IncSelectedCommand.Execute(null); // 50,000원
        await vm.PayCard1Command.ExecuteAsync(null);
        await vm.RequestCardApprovalCommand.ExecuteAsync(null);
        var toastValues = new List<string?>();
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(PosViewModel.ToastMessage) && vm.ToastMessage is not null)
                toastValues.Add(vm.ToastMessage);
        };

        await vm.ConfirmSignatureCommand.ExecuteAsync(null);

        Assert.Equal("서명을 입력해주세요", Assert.Single(toastValues));
        Assert.Empty(vanGateway.Requests);
        Assert.True(vm.IsSignatureCaptureVisible);
    }

    [Fact]
    public async Task ConfirmSignature_WithBytes_ConvertsAndSendsHexToGateway()
    {
        var vanGateway = new FakeVanPaymentGateway(ApprovedResult);
        var signatureConverter = new FakeSignatureConverter("HEXDATA1");
        var vm = CreateViewModel(out var sales, out _, out _, out _, vanGateway, signatureConverter: signatureConverter);
        await vm.LoadAsync();
        vm.VisibleProducts[0].AddCommand.Execute(null);
        for (int i = 0; i < 9; i++) vm.IncSelectedCommand.Execute(null); // 50,000원
        await vm.PayCard1Command.ExecuteAsync(null);
        await vm.RequestCardApprovalCommand.ExecuteAsync(null);

        await vm.ConfirmSignatureCommand.ExecuteAsync(new byte[] { 9, 9, 9 });

        Assert.Single(signatureConverter.ConvertedBytes);
        var request = Assert.Single(vanGateway.Requests);
        Assert.Equal("HEXDATA1", request.SignatureHex);
        Assert.Single(sales.CreatedSales);
    }

    [Fact]
    public async Task CancelSignature_ReturnsToInstallmentSelectionWithoutCallingGateway()
    {
        var vanGateway = new FakeVanPaymentGateway(ApprovedResult);
        var vm = CreateViewModel(out _, out _, out _, out _, vanGateway);
        await vm.LoadAsync();
        vm.VisibleProducts[0].AddCommand.Execute(null);
        for (int i = 0; i < 9; i++) vm.IncSelectedCommand.Execute(null); // 50,000원
        await vm.PayCard1Command.ExecuteAsync(null);
        await vm.RequestCardApprovalCommand.ExecuteAsync(null);

        vm.CancelSignatureCommand.Execute(null);

        Assert.False(vm.IsSignatureCaptureVisible);
        Assert.True(vm.IsCardPaymentVisible);
        Assert.True(vm.IsInstallmentPanelVisible);
        Assert.Empty(vanGateway.Requests);
    }
```

이 테스트들이 참조하는 `CreateViewModel(out sales, out held, out navigation, out mainMenuViewModel, vanGateway, kiccPosClient, cashReceiptGateway, receiptPrinter, signatureConverter)`의 파라미터 순서/이름이 Step 7에서 실제로 추가한 것과 일치하는지 확인할 것 — 이름있는 인자(`signatureConverter:`)로 호출하므로 위치 순서가 달라도 컴파일된다.

- [ ] **Step 10: 빌드 + 전체 테스트 실행**

Run: `dotnet build src/FishingMartPos/FishingMartPos.csproj`
Expected: 0 경고 / 0 오류

Run: `dotnet test tests/FishingMartPos.Tests`
Expected: 전체 통과

- [ ] **Step 11: 커밋**

```bash
git add src/FishingMartPos/ViewModels/PosViewModel.cs src/FishingMartPos/App.xaml.cs tests/FishingMartPos.Tests/Fakes/FakeSignatureConverter.cs tests/FishingMartPos.Tests/ViewModels/PosViewModelTests.cs tests/FishingMartPos.Tests/ViewModels/PosViewModelPaymentTests.cs tests/FishingMartPos.Tests/ViewModels/MainMenuViewModelTests.cs tests/FishingMartPos.Tests/ViewModels/LoginViewModelTests.cs
git commit -m "PosViewModel에 카드결제 서명 캡처 상태/커맨드 및 5만원 기준 분기 추가"
```

---

## Task 4: PosView.xaml 서명 캡처 UI (InkCanvas)

**Files:**
- Modify: `src/FishingMartPos/Views/PosView.xaml`
- Modify: `src/FishingMartPos/Views/PosView.xaml.cs`

**Interfaces:**
- Consumes: `PosViewModel.IsSignatureCaptureVisible`/`IsInstallmentPanelVisible`(Task 3), `ConfirmSignatureCommand`/`CancelSignatureCommand`(Task 3).
- Produces: 카드결제 팝업의 3번째 상태(서명입력) — 이후 태스크 없음(이 플랜의 마지막 UI 작업).

- [ ] **Step 1: `PosView.xaml`의 할부선택 패널 Visibility를 새 계산 프로퍼티로 변경**

`src/FishingMartPos/Views/PosView.xaml`에서 다음 줄을(할부 선택 상태 StackPanel):

```xml
                    <!-- 할부 선택 상태 -->
                    <StackPanel Visibility="{Binding IsCardApprovalInProgress, Converter={StaticResource InverseBooleanToVisibilityConverter}}">
```

다음으로 교체:

```xml
                    <!-- 할부 선택 상태 -->
                    <StackPanel Visibility="{Binding IsInstallmentPanelVisible, Converter={StaticResource BooleanToVisibilityConverter}}">
```

- [ ] **Step 2: 서명 입력 상태 패널 추가**

`<!-- 승인 처리 중 상태 -->` 섹션 바로 다음(카드결제 팝업의 `StackPanel` 안, `</StackPanel>` `</Border>` 닫히기 전)에 추가:

```xml
                    <!-- 서명 입력 상태 -->
                    <StackPanel Visibility="{Binding IsSignatureCaptureVisible, Converter={StaticResource BooleanToVisibilityConverter}}"
                                Margin="0,12,0,0">
                        <TextBlock Text="고객 서명" FontSize="13" Foreground="{DynamicResource MutedText}"
                                   HorizontalAlignment="Center" Margin="0,0,0,8" />
                        <Border BorderBrush="{DynamicResource CardBorder}" BorderThickness="1" Background="White">
                            <InkCanvas x:Name="SignatureCanvas" Height="160" />
                        </Border>
                        <StackPanel Orientation="Horizontal" HorizontalAlignment="Center" Margin="0,16,0,0">
                            <Button Content="지우기" Padding="16,6" Margin="0,0,8,0" Click="ClearSignatureButton_Click" />
                            <Button Content="취소" Padding="16,6" Margin="0,0,8,0" Click="CancelSignatureButton_Click" />
                            <Button Content="서명완료" Padding="16,6" Background="{DynamicResource Accent}"
                                    Foreground="White" BorderBrush="{DynamicResource AccentDark}"
                                    Click="ConfirmSignatureButton_Click" />
                        </StackPanel>
                    </StackPanel>
```

- [ ] **Step 3: `PosView.xaml.cs` 코드비하인드 추가**

`src/FishingMartPos/Views/PosView.xaml.cs`의 `using` 목록에 다음이 없으면 추가:

```csharp
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
```

클래스 안(다른 이벤트 핸들러들 근처)에 다음 메서드 추가:

```csharp
    private void ClearSignatureButton_Click(object sender, RoutedEventArgs e)
    {
        SignatureCanvas.Strokes.Clear();
    }

    private void CancelSignatureButton_Click(object sender, RoutedEventArgs e)
    {
        SignatureCanvas.Strokes.Clear();
        if (DataContext is ViewModels.PosViewModel vm)
        {
            vm.CancelSignatureCommand.Execute(null);
        }
    }

    private async void ConfirmSignatureButton_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not ViewModels.PosViewModel vm) return;

        byte[]? bmpBytes = null;
        if (SignatureCanvas.Strokes.Count > 0)
        {
            var renderTarget = new RenderTargetBitmap(
                (int)SignatureCanvas.ActualWidth, (int)SignatureCanvas.ActualHeight, 96, 96, PixelFormats.Pbgra32);
            renderTarget.Render(SignatureCanvas);

            var encoder = new BmpBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(renderTarget));
            using var stream = new MemoryStream();
            encoder.Save(stream);
            bmpBytes = stream.ToArray();
        }

        SignatureCanvas.Strokes.Clear();
        await vm.ConfirmSignatureCommand.ExecuteAsync(bmpBytes);
    }
```

현재 `PosView.xaml.cs`는 `System.Linq`/`System.Text`/`System.Windows.Controls`/`System.Windows.Input`만 `using`으로 갖고 있다 — 위 4개(`System.IO`, `System.Windows`, `System.Windows.Media`, `System.Windows.Media.Imaging`)는 전부 새로 추가해야 한다. 기존 코드는 `ViewModels.PosViewModel`처럼 네임스페이스를 완전정규화하지 않고 짧게 쓰는 스타일이다(`FishingMartPos` 네임스페이스 안에 중첩된 `Views`/`ViewModels`가 서로의 형제 네임스페이스를 이렇게 참조할 수 있음) — 위 코드도 이 스타일을 그대로 따른다.

- [ ] **Step 4: 빌드**

Run: `dotnet build src/FishingMartPos/FishingMartPos.csproj`
Expected: 0 경고 / 0 오류(XAML의 `InkCanvas`/`Click` 핸들러 연결까지 컴파일 시 검증됨)

- [ ] **Step 5: 전체 테스트 실행(회귀 확인)**

Run: `dotnet test tests/FishingMartPos.Tests`
Expected: 전체 통과(이 태스크는 XAML/코드비하인드만 변경 — 유닛 테스트는 영향받지 않음)

- [ ] **Step 6: 커밋**

```bash
git add src/FishingMartPos/Views/PosView.xaml src/FishingMartPos/Views/PosView.xaml.cs
git commit -m "카드결제 팝업에 서명 입력(InkCanvas) 상태 추가"
```

---

## Task 5: App.xaml.cs 최종 DI 배선 (Stub/실제 서명 변환기 스위치)

**Files:**
- Modify: `src/FishingMartPos/App.xaml.cs`

**Interfaces:**
- Consumes: `ISignatureConverter`/`StubSignatureConverter`(Task 1), `KiccSignatureConverter`(Task 1).
- Produces: 없음(최종 통합).

- [ ] **Step 1: DI 컨테이너에 `ISignatureConverter` 등록**

`src/FishingMartPos/App.xaml.cs`에서 다음 줄:

```csharp
        services.AddSingleton<IReceiptPrinter, StubReceiptPrinter>();
```

다음 줄 바로 다음에 추가:

```csharp
        services.AddSingleton<ISignatureConverter, StubSignatureConverter>();
```

- [ ] **Step 2: 서비스 resolve + 실제 구현 스위치**

`IReceiptPrinter receiptPrinter = _services.GetRequiredService<IReceiptPrinter>();` 다음 줄에 추가:

```csharp
        ISignatureConverter signatureConverter = _services.GetRequiredService<ISignatureConverter>(); // StubSignatureConverter (기본값)
```

`if (config.KiccUseRealGateway)` 블록 안, `cashReceiptGateway = new KiccCashReceiptGateway(realClient, merchantsByPayType);` 다음 줄에 추가:

```csharp
            signatureConverter = new KiccSignatureConverter();
```

- [ ] **Step 3: `CreatePosViewModelAsync`가 Task 3에서 하드코딩했던 `new StubSignatureConverter()`를 실제 resolve된 변수로 교체**

`CreatePosViewModelAsync` 로컬 함수를 다음으로 교체:

```csharp
        async Task<PosViewModel> CreatePosViewModelAsync(MainMenuViewModel mainMenu)
        {
            var vm = new PosViewModel(productRepository, codeRepository, salesRepository, heldOrderRepository, delayProvider, session, navigation, mainMenu, vanGateway, cashReceiptGateway, receiptPrinter, signatureConverter, CreatePaymentManagementViewModelAsync, kiccPosClient);
            await vm.LoadAsync();
            return vm;
        }
```

- [ ] **Step 4: 빌드 + 전체 테스트 실행**

Run: `dotnet build src/FishingMartPos/FishingMartPos.csproj`
Expected: 0 경고 / 0 오류

Run: `dotnet test tests/FishingMartPos.Tests`
Expected: 전체 통과

- [ ] **Step 5: 커밋**

```bash
git add src/FishingMartPos/App.xaml.cs
git commit -m "카드결제 서명 변환기 DI 배선 완성 (KiccUseRealGateway 시 KiccSignatureConverter로 교체)"
```

---

## 다음 작업과의 관계

- **Feature D(매출취소, 카드 D4/D2 취소)**: 이번 작업과 독립적. 우선순위상 다음 작업.
- `KiccSignatureConverter`의 `Kicc_Bmp2SignDataN` 시그니처는 실제 물리 단말기로만 최종 검증 가능 — `project_kicc_van_integration_status` 메모리의 체크리스트에 이 항목을 추가해야 한다.
