namespace FishingMartPos.Models;

public sealed class ReceiptConfig
{
    public required string PosCd { get; init; }
    public string? HeaderText { get; init; }
    public string? FooterText { get; init; }
}
