using System;
using System.Collections.Generic;
using System.IO;
using MeloongCore;
using PCLCS;

namespace PCL
{
    public static partial class ModDownloadLib
    {

        #region P4-B 编排装配层（DTO → LoaderBase）

        /// <summary>
    /// 合并安装装配层的宿主上下文：只保存 VB 侧资产与请求，不含任何编排决策。
    /// 步骤是否出现、顺序、名称、权重、Block、Show、输出槽位与下载类别
    /// 全部来自 <see cref="PCL.Download.InstallTaskDescriptor"/>。
    /// </summary>
    /// <remarks>
    /// 本类型是 Partial Module 的一部分，与 ModDownloadLib.vb 共享 Private 宿主函数
    /// （McDownloadClientLoader / McDownloadForgelikeLoader / MergeJson 等）。
    /// 口径 A 下这些函数仍属于 VB 执行层，装配层只负责调用它们。
    /// </remarks>
        internal class InstallLoaderContext
        {
            public McInstallRequest Request;
            public Download.InstallPlan Plan;
            public string TempMcFolder;
            public Download.InstallLayout Layout;
            public ModDownload.DlOptiFineListEntry OptiFineEntry;
            public string ForgeVersion;
            public string NeoForgeVersion;
            public string LiteLoaderVersion;
            public string FabricVersion;
            public string VersionFolder;
            public string ModsTempFolder;
            public string OptiFineFolder;
            public string ForgeFolder;
            public string NeoForgeFolder;
            public string LiteLoaderFolder;
            public string FabricFolder;
            public bool OptiFineAsMod;
            public bool Modable;
            public string MinecraftFolder;
            public Dictionary<string, string> ManagedFileSources;
            public ModLoader.LoaderCombo<string> ClientLoader;
            public string TargetFolder = null;
        }

        /// <summary>
    /// P4-B 唯一的 DTO → LoaderBase 装配处：遍历 C# 产出的描述符，按 Kind 分派到具名装配方法。
    /// 装配层只做三件事——读描述符、构造对应 LoaderBase 子类、挂上 VB 侧执行 lambda；
    /// 不得出现「If 组件已选 Then」形式的业务判定（步骤是否已包含由 InstallPlanner.Build 决定）。
    /// </summary>
        internal static List<ModLoader.LoaderBase> LoaderPlanAssemble(InstallLoaderContext Ctx)
        {
            var LoaderList = new List<ModLoader.LoaderBase>();
            foreach (var D in Download.InstallTaskPlanner.Describe(Ctx.Plan))
            {
                ModLoader.LoaderBase Loader = null;
                switch (D.Kind.Value)
                {
                    case Download.InstallStepKind.IgnoreMarker:
                        {
                            Loader = AssembleIgnoreMarker(D, Ctx);
                            break;
                        }
                    case Download.InstallStepKind.FabricApiDownload:
                        {
                            Loader = AssembleFabricApi(D, Ctx);
                            break;
                        }
                    case Download.InstallStepKind.OptiFabricDownload:
                        {
                            Loader = AssembleOptiFabric(D, Ctx);
                            break;
                        }
                    case Download.InstallStepKind.Client:
                        {
                            Loader = AssembleClient(D, Ctx);
                            break;
                        }
                    case Download.InstallStepKind.OptiFine:
                        {
                            Loader = AssembleOptiFine(D, Ctx);
                            break;
                        }
                    case Download.InstallStepKind.Forge:
                        {
                            Loader = AssembleForge(D, Ctx);
                            break;
                        }
                    case Download.InstallStepKind.NeoForge:
                        {
                            Loader = AssembleNeoForge(D, Ctx);
                            break;
                        }
                    case Download.InstallStepKind.LiteLoader:
                        {
                            Loader = AssembleLiteLoader(D, Ctx);
                            break;
                        }
                    case Download.InstallStepKind.Fabric:
                        {
                            Loader = AssembleFabric(D, Ctx);
                            break;
                        }
                    case Download.InstallStepKind.InstallGame:
                        {
                            Loader = AssembleInstallGame(D, Ctx);
                            break;
                        }
                    case Download.InstallStepKind.PostLibraries:
                        {
                            Loader = AssemblePostLibraries(D, Ctx);
                            break;
                        }
                    case Download.InstallStepKind.RecordManifest:
                        {
                            Loader = AssembleRecordManifest(D, Ctx);
                            break;
                        }
                    case Download.InstallStepKind.RemoveIgnoreMarker:
                        {
                            Loader = AssembleRemoveIgnoreMarker(D, Ctx);
                            break;
                        }

                    default:
                        {
                            throw new InvalidOperationException("未知的装配步骤种类：" + D.Kind.Value.ToString());
                        }
                }
                LoaderList.Add(Loader);
            }
            return LoaderList;
        }

        /// <summary>描述符是否声明了某个输出槽位（用 DTO 决策代替装配层业务判定）。</summary>
        private static bool DescriptorHasOutput(Download.InstallTaskDescriptor D, string Name)
        {
            foreach (var Item in D.Outputs)
            {
                if (string.Equals(Item, Name, StringComparison.Ordinal))
                    return true;
            }
            return false;
        }

        private static ModLoader.LoaderBase AssembleIgnoreMarker(Download.InstallTaskDescriptor D, InstallLoaderContext Ctx)
        {
            return new ModLoader.LoaderTask<int, int>(D.Name, (_) => FileUtils.Write(Ctx.VersionFolder + ".pclignore", "用于临时地在 PCL Next 的版本列表中屏蔽此版本。")) { ProgressWeight = D.ProgressWeight, Show = D.Show, Block = D.Block };
        }

        private static ModLoader.LoaderBase AssembleFabricApi(Download.InstallTaskDescriptor D, InstallLoaderContext Ctx)
        {
            var DownloadFile = Ctx.Request.FabricApi.ToNetFile(Ctx.ModsTempFolder, ResourceVersion.DownloadReason.Dependency, Ctx.Request.Components.MinecraftVersion, ModLoaders.Fabric);
            Ctx.ManagedFileSources.Add("FabricApi", DownloadFile.LocalPath);
            return new ModNet.LoaderDownload(D.Name, new List<ModNet.NetFile>() { DownloadFile }) { ProgressWeight = D.ProgressWeight, Show = D.Show, Block = D.Block };
        }

        private static ModLoader.LoaderBase AssembleOptiFabric(Download.InstallTaskDescriptor D, InstallLoaderContext Ctx)
        {
            var DownloadFile = Ctx.Request.OptiFabric.ToNetFile(Ctx.ModsTempFolder, ResourceVersion.DownloadReason.Dependency, Ctx.Request.Components.MinecraftVersion, ModLoaders.Fabric);
            Ctx.ManagedFileSources.Add("OptiFabric", DownloadFile.LocalPath);
            return new ModNet.LoaderDownload(D.Name, new List<ModNet.NetFile>() { DownloadFile }) { ProgressWeight = D.ProgressWeight, Show = D.Show, Block = D.Block };
        }

        /// <summary>Client 复合体：子加载器列表仍由 VB 宿主函数提供（SubSteps 已由 C# 描述，逐类下沉属 P4-B 后续批次）。</summary>
        private static ModLoader.LoaderBase AssembleClient(Download.InstallTaskDescriptor D, InstallLoaderContext Ctx)
        {
            var Loader = new ModLoader.LoaderCombo<string>(D.Name, McDownloadClientLoader(Ctx.Request.Components.MinecraftVersion, Ctx.Request.MinecraftJson, Ctx.VersionFolder)) { ProgressWeight = D.ProgressWeight, Show = D.Show, Block = D.Block };
            Ctx.ClientLoader = Loader;
            return Loader;
        }

        /// <summary>OptiFine 双形态由 DTO 的 SubStepsSource 决定，装配层不自行判断「作为 Mod」。</summary>
        private static ModLoader.LoaderBase AssembleOptiFine(Download.InstallTaskDescriptor D, InstallLoaderContext Ctx)
        {
            string InstallerPath = Ctx.Layout.BuildOptiFineInstallerPath(Ctx.OptiFineEntry.FileName);
            if (DescriptorHasOutput(D, "ManagedFileSources.OptiFine"))
            {
                Ctx.ManagedFileSources.Add("OptiFine", InstallerPath);
            }
            List<ModLoader.LoaderBase> Loaders;
            switch (D.SubStepsSource ?? "")
            {
                case "McDownloadOptiFineSaveLoader":
                    {
                        Loaders = McDownloadOptiFineSaveLoader(Ctx.OptiFineEntry, InstallerPath);
                        break;
                    }
                case "McDownloadOptiFineLoader":
                    {
                        Loaders = McDownloadOptiFineLoader(Ctx.OptiFineEntry, Ctx.TempMcFolder, Ctx.ClientLoader, Ctx.VersionFolder);
                        break;
                    }

                default:
                    {
                        throw new InvalidOperationException("OptiFine 装配规格缺少已知的 SubStepsSource");
                    }
            }
            return new ModLoader.LoaderCombo<string>(D.Name, Loaders) { ProgressWeight = D.ProgressWeight, Show = D.Show, Block = D.Block };
        }

        private static ModLoader.LoaderBase AssembleForge(Download.InstallTaskDescriptor D, InstallLoaderContext Ctx)
        {
            var Loaders = McDownloadForgelikeLoader(false, Ctx.ForgeVersion, "forge-" + Ctx.ForgeVersion, Ctx.Request.Components.MinecraftVersion, Ctx.Request.ForgeEntry, Ctx.TempMcFolder, Ctx.ClientLoader, Ctx.VersionFolder);
            return new ModLoader.LoaderCombo<string>(D.Name, Loaders) { ProgressWeight = D.ProgressWeight, Show = D.Show, Block = D.Block };
        }

        private static ModLoader.LoaderBase AssembleNeoForge(Download.InstallTaskDescriptor D, InstallLoaderContext Ctx)
        {
            var Loaders = McDownloadForgelikeLoader(true, Ctx.NeoForgeVersion, "neoforge-" + Ctx.NeoForgeVersion, Ctx.Request.Components.MinecraftVersion, Ctx.Request.NeoForgeEntry, Ctx.TempMcFolder, Ctx.ClientLoader, Ctx.VersionFolder);
            return new ModLoader.LoaderCombo<string>(D.Name, Loaders) { ProgressWeight = D.ProgressWeight, Show = D.Show, Block = D.Block };
        }

        private static ModLoader.LoaderBase AssembleLiteLoader(Download.InstallTaskDescriptor D, InstallLoaderContext Ctx)
        {
            var Loaders = McDownloadLiteLoaderLoader(Ctx.Request.LiteLoaderEntry, Ctx.TempMcFolder);
            return new ModLoader.LoaderCombo<string>(D.Name, Loaders) { ProgressWeight = D.ProgressWeight, Show = D.Show, Block = D.Block };
        }

        private static ModLoader.LoaderBase AssembleFabric(Download.InstallTaskDescriptor D, InstallLoaderContext Ctx)
        {
            var Loaders = McDownloadFabricLoader(Ctx.FabricVersion, Ctx.Request.Components.MinecraftVersion, Ctx.TempMcFolder);
            return new ModLoader.LoaderCombo<string>(D.Name, Loaders) { ProgressWeight = D.ProgressWeight, Show = D.Show, Block = D.Block };
        }

        private static ModLoader.LoaderBase AssembleInstallGame(Download.InstallTaskDescriptor D, InstallLoaderContext Ctx)
        {
            return new ModLoader.LoaderTask<string, string>(D.Name, (Task) =>
        {
            // 合并 JSON：将多个版本 JSON 进行合并，如果已存在则直接覆盖
            MergeJson(Ctx.VersionFolder, Ctx.OptiFineFolder, Ctx.OptiFineAsMod, Ctx.ForgeFolder, Ctx.NeoForgeFolder, Ctx.FabricFolder, Ctx.LiteLoaderFolder);
            Task.Progress = 0.2d;
            // 迁移文件
            DirectoryUtils.Copy(Ctx.TempMcFolder + "libraries", Ctx.MinecraftFolder + "libraries");
            Task.Progress = 0.8d;
            // 创建 Mod 和资源包文件夹
            Ctx.TargetFolder = Ctx.Request.IsIntermediateOutput ? Ctx.VersionFolder : new ModMinecraft.McInstance(Ctx.VersionFolder).PathIndie; // 版本隔离信息在此时被决定
            string ModsFolder = Ctx.TargetFolder + @"mods\";
            if (DirectoryUtils.Exists(Ctx.ModsTempFolder))
            {
                DirectoryUtils.Copy(Ctx.ModsTempFolder, ModsFolder);
            }
            else if (Ctx.Modable)
            {
                DirectoryUtils.Create(ModsFolder);
                Logger.Info($"自动创建 Mod 文件夹：{ModsFolder}");
            }
            string ResourcepacksFolder = Ctx.TargetFolder + @"resourcepacks\";
            DirectoryUtils.Create(ResourcepacksFolder);
            Logger.Info($"自动创建资源包文件夹：{ResourcepacksFolder}");
        })
            {
                ProgressWeight = D.ProgressWeight,
                Block = D.Block,
                Show = D.Show
            };
        }

        /// <summary>补全文件复合体：两个副加载器的名称 / 权重 / Show 取自 DTO 的 SubSteps。</summary>
        private static ModLoader.LoaderBase AssemblePostLibraries(Download.InstallTaskDescriptor D, InstallLoaderContext Ctx)
        {
            var LoadersLib = new List<ModLoader.LoaderBase>();
            var Analysis = D.SubSteps[0];
            var Download = D.SubSteps[1];
            LoadersLib.Add(new ModLoader.LoaderTask<string, List<ModNet.NetFile>>(Analysis.Name, Task => Task.Output = ModMinecraft.McLibNetFilesFromInstance(new ModMinecraft.McInstance(Ctx.VersionFolder))) { ProgressWeight = Analysis.ProgressWeight, Show = Analysis.Show });
            LoadersLib.Add(new ModNet.LoaderDownload(Download.Name, new List<ModNet.NetFile>()) { ProgressWeight = Download.ProgressWeight, Show = Download.Show });
            return new ModLoader.LoaderCombo<string>(D.Name, LoadersLib) { ProgressWeight = D.ProgressWeight, Block = D.Block, Show = D.Show };
        }

        private static ModLoader.LoaderBase AssembleRecordManifest(Download.InstallTaskDescriptor D, InstallLoaderContext Ctx)
        {
            return new ModLoader.LoaderTask<int, int>(D.Name, (_) =>
        {
            var ManagedFiles = new List<McInstallManifest.ManagedFile>();
            // 记录由 PCL 管理的 Mod 信息
            foreach (var Entry in Ctx.ManagedFileSources)
            {
                if (string.IsNullOrWhiteSpace(Ctx.ModsTempFolder) || string.IsNullOrWhiteSpace(Entry.Value))
                    throw new InvalidDataException($"托管 Mod 文件路径不完整：{Entry.Key}");
                string TempModsFolder = Path.GetFullPath(Ctx.ModsTempFolder);
                string SourcePath = Path.GetFullPath(Entry.Value);
                if (!PathUtils.IsParentOf(TempModsFolder, SourcePath) || PathUtils.IsSame(TempModsFolder, SourcePath))
                    throw new InvalidDataException($"托管 Mod 文件不在临时 mods 目录中：{SourcePath}");
                string RelativePath = Path.GetRelativePath(TempModsFolder, SourcePath);
                string FullPath = PathUtils.ForCompare(Path.Combine(Ctx.TargetFolder, "mods", RelativePath));
                ManagedFiles.Add(new McInstallManifest.ManagedFile(Download.InstallManifestRules.ManagedModPath(RelativePath), CryptographyUtils.ComputeFileHash(FullPath, CryptographyUtils.HashMethod.Sha1), FileUtils.GetInfo(FullPath).LastWriteTimeUtc, Entry.Key));
            }
            // 处理加载器版本信息（剥掉 MC 前缀，规则见 PCL.Download.InstallManifestRules）
            var TempLoaderVersions = new Dictionary<ModLoaders, string>(Ctx.Request.Components.ModLoaderVersions);
            if (Ctx.ForgeVersion is not null)
            {
                TempLoaderVersions[ModLoaders.Forge] = Download.InstallManifestRules.NormalizeLoaderVersion((Download.ModLoaders)ModLoaders.Forge, Ctx.ForgeVersion);
            }
            if (Ctx.NeoForgeVersion is not null)
            {
                TempLoaderVersions[ModLoaders.NeoForge] = Download.InstallManifestRules.NormalizeLoaderVersion((Download.ModLoaders)ModLoaders.NeoForge, Ctx.NeoForgeVersion);
            }
            // 存储 Manifest
            var Target = new McComponents(Ctx.Request.Components.MinecraftVersion, TempLoaderVersions, Ctx.Request.Components.OptiFineEntry, Ctx.Request.Components.FabricApiVersion, Ctx.Request.Components.OptiFabricVersion);
            var InstanceConfig = new ModMinecraft.McInstance(Ctx.VersionFolder).Config;
            Configs.McInstallManifest.Set(new McInstallManifest(Versions.McInstallManifestVersion, Target, DateTime.UtcNow, ManagedFiles), InstanceConfig);
            InstanceConfig.Save();
        })
            {
                ProgressWeight = D.ProgressWeight,
                Block = D.Block,
                Show = D.Show
            };
        }

        private static ModLoader.LoaderBase AssembleRemoveIgnoreMarker(Download.InstallTaskDescriptor D, InstallLoaderContext Ctx)
        {
            return new ModLoader.LoaderTask<int, int>(D.Name, (_) => FileUtils.Delete(Ctx.VersionFolder + ".pclignore")) { ProgressWeight = D.ProgressWeight, Block = D.Block, Show = D.Show };
        }

        #endregion

    }
}