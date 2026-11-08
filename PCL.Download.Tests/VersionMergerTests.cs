using System.Text.Json.Nodes;

namespace PCL.Download.Tests;

/// <summary>
/// VersionMerger（VB <c>ModDownloadLib.MergeJson</c> 的平移）对拍测试。
/// <para>
/// 重点守的是「Newtonsoft 默认合并语义在 STJ 上的复现」：数组拼接、null 忽略、对象递归。
/// 这三条任何一条走偏，合并出来的版本 json 依然是一份合法 JSON，但启动时会出现
/// 「找不到某个库」「主类不对」这类与合并代码毫无表象关联的故障。
/// </para>
/// </summary>
public class VersionMergerTests
{
    // ---- 合并语义 ----

    [Fact]
    public void Merge_数组拼接而非覆盖()
    {
        var merged = VersionMerger.Merge(
        [
            Profile("Minecraft", """{"id":"1.20.1","libraries":[{"name":"base:1.0"}]}"""),
            Profile("Forge", """{"libraries":[{"name":"forge:47.1.0"},{"name":"asm:9.5"}]}"""),
        ], "1.20.1-Forge");

        var libraries = merged["libraries"]!.AsArray();
        Assert.Equal(3, libraries.Count);
        Assert.Equal("base:1.0", libraries[0]!["name"]!.GetValue<string>());
        Assert.Equal("forge:47.1.0", libraries[1]!["name"]!.GetValue<string>());
        Assert.Equal("asm:9.5", libraries[2]!["name"]!.GetValue<string>());
    }

    [Fact]
    public void Merge_对象递归合并_同名子键逐层进入()
    {
        var merged = VersionMerger.Merge(
        [
            Profile("Minecraft", """{"arguments":{"game":["--username"],"jvm":["-Xmx2G"]}}"""),
            Profile("Fabric", """{"arguments":{"game":["--tweakClass","x"],"jvm":["-DFabricMcEmu=1"]}}"""),
        ], "v");

        var arguments = merged["arguments"]!.AsObject();
        // 两个分支各自拼接，不能因为加载器里也有 arguments 就把整个对象替换掉
        Assert.Equal(3, arguments["game"]!.AsArray().Count);
        Assert.Equal(2, arguments["jvm"]!.AsArray().Count);
        Assert.Equal("--tweakClass", arguments["game"]![1]!.GetValue<string>());
    }

    [Fact]
    public void Merge_标量由靠后的加载器覆盖()
    {
        var merged = VersionMerger.Merge(
        [
            Profile("Minecraft", """{"mainClass":"net.minecraft.client.main.Main"}"""),
            Profile("Forge", """{"mainClass":"cpw.mods.bootstraplauncher.BootstrapLauncher"}"""),
        ], "v");

        Assert.Equal("cpw.mods.bootstraplauncher.BootstrapLauncher", merged["mainClass"]!.GetValue<string>());
    }

    [Fact]
    public void Merge_来源为null时保留目标已有值()
    {
        // MergeNullValueHandling.Ignore：加载器 json 里常见的 "assets": null 不能把原版的值清掉
        var merged = VersionMerger.Merge(
        [
            Profile("Minecraft", """{"assets":"5","downloads":{"client":{"size":1}}}"""),
            Profile("Forge", """{"assets":null,"downloads":null}"""),
        ], "v");

        Assert.Equal("5", merged["assets"]!.GetValue<string>());
        Assert.NotNull(merged["downloads"]);
    }

    [Fact]
    public void Merge_剔掉加载器的发布时间_保留原版的()
    {
        var merged = VersionMerger.Merge(
        [
            Profile("Minecraft", """{"releaseTime":"2023-06-12T00:00:00+00:00","time":"2023-06-12T00:00:00+00:00"}"""),
            Profile("Forge", """{"releaseTime":"2024-01-01T00:00:00+00:00","time":"2024-01-02T00:00:00+00:00"}"""),
        ], "v");

        // 版本列表显示的是原版发布时间，不能被加载器的构建时间顶掉
        Assert.Equal("2023-06-12T00:00:00+00:00", merged["releaseTime"]!.GetValue<string>());
        Assert.Equal("2023-06-12T00:00:00+00:00", merged["time"]!.GetValue<string>());
    }

    [Fact]
    public void Merge_清理继承痕迹并写入新id()
    {
        var merged = VersionMerger.Merge(
        [
            Profile("Minecraft", """{"id":"1.20.1","jar":"1.20.1","_comment_":["x"]}"""),
            Profile("Forge", """{"id":"1.20.1-forge-47.1.0","inheritsFrom":"1.20.1","_comment_":["y"]}"""),
        ], "1.20.1-Forge");

        // inheritsFrom / jar 留着会让启动器去别的目录找原版，合并成独立版本后必须摘掉
        Assert.False(merged.ContainsKey("inheritsFrom"));
        Assert.False(merged.ContainsKey("jar"));
        Assert.False(merged.ContainsKey("_comment_"));
        Assert.Equal("1.20.1-Forge", merged["id"]!.GetValue<string>());
    }

    // ---- minecraftArguments ----

    [Fact]
    public void Merge_参数去重且值与flag绑在一起()
    {
        var merged = VersionMerger.Merge(
        [
            Profile("Minecraft", """{"minecraftArguments":"--username ${auth_player_name} --gameDir ${game_directory}"}"""),
            Profile("Forge", """{"minecraftArguments":"--tweakClass net.minecraftforge.fml.common.launcher.FMLTweaker --username ${auth_player_name}"}"""),
        ], "v");

        var arguments = merged["minecraftArguments"]!.GetValue<string>();
        // --username 的取值必须与 flag 连在一起，否则去重时值会被打散
        Assert.Contains("--username ${auth_player_name}", arguments);
        Assert.Contains("--tweakClass net.minecraftforge.fml.common.launcher.FMLTweaker", arguments);
        Assert.Equal(1, CountOccurrences(arguments, "--username"));
    }

    [Fact]
    public void Merge_每个flag的取值各自绑定_不被去重拆散()
    {
        // 两份 profile 都写了 --tweakClass，但取值不同。默认的 Distinct 会吃掉重复的 flag，
        // 若不先把取值吸附上去，第二个取值 b 就会变成一个孤立参数传给启动器（客户端直接崩）。
        var merged = VersionMerger.Merge(
        [
            Profile("Minecraft", """{"minecraftArguments":"--tweakClass a --demo"}"""),
            Profile("Forge", """{"minecraftArguments":"--tweakClass b"}"""),
        ], "v");

        Assert.Equal("--tweakClass a --demo --tweakClass b",
            merged["minecraftArguments"]!.GetValue<string>());
    }

    [Fact]
    public void Merge_多个flag只吸附第一个取值()
    {
        // VB 原实现只把紧跟 flag 的第一个词吸附上去，第二个词独立成项。
        // 这条看起来像 bug，但启动器按空白切分参数，结果等价，改动会引入差异。
        var merged = VersionMerger.Merge(
            [Profile("Minecraft", """{"minecraftArguments":"-Dfoo=a b --bar"}""")], "v");

        Assert.Equal("-Dfoo=a b --bar", merged["minecraftArguments"]!.GetValue<string>());
    }

    [Fact]
    public void Merge_无参数时不凭空造出字段()
    {
        var merged = VersionMerger.Merge([Profile("Minecraft", """{"id":"1.20.1"}""")], "v");
        Assert.False(merged.ContainsKey("minecraftArguments"));
    }

    // ---- 入参校验 ----

    [Fact]
    public void Merge_空profile列表直接报错()
    {
        Assert.Throws<ArgumentException>(() => VersionMerger.Merge([], "v"));
    }

    [Fact]
    public void Merge_非法json报出是哪个profile()
    {
        var ex = Assert.Throws<InvalidDataException>(() => VersionMerger.Merge(
            [Profile("Minecraft", """{"id":"1.20.1"}"""), Profile("OptiFine", "<html>404</html>")], "v"));
        Assert.Contains("OptiFine json 有误", ex.Message);
    }

    // ---- 落盘 ----

    [Fact]
    public void MergeToDirectory_读目录下同名json并写回合并结果()
    {
        var root = NewTempFolder();
        try
        {
            var baseFolder = Path.Combine(root, "versions", "1.20.1");
            var loaderFolder = Path.Combine(root, "versions", "1.20.1-forge-47.1.0");
            Directory.CreateDirectory(baseFolder);
            Directory.CreateDirectory(loaderFolder);
            File.WriteAllText(Path.Combine(baseFolder, "1.20.1.json"),
                """{"id":"1.20.1","libraries":[{"name":"base:1.0"}]}""");
            File.WriteAllText(Path.Combine(loaderFolder, "1.20.1-forge-47.1.0.json"),
                """{"id":"1.20.1-forge-47.1.0","inheritsFrom":"1.20.1","libraries":[{"name":"forge:47.1.0"}]}""");

            VersionMerger.MergeToDirectory(
                [new VersionMergeSource("Minecraft", baseFolder), new VersionMergeSource("Forge", loaderFolder)],
                baseFolder);

            var written = JsonNode.Parse(File.ReadAllText(Path.Combine(baseFolder, "1.20.1.json")))!.AsObject();
            Assert.Equal("1.20.1", written["id"]!.GetValue<string>());
            Assert.False(written.ContainsKey("inheritsFrom"));
            Assert.Equal(2, written["libraries"]!.AsArray().Count);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void MergeToDirectory_加载器json缺失时报错并带上路径()
    {
        var root = NewTempFolder();
        try
        {
            var baseFolder = Path.Combine(root, "versions", "1.20.1");
            var loaderFolder = Path.Combine(root, "versions", "1.20.1-optifine");
            Directory.CreateDirectory(baseFolder);
            Directory.CreateDirectory(loaderFolder);
            File.WriteAllText(Path.Combine(baseFolder, "1.20.1.json"), """{"id":"1.20.1"}""");

            var ex = Assert.Throws<InvalidDataException>(() => VersionMerger.MergeToDirectory(
                [new VersionMergeSource("Minecraft", baseFolder), new VersionMergeSource("OptiFine", loaderFolder)],
                baseFolder));
            Assert.Contains("OptiFine json 有误", ex.Message);
            Assert.Contains("1.20.1-optifine.json", ex.Message);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    // ---- 夹具 ----

    private static VersionProfile Profile(string name, string json) => new(name, json);

    private static string NewTempFolder()
    {
        var path = Path.Combine(Path.GetTempPath(), "pcl-merge-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static int CountOccurrences(string text, string value)
    {
        var count = 0;
        for (var index = text.IndexOf(value, StringComparison.Ordinal); index >= 0;
             index = text.IndexOf(value, index + value.Length, StringComparison.Ordinal))
            count++;
        return count;
    }
}
