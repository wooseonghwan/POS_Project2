using System.Windows.Input;

namespace FishingMartPos.ViewModels;

public sealed class StaffRowViewModel
{
    public required string StaffCode { get; init; }
    public required string StaffName { get; init; }
    public required string RoleName { get; init; }
    public required string UseYnLabel { get; init; }
    public required ICommand EditCommand { get; init; }
}
