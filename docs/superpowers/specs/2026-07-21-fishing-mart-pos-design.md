# 대원낚시 POS 재구축 설계 (VB6 → WPF/MVVM)

- 작성일: 2026-07-21
- 상태: 설계 확정 (사용자 승인 완료), 구현계획(writing-plans) 이전 단계

## 1. 배경 및 목표

기존 `TKMPOS_대원` VB6 POS 프로그램(구내식당/여러 업종에 재사용된 범용 코드베이스, 카드 VAN사 다중 연동·교직원공제·세콤 연동·티켓/탑승 관리 등 대원낚시 매장에는 불필요한 기능이 다수 포함)을 폐기하고, 새로 제공된 화면 디자인(HTML 목업)을 **1:1 그대로** WPF + MVVM으로 재구현한다.

- **디자인은 변경하지 않는다.** `Fishing Mart POS.html` / `pos-main-offline.html`의 레이아웃·색상(oklch 값)·타이포그래피·간격을 WPF XAML로 최대한 그대로 이식한다. View는 디자인 그대로, ViewModel/Model에서 MVVM으로 비즈니스 로직을 분리한다.
- **업무 범위는 소매 판매로 한정한다.** DB 덤프에는 입장권/탑승권(낚시공원 입장) 데이터가 대량으로 남아있으나, 이번 범위에서는 재고가 있는 상품(미끼/찌세트/바늘/생활용품/식품 등) 판매만 다룬다. 입장권/탑승권은 범위 밖(향후 확장 여지만 남김).
- **VB6 소스는 로직 참고용.** 기존 VB6 코드(특히 결제 흐름, 영수증 출력, VAN 연동 방식)를 분석해 필요한 로직만 가져오고, 불필요한 기능(교직원공제, PAYCO, 세콤연동, 급식/티켓 인쇄, 스포츠센터 등)은 버린다.
- **DB는 최소 재설계.** 기존 MariaDB 32개 테이블 중 이번 범위에 필요한 것만 추려 새 스키마로 만든다. 기존 DB는 변경 가능(사용자 승인됨).

## 2. 화면 구성 (디자인 그대로, 총 6개 화면 + 1개 신규 설계 필요)

디자인 파일: `Fishing Mart POS.html`(상호작용 프로토타입, 실제 카탈로그 포함) 기준.

| # | 화면 | 디자인 상 상태 | 설명 |
|---|------|----------------|------|
| 1 | 로그인 | 완성 | PIN 4자리 입력, POS1/POS2 단말 선택, 오류 시 모달 |
| 2 | 메인 메뉴 | 완성 | 판매/매출/재고/환경설정 4개 타일 |
| 3 | 판매(POS) | 완성 + 기능 추가 | 좌: 카테고리 탭 + 상품 그리드 / 좌하단: 장바구니(주문) 목록 / 하단: 수량 키패드, 현금·카드결제1·카드결제2, 영수증관리(직전정보/영수증발행), 돈통열기, 초기화. **보류(홀드) 기능은 디자인에 문구("티켓을 선택해주세요")만 있고 동작이 없어 신규로 추가 설계** |
| 4 | 재고관리 | 완성 | 검색, 목록(사진/대분류/소분류/POS분류/상품명/바코드/단가/재고/삭제) |
| 5 | 상품등록 | 완성 | 사진, 대/소/POS분류(인라인 코드관리 포함), 상품명, 바코드(공란시 자동생성), 단가, 초기재고 |
| 6 | 매출관리 | 완성 | 일일/월간 탭, 기간(from~to), 요약 4타일(총매출/현금/카드결제1/카드결제2), 일자별 테이블 |
| 7 | 환경설정 | 상품코드관리만 완성, 나머지 4항목은 메뉴만 존재 | 상품코드관리(완성) / 직원관리, 프린터설정, 영수증설정, 시스템정보는 화면을 신규 설계 |

### 2-1. 보류(홀드) 주문 — 신규 기능 설계

- 판매 화면에서 진행 중인 장바구니를 "보류" 버튼으로 저장하고, 새 손님의 새 주문을 바로 시작할 수 있다.
- 보류된 주문 목록(홀드 리스트)에서 원하는 건을 다시 불러와(리콜) 이어서 담거나 결제할 수 있다.
- 결제 완료된 보류 주문은 목록에서 제거된다.
- UI 배치: 기존 디자인을 깨지 않기 위해, 장바구니 상단(현재 "상품/매수/단가/금액" 헤더 위)에 보류 건수 뱃지 + "보류" / "보류목록" 버튗 한 줄을 추가하는 정도로 최소 침습적으로 얹는다. (상세 배치는 구현 단계에서 스크린샷 대조하며 확정)

### 2-2. 신규 설계가 필요한 환경설정 하위화면

- **직원 관리**: 직원 목록(코드/이름/권한/PIN 재설정/사용유무), 신규 등록. 목록형 + 등록 폼(재고관리/상품등록 화면과 동일한 톤앤매너로 제작).
- **프린터 설정**: 영수증 프린터 포트/모델 선택, 테스트 인쇄, 돈통 신호 사용 여부.
- **영수증 설정**: 상단 문구(매장명/사업자정보)·하단 문구(안내문) 편집.
- **시스템 정보**: 버전, POS 단말코드, DB 연결 상태, 라이선스 등 단순 표시.

## 3. 권한 모델

- 2단계: **관리자(ADMIN)** / **직원(STAFF)**
- 직원: 판매(결제), 보류/리콜, 재고 조회만 가능
- 관리자: 위 전체 + 매출취소(결제 취소/환불), 할인 적용, 매출관리 조회, 상품등록/코드관리, 환경설정 전체, 직원관리
- PIN 로그인 시 직원별 권한이 세션에 로드되어 화면/버튼 단위로 활성화 여부를 제어(관리자 전용 버튼은 직원 로그인 시 비활성 또는 숨김).

## 4. 시스템 구조

- 매장 내 여러 PC(단말)가 LAN으로 **중앙 MariaDB 서버 1대**를 공유. 로그인 화면의 POS1/POS2 선택 = 실제 물리 단말 식별자.
- 각 WPF 클라이언트는 Dapper로 MariaDB에 직접 접속(연결정보는 로컬 설정파일).
- 오프라인(서버 다운 시) 큐잉은 이번 범위에 포함하지 않음(매장 규모상 불필요로 판단, 필요 시 별도 확장).

## 5. 결제(VAN) 연동

| 버튼 | VAN사 | 연동 방식 | 근거 |
|------|-------|-----------|------|
| 카드결제1 | NCVAN | `NCPOS.dll` 네이티브 함수(`cryptCard`, `DataProcess`) P/Invoke | 기존 VB6에서 `frmMKT_CARD_NCVAN`이 실제 운영 중인 유일한 결제 경로였음 (`NCVAN.bas`) |
| 카드결제2 | KICC | 로컬 승인 에이전트에 `WinHttp` 방식 HTTP 호출 (KICC 결제 에이전트가 로컬에서 대기, POS는 HTTP로 승인요청) | `frmCREDIT_KICC.frm`에서 `CreateObject("Winhttp.WinHttpRequest.5.1")`로 구현되어 있음 |

- NCVAN(`NCPOS.dll`)은 **32비트 네이티브 DLL**이므로, WPF 앱은 **x86 플랫폼 타겟**으로 빌드해야 한다(AnyCPU/x64 불가). KICC는 HTTP 기반이라 비트수 제약 없음 — 앱 전체를 x86으로 통일해 단순화한다.
- 현금 결제는 하드웨어 연동 없이 즉시 완료 처리, 받은금액/잔돈 계산은 화면 로직 그대로.
- 결제 취소(카드 승인취소 등)는 관리자 권한에서만 수행.

## 6. 프린터 / 현금함(돈통) 연동

- 실제 ESC/POS 호환 영수증 프린터에 연결한다. 인쇄는 ESC/POS raw command로 직접 전송(포트: USB 시리얼/프린터 공유명, 프린터 설정 화면에서 선택).
- 돈통(현금함)은 프린터의 캐시드로어 킥아웃 신호(ESC/POS `DLE DC4` 계열 명령 또는 프린터 RJ11 포트를 통한 펄스)로 오픈 — 별도 드로어 컨트롤러 불필요, 프린터 경유가 업계 표준.
- 정확한 프린터 모델/명령어셋은 구현 단계에서 실물 확인 후 확정(우선 범용 ESC/POS 명령 세트로 구현하고, 프린터 설정 화면에서 필요 시 조정 가능하게 함).
- "영수증발행"(재발행) / "직전정보"는 마지막 결제 건을 다시 조회해 재인쇄하는 기능으로 구현.

## 7. 기술 스택

- **UI**: WPF, .NET 8, x86 플랫폼 타겟 (NCVAN DLL 제약)
- **MVVM**: CommunityToolkit.Mvvm (ObservableObject, RelayCommand)
- **데이터 액세스**: Dapper + MySqlConnector (MariaDB) — 직접 SQL, 스키마 변경 시 수기 마이그레이션 스크립트 관리
- **VAN 연동**: NCVAN(P/Invoke), KICC(HttpClient 기반 로컬 승인 요청)
- **프린터**: ESC/POS raw 명령 전송

## 8. DB 스키마 (신규, 최소 구성)

기존 32개 테이블 중 아래 항목은 **제외**한다: 교직원공제/PAYCO/세콤/급식/스포츠센터/탑승·입장권/화면-버튼 구성 테이블(design_tb, screen_tb, menu_but_tb 등 — 화면이 고정 디자인이라 불필요)/기존 관리자 메뉴권한 테이블(tb_admin_menu 등 — 2단계 역할로 단순화)/시간대·요일 코드(time_tb, week_tb — 이용시간 개념 불필요).

신규 테이블(13개):

1. **staff_tb** (직원) — `staff_cd`(PK), `staff_name`, `pin_hash`, `role`(`ADMIN`/`STAFF`), `use_yn`, `created_at`
2. **pos_terminal_tb** (POS 단말) — `pos_cd`(PK, '1'/'2'), `pos_name`
3. **code_tb** (공통 코드: 대/소/POS분류) — `code_gbn`(PK1, 'MAJOR'/'MINOR'/'POSCAT'), `code_cd`(PK2), `code_nm`, `sort_no`
4. **product_tb** (상품/재고) — `barcode`(PK), `major_cd`, `minor_cd`, `poscat_cd`, `name`, `price`, `stock_qty`, `photo_path`, `use_yn`, `created_at`, `updated_at`
5. **sales_header_tb** (매출 헤더) — `sale_no`(PK), `pos_cd`, `sale_dt`, `staff_cd`, `total_amt`, `pay_type`(`CASH`/`CARD1`/`CARD2`), `cash_received`, `change_amt`, `van_approval_no`, `van_code`, `status`(`COMPLETE`/`CANCELLED`), `created_at`
6. **sales_detail_tb** (매출 상세) — `sale_no`(FK), `line_no`, `barcode`(FK), `product_name`, `qty`, `unit_price`, `line_amt`
7. **held_order_tb** (보류 주문 헤더) — `hold_no`(PK), `pos_cd`, `staff_cd`, `held_at`, `memo`, `status`(`HELD`/`RECALLED`)
8. **held_order_detail_tb** (보류 주문 상세) — `hold_no`(FK), `line_no`, `barcode`, `product_name`, `qty`, `unit_price`
9. **printer_config_tb** (단말별 프린터 설정) — `pos_cd`(PK), `printer_port`, `printer_name`, `drawer_kick_enabled`
10. **receipt_config_tb** (영수증 문구 설정) — 단일행 또는 `pos_cd`(PK), `header_text`, `footer_text`
11. **van_config_tb** (VAN 단말 설정) — `pos_cd`(PK1), `van_code`(PK2, 'NCVAN'/'KICC'), `terminal_id`, `server_ip`, `server_port`
12. **action_log_tb** (관리자 권한 행위 로그: 매출취소/할인 등) — `log_id`(PK, auto), `staff_cd`, `action_type`, `ref_no`, `detail`, `created_at`
13. **system_info_tb** (시스템 정보 표시용, 단일행) — `app_version`, `db_version`, `updated_at`

> 참고: 대/소/POS분류를 legacy와 유사한 `code_tb(code_gbn, code_cd, code_nm)` 범용 코드 테이블 하나로 통합했다(기존 관례와 호환되면서도 이번 범위엔 3개 그룹만 필요해 단순함).

## 9. 확인된 열린 항목 (구현 중 확정)

- ESC/POS 프린터 정확한 모델/명령어셋: 실물 확인 후 확정
- 보류(홀드) UI의 정확한 배치: 스크린샷 대조하며 최소 침습적으로 확정
- VAN 연동 상세 파라미터(가맹점번호, 단말기번호 등): 실제 계약정보 확보 후 `van_config_tb`에 입력

## 10. 다음 단계

이 설계를 기준으로 `writing-plans` 스킬을 통해 구현 계획(단계별 작업 분해: DB 스키마 생성 → WPF 프로젝트 골격/MVVM 인프라 → 로그인·메뉴 → 판매 화면(장바구니+보류) → 결제 연동(NCVAN/KICC) → 프린터/돈통 → 재고관리/상품등록 → 매출관리 → 환경설정 하위화면)을 작성한다.
