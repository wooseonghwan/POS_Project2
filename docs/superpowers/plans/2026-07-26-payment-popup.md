# 판매 화면 결제 팝업(현금/카드) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 판매 화면의 현금결제/카드결제1/카드결제2 버튼이 팝업 없이 즉시 처리되던 것을, 현금결제는 거스름돈 확인 팝업으로, 카드결제는 할부개월 선택 + 승인요청 팝업으로 바꾼다.

**Architecture:** `PosViewModel`에 팝업 표시 상태(`IsCashConfirmVisible`/`IsCardPaymentVisible`/`IsCardApprovalInProgress`)와 할부개월 선택 상태(`SelectedInstallmentMonths` 등)를 추가하고, 기존 `PayCash`/`PayCard1`/`PayCard2` 커맨드는 결제를 바로 실행하는 대신 팝업만 띄우도록 바꾼다. 실제 결제 실행은 팝업 안의 새 커맨드(`ConfirmCashPayment`/`RequestCardApproval`)가 맡는다. `IVanPaymentGateway`/`KiccMessageBuilder`/`SaleHeader`/`sales_header_tb`에 할부개월 필드를 추가해 선택값이 실제 KICC 승인 요청과 DB까지 전달되게 한다. 매출관리 화면은 일/월별 집계에 "할부 건수" 컬럼만 추가한다. `PosView.xaml`은 기존 확인모달(`ModalOverlay` 오버레이)과 동일한 패턴으로 팝업 2개(현금 1개, 카드 1개 — 카드는 내부에 2가지 상태)를 추가한다.

**Tech Stack:** .NET 8 / WPF (x86), CommunityToolkit.Mvvm(`[ObservableProperty]`/`[RelayCommand]`), Dapper + MySqlConnector, xUnit.

## Global Constraints

- 스펙 문서: `docs/superpowers/specs/2026-07-26-payment-popup-design.md` — 이번 플랜의 모든 태스크는 이 문서의 결정을 그대로 따른다.
- "전화면" 버튼은 만들지 않는다.
- 카드리더기 실시간 이벤트(`KGetEvent`) 연동은 하지 않는다 — 승인 처리 중 화면은 고정 텍스트만 보여준다.
- 현금 받은금액이 합계보다 적어도 막지 않는다(기존 동작 유지, 새 검증 추가 금지).
- 기존 오버레이 패턴(`Grid.RowSpan="2"` + `ModalOverlay` 배경 + 가운데 정렬 흰색 `Border`)과 기존 `DynamicResource` 색상만 재사용한다 — 새 색상 리소스를 추가하지 않는다.
- `[RelayCommand(CanExecute = ...)]`와 XAML의 직접 `IsEnabled` 바인딩을 동시에 걸지 않는다(`InventoryViewModel`에서 발견된 CanExecute 캐시 미갱신 버그 재발 방지 — 2026-07-22 재고관리 페이징 작업 세션에서 확인됨). 커맨드 몸통 안에서 가드(`if (!CanPay) return;`)하는 기존 `PosViewModel` 패턴을 그대로 따른다.

---

## Task 1: `sales_header_tb.installment_months` 컬럼 + `SaleHeader` 모델 + `SalesRepository`

**Files:**
- Create: `db/migrations/005_add_installment_months.sql`
- Modify: `src/FishingMartPos/Models/SaleHeader.cs`
- Modify: `src/FishingMartPos/Repositories/SalesRepository.cs`
- Test: `tests/FishingMartPos.Tests/Repositories/SalesRepositoryTests.cs`

**Interfaces:**
- Produces: `SaleHeader.InstallmentMonths`(`int`, 기본값 `0`) — Task 4/5에서 `SaleHeader` 생성 시 이 필드를 채운다.

- [ ] **Step 1: 마이그레이션 파일 작성**

`db/migrations/005_add_installment_months.sql`:

```sql
-- 005_add_installment_months.sql
-- 카드결제 시 선택한 할부개월(0=일시불)을 매출 건별로 저장한다.
-- 실행: mysql -h <host> -u <user> -p <database> < 005_add_installment_months.sql

ALTER TABLE sales_header_tb
    ADD COLUMN installment_months INT NOT NULL DEFAULT 0 AFTER van_code;
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
}
```

- [ ] **Step 3: 실패하는 테스트 작성 (DB 왕복 검증)**

`tests/FishingMartPos.Tests/Repositories/SalesRepositoryTests.cs`에 아래 두 테스트를 추가(기존 `using`/클래스 구조는 그대로 두고 클래스 안, 마지막 메서드 뒤에 추가):

```csharp
    [Fact]
    public async Task CreateSale_WithInstallmentMonths_RoundTripsThroughDb()
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
            PayType = "CARD1", VanApprovalNo = "TEST123", VanCode = "KICC", InstallmentMonths = 3,
        };
        var lines = new[]
        {
            new SaleDetailLine { Barcode = barcode, ProductName = "지렁이", Qty = 1, UnitPrice = 5000 },
        };

        long saleNo = await repository.CreateSaleAsync(header, lines);

        using var verifyConn = await factory.CreateOpenConnectionAsync();
        try
        {
            int savedInstallmentMonths = await verifyConn.QuerySingleAsync<int>(
                "SELECT installment_months FROM sales_header_tb WHERE sale_no = @SaleNo", new { SaleNo = saleNo });
            Assert.Equal(3, savedInstallmentMonths);
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
    public async Task CreateSale_WithoutExplicitInstallmentMonths_DefaultsToZero()
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
            int savedInstallmentMonths = await verifyConn.QuerySingleAsync<int>(
                "SELECT installment_months FROM sales_header_tb WHERE sale_no = @SaleNo", new { SaleNo = saleNo });
            Assert.Equal(0, savedInstallmentMonths);
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

Run: `dotnet test tests/FishingMartPos.Tests --filter "FullyQualifiedName~SalesRepositoryTests.CreateSale_WithInstallmentMonths_RoundTripsThroughDb"`
Expected: FAIL — MySQL 에러("Unknown column 'installment_months'" 또는 `installment_months` 프로퍼티가 아직 `SaleHeader`/insert문에 없어 컴파일 에러 — Step 2/5를 아직 안 했다면 컴파일 에러가 정상). 이 시점에는 Step 2(모델)까지만 하고 이 스텝을 실행해 **DB 마이그레이션이 아직 적용되지 않아서** 나는 SQL 에러("Unknown column 'installment_months' in 'field list'")를 직접 확인한다.

- [ ] **Step 5: `SalesRepository`의 insert/select문에 컬럼 추가**

`src/FishingMartPos/Repositories/SalesRepository.cs`의 `CreateSaleAsync` 안 `insertHeaderSql`을 다음으로 교체(라인 21-26):

```csharp
        const string insertHeaderSql = """
            INSERT INTO sales_header_tb
                (pos_cd, sale_dt, staff_cd, total_amt, pay_type, cash_received, change_amt, van_approval_no, van_code, installment_months, status)
            VALUES
                (@PosCd, @SaleDt, @StaffCd, @TotalAmt, @PayType, @CashReceived, @ChangeAmt, @VanApprovalNo, @VanCode, @InstallmentMonths, 'COMPLETE')
            """;
```

같은 파일의 `GetCompletedSalesAsync`의 `sql`을 다음으로 교체(라인 70-76):

```csharp
        const string sql = """
            SELECT pos_cd AS PosCd, sale_dt AS SaleDt, staff_cd AS StaffCd, total_amt AS TotalAmt,
                   pay_type AS PayType, cash_received AS CashReceived, change_amt AS ChangeAmt,
                   van_approval_no AS VanApprovalNo, van_code AS VanCode, installment_months AS InstallmentMonths
            FROM sales_header_tb
            WHERE sale_dt >= @From AND sale_dt < @To AND status = 'COMPLETE'
            """;
```

- [ ] **Step 6: 마이그레이션을 개발 DB에 적용**

이 저장소에는 `mysql` CLI가 없다(기존 관례: `db/migrations/003`/`004` 적용 때도 동일한 방식 사용). 아래 임시(throwaway) 테스트 파일을 만들어 딱 한 번 실행한 뒤 삭제한다.

`tests/FishingMartPos.Tests/_ThrowawayMigration005Test.cs` (임시 파일 — 실행 후 반드시 삭제, 커밋하지 않는다):

```csharp
using Dapper;
using FishingMartPos.Configuration;
using FishingMartPos.Data;
using Xunit;

namespace FishingMartPos.Tests;

public class ThrowawayMigration005Test
{
    [Fact]
    public async Task ApplyMigration005()
    {
        var config = AppConfig.Load(AppContext.BaseDirectory);
        var factory = new MySqlConnectionFactory(config);
        using var connection = await factory.CreateOpenConnectionAsync();

        int alreadyExists = await connection.QuerySingleAsync<int>(
            """
            SELECT COUNT(*) FROM information_schema.COLUMNS
            WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'sales_header_tb' AND COLUMN_NAME = 'installment_months'
            """);

        if (alreadyExists == 0)
        {
            await connection.ExecuteAsync(
                "ALTER TABLE sales_header_tb ADD COLUMN installment_months INT NOT NULL DEFAULT 0 AFTER van_code");
        }

        int afterExists = await connection.QuerySingleAsync<int>(
            """
            SELECT COUNT(*) FROM information_schema.COLUMNS
            WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'sales_header_tb' AND COLUMN_NAME = 'installment_months'
            """);
        Assert.Equal(1, afterExists);
    }
}
```

Run: `dotnet test tests/FishingMartPos.Tests --filter "FullyQualifiedName~ThrowawayMigration005Test"`
Expected: PASS. `information_schema.COLUMNS` 조회로 컬럼이 실제로 생겼는지 확인했으므로(단순히 "마이그레이션 테스트가 통과했다"는 것만 믿지 않는다 — 과거 세션에서 주석 필터링 실수로 인덱스가 조용히 안 생긴 채 테스트만 통과한 사고가 있었다), 이제 이 파일을 삭제한다: `rm tests/FishingMartPos.Tests/_ThrowawayMigration005Test.cs` (git에 추가하지 않았다면 그냥 삭제, 실수로 스테이징했다면 `git restore --staged`).

- [ ] **Step 7: Step 3의 테스트 재실행 → 통과 확인**

Run: `dotnet test tests/FishingMartPos.Tests --filter "FullyQualifiedName~SalesRepositoryTests.CreateSale_WithInstallmentMonths_RoundTripsThroughDb|FullyQualifiedName~SalesRepositoryTests.CreateSale_WithoutExplicitInstallmentMonths_DefaultsToZero"`
Expected: PASS (2 tests)

- [ ] **Step 8: 전체 테스트 실행(회귀 확인) 후 커밋**

Run: `dotnet test tests/FishingMartPos.Tests`
Expected: 기존 테스트 전부 + 신규 2개 PASS, 0 실패 (기존 `SalesRepositoryTests`의 다른 메서드들은 `InstallmentMonths`를 명시하지 않으므로 기본값 0으로 계속 통과해야 한다).

```bash
git add db/migrations/005_add_installment_months.sql src/FishingMartPos/Models/SaleHeader.cs src/FishingMartPos/Repositories/SalesRepository.cs tests/FishingMartPos.Tests/Repositories/SalesRepositoryTests.cs
git commit -m "sales_header_tb에 installment_months 컬럼 추가, SaleHeader/SalesRepository 반영"
```

---

## Task 2: `IVanPaymentGateway`/KICC 할부개월 반영

**Files:**
- Modify: `src/FishingMartPos/Services/IVanPaymentGateway.cs`
- Modify: `src/FishingMartPos/Services/StubVanPaymentGateway.cs`
- Modify: `src/FishingMartPos/Services/Kicc/KiccMessageBuilder.cs`
- Modify: `src/FishingMartPos/Services/Kicc/KiccVanPaymentGateway.cs`
- Modify: `docs/superpowers/specs/2026-07-24-kicc-real-payment-design.md`
- Test: `tests/FishingMartPos.Tests/Services/Kicc/KiccMessageBuilderTests.cs`
- Test: `tests/FishingMartPos.Tests/Services/Kicc/KiccVanPaymentGatewayTests.cs`

**Interfaces:**
- Consumes: (없음 — 독립적으로 구현 가능)
- Produces: `VanApprovalRequest(string PosCode, string PayType, decimal Amount, int InstallmentMonths = 0)` — Task 4에서 카드결제 팝업의 `RequestCardApproval` 커맨드가 이 필드에 `SelectedInstallmentMonths`를 실어 보낸다. `KiccMessageBuilder.BuildApprovalRequest(merchant, amount, posTranNo, installmentMonths)` 4번째 파라미터 추가.

- [ ] **Step 1: 실패하는 테스트 작성 — `KiccMessageBuilder`**

`tests/FishingMartPos.Tests/Services/Kicc/KiccMessageBuilderTests.cs`의 기존 3개 테스트 메서드 호출부를 4-인자 오버로드로 바꾸고(아래 전체 파일로 교체), 할부개월 케이스 테스트를 추가한다:

```csharp
using FishingMartPos.Services.Kicc;
using Xunit;

namespace FishingMartPos.Tests.Services.Kicc;

public class KiccMessageBuilderTests
{
    [Fact]
    public void BuildApprovalRequest_ComputesVatAndFormatsFields_MatchingKiccDocSample()
    {
        var merchant = new KiccMerchantConfig("CARD1", "0788888", "1234567890");

        var sendData = KiccMessageBuilder.BuildApprovalRequest(merchant, 1004m, "POSTRAN123", installmentMonths: 0);

        Assert.Equal("S00=002;S01=D1;S02=40;S03=0788888;S04=1234567890;S09=00;S10=1004;S15=0;S16=91;S23=POSTRAN123;", sendData);
    }

    [Fact]
    public void BuildApprovalRequest_Card1_UsesDaewonSusanTidAndBusinessNo()
    {
        var card1 = new KiccMerchantConfig("CARD1", "2977338", "3169055788");

        var sendData = KiccMessageBuilder.BuildApprovalRequest(card1, 5000m, "T1", installmentMonths: 0);

        Assert.Contains("S03=2977338;S04=3169055788;", sendData);
    }

    [Fact]
    public void BuildApprovalRequest_Card2_UsesDaewonNakssiMartTidAndBusinessNo()
    {
        var card2 = new KiccMerchantConfig("CARD2", "2977340", "3160326930");

        var sendData = KiccMessageBuilder.BuildApprovalRequest(card2, 5000m, "T2", installmentMonths: 0);

        Assert.Contains("S03=2977340;S04=3160326930;", sendData);
    }

    [Theory]
    [InlineData(0, "S09=00;")]
    [InlineData(2, "S09=02;")]
    [InlineData(3, "S09=03;")]
    [InlineData(4, "S09=04;")]
    [InlineData(6, "S09=06;")]
    [InlineData(12, "S09=12;")]
    public void BuildApprovalRequest_InstallmentMonths_FormatsS09AsTwoDigitString(int installmentMonths, string expectedFragment)
    {
        var merchant = new KiccMerchantConfig("CARD1", "0788888", "1234567890");

        var sendData = KiccMessageBuilder.BuildApprovalRequest(merchant, 5000m, "T1", installmentMonths);

        Assert.Contains(expectedFragment, sendData);
    }
}
```

- [ ] **Step 2: 테스트 실행 → 컴파일 에러(4번째 인자 없음)로 실패 확인**

Run: `dotnet test tests/FishingMartPos.Tests --filter "FullyQualifiedName~KiccMessageBuilderTests"`
Expected: FAIL(컴파일 에러 — `BuildApprovalRequest`가 3개 인자만 받음)

- [ ] **Step 3: `KiccMessageBuilder`에 `installmentMonths` 파라미터 추가**

`src/FishingMartPos/Services/Kicc/KiccMessageBuilder.cs` 전체를 다음으로 교체:

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
}
```

- [ ] **Step 4: 테스트 실행 → 통과 확인**

Run: `dotnet test tests/FishingMartPos.Tests --filter "FullyQualifiedName~KiccMessageBuilderTests"`
Expected: PASS (6 tests)

- [ ] **Step 5: `VanApprovalRequest`에 `InstallmentMonths` 추가**

`src/FishingMartPos/Services/IVanPaymentGateway.cs`의 `VanApprovalRequest` 줄을 다음으로 교체:

```csharp
public sealed record VanApprovalRequest(string PosCode, string PayType, decimal Amount, int InstallmentMonths = 0);
```

(파일의 나머지 내용은 그대로 둔다.)

- [ ] **Step 6: `KiccVanPaymentGateway`에서 파라미터 전달**

`src/FishingMartPos/Services/Kicc/KiccVanPaymentGateway.cs`의 `RequestApprovalAsync` 안 `sendData` 생성 줄을 교체:

```csharp
        var sendData = KiccMessageBuilder.BuildApprovalRequest(merchant, request.Amount, posTranNo, request.InstallmentMonths);
```

(그 외 로직은 그대로.)

- [ ] **Step 7: `KiccVanPaymentGatewayTests`에 할부개월 전달 테스트 추가**

`tests/FishingMartPos.Tests/Services/Kicc/KiccVanPaymentGatewayTests.cs`의 클래스 마지막 메서드 뒤에 추가:

```csharp
    [Fact]
    public async Task RequestApprovalAsync_WithInstallmentMonths_ForwardsToSendData()
    {
        var client = new FakeKiccPosClient(KiccRawResponse.Success("R04=0000;R09=1;"));
        var gateway = new KiccVanPaymentGateway(client, Merchants);

        await gateway.RequestApprovalAsync(new VanApprovalRequest("1", "CARD1", 5000m, InstallmentMonths: 3));

        var sendData = Assert.Single(client.Requests).SendData;
        Assert.Contains("S09=03;", sendData);
    }
```

- [ ] **Step 8: 테스트 실행 → 통과 확인**

Run: `dotnet test tests/FishingMartPos.Tests --filter "FullyQualifiedName~KiccVanPaymentGatewayTests"`
Expected: PASS (7 tests)

- [ ] **Step 9: `StubVanPaymentGateway`는 시그니처 변경 없이 그대로 컴파일되는지 확인**

`src/FishingMartPos/Services/StubVanPaymentGateway.cs`는 `request.InstallmentMonths`를 참조하지 않으므로 수정이 필요 없다(레코드에 기본값 있는 새 필드를 추가한 것뿐이라 기존 코드가 깨지지 않음). 확인만 한다: `grep -n "InstallmentMonths" src/FishingMartPos/Services/StubVanPaymentGateway.cs` → 결과 없음(정상, 수정 불필요).

- [ ] **Step 10: 전체 테스트 실행**

Run: `dotnet test tests/FishingMartPos.Tests`
Expected: 0 실패

- [ ] **Step 11: 설계 문서에 보완 메모 추가**

`docs/superpowers/specs/2026-07-24-kicc-real-payment-design.md`의 105번째 줄(S09 필드 설명)을 찾아서 그 줄 바로 뒤에 아래 문장을 추가:

```markdown
| S09 | `"00"` | 할부 없음(일시불) — 이 가게 규모상 할부 미지원, 미입력시도 일시불 처리되지만 명시적으로 넣는다. |
```
을
```markdown
| S09 | `"00"`(일시불) 또는 선택한 할부개월의 2자리 문자열 | **2026-07-26 갱신: 할부개월 선택 기능이 실제로 추가되었다** (`docs/superpowers/specs/2026-07-26-payment-popup-design.md`). `installmentMonths.ToString("00")`로 채운다(0→`"00"`, 2→`"02"` 등) — 이 인코딩은 모듈 API 문서 필드 설명과 국내 VAN 표준 관례에 근거한 추정이며, 실물 단말기로 미검증 상태다. |
```

- [ ] **Step 12: 커밋**

```bash
git add src/FishingMartPos/Services/IVanPaymentGateway.cs src/FishingMartPos/Services/Kicc/KiccMessageBuilder.cs src/FishingMartPos/Services/Kicc/KiccVanPaymentGateway.cs tests/FishingMartPos.Tests/Services/Kicc/KiccMessageBuilderTests.cs tests/FishingMartPos.Tests/Services/Kicc/KiccVanPaymentGatewayTests.cs docs/superpowers/specs/2026-07-24-kicc-real-payment-design.md
git commit -m "VanApprovalRequest/KiccMessageBuilder에 할부개월(InstallmentMonths/S09) 반영"
```

---

## Task 3: `PosViewModel` 할부개월 선택 상태 (게이트웨이 연동 전)

**Files:**
- Modify: `src/FishingMartPos/ViewModels/PosViewModel.cs`
- Test: `tests/FishingMartPos.Tests/ViewModels/PosViewModelTests.cs`

**Interfaces:**
- Consumes: (없음)
- Produces: `int SelectedInstallmentMonths` / `bool IsCustomInstallmentSelected` / `string CustomInstallmentMonthsText` / `string SelectedInstallmentLabel` / `IRelayCommand<string> SelectInstallmentCommand` / `IRelayCommand SelectCustomInstallmentCommand` — Task 4가 `RequestCardApproval`에서 `SelectedInstallmentMonths`를 읽고, Task 5의 카드결제 팝업 XAML(Task 7)이 이 프로퍼티/커맨드들을 바인딩한다.

- [ ] **Step 1: 실패하는 테스트 작성**

`tests/FishingMartPos.Tests/ViewModels/PosViewModelTests.cs`의 `CanPay_DefaultsToTrue` 테스트 뒤(295번째 줄 근처)에 추가:

```csharp
    [Fact]
    public void SelectedInstallmentMonths_DefaultsToZero()
    {
        var vm = CreateViewModel(out _, out _);

        Assert.Equal(0, vm.SelectedInstallmentMonths);
        Assert.Equal("일시불", vm.SelectedInstallmentLabel);
        Assert.False(vm.IsCustomInstallmentSelected);
    }

    [Theory]
    [InlineData("2", 2, "2개월")]
    [InlineData("3", 3, "3개월")]
    [InlineData("12", 12, "12개월")]
    [InlineData("0", 0, "일시불")]
    public void SelectInstallment_SetsMonthsAndLabelAndClearsCustomMode(string param, int expectedMonths, string expectedLabel)
    {
        var vm = CreateViewModel(out _, out _);
        vm.SelectCustomInstallmentCommand.Execute(null); // 기타개월 먼저 선택해둔 상태에서

        vm.SelectInstallmentCommand.Execute(param);

        Assert.Equal(expectedMonths, vm.SelectedInstallmentMonths);
        Assert.Equal(expectedLabel, vm.SelectedInstallmentLabel);
        Assert.False(vm.IsCustomInstallmentSelected);
    }

    [Fact]
    public void SelectCustomInstallment_EnablesCustomModeAndResetsToZeroUntilTyped()
    {
        var vm = CreateViewModel(out _, out _);

        vm.SelectCustomInstallmentCommand.Execute(null);

        Assert.True(vm.IsCustomInstallmentSelected);
        Assert.Equal(0, vm.SelectedInstallmentMonths);
        Assert.Equal(string.Empty, vm.CustomInstallmentMonthsText);
    }

    [Fact]
    public void CustomInstallmentMonthsText_WhenCustomModeActive_ParsesIntoSelectedMonths()
    {
        var vm = CreateViewModel(out _, out _);
        vm.SelectCustomInstallmentCommand.Execute(null);

        vm.CustomInstallmentMonthsText = "8";

        Assert.Equal(8, vm.SelectedInstallmentMonths);
        Assert.Equal("8개월", vm.SelectedInstallmentLabel);
    }

    [Fact]
    public void CustomInstallmentMonthsText_WithNonPositiveOrInvalidInput_FallsBackToZero()
    {
        var vm = CreateViewModel(out _, out _);
        vm.SelectCustomInstallmentCommand.Execute(null);

        vm.CustomInstallmentMonthsText = "abc";
        Assert.Equal(0, vm.SelectedInstallmentMonths);

        vm.CustomInstallmentMonthsText = "-3";
        Assert.Equal(0, vm.SelectedInstallmentMonths);
    }

    [Fact]
    public void CustomInstallmentMonthsText_WhenCustomModeNotActive_IsIgnored()
    {
        var vm = CreateViewModel(out _, out _);
        vm.SelectInstallmentCommand.Execute("2"); // 고정 옵션 선택, 커스텀 모드 아님

        vm.CustomInstallmentMonthsText = "9"; // 코드 경로상 발생하지 않지만 방어적으로 확인

        Assert.Equal(2, vm.SelectedInstallmentMonths); // 커스텀 모드가 아니므로 반영되지 않아야 함
    }
```

- [ ] **Step 2: 테스트 실행 → 컴파일 에러로 실패 확인**

Run: `dotnet test tests/FishingMartPos.Tests --filter "FullyQualifiedName~PosViewModelTests"`
Expected: FAIL(컴파일 에러 — `SelectedInstallmentMonths`/`SelectCustomInstallmentCommand` 등이 아직 없음)

- [ ] **Step 3: `PosViewModel`에 할부개월 상태/커맨드 추가**

`src/FishingMartPos/ViewModels/PosViewModel.cs`의 `_isResetOrderConfirmVisible` 필드 선언(60번째 줄) 바로 뒤에 추가:

```csharp

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedInstallmentLabel))]
    private int _selectedInstallmentMonths;

    [ObservableProperty]
    private bool _isCustomInstallmentSelected;

    [ObservableProperty]
    private string _customInstallmentMonthsText = string.Empty;
```

`CanPay` 프로퍼티(91번째 줄) 바로 뒤에 추가:

```csharp
    public string SelectedInstallmentLabel => SelectedInstallmentMonths <= 0 ? "일시불" : $"{SelectedInstallmentMonths}개월";
```

파일 끝부분, `PayCard2` 메서드(288-289번째 줄) 바로 뒤에 추가:

```csharp
    [RelayCommand]
    private void SelectInstallment(string monthsParam)
    {
        IsCustomInstallmentSelected = false;
        SelectedInstallmentMonths = int.Parse(monthsParam);
    }

    [RelayCommand]
    private void SelectCustomInstallment()
    {
        IsCustomInstallmentSelected = true;
        SelectedInstallmentMonths = 0;
        CustomInstallmentMonthsText = string.Empty;
    }

    partial void OnCustomInstallmentMonthsTextChanged(string value)
    {
        if (!IsCustomInstallmentSelected) return;
        SelectedInstallmentMonths = int.TryParse(value, out int months) && months > 0 ? months : 0;
    }
```

- [ ] **Step 4: 테스트 실행 → 통과 확인**

Run: `dotnet test tests/FishingMartPos.Tests --filter "FullyQualifiedName~PosViewModelTests"`
Expected: PASS

- [ ] **Step 5: 커밋**

```bash
git add src/FishingMartPos/ViewModels/PosViewModel.cs tests/FishingMartPos.Tests/ViewModels/PosViewModelTests.cs
git commit -m "PosViewModel에 카드결제 할부개월 선택 상태 추가 (팝업 연동 전 단계)"
```

---

## Task 4: 카드결제 팝업 열기/취소/승인요청 흐름

**Files:**
- Modify: `src/FishingMartPos/ViewModels/PosViewModel.cs`
- Modify: `tests/FishingMartPos.Tests/ViewModels/PosViewModelTests.cs`

**Interfaces:**
- Consumes: `VanApprovalRequest(PosCode, PayType, Amount, InstallmentMonths)`(Task 2), `SaleHeader.InstallmentMonths`(Task 1), `SelectedInstallmentMonths`/`IsCustomInstallmentSelected`/`CustomInstallmentMonthsText`(Task 3)
- Produces: `bool IsCardPaymentVisible` / `bool IsCardApprovalInProgress` / `IAsyncRelayCommand PayCard1Command` / `IAsyncRelayCommand PayCard2Command`(동작 변경) / `IRelayCommand CancelCardPaymentCommand` / `IAsyncRelayCommand RequestCardApprovalCommand` — Task 7의 `PosView.xaml` 카드결제 팝업이 이 프로퍼티/커맨드에 바인딩한다.

- [ ] **Step 1: 기존 카드결제 테스트를 새 2단계 흐름에 맞게 수정 (먼저 실패시키기)**

`tests/FishingMartPos.Tests/ViewModels/PosViewModelTests.cs`에서 아래 4개 기존 테스트를 찾아 **교체**한다(이름은 그대로 유지, 본문만 교체 — `PayCard1Command.ExecuteAsync`가 이제 팝업만 열고, 실제 승인은 `RequestCardApprovalCommand.ExecuteAsync`가 처리하도록):

`PayCard1_PassesPosCodePayTypeAndAmountToGateway` 교체:

```csharp
    [Fact]
    public async Task PayCard1_PassesPosCodePayTypeAndAmountToGateway()
    {
        var vanGateway = new FakeVanPaymentGateway(ApprovedResult);
        var vm = CreateViewModel(out _, out _, out _, out _, vanGateway);
        await vm.LoadAsync();
        vm.VisibleProducts[0].AddCommand.Execute(null); // 5,000원

        await vm.PayCard1Command.ExecuteAsync(null);
        Assert.Empty(vanGateway.Requests); // 팝업만 열리고 아직 게이트웨이 호출 안 됨
        await vm.RequestCardApprovalCommand.ExecuteAsync(null);

        var request = Assert.Single(vanGateway.Requests);
        Assert.Equal("1", request.PosCode);
        Assert.Equal("CARD1", request.PayType);
        Assert.Equal(5000m, request.Amount);
        Assert.Equal(0, request.InstallmentMonths);
    }
```

`PayCard1_WhenApproved_CreatesSaleWithVanFieldsAndResetsCart` 교체:

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

        await vm.PayCard1Command.ExecuteAsync(null);
        vm.SelectInstallmentCommand.Execute("3");
        await vm.RequestCardApprovalCommand.ExecuteAsync(null);

        var (header, _) = Assert.Single(sales.CreatedSales);
        Assert.Equal("CARD1", header.PayType);
        Assert.Equal("20260723999999", header.VanApprovalNo);
        Assert.Equal("KICC", header.VanCode);
        Assert.Equal(3, header.InstallmentMonths);
        Assert.Empty(vm.CartLines);
        Assert.False(vm.IsCardPaymentVisible);
    }
```

`PayCard2_WhenDeclined_DoesNotCreateSaleAndKeepsCartWithWarningToast` 교체:

```csharp
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
        await vm.RequestCardApprovalCommand.ExecuteAsync(null);

        Assert.Empty(sales.CreatedSales);
        Assert.Single(vm.CartLines);
        Assert.True(vm.IsToastWarning);
        Assert.Contains("한도초과", toastValues);
        Assert.False(vm.IsCardPaymentVisible);
    }
```

`PayCard1_TogglesIsCardProcessingDuringPaymentAndClearsItAfterward` 교체:

```csharp
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
        await vm.RequestCardApprovalCommand.ExecuteAsync(null);

        Assert.Contains(true, processingValues);
        Assert.False(vm.IsCardProcessing);
    }
```

그리고 클래스 마지막에 새 테스트 3개 추가:

```csharp
    [Fact]
    public async Task PayCard1_WithItemsInCart_OpensPopupWithoutCallingGateway()
    {
        var vanGateway = new FakeVanPaymentGateway(ApprovedResult);
        var vm = CreateViewModel(out var sales, out _, out _, out _, vanGateway);
        await vm.LoadAsync();
        vm.VisibleProducts[0].AddCommand.Execute(null);

        await vm.PayCard1Command.ExecuteAsync(null);

        Assert.True(vm.IsCardPaymentVisible);
        Assert.False(vm.IsCardApprovalInProgress);
        Assert.Empty(vanGateway.Requests);
        Assert.Empty(sales.CreatedSales);
    }

    [Fact]
    public async Task CancelCardPayment_ClosesPopupWithoutCallingGatewayAndKeepsCart()
    {
        var vanGateway = new FakeVanPaymentGateway(ApprovedResult);
        var vm = CreateViewModel(out var sales, out _, out _, out _, vanGateway);
        await vm.LoadAsync();
        vm.VisibleProducts[0].AddCommand.Execute(null);
        await vm.PayCard1Command.ExecuteAsync(null);

        vm.CancelCardPaymentCommand.Execute(null);

        Assert.False(vm.IsCardPaymentVisible);
        Assert.Empty(vanGateway.Requests);
        Assert.Empty(sales.CreatedSales);
        Assert.Single(vm.CartLines);
    }

    [Fact]
    public async Task PayCard1_OpeningPopupTwice_ResetsInstallmentSelectionEachTime()
    {
        var vanGateway = new FakeVanPaymentGateway(ApprovedResult);
        var vm = CreateViewModel(out _, out _, out _, out _, vanGateway);
        await vm.LoadAsync();
        vm.VisibleProducts[0].AddCommand.Execute(null);
        await vm.PayCard1Command.ExecuteAsync(null);
        vm.SelectInstallmentCommand.Execute("6");

        vm.CancelCardPaymentCommand.Execute(null);
        vm.VisibleProducts[0].AddCommand.Execute(null);
        await vm.PayCard2Command.ExecuteAsync(null);

        Assert.Equal(0, vm.SelectedInstallmentMonths);
        Assert.False(vm.IsCustomInstallmentSelected);
    }
```

- [ ] **Step 2: 테스트 실행 → 실패 확인**

Run: `dotnet test tests/FishingMartPos.Tests --filter "FullyQualifiedName~PosViewModelTests"`
Expected: FAIL(`IsCardPaymentVisible`/`RequestCardApprovalCommand`/`CancelCardPaymentCommand` 없어서 컴파일 에러, 또는 기존 `PayCard1Command.ExecuteAsync(null)` 한 번만으로 승인까지 끝나던 옛 로직이라 어서션 실패)

- [ ] **Step 3: `PosViewModel`에 카드결제 팝업 상태/커맨드 추가, 기존 `PayCard1`/`PayCard2`/`PayCardAsync` 교체**

`src/FishingMartPos/ViewModels/PosViewModel.cs`에서 `_isResetOrderConfirmVisible` 다음(Task 3에서 추가한 3개 프로퍼티 바로 앞)에 추가:

```csharp
    [ObservableProperty]
    private bool _isCardPaymentVisible;

    [ObservableProperty]
    private bool _isCardApprovalInProgress;

```

클래스 필드 영역(`_pendingRecallHoldNo`/`_pendingDeleteHoldNo`와 같은 위치 스타일로, `PayCard2` 메서드 바로 위)에 추가:

```csharp
    private string? _pendingCardPayType;

```

기존 `PayCard1`/`PayCard2`(282-289번째 줄)와 `PayCardAsync`(330-385번째 줄) 전체를 아래로 **교체**:

```csharp
    [RelayCommand]
    private async Task PayCard1() => await OpenCardPaymentPopupAsync("CARD1");

    [RelayCommand]
    private async Task PayCard2() => await OpenCardPaymentPopupAsync("CARD2");

    private async Task OpenCardPaymentPopupAsync(string payType)
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

        _pendingCardPayType = payType;
        SelectedInstallmentMonths = 0;
        IsCustomInstallmentSelected = false;
        CustomInstallmentMonthsText = string.Empty;
        IsCardApprovalInProgress = false;
        IsCardPaymentVisible = true;
    }

    [RelayCommand]
    private void CancelCardPayment()
    {
        IsCardPaymentVisible = false;
        _pendingCardPayType = null;
    }

    [RelayCommand]
    private async Task RequestCardApproval()
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
                new VanApprovalRequest(_session.CurrentTerminal!.PosCode, capturedPayType, _cart.Total, installmentMonths));

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
            _pendingCardPayType = null;
        }
    }
```

- [ ] **Step 4: 테스트 실행 → 통과 확인**

Run: `dotnet test tests/FishingMartPos.Tests --filter "FullyQualifiedName~PosViewModelTests"`
Expected: PASS

- [ ] **Step 5: 관련된 다른 테스트 파일도 회귀 확인**

Run: `dotnet test tests/FishingMartPos.Tests --filter "FullyQualifiedName~PosViewModelPaymentTests"`
Expected: PASS (이 파일은 카드결제를 다루지 않으므로 영향 없어야 함)

- [ ] **Step 6: 전체 테스트 실행 후 커밋**

Run: `dotnet test tests/FishingMartPos.Tests`
Expected: 0 실패

```bash
git add src/FishingMartPos/ViewModels/PosViewModel.cs tests/FishingMartPos.Tests/ViewModels/PosViewModelTests.cs
git commit -m "카드결제1/2를 할부개월 선택 팝업 흐름으로 변경 (RequestCardApprovalCommand가 실제 승인 요청)"
```

---

## Task 5: 현금결제 확인 팝업 흐름

**Files:**
- Modify: `src/FishingMartPos/ViewModels/PosViewModel.cs`
- Modify: `tests/FishingMartPos.Tests/ViewModels/PosViewModelPaymentTests.cs`

**Interfaces:**
- Consumes: 기존 `PayAsync(string payType, string toastLabel)`(비공개 메서드, 변경 없음)
- Produces: `bool IsCashConfirmVisible` / `IAsyncRelayCommand PayCashCommand`(동작 변경) / `IAsyncRelayCommand ConfirmCashPaymentCommand` / `IRelayCommand CancelCashPaymentCommand` — Task 6의 `PosView.xaml` 현금결제 팝업이 이 프로퍼티/커맨드에 바인딩한다.

- [ ] **Step 1: 기존 테스트를 새 흐름에 맞게 수정 (먼저 실패시키기)**

`tests/FishingMartPos.Tests/ViewModels/PosViewModelPaymentTests.cs`의 `PayCash_WithItemsInCart_CreatesSaleAndClearsCart` 테스트를 아래로 교체:

```csharp
    [Fact]
    public async Task PayCash_WithItemsInCart_OpensConfirmPopupWithoutCreatingSale()
    {
        var vm = CreateViewModel(out var sales, out _);
        await vm.LoadAsync();
        vm.VisibleProducts[0].AddCommand.Execute(null);

        await vm.PayCashCommand.ExecuteAsync(null);

        Assert.True(vm.IsCashConfirmVisible);
        Assert.Empty(sales.CreatedSales);
    }

    [Fact]
    public async Task ConfirmCashPayment_CreatesSaleClosesPopupAndClearsCart()
    {
        var vm = CreateViewModel(out var sales, out _);
        await vm.LoadAsync();
        vm.VisibleProducts[0].AddCommand.Execute(null);
        await vm.PayCashCommand.ExecuteAsync(null);

        await vm.ConfirmCashPaymentCommand.ExecuteAsync(null);

        var sale = Assert.Single(sales.CreatedSales);
        Assert.Equal("CASH", sale.Header.PayType);
        Assert.Equal(5000, sale.Header.TotalAmt);
        Assert.False(vm.IsCashConfirmVisible);
        Assert.Empty(vm.CartLines);
    }

    [Fact]
    public async Task CancelCashPayment_ClosesPopupAndKeepsCartAndCashInputUntouched()
    {
        var vm = CreateViewModel(out var sales, out _);
        await vm.LoadAsync();
        // 숫자 키패드는 장바구니 줄이 선택되어 있으면 수량 버퍼로, 선택된 줄이 없으면 받은금액 입력으로 들어간다
        // (PosViewModel.PressKey) — 상품을 담기 *전에* 눌러야 받은금액에 반영된다(AddToCart가 SelectedBarcode를 설정해버리므로).
        vm.PressKeyCommand.Execute("5");
        vm.PressKeyCommand.Execute("0");
        vm.PressKeyCommand.Execute("0");
        vm.PressKeyCommand.Execute("0");
        vm.VisibleProducts[0].AddCommand.Execute(null);
        await vm.PayCashCommand.ExecuteAsync(null);

        vm.CancelCashPaymentCommand.Execute(null);

        Assert.False(vm.IsCashConfirmVisible);
        Assert.Empty(sales.CreatedSales);
        Assert.Single(vm.CartLines);
        Assert.Equal("5,000원", vm.CashInputStr);
    }
```

- [ ] **Step 2: 테스트 실행 → 실패 확인**

Run: `dotnet test tests/FishingMartPos.Tests --filter "FullyQualifiedName~PosViewModelPaymentTests"`
Expected: FAIL(`IsCashConfirmVisible`/`ConfirmCashPaymentCommand`/`CancelCashPaymentCommand` 없어서 컴파일 에러, 또는 `PayCashCommand.ExecuteAsync` 한 번에 바로 저장되던 옛 로직이라 실패)

- [ ] **Step 3: `PosViewModel`에 현금결제 팝업 상태/커맨드 추가, 기존 `PayCash` 교체**

`_isCardPaymentVisible`/`_isCardApprovalInProgress` 선언(Task 4에서 추가) 바로 앞에 추가:

```csharp
    [ObservableProperty]
    private bool _isCashConfirmVisible;

```

기존 `PayCash` 메서드(`private async Task PayCash() => await PayAsync("CASH", "현금 결제 완료");`)를 아래로 **교체**:

```csharp
    [RelayCommand]
    private async Task PayCash()
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

        IsCashConfirmVisible = true;
    }

    [RelayCommand]
    private async Task ConfirmCashPayment()
    {
        IsCashConfirmVisible = false;
        await PayAsync("CASH", "현금 결제 완료");
    }

    [RelayCommand]
    private void CancelCashPayment()
    {
        IsCashConfirmVisible = false;
    }
```

(기존 `PayAsync(string payType, string toastLabel)` 비공개 메서드는 수정하지 않는다 — 그대로 재사용.)

- [ ] **Step 4: 테스트 실행 → 통과 확인**

Run: `dotnet test tests/FishingMartPos.Tests --filter "FullyQualifiedName~PosViewModelPaymentTests"`
Expected: PASS

- [ ] **Step 5: 전체 테스트 실행 후 커밋**

Run: `dotnet test tests/FishingMartPos.Tests`
Expected: 0 실패 (`PosViewModelTests.PayCash_WithEmptyCart_ShowsWarningToast` 등 빈 장바구니 가드 테스트는 그대로 통과해야 함 — 변경 없음)

```bash
git add src/FishingMartPos/ViewModels/PosViewModel.cs tests/FishingMartPos.Tests/ViewModels/PosViewModelPaymentTests.cs
git commit -m "현금결제를 거스름돈 확인 팝업 흐름으로 변경 (ConfirmCashPaymentCommand가 실제 저장)"
```

---

## Task 6: `PosView.xaml` 현금결제 팝업 UI + 바코드 스캔 입력 가드

**Files:**
- Modify: `src/FishingMartPos/Views/PosView.xaml`
- Modify: `src/FishingMartPos/Views/PosView.xaml.cs`

**Interfaces:**
- Consumes: `IsCashConfirmVisible`/`ConfirmCashPaymentCommand`/`CancelCashPaymentCommand`(Task 5), `TotalAmountStr`/`CashInputStr`/`ChangeStr`(기존)

- [ ] **Step 1: 현금결제 확인 팝업 오버레이 추가**

`src/FishingMartPos/Views/PosView.xaml`의 "초기화 확인" 오버레이(338-355번째 줄) 바로 뒤, "결제완료 토스트" 오버레이(357번째 줄) 바로 앞에 추가:

```xml
        <!-- 현금결제 확인 팝업 -->
        <Grid Grid.RowSpan="2" Background="{DynamicResource ModalOverlay}"
              Visibility="{Binding IsCashConfirmVisible, Converter={StaticResource BooleanToVisibilityConverter}}">
            <Border Background="White" BorderBrush="{DynamicResource CardBorder}" BorderThickness="1"
                    Padding="32,24" Width="320" HorizontalAlignment="Center" VerticalAlignment="Center">
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
                    <StackPanel Orientation="Horizontal" HorizontalAlignment="Center">
                        <Button Content="취소" Padding="16,6" Margin="0,0,8,0" Command="{Binding CancelCashPaymentCommand}" />
                        <Button Content="계산완료" Padding="16,6" Background="{DynamicResource Accent}"
                                Foreground="White" BorderBrush="{DynamicResource AccentDark}"
                                Command="{Binding ConfirmCashPaymentCommand}" />
                    </StackPanel>
                </StackPanel>
            </Border>
        </Grid>

```

- [ ] **Step 2: 팝업이 떠 있는 동안 바코드 스캔 입력이 새어 들어가지 않도록 코드비하인드 가드 추가**

`src/FishingMartPos/Views/PosView.xaml.cs` 전체를 아래로 교체:

```csharp
using System.Text;
using System.Windows.Controls;
using System.Windows.Input;

namespace FishingMartPos.Views;

public partial class PosView : UserControl
{
    private readonly StringBuilder _scanBuffer = new();

    public PosView()
    {
        InitializeComponent();
        Loaded += (_, _) => Focus();
    }

    private bool IsAnyPaymentPopupOpen =>
        DataContext is ViewModels.PosViewModel vm && (vm.IsCashConfirmVisible || vm.IsCardPaymentVisible);

    private void PosView_PreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        if (IsAnyPaymentPopupOpen)
        {
            return;
        }

        if (e.Text.Length == 1 && char.IsDigit(e.Text[0]))
        {
            _scanBuffer.Append(e.Text);
        }
    }

    private void PosView_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (IsAnyPaymentPopupOpen)
        {
            _scanBuffer.Clear();
            return;
        }

        if (e.Key != Key.Enter || _scanBuffer.Length == 0)
        {
            return;
        }

        string barcode = _scanBuffer.ToString();
        _scanBuffer.Clear();

        if (DataContext is ViewModels.PosViewModel vm)
        {
            vm.ScanBarcodeCommand.Execute(barcode);
        }
    }
}
```

- [ ] **Step 3: 스모크 테스트로 XAML 파싱 에러 없는지 확인**

Run: `dotnet test tests/FishingMartPos.Tests --filter "FullyQualifiedName~PosViewSmokeTests"`
Expected: PASS

- [ ] **Step 4: 빌드 확인**

Run: `dotnet build FishingMartPos.sln`
Expected: 0 경고 / 0 오류

- [ ] **Step 5: 커밋**

```bash
git add src/FishingMartPos/Views/PosView.xaml src/FishingMartPos/Views/PosView.xaml.cs
git commit -m "현금결제 확인 팝업 UI 추가, 결제 팝업 표시 중 바코드스캔 입력 가드"
```

---

## Task 7: `PosView.xaml` 카드결제 팝업 UI (할부선택/승인중 2단계)

**Files:**
- Create: `src/FishingMartPos/Converters/InverseBooleanToVisibilityConverter.cs`
- Modify: `src/FishingMartPos/Views/PosView.xaml`

**Interfaces:**
- Consumes: `IsCardPaymentVisible`/`IsCardApprovalInProgress`/`SelectedInstallmentMonths`/`IsCustomInstallmentSelected`/`CustomInstallmentMonthsText`/`SelectedInstallmentLabel`/`SelectInstallmentCommand`/`SelectCustomInstallmentCommand`/`RequestCardApprovalCommand`/`CancelCardPaymentCommand`(Task 3/4), `TotalAmountStr`(기존)

- [ ] **Step 1: `InverseBooleanToVisibilityConverter` 추가**

`src/FishingMartPos/Converters/InverseBooleanToVisibilityConverter.cs` (신규 파일, 기존 `InverseNullToVisibilityConverter.cs`와 동일한 스타일):

```csharp
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace FishingMartPos.Converters;

public sealed class InverseBooleanToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object parameter, CultureInfo culture) =>
        value is true ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
```

- [ ] **Step 2: `PosView.xaml`의 `UserControl.Resources`에 새 컨버터 등록**

`src/FishingMartPos/Views/PosView.xaml`의 `UserControl.Resources` 블록(10-14번째 줄)을 아래로 교체:

```xml
    <UserControl.Resources>
        <BooleanToVisibilityConverter x:Key="BooleanToVisibilityConverter" />
        <local:NullToVisibilityConverter x:Key="NullToVisibilityConverter" xmlns:local="clr-namespace:FishingMartPos.Converters" />
        <local:ZeroCountToVisibilityConverter x:Key="ZeroCountToVisibilityConverter" xmlns:local="clr-namespace:FishingMartPos.Converters" />
        <local:InverseBooleanToVisibilityConverter x:Key="InverseBooleanToVisibilityConverter" xmlns:local="clr-namespace:FishingMartPos.Converters" />
        <Style x:Key="InstallmentOptionButtonStyle" TargetType="Button">
            <Setter Property="Margin" Value="3" />
            <Setter Property="Padding" Value="6" />
            <Setter Property="Background" Value="{DynamicResource LogoutButtonBackground}" />
            <Setter Property="Foreground" Value="{DynamicResource MutedText}" />
        </Style>
    </UserControl.Resources>
```

- [ ] **Step 3: 카드결제 팝업 오버레이 추가**

Task 6에서 추가한 "현금결제 확인 팝업" 오버레이 바로 뒤(그리고 "결제완료 토스트" 오버레이 바로 앞)에 추가:

```xml
        <!-- 카드결제 팝업 -->
        <Grid Grid.RowSpan="2" Background="{DynamicResource ModalOverlay}"
              Visibility="{Binding IsCardPaymentVisible, Converter={StaticResource BooleanToVisibilityConverter}}">
            <Border Background="White" BorderBrush="{DynamicResource CardBorder}" BorderThickness="1"
                    Padding="32,24" Width="420" HorizontalAlignment="Center" VerticalAlignment="Center">
                <StackPanel>
                    <TextBlock Text="카드결제" FontSize="16" FontWeight="Bold"
                               Foreground="{DynamicResource TitleText}" HorizontalAlignment="Center" Margin="0,0,0,4" />
                    <TextBlock Text="{Binding TotalAmountStr}" FontSize="24" FontWeight="Black"
                               Foreground="{DynamicResource Accent}" HorizontalAlignment="Center" Margin="0,0,0,16" />

                    <!-- 할부 선택 상태 -->
                    <StackPanel Visibility="{Binding IsCardApprovalInProgress, Converter={StaticResource InverseBooleanToVisibilityConverter}}">
                        <TextBlock Text="할부개월 선택" FontSize="13" Foreground="{DynamicResource MutedText}" Margin="0,0,0,8" />
                        <UniformGrid Columns="4" Rows="2">
                            <Button Content="일시불" Command="{Binding SelectInstallmentCommand}" CommandParameter="0">
                                <Button.Style>
                                    <Style TargetType="Button" BasedOn="{StaticResource InstallmentOptionButtonStyle}">
                                        <Style.Triggers>
                                            <MultiDataTrigger>
                                                <MultiDataTrigger.Conditions>
                                                    <Condition Binding="{Binding SelectedInstallmentMonths}" Value="0" />
                                                    <Condition Binding="{Binding IsCustomInstallmentSelected}" Value="False" />
                                                </MultiDataTrigger.Conditions>
                                                <Setter Property="Background" Value="{DynamicResource Accent}" />
                                                <Setter Property="Foreground" Value="White" />
                                            </MultiDataTrigger>
                                        </Style.Triggers>
                                    </Style>
                                </Button.Style>
                            </Button>
                            <Button Content="2개월" Command="{Binding SelectInstallmentCommand}" CommandParameter="2">
                                <Button.Style>
                                    <Style TargetType="Button" BasedOn="{StaticResource InstallmentOptionButtonStyle}">
                                        <Style.Triggers>
                                            <DataTrigger Binding="{Binding SelectedInstallmentMonths}" Value="2">
                                                <Setter Property="Background" Value="{DynamicResource Accent}" />
                                                <Setter Property="Foreground" Value="White" />
                                            </DataTrigger>
                                        </Style.Triggers>
                                    </Style>
                                </Button.Style>
                            </Button>
                            <Button Content="3개월" Command="{Binding SelectInstallmentCommand}" CommandParameter="3">
                                <Button.Style>
                                    <Style TargetType="Button" BasedOn="{StaticResource InstallmentOptionButtonStyle}">
                                        <Style.Triggers>
                                            <DataTrigger Binding="{Binding SelectedInstallmentMonths}" Value="3">
                                                <Setter Property="Background" Value="{DynamicResource Accent}" />
                                                <Setter Property="Foreground" Value="White" />
                                            </DataTrigger>
                                        </Style.Triggers>
                                    </Style>
                                </Button.Style>
                            </Button>
                            <Button Content="4개월" Command="{Binding SelectInstallmentCommand}" CommandParameter="4">
                                <Button.Style>
                                    <Style TargetType="Button" BasedOn="{StaticResource InstallmentOptionButtonStyle}">
                                        <Style.Triggers>
                                            <DataTrigger Binding="{Binding SelectedInstallmentMonths}" Value="4">
                                                <Setter Property="Background" Value="{DynamicResource Accent}" />
                                                <Setter Property="Foreground" Value="White" />
                                            </DataTrigger>
                                        </Style.Triggers>
                                    </Style>
                                </Button.Style>
                            </Button>
                            <Button Content="6개월" Command="{Binding SelectInstallmentCommand}" CommandParameter="6">
                                <Button.Style>
                                    <Style TargetType="Button" BasedOn="{StaticResource InstallmentOptionButtonStyle}">
                                        <Style.Triggers>
                                            <DataTrigger Binding="{Binding SelectedInstallmentMonths}" Value="6">
                                                <Setter Property="Background" Value="{DynamicResource Accent}" />
                                                <Setter Property="Foreground" Value="White" />
                                            </DataTrigger>
                                        </Style.Triggers>
                                    </Style>
                                </Button.Style>
                            </Button>
                            <Button Content="12개월" Command="{Binding SelectInstallmentCommand}" CommandParameter="12">
                                <Button.Style>
                                    <Style TargetType="Button" BasedOn="{StaticResource InstallmentOptionButtonStyle}">
                                        <Style.Triggers>
                                            <DataTrigger Binding="{Binding SelectedInstallmentMonths}" Value="12">
                                                <Setter Property="Background" Value="{DynamicResource Accent}" />
                                                <Setter Property="Foreground" Value="White" />
                                            </DataTrigger>
                                        </Style.Triggers>
                                    </Style>
                                </Button.Style>
                            </Button>
                            <Button Content="기타개월" Command="{Binding SelectCustomInstallmentCommand}">
                                <Button.Style>
                                    <Style TargetType="Button" BasedOn="{StaticResource InstallmentOptionButtonStyle}">
                                        <Style.Triggers>
                                            <DataTrigger Binding="{Binding IsCustomInstallmentSelected}" Value="True">
                                                <Setter Property="Background" Value="{DynamicResource Accent}" />
                                                <Setter Property="Foreground" Value="White" />
                                            </DataTrigger>
                                        </Style.Triggers>
                                    </Style>
                                </Button.Style>
                            </Button>
                            <TextBox Text="{Binding CustomInstallmentMonthsText, UpdateSourceTrigger=PropertyChanged}"
                                     IsEnabled="{Binding IsCustomInstallmentSelected}" Margin="3" VerticalContentAlignment="Center"
                                     HorizontalContentAlignment="Center" />
                        </UniformGrid>
                        <TextBlock Text="{Binding SelectedInstallmentLabel, StringFormat='선택: {0}'}" FontSize="12"
                                   Foreground="{DynamicResource MutedText}" HorizontalAlignment="Center" Margin="0,8,0,16" />
                        <StackPanel Orientation="Horizontal" HorizontalAlignment="Center">
                            <Button Content="취소" Padding="16,6" Margin="0,0,8,0" Command="{Binding CancelCardPaymentCommand}" />
                            <Button Content="승인요청" Padding="16,6" Background="{DynamicResource Accent}"
                                    Foreground="White" BorderBrush="{DynamicResource AccentDark}"
                                    Command="{Binding RequestCardApprovalCommand}" />
                        </StackPanel>
                    </StackPanel>

                    <!-- 승인 처리 중 상태 -->
                    <StackPanel Visibility="{Binding IsCardApprovalInProgress, Converter={StaticResource BooleanToVisibilityConverter}}"
                                HorizontalAlignment="Center" Margin="0,12,0,0">
                        <TextBlock Text="카드 리더기에 카드를 꽂아주세요" FontSize="14" FontWeight="Bold"
                                   Foreground="{DynamicResource TitleText}" HorizontalAlignment="Center" Margin="0,0,0,6" />
                        <TextBlock Text="승인 처리 중입니다" FontSize="13" Foreground="{DynamicResource MutedText}"
                                   HorizontalAlignment="Center" Margin="0,0,0,16" />
                    </StackPanel>
                </StackPanel>
            </Border>
        </Grid>

```

- [ ] **Step 4: 스모크 테스트로 XAML 파싱 에러 없는지 확인**

Run: `dotnet test tests/FishingMartPos.Tests --filter "FullyQualifiedName~PosViewSmokeTests"`
Expected: PASS (`MultiDataTrigger`/새 컨버터/새 바인딩 경로가 모두 파싱되는지 이 테스트가 잡아준다 — `PosView` 생성자가 예외 없이 끝나는지만 확인하면 되므로 `DataContext` 연결은 필요 없다)

- [ ] **Step 5: 빌드 확인**

Run: `dotnet build FishingMartPos.sln`
Expected: 0 경고 / 0 오류

- [ ] **Step 6: 커밋**

```bash
git add src/FishingMartPos/Converters/InverseBooleanToVisibilityConverter.cs src/FishingMartPos/Views/PosView.xaml
git commit -m "카드결제 팝업 UI 추가 (할부개월 선택 + 승인 처리 중 2단계)"
```

---

## Task 8: 매출관리 "할부 건수" 컬럼

**Files:**
- Modify: `src/FishingMartPos/ViewModels/SalesReportRowViewModel.cs`
- Modify: `src/FishingMartPos/ViewModels/SalesReportViewModel.cs`
- Modify: `src/FishingMartPos/Views/SalesReportView.xaml`
- Test: `tests/FishingMartPos.Tests/ViewModels/SalesReportViewModelTests.cs`

**Interfaces:**
- Consumes: `SaleHeader.InstallmentMonths`(Task 1)

- [ ] **Step 1: 실패하는 테스트 작성**

`tests/FishingMartPos.Tests/ViewModels/SalesReportViewModelTests.cs`의 `Sale` 헬퍼(25-28번째 줄)를 아래로 교체(선택적 `installmentMonths` 파라미터 추가, 기본값 0이라 기존 호출부는 그대로 컴파일됨):

```csharp
    private static SaleHeader Sale(DateTime saleDt, string payType, decimal amount, int installmentMonths = 0) => new()
    {
        PosCd = "1", SaleDt = saleDt, StaffCd = "ADMIN1", TotalAmt = amount, PayType = payType, InstallmentMonths = installmentMonths,
    };
```

클래스 마지막(`GoToMainMenu_NavigatesBackToMainMenu` 뒤)에 추가:

```csharp
    [Fact]
    public async Task LoadAsync_CountsInstallmentSalesSeparatelyFromLumpSum()
    {
        var (vm, sales, _, _) = Create();
        var day1 = DateTime.Today.AddDays(-1);
        sales.SeedCompletedSales(new[]
        {
            Sale(day1, "CARD1", 3000, installmentMonths: 3),
            Sale(day1, "CARD2", 2000, installmentMonths: 6),
            Sale(day1.AddHours(1), "CASH", 5000, installmentMonths: 0),
        });
        vm.DateFrom = day1;
        vm.DateTo = day1;

        await vm.LoadAsync();

        var row = Assert.Single(vm.Rows);
        Assert.Equal("2건", row.InstallmentCountStr);
    }

    [Fact]
    public async Task LoadAsync_NoInstallmentSales_ShowsZeroCount()
    {
        var (vm, sales, _, _) = Create();
        var day1 = DateTime.Today.AddDays(-1);
        sales.SeedCompletedSales(new[] { Sale(day1, "CASH", 5000) });
        vm.DateFrom = day1;
        vm.DateTo = day1;

        await vm.LoadAsync();

        var row = Assert.Single(vm.Rows);
        Assert.Equal("0건", row.InstallmentCountStr);
    }
```

- [ ] **Step 2: 테스트 실행 → 실패 확인**

Run: `dotnet test tests/FishingMartPos.Tests --filter "FullyQualifiedName~SalesReportViewModelTests"`
Expected: FAIL(`InstallmentCountStr`가 아직 없어 컴파일 에러)

- [ ] **Step 3: `SalesReportRowViewModel`/`SalesReportViewModel`에 집계 추가**

`src/FishingMartPos/ViewModels/SalesReportRowViewModel.cs` 전체를 아래로 교체:

```csharp
namespace FishingMartPos.ViewModels;

public sealed class SalesReportRowViewModel
{
    public required string Label { get; init; }
    public required string CountStr { get; init; }
    public required string CashStr { get; init; }
    public required string Card1Str { get; init; }
    public required string Card2Str { get; init; }
    public required string InstallmentCountStr { get; init; }
    public required string AmountStr { get; init; }
}
```

`src/FishingMartPos/ViewModels/SalesReportViewModel.cs`의 `rows` 생성 부분(101-110번째 줄, `Select(g => new SalesReportRowViewModel { ... })`)을 아래로 교체:

```csharp
            .Select(g => new SalesReportRowViewModel
            {
                Label = IsDailyTab ? g.Key.ToString("yyyy-MM-dd") : g.Key.ToString("yyyy-MM") + " 월",
                CountStr = g.Count().ToString("N0") + "건",
                CashStr = CurrencyFormat.Format(g.Where(s => s.PayType == "CASH").Sum(s => s.TotalAmt)),
                Card1Str = CurrencyFormat.Format(g.Where(s => s.PayType == "CARD1").Sum(s => s.TotalAmt)),
                Card2Str = CurrencyFormat.Format(g.Where(s => s.PayType == "CARD2").Sum(s => s.TotalAmt)),
                InstallmentCountStr = g.Count(s => s.InstallmentMonths > 0).ToString("N0") + "건",
                AmountStr = CurrencyFormat.Format(g.Sum(s => s.TotalAmt)),
            })
```

- [ ] **Step 4: 테스트 실행 → 통과 확인**

Run: `dotnet test tests/FishingMartPos.Tests --filter "FullyQualifiedName~SalesReportViewModelTests"`
Expected: PASS

- [ ] **Step 5: `SalesReportView.xaml`에 "할부 건수" 컬럼 추가**

`src/FishingMartPos/Views/SalesReportView.xaml`의 헤더 `Grid`(128-142번째 줄)를 아래로 교체:

```xml
                    <Grid Margin="16,10">
                        <Grid.ColumnDefinitions>
                            <ColumnDefinition Width="1.4*" />
                            <ColumnDefinition Width="0.8*" />
                            <ColumnDefinition Width="1.1*" />
                            <ColumnDefinition Width="1.1*" />
                            <ColumnDefinition Width="1.1*" />
                            <ColumnDefinition Width="0.9*" />
                            <ColumnDefinition Width="1.1*" />
                        </Grid.ColumnDefinitions>
                        <TextBlock Text="날짜" FontSize="12" FontWeight="Bold" Foreground="{DynamicResource MutedText}" />
                        <TextBlock Grid.Column="1" Text="건수" FontSize="12" FontWeight="Bold" Foreground="{DynamicResource MutedText}" HorizontalAlignment="Right" />
                        <TextBlock Grid.Column="2" Text="현금" FontSize="12" FontWeight="Bold" Foreground="{DynamicResource MutedText}" HorizontalAlignment="Right" />
                        <TextBlock Grid.Column="3" Text="카드결제1" FontSize="12" FontWeight="Bold" Foreground="{DynamicResource MutedText}" HorizontalAlignment="Right" />
                        <TextBlock Grid.Column="4" Text="카드결제2" FontSize="12" FontWeight="Bold" Foreground="{DynamicResource MutedText}" HorizontalAlignment="Right" />
                        <TextBlock Grid.Column="5" Text="할부건수" FontSize="12" FontWeight="Bold" Foreground="{DynamicResource MutedText}" HorizontalAlignment="Right" />
                        <TextBlock Grid.Column="6" Text="합계" FontSize="12" FontWeight="Bold" Foreground="{DynamicResource MutedText}" HorizontalAlignment="Right" />
                    </Grid>
```

같은 파일의 행 템플릿 `Grid`(164-179번째 줄)를 아래로 교체:

```xml
                                    <Grid Margin="16,8">
                                        <Grid.ColumnDefinitions>
                                            <ColumnDefinition Width="1.4*" />
                                            <ColumnDefinition Width="0.8*" />
                                            <ColumnDefinition Width="1.1*" />
                                            <ColumnDefinition Width="1.1*" />
                                            <ColumnDefinition Width="1.1*" />
                                            <ColumnDefinition Width="0.9*" />
                                            <ColumnDefinition Width="1.1*" />
                                        </Grid.ColumnDefinitions>
                                        <TextBlock Text="{Binding Label}" FontSize="13" FontWeight="Bold" Foreground="{DynamicResource TitleText}" />
                                        <TextBlock Grid.Column="1" Text="{Binding CountStr}" FontSize="12" Foreground="{DynamicResource MutedText}" HorizontalAlignment="Right" />
                                        <TextBlock Grid.Column="2" Text="{Binding CashStr}" FontSize="12" HorizontalAlignment="Right" />
                                        <TextBlock Grid.Column="3" Text="{Binding Card1Str}" FontSize="12" HorizontalAlignment="Right" />
                                        <TextBlock Grid.Column="4" Text="{Binding Card2Str}" FontSize="12" HorizontalAlignment="Right" />
                                        <TextBlock Grid.Column="5" Text="{Binding InstallmentCountStr}" FontSize="12" Foreground="{DynamicResource MutedText}" HorizontalAlignment="Right" />
                                        <TextBlock Grid.Column="6" Text="{Binding AmountStr}" FontSize="13" FontWeight="Bold" HorizontalAlignment="Right" />
                                    </Grid>
```

- [ ] **Step 6: 스모크 테스트 + 빌드 확인**

Run: `dotnet test tests/FishingMartPos.Tests --filter "FullyQualifiedName~SalesReportViewSmokeTests"`
Expected: PASS

Run: `dotnet build FishingMartPos.sln`
Expected: 0 경고 / 0 오류

- [ ] **Step 7: 전체 테스트 실행 후 커밋**

Run: `dotnet test tests/FishingMartPos.Tests`
Expected: 0 실패

```bash
git add src/FishingMartPos/ViewModels/SalesReportRowViewModel.cs src/FishingMartPos/ViewModels/SalesReportViewModel.cs src/FishingMartPos/Views/SalesReportView.xaml tests/FishingMartPos.Tests/ViewModels/SalesReportViewModelTests.cs
git commit -m "매출관리 일/월별 집계에 할부 건수 컬럼 추가"
```

---

## 최종 확인 (수동 GUI 스모크테스트)

모든 태스크 완료 후, 컨트롤러가 직접 실행 중인 앱에서 확인한다(서브에이전트는 GUI 접근이 없으므로 이 항목은 컨트롤러 담당):

1. 앱 실행 → 판매 화면 → 상품 담기 → "현금결제" 클릭 → 계산합계/받은금액/거스름돈 팝업이 뜨는지, "취소"가 팝업만 닫고 장바구니가 유지되는지, "계산완료"가 매출을 저장하고 장바구니를 비우는지.
2. 상품 담기 → "카드결제1" 클릭 → 할부개월 선택 팝업이 뜨는지, 옵션 클릭 시 강조 표시가 바뀌는지, "기타개월" 선택 시 텍스트박스가 활성화되고 숫자 입력이 반영되는지, "취소" 시 게이트웨이 호출 없이 장바구니가 유지되는지.
3. "승인요청" 클릭 → "카드 리더기에 카드를 꽂아주세요 / 승인 처리 중입니다" 문구로 바뀌는지(스텁 게이트웨이라 1.5초 후 승인/거절 토스트로 이어짐) → 승인 시 매출관리에서 해당 건이 "할부 건수"에 반영되는지.
4. 카드결제 팝업이 떠 있는 동안 바코드를 스캔(또는 숫자+Enter)해도 `등록되지 않은 바코드입니다` 토스트나 의도치 않은 `ScanBarcodeCommand` 실행이 일어나지 않는지.
