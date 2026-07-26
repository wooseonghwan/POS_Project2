# 영수증 발행(일반영수증 + 현금영수증) 설계

## 배경

결제 완료 후 영수증을 발행하는 기능이 전혀 없다. 판매 화면의 "영수증관리"/"직전정보"/"영수증발행" 버튼은 모두 `IsEnabled="False"`인 스텁이고, 실제 인쇄/현금영수증 발급 로직은 없다.

카드결제 승인은 이미 실제 KICC 연동(`IVanPaymentGateway`/`KiccVanPaymentGateway`)으로 처리되고 있으며 이번 작업에서도 변경하지 않는다. 현금영수증(B1 발급/B2 취소)도 **같은 KICC 계약·같은 물리 단말기**를 통해 처리한다 — 카드결제 때와 마찬가지로 `IKiccPosClient`를 재사용한다.

영수증 프린터(용지에 실제로 찍어내는 물리 프린터)는 아직 기종이 결정되지 않았다. 이번 범위는 **인쇄 데이터 구성 + 화면 미리보기까지**이고, 실제 인쇄 연동은 기종이 정해진 뒤 별도 작업으로 진행한다(카드결제 VAN 스텁 때와 동일한 "인터페이스+스텁 먼저, 실 연동은 하드웨어 확정 후" 패턴).

### KICC 현금영수증(B1/B2) 전문 스펙 — 실사양 확보 완료

사용자가 KICC 모듈 API 문서(`ED-721_POS연동인터페이스SPEC_일반버전_P00XX_260423.pdf`)와 실제 전문 샘플(`단말기-POS연동_전문샘플(ED-785)_250827.xlsx`)을 제공해서, D1(카드승인) 때와 달리 **추정이 아니라 실제 필드값을 확인했다**:

**단, 이 필드 인코딩은 벤더 문서에서 그대로 추출한 값일 뿐, 물리 ED-721 단말기로는 아직 검증되지 않았다** — D1(카드승인)이 겪은 것과 동일한 종류의 실기 검증 공백이며, D1 쪽은 프로젝트 메모리에 이 공백이 기록되어 있지만 현금영수증(B1/B2) 쪽은 이 저장소 어디에도 기록되어 있지 않았다는 점이 다르다. 실제 단말기로 테스트하기 전까지는 이 공백을 그대로 유지한다.

**B1 (현금영수증 발급) 요청 예시:**
```
S00=002;S01=B1;S02=40;S03=<TID>;S09=00;S10=<금액>;S11=<00|01|10>;S15=0;S16=<부가세>;S23=<POS거래번호>;
```

**B1 응답 예시 (문서 원문):**
```
S01=B1;S02=40;S03=0788888;S09=00;S10=1004;S11=00;S15=0;S16=91;S23=...;
R01=P;R04=0000;R07=2507041324215;R09=149331691   ;R13=현금(소득공제)      ;R15=CASH;
R19=              현금영수증 문의 Tel.126-1-1   http://hometax.go.kr          ** 현금영수증 테스트 거래 **;R23=010***;
```

**B2 (현금영수증 취소) 요청 예시:**
```
S00=002;S01=B2;S02=40;S03=<TID>;S09=00;S10=<금액>;S11=<발급시와 동일 값>;S12=<원승인번호>;S13=<원승인일자 YYMMDD>;S15=0;S16=<부가세>;S23=<POS거래번호>;
```

**필드 의미 (D1과 공유하는 S00-S23 표, ED-721 스펙 p.12-15 확인):**

| 필드 | 의미 | 비고 |
|---|---|---|
| S01 | 전문구분 | `"B1"`=현금승인(공제), `"B2"`=현금취소(공제) |
| S02 | 단말구분 | D1과 동일하게 `"40"`(일반) |
| S03 | 가맹점 TID | 어느 사업자(대원수산/대원낚시마트) 앞으로 등록할지 — 아래 "가맹점 선택" 참고 |
| S04 | 사업자번호 | **사용안함** (S03 TID만으로 가맹점 식별) |
| S09 | 할부개월 | 현금영수증에는 의미 없음 — `"00"` 고정 또는 필드 자체 생략 |
| S10 | 금액 | 거래금액 |
| S11 | 현금영수증 거래용도 | `"00"`=개인현금(소득공제용), `"01"`=사업자현금(지출증빙용), `"10"`=개인자진발급 |
| S12 | 원승인번호 | **B2(취소)에서만 사용** — B1 응답의 R09 값을 그대로 넣는다 |
| S13 | 원승인일자 | **B2(취소)에서만 사용**, `YYMMDD` — B1 응답의 R07(`YYMMDDhhmmssN`) 앞 6자리 |
| S15 | 봉사료 | D1과 동일하게 `"0"` |
| S16 | 부가세 | D1과 동일한 `Math.Round(amount / 11m, ...)` 계산 재사용 |
| S23 | POS 거래번호 | D1과 동일하게 `PosCode + yyMMddHHmmss + 2자리 랜덤` |

응답 파싱은 D1과 **완전히 동일한 `KiccResponseParser.Parse()`를 그대로 재사용**한다 — R04(`"0000"`=성공)/R09(승인번호)는 B1/B2에서도 같은 키로 온다. 새 파서가 필요 없다.

**핵심 발견 — 고객 식별번호(휴대폰번호/사업자등록번호)는 POS가 보내지 않는다.** 요청 전문 S00-S23 어디에도 전화번호/사업자번호를 넣는 필드가 없다. 응답의 R23은 마스킹된 값(`"010***"`)만 돌아온다 — 즉 **고객이 카드단말기 자체의 키패드에 직접 입력**하고, POS는 "이 금액으로, 이 용도(S11)로 현금영수증을 발급해줘"라고 요청만 보낸다. 이는 서명(B 기능)이 단말기 자체가 아니라 화면에서 처리되기로 한 것과 별개로, 현금영수증 번호 입력 자체는 원래 단말기 담당이라는 뜻이다 — POS 쪽에서 별도의 "전화번호 입력" UI를 만들 필요가 없다(카드 리더기 상태 팝업과 동일하게 "단말기에서 입력해주세요" 안내만 필요).

**부가 확인:** 같은 전문 샘플에 `"신용 요청(5만원 이상 할부)"` 예시(금액 51,004원)가 있어, 방금 적용한 "할부는 5만원 이상부터"라는 업무 규칙이 KICC 자체 문서와도 일치함을 확인했다(별도 작업 불필요, 참고용 확인).

## 목표

1. 현금결제 확인 팝업에 현금영수증 선택 추가: **미발행 / 개인(소득공제용) / 사업자(지출증빙용)**. 발행을 선택하면 등록할 가맹점(대원수산/대원낚시마트)도 함께 고른다.
2. 발행 선택 시 KICC B1으로 실제 발급 요청을 보내고, 승인번호를 매출에 저장한다.
3. 결제 완료(현금/카드1/카드2 공통) 후 "영수증 미리보기" 화면에서 매장 정보/상품 목록/합계/결제수단/승인번호/현금영수증 정보를 텍스트로 확인할 수 있다.
4. 실제 프린터 인쇄는 스텁(`IReceiptPrinter`)으로 남긴다 — "프린터 연동은 지원 예정" 안내.

## 이번 범위에 포함하지 않는 것

- 실제 프린터 인쇄(하드웨어 미정)
- 현금영수증 취소(B2) — 게이트웨이 인터페이스에 메서드는 만들어 두지만, 이를 호출하는 화면(매출취소)은 다음 작업(D)에서 다룬다
- 카드결제 자체 취소(D4/D2), 서명(B) — 별도 작업으로 순서상 이후 진행
- 현금결제 버튼 자체를 결제1/2로 나누는 것 — **하지 않는다.** 현금결제는 지금처럼 버튼 하나로 유지하고, 가맹점(사업자) 선택은 현금영수증 발행을 고를 때만 나타난다

## 아키텍처

### 가맹점 선택

`van_config_tb`에 이미 있는 CARD1(대원수산, TID 2977338)/CARD2(대원낚시마트, TID 2977340) 행을 그대로 재사용한다. 현금영수증 발행 시 사용자가 "대원수산"/"대원낚시마트" 중 골라 각각 CARD1/CARD2 키로 `IVanConfigRepository`(기존 리포지토리)에서 TID를 조회해 S03에 채운다. 결제수단 자체(CASH)는 그대로이고, 이 선택은 오직 현금영수증 발행 시점에만 필요한 부가 정보다.

### `ICashReceiptGateway`

```csharp
public interface ICashReceiptGateway
{
    Task<CashReceiptResult> RequestIssueAsync(CashReceiptRequest request);
}

public sealed record CashReceiptRequest(string MerchantPayType, string ReceiptType, decimal Amount);
// MerchantPayType: "CARD1"/"CARD2" (van_config_tb 조회 키 재사용)
// ReceiptType: "PERSONAL"(개인소득공제, S11=00) / "BUSINESS"(사업자지출증빙, S11=01)

public sealed class CashReceiptResult
{
    public required bool IsIssued { get; init; }
    public string? ApprovalNo { get; init; }      // 발급 성공 시 R09
    public string? ApprovalDateYyMmDd { get; init; } // R07 앞 6자리 — 이후 B2 취소에 필요, 지금은 저장만
    public required string ResponseMessage { get; init; }
}
```

- `StubCashReceiptGateway`: `StubVanPaymentGateway`와 동일한 지연+랜덤 승인률 패턴(개발/테스트용, `Kicc:UseRealGateway=false`일 때 DI 등록).
- `KiccCashReceiptGateway`: `IKiccPosClient.RequestAsync(0xFB, 0x14, 0x04, sendData)` 재사용(카드승인과 같은 명령 코드 — S01 값만 D1 대신 B1/B2로 다르다). `KiccMessageBuilder`에 `BuildCashReceiptIssueRequest(merchant, amount, receiptType, posTranNo)` 추가(기존 `BuildApprovalRequest`와 형제 함수, 위 표의 필드 그대로). 응답은 기존 `KiccResponseParser.Parse()` 그대로 사용.

### `IReceiptPrinter` (스텁만)

```csharp
public interface IReceiptPrinter
{
    Task<bool> PrintAsync(ReceiptDocument document);
}

public sealed class StubReceiptPrinter : IReceiptPrinter
{
    public Task<bool> PrintAsync(ReceiptDocument document) => Task.FromResult(false); // 항상 "지원 예정"
}
```

### `ReceiptDocument` (순수 모델, 화면 미리보기용)

```csharp
public sealed class ReceiptDocument
{
    public required string HeaderText { get; init; }   // ReceiptConfig.HeaderText 재사용
    public required string FooterText { get; init; }    // ReceiptConfig.FooterText 재사용
    public required IReadOnlyList<ReceiptLine> Lines { get; init; }
    public required decimal TotalAmt { get; init; }
    public required string PayTypeLabel { get; init; }  // "현금"/"카드결제1"/"카드결제2"
    public string? VanApprovalNo { get; init; }
    public int InstallmentMonths { get; init; }
    public string? CashReceiptTypeLabel { get; init; }  // "개인(소득공제)"/"사업자(지출증빙)"/null(미발행)
    public string? CashReceiptApprovalNo { get; init; }
}

public sealed record ReceiptLine(string ProductName, int Qty, decimal UnitPrice, decimal LineAmt);
```

### DB

`sales_header_tb`에 컬럼 추가 (`006_add_cash_receipt_columns.sql`):

```sql
ALTER TABLE sales_header_tb
    ADD COLUMN cash_receipt_type VARCHAR(10) NOT NULL DEFAULT 'NONE' AFTER installment_months, -- 'NONE'/'PERSONAL'/'BUSINESS'
    ADD COLUMN cash_receipt_merchant VARCHAR(10) NULL AFTER cash_receipt_type, -- 'CARD1'/'CARD2' — 어느 van_config_tb 행으로 등록했는지
    ADD COLUMN cash_receipt_approval_no VARCHAR(40) NULL AFTER cash_receipt_merchant,
    ADD COLUMN cash_receipt_approval_date VARCHAR(6) NULL AFTER cash_receipt_approval_no; -- YYMMDD(R07 앞 6자리) — 향후 B2 취소에 필요
```

### 화면 흐름

**현금결제 확인 팝업 확장** (기존 팝업에 섹션 추가, 새 팝업 아님):
- 계산합계/받은금액/거스름돈 아래에 "현금영수증" 섹션 추가: 미발행(기본값)/개인/사업자 라디오 형태 버튼 3개 + 발행 선택 시에만 나타나는 대원수산/대원낚시마트 선택 버튼 2개.
- "계산완료" 클릭 시: 미발행이면 지금처럼 바로 저장. 발행 선택이면 저장 직전에 `ICashReceiptGateway.RequestIssueAsync` 호출.
- **호출 중 화면 전환**: 앞서 확인했듯 고객 식별번호(휴대폰/사업자등록번호)는 POS가 전송하지 않고 카드단말기 자체 키패드로 입력받는다. 따라서 요청을 보내는 동안 카드결제 팝업의 "카드 리더기에 카드를 꽂아주세요 / 승인 처리 중입니다" 패턴을 그대로 재사용해 **"카드단말기에서 고객 전화번호(또는 사업자등록번호)를 입력해주세요 / 처리 중입니다"** 같은 정적 안내 문구로 전환한다 — 실시간 이벤트 폴링은 하지 않고(기존 카드결제 팝업과 동일한 제약), 응답이 올 때까지 버튼만 비활성화한다.
- 실패 시 경고 토스트 후 팝업 유지(장바구니 보존, 카드결제 거절과 동일한 패턴) → 성공 시 결과를 `SaleHeader`에 채워 저장.

**영수증 미리보기 화면**: 결제 완료 토스트 이후 자동으로 뜨는 팝업 하나 추가(현금/카드1/카드2 공통) — `ReceiptDocument` 내용을 텍스트로 나열. "닫기"와 "인쇄"(스텁, 항상 "프린터 연동은 지원 예정") 버튼.

## 테스트 계획

- `KiccMessageBuilderTests`: B1/B2 전문이 위 문서 원문 샘플과 정확히 일치하는지(리터럴 문자열 대조 — D1 테스트와 동일한 방식).
- `KiccCashReceiptGatewayTests`: 가짜 `IKiccPosClient`로 R04=0000/그 외 응답 시 성공/실패 판정, R09가 `ApprovalNo`로 매핑되는지.
- `PosViewModelTests`: 현금영수증 미발행/개인/사업자 각 경로에서 게이트웨이 호출 여부, 실패 시 팝업 유지 + 장바구니 보존, 성공 시 `SaleHeader.CashReceiptType`/`CashReceiptApprovalNo` 저장 확인.
- `ReceiptDocument` 빌드 로직 단위 테스트(장바구니/결제 정보 → 문서 필드 매핑).

## 다음 작업과의 관계

- **B(카드결제 서명, 5만원 이상)**: 이 작업과 독립적 — 카드결제 팝업(이미 존재)에 서명 캡처 단계를 추가하는 별도 작업.
- **D(매출취소, 카드 D4/D2)**: 이 작업의 `van_config_tb`/`KiccMessageBuilder` 확장 패턴을 그대로 재사용할 수 있다(S01="D4"/"D2", S12/S13 원승인정보 재사용) — B2(현금영수증 취소) UI도 자연스럽게 매출취소 화면에 포함될 가능성이 높다.
