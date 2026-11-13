using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using MeloongCore;
using MeloongCore.Extensions;
using MeloongCore.Wpf;
using MeloongCore.Wpf.Extensions;
using Microsoft.VisualBasic;
using Microsoft.VisualBasic.CompilerServices;
using Newtonsoft.Json.Linq;
using PCLCS;

namespace PCL
{
    public static partial class ModDownloadLib
    {

        #region Minecraft 下载

        /// <summary>
    /// 获取下载某个 Minecraft 版本的加载器列表。
    /// </summary>
    /// <param name="VersionFolder">版本文件输出目录。</param>
        private static List<ModLoader.LoaderBase> McDownloadClientLoader(string Id, string JsonUrl, string VersionFolder)
        {
            string InstanceName = PathUtils.GetLastPart(VersionFolder);
            var Loaders = new List<ModLoader.LoaderBase>();
            ModMinecraft.McInstance NewInstance = null;

            // 下载版本 Json 文件
            if (JsonUrl is null)
            {
                Loaders.Add(new ModLoader.LoaderTask<string, List<ModNet.NetFile>>("获取原版 json 文件下载地址", (Task) =>
        {
            string JsonAddress = Conversions.ToString(ModDownload.DlClientListGet(Id));
            Task.Output = new List<ModNet.NetFile>() { new ModNet.NetFile(ModDownload.DlSourceLauncherOrMetaGet(JsonAddress), VersionFolder + InstanceName + ".json") };
        })
                {
                    ProgressWeight = 2d,
                    Show = false
                });
            }
            Loaders.Add(new ModNet.LoaderDownload(McDownloadClientJsonName, new List<ModNet.NetFile>() { new ModNet.NetFile(ModDownload.DlSourceLauncherOrMetaGet(JsonUrl ?? ""), VersionFolder + InstanceName + ".json", new FileChecker() { IsJson = true }) }) { ProgressWeight = 3d });
            // 读取 Json 文件并创建实例
            Loaders.Add(new ModLoader.LoaderTask<string, string>("读取 json 文件", (Task) =>
    {
        NewInstance = new ModMinecraft.McInstance(VersionFolder);
        NewInstance.Load();
    })
            {
                ProgressWeight = 0d,
                Show = false
            });

            // 下载支持库文件
            var LoadersLib = new List<ModLoader.LoaderBase>();
            LoadersLib.Add(new ModLoader.LoaderTask<string, List<ModNet.NetFile>>("分析原版支持库文件（副加载器）", (Task) =>
        {
            Logger.Info($"开始分析原版支持库文件：{VersionFolder}");
            Task.Output = ModMinecraft.McLibNetFilesFromInstance(NewInstance);
        })
            {
                ProgressWeight = 1d,
                Show = false
            });
            LoadersLib.Add(new ModNet.LoaderDownload("下载原版支持库文件（副加载器）", new List<ModNet.NetFile>()) { ProgressWeight = 13d, Show = false });
            Loaders.Add(new ModLoader.LoaderCombo<string>(McDownloadClientLibName, LoadersLib) { Block = false, ProgressWeight = 14d });

            // 下载资源文件
            var LoadersAssets = new List<ModLoader.LoaderBase>();
            LoadersAssets.Add(new ModLoader.LoaderTask<string, List<ModNet.NetFile>>("分析资源文件索引地址（副加载器）", (Task) =>
        {
            try
            {
                var AssetIndex = ModDownload.DlClientAssetIndexGet(NewInstance);
                Task.Output = AssetIndex is null ? new List<ModNet.NetFile>() : new List<ModNet.NetFile>() { AssetIndex };
            }
            catch (Exception ex)
            {
                throw new Exception("分析资源文件索引地址失败", ex);
            }
            // 顺手添加 Json 项目
            try
            {
                JObject InstanceJson = (JObject)FileUtils.ReadAsJson(VersionFolder + InstanceName + ".json");
                InstanceJson["clientVersion"] = Id;
                FileUtils.Write($"{VersionFolder}{InstanceName}.json", InstanceJson.ToString());
            }
            catch (Exception ex)
            {
                throw new Exception("添加客户端版本失败", ex);
            }
        })
            {
                ProgressWeight = 1d,
                Show = false
            });
            LoadersAssets.Add(new ModNet.LoaderDownload("下载资源文件索引（副加载器）", new List<ModNet.NetFile>()) { ProgressWeight = 3d, Show = false });
            LoadersAssets.Add(new ModLoader.LoaderTask<string, List<ModNet.NetFile>>("分析所需资源文件（副加载器）", Task =>
    {
        ModLoader.LoaderBase argProgressFeed = Task;
        Task.Output = ModMinecraft.McAssetsFixList(NewInstance, true, ref argProgressFeed);
        Task = (ModLoader.LoaderTask<string, List<ModNet.NetFile>>)argProgressFeed;
    })
            {
                ProgressWeight = 0.01d,
                Show = false
            });
            LoadersAssets.Add(new ModNet.LoaderDownload("下载资源文件（副加载器）", new List<ModNet.NetFile>()) { ProgressWeight = 14d, Show = false });
            Loaders.Add(new ModLoader.LoaderCombo<string>("下载原版资源文件", LoadersAssets) { Block = false, ProgressWeight = 18d });

            return Loaders;

        }
        private const string McDownloadClientLibName = "下载原版支持库文件";
        private const string McDownloadClientJsonName = "下载原版 json 文件";

        #endregion

        #region Minecraft 下载菜单

        public static MyVirtualizingElement<MyListItem> McDownloadListItem(JObject Entry, MyListItem.ClickEventHandler OnClick, bool IsSaveOnly)
        {
            return new MyVirtualizingElement<MyListItem>(() =>
        {
            // 确定图标
            string Logo;
            // 原 VB 为 Select Case Entry("type")，转换器误把选择子写成常量 "type"，导致下方三个分支被判定为不可达（CS0162）；
            // 这里是转换错误而非死代码，故按原逻辑改为读取 Entry 的 type 字段
            switch (Entry["type"]?.ToString() ?? "")
            {
                case "release":
                    {
                        Logo = ModBase.PathImage + "Blocks/Grass.png";
                        break;
                    }
                case "snapshot":
                    {
                        Logo = ModBase.PathImage + "Blocks/CommandBlock.png";
                        break;
                    }
                case "special":
                    {
                        Logo = ModBase.PathImage + "Blocks/GoldBlock.png";
                        break;
                    }

                default:
                    {
                        Logo = ModBase.PathImage + "Blocks/CobbleStone.png";
                        break;
                    }
            }
            // 建立控件
            var NewItem = new MyListItem() { Logo = Logo, SnapsToDevicePixels = true, Title = Entry["id"]?.ToString() ?? "", Height = 42d, Type = MyListItem.CheckType.Clickable, Tag = Entry };
            if (Entry["lore"] is null)
            {
                JToken rtToken = Entry["releaseTime"];
                DateTime rtDate = default;
                if (rtToken is not null && rtToken.Type != JTokenType.Null)
                {
                    try { rtDate = rtToken.ToObject<DateTime>(); } catch { rtDate = default; }
                }
                NewItem.Info = rtDate == default ? "" : rtDate.ToString("yyyy'/'MM'/'dd HH':'mm");
            }
            else
            {
                NewItem.Info = Entry["lore"].ToString();
            }
            if ((Entry["url"]?.ToString() ?? "").Contains("pcl"))
                NewItem.Info = "[PCL Next 特供下载] " + NewItem.Info;
            NewItem.Click += OnClick;
            // 建立菜单
            if (IsSaveOnly)
            {
                NewItem.ContentHandler = McDownloadSaveMenuBuild;
            }
            else
            {
                NewItem.ContentHandler = McDownloadMenuBuild;
            }
            // 结束
            return NewItem;
        })
            {
                Height = 42d
            };
        }
        private static void McDownloadSaveMenuBuild(object sender, EventArgs e)
        {
            var BtnInfo = new MyIconButton() { LogoScale = 1.05d, Logo = ModBase.Logo.IconButtonInfo, ToolTip = "更新日志" };
            BtnInfo.Click += (_, __) => ModDownloadLib.McDownloadMenuLog((dynamic)_, (dynamic)__);
            var BtnServer = new MyIconButton() { LogoScale = 1d, Logo = ModBase.Logo.IconButtonServer, ToolTip = "下载服务端" };
            BtnServer.Click += (_, __) => ModDownloadLib.McDownloadMenuSaveServer((dynamic)_, (dynamic)__);
            ((dynamic)sender).Buttons = new[] { BtnServer, BtnInfo };
        }
        private static void McDownloadMenuBuild(object sender, EventArgs e)
        {
            var BtnSave = new MyIconButton() { Logo = ModBase.Logo.IconButtonSave, ToolTip = "另存为" };
            BtnSave.Click += (_, __) => ModDownloadLib.McDownloadMenuSave((dynamic)_, (dynamic)__);
            var BtnInfo = new MyIconButton() { LogoScale = 1.05d, Logo = ModBase.Logo.IconButtonInfo, ToolTip = "更新日志" };
            BtnInfo.Click += (_, __) => ModDownloadLib.McDownloadMenuLog((dynamic)_, (dynamic)__);
            var BtnServer = new MyIconButton() { LogoScale = 1d, Logo = ModBase.Logo.IconButtonServer, ToolTip = "下载服务端" };
            BtnServer.Click += (_, __) => ModDownloadLib.McDownloadMenuSaveServer((dynamic)_, (dynamic)__);
            ((dynamic)sender).Buttons = new[] { BtnSave, BtnInfo, BtnServer };
        }
        private static void McDownloadMenuLog(object sender, RoutedEventArgs e)
        {
            JToken Version;
            if (((dynamic)sender).Tag is not null)
            {
                Version = (JToken)((dynamic)sender).Tag;
            }
            else if (((dynamic)sender).Parent.Tag is not null)
            {
                Version = (JToken)((dynamic)sender).Parent.Tag;
            }
            else
            {
                Version = (JToken)((dynamic)sender).Parent.Parent.Tag;
            }
            McUpdateLogShow(Version);
        }
        private static void McDownloadMenuSaveServer(object sender, RoutedEventArgs e)
        {
            MyListItem Version;
            if (sender is MyListItem)
            {
                Version = (MyListItem)sender;
            }
            else if (((dynamic)sender).Parent is MyListItem)
            {
                Version = (MyListItem)((dynamic)sender).Parent;
            }
            else
            {
                Version = (MyListItem)((dynamic)sender).Parent.Parent;
            }
            try
            {
                string Id = Version.Title;
                string JsonUrl = (Version.Tag is JObject menuTag ? menuTag["url"]?.ToString() : null) ?? "";
                string VersionFolder = Dialogs.SelectFolder("选择目标文件夹", false).FirstOrDefault();
                if (VersionFolder is null)
                    return;
                VersionFolder = VersionFolder + Id + @"\";

                // 重复任务检查
                foreach (var OngoingLoader in ModLoader.LoaderTaskbar.ToList())
                {
                    if ((OngoingLoader.Name ?? "") != ($"Minecraft {Id} 服务端下载" ?? ""))
                        continue;
                    ModMain.Hint("该服务端正在下载中！", ModMain.HintType.Red);
                    return;
                }

                var Loaders = new List<ModLoader.LoaderBase>();
                // 下载版本 JSON 文件
                Loaders.Add(new ModNet.LoaderDownload("下载版本 JSON 文件", new List<ModNet.NetFile>() { new ModNet.NetFile(ModDownload.DlSourceLauncherOrMetaGet(JsonUrl), VersionFolder + Id + ".json", new FileChecker() { IsJson = true }) }) { ProgressWeight = 2d });
                // 构建服务端
                Loaders.Add(new ModLoader.LoaderTask<string, List<ModNet.NetFile>>("构建服务端", (Task) =>
        {
            // 分析服务端 JAR 文件下载地址
            var Instance = new ModMinecraft.McInstance(VersionFolder);
            if (Instance.JsonObject["downloads"] is null || Instance.JsonObject["downloads"]["server"] is null || Instance.JsonObject["downloads"]["server"]["url"] is null)
            {
                FileUtils.Delete(VersionFolder + Id + ".json");
                if (!DirectoryUtils.GetInfo(VersionFolder).GetFileSystemInfos().Any())
                    DirectoryUtils.Delete(VersionFolder);
                Task.Output = new List<ModNet.NetFile>();
                ModMain.Hint($"Mojang 没有给 Minecraft {Id} 提供官方服务端下载，没法下，撤退！", ModMain.HintType.Red);
                Thread.Sleep(2000); // 等玩家把上一个提示看完
                Task.Cancel();
                return;
            }
            string JarUrl = (string)Instance.JsonObject["downloads"]["server"]["url"];
            var Checker = new FileChecker() { MinSize = 1024L, ActualSize = (long)(Instance.JsonObject["downloads"]["server"]["size"] ?? -1), Hash = (string)Instance.JsonObject["downloads"]["server"]["sha1"] };
            Task.Output = new List<ModNet.NetFile>() { new ModNet.NetFile(ModDownload.DlSourceLauncherOrMetaGet(JarUrl), VersionFolder + Id + "-server.jar", Checker) };
            // 添加启动脚本
            string Bat = $@"@echo off
title {Id} 原版服务端
echo 如果服务端立即停止，请右键编辑该脚本，将下一行开头的 java 替换为适合该 Minecraft 版本的完整 java.exe 的路径。
echo 你可以在 PCL 的 [设置 → 启动选项] 中查看已安装的 java，所需的 java.exe 一般在其中的 bin 文件夹下。
echo ------------------------------
echo 如果提示 ""You need to agree to the EULA in order to run the server""，请打开 eula.txt，按说明阅读并同意 Minecraft EULA 后，将该文件最后一行中的 eula=false 改为 eula=true。
echo ------------------------------
""java"" -server -XX:+UseG1GC -Xmx6144M -Xms1024M -jar {Id}-server.jar nogui
echo ----------------------
echo 服务端已停止。
pause";
            FileUtils.Write(VersionFolder + "Launch Server.bat", Bat, encoding: Encoding.Default.Equals(Encoding.UTF8) ? Encoding.UTF8 : Encoding.GetEncoding("GB18030"));
            // 删除版本 JSON
            FileUtils.Delete(VersionFolder + Id + ".json");
        })
                {
                    ProgressWeight = 0.5d,
                    Show = false
                });
                // 下载服务端文件
                Loaders.Add(new ModNet.LoaderDownload("下载服务端文件", new List<ModNet.NetFile>()) { ProgressWeight = 5d });

                // 启动
                var Loader = new ModLoader.LoaderCombo<string>("Minecraft " + Id + " 服务端下载", Loaders) { OnStateChanged = LoaderStateChangedHintOnly };
                Loader.Start(Id);
                ModLoader.LoaderTaskbarAdd(Loader);
                ModMain.FrmMain.BtnExtraDownload.ShowRefresh();
                ModMain.FrmMain.BtnExtraDownload.Ribble();
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "开始 Minecraft 服务端下载失败");
            }
        }
        public static void McDownloadMenuSave(object sender, RoutedEventArgs e)
        {
            MyListItem Version;
            if (sender is MyListItem)
            {
                Version = (MyListItem)sender;
            }
            else if (((dynamic)sender).Parent is MyListItem)
            {
                Version = (MyListItem)((dynamic)sender).Parent;
            }
            else
            {
                Version = (MyListItem)((dynamic)sender).Parent.Parent;
            }
            try
            {
                string Id = Version.Title;
                string JsonUrl = (Version.Tag is JObject menuTag ? menuTag["url"]?.ToString() : null) ?? "";
                string VersionFolder = Dialogs.SelectFolder("选择目标文件夹", false).FirstOrDefault();
                if (VersionFolder is null)
                    return;
                VersionFolder = VersionFolder + Id + @"\";

                // 重复任务检查
                foreach (var OngoingLoader in ModLoader.LoaderTaskbar.ToList())
                {
                    if ((OngoingLoader.Name ?? "") != ($"Minecraft {Id} 下载" ?? ""))
                        continue;
                    ModMain.Hint("该版本正在下载中！", ModMain.HintType.Red);
                    return;
                }

                var Loaders = new List<ModLoader.LoaderBase>();
                // 下载版本 JSON 文件
                Loaders.Add(new ModNet.LoaderDownload("下载版本 JSON 文件", new List<ModNet.NetFile>() { new ModNet.NetFile(ModDownload.DlSourceLauncherOrMetaGet(JsonUrl), VersionFolder + Id + ".json", new FileChecker() { IsJson = true }) }) { ProgressWeight = 2d });
                // 获取支持库文件地址
                Loaders.Add(new ModLoader.LoaderTask<string, List<ModNet.NetFile>>("分析版本 JAR 文件下载地址", Task => Task.Output = new List<ModNet.NetFile>() { ModDownload.DlClientJarGet(new ModMinecraft.McInstance(VersionFolder), false) }) { ProgressWeight = 0.5d, Show = false });
                // 下载支持库文件
                Loaders.Add(new ModNet.LoaderDownload("下载版本 JAR 文件", new List<ModNet.NetFile>()) { ProgressWeight = 5d });

                // 启动
                var Loader = new ModLoader.LoaderCombo<string>("Minecraft " + Id + " 下载", Loaders) { OnStateChanged = LoaderStateChangedHintOnly };
                Loader.Start(Id);
                ModLoader.LoaderTaskbarAdd(Loader);
                ModMain.FrmMain.BtnExtraDownload.ShowRefresh();
                ModMain.FrmMain.BtnExtraDownload.Ribble();
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "开始 Minecraft 下载失败");
            }
        }
        /// <summary>
    /// 显示某 Minecraft 版本的更新日志。
    /// </summary>
    /// <param name="InstanceJson">在 version_manifest.json 中的对应项。</param>
        public static void McUpdateLogShow(JToken InstanceJson)
        {
            string WikiName;
            string Id = InstanceJson["id"]?.ToString().Lower() ?? "";
            if (Id == "3d shareware v1.34")
            {
                WikiName = "3D_Shareware_v1.34";
            }
            else if (Id == "2.0")
            {
                WikiName = "Java版2.0";
            }
            else if (Id == "1.rv-pre1")
            {
                WikiName = "Java版1.RV-Pre1";
            }
            else if (Id == "combat test 1" || Id.Contains("combat-1") || Id.Contains("combat-212796"))
            {
                WikiName = "Java版1.14.3_-_Combat_Test";
            }
            else if (Id == "combat test 2" || Id.Contains("combat-2") || Id.Contains("combat-0"))
            {
                WikiName = "Java版Combat_Test_2";
            }
            else if (Id == "combat test 3" || Id == "1.14_combat-3")
            {
                WikiName = "Java版Combat_Test_3";
            }
            else if (Id == "combat test 4" || Id == "1.15_combat-1")
            {
                WikiName = "Java版Combat_Test_4";
            }
            else if (Id == "combat test 5" || Id == "1.15_combat-6")
            {
                WikiName = "Java版Combat_Test_5";
            }
            else if (Id == "combat test 6" || Id == "1.16_combat-0")
            {
                WikiName = "Java版Combat_Test_6";
            }
            else if (Id == "combat test 7c" || Id == "1.16_combat-3")
            {
                WikiName = "Java版Combat_Test_7c";
            }
            else if (Id == "combat test 8b" || Id == "1.16_combat-5")
            {
                WikiName = "Java版Combat_Test_8b";
            }
            else if (Id == "combat test 8c" || Id == "1.16_combat-6")
            {
                WikiName = "Java版Combat_Test_8c";
            }
            else if (Id == "1.0.0-rc2-2")
            {
                WikiName = "Java版RC2";
            }
            else if (Id.StartsWithF("1.19_deep_dark_experimental_snapshot-") || Id.StartsWithF("1_19_deep_dark_experimental_snapshot-"))
            {
                WikiName = Id.Replace("1_19", "1.19").Replace("1.19_deep_dark_experimental_snapshot-", "Java版Deep_Dark_Experimental_Snapshot_");
            }
            else if (Id == "b1.9-pre6")
            {
                WikiName = "Java版Beta_1.9_Prerelease_6";
            }
            else if (Id.Contains("b1.9"))
            {
                WikiName = "Java版Beta_1.9_Prerelease";
            }
            else if ((string)InstanceJson["type"] == "release" || (string)InstanceJson["type"] == "snapshot" || (string)InstanceJson["type"] == "special")
            {
                WikiName = (Id.Contains("w") ? "" : "Java版") + Id.Replace(" Pre-Release ", "-pre");
            }
            else if (Id.StartsWithF("b"))
            {
                WikiName = "Java版" + Id.TrimEnd('a', 'b', 'c', 'd', 'e').Replace("b", "Beta_");
            }
            else if (Id.StartsWithF("a"))
            {
                WikiName = "Java版" + Id.TrimEnd('a', 'b', 'c', 'd', 'e').Replace("a", "Alpha_v");
            }
            else if (Id == "inf-20100618")
            {
                WikiName = "Java版Infdev_20100618";
            }
            else if (Id == "c0.30_01c" || Id == "c0.30_survival" || Id.Contains("生存测试"))
            {
                WikiName = "Java版Classic_0.30（生存模式）";
            }
            else if (Id.StartsWithF("c0.31"))
            {
                WikiName = "Java版Indev_0.31_20100130";
            }
            else if (Id.StartsWithF("c"))
            {
                WikiName = "Java版" + Id.Replace("c", "Classic_");
            }
            else if (Id.StartsWithF("rd-"))
            {
                WikiName = "Java版Pre-classic_" + Id;
            }
            else
            {
                Logger.Error($"未知的版本格式：{Id}。");
                return;
            }
            ModBase.OpenWebsite("https://zh.minecraft.wiki/w/" + WikiName.Replace("_experimental-snapshot-", "-exp"));
        }

        #endregion

        #region OptiFine 下载

        private static void McDownloadOptiFineSave(ModDownload.DlOptiFineListEntry DownloadInfo)
        {
            try
            {
                string Target = Dialogs.SaveFile("选择保存位置", DownloadInfo.FileName, filter: new[] { ("jar", "OptiFine Jar") });
                if (Target is null)
                    return;

                // 重复任务检查
                foreach (var OngoingLoader in ModLoader.LoaderTaskbar.ToList())
                {
                    if ((OngoingLoader.Name ?? "") != ($"OptiFine {DownloadInfo.DisplayName} 下载" ?? ""))
                        continue;
                    ModMain.Hint("该版本正在下载中！", ModMain.HintType.Red);
                    return;
                }

                var Loader = new ModLoader.LoaderCombo<ModDownload.DlOptiFineListEntry>("OptiFine " + DownloadInfo.DisplayName + " 下载", McDownloadOptiFineSaveLoader(DownloadInfo, Target)) { OnStateChanged = LoaderStateChangedHintOnly };
                Loader.Start(DownloadInfo);
                ModLoader.LoaderTaskbarAdd(Loader);
                ModMain.FrmMain.BtnExtraDownload.ShowRefresh();
                ModMain.FrmMain.BtnExtraDownload.Ribble();
            }

            catch (Exception ex)
            {
                Logger.Error(ex, "开始 OptiFine 下载失败");
            }
        }
        private static void McDownloadOptiFineInstall(string BaseMcFolderHome, string Target, ModLoader.LoaderTask<List<ModNet.NetFile>, bool> Task, bool UseJavaWrapper)
        {
            // 探测安装器要求的 Java 版本：读 jar 里 optifine/Installer.class 的 class 版本号（已迁至 C#）
            var JavaProbe = Download.OptiFineInstaller.ProbeRequiredJava(Target);
            if (JavaProbe.Failure is null)
            {
                Logger.Info($"OptiFine 安装器 class 版本：{JavaProbe.ClassVersion}，需要 Java {JavaProbe.JavaMajor}");
            }
            else
            {
                // 失败原因要落日志：原先吞掉异常只留一句「读取失败」，装错 Java 时无从判断卡在哪一步
                Logger.Warn($"读取 OptiFine 安装器 Java 版本失败：{JavaProbe.Failure}，按 Java {JavaProbe.JavaMajor} 处理");
            }
            var JavaRange = ValueRange<Version>.AtLeast(new Version(JavaProbe.JavaMajor, 0));
            // 选择 Java
            var Java = ModJava.SelectOrDownloadJava(JavaRange, true, Task.CreateCancellationToken(), Task.CreateSyncProgressProvider(0d, 0.2d));
            if (Task.IsCanceled)
                return;
            if (Java is null)
                throw new OperationCanceledException();
            // 添加 Java Wrapper 作为主 Jar（只有真要用时才解压，避免多余的文件写入）
            bool UseWrapper = UseJavaWrapper && !Settings.Get<bool>("LaunchAdvanceDisableJLW");
            string WrapperPath = null;
            if (UseWrapper)
                WrapperPath = LaunchUtils.ExtractPatch(@"Launch\JavaWrapper.jar", ModBase.PathPure.Value);
            string Arguments = Download.OptiFineInstaller.BuildArguments(Java.Version.Major, Target, PathUtils.RemoveSlashSuffix(BaseMcFolderHome), WrapperPath, PathUtils.RemoveSlashSuffix(ModBase.PathPure.Value), UseWrapper);
            // 开始启动
            lock (InstallSyncLock)
            {
                var Info = new ProcessStartInfo()
                {
                    FileName = Java.JavaExePath,
                    Arguments = Arguments,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardError = true,
                    RedirectStandardOutput = true,
                    WorkingDirectory = PathUtils.ToShortPath(BaseMcFolderHome)
                };
                if (Info.EnvironmentVariables.ContainsKey("appdata"))
                {
                    Info.EnvironmentVariables["appdata"] = BaseMcFolderHome;
                }
                else
                {
                    Info.EnvironmentVariables.Add("appdata", BaseMcFolderHome);
                }
                Logger.Info($"开始安装 OptiFine：{Target}");
                int TotalLength = 0;
                var process = new Process() { StartInfo = Info };
                string LastResult = "";
                using (var outputWaitHandle = new AutoResetEvent(false))
                {
                    using (var errorWaitHandle = new AutoResetEvent(false))
                    {
                        process.OutputDataReceived += (sender, e) =>
        {
            try
            {
                if (e.Data is null)
                {
                    outputWaitHandle.Set();
                }
                else
                {
                    LastResult = e.Data;
                    Logger.Trace(LastResult);
                    TotalLength += 1;
                    Task.Progress += Download.OptiFineInstaller.ProgressPerLine;
                }
            }
            catch (ObjectDisposedException) // 未引用异常对象，省略变量名（消除 CS0168）
            {
            }
            catch (Exception ex)
            {
                Logger.Warn(ex, "读取 OptiFine 安装器信息失败");
            }
            try
            {
                if (Task.State == ModBase.LoadState.Canceled && !process.HasExited)
                {
                    Logger.Info("由于任务取消，已中止 OptiFine 安装");
                    process.Kill();
                }
            }
            catch
            {
            }
            return;
        };
                        process.ErrorDataReceived += (sender, e) =>
        {
            try
            {
                if (e.Data is null)
                {
                    errorWaitHandle.Set();
                }
                else
                {
                    LastResult = e.Data;
                    Logger.Trace(LastResult);
                    TotalLength += 1;
                    Task.Progress += Download.OptiFineInstaller.ProgressPerLine;
                }
            }
            catch (ObjectDisposedException) // 未引用异常对象，省略变量名（消除 CS0168）
            {
            }
            catch (Exception ex)
            {
                Logger.Warn(ex, "读取 OptiFine 安装器错误信息失败");
            }
            try
            {
                if (Task.State == ModBase.LoadState.Canceled && !process.HasExited)
                {
                    Logger.Info("由于任务取消，已中止 OptiFine 安装");
                    process.Kill();
                }
            }
            catch
            {
            }
            return;
        };
                        process.Start();
                        process.BeginOutputReadLine();
                        process.BeginErrorReadLine();
                        // 等待
                        while (!process.HasExited)
                            Thread.Sleep(10);
                        // 输出
                        outputWaitHandle.WaitOne(10000);
                        errorWaitHandle.WaitOne(10000);
                        process.Dispose();
                        if (Download.OptiFineInstaller.IsFailed(TotalLength, LastResult))
                            throw new Exception("安装器运行出错，末行为 " + LastResult);
                    }
                }
            }
        }

        /// <summary>
    /// 构造 OptiFine 的候选下载源：镜像固定一个，官方源需要先解析下载页，取不到时只留镜像。
    /// </summary>
    /// <remarks>
    /// 「安装」与「另存为」原先各写了一份逐字相同的推导，改一处就会让两者打到不同地址。
    /// 地址形状与页面解析已迁至 <see cref="PCL.Download.OptiFineProfile"/>，这里只剩联网与进度。
    /// </remarks>
        private static List<string> McDownloadOptiFineSources(ModDownload.DlOptiFineListEntry DownloadInfo, ModLoader.LoaderBase Task)
        {
            var Sources = new List<string>() { Download.OptiFineProfile.BuildMirrorUrl(DownloadInfo.Inherit, DownloadInfo.IsPreview, DownloadInfo.DisplayName) };
            try
            {
                string PageData = ModNet.NetRequestByClientRetry(Download.OptiFineProfile.BuildOfficialPageUrl(DownloadInfo.FileName), Encoding: new UTF8Encoding(false), Accept: "text/html", SimulateBrowserHeaders: true);
                Task.Progress = 0.8d;
                string OfficialUrl = Download.OptiFineProfile.FindOfficialDownloadUrl(PageData);
                if (OfficialUrl is null)
                {
                    // 页面里没有下载链接时不再硬拼一个地址：那样会下回几百字节的 HTML，只在体积检查处失败
                    Logger.Warn($"OptiFine {DownloadInfo.DisplayName} 官方下载页中未找到下载链接，仅使用镜像源");
                }
                else
                {
                    Sources.Add(OfficialUrl);
                    Logger.Info($"OptiFine {DownloadInfo.DisplayName} 官方下载地址：{OfficialUrl}");
                }
            }
            catch (Exception ex)
            {
                Logger.Warn(ex, $"获取 OptiFine {DownloadInfo.DisplayName} 官方下载地址失败");
            }
            return Sources;
        }

        /// <summary>
    /// 获取下载某个 OptiFine 版本的加载器列表。
    /// 这不会补全所需的支持库文件，需要后续手动补全支持库。
    /// </summary>
        private static List<ModLoader.LoaderBase> McDownloadOptiFineLoader(ModDownload.DlOptiFineListEntry DownloadInfo, string McFolder, ModLoader.LoaderCombo<string> ClientDownloadLoader, string ClientFolder)
        {

            // 参数初始化
            string Id = DownloadInfo.InstanceName;
            string VersionFolder = McFolder + @"versions\" + Id + @"\";
            bool IsNewerVersion = DownloadInfo.Inherit.Contains("w") || McVersion.VersionToDrop(DownloadInfo.Inherit) >= 140;
            // 安装器落盘位置：新版走任务临时目录（装完即删），旧版留在 libraries 下（启动时当库加载）
            string Target = Download.OptiFineProfile.BuildInstallerTarget(IsNewerVersion, IsNewerVersion ? ModMain.RequestTaskTempFolder(true) : null, McFolder, DownloadInfo.FileName);
            var Loaders = new List<ModLoader.LoaderBase>();

            // 获取下载地址
            Loaders.Add(new ModLoader.LoaderTask<string, List<ModNet.NetFile>>("获取 OptiFine 主文件下载地址", (Task) =>
        {
            // 候选源推导已迁至 C#（镜像地址形状 + 官方页解析），「安装」与「另存为」共用这一份
            var Sources = McDownloadOptiFineSources(DownloadInfo, Task);
            // 构造文件请求
            Task.Output = new List<ModNet.NetFile>() { new ModNet.NetFile(Sources, Target, new FileChecker() { MinSize = 300 * 1024 }) };
        })
            {
                ProgressWeight = 8d
            });
            Loaders.Add(new ModNet.LoaderDownload("下载 OptiFine 主文件", new List<ModNet.NetFile>()) { ProgressWeight = 8d });
            Loaders.Add(new ModLoader.LoaderTask<List<ModNet.NetFile>, bool>("等待原版下载", (Task) =>
        {
            // 等待原版文件下载完成
            var TargetLoaders = ClientDownloadLoader.GetLoaderList().Where(l => (l.Name ?? "") == McDownloadClientLibName || (l.Name ?? "") == McDownloadClientJsonName).Where(l => l.State != ModBase.LoadState.Finished).ToList();
            if (TargetLoaders.Any())
                Logger.Info("OptiFine 安装正在等待原版文件下载完成");
            while (TargetLoaders.Any() && !Task.IsCanceled)
            {
                TargetLoaders = TargetLoaders.Where(l => l.State != ModBase.LoadState.Finished).ToList();
                Thread.Sleep(50);
            }
            if (Task.IsCanceled)
                return;
            // 复制原版文件
            lock (VanillaSyncLock)
            {
                string ClientName = PathUtils.GetLastPart(ClientFolder);
                DirectoryUtils.Create(McFolder + @"versions\" + DownloadInfo.Inherit);
                if (!FileUtils.Exists(McFolder + @"versions\" + DownloadInfo.Inherit + @"\" + DownloadInfo.Inherit + ".json"))
                {
                    FileUtils.Copy($"{ClientFolder}{ClientName}.json", $@"{McFolder}versions\{DownloadInfo.Inherit}\{DownloadInfo.Inherit}.json");
                }
                if (!FileUtils.Exists(McFolder + @"versions\" + DownloadInfo.Inherit + @"\" + DownloadInfo.Inherit + ".jar"))
                {
                    FileUtils.Copy($"{ClientFolder}{ClientName}.jar", $@"{McFolder}versions\{DownloadInfo.Inherit}\{DownloadInfo.Inherit}.jar");
                }
            }
        })
            {
                ProgressWeight = 0.1d,
                Show = false
            });

            // 安装（新旧方式均需要原版 Jar 和 Json）
            if (IsNewerVersion)
            {
                Logger.Info($"检测为新版 OptiFine：{DownloadInfo.Inherit}");
                Loaders.Add(new ModLoader.LoaderTask<List<ModNet.NetFile>, bool>("安装 OptiFine（方式 A）", (Task) =>
        {
            string BaseMcFolderHome = ModMain.RequestTaskTempFolder(true);
            string BaseMcFolder = BaseMcFolderHome + @".minecraft\";
            try
            {
                // 准备安装环境
                if (DirectoryUtils.Exists(BaseMcFolder + @"versions\" + DownloadInfo.Inherit))
                {
                    DirectoryUtils.Delete(BaseMcFolder + @"versions\" + DownloadInfo.Inherit);
                }
                DirectoryUtils.Create(BaseMcFolder + @"versions\" + DownloadInfo.Inherit + @"\");
                ModMinecraft.McFolderLauncherProfilesJsonCreate(BaseMcFolder);
                FileUtils.Copy(McFolder + @"versions\" + DownloadInfo.Inherit + @"\" + DownloadInfo.Inherit + ".json", BaseMcFolder + @"versions\" + DownloadInfo.Inherit + @"\" + DownloadInfo.Inherit + ".json");
                FileUtils.Copy(McFolder + @"versions\" + DownloadInfo.Inherit + @"\" + DownloadInfo.Inherit + ".jar", BaseMcFolder + @"versions\" + DownloadInfo.Inherit + @"\" + DownloadInfo.Inherit + ".jar");
                Task.Progress = 0.06d;
                // 进行安装
                bool UseJavaWrapper = true;
            Retry:
                ;

                try
                {
                    McDownloadOptiFineInstall(BaseMcFolderHome, Target, Task, UseJavaWrapper);
                }
                catch (Exception ex)
                {
                    if (UseJavaWrapper)
                    {
                        Logger.Warn(ex, "使用 JavaWrapper 安装 OptiFine 失败，将不使用 JavaWrapper 并重试");
                        UseJavaWrapper = false;
                        goto Retry;
                    }
                    else
                    {
                        throw new Exception("运行 OptiFine 安装器失败", ex);
                    }
                }
                Task.Progress = 0.96d;
                // 复制文件
                FileUtils.Delete(BaseMcFolder + "launcher_profiles.json");
                DirectoryUtils.Copy(BaseMcFolder, McFolder);
                Task.Progress = 0.98d;
                // 清理文件
                FileUtils.Delete(Target);
                DirectoryUtils.Delete(BaseMcFolderHome);
            }
            catch (Exception ex)
            {
                throw new Exception("安装 OptiFine（方式 A）失败", ex);
            }
        })
                {
                    ProgressWeight = 8d
                });
            }
            else
            {
                Logger.Info($"检测为旧版 OptiFine：{DownloadInfo.Inherit}");
                // 复制 Jar 文件
                // 建立 Json 文件
                Loaders.Add(new ModLoader.LoaderTask<List<ModNet.NetFile>, bool>("安装 OptiFine（方式 B）", (Task) => { try { FileUtils.Copy(McFolder + @"versions\" + DownloadInfo.Inherit + @"\" + DownloadInfo.Inherit + ".jar", VersionFolder + Id + ".jar"); Task.Progress = 0.7d; var InheritInstance = new ModMinecraft.McInstance(McFolder + @"versions\" + DownloadInfo.Inherit); string Json = Download.OptiFineProfile.BuildLegacyProfileJson(new Download.OptiFineLegacyProfile(Id, DownloadInfo.Inherit, DownloadInfo.FileName, DownloadInfo.ReleaseTime, InheritInstance.ReleaseTime.ToString("yyyy'-'MM'-'dd"), InheritInstance.IsOldJson, InheritInstance.IsOldJson ? InheritInstance.JsonObject["minecraftArguments"]?.ToString() : null)); FileUtils.Write(VersionFolder + Id + ".json", Json); } catch (Exception ex) { throw new Exception("安装 OptiFine（方式 B）失败", ex); } }) { ProgressWeight = 1d });
            }

            return Loaders;
        }
        /// <summary>
    /// 获取保存某个 OptiFine 版本的加载器列表。
    /// </summary>
        private static List<ModLoader.LoaderBase> McDownloadOptiFineSaveLoader(ModDownload.DlOptiFineListEntry DownloadInfo, string TargetFolder)
        {
            var Loaders = new List<ModLoader.LoaderBase>();
            // 获取下载地址
            Loaders.Add(new ModLoader.LoaderTask<ModDownload.DlOptiFineListEntry, List<ModNet.NetFile>>("获取 OptiFine 下载地址", (Task) =>
        {
            // 候选源推导已迁至 C#（镜像地址形状 + 官方页解析），「安装」与「另存为」共用这一份
            var Sources = McDownloadOptiFineSources(DownloadInfo, Task);
            Task.Progress = 0.9d;
            // 构造文件请求
            Task.Output = new List<ModNet.NetFile>() { new ModNet.NetFile(Sources, TargetFolder, new FileChecker() { MinSize = 64 * 1024 }) };
        })
            {
                ProgressWeight = 6d
            });
            // 下载
            Loaders.Add(new ModNet.LoaderDownload("下载 OptiFine 主文件", new List<ModNet.NetFile>()) { ProgressWeight = 10d, Block = true });
            return Loaders;
        }

        #endregion

        #region OptiFine 下载菜单

        public static MyVirtualizingElement<MyListItem> OptiFineDownloadListItem(ModDownload.DlOptiFineListEntry Entry, MyListItem.ClickEventHandler OnClick, bool IsSaveOnly)
        {
            return new MyVirtualizingElement<MyListItem>(() =>
        {
            // 建立控件
            var NewItem = new MyListItem()
            {
                Title = Entry.DisplayName,
                SnapsToDevicePixels = true,
                Height = 42d,
                Type = MyListItem.CheckType.Clickable,
                Tag = Entry,
                Info = (Entry.IsPreview ? "测试版" : "正式版") + (string.IsNullOrEmpty(Entry.ReleaseTime) ? "" : "，发布于 " + Entry.ReleaseTime) + (Entry.RequiredForgeVersion is null ? "，不兼容 Forge" : string.IsNullOrEmpty(Entry.RequiredForgeVersion) ? "" : "，兼容 Forge " + Entry.RequiredForgeVersion),
                Logo = ModBase.PathImage + "Blocks/GrassPath.png"
            };
            NewItem.Click += OnClick;
            // 建立菜单
            if (IsSaveOnly)
            {
                NewItem.ContentHandler = OptiFineSaveContMenuBuild;
            }
            else
            {
                NewItem.ContentHandler = OptiFineContMenuBuild;
            }
            return NewItem;
        })
            {
                Height = 42d
            };
        }
        private static void OptiFineSaveContMenuBuild(object sender, EventArgs e)
        {
            var BtnInfo = new MyIconButton() { LogoScale = 1.05d, Logo = ModBase.Logo.IconButtonInfo, ToolTip = "更新日志" };
            BtnInfo.Click += (_, __) => ModDownloadLib.OptiFineLog_Click((dynamic)_, (dynamic)__);
            ((dynamic)sender).Buttons = new[] { BtnInfo };
        }
        private static void OptiFineContMenuBuild(object sender, EventArgs e)
        {
            var BtnSave = new MyIconButton() { Logo = ModBase.Logo.IconButtonSave, ToolTip = "另存为" };
            BtnSave.Click += (_, __) => ModDownloadLib.OptiFineSave_Click((dynamic)_, (dynamic)__);
            var BtnInfo = new MyIconButton() { LogoScale = 1.05d, Logo = ModBase.Logo.IconButtonInfo, ToolTip = "更新日志" };
            BtnInfo.Click += (_, __) => ModDownloadLib.OptiFineLog_Click((dynamic)_, (dynamic)__);
            ((dynamic)sender).Buttons = new[] { BtnSave, BtnInfo };
        }
        private static void OptiFineLog_Click(object sender, RoutedEventArgs e)
        {
            ModDownload.DlOptiFineListEntry Version;
            if (((dynamic)sender).Tag is not null)
            {
                Version = (ModDownload.DlOptiFineListEntry)((dynamic)sender).Tag;
            }
            else if (((dynamic)sender).Parent.Tag is not null)
            {
                Version = (ModDownload.DlOptiFineListEntry)((dynamic)sender).Parent.Tag;
            }
            else
            {
                Version = (ModDownload.DlOptiFineListEntry)((dynamic)sender).Parent.Parent.Tag;
            }
            ModBase.OpenWebsite("https://optifine.net/changelog?f=" + Version.FileName);
        }
        public static void OptiFineSave_Click(object sender, RoutedEventArgs e)
        {
            ModDownload.DlOptiFineListEntry Version;
            if (((dynamic)sender).Tag is not null)
            {
                Version = (ModDownload.DlOptiFineListEntry)((dynamic)sender).Tag;
            }
            else if (((dynamic)sender).Parent.Tag is not null)
            {
                Version = (ModDownload.DlOptiFineListEntry)((dynamic)sender).Parent.Tag;
            }
            else
            {
                Version = (ModDownload.DlOptiFineListEntry)((dynamic)sender).Parent.Parent.Tag;
            }
            McDownloadOptiFineSave(Version);
        }

        #endregion

        #region LiteLoader 下载

        private static void McDownloadLiteLoaderSave(ModDownload.DlLiteLoaderListEntry DownloadInfo)
        {
            try
            {
                string Id = DownloadInfo.Inherit;
                string Target = Dialogs.SaveFile("选择保存位置", DownloadInfo.FileName.Replace("-SNAPSHOT", ""), filter: new[] { ("jar", "LiteLoader 安装器") });
                if (Target is null)
                    return;

                // 重复任务检查
                foreach (var OngoingLoader in ModLoader.LoaderTaskbar.ToList())
                {
                    if ((OngoingLoader.Name ?? "") != ($"LiteLoader {Id} 下载" ?? ""))
                        continue;
                    ModMain.Hint("该版本正在下载中！", ModMain.HintType.Red);
                    return;
                }

                // 构造步骤加载器
                var Loaders = new List<ModLoader.LoaderBase>();
                // 下载
                var Address = new List<string>();
                if (DownloadInfo.IsLegacy)
                {
                    // 老版本
                    switch (DownloadInfo.Inherit ?? "")
                    {
                        case "1.7.10":
                            {
                                Address.Add("https://dl.liteloader.com/redist/1.7.10/liteloader-installer-1.7.10-04.jar");
                                break;
                            }
                        case "1.7.2":
                            {
                                Address.Add("https://dl.liteloader.com/redist/1.7.2/liteloader-installer-1.7.2-04.jar");
                                break;
                            }
                        case "1.6.4":
                            {
                                Address.Add("https://dl.liteloader.com/redist/1.6.4/liteloader-installer-1.6.4-01.jar");
                                break;
                            }
                        case "1.6.2":
                            {
                                Address.Add("https://dl.liteloader.com/redist/1.6.2/liteloader-installer-1.6.2-04.jar");
                                break;
                            }
                        case "1.5.2":
                            {
                                Address.Add("https://dl.liteloader.com/redist/1.5.2/liteloader-installer-1.5.2-01.jar");
                                break;
                            }

                        default:
                            {
                                throw new NotSupportedException("未知的 Minecraft 版本（" + DownloadInfo.Inherit + "）");
                            }
                    }
                }
                else
                {
                    // 官方源
                    Address.Add("http://jenkins.liteloader.com/job/LiteLoaderInstaller%20" + DownloadInfo.Inherit + "/lastSuccessfulBuild/artifact/" + (DownloadInfo.Inherit == "1.8" ? "ant/dist/" : "build/libs/") + DownloadInfo.FileName);
                }
                Loaders.Add(new ModNet.LoaderDownload("下载主文件", new List<ModNet.NetFile>() { new ModNet.NetFile(Address.ToArray(), Target, new FileChecker() { MinSize = 1024 * 1024 }) }) { ProgressWeight = 15d });
                // 启动
                var Loader = new ModLoader.LoaderCombo<ModDownload.DlLiteLoaderListEntry>("LiteLoader " + Id + " 安装器下载", Loaders) { OnStateChanged = LoaderStateChangedHintOnly };
                Loader.Start(DownloadInfo);
                ModLoader.LoaderTaskbarAdd(Loader);
                ModMain.FrmMain.BtnExtraDownload.ShowRefresh();
                ModMain.FrmMain.BtnExtraDownload.Ribble();
            }

            catch (Exception ex)
            {
                Logger.Error(ex, "开始 LiteLoader 安装器下载失败");
            }
        }

        /// <summary>
    /// 获取下载某个 LiteLoader 版本的加载器列表。
    /// 这不会补全所需的支持库文件，需要后续手动补全支持库。
    /// </summary>
        private static List<ModLoader.LoaderBase> McDownloadLiteLoaderLoader(ModDownload.DlLiteLoaderListEntry DownloadInfo, string McFolder)
        {
            var Loaders = new List<ModLoader.LoaderBase>();

            // 安装
            // 版本 json 的字段顺序与「追加 liteloader 库」规则已迁至 C#
            // 输出 Json 文件，同时会新建该文件夹
            Loaders.Add(new ModLoader.LoaderTask<string, string>("安装 LiteLoader", (Task) => { try { string VersionName = DownloadInfo.Inherit + Download.LiteLoaderProfile.InstanceSuffix; string VersionFolder = McFolder + @"versions\" + VersionName + @"\"; string Json = Download.LiteLoaderProfile.BuildVersionJson(DownloadInfo.JsonToken?.ToString() ?? "", DownloadInfo.Inherit, DownloadInfo.ReleaseTime); FileUtils.Write(VersionFolder + VersionName + ".json", Json); } catch (Exception ex) { throw new Exception("安装新 LiteLoader 版本失败", ex); } }) { ProgressWeight = 1d });

            return Loaders;
        }

        #endregion

        #region LiteLoader 下载菜单

        public static MyVirtualizingElement<MyListItem> LiteLoaderDownloadListItem(ModDownload.DlLiteLoaderListEntry Entry, MyListItem.ClickEventHandler OnClick, bool IsSaveOnly)
        {
            return new MyVirtualizingElement<MyListItem>(() =>
        {
            // 建立控件
            var NewItem = new MyListItem()
            {
                Title = Entry.Inherit,
                SnapsToDevicePixels = true,
                Height = 42d,
                Type = MyListItem.CheckType.Clickable,
                Tag = Entry,
                Info = (Entry.IsPreview ? "测试版" : "稳定版") + (string.IsNullOrEmpty(Entry.ReleaseTime) ? "" : "，发布于 " + Entry.ReleaseTime),
                Logo = ModBase.PathImage + "Blocks/Egg.png"
            };
            NewItem.Click += OnClick;
            // 建立菜单
            if (IsSaveOnly)
            {
                NewItem.ContentHandler = LiteLoaderSaveContMenuBuild;
            }
            else
            {
                NewItem.ContentHandler = LiteLoaderContMenuBuild;
            }
            // 结束
            return NewItem;
        })
            {
                Height = 42d
            };
        }
        private static void LiteLoaderSaveContMenuBuild(MyListItem sender, EventArgs e)
        {
            if (Conversions.ToBoolean(((dynamic)sender.Tag).IsLegacy))
            {
                sender.Buttons = Array.Empty<MyIconButton>();
            }
            else
            {
                var BtnList = new MyIconButton() { Logo = ModBase.Logo.IconButtonList, ToolTip = "查看全部版本", Tag = sender };
                BtnList.Click += (_, __) => ModDownloadLib.LiteLoaderAll_Click((dynamic)_, (dynamic)__);
                sender.Buttons = new[] { BtnList };
            }
        }
        private static void LiteLoaderContMenuBuild(MyListItem sender, EventArgs e)
        {
            var BtnSave = new MyIconButton() { Logo = ModBase.Logo.IconButtonSave, ToolTip = "保存安装器", Tag = sender };
            BtnSave.Click += (_, __) => ModDownloadLib.LiteLoaderSave_Click((dynamic)_, (dynamic)__);
            if (Conversions.ToBoolean(((dynamic)sender.Tag).IsLegacy))
            {
                sender.Buttons = new[] { BtnSave };
            }
            else
            {
                var BtnList = new MyIconButton() { Logo = ModBase.Logo.IconButtonList, ToolTip = "查看全部版本", Tag = sender };
                BtnList.Click += (_, __) => ModDownloadLib.LiteLoaderAll_Click((dynamic)_, (dynamic)__);
                sender.Buttons = new[] { BtnSave, BtnList };
            }
        }
        private static void LiteLoaderAll_Click(object sender, RoutedEventArgs e)
        {
            ModDownload.DlLiteLoaderListEntry Version;
            if (((dynamic)sender).Tag is ModDownload.DlLiteLoaderListEntry)
            {
                Version = (ModDownload.DlLiteLoaderListEntry)((dynamic)sender).Tag;
            }
            else
            {
                Version = (ModDownload.DlLiteLoaderListEntry)((dynamic)sender).Tag.Tag;
            }
            ModBase.OpenWebsite("https://jenkins.liteloader.com/view/" + Version.Inherit);
        }
        public static void LiteLoaderSave_Click(object sender, RoutedEventArgs e)
        {
            // ListItem 与小按钮都会调用这个方法
            ModDownload.DlLiteLoaderListEntry Version;
            if (((dynamic)sender).Tag is ModDownload.DlLiteLoaderListEntry)
            {
                Version = (ModDownload.DlLiteLoaderListEntry)((dynamic)sender).Tag;
            }
            else
            {
                Version = (ModDownload.DlLiteLoaderListEntry)((dynamic)sender).Tag.Tag;
            }
            McDownloadLiteLoaderSave(Version);
        }

        #endregion

        #region Forgelike 下载

        public static void McDownloadForgelikeSave(ModDownload.DlForgelikeEntry Info)
        {
            try
            {
                string Target = Dialogs.SaveFile("选择保存位置", $"{Info.LoaderName}-{Info.Inherit}-{Info.VersionName}.{Info.FileExtension}", filter: new[] { (Info.FileExtension, $"{Info.LoaderName} 安装器") });
                if (Target is null)
                    return;
                string DisplayName = $"{Info.LoaderName} {Info.Inherit} - {Info.VersionName}";

                // 重复任务检查
                foreach (var OngoingLoader in ModLoader.LoaderTaskbar.ToList())
                {
                    if ((OngoingLoader.Name ?? "") != ($"{DisplayName} 下载" ?? ""))
                        continue;
                    ModMain.Hint("该版本正在下载中！", ModMain.HintType.Red);
                    return;
                }

                // 获取下载地址（镜像在前、官方在后）
                var Files = new List<ModNet.NetFile>();
                if (Info.IsNeoForge)
                {
                    ModDownload.DlNeoForgeListEntry NeoForge = (ModDownload.DlNeoForgeListEntry)Info;
                    var Installer = Download.ForgelikeInstaller.BuildNeoForgeInstaller(NeoForge.Inherit, NeoForge.ApiName);
                    Files.Add(new ModNet.NetFile(Installer.Urls, Target, new FileChecker() { MinSize = (int)Installer.MinSize }));
                }
                else
                {
                    ModDownload.DlForgeVersionEntry Forge = (ModDownload.DlForgeVersionEntry)Info;
                    var Installer = Download.ForgelikeInstaller.BuildForgeInstaller(Forge.Inherit, Forge.FileVersion, Forge.Category, Forge.FileExtension, Forge.Hash);
                    Files.Add(new ModNet.NetFile(Installer.Urls, Target, new FileChecker() { MinSize = (int)Installer.MinSize, Hash = Installer.Hash }));
                }

                // 构造加载器
                var Loaders = new List<ModLoader.LoaderBase>();
                Loaders.Add(new ModNet.LoaderDownload("下载主文件", Files) { ProgressWeight = 6d });

                // 启动
                var Loader = new ModLoader.LoaderCombo<ModDownload.DlForgelikeEntry>(DisplayName + " 下载", Loaders) { OnStateChanged = LoaderStateChangedHintOnly };
                Loader.Start(Info);
                ModLoader.LoaderTaskbarAdd(Loader);
                ModMain.FrmMain.BtnExtraDownload.ShowRefresh();
                ModMain.FrmMain.BtnExtraDownload.Ribble();
            }

            catch (Exception ex)
            {
                Logger.Error(ex, $"开始 {Info.LoaderName} 安装器下载失败");
            }
        }

        private static void ForgelikeInjector(string Target, ModLoader.LoaderTask<bool, bool> Task, string McFolder, bool UseJavaWrapper, bool IsNeoForge, int? RequiredJavaMajor)
        {
            // 选择 Java：原版 26+ 在版本 json 写明 javaVersion.majorVersion（26.3 要求 25）。
            // 旧口径「≥8 且按 JavaList 顺序取首个」会抓到列表靠前的 Java 21，
            // 目录问题修好后 processor 阶段仍会失败。这里先精确匹配该主版本，
            // 本机没有恰好这一版时才放宽为「不低于要求」，绝不回退到更低的 Java。
            Java Java = null;
            var Token = Task.CreateCancellationToken();
            if (RequiredJavaMajor.HasValue && RequiredJavaMajor.Value >= 22)
            {
                int Major = RequiredJavaMajor.Value;
                var ExactRange = ValueRange<Version>.ClosedOpen(new Version(Major, 0), new Version(Major + 1, 0));
                Java = ModJava.SelectOrDownloadJava(ExactRange, false, Token, Task.CreateSyncProgressProvider(0d, 0.2d));
                if (Java is null && !Task.IsCanceled)
                {
                    Logger.Info($"本机没有 Java {Major}，放宽为 Java {Major} 或更高版本");
                    Java = ModJava.SelectOrDownloadJava(ValueRange<Version>.AtLeast(new Version(Major, 0)), true, Token, Task.CreateSyncProgressProvider(0d, 0.2d));
                }
            }
            else
            {
                Java = ModJava.SelectOrDownloadJava(ValueRange<Version>.AtLeast(new Version(8, 0, 60)), true, Token, Task.CreateSyncProgressProvider(0d, 0.2d));
            }
            if (Task.IsCanceled)
                return;
            if (Java is null)
                throw new OperationCanceledException();
            // 添加 Java Wrapper 作为主 Jar
            bool UseWrapper = UseJavaWrapper && !Settings.Get<bool>("LaunchAdvanceDisableJLW");
            string WrapperPath = UseWrapper ? LaunchUtils.ExtractPatch(@"Launch\JavaWrapper.jar", ModBase.PathPure.Value) : null;
            string Arguments = Download.ForgelikeInstaller.BuildArguments(Java.Version.Major, ModBase.PathTemp + @"Cache\forge_installer.jar", Target, McFolder, WrapperPath, PathUtils.RemoveSlashSuffix(ModBase.PathPure.Value), UseWrapper);
            // 开始启动
            lock (InstallSyncLock)
            {
                var Info = new ProcessStartInfo()
                {
                    FileName = Java.JavaExePath,
                    Arguments = Arguments,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardError = true,
                    RedirectStandardOutput = true
                };
                string LoaderName = IsNeoForge ? "NeoForge" : "Forge";
                Logger.Info($"开始安装 {LoaderName}：{Arguments}");
                var process = new Process() { StartInfo = Info };
                var LastResults = new Queue<string>();
                using (var outputWaitHandle = new AutoResetEvent(false))
                {
                    using (var errorWaitHandle = new AutoResetEvent(false))
                    {
                        process.OutputDataReceived += (sender, e) =>
        {
            try
            {
                if (e.Data is null)
                {
                    outputWaitHandle.Set();
                }
                else
                {
                    LastResults.Enqueue(e.Data);
                    if (LastResults.Count > 100)
                        LastResults.Dequeue();
                    ForgelikeInjectorLine(e.Data, Task);
                }
            }
            catch (ObjectDisposedException) // 未引用异常对象，省略变量名（消除 CS0168）
            {
            }
            catch (Exception ex)
            {
                Logger.Warn(ex, $"读取 {LoaderName} 安装器信息失败");
            }
            try
            {
                if (Task.State == ModBase.LoadState.Canceled && !process.HasExited)
                {
                    Logger.Info($"由于任务取消，已中止 {LoaderName} 安装");
                    process.Kill();
                }
            }
            catch
            {
            }
            return;
        };
                        process.ErrorDataReceived += (sender, e) =>
        {
            try
            {
                if (e.Data is null)
                {
                    errorWaitHandle.Set();
                }
                else
                {
                    LastResults.Enqueue(e.Data);
                    if (LastResults.Count > 100)
                        LastResults.Dequeue();
                    ForgelikeInjectorLine(e.Data, Task);
                }
            }
            catch (ObjectDisposedException) // 未引用异常对象，省略变量名（消除 CS0168）
            {
            }
            catch (Exception ex)
            {
                Logger.Warn(ex, $"读取 {LoaderName} 安装器错误信息失败");
            }
            try
            {
                if (Task.State == ModBase.LoadState.Canceled && !process.HasExited)
                {
                    Logger.Info($"由于任务取消，已中止 {LoaderName} 安装");
                    process.Kill();
                }
            }
            catch
            {
            }
            return;
        };
                        process.Start();
                        process.BeginOutputReadLine();
                        process.BeginErrorReadLine();
                        // 等待
                        while (!process.HasExited)
                            Thread.Sleep(10);
                        // 输出
                        outputWaitHandle.WaitOne(10000);
                        errorWaitHandle.WaitOne(10000);
                        process.Dispose();
                        // 检查是否安装成功：最后 5 行中是否有 true（true 可能在倒数数行，见 #832）
                        if (LastResults.Reverse().Take(5).Any(l => l == "true"))
                            return;
                        Logger.Warn(LastResults.Join("\r\n"));
                        string LastLines = "";
                        for (int i = Math.Max(0, LastResults.Count - 5), loopTo = LastResults.Count - 1; i <= loopTo; i++) // 最后 5 行
                            LastLines += "\r\n" + LastResults.ElementAtOrDefault(i);
                        throw new Exception($"{LoaderName} 安装器出错，日志结束部分为：" + LastLines);
                    }
                }
            }
        }
        /// <summary>
    /// 把 Forge / NeoForge 安装器的一行输出翻译成进度动作。
    /// </summary>
    /// <remarks>
    /// 识别表在 <c>PCL.Download.ForgelikeInstaller.ReadProgress</c>；不认识的行只进 trace，不上抛进度。
    /// </remarks>
        private static void ForgelikeInjectorLine(string Content, ModLoader.LoaderTask<bool, bool> Task)
        {
            var ProgressStep = Download.ForgelikeInstaller.ReadProgress(Content);
            if (!ProgressStep.HasValue)
            {
                Logger.Trace(Content);
                return;
            }
            // 校验命中这类行是累加的，别写成赋值，否则进度条会来回跳
            if (ProgressStep.Value.IsIncrement)
            {
                Task.Progress += ProgressStep.Value.Progress;
            }
            else
            {
                Task.Progress = ProgressStep.Value.Progress;
            }
            Logger.Info(Content);
        }

        /// <summary>
    /// 获取下载某个 Forgelike 版本的加载器列表。
    /// </summary>
        private static List<ModLoader.LoaderBase> McDownloadForgelikeLoader(bool IsNeoForge, string LoaderVersion, string NewInstanceName, string Inherit, ModDownload.DlForgelikeEntry Info, string McFolder, ModLoader.LoaderCombo<string> ClientDownloadLoader, string ClientFolder)
        {
            // 参数初始化：整合包/实例给出的版本串可能自带 MC 版本，或缺少 NeoForge 的包名前缀
            var VersionTarget = Download.ForgelikeInstaller.Normalize(IsNeoForge, LoaderVersion, Inherit);
            Inherit = VersionTarget.Inherit;
            LoaderVersion = VersionTarget.LoaderVersion;
            if (IsNeoForge && Info is null)
                Info = new ModDownload.DlNeoForgeListEntry(LoaderVersion);
            string LoaderName = IsNeoForge ? "NeoForge" : "Forge";
            string InstallerAddress = ModMain.RequestTaskTempFolder(true) + "forge_installer.jar";
            string VersionFolder = $@"{McFolder}versions\{NewInstanceName}\";
            var Loaders = new List<ModLoader.LoaderBase>();

            // 获取 Forge 下载信息
            if (Info is null)
            {
                Loaders.Add(new ModLoader.LoaderTask<string, string>($"获取 {LoaderName} 详细信息", (Task) =>
        {
            // 获取 Forge 对应 MC 版本列表
            var ForgeLoader = new ModLoader.LoaderTask<string, List<ModDownload.DlForgeVersionEntry>>("McDownloadForgeLoader " + Inherit, ModDownload.DlForgeVersionMain);
            ForgeLoader.WaitForExit(Inherit);
            Task.Progress = 0.8d;
            // 查找对应版本
            foreach (var ForgeVersion in ForgeLoader.Output)
            {
                if (McUtils.CompareVersion(ForgeVersion.Version.ToString(), LoaderVersion) == 0)
                {
                    Info = ForgeVersion;
                    return;
                }
            }
            throw new Exception($"未能找到 {LoaderName} " + Inherit + "-" + LoaderVersion + " 的详细信息！");
        })
                {
                    ProgressWeight = 3d
                });
            }
            // 下载 Forgelike 主文件
            Loaders.Add(new ModLoader.LoaderTask<string, List<ModNet.NetFile>>($"准备下载 {LoaderName}", (Task) =>
        {
            // 添加主文件下载（镜像在前、官方在后）
            var Files = new List<ModNet.NetFile>();
            if (Info.IsNeoForge)
            {
                ModDownload.DlNeoForgeListEntry Neo = (ModDownload.DlNeoForgeListEntry)Info;
                var Installer = Download.ForgelikeInstaller.BuildNeoForgeInstaller(Neo.Inherit, Neo.ApiName);
                Files.Add(new ModNet.NetFile(Installer.Urls, InstallerAddress, new FileChecker() { MinSize = (int)Installer.MinSize }));
            }
            else
            {
                ModDownload.DlForgeVersionEntry Forge = (ModDownload.DlForgeVersionEntry)Info;
                var Installer = Download.ForgelikeInstaller.BuildForgeInstaller(Forge.Inherit, Forge.FileVersion, Forge.Category, Forge.FileExtension, Forge.Hash);
                Files.Add(new ModNet.NetFile(Installer.Urls, InstallerAddress, new FileChecker() { MinSize = (int)Installer.MinSize, Hash = Installer.Hash }));
            }
            Task.Output = Files;
        })
            {
                ProgressWeight = 0.5d,
                Show = false
            });
            Loaders.Add(new ModNet.LoaderDownload($"下载 {LoaderName} 主文件", new List<ModNet.NetFile>()) { ProgressWeight = 9d });

            // 安装（仅在新版安装时需要原版 Jar）
            string CurrentMcFolder = ModMinecraft.McFolderSelected;
            if (IsNeoForge || Conversions.ToDouble(LoaderVersion.BeforeFirst(".")) >= 20d)
            {
                Logger.Info($"检测为{(IsNeoForge ? " Neo" : "新版 ")}Forge：{LoaderVersion}");
                List<ModMinecraft.McLibEntry> Libs = null;
                Loaders.Add(new ModLoader.LoaderTask<string, List<ModNet.NetFile>>($"分析 {LoaderName} 支持库文件", (Task) =>
        {
            Task.Output = new List<ModNet.NetFile>();
            ZipArchive Installer = null;
            try
            {
                // 解压并获取、合并两个 Json 的信息
                Installer = FileUtils.OpenZip(InstallerAddress);
                Task.Progress = 0.2d;
                var profileEntry = Installer.GetEntry("install_profile.json");
                var versionEntry = Installer.GetEntry("version.json");
                if (profileEntry is null || versionEntry is null)
                    throw new Exception("安装包缺少 install_profile.json 或 version.json，安装包可能已损坏");
                JObject Json = (JObject)profileEntry.Open().ReadString().DeserializeJson();
                JObject Json2 = (JObject)versionEntry.Open().ReadString().DeserializeJson();
                Json.Merge(Json2);
                // 获取 Lib 下载信息
                Libs = ModMinecraft.McLibListGetWithJson(Json, true, CurrentMcFolder);
                // 添加 Mappings 下载信息
                var mojmaps = Json["data"]?["MOJMAPS"];
                var mappingsClient = mojmaps?["client"];
                if (mojmaps is not null && mappingsClient is not null)
                {
                    // 下载原版 Json 文件
                    Task.Progress = 0.4d;
                    JObject RawJson = (JObject)ModNet.NetRequestByLoader(ModDownload.DlSourceLauncherOrMetaGet(Conversions.ToString(ModDownload.DlClientListGet(Inherit))), IsJson: true).DeserializeJson();
                    // [net.minecraft:client:1.17.1-20210706.113038:mappings@txt] 或 @tsrg]
                    string clientRaw = mappingsClient.ToString().Trim("[]".ToCharArray());
                    string OriginalName = clientRaw.BeforeFirst("@");
                    string[] clientParts = clientRaw.Split("@");
                    string mappingSuffix = clientParts.Length > 1 ? clientParts[1] : "txt";
                    string Address = Path.ChangeExtension(ModMinecraft.McLibGet(OriginalName, CustomMcFolder: CurrentMcFolder), "." + mappingSuffix);
                    var ClientMappings = RawJson["downloads"]?["client_mappings"];
                    if (ClientMappings is null)
                        throw new Exception("原版 JSON 缺少 client_mappings 信息，安装包可能已损坏");
                    Libs.Add(new ModMinecraft.McLibEntry()
                    {
                        IsNatives = false,
                        LocalPath = Address,
                        OriginalName = OriginalName,
                        Url = (string)ClientMappings["url"] ?? "",
                        Size = (long?)ClientMappings["size"] ?? -1L,
                        SHA1 = (string)ClientMappings["sha1"] ?? ""
                    });
                    Logger.Info($"需要下载 Mappings：{ClientMappings["url"]} (SHA1: {ClientMappings["sha1"]})");
                }
                Task.Progress = 0.8d;
                // 去除其中的原始 Forgelike 项
                for (int i = 0, loopTo = Libs.Count - 1; i <= loopTo; i++)
                {
                    if (Libs[i].LocalPath.EndsWithF($"{LoaderName.Lower()}-{Inherit}-{LoaderVersion}.jar") || Libs[i].LocalPath.EndsWithF($"{LoaderName.Lower()}-{Inherit}-{LoaderVersion}-client.jar"))
                    {
                        Logger.Warn($"已从待下载 {LoaderName} 支持库中移除：{Libs[i].LocalPath}");
                        Libs.RemoveAt(i);
                        break;
                    }
                }
                Task.Output = ModMinecraft.McLibNetFilesFromTokens(Libs, CurrentMcFolder);
            }
            catch (Exception ex)
            {
                throw new Exception($"获取{(IsNeoForge ? " Neo" : "新版 ")}Forge 支持库列表失败", ex);
            }
            finally
            {
                if (Installer is not null)
                    Installer.Dispose();
            }
        })
                {
                    ProgressWeight = 1d
                });
                Loaders.Add(new ModNet.LoaderDownload($"下载 {LoaderName} 支持库文件", new List<ModNet.NetFile>()) { ProgressWeight = 12d });
                Loaders.Add(new ModLoader.LoaderTask<List<ModNet.NetFile>, bool>($"获取 {LoaderName} 支持库文件", (Task) =>
        {
            #region Forgelike 文件
            foreach (ModMinecraft.McLibEntry LibFile in Libs)
            {
                if (string.IsNullOrWhiteSpace(CurrentMcFolder) || string.IsNullOrWhiteSpace(LibFile.LocalPath))
                    throw new InvalidDataException("Forgelike 支持库文件路径不完整。");
                string SourceRoot = Path.GetFullPath(PathUtils.RemoveExtendedPrefix(CurrentMcFolder));
                string SourcePath = Path.GetFullPath(PathUtils.RemoveExtendedPrefix(LibFile.LocalPath));
                if (!PathUtils.IsParentOf(SourceRoot, SourcePath) || PathUtils.IsSame(SourceRoot, SourcePath))
                    throw new InvalidDataException($"Forgelike 支持库文件不在当前 Minecraft 目录中：{SourcePath}");
                string relativePath = Path.GetRelativePath(Path.GetFullPath(SourceRoot), Path.GetFullPath(SourcePath));
                string RealPath = Path.Combine(McFolder, relativePath);
                if (!FileUtils.Exists(RealPath))
                    FileUtils.Copy(LibFile.LocalPath, RealPath);
                Logger.Trace($"复制的 {LoaderName} 支持库文件：{LibFile.LocalPath}");
            }
            #endregion
            #region 原版文件
            // 等待原版文件下载完成
            var TargetLoaders = ClientDownloadLoader.GetLoaderList().Where(l => (l.Name ?? "") == McDownloadClientLibName || (l.Name ?? "") == McDownloadClientJsonName).Where(l => l.State != ModBase.LoadState.Finished).ToList();
            if (TargetLoaders.Any())
                Logger.Info($"{LoaderName} 安装正在等待原版文件下载完成");
            while (TargetLoaders.Any() && !Task.IsCanceled)
            {
                TargetLoaders = TargetLoaders.Where(l => l.State != ModBase.LoadState.Finished).ToList();
                Thread.Sleep(50);
            }
            if (Task.IsCanceled)
                return;
            // 复制原版文件
            lock (VanillaSyncLock)
            {
                string ClientName = PathUtils.GetLastPart(ClientFolder);
                DirectoryUtils.Create(McFolder + @"versions\" + Inherit);
                if (!FileUtils.Exists(McFolder + @"versions\" + Inherit + @"\" + Inherit + ".json"))
                {
                    FileUtils.Copy(ClientFolder + ClientName + ".json", McFolder + @"versions\" + Inherit + @"\" + Inherit + ".json");
                }
                if (!FileUtils.Exists(McFolder + @"versions\" + Inherit + @"\" + Inherit + ".jar"))
                {
                    FileUtils.Copy(ClientFolder + ClientName + ".jar", McFolder + @"versions\" + Inherit + @"\" + Inherit + ".jar");
                }
            }
            #endregion
        })
                {
                    ProgressWeight = 0.1d,
                    Show = false
                });
                Loaders.Add(new ModLoader.LoaderTask<bool, bool>(IsNeoForge ? "安装 NeoForge" : "安装 Forge（方式 A）", (Task) =>
        {
            try
            {
                // 记录当前文件夹列表（在新建目标文件夹之前）
                Logger.Info($"开始进行 Forgelike 安装：{InstallerAddress}");
                ;
                // 新建目标版本文件夹
                // [VB Port] restore Dim dropped by codeconv (CommonConversions NRE)
                var OldList = DirectoryUtils.EnumerateDirectories(McFolder + @"versions\").ToList();
                DirectoryUtils.Create(VersionFolder);
                Task.Progress = 0.04d;
                // 释放 launcher_installer.json
                ModMinecraft.McFolderLauncherProfilesJsonCreate(McFolder);
                Task.Progress = 0.05d;
                // 运行 Forge 安装器
                // 读取原版 json 的 Java 版本要求（26+ 由 Mojang 写在 javaVersion.majorVersion）。
                // 解析交给无 GUI 内核；读不到就回退旧的 Java 8+ 口径，保证老版本行为不变。
                bool UseJavaWrapper = true;
                ;
                // [VB Port] restore Dim dropped by codeconv (CommonConversions NRE)
                int? RequiredJavaMajor = null;
                try
                {
                    string VanillaJsonPath = McFolder + @"versions\" + Inherit + @"\" + Inherit + ".json";
                    if (File.Exists(VanillaJsonPath))
                    {
                        RequiredJavaMajor = Download.JavaRequirement.ReadRequiredJavaMajor(File.ReadAllText(VanillaJsonPath));
                    }
                }
                catch (Exception ex)
                {
                    Logger.Warn(ex, "读取原版 json 的 Java 版本要求失败，安装器回退到 Java 8+");
                }

            Retry:
                ;

                try
                {
                    // 释放 Forge 注入器
                    ModBase.ExtractResources(ModBase.PathTemp + @"Cache\forge_installer.jar", "ForgeInstaller");
                    Task.Progress = 0.06d;
                    // 运行注入器
                    ModDownloadLib.ForgelikeInjector(InstallerAddress, Task, McFolder, UseJavaWrapper, IsNeoForge, RequiredJavaMajor);
                    Task.Progress = 0.97d;
                }
                catch (Exception ex)
                {
                    if (UseJavaWrapper)
                    {
                        Logger.Warn(ex, $"使用 JavaWrapper 安装 {LoaderName} 失败，将不使用 JavaWrapper 并重试");
                        UseJavaWrapper = false;
                        goto Retry;
                    }
                    else
                    {
                        throw new Exception($"运行 {LoaderName} 安装器失败", ex);
                    }
                    // 复制新增的版本 Json
                }

                            ;
                // [VB Port] restore Dim dropped by codeconv (CommonConversions NRE)
                var DeltaList = DirectoryUtils.EnumerateDirectories(McFolder + @"versions\").SkipWhile(i => OldList.Contains(i)).Select(f => DirectoryUtils.GetInfo(f)).ToList();
                if (DeltaList.Count > 1)
                {
                    // 它可能和 OptiFine 安装同时运行，导致增加的文件不止一个（这导致了 #151）
                    // 也可能是因为 Forge 安装器的 Bug，生成了一个名字错误的文件夹，所以需要检查文件夹是否为空
                    DeltaList = DeltaList.Where(l => l.Name.ContainsIgnoreCase("forge") && l.EnumerateFiles().Any()).ToList();
                }
                // 如果没有新增文件夹，那么预测的文件夹名就是正确的
                // 如果只新增 1 个文件夹，那么复制 json 文件
                if (DeltaList.Count == 1)
                {
                    ;
                    // [VB Port] restore Dim dropped by codeconv (CommonConversions NRE)
                    var jsonFiles = DeltaList[0].EnumerateFiles().ToList();
                    if (jsonFiles.Count == 0)
                    {
                        Logger.Warn($"新增版本文件夹为空：{DeltaList[0].FullName}");
                    }
                    else
                    {
                        FileInfo JsonFile = jsonFiles[0];
                        FileUtils.Copy(JsonFile.FullName, VersionFolder + NewInstanceName + ".json");
                        Logger.Info($"已复制新增的版本 JSON 文件：{JsonFile.FullName} -> {VersionFolder}{NewInstanceName}.json");
                    }
                }
                else if (DeltaList.Count > 1)
                {
                    // 新增了多个文件夹
                    Logger.Info($"有多个疑似的新增版本，无法确定：{Enumerable.Select((IEnumerable<DirectoryInfo>)DeltaList, d => d.Name).Join(";")}");
                }
                else
                {
                    // 没有新增文件夹
                    Logger.Info("未找到新增的版本文件夹");
                }
            }
            catch (Exception ex)
            {
                throw new Exception($"安装新 {LoaderName} 版本失败", ex);
            }
            finally
            {
                // 清理文件
                try
                {
                    FileUtils.Delete(InstallerAddress);
                }
                catch (Exception ex)
                {
                    Logger.Warn(ex, $"安装 {LoaderName} 清理文件时出错");
                }
            }
        })
                {
                    ProgressWeight = 10d
                });
            }
            else
            {
                Logger.Info($"检测为非新版 Forge：{LoaderVersion}");
                Loaders.Add(new ModLoader.LoaderTask<List<ModNet.NetFile>, bool>($"安装 {LoaderName}（方式 B）", (Task) =>
        {
            ZipArchive Installer = null;
            try
            {
                // 解压并获取信息
                Installer = FileUtils.OpenZip(InstallerAddress);
                Task.Progress = 0.2d;
                var legacyProfileEntry = Installer.GetEntry("install_profile.json");
                if (legacyProfileEntry is null)
                    throw new Exception("安装包缺少 install_profile.json，安装包可能已损坏");
                JObject Json = (JObject)legacyProfileEntry.Open().ReadString().DeserializeJson();
                Task.Progress = 0.4d;
                // 新建版本文件夹
                DirectoryUtils.Create(VersionFolder);
                Task.Progress = 0.5d;
                if (Json["install"] is null)
                {
                    // 中版：Legacy 方式 1
                    Logger.Info($"开始进行 Forge 安装，Legacy 方式 1：{InstallerAddress}");
                    // 建立 Json 文件
                    string legacyJsonName = Json["json"]?.ToString().TrimStart('/');
                    if (string.IsNullOrEmpty(legacyJsonName))
                        throw new Exception("install_profile.json 缺少 json 字段，安装包可能已损坏");
                    var legacyJsonEntry = Installer.GetEntry(legacyJsonName);
                    if (legacyJsonEntry is null)
                        throw new Exception($"安装包缺少版本条目 {legacyJsonName}，安装包可能已损坏");
                    JObject JsonVersion = (JObject)legacyJsonEntry.Open().ReadString().DeserializeJson();
                    JsonVersion["id"] = NewInstanceName;
                    FileUtils.Write(VersionFolder + NewInstanceName + ".json", JsonVersion.ToString());
                    Task.Progress = 0.6d;
                    // 解压支持库文件
                    Installer.Dispose();
                    FileUtils.ExtractToDirectory(InstallerAddress, InstallerAddress + @"_unrar\");
                    if (DirectoryUtils.Exists(InstallerAddress + @"_unrar\maven\"))
                        DirectoryUtils.Copy(InstallerAddress + @"_unrar\maven\", McFolder + @"libraries\");
                    DirectoryUtils.Delete(InstallerAddress + @"_unrar\");
                }
                else
                {
                    // 旧版：Legacy 方式 2
                    Logger.Info($"开始进行 Forge 安装，Legacy 方式 2：{InstallerAddress}");
                    // 解压 Jar 文件
                    var installNode = Json["install"];
                    string installPath = installNode?["path"]?.ToString();
                    string installFile = installNode?["filePath"]?.ToString();
                    if (string.IsNullOrEmpty(installPath) || string.IsNullOrEmpty(installFile))
                        throw new Exception("install_profile.json 缺少 install.path/filePath，安装包可能已损坏");
                    string JarAddress = ModMinecraft.McLibGet(installPath, CustomMcFolder: McFolder);
                    var legacyJarEntry = Installer.GetEntry(installFile);
                    if (legacyJarEntry is null)
                        throw new Exception($"安装包缺少条目 {installFile}，安装包可能已损坏");
                    FileUtils.Write(JarAddress, legacyJarEntry.Open());
                    Task.Progress = 0.9d;
                    // 建立 Json 文件
                    var versionInfo = Json["versionInfo"] as JObject;
                    if (versionInfo is null)
                        throw new Exception("install_profile.json 缺少 versionInfo，安装包可能已损坏");
                    versionInfo["id"] = NewInstanceName;
                    if (versionInfo["inheritsFrom"] is null)
                        versionInfo.Add("inheritsFrom", Inherit);
                    FileUtils.Write(VersionFolder + NewInstanceName + ".json", versionInfo.ToString());
                }
            }
            catch (Exception ex)
            {
                throw new Exception("非新版方式安装 Forge 失败", ex);
            }
            finally
            {
                try
                {
                    // 清理文件
                    if (Installer is not null)
                        Installer.Dispose();
                    FileUtils.Delete(InstallerAddress);
                    DirectoryUtils.Delete(InstallerAddress + @"_unrar\");
                }
                catch (Exception ex)
                {
                    Logger.Warn(ex, "非新版方式安装 Forge 清理文件时出错");
                }
            }
        })
                {
                    ProgressWeight = 1d
                });
            }

            return Loaders;
        }

        #endregion

        #region Forge 下载菜单

        public static void ForgeDownloadListItemPreload(StackPanel Stack, List<ModDownload.DlForgeVersionEntry> Entries, MyListItem.ClickEventHandler OnClick, bool IsSaveOnly)
        {
            // 如果只有一个版本，则不特别列出
            if (Entries.IsSingle())
                return;
            // 获取推荐版本与最新版本
            ModDownload.DlForgeVersionEntry FreshVersion = null;
            if (Entries.Any())
            {
                FreshVersion = Entries[0];
            }
            else
            {
                Logger.Info("未找到可用的 Forge 版本");
            }
            ModDownload.DlForgeVersionEntry RecommendedVersion = null;
            foreach (var Entry in Entries)
            {
                if (Entry.IsRecommended)
                    RecommendedVersion = Entry;
            }
            // 若推荐版本与最新版本为同一版本，则仅显示推荐版本
            if (FreshVersion is not null && ReferenceEquals(FreshVersion, RecommendedVersion))
                FreshVersion = null;
            // 显示各个版本
            if (RecommendedVersion is not null)
            {
                var Recommended = ForgeDownloadListItem(RecommendedVersion, OnClick, IsSaveOnly).Init();
                Recommended.Info = "推荐版" + (string.IsNullOrEmpty(Recommended.Info) ? "" : "，" + Recommended.Info);
                Stack.Children.Add(Recommended);
            }
            if (FreshVersion is not null)
            {
                var Fresh = ForgeDownloadListItem(FreshVersion, OnClick, IsSaveOnly).Init();
                Fresh.Info = "最新版" + (string.IsNullOrEmpty(Fresh.Info) ? "" : "，" + Fresh.Info);
                Stack.Children.Add(Fresh);
            }
            // 添加间隔
            Stack.Children.Add(new TextBlock() { Text = "全部版本 (" + Entries.Count + ")", HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(6d, 13d, 0d, 4d) });
        }
        public static MyVirtualizingElement<MyListItem> ForgeDownloadListItem(ModDownload.DlForgeVersionEntry Entry, MyListItem.ClickEventHandler OnClick, bool IsSaveOnly)
        {
            return new MyVirtualizingElement<MyListItem>(() =>
        {
            // 建立控件
            var NewItem = new MyListItem()
            {
                Title = Entry.VersionName,
                SnapsToDevicePixels = true,
                Height = 42d,
                Type = MyListItem.CheckType.Clickable,
                Tag = Entry,
                Info = (new[] { string.IsNullOrEmpty(Entry.ReleaseTime) ? "" : "发布于 " + Entry.ReleaseTime, ModBase.ModeDebug ? "种类：" + Entry.Category : "" }).Where(d => !string.IsNullOrEmpty(d)).Join("，"),
                Logo = ModBase.PathImage + "Blocks/Anvil.png"
            };
            NewItem.Click += OnClick;
            // 建立菜单
            if (IsSaveOnly)
            {
                NewItem.ContentHandler = ForgeSaveContMenuBuild;
            }
            else
            {
                NewItem.ContentHandler = ForgeContMenuBuild;
            }
            // 结束
            return NewItem;
        })
            {
                Height = 42d
            };
        }
        private static void ForgeContMenuBuild(MyListItem sender, EventArgs e)
        {
            var BtnSave = new MyIconButton() { Logo = ModBase.Logo.IconButtonSave, ToolTip = "另存为" };
            BtnSave.Click += (_, __) => ModDownloadLib.ForgeSave_Click((dynamic)_, (dynamic)__);
            var BtnInfo = new MyIconButton() { LogoScale = 1.05d, Logo = ModBase.Logo.IconButtonInfo, ToolTip = "更新日志" };
            BtnInfo.Click += (_, __) => ModDownloadLib.ForgeLog_Click((dynamic)_, (dynamic)__);
            sender.Buttons = new[] { BtnSave, BtnInfo };
        }
        private static void ForgeSaveContMenuBuild(MyListItem sender, EventArgs e)
        {
            var BtnInfo = new MyIconButton() { LogoScale = 1.05d, Logo = ModBase.Logo.IconButtonInfo, ToolTip = "更新日志" };
            BtnInfo.Click += (_, __) => ModDownloadLib.ForgeLog_Click((dynamic)_, (dynamic)__);
            sender.Buttons = new[] { BtnInfo };
        }
        private static void ForgeLog_Click(object sender, RoutedEventArgs e)
        {
            ModDownload.DlForgeVersionEntry Version;
            if (((dynamic)sender).Tag is not null)
            {
                Version = (ModDownload.DlForgeVersionEntry)((dynamic)sender).Tag;
            }
            else if (((dynamic)sender).Parent.Tag is not null)
            {
                Version = (ModDownload.DlForgeVersionEntry)((dynamic)sender).Parent.Tag;
            }
            else
            {
                Version = (ModDownload.DlForgeVersionEntry)((dynamic)sender).Parent.Parent.Tag;
            }
            ModBase.OpenWebsite($"https://files.minecraftforge.net/maven/net/minecraftforge/forge/{Version.Inherit}-{Version.VersionName}/forge-{Version.Inherit}-{Version.VersionName}-changelog.txt");
        }
        public static void ForgeSave_Click(object sender, RoutedEventArgs e)
        {
            ModDownload.DlForgeVersionEntry Version;
            if (((dynamic)sender).Tag is not null)
            {
                Version = (ModDownload.DlForgeVersionEntry)((dynamic)sender).Tag;
            }
            else if (((dynamic)sender).Parent.Tag is not null)
            {
                Version = (ModDownload.DlForgeVersionEntry)((dynamic)sender).Parent.Tag;
            }
            else
            {
                Version = (ModDownload.DlForgeVersionEntry)((dynamic)sender).Parent.Parent.Tag;
            }
            McDownloadForgelikeSave(Version);
        }

        #endregion

        #region Forge 推荐版本获取

        /// <summary>
    /// 尝试刷新 Forge 推荐版本缓存。
    /// </summary>
        public static void McDownloadForgeRecommendedRefresh()
        {
            if (IsForgeRecommendedRefreshed)
                return;
            IsForgeRecommendedRefreshed = true;
            // 推荐通道 → 扁平表 的解析与两处下限校验已迁至 C#
            ModBase.RunInNewThread(() => { try { Logger.Info("刷新 Forge 推荐版本缓存开始"); string Result = ModNet.NetRequestByClientRetry(Download.ForgeRecommended.PromosUrl, RequireJson: true); string CacheJson = Download.ForgeRecommended.BuildCacheJson(Result); FileUtils.Write(ModBase.PathTemp + @"Cache\ForgeRecommendedList.json", CacheJson); Logger.Info("刷新 Forge 推荐版本缓存成功"); } catch (Exception ex) { Logger.Warn(ex, "刷新 Forge 推荐版本缓存失败"); } }, "ForgeRecommendedRefresh");
        }
        private static bool IsForgeRecommendedRefreshed = false;


        #endregion

        #region NeoForge 下载菜单

        public static void NeoForgeDownloadListItemPreload(StackPanel Stack, List<ModDownload.DlNeoForgeListEntry> Entries, MyListItem.ClickEventHandler OnClick, bool IsSaveOnly)
        {
            // 如果只有一个版本，则不特别列出
            if (Entries.IsSingle())
                return;
            // 获取最新稳定版和测试版
            ModDownload.DlNeoForgeListEntry FreshStableVersion = null;
            ModDownload.DlNeoForgeListEntry FreshBetaVersion = null;
            if (Entries.Any())
            {
                foreach (var Entry in Entries.ToList())
                {
                    if (Entry.IsBeta)
                    {
                        if (FreshBetaVersion is null)
                            FreshBetaVersion = Entry;
                    }
                    else
                    {
                        FreshStableVersion = Entry;
                        break;
                    }
                }
            }
            else
            {
                Logger.Warn("未找到可用的 NeoForge 版本");
            }
            // 显示各个版本
            if (FreshStableVersion is not null)
            {
                var Fresh = NeoForgeDownloadListItem(FreshStableVersion, OnClick, IsSaveOnly).Init();
                Fresh.Info = string.IsNullOrEmpty(Fresh.Info) ? "最新稳定版" : "最新" + Fresh.Info;
                Stack.Children.Add(Fresh);
            }
            if (FreshBetaVersion is not null)
            {
                var Fresh = NeoForgeDownloadListItem(FreshBetaVersion, OnClick, IsSaveOnly).Init();
                Fresh.Info = string.IsNullOrEmpty(Fresh.Info) ? "最新测试版" : "最新" + Fresh.Info;
                Stack.Children.Add(Fresh);
            }
            // 添加间隔
            Stack.Children.Add(new TextBlock() { Text = "全部版本 (" + Entries.Count + ")", HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(6d, 13d, 0d, 4d) });
        }
        public static MyVirtualizingElement<MyListItem> NeoForgeDownloadListItem(ModDownload.DlNeoForgeListEntry Info, MyListItem.ClickEventHandler OnClick, bool IsSaveOnly)
        {
            return new MyVirtualizingElement<MyListItem>(() =>
        {
            // 建立控件
            var NewItem = new MyListItem()
            {
                Title = Info.VersionName,
                SnapsToDevicePixels = true,
                Height = 42d,
                Type = MyListItem.CheckType.Clickable,
                Tag = Info,
                Info = Info.IsBeta ? "测试版" : "稳定版",
                Logo = ModBase.PathImage + "Blocks/NeoForge.png"
            };
            NewItem.Click += OnClick;
            // 建立菜单
            if (IsSaveOnly)
            {
                NewItem.ContentHandler = NeoForgeSaveContMenuBuild;
            }
            else
            {
                NewItem.ContentHandler = NeoForgeContMenuBuild;
            }
            return NewItem;
        })
            {
                Height = 42d
            };
        }
        private static void NeoForgeContMenuBuild(MyListItem sender, EventArgs e)
        {
            var BtnSave = new MyIconButton() { Logo = ModBase.Logo.IconButtonSave, ToolTip = "另存为" };
            BtnSave.Click += (_, __) => ModDownloadLib.NeoForgeSave_Click((dynamic)_, (dynamic)__);
            var BtnInfo = new MyIconButton() { LogoScale = 1.05d, Logo = ModBase.Logo.IconButtonInfo, ToolTip = "更新日志" };
            BtnInfo.Click += (_, __) => ModDownloadLib.NeoForgeLog_Click((dynamic)_, (dynamic)__);
            sender.Buttons = new[] { BtnSave, BtnInfo };
        }
        private static void NeoForgeSaveContMenuBuild(MyListItem sender, EventArgs e)
        {
            var BtnInfo = new MyIconButton() { LogoScale = 1.05d, Logo = ModBase.Logo.IconButtonInfo, ToolTip = "更新日志" };
            BtnInfo.Click += (_, __) => ModDownloadLib.NeoForgeLog_Click((dynamic)_, (dynamic)__);
            sender.Buttons = new[] { BtnInfo };
        }
        private static void NeoForgeLog_Click(object sender, RoutedEventArgs e)
        {
            ModDownload.DlNeoForgeListEntry Info;
            if (((dynamic)sender).Tag is not null)
            {
                Info = (ModDownload.DlNeoForgeListEntry)((dynamic)sender).Tag;
            }
            else if (((dynamic)sender).Parent.Tag is not null)
            {
                Info = (ModDownload.DlNeoForgeListEntry)((dynamic)sender).Parent.Tag;
            }
            else
            {
                Info = (ModDownload.DlNeoForgeListEntry)((dynamic)sender).Parent.Parent.Tag;
            }
            ModBase.OpenWebsite(Info.UrlBase + "-changelog.txt");
        }
        public static void NeoForgeSave_Click(object sender, RoutedEventArgs e)
        {
            ModDownload.DlNeoForgeListEntry Info;
            if (((dynamic)sender).Tag is not null)
            {
                Info = (ModDownload.DlNeoForgeListEntry)((dynamic)sender).Tag;
            }
            else if (((dynamic)sender).Parent.Tag is not null)
            {
                Info = (ModDownload.DlNeoForgeListEntry)((dynamic)sender).Parent.Tag;
            }
            else
            {
                Info = (ModDownload.DlNeoForgeListEntry)((dynamic)sender).Parent.Parent.Tag;
            }
            McDownloadForgelikeSave(Info);
        }

        #endregion

        #region Fabric 下载

        public static void McDownloadFabricLoaderSave(JObject DownloadInfo)
        {
            try
            {
                string Url = DownloadInfo["url"]?.ToString() ?? "";
                string FileName = PathUtils.GetLastPart(Url);
                string Version = PathUtils.GetLastPart(DownloadInfo["version"]?.ToString() ?? "");
                string Target = Dialogs.SaveFile("选择保存位置", FileName, filter: new[] { ("jar", "Fabric 安装器") });
                if (Target is null)
                    return;

                // 重复任务检查
                foreach (var OngoingLoader in ModLoader.LoaderTaskbar.ToList())
                {
                    if ((OngoingLoader.Name ?? "") != ($"Fabric {Version} 安装器下载" ?? ""))
                        continue;
                    ModMain.Hint("该版本正在下载中！", ModMain.HintType.Red);
                    return;
                }

                // 构造步骤加载器
                var Loaders = new List<ModLoader.LoaderBase>();
                // 下载
                // BMCLAPI 不支持 Fabric Installer 下载
                var Address = new List<string>();
                Address.Add(Url);
                Loaders.Add(new ModNet.LoaderDownload("下载主文件", new List<ModNet.NetFile>() { new ModNet.NetFile(Address.ToArray(), Target, new FileChecker() { MinSize = 1024 * 64 }) }) { ProgressWeight = 15d });
                // 启动
                var Loader = new ModLoader.LoaderCombo<JObject>("Fabric " + Version + " 安装器下载", Loaders) { OnStateChanged = LoaderStateChangedHintOnly };
                Loader.Start(DownloadInfo);
                ModLoader.LoaderTaskbarAdd(Loader);
                ModMain.FrmMain.BtnExtraDownload.ShowRefresh();
                ModMain.FrmMain.BtnExtraDownload.Ribble();
            }

            catch (Exception ex)
            {
                Logger.Error(ex, "开始 Fabric 安装器下载失败");
            }
        }

        /// <summary>
    /// 获取下载某个 Fabric 版本的加载器列表。
    /// 这不会补全所需的支持库文件，需要后续手动补全支持库。
    /// </summary>
        private static List<ModLoader.LoaderBase> McDownloadFabricLoader(string FabricVersion, string MinecraftName, string McFolder)
        {

            // Fabric 主文件的「下载什么、下到哪、是否 JSON 校验」由内核 InstallDownloads.FabricMainFile 统一产出，
            // 实例名与地址的口径（含 ∞→infinite 的顺序坑）都收进 C#，VB 只负责把它包成 NetFile（不动 LoaderBase 签名）。
            var Loaders = new List<ModLoader.LoaderBase>();

            // 下载 Json
            Loaders.Add(new ModLoader.LoaderTask<string, List<ModNet.NetFile>>("获取 Fabric 主文件下载地址", (Task) =>
        {
            var Desc = Download.InstallDownloads.FabricMainFile(FabricVersion, MinecraftName, McFolder);
            Task.Output = new List<ModNet.NetFile>() { new ModNet.NetFile(Desc.Urls, Desc.LocalPath, new FileChecker() { IsJson = Desc.IsJson }) }; // 构造文件请求
        })
            {
                ProgressWeight = 0.5d
            });
            Loaders.Add(new ModNet.LoaderDownload("下载 Fabric 主文件", new List<ModNet.NetFile>()) { ProgressWeight = 2.5d });

            return Loaders;
        }

        #endregion

        #region Fabric 下载菜单

        public static MyVirtualizingElement<MyListItem> FabricDownloadListItem(JObject Entry, MyListItem.ClickEventHandler OnClick)
        {
            return new MyVirtualizingElement<MyListItem>(() =>
        {
            var NewItem = new MyListItem()
            {
                Title = Entry["version"]?.ToString().Replace("+build", "") ?? "",
                SnapsToDevicePixels = true,
                Height = 42d,
                Type = MyListItem.CheckType.Clickable,
                Tag = Entry,
                Info = (Entry["stable"] is not null && Entry["stable"].Type != JTokenType.Null && Entry["stable"].ToObject<bool>()) ? "稳定版" : "测试版",
                Logo = ModBase.PathImage + "Blocks/Fabric.png"
            };
            NewItem.Click += OnClick;
            return NewItem;
        })
            {
                Height = 42d
            };
        }
        public static MyVirtualizingElement<MyListItem> FabricApiDownloadListItem(ResourceVersion Entry, MyListItem.ClickEventHandler OnClick)
        {
            return new MyVirtualizingElement<MyListItem>(() =>
        {
            var NewItem = new MyListItem()
            {
                Title = ((Entry.Display ?? "").Split("]") is var displayParts && displayParts.Length > 1 ? displayParts[1] : Entry.Display ?? "").Replace("Fabric API ", "").Replace(" build ", ".").BeforeFirst("+").Trim(),
                SnapsToDevicePixels = true,
                Height = 42d,
                Type = MyListItem.CheckType.Clickable,
                Tag = Entry,
                Info = Entry.ReleaseTypeDisplay + "，发布于 " + Entry.ReleaseDate.ToString("yyyy'/'MM'/'dd HH':'mm"),
                Logo = ModBase.PathImage + "Blocks/Fabric.png"
            };
            NewItem.Click += OnClick;
            return NewItem;
        })
            {
                Height = 42d
            };
        }
        public static MyVirtualizingElement<MyListItem> OptiFabricDownloadListItem(ResourceVersion Entry, MyListItem.ClickEventHandler OnClick)
        {
            return new MyVirtualizingElement<MyListItem>(() =>
        {
            var NewItem = new MyListItem()
            {
                Title = Entry.Display.Lower().Replace("optifabric-", "").Replace(".jar", "").Trim().TrimStart('v'),
                SnapsToDevicePixels = true,
                Height = 42d,
                Type = MyListItem.CheckType.Clickable,
                Tag = Entry,
                Info = Entry.ReleaseTypeDisplay + "，发布于 " + Entry.ReleaseDate.ToString("yyyy'/'MM'/'dd HH':'mm"),
                Logo = ModBase.PathImage + "Blocks/OptiFabric.png"
            };
            NewItem.Click += OnClick;
            return NewItem;
        })
            {
                Height = 42d
            };
        }

        #endregion

        #region 安装

        /// <summary>
    /// 安装请求。
    /// </summary>
        public class McInstallRequest
        {
            /// <summary>
        /// 新版本安装必填。安装目标版本名称。
        /// </summary>
            public string NewInstanceName;
            /// <summary>
        /// 必填。安装目标文件夹。
        /// </summary>
            public string InstanceFolder;
            /// <summary>
        /// 必填。欲安装的 Minecraft 与组件版本。
        /// </summary>
            public McComponents Components;
            /// <summary>
        /// 可选。欲下载的 Minecraft Json 地址。
        /// </summary>
            public string MinecraftJson = null;
            /// <summary>
        /// 欲下载的 Forge。
        /// </summary>
            public ModDownload.DlForgeVersionEntry ForgeEntry = null;
            /// <summary>
        /// 欲下载的 NeoForge。
        /// </summary>
            public ModDownload.DlNeoForgeListEntry NeoForgeEntry = null;
            /// <summary>
        /// 欲下载的 Fabric API 信息。
        /// </summary>
            public ResourceVersion FabricApi = null;
            /// <summary>
        /// 欲下载的 OptiFabric 信息。
        /// </summary>
            public ResourceVersion OptiFabric = null;
            /// <summary>
        /// 欲下载的 LiteLoader 详细信息。
        /// </summary>
            public ModDownload.DlLiteLoaderListEntry LiteLoaderEntry = null;
            /// <summary>
        /// 是否为中间产物。
        /// </summary>
            public bool IsIntermediateOutput = false;
        }

        /// <summary>
    /// 获取合并安装加载器列表。
    /// </summary>
        public static List<ModLoader.LoaderBase> McInstallLoadersGet(McInstallRequest Request)
        {
            // 获取缓存目录（安装 Mod 加载器的文件夹不能包含空格）
            string TempMcFolder = ModMain.RequestTaskTempFolder(Request.Components.OptiFineEntry is not null || Request.Components.ModLoaderVersions.ContainsKey(ModLoaders.Forge) || Request.Components.ModLoaderVersions.ContainsKey(ModLoaders.NeoForge));

            // 获取各个 Mod 加载器的缓存文件夹（推导规则见 PCL.Download.InstallLayout）
            var OptiFineEntry = Request.Components.OptiFineEntry?.ToObject<ModDownload.DlOptiFineListEntry>();
            string ForgeVersion = Request.Components.ModLoaderVersions.ContainsKey(ModLoaders.Forge) ? Request.Components.ModLoaderVersions[ModLoaders.Forge] : null;
            string NeoForgeVersion = Request.Components.ModLoaderVersions.ContainsKey(ModLoaders.NeoForge) ? Request.Components.ModLoaderVersions[ModLoaders.NeoForge] : null;
            string LiteLoaderVersion = Request.Components.ModLoaderVersions.ContainsKey(ModLoaders.LiteLoader) ? Request.Components.ModLoaderVersions[ModLoaders.LiteLoader] : null;
            string FabricVersion = Request.Components.ModLoaderVersions.ContainsKey(ModLoaders.Fabric) ? Request.Components.ModLoaderVersions[ModLoaders.Fabric] : null;

            var Layout = Download.InstallLayout.Create(new Download.InstallLayoutRequest
            {
                TempFolder = TempMcFolder,
                MinecraftVersion = Request.Components.MinecraftVersion,
                OptiFineInstanceName = OptiFineEntry?.InstanceName,
                ForgeVersion = ForgeVersion,
                NeoForgeVersion = NeoForgeVersion,
                LiteLoaderVersion = LiteLoaderVersion,
                FabricVersion = FabricVersion,
                HasAnyModLoader = Request.Components.ModLoaderVersions.Any(),
            });

            bool Modable = Request.Components.ModLoaderVersions.Any();
            string ModsTempFolder = Layout.ModsRoot;
            string OptiFineFolder = Layout.OptiFineFolder;
            bool OptiFineAsMod = Layout.OptiFineAsMod; // 选择了 OptiFine 与任意 Mod 加载器
            string ForgeFolder = Layout.ForgeFolder;
            string NeoForgeFolder = Layout.NeoForgeFolder;
            string LiteLoaderFolder = Layout.LiteLoaderFolder;
            string FabricFolder = Layout.FabricFolder;
            if (OptiFineAsMod)
                Logger.Info("OptiFine 将作为 Mod 进行下载");

            // 记录日志
            foreach (var CacheLine in Layout.LogLines())
                Logger.Info(CacheLine);
            Logger.Info($"对应的原版版本：{Request.Components.MinecraftVersion}");

            // 重复版本检查
            string VersionFolder = PathUtils.AddSlashSuffix(Request.InstanceFolder);
            if (FileUtils.Exists($"{VersionFolder}{Request.NewInstanceName}.json"))
            {
                ModMain.Hint("版本 " + Request.NewInstanceName + " 已经存在！", ModMain.HintType.Red);
                throw new OperationCanceledException();
            }

            // （桥接层）编排决策改由 C# 产出：步骤包含 / 顺序 / 权重 / Block / Show 全部来自 InstallPlan。
            // P4-B：HasMinecraftJson 由 VB 显式传入，供 C# 描述 Client 的可选子步骤骨架。
            var Plan = Download.InstallPlanner.Build(new Download.InstallPlanRequest()
            {
                MinecraftVersion = Request.Components.MinecraftVersion,
                ForgeVersion = ForgeVersion,
                NeoForgeVersion = NeoForgeVersion,
                LiteLoaderVersion = LiteLoaderVersion,
                FabricVersion = FabricVersion,
                HasOptiFineEntry = OptiFineEntry is not null,
                OptiFineDisplayName = OptiFineEntry is null ? null : OptiFineEntry.DisplayName,
                OptiFineAsMod = OptiFineAsMod,
                HasFabricApi = Request.Components.FabricApiVersion is not null,
                HasOptiFabric = Request.Components.OptiFabricVersion is not null,
                HasForgeEntry = Request.ForgeEntry is not null,
                HasNeoForgeEntry = Request.NeoForgeEntry is not null,
                HasLiteLoaderEntry = Request.LiteLoaderEntry is not null,
                HasAnyModLoader = Request.Components.ModLoaderVersions.Any(),
                IsIntermediateOutput = Request.IsIntermediateOutput,
                HasMinecraftJson = Request.MinecraftJson is not null
            });

            // （P4-B）装配全部交给 LoaderPlanAdapter（Modules/Download/LoaderPlanAdapter.vb）：
            // 遍历 InstallTaskPlanner.Describe(Plan)，按 Kind 分派构造 LoaderBase，
            // 名称 / 权重 / Block / Show / 输出槽位一律读 DTO，此处不再内联任何业务判定。
            var Ctx = new InstallLoaderContext()
            {
                Request = Request,
                Plan = Plan,
                TempMcFolder = TempMcFolder,
                Layout = Layout,
                OptiFineEntry = OptiFineEntry,
                ForgeVersion = ForgeVersion,
                NeoForgeVersion = NeoForgeVersion,
                LiteLoaderVersion = LiteLoaderVersion,
                FabricVersion = FabricVersion,
                VersionFolder = VersionFolder,
                ModsTempFolder = ModsTempFolder,
                OptiFineFolder = OptiFineFolder,
                ForgeFolder = ForgeFolder,
                NeoForgeFolder = NeoForgeFolder,
                LiteLoaderFolder = LiteLoaderFolder,
                FabricFolder = FabricFolder,
                OptiFineAsMod = OptiFineAsMod,
                Modable = Modable,
                MinecraftFolder = ModMinecraft.McFolderSelected,
                ManagedFileSources = new Dictionary<string, string>()
            };
            return LoaderPlanAssemble(Ctx);
        }
        /// <summary>
    /// 合并原版与各加载器的 json，输出为一份独立版本 json。
    /// </summary>
    /// <param name="VersionFolder">输出目录，同时也是原版 profile 的所在目录。</param>
    /// <param name="OptiFineFolder">OptiFine 目录。作为 Mod 安装时传 Nothing。</param>
    /// <param name="OptiFineAsMod">OptiFine 是否以 Mod 形式安装。为真时不参与合并。</param>
    /// <param name="ForgeFolder">Forge 目录，未安装传 Nothing。</param>
    /// <param name="NeoForgeFolder">NeoForge 目录，未安装传 Nothing。</param>
    /// <param name="FabricFolder">Fabric 目录，未安装传 Nothing。</param>
    /// <param name="LiteLoaderFolder">LiteLoader 目录，未安装传 Nothing。</param>
    /// <remarks>
    /// 合并顺序即叠加顺序，决定同名标量（如 mainClass）最终归属。具体合并规则见
    /// <c>PCL.Download.VersionMerger</c>；此处只负责按安装形态挑选参与合并的目录。
    /// </remarks>
        private static void MergeJson(string VersionFolder, string OptiFineFolder, bool OptiFineAsMod, string ForgeFolder, string NeoForgeFolder, string FabricFolder, string LiteLoaderFolder)
        {
            var Sources = new List<Download.VersionMergeSource>() { new Download.VersionMergeSource("Minecraft", VersionFolder) };
            if (OptiFineFolder is not null && !OptiFineAsMod)
            {
                Sources.Add(new Download.VersionMergeSource("OptiFine", OptiFineFolder));
            }
            if (ForgeFolder is not null)
                Sources.Add(new Download.VersionMergeSource("Forge", ForgeFolder));
            if (NeoForgeFolder is not null)
                Sources.Add(new Download.VersionMergeSource("NeoForge", NeoForgeFolder));
            if (LiteLoaderFolder is not null)
            {
                Sources.Add(new Download.VersionMergeSource("LiteLoader", LiteLoaderFolder));
            }
            if (FabricFolder is not null)
                Sources.Add(new Download.VersionMergeSource("Fabric", FabricFolder));
            Download.VersionMerger.MergeToDirectory(Sources, VersionFolder);
        }
        /// <summary>
    /// 进行合并安装。返回是否已经开始安装。
    /// </summary>
        public static bool McInstallStart(McInstallRequest Request)
        {
            try
            {
                var Loader = new ModLoader.LoaderCombo<string>(Request.NewInstanceName + " 安装", McInstallLoadersGet(Request)) { OnStateChanged = McInstallState };
                // 启动
                Loader.Start(Request.InstanceFolder);
                ModLoader.LoaderTaskbarAdd(Loader);
                ModMain.FrmMain.BtnExtraDownload.ShowRefresh();
                ModMain.FrmMain.BtnExtraDownload.Ribble();
                return true;
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "开始合并安装失败");
                return false;
            }
        }
        /// <summary>
    /// 进行修改安装。返回是否已经开始安装。
    /// </summary>
        public static bool McModificationStart(McInstallRequest Request, ModMinecraft.McInstance Instance)
        {
            try
            {
                // 检查版本目录下的 Natives，防止修改正在运行的游戏
                string TargetFolder = Instance.PathIndie;
                var SubLoaders = new List<ModLoader.LoaderBase>();
                SubLoaders.Add(new ModLoader.LoaderTask<int, int>("检查 Natives 文件占用", (_) => { foreach (var File in DirectoryUtils.EnumerateFiles(ModLaunch.GetNativesFolder(Instance), includeSubDirectories: true, searchPattern: "*.dll")) { if (!FileUtils.IsAvailable(File)) throw new Exception($"Natives 文件正在被占用，请关闭游戏后再次尝试：{File}"); } }) { Show = false });
                // 安装修改后的 MC 版本
                string BuildFolder = ModMain.RequestTaskTempFolder() + Instance.Name + @"\";
                var BuildRequest = new McInstallRequest()
                {
                    NewInstanceName = Instance.Name,
                    InstanceFolder = BuildFolder,
                    Components = Request.Components,
                    MinecraftJson = Request.MinecraftJson,
                    ForgeEntry = Request.ForgeEntry,
                    NeoForgeEntry = Request.NeoForgeEntry,
                    FabricApi = Request.FabricApi,
                    OptiFabric = Request.OptiFabric,
                    LiteLoaderEntry = Request.LiteLoaderEntry,
                    IsIntermediateOutput = true
                };
                SubLoaders.AddRange(McInstallLoadersGet(BuildRequest));
                // 版本合并
                bool RenameFailed = false;
                SubLoaders.Add(new ModLoader.LoaderTask<int, int>("修改版本", Task =>
        {
            // TODO: 提交前显示文件修改列表（大量文件按“XXX 文件夹下 XXX 项”汇总），可以询问玩家是否覆盖原版本或作为新版本安装（不覆盖）？
            McInstallCommit(Instance, Configs.McInstallManifest.Get(new ModMinecraft.McInstance(BuildFolder).Config), BuildFolder, TargetFolder);
            // 重命名和引用交接
            ModMinecraft.McInstance NewInstance = null;
            if (Request.NewInstanceName is null)
            {
                NewInstance = new ModMinecraft.McInstance(Instance.PathVersion).Load();
            }
            else
            {
                try
                {
                    NewInstance = Instance.Rename(Request.NewInstanceName);
                }
                catch (Exception ex)
                {
                    RenameFailed = true;
                    Logger.Warn(ex, $"版本修改成功，但重命名为 {Request.NewInstanceName} 失败：{ex.GetDisplay(false)}", LogBehavior.None);
                    NewInstance = new ModMinecraft.McInstance(Instance.PathVersion).Load();
                }
            }
            if (NewInstance is null)
                NewInstance = new ModMinecraft.McInstance(Instance.PathVersion).Load();
            ModBase.RunInUiWait(() =>
        {
                ModBase.WriteIni(ModMinecraft.McFolderSelected + "PCL.ini", "InstanceCache", "");
            // 未切换则选中修改后的版本
                if (PageInstanceLeft.Instance is not null && PathUtils.IsSame(PageInstanceLeft.Instance.PathVersion, Instance.PathVersion))
                {
                    PageInstanceLeft.Instance = NewInstance;
                }
                if (ModMinecraft.McInstanceSelected is not null && PathUtils.IsSame(ModMinecraft.McInstanceSelected.PathVersion, Instance.PathVersion))
                {
                    ModMinecraft.McInstanceSelected = NewInstance;
                }
            });
        })
                {
                    ProgressWeight = 1d,
                    Block = true
                });
                // 实际修改任务
                // 流程收尾
                var Loader = new ModLoader.LoaderCombo<string>(Instance.Name + " 修改", SubLoaders) { OnStateChanged = new Action<ModLoader.LoaderBase>(CurrentLoader => { switch (CurrentLoader.State) { case ModBase.LoadState.Finished: { if (!RenameFailed) { ModMain.Hint(CurrentLoader.Name + "成功！", ModMain.HintType.Green); } else { ModMain.Hint(CurrentLoader.Name + "成功，但重命名失败！", ModMain.HintType.Green); } break; } case ModBase.LoadState.Failed: { ModMain.MyMsgBox(CurrentLoader.Error.GetDisplay(true), CurrentLoader.Name + "失败", IsWarn: true); break; } case ModBase.LoadState.Canceled: { ModMain.Hint(CurrentLoader.Name + "已取消！", ModMain.HintType.Blue); break; } } }) };
                var Worker = new RedoableWorker(() => Loader.WaitForExit(Instance.PathVersion));
                // 启动
                if (ModMinecraft.McInstanceLock.TryAcquire(Worker, Instance.PathVersion))
                {
                    Worker.Stopped += () => ModBase.RunInUi(() => ModLoader.LoaderFolderRun(ModMinecraft.McInstanceListLoader, ModMinecraft.McFolderSelected, ModLoader.LoaderFolderRunType.ForceRun, MaxDepth: 1, ExtraPath: @"versions\"));
                    Worker.Start();
                    // 加锁后刷新版本列表，隐藏正在修改的实例。
                    ModLoader.LoaderFolderRun(ModMinecraft.McInstanceListLoader, ModMinecraft.McFolderSelected, ModLoader.LoaderFolderRunType.ForceRun, MaxDepth: 1, ExtraPath: @"versions\");
                }
                else
                {
                    Logger.Error("版本被其他 Worker 占用，版本修改失败");
                    return false;
                }
                ModLoader.LoaderTaskbarAdd(Loader);
                ModMain.FrmMain.BtnExtraDownload.ShowRefresh();
                ModMain.FrmMain.BtnExtraDownload.Ribble();
                return true;
            }
            catch (Exception ex)
            {
                ModLoader.LoaderFolderRun(ModMinecraft.McInstanceListLoader, ModMinecraft.McFolderSelected, ModLoader.LoaderFolderRunType.ForceRun, MaxDepth: 1, ExtraPath: @"versions\");
                Logger.Error(ex, "开始修改版本失败", LogBehavior.Toast);
                return false;
            }
        }

        /// <summary>
    /// 将安装好的临时版本文件夹覆盖到一个现有文件夹中。
    /// </summary>
        public static void McInstallCommit(ModMinecraft.McInstance Instance, McInstallManifest NewManifest, string BuildFolder, string TargetFolder)
        {
            var OldFiles = (Configs.McInstallManifest.Get(Instance.Config)?.ManagedFiles) ?? new List<McInstallManifest.ManagedFile>();
            var NewFiles = NewManifest.ManagedFiles;
            string OldJsonPath = Instance.GetJsonPath();

            // ============== 整理变更的文件列表 ==============
            // 哪些覆盖、哪些删除、旧版 resources 要不要一起搬，规则见 PCL.Download.InstallCommitPlanner
            var Plan = Download.InstallCommitPlanner.Build(new Download.InstallCommitPlanRequest()
            {
                BuildFolder = BuildFolder,
                TargetFolder = TargetFolder,
                VersionFolder = Instance.PathVersion ?? "",
                InstanceName = Instance.Name,
                OldJsonPath = OldJsonPath,
                NewRelativePaths = NewFiles.Select(File => File.RelativePath).ToList(),
                OldRelativePaths = OldFiles.Select(File => File.RelativePath).ToList(),
                LegacyResourcesExist = DirectoryUtils.Exists(BuildFolder + @"resources\")
            });

            // ============== 实际修改 ==============

            // 老版本 MC 的 assets 文件
            if (Plan.CopyLegacyResources)
                DirectoryUtils.Copy(BuildFolder + @"resources\", TargetFolder + @"resources\");
            Instance.Config.Save();
            foreach (var TargetPath in Plan.FilesToDelete)
                FileUtils.Delete(TargetPath, true);
            FileUtils.Move(BuildFolder + Instance.Name + ".jar", Instance.PathVersion + Instance.Name + ".jar");
            FileUtils.Move(BuildFolder + Instance.Name + ".json", OldJsonPath);
            foreach (var File in Plan.FilesToMove)
            {
                DirectoryUtils.Create(PathUtils.RemoveLastPart(File.Target));
                FileUtils.Copy(File.Source, File.Target);
            }
            var ManagedFiles = NewFiles.Select(File => new McInstallManifest.ManagedFile(File.RelativePath, File.Sha1, FileUtils.GetInfo(Path.Combine(TargetFolder, File.RelativePath)).LastWriteTimeUtc, File.Component)).ToList();
            Configs.McInstallManifest.Set(new McInstallManifest(NewManifest.SchemaVersion, NewManifest.Components, NewManifest.LastModifiedUtc, ManagedFiles), Instance.Config);
            Instance.Config.Save();
        }

        /// <summary>
    /// 在加载器状态改变后显示一条提示。
    /// 不会进行任何其他操作。
    /// </summary>
        public static void LoaderStateChangedHintOnly(ModLoader.LoaderBase Loader)
        {
            switch (Loader.State)
            {
                case ModBase.LoadState.Finished:
                    {
                        ModMain.Hint(Loader.Name + "成功！", ModMain.HintType.Green);
                        break;
                    }
                case ModBase.LoadState.Failed:
                    {
                        ModMain.Hint(Loader.Name + "失败：" + Loader.Error.GetDisplay(false), ModMain.HintType.Red);
                        break;
                    }
                case ModBase.LoadState.Canceled:
                    {
                        ModMain.Hint(Loader.Name + "已取消！", ModMain.HintType.Blue);
                        break;
                    }
            }
        }
        /// <summary>
    /// 安装加载器状态改变后进行提示和重载文件夹列表的方法。
    /// </summary>
        public static void McInstallState(ModLoader.LoaderBase Loader)
        {
            switch (Loader.State)
            {
                case ModBase.LoadState.Finished:
                    {
                        ModBase.WriteIni(ModMinecraft.McFolderSelected + "PCL.ini", "InstanceCache", ""); // 清空缓存（合并安装会先生成文件夹，这会在刷新时误判为可以使用缓存）
                        ModMain.Hint(Loader.Name + "成功！", ModMain.HintType.Green);
                        break;
                    }
                // TODO: 自动选择安装成功的版本
                case ModBase.LoadState.Failed:
                    {
                        ModMain.MyMsgBox(Loader.Error.GetDisplay(true), Loader.Name + "失败", IsWarn: true);
                        break;
                    }
                case ModBase.LoadState.Canceled:
                    {
                        ModMain.Hint(Loader.Name + "已取消！", ModMain.HintType.Blue);
                        break;
                    }
                case ModBase.LoadState.Loading:
                    {
                        return; // 不重新加载版本列表
                    }
            }
            McInstallFailedClearFolder(Loader);
            ModLoader.LoaderFolderRun(ModMinecraft.McInstanceListLoader, ModMinecraft.McFolderSelected, ModLoader.LoaderFolderRunType.ForceRun, MaxDepth: 1, ExtraPath: @"versions\");
        }
        public static void McInstallFailedClearFolder(object Loader)
        {
            if (Conversions.ToBoolean(Operators.ConditionalCompareObjectNotEqual(((dynamic)Loader).State, ModBase.LoadState.Failed, false)) && Conversions.ToBoolean(Operators.ConditionalCompareObjectNotEqual(((dynamic)Loader).State, ModBase.LoadState.Canceled, false)))
                return;
            // 删除版本文件夹
            ModBase.RunInThread(() => { try { Thread.Sleep(1000); if (DirectoryUtils.Exists(Conversions.ToString(Operators.ConcatenateObject(((dynamic)Loader).Input, @"saves\"))) || DirectoryUtils.Exists(Conversions.ToString(Operators.ConcatenateObject(((dynamic)Loader).Input, @"versions\")))) { Logger.Warn($"由于版本已被独立启动，不清理版本文件夹：{Conversions.ToString((object)((dynamic)Loader).Input)}"); } else { Logger.Warn($"由于下载失败或取消，清理版本文件夹：{Conversions.ToString((object)((dynamic)Loader).Input)}"); DirectoryUtils.Delete(Conversions.ToString(((dynamic)Loader).Input)); } } catch (Exception ex) { Logger.Warn(ex, "下载失败或取消后清理版本文件夹失败"); } }); // 防止存在尚未完全释放的文件，导致清理失败（例如整合包安装）
        }

        #endregion

        /// <summary>
    /// 如果 OptiFine 与 Forge 同时开始安装，就会导致 Forge 安装失败。
    /// </summary>
        private static object InstallSyncLock = new object();
        /// <summary>
    /// 如果 OptiFine 与 Forge 同时复制原版 JAR，就会导致复制文件时冲突。
    /// </summary>
        private static object VanillaSyncLock = new object();

    }
}