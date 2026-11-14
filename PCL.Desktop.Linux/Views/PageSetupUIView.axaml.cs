using Avalonia.Controls;

namespace PCL.Desktop.Linux.Views;

/// <summary>
/// 设置栈「个性化」页，1:1 对照 `pcl-next/Pages/PageSetup/PageSetupUI.xaml`（346 行）：
/// 基础（不透明度 / 主题 / 启动图标）、背景图片、背景音乐、标题栏、主页、功能隐藏六张卡片。
/// 行为边界（R29–R32）：Linux 侧尚无设置服务，页面按 `Settings.vb` 默认值呈现静态态；
/// 键值绑定、主题解锁、文件操作、功能隐藏联动均未接入。
/// </summary>
public partial class PageSetupUIView : UserControl
{
    public PageSetupUIView()
    {
        InitializeComponent();
    }
}
