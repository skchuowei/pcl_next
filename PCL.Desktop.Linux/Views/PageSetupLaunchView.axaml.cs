using Avalonia.Controls;

namespace PCL.Desktop.Linux.Views;

/// <summary>
/// 设置栈「启动」页，1:1 对照 `pcl-next/Pages/PageSetup/PageSetupLaunch.xaml`（300 行）：
/// 启动选项 / 内存分配 / 离线皮肤（折叠）/ 高级选项（折叠）/「版本独立设置」胶囊按钮。
/// 行为边界（R29）：Linux 侧尚无设置服务，页面为按 `Settings.vb` 默认值呈现的静态态；
/// 设置项绑定、校验规则与运行期联动（窗口尺寸框显隐、皮肤类型分支、内存条比例）未接入。
/// </summary>
public partial class PageSetupLaunchView : UserControl
{
    public PageSetupLaunchView()
    {
        InitializeComponent();
    }
}
