namespace FishingMartPos.Models;

/// <summary>action_log_tb 한 행. 문제 발생 시 "언제 누가 어떤 액션을 했는지" 추적하기 위한 로그.</summary>
public sealed class ActionLogEntry
{
    public long LogNo { get; init; }
    public DateTime LogDt { get; init; }
    public string? PosCd { get; init; }
    public string? StaffCd { get; init; }
    public required string ActionType { get; init; }
    public string? ActionDetail { get; init; }
    public string ResultStatus { get; init; } = "SUCCESS";
    public string? ErrorMessage { get; init; }
}
