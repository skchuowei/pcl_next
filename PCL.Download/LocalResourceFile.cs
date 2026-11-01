using System.Collections.Concurrent;
using System.Text.Json.Nodes;

namespace PCL.Download;

/// <summary>
/// 本地的社区资源文件（Mod / 整合包等）。
/// 平移自 pcl-next <c>Modules/Resource/LocalResourceFile.vb</c>。
///
/// 与 VB 版一致地保留以下刻意行为：
/// - <see cref="CurseForgeHash"/> 为去空白 MurmurHash2（种子 1）的十进制 uint，等价于 <see cref="HashHelper.ComputeCurseForgeFingerprintFile"/>；
/// - <see cref="ModrinthHash"/> 为小写 SHA1 十六进制；
/// - <see cref="Display"/> / <see cref="Description"/> / <see cref="Version"/> 的 setter 带有上游古怪的过滤规则（纯数字串、含 "modname" 等不采纳）。
///
/// 与 VB 的差异（受 PCL.Download 不能反引 net10.0-windows 的 PCLCS 约束）：
/// - 哈希的跨运行 INI 缓存（VB 写 <c>Cache\ModHash.ini</c>）改为进程内静态缓存（按 路径+长度+修改时间 记忆），
///   语义等价，仅不落盘；如需落盘由宿主在 PCLCS 侧承接；
/// - <see cref="HasUpdate"/>（依赖 Settings / PageInstanceLeft 等 UI 状态）属 UI 关切，已从核心模型移除，仍留于 VB UI 层；
/// - <see cref="OnUpdate"/> 事件保留以镜像结构，但核心场景通常无需订阅。
/// </summary>
public sealed class LocalResourceFile
{
    private static readonly ConcurrentDictionary<string, uint> CurseForgeHashCache = new();
    private static readonly ConcurrentDictionary<string, string> ModrinthHashCache = new();

    /// <summary>该文件的文件信息。</summary>
    public FileInfo File { get; }

    public LocalResourceFile(FileInfo file) => File = file;

    public LocalResourceFile(string fullName) : this(new FileInfo(fullName)) { }

    /// <summary>该文件是否没有被禁用（扩展名属 .jar/.zip/.litemod）。</summary>
    public bool IsEnabled
        => new[] { ".jar", ".zip", ".litemod" }.Contains(File.Extension.ToLowerInvariant());

    /// <summary>假若该资源未被禁用时，文件的文件名（去掉 .disabled / .old 后缀，含 .jar 等）。</summary>
    public string EnabledName
        => File.Name.Replace(".disabled", "").Replace(".old", "");

    /// <summary>假若该资源未被禁用时，文件的完整路径。</summary>
    public string EnabledFullName
        => Path.Combine(File.DirectoryName ?? "", EnabledName);

    /// <summary>在初始化结束之后，任何信息被后续更新时触发此事件（镜像 VB 的 OnUpdate）。</summary>
    public event Action<LocalResourceFile>? OnUpdate;

    private string? _display;

    /// <summary>
    /// 可读名称。可能为无扩展的文件名，但不会是 null。
    /// 与 VB 一致：首次获取时回退到 ProjectVersion?.Display，再回退到去扩展名的 EnabledName，并缓存。
    /// </summary>
    public string Display
    {
        get
        {
            if (_display is null) _display = ProjectVersion?.Display;
            if (_display is null) _display = Path.GetFileNameWithoutExtension(EnabledName);
            return _display;
        }
        set
        {
            // VB：仅当尚未设置、且值不为 null、不含 "modname"、小写不等 "name"、长度 > 1、且不是纯数字串时采纳。
            if (_display is null && value is not null && !value.Contains("modname")
                && value.ToLowerInvariant() != "name" && value.Length > 1
                && VbCompat.Val(value).ToString(System.Globalization.CultureInfo.InvariantCulture) != value)
            {
                _display = value;
            }
        }
    }

    private string? _description;

    /// <summary>Mod 的描述信息。可能为 null。</summary>
    public string? Description
    {
        get => _description;
        set
        {
            if (_description is null && value is not null && value.Length > 2)
            {
                _description = value.Trim('\n');
                // 优化显示：若以 [a-zA-Z0-9] 结尾，加上小数点句号
                if ("qwertyuiopasdfghjklzxcvbnm0123456789".IndexOf(_description[_description.Length - 1]) >= 0)
                    _description += ".";
            }
        }
    }

    private string? _version;

    /// <summary>Mod 的版本。不保证符合版本格式规范，且可能为 null。</summary>
    public string? Version
    {
        get
        {
            if (_version is null) _version = ProjectVersion?.Version;
            return _version;
        }
        set
        {
            // VB：已设置且符合版本格式（仅含数字/点/连字符）则不再覆盖。
            if (_version is not null && System.Text.RegularExpressions.Regex.IsMatch(_version, @"[0-9.\-]+")) return;
            if (value is not null && value.ContainsIgnoreCase("version")) value = "version"; // 需要修改的标识
            _version = value;
        }
    }

    // ── 网络信息 ──────────────────────────────────────────────────────────────

    private ResourceProject? _project;

    /// <summary>该 Mod 关联的网络项目。</summary>
    public ResourceProject? Project
    {
        get => _project;
        set
        {
            _project = value;
            OnUpdate?.Invoke(this);
        }
    }

    /// <summary>本地文件对应的联网文件信息。</summary>
    public ResourceVersion? ProjectVersion { get; set; }

    private ResourceVersion? _updateFile;

    /// <summary>该 Mod 对应的联网最新版本。</summary>
    public ResourceVersion? UpdateFile
    {
        get => _updateFile;
        set
        {
            _updateFile = value;
            OnUpdate?.Invoke(this);
        }
    }

    /// <summary>该 Mod 的更新日志网址。</summary>
    public List<string> ChangelogUrls { get; set; } = new();

    /// <summary>所有网络信息是否已成功加载。</summary>
    public bool OnlineDataLoaded { get; set; }

    private uint? _curseForgeHash;

    /// <summary>用于 CurseForge 信息获取的 Hash 值（MurmurHash2，十进制 uint）。</summary>
    public uint CurseForgeHash
    {
        get
        {
            if (_curseForgeHash is null)
            {
                var cacheKey = $"{EnabledFullName}|{File.Length}|{File.LastWriteTimeUtc.Ticks}";
                if (!CurseForgeHashCache.TryGetValue(cacheKey, out uint cached))
                {
                    cached = HashHelper.ComputeCurseForgeFingerprintFile(File.FullName);
                    CurseForgeHashCache[cacheKey] = cached;
                }
                _curseForgeHash = cached;
            }
            return _curseForgeHash.Value;
        }
    }

    private string? _modrinthHash;

    /// <summary>用于 Modrinth 信息获取的 Hash 值（SHA1，固定小写）。</summary>
    public string ModrinthHash
    {
        get
        {
            if (_modrinthHash is null)
            {
                var cacheKey = $"{EnabledFullName}|{File.Length}|{File.LastWriteTimeUtc.Ticks}";
                if (!ModrinthHashCache.TryGetValue(cacheKey, out string? cached) || cached is null)
                {
                    cached = HashHelper.ComputeFile(File.FullName, HashType.Sha1);
                    ModrinthHashCache[cacheKey] = cached;
                }
                _modrinthHash = cached;
            }
            return _modrinthHash;
        }
    }

    /// <summary>将网络信息保存为 JSON（镜像 VB ToJson）。</summary>
    public JsonObject ToJson()
    {
        var json = new JsonObject();
        if (Project is not null) json["Project"] = Project.ToJson();
        var urls = new JsonArray();
        foreach (var u in ChangelogUrls) urls.Add(u);
        json["ChangelogUrls"] = urls;
        json["OnlineDataLoaded"] = JsonValue.Create(OnlineDataLoaded);
        if (ProjectVersion is not null) json["Version"] = ProjectVersion.ToCacheJson();
        if (UpdateFile is not null) json["UpdateFile"] = UpdateFile.ToCacheJson();
        return json;
    }

    /// <summary>从 JSON 中读取网络信息（镜像 VB FromJson）。</summary>
    public void FromJson(JsonObject json)
    {
        OnlineDataLoaded = json["OnlineDataLoaded"]?.GetValue<bool>() ?? false;
        if (json.ContainsKey("Project") && json["Project"] is JsonObject pj) Project = new ResourceProject(pj);
        if (json.ContainsKey("ChangelogUrls") && json["ChangelogUrls"] is JsonArray ca)
            ChangelogUrls = ca.Select(x => x!.GetValue<string>()).ToList();
        if (json.ContainsKey("Version") && json["Version"] is JsonObject vj) ProjectVersion = ResourceVersion.FromCacheJson(vj);
        if (json.ContainsKey("UpdateFile") && json["UpdateFile"] is JsonObject uj) UpdateFile = ResourceVersion.FromCacheJson(uj);
    }

    public override string ToString() => File.FullName;

    public override bool Equals(object? obj)
    {
        var target = obj as LocalResourceFile;
        return target is not null && PathIsSame(File.FullName, target.File.FullName);
    }

    public override int GetHashCode() => File.FullName.ToLowerInvariant().GetHashCode();

    /// <summary>路径相等判断（去掉尾部分隔符后忽略大小写比较，对齐 VB 的 PathUtils.IsSame）。</summary>
    private static bool PathIsSame(string a, string b)
    {
        string Norm(string s) => s.TrimEnd('\\', '/');
        return string.Equals(Norm(a), Norm(b), StringComparison.OrdinalIgnoreCase);
    }
}
