using System.IO;
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
        catch (Exception ex)
        {
            // 로그 적재 자체의 실패(DB 단절, action_log_tb 테이블 미생성 등)가 실제 업무 흐름(결제,
            // 저장, 화면이동 등)을 막으면 안 된다 — 여기서 다시 던지지 않고 조용히 넘어간다.
            // 다만 "왜 action_log_tb에 안 쌓이는지" 현장에서 바로 확인할 수 있도록, 원인은 exe 옆
            // action-log-errors.txt에 남긴다(이 파일 쓰기 자체가 실패해도 무시).
            TryWriteDiagnosticFile(actionType, ex);
        }
    }

    private static void TryWriteDiagnosticFile(string actionType, Exception ex)
    {
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "action-log-errors.txt");
            File.AppendAllText(path,
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [{actionType}] action_log_tb 기록 실패: {ex}{Environment.NewLine}");
        }
        catch
        {
            // 진단용 파일 쓰기 실패는 완전히 무시 — 이것 때문에 앱이 영향받으면 안 된다.
        }
    }
}
