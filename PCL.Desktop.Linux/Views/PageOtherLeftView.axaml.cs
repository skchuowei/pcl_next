using PCL.Desktop.Linux.Controls;

namespace PCL.Desktop.Linux.Views;

/// <summary>
/// 更多栈左导航，1:1 对照 `pcl-next/Pages/PageOther/PageOtherLeft.xaml(.vb)`：
/// 五项（帮助 / 关于与鸣谢 / 百宝箱 / 反馈 / 新功能投票）互斥选中；
/// 帮助项右侧带「刷新」按钮（空操作，R43）；反馈 / 投票在 Windows 端弹确认框后回退选中（R46）。
/// </summary>
public partial class PageOtherLeftView : MyPageLeft
{
    /// <summary>当前选中的子页编号（0 帮助 / 1 关于与鸣谢 / 2 百宝箱）。</summary>
    public int PageId { get; private set; }

    /// <summary>子页切换（对应 `PageOtherLeft.xaml.vb` L52–L56 `PageCheck`）。</summary>
    public event EventHandler<int>? PageChanged;

    public PageOtherLeftView()
    {
        InitializeComponent();

        // 默认子页 = 帮助（无隐藏配置时，`PageOtherLeft.xaml.vb` L40–L46）
        PageId = 0;
        ItemHelp.Check += OnItemCheck;
        ItemAbout.Check += OnItemCheck;
        ItemTest.Check += OnItemCheck;

        // 反馈 / 新功能投票：Windows 端在 Changed 事件里弹 MessageBox 并把事件标记为已处理，
        // 因此条目不会保持选中、页面也不切换（`TryFeedback` / `TryVote`，vb L128–L157）。
        // Linux 无 MessageBox 等价物，点击时直接取消控件内部的选中动作（登记 R46）。
        ItemFeedback.Click += (_, e) => e.Handled = true;
        ItemVote.Click += (_, e) => e.Handled = true;

        // Windows 端「刷新」会重置帮助版本并重启加载器；Linux 未接入帮助加载器（R43）。
        ItemHelp.Buttons[0].Click += (_, _) => { };
    }

    private void OnItemCheck(object? sender, MyListItemCheckEventArgs e)
    {
        if (sender is not MyListItem item || !item.Checked) return;
        PageId = item.Index;
        PageChanged?.Invoke(this, PageId);
    }
}
