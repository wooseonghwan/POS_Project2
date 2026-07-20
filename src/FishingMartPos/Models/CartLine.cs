namespace FishingMartPos.Models;

public sealed class CartLine
{
    public required string Barcode { get; init; }
    public required string Name { get; init; }
    public required decimal Price { get; init; }
    public int Qty { get; set; } = 1;

    public decimal LineTotal => Price * Qty;
}
