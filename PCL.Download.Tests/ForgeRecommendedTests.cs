using System.Text.Json.Nodes;

namespace PCL.Download.Tests;

/// <summary>
/// ForgeRecommended（promos → 推荐版本表）测试。
/// <para>
/// 这份缓存决定 Forge 版本列表上「推荐」标记落在哪一个版本上。转错的后果不是报错，
/// 而是推荐标记消失或落到 latest 通道——用户以为自己在装推荐版本。
/// </para>
/// </summary>
public class ForgeRecommendedTests
{
    [Fact]
    public void BuildCacheJson_只取推荐通道并去掉后缀()
    {
        var promos = Promos(
            ("1.20.1-recommended", "47.2.0"),
            ("1.20.1-latest", "47.2.20"),
            ("1.19.2-recommended", "43.2.14"),
            ("1.18.2-recommended", "40.1.80"),
            ("1.16.5-recommended", "36.2.34"),
            ("1.12.2-recommended", "14.23.5.2860"));

        var table = JsonNode.Parse(ForgeRecommended.BuildCacheJson(promos))!.AsObject();

        Assert.Equal(5, table.Count);
        Assert.Equal("47.2.0", table["1.20.1"]!.GetValue<string>());
        // latest 通道不能当推荐版本
        Assert.NotEqual("47.2.20", table["1.20.1"]!.GetValue<string>());
        Assert.Equal("14.23.5.2860", table["1.12.2"]!.GetValue<string>());
        Assert.False(table.ContainsKey("1.20.1-latest"));
    }

    [Fact]
    public void BuildCacheJson_缺name或build的条目被跳过()
    {
        var array = new JsonArray
        {
            new JsonObject { ["name"] = "1.20.1-recommended", ["build"] = new JsonObject { ["version"] = "47.2.0" } },
            new JsonObject { ["name"] = "1.19.2-recommended" },                       // 缺 build
            new JsonObject { ["build"] = new JsonObject { ["version"] = "43.2.14" } }, // 缺 name
            new JsonObject { ["name"] = "1.18.2-recommended", ["build"] = new JsonObject { ["version"] = "40.1.80" } },
            new JsonObject { ["name"] = "1.16.5-recommended", ["build"] = new JsonObject { ["version"] = "36.2.34" } },
            new JsonObject { ["name"] = "1.12.2-recommended", ["build"] = new JsonObject { ["version"] = "14.23.5.2860" } },
            new JsonObject { ["name"] = "1.7.10-recommended", ["build"] = new JsonObject { ["version"] = "10.13.4.1614" } },
        };

        var table = JsonNode.Parse(ForgeRecommended.BuildCacheJson(Pad(array.ToJsonString())))!.AsObject();

        Assert.Equal(5, table.Count);
        Assert.False(table.ContainsKey("1.19.2"));
    }

    [Fact]
    public void BuildCacheJson_同一版本多次推荐时后者覆盖()
    {
        var promos = Promos(
            ("1.20.1-recommended", "47.1.0"),
            ("1.20.1-recommended", "47.2.0"),
            ("1.19.2-recommended", "43.2.14"),
            ("1.18.2-recommended", "40.1.80"),
            ("1.16.5-recommended", "36.2.34"),
            ("1.12.2-recommended", "14.23.5.2860"));

        var table = JsonNode.Parse(ForgeRecommended.BuildCacheJson(promos))!.AsObject();

        Assert.Equal("47.2.0", table["1.20.1"]!.GetValue<string>());
    }

    [Fact]
    public void BuildCacheJson_输出紧凑且保持输入顺序()
    {
        var promos = Promos(
            ("1.20.1-recommended", "47.2.0"),
            ("1.19.2-recommended", "43.2.14"),
            ("1.18.2-recommended", "40.1.80"),
            ("1.16.5-recommended", "36.2.34"),
            ("1.12.2-recommended", "14.23.5.2860"));

        var text = ForgeRecommended.BuildCacheJson(promos);

        Assert.DoesNotContain("\n", text);
        Assert.DoesNotContain("  ", text);
        Assert.Equal("{\"1.20.1\":\"47.2.0\",\"1.19.2\":\"43.2.14\",\"1.18.2\":\"40.1.80\","
                     + "\"1.16.5\":\"36.2.34\",\"1.12.2\":\"14.23.5.2860\"}", text);
    }

    [Fact]
    public void BuildCacheJson_响应过短时抛错()
    {
        // 镜像降级时会返回一个几十字节的错误页，此时应当失败而不是写出一份空缓存
        var ex = Assert.Throws<InvalidOperationException>(
            () => ForgeRecommended.BuildCacheJson("""[{"name":"1.20.1-recommended"}]"""));
        Assert.Contains("过短", ex.Message);
    }

    [Fact]
    public void BuildCacheJson_推荐条目不足时抛错()
    {
        var promos = Promos(
            ("1.20.1-recommended", "47.2.0"),
            ("1.19.2-recommended", "43.2.14"),
            ("1.20.1-latest", "47.2.20"),
            ("1.19.2-latest", "43.2.20"));

        var ex = Assert.Throws<InvalidOperationException>(() => ForgeRecommended.BuildCacheJson(promos));
        Assert.Contains("过少", ex.Message);
    }

    [Fact]
    public void BuildCacheJson_不是数组时抛错()
    {
        var ex = Assert.Throws<InvalidOperationException>(
            () => ForgeRecommended.BuildCacheJson(Pad("""{"promos":[]}""")));
        Assert.Contains("不是数组", ex.Message);
    }

    [Fact]
    public void BuildCacheJson_推荐条目缺build版本时抛错并带上通道名()
    {
        var array = new JsonArray
        {
            new JsonObject { ["name"] = "1.20.1-recommended", ["build"] = new JsonObject { ["version"] = "47.2.0" } },
            new JsonObject { ["name"] = "1.19.2-recommended", ["build"] = new JsonObject() },
            new JsonObject { ["name"] = "1.18.2-recommended", ["build"] = new JsonObject { ["version"] = "40.1.80" } },
            new JsonObject { ["name"] = "1.16.5-recommended", ["build"] = new JsonObject { ["version"] = "36.2.34" } },
            new JsonObject { ["name"] = "1.12.2-recommended", ["build"] = new JsonObject { ["version"] = "14.23.5.2860" } },
        };

        var ex = Assert.Throws<InvalidOperationException>(
            () => ForgeRecommended.BuildCacheJson(array.ToJsonString()));
        Assert.Contains("1.19.2-recommended", ex.Message);
    }

    // ---- 夹具 ----

    private static string Promos(params (string Name, string Version)[] entries)
    {
        var array = new JsonArray();
        foreach (var (name, version) in entries)
        {
            array.Add(new JsonObject
            {
                ["name"] = name,
                ["build"] = new JsonObject { ["version"] = version },
            });
        }
        return Pad(array.ToJsonString());
    }

    /// <summary>补足到长度下限之上：真实 promos 响应体有几 KB，短响应会被当作镜像降级。</summary>
    private static string Pad(string json) => json + new string(' ', Math.Max(0, 1200 - json.Length));
}
