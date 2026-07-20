namespace FishingMartPos.Models;

public sealed class Product
{
    public required string Barcode { get; init; }
    public required string MajorCd { get; init; }
    public required string MinorCd { get; init; }
    public required string PosCatCd { get; init; }
    public required string Name { get; init; }
    public required decimal Price { get; init; }
    public required int StockQty { get; init; }
}
