using System.Windows.Media;

namespace FishingMartPos.ViewModels;

public sealed class InventoryDetailViewModel
{
    public required string Barcode { get; init; }
    public required string Name { get; init; }
    public required string MajorName { get; init; }
    public required string MinorName { get; init; }
    public required string PosCatName { get; init; }
    public required string PriceStr { get; init; }
    public required string StockQtyStr { get; init; }
    public required Brush Swatch { get; init; }
    public string? PhotoAbsolutePath { get; init; }
}
