namespace PCL.Install;

using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using PCL.Download;

/// <summary>
/// 安装清单模型，字段与 PCLCS 的 <c>McInstallManifest</c>（及其 <c>McComponents</c> /
/// <c>ManagedFile</c>）一一对应。
/// <para>
/// 之所以在 <c>PCL.Install</c> 重新定义而不引用 PCLCS：PCLCS 是 <c>net10.0-windows</c> + WPF，
/// 内核护栏 <c>PclIsGuiFree</c> 禁止引用。属性名、可空口径与 PCLCS 保持一致，
/// 保证同一份 <c>PCL/config.json</c> 在 Windows 与 Linux 之间可互读。
/// </para>
/// </summary>
/// <param name="SchemaVersion">清单格式版本；PCLCS 侧常量同为 1。</param>
/// <param name="Components">安装目标及受管理组件版本。</param>
/// <param name="LastModifiedUtc">最后修改时间（UTC）。</param>
/// <param name="ManagedFiles">受管理文件；没有文件时为空列表。</param>
public sealed record InstallManifest(
    int SchemaVersion,
    InstallManifestComponents Components,
    DateTime LastModifiedUtc,
    IReadOnlyList<ManagedInstallFile> ManagedFiles)
{
    /// <summary>清单格式版本，对齐 PCLCS <c>Constants.McInstallManifestVersion</c>。</summary>
    public const int CurrentSchemaVersion = 1;
}

/// <summary>
/// 安装目标与受管理组件版本，对应 PCLCS <c>McComponents</c>。
/// </summary>
public sealed class InstallManifestComponents
{
    /// <summary>目标 Minecraft 版本。</summary>
    public required string MinecraftVersion { get; init; }

    /// <summary>
    /// 各 Mod 加载器版本串（<b>原始值</b>）。写入清单前会按
    /// <see cref="InstallManifestRules.NormalizeLoaderVersion"/> 剥掉 Forge / NeoForge 的 MC 前缀，
    /// 与 VB 写入时的处理一致。
    /// </summary>
    public IReadOnlyDictionary<ModLoaders, string> ModLoaderVersions { get; init; }
        = new Dictionary<ModLoaders, string>();

    /// <summary>OptiFine 条目（原样透传，结构由宿主决定）；不安装时为 <c>null</c>。</summary>
    public JsonNode? OptiFineEntry { get; init; }

    /// <summary>由 PCL 管理的 Fabric API 版本；不安装时为 <c>null</c>。</summary>
    public string? FabricApiVersion { get; init; }

    /// <summary>由 PCL 管理的 OptiFabric 版本；不安装时为 <c>null</c>。</summary>
    public string? OptiFabricVersion { get; init; }
}

/// <summary>
/// 由 PCL 管理并记录的版本内文件，对应 PCLCS <c>McInstallManifest.ManagedFile</c>。
/// </summary>
/// <param name="RelativePath">相对游戏目录的路径，全小写、以 <c>mods/</c> 开头。</param>
/// <param name="Sha1">小写 SHA1。</param>
/// <param name="LastWriteTimeUtc">目标文件的最后修改时间（UTC）。</param>
/// <param name="Component">安装器组件名；为 <c>null</c> 时表示由整合包管理（不写入该键）。</param>
public sealed record ManagedInstallFile(
    string RelativePath,
    string Sha1,
    DateTime LastWriteTimeUtc,
    string? Component = null);

/// <summary>
/// 安装清单的落盘与 JSON 形状。键名与 PCLCS 的 Newtonsoft 默认序列化结果逐项对齐
/// （PascalCase、<c>null</c> 可选键省略、缩进两格），并用原子替换写回，
/// 避免安装中途崩溃把实例配置写坏。
/// </summary>
public static class InstallManifestStore
{
    /// <summary>清单所在的实例配置目录（相对版本目录）。</summary>
    public const string ConfigFolderName = "PCL";

    /// <summary>实例配置文件名。</summary>
    public const string ConfigFileName = "config.json";

    /// <summary>清单在实例配置里的键名（对齐 PCLCS <c>Configs.McInstallManifest</c>）。</summary>
    public const string ConfigKey = "MinecraftInstallManifest";

    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true,
        // 中文组件名 / 路径不该被转义成 \uXXXX，否则人工排查清单时完全不可读。
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>把清单投影成 JSON 对象（读取侧无需依赖具体 CLR 类型也可复用）。</summary>
    public static JsonObject ToJson(InstallManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(manifest.Components);

        var versions = new JsonObject();
        foreach (var (loader, version) in manifest.Components.ModLoaderVersions)
            versions[loader.ToString()] = version;

        var components = new JsonObject
        {
            ["MinecraftVersion"] = manifest.Components.MinecraftVersion,
            ["ModLoaderVersions"] = versions,
        };
        // 与 Newtonsoft 的 NullValueHandling.Ignore 对齐：可选键为 null 时不写。
        if (manifest.Components.OptiFineEntry is { } optiFineEntry)
            components["OptiFineEntry"] = optiFineEntry.DeepClone();
        if (manifest.Components.FabricApiVersion is not null)
            components["FabricApiVersion"] = manifest.Components.FabricApiVersion;
        if (manifest.Components.OptiFabricVersion is not null)
            components["OptiFabricVersion"] = manifest.Components.OptiFabricVersion;

        var managedFiles = new JsonArray();
        foreach (var file in manifest.ManagedFiles)
        {
            var item = new JsonObject
            {
                ["RelativePath"] = file.RelativePath,
                ["Sha1"] = file.Sha1,
                ["LastWriteTimeUtc"] = file.LastWriteTimeUtc,
            };
            if (file.Component is not null) item["Component"] = file.Component;
            managedFiles.Add(item);
        }

        return new JsonObject
        {
            ["SchemaVersion"] = manifest.SchemaVersion,
            ["Components"] = components,
            ["LastModifiedUtc"] = manifest.LastModifiedUtc,
            ["ManagedFiles"] = managedFiles,
        };
    }

    /// <summary>
    /// 把清单写入 <c>{versionFolder}/PCL/config.json</c> 的 <see cref="ConfigKey"/> 键，
    /// 保留文件里其它键（实例设置）。
    /// </summary>
    /// <exception cref="InvalidDataException">现有配置文件不是合法 JSON 对象（宁可失败也不覆盖用户配置）。</exception>
    public static void Write(string versionFolder, InstallManifest manifest)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(versionFolder);
        ArgumentNullException.ThrowIfNull(manifest);

        var configFolder = Path.Combine(versionFolder, ConfigFolderName);
        var configPath = Path.Combine(configFolder, ConfigFileName);

        var root = ReadConfig(configPath);
        root[ConfigKey] = ToJson(manifest);

        Directory.CreateDirectory(configFolder);
        WriteAtomic(configPath, root.ToJsonString(WriteOptions));
    }

    private static JsonObject ReadConfig(string configPath)
    {
        if (!File.Exists(configPath)) return new JsonObject();

        var text = File.ReadAllText(configPath);
        try
        {
            return JsonNode.Parse(text) as JsonObject
                   ?? throw new InvalidDataException($"实例配置的根节点不是 JSON 对象：{configPath}");
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"实例配置不是合法 JSON，已中止写入以免覆盖：{configPath}", ex);
        }
    }

    /// <summary>先写同目录临时文件再原子替换，失败时原配置保持不变。</summary>
    private static void WriteAtomic(string path, string text)
    {
        var tempPath = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            File.WriteAllText(tempPath, text);
            File.Move(tempPath, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
        }
    }
}
