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
    public async Task LoadAsync_WithNoEntryForToday_LeavesAmountInputAndSavedAmountEmpty()
    {
        var (vm, _, _, _) = Create();

        await vm.LoadAsync();

        Assert.Equal(string.Empty, vm.AmountInput);
        Assert.Null(vm.SavedAmountStr);
    }

    [Fact]
    public async Task LoadAsync_WithExistingEntryForToday_FillsAmountInputAndSavedAmount()
    {
        var seed = new[]
        {
            new CashDrawerEntry { PosCd = "1", BusinessDate = DateTime.Today, OpeningAmount = 50000, StaffCd = "ADMIN1" },
        };
        var (vm, _, _, _) = Create(seed);

        await vm.LoadAsync();

        Assert.Equal("50,000", vm.AmountInput);
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

        Assert.Equal(string.Empty, vm.AmountInput);
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

        Assert.Equal(string.Empty, vm.AmountInput);
        Assert.Null(vm.SavedAmountStr);
    }

    [Fact]
    public async Task AmountInput_WhenTypedAsPlainDigits_IsReformattedWithCommas()
    {
        var (vm, _, _, _) = Create();
        await vm.LoadAsync();

        vm.AmountInput = "100000";

        Assert.Equal("100,000", vm.AmountInput);
    }

    [Fact]
    public async Task Save_WithValidAmount_PersistsEntryAndUpdatesSavedAmountAndShowsToast()
    {
        var (vm, repository, _, _) = Create();
        await vm.LoadAsync();
        vm.AmountInput = "80000";
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
        Assert.Equal(80000m, saved.OpeningAmount);
        Assert.Equal("ADMIN1", saved.StaffCd);
        Assert.Equal("80,000원", vm.SavedAmountStr);
        Assert.Equal("저장되었습니다", Assert.Single(toastValues));
    }

    [Fact]
    public async Task Save_WithBlankAmount_ShowsErrorAndDoesNotSave()
    {
        var (vm, repository, _, _) = Create();
        await vm.LoadAsync();

        await vm.SaveCommand.ExecuteAsync(null);

        Assert.NotNull(vm.ErrorMessage);
        Assert.Empty(repository.SavedEntries);
    }

    [Fact]
    public async Task Save_WithZeroAmount_SavesSuccessfully()
    {
        // 영업 시작 시 현금이 아예 없을 수도 있으므로(예: 전액 계좌 입금), 0원도 유효한 값이다.
        var (vm, repository, _, _) = Create();
        await vm.LoadAsync();
        vm.AmountInput = "0";

        await vm.SaveCommand.ExecuteAsync(null);

        Assert.Null(vm.ErrorMessage);
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

        vm.AmountInput = "70000";
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
