using System.IO;
using PCL.Download.Platform;

namespace PCL.Download;

/// <summary>
/// 本地 Mod / 资源文件的扫描与归类（平移自 pcl-next <c>Modules/Resource/LocalResourceLoaders.vb</c>
/// 的 <c>LocalResourceFileLoad</c> 纯逻辑部分）。
///
/// 与 VB 一致地保留：
/// - 递归枚举后在「非顶层」子目录下的文件，仅当满足 Forge 1.13- 版本号子目录条件时才收录；
/// - 按 <see cref="LocalResourceFile.EnabledName"/> 去重：同名启用/禁用两份只保留启用的一份，另一份告警并丢弃；
/// - 最终按 <see cref="LocalResourceFile.File"/>.Name 升序。
///
/// 已从核心逻辑剥离（依赖 VB 运行时，交由宿主在 PCLCS 侧承接）：
/// - 等待 Mod 更新完成、UI 进度、<c>RunInUiWait</c> 等交互；
/// - 本地缓存 <c>Cache\LocalMod.json</c> 的读写与 6 小时时效；
/// - 联网获取（<c>LocalResourceOnlineLoad</c>），该部分属于 Phase 3.2 的 <c>IResourceProvider</c> 抽象。
/// </summary>
public static class LocalResourceScanner
{
    /// <summary>
    /// 扫描选项。
    /// </summary>
    public sealed class Options
    {
        /// <summary>
        /// 对于「非顶层」子目录，传入其紧邻 mod 文件夹的文件夹名，返回 true 表示允许加载该子目录下的 Mod。
        /// 对应 VB 的 Forge 1.13- 且子文件夹名等于版本号时的特殊处理。
        /// 为 null 时，所有非顶层子目录文件一律不收录（仅扫描顶层）。
        /// </summary>
        public Func<string, bool>? IsLegacyForgeSubfolder { get; init; }
    }

    /// <summary>
    /// 扫描指定 mod 文件夹，返回去重并排序后的本地资源文件列表。
    /// 文件夹不存在时返回空列表（与 VB 的 DirectoryUtils.Exists 守卫一致）。
    /// </summary>
    public static List<LocalResourceFile> Scan(string modFolder, Options? options = null)
    {
        var result = new List<LocalResourceFile>();
        if (!Directory.Exists(modFolder)) return result;

        var normalizedRoot = modFolder.TrimEnd('\\', '/');
        foreach (var file in Directory.GetFiles(modFolder, "*", SearchOption.AllDirectories))
        {
            var dir = Path.GetDirectoryName(file)!.TrimEnd('\\', '/');
            if (!PathComparison.PathsEqual(dir, normalizedRoot))
            {
                // 非顶层：仅当 Legacy Forge 子目录条件满足时才可能收录
                if (options?.IsLegacyForgeSubfolder is null) continue;
                var subfolder = Path.GetFileName(dir); // 紧邻 mod 文件夹的文件夹名
                if (!options.IsLegacyForgeSubfolder(subfolder)) continue;
            }

            if (!IsModFile(file)) continue;

            var entry = new LocalResourceFile(file);
            var dump = result.FirstOrDefault(m => m.EnabledName == entry.EnabledName); // 同名启用/禁用两份
            if (dump is not null)
            {
                var disabled = dump.IsEnabled ? entry : dump; // 保留启用的一份
                DownloadLog.Warn($"重复的 Mod 文件：{dump.File.Name} 与 {entry.File.Name}，已忽略 {disabled.File.Name}");
                if (ReferenceEquals(disabled, entry)) continue; // 新的是被忽略的禁用份
                result.Remove(dump); // 移除已加入的禁用份，稍后加入启用的新份
            }
            result.Add(entry);
        }

        DownloadLog.Info($"共发现 {result.Count} 个 Mod");
        return result.OrderBy(m => m.File.Name).ToList();
    }

    /// <summary>
    /// 判断文件是否为 Mod 文件（镜像 VB 的 IsModFile 静态谓词）：
    /// 扩展名（去点小写）属 jar/zip/litemod；若扩展名为 disabled/old，则先去掉该后缀再判断。
    /// </summary>
    private static bool IsModFile(string fullName)
    {
        if (fullName is null) return false;
        var ext = Path.GetExtension(fullName).TrimStart('.').ToLowerInvariant();
        if (ext is "disabled" or "old")
            fullName = Path.GetFileNameWithoutExtension(fullName);
        ext = Path.GetExtension(fullName).TrimStart('.').ToLowerInvariant();
        return ext is "jar" or "zip" or "litemod";
    }
}
