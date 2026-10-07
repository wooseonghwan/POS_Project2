using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace FishingMartPos.Converters;

/// <summary>
/// 상품 목록 영역의 너비를 4등분한 칸 너비를 돌려준다(세로 스크롤바 폭은 미리 뺀다).
/// 상품 수와 상관없이 한 줄에 항상 4개가 같은 간격으로 놓이게 하기 위해 사용한다.
/// </summary>
public sealed class QuarterWidthConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not double width) return 0d;
        return Math.Max(0d, (width - SystemParameters.VerticalScrollBarWidth) / 4d);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
