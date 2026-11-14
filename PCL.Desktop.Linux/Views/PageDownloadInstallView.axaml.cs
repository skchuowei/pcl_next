using Avalonia.Controls;

namespace PCL.Desktop.Linux.Views;

/// <summary>
/// 下载栈「原版游戏」页，1:1 对照 `pcl-next/Pages/PageDownload/PageDownloadInstall.xaml`（359 行）：
/// 加载卡片（PanLoad）+ 版本列表容器（PanMinecraft）+ 安装配置子页（PanSelect，默认隐藏）+ 开始下载按钮。
/// 行为边界（R36–R39）：Linux 侧尚无下载服务，页面按 `PageLoaderInit` 的加载态呈现，
/// 版本列表与安装配置均由运行期数据驱动，静态不生成；位图资源以透明占位保留布局。
/// </summary>
public partial class PageDownloadInstallView : UserControl
{
    public PageDownloadInstallView()
    {
        InitializeComponent();
    }
}
