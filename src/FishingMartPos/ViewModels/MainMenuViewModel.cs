using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FishingMartPos.Navigation;
using FishingMartPos.Services;

namespace FishingMartPos.ViewModels;

public sealed partial class MainMenuViewModel : ObservableObject
{
    private readonly ICurrentSession _session;
    private readonly INavigationService _navigation;

    [ObservableProperty]
    private DateTime _now = DateTime.Now;

    public MainMenuViewModel(ICurrentSession session, INavigationService navigation)
    {
        _session = session;
        _navigation = navigation;
    }

    public string PosLabel => _session.CurrentTerminal?.PosName ?? string.Empty;

    /// <summary>App.xaml.cs(또는 테스트)에서 주입하는 LoginViewModel 팩토리 — 로그아웃 시 사용.</summary>
    public Func<LoginViewModel>? LoginViewModelFactory { get; init; }

    /// <summary>App.xaml.cs(또는 테스트)에서 주입하는 PosViewModel 팩토리 — "판매" 진입 시 사용.</summary>
    public Func<Task<PosViewModel>>? PosViewModelFactory { get; init; }

    /// <summary>App.xaml.cs(또는 테스트)에서 주입하는 InventoryViewModel 팩토리 — "재고" 진입 시 사용. 자신(this)을 넘겨줘 InventoryViewModel이 "메인메뉴로" 복귀 시 재사용한다.</summary>
    public Func<MainMenuViewModel, Task<InventoryViewModel>>? InventoryViewModelFactory { get; init; }

    [RelayCommand]
    private async Task GoToSales()
    {
        var posViewModel = await PosViewModelFactory!.Invoke();
        _navigation.NavigateTo(posViewModel);
    }

    [RelayCommand]
    private void GoToSalesReport() => _navigation.NavigateTo(new PlaceholderViewModel("매출"));

    [RelayCommand]
    private async Task GoToInventory()
    {
        var inventoryViewModel = await InventoryViewModelFactory!.Invoke(this);
        _navigation.NavigateTo(inventoryViewModel);
    }

    [RelayCommand]
    private void GoToSettings() => _navigation.NavigateTo(new PlaceholderViewModel("환경설정"));

    [RelayCommand]
    private void Logout()
    {
        _session.SignOut();
        _navigation.NavigateTo(LoginViewModelFactory!.Invoke());
    }
}
