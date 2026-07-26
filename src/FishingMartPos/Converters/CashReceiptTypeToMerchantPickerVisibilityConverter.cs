using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace FishingMartPos.Converters;

public sealed class CashReceiptTypeToMerchantPickerVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object parameter, CultureInfo culture) =>
        value is string type && type != "NONE" ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
