namespace FishingMartPos.Services;

/// <summary>화면 이동, 결제, 상품등록/수정 등 주요 액션을 action_log_tb에 남기기 위한 서비스.
/// 구현체는 내부에서 모든 예외를 삼켜야 한다 — 로그 적재 실패가 실제 업무 흐름(결제, 저장 등)을
/// 막으면 안 되기 때문이다.</summary>
public interface IActionLogger
{
    /// <param name="actionType">예: NAVIGATE, LOGIN, LOGOUT, SALE_CASH, SALE_CARD, SALE_CANCEL,
    /// CASH_RECEIPT_ISSUE, RECEIPT_PRINT, PRODUCT_SAVE, STAFF_SAVE, CASH_DRAWER_SAVE, UNHANDLED_EXCEPTION</param>
    /// <param name="detail">사람이 읽을 수 있는 상세 설명(한글 가능)</param>
    /// <param name="success">true면 SUCCESS, false면 FAIL로 저장</param>
    /// <param name="errorMessage">실패 시 원인(예외 메시지 등)</param>
    Task LogAsync(string actionType, string? detail = null, bool success = true, string? errorMessage = null);
}
