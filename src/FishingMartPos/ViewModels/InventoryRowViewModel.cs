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
    // 특정 POS분류 하나로 필터링된 상태에서만 같은 탭 안 순서를 바꿀 수 있다("전체" 보기나 검색 중에는 의미가 없음).
    public required bool CanReorder { get; init; }
    public required ICommand DeleteCommand { get; init; }
    public required ICommand EditCommand { get; init; }
    public required ICommand ShowDetailCommand { get; init; }
    public required ICommand MoveUpCommand { get; init; }
    public required ICommand MoveDownCommand { get; init; }
}
