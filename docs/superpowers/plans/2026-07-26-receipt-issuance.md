# 영수증 발행(일반영수증 + 현금영수증) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 결제 완료 후 영수증 미리보기(일반영수증 데이터 구성 + 화면 표시)와 현금영수증(개인/사업자) 실제 KICC B1 발급 연동을 추가한다.

**Architecture:** 현금결제 확인 팝업에 현금영수증 선택(미발행/개인/사업자 + 대원수산/대원낚시마트 가맹점 선택)을 추가하고, "계산완료" 시 선택했으면 `ICashReceiptGateway.RequestIssueAsync`(KICC B1, 카드결제 VAN 연동과 동일한 `IKiccPosClient` 재사용)를 호출한 뒤 매출에 결과를 저장한다. 결제 완료(현금/카드1/카드2 공통) 후에는 `ReceiptDocument`를 구성해 영수증 미리보기 팝업을 띄운다. 실제 프린터 인쇄는 `IReceiptPrinter` 인터페이스+스텁만 만들고 하드웨어 확정 후 별도 작업으로 미룬다.

**Tech Stack:** .NET 8 / WPF (x86), CommunityToolkit.Mvvm(`[ObservableProperty]`/`[RelayCommand]`), Dapper + MySqlConnector, xUnit, KICC `KiccPos.dll` P/Invoke(기존 `IKiccPosClient` 재사용).

## Global Constraints

- 스펙 문서: `docs/superpowers/specs/2026-07-26-receipt-issuance-design.md` — 이번 플랜의 모든 태스크는 이 문서의 결정을 그대로 따른다.
- **현금영수증 B1/B2 전문 필드는 추정이 아니라 실사양이다** — 아래 각 태스크의 리터럴 문자열은 사용자가 제공한 KICC 모듈 API 문서(ED-721)와 실제 전문 샘플(xlsx)에서 그대로 가져온 것이다. 이 리터럴 값을 임의로 바꾸지 않는다.
- **고객 전화번호/사업자등록번호는 POS가 전송하지 않는다** — 카드단말기 자체 키패드로 입력받는다. POS 쪽에 전화번호/사업자번호 입력 텍스트박스를 만들지 않는다(정적 안내 문구만 표시).
- 현금결제 버튼 자체는 하나로 유지한다 — 현금결제1/2로 나누지 않는다. 가맹점(대원수산/대원낚시마트) 선택은 현금영수증 발행을 고를 때만 나타난다.
- 실제 프린터 인쇄는 이번 범위에서 구현하지 않는다 — `IReceiptPrinter`는 스텁만("프린터 연동은 지원 예정" 토스트).
- 현금영수증 취소(B2)는 게이트웨이 메서드 시그니처만 이번에 만들고, 이를 호출하는 화면(매출취소)은 다음 작업(D)에서 다룬다 — 이번 플랜에서 B2를 호출하는 UI를 만들지 않는다.
- 기존 오버레이 패턴(`Grid.RowSpan="2"` + `ModalOverlay` 배경 + 가운데 정렬 흰색 `Border`)과 기존 `DynamicResource` 색상만 재사용한다.
- `[RelayCommand(CanExecute = ...)]`와 XAML의 직접 `IsEnabled` 바인딩을 동시에 걸지 않는다(`InventoryViewModel`에서 발견된 CanExecute 캐시 미갱신 버그 재발 방지). 버튼 활성/비활성은 커맨드에 `CanExecute`를 걸지 않고 별도의 plain computed property를 `IsEnabled`에 직접 바인딩하거나, 커맨드 몸통 안에서 가드한다.
- `PosViewModel` 생성자에 새 의존성(`ICashReceiptGateway`, `IReceiptPrinter`, `IReceiptConfigRepository`)이 추가될 때마다 `App.xaml.cs`의 DI 등록/생성 코드와 두 테스트 파일(`tests/FishingMartPos.Tests/ViewModels/PosViewModelTests.cs`, `PosViewModelPaymentTests.cs`)의 `CreateViewModel` 헬퍼를 **반드시 함께** 갱신한다 — 하나라도 빠지면 컴파일이 깨진다.

---

## Task 1: `sales_header_tb` 현금영수증 컬럼 + `SaleHeader` 모델 + `SalesRepository`

**Files:**
- Create: `db/migrations/006_add_cash_receipt_columns.sql`
- Modify: `src/FishingMartPos/Models/SaleHeader.cs`
- Modify: `src/FishingMartPos/Repositories/SalesRepository.cs`
- Test: `tests/FishingMartPos.Tests/Repositories/SalesRepositoryTests.cs`

**Interfaces:**
- Produces: `SaleHeader.CashReceiptType`(`string`, 기본값 `"NONE"`), `SaleHeader.CashReceiptMerchant`(`string?`), `SaleHeader.CashReceiptApprovalNo`(`string?`) — Task 5에서 `PayAsync`가 이 필드들을 채운다.

- [ ] **Step 1: 마이그레이션 파일 작성**

`db/migrations/006_add_cash_receipt_columns.sql`:

```sql
-- 006_add_cash_receipt_columns.sql
-- 현금영수증 발행 여부/가맹점/승인번호를 매출 건별로 저장한다.
-- 실행: mysql -h <host> -u <user> -p <database> < 006_add_cash_receipt_columns.sql
-- 주의: 이 마이그레이션은 멱등(idempotent)하지 않다(005와 동일) — 재실행 시 "duplicate column" 에러가 나면
-- 이미 적용된 것이니 정상이다. 신규 실행에서만 사용한다.

ALTER TABLE sales_header_tb
    ADD COLUMN cash_receipt_type VARCHAR(10) NOT NULL DEFAULT 'NONE' AFTER installment_months, -- 'NONE'/'PERSONAL'/'BUSINESS'
    ADD COLUMN cash_receipt_merchant VARCHAR(10) NULL AFTER cash_receipt_type, -- 'CARD1'/'CARD2' — van_config_tb 조회 키
    ADD COLUMN cash_receipt_approval_no VARCHAR(40) NULL AFTER cash_receipt_merchant,
    ADD COLUMN cash_receipt_approval_date VARCHAR(6) NULL AFTER cash_receipt_approval_no; -- YYMMDD(KICC 응답 R07 앞 6자리) — 향후 B2 취소에 필요
```

- [ ] **Step 2: `SaleHeader`에 필드 추가**

`src/FishingMartPos/Models/SaleHeader.cs` 전체를 다음으로 교체:

```csharp
namespace FishingMartPos.Models;

public sealed class SaleHeader
{
    public required string PosCd { get; init; }
    public required DateTime SaleDt { get; init; }
    public required string StaffCd { get; init; }
    public required decimal TotalAmt { get; init; }
    public required string PayType { get; init; } // "CASH" / "CARD1" / "CARD2"
    public decimal? CashReceived { get; init; }
    public decimal? ChangeAmt { get; init; }
    public string? VanApprovalNo { get; init; }
    public string? VanCode { get; init; }
    public int InstallmentMonths { get; init; } // 0 = 일시불, 2/3/4/6/12 = 해당 개월, 그 외 양의 정수 = 기타개월
    public string CashReceiptType { get; init; } = "NONE"; // "NONE"/"PERSONAL"/"BUSINESS"
    public string? CashReceiptMerchant { get; init; } // "CARD1"/"CARD2" — 현금영수증을 어느 가맹점(van_config_tb 행)으로 등록했는지
    public string? CashReceiptApprovalNo { get; init; }
    public string? CashReceiptApprovalDate { get; init; } // YYMMDD — 향후 B2(현금영수증 취소)에 필요, 이번 범위에서는 저장만 한다
}
```

- [ ] **Step 3: 실패하는 테스트 작성 (DB 왕복 검증)**

`tests/FishingMartPos.Tests/Repositories/SalesRepositoryTests.cs`의 클래스 마지막 메서드 뒤에 추가:

```csharp
    [Fact]
    public async Task CreateSale_WithCashReceipt_RoundTripsThroughDb()
    {
        var config = AppConfig.Load(AppContext.BaseDirectory);
        var factory = new MySqlConnectionFactory(config);
        ISalesRepository repository = new SalesRepository(factory);

        const string barcode = "8800000020001";
        int stockBefore;
        using (var conn = await factory.CreateOpenConnectionAsync())
        {
            stockBefore = await conn.QuerySingleAsync<int>(
                "SELECT stock_qty FROM product_tb WHERE barcode = @Barcode", new { Barcode = barcode });
        }

        var header = new SaleHeader
        {
            PosCd = "1", SaleDt = DateTime.Now, StaffCd = "ADMIN1", TotalAmt = 5000,
            PayType = "CASH", CashReceived = 5000, ChangeAmt = 0,
            CashReceiptType = "PERSONAL", CashReceiptMerchant = "CARD1", CashReceiptApprovalNo = "149331691",
            CashReceiptApprovalDate = "250704",
        };
        var lines = new[]
        {
            new SaleDetailLine { Barcode = barcode, ProductName = "지렁이", Qty = 1, UnitPrice = 5000 },
        };

        long saleNo = await repository.CreateSaleAsync(header, lines);

        using var verifyConn = await factory.CreateOpenConnectionAsync();
        try
        {
            var saved = await verifyConn.QuerySingleAsync<(string CashReceiptType, string CashReceiptMerchant, string CashReceiptApprovalNo, string CashReceiptApprovalDate)>(
                "SELECT cash_receipt_type AS CashReceiptType, cash_receipt_merchant AS CashReceiptMerchant, cash_receipt_approval_no AS CashReceiptApprovalNo, cash_receipt_approval_date AS CashReceiptApprovalDate FROM sales_header_tb WHERE sale_no = @SaleNo",
                new { SaleNo = saleNo });
            Assert.Equal("PERSONAL", saved.CashReceiptType);
            Assert.Equal("CARD1", saved.CashReceiptMerchant);
            Assert.Equal("149331691", saved.CashReceiptApprovalNo);
            Assert.Equal("250704", saved.CashReceiptApprovalDate);

            var fromRepository = await repository.GetCompletedSalesAsync(DateTime.Today, DateTime.Today.AddDays(2));
            var matched = Assert.Single(fromRepository, s => s.CashReceiptApprovalNo == "149331691");
            Assert.Equal("PERSONAL", matched.CashReceiptType);
            Assert.Equal("CARD1", matched.CashReceiptMerchant);
            Assert.Equal("250704", matched.CashReceiptApprovalDate);
        }
        finally
        {
            await verifyConn.ExecuteAsync("DELETE FROM sales_detail_tb WHERE sale_no = @SaleNo", new { SaleNo = saleNo });
            await verifyConn.ExecuteAsync("DELETE FROM sales_header_tb WHERE sale_no = @SaleNo", new { SaleNo = saleNo });
            await verifyConn.ExecuteAsync(
                "UPDATE product_tb SET stock_qty = @Stock WHERE barcode = @Barcode",
                new { Stock = stockBefore, Barcode = barcode });
        }
    }

    [Fact]
    public async Task CreateSale_WithoutExplicitCashReceipt_DefaultsToNone()
    {
        var config = AppConfig.Load(AppContext.BaseDirectory);
        var factory = new MySqlConnectionFactory(config);
        ISalesRepository repository = new SalesRepository(factory);

        const string barcode = "8800000020001";
        int stockBefore;
        using (var conn = await factory.CreateOpenConnectionAsync())
        {
            stockBefore = await conn.QuerySingleAsync<int>(
                "SELECT stock_qty FROM product_tb WHERE barcode = @Barcode", new { Barcode = barcode });
        }

        var header = new SaleHeader
        {
            PosCd = "1", SaleDt = DateTime.Now, StaffCd = "ADMIN1", TotalAmt = 5000,
            PayType = "CASH", CashReceived = 5000, ChangeAmt = 0,
        };
        var lines = new[]
        {
            new SaleDetailLine { Barcode = barcode, ProductName = "지렁이", Qty = 1, UnitPrice = 5000 },
        };

        long saleNo = await repository.CreateSaleAsync(header, lines);

        using var verifyConn = await factory.CreateOpenConnectionAsync();
        try
        {
            string savedType = await verifyConn.QuerySingleAsync<string>(
                "SELECT cash_receipt_type FROM sales_header_tb WHERE sale_no = @SaleNo", new { SaleNo = saleNo });
            Assert.Equal("NONE", savedType);
        }
        finally
        {
            await verifyConn.ExecuteAsync("DELETE FROM sales_detail_tb WHERE sale_no = @SaleNo", new { SaleNo = saleNo });
            await verifyConn.ExecuteAsync("DELETE FROM sales_header_tb WHERE sale_no = @SaleNo", new { SaleNo = saleNo });
            await verifyConn.ExecuteAsync(
                "UPDATE product_tb SET stock_qty = @Stock WHERE barcode = @Barcode",
                new { Stock = stockBefore, Barcode = barcode });
        }
    }
```

- [ ] **Step 4: 테스트 실행 → 컬럼이 없어서 실패하는지 확인**

Run: `dotnet test tests/FishingMartPos.Tests --filter "FullyQualifiedName~SalesRepositoryTests.CreateSale_WithCashReceipt_RoundTripsThroughDb"`
Expected: FAIL — `Unknown column 'cash_receipt_type'` (마이그레이션 미적용) 또는 `SaleHeader`에 필드가 없어 컴파일 에러(Step 2까지만 했다면).

- [ ] **Step 5: `SalesRepository`의 insert/select문에 컬럼 추가**

`src/FishingMartPos/Repositories/SalesRepository.cs`의 `CreateSaleAsync` 안 `insertHeaderSql`을 교체:

```csharp
        const string insertHeaderSql = """
            INSERT INTO sales_header_tb
                (pos_cd, sale_dt, staff_cd, total_amt, pay_type, cash_received, change_amt, van_approval_no, van_code, installment_months, cash_receipt_type, cash_receipt_merchant, cash_receipt_approval_no, cash_receipt_approval_date, status)
            VALUES
                (@PosCd, @SaleDt, @StaffCd, @TotalAmt, @PayType, @CashReceived, @ChangeAmt, @VanApprovalNo, @VanCode, @InstallmentMonths, @CashReceiptType, @CashReceiptMerchant, @CashReceiptApprovalNo, @CashReceiptApprovalDate, 'COMPLETE')
            """;
```

같은 파일의 `GetCompletedSalesAsync`의 `sql`을 교체:

```csharp
        const string sql = """
            SELECT pos_cd AS PosCd, sale_dt AS SaleDt, staff_cd AS StaffCd, total_amt AS TotalAmt,
                   pay_type AS PayType, cash_received AS CashReceived, change_amt AS ChangeAmt,
                   van_approval_no AS VanApprovalNo, van_code AS VanCode, installment_months AS InstallmentMonths,
                   cash_receipt_type AS CashReceiptType, cash_receipt_merchant AS CashReceiptMerchant,
                   cash_receipt_approval_no AS CashReceiptApprovalNo, cash_receipt_approval_date AS CashReceiptApprovalDate
            FROM sales_header_tb
            WHERE sale_dt >= @From AND sale_dt < @To AND status = 'COMPLETE'
            """;
```

- [ ] **Step 6: 마이그레이션을 개발 DB에 적용**

`005_add_installment_months.sql` 적용 때와 동일한 방식(이 저장소에 `mysql` CLI 없음). 임시 테스트 파일을 만들어 한 번 실행 후 삭제한다.

`tests/FishingMartPos.Tests/_ThrowawayMigration006Test.cs` (임시 파일 — 실행 후 반드시 삭제, 커밋하지 않는다):

```csharp
using Dapper;
using FishingMartPos.Configuration;
using FishingMartPos.Data;
using Xunit;

namespace FishingMartPos.Tests;

public class ThrowawayMigration006Test
{
    [Fact]
    public async Task ApplyMigration006()
    {
        var config = AppConfig.Load(AppContext.BaseDirectory);
        var factory = new MySqlConnectionFactory(config);
        using var connection = await factory.CreateOpenConnectionAsync();

        int alreadyExists = await connection.QuerySingleAsync<int>(
            """
            SELECT COUNT(*) FROM information_schema.COLUMNS
            WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'sales_header_tb' AND COLUMN_NAME = 'cash_receipt_type'
            """);

        if (alreadyExists == 0)
        {
            await connection.ExecuteAsync(
                """
                ALTER TABLE sales_header_tb
                    ADD COLUMN cash_receipt_type VARCHAR(10) NOT NULL DEFAULT 'NONE' AFTER installment_months,
                    ADD COLUMN cash_receipt_merchant VARCHAR(10) NULL AFTER cash_receipt_type,
                    ADD COLUMN cash_receipt_approval_no VARCHAR(40) NULL AFTER cash_receipt_merchant,
                    ADD COLUMN cash_receipt_approval_date VARCHAR(6) NULL AFTER cash_receipt_approval_no
                """);
        }

        int afterExists = await connection.QuerySingleAsync<int>(
            """
            SELECT COUNT(*) FROM information_schema.COLUMNS
            WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'sales_header_tb' AND COLUMN_NAME IN ('cash_receipt_type','cash_receipt_merchant','cash_receipt_approval_no','cash_receipt_approval_date')
            """);
        Assert.Equal(4, afterExists);
    }
}
```

Run: `dotnet test tests/FishingMartPos.Tests --filter "FullyQualifiedName~ThrowawayMigration006Test"`
Expected: PASS. 통과 확인 후 `rm tests/FishingMartPos.Tests/_ThrowawayMigration006Test.cs`로 삭제(커밋하지 않음).

- [ ] **Step 7: Step 3의 테스트 재실행 → 통과 확인**

Run: `dotnet test tests/FishingMartPos.Tests --filter "FullyQualifiedName~SalesRepositoryTests.CreateSale_WithCashReceipt_RoundTripsThroughDb|FullyQualifiedName~SalesRepositoryTests.CreateSale_WithoutExplicitCashReceipt_DefaultsToNone"`
Expected: PASS (2 tests)

- [ ] **Step 8: 전체 테스트 실행 후 커밋**

Run: `dotnet test tests/FishingMartPos.Tests`
Expected: 0 실패 (기존 `SalesRepositoryTests`의 다른 메서드들은 `CashReceiptType`을 명시하지 않으므로 기본값 `"NONE"`으로 계속 통과해야 한다).

```bash
git add db/migrations/006_add_cash_receipt_columns.sql src/FishingMartPos/Models/SaleHeader.cs src/FishingMartPos/Repositories/SalesRepository.cs tests/FishingMartPos.Tests/Repositories/SalesRepositoryTests.cs
git commit -m "sales_header_tb에 현금영수증 컬럼 추가, SaleHeader/SalesRepository 반영"
```

---

## Task 2: `ICashReceiptGateway` 인터페이스 + `StubCashReceiptGateway`

**Files:**
- Create: `src/FishingMartPos/Services/ICashReceiptGateway.cs`
- Create: `src/FishingMartPos/Services/StubCashReceiptGateway.cs`
- Create: `tests/FishingMartPos.Tests/Fakes/FakeCashReceiptGateway.cs`
- Test: `tests/FishingMartPos.Tests/Services/StubCashReceiptGatewayTests.cs`
- Modify: `src/FishingMartPos/App.xaml.cs`

**Interfaces:**
- Produces: `ICashReceiptGateway.RequestIssueAsync(CashReceiptRequest)`, `CashReceiptRequest(string MerchantPayType, string ReceiptType, decimal Amount)`, `CashReceiptResult{ IsIssued, ApprovalNo, ApprovalDateYyMmDd, ResponseMessage }` — Task 3(`KiccCashReceiptGateway`)과 Task 5(`PosViewModel` 연동)가 이 계약을 사용한다.

- [ ] **Step 1: `ICashReceiptGateway.cs` 작성** (기존 `IVanPaymentGateway.cs`와 동일한 스타일 — 인터페이스+요청/응답을 한 파일에)

```csharp
namespace FishingMartPos.Services;

public interface ICashReceiptGateway
{
    Task<CashReceiptResult> RequestIssueAsync(CashReceiptRequest request);
}

public sealed record CashReceiptRequest(string MerchantPayType, string ReceiptType, decimal Amount);
// MerchantPayType: "CARD1"/"CARD2" — van_config_tb 조회 키(카드결제와 동일한 가맹점 TID를 재사용).
// ReceiptType: "PERSONAL"(개인 소득공제용) / "BUSINESS"(사업자 지출증빙용).

public sealed class CashReceiptResult
{
    public required bool IsIssued { get; init; }
    public string? ApprovalNo { get; init; }           // 발급 성공 시 승인번호(KICC 응답 R09)
    public string? ApprovalDateYyMmDd { get; init; }   // 발급 성공 시 YYMMDD(KICC 응답 R07 앞 6자리) — 향후 B2 취소에 필요, 지금은 저장만
    public required string ResponseMessage { get; init; }
}
```

- [ ] **Step 2: 실패하는 테스트 작성 — `StubCashReceiptGateway`**

`tests/FishingMartPos.Tests/Services/StubCashReceiptGatewayTests.cs` (신규 파일, 기존 `StubVanPaymentGatewayTests.cs`가 있다면 그 스타일을 참고 — 없다면 아래 그대로):

```csharp
using FishingMartPos.Services;
using FishingMartPos.Tests.Fakes;
using Xunit;

namespace FishingMartPos.Tests.Services;

public class StubCashReceiptGatewayTests
{
    [Fact]
    public async Task RequestIssueAsync_WhenOutcomeProviderApproves_ReturnsIssuedWithApprovalNo()
    {
        var outcome = new FakeVanOutcomeProvider(isApproved: true);
        var gateway = new StubCashReceiptGateway(new FakeDelayProvider(), outcome);

        var result = await gateway.RequestIssueAsync(new CashReceiptRequest("CARD1", "PERSONAL", 5000m));

        Assert.True(result.IsIssued);
        Assert.NotNull(result.ApprovalNo);
        Assert.NotNull(result.ApprovalDateYyMmDd);
    }

    [Fact]
    public async Task RequestIssueAsync_WhenOutcomeProviderDeclines_ReturnsNotIssuedWithNullApprovalNo()
    {
        var outcome = new FakeVanOutcomeProvider(isApproved: false);
        var gateway = new StubCashReceiptGateway(new FakeDelayProvider(), outcome);

        var result = await gateway.RequestIssueAsync(new CashReceiptRequest("CARD1", "BUSINESS", 5000m));

        Assert.False(result.IsIssued);
        Assert.Null(result.ApprovalNo);
        Assert.False(string.IsNullOrEmpty(result.ResponseMessage));
    }
}
```

`tests/FishingMartPos.Tests/Fakes/FakeVanOutcomeProvider.cs` **already exists** in this repo (used by the existing `StubVanPaymentGatewayTests.cs`) with constructor `FakeVanOutcomeProvider(bool isApproved)`. Reuse it exactly as-is — do not create a new file, do not rename its constructor parameter.

- [ ] **Step 3: 테스트 실행 → 컴파일 에러로 실패 확인**

Run: `dotnet test tests/FishingMartPos.Tests --filter "FullyQualifiedName~StubCashReceiptGatewayTests"`
Expected: FAIL(`StubCashReceiptGateway` 없어서 컴파일 에러)

- [ ] **Step 4: `StubCashReceiptGateway` 구현** (기존 `StubVanPaymentGateway`와 동일한 `IDelayProvider`+`IVanOutcomeProvider` 재사용 패턴)

```csharp
namespace FishingMartPos.Services;

public sealed class StubCashReceiptGateway : ICashReceiptGateway
{
    private static readonly string[] DeclineMessages =
    {
        "현금영수증 발급 실패", "단말기 통신 오류", "응답 시간 초과",
    };

    private readonly IDelayProvider _delay;
    private readonly IVanOutcomeProvider _outcomeProvider;
    private readonly Random _messageRandom = new();

    public StubCashReceiptGateway(IDelayProvider delay, IVanOutcomeProvider outcomeProvider)
    {
        _delay = delay;
        _outcomeProvider = outcomeProvider;
    }

    public async Task<CashReceiptResult> RequestIssueAsync(CashReceiptRequest request)
    {
        await _delay.Delay(TimeSpan.FromMilliseconds(1500));

        if (_outcomeProvider.NextIsApproved())
        {
            return new CashReceiptResult
            {
                IsIssued = true,
                ApprovalNo = DateTime.Now.ToString("yyyyMMddHHmmss"),
                ApprovalDateYyMmDd = DateTime.Now.ToString("yyMMdd"),
                ResponseMessage = "현금영수증 발급 완료",
            };
        }

        return new CashReceiptResult
        {
            IsIssued = false,
            ApprovalNo = null,
            ApprovalDateYyMmDd = null,
            ResponseMessage = DeclineMessages[_messageRandom.Next(DeclineMessages.Length)],
        };
    }
}
```

- [ ] **Step 5: 테스트 실행 → 통과 확인**

Run: `dotnet test tests/FishingMartPos.Tests --filter "FullyQualifiedName~StubCashReceiptGatewayTests"`
Expected: PASS (2 tests)

- [ ] **Step 6: `FakeCashReceiptGateway` 테스트 더블 작성** (Task 5의 `PosViewModel` 테스트에서 사용 — 기존 `FakeVanPaymentGateway.cs`와 동일한 스타일)

`tests/FishingMartPos.Tests/Fakes/FakeCashReceiptGateway.cs`:

```csharp
using FishingMartPos.Services;

namespace FishingMartPos.Tests.Fakes;

public sealed class FakeCashReceiptGateway : ICashReceiptGateway
{
    private readonly CashReceiptResult _result;
    public List<CashReceiptRequest> Requests { get; } = new();

    public FakeCashReceiptGateway(CashReceiptResult result)
    {
        _result = result;
    }

    public Task<CashReceiptResult> RequestIssueAsync(CashReceiptRequest request)
    {
        Requests.Add(request);
        return Task.FromResult(_result);
    }
}
```

이 파일은 새 테스트 클래스가 없으므로 실행할 실패/통과 테스트가 없다 — 컴파일만 확인한다: `dotnet build FishingMartPos.sln` → 0 경고/0 오류.

- [ ] **Step 7: DI 등록** (`App.xaml.cs`)

`services.AddSingleton<IVanPaymentGateway, StubVanPaymentGateway>();` 줄(46번째 근처) 바로 뒤에 추가:

```csharp
        services.AddSingleton<ICashReceiptGateway, StubCashReceiptGateway>();
```

- [ ] **Step 8: 전체 테스트 실행 후 커밋**

Run: `dotnet test tests/FishingMartPos.Tests`
Expected: 0 실패

```bash
git add src/FishingMartPos/Services/ICashReceiptGateway.cs src/FishingMartPos/Services/StubCashReceiptGateway.cs src/FishingMartPos/App.xaml.cs tests/FishingMartPos.Tests/Services/StubCashReceiptGatewayTests.cs tests/FishingMartPos.Tests/Fakes/FakeCashReceiptGateway.cs
git commit -m "ICashReceiptGateway 인터페이스 + StubCashReceiptGateway 추가 (현금영수증 발급 스텁)"
```

---

## Task 3: `KiccMessageBuilder` B1/B2 + `KiccCashReceiptGateway` (실제 KICC 연동)

**Files:**
- Modify: `src/FishingMartPos/Services/Kicc/KiccMessageBuilder.cs`
- Create: `src/FishingMartPos/Services/Kicc/KiccCashReceiptGateway.cs`
- Test: `tests/FishingMartPos.Tests/Services/Kicc/KiccMessageBuilderTests.cs`
- Test: `tests/FishingMartPos.Tests/Services/Kicc/KiccCashReceiptGatewayTests.cs`
- Modify: `src/FishingMartPos/App.xaml.cs`

**Interfaces:**
- Consumes: `ICashReceiptGateway`(Task 2), 기존 `IKiccPosClient`/`KiccMerchantConfig`/`KiccResponseParser`(이미 존재, 카드결제 VAN 연동에서 사용 중).
- Produces: `KiccMessageBuilder.BuildCashReceiptIssueRequest(merchant, amount, receiptType, posTranNo)` / `BuildCashReceiptCancelRequest(merchant, amount, receiptType, originalApprovalNo, originalApprovalDateYyMmDd, posTranNo)`.

- [ ] **Step 1: 실패하는 테스트 작성 — `KiccMessageBuilder`의 B1/B2**

`tests/FishingMartPos.Tests/Services/Kicc/KiccMessageBuilderTests.cs`의 클래스 마지막(`BuildApprovalRequest_InstallmentMonths_FormatsS09AsTwoDigitString` 뒤)에 추가. 아래 리터럴 문자열은 사용자가 제공한 KICC 실제 전문 샘플(xlsx)에서 그대로 가져온 값이다 — 필드 순서/값을 임의로 바꾸지 않는다:

```csharp
    [Fact]
    public void BuildCashReceiptIssueRequest_Personal_MatchesKiccDocSample()
    {
        var merchant = new KiccMerchantConfig("CARD1", "0788888", "1234567890");

        var sendData = KiccMessageBuilder.BuildCashReceiptIssueRequest(merchant, 1004m, "PERSONAL", "20250704132431000000");

        Assert.Equal("S00=002;S01=B1;S02=40;S03=0788888;S09=00;S10=1004;S11=00;S15=0;S16=91;S23=20250704132431000000;", sendData);
    }

    [Fact]
    public void BuildCashReceiptIssueRequest_Business_UsesS11Code01()
    {
        var merchant = new KiccMerchantConfig("CARD1", "0788888", "1234567890");

        var sendData = KiccMessageBuilder.BuildCashReceiptIssueRequest(merchant, 1004m, "BUSINESS", "20250704132431000000");

        Assert.Contains("S11=01;", sendData);
    }

    [Fact]
    public void BuildCashReceiptCancelRequest_MatchesKiccDocSample()
    {
        var merchant = new KiccMerchantConfig("CARD1", "0788888", "1234567890");

        var sendData = KiccMessageBuilder.BuildCashReceiptCancelRequest(merchant, 1004m, "PERSONAL", "149331691", "250704", "20250704132431000000");

        Assert.Equal("S00=002;S01=B2;S02=40;S03=0788888;S09=00;S10=1004;S11=00;S12=149331691;S13=250704;S15=0;S16=91;S23=20250704132431000000;", sendData);
    }
```

- [ ] **Step 2: 테스트 실행 → 컴파일 에러로 실패 확인**

Run: `dotnet test tests/FishingMartPos.Tests --filter "FullyQualifiedName~KiccMessageBuilderTests"`
Expected: FAIL(컴파일 에러 — `BuildCashReceiptIssueRequest`/`BuildCashReceiptCancelRequest` 없음)

- [ ] **Step 3: `KiccMessageBuilder`에 B1/B2 빌더 추가**

`src/FishingMartPos/Services/Kicc/KiccMessageBuilder.cs` 전체를 아래로 교체(기존 `BuildApprovalRequest`는 그대로 유지, 새 메서드 2개 추가):

```csharp
namespace FishingMartPos.Services.Kicc;

public static class KiccMessageBuilder
{
    public static string BuildApprovalRequest(KiccMerchantConfig merchant, decimal amount, string posTranNo, int installmentMonths = 0)
    {
        var vat = (int)Math.Round(amount / 11m, MidpointRounding.AwayFromZero);
        string installmentCode = installmentMonths.ToString("00");
        return $"S00=002;S01=D1;S02=40;S03={merchant.Tid};S04={merchant.BusinessNo};" +
               $"S09={installmentCode};S10={(int)amount};S15=0;S16={vat};S23={posTranNo};";
    }

    public static string BuildCashReceiptIssueRequest(KiccMerchantConfig merchant, decimal amount, string receiptType, string posTranNo)
    {
        var vat = (int)Math.Round(amount / 11m, MidpointRounding.AwayFromZero);
        string receiptTypeCode = ReceiptTypeCode(receiptType);
        return $"S00=002;S01=B1;S02=40;S03={merchant.Tid};" +
               $"S09=00;S10={(int)amount};S11={receiptTypeCode};S15=0;S16={vat};S23={posTranNo};";
    }

    public static string BuildCashReceiptCancelRequest(
        KiccMerchantConfig merchant, decimal amount, string receiptType,
        string originalApprovalNo, string originalApprovalDateYyMmDd, string posTranNo)
    {
        var vat = (int)Math.Round(amount / 11m, MidpointRounding.AwayFromZero);
        string receiptTypeCode = ReceiptTypeCode(receiptType);
        return $"S00=002;S01=B2;S02=40;S03={merchant.Tid};" +
               $"S09=00;S10={(int)amount};S11={receiptTypeCode};S12={originalApprovalNo};S13={originalApprovalDateYyMmDd};S15=0;S16={vat};S23={posTranNo};";
    }

    private static string ReceiptTypeCode(string receiptType) => receiptType switch
    {
        "PERSONAL" => "00",
        "BUSINESS" => "01",
        _ => throw new ArgumentOutOfRangeException(nameof(receiptType), receiptType, "PERSONAL 또는 BUSINESS만 지원합니다"),
    };
}
```

- [ ] **Step 4: 테스트 실행 → 통과 확인**

Run: `dotnet test tests/FishingMartPos.Tests --filter "FullyQualifiedName~KiccMessageBuilderTests"`
Expected: PASS (9 tests)

- [ ] **Step 5: 실패하는 테스트 작성 — `KiccCashReceiptGateway`**

`tests/FishingMartPos.Tests/Services/Kicc/KiccCashReceiptGatewayTests.cs` (신규 파일, 기존 `KiccVanPaymentGatewayTests.cs`와 동일한 `Merchants` 딕셔너리/`FakeKiccPosClient` 패턴 재사용):

```csharp
using FishingMartPos.Services;
using FishingMartPos.Services.Kicc;
using FishingMartPos.Tests.Fakes;
using Xunit;

namespace FishingMartPos.Tests.Services.Kicc;

public class KiccCashReceiptGatewayTests
{
    private static readonly Dictionary<string, KiccMerchantConfig> Merchants = new()
    {
        ["CARD1"] = new KiccMerchantConfig("CARD1", "2977338", "3169055788"),
        ["CARD2"] = new KiccMerchantConfig("CARD2", "2977340", "3160326930"),
    };

    [Fact]
    public async Task RequestIssueAsync_WhenR04IsSuccess_ReturnsIssuedWithApprovalNoAndDate()
    {
        var client = new FakeKiccPosClient(KiccRawResponse.Success("R04=0000;R07=2507041324215;R09=149331691   ;"));
        var gateway = new KiccCashReceiptGateway(client, Merchants);

        var result = await gateway.RequestIssueAsync(new CashReceiptRequest("CARD1", "PERSONAL", 1004m));

        Assert.True(result.IsIssued);
        Assert.Equal("149331691   ", result.ApprovalNo);
        Assert.Equal("250704", result.ApprovalDateYyMmDd);
    }

    [Fact]
    public async Task RequestIssueAsync_WhenR04IsNotSuccess_ReturnsNotIssued()
    {
        var client = new FakeKiccPosClient(KiccRawResponse.Success("R04=1234;"));
        var gateway = new KiccCashReceiptGateway(client, Merchants);

        var result = await gateway.RequestIssueAsync(new CashReceiptRequest("CARD1", "PERSONAL", 1004m));

        Assert.False(result.IsIssued);
        Assert.Null(result.ApprovalNo);
    }

    [Fact]
    public async Task RequestIssueAsync_WhenClientReportsFailure_ReturnsNotIssuedWithFailureMessage()
    {
        var client = new FakeKiccPosClient(KiccRawResponse.Failure("응답 시간 초과"));
        var gateway = new KiccCashReceiptGateway(client, Merchants);

        var result = await gateway.RequestIssueAsync(new CashReceiptRequest("CARD1", "PERSONAL", 1004m));

        Assert.False(result.IsIssued);
        Assert.Equal("응답 시간 초과", result.ResponseMessage);
    }

    [Fact]
    public async Task RequestIssueAsync_Card1_SendsDaewonSusanMerchantFieldsAndB1Command()
    {
        var client = new FakeKiccPosClient(KiccRawResponse.Success("R04=0000;R07=2507041324215;R09=1;"));
        var gateway = new KiccCashReceiptGateway(client, Merchants);

        await gateway.RequestIssueAsync(new CashReceiptRequest("CARD1", "BUSINESS", 5000m));

        var req = Assert.Single(client.Requests);
        Assert.Equal(0xFB, req.Cmd);
        Assert.Equal(0x14, req.Gcd);
        Assert.Equal(0x04, req.Jcd);
        Assert.Contains("S01=B1;", req.SendData);
        Assert.Contains("S03=2977338;", req.SendData);
        Assert.Contains("S11=01;", req.SendData);
    }

    [Fact]
    public async Task RequestIssueAsync_Card2_SendsDaewonNakssiMartMerchantFields()
    {
        var client = new FakeKiccPosClient(KiccRawResponse.Success("R04=0000;R07=2507041324215;R09=1;"));
        var gateway = new KiccCashReceiptGateway(client, Merchants);

        await gateway.RequestIssueAsync(new CashReceiptRequest("CARD2", "PERSONAL", 5000m));

        var sendData = Assert.Single(client.Requests).SendData;
        Assert.Contains("S03=2977340;", sendData);
    }
}
```

- [ ] **Step 6: 테스트 실행 → 컴파일 에러로 실패 확인**

Run: `dotnet test tests/FishingMartPos.Tests --filter "FullyQualifiedName~KiccCashReceiptGatewayTests"`
Expected: FAIL(`KiccCashReceiptGateway` 없어서 컴파일 에러)

- [ ] **Step 7: `KiccCashReceiptGateway` 구현** (기존 `KiccVanPaymentGateway`와 동일한 구조 — `KiccResponseParser.Parse()` 그대로 재사용, R07 앞 6자리를 날짜로 추출)

```csharp
using FishingMartPos.Services;

namespace FishingMartPos.Services.Kicc;

public sealed class KiccCashReceiptGateway : ICashReceiptGateway
{
    private readonly IKiccPosClient _client;
    private readonly IReadOnlyDictionary<string, KiccMerchantConfig> _merchantsByPayType;

    public KiccCashReceiptGateway(IKiccPosClient client, IReadOnlyDictionary<string, KiccMerchantConfig> merchantsByPayType)
    {
        _client = client;
        _merchantsByPayType = merchantsByPayType;
    }

    public async Task<CashReceiptResult> RequestIssueAsync(CashReceiptRequest request)
    {
        var merchant = _merchantsByPayType[request.MerchantPayType];
        var posTranNo = BuildPosTranNo(merchant.Tid);
        var sendData = KiccMessageBuilder.BuildCashReceiptIssueRequest(merchant, request.Amount, request.ReceiptType, posTranNo);

        var raw = await _client.RequestAsync(0xFB, 0x14, 0x04, sendData);

        if (!raw.IsSuccess)
        {
            return new CashReceiptResult { IsIssued = false, ResponseMessage = raw.FailureMessage ?? "현금영수증 발급 거절" };
        }

        var fields = KiccResponseParser.Parse(raw.Data!);
        if (fields.TryGetValue("R04", out var rc) && rc == "0000")
        {
            string? approvalDateTime = fields.GetValueOrDefault("R07"); // YYMMDDhhmmssN
            return new CashReceiptResult
            {
                IsIssued = true,
                ApprovalNo = fields.GetValueOrDefault("R09"),
                ApprovalDateYyMmDd = approvalDateTime is { Length: >= 6 } ? approvalDateTime[..6] : null,
                ResponseMessage = "현금영수증 발급 완료",
            };
        }

        return new CashReceiptResult { IsIssued = false, ResponseMessage = "현금영수증 발급 거절" };
    }

    private static string BuildPosTranNo(string tid) =>
        $"{tid}{DateTime.Now:yyMMddHHmmss}{Random.Shared.Next(10, 99)}";
}
```

**주의**: `BuildPosTranNo`는 기존 `KiccVanPaymentGateway.BuildPosTranNo`와 매개변수가 다르다(카드결제는 `posCode` 기준, 이건 `merchant.Tid` 기준) — POS 거래번호가 카드 승인 거래와 겹치지 않도록 접두어를 다르게 가져간다. 두 클래스의 `BuildPosTranNo`를 공유 유틸로 합치지 않는다(각자 private, 의도적으로 독립 — `KiccVanPaymentGateway`를 수정하지 않는다).

- [ ] **Step 8: 테스트 실행 → 통과 확인**

Run: `dotnet test tests/FishingMartPos.Tests --filter "FullyQualifiedName~KiccCashReceiptGatewayTests"`
Expected: PASS (5 tests)

**참고**: `App.xaml.cs`에서 실제 KICC 연동 시 `KiccCashReceiptGateway`로 교체하는 배선은 이번 태스크에서 하지 않는다 — `PosViewModel` 생성자가 아직 `ICashReceiptGateway`를 받지 않으므로 지금 배선해도 쓰이는 곳이 없어 "할당되었지만 사용되지 않음" 경고만 남긴다. `App.xaml.cs` 변경은 Task 5 Step 6에서 생성자 매개변수 추가와 함께 한 번에 반영한다(0 경고 유지).

- [ ] **Step 9: 전체 테스트 실행 후 커밋**

Run: `dotnet test tests/FishingMartPos.Tests`
Expected: 0 실패

Run: `dotnet build FishingMartPos.sln`
Expected: 0 경고 / 0 오류

```bash
git add src/FishingMartPos/Services/Kicc/KiccMessageBuilder.cs src/FishingMartPos/Services/Kicc/KiccCashReceiptGateway.cs tests/FishingMartPos.Tests/Services/Kicc/KiccMessageBuilderTests.cs tests/FishingMartPos.Tests/Services/Kicc/KiccCashReceiptGatewayTests.cs
git commit -m "KiccMessageBuilder에 현금영수증 B1/B2 전문 빌더 추가, KiccCashReceiptGateway 구현 (실제 KICC 연동)"
```

---

## Task 4: `PosViewModel` 현금영수증 선택 상태 (게이트웨이 연동 전)

**Files:**
- Modify: `src/FishingMartPos/ViewModels/PosViewModel.cs`
- Test: `tests/FishingMartPos.Tests/ViewModels/PosViewModelTests.cs`

**Interfaces:**
- Consumes: (없음 — 순수 상태)
- Produces: `string SelectedCashReceiptType`(기본 `"NONE"`) / `string? SelectedCashReceiptMerchant` / `bool CanConfirmCashPayment` / `IRelayCommand<string> SelectCashReceiptTypeCommand` / `IRelayCommand<string> SelectCashReceiptMerchantCommand` — Task 5가 `ConfirmCashPayment`에서 이 값들을 읽고, Task 6의 XAML이 바인딩한다.

- [ ] **Step 1: 실패하는 테스트 작성**

`tests/FishingMartPos.Tests/ViewModels/PosViewModelTests.cs`의 클래스 마지막(`PayCard1_OpeningPopupTwice_ResetsInstallmentSelectionEachTime` 뒤)에 추가:

```csharp
    [Fact]
    public void SelectedCashReceiptType_DefaultsToNone()
    {
        var vm = CreateViewModel(out _, out _);

        Assert.Equal("NONE", vm.SelectedCashReceiptType);
        Assert.Null(vm.SelectedCashReceiptMerchant);
        Assert.True(vm.CanConfirmCashPayment);
    }

    [Fact]
    public void SelectCashReceiptType_ToPersonal_RequiresMerchantBeforeConfirming()
    {
        var vm = CreateViewModel(out _, out _);

        vm.SelectCashReceiptTypeCommand.Execute("PERSONAL");

        Assert.Equal("PERSONAL", vm.SelectedCashReceiptType);
        Assert.Null(vm.SelectedCashReceiptMerchant);
        Assert.False(vm.CanConfirmCashPayment);
    }

    [Fact]
    public void SelectCashReceiptMerchant_AfterSelectingType_EnablesConfirm()
    {
        var vm = CreateViewModel(out _, out _);
        vm.SelectCashReceiptTypeCommand.Execute("BUSINESS");

        vm.SelectCashReceiptMerchantCommand.Execute("CARD2");

        Assert.Equal("CARD2", vm.SelectedCashReceiptMerchant);
        Assert.True(vm.CanConfirmCashPayment);
    }

    [Fact]
    public void SelectCashReceiptType_BackToNone_ClearsMerchantAndReEnablesConfirm()
    {
        var vm = CreateViewModel(out _, out _);
        vm.SelectCashReceiptTypeCommand.Execute("PERSONAL");
        vm.SelectCashReceiptMerchantCommand.Execute("CARD1");

        vm.SelectCashReceiptTypeCommand.Execute("NONE");

        Assert.Equal("NONE", vm.SelectedCashReceiptType);
        Assert.Null(vm.SelectedCashReceiptMerchant);
        Assert.True(vm.CanConfirmCashPayment);
    }
```

- [ ] **Step 2: 테스트 실행 → 컴파일 에러로 실패 확인**

Run: `dotnet test tests/FishingMartPos.Tests --filter "FullyQualifiedName~PosViewModelTests"`
Expected: FAIL(컴파일 에러 — `SelectedCashReceiptType`/`SelectCashReceiptTypeCommand` 등이 아직 없음)

- [ ] **Step 3: `PosViewModel`에 현금영수증 선택 상태/커맨드 추가**

`_customInstallmentMonthsText` 필드 선언(82번째 줄 근처) 바로 뒤에 추가:

```csharp

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanConfirmCashPayment))]
    private string _selectedCashReceiptType = "NONE";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanConfirmCashPayment))]
    private string? _selectedCashReceiptMerchant;
```

`IsInstallmentEligible` 프로퍼티(119번째 줄 근처) 바로 뒤에 추가:

```csharp
    public bool CanConfirmCashPayment => SelectedCashReceiptType == "NONE" || SelectedCashReceiptMerchant is not null;
```

`RefreshInstallmentOptions` 메서드 뒤(파일 끝부분, `BuildDetailLines` 바로 앞)에 추가:

```csharp
    [RelayCommand]
    private void SelectCashReceiptType(string type)
    {
        SelectedCashReceiptType = type;
        if (type == "NONE")
        {
            SelectedCashReceiptMerchant = null;
        }
    }

    [RelayCommand]
    private void SelectCashReceiptMerchant(string merchant)
    {
        SelectedCashReceiptMerchant = merchant;
    }
```

- [ ] **Step 4: 테스트 실행 → 통과 확인**

Run: `dotnet test tests/FishingMartPos.Tests --filter "FullyQualifiedName~PosViewModelTests"`
Expected: PASS

- [ ] **Step 5: 커밋**

```bash
git add src/FishingMartPos/ViewModels/PosViewModel.cs tests/FishingMartPos.Tests/ViewModels/PosViewModelTests.cs
git commit -m "PosViewModel에 현금영수증 선택 상태 추가 (팝업 연동 전 단계)"
```

---

## Task 5: 현금영수증 발급을 `ConfirmCashPayment` 흐름에 연동

**Files:**
- Modify: `src/FishingMartPos/ViewModels/PosViewModel.cs`
- Modify: `src/FishingMartPos/App.xaml.cs`
- Modify: `tests/FishingMartPos.Tests/ViewModels/PosViewModelTests.cs`
- Modify: `tests/FishingMartPos.Tests/ViewModels/PosViewModelPaymentTests.cs`

**Interfaces:**
- Consumes: `ICashReceiptGateway.RequestIssueAsync`(Task 2/3), `SaleHeader.CashReceiptType/Merchant/ApprovalNo`(Task 1), `SelectedCashReceiptType`/`SelectedCashReceiptMerchant`(Task 4)
- Produces: `bool IsCashReceiptRequestInProgress` / `PosViewModel` 생성자에 `ICashReceiptGateway cashReceiptGateway` 매개변수 추가 — Task 6의 XAML이 `IsCashReceiptRequestInProgress`를 바인딩한다.

- [ ] **Step 1: `PosViewModel` 생성자에 `ICashReceiptGateway` 의존성 추가**

`src/FishingMartPos/ViewModels/PosViewModel.cs`의 필드 선언부(24번째 줄 `private readonly IVanPaymentGateway _vanGateway;` 바로 뒤)에 추가:

```csharp
    private readonly ICashReceiptGateway _cashReceiptGateway;
```

생성자 매개변수 목록(`IVanPaymentGateway vanGateway,` 바로 뒤, `IKiccPosClient? kiccPosClient = null` 앞)에 추가:

```csharp
        ICashReceiptGateway cashReceiptGateway,
```

생성자 본문(`_vanGateway = vanGateway;` 바로 뒤)에 추가:

```csharp
        _cashReceiptGateway = cashReceiptGateway;
```

`IsCashPaymentVisible`/`IsCardApprovalInProgress` 선언(68-72번째 줄 근처) 바로 뒤에 추가:

```csharp
    [ObservableProperty]
    private bool _isCashReceiptRequestInProgress;
```

- [ ] **Step 2: `ConfirmCashPayment`/`PayAsync`/`PayCash` 갱신**

`PayCash` 메서드(310-324번째 줄 근처) — `IsCashConfirmVisible = true;` 바로 앞에 추가(팝업을 열 때마다 이전 선택을 초기화):

```csharp
        SelectedCashReceiptType = "NONE";
        SelectedCashReceiptMerchant = null;
```

기존 `ConfirmCashPayment` 메서드 전체를 아래로 **교체**:

```csharp
    [RelayCommand]
    private async Task ConfirmCashPayment()
    {
        if (SelectedCashReceiptType != "NONE")
        {
            if (SelectedCashReceiptMerchant is not string merchant) return;

            IsCashReceiptRequestInProgress = true;
            var result = await _cashReceiptGateway.RequestIssueAsync(
                new CashReceiptRequest(merchant, SelectedCashReceiptType, _cart.Total));
            IsCashReceiptRequestInProgress = false;

            if (!result.IsIssued)
            {
                IsToastWarning = true;
                ToastMessage = result.ResponseMessage;
                await _delay.Delay(TimeSpan.FromMilliseconds(1200));
                ToastMessage = null;
                return;
            }

            _pendingCashReceiptResult = result;
        }

        IsCashConfirmVisible = false;
        await PayAsync("CASH", "현금 결제 완료");
    }
```

`CancelCardPayment` 위쪽, 기존 `_pendingCardPayType` 필드 선언 근처(또는 `ConfirmCashPayment` 바로 위)에 새 필드 추가:

```csharp
    private CashReceiptResult? _pendingCashReceiptResult;
```

기존 `PayAsync` 메서드 전체를 아래로 **교체**:

```csharp
    private async Task PayAsync(string payType, string toastLabel)
    {
        if (!CanPay) return;
        if (_cart.Lines.Count == 0)
        {
            IsToastWarning = true;
            ToastMessage = "결제할 항목이 존재하지 않습니다";
            await _delay.Delay(TimeSpan.FromMilliseconds(1200));
            ToastMessage = null;
            return;
        }

        var cashReceiptResult = _pendingCashReceiptResult;
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
            CashReceiptType = payType == "CASH" ? SelectedCashReceiptType : "NONE",
            CashReceiptMerchant = payType == "CASH" ? SelectedCashReceiptMerchant : null,
            CashReceiptApprovalNo = cashReceiptResult?.ApprovalNo,
            CashReceiptApprovalDate = cashReceiptResult?.ApprovalDateYyMmDd,
        };

        await _salesRepository.CreateSaleAsync(header, BuildDetailLines());

        _pendingCashReceiptResult = null;
        IsToastWarning = false;
        ToastMessage = toastLabel;
        await _delay.Delay(TimeSpan.FromMilliseconds(1200));
        ToastMessage = null;
        ResetOrder();
    }
```

**중요**: `PayAsync`는 지금도 `ConfirmCashPayment`에서만 호출된다(카드결제는 별도의 `RequestCardApproval`을 쓴다) — 이 사실이 바뀌지 않았는지 `grep -n "PayAsync(" src/FishingMartPos/ViewModels/PosViewModel.cs`로 확인하고, 여전히 호출부가 하나뿐이면 위 교체가 안전하다. 둘 이상이면 STOP하고 보고한다(이 태스크의 전제가 깨진 것이므로 임의로 진행하지 않는다).

- [ ] **Step 3: 실패하는 테스트 작성 (게이트웨이 연동)**

`tests/FishingMartPos.Tests/ViewModels/PosViewModelPaymentTests.cs`의 `CreateViewModel` 헬퍼(17-42번째 줄)를 아래로 교체(새 매개변수 추가, 기본값은 항상 발급 성공하는 페이크):

```csharp
    private static PosViewModel CreateViewModel(
        out FakeSalesRepository sales,
        out FakeHeldOrderRepository held,
        FakeCashReceiptGateway? cashReceiptGateway = null)
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
            }),
            cashReceiptGateway ?? new FakeCashReceiptGateway(new CashReceiptResult
            {
                IsIssued = true,
                ApprovalNo = "149331691",
                ApprovalDateYyMmDd = "250704",
                ResponseMessage = "현금영수증 발급 완료",
            }));
    }
```

클래스 마지막에 새 테스트 4개 추가:

```csharp
    [Fact]
    public async Task ConfirmCashPayment_WithNoCashReceiptSelected_DoesNotCallGateway()
    {
        var cashReceiptGateway = new FakeCashReceiptGateway(new CashReceiptResult
        {
            IsIssued = true, ApprovalNo = "1", ApprovalDateYyMmDd = "250704", ResponseMessage = "발급 완료",
        });
        var vm = CreateViewModel(out var sales, out _, cashReceiptGateway);
        await vm.LoadAsync();
        vm.VisibleProducts[0].AddCommand.Execute(null);
        await vm.PayCashCommand.ExecuteAsync(null);

        await vm.ConfirmCashPaymentCommand.ExecuteAsync(null);

        Assert.Empty(cashReceiptGateway.Requests);
        var sale = Assert.Single(sales.CreatedSales);
        Assert.Equal("NONE", sale.Header.CashReceiptType);
        Assert.Null(sale.Header.CashReceiptApprovalNo);
    }

    [Fact]
    public async Task ConfirmCashPayment_WithPersonalReceiptSelected_CallsGatewayAndSavesApprovalNo()
    {
        var cashReceiptGateway = new FakeCashReceiptGateway(new CashReceiptResult
        {
            IsIssued = true, ApprovalNo = "149331691", ApprovalDateYyMmDd = "250704", ResponseMessage = "현금영수증 발급 완료",
        });
        var vm = CreateViewModel(out var sales, out _, cashReceiptGateway);
        await vm.LoadAsync();
        vm.VisibleProducts[0].AddCommand.Execute(null);
        await vm.PayCashCommand.ExecuteAsync(null);
        vm.SelectCashReceiptTypeCommand.Execute("PERSONAL");
        vm.SelectCashReceiptMerchantCommand.Execute("CARD1");

        await vm.ConfirmCashPaymentCommand.ExecuteAsync(null);

        var request = Assert.Single(cashReceiptGateway.Requests);
        Assert.Equal("CARD1", request.MerchantPayType);
        Assert.Equal("PERSONAL", request.ReceiptType);
        var sale = Assert.Single(sales.CreatedSales);
        Assert.Equal("PERSONAL", sale.Header.CashReceiptType);
        Assert.Equal("CARD1", sale.Header.CashReceiptMerchant);
        Assert.Equal("149331691", sale.Header.CashReceiptApprovalNo);
        Assert.Equal("250704", sale.Header.CashReceiptApprovalDate);
        Assert.False(vm.IsCashConfirmVisible);
    }

    [Fact]
    public async Task ConfirmCashPayment_WhenGatewayDeclines_DoesNotCreateSaleAndKeepsPopupOpen()
    {
        var cashReceiptGateway = new FakeCashReceiptGateway(new CashReceiptResult
        {
            IsIssued = false, ResponseMessage = "현금영수증 발급 실패",
        });
        var vm = CreateViewModel(out var sales, out _, cashReceiptGateway);
        await vm.LoadAsync();
        vm.VisibleProducts[0].AddCommand.Execute(null);
        await vm.PayCashCommand.ExecuteAsync(null);
        vm.SelectCashReceiptTypeCommand.Execute("BUSINESS");
        vm.SelectCashReceiptMerchantCommand.Execute("CARD2");
        var toastValues = new List<string?>();
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(PosViewModel.ToastMessage) && vm.ToastMessage is not null)
                toastValues.Add(vm.ToastMessage);
        };

        await vm.ConfirmCashPaymentCommand.ExecuteAsync(null);

        Assert.Empty(sales.CreatedSales);
        Assert.True(vm.IsCashConfirmVisible);
        Assert.Single(vm.CartLines);
        Assert.True(vm.IsToastWarning);
        Assert.Equal("현금영수증 발급 실패", Assert.Single(toastValues));
    }

    [Fact]
    public async Task ConfirmCashPayment_TogglesIsCashReceiptRequestInProgressDuringCall()
    {
        var cashReceiptGateway = new FakeCashReceiptGateway(new CashReceiptResult
        {
            IsIssued = true, ApprovalNo = "1", ApprovalDateYyMmDd = "250704", ResponseMessage = "발급 완료",
        });
        var vm = CreateViewModel(out _, out _, cashReceiptGateway);
        await vm.LoadAsync();
        vm.VisibleProducts[0].AddCommand.Execute(null);
        await vm.PayCashCommand.ExecuteAsync(null);
        vm.SelectCashReceiptTypeCommand.Execute("PERSONAL");
        vm.SelectCashReceiptMerchantCommand.Execute("CARD1");
        var progressValues = new List<bool>();
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(PosViewModel.IsCashReceiptRequestInProgress))
                progressValues.Add(vm.IsCashReceiptRequestInProgress);
        };

        await vm.ConfirmCashPaymentCommand.ExecuteAsync(null);

        Assert.Contains(true, progressValues);
        Assert.False(vm.IsCashReceiptRequestInProgress);
    }
```

이제 이 파일 상단에 `using FishingMartPos.Services;`가 없다면 추가한다(`CashReceiptResult`/`CashReceiptRequest` 타입 참조용 — 이미 `FishingMartPos.Services` using이 있는지 `PosViewModelPaymentTests.cs` 3번째 줄을 확인하고, 없으면 추가).

- [ ] **Step 4: `PosViewModelTests.cs`의 `CreateViewModel` 헬퍼도 갱신** (Task 2에서 만든 `FakeCashReceiptGateway`를 기본값으로 추가하지 않으면 컴파일이 깨진다)

`tests/FishingMartPos.Tests/ViewModels/PosViewModelTests.cs`의 `CreateViewModel` 오버로드(33-69번째 줄 근처, `vanGateway`/`kiccPosClient` 매개변수가 있는 쪽)에 새 매개변수를 추가하고 생성자 호출에 반영:

```csharp
    private static PosViewModel CreateViewModel(
        out FakeSalesRepository sales,
        out FakeHeldOrderRepository held,
        out INavigationService navigation,
        out MainMenuViewModel mainMenuViewModel,
        IVanPaymentGateway? vanGateway = null,
        IKiccPosClient? kiccPosClient = null,
        ICashReceiptGateway? cashReceiptGateway = null)
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
            vanGateway ?? new FakeVanPaymentGateway(ApprovedResult),
            cashReceiptGateway ?? new FakeCashReceiptGateway(new CashReceiptResult
            {
                IsIssued = true, ApprovalNo = "149331691", ApprovalDateYyMmDd = "250704", ResponseMessage = "현금영수증 발급 완료",
            }),
            kiccPosClient);
    }
```

(이 파일의 다른 `CreateViewModel(out ..., out ...)` 2-out-매개변수 오버로드는 위 오버로드를 호출하는 얇은 래퍼이므로 그대로 둔다 — 시그니처 변경 없음.) 이 파일 상단에 `using FishingMartPos.Services;`가 이미 있는지 확인(3번째 줄에 이미 있음 — 추가 불필요).

- [ ] **Step 5: 테스트 실행 → 실패 확인**

Run: `dotnet test tests/FishingMartPos.Tests --filter "FullyQualifiedName~PosViewModelPaymentTests|FullyQualifiedName~PosViewModelTests"`
Expected: FAIL(컴파일 에러 — `PosViewModel` 생성자가 아직 `ICashReceiptGateway`를 안 받음)

- [ ] **Step 6: `App.xaml.cs` 전체 배선** (DI 등록은 Task 2에서 이미 했음 — 여기서는 실제 KICC 교체 + `PosViewModel` 생성 호출부만 갱신)

`IVanPaymentGateway vanGateway = _services.GetRequiredService<IVanPaymentGateway>(); // StubVanPaymentGateway (기본값)` 줄 바로 뒤에 추가:

```csharp
        ICashReceiptGateway cashReceiptGateway = _services.GetRequiredService<ICashReceiptGateway>(); // StubCashReceiptGateway (기본값)
```

`if (config.KiccUseRealGateway) { ... }` 블록 안, `vanGateway = new KiccVanPaymentGateway(realClient, merchantsByPayType);` 줄 바로 뒤에 추가:

```csharp
            cashReceiptGateway = new KiccCashReceiptGateway(realClient, merchantsByPayType);
```

`CreatePosViewModelAsync` 로컬 함수 안 `new PosViewModel(...)` 호출을 교체:

```csharp
        async Task<PosViewModel> CreatePosViewModelAsync(MainMenuViewModel mainMenu)
        {
            var vm = new PosViewModel(productRepository, codeRepository, salesRepository, heldOrderRepository, delayProvider, session, navigation, mainMenu, vanGateway, cashReceiptGateway, kiccPosClient);
            await vm.LoadAsync();
            return vm;
        }
```

- [ ] **Step 7: 테스트 실행 → 통과 확인**

Run: `dotnet test tests/FishingMartPos.Tests --filter "FullyQualifiedName~PosViewModelPaymentTests|FullyQualifiedName~PosViewModelTests"`
Expected: PASS

- [ ] **Step 8: 빌드 + 전체 테스트 실행 후 커밋**

Run: `dotnet build FishingMartPos.sln` → Expected: 0 경고 / 0 오류
Run: `dotnet test tests/FishingMartPos.Tests` → Expected: 0 실패

```bash
git add src/FishingMartPos/ViewModels/PosViewModel.cs src/FishingMartPos/App.xaml.cs tests/FishingMartPos.Tests/ViewModels/PosViewModelTests.cs tests/FishingMartPos.Tests/ViewModels/PosViewModelPaymentTests.cs
git commit -m "현금영수증 발급을 ConfirmCashPayment 흐름에 연동 (ICashReceiptGateway 호출, 실패 시 팝업 유지)"
```

---

## Task 6: `PosView.xaml` 현금영수증 선택 UI (현금결제 팝업 확장)

**Files:**
- Modify: `src/FishingMartPos/Views/PosView.xaml`

**Interfaces:**
- Consumes: `SelectedCashReceiptType`/`SelectedCashReceiptMerchant`/`CanConfirmCashPayment`/`IsCashReceiptRequestInProgress`/`SelectCashReceiptTypeCommand`/`SelectCashReceiptMerchantCommand`(Task 4/5)

- [ ] **Step 1: 현금결제 팝업에 현금영수증 섹션 + 처리중 상태 추가**

`src/FishingMartPos/Views/PosView.xaml`의 "현금결제 확인 팝업" 블록(364-393번째 줄 근처)을 아래로 **교체**(팝업 `Border`의 `Width`를 320→360으로 넓히고, 거스름돈 `Grid` 뒤·취소/계산완료 버튼 앞에 현금영수증 섹션을 끼워 넣는다. 팝업 전체를 두 상태로 나누는 대신, 진행 중(`IsCashReceiptRequestInProgress`)에는 현금영수증 선택 버튼들만 숨기고 안내 문구로 바꾼다 — 카드결제 팝업처럼 완전히 다른 레이아웃으로 가지 않고 최소 변경으로 처리):

```xml
        <!-- 현금결제 확인 팝업 -->
        <Grid Grid.RowSpan="2" Background="{DynamicResource ModalOverlay}"
              Visibility="{Binding IsCashConfirmVisible, Converter={StaticResource BooleanToVisibilityConverter}}">
            <Border Background="White" BorderBrush="{DynamicResource CardBorder}" BorderThickness="1"
                    Padding="32,24" Width="360" HorizontalAlignment="Center" VerticalAlignment="Center">
                <StackPanel>
                    <TextBlock Text="현금결제 확인" FontSize="16" FontWeight="Bold"
                               Foreground="{DynamicResource TitleText}" HorizontalAlignment="Center" Margin="0,0,0,16" />
                    <Grid Margin="0,4">
                        <TextBlock Text="계산합계" FontSize="13" Foreground="{DynamicResource MutedText}" />
                        <TextBlock Text="{Binding TotalAmountStr}" FontSize="15" FontWeight="Bold" HorizontalAlignment="Right" />
                    </Grid>
                    <Grid Margin="0,4">
                        <TextBlock Text="받은금액" FontSize="13" Foreground="{DynamicResource MutedText}" />
                        <TextBlock Text="{Binding CashInputStr}" FontSize="15" FontWeight="Bold" HorizontalAlignment="Right" />
                    </Grid>
                    <Grid Margin="0,4,0,16">
                        <TextBlock Text="거스름돈" FontSize="13" Foreground="{DynamicResource MutedText}" />
                        <TextBlock Text="{Binding ChangeStr}" FontSize="18" FontWeight="Black"
                                   Foreground="{DynamicResource Accent}" HorizontalAlignment="Right" />
                    </Grid>

                    <!-- 현금영수증 선택 -->
                    <StackPanel Visibility="{Binding IsCashReceiptRequestInProgress, Converter={StaticResource InverseBooleanToVisibilityConverter}}">
                        <TextBlock Text="현금영수증" FontSize="13" Foreground="{DynamicResource MutedText}" Margin="0,0,0,8" />
                        <UniformGrid Columns="3">
                            <Button Content="미발행" Command="{Binding SelectCashReceiptTypeCommand}" CommandParameter="NONE" Margin="3">
                                <Button.Style>
                                    <Style TargetType="Button" BasedOn="{StaticResource InstallmentOptionButtonStyle}">
                                        <Style.Triggers>
                                            <DataTrigger Binding="{Binding SelectedCashReceiptType}" Value="NONE">
                                                <Setter Property="Background" Value="{DynamicResource Accent}" />
                                                <Setter Property="Foreground" Value="White" />
                                            </DataTrigger>
                                        </Style.Triggers>
                                    </Style>
                                </Button.Style>
                            </Button>
                            <Button Content="개인" Command="{Binding SelectCashReceiptTypeCommand}" CommandParameter="PERSONAL" Margin="3">
                                <Button.Style>
                                    <Style TargetType="Button" BasedOn="{StaticResource InstallmentOptionButtonStyle}">
                                        <Style.Triggers>
                                            <DataTrigger Binding="{Binding SelectedCashReceiptType}" Value="PERSONAL">
                                                <Setter Property="Background" Value="{DynamicResource Accent}" />
                                                <Setter Property="Foreground" Value="White" />
                                            </DataTrigger>
                                        </Style.Triggers>
                                    </Style>
                                </Button.Style>
                            </Button>
                            <Button Content="사업자" Command="{Binding SelectCashReceiptTypeCommand}" CommandParameter="BUSINESS" Margin="3">
                                <Button.Style>
                                    <Style TargetType="Button" BasedOn="{StaticResource InstallmentOptionButtonStyle}">
                                        <Style.Triggers>
                                            <DataTrigger Binding="{Binding SelectedCashReceiptType}" Value="BUSINESS">
                                                <Setter Property="Background" Value="{DynamicResource Accent}" />
                                                <Setter Property="Foreground" Value="White" />
                                            </DataTrigger>
                                        </Style.Triggers>
                                    </Style>
                                </Button.Style>
                            </Button>
                        </UniformGrid>

                        <!-- 가맹점 선택: 미발행이 아닐 때만 표시 -->
                        <StackPanel Visibility="{Binding SelectedCashReceiptType, Converter={StaticResource CashReceiptTypeToMerchantPickerVisibilityConverter}}" Margin="0,6,0,0">
                            <TextBlock Text="가맹점 선택" FontSize="12" Foreground="{DynamicResource MutedText}" Margin="0,0,0,6" />
                            <UniformGrid Columns="2">
                                <Button Content="대원수산" Command="{Binding SelectCashReceiptMerchantCommand}" CommandParameter="CARD1" Margin="3">
                                    <Button.Style>
                                        <Style TargetType="Button" BasedOn="{StaticResource InstallmentOptionButtonStyle}">
                                            <Style.Triggers>
                                                <DataTrigger Binding="{Binding SelectedCashReceiptMerchant}" Value="CARD1">
                                                    <Setter Property="Background" Value="{DynamicResource Accent}" />
                                                    <Setter Property="Foreground" Value="White" />
                                                </DataTrigger>
                                            </Style.Triggers>
                                        </Style>
                                    </Button.Style>
                                </Button>
                                <Button Content="대원낚시마트" Command="{Binding SelectCashReceiptMerchantCommand}" CommandParameter="CARD2" Margin="3">
                                    <Button.Style>
                                        <Style TargetType="Button" BasedOn="{StaticResource InstallmentOptionButtonStyle}">
                                            <Style.Triggers>
                                                <DataTrigger Binding="{Binding SelectedCashReceiptMerchant}" Value="CARD2">
                                                    <Setter Property="Background" Value="{DynamicResource Accent}" />
                                                    <Setter Property="Foreground" Value="White" />
                                                </DataTrigger>
                                            </Style.Triggers>
                                        </Style>
                                    </Button.Style>
                                </Button>
                            </UniformGrid>
                        </StackPanel>
                    </StackPanel>

                    <!-- 현금영수증 발급 처리 중 -->
                    <StackPanel Visibility="{Binding IsCashReceiptRequestInProgress, Converter={StaticResource BooleanToVisibilityConverter}}"
                                HorizontalAlignment="Center" Margin="0,4,0,0">
                        <TextBlock Text="카드단말기에서 고객 전화번호(또는 사업자등록번호)를 입력해주세요" FontSize="13" FontWeight="Bold"
                                   Foreground="{DynamicResource TitleText}" HorizontalAlignment="Center" TextWrapping="Wrap" MaxWidth="300" Margin="0,0,0,6" />
                        <TextBlock Text="현금영수증 발급 처리 중입니다" FontSize="12" Foreground="{DynamicResource MutedText}"
                                   HorizontalAlignment="Center" Margin="0,0,0,4" />
                    </StackPanel>

                    <StackPanel Orientation="Horizontal" HorizontalAlignment="Center" Margin="0,16,0,0">
                        <Button Content="취소" Padding="16,6" Margin="0,0,8,0" Command="{Binding CancelCashPaymentCommand}"
                                IsEnabled="{Binding IsCashReceiptRequestInProgress, Converter={StaticResource InverseBooleanConverter}}" />
                        <Button Content="계산완료" Padding="16,6" Background="{DynamicResource Accent}"
                                Foreground="White" BorderBrush="{DynamicResource AccentDark}"
                                Command="{Binding ConfirmCashPaymentCommand}" IsEnabled="{Binding CanConfirmCashPayment}" />
                    </StackPanel>
                </StackPanel>
            </Border>
        </Grid>
```

- [ ] **Step 2: 새 컨버터 2개 추가** (`CashReceiptTypeToMerchantPickerVisibilityConverter`, `InverseBooleanConverter`)

`src/FishingMartPos/Converters/CashReceiptTypeToMerchantPickerVisibilityConverter.cs` (신규 파일, 기존 `InverseNullToVisibilityConverter.cs`와 동일한 스타일):

```csharp
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace FishingMartPos.Converters;

public sealed class CashReceiptTypeToMerchantPickerVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object parameter, CultureInfo culture) =>
        value is string type && type != "NONE" ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
```

`src/FishingMartPos/Converters/InverseBooleanConverter.cs` (신규 파일 — `bool`을 반전시켜 `IsEnabled`에 직접 바인딩하기 위함, 기존 코드베이스에 없던 종류이므로 새로 만든다):

```csharp
using System.Globalization;
using System.Windows.Data;

namespace FishingMartPos.Converters;

public sealed class InverseBooleanConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object parameter, CultureInfo culture) =>
        !(value is true);

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        !(value is true);
}
```

`src/FishingMartPos/Views/PosView.xaml`의 `UserControl.Resources` 블록에 두 컨버터를 등록(기존 `InverseBooleanToVisibilityConverter` 등록 줄 바로 뒤):

```xml
        <local:CashReceiptTypeToMerchantPickerVisibilityConverter x:Key="CashReceiptTypeToMerchantPickerVisibilityConverter" xmlns:local="clr-namespace:FishingMartPos.Converters" />
        <local:InverseBooleanConverter x:Key="InverseBooleanConverter" xmlns:local="clr-namespace:FishingMartPos.Converters" />
```

- [ ] **Step 3: 스모크 테스트 + 빌드 확인**

Run: `dotnet test tests/FishingMartPos.Tests --filter "FullyQualifiedName~PosViewSmokeTests"`
Expected: PASS

Run: `dotnet build FishingMartPos.sln`
Expected: 0 경고 / 0 오류

- [ ] **Step 4: 전체 테스트 실행 후 커밋**

Run: `dotnet test tests/FishingMartPos.Tests`
Expected: 0 실패

```bash
git add src/FishingMartPos/Views/PosView.xaml src/FishingMartPos/Converters/CashReceiptTypeToMerchantPickerVisibilityConverter.cs src/FishingMartPos/Converters/InverseBooleanConverter.cs
git commit -m "현금결제 팝업에 현금영수증 선택(미발행/개인/사업자 + 가맹점) UI 추가"
```

---

## Task 7: `ReceiptDocument` 모델 + `IReceiptPrinter` 스텁 + 결제완료 후 미리보기 상태

**Files:**
- Create: `src/FishingMartPos/Models/ReceiptDocument.cs`
- Create: `src/FishingMartPos/Services/IReceiptPrinter.cs`
- Create: `src/FishingMartPos/Services/StubReceiptPrinter.cs`
- Modify: `src/FishingMartPos/ViewModels/PosViewModel.cs`
- Modify: `src/FishingMartPos/App.xaml.cs`
- Test: `tests/FishingMartPos.Tests/ViewModels/PosViewModelTests.cs`
- Test: `tests/FishingMartPos.Tests/ViewModels/PosViewModelPaymentTests.cs`

**Interfaces:**
- Produces: `ReceiptDocument`/`ReceiptLine`(순수 모델), `bool IsReceiptPreviewVisible`, `ReceiptDocument? PreviewedReceipt`, `IAsyncRelayCommand PrintReceiptCommand`, `IRelayCommand CloseReceiptPreviewCommand` — Task 8의 XAML이 이 상태를 바인딩한다.

- [ ] **Step 1: `ReceiptDocument`/`ReceiptLine` 모델 작성**

`src/FishingMartPos/Models/ReceiptDocument.cs`:

```csharp
namespace FishingMartPos.Models;

public sealed class ReceiptDocument
{
    public required string HeaderText { get; init; }
    public required string FooterText { get; init; }
    public required IReadOnlyList<ReceiptLine> Lines { get; init; }
    public required decimal TotalAmt { get; init; }
    public required string PayTypeLabel { get; init; } // "현금"/"카드결제1"/"카드결제2"
    public string? VanApprovalNo { get; init; }
    public int InstallmentMonths { get; init; }
    public string? CashReceiptTypeLabel { get; init; } // "개인(소득공제)"/"사업자(지출증빙)"/null(미발행)
    public string? CashReceiptApprovalNo { get; init; }
}

public sealed record ReceiptLine(string ProductName, int Qty, decimal UnitPrice, decimal LineAmt);
```

- [ ] **Step 2: `IReceiptPrinter`/`StubReceiptPrinter` 작성**

`src/FishingMartPos/Services/IReceiptPrinter.cs`:

```csharp
using FishingMartPos.Models;

namespace FishingMartPos.Services;

public interface IReceiptPrinter
{
    Task<bool> PrintAsync(ReceiptDocument document);
}
```

`src/FishingMartPos/Services/StubReceiptPrinter.cs`:

```csharp
using FishingMartPos.Models;

namespace FishingMartPos.Services;

public sealed class StubReceiptPrinter : IReceiptPrinter
{
    public Task<bool> PrintAsync(ReceiptDocument document) => Task.FromResult(false);
}
```

`App.xaml.cs`의 `services.AddSingleton<ICashReceiptGateway, StubCashReceiptGateway>();` 바로 뒤에 DI 등록 추가:

```csharp
        services.AddSingleton<IReceiptPrinter, StubReceiptPrinter>();
```

- [ ] **Step 3: 실패하는 테스트 작성 — 결제 완료 후 미리보기 표시**

`PosViewModel` 생성자에 `IReceiptPrinter`를 주입해야 하므로, 먼저 두 테스트 파일의 `CreateViewModel` 헬퍼를 갱신한다.

`PosViewModelPaymentTests.cs`의 `CreateViewModel` 헬퍼(Task 5에서 갱신한 버전) 시그니처와 반환문을 교체:

```csharp
    private static PosViewModel CreateViewModel(
        out FakeSalesRepository sales,
        out FakeHeldOrderRepository held,
        FakeCashReceiptGateway? cashReceiptGateway = null,
        IReceiptPrinter? receiptPrinter = null)
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
            }),
            cashReceiptGateway ?? new FakeCashReceiptGateway(new CashReceiptResult
            {
                IsIssued = true,
                ApprovalNo = "149331691",
                ApprovalDateYyMmDd = "250704",
                ResponseMessage = "현금영수증 발급 완료",
            }),
            receiptPrinter ?? new StubReceiptPrinter());
    }
```

`PosViewModelTests.cs`의 `CreateViewModel` 오버로드(Task 5에서 갱신한 버전)도 `IReceiptPrinter? receiptPrinter = null` 매개변수를 `kiccPosClient` 매개변수 바로 앞에 추가하고, 생성자 호출에 반영한다:

```csharp
    private static PosViewModel CreateViewModel(
        out FakeSalesRepository sales,
        out FakeHeldOrderRepository held,
        out INavigationService navigation,
        out MainMenuViewModel mainMenuViewModel,
        IVanPaymentGateway? vanGateway = null,
        IKiccPosClient? kiccPosClient = null,
        ICashReceiptGateway? cashReceiptGateway = null,
        IReceiptPrinter? receiptPrinter = null)
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
            vanGateway ?? new FakeVanPaymentGateway(ApprovedResult),
            cashReceiptGateway ?? new FakeCashReceiptGateway(new CashReceiptResult
            {
                IsIssued = true, ApprovalNo = "149331691", ApprovalDateYyMmDd = "250704", ResponseMessage = "현금영수증 발급 완료",
            }),
            receiptPrinter ?? new StubReceiptPrinter(),
            kiccPosClient);
    }
```

(`kiccPosClient`가 마지막 매개변수라 실제 생성자 매개변수 순서 — `vanGateway, cashReceiptGateway, receiptPrinter, kiccPosClient` — 와 정확히 일치해야 한다. Step 5에서 만드는 `PosViewModel` 생성자 매개변수 순서와 대조해서 확인한다.)

`PosViewModelPaymentTests.cs` 클래스 마지막에 추가:

```csharp
    [Fact]
    public async Task ConfirmCashPayment_OnSuccess_ShowsReceiptPreviewWithCartSnapshot()
    {
        var vm = CreateViewModel(out _, out _);
        await vm.LoadAsync();
        vm.VisibleProducts[0].AddCommand.Execute(null);
        await vm.PayCashCommand.ExecuteAsync(null);

        await vm.ConfirmCashPaymentCommand.ExecuteAsync(null);

        Assert.True(vm.IsReceiptPreviewVisible);
        Assert.NotNull(vm.PreviewedReceipt);
        Assert.Equal("현금", vm.PreviewedReceipt!.PayTypeLabel);
        Assert.Equal(5000, vm.PreviewedReceipt.TotalAmt);
        var line = Assert.Single(vm.PreviewedReceipt.Lines);
        Assert.Equal("지렁이", line.ProductName);
        Assert.Equal(1, line.Qty);
    }

    [Fact]
    public async Task CloseReceiptPreview_HidesPreview()
    {
        var vm = CreateViewModel(out _, out _);
        await vm.LoadAsync();
        vm.VisibleProducts[0].AddCommand.Execute(null);
        await vm.PayCashCommand.ExecuteAsync(null);
        await vm.ConfirmCashPaymentCommand.ExecuteAsync(null);

        vm.CloseReceiptPreviewCommand.Execute(null);

        Assert.False(vm.IsReceiptPreviewVisible);
    }

    [Fact]
    public async Task PrintReceipt_WithStubPrinter_ShowsNotSupportedToastAndKeepsPreviewOpen()
    {
        var vm = CreateViewModel(out _, out _);
        await vm.LoadAsync();
        vm.VisibleProducts[0].AddCommand.Execute(null);
        await vm.PayCashCommand.ExecuteAsync(null);
        await vm.ConfirmCashPaymentCommand.ExecuteAsync(null);
        var toastValues = new List<string?>();
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(PosViewModel.ToastMessage) && vm.ToastMessage is not null)
                toastValues.Add(vm.ToastMessage);
        };

        await vm.PrintReceiptCommand.ExecuteAsync(null);

        Assert.Equal("프린터 연동은 지원 예정입니다", Assert.Single(toastValues));
        Assert.True(vm.IsReceiptPreviewVisible);
    }
```

`PosViewModelTests.cs`에도 카드결제 경로용 테스트 1개 추가(클래스 마지막):

```csharp
    [Fact]
    public async Task RequestCardApproval_OnSuccess_ShowsReceiptPreviewWithCardDetails()
    {
        var vanGateway = new FakeVanPaymentGateway(new VanApprovalResult
        {
            IsApproved = true, ApprovalNo = "20260723999999", VanCode = "KICC", ResponseMessage = "카드 결제 완료",
        });
        var vm = CreateViewModel(out _, out _, out _, out _, vanGateway);
        await vm.LoadAsync();
        vm.VisibleProducts[0].AddCommand.Execute(null);
        for (int i = 0; i < 9; i++) vm.IncSelectedCommand.Execute(null); // 5,000원 x 10 = 50,000원 — 할부 가능 최소금액
        await vm.PayCard1Command.ExecuteAsync(null);
        vm.SelectInstallmentCommand.Execute("3");

        await vm.RequestCardApprovalCommand.ExecuteAsync(null);

        Assert.True(vm.IsReceiptPreviewVisible);
        Assert.Equal("카드결제1", vm.PreviewedReceipt!.PayTypeLabel);
        Assert.Equal("20260723999999", vm.PreviewedReceipt.VanApprovalNo);
        Assert.Equal(3, vm.PreviewedReceipt.InstallmentMonths);
    }
```

- [ ] **Step 4: 테스트 실행 → 컴파일 에러로 실패 확인**

Run: `dotnet test tests/FishingMartPos.Tests --filter "FullyQualifiedName~PosViewModelPaymentTests|FullyQualifiedName~PosViewModelTests"`
Expected: FAIL(컴파일 에러 — `IsReceiptPreviewVisible`/`PreviewedReceipt`/`PrintReceiptCommand`/생성자 매개변수 등이 아직 없음)

- [ ] **Step 5: `PosViewModel`에 미리보기 상태 + `BuildReceiptDocument` + 생성자 갱신**

생성자에 `IReceiptPrinter receiptPrinter` 매개변수 추가(`ICashReceiptGateway cashReceiptGateway,` 바로 뒤, `IKiccPosClient? kiccPosClient = null` 앞), 필드 `private readonly IReceiptPrinter _receiptPrinter;` 추가(`_cashReceiptGateway` 필드 바로 뒤), 생성자 본문에 `_receiptPrinter = receiptPrinter;` 추가.

`_isCashReceiptRequestInProgress` 선언 뒤에 추가:

```csharp
    [ObservableProperty]
    private bool _isReceiptPreviewVisible;

    [ObservableProperty]
    private ReceiptDocument? _previewedReceipt;
```

파일 끝부분(`RefreshCartLines` 메서드 바로 앞)에 추가:

```csharp
    [RelayCommand]
    private void CloseReceiptPreview() => IsReceiptPreviewVisible = false;

    [RelayCommand]
    private async Task PrintReceipt()
    {
        if (PreviewedReceipt is null) return;
        bool printed = await _receiptPrinter.PrintAsync(PreviewedReceipt);
        if (!printed)
        {
            IsToastWarning = true;
            ToastMessage = "프린터 연동은 지원 예정입니다";
            await _delay.Delay(TimeSpan.FromMilliseconds(1200));
            ToastMessage = null;
        }
    }

    private ReceiptDocument BuildReceiptDocument(
        string payTypeLabel, string? vanApprovalNo, int installmentMonths,
        string? cashReceiptTypeLabel, string? cashReceiptApprovalNo) => new()
    {
        HeaderText = string.Empty,
        FooterText = string.Empty,
        Lines = _cart.Lines.Select(l => new ReceiptLine(l.Name, l.Qty, l.Price, l.LineTotal)).ToList(),
        TotalAmt = _cart.Total,
        PayTypeLabel = payTypeLabel,
        VanApprovalNo = vanApprovalNo,
        InstallmentMonths = installmentMonths,
        CashReceiptTypeLabel = cashReceiptTypeLabel,
        CashReceiptApprovalNo = cashReceiptApprovalNo,
    };

    private static string? CashReceiptTypeLabel(string type) => type switch
    {
        "PERSONAL" => "개인(소득공제)",
        "BUSINESS" => "사업자(지출증빙)",
        _ => null,
    };
```

**중요**: `BuildReceiptDocument`는 `_cart.Lines`/`_cart.Total`을 읽으므로, **반드시 `ResetOrder()`를 호출하기 전에** 호출해야 한다(`ResetOrder()`가 카트를 비우기 때문). `HeaderText`/`FooterText`는 이번 태스크에서는 빈 문자열로 둔다(영수증 설정 연동은 이번 범위 밖 — 스펙 문서에 명시된 대로 `ReceiptConfig` 로딩은 이번 플랜에 포함하지 않는다. 필요하면 다음 개선에서 `IReceiptConfigRepository`를 주입한다).

`PayAsync` 메서드 안, `ToastMessage = null;` 다음 줄(현재 `ResetOrder();` 바로 앞)에 추가:

```csharp
        IsReceiptPreviewVisible = true;
        PreviewedReceipt = BuildReceiptDocument(
            "현금", null, 0,
            CashReceiptTypeLabel(header.CashReceiptType), header.CashReceiptApprovalNo);
```

`RequestCardApproval` 메서드 안, 승인(`result.IsApproved`) 분기의 `ToastMessage = null;` 다음 줄(현재 `ResetOrder();` 바로 앞)에 추가:

```csharp
                IsReceiptPreviewVisible = true;
                PreviewedReceipt = BuildReceiptDocument(
                    capturedPayType == "CARD1" ? "카드결제1" : "카드결제2", result.ApprovalNo, installmentMonths,
                    null, null);
```

- [ ] **Step 6: `App.xaml.cs`의 `PosViewModel` 생성 호출부 갱신**

`CreatePosViewModelAsync` 로컬 함수 안 `new PosViewModel(...)` 호출에 `IReceiptPrinter` 인자를 추가한다. 먼저 `_services.GetRequiredService<IReceiptPrinter>();`를 다른 리포지토리들 조회부 근처(`var vanConfigRepository = ...` 아래)에 추가:

```csharp
        var receiptPrinter = _services.GetRequiredService<IReceiptPrinter>();
```

`new PosViewModel(...)` 호출을 교체:

```csharp
            var vm = new PosViewModel(productRepository, codeRepository, salesRepository, heldOrderRepository, delayProvider, session, navigation, mainMenu, vanGateway, cashReceiptGateway, receiptPrinter, kiccPosClient);
```

- [ ] **Step 7: 테스트 실행 → 통과 확인**

Run: `dotnet test tests/FishingMartPos.Tests --filter "FullyQualifiedName~PosViewModelPaymentTests|FullyQualifiedName~PosViewModelTests"`
Expected: PASS

- [ ] **Step 8: 빌드 + 전체 테스트 실행 후 커밋**

Run: `dotnet build FishingMartPos.sln` → Expected: 0 경고 / 0 오류
Run: `dotnet test tests/FishingMartPos.Tests` → Expected: 0 실패

```bash
git add src/FishingMartPos/Models/ReceiptDocument.cs src/FishingMartPos/Services/IReceiptPrinter.cs src/FishingMartPos/Services/StubReceiptPrinter.cs src/FishingMartPos/ViewModels/PosViewModel.cs src/FishingMartPos/App.xaml.cs tests/FishingMartPos.Tests/ViewModels/PosViewModelTests.cs tests/FishingMartPos.Tests/ViewModels/PosViewModelPaymentTests.cs
git commit -m "ReceiptDocument 모델 + IReceiptPrinter 스텁 추가, 결제 완료 후 영수증 미리보기 상태 연동"
```

---

## Task 8: `PosView.xaml` 영수증 미리보기 팝업

**Files:**
- Modify: `src/FishingMartPos/Views/PosView.xaml`

**Interfaces:**
- Consumes: `IsReceiptPreviewVisible`/`PreviewedReceipt`/`PrintReceiptCommand`/`CloseReceiptPreviewCommand`(Task 7)

- [ ] **Step 1: 영수증 미리보기 오버레이 추가**

`src/FishingMartPos/Views/PosView.xaml`의 "결제완료 토스트" 오버레이(474번째 줄 근처) 바로 뒤, `</Grid>`(최상위 닫는 태그, 파일 마지막) 바로 앞에 추가:

```xml
        <!-- 영수증 미리보기 -->
        <Grid Grid.RowSpan="2" Background="{DynamicResource ModalOverlay}"
              Visibility="{Binding IsReceiptPreviewVisible, Converter={StaticResource BooleanToVisibilityConverter}}">
            <Border Background="White" BorderBrush="{DynamicResource CardBorder}" BorderThickness="1"
                    Padding="24" Width="340" MaxHeight="480" HorizontalAlignment="Center" VerticalAlignment="Center">
                <Grid>
                    <Grid.RowDefinitions>
                        <RowDefinition Height="Auto" />
                        <RowDefinition Height="*" />
                        <RowDefinition Height="Auto" />
                    </Grid.RowDefinitions>
                    <TextBlock Grid.Row="0" Text="영수증 미리보기" FontSize="16" FontWeight="Bold"
                               Foreground="{DynamicResource TitleText}" HorizontalAlignment="Center" Margin="0,0,0,16" />
                    <ScrollViewer Grid.Row="1" VerticalScrollBarVisibility="Auto" MaxHeight="300">
                        <StackPanel DataContext="{Binding PreviewedReceipt}">
                            <ItemsControl ItemsSource="{Binding Lines}">
                                <ItemsControl.ItemTemplate>
                                    <DataTemplate>
                                        <Grid Margin="0,3">
                                            <Grid.ColumnDefinitions>
                                                <ColumnDefinition Width="2*" />
                                                <ColumnDefinition Width="0.6*" />
                                                <ColumnDefinition Width="1*" />
                                            </Grid.ColumnDefinitions>
                                            <TextBlock Text="{Binding ProductName}" FontSize="12" />
                                            <TextBlock Grid.Column="1" Text="{Binding Qty}" FontSize="12" HorizontalAlignment="Center" />
                                            <TextBlock Grid.Column="2" Text="{Binding LineAmt, StringFormat='{}{0:N0}원'}" FontSize="12" HorizontalAlignment="Right" />
                                        </Grid>
                                    </DataTemplate>
                                </ItemsControl.ItemTemplate>
                            </ItemsControl>
                            <Border BorderThickness="0,1,0,0" BorderBrush="{DynamicResource Divider}" Margin="0,8" Padding="0,8,0,0">
                                <Grid>
                                    <TextBlock Text="합계" FontSize="14" FontWeight="Bold" />
                                    <TextBlock Text="{Binding TotalAmt, StringFormat='{}{0:N0}원'}" FontSize="14" FontWeight="Bold" HorizontalAlignment="Right" />
                                </Grid>
                            </Border>
                            <TextBlock Text="{Binding PayTypeLabel, StringFormat='결제수단: {0}'}" FontSize="12" Margin="0,2" />
                            <TextBlock Text="{Binding VanApprovalNo, StringFormat='승인번호: {0}'}" FontSize="12" Margin="0,2"
                                       Visibility="{Binding VanApprovalNo, Converter={StaticResource NullToVisibilityConverter}}" />
                            <TextBlock Text="{Binding InstallmentMonths, StringFormat='할부: {0}개월'}" FontSize="12" Margin="0,2"
                                       Visibility="{Binding VanApprovalNo, Converter={StaticResource NullToVisibilityConverter}}" />
                            <TextBlock Text="{Binding CashReceiptTypeLabel, StringFormat='현금영수증: {0}'}" FontSize="12" Margin="0,2"
                                       Visibility="{Binding CashReceiptTypeLabel, Converter={StaticResource NullToVisibilityConverter}}" />
                            <TextBlock Text="{Binding CashReceiptApprovalNo, StringFormat='현금영수증 승인번호: {0}'}" FontSize="12" Margin="0,2"
                                       Visibility="{Binding CashReceiptApprovalNo, Converter={StaticResource NullToVisibilityConverter}}" />
                        </StackPanel>
                    </ScrollViewer>
                    <StackPanel Grid.Row="2" Orientation="Horizontal" HorizontalAlignment="Center" Margin="0,16,0,0">
                        <Button Content="닫기" Padding="16,6" Margin="0,0,8,0" Command="{Binding CloseReceiptPreviewCommand}" />
                        <Button Content="인쇄" Padding="16,6" Background="{DynamicResource Accent}"
                                Foreground="White" BorderBrush="{DynamicResource AccentDark}"
                                Command="{Binding PrintReceiptCommand}" />
                    </StackPanel>
                </Grid>
            </Border>
        </Grid>
```

**주의**: 내부 `StackPanel`의 `DataContext="{Binding PreviewedReceipt}"`로 바인딩 컨텍스트를 `ReceiptDocument`로 좁혔으므로, `CloseReceiptPreviewCommand`/`PrintReceiptCommand`가 있는 바깥쪽 `StackPanel Grid.Row="2"`는 그 안에 있지 않다 — `PosViewModel`(바깥 `DataContext`) 기준으로 바인딩되므로 그대로 동작한다. `InstallmentMonths`가 0(일시불)일 때도 "할부: 0개월"이 표시되는 것을 막기 위해 `VanApprovalNo` 존재 여부로 같이 가리는 것도 완전하지 않다(카드결제 일시불이면 VanApprovalNo는 있지만 개월수 0) — 이번 범위에서는 "할부: 0개월"이 보여도 기능적 결함은 아니므로(정보 자체는 정확함) 그대로 둔다. 더 다듬고 싶다면 `InstallmentMonths > 0`일 때만 보이는 전용 불리언 프로퍼티를 추가할 수 있지만, 이번 태스크에서는 하지 않는다(YAGNI).

- [ ] **Step 2: 스모크 테스트 + 빌드 확인**

Run: `dotnet test tests/FishingMartPos.Tests --filter "FullyQualifiedName~PosViewSmokeTests"`
Expected: PASS

Run: `dotnet build FishingMartPos.sln`
Expected: 0 경고 / 0 오류

- [ ] **Step 3: 전체 테스트 실행 후 커밋**

Run: `dotnet test tests/FishingMartPos.Tests`
Expected: 0 실패

```bash
git add src/FishingMartPos/Views/PosView.xaml
git commit -m "영수증 미리보기 팝업 UI 추가"
```

---

## 최종 확인 (수동 GUI 스모크테스트)

모든 태스크 완료 후, 컨트롤러가 직접 실행 중인 앱에서 확인한다:

1. 상품 담기 → "현금결제" → 계산합계/받은금액/거스름돈 아래 "현금영수증" 섹션(미발행/개인/사업자)이 보이는지, 개인/사업자 선택 시 대원수산/대원낚시마트 가맹점 버튼이 나타나는지, 가맹점을 고르기 전엔 "계산완료"가 비활성인지.
2. 가맹점까지 고르고 "계산완료" → "카드단말기에서 고객 전화번호(또는 사업자등록번호)를 입력해주세요" 문구로 바뀌었다가(스텁이라 1.5초 후) 성공/실패 토스트로 이어지는지.
3. 결제 성공 후 영수증 미리보기 팝업이 자동으로 뜨는지, 상품 목록/합계/결제수단/현금영수증 정보가 정확한지, "인쇄" 클릭 시 "프린터 연동은 지원 예정입니다" 토스트가 뜨는지, "닫기"로 팝업이 사라지는지.
4. 카드결제1/2도 승인 후 영수증 미리보기가 뜨는지(할부개월/승인번호 표시 확인).
5. 미발행을 고르고 계산완료 시 현금영수증 게이트웨이가 호출되지 않고 바로 저장되는지(장바구니 화면상 지연 없이 진행).
