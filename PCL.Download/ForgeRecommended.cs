using System.Text.Json.Nodes;

namespace PCL.Download;

/// <summary>
/// Forge 官方 promos 接口 → <c>Cache/ForgeRecommendedList.json</c> 的转换。
/// <para>
/// promos 返回的是「构建通道」列表（<c>1.15.2-recommended</c>、<c>1.20.1-latest</c> …），
/// 而 UI 需要的是「每个 MC 版本一个推荐 Forge 版本」的扁平表。这段转换原先写在
/// <c>ModDownloadLib.McDownloadForgeRecommendedRefresh</c> 里，用字符串拼出一份 JSON——
/// 拼错一个引号就整份不可读，且只在「推荐版本不显示」时才被发现。
/// </para>
/// <para>
/// 请求仍在宿主：那条路径带重试与浏览器头，属下载层的既有能力，不在这里复刻。
/// </para>
/// </summary>
public static class ForgeRecommended
{
    /// <summary>推荐通道名的后缀。</summary>
    public const string RecommendedSuffix = "-recommended";

    /// <summary>promos 接口地址（镜像）。注意它的响应是「构建通道」列表，不是版本清单。</summary>
    public const string PromosUrl = "https://bmclapi2.bangbang93.com/forge/promos";

    /// <summary>
    /// promos 响应体的长度下限。镜像偶发返回一个几十字节的错误页，此时应当失败而不是写出一份空缓存。
    /// </summary>
    public const int MinPromosLength = 1000;

    /// <summary>
    /// 推荐版本条目数下限。低于此值多半是接口改版或镜像降级，写进缓存会让「推荐版本」大面积缺失。
    /// </summary>
    public const int MinRecommendedCount = 5;

    /// <summary>
    /// 把 promos 响应体转换成缓存用的扁平表：<c>{"1.15.2": "31.2.0", ...}</c>。
    /// </summary>
    /// <param name="promosJson">promos 接口的响应原文。</param>
    /// <exception cref="InvalidOperationException">响应过短、条目过少，或条目结构不符。</exception>
    /// <remarks>
    /// 同一 MC 版本可能有多条 recommended（历史上重新推荐过），后出现的覆盖先出现的——
    /// 与逐条写入字典的语义一致，故用 <see cref="JsonObject"/> 的索引器直接覆盖。
    /// </remarks>
    public static string BuildCacheJson(string promosJson)
    {
        ArgumentNullException.ThrowIfNull(promosJson);

        if (promosJson.Length < MinPromosLength)
            throw new InvalidOperationException($"获取的结果过短（{promosJson}）");

        var parsed = JsonNode.Parse(promosJson) as JsonArray
                     ?? throw new InvalidOperationException("Forge promos 响应不是数组");

        var table = new JsonObject();
        foreach (var entry in parsed)
        {
            if (entry is not JsonObject item) continue;

            // 缺字段的条目直接跳过：接口里确实存在既无 name 也无 build 的记录
            var name = item["name"]?.AsString();
            if (string.IsNullOrEmpty(name)) continue;
            if (!name.EndsWith(RecommendedSuffix, StringComparison.Ordinal)) continue;

            // 整个 build 缺失属「这条记录没用」，跳过；build 在但 version 不在属接口改版，报错
            var build = item["build"];
            if (build is null) continue;
            var version = build["version"]?.AsString();
            if (string.IsNullOrEmpty(version))
                throw new InvalidOperationException($"Forge promos 条目缺少 build.version：{name}");

            table[name[..^RecommendedSuffix.Length]] = version;
        }

        if (table.Count < MinRecommendedCount)
            throw new InvalidOperationException($"获取的推荐版本数过少（{promosJson}）");

        // 不缩进：这份文件只给机器读，体积小一点、diff 也稳定
        return table.ToJsonString();
    }
}
