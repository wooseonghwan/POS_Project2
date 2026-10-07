namespace FishingMartPos.Models;

public sealed class CodeItem
{
    public required string Code { get; init; }
    public required string Name { get; init; }
    public required int SortNo { get; init; }
    // 판매화면에 이 분류의 메뉴 탭을 노출할지 여부. false면 탭은 숨겨지지만 코드/상품은 그대로 유지된다.
    public bool UseYn { get; init; } = true;
}
