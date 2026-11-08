using PCL.Download;

namespace PCL.Download.Tests;

/// <summary>
/// FuzzySearch（VB ModBase 模糊搜索算法）对拍测试。
/// </summary>
public class FuzzySearchTests
{
    [Fact]
    public void Search_完全匹配项置AbsoluteRight且排在最前()
    {
        var entries = new List<FuzzySearch.SearchEntry<string>>
        {
            new() { Item = "gamma", SearchSource = [new FuzzySearch.SearchSource("gamma delta", 1)] },
            new() { Item = "alpha", SearchSource = [new FuzzySearch.SearchSource("alpha beta", 1), new FuzzySearch.SearchSource("x", 0.05)] },
        };
        var result = FuzzySearch.Search(entries, "alpha", 5, 0.1);
        Assert.Equal("alpha", result[0].Item);
        Assert.True(result[0].AbsoluteRight);
    }

    [Fact]
    public void Search_无匹配时按相似度降序返回模糊结果()
    {
        var entries = new List<FuzzySearch.SearchEntry<string>>
        {
            new() { Item = "xyz", SearchSource = [new FuzzySearch.SearchSource("xyz", 1)] },
            new() { Item = "alphabet", SearchSource = [new FuzzySearch.SearchSource("alphabet", 1)] },
            new() { Item = "alpha", SearchSource = [new FuzzySearch.SearchSource("alpha", 1)] },
        };
        var result = FuzzySearch.Search(entries, "alpha", 5, 0.05);
        //alpha 完全匹配在最前
        Assert.Equal("alpha", result[0].Item);
        var rest = result.Skip(1).Select(r => r.Item).ToList();
        //"alphabet" 与 "alpha" 相似，被保留；"xyz" 与 "alpha" 几乎无相似度，被 minBlur 阈值过滤
        Assert.Contains("alphabet", rest);
        Assert.DoesNotContain("xyz", rest);
    }

    [Fact]
    public void Search_空列表返回空()
    {
        var result = FuzzySearch.Search(new List<FuzzySearch.SearchEntry<int>>(), "q");
        Assert.Empty(result);
    }

    [Fact]
    public void Search_受MaxBlurCount限制模糊结果数量()
    {
        var entries = new List<FuzzySearch.SearchEntry<string>>();
        for (int i = 0; i < 10; i++)
            entries.Add(new FuzzySearch.SearchEntry<string> { Item = $"w{i}", SearchSource = [new FuzzySearch.SearchSource($"w{i}", 1)] });
        var result = FuzzySearch.Search(entries, "zzz", maxBlurCount: 3, minBlurSimilarity: -1);
        //minBlurSimilarity=-1 → 全部候选；maxBlurCount=3 且无完全匹配 → 至多 3 个模糊结果
        Assert.True(result.Count <= 3);
    }

    [Fact]
    public void Search_空查询时所有项置AbsoluteRight并返回()
    {
        //忠实于 VB：空查询使 queryParts 为空数组，All() 短路为 True，
        //于是每个项都被标记为完全匹配（AbsoluteRight）并全部返回。
        var entries = new List<FuzzySearch.SearchEntry<string>>
        {
            new() { Item = "a", SearchSource = [new FuzzySearch.SearchSource("a", 1)] },
            new() { Item = "b", SearchSource = [new FuzzySearch.SearchSource("b", 1)] },
        };
        var result = FuzzySearch.Search(entries, "");
        Assert.Equal(2, result.Count);
        Assert.All(result, r => Assert.True(r.AbsoluteRight));
    }
}
