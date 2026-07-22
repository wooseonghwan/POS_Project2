using FishingMartPos.Models;
using FishingMartPos.Navigation;
using FishingMartPos.Services;
using FishingMartPos.Tests.Fakes;
using FishingMartPos.ViewModels;
using Xunit;

namespace FishingMartPos.Tests.ViewModels;

public class ReceiptSettingsViewModelTests
{
    private static (ReceiptSettingsViewModel vm, FakeReceiptConfigRepository repo, INavigationService navigation, SettingsViewModel settings)
        Create(ReceiptConfig? existing = null)
    {
        var byPosCd = new Dictionary<string, ReceiptConfig>();
        if (existing is not null) byPosCd[existing.PosCd] = existing;
        var repo = new FakeReceiptConfigRepository(byPosCd);
        var session = new CurrentSession();
        session.SignIn(
            new Staff { StaffCode = "ADMIN1", StaffName = "관리자", Role = "ADMIN", UseYn = "Y" },
            new PosTerminal { PosCode = "1", PosName = "POS1" });
        var navigation = new NavigationService();
        var settings = new SettingsViewModel(navigation, new MainMenuViewModel(session, navigation));
        var vm = new ReceiptSettingsViewModel(repo, session, new FakeDelayProvider(), navigation, settings);
        return (vm, repo, navigation, settings);
    }

    [Fact]
    public async Task LoadAsync_WhenNoConfigExists_UsesEmptyDefaults()
    {
        var (vm, _, _, _) = Create();

        await vm.LoadAsync();

        Assert.Equal(string.Empty, vm.HeaderText);
        Assert.Equal(string.Empty, vm.FooterText);
    }

    [Fact]
    public async Task LoadAsync_WhenConfigExists_FillsFormFromRepository()
    {
        var existing = new ReceiptConfig { PosCd = "1", HeaderText = "환영합니다", FooterText = "감사합니다" };
        var (vm, _, _, _) = Create(existing);

        await vm.LoadAsync();

        Assert.Equal("환영합니다", vm.HeaderText);
        Assert.Equal("감사합니다", vm.FooterText);
    }

    [Fact]
    public async Task Save_PersistsCurrentValuesForCurrentTerminal()
    {
        var (vm, repo, _, _) = Create();
        await vm.LoadAsync();
        vm.HeaderText = "새 상단문구";
        vm.FooterText = "새 하단문구";

        await vm.SaveCommand.ExecuteAsync(null);

        var saved = Assert.Single(repo.SavedConfigs);
        Assert.Equal("1", saved.PosCd);
        Assert.Equal("새 상단문구", saved.HeaderText);
        Assert.Equal("새 하단문구", saved.FooterText);
    }

    [Fact]
    public async Task Save_ShowsSavedToast()
    {
        var (vm, _, _, _) = Create();
        await vm.LoadAsync();
        var toastValues = new List<string?>();
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ReceiptSettingsViewModel.ToastMessage) && vm.ToastMessage is not null)
                toastValues.Add(vm.ToastMessage);
        };

        await vm.SaveCommand.ExecuteAsync(null);

        Assert.Equal("저장되었습니다", Assert.Single(toastValues));
    }

    [Fact]
    public void GoToSettings_NavigatesBackToSettingsViewModel()
    {
        var (vm, _, navigation, settings) = Create();

        vm.GoToSettingsCommand.Execute(null);

        Assert.Same(settings, navigation.CurrentViewModel);
    }
}
