namespace FishingMartPos.Models;

public sealed class PrinterConfig
{
    public required string PosCd { get; init; }
    public string? PrinterPort { get; init; }
    public string? PrinterName { get; init; }
    public required bool DrawerKickEnabled { get; init; }
}
