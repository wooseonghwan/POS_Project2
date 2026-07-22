using FishingMartPos.Models;
using FishingMartPos.Navigation;
using FishingMartPos.Services;
using FishingMartPos.Tests.Fakes;
using FishingMartPos.ViewModels;
using Xunit;

namespace FishingMartPos.Tests.ViewModels;

public class SystemInfoViewModelTests
{
    private static (SystemInfoViewModel vm, INavigationService navigation, SettingsViewModel settings) Create(FakeSystemInfoRepository repo)
    {
        var session = new CurrentSession();
        session.SignIn(
            new Staff { StaffCode = "ADMIN1", StaffName = "관리자", Role = "ADMIN", UseYn = "Y" },
            new PosTerminal { PosCode = "1", PosName = "POS1" });
        var navigation = new NavigationService();
        var settings = new SettingsViewModel(navigation, new MainMenuViewModel(session, navigation));
        var vm = new SystemInfoViewModel(repo, session, navigation, settings);
        return (vm, navigation, settings);
    }

    [Fact]
    public async Task LoadAsync_WhenRepositorySucceeds_FillsVersionsAndConnectedStatus()
    {
        var repo = new FakeSystemInfoRepository(new FishingMartPos.Models.SystemInfo { AppVersion = "0.1.0", DbVersion = "001" });
        var (vm, _, _) = Create(repo);

        await vm.LoadAsync();

        Assert.Equal("0.1.0", vm.AppVersionStr);
        Assert.Equal("001", vm.DbVersionStr);
        Assert.Equal("연결됨", vm.DbConnectionStatusStr);
        Assert.Equal("POS1 (1)", vm.PosTerminalStr);
    }

    [Fact]
    public async Task LoadAsync_WhenRepositoryThrows_ShowsDisconnectedStatusButStillFillsTerminal()
    {
        var repo = new FakeSystemInfoRepository(exceptionToThrow: new InvalidOperationException("DB down"));
        var (vm, _, _) = Create(repo);

        await vm.LoadAsync();

        Assert.Equal("-", vm.AppVersionStr);
        Assert.Equal("-", vm.DbVersionStr);
        Assert.Equal("연결 안됨", vm.DbConnectionStatusStr);
        Assert.Equal("POS1 (1)", vm.PosTerminalStr);
    }

    [Fact]
    public void GoToSettings_NavigatesBackToSettingsViewModel()
    {
        var repo = new FakeSystemInfoRepository();
        var (vm, navigation, settings) = Create(repo);

        vm.GoToSettingsCommand.Execute(null);

        Assert.Same(settings, navigation.CurrentViewModel);
    }
}
