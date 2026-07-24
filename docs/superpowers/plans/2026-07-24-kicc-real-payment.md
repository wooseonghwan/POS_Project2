# KICC 실제 카드결제 연동 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 카드결제1/카드결제2가 시뮬레이션 스텁(`StubVanPaymentGateway`) 대신 실제 KICC `KiccPos.dll`을 통해 대원수산/대원낚시마트 앞으로 각각 진짜 승인 요청을 보내도록 `IVanPaymentGateway`의 새 구현체 `KiccVanPaymentGateway`를 추가한다.

**Architecture:** `KiccPosClient`(네이티브 DLL P/Invoke 래퍼) → `KiccMessageBuilder`/`KiccResponseParser`(순수 함수, 하드웨어 없이 단위테스트) → `KiccVanPaymentGateway`(`IVanPaymentGateway` 구현, 기존 `PosViewModel`은 무변경). 가맹점(TID/사업자번호) 설정은 `van_config_tb`에 `pay_type` 컬럼을 추가해 저장하고, `appsettings`의 `Kicc:UseRealGateway` 스위치로 스텁/실제 게이트웨이를 전환한다.

**이 개발 PC에는 실제 ED-721 단말기가 연결되어 있지 않다.** `KiccPosClient`(P/Invoke 자체)는 하드웨어 없이 테스트 불가능하므로 이번 계획에서 유닛테스트 대상이 아니다 — Task 3에서 명시적으로 이 갭을 남긴다. 실제 카드 승인 확인은 매장에서 하드웨어 연결 후 별도로 진행해야 한다.

## Global Constraints

- 승인 요청 Cmd/Gcd/Jcd: `0xFB`/`0x14`/`0x04` (고정값, 스펙 3.5절).
- 돈통 열기 Cmd/Gcd/Jcd: `0xFB`/`0x14`/`0x0B`.
- 승인요청 SendData 형식: `"S00=002;S01=D1;S02=40;S03=<TID>;S04=<사업자번호>;S09=00;S10=<금액>;S15=0;S16=<부가세>;S23=<POS거래번호>;"` — 필드 순서와 값 모두 이 형식 그대로.
- 부가세(S16) 계산: `Math.Round(amount / 11m, MidpointRounding.AwayFromZero)`, 정수로 캐스팅. 문서 예시 `S10=1004` → `S16=91`로 검증됨(`1004/11=91.27...→91`).
- 카드결제1(`PayType="CARD1"`) = **대원수산** = TID(S03) `"2977338"`, 사업자번호(S04) `"3169055788"`.
- 카드결제2(`PayType="CARD2"`) = **대원낚시마트** = TID(S03) `"2977340"`, 사업자번호(S04) `"3160326930"`.
- 응답 파싱: 세미콜론 구분 `KEY=VALUE` 문자열. 승인 성공 판정은 `R04=="0000"`, 승인번호는 `R09`. `VanApprovalResult.VanCode`는 승인 성공 시 정확히 `"KICC"`.
- `KReqCmd` 반환값: `0`=성공(응답은 `KGetEvent`로 추가 폴링), `-1`=명령처리실패, `-2`=TimeOut→`"응답 시간 초과"`, `-3`=사용자취소→`"고객이 결제를 취소했습니다"`.
- `KGetEvent`의 `RCD`: `0x00`=성공, 그 외(`0xFF`/`0xFA`/`0xF9`/`0xF8`)=실패로 취급.
- P/Invoke: `DllImport("KiccPos.dll", CharSet = CharSet.Ansi)`, 엔트리포인트 `KLoad`/`KUnLoad`/`KReqCmd`/`KGetEvent` — 이름과 파라미터 순서는 KICC 제공 C# 샘플(`Form1.cs`)의 선언과 정확히 일치해야 한다.
- 네이티브 블로킹 호출(`KReqCmd`, `KLoad`)은 반드시 `Task.Run`으로 감싼다 — 그대로 호출하면 호출 스레드가 카드 승인 대기(수 초~수십 초) 동안 멈춘다.
- `PosViewModel.cs`의 `PayAsync`/`PayCardAsync`/`BuildDetailLines` 등 기존 결제 로직은 **이번 계획에서 한 줄도 바뀌지 않는다** — 유일한 변경은 생성자에 `IKiccPosClient? kiccPosClient = null`(선택 인자, 기본값 null)을 추가하는 것과 새 `OpenCashDrawer` 커맨드 추가뿐이다.
- 새 생성자 파라미터는 선택 인자(`= null`)로 추가한다 — 기존 5개 호출부(`App.xaml.cs`, `PosViewModelTests.cs`, `PosViewModelPaymentTests.cs`, `MainMenuViewModelTests.cs`, `LoginViewModelTests.cs`) 중 이 값이 필요 없는 4곳은 **손대지 않는다**(선택 인자라 생략 가능 — 지난 카드결제 스텁 작업 때 이 부분을 빠뜨려 Task가 중간에 멈췄던 문제를 이번엔 설계 단계에서 미리 방지).
- 새 리포지토리는 기존 `IDbConnectionFactory.CreateOpenConnectionAsync()`(비동기 오픈, 최근 DB 성능 개선 작업에서 도입됨)를 사용한다 — 옛 동기 `CreateOpenConnection()`을 쓰지 않는다.
- 마이그레이션 파일은 새 파일로만 추가한다(`004_van_config_pay_type.sql`) — 기존 `001_create_schema.sql`은 수정하지 않는다.

---

### Task 1: 네이티브 DLL 배선

**Files:**
- Create(이미 파일 자체는 컨트롤러가 복사해둠, git add만 하면 됨): `src/FishingMartPos/Native/Kicc.dll`, `src/FishingMartPos/Native/KiccPos.dll`
- Modify: `src/FishingMartPos/FishingMartPos.csproj`

**Interfaces:**
- Produces: 빌드 출력 폴더(`bin/x86/Debug/net8.0-windows/`)에 `Kicc.dll`/`KiccPos.dll`이 존재하게 됨 — Task 3의 `KiccPosClient`가 런타임에 `DllImport("KiccPos.dll")`로 찾는 파일.

- [ ] **Step 1: 파일 존재 확인**

`src/FishingMartPos/Native/Kicc.dll`, `src/FishingMartPos/Native/KiccPos.dll` 두 파일이 이미 저장소에 있는지 확인한다(컨트롤러가 이번 계획 수립 전에 KICC 개발지원 ZIP에서 복사해둠).

Run: `ls -la "C:/AI/pos-project2/src/FishingMartPos/Native/"`
Expected: `Kicc.dll`(약 776KB), `KiccPos.dll`(약 11.1MB) 두 파일 존재

파일이 없다면 STOP하고 BLOCKED로 보고한다(바이너리 파일을 직접 만들어낼 방법이 없다 — 컨트롤러의 준비 작업이 빠진 것이므로 임의로 진행하면 안 됨).

- [ ] **Step 2: csproj에 배포 항목 추가**

`src/FishingMartPos/FishingMartPos.csproj`의 기존 `<ItemGroup>`(`appsettings.json`/`appsettings.Local.json` 복사 설정이 있는 블록, 24~31행) 바로 아래에 새 `<ItemGroup>`을 추가:

```xml
  <ItemGroup>
    <None Include="Native\Kicc.dll">
      <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
    </None>
    <None Include="Native\KiccPos.dll">
      <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
    </None>
  </ItemGroup>
```

- [ ] **Step 3: 빌드 확인 및 배포 검증**

Run: `cd "C:/AI/pos-project2" && dotnet build`
Expected: `Build succeeded.` 0경고/0오류

Run: `ls "C:/AI/pos-project2/src/FishingMartPos/bin/x86/Debug/net8.0-windows/" | grep -i kicc`
Expected: `Kicc.dll`, `KiccPos.dll` 둘 다 출력에 나타남(빌드 출력 폴더로 실제 복사됐는지 확인)

- [ ] **Step 4: 커밋**

```bash
git add src/FishingMartPos/Native/Kicc.dll src/FishingMartPos/Native/KiccPos.dll src/FishingMartPos/FishingMartPos.csproj
git commit -m "KICC 네이티브 DLL(Kicc.dll/KiccPos.dll) 프로젝트에 포함 및 빌드 출력 복사 설정"
```

---

### Task 2: `KiccMerchantConfig` / `KiccMessageBuilder` / `KiccResponseParser`

**Files:**
- Create: `src/FishingMartPos/Services/Kicc/KiccMerchantConfig.cs`
- Create: `src/FishingMartPos/Services/Kicc/KiccMessageBuilder.cs`
- Create: `src/FishingMartPos/Services/Kicc/KiccResponseParser.cs`
- Test: `tests/FishingMartPos.Tests/Services/Kicc/KiccMessageBuilderTests.cs`, `tests/FishingMartPos.Tests/Services/Kicc/KiccResponseParserTests.cs`

**Interfaces:**
- Produces: `KiccMerchantConfig(string PayType, string Tid, string BusinessNo)`(record), `KiccMessageBuilder.BuildApprovalRequest(KiccMerchantConfig merchant, decimal amount, string posTranNo): string`, `KiccResponseParser.Parse(string rdata): IReadOnlyDictionary<string, string>` — Task 5(`KiccVanPaymentGateway`)가 이 세 타입/메서드를 그대로 사용한다.

- [ ] **Step 1: 실패하는 테스트 작성**

`tests/FishingMartPos.Tests/Services/Kicc/KiccMessageBuilderTests.cs`:

```csharp
using FishingMartPos.Services.Kicc;
using Xunit;

namespace FishingMartPos.Tests.Services.Kicc;

public class KiccMessageBuilderTests
{
    [Fact]
    public void BuildApprovalRequest_ComputesVatAndFormatsFields_MatchingKiccDocSample()
    {
        var merchant = new KiccMerchantConfig("CARD1", "0788888", "1234567890");

        var sendData = KiccMessageBuilder.BuildApprovalRequest(merchant, 1004m, "POSTRAN123");

        Assert.Equal("S00=002;S01=D1;S02=40;S03=0788888;S04=1234567890;S09=00;S10=1004;S15=0;S16=91;S23=POSTRAN123;", sendData);
    }

    [Fact]
    public void BuildApprovalRequest_Card1_UsesDaewonSusanTidAndBusinessNo()
    {
        var card1 = new KiccMerchantConfig("CARD1", "2977338", "3169055788");

        var sendData = KiccMessageBuilder.BuildApprovalRequest(card1, 5000m, "T1");

        Assert.Contains("S03=2977338;S04=3169055788;", sendData);
    }

    [Fact]
    public void BuildApprovalRequest_Card2_UsesDaewonNakssiMartTidAndBusinessNo()
    {
        var card2 = new KiccMerchantConfig("CARD2", "2977340", "3160326930");

        var sendData = KiccMessageBuilder.BuildApprovalRequest(card2, 5000m, "T2");

        Assert.Contains("S03=2977340;S04=3160326930;", sendData);
    }
}
```

`tests/FishingMartPos.Tests/Services/Kicc/KiccResponseParserTests.cs`:

```csharp
using FishingMartPos.Services.Kicc;
using Xunit;

namespace FishingMartPos.Tests.Services.Kicc;

public class KiccResponseParserTests
{
    // 모듈 API 문서(단말기승인연동_일반(신전문)_모듈API_250620.pdf 4페이지)의 실제 CAT 응답전문 샘플 그대로.
    private const string RealCatApprovalSample =
        "S01=I1;S02=CU;S03=0788888;S04=1168119948;S05=C ;S06=I;S07=6258-04**-****-9022;S09=00;S10=1004;S15=0;S16=91;S21=N;" +
        "R01=P;R02=Q;R03=0010;R04=0000;R05=016;R06=0000;R07=2111301456162;R09=99145616;R11= N;R12=016;R13=KB국민카드;" +
        "R14=00001220713;R15=KB국민카드;R16=d;R18=00000000;R19=TEST용;R20=301441019646;R22=Y;R23=6258-04**-****-9022;";

    [Fact]
    public void Parse_RealCatApprovalResponseSample_ExtractsApprovalFields()
    {
        var fields = KiccResponseParser.Parse(RealCatApprovalSample);

        Assert.Equal("0000", fields["R04"]);
        Assert.Equal("99145616", fields["R09"]);
        Assert.Equal("KB국민카드", fields["R13"]);
    }

    [Fact]
    public void Parse_RealCatApprovalResponseSample_AlsoExtractsEchoedRequestFields()
    {
        var fields = KiccResponseParser.Parse(RealCatApprovalSample);

        Assert.Equal("I1", fields["S01"]);
        Assert.Equal("1004", fields["S10"]);
    }

    [Fact]
    public void Parse_EmptyString_ReturnsEmptyDictionary()
    {
        var fields = KiccResponseParser.Parse("");

        Assert.Empty(fields);
    }
}
```

- [ ] **Step 2: 테스트 실패 확인**

Run: `dotnet test --filter "FullyQualifiedName~Kicc.Kicc"`
Expected: FAIL (컴파일 에러 — `KiccMerchantConfig`/`KiccMessageBuilder`/`KiccResponseParser`가 아직 없음)

- [ ] **Step 3: 구현**

`src/FishingMartPos/Services/Kicc/KiccMerchantConfig.cs`:

```csharp
namespace FishingMartPos.Services.Kicc;

public sealed record KiccMerchantConfig(string PayType, string Tid, string BusinessNo);
```

`src/FishingMartPos/Services/Kicc/KiccMessageBuilder.cs`:

```csharp
namespace FishingMartPos.Services.Kicc;

public static class KiccMessageBuilder
{
    public static string BuildApprovalRequest(KiccMerchantConfig merchant, decimal amount, string posTranNo)
    {
        var vat = (int)Math.Round(amount / 11m, MidpointRounding.AwayFromZero);
        return $"S00=002;S01=D1;S02=40;S03={merchant.Tid};S04={merchant.BusinessNo};" +
               $"S09=00;S10={(int)amount};S15=0;S16={vat};S23={posTranNo};";
    }
}
```

`src/FishingMartPos/Services/Kicc/KiccResponseParser.cs`:

```csharp
namespace FishingMartPos.Services.Kicc;

public static class KiccResponseParser
{
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
}
```

- [ ] **Step 4: 테스트 통과 확인**

Run: `dotnet test --filter "FullyQualifiedName~Kicc.Kicc"`
Expected: PASS (6개 테스트 전부)

- [ ] **Step 5: 커밋**

```bash
git add src/FishingMartPos/Services/Kicc/KiccMerchantConfig.cs src/FishingMartPos/Services/Kicc/KiccMessageBuilder.cs src/FishingMartPos/Services/Kicc/KiccResponseParser.cs tests/FishingMartPos.Tests/Services/Kicc/KiccMessageBuilderTests.cs tests/FishingMartPos.Tests/Services/Kicc/KiccResponseParserTests.cs
git commit -m "KiccMessageBuilder/KiccResponseParser 추가 (KICC 전문 생성/파싱, 순수함수)"
```

---

### Task 3: `IKiccPosClient` / `KiccRawResponse` / `KiccPosClient`

**Files:**
- Create: `src/FishingMartPos/Services/Kicc/KiccRawResponse.cs`
- Create: `src/FishingMartPos/Services/Kicc/IKiccPosClient.cs`
- Create: `src/FishingMartPos/Services/Kicc/KiccPosClient.cs`
- Create: `tests/FishingMartPos.Tests/Fakes/FakeKiccPosClient.cs`

**Interfaces:**
- Produces: `IKiccPosClient.ConnectAsync(): Task<bool>`, `IKiccPosClient.Disconnect(): void`, `IKiccPosClient.RequestAsync(int cmd, int gcd, int jcd, string sendData): Task<KiccRawResponse>`, `KiccRawResponse { bool IsSuccess, string? Data, string? FailureMessage }` — Task 5(`KiccVanPaymentGateway`)와 Task 6(`PosViewModel.OpenCashDrawer`)이 이 인터페이스를 사용한다.

**중요 — 이 태스크는 TDD 대상이 아니다.** `KiccPosClient`는 실제 `KiccPos.dll`과 물리 단말기가 있어야 동작을 검증할 수 있는 P/Invoke 래퍼다. 이 개발 PC에는 하드웨어가 없으므로 `KiccPosClient` 자체를 호출하는 테스트는 작성하지 않는다(작성해도 통과/실패를 판단할 방법이 없다 — 컴파일만 되고 실행하면 하드웨어 부재로 반드시 실패한다). 대신 `IKiccPosClient` 인터페이스만 깔끔하게 만들고, `FakeKiccPosClient`로 Task 5/6에서 이 인터페이스의 소비자 쪽을 테스트한다. 이 갭(하드웨어 없이는 `KiccPosClient` 자체를 검증 못 함)은 계획에 의도적으로 남기는 것이며, 브리프를 따르지 않은 게 아니다 — 셀프리뷰에서 "왜 KiccPosClient 테스트가 없냐"고 스스로 의심하지 말 것.

- [ ] **Step 1: `KiccRawResponse`와 `IKiccPosClient` 작성**

`src/FishingMartPos/Services/Kicc/KiccRawResponse.cs`:

```csharp
namespace FishingMartPos.Services.Kicc;

public sealed class KiccRawResponse
{
    public required bool IsSuccess { get; init; }
    public string? Data { get; init; }
    public string? FailureMessage { get; init; }

    public static KiccRawResponse Success(string data) => new() { IsSuccess = true, Data = data };

    public static KiccRawResponse Failure(string message) => new() { IsSuccess = false, FailureMessage = message };
}
```

`src/FishingMartPos/Services/Kicc/IKiccPosClient.cs`:

```csharp
namespace FishingMartPos.Services.Kicc;

public interface IKiccPosClient
{
    Task<bool> ConnectAsync();
    void Disconnect();
    Task<KiccRawResponse> RequestAsync(int cmd, int gcd, int jcd, string sendData);
}
```

- [ ] **Step 2: `KiccPosClient` 구현**

`src/FishingMartPos/Services/Kicc/KiccPosClient.cs`:

```csharp
using System.Runtime.InteropServices;
using System.Text;

namespace FishingMartPos.Services.Kicc;

public sealed class KiccPosClient : IKiccPosClient
{
    private const int MaxPollAttempts = 50;
    private const int PollIntervalMs = 100;

    private readonly int _port;
    private readonly int _baud;

    public KiccPosClient(int port, int baud)
    {
        _port = port;
        _baud = baud;
    }

    [DllImport("KiccPos.dll", EntryPoint = "KLoad", CharSet = CharSet.Ansi)]
    private static extern int KLoad(int pPort, int pBaud, byte[] pErrMsg);

    [DllImport("KiccPos.dll", EntryPoint = "KUnLoad", CharSet = CharSet.Ansi)]
    private static extern void KUnLoad();

    [DllImport("KiccPos.dll", EntryPoint = "KReqCmd", CharSet = CharSet.Ansi)]
    private static extern int KReqCmd(int CMD, int GCD, int JCD, string SendData, byte[] ErrMsg);

    [DllImport("KiccPos.dll", EntryPoint = "KGetEvent", CharSet = CharSet.Ansi)]
    private static extern int KGetEvent(ref int CMD, ref int GCD, ref int JCD, ref int RCD, byte[] RData, byte[] RHexData);

    public Task<bool> ConnectAsync() => Task.Run(() =>
    {
        var err = new byte[4096];
        int ret = KLoad(_port, _baud, err);
        return ret == 0;
    });

    public void Disconnect() => KUnLoad();

    public async Task<KiccRawResponse> RequestAsync(int cmd, int gcd, int jcd, string sendData)
    {
        var err = new byte[4096];
        int ret = await Task.Run(() => KReqCmd(cmd, gcd, jcd, sendData, err));

        if (ret == -2) return KiccRawResponse.Failure("응답 시간 초과");
        if (ret == -3) return KiccRawResponse.Failure("고객이 결제를 취소했습니다");
        if (ret != 0) return KiccRawResponse.Failure(Encoding.Default.GetString(err).TrimEnd('\0'));

        for (int attempt = 0; attempt < MaxPollAttempts; attempt++)
        {
            int c = 0, g = 0, j = 0, rcd = 0;
            var rData = new byte[2048];
            var rHex = new byte[4096];
            int len = KGetEvent(ref c, ref g, ref j, ref rcd, rData, rHex);
            if (len > 0)
            {
                var text = Encoding.Default.GetString(rData).TrimEnd('\0');
                return rcd == 0x00 ? KiccRawResponse.Success(text) : KiccRawResponse.Failure(text);
            }
            await Task.Delay(PollIntervalMs);
        }
        return KiccRawResponse.Failure("응답 시간 초과");
    }
}
```

- [ ] **Step 3: `FakeKiccPosClient` 작성 (Task 5/6에서 사용)**

`tests/FishingMartPos.Tests/Fakes/FakeKiccPosClient.cs`:

```csharp
using FishingMartPos.Services.Kicc;

namespace FishingMartPos.Tests.Fakes;

public sealed class FakeKiccPosClient : IKiccPosClient
{
    private readonly KiccRawResponse _response;
    public List<(int Cmd, int Gcd, int Jcd, string SendData)> Requests { get; } = new();

    public FakeKiccPosClient(KiccRawResponse response)
    {
        _response = response;
    }

    public Task<bool> ConnectAsync() => Task.FromResult(true);

    public void Disconnect() { }

    public Task<KiccRawResponse> RequestAsync(int cmd, int gcd, int jcd, string sendData)
    {
        Requests.Add((cmd, gcd, jcd, sendData));
        return Task.FromResult(_response);
    }
}
```

- [ ] **Step 4: 빌드 확인**

Run: `dotnet build`
Expected: `Build succeeded.` 0경고/0오류 (이 태스크는 실행 가능한 테스트가 없으므로 빌드 성공이 유일한 검증 기준)

- [ ] **Step 5: 커밋**

```bash
git add src/FishingMartPos/Services/Kicc/KiccRawResponse.cs src/FishingMartPos/Services/Kicc/IKiccPosClient.cs src/FishingMartPos/Services/Kicc/KiccPosClient.cs tests/FishingMartPos.Tests/Fakes/FakeKiccPosClient.cs
git commit -m "IKiccPosClient/KiccPosClient 추가 (KiccPos.dll P/Invoke 래퍼) — 하드웨어 없어 단위테스트 불가, FakeKiccPosClient만 제공"
```

---

### Task 4: `VanConfigRow` / `IVanConfigRepository` / `VanConfigRepository` / 마이그레이션

**Files:**
- Create: `db/migrations/004_van_config_pay_type.sql`
- Create: `src/FishingMartPos/Models/VanConfigRow.cs`
- Create: `src/FishingMartPos/Repositories/IVanConfigRepository.cs`, `src/FishingMartPos/Repositories/VanConfigRepository.cs`
- Test: `tests/FishingMartPos.Tests/Repositories/VanConfigRepositoryTests.cs`

**Interfaces:**
- Produces: `VanConfigRow { string PosCd, string VanCode, string PayType, string? TerminalId, string? BusinessNo }`, `IVanConfigRepository.GetByPosCodeAsync(string posCd): Task<IReadOnlyList<VanConfigRow>>` — Task 6의 `App.xaml.cs`가 이걸로 `KiccMerchantConfig` 딕셔너리를 만든다.

- [ ] **Step 1: 마이그레이션 작성 및 적용**

`db/migrations/004_van_config_pay_type.sql`:

```sql
-- 004_van_config_pay_type.sql
-- van_config_tb에 pay_type 컬럼 추가: 카드결제1/2가 같은 물리 단말기(van_code='KICC')를
-- 공유하면서도 서로 다른 가맹점(TID/사업자번호) 앞으로 승인 요청을 보내야 하기 때문.
-- 실행: mysql -h <host> -u <user> -p <database> < 004_van_config_pay_type.sql

ALTER TABLE van_config_tb
    ADD COLUMN pay_type VARCHAR(10) NOT NULL DEFAULT 'CARD1' AFTER van_code,
    ADD COLUMN business_no VARCHAR(10) NULL AFTER terminal_id,
    DROP PRIMARY KEY,
    ADD PRIMARY KEY (pos_cd, van_code, pay_type);

-- 대원수산(카드결제1) / 대원낚시마트(카드결제2) 실제 가맹점 정보 시드
INSERT INTO van_config_tb (pos_cd, van_code, pay_type, terminal_id, business_no) VALUES
    ('1', 'KICC', 'CARD1', '2977338', '3169055788'),
    ('1', 'KICC', 'CARD2', '2977340', '3160326930')
ON DUPLICATE KEY UPDATE terminal_id = VALUES(terminal_id), business_no = VALUES(business_no);
```

이 개발 PC의 dev DB에 직접 적용한다(mysql CLI가 없으므로, 임시 xUnit 테스트로 실행 — [[project-pos-status]] 메모리에 기록된 기존 요령과 동일한 방식): `tests/FishingMartPos.Tests/_ScratchMigrationRunner.cs`를 만들어 위 SQL 파일을 세미콜론으로 분리해 실행하는 테스트를 작성하고, **주의: 각 statement 청크의 첫 줄만 보고 "--면 주석"으로 건너뛰면 안 된다** — 청크 안의 각 줄을 개별로 걸러내고 나머지를 합쳐야 한다(과거 세션에서 이 실수로 마이그레이션이 조용히 하나도 안 먹힌 적이 있음). 실행 후 `information_schema.STATISTICS` 혹은 `DESCRIBE van_config_tb`로 컬럼이 실제로 추가됐는지, `SELECT * FROM van_config_tb`로 시드 데이터가 실제로 들어갔는지 **반드시 확인**한 뒤 스크래치 테스트 파일을 삭제한다.

- [ ] **Step 2: 실패하는 테스트 작성**

`tests/FishingMartPos.Tests/Repositories/VanConfigRepositoryTests.cs`:

```csharp
using Dapper;
using FishingMartPos.Configuration;
using FishingMartPos.Data;
using FishingMartPos.Repositories;
using Xunit;

namespace FishingMartPos.Tests.Repositories;

public class VanConfigRepositoryTests
{
    private const string TestPosCd = "9";

    private static (IVanConfigRepository repo, IDbConnectionFactory factory) Create()
    {
        var config = AppConfig.Load(AppContext.BaseDirectory);
        var factory = new MySqlConnectionFactory(config);
        return (new VanConfigRepository(factory), factory);
    }

    [Fact]
    public async Task GetByPosCodeAsync_ReturnsEachPayTypeRowSeparately()
    {
        var (repo, factory) = Create();
        using var conn = await factory.CreateOpenConnectionAsync();
        await conn.ExecuteAsync("DELETE FROM van_config_tb WHERE pos_cd = @PosCd", new { PosCd = TestPosCd });
        try
        {
            await conn.ExecuteAsync(
                """
                INSERT INTO van_config_tb (pos_cd, van_code, pay_type, terminal_id, business_no)
                VALUES (@PosCd, 'KICC', 'CARD1', '1111111', '1111111111'), (@PosCd, 'KICC', 'CARD2', '2222222', '2222222222')
                """,
                new { PosCd = TestPosCd });

            var rows = await repo.GetByPosCodeAsync(TestPosCd);

            Assert.Equal(2, rows.Count);
            var card1 = Assert.Single(rows, r => r.PayType == "CARD1");
            Assert.Equal("1111111", card1.TerminalId);
            Assert.Equal("1111111111", card1.BusinessNo);
            var card2 = Assert.Single(rows, r => r.PayType == "CARD2");
            Assert.Equal("2222222", card2.TerminalId);
        }
        finally
        {
            await conn.ExecuteAsync("DELETE FROM van_config_tb WHERE pos_cd = @PosCd", new { PosCd = TestPosCd });
        }
    }

    [Fact]
    public async Task GetByPosCodeAsync_SeedData_ReturnsDaewonMerchantsForPos1()
    {
        var (repo, _) = Create();

        var rows = await repo.GetByPosCodeAsync("1");

        var card1 = Assert.Single(rows, r => r.PayType == "CARD1");
        Assert.Equal("2977338", card1.TerminalId);
        Assert.Equal("3169055788", card1.BusinessNo);
        var card2 = Assert.Single(rows, r => r.PayType == "CARD2");
        Assert.Equal("2977340", card2.TerminalId);
        Assert.Equal("3160326930", card2.BusinessNo);
    }
}
```

두 번째 테스트(`GetByPosCodeAsync_SeedData_ReturnsDaewonMerchantsForPos1`)는 Step 1에서 적용한 마이그레이션의 시드 값이 실제로 정확한지(TID/사업자번호 오타 없이) 검증하는 안전장치다 — 이 값이 틀리면 실제 매장에서 승인이 엉뚱한 사업자 앞으로 잡히는 심각한 문제이므로 반드시 통과해야 한다.

- [ ] **Step 3: 테스트 실패 확인**

Run: `dotnet test --filter "FullyQualifiedName~VanConfigRepositoryTests"`
Expected: FAIL (컴파일 에러 — `IVanConfigRepository`/`VanConfigRepository`/`VanConfigRow`가 아직 없음)

- [ ] **Step 4: 구현**

`src/FishingMartPos/Models/VanConfigRow.cs`:

```csharp
namespace FishingMartPos.Models;

public sealed class VanConfigRow
{
    public required string PosCd { get; init; }
    public required string VanCode { get; init; }
    public required string PayType { get; init; }
    public string? TerminalId { get; init; }
    public string? BusinessNo { get; init; }
}
```

`src/FishingMartPos/Repositories/IVanConfigRepository.cs`:

```csharp
using FishingMartPos.Models;

namespace FishingMartPos.Repositories;

public interface IVanConfigRepository
{
    Task<IReadOnlyList<VanConfigRow>> GetByPosCodeAsync(string posCd);
}
```

`src/FishingMartPos/Repositories/VanConfigRepository.cs`:

```csharp
using Dapper;
using FishingMartPos.Data;
using FishingMartPos.Models;

namespace FishingMartPos.Repositories;

public sealed class VanConfigRepository : IVanConfigRepository
{
    private readonly IDbConnectionFactory _connectionFactory;

    public VanConfigRepository(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<IReadOnlyList<VanConfigRow>> GetByPosCodeAsync(string posCd)
    {
        using var connection = await _connectionFactory.CreateOpenConnectionAsync();
        const string sql = """
            SELECT pos_cd AS PosCd, van_code AS VanCode, pay_type AS PayType,
                   terminal_id AS TerminalId, business_no AS BusinessNo
            FROM van_config_tb
            WHERE pos_cd = @PosCd
            """;
        var rows = await connection.QueryAsync<VanConfigRow>(sql, new { PosCd = posCd });
        return rows.ToList();
    }
}
```

- [ ] **Step 5: 테스트 통과 확인**

Run: `dotnet test --filter "FullyQualifiedName~VanConfigRepositoryTests"`
Expected: PASS (2개 테스트 전부 — 특히 시드 데이터 검증 테스트가 실제 DB의 마이그레이션 적용 결과를 확인하는 것이므로 반드시 통과해야 한다)

- [ ] **Step 6: 커밋**

```bash
git add db/migrations/004_van_config_pay_type.sql src/FishingMartPos/Models/VanConfigRow.cs src/FishingMartPos/Repositories/IVanConfigRepository.cs src/FishingMartPos/Repositories/VanConfigRepository.cs tests/FishingMartPos.Tests/Repositories/VanConfigRepositoryTests.cs
git commit -m "van_config_tb에 pay_type/business_no 추가, 대원수산/대원낚시마트 시드 + VanConfigRepository"
```

---

### Task 5: `KiccVanPaymentGateway`

**Files:**
- Create: `src/FishingMartPos/Services/Kicc/KiccVanPaymentGateway.cs`
- Test: `tests/FishingMartPos.Tests/Services/Kicc/KiccVanPaymentGatewayTests.cs`

**Interfaces:**
- Consumes: `IKiccPosClient.RequestAsync`(Task 3), `KiccMessageBuilder.BuildApprovalRequest`/`KiccResponseParser.Parse`(Task 2), 기존 `IVanPaymentGateway`/`VanApprovalRequest`/`VanApprovalResult`(`src/FishingMartPos/Services/IVanPaymentGateway.cs`)
- Produces: `KiccVanPaymentGateway : IVanPaymentGateway` — Task 6의 `App.xaml.cs`가 이 클래스를 인스턴스화한다.

- [ ] **Step 1: 실패하는 테스트 작성**

`tests/FishingMartPos.Tests/Services/Kicc/KiccVanPaymentGatewayTests.cs`:

```csharp
using FishingMartPos.Services;
using FishingMartPos.Services.Kicc;
using FishingMartPos.Tests.Fakes;
using Xunit;

namespace FishingMartPos.Tests.Services.Kicc;

public class KiccVanPaymentGatewayTests
{
    private static readonly Dictionary<string, KiccMerchantConfig> Merchants = new()
    {
        ["CARD1"] = new KiccMerchantConfig("CARD1", "2977338", "3169055788"),
        ["CARD2"] = new KiccMerchantConfig("CARD2", "2977340", "3160326930"),
    };

    [Fact]
    public async Task RequestApprovalAsync_WhenR04IsSuccess_ReturnsApprovedWithApprovalNoAndKiccVanCode()
    {
        var client = new FakeKiccPosClient(KiccRawResponse.Success("R04=0000;R09=99145616;"));
        var gateway = new KiccVanPaymentGateway(client, Merchants);

        var result = await gateway.RequestApprovalAsync(new VanApprovalRequest("1", "CARD1", 5000m));

        Assert.True(result.IsApproved);
        Assert.Equal("99145616", result.ApprovalNo);
        Assert.Equal("KICC", result.VanCode);
    }

    [Fact]
    public async Task RequestApprovalAsync_WhenR04IsNotSuccess_ReturnsDeclined()
    {
        var client = new FakeKiccPosClient(KiccRawResponse.Success("R04=1234;"));
        var gateway = new KiccVanPaymentGateway(client, Merchants);

        var result = await gateway.RequestApprovalAsync(new VanApprovalRequest("1", "CARD1", 5000m));

        Assert.False(result.IsApproved);
        Assert.Null(result.ApprovalNo);
    }

    [Fact]
    public async Task RequestApprovalAsync_WhenClientReportsFailure_ReturnsDeclinedWithFailureMessage()
    {
        var client = new FakeKiccPosClient(KiccRawResponse.Failure("응답 시간 초과"));
        var gateway = new KiccVanPaymentGateway(client, Merchants);

        var result = await gateway.RequestApprovalAsync(new VanApprovalRequest("1", "CARD1", 5000m));

        Assert.False(result.IsApproved);
        Assert.Equal("응답 시간 초과", result.ResponseMessage);
    }

    [Fact]
    public async Task RequestApprovalAsync_Card1_SendsDaewonSusanMerchantFields()
    {
        var client = new FakeKiccPosClient(KiccRawResponse.Success("R04=0000;R09=1;"));
        var gateway = new KiccVanPaymentGateway(client, Merchants);

        await gateway.RequestApprovalAsync(new VanApprovalRequest("1", "CARD1", 5000m));

        var sendData = Assert.Single(client.Requests).SendData;
        Assert.Contains("S03=2977338;S04=3169055788;", sendData);
    }

    [Fact]
    public async Task RequestApprovalAsync_Card2_SendsDaewonNakssiMartMerchantFields()
    {
        var client = new FakeKiccPosClient(KiccRawResponse.Success("R04=0000;R09=1;"));
        var gateway = new KiccVanPaymentGateway(client, Merchants);

        await gateway.RequestApprovalAsync(new VanApprovalRequest("1", "CARD2", 5000m));

        var sendData = Assert.Single(client.Requests).SendData;
        Assert.Contains("S03=2977340;S04=3160326930;", sendData);
    }

    [Fact]
    public async Task RequestApprovalAsync_SendsApprovalCmdGcdJcd()
    {
        var client = new FakeKiccPosClient(KiccRawResponse.Success("R04=0000;R09=1;"));
        var gateway = new KiccVanPaymentGateway(client, Merchants);

        await gateway.RequestApprovalAsync(new VanApprovalRequest("1", "CARD1", 5000m));

        var req = Assert.Single(client.Requests);
        Assert.Equal(0xFB, req.Cmd);
        Assert.Equal(0x14, req.Gcd);
        Assert.Equal(0x04, req.Jcd);
    }
}
```

- [ ] **Step 2: 테스트 실패 확인**

Run: `dotnet test --filter "FullyQualifiedName~KiccVanPaymentGatewayTests"`
Expected: FAIL (컴파일 에러 — `KiccVanPaymentGateway`가 아직 없음)

- [ ] **Step 3: 구현**

`src/FishingMartPos/Services/Kicc/KiccVanPaymentGateway.cs`:

```csharp
using FishingMartPos.Services;

namespace FishingMartPos.Services.Kicc;

public sealed class KiccVanPaymentGateway : IVanPaymentGateway
{
    private readonly IKiccPosClient _client;
    private readonly IReadOnlyDictionary<string, KiccMerchantConfig> _merchantsByPayType;

    public KiccVanPaymentGateway(IKiccPosClient client, IReadOnlyDictionary<string, KiccMerchantConfig> merchantsByPayType)
    {
        _client = client;
        _merchantsByPayType = merchantsByPayType;
    }

    public async Task<VanApprovalResult> RequestApprovalAsync(VanApprovalRequest request)
    {
        var merchant = _merchantsByPayType[request.PayType];
        var posTranNo = BuildPosTranNo(request.PosCode);
        var sendData = KiccMessageBuilder.BuildApprovalRequest(merchant, request.Amount, posTranNo);

        var raw = await _client.RequestAsync(0xFB, 0x14, 0x04, sendData);

        if (!raw.IsSuccess)
        {
            return new VanApprovalResult { IsApproved = false, ResponseMessage = raw.FailureMessage ?? "카드 승인 거절" };
        }

        var fields = KiccResponseParser.Parse(raw.Data!);
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

    private static string BuildPosTranNo(string posCode) =>
        $"{posCode}{DateTime.Now:yyMMddHHmmss}{Random.Shared.Next(10, 99)}";
}
```

(`src/FishingMartPos/Services/Kicc/` 네임스페이스 안에서 `using FishingMartPos.Services;`로 최상위 `Services` 네임스페이스의 `IVanPaymentGateway`/`VanApprovalRequest`/`VanApprovalResult`를 참조한다.)

- [ ] **Step 4: 테스트 통과 확인**

Run: `dotnet test --filter "FullyQualifiedName~KiccVanPaymentGatewayTests"`
Expected: PASS (6개 테스트 전부 — 특히 `RequestApprovalAsync_Card1_*`/`RequestApprovalAsync_Card2_*` 두 개는 카드결제1/2가 서로 다른 가맹점으로 정확히 라우팅되는지 확인하는 핵심 테스트)

- [ ] **Step 5: 커밋**

```bash
git add src/FishingMartPos/Services/Kicc/KiccVanPaymentGateway.cs tests/FishingMartPos.Tests/Services/Kicc/KiccVanPaymentGatewayTests.cs
git commit -m "KiccVanPaymentGateway 추가 (IVanPaymentGateway의 실제 KICC 구현체)"
```

---

### Task 6: 설정/DI 배선, 돈통열기, 최종 검증

**Files:**
- Modify: `src/FishingMartPos/Configuration/AppConfig.cs`
- Modify: `src/FishingMartPos/appsettings.json`
- Modify: `src/FishingMartPos/App.xaml.cs`
- Modify: `src/FishingMartPos/ViewModels/PosViewModel.cs` (생성자에 선택 인자 추가, `OpenCashDrawer` 커맨드 신규)
- Modify: `src/FishingMartPos/Views/PosView.xaml:246` (돈통열기 버튼)
- Modify: `tests/FishingMartPos.Tests/ViewModels/PosViewModelTests.cs` (`CreateViewModel` 헬퍼 확장 + 신규 테스트)

**Interfaces:**
- Consumes: `IKiccPosClient`(Task 3), `IVanConfigRepository`(Task 4), `KiccVanPaymentGateway`/`KiccMerchantConfig`(Task 5)
- Produces: (없음 — 이 태스크가 조립의 마지막 단계)

- [ ] **Step 1: `AppConfig`에 Kicc 섹션 추가**

`src/FishingMartPos/Configuration/AppConfig.cs` 전체를 아래로 교체:

```csharp
using Microsoft.Extensions.Configuration;

namespace FishingMartPos.Configuration;

public sealed class AppConfig
{
    public required string ConnectionString { get; init; }
    public required string PosCode { get; init; }
    public bool KiccUseRealGateway { get; init; }
    public int KiccComPort { get; init; }
    public int KiccBaudRate { get; init; } = 57600;

    public static AppConfig Load(string basePath)
    {
        var configuration = new ConfigurationBuilder()
            .SetBasePath(basePath)
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
            .AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: false)
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

        bool.TryParse(configuration["Kicc:UseRealGateway"], out bool kiccUseRealGateway);
        int.TryParse(configuration["Kicc:ComPort"], out int kiccComPort);
        if (!int.TryParse(configuration["Kicc:BaudRate"], out int kiccBaudRate))
        {
            kiccBaudRate = 57600;
        }

        return new AppConfig
        {
            ConnectionString = connectionString,
            PosCode = posCode,
            KiccUseRealGateway = kiccUseRealGateway,
            KiccComPort = kiccComPort,
            KiccBaudRate = kiccBaudRate,
        };
    }
}
```

**주의:** `configuration.GetValue<T>(...)` 확장 메서드는 사용하지 않는다 — `Microsoft.Extensions.Configuration.Binder` 패키지가 `FishingMartPos.csproj`에 없어서 컴파일 에러가 난다(기존 코드도 인덱서(`configuration["..."]`) + 수동 파싱만 쓰는 이유가 이것). 위처럼 `configuration["..."]`로 문자열을 읽어 `bool.TryParse`/`int.TryParse`로 직접 변환하는 기존 파일의 방식을 그대로 따른다.

- [ ] **Step 2: `appsettings.json`에 기본값(스텁 모드) 추가**

`src/FishingMartPos/appsettings.json` 전체를 아래로 교체:

```json
{
  "Database": {
    "ConnectionString": "Server=127.0.0.1;Port=3306;Database=fishingmart_dev;Uid=root;Pwd=;"
  },
  "Terminal": {
    "PosCode": "1"
  },
  "Kicc": {
    "UseRealGateway": false,
    "ComPort": 0,
    "BaudRate": 57600
  }
}
```

(이 커밋본 기본값은 반드시 `UseRealGateway: false`로 유지한다 — 매장 PC 전환은 git-ignore 대상인 `appsettings.Local.json`에서 오버라이드한다, 기존 DB 연결 문자열이 이미 이 패턴을 쓰고 있다.)

- [ ] **Step 3: `PosViewModel`에 선택 인자와 `OpenCashDrawer` 추가**

`src/FishingMartPos/ViewModels/PosViewModel.cs` 상단 `using` 목록(8행 부근, `using FishingMartPos.Services;` 아래)에 추가:

```csharp
using FishingMartPos.Services.Kicc;
```

필드(23행 `private readonly IVanPaymentGateway _vanGateway;` 바로 아래)에 추가:

```csharp
    private readonly IKiccPosClient? _kiccPosClient;
```

생성자(65~85행)를 아래로 교체 — **마지막 파라미터만 추가, 나머지는 그대로**:

```csharp
    public PosViewModel(
        IProductRepository productRepository,
        ICodeRepository codeRepository,
        ISalesRepository salesRepository,
        IHeldOrderRepository heldOrderRepository,
        IDelayProvider delay,
        ICurrentSession session,
        INavigationService navigation,
        MainMenuViewModel mainMenuViewModel,
        IVanPaymentGateway vanGateway,
        IKiccPosClient? kiccPosClient = null)
    {
        _productRepository = productRepository;
        _codeRepository = codeRepository;
        _salesRepository = salesRepository;
        _heldOrderRepository = heldOrderRepository;
        _delay = delay;
        _session = session;
        _navigation = navigation;
        _mainMenuViewModel = mainMenuViewModel;
        _vanGateway = vanGateway;
        _kiccPosClient = kiccPosClient;
    }
```

새 파라미터가 선택 인자(`= null`)이므로, `App.xaml.cs`를 제외한 나머지 4개 호출부(`PosViewModelTests.cs`, `PosViewModelPaymentTests.cs`, `MainMenuViewModelTests.cs`, `LoginViewModelTests.cs`)는 **이 Step에서 수정하지 않는다** — 그대로 컴파일된다.

`OpenCashDrawer` 커맨드를 `PayCardAsync` 메서드(326~381행) 바로 아래에 추가:

```csharp
    [RelayCommand]
    private async Task OpenCashDrawer()
    {
        if (!CanPay) return;
        if (_kiccPosClient is null)
        {
            IsToastWarning = true;
            ToastMessage = "돈통 연동은 지원 예정입니다";
            await _delay.Delay(TimeSpan.FromMilliseconds(1200));
            ToastMessage = null;
            return;
        }

        await _kiccPosClient.RequestAsync(0xFB, 0x14, 0x0B, "");
    }
```

- [ ] **Step 4: `PosViewModelTests.cs` 헬퍼 확장 및 신규 테스트**

`tests/FishingMartPos.Tests/ViewModels/PosViewModelTests.cs` 상단 `using` 목록에 추가:

```csharp
using FishingMartPos.Services.Kicc;
```

`CreateViewModel` 헬퍼(32~66행)를 아래로 교체 — 마지막 파라미터 하나만 추가:

```csharp
    private static PosViewModel CreateViewModel(
        out FakeSalesRepository sales,
        out FakeHeldOrderRepository held,
        out INavigationService navigation,
        out MainMenuViewModel mainMenuViewModel,
        IVanPaymentGateway? vanGateway = null,
        IKiccPosClient? kiccPosClient = null)
    {
        sales = new FakeSalesRepository();
        held = new FakeHeldOrderRepository();
        var codes = new Dictionary<string, IReadOnlyList<CodeItem>>
        {
            ["POSCAT"] = new List<CodeItem>
            {
                new() { Code = "BAIT", Name = "미끼", SortNo = 1 },
                new() { Code = "FLOAT", Name = "찌세트", SortNo = 2 },
            },
        };
        var session = new FishingMartPos.Services.CurrentSession();
        session.SignIn(
            new Staff { StaffCode = "ADMIN1", StaffName = "관리자", Role = "ADMIN", UseYn = "Y" },
            new PosTerminal { PosCode = "1", PosName = "POS1" });
        navigation = new NavigationService();
        mainMenuViewModel = new MainMenuViewModel(session, navigation);

        return new PosViewModel(
            new FakeProductRepository(new[] { Bait1, Float1 }),
            new FakeCodeRepository(codes),
            sales,
            held,
            new FakeDelayProvider(),
            session,
            navigation,
            mainMenuViewModel,
            vanGateway ?? new FakeVanPaymentGateway(ApprovedResult),
            kiccPosClient);
    }
```

파일 끝(마지막 `}` 앞)에 새 테스트를 추가한다:

```csharp
    [Fact]
    public async Task OpenCashDrawer_WhenNoKiccClientConfigured_ShowsNotSupportedToast()
    {
        var vm = CreateViewModel(out _, out _);
        await vm.LoadAsync();
        var toastValues = new List<string?>();
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(PosViewModel.ToastMessage) && vm.ToastMessage is not null)
                toastValues.Add(vm.ToastMessage);
        };

        await vm.OpenCashDrawerCommand.ExecuteAsync(null);

        Assert.True(vm.IsToastWarning);
        Assert.Equal("돈통 연동은 지원 예정입니다", Assert.Single(toastValues));
    }

    [Fact]
    public async Task OpenCashDrawer_WhenKiccClientConfigured_SendsCashDrawerCommand()
    {
        var fakeClient = new FakeKiccPosClient(KiccRawResponse.Success(""));
        var vm = CreateViewModel(out _, out _, out _, out _, kiccPosClient: fakeClient);
        await vm.LoadAsync();

        await vm.OpenCashDrawerCommand.ExecuteAsync(null);

        var req = Assert.Single(fakeClient.Requests);
        Assert.Equal(0xFB, req.Cmd);
        Assert.Equal(0x14, req.Gcd);
        Assert.Equal(0x0B, req.Jcd);
    }
```

- [ ] **Step 5: `App.xaml.cs` 배선**

`src/FishingMartPos/App.xaml.cs` 상단 `using` 목록에 추가(8행 `using FishingMartPos.Services;` 아래):

```csharp
using FishingMartPos.Services.Kicc;
```

`services.AddSingleton<IHeldOrderRepository, HeldOrderRepository>();`(42행) 바로 아래에 추가:

```csharp
        services.AddSingleton<IVanConfigRepository, VanConfigRepository>();
```

`var heldOrderRepository = _services.GetRequiredService<IHeldOrderRepository>();`(60행) 바로 아래에 추가:

```csharp
        var vanConfigRepository = _services.GetRequiredService<IVanConfigRepository>();
```

`var vanGateway = _services.GetRequiredService<IVanPaymentGateway>();`(65행)을 아래 블록으로 교체:

```csharp
        IVanPaymentGateway vanGateway = _services.GetRequiredService<IVanPaymentGateway>(); // StubVanPaymentGateway (기본값)
        IKiccPosClient? kiccPosClient = null;
        if (config.KiccUseRealGateway)
        {
            var merchantRows = await vanConfigRepository.GetByPosCodeAsync(config.PosCode);
            var merchantsByPayType = merchantRows.ToDictionary(
                r => r.PayType,
                r => new KiccMerchantConfig(r.PayType, r.TerminalId ?? string.Empty, r.BusinessNo ?? string.Empty));

            var realClient = new KiccPosClient(config.KiccComPort, config.KiccBaudRate);
            await realClient.ConnectAsync(); // 연결 실패해도 앱은 계속 기동 — 카드결제 시점에 자연스럽게 실패 처리됨
            kiccPosClient = realClient;
            vanGateway = new KiccVanPaymentGateway(realClient, merchantsByPayType);
        }
        _kiccPosClient = kiccPosClient;
```

(`ToDictionary`를 쓰려면 `using System.Linq;`가 필요한데, 파일 상단에 이미 암시적 usings(`ImplicitUsings=enable`, csproj 확인됨)가 켜져 있어 `System.Linq`는 이미 전역으로 사용 가능하다 — 별도 using 불필요.)

`CreatePosViewModelAsync` 내부(71행)의 `new PosViewModel(...)` 호출을 아래로 교체:

```csharp
            var vm = new PosViewModel(productRepository, codeRepository, salesRepository, heldOrderRepository, delayProvider, session, navigation, mainMenu, vanGateway, kiccPosClient);
```

클래스 필드(`private ServiceProvider? _services;` 16행) 바로 아래에 추가:

```csharp
    private IKiccPosClient? _kiccPosClient;
```

`OnExit`(151~155행)를 아래로 교체(단말기 연결 종료 추가):

```csharp
    protected override void OnExit(ExitEventArgs e)
    {
        _kiccPosClient?.Disconnect();
        _services?.Dispose();
        base.OnExit(e);
    }
```

- [ ] **Step 6: `PosView.xaml` 돈통열기 버튼 연결**

`src/FishingMartPos/Views/PosView.xaml:246`:

```xml
                    <Button Grid.Row="1" Content="돈통열기" Command="{Binding OpenCashDrawerCommand}" IsEnabled="{Binding CanPay}" Margin="3" />
```

- [ ] **Step 7: 빌드 및 전체 테스트 통과 확인**

Run: `dotnet build`
Expected: `Build succeeded.` 0경고/0오류

Run: `dotnet test`
Expected: 기존 테스트 전부 + 이번에 추가한 테스트(Task 2~6) 전부 PASS, 실패 0건. (`UseRealGateway: false`가 기본값이므로 이 개발 PC에서 돌리는 `dotnet test`는 여전히 `StubVanPaymentGateway` 경로만 타고, `KiccPosClient`/실제 하드웨어를 전혀 건드리지 않는다 — 정상.)

- [ ] **Step 8: 수동 확인 (하드웨어 없이 가능한 범위까지)**

앱을 실행해(`dotnet run --project src/FishingMartPos`) 로그인 → 판매화면까지 정상 진입하는지 확인한다(appsettings.json의 `Kicc.UseRealGateway=false`이므로 카드결제는 여전히 스텁 시뮬레이션으로 동작 — 이전 스텁 작업 때 이미 검증된 흐름과 동일해야 한다). "돈통열기" 버튼을 눌러 "돈통 연동은 지원 예정입니다" 경고 토스트가 뜨는지 확인한다(`_kiccPosClient`가 null인 스텁 모드 경로).

**이 스텝에서 확인하지 못하는 것(하드웨어 필요, 매장에서 별도 확인):** `appsettings.Local.json`에 `Kicc.UseRealGateway=true`+실제 COM포트/보드레이트를 넣고, 실제 ED-721 단말기를 연결한 뒤 카드결제1(대원수산)/카드결제2(대원낚시마트) 각각 실제 카드로 승인이 나는지, 승인번호가 매출 내역에 정확히 기록되는지, 돈통열기가 실제로 동작하는지.

- [ ] **Step 9: 커밋**

```bash
git add src/FishingMartPos/Configuration/AppConfig.cs src/FishingMartPos/appsettings.json src/FishingMartPos/App.xaml.cs src/FishingMartPos/ViewModels/PosViewModel.cs src/FishingMartPos/Views/PosView.xaml tests/FishingMartPos.Tests/ViewModels/PosViewModelTests.cs
git commit -m "KICC 실제 게이트웨이 DI 배선(Kicc:UseRealGateway 스위치), 돈통열기 연결"
```

(이번 계획의 마지막 태스크지만, 실제 매장 배포/`git push`는 컨트롤러가 사용자에게 확인 후 진행한다 — 이 계획 자체에는 push 스텝을 포함하지 않는다.)
