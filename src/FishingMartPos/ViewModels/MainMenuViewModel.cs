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

    [RelayCommand]
    private void GoToSales() => _navigation.NavigateTo(new PlaceholderViewModel("판매"));

    [RelayCommand]
    private void GoToSalesReport() => _navigation.NavigateTo(new PlaceholderViewModel("매출"));

    [RelayCommand]
    private void GoToInventory() => _navigation.NavigateTo(new PlaceholderViewModel("재고"));

    [RelayCommand]
    private void GoToSettings() => _navigation.NavigateTo(new PlaceholderViewModel("환경설정"));

    [RelayCommand]
    private void Logout()
    {
        _session.SignOut();
        _navigation.NavigateTo(LoginViewModelFactory!.Invoke());
    }
}
