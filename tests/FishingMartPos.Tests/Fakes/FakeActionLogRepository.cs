using FishingMartPos.Models;
using FishingMartPos.Repositories;

namespace FishingMartPos.Tests.Fakes;

public sealed class FakeActionLogRepository : IActionLogRepository
{
    public List<ActionLogEntry> Inserted { get; } = new();

    public bool ThrowOnInsert { get; set; }

    public Task InsertAsync(ActionLogEntry entry)
    {
        if (ThrowOnInsert)
        {
            throw new InvalidOperationException("DB 연결 끊김(테스트용)");
        }

        Inserted.Add(entry);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<ActionLogEntry>> SearchAsync(
        DateTime from, DateTime to, string? actionType = null, string? resultStatus = null)
    {
        IReadOnlyList<ActionLogEntry> rows = Inserted
            .Where(e => e.LogDt >= from && e.LogDt < to)
            .Where(e => actionType is null || e.ActionType == actionType)
            .Where(e => resultStatus is null || e.ResultStatus == resultStatus)
            .ToList();
        return Task.FromResult(rows);
    }
}
