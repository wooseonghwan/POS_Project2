using CommunityToolkit.Mvvm.Input;

namespace FishingMartPos.ViewModels;

public sealed class PaymentSummaryRowViewModel
{
    public required long SaleNo { get; init; }
    public required string SaleDtStr { get; init; }
    public required string PayTypeLabel { get; init; }
    public required string TotalAmtStr { get; init; }
    public string? ApprovalNo { get; init; }
    public required string StatusLabel { get; init; }
    public required IAsyncRelayCommand SelectCommand { get; init; }
}
