# 판매 화면 결제 팝업(현금/카드) 설계

## 배경

현재 판매(POS) 화면의 결제 버튼(현금결제/카드결제1/카드결제2)은 팝업 없이 즉시 처리된다.

- **현금결제**: 하단 키패드로 미리 입력해둔 받은금액(`CashInput`)을 그대로 써서 버튼 클릭 즉시 매출을 저장하고 완료 토스트만 띄운다. 거스름돈을 확인하는 중간 단계가 없다.
- **카드결제1/2**: 버튼 클릭 즉시 `IVanPaymentGateway.RequestApprovalAsync`를 호출한다. 할부개월 개념 자체가 없다 — `KiccMessageBuilder.BuildApprovalRequest`의 `S09`(할부개월) 필드는 항상 `"00"`(일시불)으로 고정되어 있으며, 이는 `docs/superpowers/specs/2026-07-24-kicc-real-payment-design.md`에 "이 가게 규모상 할부 미지원"으로 명시적으로 결정되어 있던 사항이다.

사용자가 구 VB6 POS의 실제 결제 팝업 사진 3장(카드결제 화면1/2, 현금결제 화면1)을 제공했다. 이번 작업은 **그 레퍼런스의 시각 디자인을 그대로 복제하는 것이 아니라, 빠져 있던 기능(할부개월 선택, 거스름돈 확인 단계)만 현재 앱의 기존 테마/색상 톤 안에서 보완**하는 것이 목표다. 이 POS는 카드리더기·영수증프린터 모두 자체 화면이 없는 주변장치이고, 결제 승인도 이 프로그램 프로세스 안에서 직접 이루어진다(별도 결제 단말기 소프트웨어 없음) — 화면에 뭔가 보여줄 수 있는 곳은 이 POS 화면 하나뿐이라는 뜻이며, 이는 아래 "정적 상태 문구" 설계와 일치한다.

## 목표

1. 현금결제: 버튼 클릭 시 계산합계/받은금액/거스름돈을 보여주는 확인 팝업을 띄우고, "계산완료"를 눌러야 실제로 매출이 저장된다.
2. 카드결제1/2: 버튼 클릭 시 할부개월(일시불/2/3/4/6/12개월/기타개월 직접입력)을 선택하는 팝업을 띄우고, "승인요청"을 눌러야 실제 VAN 승인 요청이 나간다. 요청 중에는 "카드 리더기에 카드를 꽂아주세요 / 승인 처리 중입니다" 정적 안내 문구를 보여준다.
3. 선택한 할부개월이 실제 KICC 승인 요청(`S09` 필드)에 반영되고, `sales_header_tb`에 저장되어 매출관리 화면의 일/월별 집계에 "할부 건수" 컬럼으로 나타난다.

## 이번 범위에 포함하지 않는 것 (명시적 제외)

- 레퍼런스 사진의 "전화면"(VAN 고객센터 전화연결) 버튼 — 실제로 연결할 기능이 없어 제외.
- 카드리더기 실시간 이벤트(`KGetEvent` 폴링) 연동 — 물리 단말기 검증이 필요해 이번 범위 밖. 팝업의 카드 상태 문구는 고정 텍스트다.
- 매출관리에 거래 건별 상세 테이블 추가 — 현재 화면은 일/월별 합계 구조이며, 이번 변경은 그 구조를 유지한 채 "할부 건수" 집계 컬럼만 추가한다. 거래별 상세보기는 이번 범위 밖.
- 카드 승인 취소/재시도 로직 변경 — 기존 거절 시 흐름(장바구니 유지, 경고 토스트)은 그대로 둔다.
- `S09` 필드의 정확한 실물 단말기 인코딩 검증 — 아래 "실물 단말기 검증 필요" 절 참고.

## 화면/흐름 설계

### 현금결제 팝업

- "현금결제" 버튼: 장바구니가 비어 있으면 기존과 동일하게 즉시 경고 토스트("결제할 항목이 존재하지 않습니다") 후 리턴 — 팝업을 띄우지 않는다.
- 장바구니에 항목이 있으면 `IsCashConfirmVisible = true`로 팝업을 띄운다. 팝업 내용은 새로 계산하지 않고 기존에 이미 있는 값을 그대로 바인딩한다: `TotalAmountStr`(계산합계), `CashInputStr`(받은금액), `ChangeStr`(거스름돈).
- 팝업 버튼:
  - **계산완료** (`ConfirmCashPaymentCommand`): 팝업을 닫고 기존 `PayAsync("CASH", "현금 결제 완료")` 로직을 그대로 실행(매출 저장 → 완료 토스트 → `ResetOrder()`).
  - **취소** (`CancelCashPaymentCommand`): 팝업만 닫는다. 장바구니/받은금액 입력 모두 그대로 유지 — 사용자가 금액을 다시 조정하고 재시도할 수 있어야 한다.
- 받은금액이 합계보다 적어도(거스름돈 0원) 막지 않는다 — 기존 동작 그대로 유지, 이번 변경으로 새 검증을 추가하지 않는다.

### 카드결제 팝업 (카드결제1/카드결제2 공통)

- "카드결제1"/"카드결제2" 버튼: 장바구니가 비어 있으면 기존과 동일하게 즉시 경고 토스트 후 리턴.
- 장바구니에 항목이 있으면 카드결제 팝업을 띄운다: `IsCardPaymentVisible = true`, `PendingCardPayType`(`"CARD1"`/`"CARD2"`)을 기록.
- 팝업 상태 1 — **할부개월 선택**:
  - 표시 항목: 결제금액(`TotalAmountStr`), 할부개월 선택지 — 일시불(기본 선택값) / 2개월 / 3개월 / 4개월 / 6개월 / 12개월 / 기타개월(직접입력 텍스트박스, 숫자만 허용).
  - `SelectedInstallmentMonths`(int, 기본 0 = 일시불)로 상태 보관. "기타개월" 선택 시에만 텍스트박스가 활성화되고 입력값이 `SelectedInstallmentMonths`에 반영된다(0 이하 또는 숫자가 아니면 일시불(0)로 취급).
  - 버튼: **승인요청**(`RequestCardApprovalCommand`) / **취소**(`CancelCardPaymentCommand` — 팝업만 닫고 장바구니 그대로 유지).
- 팝업 상태 2 — **승인 처리 중** (승인요청 클릭 후):
  - 할부 선택 UI 대신 고정 안내 문구로 전환: "카드 리더기에 카드를 꽂아주세요" + "승인 처리 중입니다". 이 상태에서는 취소/승인요청 버튼 모두 비활성화(기존 `IsCardProcessing` 패턴 재사용 — 처리 중 중복 클릭 방지).
  - `_vanGateway.RequestApprovalAsync(new VanApprovalRequest(PosCode, PayType, Total, SelectedInstallmentMonths))` 호출.
  - 승인: 팝업을 닫고(`IsCardPaymentVisible = false`) 기존과 동일하게 `SaleHeader` 저장(`InstallmentMonths` 필드 추가분 포함) → 완료 토스트 → `ResetOrder()`.
  - 거절: 팝업을 닫고 기존과 동일하게 경고 토스트, 장바구니는 그대로 유지(재시도 가능).
- 팝업이 닫히면(승인/거절/취소 모두) `SelectedInstallmentMonths`는 다음 결제를 위해 0(일시불)으로 리셋한다.

### 화면 배치

기존 `PosView.xaml`의 다른 오버레이(보류목록, 보류삭제확인, 초기화확인 등)와 동일한 패턴을 그대로 재사용한다: `Grid.RowSpan="2"` 반투명 `ModalOverlay` 배경 + 가운데 정렬된 흰색 `Border` 카드. 새 색상 리소스를 추가하지 않고 기존 `DynamicResource`(Accent, CardBorder, MutedText 등)만 사용한다.

## 데이터 모델 변경

### `IVanPaymentGateway`

```csharp
public sealed record VanApprovalRequest(string PosCode, string PayType, decimal Amount, int InstallmentMonths = 0);
```

- `InstallmentMonths`: 0 = 일시불, 2/3/4/6/12 = 해당 개월, 그 외 양의 정수 = 기타개월 직접입력값.
- `StubVanPaymentGateway`: 이 필드를 무시한다(시뮬레이션 승인/거절 판정에 영향 없음) — 시그니처만 맞춘다.

### `KiccVanPaymentGateway` / `KiccMessageBuilder`

- `BuildApprovalRequest(merchant, amount, posTranNo, installmentMonths)`로 파라미터 추가.
- `S09` 필드를 고정 `"00"` 대신 `installmentMonths.ToString("00")`로 채운다(0 → `"00"`, 2 → `"02"`, 12 → `"12"`).
- **실물 단말기 검증 필요**: `S09`가 2자리 숫자 문자열로 할부개월을 나타낸다는 것은 모듈 API 문서의 필드 설명과 국내 VAN 표준 규격상 통상적인 관례를 근거로 한 추정이며, 이 저장소에는 할부개월 값별 정확한 인코딩표가 확보되어 있지 않다. `docs/superpowers/specs/2026-07-24-kicc-real-payment-design.md`의 "실물 단말기 필요" 체크리스트에 다음 항목을 추가한다: *"할부개월 2/3/4/6/12개월 선택 시 실제 단말기에서 정상 승인되는지, 전표에 할부 개월이 올바르게 표기되는지 확인"*.

### `SaleHeader` / DB

- `SaleHeader`에 `public int InstallmentMonths { get; init; }` 추가(기본값 0).
- `db/migrations/005_add_installment_months.sql` 신규: `ALTER TABLE sales_header_tb ADD COLUMN IF NOT EXISTS installment_months INT NOT NULL DEFAULT 0 AFTER van_code;` (기존 마이그레이션 파일들과 동일하게 순방향 전용, 3자리 번호).
- `SalesRepository.CreateSaleAsync`의 insert문 컬럼 목록에 `installment_months`(파라미터 `@InstallmentMonths`) 추가. `PosViewModel`의 카드결제 흐름에서 `SaleHeader` 생성 시 `InstallmentMonths = SelectedInstallmentMonths`로 채운다(현금결제는 항상 0).

### `PosViewModel` 신규 상태

```csharp
[ObservableProperty] private bool _isCashConfirmVisible;
[ObservableProperty] private bool _isCardPaymentVisible;
[ObservableProperty] private bool _isCardApprovalInProgress; // 팝업 내부 화면 전환(선택 단계 vs 처리중 단계)
[ObservableProperty] private int _selectedInstallmentMonths; // 0 = 일시불
[ObservableProperty] private string _customInstallmentMonthsText = string.Empty;
private string? _pendingCardPayType; // "CARD1" / "CARD2"
```

- `PayCash`: 장바구니 비었으면 기존 경고 토스트, 아니면 `IsCashConfirmVisible = true`로 바꾸고 리턴(더 이상 바로 `PayAsync` 호출하지 않음).
- `PayCard1`/`PayCard2`: 장바구니 비었으면 기존 경고 토스트, 아니면 `_pendingCardPayType` 설정 + `SelectedInstallmentMonths = 0` 초기화 + `IsCardPaymentVisible = true`.
- 새 커맨드: `ConfirmCashPaymentCommand`, `CancelCashPaymentCommand`, `SelectInstallmentCommand(int months)`(할부 버튼 6개가 공유), `RequestCardApprovalCommand`, `CancelCardPaymentCommand`.
- `RequestCardApprovalCommand` 내부에서 기존 `PayCardAsync(string payType)`의 몸통(승인 요청 → 저장/토스트 분기)을 재사용하되, `VanApprovalRequest`에 `SelectedInstallmentMonths`를 실어 보내고 처리 시작 시 `IsCardApprovalInProgress = true`, `finally`에서 `false` + `IsCardPaymentVisible = false`로 팝업을 닫는다.

## 매출관리 화면 변경

- `SalesReportRowViewModel`에 `InstallmentCountStr`(예: `"3건"`) 추가.
- `SalesReportViewModel.RefreshAsync`의 그룹 집계에 `InstallmentCountStr = g.Count(s => s.InstallmentMonths > 0).ToString("N0") + "건"` 추가.
- `SalesReportView.xaml`의 집계 테이블에 "할부 건수" 컬럼 1개 추가(기존 총매출액/현금/카드결제1/카드결제2 컬럼 옆).

## 테스트 계획

- `KiccMessageBuilderTests`: `installmentMonths`별(0, 2, 3, 4, 6, 12, 임의 양수) `S09` 필드가 2자리 문자열로 올바르게 채워지는지.
- `PosViewModelTests` 추가:
  - "현금결제" 클릭 시 팝업(`IsCashConfirmVisible`)만 뜨고 아직 `CreateSaleAsync`가 호출되지 않는지.
  - 현금 팝업에서 "취소" 시 팝업만 닫히고 장바구니/받은금액이 그대로인지.
  - 현금 팝업에서 "계산완료" 시 `CreateSaleAsync` 호출 + 팝업 닫힘 + `ResetOrder()`.
  - "카드결제1/2" 클릭 시 팝업만 뜨고 아직 게이트웨이가 호출되지 않는지.
  - 할부개월 버튼 선택 시 `SelectedInstallmentMonths`가 올바르게 바뀌는지, "기타개월" 직접입력 시 반영되는지(비정상 입력은 0으로 취급).
  - "승인요청" 클릭 시 가짜 게이트웨이로 전달되는 `VanApprovalRequest.InstallmentMonths`가 선택값과 일치하는지, 승인 시 `SaleHeader.InstallmentMonths`도 함께 저장되는지.
  - 카드 팝업에서 "취소" 시 게이트웨이 호출 없이 팝업만 닫히는지.
- `SalesReportViewModelTests`: 할부(`InstallmentMonths > 0`)로 저장된 매출과 일시불 매출이 섞여 있을 때 그룹별 "할부 건수" 집계가 올바른지.

## 새로 생기는 파일

- `db/migrations/005_add_installment_months.sql`

## 수정되는 기존 파일

- `src/FishingMartPos/Services/IVanPaymentGateway.cs` — `VanApprovalRequest`에 `InstallmentMonths` 추가
- `src/FishingMartPos/Services/StubVanPaymentGateway.cs` — 시그니처만 반영
- `src/FishingMartPos/Services/Kicc/KiccMessageBuilder.cs` — `S09` 필드 동적 반영
- `src/FishingMartPos/Services/Kicc/KiccVanPaymentGateway.cs` — 파라미터 전달
- `src/FishingMartPos/Models/SaleHeader.cs` — `InstallmentMonths` 추가
- `src/FishingMartPos/Repositories/SalesRepository.cs` — insert문에 컬럼 추가
- `src/FishingMartPos/Repositories/ISalesRepository.cs` / 구현 — 변경 없음(기존 `GetCompletedSalesAsync`가 `SaleHeader` 전체를 반환하므로 `InstallmentMonths`도 자동 포함)
- `src/FishingMartPos/ViewModels/PosViewModel.cs` — 팝업 상태/커맨드 추가, `PayCash`/`PayCard1`/`PayCard2` 흐름 변경
- `src/FishingMartPos/Views/PosView.xaml` — 현금결제 확인 팝업, 카드결제 팝업(할부선택/승인중 두 상태) 오버레이 추가
- `src/FishingMartPos/ViewModels/SalesReportViewModel.cs` — `InstallmentCountStr` 집계 추가
- `src/FishingMartPos/Views/SalesReportView.xaml` — "할부 건수" 컬럼 추가
- `docs/superpowers/specs/2026-07-24-kicc-real-payment-design.md` — 실물 단말기 체크리스트에 할부개월 검증 항목 추가
