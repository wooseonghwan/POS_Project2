using FishingMartPos.Models;
using FishingMartPos.Repositories;

namespace FishingMartPos.Services;

public sealed class ActionLogger : IActionLogger
{
    private readonly IActionLogRepository _repository;
    private readonly ICurrentSession _session;

    public ActionLogger(IActionLogRepository repository, ICurrentSession session)
    {
        _repository = repository;
        _session = session;
    }

    public async Task LogAsync(string actionType, string? detail = null, bool success = true, string? errorMessage = null)
    {
        try
        {
            var entry = new ActionLogEntry
            {
                PosCd = _session.CurrentTerminal?.PosCode,
                StaffCd = _session.CurrentStaff?.StaffCode,
                ActionType = actionType,
                ActionDetail = detail,
                ResultStatus = success ? "SUCCESS" : "FAIL",
                ErrorMessage = errorMessage,
            };
            await _repository.InsertAsync(entry);
        }
        catch
        {
            // 로그 적재 자체의 실패(DB 단절 등)가 실제 업무 흐름(결제, 저장, 화면이동 등)을
            // 막으면 안 된다 — 조용히 무시한다. 로깅은 "있으면 좋은" 보조 기능이다.
        }
    }
}
