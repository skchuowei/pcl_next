using Avalonia.Controls;

namespace PCL.Desktop.Linux.Views;

/// <summary>
/// 实例设置栈「设置」页，1:1 对照 `pcl-next/Pages/PageInstance/PageInstanceSetup.xaml`（276 行）：
/// 启动选项 / 内存分配 / 服务器 / 高级选项四张卡的布局、行高、提示与默认选中照抄。
/// 差异登记 R48：SettingService 绑定与校验规则未接入；实例服务缺失（R47），所有输入框与下拉保持静态默认。
/// </summary>
public partial class PageInstanceSetupView : UserControl
{
    public PageInstanceSetupView()
    {
        InitializeComponent();

        // 对应 `PageInstanceSetup.xaml.vb` L7：Loaded 时回到顶部
        Loaded += (_, _) => PanBack.Offset = new Avalonia.Vector(0, 0);
    }
}
