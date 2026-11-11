namespace PCL.Install.Tests;

using System.Text.Json.Nodes;
using PCL.Download;

/// <summary>
/// 「安装游戏」动作：合并版本 json、迁移 libraries、建 Mod / 资源包目录，
/// 以及目标目录（版本隔离）与进度点。
/// </summary>
public sealed class InstallGameActionTests
{
    private static (TempWorkspace Workspace, InstallAssetBag Assets, InstallLayout Layout, string VersionFolder, string MinecraftFolder) Setup(
        bool withForge = false, bool hasAnyModLoader = false, bool createModsTemp = false)
    {
        var workspace = new TempWorkspace();
        var minecraftFolder = workspace.PathOf("mc") + Path.DirectorySeparatorChar;
        var tempFolder = workspace.PathOf("temp") + Path.DirectorySeparatorChar;
        var versionFolder = Path.Combine(minecraftFolder, "versions", "1.20.1");
        Directory.CreateDirectory(versionFolder);

        File.WriteAllText(Path.Combine(versionFolder, "1.20.1.json"),
            """
            {
              "id": "1.20.1",
              "type": "release",
              "mainClass": "net.minecraft.client.main.Main",
              "libraries": [{ "name": "vanilla:lib:1" }]
            }
            """);

        var layout = InstallLayout.Create(new InstallLayoutRequest
        {
            TempFolder = tempFolder,
            MinecraftVersion = "1.20.1",
            ForgeVersion = withForge ? "1.20.1-47.1.82" : null,
            HasAnyModLoader = hasAnyModLoader || withForge,
        });

        if (withForge)
        {
            var forgeFolder = layout.ForgeFolder!;
            Directory.CreateDirectory(forgeFolder);
            File.WriteAllText(Path.Combine(forgeFolder, Path.GetFileName(forgeFolder) + ".json"),
                """
                {
                  "id": "forge",
                  "inheritsFrom": "1.20.1",
                  "mainClass": "cpw.mods.bootstraplauncher.BootstrapLauncher",
                  "libraries": [{ "name": "forge:lib:2" }],
                  "time": "2020-01-01T00:00:00Z",
                  "releaseTime": "2020-01-01T00:00:00Z"
                }
                """);
        }

        if (createModsTemp)
        {
            Directory.CreateDirectory(layout.ModsRoot);
            File.WriteAllText(Path.Combine(layout.ModsRoot, "foo.jar"), "mod-bytes");
        }

        var assets = new InstallAssetBag()
            .Set("VersionFolder", versionFolder)
            .Set("Layout", layout)
            .Set("McFolderSelected", minecraftFolder)
            .Set("ModsTempFolder", layout.ModsRoot);
        return (workspace, assets, layout, versionFolder, minecraftFolder);
    }

    private static JsonNode MergedJson(string versionFolder)
        => JsonNode.Parse(File.ReadAllText(Path.Combine(versionFolder, "1.20.1.json")))!;

    [Fact]
    public async Task 原版安装只复写版本json并创建资源包目录()
    {
        var (workspace, assets, _, versionFolder, _) = Setup();
        using var _workspace = workspace;
        var context = InstallTestContext.Create(InstallStepKind.InstallGame, assets);

        await new InstallGameAction().ExecuteAsync(context, CancellationToken.None);

        var merged = MergedJson(versionFolder);
        Assert.Equal("1.20.1", merged["id"]!.GetValue<string>());
        Assert.Equal("net.minecraft.client.main.Main", merged["mainClass"]!.GetValue<string>());
        Assert.Single(merged["libraries"]!.AsArray());
        Assert.True(Directory.Exists(Path.Combine(versionFolder, "resourcepacks")));
        Assert.False(Directory.Exists(Path.Combine(versionFolder, "mods")));
    }

    [Fact]
    public async Task 合并加载器json时按原版在前的顺序拼接并去掉继承与时间()
    {
        var (workspace, assets, _, versionFolder, _) = Setup(withForge: true);
        using var _workspace = workspace;
        var context = InstallTestContext.Create(InstallStepKind.InstallGame, assets);

        await new InstallGameAction().ExecuteAsync(context, CancellationToken.None);

        var merged = MergedJson(versionFolder);
        Assert.Equal("cpw.mods.bootstraplauncher.BootstrapLauncher", merged["mainClass"]!.GetValue<string>());
        var libraries = merged["libraries"]!.AsArray();
        Assert.Equal(2, libraries.Count);
        Assert.Equal("vanilla:lib:1", libraries[0]!["name"]!.GetValue<string>());
        Assert.Equal("forge:lib:2", libraries[1]!["name"]!.GetValue<string>());
        Assert.Null(merged["inheritsFrom"]);
        Assert.Null(merged["time"]);
        Assert.Null(merged["releaseTime"]);
    }

    [Fact]
    public async Task 迁移临时目录的支持库到游戏目录()
    {
        var (workspace, assets, layout, _, minecraftFolder) = Setup();
        using var _workspace = workspace;
        var libraryFile = Path.Combine(layout.TempFolder, "libraries", "net", "example", "1.0", "example-1.0.jar");
        Directory.CreateDirectory(Path.GetDirectoryName(libraryFile)!);
        File.WriteAllText(libraryFile, "library-bytes");
        var context = InstallTestContext.Create(InstallStepKind.InstallGame, assets);

        await new InstallGameAction().ExecuteAsync(context, CancellationToken.None);

        var copied = Path.Combine(minecraftFolder, "libraries", "net", "example", "1.0", "example-1.0.jar");
        Assert.True(File.Exists(copied));
        Assert.Equal("library-bytes", File.ReadAllText(copied));
    }

    [Fact]
    public async Task 临时目录存在mods时整目录复制到目标()
    {
        var (workspace, assets, _, versionFolder, _) = Setup(createModsTemp: true);
        using var _workspace = workspace;
        var context = InstallTestContext.Create(InstallStepKind.InstallGame, assets);

        await new InstallGameAction().ExecuteAsync(context, CancellationToken.None);

        var copied = Path.Combine(versionFolder, "mods", "foo.jar");
        Assert.True(File.Exists(copied));
        Assert.Equal("mod-bytes", File.ReadAllText(copied));
    }

    [Fact]
    public async Task 无临时mods且有加载器时创建空mods目录()
    {
        var (workspace, assets, _, versionFolder, _) = Setup(hasAnyModLoader: true);
        using var _workspace = workspace;
        var context = InstallTestContext.Create(InstallStepKind.InstallGame, assets);
        var options = new InstallGameOptions { HasAnyModLoader = true };

        await new InstallGameAction(options).ExecuteAsync(context, CancellationToken.None);

        Assert.True(Directory.Exists(Path.Combine(versionFolder, "mods")));
        Assert.Empty(Directory.EnumerateFileSystemEntries(Path.Combine(versionFolder, "mods")));
    }

    [Fact]
    public async Task 版本隔离时目标目录由宿主解析器决定()
    {
        var (workspace, assets, _, versionFolder, _) = Setup();
        using var _workspace = workspace;
        var isolatedFolder = workspace.PathOf("instances", "1.20.1");
        var context = InstallTestContext.Create(InstallStepKind.InstallGame, assets);
        var options = new InstallGameOptions { ResolveTargetFolder = _ => isolatedFolder };

        await new InstallGameAction(options).ExecuteAsync(context, CancellationToken.None);

        Assert.True(Directory.Exists(Path.Combine(isolatedFolder, "resourcepacks")));
        Assert.False(Directory.Exists(Path.Combine(versionFolder, "resourcepacks")));
    }

    [Fact]
    public async Task 中间产物输出时忽略隔离解析器()
    {
        var (workspace, assets, _, versionFolder, _) = Setup();
        using var _workspace = workspace;
        var isolatedFolder = workspace.PathOf("instances", "1.20.1");
        var context = InstallTestContext.Create(InstallStepKind.InstallGame, assets);
        var options = new InstallGameOptions
        {
            IsIntermediateOutput = true,
            ResolveTargetFolder = _ => isolatedFolder,
        };

        await new InstallGameAction(options).ExecuteAsync(context, CancellationToken.None);

        Assert.True(Directory.Exists(Path.Combine(versionFolder, "resourcepacks")));
        Assert.False(Directory.Exists(Path.Combine(isolatedFolder, "resourcepacks")));
    }

    [Fact]
    public async Task 进度按合并与迁移两个节点上报()
    {
        var (workspace, assets, _, _, _) = Setup();
        using var _workspace = workspace;
        var context = InstallTestContext.Create(InstallStepKind.InstallGame, assets);
        var fractions = new List<double>();
        var progressContext = new InstallStepContext
        {
            Node = context.Node,
            Assets = context.Assets,
            Engine = context.Engine,
            Progress = new SyncProgress<InstallStepProgress>(p => fractions.Add(p.Fraction)),
        };

        await new InstallGameAction().ExecuteAsync(progressContext, CancellationToken.None);

        Assert.Equal([0.2, 0.8], fractions);
    }

    [Fact]
    public async Task 缺少布局资产时抛出()
    {
        using var workspace = new TempWorkspace();
        var assets = new InstallAssetBag().Set("VersionFolder", workspace.PathOf("versions", "1.20.1"));
        var context = InstallTestContext.Create(InstallStepKind.InstallGame, assets);

        var ex = await Assert.ThrowsAsync<MissingInstallAssetException>(
            () => new InstallGameAction().ExecuteAsync(context, CancellationToken.None));

        Assert.Equal("Layout", ex.AssetName);
    }

    [Fact]
    public async Task 版本json缺失时直接失败()
    {
        var (workspace, assets, _, versionFolder, _) = Setup();
        using var _workspace = workspace;
        File.Delete(Path.Combine(versionFolder, "1.20.1.json"));
        var context = InstallTestContext.Create(InstallStepKind.InstallGame, assets);

        await Assert.ThrowsAsync<InvalidDataException>(
            () => new InstallGameAction().ExecuteAsync(context, CancellationToken.None));
    }
}
