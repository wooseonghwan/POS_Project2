using FishingMartPos.Models;
using FishingMartPos.Navigation;
using FishingMartPos.Services;
using FishingMartPos.Tests.Fakes;
using FishingMartPos.ViewModels;
using Xunit;

namespace FishingMartPos.Tests.ViewModels;

public class StaffFormViewModelTests
{
    private static (StaffFormViewModel vm, FakeStaffRepository staff, INavigationService navigation, StaffListViewModel listVm)
        CreateForAdd()
    {
        var staff = new FakeStaffRepository(new Dictionary<string, Staff>
        {
            ["0000"] = new() { StaffCode = "ADMIN1", StaffName = "관리자", Role = "ADMIN", UseYn = "Y" },
        });
        var navigation = new NavigationService();
        var settings = new SettingsViewModel(navigation, new MainMenuViewModel(new CurrentSession(), navigation));
        var listVm = new StaffListViewModel(staff, navigation, settings);
        var vm = new StaffFormViewModel(staff, new FakeDelayProvider(), navigation, listVm, editingStaff: null);
        return (vm, staff, navigation, listVm);
    }

    private static (StaffFormViewModel vm, FakeStaffRepository staff, INavigationService navigation, StaffListViewModel listVm)
        CreateForEdit(Staff editing)
    {
        var staff = new FakeStaffRepository(new Dictionary<string, Staff> { ["0000"] = editing });
        var navigation = new NavigationService();
        var settings = new SettingsViewModel(navigation, new MainMenuViewModel(new CurrentSession(), navigation));
        var listVm = new StaffListViewModel(staff, navigation, settings);
        var vm = new StaffFormViewModel(staff, new FakeDelayProvider(), navigation, listVm, editingStaff: editing);
        return (vm, staff, navigation, listVm);
    }

    [Fact]
    public async Task LoadAsync_AddMode_StartsWithEmptyFormAndStaffRoleSelected()
    {
        var (vm, _, _, _) = CreateForAdd();

        await vm.LoadAsync();

        Assert.False(vm.IsEditMode);
        Assert.Equal(string.Empty, vm.StaffCode);
        Assert.Equal("STAFF", vm.SelectedRole.Code);
        Assert.True(vm.IsActive);
    }

    [Fact]
    public async Task LoadAsync_EditMode_FillsFormFromExistingStaff()
    {
        var editing = new Staff { StaffCode = "STAFF1", StaffName = "김직원", Role = "STAFF", UseYn = "N" };
        var (vm, _, _, _) = CreateForEdit(editing);

        await vm.LoadAsync();

        Assert.True(vm.IsEditMode);
        Assert.Equal("STAFF1", vm.StaffCode);
        Assert.Equal("김직원", vm.StaffName);
        Assert.Equal("STAFF", vm.SelectedRole.Code);
        Assert.False(vm.IsActive);
    }

    [Fact]
    public async Task Save_AddMode_WithoutStaffCode_ShowsErrorAndDoesNotCreate()
    {
        var (vm, staff, _, _) = CreateForAdd();
        await vm.LoadAsync();
        vm.StaffName = "김신입";
        vm.PinInput = "1234";

        await vm.SaveCommand.ExecuteAsync(null);

        Assert.NotNull(vm.ErrorMessage);
        Assert.Empty(staff.CreatedStaff);
    }

    [Fact]
    public async Task Save_AddMode_WithoutValidPin_ShowsErrorAndDoesNotCreate()
    {
        var (vm, staff, _, _) = CreateForAdd();
        await vm.LoadAsync();
        vm.StaffCode = "STAFF2";
        vm.StaffName = "김신입";
        vm.PinInput = "12";

        await vm.SaveCommand.ExecuteAsync(null);

        Assert.NotNull(vm.ErrorMessage);
        Assert.Empty(staff.CreatedStaff);
    }

    [Fact]
    public async Task Save_AddMode_WithDuplicateStaffCode_ShowsErrorAndDoesNotCreate()
    {
        var (vm, staff, _, _) = CreateForAdd();
        await vm.LoadAsync();
        vm.StaffCode = "ADMIN1";
        vm.StaffName = "김신입";
        vm.PinInput = "1234";

        await vm.SaveCommand.ExecuteAsync(null);

        Assert.Equal("이미 등록된 직원코드입니다", vm.ErrorMessage);
        Assert.Empty(staff.CreatedStaff);
    }

    [Fact]
    public async Task Save_AddMode_WithValidInput_CreatesStaffAndNavigatesBack()
    {
        var (vm, staff, navigation, listVm) = CreateForAdd();
        await vm.LoadAsync();
        vm.StaffCode = "STAFF2";
        vm.StaffName = "김신입";
        vm.PinInput = "1234";

        await vm.SaveCommand.ExecuteAsync(null);

        var created = Assert.Single(staff.CreatedStaff);
        Assert.Equal("STAFF2", created.Staff.StaffCode);
        Assert.Equal("1234", created.Pin);
        Assert.Same(listVm, navigation.CurrentViewModel);
    }

    [Fact]
    public async Task Save_EditMode_WithBlankPin_UpdatesWithoutChangingPin()
    {
        var editing = new Staff { StaffCode = "STAFF1", StaffName = "김직원", Role = "STAFF", UseYn = "Y" };
        var (vm, staff, _, _) = CreateForEdit(editing);
        await vm.LoadAsync();
        vm.StaffName = "김직원2";

        await vm.SaveCommand.ExecuteAsync(null);

        var updated = Assert.Single(staff.UpdatedStaff);
        Assert.Equal("김직원2", updated.Staff.StaffName);
        Assert.Null(updated.NewPin);
    }

    [Fact]
    public async Task Save_EditMode_WithInvalidPin_ShowsErrorAndDoesNotUpdate()
    {
        var editing = new Staff { StaffCode = "STAFF1", StaffName = "김직원", Role = "STAFF", UseYn = "Y" };
        var (vm, staff, _, _) = CreateForEdit(editing);
        await vm.LoadAsync();
        vm.PinInput = "12";

        await vm.SaveCommand.ExecuteAsync(null);

        Assert.NotNull(vm.ErrorMessage);
        Assert.Empty(staff.UpdatedStaff);
    }

    [Fact]
    public async Task Save_EditMode_WithNewValidPin_UpdatesPin()
    {
        var editing = new Staff { StaffCode = "STAFF1", StaffName = "김직원", Role = "STAFF", UseYn = "Y" };
        var (vm, staff, _, _) = CreateForEdit(editing);
        await vm.LoadAsync();
        vm.PinInput = "5678";

        await vm.SaveCommand.ExecuteAsync(null);

        var updated = Assert.Single(staff.UpdatedStaff);
        Assert.Equal("5678", updated.NewPin);
    }

    [Fact]
    public async Task Save_AddMode_ShowsRegisteredToast()
    {
        var (vm, _, _, _) = CreateForAdd();
        await vm.LoadAsync();
        vm.StaffCode = "STAFF2";
        vm.StaffName = "김신입";
        vm.PinInput = "1234";
        var toastValues = new List<string?>();
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(StaffFormViewModel.ToastMessage) && vm.ToastMessage is not null)
                toastValues.Add(vm.ToastMessage);
        };

        await vm.SaveCommand.ExecuteAsync(null);

        Assert.Equal("등록되었습니다", Assert.Single(toastValues));
    }

    [Fact]
    public async Task Cancel_NavigatesBackToStaffList()
    {
        var (vm, _, navigation, listVm) = CreateForAdd();
        await vm.LoadAsync();

        await vm.CancelCommand.ExecuteAsync(null);

        Assert.Same(listVm, navigation.CurrentViewModel);
    }
}
