using PCL.Desktop.Linux.Controls;

namespace PCL.Desktop.Linux.Views;

/// <summary>
/// 版本选择栈左页，1:1 对照 `pcl-next/Pages/PageSelectLeft.xaml`：
/// 固定 250–300 宽的滚动列表容器（`AnimatedControl="PanList"`）。
/// 列表条目由运行期注入；Linux 暂无实例列表服务，当前为空（R26）。
/// </summary>
public partial class PageSelectLeftView : MyPageLeft
{
    public PageSelectLeftView()
    {
        InitializeComponent();
    }
}
