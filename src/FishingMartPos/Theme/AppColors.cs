using System.Windows.Media;
using FishingMartPos.Colors;

namespace FishingMartPos.Theme;

/// <summary>
/// Fishing Mart POS.html에서 쓰인 oklch(...) 값을 그대로 옮긴 색상 상수.
/// 값을 바꿀 때는 반드시 원본 HTML의 oklch 리터럴과 대조한다.
/// </summary>
public static class AppColors
{
    public static IReadOnlyDictionary<string, Brush> BuildBrushes()
    {
        var map = new Dictionary<string, (double L, double C, double H, double A)>
        {
            ["Accent"] = (0.5, 0.1, 195, 1.0),
            ["AccentDark"] = (0.4, 0.1, 195, 1.0),
            ["OuterBackground"] = (0.93, 0.02, 195, 1.0),
            ["CardBackground"] = (0.99, 0.002, 250, 1.0),
            ["CardBorder"] = (0.75, 0.01, 250, 1.0),
            ["LoginBackground"] = (0.97, 0.01, 195, 1.0),
            ["TitleText"] = (0.22, 0.02, 250, 1.0),
            ["SubtitleText"] = (0.5, 0.01, 250, 1.0),
            ["MutedText"] = (0.4, 0.01, 250, 1.0),
            ["InputBorder"] = (0.8, 0.01, 250, 1.0),
            ["PinDotInactive"] = (0.88, 0.006, 250, 1.0),
            ["KeypadSpecialBackground"] = (0.93, 0.01, 60, 1.0),
            ["KeypadText"] = (0.3, 0.02, 250, 1.0),
            ["ModalOverlay"] = (0.2, 0.02, 250, 0.4),
            ["ErrorBorder"] = (0.6, 0.15, 25, 1.0),
            ["ErrorIconBackground"] = (0.55, 0.15, 25, 1.0),
            ["HeaderBorder"] = (0.91, 0.006, 250, 1.0),
            ["HeaderButtonBackground"] = (0.95, 0.006, 250, 1.0),
            ["HeaderSecondaryText"] = (0.45, 0.01, 250, 1.0),
            ["Divider"] = (0.88, 0.006, 250, 1.0),
            ["LogoutButtonBackground"] = (0.96, 0.006, 250, 1.0),
            ["MenuBackground"] = (0.95, 0.02, 195, 1.0),
            ["MenuTileBorder"] = (0.85, 0.008, 250, 1.0),
            ["MenuTileHoverBackground"] = (0.97, 0.02, 195, 1.0),
            ["MenuTileLabelText"] = (0.25, 0.02, 250, 1.0),
            ["MenuIconSales"] = (0.55, 0.09, 30, 1.0),
            ["MenuIconInventory"] = (0.55, 0.09, 250, 1.0),
            ["MenuIconSettingsDot"] = (0.55, 0.09, 340, 1.0),
            ["MenuIconSettingsTrack"] = (0.7, 0.01, 250, 1.0),
            ["ProductSwatch0"] = (0.55, 0.09, 195, 1.0),
            ["ProductSwatch1"] = (0.55, 0.09, 250, 1.0),
            ["ProductSwatch2"] = (0.55, 0.09, 30, 1.0),
            ["ProductSwatch3"] = (0.55, 0.09, 340, 1.0),
            ["ProductSwatch4"] = (0.55, 0.09, 150, 1.0),
            ["ProductSwatch5"] = (0.55, 0.09, 80, 1.0),
        };

        var brushes = new Dictionary<string, Brush>(map.Count);
        foreach (var (name, value) in map)
        {
            var color = OklchColor.ToColor(value.L, value.C, value.H, value.A);
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            brushes[name] = brush;
        }

        return brushes;
    }
}
