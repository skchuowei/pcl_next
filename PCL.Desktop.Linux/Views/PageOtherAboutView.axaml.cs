using Avalonia.Controls;

namespace PCL.Desktop.Linux.Views;

/// <summary>
/// 更多栈「关于与鸣谢」页，1:1 对照 `pcl-next/Pages/PageOther/PageOtherAbout.xaml`（335 行）：
/// 七张卡片的文案、行列高、按钮尺寸与折叠状态照抄。
/// 差异登记（R44 位图占位 / R45 外部链接与版本信息动作未接入）：
/// Windows 端 `.xaml.vb` L3–L24 只填 `ItemAboutPcl.Info` / `ItemAboutBuild.Info`，
/// 而本 fork 的 `MyListItem` 模板已把 Info 行注释掉（`MyListItem.xaml` L25），故静态页无需赋值。
/// </summary>
public partial class PageOtherAboutView : UserControl
{
    public PageOtherAboutView()
    {
        InitializeComponent();

        // 对应 `PageOtherAbout.xaml.vb` L7：Loaded 时回到顶部
        Loaded += (_, _) => PanBack.Offset = new Avalonia.Vector(0, 0);
    }
}
