using System;
using Avalonia.Controls;

namespace PCL.Desktop.Linux.Views;

/// <summary>
/// 版本选择栈右页，1:1 对照 `pcl-next/Pages/PageSelectRight.xaml`：
/// 版本卡片列表 / 「无可用版本」空态卡片 / 加载卡片三态叠放。
/// Linux 侧无实例列表服务，固定显示空态（R26）；空态按钮跳下载栈。
/// </summary>
public partial class PageSelectRightView : UserControl
{
    /// <summary>点「下载游戏」→ 切到下载栈（`PageSelectRight.xaml.vb` L187–L189 `BtnEmptyDownload_Click`）。</summary>
    public event EventHandler? DownloadRequested;

    public PageSelectRightView()
    {
        InitializeComponent();
        BtnEmptyDownload.Click += (_, _) => DownloadRequested?.Invoke(this, EventArgs.Empty);
    }
}
