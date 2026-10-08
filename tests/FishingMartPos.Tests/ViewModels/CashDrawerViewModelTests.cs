using FishingMartPos.Models;
using FishingMartPos.Navigation;
using FishingMartPos.Services;
using FishingMartPos.Tests.Fakes;
using FishingMartPos.ViewModels;
using Xunit;

namespace FishingMartPos.Tests.ViewModels;

public class CashDrawerViewModelTests
{
    private static (CashDrawerViewModel vm, FakeCashDrawerRepository repository, INavigationService navigation, MainMenuViewModel mainMenu)
        Create(IEnumerable<CashDrawerEntry>? seed = null)
    {
        var session = new CurrentSession();
        session.SignIn(
            new Staff { StaffCode = "ADMIN1", StaffName = "관리자", Role = "ADMIN", UseYn = "Y" },
            new PosTerminal { PosCode = "1", PosName = "POS1" });
        var navigation = new NavigationService();
        var mainMenu = new MainMenuViewModel(session, navigation);
        var repository = new FakeCashDrawerRepository(seed);
        var vm = new CashDrawerViewModel(repository, session, new FakeDelayProvider(), navigation, mainMenu);
        return (vm, repository, navigation, mainMenu);
    }

    [Fact]
    public async Task LoadAsync_WithNoEntryForToday_LeavesCountsAndSavedAmountEmpty()
    {
        var (vm, _, _, _) = Create();

        await vm.LoadAsync();

        Assert.Equal(string.Empty, vm.Count1000Input);
        Assert.Equal(string.Empty, vm.Count5000Input);
        Assert.Equal(string.Empty, vm.Count10000Input);
        Assert.Equal(string.Empty, vm.Count50000Input);
        Assert.Null(vm.SavedAmountStr);
        Assert.Equal("0원", vm.TotalAmountStr);
    }

    [Fact]
    public async Task LoadAsync_WithExistingEntryForToday_ShowsSavedAmountButLeavesCountsEmpty()
    {
        // 지폐별 매수는 저장하지 않고 합계만 저장하므로, 이미 저장된 날 다시 들어와도 매수 입력란은
        // 비어 있는 채로 시작하고(다시 세어 입력), 참고용으로 합계만 보여준다.
        var seed = new[]
        {
            new CashDrawerEntry { PosCd = "1", BusinessDate = DateTime.Today, OpeningAmount = 50000, StaffCd = "ADMIN1" },
        };
        var (vm, _, _, _) = Create(seed);

        await vm.LoadAsync();

        Assert.Equal(string.Empty, vm.Count1000Input);
        Assert.Equal("50,000원", vm.SavedAmountStr);
    }

    [Fact]
    public async Task LoadAsync_IgnoresEntryFromADifferentDay()
    {
        var seed = new[]
        {
            new CashDrawerEntry { PosCd = "1", BusinessDate = DateTime.Today.AddDays(-1), OpeningAmount = 30000, StaffCd = "ADMIN1" },
        };
        var (vm, _, _, _) = Create(seed);

        await vm.LoadAsync();

        Assert.Null(vm.SavedAmountStr);
    }

    [Fact]
    public async Task LoadAsync_IgnoresEntryFromADifferentPosTerminal()
    {
        var seed = new[]
        {
            new CashDrawerEntry { PosCd = "2", BusinessDate = DateTime.Today, OpeningAmount = 30000, StaffCd = "ADMIN1" },
        };
        var (vm, _, _, _) = Create(seed);

        await vm.LoadAsync();

        Assert.Null(vm.SavedAmountStr);
    }

    [Theory]
    [InlineData("abc", "")]
    [InlineData("12a3", "123")]
    [InlineData("1.5", "15")]
    [InlineData("-3", "3")]
    public async Task CountInput_StripsNonDigitCharacters(string typed, string expected)
    {
        var (vm, _, _, _) = Create();
        await vm.LoadAsync();

        vm.Count1000Input = typed;

        Assert.Equal(expected, vm.Count1000Input);
    }

    [Fact]
    public async Task TotalAmountStr_UpdatesAutomaticallyAsCountsChange()
    {
        var (vm, _, _, _) = Create();
        await vm.LoadAsync();

        vm.Count1000Input = "3";   // 3,000
        vm.Count5000Input = "2";   // 10,000
        vm.Count10000Input = "1";  // 10,000
        vm.Count50000Input = "1";  // 50,000

        Assert.Equal("73,000원", vm.TotalAmountStr);
    }

    [Fact]
    public async Task TotalAmountStr_WithBlankCounts_IsZero()
    {
        var (vm, _, _, _) = Create();
        await vm.LoadAsync();

        Assert.Equal("0원", vm.TotalAmountStr);
    }

    [Fact]
    public async Task Save_PersistsComputedTotalAndUpdatesSavedAmountAndShowsToast()
    {
        var (vm, repository, _, _) = Create();
        await vm.LoadAsync();
        vm.Count10000Input = "5";   // 50,000
        vm.Count50000Input = "1";   // 50,000
        var toastValues = new List<string?>();
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(CashDrawerViewModel.ToastMessage) && vm.ToastMessage is not null)
                toastValues.Add(vm.ToastMessage);
        };

        await vm.SaveCommand.ExecuteAsync(null);

        var saved = Assert.Single(repository.SavedEntries);
        Assert.Equal("1", saved.PosCd);
        Assert.Equal(DateTime.Today, saved.BusinessDate.Date);
        Assert.Equal(100000m, saved.OpeningAmount);
        Assert.Equal("ADMIN1", saved.StaffCd);
        Assert.Equal("100,000원", vm.SavedAmountStr);
        Assert.Equal("저장되었습니다", Assert.Single(toastValues));
    }

    [Fact]
    public async Task Save_WithAllCountsBlank_SavesZeroSuccessfully()
    {
        // 영업 시작 시 현금이 아예 없을 수도 있으므로(예: 전액 계좌 입금), 0원도 유효한 값이다.
        var (vm, repository, _, _) = Create();
        await vm.LoadAsync();

        await vm.SaveCommand.ExecuteAsync(null);

        var saved = Assert.Single(repository.SavedEntries);
        Assert.Equal(0m, saved.OpeningAmount);
    }

    [Fact]
    public async Task Save_OverwritesPreviousEntryForSameDay()
    {
        var seed = new[]
        {
            new CashDrawerEntry { PosCd = "1", BusinessDate = DateTime.Today, OpeningAmount = 50000, StaffCd = "ADMIN1" },
        };
        var (vm, repository, _, _) = Create(seed);
        await vm.LoadAsync();

        vm.Count10000Input = "7"; // 70,000
        await vm.SaveCommand.ExecuteAsync(null);

        var latest = await repository.GetAsync("1", DateTime.Today);
        Assert.Equal(70000m, latest!.OpeningAmount);
    }

    [Fact]
    public void GoToMainMenu_NavigatesToInjectedMainMenuViewModel()
    {
        var (vm, _, navigation, mainMenu) = Create();

        vm.GoToMainMenuCommand.Execute(null);

        Assert.Same(mainMenu, navigation.CurrentViewModel);
    }
}
