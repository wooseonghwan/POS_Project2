namespace FishingMartPos.Models;

public sealed class Product
{
    public required string Barcode { get; init; }
    public required string PosCatCd { get; init; }
    public required string Name { get; init; }
    public required decimal Price { get; init; }
    public required int StockQty { get; init; }
    public string? PhotoPath { get; init; }
    // 판매 화면 카테고리 목록에 노출할지 여부. false면 바코드 스캔으로만 담긴다.
    public bool ShowInGrid { get; init; }
    // 같은 POS분류 탭 안에서의 노출 순서(작을수록 먼저). 상품등록/수정 화면에서는 건드리지 않고,
    // 재고관리 화면의 ▲▼ 버튼으로만 바뀐다.
    public int SortNo { get; init; }
}
