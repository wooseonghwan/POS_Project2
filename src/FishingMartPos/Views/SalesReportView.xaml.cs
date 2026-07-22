using System.Windows.Controls;

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
}
