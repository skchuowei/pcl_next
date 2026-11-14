using PCL.Desktop.Linux.Controls;

namespace PCL.Desktop.Linux.Views;

/// <summary>
/// 下载栈左导航，1:1 对照 `pcl-next/Pages/PageDownload/PageDownloadLeft.xaml(.vb)`：
/// 原版游戏 + 「社区资源」下五个资源子页，互斥选中，选中项经 `Check` 事件回传子页编号。
/// 刷新按钮只保留形态（R36），右侧子页由宿主按编号切换（原版游戏已移植，资源子页待补，R20）。
/// </summary>
public partial class PageDownloadLeftView : MyPageLeft
{
    /// <summary>当前选中的子页编号（对应 `PageDownloadLeft` 的 `PageID`，0 原版 / 2 Mod / 3 整合包 / 4 数据包 / 5 资源包 / 6 光影包）。</summary>
    public int PageId { get; private set; }

    /// <summary>子页切换（对应 `PageDownloadLeft.xaml.vb` L24–L28 `PageCheck`）。</summary>
    public event EventHandler<int>? PageChanged;

    public PageDownloadLeftView()
    {
        InitializeComponent();

        foreach (var item in new[] { ItemInstall, ItemMod, ItemPack, ItemDataPack, ItemResourcePack, ItemShader })
        {
            item.Check += OnItemCheck;
            // 原逻辑刷新缓存并重启对应加载器；Linux 尚未接入下载服务，此处只保留按钮形态（R36）。
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
