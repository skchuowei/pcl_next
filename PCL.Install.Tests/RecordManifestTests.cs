namespace PCL.Install.Tests;

using System.Text.Json.Nodes;
using PCL.Download;

/// <summary>
/// 「记录安装清单」：对照 VB <c>AssembleRecordManifest</c> 锁住清单的形状与取值口径
/// （相对路径规范化、SHA1 小写、Forge / NeoForge 版本剥前缀、可选键省略、保留实例其它配置）。
/// </summary>
public sealed class RecordManifestTests
{
    private static (TempWorkspace Workspace, InstallAssetBag Assets, string VersionFolder, string TargetModPath) Setup(
        Dictionary<string, string>? managedFileSources = null)
    {
        var workspace = new TempWorkspace();
        var targetFolder = workspace.PathOf("mc", "versions", "1.20.1");
        var modsTempFolder = workspace.PathOf("temp", "mods") + Path.DirectorySeparatorChar;

        var targetModPath = Path.Combine(targetFolder, "mods", "sub", "MyMod.jar");
        Directory.CreateDirectory(Path.GetDirectoryName(targetModPath)!);
        File.WriteAllBytes(targetModPath, [1, 2, 3, 4]);

        var sources = managedFileSources ?? new Dictionary<string, string>
        {
            ["FabricApi"] = Path.Combine(modsTempFolder, "sub", "MyMod.jar"),
        };

        var assets = new InstallAssetBag()
            .Set("TargetFolder", targetFolder)
            .Set("VersionFolder", targetFolder)
            .Set("ManagedFileSources", sources);
        return (workspace, assets, targetFolder, targetModPath);
    }

    private static InstallManifestComponents Components() => new()
    {
        MinecraftVersion = "1.20.1",
        ModLoaderVersions = new Dictionary<ModLoaders, string>
        {
            [ModLoaders.Forge] = "1.20.1-47.1.82",
            [ModLoaders.NeoForge] = "1.20.1-20.4.237",
            [ModLoaders.Fabric] = "0.15.0",
            [ModLoaders.LiteLoader] = "1.12.2",
        },
        OptiFineEntry = new JsonObject { ["InstanceName"] = "1.20.1-OptiFine_HD_U_I6" },
        FabricApiVersion = "0.92.0+1.20.1",
        OptiFabricVersion = "1.16.5",
    };

    private static JsonObject ReadManifest(string versionFolder)
    {
        var configPath = Path.Combine(versionFolder, "PCL", "config.json");
        var root = JsonNode.Parse(File.ReadAllText(configPath))!.AsObject();
        return root[InstallManifestStore.ConfigKey]!.AsObject();
    }

    private static async Task<(TempWorkspace Workspace, InstallAssetBag Assets, string VersionFolder, string TargetModPath)>
        ExecuteAsync(Dictionary<string, string>? managedFileSources = null, InstallManifestComponents? components = null)
    {
        var setup = Setup(managedFileSources);
        var context = InstallTestContext.Create(InstallStepKind.RecordManifest, setup.Assets);
        var action = new RecordManifestAction(components ?? Components(), setup.Workspace.PathOf("temp", "mods") + Path.DirectorySeparatorChar);
        await action.ExecuteAsync(context, CancellationToken.None);
        return setup;
    }

    [Fact]
    public async Task 清单写入实例配置并保留其它键()
    {
        var (workspace, assets, versionFolder, _) = Setup();
        using var _workspace = workspace;
        var configFolder = Path.Combine(versionFolder, "PCL");
        Directory.CreateDirectory(configFolder);
        File.WriteAllText(Path.Combine(configFolder, "config.json"), """{"OtherKey":1,"Nested":{"A":true}}""");
        var context = InstallTestContext.Create(InstallStepKind.RecordManifest, assets);
        var action = new RecordManifestAction(Components(), workspace.PathOf("temp", "mods") + Path.DirectorySeparatorChar);

        await action.ExecuteAsync(context, CancellationToken.None);

        var root = JsonNode.Parse(File.ReadAllText(Path.Combine(configFolder, "config.json")))!.AsObject();
        Assert.Equal(1, root["OtherKey"]!.GetValue<int>());
        Assert.True(root["Nested"]!["A"]!.GetValue<bool>());
        Assert.NotNull(root[InstallManifestStore.ConfigKey]);
    }

    [Fact]
    public async Task 清单字段与VB口径一致()
    {
        var (workspace, _, versionFolder, targetModPath) = await ExecuteAsync();
        using var _workspace = workspace;

        var manifest = ReadManifest(versionFolder);
        Assert.Equal(1, manifest["SchemaVersion"]!.GetValue<int>());

        var components = manifest["Components"]!.AsObject();
        Assert.Equal("1.20.1", components["MinecraftVersion"]!.GetValue<string>());
        var loaders = components["ModLoaderVersions"]!.AsObject();
        Assert.Equal("47.1.82", loaders["Forge"]!.GetValue<string>());
        Assert.Equal("20.4.237", loaders["NeoForge"]!.GetValue<string>());
        Assert.Equal("0.15.0", loaders["Fabric"]!.GetValue<string>());
        Assert.Equal("1.12.2", loaders["LiteLoader"]!.GetValue<string>());
        Assert.Equal("1.20.1-OptiFine_HD_U_I6", components["OptiFineEntry"]!["InstanceName"]!.GetValue<string>());
        Assert.Equal("0.92.0+1.20.1", components["FabricApiVersion"]!.GetValue<string>());
        Assert.Equal("1.16.5", components["OptiFabricVersion"]!.GetValue<string>());

        var files = manifest["ManagedFiles"]!.AsArray();
        var file = Assert.Single(files)!.AsObject();
        Assert.Equal("mods/sub/mymod.jar", file["RelativePath"]!.GetValue<string>());
        Assert.Equal(HashHelper.ComputeFile(targetModPath, HashType.Sha1), file["Sha1"]!.GetValue<string>());
        Assert.Equal(40, file["Sha1"]!.GetValue<string>().Length);
        Assert.Equal(File.GetLastWriteTimeUtc(targetModPath), file["LastWriteTimeUtc"]!.GetValue<DateTime>());
        Assert.Equal("FabricApi", file["Component"]!.GetValue<string>());

        var lastModified = manifest["LastModifiedUtc"]!.GetValue<DateTime>();
        Assert.True((DateTime.UtcNow - lastModified).Duration() < TimeSpan.FromMinutes(1));
    }

    [Fact]
    public async Task 可选组件为空时不写入对应键()
    {
        var components = new InstallManifestComponents { MinecraftVersion = "1.20.1" };
        var (workspace, _, versionFolder, _) = await ExecuteAsync(components: components);
        using var _workspace = workspace;

        var manifest = ReadManifest(versionFolder);
        var componentJson = manifest["Components"]!.AsObject();
        Assert.Empty(componentJson["ModLoaderVersions"]!.AsObject());
        Assert.False(componentJson.ContainsKey("OptiFineEntry"));
        Assert.False(componentJson.ContainsKey("FabricApiVersion"));
        Assert.False(componentJson.ContainsKey("OptiFabricVersion"));
    }

    [Fact]
    public async Task 没有受管理文件时清单为空数组()
    {
        var (workspace, _, versionFolder, _) = await ExecuteAsync(new Dictionary<string, string>());
        using var _workspace = workspace;

        Assert.Empty(ReadManifest(versionFolder)["ManagedFiles"]!.AsArray());
    }

    [Fact]
    public async Task 目标文件缺失时直接失败()
    {
        var (workspace, assets, _, targetModPath) = Setup();
        using var _workspace = workspace;
        File.Delete(targetModPath);
        var context = InstallTestContext.Create(InstallStepKind.RecordManifest, assets);
        var action = new RecordManifestAction(Components(), workspace.PathOf("temp", "mods") + Path.DirectorySeparatorChar);

        await Assert.ThrowsAsync<FileNotFoundException>(
            () => action.ExecuteAsync(context, CancellationToken.None));
    }

    [Fact]
    public async Task 实例配置损坏时中止写入并保留原文件()
    {
        var (workspace, assets, versionFolder, _) = Setup();
        using var _workspace = workspace;
        var configFolder = Path.Combine(versionFolder, "PCL");
        Directory.CreateDirectory(configFolder);
        var configPath = Path.Combine(configFolder, "config.json");
        File.WriteAllText(configPath, "{ 这不是 JSON");
        var context = InstallTestContext.Create(InstallStepKind.RecordManifest, assets);
        var action = new RecordManifestAction(Components(), workspace.PathOf("temp", "mods") + Path.DirectorySeparatorChar);

        await Assert.ThrowsAsync<InvalidDataException>(
            () => action.ExecuteAsync(context, CancellationToken.None));

        Assert.Equal("{ 这不是 JSON", File.ReadAllText(configPath));
    }

    [Fact]
    public async Task 缺少受管理文件来源资产时抛出()
    {
        using var workspace = new TempWorkspace();
        var assets = new InstallAssetBag()
            .Set("TargetFolder", workspace.PathOf("mc"))
            .Set("VersionFolder", workspace.PathOf("mc", "versions", "1.20.1"));
        var context = InstallTestContext.Create(InstallStepKind.RecordManifest, assets);
        var action = new RecordManifestAction(Components(), workspace.PathOf("temp", "mods") + Path.DirectorySeparatorChar);

        var ex = await Assert.ThrowsAsync<MissingInstallAssetException>(
            () => action.ExecuteAsync(context, CancellationToken.None));

        Assert.Equal("ManagedFileSources", ex.AssetName);
    }

    [Fact]
    public async Task 提交前的清单结构可直接被反序列化为同形状记录()
    {
        var (workspace, _, versionFolder, _) = await ExecuteAsync();
        using var _workspace = workspace;

        var manifest = ReadManifest(versionFolder);
        Assert.True(manifest.ContainsKey("Components"));
        Assert.True(manifest.ContainsKey("LastModifiedUtc"));
        Assert.True(manifest.ContainsKey("ManagedFiles"));
        // 缩进写回：便于人工 diff，避免整份配置被压成一行。
        var text = File.ReadAllText(Path.Combine(versionFolder, "PCL", "config.json"));
        Assert.Contains("\n", text);
        Assert.Contains("  \"", text);
    }

    [Fact]
    public void 清单格式版本与PCLCS常量一致()
    {
        Assert.Equal(1, InstallManifest.CurrentSchemaVersion);
    }
}
