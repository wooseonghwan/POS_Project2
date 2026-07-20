using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using FishingMartPos.Navigation;

namespace FishingMartPos;

public partial class MainWindow : Window, INotifyPropertyChanged
{
    private readonly INavigationService _navigation;

    public MainWindow(INavigationService navigation)
    {
        InitializeComponent();
        _navigation = navigation;
        _navigation.CurrentViewModelChanged += (_, _) => OnPropertyChanged(nameof(CurrentContent));
        DataContext = this;
    }

    public object? CurrentContent => _navigation.CurrentViewModel;

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
