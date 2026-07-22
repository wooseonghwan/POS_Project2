using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FishingMartPos.Models;
using FishingMartPos.Navigation;
using FishingMartPos.Repositories;

namespace FishingMartPos.ViewModels;

public sealed partial class StaffListViewModel : ObservableObject
{
    private readonly IStaffRepository _staffRepository;
    private readonly INavigationService _navigation;
    private readonly SettingsViewModel _returnTo;

    public ObservableCollection<StaffRowViewModel> Rows { get; } = new();

    /// <summary>App.xaml.cs(또는 테스트)에서 주입하는 StaffFormViewModel 팩토리 — "+ 직원등록"/행별 "수정" 진입 시 사용. staff가 null이면 등록 모드, 있으면 수정 모드.</summary>
    public Func<StaffListViewModel, Staff?, Task<StaffFormViewModel>>? StaffFormViewModelFactory { get; set; }

    public StaffListViewModel(IStaffRepository staffRepository, INavigationService navigation, SettingsViewModel returnTo)
    {
        _staffRepository = staffRepository;
        _navigation = navigation;
        _returnTo = returnTo;
    }

    public async Task LoadAsync()
    {
        var staffList = await _staffRepository.GetAllAsync();

        Rows.Clear();
        foreach (var staff in staffList)
        {
            var captured = staff;
            Rows.Add(new StaffRowViewModel
            {
                StaffCode = captured.StaffCode,
                StaffName = captured.StaffName,
                RoleName = captured.Role == "ADMIN" ? "관리자" : "직원",
                UseYnLabel = captured.UseYn == "Y" ? "사용" : "미사용",
                EditCommand = new AsyncRelayCommand(() => GoToEditStaff(captured)),
            });
        }
    }

    [RelayCommand]
    private async Task GoToAddStaff()
    {
        var formVm = await StaffFormViewModelFactory!.Invoke(this, null);
        await formVm.LoadAsync();
        _navigation.NavigateTo(formVm);
    }

    private async Task GoToEditStaff(Staff staff)
    {
        var formVm = await StaffFormViewModelFactory!.Invoke(this, staff);
        await formVm.LoadAsync();
        _navigation.NavigateTo(formVm);
    }

    [RelayCommand]
    private void GoToSettings() => _navigation.NavigateTo(_returnTo);
}
