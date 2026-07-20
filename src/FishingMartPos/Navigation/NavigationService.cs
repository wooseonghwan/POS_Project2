namespace FishingMartPos.Navigation;

public sealed class NavigationService : INavigationService
{
    public object? CurrentViewModel { get; private set; }
    public event EventHandler? CurrentViewModelChanged;

    public void NavigateTo(object viewModel)
    {
        CurrentViewModel = viewModel;
        CurrentViewModelChanged?.Invoke(this, EventArgs.Empty);
    }
}
