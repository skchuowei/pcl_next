using System.IO.Compression;

namespace PCL.Download.Tests;

/// <summary>
/// OptiFineInstaller（OptiFine 安装的纯逻辑）测试。
/// <para>
/// 这一批逻辑的共性是「错了不报错」：Java 版本读低了，安装器一闪而过；参数拼错一个引号，
/// 路径带空格时才失败；成败判定写反，成功被当失败。故逐条按住。
/// </para>
/// </summary>
public class OptiFineInstallerTests
{
    // ---- Java 版本探测 ----

    [Theory]
    [InlineData(49, 5)]   // Java 5：允许的下限
    [InlineData(52, 8)]   // Java 8
    [InlineData(55, 11)]
    [InlineData(61, 17)]
    [InlineData(65, 21)]  // 默认值对应的 class 版本
    [InlineData(100, 56)] // 允许的上限
    public void Probe_按class版本推算Java主版本(int classVersion, int expectJava)
    {
        using var jar = TempJar.Create(classVersion: classVersion);
        var probe = OptiFineInstaller.ProbeRequiredJava(jar.Path);

        Assert.Null(probe.Failure);
        Assert.Equal(classVersion, probe.ClassVersion);
        Assert.Equal(expectJava, probe.JavaMajor);
    }

    [Fact]
    public void Probe_找不到InstallerClass时退回默认版本并给出原因()
    {
        using var jar = TempJar.Create(classVersion: 61, entryName: "optifine/Other.class");
        var probe = OptiFineInstaller.ProbeRequiredJava(jar.Path);

        Assert.Equal(OptiFineInstaller.DefaultJavaMajor, probe.JavaMajor);
        Assert.Null(probe.ClassVersion);
        Assert.Contains("未找到", probe.Failure);
        // 原因必须指明是哪个条目，否则用户只看到「读取 Java 版本失败」
        Assert.Contains("optifine/Installer.class", probe.Failure);
    }

    [Fact]
    public void Probe_文件头不是CAFEBABE时退回默认版本()
    {
        using var jar = TempJar.Create(classVersion: 61, corruptMagic: true);
        var probe = OptiFineInstaller.ProbeRequiredJava(jar.Path);

        Assert.Equal(OptiFineInstaller.DefaultJavaMajor, probe.JavaMajor);
        Assert.Contains("文件头无效", probe.Failure);
    }

    [Fact]
    public void Probe_class文件短于8字节时退回默认版本()
    {
        using var jar = TempJar.Create(classVersion: 61, truncateTo: 5);
        var probe = OptiFineInstaller.ProbeRequiredJava(jar.Path);

        Assert.Equal(OptiFineInstaller.DefaultJavaMajor, probe.JavaMajor);
        Assert.Contains("长度不足", probe.Failure);
    }

    [Theory]
    [InlineData(48)]  // 低于 Java 5 下限
    [InlineData(101)] // 高于上限
    public void Probe_class版本超范围时退回默认版本并带上读到的值(int classVersion)
    {
        using var jar = TempJar.Create(classVersion: classVersion);
        var probe = OptiFineInstaller.ProbeRequiredJava(jar.Path);

        Assert.Equal(OptiFineInstaller.DefaultJavaMajor, probe.JavaMajor);
        // 超范围时仍要把读到的值带回来，否则日志里看不出到底读到了什么
        Assert.Equal(classVersion, probe.ClassVersion);
        Assert.NotNull(probe.Failure);
    }

    [Fact]
    public void Probe_文件不是zip时退回默认版本而不抛异常()
    {
        var path = Path.Combine(Path.GetTempPath(), "pcl-optifine-" + Guid.NewGuid().ToString("N") + ".jar");
        File.WriteAllText(path, "这不是一个 zip 文件");
        try
        {
            var probe = OptiFineInstaller.ProbeRequiredJava(path);
            Assert.Equal(OptiFineInstaller.DefaultJavaMajor, probe.JavaMajor);
            Assert.Contains("读取", probe.Failure);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Probe_文件不存在时退回默认版本而不抛异常()
    {
        var probe = OptiFineInstaller.ProbeRequiredJava(
            Path.Combine(Path.GetTempPath(), "pcl-missing-" + Guid.NewGuid().ToString("N") + ".jar"));

        Assert.Equal(OptiFineInstaller.DefaultJavaMajor, probe.JavaMajor);
        Assert.NotNull(probe.Failure);
    }

    // ---- 启动参数 ----

    [Fact]
    public void BuildArguments_普通模式不带JavaWrapper也不带模块导出()
    {
        var arguments = OptiFineInstaller.BuildArguments(8, @"C:\mc\optifine.jar", @"C:\mc");

        Assert.Equal(@"-Duser.home=""C:\mc"" -cp ""C:\mc\optifine.jar"" optifine.Installer", arguments);
        Assert.DoesNotContain("--add-exports", arguments);
        Assert.DoesNotContain("oolloo", arguments);
    }

    [Fact]
    public void BuildArguments_Java9起补模块导出()
    {
        var arguments = OptiFineInstaller.BuildArguments(17, @"C:\mc\optifine.jar", @"C:\mc");

        Assert.StartsWith("--add-exports cpw.mods.bootstraplauncher/cpw.mods.bootstraplauncher=ALL-UNNAMED ", arguments);
        Assert.EndsWith(@"-cp ""C:\mc\optifine.jar"" optifine.Installer", arguments);
    }

    [Fact]
    public void BuildArguments_JavaWrapper模式多出临时目录与jar入口()
    {
        var arguments = OptiFineInstaller.BuildArguments(21, @"C:\mc\optifine.jar", @"C:\mc",
            @"C:\mc\JavaWrapper.jar", @"C:\temp", useJavaWrapper: true);

        Assert.Contains(@"-Doolloo.jlw.tmpdir=""C:\temp""", arguments);
        Assert.Contains(@"-Duser.home=""C:\mc""", arguments);
        Assert.Contains(@"-jar ""C:\mc\JavaWrapper.jar"" optifine.Installer", arguments);
    }

    [Fact]
    public void BuildArguments_要用Wrapper但拿不到路径时退回普通模式()
    {
        // 解压 JavaWrapper 失败时不能因此整个安装作废
        var arguments = OptiFineInstaller.BuildArguments(21, @"C:\mc\optifine.jar", @"C:\mc",
            javaWrapperPath: null, useJavaWrapper: true);

        Assert.DoesNotContain("-jar", arguments);
        Assert.Contains(@"-cp ""C:\mc\optifine.jar"" optifine.Installer", arguments);
    }

    [Fact]
    public void BuildArguments_classpath里只有安装器自己没有目标jar()
    {
        // Forge 的 classpath 是「安装器;目标jar」，照抄过来会让 OptiFine 装完缺库
        var arguments = OptiFineInstaller.BuildArguments(17, @"C:\mc\optifine.jar", @"C:\mc");

        Assert.DoesNotContain(";", arguments);
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(arguments, "optifine\\.Installer"));
    }

    // ---- 成败判定 ----

    [Theory]
    [InlineData(0, false)]
    [InlineData(999, false)]
    [InlineData(1000, true)]
    [InlineData(5000, true)]
    public void IsFailed_输出行数不足即判失败(int lines, bool expectSucceeded)
    {
        Assert.Equal(!expectSucceeded, OptiFineInstaller.IsFailed(lines, "正常输出"));
    }

    [Theory]
    [InlineData("at java.base/java.lang.Thread.run(Thread.java:1583)")]
    [InlineData("    at optifine.Installer.main(Installer.java:112)")]
    [InlineData("Caused by: java.io.IOException: at something")]
    public void IsFailed_末行是栈帧即判失败(string lastLine)
    {
        Assert.True(OptiFineInstaller.IsFailed(5000, lastLine));
    }

    [Fact]
    public void IsFailed_末行为空且行数足够时视为成功()
    {
        Assert.False(OptiFineInstaller.IsFailed(5000, null));
        Assert.False(OptiFineInstaller.IsFailed(5000, ""));
    }

    [Fact]
    public void ProgressPerLine_七千行摊满0点7()
    {
        Assert.Equal(0.7, OptiFineInstaller.ProgressPerLine * 7000, 10);
    }

    // ---- 夹具 ----

    /// <summary>构造一个只含 <c>optifine/Installer.class</c> 的最小 jar。</summary>
    private sealed class TempJar : IDisposable
    {
        public string Path { get; }

        private TempJar(string path) => Path = path;

        public static TempJar Create(int classVersion, string entryName = "optifine/Installer.class",
            bool corruptMagic = false, int? truncateTo = null)
        {
            var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
                "pcl-optifine-" + Guid.NewGuid().ToString("N") + ".jar");

            var payload = new byte[truncateTo ?? 32];
            if (payload.Length >= 4)
            {
                if (!corruptMagic)
                {
                    payload[0] = 0xCA;
                    payload[1] = 0xFE;
                    payload[2] = 0xBA;
                    payload[3] = 0xBE;
                }
                else
                {
                    payload[0] = 0x50;
                    payload[1] = 0x4B; // 「PK」：把 jar 当成普通 zip 读时最容易出现的误判
                }
            }

            if (payload.Length >= 8)
            {
                payload[6] = (byte)(classVersion >> 8);
                payload[7] = (byte)(classVersion & 0xFF);
            }

            using (var zip = ZipFile.Open(path, ZipArchiveMode.Create))
            {
                var entry = zip.CreateEntry(entryName);
                using var stream = entry.Open();
                stream.Write(payload);
            }

            return new TempJar(path);
        }

        public void Dispose()
        {
            try
            {
                File.Delete(Path);
            }
            catch (IOException)
            {
                // 临时文件删不掉不影响测试结论
            }
        }
    }
}
