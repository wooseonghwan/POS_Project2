using System.Windows.Input;

namespace FishingMartPos.ViewModels;

public sealed class HeldOrderSummaryViewModel
{
    public required long HoldNo { get; init; }
    public required string HeldAtStr { get; init; }
    public required string TotalStr { get; init; }
    public required ICommand RecallCommand { get; init; }
    public required ICommand DeleteCommand { get; init; }
}
