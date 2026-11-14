using Avalonia.Controls;

namespace PCL.Desktop.Linux.Views;

/// <summary>
/// 更多栈「百宝箱」页，1:1 对照 `pcl-next/Pages/PageOther/PageOtherTest.xaml`（12 行）：
/// 整页只有居中提示「为便于维护，开源内容中不包含百宝箱功能……」。
/// Windows 端 `.xaml.vb` 的同名方法全部返回同一提示（Hint），Linux 侧无入口，仅保留静态页。
/// </summary>
public partial class PageOtherTestView : UserControl
{
    public PageOtherTestView()
    {
        InitializeComponent();
    }
}
