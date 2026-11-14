using PCL.Desktop.Linux.Controls;

namespace PCL.Desktop.Linux.Views;

/// <summary>
/// 设置栈左导航，1:1 对照 `pcl-next/Pages/PageSetup/PageSetupLeft.xaml(.vb)`：
/// 四项（启动 / 联机 / 个性化 / 其他）互斥选中，选中项经 `Check` 事件回传子页编号；
/// 右页由宿主按编号切换（启动 / 联机已移植，个性化 / 其他待补，登记 R20）。
/// </summary>
public partial class PageSetupLeftView : MyPageLeft
{
    /// <summary>当前选中的子页编号（对应 `PageSetupLeft` 的 `PageID`，0 启动 / 1 联机 / 2 个性化 / 3 其他）。</summary>
    public int PageId { get; private set; }

    /// <summary>子页切换（对应 `PageSetupLeft.xaml.vb` L61–L65 `PageCheck`）。</summary>
    public event EventHandler<int>? PageChanged;

    public PageSetupLeftView()
    {
        InitializeComponent();

        foreach (var item in new[] { ItemLaunch, ItemLink, ItemUI, ItemSystem })
        {
            item.Check += OnItemCheck;
            // Windows 端点重置会弹确认框并重置本页设置；设置子页尚未移植，此处只保留按钮形态（登记 R20）。
            item.Buttons[0].Click += (_, _) => { };
        }
    }

    private void OnItemCheck(object? sender, MyListItemCheckEventArgs e)
    {
        if (sender is not MyListItem item || !item.Checked) return;
        PageId = item.Index;
        PageChanged?.Invoke(this, PageId);
    }
}
