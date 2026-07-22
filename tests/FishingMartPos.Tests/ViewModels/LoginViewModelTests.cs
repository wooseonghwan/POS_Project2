using FishingMartPos.Models;
using FishingMartPos.Navigation;
using FishingMartPos.Services;
using FishingMartPos.Tests.Fakes;
using FishingMartPos.ViewModels;
using Xunit;

namespace FishingMartPos.Tests.ViewModels;

public class LoginViewModelTests
{
    private static Staff AdminStaff => new()
    {
        StaffCode = "ADMIN1", StaffName = "관리자", Role = "ADMIN", UseYn = "Y"
    };

    private static LoginViewModel CreateViewModel(
        out ICurrentSession session,
        out INavigationService navigation,
        Dictionary<string, Staff>? staffByPin = null)
    {
        var repository = new FakeStaffRepository(staffByPin ?? new Dictionary<string, Staff> { ["0000"] = AdminStaff });
        session = new CurrentSession();
        navigation = new NavigationService();
        var terminals = new[]
        {
            new PosTerminal { PosCode = "1", PosName = "POS1" },
            new PosTerminal { PosCode = "2", PosName = "POS2" },
        };

        return new LoginViewModel(
            repository, session, navigation, terminals,
            CreateDummyPosViewModelFactory(session, navigation),
            CreateDummyInventoryViewModelFactory(session, navigation),
            CreateDummySalesReportViewModelFactory(session, navigation));
    }

    private static Func<MainMenuViewModel, Task<PosViewModel>> CreateDummyPosViewModelFactory(
        ICurrentSession session, INavigationService navigation) =>
        mainMenu => Task.FromResult(new PosViewModel(
            new FakeProductRepository(Array.Empty<Product>()),
            new FakeCodeRepository(new Dictionary<string, IReadOnlyList<CodeItem>>()),
            new FakeSalesRepository(),
            new FakeHeldOrderRepository(),
            new FakeDelayProvider(),
            session,
            navigation,
            mainMenu));

    private static Func<MainMenuViewModel, Task<InventoryViewModel>> CreateDummyInventoryViewModelFactory(
        ICurrentSession session, INavigationService navigation) =>
        mainMenu => Task.FromResult(new InventoryViewModel(
            new FakeProductRepository(Array.Empty<Product>()),
            new FakeCodeRepository(new Dictionary<string, IReadOnlyList<CodeItem>>()),
            session,
            navigation,
            mainMenu));

    private static Func<MainMenuViewModel, Task<SalesReportViewModel>> CreateDummySalesReportViewModelFactory(
        ICurrentSession session, INavigationService navigation) =>
        mainMenu => Task.FromResult(new SalesReportViewModel(new FakeSalesRepository(), navigation, mainMenu));

    [Fact]
    public void PressingDigits_BuildsPinString()
    {
        // 4자리를 다 채우면 자동 제출이 동기적으로(Fake는 즉시 완료) 실행되어 Pin이 초기화되므로,
        // 자동 제출 트리거 전인 3자리까지만 눌러 입력 누적 자체를 검증한다.
        var vm = CreateViewModel(out _, out _);

        vm.PressKeyCommand.Execute("1");
        vm.PressKeyCommand.Execute("2");
        vm.PressKeyCommand.Execute("3");

        Assert.Equal("123", vm.Pin);
    }

    [Fact]
    public void PressingBackspace_RemovesLastDigit()
    {
        var vm = CreateViewModel(out _, out _);
        vm.PressKeyCommand.Execute("1");
        vm.PressKeyCommand.Execute("2");

        vm.PressKeyCommand.Execute("<");

        Assert.Equal("1", vm.Pin);
    }

    [Fact]
    public void PressingClear_EmptiesPin()
    {
        var vm = CreateViewModel(out _, out _);
        vm.PressKeyCommand.Execute("1");
        vm.PressKeyCommand.Execute("2");

        vm.PressKeyCommand.Execute("CLS");

        Assert.Equal(string.Empty, vm.Pin);
    }

    [Fact]
    public async Task FourCorrectDigits_SignsInAndNavigatesToMainMenu()
    {
        var vm = CreateViewModel(out var session, out var navigation);

        vm.PressKeyCommand.Execute("0");
        vm.PressKeyCommand.Execute("0");
        vm.PressKeyCommand.Execute("0");
        vm.PressKeyCommand.Execute("0");
        await Task.Delay(50); // 4번째 입력 시 내부적으로 비동기 제출이 걸리므로 완료를 기다린다

        Assert.True(session.IsSignedIn);
        Assert.Equal("ADMIN1", session.CurrentStaff?.StaffCode);
        Assert.IsType<MainMenuViewModel>(navigation.CurrentViewModel);
    }

    [Fact]
    public async Task FourWrongDigits_ShowsErrorAndClearsPin()
    {
        var vm = CreateViewModel(out var session, out var navigation);

        vm.PressKeyCommand.Execute("9");
        vm.PressKeyCommand.Execute("9");
        vm.PressKeyCommand.Execute("9");
        vm.PressKeyCommand.Execute("9");
        await Task.Delay(50);

        Assert.False(session.IsSignedIn);
        Assert.Null(navigation.CurrentViewModel);
        Assert.True(vm.IsLoginErrorVisible);
        Assert.Equal(string.Empty, vm.Pin);
    }

    [Fact]
    public void SelectingTerminal_UpdatesSelectedTerminal()
    {
        var vm = CreateViewModel(out _, out _);

        vm.SelectTerminalCommand.Execute(vm.Terminals[1]);

        Assert.Equal("2", vm.SelectedTerminal.PosCode);
    }
}
