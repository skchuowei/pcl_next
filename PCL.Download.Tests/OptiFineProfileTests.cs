using System.Text.Json.Nodes;

namespace PCL.Download.Tests;

/// <summary>
/// OptiFineProfile（下载源、安装器落盘位置、旧式版本 json）测试。
/// <para>
/// 这批逻辑里有三处「历史形状」：镜像侧 1.8/1.9 目录带 .0、预览版的路径把空格换成斜杠、
/// 新版 json 的 minimumLauncherVersion 是字符串。任何一处走偏都不抛异常，只表现为
/// 「下载得到 404」或「启动器不认这个版本」。
/// </para>
/// </summary>
public class OptiFineProfileTests
{
    // ---- 镜像源地址 ----

    [Fact]
    public void BuildMirrorUrl_正式版走HD_U目录()
    {
        var url = OptiFineProfile.BuildMirrorUrl("1.20.1", false, "1.20.1 HD U I6");

        Assert.Equal("https://bmclapi2.bangbang93.com/optifine/1.20.1/HD_U/HD U I6", url);
    }

    [Theory]
    [InlineData("1.8", "https://bmclapi2.bangbang93.com/optifine/1.8.0/HD_U/HD U M5")]
    [InlineData("1.9", "https://bmclapi2.bangbang93.com/optifine/1.9.0/HD_U/HD U C1")]
    public void BuildMirrorUrl_1点8与1点9的目录带点0(string inherit, string expect)
    {
        // #4281：镜像侧这两个版本的目录名是 1.8.0 / 1.9.0，不带 .0 会 404
        var displayName = inherit + " HD U " + (inherit == "1.8" ? "M5" : "C1");
        Assert.Equal(expect, OptiFineProfile.BuildMirrorUrl(inherit, false, displayName));
    }

    [Fact]
    public void BuildMirrorUrl_预览版把空格换成斜杠()
    {
        var url = OptiFineProfile.BuildMirrorUrl("1.8.9", true, "1.8.9 HD U M5 preview");

        Assert.Equal("https://bmclapi2.bangbang93.com/optifine/1.8.9/HD_U_HD/U/M5/preview", url);
    }

    [Fact]
    public void BuildMirrorUrl_只去掉前缀里的版本号不误伤其余内容()
    {
        // 「1.8 」既是前缀也可能是版本号片段，Replace 只应命中第一次出现的位置
        var url = OptiFineProfile.BuildMirrorUrl("1.8", false, "1.8 HD U H8");

        Assert.Equal("https://bmclapi2.bangbang93.com/optifine/1.8.0/HD_U/HD U H8", url);
    }

    // ---- 官方下载地址 ----

    [Fact]
    public void BuildOfficialPageUrl_拼文件名()
    {
        Assert.Equal("https://optifine.net/adloadx?f=OptiFine_1.20.1_HD_U_I6.jar",
            OptiFineProfile.BuildOfficialPageUrl("OptiFine_1.20.1_HD_U_I6.jar"));
    }

    [Fact]
    public void FindOfficialDownloadUrl_从页面里抠出下载链接()
    {
        const string page = """
            <html><body>
            <a href="downloadx?f=OptiFine_1.20.1_HD_U_I6.jar&x=abc">Download</a>
            </body></html>
            """;

        var url = OptiFineProfile.FindOfficialDownloadUrl(page);

        Assert.Equal("https://optifine.net/downloadx?f=OptiFine_1.20.1_HD_U_I6.jar&x=abc", url);
    }

    [Fact]
    public void FindOfficialDownloadUrl_页面里没有链接时返回null()
    {
        // 返回 null 而不是照样拼地址：否则会下回一个几百字节的 HTML，只在体积检查处失败
        Assert.Null(OptiFineProfile.FindOfficialDownloadUrl("<html><body>Mirror is down.</body></html>"));
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void FindOfficialDownloadUrl_空页面返回null(string? page)
    {
        Assert.Null(OptiFineProfile.FindOfficialDownloadUrl(page));
    }

    // ---- 安装器落盘位置 ----
    // 断言一律用 Path.Combine 构造期望值：这批测试在 Linux（build-linux job）同样要跑，
    // 写死反斜杠会把「实现硬拼 Windows 分隔符」固化成期望值。

    [Fact]
    public void BuildInstallerTarget_新版落临时目录()
    {
        var target = OptiFineProfile.BuildInstallerTarget(true, "temp-task1" + Path.DirectorySeparatorChar,
            "mc-root" + Path.DirectorySeparatorChar, "OptiFine_1.20.1_HD_U_I6.jar");

        Assert.Equal(Path.Combine("temp-task1", "OptiFine.jar"), target);
    }

    [Fact]
    public void BuildInstallerTarget_旧版落libraies供启动时当库加载()
    {
        var target = OptiFineProfile.BuildInstallerTarget(false, "temp-task1" + Path.DirectorySeparatorChar,
            "mc-root" + Path.DirectorySeparatorChar, "OptiFine_1.12.2_HD_U_G5.jar");

        // 目录名去掉 OptiFine_ 前缀与扩展名，文件名把 OptiFine_ 换成 OptiFine-
        Assert.Equal(
            Path.Combine("mc-root", "libraries", "optifine", "OptiFine", "1.12.2_HD_U_G5",
                "OptiFine-1.12.2_HD_U_G5.jar"),
            target);
    }

    [Fact]
    public void BuildInstallerTarget_预览版目录与文件名都去掉preview下划线()
    {
        var target = OptiFineProfile.BuildInstallerTarget(false, "",
            "mc-root" + Path.DirectorySeparatorChar, "OptiFine_preview_1.9.4_HD_U_G2_pre1.jar");

        Assert.Equal(
            Path.Combine("mc-root", "libraries", "optifine", "OptiFine", "1.9.4_HD_U_G2_pre1",
                "OptiFine-1.9.4_HD_U_G2_pre1.jar"),
            target);
    }

    // ---- 旧式版本 json ----

    [Fact]
    public void BuildLegacyProfileJson_新版格式用arguments且minimumLauncherVersion是字符串()
    {
        var text = OptiFineProfile.BuildLegacyProfileJson(new OptiFineLegacyProfile(
            "1.20.1-OptiFine_HD_U_I6", "1.20.1", "OptiFine_1.20.1_HD_U_I6.jar",
            "2023/06/12 12:00", "2023-06-12", IsOldJson: false, MinecraftArguments: null));

        var json = JsonNode.Parse(text)!.AsObject();
        Assert.Equal("1.20.1-OptiFine_HD_U_I6", json["id"]!.GetValue<string>());
        Assert.Equal("1.20.1", json["inheritsFrom"]!.GetValue<string>());
        Assert.Equal("release", json["type"]!.GetValue<string>());
        // 字符串而非数字：同步成数字会让新版被启动器判为不兼容
        Assert.Equal("21", json["minimumLauncherVersion"]!.GetValue<string>());
        Assert.Null(json["minecraftArguments"]);

        var game = json["arguments"]!["game"]!.AsArray();
        Assert.Equal(2, game.Count);
        Assert.Equal("--tweakClass", game[0]!.GetValue<string>());
        Assert.Equal("optifine.OptiFineTweaker", game[1]!.GetValue<string>());
    }

    [Fact]
    public void BuildLegacyProfileJson_旧版格式保留原参数并追加tweakClass()
    {
        var text = OptiFineProfile.BuildLegacyProfileJson(new OptiFineLegacyProfile(
            "1.12.2-OptiFine_HD_U_G5", "1.12.2", "OptiFine_1.12.2_HD_U_G5.jar",
            "2019/01/01 00:00", "2017-09-18", IsOldJson: true,
            MinecraftArguments: "--username ${auth_player_name}"));

        var json = JsonNode.Parse(text)!.AsObject();
        Assert.Equal(18, json["minimumLauncherVersion"]!.GetValue<int>());
        // 两个空格是原实现的写法，启动器按参数串切分，单空格也能跑，但不要顺手「修正」
        Assert.Equal("--username ${auth_player_name}  --tweakClass optifine.OptiFineTweaker",
            json["minecraftArguments"]!.GetValue<string>());
    }

    [Fact]
    public void BuildLegacyProfileJson_两份库声明顺序固定()
    {
        var text = OptiFineProfile.BuildLegacyProfileJson(new OptiFineLegacyProfile(
            "x", "1.12.2", "OptiFine_1.12.2_HD_U_G5.jar", "2019/01/01 00:00", "2017-09-18", true, ""));

        var libraries = JsonNode.Parse(text)!["libraries"]!.AsArray();
        Assert.Equal(2, libraries.Count);
        Assert.Equal("optifine:OptiFine:1.12.2_HD_U_G5", libraries[0]!["name"]!.GetValue<string>());
        Assert.Equal("net.minecraft:launchwrapper:1.12", libraries[1]!["name"]!.GetValue<string>());
    }

    [Fact]
    public void BuildLegacyProfileJson_时间戳时分秒写死且斜杠换横杠()
    {
        // OptiFine 列表里的发布时间只有日期（yyyy/MM/dd，见 LoaderListParsers），时分秒是写死的
        var text = OptiFineProfile.BuildLegacyProfileJson(new OptiFineLegacyProfile(
            "x", "1.20.1", "OptiFine_1.20.1_HD_U_I6.jar", "2023/06/12", "2023-06-12", false, null));

        var json = JsonNode.Parse(text)!.AsObject();
        Assert.Equal("2023-06-12T23:33:33+08:00", json["time"]!.GetValue<string>());
        Assert.Equal("2023-06-12T23:33:33+08:00", json["releaseTime"]!.GetValue<string>());
    }

    [Fact]
    public void BuildLegacyProfileJson_发布时间缺失时退回原版实例的时间()
    {
        var text = OptiFineProfile.BuildLegacyProfileJson(new OptiFineLegacyProfile(
            "x", "1.20.1", "OptiFine_1.20.1_HD_U_I6.jar", "", "2019-07-05", false, null));

        var json = JsonNode.Parse(text)!.AsObject();
        Assert.Equal("2019-07-05T23:33:33+08:00", json["releaseTime"]!.GetValue<string>());
    }

    // ---- 库版本号 ----

    [Theory]
    [InlineData("OptiFine_1.20.1_HD_U_I6.jar", "1.20.1_HD_U_I6")]
    [InlineData("OptiFine_preview_1.9.4_HD_U_G2_pre1.jar", "1.9.4_HD_U_G2_pre1")]
    [InlineData("OptiFine_1.7.10_HD_U_E7.jar", "1.7.10_HD_U_E7")]
    public void OptiFineVersionToken_去掉前缀与扩展名(string fileName, string expect)
    {
        Assert.Equal(expect, OptiFineProfile.OptiFineVersionToken(fileName));
    }
}
