using System.Windows.Controls;
using System.Windows.Threading;
using FishingMartPos.ViewModels;

namespace FishingMartPos.Views;

public partial class MainMenuView : UserControl
{
    private readonly DispatcherTimer _clockTimer;

    public MainMenuView()
    {
        InitializeComponent();

        _clockTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _clockTimer.Tick += (_, _) =>
        {
            if (DataContext is MainMenuViewModel vm)
            {
                vm.Now = DateTime.Now;
            }
        };
        Loaded += (_, _) => _clockTimer.Start();
        Unloaded += (_, _) => _clockTimer.Stop();
    }
}
