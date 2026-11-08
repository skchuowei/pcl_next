using System.Text;

namespace PCL.Download;

/// <summary>
/// 多段文本加权模糊搜索（平移自 VB Modules/Base/ModBase.vb 的同名算法）。
/// 在搜索时对每个文本源单独加权，但单个文本源内的多个别名只取最高的一个相似度。
/// 会修改 Entries 中每项的 <see cref="SearchEntry{T}.Similarity"/> 与 <see cref="SearchEntry{T}.AbsoluteRight"/> 字段。
/// </summary>
public static class FuzzySearch
{
    /// <summary>单段文本相对查询的相似度（0~1+）。</summary>
    private static double SearchSimilarity(string source, string query)
    {
        if (string.IsNullOrEmpty(source) || string.IsNullOrEmpty(query)) return 0;
        var qp = 0;
        double lenSum = 0;
        var str = new StringBuilder(source.ToLowerInvariant().Replace(" ", ""));
        query = query.ToLowerInvariant().Replace(" ", "");
        var sourceLength = str.Length;
        var queryLength = query.Length;
        if (queryLength == 0) return 0;
        while (qp < queryLength)
        {
            var sp = 0;
            var lenMax = 0;
            var spMax = 0;
            var currentSourceLength = str.Length;
            while (sp < currentSourceLength)
            {
                var len = 0;
                while ((qp + len) < queryLength && (sp + len) < currentSourceLength && str[sp + len] == query[qp + len])
                    len++;
                if (len > lenMax)
                {
                    lenMax = len;
                    spMax = sp;
                }
                sp += len > 0 ? len : 1;
            }
            if (lenMax > 0)
            {
                str.Remove(spMax, lenMax); //将源中的对应字段移除
                var incWeight = (Math.Pow(1.4, 3 + lenMax) - 3.6); //根据长度加成
                incWeight *= 1 + 0.3 * Math.Max(0, 3 - Math.Abs(qp - spMax)); //根据位置加成
                lenSum += incWeight;
            }
            qp += lenMax > 0 ? lenMax : 1;
        }
        //计算结果：重复字段量 × 源长度影响比例
        return (lenSum / queryLength) * (3 / Math.Sqrt(sourceLength + 15)) * (queryLength <= 2 ? 3 - queryLength : 1);
    }

    /// <summary>获取多段文本加权后的相似度。</summary>
    private static double SearchSimilarityWeighted(List<SearchSource> source, string query)
    {
        double totalWeight = 0, sum = 0;
        foreach (var pair in source)
        {
            if (pair.Aliases.Count > 0)
                sum += pair.Aliases.Max(a => SearchSimilarity(a, query)) * pair.Weight;
            totalWeight += pair.Weight;
        }
        return sum / totalWeight;
    }

    /// <summary>用于搜索的项目（平移自 VB SearchEntry(Of T)）。</summary>
    public sealed class SearchEntry<T>
    {
        /// <summary>该项目对应的源数据。</summary>
        public required T Item { get; init; }
        /// <summary>该项目用于搜索的文本源。</summary>
        public required List<SearchSource> SearchSource { get; init; }
        /// <summary>相似度。</summary>
        public double Similarity { get; set; }
        /// <summary>是否完全匹配。</summary>
        public bool AbsoluteRight { get; set; }
        public override string ToString() => $"{Math.Round(Similarity, 3)} - {Item}";
    }

    /// <summary>单个用于搜索的文本源（平移自 VB SearchSource）。</summary>
    public sealed class SearchSource
    {
        public List<string> Aliases { get; }
        public double Weight { get; }
        public SearchSource(string[] aliases, double weight = 1)
        {
            Aliases = aliases.ToList();
            Weight = weight;
        }
        public SearchSource(string text, double weight = 1)
        {
            Aliases = [text];
            Weight = weight;
        }
    }

    /// <summary>
    /// 进行多段文本加权搜索，获取相似度较高的数项结果。
    /// </summary>
    /// <param name="maxBlurCount">返回的最大模糊结果数。</param>
    /// <param name="minBlurSimilarity">返回结果要求的最低相似度。</param>
    public static List<SearchEntry<T>> Search<T>(List<SearchEntry<T>> entries, string query, int maxBlurCount = 5, double minBlurSimilarity = 0.1)
    {
        var resultList = new List<SearchEntry<T>>();
        if (entries.Count == 0) return resultList;
        var queryParts = query.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var candidates = new List<SearchEntry<T>>();
        foreach (var entry in entries)
        {
            entry.Similarity = SearchSimilarityWeighted(entry.SearchSource, query);
            entry.AbsoluteRight = queryParts.All(queryPart =>
                entry.SearchSource.Any(source => source.Aliases.Any(alias => alias.Replace(" ", "").ContainsIgnoreCase(queryPart))));
            if (entry.AbsoluteRight || entry.Similarity >= minBlurSimilarity) candidates.Add(entry);
        }
        //按照相似度进行排序
        candidates = VbCompat.SortByComparison(candidates, (left, right) =>
            left.AbsoluteRight != right.AbsoluteRight ? left.AbsoluteRight : left.Similarity > right.Similarity);
        //返回结果
        var blurCount = 0;
        foreach (var entry in candidates)
        {
            if (entry.AbsoluteRight)
            {
                resultList.Add(entry); //完全匹配，直接加入
            }
            else
            {
                if (blurCount == maxBlurCount) break;
                resultList.Add(entry);
                blurCount++; //模糊结果计数
            }
        }
        return resultList;
    }

    /// <summary>返回所有达到最大值的元素（对应 VB 的 MaxByAll）。</summary>
    internal static List<T> MaxByAll<T>(this IEnumerable<T> source, Func<T, double> selector)
    {
        var list = source.ToList();
        if (list.Count == 0) return list;
        var max = list.Max(selector);
        return list.Where(x => selector(x).Equals(max)).ToList();
    }
}
