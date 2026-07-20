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
        IReadOnlyList<PosTerminal> terminals)
    {
        _staffRepository = staffRepository;
        _session = session;
        _navigation = navigation;
        Terminals = terminals;
        _selectedTerminal = terminals[0];
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
            IsLoginErrorVisible = true;
            Pin = string.Empty;
            return;
        }

        _session.SignIn(staff, SelectedTerminal);

        var mainMenuViewModel = new MainMenuViewModel(_session, _navigation)
        {
            LoginViewModelFactory = () => new LoginViewModel(_staffRepository, _session, _navigation, Terminals)
        };
        _navigation.NavigateTo(mainMenuViewModel);
    }
}
