namespace FishingMartPos.Services;

public interface ICashReceiptGateway
{
    Task<CashReceiptResult> RequestIssueAsync(CashReceiptRequest request);
}

public sealed record CashReceiptRequest(string MerchantPayType, string ReceiptType, decimal Amount);
// MerchantPayType: "CARD1"/"CARD2" — van_config_tb 조회 키(카드결제와 동일한 가맹점 TID를 재사용).
// ReceiptType: "PERSONAL"(개인 소득공제용) / "BUSINESS"(사업자 지출증빙용).

public sealed class CashReceiptResult
{
    public required bool IsIssued { get; init; }
    public string? ApprovalNo { get; init; }           // 발급 성공 시 승인번호(KICC 응답 R09)
    public string? ApprovalDateYyMmDd { get; init; }   // 발급 성공 시 YYMMDD(KICC 응답 R07 앞 6자리) — 향후 B2 취소에 필요, 지금은 저장만
    public required string ResponseMessage { get; init; }
}
