using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FishingMartPos.Navigation;

namespace FishingMartPos.ViewModels;

public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly INavigationService _navigation;
    private readonly MainMenuViewModel _returnTo;

    public SettingsViewModel(INavigationService navigation, MainMenuViewModel returnTo)
    {
        _navigation = navigation;
        _returnTo = returnTo;
    }

    public Func<SettingsViewModel, Task<StaffListViewModel>>? StaffListViewModelFactory { get; init; }
    public Func<SettingsViewModel, Task<PrinterSettingsViewModel>>? PrinterSettingsViewModelFactory { get; init; }
    public Func<SettingsViewModel, Task<ReceiptSettingsViewModel>>? ReceiptSettingsViewModelFactory { get; init; }
    public Func<SettingsViewModel, Task<SystemInfoViewModel>>? SystemInfoViewModelFactory { get; init; }
    public Func<SettingsViewModel, Task<CodeManageViewModel>>? CodeManageViewModelFactory { get; init; }

    [RelayCommand]
    private async Task GoToStaffList()
    {
        var vm = await StaffListViewModelFactory!.Invoke(this);
        _navigation.NavigateTo(vm);
    }

    [RelayCommand]
    private async Task GoToPrinterSettings()
    {
        var vm = await PrinterSettingsViewModelFactory!.Invoke(this);
        _navigation.NavigateTo(vm);
    }

    [RelayCommand]
    private async Task GoToReceiptSettings()
    {
        var vm = await ReceiptSettingsViewModelFactory!.Invoke(this);
        _navigation.NavigateTo(vm);
    }

    [RelayCommand]
    private async Task GoToSystemInfo()
    {
        var vm = await SystemInfoViewModelFactory!.Invoke(this);
        _navigation.NavigateTo(vm);
    }

    [RelayCommand]
    private async Task GoToCodeManage()
    {
        var vm = await CodeManageViewModelFactory!.Invoke(this);
        _navigation.NavigateTo(vm);
    }

    [RelayCommand]
    private void GoToMainMenu() => _navigation.NavigateTo(_returnTo);
}
