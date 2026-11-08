using System.Security.Cryptography;
using System.Text.Json.Nodes;

namespace PCL.Download.Tests;

[Collection("Resource")]
public class LocalResourceFileTests
{
    private static string WriteTempFile(string name, byte[] content)
    {
        var dir = Path.Combine(Path.GetTempPath(), "pcldl_lrf_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, name);
        File.WriteAllBytes(path, content);
        return path;
    }

    [Fact]
    public void ModrinthHash_为小写Sha1且与独立计算一致()
    {
        var content = new byte[] { 1, 2, 3, 4, 5, 0x41, 0x42 };
        var path = WriteTempFile("m.jar", content);
        var file = new LocalResourceFile(path);

        var expected = Convert.ToHexString(SHA1.HashData(content)).ToLowerInvariant();
        Assert.Equal(expected, file.ModrinthHash);
        Assert.Equal(40, file.ModrinthHash.Length);
        Assert.Equal(file.ModrinthHash, file.ModrinthHash); // 缓存一致
    }

    [Fact]
    public void CurseForgeHash_去空白后与含空白的等价()
    {
        // "ab" 与 "a b"（中间一个空格字节）去空白后都是 "ab"，指纹应相同；
        // 与 "axb"（真正不同的字节）应不同。这独立验证了「仅剥离 0x09/0x0A/0x0D/0x20」。
        var pathPlain = WriteTempFile("a.jar", new byte[] { (byte)'a', (byte)'b' });
        var pathSpaced = WriteTempFile("b.jar", new byte[] { (byte)'a', 0x20, (byte)'b' });
        var pathDiff = WriteTempFile("c.jar", new byte[] { (byte)'a', (byte)'x', (byte)'b' });

        var hPlain = new LocalResourceFile(pathPlain).CurseForgeHash;
        var hSpaced = new LocalResourceFile(pathSpaced).CurseForgeHash;
        var hDiff = new LocalResourceFile(pathDiff).CurseForgeHash;

        Assert.Equal(hPlain, hSpaced);
        Assert.NotEqual(hPlain, hDiff);
    }

    [Fact]
    public void EnabledName_去掉Disabled与Old后缀()
    {
        Assert.Equal("x.jar", new LocalResourceFile("x.jar").EnabledName);
        Assert.Equal("x.jar", new LocalResourceFile("x.jar.disabled").EnabledName);
        Assert.Equal("x.zip", new LocalResourceFile("x.zip.old").EnabledName);
        Assert.True(new LocalResourceFile("x.jar").IsEnabled);
        Assert.False(new LocalResourceFile("x.jar.disabled").IsEnabled);
        Assert.EndsWith(".jar", new LocalResourceFile("x.jar").EnabledFullName);
    }

    [Fact]
    public void Display_过滤纯数字与保留字()
    {
        var f = new LocalResourceFile("some.jar");
        f.Display = "modname";   // 含保留字，忽略
        f.Display = "name";      // 小写等于 name，忽略
        f.Display = "a";         // 长度 1，忽略
        f.Display = "12.5";      // 纯数字串，忽略
        Assert.Equal("some", f.Display); // 回退到去扩展名文件名

        var f2 = new LocalResourceFile("other.jar");
        f2.Display = "Cool Mod";
        Assert.Equal("Cool Mod", f2.Display);
    }

    [Fact]
    public void Version_已设为版本格式后不可覆盖且version字面量处理()
    {
        var f = new LocalResourceFile("v.jar");
        f.Version = "1.0";
        Assert.Equal("1.0", f.Version);
        f.Version = "2.0"; // 已为版本格式，不应覆盖
        Assert.Equal("1.0", f.Version);

        var f2 = new LocalResourceFile("w.jar");
        f2.Version = "myversion"; // 含 version，应被改写为字面量
        Assert.Equal("version", f2.Version);
    }

    [Fact]
    public void ToJson_FromJson_往返保留ChangelogUrls与OnlineDataLoaded()
    {
        var f = new LocalResourceFile("z.jar")
        {
            ChangelogUrls = new List<string> { "https://a", "https://b" },
            OnlineDataLoaded = true,
        };
        var json = f.ToJson();
        var back = new LocalResourceFile("z.jar");
        back.FromJson(json);
        Assert.Equal(new[] { "https://a", "https://b" }, back.ChangelogUrls);
        Assert.True(back.OnlineDataLoaded);
    }

    [Fact]
    public void ToJson_FromJson_往返保留Project与ProjectVersion()
    {
        var project = MakeProject("Just Enough Items", "238222", "jei");
        var version = MakeVersion("1.0", "jei-1.0.jar");

        var f = new LocalResourceFile("z.jar") { Project = project, ProjectVersion = version };
        var json = f.ToJson();
        Assert.NotNull(json["Project"]);

        var back = new LocalResourceFile("z.jar");
        back.FromJson(json);
        Assert.NotNull(back.Project);
        Assert.Equal("Just Enough Items", back.Project!.RawName);
        Assert.NotNull(back.ProjectVersion);
        Assert.Equal("1.0", back.ProjectVersion!.Version);
        Assert.Equal("jei-1.0.jar", back.ProjectVersion.FileName);
    }

    [Fact]
    public void Equals_按路径忽略大小写()
    {
        var a = new LocalResourceFile(@"C:\mods\x.jar");
        var b = new LocalResourceFile(@"c:\mods\x.jar");
        var c = new LocalResourceFile(@"c:\mods\y.jar");
        Assert.True(a.Equals(b));
        Assert.False(a.Equals(c));
    }

    private static ResourceProject MakeProject(string rawName, string id, string slug)
    {
        var j = new JsonObject
        {
            ["Platform"] = (int)ResourcePlatforms.CurseForge,
            ["Types"] = (int)ResourceTypes.Mod,
            ["Slug"] = slug,
            ["Id"] = id,
            ["RawName"] = rawName,
            ["Description"] = "",
            ["Website"] = "",
            ["LastUpdate"] = JsonValue.Create(DateTime.Now),
            ["DownloadCount"] = 0,
            ["Tags"] = new JsonArray(),
        };
        return new ResourceProject(j);
    }

    private static ResourceVersion MakeVersion(string version, string fileName)
    {
        var j = new JsonObject
        {
            ["ResourceType"] = (int)ResourceTypes.Mod,
            ["Id"] = "v1",
            ["DisplayName"] = "Disp",
            ["ReleaseDate"] = JsonValue.Create(DateTime.Now),
            ["DownloadCount"] = 0,
            ["ReleaseType"] = 1,
            ["Version"] = version,
            ["FileName"] = fileName,
        };
        return ResourceVersion.FromCacheJson(j);
    }
}
