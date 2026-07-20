using System.Windows.Input;

namespace FishingMartPos.ViewModels;

public sealed class CategoryTabViewModel
{
    public required string Code { get; init; }
    public required string Name { get; init; }
    public required bool IsActive { get; init; }
    public required ICommand SelectCommand { get; init; }
}
