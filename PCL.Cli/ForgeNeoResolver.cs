using System.IO.Compression;
using System.Text.Json.Nodes;
using PCL.Download;

namespace PCL.Cli;

/// <summary>
/// Forge / NeoForge installer 解析：下载 installer jar → 解压 → 读内嵌 <c>version.json</c>
/// （Neo 为 <c>version.json</c> / <c>install_profile.json</c>）→ 展开依赖库。
/// <para/>
/// 注意：installer 内 <c>processors</c> 的二进制补丁 / jar 重命名需在安装期由 Java 工具链执行，
/// 本下载链路不跑 Java 安装器；此处分发的是库与 profile，processor 产物由运行环境补齐。
/// </summary>
internal static class ForgeNeoResolver
{
    public static async Task<LoaderProfile> ResolveAsync(
        CliSession session, IDownloadEngine engine, string mcVersion, string? loaderVersion,
        string instanceDir, string kind)
    {
        if (string.IsNullOrWhiteSpace(loaderVersion))
            throw new CliUsageException($"{kind} 需用 --loader-version 指定版本号（暂不自动解析 maven metadata）");

        var installerUrl = BuildInstallerUrl(kind, mcVersion, loaderVersion);
        var installerDir = Path.Combine(session.WorkDir, "installer", kind);
        Directory.CreateDirectory(installerDir);
        var installerPath = Path.Combine(installerDir, Path.GetFileName(installerUrl));

        session.Output.Say($"下载 {kind} installer：{installerUrl}");
        // installer 有 8~10MB，此前不带进度回调：CLI 在这段时间一个 progress 事件都不发，
        // 外部看到的就是「卡住不说话」（真机 Forge 安装前 70 秒、NeoForge 前 15 秒全耗在这里）。
        await engine.DownloadAsync(
            [
                new DownloadFileRequest
                {
                    Sources = McSource.ForLoaderInstaller(installerUrl, session.Mirror),
                    LocalPath = installerPath,
                    Check = null,
                    SimulateBrowserHeaders = true,
                },
            ],
            session.CreateOptions(), session.CreateProgress($"{kind} installer"), session.Token).ConfigureAwait(false);
        session.Output.EndProgressLine();

        var extractDir = Path.Combine(installerDir, "extracted");
        if (Directory.Exists(extractDir)) Directory.Delete(extractDir, true);
        ZipFile.ExtractToDirectory(installerPath, extractDir);

        var profileJsonPath = FindProfileJson(extractDir);
        var root = JsonNode.Parse(await File.ReadAllTextAsync(profileJsonPath, session.Token).ConfigureAwait(false))
            ?? throw new CliUsageException($"{kind} installer 内嵌 profile 解析失败");

        return ParseProfile(root, kind, mcVersion);
    }

    /// <summary>构造 installer jar 官方直链（镜像由 <see cref="McSource.ForLoaderInstaller"/> 替换）。</summary>
    private static string BuildInstallerUrl(string kind, string mcVersion, string loaderVersion) => kind switch
    {
        "forge" => $"https://maven.minecraftforge.net/net/minecraftforge/forge/{loaderVersion}/forge-{loaderVersion}-installer.jar",
        "neoforge" => $"https://maven.neoforged.net/releases/net/neoforged/neoforge/{loaderVersion}/neoforge-{loaderVersion}-installer.jar",
        _ => throw new CliUsageException("未知加载器类型：" + kind),
    };

    private static string FindProfileJson(string extractDir)
    {
        foreach (var name in new[] { "version.json", "install_profile.json", "profile.json" })
        {
            var candidate = Path.Combine(extractDir, name);
            if (File.Exists(candidate)) return candidate;
        }
        throw new CliUsageException("installer 内未找到 version.json / install_profile.json");
    }

    private static LoaderProfile ParseProfile(JsonNode root, string kind, string mcVersion)
    {
        var obj = root.AsObject();
        var id = obj["id"]?.GetValue<string>() ?? $"{kind}-{mcVersion}";
        var mainClass = obj["mainClass"]?.GetValue<string>() ?? throw new CliUsageException("profile 缺少 mainClass");
        var inherits = obj["inheritsFrom"]?.GetValue<string>() ?? mcVersion;
        var defaultRoot = kind == "forge"
            ? "https://maven.minecraftforge.net/"
            : "https://maven.neoforged.net/releases/";

        var libraries = new List<LoaderLibrary>();
        if (obj["libraries"] is JsonArray libs)
        {
            foreach (var node in libs)
            {
                if (node is not JsonObject lib) continue;
                var name = lib["name"]?.GetValue<string>();
                if (string.IsNullOrWhiteSpace(name)) continue;

                var artifact = lib["downloads"]?["artifact"];
                var url = artifact?["url"]?.GetValue<string>();
                var sha1 = artifact?["sha1"]?.GetValue<string>();
                var size = artifact?["size"] is JsonValue s && s.TryGetValue<long>(out var sz) ? sz : -1;
                // url 为空 = 该产物要由安装期 processor 现场生成（Forge 1.21.4 的 -client.jar，
                // install_profile 只给了 sha1/size、url 是空串）。按「有 artifact 条目」判会把它当普通库，
                // 再按坐标拼出 /forge-<ver>-client.jar 去下载 → 官方与镜像全 404，整单安装失败。
                var hasArtifact = artifact is not null && !string.IsNullOrWhiteSpace(url);
                var repositoryRoot = string.IsNullOrWhiteSpace(url) ? defaultRoot : AuthorityRootOf(url!);

                libraries.Add(new LoaderLibrary(name, repositoryRoot, sha1, size, url)
                {
                    // 无 artifact 条目（如 Forge -client.jar）是安装期 processor 产物，下载层不拉取
                    IsProcessorArtifact = !hasArtifact,
                });
            }
        }

        return new LoaderProfile
        {
            Id = id,
            MainClass = mainClass,
            InheritsFrom = inherits,
            Libraries = libraries,
            Arguments = obj["arguments"]?.DeepClone(),
            LoaderKind = kind,
        };
    }

    /// <summary>只取仓库权威根（scheme+host[:port]），group 层级由坐标相对路径补齐，避免 root 与相对路径重复。</summary>
    private static string AuthorityRootOf(string artifactUrl)
    {
        var uri = new Uri(artifactUrl);
        return uri.GetLeftPart(UriPartial.Authority) + "/";
    }
}
