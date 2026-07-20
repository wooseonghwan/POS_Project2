using FishingMartPos.Services;

namespace FishingMartPos.Tests.Fakes;

public sealed class FakeDelayProvider : IDelayProvider
{
    public Task Delay(TimeSpan span) => Task.CompletedTask;
}
