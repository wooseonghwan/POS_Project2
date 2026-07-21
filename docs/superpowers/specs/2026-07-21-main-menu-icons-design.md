# 메인메뉴 타일 아이콘 복원 설계

## 배경

원본 디자인(`Kiosk screen design.zip` 내 `Fishing Mart POS.html`, 프로젝트 루트에 존재 — 이전에 "찾을 수 없음"으로 알고 있었으나 실제로는 zip 안에 있었음)의 메인메뉴 4개 타일(판매/매출/재고/환경설정)에는 라벨 위에 CSS(div/border/absolute position)로 그린 작은 아이콘이 있었다. 현재 `MainMenuView.xaml`에는 이 아이콘이 없고 텍스트 라벨만 있다 — git log상 커밋이 한 번뿐이라 애초에 포함된 적이 없다(당시 원본 HTML을 못 찾은 상태로 새로 디자인했기 때문).

색상 인프라는 이미 준비되어 있다 — `src/FishingMartPos/Theme/AppColors.cs`에 `Accent`, `MenuIconSales`(oklch 0.55/0.09/30), `MenuIconInventory`(oklch 0.55/0.09/250), `MenuIconSettingsTrack`(oklch 0.7/0.01/250), `MenuIconSettingsDot`(oklch 0.55/0.09/340)가 이미 정의되어 있다. 실제 도형만 XAML에 빠져 있다.

## 목표

`MainMenuView.xaml`의 4개 타일 버튼에, 원본 HTML과 동일한 좌표·비율의 아이콘 도형을 추가한다. 새 색상은 만들지 않고 기존 `DynamicResource` 브러시만 사용한다.

## 구현 방식

각 타일 버튼의 `TextBlock` 라벨 위에 `StackPanel`(Vertical, 기존 HTML의 `gap:14px`에 대응하는 Margin)을 두고, 그 안에 고정 크기 `Canvas`(원본 div의 px 크기 그대로)를 추가한다. Canvas 내부는 원본 div 좌표를 그대로 `Canvas.Left`/`Canvas.Top`으로 옮긴 `Border`(사각형/사각테두리) 또는 `Ellipse`(원)로 구성한다.

### 아이콘별 상세

- **판매** (Canvas 48×48): `Border` Width=48 Height=48 CornerRadius=6 BorderThickness=3 BorderBrush={DynamicResource Accent} Background=Transparent + 내부에 가로 막대(Border 또는 Rectangle) 3개, Width=36 Height=3 Fill/Background={DynamicResource Accent}, Canvas.Left=6, Canvas.Top=10/18/26.
- **매출** (Canvas 48×36): 세로 막대 4개(Rectangle) Width=9 Fill={DynamicResource MenuIconSales}, Canvas.Left=0/14/28/42, 높이는 36의 40%/70%/100%/55%(≈14/25/36/20)이고 바닥 정렬(Canvas.Top = 36 - height).
- **재고** (Canvas 48×40): 아래쪽 `Border` Width=48 Height=30 CornerRadius=4 BorderThickness=3 BorderBrush={DynamicResource MenuIconInventory}, Canvas.Top=10 + 위쪽 "뚜껑" `Border` Width=32 Height=14 CornerRadius="4,4,0,0" BorderThickness="3,3,3,0" BorderBrush={DynamicResource MenuIconInventory}, Canvas.Left=8, Canvas.Top=0.
- **환경설정** (Canvas 48×40): 가로 트랙 3개(`Border` 또는 `Rectangle`) Width=48 Height=3 CornerRadius=2 Background={DynamicResource MenuIconSettingsTrack}, Canvas.Top=0/18/37 + 각 트랙 위 손잡이(`Ellipse`) Width=14 Height=14 Fill={DynamicResource MenuIconSettingsDot}, Canvas.Top = 트랙Top-6, Canvas.Left=28/10/20(트랙 순서대로).

## 범위 밖

- 새 색상/브러시 추가 없음(기존 것만 재사용).
- 다른 화면(판매/재고관리 등)의 아이콘 복원은 이번 범위에 포함하지 않는다 — 이번엔 메인메뉴 4타일만.
- 애니메이션/호버 효과는 다루지 않는다(기존 타일 호버 스타일 그대로 유지).

## 테스트

- 기존 `MainMenuViewSmokeTests.MainMenuView_ConstructsWithoutException`이 XAML 파싱 오류를 잡아준다 — 추가 자동 테스트는 필요 없음(순수 정적 비주얼 변경).
- 구현 후 앱을 직접 실행해 스크린샷으로 원본 HTML 렌더링과 육안 대조한다.
