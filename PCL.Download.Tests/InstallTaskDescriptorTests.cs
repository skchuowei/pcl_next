using System;
using PCL.Download;
using Xunit;

namespace PCL.Download.Tests;

/// <summary>
/// P4-A 编排决策 DTO 的对照用例。
/// <para>
/// 期望值逐字取自 VB <c>ModDownloadLib.McInstallLoadersGet</c>（1609–1819）、
/// <c>McDownloadClientLoader</c>（9–69）以及各 <c>New NetFile(…, New FileChecker With {…})</c> 构造点。
/// 这些用例是口径 A 的守门线：C# 侧的顺序 / 权重 / Block / Show / 下载清单任一处被改坏，
/// 对应用例必须失败；验证阶段另做一次真实变异演示（改坏 <c>Forge.Block</c> → 用例红 → 还原）。
/// </para>
/// </summary>
public class InstallTaskDescriptorTests
{
    private static readonly string[] SampleUrls = ["https://mirror.example/file", "https://official.example/file"];

    /// <summary>能触发全部 13 类顶层步骤的请求。</summary>
    private static InstallPlan FullPlan(Action<InstallPlanRequest>? configure = null)
    {
        var request = new InstallPlanRequest
        {
            MinecraftVersion = "1.20.1",
            HasAnyModLoader = true,
            ForgeVersion = "41.2.8",
            NeoForgeVersion = "20.4.80-beta",
            LiteLoaderVersion = "1.12.2",
            FabricVersion = "0.15.0",
            HasOptiFineEntry = true,
            OptiFineDisplayName = "HD_U_I7",
            OptiFineAsMod = true,
            HasFabricApi = true,
            HasOptiFabric = true,
            HasForgeEntry = true,
            HasNeoForgeEntry = true,
            HasLiteLoaderEntry = true,
        };
        configure?.Invoke(request);
        return InstallPlanner.Build(request);
    }

    private static InstallTaskDescriptor Task(InstallPlan plan, InstallStepKind kind)
        => plan.GetStep(kind)?.Task
           ?? throw new Exception($"步骤 {kind} 未出现在计划中");

    #region 顶层装配规格

    [Fact]
    public void 描述符与顶层步骤一一对应且顺序一致()
    {
        var plan = FullPlan();
        var descriptors = InstallTaskPlanner.Describe(plan);

        Assert.Equal(13, plan.Steps.Count);
        Assert.Equal(13, descriptors.Count);
        Assert.Equal(plan.Steps.Select(s => (InstallStepKind?)s.Kind), descriptors.Select(d => d.Kind));
        Assert.Equal(plan.Steps.Select(s => s.Name), descriptors.Select(d => d.Name));
    }

    [Theory]
    [InlineData(InstallStepKind.IgnoreMarker, InstallLoaderKind.Task)]
    [InlineData(InstallStepKind.FabricApiDownload, InstallLoaderKind.Download)]
    [InlineData(InstallStepKind.OptiFabricDownload, InstallLoaderKind.Download)]
    [InlineData(InstallStepKind.Client, InstallLoaderKind.Combo)]
    [InlineData(InstallStepKind.OptiFine, InstallLoaderKind.Combo)]
    [InlineData(InstallStepKind.Forge, InstallLoaderKind.Combo)]
    [InlineData(InstallStepKind.NeoForge, InstallLoaderKind.Combo)]
    [InlineData(InstallStepKind.LiteLoader, InstallLoaderKind.Combo)]
    [InlineData(InstallStepKind.Fabric, InstallLoaderKind.Combo)]
    [InlineData(InstallStepKind.InstallGame, InstallLoaderKind.Task)]
    [InlineData(InstallStepKind.PostLibraries, InstallLoaderKind.Combo)]
    [InlineData(InstallStepKind.RecordManifest, InstallLoaderKind.Task)]
    [InlineData(InstallStepKind.RemoveIgnoreMarker, InstallLoaderKind.Task)]
    public void 每类步骤的加载器种类对齐VB(InstallStepKind kind, InstallLoaderKind expected)
    {
        Assert.Equal(expected, Task(FullPlan(), kind).LoaderKind);
    }

    [Fact]
    public void 描述符不得重算计划里的权重Block与Show()
    {
        // 装配层只能读取，不能「顺手优化」；这条用例在 Build 的布尔式被改坏时也会跟着红
        var plan = FullPlan();
        foreach (var step in plan.Steps)
        {
            var d = step.Task;
            Assert.Equal(step.ProgressWeight, d.ProgressWeight);
            Assert.Equal(step.Block, d.Block);
            Assert.Equal(step.Show, d.Show);
        }

        // 全家桶下逐字对齐 VB 的 Block 布尔式
        Assert.True(Task(plan, InstallStepKind.IgnoreMarker).Block);
        Assert.False(Task(plan, InstallStepKind.FabricApiDownload).Block);
        Assert.False(Task(plan, InstallStepKind.OptiFabricDownload).Block);
        Assert.False(Task(plan, InstallStepKind.Client).Block);   // modable = true
        Assert.False(Task(plan, InstallStepKind.OptiFine).Block); // Block = Not Modable
        Assert.False(Task(plan, InstallStepKind.Forge).Block);    // FabricVersion 已选
        Assert.False(Task(plan, InstallStepKind.NeoForge).Block); // HasForgeEntry 为真
        Assert.False(Task(plan, InstallStepKind.LiteLoader).Block);
        Assert.True(Task(plan, InstallStepKind.Fabric).Block);    // 恒阻塞
        Assert.True(Task(plan, InstallStepKind.InstallGame).Block);
        Assert.True(Task(plan, InstallStepKind.PostLibraries).Block);
        Assert.True(Task(plan, InstallStepKind.RecordManifest).Block);
    }

    [Theory]
    [InlineData(InstallStepKind.IgnoreMarker, false)]
    [InlineData(InstallStepKind.FabricApiDownload, true)]
    [InlineData(InstallStepKind.OptiFabricDownload, true)]
    [InlineData(InstallStepKind.Client, false)]
    [InlineData(InstallStepKind.OptiFine, false)]
    [InlineData(InstallStepKind.Forge, false)]
    [InlineData(InstallStepKind.NeoForge, false)]
    [InlineData(InstallStepKind.LiteLoader, false)]
    [InlineData(InstallStepKind.Fabric, false)]
    [InlineData(InstallStepKind.InstallGame, true)]
    [InlineData(InstallStepKind.PostLibraries, true)]
    [InlineData(InstallStepKind.RecordManifest, false)]
    [InlineData(InstallStepKind.RemoveIgnoreMarker, false)]
    public void 每类步骤的Show对齐VB(InstallStepKind kind, bool expected)
    {
        Assert.Equal(expected, Task(FullPlan(), kind).Show);
    }

    [Fact]
    public void 关键步骤的输入产出槽位对齐VB()
    {
        var plan = FullPlan();
        Assert.Contains("VersionFolder", Task(plan, InstallStepKind.IgnoreMarker).Inputs);
        Assert.Contains("Request.MinecraftJson", Task(plan, InstallStepKind.Client).Inputs);
        Assert.Contains("ClientLoader", Task(plan, InstallStepKind.Client).Outputs);
        Assert.Contains("TargetFolder", Task(plan, InstallStepKind.InstallGame).Outputs);
        Assert.Contains("ManagedFileSources", Task(plan, InstallStepKind.RecordManifest).Inputs);
        Assert.Contains("McInstallManifest", Task(plan, InstallStepKind.RecordManifest).Outputs);
        Assert.Contains("ManagedFileSources.FabricApi", Task(plan, InstallStepKind.FabricApiDownload).Outputs);
        Assert.Contains("ManagedFileSources.OptiFabric", Task(plan, InstallStepKind.OptiFabricDownload).Outputs);
    }

    [Theory]
    [InlineData(InstallStepKind.FabricApiDownload, NetFileDescriptorKind.FabricApi)]
    [InlineData(InstallStepKind.OptiFabricDownload, NetFileDescriptorKind.OptiFabric)]
    [InlineData(InstallStepKind.OptiFine, NetFileDescriptorKind.OptiFineInstaller)]
    [InlineData(InstallStepKind.Forge, NetFileDescriptorKind.ForgeInstaller)]
    [InlineData(InstallStepKind.NeoForge, NetFileDescriptorKind.NeoForgeInstaller)]
    [InlineData(InstallStepKind.Fabric, NetFileDescriptorKind.FabricMain)]
    [InlineData(InstallStepKind.PostLibraries, NetFileDescriptorKind.VanillaLibrary)]
    public void 每类步骤的下载清单类别对齐VB(InstallStepKind kind, NetFileDescriptorKind expected)
    {
        Assert.Contains(expected, Task(FullPlan(), kind).DownloadKinds);
    }

    [Fact]
    public void 客户端下载清单覆盖五类原版文件()
    {
        Assert.Equal(
            [NetFileDescriptorKind.VanillaJson, NetFileDescriptorKind.VanillaJar,
             NetFileDescriptorKind.VanillaLibrary, NetFileDescriptorKind.AssetIndex,
             NetFileDescriptorKind.AssetObject],
            Task(FullPlan(), InstallStepKind.Client).DownloadKinds);
    }

    [Fact]
    public void 取消与进度语义对齐VB()
    {
        var plan = FullPlan();
        Assert.Equal(InstallCancelSemantics.Inherit, Task(plan, InstallStepKind.InstallGame).Cancel);
        Assert.True(Task(plan, InstallStepKind.InstallGame).ReportsProgress); // 0.2 / 0.8
        Assert.Equal(InstallCancelSemantics.CancelIfUnavailable, Task(plan, InstallStepKind.NeoForge).Cancel);
        Assert.Equal(InstallCancelSemantics.Cooperative, Task(plan, InstallStepKind.FabricApiDownload).Cancel);
        Assert.Equal(InstallCancelSemantics.Cooperative, Task(plan, InstallStepKind.OptiFine).Cancel); // 作为 Mod
    }

    [Fact]
    public void OptiFine非Mod形态等待原版且标注安装函数()
    {
        var plan = FullPlan(r => r.OptiFineAsMod = false);
        var d = Task(plan, InstallStepKind.OptiFine);
        Assert.Equal(InstallCancelSemantics.WaitForClient, d.Cancel);
        Assert.Equal("McDownloadOptiFineLoader", d.SubStepsSource);
        Assert.DoesNotContain("ManagedFileSources.OptiFine", d.Outputs);
    }

    [Fact]
    public void OptiFine作为Mod形态走SaveLoader()
    {
        var d = Task(FullPlan(), InstallStepKind.OptiFine);
        Assert.Equal("McDownloadOptiFineSaveLoader", d.SubStepsSource);
        Assert.Contains("ManagedFileSources.OptiFine", d.Outputs);
    }

    [Theory]
    [InlineData(InstallStepKind.Forge, "McDownloadForgelikeLoader(false)")]
    [InlineData(InstallStepKind.NeoForge, "McDownloadForgelikeLoader(true)")]
    [InlineData(InstallStepKind.LiteLoader, "McDownloadLiteLoaderLoader")]
    [InlineData(InstallStepKind.Fabric, "McDownloadFabricLoader")]
    public void 复合体内部子列表来源已登记(InstallStepKind kind, string source)
    {
        var d = Task(FullPlan(), kind);
        Assert.Equal(source, d.SubStepsSource);
        Assert.Empty(d.SubSteps); // 尚未下沉，P4-B 逐类推进
    }

    #endregion

    #region 子加载器骨架（逐字对照 VB）

    [Fact]
    public void 客户端子加载器骨架对齐McDownloadClientLoader()
    {
        // JsonUrl 未提供时 VB 先插入「获取原版 json 文件下载地址」，共 5 个顶层子加载器
        var subs = Task(FullPlan(), InstallStepKind.Client).SubSteps;
        Assert.Equal(5, subs.Count);
        Assert.Equal(
            ["获取原版 json 文件下载地址", "下载原版 json 文件", "读取 json 文件",
             "下载原版支持库文件", "下载原版资源文件"],
            subs.Select(s => s.Name));
        Assert.All(subs, s => Assert.Null(s.Kind)); // 子加载器不属于顶层步骤

        Assert.Equal(2, subs[0].ProgressWeight);
        Assert.False(subs[0].Show);
        Assert.Equal(InstallLoaderKind.Task, subs[0].LoaderKind);
        Assert.Equal(3, subs[1].ProgressWeight);
        Assert.Equal(InstallLoaderKind.Download, subs[1].LoaderKind);
        Assert.Equal(0, subs[2].ProgressWeight);
        Assert.False(subs[2].Show);

        // 支持库复合体：Block=False / 权重 14，两个副加载器
        Assert.Equal(InstallLoaderKind.Combo, subs[3].LoaderKind);
        Assert.Equal(14, subs[3].ProgressWeight);
        Assert.False(subs[3].Block);
        Assert.Equal(["分析原版支持库文件（副加载器）", "下载原版支持库文件（副加载器）"],
            subs[3].SubSteps.Select(s => s.Name));
        Assert.Equal(1, subs[3].SubSteps[0].ProgressWeight);
        Assert.Equal(13, subs[3].SubSteps[1].ProgressWeight);
        Assert.All(subs[3].SubSteps, s => Assert.False(s.Show));

        // 资源复合体：Block=False / 权重 18，四个副加载器（0.01 那个不能写 0 或 1）
        Assert.Equal(18, subs[4].ProgressWeight);
        Assert.False(subs[4].Block);
        Assert.Equal(
            ["分析资源文件索引地址（副加载器）", "下载资源文件索引（副加载器）",
             "分析所需资源文件（副加载器）", "下载资源文件（副加载器）"],
            subs[4].SubSteps.Select(s => s.Name));
        Assert.Equal([1, 3, 0.01, 14], subs[4].SubSteps.Select(s => s.ProgressWeight));
        Assert.All(subs[4].SubSteps, s => Assert.False(s.Show));
    }

    [Fact]
    public void 已有原版Json地址时不再插入获取地址任务()
    {
        var subs = Task(FullPlan(r => r.HasMinecraftJson = true), InstallStepKind.Client).SubSteps;
        Assert.Equal(4, subs.Count);
        Assert.DoesNotContain(subs, s => s.Name == "获取原版 json 文件下载地址");
    }

    [Fact]
    public void 补全文件复合体的两个副加载器对齐VB()
    {
        var subs = Task(FullPlan(), InstallStepKind.PostLibraries).SubSteps;
        Assert.Equal(2, subs.Count);
        Assert.Equal("分析游戏支持库文件（副加载器）", subs[0].Name);
        Assert.Equal("下载游戏支持库文件（副加载器）", subs[1].Name);
        Assert.Equal([1, 7], subs.Select(s => s.ProgressWeight));
        Assert.All(subs, s => Assert.False(s.Show));
        Assert.Null(Task(FullPlan(), InstallStepKind.PostLibraries).SubStepsSource);
    }

    #endregion

    #region 九类下载文件描述符（逐字段对照 VB FileChecker）

    [Fact]
    public void 原版Json与资源索引都按Json校验()
    {
        var json = InstallDownloads.VanillaJson(SampleUrls, @"C:\MC\v.json");
        Assert.Equal(NetFileDescriptorKind.VanillaJson, json.Kind);
        Assert.True(json.IsJson);
        Assert.Null(json.MinSize);
        Assert.False(json.SimulateBrowserHeaders);

        var index = InstallDownloads.AssetIndex(SampleUrls, @"C:\MC\assets\indexes\1.20.json");
        Assert.Equal(NetFileDescriptorKind.AssetIndex, index.Kind);
        Assert.True(index.IsJson);
    }

    [Fact]
    public void 原版Jar的校验口径对齐DlClientJarGet()
    {
        var desc = InstallDownloads.VanillaJar(SampleUrls, @"C:\MC\1.20.1.jar", "sha1-value", 123456);
        Assert.Equal(NetFileDescriptorKind.VanillaJar, desc.Kind);
        Assert.Equal(1024, desc.MinSize);
        Assert.Equal("sha1-value", desc.Hash);
        Assert.Equal(123456, desc.ActualSize);

        // VB 用 If(JsonObject("size"), -1) 兜底，缺 size 时是 -1 而不是 null
        Assert.Equal(-1, InstallDownloads.VanillaJar(SampleUrls, @"C:\MC\a.jar", null, null).ActualSize);
    }

    [Fact]
    public void 支持库与资源文件的校验口径对齐ModMinecraft()
    {
        var lib = InstallDownloads.VanillaLibrary(SampleUrls, @"C:\MC\libraries\x.jar", "hash", 4096);
        Assert.Equal(NetFileDescriptorKind.VanillaLibrary, lib.Kind);
        Assert.Equal("hash", lib.Hash);
        Assert.Equal(4096, lib.ActualSize);

        var asset = InstallDownloads.AssetObject(SampleUrls, @"C:\MC\assets\objects\ab\abc", "abc", 512);
        Assert.Equal(NetFileDescriptorKind.AssetObject, asset.Kind);
        Assert.Equal("abc", asset.Hash);
        Assert.Equal(512, asset.ActualSize);
        // Size = 0 时 VB 写 -1（-1 表示无法获取准确大小）
        Assert.Equal(-1, InstallDownloads.AssetObject(SampleUrls, @"C:\MC\a", "abc", 0).ActualSize);
    }

    [Fact]
    public void OptiFine两种形态的MinSize不同()
    {
        var install = InstallDownloads.OptiFineInstaller(SampleUrls, @"C:\MC\optifine.jar", asMod: false);
        var save = InstallDownloads.OptiFineInstaller(SampleUrls, @"C:\MC\mods\optifine.jar", asMod: true);
        Assert.Equal(300 * 1024, install.MinSize); // ModDownloadLib.vb:505
        Assert.Equal(64 * 1024, save.MinSize);     // ModDownloadLib.vb:615
        Assert.Equal(NetFileDescriptorKind.OptiFineInstaller, install.Kind);
        Assert.Equal(NetFileDescriptorKind.OptiFineInstaller, save.Kind);
    }

    [Fact]
    public void Forge带Hash而NeoForge不带Hash()
    {
        var installer = new ForgelikeInstallerFile(SampleUrls, 1024 * 1024, "forge-md5");
        var forge = InstallDownloads.ForgeInstaller(installer, @"C:\MC\forge-installer.jar");
        var neo = InstallDownloads.NeoForgeInstaller(installer, @"C:\MC\neoforge-installer.jar");

        Assert.Equal(NetFileDescriptorKind.ForgeInstaller, forge.Kind);
        Assert.Equal(1024 * 1024, forge.MinSize);
        Assert.Equal("forge-md5", forge.Hash);
        Assert.Equal(NetFileDescriptorKind.NeoForgeInstaller, neo.Kind);
        Assert.Equal(1024 * 1024, neo.MinSize);
        Assert.Null(neo.Hash); // 不得顺手补上
    }

    [Fact]
    public void LiteLoader与Fabric安装器的MinSize对齐另存为路径()
    {
        Assert.Equal(1024 * 1024,
            InstallDownloads.LiteLoaderInstaller(SampleUrls, @"C:\MC\liteloader.jar").MinSize);
        Assert.Equal(NetFileDescriptorKind.LiteLoaderInstaller,
            InstallDownloads.LiteLoaderInstaller(SampleUrls, @"C:\MC\liteloader.jar").Kind);
        Assert.Equal(1024 * 64,
            InstallDownloads.FabricInstaller(SampleUrls, @"C:\MC\fabric-installer.jar").MinSize);
        Assert.Equal(NetFileDescriptorKind.FabricInstaller,
            InstallDownloads.FabricInstaller(SampleUrls, @"C:\MC\fabric-installer.jar").Kind);
    }

    [Fact]
    public void 依赖Mod走资源对象的校验与浏览器头()
    {
        var api = InstallDownloads.ResourceDependency(
            NetFileDescriptorKind.FabricApi, SampleUrls, @"C:\MC\mods\fabric-api.jar", "api-hash", 8192);
        var optiFabric = InstallDownloads.ResourceDependency(
            NetFileDescriptorKind.OptiFabric, SampleUrls, @"C:\MC\mods\optifabric.jar", "of-hash", 4096);

        Assert.Equal("api-hash", api.Hash);
        Assert.Equal(8192, api.ActualSize);
        Assert.True(api.SimulateBrowserHeaders);
        Assert.Equal(NetFileDescriptorKind.OptiFabric, optiFabric.Kind);
        Assert.True(optiFabric.SimulateBrowserHeaders);
        Assert.Throws<ArgumentOutOfRangeException>(() => InstallDownloads.ResourceDependency(
            NetFileDescriptorKind.ForgeInstaller, SampleUrls, @"C:\MC\x", null, null));
    }

    [Fact]
    public void 既有Fabric主文件仍是FabricMain类别()
    {
        var desc = InstallDownloads.FabricMainFile("0.15.0", "1.20.1", @"C:\MC\");
        Assert.Equal(NetFileDescriptorKind.FabricMain, desc.Kind);
        Assert.True(desc.IsJson);
        Assert.Equal(2, desc.Urls.Count);
    }

    [Fact]
    public void 十三类描述符都有对应工厂且类别自洽()
    {
        foreach (var kind in Enum.GetValues<NetFileDescriptorKind>())
        {
            var desc = kind switch
            {
                NetFileDescriptorKind.VanillaJson => InstallDownloads.VanillaJson(SampleUrls, "x"),
                NetFileDescriptorKind.VanillaJar => InstallDownloads.VanillaJar(SampleUrls, "x", null, 1),
                NetFileDescriptorKind.VanillaLibrary => InstallDownloads.VanillaLibrary(SampleUrls, "x"),
                NetFileDescriptorKind.AssetIndex => InstallDownloads.AssetIndex(SampleUrls, "x"),
                NetFileDescriptorKind.AssetObject => InstallDownloads.AssetObject(SampleUrls, "x", "h", 1),
                NetFileDescriptorKind.OptiFineInstaller => InstallDownloads.OptiFineInstaller(SampleUrls, "x", false),
                NetFileDescriptorKind.ForgeInstaller => InstallDownloads.ForgeInstaller(
                    new ForgelikeInstallerFile(SampleUrls, 1, null), "x"),
                NetFileDescriptorKind.NeoForgeInstaller => InstallDownloads.NeoForgeInstaller(
                    new ForgelikeInstallerFile(SampleUrls, 1, null), "x"),
                NetFileDescriptorKind.LiteLoaderInstaller => InstallDownloads.LiteLoaderInstaller(SampleUrls, "x"),
                NetFileDescriptorKind.FabricInstaller => InstallDownloads.FabricInstaller(SampleUrls, "x"),
                NetFileDescriptorKind.FabricMain => InstallDownloads.FabricMainFile("0.15.0", "1.20.1", @"C:\MC\"),
                NetFileDescriptorKind.FabricApi => InstallDownloads.ResourceDependency(kind, SampleUrls, "x", null, null),
                NetFileDescriptorKind.OptiFabric => InstallDownloads.ResourceDependency(kind, SampleUrls, "x", null, null),
                _ => throw new Exception($"类别 {kind} 没有对应工厂"),
            };
            Assert.Equal(kind, desc.Kind);
            Assert.NotEmpty(desc.Urls);
            Assert.False(string.IsNullOrEmpty(desc.LocalPath));
        }
    }

    #endregion
}
