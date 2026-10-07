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
    public string? PhotoPath { get; init; }
    // 판매 화면 카테고리 목록에 노출할지 여부. false면 바코드 스캔으로만 담긴다.
    public bool ShowInGrid { get; init; }
}
