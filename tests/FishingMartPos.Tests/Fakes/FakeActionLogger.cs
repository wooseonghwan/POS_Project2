using FishingMartPos.Services;

namespace FishingMartPos.Tests.Fakes;

public sealed class FakeActionLogger : IActionLogger
{
    public sealed record LoggedCall(string ActionType, string? Detail, bool Success, string? ErrorMessage);

    public List<LoggedCall> Calls { get; } = new();

    public Task LogAsync(string actionType, string? detail = null, bool success = true, string? errorMessage = null)
    {
        Calls.Add(new LoggedCall(actionType, detail, success, errorMessage));
        return Task.CompletedTask;
    }
}
