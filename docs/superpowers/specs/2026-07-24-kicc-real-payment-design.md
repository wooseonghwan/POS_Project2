# KICC 실제 카드결제 연동 설계

## 배경

`docs/superpowers/specs/2026-07-23-card-payment-van-stub-design.md`에서 카드결제1/카드결제2를 `IVanPaymentGateway` 추상화 뒤에 스텁(`StubVanPaymentGateway`, 승인율 85% 시뮬레이션)으로 구현했다. 그 문서는 "실제 KICC 로컬 에이전트 API 문서를 확보하지 못했다"는 것을 명시적 범위 제외 사유로 남겼는데, 이번에 사용자가 다음 자료를 직접 전달했다:

- KICC 공식 전문(protocol) 스펙 PDF(`ED-721_POS연동인터페이스SPEC_일반버전_P00XX_260423.pdf`, v5.1) — STX/LEN/CNT/CMD/GCD/JCD/DATA/ETX/CRC 프레이밍의 로우레벨 TCP/RS-232 프로토콜 정의
- KICC 개발지원 ZIP(`단말기승인(부착형)__연동_개발지원_260423.zip`) — 다음을 포함:
  - `모듈/Kicc.dll`, `모듈/KiccPos.dll` — 실제 연동에 쓸 네이티브(x86) DLL. 이 DLL이 STX/CRC 프레이밍을 내부적으로 처리해주므로, **로우레벨 전문 스펙을 직접 구현할 필요가 없다** — DLL의 P/Invoke 함수만 호출하면 된다.
  - `전문스펙/단말기승인연동_일반(신전문)_모듈API_250620.pdf` — `KiccPos.dll`의 실제 함수 시그니처와 승인요청/응답 필드 스펙(권위 있는 문서, 이번 구현의 1차 출처)
  - `예시 프로그램/C#_Sample_단말기승인(신전문)_260317/` — KICC가 제공한 실제 동작하는 C# WinForms 샘플(Form1.cs) — P/Invoke 선언과 실제 호출 패턴의 정답 참고자료
  - `단말기 설정 메뉴얼/단말기_POS연동설정(단말기모드).pdf` — 단말기 뒷면 RS-232 포트(1/2/3) 또는 LAN 연결, POS속도(9600~115200) 설정 화면 실사진
- 가맹점 정보: **대원수산**(사업자번호 316-90-55788, TID 2977338) / **대원낚시마트**(사업자번호 316-03-26930, TID 2977340) — 사용자가 "결제하기1은 대원수산 / 결제하기2는 대원낚시마트"로 명시. 즉 물리적으로 하나의 카드단말기가 **멀티 TID**로 두 사업자 앞으로 승인을 나눠 낼 수 있어야 한다.

**이 개발 PC에는 실제 ED-721 단말기가 연결되어 있지 않다.** 따라서 이번 구현은 전문 생성/파싱 로직을 문서의 실제 샘플 값으로 단위테스트하는 데까지만 검증 가능하고, 실제 카드로 승인이 나는지는 매장에서 하드웨어를 연결한 뒤 확인해야 한다.

## 목표

- 카드결제1 → 대원수산 TID로, 카드결제2 → 대원낚시마트 TID로 실제 KICC 승인 요청을 보낸다.
- 기존 `IVanPaymentGateway` 인터페이스는 그대로 두고, `KiccVanPaymentGateway`라는 새 구현체로 `StubVanPaymentGateway`를 교체한다 — **`PosViewModel`은 이번 작업에서 코드 변경이 전혀 없어야 한다** (그게 스텁 설계 당시 의도한 목적이므로, 실제로 그렇게 되는지가 이 설계의 핵심 성공 기준이다).
- 이 개발 PC처럼 실제 단말기가 없는 환경에서도 앱이 정상 실행되고 기존 스텁으로 계속 테스트할 수 있도록, 설정으로 스텁/실제 구현을 전환할 수 있게 한다.
- 판매화면의 비활성화된 "돈통열기" 버튼을 같은 김에 실제 명령으로 연결한다.

## 아키텍처

### 계층 구조

```
PosViewModel (변경 없음)
  └─ IVanPaymentGateway
       ├─ StubVanPaymentGateway (기존, 유지)
       └─ KiccVanPaymentGateway (신규)
            ├─ KiccMessageBuilder   (순수 함수 — 요청 전문 문자열 생성)
            ├─ KiccResponseParser   (순수 함수 — 응답 전문 문자열 파싱)
            └─ IKiccPosClient
                 └─ KiccPosClient   (KiccPos.dll P/Invoke 래퍼)
```

`KiccMessageBuilder`/`KiccResponseParser`는 문자열만 다루는 순수 함수라 하드웨어 없이 단위테스트 가능하다. `IKiccPosClient`는 네이티브 DLL 호출을 캡슐화해서, `KiccVanPaymentGateway`의 나머지 로직(성공/실패 판정, `VanApprovalResult` 매핑)도 페이크로 테스트할 수 있게 분리한다.

### `IKiccPosClient` / `KiccPosClient` (P/Invoke 래퍼)

`KiccPos.dll` 실제 함수 시그니처 (`단말기승인연동_일반(신전문)_모듈API_250620.pdf` 기준, C# 샘플의 `DllImport` 선언과 일치 확인됨):

```csharp
[DllImport("KiccPos.dll", EntryPoint = "KLoad", CharSet = CharSet.Ansi)]
private static extern int KLoad(int pPort, int pBaud, byte[] pErrMsg);

[DllImport("KiccPos.dll", EntryPoint = "KUnLoad", CharSet = CharSet.Ansi)]
private static extern void KUnLoad();

[DllImport("KiccPos.dll", EntryPoint = "KReqCmd", CharSet = CharSet.Ansi)]
private static extern int KReqCmd(int CMD, int GCD, int JCD, String SendData, byte[] ErrMsg);

[DllImport("KiccPos.dll", EntryPoint = "KGetEvent", CharSet = CharSet.Ansi)]
private static extern int KGetEvent(ref int CMD, ref int GCD, ref int JCD, ref int RCD, byte[] RData, byte[] RHexData);
```

- **`KLoad(port, baud, errMsg)`**: 모듈을 로드하고 포트를 연다. `port`는 실제 RS-232 COM 포트 번호(정수, 예: 3 → COM3), `baud`는 9600/19200/38400/57600/115200 중 하나(단말기 자체의 "부가장치 > POS연동설정" 화면에 설정된 값과 일치해야 함, 사진상 매장 예시는 57600). 성공 0, 실패 -1.
- **`KUnLoad()`**: 앱 종료 전 반드시 호출.
- **`KReqCmd(cmd, gcd, jcd, sendData, errMsg)`**: 단말기에 명령 전송. **블로킹 호출** — 승인요청의 경우 카드 삽입/PIN 입력을 사람이 하는 동안 수 초~수십 초 걸릴 수 있다. 반환값: `0`=성공, `-1`=명령처리실패, `-2`=TimeOut, `-3`=사용자취소(단말기 화면에서 취소 버튼).
- **`KGetEvent(ref cmd, ref gcd, ref jcd, ref rcd, rData, rHexData)`**: `KReqCmd` 완료 후(혹은 완료와 거의 동시에) 폴링해서 실제 응답 데이터(`rData`, 세미콜론 KEY=VALUE 문자열)를 가져온다. 반환값이 양수면 `rData` 길이(성공), `-1`이면 자료 없음. `rcd`(응답코드)는 `0x00`=성공, `0xFF`=실패, `0xFA`=BUSY, `0xF9`=INVALID_COMMAND, `0xF8`=INVALID_DATA.

**호출 패턴** (C# 샘플 `Form1.cs`의 `But_Send_Click`/`timer1_Tick` 참고, 우리는 타이머 폴링 대신 `KReqCmd` 완료 직후 짧은 재시도 루프로 대체한다):

```csharp
public async Task<KiccRawResponse> RequestAsync(int cmd, int gcd, int jcd, string sendData)
{
    var errMsg = new byte[4096];
    int ret = await Task.Run(() => KReqCmd(cmd, gcd, jcd, sendData, errMsg));
    // ret: 0=성공, -1=명령처리실패, -2=TimeOut, -3=사용자취소

    if (ret != 0)
        return KiccRawResponse.FromReqCmdFailure(ret, Encoding.Default.GetString(errMsg));

    // KReqCmd가 0을 반환한 뒤 응답 데이터를 폴링으로 가져온다(짧은 지연 후 즉시 사용 가능한 것이 보통이나,
    // 안전하게 최대 N회/짧은 간격으로 재시도).
    for (int attempt = 0; attempt < MaxPollAttempts; attempt++)
    {
        int c = 0, g = 0, j = 0, rcd = 0;
        var rData = new byte[2048];
        var rHex = new byte[4096];
        int len = KGetEvent(ref c, ref g, ref j, ref rcd, rData, rHex);
        if (len > 0)
            return KiccRawResponse.FromEvent(rcd, Encoding.Default.GetString(rData).TrimEnd('\0'));
        await Task.Delay(PollIntervalMs);
    }
    return KiccRawResponse.Timeout();
}
```

`Task.Run`으로 감싸는 이유: `KReqCmd`가 네이티브 블로킹 호출이라 그대로 호출하면 UI 스레드(혹은 호출 스레드)가 카드 승인 대기 시간만큼 멈춘다. `PosViewModel.PayCardAsync`는 이미 `await`로 비동기 흐름을 타고 있으므로(스텁 설계 때 만든 구조), `KiccVanPaymentGateway.RequestApprovalAsync`가 이 블로킹 호출을 `Task.Run`으로 감싸주기만 하면 위 스택 전체가 자연스럽게 논블로킹이 된다.

### `KiccMessageBuilder` (요청 전문 생성)

승인 요청(`0xFB`/`0x14`/`0x04`)의 `SendData`는 `"S00=...;S01=...;...;"` 형식의 세미콜론 구분 KEY=VALUE 문자열이다. 모듈 API 문서의 실제 예시(`"S01=D1;S02=40;S09=00;S10=1004;S15=0;S16=91;"`)와 필드 표를 기준으로 아래 필드만 채운다(그 외 필드는 "사용안함"이거나 이번 범위 밖):

| 필드 | 값 | 비고 |
|---|---|---|
| S00 | `"002"` | 전문버전 — 신규개발 권장값(전문 스펙 3.5절). 실제 하드웨어 테스트 시 단말기가 이 값을 거부하면 `"001"`로 낮추는 것이 유일한 수정 지점이 되도록 상수 하나로 관리한다. |
| S01 | `"D1"` | 신용승인. 취소(`D4`)/현금(`B1`,`B2`) 등은 이번 범위 밖 — [[card-payment-van-stub-design]]과 동일하게 승인만 다룬다. |
| S02 | `"40"` | 일반 단말구분. |
| S03 | 가맹점 TID | 카드결제1=`"2977338"`(대원수산), 카드결제2=`"2977340"`(대원낚시마트). 모듈 API 문서: "(멀티 TID 지원 단말기 사용시)". |
| S04 | 가맹점 사업자번호(대시 제거) | 카드결제1=`"3169055788"`, 카드결제2=`"3160326930"`. |
| S09 | `"00"`(일시불) 또는 선택한 할부개월의 2자리 문자열 | **2026-07-26 갱신: 할부개월 선택 기능이 실제로 추가되었다** (`docs/superpowers/specs/2026-07-26-payment-popup-design.md`). `installmentMonths.ToString("00")`로 채운다(0→`"00"`, 2→`"02"` 등) — 이 인코딩은 모듈 API 문서 필드 설명과 국내 VAN 표준 관례에 근거한 추정이며, 실물 단말기로 미검증 상태다. |
| S10 | 거래 금액(정수 문자열) | 예: `"5000"`. |
| S15 | `"0"` | 봉사료 없음. |
| S16 | 부가세(정수 문자열, 반올림) | `Math.Round(amount / 11m, MidpointRounding.AwayFromZero)` — 부가세 포함가 기준 역산(한국 부가세 10% 내포 방식). 샘플 예시(`S10=1004`일 때 `S16=91`)로 검증: `1004/11 = 91.27... → 91`, 일치. |
| S23 | POS 거래 고유번호(최대 20byte) | `PosCode + yyMMddHHmmss + 2자리 랜덤` 조합으로 20byte 이내 유니크 값 생성. 단말기가 응답전문에 그대로 리턴하므로 결과 확인/로그 상관관계에 쓸 수 있다(당장은 사용하지 않고 필드만 채워 넣는다). |

```csharp
public static string BuildApprovalRequest(KiccMerchantConfig merchant, decimal amount, string posTranNo)
{
    var vat = Math.Round(amount / 11m, MidpointRounding.AwayFromZero);
    return $"S00=002;S01=D1;S02=40;S03={merchant.Tid};S04={merchant.BusinessNo};" +
           $"S09=00;S10={(int)amount};S15=0;S16={(int)vat};S23={posTranNo};";
}
```

### `KiccResponseParser` (응답 전문 파싱)

응답은 요청 필드들을 그대로 에코(`S01=...;S02=...;...`)한 뒤 응답 필드(`R01=...;...;R23=...;`)가 이어붙는 하나의 세미콜론 문자열이다(모듈 API 문서 예시 CAT 응답전문 참고). C# 샘플의 `parseData()`가 하는 것과 동일하게 `key=value` 쌍을 파싱해 사전으로 만든 뒤 `R04`(응답코드, `"0000"`=성공)를 승인 여부 판정에 쓴다.

```csharp
public static IReadOnlyDictionary<string, string> Parse(string rdata)
{
    var result = new Dictionary<string, string>();
    foreach (var pair in rdata.Split(';', StringSplitOptions.RemoveEmptyEntries))
    {
        int eq = pair.IndexOf('=');
        if (eq <= 0) continue;
        result[pair[..eq]] = pair[(eq + 1)..];
    }
    return result;
}
```

승인 성공 시 필요한 필드: `R04`(`"0000"`이면 성공), `R09`(승인번호), `R07`(승인일시, `YYMMDDhhmmssN` 형식 — N은 요일 0~6이라 그대로 저장하지 않고 우리 쪽 `DateTime.Now`를 계속 쓴다, KICC 승인번호 문자열만 `VanApprovalNo`로 저장).

`KGetEvent`의 `rcd`가 `0xFF`(실패)일 때의 RDATA는 `응답코드(1)+거절코드(4)+메시지(V)` 형식(요청 필드 에코 없음). 거절코드 매핑:

| 거절코드 | 의미 | 우리 쪽 표시 문구 |
|---|---|---|
| `9999` | KEY CANCEL | `"고객이 결제를 취소했습니다"` |
| `9998` | 카드빠짐 | `"카드를 다시 삽입해주세요"` |
| `9997` | POS수용버전에러/전문포맷에러 | `"결제 요청 형식 오류"` |
| `9996` | LAN 통신 에러 | `"단말기 통신 오류"` |

메시지 필드(`"CANCELED"`/`"TIMEOUT"`/`"FAIL"`)도 같이 오는데, 거절코드가 있으면 거절코드 매핑을 우선하고 없으면 메시지 필드로 폴백한다.

`KReqCmd` 자체가 `-2`(TimeOut)/`-3`(사용자취소)를 반환한 경우는 `KGetEvent` 폴링 없이 바로 `"응답 시간 초과"`/`"고객이 결제를 취소했습니다"`로 매핑한다(둘 다 응답 전문 자체가 없는 상태이므로).

### `KiccVanPaymentGateway : IVanPaymentGateway`

```csharp
public sealed class KiccVanPaymentGateway : IVanPaymentGateway
{
    private readonly IKiccPosClient _client;
    private readonly IReadOnlyDictionary<string, KiccMerchantConfig> _merchantsByPayType; // "CARD1"/"CARD2" -> config

    public async Task<VanApprovalResult> RequestApprovalAsync(VanApprovalRequest request)
    {
        var merchant = _merchantsByPayType[request.PayType]; // CARD1/CARD2 외 PayType(예: CASH)이 오면 프로그램 오류이므로 그대로 예외
        var posTranNo = BuildPosTranNo(request.PosCode);
        var sendData = KiccMessageBuilder.BuildApprovalRequest(merchant, request.Amount, posTranNo);

        var raw = await _client.RequestAsync(0xFB, 0x14, 0x04, sendData);

        if (!raw.IsSuccess)
            return new VanApprovalResult { IsApproved = false, ResponseMessage = raw.FailureMessage };

        var fields = KiccResponseParser.Parse(raw.Data);
        if (fields.TryGetValue("R04", out var rc) && rc == "0000")
        {
            return new VanApprovalResult
            {
                IsApproved = true,
                ApprovalNo = fields.GetValueOrDefault("R09"),
                VanCode = "KICC",
                ResponseMessage = "카드 결제 완료",
            };
        }

        return new VanApprovalResult { IsApproved = false, ResponseMessage = "카드 승인 거절" };
    }
}
```

`VanApprovalRequest.PayType`("CARD1"/"CARD2")으로 `_merchantsByPayType`에서 TID/사업자번호를 찾는 구조라, 카드결제1/2가 서로 다른 가맹점으로 승인 나가는 요구사항이 `PosViewModel` 변경 없이 게이트웨이 내부에서만 처리된다.

### 가맹점 설정 저장: `van_config_tb` 스키마 변경

현재 스키마(`db/migrations/001_create_schema.sql`):
```sql
CREATE TABLE IF NOT EXISTS van_config_tb (
    pos_cd      VARCHAR(4)      NOT NULL,
    van_code    VARCHAR(10)     NOT NULL,
    terminal_id VARCHAR(30)     NULL,
    server_ip   VARCHAR(50)     NULL,
    server_port INT             NULL,
    PRIMARY KEY (pos_cd, van_code)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
```

`(pos_cd, van_code)`만으로는 카드결제1/2가 서로 다른 TID/사업자번호를 갖는 걸 표현 못 한다(하나의 물리 단말기, 같은 `van_code`='KICC'). `db/migrations/004_van_config_pay_type.sql`로 `pay_type` 컬럼을 추가해 PK를 `(pos_cd, van_code, pay_type)`으로 바꾸고, `business_no` 컬럼을 추가한다(`terminal_id`는 그대로 TID 용도로 재사용). `server_ip`/`server_port`는 이번 RS-232 연결에서는 안 쓰지만 스키마에는 남겨둔다(향후 LAN 모드 전환 시 재사용 가능, 지금 굳이 뺄 이유 없음).

```sql
ALTER TABLE van_config_tb
    ADD COLUMN pay_type VARCHAR(10) NOT NULL DEFAULT 'CARD1' AFTER van_code,
    ADD COLUMN business_no VARCHAR(10) NULL AFTER terminal_id,
    DROP PRIMARY KEY,
    ADD PRIMARY KEY (pos_cd, van_code, pay_type);

INSERT INTO van_config_tb (pos_cd, van_code, pay_type, terminal_id, business_no) VALUES
    ('1', 'KICC', 'CARD1', '2977338', '3169055788'),
    ('1', 'KICC', 'CARD2', '2977340', '3160326930')
ON DUPLICATE KEY UPDATE terminal_id = VALUES(terminal_id), business_no = VALUES(business_no);
```

(`pos_cd`='1'만 시드 — 현재 시드된 단말기가 POS1 하나뿐이라는 기존 컨벤션을 따름. POS2용 데이터는 실제 두 번째 매장 PC 설치 시 관리자가 직접 입력하거나 별도로 시드한다. 이번 범위에서 "환경설정에서 van_config_tb 편집하는 화면"은 만들지 않는다 — 프린터설정처럼 화면으로 노출하는 건 다음 범위로 미룬다. 지금은 DB 시드값과 앱 재시작만으로 충분하다.)

`IVanConfigRepository`(신규) — `GetByPosCodeAsync(string posCd) -> IReadOnlyList<VanConfigRow>` 하나만 있으면 된다. `App.xaml.cs`가 시작 시 이 리포지토리로 두 행(CARD1/CARD2)을 읽어 `KiccMerchantConfig` 딕셔너리를 만들어 `KiccVanPaymentGateway`에 주입한다.

### 스텁/실제 전환: `appsettings.json`

```json
{
  "Database": { "ConnectionString": "..." },
  "Terminal": { "PosCode": "1" },
  "Kicc": {
    "UseRealGateway": false,
    "ComPort": 3,
    "BaudRate": 57600
  }
}
```

`AppConfig`에 `Kicc.UseRealGateway`(bool)/`Kicc.ComPort`(int)/`Kicc.BaudRate`(int) 추가. `App.xaml.cs`의 DI 등록에서:

```csharp
if (config.Kicc.UseRealGateway)
{
    services.AddSingleton<IKiccPosClient>(_ => new KiccPosClient(config.Kicc.ComPort, config.Kicc.BaudRate));
    services.AddSingleton<IVanPaymentGateway>(sp => new KiccVanPaymentGateway(sp.GetRequiredService<IKiccPosClient>(), merchantsByPayType));
}
else
{
    services.AddSingleton<IVanOutcomeProvider, RandomVanOutcomeProvider>();
    services.AddSingleton<IVanPaymentGateway, StubVanPaymentGateway>();
}
```

`appsettings.json`(저장소 커밋본)의 기본값은 `false`(스텁) — 이 개발 PC 및 앞으로 이 리포지토리를 체크아웃하는 누구나 하드웨어 없이 기존처럼 동작한다. 매장 PC에는 `appsettings.Local.json`(git-ignore 대상, 기존에 DB 연결 문자열도 이 파일로 오버라이드하는 패턴을 그대로 따름)에 `"Kicc": { "UseRealGateway": true, "ComPort": <실제값>, "BaudRate": <실제값> }`를 넣어 배포 시점에 켠다.

### 연결 수명주기

`KLoad`는 앱 시작 시 1회(`UseRealGateway=true`일 때만), `KUnLoad`는 앱 종료 시 1회 호출한다. `App.xaml.cs`의 `OnStartup`에서 `KiccPosClient.ConnectAsync()`(내부에서 `Task.Run(() => KLoad(...))`) 실패 시 — **앱을 크래시시키지 않는다.** 카드결제만 실패하고 현금결제는 계속 동작해야 하는 소형 매장 특성상, 연결 실패는 로그만 남기고(이번 로깅 작업과 자연히 맞물림) 앱은 정상 기동한다. 이후 카드결제 버튼을 누르면 `KReqCmd` 자체가 포트 미연결로 실패를 반환할 것이므로, 그 시점에 기존 거절 토스트 흐름(스텁 때 이미 만든 경로)으로 자연스럽게 안내된다.

### 돈통열기 연결

`PosView.xaml:246`의 `<Button Content="돈통열기" IsEnabled="False" ... />`(현재 `Command` 바인딩 자체가 없음)를 `PosViewModel`에 새 `[RelayCommand] OpenCashDrawer` 추가해서 연결한다. `IKiccPosClient.RequestAsync(0xFB, 0x14, 0x0B, "")`를 직접 호출(승인 요청과 달리 `KiccMessageBuilder`/`KiccResponseParser` 안 거침 — SendData가 `none`이고 응답도 딱히 파싱할 필드가 없음, 성공/실패만 확인). `IVanPaymentGateway`가 아니라 별도로 `PosViewModel`에 `IKiccPosClient?`를 주입해야 하는데, 스텁 모드(`UseRealGateway=false`)에서는 `IKiccPosClient`가 DI에 없으므로 **`IKiccPosClient`를 nullable로 주입하고, null이면 버튼을 눌러도 "지원 예정" 경고 토스트만 띄운다**(기존 프린터설정 화면의 "테스트인쇄는 지원 예정 안내만" 패턴과 동일). `CanPay`(카드결제 처리중 가드)와는 무관하게 항상 클릭 가능하되, 카드결제 처리 중에는 같이 눌리지 않게 `CanPay` 조건을 재사용한다(물리적으로 같은 단말기와 통신하므로 동시 요청을 보내면 안 됨).

## 새로 생기는 파일

- `src/FishingMartPos/Native/Kicc.dll`, `src/FishingMartPos/Native/KiccPos.dll` — 빌드 출력에 복사(`CopyToOutputDirectory=PreserveNewest`), x86 전용(기존 프로젝트가 이미 x86)
- `src/FishingMartPos/Services/Kicc/IKiccPosClient.cs`, `KiccPosClient.cs`, `KiccRawResponse.cs`
- `src/FishingMartPos/Services/Kicc/KiccMessageBuilder.cs`, `KiccResponseParser.cs`
- `src/FishingMartPos/Services/Kicc/KiccVanPaymentGateway.cs`, `KiccMerchantConfig.cs`
- `src/FishingMartPos/Repositories/IVanConfigRepository.cs`, `VanConfigRepository.cs`
- `src/FishingMartPos/Models/VanConfigRow.cs`
- `db/migrations/004_van_config_pay_type.sql`
- 단위테스트: `tests/FishingMartPos.Tests/Services/Kicc/KiccMessageBuilderTests.cs`, `KiccResponseParserTests.cs`(문서의 실제 샘플 문자열을 픽스처로 사용), `KiccVanPaymentGatewayTests.cs`(가짜 `IKiccPosClient`로 승인/거절/타임아웃/사용자취소 경로)
- `tests/FishingMartPos.Tests/Repositories/VanConfigRepositoryTests.cs`

## 수정되는 기존 파일

- `db/migrations/001_create_schema.sql`은 건드리지 않는다(이미 배포된 스키마 파일 수정 금지 원칙 — 새 마이그레이션 파일로만 변경)
- `src/FishingMartPos/Configuration/AppConfig.cs` — `Kicc` 섹션(`UseRealGateway`/`ComPort`/`BaudRate`) 추가
- `appsettings.json` — `Kicc.UseRealGateway: false` 기본값 추가
- `src/FishingMartPos/App.xaml.cs` — 스텁/실제 분기 DI 등록, `KLoad`/`KUnLoad` 수명주기(`OnStartup`/`OnExit`)
- `src/FishingMartPos/ViewModels/PosViewModel.cs` — **`PayCardAsync` 등 결제 로직은 무변경.** `OpenCashDrawer` 커맨드만 추가(nullable `IKiccPosClient` 생성자 파라미터 추가 필요 — 유일한 생성자 시그니처 변경. [[card-payment-van-stub-design]] 때 배운 교훈대로, 생성자를 직접 호출하는 다른 곳(`PosViewModelTests.cs`, `PosViewModelPaymentTests.cs`, `MainMenuViewModelTests.cs`, `LoginViewModelTests.cs`, `App.xaml.cs`)을 계획 수립 시점에 전부 찾아 계획에 포함시킨다 — 지난번처럼 빠뜨려서 Task 중간에 멈추는 일을 반복하지 않는다.)
- `src/FishingMartPos/Views/PosView.xaml:246` — 돈통열기 버튼에 `Command="{Binding OpenCashDrawerCommand}"` 추가

## 테스트 계획

- `KiccMessageBuilderTests`: 문서 예시(`S10=1004`일 때 `S16=91`)로 부가세 계산 검증, TID/사업자번호가 올바른 필드에 들어가는지, 카드결제1/2가 다른 가맹점 값을 만드는지
- `KiccResponseParserTests`: 모듈 API 문서의 실제 CAT 응답전문 예시 문자열을 그대로 픽스처로 사용해 `R04`/`R09` 등이 정확히 파싱되는지, 실패 응답(응답코드+거절코드+메시지 형식) 파싱
- `KiccVanPaymentGatewayTests`: 가짜 `IKiccPosClient`로 (1) 승인 성공 시 `VanApprovalNo`/`VanCode="KICC"` 채워짐, (2) `R04≠"0000"` 시 거절, (3) `KReqCmd` 반환값 `-2`(TimeOut)/`-3`(사용자취소) 시 각각의 안내 문구, (4) 카드결제1/2가 실제로 다른 `S03`/`S04`로 요청 생성하는지(TID 스푸핑 방지 확인 — 잘못 매핑되면 매출이 엉뚱한 사업자 앞으로 잡히는 심각한 버그이므로 반드시 테스트)
- `VanConfigRepositoryTests`: 시드 데이터 조회, `pay_type`별 분리 조회
- `IKiccPosClient`/`KiccPosClient`(P/Invoke 자체)는 실제 하드웨어 없이 유닛테스트 불가 — 이번 범위에서는 테스트하지 않고, 매장에서 하드웨어 연결 후 수동 확인 대상으로 남긴다(테스트 계획에 명시적으로 이 갭을 기록해 나중에 잊지 않게 한다)

## 이번 범위에 포함하지 않는 것

- 신용카드 취소(`D4`)/망취소 — 매출취소 기능 자체가 아직 없음(project 메모리 기준 별도 범위)
- 현금영수증(`B1`/`B2`), 제로페이/간편결제(`Z1`/`Z2`, RF Flag) — 이 매장은 카드결제만 요구했음
- 서명 데이터 수신/영수증 프린트(`KReqPrint`, `KGetBmp`, `KSaveToBmp`) — 프린터 연동 자체가 이미 별도 미완료 범위("지원 예정")
- `van_config_tb`를 환경설정 화면에서 편집하는 UI — 이번엔 DB 시드로만 처리, 화면은 다음 범위
- LAN(TCP) 연결 모드 — 이번 매장은 RS-232로 확정, LAN 모드는 스펙 문서에 근거만 남겨두고 미구현
- POS2(두 번째 단말)용 van_config_tb 시드 — 실제 두 번째 매장 PC 설치 시점에 처리
