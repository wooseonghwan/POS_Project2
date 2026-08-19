using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using FishingMartPos.Theme;

namespace FishingMartPos.Converters;

public sealed class CategoryCodeToIconGeometryConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object parameter, CultureInfo culture) =>
        CategoryIcons.GetGeometry(value as string ?? string.Empty);

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
