using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FishingMartPos.Models;
using FishingMartPos.Navigation;
using FishingMartPos.Repositories;
using FishingMartPos.Services;

namespace FishingMartPos.ViewModels;

public sealed partial class StaffFormViewModel : ObservableObject
{
    private readonly IStaffRepository _staffRepository;
    private readonly IDelayProvider _delay;
    private readonly INavigationService _navigation;
    private readonly StaffListViewModel _returnTo;
    private readonly string? _editingStaffCode;
    private readonly IActionLogger _actionLogger;

    private IReadOnlyList<Staff> _allStaff = Array.Empty<Staff>();

    [ObservableProperty] private string _staffCode = string.Empty;
    [ObservableProperty] private string _staffName = string.Empty;
    [ObservableProperty] private RoleOptionViewModel _selectedRole = null!;
    [ObservableProperty] private bool _isActive = true;
    [ObservableProperty] private string _pinInput = string.Empty;
    [ObservableProperty] private string? _errorMessage;
    [ObservableProperty] private string? _toastMessage;

    public IReadOnlyList<RoleOptionViewModel> RoleOptions { get; } = new[]
    {
        new RoleOptionViewModel { Code = "ADMIN", Name = "관리자" },
        new RoleOptionViewModel { Code = "STAFF", Name = "직원" },
    };

    public bool IsEditMode => _editingStaffCode is not null;
    public string HeaderText => IsEditMode ? "직원수정" : "직원등록";
    public string SaveButtonText => IsEditMode ? "저장하기" : "등록하기";

    public StaffFormViewModel(
        IStaffRepository staffRepository,
        IDelayProvider delay,
        INavigationService navigation,
        StaffListViewModel returnTo,
        Staff? editingStaff,
        IActionLogger? actionLogger = null)
    {
        _staffRepository = staffRepository;
        _delay = delay;
        _navigation = navigation;
        _returnTo = returnTo;
        _editingStaffCode = editingStaff?.StaffCode;
        _actionLogger = actionLogger ?? NullActionLogger.Instance;

        SelectedRole = editingStaff is not null
            ? RoleOptions.First(r => r.Code == editingStaff.Role)
            : RoleOptions[1];

        if (editingStaff is not null)
        {
            StaffCode = editingStaff.StaffCode;
            StaffName = editingStaff.StaffName;
            IsActive = editingStaff.UseYn == "Y";
        }
    }

    public async Task LoadAsync()
    {
        _allStaff = await _staffRepository.GetAllAsync();
    }

    [RelayCommand]
    private async Task Save()
    {
        ErrorMessage = null;

        if (string.IsNullOrWhiteSpace(StaffCode))
        {
            ErrorMessage = "직원코드를 입력해주세요";
            return;
        }

        if (string.IsNullOrWhiteSpace(StaffName))
        {
            ErrorMessage = "이름을 입력해주세요";
            return;
        }

        if (!IsEditMode && _allStaff.Any(s => s.StaffCode == StaffCode))
        {
            ErrorMessage = "이미 등록된 직원코드입니다";
            return;
        }

        bool isNewPinProvided = !string.IsNullOrWhiteSpace(PinInput);
        bool pinRequiredNow = !IsEditMode || isNewPinProvided;
        if (pinRequiredNow && !IsValidPin(PinInput))
        {
            ErrorMessage = "PIN은 4자리 숫자로 입력해주세요";
            return;
        }

        var staff = new Staff
        {
            StaffCode = StaffCode,
            StaffName = StaffName,
            Role = SelectedRole.Code,
            UseYn = IsActive ? "Y" : "N",
        };

        if (IsEditMode)
        {
            await _staffRepository.UpdateAsync(staff, isNewPinProvided ? PinInput : null);
        }
        else
        {
            await _staffRepository.CreateAsync(staff, PinInput);
        }

        await _actionLogger.LogAsync("STAFF_SAVE",
            $"{(IsEditMode ? "직원수정" : "직원등록")}: 직원코드={StaffCode}, 이름={StaffName}, 권한={SelectedRole.Code}, 사용={IsActive}");

        ToastMessage = IsEditMode ? "수정되었습니다" : "등록되었습니다";
        await _delay.Delay(TimeSpan.FromMilliseconds(1200));
        ToastMessage = null;

        await GoBackToStaffListAsync();
    }

    [RelayCommand]
    private async Task Cancel() => await GoBackToStaffListAsync();

    private async Task GoBackToStaffListAsync()
    {
        await _returnTo.LoadAsync();
        _navigation.NavigateTo(_returnTo);
    }

    private static bool IsValidPin(string pin) => pin.Length == 4 && pin.All(char.IsDigit);
}
