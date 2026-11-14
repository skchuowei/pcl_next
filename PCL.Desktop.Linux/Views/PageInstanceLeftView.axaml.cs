using PCL.Desktop.Linux.Controls;

namespace PCL.Desktop.Linux.Views;

/// <summary>
/// 实例设置栈左导航，1:1 对照 `pcl-next/Pages/PageInstance/PageInstanceLeft.xaml(.vb)`：
/// 概览 / 设置 / 修改 / Mod 管理 / 导出 五项互斥选中；设置带「初始化」、修改 / Mod / 导出带「刷新」。
/// 静态默认（R47）：Linux 尚无实例数据服务，`Instance Is Nothing` → Mod 项隐藏、ModDisabled 项显示
/// （vb L13–L21）；初始化与刷新按钮均为空操作。
/// </summary>
public partial class PageInstanceLeftView : MyPageLeft
{
    /// <summary>当前子页编号（0 概览 / 1 设置 / 2 修改 / 3 Mod / 4 ModDisabled / 5 导出）。</summary>
    public int PageId { get; private set; }

    public event EventHandler<int>? PageChanged;

    public PageInstanceLeftView()
    {
        InitializeComponent();

        PageId = 0;
        ItemOverall.Check += OnItemCheck;
        ItemSetup.Check += OnItemCheck;
        ItemModify.Check += OnItemCheck;
        ItemMod.Check += OnItemCheck;
        ItemModDisabled.Check += OnItemCheck;
        ItemExport.Check += OnItemCheck;

        // 初始化 / 刷新按钮：Windows 端分别调 Reset 与 Refresh（vb L105–L135），Linux 未接实例服务（R47）
        ItemSetup.Buttons[0].Click += (_, _) => { };
        ItemModify.Buttons[0].Click += (_, _) => { };
        ItemMod.Buttons[0].Click += (_, _) => { };
        ItemExport.Buttons[0].Click += (_, _) => { };
    }

    private void OnItemCheck(object? sender, MyListItemCheckEventArgs e)
    {
        if (sender is not MyListItem item || !item.Checked) return;
        PageId = item.Index;
        PageChanged?.Invoke(this, PageId);
    }
}
