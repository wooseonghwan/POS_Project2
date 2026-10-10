using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FishingMartPos.Models;
using FishingMartPos.Navigation;
using FishingMartPos.Repositories;
using FishingMartPos.Services;

namespace FishingMartPos.ViewModels;

public sealed partial class LoginViewModel : ObservableObject
{
    private readonly IStaffRepository _staffRepository;
    private readonly ICurrentSession _session;
    private readonly INavigationService _navigation;
    private readonly Func<MainMenuViewModel, Task<PosViewModel>> _posViewModelFactory;
    private readonly Func<MainMenuViewModel, Task<InventoryViewModel>> _inventoryViewModelFactory;
    private readonly Func<MainMenuViewModel, Task<SalesReportViewModel>> _salesReportViewModelFactory;
    private readonly Func<MainMenuViewModel, Task<SettingsViewModel>> _settingsViewModelFactory;
    private readonly Func<MainMenuViewModel, Task<PaymentManagementViewModel>> _paymentManagementViewModelFactory;
    private readonly Func<MainMenuViewModel, Task<CashDrawerViewModel>> _cashDrawerViewModelFactory;
    private readonly IActionLogger _actionLogger;

    [ObservableProperty]
    private string _pin = string.Empty;

    [ObservableProperty]
    private bool _isLoginErrorVisible;

    [ObservableProperty]
    private PosTerminal _selectedTerminal;

    public IReadOnlyList<PosTerminal> Terminals { get; }

    public IReadOnlyList<bool> PinDots => Enumerable.Range(0, 4).Select(i => i < Pin.Length).ToArray();

    partial void OnPinChanged(string value) => OnPropertyChanged(nameof(PinDots));

    public LoginViewModel(
        IStaffRepository staffRepository,
        ICurrentSession session,
        INavigationService navigation,
        IReadOnlyList<PosTerminal> terminals,
        Func<MainMenuViewModel, Task<PosViewModel>> posViewModelFactory,
        Func<MainMenuViewModel, Task<InventoryViewModel>> inventoryViewModelFactory,
        Func<MainMenuViewModel, Task<SalesReportViewModel>> salesReportViewModelFactory,
        Func<MainMenuViewModel, Task<SettingsViewModel>> settingsViewModelFactory,
        Func<MainMenuViewModel, Task<PaymentManagementViewModel>> paymentManagementViewModelFactory,
        Func<MainMenuViewModel, Task<CashDrawerViewModel>> cashDrawerViewModelFactory,
        IActionLogger? actionLogger = null)
    {
        _staffRepository = staffRepository;
        _session = session;
        _navigation = navigation;
        Terminals = terminals;
        _selectedTerminal = terminals[0];
        _posViewModelFactory = posViewModelFactory;
        _inventoryViewModelFactory = inventoryViewModelFactory;
        _salesReportViewModelFactory = salesReportViewModelFactory;
        _settingsViewModelFactory = settingsViewModelFactory;
        _paymentManagementViewModelFactory = paymentManagementViewModelFactory;
        _cashDrawerViewModelFactory = cashDrawerViewModelFactory;
        _actionLogger = actionLogger ?? NullActionLogger.Instance;
    }

    [RelayCommand]
    private void SelectTerminal(PosTerminal terminal)
    {
        SelectedTerminal = terminal;
    }

    [RelayCommand]
    private async Task PressKey(string key)
    {
        switch (key)
        {
            case "CLS":
                Pin = string.Empty;
                IsLoginErrorVisible = false;
                return;
            case "<":
                if (Pin.Length > 0)
                {
                    Pin = Pin[..^1];
                }
                IsLoginErrorVisible = false;
                return;
        }

        if (Pin.Length >= 4)
        {
            return;
        }

        Pin += key;
        IsLoginErrorVisible = false;

        if (Pin.Length == 4)
        {
            await SubmitAsync();
        }
    }

    private async Task SubmitAsync()
    {
        Staff? staff = await _staffRepository.FindByPinAsync(Pin);

        if (staff is null)
        {
            // 어떤 PIN으로 로그인에 실패했는지는 남기지 않는다(PIN 자체가 민감정보) — 실패했다는 사실과
            // 선택된 단말만 남긴다.
            await _actionLogger.LogAsync("LOGIN", $"로그인 실패 (단말={SelectedTerminal.PosName})", success: false);
            IsLoginErrorVisible = true;
            Pin = string.Empty;
            return;
        }

        _session.SignIn(staff, SelectedTerminal);
        await _actionLogger.LogAsync("LOGIN", $"로그인 성공: {staff.StaffName}({staff.StaffCode}), 단말={SelectedTerminal.PosName}");

        var mainMenuViewModel = new MainMenuViewModel(_session, _navigation, _actionLogger)
        {
            LoginViewModelFactory = () => new LoginViewModel(_staffRepository, _session, _navigation, Terminals, _posViewModelFactory, _inventoryViewModelFactory, _salesReportViewModelFactory, _settingsViewModelFactory, _paymentManagementViewModelFactory, _cashDrawerViewModelFactory, _actionLogger),
            PosViewModelFactory = _posViewModelFactory,
            InventoryViewModelFactory = _inventoryViewModelFactory,
            SalesReportViewModelFactory = _salesReportViewModelFactory,
            SettingsViewModelFactory = _settingsViewModelFactory,
            PaymentManagementViewModelFactory = _paymentManagementViewModelFactory,
            CashDrawerViewModelFactory = _cashDrawerViewModelFactory,
        };
        _navigation.NavigateTo(mainMenuViewModel);
    }
}
