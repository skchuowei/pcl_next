namespace PCL.Download.Tests;

/// <summary>
/// InstallLayout（合并安装的临时目录布局）与 InstallManifestRules（清单里的字符串规则）测试。
/// <para>
/// 这一批全是字符串拼接，拼错不抛异常，只表现为「OptiFine 装到别的目录」「合并时少一份源」
/// 「修改页认不出已装 Mod」。所以每条规则都要有用例。
/// </para>
/// </summary>
public class InstallLayoutTests
{
    private const string TempRoot = @"C:\PCL\Temp";

    private static InstallLayout Create(
        string? optiFineInstanceName = null,
        string? forge = null,
        string? neoForge = null,
        string? liteLoader = null,
        string? fabric = null,
        bool? hasAnyModLoader = null,
        string minecraftVersion = "1.20.1",
        string tempFolder = TempRoot + @"\")
        => InstallLayout.Create(new InstallLayoutRequest
        {
            TempFolder = tempFolder,
            MinecraftVersion = minecraftVersion,
            OptiFineInstanceName = optiFineInstanceName,
            ForgeVersion = forge,
            NeoForgeVersion = neoForge,
            LiteLoaderVersion = liteLoader,
            FabricVersion = fabric,
            HasAnyModLoader = hasAnyModLoader,
        });

    /// <summary>期望路径：以临时根目录打底，按段拼接（与实现的 Path.Combine 口径一致）。</summary>
    private static string Under(params string[] parts)
    {
        var all = new string[parts.Length + 1];
        all[0] = TempRoot;
        parts.CopyTo(all, 1);
        return Path.Combine(all);
    }

    [Fact]
    public void Create_只有原版时各加载器目录都为空()
    {
        var layout = Create();

        Assert.Null(layout.OptiFineFolder);
        Assert.Null(layout.ForgeFolder);
        Assert.Null(layout.NeoForgeFolder);
        Assert.Null(layout.LiteLoaderFolder);
        Assert.Null(layout.FabricFolder);
        Assert.False(layout.OptiFineAsMod);
        Assert.Empty(layout.LogLines());
    }

    [Fact]
    public void Create_只选OptiFine时落在versions下的实例目录()
    {
        var layout = Create(optiFineInstanceName: "1.12.2-OptiFine_HD_U_G5");

        Assert.False(layout.OptiFineAsMod);
        Assert.Equal(Under("versions", "1.12.2-OptiFine_HD_U_G5"), layout.OptiFineFolder);
    }

    [Fact]
    public void Create_OptiFine加任意Mod加载器时作为Mod落在mods根目录()
    {
        var layout = Create(optiFineInstanceName: "1.20.1-OptiFine_HD_U_I6", forge: "1.20.1-47.1.82");

        Assert.True(layout.OptiFineAsMod);
        Assert.Equal(Under("mods"), layout.OptiFineFolder);
    }

    [Fact]
    public void Create_OptiFine配LiteLoader时落在mods的版本号子目录()
    {
        // #8147：LiteLoader 只从 mods/{MC版本}/ 读 Mod，放根目录加载不到
        var layout = Create(optiFineInstanceName: "1.12.2-OptiFine_HD_U_G5", liteLoader: "1.12.2");

        Assert.True(layout.OptiFineAsMod);
        Assert.Equal(Under("mods", "1.20.1"), layout.OptiFineFolder);
    }

    [Fact]
    public void Create_OptiFine配LiteLoader与Fabric时仍落在版本号子目录()
    {
        var layout = Create(optiFineInstanceName: "1.12.2-OptiFine_HD_U_G5", liteLoader: "1.12.2", fabric: "0.15.0");

        Assert.Equal(Under("mods", "1.20.1"), layout.OptiFineFolder);
    }

    [Fact]
    public void Create_Forge目录名带forge前缀且保留原版本串()
    {
        var layout = Create(forge: "1.20.1-47.1.82");

        Assert.Equal(Under("versions", "forge-1.20.1-47.1.82"), layout.ForgeFolder);
    }

    [Fact]
    public void Create_NeoForge目录名带neoforge前缀()
    {
        var layout = Create(neoForge: "20.4.237");

        Assert.Equal(Under("versions", "neoforge-20.4.237"), layout.NeoForgeFolder);
    }

    [Fact]
    public void Create_LiteLoader目录名是MC版本加后缀()
    {
        var layout = Create(liteLoader: "1.12.2", minecraftVersion: "1.12.2");

        Assert.Equal(Under("versions", "1.12.2-LiteLoader"), layout.LiteLoaderFolder);
    }

    [Fact]
    public void Create_Fabric目录名带loader版本与MC版本()
    {
        var layout = Create(fabric: "0.15.0", minecraftVersion: "1.20.1");

        Assert.Equal(Under("versions", "fabric-loader-0.15.0-1.20.1"), layout.FabricFolder);
    }

    [Fact]
    public void Create_临时目录带不带尾分隔符结果一致()
    {
        var withSlash = Create(forge: "1.20.1-47.1.82", tempFolder: TempRoot + @"\");
        var withoutSlash = Create(forge: "1.20.1-47.1.82", tempFolder: TempRoot);

        Assert.Equal(withoutSlash.ForgeFolder, withSlash.ForgeFolder);
        Assert.Equal(withoutSlash.ModsRoot, withSlash.ModsRoot);
        Assert.Equal(withoutSlash.TempFolder, withSlash.TempFolder);
    }

    [Fact]
    public void Create_两个根目录带尾分隔符供直接拼接文件名()
    {
        // VB 侧是 $"{TempMcFolder}mods\" 这种拼法，尾部必须带分隔符
        var layout = Create();

        Assert.Equal(Under("mods") + Path.DirectorySeparatorChar, layout.ModsRoot);
        Assert.Equal(Under("versions") + Path.DirectorySeparatorChar, layout.VersionsRoot);
    }

    [Fact]
    public void BuildOptiFineInstallerPath_在Mod目录下拼出完整路径()
    {
        var layout = Create(optiFineInstanceName: "1.20.1-OptiFine_HD_U_I6", forge: "1.20.1-47.1.82");

        Assert.Equal(Under("mods", "OptiFine_1.20.1_HD_U_I6.jar"),
            layout.BuildOptiFineInstallerPath("OptiFine_1.20.1_HD_U_I6.jar"));
    }

    [Fact]
    public void BuildOptiFineInstallerPath_未选OptiFine时返回null()
    {
        Assert.Null(Create().BuildOptiFineInstallerPath("x.jar"));
    }

    [Fact]
    public void LogLines_顺序与原实现一致()
    {
        var layout = Create(optiFineInstanceName: "1.20.1-OptiFine_HD_U_I6", forge: "1.20.1-47.1.82",
            neoForge: "20.4.237", liteLoader: "1.20.1", fabric: "0.15.0");

        var lines = layout.LogLines();

        Assert.Equal(5, lines.Count);
        Assert.StartsWith("OptiFine 缓存：", lines[0]);
        Assert.StartsWith("Forge 缓存：", lines[1]);
        Assert.StartsWith("NeoForge 缓存：", lines[2]);
        // Fabric 在 LiteLoader 之前是原实现的输出顺序，不要「顺手」按字母序整理
        Assert.StartsWith("Fabric 缓存：", lines[3]);
        Assert.StartsWith("LiteLoader 缓存：", lines[4]);
    }

    [Fact]
    public void Create_宿主声明无Mod加载器时OptiFine不降级为Mod()
    {
        // 对齐 VB 的 ModLoaderVersions.Any：字典为空则 OptiFine 参与版本合并
        var layout = Create(optiFineInstanceName: "1.20.1-OptiFine_HD_U_I6", forge: "1.20.1-47.1.82",
            hasAnyModLoader: false);

        Assert.False(layout.OptiFineAsMod);
        Assert.Equal(Under("versions", "1.20.1-OptiFine_HD_U_I6"), layout.OptiFineFolder);
    }

    [Fact]
    public void Create_未显式声明时按传入的版本号推断有无Mod加载器()
    {
        Assert.True(Create(optiFineInstanceName: "x", fabric: "0.15.0").OptiFineAsMod);
        Assert.False(Create(optiFineInstanceName: "x").OptiFineAsMod);
    }

    [Fact]
    public void ManagedModPath_反斜杠转正斜杠并转小写()
    {
        Assert.Equal("mods/sub/mymod.jar", InstallManifestRules.ManagedModPath(@"sub\MyMod.jar"));
    }

    [Fact]
    public void ManagedModPath_根目录下的文件也带mods前缀()
    {
        Assert.Equal("mods/optifine_hd_u_g5.jar", InstallManifestRules.ManagedModPath("OptiFine_HD_U_G5.jar"));
    }

    [Fact]
    public void NormalizeLoaderVersion_Forge剥掉MC前缀()
    {
        Assert.Equal("41.2.8", InstallManifestRules.NormalizeLoaderVersion(ModLoaders.Forge, "1.19.3-41.2.8"));
        Assert.Equal("47.1.82", InstallManifestRules.NormalizeLoaderVersion(ModLoaders.Forge, "1.20.1-47.1.82"));
    }

    [Fact]
    public void NormalizeLoaderVersion_Forge版本串本身不带前缀时原样保留()
    {
        // 47.1.82 不以 "1." 开头，不满足剥离条件
        Assert.Equal("47.1.82", InstallManifestRules.NormalizeLoaderVersion(ModLoaders.Forge, "47.1.82"));
    }

    [Fact]
    public void NormalizeLoaderVersion_Forge老命名按原实现取最后一段()
    {
        // 1.7.10-10.13.4.1614-1.7.10 会被剥成 1.7.10，这是原实现的行为，不做「顺手修正」：
        // 改了会让已装实例在修改页里对不上号
        Assert.Equal("1.7.10",
            InstallManifestRules.NormalizeLoaderVersion(ModLoaders.Forge, "1.7.10-10.13.4.1614-1.7.10"));
    }

    [Fact]
    public void NormalizeLoaderVersion_NeoForge只剥1201前缀()
    {
        Assert.Equal("47.1.82", InstallManifestRules.NormalizeLoaderVersion(ModLoaders.NeoForge, "1.20.1-47.1.82"));
        // 1.20.4 起版本串不带 MC 前缀
        Assert.Equal("20.4.237", InstallManifestRules.NormalizeLoaderVersion(ModLoaders.NeoForge, "20.4.237"));
    }

    [Fact]
    public void NormalizeLoaderVersion_其他加载器原样返回()
    {
        Assert.Equal("0.15.0", InstallManifestRules.NormalizeLoaderVersion(ModLoaders.Fabric, "0.15.0"));
        Assert.Equal("1.12.2", InstallManifestRules.NormalizeLoaderVersion(ModLoaders.LiteLoader, "1.12.2"));
    }
}
