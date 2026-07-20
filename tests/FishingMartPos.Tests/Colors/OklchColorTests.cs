using System.Windows.Media;
using FishingMartPos.Colors;
using Xunit;

namespace FishingMartPos.Tests.Colors;

public class OklchColorTests
{
    [Fact]
    public void WhiteAtMaxLightnessZeroChroma()
    {
        Color result = OklchColor.ToColor(1.0, 0.0, 0.0);

        Assert.Equal(255, result.R);
        Assert.Equal(255, result.G);
        Assert.Equal(255, result.B);
        Assert.Equal(255, result.A);
    }

    [Fact]
    public void BlackAtZeroLightnessZeroChroma()
    {
        Color result = OklchColor.ToColor(0.0, 0.0, 0.0);

        Assert.Equal(0, result.R);
        Assert.Equal(0, result.G);
        Assert.Equal(0, result.B);
    }

    [Fact]
    public void HueIsIgnoredWhenChromaIsZero()
    {
        Color atHue0 = OklchColor.ToColor(0.5, 0.0, 0.0);
        Color atHue270 = OklchColor.ToColor(0.5, 0.0, 270.0);

        Assert.Equal(atHue0, atHue270);
    }

    [Fact]
    public void AlphaIsAppliedAsProvided()
    {
        Color result = OklchColor.ToColor(1.0, 0.0, 0.0, 0.4);

        Assert.Equal((byte)Math.Round(0.4 * 255), result.A);
    }

    [Fact]
    public void ChannelsAreClampedToValidRange()
    {
        // 매우 높은 채도(C)는 sRGB 영역을 벗어나므로 0~255로 클램프되어야 한다.
        Color result = OklchColor.ToColor(0.5, 5.0, 195.0);

        Assert.InRange(result.R, (byte)0, (byte)255);
        Assert.InRange(result.G, (byte)0, (byte)255);
        Assert.InRange(result.B, (byte)0, (byte)255);
    }
}
