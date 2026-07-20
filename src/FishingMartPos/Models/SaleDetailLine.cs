namespace FishingMartPos.Models;

public sealed class SaleDetailLine
{
    public required string Barcode { get; init; }
    public required string ProductName { get; init; }
    public required int Qty { get; init; }
    public required decimal UnitPrice { get; init; }

    public decimal LineAmt => UnitPrice * Qty;
}
