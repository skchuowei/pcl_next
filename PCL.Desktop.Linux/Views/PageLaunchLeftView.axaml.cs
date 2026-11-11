using System;
using PCL.Desktop.Linux.Controls;

namespace PCL.Desktop.Linux.Views;

/// <summary>
/// 启动栈左页，1:1 对照 `pcl-next/Pages/PageLaunch/PageLaunchLeft.xaml(.xaml.vb)`。
/// 结构：版本选择 / 版本设置按钮、登录区容器、版本类型胶囊、登录方式单选、
/// 启动大按钮 + 版本名、启动中整页（`PanLaunching`）。
/// 行为边界（R24）：Linux 侧尚无实例列表 / 启动服务，静态呈现 Windows「加载完成但无可用版本」的
/// Case 2（`PageLaunchLeft.xaml.vb` L671–L677）；两个按钮只做栈跳转，不发起真实加载 / 启动。
/// </summary>
public partial class PageLaunchLeftView : MyPageLeft
{
    /// <summary>点「版本选择」→ 切到版本选择栈（Windows 端 `BtnVersion_Click`）。</summary>
    public event EventHandler? VersionSelectRequested;

    /// <summary>点「下载游戏」→ 切到下载栈（Windows 端 Case 2 的启动按钮语义）。</summary>
    public event EventHandler? DownloadRequested;

    public PageLaunchLeftView()
    {
        InitializeComponent();

        BtnVersion.Click += (_, _) => VersionSelectRequested?.Invoke(this, EventArgs.Empty);
        BtnLaunch.Click += (_, _) => DownloadRequested?.Invoke(this, EventArgs.Empty);

        // 版本设置 / 取消：对应 Windows 的版本设置栈与启动取消流程，均未移植（R24）
        BtnMore.Click += (_, _) => { };
        BtnCancel.Click += (_, _) => { };
    }
}
