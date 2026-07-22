using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FishingMartPos.Navigation;

namespace FishingMartPos.ViewModels;

public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly INavigationService _navigation;
    private readonly MainMenuViewModel _returnTo;

    public SettingsViewModel(INavigationService navigation, MainMenuViewModel returnTo)
    {
        _navigation = navigation;
        _returnTo = returnTo;
    }

    [RelayCommand]
    private void GoToMainMenu() => _navigation.NavigateTo(_returnTo);
}
