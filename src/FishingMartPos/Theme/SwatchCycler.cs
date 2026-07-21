using System.Windows.Media;

namespace FishingMartPos.Theme;

public static class SwatchCycler
{
    private static readonly string[] Keys =
    {
        "ProductSwatch0", "ProductSwatch1", "ProductSwatch2", "ProductSwatch3", "ProductSwatch4", "ProductSwatch5",
    };
    private static readonly Brush[] Swatches = Build();

    public static Brush ForIndex(int index) => Swatches[index % Swatches.Length];

    private static Brush[] Build()
    {
        var all = AppColors.BuildBrushes();
        return Keys.Select(key => all[key]).ToArray();
    }
}
