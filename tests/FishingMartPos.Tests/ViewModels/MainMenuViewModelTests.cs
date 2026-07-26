using FishingMartPos.Models;
using FishingMartPos.Navigation;
using FishingMartPos.Services;
using FishingMartPos.Tests.Fakes;
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
        var staffRepository = new FakeStaffRepository(new Dictionary<string, Staff>());
        var terminals = new[] { new PosTerminal { PosCode = "1", PosName = "POS1" } };
        var posViewModel = new PosViewModel(
            new FakeProductRepository(Array.Empty<Product>()),
            new FakeCodeRepository(new Dictionary<string, IReadOnlyList<CodeItem>>()),
            new FakeSalesRepository(),
            new FakeHeldOrderRepository(),
            new FakeDelayProvider(),
            session,
            navigation,
            new MainMenuViewModel(session, navigation),
            new FakeVanPaymentGateway(new VanApprovalResult
            {
                IsApproved = true,
                ApprovalNo = "20260723120000",
                VanCode = "KICC",
                ResponseMessage = "카드 결제 완료",
            }),
            new FakeCashReceiptGateway(new CashReceiptResult
            {
                IsIssued = true,
                ApprovalNo = "149331691",
                ApprovalDateYyMmDd = "250704",
                ResponseMessage = "현금영수증 발급 완료",
            }),
            new StubReceiptPrinter());

        Func<MainMenuViewModel, Task<PosViewModel>> posViewModelFactory = _ => Task.FromResult(posViewModel);
        Func<MainMenuViewModel, Task<InventoryViewModel>> inventoryViewModelFactory = mainMenu =>
            Task.FromResult(new InventoryViewModel(
                new FakeProductRepository(Array.Empty<Product>()),
                new FakeCodeRepository(new Dictionary<string, IReadOnlyList<CodeItem>>()),
                session,
                navigation,
                mainMenu));
        Func<MainMenuViewModel, Task<SalesReportViewModel>> salesReportViewModelFactory = mainMenu =>
            Task.FromResult(new SalesReportViewModel(new FakeSalesRepository(), navigation, mainMenu));
        Func<MainMenuViewModel, Task<SettingsViewModel>> settingsViewModelFactory = mainMenu =>
            Task.FromResult(new SettingsViewModel(navigation, mainMenu));
        Func<MainMenuViewModel, Task<PaymentManagementViewModel>> paymentManagementViewModelFactory = mainMenu =>
            Task.FromResult(new PaymentManagementViewModel(
                new FakeSalesRepository(),
                new FakeVanPaymentGateway(new VanApprovalResult { IsApproved = true, ResponseMessage = "ok" }),
                new FakeCashReceiptGateway(new CashReceiptResult { IsIssued = true, ResponseMessage = "ok" }),
                new StubReceiptPrinter(), new FakeDelayProvider(), session, navigation, mainMenu));

        var vm = new MainMenuViewModel(session, navigation)
        {
            LoginViewModelFactory = () => new LoginViewModel(staffRepository, session, navigation, terminals, posViewModelFactory, inventoryViewModelFactory, salesReportViewModelFactory, settingsViewModelFactory, paymentManagementViewModelFactory),
            PosViewModelFactory = posViewModelFactory,
            InventoryViewModelFactory = inventoryViewModelFactory,
            SalesReportViewModelFactory = salesReportViewModelFactory,
            SettingsViewModelFactory = settingsViewModelFactory,
            PaymentManagementViewModelFactory = paymentManagementViewModelFactory,
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
    public async Task GoToSalesReport_AsAdmin_NavigatesToSalesReportViewModel()
    {
        var (vm, _, navigation) = Create();

        await vm.GoToSalesReportCommand.ExecuteAsync(null);

        Assert.IsType<SalesReportViewModel>(navigation.CurrentViewModel);
    }

    [Fact]
    public async Task GoToSalesReport_AsStaff_DoesNothing()
    {
        var session = new CurrentSession();
        session.SignIn(
            new Staff { StaffCode = "STAFF1", StaffName = "직원", Role = "STAFF", UseYn = "Y" },
            new PosTerminal { PosCode = "1", PosName = "POS1" });
        var navigation = new NavigationService();
        var vm = new MainMenuViewModel(session, navigation)
        {
            SalesReportViewModelFactory = mainMenu => Task.FromResult(new SalesReportViewModel(new FakeSalesRepository(), navigation, mainMenu)),
        };

        await vm.GoToSalesReportCommand.ExecuteAsync(null);

        Assert.Null(navigation.CurrentViewModel);
        Assert.False(vm.IsAdmin);
    }

    [Fact]
    public async Task GoToInventory_NavigatesToInventoryViewModel()
    {
        var (vm, _, navigation) = Create();

        await vm.GoToInventoryCommand.ExecuteAsync(null);

        Assert.IsType<InventoryViewModel>(navigation.CurrentViewModel);
    }

    [Fact]
    public async Task GoToSettings_AsAdmin_NavigatesToSettingsViewModel()
    {
        var (vm, _, navigation) = Create();

        await vm.GoToSettingsCommand.ExecuteAsync(null);

        Assert.IsType<SettingsViewModel>(navigation.CurrentViewModel);
    }

    [Fact]
    public async Task GoToSettings_AsStaff_DoesNothing()
    {
        var session = new CurrentSession();
        session.SignIn(
            new Staff { StaffCode = "STAFF1", StaffName = "직원", Role = "STAFF", UseYn = "Y" },
            new PosTerminal { PosCode = "1", PosName = "POS1" });
        var navigation = new NavigationService();
        var vm = new MainMenuViewModel(session, navigation)
        {
            SettingsViewModelFactory = mainMenu => Task.FromResult(new SettingsViewModel(navigation, mainMenu)),
        };

        await vm.GoToSettingsCommand.ExecuteAsync(null);

        Assert.Null(navigation.CurrentViewModel);
        Assert.False(vm.IsAdmin);
    }

    [Fact]
    public async Task GoToPaymentManagement_InvokesFactoryAndNavigates()
    {
        var session = new CurrentSession();
        session.SignIn(
            new Staff { StaffCode = "S1", StaffName = "직원", Role = "STAFF", UseYn = "Y" },
            new PosTerminal { PosCode = "1", PosName = "POS1" });
        var navigation = new NavigationService();
        PaymentManagementViewModel? created = null;
        var vm = new MainMenuViewModel(session, navigation)
        {
            PaymentManagementViewModelFactory = mainMenu =>
            {
                created = new PaymentManagementViewModel(
                    new FakeSalesRepository(),
                    new FakeVanPaymentGateway(new VanApprovalResult { IsApproved = true, ResponseMessage = "ok" }),
                    new FakeCashReceiptGateway(new CashReceiptResult { IsIssued = true, ResponseMessage = "ok" }),
                    new StubReceiptPrinter(), new FakeDelayProvider(), session, navigation, mainMenu);
                return Task.FromResult(created);
            },
        };

        await vm.GoToPaymentManagementCommand.ExecuteAsync(null);

        Assert.NotNull(created);
        Assert.Same(created, navigation.CurrentViewModel);
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
