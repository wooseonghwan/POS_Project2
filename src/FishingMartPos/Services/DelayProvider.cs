namespace FishingMartPos.Services;

public sealed class DelayProvider : IDelayProvider
{
    public Task Delay(TimeSpan span) => Task.Delay(span);
}
