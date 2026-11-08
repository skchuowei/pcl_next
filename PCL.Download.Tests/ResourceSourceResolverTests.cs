namespace PCL.Download.Tests;

/// <summary>
/// 候选源解析（VB <c>DlModRequest</c> 的源列表构造）对拍测试。
/// 关注：四种分支的候选顺序、每个候选的超时、无镜像映射时的回退。
/// </summary>
public class ResourceSourceResolverTests
{
    private const string CurseForgeUrl = "https://api.curseforge.com/v1/mods/238222/files";
    private const string ModrinthUrl = "https://api.modrinth.com/v2/project/jei/version";
    private const string MirrorPrefix = "https://mod.mcimirror.top";

    [Fact]
    public void PreferMirror_TriesMirrorTwiceThenOfficial()
    {
        var candidates = ResourceSourceResolver.Resolve(CurseForgeUrl, ModSourcePreference.PreferMirror);

        Assert.Equal(3, candidates.Count);
        Assert.All(candidates.Take(2), c =>
        {
            Assert.StartsWith(MirrorPrefix, c.Url);
            Assert.True(c.IsMirror);
            Assert.Equal(10, c.TimeoutSeconds);
        });
        Assert.Equal(CurseForgeUrl, candidates[2].Url);
        Assert.False(candidates[2].IsMirror);
        Assert.Equal(30, candidates[2].TimeoutSeconds);
    }

    [Fact]
    public void Automatic_OfficialFirstThenMirrorThenBothWideTimeout()
    {
        var candidates = ResourceSourceResolver.Resolve(CurseForgeUrl, ModSourcePreference.Automatic);

        Assert.Equal(4, candidates.Count);
        Assert.Equal((CurseForgeUrl, 10, false), (candidates[0].Url, candidates[0].TimeoutSeconds, candidates[0].IsMirror));
        Assert.StartsWith(MirrorPrefix, candidates[1].Url);
        Assert.Equal(10, candidates[1].TimeoutSeconds);
        Assert.Equal((CurseForgeUrl, 30), (candidates[2].Url, candidates[2].TimeoutSeconds));
        Assert.StartsWith(MirrorPrefix, candidates[3].Url);
        Assert.Equal(30, candidates[3].TimeoutSeconds);
    }

    [Fact]
    public void Automatic_ModrinthGetsTwentySecondFirstTimeout()
    {
        // VB：Modrinth 官方接口至少给 20s，否则常常返回不过来
        var candidates = ResourceSourceResolver.Resolve(ModrinthUrl, ModSourcePreference.Automatic);

        Assert.Equal(20, candidates[0].TimeoutSeconds);
        Assert.Equal(ModrinthUrl, candidates[0].Url);
    }

    [Fact]
    public void PreferOfficial_TriesOfficialTwiceThenMirror()
    {
        var candidates = ResourceSourceResolver.Resolve(CurseForgeUrl, ModSourcePreference.PreferOfficial);

        Assert.Equal(3, candidates.Count);
        Assert.Equal(CurseForgeUrl, candidates[0].Url);
        Assert.Equal(10, candidates[0].TimeoutSeconds);
        Assert.Equal((CurseForgeUrl, 30), (candidates[1].Url, candidates[1].TimeoutSeconds));
        Assert.StartsWith(MirrorPrefix, candidates[2].Url);
        Assert.True(candidates[2].IsMirror);
    }

    [Fact]
    public void NoMirrorMapping_FallsBackToOfficialOnly()
    {
        // edge.forgecdn.net 会被映射，但 example.com 不会
        var candidates = ResourceSourceResolver.Resolve("https://example.com/api/x", ModSourcePreference.PreferMirror);

        Assert.Equal(2, candidates.Count);
        Assert.All(candidates, c => Assert.Equal("https://example.com/api/x", c.Url));
        Assert.DoesNotContain(candidates, c => c.IsMirror);
        Assert.Equal([10, 30], candidates.Select(c => c.TimeoutSeconds));
    }

    [Fact]
    public void EmptyUrl_Throws()
    {
        Assert.ThrowsAny<ArgumentException>(() => ResourceSourceResolver.Resolve(" "));
    }
}
