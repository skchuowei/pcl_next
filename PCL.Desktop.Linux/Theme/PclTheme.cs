using System;
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
            ClampToByte(255 * Hue(l, s, h + 1.0 / 3.0)),
            ClampToByte(255 * Hue(l, s, h)),
            ClampToByte(255 * Hue(l, s, h - 1.0 / 3.0)));
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
/// 主题参数与 `ModSecret.vb` L66 的四个字段一一对应（`ColorHue=210`、`ColorSat=85`、
/// `ColorLightAdjust=0`、`ColorHueTopbarDelta=0`）。
/// 核实结论（计划 §13.8）：Windows 端本 fork 不存在「皮肤编号 → 参数」的映射表，这四个参数
/// 全程无赋值点，`ThemeRefresh(NewTheme)` 只记录 `ThemeNow` 用于早退。故此处同样只保留参数
/// 与早退语义，不自行发明映射；一旦取得映射数据，只需在 <see cref="Refresh"/> 内推导。
/// </summary>
internal static class PclTheme
{
    private static int _colorHue = 210;
    private static int _colorSat = 85;
    private static int _colorLightAdjust = 0;
    private static int _colorHueTopbarDelta = 0;
    private static int _themeNow = -1;

    /// <summary>主题重算后触发，调用方据此重新套用 `PanTitle` / `PanForm` 背景。</summary>
    public static event Action? Changed;

    /// <summary>
    /// `ModSecret.vb` L69–L73 的切换入口与早退语义：同一主题且非负则不再重算。
    /// 返回是否真的重算。
    /// </summary>
    public static bool Refresh(int newTheme = -1)
    {
        if (_themeNow == newTheme && newTheme >= 0) return false;
        if (newTheme >= 0) _themeNow = newTheme;
        Changed?.Invoke();
        return true;
    }

    /// <summary>顶部条背景：水平三段渐变，48 / 54 / 48 亮度（`ModSecret.vb` L116–L121）。</summary>
    public static IBrush CreateTitleBarBrush()
    {
        var deltas = _colorHueTopbarDelta == 0
            ? new[] { 0, 0, 0 }
            : new[] { -_colorHueTopbarDelta, 0, _colorHueTopbarDelta };
        var brush = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(1, 0, RelativeUnit.Relative)
        };
        brush.GradientStops.Add(new GradientStop(PclColorMath.FromHsl2(_colorHue + deltas[0], _colorSat, 48 + _colorLightAdjust), 0));
        brush.GradientStops.Add(new GradientStop(PclColorMath.FromHsl2(_colorHue + deltas[1], _colorSat, 54 + _colorLightAdjust), 0.5));
        brush.GradientStops.Add(new GradientStop(PclColorMath.FromHsl2(_colorHue + deltas[2], _colorSat, 48 + _colorLightAdjust), 1));
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
        brush.GradientStops.Add(new GradientStop(PclColorMath.FromHsl2(_colorHue - 15, _colorSat * 0.8, 91), -0.1));
        brush.GradientStops.Add(new GradientStop(PclColorMath.FromHsl2(_colorHue, _colorSat * 0.8, 91), 0.4));
        brush.GradientStops.Add(new GradientStop(PclColorMath.FromHsl2(_colorHue + 15, _colorSat * 0.8, 91), 1.1));
        return brush;
    }
}
