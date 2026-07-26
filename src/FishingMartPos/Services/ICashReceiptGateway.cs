namespace FishingMartPos.Services;

public interface ICashReceiptGateway
{
    Task<CashReceiptResult> RequestIssueAsync(CashReceiptRequest request);

    Task<CashReceiptCancelResult> RequestCancelAsync(CashReceiptCancelRequest request);
}

public sealed record CashReceiptRequest(string PosCode, string MerchantPayType, string ReceiptType, decimal Amount);
// PosCode: S23(POS 거래번호) 조립에 사용 — 카드승인(VanApprovalRequest)과 동일한 규칙.
// MerchantPayType: "CARD1"/"CARD2" — van_config_tb 조회 키(카드결제와 동일한 가맹점 TID를 재사용).
// ReceiptType: "PERSONAL"(개인 소득공제용) / "BUSINESS"(사업자 지출증빙용).

public sealed class CashReceiptResult
{
    public required bool IsIssued { get; init; }
    public string? ApprovalNo { get; init; }           // 발급 성공 시 승인번호(KICC 응답 R09)
    public string? ApprovalDateYyMmDd { get; init; }   // 발급 성공 시 YYMMDD(KICC 응답 R07 앞 6자리) — B2 취소에 사용
    public required string ResponseMessage { get; init; }
}

public sealed record CashReceiptCancelRequest(
    string PosCode, string MerchantPayType, string ReceiptType, decimal Amount,
    string OriginalApprovalNo, string OriginalApprovalDateYyMmDd);

public sealed class CashReceiptCancelResult
{
    public required bool IsCancelled { get; init; }
    public required string ResponseMessage { get; init; }
}
