using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FishingMartPos.Models;
using FishingMartPos.Navigation;
using FishingMartPos.Repositories;
using FishingMartPos.Services;

namespace FishingMartPos.ViewModels;

public sealed partial class ReceiptSettingsViewModel : ObservableObject
{
    private readonly IReceiptConfigRepository _receiptConfigRepository;
    private readonly ICurrentSession _session;
    private readonly IDelayProvider _delay;
    private readonly INavigationService _navigation;
    private readonly SettingsViewModel _returnTo;

    [ObservableProperty] private string _headerText = string.Empty;
    [ObservableProperty] private string _footerText = string.Empty;
    [ObservableProperty] private string? _toastMessage;

    public ReceiptSettingsViewModel(
        IReceiptConfigRepository receiptConfigRepository,
        ICurrentSession session,
        IDelayProvider delay,
        INavigationService navigation,
        SettingsViewModel returnTo)
    {
        _receiptConfigRepository = receiptConfigRepository;
        _session = session;
        _delay = delay;
        _navigation = navigation;
        _returnTo = returnTo;
    }

    public async Task LoadAsync()
    {
        string posCd = _session.CurrentTerminal!.PosCode;
        var config = await _receiptConfigRepository.GetAsync(posCd);

        HeaderText = config?.HeaderText ?? string.Empty;
        FooterText = config?.FooterText ?? string.Empty;
    }

    [RelayCommand]
    private async Task Save()
    {
        string posCd = _session.CurrentTerminal!.PosCode;
        await _receiptConfigRepository.SaveAsync(new ReceiptConfig
        {
            PosCd = posCd,
            HeaderText = HeaderText,
            FooterText = FooterText,
        });

        ToastMessage = "저장되었습니다";
        await _delay.Delay(TimeSpan.FromMilliseconds(1200));
        ToastMessage = null;
    }

    [RelayCommand]
    private void GoToSettings() => _navigation.NavigateTo(_returnTo);
}
