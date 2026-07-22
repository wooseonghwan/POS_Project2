# 환경설정 하위화면 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 환경설정 허브 화면과 5개 하위화면(직원관리/프린터설정/영수증설정/시스템정보/상품코드관리)을 구현하고, 메인메뉴의 "환경설정" 타일을 ADMIN 전용으로 제한한다.

**Architecture:** 기존 화면들과 동일한 원칙을 따른다 — 리포지토리는 원본 데이터만 반환하고 ViewModel이 가공한다. 목록+폼 화면(직원관리)은 `InventoryView`/`InventoryFormView`와 동일한 팩토리·returnTo 패턴. 허브(`SettingsViewModel`)는 5개 하위화면 각각에 대한 `Func<SettingsViewModel, Task<XViewModel>>` 팩토리를 갖는다. 상품코드관리는 기존 `ICodeRepository`를 재사용하지만 `InventoryFormViewModel`과 코드를 공유하지 않고 독립적으로 재구현한다(의도적 중복 — 아래 Global Constraints 참고).

**Tech Stack:** WPF/.NET8 MVVM, CommunityToolkit.Mvvm(`[ObservableProperty]`/`[RelayCommand]`), Dapper + MySqlConnector, xUnit.

## Global Constraints

- DB 마이그레이션 불필요 — `staff_tb`/`printer_config_tb`/`receipt_config_tb`/`system_info_tb`는 이미 `db/migrations/001_create_schema.sql`에 존재.
- 허브 화면 진입 이후의 5개 하위화면에는 개별 `IsAdmin` 가드를 넣지 않는다 — 허브 자체가 `MainMenuViewModel.GoToSettings`의 `if (!IsAdmin) return;` 가드를 통해서만 도달 가능하다.
- 토스트 패턴은 항상 동일: `ToastMessage = "메시지"; await _delay.Delay(TimeSpan.FromMilliseconds(1200)); ToastMessage = null;` (`IDelayProvider` 재사용).
- PIN 해시는 반드시 `FishingMartPos.Security.PinHasher.Hash(pin)` 재사용 — 별도 구현 금지.
- `printer_config_tb.drawer_kick_enabled`는 DB에 `CHAR(1)` 'Y'/'N'으로 저장되어 있다 — 리포지토리 SQL에서 변환하고, 모델(`PrinterConfig.DrawerKickEnabled`)은 `bool`로 유지.
- `CodeManageViewModel`은 `InventoryFormViewModel`의 코드관리 로직(`AddMajorCode`/`DeleteMajorCode` 등)과 **의도적으로 코드를 공유하지 않는다** — 두 화면 모두 이미 안정적으로 동작 중인 상태에서 공용 서비스로 추출하는 리팩터링은 이번 범위 밖. 약 20~30줄의 유사 로직 중복을 감수한다.
- **`App.xaml.cs` 수정 시 반드시 파일을 직접 읽어서 확인할 것** — 지난 매출관리 작업에서 `CreateLoginViewModel()`이 `new(...)` 형태의 target-typed 표현식이라 `"new LoginViewModel("` 문자열 grep으로 걸리지 않는 호출부였음이 뒤늦게 발견된 적이 있다. grep만 믿지 말고 파일을 직접 열어 생성자 호출부와 파라미터 개수를 확인·갱신할 것.
- 신규 리포지토리(`IPrinterConfigRepository`/`IReceiptConfigRepository`/`ISystemInfoRepository`)의 `SaveAsync`는 `INSERT ... ON DUPLICATE KEY UPDATE` upsert 패턴(`ProductRepository.SaveAsync`와 동일한 스타일).
- 프린터/영수증 설정은 항상 **현재 세션의 `_session.CurrentTerminal!.PosCode`** 기준으로 조회/저장한다(단말별 설정).
- "테스트 인쇄" 버튼은 실제 인쇄 없이 토스트 "프린터 연동은 지원 예정입니다"만 표시.

---

## Task 1: IStaffRepository 확장 (GetAllAsync/CreateAsync/UpdateAsync)

**Files:**
- Modify: `src/FishingMartPos/Repositories/IStaffRepository.cs`
- Modify: `src/FishingMartPos/Repositories/StaffRepository.cs`
- Modify: `tests/FishingMartPos.Tests/Fakes/FakeStaffRepository.cs`
- Modify: `tests/FishingMartPos.Tests/Repositories/StaffRepositoryTests.cs`

**Interfaces:**
- Produces: `IStaffRepository.GetAllAsync(): Task<IReadOnlyList<Staff>>` (use_yn 무관 전체), `CreateAsync(Staff staff, string pin): Task`, `UpdateAsync(Staff staff, string? newPin): Task` (newPin이 null/공백이면 기존 pin_hash 유지).
- `FakeStaffRepository`의 기존 생성자 `FakeStaffRepository(Dictionary<string, Staff> staffByPin)`는 그대로 유지(다른 테스트 파일들이 이미 이 시그니처로 사용 중) — 내부에 `List<Staff>` 백업 스토어를 추가해 `GetAllAsync`/`CreateAsync`/`UpdateAsync`를 구현하고, `CreatedStaff`/`UpdatedStaff` 호출 기록 리스트를 노출한다.

- [ ] **Step 1: 통합 테스트 작성 (StaffRepositoryTests 확장)**

`tests/FishingMartPos.Tests/Repositories/StaffRepositoryTests.cs`의 기존 내용 끝에 아래 테스트들을 추가한다(파일 상단 `using`/`CreateRepository()`는 기존 그대로 유지):

```csharp
    [Fact]
    public async Task GetAllAsync_ReturnsSeededAdminRegardlessOfUseYn()
    {
        IStaffRepository repository = CreateRepository();

        var all = await repository.GetAllAsync();

        Assert.Contains(all, s => s.StaffCode == "ADMIN1");
    }

    [Fact]
    public async Task CreateAsync_ThenFindByPin_ReturnsNewlyCreatedStaff()
    {
        IStaffRepository repository = CreateRepository();
        var config = AppConfig.Load(AppContext.BaseDirectory);
        var factory = new MySqlConnectionFactory(config);
        const string testCode = "TESTSTF";

        using var conn = factory.CreateOpenConnection();
        await conn.ExecuteAsync("DELETE FROM staff_tb WHERE staff_cd = @Code", new { Code = testCode });
        try
        {
            await repository.CreateAsync(
                new Staff { StaffCode = testCode, StaffName = "테스트직원", Role = "STAFF", UseYn = "Y" },
                "1357");

            var found = await repository.FindByPinAsync("1357");

            Assert.NotNull(found);
            Assert.Equal(testCode, found!.StaffCode);
            Assert.Equal("테스트직원", found.StaffName);
        }
        finally
        {
            await conn.ExecuteAsync("DELETE FROM staff_tb WHERE staff_cd = @Code", new { Code = testCode });
        }
    }

    [Fact]
    public async Task UpdateAsync_WithNullPin_KeepsExistingPinButUpdatesOtherFields()
    {
        IStaffRepository repository = CreateRepository();
        var config = AppConfig.Load(AppContext.BaseDirectory);
        var factory = new MySqlConnectionFactory(config);
        const string testCode = "TESTSTF";

        using var conn = factory.CreateOpenConnection();
        await conn.ExecuteAsync("DELETE FROM staff_tb WHERE staff_cd = @Code", new { Code = testCode });
        try
        {
            await repository.CreateAsync(
                new Staff { StaffCode = testCode, StaffName = "원래이름", Role = "STAFF", UseYn = "Y" },
                "2468");

            await repository.UpdateAsync(
                new Staff { StaffCode = testCode, StaffName = "바뀐이름", Role = "STAFF", UseYn = "N" },
                newPin: null);

            var stillFindableByOldPin = await repository.FindByPinAsync("2468");
            Assert.Null(stillFindableByOldPin); // use_yn='N'이 되었으므로 로그인 조회(FindByPinAsync)는 실패해야 함

            var all = await repository.GetAllAsync();
            var updated = all.Single(s => s.StaffCode == testCode);
            Assert.Equal("바뀐이름", updated.StaffName);
            Assert.Equal("N", updated.UseYn);
        }
        finally
        {
            await conn.ExecuteAsync("DELETE FROM staff_tb WHERE staff_cd = @Code", new { Code = testCode });
        }
    }

    [Fact]
    public async Task UpdateAsync_WithNewPin_ChangesPinHash()
    {
        IStaffRepository repository = CreateRepository();
        var config = AppConfig.Load(AppContext.BaseDirectory);
        var factory = new MySqlConnectionFactory(config);
        const string testCode = "TESTSTF";

        using var conn = factory.CreateOpenConnection();
        await conn.ExecuteAsync("DELETE FROM staff_tb WHERE staff_cd = @Code", new { Code = testCode });
        try
        {
            await repository.CreateAsync(
                new Staff { StaffCode = testCode, StaffName = "직원", Role = "STAFF", UseYn = "Y" },
                "1111");

            await repository.UpdateAsync(
                new Staff { StaffCode = testCode, StaffName = "직원", Role = "STAFF", UseYn = "Y" },
                newPin: "2222");

            Assert.Null(await repository.FindByPinAsync("1111"));
            Assert.NotNull(await repository.FindByPinAsync("2222"));
        }
        finally
        {
            await conn.ExecuteAsync("DELETE FROM staff_tb WHERE staff_cd = @Code", new { Code = testCode });
        }
    }
```

Also add these `using` statements at the top of the file if not already present: `using Dapper;` and `using Xunit;` (already present) — `FishingMartPos.Data`/`FishingMartPos.Configuration`/`FishingMartPos.Repositories` are already imported.

- [ ] **Step 2: 테스트 실행 — 컴파일 실패 확인**

Run: `dotnet test tests/FishingMartPos.Tests --filter StaffRepositoryTests`
Expected: FAIL to compile — `GetAllAsync`/`CreateAsync`/`UpdateAsync` do not exist on `IStaffRepository`.

- [ ] **Step 3: IStaffRepository/StaffRepository 구현**

`src/FishingMartPos/Repositories/IStaffRepository.cs` 전체를 다음으로 교체:

```csharp
using FishingMartPos.Models;

namespace FishingMartPos.Repositories;

public interface IStaffRepository
{
    Task<Staff?> FindByPinAsync(string pin);
    Task<IReadOnlyList<Staff>> GetAllAsync();
    Task CreateAsync(Staff staff, string pin);
    Task UpdateAsync(Staff staff, string? newPin);
}
```

`src/FishingMartPos/Repositories/StaffRepository.cs` 전체를 다음으로 교체:

```csharp
using Dapper;
using FishingMartPos.Data;
using FishingMartPos.Models;
using FishingMartPos.Security;

namespace FishingMartPos.Repositories;

public sealed class StaffRepository : IStaffRepository
{
    private readonly IDbConnectionFactory _connectionFactory;

    public StaffRepository(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<Staff?> FindByPinAsync(string pin)
    {
        string pinHash = PinHasher.Hash(pin);

        using var connection = _connectionFactory.CreateOpenConnection();
        const string sql = """
            SELECT staff_cd AS StaffCode, staff_name AS StaffName, role AS Role, use_yn AS UseYn
            FROM staff_tb
            WHERE pin_hash = @PinHash AND use_yn = 'Y'
            LIMIT 1
            """;

        return await connection.QuerySingleOrDefaultAsync<Staff>(sql, new { PinHash = pinHash });
    }

    public async Task<IReadOnlyList<Staff>> GetAllAsync()
    {
        using var connection = _connectionFactory.CreateOpenConnection();
        const string sql = """
            SELECT staff_cd AS StaffCode, staff_name AS StaffName, role AS Role, use_yn AS UseYn
            FROM staff_tb
            ORDER BY staff_cd
            """;

        var result = await connection.QueryAsync<Staff>(sql);
        return result.ToList();
    }

    public async Task CreateAsync(Staff staff, string pin)
    {
        string pinHash = PinHasher.Hash(pin);
        using var connection = _connectionFactory.CreateOpenConnection();
        const string sql = """
            INSERT INTO staff_tb (staff_cd, staff_name, pin_hash, role, use_yn)
            VALUES (@StaffCode, @StaffName, @PinHash, @Role, @UseYn)
            """;
        await connection.ExecuteAsync(sql, new { staff.StaffCode, staff.StaffName, PinHash = pinHash, staff.Role, staff.UseYn });
    }

    public async Task UpdateAsync(Staff staff, string? newPin)
    {
        using var connection = _connectionFactory.CreateOpenConnection();
        if (string.IsNullOrWhiteSpace(newPin))
        {
            const string sql = """
                UPDATE staff_tb SET staff_name = @StaffName, role = @Role, use_yn = @UseYn
                WHERE staff_cd = @StaffCode
                """;
            await connection.ExecuteAsync(sql, new { staff.StaffCode, staff.StaffName, staff.Role, staff.UseYn });
        }
        else
        {
            string pinHash = PinHasher.Hash(newPin);
            const string sql = """
                UPDATE staff_tb SET staff_name = @StaffName, role = @Role, use_yn = @UseYn, pin_hash = @PinHash
                WHERE staff_cd = @StaffCode
                """;
            await connection.ExecuteAsync(sql, new { staff.StaffCode, staff.StaffName, staff.Role, staff.UseYn, PinHash = pinHash });
        }
    }
}
```

`tests/FishingMartPos.Tests/Fakes/FakeStaffRepository.cs` 전체를 다음으로 교체:

```csharp
using FishingMartPos.Models;
using FishingMartPos.Repositories;

namespace FishingMartPos.Tests.Fakes;

public sealed class FakeStaffRepository : IStaffRepository
{
    private readonly Dictionary<string, Staff> _staffByPin;
    private readonly List<Staff> _allStaff;

    public FakeStaffRepository(Dictionary<string, Staff> staffByPin)
    {
        _staffByPin = staffByPin;
        _allStaff = staffByPin.Values.ToList();
    }

    public List<(Staff Staff, string Pin)> CreatedStaff { get; } = new();
    public List<(Staff Staff, string? NewPin)> UpdatedStaff { get; } = new();

    public Task<Staff?> FindByPinAsync(string pin)
    {
        _staffByPin.TryGetValue(pin, out var staff);
        return Task.FromResult(staff);
    }

    public Task<IReadOnlyList<Staff>> GetAllAsync() =>
        Task.FromResult((IReadOnlyList<Staff>)_allStaff.ToList());

    public Task CreateAsync(Staff staff, string pin)
    {
        CreatedStaff.Add((staff, pin));
        _allStaff.Add(staff);
        return Task.CompletedTask;
    }

    public Task UpdateAsync(Staff staff, string? newPin)
    {
        UpdatedStaff.Add((staff, newPin));
        _allStaff.RemoveAll(s => s.StaffCode == staff.StaffCode);
        _allStaff.Add(staff);
        return Task.CompletedTask;
    }
}
```

- [ ] **Step 4: 테스트 재실행 — 통과 확인**

Run: `dotnet test tests/FishingMartPos.Tests --filter StaffRepositoryTests`
Expected: PASS (all tests, including the 4 new ones). This test requires a reachable dev DB (matches the existing pattern for all `*RepositoryTests`).

Also run the full existing suite once to confirm nothing else broke (other files reference `FakeStaffRepository`'s constructor, which is unchanged):

Run: `dotnet test tests/FishingMartPos.Tests`
Expected: PASS (all existing tests still pass — `FakeStaffRepository`'s public constructor signature did not change).

- [ ] **Step 5: 커밋**

```bash
git add src/FishingMartPos/Repositories/IStaffRepository.cs src/FishingMartPos/Repositories/StaffRepository.cs tests/FishingMartPos.Tests/Fakes/FakeStaffRepository.cs tests/FishingMartPos.Tests/Repositories/StaffRepositoryTests.cs
git commit -m "IStaffRepository에 GetAllAsync/CreateAsync/UpdateAsync 추가 (직원관리 화면 준비)"
```

---

## Task 2: PrinterConfig/ReceiptConfig/SystemInfo 모델 + 리포지토리

**Files:**
- Create: `src/FishingMartPos/Models/PrinterConfig.cs`
- Create: `src/FishingMartPos/Models/ReceiptConfig.cs`
- Create: `src/FishingMartPos/Models/SystemInfo.cs`
- Create: `src/FishingMartPos/Repositories/IPrinterConfigRepository.cs`
- Create: `src/FishingMartPos/Repositories/PrinterConfigRepository.cs`
- Create: `src/FishingMartPos/Repositories/IReceiptConfigRepository.cs`
- Create: `src/FishingMartPos/Repositories/ReceiptConfigRepository.cs`
- Create: `src/FishingMartPos/Repositories/ISystemInfoRepository.cs`
- Create: `src/FishingMartPos/Repositories/SystemInfoRepository.cs`
- Create: `tests/FishingMartPos.Tests/Fakes/FakePrinterConfigRepository.cs`
- Create: `tests/FishingMartPos.Tests/Fakes/FakeReceiptConfigRepository.cs`
- Create: `tests/FishingMartPos.Tests/Fakes/FakeSystemInfoRepository.cs`
- Create: `tests/FishingMartPos.Tests/Repositories/PrinterConfigRepositoryTests.cs`
- Create: `tests/FishingMartPos.Tests/Repositories/ReceiptConfigRepositoryTests.cs`
- Create: `tests/FishingMartPos.Tests/Repositories/SystemInfoRepositoryTests.cs`
- Modify: `src/FishingMartPos/App.xaml.cs` (DI 등록만 추가 — 아직 사용처는 없음)

**Interfaces:**
- Produces: `PrinterConfig { PosCd, PrinterPort, PrinterName, DrawerKickEnabled(bool) }`, `ReceiptConfig { PosCd, HeaderText, FooterText }`, `SystemInfo { AppVersion, DbVersion }`.
- Produces: `IPrinterConfigRepository.GetAsync(string posCd): Task<PrinterConfig?>`, `SaveAsync(PrinterConfig): Task`.
- Produces: `IReceiptConfigRepository.GetAsync(string posCd): Task<ReceiptConfig?>`, `SaveAsync(ReceiptConfig): Task`.
- Produces: `ISystemInfoRepository.GetAsync(): Task<SystemInfo?>` (단일 행 테이블, `LIMIT 1`).
- Produces (Fakes): `FakePrinterConfigRepository(Dictionary<string, PrinterConfig>? byPosCd = null)` with `SavedConfigs: List<PrinterConfig>`; `FakeReceiptConfigRepository` analogous; `FakeSystemInfoRepository(SystemInfo? info = null, Exception? exceptionToThrow = null)`.

- [ ] **Step 1: 모델 작성**

`src/FishingMartPos/Models/PrinterConfig.cs`:
```csharp
namespace FishingMartPos.Models;

public sealed class PrinterConfig
{
    public required string PosCd { get; init; }
    public string? PrinterPort { get; init; }
    public string? PrinterName { get; init; }
    public required bool DrawerKickEnabled { get; init; }
}
```

`src/FishingMartPos/Models/ReceiptConfig.cs`:
```csharp
namespace FishingMartPos.Models;

public sealed class ReceiptConfig
{
    public required string PosCd { get; init; }
    public string? HeaderText { get; init; }
    public string? FooterText { get; init; }
}
```

`src/FishingMartPos/Models/SystemInfo.cs`:
```csharp
namespace FishingMartPos.Models;

public sealed class SystemInfo
{
    public required string AppVersion { get; init; }
    public required string DbVersion { get; init; }
}
```

- [ ] **Step 2: 리포지토리 인터페이스 + 구현 작성**

`src/FishingMartPos/Repositories/IPrinterConfigRepository.cs`:
```csharp
using FishingMartPos.Models;

namespace FishingMartPos.Repositories;

public interface IPrinterConfigRepository
{
    Task<PrinterConfig?> GetAsync(string posCd);
    Task SaveAsync(PrinterConfig config);
}
```

`src/FishingMartPos/Repositories/PrinterConfigRepository.cs`:
```csharp
using Dapper;
using FishingMartPos.Data;
using FishingMartPos.Models;

namespace FishingMartPos.Repositories;

public sealed class PrinterConfigRepository : IPrinterConfigRepository
{
    private readonly IDbConnectionFactory _connectionFactory;

    public PrinterConfigRepository(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<PrinterConfig?> GetAsync(string posCd)
    {
        using var connection = _connectionFactory.CreateOpenConnection();
        const string sql = """
            SELECT pos_cd AS PosCd, printer_port AS PrinterPort, printer_name AS PrinterName,
                   CASE WHEN drawer_kick_enabled = 'Y' THEN 1 ELSE 0 END AS DrawerKickEnabled
            FROM printer_config_tb
            WHERE pos_cd = @PosCd
            LIMIT 1
            """;
        return await connection.QuerySingleOrDefaultAsync<PrinterConfig>(sql, new { PosCd = posCd });
    }

    public async Task SaveAsync(PrinterConfig config)
    {
        using var connection = _connectionFactory.CreateOpenConnection();
        const string sql = """
            INSERT INTO printer_config_tb (pos_cd, printer_port, printer_name, drawer_kick_enabled)
            VALUES (@PosCd, @PrinterPort, @PrinterName, @DrawerKickEnabled)
            ON DUPLICATE KEY UPDATE
                printer_port = VALUES(printer_port), printer_name = VALUES(printer_name),
                drawer_kick_enabled = VALUES(drawer_kick_enabled)
            """;
        await connection.ExecuteAsync(sql, new
        {
            config.PosCd,
            config.PrinterPort,
            config.PrinterName,
            DrawerKickEnabled = config.DrawerKickEnabled ? "Y" : "N",
        });
    }
}
```

`src/FishingMartPos/Repositories/IReceiptConfigRepository.cs`:
```csharp
using FishingMartPos.Models;

namespace FishingMartPos.Repositories;

public interface IReceiptConfigRepository
{
    Task<ReceiptConfig?> GetAsync(string posCd);
    Task SaveAsync(ReceiptConfig config);
}
```

`src/FishingMartPos/Repositories/ReceiptConfigRepository.cs`:
```csharp
using Dapper;
using FishingMartPos.Data;
using FishingMartPos.Models;

namespace FishingMartPos.Repositories;

public sealed class ReceiptConfigRepository : IReceiptConfigRepository
{
    private readonly IDbConnectionFactory _connectionFactory;

    public ReceiptConfigRepository(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<ReceiptConfig?> GetAsync(string posCd)
    {
        using var connection = _connectionFactory.CreateOpenConnection();
        const string sql = """
            SELECT pos_cd AS PosCd, header_text AS HeaderText, footer_text AS FooterText
            FROM receipt_config_tb
            WHERE pos_cd = @PosCd
            LIMIT 1
            """;
        return await connection.QuerySingleOrDefaultAsync<ReceiptConfig>(sql, new { PosCd = posCd });
    }

    public async Task SaveAsync(ReceiptConfig config)
    {
        using var connection = _connectionFactory.CreateOpenConnection();
        const string sql = """
            INSERT INTO receipt_config_tb (pos_cd, header_text, footer_text)
            VALUES (@PosCd, @HeaderText, @FooterText)
            ON DUPLICATE KEY UPDATE
                header_text = VALUES(header_text), footer_text = VALUES(footer_text)
            """;
        await connection.ExecuteAsync(sql, config);
    }
}
```

`src/FishingMartPos/Repositories/ISystemInfoRepository.cs`:
```csharp
using FishingMartPos.Models;

namespace FishingMartPos.Repositories;

public interface ISystemInfoRepository
{
    Task<SystemInfo?> GetAsync();
}
```

`src/FishingMartPos/Repositories/SystemInfoRepository.cs`:
```csharp
using Dapper;
using FishingMartPos.Data;
using FishingMartPos.Models;

namespace FishingMartPos.Repositories;

public sealed class SystemInfoRepository : ISystemInfoRepository
{
    private readonly IDbConnectionFactory _connectionFactory;

    public SystemInfoRepository(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<SystemInfo?> GetAsync()
    {
        using var connection = _connectionFactory.CreateOpenConnection();
        const string sql = """
            SELECT app_version AS AppVersion, db_version AS DbVersion
            FROM system_info_tb
            LIMIT 1
            """;
        return await connection.QuerySingleOrDefaultAsync<SystemInfo>(sql);
    }
}
```

- [ ] **Step 3: Fakes 작성**

`tests/FishingMartPos.Tests/Fakes/FakePrinterConfigRepository.cs`:
```csharp
using FishingMartPos.Models;
using FishingMartPos.Repositories;

namespace FishingMartPos.Tests.Fakes;

public sealed class FakePrinterConfigRepository : IPrinterConfigRepository
{
    private readonly Dictionary<string, PrinterConfig> _byPosCd;

    public FakePrinterConfigRepository(Dictionary<string, PrinterConfig>? byPosCd = null)
    {
        _byPosCd = byPosCd ?? new Dictionary<string, PrinterConfig>();
    }

    public List<PrinterConfig> SavedConfigs { get; } = new();

    public Task<PrinterConfig?> GetAsync(string posCd)
    {
        _byPosCd.TryGetValue(posCd, out var config);
        return Task.FromResult(config);
    }

    public Task SaveAsync(PrinterConfig config)
    {
        SavedConfigs.Add(config);
        _byPosCd[config.PosCd] = config;
        return Task.CompletedTask;
    }
}
```

`tests/FishingMartPos.Tests/Fakes/FakeReceiptConfigRepository.cs`:
```csharp
using FishingMartPos.Models;
using FishingMartPos.Repositories;

namespace FishingMartPos.Tests.Fakes;

public sealed class FakeReceiptConfigRepository : IReceiptConfigRepository
{
    private readonly Dictionary<string, ReceiptConfig> _byPosCd;

    public FakeReceiptConfigRepository(Dictionary<string, ReceiptConfig>? byPosCd = null)
    {
        _byPosCd = byPosCd ?? new Dictionary<string, ReceiptConfig>();
    }

    public List<ReceiptConfig> SavedConfigs { get; } = new();

    public Task<ReceiptConfig?> GetAsync(string posCd)
    {
        _byPosCd.TryGetValue(posCd, out var config);
        return Task.FromResult(config);
    }

    public Task SaveAsync(ReceiptConfig config)
    {
        SavedConfigs.Add(config);
        _byPosCd[config.PosCd] = config;
        return Task.CompletedTask;
    }
}
```

`tests/FishingMartPos.Tests/Fakes/FakeSystemInfoRepository.cs`:
```csharp
using FishingMartPos.Models;
using FishingMartPos.Repositories;

namespace FishingMartPos.Tests.Fakes;

public sealed class FakeSystemInfoRepository : ISystemInfoRepository
{
    private readonly SystemInfo? _info;
    private readonly Exception? _exceptionToThrow;

    public FakeSystemInfoRepository(SystemInfo? info = null, Exception? exceptionToThrow = null)
    {
        _info = info;
        _exceptionToThrow = exceptionToThrow;
    }

    public Task<SystemInfo?> GetAsync()
    {
        if (_exceptionToThrow is not null)
        {
            throw _exceptionToThrow;
        }
        return Task.FromResult(_info);
    }
}
```

- [ ] **Step 4: 통합 테스트 작성 (실제 개발 DB 대상)**

`tests/FishingMartPos.Tests/Repositories/PrinterConfigRepositoryTests.cs`:
```csharp
using Dapper;
using FishingMartPos.Configuration;
using FishingMartPos.Data;
using FishingMartPos.Models;
using FishingMartPos.Repositories;
using Xunit;

namespace FishingMartPos.Tests.Repositories;

public class PrinterConfigRepositoryTests
{
    private const string TestPosCd = "9";

    private static (IPrinterConfigRepository repo, IDbConnectionFactory factory) Create()
    {
        var config = AppConfig.Load(AppContext.BaseDirectory);
        var factory = new MySqlConnectionFactory(config);
        return (new PrinterConfigRepository(factory), factory);
    }

    [Fact]
    public async Task GetAsync_ReturnsNull_WhenNoRowExists()
    {
        var (repo, factory) = Create();
        using var conn = factory.CreateOpenConnection();
        await conn.ExecuteAsync("DELETE FROM printer_config_tb WHERE pos_cd = @PosCd", new { PosCd = TestPosCd });

        var result = await repo.GetAsync(TestPosCd);

        Assert.Null(result);
    }

    [Fact]
    public async Task SaveAsync_ThenGetAsync_ReturnsSavedValues()
    {
        var (repo, factory) = Create();
        using var conn = factory.CreateOpenConnection();
        await conn.ExecuteAsync("DELETE FROM printer_config_tb WHERE pos_cd = @PosCd", new { PosCd = TestPosCd });
        try
        {
            await repo.SaveAsync(new PrinterConfig { PosCd = TestPosCd, PrinterPort = "COM3", PrinterName = "EPSON-TM88", DrawerKickEnabled = true });

            var result = await repo.GetAsync(TestPosCd);

            Assert.NotNull(result);
            Assert.Equal("COM3", result!.PrinterPort);
            Assert.Equal("EPSON-TM88", result.PrinterName);
            Assert.True(result.DrawerKickEnabled);
        }
        finally
        {
            await conn.ExecuteAsync("DELETE FROM printer_config_tb WHERE pos_cd = @PosCd", new { PosCd = TestPosCd });
        }
    }

    [Fact]
    public async Task SaveAsync_Twice_OverwritesExistingRow()
    {
        var (repo, factory) = Create();
        using var conn = factory.CreateOpenConnection();
        await conn.ExecuteAsync("DELETE FROM printer_config_tb WHERE pos_cd = @PosCd", new { PosCd = TestPosCd });
        try
        {
            await repo.SaveAsync(new PrinterConfig { PosCd = TestPosCd, PrinterPort = "COM3", PrinterName = "A", DrawerKickEnabled = true });
            await repo.SaveAsync(new PrinterConfig { PosCd = TestPosCd, PrinterPort = "COM4", PrinterName = "B", DrawerKickEnabled = false });

            var result = await repo.GetAsync(TestPosCd);

            Assert.Equal("COM4", result!.PrinterPort);
            Assert.Equal("B", result.PrinterName);
            Assert.False(result.DrawerKickEnabled);
        }
        finally
        {
            await conn.ExecuteAsync("DELETE FROM printer_config_tb WHERE pos_cd = @PosCd", new { PosCd = TestPosCd });
        }
    }
}
```

`tests/FishingMartPos.Tests/Repositories/ReceiptConfigRepositoryTests.cs`:
```csharp
using Dapper;
using FishingMartPos.Configuration;
using FishingMartPos.Data;
using FishingMartPos.Models;
using FishingMartPos.Repositories;
using Xunit;

namespace FishingMartPos.Tests.Repositories;

public class ReceiptConfigRepositoryTests
{
    private const string TestPosCd = "9";

    private static (IReceiptConfigRepository repo, IDbConnectionFactory factory) Create()
    {
        var config = AppConfig.Load(AppContext.BaseDirectory);
        var factory = new MySqlConnectionFactory(config);
        return (new ReceiptConfigRepository(factory), factory);
    }

    [Fact]
    public async Task GetAsync_ReturnsNull_WhenNoRowExists()
    {
        var (repo, factory) = Create();
        using var conn = factory.CreateOpenConnection();
        await conn.ExecuteAsync("DELETE FROM receipt_config_tb WHERE pos_cd = @PosCd", new { PosCd = TestPosCd });

        var result = await repo.GetAsync(TestPosCd);

        Assert.Null(result);
    }

    [Fact]
    public async Task SaveAsync_ThenGetAsync_ReturnsSavedValues()
    {
        var (repo, factory) = Create();
        using var conn = factory.CreateOpenConnection();
        await conn.ExecuteAsync("DELETE FROM receipt_config_tb WHERE pos_cd = @PosCd", new { PosCd = TestPosCd });
        try
        {
            await repo.SaveAsync(new ReceiptConfig { PosCd = TestPosCd, HeaderText = "환영합니다", FooterText = "감사합니다" });

            var result = await repo.GetAsync(TestPosCd);

            Assert.NotNull(result);
            Assert.Equal("환영합니다", result!.HeaderText);
            Assert.Equal("감사합니다", result.FooterText);
        }
        finally
        {
            await conn.ExecuteAsync("DELETE FROM receipt_config_tb WHERE pos_cd = @PosCd", new { PosCd = TestPosCd });
        }
    }

    [Fact]
    public async Task SaveAsync_Twice_OverwritesExistingRow()
    {
        var (repo, factory) = Create();
        using var conn = factory.CreateOpenConnection();
        await conn.ExecuteAsync("DELETE FROM receipt_config_tb WHERE pos_cd = @PosCd", new { PosCd = TestPosCd });
        try
        {
            await repo.SaveAsync(new ReceiptConfig { PosCd = TestPosCd, HeaderText = "A", FooterText = "B" });
            await repo.SaveAsync(new ReceiptConfig { PosCd = TestPosCd, HeaderText = "C", FooterText = "D" });

            var result = await repo.GetAsync(TestPosCd);

            Assert.Equal("C", result!.HeaderText);
            Assert.Equal("D", result.FooterText);
        }
        finally
        {
            await conn.ExecuteAsync("DELETE FROM receipt_config_tb WHERE pos_cd = @PosCd", new { PosCd = TestPosCd });
        }
    }
}
```

`tests/FishingMartPos.Tests/Repositories/SystemInfoRepositoryTests.cs`:
```csharp
using FishingMartPos.Configuration;
using FishingMartPos.Data;
using FishingMartPos.Repositories;
using Xunit;

namespace FishingMartPos.Tests.Repositories;

public class SystemInfoRepositoryTests
{
    [Fact]
    public async Task GetAsync_ReturnsSeededRow()
    {
        var config = AppConfig.Load(AppContext.BaseDirectory);
        var factory = new MySqlConnectionFactory(config);
        ISystemInfoRepository repository = new SystemInfoRepository(factory);

        var result = await repository.GetAsync();

        Assert.NotNull(result);
        Assert.False(string.IsNullOrWhiteSpace(result!.AppVersion));
        Assert.False(string.IsNullOrWhiteSpace(result.DbVersion));
    }
}
```

- [ ] **Step 5: 테스트 실행 — 실패 확인 후 통과 확인**

Run: `dotnet test tests/FishingMartPos.Tests --filter "PrinterConfigRepositoryTests|ReceiptConfigRepositoryTests|SystemInfoRepositoryTests"`
Expected (Step 1-3 완료 전): 컴파일 실패(타입 없음). Step 1-3 완료 후 재실행: PASS.

- [ ] **Step 6: App.xaml.cs에 DI 등록 추가 (사용처는 아직 없음 — 등록만)**

`src/FishingMartPos/App.xaml.cs`에서 `services.AddSingleton<ICodeRepository, CodeRepository>();` 줄 바로 다음에 아래 3줄을 추가한다:

```csharp
        services.AddSingleton<IPrinterConfigRepository, PrinterConfigRepository>();
        services.AddSingleton<IReceiptConfigRepository, ReceiptConfigRepository>();
        services.AddSingleton<ISystemInfoRepository, SystemInfoRepository>();
```

(이 시점에는 아무도 `GetRequiredService<IPrinterConfigRepository>()` 등을 호출하지 않으므로, 등록만 추가해도 빌드는 그대로 통과한다. 실제 사용은 Task 6에서 연결한다.)

- [ ] **Step 7: 빌드 확인 + 커밋**

Run: `dotnet build`
Expected: 빌드 성공(경고 없이).

```bash
git add src/FishingMartPos/Models/PrinterConfig.cs src/FishingMartPos/Models/ReceiptConfig.cs src/FishingMartPos/Models/SystemInfo.cs src/FishingMartPos/Repositories/IPrinterConfigRepository.cs src/FishingMartPos/Repositories/PrinterConfigRepository.cs src/FishingMartPos/Repositories/IReceiptConfigRepository.cs src/FishingMartPos/Repositories/ReceiptConfigRepository.cs src/FishingMartPos/Repositories/ISystemInfoRepository.cs src/FishingMartPos/Repositories/SystemInfoRepository.cs src/FishingMartPos/App.xaml.cs tests/FishingMartPos.Tests/Fakes/FakePrinterConfigRepository.cs tests/FishingMartPos.Tests/Fakes/FakeReceiptConfigRepository.cs tests/FishingMartPos.Tests/Fakes/FakeSystemInfoRepository.cs tests/FishingMartPos.Tests/Repositories/PrinterConfigRepositoryTests.cs tests/FishingMartPos.Tests/Repositories/ReceiptConfigRepositoryTests.cs tests/FishingMartPos.Tests/Repositories/SystemInfoRepositoryTests.cs
git commit -m "프린터/영수증/시스템정보 모델·리포지토리 추가 (환경설정 하위화면 준비)"
```

---

## Task 3: 직원관리 ViewModel (StaffList/StaffForm/StaffRow/RoleOption)

**Files:**
- Create: `src/FishingMartPos/ViewModels/RoleOptionViewModel.cs`
- Create: `src/FishingMartPos/ViewModels/StaffRowViewModel.cs`
- Create: `src/FishingMartPos/ViewModels/StaffListViewModel.cs`
- Create: `src/FishingMartPos/ViewModels/StaffFormViewModel.cs`
- Create: `tests/FishingMartPos.Tests/ViewModels/StaffListViewModelTests.cs`
- Create: `tests/FishingMartPos.Tests/ViewModels/StaffFormViewModelTests.cs`

**Interfaces:**
- Consumes: `IStaffRepository`(Task 1의 `GetAllAsync`/`CreateAsync`/`UpdateAsync`), `INavigationService`, `IDelayProvider`, `SettingsViewModel`(Task 6에서 생성 — 이 태스크에서는 생성자 타입으로만 참조; `SettingsViewModel`이 아직 없으므로 이 태스크의 코드는 Task 6 완료 전까지는 컴파일되지 않는다. **이 태스크를 Task 6보다 먼저 진행하려면, 이 Step 1에서 `SettingsViewModel`의 최소 골격(생성자 `(INavigationService, MainMenuViewModel)`만 있는 빈 클래스)을 먼저 만들어야 한다 — 아래 Step 0 참고.**
- Produces: `StaffListViewModel { Rows: ObservableCollection<StaffRowViewModel>, StaffFormViewModelFactory: Func<StaffListViewModel, Staff?, Task<StaffFormViewModel>>?, LoadAsync(), GoToAddStaffCommand, GoToSettingsCommand }`.
- Produces: `StaffFormViewModel { StaffCode, StaffName, SelectedRole: RoleOptionViewModel, IsActive, PinInput, ErrorMessage, ToastMessage, IsEditMode, HeaderText, SaveButtonText, RoleOptions, LoadAsync(), SaveCommand, CancelCommand }`.

- [ ] **Step 0: SettingsViewModel 최소 골격 선(先) 생성**

이후 태스크(Task 3~5)들이 `SettingsViewModel`을 생성자 파라미터 타입으로 참조하므로, Task 6에서 완성할 전체 기능(5개 팩토리 프로퍼티 + 5개 GoTo 커맨드)은 아직 없이 최소 골격만 지금 만든다. Task 6에서 이 파일을 완성된 버전으로 교체한다.

`src/FishingMartPos/ViewModels/SettingsViewModel.cs`:
```csharp
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FishingMartPos.Navigation;

namespace FishingMartPos.ViewModels;

public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly INavigationService _navigation;
    private readonly MainMenuViewModel _returnTo;

    public SettingsViewModel(INavigationService navigation, MainMenuViewModel returnTo)
    {
        _navigation = navigation;
        _returnTo = returnTo;
    }

    [RelayCommand]
    private void GoToMainMenu() => _navigation.NavigateTo(_returnTo);
}
```

Run: `dotnet build` — Expected: 빌드 성공.

```bash
git add src/FishingMartPos/ViewModels/SettingsViewModel.cs
git commit -m "SettingsViewModel 최소 골격 추가 (하위화면 태스크들의 생성자 의존성 해소)"
```

- [ ] **Step 1: 실패하는 테스트 작성**

`src/FishingMartPos/ViewModels/RoleOptionViewModel.cs`가 아직 없으므로 아래 테스트는 컴파일되지 않는다 — 이는 의도된 것이다(Step 2에서 구현).

`tests/FishingMartPos.Tests/ViewModels/StaffListViewModelTests.cs`:
```csharp
using FishingMartPos.Models;
using FishingMartPos.Navigation;
using FishingMartPos.Services;
using FishingMartPos.Tests.Fakes;
using FishingMartPos.ViewModels;
using Xunit;

namespace FishingMartPos.Tests.ViewModels;

public class StaffListViewModelTests
{
    private static (StaffListViewModel vm, FakeStaffRepository staff, INavigationService navigation, SettingsViewModel settings)
        Create()
    {
        var staff = new FakeStaffRepository(new Dictionary<string, Staff>
        {
            ["0000"] = new() { StaffCode = "ADMIN1", StaffName = "관리자", Role = "ADMIN", UseYn = "Y" },
        });
        var navigation = new NavigationService();
        var settings = new SettingsViewModel(navigation, new MainMenuViewModel(new CurrentSession(), navigation));
        var vm = new StaffListViewModel(staff, navigation, settings)
        {
            StaffFormViewModelFactory = (list, editing) =>
                Task.FromResult(new StaffFormViewModel(staff, new FakeDelayProvider(), navigation, list, editing)),
        };
        return (vm, staff, navigation, settings);
    }

    [Fact]
    public async Task LoadAsync_PopulatesRowsFromRepository()
    {
        var (vm, _, _, _) = Create();

        await vm.LoadAsync();

        var row = Assert.Single(vm.Rows);
        Assert.Equal("ADMIN1", row.StaffCode);
        Assert.Equal("관리자", row.RoleName);
        Assert.Equal("사용", row.UseYnLabel);
    }

    [Fact]
    public async Task GoToAddStaff_NavigatesToStaffFormViewModelInAddMode()
    {
        var (vm, _, navigation, _) = Create();
        await vm.LoadAsync();

        await vm.GoToAddStaffCommand.ExecuteAsync(null);

        var form = Assert.IsType<StaffFormViewModel>(navigation.CurrentViewModel);
        Assert.False(form.IsEditMode);
    }

    [Fact]
    public async Task RowEditCommand_NavigatesToStaffFormViewModelInEditModeForThatStaff()
    {
        var (vm, _, navigation, _) = Create();
        await vm.LoadAsync();
        var row = vm.Rows[0];

        row.EditCommand.Execute(null);
        await Task.Delay(1);

        var form = Assert.IsType<StaffFormViewModel>(navigation.CurrentViewModel);
        Assert.True(form.IsEditMode);
        Assert.Equal("ADMIN1", form.StaffCode);
    }

    [Fact]
    public void GoToSettings_NavigatesBackToSettingsViewModel()
    {
        var (vm, _, navigation, settings) = Create();

        vm.GoToSettingsCommand.Execute(null);

        Assert.Same(settings, navigation.CurrentViewModel);
    }
}
```

`tests/FishingMartPos.Tests/ViewModels/StaffFormViewModelTests.cs`:
```csharp
using FishingMartPos.Models;
using FishingMartPos.Navigation;
using FishingMartPos.Services;
using FishingMartPos.Tests.Fakes;
using FishingMartPos.ViewModels;
using Xunit;

namespace FishingMartPos.Tests.ViewModels;

public class StaffFormViewModelTests
{
    private static (StaffFormViewModel vm, FakeStaffRepository staff, INavigationService navigation, StaffListViewModel listVm)
        CreateForAdd()
    {
        var staff = new FakeStaffRepository(new Dictionary<string, Staff>
        {
            ["0000"] = new() { StaffCode = "ADMIN1", StaffName = "관리자", Role = "ADMIN", UseYn = "Y" },
        });
        var navigation = new NavigationService();
        var settings = new SettingsViewModel(navigation, new MainMenuViewModel(new CurrentSession(), navigation));
        var listVm = new StaffListViewModel(staff, navigation, settings);
        var vm = new StaffFormViewModel(staff, new FakeDelayProvider(), navigation, listVm, editingStaff: null);
        return (vm, staff, navigation, listVm);
    }

    private static (StaffFormViewModel vm, FakeStaffRepository staff, INavigationService navigation, StaffListViewModel listVm)
        CreateForEdit(Staff editing)
    {
        var staff = new FakeStaffRepository(new Dictionary<string, Staff> { ["0000"] = editing });
        var navigation = new NavigationService();
        var settings = new SettingsViewModel(navigation, new MainMenuViewModel(new CurrentSession(), navigation));
        var listVm = new StaffListViewModel(staff, navigation, settings);
        var vm = new StaffFormViewModel(staff, new FakeDelayProvider(), navigation, listVm, editingStaff: editing);
        return (vm, staff, navigation, listVm);
    }

    [Fact]
    public async Task LoadAsync_AddMode_StartsWithEmptyFormAndStaffRoleSelected()
    {
        var (vm, _, _, _) = CreateForAdd();

        await vm.LoadAsync();

        Assert.False(vm.IsEditMode);
        Assert.Equal(string.Empty, vm.StaffCode);
        Assert.Equal("STAFF", vm.SelectedRole.Code);
        Assert.True(vm.IsActive);
    }

    [Fact]
    public async Task LoadAsync_EditMode_FillsFormFromExistingStaff()
    {
        var editing = new Staff { StaffCode = "STAFF1", StaffName = "김직원", Role = "STAFF", UseYn = "N" };
        var (vm, _, _, _) = CreateForEdit(editing);

        await vm.LoadAsync();

        Assert.True(vm.IsEditMode);
        Assert.Equal("STAFF1", vm.StaffCode);
        Assert.Equal("김직원", vm.StaffName);
        Assert.Equal("STAFF", vm.SelectedRole.Code);
        Assert.False(vm.IsActive);
    }

    [Fact]
    public async Task Save_AddMode_WithoutStaffCode_ShowsErrorAndDoesNotCreate()
    {
        var (vm, staff, _, _) = CreateForAdd();
        await vm.LoadAsync();
        vm.StaffName = "김신입";
        vm.PinInput = "1234";

        await vm.SaveCommand.ExecuteAsync(null);

        Assert.NotNull(vm.ErrorMessage);
        Assert.Empty(staff.CreatedStaff);
    }

    [Fact]
    public async Task Save_AddMode_WithoutValidPin_ShowsErrorAndDoesNotCreate()
    {
        var (vm, staff, _, _) = CreateForAdd();
        await vm.LoadAsync();
        vm.StaffCode = "STAFF2";
        vm.StaffName = "김신입";
        vm.PinInput = "12";

        await vm.SaveCommand.ExecuteAsync(null);

        Assert.NotNull(vm.ErrorMessage);
        Assert.Empty(staff.CreatedStaff);
    }

    [Fact]
    public async Task Save_AddMode_WithDuplicateStaffCode_ShowsErrorAndDoesNotCreate()
    {
        var (vm, staff, _, _) = CreateForAdd();
        await vm.LoadAsync();
        vm.StaffCode = "ADMIN1";
        vm.StaffName = "김신입";
        vm.PinInput = "1234";

        await vm.SaveCommand.ExecuteAsync(null);

        Assert.Equal("이미 등록된 직원코드입니다", vm.ErrorMessage);
        Assert.Empty(staff.CreatedStaff);
    }

    [Fact]
    public async Task Save_AddMode_WithValidInput_CreatesStaffAndNavigatesBack()
    {
        var (vm, staff, navigation, listVm) = CreateForAdd();
        await vm.LoadAsync();
        vm.StaffCode = "STAFF2";
        vm.StaffName = "김신입";
        vm.PinInput = "1234";

        await vm.SaveCommand.ExecuteAsync(null);

        var created = Assert.Single(staff.CreatedStaff);
        Assert.Equal("STAFF2", created.Staff.StaffCode);
        Assert.Equal("1234", created.Pin);
        Assert.Same(listVm, navigation.CurrentViewModel);
    }

    [Fact]
    public async Task Save_EditMode_WithBlankPin_UpdatesWithoutChangingPin()
    {
        var editing = new Staff { StaffCode = "STAFF1", StaffName = "김직원", Role = "STAFF", UseYn = "Y" };
        var (vm, staff, _, _) = CreateForEdit(editing);
        await vm.LoadAsync();
        vm.StaffName = "김직원2";

        await vm.SaveCommand.ExecuteAsync(null);

        var updated = Assert.Single(staff.UpdatedStaff);
        Assert.Equal("김직원2", updated.Staff.StaffName);
        Assert.Null(updated.NewPin);
    }

    [Fact]
    public async Task Save_EditMode_WithInvalidPin_ShowsErrorAndDoesNotUpdate()
    {
        var editing = new Staff { StaffCode = "STAFF1", StaffName = "김직원", Role = "STAFF", UseYn = "Y" };
        var (vm, staff, _, _) = CreateForEdit(editing);
        await vm.LoadAsync();
        vm.PinInput = "12";

        await vm.SaveCommand.ExecuteAsync(null);

        Assert.NotNull(vm.ErrorMessage);
        Assert.Empty(staff.UpdatedStaff);
    }

    [Fact]
    public async Task Save_EditMode_WithNewValidPin_UpdatesPin()
    {
        var editing = new Staff { StaffCode = "STAFF1", StaffName = "김직원", Role = "STAFF", UseYn = "Y" };
        var (vm, staff, _, _) = CreateForEdit(editing);
        await vm.LoadAsync();
        vm.PinInput = "5678";

        await vm.SaveCommand.ExecuteAsync(null);

        var updated = Assert.Single(staff.UpdatedStaff);
        Assert.Equal("5678", updated.NewPin);
    }

    [Fact]
    public async Task Save_AddMode_ShowsRegisteredToast()
    {
        var (vm, _, _, _) = CreateForAdd();
        await vm.LoadAsync();
        vm.StaffCode = "STAFF2";
        vm.StaffName = "김신입";
        vm.PinInput = "1234";
        var toastValues = new List<string?>();
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(StaffFormViewModel.ToastMessage) && vm.ToastMessage is not null)
                toastValues.Add(vm.ToastMessage);
        };

        await vm.SaveCommand.ExecuteAsync(null);

        Assert.Equal("등록되었습니다", Assert.Single(toastValues));
    }

    [Fact]
    public async Task Cancel_NavigatesBackToStaffList()
    {
        var (vm, _, navigation, listVm) = CreateForAdd();
        await vm.LoadAsync();

        await vm.CancelCommand.ExecuteAsync(null);

        Assert.Same(listVm, navigation.CurrentViewModel);
    }
}
```

- [ ] **Step 2: 테스트 실행 — 컴파일 실패 확인**

Run: `dotnet test tests/FishingMartPos.Tests --filter "StaffListViewModelTests|StaffFormViewModelTests"`
Expected: FAIL to compile — `RoleOptionViewModel`/`StaffRowViewModel`/`StaffListViewModel`/`StaffFormViewModel`이 존재하지 않음.

- [ ] **Step 3: ViewModel 구현**

`src/FishingMartPos/ViewModels/RoleOptionViewModel.cs`:
```csharp
namespace FishingMartPos.ViewModels;

public sealed class RoleOptionViewModel
{
    public required string Code { get; init; }
    public required string Name { get; init; }
}
```

`src/FishingMartPos/ViewModels/StaffRowViewModel.cs`:
```csharp
using System.Windows.Input;

namespace FishingMartPos.ViewModels;

public sealed class StaffRowViewModel
{
    public required string StaffCode { get; init; }
    public required string StaffName { get; init; }
    public required string RoleName { get; init; }
    public required string UseYnLabel { get; init; }
    public required ICommand EditCommand { get; init; }
}
```

`src/FishingMartPos/ViewModels/StaffListViewModel.cs`:
```csharp
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FishingMartPos.Models;
using FishingMartPos.Navigation;
using FishingMartPos.Repositories;

namespace FishingMartPos.ViewModels;

public sealed partial class StaffListViewModel : ObservableObject
{
    private readonly IStaffRepository _staffRepository;
    private readonly INavigationService _navigation;
    private readonly SettingsViewModel _returnTo;

    public ObservableCollection<StaffRowViewModel> Rows { get; } = new();

    /// <summary>App.xaml.cs(또는 테스트)에서 주입하는 StaffFormViewModel 팩토리 — "+ 직원등록"/행별 "수정" 진입 시 사용. staff가 null이면 등록 모드, 있으면 수정 모드.</summary>
    public Func<StaffListViewModel, Staff?, Task<StaffFormViewModel>>? StaffFormViewModelFactory { get; set; }

    public StaffListViewModel(IStaffRepository staffRepository, INavigationService navigation, SettingsViewModel returnTo)
    {
        _staffRepository = staffRepository;
        _navigation = navigation;
        _returnTo = returnTo;
    }

    public async Task LoadAsync()
    {
        var staffList = await _staffRepository.GetAllAsync();

        Rows.Clear();
        foreach (var staff in staffList)
        {
            var captured = staff;
            Rows.Add(new StaffRowViewModel
            {
                StaffCode = captured.StaffCode,
                StaffName = captured.StaffName,
                RoleName = captured.Role == "ADMIN" ? "관리자" : "직원",
                UseYnLabel = captured.UseYn == "Y" ? "사용" : "미사용",
                EditCommand = new AsyncRelayCommand(() => GoToEditStaff(captured)),
            });
        }
    }

    [RelayCommand]
    private async Task GoToAddStaff()
    {
        var formVm = await StaffFormViewModelFactory!.Invoke(this, null);
        await formVm.LoadAsync();
        _navigation.NavigateTo(formVm);
    }

    private async Task GoToEditStaff(Staff staff)
    {
        var formVm = await StaffFormViewModelFactory!.Invoke(this, staff);
        await formVm.LoadAsync();
        _navigation.NavigateTo(formVm);
    }

    [RelayCommand]
    private void GoToSettings() => _navigation.NavigateTo(_returnTo);
}
```

`src/FishingMartPos/ViewModels/StaffFormViewModel.cs`:
```csharp
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FishingMartPos.Models;
using FishingMartPos.Navigation;
using FishingMartPos.Repositories;
using FishingMartPos.Services;

namespace FishingMartPos.ViewModels;

public sealed partial class StaffFormViewModel : ObservableObject
{
    private readonly IStaffRepository _staffRepository;
    private readonly IDelayProvider _delay;
    private readonly INavigationService _navigation;
    private readonly StaffListViewModel _returnTo;
    private readonly string? _editingStaffCode;

    private IReadOnlyList<Staff> _allStaff = Array.Empty<Staff>();

    [ObservableProperty] private string _staffCode = string.Empty;
    [ObservableProperty] private string _staffName = string.Empty;
    [ObservableProperty] private RoleOptionViewModel _selectedRole = new() { Code = "STAFF", Name = "직원" };
    [ObservableProperty] private bool _isActive = true;
    [ObservableProperty] private string _pinInput = string.Empty;
    [ObservableProperty] private string? _errorMessage;
    [ObservableProperty] private string? _toastMessage;

    public IReadOnlyList<RoleOptionViewModel> RoleOptions { get; } = new[]
    {
        new RoleOptionViewModel { Code = "ADMIN", Name = "관리자" },
        new RoleOptionViewModel { Code = "STAFF", Name = "직원" },
    };

    public bool IsEditMode => _editingStaffCode is not null;
    public string HeaderText => IsEditMode ? "직원수정" : "직원등록";
    public string SaveButtonText => IsEditMode ? "저장하기" : "등록하기";

    public StaffFormViewModel(
        IStaffRepository staffRepository,
        IDelayProvider delay,
        INavigationService navigation,
        StaffListViewModel returnTo,
        Staff? editingStaff)
    {
        _staffRepository = staffRepository;
        _delay = delay;
        _navigation = navigation;
        _returnTo = returnTo;
        _editingStaffCode = editingStaff?.StaffCode;

        if (editingStaff is not null)
        {
            StaffCode = editingStaff.StaffCode;
            StaffName = editingStaff.StaffName;
            SelectedRole = RoleOptions.First(r => r.Code == editingStaff.Role);
            IsActive = editingStaff.UseYn == "Y";
        }
    }

    public async Task LoadAsync()
    {
        _allStaff = await _staffRepository.GetAllAsync();
    }

    [RelayCommand]
    private async Task Save()
    {
        ErrorMessage = null;

        if (string.IsNullOrWhiteSpace(StaffCode))
        {
            ErrorMessage = "직원코드를 입력해주세요";
            return;
        }

        if (string.IsNullOrWhiteSpace(StaffName))
        {
            ErrorMessage = "이름을 입력해주세요";
            return;
        }

        if (!IsEditMode && _allStaff.Any(s => s.StaffCode == StaffCode))
        {
            ErrorMessage = "이미 등록된 직원코드입니다";
            return;
        }

        bool isNewPinProvided = !string.IsNullOrWhiteSpace(PinInput);
        bool pinRequiredNow = !IsEditMode || isNewPinProvided;
        if (pinRequiredNow && !IsValidPin(PinInput))
        {
            ErrorMessage = "PIN은 4자리 숫자로 입력해주세요";
            return;
        }

        var staff = new Staff
        {
            StaffCode = StaffCode,
            StaffName = StaffName,
            Role = SelectedRole.Code,
            UseYn = IsActive ? "Y" : "N",
        };

        if (IsEditMode)
        {
            await _staffRepository.UpdateAsync(staff, isNewPinProvided ? PinInput : null);
        }
        else
        {
            await _staffRepository.CreateAsync(staff, PinInput);
        }

        ToastMessage = IsEditMode ? "수정되었습니다" : "등록되었습니다";
        await _delay.Delay(TimeSpan.FromMilliseconds(1200));
        ToastMessage = null;

        await GoBackToStaffListAsync();
    }

    [RelayCommand]
    private async Task Cancel() => await GoBackToStaffListAsync();

    private async Task GoBackToStaffListAsync()
    {
        await _returnTo.LoadAsync();
        _navigation.NavigateTo(_returnTo);
    }

    private static bool IsValidPin(string pin) => pin.Length == 4 && pin.All(char.IsDigit);
}
```

- [ ] **Step 4: 테스트 재실행 — 통과 확인**

Run: `dotnet test tests/FishingMartPos.Tests --filter "StaffListViewModelTests|StaffFormViewModelTests"`
Expected: PASS (all tests). These are pure ViewModel unit tests using Fakes — no DB required.

- [ ] **Step 5: 커밋**

```bash
git add src/FishingMartPos/ViewModels/RoleOptionViewModel.cs src/FishingMartPos/ViewModels/StaffRowViewModel.cs src/FishingMartPos/ViewModels/StaffListViewModel.cs src/FishingMartPos/ViewModels/StaffFormViewModel.cs tests/FishingMartPos.Tests/ViewModels/StaffListViewModelTests.cs tests/FishingMartPos.Tests/ViewModels/StaffFormViewModelTests.cs
git commit -m "직원관리 목록/폼 ViewModel 추가"
```

---

## Task 4: 프린터설정/영수증설정 ViewModel

**Files:**
- Create: `src/FishingMartPos/ViewModels/PrinterSettingsViewModel.cs`
- Create: `src/FishingMartPos/ViewModels/ReceiptSettingsViewModel.cs`
- Create: `tests/FishingMartPos.Tests/ViewModels/PrinterSettingsViewModelTests.cs`
- Create: `tests/FishingMartPos.Tests/ViewModels/ReceiptSettingsViewModelTests.cs`

**Interfaces:**
- Consumes: `IPrinterConfigRepository`/`IReceiptConfigRepository`(Task 2), `ICurrentSession`(`CurrentTerminal!.PosCode`), `IDelayProvider`, `INavigationService`, `SettingsViewModel`(Task 3의 Step 0에서 만든 골격).
- Produces: `PrinterSettingsViewModel { PrinterPort, PrinterName, DrawerKickEnabled, ToastMessage, LoadAsync(), SaveCommand, TestPrintCommand, GoToSettingsCommand }`.
- Produces: `ReceiptSettingsViewModel { HeaderText, FooterText, ToastMessage, LoadAsync(), SaveCommand, GoToSettingsCommand }`.

- [ ] **Step 1: 실패하는 테스트 작성**

`tests/FishingMartPos.Tests/ViewModels/PrinterSettingsViewModelTests.cs`:
```csharp
using FishingMartPos.Models;
using FishingMartPos.Navigation;
using FishingMartPos.Services;
using FishingMartPos.Tests.Fakes;
using FishingMartPos.ViewModels;
using Xunit;

namespace FishingMartPos.Tests.ViewModels;

public class PrinterSettingsViewModelTests
{
    private static (PrinterSettingsViewModel vm, FakePrinterConfigRepository repo, INavigationService navigation, SettingsViewModel settings)
        Create(PrinterConfig? existing = null)
    {
        var byPosCd = new Dictionary<string, PrinterConfig>();
        if (existing is not null) byPosCd[existing.PosCd] = existing;
        var repo = new FakePrinterConfigRepository(byPosCd);
        var session = new CurrentSession();
        session.SignIn(
            new Staff { StaffCode = "ADMIN1", StaffName = "관리자", Role = "ADMIN", UseYn = "Y" },
            new PosTerminal { PosCode = "1", PosName = "POS1" });
        var navigation = new NavigationService();
        var settings = new SettingsViewModel(navigation, new MainMenuViewModel(session, navigation));
        var vm = new PrinterSettingsViewModel(repo, session, new FakeDelayProvider(), navigation, settings);
        return (vm, repo, navigation, settings);
    }

    [Fact]
    public async Task LoadAsync_WhenNoConfigExists_UsesDefaults()
    {
        var (vm, _, _, _) = Create();

        await vm.LoadAsync();

        Assert.Equal(string.Empty, vm.PrinterPort);
        Assert.True(vm.DrawerKickEnabled);
    }

    [Fact]
    public async Task LoadAsync_WhenConfigExists_FillsFormFromRepository()
    {
        var existing = new PrinterConfig { PosCd = "1", PrinterPort = "COM3", PrinterName = "EPSON", DrawerKickEnabled = false };
        var (vm, _, _, _) = Create(existing);

        await vm.LoadAsync();

        Assert.Equal("COM3", vm.PrinterPort);
        Assert.Equal("EPSON", vm.PrinterName);
        Assert.False(vm.DrawerKickEnabled);
    }

    [Fact]
    public async Task Save_PersistsCurrentValuesForCurrentTerminal()
    {
        var (vm, repo, _, _) = Create();
        await vm.LoadAsync();
        vm.PrinterPort = "COM5";
        vm.PrinterName = "STAR";
        vm.DrawerKickEnabled = false;

        await vm.SaveCommand.ExecuteAsync(null);

        var saved = Assert.Single(repo.SavedConfigs);
        Assert.Equal("1", saved.PosCd);
        Assert.Equal("COM5", saved.PrinterPort);
        Assert.False(saved.DrawerKickEnabled);
    }

    [Fact]
    public async Task Save_ShowsSavedToast()
    {
        var (vm, _, _, _) = Create();
        await vm.LoadAsync();
        var toastValues = new List<string?>();
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(PrinterSettingsViewModel.ToastMessage) && vm.ToastMessage is not null)
                toastValues.Add(vm.ToastMessage);
        };

        await vm.SaveCommand.ExecuteAsync(null);

        Assert.Equal("저장되었습니다", Assert.Single(toastValues));
    }

    [Fact]
    public async Task TestPrint_ShowsNotSupportedYetToast()
    {
        var (vm, _, _, _) = Create();
        await vm.LoadAsync();
        var toastValues = new List<string?>();
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(PrinterSettingsViewModel.ToastMessage) && vm.ToastMessage is not null)
                toastValues.Add(vm.ToastMessage);
        };

        await vm.TestPrintCommand.ExecuteAsync(null);

        Assert.Equal("프린터 연동은 지원 예정입니다", Assert.Single(toastValues));
    }

    [Fact]
    public void GoToSettings_NavigatesBackToSettingsViewModel()
    {
        var (vm, _, navigation, settings) = Create();

        vm.GoToSettingsCommand.Execute(null);

        Assert.Same(settings, navigation.CurrentViewModel);
    }
}
```

`tests/FishingMartPos.Tests/ViewModels/ReceiptSettingsViewModelTests.cs`:
```csharp
using FishingMartPos.Models;
using FishingMartPos.Navigation;
using FishingMartPos.Services;
using FishingMartPos.Tests.Fakes;
using FishingMartPos.ViewModels;
using Xunit;

namespace FishingMartPos.Tests.ViewModels;

public class ReceiptSettingsViewModelTests
{
    private static (ReceiptSettingsViewModel vm, FakeReceiptConfigRepository repo, INavigationService navigation, SettingsViewModel settings)
        Create(ReceiptConfig? existing = null)
    {
        var byPosCd = new Dictionary<string, ReceiptConfig>();
        if (existing is not null) byPosCd[existing.PosCd] = existing;
        var repo = new FakeReceiptConfigRepository(byPosCd);
        var session = new CurrentSession();
        session.SignIn(
            new Staff { StaffCode = "ADMIN1", StaffName = "관리자", Role = "ADMIN", UseYn = "Y" },
            new PosTerminal { PosCode = "1", PosName = "POS1" });
        var navigation = new NavigationService();
        var settings = new SettingsViewModel(navigation, new MainMenuViewModel(session, navigation));
        var vm = new ReceiptSettingsViewModel(repo, session, new FakeDelayProvider(), navigation, settings);
        return (vm, repo, navigation, settings);
    }

    [Fact]
    public async Task LoadAsync_WhenNoConfigExists_UsesEmptyDefaults()
    {
        var (vm, _, _, _) = Create();

        await vm.LoadAsync();

        Assert.Equal(string.Empty, vm.HeaderText);
        Assert.Equal(string.Empty, vm.FooterText);
    }

    [Fact]
    public async Task LoadAsync_WhenConfigExists_FillsFormFromRepository()
    {
        var existing = new ReceiptConfig { PosCd = "1", HeaderText = "환영합니다", FooterText = "감사합니다" };
        var (vm, _, _, _) = Create(existing);

        await vm.LoadAsync();

        Assert.Equal("환영합니다", vm.HeaderText);
        Assert.Equal("감사합니다", vm.FooterText);
    }

    [Fact]
    public async Task Save_PersistsCurrentValuesForCurrentTerminal()
    {
        var (vm, repo, _, _) = Create();
        await vm.LoadAsync();
        vm.HeaderText = "새 상단문구";
        vm.FooterText = "새 하단문구";

        await vm.SaveCommand.ExecuteAsync(null);

        var saved = Assert.Single(repo.SavedConfigs);
        Assert.Equal("1", saved.PosCd);
        Assert.Equal("새 상단문구", saved.HeaderText);
        Assert.Equal("새 하단문구", saved.FooterText);
    }

    [Fact]
    public async Task Save_ShowsSavedToast()
    {
        var (vm, _, _, _) = Create();
        await vm.LoadAsync();
        var toastValues = new List<string?>();
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ReceiptSettingsViewModel.ToastMessage) && vm.ToastMessage is not null)
                toastValues.Add(vm.ToastMessage);
        };

        await vm.SaveCommand.ExecuteAsync(null);

        Assert.Equal("저장되었습니다", Assert.Single(toastValues));
    }

    [Fact]
    public void GoToSettings_NavigatesBackToSettingsViewModel()
    {
        var (vm, _, navigation, settings) = Create();

        vm.GoToSettingsCommand.Execute(null);

        Assert.Same(settings, navigation.CurrentViewModel);
    }
}
```

- [ ] **Step 2: 테스트 실행 — 컴파일 실패 확인**

Run: `dotnet test tests/FishingMartPos.Tests --filter "PrinterSettingsViewModelTests|ReceiptSettingsViewModelTests"`
Expected: FAIL to compile — `PrinterSettingsViewModel`/`ReceiptSettingsViewModel`이 존재하지 않음.

- [ ] **Step 3: ViewModel 구현**

`src/FishingMartPos/ViewModels/PrinterSettingsViewModel.cs`:
```csharp
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FishingMartPos.Models;
using FishingMartPos.Navigation;
using FishingMartPos.Repositories;
using FishingMartPos.Services;

namespace FishingMartPos.ViewModels;

public sealed partial class PrinterSettingsViewModel : ObservableObject
{
    private readonly IPrinterConfigRepository _printerConfigRepository;
    private readonly ICurrentSession _session;
    private readonly IDelayProvider _delay;
    private readonly INavigationService _navigation;
    private readonly SettingsViewModel _returnTo;

    [ObservableProperty] private string _printerPort = string.Empty;
    [ObservableProperty] private string _printerName = string.Empty;
    [ObservableProperty] private bool _drawerKickEnabled = true;
    [ObservableProperty] private string? _toastMessage;

    public PrinterSettingsViewModel(
        IPrinterConfigRepository printerConfigRepository,
        ICurrentSession session,
        IDelayProvider delay,
        INavigationService navigation,
        SettingsViewModel returnTo)
    {
        _printerConfigRepository = printerConfigRepository;
        _session = session;
        _delay = delay;
        _navigation = navigation;
        _returnTo = returnTo;
    }

    public async Task LoadAsync()
    {
        string posCd = _session.CurrentTerminal!.PosCode;
        var config = await _printerConfigRepository.GetAsync(posCd);

        PrinterPort = config?.PrinterPort ?? string.Empty;
        PrinterName = config?.PrinterName ?? string.Empty;
        DrawerKickEnabled = config?.DrawerKickEnabled ?? true;
    }

    [RelayCommand]
    private async Task Save()
    {
        string posCd = _session.CurrentTerminal!.PosCode;
        await _printerConfigRepository.SaveAsync(new PrinterConfig
        {
            PosCd = posCd,
            PrinterPort = PrinterPort,
            PrinterName = PrinterName,
            DrawerKickEnabled = DrawerKickEnabled,
        });

        ToastMessage = "저장되었습니다";
        await _delay.Delay(TimeSpan.FromMilliseconds(1200));
        ToastMessage = null;
    }

    [RelayCommand]
    private async Task TestPrint()
    {
        ToastMessage = "프린터 연동은 지원 예정입니다";
        await _delay.Delay(TimeSpan.FromMilliseconds(1200));
        ToastMessage = null;
    }

    [RelayCommand]
    private void GoToSettings() => _navigation.NavigateTo(_returnTo);
}
```

`src/FishingMartPos/ViewModels/ReceiptSettingsViewModel.cs`:
```csharp
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FishingMartPos.Models;
using FishingMartPos.Navigation;
using FishingMartPos.Repositories;
using FishingMartPos.Services;

namespace FishingMartPos.ViewModels;

public sealed partial class ReceiptSettingsViewModel : ObservableObject
{
    private readonly IReceiptConfigRepository _receiptConfigRepository;
    private readonly ICurrentSession _session;
    private readonly IDelayProvider _delay;
    private readonly INavigationService _navigation;
    private readonly SettingsViewModel _returnTo;

    [ObservableProperty] private string _headerText = string.Empty;
    [ObservableProperty] private string _footerText = string.Empty;
    [ObservableProperty] private string? _toastMessage;

    public ReceiptSettingsViewModel(
        IReceiptConfigRepository receiptConfigRepository,
        ICurrentSession session,
        IDelayProvider delay,
        INavigationService navigation,
        SettingsViewModel returnTo)
    {
        _receiptConfigRepository = receiptConfigRepository;
        _session = session;
        _delay = delay;
        _navigation = navigation;
        _returnTo = returnTo;
    }

    public async Task LoadAsync()
    {
        string posCd = _session.CurrentTerminal!.PosCode;
        var config = await _receiptConfigRepository.GetAsync(posCd);

        HeaderText = config?.HeaderText ?? string.Empty;
        FooterText = config?.FooterText ?? string.Empty;
    }

    [RelayCommand]
    private async Task Save()
    {
        string posCd = _session.CurrentTerminal!.PosCode;
        await _receiptConfigRepository.SaveAsync(new ReceiptConfig
        {
            PosCd = posCd,
            HeaderText = HeaderText,
            FooterText = FooterText,
        });

        ToastMessage = "저장되었습니다";
        await _delay.Delay(TimeSpan.FromMilliseconds(1200));
        ToastMessage = null;
    }

    [RelayCommand]
    private void GoToSettings() => _navigation.NavigateTo(_returnTo);
}
```

- [ ] **Step 4: 테스트 재실행 — 통과 확인**

Run: `dotnet test tests/FishingMartPos.Tests --filter "PrinterSettingsViewModelTests|ReceiptSettingsViewModelTests"`
Expected: PASS (all tests).

- [ ] **Step 5: 커밋**

```bash
git add src/FishingMartPos/ViewModels/PrinterSettingsViewModel.cs src/FishingMartPos/ViewModels/ReceiptSettingsViewModel.cs tests/FishingMartPos.Tests/ViewModels/PrinterSettingsViewModelTests.cs tests/FishingMartPos.Tests/ViewModels/ReceiptSettingsViewModelTests.cs
git commit -m "프린터설정/영수증설정 ViewModel 추가"
```

---

## Task 5: 시스템정보/상품코드관리 ViewModel

**Files:**
- Create: `src/FishingMartPos/ViewModels/SystemInfoViewModel.cs`
- Create: `src/FishingMartPos/ViewModels/CodeManageViewModel.cs`
- Create: `tests/FishingMartPos.Tests/ViewModels/SystemInfoViewModelTests.cs`
- Create: `tests/FishingMartPos.Tests/ViewModels/CodeManageViewModelTests.cs`

**Interfaces:**
- Consumes: `ISystemInfoRepository`(Task 2), `ICurrentSession`, `ICodeRepository`/`IProductRepository`(기존), `INavigationService`, `SettingsViewModel`.
- Produces: `SystemInfoViewModel { AppVersionStr, DbVersionStr, DbConnectionStatusStr, PosTerminalStr, LoadAsync(), GoToSettingsCommand }`.
- Produces: `CodeManageViewModel { MajorCodes/MinorCodes/PosCatCodes: ObservableCollection<CodeItem>, NewMajorCode/NewMajorName/NewMinorCode/NewMinorName/NewPosCatCode/NewPosCatName, ErrorMessage, LoadAsync(), AddMajorCodeCommand, AddMinorCodeCommand, AddPosCatCodeCommand, DeleteMajorCodeCommand, DeleteMinorCodeCommand, DeletePosCatCodeCommand, GoToSettingsCommand }`. **`InventoryFormViewModel`의 코드관리 로직과 의도적으로 코드를 공유하지 않는다(Global Constraints 참고) — 완전히 독립된 구현.**

- [ ] **Step 1: 실패하는 테스트 작성**

`tests/FishingMartPos.Tests/ViewModels/SystemInfoViewModelTests.cs`:
```csharp
using FishingMartPos.Models;
using FishingMartPos.Navigation;
using FishingMartPos.Services;
using FishingMartPos.Tests.Fakes;
using FishingMartPos.ViewModels;
using Xunit;

namespace FishingMartPos.Tests.ViewModels;

public class SystemInfoViewModelTests
{
    private static (SystemInfoViewModel vm, INavigationService navigation, SettingsViewModel settings) Create(FakeSystemInfoRepository repo)
    {
        var session = new CurrentSession();
        session.SignIn(
            new Staff { StaffCode = "ADMIN1", StaffName = "관리자", Role = "ADMIN", UseYn = "Y" },
            new PosTerminal { PosCode = "1", PosName = "POS1" });
        var navigation = new NavigationService();
        var settings = new SettingsViewModel(navigation, new MainMenuViewModel(session, navigation));
        var vm = new SystemInfoViewModel(repo, session, navigation, settings);
        return (vm, navigation, settings);
    }

    [Fact]
    public async Task LoadAsync_WhenRepositorySucceeds_FillsVersionsAndConnectedStatus()
    {
        var repo = new FakeSystemInfoRepository(new FishingMartPos.Models.SystemInfo { AppVersion = "0.1.0", DbVersion = "001" });
        var (vm, _, _) = Create(repo);

        await vm.LoadAsync();

        Assert.Equal("0.1.0", vm.AppVersionStr);
        Assert.Equal("001", vm.DbVersionStr);
        Assert.Equal("연결됨", vm.DbConnectionStatusStr);
        Assert.Equal("POS1 (1)", vm.PosTerminalStr);
    }

    [Fact]
    public async Task LoadAsync_WhenRepositoryThrows_ShowsDisconnectedStatusButStillFillsTerminal()
    {
        var repo = new FakeSystemInfoRepository(exceptionToThrow: new InvalidOperationException("DB down"));
        var (vm, _, _) = Create(repo);

        await vm.LoadAsync();

        Assert.Equal("-", vm.AppVersionStr);
        Assert.Equal("-", vm.DbVersionStr);
        Assert.Equal("연결 안됨", vm.DbConnectionStatusStr);
        Assert.Equal("POS1 (1)", vm.PosTerminalStr);
    }

    [Fact]
    public void GoToSettings_NavigatesBackToSettingsViewModel()
    {
        var repo = new FakeSystemInfoRepository();
        var (vm, navigation, settings) = Create(repo);

        vm.GoToSettingsCommand.Execute(null);

        Assert.Same(settings, navigation.CurrentViewModel);
    }
}
```

`tests/FishingMartPos.Tests/ViewModels/CodeManageViewModelTests.cs`:
```csharp
using FishingMartPos.Models;
using FishingMartPos.Navigation;
using FishingMartPos.Services;
using FishingMartPos.Tests.Fakes;
using FishingMartPos.ViewModels;
using Xunit;

namespace FishingMartPos.Tests.ViewModels;

public class CodeManageViewModelTests
{
    private static Dictionary<string, IReadOnlyList<CodeItem>> SampleCodes() => new()
    {
        ["MAJOR"] = new List<CodeItem> { new() { Code = "FISH", Name = "낚시용품", SortNo = 1 } },
        ["MINOR"] = new List<CodeItem> { new() { Code = "BAIT", Name = "미끼", SortNo = 1 } },
        ["POSCAT"] = new List<CodeItem> { new() { Code = "BAIT", Name = "미끼", SortNo = 1 } },
    };

    private static (CodeManageViewModel vm, FakeCodeRepository codes, FakeProductRepository products, INavigationService navigation, SettingsViewModel settings)
        Create()
    {
        var products = new FakeProductRepository(new List<Product>
        {
            new() { Barcode = "8800000020001", MajorCd = "FISH", MinorCd = "BAIT", PosCatCd = "BAIT", Name = "지렁이", Price = 5000, StockQty = 10 },
        });
        var codes = new FakeCodeRepository(SampleCodes());
        var navigation = new NavigationService();
        var settings = new SettingsViewModel(navigation, new MainMenuViewModel(new CurrentSession(), navigation));
        var vm = new CodeManageViewModel(codes, products, navigation, settings);
        return (vm, codes, products, navigation, settings);
    }

    [Fact]
    public async Task LoadAsync_FillsAllThreeColumnsFromRepository()
    {
        var (vm, _, _, _, _) = Create();

        await vm.LoadAsync();

        Assert.Single(vm.MajorCodes, c => c.Code == "FISH");
        Assert.Single(vm.MinorCodes, c => c.Code == "BAIT");
        Assert.Single(vm.PosCatCodes, c => c.Code == "BAIT");
    }

    [Fact]
    public async Task AddMajorCode_AddsToMajorCodesAndRepositoryAndClearsInput()
    {
        var (vm, codes, _, _, _) = Create();
        await vm.LoadAsync();
        vm.NewMajorCode = "TACKLE";
        vm.NewMajorName = "채비";

        await vm.AddMajorCodeCommand.ExecuteAsync(null);

        Assert.Contains(vm.MajorCodes, c => c.Code == "TACKLE" && c.Name == "채비");
        Assert.Contains(await codes.GetByGroupAsync("MAJOR"), c => c.Code == "TACKLE");
        Assert.Equal(string.Empty, vm.NewMajorCode);
        Assert.Equal(string.Empty, vm.NewMajorName);
    }

    [Fact]
    public async Task AddMinorCode_WithDuplicateCode_ShowsErrorAndDoesNotAddDuplicate()
    {
        var (vm, codes, _, _, _) = Create();
        await vm.LoadAsync();
        vm.NewMinorCode = "BAIT";
        vm.NewMinorName = "중복코드";

        await vm.AddMinorCodeCommand.ExecuteAsync(null);

        Assert.Equal("이미 존재하는 코드입니다", vm.ErrorMessage);
        Assert.Single(vm.MinorCodes, c => c.Code == "BAIT");
    }

    [Fact]
    public async Task AddPosCatCode_WithMissingName_ShowsErrorAndDoesNotAdd()
    {
        var (vm, codes, _, _, _) = Create();
        await vm.LoadAsync();
        vm.NewPosCatCode = "NEWCAT";
        vm.NewPosCatName = "";

        await vm.AddPosCatCodeCommand.ExecuteAsync(null);

        Assert.Equal("코드와 이름을 모두 입력해주세요", vm.ErrorMessage);
        Assert.DoesNotContain(vm.PosCatCodes, c => c.Code == "NEWCAT");
    }

    [Fact]
    public async Task DeletePosCatCode_WhenUnused_RemovesFromListAndRepository()
    {
        var (vm, codes, _, _, _) = Create();
        await vm.LoadAsync();
        await codes.AddAsync("POSCAT", "UNUSED", "미사용");
        var unusedCode = new CodeItem { Code = "UNUSED", Name = "미사용", SortNo = 9 };
        vm.PosCatCodes.Add(unusedCode);

        await vm.DeletePosCatCodeCommand.ExecuteAsync(unusedCode);

        Assert.DoesNotContain(vm.PosCatCodes, c => c.Code == "UNUSED");
        Assert.DoesNotContain(await codes.GetByGroupAsync("POSCAT"), c => c.Code == "UNUSED");
    }

    [Fact]
    public async Task DeleteMajorCode_WhenUsedByExistingProduct_ShowsErrorAndKeepsCode()
    {
        var (vm, codes, _, _, _) = Create();
        await vm.LoadAsync();
        var usedCode = vm.MajorCodes.Single(c => c.Code == "FISH");

        await vm.DeleteMajorCodeCommand.ExecuteAsync(usedCode);

        Assert.NotNull(vm.ErrorMessage);
        Assert.Contains(vm.MajorCodes, c => c.Code == "FISH");
        Assert.Contains(await codes.GetByGroupAsync("MAJOR"), c => c.Code == "FISH");
    }

    [Fact]
    public void GoToSettings_NavigatesBackToSettingsViewModel()
    {
        var (vm, _, _, navigation, settings) = Create();

        vm.GoToSettingsCommand.Execute(null);

        Assert.Same(settings, navigation.CurrentViewModel);
    }
}
```

- [ ] **Step 2: 테스트 실행 — 컴파일 실패 확인**

Run: `dotnet test tests/FishingMartPos.Tests --filter "SystemInfoViewModelTests|CodeManageViewModelTests"`
Expected: FAIL to compile — `SystemInfoViewModel`/`CodeManageViewModel`이 존재하지 않음.

- [ ] **Step 3: ViewModel 구현**

`src/FishingMartPos/ViewModels/SystemInfoViewModel.cs`:
```csharp
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FishingMartPos.Navigation;
using FishingMartPos.Repositories;
using FishingMartPos.Services;

namespace FishingMartPos.ViewModels;

public sealed partial class SystemInfoViewModel : ObservableObject
{
    private readonly ISystemInfoRepository _systemInfoRepository;
    private readonly ICurrentSession _session;
    private readonly INavigationService _navigation;
    private readonly SettingsViewModel _returnTo;

    [ObservableProperty] private string _appVersionStr = "-";
    [ObservableProperty] private string _dbVersionStr = "-";
    [ObservableProperty] private string _dbConnectionStatusStr = "-";
    [ObservableProperty] private string _posTerminalStr = string.Empty;

    public SystemInfoViewModel(
        ISystemInfoRepository systemInfoRepository,
        ICurrentSession session,
        INavigationService navigation,
        SettingsViewModel returnTo)
    {
        _systemInfoRepository = systemInfoRepository;
        _session = session;
        _navigation = navigation;
        _returnTo = returnTo;
    }

    public async Task LoadAsync()
    {
        var terminal = _session.CurrentTerminal;
        PosTerminalStr = terminal is not null ? $"{terminal.PosName} ({terminal.PosCode})" : string.Empty;

        try
        {
            var info = await _systemInfoRepository.GetAsync();
            AppVersionStr = info?.AppVersion ?? "-";
            DbVersionStr = info?.DbVersion ?? "-";
            DbConnectionStatusStr = "연결됨";
        }
        catch
        {
            AppVersionStr = "-";
            DbVersionStr = "-";
            DbConnectionStatusStr = "연결 안됨";
        }
    }

    [RelayCommand]
    private void GoToSettings() => _navigation.NavigateTo(_returnTo);
}
```

`src/FishingMartPos/ViewModels/CodeManageViewModel.cs`:
```csharp
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FishingMartPos.Models;
using FishingMartPos.Navigation;
using FishingMartPos.Repositories;

namespace FishingMartPos.ViewModels;

public sealed partial class CodeManageViewModel : ObservableObject
{
    private readonly ICodeRepository _codeRepository;
    private readonly IProductRepository _productRepository;
    private readonly INavigationService _navigation;
    private readonly SettingsViewModel _returnTo;

    private IReadOnlyList<Product> _allProducts = Array.Empty<Product>();

    [ObservableProperty] private string _newMajorCode = string.Empty;
    [ObservableProperty] private string _newMajorName = string.Empty;
    [ObservableProperty] private string _newMinorCode = string.Empty;
    [ObservableProperty] private string _newMinorName = string.Empty;
    [ObservableProperty] private string _newPosCatCode = string.Empty;
    [ObservableProperty] private string _newPosCatName = string.Empty;
    [ObservableProperty] private string? _errorMessage;

    public ObservableCollection<CodeItem> MajorCodes { get; } = new();
    public ObservableCollection<CodeItem> MinorCodes { get; } = new();
    public ObservableCollection<CodeItem> PosCatCodes { get; } = new();

    public CodeManageViewModel(
        ICodeRepository codeRepository,
        IProductRepository productRepository,
        INavigationService navigation,
        SettingsViewModel returnTo)
    {
        _codeRepository = codeRepository;
        _productRepository = productRepository;
        _navigation = navigation;
        _returnTo = returnTo;
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
    private async Task AddMajorCode() =>
        await AddCodeAsync("MAJOR", MajorCodes, NewMajorCode, NewMajorName, () => { NewMajorCode = string.Empty; NewMajorName = string.Empty; });

    [RelayCommand]
    private async Task AddMinorCode() =>
        await AddCodeAsync("MINOR", MinorCodes, NewMinorCode, NewMinorName, () => { NewMinorCode = string.Empty; NewMinorName = string.Empty; });

    [RelayCommand]
    private async Task AddPosCatCode() =>
        await AddCodeAsync("POSCAT", PosCatCodes, NewPosCatCode, NewPosCatName, () => { NewPosCatCode = string.Empty; NewPosCatName = string.Empty; });

    private async Task AddCodeAsync(string codeGbn, ObservableCollection<CodeItem> target, string newCode, string newName, Action clearInputs)
    {
        if (string.IsNullOrWhiteSpace(newCode) || string.IsNullOrWhiteSpace(newName))
        {
            ErrorMessage = "코드와 이름을 모두 입력해주세요";
            return;
        }

        if (target.Any(c => c.Code == newCode))
        {
            ErrorMessage = "이미 존재하는 코드입니다";
            return;
        }

        await _codeRepository.AddAsync(codeGbn, newCode, newName);
        target.Add(new CodeItem { Code = newCode, Name = newName, SortNo = 0 });
        clearInputs();
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

    [RelayCommand]
    private void GoToSettings() => _navigation.NavigateTo(_returnTo);
}
```

- [ ] **Step 4: 테스트 재실행 — 통과 확인**

Run: `dotnet test tests/FishingMartPos.Tests --filter "SystemInfoViewModelTests|CodeManageViewModelTests"`
Expected: PASS (all tests).

- [ ] **Step 5: 커밋**

```bash
git add src/FishingMartPos/ViewModels/SystemInfoViewModel.cs src/FishingMartPos/ViewModels/CodeManageViewModel.cs tests/FishingMartPos.Tests/ViewModels/SystemInfoViewModelTests.cs tests/FishingMartPos.Tests/ViewModels/CodeManageViewModelTests.cs
git commit -m "시스템정보/상품코드관리 ViewModel 추가"
```

---

## Task 6: SettingsViewModel 완성 + ADMIN 가드 + LoginViewModel/App.xaml.cs 배선

**Files:**
- Modify: `src/FishingMartPos/ViewModels/SettingsViewModel.cs` (Task 3 Step 0의 골격을 완성된 버전으로 교체)
- Modify: `src/FishingMartPos/ViewModels/MainMenuViewModel.cs`
- Modify: `src/FishingMartPos/ViewModels/LoginViewModel.cs`
- Modify: `src/FishingMartPos/App.xaml.cs`
- Modify: `src/FishingMartPos/Views/MainMenuView.xaml`
- Modify: `tests/FishingMartPos.Tests/ViewModels/SettingsViewModelTests.cs` (신규 생성 — 아직 없음)
- Modify: `tests/FishingMartPos.Tests/ViewModels/MainMenuViewModelTests.cs`
- Modify: `tests/FishingMartPos.Tests/ViewModels/LoginViewModelTests.cs`

**Interfaces:**
- Produces: `SettingsViewModel { StaffListViewModelFactory, PrinterSettingsViewModelFactory, ReceiptSettingsViewModelFactory, SystemInfoViewModelFactory, CodeManageViewModelFactory: Func<SettingsViewModel, Task<X>>?, GoToStaffListCommand, GoToPrinterSettingsCommand, GoToReceiptSettingsCommand, GoToSystemInfoCommand, GoToCodeManageCommand, GoToMainMenuCommand }`.
- Produces: `MainMenuViewModel.SettingsViewModelFactory: Func<MainMenuViewModel, Task<SettingsViewModel>>?`, `GoToSettingsCommand`(ADMIN 가드 포함, async).
- Produces: `LoginViewModel` 생성자에 8번째 파라미터 `Func<MainMenuViewModel, Task<SettingsViewModel>> settingsViewModelFactory` 추가.

- [ ] **Step 1: 실패하는 테스트 작성 (SettingsViewModelTests 신규 + MainMenuViewModelTests/LoginViewModelTests 갱신)**

`tests/FishingMartPos.Tests/ViewModels/SettingsViewModelTests.cs` (신규 파일):
```csharp
using FishingMartPos.Navigation;
using FishingMartPos.Services;
using FishingMartPos.Tests.Fakes;
using FishingMartPos.ViewModels;
using Xunit;

namespace FishingMartPos.Tests.ViewModels;

public class SettingsViewModelTests
{
    private static (SettingsViewModel vm, INavigationService navigation, MainMenuViewModel mainMenu) Create()
    {
        var session = new CurrentSession();
        var navigation = new NavigationService();
        var mainMenu = new MainMenuViewModel(session, navigation);
        var staff = new FakeStaffRepository(new Dictionary<string, FishingMartPos.Models.Staff>());
        var vm = new SettingsViewModel(navigation, mainMenu)
        {
            StaffListViewModelFactory = s => Task.FromResult(new StaffListViewModel(staff, navigation, s)),
            PrinterSettingsViewModelFactory = s => Task.FromResult(new PrinterSettingsViewModel(
                new FakePrinterConfigRepository(), session, new FakeDelayProvider(), navigation, s)),
            ReceiptSettingsViewModelFactory = s => Task.FromResult(new ReceiptSettingsViewModel(
                new FakeReceiptConfigRepository(), session, new FakeDelayProvider(), navigation, s)),
            SystemInfoViewModelFactory = s => Task.FromResult(new SystemInfoViewModel(
                new FakeSystemInfoRepository(), session, navigation, s)),
            CodeManageViewModelFactory = s => Task.FromResult(new CodeManageViewModel(
                new FakeCodeRepository(new Dictionary<string, IReadOnlyList<FishingMartPos.Models.CodeItem>>()),
                new FakeProductRepository(Array.Empty<FishingMartPos.Models.Product>()), navigation, s)),
        };
        return (vm, navigation, mainMenu);
    }

    [Fact]
    public async Task GoToStaffList_NavigatesToStaffListViewModel()
    {
        var (vm, navigation, _) = Create();

        await vm.GoToStaffListCommand.ExecuteAsync(null);

        Assert.IsType<StaffListViewModel>(navigation.CurrentViewModel);
    }

    [Fact]
    public async Task GoToPrinterSettings_NavigatesToPrinterSettingsViewModel()
    {
        var (vm, navigation, _) = Create();

        await vm.GoToPrinterSettingsCommand.ExecuteAsync(null);

        Assert.IsType<PrinterSettingsViewModel>(navigation.CurrentViewModel);
    }

    [Fact]
    public async Task GoToReceiptSettings_NavigatesToReceiptSettingsViewModel()
    {
        var (vm, navigation, _) = Create();

        await vm.GoToReceiptSettingsCommand.ExecuteAsync(null);

        Assert.IsType<ReceiptSettingsViewModel>(navigation.CurrentViewModel);
    }

    [Fact]
    public async Task GoToSystemInfo_NavigatesToSystemInfoViewModel()
    {
        var (vm, navigation, _) = Create();

        await vm.GoToSystemInfoCommand.ExecuteAsync(null);

        Assert.IsType<SystemInfoViewModel>(navigation.CurrentViewModel);
    }

    [Fact]
    public async Task GoToCodeManage_NavigatesToCodeManageViewModel()
    {
        var (vm, navigation, _) = Create();

        await vm.GoToCodeManageCommand.ExecuteAsync(null);

        Assert.IsType<CodeManageViewModel>(navigation.CurrentViewModel);
    }

    [Fact]
    public void GoToMainMenu_NavigatesBackToMainMenuViewModel()
    {
        var (vm, navigation, mainMenu) = Create();

        vm.GoToMainMenuCommand.Execute(null);

        Assert.Same(mainMenu, navigation.CurrentViewModel);
    }
}
```

`tests/FishingMartPos.Tests/ViewModels/MainMenuViewModelTests.cs` 전체를 다음으로 교체:
```csharp
using FishingMartPos.Models;
using FishingMartPos.Navigation;
using FishingMartPos.Services;
using FishingMartPos.Tests.Fakes;
using FishingMartPos.ViewModels;
using Xunit;

namespace FishingMartPos.Tests.ViewModels;

public class MainMenuViewModelTests
{
    private static (MainMenuViewModel vm, ICurrentSession session, INavigationService navigation) Create()
    {
        var session = new CurrentSession();
        session.SignIn(
            new Staff { StaffCode = "ADMIN1", StaffName = "관리자", Role = "ADMIN", UseYn = "Y" },
            new PosTerminal { PosCode = "2", PosName = "POS2" });
        var navigation = new NavigationService();
        var staffRepository = new FakeStaffRepository(new Dictionary<string, Staff>());
        var terminals = new[] { new PosTerminal { PosCode = "1", PosName = "POS1" } };
        var posViewModel = new PosViewModel(
            new FakeProductRepository(Array.Empty<Product>()),
            new FakeCodeRepository(new Dictionary<string, IReadOnlyList<CodeItem>>()),
            new FakeSalesRepository(),
            new FakeHeldOrderRepository(),
            new FakeDelayProvider(),
            session,
            navigation,
            new MainMenuViewModel(session, navigation));

        Func<MainMenuViewModel, Task<PosViewModel>> posViewModelFactory = _ => Task.FromResult(posViewModel);
        Func<MainMenuViewModel, Task<InventoryViewModel>> inventoryViewModelFactory = mainMenu =>
            Task.FromResult(new InventoryViewModel(
                new FakeProductRepository(Array.Empty<Product>()),
                new FakeCodeRepository(new Dictionary<string, IReadOnlyList<CodeItem>>()),
                session,
                navigation,
                mainMenu));
        Func<MainMenuViewModel, Task<SalesReportViewModel>> salesReportViewModelFactory = mainMenu =>
            Task.FromResult(new SalesReportViewModel(new FakeSalesRepository(), navigation, mainMenu));
        Func<MainMenuViewModel, Task<SettingsViewModel>> settingsViewModelFactory = mainMenu =>
            Task.FromResult(new SettingsViewModel(navigation, mainMenu));

        var vm = new MainMenuViewModel(session, navigation)
        {
            LoginViewModelFactory = () => new LoginViewModel(staffRepository, session, navigation, terminals, posViewModelFactory, inventoryViewModelFactory, salesReportViewModelFactory, settingsViewModelFactory),
            PosViewModelFactory = posViewModelFactory,
            InventoryViewModelFactory = inventoryViewModelFactory,
            SalesReportViewModelFactory = salesReportViewModelFactory,
            SettingsViewModelFactory = settingsViewModelFactory,
        };
        return (vm, session, navigation);
    }

    [Fact]
    public void PosLabel_ReflectsCurrentTerminal()
    {
        var (vm, _, _) = Create();

        Assert.Equal("POS2", vm.PosLabel);
    }

    [Fact]
    public async Task GoToSales_NavigatesToPosViewModel()
    {
        var (vm, _, navigation) = Create();

        await vm.GoToSalesCommand.ExecuteAsync(null);

        Assert.IsType<PosViewModel>(navigation.CurrentViewModel);
    }

    [Fact]
    public async Task GoToSalesReport_AsAdmin_NavigatesToSalesReportViewModel()
    {
        var (vm, _, navigation) = Create();

        await vm.GoToSalesReportCommand.ExecuteAsync(null);

        Assert.IsType<SalesReportViewModel>(navigation.CurrentViewModel);
    }

    [Fact]
    public async Task GoToSalesReport_AsStaff_DoesNothing()
    {
        var session = new CurrentSession();
        session.SignIn(
            new Staff { StaffCode = "STAFF1", StaffName = "직원", Role = "STAFF", UseYn = "Y" },
            new PosTerminal { PosCode = "1", PosName = "POS1" });
        var navigation = new NavigationService();
        var vm = new MainMenuViewModel(session, navigation)
        {
            SalesReportViewModelFactory = mainMenu => Task.FromResult(new SalesReportViewModel(new FakeSalesRepository(), navigation, mainMenu)),
        };

        await vm.GoToSalesReportCommand.ExecuteAsync(null);

        Assert.Null(navigation.CurrentViewModel);
        Assert.False(vm.IsAdmin);
    }

    [Fact]
    public async Task GoToInventory_NavigatesToInventoryViewModel()
    {
        var (vm, _, navigation) = Create();

        await vm.GoToInventoryCommand.ExecuteAsync(null);

        Assert.IsType<InventoryViewModel>(navigation.CurrentViewModel);
    }

    [Fact]
    public async Task GoToSettings_AsAdmin_NavigatesToSettingsViewModel()
    {
        var (vm, _, navigation) = Create();

        await vm.GoToSettingsCommand.ExecuteAsync(null);

        Assert.IsType<SettingsViewModel>(navigation.CurrentViewModel);
    }

    [Fact]
    public async Task GoToSettings_AsStaff_DoesNothing()
    {
        var session = new CurrentSession();
        session.SignIn(
            new Staff { StaffCode = "STAFF1", StaffName = "직원", Role = "STAFF", UseYn = "Y" },
            new PosTerminal { PosCode = "1", PosName = "POS1" });
        var navigation = new NavigationService();
        var vm = new MainMenuViewModel(session, navigation)
        {
            SettingsViewModelFactory = mainMenu => Task.FromResult(new SettingsViewModel(navigation, mainMenu)),
        };

        await vm.GoToSettingsCommand.ExecuteAsync(null);

        Assert.Null(navigation.CurrentViewModel);
        Assert.False(vm.IsAdmin);
    }

    [Fact]
    public void Logout_ClearsSessionAndNavigatesToLogin()
    {
        var (vm, session, navigation) = Create();

        vm.LogoutCommand.Execute(null);

        Assert.False(session.IsSignedIn);
        Assert.IsType<LoginViewModel>(navigation.CurrentViewModel);
    }
}
```

`tests/FishingMartPos.Tests/ViewModels/LoginViewModelTests.cs` 전체를 다음으로 교체:
```csharp
using FishingMartPos.Models;
using FishingMartPos.Navigation;
using FishingMartPos.Services;
using FishingMartPos.Tests.Fakes;
using FishingMartPos.ViewModels;
using Xunit;

namespace FishingMartPos.Tests.ViewModels;

public class LoginViewModelTests
{
    private static Staff AdminStaff => new()
    {
        StaffCode = "ADMIN1", StaffName = "관리자", Role = "ADMIN", UseYn = "Y"
    };

    private static LoginViewModel CreateViewModel(
        out ICurrentSession session,
        out INavigationService navigation,
        Dictionary<string, Staff>? staffByPin = null)
    {
        var repository = new FakeStaffRepository(staffByPin ?? new Dictionary<string, Staff> { ["0000"] = AdminStaff });
        session = new CurrentSession();
        navigation = new NavigationService();
        var terminals = new[]
        {
            new PosTerminal { PosCode = "1", PosName = "POS1" },
            new PosTerminal { PosCode = "2", PosName = "POS2" },
        };

        return new LoginViewModel(
            repository, session, navigation, terminals,
            CreateDummyPosViewModelFactory(session, navigation),
            CreateDummyInventoryViewModelFactory(session, navigation),
            CreateDummySalesReportViewModelFactory(session, navigation),
            CreateDummySettingsViewModelFactory(navigation));
    }

    private static Func<MainMenuViewModel, Task<PosViewModel>> CreateDummyPosViewModelFactory(
        ICurrentSession session, INavigationService navigation) =>
        mainMenu => Task.FromResult(new PosViewModel(
            new FakeProductRepository(Array.Empty<Product>()),
            new FakeCodeRepository(new Dictionary<string, IReadOnlyList<CodeItem>>()),
            new FakeSalesRepository(),
            new FakeHeldOrderRepository(),
            new FakeDelayProvider(),
            session,
            navigation,
            mainMenu));

    private static Func<MainMenuViewModel, Task<InventoryViewModel>> CreateDummyInventoryViewModelFactory(
        ICurrentSession session, INavigationService navigation) =>
        mainMenu => Task.FromResult(new InventoryViewModel(
            new FakeProductRepository(Array.Empty<Product>()),
            new FakeCodeRepository(new Dictionary<string, IReadOnlyList<CodeItem>>()),
            session,
            navigation,
            mainMenu));

    private static Func<MainMenuViewModel, Task<SalesReportViewModel>> CreateDummySalesReportViewModelFactory(
        ICurrentSession session, INavigationService navigation) =>
        mainMenu => Task.FromResult(new SalesReportViewModel(new FakeSalesRepository(), navigation, mainMenu));

    private static Func<MainMenuViewModel, Task<SettingsViewModel>> CreateDummySettingsViewModelFactory(
        INavigationService navigation) =>
        mainMenu => Task.FromResult(new SettingsViewModel(navigation, mainMenu));

    [Fact]
    public void PressingDigits_BuildsPinString()
    {
        var vm = CreateViewModel(out _, out _);

        vm.PressKeyCommand.Execute("1");
        vm.PressKeyCommand.Execute("2");
        vm.PressKeyCommand.Execute("3");

        Assert.Equal("123", vm.Pin);
    }

    [Fact]
    public void PressingBackspace_RemovesLastDigit()
    {
        var vm = CreateViewModel(out _, out _);
        vm.PressKeyCommand.Execute("1");
        vm.PressKeyCommand.Execute("2");

        vm.PressKeyCommand.Execute("<");

        Assert.Equal("1", vm.Pin);
    }

    [Fact]
    public void PressingClear_EmptiesPin()
    {
        var vm = CreateViewModel(out _, out _);
        vm.PressKeyCommand.Execute("1");
        vm.PressKeyCommand.Execute("2");

        vm.PressKeyCommand.Execute("CLS");

        Assert.Equal(string.Empty, vm.Pin);
    }

    [Fact]
    public async Task FourCorrectDigits_SignsInAndNavigatesToMainMenu()
    {
        var vm = CreateViewModel(out var session, out var navigation);

        vm.PressKeyCommand.Execute("0");
        vm.PressKeyCommand.Execute("0");
        vm.PressKeyCommand.Execute("0");
        vm.PressKeyCommand.Execute("0");
        await Task.Delay(50);

        Assert.True(session.IsSignedIn);
        Assert.Equal("ADMIN1", session.CurrentStaff?.StaffCode);
        Assert.IsType<MainMenuViewModel>(navigation.CurrentViewModel);
    }

    [Fact]
    public async Task FourWrongDigits_ShowsErrorAndClearsPin()
    {
        var vm = CreateViewModel(out var session, out var navigation);

        vm.PressKeyCommand.Execute("9");
        vm.PressKeyCommand.Execute("9");
        vm.PressKeyCommand.Execute("9");
        vm.PressKeyCommand.Execute("9");
        await Task.Delay(50);

        Assert.False(session.IsSignedIn);
        Assert.Null(navigation.CurrentViewModel);
        Assert.True(vm.IsLoginErrorVisible);
        Assert.Equal(string.Empty, vm.Pin);
    }

    [Fact]
    public void SelectingTerminal_UpdatesSelectedTerminal()
    {
        var vm = CreateViewModel(out _, out _);

        vm.SelectTerminalCommand.Execute(vm.Terminals[1]);

        Assert.Equal("2", vm.SelectedTerminal.PosCode);
    }
}
```

- [ ] **Step 2: 테스트 실행 — 컴파일 실패 확인**

Run: `dotnet test tests/FishingMartPos.Tests --filter "SettingsViewModelTests|MainMenuViewModelTests|LoginViewModelTests"`
Expected: FAIL to compile — `SettingsViewModel`에 5개 팩토리/커맨드가 없고, `LoginViewModel` 생성자가 7개 인자만 받으며, `MainMenuViewModel`에 `SettingsViewModelFactory`가 없음.

- [ ] **Step 3: SettingsViewModel 완성, MainMenuViewModel/LoginViewModel 수정**

`src/FishingMartPos/ViewModels/SettingsViewModel.cs` 전체를 다음으로 교체(Task 3 Step 0의 골격을 대체):
```csharp
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FishingMartPos.Navigation;

namespace FishingMartPos.ViewModels;

public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly INavigationService _navigation;
    private readonly MainMenuViewModel _returnTo;

    public SettingsViewModel(INavigationService navigation, MainMenuViewModel returnTo)
    {
        _navigation = navigation;
        _returnTo = returnTo;
    }

    public Func<SettingsViewModel, Task<StaffListViewModel>>? StaffListViewModelFactory { get; init; }
    public Func<SettingsViewModel, Task<PrinterSettingsViewModel>>? PrinterSettingsViewModelFactory { get; init; }
    public Func<SettingsViewModel, Task<ReceiptSettingsViewModel>>? ReceiptSettingsViewModelFactory { get; init; }
    public Func<SettingsViewModel, Task<SystemInfoViewModel>>? SystemInfoViewModelFactory { get; init; }
    public Func<SettingsViewModel, Task<CodeManageViewModel>>? CodeManageViewModelFactory { get; init; }

    [RelayCommand]
    private async Task GoToStaffList()
    {
        var vm = await StaffListViewModelFactory!.Invoke(this);
        _navigation.NavigateTo(vm);
    }

    [RelayCommand]
    private async Task GoToPrinterSettings()
    {
        var vm = await PrinterSettingsViewModelFactory!.Invoke(this);
        _navigation.NavigateTo(vm);
    }

    [RelayCommand]
    private async Task GoToReceiptSettings()
    {
        var vm = await ReceiptSettingsViewModelFactory!.Invoke(this);
        _navigation.NavigateTo(vm);
    }

    [RelayCommand]
    private async Task GoToSystemInfo()
    {
        var vm = await SystemInfoViewModelFactory!.Invoke(this);
        _navigation.NavigateTo(vm);
    }

    [RelayCommand]
    private async Task GoToCodeManage()
    {
        var vm = await CodeManageViewModelFactory!.Invoke(this);
        _navigation.NavigateTo(vm);
    }

    [RelayCommand]
    private void GoToMainMenu() => _navigation.NavigateTo(_returnTo);
}
```

`src/FishingMartPos/ViewModels/MainMenuViewModel.cs` 전체를 다음으로 교체:
```csharp
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FishingMartPos.Navigation;
using FishingMartPos.Services;

namespace FishingMartPos.ViewModels;

public sealed partial class MainMenuViewModel : ObservableObject
{
    private readonly ICurrentSession _session;
    private readonly INavigationService _navigation;

    [ObservableProperty]
    private DateTime _now = DateTime.Now;

    public MainMenuViewModel(ICurrentSession session, INavigationService navigation)
    {
        _session = session;
        _navigation = navigation;
    }

    public string PosLabel => _session.CurrentTerminal?.PosName ?? string.Empty;

    public bool IsAdmin => _session.CurrentStaff?.IsAdmin ?? false;

    /// <summary>App.xaml.cs(또는 테스트)에서 주입하는 LoginViewModel 팩토리 — 로그아웃 시 사용.</summary>
    public Func<LoginViewModel>? LoginViewModelFactory { get; init; }

    /// <summary>App.xaml.cs(또는 테스트)에서 주입하는 PosViewModel 팩토리 — "판매" 진입 시 사용. 자신(this)을 넘겨줘 PosViewModel이 "메뉴" 버튼으로 복귀 시 재사용한다.</summary>
    public Func<MainMenuViewModel, Task<PosViewModel>>? PosViewModelFactory { get; init; }

    /// <summary>App.xaml.cs(또는 테스트)에서 주입하는 InventoryViewModel 팩토리 — "재고" 진입 시 사용. 자신(this)을 넘겨줘 InventoryViewModel이 "메인메뉴로" 복귀 시 재사용한다.</summary>
    public Func<MainMenuViewModel, Task<InventoryViewModel>>? InventoryViewModelFactory { get; init; }

    /// <summary>App.xaml.cs(또는 테스트)에서 주입하는 SalesReportViewModel 팩토리 — "매출" 진입 시 사용(ADMIN 전용). 자신(this)을 넘겨줘 SalesReportViewModel이 "메인메뉴로" 복귀 시 재사용한다.</summary>
    public Func<MainMenuViewModel, Task<SalesReportViewModel>>? SalesReportViewModelFactory { get; init; }

    /// <summary>App.xaml.cs(또는 테스트)에서 주입하는 SettingsViewModel 팩토리 — "환경설정" 진입 시 사용(ADMIN 전용). 자신(this)을 넘겨줘 SettingsViewModel이 "메인메뉴로" 복귀 시 재사용한다.</summary>
    public Func<MainMenuViewModel, Task<SettingsViewModel>>? SettingsViewModelFactory { get; init; }

    [RelayCommand]
    private async Task GoToSales()
    {
        var posViewModel = await PosViewModelFactory!.Invoke(this);
        _navigation.NavigateTo(posViewModel);
    }

    [RelayCommand]
    private async Task GoToSalesReport()
    {
        if (!IsAdmin) return;
        var salesReportViewModel = await SalesReportViewModelFactory!.Invoke(this);
        _navigation.NavigateTo(salesReportViewModel);
    }

    [RelayCommand]
    private async Task GoToInventory()
    {
        var inventoryViewModel = await InventoryViewModelFactory!.Invoke(this);
        _navigation.NavigateTo(inventoryViewModel);
    }

    [RelayCommand]
    private async Task GoToSettings()
    {
        if (!IsAdmin) return;
        var settingsViewModel = await SettingsViewModelFactory!.Invoke(this);
        _navigation.NavigateTo(settingsViewModel);
    }

    [RelayCommand]
    private void Logout()
    {
        _session.SignOut();
        _navigation.NavigateTo(LoginViewModelFactory!.Invoke());
    }
}
```

`src/FishingMartPos/ViewModels/LoginViewModel.cs` 전체를 다음으로 교체:
```csharp
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FishingMartPos.Models;
using FishingMartPos.Navigation;
using FishingMartPos.Repositories;
using FishingMartPos.Services;

namespace FishingMartPos.ViewModels;

public sealed partial class LoginViewModel : ObservableObject
{
    private readonly IStaffRepository _staffRepository;
    private readonly ICurrentSession _session;
    private readonly INavigationService _navigation;
    private readonly Func<MainMenuViewModel, Task<PosViewModel>> _posViewModelFactory;
    private readonly Func<MainMenuViewModel, Task<InventoryViewModel>> _inventoryViewModelFactory;
    private readonly Func<MainMenuViewModel, Task<SalesReportViewModel>> _salesReportViewModelFactory;
    private readonly Func<MainMenuViewModel, Task<SettingsViewModel>> _settingsViewModelFactory;

    [ObservableProperty]
    private string _pin = string.Empty;

    [ObservableProperty]
    private bool _isLoginErrorVisible;

    [ObservableProperty]
    private PosTerminal _selectedTerminal;

    public IReadOnlyList<PosTerminal> Terminals { get; }

    public IReadOnlyList<bool> PinDots => Enumerable.Range(0, 4).Select(i => i < Pin.Length).ToArray();

    partial void OnPinChanged(string value) => OnPropertyChanged(nameof(PinDots));

    public LoginViewModel(
        IStaffRepository staffRepository,
        ICurrentSession session,
        INavigationService navigation,
        IReadOnlyList<PosTerminal> terminals,
        Func<MainMenuViewModel, Task<PosViewModel>> posViewModelFactory,
        Func<MainMenuViewModel, Task<InventoryViewModel>> inventoryViewModelFactory,
        Func<MainMenuViewModel, Task<SalesReportViewModel>> salesReportViewModelFactory,
        Func<MainMenuViewModel, Task<SettingsViewModel>> settingsViewModelFactory)
    {
        _staffRepository = staffRepository;
        _session = session;
        _navigation = navigation;
        Terminals = terminals;
        _selectedTerminal = terminals[0];
        _posViewModelFactory = posViewModelFactory;
        _inventoryViewModelFactory = inventoryViewModelFactory;
        _salesReportViewModelFactory = salesReportViewModelFactory;
        _settingsViewModelFactory = settingsViewModelFactory;
    }

    [RelayCommand]
    private void SelectTerminal(PosTerminal terminal)
    {
        SelectedTerminal = terminal;
    }

    [RelayCommand]
    private async Task PressKey(string key)
    {
        switch (key)
        {
            case "CLS":
                Pin = string.Empty;
                IsLoginErrorVisible = false;
                return;
            case "<":
                if (Pin.Length > 0)
                {
                    Pin = Pin[..^1];
                }
                IsLoginErrorVisible = false;
                return;
        }

        if (Pin.Length >= 4)
        {
            return;
        }

        Pin += key;
        IsLoginErrorVisible = false;

        if (Pin.Length == 4)
        {
            await SubmitAsync();
        }
    }

    private async Task SubmitAsync()
    {
        Staff? staff = await _staffRepository.FindByPinAsync(Pin);

        if (staff is null)
        {
            IsLoginErrorVisible = true;
            Pin = string.Empty;
            return;
        }

        _session.SignIn(staff, SelectedTerminal);

        var mainMenuViewModel = new MainMenuViewModel(_session, _navigation)
        {
            LoginViewModelFactory = () => new LoginViewModel(_staffRepository, _session, _navigation, Terminals, _posViewModelFactory, _inventoryViewModelFactory, _salesReportViewModelFactory, _settingsViewModelFactory),
            PosViewModelFactory = _posViewModelFactory,
            InventoryViewModelFactory = _inventoryViewModelFactory,
            SalesReportViewModelFactory = _salesReportViewModelFactory,
            SettingsViewModelFactory = _settingsViewModelFactory,
        };
        _navigation.NavigateTo(mainMenuViewModel);
    }
}
```

- [ ] **Step 4: App.xaml.cs 배선**

`src/FishingMartPos/App.xaml.cs`를 직접 열어(**grep에 의존하지 말 것** — Global Constraints 참고) `OnStartup` 메서드 전체를 다음으로 교체:

```csharp
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        foreach (var (name, brush) in AppColors.BuildBrushes())
        {
            Resources[name] = brush;
        }

        var config = AppConfig.Load(AppContext.BaseDirectory);

        var services = new ServiceCollection();
        services.AddSingleton(config);
        services.AddSingleton<IDbConnectionFactory, MySqlConnectionFactory>();
        services.AddSingleton<IStaffRepository, StaffRepository>();
        services.AddSingleton<IPosTerminalRepository, PosTerminalRepository>();
        services.AddSingleton<ICurrentSession, CurrentSession>();
        services.AddSingleton<INavigationService, NavigationService>();
        services.AddSingleton<IProductRepository, ProductRepository>();
        services.AddSingleton<ICodeRepository, CodeRepository>();
        services.AddSingleton<ISalesRepository, SalesRepository>();
        services.AddSingleton<IHeldOrderRepository, HeldOrderRepository>();
        services.AddSingleton<IPrinterConfigRepository, PrinterConfigRepository>();
        services.AddSingleton<IReceiptConfigRepository, ReceiptConfigRepository>();
        services.AddSingleton<ISystemInfoRepository, SystemInfoRepository>();
        services.AddSingleton<IDelayProvider, DelayProvider>();
        services.AddSingleton<IPhotoPicker, WpfPhotoPicker>();
        services.AddSingleton<IProductPhotoStorage>(_ => new FileSystemProductPhotoStorage(AppContext.BaseDirectory));
        _services = services.BuildServiceProvider();

        var terminalRepository = _services.GetRequiredService<IPosTerminalRepository>();
        IReadOnlyList<PosTerminal> terminals = await terminalRepository.GetAllAsync();

        var staffRepository = _services.GetRequiredService<IStaffRepository>();
        var session = _services.GetRequiredService<ICurrentSession>();
        var navigation = _services.GetRequiredService<INavigationService>();
        var productRepository = _services.GetRequiredService<IProductRepository>();
        var codeRepository = _services.GetRequiredService<ICodeRepository>();
        var salesRepository = _services.GetRequiredService<ISalesRepository>();
        var heldOrderRepository = _services.GetRequiredService<IHeldOrderRepository>();
        var printerConfigRepository = _services.GetRequiredService<IPrinterConfigRepository>();
        var receiptConfigRepository = _services.GetRequiredService<IReceiptConfigRepository>();
        var systemInfoRepository = _services.GetRequiredService<ISystemInfoRepository>();
        var delayProvider = _services.GetRequiredService<IDelayProvider>();
        var photoPicker = _services.GetRequiredService<IPhotoPicker>();
        var photoStorage = _services.GetRequiredService<IProductPhotoStorage>();

        async Task<PosViewModel> CreatePosViewModelAsync(MainMenuViewModel mainMenu)
        {
            var vm = new PosViewModel(productRepository, codeRepository, salesRepository, heldOrderRepository, delayProvider, session, navigation, mainMenu);
            await vm.LoadAsync();
            return vm;
        }

        async Task<InventoryViewModel> CreateInventoryViewModelAsync(MainMenuViewModel mainMenu)
        {
            var vm = new InventoryViewModel(productRepository, codeRepository, session, navigation, mainMenu);
            vm.InventoryFormViewModelFactory = (inv, product) =>
                Task.FromResult(new InventoryFormViewModel(productRepository, codeRepository, photoPicker, photoStorage, delayProvider, navigation, inv, product));
            await vm.LoadAsync();
            return vm;
        }

        async Task<SalesReportViewModel> CreateSalesReportViewModelAsync(MainMenuViewModel mainMenu)
        {
            var vm = new SalesReportViewModel(salesRepository, navigation, mainMenu);
            await vm.LoadAsync();
            return vm;
        }

        async Task<StaffListViewModel> CreateStaffListViewModelAsync(SettingsViewModel settings)
        {
            var vm = new StaffListViewModel(staffRepository, navigation, settings);
            vm.StaffFormViewModelFactory = (list, staff) =>
                Task.FromResult(new StaffFormViewModel(staffRepository, delayProvider, navigation, list, staff));
            await vm.LoadAsync();
            return vm;
        }

        async Task<PrinterSettingsViewModel> CreatePrinterSettingsViewModelAsync(SettingsViewModel settings)
        {
            var vm = new PrinterSettingsViewModel(printerConfigRepository, session, delayProvider, navigation, settings);
            await vm.LoadAsync();
            return vm;
        }

        async Task<ReceiptSettingsViewModel> CreateReceiptSettingsViewModelAsync(SettingsViewModel settings)
        {
            var vm = new ReceiptSettingsViewModel(receiptConfigRepository, session, delayProvider, navigation, settings);
            await vm.LoadAsync();
            return vm;
        }

        async Task<SystemInfoViewModel> CreateSystemInfoViewModelAsync(SettingsViewModel settings)
        {
            var vm = new SystemInfoViewModel(systemInfoRepository, session, navigation, settings);
            await vm.LoadAsync();
            return vm;
        }

        async Task<CodeManageViewModel> CreateCodeManageViewModelAsync(SettingsViewModel settings)
        {
            var vm = new CodeManageViewModel(codeRepository, productRepository, navigation, settings);
            await vm.LoadAsync();
            return vm;
        }

        Task<SettingsViewModel> CreateSettingsViewModelAsync(MainMenuViewModel mainMenu)
        {
            var vm = new SettingsViewModel(navigation, mainMenu)
            {
                StaffListViewModelFactory = CreateStaffListViewModelAsync,
                PrinterSettingsViewModelFactory = CreatePrinterSettingsViewModelAsync,
                ReceiptSettingsViewModelFactory = CreateReceiptSettingsViewModelAsync,
                SystemInfoViewModelFactory = CreateSystemInfoViewModelAsync,
                CodeManageViewModelFactory = CreateCodeManageViewModelAsync,
            };
            return Task.FromResult(vm);
        }

        LoginViewModel CreateLoginViewModel() =>
            new(staffRepository, session, navigation, terminals, CreatePosViewModelAsync, CreateInventoryViewModelAsync, CreateSalesReportViewModelAsync, CreateSettingsViewModelAsync);

        navigation.NavigateTo(CreateLoginViewModel());

        var mainWindow = new MainWindow(navigation);
        mainWindow.Show();
    }
```

(참고: `services.AddSingleton<IPrinterConfigRepository, ...>` 등 3줄은 Task 2에서 이미 추가되어 있으므로 중복 추가하지 않는다 — 이미 있으면 그대로 둔다.)

- [ ] **Step 5: MainMenuView.xaml — 환경설정 타일에 ADMIN 가드 Visibility 추가**

`src/FishingMartPos/Views/MainMenuView.xaml`에서 "환경설정" 타일의 `<Button Command="{Binding GoToSettingsCommand}" ...>` 여는 태그를 찾아, 매출 타일과 동일하게 `Visibility` 속성을 추가한다:

```xml
                <Button Command="{Binding GoToSettingsCommand}" Margin="10" Background="White"
                        BorderBrush="{DynamicResource MenuTileBorder}" BorderThickness="1"
                        Visibility="{Binding IsAdmin, Converter={StaticResource BooleanToVisibilityConverter}}">
```

(태그 내부의 나머지 내용은 변경하지 않는다.)

- [ ] **Step 6: 빌드 + 테스트 실행 — 통과 확인**

Run: `dotnet build`
Expected: 빌드 성공.

Run: `dotnet test tests/FishingMartPos.Tests --filter "SettingsViewModelTests|MainMenuViewModelTests|LoginViewModelTests"`
Expected: PASS (all tests).

Run: `dotnet test tests/FishingMartPos.Tests`
Expected: PASS (전체 스위트 — 다른 테스트들이 `MainMenuViewModel`/`LoginViewModel`을 생성하는 방식이 바뀌지 않았는지 확인).

Note: 이 시점에는 `MainWindow.xaml`에 신규 7개 ViewModel에 대한 `DataTemplate`이 아직 없으므로, 앱을 실제로 실행해 "환경설정"을 클릭하면 화면이 비어 보이거나 예외가 발생할 수 있다 — Task 7에서 XAML 뷰와 함께 해결된다. 이는 의도된 중간 상태다.

- [ ] **Step 7: 커밋**

```bash
git add src/FishingMartPos/ViewModels/SettingsViewModel.cs src/FishingMartPos/ViewModels/MainMenuViewModel.cs src/FishingMartPos/ViewModels/LoginViewModel.cs src/FishingMartPos/App.xaml.cs src/FishingMartPos/Views/MainMenuView.xaml tests/FishingMartPos.Tests/ViewModels/SettingsViewModelTests.cs tests/FishingMartPos.Tests/ViewModels/MainMenuViewModelTests.cs tests/FishingMartPos.Tests/ViewModels/LoginViewModelTests.cs
git commit -m "환경설정 허브 ViewModel 완성, 메인메뉴 ADMIN 가드, LoginViewModel/App.xaml.cs 배선"
```

---

## Task 7: 7개 XAML 뷰 + MainWindow.xaml DataTemplate 배선

**Files:**
- Create: `src/FishingMartPos/Views/SettingsView.xaml` / `.xaml.cs`
- Create: `src/FishingMartPos/Views/StaffListView.xaml` / `.xaml.cs`
- Create: `src/FishingMartPos/Views/StaffFormView.xaml` / `.xaml.cs`
- Create: `src/FishingMartPos/Views/PrinterSettingsView.xaml` / `.xaml.cs`
- Create: `src/FishingMartPos/Views/ReceiptSettingsView.xaml` / `.xaml.cs`
- Create: `src/FishingMartPos/Views/SystemInfoView.xaml` / `.xaml.cs`
- Create: `src/FishingMartPos/Views/CodeManageView.xaml` / `.xaml.cs`
- Modify: `src/FishingMartPos/MainWindow.xaml`
- Create: `tests/FishingMartPos.Tests/Views/SettingsScreensSmokeTests.cs`

**Interfaces:**
- Consumes: 모든 ViewModel의 바인딩 프로퍼티/커맨드(Task 3~6에서 이미 확정).
- Produces: 없음(뷰 레이어의 끝 — 이 태스크 이후 전체 기능이 실사용 가능해진다).

- [ ] **Step 1: 7개 View 코드비하인드 작성 (모두 동일한 trivial 패턴)**

`src/FishingMartPos/Views/SettingsView.xaml.cs`:
```csharp
using System.Windows.Controls;

namespace FishingMartPos.Views;

public partial class SettingsView : UserControl
{
    public SettingsView()
    {
        InitializeComponent();
    }
}
```

`src/FishingMartPos/Views/StaffListView.xaml.cs`:
```csharp
using System.Windows.Controls;

namespace FishingMartPos.Views;

public partial class StaffListView : UserControl
{
    public StaffListView()
    {
        InitializeComponent();
    }
}
```

`src/FishingMartPos/Views/StaffFormView.xaml.cs`:
```csharp
using System.Windows.Controls;

namespace FishingMartPos.Views;

public partial class StaffFormView : UserControl
{
    public StaffFormView()
    {
        InitializeComponent();
    }
}
```

`src/FishingMartPos/Views/PrinterSettingsView.xaml.cs`:
```csharp
using System.Windows.Controls;

namespace FishingMartPos.Views;

public partial class PrinterSettingsView : UserControl
{
    public PrinterSettingsView()
    {
        InitializeComponent();
    }
}
```

`src/FishingMartPos/Views/ReceiptSettingsView.xaml.cs`:
```csharp
using System.Windows.Controls;

namespace FishingMartPos.Views;

public partial class ReceiptSettingsView : UserControl
{
    public ReceiptSettingsView()
    {
        InitializeComponent();
    }
}
```

`src/FishingMartPos/Views/SystemInfoView.xaml.cs`:
```csharp
using System.Windows.Controls;

namespace FishingMartPos.Views;

public partial class SystemInfoView : UserControl
{
    public SystemInfoView()
    {
        InitializeComponent();
    }
}
```

`src/FishingMartPos/Views/CodeManageView.xaml.cs`:
```csharp
using System.Windows.Controls;

namespace FishingMartPos.Views;

public partial class CodeManageView : UserControl
{
    public CodeManageView()
    {
        InitializeComponent();
    }
}
```

- [ ] **Step 2: SettingsView.xaml (허브)**

`src/FishingMartPos/Views/SettingsView.xaml`:
```xml
<UserControl x:Class="FishingMartPos.Views.SettingsView"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006"
             xmlns:d="http://schemas.microsoft.com/expression/blend/2008"
             mc:Ignorable="d">
    <Grid Background="{DynamicResource MenuBackground}">
        <Grid.RowDefinitions>
            <RowDefinition Height="Auto" />
            <RowDefinition Height="*" />
        </Grid.RowDefinitions>

        <Border Grid.Row="0" Background="White" BorderBrush="{DynamicResource HeaderBorder}" BorderThickness="0,0,0,1">
            <Grid Margin="16,0" Height="42">
                <TextBlock Text="환경설정" FontSize="14" FontWeight="Bold"
                           Foreground="{DynamicResource TitleText}" VerticalAlignment="Center" HorizontalAlignment="Left" />
                <Button Content="← 메인메뉴로" Padding="12,5" FontSize="12" FontWeight="Bold"
                        Background="{DynamicResource LogoutButtonBackground}" Foreground="{DynamicResource MutedText}"
                        BorderBrush="{DynamicResource InputBorder}" BorderThickness="1"
                        HorizontalAlignment="Right" VerticalAlignment="Center"
                        Command="{Binding GoToMainMenuCommand}" />
            </Grid>
        </Border>

        <Border Grid.Row="1" Margin="24,20" Background="White" BorderBrush="{DynamicResource CardBorder}" BorderThickness="1"
                MaxWidth="480" HorizontalAlignment="Left" VerticalAlignment="Top">
            <StackPanel>
                <Button Command="{Binding GoToStaffListCommand}" Padding="16,14" Background="Transparent" BorderThickness="0"
                        HorizontalContentAlignment="Stretch" Cursor="Hand">
                    <TextBlock Text="직원관리" FontSize="14" Foreground="{DynamicResource TitleText}" />
                </Button>
                <Border BorderBrush="{DynamicResource CardBorder}" BorderThickness="0,0,0,1" />
                <Button Command="{Binding GoToPrinterSettingsCommand}" Padding="16,14" Background="Transparent" BorderThickness="0"
                        HorizontalContentAlignment="Stretch" Cursor="Hand">
                    <TextBlock Text="프린터설정" FontSize="14" Foreground="{DynamicResource TitleText}" />
                </Button>
                <Border BorderBrush="{DynamicResource CardBorder}" BorderThickness="0,0,0,1" />
                <Button Command="{Binding GoToReceiptSettingsCommand}" Padding="16,14" Background="Transparent" BorderThickness="0"
                        HorizontalContentAlignment="Stretch" Cursor="Hand">
                    <TextBlock Text="영수증설정" FontSize="14" Foreground="{DynamicResource TitleText}" />
                </Button>
                <Border BorderBrush="{DynamicResource CardBorder}" BorderThickness="0,0,0,1" />
                <Button Command="{Binding GoToSystemInfoCommand}" Padding="16,14" Background="Transparent" BorderThickness="0"
                        HorizontalContentAlignment="Stretch" Cursor="Hand">
                    <TextBlock Text="시스템정보" FontSize="14" Foreground="{DynamicResource TitleText}" />
                </Button>
                <Border BorderBrush="{DynamicResource CardBorder}" BorderThickness="0,0,0,1" />
                <Button Command="{Binding GoToCodeManageCommand}" Padding="16,14" Background="Transparent" BorderThickness="0"
                        HorizontalContentAlignment="Stretch" Cursor="Hand">
                    <TextBlock Text="상품코드관리" FontSize="14" Foreground="{DynamicResource TitleText}" />
                </Button>
            </StackPanel>
        </Border>
    </Grid>
</UserControl>
```

- [ ] **Step 3: StaffListView.xaml**

`src/FishingMartPos/Views/StaffListView.xaml`:
```xml
<UserControl x:Class="FishingMartPos.Views.StaffListView"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006"
             xmlns:d="http://schemas.microsoft.com/expression/blend/2008"
             mc:Ignorable="d">
    <Grid Background="{DynamicResource MenuBackground}">
        <Grid.RowDefinitions>
            <RowDefinition Height="Auto" />
            <RowDefinition Height="*" />
        </Grid.RowDefinitions>

        <Border Grid.Row="0" Background="White" BorderBrush="{DynamicResource HeaderBorder}" BorderThickness="0,0,0,1">
            <Grid Margin="16,0" Height="42">
                <TextBlock Text="직원관리" FontSize="14" FontWeight="Bold"
                           Foreground="{DynamicResource TitleText}" VerticalAlignment="Center" HorizontalAlignment="Left" />
                <StackPanel Orientation="Horizontal" HorizontalAlignment="Right" VerticalAlignment="Center">
                    <Button Content="+ 직원등록" Padding="12,5" FontSize="12" FontWeight="Bold" Margin="0,0,8,0"
                            Background="{DynamicResource Accent}" Foreground="White" BorderBrush="{DynamicResource Accent}" BorderThickness="1"
                            Command="{Binding GoToAddStaffCommand}" />
                    <Button Content="← 환경설정으로" Padding="12,5" FontSize="12" FontWeight="Bold"
                            Background="{DynamicResource LogoutButtonBackground}" Foreground="{DynamicResource MutedText}"
                            BorderBrush="{DynamicResource InputBorder}" BorderThickness="1"
                            Command="{Binding GoToSettingsCommand}" />
                </StackPanel>
            </Grid>
        </Border>

        <Border Grid.Row="1" Margin="16,16" Background="White" BorderBrush="{DynamicResource CardBorder}" BorderThickness="1">
            <Grid>
                <Grid.RowDefinitions>
                    <RowDefinition Height="Auto" />
                    <RowDefinition Height="*" />
                </Grid.RowDefinitions>

                <Grid Grid.Row="0" Margin="12,8" Background="{DynamicResource HeaderButtonBackground}">
                    <Grid.ColumnDefinitions>
                        <ColumnDefinition Width="1*" />
                        <ColumnDefinition Width="2*" />
                        <ColumnDefinition Width="1*" />
                        <ColumnDefinition Width="1*" />
                        <ColumnDefinition Width="90" />
                    </Grid.ColumnDefinitions>
                    <TextBlock Text="직원코드" FontSize="11" FontWeight="Bold" Foreground="{DynamicResource MutedText}" HorizontalAlignment="Center" />
                    <TextBlock Grid.Column="1" Text="이름" FontSize="11" FontWeight="Bold" Foreground="{DynamicResource MutedText}" />
                    <TextBlock Grid.Column="2" Text="권한" FontSize="11" FontWeight="Bold" Foreground="{DynamicResource MutedText}" HorizontalAlignment="Center" />
                    <TextBlock Grid.Column="3" Text="사용유무" FontSize="11" FontWeight="Bold" Foreground="{DynamicResource MutedText}" HorizontalAlignment="Center" />
                    <TextBlock Grid.Column="4" Text="수정" FontSize="11" FontWeight="Bold" Foreground="{DynamicResource MutedText}" HorizontalAlignment="Center" />
                </Grid>

                <ScrollViewer Grid.Row="1" VerticalScrollBarVisibility="Auto">
                    <ItemsControl ItemsSource="{Binding Rows}">
                        <ItemsControl.ItemTemplate>
                            <DataTemplate>
                                <Grid Margin="12,8">
                                    <Grid.ColumnDefinitions>
                                        <ColumnDefinition Width="1*" />
                                        <ColumnDefinition Width="2*" />
                                        <ColumnDefinition Width="1*" />
                                        <ColumnDefinition Width="1*" />
                                        <ColumnDefinition Width="90" />
                                    </Grid.ColumnDefinitions>
                                    <TextBlock Text="{Binding StaffCode}" FontSize="12" HorizontalAlignment="Center" VerticalAlignment="Center" />
                                    <TextBlock Grid.Column="1" Text="{Binding StaffName}" FontSize="13" FontWeight="Bold" VerticalAlignment="Center" Foreground="{DynamicResource TitleText}" />
                                    <TextBlock Grid.Column="2" Text="{Binding RoleName}" FontSize="12" HorizontalAlignment="Center" VerticalAlignment="Center" />
                                    <TextBlock Grid.Column="3" Text="{Binding UseYnLabel}" FontSize="12" HorizontalAlignment="Center" VerticalAlignment="Center" />
                                    <Button Grid.Column="4" Content="수정" FontSize="11" Padding="8,4" HorizontalAlignment="Center"
                                            Command="{Binding EditCommand}" />
                                </Grid>
                            </DataTemplate>
                        </ItemsControl.ItemTemplate>
                    </ItemsControl>
                </ScrollViewer>
            </Grid>
        </Border>
    </Grid>
</UserControl>
```

- [ ] **Step 4: StaffFormView.xaml**

`src/FishingMartPos/Views/StaffFormView.xaml`:
```xml
<UserControl x:Class="FishingMartPos.Views.StaffFormView"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006"
             xmlns:d="http://schemas.microsoft.com/expression/blend/2008"
             mc:Ignorable="d">
    <UserControl.Resources>
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
                <Button Content="← 직원목록으로" Padding="12,5" FontSize="12" FontWeight="Bold"
                        Background="{DynamicResource LogoutButtonBackground}" Foreground="{DynamicResource MutedText}"
                        BorderBrush="{DynamicResource InputBorder}" BorderThickness="1"
                        HorizontalAlignment="Right" VerticalAlignment="Center"
                        Command="{Binding CancelCommand}" />
            </Grid>
        </Border>

        <ScrollViewer Grid.Row="1" VerticalScrollBarVisibility="Auto">
            <Border Margin="24,20" Background="White" BorderBrush="{DynamicResource CardBorder}" BorderThickness="1"
                    Padding="24" MaxWidth="420" HorizontalAlignment="Left">
                <StackPanel>
                    <Border Background="{DynamicResource CardBorder}" BorderBrush="{DynamicResource ErrorBorder}" BorderThickness="1"
                            Padding="12,8" Margin="0,0,0,16"
                            Visibility="{Binding ErrorMessage, Converter={StaticResource NullToVisibilityConverter}}">
                        <TextBlock Text="{Binding ErrorMessage}" Foreground="{DynamicResource ErrorIconBackground}" FontSize="12" FontWeight="Bold" />
                    </Border>

                    <TextBlock Text="직원코드" FontSize="12" FontWeight="Bold" Foreground="{DynamicResource MutedText}" Margin="0,0,0,6" />
                    <TextBox Text="{Binding StaffCode, UpdateSourceTrigger=PropertyChanged}" IsReadOnly="{Binding IsEditMode}" Padding="8,6" Margin="0,0,0,14" />

                    <TextBlock Text="이름" FontSize="12" FontWeight="Bold" Foreground="{DynamicResource MutedText}" Margin="0,0,0,6" />
                    <TextBox Text="{Binding StaffName, UpdateSourceTrigger=PropertyChanged}" Padding="8,6" Margin="0,0,0,14" />

                    <TextBlock Text="권한" FontSize="12" FontWeight="Bold" Foreground="{DynamicResource MutedText}" Margin="0,0,0,6" />
                    <ComboBox ItemsSource="{Binding RoleOptions}" SelectedItem="{Binding SelectedRole}" DisplayMemberPath="Name" Padding="8,6" Margin="0,0,0,14" />

                    <CheckBox Content="사용" IsChecked="{Binding IsActive}" FontSize="13" Margin="0,0,0,14" />

                    <TextBlock Text="PIN (4자리 숫자, 수정 시 비워두면 기존 유지)" FontSize="12" FontWeight="Bold" Foreground="{DynamicResource MutedText}" Margin="0,0,0,6" />
                    <TextBox Text="{Binding PinInput, UpdateSourceTrigger=PropertyChanged}" MaxLength="4" Padding="8,6" Margin="0,0,0,20" />

                    <Button Content="{Binding SaveButtonText}" Padding="0,12" Background="{DynamicResource Accent}" Foreground="White"
                            FontSize="14" FontWeight="Bold" Command="{Binding SaveCommand}" />
                </StackPanel>
            </Border>
        </ScrollViewer>

        <Grid Grid.RowSpan="2" Background="{DynamicResource ModalOverlay}"
              Visibility="{Binding ToastMessage, Converter={StaticResource NullToVisibilityConverter}}">
            <Border Background="White" BorderBrush="{DynamicResource CardBorder}" BorderThickness="1"
                    Padding="48,36" HorizontalAlignment="Center" VerticalAlignment="Center">
                <StackPanel HorizontalAlignment="Center">
                    <Border Width="56" Height="56" Margin="0,0,0,12" Background="{DynamicResource Accent}">
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

- [ ] **Step 5: PrinterSettingsView.xaml / ReceiptSettingsView.xaml**

`src/FishingMartPos/Views/PrinterSettingsView.xaml`:
```xml
<UserControl x:Class="FishingMartPos.Views.PrinterSettingsView"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006"
             xmlns:d="http://schemas.microsoft.com/expression/blend/2008"
             mc:Ignorable="d">
    <UserControl.Resources>
        <local:NullToVisibilityConverter x:Key="NullToVisibilityConverter" xmlns:local="clr-namespace:FishingMartPos.Converters" />
    </UserControl.Resources>
    <Grid Background="{DynamicResource MenuBackground}">
        <Grid.RowDefinitions>
            <RowDefinition Height="Auto" />
            <RowDefinition Height="*" />
        </Grid.RowDefinitions>

        <Border Grid.Row="0" Background="White" BorderBrush="{DynamicResource HeaderBorder}" BorderThickness="0,0,0,1">
            <Grid Margin="16,0" Height="42">
                <TextBlock Text="프린터설정" FontSize="14" FontWeight="Bold"
                           Foreground="{DynamicResource TitleText}" VerticalAlignment="Center" HorizontalAlignment="Left" />
                <Button Content="← 환경설정으로" Padding="12,5" FontSize="12" FontWeight="Bold"
                        Background="{DynamicResource LogoutButtonBackground}" Foreground="{DynamicResource MutedText}"
                        BorderBrush="{DynamicResource InputBorder}" BorderThickness="1"
                        HorizontalAlignment="Right" VerticalAlignment="Center"
                        Command="{Binding GoToSettingsCommand}" />
            </Grid>
        </Border>

        <ScrollViewer Grid.Row="1" VerticalScrollBarVisibility="Auto">
            <Border Margin="24,20" Background="White" BorderBrush="{DynamicResource CardBorder}" BorderThickness="1"
                    Padding="24" MaxWidth="420" HorizontalAlignment="Left">
                <StackPanel>
                    <TextBlock Text="프린터 포트" FontSize="12" FontWeight="Bold" Foreground="{DynamicResource MutedText}" Margin="0,0,0,6" />
                    <TextBox Text="{Binding PrinterPort, UpdateSourceTrigger=PropertyChanged}" Padding="8,6" Margin="0,0,0,14" />

                    <TextBlock Text="프린터 모델명" FontSize="12" FontWeight="Bold" Foreground="{DynamicResource MutedText}" Margin="0,0,0,6" />
                    <TextBox Text="{Binding PrinterName, UpdateSourceTrigger=PropertyChanged}" Padding="8,6" Margin="0,0,0,14" />

                    <CheckBox Content="돈통 신호 사용" IsChecked="{Binding DrawerKickEnabled}" FontSize="13" Margin="0,0,0,20" />

                    <Button Content="저장" Padding="0,12" Background="{DynamicResource Accent}" Foreground="White"
                            FontSize="14" FontWeight="Bold" Command="{Binding SaveCommand}" Margin="0,0,0,10" />
                    <Button Content="테스트 인쇄" Padding="0,10" Command="{Binding TestPrintCommand}" />
                </StackPanel>
            </Border>
        </ScrollViewer>

        <Grid Grid.RowSpan="2" Background="{DynamicResource ModalOverlay}"
              Visibility="{Binding ToastMessage, Converter={StaticResource NullToVisibilityConverter}}">
            <Border Background="White" BorderBrush="{DynamicResource CardBorder}" BorderThickness="1"
                    Padding="48,36" HorizontalAlignment="Center" VerticalAlignment="Center">
                <TextBlock Text="{Binding ToastMessage}" FontSize="16" FontWeight="Bold" Foreground="{DynamicResource TitleText}" />
            </Border>
        </Grid>
    </Grid>
</UserControl>
```

`src/FishingMartPos/Views/ReceiptSettingsView.xaml`:
```xml
<UserControl x:Class="FishingMartPos.Views.ReceiptSettingsView"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006"
             xmlns:d="http://schemas.microsoft.com/expression/blend/2008"
             mc:Ignorable="d">
    <UserControl.Resources>
        <local:NullToVisibilityConverter x:Key="NullToVisibilityConverter" xmlns:local="clr-namespace:FishingMartPos.Converters" />
    </UserControl.Resources>
    <Grid Background="{DynamicResource MenuBackground}">
        <Grid.RowDefinitions>
            <RowDefinition Height="Auto" />
            <RowDefinition Height="*" />
        </Grid.RowDefinitions>

        <Border Grid.Row="0" Background="White" BorderBrush="{DynamicResource HeaderBorder}" BorderThickness="0,0,0,1">
            <Grid Margin="16,0" Height="42">
                <TextBlock Text="영수증설정" FontSize="14" FontWeight="Bold"
                           Foreground="{DynamicResource TitleText}" VerticalAlignment="Center" HorizontalAlignment="Left" />
                <Button Content="← 환경설정으로" Padding="12,5" FontSize="12" FontWeight="Bold"
                        Background="{DynamicResource LogoutButtonBackground}" Foreground="{DynamicResource MutedText}"
                        BorderBrush="{DynamicResource InputBorder}" BorderThickness="1"
                        HorizontalAlignment="Right" VerticalAlignment="Center"
                        Command="{Binding GoToSettingsCommand}" />
            </Grid>
        </Border>

        <ScrollViewer Grid.Row="1" VerticalScrollBarVisibility="Auto">
            <Border Margin="24,20" Background="White" BorderBrush="{DynamicResource CardBorder}" BorderThickness="1"
                    Padding="24" MaxWidth="420" HorizontalAlignment="Left">
                <StackPanel>
                    <TextBlock Text="상단 문구" FontSize="12" FontWeight="Bold" Foreground="{DynamicResource MutedText}" Margin="0,0,0,6" />
                    <TextBox Text="{Binding HeaderText, UpdateSourceTrigger=PropertyChanged}" AcceptsReturn="True" TextWrapping="Wrap"
                              Height="80" Padding="8,6" Margin="0,0,0,14" />

                    <TextBlock Text="하단 문구" FontSize="12" FontWeight="Bold" Foreground="{DynamicResource MutedText}" Margin="0,0,0,6" />
                    <TextBox Text="{Binding FooterText, UpdateSourceTrigger=PropertyChanged}" AcceptsReturn="True" TextWrapping="Wrap"
                              Height="80" Padding="8,6" Margin="0,0,0,20" />

                    <Button Content="저장" Padding="0,12" Background="{DynamicResource Accent}" Foreground="White"
                            FontSize="14" FontWeight="Bold" Command="{Binding SaveCommand}" />
                </StackPanel>
            </Border>
        </ScrollViewer>

        <Grid Grid.RowSpan="2" Background="{DynamicResource ModalOverlay}"
              Visibility="{Binding ToastMessage, Converter={StaticResource NullToVisibilityConverter}}">
            <Border Background="White" BorderBrush="{DynamicResource CardBorder}" BorderThickness="1"
                    Padding="48,36" HorizontalAlignment="Center" VerticalAlignment="Center">
                <TextBlock Text="{Binding ToastMessage}" FontSize="16" FontWeight="Bold" Foreground="{DynamicResource TitleText}" />
            </Border>
        </Grid>
    </Grid>
</UserControl>
```

- [ ] **Step 6: SystemInfoView.xaml**

`src/FishingMartPos/Views/SystemInfoView.xaml`:
```xml
<UserControl x:Class="FishingMartPos.Views.SystemInfoView"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006"
             xmlns:d="http://schemas.microsoft.com/expression/blend/2008"
             mc:Ignorable="d">
    <Grid Background="{DynamicResource MenuBackground}">
        <Grid.RowDefinitions>
            <RowDefinition Height="Auto" />
            <RowDefinition Height="*" />
        </Grid.RowDefinitions>

        <Border Grid.Row="0" Background="White" BorderBrush="{DynamicResource HeaderBorder}" BorderThickness="0,0,0,1">
            <Grid Margin="16,0" Height="42">
                <TextBlock Text="시스템정보" FontSize="14" FontWeight="Bold"
                           Foreground="{DynamicResource TitleText}" VerticalAlignment="Center" HorizontalAlignment="Left" />
                <Button Content="← 환경설정으로" Padding="12,5" FontSize="12" FontWeight="Bold"
                        Background="{DynamicResource LogoutButtonBackground}" Foreground="{DynamicResource MutedText}"
                        BorderBrush="{DynamicResource InputBorder}" BorderThickness="1"
                        HorizontalAlignment="Right" VerticalAlignment="Center"
                        Command="{Binding GoToSettingsCommand}" />
            </Grid>
        </Border>

        <Border Grid.Row="1" Margin="24,20" Background="White" BorderBrush="{DynamicResource CardBorder}" BorderThickness="1"
                Padding="24" MaxWidth="420" HorizontalAlignment="Left" VerticalAlignment="Top">
            <StackPanel>
                <Grid Margin="0,0,0,10">
                    <Grid.ColumnDefinitions>
                        <ColumnDefinition Width="120" />
                        <ColumnDefinition Width="*" />
                    </Grid.ColumnDefinitions>
                    <TextBlock Text="앱 버전" FontSize="12" Foreground="{DynamicResource MutedText}" />
                    <TextBlock Grid.Column="1" Text="{Binding AppVersionStr}" FontSize="13" />
                </Grid>
                <Grid Margin="0,0,0,10">
                    <Grid.ColumnDefinitions>
                        <ColumnDefinition Width="120" />
                        <ColumnDefinition Width="*" />
                    </Grid.ColumnDefinitions>
                    <TextBlock Text="DB 버전" FontSize="12" Foreground="{DynamicResource MutedText}" />
                    <TextBlock Grid.Column="1" Text="{Binding DbVersionStr}" FontSize="13" />
                </Grid>
                <Grid Margin="0,0,0,10">
                    <Grid.ColumnDefinitions>
                        <ColumnDefinition Width="120" />
                        <ColumnDefinition Width="*" />
                    </Grid.ColumnDefinitions>
                    <TextBlock Text="현재 POS 단말" FontSize="12" Foreground="{DynamicResource MutedText}" />
                    <TextBlock Grid.Column="1" Text="{Binding PosTerminalStr}" FontSize="13" />
                </Grid>
                <Grid>
                    <Grid.ColumnDefinitions>
                        <ColumnDefinition Width="120" />
                        <ColumnDefinition Width="*" />
                    </Grid.ColumnDefinitions>
                    <TextBlock Text="DB 연결상태" FontSize="12" Foreground="{DynamicResource MutedText}" />
                    <TextBlock Grid.Column="1" Text="{Binding DbConnectionStatusStr}" FontSize="13" />
                </Grid>
            </StackPanel>
        </Border>
    </Grid>
</UserControl>
```

- [ ] **Step 7: CodeManageView.xaml**

`src/FishingMartPos/Views/CodeManageView.xaml`:
```xml
<UserControl x:Class="FishingMartPos.Views.CodeManageView"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006"
             xmlns:d="http://schemas.microsoft.com/expression/blend/2008"
             mc:Ignorable="d">
    <UserControl.Resources>
        <local:NullToVisibilityConverter x:Key="NullToVisibilityConverter" xmlns:local="clr-namespace:FishingMartPos.Converters" />
    </UserControl.Resources>
    <Grid Background="{DynamicResource MenuBackground}">
        <Grid.RowDefinitions>
            <RowDefinition Height="Auto" />
            <RowDefinition Height="*" />
        </Grid.RowDefinitions>

        <Border Grid.Row="0" Background="White" BorderBrush="{DynamicResource HeaderBorder}" BorderThickness="0,0,0,1">
            <Grid Margin="16,0" Height="42">
                <TextBlock Text="상품코드관리" FontSize="14" FontWeight="Bold"
                           Foreground="{DynamicResource TitleText}" VerticalAlignment="Center" HorizontalAlignment="Left" />
                <Button Content="← 환경설정으로" Padding="12,5" FontSize="12" FontWeight="Bold"
                        Background="{DynamicResource LogoutButtonBackground}" Foreground="{DynamicResource MutedText}"
                        BorderBrush="{DynamicResource InputBorder}" BorderThickness="1"
                        HorizontalAlignment="Right" VerticalAlignment="Center"
                        Command="{Binding GoToSettingsCommand}" />
            </Grid>
        </Border>

        <Grid Grid.Row="1" Margin="16">
            <Grid.ColumnDefinitions>
                <ColumnDefinition Width="*" />
                <ColumnDefinition Width="16" />
                <ColumnDefinition Width="*" />
                <ColumnDefinition Width="16" />
                <ColumnDefinition Width="*" />
            </Grid.ColumnDefinitions>

            <Border Grid.Column="0" Background="White" BorderBrush="{DynamicResource CardBorder}" BorderThickness="1" Padding="16">
                <StackPanel>
                    <TextBlock Text="대분류" FontSize="13" FontWeight="Bold" Foreground="{DynamicResource TitleText}" Margin="0,0,0,10" />
                    <Border Background="{DynamicResource CardBorder}" BorderBrush="{DynamicResource ErrorBorder}" BorderThickness="1"
                            Padding="8,6" Margin="0,0,0,10"
                            Visibility="{Binding ErrorMessage, Converter={StaticResource NullToVisibilityConverter}}">
                        <TextBlock Text="{Binding ErrorMessage}" Foreground="{DynamicResource ErrorIconBackground}" FontSize="11" FontWeight="Bold" TextWrapping="Wrap" />
                    </Border>
                    <ItemsControl ItemsSource="{Binding MajorCodes}">
                        <ItemsControl.ItemTemplate>
                            <DataTemplate>
                                <Grid Margin="0,4">
                                    <TextBlock Text="{Binding Name}" FontSize="12" />
                                    <Button Content="삭제" HorizontalAlignment="Right" Padding="6,2" FontSize="10"
                                            Command="{Binding DataContext.DeleteMajorCodeCommand, RelativeSource={RelativeSource AncestorType=UserControl}}"
                                            CommandParameter="{Binding}" />
                                </Grid>
                            </DataTemplate>
                        </ItemsControl.ItemTemplate>
                    </ItemsControl>
                    <Grid Margin="0,10,0,0">
                        <Grid.ColumnDefinitions>
                            <ColumnDefinition Width="70" />
                            <ColumnDefinition Width="*" />
                            <ColumnDefinition Width="Auto" />
                        </Grid.ColumnDefinitions>
                        <TextBox Text="{Binding NewMajorCode, UpdateSourceTrigger=PropertyChanged}" Margin="0,0,4,0" />
                        <TextBox Grid.Column="1" Text="{Binding NewMajorName, UpdateSourceTrigger=PropertyChanged}" Margin="0,0,4,0" />
                        <Button Grid.Column="2" Content="추가" Command="{Binding AddMajorCodeCommand}" />
                    </Grid>
                </StackPanel>
            </Border>

            <Border Grid.Column="2" Background="White" BorderBrush="{DynamicResource CardBorder}" BorderThickness="1" Padding="16">
                <StackPanel>
                    <TextBlock Text="소분류" FontSize="13" FontWeight="Bold" Foreground="{DynamicResource TitleText}" Margin="0,0,0,10" />
                    <ItemsControl ItemsSource="{Binding MinorCodes}">
                        <ItemsControl.ItemTemplate>
                            <DataTemplate>
                                <Grid Margin="0,4">
                                    <TextBlock Text="{Binding Name}" FontSize="12" />
                                    <Button Content="삭제" HorizontalAlignment="Right" Padding="6,2" FontSize="10"
                                            Command="{Binding DataContext.DeleteMinorCodeCommand, RelativeSource={RelativeSource AncestorType=UserControl}}"
                                            CommandParameter="{Binding}" />
                                </Grid>
                            </DataTemplate>
                        </ItemsControl.ItemTemplate>
                    </ItemsControl>
                    <Grid Margin="0,10,0,0">
                        <Grid.ColumnDefinitions>
                            <ColumnDefinition Width="70" />
                            <ColumnDefinition Width="*" />
                            <ColumnDefinition Width="Auto" />
                        </Grid.ColumnDefinitions>
                        <TextBox Text="{Binding NewMinorCode, UpdateSourceTrigger=PropertyChanged}" Margin="0,0,4,0" />
                        <TextBox Grid.Column="1" Text="{Binding NewMinorName, UpdateSourceTrigger=PropertyChanged}" Margin="0,0,4,0" />
                        <Button Grid.Column="2" Content="추가" Command="{Binding AddMinorCodeCommand}" />
                    </Grid>
                </StackPanel>
            </Border>

            <Border Grid.Column="4" Background="White" BorderBrush="{DynamicResource CardBorder}" BorderThickness="1" Padding="16">
                <StackPanel>
                    <TextBlock Text="POS분류" FontSize="13" FontWeight="Bold" Foreground="{DynamicResource TitleText}" Margin="0,0,0,10" />
                    <ItemsControl ItemsSource="{Binding PosCatCodes}">
                        <ItemsControl.ItemTemplate>
                            <DataTemplate>
                                <Grid Margin="0,4">
                                    <TextBlock Text="{Binding Name}" FontSize="12" />
                                    <Button Content="삭제" HorizontalAlignment="Right" Padding="6,2" FontSize="10"
                                            Command="{Binding DataContext.DeletePosCatCodeCommand, RelativeSource={RelativeSource AncestorType=UserControl}}"
                                            CommandParameter="{Binding}" />
                                </Grid>
                            </DataTemplate>
                        </ItemsControl.ItemTemplate>
                    </ItemsControl>
                    <Grid Margin="0,10,0,0">
                        <Grid.ColumnDefinitions>
                            <ColumnDefinition Width="70" />
                            <ColumnDefinition Width="*" />
                            <ColumnDefinition Width="Auto" />
                        </Grid.ColumnDefinitions>
                        <TextBox Text="{Binding NewPosCatCode, UpdateSourceTrigger=PropertyChanged}" Margin="0,0,4,0" />
                        <TextBox Grid.Column="1" Text="{Binding NewPosCatName, UpdateSourceTrigger=PropertyChanged}" Margin="0,0,4,0" />
                        <Button Grid.Column="2" Content="추가" Command="{Binding AddPosCatCodeCommand}" />
                    </Grid>
                </StackPanel>
            </Border>
        </Grid>
    </Grid>
</UserControl>
```

- [ ] **Step 8: MainWindow.xaml에 7개 DataTemplate 추가**

`src/FishingMartPos/MainWindow.xaml`에서 `<DataTemplate DataType="{x:Type vm:SalesReportViewModel}">...</DataTemplate>` 바로 다음, `</Window.Resources>` 앞에 아래 7개를 추가한다:

```xml
        <DataTemplate DataType="{x:Type vm:SettingsViewModel}">
            <views:SettingsView />
        </DataTemplate>
        <DataTemplate DataType="{x:Type vm:StaffListViewModel}">
            <views:StaffListView />
        </DataTemplate>
        <DataTemplate DataType="{x:Type vm:StaffFormViewModel}">
            <views:StaffFormView />
        </DataTemplate>
        <DataTemplate DataType="{x:Type vm:PrinterSettingsViewModel}">
            <views:PrinterSettingsView />
        </DataTemplate>
        <DataTemplate DataType="{x:Type vm:ReceiptSettingsViewModel}">
            <views:ReceiptSettingsView />
        </DataTemplate>
        <DataTemplate DataType="{x:Type vm:SystemInfoViewModel}">
            <views:SystemInfoView />
        </DataTemplate>
        <DataTemplate DataType="{x:Type vm:CodeManageViewModel}">
            <views:CodeManageView />
        </DataTemplate>
```

- [ ] **Step 9: 스모크 테스트 작성**

`tests/FishingMartPos.Tests/Views/SettingsScreensSmokeTests.cs`:
```csharp
using FishingMartPos.Views;
using Xunit;

namespace FishingMartPos.Tests.Views;

public class SettingsScreensSmokeTests
{
    [Fact]
    public void SettingsView_ConstructsWithoutException()
    {
        StaTestHelper.RunOnSta(() =>
        {
            var view = new SettingsView();
            Assert.NotNull(view);
        });
    }

    [Fact]
    public void StaffListView_ConstructsWithoutException()
    {
        StaTestHelper.RunOnSta(() =>
        {
            var view = new StaffListView();
            Assert.NotNull(view);
        });
    }

    [Fact]
    public void StaffFormView_ConstructsWithoutException()
    {
        StaTestHelper.RunOnSta(() =>
        {
            var view = new StaffFormView();
            Assert.NotNull(view);
        });
    }

    [Fact]
    public void PrinterSettingsView_ConstructsWithoutException()
    {
        StaTestHelper.RunOnSta(() =>
        {
            var view = new PrinterSettingsView();
            Assert.NotNull(view);
        });
    }

    [Fact]
    public void ReceiptSettingsView_ConstructsWithoutException()
    {
        StaTestHelper.RunOnSta(() =>
        {
            var view = new ReceiptSettingsView();
            Assert.NotNull(view);
        });
    }

    [Fact]
    public void SystemInfoView_ConstructsWithoutException()
    {
        StaTestHelper.RunOnSta(() =>
        {
            var view = new SystemInfoView();
            Assert.NotNull(view);
        });
    }

    [Fact]
    public void CodeManageView_ConstructsWithoutException()
    {
        StaTestHelper.RunOnSta(() =>
        {
            var view = new CodeManageView();
            Assert.NotNull(view);
        });
    }
}
```

- [ ] **Step 10: 빌드 + 전체 테스트 실행**

Run: `dotnet build`
Expected: 빌드 성공.

Run: `dotnet test tests/FishingMartPos.Tests`
Expected: PASS (전체 스위트 — 이 플랜에서 추가/수정한 모든 테스트 포함).

- [ ] **Step 11: 커밋**

```bash
git add src/FishingMartPos/Views/SettingsView.xaml src/FishingMartPos/Views/SettingsView.xaml.cs src/FishingMartPos/Views/StaffListView.xaml src/FishingMartPos/Views/StaffListView.xaml.cs src/FishingMartPos/Views/StaffFormView.xaml src/FishingMartPos/Views/StaffFormView.xaml.cs src/FishingMartPos/Views/PrinterSettingsView.xaml src/FishingMartPos/Views/PrinterSettingsView.xaml.cs src/FishingMartPos/Views/ReceiptSettingsView.xaml src/FishingMartPos/Views/ReceiptSettingsView.xaml.cs src/FishingMartPos/Views/SystemInfoView.xaml src/FishingMartPos/Views/SystemInfoView.xaml.cs src/FishingMartPos/Views/CodeManageView.xaml src/FishingMartPos/Views/CodeManageView.xaml.cs src/FishingMartPos/MainWindow.xaml tests/FishingMartPos.Tests/Views/SettingsScreensSmokeTests.cs
git commit -m "환경설정 7개 하위화면 XAML 뷰 구현 및 MainWindow 배선"
```

---

## Self-Review Notes

- **Spec 커버리지 확인**: 배경/목표의 5개 하위화면(직원관리·프린터설정·영수증설정·시스템정보·상품코드관리) 모두 Task 3~5에서 ViewModel, Task 7에서 View가 구현됨. ADMIN 이중 가드(타일 Visibility + 커맨드 가드)는 Task 6에서 구현. PIN 재설정("관리자가 새 PIN을 직접 입력")은 `StaffFormViewModel.PinInput`(수정 모드에서 공란=유지)로 구현. 테스트인쇄 "지원 예정" 안내는 `PrinterSettingsViewModel.TestPrint`로 구현. 상품코드관리의 3열 항상-펼침 구조는 `CodeManageViewModel`/`CodeManageView`로 구현하며 `InventoryFormViewModel`과 의도적으로 코드 비공유.
- **Placeholder 스캔**: 모든 Step에 실제 완전한 코드가 포함되어 있으며 "TODO"/"나머지는 비슷하게" 같은 표현 없음.
- **타입 일관성 확인**: `SettingsViewModel`의 5개 팩토리 프로퍼티 이름(`StaffListViewModelFactory` 등)이 Task 6(정의)과 Task 6의 App.xaml.cs 배선, Task 3/6의 테스트에서 모두 동일하게 사용됨. `StaffFormViewModel` 생성자 파라미터 순서(`staffRepository, delay, navigation, returnTo, editingStaff`)가 Task 3의 구현·테스트와 Task 6의 App.xaml.cs 배선에서 일치. `PrinterConfig.DrawerKickEnabled`(bool)이 모델(Task 2)·ViewModel(Task 4)·XAML(Task 7)에서 일관되게 bool로 사용되고 DB 변환은 리포지토리 계층에서만 발생.
- **Task 3의 선행 의존성**: `SettingsViewModel`을 Task 6 이전에 참조해야 하므로 Task 3 Step 0에서 최소 골격을 먼저 만들고 Task 6에서 완성본으로 교체하는 2단계 구조를 명시함 — SDD로 실행 시 Task 3, 4, 5의 구현자에게 이 골격이 이미 존재한다는 점과 Task 6에서 교체될 예정임을 브리핑에 포함할 것.
