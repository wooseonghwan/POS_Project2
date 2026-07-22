namespace FishingMartPos.Models;

public sealed class SystemInfo
{
    public required string AppVersion { get; init; }
    public required string DbVersion { get; init; }
}
