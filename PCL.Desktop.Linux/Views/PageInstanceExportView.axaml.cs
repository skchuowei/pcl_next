using System;
using Avalonia.Controls;
using Avalonia.Data;
using PCL.Desktop.Linux.Controls;

namespace PCL.Desktop.Linux.Views;

/// <summary>
/// 实例设置栈「导出」页，1:1 对照 `pcl-next/Pages/PageInstance/PageInstanceExport.xaml`（314 行）：
/// 整合包信息 / 导出内容（26 高复选框树）/ 高级选项（折叠）与底部「开始导出」照抄。
/// 差异登记 R47（无实例数据：OptiFine / Java 提示隐藏、名称与版本无 HintText）与
/// R51（`ExportOption` 的 Rules 规则、重置按钮、读取 / 保存配置、导出动作与格式选择未接入）。
/// </summary>
public partial class PageInstanceExportView : UserControl
{
    public PageInstanceExportView()
    {
        InitializeComponent();

        // 对应 XAML 里 `Visibility="{Binding Checked, ElementName=...}"` 的布尔到可见性绑定
        Hook(CheckOptionsMod, PanOptionsMod);
        Hook(CheckOptionsResourcePacks, PanOptionsResourcePacks);
        Hook(CheckOptionsShaderPacks, PanOptionsShaderSettings, PanOptionsShaderPacks);
        Hook(CheckOptionsSaves, PanOptionsSaves);
        Hook(CheckOptionsPcl, PanOptionsPcl);
        Hook(CheckAdvancedInclude, HintAdvanced);

        Loaded += (_, _) => PanBack.Offset = new Avalonia.Vector(0, 0);
    }

    /// <summary>复选框勾选状态 → 子面板 / 提示可见性（对应 Windows 的 `BooleanToVisibilityConverter` 绑定）。</summary>
    private static void Hook(MyCheckBox box, params Control[] targets)
    {
        void Sync()
        {
            foreach (var target in targets) target.IsVisible = box.Checked;
        }
        box.PropertyChanged += (_, e) =>
        {
            if (e.Property == MyCheckBox.CheckedProperty) Sync();
        };
        Sync();
    }
}
