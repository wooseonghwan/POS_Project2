using System.Windows.Input;
using System.Windows.Media;

namespace FishingMartPos.ViewModels;

public sealed class InventoryRowViewModel
{
    public required string Barcode { get; init; }
    public required string Name { get; init; }
    public required string PosCatName { get; init; }
    public required string PriceStr { get; init; }
    public required string StockQtyStr { get; init; }
    public required Brush Swatch { get; init; }
    public string? PhotoAbsolutePath { get; init; }
    public required bool CanDelete { get; init; }
    public required bool CanEdit { get; init; }
    public required ICommand DeleteCommand { get; init; }
    public required ICommand EditCommand { get; init; }
    public required ICommand ShowDetailCommand { get; init; }
}
