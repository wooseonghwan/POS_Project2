using FishingMartPos.Models;
using FishingMartPos.Navigation;
using FishingMartPos.Services;
using FishingMartPos.ViewModels;
using Xunit;

namespace FishingMartPos.Tests.ViewModels;

public class MainMenuViewModelTests
{
    private static (MainMenuViewModel vm, ICurrentSession session, INavigationService navigation) Create()
    {
        var session = new CurrentSession();
        session.SignIn(
            new Staff { StaffCode = "ADMIN1", StaffName = "관리자", Role = "ADMIN", UseYn = "Y" },
            new PosTerminal { PosCode = "2", PosName = "POS2" });
        var navigation = new NavigationService();
        var staffRepository = new FishingMartPos.Tests.Fakes.FakeStaffRepository(
            new Dictionary<string, Staff>());
        var terminals = new[] { new PosTerminal { PosCode = "1", PosName = "POS1" } };
        var posViewModel = new PosViewModel(
            new FishingMartPos.Tests.Fakes.FakeProductRepository(Array.Empty<Product>()),
            new FishingMartPos.Tests.Fakes.FakeCodeRepository(new Dictionary<string, IReadOnlyList<CodeItem>>()),
            new FishingMartPos.Tests.Fakes.FakeSalesRepository(),
            new FishingMartPos.Tests.Fakes.FakeHeldOrderRepository(),
            new FishingMartPos.Tests.Fakes.FakeDelayProvider(),
            session,
            navigation,
            new MainMenuViewModel(session, navigation));

        Func<MainMenuViewModel, Task<PosViewModel>> posViewModelFactory = _ => Task.FromResult(posViewModel);
        Func<MainMenuViewModel, Task<InventoryViewModel>> inventoryViewModelFactory = mainMenu =>
            Task.FromResult(new InventoryViewModel(
                new FishingMartPos.Tests.Fakes.FakeProductRepository(Array.Empty<Product>()),
                new FishingMartPos.Tests.Fakes.FakeCodeRepository(new Dictionary<string, IReadOnlyList<CodeItem>>()),
                session,
                navigation,
                mainMenu));

        var vm = new MainMenuViewModel(session, navigation)
        {
            LoginViewModelFactory = () => new LoginViewModel(staffRepository, session, navigation, terminals, posViewModelFactory, inventoryViewModelFactory),
            PosViewModelFactory = posViewModelFactory,
            InventoryViewModelFactory = inventoryViewModelFactory,
        };
        return (vm, session, navigation);
    }

    [Fact]
    public void PosLabel_ReflectsCurrentTerminal()
    {
        var (vm, _, _) = Create();

        Assert.Equal("POS2", vm.PosLabel);
    }

    [Fact]
    public async Task GoToSales_NavigatesToPosViewModel()
    {
        var (vm, _, navigation) = Create();

        await vm.GoToSalesCommand.ExecuteAsync(null);

        Assert.IsType<PosViewModel>(navigation.CurrentViewModel);
    }

    [Fact]
    public void GoToSalesReport_NavigatesToPlaceholderWithSalesReportTitle()
    {
        var (vm, _, navigation) = Create();

        vm.GoToSalesReportCommand.Execute(null);

        var target = Assert.IsType<PlaceholderViewModel>(navigation.CurrentViewModel);
        Assert.Equal("매출", target.Title);
    }

    [Fact]
    public async Task GoToInventory_NavigatesToInventoryViewModel()
    {
        var (vm, _, navigation) = Create();

        await vm.GoToInventoryCommand.ExecuteAsync(null);

        Assert.IsType<InventoryViewModel>(navigation.CurrentViewModel);
    }

    [Fact]
    public void GoToSettings_NavigatesToPlaceholderWithSettingsTitle()
    {
        var (vm, _, navigation) = Create();

        vm.GoToSettingsCommand.Execute(null);

        var target = Assert.IsType<PlaceholderViewModel>(navigation.CurrentViewModel);
        Assert.Equal("환경설정", target.Title);
    }

    [Fact]
    public void Logout_ClearsSessionAndNavigatesToLogin()
    {
        var (vm, session, navigation) = Create();

        vm.LogoutCommand.Execute(null);

        Assert.False(session.IsSignedIn);
        Assert.IsType<LoginViewModel>(navigation.CurrentViewModel);
    }
}
