using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using PCL.Desktop.Linux.Controls;

namespace PCL.Desktop.Linux.Views;

/// <summary>资源搜索页的种类（对应原五个包装页 `PageDownloadMod/Pack/DataPack/ResourcePack/Shader`）。</summary>
public enum ResourcePageKind
{
    Mod,
    ModPack,
    DataPack,
    ResourcePack,
    Shader
}

/// <summary>
/// 资源搜索页，1:1 对照 `pcl-next/Pages/PageDownload/Resource/PageResource.xaml`（96 行）与五个包装页：
/// `PageDownloadMod.xaml`（36 行）、`PageDownloadPack.xaml`（28 行）、`PageDownloadDataPack.xaml`（26 行）、
/// `PageDownloadResourcePack.xaml`（42 行）、`PageDownloadShader.xaml`（31 行）。
/// 五页布局完全一致，差异只有标题、加载文案、「类型」下拉项与「安装已有整合包」按钮可见性，
/// 因此 Linux 侧共用本视图，由 <see cref="ResourcePageKind"/> 在代码侧按原 XAML 逐项生成下拉项。
/// 行为边界（R40–R42）：无资源搜索服务，结果为空、分页按钮禁用，所有按钮为空操作。
/// </summary>
public partial class PageResourceView : UserControl
{
    private static readonly (string Tag, string Text, string? Tip, bool Indent)[] ModTags =
    {
        ("", "全部", null, false),
        ("406/worldgen", "世界元素", null, true),
        ("407/", "生物群系", null, true),
        ("410/", "维度", null, true),
        ("408/", "矿物与资源", null, true),
        ("409/", "天然结构", null, true),
        ("412/technology", "科技", null, true),
        ("415/", "管道与物流", null, true),
        ("4843/", "自动化", null, true),
        ("417/", "能源", null, true),
        ("4558/", "红石", null, true),
        ("436/food", "食物与烹饪", null, true),
        ("416/", "农业", null, true),
        ("/game-mechanics", "游戏机制", null, true),
        ("414/transportation", "运输", null, true),
        ("420/storage", "仓储", null, true),
        ("419/magic", "魔法", null, true),
        ("422/adventure", "冒险", null, true),
        ("424/decoration", "装饰", null, true),
        ("411/mobs", "生物", null, true),
        ("5191/utility", "实用", null, true),
        ("434/equipment", "装备与工具", null, true),
        ("9026/", "创造模式", null, true),
        ("6814/optimization", "性能优化", null, true),
        ("423/", "信息显示", null, true),
        ("435/social", "服务器", null, true),
        ("421/library", "支持库", null, true)
    };

    private static readonly (string Tag, string Text, string? Tip, bool Indent)[] ModPackTags =
    {
        ("", "全部", null, false),
        ("4484/", "多人", null, false),
        ("/optimization", "性能优化", null, false),
        ("4479/challenging", "硬核", null, false),
        ("4483/combat", "战斗", null, false),
        ("4478/quests", "任务", null, false),
        ("4472/technology", "科技", null, false),
        ("4473/magic", "魔法", null, false),
        ("4475/adventure", "冒险", null, false),
        ("/kitchen-sink", "水槽包", null, false),
        ("4476/", "探索", null, false),
        ("4477/", "小游戏", null, false),
        ("4474/", "科幻", null, false),
        ("4736/", "空岛", null, false),
        ("5128/", "原版改良", null, false),
        ("4487/", "FTB", "由 FTB 官方发布", false),
        ("4480/", "基于地图", null, false),
        ("4481/lightweight", "轻量整合", null, false),
        ("4482/", "大型整合", null, false)
    };

    private static readonly (string Tag, string Text, string? Tip, bool Indent)[] DataPackTags =
    {
        ("", "全部", null, false),
        ("/worldgen", "世界元素", null, false),
        ("6951/technology", "科技", null, false),
        ("/game-mechanics", "游戏机制", null, false),
        ("/transportation", "运输", null, false),
        ("/storage", "仓储", null, false),
        ("6952/magic", "魔法", null, false),
        ("6948/adventure", "冒险", null, false),
        ("6949/", "幻想", null, false),
        ("/decoration", "装饰", null, false),
        ("/mobs", "生物", null, false),
        ("6953/utility", "实用", null, false),
        ("/equipment", "装备与工具", null, false),
        ("/optimization", "性能优化", null, false),
        ("/social", "服务器", null, false),
        ("6950/library", "支持库", null, false),
        ("6946/", "Mod 相关", null, false)
    };

    private static readonly (string Tag, string Text, string? Tip, bool Indent)[] ResourcePackTags =
    {
        ("", "全部", null, false),
        ("", "风格", null, false),
        ("403/vanilla-like", "原版风", null, true),
        ("400/realistic", "写实风", null, true),
        ("401/", "现代风", null, true),
        ("402/", "中世纪", null, true),
        ("399/", "蒸汽朋克", null, true),
        ("/themed", "主题化", null, true),
        ("/simplistic", "简洁", null, true),
        ("/decoration", "装饰", null, true),
        ("/combat", "战斗", null, true),
        ("/utility", "实用", null, true),
        ("/tweaks", "改良", null, true),
        ("/cursed", "鬼畜", null, true),
        ("", "特性", null, false),
        ("/entities", "含实体", null, true),
        ("/audio", "含声音", null, true),
        ("5244/fonts", "含字体", null, true),
        ("/models", "含模型", null, true),
        ("/locale", "含语言", null, true),
        ("/gui", "含 UI", null, true),
        ("/core-shaders", "核心着色器", null, true),
        ("404/", "动态效果", null, true),
        ("4465/modded", "兼容 Mod", null, true),
        ("", "分辨率", null, false),
        ("/8x-", "8x 或更低", null, true),
        ("393/16x", "16x", null, true),
        ("394/32x", "32x", null, true),
        ("/48x", "48x", null, true),
        ("395/64x", "64x", null, true),
        ("396/128x", "128x", null, true),
        ("397/256x", "256x", null, true),
        ("398/512x+", "512x 或更高", null, true)
    };

    private static readonly (string Tag, string Text, string? Tip, bool Indent)[] ShaderTags =
    {
        ("", "全部", null, false),
        ("", "风格", null, false),
        ("6555/vanilla-like", "原版风", null, true),
        ("6554/fantasy", "幻想风", null, true),
        ("6553/realistic", "写实风", null, true),
        ("/semi-realistic", "半写实风", null, true),
        ("/cartoon", "卡通风", null, true),
        ("", "特性", null, false),
        ("/colored-lighting", "彩色光照", null, true),
        ("/path-tracing", "路径追踪", null, true),
        ("/pbr", "PBR", null, true),
        ("/reflections", "反射", null, true),
        ("", "性能负荷", null, false),
        ("/potato", "极低", null, true),
        ("/low", "低", null, true),
        ("/medium", "中", null, true),
        ("/high", "高", null, true),
        ("", "加载器", null, false),
        ("/vanilla", "原版可用", "作为资源包加载，可以直接在原版游戏里使用。", true),
        ("/iris", "Iris", null, true),
        ("/optifine", "OptiFine", null, true)
    };

    public PageResourceView()
    {
        InitializeComponent();
    }

    public PageResourceView(ResourcePageKind kind) : this()
    {
        ApplyKind(kind);
    }

    /// <summary>按种类设置标题、加载文案、类型下拉与专属按钮（对应五个包装页的 XAML 参数）。</summary>
    private void ApplyKind(ResourcePageKind kind)
    {
        var (name, spaced, tags) = kind switch
        {
            ResourcePageKind.Mod => ("Mod", " Mod ", ModTags),
            ResourcePageKind.ModPack => ("整合包", "整合包", ModPackTags),
            ResourcePageKind.DataPack => ("数据包", "数据包", DataPackTags),
            ResourcePageKind.ResourcePack => ("资源包", "资源包", ResourcePackTags),
            _ => ("光影包", "光影包", ShaderTags)
        };

        PanAlways.Title = $"搜索{spaced}";
        Load.Text = $"正在获取{spaced}列表";
        BtnSearchInstallModPack.IsVisible = kind == ResourcePageKind.ModPack;

        for (var i = 0; i < tags.Length; i++)
        {
            // 分组标题（风格 / 特性 / 分辨率 / 性能负荷 / 加载器）在原 XAML 中是 IsEnabled=False 的无 Tag 项
            var isHeader = tags[i].Tag.Length == 0 && i != 0;
            var item = new MyComboBoxItem
            {
                Content = tags[i].Text,
                Tag = tags[i].Tag,
                IsEnabled = !isHeader,
                Margin = tags[i].Indent ? new Thickness(15, 0, 0, 0) : default,
                HorizontalAlignment = HorizontalAlignment.Stretch
            };
            if (tags[i].Tip is not null) ToolTip.SetTip(item, tags[i].Tip);
            ComboSearchTag.Items.Add(item);
            if (i == 0) ComboSearchTag.SelectedIndex = 0;
        }

        // 记录类型名，供后续接线使用（对应 `PageResource.TypeName`）
        Kind = kind;
        TypeName = name;
    }

    /// <summary>当前资源种类（对应 `PageResource.PageType`）。</summary>
    public ResourcePageKind Kind { get; private set; }

    /// <summary>英文前后不含空格的可读资源类型名（对应 `PageResource.TypeName`）。</summary>
    public string TypeName { get; private set; } = "";
}
