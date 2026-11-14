using Avalonia.Controls;

namespace PCL.Desktop.Linux.Views;

/// <summary>
/// 实例设置栈「概览」页，1:1 对照 `pcl-next/Pages/PageInstance/PageInstanceOverall.xaml`（88 行）：
/// 版本头卡 / 个性化 / 快捷方式 / 高级管理四张卡片的结构与尺寸照抄。
/// 差异登记 R47：Linux 尚无实例数据服务，头卡为空、Mod 文件夹按钮隐藏、下拉选中项与全部按钮动作为静态。
/// </summary>
public partial class PageInstanceOverallView : UserControl
{
    public PageInstanceOverallView()
    {
        InitializeComponent();

        // 对应 `PageInstanceOverall.xaml.vb` L7：Loaded 时回到顶部
        Loaded += (_, _) => PanBack.Offset = new Avalonia.Vector(0, 0);
    }
}
