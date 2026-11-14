using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading;
using MeloongCore;
using MeloongCore.Extensions;
using Newtonsoft.Json.Linq;

namespace PCL
{
    /// <summary>
    /// 实例级整合包备份与还原。
    /// 备份目录永远位于实例自己的版本文件夹下（versions\&lt;实例名&gt;\backups\），
    /// 不同实例之间不共用任何备份文件；删除实例时备份随实例目录一同删除。
    /// </summary>
    public static class ModBackup
    {

        #region 类型定义

        /// <summary>备份的触发来源。</summary>
        public enum BackupKind
        {
            /// <summary>导入整合包后自动生成的初始基准包，锁定不可删除。</summary>
            Initial,
            /// <summary>手动完整 zip 备份。</summary>
            FullZip,
            /// <summary>手动整合包备份（只含整合包相关与运行必需项）。</summary>
            Modpack,
            /// <summary>导入包备份：只保存导入该实例时用的原始整合包文件，还原时按该包全量重下。</summary>
            Import,
            /// <summary>增量备份，必须存在同实例内的基准。</summary>
            Incremental,
            /// <summary>还原前自动兜底备份。</summary>
            PreRestore
        }

        /// <summary>备份包含的文件范围。</summary>
        public enum BackupScope
        {
            /// <summary>实例内容根下的全部文件（排除临时目录）。</summary>
            Full,
            /// <summary>仅整合包相关与运行必需项。</summary>
            Modpack
        }

        /// <summary>备份清单中的单个文件条目。</summary>
        public sealed class BackupFileEntry
        {
            /// <summary>相对于实例内容根的路径，统一使用 / 分隔。</summary>
            public string RelativePath;
            public long Size;
            public DateTime LastWriteTime;
        }

        /// <summary>一份备份的元信息。</summary>
        public sealed class BackupItem
        {
            public string FilePath;
            public string ManifestPath;
            public BackupKind Kind;
            public BackupScope Scope;
            public DateTime CreatedAt;
            public long Size;
            public string Remark = "";
            public bool Locked;
            public string BaseFile;
            public string InstanceName;
            public bool HasManifest;
            /// <summary>导入包备份专用的原始整合包文件绝对路径；其余类型为空。</summary>
            public string PackFile;
            public List<BackupFileEntry> Files = new List<BackupFileEntry>();
            public List<string> Deleted = new List<string>();

            public string FileName
            {
                get { return Path.GetFileName(FilePath); }
            }

            public bool IsIncremental
            {
                get { return Kind == BackupKind.Incremental; }
            }

            public string KindTag
            {
                get
                {
                    switch (Kind)
                    {
                        case BackupKind.Initial:
                            return "初始";
                        case BackupKind.FullZip:
                            return "完整";
                        case BackupKind.Modpack:
                            return "整合包";
                        case BackupKind.Import:
                            return "导入包";
                        case BackupKind.Incremental:
                            return "增量";
                        default:
                            return "还原前";
                    }
                }
            }
        }

        /// <summary>
        /// 一次备份 / 还原操作所针对的实例上下文。
        /// 所有路径均以 Windows 分隔符结尾。
        /// </summary>
        public sealed class BackupContext
        {
            public string InstanceName;
            /// <summary>实例版本文件夹（versions\&lt;实例名&gt;\）。</summary>
            public string VersionFolder;
            /// <summary>实例实际游戏根目录（版本隔离开启时等于 VersionFolder）。</summary>
            public string ContentRoot;
            /// <summary>内容根是否为全局 .minecraft 根（未开启版本隔离）。</summary>
            public bool IsGlobalRoot;

            public string BackupRoot
            {
                get { return Path.Combine(VersionFolder, "backups"); }
            }
        }

        #endregion

        #region 常量

        /// <summary>还原前兜底的时间阈值（分钟）。</summary>
        public const double PreRestoreThresholdMinutes = 10d;

        /// <summary>完整 / 增量备份始终排除的目录名（任意层级）。</summary>
        private static readonly HashSet<string> ExcludedDirectoryNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "backups",
            "logs",
            "crash-reports",
            "caches",
            "cache",
            ".cache",
            ".fabric",
            ".mixin.out",
            "avatar-cache",
            "cosmetic-cache",
            "downloads",
            "PCL",
            "structureCacheV1"
        };

        /// <summary>未开启版本隔离时，额外排除的全局安装目录，避免把别的实例一起打包。</summary>
        private static readonly HashSet<string> GlobalExcludedDirectoryNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "versions",
            "assets",
            "libraries",
            "runtime",
            "logs"
        };

        /// <summary>整合包备份包含的目录。</summary>
        private static readonly string[] ModpackFolders =
        {
            "mods",
            "config",
            "defaultconfigs",
            "kubejs",
            "scripts",
            "saves",
            "resourcepacks",
            "shaderpacks"
        };

        /// <summary>整合包备份包含的根级文件。</summary>
        private static readonly string[] ModpackFiles =
        {
            "options.txt",
            "optionsof.txt",
            "servers.dat",
            "iris.properties"
        };

        #endregion

        #region 上下文

        /// <summary>根据实例构造备份上下文。</summary>
        public static BackupContext ContextFromInstance(ModMinecraft.McInstance Instance)
        {
            if (Instance is null)
                throw new ArgumentNullException(nameof(Instance));
            var VersionFolder = PathUtils.AddSlashSuffix(Instance.PathVersion);
            var ContentRoot = PathUtils.AddSlashSuffix(Instance.PathIndie);
            return new BackupContext()
            {
                InstanceName = Instance.Name,
                VersionFolder = VersionFolder,
                ContentRoot = ContentRoot,
                IsGlobalRoot = !PathUtils.IsSame(VersionFolder, ContentRoot)
            };
        }

        /// <summary>根据版本文件夹构造备份上下文（整合包安装完成时使用，隔离已强制开启）。</summary>
        public static BackupContext ContextFromVersionFolder(string VersionFolder)
        {
            var Folder = PathUtils.AddSlashSuffix(VersionFolder);
            return new BackupContext()
            {
                InstanceName = Path.GetFileName(Path.TrimEndingDirectorySeparator(Folder)),
                VersionFolder = Folder,
                ContentRoot = Folder,
                IsGlobalRoot = false
            };
        }

        #endregion

        #region 列表与读取

        /// <summary>列出该实例的全部备份，按时间倒序。</summary>
        public static List<BackupItem> ListBackups(BackupContext Context)
        {
            var Result = new List<BackupItem>();
            foreach (var Kind in new[] { BackupKind.Initial, BackupKind.Import, BackupKind.Modpack, BackupKind.FullZip, BackupKind.Incremental, BackupKind.PreRestore })
            {
                var Folder = FolderForKind(Context, Kind);
                if (!DirectoryUtils.Exists(Folder))
                    continue;
                foreach (var Path in DirectoryUtils.EnumerateFiles(Folder, false, "*.zip"))
                {
                    try
                    {
                        var Item = ReadItem(Path);
                        if (Item is not null)
                            Result.Add(Item);
                    }
                    catch (Exception ex)
                    {
                        Logger.Warn(ex, "读取备份信息失败：" + Path);
                    }
                }
            }
            Result.Sort((a, b) => b.CreatedAt.CompareTo(a.CreatedAt));
            return Result;
        }

        /// <summary>读取单个备份（含清单）。清单缺失时按目录推断类型。</summary>
        private static BackupItem ReadItem(string ZipPath)
        {
            var Item = new BackupItem()
            {
                FilePath = ZipPath,
                ManifestPath = ZipPath + ".json",
                CreatedAt = FileUtils.GetInfo(ZipPath).LastWriteTime,
                Size = FileUtils.GetInfo(ZipPath).Length,
                InstanceName = "",
                Scope = BackupScope.Full,
                Kind = KindFromFolder(Path.GetDirectoryName(ZipPath))
            };
            var Manifest = ReadManifest(Item);
            if (Manifest is null)
            {
                // 无清单时按目录降级：只有「整合包」与「还原前」可以安全地按白名单范围还原，
                // 其余类型（初始 / 完整 / 增量）都是整目录快照。
                Item.Scope = Item.Kind == BackupKind.Modpack || Item.Kind == BackupKind.PreRestore ? BackupScope.Modpack : BackupScope.Full;
                return Item;
            }
            Item.HasManifest = true;
            Item.Kind = Manifest.Kind;
            Item.Scope = Manifest.Scope;
            Item.CreatedAt = Manifest.CreatedAt;
            Item.Remark = Manifest.Remark;
            Item.Locked = Manifest.Locked;
            Item.BaseFile = Manifest.BaseFile;
            Item.InstanceName = Manifest.InstanceName;
            Item.Files = Manifest.Files;
            Item.Deleted = Manifest.Deleted;
            Item.PackFile = Manifest.PackFile;
            return Item;
        }

        /// <summary>把清单里相对备份根记录的原始整合包路径还原成绝对路径。</summary>
        private static string ResolvePackPath(string ZipPath, string Relative)
        {
            if (string.IsNullOrEmpty(Relative))
                return null;
            if (Path.IsPathRooted(Relative))
                return Relative;
            string Folder = Path.GetDirectoryName(ZipPath) ?? "";
            string Root = Path.GetDirectoryName(Folder) ?? Folder;
            return Path.Combine(Root, Relative);
        }

        private static BackupItem ReadManifest(BackupItem Source)
        {
            string ManifestPath = Source.FilePath + ".json";
            if (!FileUtils.Exists(ManifestPath))
                return null;
            try
            {
                var Json = JObject.Parse(FileUtils.ReadAsString(ManifestPath, Encoding.UTF8));
                var Item = new BackupItem()
                {
                    FilePath = Source.FilePath,
                    ManifestPath = ManifestPath,
                    Kind = ParseKind((string)Json["Kind"], Source.Kind),
                    Scope = ParseScope((string)Json["Scope"], Source.Scope),
                    InstanceName = (string)Json["Instance"] ?? "",
                    CreatedAt = ParseTime((string)Json["CreatedAt"], Source.CreatedAt),
                    Remark = (string)Json["Remark"] ?? "",
                    Locked = Json["Locked"] is not null && Json["Locked"].Type == JTokenType.Boolean && (bool)Json["Locked"],
                    BaseFile = (string)Json["Base"],
                    PackFile = ResolvePackPath(Source.FilePath, (string)Json["Pack"]),
                    HasManifest = true
                };
                Item.Size = Source.Size;
                if (Json["Files"] is JArray Files)
                {
                    foreach (var Token in Files)
                    {
                        if (Token is not JObject File)
                            continue;
                        Item.Files.Add(new BackupFileEntry()
                        {
                            RelativePath = (string)File["p"] ?? "",
                            Size = File["s"] is null ? 0L : File["s"].Value<long>(),
                            LastWriteTime = ParseTime((string)File["t"], DateTime.MinValue)
                        });
                    }
                }
                if (Json["Deleted"] is JArray Deleted)
                {
                    foreach (var Token in Deleted)
                        Item.Deleted.Add((string)Token ?? "");
                }
                return Item;
            }
            catch (Exception ex)
            {
                Logger.Warn(ex, "解析备份清单失败：" + ManifestPath);
                return null;
            }
        }

        private static BackupKind ParseKind(string Text, BackupKind Fallback)
        {
            switch ((Text ?? "").ToLower())
            {
                case "initial":
                    return BackupKind.Initial;
                case "full-zip":
                    return BackupKind.FullZip;
                case "modpack":
                    return BackupKind.Modpack;
                case "import":
                    return BackupKind.Import;
                case "incremental":
                    return BackupKind.Incremental;
                case "pre-restore":
                    return BackupKind.PreRestore;
                default:
                    return Fallback;
            }
        }

        private static BackupScope ParseScope(string Text, BackupScope Fallback)
        {
            return (Text ?? "").ToLower() == "modpack" ? BackupScope.Modpack : (string.IsNullOrEmpty(Text) ? Fallback : BackupScope.Full);
        }

        private static DateTime ParseTime(string Text, DateTime Fallback)
        {
            if (string.IsNullOrEmpty(Text))
                return Fallback;
            try
            {
                return DateTime.Parse(Text);
            }
            catch (Exception)
            {
                return Fallback;
            }
        }

        private static BackupKind KindFromFolder(string Folder)
        {
            string Name = (Folder ?? "").ToLower();
            if (Name.EndsWith(@"\initial"))
                return BackupKind.Initial;
            if (Name.EndsWith(@"\full-zip"))
                return BackupKind.FullZip;
            if (Name.EndsWith(@"\modpack"))
                return BackupKind.Modpack;
            if (Name.EndsWith(@"\imported"))
                return BackupKind.Import;
            if (Name.EndsWith(@"\incremental"))
                return BackupKind.Incremental;
            return BackupKind.PreRestore;
        }

        private static string FolderForKind(BackupContext Context, BackupKind Kind)
        {
            switch (Kind)
            {
                case BackupKind.Initial:
                    return Path.Combine(Context.BackupRoot, "initial");
                case BackupKind.FullZip:
                    return Path.Combine(Context.BackupRoot, "manual", "full-zip");
                case BackupKind.Modpack:
                    return Path.Combine(Context.BackupRoot, "manual", "modpack");
                case BackupKind.Import:
                    return Path.Combine(Context.BackupRoot, "imported");
                case BackupKind.Incremental:
                    return Path.Combine(Context.BackupRoot, "manual", "incremental");
                default:
                    return Path.Combine(Context.BackupRoot, "pre-restore");
            }
        }

        #endregion

        #region 创建备份

        /// <summary>备份草稿：已完成文件收集与命名，尚未写压缩包。供「收集 → 写包 → 写清单」分阶段任务共用。</summary>
        public sealed class BackupDraft
        {
            public BackupContext Context;
            public BackupKind Kind;
            public BackupScope Scope;
            public string Remark;
            public bool Locked;
            public bool Incremental;
            public List<BackupFileEntry> Source = new List<BackupFileEntry>();
            public List<string> Deleted = new List<string>();
            public BackupItem Item;
        }

        /// <summary>阶段一：收集需要备份的文件并决定文件名（不写盘）。</summary>
        public static BackupDraft DraftBackup(BackupContext Context, BackupKind Kind, BackupScope Scope, string Remark, bool Locked, string BaseFile, CancellationToken c, bool Incremental = false)
        {
            var Draft = new BackupDraft() { Context = Context, Kind = Kind, Scope = Scope, Remark = Remark ?? "", Locked = Locked, Incremental = Incremental };
            DirectoryUtils.Create(FolderForKind(Context, Kind));
            string ResolvedBase = null;
            if (Kind == BackupKind.Incremental || Incremental)
            {
                var Base = string.IsNullOrEmpty(BaseFile) ? FindBase(Context) : FindByFileName(Context, BaseFile);
                if (Base is null)
                    throw new OperationCanceledException("当前实例没有可用的基准备份，无法创建增量备份。");
                var BaseFiles = ResolveSnapshot(Context, Base, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
                var Current = EnumerateFiles(Context, BackupScope.Full, c);
                foreach (var File in Current)
                {
                    var Old = BaseFiles.ContainsKey(File.RelativePath) ? BaseFiles[File.RelativePath] : null;
                    if (Old is null || Old.Size != File.Size || !IsSameTime(Old.LastWriteTime, File.LastWriteTime))
                        Draft.Source.Add(File);
                }
                var CurrentPaths = new HashSet<string>(Current.Select(f => f.RelativePath), StringComparer.OrdinalIgnoreCase);
                foreach (var BasePath in BaseFiles.Keys)
                {
                    if (!CurrentPaths.Contains(BasePath))
                        Draft.Deleted.Add(BasePath);
                }
                ResolvedBase = Base.FileName;
                Logger.Info($"增量比对：基准 {Base.FileName}（清单 {BaseFiles.Count} 个文件，范围 {(Base.Scope == BackupScope.Modpack ? "整合包" : "完整")}），当前 {Current.Count} 个文件，变化 {Draft.Source.Count} 个，删除 {Draft.Deleted.Count} 个");
            }
            else
            {
                Draft.Source.AddRange(EnumerateFiles(Context, Scope, c));
            }
            c.ThrowIfCancellationRequested();
            var Time = DateTime.Now;
            string Name = BuildFileName(Context, Kind, Time, Remark);
            string ZipPath = Path.Combine(FolderForKind(Context, Kind), Name + ".zip");
            int Suffix = 2;
            while (FileUtils.Exists(ZipPath))
            {
                ZipPath = Path.Combine(FolderForKind(Context, Kind), $"{Name}-{Suffix}.zip");
                Suffix += 1;
            }
            Draft.Item = new BackupItem()
            {
                FilePath = ZipPath,
                ManifestPath = ZipPath + ".json",
                Kind = Kind,
                Scope = Kind == BackupKind.Incremental || Incremental ? BackupScope.Full : Scope,
                CreatedAt = Time,
                Remark = Draft.Remark,
                Locked = Locked,
                BaseFile = ResolvedBase,
                InstanceName = Context.InstanceName,
                HasManifest = true,
                Files = Draft.Source
            };
            return Draft;
        }

        /// <summary>阶段二：把收集到的文件写进压缩包。</summary>
        public static BackupItem CommitBackup(BackupDraft Draft, CancellationToken c, ProgressProvider p, double From = 0d, double To = 0.95d)
        {
            try
            {
                Draft.Item.Files = WriteZip(Draft.Item.FilePath, Draft.Context, Draft.Source, c, p, From, To);
                Draft.Item.Deleted = Draft.Deleted;
                Draft.Item.Size = FileUtils.GetInfo(Draft.Item.FilePath).Length;
            }
            catch (Exception)
            {
                try
                {
                    FileUtils.Delete(Draft.Item.FilePath);
                }
                catch (Exception)
                {
                    // 清理失败不覆盖原始异常
                }
                throw;
            }
            return Draft.Item;
        }

        /// <summary>阶段三：写入备份清单，此后该备份才会出现在列表里。</summary>
        public static BackupItem FinishBackup(BackupDraft Draft, ProgressProvider p = null)
        {
            WriteManifest(Draft.Item);
            Logger.Info($"已创建{Draft.Kind.Text()}备份：{Path.GetFileName(Draft.Item.FilePath)}（{Draft.Source.Count} 个文件，{StringUtils.FormatByteSize(Draft.Item.Size)}）");
            p?.Set(1d);
            return Draft.Item;
        }

        /// <summary>
        /// 创建一份备份。所有文件都从 <paramref name="Context"/>.ContentRoot 现场提取，绝不复用原始整合包文件。
        /// </summary>
        /// <param name="Kind">备份类型。</param>
        /// <param name="Scope">文件范围，增量备份固定为 Full。</param>
        /// <param name="Remark">备注，可为空。</param>
        /// <param name="Locked">是否锁定（初始基准包为 True）。</param>
        /// <param name="BaseFile">增量备份的基准文件名，为空时自动取该实例最近的完整基准。</param>
        public static BackupItem Create(BackupContext Context, BackupKind Kind, BackupScope Scope, string Remark, bool Locked, string BaseFile, CancellationToken c, ProgressProvider p, double From = 0d, double To = 1d, bool Incremental = false, Action<string> Stage = null)
        {
            Stage?.Invoke("收集需要备份的文件");
            var Draft = DraftBackup(Context, Kind, Scope, Remark, Locked, BaseFile, c, Incremental);
            Stage?.Invoke("写入压缩包");
            CommitBackup(Draft, c, p, From, From + (To - From) * 0.95d);
            Stage?.Invoke("写入备份清单");
            return FinishBackup(Draft, p);
        }

        /// <summary>为整合包安装流程追加「创建初始基准备份」与「留档导入包」两步，失败不阻断安装。</summary>
        public static void AppendInitialBackupTask(List<ModLoader.LoaderBase> Loaders, string VersionFolderName, string VersionFolder, string PackAddress = null)
        {
            Loaders.Add(new ModLoader.LoaderTask<string, string>("创建初始基准备份", (Task) =>
        {
            try
            {
                var Context = ContextFromVersionFolder(VersionFolder);
                var Existing = ListBackups(Context).Where(i => i.Kind == BackupKind.Initial).ToList();
                if (Existing.Any())
                {
                    Logger.Info($"实例 {VersionFolderName} 已存在初始基准备份，跳过：{Existing[0].FileName}");
                    return;
                }
                Create(Context, BackupKind.Initial, BackupScope.Full, "导入整合包后的初始基准", true, null, Task.CreateCancellationToken(), Task.CreateSyncProgressProvider());
            }
            catch (Exception ex)
            {
                if (ex is OperationCanceledException)
                    throw;
                Logger.Warn(ex, $"创建实例 {VersionFolderName} 的初始基准备份失败（不影响整合包安装）");
            }
        })
            {
                ProgressWeight = 2d,
                Show = false
            });

            // 留档导入包：整合包备份只保存原始包文件，还原时按它全量重下
            Loaders.Add(new ModLoader.LoaderTask<string, string>("留档整合包原文件", (Task) =>
            {
                try
                {
                    var Context = ContextFromVersionFolder(VersionFolder);
                    if (FindImportPack(Context) is not null)
                    {
                        Logger.Info($"实例 {VersionFolderName} 已留档导入包，跳过");
                        return;
                    }
                    SaveImportPack(Context, PackAddress, Task.CreateCancellationToken(), Task.CreateSyncProgressProvider());
                }
                catch (Exception ex)
                {
                    if (ex is OperationCanceledException)
                        throw;
                    Logger.Warn(ex, $"实例 {VersionFolderName} 留档整合包原文件失败（不影响安装）");
                }
            })
            {
                ProgressWeight = 1d,
                Show = false
            });
        }

        #endregion

        /// <summary>
        /// 保存「导入包备份」：只把导入该实例时用的原始整合包文件留档，并写一份只含该包信息的清单。
        /// 还原这种备份时会清空实例内容并按该包重新下载安装（等价于删掉游戏重装），而不是回放文件快照。
        /// </summary>
        public static BackupItem SaveImportPack(BackupContext Context, string PackAddress, CancellationToken c, ProgressProvider p)
        {
            if (string.IsNullOrEmpty(PackAddress) || !FileUtils.Exists(PackAddress))
            {
                Logger.Warn($"导入包备份跳过：原始整合包不可用（{PackAddress ?? "null"}）");
                return null;
            }
            string Folder = FolderForKind(Context, BackupKind.Import);
            DirectoryUtils.Create(Folder);
            string Name = Path.GetFileName(PackAddress);
            string DestName = $"{DateTime.Now:yyyyMMdd-HHmmss}-{Name}";
            string Dest = Path.Combine(Folder, DestName);
            c.ThrowIfCancellationRequested();
            p?.Set(0.05d);
            if (!string.Equals(Path.GetFullPath(PackAddress), Path.GetFullPath(Dest), StringComparison.OrdinalIgnoreCase))
                System.IO.File.Copy(PackAddress, Dest, true);
            p?.Set(0.9d);

            var Item = new BackupItem()
            {
                FilePath = Dest,
                ManifestPath = Dest + ".json",
                Kind = BackupKind.Import,
                Scope = BackupScope.Full,
                CreatedAt = DateTime.Now,
                Size = FileUtils.GetInfo(Dest).Length,
                Remark = "导入该实例时使用的原始整合包",
                Locked = false,
                InstanceName = Context.InstanceName,
                PackFile = Dest,
                HasManifest = true
            };
            var Json = new JObject
            {
                ["Kind"] = "import",
                ["Scope"] = "full",
                ["Instance"] = Context.InstanceName,
                ["CreatedAt"] = Item.CreatedAt.ToString("O"),
                ["Remark"] = Item.Remark,
                ["Locked"] = false,
                ["Pack"] = "imported\\" + DestName
            };
            FileUtils.Write(Item.ManifestPath, Json.ToString(), Encoding.UTF8);
            p?.Set(1d);
            Logger.Info($"已保存导入包备份：{DestName}");
            return Item;
        }

        /// <summary>从当前实例现场生成「初始基准包」，锁定不可删除；配合「还原到初始包」使用。</summary>
        public static BackupItem CreateInitial(BackupContext Context, CancellationToken c, ProgressProvider p, Action<string> Stage = null)
        {
            Stage?.Invoke("打包当前实例作为初始基准");
            var Item = Create(Context, BackupKind.Initial, BackupScope.Full, "手动初始化的整合包基准", true, null, c, p);
            Stage?.Invoke("初始基准已生成");
            return Item;
        }

        /// <summary>还原到最新的初始基准包（找不到则抛出异常）。</summary>
        public static BackupItem RestoreToInitial(BackupContext Context, CancellationToken c, ProgressProvider p, Action<string> Stage = null)
        {
            var Initial = ListBackups(Context)
                .Where(i => i.Kind == BackupKind.Initial)
                .OrderByDescending(i => i.CreatedAt)
                .FirstOrDefault();
            if (Initial is null)
                throw new Exception("该实例还没有初始基准包，请先点「初始化整合包」。");
            Restore(Context, Initial, c, p, Stage);
            return Initial;
        }

        /// <summary>查找该实例已有的导入包备份（没有则返回 null）。</summary>
        public static BackupItem FindImportPack(BackupContext Context)
        {
            return ListBackups(Context)
                .Where(i => i.Kind == BackupKind.Import && !string.IsNullOrEmpty(i.PackFile))
                .OrderByDescending(i => i.CreatedAt)
                .FirstOrDefault();
        }

        /// <summary>
        /// 从当前实例现场生成一个「导入包」留档：把整合包相关内容连同游戏版本与加载器信息打成 pcln 自建整合包。
        /// 还原时按普通导入流程安装它——先删掉实例内容，再重新导入。
        /// </summary>
        public static BackupItem CreateImportPack(BackupContext Context, ModMinecraft.McInstance Instance, CancellationToken c, ProgressProvider p)
        {
            if (Instance is null)
                throw new ArgumentNullException(nameof(Instance));
            string Folder = FolderForKind(Context, BackupKind.Import);
            DirectoryUtils.Create(Folder);
            string SafeName = Instance.Name;
            foreach (var Bad in Path.GetInvalidFileNameChars())
                SafeName = SafeName.Replace(Bad, '_');
            string DestName = $"imported-{SafeName}-{DateTime.Now:yyyyMMdd-HHmmss}.zip";
            string Dest = Path.Combine(Folder, DestName);

            var Version = Instance.Version;
            var Json = new JObject
            {
                ["name"] = Instance.Name,
                ["gameVersion"] = Version?.VanillaName ?? "",
                ["createdAt"] = DateTime.Now.ToString("O"),
                ["format"] = "pcln-selfpack-1",
                ["loaders"] = new JObject()
            };
            if (Version?.ModLoaderVersions is not null)
            {
                foreach (var Pair in Version.ModLoaderVersions)
                    ((JObject)Json["loaders"])[Pair.Key.ToString().ToLower()] = Pair.Value ?? "";
            }

            string TempJson = Path.Combine(Folder, "." + DestName + ".json");
            FileUtils.Write(TempJson, Json.ToString(), Encoding.UTF8);

            var Sources = new Dictionary<string, string>
            {
                ["pcln-selfpack.json"] = TempJson
            };
            foreach (var File in EnumerateFiles(Context, BackupScope.Modpack, c))
            {
                c.ThrowIfCancellationRequested();
                var Full = SafeCombine(Context.ContentRoot, File.RelativePath);
                if (Full is null || !FileUtils.Exists(Full))
                    continue;
                Sources["minecraft/" + File.RelativePath.Replace('\\', '/')] = Full;
            }
            p?.Set(0.15d);
            try
            {
                // 逐文件写包并上报进度：原来一次性压缩没有任何进度回调，界面会一直停在 0%
                long Total = Math.Max(1L, Sources.Values.Sum(f => Math.Max(FileUtils.GetInfo(f).Length, 1L)));
                long Done = 0L;
                string Parent = Path.GetDirectoryName(Dest);
                DirectoryUtils.Create(Parent);
                using (var Stream = new FileStream(Dest, FileMode.Create, FileAccess.Write, FileShare.None))
                using (var Archive = new ZipArchive(Stream, ZipArchiveMode.Create, true))
                {
                    foreach (var Pair in Sources)
                    {
                        c.ThrowIfCancellationRequested();
                        var Entry = Archive.CreateEntry(Pair.Key, CompressionLevel.Optimal);
                        long Size = Math.Max(FileUtils.GetInfo(Pair.Value).Length, 1L);
                        using (var Input = File.OpenRead(Pair.Value))
                        using (var Output = Entry.Open())
                            Input.CopyTo(Output, 64 * 1024);
                        Done += Size;
                        p?.Set(0.15d + 0.8d * Math.Min(1d, (double)Done / Total));
                    }
                }
            }
            finally
            {
                try { FileUtils.Delete(TempJson); } catch (Exception) { /* 临时文件清理失败不影响结果 */ }
            }
            p?.Set(0.95d);

            var Item = new BackupItem()
            {
                FilePath = Dest,
                ManifestPath = Dest + ".json",
                Kind = BackupKind.Import,
                Scope = BackupScope.Full,
                CreatedAt = DateTime.Now,
                Size = FileUtils.GetInfo(Dest).Length,
                Remark = "由当前实例生成的导入包",
                InstanceName = Context.InstanceName,
                PackFile = Dest,
                HasManifest = true
            };
            var Manifest = new JObject
            {
                ["Kind"] = "import",
                ["Scope"] = "full",
                ["Instance"] = Context.InstanceName,
                ["CreatedAt"] = Item.CreatedAt.ToString("O"),
                ["Remark"] = Item.Remark,
                ["Locked"] = false,
                ["Pack"] = "imported\\" + DestName
            };
            FileUtils.Write(Item.ManifestPath, Manifest.ToString(), Encoding.UTF8);
            p?.Set(1d);
            Logger.Info($"已生成导入包备份：{DestName}（{Sources.Count - 1} 个文件）");
            return Item;
        }

        #region 还原

        /// <summary>
        /// 用指定备份还原当前实例。还原前会自动按 10 分钟阈值生成兜底备份。
        /// </summary>
        public static void Restore(BackupContext Context, BackupItem Item, CancellationToken c, ProgressProvider p, Action<string> Stage = null)
        {
            if (Item.Kind == BackupKind.Import || !string.IsNullOrEmpty(Item.PackFile))
            {
                RestoreFromImportPack(Context, Item, c, p, Stage);
                return;
            }
            if (!FileUtils.Exists(Item.FilePath))
                throw new FileNotFoundException("备份文件不存在：" + Item.FilePath);
            Stage?.Invoke("创建还原前兜底备份");
            EnsurePreRestore(Context, c, p, 0d, 0.15d);
            Stage?.Invoke("清空并回放备份文件");
            RestoreItem(Context, Item, c, p, 0.15d, 1d, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
            p?.Finish();
            Stage?.Invoke("完成");
            Logger.Info($"已使用备份还原实例 {Context.InstanceName}：{Item.FileName}");
        }

        /// <summary>
        /// 还原「导入包备份」：清空该实例的全部内容，再按留档的原始整合包走一遍完整安装（Mod、资源、游戏本体全部重新下载）。
        /// 与快照还原的区别：这里不做任何文件回放，相当于把实例删掉重装一遍。
        /// </summary>
        public static void RestoreFromImportPack(BackupContext Context, BackupItem Item, CancellationToken c, ProgressProvider p, Action<string> Stage)
        {
            string Pack = Item.PackFile;
            if (string.IsNullOrEmpty(Pack) || !FileUtils.Exists(Pack))
                throw new FileNotFoundException("导入包原文件已丢失，无法按整合包还原：" + (Pack ?? "null"));

            Stage?.Invoke("清理该实例的现有内容");
            p?.Set(0.02d);
            ClearScope(Context, BackupScope.Full, c);
            p?.Set(0.10d);

            Stage?.Invoke("按导入包重新下载并安装（这一步会联网，耗时较长）");
            var Loader = ModModpack.ModpackInstall(Pack, Context.InstanceName);
            while (Loader.State == ModBase.LoadState.Loading || Loader.State == ModBase.LoadState.Waiting)
            {
                c.ThrowIfCancellationRequested();
                p?.Set(0.10d + 0.88d * Math.Min(1d, Loader.Progress));
                Thread.Sleep(150);
            }
            if (Loader.State != ModBase.LoadState.Finished)
                throw new Exception("按导入包重新安装失败：" + (Loader.Error?.Message ?? Loader.State.ToString()));

            Stage?.Invoke("完成");
            p?.Set(1d);
            p?.Finish();
            Logger.Info($"已按导入包还原实例 {Context.InstanceName}：{Item.FileName}");
        }

        #region 分阶段还原（供任务管理器逐步显示「完成 → 下一阶段」）

        /// <summary>阶段：创建还原前兜底备份（智能备份关闭时直接跳过）。</summary>
        public static BackupItem PreRestoreStep(BackupContext Context, CancellationToken c, ProgressProvider p)
        {
            return EnsurePreRestore(Context, c, p, 0d, 1d);
        }

        /// <summary>阶段：清空并回放快照备份的文件。</summary>
        public static void RestoreSnapshotStep(BackupContext Context, BackupItem Item, CancellationToken c, ProgressProvider p)
        {
            if (!FileUtils.Exists(Item.FilePath))
                throw new FileNotFoundException("备份文件不存在：" + Item.FilePath);
            RestoreItem(Context, Item, c, p, 0d, 1d, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
            p?.Finish();
            Logger.Info($"已使用备份还原实例 {Context.InstanceName}：{Item.FileName}");
        }

        /// <summary>阶段：清空该实例的现有内容（按导入包还原的第一步）。</summary>
        public static void ClearForImportStep(BackupContext Context, BackupItem Item, CancellationToken c)
        {
            string Pack = Item.PackFile;
            if (string.IsNullOrEmpty(Pack) || !FileUtils.Exists(Pack))
                throw new FileNotFoundException("导入包原文件已丢失，无法按整合包还原：" + (Pack ?? "null"));
            ClearScope(Context, BackupScope.Full, c);
        }

        /// <summary>阶段：按导入包重新下载安装（会联网，耗时由网速决定）。</summary>
        public static void InstallForImportStep(BackupContext Context, BackupItem Item, CancellationToken c, ProgressProvider p)
        {
            string Pack = Item.PackFile;
            if (string.IsNullOrEmpty(Pack) || !FileUtils.Exists(Pack))
                throw new FileNotFoundException("导入包原文件已丢失，无法按整合包还原：" + (Pack ?? "null"));
            var Loader = ModModpack.ModpackInstall(Pack, Context.InstanceName);
            while (Loader.State == ModBase.LoadState.Loading || Loader.State == ModBase.LoadState.Waiting)
            {
                c.ThrowIfCancellationRequested();
                p?.Set(Math.Min(1d, Loader.Progress));
                Thread.Sleep(150);
            }
            if (Loader.State != ModBase.LoadState.Finished)
                throw new Exception("按导入包重新安装失败：" + (Loader.Error?.Message ?? Loader.State.ToString()));
            p?.Finish();
            Logger.Info($"已按导入包还原实例 {Context.InstanceName}：{Item.FileName}");
        }

        /// <summary>找出最新的初始基准包（没有则返回 null）。</summary>
        public static BackupItem FindInitial(BackupContext Context)
        {
            return ListBackups(Context)
                .Where(i => i.Kind == BackupKind.Initial)
                .OrderByDescending(i => i.CreatedAt)
                .FirstOrDefault();
        }

        #endregion

        /// <summary>智能备份开关：开启时还原前自动做一次完整兜底，10 分钟后降级为整合包备份以释放空间。</summary>
        public static bool SmartBackupEnabled
        {
            get { return ModBase.ReadIni("Setup", "SmartBackupDisabled", "False") == "False"; }
            set { ModBase.WriteIni("Setup", "SmartBackupDisabled", value ? "False" : "True"); }
        }

        /// <summary>还原前兜底：智能备份关闭时不做；开启时先做完整备份，10 分钟后自动换成体积更小的整合包备份。</summary>
        private static BackupItem EnsurePreRestore(BackupContext Context, CancellationToken c, ProgressProvider p, double From, double To)
        {
            if (!SmartBackupEnabled)
            {
                Logger.Info("智能备份已关闭，跳过还原前兜底");
                return null;
            }
            var Item = Create(Context, BackupKind.PreRestore, BackupScope.Full, "还原前兜底（完整，10 分钟后降级为整合包备份）", false, null, c, p, From, To);
            ScheduleDegradeToModpack(Context, Item);
            return Item;
        }

        /// <summary>10 分钟后把这次还原前的完整兜底换成整合包范围的兜底，避免长期占着整包体积。</summary>
        private static void ScheduleDegradeToModpack(BackupContext Context, BackupItem FullItem)
        {
            if (FullItem is null)
                return;
            string Folder = Context.BackupRoot;
            ModBase.RunInThread(() =>
            {
                Thread.Sleep(TimeSpan.FromMinutes(PreRestoreThresholdMinutes));
                try
                {
                    if (!DirectoryUtils.Exists(Folder))
                        return;
                    if (!FileUtils.Exists(FullItem.FilePath))
                        return;
                    Logger.Info($"降级还原前兜底备份：{FullItem.FileName}");
                    FileUtils.Delete(FullItem.FilePath);
                    FileUtils.Delete(FullItem.ManifestPath);
                    Create(Context, BackupKind.PreRestore, BackupScope.Modpack, "还原前兜底（整合包范围）", false, null, default, new ProgressProvider(), 0d, 1d);
                }
                catch (Exception ex)
                {
                    Logger.Warn(ex, "降级还原前兜底备份失败");
                }
            });
        }

        /// <summary>清理 pre-restore 下超过阈值的完整 zip 兜底（整包兜底只做短期安全网）。</summary>
        private static void CleanupStalePreRestore(BackupContext Context)
        {
            var Now = DateTime.Now;
            foreach (var Item in ListBackups(Context))
            {
                if (Item.Kind != BackupKind.PreRestore || Item.Scope != BackupScope.Full || Item.Locked)
                    continue;
                if ((Now - Item.CreatedAt).TotalMinutes <= PreRestoreThresholdMinutes)
                    continue;
                try
                {
                    Logger.Info($"清理超时的还原前兜底备份：{Item.FileName}");
                    FileUtils.Delete(Item.FilePath);
                    FileUtils.Delete(Item.ManifestPath);
                }
                catch (Exception ex)
                {
                    Logger.Warn(ex, "清理超时的还原前兜底备份失败：" + Item.FileName);
                }
            }
        }

        private static void RestoreItem(BackupContext Context, BackupItem Item, CancellationToken c, ProgressProvider p, double From, double To, HashSet<string> Visited)
        {
            c.ThrowIfCancellationRequested();
            if (!Visited.Add(Item.FileName))
                throw new InvalidOperationException("增量备份的基准链出现循环：" + Item.FileName);
            if (Item.IsIncremental || !string.IsNullOrEmpty(Item.BaseFile))
            {
                var Base = Item.BaseFile is null ? null : FindByFileName(Context, Item.BaseFile);
                if (Base is null)
                    throw new InvalidOperationException("增量备份的基准已丢失，无法还原：" + Item.FileName);
                double Split = From + (To - From) * 0.6d;
                RestoreItem(Context, Base, c, p, From, Split, Visited);
                ApplyIncremental(Context, Item, c, p, Split, To);
            }
            else
            {
                double Split = From + (To - From) * 0.25d;
                ClearScope(Context, Item.Scope, c);
                p?.Set(Split);
                ExtractZip(Item.FilePath, Context.ContentRoot, c, p, Split, To);
            }
        }

        /// <summary>应用增量包：覆盖变更文件并删除清单中记录的已删除文件。</summary>
        private static void ApplyIncremental(BackupContext Context, BackupItem Item, CancellationToken c, ProgressProvider p, double From, double To)
        {
            ExtractZip(Item.FilePath, Context.ContentRoot, c, p, From, From + (To - From) * 0.85d);
            foreach (var RelativePath in Item.Deleted)
            {
                c.ThrowIfCancellationRequested();
                var FullPath = SafeCombine(Context.ContentRoot, RelativePath);
                if (FullPath is null)
                    continue;
                try
                {
                    if (FileUtils.Exists(FullPath))
                        FileUtils.Delete(FullPath);
                }
                catch (Exception ex)
                {
                    Logger.Warn(ex, "删除增量备份记录的文件失败：" + RelativePath);
                }
            }
            p?.Set(To);
        }

        /// <summary>清空目标范围内的文件，backups 目录与临时目录永不清理。</summary>
        private static void ClearScope(BackupContext Context, BackupScope Scope, CancellationToken c)
        {
            var Root = PathUtils.AddSlashSuffix(Context.ContentRoot);
            if (!DirectoryUtils.Exists(Root))
                return;
            if (Scope == BackupScope.Modpack)
            {
                foreach (var Folder in ModpackFolders)
                {
                    c.ThrowIfCancellationRequested();
                    var Target = Path.Combine(Root, Folder);
                    if (DirectoryUtils.Exists(Target))
                        DirectoryUtils.Delete(Target, false);
                }
                foreach (var File in ModpackFiles)
                {
                    var Target = Path.Combine(Root, File);
                    if (FileUtils.Exists(Target))
                        FileUtils.Delete(Target);
                }
                return;
            }
            foreach (var Dir in DirectoryUtils.EnumerateDirectories(Root))
            {
                c.ThrowIfCancellationRequested();
                string DirName = Path.GetFileName(Dir);
                if (IsExcludedDirectory(DirName) || Context.IsGlobalRoot && GlobalExcludedDirectoryNames.Contains(DirName))
                {
                    Logger.Info($"还原时跳过目录：{DirName}");
                    continue;
                }
                try
                {
                    DirectoryUtils.Delete(Dir, false);
                }
                catch (Exception ex)
                {
                    Logger.Warn(ex, "还原前清理目录失败：" + Dir);
                }
            }
            foreach (var FilePath in DirectoryUtils.EnumerateFiles(Root))
            {
                c.ThrowIfCancellationRequested();
                if (IsExcludedFile(Path.GetFileName(FilePath)))
                    continue;
                try
                {
                    FileUtils.Delete(FilePath);
                }
                catch (Exception ex)
                {
                    Logger.Warn(ex, "还原前清理文件失败：" + FilePath);
                }
            }
        }

        /// <endregion>

        #endregion

        #region 文件枚举与压缩

        /// <summary>按范围枚举实例内容根下的文件。</summary>
        public static List<BackupFileEntry> EnumerateFiles(BackupContext Context, BackupScope Scope, CancellationToken c)
        {
            var Result = new List<BackupFileEntry>();
            var Root = PathUtils.AddSlashSuffix(Context.ContentRoot);
            if (!DirectoryUtils.Exists(Root))
                return Result;
            if (Scope == BackupScope.Modpack)
            {
                foreach (var Folder in ModpackFolders)
                {
                    string Target = Path.Combine(Root, Folder);
                    if (DirectoryUtils.Exists(Target))
                        Walk(Target, Root, Context.IsGlobalRoot, Result, c, false);
                }
                foreach (var File in ModpackFiles)
                {
                    string Target = Path.Combine(Root, File);
                    if (FileUtils.Exists(Target))
                        AddFile(Target, Root, Result);
                }
            }
            else
            {
                Walk(Root, Root, Context.IsGlobalRoot, Result, c, true);
            }
            return Result;
        }

        private static void Walk(string Folder, string Root, bool IsGlobalRoot, List<BackupFileEntry> Output, CancellationToken c, bool IsTopLevel)
        {
            foreach (var SubFolder in DirectoryUtils.EnumerateDirectories(Folder))
            {
                c.ThrowIfCancellationRequested();
                string Name = Path.GetFileName(SubFolder);
                if (IsExcludedDirectory(Name))
                    continue;
                if (IsGlobalRoot && IsTopLevel && GlobalExcludedDirectoryNames.Contains(Name))
                    continue;
                Walk(SubFolder, Root, IsGlobalRoot, Output, c, false);
            }
            foreach (var File in DirectoryUtils.EnumerateFiles(Folder))
            {
                c.ThrowIfCancellationRequested();
                if (IsExcludedFile(Path.GetFileName(File)))
                    continue;
                AddFile(File, Root, Output);
            }
        }

        private static void AddFile(string FilePath, string Root, List<BackupFileEntry> Output)
        {
            try
            {
                var Info = FileUtils.GetInfo(FilePath);
                if (!Info.Exists)
                    return;
                Output.Add(new BackupFileEntry()
                {
                    RelativePath = Path.GetRelativePath(Root, FilePath).Replace('\\', '/'),
                    Size = Info.Length,
                    LastWriteTime = Info.LastWriteTime
                });
            }
            catch (Exception ex)
            {
                Logger.Warn(ex, "读取文件信息失败，已跳过：" + FilePath);
            }
        }

        private static bool IsExcludedDirectory(string Name)
        {
            return ExcludedDirectoryNames.Contains(Name);
        }

        private static bool IsExcludedFile(string Name)
        {
            if (Name.EndsWithF(".log"))
                return true;
            if (Name.StartsWithF("hs_err_pid", true) && Name.EndsWithF(".log", true))
                return true;
            return false;
        }

        /// <summary>写出 zip，返回实际写入的文件清单（读取失败的条目会被跳过）。</summary>
        private static List<BackupFileEntry> WriteZip(string ZipPath, BackupContext Context, List<BackupFileEntry> Source, CancellationToken c, ProgressProvider p, double From, double To)
        {
            var Added = new List<BackupFileEntry>(Source.Count);
            long Total = Math.Max(1L, Source.Sum(f => Math.Max(f.Size, 1L)));
            long Done = 0L;
            DirectoryUtils.Create(Path.GetDirectoryName(ZipPath));
            using (var Stream = new FileStream(ZipPath, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var Archive = new ZipArchive(Stream, ZipArchiveMode.Create, true))
            {
                foreach (var Entry in Source)
                {
                    c.ThrowIfCancellationRequested();
                    string SourcePath = Path.Combine(Context.ContentRoot, Entry.RelativePath.Replace('/', Path.DirectorySeparatorChar));
                    try
                    {
                        if (!FileUtils.Exists(SourcePath))
                            continue;
                        var ZipEntry = Archive.CreateEntry(Entry.RelativePath, CompressionLevel.Optimal);
                        using (var Input = File.OpenRead(SourcePath))
                        using (var Output = ZipEntry.Open())
                            Input.CopyTo(Output, 64 * 1024);
                        Added.Add(Entry);
                    }
                    catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                    {
                        Logger.Warn(ex, "文件被占用或无法读取，已跳过：" + Entry.RelativePath);
                    }
                    Done += Math.Max(Entry.Size, 1L);
                    p?.Set(From + (To - From) * Math.Min(1d, (double)Done / Total));
                }
            }
            return Added;
        }

        /// <summary>解压 zip 到内容根，校验路径不越界，跳过 backups 与临时目录。</summary>
        private static void ExtractZip(string ZipPath, string ContentRoot, CancellationToken c, ProgressProvider p, double From, double To)
        {
            var Root = PathUtils.AddSlashSuffix(ContentRoot);
            DirectoryUtils.Create(Root);
            using (var Archive = ZipFile.OpenRead(ZipPath))
            {
                long Total = Math.Max(1L, Archive.Entries.Sum(e => Math.Max(e.Length, 1L)));
                long Done = 0L;
                foreach (var Entry in Archive.Entries)
                {
                    c.ThrowIfCancellationRequested();
                    string Relative = Entry.FullName.Replace('\\', '/');
                    if (string.IsNullOrWhiteSpace(Relative))
                        continue;
                    string FirstSegment = Relative.Split('/')[0];
                    if (IsExcludedDirectory(FirstSegment))
                    {
                        Logger.Info($"解压时跳过受保护目录：{Relative}");
                        continue;
                    }
                    var Target = SafeCombine(Root, Relative);
                    if (Target is null)
                    {
                        Logger.Warn("备份中存在非法路径，已跳过：" + Relative);
                        continue;
                    }
                    try
                    {
                        if (Entry.Name.Length == 0)
                        {
                            DirectoryUtils.Create(Target);
                        }
                        else
                        {
                            DirectoryUtils.Create(Path.GetDirectoryName(Target));
                            using (var Input = Entry.Open())
                            using (var Output = new FileStream(Target, FileMode.Create, FileAccess.Write, FileShare.None))
                                Input.CopyTo(Output, 64 * 1024);
                            try
                            {
                                File.SetLastWriteTime(Target, Entry.LastWriteTime.LocalDateTime);
                            }
                            catch (Exception)
                            {
                                // 修改时间设置失败不影响内容还原
                            }
                        }
                    }
                    catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                    {
                        Logger.Warn(ex, "解压文件失败：" + Relative);
                    }
                    Done += Math.Max(Entry.Length, 1L);
                    p?.Set(From + (To - From) * Math.Min(1d, (double)Done / Total));
                }
            }
            p?.Set(To);
        }

        /// <summary>把相对路径安全地拼到根目录下；越界或指向受保护目录时返回 null。</summary>
        private static string SafeCombine(string Root, string RelativePath)
        {
            string Rel = (RelativePath ?? "").Replace('\\', '/').TrimStart('/');
            if (Rel.Length == 0 || Rel.Split('/').Any(s => s == ".."))
                return null;
            string FullRoot = Path.GetFullPath(Root);
            if (!FullRoot.EndsWith(Path.DirectorySeparatorChar))
                FullRoot += Path.DirectorySeparatorChar;
            string FullTarget = Path.GetFullPath(Path.Combine(FullRoot, Rel.Replace('/', Path.DirectorySeparatorChar)));
            if (!FullTarget.StartsWith(FullRoot, StringComparison.OrdinalIgnoreCase))
                return null;
            return FullTarget;
        }

        #endregion

        #region 增量基准

        /// <summary>取该实例内最近的、可作为增量基准的完整快照（初始基准 / 完整 zip / 增量链）。</summary>
        public static BackupItem FindBase(BackupContext Context)
        {
            return ListBackups(Context)
                .Where(i => i.HasManifest
                            && (i.Kind == BackupKind.Initial || i.Kind == BackupKind.FullZip || i.Kind == BackupKind.Incremental)
                            && i.Scope == BackupScope.Full
                            && i.Files.Count > 0)
                .OrderByDescending(i => i.CreatedAt)
                .FirstOrDefault();
        }

        /// <summary>该实例是否具备增量备份条件。</summary>
        public static bool CanCreateIncremental(BackupContext Context)
        {
            return FindBase(Context) is not null;
        }

        private static BackupItem FindByFileName(BackupContext Context, string FileName)
        {
            return ListBackups(Context).FirstOrDefault(i => string.Equals(i.FileName, FileName, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>把一份备份（可能是一条增量链）还原为「那一刻的完整文件清单」。</summary>
        private static Dictionary<string, BackupFileEntry> ResolveSnapshot(BackupContext Context, BackupItem Item, HashSet<string> Visited)
        {
            var Map = new Dictionary<string, BackupFileEntry>(StringComparer.OrdinalIgnoreCase);
            if (!Item.HasManifest)
                throw new InvalidOperationException("备份缺少清单，无法作为增量基准：" + Item.FileName);
            if (!Item.IsIncremental)
            {
                foreach (var File in Item.Files)
                    Map[File.RelativePath] = File;
                return Map;
            }
            if (!Visited.Add(Item.FileName))
                throw new InvalidOperationException("增量备份的基准链出现循环：" + Item.FileName);
            var Base = Item.BaseFile is null ? null : FindByFileName(Context, Item.BaseFile);
            if (Base is null)
                throw new InvalidOperationException("增量备份的基准已丢失：" + Item.FileName);
            Map = ResolveSnapshot(Context, Base, Visited);
            foreach (var File in Item.Files)
                Map[File.RelativePath] = File;
            foreach (var Path in Item.Deleted)
                Map.Remove(Path);
            return Map;
        }

        private static bool IsSameTime(DateTime A, DateTime B)
        {
            return Math.Abs((A - B).TotalSeconds) < 1d;
        }

        #endregion

        #region 备注 / 删除

        /// <summary>修改备份备注，同步写入清单。</summary>
        public static void SetRemark(BackupItem Item, string Remark)
        {
            Item.Remark = Remark ?? "";
            if (Item.HasManifest)
                WriteManifest(Item);
        }

        /// <summary>删除备份；初始基准等锁定项拒绝删除。</summary>
        public static void Delete(BackupItem Item)
        {
            if (Item.Locked)
                throw new InvalidOperationException("初始基准备份已锁定，不能删除。");
            FileUtils.Delete(Item.FilePath);
            if (FileUtils.Exists(Item.ManifestPath))
                FileUtils.Delete(Item.ManifestPath);
        }

        #endregion

        #region 清单写入

        private static void WriteManifest(BackupItem Item)
        {
            var Json = new JObject()
            {
                ["Version"] = 1,
                ["Kind"] = Item.Kind.Text(),
                ["Scope"] = Item.Scope == BackupScope.Modpack ? "modpack" : "full",
                ["Instance"] = Item.InstanceName,
                ["CreatedAt"] = Item.CreatedAt.ToString("o"),
                ["Remark"] = Item.Remark,
                ["Locked"] = Item.Locked,
                ["Base"] = Item.BaseFile is null ? JValue.CreateNull() : new JValue(Item.BaseFile)
            };
            var Files = new JArray();
            foreach (var File in Item.Files)
            {
                Files.Add(new JObject()
                {
                    ["p"] = File.RelativePath,
                    ["s"] = File.Size,
                    ["t"] = File.LastWriteTime.ToString("o")
                });
            }
            Json["Files"] = Files;
            var Deleted = new JArray();
            foreach (var Path in Item.Deleted)
                Deleted.Add(Path);
            Json["Deleted"] = Deleted;
            FileUtils.WriteAtomic(Item.ManifestPath, Json.ToString(Newtonsoft.Json.Formatting.Indented), new UTF8Encoding(false));
        }

        #endregion

        #region 命名

        private static string BuildFileName(BackupContext Context, BackupKind Kind, DateTime Time, string Remark)
        {
            var Name = $"{Sanitize(Context.InstanceName)}_{KindTagOf(Kind)}_{Time:yyyyMMdd-HHmmss}";
            if (!string.IsNullOrWhiteSpace(Remark))
                Name += "_" + Sanitize(Remark).TrimEnd('_');
            return Name;
        }

        private static string KindTagOf(BackupKind Kind)
        {
            switch (Kind)
            {
                case BackupKind.Initial:
                    return "初始";
                case BackupKind.FullZip:
                    return "完整";
                case BackupKind.Modpack:
                    return "整合包";
                case BackupKind.Incremental:
                    return "增量";
                default:
                    return "还原前";
            }
        }

        private static string Sanitize(string Text)
        {
            var Result = new StringBuilder();
            foreach (char Char in (Text ?? "").Trim())
            {
                if (Path.GetInvalidFileNameChars().Contains(Char) || Char == ' ')
                    Result.Append('_');
                else
                    Result.Append(Char);
                if (Result.Length >= 40)
                    break;
            }
            var Value = Result.ToString().Trim('_');
            return string.IsNullOrEmpty(Value) ? "实例" : Value;
        }

        #endregion

    }

    internal static class ModBackupKindExtensions
    {
        internal static string Text(this ModBackup.BackupKind Kind)
        {
            switch (Kind)
            {
                case ModBackup.BackupKind.Initial:
                    return "initial";
                case ModBackup.BackupKind.FullZip:
                    return "full-zip";
                case ModBackup.BackupKind.Modpack:
                    return "modpack";
                case ModBackup.BackupKind.Incremental:
                    return "incremental";
                default:
                    return "pre-restore";
            }
        }
    }
}
