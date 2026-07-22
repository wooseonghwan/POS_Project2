using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace FishingMartPos.Views;

public partial class SalesReportView : UserControl
{
    public SalesReportView()
    {
        InitializeComponent();
    }

    private void DatePicker_SelectedDateChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DataContext is ViewModels.SalesReportViewModel vm)
        {
            vm.RefreshCommand.Execute(null);
        }
    }

    private void DatePicker_PreviewGotKeyboardFocus(object sender, System.Windows.Input.KeyboardFocusChangedEventArgs e)
    {
        if (sender is DatePicker picker)
        {
            Dispatcher.BeginInvoke(new System.Action(() => picker.IsDropDownOpen = true),
                System.Windows.Threading.DispatcherPriority.Input);
        }
    }

    private void DatePicker_CalendarOpened(object sender, RoutedEventArgs e)
    {
        Dispatcher.BeginInvoke(new System.Action(() =>
        {
            foreach (PresentationSource source in PresentationSource.CurrentSources)
            {
                if (source.RootVisual is DependencyObject root)
                {
                    var calendar = FindVisualChild<Calendar>(root);
                    if (calendar is not null && calendar.LayoutTransform is not ScaleTransform)
                    {
                        calendar.LayoutTransform = new ScaleTransform(1.9, 1.9);
                    }
                }
            }
        }), System.Windows.Threading.DispatcherPriority.Loaded);
    }

    private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
    {
        int childCount = VisualTreeHelper.GetChildrenCount(parent);
        for (int i = 0; i < childCount; i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T typedChild)
            {
                return typedChild;
            }

            var descendant = FindVisualChild<T>(child);
            if (descendant is not null)
            {
                return descendant;
            }
        }

        return null;
    }
}
