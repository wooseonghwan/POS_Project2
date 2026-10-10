using FishingMartPos.Services;

namespace FishingMartPos.Navigation;

public sealed class NavigationService : INavigationService
{
    private readonly IActionLogger _actionLogger;

    public NavigationService(IActionLogger? actionLogger = null)
    {
        _actionLogger = actionLogger ?? NullActionLogger.Instance;
    }

    public object? CurrentViewModel { get; private set; }
    public event EventHandler? CurrentViewModelChanged;

    public void NavigateTo(object viewModel)
    {
        CurrentViewModel = viewModel;
        CurrentViewModelChanged?.Invoke(this, EventArgs.Empty);
        // 모든 화면 이동이 여기를 거치므로, 한곳에서만 로그를 남겨도 "메뉴진입" 전체가 커버된다.
        _ = _actionLogger.LogAsync("NAVIGATE", $"화면 이동: {viewModel.GetType().Name}");
    }
}
