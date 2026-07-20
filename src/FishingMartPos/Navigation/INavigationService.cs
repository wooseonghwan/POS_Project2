namespace FishingMartPos.Navigation;

public interface INavigationService
{
    object? CurrentViewModel { get; }
    event EventHandler? CurrentViewModelChanged;

    void NavigateTo(object viewModel);
}
