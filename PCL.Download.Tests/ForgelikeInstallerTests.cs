namespace PCL.Download.Tests;

/// <summary>
/// ForgelikeInstaller（Forge/NeoForge 安装的纯逻辑）测试。
/// <para>
/// 这些规则全部来自「踩过坑之后的特殊处理」：整合包版本串自带 MC 版本、NeoForge 1.20.1 的
/// forge 包名、快照版本在 maven 里的下划线、Java 9+ 的模块导出。它们出错时几乎不会抛异常，
/// 只表现为「找不到安装器」或「进度条卡住」。
/// </para>
/// </summary>
public class ForgelikeInstallerTests
{
    // ---- 版本串归一化 ----

    [Theory]
    [InlineData("41.2.8", "1.19.3", "1.19.3", "41.2.8")]
    [InlineData("47.2.0", "1.20.1", "1.20.1", "47.2.0")]
    [InlineData("14.23.5.2860", "1.7.10", "1.7.10", "14.23.5.2860")]
    public void Normalize_普通版本串原样返回(string loaderVersion, string inherit, string expectInherit, string expectLoader)
    {
        var target = ForgelikeInstaller.Normalize(false, loaderVersion, inherit);
        Assert.Equal(expectInherit, target.Inherit);
        Assert.Equal(expectLoader, target.LoaderVersion);
    }

    [Fact]
    public void Normalize_版本串自带MC版本时以它为准()
    {
        // 整合包里出现过「声明 1.19 但实际用 1.19.3 的 Forge」，按声明值去找会找不到版本
        var target = ForgelikeInstaller.Normalize(false, "1.19.3-41.2.8", "1.19");
        Assert.Equal("1.19.3", target.Inherit);
        Assert.Equal("41.2.8", target.LoaderVersion);
    }

    [Fact]
    public void Normalize_NeoForge_为1点20点1补齐包名前缀()
    {
        var target = ForgelikeInstaller.Normalize(true, "47.1.99", "1.20.1");
        Assert.Equal("1.20.1", target.Inherit);
        Assert.Equal("1.20.1-47.1.99", target.LoaderVersion);
    }

    [Fact]
    public void Normalize_NeoForge_已有前缀不重复添加()
    {
        var target = ForgelikeInstaller.Normalize(true, "1.20.1-47.1.99", "1.20.1");
        Assert.Equal("1.20.1-47.1.99", target.LoaderVersion);
    }

    [Fact]
    public void Normalize_NeoForge_其他版本不动版本串()
    {
        var target = ForgelikeInstaller.Normalize(true, "20.4.30", "1.20.4");
        Assert.Equal("1.20.4", target.Inherit);
        Assert.Equal("20.4.30", target.LoaderVersion);
    }

    [Fact]
    public void Normalize_NeoForge不做Forge的短横线拆分()
    {
        // 1.19.3-41.2.8 的形态只对 Forge 成立；NeoForge 的 API 名里没有这种组合
        var target = ForgelikeInstaller.Normalize(true, "1.20.1-47.1.99", "1.20.4");
        Assert.Equal("1.20.4", target.Inherit);
        Assert.Equal("1.20.1-47.1.99", target.LoaderVersion);
    }

    // ---- 安装器地址 ----

    [Fact]
    public void BuildForgeInstaller_镜像在前官方在后()
    {
        var file = ForgelikeInstaller.BuildForgeInstaller("1.20.1", "47.1.0", "installer", "jar", "abc123");

        Assert.Equal(2, file.Urls.Count);
        Assert.Equal(
            "https://bmclapi2.bangbang93.com/maven/net/minecraftforge/forge/1.20.1-47.1.0/forge-1.20.1-47.1.0-installer.jar",
            file.Urls[0]);
        Assert.Equal(
            "https://files.minecraftforge.net/maven/net/minecraftforge/forge/1.20.1-47.1.0/forge-1.20.1-47.1.0-installer.jar",
            file.Urls[1]);
        Assert.Equal(64 * 1024, file.MinSize);
        Assert.Equal("abc123", file.Hash);
    }

    [Fact]
    public void BuildForgeInstaller_快照版本的横线换成下划线()
    {
        // 快照在 Forge 的 maven 里是 1.20.2_pre1，沿用横线会 404
        var file = ForgelikeInstaller.BuildForgeInstaller("1.20.2-pre1", "48.0.1", "installer", "jar", null);
        Assert.Contains("/forge/1.20.2_pre1-48.0.1/forge-1.20.2_pre1-48.0.1-installer.jar", file.Urls[1]);
    }

    [Fact]
    public void BuildForgeInstaller_带分支后缀的文件版本名原样使用()
    {
        var file = ForgelikeInstaller.BuildForgeInstaller("1.7.10", "14.23.5.2860-1.7.10", "universal", "zip", null);
        Assert.EndsWith("/forge-1.7.10-14.23.5.2860-1.7.10-universal.zip", file.Urls[1]);
    }

    [Fact]
    public void BuildNeoForgeInstaller_常规版本走neoforge包()
    {
        var file = ForgelikeInstaller.BuildNeoForgeInstaller("1.20.4", "20.4.30");

        Assert.Equal(
            "https://bmclapi2.bangbang93.com/maven/net/neoforged/neoforge/20.4.30/neoforge-20.4.30-installer.jar",
            file.Urls[0]);
        Assert.Equal(
            "https://maven.neoforged.net/releases/net/neoforged/neoforge/20.4.30/neoforge-20.4.30-installer.jar",
            file.Urls[1]);
        Assert.Null(file.Hash);
    }

    [Fact]
    public void BuildNeoForgeInstaller_1点20点1仍走forge包()
    {
        var file = ForgelikeInstaller.BuildNeoForgeInstaller("1.20.1", "1.20.1-47.1.99");
        Assert.Contains("/net/neoforged/forge/1.20.1-47.1.99/forge-1.20.1-47.1.99-installer.jar", file.Urls[1]);
    }

    // ---- 启动参数 ----

    [Fact]
    public void BuildArguments_Java8不加模块导出参数()
    {
        var arguments = ForgelikeInstaller.BuildArguments(8, @"C:\tmp\forge_installer.jar", @"C:\inst\forge.jar", @"C:\mc");
        Assert.Equal(
            @"-cp ""C:\tmp\forge_installer.jar;C:\inst\forge.jar"" com.bangbang93.ForgeInstaller ""C:\mc""",
            arguments);
    }

    [Theory]
    [InlineData(9)]
    [InlineData(17)]
    [InlineData(21)]
    public void BuildArguments_Java9起加模块导出参数(int javaMajor)
    {
        var arguments = ForgelikeInstaller.BuildArguments(javaMajor, "i.jar", "t.jar", "mc");
        Assert.StartsWith(
            "--add-exports cpw.mods.bootstraplauncher/cpw.mods.bootstraplauncher=ALL-UNNAMED ", arguments);
    }

    [Fact]
    public void BuildArguments_JavaWrapper模式带上临时目录与wrapper()
    {
        var arguments = ForgelikeInstaller.BuildArguments(
            17, "i.jar", "t.jar", "mc", javaWrapperPath: "jlw.jar", javaWrapperTempDir: @"C:\temp", useJavaWrapper: true);

        Assert.Contains(@"-Doolloo.jlw.tmpdir=""C:\temp""", arguments);
        Assert.Contains(@"-jar ""jlw.jar""", arguments);
        Assert.Contains("com.bangbang93.ForgeInstaller \"mc\"", arguments);
    }

    [Fact]
    public void BuildArguments_要求wrapper但拿不到路径时退回普通模式()
    {
        var arguments = ForgelikeInstaller.BuildArguments(17, "i.jar", "t.jar", "mc", useJavaWrapper: true);
        Assert.DoesNotContain("oolloo", arguments);
        Assert.DoesNotContain("-jar", arguments);
    }

    // ---- 进度映射 ----

    [Theory]
    [InlineData("Extracting json", 0.27)]
    [InlineData("Building Processors", 0.38)]
    [InlineData("Task: MERGE_MAPPING", 0.5)]
    [InlineData("Processing Complete", 0.67)]
    [InlineData("Injecting profile", 0.98)]
    public void ReadProgress_识别安装器阶段(string line, double expected)
    {
        var step = ForgelikeInstaller.ReadProgress(line);
        Assert.NotNull(step);
        Assert.Equal(expected, step!.Value.Progress, 3);
        Assert.False(step.Value.IsIncrement);
    }

    [Fact]
    public void ReadProgress_校验命中时是累加而非覆盖()
    {
        // 安装器会连续打很多行「File exists」，靠累加推进细粒度进度；改成赋值进度条会来回跳
        var step = ForgelikeInstaller.ReadProgress("  File exists: Checksum validated.");
        Assert.NotNull(step);
        Assert.True(step!.Value.IsIncrement);
        Assert.Equal(0.003, step.Value.Progress, 4);
    }

    [Theory]
    [InlineData("File exists: Checksum validated.")]   // 少了两个前导空格
    [InlineData("Splitting:")]                          // 少了尾部空格
    [InlineData("extracting json")]                     // 大小写不同
    [InlineData("")]
    public void ReadProgress_原文不完全一致就不认(string line)
        => Assert.Null(ForgelikeInstaller.ReadProgress(line));

    [Fact]
    public void ReadProgress_null当作不认识()
        => Assert.Null(ForgelikeInstaller.ReadProgress(null));
}
