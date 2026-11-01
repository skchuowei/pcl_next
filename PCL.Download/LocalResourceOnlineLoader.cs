using System.Text.Json.Nodes;

namespace PCL.Download;

/// <summary>
/// 在线反查的上下文：目标 MC 版本、实例的加载器组合、缓存时效。
/// </summary>
public sealed class OnlineLoadContext
{
    /// <summary>目标 MC 版本的原版版本名（如 <c>1.20.1</c>）。</summary>
    public string GameVersion { get; init; } = "";

    /// <summary>实例的加载器组合（影响更新候选的筛选）。</summary>
    public ModLoaders Loaders { get; init; } = ModLoaders.None;

    /// <summary>缓存有效期。VB 为 6 小时。</summary>
    public TimeSpan CacheExpiry { get; init; } = TimeSpan.FromHours(6);
}

/// <summary>
/// 在线反查的结果统计。
/// </summary>
public sealed class OnlineLoadReport
{
    /// <summary>本次需要联网的文件数（缓存新鲜的不算）。</summary>
    public int Pending { get; init; }

    /// <summary>成功带上工程信息（<see cref="LocalResourceFile.Project"/> 非空）的文件数。</summary>
    public int Resolved { get; init; }

    /// <summary>检测到可更新的文件数。</summary>
    public int UpdatesFound { get; init; }

    /// <summary>CurseForge 分支是否实际执行。</summary>
    public bool CurseForgeUsed { get; init; }

    /// <summary>降级提示（CurseForge 未配置 Key 时非空），可直接展示给用户。</summary>
    public string? Notice { get; init; }

    /// <summary>是否有站点请求失败（失败时不写 OnlineDataLoaded 标记，与 VB 一致）。</summary>
    public bool Failed { get; init; }
}

/// <summary>
/// 本地资源文件的在线反查与更新检测（平移自 VB <c>LocalResourceLoaders.LocalResourceOnlineLoad</c>）。
/// <para/>
/// 流程与 VB 一致：① 按「指纹 + 原版版本 + 加载器」查缓存，6 小时内且已成功加载的跳过；
/// ② 从 Modrinth / CurseForge 反查工程与本地文件对应的版本（<see cref="ResourceFingerprint"/>）；
/// ③ 取工程信息回填；④ 查询更新并合并下载源与更新日志；⑤ 回写缓存。
/// </para>
/// <para/>
/// 与 VB 的差异（均不影响结果）：
/// <list type="bullet">
/// <item>两个站点<b>串行</b>执行而非各起线程，保证结果确定、便于测试；</item>
/// <item>缓存只回写到传入的 <see cref="JsonObject"/>，落盘由宿主负责（VB 直接写 <c>Cache\LocalMod.json</c>）；</item>
/// <item>不使用 <c>LoaderTask</c> 状态机与 UI 刷新，改用 <see cref="CancellationToken"/>。</item>
/// </list>
/// </para>
/// </summary>
public sealed class LocalResourceOnlineLoader
{
    private readonly ResourceProviderRegistry _providers;

    /// <summary>构造加载器。</summary>
    /// <param name="providers">Provider 注册表；null 时按默认配置构造（CurseForge 的 Key 取自环境变量）。</param>
    public LocalResourceOnlineLoader(ResourceProviderRegistry? providers = null)
        => _providers = providers ?? ResourceProviderRegistry.CreateDefault();

    /// <summary>
    /// 执行在线反查与更新检测。会就地修改传入的 <paramref name="modList"/> 并回写 <paramref name="cache"/>。
    /// </summary>
    /// <param name="modList">本地 Mod 列表（通常来自 <see cref="LocalResourceScanner"/>）。</param>
    /// <param name="cache">缓存对象（键为「指纹 + 原版版本 + 加载器」）。</param>
    /// <param name="context">目标版本与加载器。</param>
    /// <param name="ct">取消令牌。</param>
    public OnlineLoadReport Load(IReadOnlyList<LocalResourceFile> modList, JsonObject cache,
        OnlineLoadContext context, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(modList);
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentException.ThrowIfNullOrWhiteSpace(context.GameVersion);

        // ① 读缓存，挑出需要联网的条目
        var pending = new List<LocalResourceFile>();
        foreach (var file in modList)
        {
            ct.ThrowIfCancellationRequested();
            if (cache[CacheKey(file, context)] is JsonObject cached)
            {
                file.FromJson(cached);
                if (file.OnlineDataLoaded && IsCacheFresh(cached, context.CacheExpiry)) continue;
            }
            pending.Add(file);
        }

        var sources = _providers.ResolveSources(ResourcePlatforms.Any, out var notice);
        DownloadLog.Info($"本地资源在线反查：{pending.Count} 个文件需要联网（共 {modList.Count} 个），" +
                         $"可用源 {sources}" + (notice is null ? "" : $"（{notice}）"));

        if (pending.Count == 0)
            return new OnlineLoadReport { Pending = 0, Notice = notice };

        var failed = false;
        var curseForgeUsed = false;

        // ②③④ 从两个站点反查（串行，见类注释）
        if (sources.HasFlagF(ResourcePlatforms.Modrinth) &&
            _providers.Get(ResourcePlatforms.Modrinth) is ModrinthProvider modrinth)
        {
            try
            {
                LoadFromModrinth(modrinth, pending, context, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                DownloadLog.Warn(ex, "从 Modrinth 获取本地 Mod 信息失败");
                failed = true;
            }
        }

        if (sources.HasFlagF(ResourcePlatforms.CurseForge) &&
            _providers.Get(ResourcePlatforms.CurseForge) is CurseForgeProvider curseForge)
        {
            curseForgeUsed = true;
            try
            {
                LoadFromCurseForge(curseForge, pending, context, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                DownloadLog.Warn(ex, "从 CurseForge 获取本地 Mod 信息失败");
                failed = true;
            }
        }

        // ⑤ 回写缓存（只缓存拿到工程信息的条目，与 VB 一致）
        var resolved = pending.Where(m => m.Project is not null).ToList();
        foreach (var entry in resolved)
        {
            entry.OnlineDataLoaded = !failed;
            cache[CacheKey(entry, context)] = entry.ToJson();
        }

        var updates = resolved.Count(m => m.UpdateFile is not null);
        DownloadLog.Info($"本地资源在线反查完成：{resolved.Count} 个文件已有工程信息，{updates} 个可更新" +
                         (failed ? "（存在请求失败，本次不写缓存标记）" : ""));

        return new OnlineLoadReport
        {
            Pending = pending.Count,
            Resolved = resolved.Count,
            UpdatesFound = updates,
            CurseForgeUsed = curseForgeUsed,
            Notice = notice,
            Failed = failed,
        };
    }

    /// <summary>缓存键：指纹 + 原版版本 + 加载器（与 VB 逐字一致）。</summary>
    public static string CacheKey(LocalResourceFile file, OnlineLoadContext context)
        => $"{file.ModrinthHash}{context.GameVersion}{context.Loaders}";

    private static bool IsCacheFresh(JsonObject cached, TimeSpan expiry)
        => cached["Project"] is JsonObject project && project["CacheTime"] is not null
           && DateTime.Now - project["CacheTime"].AsDate() < expiry;

    private static void LoadFromModrinth(ModrinthProvider provider, IReadOnlyList<LocalResourceFile> pending,
        OnlineLoadContext context, CancellationToken ct)
    {
        // 步骤 1：指纹 → 工程
        var fingerprints = pending.Select(f => new ResourceFingerprint(f.ModrinthHash, null)).ToList();
        var matches = provider.LookupFingerprints(fingerprints);
        if (matches.Count == 0) return;

        var byHash = pending
            .GroupBy(f => f.ModrinthHash, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        var projectFiles = new Dictionary<string, List<LocalResourceFile>>(StringComparer.Ordinal);
        var loaders = new Dictionary<LocalResourceFile, List<string>>();

        foreach (var match in matches)
        {
            ct.ThrowIfCancellationRequested();
            if (match.Fingerprint.ModrinthSha1 is null) continue;
            if (!byHash.TryGetValue(match.Fingerprint.ModrinthSha1, out var entry)) continue;

            if (!projectFiles.TryGetValue(match.ProjectId, out var list))
                projectFiles[match.ProjectId] = list = [];
            list.Add(entry);
            loaders[entry] = match.Loaders.ToList();

            // 本地版本信息：取较新的一份；否则只更新版本号（VB 的原样逻辑）
            if (entry.ProjectVersion is null || entry.ProjectVersion.ReleaseDate < match.File.ReleaseDate)
                entry.ProjectVersion = match.File;
            else
                entry.ProjectVersion.Version = match.File.Version;
        }

        if (projectFiles.Count == 0) return;

        // 步骤 2：工程信息
        foreach (var project in provider.GetProjects(projectFiles.Keys.ToList()))
        {
            if (!projectFiles.TryGetValue(project.Id, out var entries)) continue;
            foreach (var entry in entries) entry.Project = project;
        }

        // 步骤 3：更新信息
        var toUpdate = projectFiles.Values.SelectMany(l => l).Where(e => e.ProjectVersion is not null).ToList();
        if (toUpdate.Count == 0) return;

        var updates = provider.LookupUpdates(
            toUpdate.Select(e => (e.ModrinthHash, (IReadOnlyList<string>)loaders[e])), context.GameVersion);

        foreach (var entry in toUpdate)
        {
            ct.ThrowIfCancellationRequested();
            if (!updates.TryGetValue(entry.ModrinthHash, out var update)) continue;
            if (entry.ProjectVersion is null || !update.File.DownloadAvailable) continue;
            // 更新版本的加载器必须与本地的完全一致（VB 的 SequenceEqual 判定）
            if (!update.Loaders.SequenceEqual(loaders[entry])) continue;
            if (entry.ProjectVersion.ReleaseDate >= update.File.ReleaseDate) continue;
            if (entry.ProjectVersion.Hash == update.File.Hash) continue;

            var changelog = $"https://modrinth.com/mod/{update.ProjectId}/changelog?g={context.GameVersion}";
            if (entry.UpdateFile is not null && update.File.Hash == entry.UpdateFile.Hash)
            {
                // 同一文件的另一条记录：合并下载源，优先使用 Modrinth 的文件
                entry.ChangelogUrls.Add(changelog);
                if (update.File.DownloadUrls is not null)
                    update.File.DownloadUrls.AddRange(entry.UpdateFile.DownloadUrls ?? []);
                entry.UpdateFile = update.File;
            }
            else if (entry.UpdateFile is null || update.File.ReleaseDate >= entry.UpdateFile.ReleaseDate)
            {
                entry.ChangelogUrls = [changelog];
                entry.UpdateFile = update.File;
            }
        }
    }

    private static void LoadFromCurseForge(CurseForgeProvider provider, IReadOnlyList<LocalResourceFile> pending,
        OnlineLoadContext context, CancellationToken ct)
    {
        // 步骤 1：指纹 → 工程
        var fingerprints = pending.Select(f => new ResourceFingerprint(null, f.CurseForgeHash)).ToList();
        var matches = provider.LookupFingerprints(fingerprints);
        if (matches.Count == 0) return;

        var byHash = pending.GroupBy(f => f.CurseForgeHash).ToDictionary(g => g.Key, g => g.First());
        var projectFiles = new Dictionary<string, List<LocalResourceFile>>(StringComparer.Ordinal);
        var fileLoaders = new Dictionary<LocalResourceFile, ModLoaders>();

        foreach (var match in matches)
        {
            ct.ThrowIfCancellationRequested();
            if (match.Fingerprint.CurseForge is null) continue;
            if (!byHash.TryGetValue(match.Fingerprint.CurseForge.Value, out var entry)) continue;

            if (!projectFiles.TryGetValue(match.ProjectId, out var list))
                projectFiles[match.ProjectId] = list = [];
            list.Add(entry);
            fileLoaders[entry] = match.File.ModLoaders;

            if (entry.ProjectVersion is null || entry.ProjectVersion.ReleaseDate < match.File.ReleaseDate)
                entry.ProjectVersion = match.File;
        }

        if (projectFiles.Count == 0) return;

        // 步骤 2：工程信息 + 按「加载器 + MC 版本」挑候选文件
        var bundles = provider.GetProjectBundles(projectFiles.Keys.ToList());
        var updateFileIds = new Dictionary<int, List<LocalResourceFile>>();
        var fileIdToSlug = new Dictionary<int, string>();

        foreach (var bundle in bundles)
        {
            if (!projectFiles.TryGetValue(bundle.Project.Id, out var localFiles)) continue;
            foreach (var localFile in localFiles)
            {
                ct.ThrowIfCancellationRequested();
                // 若该文件已在 Modrinth 找到工程，保留 Modrinth 的信息（VB 同样优先 Modrinth）
                if (localFile.Project is null || localFile.Project.Platform != ResourcePlatforms.Modrinth)
                    localFile.Project = bundle.Project;

                foreach (var index in bundle.LatestFiles)
                {
                    if (!IsLoaderMatch(fileLoaders[localFile], index.Loader)) continue;
                    if (!string.Equals(index.GameVersion, context.GameVersion, StringComparison.Ordinal)) continue;
                    if (!updateFileIds.TryGetValue(index.FileId, out var targets))
                        updateFileIds[index.FileId] = targets = [];
                    targets.Add(localFile);
                    fileIdToSlug[index.FileId] = bundle.Project.Slug;
                }
            }
        }

        if (updateFileIds.Count == 0) return;

        // 步骤 3：取候选文件详情，挑每个本地文件的最新版本
        var files = provider.GetFiles(updateFileIds.Keys.ToList());
        var best = new Dictionary<LocalResourceFile, ResourceVersion>();
        foreach (var file in files)
        {
            if (!file.DownloadAvailable) continue;
            if (!int.TryParse(file.Id, System.Globalization.NumberStyles.Integer,
                    System.Globalization.CultureInfo.InvariantCulture, out var fileId)) continue;
            if (!updateFileIds.TryGetValue(fileId, out var targets)) continue;

            foreach (var entry in targets)
            {
                if (file.ModLoaders != fileLoaders[entry])
                {
                    DownloadLog.Warn($"从 CurseForge 获取到的更新文件 {file.FileName} 加载器为 {file.ModLoaders}，" +
                                     $"与本地文件 {entry.File.Name} 的加载器 {fileLoaders[entry]} 不一致，已忽略");
                    continue;
                }
                if (best.TryGetValue(entry, out var existing) && existing.ReleaseDate >= file.ReleaseDate) continue;
                best[entry] = file;
            }
        }

        // 步骤 4：判定更新
        foreach (var (entry, updateFile) in best)
        {
            ct.ThrowIfCancellationRequested();
            if (entry.ProjectVersion is null) continue;
            if (entry.ProjectVersion.ReleaseDate >= updateFile.ReleaseDate || entry.ProjectVersion.Hash == updateFile.Hash)
                continue;

            var fileId = updateFile.Id;
            var changelog = fileIdToSlug.TryGetValue(int.TryParse(fileId, out var id) ? id : 0, out var slug)
                ? $"https://www.curseforge.com/minecraft/mc-mods/{slug}/files/{fileId}"
                : $"https://www.curseforge.com/minecraft/mc-mods/{entry.Project?.Slug}/files/{fileId}";

            if (entry.UpdateFile is not null && updateFile.Hash == entry.UpdateFile.Hash)
            {
                entry.ChangelogUrls.Add(changelog);
                if (updateFile.DownloadUrls is not null)
                    updateFile.DownloadUrls.AddRange(entry.UpdateFile.DownloadUrls ?? []);
            }
            else if (entry.UpdateFile is null || updateFile.ReleaseDate > entry.UpdateFile.ReleaseDate)
            {
                entry.ChangelogUrls = [changelog];
                entry.UpdateFile = updateFile;
            }
        }
    }

    /// <summary>
    /// 加载器匹配（对齐 VB）：本地文件没有加载器信息时只接受同样没有加载器信息的候选，否则要求包含。
    /// </summary>
    private static bool IsLoaderMatch(ModLoaders local, ModLoaders candidate)
        => local == ModLoaders.None ? candidate == ModLoaders.None : local.HasFlagF(candidate);
}
