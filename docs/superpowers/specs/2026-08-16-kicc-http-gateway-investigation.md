# KICC 카드결제 재조사 — 단말기승인(부착형) → PC결제(EasyCard2 HTTP) 전환

- 작성일: 2026-08-16
- 상태: 조사 완료, 코드 반영 완료. 실제 카드로 최종 승인 테스트는 진행 중.
- 관련 문서: [[kicc-real-payment-design]] (2026-07-24, 원래 단말기승인 부착형 설계)

## 1. 배경

`docs/superpowers/specs/2026-07-24-kicc-real-payment-design.md`에서 설계한 대로 `KiccPos.dll`(`KLoad`/`KReqCmd`/`KGetEvent`) P/Invoke 연동을 실제 매장 포스기기(Windows 7 Embedded, COM6)에 배포해 테스트했으나, 여러 단계에서 실패했다. 원인을 추적하는 과정에서 **이 매장은 애초에 "단말기승인(부착형)" 방식이 아니라 "PC결제(IP기반)" 방식으로 운영 중**이라는 사실이 드러났고, 최종적으로 카드결제 연동을 EasyCard2의 로컬 HTTP 인터페이스를 호출하는 방식으로 교체했다.

## 2. 발견 타임라인

### 2.1 앱이 창도 못 띄우고 멈춤 (exit code 0, CPU 0%로 무한 대기)

- `Kicc.UseRealGateway=true`로 켜면 `FishingMartPos.exe`가 프로세스는 뜨지만 창이 안 나타남.
- 처음엔 KICC 실연동을 창을 띄우기 *전에* `await`했는데, WPF의 `OnStartup`이 async라 첫 `await` 지점에서 제어가 반환되고 그 시점에 열린 창이 없으면 `ShutdownMode` 기본값 때문에 조용히 종료될 수 있다는 가설 → `ShutdownMode.OnExplicitShutdown` 임시 적용으로 수정했으나 재현됨.
- `startup-log.txt`(단계별 파일 로깅 추가)로 확인한 결과, 매번 정확히 `"KiccPosClient 생성 완료, ConnectAsync(KLoad) 호출 직전"`에서 멈춤. Process Monitor로 확인하니 `User Time`이 수십 초간 전혀 안 올라감(진짜 데드락, JIT 지연 아님) — **`KLoad` 호출이 스레드풀 스레드를 새로 만드는 시점에 네이티브 DLL의 스레드 초기화 코드와 충돌하는 로더 락(loader lock) 계열 데드락**으로 추정.
- **조치**: KICC 네이티브 호출 전용 스레드를 앱 수명 동안 하나만 만들어 재사용(`KiccPosClient` 내부에 `BlockingCollection` 기반 워커 스레드 도입). KICC 연결 시도도 `MainWindow.Show()` *이후* 백그라운드로 이동. `KLoad` 자체에도 10초 타임아웃 추가(응답 없으면 "연결 실패"로 간주하고 앱은 정상 기동).

### 2.2 KLoad는 성공하는데 KReqCmd에 단말기가 완전히 무응답

- 위 수정 후 로그인 화면은 정상적으로 뜨고 `KLoad`도 0.5초 만에 성공(`connected=True`).
- 카드결제 버튼을 눌러 `KReqCmd`(카드승인 요청, `CMD=0xFB GCD=0x14 JCD=0x04`)를 호출하면 `ret=0`(성공)이 즉시 반환되지만, 그 뒤 `KGetEvent`를 60초(600회) 폴링해도 단말기로부터 응답이 전혀 없음. **카드단말기에서 소리(삐릭)조차 나지 않음** — 요청이 물리적으로 단말기에 전달조차 안 되고 있다는 신호.
- 설계 문서에 미리 남겨둔 메모(`S00 전문버전, 002가 거부되면 001로 낮출 것`)에 따라 `001`로 낮춰봤으나 동일하게 무응답.

### 2.3 결정적 단서 — EasyCard2 환경설정 화면

포스기기에 이미 설치되어 있던 **EasyCard2**(KICC 정품 카드결제 프로그램)의 환경설정 화면을 확인한 결과:

- **"단말기연결"이 "사용안함"으로 체크**되어 있음.
- 대신 **"신용 IP: 203.233.72.21", "신용 PORT: 15700"** 이 설정되어 있음.
- 즉 이 매장은 **"PC결제(IP기반)"** 방식으로 운영 중이며, `KiccPos.dll`이 전제하는 "단말기승인(부착형, 지능형 단말기가 카드읽기·PIN입력·VAN통신을 전부 자체 처리)" 방식 자체를 쓰지 않는다.
- COM6에 연결된 장치는 `EzMSR`("Easy Magnetic Stripe Reader") — **카드 마그네틱 정보만 읽는 단순 리더기**이며, 화면·PIN패드·VAN 통신 로직이 없다. 애초에 `KReqCmd` 프로토콜에 응답할 능력이 없는 하드웨어였다.

### 2.4 `Kicc.dll` 익스포트 함수 확인 (공식 문서 없어 보류)

- EasyCard2가 실제로 사용하는 `Kicc.dll`(753,664 bytes, `KiccPos.dll`과는 다른 버전/DLL)의 익스포트 함수를 PE 헤더에서 직접 추출.
- `Kicc_Approval`, `Kicc_Approval_TCP5`, `Kicc_RollBack`, `Kicc_GetShopInfo`, `Kicc_UpShopInfo`, `Kicc_SendSign` 등 PC결제용 함수 세트 확인.
- `EasyCard.exe`는 .NET이 아닌 네이티브 실행파일이라 디컴파일로 정확한 파라미터 시그니처를 알아낼 수 없었고, 공식 문서도 기기 내 어디에도 없었음(`kiccDSC`, `Program Files` 전체를 검색했으나 매뉴얼/샘플코드 없음). 금전 처리 함수를 파라미터 추측으로 P/Invoke 구현하는 것은 위험하다고 판단해 보류.

### 2.5 실제 운영 로그에서 프로토콜 역공학 성공

- `C:\Program Files\Kicc\EasyCard2\Log\Slog*.txt` — EasyCard2가 몇 년간 실제 운영하며 남긴 거래 로그. 여기에 요청/응답 전문이 그대로 남아있었음.
- 로그 상단에 `[HTTP ...][Recv Msg][callback=jsonp...&REQ=D1^^...]` 형태가 반복 확인됨 → **EasyCard2가 로컬에 HTTP 서버를 띄워 JSONP 스타일로 요청을 받고 있다는 것**을 발견.
- `Setup.ini`의 `[KICC] HTTPPORT=8080` 확인, `netstat`으로 `127.0.0.1:8080 LISTENING (PID = EasyCard.exe)` 실제 검증.
- 승인 성공/거절 사례를 여러 건 대조해 요청·응답 필드를 특정했다(3절 참고).

## 3. 결론

이 매장은 설계 문서가 전제한 "단말기승인(부착형)"이 아니라 **"PC결제(IP기반), EasyCard2 경유"** 방식으로 이미 운영 중이었다. `KiccPos.dll` 코드 자체는 문제가 없었으나(전용 스레드/타임아웃까지 다 고쳤음),애초에 이 매장 하드웨어로는 응답받을 수 없는 프로토콜이었다.

## 4. 코드 반영 내용

### 4.1 신규 — `Services/Kicc/KiccHttpVanPaymentGateway.cs`

카드 승인(D1)/취소(D4)를 `http://127.0.0.1:{Kicc:HttpPort}/`(기본 8080)로 GET 요청을 보내 처리. `IVanPaymentGateway` 구현체로, `App.xaml.cs`에서 `KiccUseRealGateway=true`일 때 `KiccVanPaymentGateway`(네이티브) 대신 이걸 사용하도록 배선.

### 4.2 유지 — `Services/Kicc/KiccPosClient.cs` (네이티브 P/Invoke)

현금영수증(`KiccCashReceiptGateway`)·서명(`KiccSignatureConverter`)·돈통열기는 여전히 이 경로를 쓴다(이 기능들이 실제로 EasyCard2/HTTP로 커버되는지 미확인이라 건드리지 않음). 전용 워커 스레드 + 10초 타임아웃 수정은 그대로 남아있다.

### 4.3 설정 추가 — `Configuration/AppConfig.cs`, `appsettings.json`

`Kicc:HttpPort`(기본 8080) 추가.

## 5. EasyCard2 HTTP API 스펙 (비공식, 실 운영 로그 역추적 — 공식 문서 아님)

### 요청 (승인, D1)

```
GET http://127.0.0.1:8080/?callback={임의문자열}&REQ=D1^^{금액}^{할부(2자리,00=일시불)}^^^^{거래번호10자리}^WEB{거래번호10자리}^^{가맹점TID}^40^A^^
```

### 요청 (취소, D4)

```
REQ=D4^^{금액}^{할부}^{원거래일자YYMMDD}^{원승인번호}^^{거래번호}^^^{TID}^30
```

### 응답 (JSONP, 작은따옴표 사용 — 표준 JSON 아님)

```
{callback}({'SUC':'00', 'RQ01':'D1', ..., 'RS01':'P','RS02':'A','RS03':'0128','RS04':'0000','RS05':'016','RS07':'...','RS09':'30052334','RS12':'KB국민카드','RS18':'Y','RS19':'3160326930', ...})
```

| 필드 | 의미 |
|---|---|
| `SUC` | `'00'`=처리됨, `'01'`=사용자취소/무효요청 |
| `RS18` | **`'Y'`=승인, `'N'`=거절** (최종 성공여부 판단 필드) |
| `RS09` | 실제 카드 승인번호 |
| `RS04` | 응답코드(`'0000'`=정상, 그 외는 거절코드, 예: `'8003'`=IC카드 ARQC 검증오류) |
| `RS12` | 카드사명(한글) |
| `RS16` | 거절 사유 메시지(실패 시) |
| `RS19` | 가맹점 사업자번호(우리 `merchant.BusinessNo`와 일치 확인됨) |

카드 읽기·PIN입력·서명은 전부 EasyCard2가 자체 UI로 처리하며, 우리 앱은 요청을 보내고 결과를 기다리기만 하면 된다.

## 6. 원래(단말기승인 부착형) 방식으로 되돌리려면 필요한 것

1. **실제 지능형 카드단말기 하드웨어** — 현재 연결된 `EzMSR`는 단순 리더기라 구조적으로 이 프로토콜에 응답 불가. (사용자 확인: 이 매장 TID는 단말기승인 방식으로도 이미 등록되어 있을 수 있음 — 하드웨어만 확보하면 될 가능성 높음)
2. 단말기 자체 설정 화면에서 COM포트/전송속도를 `appsettings.json`의 `Kicc:ComPort`/`BaudRate`와 일치시킬 것.
3. 실물 연결 후 `S00`(전문버전 001/002), 가맹점 TID 매핑 등 미세 조정이 필요할 수 있음(이미 한 번 겪어봐서 빠르게 대응 가능).
4. `KiccPosClient`/`KiccVanPaymentGateway`(네이티브) 코드는 삭제하지 않고 그대로 보존되어 있어, 단말기 확보 시 `IVanPaymentGateway` 배선만 다시 바꾸면 재사용 가능.

## 7. 관련 파일

- `src/FishingMartPos/Services/Kicc/KiccHttpVanPaymentGateway.cs` (신규)
- `src/FishingMartPos/Services/Kicc/KiccPosClient.cs` (전용 스레드/타임아웃 수정, 보존)
- `src/FishingMartPos/Services/Kicc/KiccVanPaymentGateway.cs` (네이티브 구현체, 현재 미사용이지만 보존)
- `src/FishingMartPos/App.xaml.cs` (배선 변경, 백그라운드 초기화)
- `src/FishingMartPos/Configuration/AppConfig.cs`, `appsettings.json` (`Kicc:HttpPort` 추가)
