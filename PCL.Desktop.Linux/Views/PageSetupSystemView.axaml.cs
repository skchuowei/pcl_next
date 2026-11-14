using Avalonia.Controls;

namespace PCL.Desktop.Linux.Views;

/// <summary>
/// 设置栈「其他」页，1:1 对照 `pcl-next/Pages/PageSetup/PageSetupSystem.xaml`（205 行）：
/// 下载 / 社区资源 / 辅助功能 / 启动器四张卡片 + 默认折叠的「调试选项」。
/// 行为边界（R33–R35）：Linux 侧尚无设置服务与更新服务，页面按 `Settings.vb` 默认值呈现静态态；
/// 键值绑定、按钮动作、线程数警告、下拉末项确认回滚、输入框校验规则均未接入。
/// </summary>
public partial class PageSetupSystemView : UserControl
{
    public PageSetupSystemView()
    {
        InitializeComponent();
    }
}
