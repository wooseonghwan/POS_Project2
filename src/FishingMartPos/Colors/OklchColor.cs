using MediaColor = System.Windows.Media.Color;

namespace FishingMartPos.Colors;

/// <summary>
/// CSS oklch(L C H [/ A]) 색상을 WPF Color(sRGB)로 변환한다.
/// 디자인 원본 HTML이 oklch()를 그대로 쓰고 있어, 수작업 색상 변환 대신
/// 동일한 표준 알고리즘(Björn Ottosson OKLab)으로 런타임에 계산해 색 오차를 없앤다.
/// </summary>
public static class OklchColor
{
    public static MediaColor ToColor(double l, double c, double h, double alpha = 1.0)
    {
        double hRadians = h * Math.PI / 180.0;
        double a = c * Math.Cos(hRadians);
        double b = c * Math.Sin(hRadians);

        double lPrime = l + 0.3963377774 * a + 0.2158037573 * b;
        double mPrime = l - 0.1055613458 * a - 0.0638541728 * b;
        double sPrime = l - 0.0894841775 * a - 1.2914855480 * b;

        double lCubed = lPrime * lPrime * lPrime;
        double mCubed = mPrime * mPrime * mPrime;
        double sCubed = sPrime * sPrime * sPrime;

        double rLinear = 4.0767416621 * lCubed - 3.3077115913 * mCubed + 0.2309699292 * sCubed;
        double gLinear = -1.2684380046 * lCubed + 2.6097574011 * mCubed - 0.3413193965 * sCubed;
        double bLinear = -0.0041960863 * lCubed - 0.7034186147 * mCubed + 1.7076147010 * sCubed;

        byte r = ToSrgbByte(rLinear);
        byte g = ToSrgbByte(gLinear);
        byte bl = ToSrgbByte(bLinear);
        byte a8 = (byte)Math.Clamp(Math.Round(alpha * 255.0), 0.0, 255.0);

        return MediaColor.FromArgb(a8, r, g, bl);
    }

    private static byte ToSrgbByte(double linear)
    {
        double clampedLinear = Math.Clamp(linear, 0.0, 1.0);
        double srgb = clampedLinear <= 0.0031308
            ? clampedLinear * 12.92
            : 1.055 * Math.Pow(clampedLinear, 1.0 / 2.4) - 0.055;

        return (byte)Math.Clamp(Math.Round(srgb * 255.0), 0.0, 255.0);
    }
}
