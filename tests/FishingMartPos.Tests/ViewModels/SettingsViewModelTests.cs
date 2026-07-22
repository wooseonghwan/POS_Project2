using FishingMartPos.Navigation;
using FishingMartPos.Services;
using FishingMartPos.Tests.Fakes;
using FishingMartPos.ViewModels;
using Xunit;

namespace FishingMartPos.Tests.ViewModels;

public class SettingsViewModelTests
{
    private static (SettingsViewModel vm, INavigationService navigation, MainMenuViewModel mainMenu) Create()
    {
        var session = new CurrentSession();
        var navigation = new NavigationService();
        var mainMenu = new MainMenuViewModel(session, navigation);
        var staff = new FakeStaffRepository(new Dictionary<string, FishingMartPos.Models.Staff>());
        var vm = new SettingsViewModel(navigation, mainMenu)
        {
            StaffListViewModelFactory = s => Task.FromResult(new StaffListViewModel(staff, navigation, s)),
            PrinterSettingsViewModelFactory = s => Task.FromResult(new PrinterSettingsViewModel(
                new FakePrinterConfigRepository(), session, new FakeDelayProvider(), navigation, s)),
            ReceiptSettingsViewModelFactory = s => Task.FromResult(new ReceiptSettingsViewModel(
                new FakeReceiptConfigRepository(), session, new FakeDelayProvider(), navigation, s)),
            SystemInfoViewModelFactory = s => Task.FromResult(new SystemInfoViewModel(
                new FakeSystemInfoRepository(), session, navigation, s)),
            CodeManageViewModelFactory = s => Task.FromResult(new CodeManageViewModel(
                new FakeCodeRepository(new Dictionary<string, IReadOnlyList<FishingMartPos.Models.CodeItem>>()),
                new FakeProductRepository(Array.Empty<FishingMartPos.Models.Product>()), navigation, s)),
        };
        return (vm, navigation, mainMenu);
    }

    [Fact]
    public async Task GoToStaffList_NavigatesToStaffListViewModel()
    {
        var (vm, navigation, _) = Create();

        await vm.GoToStaffListCommand.ExecuteAsync(null);

        Assert.IsType<StaffListViewModel>(navigation.CurrentViewModel);
    }

    [Fact]
    public async Task GoToPrinterSettings_NavigatesToPrinterSettingsViewModel()
    {
        var (vm, navigation, _) = Create();

        await vm.GoToPrinterSettingsCommand.ExecuteAsync(null);

        Assert.IsType<PrinterSettingsViewModel>(navigation.CurrentViewModel);
    }

    [Fact]
    public async Task GoToReceiptSettings_NavigatesToReceiptSettingsViewModel()
    {
        var (vm, navigation, _) = Create();

        await vm.GoToReceiptSettingsCommand.ExecuteAsync(null);

        Assert.IsType<ReceiptSettingsViewModel>(navigation.CurrentViewModel);
    }

    [Fact]
    public async Task GoToSystemInfo_NavigatesToSystemInfoViewModel()
    {
        var (vm, navigation, _) = Create();

        await vm.GoToSystemInfoCommand.ExecuteAsync(null);

        Assert.IsType<SystemInfoViewModel>(navigation.CurrentViewModel);
    }

    [Fact]
    public async Task GoToCodeManage_NavigatesToCodeManageViewModel()
    {
        var (vm, navigation, _) = Create();

        await vm.GoToCodeManageCommand.ExecuteAsync(null);

        Assert.IsType<CodeManageViewModel>(navigation.CurrentViewModel);
    }

    [Fact]
    public void GoToMainMenu_NavigatesBackToMainMenuViewModel()
    {
        var (vm, navigation, mainMenu) = Create();

        vm.GoToMainMenuCommand.Execute(null);

        Assert.Same(mainMenu, navigation.CurrentViewModel);
    }
}
