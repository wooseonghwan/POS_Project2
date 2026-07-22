# 환경설정 하위화면 설계

## 배경

메인메뉴의 "환경설정" 타일은 현재 `PlaceholderViewModel("환경설정")`로 연결된 미구현 화면이며, 접근 권한 가드도 없다(누구나 들어갈 수 있음). 원본 디자인(`Fishing Mart POS.html`)에는 환경설정 허브 화면(5개 메뉴: 상품코드관리/직원관리/프린터설정/영수증설정/시스템정보)이 있고, 이 중 "상품 코드관리"만 실제로 연결되어 있었다(나머지 4개는 정적 텍스트만 존재). Phase 1 DB 스키마(`db/migrations/001_create_schema.sql`)에는 이번에 필요한 4개 테이블(`staff_tb`, `printer_config_tb`, `receipt_config_tb`, `system_info_tb`)이 이미 만들어져 있어 새 마이그레이션 없이 시작한다.

## 목표

- 환경설정 허브 화면(5개 메뉴 리스트)을 만들고, 메인메뉴의 "환경설정" 타일을 ADMIN 전용으로 제한한다(매출관리와 동일한 이중 가드 패턴: 타일 Visibility + 커맨드 가드).
- **직원관리**: 목록(직원코드/이름/권한/사용유무) + 등록·수정 폼. PIN 재설정은 관리자가 새 PIN을 직접 입력.
- **프린터설정**: 현재 POS 단말 기준 포트/모델/돈통신호 사용여부 저장. "테스트 인쇄"는 이번 범위에서 실제 인쇄 없이 "지원 예정" 안내만.
- **영수증설정**: 현재 POS 단말 기준 영수증 상단/하단 문구 저장.
- **시스템정보**: 앱 버전/DB 버전/현재 POS 단말/DB 연결상태를 읽기 전용으로 표시.
- **상품코드관리**: 기존 `ICodeRepository`(이미 `GetByGroupAsync`/`AddAsync`/`DeleteAsync` 보유)를 재사용하는 신규 독립 화면. 대분류/소분류/POS분류 3열을 원본 목업처럼 항상 펼쳐서 보여준다(상품등록 폼의 토글 방식과 다름 — 이 화면은 코드 관리가 목적이므로 처음부터 다 보여주는 게 자연스럽다).
- 매출취소/할인 등은 이번 범위 밖(원본 권한모델에는 있으나 별도 기능이며 이번 요청과 무관).

## 아키텍처

기존 화면들과 동일한 원칙을 따른다: 리포지토리는 원본 데이터만 주고 ViewModel이 가공하며, 목록+폼이 필요한 화면(직원관리)은 `InventoryView`/`InventoryFormView` 쌍과 동일한 팩토리·returnTo 패턴을 쓴다.

### 네비게이션 구조

```
MainMenuViewModel --(GoToSettings, ADMIN 전용)--> SettingsViewModel (허브)
SettingsViewModel --(5개 항목 클릭)--> StaffListViewModel / PrinterSettingsViewModel / ReceiptSettingsViewModel / SystemInfoViewModel / CodeManageViewModel
StaffListViewModel --(+ 등록 / 행별 수정)--> StaffFormViewModel --(저장/취소)--> StaffListViewModel
(나머지 4개 하위화면) --(← 환경설정으로)--> SettingsViewModel
SettingsViewModel --(← 메인메뉴로)--> MainMenuViewModel
```

`SettingsViewModel`은 `InventoryViewModel`이 `InventoryFormViewModelFactory`를 갖듯, 5개 하위화면 각각에 대한 `Func<SettingsViewModel, Task<XViewModel>>` 팩토리 프로퍼티를 갖는다. `StaffListViewModel`은 별도로 `StaffFormViewModelFactory: Func<StaffListViewModel, Staff?, Task<StaffFormViewModel>>`를 갖는다(재고관리의 `InventoryFormViewModelFactory`와 정확히 같은 모양).

`MainMenuViewModel`은 새 `SettingsViewModelFactory: Func<MainMenuViewModel, Task<SettingsViewModel>>?`를 갖고, `LoginViewModel`의 생성자에 4번째 팩토리 파라미터가 추가된다(기존 Pos/Inventory/SalesReport 팩토리와 동일한 방식으로 스레딩).

### 데이터 계층

**`IStaffRepository`에 메서드 추가** (기존 `FindByPinAsync`는 그대로 유지):
```csharp
Task<IReadOnlyList<Staff>> GetAllAsync(); // use_yn 상관없이 전체 (로그인용 FindByPinAsync는 use_yn='Y'만 보므로 별개)
Task CreateAsync(Staff staff, string pin); // pin은 PinHasher.Hash로 해시해서 저장
Task UpdateAsync(Staff staff, string? newPin); // newPin이 null/공백이면 기존 pin_hash 유지, 아니면 갱신
```

**신규 모델 + 리포지토리** (각각 대응하는 기존 테이블 그대로 사용):

```csharp
// Models/PrinterConfig.cs
public sealed class PrinterConfig
{
    public required string PosCd { get; init; }
    public string? PrinterPort { get; init; }
    public string? PrinterName { get; init; }
    public required bool DrawerKickEnabled { get; init; }
}

// Repositories/IPrinterConfigRepository.cs
public interface IPrinterConfigRepository
{
    Task<PrinterConfig?> GetAsync(string posCd);
    Task SaveAsync(PrinterConfig config);
}
```
`drawer_kick_enabled`는 DB에 `CHAR(1)` 'Y'/'N'으로 저장되어 있으므로, 리포지토리 SQL에서 `CASE WHEN drawer_kick_enabled='Y' THEN 1 ELSE 0 END AS DrawerKickEnabled`(조회)와 `@DrawerKickEnabled를 "Y"/"N" 문자열로 변환해서 파라미터로 전달`(저장)로 변환한다 — 모델 자체는 bool로 깔끔하게 유지.

```csharp
// Models/ReceiptConfig.cs
public sealed class ReceiptConfig
{
    public required string PosCd { get; init; }
    public string? HeaderText { get; init; }
    public string? FooterText { get; init; }
}

// Repositories/IReceiptConfigRepository.cs
public interface IReceiptConfigRepository
{
    Task<ReceiptConfig?> GetAsync(string posCd);
    Task SaveAsync(ReceiptConfig config);
}
```

```csharp
// Models/SystemInfo.cs
public sealed class SystemInfo
{
    public required string AppVersion { get; init; }
    public required string DbVersion { get; init; }
}

// Repositories/ISystemInfoRepository.cs
public interface ISystemInfoRepository
{
    Task<SystemInfo?> GetAsync(); // system_info_tb는 항상 단일 행 — SELECT ... LIMIT 1
}
```

`PrinterConfig`/`ReceiptConfig`의 `SaveAsync`는 `INSERT ... ON DUPLICATE KEY UPDATE` (PK가 `pos_cd` 단독이므로 `ProductRepository.SaveAsync`와 동일한 upsert 패턴).

### 화면별 상세

**1. `SettingsViewModel` (허브)** — `ObservableCollection` 없이 5개 고정 메뉴 항목(각각 `RelayCommand`)과 `GoToMainMenuCommand`만 있는 단순 화면. 각 메뉴 클릭 시 해당 팩토리를 호출해 자신(this)을 넘기고 네비게이션.

**2. `StaffListViewModel`** — `LoadAsync()`가 `GetAllAsync()`로 전체 직원을 불러와 행(직원코드/이름/권한명/사용유무 표시)을 만든다. "+ 직원등록" 버튼(항상 ADMIN — 이 화면 자체가 이미 ADMIN 전용이므로 재고관리처럼 별도 `IsAdmin` 체크는 불필요), 행별 "수정" 버튼. `GoToSettingsCommand`로 허브 복귀.

**3. `StaffFormViewModel`** — 필드: `StaffCode`(등록 모드에서만 입력 가능, 수정 모드에서는 `IsReadOnly` — 상품등록 폼의 바코드 필드와 동일한 패턴), `StaffName`, `Role`(콤보박스 — 대분류 드롭다운과 동일하게 `DisplayMemberPath`는 "관리자"/"직원" 표시용 이름, `SelectedValuePath`는 실제 저장값 "ADMIN"/"STAFF"), `IsActive`(bool, 체크박스 "사용" — 체크됨=`use_yn='Y'`, 해제=`'N'`, 기본값은 등록 모드에서 체크됨), `PinInput`(등록 모드 필수 4자리 숫자, 수정 모드는 공란 허용=기존 유지).
   - 검증: 직원코드/이름 필수. 등록 모드에서 PIN 미입력 또는 4자리 숫자가 아니면 에러. 등록 모드에서 이미 존재하는 직원코드면 에러("이미 등록된 직원코드입니다"). 수정 모드에서 PIN을 입력했는데 4자리 숫자가 아니면 에러.
   - 저장 성공 시 "등록되었습니다"/"수정되었습니다" 토스트(매출관리 폼과 동일한 패턴, `IDelayProvider` 재사용) 후 `StaffListViewModel`로 복귀 + 목록 새로고침.
   - `Role` 콤보박스의 선택지는 DB 코드 테이블이 아니라 ViewModel 안에 고정된 2개 값이다: `new[] { new RoleOption("ADMIN", "관리자"), new RoleOption("STAFF", "직원") }` — 대/소/POS분류처럼 `ICodeRepository`를 거치지 않는다(권한은 이 앱의 2단계 권한모델에 고정되어 있고 사용자가 새 권한을 추가할 개념이 아니므로).

**4. `PrinterSettingsViewModel`** — `LoadAsync()`가 현재 세션의 `_session.CurrentTerminal.PosCode`로 `GetAsync` 호출, 없으면(신규 단말) 빈 값/`DrawerKickEnabled=true` 기본값. 필드: `PrinterPort`, `PrinterName`, `DrawerKickEnabled`(bool). `SaveCommand` → 저장 + "저장되었습니다" 토스트. `TestPrintCommand` → "프린터 연동은 지원 예정입니다" 토스트(정보성, `IsToastWarning=false`).

**5. `ReceiptSettingsViewModel`** — 위와 동일한 구조, 필드 `HeaderText`/`FooterText`(멀티라인 텍스트).

**6. `SystemInfoViewModel`** — `LoadAsync()`가 `ISystemInfoRepository.GetAsync()`를 호출하되 **DB 연결 자체가 실패할 경우를 대비해 try/catch로 감싼다**: 성공하면 `AppVersionStr`/`DbVersionStr`를 채우고 `DbConnectionStatusStr = "연결됨"`, 예외가 발생하면 두 값 다 "-"로, `DbConnectionStatusStr = "연결 안됨"`. `PosTerminalStr`은 세션의 `CurrentTerminal`에서 직접 구성(`"{PosName} ({PosCode})"`), DB 조회와 무관하므로 try/catch 밖에서 항상 채운다. 저장 기능 없음, 읽기 전용 표시만.

**7. `CodeManageViewModel`** — 대분류/소분류/POS분류 3개 컬럼을 항상 펼쳐서 보여준다. 각 컬럼: 코드 목록(이름+코드, 삭제 버튼) + 코드/이름 입력 + 추가 버튼. `InventoryFormViewModel`의 `AddMajorCode`/`DeleteMajorCode` 등과 **로직은 동일하지만 코드를 공유하지 않고 이 ViewModel에 독립적으로 다시 구현한다** — 두 화면이 이미 각자 안정적으로 동작 중인 상태에서 공용 서비스로 추출하는 리팩터링은 이번 범위 밖으로 두고(기존 검증된 코드를 건드리는 리스크 대비 이득이 크지 않음), 약 20~30줄의 유사 로직 중복을 감수한다. 삭제 시 "사용 중인 코드는 삭제할 수 없습니다" 가드는 `IProductRepository.GetActiveAsync()`로 조회한 상품 목록과 대조하는 동일한 방식.

## 권한 (ADMIN 전용)

- `MainMenuViewModel.GoToSettings`를 `GoToSalesReport`와 동일한 모양으로 변경: `if (!IsAdmin) return;` 가드 + 팩토리 호출.
- `MainMenuView.xaml`의 "환경설정" 타일에 `Visibility="{Binding IsAdmin, Converter={StaticResource BooleanToVisibilityConverter}}"` 추가(매출 타일과 동일).
- 허브 화면 진입 이후의 5개 하위화면은 이미 허브 자체가 ADMIN 전용 경로로만 도달 가능하므로 각 하위화면에 개별 `IsAdmin` 가드를 중복으로 넣지 않는다(재고관리의 "+ 상품등록" 버튼처럼 STAFF도 들어올 수 있는 화면 안에서 특정 동작만 막는 경우와는 상황이 다르다 — 이 경우는 화면 자체가 진입 불가).

## 네비게이션 / DI 배선

- `MainMenuViewModel`에 `SettingsViewModelFactory` 추가, `LoginViewModel` 생성자에 4번째 팩토리(`Func<MainMenuViewModel, Task<SettingsViewModel>>`) 추가하고 `SubmitAsync()`의 두 `MainMenuViewModel` 생성 지점 모두에 배선.
- **`App.xaml.cs` 수정 시 주의**: 지난 매출관리 작업에서 `CreateLoginViewModel()`이 `new(...)` 형태의 target-typed 표현식이라 `"new LoginViewModel("` 문자열 grep으로 걸리지 않는 호출부였음이 뒤늦게 발견된 적이 있다. 이번에도 `App.xaml.cs`를 반드시 직접 읽어서 `LoginViewModel` 생성자 호출부(및 파라미터 개수)를 확인하고 갱신할 것 — grep만 믿지 말 것.
- `App.xaml.cs`에 `CreateSettingsViewModelAsync(MainMenuViewModel mainMenu)` 팩토리 함수 추가(각 하위화면 팩토리를 내부에서 구성해 `SettingsViewModel`에 주입) + `CreateStaffListViewModelAsync`, `CreatePrinterSettingsViewModelAsync`, `CreateReceiptSettingsViewModelAsync`, `CreateSystemInfoViewModelAsync`, `CreateCodeManageViewModelAsync`, 그리고 `StaffListViewModel` 내부의 `StaffFormViewModelFactory` 배선.
- `MainWindow.xaml`에 7개 신규 ViewModel 각각의 `DataTemplate` 추가.
- DI 등록: `IPrinterConfigRepository`, `IReceiptConfigRepository`, `ISystemInfoRepository`를 `App.xaml.cs`의 `ServiceCollection`에 싱글톤으로 추가.

## 화면 레이아웃 (원본 디자인 기준)

- 허브 화면: "환경설정" 제목 + "← 메인메뉴로" 버튼(상단), 흰 카드 안에 5개 행(각 행 클릭 가능, 아래쪽에 구분선) — `InventoryView`의 헤더 스타일 재사용.
- 5개 하위화면 공통: 제목 + "← 환경설정으로" 버튼(상단), 나머지는 화면별로 다름(폼 vs 3열 그리드 vs 읽기전용 표시).
- 직원관리 목록/폼은 `InventoryView`/`InventoryFormView`와 동일한 흰 카드+테두리 톤앤매너.
- 상품코드관리는 원본 목업처럼 3열 그리드(`grid-template-columns:repeat(3,1fr)`에 대응하는 WPF `Grid` 3-ColumnDefinition).

## 테스트 계획

- `IStaffRepository`의 신규 메서드: 기존 `SalesRepositoryTests`/`CodeRepositoryTests`와 같은 방식(실제 개발 DB, 생성 후 cleanup)으로 통합 테스트.
- `IPrinterConfigRepository`/`IReceiptConfigRepository`/`ISystemInfoRepository`: 동일하게 실제 DB 통합 테스트(GetAsync가 없는 경우 null 반환, SaveAsync 후 재조회 시 값 일치, upsert가 기존 행을 덮어쓰는지).
- 각 ViewModel: Fake 리포지토리로 순수 단위 테스트. 특히 `StaffFormViewModelTests`(등록/수정/중복코드/PIN 검증 — `InventoryFormViewModelTests`와 유사한 케이스 구성), `SystemInfoViewModelTests`(DB 연결 실패 시나리오는 Fake 리포지토리가 예외를 던지도록 해서 검증), `CodeManageViewModelTests`(추가/삭제/사용중 가드 — `InventoryFormViewModelTests`의 코드관리 테스트와 유사).
- `MainMenuViewModelTests`: ADMIN은 `SettingsViewModel`로 이동, STAFF는 아무 동작 안 함(매출관리 때와 동일한 테스트 형태).
