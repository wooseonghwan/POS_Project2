using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FishingMartPos.Models;
using FishingMartPos.Navigation;
using FishingMartPos.Repositories;
using FishingMartPos.Services;

namespace FishingMartPos.ViewModels;

public sealed partial class PrinterSettingsViewModel : ObservableObject
{
    private readonly IPrinterConfigRepository _printerConfigRepository;
    private readonly ICurrentSession _session;
    private readonly IDelayProvider _delay;
    private readonly INavigationService _navigation;
    private readonly SettingsViewModel _returnTo;

    [ObservableProperty] private string _printerPort = string.Empty;
    [ObservableProperty] private string _printerName = string.Empty;
    [ObservableProperty] private bool _drawerKickEnabled = true;
    [ObservableProperty] private string? _toastMessage;

    public PrinterSettingsViewModel(
        IPrinterConfigRepository printerConfigRepository,
        ICurrentSession session,
        IDelayProvider delay,
        INavigationService navigation,
        SettingsViewModel returnTo)
    {
        _printerConfigRepository = printerConfigRepository;
        _session = session;
        _delay = delay;
        _navigation = navigation;
        _returnTo = returnTo;
    }

    public async Task LoadAsync()
    {
        string posCd = _session.CurrentTerminal!.PosCode;
        var config = await _printerConfigRepository.GetAsync(posCd);

        PrinterPort = config?.PrinterPort ?? string.Empty;
        PrinterName = config?.PrinterName ?? string.Empty;
        DrawerKickEnabled = config?.DrawerKickEnabled ?? true;
    }

    [RelayCommand]
    private async Task Save()
    {
        string posCd = _session.CurrentTerminal!.PosCode;
        await _printerConfigRepository.SaveAsync(new PrinterConfig
        {
            PosCd = posCd,
            PrinterPort = PrinterPort,
            PrinterName = PrinterName,
            DrawerKickEnabled = DrawerKickEnabled,
        });

        ToastMessage = "저장되었습니다";
        await _delay.Delay(TimeSpan.FromMilliseconds(1200));
        ToastMessage = null;
    }

    [RelayCommand]
    private async Task TestPrint()
    {
        ToastMessage = "프린터 연동은 지원 예정입니다";
        await _delay.Delay(TimeSpan.FromMilliseconds(1200));
        ToastMessage = null;
    }

    [RelayCommand]
    private void GoToSettings() => _navigation.NavigateTo(_returnTo);
}
