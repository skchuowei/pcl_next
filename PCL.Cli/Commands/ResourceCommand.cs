using PCL.Download;

namespace PCL.Cli.Commands;

/// <summary>
/// <c>pclcli resource</c>：社区资源联网查询（dev.4 B5 门槛）。
/// <para/>
/// 走 <see cref="ResourceProviderRegistry"/> + <see cref="ResourceSearcher"/>，
/// 不复制 VB 侧任何请求/解析逻辑；CurseForge 缺 Key 时按注册表降级并给 <c>notice</c>，**不算失败**。
/// </para>
/// </summary>
internal static class ResourceCommand
{
    public const string Usage = """
        pclcli resource search --query <关键词> [--source auto|modrinth|curseforge]
                               [--type mod|modpack|resourcepack|shader|datapack|plugin|map]
                               [--game-version 1.21.1] [--loader fabric|forge|neoforge|liteloader]
                               [--page-size 20]
        pclcli resource versions --project <工程ID> [--source ...]
        pclcli resource fingerprint --file <本地文件> [--source ...]
        """;

    public static Task<int> RunAsync(CliSession session)
    {
        var sub = session.Args.Positionals.Count > 0 ? session.Args.Positionals[0].ToLowerInvariant() : "";
        return sub switch
        {
            "search" => RunSearchAsync(session),
            "versions" => RunVersionsAsync(session),
            "fingerprint" or "fp" => RunFingerprintAsync(session),
            "" => throw new CliUsageException("缺少子命令：search / versions / fingerprint"),
            _ => throw new CliUsageException($"未知子命令「{sub}」，可选 search / versions / fingerprint"),
        };
    }

    private static async Task<int> RunSearchAsync(CliSession session)
    {
        var args = session.Args;
        var query = args.Get("query") ?? throw new CliUsageException("缺少 --query");
        var type = ParseType(args.Get("type"));
        var loaders = ParseLoaders(args.Get("loader"));
        var pageSize = args.GetInt("page-size") ?? 20;
        var requested = ParseSource(args.Get("source"));

        using var api = new HttpResourceApi();
        var registry = ResourceProviderRegistry.CreateDefault(api);
        var sources = Resolve(session, registry, requested);

        var storage = new ResourceSearcher.SearchResult();
        var request = new ResourceSearcher.SearchRequest(type, storage, pageSize)
        {
            SearchText = query,
            GameVersion = args.Get("game-version"),
            ModLoaders = loaders,
            Sources = sources,
        };

        try
        {
            await Task.Run(() => ResourceSearcher.Search(request, api), session.Token).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            session.Output.Error("search_failed", ex.Message);
            session.Output.Result(false, ("code", "search_failed"), ("query", query));
            return (int)CliExitCode.Network;
        }

        var items = storage.Results.Take(pageSize).ToList();
        foreach (var project in items) EmitProject(session, project);

        if (storage.ErrorMessage is not null)
            session.Output.Notice(storage.ErrorMessage);

        if (items.Count == 0 && storage.ErrorMessage is not null)
        {
            session.Output.Result(false, ("code", "source_unavailable"), ("query", query), ("message", storage.ErrorMessage));
            return (int)CliExitCode.SourceUnavailable;
        }

        session.Output.Say($"共 {items.Count} 条结果（{sources}）");
        session.Output.Result(true, ("query", query), ("count", items.Count), ("sources", sources.ToString()));
        return (int)CliExitCode.Ok;
    }

    private static async Task<int> RunVersionsAsync(CliSession session)
    {
        var args = session.Args;
        var projectId = args.Require("project", "工程 ID");
        var requested = ParseSource(args.Get("source"));

        using var api = new HttpResourceApi();
        var registry = ResourceProviderRegistry.CreateDefault(api);
        var sources = Resolve(session, registry, requested);

        var platform = FirstPlatform(sources, registry);
        if (platform is null)
        {
            session.Output.Error("source_unavailable", "没有任何可用的资源站");
            session.Output.Result(false, ("code", "source_unavailable"));
            return (int)CliExitCode.SourceUnavailable;
        }

        try
        {
            var versions = await Task.Run(() => registry.Get(platform.Value)!.GetVersions(projectId), session.Token)
                .ConfigureAwait(false);
            foreach (var version in versions)
            {
                session.Output.Item(
                    ("project", projectId),
                    ("id", version.Id),
                    ("name", version.Display),
                    ("version", version.Version),
                    ("releaseType", version.ReleaseType.ToString()),
                    ("releaseDate", version.ReleaseDate.ToString("yyyy-MM-dd")),
                    ("downloads", version.DownloadCount),
                    ("fileName", version.FileName),
                    ("size", version.Size),
                    ("hash", version.Hash),
                    ("gameVersions", version.GameVersions),
                    ("urls", version.DownloadUrls));
            }

            session.Output.Say($"{projectId}：{versions.Count} 个版本（{platform}）");
            session.Output.Result(true, ("project", projectId), ("count", versions.Count), ("source", platform.ToString()));
            return (int)CliExitCode.Ok;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            session.Output.Error("versions_failed", ex.Message);
            session.Output.Result(false, ("code", "versions_failed"), ("project", projectId));
            return (int)CliExitCode.Network;
        }
    }

    private static async Task<int> RunFingerprintAsync(CliSession session)
    {
        var args = session.Args;
        var file = args.Require("file", "待反查的本地文件");
        if (!File.Exists(file)) throw new CliUsageException($"文件不存在：{file}");

        var requested = ParseSource(args.Get("source"));
        using var api = new HttpResourceApi();
        var registry = ResourceProviderRegistry.CreateDefault(api);
        Resolve(session, registry, requested);

        var sha1 = HashHelper.ComputeFileHash(file, 40);
        var curseForge = HashHelper.ComputeCurseForgeFingerprintFile(file);
        session.Output.Say($"指纹计算完成：SHA1={sha1}，CurseForge={curseForge}");

        try
        {
            var matches = await Task.Run(() => ResolveMatches(registry), session.Token).ConfigureAwait(false);
            if (matches.Count == 0)
            {
                session.Output.Notice("两个资源站都没有命中该指纹（文件可能来自其他渠道）");
                session.Output.Result(true, ("file", file), ("sha1", sha1), ("curseForge", curseForge), ("matches", 0));
                return (int)CliExitCode.Ok;
            }

            foreach (var match in matches)
            {
                session.Output.Item(
                    ("file", file),
                    ("projectId", match.ProjectId),
                    ("versionId", match.File.Id),
                    ("display", match.File.Display),
                    ("platform", match.File.ResourceType.ToString()),
                    ("loaders", match.Loaders));
            }

            session.Output.Result(true,
                ("file", file), ("sha1", sha1), ("curseForge", curseForge), ("matches", matches.Count));
            return (int)CliExitCode.Ok;

            IReadOnlyList<ResourceFingerprintMatch> ResolveMatches(ResourceProviderRegistry reg)
            {
                var fingerprint = new ResourceFingerprint(sha1, curseForge);
                var all = new List<ResourceFingerprintMatch>();
                foreach (var provider in reg.Available)
                {
                    try
                    {
                        all.AddRange(provider.LookupFingerprints([fingerprint]));
                    }
                    catch (ResourceProviderUnavailableException)
                    {
                        // 不可用已在 Resolve 阶段提示过，这里静默跳过
                    }
                }
                return all;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            session.Output.Error("fingerprint_failed", ex.Message);
            session.Output.Result(false, ("code", "fingerprint_failed"), ("file", file));
            return (int)CliExitCode.Network;
        }
    }

    // ── 公共小工具 ───────────────────────────────────────────────────────────

    /// <summary>把期望的源收敛成实际可用的源，并把降级提示写成 notice。</summary>
    private static ResourcePlatforms Resolve(CliSession session, ResourceProviderRegistry registry, ResourcePlatforms requested)
    {
        var resolved = registry.ResolveSources(requested, out var notice);
        if (notice is not null)
        {
            session.Output.Notice(notice);
            session.Output.Say("提示：" + notice);
        }
        return resolved;
    }

    private static ResourcePlatforms? FirstPlatform(ResourcePlatforms sources, ResourceProviderRegistry registry)
    {
        var match = registry.Available.FirstOrDefault(p => (sources & p.Platform) != 0);
        if (match is not null) return match.Platform;
        return registry.Available.Count > 0 ? registry.Available[0].Platform : null;
    }

    private static void EmitProject(CliSession session, ResourceProject project)
    {
        var description = project.Description.Replace("\r", " ").Replace("\n", " ").Trim();
        if (description.Length > 160) description = description[..160] + "…";

        session.Output.Item(
            ("id", project.Id),
            ("slug", project.Slug),
            ("name", project.TranslatedName),
            ("platform", project.Platform.ToString()),
            ("types", project.Types.ToString()),
            ("downloads", project.DownloadCount),
            ("lastUpdate", project.LastUpdate.ToString("yyyy-MM-dd")),
            ("loaders", project.ModLoaders.ToString()),
            ("description", description),
            ("website", project.Website));
    }

    private static ResourcePlatforms ParseSource(string? raw) => raw?.Trim().ToLowerInvariant() switch
    {
        null or "" or "auto" => ResourcePlatforms.Any,
        "modrinth" or "mr" => ResourcePlatforms.Modrinth,
        "curseforge" or "cf" => ResourcePlatforms.CurseForge,
        _ => throw new CliUsageException($"--source 只能是 auto / modrinth / curseforge，实际为「{raw}」"),
    };

    private static ResourceTypes ParseType(string? raw) => raw?.Trim().ToLowerInvariant() switch
    {
        null or "" or "any" => ResourceTypes.Any,
        "mod" => ResourceTypes.Mod,
        "modpack" or "整合包" => ResourceTypes.ModPack,
        "resourcepack" or "资源包" => ResourceTypes.ResourcePack,
        "shader" or "光影" => ResourceTypes.Shader,
        "datapack" or "数据包" => ResourceTypes.DataPack,
        "plugin" or "插件" => ResourceTypes.Plugin,
        "map" or "地图" => ResourceTypes.Map,
        _ => throw new CliUsageException($"--type 不支持「{raw}」"),
    };

    private static ModLoaders ParseLoaders(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return ModLoaders.None;

        var result = ModLoaders.None;
        foreach (var part in raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            result |= part.ToLowerInvariant() switch
            {
                "forge" => ModLoaders.Forge,
                "fabric" => ModLoaders.Fabric,
                "neoforge" => ModLoaders.NeoForge,
                "liteloader" => ModLoaders.LiteLoader,
                "all" or "any" => ModLoaders.All,
                _ => throw new CliUsageException($"--loader 不支持「{part}」"),
            };
        }
        return result;
    }
}
