using Avalonia.Controls;

namespace PCL.Desktop.Linux.Views;

/// <summary>
/// 实例设置栈「Mod 管理」页，1:1 对照 `pcl-next/Pages/PageInstance/PageInstanceMod.xaml`（79 行）：
/// 搜索框 / 管理按钮卡 / 筛选列表卡 / 空态卡 / 加载卡五层结构与尺寸照抄。
/// 差异登记 R47（无实例与 Mod 列表服务，固定空态）与 R49（多选操作浮卡 CardSelect 未建）。
/// </summary>
public partial class PageInstanceModView : UserControl
{
    public PageInstanceModView()
    {
        InitializeComponent();

        // 搜索框清空按钮联动（同 `MySearchBox.xaml.vb` L27–L40）
        SearchBox.TextChanged += (_, _) =>
        {
            var hasText = !string.IsNullOrEmpty(SearchBox.Text);
            BtnClear.Opacity = hasText ? 1 : 0;
            BtnClear.IsHitTestVisible = hasText;
        };
        BtnClear.Click += (_, _) =>
        {
            SearchBox.Text = "";
            SearchBox.Focus();
        };
    }
}
