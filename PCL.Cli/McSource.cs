namespace PCL.Cli;

/// <summary>
/// Mojang 下载地址的镜像候选生成，与 VB <c>ModDownload</c> 的
/// <c>DlSourceAssetsGet</c> / <c>DlSourceLibraryGet</c> / <c>DlSourceLauncherOrMetaGet</c>
/// **替换规则逐条对齐**（含 BMCLAPI 的 assets / maven / libraries 三个前缀）。
/// <para/>
/// 这里只生成「下载源候选列表」，不做任何下载动作——真正的失败切换由
/// <c>IDownloadEngine</c> 负责（计划 §5.3：CLI 禁止自写下载循环）。
/// </para>
/// </summary>
internal enum McMirrorPreference
{
    /// <summary>官方优先，镜像兜底（默认）。</summary>
    Auto,
    /// <summary>仅官方源。</summary>
    Official,
    /// <summary>仅镜像源（BMCLAPI）。</summary>
    Mirror,
}

internal static class McSource
{
    private const string Bmclapi = "https://bmclapi2.bangbang93.com";

    /// <summary>解析 <c>--mirror</c> 取值。</summary>
    public static McMirrorPreference ParsePreference(string? raw) => raw?.Trim().ToLowerInvariant() switch
    {
        null or "" or "auto" => McMirrorPreference.Auto,
        "official" or "mojang" => McMirrorPreference.Official,
        "mirror" or "bmclapi" => McMirrorPreference.Mirror,
        _ => throw new CliUsageException($"--mirror 只能是 auto / official / mirror，实际为「{raw}」"),
    };

    /// <summary>加载器第三方 maven 根（Fabric / Forge / NeoForge）的镜像替换前缀。</summary>
    private static readonly (string Original, string Mirror)[] ThirdPartyMavenRoots =
    [
        ("https://maven.fabricmc.net", Bmclapi + "/maven"),
        ("https://maven.minecraftforge.net", Bmclapi + "/maven"),
        ("https://maven.neoforged.net/releases", Bmclapi + "/maven"),
        ("https://maven.neoforged.net", Bmclapi + "/maven"),
        ("https://repo.spongepowered.org/repository/maven-public", Bmclapi + "/maven"),
    ];

    /// <summary>
    /// 加载器依赖库（第三方 maven 坐标）。Fabric/Forge/Neo 官方 maven 在受限网络下不可达，
    /// 镜像优先（/maven 与 /libraries 双前缀），官方直链仅在 Auto 模式兜底。
    /// </summary>
    public static IReadOnlyList<string> ForLoaderLibrary(MavenCoord coord, string repositoryRoot, string? sourceUrl, McMirrorPreference preference)
    {
        var original = string.IsNullOrWhiteSpace(sourceUrl) ? coord.ToUrl(repositoryRoot) : sourceUrl!;
        var mirrorMaven = Bmclapi + "/maven/" + coord.RelativePath;
        var mirrorLibs = Bmclapi + "/libraries/" + coord.RelativePath;

        return preference switch
        {
            McMirrorPreference.Official => [original],
            McMirrorPreference.Mirror => [mirrorMaven, mirrorLibs],
            _ => new[] { mirrorMaven, mirrorLibs, original }.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
        };
    }

    /// <summary>加载器 installer jar 候选：镜像优先（/maven、/libraries 双前缀），官方 maven 直链兜底。</summary>
    public static IReadOnlyList<string> ForLoaderInstaller(string installerUrl, McMirrorPreference preference)
    {
        var rel = new Uri(installerUrl).AbsolutePath.TrimStart('/');
        var mirrorMaven = Bmclapi + "/maven/" + rel;
        var mirrorLibs = Bmclapi + "/libraries/" + rel;

        return preference switch
        {
            McMirrorPreference.Official => [installerUrl],
            McMirrorPreference.Mirror => [mirrorMaven, mirrorLibs],
            _ => new[] { mirrorMaven, mirrorLibs, installerUrl }.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
        };
    }

    /// <summary>Fabric meta 接口候选（官方 meta.fabricmc.net / BMCLAPI fabric-meta 镜像）。</summary>
    public static IReadOnlyList<string> ForFabricMeta(string path, McMirrorPreference preference)
    {
        path = path.StartsWith('/') ? path : "/" + path;
        return Order(
            "https://meta.fabricmc.net" + path,
            Bmclapi + "/fabric-meta" + path,
            preference);
    }

    /// <summary>资源对象（assets/objects）地址。</summary>
    public static IReadOnlyList<string> ForAsset(string original, McMirrorPreference preference)
    {
        original = original.Replace("http://resources.download.minecraft.net", "https://resources.download.minecraft.net");
        var mirror = original
            .Replace("https://piston-data.mojang.com", Bmclapi + "/assets")
            .Replace("https://piston-meta.mojang.com", Bmclapi + "/assets")
            .Replace("https://resources.download.minecraft.net", Bmclapi + "/assets");
        return Order(original, mirror, preference);
    }

    /// <summary>依赖库（libraries）地址。</summary>
    public static IReadOnlyList<string> ForLibrary(string original, McMirrorPreference preference)
    {
        // 第三方 maven（forge / fabric / neoforge）不使用官方源，直接走两个镜像前缀
        if (new[] { "minecraftforge", "fabricmc", "neoforged" }.Any(k => original.Contains(k, StringComparison.OrdinalIgnoreCase)))
        {
            var maven = original
                .Replace("https://piston-data.mojang.com", Bmclapi + "/maven")
                .Replace("https://piston-meta.mojang.com", Bmclapi + "/maven")
                .Replace("https://libraries.minecraft.net", Bmclapi + "/maven");
            var libs = original
                .Replace("https://piston-data.mojang.com", Bmclapi + "/libraries")
                .Replace("https://piston-meta.mojang.com", Bmclapi + "/libraries")
                .Replace("https://libraries.minecraft.net", Bmclapi + "/libraries");

            return preference == McMirrorPreference.Official
                ? [original]
                : new[] { maven, libs }.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }

        var mirrorMaven = original
            .Replace("https://piston-data.mojang.com", Bmclapi + "/maven")
            .Replace("https://piston-meta.mojang.com", Bmclapi + "/maven")
            .Replace("https://libraries.minecraft.net", Bmclapi + "/maven");
        var mirrorLibs = original
            .Replace("https://piston-data.mojang.com", Bmclapi + "/libraries")
            .Replace("https://piston-meta.mojang.com", Bmclapi + "/libraries")
            .Replace("https://libraries.minecraft.net", Bmclapi + "/libraries");

        return preference switch
        {
            McMirrorPreference.Official => [original],
            McMirrorPreference.Mirror => new[] { mirrorMaven, mirrorLibs }.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            _ => new[] { original, mirrorMaven, mirrorLibs }.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
        };
    }

    /// <summary>版本 JSON（launcher / meta）地址。</summary>
    public static IReadOnlyList<string> ForMeta(string original, McMirrorPreference preference)
    {
        if (string.IsNullOrWhiteSpace(original))
            throw new CliUsageException("版本 JSON 缺少下载地址");

        var mirror = original
            .Replace("https://piston-data.mojang.com", Bmclapi)
            .Replace("https://piston-meta.mojang.com", Bmclapi)
            .Replace("https://launcher.mojang.com", Bmclapi)
            .Replace("https://launchermeta.mojang.com", Bmclapi);
        return Order(original, mirror, preference);
    }

    private static IReadOnlyList<string> Order(string official, string mirror, McMirrorPreference preference)
        => preference switch
        {
            McMirrorPreference.Official => [official],
            McMirrorPreference.Mirror => new[] { mirror }.Where(m => m != official).DefaultIfEmpty(mirror).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            _ => new[] { official, mirror }.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
        };
}
