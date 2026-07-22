using FishingMartPos.Models;
using FishingMartPos.Navigation;
using FishingMartPos.Services;
using FishingMartPos.Tests.Fakes;
using FishingMartPos.ViewModels;
using Xunit;

namespace FishingMartPos.Tests.ViewModels;

public class PrinterSettingsViewModelTests
{
    private static (PrinterSettingsViewModel vm, FakePrinterConfigRepository repo, INavigationService navigation, SettingsViewModel settings)
        Create(PrinterConfig? existing = null)
    {
        var byPosCd = new Dictionary<string, PrinterConfig>();
        if (existing is not null) byPosCd[existing.PosCd] = existing;
        var repo = new FakePrinterConfigRepository(byPosCd);
        var session = new CurrentSession();
        session.SignIn(
            new Staff { StaffCode = "ADMIN1", StaffName = "관리자", Role = "ADMIN", UseYn = "Y" },
            new PosTerminal { PosCode = "1", PosName = "POS1" });
        var navigation = new NavigationService();
        var settings = new SettingsViewModel(navigation, new MainMenuViewModel(session, navigation));
        var vm = new PrinterSettingsViewModel(repo, session, new FakeDelayProvider(), navigation, settings);
        return (vm, repo, navigation, settings);
    }

    [Fact]
    public async Task LoadAsync_WhenNoConfigExists_UsesDefaults()
    {
        var (vm, _, _, _) = Create();

        await vm.LoadAsync();

        Assert.Equal(string.Empty, vm.PrinterPort);
        Assert.True(vm.DrawerKickEnabled);
    }

    [Fact]
    public async Task LoadAsync_WhenConfigExists_FillsFormFromRepository()
    {
        var existing = new PrinterConfig { PosCd = "1", PrinterPort = "COM3", PrinterName = "EPSON", DrawerKickEnabled = false };
        var (vm, _, _, _) = Create(existing);

        await vm.LoadAsync();

        Assert.Equal("COM3", vm.PrinterPort);
        Assert.Equal("EPSON", vm.PrinterName);
        Assert.False(vm.DrawerKickEnabled);
    }

    [Fact]
    public async Task Save_PersistsCurrentValuesForCurrentTerminal()
    {
        var (vm, repo, _, _) = Create();
        await vm.LoadAsync();
        vm.PrinterPort = "COM5";
        vm.PrinterName = "STAR";
        vm.DrawerKickEnabled = false;

        await vm.SaveCommand.ExecuteAsync(null);

        var saved = Assert.Single(repo.SavedConfigs);
        Assert.Equal("1", saved.PosCd);
        Assert.Equal("COM5", saved.PrinterPort);
        Assert.False(saved.DrawerKickEnabled);
    }

    [Fact]
    public async Task Save_ShowsSavedToast()
    {
        var (vm, _, _, _) = Create();
        await vm.LoadAsync();
        var toastValues = new List<string?>();
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(PrinterSettingsViewModel.ToastMessage) && vm.ToastMessage is not null)
                toastValues.Add(vm.ToastMessage);
        };

        await vm.SaveCommand.ExecuteAsync(null);

        Assert.Equal("저장되었습니다", Assert.Single(toastValues));
    }

    [Fact]
    public async Task TestPrint_ShowsNotSupportedYetToast()
    {
        var (vm, _, _, _) = Create();
        await vm.LoadAsync();
        var toastValues = new List<string?>();
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(PrinterSettingsViewModel.ToastMessage) && vm.ToastMessage is not null)
                toastValues.Add(vm.ToastMessage);
        };

        await vm.TestPrintCommand.ExecuteAsync(null);

        Assert.Equal("프린터 연동은 지원 예정입니다", Assert.Single(toastValues));
    }

    [Fact]
    public void GoToSettings_NavigatesBackToSettingsViewModel()
    {
        var (vm, _, navigation, settings) = Create();

        vm.GoToSettingsCommand.Execute(null);

        Assert.Same(settings, navigation.CurrentViewModel);
    }
}
