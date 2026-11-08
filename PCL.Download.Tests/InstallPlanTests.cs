namespace PCL.Download.Tests;

/// <summary>
/// InstallPlanner（合并安装顶层编排的纯决策）测试。
/// <para>
/// 这一段只算「有哪些步骤、什么顺序、权重 / Block / Show」，出错的表现是「该阻塞的没阻塞、
/// 顺序颠倒、权重失衡」，装到一半卡住或结果错乱，所以把每条 Block 布尔式和权重常量都锁一遍。
/// 真正的 LoaderBase 构造与执行 lambda 仍留在 VB，这里只验证决策等价。
/// </para>
/// </summary>
public class InstallPlanTests
{
    private static InstallPlan Build(Action<InstallPlanRequest>? configure = null)
    {
        var request = new InstallPlanRequest
        {
            MinecraftVersion = "1.20.1",
        };
        configure?.Invoke(request);
        return InstallPlanner.Build(request);
    }

    private static InstallPlanStep Step(InstallPlan plan, InstallStepKind kind)
        => plan.GetStep(kind) ?? throw new Exception($"步骤 {kind} 未出现在计划中");

    [Fact]
    public void 纯原版_只有客户端与收尾步骤()
    {
        var plan = Build();

        Assert.Equal(6, plan.Steps.Count);
        Assert.Equal(
            [InstallStepKind.IgnoreMarker, InstallStepKind.Client, InstallStepKind.InstallGame,
             InstallStepKind.PostLibraries, InstallStepKind.RecordManifest, InstallStepKind.RemoveIgnoreMarker],
            plan.Steps.Select(s => s.Kind).ToArray());
    }

    [Fact]
    public void 纯原版_客户端阻塞后续()
    {
        // modable = false 且 OptiFine 未选 => Client.Block = True
        var plan = Build();
        Assert.True(Step(plan, InstallStepKind.Client).Block);
    }

    [Fact]
    public void 纯原版_无加载器与依赖步骤()
    {
        var plan = Build();
        Assert.Null(plan.GetStep(InstallStepKind.FabricApiDownload));
        Assert.Null(plan.GetStep(InstallStepKind.OptiFabricDownload));
        Assert.Null(plan.GetStep(InstallStepKind.OptiFine));
        Assert.Null(plan.GetStep(InstallStepKind.Forge));
        Assert.Null(plan.GetStep(InstallStepKind.NeoForge));
        Assert.Null(plan.GetStep(InstallStepKind.LiteLoader));
        Assert.Null(plan.GetStep(InstallStepKind.Fabric));
    }

    [Fact]
    public void 选了OptiFine但无Mod加载器_客户端不再阻塞()
    {
        // modable = false，但 HasOptiFineEntry = true => Client.Block = False
        var plan = Build(r =>
        {
            r.HasOptiFineEntry = true;
            r.OptiFineDisplayName = "预览版";
        });
        Assert.False(Step(plan, InstallStepKind.Client).Block);
        var optiFine = Step(plan, InstallStepKind.OptiFine);
        Assert.Equal(24, optiFine.ProgressWeight); // If(Modable, 16, 24) => 24
        Assert.True(optiFine.Block);               // Block = Not Modable => True
        Assert.Equal("下载 OptiFine 预览版", optiFine.Name);
    }

    [Fact]
    public void OptiFine作为Mod且有其他Mod加载器_权重与阻塞都对()
    {
        // modable = true（有 Fabric），OptiFine 作为 Mod => 权重 16，Block = Not Modable => False
        var plan = Build(r =>
        {
            r.HasAnyModLoader = true;
            r.HasOptiFineEntry = true;
            r.OptiFineAsMod = true;
            r.OptiFineDisplayName = "HD_U_I7";
            r.FabricVersion = "0.15.0";
        });
        var optiFine = Step(plan, InstallStepKind.OptiFine);
        Assert.Equal(16, optiFine.ProgressWeight);
        Assert.False(optiFine.Block);
    }

    [Fact]
    public void 仅Forge_阻塞后续()
    {
        var plan = Build(r => r.ForgeVersion = "41.2.8");
        var forge = Step(plan, InstallStepKind.Forge);
        Assert.Equal("下载 Forge 41.2.8", forge.Name);
        Assert.Equal(25, forge.ProgressWeight);
        // Fabric 未选、LiteLoader 条目无、NeoForge 条目无 => Block = True
        Assert.True(forge.Block);
    }

    [Fact]
    public void 同时选Fabric与Forge_Forge不再阻塞()
    {
        var plan = Build(r =>
        {
            r.ForgeVersion = "41.2.8";
            r.FabricVersion = "0.15.0";
        });
        // Block = FabricVersion Is Nothing(否) => False
        Assert.False(Step(plan, InstallStepKind.Forge).Block);
        var fabric = Step(plan, InstallStepKind.Fabric);
        Assert.Equal(2, fabric.ProgressWeight);
        Assert.True(fabric.Block); // Fabric 恒阻塞
    }

    [Fact]
    public void 仅NeoForge_阻塞后续()
    {
        var plan = Build(r => r.NeoForgeVersion = "20.4.80-beta");
        var neo = Step(plan, InstallStepKind.NeoForge);
        Assert.Equal(25, neo.ProgressWeight);
        // HasForgeEntry 否、Fabric 未选、LiteLoader 条目无 => Block = True
        Assert.True(neo.Block);
    }

    [Fact]
    public void 存在Forge条目时NeoForge不再阻塞()
    {
        var plan = Build(r =>
        {
            r.NeoForgeVersion = "20.4.80-beta";
            r.HasForgeEntry = true;
        });
        // Block = Not HasForgeEntry(否) => False
        Assert.False(Step(plan, InstallStepKind.NeoForge).Block);
    }

    [Fact]
    public void 仅LiteLoader_阻塞后续()
    {
        var plan = Build(r => r.LiteLoaderVersion = "1.12.2");
        var lite = Step(plan, InstallStepKind.LiteLoader);
        Assert.Equal("下载 LiteLoader 1.20.1", lite.Name);
        Assert.Equal(1, lite.ProgressWeight);
        // Fabric 未选 => Block = True
        Assert.True(lite.Block);
    }

    [Fact]
    public void 同时选Fabric与LiteLoader_LiteLoader不再阻塞()
    {
        var plan = Build(r =>
        {
            r.LiteLoaderVersion = "1.12.2";
            r.FabricVersion = "0.15.0";
        });
        Assert.False(Step(plan, InstallStepKind.LiteLoader).Block);
    }

    [Fact]
    public void 选FabricApi与OptiFabric_两个依赖步骤都显示且不阻塞()
    {
        var plan = Build(r =>
        {
            r.HasFabricApi = true;
            r.HasOptiFabric = true;
        });
        var api = Step(plan, InstallStepKind.FabricApiDownload);
        var optiFabric = Step(plan, InstallStepKind.OptiFabricDownload);
        Assert.Equal(3, api.ProgressWeight);
        Assert.Equal(3, optiFabric.ProgressWeight);
        Assert.False(api.Block);
        Assert.False(optiFabric.Block);
        Assert.True(api.Show);
        Assert.True(optiFabric.Show);
    }

    [Fact]
    public void 中间产物输出时不删忽略标识()
    {
        var plan = Build(r => r.IsIntermediateOutput = true);
        Assert.Null(plan.GetStep(InstallStepKind.RemoveIgnoreMarker));
        // 其余收尾步骤仍在
        Assert.NotNull(plan.GetStep(InstallStepKind.RecordManifest));
        Assert.NotNull(plan.GetStep(InstallStepKind.PostLibraries));
    }

    [Fact]
    public void 非中间产物输出时删忽略标识存在()
    {
        var plan = Build();
        Assert.NotNull(plan.GetStep(InstallStepKind.RemoveIgnoreMarker));
    }

    [Fact]
    public void 安装游戏与后置步骤的默认阻塞与显示()
    {
        var plan = Build();
        // VB 仅设 InstallGame.Block=True，Show 取默认 True
        Assert.True(Step(plan, InstallStepKind.InstallGame).Block);
        Assert.True(Step(plan, InstallStepKind.InstallGame).Show);
        // PostLibraries 仅设权重 8，其余取默认
        Assert.True(Step(plan, InstallStepKind.PostLibraries).Block);
        Assert.True(Step(plan, InstallStepKind.PostLibraries).Show);
        Assert.Equal(8, Step(plan, InstallStepKind.PostLibraries).ProgressWeight);
        // RecordManifest / RemoveIgnoreMarker 仅设 Show=False
        Assert.False(Step(plan, InstallStepKind.RecordManifest).Show);
        Assert.True(Step(plan, InstallStepKind.RecordManifest).Block);
    }

    [Fact]
    public void 忽略标识步骤的默认权重与显示()
    {
        var plan = Build();
        var marker = Step(plan, InstallStepKind.IgnoreMarker);
        Assert.Equal(1, marker.ProgressWeight);
        Assert.False(marker.Show);
        Assert.True(marker.Block);
    }

    [Fact]
    public void 完整组合_步骤顺序与计数正确()
    {
        // Fabric + FabricApi + OptiFabric + OptiFine(as mod) + LiteLoader，无 Forge/NeoForge
        var plan = Build(r =>
        {
            r.HasAnyModLoader = true;
            r.FabricVersion = "0.15.0";
            r.HasFabricApi = true;
            r.HasOptiFabric = true;
            r.HasOptiFineEntry = true;
            r.OptiFineAsMod = true;
            r.OptiFineDisplayName = "HD_U_I7";
            r.LiteLoaderVersion = "1.12.2";
        });

        Assert.Equal(
            [InstallStepKind.IgnoreMarker, InstallStepKind.FabricApiDownload, InstallStepKind.OptiFabricDownload,
             InstallStepKind.Client, InstallStepKind.OptiFine, InstallStepKind.LiteLoader, InstallStepKind.Fabric,
             InstallStepKind.InstallGame, InstallStepKind.PostLibraries, InstallStepKind.RecordManifest,
             InstallStepKind.RemoveIgnoreMarker],
            plan.Steps.Select(s => s.Kind).ToArray());

        // Client 在有 Mod 加载器时 Block = Not Modable(否) && … => False
        Assert.False(Step(plan, InstallStepKind.Client).Block);
        // LiteLoader 因 Fabric 已选 => Block = False
        Assert.False(Step(plan, InstallStepKind.LiteLoader).Block);
    }
}
