using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Effects;
using MeloongCore;

namespace PCL
{
    /// <summary>
    /// 卡片毛玻璃：在窗口背景层之上铺一层「模糊后的背景图」，卡片底色转为半透明后
    /// 即可透出磨砂质感。只用 WPF 公开 API（VisualBrush + BlurEffect），
    /// 不反射 UIElement 私有成员，也不依赖任何第三方控件库。
    /// </summary>
    public static class ModBlur
    {
        private static VisualBrush _backdropBrush;
        private static bool _noBackdropHintShown;

        /// <summary>
        /// 按当前设置应用卡片毛玻璃。可重复调用（幂等），窗口未加载时静默返回。
        /// 半径为 0 时隐藏模糊层，界面与改动前完全一致。
        /// </summary>
        public static void Apply()
        {
            try
            {
                var w = ModMain.FrmMain;
                if (w is null || !w.IsLoaded)
                    return;
                var layer = w.PanBlurBackdrop;
                if (layer is null)
                    return;

                int radius = Settings.Get<int>("UiCardBlur");
                if (radius <= 0)
                {
                    layer.Visibility = Visibility.Collapsed;
                    return;
                }

                // 没有自定义背景图时 ImgBack 是空 Canvas，VisualBrush 拍不到任何内容，
                // 模糊层纯透明：既不浪费一次全屏模糊渲染，也要明确告知用户原因，
                // 避免重演「开关打开了却看不出变化」的体验坑。
                bool hasBackdrop = w.ImgBack.Background is not null
                                   && w.ImgBack.Visibility == Visibility.Visible;
                if (!hasBackdrop)
                {
                    layer.Visibility = Visibility.Collapsed;
                    if (!_noBackdropHintShown)
                    {
                        _noBackdropHintShown = true;
                        ModMain.Hint("卡片毛玻璃需要自定义背景图：请先在设置 → 个性化 → 背景图片中添加一张图片，磨砂效果才会显示。", ModMain.HintType.Blue);
                    }
                    return;
                }

                // VisualBrush 指向窗口背景图 Canvas：两者同在 PanForm 第 1 行、同尺寸，
                // 坐标天然对齐，无需计算 Viewbox 偏移；ImgBack 是叶子 Canvas，不在
                // PanBlurBackdrop 的可视子树里，不会形成循环引用。
                // VisualBrush 是实时的，背景图切换 / UiBackgroundBlur 变化时自动跟随，
                // 不需要快照失效与重拍逻辑；全局只渲染一次源视觉 + 一次模糊。
                if (_backdropBrush is null)
                {
                    _backdropBrush = new VisualBrush(w.ImgBack)
                    {
                        Stretch = Stretch.None,
                        AlignmentX = AlignmentX.Left,
                        AlignmentY = AlignmentY.Top
                    };
                    layer.Background = _backdropBrush;
                }
                layer.Effect = new BlurEffect()
                {
                    Radius = Math.Max(1d, radius),
                    KernelType = KernelType.Gaussian,
                    RenderingBias = RenderingBias.Performance
                };
                layer.Visibility = Visibility.Visible;
            }
            catch (Exception ex)
            {
                // 模糊层是纯装饰，任何异常都只降级为「不显示」，不能影响主界面。
                Logger.Error(ex, "应用卡片毛玻璃失败", LogBehavior.Toast);
            }
        }
    }
}
