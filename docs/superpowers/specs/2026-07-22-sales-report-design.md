# 매출관리 화면 설계

## 배경

메인메뉴의 "매출" 타일은 현재 `PlaceholderViewModel("매출")`로 연결된 미구현 화면이다. 원본 디자인(`Fishing Mart POS.html`)에는 완성된 매출관리 화면이 있었고(일일/월간 탭, 기간 필터, 요약 타일 4개, 상세 테이블), `sales_header_tb`에 필요한 컬럼(`pay_type`, `status`)이 이미 갖춰져 있어 이번에 실제 조회 화면으로 구현한다.

## 목표

- 기간(from~to)을 지정해 완료된(`status='COMPLETE'`) 매출을 일별 또는 월별로 조회한다.
- 총매출액/현금/카드결제1/카드결제2 요약을 한눈에 보여준다.
- 날짜(또는 월)별 상세 테이블을 최신순으로 보여준다.
- 매출취소(개별 건 취소) 기능은 이번 범위에 포함하지 않는다 — 조회 전용 화면이다.
- 메인메뉴의 "매출" 타일은 ADMIN 전용으로 제한한다(직원=판매/보류리콜/재고조회, 관리자=+매출관리 등 기존 권한 모델과 동일 패턴).

## 아키텍처

기존 `InventoryViewModel`의 "리포지토리는 원본 데이터만 주고, ViewModel이 필터/가공한다" 패턴을 그대로 따른다.

### 데이터 계층

`ISalesRepository`에 메서드 추가:

```csharp
Task<IReadOnlyList<SaleHeader>> GetCompletedSalesAsync(DateTime from, DateTime to);
```

- **계약**: `from`은 포함(inclusive), `to`는 제외(exclusive)하는 반열린 구간이다. 즉 "이 범위의 마지막 날 전체"를 포함하려면 호출자가 `to`에 하루를 더한 자정 시각을 넘겨야 한다(`WHERE sale_dt >= @From AND sale_dt < @To AND status = 'COMPLETE'`). 이 규칙은 구현 태스크의 코드 스니펫에 정확한 예시로 명시한다 — "포함/제외" 해석을 각 태스크가 임의로 다르게 하지 않도록.
- `status = 'COMPLETE'`만 조회해서, 향후 매출취소 기능이 생겨도 이 화면의 합계에 취소 건이 자동으로 반영되지 않게(즉 애초에 안 잡히게) 한다.
- 정렬은 리포지토리에서 하지 않는다(ViewModel이 그룹핑 후 정렬).

`SalesRepository`에 위 메서드를 구현한다(단순 `SELECT ... WHERE ...`, 트랜잭션 불필요).

### 집계 로직 (`SalesReportViewModel`)

- 탭(`IsDailyTab`)이 바뀌거나 날짜 범위(`DateFrom`/`DateTo`)가 바뀌거나 화면 진입 시 **매번 리포지토리를 재조회**한다. 실제 DB 조회에 쓸 `(from, to)` 경계는 탭에 따라 다르게 계산한다(월간매출 탭은 일별매출 탭보다 더 넓은 범위를 조회해야 하므로, 탭 전환 자체가 조회 범위를 바꾼다 — 캐시해서 재그룹핑만 하는 최적화는 하지 않는다):
  - **일일매출 탭**: `from = DateFrom.Date`, `to = DateTo.Date.AddDays(1)` (선택한 날짜 그대로, 하루 단위)
  - **월간매출 탭**: `from = DateFrom`이 속한 달의 1일, `to = DateTo`가 속한 달의 다음 달 1일 (즉 두 날짜가 걸친 달 전체를 다 가져온다 — 월 중간 날짜를 골라도 그 달 전체 합계가 나와야 하므로)
- 그룹핑:
  - 일일: `SaleDt.Date`로 그룹 → 키는 `yyyy-MM-dd`
  - 월간: `new DateTime(SaleDt.Year, SaleDt.Month, 1)`로 그룹 → 키는 `yyyy-MM`
  - 각 그룹의 정렬은 **최신순(내림차순)**
- 각 그룹(행)당 계산값:
  - `Count` = 그룹 내 건수
  - `Cash` = `PayType == "CASH"`인 건들의 `TotalAmt` 합
  - `Card1` = `PayType == "CARD1"`인 건들의 `TotalAmt` 합
  - `Card2` = `PayType == "CARD2"`인 건들의 `TotalAmt` 합
  - `Amount` = 그룹 전체 `TotalAmt` 합 (Cash+Card1+Card2와 항상 같음 — 한 매출 건은 결제수단이 하나뿐이므로)
- 화면 상단 요약 타일 4개는 **현재 필터링된 전체 행들의 합** (그룹 합계가 아니라 조회된 전체 기간의 총합).
- `HasRows` = 그룹 결과가 1개 이상인지 여부. `false`면 목록 자리에 "조회된 매출이 없습니다" 안내 문구를 보여준다(보류목록 빈 상태와 동일 패턴).

### DatePicker 어댑테이션 (원본 디자인과의 유일한 차이)

원본은 `<input type="date">`/`<input type="month">`를 탭에 따라 바꿔치기하지만, WPF `DatePicker`는 월 단위 선택을 기본 지원하지 않는다. 두 탭이 **동일한 `DatePicker` 두 개(From/To)를 공유**하고:
- 값 자체는 항상 완전한 날짜(`DateTime?`)로 유지한다.
- 월간매출 탭에서는 위 "월간매출 탭" 규칙대로 선택된 날짜가 속한 달을 자동으로 확장해서 조회한다.
- 탭을 전환해도 From/To 값 자체는 리셋하지 않는다(사용자가 고른 날짜를 그대로 유지, 재해석만 다르게 함).
- From > To인 경우 별도 에러 처리는 하지 않는다 — 자연히 조회 결과가 0건이 되어 빈 상태 문구가 뜨는 것으로 충분하다(원본 목업도 별도 검증 없음).

## 화면 구성 (원본 디자인 그대로)

1. **상단 헤더**: "매출관리" 제목 + "메인메뉴로" 버튼 (재고관리 화면과 동일 위치/스타일)
2. **탭**: "일일매출" / "월간매출" — 진입 시 기본값은 **일일매출 탭, 최근 7일**(`DateFrom = 오늘-6일`, `DateTo = 오늘`)
3. **기간 필터**: "기간" 라벨 + `DatePicker` 2개(~ 로 연결)
4. **요약 타일 4개** (가로 배치): 총매출액(강조색) / 현금 / 카드결제1 / 카드결제2
5. **상세 테이블**: 헤더(날짜(or 월) / 건수 / 현금 / 카드결제1 / 카드결제2 / 합계) + 스크롤 가능한 행 목록, 최신순
6. **빈 상태**: 테이블 자리에 "조회된 매출이 없습니다" 안내 문구 (행이 0개일 때)

## 권한 (ADMIN 전용)

- `MainMenuViewModel`에 `IsAdmin` 프로퍼티 추가 (`InventoryViewModel.IsAdmin`과 동일하게 `_session.CurrentStaff?.IsAdmin ?? false`).
- 메인메뉴 XAML의 "매출" 타일 버튼에 `Visibility="{Binding IsAdmin, Converter={StaticResource BooleanToVisibilityConverter}}"` 추가.
- `GoToSalesReportCommand`에도 `if (!IsAdmin) return;` 가드 추가 (버튼이 숨겨져 있어도 커맨드 자체를 방어).

## 네비게이션 / DI 배선

- `InventoryViewModel`이 이미 쓰고 있는 "메인메뉴 → 자식 화면 → 메인메뉴로 복귀" 팩토리 패턴을 그대로 따른다.
- `MainMenuViewModel`에 `Func<MainMenuViewModel, Task<SalesReportViewModel>>? SalesReportViewModelFactory` 추가.
- `GoToSalesReportCommand`: `PlaceholderViewModel("매출")` 대신 팩토리를 호출해 `SalesReportViewModel`을 만들고 `LoadAsync()` 후 네비게이션.
- `SalesReportViewModel`은 생성자에서 `ISalesRepository`와 `MainMenuViewModel returnTo`를 받는다. "메인메뉴로" 버튼은 `_navigation.NavigateTo(_returnTo)`.
- `App.xaml.cs`: `ISalesRepository`는 이미 DI에 등록되어 있으므로 그대로 재사용, `CreateMainMenuViewModelAsync`(또는 현재 메인메뉴 생성 지점)에서 `vm.SalesReportViewModelFactory = (mainMenu) => ...` 형태로 배선.
- `MainWindow.xaml`에 `<DataTemplate DataType="{x:Type vm:SalesReportViewModel}"><views:SalesReportView /></DataTemplate>` 추가.

## 새로 생기는 파일

- `src/FishingMartPos/ViewModels/SalesReportViewModel.cs`
- `src/FishingMartPos/ViewModels/SalesReportRowViewModel.cs` (Label/CountStr/CashStr/Card1Str/Card2Str/AmountStr — 모두 화면 표시용 포맷된 문자열)
- `src/FishingMartPos/Views/SalesReportView.xaml` (+ `.xaml.cs`)

## 수정되는 기존 파일

- `src/FishingMartPos/Repositories/ISalesRepository.cs`, `SalesRepository.cs` — `GetCompletedSalesAsync` 추가
- `src/FishingMartPos/ViewModels/MainMenuViewModel.cs` — `IsAdmin`, `SalesReportViewModelFactory`, `GoToSalesReportCommand` 가드
- `src/FishingMartPos/Views/MainMenuView.xaml` — "매출" 타일 Visibility 바인딩
- `src/FishingMartPos/App.xaml.cs` — 팩토리 배선
- `src/FishingMartPos/MainWindow.xaml` — DataTemplate 추가

## 테스트 계획

- `SalesRepositoryTests`: 기존 파일에 `GetCompletedSalesAsync` 테스트 추가 — 실제 개발 DB에 `CreateSaleAsync`로 테스트 데이터를 만들고(다른 날짜/결제수단 조합 포함), 범위 안/밖 필터링과 `status != 'COMPLETE'`(직접 UPDATE로 CANCELLED로 바꾼 행) 제외를 검증한 뒤 정리(cleanup)한다.
- `SalesReportViewModelTests`: `FakeSalesRepository`(현재는 `CreateSaleAsync` 기록만 하므로, `GetCompletedSalesAsync`를 지원하도록 확장 필요)로 순수 단위 테스트:
  - 일별 그룹핑/합계 계산이 맞는지
  - 월별 그룹핑/합계 계산이 맞는지 (월 중간 날짜를 선택해도 그 달 전체가 나오는지)
  - 탭 전환 시 월간매출 탭에 맞는(달 전체로 확장된) 범위로 재조회하는지
  - 날짜 범위 변경 시 새 범위로 재조회하는지
  - 빈 결과일 때 `HasRows == false`
  - ADMIN이 아닌 세션에서 `GoToSalesReportCommand`(메인메뉴 쪽)가 아무 동작도 하지 않는지
