using Xunit;

namespace PCL.Download.Tests;

/// <summary>
/// 2026-11-08 真机回归：NeoForge 26.3.0.101 安装器报
/// <c>There is no minecraft installation at: ...\TaskTemp\1273-692147"</c>。
/// 守门两类问题：目录参数尾斜杠把闭合引号转义掉；原版 json 的 Java 主版本要求没被读取。
/// </summary>
public class InstallerArgumentRegressionTests
{
    /// <summary>
    /// 按 CommandLineToArgvW 规则守门：每个引号前的连续反斜杠必须是偶数，
    /// 奇数说明该引号被转义（<c>"C:\mc\"</c> 就是 1 个反斜杠 + 闭合引号）。
    /// 盘符根特例写成 <c>"C:\\"</c> 时是 2 个反斜杠，属于正确闭合，不应误报。
    /// </summary>
    private static void AssertNoEscapedQuote(string arguments)
    {
        var backslashes = 0;
        for (var i = 0; i < arguments.Length; i++)
        {
            if (arguments[i] == '\\')
            {
                backslashes++;
                continue;
            }
            if (arguments[i] == '"')
                Assert.True(backslashes % 2 == 0,
                    $"第 {i} 位的引号被 {backslashes} 个反斜杠转义：{arguments}");
            backslashes = 0;
        }
    }

    // ---- 目录引号包装 ----

    [Theory]
    [InlineData(@"C:\Temp\mc\", "\"C:\\Temp\\mc\"")]
    [InlineData(@"C:\Temp\mc\\", "\"C:\\Temp\\mc\"")]
    [InlineData(@"C:\Temp\mc/", "\"C:\\Temp\\mc\"")]
    [InlineData(@"C:\Temp\mc", "\"C:\\Temp\\mc\"")]
    [InlineData(@"\\server\share\mc\", "\"\\\\server\\share\\mc\"")]
    public void QuoteDirectory_去掉尾部斜杠(string path, string expected)
    {
        Assert.Equal(expected, InstallerArguments.QuoteDirectory(path));
        AssertNoEscapedQuote(expected);
    }

    [Fact]
    public void QuoteDirectory_盘符根保留反斜杠但闭合引号不被转义()
    {
        // C:\ 去尾会退化成 C:（当前目录），因此改成引号内写 \\，由解析器还原成一个字面反斜杠
        Assert.Equal("\"C:\\\\\"", InstallerArguments.QuoteDirectory(@"C:\"));
        AssertNoEscapedQuote(InstallerArguments.QuoteDirectory(@"C:\"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void QuoteDirectory_空值保持旧行为(string? path)
        => Assert.Equal("\"\"", InstallerArguments.QuoteDirectory(path));

    // ---- Forge / NeoForge 启动参数 ----

    [Fact]
    public void ForgelikeArguments_临时目录带尾斜杠时不再混入字面引号()
    {
        var arguments = ForgelikeInstaller.BuildArguments(
            25, @"C:\Temp\forge_installer.jar", @"C:\Temp\forge.jar", @"C:\Temp\TaskTemp\1273-692147\");

        AssertNoEscapedQuote(arguments);
        Assert.Contains(@"com.bangbang93.ForgeInstaller ""C:\Temp\TaskTemp\1273-692147""", arguments);
        Assert.DoesNotContain("692147\\\"", arguments);
    }

    [Fact]
    public void ForgelikeArguments_Wrapper双目录都带尾斜杠也安全()
    {
        var arguments = ForgelikeInstaller.BuildArguments(
            25, "i.jar", "t.jar", @"C:\Temp\mc\",
            javaWrapperPath: "jlw.jar", javaWrapperTempDir: @"C:\Temp\PCL\", useJavaWrapper: true);

        AssertNoEscapedQuote(arguments);
        Assert.Contains(@"-Doolloo.jlw.tmpdir=""C:\Temp\PCL""", arguments);
        Assert.Contains(@"com.bangbang93.ForgeInstaller ""C:\Temp\mc""", arguments);
    }

    [Fact]
    public void ForgelikeArguments_尾部斜杠修复不影响Java9模块导出前缀()
    {
        var arguments = ForgelikeInstaller.BuildArguments(
            25, "i.jar", "t.jar", @"C:\Temp\mc\");

        Assert.StartsWith(
            "--add-exports cpw.mods.bootstraplauncher/cpw.mods.bootstraplauncher=ALL-UNNAMED ", arguments);
        AssertNoEscapedQuote(arguments);
    }

    // ---- OptiFine 启动参数（同类拼接，一并收编）----

    [Fact]
    public void OptiFineArguments_userHome带尾斜杠时不再混入字面引号()
    {
        var arguments = OptiFineInstaller.BuildArguments(21, @"C:\mc\optifine.jar", @"C:\Temp\mc\");

        AssertNoEscapedQuote(arguments);
        Assert.Contains(@"-Duser.home=""C:\Temp\mc""", arguments);
    }

    [Fact]
    public void OptiFineArguments_Wrapper双目录都带尾斜杠也安全()
    {
        var arguments = OptiFineInstaller.BuildArguments(21, @"C:\mc\optifine.jar", @"C:\Temp\mc\",
            @"C:\mc\JavaWrapper.jar", @"C:\Temp\PCL\", useJavaWrapper: true);

        AssertNoEscapedQuote(arguments);
        Assert.Contains(@"-Doolloo.jlw.tmpdir=""C:\Temp\PCL""", arguments);
        Assert.Contains(@"-Duser.home=""C:\Temp\mc""", arguments);
    }

    // ---- 原版 json 的 Java 主版本要求 ----

    [Fact]
    public void ReadRequiredJavaMajor_标准写法读得到26点3要求的25()
        => Assert.Equal(25,
            JavaRequirement.ReadRequiredJavaMajor("""{"javaVersion":{"component":"java-runtime-gamma","majorVersion":25}}"""));

    [Fact]
    public void ReadRequiredJavaMajor_兼容字符串版本号()
        => Assert.Equal(25,
            JavaRequirement.ReadRequiredJavaMajor("""{"javaVersion":{"majorVersion":"25"}}"""));

    [Fact]
    public void ReadRequiredJavaMajor_兼容旧字段java_version()
        => Assert.Equal(21, JavaRequirement.ReadRequiredJavaMajor("""{"java_version":21}"""));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("""{"javaVersion":{}}""")]
    [InlineData("""{"javaVersion":{"majorVersion":0}}""")]
    [InlineData("""{"javaVersion":{"majorVersion":-1}}""")]
    [InlineData("""{"javaVersion":{"majorVersion":"abc"}}""")]
    public void ReadRequiredJavaMajor_缺失或异常一律回退(string? json)
        => Assert.Null(JavaRequirement.ReadRequiredJavaMajor(json));
}
