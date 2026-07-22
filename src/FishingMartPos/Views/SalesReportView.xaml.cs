using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace FishingMartPos.Views;

public partial class SalesReportView : UserControl
{
    // Scoped per-picker (not a single shared flag): closing one picker to enforce
    // mutual exclusion below fires ITS OWN CalendarClosed, which must not suppress
    // the OTHER picker's legitimate open.
    private bool _suppressDateFromReopen;
    private bool _suppressDateToReopen;

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
        if (sender is not DatePicker picker)
        {
            return;
        }

        bool isDateFrom = ReferenceEquals(picker, DateFromPicker);

        // Enforce "only one calendar open at a time" ourselves -- WPF does not
        // guarantee this between two independent DatePicker instances.
        var other = isDateFrom ? DateToPicker : DateFromPicker;
        if (other.IsDropDownOpen)
        {
            other.IsDropDownOpen = false;
        }

        Dispatcher.BeginInvoke(new System.Action(() =>
        {
            // Checked at execution time, not schedule time: focus can re-enter the
            // text box as a side effect of the calendar closing itself (date selected),
            // and CalendarClosed may not have set the flag yet when this was scheduled.
            bool suppressed = isDateFrom ? _suppressDateFromReopen : _suppressDateToReopen;
            if (!suppressed)
            {
                picker.IsDropDownOpen = true;
            }

            if (isDateFrom)
            {
                _suppressDateFromReopen = false;
            }
            else
            {
                _suppressDateToReopen = false;
            }
        }), System.Windows.Threading.DispatcherPriority.Input);
    }

    private void DatePicker_CalendarClosed(object sender, RoutedEventArgs e)
    {
        if (ReferenceEquals(sender, DateFromPicker))
        {
            _suppressDateFromReopen = true;
        }
        else if (ReferenceEquals(sender, DateToPicker))
        {
            _suppressDateToReopen = true;
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
