using System.Text.Json.Nodes;

namespace PCL.Download.Tests;

/// <summary>
/// LiteLoaderProfile（LiteLoader 版本 json 构造）测试。
/// <para>
/// 这份 json 是「拼出来的」，字段缺一个都不会在安装时报错，只在启动时表现为
/// 「找不到 tweaker」或「版本列表里这个实例消失」。
/// </para>
/// </summary>
public class LiteLoaderProfileTests
{
    private const string EntryJson = """
        {
          "tweakClass": "com.mumfrey.liteloader.launch.LiteLoaderTweaker",
          "version": "1.12.2-SNAPSHOT-r1",
          "libraries": [
            {"name": "net.minecraft:launchwrapper:1.12"},
            {"name": "org.ow2.asm:asm-all:5.2"}
          ],
          "stream": "release",
          "timestamp": 1552000000
        }
        """;

    [Fact]
    public void BuildVersionJson_基本字段与实例名()
    {
        var text = LiteLoaderProfile.BuildVersionJson(EntryJson, "1.12.2", "2019/03/08 06:00");
        var json = JsonNode.Parse(text)!.AsObject();

        Assert.Equal("1.12.2-LiteLoader", json["id"]!.GetValue<string>());
        Assert.Equal("1.12.2", json["inheritsFrom"]!.GetValue<string>());
        Assert.Equal("1.12.2", json["jar"]!.GetValue<string>());
        Assert.Equal("release", json["type"]!.GetValue<string>());
        Assert.Equal("net.minecraft.launchwrapper.Launch", json["mainClass"]!.GetValue<string>());
        Assert.Equal(18, json["minimumLauncherVersion"]!.GetValue<int>());
    }

    [Fact]
    public void BuildVersionJson_参数里带tweakClass()
    {
        var text = LiteLoaderProfile.BuildVersionJson(EntryJson, "1.12.2", "2019/03/08 06:00");
        var game = JsonNode.Parse(text)!["arguments"]!["game"]!.AsArray();

        Assert.Equal(2, game.Count);
        Assert.Equal("--tweakClass", game[0]!.GetValue<string>());
        Assert.Equal("com.mumfrey.liteloader.launch.LiteLoaderTweaker", game[1]!.GetValue<string>());
    }

    [Fact]
    public void BuildVersionJson_在条目自带的库之后追加liteloader()
    {
        var text = LiteLoaderProfile.BuildVersionJson(EntryJson, "1.12.2", "2019/03/08 06:00");
        var libraries = JsonNode.Parse(text)!["libraries"]!.AsArray();

        // 原有的两条必须保留且顺序不变，否则 launchwrapper 可能被后面的同名库覆盖
        Assert.Equal(3, libraries.Count);
        Assert.Equal("net.minecraft:launchwrapper:1.12", libraries[0]!["name"]!.GetValue<string>());
        Assert.Equal("org.ow2.asm:asm-all:5.2", libraries[1]!["name"]!.GetValue<string>());
        Assert.Equal("com.mumfrey:liteloader:1.12.2-SNAPSHOT-r1", libraries[2]!["name"]!.GetValue<string>());
        // 少了 url 启动器无法解析 com.mumfrey 的坐标
        Assert.Equal(LiteLoaderProfile.MavenUrl, libraries[2]!["url"]!.GetValue<string>());
    }

    [Fact]
    public void BuildVersionJson_不改动调用方传入的条目()
    {
        var original = JsonNode.Parse(EntryJson)!;
        LiteLoaderProfile.BuildVersionJson(EntryJson, "1.12.2", "2019/03/08 06:00");

        // 深拷贝后追加，不能把 liteloader 那条写回条目本身（列表会被复用）
        Assert.Equal(2, original["libraries"]!.AsArray().Count);
    }

    [Fact]
    public void BuildVersionJson_发布时间按分钟精度转换()
    {
        var text = LiteLoaderProfile.BuildVersionJson(EntryJson, "1.12.2", "2019/03/08 06:30");
        var json = JsonNode.Parse(text)!.AsObject();

        Assert.Equal("2019-03-08T06:30:00", json["time"]!.GetValue<string>());
        Assert.Equal("2019-03-08T06:30:00", json["releaseTime"]!.GetValue<string>());
    }

    [Theory]
    [InlineData("2019-03-08 06:30")]  // 分隔符不对
    [InlineData("2019/03/08")]        // 缺时间
    [InlineData("")]
    public void BuildVersionJson_发布时间格式不符时抛错并带上原值(string releaseTime)
    {
        var ex = Assert.Throws<InvalidOperationException>(
            () => LiteLoaderProfile.BuildVersionJson(EntryJson, "1.12.2", releaseTime));
        Assert.Contains("发布时间", ex.Message);
    }

    [Fact]
    public void BuildVersionJson_缺libraries时抛错()
    {
        const string entry = """{"tweakClass":"a","version":"b"}""";
        var ex = Assert.Throws<InvalidOperationException>(
            () => LiteLoaderProfile.BuildVersionJson(entry, "1.12.2", "2019/03/08 06:30"));
        Assert.Contains("libraries", ex.Message);
    }

    [Fact]
    public void BuildVersionJson_缺tweakClass时抛错()
    {
        const string entry = """{"version":"b","libraries":[]}""";
        var ex = Assert.Throws<InvalidOperationException>(
            () => LiteLoaderProfile.BuildVersionJson(entry, "1.12.2", "2019/03/08 06:30"));
        Assert.Contains("tweakClass", ex.Message);
    }

    [Fact]
    public void BuildVersionJson_空库数组也能只追加liteloader()
    {
        const string entry = """{"tweakClass":"a","version":"b","libraries":[]}""";
        var libraries = JsonNode.Parse(LiteLoaderProfile.BuildVersionJson(entry, "1.7.10", "2019/03/08 06:30"))!
            ["libraries"]!.AsArray();

        Assert.Single(libraries);
        Assert.Equal("com.mumfrey:liteloader:b", libraries[0]!["name"]!.GetValue<string>());
    }
}
