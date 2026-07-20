using System.Windows.Input;

namespace FishingMartPos.ViewModels;

public sealed class CartLineViewModel
{
    public required string Barcode { get; init; }
    public required string Name { get; init; }
    public required int Qty { get; init; }
    public required string PriceStr { get; init; }
    public required string LineTotalStr { get; init; }
    public required bool IsSelected { get; init; }
    public required ICommand SelectCommand { get; init; }
}
