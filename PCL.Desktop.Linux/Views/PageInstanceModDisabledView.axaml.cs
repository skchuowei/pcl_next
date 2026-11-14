using System;
using Avalonia.Controls;

namespace PCL.Desktop.Linux.Views;

/// <summary>
/// 实例设置栈「Mod 管理（不可用）」页，1:1 对照 `pcl-next/Pages/PageInstance/PageInstanceModDisabled.xaml`（30 行）：
/// 静态文案与两个按钮照抄；按钮动作在 Windows 端跳下载栈 / 版本选择栈
/// （`PageInstanceModDisabled.xaml.vb` L10–L15），Linux 侧以事件暴露给外壳接线。
/// </summary>
public partial class PageInstanceModDisabledView : UserControl
{
    /// <summary>「转到下载页面」→ 下载栈（vb L10–L12）。</summary>
    public event EventHandler? DownloadRequested;

    /// <summary>「版本选择」→ 版本选择栈（vb L13–L15）。</summary>
    public event EventHandler? VersionSelectRequested;

    public PageInstanceModDisabledView()
    {
        InitializeComponent();
        BtnDownload.Click += (_, _) => DownloadRequested?.Invoke(this, EventArgs.Empty);
        BtnVersion.Click += (_, _) => VersionSelectRequested?.Invoke(this, EventArgs.Empty);
    }
}
