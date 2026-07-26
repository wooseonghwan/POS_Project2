# 결제관리 / 직전정보 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** POS 화면의 비활성 "직전정보" 버튼을 활성화하고, 메인메뉴에 "결제관리" 화면을 추가해 — 두 진입점 모두에서 — 거래 조회, 취소(카드 D4 / 현금영수증 B2), 현금영수증으로 변경, 영수증 재발행을 할 수 있게 한다.

**Architecture:** 조회/취소/변경/재발행 로직은 `TransactionDetailViewModel`이라는 하나의 공용 하위 뷰모델에 모아두고, `PosViewModel`(직전정보)과 신규 `PaymentManagementViewModel`(결제관리) 둘 다 이 뷰모델의 인스턴스를 생성해서 쓴다. 화면도 `TransactionDetailPopup`이라는 공용 `UserControl` 하나로 공유한다. 카드취소는 KICC `D4`(이 단말기는 D2가 없음), 현금영수증취소는 기존에 만들어둔 `B2` 메시지 빌더를 그대로 재사용한다.

**Tech Stack:** .NET 8 WPF/MVVM(CommunityToolkit.Mvvm), Dapper+MySqlConnector, xUnit.

## Global Constraints

- 카드취소는 `D4` 코드 하나만 사용한다(D2는 이 단말기 스펙에 없음 — `docs/superpowers/specs/2026-07-26-payment-management-design.md`의 "KICC 연동 정정 사항" 참고).
- 취소 성공 시 재고를 자동 복구한다(판매 시 차감한 수량만큼 `product_tb.stock_qty`를 다시 더한다).
- 이미 취소된 건은 재취소할 수 없다(버튼 비활성화).
- "현금영수증으로 변경하기"는 `PayType == "CASH"` 이고 `CashReceiptType == "NONE"`인 경우에만 활성화된다.
- 이 기능 전체에 ADMIN 게이트가 없다 — STAFF도 모두 사용 가능(재고 화면과 동일한 접근 레벨).
- 영수증 재발행/인쇄는 기존 `IReceiptPrinter` 스텁을 그대로 쓴다(실 프린터 연동은 범위 밖).
- 기존 리포지토리/게이트웨이 메서드 시그니처는 변경하지 않고 새 메서드만 추가한다(하위 호환).

---

## Task 1: SaleHeader 확장 + SalesRepository 조회/변경 메서드

**Files:**
- Modify: `src/FishingMartPos/Models/SaleHeader.cs`
- Modify: `src/FishingMartPos/Repositories/ISalesRepository.cs`
- Modify: `src/FishingMartPos/Repositories/SalesRepository.cs`
- Modify: `tests/FishingMartPos.Tests/Fakes/FakeSalesRepository.cs`
- Test: `tests/FishingMartPos.Tests/Repositories/SalesRepositoryPaymentManagementTests.cs` (신규)

**Interfaces:**
- Produces: `SaleHeader.SaleNo`(long), `SaleHeader.Status`(string, 기본 `"COMPLETE"`), `SaleHeader`가 `sealed record`로 변경(값 복사용 `with` 지원).
- Produces: `ISalesRepository.GetLastCompletedSaleAsync(string posCd)`, `SearchSalesAsync(DateTime from, DateTime to, string? payType, string? approvalNo)`, `GetSaleWithLinesAsync(long saleNo)`, `CancelSaleAsync(long saleNo)`, `UpdateCashReceiptAsync(long saleNo, string receiptType, string merchant, string approvalNo, string approvalDateYyMmDd)`.

- [ ] **Step 1: `SaleHeader`를 `sealed record`로 바꾸고 `SaleNo`/`Status` 추가**

`src/FishingMartPos/Models/SaleHeader.cs` 전체를 다음으로 교체:

```csharp
namespace FishingMartPos.Models;

public sealed record SaleHeader
{
    public long SaleNo { get; init; }
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
    public string? CashReceiptApprovalDate { get; init; } // YYMMDD — B2(현금영수증 취소)에 사용
    public string Status { get; init; } = "COMPLETE"; // "COMPLETE" / "CANCELLED"
}
```

`record`로 바뀌어도 `required`/`init` 프로퍼티는 그대로 지원되고, Dapper는 이런 타입에도 그대로 매핑되며, 기존 `new SaleHeader { ... }` 생성 코드는 전부 그대로 컴파일된다(`SaleNo`/`Status`는 필수가 아니므로 기존 생성 코드에 영향 없음).

- [ ] **Step 2: `ISalesRepository`에 메서드 5개 추가**

`src/FishingMartPos/Repositories/ISalesRepository.cs` 전체를 다음으로 교체:

```csharp
using FishingMartPos.Models;

namespace FishingMartPos.Repositories;

public interface ISalesRepository
{
    Task<long> CreateSaleAsync(SaleHeader header, IReadOnlyList<SaleDetailLine> lines);

    Task<IReadOnlyList<SaleHeader>> GetCompletedSalesAsync(DateTime from, DateTime to);

    Task<SaleHeader?> GetLastCompletedSaleAsync(string posCd);

    Task<IReadOnlyList<SaleHeader>> SearchSalesAsync(DateTime from, DateTime to, string? payType, string? approvalNo);

    Task<(SaleHeader Header, IReadOnlyList<SaleDetailLine> Lines)?> GetSaleWithLinesAsync(long saleNo);

    Task CancelSaleAsync(long saleNo);

    Task UpdateCashReceiptAsync(long saleNo, string receiptType, string merchant, string approvalNo, string approvalDateYyMmDd);
}
```

- [ ] **Step 3: `SalesRepository`에 5개 메서드 구현 + 기존 SELECT에 `sale_no`/`status` 추가**

`src/FishingMartPos/Repositories/SalesRepository.cs` 전체를 다음으로 교체:

```csharp
using Dapper;
using FishingMartPos.Data;
using FishingMartPos.Models;

namespace FishingMartPos.Repositories;

public sealed class SalesRepository : ISalesRepository
{
    private const string HeaderColumns = """
        sale_no AS SaleNo, pos_cd AS PosCd, sale_dt AS SaleDt, staff_cd AS StaffCd, total_amt AS TotalAmt,
        pay_type AS PayType, cash_received AS CashReceived, change_amt AS ChangeAmt,
        van_approval_no AS VanApprovalNo, van_code AS VanCode, installment_months AS InstallmentMonths,
        cash_receipt_type AS CashReceiptType, cash_receipt_merchant AS CashReceiptMerchant,
        cash_receipt_approval_no AS CashReceiptApprovalNo, cash_receipt_approval_date AS CashReceiptApprovalDate,
        status AS Status
        """;

    private readonly IDbConnectionFactory _connectionFactory;

    public SalesRepository(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<long> CreateSaleAsync(SaleHeader header, IReadOnlyList<SaleDetailLine> lines)
    {
        using var connection = await _connectionFactory.CreateOpenConnectionAsync();
        using var transaction = connection.BeginTransaction();

        const string insertHeaderSql = """
            INSERT INTO sales_header_tb
                (pos_cd, sale_dt, staff_cd, total_amt, pay_type, cash_received, change_amt, van_approval_no, van_code, installment_months, cash_receipt_type, cash_receipt_merchant, cash_receipt_approval_no, cash_receipt_approval_date, status)
            VALUES
                (@PosCd, @SaleDt, @StaffCd, @TotalAmt, @PayType, @CashReceived, @ChangeAmt, @VanApprovalNo, @VanCode, @InstallmentMonths, @CashReceiptType, @CashReceiptMerchant, @CashReceiptApprovalNo, @CashReceiptApprovalDate, 'COMPLETE')
            """;
        await connection.ExecuteAsync(insertHeaderSql, header, transaction);
        long saleNo = await connection.QuerySingleAsync<long>("SELECT LAST_INSERT_ID()", transaction: transaction);

        if (lines.Count > 0)
        {
            var detailParams = new DynamicParameters();
            detailParams.Add("SaleNo", saleNo);
            var valueRows = new List<string>(lines.Count);
            int lineNo = 1;
            foreach (var line in lines)
            {
                valueRows.Add($"(@SaleNo, @LineNo{lineNo}, @Barcode{lineNo}, @ProductName{lineNo}, @Qty{lineNo}, @UnitPrice{lineNo}, @LineAmt{lineNo})");
                detailParams.Add($"LineNo{lineNo}", lineNo);
                detailParams.Add($"Barcode{lineNo}", line.Barcode);
                detailParams.Add($"ProductName{lineNo}", line.ProductName);
                detailParams.Add($"Qty{lineNo}", line.Qty);
                detailParams.Add($"UnitPrice{lineNo}", line.UnitPrice);
                detailParams.Add($"LineAmt{lineNo}", line.LineAmt);
                lineNo++;
            }

            string insertDetailSql = $"""
                INSERT INTO sales_detail_tb (sale_no, line_no, barcode, product_name, qty, unit_price, line_amt)
                VALUES {string.Join(", ", valueRows)}
                """;
            await connection.ExecuteAsync(insertDetailSql, detailParams, transaction);

            const string decrementStockSql = """
                UPDATE product_tb SET stock_qty = stock_qty - @Qty WHERE barcode = @Barcode
                """;
            foreach (var line in lines)
            {
                await connection.ExecuteAsync(decrementStockSql, new { line.Qty, line.Barcode }, transaction);
            }
        }

        transaction.Commit();
        return saleNo;
    }

    public async Task<IReadOnlyList<SaleHeader>> GetCompletedSalesAsync(DateTime from, DateTime to)
    {
        using var connection = await _connectionFactory.CreateOpenConnectionAsync();
        string sql = $"""
            SELECT {HeaderColumns}
            FROM sales_header_tb
            WHERE sale_dt >= @From AND sale_dt < @To AND status = 'COMPLETE'
            """;
        var rows = await connection.QueryAsync<SaleHeader>(sql, new { From = from, To = to });
        return rows.ToList();
    }

    public async Task<SaleHeader?> GetLastCompletedSaleAsync(string posCd)
    {
        using var connection = await _connectionFactory.CreateOpenConnectionAsync();
        string sql = $"""
            SELECT {HeaderColumns}
            FROM sales_header_tb
            WHERE pos_cd = @PosCd AND status = 'COMPLETE'
            ORDER BY sale_no DESC
            LIMIT 1
            """;
        return await connection.QuerySingleOrDefaultAsync<SaleHeader>(sql, new { PosCd = posCd });
    }

    public async Task<IReadOnlyList<SaleHeader>> SearchSalesAsync(DateTime from, DateTime to, string? payType, string? approvalNo)
    {
        using var connection = await _connectionFactory.CreateOpenConnectionAsync();
        var conditions = new List<string> { "sale_dt >= @From", "sale_dt < @To" };
        var parameters = new DynamicParameters();
        parameters.Add("From", from);
        parameters.Add("To", to);

        if (!string.IsNullOrWhiteSpace(payType))
        {
            conditions.Add("pay_type = @PayType");
            parameters.Add("PayType", payType);
        }

        if (!string.IsNullOrWhiteSpace(approvalNo))
        {
            conditions.Add("(van_approval_no = @ApprovalNo OR cash_receipt_approval_no = @ApprovalNo)");
            parameters.Add("ApprovalNo", approvalNo);
        }

        string sql = $"""
            SELECT {HeaderColumns}
            FROM sales_header_tb
            WHERE {string.Join(" AND ", conditions)}
            ORDER BY sale_no DESC
            """;
        var rows = await connection.QueryAsync<SaleHeader>(sql, parameters);
        return rows.ToList();
    }

    public async Task<(SaleHeader Header, IReadOnlyList<SaleDetailLine> Lines)?> GetSaleWithLinesAsync(long saleNo)
    {
        using var connection = await _connectionFactory.CreateOpenConnectionAsync();
        string headerSql = $"SELECT {HeaderColumns} FROM sales_header_tb WHERE sale_no = @SaleNo";
        var header = await connection.QuerySingleOrDefaultAsync<SaleHeader>(headerSql, new { SaleNo = saleNo });
        if (header is null) return null;

        const string linesSql = """
            SELECT barcode AS Barcode, product_name AS ProductName, qty AS Qty, unit_price AS UnitPrice
            FROM sales_detail_tb WHERE sale_no = @SaleNo ORDER BY line_no
            """;
        var lines = await connection.QueryAsync<SaleDetailLine>(linesSql, new { SaleNo = saleNo });
        return (header, lines.ToList());
    }

    public async Task CancelSaleAsync(long saleNo)
    {
        using var connection = await _connectionFactory.CreateOpenConnectionAsync();
        using var transaction = connection.BeginTransaction();

        const string cancelSql = "UPDATE sales_header_tb SET status = 'CANCELLED' WHERE sale_no = @SaleNo AND status = 'COMPLETE'";
        int affected = await connection.ExecuteAsync(cancelSql, new { SaleNo = saleNo }, transaction);
        if (affected == 0)
        {
            transaction.Rollback();
            return;
        }

        const string linesSql = """
            SELECT barcode AS Barcode, product_name AS ProductName, qty AS Qty, unit_price AS UnitPrice
            FROM sales_detail_tb WHERE sale_no = @SaleNo
            """;
        var lines = await connection.QueryAsync<SaleDetailLine>(linesSql, new { SaleNo = saleNo }, transaction);

        const string restockSql = "UPDATE product_tb SET stock_qty = stock_qty + @Qty WHERE barcode = @Barcode";
        foreach (var line in lines)
        {
            await connection.ExecuteAsync(restockSql, new { line.Qty, line.Barcode }, transaction);
        }

        transaction.Commit();
    }

    public async Task UpdateCashReceiptAsync(long saleNo, string receiptType, string merchant, string approvalNo, string approvalDateYyMmDd)
    {
        using var connection = await _connectionFactory.CreateOpenConnectionAsync();
        const string sql = """
            UPDATE sales_header_tb
            SET cash_receipt_type = @ReceiptType, cash_receipt_merchant = @Merchant,
                cash_receipt_approval_no = @ApprovalNo, cash_receipt_approval_date = @ApprovalDate
            WHERE sale_no = @SaleNo
            """;
        await connection.ExecuteAsync(sql, new
        {
            SaleNo = saleNo,
            ReceiptType = receiptType,
            Merchant = merchant,
            ApprovalNo = approvalNo,
            ApprovalDate = approvalDateYyMmDd,
        });
    }
}
```

- [ ] **Step 4: `FakeSalesRepository` 확장**

`tests/FishingMartPos.Tests/Fakes/FakeSalesRepository.cs` 전체를 다음으로 교체:

```csharp
using FishingMartPos.Models;
using FishingMartPos.Repositories;

namespace FishingMartPos.Tests.Fakes;

public sealed class FakeSalesRepository : ISalesRepository
{
    public List<(SaleHeader Header, IReadOnlyList<SaleDetailLine> Lines)> CreatedSales { get; } = new();
    public List<(DateTime From, DateTime To)> GetCompletedSalesCalls { get; } = new();
    public List<long> CancelledSaleNos { get; } = new();
    public List<(long SaleNo, string ReceiptType, string Merchant, string ApprovalNo, string ApprovalDateYyMmDd)> CashReceiptUpdates { get; } = new();

    private long _nextSaleNo = 1;
    private IReadOnlyList<SaleHeader> _completedSales = Array.Empty<SaleHeader>();
    private SaleHeader? _lastCompletedSale;
    private readonly Dictionary<long, (SaleHeader Header, IReadOnlyList<SaleDetailLine> Lines)> _salesByNo = new();

    public Task<long> CreateSaleAsync(SaleHeader header, IReadOnlyList<SaleDetailLine> lines)
    {
        CreatedSales.Add((header, lines));
        return Task.FromResult(_nextSaleNo++);
    }

    public void SeedCompletedSales(IReadOnlyList<SaleHeader> sales)
    {
        _completedSales = sales;
    }

    public void SeedLastCompletedSale(SaleHeader? sale)
    {
        _lastCompletedSale = sale;
    }

    public void SeedSaleWithLines(long saleNo, SaleHeader header, IReadOnlyList<SaleDetailLine> lines)
    {
        _salesByNo[saleNo] = (header, lines);
    }

    public Task<IReadOnlyList<SaleHeader>> GetCompletedSalesAsync(DateTime from, DateTime to)
    {
        GetCompletedSalesCalls.Add((from, to));
        var filtered = _completedSales.Where(s => s.SaleDt >= from && s.SaleDt < to).ToList();
        return Task.FromResult<IReadOnlyList<SaleHeader>>(filtered);
    }

    public Task<SaleHeader?> GetLastCompletedSaleAsync(string posCd) => Task.FromResult(_lastCompletedSale);

    public Task<IReadOnlyList<SaleHeader>> SearchSalesAsync(DateTime from, DateTime to, string? payType, string? approvalNo)
    {
        var filtered = _completedSales
            .Where(s => s.SaleDt >= from && s.SaleDt < to)
            .Where(s => payType is null || s.PayType == payType)
            .Where(s => approvalNo is null || s.VanApprovalNo == approvalNo || s.CashReceiptApprovalNo == approvalNo)
            .ToList();
        return Task.FromResult<IReadOnlyList<SaleHeader>>(filtered);
    }

    public Task<(SaleHeader Header, IReadOnlyList<SaleDetailLine> Lines)?> GetSaleWithLinesAsync(long saleNo)
    {
        (SaleHeader Header, IReadOnlyList<SaleDetailLine> Lines)? result =
            _salesByNo.TryGetValue(saleNo, out var value) ? value : null;
        return Task.FromResult(result);
    }

    public Task CancelSaleAsync(long saleNo)
    {
        CancelledSaleNos.Add(saleNo);
        return Task.CompletedTask;
    }

    public Task UpdateCashReceiptAsync(long saleNo, string receiptType, string merchant, string approvalNo, string approvalDateYyMmDd)
    {
        CashReceiptUpdates.Add((saleNo, receiptType, merchant, approvalNo, approvalDateYyMmDd));
        return Task.CompletedTask;
    }
}
```

- [ ] **Step 5: 실제 dev DB로 신규 메서드 통합 테스트 작성**

`tests/FishingMartPos.Tests/Repositories/SalesRepositoryPaymentManagementTests.cs` 신규 생성. 기존 `SalesRepositoryTests.cs`가 실제 dev DB 커넥션을 어떻게 얻는지 먼저 확인하고 동일한 방식(`AppConfig.Load` + `MySqlConnectionFactory`)으로 작성한다:

```csharp
using FishingMartPos.Configuration;
using FishingMartPos.Data;
using FishingMartPos.Models;
using FishingMartPos.Repositories;
using Xunit;

namespace FishingMartPos.Tests.Repositories;

public class SalesRepositoryPaymentManagementTests
{
    private static SalesRepository CreateRepository()
    {
        var config = AppConfig.Load(AppContext.BaseDirectory);
        var connectionFactory = new MySqlConnectionFactory(config);
        return new SalesRepository(connectionFactory);
    }

    private static SaleHeader NewHeader(string payType = "CASH") => new()
    {
        PosCd = "9",
        SaleDt = DateTime.Now,
        StaffCd = "ADMIN1",
        TotalAmt = 5000m,
        PayType = payType,
        CashReceived = payType == "CASH" ? 5000m : null,
        ChangeAmt = payType == "CASH" ? 0m : null,
        VanApprovalNo = payType != "CASH" ? "TESTAPPROVAL1" : null,
        VanCode = payType != "CASH" ? "KICC" : null,
    };

    private static List<SaleDetailLine> OneLine() => new()
    {
        new SaleDetailLine { Barcode = "TESTBARCODE1", ProductName = "테스트상품", Qty = 2, UnitPrice = 1000m },
    };

    [Fact]
    public async Task GetLastCompletedSaleAsync_ReturnsMostRecentSaleForPosCode()
    {
        var repo = CreateRepository();
        long firstSaleNo = await repo.CreateSaleAsync(NewHeader(), OneLine());
        long secondSaleNo = await repo.CreateSaleAsync(NewHeader(), OneLine());

        var result = await repo.GetLastCompletedSaleAsync("9");

        Assert.NotNull(result);
        Assert.Equal(secondSaleNo, result!.SaleNo);
        Assert.True(result.SaleNo >= firstSaleNo);
    }

    [Fact]
    public async Task GetSaleWithLinesAsync_ReturnsHeaderAndLines()
    {
        var repo = CreateRepository();
        long saleNo = await repo.CreateSaleAsync(NewHeader(), OneLine());

        var result = await repo.GetSaleWithLinesAsync(saleNo);

        Assert.NotNull(result);
        Assert.Equal(saleNo, result!.Value.Header.SaleNo);
        Assert.Single(result.Value.Lines);
        Assert.Equal("TESTBARCODE1", result.Value.Lines[0].Barcode);
    }

    [Fact]
    public async Task SearchSalesAsync_FiltersByPayTypeAndApprovalNo()
    {
        var repo = CreateRepository();
        await repo.CreateSaleAsync(NewHeader("CASH"), OneLine());
        await repo.CreateSaleAsync(NewHeader("CARD1"), OneLine());

        var from = DateTime.Today;
        var to = DateTime.Today.AddDays(1);

        var cashOnly = await repo.SearchSalesAsync(from, to, "CASH", null);
        Assert.All(cashOnly, s => Assert.Equal("CASH", s.PayType));

        var byApproval = await repo.SearchSalesAsync(from, to, null, "TESTAPPROVAL1");
        Assert.All(byApproval, s => Assert.Equal("TESTAPPROVAL1", s.VanApprovalNo));
    }

    [Fact]
    public async Task CancelSaleAsync_MarksCancelledAndRestocksProduct()
    {
        var repo = CreateRepository();
        long saleNo = await repo.CreateSaleAsync(NewHeader(), OneLine());

        await repo.CancelSaleAsync(saleNo);

        var result = await repo.GetSaleWithLinesAsync(saleNo);
        Assert.Equal("CANCELLED", result!.Value.Header.Status);
    }

    [Fact]
    public async Task CancelSaleAsync_WhenAlreadyCancelled_IsNoOp()
    {
        var repo = CreateRepository();
        long saleNo = await repo.CreateSaleAsync(NewHeader(), OneLine());
        await repo.CancelSaleAsync(saleNo);

        await repo.CancelSaleAsync(saleNo); // 두 번째 취소 — 예외 없이 조용히 무시되어야 함

        var result = await repo.GetSaleWithLinesAsync(saleNo);
        Assert.Equal("CANCELLED", result!.Value.Header.Status);
    }

    [Fact]
    public async Task UpdateCashReceiptAsync_UpdatesHeaderFields()
    {
        var repo = CreateRepository();
        long saleNo = await repo.CreateSaleAsync(NewHeader(), OneLine());

        await repo.UpdateCashReceiptAsync(saleNo, "PERSONAL", "CARD1", "APPROVAL999", "260726");

        var result = await repo.GetSaleWithLinesAsync(saleNo);
        Assert.Equal("PERSONAL", result!.Value.Header.CashReceiptType);
        Assert.Equal("CARD1", result.Value.Header.CashReceiptMerchant);
        Assert.Equal("APPROVAL999", result.Value.Header.CashReceiptApprovalNo);
    }
}
```

**주의:** `NewHeader`가 사용하는 `barcode = "TESTBARCODE1"`이 `product_tb`에 존재하지 않으면 `CancelSaleAsync`의 재입고 UPDATE는 그냥 0 rows affected로 조용히 지나간다(에러 없음) — 재고 수량 자체를 검증하고 싶다면 기존 `SalesRepositoryTests.cs`가 테스트용 상품을 어떻게 세팅하는지 확인해서 동일한 바코드/상품을 재사용할 것.

- [ ] **Step 6: 빌드 + 테스트 실행**

Run: `dotnet build src/FishingMartPos/FishingMartPos.csproj` (0 경고/0 오류 확인 — `SaleHeader`가 `record`로 바뀌면서 컴파일 에러가 나는 다른 파일이 있는지 반드시 확인)
Run: `dotnet test tests/FishingMartPos.Tests`
Expected: 전체 통과 (DB 연결이 필요한 신규 통합 테스트 포함)

- [ ] **Step 7: 커밋**

```bash
git add src/FishingMartPos/Models/SaleHeader.cs src/FishingMartPos/Repositories/ISalesRepository.cs src/FishingMartPos/Repositories/SalesRepository.cs tests/FishingMartPos.Tests/Fakes/FakeSalesRepository.cs tests/FishingMartPos.Tests/Repositories/SalesRepositoryPaymentManagementTests.cs
git commit -m "SaleHeader에 SaleNo/Status 추가, 결제관리용 SalesRepository 조회/취소 메서드 추가"
```

---

## Task 2: 카드취소(D4) — IVanPaymentGateway 확장

**Files:**
- Modify: `src/FishingMartPos/Services/IVanPaymentGateway.cs`
- Modify: `src/FishingMartPos/Services/StubVanPaymentGateway.cs`
- Modify: `src/FishingMartPos/Services/Kicc/KiccMessageBuilder.cs`
- Modify: `src/FishingMartPos/Services/Kicc/KiccVanPaymentGateway.cs`
- Modify: `tests/FishingMartPos.Tests/Fakes/FakeVanPaymentGateway.cs`
- Test: `tests/FishingMartPos.Tests/Services/Kicc/KiccMessageBuilderTests.cs`
- Test: `tests/FishingMartPos.Tests/Services/Kicc/KiccVanPaymentGatewayTests.cs`
- Test: `tests/FishingMartPos.Tests/Services/StubVanPaymentGatewayTests.cs`

**Interfaces:**
- Consumes: Task 1의 `SaleHeader`(취소 시 필요한 `VanApprovalNo`/`SaleDt`/`InstallmentMonths`/`TotalAmt` 필드).
- Produces: `IVanPaymentGateway.RequestCancelAsync(VanCancelRequest)`, `VanCancelRequest(string PosCode, string PayType, decimal Amount, int InstallmentMonths, string OriginalApprovalNo, string OriginalApprovalDateYyMmDd)`, `VanCancelResult { bool IsCancelled, string ResponseMessage }`.

- [ ] **Step 1: `IVanPaymentGateway`에 취소 메서드 추가**

`src/FishingMartPos/Services/IVanPaymentGateway.cs` 전체를 다음으로 교체:

```csharp
namespace FishingMartPos.Services;

public interface IVanPaymentGateway
{
    Task<VanApprovalResult> RequestApprovalAsync(VanApprovalRequest request);

    Task<VanCancelResult> RequestCancelAsync(VanCancelRequest request);
}

public sealed record VanApprovalRequest(string PosCode, string PayType, decimal Amount, int InstallmentMonths = 0);

public sealed class VanApprovalResult
{
    public required bool IsApproved { get; init; }
    public string? ApprovalNo { get; init; }
    public string? VanCode { get; init; }
    public required string ResponseMessage { get; init; }
}

public sealed record VanCancelRequest(
    string PosCode, string PayType, decimal Amount, int InstallmentMonths,
    string OriginalApprovalNo, string OriginalApprovalDateYyMmDd);

public sealed class VanCancelResult
{
    public required bool IsCancelled { get; init; }
    public required string ResponseMessage { get; init; }
}
```

- [ ] **Step 2: `KiccMessageBuilder.BuildCardCancelRequest` 추가**

`src/FishingMartPos/Services/Kicc/KiccMessageBuilder.cs`에서 `BuildApprovalRequest` 메서드 바로 아래에 추가:

```csharp
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

- [ ] **Step 3: `KiccMessageBuilder.BuildCardCancelRequest` 테스트 추가**

`tests/FishingMartPos.Tests/Services/Kicc/KiccMessageBuilderTests.cs`에 기존 `BuildApprovalRequest` 테스트 클래스를 참고해 추가(기존 파일이 클래스별로 나뉘어 있으면 같은 파일 안에 새 `[Fact]`들을 추가):

```csharp
    [Fact]
    public void BuildCardCancelRequest_UsesD4CommandCode()
    {
        var merchant = new KiccMerchantConfig("CARD1", "2977338", "3169055788");

        var sendData = KiccMessageBuilder.BuildCardCancelRequest(merchant, 5000m, 0, "99145616", "260726", "1260726120000012");

        Assert.Contains("S01=D4;", sendData);
        Assert.Contains("S03=2977338;S04=3169055788;", sendData);
        Assert.Contains("S12=99145616;", sendData);
        Assert.Contains("S13=260726;", sendData);
    }

    [Fact]
    public void BuildCardCancelRequest_WithInstallmentMonths_FormatsAsTwoDigits()
    {
        var merchant = new KiccMerchantConfig("CARD1", "2977338", "3169055788");

        var sendData = KiccMessageBuilder.BuildCardCancelRequest(merchant, 60000m, 3, "99145616", "260726", "1260726120000012");

        Assert.Contains("S09=03;", sendData);
    }
```

- [ ] **Step 4: 테스트 실행 (실패 확인)**

Run: `dotnet test tests/FishingMartPos.Tests --filter KiccMessageBuilderTests`
Expected: 새 2건 FAIL (`BuildCardCancelRequest` 없음)

- [ ] **Step 5: `StubVanPaymentGateway`에 취소 구현 추가**

`src/FishingMartPos/Services/StubVanPaymentGateway.cs`에서 `RequestApprovalAsync` 메서드 바로 아래에 추가:

```csharp
    public async Task<VanCancelResult> RequestCancelAsync(VanCancelRequest request)
    {
        await _delay.Delay(TimeSpan.FromMilliseconds(1000));
        return new VanCancelResult { IsCancelled = true, ResponseMessage = "카드 결제 취소 완료" };
    }
```

- [ ] **Step 6: `KiccVanPaymentGateway`에 취소 구현 추가**

`src/FishingMartPos/Services/Kicc/KiccVanPaymentGateway.cs`에서 `RequestApprovalAsync` 메서드 바로 아래에 추가:

```csharp
    public async Task<VanCancelResult> RequestCancelAsync(VanCancelRequest request)
    {
        var merchant = _merchantsByPayType[request.PayType];
        var posTranNo = BuildPosTranNo(request.PosCode);
        var sendData = KiccMessageBuilder.BuildCardCancelRequest(
            merchant, request.Amount, request.InstallmentMonths,
            request.OriginalApprovalNo, request.OriginalApprovalDateYyMmDd, posTranNo);

        var raw = await _client.RequestAsync(0xFB, 0x14, 0x04, sendData);

        if (!raw.IsSuccess)
        {
            return new VanCancelResult { IsCancelled = false, ResponseMessage = raw.FailureMessage ?? "카드 취소 거절" };
        }

        var fields = KiccResponseParser.Parse(raw.Data!);
        if (fields.TryGetValue("R04", out var rc) && rc == "0000")
        {
            return new VanCancelResult { IsCancelled = true, ResponseMessage = "카드 결제 취소 완료" };
        }

        return new VanCancelResult { IsCancelled = false, ResponseMessage = "카드 취소 거절" };
    }
```

- [ ] **Step 7: `KiccVanPaymentGateway` 취소 테스트 추가**

`tests/FishingMartPos.Tests/Services/Kicc/KiccVanPaymentGatewayTests.cs`의 클래스 안, 마지막 `[Fact]` 다음에 추가:

```csharp
    [Fact]
    public async Task RequestCancelAsync_WhenR04IsSuccess_ReturnsCancelled()
    {
        var client = new FakeKiccPosClient(KiccRawResponse.Success("R04=0000;"));
        var gateway = new KiccVanPaymentGateway(client, Merchants);

        var result = await gateway.RequestCancelAsync(new VanCancelRequest("1", "CARD1", 5000m, 0, "99145616", "260726"));

        Assert.True(result.IsCancelled);
    }

    [Fact]
    public async Task RequestCancelAsync_WhenR04IsNotSuccess_ReturnsNotCancelled()
    {
        var client = new FakeKiccPosClient(KiccRawResponse.Success("R04=1234;"));
        var gateway = new KiccVanPaymentGateway(client, Merchants);

        var result = await gateway.RequestCancelAsync(new VanCancelRequest("1", "CARD1", 5000m, 0, "99145616", "260726"));

        Assert.False(result.IsCancelled);
    }

    [Fact]
    public async Task RequestCancelAsync_SendsD4WithOriginalApprovalFields()
    {
        var client = new FakeKiccPosClient(KiccRawResponse.Success("R04=0000;"));
        var gateway = new KiccVanPaymentGateway(client, Merchants);

        await gateway.RequestCancelAsync(new VanCancelRequest("1", "CARD1", 5000m, 0, "99145616", "260726"));

        var sendData = Assert.Single(client.Requests).SendData;
        Assert.Contains("S01=D4;", sendData);
        Assert.Contains("S12=99145616;S13=260726;", sendData);
    }
```

- [ ] **Step 8: `StubVanPaymentGateway` 취소 테스트 추가**

`tests/FishingMartPos.Tests/Services/StubVanPaymentGatewayTests.cs`에 추가(기존 테스트가 `FakeDelayProvider`/`IVanOutcomeProvider` 픽스처를 어떻게 만드는지 확인하고 동일한 방식 사용):

```csharp
    [Fact]
    public async Task RequestCancelAsync_AlwaysReturnsCancelled()
    {
        var gateway = new StubVanPaymentGateway(new FakeDelayProvider(), new AlwaysApprovedOutcomeProvider());

        var result = await gateway.RequestCancelAsync(new VanCancelRequest("1", "CARD1", 5000m, 0, "99145616", "260726"));

        Assert.True(result.IsCancelled);
    }
```

(`FakeDelayProvider`/`AlwaysApprovedOutcomeProvider` 클래스 이름은 파일 상단의 기존 `RequestApprovalAsync` 테스트에서 실제로 쓰는 이름을 그대로 따라갈 것 — 다르면 그 이름으로 맞춰 쓴다.)

- [ ] **Step 9: `FakeVanPaymentGateway`에 취소 지원 추가**

`tests/FishingMartPos.Tests/Fakes/FakeVanPaymentGateway.cs` 전체를 다음으로 교체:

```csharp
using FishingMartPos.Services;

namespace FishingMartPos.Tests.Fakes;

public sealed class FakeVanPaymentGateway : IVanPaymentGateway
{
    private readonly VanApprovalResult _approvalResult;
    private readonly VanCancelResult _cancelResult;
    public List<VanApprovalRequest> Requests { get; } = new();
    public List<VanCancelRequest> CancelRequests { get; } = new();

    public FakeVanPaymentGateway(VanApprovalResult approvalResult, VanCancelResult? cancelResult = null)
    {
        _approvalResult = approvalResult;
        _cancelResult = cancelResult ?? new VanCancelResult { IsCancelled = true, ResponseMessage = "카드 결제 취소 완료" };
    }

    public Task<VanApprovalResult> RequestApprovalAsync(VanApprovalRequest request)
    {
        Requests.Add(request);
        return Task.FromResult(_approvalResult);
    }

    public Task<VanCancelResult> RequestCancelAsync(VanCancelRequest request)
    {
        CancelRequests.Add(request);
        return Task.FromResult(_cancelResult);
    }
}
```

- [ ] **Step 10: 빌드 + 테스트 실행**

Run: `dotnet build src/FishingMartPos/FishingMartPos.csproj`
Run: `dotnet test tests/FishingMartPos.Tests`
Expected: 전체 통과

- [ ] **Step 11: 커밋**

```bash
git add src/FishingMartPos/Services/IVanPaymentGateway.cs src/FishingMartPos/Services/StubVanPaymentGateway.cs src/FishingMartPos/Services/Kicc/KiccMessageBuilder.cs src/FishingMartPos/Services/Kicc/KiccVanPaymentGateway.cs tests/FishingMartPos.Tests/Fakes/FakeVanPaymentGateway.cs tests/FishingMartPos.Tests/Services/Kicc/KiccMessageBuilderTests.cs tests/FishingMartPos.Tests/Services/Kicc/KiccVanPaymentGatewayTests.cs tests/FishingMartPos.Tests/Services/StubVanPaymentGatewayTests.cs
git commit -m "IVanPaymentGateway에 카드취소(D4) 메서드 추가"
```

---

## Task 3: 현금영수증취소(B2) — ICashReceiptGateway 확장

**Files:**
- Modify: `src/FishingMartPos/Services/ICashReceiptGateway.cs`
- Modify: `src/FishingMartPos/Services/StubCashReceiptGateway.cs`
- Modify: `src/FishingMartPos/Services/Kicc/KiccCashReceiptGateway.cs`
- Modify: `tests/FishingMartPos.Tests/Fakes/FakeCashReceiptGateway.cs`
- Test: `tests/FishingMartPos.Tests/Services/Kicc/KiccCashReceiptGatewayTests.cs`
- Test: `tests/FishingMartPos.Tests/Services/StubCashReceiptGatewayTests.cs`

**Interfaces:**
- Consumes: 기존 `KiccMessageBuilder.BuildCashReceiptCancelRequest`(Feature A에서 이미 구현됨, 그대로 재사용).
- Produces: `ICashReceiptGateway.RequestCancelAsync(CashReceiptCancelRequest)`, `CashReceiptCancelRequest(string PosCode, string MerchantPayType, string ReceiptType, decimal Amount, string OriginalApprovalNo, string OriginalApprovalDateYyMmDd)`, `CashReceiptCancelResult { bool IsCancelled, string ResponseMessage }`.

- [ ] **Step 1: `ICashReceiptGateway`에 취소 메서드 추가**

`src/FishingMartPos/Services/ICashReceiptGateway.cs` 전체를 다음으로 교체:

```csharp
namespace FishingMartPos.Services;

public interface ICashReceiptGateway
{
    Task<CashReceiptResult> RequestIssueAsync(CashReceiptRequest request);

    Task<CashReceiptCancelResult> RequestCancelAsync(CashReceiptCancelRequest request);
}

public sealed record CashReceiptRequest(string PosCode, string MerchantPayType, string ReceiptType, decimal Amount);
// PosCode: S23(POS 거래번호) 조립에 사용 — 카드승인(VanApprovalRequest)과 동일한 규칙.
// MerchantPayType: "CARD1"/"CARD2" — van_config_tb 조회 키(카드결제와 동일한 가맹점 TID를 재사용).
// ReceiptType: "PERSONAL"(개인 소득공제용) / "BUSINESS"(사업자 지출증빙용).

public sealed class CashReceiptResult
{
    public required bool IsIssued { get; init; }
    public string? ApprovalNo { get; init; }           // 발급 성공 시 승인번호(KICC 응답 R09)
    public string? ApprovalDateYyMmDd { get; init; }   // 발급 성공 시 YYMMDD(KICC 응답 R07 앞 6자리) — B2 취소에 사용
    public required string ResponseMessage { get; init; }
}

public sealed record CashReceiptCancelRequest(
    string PosCode, string MerchantPayType, string ReceiptType, decimal Amount,
    string OriginalApprovalNo, string OriginalApprovalDateYyMmDd);

public sealed class CashReceiptCancelResult
{
    public required bool IsCancelled { get; init; }
    public required string ResponseMessage { get; init; }
}
```

- [ ] **Step 2: `StubCashReceiptGateway`에 취소 구현 추가**

`src/FishingMartPos/Services/StubCashReceiptGateway.cs`에서 `RequestIssueAsync` 메서드 바로 아래에 추가:

```csharp
    public async Task<CashReceiptCancelResult> RequestCancelAsync(CashReceiptCancelRequest request)
    {
        await _delay.Delay(TimeSpan.FromMilliseconds(1500));

        if (_outcomeProvider.NextIsApproved())
        {
            return new CashReceiptCancelResult { IsCancelled = true, ResponseMessage = "현금영수증 취소 완료" };
        }

        return new CashReceiptCancelResult
        {
            IsCancelled = false,
            ResponseMessage = DeclineMessages[_messageRandom.Next(DeclineMessages.Length)],
        };
    }
```

- [ ] **Step 3: `KiccCashReceiptGateway`에 취소 구현 추가(기존 `BuildCashReceiptCancelRequest` 재사용)**

`src/FishingMartPos/Services/Kicc/KiccCashReceiptGateway.cs`에서 `RequestIssueAsync` 메서드 바로 아래에 추가:

```csharp
    public async Task<CashReceiptCancelResult> RequestCancelAsync(CashReceiptCancelRequest request)
    {
        if (!_merchantsByPayType.TryGetValue(request.MerchantPayType, out var merchant))
        {
            return new CashReceiptCancelResult { IsCancelled = false, ResponseMessage = "가맹점 설정을 찾을 수 없습니다" };
        }

        var posTranNo = BuildPosTranNo(request.PosCode);
        var sendData = KiccMessageBuilder.BuildCashReceiptCancelRequest(
            merchant, request.Amount, request.ReceiptType,
            request.OriginalApprovalNo, request.OriginalApprovalDateYyMmDd, posTranNo);

        var raw = await _client.RequestAsync(0xFB, 0x14, 0x04, sendData);

        if (!raw.IsSuccess)
        {
            return new CashReceiptCancelResult { IsCancelled = false, ResponseMessage = raw.FailureMessage ?? "현금영수증 취소 거절" };
        }

        var fields = KiccResponseParser.Parse(raw.Data!);
        if (fields.TryGetValue("R04", out var rc) && rc == "0000")
        {
            return new CashReceiptCancelResult { IsCancelled = true, ResponseMessage = "현금영수증 취소 완료" };
        }

        return new CashReceiptCancelResult { IsCancelled = false, ResponseMessage = "현금영수증 취소 거절" };
    }
```

- [ ] **Step 4: `KiccCashReceiptGateway` 취소 테스트 추가**

`tests/FishingMartPos.Tests/Services/Kicc/KiccCashReceiptGatewayTests.cs`의 클래스 안, 마지막 `[Fact]` 다음에 추가:

```csharp
    [Fact]
    public async Task RequestCancelAsync_WhenR04IsSuccess_ReturnsCancelled()
    {
        var client = new FakeKiccPosClient(KiccRawResponse.Success("R04=0000;"));
        var gateway = new KiccCashReceiptGateway(client, Merchants);

        var result = await gateway.RequestCancelAsync(new CashReceiptCancelRequest("1", "CARD1", "PERSONAL", 1004m, "149331691", "250704"));

        Assert.True(result.IsCancelled);
    }

    [Fact]
    public async Task RequestCancelAsync_WhenR04IsNotSuccess_ReturnsNotCancelled()
    {
        var client = new FakeKiccPosClient(KiccRawResponse.Success("R04=1234;"));
        var gateway = new KiccCashReceiptGateway(client, Merchants);

        var result = await gateway.RequestCancelAsync(new CashReceiptCancelRequest("1", "CARD1", "PERSONAL", 1004m, "149331691", "250704"));

        Assert.False(result.IsCancelled);
    }

    [Fact]
    public async Task RequestCancelAsync_SendsB2WithOriginalApprovalFields()
    {
        var client = new FakeKiccPosClient(KiccRawResponse.Success("R04=0000;"));
        var gateway = new KiccCashReceiptGateway(client, Merchants);

        await gateway.RequestCancelAsync(new CashReceiptCancelRequest("1", "CARD1", "PERSONAL", 1004m, "149331691", "250704"));

        var sendData = Assert.Single(client.Requests).SendData;
        Assert.Contains("S01=B2;", sendData);
        Assert.Contains("S12=149331691;S13=250704;", sendData);
    }
```

- [ ] **Step 5: `StubCashReceiptGateway` 취소 테스트 추가**

`tests/FishingMartPos.Tests/Services/StubCashReceiptGatewayTests.cs`에 추가(기존 `RequestIssueAsync` 테스트가 픽스처를 만드는 방식과 동일하게):

```csharp
    [Fact]
    public async Task RequestCancelAsync_WhenOutcomeProviderApproves_ReturnsCancelled()
    {
        var gateway = new StubCashReceiptGateway(new FakeDelayProvider(), new AlwaysApprovedOutcomeProvider());

        var result = await gateway.RequestCancelAsync(new CashReceiptCancelRequest("1", "CARD1", "PERSONAL", 1004m, "149331691", "250704"));

        Assert.True(result.IsCancelled);
    }
```

(픽스처 클래스 이름은 Task 2의 Step 8과 동일하게, 파일에 실제로 존재하는 이름을 확인해서 맞춘다.)

- [ ] **Step 6: `FakeCashReceiptGateway`에 취소 지원 추가**

`tests/FishingMartPos.Tests/Fakes/FakeCashReceiptGateway.cs` 전체를 다음으로 교체:

```csharp
using FishingMartPos.Services;

namespace FishingMartPos.Tests.Fakes;

public sealed class FakeCashReceiptGateway : ICashReceiptGateway
{
    private readonly CashReceiptResult _issueResult;
    private readonly CashReceiptCancelResult _cancelResult;
    public List<CashReceiptRequest> Requests { get; } = new();
    public List<CashReceiptCancelRequest> CancelRequests { get; } = new();

    public FakeCashReceiptGateway(CashReceiptResult issueResult, CashReceiptCancelResult? cancelResult = null)
    {
        _issueResult = issueResult;
        _cancelResult = cancelResult ?? new CashReceiptCancelResult { IsCancelled = true, ResponseMessage = "현금영수증 취소 완료" };
    }

    public Task<CashReceiptResult> RequestIssueAsync(CashReceiptRequest request)
    {
        Requests.Add(request);
        return Task.FromResult(_issueResult);
    }

    public Task<CashReceiptCancelResult> RequestCancelAsync(CashReceiptCancelRequest request)
    {
        CancelRequests.Add(request);
        return Task.FromResult(_cancelResult);
    }
}
```

- [ ] **Step 7: 빌드 + 테스트 실행**

Run: `dotnet build src/FishingMartPos/FishingMartPos.csproj`
Run: `dotnet test tests/FishingMartPos.Tests`
Expected: 전체 통과

- [ ] **Step 8: 커밋**

```bash
git add src/FishingMartPos/Services/ICashReceiptGateway.cs src/FishingMartPos/Services/StubCashReceiptGateway.cs src/FishingMartPos/Services/Kicc/KiccCashReceiptGateway.cs tests/FishingMartPos.Tests/Fakes/FakeCashReceiptGateway.cs tests/FishingMartPos.Tests/Services/Kicc/KiccCashReceiptGatewayTests.cs tests/FishingMartPos.Tests/Services/StubCashReceiptGatewayTests.cs
git commit -m "ICashReceiptGateway에 현금영수증취소(B2) 메서드 추가"
```

---

## Task 4: ReceiptDocumentFactory (영수증 재발행용 정적 빌더)

**Files:**
- Create: `src/FishingMartPos/Models/ReceiptDocumentFactory.cs`
- Test: `tests/FishingMartPos.Tests/Models/ReceiptDocumentFactoryTests.cs`

**Interfaces:**
- Consumes: `SaleHeader`(Task 1), `SaleDetailLine`, `ReceiptDocument`/`ReceiptLine`(기존, `src/FishingMartPos/Models/ReceiptDocument.cs`).
- Produces: `ReceiptDocumentFactory.FromSale(SaleHeader header, IReadOnlyList<SaleDetailLine> lines) : ReceiptDocument` — 과거 매출 건으로부터 영수증을 재구성(현재 `PosViewModel.BuildReceiptDocument`는 진행 중인 카트 기준이라 과거 건 재발행에는 쓸 수 없다).

- [ ] **Step 1: 실패하는 테스트 작성**

`tests/FishingMartPos.Tests/Models/ReceiptDocumentFactoryTests.cs` 생성:

```csharp
using FishingMartPos.Models;
using Xunit;

namespace FishingMartPos.Tests.Models;

public class ReceiptDocumentFactoryTests
{
    [Fact]
    public void FromSale_CardSale_MapsPayTypeLabelAndApprovalNo()
    {
        var header = new SaleHeader
        {
            SaleNo = 1,
            PosCd = "1",
            SaleDt = DateTime.Now,
            StaffCd = "ADMIN1",
            TotalAmt = 22500m,
            PayType = "CARD1",
            VanApprovalNo = "99145616",
            InstallmentMonths = 3,
        };
        var lines = new List<SaleDetailLine>
        {
            new() { Barcode = "A1", ProductName = "지렁이", Qty = 1, UnitPrice = 5000m },
        };

        var document = ReceiptDocumentFactory.FromSale(header, lines);

        Assert.Equal("카드결제1", document.PayTypeLabel);
        Assert.Equal("99145616", document.VanApprovalNo);
        Assert.Equal(3, document.InstallmentMonths);
        Assert.Equal(22500m, document.TotalAmt);
        Assert.Single(document.Lines);
        Assert.Equal("지렁이", document.Lines[0].ProductName);
    }

    [Fact]
    public void FromSale_CashSaleWithReceipt_MapsCashReceiptTypeLabel()
    {
        var header = new SaleHeader
        {
            SaleNo = 2,
            PosCd = "1",
            SaleDt = DateTime.Now,
            StaffCd = "ADMIN1",
            TotalAmt = 5000m,
            PayType = "CASH",
            CashReceiptType = "PERSONAL",
            CashReceiptApprovalNo = "149331691",
        };

        var document = ReceiptDocumentFactory.FromSale(header, new List<SaleDetailLine>());

        Assert.Equal("현금", document.PayTypeLabel);
        Assert.Equal("개인(소득공제)", document.CashReceiptTypeLabel);
        Assert.Equal("149331691", document.CashReceiptApprovalNo);
    }

    [Fact]
    public void FromSale_CashSaleWithoutReceipt_CashReceiptTypeLabelIsNull()
    {
        var header = new SaleHeader
        {
            SaleNo = 3,
            PosCd = "1",
            SaleDt = DateTime.Now,
            StaffCd = "ADMIN1",
            TotalAmt = 5000m,
            PayType = "CASH",
        };

        var document = ReceiptDocumentFactory.FromSale(header, new List<SaleDetailLine>());

        Assert.Null(document.CashReceiptTypeLabel);
    }
}
```

- [ ] **Step 2: 테스트 실행 (실패 확인)**

Run: `dotnet test tests/FishingMartPos.Tests --filter ReceiptDocumentFactoryTests`
Expected: FAIL (`ReceiptDocumentFactory` 없음)

- [ ] **Step 3: 구현**

`src/FishingMartPos/Models/ReceiptDocumentFactory.cs` 신규 생성:

```csharp
namespace FishingMartPos.Models;

public static class ReceiptDocumentFactory
{
    // ReceiptConfig 연동은 범위 밖 — 헤더/푸터는 항상 빈 문자열(PosViewModel.BuildReceiptDocument와 동일)
    public static ReceiptDocument FromSale(SaleHeader header, IReadOnlyList<SaleDetailLine> lines) => new()
    {
        HeaderText = string.Empty,
        FooterText = string.Empty,
        Lines = lines.Select(l => new ReceiptLine(l.ProductName, l.Qty, l.UnitPrice, l.LineAmt)).ToList(),
        TotalAmt = header.TotalAmt,
        PayTypeLabel = PayTypeLabel(header.PayType),
        VanApprovalNo = header.VanApprovalNo,
        InstallmentMonths = header.InstallmentMonths,
        CashReceiptTypeLabel = CashReceiptTypeLabel(header.CashReceiptType),
        CashReceiptApprovalNo = header.CashReceiptApprovalNo,
    };

    private static string PayTypeLabel(string payType) => payType switch
    {
        "CASH" => "현금",
        "CARD1" => "카드결제1",
        "CARD2" => "카드결제2",
        _ => payType,
    };

    private static string? CashReceiptTypeLabel(string type) => type switch
    {
        "PERSONAL" => "개인(소득공제)",
        "BUSINESS" => "사업자(지출증빙)",
        _ => null,
    };
}
```

- [ ] **Step 4: 테스트 실행 (통과 확인)**

Run: `dotnet test tests/FishingMartPos.Tests --filter ReceiptDocumentFactoryTests`
Expected: PASS (3건)

- [ ] **Step 5: 커밋**

```bash
git add src/FishingMartPos/Models/ReceiptDocumentFactory.cs tests/FishingMartPos.Tests/Models/ReceiptDocumentFactoryTests.cs
git commit -m "과거 매출 건으로 영수증을 재구성하는 ReceiptDocumentFactory 추가"
```

---

## Task 5: TransactionDetailViewModel (공용 상세/액션 뷰모델)

**Files:**
- Create: `src/FishingMartPos/ViewModels/TransactionDetailViewModel.cs`
- Test: `tests/FishingMartPos.Tests/ViewModels/TransactionDetailViewModelTests.cs`

**Interfaces:**
- Consumes: `SaleHeader`/`SaleDetailLine`(Task 1), `IVanPaymentGateway.RequestCancelAsync`(Task 2), `ICashReceiptGateway.RequestCancelAsync`/`RequestIssueAsync`(Task 3), `ReceiptDocumentFactory.FromSale`(Task 4), `ISalesRepository.CancelSaleAsync`/`UpdateCashReceiptAsync`(Task 1), `IReceiptPrinter.PrintAsync`(기존).
- Produces: `TransactionDetailViewModel` 생성자 `(SaleHeader header, IReadOnlyList<SaleDetailLine> lines, string currentPosCode, ISalesRepository salesRepository, IVanPaymentGateway vanGateway, ICashReceiptGateway cashReceiptGateway, IReceiptPrinter receiptPrinter, IDelayProvider delay)`, 커맨드 `CancelCommand`/`ShowReceiptConversionCommand`/`SelectReceiptTypeCommand`/`SelectReceiptMerchantCommand`/`CancelReceiptConversionCommand`/`ConfirmReceiptConversionCommand`/`ReissueReceiptCommand`/`CloseCommand`, 이벤트 `Changed`/`CloseRequested`. Task 6(POS 직전정보 팝업), Task 7(결제관리 화면)이 이 클래스를 그대로 사용한다.

- [ ] **Step 1: 실패하는 테스트 작성**

`tests/FishingMartPos.Tests/ViewModels/TransactionDetailViewModelTests.cs` 생성:

```csharp
using FishingMartPos.Models;
using FishingMartPos.Services;
using FishingMartPos.Tests.Fakes;
using FishingMartPos.ViewModels;
using Xunit;

namespace FishingMartPos.Tests.ViewModels;

public class TransactionDetailViewModelTests
{
    private static SaleHeader CardHeader(string status = "COMPLETE") => new()
    {
        SaleNo = 10,
        PosCd = "1",
        SaleDt = new DateTime(2026, 7, 26),
        StaffCd = "ADMIN1",
        TotalAmt = 22500m,
        PayType = "CARD1",
        VanApprovalNo = "99145616",
        InstallmentMonths = 0,
        Status = status,
    };

    private static SaleHeader CashHeaderNoReceipt(string status = "COMPLETE") => new()
    {
        SaleNo = 11,
        PosCd = "1",
        SaleDt = new DateTime(2026, 7, 26),
        StaffCd = "ADMIN1",
        TotalAmt = 5000m,
        PayType = "CASH",
        CashReceiptType = "NONE",
        Status = status,
    };

    private static TransactionDetailViewModel CreateViewModel(
        SaleHeader header,
        out FakeSalesRepository sales,
        out FakeVanPaymentGateway van,
        out FakeCashReceiptGateway cashReceipt)
    {
        sales = new FakeSalesRepository();
        van = new FakeVanPaymentGateway(new VanApprovalResult { IsApproved = true, ResponseMessage = "ok" });
        cashReceipt = new FakeCashReceiptGateway(new CashReceiptResult { IsIssued = true, ApprovalNo = "APR1", ApprovalDateYyMmDd = "260726", ResponseMessage = "발급완료" });
        return new TransactionDetailViewModel(
            header, new List<SaleDetailLine>(), "1", sales, van, cashReceipt,
            new FakeReceiptPrinter(true), new FakeDelayProvider());
    }

    [Fact]
    public void CanCancel_WhenStatusIsComplete_IsTrue()
    {
        var vm = CreateViewModel(CardHeader(), out _, out _, out _);
        Assert.True(vm.CanCancel);
    }

    [Fact]
    public void CanCancel_WhenAlreadyCancelled_IsFalse()
    {
        var vm = CreateViewModel(CardHeader("CANCELLED"), out _, out _, out _);
        Assert.False(vm.CanCancel);
    }

    [Fact]
    public void CanConvertToCashReceipt_ForCashSaleWithoutReceipt_IsTrue()
    {
        var vm = CreateViewModel(CashHeaderNoReceipt(), out _, out _, out _);
        Assert.True(vm.CanConvertToCashReceipt);
    }

    [Fact]
    public void CanConvertToCashReceipt_ForCardSale_IsFalse()
    {
        var vm = CreateViewModel(CardHeader(), out _, out _, out _);
        Assert.False(vm.CanConvertToCashReceipt);
    }

    [Fact]
    public async Task CancelCommand_ForCardSale_CallsVanCancelWithOriginalApprovalFields()
    {
        var vm = CreateViewModel(CardHeader(), out var sales, out var van, out _);

        await vm.CancelCommand.ExecuteAsync(null);

        var request = Assert.Single(van.CancelRequests);
        Assert.Equal("99145616", request.OriginalApprovalNo);
        Assert.Equal("260726", request.OriginalApprovalDateYyMmDd);
        Assert.Single(sales.CancelledSaleNos, 10);
        Assert.False(vm.CanCancel);
    }

    [Fact]
    public async Task CancelCommand_ForCashSaleWithoutReceipt_SkipsGatewayCallButCancelsSale()
    {
        var vm = CreateViewModel(CashHeaderNoReceipt(), out var sales, out _, out var cashReceipt);

        await vm.CancelCommand.ExecuteAsync(null);

        Assert.Empty(cashReceipt.CancelRequests);
        Assert.Single(sales.CancelledSaleNos, 11);
    }

    [Fact]
    public async Task ConfirmReceiptConversionCommand_IssuesReceiptAndUpdatesSale()
    {
        var vm = CreateViewModel(CashHeaderNoReceipt(), out var sales, out _, out var cashReceipt);

        vm.ShowReceiptConversionCommand.Execute(null);
        vm.SelectReceiptTypeCommand.Execute("PERSONAL");
        vm.SelectReceiptMerchantCommand.Execute("CARD1");
        await vm.ConfirmReceiptConversionCommand.ExecuteAsync(null);

        Assert.Single(cashReceipt.Requests);
        var update = Assert.Single(sales.CashReceiptUpdates);
        Assert.Equal(11, update.SaleNo);
        Assert.Equal("PERSONAL", update.ReceiptType);
        Assert.False(vm.CanConvertToCashReceipt);
    }

    [Fact]
    public void CloseCommand_InvokesCloseRequestedEvent()
    {
        var vm = CreateViewModel(CardHeader(), out _, out _, out _);
        bool closed = false;
        vm.CloseRequested += () => closed = true;

        vm.CloseCommand.Execute(null);

        Assert.True(closed);
    }
}
```

이 테스트는 `FakeReceiptPrinter`를 사용한다 — 아직 없다면 확인 후 생성한다:

```bash
find tests -iname "*FakeReceiptPrinter*"
```

없으면 `tests/FishingMartPos.Tests/Fakes/FakeReceiptPrinter.cs`를 생성:

```csharp
using FishingMartPos.Models;
using FishingMartPos.Services;

namespace FishingMartPos.Tests.Fakes;

public sealed class FakeReceiptPrinter : IReceiptPrinter
{
    private readonly bool _result;
    public List<ReceiptDocument> PrintedDocuments { get; } = new();

    public FakeReceiptPrinter(bool result)
    {
        _result = result;
    }

    public Task<bool> PrintAsync(ReceiptDocument document)
    {
        PrintedDocuments.Add(document);
        return Task.FromResult(_result);
    }
}
```

(이미 존재한다면 이 스텝은 건너뛴다.)

- [ ] **Step 2: 테스트 실행 (실패 확인)**

Run: `dotnet test tests/FishingMartPos.Tests --filter TransactionDetailViewModelTests`
Expected: FAIL (`TransactionDetailViewModel` 없음)

- [ ] **Step 3: 구현**

`src/FishingMartPos/ViewModels/TransactionDetailViewModel.cs` 신규 생성:

```csharp
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FishingMartPos.Models;
using FishingMartPos.Repositories;
using FishingMartPos.Services;
using FishingMartPos.Theme;

namespace FishingMartPos.ViewModels;

public sealed partial class TransactionDetailViewModel : ObservableObject
{
    private readonly string _currentPosCode;
    private readonly ISalesRepository _salesRepository;
    private readonly IVanPaymentGateway _vanGateway;
    private readonly ICashReceiptGateway _cashReceiptGateway;
    private readonly IReceiptPrinter _receiptPrinter;
    private readonly IDelayProvider _delay;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanCancel))]
    [NotifyPropertyChangedFor(nameof(CanConvertToCashReceipt))]
    [NotifyPropertyChangedFor(nameof(SaleNoStr))]
    [NotifyPropertyChangedFor(nameof(SaleDtStr))]
    [NotifyPropertyChangedFor(nameof(PayTypeLabelStr))]
    [NotifyPropertyChangedFor(nameof(TotalAmtStr))]
    [NotifyPropertyChangedFor(nameof(InstallmentLabelStr))]
    [NotifyPropertyChangedFor(nameof(StatusLabelStr))]
    private SaleHeader _header;

    [ObservableProperty]
    private bool _isCancelling;

    [ObservableProperty]
    private bool _isConvertingToReceipt;

    [ObservableProperty]
    private bool _isReceiptConversionVisible;

    [ObservableProperty]
    private string _selectedReceiptType = "PERSONAL";

    [ObservableProperty]
    private string? _selectedReceiptMerchant;

    [ObservableProperty]
    private string? _statusMessage;

    [ObservableProperty]
    private bool _isStatusError;

    public IReadOnlyList<SaleDetailLine> Lines { get; }

    public event Action? Changed;
    public event Action? CloseRequested;

    public TransactionDetailViewModel(
        SaleHeader header,
        IReadOnlyList<SaleDetailLine> lines,
        string currentPosCode,
        ISalesRepository salesRepository,
        IVanPaymentGateway vanGateway,
        ICashReceiptGateway cashReceiptGateway,
        IReceiptPrinter receiptPrinter,
        IDelayProvider delay)
    {
        _header = header;
        Lines = lines;
        _currentPosCode = currentPosCode;
        _salesRepository = salesRepository;
        _vanGateway = vanGateway;
        _cashReceiptGateway = cashReceiptGateway;
        _receiptPrinter = receiptPrinter;
        _delay = delay;
    }

    public bool CanCancel => Header.Status == "COMPLETE";
    public bool CanConvertToCashReceipt => Header.PayType == "CASH" && Header.CashReceiptType == "NONE" && Header.Status == "COMPLETE";
    public string SaleNoStr => Header.SaleNo.ToString();
    public string SaleDtStr => Header.SaleDt.ToString("yyyy-MM-dd HH:mm:ss");
    public string PayTypeLabelStr => Header.PayType switch
    {
        "CASH" => "현금",
        "CARD1" => "카드결제1",
        "CARD2" => "카드결제2",
        _ => Header.PayType,
    };
    public string TotalAmtStr => CurrencyFormat.Format(Header.TotalAmt);
    public string InstallmentLabelStr => Header.InstallmentMonths <= 0 ? "일시불" : $"{Header.InstallmentMonths}개월";
    public string StatusLabelStr => Header.Status == "CANCELLED" ? "취소됨" : "정상";

    [RelayCommand]
    private async Task Cancel()
    {
        if (!CanCancel || IsCancelling) return;

        IsCancelling = true;
        StatusMessage = null;
        try
        {
            if (Header.PayType != "CASH")
            {
                var result = await _vanGateway.RequestCancelAsync(new VanCancelRequest(
                    _currentPosCode, Header.PayType, Header.TotalAmt, Header.InstallmentMonths,
                    Header.VanApprovalNo!, Header.SaleDt.ToString("yyMMdd")));
                if (!result.IsCancelled)
                {
                    IsStatusError = true;
                    StatusMessage = result.ResponseMessage;
                    return;
                }
            }
            else if (Header.CashReceiptType != "NONE")
            {
                var result = await _cashReceiptGateway.RequestCancelAsync(new CashReceiptCancelRequest(
                    _currentPosCode, Header.CashReceiptMerchant!, Header.CashReceiptType, Header.TotalAmt,
                    Header.CashReceiptApprovalNo!, Header.CashReceiptApprovalDate!));
                if (!result.IsCancelled)
                {
                    IsStatusError = true;
                    StatusMessage = result.ResponseMessage;
                    return;
                }
            }

            await _salesRepository.CancelSaleAsync(Header.SaleNo);
            Header = Header with { Status = "CANCELLED" };
            IsStatusError = false;
            StatusMessage = "취소되었습니다";
            Changed?.Invoke();
        }
        finally
        {
            IsCancelling = false;
        }
    }

    [RelayCommand]
    private void ShowReceiptConversion()
    {
        if (!CanConvertToCashReceipt) return;
        SelectedReceiptType = "PERSONAL";
        SelectedReceiptMerchant = null;
        StatusMessage = null;
        IsReceiptConversionVisible = true;
    }

    [RelayCommand]
    private void SelectReceiptType(string type) => SelectedReceiptType = type;

    [RelayCommand]
    private void SelectReceiptMerchant(string merchant) => SelectedReceiptMerchant = merchant;

    [RelayCommand]
    private void CancelReceiptConversion() => IsReceiptConversionVisible = false;

    [RelayCommand]
    private async Task ConfirmReceiptConversion()
    {
        if (SelectedReceiptMerchant is not string merchant || IsConvertingToReceipt) return;

        IsConvertingToReceipt = true;
        StatusMessage = null;
        try
        {
            var result = await _cashReceiptGateway.RequestIssueAsync(
                new CashReceiptRequest(_currentPosCode, merchant, SelectedReceiptType, Header.TotalAmt));
            if (!result.IsIssued)
            {
                IsStatusError = true;
                StatusMessage = result.ResponseMessage;
                return;
            }

            await _salesRepository.UpdateCashReceiptAsync(Header.SaleNo, SelectedReceiptType, merchant, result.ApprovalNo!, result.ApprovalDateYyMmDd!);
            Header = Header with
            {
                CashReceiptType = SelectedReceiptType,
                CashReceiptMerchant = merchant,
                CashReceiptApprovalNo = result.ApprovalNo,
                CashReceiptApprovalDate = result.ApprovalDateYyMmDd,
            };
            IsReceiptConversionVisible = false;
            IsStatusError = false;
            StatusMessage = "현금영수증이 발급되었습니다";
            Changed?.Invoke();
        }
        finally
        {
            IsConvertingToReceipt = false;
        }
    }

    [RelayCommand]
    private async Task ReissueReceipt()
    {
        var document = ReceiptDocumentFactory.FromSale(Header, Lines);
        bool printed = await _receiptPrinter.PrintAsync(document);
        if (!printed)
        {
            IsStatusError = true;
            StatusMessage = "프린터 연동은 지원 예정입니다";
        }
    }

    [RelayCommand]
    private void Close() => CloseRequested?.Invoke();
}
```

- [ ] **Step 4: 테스트 실행 (통과 확인)**

Run: `dotnet test tests/FishingMartPos.Tests --filter TransactionDetailViewModelTests`
Expected: 전체 PASS

- [ ] **Step 5: 커밋**

```bash
git add src/FishingMartPos/ViewModels/TransactionDetailViewModel.cs tests/FishingMartPos.Tests/ViewModels/TransactionDetailViewModelTests.cs tests/FishingMartPos.Tests/Fakes/FakeReceiptPrinter.cs
git commit -m "거래 취소/현금영수증 변경/영수증 재발행을 처리하는 공용 TransactionDetailViewModel 추가"
```

---

## Task 6: TransactionDetailPopup (공용 화면) + POS 직전정보 연결

**Files:**
- Create: `src/FishingMartPos/Views/TransactionDetailPopup.xaml`
- Create: `src/FishingMartPos/Views/TransactionDetailPopup.xaml.cs`
- Modify: `src/FishingMartPos/ViewModels/PosViewModel.cs`
- Modify: `src/FishingMartPos/Views/PosView.xaml`
- Test: `tests/FishingMartPos.Tests/ViewModels/PosViewModelTests.cs`

**Interfaces:**
- Consumes: `TransactionDetailViewModel`(Task 5), 기존 `PosViewModel`의 `_salesRepository`/`_vanGateway`/`_cashReceiptGateway`/`_receiptPrinter`/`_delay`/`_session`(생성자 변경 없음 — 전부 이미 있음).
- Produces: `PosViewModel.ShowLastTransactionCommand`, `PosViewModel.IsLastTransactionVisible`, `PosViewModel.LastTransactionDetail`.

- [ ] **Step 1: `TransactionDetailPopup.xaml.cs` 코드비하인드 생성**

`src/FishingMartPos/Views/TransactionDetailPopup.xaml.cs` 신규 생성:

```csharp
using System.Windows.Controls;

namespace FishingMartPos.Views;

public partial class TransactionDetailPopup : UserControl
{
    public TransactionDetailPopup()
    {
        InitializeComponent();
    }
}
```

- [ ] **Step 2: `TransactionDetailPopup.xaml` 생성**

`src/FishingMartPos/Views/TransactionDetailPopup.xaml` 신규 생성:

```xml
<UserControl x:Class="FishingMartPos.Views.TransactionDetailPopup"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006"
             xmlns:d="http://schemas.microsoft.com/expression/blend/2008"
             mc:Ignorable="d">
    <UserControl.Resources>
        <BooleanToVisibilityConverter x:Key="BooleanToVisibilityConverter" />
        <local:InverseBooleanToVisibilityConverter x:Key="InverseBooleanToVisibilityConverter" xmlns:local="clr-namespace:FishingMartPos.Converters" />
        <local:NullToVisibilityConverter x:Key="NullToVisibilityConverter" xmlns:local="clr-namespace:FishingMartPos.Converters" />
        <Style x:Key="DetailActionButtonStyle" TargetType="Button">
            <Setter Property="Margin" Value="3" />
            <Setter Property="Padding" Value="8,6" />
            <Setter Property="FontSize" Value="12" />
        </Style>
        <Style x:Key="DetailPickerButtonStyle" TargetType="Button">
            <Setter Property="Margin" Value="3" />
            <Setter Property="Padding" Value="6" />
            <Setter Property="Background" Value="{DynamicResource LogoutButtonBackground}" />
            <Setter Property="Foreground" Value="{DynamicResource MutedText}" />
        </Style>
    </UserControl.Resources>
    <Border Background="White" BorderBrush="{DynamicResource CardBorder}" BorderThickness="1"
            Padding="24" Width="380" MaxHeight="540" HorizontalAlignment="Center" VerticalAlignment="Center">
        <Grid>
            <Grid.RowDefinitions>
                <RowDefinition Height="Auto" />
                <RowDefinition Height="*" />
                <RowDefinition Height="Auto" />
                <RowDefinition Height="Auto" />
            </Grid.RowDefinitions>

            <StackPanel Grid.Row="0" Margin="0,0,0,12">
                <TextBlock Text="{Binding SaleNoStr, StringFormat='거래번호: {0}'}" FontSize="13" FontWeight="Bold" />
                <TextBlock Text="{Binding SaleDtStr}" FontSize="12" Foreground="{DynamicResource MutedText}" Margin="0,2" />
                <TextBlock Text="{Binding PayTypeLabelStr, StringFormat='결제구분: {0}'}" FontSize="13" Margin="0,4,0,0" />
                <TextBlock Text="{Binding TotalAmtStr, StringFormat='결제금액: {0}'}" FontSize="15" FontWeight="Bold" Margin="0,2" />
                <TextBlock Text="{Binding InstallmentLabelStr, StringFormat='할부: {0}'}" FontSize="12" Margin="0,2" />
                <TextBlock Text="{Binding StatusLabelStr, StringFormat='상태: {0}'}" FontSize="12" FontWeight="Bold" Margin="0,4,0,0" />
            </StackPanel>

            <ScrollViewer Grid.Row="1" VerticalScrollBarVisibility="Auto" MaxHeight="180">
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
            </ScrollViewer>

            <StackPanel Grid.Row="2" Margin="0,10,0,0">
                <StackPanel Visibility="{Binding IsReceiptConversionVisible, Converter={StaticResource BooleanToVisibilityConverter}}">
                    <TextBlock Text="현금영수증 종류 선택" FontSize="12" Foreground="{DynamicResource MutedText}" Margin="0,0,0,6" />
                    <UniformGrid Columns="2">
                        <Button Content="개인" Command="{Binding SelectReceiptTypeCommand}" CommandParameter="PERSONAL">
                            <Button.Style>
                                <Style TargetType="Button" BasedOn="{StaticResource DetailPickerButtonStyle}">
                                    <Style.Triggers>
                                        <DataTrigger Binding="{Binding SelectedReceiptType}" Value="PERSONAL">
                                            <Setter Property="Background" Value="{DynamicResource Accent}" />
                                            <Setter Property="Foreground" Value="White" />
                                        </DataTrigger>
                                    </Style.Triggers>
                                </Style>
                            </Button.Style>
                        </Button>
                        <Button Content="사업자" Command="{Binding SelectReceiptTypeCommand}" CommandParameter="BUSINESS">
                            <Button.Style>
                                <Style TargetType="Button" BasedOn="{StaticResource DetailPickerButtonStyle}">
                                    <Style.Triggers>
                                        <DataTrigger Binding="{Binding SelectedReceiptType}" Value="BUSINESS">
                                            <Setter Property="Background" Value="{DynamicResource Accent}" />
                                            <Setter Property="Foreground" Value="White" />
                                        </DataTrigger>
                                    </Style.Triggers>
                                </Style>
                            </Button.Style>
                        </Button>
                    </UniformGrid>
                    <TextBlock Text="가맹점 선택" FontSize="12" Foreground="{DynamicResource MutedText}" Margin="0,8,0,6" />
                    <UniformGrid Columns="2">
                        <Button Content="대원수산" Command="{Binding SelectReceiptMerchantCommand}" CommandParameter="CARD1">
                            <Button.Style>
                                <Style TargetType="Button" BasedOn="{StaticResource DetailPickerButtonStyle}">
                                    <Style.Triggers>
                                        <DataTrigger Binding="{Binding SelectedReceiptMerchant}" Value="CARD1">
                                            <Setter Property="Background" Value="{DynamicResource Accent}" />
                                            <Setter Property="Foreground" Value="White" />
                                        </DataTrigger>
                                    </Style.Triggers>
                                </Style>
                            </Button.Style>
                        </Button>
                        <Button Content="대원낚시마트" Command="{Binding SelectReceiptMerchantCommand}" CommandParameter="CARD2">
                            <Button.Style>
                                <Style TargetType="Button" BasedOn="{StaticResource DetailPickerButtonStyle}">
                                    <Style.Triggers>
                                        <DataTrigger Binding="{Binding SelectedReceiptMerchant}" Value="CARD2">
                                            <Setter Property="Background" Value="{DynamicResource Accent}" />
                                            <Setter Property="Foreground" Value="White" />
                                        </DataTrigger>
                                    </Style.Triggers>
                                </Style>
                            </Button.Style>
                        </Button>
                    </UniformGrid>
                    <TextBlock Text="카드단말기에서 고객 전화번호(또는 사업자등록번호)를 입력해주세요" FontSize="12" FontWeight="Bold"
                               TextWrapping="Wrap" Margin="0,8,0,0"
                               Visibility="{Binding IsConvertingToReceipt, Converter={StaticResource BooleanToVisibilityConverter}}" />
                    <StackPanel Orientation="Horizontal" HorizontalAlignment="Center" Margin="0,10,0,0">
                        <Button Content="취소" Command="{Binding CancelReceiptConversionCommand}" Style="{StaticResource DetailActionButtonStyle}"
                                IsEnabled="{Binding IsConvertingToReceipt, Converter={StaticResource InverseBooleanToVisibilityConverter}}" />
                        <Button Content="발급요청" Command="{Binding ConfirmReceiptConversionCommand}" Style="{StaticResource DetailActionButtonStyle}"
                                Background="{DynamicResource Accent}" Foreground="White" />
                    </StackPanel>
                </StackPanel>

                <TextBlock Text="{Binding StatusMessage}" FontSize="12" TextWrapping="Wrap" Margin="0,8,0,0"
                           HorizontalAlignment="Center"
                           Visibility="{Binding StatusMessage, Converter={StaticResource NullToVisibilityConverter}}">
                    <TextBlock.Style>
                        <Style TargetType="TextBlock">
                            <Setter Property="Foreground" Value="{DynamicResource Accent}" />
                            <Style.Triggers>
                                <DataTrigger Binding="{Binding IsStatusError}" Value="True">
                                    <Setter Property="Foreground" Value="{DynamicResource CartDeleteBorder}" />
                                </DataTrigger>
                            </Style.Triggers>
                        </Style>
                    </TextBlock.Style>
                </TextBlock>
            </StackPanel>

            <StackPanel Grid.Row="3" Orientation="Horizontal" HorizontalAlignment="Center" Margin="0,12,0,0"
                        Visibility="{Binding IsReceiptConversionVisible, Converter={StaticResource InverseBooleanToVisibilityConverter}}">
                <Button Content="거래취소" Command="{Binding CancelCommand}" IsEnabled="{Binding CanCancel}"
                        Background="{DynamicResource CartDeleteBackground}" Foreground="{DynamicResource CartDeleteText}"
                        BorderBrush="{DynamicResource CartDeleteBorder}" Style="{StaticResource DetailActionButtonStyle}" />
                <Button Content="현금영수증으로 변경하기" Command="{Binding ShowReceiptConversionCommand}" IsEnabled="{Binding CanConvertToCashReceipt}"
                        Style="{StaticResource DetailActionButtonStyle}" />
                <Button Content="영수증 재발행" Command="{Binding ReissueReceiptCommand}" Style="{StaticResource DetailActionButtonStyle}" />
                <Button Content="닫기" Command="{Binding CloseCommand}" Style="{StaticResource DetailActionButtonStyle}" />
            </StackPanel>
        </Grid>
    </Border>
</UserControl>
```

- [ ] **Step 3: `PosViewModel`에 직전정보 상태/커맨드 추가**

`src/FishingMartPos/ViewModels/PosViewModel.cs`에서 `[ObservableProperty] private ReceiptDocument? _previewedReceipt;` 바로 아래에 추가:

```csharp
    [ObservableProperty]
    private bool _isLastTransactionVisible;

    [ObservableProperty]
    private TransactionDetailViewModel? _lastTransactionDetail;
```

그리고 `CloseReceiptPreview`/`PrintReceipt` 커맨드가 있는 부분(파일 맨 아래쪽, `BuildReceiptDocument` 메서드 위)에 추가:

```csharp
    [RelayCommand]
    private async Task ShowLastTransaction()
    {
        var header = await _salesRepository.GetLastCompletedSaleAsync(_session.CurrentTerminal!.PosCode);
        if (header is null)
        {
            IsToastWarning = true;
            ToastMessage = "최근 거래가 없습니다";
            await _delay.Delay(TimeSpan.FromMilliseconds(1200));
            ToastMessage = null;
            return;
        }

        var result = await _salesRepository.GetSaleWithLinesAsync(header.SaleNo);
        if (result is null) return;

        var detail = new TransactionDetailViewModel(
            result.Value.Header, result.Value.Lines, _session.CurrentTerminal!.PosCode,
            _salesRepository, _vanGateway, _cashReceiptGateway, _receiptPrinter, _delay);
        detail.CloseRequested += () => IsLastTransactionVisible = false;

        LastTransactionDetail = detail;
        IsLastTransactionVisible = true;
    }
```

- [ ] **Step 4: `PosView.xaml`에서 "직전정보" 버튼 활성화 + 팝업 추가**

`src/FishingMartPos/Views/PosView.xaml`의 루트 `<UserControl ...>` 태그에 `xmlns:views` 선언 추가(기존 속성들 다음 줄에):

```xml
             xmlns:views="clr-namespace:FishingMartPos.Views"
```

`<Button Content="직전정보" IsEnabled="False" Margin="3" />` 줄을 다음으로 교체:

```xml
                        <Button Content="직전정보" Command="{Binding ShowLastTransactionCommand}" Margin="3" />
```

`<!-- 영수증 미리보기 -->` 오버레이 `Grid` 바로 다음에 새 오버레이 추가:

```xml
        <!-- 직전정보 팝업 -->
        <Grid Grid.RowSpan="2" Background="{DynamicResource ModalOverlay}"
              Visibility="{Binding IsLastTransactionVisible, Converter={StaticResource BooleanToVisibilityConverter}}">
            <views:TransactionDetailPopup DataContext="{Binding LastTransactionDetail}" />
        </Grid>
```

- [ ] **Step 5: `PosViewModel` 직전정보 테스트 추가**

`tests/FishingMartPos.Tests/ViewModels/PosViewModelTests.cs`에서 `CreateViewModel` 헬퍼를 사용하는 기존 테스트 근처에 추가:

```csharp
    [Fact]
    public async Task ShowLastTransaction_WhenNoCompletedSale_ShowsWarningToast()
    {
        var vm = CreateViewModel(out var sales, out _, out _, out _);
        sales.SeedLastCompletedSale(null);

        await vm.ShowLastTransactionCommand.ExecuteAsync(null);

        Assert.False(vm.IsLastTransactionVisible);
        Assert.Equal("최근 거래가 없습니다", vm.ToastMessage);
    }

    [Fact]
    public async Task ShowLastTransaction_WhenCompletedSaleExists_OpensDetailPopup()
    {
        var vm = CreateViewModel(out var sales, out _, out _, out _);
        var header = new SaleHeader
        {
            SaleNo = 5,
            PosCd = "1",
            SaleDt = DateTime.Now,
            StaffCd = "ADMIN1",
            TotalAmt = 5000m,
            PayType = "CASH",
        };
        sales.SeedLastCompletedSale(header);
        sales.SeedSaleWithLines(5, header, new List<SaleDetailLine>());

        await vm.ShowLastTransactionCommand.ExecuteAsync(null);

        Assert.True(vm.IsLastTransactionVisible);
        Assert.NotNull(vm.LastTransactionDetail);
        Assert.Equal("5", vm.LastTransactionDetail!.SaleNoStr);
    }
```

이 테스트가 참조하는 `_session.CurrentTerminal!.PosCode`(예: `"1"`)가 실제 `CreateViewModel` 헬퍼가 세팅하는 `PosTerminal.PosCode`와 일치하는지 반드시 확인할 것 — 다르면 그 값에 맞춰 테스트를 수정한다.

- [ ] **Step 6: 빌드 + 테스트 실행**

Run: `dotnet build src/FishingMartPos/FishingMartPos.csproj`
Run: `dotnet test tests/FishingMartPos.Tests`
Expected: 전체 통과

- [ ] **Step 7: 커밋**

```bash
git add src/FishingMartPos/Views/TransactionDetailPopup.xaml src/FishingMartPos/Views/TransactionDetailPopup.xaml.cs src/FishingMartPos/ViewModels/PosViewModel.cs src/FishingMartPos/Views/PosView.xaml tests/FishingMartPos.Tests/ViewModels/PosViewModelTests.cs
git commit -m "POS 화면 직전정보 버튼 연결 — 마지막 거래 조회/취소/현금영수증변경/재발행 팝업"
```

---

## Task 7: 결제관리 화면 (PaymentManagementViewModel/View)

**Files:**
- Create: `src/FishingMartPos/ViewModels/PaymentManagementViewModel.cs`
- Create: `src/FishingMartPos/ViewModels/PaymentSummaryRowViewModel.cs`
- Create: `src/FishingMartPos/Views/PaymentManagementView.xaml`
- Create: `src/FishingMartPos/Views/PaymentManagementView.xaml.cs`
- Test: `tests/FishingMartPos.Tests/ViewModels/PaymentManagementViewModelTests.cs`

**Interfaces:**
- Consumes: `ISalesRepository.SearchSalesAsync`/`GetSaleWithLinesAsync`(Task 1), `TransactionDetailViewModel`(Task 5), `TransactionDetailPopup`(Task 6), `ICurrentSession`, `INavigationService`, `MainMenuViewModel`.
- Produces: `PaymentManagementViewModel` 생성자 `(ISalesRepository, IVanPaymentGateway, ICashReceiptGateway, IReceiptPrinter, IDelayProvider, ICurrentSession, INavigationService, MainMenuViewModel returnTo)`, `LoadAsync()`. Task 8이 메인메뉴에서 이 화면으로 진입하는 배선을 담당한다.

- [ ] **Step 1: `PaymentSummaryRowViewModel` 생성**

`src/FishingMartPos/ViewModels/PaymentSummaryRowViewModel.cs` 신규 생성:

```csharp
using CommunityToolkit.Mvvm.Input;

namespace FishingMartPos.ViewModels;

public sealed class PaymentSummaryRowViewModel
{
    public required long SaleNo { get; init; }
    public required string SaleDtStr { get; init; }
    public required string PayTypeLabel { get; init; }
    public required string TotalAmtStr { get; init; }
    public required string StatusLabel { get; init; }
    public required IAsyncRelayCommand SelectCommand { get; init; }
}
```

- [ ] **Step 2: 실패하는 `PaymentManagementViewModel` 테스트 작성**

`tests/FishingMartPos.Tests/ViewModels/PaymentManagementViewModelTests.cs` 신규 생성. 기존 `SalesReportViewModelTests.cs`가 `ICurrentSession`/`INavigationService`/`MainMenuViewModel` 픽스처를 어떻게 만드는지 먼저 확인하고 동일한 방식으로 작성:

```csharp
using FishingMartPos.Models;
using FishingMartPos.Navigation;
using FishingMartPos.Services;
using FishingMartPos.Tests.Fakes;
using FishingMartPos.ViewModels;
using Xunit;

namespace FishingMartPos.Tests.ViewModels;

public class PaymentManagementViewModelTests
{
    private static PaymentManagementViewModel CreateViewModel(out FakeSalesRepository sales)
    {
        sales = new FakeSalesRepository();
        var session = new CurrentSession();
        session.SignIn(
            new Staff { StaffCode = "ADMIN1", StaffName = "관리자", Role = "ADMIN", UseYn = "Y" },
            new PosTerminal { PosCode = "1", PosName = "POS1" });
        var navigation = new NavigationService();
        var mainMenu = new MainMenuViewModel(session, navigation);
        var van = new FakeVanPaymentGateway(new VanApprovalResult { IsApproved = true, ResponseMessage = "ok" });
        var cashReceipt = new FakeCashReceiptGateway(new CashReceiptResult { IsIssued = true, ResponseMessage = "ok" });

        return new PaymentManagementViewModel(
            sales, van, cashReceipt, new StubReceiptPrinter(), new FakeDelayProvider(),
            session, navigation, mainMenu);
    }

    [Fact]
    public async Task LoadAsync_PopulatesRowsFromSearchResults()
    {
        var vm = CreateViewModel(out var sales);
        sales.SeedCompletedSales(new List<SaleHeader>
        {
            new()
            {
                SaleNo = 1, PosCd = "1", SaleDt = DateTime.Today, StaffCd = "ADMIN1",
                TotalAmt = 5000m, PayType = "CASH",
            },
        });

        await vm.LoadAsync();

        Assert.True(vm.HasRows);
        Assert.Single(vm.Rows);
        Assert.Equal(1, vm.Rows[0].SaleNo);
    }

    [Fact]
    public async Task SelectRow_OpensDetailPopupForThatSale()
    {
        var vm = CreateViewModel(out var sales);
        var header = new SaleHeader
        {
            SaleNo = 7, PosCd = "1", SaleDt = DateTime.Today, StaffCd = "ADMIN1",
            TotalAmt = 5000m, PayType = "CASH",
        };
        sales.SeedCompletedSales(new List<SaleHeader> { header });
        sales.SeedSaleWithLines(7, header, new List<SaleDetailLine>());
        await vm.LoadAsync();

        await vm.Rows[0].SelectCommand.ExecuteAsync(null);

        Assert.True(vm.IsDetailVisible);
        Assert.Equal("7", vm.SelectedDetail!.SaleNoStr);
    }
}
```

- [ ] **Step 3: 테스트 실행 (실패 확인)**

Run: `dotnet test tests/FishingMartPos.Tests --filter PaymentManagementViewModelTests`
Expected: FAIL (`PaymentManagementViewModel` 없음)

- [ ] **Step 4: `PaymentManagementViewModel` 구현**

`src/FishingMartPos/ViewModels/PaymentManagementViewModel.cs` 신규 생성:

```csharp
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FishingMartPos.Navigation;
using FishingMartPos.Repositories;
using FishingMartPos.Services;
using FishingMartPos.Theme;

namespace FishingMartPos.ViewModels;

public sealed partial class PaymentManagementViewModel : ObservableObject
{
    private readonly ISalesRepository _salesRepository;
    private readonly IVanPaymentGateway _vanGateway;
    private readonly ICashReceiptGateway _cashReceiptGateway;
    private readonly IReceiptPrinter _receiptPrinter;
    private readonly IDelayProvider _delay;
    private readonly ICurrentSession _session;
    private readonly INavigationService _navigation;
    private readonly MainMenuViewModel _returnTo;

    [ObservableProperty]
    private DateTime? _dateFrom;

    [ObservableProperty]
    private DateTime? _dateTo;

    [ObservableProperty]
    private string _payTypeFilter = "ALL";

    [ObservableProperty]
    private string _approvalNoFilter = string.Empty;

    [ObservableProperty]
    private bool _hasRows;

    [ObservableProperty]
    private bool _isDetailVisible;

    [ObservableProperty]
    private TransactionDetailViewModel? _selectedDetail;

    public ObservableCollection<PaymentSummaryRowViewModel> Rows { get; } = new();

    public PaymentManagementViewModel(
        ISalesRepository salesRepository,
        IVanPaymentGateway vanGateway,
        ICashReceiptGateway cashReceiptGateway,
        IReceiptPrinter receiptPrinter,
        IDelayProvider delay,
        ICurrentSession session,
        INavigationService navigation,
        MainMenuViewModel returnTo)
    {
        _salesRepository = salesRepository;
        _vanGateway = vanGateway;
        _cashReceiptGateway = cashReceiptGateway;
        _receiptPrinter = receiptPrinter;
        _delay = delay;
        _session = session;
        _navigation = navigation;
        _returnTo = returnTo;
        DateTo = DateTime.Today;
        DateFrom = DateTime.Today.AddDays(-6);
    }

    public async Task LoadAsync() => await RefreshAsync();

    [RelayCommand]
    private async Task Search() => await RefreshAsync();

    [RelayCommand]
    private async Task SelectPayTypeFilter(string payType)
    {
        PayTypeFilter = payType;
        await RefreshAsync();
    }

    private async Task RefreshAsync()
    {
        var from = (DateFrom ?? DateTime.Today).Date;
        var to = (DateTo ?? DateTime.Today).Date.AddDays(1);
        string? payType = PayTypeFilter == "ALL" ? null : PayTypeFilter;
        string? approvalNo = string.IsNullOrWhiteSpace(ApprovalNoFilter) ? null : ApprovalNoFilter.Trim();

        var sales = await _salesRepository.SearchSalesAsync(from, to, payType, approvalNo);

        Rows.Clear();
        foreach (var sale in sales)
        {
            var captured = sale;
            Rows.Add(new PaymentSummaryRowViewModel
            {
                SaleNo = captured.SaleNo,
                SaleDtStr = captured.SaleDt.ToString("yyyy-MM-dd HH:mm"),
                PayTypeLabel = captured.PayType switch
                {
                    "CASH" => "현금",
                    "CARD1" => "카드결제1",
                    "CARD2" => "카드결제2",
                    _ => captured.PayType,
                },
                TotalAmtStr = CurrencyFormat.Format(captured.TotalAmt),
                StatusLabel = captured.Status == "CANCELLED" ? "취소됨" : "정상",
                SelectCommand = new AsyncRelayCommand(() => OpenDetailAsync(captured.SaleNo)),
            });
        }
        HasRows = Rows.Count > 0;
    }

    private async Task OpenDetailAsync(long saleNo)
    {
        var result = await _salesRepository.GetSaleWithLinesAsync(saleNo);
        if (result is null) return;

        var detail = new TransactionDetailViewModel(
            result.Value.Header, result.Value.Lines, _session.CurrentTerminal!.PosCode,
            _salesRepository, _vanGateway, _cashReceiptGateway, _receiptPrinter, _delay);
        detail.Changed += async () => await RefreshAsync();
        detail.CloseRequested += () => IsDetailVisible = false;

        SelectedDetail = detail;
        IsDetailVisible = true;
    }

    [RelayCommand]
    private void GoToMainMenu() => _navigation.NavigateTo(_returnTo);
}
```

- [ ] **Step 5: 테스트 실행 (통과 확인)**

Run: `dotnet test tests/FishingMartPos.Tests --filter PaymentManagementViewModelTests`
Expected: 전체 PASS

- [ ] **Step 6: `PaymentManagementView.xaml.cs` 코드비하인드 생성**

`src/FishingMartPos/Views/PaymentManagementView.xaml.cs` 신규 생성:

```csharp
using System.Windows.Controls;

namespace FishingMartPos.Views;

public partial class PaymentManagementView : UserControl
{
    public PaymentManagementView()
    {
        InitializeComponent();
    }
}
```

- [ ] **Step 7: `PaymentManagementView.xaml` 생성**

`src/FishingMartPos/Views/PaymentManagementView.xaml` 신규 생성(검색 필터 + 목록 + 상세 팝업, `SalesReportView`의 날짜 필터 UX를 재사용):

```xml
<UserControl x:Class="FishingMartPos.Views.PaymentManagementView"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006"
             xmlns:d="http://schemas.microsoft.com/expression/blend/2008"
             xmlns:views="clr-namespace:FishingMartPos.Views"
             mc:Ignorable="d">
    <UserControl.Resources>
        <BooleanToVisibilityConverter x:Key="BooleanToVisibilityConverter" />
        <local:ZeroCountToVisibilityConverter x:Key="ZeroCountToVisibilityConverter" xmlns:local="clr-namespace:FishingMartPos.Converters" />
    </UserControl.Resources>
    <Grid Background="{DynamicResource MenuBackground}">
        <Grid.RowDefinitions>
            <RowDefinition Height="42" />
            <RowDefinition Height="Auto" />
            <RowDefinition Height="*" />
        </Grid.RowDefinitions>

        <Border Grid.Row="0" Background="White" BorderBrush="{DynamicResource HeaderBorder}" BorderThickness="0,0,0,1">
            <Grid Margin="16,0">
                <TextBlock Text="결제관리" FontSize="14" FontWeight="Bold" VerticalAlignment="Center"
                           Foreground="{DynamicResource TitleText}" />
                <Button Content="메인메뉴로" HorizontalAlignment="Right" Padding="10,4" FontSize="12"
                        Command="{Binding GoToMainMenuCommand}" />
            </Grid>
        </Border>

        <Border Grid.Row="1" Background="White" BorderBrush="{DynamicResource HeaderBorder}" BorderThickness="0,0,0,1" Padding="16,10">
            <StackPanel>
                <StackPanel Orientation="Horizontal" Margin="0,0,0,8">
                    <TextBlock Text="기간" VerticalAlignment="Center" Margin="0,0,8,0" FontSize="12" />
                    <DatePicker SelectedDate="{Binding DateFrom}" Width="130" Margin="0,0,4,0" />
                    <TextBlock Text="~" VerticalAlignment="Center" Margin="4,0" />
                    <DatePicker SelectedDate="{Binding DateTo}" Width="130" Margin="0,0,16,0" />
                    <TextBlock Text="승인번호" VerticalAlignment="Center" Margin="0,0,8,0" FontSize="12" />
                    <TextBox Text="{Binding ApprovalNoFilter, UpdateSourceTrigger=PropertyChanged}" Width="160" Margin="0,0,16,0" />
                    <Button Content="조회" Command="{Binding SearchCommand}" Padding="14,4"
                            Background="{DynamicResource Accent}" Foreground="White" />
                </StackPanel>
                <StackPanel Orientation="Horizontal">
                    <TextBlock Text="결제수단" VerticalAlignment="Center" Margin="0,0,8,0" FontSize="12" />
                    <Button Content="전체" Command="{Binding SelectPayTypeFilterCommand}" CommandParameter="ALL" Margin="0,0,4,0" Padding="10,4" />
                    <Button Content="현금" Command="{Binding SelectPayTypeFilterCommand}" CommandParameter="CASH" Margin="0,0,4,0" Padding="10,4" />
                    <Button Content="카드결제1" Command="{Binding SelectPayTypeFilterCommand}" CommandParameter="CARD1" Margin="0,0,4,0" Padding="10,4" />
                    <Button Content="카드결제2" Command="{Binding SelectPayTypeFilterCommand}" CommandParameter="CARD2" Padding="10,4" />
                </StackPanel>
            </StackPanel>
        </Border>

        <Grid Grid.Row="2" Margin="16">
            <TextBlock Text="조회된 거래가 없습니다" FontSize="13" Foreground="{DynamicResource MutedText}"
                       HorizontalAlignment="Center" VerticalAlignment="Center"
                       Visibility="{Binding Rows.Count, Converter={StaticResource ZeroCountToVisibilityConverter}}" />
            <ScrollViewer VerticalScrollBarVisibility="Auto">
                <ItemsControl ItemsSource="{Binding Rows}">
                    <ItemsControl.ItemTemplate>
                        <DataTemplate>
                            <Button Command="{Binding SelectCommand}" HorizontalContentAlignment="Stretch"
                                    Background="White" BorderBrush="{DynamicResource CardBorder}" BorderThickness="0,0,0,1"
                                    Padding="10,8" Margin="0,0,0,2">
                                <Grid>
                                    <Grid.ColumnDefinitions>
                                        <ColumnDefinition Width="1.4*" />
                                        <ColumnDefinition Width="*" />
                                        <ColumnDefinition Width="*" />
                                        <ColumnDefinition Width="0.6*" />
                                    </Grid.ColumnDefinitions>
                                    <TextBlock Text="{Binding SaleDtStr}" FontSize="12" VerticalAlignment="Center" />
                                    <TextBlock Grid.Column="1" Text="{Binding PayTypeLabel}" FontSize="12" VerticalAlignment="Center" />
                                    <TextBlock Grid.Column="2" Text="{Binding TotalAmtStr}" FontSize="12" FontWeight="Bold" VerticalAlignment="Center" HorizontalAlignment="Right" Margin="0,0,12,0" />
                                    <TextBlock Grid.Column="3" Text="{Binding StatusLabel}" FontSize="12" VerticalAlignment="Center" HorizontalAlignment="Right" />
                                </Grid>
                            </Button>
                        </DataTemplate>
                    </ItemsControl.ItemTemplate>
                </ItemsControl>
            </ScrollViewer>
        </Grid>

        <!-- 거래 상세 팝업 -->
        <Grid Grid.RowSpan="3" Background="{DynamicResource ModalOverlay}"
              Visibility="{Binding IsDetailVisible, Converter={StaticResource BooleanToVisibilityConverter}}">
            <views:TransactionDetailPopup DataContext="{Binding SelectedDetail}" />
        </Grid>
    </Grid>
</UserControl>
```

- [ ] **Step 8: 빌드 + 테스트 실행**

Run: `dotnet build src/FishingMartPos/FishingMartPos.csproj`
Run: `dotnet test tests/FishingMartPos.Tests`
Expected: 전체 통과

- [ ] **Step 9: 커밋**

```bash
git add src/FishingMartPos/ViewModels/PaymentManagementViewModel.cs src/FishingMartPos/ViewModels/PaymentSummaryRowViewModel.cs src/FishingMartPos/Views/PaymentManagementView.xaml src/FishingMartPos/Views/PaymentManagementView.xaml.cs tests/FishingMartPos.Tests/ViewModels/PaymentManagementViewModelTests.cs
git commit -m "결제관리 화면 추가 — 기간/결제수단/승인번호 검색 + 거래 상세 팝업"
```

---

## Task 8: 메인메뉴 5번째 타일 + 최종 DI 배선

**Files:**
- Modify: `src/FishingMartPos/ViewModels/MainMenuViewModel.cs`
- Modify: `src/FishingMartPos/Views/MainMenuView.xaml`
- Modify: `src/FishingMartPos/ViewModels/LoginViewModel.cs`
- Modify: `src/FishingMartPos/App.xaml.cs`
- Modify: `tests/FishingMartPos.Tests/ViewModels/MainMenuViewModelTests.cs`
- Modify: `tests/FishingMartPos.Tests/ViewModels/LoginViewModelTests.cs`

**Interfaces:**
- Consumes: `PaymentManagementViewModel`(Task 7).
- Produces: 메인메뉴 "결제관리" 타일 → `PaymentManagementView` 진입 가능.

**주의(과거 태스크에서 반복 발견된 패턴):** `LoginViewModel`/`MainMenuViewModel` 생성자가 바뀌면 `App.xaml.cs` 외에도 이 두 뷰모델을 직접 생성하는 테스트 파일이 있다. 이 태스크를 시작하기 전에 반드시 다음을 실행해서 전체 호출부를 먼저 확인할 것:

```bash
grep -rn "new LoginViewModel(\|new MainMenuViewModel(" src tests
```

- [ ] **Step 1: `MainMenuViewModel`에 팩토리/커맨드 추가**

`src/FishingMartPos/ViewModels/MainMenuViewModel.cs`에서 `SettingsViewModelFactory` 프로퍼티 바로 아래에 추가:

```csharp
    /// <summary>App.xaml.cs(또는 테스트)에서 주입하는 PaymentManagementViewModel 팩토리 — "결제관리" 진입 시 사용. 자신(this)을 넘겨줘 PaymentManagementViewModel이 "메인메뉴로" 복귀 시 재사용한다.</summary>
    public Func<MainMenuViewModel, Task<PaymentManagementViewModel>>? PaymentManagementViewModelFactory { get; init; }
```

`GoToSettings` 커맨드 메서드 바로 아래에 추가:

```csharp
    [RelayCommand]
    private async Task GoToPaymentManagement()
    {
        var paymentManagementViewModel = await PaymentManagementViewModelFactory!.Invoke(this);
        _navigation.NavigateTo(paymentManagementViewModel);
    }
```

- [ ] **Step 2: `MainMenuView.xaml`에 5번째 타일 추가**

`src/FishingMartPos/Views/MainMenuView.xaml`에서 `<UniformGrid Columns="2" Rows="2" HorizontalAlignment="Center" VerticalAlignment="Center" Width="460" Height="300">`를 다음으로 교체:

```xml
            <UniformGrid Columns="3" Rows="2" HorizontalAlignment="Center" VerticalAlignment="Center" Width="700" Height="300">
```

환경설정 타일(`GoToSettingsCommand`) `</Button>` 바로 다음에 추가:

```xml
                <Button Command="{Binding GoToPaymentManagementCommand}" Margin="10" Background="White"
                        BorderBrush="{DynamicResource MenuTileBorder}" BorderThickness="1">
                    <StackPanel Orientation="Vertical" HorizontalAlignment="Center">
                        <Canvas Width="48" Height="40" Margin="0,0,0,14">
                            <Border Width="34" Height="24" CornerRadius="3" BorderThickness="3"
                                    BorderBrush="{DynamicResource MenuIconInventory}" Background="Transparent"
                                    Canvas.Left="0" Canvas.Top="8" />
                            <Rectangle Width="34" Height="3" Fill="{DynamicResource MenuIconInventory}"
                                       Canvas.Left="0" Canvas.Top="16" />
                            <Ellipse Width="14" Height="14" Fill="{DynamicResource Accent}"
                                     Canvas.Left="28" Canvas.Top="26" />
                        </Canvas>
                        <TextBlock Text="결제관리" FontSize="16" FontWeight="Bold"
                                   Foreground="{DynamicResource MenuTileLabelText}" HorizontalAlignment="Center" />
                    </StackPanel>
                </Button>
```

- [ ] **Step 3: `LoginViewModel`에 팩토리 파라미터 추가**

`src/FishingMartPos/ViewModels/LoginViewModel.cs`의 필드 목록에 추가:

```csharp
    private readonly Func<MainMenuViewModel, Task<PaymentManagementViewModel>> _paymentManagementViewModelFactory;
```

생성자 시그니처를 다음으로 교체:

```csharp
    public LoginViewModel(
        IStaffRepository staffRepository,
        ICurrentSession session,
        INavigationService navigation,
        IReadOnlyList<PosTerminal> terminals,
        Func<MainMenuViewModel, Task<PosViewModel>> posViewModelFactory,
        Func<MainMenuViewModel, Task<InventoryViewModel>> inventoryViewModelFactory,
        Func<MainMenuViewModel, Task<SalesReportViewModel>> salesReportViewModelFactory,
        Func<MainMenuViewModel, Task<SettingsViewModel>> settingsViewModelFactory,
        Func<MainMenuViewModel, Task<PaymentManagementViewModel>> paymentManagementViewModelFactory)
```

생성자 본문 마지막(기존 필드 대입부 끝) 다음 줄에 추가:

```csharp
        _paymentManagementViewModelFactory = paymentManagementViewModelFactory;
```

`mainMenuViewModel` 생성부(로그인 성공 처리 메서드 안)를 다음으로 교체:

```csharp
        var mainMenuViewModel = new MainMenuViewModel(_session, _navigation)
        {
            LoginViewModelFactory = () => new LoginViewModel(_staffRepository, _session, _navigation, Terminals, _posViewModelFactory, _inventoryViewModelFactory, _salesReportViewModelFactory, _settingsViewModelFactory, _paymentManagementViewModelFactory),
            PosViewModelFactory = _posViewModelFactory,
            InventoryViewModelFactory = _inventoryViewModelFactory,
            SalesReportViewModelFactory = _salesReportViewModelFactory,
            SettingsViewModelFactory = _settingsViewModelFactory,
            PaymentManagementViewModelFactory = _paymentManagementViewModelFactory,
        };
```

- [ ] **Step 4: `App.xaml.cs`에 DI 배선 추가**

`src/FishingMartPos/App.xaml.cs`에서 `CreateSettingsViewModelAsync` 로컬 함수 바로 아래, `CreateLoginViewModel` 위에 추가:

```csharp
        async Task<PaymentManagementViewModel> CreatePaymentManagementViewModelAsync(MainMenuViewModel mainMenu)
        {
            var vm = new PaymentManagementViewModel(salesRepository, vanGateway, cashReceiptGateway, receiptPrinter, delayProvider, session, navigation, mainMenu);
            await vm.LoadAsync();
            return vm;
        }
```

`CreateLoginViewModel`을 다음으로 교체:

```csharp
        LoginViewModel CreateLoginViewModel() =>
            new(staffRepository, session, navigation, terminals, CreatePosViewModelAsync, CreateInventoryViewModelAsync, CreateSalesReportViewModelAsync, CreateSettingsViewModelAsync, CreatePaymentManagementViewModelAsync);
```

- [ ] **Step 5: 다른 태스크에서 발견되었던 것과 같은 패턴 — `LoginViewModel`/`MainMenuViewModel`을 직접 생성하는 테스트 수정**

Step에서 미리 실행한 `grep -rn "new LoginViewModel(\|new MainMenuViewModel("` 결과에 나온 모든 호출부를 확인한다. 최소한 다음 두 파일이 해당될 가능성이 높다(Feature A 때도 동일한 패턴이 발견됨 — `MainMenuViewModelTests.cs`, `LoginViewModelTests.cs`):

- `tests/FishingMartPos.Tests/ViewModels/MainMenuViewModelTests.cs`: `new MainMenuViewModel(...)`를 생성한 뒤 `PaymentManagementViewModelFactory`를 세팅하지 않는 기존 테스트가 있다면, `GoToPaymentManagementCommand`를 호출하는 테스트가 아닌 한 그대로 둬도 컴파일은 된다(옵셔널 `init` 프로퍼티라 필수 아님). 다만 "결제관리 진입" 동작을 검증하는 새 테스트를 추가한다:

```csharp
    [Fact]
    public async Task GoToPaymentManagement_InvokesFactoryAndNavigates()
    {
        var session = new CurrentSession();
        session.SignIn(
            new Staff { StaffCode = "S1", StaffName = "직원", Role = "STAFF", UseYn = "Y" },
            new PosTerminal { PosCode = "1", PosName = "POS1" });
        var navigation = new NavigationService();
        var vm = new MainMenuViewModel(session, navigation);
        PaymentManagementViewModel? created = null;
        vm.PaymentManagementViewModelFactory = _ =>
        {
            created = new PaymentManagementViewModel(
                new FakeSalesRepository(),
                new FakeVanPaymentGateway(new VanApprovalResult { IsApproved = true, ResponseMessage = "ok" }),
                new FakeCashReceiptGateway(new CashReceiptResult { IsIssued = true, ResponseMessage = "ok" }),
                new StubReceiptPrinter(), new FakeDelayProvider(), session, navigation, vm);
            return Task.FromResult(created);
        };

        await vm.GoToPaymentManagementCommand.ExecuteAsync(null);

        Assert.NotNull(created);
        Assert.Same(created, navigation.CurrentViewModel);
    }
```

(위 테스트의 `Staff`/`PosTerminal`/`CurrentSession`/`NavigationService` 생성 방식은 파일 상단의 기존 `Create()` 헬퍼가 실제로 쓰는 방식과 동일하다 — 다르면 그 방식으로 맞춰 쓴다. `ADMIN` 게이트가 없으므로 `Role = "STAFF"`인 세션으로도 성공해야 하는 것이 이 테스트의 핵심 포인트다.)

- `tests/FishingMartPos.Tests/ViewModels/LoginViewModelTests.cs`: `new LoginViewModel(...)`를 직접 호출하는 모든 곳(`CreateViewModel` 헬퍼)에 마지막 인자로 `PaymentManagementViewModel` 팩토리를 추가해야 컴파일된다. `CreateDummyPosViewModelFactory`류 헬퍼들 옆에 다음을 추가:

```csharp
    private static Func<MainMenuViewModel, Task<PaymentManagementViewModel>> CreateDummyPaymentManagementViewModelFactory(
        ICurrentSession session, INavigationService navigation) =>
        mainMenu => Task.FromResult(new PaymentManagementViewModel(
            new FakeSalesRepository(),
            new FakeVanPaymentGateway(new VanApprovalResult
            {
                IsApproved = true,
                ApprovalNo = "20260723120000",
                VanCode = "KICC",
                ResponseMessage = "카드 결제 완료",
            }),
            new FakeCashReceiptGateway(new CashReceiptResult
            {
                IsIssued = true,
                ApprovalNo = "149331691",
                ApprovalDateYyMmDd = "250704",
                ResponseMessage = "현금영수증 발급 완료",
            }),
            new StubReceiptPrinter(), new FakeDelayProvider(), session, navigation, mainMenu));
```

그리고 `CreateViewModel` 헬퍼의 `new LoginViewModel(...)` 호출 마지막 인자로 추가:

```csharp
        return new LoginViewModel(
            repository, session, navigation, terminals,
            CreateDummyPosViewModelFactory(session, navigation),
            CreateDummyInventoryViewModelFactory(session, navigation),
            CreateDummySalesReportViewModelFactory(session, navigation),
            CreateDummySettingsViewModelFactory(navigation),
            CreateDummyPaymentManagementViewModelFactory(session, navigation));
```

- [ ] **Step 6: 빌드 + 전체 테스트 실행**

Run: `dotnet build src/FishingMartPos/FishingMartPos.csproj`
Expected: 0 경고 / 0 오류

Run: `dotnet test tests/FishingMartPos.Tests`
Expected: 전체 통과

- [ ] **Step 7: 커밋**

```bash
git add src/FishingMartPos/ViewModels/MainMenuViewModel.cs src/FishingMartPos/Views/MainMenuView.xaml src/FishingMartPos/ViewModels/LoginViewModel.cs src/FishingMartPos/App.xaml.cs tests/FishingMartPos.Tests/ViewModels/MainMenuViewModelTests.cs tests/FishingMartPos.Tests/ViewModels/LoginViewModelTests.cs
git commit -m "메인메뉴에 결제관리 타일 추가 + 전체 DI 배선 연결"
```

---

## Task 9: POS 화면 "영수증관리" 버튼 → 결제관리 화면 연결

`docs/superpowers/specs/2026-07-26-payment-management-design.md` 작성 이후 사용자가 실제 레거시 화면 사진(영수증관리)을 추가로 제공했고, 이 화면이 방금 만든 결제관리 화면(거래 목록 + 재발행)과 사실상 동일하다고 확인했다. 그래서 POS 화면의 세 번째 비활성 버튼("영수증관리", `영수증발행`과는 다른 버튼)도 같은 `PaymentManagementView`를 열도록 연결한다.

**Files:**
- Modify: `src/FishingMartPos/ViewModels/PosViewModel.cs`
- Modify: `src/FishingMartPos/Views/PosView.xaml`
- Modify: `src/FishingMartPos/App.xaml.cs`
- Modify: `tests/FishingMartPos.Tests/ViewModels/PosViewModelTests.cs`
- Modify: `tests/FishingMartPos.Tests/ViewModels/PosViewModelPaymentTests.cs`
- Modify: `tests/FishingMartPos.Tests/ViewModels/MainMenuViewModelTests.cs`
- Modify: `tests/FishingMartPos.Tests/ViewModels/LoginViewModelTests.cs`

**Interfaces:**
- Consumes: `PaymentManagementViewModel`(Task 7), `MainMenuViewModel.PaymentManagementViewModelFactory`(Task 8) — 정확히는 App.xaml.cs가 이미 만들어 둔 `CreatePaymentManagementViewModelAsync` 로컬 함수를 재사용한다.
- Produces: `PosViewModel`의 새 생성자 파라미터(마지막 자리) `Func<MainMenuViewModel, Task<PaymentManagementViewModel>> paymentManagementViewModelFactory`, 새 커맨드 `ShowPaymentManagementCommand`.

**주의(반복 패턴):** `PosViewModel` 생성자가 바뀌므로, 이 태스크를 시작하기 전에 실제 호출부를 전부 확인할 것:

```bash
grep -rn "new PosViewModel(" src tests
```
최소 4곳(App.xaml.cs, `PosViewModelTests.cs`, `PosViewModelPaymentTests.cs`, `MainMenuViewModelTests.cs`, `LoginViewModelTests.cs`)이 나올 것이다 — 전부 수정 대상이다.

- [ ] **Step 1: `PosViewModel` 생성자에 파라미터 추가**

`src/FishingMartPos/ViewModels/PosViewModel.cs`의 필드 목록(`private readonly IKiccPosClient? _kiccPosClient;` 아래)에 추가:

```csharp
    private readonly Func<MainMenuViewModel, Task<PaymentManagementViewModel>> _paymentManagementViewModelFactory;
```

생성자 시그니처 마지막 파라미터(`IKiccPosClient? kiccPosClient = null`) 앞에 추가(옵셔널 파라미터는 항상 마지막에 와야 하므로 순서 주의):

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
        IVanPaymentGateway vanGateway,
        ICashReceiptGateway cashReceiptGateway,
        IReceiptPrinter receiptPrinter,
        Func<MainMenuViewModel, Task<PaymentManagementViewModel>> paymentManagementViewModelFactory,
        IKiccPosClient? kiccPosClient = null)
```

생성자 본문에 대입 추가(`_receiptPrinter = receiptPrinter;` 다음 줄):

```csharp
        _paymentManagementViewModelFactory = paymentManagementViewModelFactory;
```

`GoToMainMenu` 커맨드 바로 아래에 새 커맨드 추가:

```csharp
    [RelayCommand]
    private async Task ShowPaymentManagement()
    {
        var vm = await _paymentManagementViewModelFactory.Invoke(_mainMenuViewModel);
        _navigation.NavigateTo(vm);
    }
```

- [ ] **Step 2: `PosView.xaml`에서 "영수증관리" 버튼 활성화**

`<Button Content="영수증관리" IsEnabled="False" Margin="3" />` 줄을 다음으로 교체:

```xml
                        <Button Content="영수증관리" Command="{Binding ShowPaymentManagementCommand}" Margin="3" />
```

(`영수증발행` 버튼은 이번 범위에 포함되지 않으므로 `IsEnabled="False"`로 그대로 둔다.)

- [ ] **Step 3: `App.xaml.cs`의 `CreatePosViewModelAsync`에 인자 추가**

`src/FishingMartPos/App.xaml.cs`의 `CreatePosViewModelAsync` 로컬 함수를 다음으로 교체(Task 8에서 이미 정의한 `CreatePaymentManagementViewModelAsync`를 그대로 전달):

```csharp
        async Task<PosViewModel> CreatePosViewModelAsync(MainMenuViewModel mainMenu)
        {
            var vm = new PosViewModel(productRepository, codeRepository, salesRepository, heldOrderRepository, delayProvider, session, navigation, mainMenu, vanGateway, cashReceiptGateway, receiptPrinter, CreatePaymentManagementViewModelAsync, kiccPosClient);
            await vm.LoadAsync();
            return vm;
        }
```

`CreatePosViewModelAsync`가 `CreatePaymentManagementViewModelAsync`보다 파일에서 먼저 선언돼 있어도 로컬 함수는 선언 순서에 상관없이 서로 참조 가능하므로(C# 로컬 함수는 호이스팅됨) 문제 없다.

- [ ] **Step 4: `PosViewModelTests.cs`의 `CreateViewModel` 헬퍼 수정**

`tests/FishingMartPos.Tests/ViewModels/PosViewModelTests.cs`의 `CreateViewModel` 헬퍼에서 `return new PosViewModel(...)` 호출을 다음으로 교체(마지막 두 인자 순서 주의 — `kiccPosClient`가 진짜 마지막):

```csharp
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
            _ => Task.FromResult(new PaymentManagementViewModel(
                sales, vanGateway ?? new FakeVanPaymentGateway(ApprovedResult),
                cashReceiptGateway ?? new FakeCashReceiptGateway(new CashReceiptResult { IsIssued = true, ResponseMessage = "ok" }),
                receiptPrinter ?? new StubReceiptPrinter(), new FakeDelayProvider(), session, navigation, mainMenuViewModel)),
            kiccPosClient);
```

- [ ] **Step 5: `PosViewModel` "영수증관리" 커맨드 테스트 추가**

같은 파일에 추가:

```csharp
    [Fact]
    public async Task ShowPaymentManagement_NavigatesToPaymentManagementViewModel()
    {
        var vm = CreateViewModel(out _, out _, out var navigation, out _);

        await vm.ShowPaymentManagementCommand.ExecuteAsync(null);

        Assert.IsType<PaymentManagementViewModel>(navigation.CurrentViewModel);
    }
```

- [ ] **Step 6: `PosViewModelPaymentTests.cs`의 `CreateViewModel` 헬퍼 수정**

`tests/FishingMartPos.Tests/ViewModels/PosViewModelPaymentTests.cs`의 `return new PosViewModel(...)` 호출을 다음으로 교체:

```csharp
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
            receiptPrinter ?? new StubReceiptPrinter(),
            _ => Task.FromResult(new PaymentManagementViewModel(
                sales, new FakeVanPaymentGateway(new VanApprovalResult { IsApproved = true, ResponseMessage = "ok" }),
                new FakeCashReceiptGateway(new CashReceiptResult { IsIssued = true, ResponseMessage = "ok" }),
                new StubReceiptPrinter(), new FakeDelayProvider(), session, navigation, mainMenuViewModel)));
```

- [ ] **Step 7: `MainMenuViewModelTests.cs`의 직접 `new PosViewModel(...)` 호출 수정**

`tests/FishingMartPos.Tests/ViewModels/MainMenuViewModelTests.cs`의 `Create()` 헬퍼에서 `var posViewModel = new PosViewModel(...)` 호출 마지막에 다음 인자를 추가:

```csharp
            new StubReceiptPrinter(),
            _ => Task.FromResult(new PaymentManagementViewModel(
                new FakeSalesRepository(),
                new FakeVanPaymentGateway(new VanApprovalResult { IsApproved = true, ResponseMessage = "ok" }),
                new FakeCashReceiptGateway(new CashReceiptResult { IsIssued = true, ResponseMessage = "ok" }),
                new StubReceiptPrinter(), new FakeDelayProvider(), session, navigation, new MainMenuViewModel(session, navigation))));
```

(기존 호출이 `new StubReceiptPrinter());`로 끝나 있던 자리를 위 블록으로 교체하는 것 — 괄호 닫는 위치에 주의.)

- [ ] **Step 8: `LoginViewModelTests.cs`의 `CreateDummyPosViewModelFactory` 수정**

`tests/FishingMartPos.Tests/ViewModels/LoginViewModelTests.cs`의 `CreateDummyPosViewModelFactory`에서 `new StubReceiptPrinter()));`로 끝나는 마지막 줄을 다음으로 교체:

```csharp
            new StubReceiptPrinter(),
            _ => Task.FromResult(new PaymentManagementViewModel(
                new FakeSalesRepository(),
                new FakeVanPaymentGateway(new VanApprovalResult { IsApproved = true, ResponseMessage = "ok" }),
                new FakeCashReceiptGateway(new CashReceiptResult { IsIssued = true, ResponseMessage = "ok" }),
                new StubReceiptPrinter(), new FakeDelayProvider(), session, navigation, mainMenu))));
```

이 파일은 이미 Task 8의 Step 5에서 `CreateDummyPaymentManagementViewModelFactory`라는 별도 헬퍼를 추가했다 — 위처럼 인라인으로 작성하는 대신 그 헬퍼를 재사용해도 된다(`CreateDummyPaymentManagementViewModelFactory(session, navigation)`를 그대로 마지막 인자로 전달). 어느 쪽이든 컴파일되고 동작이 동일하면 무방하다.

- [ ] **Step 9: 빌드 + 전체 테스트 실행**

Run: `dotnet build src/FishingMartPos/FishingMartPos.csproj`
Expected: 0 경고 / 0 오류

Run: `dotnet test tests/FishingMartPos.Tests`
Expected: 전체 통과

- [ ] **Step 10: 커밋**

```bash
git add src/FishingMartPos/ViewModels/PosViewModel.cs src/FishingMartPos/Views/PosView.xaml src/FishingMartPos/App.xaml.cs tests/FishingMartPos.Tests/ViewModels/PosViewModelTests.cs tests/FishingMartPos.Tests/ViewModels/PosViewModelPaymentTests.cs tests/FishingMartPos.Tests/ViewModels/MainMenuViewModelTests.cs tests/FishingMartPos.Tests/ViewModels/LoginViewModelTests.cs
git commit -m "POS 화면 영수증관리 버튼을 결제관리 화면에 연결"
```

---

## 다음 작업과의 관계

- **Feature B(카드결제 서명, 5만원 이상)**: 이번 작업과 독립적. 카드결제 승인 팝업(`PosView.xaml`의 카드결제 팝업) 쪽에 서명 캡처를 추가하는 별도 작업이 될 것으로 예상.
- 이번 작업으로 `ICashReceiptGateway.RequestCancelAsync`(B2)가 실제로 UI에서 호출되는 경로(거래취소 시 자동 호출, 또는 "현금영수증취소" 단독 액션 필요 여부)가 생겼다 — 향후 요구사항에 "현금영수증만 별도로 취소"가 추가되면 `TransactionDetailViewModel`에 별도 커맨드를 얹으면 된다(현재는 거래취소에 종속되어 있음).
