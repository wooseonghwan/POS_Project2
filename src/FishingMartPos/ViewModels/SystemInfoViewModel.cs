using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FishingMartPos.Navigation;
using FishingMartPos.Repositories;
using FishingMartPos.Services;

namespace FishingMartPos.ViewModels;

public sealed partial class SystemInfoViewModel : ObservableObject
{
    private readonly ISystemInfoRepository _systemInfoRepository;
    private readonly ICurrentSession _session;
    private readonly INavigationService _navigation;
    private readonly SettingsViewModel _returnTo;

    [ObservableProperty] private string _appVersionStr = "-";
    [ObservableProperty] private string _dbVersionStr = "-";
    [ObservableProperty] private string _dbConnectionStatusStr = "-";
    [ObservableProperty] private string _posTerminalStr = string.Empty;

    public SystemInfoViewModel(
        ISystemInfoRepository systemInfoRepository,
        ICurrentSession session,
        INavigationService navigation,
        SettingsViewModel returnTo)
    {
        _systemInfoRepository = systemInfoRepository;
        _session = session;
        _navigation = navigation;
        _returnTo = returnTo;
    }

    public async Task LoadAsync()
    {
        var terminal = _session.CurrentTerminal;
        PosTerminalStr = terminal is not null ? $"{terminal.PosName} ({terminal.PosCode})" : string.Empty;

        try
        {
            var info = await _systemInfoRepository.GetAsync();
            AppVersionStr = info?.AppVersion ?? "-";
            DbVersionStr = info?.DbVersion ?? "-";
            DbConnectionStatusStr = "연결됨";
        }
        catch
        {
            AppVersionStr = "-";
            DbVersionStr = "-";
            DbConnectionStatusStr = "연결 안됨";
        }
    }

    [RelayCommand]
    private void GoToSettings() => _navigation.NavigateTo(_returnTo);
}
