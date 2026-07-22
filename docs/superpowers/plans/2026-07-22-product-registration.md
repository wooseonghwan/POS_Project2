# 상품 등록/수정 화면 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 재고관리 화면에 상품 등록/수정 기능을 추가한다. 등록과 수정은 같은 폼을 재사용하고, 대/소/POS분류 코드를 폼 안에서 바로 추가/삭제할 수 있으며, 상품 사진을 실제로 업로드해 재고관리 목록에 표시한다.

**Architecture:** 새 화면 `InventoryFormView`/`InventoryFormViewModel`을 추가한다. `InventoryViewModel`이 팩토리(`Func<InventoryViewModel, string?, Task<InventoryFormViewModel>>`)를 통해 이 화면으로 이동시키고(barcode가 null이면 등록, 있으면 수정), 저장 완료 시 다시 `InventoryViewModel`로 돌아와 목록을 새로고침한다. 이 패턴은 기존 `MainMenuViewModel` → `InventoryViewModel`/`PosViewModel` 팩토리 방식과 동일하다. 사진 선택(`IPhotoPicker`)과 사진 저장(`IProductPhotoStorage`)은 별도 인터페이스로 분리해 파일 I/O 없이 ViewModel을 단위 테스트할 수 있게 한다.

**Tech Stack:** WPF/.NET8, CommunityToolkit.Mvvm, Dapper + MySqlConnector, xUnit.

## Global Constraints

- 바코드 접두사: `880000002` (9자리) + 4자리 0-padding 일련번호, 총 13자리. (스펙 문서에 `8800000002`로 적혀 있었으나 실제 데이터 확인 결과 9자리가 맞다 — 이 문서 값을 따른다.)
- 사진 파일은 `AppContext.BaseDirectory` 하위 `ProductPhotos/{바코드}.{확장자}`에 저장하고, DB에는 `ProductPhotos/{바코드}.{확장자}` 형태의 상대경로만 저장한다.
- 상품명 필수, 단가 필수(0보다 커야 함), 초기재고/재고는 공란이면 0.
- 판매 화면(POS) 상품 타일 디자인은 변경하지 않는다.
- 기존 테스트(현재 101개 이상)는 계속 통과해야 한다. 각 태스크 끝에 `dotnet test tests/FishingMartPos.Tests/FishingMartPos.Tests.csproj` 실행.
- 실행 중인 앱 프로세스가 있으면 빌드 전에 반드시 종료한다(`Get-Process FishingMartPos | Stop-Process -Force`).

---

### Task 1: Product 모델·리포지토리 확장 (PhotoPath, 저장/코드 CRUD)

**Files:**
- Modify: `src/FishingMartPos/Models/Product.cs`
- Modify: `src/FishingMartPos/Repositories/IProductRepository.cs`
- Modify: `src/FishingMartPos/Repositories/ProductRepository.cs`
- Modify: `src/FishingMartPos/Repositories/ICodeRepository.cs`
- Modify: `src/FishingMartPos/Repositories/CodeRepository.cs`
- Modify: `tests/FishingMartPos.Tests/Fakes/FakeProductRepository.cs`
- Modify: `tests/FishingMartPos.Tests/Fakes/FakeCodeRepository.cs`
- Test: `tests/FishingMartPos.Tests/Repositories/ProductRepositorySaveTests.cs` (신규, 실제 dev DB 대상 통합 테스트 — `HeldOrderRepositoryTests.cs`와 동일한 패턴)
- Test: `tests/FishingMartPos.Tests/Repositories/CodeRepositoryTests.cs` (신규, 실제 dev DB 대상)

**Interfaces:**
- Produces: `Product.PhotoPath` (string?, nullable), `IProductRepository.SaveAsync(Product product)`, `ICodeRepository.AddAsync(string codeGbn, string codeCd, string codeNm)`, `ICodeRepository.DeleteAsync(string codeGbn, string codeCd)`

- [ ] **Step 1: Product 모델에 PhotoPath 추가**

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
    public string? PhotoPath { get; init; }
}
```

- [ ] **Step 2: IProductRepository/ProductRepository에 SaveAsync 추가**

`src/FishingMartPos/Repositories/IProductRepository.cs`:
```csharp
using FishingMartPos.Models;

namespace FishingMartPos.Repositories;

public interface IProductRepository
{
    Task<IReadOnlyList<Product>> GetActiveAsync();
    Task DeactivateAsync(string barcode);
    Task SaveAsync(Product product);
}
```

`src/FishingMartPos/Repositories/ProductRepository.cs`에 `GetActiveAsync`/`DeactivateAsync` 아래 추가:
```csharp
    public async Task SaveAsync(Product product)
    {
        using var connection = _connectionFactory.CreateOpenConnection();
        const string sql = """
            INSERT INTO product_tb (barcode, major_cd, minor_cd, poscat_cd, name, price, stock_qty, photo_path, use_yn)
            VALUES (@Barcode, @MajorCd, @MinorCd, @PosCatCd, @Name, @Price, @StockQty, @PhotoPath, 'Y')
            ON DUPLICATE KEY UPDATE
                major_cd = VALUES(major_cd), minor_cd = VALUES(minor_cd), poscat_cd = VALUES(poscat_cd),
                name = VALUES(name), price = VALUES(price), stock_qty = VALUES(stock_qty),
                photo_path = VALUES(photo_path), use_yn = 'Y'
            """;
        await connection.ExecuteAsync(sql, product);
    }
```
(Dapper가 `Product`의 public 속성을 파라미터로 그대로 매핑한다 — `PhotoPath`가 null이어도 문제 없다.)

또한 `GetActiveAsync`의 SELECT 목록에 `photo_path AS PhotoPath` 컬럼을 추가한다:
```csharp
        const string sql = """
            SELECT barcode AS Barcode, major_cd AS MajorCd, minor_cd AS MinorCd, poscat_cd AS PosCatCd,
                   name AS Name, price AS Price, stock_qty AS StockQty, photo_path AS PhotoPath
            FROM product_tb
            WHERE use_yn = 'Y'
            ORDER BY poscat_cd, name
            """;
```

- [ ] **Step 3: ICodeRepository/CodeRepository에 Add/Delete 추가**

`src/FishingMartPos/Repositories/ICodeRepository.cs`:
```csharp
using FishingMartPos.Models;

namespace FishingMartPos.Repositories;

public interface ICodeRepository
{
    Task<IReadOnlyList<CodeItem>> GetByGroupAsync(string codeGbn);
    Task AddAsync(string codeGbn, string codeCd, string codeNm);
    Task DeleteAsync(string codeGbn, string codeCd);
}
```

`src/FishingMartPos/Repositories/CodeRepository.cs`에 추가:
```csharp
    public async Task AddAsync(string codeGbn, string codeCd, string codeNm)
    {
        using var connection = _connectionFactory.CreateOpenConnection();
        const string sql = """
            INSERT INTO code_tb (code_gbn, code_cd, code_nm, sort_no)
            VALUES (@CodeGbn, @CodeCd, @CodeNm, 0)
            """;
        await connection.ExecuteAsync(sql, new { CodeGbn = codeGbn, CodeCd = codeCd, CodeNm = codeNm });
    }

    public async Task DeleteAsync(string codeGbn, string codeCd)
    {
        using var connection = _connectionFactory.CreateOpenConnection();
        const string sql = "DELETE FROM code_tb WHERE code_gbn = @CodeGbn AND code_cd = @CodeCd";
        await connection.ExecuteAsync(sql, new { CodeGbn = codeGbn, CodeCd = codeCd });
    }
```

- [ ] **Step 4: Fake 리포지토리 업데이트**

`tests/FishingMartPos.Tests/Fakes/FakeProductRepository.cs` (전체 교체):
```csharp
using FishingMartPos.Models;
using FishingMartPos.Repositories;

namespace FishingMartPos.Tests.Fakes;

public sealed class FakeProductRepository : IProductRepository
{
    private readonly List<Product> _products;

    public FakeProductRepository(IReadOnlyList<Product> products)
    {
        _products = products.ToList();
    }

    public List<string> DeactivatedBarcodes { get; } = new();
    public List<Product> SavedProducts { get; } = new();

    public Task<IReadOnlyList<Product>> GetActiveAsync() =>
        Task.FromResult((IReadOnlyList<Product>)_products);

    public Task DeactivateAsync(string barcode)
    {
        DeactivatedBarcodes.Add(barcode);
        _products.RemoveAll(p => p.Barcode == barcode);
        return Task.CompletedTask;
    }

    public Task SaveAsync(Product product)
    {
        SavedProducts.Add(product);
        _products.RemoveAll(p => p.Barcode == product.Barcode);
        _products.Add(product);
        return Task.CompletedTask;
    }
}
```

`tests/FishingMartPos.Tests/Fakes/FakeCodeRepository.cs` (전체 교체):
```csharp
using FishingMartPos.Models;
using FishingMartPos.Repositories;

namespace FishingMartPos.Tests.Fakes;

public sealed class FakeCodeRepository : ICodeRepository
{
    private readonly Dictionary<string, List<CodeItem>> _byGroup;

    public FakeCodeRepository(Dictionary<string, IReadOnlyList<CodeItem>> byGroup)
    {
        _byGroup = byGroup.ToDictionary(kv => kv.Key, kv => kv.Value.ToList());
    }

    public List<(string CodeGbn, string CodeCd)> DeletedCodes { get; } = new();

    public Task<IReadOnlyList<CodeItem>> GetByGroupAsync(string codeGbn) =>
        Task.FromResult(_byGroup.TryGetValue(codeGbn, out var list) ? (IReadOnlyList<CodeItem>)list : Array.Empty<CodeItem>());

    public Task AddAsync(string codeGbn, string codeCd, string codeNm)
    {
        if (!_byGroup.TryGetValue(codeGbn, out var list))
        {
            list = new List<CodeItem>();
            _byGroup[codeGbn] = list;
        }
        list.Add(new CodeItem { Code = codeCd, Name = codeNm, SortNo = 0 });
        return Task.CompletedTask;
    }

    public Task DeleteAsync(string codeGbn, string codeCd)
    {
        DeletedCodes.Add((codeGbn, codeCd));
        if (_byGroup.TryGetValue(codeGbn, out var list))
        {
            list.RemoveAll(c => c.Code == codeCd);
        }
        return Task.CompletedTask;
    }
}
```

- [ ] **Step 5: 기존 테스트 컴파일 확인 (Fake 시그니처 변경 영향 점검)**

Run: `dotnet build tests/FishingMartPos.Tests/FishingMartPos.Tests.csproj`
Expected: 빌드 성공 (Fake 생성자 시그니처는 변경하지 않았으므로 기존 호출부는 그대로 컴파일되어야 함)

- [ ] **Step 6: 리포지토리 통합 테스트 작성 (실DB, HeldOrderRepositoryTests 패턴)**

`tests/FishingMartPos.Tests/Repositories/ProductRepositorySaveTests.cs`:
```csharp
using FishingMartPos.Configuration;
using FishingMartPos.Data;
using FishingMartPos.Models;
using FishingMartPos.Repositories;
using Xunit;

namespace FishingMartPos.Tests.Repositories;

public class ProductRepositorySaveTests
{
    private static IProductRepository CreateRepository()
    {
        var config = AppConfig.Load(AppContext.BaseDirectory);
        var factory = new MySqlConnectionFactory(config);
        return new ProductRepository(factory);
    }

    [Fact]
    public async Task SaveAsync_InsertsNewProduct_ThenUpdatesOnSecondSave()
    {
        var repository = CreateRepository();
        const string barcode = "TEST_SAVE_0001";
        try
        {
            await repository.SaveAsync(new Product
            {
                Barcode = barcode, MajorCd = "FISH", MinorCd = "BAIT", PosCatCd = "BAIT",
                Name = "테스트상품", Price = 1000, StockQty = 5, PhotoPath = null,
            });

            var afterInsert = await repository.GetActiveAsync();
            var inserted = Assert.Single(afterInsert.Where(p => p.Barcode == barcode));
            Assert.Equal("테스트상품", inserted.Name);
            Assert.Null(inserted.PhotoPath);

            await repository.SaveAsync(new Product
            {
                Barcode = barcode, MajorCd = "FISH", MinorCd = "BAIT", PosCatCd = "BAIT",
                Name = "테스트상품(수정)", Price = 2000, StockQty = 9, PhotoPath = "ProductPhotos/TEST_SAVE_0001.jpg",
            });

            var afterUpdate = await repository.GetActiveAsync();
            var updated = Assert.Single(afterUpdate.Where(p => p.Barcode == barcode));
            Assert.Equal("테스트상품(수정)", updated.Name);
            Assert.Equal(2000, updated.Price);
            Assert.Equal("ProductPhotos/TEST_SAVE_0001.jpg", updated.PhotoPath);
        }
        finally
        {
            await repository.DeactivateAsync(barcode);
        }
    }
}
```

- [ ] **Step 7: 코드 리포지토리 통합 테스트 작성**

`tests/FishingMartPos.Tests/Repositories/CodeRepositoryTests.cs`:
```csharp
using FishingMartPos.Configuration;
using FishingMartPos.Data;
using FishingMartPos.Repositories;
using Xunit;

namespace FishingMartPos.Tests.Repositories;

public class CodeRepositoryTests
{
    private static ICodeRepository CreateRepository()
    {
        var config = AppConfig.Load(AppContext.BaseDirectory);
        var factory = new MySqlConnectionFactory(config);
        return new CodeRepository(factory);
    }

    [Fact]
    public async Task AddAsync_ThenGetByGroup_ThenDeleteAsync_RoundTrips()
    {
        var repository = CreateRepository();
        const string codeGbn = "MAJOR";
        const string codeCd = "TEST_CD";

        await repository.AddAsync(codeGbn, codeCd, "테스트분류");
        var afterAdd = await repository.GetByGroupAsync(codeGbn);
        Assert.Contains(afterAdd, c => c.Code == codeCd && c.Name == "테스트분류");

        await repository.DeleteAsync(codeGbn, codeCd);
        var afterDelete = await repository.GetByGroupAsync(codeGbn);
        Assert.DoesNotContain(afterDelete, c => c.Code == codeCd);
    }
}
```

- [ ] **Step 8: 전체 테스트 실행**

Run: `dotnet test tests/FishingMartPos.Tests/FishingMartPos.Tests.csproj`
Expected: 모두 통과 (신규 3개 포함)

- [ ] **Step 9: 커밋**

```bash
git add src/FishingMartPos/Models/Product.cs src/FishingMartPos/Repositories tests/FishingMartPos.Tests/Fakes tests/FishingMartPos.Tests/Repositories
git commit -m "상품 저장(upsert)/코드 추가삭제 리포지토리 메서드 추가, Product에 PhotoPath 추가"
```

---

### Task 2: 바코드 자동생성 헬퍼

**Files:**
- Create: `src/FishingMartPos/Domain/BarcodeGenerator.cs`
- Test: `tests/FishingMartPos.Tests/Domain/BarcodeGeneratorTests.cs`

**Interfaces:**
- Produces: `BarcodeGenerator.GenerateNext(IEnumerable<string> existingBarcodes)` → `string` (순수 함수, DB 접근 없음)

- [ ] **Step 1: 실패하는 테스트 작성**

`tests/FishingMartPos.Tests/Domain/BarcodeGeneratorTests.cs`:
```csharp
using FishingMartPos.Domain;
using Xunit;

namespace FishingMartPos.Tests.Domain;

public class BarcodeGeneratorTests
{
    [Fact]
    public void GenerateNext_WithNoExistingBarcodes_StartsAtOne()
    {
        var result = BarcodeGenerator.GenerateNext(Array.Empty<string>());

        Assert.Equal("8800000020001", result);
    }

    [Fact]
    public void GenerateNext_WithExistingBarcodes_ReturnsMaxPlusOne()
    {
        var existing = new[] { "8800000020001", "8800000020050", "8800000020003" };

        var result = BarcodeGenerator.GenerateNext(existing);

        Assert.Equal("8800000020051", result);
    }

    [Fact]
    public void GenerateNext_IgnoresBarcodesWithDifferentPrefix()
    {
        var existing = new[] { "8800000020099", "9999999999999" };

        var result = BarcodeGenerator.GenerateNext(existing);

        Assert.Equal("8800000020100", result);
    }
}
```

- [ ] **Step 2: 테스트 실패 확인**

Run: `dotnet test tests/FishingMartPos.Tests/FishingMartPos.Tests.csproj --filter "FullyQualifiedName~BarcodeGenerator"`
Expected: FAIL (컴파일 오류 — `BarcodeGenerator` 타입 없음)

- [ ] **Step 3: 구현**

`src/FishingMartPos/Domain/BarcodeGenerator.cs`:
```csharp
namespace FishingMartPos.Domain;

public static class BarcodeGenerator
{
    private const string Prefix = "880000002";

    public static string GenerateNext(IEnumerable<string> existingBarcodes)
    {
        int maxSequence = 0;
        foreach (var barcode in existingBarcodes)
        {
            if (barcode.Length != 13 || !barcode.StartsWith(Prefix, StringComparison.Ordinal)) continue;
            if (int.TryParse(barcode.AsSpan(Prefix.Length), out int sequence) && sequence > maxSequence)
            {
                maxSequence = sequence;
            }
        }

        return Prefix + (maxSequence + 1).ToString("D4");
    }
}
```

- [ ] **Step 4: 테스트 통과 확인**

Run: `dotnet test tests/FishingMartPos.Tests/FishingMartPos.Tests.csproj --filter "FullyQualifiedName~BarcodeGenerator"`
Expected: PASS (3개 모두)

- [ ] **Step 5: 커밋**

```bash
git add src/FishingMartPos/Domain/BarcodeGenerator.cs tests/FishingMartPos.Tests/Domain/BarcodeGeneratorTests.cs
git commit -m "바코드 자동생성 헬퍼(BarcodeGenerator) 추가"
```

---

### Task 3: 사진 선택/저장 인터페이스 (IPhotoPicker, IProductPhotoStorage)

**Files:**
- Create: `src/FishingMartPos/Services/IPhotoPicker.cs`
- Create: `src/FishingMartPos/Services/WpfPhotoPicker.cs`
- Create: `src/FishingMartPos/Services/IProductPhotoStorage.cs`
- Create: `src/FishingMartPos/Services/FileSystemProductPhotoStorage.cs`
- Create: `tests/FishingMartPos.Tests/Fakes/FakePhotoPicker.cs`
- Create: `tests/FishingMartPos.Tests/Fakes/FakeProductPhotoStorage.cs`
- Test: `tests/FishingMartPos.Tests/Services/FileSystemProductPhotoStorageTests.cs`

**Interfaces:**
- Produces: `IPhotoPicker.PickPhoto()` → `string?` (선택한 원본 파일의 절대경로, 취소 시 null)
- Produces: `IProductPhotoStorage.SavePhoto(string barcode, string sourceFilePath)` → `string` (저장된 상대경로, 예: `ProductPhotos/8800000020051.jpg`)

- [ ] **Step 1: IPhotoPicker/WpfPhotoPicker**

`src/FishingMartPos/Services/IPhotoPicker.cs`:
```csharp
namespace FishingMartPos.Services;

public interface IPhotoPicker
{
    string? PickPhoto();
}
```

`src/FishingMartPos/Services/WpfPhotoPicker.cs`:
```csharp
using Microsoft.Win32;

namespace FishingMartPos.Services;

public sealed class WpfPhotoPicker : IPhotoPicker
{
    public string? PickPhoto()
    {
        var dialog = new OpenFileDialog
        {
            Filter = "이미지 파일 (*.jpg;*.jpeg;*.png)|*.jpg;*.jpeg;*.png",
        };
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }
}
```

- [ ] **Step 2: IProductPhotoStorage 실패 테스트 작성**

`tests/FishingMartPos.Tests/Services/FileSystemProductPhotoStorageTests.cs`:
```csharp
using FishingMartPos.Services;
using Xunit;

namespace FishingMartPos.Tests.Services;

public class FileSystemProductPhotoStorageTests
{
    [Fact]
    public void SavePhoto_CopiesFileIntoProductPhotosFolder_ReturnsRelativePath()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "FishingMartPosTests_" + Guid.NewGuid());
        Directory.CreateDirectory(tempRoot);
        try
        {
            var sourceFile = Path.Combine(tempRoot, "source.jpg");
            File.WriteAllText(sourceFile, "fake image bytes");

            var storage = new FileSystemProductPhotoStorage(tempRoot);
            var relativePath = storage.SavePhoto("8800000020051", sourceFile);

            Assert.Equal(Path.Combine("ProductPhotos", "8800000020051.jpg"), relativePath);
            Assert.True(File.Exists(Path.Combine(tempRoot, relativePath)));
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }
}
```

- [ ] **Step 3: 테스트 실패 확인**

Run: `dotnet test tests/FishingMartPos.Tests/FishingMartPos.Tests.csproj --filter "FullyQualifiedName~FileSystemProductPhotoStorage"`
Expected: FAIL (타입 없음)

- [ ] **Step 4: 구현**

`src/FishingMartPos/Services/IProductPhotoStorage.cs`:
```csharp
namespace FishingMartPos.Services;

public interface IProductPhotoStorage
{
    string SavePhoto(string barcode, string sourceFilePath);
}
```

`src/FishingMartPos/Services/FileSystemProductPhotoStorage.cs`:
```csharp
namespace FishingMartPos.Services;

public sealed class FileSystemProductPhotoStorage : IProductPhotoStorage
{
    private readonly string _baseDirectory;

    public FileSystemProductPhotoStorage(string baseDirectory)
    {
        _baseDirectory = baseDirectory;
    }

    public string SavePhoto(string barcode, string sourceFilePath)
    {
        var extension = Path.GetExtension(sourceFilePath);
        var relativePath = Path.Combine("ProductPhotos", barcode + extension);
        var destinationPath = Path.Combine(_baseDirectory, relativePath);

        Directory.CreateDirectory(Path.Combine(_baseDirectory, "ProductPhotos"));
        File.Copy(sourceFilePath, destinationPath, overwrite: true);

        return relativePath;
    }
}
```

- [ ] **Step 5: 테스트 통과 확인**

Run: `dotnet test tests/FishingMartPos.Tests/FishingMartPos.Tests.csproj --filter "FullyQualifiedName~FileSystemProductPhotoStorage"`
Expected: PASS

- [ ] **Step 6: 테스트용 Fake 작성**

`tests/FishingMartPos.Tests/Fakes/FakePhotoPicker.cs`:
```csharp
using FishingMartPos.Services;

namespace FishingMartPos.Tests.Fakes;

public sealed class FakePhotoPicker : IPhotoPicker
{
    public string? NextPickedPath { get; set; }

    public string? PickPhoto() => NextPickedPath;
}
```

`tests/FishingMartPos.Tests/Fakes/FakeProductPhotoStorage.cs`:
```csharp
using FishingMartPos.Services;

namespace FishingMartPos.Tests.Fakes;

public sealed class FakeProductPhotoStorage : IProductPhotoStorage
{
    public List<(string Barcode, string SourceFilePath)> SaveCalls { get; } = new();

    public string SavePhoto(string barcode, string sourceFilePath)
    {
        SaveCalls.Add((barcode, sourceFilePath));
        return $"ProductPhotos/{barcode}.jpg";
    }
}
```

- [ ] **Step 7: 전체 테스트 실행 후 커밋**

Run: `dotnet test tests/FishingMartPos.Tests/FishingMartPos.Tests.csproj`
Expected: 모두 통과

```bash
git add src/FishingMartPos/Services tests/FishingMartPos.Tests/Fakes tests/FishingMartPos.Tests/Services
git commit -m "상품 사진 선택(IPhotoPicker)/저장(IProductPhotoStorage) 인터페이스와 구현 추가"
```

---

### Task 4: InventoryFormViewModel — 등록/수정 핵심 로직

**Files:**
- Create: `src/FishingMartPos/ViewModels/InventoryFormViewModel.cs`
- Test: `tests/FishingMartPos.Tests/ViewModels/InventoryFormViewModelTests.cs`

**Interfaces:**
- Consumes: `IProductRepository`(Task 1), `ICodeRepository`(Task 1), `IPhotoPicker`/`IProductPhotoStorage`(Task 3), `BarcodeGenerator`(Task 2), `INavigationService`, `InventoryViewModel`(복귀 대상), `MainMenuViewModel`은 불필요(재고관리를 거쳐서만 진입)
- Produces: `InventoryFormViewModel` 생성자 `(IProductRepository, ICodeRepository, IPhotoPicker, IProductPhotoStorage, INavigationService, InventoryViewModel returnTo, Product? editingProduct)`, `LoadAsync()`, 필드 프로퍼티(`MajorCd`,`MinorCd`,`PosCatCd`,`Name`,`BarcodeInput`,`PriceInput`,`StockInput`,`ErrorMessage`), `SaveCommand`, `PickPhotoCommand`, `CancelCommand`(재고관리로 복귀), `bool IsEditMode`

- [ ] **Step 1: 실패하는 테스트들 작성 (등록 모드)**

`tests/FishingMartPos.Tests/ViewModels/InventoryFormViewModelTests.cs`:
```csharp
using FishingMartPos.Models;
using FishingMartPos.Navigation;
using FishingMartPos.Services;
using FishingMartPos.Tests.Fakes;
using FishingMartPos.ViewModels;
using Xunit;

namespace FishingMartPos.Tests.ViewModels;

public class InventoryFormViewModelTests
{
    private static Dictionary<string, IReadOnlyList<CodeItem>> SampleCodes() => new()
    {
        ["MAJOR"] = new List<CodeItem> { new() { Code = "FISH", Name = "낚시용품", SortNo = 1 } },
        ["MINOR"] = new List<CodeItem> { new() { Code = "BAIT", Name = "미끼", SortNo = 1 } },
        ["POSCAT"] = new List<CodeItem> { new() { Code = "BAIT", Name = "미끼", SortNo = 1 } },
    };

    private static (InventoryFormViewModel vm, FakeProductRepository products, FakeCodeRepository codes, FakeProductPhotoStorage photoStorage, FakePhotoPicker photoPicker, INavigationService navigation, InventoryViewModel inventoryVm)
        CreateForAdd()
    {
        var products = new FakeProductRepository(new List<Product>
        {
            new() { Barcode = "8800000020001", MajorCd = "FISH", MinorCd = "BAIT", PosCatCd = "BAIT", Name = "지렁이", Price = 5000, StockQty = 10 },
        });
        var codes = new FakeCodeRepository(SampleCodes());
        var photoStorage = new FakeProductPhotoStorage();
        var photoPicker = new FakePhotoPicker();
        var session = new FishingMartPos.Services.CurrentSession();
        session.SignIn(
            new Staff { StaffCode = "ADMIN1", StaffName = "관리자", Role = "ADMIN", UseYn = "Y" },
            new PosTerminal { PosCode = "1", PosName = "POS1" });
        var navigation = new NavigationService();
        var mainMenu = new MainMenuViewModel(session, navigation);
        var inventoryVm = new InventoryViewModel(products, codes, session, navigation, mainMenu);

        var vm = new InventoryFormViewModel(products, codes, photoPicker, photoStorage, navigation, inventoryVm, editingProduct: null);
        return (vm, products, codes, photoStorage, photoPicker, navigation, inventoryVm);
    }

    [Fact]
    public async Task LoadAsync_AddMode_StartsWithEmptyForm()
    {
        var (vm, _, _, _, _, _, _) = CreateForAdd();

        await vm.LoadAsync();

        Assert.False(vm.IsEditMode);
        Assert.Equal(string.Empty, vm.Name);
        Assert.Equal(string.Empty, vm.BarcodeInput);
    }

    [Fact]
    public async Task LoadAsync_EditMode_FillsFormFromExistingProduct()
    {
        var products = new FakeProductRepository(new List<Product>());
        var codes = new FakeCodeRepository(SampleCodes());
        var photoStorage = new FakeProductPhotoStorage();
        var photoPicker = new FakePhotoPicker();
        var session = new FishingMartPos.Services.CurrentSession();
        session.SignIn(new Staff { StaffCode = "ADMIN1", StaffName = "관리자", Role = "ADMIN", UseYn = "Y" }, new PosTerminal { PosCode = "1", PosName = "POS1" });
        var navigation = new NavigationService();
        var mainMenu = new MainMenuViewModel(session, navigation);
        var inventoryVm = new InventoryViewModel(products, codes, session, navigation, mainMenu);
        var editing = new Product { Barcode = "8800000020001", MajorCd = "FISH", MinorCd = "BAIT", PosCatCd = "BAIT", Name = "지렁이", Price = 5000, StockQty = 10 };

        var vm = new InventoryFormViewModel(products, codes, photoPicker, photoStorage, navigation, inventoryVm, editingProduct: editing);
        await vm.LoadAsync();

        Assert.True(vm.IsEditMode);
        Assert.Equal("지렁이", vm.Name);
        Assert.Equal("8800000020001", vm.BarcodeInput);
        Assert.Equal("5000", vm.PriceInput);
        Assert.Equal("10", vm.StockInput);
    }

    [Fact]
    public async Task Save_WithoutName_ShowsErrorAndDoesNotSave()
    {
        var (vm, products, _, _, _, _, _) = CreateForAdd();
        await vm.LoadAsync();
        vm.PriceInput = "1000";

        await vm.SaveCommand.ExecuteAsync(null);

        Assert.NotNull(vm.ErrorMessage);
        Assert.Empty(products.SavedProducts);
    }

    [Fact]
    public async Task Save_WithoutPrice_ShowsErrorAndDoesNotSave()
    {
        var (vm, products, _, _, _, _, _) = CreateForAdd();
        await vm.LoadAsync();
        vm.Name = "새우";

        await vm.SaveCommand.ExecuteAsync(null);

        Assert.NotNull(vm.ErrorMessage);
        Assert.Empty(products.SavedProducts);
    }

    [Fact]
    public async Task Save_WithBlankBarcode_GeneratesNextBarcode()
    {
        var (vm, products, _, _, _, _, _) = CreateForAdd();
        await vm.LoadAsync();
        vm.Name = "새우";
        vm.PriceInput = "3000";
        vm.MajorCd = "FISH";
        vm.MinorCd = "BAIT";
        vm.PosCatCd = "BAIT";

        await vm.SaveCommand.ExecuteAsync(null);

        var saved = Assert.Single(products.SavedProducts);
        Assert.Equal("8800000020002", saved.Barcode);
    }

    [Fact]
    public async Task Save_WithDuplicateManualBarcode_ShowsErrorAndDoesNotSave()
    {
        var (vm, products, _, _, _, _, _) = CreateForAdd();
        await vm.LoadAsync();
        vm.Name = "새우";
        vm.PriceInput = "3000";
        vm.BarcodeInput = "8800000020001"; // 이미 존재

        await vm.SaveCommand.ExecuteAsync(null);

        Assert.NotNull(vm.ErrorMessage);
        Assert.Empty(products.SavedProducts);
    }

    [Fact]
    public async Task Save_WithBlankStock_DefaultsToZero()
    {
        var (vm, products, _, _, _, _, _) = CreateForAdd();
        await vm.LoadAsync();
        vm.Name = "새우";
        vm.PriceInput = "3000";

        await vm.SaveCommand.ExecuteAsync(null);

        var saved = Assert.Single(products.SavedProducts);
        Assert.Equal(0, saved.StockQty);
    }

    [Fact]
    public async Task Save_NavigatesBackToInventoryAndRefreshesRows()
    {
        var (vm, _, _, _, _, navigation, inventoryVm) = CreateForAdd();
        await vm.LoadAsync();
        vm.Name = "새우";
        vm.PriceInput = "3000";

        await vm.SaveCommand.ExecuteAsync(null);

        Assert.Same(inventoryVm, navigation.CurrentViewModel);
    }

    [Fact]
    public async Task PickPhoto_ThenSave_StoresPhotoAndSetsPhotoPath()
    {
        var (vm, products, _, photoStorage, photoPicker, _, _) = CreateForAdd();
        await vm.LoadAsync();
        vm.Name = "새우";
        vm.PriceInput = "3000";
        photoPicker.NextPickedPath = @"C:\temp\shrimp.jpg";

        vm.PickPhotoCommand.Execute(null);
        await vm.SaveCommand.ExecuteAsync(null);

        var saved = Assert.Single(products.SavedProducts);
        Assert.Single(photoStorage.SaveCalls);
        Assert.Equal(saved.Barcode, photoStorage.SaveCalls[0].Barcode);
        Assert.Equal($"ProductPhotos/{saved.Barcode}.jpg", saved.PhotoPath);
    }
}
```

- [ ] **Step 2: 테스트 실패 확인**

Run: `dotnet test tests/FishingMartPos.Tests/FishingMartPos.Tests.csproj --filter "FullyQualifiedName~InventoryFormViewModelTests"`
Expected: FAIL (컴파일 오류 — `InventoryFormViewModel` 없음)

- [ ] **Step 3: InventoryFormViewModel 구현**

`src/FishingMartPos/ViewModels/InventoryFormViewModel.cs`:
```csharp
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FishingMartPos.Domain;
using FishingMartPos.Models;
using FishingMartPos.Navigation;
using FishingMartPos.Repositories;
using FishingMartPos.Services;

namespace FishingMartPos.ViewModels;

public sealed partial class InventoryFormViewModel : ObservableObject
{
    private readonly IProductRepository _productRepository;
    private readonly ICodeRepository _codeRepository;
    private readonly IPhotoPicker _photoPicker;
    private readonly IProductPhotoStorage _photoStorage;
    private readonly INavigationService _navigation;
    private readonly InventoryViewModel _returnTo;
    private readonly string? _editingBarcode;

    private IReadOnlyList<Product> _allProducts = Array.Empty<Product>();
    private string? _pickedPhotoFilePath;

    [ObservableProperty] private string _majorCd = string.Empty;
    [ObservableProperty] private string _minorCd = string.Empty;
    [ObservableProperty] private string _posCatCd = string.Empty;
    [ObservableProperty] private string _name = string.Empty;
    [ObservableProperty] private string _barcodeInput = string.Empty;
    [ObservableProperty] private string _priceInput = string.Empty;
    [ObservableProperty] private string _stockInput = string.Empty;
    [ObservableProperty] private string? _errorMessage;
    [ObservableProperty] private bool _isMajorCodeManagerOpen;
    [ObservableProperty] private bool _isMinorCodeManagerOpen;
    [ObservableProperty] private bool _isPosCatCodeManagerOpen;
    [ObservableProperty] private string _newCodeCode = string.Empty;
    [ObservableProperty] private string _newCodeName = string.Empty;

    public ObservableCollection<CodeItem> MajorCodes { get; } = new();
    public ObservableCollection<CodeItem> MinorCodes { get; } = new();
    public ObservableCollection<CodeItem> PosCatCodes { get; } = new();

    public bool IsEditMode => _editingBarcode is not null;
    public string HeaderText => IsEditMode ? "상품수정" : "상품등록";
    public string SaveButtonText => IsEditMode ? "저장하기" : "등록하기";

    public InventoryFormViewModel(
        IProductRepository productRepository,
        ICodeRepository codeRepository,
        IPhotoPicker photoPicker,
        IProductPhotoStorage photoStorage,
        INavigationService navigation,
        InventoryViewModel returnTo,
        Product? editingProduct)
    {
        _productRepository = productRepository;
        _codeRepository = codeRepository;
        _photoPicker = photoPicker;
        _photoStorage = photoStorage;
        _navigation = navigation;
        _returnTo = returnTo;
        _editingBarcode = editingProduct?.Barcode;

        if (editingProduct is not null)
        {
            MajorCd = editingProduct.MajorCd;
            MinorCd = editingProduct.MinorCd;
            PosCatCd = editingProduct.PosCatCd;
            Name = editingProduct.Name;
            BarcodeInput = editingProduct.Barcode;
            PriceInput = editingProduct.Price.ToString("0.####");
            StockInput = editingProduct.StockQty.ToString();
        }
    }

    public async Task LoadAsync()
    {
        _allProducts = await _productRepository.GetActiveAsync();

        async Task FillAsync(string codeGbn, ObservableCollection<CodeItem> target)
        {
            target.Clear();
            foreach (var code in await _codeRepository.GetByGroupAsync(codeGbn))
            {
                target.Add(code);
            }
        }

        await FillAsync("MAJOR", MajorCodes);
        await FillAsync("MINOR", MinorCodes);
        await FillAsync("POSCAT", PosCatCodes);
    }

    [RelayCommand]
    private void PickPhoto()
    {
        var path = _photoPicker.PickPhoto();
        if (path is not null)
        {
            _pickedPhotoFilePath = path;
        }
    }

    [RelayCommand]
    private async Task Save()
    {
        ErrorMessage = null;

        if (string.IsNullOrWhiteSpace(Name))
        {
            ErrorMessage = "상품명을 입력해주세요";
            return;
        }

        if (!decimal.TryParse(PriceInput, out decimal price) || price <= 0)
        {
            ErrorMessage = "단가를 올바르게 입력해주세요";
            return;
        }

        int stock = int.TryParse(StockInput, out int parsedStock) ? parsedStock : 0;

        string barcode;
        if (string.IsNullOrWhiteSpace(BarcodeInput))
        {
            barcode = BarcodeGenerator.GenerateNext(_allProducts.Select(p => p.Barcode));
        }
        else if (!IsEditMode && _allProducts.Any(p => p.Barcode == BarcodeInput))
        {
            ErrorMessage = "이미 등록된 바코드입니다";
            return;
        }
        else
        {
            barcode = BarcodeInput;
        }

        string? photoPath = _editingBarcode is not null
            ? _allProducts.FirstOrDefault(p => p.Barcode == _editingBarcode)?.PhotoPath
            : null;
        if (_pickedPhotoFilePath is not null)
        {
            photoPath = _photoStorage.SavePhoto(barcode, _pickedPhotoFilePath);
        }

        var product = new Product
        {
            Barcode = barcode,
            MajorCd = MajorCd,
            MinorCd = MinorCd,
            PosCatCd = PosCatCd,
            Name = Name,
            Price = price,
            StockQty = stock,
            PhotoPath = photoPath,
        };

        await _productRepository.SaveAsync(product);
        await GoBackToInventoryAsync();
    }

    [RelayCommand]
    private async Task Cancel() => await GoBackToInventoryAsync();

    private async Task GoBackToInventoryAsync()
    {
        await _returnTo.LoadAsync();
        _navigation.NavigateTo(_returnTo);
    }

    [RelayCommand]
    private void ToggleMajorCodeManager() => IsMajorCodeManagerOpen = !IsMajorCodeManagerOpen;

    [RelayCommand]
    private void ToggleMinorCodeManager() => IsMinorCodeManagerOpen = !IsMinorCodeManagerOpen;

    [RelayCommand]
    private void TogglePosCatCodeManager() => IsPosCatCodeManagerOpen = !IsPosCatCodeManagerOpen;
}
```

- [ ] **Step 4: 테스트 통과 확인**

Run: `dotnet test tests/FishingMartPos.Tests/FishingMartPos.Tests.csproj --filter "FullyQualifiedName~InventoryFormViewModelTests"`
Expected: PASS (9개 모두)

주의: `InventoryViewModel`이 `NavigateTo` 호출 없이 `LoadAsync()`만으로 재사용 가능한지 확인 — 현재 구현상 `LoadAsync`는 DB에서 다시 읽어오고 `RefreshRows`를 호출하지 않으므로, `GoBackToInventoryAsync`에서 `LoadAsync` 후 `RefreshRows`도 호출되도록 `InventoryViewModel`에 `public`으로 노출된 새로고침 경로가 필요하다. `InventoryViewModel.LoadAsync()`가 내부적으로 `SelectedCategoryOption`을 재설정하면서 `OnSelectedCategoryOptionChanged` → `RefreshRows()`가 이미 트리거되므로 별도 변경 없이 동작해야 한다 — 실제로 테스트가 실패하면 `InventoryViewModel.LoadAsync` 흐름을 확인해 필요한 만큼만 고친다.

- [ ] **Step 5: 커밋**

```bash
git add src/FishingMartPos/ViewModels/InventoryFormViewModel.cs tests/FishingMartPos.Tests/ViewModels/InventoryFormViewModelTests.cs
git commit -m "InventoryFormViewModel 추가 — 상품 등록/수정 핵심 로직(검증, 바코드 자동생성, 사진 저장)"
```

---

### Task 5: InventoryFormViewModel — 코드관리(대/소/POS분류 인라인 추가·삭제)

**Files:**
- Modify: `src/FishingMartPos/ViewModels/InventoryFormViewModel.cs`
- Modify: `tests/FishingMartPos.Tests/ViewModels/InventoryFormViewModelTests.cs`

**Interfaces:**
- Produces: `AddMajorCodeCommand`/`AddMinorCodeCommand`/`AddPosCatCodeCommand`, `DeleteMajorCodeCommand(CodeItem)`/... (또는 `CodeItem`에 커맨드를 심어 목록 항목별로 바인딩 — 기존 `InventoryRowViewModel.DeleteCommand` 패턴과 동일하게 목록용 별도 뷰모델 `CodeManagerItemViewModel` 사용)

- [ ] **Step 1: 실패하는 테스트 작성**

`InventoryFormViewModelTests.cs`에 추가:
```csharp
    [Fact]
    public async Task AddMajorCode_AddsToMajorCodesAndRepository()
    {
        var (vm, _, codes, _, _, _, _) = CreateForAdd();
        await vm.LoadAsync();
        vm.NewCodeCode = "TACKLE";
        vm.NewCodeName = "채비";

        await vm.AddMajorCodeCommand.ExecuteAsync(null);

        Assert.Contains(vm.MajorCodes, c => c.Code == "TACKLE" && c.Name == "채비");
        Assert.Contains((await codes.GetByGroupAsync("MAJOR")), c => c.Code == "TACKLE");
    }

    [Fact]
    public async Task DeleteMajorCode_WhenUnused_RemovesFromListAndRepository()
    {
        var (vm, _, codes, _, _, _, _) = CreateForAdd();
        await vm.LoadAsync();
        var unusedCode = new CodeItem { Code = "UNUSED", Name = "미사용", SortNo = 9 };
        vm.MajorCodes.Add(unusedCode);

        await vm.DeleteMajorCodeCommand.ExecuteAsync(unusedCode);

        Assert.DoesNotContain(vm.MajorCodes, c => c.Code == "UNUSED");
    }

    [Fact]
    public async Task DeleteMajorCode_WhenUsedByExistingProduct_ShowsErrorAndKeepsCode()
    {
        var (vm, _, _, _, _, _, _) = CreateForAdd(); // 샘플 상품이 MajorCd="FISH" 사용 중
        await vm.LoadAsync();
        var usedCode = vm.MajorCodes.Single(c => c.Code == "FISH");

        await vm.DeleteMajorCodeCommand.ExecuteAsync(usedCode);

        Assert.NotNull(vm.ErrorMessage);
        Assert.Contains(vm.MajorCodes, c => c.Code == "FISH");
    }
```

- [ ] **Step 2: 테스트 실패 확인**

Run: `dotnet test tests/FishingMartPos.Tests/FishingMartPos.Tests.csproj --filter "FullyQualifiedName~InventoryFormViewModelTests"`
Expected: FAIL (`AddMajorCodeCommand`/`DeleteMajorCodeCommand` 없음)

- [ ] **Step 3: 구현 — MAJOR만 우선 (MINOR/POSCAT은 동일 패턴 반복)**

`InventoryFormViewModel.cs`에 추가:
```csharp
    [RelayCommand]
    private async Task AddMajorCode() => await AddCodeAsync("MAJOR", MajorCodes);

    [RelayCommand]
    private async Task AddMinorCode() => await AddCodeAsync("MINOR", MinorCodes);

    [RelayCommand]
    private async Task AddPosCatCode() => await AddCodeAsync("POSCAT", PosCatCodes);

    private async Task AddCodeAsync(string codeGbn, ObservableCollection<CodeItem> target)
    {
        if (string.IsNullOrWhiteSpace(NewCodeCode) || string.IsNullOrWhiteSpace(NewCodeName))
        {
            ErrorMessage = "코드와 이름을 모두 입력해주세요";
            return;
        }

        await _codeRepository.AddAsync(codeGbn, NewCodeCode, NewCodeName);
        target.Add(new CodeItem { Code = NewCodeCode, Name = NewCodeName, SortNo = 0 });
        NewCodeCode = string.Empty;
        NewCodeName = string.Empty;
        ErrorMessage = null;
    }

    [RelayCommand]
    private async Task DeleteMajorCode(CodeItem code) => await DeleteCodeAsync("MAJOR", MajorCodes, code, p => p.MajorCd);

    [RelayCommand]
    private async Task DeleteMinorCode(CodeItem code) => await DeleteCodeAsync("MINOR", MinorCodes, code, p => p.MinorCd);

    [RelayCommand]
    private async Task DeletePosCatCode(CodeItem code) => await DeleteCodeAsync("POSCAT", PosCatCodes, code, p => p.PosCatCd);

    private async Task DeleteCodeAsync(string codeGbn, ObservableCollection<CodeItem> target, CodeItem code, Func<Product, string> codeSelector)
    {
        if (_allProducts.Any(p => codeSelector(p) == code.Code))
        {
            ErrorMessage = "사용 중인 코드는 삭제할 수 없습니다";
            return;
        }

        await _codeRepository.DeleteAsync(codeGbn, code.Code);
        target.Remove(code);
        ErrorMessage = null;
    }
```

- [ ] **Step 4: 테스트 통과 확인**

Run: `dotnet test tests/FishingMartPos.Tests/FishingMartPos.Tests.csproj --filter "FullyQualifiedName~InventoryFormViewModelTests"`
Expected: PASS (12개 모두)

- [ ] **Step 5: 커밋**

```bash
git add src/FishingMartPos/ViewModels/InventoryFormViewModel.cs tests/FishingMartPos.Tests/ViewModels/InventoryFormViewModelTests.cs
git commit -m "InventoryFormViewModel에 대/소/POS분류 인라인 코드 추가·삭제 기능 추가"
```

---

### Task 6: InventoryViewModel 연결 — "+ 상품등록" / 행별 "수정" 버튼

**Files:**
- Modify: `src/FishingMartPos/ViewModels/InventoryViewModel.cs`
- Modify: `src/FishingMartPos/ViewModels/InventoryRowViewModel.cs`
- Modify: `tests/FishingMartPos.Tests/ViewModels/InventoryViewModelTests.cs`

**Interfaces:**
- Produces: `InventoryViewModel.InventoryFormViewModelFactory` (`Func<InventoryViewModel, Product?, Task<InventoryFormViewModel>>`, App.xaml.cs/테스트에서 주입), `GoToAddProductCommand`, `InventoryRowViewModel.EditCommand`

- [ ] **Step 1: 실패하는 테스트 작성**

`InventoryViewModelTests.cs`에 추가 (파일 상단 `CreateAdmin`/`CreateStaff` 헬퍼는 그대로 두고 팩토리만 주입):
```csharp
    [Fact]
    public async Task GoToAddProduct_InvokesFactoryWithNullProductAndNavigates()
    {
        var (vm, _, navigation, _) = CreateAdmin();
        await vm.LoadAsync();
        Product? capturedProduct = "sentinel" as Product; // 초기값은 null이 아니어야 검증 의미가 있음 — 아래에서 실제로 null 전달되는지 확인
        InventoryFormViewModel? formVm = null;
        vm.InventoryFormViewModelFactory = (inv, product) =>
        {
            capturedProduct = product;
            formVm = new InventoryFormViewModel(
                new FakeProductRepository(Array.Empty<Product>()),
                new FakeCodeRepository(new Dictionary<string, IReadOnlyList<CodeItem>>()),
                new FakePhotoPicker(), new FakeProductPhotoStorage(), navigation, inv, product);
            return Task.FromResult(formVm);
        };

        await vm.GoToAddProductCommand.ExecuteAsync(null);

        Assert.Null(capturedProduct);
        Assert.Same(formVm, navigation.CurrentViewModel);
    }

    [Fact]
    public async Task RowEditCommand_InvokesFactoryWithThatProductAndNavigates()
    {
        var (vm, _, navigation, _) = CreateAdmin();
        await vm.LoadAsync();
        Product? capturedProduct = null;
        InventoryFormViewModel? formVm = null;
        vm.InventoryFormViewModelFactory = (inv, product) =>
        {
            capturedProduct = product;
            formVm = new InventoryFormViewModel(
                new FakeProductRepository(Array.Empty<Product>()),
                new FakeCodeRepository(new Dictionary<string, IReadOnlyList<CodeItem>>()),
                new FakePhotoPicker(), new FakeProductPhotoStorage(), navigation, inv, product);
            return Task.FromResult(formVm);
        };
        var row = vm.Rows.Single(r => r.Barcode == "B1");

        row.EditCommand.Execute(null);
        await Task.Delay(1); // AsyncRelayCommand.Execute는 fire-and-forget이므로 fake는 동기 완료됨(FakeDelayProvider 미사용, 실제로 즉시 완료)

        Assert.NotNull(capturedProduct);
        Assert.Equal("B1", capturedProduct!.Barcode);
        Assert.Same(formVm, navigation.CurrentViewModel);
    }
```
(`using FishingMartPos.Tests.Fakes;`와 `using FishingMartPos.Services;` 상단에 필요 시 추가)

- [ ] **Step 2: 테스트 실패 확인**

Run: `dotnet test tests/FishingMartPos.Tests/FishingMartPos.Tests.csproj --filter "FullyQualifiedName~InventoryViewModelTests"`
Expected: FAIL (`InventoryFormViewModelFactory`/`GoToAddProductCommand`/`EditCommand` 없음)

- [ ] **Step 3: InventoryRowViewModel에 EditCommand 추가**

`src/FishingMartPos/ViewModels/InventoryRowViewModel.cs`:
```csharp
using System.Windows.Input;
using System.Windows.Media;

namespace FishingMartPos.ViewModels;

public sealed class InventoryRowViewModel
{
    public required string Barcode { get; init; }
    public required string Name { get; init; }
    public required string MajorName { get; init; }
    public required string MinorName { get; init; }
    public required string PosCatName { get; init; }
    public required string PriceStr { get; init; }
    public required string StockQtyStr { get; init; }
    public required Brush Swatch { get; init; }
    public string? PhotoAbsolutePath { get; init; }
    public required bool CanDelete { get; init; }
    public required ICommand DeleteCommand { get; init; }
    public required ICommand EditCommand { get; init; }
}
```

- [ ] **Step 4: InventoryViewModel에 팩토리/커맨드 추가**

`InventoryViewModel.cs`에 추가 (필드 선언부):
```csharp
    /// <summary>App.xaml.cs(또는 테스트)에서 주입하는 InventoryFormViewModel 팩토리 — "+ 상품등록"/행별 "수정" 진입 시 사용. product가 null이면 등록 모드, 있으면 수정 모드.</summary>
    public Func<InventoryViewModel, Product?, Task<InventoryFormViewModel>>? InventoryFormViewModelFactory { get; init; }
```

커맨드 추가:
```csharp
    [RelayCommand]
    private async Task GoToAddProduct()
    {
        var formVm = await InventoryFormViewModelFactory!.Invoke(this, null);
        await formVm.LoadAsync();
        _navigation.NavigateTo(formVm);
    }

    private async Task GoToEditProduct(Product product)
    {
        var formVm = await InventoryFormViewModelFactory!.Invoke(this, product);
        await formVm.LoadAsync();
        _navigation.NavigateTo(formVm);
    }
```

`RefreshRows()`의 `Rows.Add(...)` 호출에 `EditCommand` 라인 추가:
```csharp
                EditCommand = new AsyncRelayCommand(() => GoToEditProduct(captured)),
```
(`CommunityToolkit.Mvvm.Input.AsyncRelayCommand`는 이미 `using CommunityToolkit.Mvvm.Input;`으로 임포트되어 있음)

- [ ] **Step 5: 테스트 통과 확인**

Run: `dotnet test tests/FishingMartPos.Tests/FishingMartPos.Tests.csproj --filter "FullyQualifiedName~InventoryViewModelTests"`
Expected: PASS

- [ ] **Step 6: 전체 테스트 실행 후 커밋**

Run: `dotnet test tests/FishingMartPos.Tests/FishingMartPos.Tests.csproj`
Expected: 모두 통과

```bash
git add src/FishingMartPos/ViewModels/InventoryViewModel.cs src/FishingMartPos/ViewModels/InventoryRowViewModel.cs tests/FishingMartPos.Tests/ViewModels/InventoryViewModelTests.cs
git commit -m "InventoryViewModel에 상품등록/수정 진입 커맨드(GoToAddProduct, 행별 EditCommand) 연결"
```

---

### Task 7: InventoryFormView.xaml (UI) + App.xaml.cs DI 배선 + 스모크 테스트

**Files:**
- Create: `src/FishingMartPos/Views/InventoryFormView.xaml`
- Create: `src/FishingMartPos/Views/InventoryFormView.xaml.cs`
- Modify: `src/FishingMartPos/App.xaml.cs`
- Modify: `src/FishingMartPos/MainWindow.xaml`
- Test: `tests/FishingMartPos.Tests/Views/InventoryFormViewSmokeTests.cs`

- [ ] **Step 1: MainWindow.xaml에 DataTemplate 매핑 추가**

`MainWindow.xaml`은 `Window.Resources`에 뷰모델 타입 → 뷰 `DataTemplate`을 등록하는 방식이다(코드비하인드 변경 불필요). `<DataTemplate DataType="{x:Type vm:InventoryViewModel}"><views:InventoryView /></DataTemplate>` 바로 아래에 추가:
```xml
        <DataTemplate DataType="{x:Type vm:InventoryFormViewModel}">
            <views:InventoryFormView />
        </DataTemplate>
```

- [ ] **Step 2: InventoryFormView.xaml.cs (코드비하인드, 다른 View들과 동일 패턴)**

`src/FishingMartPos/Views/InventoryFormView.xaml.cs`:
```csharp
using System.Windows.Controls;

namespace FishingMartPos.Views;

public partial class InventoryFormView : UserControl
{
    public InventoryFormView()
    {
        InitializeComponent();
    }
}
```

- [ ] **Step 3: InventoryFormView.xaml**

`src/FishingMartPos/Views/InventoryFormView.xaml` — 기존 `InventoryView.xaml`/`PosView.xaml`의 톤앤매너(Border/Grid 구조, `DynamicResource` 브러시)를 그대로 따른다:
```xml
<UserControl x:Class="FishingMartPos.Views.InventoryFormView"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006"
             xmlns:d="http://schemas.microsoft.com/expression/blend/2008"
             mc:Ignorable="d">
    <UserControl.Resources>
        <BooleanToVisibilityConverter x:Key="BooleanToVisibilityConverter" />
        <local:NullToVisibilityConverter x:Key="NullToVisibilityConverter" xmlns:local="clr-namespace:FishingMartPos.Converters" />
    </UserControl.Resources>
    <Grid Background="{DynamicResource MenuBackground}">
        <Grid.RowDefinitions>
            <RowDefinition Height="Auto" />
            <RowDefinition Height="*" />
        </Grid.RowDefinitions>

        <Border Grid.Row="0" Background="White" BorderBrush="{DynamicResource HeaderBorder}" BorderThickness="0,0,0,1">
            <Grid Margin="16,0" Height="42">
                <TextBlock Text="{Binding HeaderText}" FontSize="14" FontWeight="Bold"
                           Foreground="{DynamicResource TitleText}" VerticalAlignment="Center" HorizontalAlignment="Left" />
                <Button Content="← 재고관리로" Padding="12,5" FontSize="12" FontWeight="Bold"
                        Background="{DynamicResource LogoutButtonBackground}" Foreground="{DynamicResource MutedText}"
                        BorderBrush="{DynamicResource InputBorder}" BorderThickness="1"
                        HorizontalAlignment="Right" VerticalAlignment="Center"
                        Command="{Binding CancelCommand}" />
            </Grid>
        </Border>

        <ScrollViewer Grid.Row="1" VerticalScrollBarVisibility="Auto">
            <Border Margin="24,20" Background="White" BorderBrush="{DynamicResource CardBorder}" BorderThickness="1"
                    Padding="24" MaxWidth="560" HorizontalAlignment="Left">
                <StackPanel>
                    <Border Background="{DynamicResource CardBorder}" BorderBrush="{DynamicResource ErrorBorder}" BorderThickness="1"
                            Padding="12,8" Margin="0,0,0,16"
                            Visibility="{Binding ErrorMessage, Converter={StaticResource NullToVisibilityConverter}}">
                        <TextBlock Text="{Binding ErrorMessage}" Foreground="{DynamicResource ErrorIconBackground}" FontSize="12" FontWeight="Bold" />
                    </Border>

                    <TextBlock Text="상품 사진" FontSize="12" FontWeight="Bold" Foreground="{DynamicResource MutedText}" Margin="0,0,0,6" />
                    <Button Content="사진 선택" HorizontalAlignment="Left" Padding="12,6" Margin="0,0,0,16" Command="{Binding PickPhotoCommand}" />

                    <Grid Margin="0,0,0,14">
                        <Grid.ColumnDefinitions>
                            <ColumnDefinition Width="*" />
                            <ColumnDefinition Width="14" />
                            <ColumnDefinition Width="*" />
                        </Grid.ColumnDefinitions>
                        <StackPanel Grid.Column="0">
                            <Grid Margin="0,0,0,6">
                                <TextBlock Text="대분류" FontSize="12" FontWeight="Bold" Foreground="{DynamicResource MutedText}" />
                                <Button Content="코드관리" HorizontalAlignment="Right" Padding="0" Background="Transparent" BorderThickness="0"
                                        Foreground="{DynamicResource Accent}" FontSize="11" FontWeight="Bold" Command="{Binding ToggleMajorCodeManagerCommand}" />
                            </Grid>
                            <ComboBox ItemsSource="{Binding MajorCodes}" SelectedValue="{Binding MajorCd}" SelectedValuePath="Code" DisplayMemberPath="Name" Padding="8,6" />
                            <StackPanel Visibility="{Binding IsMajorCodeManagerOpen, Converter={StaticResource BooleanToVisibilityConverter}}"
                                        Margin="0,8,0,0" Background="{DynamicResource LoginBackground}">
                                <ItemsControl ItemsSource="{Binding MajorCodes}">
                                    <ItemsControl.ItemTemplate>
                                        <DataTemplate>
                                            <Grid Margin="4">
                                                <TextBlock Text="{Binding Name}" FontSize="12" />
                                                <Button Content="삭제" HorizontalAlignment="Right" Padding="6,2" FontSize="10"
                                                        Command="{Binding DataContext.DeleteMajorCodeCommand, RelativeSource={RelativeSource AncestorType=UserControl}}"
                                                        CommandParameter="{Binding}" />
                                            </Grid>
                                        </DataTemplate>
                                    </ItemsControl.ItemTemplate>
                                </ItemsControl>
                                <Grid Margin="4">
                                    <Grid.ColumnDefinitions>
                                        <ColumnDefinition Width="70" />
                                        <ColumnDefinition Width="*" />
                                        <ColumnDefinition Width="Auto" />
                                    </Grid.ColumnDefinitions>
                                    <TextBox Text="{Binding NewCodeCode, UpdateSourceTrigger=PropertyChanged}" Margin="0,0,4,0" />
                                    <TextBox Grid.Column="1" Text="{Binding NewCodeName, UpdateSourceTrigger=PropertyChanged}" Margin="0,0,4,0" />
                                    <Button Grid.Column="2" Content="추가" Command="{Binding AddMajorCodeCommand}" />
                                </Grid>
                            </StackPanel>
                        </StackPanel>

                        <StackPanel Grid.Column="2">
                            <Grid Margin="0,0,0,6">
                                <TextBlock Text="소분류" FontSize="12" FontWeight="Bold" Foreground="{DynamicResource MutedText}" />
                                <Button Content="코드관리" HorizontalAlignment="Right" Padding="0" Background="Transparent" BorderThickness="0"
                                        Foreground="{DynamicResource Accent}" FontSize="11" FontWeight="Bold" Command="{Binding ToggleMinorCodeManagerCommand}" />
                            </Grid>
                            <ComboBox ItemsSource="{Binding MinorCodes}" SelectedValue="{Binding MinorCd}" SelectedValuePath="Code" DisplayMemberPath="Name" Padding="8,6" />
                            <StackPanel Visibility="{Binding IsMinorCodeManagerOpen, Converter={StaticResource BooleanToVisibilityConverter}}"
                                        Margin="0,8,0,0" Background="{DynamicResource LoginBackground}">
                                <ItemsControl ItemsSource="{Binding MinorCodes}">
                                    <ItemsControl.ItemTemplate>
                                        <DataTemplate>
                                            <Grid Margin="4">
                                                <TextBlock Text="{Binding Name}" FontSize="12" />
                                                <Button Content="삭제" HorizontalAlignment="Right" Padding="6,2" FontSize="10"
                                                        Command="{Binding DataContext.DeleteMinorCodeCommand, RelativeSource={RelativeSource AncestorType=UserControl}}"
                                                        CommandParameter="{Binding}" />
                                            </Grid>
                                        </DataTemplate>
                                    </ItemsControl.ItemTemplate>
                                </ItemsControl>
                                <Grid Margin="4">
                                    <Grid.ColumnDefinitions>
                                        <ColumnDefinition Width="70" />
                                        <ColumnDefinition Width="*" />
                                        <ColumnDefinition Width="Auto" />
                                    </Grid.ColumnDefinitions>
                                    <TextBox Text="{Binding NewCodeCode, UpdateSourceTrigger=PropertyChanged}" Margin="0,0,4,0" />
                                    <TextBox Grid.Column="1" Text="{Binding NewCodeName, UpdateSourceTrigger=PropertyChanged}" Margin="0,0,4,0" />
                                    <Button Grid.Column="2" Content="추가" Command="{Binding AddMinorCodeCommand}" />
                                </Grid>
                            </StackPanel>
                        </StackPanel>
                    </Grid>

                    <Grid Margin="0,0,0,6">
                        <TextBlock Text="POS분류" FontSize="12" FontWeight="Bold" Foreground="{DynamicResource MutedText}" />
                        <Button Content="코드관리" HorizontalAlignment="Right" Padding="0" Background="Transparent" BorderThickness="0"
                                Foreground="{DynamicResource Accent}" FontSize="11" FontWeight="Bold" Command="{Binding TogglePosCatCodeManagerCommand}" />
                    </Grid>
                    <ComboBox ItemsSource="{Binding PosCatCodes}" SelectedValue="{Binding PosCatCd}" SelectedValuePath="Code" DisplayMemberPath="Name" Padding="8,6" Margin="0,0,0,14" />
                    <StackPanel Visibility="{Binding IsPosCatCodeManagerOpen, Converter={StaticResource BooleanToVisibilityConverter}}"
                                Margin="0,-8,0,14" Background="{DynamicResource LoginBackground}">
                        <ItemsControl ItemsSource="{Binding PosCatCodes}">
                            <ItemsControl.ItemTemplate>
                                <DataTemplate>
                                    <Grid Margin="4">
                                        <TextBlock Text="{Binding Name}" FontSize="12" />
                                        <Button Content="삭제" HorizontalAlignment="Right" Padding="6,2" FontSize="10"
                                                Command="{Binding DataContext.DeletePosCatCodeCommand, RelativeSource={RelativeSource AncestorType=UserControl}}"
                                                CommandParameter="{Binding}" />
                                    </Grid>
                                </DataTemplate>
                            </ItemsControl.ItemTemplate>
                        </ItemsControl>
                        <Grid Margin="4">
                            <Grid.ColumnDefinitions>
                                <ColumnDefinition Width="70" />
                                <ColumnDefinition Width="*" />
                                <ColumnDefinition Width="Auto" />
                            </Grid.ColumnDefinitions>
                            <TextBox Text="{Binding NewCodeCode, UpdateSourceTrigger=PropertyChanged}" Margin="0,0,4,0" />
                            <TextBox Grid.Column="1" Text="{Binding NewCodeName, UpdateSourceTrigger=PropertyChanged}" Margin="0,0,4,0" />
                            <Button Grid.Column="2" Content="추가" Command="{Binding AddPosCatCodeCommand}" />
                        </Grid>
                    </StackPanel>

                    <TextBlock Text="상품명" FontSize="12" FontWeight="Bold" Foreground="{DynamicResource MutedText}" Margin="0,0,0,6" />
                    <TextBox Text="{Binding Name, UpdateSourceTrigger=PropertyChanged}" Padding="8,6" Margin="0,0,0,14" />

                    <TextBlock Text="바코드 번호 (미입력 시 자동 생성)" FontSize="12" FontWeight="Bold" Foreground="{DynamicResource MutedText}" Margin="0,0,0,6" />
                    <TextBox Text="{Binding BarcodeInput, UpdateSourceTrigger=PropertyChanged}" Padding="8,6" Margin="0,0,0,14" />

                    <Grid Margin="0,0,0,20">
                        <Grid.ColumnDefinitions>
                            <ColumnDefinition Width="*" />
                            <ColumnDefinition Width="14" />
                            <ColumnDefinition Width="*" />
                        </Grid.ColumnDefinitions>
                        <StackPanel Grid.Column="0">
                            <TextBlock Text="단가" FontSize="12" FontWeight="Bold" Foreground="{DynamicResource MutedText}" Margin="0,0,0,6" />
                            <TextBox Text="{Binding PriceInput, UpdateSourceTrigger=PropertyChanged}" Padding="8,6" />
                        </StackPanel>
                        <StackPanel Grid.Column="2">
                            <TextBlock Text="재고" FontSize="12" FontWeight="Bold" Foreground="{DynamicResource MutedText}" Margin="0,0,0,6" />
                            <TextBox Text="{Binding StockInput, UpdateSourceTrigger=PropertyChanged}" Padding="8,6" />
                        </StackPanel>
                    </Grid>

                    <Button Content="{Binding SaveButtonText}" Padding="0,12" Background="{DynamicResource Accent}" Foreground="White"
                            FontSize="14" FontWeight="Bold" Command="{Binding SaveCommand}" />
                </StackPanel>
            </Border>
        </ScrollViewer>
    </Grid>
</UserControl>
```
수정 모드에서도 바코드는 항상 편집 가능하게 둔다(원본 설계에 잠금 요구사항 없음).

- [ ] **Step 4: App.xaml.cs에 InventoryFormViewModel 팩토리 배선**

`App.xaml.cs`의 `OnStartup`에 서비스 등록 추가:
```csharp
        services.AddSingleton<IPhotoPicker, WpfPhotoPicker>();
        services.AddSingleton<IProductPhotoStorage>(_ => new FileSystemProductPhotoStorage(AppContext.BaseDirectory));
```

`CreateInventoryViewModelAsync` 아래에 폼 팩토리 함수와 배선 추가:
```csharp
        var photoPicker = _services.GetRequiredService<IPhotoPicker>();
        var photoStorage = _services.GetRequiredService<IProductPhotoStorage>();

        async Task<InventoryViewModel> CreateInventoryViewModelAsync(MainMenuViewModel mainMenu)
        {
            var vm = new InventoryViewModel(productRepository, codeRepository, session, navigation, mainMenu);
            vm.InventoryFormViewModelFactory = (inv, product) =>
                Task.FromResult(new InventoryFormViewModel(productRepository, codeRepository, photoPicker, photoStorage, navigation, inv, product));
            await vm.LoadAsync();
            return vm;
        }
```
(기존 `CreateInventoryViewModelAsync` 정의를 통째로 교체 — `using FishingMartPos.Services;`, `using FishingMartPos.ViewModels;`는 이미 상단에 있음)

- [ ] **Step 5: 스모크 테스트**

`tests/FishingMartPos.Tests/Views/InventoryFormViewSmokeTests.cs`:
```csharp
using FishingMartPos.Views;
using Xunit;

namespace FishingMartPos.Tests.Views;

public class InventoryFormViewSmokeTests
{
    [Fact]
    public void InventoryFormView_ConstructsWithoutException()
    {
        StaTestHelper.RunOnSta(() =>
        {
            var view = new InventoryFormView();
            Assert.NotNull(view);
        });
    }
}
```

- [ ] **Step 6: 빌드 + 전체 테스트**

Run: `dotnet build src/FishingMartPos/FishingMartPos.csproj -c Debug`
Expected: 빌드 성공 (실행 중인 앱 프로세스가 있으면 먼저 종료)

Run: `dotnet test tests/FishingMartPos.Tests/FishingMartPos.Tests.csproj`
Expected: 모두 통과

- [ ] **Step 7: 커밋**

```bash
git add src/FishingMartPos/Views/InventoryFormView.xaml src/FishingMartPos/Views/InventoryFormView.xaml.cs src/FishingMartPos/App.xaml.cs src/FishingMartPos/MainWindow.xaml tests/FishingMartPos.Tests/Views/InventoryFormViewSmokeTests.cs
git commit -m "InventoryFormView 화면 추가 및 App.xaml.cs DI 배선"
```

---

### Task 8: InventoryView — 헤더 "+ 상품등록", 행별 "수정" 버튼, 실제 사진 표시

**Files:**
- Modify: `src/FishingMartPos/Views/InventoryView.xaml`
- Modify: `src/FishingMartPos/ViewModels/InventoryViewModel.cs` (PhotoAbsolutePath 계산)
- Test: 기존 `InventoryViewModelTests.cs`에 사진 관련 케이스 추가

- [ ] **Step 1: RefreshRows에서 PhotoAbsolutePath 계산**

`InventoryViewModel.RefreshRows()`의 `Rows.Add(...)` 안, `Swatch = ...,` 다음 줄에 추가:
```csharp
                PhotoAbsolutePath = captured.PhotoPath is not null
                    ? System.IO.Path.Combine(AppContext.BaseDirectory, captured.PhotoPath)
                    : null,
```

- [ ] **Step 2: 실패하는 테스트 작성**

`InventoryViewModelTests.cs`에 추가:
```csharp
    [Fact]
    public async Task RefreshRows_ProductWithPhotoPath_SetsPhotoAbsolutePath()
    {
        var session = new CurrentSession();
        session.SignIn(new Staff { StaffCode = "ADMIN1", StaffName = "관리자", Role = "ADMIN", UseYn = "Y" }, new PosTerminal { PosCode = "1", PosName = "POS1" });
        var navigation = new NavigationService();
        var mainMenu = DummyMainMenu(session, navigation);
        var productsWithPhoto = new[]
        {
            new Product { Barcode = "B1", MajorCd = "FISH", MinorCd = "BAIT", PosCatCd = "BAIT", Name = "지렁이", Price = 5000, StockQty = 10, PhotoPath = "ProductPhotos/B1.jpg" },
        };
        var vm = new InventoryViewModel(new FakeProductRepository(productsWithPhoto), new FakeCodeRepository(SampleCodes()), session, navigation, mainMenu);
        await vm.LoadAsync();

        var row = Assert.Single(vm.Rows);
        Assert.EndsWith(System.IO.Path.Combine("ProductPhotos", "B1.jpg"), row.PhotoAbsolutePath);
    }
```

- [ ] **Step 3: 테스트 통과 확인 (Step 1에서 이미 구현했으므로 바로 통과해야 함)**

Run: `dotnet test tests/FishingMartPos.Tests/FishingMartPos.Tests.csproj --filter "FullyQualifiedName~InventoryViewModelTests"`
Expected: PASS

- [ ] **Step 4: InventoryView.xaml — 헤더에 "+ 상품등록" 버튼 추가**

`InventoryView.xaml`의 헤더 `Border` 안 `Grid`에 버튼 추가 (기존 "메인메뉴로" 버튼 왼쪽에):
```xml
                <StackPanel Orientation="Horizontal" HorizontalAlignment="Right">
                    <Button Content="+ 상품등록" Padding="12,5" FontSize="12" FontWeight="Bold" Margin="0,0,8,0"
                            Background="{DynamicResource Accent}" Foreground="White" BorderBrush="{DynamicResource Accent}" BorderThickness="1"
                            Command="{Binding GoToAddProductCommand}" />
                    <Button Content="메인메뉴로" Padding="12,5" FontSize="12" FontWeight="Bold"
                            Background="{DynamicResource LogoutButtonBackground}"
                            Foreground="{DynamicResource MutedText}"
                            BorderBrush="{DynamicResource InputBorder}" BorderThickness="1"
                            Command="{Binding GoToMainMenuCommand}" />
                </StackPanel>
```
(기존 `<Button Content="메인메뉴로" ... HorizontalAlignment="Right" .../>` 한 줄을 위 `StackPanel`로 교체 — `HorizontalAlignment="Right" VerticalAlignment="Center"`는 StackPanel에 옮긴다)

- [ ] **Step 5: InventoryView.xaml — 행에 "수정" 버튼 + 사진 표시(폴백 포함)**

`Grid.Column="8"` (기존 "삭제" 칸)을 두 칸으로 늘리기보다, 같은 칸 안에 가로 StackPanel로 "수정"/"삭제"를 나란히 배치(판매 화면의 "불러오기"/"삭제" 패턴과 동일):
```xml
                                    <StackPanel Grid.Column="8" Orientation="Horizontal" HorizontalAlignment="Center">
                                        <Button Content="수정" FontSize="11" Padding="8,4" Margin="0,0,4,0"
                                                Command="{Binding EditCommand}" />
                                        <Button Content="삭제" FontSize="11" Padding="8,4"
                                                Command="{Binding DeleteCommand}"
                                                Visibility="{Binding CanDelete, Converter={StaticResource BooleanToVisibilityConverter}}" />
                                    </StackPanel>
```
(열 너비가 좁으면 `ColumnDefinition Width="60"`을 `Width="110"` 정도로 늘린다 — 헤더 행의 마지막 `ColumnDefinition`과 `TextBlock Text="삭제"`도 폭에 맞춰 조정)

사진 칸(`Grid.Column="0"`, 기존 `<Border Background="{Binding Swatch}" .../>`)을 사진 유무에 따라 폴백하도록 교체:
```xml
                                    <Grid Width="32" Height="32">
                                        <Border Background="{Binding Swatch}" CornerRadius="2"
                                                Visibility="{Binding PhotoAbsolutePath, Converter={StaticResource NullToVisibilityConverter}, ConverterParameter=Invert}" />
                                        <Image Source="{Binding PhotoAbsolutePath}" Stretch="UniformToFill"
                                               Visibility="{Binding PhotoAbsolutePath, Converter={StaticResource NullToVisibilityConverter}}" />
                                    </Grid>
```
**주의:** `NullToVisibilityConverter`는 `ConverterParameter`로 반전 기능이 없다(현재 구현은 null→Collapsed, non-null→Visible 고정). "사진 없으면 스와치, 있으면 사진"을 boolean 반전 없이 구현하려면 `NullToVisibilityConverter`에 반전 파라미터를 추가하거나, 새 컨버터 `InverseNullToVisibilityConverter`를 하나 더 만든다. 이 스텝에서 `src/FishingMartPos/Converters/InverseNullToVisibilityConverter.cs`를 추가:
```csharp
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace FishingMartPos.Converters;

public sealed class InverseNullToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object parameter, CultureInfo culture) =>
        value is null ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
```
그리고 `InventoryView.xaml`의 `UserControl.Resources`에 등록 후, 스와치 `Border`의 `Visibility` 바인딩을 `Converter={StaticResource InverseNullToVisibilityConverter}`로 고친다(위 예시의 `ConverterParameter=Invert` 부분은 삭제).

- [ ] **Step 6: 빌드 + 전체 테스트 + 수동 GUI 확인**

Run: `dotnet build src/FishingMartPos/FishingMartPos.csproj -c Debug`
Run: `dotnet test tests/FishingMartPos.Tests/FishingMartPos.Tests.csproj`
Expected: 둘 다 성공

앱을 직접 실행해 재고관리 → "+ 상품등록" → 폼 채우고 저장 → 목록에 반영되는지, 행의 "수정" → 값 채워짐 → 수정 저장 → 반영되는지, 사진 선택 후 저장 → 목록에 실제 사진 표시되는지, 코드관리에서 코드 추가/삭제(사용 중 코드 삭제 거부 포함)까지 스크린샷으로 확인한다.

- [ ] **Step 7: 커밋**

```bash
git add src/FishingMartPos/Views/InventoryView.xaml src/FishingMartPos/ViewModels/InventoryViewModel.cs src/FishingMartPos/Converters/InverseNullToVisibilityConverter.cs tests/FishingMartPos.Tests/ViewModels/InventoryViewModelTests.cs
git commit -m "재고관리 화면에 상품등록 진입 버튼, 행별 수정 버튼, 실제 사진 표시(폴백 포함) 반영"
```

---

## Self-Review 체크리스트 (executor가 완료 후 확인)

- [ ] 스펙의 모든 요구사항(등록/수정 통합 폼, 인라인 코드관리, 실사진 업로드, 바코드 자동생성, 검증 규칙)에 대응하는 태스크가 있는가
- [ ] 실행 중인 `FishingMartPos` 프로세스를 매 빌드 전에 종료했는가
- [ ] `ProductPhotos` 폴더가 `.gitignore`에 없다면 실제 사진 테스트 산출물이 커밋되지 않도록 주의(테스트는 임시 폴더만 사용하므로 실제 앱 실행 시에만 폴더가 생김 — 커밋 전 `git status`로 확인)
- [ ] 전체 테스트(`dotnet test`)가 마지막에 통과하는가
