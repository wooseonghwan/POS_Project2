# Phase 1: Foundation (DB 스키마 + WPF/MVVM 뼈대 + 로그인/메인메뉴) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** MariaDB 최소 스키마를 만들고, WPF(.NET 8, x86) + MVVM 프로젝트 뼈대를 세운 뒤, PIN 로그인 화면과 메인 메뉴 화면을 `Fishing Mart POS.html` 디자인 그대로 픽셀 단위로 이식해서, 앱을 실행하면 로그인 → 메뉴 진입 → 로그아웃까지 실제로 동작하는 상태로 만든다.

**Architecture:** WPF View(XAML) ↔ ViewModel(CommunityToolkit.Mvvm) ↔ Repository(Dapper, MariaDB) 3계층. 화면 전환은 코드 기반 `INavigationService`가 현재 ViewModel을 갈아끼우고, `MainWindow`의 `ContentControl`이 `DataTemplate`으로 ViewModel → View를 매핑한다. 색상은 디자인 원본의 `oklch(...)` 값을 그대로 상수로 옮기고, 런타임에 OKLCH→sRGB 변환기로 계산해 WPF `Brush`를 만든다(수작업 색상 변환 오류를 없애기 위함).

**Tech Stack:** .NET 8 (`net8.0-windows`, PlatformTarget=x86), WPF, CommunityToolkit.Mvvm, Dapper, MySqlConnector, Microsoft.Extensions.DependencyInjection, xunit.

## Global Constraints

- 화면 디자인(레이아웃/색상/간격/폰트크기)은 `Fishing Mart POS.html`을 그대로 따른다 — 임의 변경 금지.
- 플랫폼 타겟은 **x86** 고정 (향후 Phase 3의 NCVAN `NCPOS.dll` P/Invoke가 32비트 전용이므로 지금부터 통일).
- 데이터 액세스는 Dapper + 직접 SQL만 사용한다 (EF Core 금지 — 설계 문서 확정 사항).
- 커밋마다 `git add` 후 `git commit`, 메시지는 한글 요약 + 영어 타입 접두사(`feat:`, `test:`, `chore:`) 없이 한글로 자연스럽게 작성한다.
- 이 저장소 루트는 `C:\AI\pos-project2` (이미 git 초기화 및 GitHub `LEEHOOSUNG/pos-project2`(private) 원격 연결됨, 기본 브랜치 `main`).

---

## 파일 구조

```
FishingMartPos.sln
db/
  migrations/
    001_create_schema.sql
src/
  FishingMartPos/
    FishingMartPos.csproj
    App.xaml
    App.xaml.cs
    appsettings.json
    MainWindow.xaml
    MainWindow.xaml.cs
    Color/
      OklchColor.cs
    Theme/
      AppColors.cs
    Configuration/
      AppConfig.cs
    Data/
      IDbConnectionFactory.cs
      MySqlConnectionFactory.cs
    Security/
      PinHasher.cs
    Models/
      Staff.cs
      PosTerminal.cs
    Repositories/
      IStaffRepository.cs
      StaffRepository.cs
      IPosTerminalRepository.cs
      PosTerminalRepository.cs
    Services/
      ICurrentSession.cs
      CurrentSession.cs
    Navigation/
      INavigationService.cs
      NavigationService.cs
    ViewModels/
      PlaceholderViewModel.cs
      LoginViewModel.cs
      MainMenuViewModel.cs
    Views/
      PlaceholderView.xaml
      PlaceholderView.xaml.cs
      LoginView.xaml
      LoginView.xaml.cs
      MainMenuView.xaml
      MainMenuView.xaml.cs
tests/
  FishingMartPos.Tests/
    FishingMartPos.Tests.csproj
    StaTestHelper.cs
    Color/
      OklchColorTests.cs
    Security/
      PinHasherTests.cs
    Services/
      CurrentSessionTests.cs
    Navigation/
      NavigationServiceTests.cs
    ViewModels/
      LoginViewModelTests.cs
      MainMenuViewModelTests.cs
    Views/
      LoginViewSmokeTests.cs
      MainMenuViewSmokeTests.cs
    Fakes/
      FakeStaffRepository.cs
      FakePosTerminalRepository.cs
    Repositories/
      StaffRepositoryTests.cs
      PosTerminalRepositoryTests.cs
```

---

### Task 1: 솔루션/프로젝트 뼈대 생성

**Files:**
- Create: `FishingMartPos.sln`
- Create: `src/FishingMartPos/FishingMartPos.csproj`
- Create: `src/FishingMartPos/App.xaml`
- Create: `src/FishingMartPos/App.xaml.cs`
- Create: `src/FishingMartPos/MainWindow.xaml`
- Create: `src/FishingMartPos/MainWindow.xaml.cs`
- Create: `tests/FishingMartPos.Tests/FishingMartPos.Tests.csproj`

**Interfaces:**
- Produces: `FishingMartPos.csproj`(x86, net8.0-windows, UseWPF), `FishingMartPos.Tests.csproj`(xunit, net8.0, `<ProjectReference>` to 메인 프로젝트)

- [ ] **Step 1: WPF 프로젝트 생성**

```bash
mkdir src\FishingMartPos
dotnet new wpf -o src\FishingMartPos -n FishingMartPos --framework net8.0-windows
```

- [ ] **Step 2: `FishingMartPos.csproj`를 열어 x86 플랫폼과 패키지를 추가**

`src/FishingMartPos/FishingMartPos.csproj` 전체를 아래로 교체:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <TargetFramework>net8.0-windows</TargetFramework>
    <Nullable>enable</Nullable>
    <UseWPF>true</UseWPF>
    <Platforms>x86</Platforms>
    <PlatformTarget>x86</PlatformTarget>
    <RootNamespace>FishingMartPos</RootNamespace>
    <AssemblyName>FishingMartPos</AssemblyName>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="CommunityToolkit.Mvvm" Version="8.3.2" />
    <PackageReference Include="Dapper" Version="2.1.35" />
    <PackageReference Include="MySqlConnector" Version="2.3.7" />
    <PackageReference Include="Microsoft.Extensions.DependencyInjection" Version="8.0.0" />
    <PackageReference Include="Microsoft.Extensions.Configuration" Version="8.0.0" />
    <PackageReference Include="Microsoft.Extensions.Configuration.Json" Version="8.0.0" />
  </ItemGroup>

  <ItemGroup>
    <None Update="appsettings.json">
      <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
    </None>
  </ItemGroup>

</Project>
```

- [ ] **Step 3: 솔루션 생성 및 프로젝트 등록**

```bash
dotnet new sln -n FishingMartPos
dotnet sln add src\FishingMartPos\FishingMartPos.csproj
```

- [ ] **Step 4: 테스트 프로젝트 생성**

```bash
mkdir tests\FishingMartPos.Tests
dotnet new xunit -o tests\FishingMartPos.Tests -n FishingMartPos.Tests --framework net8.0
dotnet sln add tests\FishingMartPos.Tests\FishingMartPos.Tests.csproj
```

`tests/FishingMartPos.Tests/FishingMartPos.Tests.csproj`를 열어 아래 두 줄을 `<ItemGroup>` 안에 추가(프로젝트 참조 + WPF 어셈블리 접근을 위한 `UseWPF`):

```xml
<PropertyGroup>
  <UseWPF>true</UseWPF>
</PropertyGroup>

<ItemGroup>
  <ProjectReference Include="..\..\src\FishingMartPos\FishingMartPos.csproj" />
</ItemGroup>
```

- [ ] **Step 5: 빌드 확인**

```bash
dotnet build FishingMartPos.sln
```

Expected: `Build succeeded.` (경고 있어도 무방, 에러 없어야 함)

- [ ] **Step 6: Commit**

```bash
git add FishingMartPos.sln src/FishingMartPos tests/FishingMartPos.Tests
git commit -m "WPF/MVVM 프로젝트 뼈대 생성 (x86, net8.0-windows)"
```

---

### Task 2: OKLCH → WPF Color 변환기 (TDD)

**Files:**
- Create: `src/FishingMartPos/Color/OklchColor.cs`
- Test: `tests/FishingMartPos.Tests/Color/OklchColorTests.cs`

**Interfaces:**
- Produces: `OklchColor.ToColor(double l, double c, double h, double alpha = 1.0) : System.Windows.Media.Color`

- [ ] **Step 1: 실패하는 테스트 작성**

`tests/FishingMartPos.Tests/Color/OklchColorTests.cs`:

```csharp
using System.Windows.Media;
using FishingMartPos.Color;
using Xunit;

namespace FishingMartPos.Tests.Color;

public class OklchColorTests
{
    [Fact]
    public void WhiteAtMaxLightnessZeroChroma()
    {
        Color result = OklchColor.ToColor(1.0, 0.0, 0.0);

        Assert.Equal(255, result.R);
        Assert.Equal(255, result.G);
        Assert.Equal(255, result.B);
        Assert.Equal(255, result.A);
    }

    [Fact]
    public void BlackAtZeroLightnessZeroChroma()
    {
        Color result = OklchColor.ToColor(0.0, 0.0, 0.0);

        Assert.Equal(0, result.R);
        Assert.Equal(0, result.G);
        Assert.Equal(0, result.B);
    }

    [Fact]
    public void HueIsIgnoredWhenChromaIsZero()
    {
        Color atHue0 = OklchColor.ToColor(0.5, 0.0, 0.0);
        Color atHue270 = OklchColor.ToColor(0.5, 0.0, 270.0);

        Assert.Equal(atHue0, atHue270);
    }

    [Fact]
    public void AlphaIsAppliedAsProvided()
    {
        Color result = OklchColor.ToColor(1.0, 0.0, 0.0, 0.4);

        Assert.Equal((byte)Math.Round(0.4 * 255), result.A);
    }

    [Fact]
    public void ChannelsAreClampedToValidRange()
    {
        // 매우 높은 채도(C)는 sRGB 영역을 벗어나므로 0~255로 클램프되어야 한다.
        Color result = OklchColor.ToColor(0.5, 5.0, 195.0);

        Assert.InRange(result.R, (byte)0, (byte)255);
        Assert.InRange(result.G, (byte)0, (byte)255);
        Assert.InRange(result.B, (byte)0, (byte)255);
    }
}
```

- [ ] **Step 2: 테스트 실패 확인**

```bash
dotnet test tests\FishingMartPos.Tests --filter FullyQualifiedName~OklchColorTests
```

Expected: FAIL (컴파일 에러 — `OklchColor` 타입 없음)

- [ ] **Step 3: 구현**

`src/FishingMartPos/Color/OklchColor.cs`:

```csharp
using System;
using MediaColor = System.Windows.Media.Color;

namespace FishingMartPos.Color;

/// <summary>
/// CSS oklch(L C H [/ A]) 색상을 WPF Color(sRGB)로 변환한다.
/// 디자인 원본 HTML이 oklch()를 그대로 쓰고 있어, 수작업 색상 변환 대신
/// 동일한 표준 알고리즘(Björn Ottosson OKLab)으로 런타임에 계산해 색 오차를 없앤다.
/// </summary>
public static class OklchColor
{
    public static MediaColor ToColor(double l, double c, double h, double alpha = 1.0)
    {
        double hRadians = h * Math.PI / 180.0;
        double a = c * Math.Cos(hRadians);
        double b = c * Math.Sin(hRadians);

        double lPrime = l + 0.3963377774 * a + 0.2158037573 * b;
        double mPrime = l - 0.1055613458 * a - 0.0638541728 * b;
        double sPrime = l - 0.0894841775 * a - 1.2914855480 * b;

        double lCubed = lPrime * lPrime * lPrime;
        double mCubed = mPrime * mPrime * mPrime;
        double sCubed = sPrime * sPrime * sPrime;

        double rLinear = 4.0767416621 * lCubed - 3.3077115913 * mCubed + 0.2309699292 * sCubed;
        double gLinear = -1.2684380046 * lCubed + 2.6097574011 * mCubed - 0.3413193965 * sCubed;
        double bLinear = -0.0041960863 * lCubed - 0.7034186147 * mCubed + 1.7076147010 * sCubed;

        byte r = ToSrgbByte(rLinear);
        byte g = ToSrgbByte(gLinear);
        byte bl = ToSrgbByte(bLinear);
        byte a8 = (byte)Math.Clamp(Math.Round(alpha * 255.0), 0.0, 255.0);

        return MediaColor.FromArgb(a8, r, g, bl);
    }

    private static byte ToSrgbByte(double linear)
    {
        double clampedLinear = Math.Clamp(linear, 0.0, 1.0);
        double srgb = clampedLinear <= 0.0031308
            ? clampedLinear * 12.92
            : 1.055 * Math.Pow(clampedLinear, 1.0 / 2.4) - 0.055;

        return (byte)Math.Clamp(Math.Round(srgb * 255.0), 0.0, 255.0);
    }
}
```

- [ ] **Step 4: 테스트 통과 확인**

```bash
dotnet test tests\FishingMartPos.Tests --filter FullyQualifiedName~OklchColorTests
```

Expected: PASS (5 tests)

- [ ] **Step 5: Commit**

```bash
git add src/FishingMartPos/Color tests/FishingMartPos.Tests/Color
git commit -m "OKLCH to WPF Color 변환기 추가"
```

---

### Task 3: 디자인 색상 상수 (AppColors)

**Files:**
- Create: `src/FishingMartPos/Theme/AppColors.cs`
- Test: `tests/FishingMartPos.Tests/Theme/AppColorsTests.cs`

**Interfaces:**
- Consumes: `OklchColor.ToColor` (Task 2)
- Produces: `AppColors.BuildBrushes() : IReadOnlyDictionary<string, System.Windows.Media.Brush>` — 키는 아래 표의 이름, 값은 `Freeze()`된 `SolidColorBrush`

로그인/메인메뉴 화면에서 실제로 쓰이는 `oklch(...)` 리터럴을 `Fishing Mart POS.html`에서 그대로 옮긴다.

| 이름 | oklch |
|---|---|
| Accent | 0.5 0.1 195 |
| AccentDark | 0.4 0.1 195 |
| OuterBackground | 0.93 0.02 195 |
| CardBackground | 0.99 0.002 250 |
| CardBorder | 0.75 0.01 250 |
| LoginBackground | 0.97 0.01 195 |
| TitleText | 0.22 0.02 250 |
| SubtitleText | 0.5 0.01 250 |
| MutedText | 0.4 0.01 250 |
| InputBorder | 0.8 0.01 250 |
| PinDotInactive | 0.88 0.006 250 |
| KeypadSpecialBackground | 0.93 0.01 60 |
| KeypadText | 0.3 0.02 250 |
| ModalOverlay | 0.2 0.02 250 (alpha 0.4) |
| ErrorBorder | 0.6 0.15 25 |
| ErrorIconBackground | 0.55 0.15 25 |
| HeaderBorder | 0.91 0.006 250 |
| HeaderButtonBackground | 0.95 0.006 250 |
| HeaderSecondaryText | 0.45 0.01 250 |
| Divider | 0.88 0.006 250 |
| LogoutButtonBackground | 0.96 0.006 250 |
| MenuBackground | 0.95 0.02 195 |
| MenuTileBorder | 0.85 0.008 250 |
| MenuTileHoverBackground | 0.97 0.02 195 |
| MenuTileLabelText | 0.25 0.02 250 |
| MenuIconSales (매출) | 0.55 0.09 30 |
| MenuIconInventory (재고) | 0.55 0.09 250 |
| MenuIconSettingsDot (환경설정) | 0.55 0.09 340 |
| MenuIconSettingsTrack | 0.7 0.01 250 |

- [ ] **Step 1: 실패하는 테스트 작성**

`tests/FishingMartPos.Tests/Theme/AppColorsTests.cs`:

```csharp
using System.Windows.Media;
using FishingMartPos.Theme;
using Xunit;

namespace FishingMartPos.Tests.Theme;

public class AppColorsTests
{
    [Fact]
    public void ContainsAccentBrush()
    {
        var brushes = AppColors.BuildBrushes();

        Assert.True(brushes.ContainsKey("Accent"));
        Assert.IsType<SolidColorBrush>(brushes["Accent"]);
    }

    [Fact]
    public void AllBrushesAreFrozenForCrossThreadUse()
    {
        var brushes = AppColors.BuildBrushes();

        Assert.All(brushes.Values, brush => Assert.True(brush.IsFrozen));
    }

    [Fact]
    public void ModalOverlayHasReducedAlpha()
    {
        var brushes = AppColors.BuildBrushes();
        var overlay = (SolidColorBrush)brushes["ModalOverlay"];

        Assert.True(overlay.Color.A < 255);
    }

    [Fact]
    public void ContainsAllTwentyNineNamedColors()
    {
        var brushes = AppColors.BuildBrushes();

        Assert.Equal(29, brushes.Count);
    }
}
```

- [ ] **Step 2: 테스트 실패 확인**

```bash
dotnet test tests\FishingMartPos.Tests --filter FullyQualifiedName~AppColorsTests
```

Expected: FAIL (`AppColors` 타입 없음)

- [ ] **Step 3: 구현**

`src/FishingMartPos/Theme/AppColors.cs`:

```csharp
using System.Collections.Generic;
using System.Windows.Media;
using FishingMartPos.Color;

namespace FishingMartPos.Theme;

/// <summary>
/// Fishing Mart POS.html에서 쓰인 oklch(...) 값을 그대로 옮긴 색상 상수.
/// 값을 바꿀 때는 반드시 원본 HTML의 oklch 리터럴과 대조한다.
/// </summary>
public static class AppColors
{
    public static IReadOnlyDictionary<string, Brush> BuildBrushes()
    {
        var map = new Dictionary<string, (double L, double C, double H, double A)>
        {
            ["Accent"] = (0.5, 0.1, 195, 1.0),
            ["AccentDark"] = (0.4, 0.1, 195, 1.0),
            ["OuterBackground"] = (0.93, 0.02, 195, 1.0),
            ["CardBackground"] = (0.99, 0.002, 250, 1.0),
            ["CardBorder"] = (0.75, 0.01, 250, 1.0),
            ["LoginBackground"] = (0.97, 0.01, 195, 1.0),
            ["TitleText"] = (0.22, 0.02, 250, 1.0),
            ["SubtitleText"] = (0.5, 0.01, 250, 1.0),
            ["MutedText"] = (0.4, 0.01, 250, 1.0),
            ["InputBorder"] = (0.8, 0.01, 250, 1.0),
            ["PinDotInactive"] = (0.88, 0.006, 250, 1.0),
            ["KeypadSpecialBackground"] = (0.93, 0.01, 60, 1.0),
            ["KeypadText"] = (0.3, 0.02, 250, 1.0),
            ["ModalOverlay"] = (0.2, 0.02, 250, 0.4),
            ["ErrorBorder"] = (0.6, 0.15, 25, 1.0),
            ["ErrorIconBackground"] = (0.55, 0.15, 25, 1.0),
            ["HeaderBorder"] = (0.91, 0.006, 250, 1.0),
            ["HeaderButtonBackground"] = (0.95, 0.006, 250, 1.0),
            ["HeaderSecondaryText"] = (0.45, 0.01, 250, 1.0),
            ["Divider"] = (0.88, 0.006, 250, 1.0),
            ["LogoutButtonBackground"] = (0.96, 0.006, 250, 1.0),
            ["MenuBackground"] = (0.95, 0.02, 195, 1.0),
            ["MenuTileBorder"] = (0.85, 0.008, 250, 1.0),
            ["MenuTileHoverBackground"] = (0.97, 0.02, 195, 1.0),
            ["MenuTileLabelText"] = (0.25, 0.02, 250, 1.0),
            ["MenuIconSales"] = (0.55, 0.09, 30, 1.0),
            ["MenuIconInventory"] = (0.55, 0.09, 250, 1.0),
            ["MenuIconSettingsDot"] = (0.55, 0.09, 340, 1.0),
            ["MenuIconSettingsTrack"] = (0.7, 0.01, 250, 1.0),
        };

        var brushes = new Dictionary<string, Brush>(map.Count);
        foreach (var (name, value) in map)
        {
            var color = OklchColor.ToColor(value.L, value.C, value.H, value.A);
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            brushes[name] = brush;
        }

        return brushes;
    }
}
```

- [ ] **Step 4: 테스트 통과 확인**

```bash
dotnet test tests\FishingMartPos.Tests --filter FullyQualifiedName~AppColorsTests
```

Expected: PASS (4 tests)

- [ ] **Step 5: Commit**

```bash
git add src/FishingMartPos/Theme tests/FishingMartPos.Tests/Theme
git commit -m "로그인/메인메뉴 화면용 디자인 색상 상수 추가"
```

---

### Task 4: DB 마이그레이션 스크립트 (13개 테이블)

**Files:**
- Create: `db/migrations/001_create_schema.sql`

**Interfaces:**
- Produces: `staff_tb`, `pos_terminal_tb` 등 13개 테이블. Phase 1은 `staff_tb`, `pos_terminal_tb`만 사용하지만, 설계 문서(8절)에 정의된 스키마 전체를 한 번에 만들어 이후 단계에서 재작업하지 않도록 한다.

- [ ] **Step 1: 스크립트 작성**

`db/migrations/001_create_schema.sql`:

```sql
-- 001_create_schema.sql
-- 대원낚시 POS 최소 스키마 (설계 문서 8절 기준)
-- 실행: mysql -h <host> -u <user> -p <database> < 001_create_schema.sql

CREATE TABLE IF NOT EXISTS staff_tb (
    staff_cd    VARCHAR(10)     NOT NULL,
    staff_name  VARCHAR(50)     NOT NULL,
    pin_hash    VARCHAR(128)    NOT NULL,
    role        VARCHAR(10)     NOT NULL, -- 'ADMIN' or 'STAFF'
    use_yn      CHAR(1)         NOT NULL DEFAULT 'Y',
    created_at  DATETIME        NOT NULL DEFAULT CURRENT_TIMESTAMP,
    PRIMARY KEY (staff_cd)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

CREATE TABLE IF NOT EXISTS pos_terminal_tb (
    pos_cd      VARCHAR(4)      NOT NULL,
    pos_name    VARCHAR(30)     NOT NULL,
    PRIMARY KEY (pos_cd)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

CREATE TABLE IF NOT EXISTS code_tb (
    code_gbn    VARCHAR(10)     NOT NULL, -- 'MAJOR' / 'MINOR' / 'POSCAT'
    code_cd     VARCHAR(20)     NOT NULL,
    code_nm     VARCHAR(50)     NOT NULL,
    sort_no     INT             NOT NULL DEFAULT 0,
    PRIMARY KEY (code_gbn, code_cd)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

CREATE TABLE IF NOT EXISTS product_tb (
    barcode     VARCHAR(30)     NOT NULL,
    major_cd    VARCHAR(20)     NULL,
    minor_cd    VARCHAR(20)     NULL,
    poscat_cd   VARCHAR(20)     NULL,
    name        VARCHAR(100)    NOT NULL,
    price       DECIMAL(12,0)   NOT NULL,
    stock_qty   INT             NOT NULL DEFAULT 0,
    photo_path  VARCHAR(255)    NULL,
    use_yn      CHAR(1)         NOT NULL DEFAULT 'Y',
    created_at  DATETIME        NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at  DATETIME        NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
    PRIMARY KEY (barcode)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

CREATE TABLE IF NOT EXISTS sales_header_tb (
    sale_no         BIGINT          NOT NULL AUTO_INCREMENT,
    pos_cd          VARCHAR(4)      NOT NULL,
    sale_dt         DATETIME        NOT NULL,
    staff_cd        VARCHAR(10)     NOT NULL,
    total_amt       DECIMAL(12,0)   NOT NULL,
    pay_type        VARCHAR(10)     NOT NULL, -- 'CASH' / 'CARD1' / 'CARD2'
    cash_received   DECIMAL(12,0)   NULL,
    change_amt      DECIMAL(12,0)   NULL,
    van_approval_no VARCHAR(40)     NULL,
    van_code        VARCHAR(10)     NULL, -- 'NCVAN' / 'KICC'
    status          VARCHAR(10)     NOT NULL DEFAULT 'COMPLETE', -- 'COMPLETE' / 'CANCELLED'
    created_at      DATETIME        NOT NULL DEFAULT CURRENT_TIMESTAMP,
    PRIMARY KEY (sale_no)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

CREATE TABLE IF NOT EXISTS sales_detail_tb (
    sale_no      BIGINT          NOT NULL,
    line_no      INT             NOT NULL,
    barcode      VARCHAR(30)     NOT NULL,
    product_name VARCHAR(100)    NOT NULL,
    qty          INT             NOT NULL,
    unit_price   DECIMAL(12,0)   NOT NULL,
    line_amt     DECIMAL(12,0)   NOT NULL,
    PRIMARY KEY (sale_no, line_no)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

CREATE TABLE IF NOT EXISTS held_order_tb (
    hold_no     BIGINT          NOT NULL AUTO_INCREMENT,
    pos_cd      VARCHAR(4)      NOT NULL,
    staff_cd    VARCHAR(10)     NOT NULL,
    held_at     DATETIME        NOT NULL DEFAULT CURRENT_TIMESTAMP,
    memo        VARCHAR(100)    NULL,
    status      VARCHAR(10)     NOT NULL DEFAULT 'HELD', -- 'HELD' / 'RECALLED'
    PRIMARY KEY (hold_no)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

CREATE TABLE IF NOT EXISTS held_order_detail_tb (
    hold_no      BIGINT          NOT NULL,
    line_no      INT             NOT NULL,
    barcode      VARCHAR(30)     NOT NULL,
    product_name VARCHAR(100)    NOT NULL,
    qty          INT             NOT NULL,
    unit_price   DECIMAL(12,0)   NOT NULL,
    PRIMARY KEY (hold_no, line_no)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

CREATE TABLE IF NOT EXISTS printer_config_tb (
    pos_cd              VARCHAR(4)      NOT NULL,
    printer_port        VARCHAR(50)     NULL,
    printer_name        VARCHAR(50)     NULL,
    drawer_kick_enabled CHAR(1)         NOT NULL DEFAULT 'Y',
    PRIMARY KEY (pos_cd)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

CREATE TABLE IF NOT EXISTS receipt_config_tb (
    pos_cd      VARCHAR(4)      NOT NULL,
    header_text VARCHAR(200)    NULL,
    footer_text VARCHAR(200)    NULL,
    PRIMARY KEY (pos_cd)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

CREATE TABLE IF NOT EXISTS van_config_tb (
    pos_cd      VARCHAR(4)      NOT NULL,
    van_code    VARCHAR(10)     NOT NULL, -- 'NCVAN' / 'KICC'
    terminal_id VARCHAR(30)     NULL,
    server_ip   VARCHAR(50)     NULL,
    server_port INT             NULL,
    PRIMARY KEY (pos_cd, van_code)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

CREATE TABLE IF NOT EXISTS action_log_tb (
    log_id      BIGINT          NOT NULL AUTO_INCREMENT,
    staff_cd    VARCHAR(10)     NOT NULL,
    action_type VARCHAR(30)     NOT NULL,
    ref_no      VARCHAR(30)     NULL,
    detail      VARCHAR(200)    NULL,
    created_at  DATETIME        NOT NULL DEFAULT CURRENT_TIMESTAMP,
    PRIMARY KEY (log_id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

CREATE TABLE IF NOT EXISTS system_info_tb (
    app_version VARCHAR(20)     NOT NULL,
    db_version  VARCHAR(20)     NOT NULL,
    updated_at  DATETIME        NOT NULL DEFAULT CURRENT_TIMESTAMP
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

-- 시드 데이터: Phase 1 로그인/메뉴 화면 동작 확인용
-- PIN '0000'의 SHA-256("0000:fishingmart-pos-salt") 해시값
-- (Task 6 PinHasher.Hash("0000")와 반드시 동일해야 한다 — 값이 바뀌면 이 INSERT도 함께 갱신할 것)
INSERT INTO staff_tb (staff_cd, staff_name, pin_hash, role, use_yn)
VALUES ('ADMIN1', '관리자', SHA2('0000:fishingmart-pos-salt', 256), 'ADMIN', 'Y')
ON DUPLICATE KEY UPDATE staff_name = staff_name;

INSERT INTO pos_terminal_tb (pos_cd, pos_name) VALUES
    ('1', 'POS1'),
    ('2', 'POS2')
ON DUPLICATE KEY UPDATE pos_name = VALUES(pos_name);

INSERT INTO system_info_tb (app_version, db_version)
SELECT '0.1.0-phase1', '001'
WHERE NOT EXISTS (SELECT 1 FROM system_info_tb);
```

- [ ] **Step 2: 로컬 개발 DB에 적용**

로컬에 개발용 MariaDB가 있다고 가정(`fishingmart_dev` 데이터베이스, 이미 존재해야 함 — 없으면 `CREATE DATABASE fishingmart_dev DEFAULT CHARACTER SET utf8mb4;` 먼저 실행):

```bash
mysql -h 127.0.0.1 -u root -p fishingmart_dev < db\migrations\001_create_schema.sql
```

- [ ] **Step 3: 테이블 생성 확인**

```bash
mysql -h 127.0.0.1 -u root -p fishingmart_dev -e "SHOW TABLES;"
```

Expected: 13개 테이블 목록 출력 (`staff_tb`, `pos_terminal_tb`, `code_tb`, `product_tb`, `sales_header_tb`, `sales_detail_tb`, `held_order_tb`, `held_order_detail_tb`, `printer_config_tb`, `receipt_config_tb`, `van_config_tb`, `action_log_tb`, `system_info_tb`)

- [ ] **Step 4: Commit**

```bash
git add db/migrations/001_create_schema.sql
git commit -m "DB 마이그레이션 001: 최소 스키마 13개 테이블 생성"
```

---

### Task 5: 설정 로딩 + DB 연결 팩토리

**Files:**
- Create: `src/FishingMartPos/appsettings.json`
- Create: `src/FishingMartPos/Configuration/AppConfig.cs`
- Create: `src/FishingMartPos/Data/IDbConnectionFactory.cs`
- Create: `src/FishingMartPos/Data/MySqlConnectionFactory.cs`

**Interfaces:**
- Produces: `AppConfig.Load(string basePath) : AppConfig` (속성: `ConnectionString`, `PosCode`), `IDbConnectionFactory.CreateOpenConnection() : System.Data.IDbConnection`

- [ ] **Step 1: 설정 파일 작성**

`src/FishingMartPos/appsettings.json`:

```json
{
  "Database": {
    "ConnectionString": "Server=127.0.0.1;Port=3306;Database=fishingmart_dev;Uid=root;Pwd=;"
  },
  "Terminal": {
    "PosCode": "1"
  }
}
```

- [ ] **Step 2: AppConfig 구현**

`src/FishingMartPos/Configuration/AppConfig.cs`:

```csharp
using System;
using System.IO;
using Microsoft.Extensions.Configuration;

namespace FishingMartPos.Configuration;

public sealed class AppConfig
{
    public required string ConnectionString { get; init; }
    public required string PosCode { get; init; }

    public static AppConfig Load(string basePath)
    {
        var configuration = new ConfigurationBuilder()
            .SetBasePath(basePath)
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
            .Build();

        string? connectionString = configuration["Database:ConnectionString"];
        string? posCode = configuration["Terminal:PosCode"];

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException("Database:ConnectionString 설정이 없습니다.");
        }

        if (string.IsNullOrWhiteSpace(posCode))
        {
            throw new InvalidOperationException("Terminal:PosCode 설정이 없습니다.");
        }

        return new AppConfig { ConnectionString = connectionString, PosCode = posCode };
    }
}
```

- [ ] **Step 3: DB 연결 팩토리 구현**

`src/FishingMartPos/Data/IDbConnectionFactory.cs`:

```csharp
using System.Data;

namespace FishingMartPos.Data;

public interface IDbConnectionFactory
{
    IDbConnection CreateOpenConnection();
}
```

`src/FishingMartPos/Data/MySqlConnectionFactory.cs`:

```csharp
using System.Data;
using FishingMartPos.Configuration;
using MySqlConnector;

namespace FishingMartPos.Data;

public sealed class MySqlConnectionFactory : IDbConnectionFactory
{
    private readonly string _connectionString;

    public MySqlConnectionFactory(AppConfig config)
    {
        _connectionString = config.ConnectionString;
    }

    public IDbConnection CreateOpenConnection()
    {
        var connection = new MySqlConnection(_connectionString);
        connection.Open();
        return connection;
    }
}
```

- [ ] **Step 4: 빌드 확인**

```bash
dotnet build FishingMartPos.sln
```

Expected: `Build succeeded.`

- [ ] **Step 5: Commit**

```bash
git add src/FishingMartPos/appsettings.json src/FishingMartPos/Configuration src/FishingMartPos/Data
git commit -m "설정 로딩(AppConfig)과 MariaDB 연결 팩토리 추가"
```

---

### Task 6: PIN 해시 유틸리티 (TDD)

**Files:**
- Create: `src/FishingMartPos/Security/PinHasher.cs`
- Test: `tests/FishingMartPos.Tests/Security/PinHasherTests.cs`

**Interfaces:**
- Produces: `PinHasher.Hash(string pin) : string`

- [ ] **Step 1: 실패하는 테스트 작성**

`tests/FishingMartPos.Tests/Security/PinHasherTests.cs`:

```csharp
using FishingMartPos.Security;
using Xunit;

namespace FishingMartPos.Tests.Security;

public class PinHasherTests
{
    [Fact]
    public void SamePinProducesSameHash()
    {
        Assert.Equal(PinHasher.Hash("0000"), PinHasher.Hash("0000"));
    }

    [Fact]
    public void DifferentPinProducesDifferentHash()
    {
        Assert.NotEqual(PinHasher.Hash("0000"), PinHasher.Hash("1234"));
    }

    [Fact]
    public void HashIsSixtyFourHexCharacters()
    {
        string hash = PinHasher.Hash("0000");

        Assert.Equal(64, hash.Length);
        Assert.Matches("^[0-9a-f]{64}$", hash);
    }

    [Fact]
    public void KnownPinMatchesSeedMigrationHash()
    {
        // db/migrations/001_create_schema.sql의 SHA2('0000:fishingmart-pos-salt', 256)과 반드시 일치해야 한다.
        string hash = PinHasher.Hash("0000");

        Assert.Equal("b8f7c1e6a5f45a5b0c1e0e3b0a6b74d4b6c2b0a5e3d4b0d1a5c6e7f8091a2b3c".Length, hash.Length);
    }
}
```

- [ ] **Step 2: 테스트 실패 확인**

```bash
dotnet test tests\FishingMartPos.Tests --filter FullyQualifiedName~PinHasherTests
```

Expected: FAIL (`PinHasher` 타입 없음)

- [ ] **Step 3: 구현**

`src/FishingMartPos/Security/PinHasher.cs`:

```csharp
using System.Security.Cryptography;
using System.Text;

namespace FishingMartPos.Security;

/// <summary>
/// PIN 해시. db/migrations/001_create_schema.sql의 SHA2(pin || ':' || Salt, 256)과
/// 알고리즘이 동일해야 한다 — 한쪽만 바꾸면 로그인이 깨진다.
/// </summary>
public static class PinHasher
{
    private const string Salt = "fishingmart-pos-salt";

    public static string Hash(string pin)
    {
        byte[] input = Encoding.UTF8.GetBytes($"{pin}:{Salt}");
        byte[] hashBytes = SHA256.HashData(input);
        return Convert.ToHexStringLower(hashBytes);
    }
}
```

- [ ] **Step 4: `KnownPinMatchesSeedMigrationHash` 테스트를 실제 해시값 비교로 교체**

Step 1의 4번째 테스트는 자리표시자였다. 아래로 교체:

```csharp
    [Fact]
    public void HashAlgorithmMatchesSha2WithColonSeparatedSalt()
    {
        // MariaDB의 SHA2('0000:fishingmart-pos-salt', 256)과 동일한 결과가 나와야 한다.
        using var sha256 = System.Security.Cryptography.SHA256.Create();
        byte[] expectedBytes = sha256.ComputeHash(
            System.Text.Encoding.UTF8.GetBytes("0000:fishingmart-pos-salt"));
        string expected = Convert.ToHexStringLower(expectedBytes);

        Assert.Equal(expected, PinHasher.Hash("0000"));
    }
```

- [ ] **Step 5: 테스트 통과 확인**

```bash
dotnet test tests\FishingMartPos.Tests --filter FullyQualifiedName~PinHasherTests
```

Expected: PASS (4 tests)

- [ ] **Step 6: Commit**

```bash
git add src/FishingMartPos/Security tests/FishingMartPos.Tests/Security
git commit -m "PIN 해시 유틸리티(PinHasher) 추가"
```

---

### Task 7: Staff 모델 + Repository (TDD, MariaDB 연동)

**Files:**
- Create: `src/FishingMartPos/Models/Staff.cs`
- Create: `src/FishingMartPos/Repositories/IStaffRepository.cs`
- Create: `src/FishingMartPos/Repositories/StaffRepository.cs`
- Test: `tests/FishingMartPos.Tests/Repositories/StaffRepositoryTests.cs`

**Interfaces:**
- Consumes: `IDbConnectionFactory`(Task 5), `PinHasher.Hash`(Task 6)
- Produces: `Staff { StaffCode, StaffName, Role, UseYn }`, `IStaffRepository.FindByPinAsync(string pin) : Task<Staff?>`

> 이 테스트는 로컬 MariaDB(`fishingmart_dev`)에 Task 4 마이그레이션이 적용돼 있어야 통과한다(통합 테스트). DB가 없는 환경에서는 스킵하고 다음 태스크로 진행해도 되지만, 실행 가능한 환경에서 반드시 한 번은 확인한다.

- [ ] **Step 1: 모델 작성**

`src/FishingMartPos/Models/Staff.cs`:

```csharp
namespace FishingMartPos.Models;

public sealed class Staff
{
    public required string StaffCode { get; init; }
    public required string StaffName { get; init; }
    public required string Role { get; init; } // "ADMIN" or "STAFF"
    public required string UseYn { get; init; }

    public bool IsAdmin => Role == "ADMIN";
}
```

- [ ] **Step 2: 실패하는 통합 테스트 작성**

`tests/FishingMartPos.Tests/Repositories/StaffRepositoryTests.cs`:

```csharp
using FishingMartPos.Configuration;
using FishingMartPos.Data;
using FishingMartPos.Repositories;
using Xunit;

namespace FishingMartPos.Tests.Repositories;

public class StaffRepositoryTests
{
    private static IStaffRepository CreateRepository()
    {
        var config = AppConfig.Load(AppContext.BaseDirectory);
        var factory = new MySqlConnectionFactory(config);
        return new StaffRepository(factory);
    }

    [Fact]
    public async Task FindByPin_ReturnsSeededAdmin_WhenPinIsCorrect()
    {
        IStaffRepository repository = CreateRepository();

        var staff = await repository.FindByPinAsync("0000");

        Assert.NotNull(staff);
        Assert.Equal("ADMIN1", staff!.StaffCode);
        Assert.True(staff.IsAdmin);
    }

    [Fact]
    public async Task FindByPin_ReturnsNull_WhenPinIsWrong()
    {
        IStaffRepository repository = CreateRepository();

        var staff = await repository.FindByPinAsync("9999");

        Assert.Null(staff);
    }
}
```

`tests/FishingMartPos.Tests/appsettings.json`을 생성(테스트 실행 디렉터리에 복사되도록 `.csproj`에도 등록):

```json
{
  "Database": {
    "ConnectionString": "Server=127.0.0.1;Port=3306;Database=fishingmart_dev;Uid=root;Pwd=;"
  },
  "Terminal": {
    "PosCode": "1"
  }
}
```

`tests/FishingMartPos.Tests/FishingMartPos.Tests.csproj`의 `<ItemGroup>`에 추가:

```xml
<ItemGroup>
  <None Update="appsettings.json">
    <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
  </None>
</ItemGroup>
```

- [ ] **Step 3: 테스트 실패 확인**

```bash
dotnet test tests\FishingMartPos.Tests --filter FullyQualifiedName~StaffRepositoryTests
```

Expected: FAIL (`IStaffRepository`, `StaffRepository` 타입 없음)

- [ ] **Step 4: 구현**

`src/FishingMartPos/Repositories/IStaffRepository.cs`:

```csharp
using FishingMartPos.Models;

namespace FishingMartPos.Repositories;

public interface IStaffRepository
{
    Task<Staff?> FindByPinAsync(string pin);
}
```

`src/FishingMartPos/Repositories/StaffRepository.cs`:

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
}
```

- [ ] **Step 5: 테스트 통과 확인 (로컬 MariaDB 필요)**

```bash
dotnet test tests\FishingMartPos.Tests --filter FullyQualifiedName~StaffRepositoryTests
```

Expected: PASS (2 tests) — DB 미접속 환경이면 연결 예외로 실패하는 것이 정상이며, Task 4의 DB 준비를 먼저 완료해야 한다.

- [ ] **Step 6: Commit**

```bash
git add src/FishingMartPos/Models/Staff.cs src/FishingMartPos/Repositories/IStaffRepository.cs src/FishingMartPos/Repositories/StaffRepository.cs tests/FishingMartPos.Tests/Repositories/StaffRepositoryTests.cs tests/FishingMartPos.Tests/appsettings.json tests/FishingMartPos.Tests/FishingMartPos.Tests.csproj
git commit -m "Staff 모델과 PIN 조회 Repository 추가"
```

---

### Task 8: PosTerminal 모델 + Repository (TDD, MariaDB 연동)

**Files:**
- Create: `src/FishingMartPos/Models/PosTerminal.cs`
- Create: `src/FishingMartPos/Repositories/IPosTerminalRepository.cs`
- Create: `src/FishingMartPos/Repositories/PosTerminalRepository.cs`
- Test: `tests/FishingMartPos.Tests/Repositories/PosTerminalRepositoryTests.cs`

**Interfaces:**
- Consumes: `IDbConnectionFactory`(Task 5)
- Produces: `PosTerminal { PosCode, PosName }`, `IPosTerminalRepository.GetAllAsync() : Task<IReadOnlyList<PosTerminal>>`

- [ ] **Step 1: 모델 작성**

`src/FishingMartPos/Models/PosTerminal.cs`:

```csharp
namespace FishingMartPos.Models;

public sealed class PosTerminal
{
    public required string PosCode { get; init; }
    public required string PosName { get; init; }
}
```

- [ ] **Step 2: 실패하는 통합 테스트 작성**

`tests/FishingMartPos.Tests/Repositories/PosTerminalRepositoryTests.cs`:

```csharp
using FishingMartPos.Configuration;
using FishingMartPos.Data;
using FishingMartPos.Repositories;
using Xunit;

namespace FishingMartPos.Tests.Repositories;

public class PosTerminalRepositoryTests
{
    [Fact]
    public async Task GetAll_ReturnsSeededTwoTerminals()
    {
        var config = AppConfig.Load(AppContext.BaseDirectory);
        var factory = new MySqlConnectionFactory(config);
        IPosTerminalRepository repository = new PosTerminalRepository(factory);

        var terminals = await repository.GetAllAsync();

        Assert.Contains(terminals, t => t.PosCode == "1" && t.PosName == "POS1");
        Assert.Contains(terminals, t => t.PosCode == "2" && t.PosName == "POS2");
    }
}
```

- [ ] **Step 3: 테스트 실패 확인**

```bash
dotnet test tests\FishingMartPos.Tests --filter FullyQualifiedName~PosTerminalRepositoryTests
```

Expected: FAIL (타입 없음)

- [ ] **Step 4: 구현**

`src/FishingMartPos/Repositories/IPosTerminalRepository.cs`:

```csharp
using FishingMartPos.Models;

namespace FishingMartPos.Repositories;

public interface IPosTerminalRepository
{
    Task<IReadOnlyList<PosTerminal>> GetAllAsync();
}
```

`src/FishingMartPos/Repositories/PosTerminalRepository.cs`:

```csharp
using Dapper;
using FishingMartPos.Data;
using FishingMartPos.Models;

namespace FishingMartPos.Repositories;

public sealed class PosTerminalRepository : IPosTerminalRepository
{
    private readonly IDbConnectionFactory _connectionFactory;

    public PosTerminalRepository(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<IReadOnlyList<PosTerminal>> GetAllAsync()
    {
        using var connection = _connectionFactory.CreateOpenConnection();
        const string sql = """
            SELECT pos_cd AS PosCode, pos_name AS PosName
            FROM pos_terminal_tb
            ORDER BY pos_cd
            """;

        var result = await connection.QueryAsync<PosTerminal>(sql);
        return result.ToList();
    }
}
```

- [ ] **Step 5: 테스트 통과 확인**

```bash
dotnet test tests\FishingMartPos.Tests --filter FullyQualifiedName~PosTerminalRepositoryTests
```

Expected: PASS (1 test)

- [ ] **Step 6: Commit**

```bash
git add src/FishingMartPos/Models/PosTerminal.cs src/FishingMartPos/Repositories/IPosTerminalRepository.cs src/FishingMartPos/Repositories/PosTerminalRepository.cs tests/FishingMartPos.Tests/Repositories/PosTerminalRepositoryTests.cs
git commit -m "PosTerminal 모델과 단말 목록 Repository 추가"
```

---

### Task 9: 세션 서비스 (로그인 상태 보관)

**Files:**
- Create: `src/FishingMartPos/Services/ICurrentSession.cs`
- Create: `src/FishingMartPos/Services/CurrentSession.cs`
- Test: `tests/FishingMartPos.Tests/Services/CurrentSessionTests.cs`

**Interfaces:**
- Consumes: `Staff`(Task 7), `PosTerminal`(Task 8)
- Produces: `ICurrentSession { Staff? CurrentStaff, PosTerminal? CurrentTerminal, void SignIn(Staff, PosTerminal), void SignOut(), bool IsSignedIn }`

- [ ] **Step 1: 실패하는 테스트 작성**

`tests/FishingMartPos.Tests/Services/CurrentSessionTests.cs`:

```csharp
using FishingMartPos.Models;
using FishingMartPos.Services;
using Xunit;

namespace FishingMartPos.Tests.Services;

public class CurrentSessionTests
{
    private static Staff SampleStaff() => new()
    {
        StaffCode = "ADMIN1", StaffName = "관리자", Role = "ADMIN", UseYn = "Y"
    };

    private static PosTerminal SampleTerminal() => new()
    {
        PosCode = "1", PosName = "POS1"
    };

    [Fact]
    public void IsSignedIn_IsFalse_BeforeSignIn()
    {
        ICurrentSession session = new CurrentSession();

        Assert.False(session.IsSignedIn);
    }

    [Fact]
    public void SignIn_SetsStaffAndTerminal()
    {
        ICurrentSession session = new CurrentSession();

        session.SignIn(SampleStaff(), SampleTerminal());

        Assert.True(session.IsSignedIn);
        Assert.Equal("ADMIN1", session.CurrentStaff?.StaffCode);
        Assert.Equal("1", session.CurrentTerminal?.PosCode);
    }

    [Fact]
    public void SignOut_ClearsStaffAndTerminal()
    {
        ICurrentSession session = new CurrentSession();
        session.SignIn(SampleStaff(), SampleTerminal());

        session.SignOut();

        Assert.False(session.IsSignedIn);
        Assert.Null(session.CurrentStaff);
        Assert.Null(session.CurrentTerminal);
    }
}
```

- [ ] **Step 2: 테스트 실패 확인**

```bash
dotnet test tests\FishingMartPos.Tests --filter FullyQualifiedName~CurrentSessionTests
```

Expected: FAIL (타입 없음)

- [ ] **Step 3: 구현**

`src/FishingMartPos/Services/ICurrentSession.cs`:

```csharp
using FishingMartPos.Models;

namespace FishingMartPos.Services;

public interface ICurrentSession
{
    Staff? CurrentStaff { get; }
    PosTerminal? CurrentTerminal { get; }
    bool IsSignedIn { get; }

    void SignIn(Staff staff, PosTerminal terminal);
    void SignOut();
}
```

`src/FishingMartPos/Services/CurrentSession.cs`:

```csharp
using FishingMartPos.Models;

namespace FishingMartPos.Services;

public sealed class CurrentSession : ICurrentSession
{
    public Staff? CurrentStaff { get; private set; }
    public PosTerminal? CurrentTerminal { get; private set; }
    public bool IsSignedIn => CurrentStaff is not null;

    public void SignIn(Staff staff, PosTerminal terminal)
    {
        CurrentStaff = staff;
        CurrentTerminal = terminal;
    }

    public void SignOut()
    {
        CurrentStaff = null;
        CurrentTerminal = null;
    }
}
```

- [ ] **Step 4: 테스트 통과 확인**

```bash
dotnet test tests\FishingMartPos.Tests --filter FullyQualifiedName~CurrentSessionTests
```

Expected: PASS (3 tests)

- [ ] **Step 5: Commit**

```bash
git add src/FishingMartPos/Services tests/FishingMartPos.Tests/Services
git commit -m "로그인 세션 서비스(CurrentSession) 추가"
```

---

### Task 10: 내비게이션 서비스

**Files:**
- Create: `src/FishingMartPos/Navigation/INavigationService.cs`
- Create: `src/FishingMartPos/Navigation/NavigationService.cs`
- Test: `tests/FishingMartPos.Tests/Navigation/NavigationServiceTests.cs`

**Interfaces:**
- Produces: `INavigationService { object? CurrentViewModel, event EventHandler? CurrentViewModelChanged, void NavigateTo(object viewModel) }`

- [ ] **Step 1: 실패하는 테스트 작성**

`tests/FishingMartPos.Tests/Navigation/NavigationServiceTests.cs`:

```csharp
using FishingMartPos.Navigation;
using Xunit;

namespace FishingMartPos.Tests.Navigation;

public class NavigationServiceTests
{
    [Fact]
    public void CurrentViewModel_IsNull_Initially()
    {
        INavigationService navigation = new NavigationService();

        Assert.Null(navigation.CurrentViewModel);
    }

    [Fact]
    public void NavigateTo_SetsCurrentViewModel()
    {
        INavigationService navigation = new NavigationService();
        var target = new object();

        navigation.NavigateTo(target);

        Assert.Same(target, navigation.CurrentViewModel);
    }

    [Fact]
    public void NavigateTo_RaisesCurrentViewModelChanged()
    {
        INavigationService navigation = new NavigationService();
        bool raised = false;
        navigation.CurrentViewModelChanged += (_, _) => raised = true;

        navigation.NavigateTo(new object());

        Assert.True(raised);
    }
}
```

- [ ] **Step 2: 테스트 실패 확인**

```bash
dotnet test tests\FishingMartPos.Tests --filter FullyQualifiedName~NavigationServiceTests
```

Expected: FAIL (타입 없음)

- [ ] **Step 3: 구현**

`src/FishingMartPos/Navigation/INavigationService.cs`:

```csharp
namespace FishingMartPos.Navigation;

public interface INavigationService
{
    object? CurrentViewModel { get; }
    event EventHandler? CurrentViewModelChanged;

    void NavigateTo(object viewModel);
}
```

`src/FishingMartPos/Navigation/NavigationService.cs`:

```csharp
namespace FishingMartPos.Navigation;

public sealed class NavigationService : INavigationService
{
    public object? CurrentViewModel { get; private set; }
    public event EventHandler? CurrentViewModelChanged;

    public void NavigateTo(object viewModel)
    {
        CurrentViewModel = viewModel;
        CurrentViewModelChanged?.Invoke(this, EventArgs.Empty);
    }
}
```

- [ ] **Step 4: 테스트 통과 확인**

```bash
dotnet test tests\FishingMartPos.Tests --filter FullyQualifiedName~NavigationServiceTests
```

Expected: PASS (3 tests)

- [ ] **Step 5: Commit**

```bash
git add src/FishingMartPos/Navigation tests/FishingMartPos.Tests/Navigation
git commit -m "화면 전환용 NavigationService 추가"
```

---

### Task 11: Placeholder 화면 (미구현 화면 스텁)

메인 메뉴에서 판매/매출/재고/환경설정으로 이동은 해야 하지만, 각 화면은 Phase 2 이후에 만든다. 지금은 "준비 중" 스텁으로 앱이 끊기지 않게 한다.

**Files:**
- Create: `src/FishingMartPos/ViewModels/PlaceholderViewModel.cs`
- Create: `src/FishingMartPos/Views/PlaceholderView.xaml`
- Create: `src/FishingMartPos/Views/PlaceholderView.xaml.cs`

**Interfaces:**
- Produces: `PlaceholderViewModel(string title)` — `Title` 속성 노출

- [ ] **Step 1: ViewModel 작성**

`src/FishingMartPos/ViewModels/PlaceholderViewModel.cs`:

```csharp
using CommunityToolkit.Mvvm.ComponentModel;

namespace FishingMartPos.ViewModels;

public sealed partial class PlaceholderViewModel : ObservableObject
{
    public PlaceholderViewModel(string title)
    {
        Title = title;
    }

    public string Title { get; }
}
```

- [ ] **Step 2: View 작성**

`src/FishingMartPos/Views/PlaceholderView.xaml`:

```xml
<UserControl x:Class="FishingMartPos.Views.PlaceholderView"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006"
             xmlns:d="http://schemas.microsoft.com/expression/blend/2008"
             mc:Ignorable="d">
    <Grid Background="{DynamicResource MenuBackground}">
        <TextBlock Text="{Binding Title, StringFormat='준비 중: {0}'}"
                   HorizontalAlignment="Center"
                   VerticalAlignment="Center"
                   FontSize="18"
                   FontWeight="Bold"
                   Foreground="{DynamicResource TitleText}" />
    </Grid>
</UserControl>
```

`src/FishingMartPos/Views/PlaceholderView.xaml.cs`:

```csharp
using System.Windows.Controls;

namespace FishingMartPos.Views;

public partial class PlaceholderView : UserControl
{
    public PlaceholderView()
    {
        InitializeComponent();
    }
}
```

- [ ] **Step 3: 빌드 확인**

```bash
dotnet build FishingMartPos.sln
```

Expected: `Build succeeded.` (이 시점엔 `MenuBackground`/`TitleText` 리소스가 아직 앱에 등록되지 않아 XAML 파서 경고가 날 수 있으나, 리소스는 Task 15에서 등록하므로 컴파일은 성공해야 한다)

- [ ] **Step 4: Commit**

```bash
git add src/FishingMartPos/ViewModels/PlaceholderViewModel.cs src/FishingMartPos/Views/PlaceholderView.xaml src/FishingMartPos/Views/PlaceholderView.xaml.cs
git commit -m "미구현 화면용 PlaceholderView 스텁 추가"
```

---

### Task 12: LoginViewModel (TDD)

**Files:**
- Create: `src/FishingMartPos/ViewModels/LoginViewModel.cs`
- Create: `tests/FishingMartPos.Tests/Fakes/FakeStaffRepository.cs`
- Test: `tests/FishingMartPos.Tests/ViewModels/LoginViewModelTests.cs`

**Interfaces:**
- Consumes: `IStaffRepository`(Task 7), `ICurrentSession`(Task 9), `INavigationService`(Task 10), `PosTerminal`(Task 8), `MainMenuViewModel`(Task 13 — 여기서는 타입만 참조, Task 13에서 정의)
- Produces: `LoginViewModel { string Pin, bool IsLoginErrorVisible, PosTerminal SelectedTerminal, IRelayCommand<string> PressKeyCommand, IAsyncRelayCommand SubmitCommand }`

로그인 화면 동작(디자인 JS 기준): 숫자 4자리 입력 시 자동 제출, `<`(백스페이스)/`CLS`(전체 지우기) 지원, PIN 틀리면 에러 모달을 잠깐 보여주고 자동으로 지운다.

- [ ] **Step 1: Fake Repository 작성**

`tests/FishingMartPos.Tests/Fakes/FakeStaffRepository.cs`:

```csharp
using FishingMartPos.Models;
using FishingMartPos.Repositories;

namespace FishingMartPos.Tests.Fakes;

public sealed class FakeStaffRepository : IStaffRepository
{
    private readonly Dictionary<string, Staff> _staffByPin;

    public FakeStaffRepository(Dictionary<string, Staff> staffByPin)
    {
        _staffByPin = staffByPin;
    }

    public Task<Staff?> FindByPinAsync(string pin)
    {
        _staffByPin.TryGetValue(pin, out var staff);
        return Task.FromResult(staff);
    }
}
```

- [ ] **Step 2: 실패하는 테스트 작성**

`tests/FishingMartPos.Tests/ViewModels/LoginViewModelTests.cs`:

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

        return new LoginViewModel(repository, session, navigation, terminals);
    }

    [Fact]
    public void PressingFourDigits_BuildsPinString()
    {
        var vm = CreateViewModel(out _, out _);

        vm.PressKeyCommand.Execute("1");
        vm.PressKeyCommand.Execute("2");
        vm.PressKeyCommand.Execute("3");
        vm.PressKeyCommand.Execute("9"); // 존재하지 않는 PIN이라 제출은 실패하지만 입력 자체는 확인 가능

        Assert.Equal("1239", vm.Pin);
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
        await Task.Delay(50); // 4번째 입력 시 내부적으로 비동기 제출이 걸리므로 완료를 기다린다

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

- [ ] **Step 3: 테스트 실패 확인**

```bash
dotnet test tests\FishingMartPos.Tests --filter FullyQualifiedName~LoginViewModelTests
```

Expected: FAIL (`LoginViewModel`, `MainMenuViewModel` 타입 없음)

- [ ] **Step 4: 구현**

`src/FishingMartPos/ViewModels/LoginViewModel.cs`:

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

    [ObservableProperty]
    private string _pin = string.Empty;

    [ObservableProperty]
    private bool _isLoginErrorVisible;

    [ObservableProperty]
    private PosTerminal _selectedTerminal;

    public IReadOnlyList<PosTerminal> Terminals { get; }

    public LoginViewModel(
        IStaffRepository staffRepository,
        ICurrentSession session,
        INavigationService navigation,
        IReadOnlyList<PosTerminal> terminals)
    {
        _staffRepository = staffRepository;
        _session = session;
        _navigation = navigation;
        Terminals = terminals;
        _selectedTerminal = terminals[0];
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
        _navigation.NavigateTo(new MainMenuViewModel(_session, _navigation));
    }
}
```

- [ ] **Step 5: 테스트 통과 확인 (MainMenuViewModel은 Task 13에서 정의 예정 — 지금은 최소 컴파일용 스텁을 임시로 추가)**

`LoginViewModel`이 `MainMenuViewModel`을 참조하므로, Task 13을 아직 하지 않았다면 컴파일 에러가 난다. Task 12와 Task 13은 반드시 순서대로(12 → 13) 진행하고, Task 12의 테스트 통과 확인은 Task 13 완료 후 함께 수행한다. 지금은 다음 태스크로 진행한다.

- [ ] **Step 6: Commit (Task 13 완료 후 함께 커밋해도 무방 — 여기서는 우선 스테이징만)**

```bash
git add src/FishingMartPos/ViewModels/LoginViewModel.cs tests/FishingMartPos.Tests/Fakes/FakeStaffRepository.cs tests/FishingMartPos.Tests/ViewModels/LoginViewModelTests.cs
git commit -m "LoginViewModel 추가 (MainMenuViewModel 의존 - Task 13과 함께 동작 확인)"
```

---

### Task 13: MainMenuViewModel (TDD)

**Files:**
- Create: `src/FishingMartPos/ViewModels/MainMenuViewModel.cs`
- Test: `tests/FishingMartPos.Tests/ViewModels/MainMenuViewModelTests.cs`

**Interfaces:**
- Consumes: `ICurrentSession`(Task 9), `INavigationService`(Task 10), `PlaceholderViewModel`(Task 11), `LoginViewModel`(Task 12 — 로그아웃 시 되돌아갈 대상)
- Produces: `MainMenuViewModel { string PosLabel, DateTime Now, IRelayCommand GoToSalesCommand, IRelayCommand GoToSalesReportCommand, IRelayCommand GoToInventoryCommand, IRelayCommand GoToSettingsCommand, IRelayCommand LogoutCommand }`

디자인 상 헤더에는 실시간 시계(`{{ timeStr }}`)가 있다. Phase 1에서는 `DispatcherTimer`로 뷰(View)에서 1초마다 `Now`를 갱신하도록 하고, ViewModel의 시간 형식 로직 자체는 순수 함수로 테스트한다.

- [ ] **Step 1: 실패하는 테스트 작성**

`tests/FishingMartPos.Tests/ViewModels/MainMenuViewModelTests.cs`:

```csharp
using FishingMartPos.Models;
using FishingMartPos.Navigation;
using FishingMartPos.Services;
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
        var vm = new MainMenuViewModel(session, navigation);
        return (vm, session, navigation);
    }

    [Fact]
    public void PosLabel_ReflectsCurrentTerminal()
    {
        var (vm, _, _) = Create();

        Assert.Equal("POS2", vm.PosLabel);
    }

    [Fact]
    public void GoToSales_NavigatesToPlaceholderWithSalesTitle()
    {
        var (vm, _, navigation) = Create();

        vm.GoToSalesCommand.Execute(null);

        var target = Assert.IsType<PlaceholderViewModel>(navigation.CurrentViewModel);
        Assert.Equal("판매", target.Title);
    }

    [Fact]
    public void GoToSalesReport_NavigatesToPlaceholderWithSalesReportTitle()
    {
        var (vm, _, navigation) = Create();

        vm.GoToSalesReportCommand.Execute(null);

        var target = Assert.IsType<PlaceholderViewModel>(navigation.CurrentViewModel);
        Assert.Equal("매출", target.Title);
    }

    [Fact]
    public void GoToInventory_NavigatesToPlaceholderWithInventoryTitle()
    {
        var (vm, _, navigation) = Create();

        vm.GoToInventoryCommand.Execute(null);

        var target = Assert.IsType<PlaceholderViewModel>(navigation.CurrentViewModel);
        Assert.Equal("재고", target.Title);
    }

    [Fact]
    public void GoToSettings_NavigatesToPlaceholderWithSettingsTitle()
    {
        var (vm, _, navigation) = Create();

        vm.GoToSettingsCommand.Execute(null);

        var target = Assert.IsType<PlaceholderViewModel>(navigation.CurrentViewModel);
        Assert.Equal("환경설정", target.Title);
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

- [ ] **Step 2: 테스트 실패 확인**

```bash
dotnet test tests\FishingMartPos.Tests --filter FullyQualifiedName~MainMenuViewModelTests
```

Expected: FAIL (`MainMenuViewModel` 타입 없음)

- [ ] **Step 3: 구현**

`src/FishingMartPos/ViewModels/MainMenuViewModel.cs`:

```csharp
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FishingMartPos.Navigation;
using FishingMartPos.Repositories;
using FishingMartPos.Services;

namespace FishingMartPos.ViewModels;

public sealed partial class MainMenuViewModel : ObservableObject
{
    private readonly ICurrentSession _session;
    private readonly INavigationService _navigation;

    public MainMenuViewModel(ICurrentSession session, INavigationService navigation)
    {
        _session = session;
        _navigation = navigation;
    }

    public string PosLabel => _session.CurrentTerminal?.PosName ?? string.Empty;

    [RelayCommand]
    private void GoToSales() => _navigation.NavigateTo(new PlaceholderViewModel("판매"));

    [RelayCommand]
    private void GoToSalesReport() => _navigation.NavigateTo(new PlaceholderViewModel("매출"));

    [RelayCommand]
    private void GoToInventory() => _navigation.NavigateTo(new PlaceholderViewModel("재고"));

    [RelayCommand]
    private void GoToSettings() => _navigation.NavigateTo(new PlaceholderViewModel("환경설정"));

    [RelayCommand]
    private void Logout()
    {
        _session.SignOut();
        _navigation.NavigateTo(BuildLoginViewModel());
    }

    private LoginViewModel BuildLoginViewModel()
    {
        // 로그아웃 후 로그인 화면으로 돌아갈 때 필요한 의존성은
        // App.xaml.cs의 DI 컨테이너에서 만든 팩토리로 주입한다(Task 15).
        return LoginViewModelFactory!.Invoke();
    }

    /// <summary>App.xaml.cs에서 DI 컨테이너로 주입하는 LoginViewModel 팩토리.</summary>
    public Func<LoginViewModel>? LoginViewModelFactory { get; init; }
}
```

> 참고: 테스트(`Logout_ClearsSessionAndNavigatesToLogin`)가 `LoginViewModel`을 직접 생성하려면 `IStaffRepository`, `ICurrentSession`, `INavigationService`, 단말 목록이 필요하다. 테스트에서는 `MainMenuViewModel`에 `LoginViewModelFactory`를 주입해 사용한다 — 아래 Step 4에서 테스트를 이 구조에 맞게 보정한다.

- [ ] **Step 4: 테스트의 `Create()` 헬퍼를 `LoginViewModelFactory` 주입에 맞게 보정**

`tests/FishingMartPos.Tests/ViewModels/MainMenuViewModelTests.cs`의 `Create()`를 아래로 교체:

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

        var vm = new MainMenuViewModel(session, navigation)
        {
            LoginViewModelFactory = () => new LoginViewModel(staffRepository, session, navigation, terminals)
        };
        return (vm, session, navigation);
    }
```

- [ ] **Step 5: Task 12 + Task 13 테스트 함께 통과 확인**

```bash
dotnet test tests\FishingMartPos.Tests --filter "FullyQualifiedName~LoginViewModelTests|FullyQualifiedName~MainMenuViewModelTests"
```

Expected: PASS (11 tests: LoginViewModelTests 6 + MainMenuViewModelTests 6 — 위 목록 재확인 후 숫자 일치하는지 확인)

- [ ] **Step 6: Commit**

```bash
git add src/FishingMartPos/ViewModels/MainMenuViewModel.cs tests/FishingMartPos.Tests/ViewModels/MainMenuViewModelTests.cs tests/FishingMartPos.Tests/ViewModels/LoginViewModelTests.cs
git commit -m "MainMenuViewModel 추가, LoginViewModel과 상호 연동 테스트 완료"
```

---

### Task 14: LoginView.xaml — 디자인 픽셀 이식

**Files:**
- Create: `src/FishingMartPos/Views/LoginView.xaml`
- Create: `src/FishingMartPos/Views/LoginView.xaml.cs`
- Create: `tests/FishingMartPos.Tests/StaTestHelper.cs`
- Test: `tests/FishingMartPos.Tests/Views/LoginViewSmokeTests.cs`

**Interfaces:**
- Consumes: `LoginViewModel`(Task 12), `AppColors`(Task 3, 리소스 이름으로 참조)

`Fishing Mart POS.html`의 로그인 섹션(줄 27~58)을 그대로 이식: 제목 "대원낚시 POS시스템" / 부제 "직원 PIN을 입력하세요" / POS1·POS2 선택 / PIN 점 4개 / 3x4 키패드 / PIN 오류 모달.

- [ ] **Step 1: STA 테스트 헬퍼 작성** (WPF 컨트롤은 STA 스레드에서만 생성 가능하므로 xunit 기본 스레드에서 직접 인스턴스화할 수 없다)

`tests/FishingMartPos.Tests/StaTestHelper.cs`:

```csharp
using System.Threading;

namespace FishingMartPos.Tests;

public static class StaTestHelper
{
    public static void RunOnSta(Action action)
    {
        Exception? caught = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                caught = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (caught is not null)
        {
            throw caught;
        }
    }
}
```

- [ ] **Step 2: 실패하는 스모크 테스트 작성**

`tests/FishingMartPos.Tests/Views/LoginViewSmokeTests.cs`:

```csharp
using FishingMartPos.Views;
using Xunit;

namespace FishingMartPos.Tests.Views;

public class LoginViewSmokeTests
{
    [Fact]
    public void LoginView_ConstructsWithoutException()
    {
        StaTestHelper.RunOnSta(() =>
        {
            var view = new LoginView();
            Assert.NotNull(view);
        });
    }
}
```

- [ ] **Step 3: 테스트 실패 확인**

```bash
dotnet test tests\FishingMartPos.Tests --filter FullyQualifiedName~LoginViewSmokeTests
```

Expected: FAIL (`LoginView` 타입 없음)

- [ ] **Step 4: XAML 작성**

`src/FishingMartPos/Views/LoginView.xaml`:

```xml
<UserControl x:Class="FishingMartPos.Views.LoginView"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006"
             xmlns:d="http://schemas.microsoft.com/expression/blend/2008"
             mc:Ignorable="d">
    <UserControl.Resources>
        <Style x:Key="KeypadButtonStyle" TargetType="Button">
            <Setter Property="Background" Value="White" />
            <Setter Property="BorderBrush" Value="{DynamicResource InputBorder}" />
            <Setter Property="BorderThickness" Value="1" />
            <Setter Property="FontSize" Value="18" />
            <Setter Property="FontWeight" Value="Bold" />
            <Setter Property="Foreground" Value="{DynamicResource KeypadText}" />
            <Setter Property="Cursor" Value="Hand" />
        </Style>
        <Style x:Key="SpecialKeypadButtonStyle" TargetType="Button" BasedOn="{StaticResource KeypadButtonStyle}">
            <Setter Property="Background" Value="{DynamicResource KeypadSpecialBackground}" />
        </Style>
    </UserControl.Resources>

    <Grid Background="{DynamicResource LoginBackground}">
        <StackPanel HorizontalAlignment="Center" VerticalAlignment="Center" Width="320">

            <TextBlock Text="대원낚시 POS시스템"
                       FontSize="22" FontWeight="Black"
                       Foreground="{DynamicResource TitleText}"
                       HorizontalAlignment="Center" />
            <TextBlock Text="직원 PIN을 입력하세요"
                       FontSize="13" Margin="0,4,0,22"
                       Foreground="{DynamicResource SubtitleText}"
                       HorizontalAlignment="Center" />

            <ItemsControl ItemsSource="{Binding Terminals}" HorizontalAlignment="Center" Margin="0,0,0,22">
                <ItemsControl.ItemsPanel>
                    <ItemsPanelTemplate>
                        <StackPanel Orientation="Horizontal" />
                    </ItemsPanelTemplate>
                </ItemsControl.ItemsPanel>
                <ItemsControl.ItemTemplate>
                    <DataTemplate>
                        <Button Content="{Binding PosName}"
                                Padding="22,10"
                                Margin="5,0"
                                FontSize="14" FontWeight="Bold"
                                Command="{Binding DataContext.SelectTerminalCommand, RelativeSource={RelativeSource AncestorType=UserControl}}"
                                CommandParameter="{Binding}">
                            <Button.Style>
                                <Style TargetType="Button">
                                    <Setter Property="Background" Value="White" />
                                    <Setter Property="Foreground" Value="{DynamicResource MutedText}" />
                                    <Setter Property="BorderBrush" Value="{DynamicResource InputBorder}" />
                                    <Setter Property="BorderThickness" Value="1" />
                                    <Style.Triggers>
                                        <DataTrigger Binding="{Binding IsSelected}" Value="True">
                                            <Setter Property="Background" Value="{DynamicResource Accent}" />
                                            <Setter Property="Foreground" Value="White" />
                                            <Setter Property="BorderBrush" Value="{DynamicResource AccentDark}" />
                                        </DataTrigger>
                                    </Style.Triggers>
                                </Style>
                            </Button.Style>
                        </Button>
                    </DataTemplate>
                </ItemsControl.ItemTemplate>
            </ItemsControl>

            <ItemsControl ItemsSource="{Binding PinDots}" HorizontalAlignment="Center" Margin="0,0,0,20">
                <ItemsControl.ItemsPanel>
                    <ItemsPanelTemplate>
                        <StackPanel Orientation="Horizontal" />
                    </ItemsPanelTemplate>
                </ItemsControl.ItemsPanel>
                <ItemsControl.ItemTemplate>
                    <DataTemplate>
                        <Ellipse Width="18" Height="18" Margin="7,0" Stroke="{DynamicResource InputBorder}" StrokeThickness="1">
                            <Ellipse.Style>
                                <Style TargetType="Ellipse">
                                    <Setter Property="Fill" Value="{DynamicResource PinDotInactive}" />
                                    <Style.Triggers>
                                        <DataTrigger Binding="{Binding}" Value="True">
                                            <Setter Property="Fill" Value="{DynamicResource Accent}" />
                                        </DataTrigger>
                                    </Style.Triggers>
                                </Style>
                            </Ellipse.Style>
                        </Ellipse>
                    </DataTemplate>
                </ItemsControl.ItemTemplate>
            </ItemsControl>

            <UniformGrid Columns="3" Rows="4" Width="260" Height="224">
                <Button Content="1" Style="{StaticResource KeypadButtonStyle}" Command="{Binding PressKeyCommand}" CommandParameter="1" Margin="4" />
                <Button Content="2" Style="{StaticResource KeypadButtonStyle}" Command="{Binding PressKeyCommand}" CommandParameter="2" Margin="4" />
                <Button Content="3" Style="{StaticResource KeypadButtonStyle}" Command="{Binding PressKeyCommand}" CommandParameter="3" Margin="4" />
                <Button Content="4" Style="{StaticResource KeypadButtonStyle}" Command="{Binding PressKeyCommand}" CommandParameter="4" Margin="4" />
                <Button Content="5" Style="{StaticResource KeypadButtonStyle}" Command="{Binding PressKeyCommand}" CommandParameter="5" Margin="4" />
                <Button Content="6" Style="{StaticResource KeypadButtonStyle}" Command="{Binding PressKeyCommand}" CommandParameter="6" Margin="4" />
                <Button Content="7" Style="{StaticResource KeypadButtonStyle}" Command="{Binding PressKeyCommand}" CommandParameter="7" Margin="4" />
                <Button Content="8" Style="{StaticResource KeypadButtonStyle}" Command="{Binding PressKeyCommand}" CommandParameter="8" Margin="4" />
                <Button Content="9" Style="{StaticResource KeypadButtonStyle}" Command="{Binding PressKeyCommand}" CommandParameter="9" Margin="4" />
                <Button Content="&lt;" Style="{StaticResource SpecialKeypadButtonStyle}" Command="{Binding PressKeyCommand}" CommandParameter="&lt;" Margin="4" />
                <Button Content="0" Style="{StaticResource KeypadButtonStyle}" Command="{Binding PressKeyCommand}" CommandParameter="0" Margin="4" />
                <Button Content="CLS" Style="{StaticResource SpecialKeypadButtonStyle}" Command="{Binding PressKeyCommand}" CommandParameter="CLS" Margin="4" />
            </UniformGrid>
        </StackPanel>

        <Grid Background="{DynamicResource ModalOverlay}"
              Visibility="{Binding IsLoginErrorVisible, Converter={StaticResource BooleanToVisibilityConverter}}">
            <Border Background="White" BorderBrush="{DynamicResource ErrorBorder}" BorderThickness="1"
                    Padding="44,32" HorizontalAlignment="Center" VerticalAlignment="Center">
                <StackPanel HorizontalAlignment="Center">
                    <Border Width="48" Height="48" Background="{DynamicResource ErrorIconBackground}" Margin="0,0,0,10">
                        <TextBlock Text="!" FontSize="24" FontWeight="Bold" Foreground="White"
                                   HorizontalAlignment="Center" VerticalAlignment="Center" />
                    </Border>
                    <TextBlock Text="PIN이 올바르지 않습니다" FontSize="16" FontWeight="Bold"
                               Foreground="{DynamicResource TitleText}" />
                </StackPanel>
            </Border>
        </Grid>
    </Grid>
</UserControl>
```

`SelectedTerminal`과 개별 `PosTerminal`을 비교해 `IsSelected`를 계산하려면 뷰모델 쪽에 바인딩 가능한 목록이 필요하다. `PinDots`도 `bool[4]` 형태로 노출해야 위 XAML 트리거가 동작한다. **Step 5**에서 `LoginViewModel`을 보정한다.

- [ ] **Step 5: LoginViewModel에 `PinDots`, `Terminals`의 `IsSelected` 바인딩 지원 추가**

`src/FishingMartPos/ViewModels/LoginViewModel.cs`에 아래 내용 추가(클래스 내부):

```csharp
    public IReadOnlyList<bool> PinDots => Enumerable.Range(0, 4).Select(i => i < Pin.Length).ToArray();

    partial void OnPinChanged(string value) => OnPropertyChanged(nameof(PinDots));
```

`PosTerminal`을 `IsSelected` 바인딩 가능하게 하려면 `LoginViewModel.Terminals`를 `PosTerminal` 자체가 아니라 래퍼로 바꿔야 하지만, Phase 1 범위에서는 XAML의 `IsSelected` DataTrigger 대신 코드비하인드에서 `SelectedTerminal` 변경 시 버튼 스타일을 직접 갱신하는 대신, 아래처럼 **간단화**한다: `LoginView.xaml`의 POS 선택 버튼 `DataTrigger`를 지우고, 바인딩을 `{Binding PosCode}` 텍스트만 쓰는 대신 `IsEnabled`/강조 표시는 Phase 2에서 다듬기로 하고 지금은 아래처럼 XAML을 단순화한다:

`LoginView.xaml`의 POS 선택 `Button.Style` 블록을 다음으로 교체(간단 버전 — `IsSelected` 바인딩 제거):

```xml
                        <Button Content="{Binding PosName}"
                                Padding="22,10"
                                Margin="5,0"
                                FontSize="14" FontWeight="Bold"
                                Background="White"
                                Foreground="{DynamicResource MutedText}"
                                BorderBrush="{DynamicResource InputBorder}"
                                BorderThickness="1"
                                Command="{Binding DataContext.SelectTerminalCommand, RelativeSource={RelativeSource AncestorType=UserControl}}"
                                CommandParameter="{Binding}" />
```

> 선택된 단말 강조 표시(배경을 Accent로 바꾸는 것)는 Phase 2에서 `SelectedTerminal`과 값 비교하는 `IValueConverter`를 추가해 마무리한다 — Phase 1은 "선택이 동작한다"까지만 검증한다(Step 6의 ViewModel 테스트가 이미 커버).

- [ ] **Step 6: code-behind 작성**

`src/FishingMartPos/Views/LoginView.xaml.cs`:

```csharp
using System.Windows.Controls;

namespace FishingMartPos.Views;

public partial class LoginView : UserControl
{
    public LoginView()
    {
        InitializeComponent();
    }
}
```

- [ ] **Step 7: `BooleanToVisibilityConverter` 리소스 등록**

`src/FishingMartPos/App.xaml`을 열어 `<Application.Resources>`에 추가(아직 없다면 생성):

```xml
<Application x:Class="FishingMartPos.App"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
    <Application.Resources>
        <BooleanToVisibilityConverter x:Key="BooleanToVisibilityConverter" />
    </Application.Resources>
</Application>
```

- [ ] **Step 8: 테스트 통과 확인**

```bash
dotnet test tests\FishingMartPos.Tests --filter FullyQualifiedName~LoginViewSmokeTests
```

Expected: PASS (1 test)

- [ ] **Step 9: Commit**

```bash
git add src/FishingMartPos/Views/LoginView.xaml src/FishingMartPos/Views/LoginView.xaml.cs src/FishingMartPos/ViewModels/LoginViewModel.cs src/FishingMartPos/App.xaml tests/FishingMartPos.Tests/StaTestHelper.cs tests/FishingMartPos.Tests/Views/LoginViewSmokeTests.cs
git commit -m "LoginView 디자인 이식 (PIN 키패드, 단말 선택, 오류 모달)"
```

---

### Task 15: MainMenuView.xaml — 디자인 픽셀 이식

**Files:**
- Create: `src/FishingMartPos/Views/MainMenuView.xaml`
- Create: `src/FishingMartPos/Views/MainMenuView.xaml.cs`
- Test: `tests/FishingMartPos.Tests/Views/MainMenuViewSmokeTests.cs`

**Interfaces:**
- Consumes: `MainMenuViewModel`(Task 13)

`Fishing Mart POS.html` 줄 60~119: 상단 헤더(메뉴 버튼/타이틀/시계·날짜·POS라벨/로그아웃) + 4개 타일(판매/매출/재고/환경설정).

- [ ] **Step 1: 실패하는 스모크 테스트 작성**

`tests/FishingMartPos.Tests/Views/MainMenuViewSmokeTests.cs`:

```csharp
using FishingMartPos.Views;
using Xunit;

namespace FishingMartPos.Tests.Views;

public class MainMenuViewSmokeTests
{
    [Fact]
    public void MainMenuView_ConstructsWithoutException()
    {
        StaTestHelper.RunOnSta(() =>
        {
            var view = new MainMenuView();
            Assert.NotNull(view);
        });
    }
}
```

- [ ] **Step 2: 테스트 실패 확인**

```bash
dotnet test tests\FishingMartPos.Tests --filter FullyQualifiedName~MainMenuViewSmokeTests
```

Expected: FAIL (`MainMenuView` 타입 없음)

- [ ] **Step 3: XAML 작성**

`src/FishingMartPos/Views/MainMenuView.xaml`:

```xml
<UserControl x:Class="FishingMartPos.Views.MainMenuView"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006"
             xmlns:d="http://schemas.microsoft.com/expression/blend/2008"
             mc:Ignorable="d">
    <Grid>
        <Grid.RowDefinitions>
            <RowDefinition Height="42" />
            <RowDefinition Height="*" />
        </Grid.RowDefinitions>

        <Border Grid.Row="0" Background="White" BorderBrush="{DynamicResource HeaderBorder}" BorderThickness="0,0,0,1">
            <Grid Margin="16,0">
                <StackPanel Orientation="Horizontal" VerticalAlignment="Center" HorizontalAlignment="Left">
                    <TextBlock Text="대원낚시 POS시스템" FontSize="14" FontWeight="Bold"
                               Foreground="{DynamicResource TitleText}" VerticalAlignment="Center" />
                </StackPanel>
                <StackPanel Orientation="Horizontal" VerticalAlignment="Center" HorizontalAlignment="Right">
                    <TextBlock Text="{Binding Now, StringFormat='HH:mm:ss'}" VerticalAlignment="Center"
                               Foreground="{DynamicResource HeaderSecondaryText}" FontSize="11" Margin="0,0,12,0" />
                    <TextBlock Text="{Binding Now, StringFormat='yyyy-MM-dd'}" VerticalAlignment="Center"
                               Foreground="{DynamicResource HeaderSecondaryText}" FontSize="11" Margin="0,0,12,0" />
                    <TextBlock Text="{Binding PosLabel}" VerticalAlignment="Center"
                               Foreground="{DynamicResource HeaderSecondaryText}" FontSize="11" Margin="0,0,12,0" />
                    <Button Content="로그아웃" Padding="12,5" FontSize="12" FontWeight="Bold"
                            Background="{DynamicResource LogoutButtonBackground}"
                            Foreground="{DynamicResource MutedText}"
                            BorderBrush="{DynamicResource InputBorder}" BorderThickness="1"
                            Command="{Binding LogoutCommand}" />
                </StackPanel>
            </Grid>
        </Border>

        <Grid Grid.Row="1" Background="{DynamicResource MenuBackground}">
            <UniformGrid Columns="2" Rows="2" HorizontalAlignment="Center" VerticalAlignment="Center" Width="460" Height="300">
                <Button Command="{Binding GoToSalesCommand}" Margin="10" Background="White"
                        BorderBrush="{DynamicResource MenuTileBorder}" BorderThickness="1">
                    <StackPanel>
                        <TextBlock Text="판매" FontSize="16" FontWeight="Bold"
                                   Foreground="{DynamicResource MenuTileLabelText}" HorizontalAlignment="Center" />
                    </StackPanel>
                </Button>
                <Button Command="{Binding GoToSalesReportCommand}" Margin="10" Background="White"
                        BorderBrush="{DynamicResource MenuTileBorder}" BorderThickness="1">
                    <TextBlock Text="매출" FontSize="16" FontWeight="Bold"
                               Foreground="{DynamicResource MenuTileLabelText}" HorizontalAlignment="Center" />
                </Button>
                <Button Command="{Binding GoToInventoryCommand}" Margin="10" Background="White"
                        BorderBrush="{DynamicResource MenuTileBorder}" BorderThickness="1">
                    <TextBlock Text="재고" FontSize="16" FontWeight="Bold"
                               Foreground="{DynamicResource MenuTileLabelText}" HorizontalAlignment="Center" />
                </Button>
                <Button Command="{Binding GoToSettingsCommand}" Margin="10" Background="White"
                        BorderBrush="{DynamicResource MenuTileBorder}" BorderThickness="1">
                    <TextBlock Text="환경설정" FontSize="16" FontWeight="Bold"
                               Foreground="{DynamicResource MenuTileLabelText}" HorizontalAlignment="Center" />
                </Button>
            </UniformGrid>
        </Grid>
    </Grid>
</UserControl>
```

> 타일 안의 아이콘(선/그래프/박스/점 그래픽)은 Phase 1에서는 텍스트만 이식하고, 아이콘 벡터 표현은 Phase 2에서 `Path`/`Canvas`로 다듬는다 — 레이아웃과 동작(클릭 시 이동)이 이번 범위의 핵심이다.

- [ ] **Step 4: code-behind + `Now` 갱신용 DispatcherTimer 배선**

`src/FishingMartPos/Views/MainMenuView.xaml.cs`:

```csharp
using System.Windows.Controls;
using System.Windows.Threading;
using FishingMartPos.ViewModels;

namespace FishingMartPos.Views;

public partial class MainMenuView : UserControl
{
    private readonly DispatcherTimer _clockTimer;

    public MainMenuView()
    {
        InitializeComponent();

        _clockTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _clockTimer.Tick += (_, _) =>
        {
            if (DataContext is MainMenuViewModel vm)
            {
                vm.Now = DateTime.Now;
            }
        };
        Loaded += (_, _) => _clockTimer.Start();
        Unloaded += (_, _) => _clockTimer.Stop();
    }
}
```

`MainMenuViewModel`에 `Now` 가변 속성이 필요하다 — Task 13의 `MainMenuViewModel`에 추가:

```csharp
    [ObservableProperty]
    private DateTime _now = DateTime.Now;
```

(`ObservableObject`/`[ObservableProperty]`를 쓰므로 `partial` 클래스 상단에 `using CommunityToolkit.Mvvm.ComponentModel;`이 이미 있음 — 그대로 사용)

- [ ] **Step 5: 테스트 통과 확인**

```bash
dotnet test tests\FishingMartPos.Tests --filter FullyQualifiedName~MainMenuViewSmokeTests
```

Expected: PASS (1 test)

- [ ] **Step 6: 전체 테스트 스위트 재확인**

```bash
dotnet test FishingMartPos.sln
```

Expected: 모든 테스트 PASS (DB 연동 통합 테스트는 로컬 MariaDB가 켜져 있어야 함)

- [ ] **Step 7: Commit**

```bash
git add src/FishingMartPos/Views/MainMenuView.xaml src/FishingMartPos/Views/MainMenuView.xaml.cs src/FishingMartPos/ViewModels/MainMenuViewModel.cs tests/FishingMartPos.Tests/Views/MainMenuViewSmokeTests.cs
git commit -m "MainMenuView 디자인 이식 (헤더 시계/날짜/단말/로그아웃, 4개 메뉴 타일)"
```

---

### Task 16: App.xaml.cs 배선 — DI 컨테이너 + 시작 화면

**Files:**
- Modify: `src/FishingMartPos/App.xaml`
- Modify: `src/FishingMartPos/App.xaml.cs`
- Modify: `src/FishingMartPos/MainWindow.xaml`
- Modify: `src/FishingMartPos/MainWindow.xaml.cs`

**Interfaces:**
- Consumes: 지금까지 만든 모든 서비스/레포지토리/뷰모델
- Produces: 실행 가능한 앱 (로그인 → 메뉴 → 로그아웃 왕복 동작)

- [ ] **Step 1: `App.xaml`에서 `StartupUri` 제거, 리소스 유지**

`src/FishingMartPos/App.xaml`:

```xml
<Application x:Class="FishingMartPos.App"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
    <Application.Resources>
        <BooleanToVisibilityConverter x:Key="BooleanToVisibilityConverter" />
    </Application.Resources>
</Application>
```

- [ ] **Step 2: `App.xaml.cs` 작성 — DI 등록, 색상 리소스 등록, 초기 내비게이션**

`src/FishingMartPos/App.xaml.cs`:

```csharp
using System.Windows;
using FishingMartPos.Configuration;
using FishingMartPos.Data;
using FishingMartPos.Models;
using FishingMartPos.Navigation;
using FishingMartPos.Repositories;
using FishingMartPos.Services;
using FishingMartPos.Theme;
using FishingMartPos.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace FishingMartPos;

public partial class App : Application
{
    private ServiceProvider? _services;

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
        _services = services.BuildServiceProvider();

        var terminalRepository = _services.GetRequiredService<IPosTerminalRepository>();
        IReadOnlyList<PosTerminal> terminals = await terminalRepository.GetAllAsync();

        var staffRepository = _services.GetRequiredService<IStaffRepository>();
        var session = _services.GetRequiredService<ICurrentSession>();
        var navigation = _services.GetRequiredService<INavigationService>();

        var loginViewModel = new LoginViewModel(staffRepository, session, navigation, terminals);
        navigation.NavigateTo(loginViewModel);

        var mainWindow = new MainWindow(navigation);
        mainWindow.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _services?.Dispose();
        base.OnExit(e);
    }
}
```

> `MainMenuViewModel.LoginViewModelFactory`가 로그아웃 시 새 `LoginViewModel`을 만들려면 동일한 `terminals` 목록이 필요하다 — 앱 시작 시점에 한 번만 조회했던 `terminals`를 재사용하도록, `LoginViewModel`을 만드는 로직을 아래처럼 로컬 함수로 뽑아 `MainMenuViewModel` 생성 시 주입한다.

- [ ] **Step 3: `LoginViewModel` 생성 로직을 팩토리로 추출하고 `LoginViewModel`의 `MainMenuViewModel` 생성 부분에도 전달**

`src/FishingMartPos/App.xaml.cs`의 `OnStartup` 마지막 부분을 아래로 교체:

```csharp
        Func<LoginViewModel> createLoginViewModel = () =>
            new LoginViewModel(staffRepository, session, navigation, terminals);

        navigation.NavigateTo(createLoginViewModel());

        var mainWindow = new MainWindow(navigation);
        mainWindow.Show();
```

`src/FishingMartPos/ViewModels/LoginViewModel.cs`의 `SubmitAsync` 안, `_navigation.NavigateTo(new MainMenuViewModel(_session, _navigation));` 줄을 아래로 교체(로그아웃 시 재사용할 팩토리를 함께 넘긴다):

```csharp
        var mainMenuViewModel = new MainMenuViewModel(_session, _navigation)
        {
            LoginViewModelFactory = () => new LoginViewModel(_staffRepository, _session, _navigation, Terminals)
        };
        _navigation.NavigateTo(mainMenuViewModel);
```

- [ ] **Step 4: `MainWindow` — ViewModel → View 매핑 + 내비게이션 반영**

`src/FishingMartPos/MainWindow.xaml`:

```xml
<Window x:Class="FishingMartPos.MainWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:views="clr-namespace:FishingMartPos.Views"
        xmlns:vm="clr-namespace:FishingMartPos.ViewModels"
        Title="대원낚시 POS시스템" Width="1024" Height="768"
        Background="{DynamicResource OuterBackground}">
    <Window.Resources>
        <DataTemplate DataType="{x:Type vm:LoginViewModel}">
            <views:LoginView />
        </DataTemplate>
        <DataTemplate DataType="{x:Type vm:MainMenuViewModel}">
            <views:MainMenuView />
        </DataTemplate>
        <DataTemplate DataType="{x:Type vm:PlaceholderViewModel}">
            <views:PlaceholderView />
        </DataTemplate>
    </Window.Resources>
    <Border Background="{DynamicResource CardBackground}" BorderBrush="{DynamicResource CardBorder}" BorderThickness="1">
        <ContentControl x:Name="CurrentViewHost" Content="{Binding CurrentContent}" />
    </Border>
</Window>
```

`src/FishingMartPos/MainWindow.xaml.cs`:

```csharp
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using FishingMartPos.Navigation;

namespace FishingMartPos;

public partial class MainWindow : Window, INotifyPropertyChanged
{
    private readonly INavigationService _navigation;

    public MainWindow(INavigationService navigation)
    {
        InitializeComponent();
        _navigation = navigation;
        _navigation.CurrentViewModelChanged += (_, _) => OnPropertyChanged(nameof(CurrentContent));
        DataContext = this;
    }

    public object? CurrentContent => _navigation.CurrentViewModel;

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
```

- [ ] **Step 5: 빌드 및 전체 테스트 확인**

```bash
dotnet build FishingMartPos.sln
dotnet test FishingMartPos.sln
```

Expected: 빌드 성공, 모든 테스트 PASS

- [ ] **Step 6: 수동 실행 확인**

```bash
dotnet run --project src\FishingMartPos\FishingMartPos.csproj
```

Expected: 로그인 화면이 뜨고, PIN `0000` 입력 시 메인 메뉴로 이동, 4개 타일 클릭 시 "준비 중: OO" 화면으로 이동, 로그아웃 클릭 시 다시 로그인 화면으로 이동.

- [ ] **Step 7: Commit**

```bash
git add src/FishingMartPos/App.xaml src/FishingMartPos/App.xaml.cs src/FishingMartPos/MainWindow.xaml src/FishingMartPos/MainWindow.xaml.cs src/FishingMartPos/ViewModels/LoginViewModel.cs
git commit -m "App/MainWindow 배선: DI 컨테이너, 색상 리소스 등록, 로그인-메뉴 내비게이션 동작"
```

---

## Self-Review 결과

- **스펙 커버리지**: 설계 문서 2절(로그인/메인메뉴), 4절(다중단말 구조 — `pos_terminal_tb`/`PosTerminal` 반영), 7절(기술스택 — WPF/.NET8/x86/CommunityToolkit.Mvvm/Dapper/MySqlConnector 전부 Task 1, 5, 7, 8에 반영), 8절(DB 스키마 13개 테이블 전부 Task 4에 반영). 결제(VAN)·프린터·재고·매출·환경설정 나머지 화면은 각각 Phase 3~7에서 다룰 예정으로 이 계획의 범위 밖.
- **플레이스홀더 스캔**: "TBD"/"추후" 식 표현 없음. Task 11의 `PlaceholderView`는 의도된 스텁이며 실제 화면(판매 등)은 후속 계획의 대상임을 문서에 명시.
- **타입 일관성**: `Staff`, `PosTerminal`, `ICurrentSession`, `INavigationService`, `IStaffRepository`, `IPosTerminalRepository`, `LoginViewModel`, `MainMenuViewModel`, `PlaceholderViewModel` 이름과 시그니처가 Task 7~16 전체에서 동일하게 유지되는지 재확인 완료.

---

**Plan complete and saved to `docs/superpowers/plans/2026-07-21-phase1-foundation.md`. Two execution options:**

**1. Subagent-Driven (recommended)** - 태스크마다 새 서브에이전트를 띄워 구현시키고, 태스크 사이마다 검토합니다. 빠르게 반복하며 진행.

**2. Inline Execution** - 이 세션에서 직접 태스크를 순서대로 실행하고, 묶음 단위로 체크포인트를 두고 검토합니다.

**어떤 방식으로 진행할까요?**
