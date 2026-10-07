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

    public bool IsAdmin => _session.CurrentStaff?.IsAdmin ?? false;

    /// <summary>App.xaml.cs(또는 테스트)에서 주입하는 LoginViewModel 팩토리 — 로그아웃 시 사용.</summary>
    public Func<LoginViewModel>? LoginViewModelFactory { get; init; }

    /// <summary>App.xaml.cs(또는 테스트)에서 주입하는 PosViewModel 팩토리 — "판매" 진입 시 사용. 자신(this)을 넘겨줘 PosViewModel이 "메뉴" 버튼으로 복귀 시 재사용한다.</summary>
    public Func<MainMenuViewModel, Task<PosViewModel>>? PosViewModelFactory { get; init; }

    /// <summary>App.xaml.cs(또는 테스트)에서 주입하는 InventoryViewModel 팩토리 — "재고" 진입 시 사용. 자신(this)을 넘겨줘 InventoryViewModel이 "메인메뉴로" 복귀 시 재사용한다.</summary>
    public Func<MainMenuViewModel, Task<InventoryViewModel>>? InventoryViewModelFactory { get; init; }

    /// <summary>App.xaml.cs(또는 테스트)에서 주입하는 SalesReportViewModel 팩토리 — "매출" 진입 시 사용(ADMIN 전용). 자신(this)을 넘겨줘 SalesReportViewModel이 "메인메뉴로" 복귀 시 재사용한다.</summary>
    public Func<MainMenuViewModel, Task<SalesReportViewModel>>? SalesReportViewModelFactory { get; init; }

    /// <summary>App.xaml.cs(또는 테스트)에서 주입하는 SettingsViewModel 팩토리 — "환경설정" 진입 시 사용(ADMIN 전용). 자신(this)을 넘겨줘 SettingsViewModel이 "메인메뉴로" 복귀 시 재사용한다.</summary>
    public Func<MainMenuViewModel, Task<SettingsViewModel>>? SettingsViewModelFactory { get; init; }

    /// <summary>App.xaml.cs(또는 테스트)에서 주입하는 PaymentManagementViewModel 팩토리 — "결제관리" 진입 시 사용. 자신(this)을 넘겨줘 PaymentManagementViewModel이 "메인메뉴로" 복귀 시 재사용한다.</summary>
    public Func<MainMenuViewModel, Task<PaymentManagementViewModel>>? PaymentManagementViewModelFactory { get; init; }

    /// <summary>App.xaml.cs(또는 테스트)에서 주입하는 CashDrawerViewModel 팩토리 — "시재" 진입 시 사용. 자신(this)을 넘겨줘 CashDrawerViewModel이 "메인메뉴로" 복귀 시 재사용한다.</summary>
    public Func<MainMenuViewModel, Task<CashDrawerViewModel>>? CashDrawerViewModelFactory { get; init; }

    [RelayCommand]
    private async Task GoToSales()
    {
        var posViewModel = await PosViewModelFactory!.Invoke(this);
        _navigation.NavigateTo(posViewModel);
    }

    [RelayCommand]
    private async Task GoToSalesReport()
    {
        if (!IsAdmin) return;
        var salesReportViewModel = await SalesReportViewModelFactory!.Invoke(this);
        _navigation.NavigateTo(salesReportViewModel);
    }

    [RelayCommand]
    private async Task GoToInventory()
    {
        var inventoryViewModel = await InventoryViewModelFactory!.Invoke(this);
        _navigation.NavigateTo(inventoryViewModel);
    }

    [RelayCommand]
    private async Task GoToSettings()
    {
        if (!IsAdmin) return;
        var settingsViewModel = await SettingsViewModelFactory!.Invoke(this);
        _navigation.NavigateTo(settingsViewModel);
    }

    [RelayCommand]
    private async Task GoToPaymentManagement()
    {
        var paymentManagementViewModel = await PaymentManagementViewModelFactory!.Invoke(this);
        _navigation.NavigateTo(paymentManagementViewModel);
    }

    [RelayCommand]
    private async Task GoToCashDrawer()
    {
        var cashDrawerViewModel = await CashDrawerViewModelFactory!.Invoke(this);
        _navigation.NavigateTo(cashDrawerViewModel);
    }

    [RelayCommand]
    private void Logout()
    {
        _session.SignOut();
        _navigation.NavigateTo(LoginViewModelFactory!.Invoke());
    }
}
