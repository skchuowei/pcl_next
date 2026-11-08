using System.IO;

namespace PCL.Download.Tests;

[Collection("Resource")]
public class LocalResourceScannerTests
{
    private static string MakeTree(Action<string> build)
    {
        var root = Path.Combine(Path.GetTempPath(), "pcldl_scan_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        build(root);
        return root;
    }

    private static void Write(string path, string content = "x") => File.WriteAllText(path, content);

    [Fact]
    public void Scan_仅收录顶层Mod文件排除非Mod与子目录()
    {
        var root = MakeTree(r =>
        {
            Write(Path.Combine(r, "a.jar"));
            Write(Path.Combine(r, "b.zip"));
            Write(Path.Combine(r, "c.litemod"));
            Write(Path.Combine(r, "notes.txt"));      // 非 Mod，排除
            Directory.CreateDirectory(Path.Combine(r, "sub"));
            Write(Path.Combine(r, "sub", "d.jar"));   // 子目录，未开启 Legacy 则排除
        });

        var list = LocalResourceScanner.Scan(root);
        Assert.Equal(3, list.Count);
        Assert.Equal(new[] { "a.jar", "b.zip", "c.litemod" }, list.Select(f => f.File.Name));
    }

    [Fact]
    public void Scan_同名启用与禁用只保留启用的一份()
    {
        var root = MakeTree(r =>
        {
            Write(Path.Combine(r, "mod.jar"));
            Write(Path.Combine(r, "mod.jar.disabled")); // 禁用份
        });

        var list = LocalResourceScanner.Scan(root);
        Assert.Single(list);
        Assert.Equal("mod.jar", list[0].File.Name);
        Assert.True(list[0].IsEnabled);
    }

    [Fact]
    public void Scan_LegacyForge子目录按判定收录()
    {
        var root = MakeTree(r =>
        {
            Directory.CreateDirectory(Path.Combine(r, "1.12.2"));
            Write(Path.Combine(r, "1.12.2", "e.jar"));
            Write(Path.Combine(r, "top.jar"));
        });

        // 不开启：子目录 e.jar 不收录
        var without = LocalResourceScanner.Scan(root);
        Assert.Single(without);
        Assert.Equal("top.jar", without[0].File.Name);

        // 开启且仅匹配 "1.12.2"：e.jar 收录
        var with = LocalResourceScanner.Scan(root, new LocalResourceScanner.Options
        {
            IsLegacyForgeSubfolder = name => name == "1.12.2",
        });
        Assert.Equal(2, with.Count);
        Assert.Contains(with, f => f.File.Name == "e.jar");
    }

    [Fact]
    public void Scan_文件夹不存在返回空()
    {
        var list = LocalResourceScanner.Scan(Path.Combine(Path.GetTempPath(), "pcldl_noexist_" + Guid.NewGuid().ToString("N")));
        Assert.Empty(list);
    }
}
