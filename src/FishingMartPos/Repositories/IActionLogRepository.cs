using FishingMartPos.Models;

namespace FishingMartPos.Repositories;

public interface IActionLogRepository
{
    Task InsertAsync(ActionLogEntry entry);

    /// <summary>문제 추적용 조회. actionType/resultStatus가 null이면 해당 조건은 걸지 않는다.</summary>
    Task<IReadOnlyList<ActionLogEntry>> SearchAsync(
        DateTime from, DateTime to, string? actionType = null, string? resultStatus = null);
}
