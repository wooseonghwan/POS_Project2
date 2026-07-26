namespace FishingMartPos.ViewModels;

public sealed class SalesReportRowViewModel
{
    public required string Label { get; init; }
    public required string CountStr { get; init; }
    public required string CashStr { get; init; }
    public required string Card1Str { get; init; }
    public required string Card2Str { get; init; }
    public required string InstallmentCountStr { get; init; }
    public required string AmountStr { get; init; }
}
