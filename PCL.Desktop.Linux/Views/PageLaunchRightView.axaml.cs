using Avalonia.Controls;

namespace PCL.Desktop.Linux.Views;

/// <summary>
/// 启动栈右页，1:1 对照 `pcl-next/Pages/PageLaunch/PageLaunchRight.xaml`：
/// 快照提示条 + 运行期自定义项容器 + 「启动日志」卡片。
/// 日志文本由启动流程写入 <c>LabLog</c>（对应 Windows 端 `LabLog.Text`），Linux 启动服务尚未接入，
/// 当前保持空文本。
/// </summary>
public partial class PageLaunchRightView : UserControl
{
    public PageLaunchRightView()
    {
        InitializeComponent();
    }
}
