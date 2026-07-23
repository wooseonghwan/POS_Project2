# 카드결제 VAN 승인 스텁(Stub) 설계

## 배경

판매 화면의 카드결제1/카드결제2는 현재 100% 시뮬레이션이다. `PosViewModel.PayCard1`/`PayCard2`가 공용 `PayAsync`를 호출하는데, `VanApprovalNo`/`VanCode`를 항상 `null`로 하드코딩하고 결제수단과 무관하게 곧바로 `COMPLETE` 상태의 매출을 저장한다 — HTTP 호출도, 단말기 응답 대기도 없다.

실제 KICC 연동을 위해서는 매장 계산대에서 물리 카드단말기와 통신하는 로컬 에이전트(레거시 VB6 `frmCREDIT_KICC.frm`이 쓰던 방식, KICC의 "EasyCard" 계열 프로그램으로 추정)의 요청/응답 스펙이 필요하다. 이번에 웹 검색으로 KICC 공식 개발자센터(`docs.kicc.co.kr`)의 VAN 결제 API(`reqAprv`/`reqCancel`/`reqRtran`)를 확인했으나, 이는 미리 등록된 카드(토큰)로 결제하는 "간편결제/이지톡페이"용 API이며, 매장 카운터에서 실물 카드를 단말기에 꽂는 방식과는 다르다. 실물 단말기 연동 스펙은 VAN사가 등록된 POS 소프트웨어 업체에 개별적으로 제공하는 비공개 문서로 보이며, 현재 이 저장소에는 확보되어 있지 않다.

**따라서 이번 작업 범위는 실제 KICC HTTP 연동이 아니라, 나중에 실제 연동으로 교체하기 쉬운 형태로 승인 로직을 추상화하고, 승인/거절 양쪽 흐름이 실제처럼 동작하는 스텁(stub)을 구현하는 것이다.** 실제 KICC 연동은 로컬 에이전트 API 문서를 확보한 이후 별도 작업으로 진행한다.

## 목표

- 카드결제 시 "즉시 완료"가 아니라, 승인 요청 → (지연) → 승인/거절 응답을 받는 흐름으로 바꾼다.
- 승인 시에만 매출이 저장되고(`VanApprovalNo`/`VanCode` 채움), 거절 시에는 매출이 저장되지 않고 장바구니가 유지되어 재시도할 수 있어야 한다.
- 향후 실제 KICC 로컬 에이전트 연동으로 교체할 때 `PosViewModel`이나 화면 쪽 코드를 건드리지 않고, 게이트웨이 구현체 하나만 교체하면 되도록 인터페이스로 분리한다.
- 현금결제(`PayCash`)는 이번 변경의 영향을 받지 않는다.

## 아키텍처

### 게이트웨이 추상화

`src/FishingMartPos/Services/IVanPaymentGateway.cs`:

```csharp
public interface IVanPaymentGateway
{
    Task<VanApprovalResult> RequestApprovalAsync(VanApprovalRequest request);
}

public sealed record VanApprovalRequest(string PosCode, string PayType, decimal Amount);

public sealed class VanApprovalResult
{
    public required bool IsApproved { get; init; }
    public string? ApprovalNo { get; init; }   // 승인 시에만 값 있음
    public string? VanCode { get; init; }      // 승인 시에만 "KICC"
    public required string ResponseMessage { get; init; } // 토스트에 그대로 표시할 문구
}
```

- `PayType`은 `"CARD1"`/`"CARD2"` 그대로 넘긴다(두 카드결제 버튼이 실제로는 서로 다른 물리 단말기에 대응한다는 기존 설계를 유지 — 스텁 동작 자체는 두 값에 대해 동일하게 처리한다).

### 승인/거절 판정 분리 (테스트 가능하도록)

`src/FishingMartPos/Services/IVanOutcomeProvider.cs`:

```csharp
public interface IVanOutcomeProvider
{
    bool NextIsApproved();
}
```

`RandomVanOutcomeProvider`: `System.Random` 기반, 승인율 85%(`NextDouble() < 0.85`)로 판정한다. 이 인터페이스로 분리하는 이유는 `StubVanPaymentGateway`의 승인/거절 각 경로를 단위 테스트에서 결정적으로 재현하기 위함이다(가짜 provider로 항상 true/false를 반환시켜 테스트).

### 스텁 게이트웨이

`src/FishingMartPos/Services/StubVanPaymentGateway.cs` — `IVanPaymentGateway` 구현:

- 생성자에서 `IDelayProvider`, `IVanOutcomeProvider`를 주입받는다.
- `RequestApprovalAsync`: `_delay.Delay(1500ms)`로 처리 지연을 흉내낸 뒤, `_outcomeProvider.NextIsApproved()`로 승인 여부를 결정한다.
  - 승인: `ApprovalNo`는 `DateTime.Now`를 `yyyyMMddHHmmss` 형식으로 만든 14자리 문자열(가짜 승인번호), `VanCode = "KICC"`, `ResponseMessage = "카드 결제 완료"`.
  - 거절: 아래 문구 중 하나를 무작위로(같은 `IVanOutcomeProvider`가 제공하는 `Random`을 재사용하지 않고 게이트웨이 자체 `Random` 필드로 인덱스만 고름 — 승인/거절 판정 자체는 이미 `IVanOutcomeProvider`가 결정했으므로 문구 선택은 테스트에서 검증 대상이 아님) 골라 `ResponseMessage`에 담는다: `"한도초과"`, `"카드 조회 실패"`, `"가맹점 정보 오류"`, `"응답 시간 초과"`. `ApprovalNo`/`VanCode`는 `null`.

### `PosViewModel` 변경

- `PayCard1`/`PayCard2` → 공용 `PayCardAsync(string payType)`로 통합(기존 `PayAsync`와는 별도 메서드 — `PayAsync`는 `PayCash` 전용으로 남긴다. `PayCash`는 게이트웨이를 호출하지 않으므로 억지로 공유 경로에 넣지 않는다).
- 새 `[ObservableProperty] bool _isCardProcessing` 추가. `PayCash`/`PayCard1`/`PayCard2` 커맨드 모두 `CanExecute`에서 `!IsCardProcessing`을 확인해 처리 중 중복 클릭을 막는다(현금결제도 카드 처리 중엔 눌리지 않아야 화면이 꼬이지 않는다).
- `PayCardAsync` 흐름:
  1. 장바구니가 비어 있으면 기존과 동일하게 즉시 리턴.
  2. `IsCardProcessing = true`, `IsToastWarning = false`, `ToastMessage = "카드 결제 처리 중..."`.
  3. `var result = await _vanGateway.RequestApprovalAsync(new(_session.CurrentTerminal!.PosCode, payType, _cart.Total));`
  4. **승인(`result.IsApproved == true`)**: `SaleHeader`를 구성하되 `VanApprovalNo = result.ApprovalNo`, `VanCode = result.VanCode`로 채우고, 기존 `PayAsync`가 하던 대로 `_salesRepository.CreateSaleAsync` → 성공 토스트(`result.ResponseMessage`, 1200ms) → `ResetOrder()`.
  5. **거절(`result.IsApproved == false`)**: `CreateSaleAsync` 호출하지 않음(재고 차감도 발생하지 않음). 장바구니는 그대로 유지. `IsToastWarning = true`, `ToastMessage = result.ResponseMessage`로 경고 토스트(1200ms) 후 클리어. `ResetOrder()`를 호출하지 않아 그대로 재시도 가능해야 한다.
  6. `finally`에서 `IsCardProcessing = false`.

### DI 등록

`App.xaml.cs`의 서비스 등록부에 다음을 추가한다(다른 서비스들과 동일한 방식):

```csharp
services.AddSingleton<IVanOutcomeProvider, RandomVanOutcomeProvider>();
services.AddSingleton<IVanPaymentGateway, StubVanPaymentGateway>();
```

등록부 근처에 "실제 KICC 로컬 에이전트 연동 시 `IVanPaymentGateway` 구현체만 교체(예: `KiccVanPaymentGateway`)" 주석을 남긴다.

## 화면(XAML) 변경

- 결제 버튼 3개(현금/카드결제1/카드결제2)의 `IsEnabled`(또는 커맨드 `CanExecute`가 이미 처리하므로 버튼 자체 바인딩은 불필요 — WPF는 `ICommand.CanExecute`가 false면 버튼을 자동으로 비활성화한다)는 코드 변경 없이 커맨드의 `CanExecute` 갱신만으로 처리된다. `[RelayCommand(CanExecute = ...)]` 사용 시 `IsCardProcessing` 변경에 대해 `NotifyCanExecuteChangedFor`를 걸어줘야 한다(재고관리 화면에서 겪은 "CanExecute 캐시가 안 갱신되는" 이슈를 반복하지 않도록 — 프로퍼티 변경 시점에 커맨드가 실제로 재평가되는지 구현 단계에서 확인).
- 토스트는 기존 `ToastMessage`/`IsToastWarning` 바인딩을 그대로 재사용하므로 XAML 변경 없음.

## 새로 생기는 파일

- `src/FishingMartPos/Services/IVanPaymentGateway.cs` (인터페이스 + `VanApprovalRequest`/`VanApprovalResult`)
- `src/FishingMartPos/Services/StubVanPaymentGateway.cs`
- `src/FishingMartPos/Services/IVanOutcomeProvider.cs`
- `src/FishingMartPos/Services/RandomVanOutcomeProvider.cs`

## 수정되는 기존 파일

- `src/FishingMartPos/ViewModels/PosViewModel.cs` — 생성자에 `IVanPaymentGateway` 주입, `PayCard1`/`PayCard2`/`PayCardAsync`/`IsCardProcessing` 추가
- `src/FishingMartPos/App.xaml.cs` — DI 등록

## 테스트 계획

- `StubVanPaymentGatewayTests`: 가짜 `IVanOutcomeProvider`(항상 true/항상 false 반환)로 승인 경로/거절 경로 각각 검증 — 승인 시 `ApprovalNo`/`VanCode`가 채워지는지, 거절 시 둘 다 `null`이고 `ResponseMessage`가 4가지 문구 중 하나인지. `IDelayProvider`는 즉시 완료되는 가짜로 대체(테스트 속도).
- `PosViewModelTests`에 추가:
  - 가짜 `IVanPaymentGateway`가 승인을 반환하면: `CreateSaleAsync`가 호출되고, 전달된 `SaleHeader.VanApprovalNo`/`VanCode`가 게이트웨이 응답값과 일치하며, 장바구니가 초기화되는지
  - 가짜 게이트웨이가 거절을 반환하면: `CreateSaleAsync`가 호출되지 않고, 장바구니(카트 라인)가 그대로 남아 있으며, `IsToastWarning == true`인지
  - 처리 중(`IsCardProcessing == true`)일 때 `PayCash`/`PayCard1`/`PayCard2` 커맨드의 `CanExecute`가 모두 false인지
  - 장바구니가 비어 있으면 게이트웨이를 호출하지 않고 즉시 리턴하는지(기존 동작 유지)

## 이번 범위에 포함하지 않는 것

- 실제 KICC HTTP/로컬 에이전트 연동 — 로컬 에이전트 API 문서 확보 후 별도 설계/작업으로 진행.
- `van_config_tb`(가맹점번호/단말기번호 등) 읽기/쓰기 — 실제 연동 시점에 필요.
- 결제 취소/망취소(KICC `reqCancel`/`reqRtran`에 대응하는 동작) — 실제 연동 스펙이 없으므로 이번 스텁에는 없음.
