using System.Windows.Media;
using FishingMartPos.Theme;
using Xunit;

namespace FishingMartPos.Tests.Theme;

public class AppColorsTests
{
    [Fact]
    public void ContainsAccentBrush()
    {
        var brushes = AppColors.BuildBrushes();

        Assert.True(brushes.ContainsKey("Accent"));
        Assert.IsType<SolidColorBrush>(brushes["Accent"]);
    }

    [Fact]
    public void AllBrushesAreFrozenForCrossThreadUse()
    {
        var brushes = AppColors.BuildBrushes();

        Assert.All(brushes.Values, brush => Assert.True(brush.IsFrozen));
    }

    [Fact]
    public void ModalOverlayHasReducedAlpha()
    {
        var brushes = AppColors.BuildBrushes();
        var overlay = (SolidColorBrush)brushes["ModalOverlay"];

        Assert.True(overlay.Color.A < 255);
    }

    [Fact]
    public void ContainsAllTwentyNineNamedColors()
    {
        var brushes = AppColors.BuildBrushes();

        Assert.Equal(29, brushes.Count);
    }
}
