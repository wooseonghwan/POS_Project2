# 재고관리 화면 설계

- 작성일: 2026-07-21
- 상태: 설계 확정 (사용자 승인 완료), 구현계획(writing-plans) 이전 단계
- 관련 문서: `docs/superpowers/specs/2026-07-21-fishing-mart-pos-design.md` (§2, 화면 4: 재고관리)

## 1. 배경

전체 설계문서의 화면 구성표에 따르면 재고관리 화면은 "완성"(디자인 프로토타입 존재)으로 표시되어 있으나, 원본 디자인 파일(`Fishing Mart POS.html`)이 이 저장소에 존재하지 않고 다른 경로에서도 찾을 수 없었다. 따라서 이번 화면은 **디자인 프로토타입을 1:1로 포팅하는 대신, 전체 설계문서의 텍스트 설명(검색, 목록: 사진/대분류/소분류/POS분류/상품명/바코드/단가/재고/삭제)과 이미 구현된 `MainMenuView`/`PosView`의 톤앤매너(AppColors, 카드형 레이아웃)를 기준으로 새로 설계**한다.

Phase 2(판매 화면)까지 구현이 끝난 상태이며, `MainMenuViewModel.GoToInventory`는 현재 `PlaceholderViewModel("재고")`로 연결되어 있다. 이번 작업은 이를 실제 재고관리 화면으로 대체한다.

## 2. 범위

- **포함**: 상품 목록 조회, 검색(상품명+바코드), 카테고리(POS분류) 필터, 상품 소프트 삭제(비활성화)
- **제외**: 상품 등록/수정(별도 "상품등록" 화면), 재고 수량 직접 조정, 상품 사진 업로드, 삭제된 상품 복구

## 3. 데이터 / Repository 변경

- `IProductRepository`에 메서드 추가:
  ```csharp
  Task DeactivateAsync(string barcode);
  ```
  구현: `UPDATE product_tb SET use_yn = 'N' WHERE barcode = @Barcode`
- 목록 조회는 기존 `GetActiveAsync()`(`use_yn = 'Y'` 필터)를 그대로 재사용한다. 삭제(비활성화)된 상품은 별도 복구 기능 없이 즉시 목록에서 사라진다.
- 카테고리 필터 옵션은 기존 `ICodeRepository.GetByGroupAsync("POSCAT")`을 재사용한다.
- 대/소분류명 표시를 위해 `ICodeRepository.GetByGroupAsync("MAJOR")`, `GetByGroupAsync("MINOR")`도 함께 로드해 코드→이름 매핑에 사용한다.

## 4. ViewModel: `InventoryViewModel`

**의존성**: `IProductRepository`, `ICodeRepository`, `ICurrentSession`

**상태**:
- 로드 시 전체 활성 상품 + 코드 매핑을 한 번 캐싱한다.
- `string SearchText` — 상품명 또는 바코드에 부분일치하는 항목만 표시(대소문자/공백 무관, 두 필드에 OR 조건).
- `CodeItem? SelectedCategory` — POSCAT 목록 + "전체" 항목. "전체" 선택 시 필터 없음.
- `ObservableCollection<InventoryRowViewModel> Rows` — `SearchText`와 `SelectedCategory` 조건을 캐시된 전체 목록에 적용한 결과(클라이언트 사이드 필터링, `PosViewModel.RefreshVisibleProducts`와 동일한 패턴).
- `bool IsAdmin` — `ICurrentSession.CurrentStaff.IsAdmin`을 노출(화면에서 삭제 열 표시 여부 등에 사용 가능).

**`InventoryRowViewModel`** (표시 전용, 읽기 전용 프로퍼티):
- `Barcode`, `Name`, `MajorName`, `MinorName`, `PosCatName`, `PriceStr`(천단위 콤마 + "원"), `StockQtyStr`
- `Swatch`(`Brush`) — `PosViewModel`과 동일하게 `ProductSwatch0`~`5` 6색을 순환 배정(사진 대체)
- `bool CanDelete` — 생성 시점의 `IsAdmin` 값으로 고정
- `DeleteCommand` — 부모 뷰모델에 삭제 확인 모달을 띄우도록 위임

**삭제 흐름**:
1. 행의 삭제 버튼 클릭 → `PendingDeleteBarcode`/`PendingDeleteName` 저장, `IsDeleteConfirmVisible = true`
2. 확인 모달에 "{상품명} 상품을 삭제하시겠습니까?" 표시, 확인/취소 버튼
3. `ConfirmDeleteCommand` → `IProductRepository.DeactivateAsync(barcode)` 호출 → 캐시된 전체 목록에서 제거 → `Rows` 재계산 → 모달 닫기
4. `CancelDeleteCommand` → 모달만 닫고 아무 변화 없음

**권한**: `ICurrentSession.CurrentStaff.IsAdmin`이 `false`(STAFF)이면 모든 행의 `CanDelete`가 `false`가 되어 뷰에서 삭제 버튼이 비활성화(또는 숨김)된다. 전체 설계문서 §3(직원은 재고 "조회"만 가능)과 일치한다.

**네비게이션**: "메인메뉴로" 버튼 클릭 시 `INavigationService`를 통해 `MainMenuViewModel`로 복귀한다.

## 5. View: `InventoryView.xaml`

- `MainMenuView`/`PosView`와 동일한 톤앤매너(`AppColors` 브러시, 카드형 레이아웃, 헤더 스타일)를 따른다.
- **상단**: 검색 텍스트박스 + POS분류 드롭다운(콤보박스, "전체" + 8종) + 메인메뉴로 돌아가기 버튼
- **목록**: 스크롤 가능한 행 리스트. 컬럼 구성 — 사진(색상 스와치 블록) / 대분류 / 소분류 / POS분류 / 상품명 / 바코드 / 단가 / 재고 / 삭제 버튼
- **삭제 확인 모달**: `LoginView`의 오류 모달과 동일한 오버레이 스타일(`ModalOverlay` 브러시)을 재사용해 확인/취소 버튼을 배치한다.
- 페이지네이션은 두지 않는다(현재 상품 수 43개 규모, 스크롤로 충분).

## 6. 배선 (Wiring)

- `MainMenuViewModel`에 `Func<Task<InventoryViewModel>>? InventoryViewModelFactory` 추가, `GoToInventory` 커맨드가 `PosViewModel`과 동일한 패턴(`await` 후 `_navigation.NavigateTo(...)`)으로 실제 화면에 진입하도록 변경.
- `App.xaml.cs`에 `InventoryViewModel` 팩토리 DI 등록, `MainMenuViewModel` 생성 시 팩토리 주입.
- `MainWindow.xaml`에 `InventoryViewModel` → `InventoryView` `DataTemplate` 추가.

## 7. 에러 처리

- 삭제 대상 상품이 이미 목록에서 사라진 상태(동시성 등)에서 확인을 눌러도 `DeactivateAsync`는 멱등적으로 동작(대상 없으면 0행 업데이트, 예외 없음).
- 목록 로드 실패(DB 연결 등)에 대한 별도 재시도 UI는 이번 범위에 포함하지 않는다(Phase 1/2 기존 화면들과 동일 수준).

## 8. 테스트 계획 (TDD)

- `tests/FishingMartPos.Tests/Fakes/FakeProductRepository.cs`: `DeactivateAsync` 추가(호출된 바코드를 기록하고, 내부 목록에서도 `use_yn`을 반영하거나 제거).
- `tests/FishingMartPos.Tests/ViewModels/InventoryViewModelTests.cs`:
  - 로드 후 `Rows`에 캐싱된 상품이 올바르게 표시되는지
  - `SearchText` 입력 시 상품명/바코드 부분일치 필터링
  - `SelectedCategory` 변경 시 POS분류 필터링
  - 삭제 버튼 → 확인모달 표시 → 확인 시 `DeactivateAsync` 호출 및 `Rows`에서 제거
  - 확인모달에서 취소 시 `DeactivateAsync` 미호출, `Rows` 불변
  - STAFF로 로그인한 세션에서는 모든 행의 `CanDelete`가 `false`
- `tests/FishingMartPos.Tests/ViewModels/MainMenuViewModelTests.cs`: `GoToInventory` 기대값을 `InventoryViewModel` 내비게이션으로 수정
- `tests/FishingMartPos.Tests/Repositories/ProductRepositoryTests.cs`: `DeactivateAsync` 통합 테스트 추가(실제 DB에 대해 실행 후 `use_yn`을 원복하는 정리 코드 포함)
- `tests/FishingMartPos.Tests/Views/InventoryViewSmokeTests.cs`: 기존 `PosViewSmokeTests`와 동일한 패턴으로 XAML 로드 스모크 테스트

## 9. 열린 항목

- 원본 디자인 HTML이 확보되면 색상/간격/문구를 대조해 재조정이 필요할 수 있다(이번 설계는 기존 구현 화면들의 톤앤매너를 기준으로 함).
