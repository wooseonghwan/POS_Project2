using FishingMartPos.Models;
using FishingMartPos.Navigation;
using FishingMartPos.Services;
using FishingMartPos.Tests.Fakes;
using FishingMartPos.ViewModels;
using Xunit;

namespace FishingMartPos.Tests.ViewModels;

public class StaffListViewModelTests
{
    private static (StaffListViewModel vm, FakeStaffRepository staff, INavigationService navigation, SettingsViewModel settings)
        Create()
    {
        var staff = new FakeStaffRepository(new Dictionary<string, Staff>
        {
            ["0000"] = new() { StaffCode = "ADMIN1", StaffName = "관리자", Role = "ADMIN", UseYn = "Y" },
        });
        var navigation = new NavigationService();
        var settings = new SettingsViewModel(navigation, new MainMenuViewModel(new CurrentSession(), navigation));
        var vm = new StaffListViewModel(staff, navigation, settings)
        {
            StaffFormViewModelFactory = (list, editing) =>
                Task.FromResult(new StaffFormViewModel(staff, new FakeDelayProvider(), navigation, list, editing)),
        };
        return (vm, staff, navigation, settings);
    }

    [Fact]
    public async Task LoadAsync_PopulatesRowsFromRepository()
    {
        var (vm, _, _, _) = Create();

        await vm.LoadAsync();

        var row = Assert.Single(vm.Rows);
        Assert.Equal("ADMIN1", row.StaffCode);
        Assert.Equal("관리자", row.RoleName);
        Assert.Equal("사용", row.UseYnLabel);
    }

    [Fact]
    public async Task GoToAddStaff_NavigatesToStaffFormViewModelInAddMode()
    {
        var (vm, _, navigation, _) = Create();
        await vm.LoadAsync();

        await vm.GoToAddStaffCommand.ExecuteAsync(null);

        var form = Assert.IsType<StaffFormViewModel>(navigation.CurrentViewModel);
        Assert.False(form.IsEditMode);
    }

    [Fact]
    public async Task RowEditCommand_NavigatesToStaffFormViewModelInEditModeForThatStaff()
    {
        var (vm, _, navigation, _) = Create();
        await vm.LoadAsync();
        var row = vm.Rows[0];

        row.EditCommand.Execute(null);
        await Task.Delay(1);

        var form = Assert.IsType<StaffFormViewModel>(navigation.CurrentViewModel);
        Assert.True(form.IsEditMode);
        Assert.Equal("ADMIN1", form.StaffCode);
    }

    [Fact]
    public void GoToSettings_NavigatesBackToSettingsViewModel()
    {
        var (vm, _, navigation, settings) = Create();

        vm.GoToSettingsCommand.Execute(null);

        Assert.Same(settings, navigation.CurrentViewModel);
    }
}
