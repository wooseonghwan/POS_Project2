namespace FishingMartPos.Models;

public sealed class VanConfigRow
{
    public required string PosCd { get; init; }
    public required string VanCode { get; init; }
    public required string PayType { get; init; }
    public string? TerminalId { get; init; }
    public string? BusinessNo { get; init; }
}
