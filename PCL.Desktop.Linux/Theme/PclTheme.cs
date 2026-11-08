using Avalonia;
using Avalonia.Media;

namespace PCL.Desktop.Linux.Theme;

/// <summary>
/// 1:1 移植 `pcl-next/Modules/Base/ModBase.vb` 的 `MyColor.FromHSL2` / `FromHSL` / `Hue`（L360–L409），
/// 取整口径同 `MyColor.ClampToByte`（L284–L288，`Math.Round`）。
/// 用途：标题栏与页面背景的渐变在 Windows 端是**运行期按主题色算出**的（`ModSecret.vb` L111–L135），
/// 没有静态色值可抄，必须用同一算法才能得到同一结果。
/// </summary>
internal static class PclColorMath
{
    /// <summary>`MyColor.FromHSL2`：PCL 自定义 HSL（按色相修正中间调亮度）。</summary>
    public static Color FromHsl2(double sH, double sS, double sL)
    {
        if (sS == 0)
        {
            var gray = ClampToByte(sL * 2.55);
            return Color.FromArgb(255, gray, gray, gray);
        }

        sH = (sH + 3600000) % 360;

        // 各色相分片的亮度修正量（与原数组逐项一致，末尾两位与前两位重复）
        double[] cent =
        {
            +0.1, -0.06, -0.3,   // 0, 30, 60
            -0.19, -0.15, -0.24, // 90, 120, 150
            -0.32, -0.09, +0.18, // 180, 210, 240
            +0.05, -0.12, -0.02, // 270, 300, 330
            +0.1, -0.06
        };

        var center = sH / 30.0;
        var intCenter = (int)Math.Floor(center);
        center = 50 - ((1 - center + intCenter) * cent[intCenter] + (center - intCenter) * cent[intCenter + 1]) * sS;
        sL = (sL < center ? sL / center : 1 + (sL - center) / (100 - center)) * 50;
        return FromHsl(sH, sS, sL);
    }

    /// <summary>`MyColor.FromHSL`：标准 HSL 转 RGB（S、L 取百分数）。</summary>
    public static Color FromHsl(double sH, double sS, double sL)
    {
        if (sS == 0)
        {
            var gray = ClampToByte(sL * 2.55);
            return Color.FromArgb(255, gray, gray, gray);
        }

        var h = sH / 360;
        var s = sS / 100;
        var l = sL / 100;
        s = l < 0.5 ? s * l + l : s * (1.0 - l) + l;
        l = 2 * l - s;
        return Color.FromArgb(
            255,
            ClampToByte(255 * Hue(l, s, h + 1 / 3)),
            ClampToByte(255 * Hue(l, s, h)),
            ClampToByte(255 * Hue(l, s, h - 1 / 3)));
    }

    private static double Hue(double v1, double v2, double vH)
    {
        if (vH < 0) vH += 1;
        if (vH > 1) vH -= 1;
        if (vH < 0.16667) return v1 + (v2 - v1) * 6 * vH;
        if (vH < 0.5) return v2;
        if (vH < 0.66667) return v1 + (v2 - v1) * (4 - vH * 6);
        return v1;
    }

    private static byte ClampToByte(double d)
    {
        if (d < 0) d = 0;
        if (d > 255) d = 255;
        return (byte)Math.Round(d);
    }
}

/// <summary>
/// 外壳与页面底色，照抄 `pcl-next/Modules/ModSecret.vb` L111–L135（`ThemeRefreshMain`）。
/// 主题参数取 Windows 端默认值（`ModSecret.vb` L66：`ColorHue=210`、`ColorSat=85`、
/// `ColorLightAdjust=0`、`ColorHueTopbarDelta=0`，`UiLauncherTheme` 默认 0 见 `Settings.vb` L144），
/// 主题色自定义未接入（登记 R17）。
/// </summary>
internal static class PclTheme
{
    private const int ColorHue = 210;
    private const int ColorSat = 85;
    private const int ColorLightAdjust = 0;
    private const int ColorHueTopbarDelta = 0;

    /// <summary>顶部条背景：水平三段渐变，48 / 54 / 48 亮度（`ModSecret.vb` L116–L121）。</summary>
    public static IBrush CreateTitleBarBrush()
    {
        var deltas = ColorHueTopbarDelta == 0
            ? new[] { 0, 0, 0 }
            : new[] { -ColorHueTopbarDelta, 0, ColorHueTopbarDelta };
        var brush = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(1, 0, RelativeUnit.Relative)
        };
        brush.GradientStops.Add(new GradientStop(PclColorMath.FromHsl2(ColorHue + deltas[0], ColorSat, 48 + ColorLightAdjust), 0));
        brush.GradientStops.Add(new GradientStop(PclColorMath.FromHsl2(ColorHue + deltas[1], ColorSat, 54 + ColorLightAdjust), 0.5));
        brush.GradientStops.Add(new GradientStop(PclColorMath.FromHsl2(ColorHue + deltas[2], ColorSat, 48 + ColorLightAdjust), 1));
        return brush;
    }

    /// <summary>主页面背景：`UiBackgroundColorful`（默认 True）时的斜向三段渐变（L124–L129）。</summary>
    public static IBrush CreateContentBrush(bool colorful = true)
    {
        if (!colorful) return new SolidColorBrush(Color.FromRgb(245, 245, 245));

        var brush = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0.9, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(0.1, 1, RelativeUnit.Relative)
        };
        brush.GradientStops.Add(new GradientStop(PclColorMath.FromHsl2(ColorHue - 15, ColorSat * 0.8, 91), -0.1));
        brush.GradientStops.Add(new GradientStop(PclColorMath.FromHsl2(ColorHue, ColorSat * 0.8, 91), 0.4));
        brush.GradientStops.Add(new GradientStop(PclColorMath.FromHsl2(ColorHue + 15, ColorSat * 0.8, 91), 1.1));
        return brush;
    }
}
