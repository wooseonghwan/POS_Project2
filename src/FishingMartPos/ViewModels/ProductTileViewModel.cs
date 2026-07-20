using System.Windows.Input;
using System.Windows.Media;

namespace FishingMartPos.ViewModels;

public sealed class ProductTileViewModel
{
    public required string Barcode { get; init; }
    public required string Name { get; init; }
    public required string PriceStr { get; init; }
    public required string Initial { get; init; }
    public required Brush Swatch { get; init; }
    public required ICommand AddCommand { get; init; }
}
