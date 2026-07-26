# 결제관리 / 직전정보 기능 설계

**작성일:** 2026-07-26

## 배경

기존 POS 화면에는 원래 프로토타입에서 포팅해온 "영수증관리"/"직전정보"/"영수증발행" 3개 버튼이 있지만, 지금까지 전부 `IsEnabled="False"` 상태의 비활성 placeholder였다(`PosView.xaml:251-253`). 그중 "직전정보"는 레거시 VB6 POS에서 방금 결제한 건의 정보를 보여주고 그 자리에서 취소/영수증 처리를 할 수 있는 화면이었다(참고 사진: 거래일자/거래번호/결제구분/결제정보 + 거래취소/현금영수증으로 변경하기/영수증 재발행/처음 버튼).

여기에 더해, 사용자는 특정 건을 찾아 취소할 수 있는 화면이 메인메뉴에도 필요하다고 요청했다("이전 결제 정보로 취소하거나 내역을 보는 부분이 없어서, 그런 관리가 필요한 대메뉴가 필요함"). 두 요구사항 모두 "거래 조회 + 취소/영수증 처리"라는 동일한 로직을 필요로 하므로, 하나의 기능으로 묶어서 설계한다.

이 기능은 이전에 이름 붙였던 "Feature D(매출취소)"의 상위 호환이다 — 카드 취소(D4)뿐 아니라 현금영수증 취소(B2), 재고 롤백, 영수증 재발행까지 포함하도록 범위가 넓어졌다.

## 범위

1. **직전정보** — POS 화면의 기존 비활성 버튼을 활성화. 이 단말(POS기)의 DB상 마지막 완료(`COMPLETE`) 거래를 팝업으로 보여준다.
2. **결제관리** — 메인메뉴에 추가되는 새 5번째 타일. 기간/결제수단/승인번호로 거래내역을 검색하는 목록 화면. 행을 클릭하면 직전정보와 동일한 상세 패널이 뜬다.
3. 두 진입점이 공유하는 **상세 액션 패널**: 거래취소, 현금영수증으로 변경하기, 영수증 재발행.

**범위 밖:** "영수증관리"/"영수증발행" 버튼(별개의 placeholder, 이번 작업과 무관 — 계속 비활성 상태 유지), 실제 영수증 프린터/돈통 하드웨어 연동(기존과 동일하게 스텁 유지), 카드 취소 시 D2 코드(존재하지 않음 — 아래 KICC 절 참고).

## KICC 연동 정정 사항

기존에는 카드취소가 "당일=D2(직전취소)/타일=D4(일반취소)" 두 코드로 나뉜다고 가정했었다. 그러나 실제 `ED-785_POS연동_인터페이스_SPEC` 문서(command code 목록: `D1`=신용승인, `D4`=신용취소, `I1`/`I4`=EMV, `B1`/`B2`=현금영수증)와 실거래 샘플(`단말기-POS연동_전문샘플(ED-785).xlsx`, "신용 전일 취소 요청/응답" 샘플)을 직접 확인한 결과 **이 단말기는 카드취소 코드가 `D4` 하나뿐**이다. D2는 스펙 어디에도 없다. 따라서 당일/타일 구분 로직 없이 항상 `D4`를 사용한다.

`D4` 취소 요청은 `B2`(현금영수증취소, 이미 구현됨)와 동일한 필드 패턴을 따른다 — 원거래를 특정하기 위해 `S12`(원승인번호)와 `S13`(원거래일자, YYMMDD)를 추가로 실어 보낸다:

```csharp
// 기존 BuildCashReceiptCancelRequest와 대칭 구조
public static string BuildCardCancelRequest(
    KiccMerchantConfig merchant, decimal amount, int installmentMonths,
    string originalApprovalNo, string originalApprovalDateYyMmDd, string posTranNo)
{
    var vat = (int)Math.Round(amount / 11m, MidpointRounding.AwayFromZero);
    string installmentCode = installmentMonths.ToString("00");
    return $"S00=002;S01=D4;S02=40;S03={merchant.Tid};S04={merchant.BusinessNo};" +
           $"S09={installmentCode};S10={(int)amount};S12={originalApprovalNo};S13={originalApprovalDateYyMmDd};S15=0;S16={vat};S23={posTranNo};";
}
```

`원거래일자`는 `SaleHeader.SaleDt`를 `yyMMdd`로 포맷해서 사용한다(별도 저장 컬럼 불필요 — 이미 있는 값으로 충분).

## 데이터 흐름

### 거래 조회
- **직전정보**: `ISalesRepository.GetLastCompletedSaleAsync(posCd)` — `pos_cd`와 `status='COMPLETE'`로 필터, `sale_no DESC LIMIT 1`.
- **결제관리 목록**: `ISalesRepository.SearchSalesAsync(from, to, payType?, approvalNo?)` — `payType`/`approvalNo`는 선택적 필터(null이면 조건 생략). `status` 상관없이 COMPLETE/CANCELLED 모두 보여준다(취소된 건도 이력으로 남아야 하므로).
- 상세 패널을 열 때 `ISalesRepository.GetSaleWithLinesAsync(saleNo)`로 라인아이템까지 조회(영수증 재발행에 필요).

### 거래취소
1. 카드(CARD1/CARD2): `van_config_tb`에서 `PayType` 기준으로 가맹점 설정 조회 → `IVanPaymentGateway.RequestCancelAsync(VanCancelRequest)` 호출(내부적으로 D4 전송) → 승인 실패 시 오류 토스트만 표시하고 아무 것도 바꾸지 않음.
2. 현금: `CashReceiptType != "NONE"`이면 먼저 `ICashReceiptGateway`에 취소(B2) 요청 → 실패 시 오류 토스트, 중단.
3. (카드/현금 공통) 위 단계가 성공했거나 애초에 KICC 호출이 필요 없었다면(현금이면서 영수증 미발행) `ISalesRepository.CancelSaleAsync(saleNo)`로 `status='CANCELLED'` 갱신 + 같은 트랜잭션 안에서 `sales_detail_tb` 라인별 수량만큼 `product_tb.stock_qty`를 다시 더한다(재고 자동 복구, `CreateSaleAsync`의 차감 로직을 역으로 수행).
4. 이미 `CANCELLED`인 건은 취소 버튼을 비활성화한다(중복 취소 방지).

### 현금영수증으로 변경하기
- 조건: `PayType == "CASH"` AND `CashReceiptType == "NONE"`. 다른 경우 버튼 비활성.
- 기존 `ICashReceiptGateway.RequestIssueAsync` 재사용(Feature A와 동일 플로우) → 성공 시 `ISalesRepository.UpdateCashReceiptAsync(saleNo, receiptType, merchant, approvalNo, approvalDate)`로 헤더 갱신.

### 영수증 재발행
- 조회한 `SaleHeader` + 라인아이템으로 `ReceiptDocument`를 재구성(Feature A의 `BuildReceiptDocument` 로직과 동일한 매핑, 재사용 가능하도록 `PosViewModel`에서 공용 메서드로 뽑아내거나 static 헬퍼로 이동)한 뒤 기존 영수증 미리보기 팝업을 그대로 띄운다. 인쇄는 기존 `IReceiptPrinter` 스텁 그대로(실 프린터 연동은 이번 범위 밖).

## 화면 구성

### 직전정보 팝업 (POS 화면)
- 기존 모달 오버레이 패턴 재사용(`Grid.RowSpan="2"` + `ModalOverlay` 배경 + 중앙 `Border`).
- 표시 항목: 거래일자, 거래번호(`sale_no`), 결제구분(현금/카드결제1/카드결제2), 결제금액, 할부개월(카드인 경우만), 라인아이템 목록(상품명/수량/단가/금액).
- 하단 액션 버튼: 거래취소 / 현금영수증으로 변경하기 / 영수증 재발행 / 닫기.
- 이 단말에 완료 거래가 하나도 없으면("최근 거래가 없습니다" 안내 문구) 팝업 자체를 비활성 상태로 유지하거나 안내만 표시.

### 결제관리 화면 (메인메뉴 신규 타일)
- 상단: 기간(시작일~종료일, `DatePicker` 2개, 매출관리 화면과 동일 UX) + 결제수단 필터(전체/현금/카드결제1/카드결제2) + 승인번호 검색(텍스트 입력).
- 목록: 일시/결제수단/금액/승인번호/상태(정상/취소됨) 컬럼, 최신순 정렬.
- 행 클릭 → 직전정보와 동일한 상세 패널(거래취소/현금영수증변경/영수증재발행 액션 포함)이 팝업으로 뜬다.
- `MainMenuView.xaml`의 `UniformGrid Columns="2" Rows="2"`는 5개 타일을 담지 못하므로 레이아웃 조정 필요(예: `Columns="3"`로 변경하고 6칸 중 5칸 사용, 나머지 1칸은 빈 공간). 아이콘은 기존 타일들처럼 `Canvas` 도형으로 새로 그린다.

## 권한

ADMIN 게이트 없음 — STAFF도 전부 사용 가능(매출관리/상품등록/환경설정과 다른 점, 재고 화면과 동일한 접근 레벨).

## 데이터 모델 변경

컬럼 추가 불필요 — 기존 `sales_header_tb`(`status`, `van_approval_no`, `cash_receipt_*`)와 `sales_detail_tb`만으로 충분하다.

`ISalesRepository`에 다음 메서드 추가:
```csharp
Task<SaleHeader?> GetLastCompletedSaleAsync(string posCd);
Task<IReadOnlyList<SaleHeader>> SearchSalesAsync(DateTime from, DateTime to, string? payType, string? approvalNo);
Task<(SaleHeader Header, IReadOnlyList<SaleDetailLine> Lines)?> GetSaleWithLinesAsync(long saleNo);
Task CancelSaleAsync(long saleNo); // status=CANCELLED 갱신 + 재고 롤백을 한 트랜잭션으로
Task UpdateCashReceiptAsync(long saleNo, string receiptType, string merchant, string approvalNo, string approvalDateYyMmDd);
```

`IVanPaymentGateway`에 취소 메서드 추가:
```csharp
Task<VanCancelResult> RequestCancelAsync(VanCancelRequest request);

public sealed record VanCancelRequest(string PosCode, string PayType, decimal Amount, int InstallmentMonths, string OriginalApprovalNo, string OriginalApprovalDateYyMmDd);

public sealed class VanCancelResult
{
    public required bool IsCancelled { get; init; }
    public required string ResponseMessage { get; init; }
}
```

`StubVanPaymentGateway`는 항상 성공하는 취소 스텁을 구현(기존 승인 스텁과 동일한 패턴). `KiccVanPaymentGateway`는 `BuildCardCancelRequest`로 실제 D4를 전송.

## 테스트 방침

- `SalesRepository`의 신규 조회/취소/재고롤백 메서드는 다른 리포지토리 테스트와 동일하게 실제 dev DB로 검증(throwaway 마이그레이션 테스트 패턴 불필요 — 스키마 변경이 없으므로 일반 통합 테스트로 충분).
- `KiccMessageBuilder.BuildCardCancelRequest`는 B1/B2와 동일하게 실거래 샘플 문자열과 리터럴 비교로 검증.
- `PaymentManagementViewModel`/`PosViewModel`의 액션 활성화 조건(이미 취소된 건, 현금영수증 이미 발행된 건 등)은 ViewModel 단위 테스트로 커버.
- 재고 롤백은 취소 전/후 `stock_qty` 값을 비교하는 통합 테스트로 검증.

## 자기 검토

- **플레이스홀더 스캔**: TBD/TODO 없음.
- **내부 일관성**: D4 단일 코드 정정이 KICC 절과 컴포넌트 절 모두에 일관되게 반영됨.
- **범위 점검**: 단일 플랜으로 처리 가능한 범위(리포지토리 확장 + 게이트웨이 취소 메서드 + POS 팝업 + 신규 메인메뉴 화면). 화면이 2개(직전정보 팝업, 결제관리 목록)이지만 로직을 공유하므로 태스크 분해 시 자연스럽게 나눌 수 있다.
- **모호성 점검**: "현금영수증으로 변경하기"는 미발행 건에서만 활성화되는 것으로 명확화. "거래취소"는 이미 취소된 건에서 비활성화되는 것으로 명확화.
