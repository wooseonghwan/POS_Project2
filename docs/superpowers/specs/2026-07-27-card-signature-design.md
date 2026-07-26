# 카드결제 서명 (Feature B) 설계

**작성일:** 2026-07-27

## 배경

카드결제 5만원 이상 시 서명을 받아야 한다는 요구사항. POS에 붙어있는 카드리더기(ED-785, 부착형)는 화면/서명패드가 없는 단순 카드투입구이므로, **서명은 POS 화면에서 직접 캡처**해야 한다.

KICC의 신용승인(D1) 전문에는 이미 서명 관련 필드가 정의되어 있다(`단말기승인연동_일반(신전문)_모듈API` 문서, S01=D1/D4 등과 같은 요청에서 쓰는 공용 필드 테이블):
- `S30` — 값 `1`이면 서명 데이터 포함. 벤더 문서: "Kicc.dll의 Kicc_Bmp2SignDataN() 함수를 이용하여 생성"
- `S31` — HexString 타입 서명 데이터 (예: 128x32 size bmp file)

`Kicc.dll`은 `KiccPos.dll`(카드 승인/취소 등 실제 통신을 담당)과는 별개의 DLL로, 이미 `src/FishingMartPos/Native/Kicc.dll`에 존재하지만 이 프로젝트에서 아직 한 번도 사용된 적이 없다. 벤더가 제공한 C# 샘플 프로그램(`Form1.cs`)에는 `KiccPos.dll`의 `KGetBmp`/`KSaveToBmp`/`KBmpFileToEpsonPrtData` 같은 BMP 관련 함수들이 전부 "파일 경로를 입력받아 byte[] 출력 버퍼에 결과를 채우고 길이(또는 -1)를 반환"하는 동일한 패턴을 따른다 — `Kicc_Bmp2SignDataN()`도 같은 패턴(비트맵 파일 경로 입력 → 헥스 문자열 출력 버퍼)일 가능성이 높지만, **이 함수 자체의 정확한 파라미터 시그니처는 어떤 벤더 문서에도 없다** (KiccPos.dll의 함수만 문서화되어 있음). 실제 물리 단말기 없이는 검증 불가능한 부분이며, 구현 단계에서 export 이름 확인/시행착오로 확정해야 한다 — 이는 이번 프로젝트의 다른 KICC 연동 작업들과 동일하게 "물리적 단말기로 최종 검증 필요" 항목으로 남는다.

## 범위

1. 카드결제 팝업에서 합계금액이 **5만원 이상**이면, 실제 승인 요청 전에 서명 캡처 화면을 보여준다.
2. 서명은 **필수** — 서명 없이는 승인 진행 불가.
3. 서명은 **누가 그려도 상관없음**(고객이든 직원이든) — 접근 제한 없이 단순 캔버스.
4. 서명 데이터는 KICC 승인 요청(D1)에 실어 보내는 것으로 끝 — **POS 쪽에는 저장하지 않는다**(파일/DB 어디에도 남기지 않음, 변환용 임시 파일도 전송 직후 삭제).
5. 5만원 미만 카드결제는 기존과 동일하게 서명 단계 없이 바로 승인 진행.
6. 카드결제1/카드결제2 모두 동일하게 적용, 할부 여부와 무관(일시불이든 할부든 5만원 이상이면 서명 필요).

**범위 밖:** 영수증에 서명 이미지 인쇄(현재 영수증 프린터가 스텁이라 범위 밖), 서명 이미지 로컬 저장/재조회.

## 흐름

1. 사용자가 할부개월을 선택하고 "승인요청" 클릭.
2. `PosViewModel.RequestCardApproval`이 합계금액을 확인 — `_cart.Total >= CardSignatureMinimumAmount(50,000)` 이고 아직 서명을 안 받았으면, 실제 KICC 호출 대신 서명 캡처 화면을 띄우고 리턴한다(카드결제 팝업의 3번째 상태: 할부선택 / **서명입력** / 승인처리중).
3. 서명 캡처 화면: WPF `InkCanvas`로 마우스/터치 드로잉. "지우기"(캔버스 초기화), "취소"(할부선택으로 복귀, 서명 폐기), "서명완료" 버튼.
4. "서명완료" 클릭 시: 캔버스에 획이 하나도 없으면(빈 서명) 경고 토스트("서명을 입력해주세요")를 띄우고 진행하지 않는다. 획이 있으면 `InkCanvas`를 비트맵으로 렌더링해 임시 BMP 파일로 저장하고, `Kicc_Bmp2SignDataN()`으로 헥스 문자열 변환 → 임시 파일 삭제 → 변환된 헥스를 들고 그대로 카드 승인 로직(`RequestCardApproval`의 나머지 부분, 승인처리중 상태)으로 이어간다.
5. 승인 요청 메시지(`KiccMessageBuilder.BuildApprovalRequest`)에 서명 헥스가 있으면 `S30=1;S31=<hex>;`를 추가로 실어 보낸다. 5만원 미만이거나 서명이 없는 경우 이 필드들은 생략(기존 동작 그대로).
6. 승인 성공/실패와 무관하게 서명 데이터는 요청 한 번 보내고 나면 메모리/파일 어디에도 남기지 않는다.

## 컴포넌트

### `ISignatureConverter` (신규)
기존 `IVanPaymentGateway`/`ICashReceiptGateway`와 동일한 Stub/실제구현 패턴:
```csharp
public interface ISignatureConverter
{
    Task<string?> ConvertToHexAsync(byte[] bmpBytes);
}
```
- `StubSignatureConverter` — 개발/테스트용. 항상 고정된 더미 헥스 문자열을 반환(실제 변환 로직 없음).
- `KiccSignatureConverter` — 실제 구현. 전달받은 BMP 바이트를 임시 파일로 쓰고 `Kicc.dll`의 `Kicc_Bmp2SignDataN`을 P/Invoke로 호출해 헥스 문자열을 얻은 뒤 임시 파일을 삭제한다. 정확한 P/Invoke 시그니처는 구현 단계에서 확정(위 배경 참고).

### `IVanPaymentGateway` 확장
`VanApprovalRequest`에 선택적 필드 추가:
```csharp
public sealed record VanApprovalRequest(string PosCode, string PayType, decimal Amount, int InstallmentMonths = 0, string? SignatureHex = null);
```
`KiccMessageBuilder.BuildApprovalRequest`가 `signatureHex`가 null이 아니면 `S30=1;S31={signatureHex};`를 기존 필드 뒤에 추가.

### `PosViewModel` 확장
- `CardSignatureMinimumAmount = 50000m` 상수(기존 `InstallmentMinimumAmount`와 값은 같지만 별개의 상수 — 할부 자격과 서명 필요 여부는 서로 다른 업무 규칙이라 개념적으로 분리).
- `IsSignatureCaptureVisible` bool, 서명 캔버스 상태.
- `RequestCardApproval`이 금액 확인 후 서명 캡처 화면으로 분기하는 로직.
- `ConfirmSignatureCommand`(캔버스 → 헥스 변환 → 승인 로직 이어가기), `ClearSignatureCommand`(캔버스 초기화), `CancelSignatureCommand`(할부선택으로 복귀).
- `InkCanvas`의 획 데이터를 다루는 부분은 코드비하인드(`PosView.xaml.cs`)에서 렌더링/커맨드 파라미터 전달을 담당 — 이 프로젝트는 이미 `PosView.xaml.cs`에 `PreviewTextInput`/`PreviewKeyDown` 같은 최소한의 코드비하인드를 두는 선례가 있다(바코드 스캔 입력 처리).

### `PosView.xaml` 확장
카드결제 팝업에 3번째 상태(서명입력) 추가: `InkCanvas` + 지우기/취소/서명완료 버튼. 기존 모달 오버레이 패턴은 그대로(카드결제 팝업 자체 안에서 상태 전환이므로 별도 오버레이 Grid는 불필요, 기존 팝업 내부에 `StackPanel Visibility` 전환으로 처리 — 할부선택/승인처리중 상태 전환과 동일한 방식).

## 테스트 방침

- `StubSignatureConverter`를 사용하면 `PosViewModel`의 분기 로직(5만원 기준 서명 요구 여부, 빈 서명 거부, 서명 취소 시 할부선택 복귀)을 실제 하드웨어 없이 전부 단위 테스트로 검증 가능.
- `KiccMessageBuilder.BuildApprovalRequest`의 S30/S31 추가 로직은 리터럴 문자열 비교로 테스트(서명 있음/없음 두 케이스).
- `KiccSignatureConverter`의 실제 P/Invoke 호출은 물리 단말기/DLL 없이 자동 테스트 불가 — 인터페이스 뒤에 격리해 나머지 로직과 독립적으로 둔다(기존 `KiccPosClient` 계열과 동일한 한계).

## 자기 검토

- **플레이스홀더 스캔**: `Kicc_Bmp2SignDataN`의 정확한 시그니처는 의도적으로 "구현 단계에서 확정"으로 남겨둠 — 이는 실제 하드웨어/DLL 없이는 검증 불가능한 사실이라 배경 절에 명시했고, 플랜 작성 시에도 이 사실을 다시 명시해 구현자가 놓치지 않게 한다(TBD 방치가 아니라 명확한 이유가 있는 알려진 제약).
- **내부 일관성**: 5만원 기준이 `InstallmentMinimumAmount`와 값은 같지만 의도적으로 분리된 상수라는 점을 명시해 향후 두 값이 달라져도 헷갈리지 않게 함.
- **범위 점검**: 단일 플랜으로 처리 가능한 범위(신규 인터페이스 1개 + 게이트웨이 확장 + ViewModel/View 확장).
- **모호성 점검**: "서명 필수", "POS 미보관", "5만원 이상(할부 무관)"을 명확히 확정함.
