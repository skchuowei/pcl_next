using System.Globalization;
using System.Text.Json.Nodes;

namespace PCL.Download;

/// <summary>
/// 加载器列表的解析器（逐字平移自 VB <c>ModDownload</c> 的各个 <c>*Main</c> 方法）。
/// <para>
/// 只做「文本/JSON → 条目」的转换，不发请求、不管候选源调度；这样每条正则与字段规则都能单测。
/// 所有数量下限、字段对应关系失败时的异常文案都保留 VB 原文，便于与旧日志对照。
/// </para>
/// </summary>
internal static class LoaderListParsers
{
    #region OptiFine

    /// <summary>解析 OptiFine 官方下载页（HTML）。</summary>
    /// <param name="html">页面正文。</param>
    /// <param name="url">请求地址，仅用于异常定位。</param>
    public static List<OptiFineListEntry> OptiFineFromHtml(string html, string url)
    {
        if (html.Length < 200)
            throw ParseError(url, $"获取到的版本列表长度不足（{html}）");

        var forge = VbCompat.RegexSearch(html, "(?<=colForge'>)[^<]*");
        var releaseTime = VbCompat.RegexSearch(html, "(?<=colDate'>)[^<]+");
        var names = VbCompat.RegexSearch(html, "(?<=OptiFine_)[0-9A-Za-z_.]+(?=.jar\")");

        if (releaseTime.Count != names.Count) throw ParseError(url, "版本与发布时间数据无法对应");
        if (forge.Count != names.Count) throw ParseError(url, "版本与 Forge 兼容数据无法对应");
        if (releaseTime.Count < LoaderListSources.OptiFineMinimumCount)
            throw ParseError(url, $"获取到的版本数量不足（{html}）");

        var versions = new List<OptiFineListEntry>(releaseTime.Count);
        for (var i = 0; i < releaseTime.Count; i++)
        {
            var name = names[i].Replace("_", " ");
            var dateParts = releaseTime[i].Split('.');
            if (dateParts.Length < 3) throw ParseError(url, $"无法解析发布时间：{releaseTime[i]}");

            var requiredForge = forge[i].Replace("Forge ", "").Replace("#", "");
            var inherit = StripTrailingZero(name.Split(' ')[0]);
            var entry = new OptiFineListEntry
            {
                DisplayName = name.Replace("HD U ", "").Replace(".0 ", " "),
                ReleaseTime = $"{dateParts[2]}/{dateParts[1]}/{dateParts[0]}",
                IsPreview = name.ContainsIgnoreCase("pre"),
                Inherit = inherit,
                FileName = (name.ContainsIgnoreCase("pre") ? "preview_" : "") + "OptiFine_" + name.Replace(" ", "_") + ".jar",
                RequiredForgeVersion = requiredForge.Contains("N/A") ? null : requiredForge,
                InstanceName = inherit + "-OptiFine_" + name.AfterFirst(" ").Replace(" ", "_"),
            };
            versions.Add(entry);
        }
        return versions;
    }

    /// <summary>解析 OptiFine 的 BMCLAPI 版本列表（JSON 数组）。</summary>
    public static List<OptiFineListEntry> OptiFineFromJson(string json, string url)
    {
        var root = ParseJson(json, url) as JsonArray
                   ?? throw ParseError(url, "OptiFine 版本列表根节点不是数组");

        var versions = new List<OptiFineListEntry>(root.Count);
        foreach (var node in root)
        {
            if (node is not JsonObject token) throw ParseError(url, $"OptiFine 版本项不是对象：{node?.ToJsonString()}");
            var mcVersion = token["mcversion"].AsString() ?? "";
            var type = token["type"].AsString() ?? "";
            var patch = token["patch"].AsString() ?? "";

            var requiredForge = (token["forge"].AsString() ?? "").Replace("Forge ", "").Replace("#", "");
            var inherit = StripTrailingZero(mcVersion);
            versions.Add(new OptiFineListEntry
            {
                DisplayName = (mcVersion + type.Replace("HD_U", "").Replace("_", " ") + " " + patch).Replace(".0 ", " "),
                ReleaseTime = "",
                IsPreview = patch.ContainsIgnoreCase("pre"),
                Inherit = inherit,
                FileName = token["filename"].AsString() ?? "",
                RequiredForgeVersion = requiredForge.Contains("N/A") ? null : requiredForge,
                InstanceName = inherit + "-OptiFine_" +
                               (type + " " + patch).Replace(".0 ", " ").Replace(" ", "_").Replace(inherit + "_", ""),
            });
        }
        return versions;
    }

    #endregion

    #region Forge 的 MC 版本列表

    /// <summary>解析 Forge 官方索引页里的 MC 版本名（HTML）。</summary>
    public static List<string> ForgeGameVersionsFromHtml(string html, string url)
    {
        if (html.Length < 200)
            throw ParseError(url, $"获取到的版本列表长度不足（{html}）");

        var names = VbCompat.RegexSearch(html, "(?<=a href=\"index_)[0-9.]+((_pre|[_-]snapshot[_-]?)[0-9]?)?(?=.html)");
        names.Add("1.2.4"); // 1.2.4 不会被匹配上
        if (names.Count < LoaderListSources.ForgeGameListMinimumCount)
            throw ParseError(url, $"获取到的版本数量不足（{html}）");
        return names;
    }

    /// <summary>解析 Forge 的 BMCLAPI MC 版本列表（JSON 字符串数组）。</summary>
    public static List<string> ForgeGameVersionsFromJson(string json, string url)
    {
        var root = ParseJson(json, url) as JsonArray
                   ?? throw ParseError(url, "Forge 版本列表根节点不是数组");

        var names = new List<string>(root.Count);
        foreach (var node in root)
        {
            var name = node.AsString();
            if (name is not null) names.Add(name);
        }
        if (names.Count < LoaderListSources.ForgeGameListMinimumCount)
            throw ParseError(url, "获取到的版本数量不足");
        return names;
    }

    #endregion

    #region Forge 版本列表

    /// <summary>解析 Forge 官方某个 MC 版本下的版本列表页（HTML）。</summary>
    /// <param name="html">页面正文。</param>
    /// <param name="mcVersion">该页面对应的 MC 版本。</param>
    /// <param name="url">请求地址，仅用于异常定位。</param>
    public static List<ForgeVersionEntry> ForgeVersionsFromHtml(string html, string mcVersion, string url)
    {
        if (html.Length < 1000)
            throw ParseError(url, $"获取到的版本列表长度不足（{html}）");

        var tableEnd = html.LastIndexOf("</table>", StringComparison.Ordinal);
        if (tableEnd < 0) throw ParseError(url, "页面中找不到版本表格");

        var versionCodes = html[..tableEnd].Split("<td class=\"download-version");
        var versions = new List<ForgeVersionEntry>(versionCodes.Length);
        for (var i = 1; i < versionCodes.Length; i++)
        {
            var code = versionCodes[i];
            try
            {
                // 基础信息获取
                var name = VbCompat.RegexSeek(code, @"(?<=[^(0-9)]+)[0-9\.]+");
                if (string.IsNullOrEmpty(name)) continue;

                var isRecommended = code.Contains("fa promo-recommended", StringComparison.Ordinal);

                // 分支获取
                var branch = VbCompat.RegexSeek(code, $"(?<=-{name}-)[^-\"]+(?=-[a-z]+.[a-z]{{3}})");
                if (string.IsNullOrWhiteSpace(branch)) branch = null;

                // 发布时间获取：原格式 "2021-02-15 03:24:02"，按 UTC 解释后转本地时间
                var releaseTimeOriginal = VbCompat.RegexSeek(code, "(?<=\"download-time\" title=\")[^\"]+");
                var releaseTime = FormatForgeReleaseTime(releaseTimeOriginal, url);

                // 分类与 MD5 获取
                string? hash;
                string category;
                if (code.Contains("classifier-installer\"", StringComparison.Ordinal))
                {
                    // installer.jar，支持范围 ~753 (~ 1.6.1 部分), 738~684 (1.5.2 全部)
                    code = Slice(code, "installer.jar");
                    hash = VbCompat.RegexSeek(code, "(?<=MD5:</strong> )[^<]+");
                    category = "installer";
                }
                else if (code.Contains("classifier-universal\"", StringComparison.Ordinal))
                {
                    // universal.zip，支持范围 751~449 (1.6.1 部分), 682~183 (1.5.1 ~ 1.3.2 部分)
                    code = Slice(code, "universal.zip");
                    hash = VbCompat.RegexSeek(code, "(?<=MD5:</strong> )[^<]+");
                    category = "universal";
                }
                else if (code.Contains("client.zip", StringComparison.Ordinal))
                {
                    // client.zip，支持范围 182~ (1.3.2 部分 ~)
                    code = Slice(code, "client.zip");
                    hash = VbCompat.RegexSeek(code, "(?<=MD5:</strong> )[^<]+");
                    category = "client";
                }
                else
                {
                    // 没有任何下载（1.6.4 有一部分这种情况）
                    continue;
                }

                versions.Add(ForgeVersionEntry.Create(name, branch, mcVersion, category,
                    hash?.ReplaceLineEndings(""), isRecommended, releaseTime));
            }
            catch (ResourceRequestException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw ParseError(url, $"Forge 官方源版本信息提取失败（{code}）", ex);
            }
        }

        if (versions.Count == 0) throw new ResourceRequestException(ResourceRequestErrorKind.NotFound, url, "无");
        return versions;
    }

    /// <summary>解析 Forge 的 BMCLAPI 某个 MC 版本下的版本列表（JSON 数组）。</summary>
    /// <param name="json">响应正文。</param>
    /// <param name="mcVersion">该列表对应的 MC 版本。</param>
    /// <param name="recommended">推荐版本号；null 表示无推荐版本信息。</param>
    /// <param name="url">请求地址，仅用于异常定位。</param>
    public static List<ForgeVersionEntry> ForgeVersionsFromJson(string json, string mcVersion, string? recommended,
        string url)
    {
        var root = ParseJson(json, url) as JsonArray
                   ?? throw ParseError(url, "Forge 版本列表根节点不是数组");

        var versions = new List<ForgeVersionEntry>(root.Count);
        foreach (var node in root)
        {
            if (node is not JsonObject token) throw ParseError(url, $"Forge 版本项不是对象：{node?.ToJsonString()}");

            // 分类与 Hash 获取：installer > universal > client，同类里先出现的优先
            string? hash = null;
            var category = "unknown";
            var priority = -1;
            foreach (var fileNode in token["files"].AsArrayOrEmpty())
            {
                if (fileNode is not JsonObject file) continue;
                var format = file["format"].AsString();
                switch (file["category"].AsString())
                {
                    case "installer" when format == "jar":
                        hash = file["hash"].AsString();
                        category = "installer";
                        priority = 2;
                        break;
                    case "universal" when priority <= 1 && format == "zip":
                        hash = file["hash"].AsString();
                        category = "universal";
                        priority = 1;
                        break;
                    case "client" when priority <= 0 && format == "zip":
                        hash = file["hash"].AsString();
                        category = "client";
                        priority = 0;
                        break;
                }
            }

            var name = token["version"].AsString() ?? "";
            var branch = token["branch"].AsString();
            versions.Add(ForgeVersionEntry.Create(name, branch, mcVersion, category, hash,
                recommended == name, FormatIsoReleaseTime(token["modified"], url)));
        }

        if (versions.Count == 0) throw new ResourceRequestException(ResourceRequestErrorKind.NotFound, url, "无");
        return versions;
    }

    #endregion

    #region NeoForge

    /// <summary>
    /// 解析 NeoForge 的版本列表：VB 直接对「Legacy + 新版」两段 JSON 原文做正则扫描，
    /// 不区分响应结构（官方是 versions 数组、镜像是 files 数组），这里保持一致。
    /// </summary>
    /// <param name="latestText">新版包（neoforge）的响应正文。</param>
    /// <param name="legacyText">Legacy 包（forge，对应 1.20.1）的响应正文。</param>
    /// <param name="url">请求地址，仅用于异常定位。</param>
    public static List<NeoForgeListEntry> NeoForgeFromText(string latestText, string legacyText, string url)
    {
        if (latestText.Length < LoaderListSources.NeoForgeMinimumLength ||
            legacyText.Length < LoaderListSources.NeoForgeMinimumLength)
            throw ParseError(url, $"获取到的版本列表长度不足（{latestText}）");

        var names = VbCompat.RegexSearch(legacyText + latestText,
            "(?<=\")(1\\.20\\.1-)?\\d+\\.[^.]+\\.\\d+(\\.\\d+)?(-(beta|alpha)(\\.\\d+)?)?(\\+snapshot-\\d+)?(?=\")");

        var versions = new List<NeoForgeListEntry>(names.Count);
        foreach (var name in names)
        {
            if (name == "47.1.82") continue; // 这个版本虽然在版本列表中，但不能下载
            versions.Add(CreateNeoForgeEntry(name, url));
        }

        if (versions.Count == 0) throw new ResourceRequestException(ResourceRequestErrorKind.NotFound, url, "无");

        // VB 用 OrderByDescending(版本)，即「版本号降序，同版本号按版本名降序」
        versions.Sort((left, right) =>
        {
            var byVersion = right.Version.CompareTo(left.Version);
            return byVersion != 0 ? byVersion : McVersionUtil.CompareVersion(right.VersionName, left.VersionName);
        });
        return versions;
    }

    /// <summary>构造一条 NeoForge 记录，含 VB 构造函数里的三种版本名形态。</summary>
    private static NeoForgeListEntry CreateNeoForgeEntry(string apiName, string url)
    {
        var isBeta = apiName.ContainsIgnoreCase("beta") || apiName.ContainsIgnoreCase("alpha");

        if (apiName.Contains("1.20.1", StringComparison.Ordinal))
        {
            // 1.20.1-47.1.99：Legacy 包，版本号第一位固定为 19
            var versionName = apiName.Replace("1.20.1-", "");
            return new NeoForgeListEntry
            {
                ApiName = apiName,
                VersionName = versionName,
                Version = new Version("19." + versionName),
                Inherit = "1.20.1",
                IsBeta = isBeta,
            };
        }

        if (apiName.StartsWith("0.", StringComparison.Ordinal))
        {
            // 0.25w14craftmine.3-beta：版本号落在第三段
            var segments = apiName.BeforeFirst("-").Split('.');
            if (segments.Length < 2 || !int.TryParse(segments[^1], out var build))
                throw ParseError(url, $"无法解析 NeoForge 版本号：{apiName}");
            return new NeoForgeListEntry
            {
                ApiName = apiName,
                VersionName = apiName,
                Version = new Version(0, 0, build),
                Inherit = segments[1],
                IsBeta = isBeta,
            };
        }

        // 20.4.30-beta；26.1.0.0-alpha.1+snapshot-1
        {
            var versionName = apiName;
            var version = new Version(apiName.BeforeFirst("-"));
            var inherit = version.Major >= 24
                ? $"{version.Major}.{version.Minor}.{version.Build}"
                : $"1.{version.Major}.{version.Minor}";
            inherit = StripTrailingZero(inherit);
            if (versionName.Contains('+')) inherit += "-" + versionName.AfterFirst("+");

            return new NeoForgeListEntry
            {
                ApiName = apiName,
                VersionName = versionName,
                Version = version,
                Inherit = inherit,
                IsBeta = isBeta,
            };
        }
    }

    #endregion

    #region LiteLoader / Fabric

    /// <summary>解析 LiteLoader 的版本清单（官方与镜像结构相同）。</summary>
    public static List<LiteLoaderListEntry> LiteLoaderFromJson(string json, string url)
    {
        var root = ParseJson(json, url) as JsonObject
                   ?? throw ParseError(url, "LiteLoader 版本列表根节点不是对象");
        if (root["versions"] is not JsonObject versionsNode)
            throw ParseError(url, "LiteLoader 版本列表缺少 versions 节点");

        var versions = new List<LiteLoaderListEntry>(versionsNode.Count);
        foreach (var pair in versionsNode)
        {
            // 1.5 / 1.6 的条目结构不同且早已不可用，VB 直接跳过
            if (pair.Key.StartsWith("1.6", StringComparison.Ordinal) ||
                pair.Key.StartsWith("1.5", StringComparison.Ordinal)) continue;

            var container = (pair.Value as JsonObject)?["artefacts"] as JsonObject
                            ?? (pair.Value as JsonObject)?["snapshots"] as JsonObject;
            var realEntry = container?["com.mumfrey:liteloader"]?["latest"] as JsonObject
                            ?? throw ParseError(url, $"LiteLoader 条目缺少 latest 节点：{pair.Key}");

            var inherit = pair.Key;
            var segments = inherit.Split('.');
            var minor = segments.Length >= 2 && int.TryParse(segments[1], out var parsed) ? parsed : 0;

            versions.Add(new LiteLoaderListEntry
            {
                Inherit = inherit,
                IsLegacy = minor < 8,
                IsPreview = string.Equals(realEntry["stream"].AsString(), "snapshot", StringComparison.OrdinalIgnoreCase),
                FileName = "liteloader-installer-" + inherit + (inherit is "1.8" or "1.9" ? ".0" : "") + "-00-SNAPSHOT.jar",
                Md5 = realEntry["md5"].AsString(),
                ReleaseTime = FormatUnixReleaseTime(realEntry["timestamp"], url),
                RawJson = realEntry.ToJsonString(),
            });
        }
        return versions;
    }

    /// <summary>
    /// 校验 Fabric 元数据：必须同时含 game / loader / installer 三段。
    /// 结果由 UI 直接按 JSON 读取，故本方法只做校验、不改写内容。
    /// </summary>
    public static void ValidateFabric(string json, string url)
    {
        var root = ParseJson(json, url) as JsonObject
                   ?? throw ParseError(url, "Fabric 版本列表根节点不是对象");
        if (root["game"] is null || root["loader"] is null || root["installer"] is null)
            throw ParseError(url, "获取到的列表缺乏必要项");
    }

    #endregion

    #region 辅助

    /// <summary>去掉末尾的 <c>.0</c>（VB 的 <c>Inherit</c> 属性 setter 行为）。</summary>
    private static string StripTrailingZero(string value)
        => value.EndsWith(".0", StringComparison.Ordinal) ? value[..^2] : value;

    /// <summary>从 <paramref name="marker"/> 首次出现处截断（VB 的 <c>Substring(IndexOfF(...))</c>）。</summary>
    private static string Slice(string text, string marker)
    {
        var index = text.IndexOf(marker, StringComparison.Ordinal);
        return index < 0 ? text : text[index..];
    }

    /// <summary>把形如「2021-02-15 03:24:02」的 UTC 时间转成本地时区的「yyyy/MM/dd HH:mm」。</summary>
    private static string FormatForgeReleaseTime(string? original, string url)
    {
        if (string.IsNullOrWhiteSpace(original)) throw ParseError(url, "页面中找不到发布时间");

        var parts = original.Split(' ', '-', ':');
        if (parts.Length < 6) throw ParseError(url, $"无法解析发布时间：{original}");

        var date = new DateTime(
            int.Parse(parts[0], CultureInfo.InvariantCulture), int.Parse(parts[1], CultureInfo.InvariantCulture),
            int.Parse(parts[2], CultureInfo.InvariantCulture), int.Parse(parts[3], CultureInfo.InvariantCulture),
            int.Parse(parts[4], CultureInfo.InvariantCulture), int.Parse(parts[5], CultureInfo.InvariantCulture),
            0, DateTimeKind.Utc);
        return date.ToLocalTime().ToString("yyyy'/'MM'/'dd HH':'mm", CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// 把 ISO 8601 时间转成本地时区的「yyyy/MM/dd HH:mm」。
    /// 带偏移量时按偏移量换算；不带偏移量时按 UTC 解释（VB 原实现按本地解释，会让镜像与官方源的时区不一致）。
    /// </summary>
    private static string FormatIsoReleaseTime(JsonNode? node, string url)
    {
        var text = node.AsString();
        if (string.IsNullOrWhiteSpace(text)) throw ParseError(url, "版本项缺少 modified 时间");

        if (DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var offset))
            return offset.ToLocalTime().ToString("yyyy'/'MM'/'dd HH':'mm", CultureInfo.InvariantCulture);

        throw ParseError(url, $"无法解析发布时间：{text}");
    }

    /// <summary>把 Unix 秒时间戳转成本地时区的「yyyy/MM/dd HH:mm」。</summary>
    private static string FormatUnixReleaseTime(JsonNode? node, string url)
    {
        if (node is null) throw ParseError(url, "LiteLoader 条目缺少 timestamp");
        if (!long.TryParse(node.AsString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var seconds))
            throw ParseError(url, $"无法解析 LiteLoader 时间戳：{node.ToJsonString()}");

        return DateTimeOffset.FromUnixTimeSeconds(seconds).LocalDateTime
            .ToString("yyyy'/'MM'/'dd HH':'mm", CultureInfo.InvariantCulture);
    }

    private static JsonNode? ParseJson(string json, string url)
    {
        try
        {
            return JsonNode.Parse(json);
        }
        catch (Exception ex)
        {
            throw ParseError(url, $"响应不是合法 JSON：{ex.Message}", ex);
        }
    }

    private static ResourceRequestException ParseError(string url, string message, Exception? inner = null)
        => new(ResourceRequestErrorKind.Parse, url, message, null, inner);

    #endregion
}
