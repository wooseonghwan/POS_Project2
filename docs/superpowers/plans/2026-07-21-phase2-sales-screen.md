# Phase 2: 판매 화면 핵심 (장바구니/카테고리/키패드/현금결제/보류) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** `Fishing Mart POS.html`의 판매(POS) 화면을 픽셀 그대로 WPF로 이식하고, 카테고리별 상품 그리드 → 장바구니 담기/수량조절/삭제 → 숫자 키패드(수량 입력·현금 입력 겸용) → 현금/카드결제1/카드결제2 결제(VAN 연동은 Phase 3에서 붙일 스텁) → 보류(홀드)·리콜까지 실제 MariaDB 연동으로 동작하게 만든다.

**Architecture:** Phase 1과 동일한 3계층(View–ViewModel–Repository)을 유지한다. 장바구니는 순수 C# 도메인 클래스(`Cart`)로 분리해 UI와 무관하게 단위 테스트하고, `PosViewModel`이 이를 감싸 화면 바인딩용 컬렉션을 관리한다. 결제는 `ISalesRepository`가 트랜잭션으로 매출헤더/상세 저장 + 재고 차감을 한 번에 처리한다.

**Tech Stack:** Phase 1과 동일(WPF/.NET8/x86, CommunityToolkit.Mvvm, Dapper, MySqlConnector). 신규: 결제/보류 완료 토스트의 지연 처리를 테스트 가능하게 만들기 위한 `IDelayProvider` 추상화.

## Global Constraints

- 화면 디자인(레이아웃/색상/간격/문구)은 `Fishing Mart POS.html`의 판매(POS) 섹션을 그대로 따른다 — 임의 변경 금지.
- 카드결제1/카드결제2는 이번 단계에서 **VAN 연동 없이 즉시 완료 처리**한다(`van_code`는 NULL로 저장). 실제 NCVAN/KICC 연동은 Phase 3.
- 영수증 인쇄/돈통 오픈은 이번 단계에서 다루지 않는다(Phase 4). "영수증관리/직전정보/영수증발행/돈통열기" 버튼은 화면에는 그대로 두되 Phase 2에서는 비활성(추후 연결 예정 표시)으로 둔다.
- 보류(홀드) UI는 설계문서(2-1절)대로 장바구니 헤더 위에 최소 침습적으로 얹는다.
- 플랫폼/DB/커밋 규칙은 Phase 1 계획 문서(`docs/superpowers/plans/2026-07-21-phase1-foundation.md`)의 Global Constraints를 그대로 따른다.
- 이 저장소는 `C:\AI\pos-project2`, 기본 브랜치 `main`, 로컬 개발 DB `fishingmart_dev`(MariaDB, `db/migrations/001_create_schema.sql` 적용됨).

---

## 파일 구조 (Phase 1 기준 추가/변경분만)

```
db/
  migrations/
    002_seed_products.sql                 (신규)
src/
  FishingMartPos/
    Theme/
      AppColors.cs                        (수정: 상품 타일 스와치 색상 6개 추가)
    Models/
      Product.cs                          (신규)
      CodeItem.cs                         (신규)
      CartLine.cs                         (신규)
      SaleHeader.cs                       (신규)
      SaleDetailLine.cs                   (신규)
      HeldOrderLine.cs                    (신규)
    Domain/
      Cart.cs                             (신규)
    Repositories/
      IProductRepository.cs / ProductRepository.cs     (신규)
      ICodeRepository.cs / CodeRepository.cs           (신규)
      ISalesRepository.cs / SalesRepository.cs         (신규)
      IHeldOrderRepository.cs / HeldOrderRepository.cs (신규)
    Services/
      IDelayProvider.cs / DelayProvider.cs (신규)
    ViewModels/
      CartLineViewModel.cs                (신규)
      CategoryTabViewModel.cs             (신규)
      ProductTileViewModel.cs             (신규)
      HeldOrderSummaryViewModel.cs        (신규)
      PosViewModel.cs                     (신규)
      MainMenuViewModel.cs                (수정: GoToSales가 PosViewModel로 이동)
    Views/
      PosView.xaml / PosView.xaml.cs      (신규)
    App.xaml.cs                           (수정: 신규 리포지토리/뷰모델 DI 등록)
tests/
  FishingMartPos.Tests/
    Theme/AppColorsTests.cs               (수정: 스와치 추가로 개수 검증값 변경)
    Domain/CartTests.cs                   (신규)
    Repositories/ProductRepositoryTests.cs / CodeRepositoryTests.cs / SalesRepositoryTests.cs / HeldOrderRepositoryTests.cs (신규)
    Fakes/FakeProductRepository.cs / FakeCodeRepository.cs / FakeSalesRepository.cs / FakeHeldOrderRepository.cs / FakeDelayProvider.cs (신규)
    ViewModels/PosViewModelTests.cs / PosViewModelPaymentTests.cs (신규)
    ViewModels/MainMenuViewModelTests.cs  (수정: GoToSales 기대값 변경)
    Views/PosViewSmokeTests.cs            (신규)
```

---

### Task 1: 상품/카테고리 시드 데이터 마이그레이션

**Files:**
- Create: `db/migrations/002_seed_products.sql`

**Interfaces:**
- Produces: `code_tb`에 POSCAT 8종 + MAJOR 4종 + MINOR 6종, `product_tb`에 43개 상품(디자인 프로토타입 `CATALOG`/`CATEGORY_META`와 동일한 이름·가격·분류)

- [ ] **Step 1: 스크립트 작성**

`db/migrations/002_seed_products.sql`:

```sql
-- 002_seed_products.sql
-- 판매 화면 카테고리(POS분류)·대/소분류 코드 + 상품 시드 데이터
-- Fishing Mart POS.html의 CATALOG/CATEGORY_META를 그대로 옮김

INSERT INTO code_tb (code_gbn, code_cd, code_nm, sort_no) VALUES
    ('POSCAT', 'BAIT', '미끼', 1),
    ('POSCAT', 'FLOAT', '찌세트', 2),
    ('POSCAT', 'HOOK', '바늘', 3),
    ('POSCAT', 'LIFE', '생활용품', 4),
    ('POSCAT', 'SEAFOOD', '수산', 5),
    ('POSCAT', 'HAT', '모자', 6),
    ('POSCAT', 'ICE', '아이스크림', 7),
    ('POSCAT', 'DRINK', '주류/음료/식품', 8),
    ('MAJOR', 'FISH', '낚시용품', 1),
    ('MAJOR', 'LIFE', '생활용품', 2),
    ('MAJOR', 'WEAR', '의류잡화', 3),
    ('MAJOR', 'FOOD', '식품', 4),
    ('MINOR', 'BAIT', '미끼', 1),
    ('MINOR', 'TACKLE', '채비', 2),
    ('MINOR', 'GOODS', '잡화', 3),
    ('MINOR', 'HAT', '모자', 4),
    ('MINOR', 'ICE', '아이스크림', 5),
    ('MINOR', 'DRINK', '음료/주류', 6)
ON DUPLICATE KEY UPDATE code_nm = VALUES(code_nm);

INSERT INTO product_tb (barcode, major_cd, minor_cd, poscat_cd, name, price, stock_qty, use_yn) VALUES
    ('8800000020001', 'FISH', 'BAIT', 'BAIT', '지렁이', 5000, 50, 'Y'),
    ('8800000020002', 'FISH', 'BAIT', 'BAIT', '냉동새우', 5000, 50, 'Y'),
    ('8800000020003', 'FISH', 'BAIT', 'BAIT', '오징어', 5000, 50, 'Y'),
    ('8800000020004', 'FISH', 'BAIT', 'BAIT', '꼴뚜기', 5000, 50, 'Y'),
    ('8800000020005', 'FISH', 'BAIT', 'BAIT', '생새우', 10000, 50, 'Y'),
    ('8800000020006', 'FISH', 'BAIT', 'BAIT', '멸치', 5000, 50, 'Y'),
    ('8800000020007', 'FISH', 'BAIT', 'BAIT', '빨간 오징어', 6000, 50, 'Y'),
    ('8800000020008', 'FISH', 'BAIT', 'BAIT', '각크릴', 5000, 50, 'Y'),
    ('8800000020009', 'FISH', 'BAIT', 'BAIT', '숭어 떡밥', 5000, 50, 'Y'),
    ('8800000020010', 'FISH', 'BAIT', 'BAIT', '크릴 밑밥', 13000, 50, 'Y'),
    ('8800000020011', 'FISH', 'BAIT', 'BAIT', '크릴 밑밥(중지)', 12000, 50, 'Y'),
    ('8800000020012', 'FISH', 'BAIT', 'BAIT', '크릴 새우(중지)', 6500, 50, 'Y'),
    ('8800000020013', 'FISH', 'BAIT', 'BAIT', '파우더', 4500, 50, 'Y'),
    ('8800000020014', 'FISH', 'TACKLE', 'FLOAT', '막대찌 세트', 8000, 50, 'Y'),
    ('8800000020015', 'FISH', 'TACKLE', 'FLOAT', '전자찌', 15000, 50, 'Y'),
    ('8800000020016', 'FISH', 'TACKLE', 'FLOAT', '구멍찌', 9000, 50, 'Y'),
    ('8800000020017', 'FISH', 'TACKLE', 'FLOAT', '수중찌', 4000, 50, 'Y'),
    ('8800000020018', 'FISH', 'TACKLE', 'FLOAT', '채비 세트', 12000, 50, 'Y'),
    ('8800000020019', 'FISH', 'TACKLE', 'FLOAT', '찌 고무', 1500, 50, 'Y'),
    ('8800000020020', 'FISH', 'TACKLE', 'HOOK', '감성돔 바늘', 3000, 50, 'Y'),
    ('8800000020021', 'FISH', 'TACKLE', 'HOOK', '벵에돔 바늘', 3000, 50, 'Y'),
    ('8800000020022', 'FISH', 'TACKLE', 'HOOK', '우럭 바늘', 2500, 50, 'Y'),
    ('8800000020023', 'FISH', 'TACKLE', 'HOOK', '원줄 세트', 6000, 50, 'Y'),
    ('8800000020024', 'FISH', 'TACKLE', 'HOOK', '목줄', 4000, 50, 'Y'),
    ('8800000020025', 'FISH', 'TACKLE', 'HOOK', '봉돌 세트', 2000, 50, 'Y'),
    ('8800000020026', 'LIFE', 'GOODS', 'LIFE', '낚시장갑', 7000, 50, 'Y'),
    ('8800000020027', 'LIFE', 'GOODS', 'LIFE', '아이스박스', 25000, 50, 'Y'),
    ('8800000020028', 'LIFE', 'GOODS', 'LIFE', '우비', 12000, 50, 'Y'),
    ('8800000020029', 'FISH', 'BAIT', 'SEAFOOD', '지렁이', 5000, 50, 'Y'),
    ('8800000020030', 'FISH', 'BAIT', 'SEAFOOD', '냉동새우', 5000, 50, 'Y'),
    ('8800000020031', 'FISH', 'BAIT', 'SEAFOOD', '오징어', 5000, 50, 'Y'),
    ('8800000020032', 'FISH', 'BAIT', 'SEAFOOD', '생새우', 10000, 50, 'Y'),
    ('8800000020033', 'WEAR', 'HAT', 'HAT', '캡모자', 9000, 50, 'Y'),
    ('8800000020034', 'WEAR', 'HAT', 'HAT', '버킷햇', 12000, 50, 'Y'),
    ('8800000020035', 'WEAR', 'HAT', 'HAT', '넥게이터', 6000, 50, 'Y'),
    ('8800000020036', 'FOOD', 'ICE', 'ICE', '바닐라콘', 2000, 50, 'Y'),
    ('8800000020037', 'FOOD', 'ICE', 'ICE', '초코바', 2500, 50, 'Y'),
    ('8800000020038', 'FOOD', 'ICE', 'ICE', '아이스크림바', 2000, 50, 'Y'),
    ('8800000020039', 'FOOD', 'DRINK', 'DRINK', '생수', 1000, 50, 'Y'),
    ('8800000020040', 'FOOD', 'DRINK', 'DRINK', '이온음료', 1800, 50, 'Y'),
    ('8800000020041', 'FOOD', 'DRINK', 'DRINK', '컵라면', 2500, 50, 'Y'),
    ('8800000020042', 'FOOD', 'DRINK', 'DRINK', '맥주', 3000, 50, 'Y'),
    ('8800000020043', 'FOOD', 'DRINK', 'DRINK', '소주', 3500, 50, 'Y')
ON DUPLICATE KEY UPDATE name = VALUES(name);
```

- [ ] **Step 2: 로컬 개발 DB에 적용**

```bash
mysql -h 127.0.0.1 -u root -p fishingmart_dev < db\migrations\002_seed_products.sql
```

- [ ] **Step 3: 확인**

```bash
mysql -h 127.0.0.1 -u root -p fishingmart_dev -e "SELECT COUNT(*) FROM product_tb; SELECT code_gbn, COUNT(*) FROM code_tb GROUP BY code_gbn;"
```

Expected: `product_tb` 43행, `code_tb`는 `MAJOR` 4 / `MINOR` 6 / `POSCAT` 8 (기존 것 있으면 합산).

- [ ] **Step 4: Commit**

```bash
git add db/migrations/002_seed_products.sql
git commit -m "DB 마이그레이션 002: 판매화면 카테고리 코드 및 상품 43종 시드"
```

---

### Task 2: 상품 타일용 스와치 색상 추가 (AppColors 확장)

**Files:**
- Modify: `src/FishingMartPos/Theme/AppColors.cs`
- Modify: `tests/FishingMartPos.Tests/Theme/AppColorsTests.cs`

**Interfaces:**
- Produces: `AppColors.BuildBrushes()`에 `ProductSwatch0`~`ProductSwatch5` 6개 추가 (총 35개)

디자인 원본의 `SWATCHES = ['oklch(0.55 0.09 195)','oklch(0.55 0.09 250)','oklch(0.55 0.09 30)','oklch(0.55 0.09 340)','oklch(0.55 0.09 150)','oklch(0.55 0.09 80)']`를 그대로 옮긴다.

- [ ] **Step 1: 실패하는 테스트로 개수 기대값 수정**

`tests/FishingMartPos.Tests/Theme/AppColorsTests.cs`의 `ContainsAllTwentyNineNamedColors` 테스트를 아래로 교체:

```csharp
    [Fact]
    public void ContainsAllThirtyFiveNamedColors()
    {
        var brushes = AppColors.BuildBrushes();

        Assert.Equal(35, brushes.Count);
    }
```

- [ ] **Step 2: 테스트 실패 확인**

```bash
dotnet test tests\FishingMartPos.Tests --filter FullyQualifiedName~AppColorsTests
```

Expected: FAIL (`ContainsAllThirtyFiveNamedColors`에서 실제 29 vs 기대 35)

- [ ] **Step 3: 구현**

`src/FishingMartPos/Theme/AppColors.cs`의 `map` 딕셔너리 마지막 항목(`["MenuIconSettingsTrack"]`) 바로 뒤에 추가:

```csharp
            ["ProductSwatch0"] = (0.55, 0.09, 195, 1.0),
            ["ProductSwatch1"] = (0.55, 0.09, 250, 1.0),
            ["ProductSwatch2"] = (0.55, 0.09, 30, 1.0),
            ["ProductSwatch3"] = (0.55, 0.09, 340, 1.0),
            ["ProductSwatch4"] = (0.55, 0.09, 150, 1.0),
            ["ProductSwatch5"] = (0.55, 0.09, 80, 1.0),
```

- [ ] **Step 4: 테스트 통과 확인**

```bash
dotnet test tests\FishingMartPos.Tests --filter FullyQualifiedName~AppColorsTests
```

Expected: PASS (4 tests)

- [ ] **Step 5: Commit**

```bash
git add src/FishingMartPos/Theme/AppColors.cs tests/FishingMartPos.Tests/Theme/AppColorsTests.cs
git commit -m "상품 타일용 스와치 색상 6종 추가"
```

---

### Task 3: Cart 도메인 모델 (순수 C#, TDD)

**Files:**
- Create: `src/FishingMartPos/Models/CartLine.cs`
- Create: `src/FishingMartPos/Domain/Cart.cs`
- Test: `tests/FishingMartPos.Tests/Domain/CartTests.cs`

**Interfaces:**
- Produces: `CartLine { Barcode, Name, Price, Qty, LineTotal }`, `Cart { IReadOnlyList<CartLine> Lines, decimal Total, int TotalQty, void Add(barcode,name,price), void SetQty(barcode,qty), void Increment(barcode), void Decrement(barcode), void Remove(barcode), void Clear() }`

- [ ] **Step 1: 실패하는 테스트 작성**

`tests/FishingMartPos.Tests/Domain/CartTests.cs`:

```csharp
using FishingMartPos.Domain;
using Xunit;

namespace FishingMartPos.Tests.Domain;

public class CartTests
{
    [Fact]
    public void Add_NewItem_CreatesLineWithQtyOne()
    {
        var cart = new Cart();

        cart.Add("B1", "지렁이", 5000);

        var line = Assert.Single(cart.Lines);
        Assert.Equal("B1", line.Barcode);
        Assert.Equal(1, line.Qty);
        Assert.Equal(5000, line.LineTotal);
    }

    [Fact]
    public void Add_ExistingItem_IncrementsQty()
    {
        var cart = new Cart();
        cart.Add("B1", "지렁이", 5000);

        cart.Add("B1", "지렁이", 5000);

        var line = Assert.Single(cart.Lines);
        Assert.Equal(2, line.Qty);
        Assert.Equal(10000, line.LineTotal);
    }

    [Fact]
    public void SetQty_ClampsToMinimumOne()
    {
        var cart = new Cart();
        cart.Add("B1", "지렁이", 5000);

        cart.SetQty("B1", 0);

        Assert.Equal(1, cart.Lines[0].Qty);
    }

    [Fact]
    public void Increment_And_Decrement_AdjustQty()
    {
        var cart = new Cart();
        cart.Add("B1", "지렁이", 5000);

        cart.Increment("B1");
        cart.Increment("B1");
        cart.Decrement("B1");

        Assert.Equal(2, cart.Lines[0].Qty);
    }

    [Fact]
    public void Decrement_NeverGoesBelowOne()
    {
        var cart = new Cart();
        cart.Add("B1", "지렁이", 5000);

        cart.Decrement("B1");
        cart.Decrement("B1");

        Assert.Equal(1, cart.Lines[0].Qty);
    }

    [Fact]
    public void Remove_DeletesLine()
    {
        var cart = new Cart();
        cart.Add("B1", "지렁이", 5000);

        cart.Remove("B1");

        Assert.Empty(cart.Lines);
    }

    [Fact]
    public void Total_And_TotalQty_SumAcrossLines()
    {
        var cart = new Cart();
        cart.Add("B1", "지렁이", 5000);
        cart.Add("B2", "냉동새우", 3000);
        cart.Increment("B2");

        Assert.Equal(11000, cart.Total);
        Assert.Equal(3, cart.TotalQty);
    }

    [Fact]
    public void Clear_EmptiesCart()
    {
        var cart = new Cart();
        cart.Add("B1", "지렁이", 5000);

        cart.Clear();

        Assert.Empty(cart.Lines);
        Assert.Equal(0, cart.Total);
    }
}
```

- [ ] **Step 2: 테스트 실패 확인**

```bash
dotnet test tests\FishingMartPos.Tests --filter FullyQualifiedName~CartTests
```

Expected: FAIL (`Cart`, `CartLine` 타입 없음)

- [ ] **Step 3: 구현**

`src/FishingMartPos/Models/CartLine.cs`:

```csharp
namespace FishingMartPos.Models;

public sealed class CartLine
{
    public required string Barcode { get; init; }
    public required string Name { get; init; }
    public required decimal Price { get; init; }
    public int Qty { get; set; } = 1;

    public decimal LineTotal => Price * Qty;
}
```

`src/FishingMartPos/Domain/Cart.cs`:

```csharp
using FishingMartPos.Models;

namespace FishingMartPos.Domain;

public sealed class Cart
{
    private readonly List<CartLine> _lines = new();

    public IReadOnlyList<CartLine> Lines => _lines;
    public decimal Total => _lines.Sum(l => l.LineTotal);
    public int TotalQty => _lines.Sum(l => l.Qty);

    public void Add(string barcode, string name, decimal price)
    {
        var existing = _lines.FirstOrDefault(l => l.Barcode == barcode);
        if (existing is not null)
        {
            existing.Qty += 1;
            return;
        }

        _lines.Add(new CartLine { Barcode = barcode, Name = name, Price = price, Qty = 1 });
    }

    public void AddExisting(string barcode, string name, decimal price, int qty)
    {
        var existing = _lines.FirstOrDefault(l => l.Barcode == barcode);
        if (existing is not null)
        {
            existing.Qty += qty;
            return;
        }

        _lines.Add(new CartLine { Barcode = barcode, Name = name, Price = price, Qty = qty });
    }

    public void SetQty(string barcode, int qty)
    {
        var line = _lines.FirstOrDefault(l => l.Barcode == barcode);
        if (line is null) return;
        line.Qty = Math.Max(1, qty);
    }

    public void Increment(string barcode) => SetQty(barcode, GetQty(barcode) + 1);

    public void Decrement(string barcode) => SetQty(barcode, GetQty(barcode) - 1);

    public void Remove(string barcode) => _lines.RemoveAll(l => l.Barcode == barcode);

    public void Clear() => _lines.Clear();

    private int GetQty(string barcode) => _lines.FirstOrDefault(l => l.Barcode == barcode)?.Qty ?? 1;
}
```

- [ ] **Step 4: 테스트 통과 확인**

```bash
dotnet test tests\FishingMartPos.Tests --filter FullyQualifiedName~CartTests
```

Expected: PASS (8 tests)

- [ ] **Step 5: Commit**

```bash
git add src/FishingMartPos/Models/CartLine.cs src/FishingMartPos/Domain/Cart.cs tests/FishingMartPos.Tests/Domain/CartTests.cs
git commit -m "Cart 도메인 모델 추가 (장바구니 담기/수량조절/삭제)"
```

---

### Task 4: Product/CodeItem 모델 + Repository (TDD, MariaDB 연동)

**Files:**
- Create: `src/FishingMartPos/Models/Product.cs`
- Create: `src/FishingMartPos/Models/CodeItem.cs`
- Create: `src/FishingMartPos/Repositories/IProductRepository.cs` / `ProductRepository.cs`
- Create: `src/FishingMartPos/Repositories/ICodeRepository.cs` / `CodeRepository.cs`
- Test: `tests/FishingMartPos.Tests/Repositories/ProductRepositoryTests.cs`, `CodeRepositoryTests.cs`

**Interfaces:**
- Consumes: `IDbConnectionFactory`(Phase 1)
- Produces: `Product { Barcode, MajorCd, MinorCd, PosCatCd, Name, Price, StockQty }`, `IProductRepository.GetActiveAsync() : Task<IReadOnlyList<Product>>`, `CodeItem { Code, Name, SortNo }`, `ICodeRepository.GetByGroupAsync(string codeGbn) : Task<IReadOnlyList<CodeItem>>`

- [ ] **Step 1: 모델 작성**

`src/FishingMartPos/Models/Product.cs`:

```csharp
namespace FishingMartPos.Models;

public sealed class Product
{
    public required string Barcode { get; init; }
    public required string MajorCd { get; init; }
    public required string MinorCd { get; init; }
    public required string PosCatCd { get; init; }
    public required string Name { get; init; }
    public required decimal Price { get; init; }
    public required int StockQty { get; init; }
}
```

`src/FishingMartPos/Models/CodeItem.cs`:

```csharp
namespace FishingMartPos.Models;

public sealed class CodeItem
{
    public required string Code { get; init; }
    public required string Name { get; init; }
    public required int SortNo { get; init; }
}
```

- [ ] **Step 2: 실패하는 통합 테스트 작성**

`tests/FishingMartPos.Tests/Repositories/ProductRepositoryTests.cs`:

```csharp
using FishingMartPos.Configuration;
using FishingMartPos.Data;
using FishingMartPos.Repositories;
using Xunit;

namespace FishingMartPos.Tests.Repositories;

public class ProductRepositoryTests
{
    [Fact]
    public async Task GetActive_ReturnsSeededFortyThreeProducts()
    {
        var config = AppConfig.Load(AppContext.BaseDirectory);
        var factory = new MySqlConnectionFactory(config);
        IProductRepository repository = new ProductRepository(factory);

        var products = await repository.GetActiveAsync();

        Assert.Contains(products, p => p.Barcode == "8800000020001" && p.Name == "지렁이" && p.PosCatCd == "BAIT");
        Assert.True(products.Count >= 43);
    }
}
```

`tests/FishingMartPos.Tests/Repositories/CodeRepositoryTests.cs`:

```csharp
using FishingMartPos.Configuration;
using FishingMartPos.Data;
using FishingMartPos.Repositories;
using Xunit;

namespace FishingMartPos.Tests.Repositories;

public class CodeRepositoryTests
{
    [Fact]
    public async Task GetByGroup_ReturnsPosCatCodesInSortOrder()
    {
        var config = AppConfig.Load(AppContext.BaseDirectory);
        var factory = new MySqlConnectionFactory(config);
        ICodeRepository repository = new CodeRepository(factory);

        var codes = await repository.GetByGroupAsync("POSCAT");

        Assert.Equal("BAIT", codes[0].Code);
        Assert.Equal("미끼", codes[0].Name);
        Assert.Equal(8, codes.Count);
    }
}
```

- [ ] **Step 3: 테스트 실패 확인**

```bash
dotnet test tests\FishingMartPos.Tests --filter "FullyQualifiedName~ProductRepositoryTests|FullyQualifiedName~CodeRepositoryTests"
```

Expected: FAIL (타입 없음)

- [ ] **Step 4: 구현**

`src/FishingMartPos/Repositories/IProductRepository.cs`:

```csharp
using FishingMartPos.Models;

namespace FishingMartPos.Repositories;

public interface IProductRepository
{
    Task<IReadOnlyList<Product>> GetActiveAsync();
}
```

`src/FishingMartPos/Repositories/ProductRepository.cs`:

```csharp
using Dapper;
using FishingMartPos.Data;
using FishingMartPos.Models;

namespace FishingMartPos.Repositories;

public sealed class ProductRepository : IProductRepository
{
    private readonly IDbConnectionFactory _connectionFactory;

    public ProductRepository(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<IReadOnlyList<Product>> GetActiveAsync()
    {
        using var connection = _connectionFactory.CreateOpenConnection();
        const string sql = """
            SELECT barcode AS Barcode, major_cd AS MajorCd, minor_cd AS MinorCd, poscat_cd AS PosCatCd,
                   name AS Name, price AS Price, stock_qty AS StockQty
            FROM product_tb
            WHERE use_yn = 'Y'
            ORDER BY poscat_cd, name
            """;

        var result = await connection.QueryAsync<Product>(sql);
        return result.ToList();
    }
}
```

`src/FishingMartPos/Repositories/ICodeRepository.cs`:

```csharp
using FishingMartPos.Models;

namespace FishingMartPos.Repositories;

public interface ICodeRepository
{
    Task<IReadOnlyList<CodeItem>> GetByGroupAsync(string codeGbn);
}
```

`src/FishingMartPos/Repositories/CodeRepository.cs`:

```csharp
using Dapper;
using FishingMartPos.Data;
using FishingMartPos.Models;

namespace FishingMartPos.Repositories;

public sealed class CodeRepository : ICodeRepository
{
    private readonly IDbConnectionFactory _connectionFactory;

    public CodeRepository(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<IReadOnlyList<CodeItem>> GetByGroupAsync(string codeGbn)
    {
        using var connection = _connectionFactory.CreateOpenConnection();
        const string sql = """
            SELECT code_cd AS Code, code_nm AS Name, sort_no AS SortNo
            FROM code_tb
            WHERE code_gbn = @CodeGbn
            ORDER BY sort_no, code_cd
            """;

        var result = await connection.QueryAsync<CodeItem>(sql, new { CodeGbn = codeGbn });
        return result.ToList();
    }
}
```

- [ ] **Step 5: 테스트 통과 확인 (Task 1 시드가 먼저 적용되어 있어야 함)**

```bash
dotnet test tests\FishingMartPos.Tests --filter "FullyQualifiedName~ProductRepositoryTests|FullyQualifiedName~CodeRepositoryTests"
```

Expected: PASS (2 tests)

- [ ] **Step 6: Commit**

```bash
git add src/FishingMartPos/Models/Product.cs src/FishingMartPos/Models/CodeItem.cs src/FishingMartPos/Repositories/IProductRepository.cs src/FishingMartPos/Repositories/ProductRepository.cs src/FishingMartPos/Repositories/ICodeRepository.cs src/FishingMartPos/Repositories/CodeRepository.cs tests/FishingMartPos.Tests/Repositories/ProductRepositoryTests.cs tests/FishingMartPos.Tests/Repositories/CodeRepositoryTests.cs
git commit -m "Product/CodeItem 모델과 Repository 추가"
```

---

### Task 5: 매출 저장 Repository (트랜잭션, TDD)

**Files:**
- Create: `src/FishingMartPos/Models/SaleHeader.cs`
- Create: `src/FishingMartPos/Models/SaleDetailLine.cs`
- Create: `src/FishingMartPos/Repositories/ISalesRepository.cs` / `SalesRepository.cs`
- Test: `tests/FishingMartPos.Tests/Repositories/SalesRepositoryTests.cs`

**Interfaces:**
- Consumes: `IDbConnectionFactory`
- Produces: `SaleHeader { PosCd, SaleDt, StaffCd, TotalAmt, PayType, CashReceived, ChangeAmt, VanApprovalNo, VanCode }`, `SaleDetailLine { Barcode, ProductName, Qty, UnitPrice }`, `ISalesRepository.CreateSaleAsync(SaleHeader header, IReadOnlyList<SaleDetailLine> lines) : Task<long>` (매출헤더 insert + 매출상세 insert + 재고 차감을 한 트랜잭션으로 처리, `sale_no` 반환)

- [ ] **Step 1: 모델 작성**

`src/FishingMartPos/Models/SaleHeader.cs`:

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
}
```

`src/FishingMartPos/Models/SaleDetailLine.cs`:

```csharp
namespace FishingMartPos.Models;

public sealed class SaleDetailLine
{
    public required string Barcode { get; init; }
    public required string ProductName { get; init; }
    public required int Qty { get; init; }
    public required decimal UnitPrice { get; init; }

    public decimal LineAmt => UnitPrice * Qty;
}
```

- [ ] **Step 2: 실패하는 통합 테스트 작성**

`tests/FishingMartPos.Tests/Repositories/SalesRepositoryTests.cs`:

```csharp
using Dapper;
using FishingMartPos.Configuration;
using FishingMartPos.Data;
using FishingMartPos.Models;
using FishingMartPos.Repositories;
using Xunit;

namespace FishingMartPos.Tests.Repositories;

public class SalesRepositoryTests
{
    [Fact]
    public async Task CreateSale_InsertsHeaderDetailAndDecrementsStock()
    {
        var config = AppConfig.Load(AppContext.BaseDirectory);
        var factory = new MySqlConnectionFactory(config);
        ISalesRepository repository = new SalesRepository(factory);

        int stockBefore;
        using (var conn = factory.CreateOpenConnection())
        {
            stockBefore = await conn.QuerySingleAsync<int>(
                "SELECT stock_qty FROM product_tb WHERE barcode = @Barcode", new { Barcode = "8800000020001" });
        }

        var header = new SaleHeader
        {
            PosCd = "1", SaleDt = DateTime.Now, StaffCd = "ADMIN1", TotalAmt = 5000,
            PayType = "CASH", CashReceived = 5000, ChangeAmt = 0, VanApprovalNo = null, VanCode = null,
        };
        var lines = new[]
        {
            new SaleDetailLine { Barcode = "8800000020001", ProductName = "지렁이", Qty = 1, UnitPrice = 5000 },
        };

        long saleNo = await repository.CreateSaleAsync(header, lines);

        Assert.True(saleNo > 0);

        using var verifyConn = factory.CreateOpenConnection();
        var savedHeader = await verifyConn.QuerySingleAsync<(decimal TotalAmt, string PayType)>(
            "SELECT total_amt AS TotalAmt, pay_type AS PayType FROM sales_header_tb WHERE sale_no = @SaleNo",
            new { SaleNo = saleNo });
        Assert.Equal(5000, savedHeader.TotalAmt);
        Assert.Equal("CASH", savedHeader.PayType);

        var detailCount = await verifyConn.QuerySingleAsync<int>(
            "SELECT COUNT(*) FROM sales_detail_tb WHERE sale_no = @SaleNo", new { SaleNo = saleNo });
        Assert.Equal(1, detailCount);

        int stockAfter = await verifyConn.QuerySingleAsync<int>(
            "SELECT stock_qty FROM product_tb WHERE barcode = @Barcode", new { Barcode = "8800000020001" });
        Assert.Equal(stockBefore - 1, stockAfter);

        // 정리: 테스트가 남긴 매출/재고 변화를 되돌린다
        await verifyConn.ExecuteAsync("DELETE FROM sales_detail_tb WHERE sale_no = @SaleNo", new { SaleNo = saleNo });
        await verifyConn.ExecuteAsync("DELETE FROM sales_header_tb WHERE sale_no = @SaleNo", new { SaleNo = saleNo });
        await verifyConn.ExecuteAsync(
            "UPDATE product_tb SET stock_qty = @Stock WHERE barcode = @Barcode",
            new { Stock = stockBefore, Barcode = "8800000020001" });
    }
}
```

- [ ] **Step 3: 테스트 실패 확인**

```bash
dotnet test tests\FishingMartPos.Tests --filter FullyQualifiedName~SalesRepositoryTests
```

Expected: FAIL (`ISalesRepository`, `SalesRepository` 타입 없음)

- [ ] **Step 4: 구현**

`src/FishingMartPos/Repositories/ISalesRepository.cs`:

```csharp
using FishingMartPos.Models;

namespace FishingMartPos.Repositories;

public interface ISalesRepository
{
    Task<long> CreateSaleAsync(SaleHeader header, IReadOnlyList<SaleDetailLine> lines);
}
```

`src/FishingMartPos/Repositories/SalesRepository.cs`:

```csharp
using Dapper;
using FishingMartPos.Data;
using FishingMartPos.Models;

namespace FishingMartPos.Repositories;

public sealed class SalesRepository : ISalesRepository
{
    private readonly IDbConnectionFactory _connectionFactory;

    public SalesRepository(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<long> CreateSaleAsync(SaleHeader header, IReadOnlyList<SaleDetailLine> lines)
    {
        using var connection = _connectionFactory.CreateOpenConnection();
        using var transaction = connection.BeginTransaction();

        const string insertHeaderSql = """
            INSERT INTO sales_header_tb
                (pos_cd, sale_dt, staff_cd, total_amt, pay_type, cash_received, change_amt, van_approval_no, van_code, status)
            VALUES
                (@PosCd, @SaleDt, @StaffCd, @TotalAmt, @PayType, @CashReceived, @ChangeAmt, @VanApprovalNo, @VanCode, 'COMPLETE')
            """;
        await connection.ExecuteAsync(insertHeaderSql, header, transaction);
        long saleNo = await connection.QuerySingleAsync<long>("SELECT LAST_INSERT_ID()", transaction: transaction);

        const string insertDetailSql = """
            INSERT INTO sales_detail_tb (sale_no, line_no, barcode, product_name, qty, unit_price, line_amt)
            VALUES (@SaleNo, @LineNo, @Barcode, @ProductName, @Qty, @UnitPrice, @LineAmt)
            """;
        const string decrementStockSql = """
            UPDATE product_tb SET stock_qty = stock_qty - @Qty WHERE barcode = @Barcode
            """;

        int lineNo = 1;
        foreach (var line in lines)
        {
            await connection.ExecuteAsync(insertDetailSql, new
            {
                SaleNo = saleNo,
                LineNo = lineNo,
                line.Barcode,
                line.ProductName,
                line.Qty,
                UnitPrice = line.UnitPrice,
                LineAmt = line.LineAmt,
            }, transaction);

            await connection.ExecuteAsync(decrementStockSql, new { line.Qty, line.Barcode }, transaction);
            lineNo++;
        }

        transaction.Commit();
        return saleNo;
    }
}
```

- [ ] **Step 5: 테스트 통과 확인**

```bash
dotnet test tests\FishingMartPos.Tests --filter FullyQualifiedName~SalesRepositoryTests
```

Expected: PASS (1 test)

- [ ] **Step 6: Commit**

```bash
git add src/FishingMartPos/Models/SaleHeader.cs src/FishingMartPos/Models/SaleDetailLine.cs src/FishingMartPos/Repositories/ISalesRepository.cs src/FishingMartPos/Repositories/SalesRepository.cs tests/FishingMartPos.Tests/Repositories/SalesRepositoryTests.cs
git commit -m "매출 저장 Repository 추가 (헤더+상세+재고차감 트랜잭션)"
```

---

### Task 6: 보류(홀드) 주문 Repository (TDD)

**Files:**
- Create: `src/FishingMartPos/Models/HeldOrderLine.cs`
- Create: `src/FishingMartPos/Repositories/IHeldOrderRepository.cs` / `HeldOrderRepository.cs`
- Test: `tests/FishingMartPos.Tests/Repositories/HeldOrderRepositoryTests.cs`

**Interfaces:**
- Produces: `HeldOrderLine { Barcode, ProductName, Qty, UnitPrice }`, `IHeldOrderRepository { Task<long> HoldAsync(string posCd, string staffCd, IReadOnlyList<HeldOrderLine> lines), Task<IReadOnlyList<(long HoldNo, DateTime HeldAt, decimal Total)>> GetHeldAsync(string posCd), Task<IReadOnlyList<HeldOrderLine>> GetLinesAsync(long holdNo), Task DeleteAsync(long holdNo) }`

- [ ] **Step 1: 모델 작성**

`src/FishingMartPos/Models/HeldOrderLine.cs`:

```csharp
namespace FishingMartPos.Models;

public sealed class HeldOrderLine
{
    public required string Barcode { get; init; }
    public required string ProductName { get; init; }
    public required int Qty { get; init; }
    public required decimal UnitPrice { get; init; }
}
```

- [ ] **Step 2: 실패하는 통합 테스트 작성**

`tests/FishingMartPos.Tests/Repositories/HeldOrderRepositoryTests.cs`:

```csharp
using FishingMartPos.Configuration;
using FishingMartPos.Data;
using FishingMartPos.Models;
using FishingMartPos.Repositories;
using Xunit;

namespace FishingMartPos.Tests.Repositories;

public class HeldOrderRepositoryTests
{
    private static IHeldOrderRepository CreateRepository(out MySqlConnectionFactory factory)
    {
        var config = AppConfig.Load(AppContext.BaseDirectory);
        factory = new MySqlConnectionFactory(config);
        return new HeldOrderRepository(factory);
    }

    [Fact]
    public async Task Hold_Then_GetHeld_Then_GetLines_Then_Delete_RoundTrips()
    {
        IHeldOrderRepository repository = CreateRepository(out _);
        var lines = new[]
        {
            new HeldOrderLine { Barcode = "8800000020001", ProductName = "지렁이", Qty = 2, UnitPrice = 5000 },
        };

        long holdNo = await repository.HoldAsync("1", "ADMIN1", lines);
        Assert.True(holdNo > 0);

        var heldList = await repository.GetHeldAsync("1");
        Assert.Contains(heldList, h => h.HoldNo == holdNo && h.Total == 10000);

        var fetchedLines = await repository.GetLinesAsync(holdNo);
        var fetchedLine = Assert.Single(fetchedLines);
        Assert.Equal("지렁이", fetchedLine.ProductName);
        Assert.Equal(2, fetchedLine.Qty);

        await repository.DeleteAsync(holdNo);

        var afterDelete = await repository.GetHeldAsync("1");
        Assert.DoesNotContain(afterDelete, h => h.HoldNo == holdNo);
    }
}
```

- [ ] **Step 3: 테스트 실패 확인**

```bash
dotnet test tests\FishingMartPos.Tests --filter FullyQualifiedName~HeldOrderRepositoryTests
```

Expected: FAIL (타입 없음)

- [ ] **Step 4: 구현**

`src/FishingMartPos/Repositories/IHeldOrderRepository.cs`:

```csharp
using FishingMartPos.Models;

namespace FishingMartPos.Repositories;

public interface IHeldOrderRepository
{
    Task<long> HoldAsync(string posCd, string staffCd, IReadOnlyList<HeldOrderLine> lines);
    Task<IReadOnlyList<(long HoldNo, DateTime HeldAt, decimal Total)>> GetHeldAsync(string posCd);
    Task<IReadOnlyList<HeldOrderLine>> GetLinesAsync(long holdNo);
    Task DeleteAsync(long holdNo);
}
```

`src/FishingMartPos/Repositories/HeldOrderRepository.cs`:

```csharp
using Dapper;
using FishingMartPos.Data;
using FishingMartPos.Models;

namespace FishingMartPos.Repositories;

public sealed class HeldOrderRepository : IHeldOrderRepository
{
    private readonly IDbConnectionFactory _connectionFactory;

    public HeldOrderRepository(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<long> HoldAsync(string posCd, string staffCd, IReadOnlyList<HeldOrderLine> lines)
    {
        using var connection = _connectionFactory.CreateOpenConnection();
        using var transaction = connection.BeginTransaction();

        const string insertHeaderSql = """
            INSERT INTO held_order_tb (pos_cd, staff_cd, held_at, status)
            VALUES (@PosCd, @StaffCd, NOW(), 'HELD')
            """;
        await connection.ExecuteAsync(insertHeaderSql, new { PosCd = posCd, StaffCd = staffCd }, transaction);
        long holdNo = await connection.QuerySingleAsync<long>("SELECT LAST_INSERT_ID()", transaction: transaction);

        const string insertLineSql = """
            INSERT INTO held_order_detail_tb (hold_no, line_no, barcode, product_name, qty, unit_price)
            VALUES (@HoldNo, @LineNo, @Barcode, @ProductName, @Qty, @UnitPrice)
            """;

        int lineNo = 1;
        foreach (var line in lines)
        {
            await connection.ExecuteAsync(insertLineSql, new
            {
                HoldNo = holdNo,
                LineNo = lineNo,
                line.Barcode,
                line.ProductName,
                line.Qty,
                line.UnitPrice,
            }, transaction);
            lineNo++;
        }

        transaction.Commit();
        return holdNo;
    }

    public async Task<IReadOnlyList<(long HoldNo, DateTime HeldAt, decimal Total)>> GetHeldAsync(string posCd)
    {
        using var connection = _connectionFactory.CreateOpenConnection();
        const string sql = """
            SELECT h.hold_no AS HoldNo, h.held_at AS HeldAt,
                   COALESCE(SUM(d.qty * d.unit_price), 0) AS Total
            FROM held_order_tb h
            LEFT JOIN held_order_detail_tb d ON d.hold_no = h.hold_no
            WHERE h.pos_cd = @PosCd AND h.status = 'HELD'
            GROUP BY h.hold_no, h.held_at
            ORDER BY h.held_at
            """;

        var rows = await connection.QueryAsync<(long HoldNo, DateTime HeldAt, decimal Total)>(sql, new { PosCd = posCd });
        return rows.ToList();
    }

    public async Task<IReadOnlyList<HeldOrderLine>> GetLinesAsync(long holdNo)
    {
        using var connection = _connectionFactory.CreateOpenConnection();
        const string sql = """
            SELECT barcode AS Barcode, product_name AS ProductName, qty AS Qty, unit_price AS UnitPrice
            FROM held_order_detail_tb
            WHERE hold_no = @HoldNo
            ORDER BY line_no
            """;

        var result = await connection.QueryAsync<HeldOrderLine>(sql, new { HoldNo = holdNo });
        return result.ToList();
    }

    public async Task DeleteAsync(long holdNo)
    {
        using var connection = _connectionFactory.CreateOpenConnection();
        using var transaction = connection.BeginTransaction();
        await connection.ExecuteAsync("DELETE FROM held_order_detail_tb WHERE hold_no = @HoldNo", new { HoldNo = holdNo }, transaction);
        await connection.ExecuteAsync("DELETE FROM held_order_tb WHERE hold_no = @HoldNo", new { HoldNo = holdNo }, transaction);
        transaction.Commit();
    }
}
```

- [ ] **Step 5: 테스트 통과 확인**

```bash
dotnet test tests\FishingMartPos.Tests --filter FullyQualifiedName~HeldOrderRepositoryTests
```

Expected: PASS (1 test)

- [ ] **Step 6: Commit**

```bash
git add src/FishingMartPos/Models/HeldOrderLine.cs src/FishingMartPos/Repositories/IHeldOrderRepository.cs src/FishingMartPos/Repositories/HeldOrderRepository.cs tests/FishingMartPos.Tests/Repositories/HeldOrderRepositoryTests.cs
git commit -m "보류(홀드) 주문 Repository 추가"
```

---

### Task 7: DelayProvider (결제/보류 완료 토스트 지연을 테스트 가능하게)

**Files:**
- Create: `src/FishingMartPos/Services/IDelayProvider.cs` / `DelayProvider.cs`
- Test: 별도 테스트 없음 (Task 9에서 Fake로 대체해 간접 검증)

**Interfaces:**
- Produces: `IDelayProvider.Delay(TimeSpan span) : Task`

- [ ] **Step 1: 구현** (단순 래퍼라 TDD 없이 바로 작성 — 별도 로직이 없어 실패시킬 동작이 없음)

`src/FishingMartPos/Services/IDelayProvider.cs`:

```csharp
namespace FishingMartPos.Services;

public interface IDelayProvider
{
    Task Delay(TimeSpan span);
}
```

`src/FishingMartPos/Services/DelayProvider.cs`:

```csharp
namespace FishingMartPos.Services;

public sealed class DelayProvider : IDelayProvider
{
    public Task Delay(TimeSpan span) => Task.Delay(span);
}
```

- [ ] **Step 2: 빌드 확인**

```bash
dotnet build FishingMartPos.sln
```

Expected: `Build succeeded.`

- [ ] **Step 3: Commit**

```bash
git add src/FishingMartPos/Services/IDelayProvider.cs src/FishingMartPos/Services/DelayProvider.cs
git commit -m "결제/보류 토스트 지연을 위한 IDelayProvider 추가"
```

---

### Task 8: PosViewModel — 상품 그리드 & 장바구니 (TDD)

**Files:**
- Create: `src/FishingMartPos/ViewModels/CartLineViewModel.cs`
- Create: `src/FishingMartPos/ViewModels/CategoryTabViewModel.cs`
- Create: `src/FishingMartPos/ViewModels/ProductTileViewModel.cs`
- Create: `src/FishingMartPos/ViewModels/PosViewModel.cs`
- Create: `tests/FishingMartPos.Tests/Fakes/FakeProductRepository.cs`, `FakeCodeRepository.cs`, `FakeSalesRepository.cs`, `FakeHeldOrderRepository.cs`, `FakeDelayProvider.cs`
- Test: `tests/FishingMartPos.Tests/ViewModels/PosViewModelTests.cs`

**Interfaces:**
- Consumes: `IProductRepository`, `ICodeRepository`, `ISalesRepository`, `IHeldOrderRepository`, `IDelayProvider`, `ICurrentSession`
- Produces: `PosViewModel { ObservableCollection<CategoryTabViewModel> Categories, ObservableCollection<ProductTileViewModel> VisibleProducts, ObservableCollection<CartLineViewModel> CartLines, string TotalAmountStr, string TotalQtyStr, Task LoadAsync() }`

- [ ] **Step 1: Fake Repository들 작성**

`tests/FishingMartPos.Tests/Fakes/FakeProductRepository.cs`:

```csharp
using FishingMartPos.Models;
using FishingMartPos.Repositories;

namespace FishingMartPos.Tests.Fakes;

public sealed class FakeProductRepository : IProductRepository
{
    private readonly IReadOnlyList<Product> _products;

    public FakeProductRepository(IReadOnlyList<Product> products)
    {
        _products = products;
    }

    public Task<IReadOnlyList<Product>> GetActiveAsync() => Task.FromResult(_products);
}
```

`tests/FishingMartPos.Tests/Fakes/FakeCodeRepository.cs`:

```csharp
using FishingMartPos.Models;
using FishingMartPos.Repositories;

namespace FishingMartPos.Tests.Fakes;

public sealed class FakeCodeRepository : ICodeRepository
{
    private readonly Dictionary<string, IReadOnlyList<CodeItem>> _byGroup;

    public FakeCodeRepository(Dictionary<string, IReadOnlyList<CodeItem>> byGroup)
    {
        _byGroup = byGroup;
    }

    public Task<IReadOnlyList<CodeItem>> GetByGroupAsync(string codeGbn) =>
        Task.FromResult(_byGroup.TryGetValue(codeGbn, out var list) ? list : Array.Empty<CodeItem>());
}
```

`tests/FishingMartPos.Tests/Fakes/FakeSalesRepository.cs`:

```csharp
using FishingMartPos.Models;
using FishingMartPos.Repositories;

namespace FishingMartPos.Tests.Fakes;

public sealed class FakeSalesRepository : ISalesRepository
{
    public List<(SaleHeader Header, IReadOnlyList<SaleDetailLine> Lines)> CreatedSales { get; } = new();
    private long _nextSaleNo = 1;

    public Task<long> CreateSaleAsync(SaleHeader header, IReadOnlyList<SaleDetailLine> lines)
    {
        CreatedSales.Add((header, lines));
        return Task.FromResult(_nextSaleNo++);
    }
}
```

`tests/FishingMartPos.Tests/Fakes/FakeHeldOrderRepository.cs`:

```csharp
using FishingMartPos.Models;
using FishingMartPos.Repositories;

namespace FishingMartPos.Tests.Fakes;

public sealed class FakeHeldOrderRepository : IHeldOrderRepository
{
    private readonly Dictionary<long, (string PosCd, List<HeldOrderLine> Lines, DateTime HeldAt)> _held = new();
    private long _nextHoldNo = 1;

    public Task<long> HoldAsync(string posCd, string staffCd, IReadOnlyList<HeldOrderLine> lines)
    {
        long holdNo = _nextHoldNo++;
        _held[holdNo] = (posCd, lines.ToList(), DateTime.Now);
        return Task.FromResult(holdNo);
    }

    public Task<IReadOnlyList<(long HoldNo, DateTime HeldAt, decimal Total)>> GetHeldAsync(string posCd)
    {
        var result = _held
            .Where(kv => kv.Value.PosCd == posCd)
            .Select(kv => (kv.Key, kv.Value.HeldAt, kv.Value.Lines.Sum(l => l.Qty * l.UnitPrice)))
            .ToList();
        return Task.FromResult<IReadOnlyList<(long, DateTime, decimal)>>(result);
    }

    public Task<IReadOnlyList<HeldOrderLine>> GetLinesAsync(long holdNo) =>
        Task.FromResult<IReadOnlyList<HeldOrderLine>>(_held[holdNo].Lines);

    public Task DeleteAsync(long holdNo)
    {
        _held.Remove(holdNo);
        return Task.CompletedTask;
    }
}
```

`tests/FishingMartPos.Tests/Fakes/FakeDelayProvider.cs`:

```csharp
using FishingMartPos.Services;

namespace FishingMartPos.Tests.Fakes;

public sealed class FakeDelayProvider : IDelayProvider
{
    public Task Delay(TimeSpan span) => Task.CompletedTask;
}
```

- [ ] **Step 2: 실패하는 테스트 작성**

`tests/FishingMartPos.Tests/ViewModels/PosViewModelTests.cs`:

```csharp
using FishingMartPos.Models;
using FishingMartPos.Services;
using FishingMartPos.Tests.Fakes;
using FishingMartPos.ViewModels;
using Xunit;

namespace FishingMartPos.Tests.ViewModels;

public class PosViewModelTests
{
    private static readonly Product Bait1 = new()
    {
        Barcode = "B1", MajorCd = "FISH", MinorCd = "BAIT", PosCatCd = "BAIT", Name = "지렁이", Price = 5000, StockQty = 50
    };
    private static readonly Product Float1 = new()
    {
        Barcode = "F1", MajorCd = "FISH", MinorCd = "TACKLE", PosCatCd = "FLOAT", Name = "막대찌 세트", Price = 8000, StockQty = 50
    };

    private static PosViewModel CreateViewModel(out FakeSalesRepository sales, out FakeHeldOrderRepository held)
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

        return new PosViewModel(
            new FakeProductRepository(new[] { Bait1, Float1 }),
            new FakeCodeRepository(codes),
            sales,
            held,
            new FakeDelayProvider(),
            session);
    }

    [Fact]
    public async Task LoadAsync_PopulatesCategoriesAndFirstCategoryProducts()
    {
        var vm = CreateViewModel(out _, out _);

        await vm.LoadAsync();

        Assert.Equal(2, vm.Categories.Count);
        Assert.Single(vm.VisibleProducts);
        Assert.Equal("지렁이", vm.VisibleProducts[0].Name);
    }

    [Fact]
    public async Task SelectingCategory_FiltersVisibleProducts()
    {
        var vm = CreateViewModel(out _, out _);
        await vm.LoadAsync();

        vm.Categories[1].SelectCommand.Execute(null);

        Assert.Single(vm.VisibleProducts);
        Assert.Equal("막대찌 세트", vm.VisibleProducts[0].Name);
    }

    [Fact]
    public async Task AddingProductToCart_CreatesCartLineAndUpdatesTotal()
    {
        var vm = CreateViewModel(out _, out _);
        await vm.LoadAsync();

        vm.VisibleProducts[0].AddCommand.Execute(null);

        var line = Assert.Single(vm.CartLines);
        Assert.Equal("지렁이", line.Name);
        Assert.Equal(1, line.Qty);
        Assert.Equal("5,000원", vm.TotalAmountStr);
    }

    [Fact]
    public async Task AddingSameProductTwice_IncrementsQty()
    {
        var vm = CreateViewModel(out _, out _);
        await vm.LoadAsync();

        vm.VisibleProducts[0].AddCommand.Execute(null);
        vm.VisibleProducts[0].AddCommand.Execute(null);

        var line = Assert.Single(vm.CartLines);
        Assert.Equal(2, line.Qty);
    }

    [Fact]
    public async Task IncSelectedAndDecSelected_AdjustQty()
    {
        var vm = CreateViewModel(out _, out _);
        await vm.LoadAsync();
        vm.VisibleProducts[0].AddCommand.Execute(null);

        vm.IncSelectedCommand.Execute(null);
        vm.IncSelectedCommand.Execute(null);
        vm.DecSelectedCommand.Execute(null);

        Assert.Equal(2, vm.CartLines[0].Qty);
    }

    [Fact]
    public async Task RemoveSelected_DeletesLine()
    {
        var vm = CreateViewModel(out _, out _);
        await vm.LoadAsync();
        vm.VisibleProducts[0].AddCommand.Execute(null);

        vm.RemoveSelectedCommand.Execute(null);

        Assert.Empty(vm.CartLines);
    }

    [Fact]
    public async Task PressKey_WhenLineSelected_SetsExactQty()
    {
        var vm = CreateViewModel(out _, out _);
        await vm.LoadAsync();
        vm.VisibleProducts[0].AddCommand.Execute(null); // 선택 상태가 됨

        vm.PressKeyCommand.Execute("5");

        Assert.Equal(5, vm.CartLines[0].Qty);
    }

    [Fact]
    public async Task PressKey_WhenNoLineSelected_BuildsCashInput()
    {
        var vm = CreateViewModel(out _, out _);
        await vm.LoadAsync();

        vm.PressKeyCommand.Execute("1");
        vm.PressKeyCommand.Execute("0");
        vm.PressKeyCommand.Execute("0");
        vm.PressKeyCommand.Execute("0");
        vm.PressKeyCommand.Execute("0");

        Assert.Equal("10,000원", vm.CashInputStr);
    }

    [Fact]
    public async Task ResetOrder_ClearsCartAndCashInput()
    {
        var vm = CreateViewModel(out _, out _);
        await vm.LoadAsync();
        vm.VisibleProducts[0].AddCommand.Execute(null);

        vm.ResetOrderCommand.Execute(null);

        Assert.Empty(vm.CartLines);
        Assert.Equal("0원", vm.TotalAmountStr);
    }
}
```

- [ ] **Step 3: 테스트 실패 확인**

```bash
dotnet test tests\FishingMartPos.Tests --filter FullyQualifiedName~PosViewModelTests
```

Expected: FAIL (`PosViewModel` 등 타입 없음)

- [ ] **Step 4: 구현**

`src/FishingMartPos/ViewModels/CartLineViewModel.cs`:

```csharp
namespace FishingMartPos.ViewModels;

public sealed class CartLineViewModel
{
    public required string Barcode { get; init; }
    public required string Name { get; init; }
    public required int Qty { get; init; }
    public required string PriceStr { get; init; }
    public required string LineTotalStr { get; init; }
    public required bool IsSelected { get; init; }
    public required IRelayCommandLike SelectCommand { get; init; }
}
```

> `IRelayCommandLike`는 `System.Windows.Input.ICommand`를 그대로 쓰면 되므로, 실제로는 아래처럼 `ICommand`를 직접 사용한다. 위 초안을 아래 최종본으로 교체한다:

`src/FishingMartPos/ViewModels/CartLineViewModel.cs` (최종):

```csharp
using System.Windows.Input;

namespace FishingMartPos.ViewModels;

public sealed class CartLineViewModel
{
    public required string Barcode { get; init; }
    public required string Name { get; init; }
    public required int Qty { get; init; }
    public required string PriceStr { get; init; }
    public required string LineTotalStr { get; init; }
    public required bool IsSelected { get; init; }
    public required ICommand SelectCommand { get; init; }
}
```

`src/FishingMartPos/ViewModels/CategoryTabViewModel.cs`:

```csharp
using System.Windows.Input;

namespace FishingMartPos.ViewModels;

public sealed class CategoryTabViewModel
{
    public required string Code { get; init; }
    public required string Name { get; init; }
    public required bool IsActive { get; init; }
    public required ICommand SelectCommand { get; init; }
}
```

`src/FishingMartPos/ViewModels/ProductTileViewModel.cs`:

```csharp
using System.Windows.Input;
using System.Windows.Media;

namespace FishingMartPos.ViewModels;

public sealed class ProductTileViewModel
{
    public required string Barcode { get; init; }
    public required string Name { get; init; }
    public required string PriceStr { get; init; }
    public required string Initial { get; init; }
    public required Brush Swatch { get; init; }
    public required ICommand AddCommand { get; init; }
}
```

`src/FishingMartPos/ViewModels/PosViewModel.cs`:

```csharp
using System.Collections.ObjectModel;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FishingMartPos.Domain;
using FishingMartPos.Models;
using FishingMartPos.Repositories;
using FishingMartPos.Services;
using FishingMartPos.Theme;

namespace FishingMartPos.ViewModels;

public sealed partial class PosViewModel : ObservableObject
{
    private static readonly Brush[] Swatches = BuildSwatches();
    private static readonly string[] SwatchKeys =
    {
        "ProductSwatch0", "ProductSwatch1", "ProductSwatch2", "ProductSwatch3", "ProductSwatch4", "ProductSwatch5",
    };

    private readonly IProductRepository _productRepository;
    private readonly ICodeRepository _codeRepository;
    private readonly ISalesRepository _salesRepository;
    private readonly IHeldOrderRepository _heldOrderRepository;
    private readonly IDelayProvider _delay;
    private readonly ICurrentSession _session;
    private readonly Cart _cart = new();

    private IReadOnlyList<Product> _allProducts = Array.Empty<Product>();
    private string _activeCategoryCode = string.Empty;

    [ObservableProperty]
    private string? _selectedBarcode;

    [ObservableProperty]
    private string _qtyBuffer = string.Empty;

    [ObservableProperty]
    private string _cashInput = string.Empty;

    [ObservableProperty]
    private string? _toastMessage;

    [ObservableProperty]
    private bool _isHeldListVisible;

    public ObservableCollection<CategoryTabViewModel> Categories { get; } = new();
    public ObservableCollection<ProductTileViewModel> VisibleProducts { get; } = new();
    public ObservableCollection<CartLineViewModel> CartLines { get; } = new();
    public ObservableCollection<HeldOrderSummaryViewModel> HeldOrders { get; } = new();

    public PosViewModel(
        IProductRepository productRepository,
        ICodeRepository codeRepository,
        ISalesRepository salesRepository,
        IHeldOrderRepository heldOrderRepository,
        IDelayProvider delay,
        ICurrentSession session)
    {
        _productRepository = productRepository;
        _codeRepository = codeRepository;
        _salesRepository = salesRepository;
        _heldOrderRepository = heldOrderRepository;
        _delay = delay;
        _session = session;
    }

    public string TotalAmountStr => Format(_cart.Total);
    public string TotalQtyStr => _cart.TotalQty.ToString("N0");
    public string CashInputStr => CashInput.Length == 0 ? "0원" : Format(decimal.Parse(CashInput));
    public string ChangeStr => Format(Math.Max(0, CashAmount - _cart.Total));
    private decimal CashAmount => CashInput.Length == 0 ? 0 : decimal.Parse(CashInput);

    public async Task LoadAsync()
    {
        var codes = await _codeRepository.GetByGroupAsync("POSCAT");
        _allProducts = await _productRepository.GetActiveAsync();

        Categories.Clear();
        foreach (var code in codes)
        {
            Categories.Add(new CategoryTabViewModel
            {
                Code = code.Code,
                Name = code.Name,
                IsActive = Categories.Count == 0,
                SelectCommand = new RelayCommand(() => SelectCategory(code.Code)),
            });
        }

        _activeCategoryCode = codes.Count > 0 ? codes[0].Code : string.Empty;
        RefreshVisibleProducts();
        await RefreshHeldOrdersAsync();
    }

    private void SelectCategory(string code)
    {
        _activeCategoryCode = code;
        for (int i = 0; i < Categories.Count; i++)
        {
            var tab = Categories[i];
            Categories[i] = new CategoryTabViewModel
            {
                Code = tab.Code,
                Name = tab.Name,
                IsActive = tab.Code == code,
                SelectCommand = tab.SelectCommand,
            };
        }
        RefreshVisibleProducts();
    }

    private void RefreshVisibleProducts()
    {
        VisibleProducts.Clear();
        int swatchIndex = 0;
        foreach (var product in _allProducts.Where(p => p.PosCatCd == _activeCategoryCode))
        {
            var captured = product;
            VisibleProducts.Add(new ProductTileViewModel
            {
                Barcode = captured.Barcode,
                Name = captured.Name,
                PriceStr = Format(captured.Price),
                Initial = captured.Name.Length > 0 ? captured.Name[..1] : "?",
                Swatch = Swatches[swatchIndex % Swatches.Length],
                AddCommand = new RelayCommand(() => AddToCart(captured)),
            });
            swatchIndex++;
        }
    }

    private void AddToCart(Product product)
    {
        _cart.Add(product.Barcode, product.Name, product.Price);
        SelectedBarcode = product.Barcode;
        QtyBuffer = string.Empty;
        RefreshCartLines();
    }

    [RelayCommand]
    private void SelectCartLine(string barcode)
    {
        SelectedBarcode = barcode;
        QtyBuffer = string.Empty;
        RefreshCartLines();
    }

    [RelayCommand]
    private void IncSelected()
    {
        if (SelectedBarcode is null) return;
        _cart.Increment(SelectedBarcode);
        RefreshCartLines();
    }

    [RelayCommand]
    private void DecSelected()
    {
        if (SelectedBarcode is null) return;
        _cart.Decrement(SelectedBarcode);
        RefreshCartLines();
    }

    [RelayCommand]
    private void RemoveSelected()
    {
        if (SelectedBarcode is null) return;
        _cart.Remove(SelectedBarcode);
        SelectedBarcode = null;
        RefreshCartLines();
    }

    [RelayCommand]
    private void PressKey(string key)
    {
        if (SelectedBarcode is not null)
        {
            if (key == "CLS")
            {
                QtyBuffer = string.Empty;
                _cart.SetQty(SelectedBarcode, 1);
            }
            else if (key == "<")
            {
                QtyBuffer = QtyBuffer.Length > 0 ? QtyBuffer[..^1] : string.Empty;
                int qty = QtyBuffer.Length > 0 ? int.Parse(QtyBuffer) : 1;
                _cart.SetQty(SelectedBarcode, qty);
            }
            else if (QtyBuffer.Length < 3)
            {
                QtyBuffer += key;
                _cart.SetQty(SelectedBarcode, Math.Max(1, int.Parse(QtyBuffer)));
            }
            RefreshCartLines();
            return;
        }

        if (key == "CLS")
        {
            CashInput = string.Empty;
        }
        else if (key == "<")
        {
            CashInput = CashInput.Length > 0 ? CashInput[..^1] : string.Empty;
        }
        else if (CashInput.Length < 9)
        {
            CashInput += key;
        }
        OnPropertyChanged(nameof(CashInputStr));
        OnPropertyChanged(nameof(ChangeStr));
    }

    [RelayCommand]
    private void ResetOrder()
    {
        _cart.Clear();
        SelectedBarcode = null;
        QtyBuffer = string.Empty;
        CashInput = string.Empty;
        RefreshCartLines();
    }

    [RelayCommand]
    private async Task PayCash() => await PayAsync("CASH", "현금 결제 완료");

    [RelayCommand]
    private async Task PayCard1() => await PayAsync("CARD1", "카드 결제 완료");

    [RelayCommand]
    private async Task PayCard2() => await PayAsync("CARD2", "카드 결제 완료");

    private async Task PayAsync(string payType, string toastLabel)
    {
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
        var lines = _cart.Lines
            .Select(l => new SaleDetailLine { Barcode = l.Barcode, ProductName = l.Name, Qty = l.Qty, UnitPrice = l.Price })
            .ToList();

        await _salesRepository.CreateSaleAsync(header, lines);

        ToastMessage = toastLabel;
        await _delay.Delay(TimeSpan.FromMilliseconds(1200));
        ToastMessage = null;
        ResetOrder();
    }

    [RelayCommand]
    private async Task HoldOrder()
    {
        if (_cart.Lines.Count == 0) return;

        var lines = _cart.Lines
            .Select(l => new HeldOrderLine { Barcode = l.Barcode, ProductName = l.Name, Qty = l.Qty, UnitPrice = l.Price })
            .ToList();
        await _heldOrderRepository.HoldAsync(_session.CurrentTerminal!.PosCode, _session.CurrentStaff!.StaffCode, lines);

        ResetOrder();
        await RefreshHeldOrdersAsync();
    }

    [RelayCommand]
    private void ShowHeldList() => IsHeldListVisible = true;

    [RelayCommand]
    private void HideHeldList() => IsHeldListVisible = false;

    private async Task RecallOrder(long holdNo)
    {
        var lines = await _heldOrderRepository.GetLinesAsync(holdNo);
        foreach (var line in lines)
        {
            _cart.AddExisting(line.Barcode, line.ProductName, line.UnitPrice, line.Qty);
        }
        await _heldOrderRepository.DeleteAsync(holdNo);
        RefreshCartLines();
        await RefreshHeldOrdersAsync();
        IsHeldListVisible = false;
    }

    private async Task RefreshHeldOrdersAsync()
    {
        var held = await _heldOrderRepository.GetHeldAsync(_session.CurrentTerminal?.PosCode ?? string.Empty);
        HeldOrders.Clear();
        foreach (var item in held)
        {
            long capturedHoldNo = item.HoldNo;
            HeldOrders.Add(new HeldOrderSummaryViewModel
            {
                HoldNo = item.HoldNo,
                HeldAtStr = item.HeldAt.ToString("HH:mm:ss"),
                TotalStr = Format(item.Total),
                RecallCommand = new AsyncRelayCommand(() => RecallOrder(capturedHoldNo)),
            });
        }
    }

    private void RefreshCartLines()
    {
        CartLines.Clear();
        foreach (var line in _cart.Lines)
        {
            string barcode = line.Barcode;
            CartLines.Add(new CartLineViewModel
            {
                Barcode = barcode,
                Name = line.Name,
                Qty = line.Qty,
                PriceStr = Format(line.Price),
                LineTotalStr = Format(line.LineTotal),
                IsSelected = barcode == SelectedBarcode,
                SelectCommand = SelectCartLineCommand,
            });
        }
        OnPropertyChanged(nameof(TotalAmountStr));
        OnPropertyChanged(nameof(TotalQtyStr));
    }

    private static string Format(decimal amount) => amount.ToString("N0") + "원";

    private static Brush[] BuildSwatches()
    {
        var all = AppColors.BuildBrushes();
        return SwatchKeys.Select(key => all[key]).ToArray();
    }
}
```

> `SelectCartLineCommand`는 `[RelayCommand] private void SelectCartLine(string barcode)`로부터 CommunityToolkit이 자동 생성한다(파라미터 있는 커맨드이므로 `IRelayCommand`이며 `Execute(barcode)` 형태로 호출됨 — `CartLineViewModel.SelectCommand`에 바인딩하면 XAML에서 `CommandParameter`로 barcode를 넘긴다).

- [ ] **Step 5: 테스트 통과 확인**

```bash
dotnet test tests\FishingMartPos.Tests --filter FullyQualifiedName~PosViewModelTests
```

Expected: PASS (9 tests)

- [ ] **Step 6: Commit**

```bash
git add src/FishingMartPos/ViewModels/CartLineViewModel.cs src/FishingMartPos/ViewModels/CategoryTabViewModel.cs src/FishingMartPos/ViewModels/ProductTileViewModel.cs src/FishingMartPos/ViewModels/PosViewModel.cs tests/FishingMartPos.Tests/Fakes/FakeProductRepository.cs tests/FishingMartPos.Tests/Fakes/FakeCodeRepository.cs tests/FishingMartPos.Tests/Fakes/FakeSalesRepository.cs tests/FishingMartPos.Tests/Fakes/FakeHeldOrderRepository.cs tests/FishingMartPos.Tests/Fakes/FakeDelayProvider.cs tests/FishingMartPos.Tests/ViewModels/PosViewModelTests.cs
git commit -m "PosViewModel 추가: 카테고리/상품그리드/장바구니/키패드 핵심 로직"
```

---

### Task 9: PosViewModel — 결제 & 보류/리콜 (TDD)

**Files:**
- Create: `src/FishingMartPos/ViewModels/HeldOrderSummaryViewModel.cs`
- Test: `tests/FishingMartPos.Tests/ViewModels/PosViewModelPaymentTests.cs`

**Interfaces:**
- Consumes: Task 8의 `PosViewModel`
- Produces: `HeldOrderSummaryViewModel { HoldNo, HeldAtStr, TotalStr, RecallCommand }`

- [ ] **Step 1: 모델 작성**

`src/FishingMartPos/ViewModels/HeldOrderSummaryViewModel.cs`:

```csharp
using System.Windows.Input;

namespace FishingMartPos.ViewModels;

public sealed class HeldOrderSummaryViewModel
{
    public required long HoldNo { get; init; }
    public required string HeldAtStr { get; init; }
    public required string TotalStr { get; init; }
    public required ICommand RecallCommand { get; init; }
}
```

- [ ] **Step 2: 실패하는 테스트 작성**

`tests/FishingMartPos.Tests/ViewModels/PosViewModelPaymentTests.cs`:

```csharp
using FishingMartPos.Models;
using FishingMartPos.Services;
using FishingMartPos.Tests.Fakes;
using FishingMartPos.ViewModels;
using Xunit;

namespace FishingMartPos.Tests.ViewModels;

public class PosViewModelPaymentTests
{
    private static readonly Product Bait1 = new()
    {
        Barcode = "B1", MajorCd = "FISH", MinorCd = "BAIT", PosCatCd = "BAIT", Name = "지렁이", Price = 5000, StockQty = 50
    };

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

        return new PosViewModel(
            new FakeProductRepository(new[] { Bait1 }), new FakeCodeRepository(codes),
            sales, held, new FakeDelayProvider(), session);
    }

    [Fact]
    public async Task PayCash_WithEmptyCart_DoesNothing()
    {
        var vm = CreateViewModel(out var sales, out _);
        await vm.LoadAsync();

        await vm.PayCashCommand.ExecuteAsync(null);

        Assert.Empty(sales.CreatedSales);
    }

    [Fact]
    public async Task PayCash_WithItemsInCart_CreatesSaleAndClearsCart()
    {
        var vm = CreateViewModel(out var sales, out _);
        await vm.LoadAsync();
        vm.VisibleProducts[0].AddCommand.Execute(null);

        await vm.PayCashCommand.ExecuteAsync(null);

        var sale = Assert.Single(sales.CreatedSales);
        Assert.Equal("CASH", sale.Header.PayType);
        Assert.Equal(5000, sale.Header.TotalAmt);
        Assert.Empty(vm.CartLines);
    }

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

    [Fact]
    public async Task HoldOrder_WithEmptyCart_DoesNothing()
    {
        var vm = CreateViewModel(out _, out var held);
        await vm.LoadAsync();

        await vm.HoldOrderCommand.ExecuteAsync(null);

        Assert.Empty(vm.HeldOrders);
    }

    [Fact]
    public async Task HoldOrder_ThenRecall_RestoresCartAndRemovesFromHeldList()
    {
        var vm = CreateViewModel(out _, out var held);
        await vm.LoadAsync();
        vm.VisibleProducts[0].AddCommand.Execute(null);
        vm.IncSelectedCommand.Execute(null); // qty=2

        await vm.HoldOrderCommand.ExecuteAsync(null);

        Assert.Empty(vm.CartLines);
        var heldItem = Assert.Single(vm.HeldOrders);

        await ((CommunityToolkit.Mvvm.Input.IAsyncRelayCommand)heldItem.RecallCommand).ExecuteAsync(null);

        var restored = Assert.Single(vm.CartLines);
        Assert.Equal(2, restored.Qty);
        Assert.Empty(vm.HeldOrders);
    }
}
```

- [ ] **Step 3: 테스트 실패 확인**

```bash
dotnet test tests\FishingMartPos.Tests --filter FullyQualifiedName~PosViewModelPaymentTests
```

Expected: FAIL (`HeldOrderSummaryViewModel` 타입 없음 — Task 8의 `PosViewModel`이 이미 이를 참조하므로 컴파일 에러였을 것. Task 8 Step 6 커밋 이전에 이 파일이 필요하다는 뜻이므로, 실제로는 Task 8 Step 4에서 `HeldOrderSummaryViewModel.cs`가 없어 컴파일이 안 됐을 것이다. Task 8을 진행할 때 이 파일을 함께 만들어야 한다 — 아래 Step 4에서 생성)

- [ ] **Step 4: 구현 확인** (Task 8에서 이미 `PosViewModel.cs`가 `HeldOrderSummaryViewModel`을 참조하고 있었으므로, Step 1의 모델 파일 생성이 곧 구현이다 — 추가 구현 불필요)

- [ ] **Step 5: 테스트 통과 확인**

```bash
dotnet test tests\FishingMartPos.Tests --filter FullyQualifiedName~PosViewModelPaymentTests
```

Expected: PASS (6 tests)

- [ ] **Step 6: Commit**

```bash
git add src/FishingMartPos/ViewModels/HeldOrderSummaryViewModel.cs tests/FishingMartPos.Tests/ViewModels/PosViewModelPaymentTests.cs
git commit -m "PosViewModel 결제(현금/카드1/카드2) 및 보류/리콜 로직 추가"
```

---

### Task 10: PosView.xaml — 디자인 픽셀 이식

**Files:**
- Create: `src/FishingMartPos/Views/PosView.xaml` / `PosView.xaml.cs`
- Test: `tests/FishingMartPos.Tests/Views/PosViewSmokeTests.cs`

**Interfaces:**
- Consumes: `PosViewModel`(Task 8·9)

`Fishing Mart POS.html` 줄 346~441(판매 화면 섹션)을 그대로 이식한다: 좌측 카테고리+상품그리드, 좌하단 장바구니, 하단 키패드/결제바. 설계문서 2-1절에 따라 장바구니 헤더 위에 보류 뱃지/버튼 한 줄을 최소 침습적으로 추가한다.

- [ ] **Step 1: 실패하는 스모크 테스트 작성**

`tests/FishingMartPos.Tests/Views/PosViewSmokeTests.cs`:

```csharp
using FishingMartPos.Views;
using Xunit;

namespace FishingMartPos.Tests.Views;

public class PosViewSmokeTests
{
    [Fact]
    public void PosView_ConstructsWithoutException()
    {
        StaTestHelper.RunOnSta(() =>
        {
            var view = new PosView();
            Assert.NotNull(view);
        });
    }
}
```

- [ ] **Step 2: 테스트 실패 확인**

```bash
dotnet test tests\FishingMartPos.Tests --filter FullyQualifiedName~PosViewSmokeTests
```

Expected: FAIL (`PosView` 타입 없음)

- [ ] **Step 3: XAML 작성**

`src/FishingMartPos/Views/PosView.xaml`:

```xml
<UserControl x:Class="FishingMartPos.Views.PosView"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006"
             xmlns:d="http://schemas.microsoft.com/expression/blend/2008"
             mc:Ignorable="d">
    <Grid>
        <Grid.RowDefinitions>
            <RowDefinition Height="*" />
            <RowDefinition Height="200" />
        </Grid.RowDefinitions>

        <Grid Grid.Row="0">
            <Grid.ColumnDefinitions>
                <ColumnDefinition Width="*" />
                <ColumnDefinition Width="*" />
            </Grid.ColumnDefinitions>

            <!-- 좌측: 보류 바 + 장바구니 -->
            <Border Grid.Column="0" BorderBrush="{DynamicResource HeaderBorder}" BorderThickness="0,0,1,0" Background="White">
                <Grid>
                    <Grid.RowDefinitions>
                        <RowDefinition Height="Auto" />
                        <RowDefinition Height="Auto" />
                        <RowDefinition Height="*" />
                        <RowDefinition Height="Auto" />
                    </Grid.RowDefinitions>

                    <Grid Grid.Row="0" Margin="8,6">
                        <Grid.ColumnDefinitions>
                            <ColumnDefinition Width="*" />
                            <ColumnDefinition Width="Auto" />
                        </Grid.ColumnDefinitions>
                        <TextBlock Text="{Binding HeldOrders.Count, StringFormat='보류 {0}건'}"
                                   VerticalAlignment="Center" FontSize="11" Foreground="{DynamicResource MutedText}" />
                        <StackPanel Grid.Column="1" Orientation="Horizontal">
                            <Button Content="보류" Padding="10,4" Margin="0,0,4,0" FontSize="11"
                                    Command="{Binding HoldOrderCommand}" />
                            <Button Content="보류목록" Padding="10,4" FontSize="11"
                                    Command="{Binding ShowHeldListCommand}" />
                        </StackPanel>
                    </Grid>

                    <Grid Grid.Row="1" Margin="12,4" >
                        <Grid.ColumnDefinitions>
                            <ColumnDefinition Width="2*" />
                            <ColumnDefinition Width="0.8*" />
                            <ColumnDefinition Width="1*" />
                            <ColumnDefinition Width="1*" />
                        </Grid.ColumnDefinitions>
                        <TextBlock Text="상품" FontSize="10" FontWeight="Bold" Foreground="{DynamicResource MutedText}" />
                        <TextBlock Grid.Column="1" Text="매수" FontSize="10" FontWeight="Bold" HorizontalAlignment="Center" Foreground="{DynamicResource MutedText}" />
                        <TextBlock Grid.Column="2" Text="단가" FontSize="10" FontWeight="Bold" HorizontalAlignment="Right" Foreground="{DynamicResource MutedText}" />
                        <TextBlock Grid.Column="3" Text="금액" FontSize="10" FontWeight="Bold" HorizontalAlignment="Right" Foreground="{DynamicResource MutedText}" />
                    </Grid>

                    <ItemsControl Grid.Row="2" ItemsSource="{Binding CartLines}" Background="{DynamicResource MenuBackground}">
                        <ItemsControl.ItemTemplate>
                            <DataTemplate>
                                <Button Command="{Binding SelectCommand}" CommandParameter="{Binding Barcode}"
                                        HorizontalContentAlignment="Stretch" Padding="12,5" BorderThickness="0,0,0,1"
                                        BorderBrush="{DynamicResource Divider}">
                                    <Button.Style>
                                        <Style TargetType="Button">
                                            <Setter Property="Background" Value="White" />
                                            <Style.Triggers>
                                                <DataTrigger Binding="{Binding IsSelected}" Value="True">
                                                    <Setter Property="Background" Value="{DynamicResource MenuTileHoverBackground}" />
                                                </DataTrigger>
                                            </Style.Triggers>
                                        </Style>
                                    </Button.Style>
                                    <Grid>
                                        <Grid.ColumnDefinitions>
                                            <ColumnDefinition Width="2*" />
                                            <ColumnDefinition Width="0.8*" />
                                            <ColumnDefinition Width="1*" />
                                            <ColumnDefinition Width="1*" />
                                        </Grid.ColumnDefinitions>
                                        <TextBlock Text="{Binding Name}" FontSize="13" FontWeight="Bold" Foreground="{DynamicResource TitleText}" />
                                        <TextBlock Grid.Column="1" Text="{Binding Qty}" FontSize="13" HorizontalAlignment="Center" />
                                        <TextBlock Grid.Column="2" Text="{Binding PriceStr}" FontSize="13" HorizontalAlignment="Right" Foreground="{DynamicResource MutedText}" />
                                        <TextBlock Grid.Column="3" Text="{Binding LineTotalStr}" FontSize="13" FontWeight="Bold" HorizontalAlignment="Right" />
                                    </Grid>
                                </Button>
                            </DataTemplate>
                        </ItemsControl.ItemTemplate>
                    </ItemsControl>

                    <Border Grid.Row="3" BorderThickness="0,2,0,0" BorderBrush="{DynamicResource Accent}" Padding="16,8">
                        <Grid>
                            <TextBlock Text="합계금액" FontSize="14" FontWeight="Bold" Foreground="{DynamicResource MutedText}" />
                            <TextBlock Text="{Binding TotalAmountStr}" FontSize="20" FontWeight="Black"
                                       Foreground="{DynamicResource Accent}" HorizontalAlignment="Right" />
                        </Grid>
                    </Border>
                </Grid>
            </Border>

            <!-- 우측: 카테고리 탭 + 상품 그리드 -->
            <Grid Grid.Column="1">
                <Grid.RowDefinitions>
                    <RowDefinition Height="Auto" />
                    <RowDefinition Height="*" />
                </Grid.RowDefinitions>

                <ItemsControl Grid.Row="0" ItemsSource="{Binding Categories}">
                    <ItemsControl.ItemsPanel>
                        <ItemsPanelTemplate>
                            <WrapPanel />
                        </ItemsPanelTemplate>
                    </ItemsControl.ItemsPanel>
                    <ItemsControl.ItemTemplate>
                        <DataTemplate>
                            <Button Content="{Binding Name}" Command="{Binding SelectCommand}"
                                    Padding="12,10" FontSize="14" FontWeight="Bold" BorderThickness="0,0,1,1"
                                    BorderBrush="{DynamicResource MenuTileBorder}">
                                <Button.Style>
                                    <Style TargetType="Button">
                                        <Setter Property="Background" Value="{DynamicResource HeaderButtonBackground}" />
                                        <Setter Property="Foreground" Value="{DynamicResource MutedText}" />
                                        <Style.Triggers>
                                            <DataTrigger Binding="{Binding IsActive}" Value="True">
                                                <Setter Property="Background" Value="{DynamicResource Accent}" />
                                                <Setter Property="Foreground" Value="White" />
                                            </DataTrigger>
                                        </Style.Triggers>
                                    </Style>
                                </Button.Style>
                            </Button>
                        </DataTemplate>
                    </ItemsControl.ItemTemplate>
                </ItemsControl>

                <ItemsControl Grid.Row="1" ItemsSource="{Binding VisibleProducts}" Background="{DynamicResource MenuBackground}">
                    <ItemsControl.ItemsPanel>
                        <ItemsPanelTemplate>
                            <UniformGrid Columns="4" />
                        </ItemsPanelTemplate>
                    </ItemsControl.ItemsPanel>
                    <ItemsControl.ItemTemplate>
                        <DataTemplate>
                            <Button Command="{Binding AddCommand}" Margin="6" Padding="8" Background="White"
                                    BorderBrush="{DynamicResource InputBorder}" BorderThickness="1">
                                <StackPanel>
                                    <Border Background="{Binding Swatch}" Height="32" CornerRadius="2" Margin="0,0,0,4">
                                        <TextBlock Text="{Binding Initial}" Foreground="White" FontWeight="Bold" FontSize="14"
                                                   HorizontalAlignment="Center" VerticalAlignment="Center" />
                                    </Border>
                                    <TextBlock Text="{Binding Name}" FontSize="12" FontWeight="Bold" TextAlignment="Center"
                                               Foreground="{DynamicResource TitleText}" />
                                    <TextBlock Text="{Binding PriceStr}" FontSize="16" FontWeight="Black" TextAlignment="Center"
                                               Foreground="{DynamicResource Accent}" />
                                </StackPanel>
                            </Button>
                        </DataTemplate>
                    </ItemsControl.ItemTemplate>
                </ItemsControl>
            </Grid>
        </Grid>

        <!-- 하단: 수량/키패드/결제 -->
        <Border Grid.Row="1" BorderThickness="0,1,0,0" BorderBrush="{DynamicResource HeaderBorder}" Background="White">
            <Grid>
                <Grid.ColumnDefinitions>
                    <ColumnDefinition Width="*" />
                    <ColumnDefinition Width="*" />
                </Grid.ColumnDefinitions>

                <Grid Grid.Column="0">
                    <Grid.ColumnDefinitions>
                        <ColumnDefinition Width="130" />
                        <ColumnDefinition Width="*" />
                    </Grid.ColumnDefinitions>

                    <StackPanel Grid.Column="0" Margin="10,6">
                        <TextBlock Text="합계수량" FontSize="10" Foreground="{DynamicResource MutedText}" />
                        <TextBlock Text="{Binding TotalQtyStr}" FontSize="15" FontWeight="Black" />
                        <TextBlock Text="받은금액" FontSize="10" Foreground="{DynamicResource MutedText}" Margin="0,4,0,0" />
                        <TextBlock Text="{Binding CashInputStr}" FontSize="15" FontWeight="Black" Foreground="{DynamicResource Accent}" />
                        <TextBlock Text="남은금액" FontSize="10" Foreground="{DynamicResource MutedText}" Margin="0,4,0,0" />
                        <TextBlock Text="{Binding ChangeStr}" FontSize="15" FontWeight="Black" />
                        <StackPanel Orientation="Horizontal" Margin="0,4,0,0">
                            <Button Content="－" Width="30" Command="{Binding DecSelectedCommand}" />
                            <Button Content="＋" Width="30" Margin="4,0,0,0" Command="{Binding IncSelectedCommand}" />
                        </StackPanel>
                        <Button Content="삭제" Margin="0,4,0,0" Command="{Binding RemoveSelectedCommand}" />
                    </StackPanel>

                    <UniformGrid Grid.Column="1" Columns="3" Rows="4" Margin="8">
                        <Button Content="1" Command="{Binding PressKeyCommand}" CommandParameter="1" Margin="3" />
                        <Button Content="2" Command="{Binding PressKeyCommand}" CommandParameter="2" Margin="3" />
                        <Button Content="3" Command="{Binding PressKeyCommand}" CommandParameter="3" Margin="3" />
                        <Button Content="4" Command="{Binding PressKeyCommand}" CommandParameter="4" Margin="3" />
                        <Button Content="5" Command="{Binding PressKeyCommand}" CommandParameter="5" Margin="3" />
                        <Button Content="6" Command="{Binding PressKeyCommand}" CommandParameter="6" Margin="3" />
                        <Button Content="7" Command="{Binding PressKeyCommand}" CommandParameter="7" Margin="3" />
                        <Button Content="8" Command="{Binding PressKeyCommand}" CommandParameter="8" Margin="3" />
                        <Button Content="9" Command="{Binding PressKeyCommand}" CommandParameter="9" Margin="3" />
                        <Button Content="&lt;" Command="{Binding PressKeyCommand}" CommandParameter="&lt;" Margin="3" Background="{DynamicResource KeypadSpecialBackground}" />
                        <Button Content="0" Command="{Binding PressKeyCommand}" CommandParameter="0" Margin="3" />
                        <Button Content="CLS" Command="{Binding PressKeyCommand}" CommandParameter="CLS" Margin="3" Background="{DynamicResource KeypadSpecialBackground}" />
                    </UniformGrid>
                </Grid>

                <Grid Grid.Column="1" Margin="10,6">
                    <Grid.RowDefinitions>
                        <RowDefinition Height="*" />
                        <RowDefinition Height="*" />
                        <RowDefinition Height="1.3*" />
                    </Grid.RowDefinitions>

                    <UniformGrid Grid.Row="0" Columns="3">
                        <Button Content="영수증관리" IsEnabled="False" Margin="3" />
                        <Button Content="직전정보" IsEnabled="False" Margin="3" />
                        <Button Content="영수증발행" IsEnabled="False" Margin="3" />
                    </UniformGrid>
                    <Button Grid.Row="1" Content="돈통열기" IsEnabled="False" Margin="3" />
                    <UniformGrid Grid.Row="2" Columns="4" Margin="0,6,0,0">
                        <Button Content="초기화" Command="{Binding ResetOrderCommand}" Margin="3" />
                        <Button Content="현금결제" Command="{Binding PayCashCommand}" Margin="3"
                                Background="{DynamicResource Accent}" Foreground="White" />
                        <Button Content="카드결제1" Command="{Binding PayCard1Command}" Margin="3"
                                Background="{DynamicResource Accent}" Foreground="White" />
                        <Button Content="카드결제2" Command="{Binding PayCard2Command}" Margin="3"
                                Background="{DynamicResource AccentDark}" Foreground="White" />
                    </UniformGrid>
                </Grid>
            </Grid>
        </Border>

        <!-- 보류목록 오버레이 -->
        <Grid Grid.RowSpan="2" Background="{DynamicResource ModalOverlay}"
              Visibility="{Binding IsHeldListVisible, Converter={StaticResource BooleanToVisibilityConverter}}">
            <Border Background="White" BorderBrush="{DynamicResource CardBorder}" BorderThickness="1"
                    Padding="20" Width="360" MaxHeight="400" HorizontalAlignment="Center" VerticalAlignment="Center">
                <StackPanel>
                    <Grid Margin="0,0,0,10">
                        <TextBlock Text="보류 목록" FontSize="16" FontWeight="Bold" />
                        <Button Content="닫기" HorizontalAlignment="Right" Command="{Binding HideHeldListCommand}" />
                    </Grid>
                    <ItemsControl ItemsSource="{Binding HeldOrders}">
                        <ItemsControl.ItemTemplate>
                            <DataTemplate>
                                <Grid Margin="0,4">
                                    <Grid.ColumnDefinitions>
                                        <ColumnDefinition Width="*" />
                                        <ColumnDefinition Width="Auto" />
                                    </Grid.ColumnDefinitions>
                                    <TextBlock Text="{Binding HeldAtStr}" VerticalAlignment="Center" />
                                    <TextBlock Grid.Column="0" Text="{Binding TotalStr}" HorizontalAlignment="Right" Margin="0,0,80,0" VerticalAlignment="Center" />
                                    <Button Grid.Column="1" Content="불러오기" Command="{Binding RecallCommand}" />
                                </Grid>
                            </DataTemplate>
                        </ItemsControl.ItemTemplate>
                    </ItemsControl>
                </StackPanel>
            </Border>
        </Grid>

        <!-- 결제완료 토스트 -->
        <Grid Grid.RowSpan="2" Background="{DynamicResource ModalOverlay}"
              Visibility="{Binding ToastMessage, Converter={StaticResource NullToVisibilityConverter}}">
            <Border Background="White" BorderBrush="{DynamicResource CardBorder}" BorderThickness="1"
                    Padding="48,36" HorizontalAlignment="Center" VerticalAlignment="Center">
                <StackPanel HorizontalAlignment="Center">
                    <Border Width="56" Height="56" Background="{DynamicResource Accent}" Margin="0,0,0,12">
                        <TextBlock Text="✓" FontSize="28" FontWeight="Bold" Foreground="White"
                                   HorizontalAlignment="Center" VerticalAlignment="Center" />
                    </Border>
                    <TextBlock Text="{Binding ToastMessage}" FontSize="18" FontWeight="Bold"
                               Foreground="{DynamicResource TitleText}" />
                </StackPanel>
            </Border>
        </Grid>
    </Grid>
</UserControl>
```

- [ ] **Step 4: `NullToVisibilityConverter` 추가** (토스트는 문자열이 null이 아닐 때만 보여야 하므로 `BooleanToVisibilityConverter`로는 부족)

`src/FishingMartPos/Converters/NullToVisibilityConverter.cs`:

```csharp
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace FishingMartPos.Converters;

public sealed class NullToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object parameter, CultureInfo culture) =>
        value is null ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
```

`PosView.xaml`의 `UserControl.Resources`에 추가(현재 `PosView.xaml`에는 `UserControl.Resources` 블록이 없으므로 루트 `<UserControl ...>` 바로 아래에 삽입):

```xml
    <UserControl.Resources>
        <BooleanToVisibilityConverter x:Key="BooleanToVisibilityConverter" />
        <local:NullToVisibilityConverter x:Key="NullToVisibilityConverter" xmlns:local="clr-namespace:FishingMartPos.Converters" />
    </UserControl.Resources>
```

- [ ] **Step 5: code-behind 작성**

`src/FishingMartPos/Views/PosView.xaml.cs`:

```csharp
using System.Windows.Controls;

namespace FishingMartPos.Views;

public partial class PosView : UserControl
{
    public PosView()
    {
        InitializeComponent();
    }
}
```

- [ ] **Step 6: 테스트 통과 확인**

```bash
dotnet test tests\FishingMartPos.Tests --filter FullyQualifiedName~PosViewSmokeTests
```

Expected: PASS (1 test)

- [ ] **Step 7: Commit**

```bash
git add src/FishingMartPos/Views/PosView.xaml src/FishingMartPos/Views/PosView.xaml.cs src/FishingMartPos/Converters tests/FishingMartPos.Tests/Views/PosViewSmokeTests.cs
git commit -m "PosView 디자인 이식 (카테고리/상품그리드/장바구니/키패드/결제바/보류목록)"
```

---

### Task 11: 배선 — 메인메뉴에서 실제 판매화면으로 이동 + DI 등록

**Files:**
- Modify: `src/FishingMartPos/ViewModels/MainMenuViewModel.cs`
- Modify: `tests/FishingMartPos.Tests/ViewModels/MainMenuViewModelTests.cs`
- Modify: `src/FishingMartPos/App.xaml.cs`
- Modify: `src/FishingMartPos/MainWindow.xaml`

**Interfaces:**
- Consumes: `PosViewModel`(Task 8·9)
- Produces: 실행 시 메인메뉴 "판매" 클릭 → 실제 판매 화면으로 이동

- [ ] **Step 1: 테스트를 먼저 새 기대값으로 수정**

`tests/FishingMartPos.Tests/ViewModels/MainMenuViewModelTests.cs`의 `Create()`와 `GoToSales_...` 테스트를 아래로 교체:

```csharp
    private static (MainMenuViewModel vm, ICurrentSession session, INavigationService navigation) Create()
    {
        var session = new CurrentSession();
        session.SignIn(
            new Staff { StaffCode = "ADMIN1", StaffName = "관리자", Role = "ADMIN", UseYn = "Y" },
            new PosTerminal { PosCode = "2", PosName = "POS2" });
        var navigation = new NavigationService();
        var staffRepository = new FishingMartPos.Tests.Fakes.FakeStaffRepository(
            new Dictionary<string, Staff>());
        var terminals = new[] { new PosTerminal { PosCode = "1", PosName = "POS1" } };
        var posViewModel = new PosViewModel(
            new FishingMartPos.Tests.Fakes.FakeProductRepository(Array.Empty<Product>()),
            new FishingMartPos.Tests.Fakes.FakeCodeRepository(new Dictionary<string, IReadOnlyList<CodeItem>>()),
            new FishingMartPos.Tests.Fakes.FakeSalesRepository(),
            new FishingMartPos.Tests.Fakes.FakeHeldOrderRepository(),
            new FishingMartPos.Tests.Fakes.FakeDelayProvider(),
            session);

        var vm = new MainMenuViewModel(session, navigation)
        {
            LoginViewModelFactory = () => new LoginViewModel(staffRepository, session, navigation, terminals),
            PosViewModelFactory = () => Task.FromResult(posViewModel),
        };
        return (vm, session, navigation);
    }

    [Fact]
    public async Task GoToSales_NavigatesToPosViewModel()
    {
        var (vm, _, navigation) = Create();

        await vm.GoToSalesCommand.ExecuteAsync(null);

        Assert.IsType<PosViewModel>(navigation.CurrentViewModel);
    }
```

- [ ] **Step 2: 테스트 실패 확인**

```bash
dotnet test tests\FishingMartPos.Tests --filter FullyQualifiedName~MainMenuViewModelTests
```

Expected: FAIL (`PosViewModelFactory` 속성 없음, `GoToSalesCommand`가 동기 커맨드라 `ExecuteAsync` 없음)

- [ ] **Step 3: MainMenuViewModel 수정**

`src/FishingMartPos/ViewModels/MainMenuViewModel.cs`에서 `GoToSales` 관련 부분을 아래로 교체:

```csharp
    public Func<Task<PosViewModel>>? PosViewModelFactory { get; init; }

    [RelayCommand]
    private async Task GoToSales()
    {
        var posViewModel = await PosViewModelFactory!.Invoke();
        _navigation.NavigateTo(posViewModel);
    }
```

(기존 `private void GoToSales() => _navigation.NavigateTo(new PlaceholderViewModel("판매"));` 를 위 코드로 완전히 대체한다)

- [ ] **Step 4: 테스트 통과 확인**

```bash
dotnet test tests\FishingMartPos.Tests --filter FullyQualifiedName~MainMenuViewModelTests
```

Expected: PASS (6 tests)

- [ ] **Step 5: App.xaml.cs에 신규 리포지토리/뷰모델 팩토리 DI 등록**

`src/FishingMartPos/App.xaml.cs`의 `OnStartup` 메서드에서 `services.AddSingleton<INavigationService, NavigationService>();` 바로 아래에 추가:

```csharp
        services.AddSingleton<IProductRepository, ProductRepository>();
        services.AddSingleton<ICodeRepository, CodeRepository>();
        services.AddSingleton<ISalesRepository, SalesRepository>();
        services.AddSingleton<IHeldOrderRepository, HeldOrderRepository>();
        services.AddSingleton<IDelayProvider, DelayProvider>();
```

파일 상단 `using` 목록에 아래 추가:

```csharp
using FishingMartPos.Services;
```

(이미 `using FishingMartPos.Services;`가 있다면 중복 추가하지 않는다 — Phase 1에서 `ICurrentSession`용으로 이미 있을 수 있으니 확인 후 없을 때만 추가)

`mainMenuViewModel` 생성부(`LoginViewModel.SubmitAsync` 내부)에서 `PosViewModelFactory`를 함께 주입하도록 `LoginViewModel.cs`도 수정한다:

`src/FishingMartPos/ViewModels/LoginViewModel.cs`의 `SubmitAsync` 안, `MainMenuViewModel` 생성 부분을 아래로 교체:

```csharp
        var mainMenuViewModel = new MainMenuViewModel(_session, _navigation)
        {
            LoginViewModelFactory = () => new LoginViewModel(_staffRepository, _session, _navigation, Terminals),
            PosViewModelFactory = _posViewModelFactory,
        };
        _navigation.NavigateTo(mainMenuViewModel);
```

`LoginViewModel`에 `Func<Task<PosViewModel>>` 필드/생성자 파라미터를 추가한다. `src/FishingMartPos/ViewModels/LoginViewModel.cs` 생성자를 아래로 교체:

```csharp
    private readonly Func<Task<PosViewModel>> _posViewModelFactory;

    public LoginViewModel(
        IStaffRepository staffRepository,
        ICurrentSession session,
        INavigationService navigation,
        IReadOnlyList<PosTerminal> terminals,
        Func<Task<PosViewModel>> posViewModelFactory)
    {
        _staffRepository = staffRepository;
        _session = session;
        _navigation = navigation;
        Terminals = terminals;
        _selectedTerminal = terminals[0];
        _posViewModelFactory = posViewModelFactory;
    }
```

> 이 생성자 시그니처 변경으로 `LoginViewModel`을 생성하는 모든 곳(App.xaml.cs, 테스트의 `CreateViewModel`/`Create()` 헬퍼들)에 `posViewModelFactory` 인자를 추가해야 한다. Task 11 Step 6에서 `App.xaml.cs`를, Step 7에서 기존 테스트 파일들을 함께 고친다.

- [ ] **Step 6: App.xaml.cs에서 LoginViewModel 생성 시 PosViewModel 팩토리 전달**

`src/FishingMartPos/App.xaml.cs`의 `OnStartup`에서 서비스 조회 부분과 `CreateLoginViewModel` 로컬 함수를 아래로 교체:

```csharp
        var productRepository = _services.GetRequiredService<IProductRepository>();
        var codeRepository = _services.GetRequiredService<ICodeRepository>();
        var salesRepository = _services.GetRequiredService<ISalesRepository>();
        var heldOrderRepository = _services.GetRequiredService<IHeldOrderRepository>();
        var delayProvider = _services.GetRequiredService<IDelayProvider>();

        async Task<PosViewModel> CreatePosViewModelAsync()
        {
            var vm = new PosViewModel(productRepository, codeRepository, salesRepository, heldOrderRepository, delayProvider, session);
            await vm.LoadAsync();
            return vm;
        }

        LoginViewModel CreateLoginViewModel() =>
            new(staffRepository, session, navigation, terminals, CreatePosViewModelAsync);

        navigation.NavigateTo(CreateLoginViewModel());
```

- [ ] **Step 7: 기존 테스트 파일에서 `LoginViewModel` 생성 부분에 팩토리 인자 추가**

`tests/FishingMartPos.Tests/ViewModels/LoginViewModelTests.cs`의 `CreateViewModel` 안 `new LoginViewModel(repository, session, navigation, terminals)` 호출을 아래로 교체:

```csharp
        return new LoginViewModel(repository, session, navigation, terminals, CreateDummyPosViewModelFactory(session));
```

같은 파일 클래스 내부에 헬퍼 메서드 추가:

```csharp
    private static Func<Task<PosViewModel>> CreateDummyPosViewModelFactory(ICurrentSession session) =>
        () => Task.FromResult(new PosViewModel(
            new FakeProductRepository(Array.Empty<Product>()),
            new FakeCodeRepository(new Dictionary<string, IReadOnlyList<CodeItem>>()),
            new FakeSalesRepository(),
            new FakeHeldOrderRepository(),
            new FakeDelayProvider(),
            session));
```

`tests/FishingMartPos.Tests/ViewModels/MainMenuViewModelTests.cs`의 `Create()` 안 `LoginViewModelFactory` 람다도 동일하게 5번째 인자를 추가:

```csharp
            LoginViewModelFactory = () => new LoginViewModel(staffRepository, session, navigation, terminals, () => Task.FromResult(posViewModel)),
```

- [ ] **Step 8: MainWindow.xaml에 PosViewModel → PosView DataTemplate 추가**

`src/FishingMartPos/MainWindow.xaml`의 `<Window.Resources>` 안, 기존 `PlaceholderViewModel` DataTemplate 위나 아래에 추가:

```xml
        <DataTemplate DataType="{x:Type vm:PosViewModel}">
            <views:PosView />
        </DataTemplate>
```

- [ ] **Step 9: 빌드 및 전체 테스트 확인**

```bash
dotnet build FishingMartPos.sln
dotnet test FishingMartPos.sln
```

Expected: 빌드 성공, 모든 테스트 PASS

- [ ] **Step 10: 수동 실행 확인**

```bash
dotnet run --project src\FishingMartPos\FishingMartPos.csproj
```

Expected: PIN `0000` 로그인 → 메인메뉴 → "판매" 클릭 시 실제 상품 그리드가 보이는 판매 화면으로 이동. 상품 클릭 시 좌측 장바구니에 담기고 합계금액이 갱신됨. "보류" 클릭 시 장바구니가 비워지고 "보류목록"에서 다시 불러올 수 있음. "현금결제"/"카드결제1"/"카드결제2" 클릭 시 완료 토스트가 뜨고 장바구니가 초기화됨(DB에 매출 저장 확인은 `SELECT * FROM sales_header_tb ORDER BY sale_no DESC LIMIT 1;`로 확인).

- [ ] **Step 11: Commit**

```bash
git add src/FishingMartPos/ViewModels/MainMenuViewModel.cs src/FishingMartPos/ViewModels/LoginViewModel.cs src/FishingMartPos/App.xaml.cs src/FishingMartPos/MainWindow.xaml tests/FishingMartPos.Tests/ViewModels/MainMenuViewModelTests.cs tests/FishingMartPos.Tests/ViewModels/LoginViewModelTests.cs
git commit -m "메인메뉴 판매 진입을 실제 PosViewModel로 연결, DI 배선 완료"
```

---

## Self-Review 결과

- **스펙 커버리지**: 설계문서 2절 판매(POS) 화면 항목(카테고리 탭/상품그리드/장바구니/키패드/현금·카드결제1·카드결제2/보류)이 Task 3~11에 모두 반영됨. 영수증관리/직전정보/영수증발행/돈통열기는 설계 범위대로 Phase 4까지 비활성 버튼으로 남김(Task 10에서 `IsEnabled="False"`로 명시).
- **플레이스홀더 스캔**: "TBD"/"나중에" 없음. VAN 미연동은 명시적으로 Phase 3로 범위를 못박음(Global Constraints).
- **타입 일관성**: `Cart`, `CartLine`, `Product`, `CodeItem`, `SaleHeader`, `SaleDetailLine`, `HeldOrderLine`, `PosViewModel`, `CartLineViewModel`, `CategoryTabViewModel`, `ProductTileViewModel`, `HeldOrderSummaryViewModel` 이름과 시그니처가 Task 3~11 전체에서 동일하게 유지되는지 재확인 완료. `LoginViewModel` 생성자 시그니처 변경이 App.xaml.cs·기존 테스트 2곳에 모두 반영됨을 Task 11에서 명시적으로 다룸.

---

**Plan complete and saved to `docs/superpowers/plans/2026-07-21-phase2-sales-screen.md`. Two execution options:**

**1. Subagent-Driven (recommended)** - 태스크마다 새 서브에이전트를 띄워 구현시키고, 태스크 사이마다 검토

**2. Inline Execution** - 이 세션에서 직접 태스크를 순서대로 실행하고, 중간중간 화면을 캡처해서 보여드리며 진행

**어떤 방식으로 진행할까요?**
