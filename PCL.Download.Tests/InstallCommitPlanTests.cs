namespace PCL.Download.Tests;

/// <summary>
/// InstallCommitPlanner（修改版本时的变更计划）测试。
/// <para>
/// 这一段只算不改，出错的表现是「该删的没删 / 该覆盖的没覆盖」，装完之后表面看不出来，
/// 下次启动才发现版本里混着上一个加载器的库。两处比较规则还不一致（见类型注释），
/// 所以每条规则都单独锁一遍。
/// </para>
/// </summary>
public class InstallCommitPlanTests
{
    private const string InstanceName = "1.20.1-Forge";

    private static readonly string Root = Path.Combine(Path.GetTempPath(), "pcl-commit-test");
    private static readonly string BuildFolder = Path.Combine(Root, "build", InstanceName);
    private static readonly string TargetFolder = Path.Combine(Root, "instances", ".minecraft");
    private static readonly string VersionFolder = Path.Combine(TargetFolder, "versions", InstanceName);

    private static InstallCommitPlan Build(
        IReadOnlyList<string> newPaths,
        IReadOnlyList<string> oldPaths,
        string? oldJsonPath = null,
        bool? legacyResources = false)
        => InstallCommitPlanner.Build(new InstallCommitPlanRequest
        {
            BuildFolder = BuildFolder,
            TargetFolder = TargetFolder,
            VersionFolder = VersionFolder,
            InstanceName = InstanceName,
            OldJsonPath = oldJsonPath,
            NewRelativePaths = newPaths,
            OldRelativePaths = oldPaths,
            LegacyResourcesExist = legacyResources,
        });

    [Fact]
    public void Build_新清单的文件逐个从构建目录搬到目标目录()
    {
        var plan = Build(["mods/a.jar", "mods/b.jar"], []);

        Assert.Equal(2, plan.FilesToMove.Count);
        Assert.Equal(new InstallCommitMove(Path.Combine(BuildFolder, "mods/a.jar"), Path.Combine(TargetFolder, "mods/a.jar")),
            plan.FilesToMove[0]);
        Assert.Equal(new InstallCommitMove(Path.Combine(BuildFolder, "mods/b.jar"), Path.Combine(TargetFolder, "mods/b.jar")),
            plan.FilesToMove[1]);
    }

    [Fact]
    public void Build_新清单为空时不产生搬移项()
    {
        Assert.Empty(Build([], []).FilesToMove);
    }

    [Fact]
    public void Build_旧清单里有而新清单里没有的文件要被删除()
    {
        var plan = Build(["mods/keep.jar"], ["mods/keep.jar", "mods/gone.jar"]);

        var gone = Path.GetFullPath(Path.Combine(TargetFolder, "mods/gone.jar"));
        Assert.Contains(gone, plan.FilesToDelete);
        Assert.DoesNotContain(Path.GetFullPath(Path.Combine(TargetFolder, "mods/keep.jar")), plan.FilesToDelete);
    }

    [Fact]
    public void Build_新旧清单相对路径大小写不同时按区分大小写视为不同文件()
    {
        // 原实现用的是默认（Ordinal）集合：清单里 mods/A.jar 与 mods/a.jar 不是同一项，
        // 于是老的那份会被删、新的那份会被搬——保留这个行为，不做「顺手归一」
        var plan = Build(["mods/a.jar"], ["mods/A.jar"]);

        Assert.Contains(Path.GetFullPath(Path.Combine(TargetFolder, "mods/A.jar")), plan.FilesToDelete);
        Assert.Single(plan.FilesToMove);
    }

    [Fact]
    public void Build_删除项使用归一后的绝对路径()
    {
        var plan = Build([], ["mods/old.jar"]);

        var deleted = Path.GetFullPath(Path.Combine(TargetFolder, "mods", "old.jar"));
        Assert.Contains(deleted, plan.FilesToDelete);
        Assert.All(plan.FilesToDelete, path => Assert.False(path.EndsWith(Path.DirectorySeparatorChar)));
    }

    [Fact]
    public void Build_总是清理实例自己的json与jar()
    {
        var oldJson = Path.Combine(VersionFolder, InstanceName + ".json");

        var plan = Build([], [], oldJson);

        Assert.Contains(oldJson, plan.FilesToDelete);
        Assert.Contains(Path.Combine(VersionFolder, InstanceName + ".jar"), plan.FilesToDelete);
    }

    [Fact]
    public void Build_旧json的jar与版本目录的jar是同一条时只保留一次()
    {
        // 正常情况下 OldJsonPath 就落在版本目录下，这两项必然重合
        var oldJson = Path.Combine(VersionFolder, InstanceName + ".json");

        var plan = Build([], [], oldJson);

        Assert.Equal(2, plan.FilesToDelete.Count); // .json + .jar
        Assert.Single(plan.FilesToDelete, path => path.EndsWith(".jar", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Build_没有旧json时只清理版本目录下的jar()
    {
        var plan = Build([], [], oldJsonPath: null);

        var deleted = Assert.Single(plan.FilesToDelete);
        Assert.Equal(Path.Combine(VersionFolder, InstanceName + ".jar"), deleted);
    }

    [Fact]
    public void Build_删除列表按忽略大小写去重()
    {
        // 同一个文件在旧清单里出现了两种大小写写法，只删一次
        var plan = Build([], ["mods/x.jar", "mods/X.jar"]);

        Assert.Equal(2, plan.FilesToDelete.Count); // mods/x.jar（去重后一条）+ 版本目录的 jar
    }

    [Fact]
    public void Build_构建目录有旧版resources时要求复制()
    {
        Assert.True(Build([], [], legacyResources: true).CopyLegacyResources);
        Assert.False(Build([], [], legacyResources: false).CopyLegacyResources);
    }

    [Fact]
    public void Build_未声明时按构建目录下有无resources判断()
    {
        // Root 是本用例自造的临时路径，不存在 resources 子目录
        Assert.False(Build([], [], legacyResources: null).CopyLegacyResources);
        Assert.False(Directory.Exists(Path.Combine(BuildFolder, "resources")));
    }

    [Fact]
    public void ForCompare_去掉尾分隔符()
    {
        var path = Path.Combine(Root, "a");
        Assert.Equal(Path.GetFullPath(path), InstallCommitPlanner.ForCompare(path + Path.DirectorySeparatorChar));
    }

    [Fact]
    public void ForCompare_把正斜杠统一成平台分隔符()
    {
        var mixed = Root.Replace(Path.DirectorySeparatorChar, '/') + "/sub/a.jar";
        Assert.Equal(Path.GetFullPath(Path.Combine(Root, "sub", "a.jar")), InstallCommitPlanner.ForCompare(mixed));
    }

    [Fact]
    public void ForCompare_相对路径展开成绝对路径()
    {
        var relative = Path.Combine("sub", "a.jar");
        Assert.Equal(Path.GetFullPath(relative), InstallCommitPlanner.ForCompare(relative));
    }

    [Fact]
    public void ForCompare_空路径抛参数异常()
    {
        Assert.Throws<ArgumentException>(() => InstallCommitPlanner.ForCompare("  "));
    }
}
