using Avalonia.Controls;
using Avalonia.Interactivity;

namespace PCL.Desktop.Linux.Views;

/// <summary>
/// 更多栈「帮助」页，1:1 对照 `pcl-next/Pages/PageOther/PageOtherHelp.xaml(.vb)`：
/// 结构（搜索框 / 搜索结果卡 / 分类列表 / 加载卡）全部照抄，搜索框的清空按钮按
/// `Controls/MySearchBox.xaml.vb` L27–L40 实现（空文本隐藏按钮、点击清空并聚焦）。
/// 差异登记 R43：帮助加载器与帮助详情页未移植，列表为空、搜索不过滤分类卡片。
/// </summary>
public partial class PageOtherHelpView : UserControl
{
    public PageOtherHelpView()
    {
        InitializeComponent();

        // 对应 `MySearchBox.xaml.vb` L27–L35：文本为空时清空按钮隐藏且不可命中
        SearchBox.TextChanged += (_, _) =>
        {
            var hasText = !string.IsNullOrEmpty(SearchBox.Text);
            BtnClear.Opacity = hasText ? 1 : 0;
            BtnClear.IsHitTestVisible = hasText;
        };
        // 对应 `MySearchBox.xaml.vb` L37–L40
        BtnClear.Click += (_, _) =>
        {
            SearchBox.Text = "";
            SearchBox.Focus();
        };

        // 对应 `PageOtherHelp.xaml.vb` L7–L9：Loaded 时回到顶部；搜索框自动聚焦（`MySearchBox.xaml.vb` L5–L7）
        Loaded += (_, _) =>
        {
            PanBack.Offset = new Avalonia.Vector(0, 0);
            SearchBox.Focus();
        };
    }
}
