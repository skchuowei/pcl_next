using System;

namespace PCL
{
    /// <summary>
    /// OKLab / OKLCh 感知均匀色彩空间与深色调色板。
    /// 转换矩阵采用 Björn Ottosson 公开的 OKLab 公式，色域映射采用「降低色度直至落入 sRGB」
    /// 的策略；调色板参数参照 PCL2-CE 的 ToneProfile.DefaultDark 亮度 / 色度阶梯。
    /// 本文件为独立实现，不复制 CE 源码、不引入 Wacton.Unicolour 等第三方包。
    /// 详见 docs/plans/计划-深色主题重做-OKLCh.md。
    /// </summary>
    internal static class ModOklch
    {
        /// <summary>深色品牌蓝在 OKLCh 中的色相角（度）。亮色仍走 HSL 的 ColorHue=210。</summary>
        internal const double DarkHue = 255d;

        private static double Cube(double x) => x * x * x;

        private static double Clamp01(double v) => v < 0d ? 0d : (v > 1d ? 1d : v);

        private static double EncodeGamma(double v)
        {
            v = Clamp01(v);
            return v <= 0.0031308d ? 12.92d * v : 1.055d * Math.Pow(v, 1d / 2.4d) - 0.055d;
        }

        private struct LinearRgb
        {
            internal double R;
            internal double G;
            internal double B;

            internal LinearRgb(double r, double g, double b)
            {
                R = r;
                G = g;
                B = b;
            }
        }

        /// <summary>OKLab (L, a, b) → linear sRGB。L/a/b 均为 OKLab 坐标，非 0–255。</summary>
        private static LinearRgb OklabToLinearSrgb(double L, double a, double b)
        {
            double l_ = L + 0.3963377774d * a + 0.2158037573d * b;
            double m_ = L - 0.1055613458d * a - 0.0638541728d * b;
            double s_ = L - 0.0894841775d * a - 1.2914855480d * b;
            double l = Cube(l_);
            double m = Cube(m_);
            double s = Cube(s_);
            return new LinearRgb(
                 4.0767416621d * l - 3.3077115913d * m + 0.2309699292d * s,
                -1.2684380046d * l + 2.6097574011d * m - 0.3413193965d * s,
                -0.0041960863d * l - 0.7034186147d * m + 1.7076147010d * s);
        }

        private static bool IsInGamut(LinearRgb c)
        {
            const double Eps = 1e-4d;
            return c.R >= -Eps && c.R <= 1d + Eps
                && c.G >= -Eps && c.G <= 1d + Eps
                && c.B >= -Eps && c.B <= 1d + Eps;
        }

        /// <summary>
        /// 由 OKLCh 生成 MyColor。lightness / chroma / alpha 取 0–1，hueDegrees 取角度。
        /// 越界时二分降低色度（等价于 CE 的 ReduceChroma），避免直接截断使高饱和色发灰。
        /// </summary>
        internal static ModBase.MyColor FromLch(double lightness, double chroma = 0d, double hueDegrees = 0d, double alpha = 1d)
        {
            double rad = hueDegrees * Math.PI / 180d;
            double cos = Math.Cos(rad);
            double sin = Math.Sin(rad);
            double c = chroma < 0d ? 0d : chroma;
            LinearRgb rgb = OklabToLinearSrgb(lightness, c * cos, c * sin);
            if (c > 0d && !IsInGamut(rgb))
            {
                double low = 0d;
                double high = c;
                double best = 0d;
                for (int i = 0; i < 24; i++)
                {
                    double mid = (low + high) * 0.5d;
                    if (IsInGamut(OklabToLinearSrgb(lightness, mid * cos, mid * sin)))
                    {
                        best = mid;
                        low = mid;
                    }
                    else
                    {
                        high = mid;
                    }
                }
                rgb = OklabToLinearSrgb(lightness, best * cos, best * sin);
            }
            return new ModBase.MyColor(
                Clamp01(alpha) * 255d,
                EncodeGamma(rgb.R) * 255d,
                EncodeGamma(rgb.G) * 255d,
                EncodeGamma(rgb.B) * 255d);
        }

        internal static ModDarkPalette CreateDarkPalette() => new ModDarkPalette();
    }

    /// <summary>
    /// 一整套深色调色板。只负责算颜色，不写资源字典；资源键契约由 ModSecret.ThemeRefresh 保持。
    /// </summary>
    internal sealed class ModDarkPalette
    {
        // 参照 PCL2-CE ToneProfile.DefaultDark 的感知亮度阶梯。
        private const double L1 = 0.96d;
        private const double L2 = 0.75d;
        private const double L3 = 0.60d;
        // CE 的 DefaultDark 为 L3=0.60 / L4=0.65，会让中性灰阶 Gray3→Gray4 亮度倒挂。
        // 此处下调 L4 保证 8 级严格递减；深色下悬停色比主色略暗也是常见处理。
        private const double L4 = 0.58d;
        private const double L5 = 0.45d;
        private const double L6 = 0.25d;
        private const double L7 = 0.225d;
        private const double L8 = 0.20d;
        private const double LBackground = 0.30d;

        // 色度阶沿用 CE 的 C1..C8；CatBlue 主题的 chromaAdj = -0.2，即色度 ×0.8。
        private const double Ch1 = 0.025d;
        private const double Ch2 = 0.188d;
        private const double Ch3 = 0.213d;
        private const double Ch4 = 0.168d;
        private const double Ch5 = 0.093d;
        private const double Ch6 = 0.036d;
        private const double Ch7 = 0.028d;
        private const double Ch8 = 0.018d;
        private const double ChromaScale = 0.8d;

        // 半透明面：卡片 / 提示气泡 / 遮罩 / 侧栏。
        private const double ABackground = 0.824d;
        private const double AToolTip = 0.9d;
        private const double AOverlay = 0.867d;
        private const double ASidebar = 0.953d;

        internal ModBase.MyColor C1 { get; }
        internal ModBase.MyColor C2 { get; }
        internal ModBase.MyColor C3 { get; }
        internal ModBase.MyColor C4 { get; }
        internal ModBase.MyColor C5 { get; }
        internal ModBase.MyColor C6 { get; }
        internal ModBase.MyColor C7 { get; }
        internal ModBase.MyColor C8 { get; }
        internal ModBase.MyColor Gray1 { get; }
        internal ModBase.MyColor Gray2 { get; }
        internal ModBase.MyColor Gray3 { get; }
        internal ModBase.MyColor Gray4 { get; }
        internal ModBase.MyColor Gray5 { get; }
        internal ModBase.MyColor Gray6 { get; }
        internal ModBase.MyColor Gray7 { get; }
        internal ModBase.MyColor Gray8 { get; }
        internal ModBase.MyColor Bg0 { get; }
        internal ModBase.MyColor Card { get; }
        internal ModBase.MyColor Popup { get; }
        internal ModBase.MyColor Tooltip { get; }
        internal ModBase.MyColor Overlay { get; }
        internal ModBase.MyColor Sidebar { get; }
        internal ModBase.MyColor Window { get; }
        internal ModBase.MyColor ScrollBar { get; }
        internal ModBase.MyColor ScrollBarHover { get; }
        internal ModBase.MyColor OnAccent { get; }
        internal ModBase.MyColor ObjectShadow { get; }

        internal ModDarkPalette()
        {
            double h = ModOklch.DarkHue;

            // 彩色阶：同一感知亮度下按色度阶递进，品牌蓝只落在 C2/C3/C4。
            C1 = ModOklch.FromLch(L1, Ch1 * ChromaScale, h);
            C2 = ModOklch.FromLch(L2, Ch2 * ChromaScale, h);
            C3 = ModOklch.FromLch(L3, Ch3 * ChromaScale, h);
            C4 = ModOklch.FromLch(L4, Ch4 * ChromaScale, h);
            C5 = ModOklch.FromLch(L5, Ch5 * ChromaScale, h);
            C6 = ModOklch.FromLch(L6, Ch6 * ChromaScale, h);
            C7 = ModOklch.FromLch(L7, Ch7 * ChromaScale, h);
            C8 = ModOklch.FromLch(L8, Ch8 * ChromaScale, h);

            // 中性灰阶：OKLab L 是感知亮度，等间隔即视觉等间隔，不再手写十六进制。
            Gray1 = ModOklch.FromLch(L1);
            Gray2 = ModOklch.FromLch(L2);
            Gray3 = ModOklch.FromLch(L3);
            Gray4 = ModOklch.FromLch(L4);
            Gray5 = ModOklch.FromLch(L5);
            Gray6 = ModOklch.FromLch(L6);
            Gray7 = ModOklch.FromLch(L7);
            Gray8 = ModOklch.FromLch(L8);

            Bg0 = ModOklch.FromLch(L5, Ch5 * ChromaScale, h);

            // 语义面：逐层抬升，页面底 < 侧栏 < 卡片 < 输入 < 悬停。
            Card = ModOklch.FromLch(LBackground, Ch8 * ChromaScale, h, ABackground);
            Popup = ModOklch.FromLch(0.26d, 0.012d, h);
            Tooltip = ModOklch.FromLch(LBackground + 0.03d, 0.012d, h, AToolTip);
            Overlay = ModOklch.FromLch(0.12d, 0.006d, h, AOverlay);
            Sidebar = ModOklch.FromLch(0.24d, 0.010d, h, ASidebar);
            Window = ModOklch.FromLch(L8, Ch8 * ChromaScale, h);

            ScrollBar = ModOklch.FromLch(0.45d);
            ScrollBarHover = ModOklch.FromLch(0.55d);
            OnAccent = new ModBase.MyColor(255d, 253d, 254d, 255d);
            ObjectShadow = new ModBase.MyColor(255d, 0d, 0d, 0d);
        }
    }
}
