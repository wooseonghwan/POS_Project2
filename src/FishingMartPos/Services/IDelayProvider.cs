namespace FishingMartPos.Services;

public interface IDelayProvider
{
    Task Delay(TimeSpan span);
}
