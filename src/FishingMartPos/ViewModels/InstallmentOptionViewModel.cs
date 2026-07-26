using System.Windows.Input;

namespace FishingMartPos.ViewModels;

public sealed class InstallmentOptionViewModel
{
    public required int Months { get; init; }
    public required string Label { get; init; }
    public required bool IsSelected { get; init; }
    public required bool IsEnabled { get; init; }
    public required ICommand SelectCommand { get; init; }
}
