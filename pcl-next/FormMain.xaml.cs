using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using MeloongCore;
using MeloongCore.Extensions;
using MeloongCore.Wpf.Extensions;
using Microsoft.VisualBasic;
using Microsoft.VisualBasic.CompilerServices;
using Newtonsoft.Json.Linq;

namespace PCL
{

    public partial class FormMain
    {

        #region 基础

        // 更新日志
        private void ShowUpdateLog(int LastVersion)
        {
            int FeatureCount = 0;
            int BugCount = 0;
            var FeatureList = new List<KeyValuePair<int, string>>();
            // 统计更新日志条目
            if (ModBuildChannel.ChannelIsOfficial)
            {
                if (LastVersion < 5070) // PC 2.100.0.0
                {
                    FeatureList.Add(new KeyValuePair<int, string>(1, "重写：主程序由 .NET Framework 迁移到 .NET 10"));
                }
                if (LastVersion < 415) // Release 2.99.0.1
                {
                    FeatureList.Add(new KeyValuePair<int, string>(4, "删除：MC 下载功能相关入口"));
                    FeatureList.Add(new KeyValuePair<int, string>(1, "优化：为设置更新添加部分黑名单"));
                }
                if (LastVersion < 413) // Release 2.99.0.0
                {
                    FeatureList.Add(new KeyValuePair<int, string>(5, "删除：为遵守相关法律法规，PCL Next 不再提供 MC 下载功能，非常抱歉 qwq……"));
                }
                if (LastVersion < 411) // Release 2.14.0.0
                {
                    FeatureList.Add(new KeyValuePair<int, string>(4, "新增：允许修改、添加、移除、更新已安装版本的各个组件，可从版本设置中进入"));
                    FeatureList.Add(new KeyValuePair<int, string>(2, "优化：启动游戏时允许按 Esc 取消"));
                    FeatureList.Add(new KeyValuePair<int, string>(2, "优化：允许复制主页的启动日志中的内容"));
                    FeatureCount += 19;
                    BugCount += 26;
                }
                if (LastVersion < 408) // Release 2.13.1.1
                {
                    FeatureList.Add(new KeyValuePair<int, string>(2, "优化：版本设置中，添加打开截图文件夹的快捷方式"));
                    FeatureList.Add(new KeyValuePair<int, string>(2, "优化：导出整合包时，可以勾选导出光影设置文件"));
                    FeatureCount += 23;
                    BugCount += 28;
                }
                if (LastVersion < 406) // Release 2.13.0.1
                {
                    FeatureList.Add(new KeyValuePair<int, string>(4, "新增：重做 Java 管理与相关设置，允许调整 Java 优先级、指定 Java 版本范围等"));
                    FeatureList.Add(new KeyValuePair<int, string>(3, "优化：导出整合包时允许自动导出版本文件夹中的 Java"));
                    FeatureList.Add(new KeyValuePair<int, string>(3, "优化：重做弹出提示的样式以及动画，以更符合现代 UI 审美"));
                    FeatureList.Add(new KeyValuePair<int, string>(1, "修复：可能无法安装 OptiFine 26.1.2"));
                    FeatureCount += 35;
                    BugCount += 17;
                }
                if (LastVersion < 404) // Release 2.12.8.2
                {
                    FeatureList.Add(new KeyValuePair<int, string>(1, "修复：Windows 7 无法正常联网"));
                    FeatureCount += 1;
                    BugCount += 2;
                }
                if (LastVersion < 402) // Release 2.12.8.1
                {
                    FeatureList.Add(new KeyValuePair<int, string>(2, "优化：升级框架到 .NET Framework 4.8"));
                    FeatureCount += 35;
                    BugCount += 21;
                }
                if (LastVersion < 398) // Release 2.12.7.3
                {
                    FeatureList.Add(new KeyValuePair<int, string>(2, "优化：下载游戏、整合包的稳定性"));
                    FeatureCount += 1;
                    BugCount += 5;
                }
                if (LastVersion < 391) // Release 2.12.6.2
                {
                    FeatureList.Add(new KeyValuePair<int, string>(1, "修复：会为部分 Forge / NeoForge 选择不支持的 Java"));
                    BugCount += 4;
                }
                if (LastVersion < 389) // Release 2.12.6.1
                {
                    if (LastVersion == 387)
                        FeatureList.Add(new KeyValuePair<int, string>(2, "修复：无法使用 Java 25+ 启动 Forge"));
                    FeatureCount += 4;
                    BugCount += 3;
                }
                if (LastVersion < 387) // Release 2.12.6
                {
                    FeatureList.Add(new KeyValuePair<int, string>(3, "优化：降低 Minecraft 的内存占用"));
                    FeatureList.Add(new KeyValuePair<int, string>(2, "修复：Minecraft 使用 Java 25+ 时的一个性能问题"));
                    FeatureList.Add(new KeyValuePair<int, string>(2, "修复：无法下载 NeoForge 26.1"));
                    FeatureList.Add(new KeyValuePair<int, string>(2, "修复：部分关键词搜不到资源，特别是中文 Mod 搜索经常没有结果"));
                    FeatureList.Add(new KeyValuePair<int, string>(1, "修复：老版本 Windows 无法启动游戏"));
                    FeatureList.Add(new KeyValuePair<int, string>(1, "修复：连接 Mojang 的服务可能失败，提示错误码 421"));
                    FeatureList.Add(new KeyValuePair<int, string>(1, "修复：无法访问爱发电"));
                    FeatureCount += 24;
                    BugCount += 19;
                }
                if (LastVersion < 383) // Release 2.12.3
                {
                    FeatureList.Add(new KeyValuePair<int, string>(2, "修复：部分关键词搜不到资源，特别是中文 Mod 搜索经常没有结果"));
                    FeatureList.Add(new KeyValuePair<int, string>(2, "修复：下载可能完全卡住，或是下载进度反复回退"));
                    FeatureList.Add(new KeyValuePair<int, string>(1, "新增：内存管理设置，可以选择 G1GC、ZGC 或分代 ZGC"));
                    FeatureList.Add(new KeyValuePair<int, string>(1, "优化：来自 Modrinth 的文件下载速度"));
                    FeatureCount += 15;
                    BugCount += 16;
                }
                if (LastVersion < 381) // Release 2.12.2
                {
                    FeatureCount += 3;
                    BugCount += 13;
                }
                if (LastVersion < 379) // Release 2.12.1
                {
                    if (LastVersion >= 376)
                    {
                        FeatureList.Add(new KeyValuePair<int, string>(5, "删除：暂时隐藏联机入口……不过只是暂时关闭，它还会回来的！"));
                    }
                    FeatureList.Add(new KeyValuePair<int, string>(3, "新增：适配新的 Minecraft 版本号系统与 Unobfuscated 版本"));
                    FeatureList.Add(new KeyValuePair<int, string>(2, "优化：巨幅优化各个下载页面和 Mod 管理页面的性能"));
                    FeatureList.Add(new KeyValuePair<int, string>(1, "优化：更换披风时会显示当前使用的披风"));
                    FeatureCount += 33;
                    BugCount += 29;
                }
                if (LastVersion < 376) // Release 2.11.2
                {
                    // FeatureList.Add(New KeyValuePair(Of Integer, String)(5, "新增：联机功能！"))
                    FeatureCount += 32;
                    BugCount += 21;
                }
            }
            else if (ModBuildChannel.Channel == ModBuildChannel.Channels.Alpha)
            {
                // 内测线的更新日志，按《分支与发布规范》§5.1 的版本号段排列
                if (LastVersion < 5020) // alpha.1 2.100.0.1
                {
                    FeatureList.Add(new KeyValuePair<int, string>(1, "重写：主程序由 .NET Framework 迁移到 .NET 10"));
                    FeatureList.Add(new KeyValuePair<int, string>(2, "新增：按分支规范区分开发 / 收敛 / 内测 / 公测 / 正式 / 快照六种渠道"));
                }
            }
            else if (ModBuildChannel.Channel == ModBuildChannel.Channels.Dev || ModBuildChannel.Channel == ModBuildChannel.Channels.Slush)
            {
                // 开发线 / 收敛线的更新日志，按《分支与发布规范》§5.1 的版本号段排列
                // 说明：本项目仍在 C# 重写过程中，此处的「修复」条目大多为开发期自测发现的问题
                if (LastVersion >= 5000 && LastVersion < 5010) // slush.1 2.100.0.2
                {
                    // 收敛版：功能与 dev 相同，只做收敛、清理与回归，不再新增功能
                    FeatureList.Add(new KeyValuePair<int, string>(3, "收敛：dev 分支功能已全部合入，本版为内部候选，功能与 dev 相同"));
                    FeatureCount += 1;
                }
                if (LastVersion < 5000) // dev.1 2.100.0.1
                {
                    FeatureList.Add(new KeyValuePair<int, string>(1, "重写：主程序由 .NET Framework 4.8 迁移到 .NET 10，改为自包含单文件发布"));
                    FeatureList.Add(new KeyValuePair<int, string>(2, "新增：按分支规范区分开发 / 收敛 / 内测 / 公测 / 正式 / 快照六种代号"));
                    FeatureList.Add(new KeyValuePair<int, string>(2, "新增：MC 下载功能恢复，开关改为部署期配置"));
                    FeatureList.Add(new KeyValuePair<int, string>(3, "新增：下载服务完成 C# 化并接入主程序（多源并发 / 分片续传 / 断点续传 / 校验）"));
                    FeatureList.Add(new KeyValuePair<int, string>(2, "重写：下线内置自动更新模块，检查更新改为打开本项目 GitHub Releases"));
                    FeatureList.Add(new KeyValuePair<int, string>(2, "修复：社区资源加载器全部失败——.NET Core 需显式注册 GBK 等代码页"));
                    FeatureList.Add(new KeyValuePair<int, string>(2, "修复：命中 HTTP 缓存的请求整体失败——缓存响应头残留 Transfer-Encoding"));
                    FeatureList.Add(new KeyValuePair<int, string>(1, "修复：旧版 HTTP 缓存目录无法读取，切换到 Cache/HttpV2"));
                    FeatureList.Add(new KeyValuePair<int, string>(1, "修复：帮助页加载报未知类型 MyCard——内联 XAML 的 assembly= 漏改 10 处"));
                    FeatureList.Add(new KeyValuePair<int, string>(1, "修复：单例检查与自动更新残留清理遗漏更名后的 pcl-next.exe"));
                    FeatureList.Add(new KeyValuePair<int, string>(1, "修复：dev 与 slush 版本号相同，切回时被误判成版本倒退"));
                    FeatureCount += 5;
                    BugCount += 6;
                }
            }
            else
            {
                // 5：          FEAT+
                // 4：     IMP+ FEAT*
                // 3：BUG+ IMP* FEAT-
                // 2：BUG* IMP-
                // 1：BUG-
                if (LastVersion < 416) // Snapshot 2.99.0.1
                {
                    FeatureList.Add(new KeyValuePair<int, string>(4, "删除：MC 下载功能相关入口"));
                    FeatureList.Add(new KeyValuePair<int, string>(1, "优化：为设置更新添加部分黑名单"));
                }
                if (LastVersion < 414) // Snapshot 2.99.0.0
                {
                    FeatureList.Add(new KeyValuePair<int, string>(5, "删除：为遵守相关法律法规，PCL Next 不再提供 MC 下载功能，非常抱歉 qwq……"));
                }
                if (LastVersion < 412) // Snapshot 2.14.0.0
                {
                    FeatureList.Add(new KeyValuePair<int, string>(4, "新增：允许修改、添加、移除、更新已安装版本的各个组件，可从版本设置中进入"));
                    FeatureList.Add(new KeyValuePair<int, string>(2, "优化：启动游戏时允许按 Esc 取消"));
                    FeatureList.Add(new KeyValuePair<int, string>(2, "优化：允许复制主页的启动日志中的内容"));
                    FeatureCount += 19;
                    BugCount += 26;
                }
                if (LastVersion < 410) // Snapshot 2.13.1.1
                {
                    if (LastVersion == 409)
                        FeatureList.Add(new KeyValuePair<int, string>(1, "修复：更新 Mod 时可能更新成另一个 Mod 加载器的版本"));
                    FeatureCount += 2;
                    BugCount += 6;
                }
                if (LastVersion < 409) // Snapshot 2.13.1.0
                {
                    FeatureList.Add(new KeyValuePair<int, string>(2, "优化：版本设置中，添加打开截图文件夹的快捷方式"));
                    FeatureList.Add(new KeyValuePair<int, string>(2, "优化：导出整合包时，可以勾选导出光影设置文件"));
                    FeatureCount += 21;
                    BugCount += 22;
                }
                if (LastVersion < 407) // Snapshot 2.13.0.1
                {
                    if (LastVersion == 405)
                    {
                        FeatureList.Add(new KeyValuePair<int, string>(1, "修复：使用部分主页预设时崩溃"));
                        FeatureList.Add(new KeyValuePair<int, string>(1, "修复：无法删除部分文件夹"));
                        FeatureList.Add(new KeyValuePair<int, string>(1, "修复：版本设置中指定的 Java 无法被保存"));
                        FeatureCount += 1;
                        BugCount += 3;
                    }
                    FeatureCount += 2;
                }
                if (LastVersion < 405) // Snapshot 2.13.0.0
                {
                    FeatureList.Add(new KeyValuePair<int, string>(4, "新增：重做 Java 管理与相关设置，允许调整 Java 优先级、指定 Java 版本范围等"));
                    FeatureList.Add(new KeyValuePair<int, string>(3, "优化：导出整合包时允许自动导出版本文件夹中的 Java"));
                    FeatureList.Add(new KeyValuePair<int, string>(3, "优化：重做弹出提示的样式以及动画，以更符合现代 UI 审美"));
                    FeatureList.Add(new KeyValuePair<int, string>(1, "修复：可能无法安装 OptiFine 26.1.2"));
                    FeatureCount += 35;
                    BugCount += 17;
                }
                if (LastVersion < 403) // Snapshot 2.12.8.2
                {
                    FeatureList.Add(new KeyValuePair<int, string>(1, "修复：Windows 7 无法正常联网"));
                    FeatureCount += 1;
                    BugCount += 2;
                }
                if (LastVersion < 401) // Snapshot 2.12.8.1
                {
                    FeatureList.Add(new KeyValuePair<int, string>(1, "修复：可能无法更换披风"));
                    FeatureCount += 8;
                    BugCount += 6;
                }
                if (LastVersion < 399) // Snapshot 2.12.8.0
                {
                    FeatureList.Add(new KeyValuePair<int, string>(2, "优化：升级框架到 .NET Framework 4.8"));
                    FeatureCount += 28;
                    BugCount += 19;
                }
                if (LastVersion < 397) // Snapshot 2.12.7.3
                {
                    if (LastVersion >= 393 && LastVersion < 397)
                        FeatureList.Add(new KeyValuePair<int, string>(1, "修复：无法使用自定义隐藏主题"));
                }
                if (LastVersion < 395) // Snapshot 2.12.7.2
                {
                    if (LastVersion == 393)
                        FeatureList.Add(new KeyValuePair<int, string>(2, "修复：部分个性化功能失效"));
                }
                if (LastVersion < 393) // Snapshot 2.12.7.1
                {
                    FeatureList.Add(new KeyValuePair<int, string>(2, "优化：允许永久关闭启动页面的快照版提示"));
                    FeatureList.Add(new KeyValuePair<int, string>(2, "优化：下载游戏、整合包的稳定性"));
                    FeatureCount += 1;
                    BugCount += 5;
                }
                if (LastVersion < 390) // Snapshot 2.12.6.2
                {
                    FeatureList.Add(new KeyValuePair<int, string>(1, "修复：会为部分 Forge / NeoForge 选择不支持的 Java"));
                    BugCount += 4;
                }
                if (LastVersion < 388) // Snapshot 2.12.6.1
                {
                    if (LastVersion == 386)
                        FeatureList.Add(new KeyValuePair<int, string>(2, "修复：无法使用 Java 25+ 启动 Forge"));
                    FeatureCount += 4;
                    BugCount += 3;
                }
                if (LastVersion < 386) // Snapshot 2.12.6
                {
                    FeatureList.Add(new KeyValuePair<int, string>(2, "修复：Minecraft 使用 Java 25+ 时的一个性能问题"));
                    if (LastVersion == 384 || LastVersion == 385)
                        FeatureList.Add(new KeyValuePair<int, string>(2, "修复：无法正常重命名游戏版本"));
                    if (LastVersion == 385)
                        FeatureList.Add(new KeyValuePair<int, string>(1, "修复：无法显示 Modrinth 整合包的版本列表"));
                    BugCount += 1;
                }
                if (LastVersion < 385) // Snapshot 2.12.5
                {
                    FeatureList.Add(new KeyValuePair<int, string>(2, "修复：无法下载 NeoForge 26.1"));
                    if (LastVersion == 384)
                    {
                        FeatureList.Add(new KeyValuePair<int, string>(2, "修复：下载可能失败，提示任务管理刷新线程出错"));
                        FeatureList.Add(new KeyValuePair<int, string>(2, "修复：无法保存选择的 Minecraft 文件夹"));
                        FeatureList.Add(new KeyValuePair<int, string>(1, "修复：使用中文搜索 Mod 时，部分结果会忽略筛选条件"));
                    }
                    FeatureCount += 3;
                    BugCount += 6;
                }
                if (LastVersion < 384) // Snapshot 2.12.4
                {
                    FeatureList.Add(new KeyValuePair<int, string>(3, "优化：降低 Minecraft 的内存占用"));
                    FeatureList.Add(new KeyValuePair<int, string>(2, "修复：部分关键词搜不到资源，特别是中文 Mod 搜索经常没有结果"));
                    FeatureList.Add(new KeyValuePair<int, string>(1, "修复：老版本 Windows 无法启动游戏"));
                    FeatureList.Add(new KeyValuePair<int, string>(1, "修复：连接 Mojang 的服务可能失败，提示错误码 421"));
                    FeatureList.Add(new KeyValuePair<int, string>(1, "修复：无法访问爱发电"));
                    FeatureCount += 21;
                    BugCount += 14;
                }
                if (LastVersion < 382) // Snapshot 2.12.3
                {
                    FeatureList.Add(new KeyValuePair<int, string>(2, "修复：下载可能完全卡住，或是下载进度反复回退"));
                    FeatureList.Add(new KeyValuePair<int, string>(1, "新增：内存管理设置，可以选择 G1GC、ZGC 或分代 ZGC"));
                    FeatureList.Add(new KeyValuePair<int, string>(1, "优化：来自 Modrinth 的文件下载速度"));
                    FeatureCount += 15;
                    BugCount += 16;
                }
                if (LastVersion < 380) // Snapshot 2.12.2
                {
                    FeatureCount += 3;
                    BugCount += 13;
                }
                if (LastVersion < 378) // Snapshot 2.12.1
                {
                    if (LastVersion >= 377)
                    {
                        FeatureList.Add(new KeyValuePair<int, string>(1, "修复：在版本列表中存在 OptiFine 时可能无法加载版本列表"));
                        BugCount += 2;
                    }
                }
                if (LastVersion < 377) // Snapshot 2.12.0
                {
                    if (LastVersion >= 373)
                    {
                        FeatureList.Add(new KeyValuePair<int, string>(5, "删除：暂时隐藏联机入口……不过只是暂时关闭，它还会回来的！"));
                    }
                    FeatureList.Add(new KeyValuePair<int, string>(3, "新增：适配新的 Minecraft 版本号系统与 Unobfuscated 版本"));
                    FeatureList.Add(new KeyValuePair<int, string>(2, "优化：巨幅优化各个下载页面和 Mod 管理页面的性能"));
                    FeatureList.Add(new KeyValuePair<int, string>(1, "优化：更换披风时会显示当前使用的披风"));
                    FeatureCount += 33;
                    BugCount += 29;
                }
                if (LastVersion < 375) // Snapshot 2.11.2
                {
                    // If LastVersion >= 373 Then
                    // FeatureList.Add(New KeyValuePair(Of Integer, String)(3, "优化：对联机进行了各种各样的优化，以改善稳定性"))
                    // FeatureList.Add(New KeyValuePair(Of Integer, String)(2, "优化：若有加入者的网络环境比房主更好，会提示可以让那位加入者担任房主"))
                    // End If
                    FeatureCount += 16;
                    BugCount += 4;
                }
                if (LastVersion < 374) // Snapshot 2.11.1
                {
                    // If LastVersion >= 373 Then
                    // FeatureList.Add(New KeyValuePair(Of Integer, String)(3, "优化：使用离线登录也可以直接加入联机房间了"))
                    // FeatureList.Add(New KeyValuePair(Of Integer, String)(3, "优化：会从所有共享节点中自动选择负载最低的进行中继连接"))
                    // FeatureList.Add(New KeyValuePair(Of Integer, String)(2, "优化：若复制了邀请码，则可以直接快速加入房间"))
                    // FeatureList.Add(New KeyValuePair(Of Integer, String)(2, "优化：关闭 PCL Next 时总是会提示是否退出联机，防止在关闭 PCL Next 时无意地关闭或退出了房间"))
                    // FeatureList.Add(New KeyValuePair(Of Integer, String)(2, "新增：允许自定义要连接的节点"))
                    // End If
                    FeatureCount += 9;
                    BugCount += 7;
                }
                if (LastVersion < 373) // Snapshot 2.11.0
                {
                    // FeatureList.Add(New KeyValuePair(Of Integer, String)(5, "新增：联机功能！"))
                    FeatureCount += 7;
                    BugCount += 10;
                }
            }
            // 整理更新日志文本
            var ContentList = new List<string>();
            var SortedFeatures = FeatureList.OrderByDescending(f => f.Key).ToList();
            if (!SortedFeatures.Any() && FeatureCount == 0 && BugCount == 0)
                ContentList.Add("没有更新日志……");
            for (int i = 0, loopTo = Math.Min(9, SortedFeatures.Count - 1); i <= loopTo; i++) // 最多取 10 项
                ContentList.Add(SortedFeatures[i].Value);
            if (SortedFeatures.Count > 10)
                FeatureCount += SortedFeatures.Count - 10;
            if (FeatureCount > 0 || BugCount > 0)
            {
                ContentList.Add((FeatureCount > 0 ? FeatureCount + " 项小调整与修改" : "") + (FeatureCount > 0 && BugCount > 0 ? "，" : "") + (BugCount > 0 ? "修复了 " + BugCount + " 个 Bug" : "") + "，详见完整更新日志");
            }
            string Content = "· " + ContentList.Join(Constants.vbCrLf + "· ");
            // 输出更新日志
            ModBase.RunInNewThread(() => { if (ModMain.MyMsgBox(Content, "PCL Next 已更新至" + ModBase.VersionDisplay, "确定", "完整更新日志") == 2) { ModBase.OpenWebsite("https://meloong.com/afd/a/LTCat?tab=feed"); } }, "UpdateLog Output");
        }

        // 窗口加载
        private bool IsWindowLoadFinished = false;
        private DragHelper _Helper = new DragHelper();
        public FormMain()
        {
            ModBase.ApplicationStartTick = ModBase.GetTimeMs();
            // 窗体参数初始化
            ModMain.FrmMain = this;
            ModMain.FrmLaunchLeft = new PageLaunchLeft();
            ModMain.FrmLaunchRight = new PageLaunchRight();
            // 版本号改变
            // 「历史最高版本号」按渠道隔离，比较时也必须用渠道自己那份记录：
            // 若用全局的 SystemLastVersionReg，跨渠道切换会被误判成「版本倒退」
            // （例如从正式通道的 5090 切到开发通道的 5000），
            // 结果是该渠道的更新日志不显示、最高版本号也不落库。
            int LastVersion = Settings.Get<int>(ModBuildChannel.ChannelHighestVersionKey);
            if (LastVersion < ModBase.VersionCode)
            {
                // 触发升级
                UpgradeSub(LastVersion);
            }
            else if (LastVersion > ModBase.VersionCode)
            {
                // 触发降级
                DowngradeSub(LastVersion);
            }
            // 版本隔离设置迁移
            if (!Settings.HasSaved("LaunchArgumentIndieV2"))
            {
                if (Settings.HasSaved("LaunchArgumentIndie"))
                {
                    Logger.Info("从老 PCL 迁移版本隔离");
                    Settings.Set("LaunchArgumentIndieV2", Settings.Get<int>("LaunchArgumentIndie"));
                }
                else if (ModBase.HasIniKey("Setup", "LaunchVersionSelect"))
                {
                    Logger.Info("从老 PCL 升级，但此前未调整版本隔离，使用老的版本隔离默认值");
                    Settings.Set("LaunchArgumentIndieV2", Settings.GetDefault("LaunchArgumentIndie"));
                }
                else
                {
                    Logger.Info("全新的 PCL Next，使用新的版本隔离默认值");
                    Settings.Set("LaunchArgumentIndieV2", Settings.GetDefault("LaunchArgumentIndieV2"));
                }
            }
            // 刷新主题
            ModSecret.ThemeCheckAll(false);
            ModSecret.ThemeRefresh(Settings.Get<int>("UiLauncherTheme"));
            // 注册拖拽事件（不能直接加 Handles，否则没用；#6340）
            this.AddHandler(DragDrop.DragEnterEvent, new DragEventHandler(HandleDrag), handledEventsToo: true);
            this.AddHandler(DragDrop.DragOverEvent, new DragEventHandler(HandleDrag), handledEventsToo: true);
            // 加载 UI
            this.InitializeComponent();
            this.PanTitle.MouseLeftButtonDown += (p1, p2) => FormDragMove((object)p1, (MouseButtonEventArgs)p2);
            this.PanMsg.MouseLeftButtonDown += (p1, p2) => FormDragMove((object)p1, (MouseButtonEventArgs)p2);
            // 标题栏关闭 / 最小化：VB 原版靠 Handles BtnTitleClose.Click / BtnTitleMin.Click 绑定，
            // 迁移后 XAML 与构造函数都没有订阅，导致点关闭、最小化完全无反应（2026-11-11 修复）。
            this.BtnTitleClose.Click += (p1, p2) => BtnTitleClose_Click(p1, p2);
            this.BtnTitleMin.Click += (p1, p2) => BtnTitleMin_Click(p1, p2);
            // 左上角返回箭头：VB 原版是 Public Sub PageBack() Handles BtnTitleInner.Click，
            // 迁移后漏了订阅，导致所有子页面（版本设置、Mod 管理等）点返回没反应（2026-11-11 修复）。
            this.BtnTitleInner.Click += (p1, p2) => PageBack();
            this.BtnTitleSelect0.Check += (p1, p2) => BtnTitleSelect_Click((MyRadioButton)p1, (bool)p2);
            this.BtnTitleSelect1.Check += (p1, p2) => BtnTitleSelect_Click((MyRadioButton)p1, (bool)p2);
            this.BtnTitleSelect2.Check += (p1, p2) => BtnTitleSelect_Click((MyRadioButton)p1, (bool)p2);
            this.BtnTitleSelect3.Check += (p1, p2) => BtnTitleSelect_Click((MyRadioButton)p1, (bool)p2);
            this.BtnTitleSelect4.Check += (p1, p2) => BtnTitleSelect_Click((MyRadioButton)p1, (bool)p2);
            this.PanMainLeft.SizeChanged += (p1, p2) => PanMainLeft_SizeChanged((object)p1, (SizeChangedEventArgs)p2);
            this.PanBack.MouseMove += (p1, p2) => DragDoing();
            this.Opacity = 0d;
            // 开启管理员权限下的文件拖拽
            if (WindowsUtils.HasAdminRole())
            {
                SourceInitialized += (_, __) =>
        {
            var WpfHelper = new global::System.Windows.Interop.WindowInteropHelper(this);
            _Helper.HwndIntPtrSource = global::System.Windows.Interop.HwndSource.FromHwnd(WpfHelper.Handle);
            _Helper.AddHook();
        };
                Closing += (_, __) => _Helper.RemoveDragHook();
                _Helper.DragDrop += (_, __) => FileDrag(_Helper.DropFilePaths);
            }
            // 切换到首页
            if (!(ModMain.FrmLaunchLeft.Parent == null))
                ModMain.FrmLaunchLeft.SetValue(ContentPresenter.ContentProperty, (object)null);
            if (!(ModMain.FrmLaunchRight.Parent == null))
                ModMain.FrmLaunchRight.SetValue(ContentPresenter.ContentProperty, (object)null);
            this.PanMainLeft.Child = ModMain.FrmLaunchLeft;
            PageLeft = ModMain.FrmLaunchLeft;
            this.PanMainRight.Child = ModMain.FrmLaunchRight;
            PageRight = ModMain.FrmLaunchRight;
            ModMain.FrmLaunchRight.PageState = MyPageRight.PageStates.ContentStay;
            // 模式提醒
            if (ModBase.BuildType == ModBase.BuildTypes.Debug)
                ModMain.Hint("[开发者模式] PCL Next 正以开发者模式运行，这可能会造成严重的性能下降，请务必立即向开发者反馈此问题！", ModMain.HintType.Red);
            if (ModBase.ModeDebug)
                ModMain.Hint("[调试模式] PCL Next 正以调试模式运行，这可能会导致性能下降，若无必要请不要开启！");
            // 尽早执行的加载池
            ModMinecraft.McFolderListLoader.Start(0); // 为了让下载已存在文件检测可以正常运行，必须跑一次；为了让启动按钮尽快可用，需要尽早执行；为了与 PageLaunchLeft 联动，需要为 0 而不是 GetUuid

            Logger.Info($"第二阶段加载用时：{ModBase.GetTimeMs() - ModBase.ApplicationStartTick} ms");
            this.Loaded += FormMain_Loaded;
            this.Closing += FormMain_Closing;
            this.SizeChanged += (_, __) => FormMain_SizeChanged();
            this.Loaded += (_, __) => FormMain_SizeChanged();
            this.KeyDown += FormMain_KeyDown;
            this.MouseDown += FormMain_MouseDown;
            this.Activated += (_, __) => FormMain_Activated();
            this.Drop += FrmMain_Drop;
            this.MouseMove += FormMain_MouseMove;
        }
        private void FormMain_Loaded(object sender, RoutedEventArgs e)
        {
            ModBase.ApplicationStartTick = ModBase.GetTimeMs();
            ModBase.Handle = new WindowInteropHelper(this).Handle;
            // 读取设置
            UpdateBackgroundAndTitleBar();
            PageSetupHome.HiddenRefresh();
            PageSetupBackdrop.BackgroundRefresh(false, true);
            ModMusic.MusicRefreshPlay(false, true);
            // 扩展按钮
            this.BtnExtraDownload.Click += (p1, p2) => BtnExtraDownload_Click(p1, p2);
            this.BtnExtraMusic.Click += (p1, p2) => BtnExtraMusic_Click(p1, p2);
            this.BtnExtraMusic.RightClick += (p1, p2) => BtnExtraMusic_RightClick(p1, p2);
            // 以下三个同样是 VB 的 Handles 在迁移时被丢弃的（2026-11-11 补）：
            // 回到顶部的浮动按钮、愚人节彩蛋按钮、关机按钮此前点了完全没反应。
            this.BtnExtraBack.Click += (p1, p2) => BackToTop();
            this.BtnExtraApril.Click += (p1, p2) => AprilGiveup();
            this.BtnExtraShutdown.Click += (p1, p2) => BtnExtraShutdown_Click();
            this.BtnExtraDownload.ShowCheck = this.BtnExtraDownload_ShowCheck;
            this.BtnExtraBack.ShowCheck = this.BtnExtraBack_ShowCheck;
            this.BtnExtraApril.ShowCheck = this.BtnExtraApril_ShowCheck;
            this.BtnExtraShutdown.ShowCheck = this.BtnExtraShutdown_ShowCheck;
            this.BtnExtraApril.ShowRefresh();
            // 初始化尺寸改变
            var Resizer = new MyResizer(this);
            Resizer.addResizerDown(this.ResizerB);
            Resizer.addResizerLeft(this.ResizerL);
            Resizer.addResizerLeftDown(this.ResizerLB);
            Resizer.addResizerLeftUp(this.ResizerLT);
            Resizer.addResizerRight(this.ResizerR);
            Resizer.addResizerRightDown(this.ResizerRB);
            Resizer.addResizerRightUp(this.ResizerRT);
            Resizer.addResizerUp(this.ResizerT);
            // PLC 彩蛋
            if (ModBase.RandomInteger(1, 1000) == 233)
            {
                this.ShapeTitleLogo.Data = (Geometry)new GeometryConverter().ConvertFromString("M26,29 v-25 h6 a7,7 180 0 1 0,14 h-6 M83,6.5 a10,11.5 180 1 0 0,18 M48,2.5 v24.5 h13.5");
            }
            // 加载窗口
            ModSecret.ThemeRefreshMain();
            try
            {
                this.Height = (double)Settings.Get<int>("WindowHeight");
                this.Width = (double)Settings.Get<int>("WindowWidth");
            }
            catch (Exception ex) // 修复 #2019
            {
                Logger.Error(ex, "读取窗口默认大小失败", LogBehavior.Toast);
                this.Height = this.MinHeight + 100d;
                this.Width = this.MinWidth + 100d;
            }
            // MinHeight = 50
            // MinWidth = 50
            this.Topmost = false;
            if (ModMain.FrmStart is not null)
                ModMain.FrmStart.Close(new TimeSpan(0, 0, 0, 0, (int)Math.Round(400d / ModAnimation.AniSpeed)));
            // 更改窗口
            this.Top = (ModBase.GetWPFSize(My.Computer.Screen.WorkingArea.Height) - this.Height) / 2d;
            this.Left = (ModBase.GetWPFSize(My.Computer.Screen.WorkingArea.Width) - this.Width) / 2d;
            IsSizeSaveable = true;
            ShowWindowToTop();
            HwndSource HwndSource = (HwndSource)PresentationSource.FromVisual(this);
            HwndSource.AddHook(new HwndSourceHook(WndProc));
            ModAnimation.AniStart(new[] {
            ModAnimation.AaCode(() => ModAnimation.AniControlEnabled -= 1, 50),
            ModAnimation.AaOpacity(this, Settings.Get<int>("UiLauncherTransparent") / 1000d + 0.4d, 250, 100),
            ModAnimation.AaDouble(i => this.TransformPos.Y = Conversions.ToDouble(Conversions.ToDouble(this.TransformPos.Y) + Conversions.ToDouble(i)), -Conversions.ToDouble(this.TransformPos.Y), 600, 100, new ModAnimation.AniEaseOutBack(ModAnimation.AniEasePower.Weak)),
            ModAnimation.AaDouble(i => this.TransformRotate.Angle = Conversions.ToDouble(Conversions.ToDouble(this.TransformRotate.Angle) + Conversions.ToDouble(i)), -Conversions.ToDouble(this.TransformRotate.Angle), 500, 100, new ModAnimation.AniEaseOutBack(ModAnimation.AniEasePower.Weak)),
                        ModAnimation.AaCode(() =>
                {
                this.PanBack.RenderTransform = (Transform)null;
                IsWindowLoadFinished = true;
                Logger.Info($"DPI：{ModBase.DPI}，系统版本：{Environment.OSVersion.VersionString}，PCL Next 位置：{ModBase.PathExe}");
            }, After: true)
        }, "Form Show");
            // Timer 启动
            ModAnimation.AniStart();
            ModMain.TimerMainStart();
            // 加载池
            ModBase.RunInNewThread(() =>
        {
            // EULA 提示
            const int EulaVersion = 2;
            if (Settings.Get<int>("SystemEulaVersion") < EulaVersion)
            {
                switch (ModMain.MyMsgBox(Settings.Get<int>("SystemEulaVersion") == 0 ? "在使用 PCL Next 前，请先阅读用户协议与免责声明。" : $"PCL Next 的用户协议与免责声明已更新。{Constants.vbCrLf}请阅读更新后的用户协议与免责声明。", "协议授权", "同意", "拒绝", "查看用户协议与免责声明", Button3Action: () => ModBase.OpenWebsite("https://shimo.im/docs/rGrd8pY8xWkt6ryW")))
                {
                    case 1:
                        {
                            Settings.Set("SystemEulaVersion", EulaVersion);
                            break;
                        }
                    case 2:
                        {
                            EndProgram(false);
                            break;
                        }
                }
            }
            // 启动加载器池
            try
            {
                ModJava.JavaInit(); // 延后到同意协议后再执行，避免在初次启动时进行进程操作
                Thread.Sleep(100);
                ModDownload.DlClientListMojangLoader.Start(1); // PCL 会同时根据这里的加载结果决定是否使用官方源进行下载
                RunCountSub();
                ModSecret.ServerLoader.Start();
                ModBase.RunInNewThread(ModMain.TryClearTaskTemp, "TryClearTaskTemp", ThreadPriority.BelowNormal);
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "初始化加载池运行失败");
            }
            // 清理自动更新文件
            try
            {
                FileUtils.Delete(Paths.Base + @"PCL\pcl-next.exe");
                FileUtils.Delete(Paths.Base + @"PCL\Plain Craft Launcher 2.exe");
            }
            catch (Exception ex)
            {
                Logger.Warn(ex, "清理自动更新文件失败");
            }
            // 更新日志 / 开发阶段提示统一在「更新后第一次启动」触发：
            // UpgradeSub 只写待显示标记，由 RunCountSub 负责呈现并清除（2026-11-08 用户裁决）。
        }, "初始化", ThreadPriority.Lowest);

            Logger.Info($"第三阶段加载用时：{ModBase.GetTimeMs() - ModBase.ApplicationStartTick} ms");
        }
        // 根据打开次数触发的事件
        private void RunCountSub()
        {
            Settings.Set("SystemCount", Settings.Get<int>("SystemCount") + 1);
            // 更新日志 / 开发阶段提示：只在「更新后第一次启动」显示一次。
            // 待显示标记由 UpgradeSub 在检测到版本升高时写入；呈现后立即清除，避免重复弹出。
            int PendingUpdateFrom = Settings.Get<int>("SystemPendingUpdateLogFrom");
            if (PendingUpdateFrom > 0)
            {
                Settings.Set("SystemPendingUpdateLogFrom", 0);
                ShowUpdateLog(PendingUpdateFrom);
                ShowDevNotes();
            }
            // 各渠道的首次运行提示，按《分支与发布规范》§1.2、§1.3、§1.4 的要求分别明示
            if (Settings.Get<int>("SystemCount") == 1)
            {
                switch (ModBuildChannel.Channel)
                {
                    case ModBuildChannel.Channels.Alpha:
                        {
                            ModMain.MyMsgBox("欢迎使用 PCL Next 内测版！" + Constants.vbCrLf + Constants.vbCrLf + "内测版仅面向少量受邀用户，可能存在已知缺陷，甚至会导致配置丢失或游戏崩溃。" + Constants.vbCrLf + "请务必先备份好 PCL Next 的配置与整合包，遇到无法接受的问题请及时反馈。", "内测版使用说明");
                            break;
                        }
                    case ModBuildChannel.Channels.Beta:
                        {
                            ModMain.MyMsgBox("欢迎使用 PCL Next 公测版！" + Constants.vbCrLf + Constants.vbCrLf + "公测版面向所有用户开放试跑，只修复阻塞使用的问题，仍可能存在少量已知缺陷。" + Constants.vbCrLf + "请不要将公测版用于正式生产环境，遇到无法接受的崩溃请反馈。", "公测版使用说明");
                            break;
                        }
                    case ModBuildChannel.Channels.Slush:
                        {
                            ModMain.MyMsgBox("欢迎使用 PCL Next 收敛版！" + Constants.vbCrLf + Constants.vbCrLf + "这是功能冻结后的内部候选构建，仅供内部测试使用，请不要对外分发。", "收敛版使用说明");
                            break;
                        }
                }
            }
            if (ModBuildChannel.ChannelIsInternal)
            {
                if (Settings.Get<int>("SystemCount") >= 99)
                {
                    if (ModSecret.ThemeUnlock(6, false))
                    {
                        ModMain.MyMsgBox("你已经使用了 99 次 PCL Next 啦，感谢你长期以来的支持！" + Constants.vbCrLf + "隐藏主题 铁杆粉 已解锁！", "提示");
                    }
                }
            }
        }
        // 开发阶段提示与开发日志（内置，每版本硬编码）
        // 仅开发阶段渠道（dev / slush / alpha / beta）在「更新后第一次启动」弹出；RC / 正式 / SP 不显示。
        // 内容见 ModDevNotes（按 VersionCode 分支，由开发人员每版本写入）。
        private void ShowDevNotes()
        {
            if (ModBuildChannel.ChannelIsOfficial)
                return;
            string Notice = ModDevNotes.DevStageNotice;
            if (!string.IsNullOrEmpty(Notice))
                ModMain.MyMsgBox(Notice, "开发阶段提示");
            string Log = ModDevNotes.DevChangelog;
            if (!string.IsNullOrEmpty(Log))
                ModMain.MyMsgBox(Log, "开发日志 · " + ModBuildChannel.ChannelDisplay + " " + ModBase.VersionBaseName);
        }
        // 升级与降级事件
        private void UpgradeSub(int LastVersionCode)
        {
            Logger.Info($"版本号从 {LastVersionCode} 升高到 {ModBase.VersionCode}");
            Settings.Set("SystemLastVersionReg", ModBase.VersionCode);
            // 检查有记录的最高版本号
            // 不同渠道各记各的，避免开发 / 内测版的高版本号污染正式通道的升级判定
            int HighestVersionCode;
            string SettingKey = ModBuildChannel.ChannelHighestVersionKey;
            HighestVersionCode = Settings.Get<int>(SettingKey);
            if (HighestVersionCode < ModBase.VersionCode)
            {
                Settings.Set(SettingKey, ModBase.VersionCode);
                Logger.Info($"最高版本号从 {HighestVersionCode} 升高到 {ModBase.VersionCode}");
            }
            // 被移除的窗口设置选项
            if (Settings.Get<int>("LaunchArgumentWindowType") == 5)
                Settings.Set("LaunchArgumentWindowType", 1);
            // 修改主题设置项名称
            if (HighestVersionCode <= 207)
            {
                var UnlockedTheme = new List<string>() { "2" };
                UnlockedTheme.AddRange(new List<string>(Settings.Get<string>("UiLauncherThemeHide").ToString().Split("|")));
                UnlockedTheme.AddRange(new List<string>(Settings.Get<string>("UiLauncherThemeHide2").ToString().Split("|")));
                Settings.Set("UiLauncherThemeHide2", UnlockedTheme.Distinct().ToList().Join('|'));
            }
            // 重置欧皇彩
            if (LastVersionCode <= 115 && Settings.Get<string>("UiLauncherThemeHide2").ToString().Split("|").Contains("13"))
            {
                var UnlockedTheme = new List<string>(Settings.Get<string>("UiLauncherThemeHide2").ToString().Split("|"));
                UnlockedTheme.Remove("13");
                Settings.Set("UiLauncherThemeHide2", UnlockedTheme.Join('|'));
                ModMain.MyMsgBox("由于新版 PCL Next 修改了欧皇彩的解锁方式，你需要重新解锁欧皇彩。" + Constants.vbCrLf + "多谢各位的理解啦！", "重新解锁提醒");
            }
            // 重置滑稽彩
            if (LastVersionCode <= 152 && Settings.Get<string>("UiLauncherThemeHide2").ToString().Split("|").Contains("12"))
            {
                var UnlockedTheme = new List<string>(Settings.Get<string>("UiLauncherThemeHide2").ToString().Split("|"));
                UnlockedTheme.Remove("12");
                Settings.Set("UiLauncherThemeHide2", UnlockedTheme.Join('|'));
                ModMain.MyMsgBox("由于新版 PCL Next 修改了滑稽彩的解锁方式，你需要重新解锁滑稽彩。" + Constants.vbCrLf + "多谢各位的理解啦！", "重新解锁提醒");
            }
            // 移动自定义皮肤
            if (LastVersionCode <= 161 && FileUtils.Exists(Paths.Base + @"PCL\CustomSkin.png") && !FileUtils.Exists(Paths.AppDataThenName + "CustomSkin.png"))
            {
                FileUtils.Copy(Paths.Base + @"PCL\CustomSkin.png", Paths.AppDataThenName + "CustomSkin.png");
                Logger.Info("已移动离线自定义皮肤 (162)");
            }
            if (LastVersionCode <= 263 && FileUtils.Exists(ModBase.PathTemp + "CustomSkin.png") && !FileUtils.Exists(Paths.AppDataThenName + "CustomSkin.png"))
            {
                FileUtils.Copy(ModBase.PathTemp + "CustomSkin.png", Paths.AppDataThenName + "CustomSkin.png");
                Logger.Info("已移动离线自定义皮肤 (264)");
            }
            // 解除帮助页面的隐藏
            if (LastVersionCode <= 205)
            {
                Settings.Set("UiHiddenOtherHelp", false);
                Logger.Info("已解除帮助页面的隐藏");
            }
            // 单向迁移微软登录结果（#4836）
            if (!Settings.Get<bool>("CacheMsV2Migrated"))
            {
                Settings.Set("CacheMsV2Migrated", true);
                Settings.Set("CacheMsV2OAuthRefresh", Settings.Get<string>("CacheMsOAuthRefresh"));
                Settings.Set("CacheMsV2Access", Settings.Get<string>("CacheMsAccess"));
                Settings.Set("CacheMsV2ProfileJson", Settings.Get<string>("CacheMsProfileJson"));
                Settings.Set("CacheMsV2Uuid", Settings.Get<string>("CacheMsUuid"));
                Settings.Set("CacheMsV2Name", Settings.Get<string>("CacheMsName"));
                Logger.Info("已从老版本迁移微软登录结果");
            }
            // Mod 命名设置迁移
            if (Settings.HasSaved("ToolDownloadTranslate") && !Settings.HasSaved("ToolDownloadTranslateV2"))
            {
                Settings.Set("ToolDownloadTranslateV2", Settings.Get<int>("ToolDownloadTranslate") + 1);
                Logger.Info("已从老版本迁移 Mod 命名设置");
            }
            // 重置 JVM 参数设置
            if (LastVersionCode <= 381 && Settings.HasSaved("LaunchAdvanceJvm") && (Settings.Get<string>("LaunchAdvanceJvm").ToString().Replace("-XX:+UseG1GC ", "").Replace("-XX:-UseAdaptiveSizePolicy ", "").Trim() ?? "") == (Settings.GetDefault("LaunchAdvanceJvm") ?? ""))
            {
                Settings.Reset("LaunchAdvanceJvm");
                Logger.Info("已重置 JVM 参数设置");
            }
            // 更新日志不在本处直接弹窗：只写待显示标记，由本次启动的 RunCountSub 在
            // 「更新后第一次启动」统一呈现一次（2026-11-08 用户裁决）。
            if (LastVersionCode <= 0)
                return;
            if (HighestVersionCode >= ModBase.VersionCode)
                return;
            Settings.Set("SystemPendingUpdateLogFrom", HighestVersionCode);
        }
        private void DowngradeSub(int LastVersionCode)
        {
            Logger.Info($"版本号从 {LastVersionCode} 降低到 {ModBase.VersionCode}");
            Settings.Set("SystemLastVersionReg", ModBase.VersionCode);
        }

        #endregion

        #region 自定义窗口

        // 关闭
        private void FormMain_Closing(object sender, CancelEventArgs e)
        {
            EndProgram(true);
            e.Cancel = true;
        }
        /// <summary>
    /// 正常关闭程序。程序将在执行此方法后约 0.3s 退出。
    /// </summary>
    /// <param name="SendWarning">是否在还有下载任务未完成时发出警告。</param>
        public void EndProgram(bool SendWarning)
        {
            // 强行结束下载任务？
            if (ModNet.HasDownloadingTask())
            {
                if (SendWarning && ModMain.MyMsgBox("还有下载任务尚未完成，是否确定退出？", "提示", "确定", "取消") == 2)
                    return;
                ModBase.RunInNewThread(() =>
        {
            Logger.Info("正在强行停止任务");
            foreach (ModLoader.LoaderBase Task in ModLoader.LoaderTaskbar.ToList())
                Task.Cancel();
        }, "强行停止下载任务");
            }
            // 关闭联机？
            if (ModMain.FrmLinkMain?.TryExit(!SendWarning, true) == true)
                return;
            // 关闭
            ModBase.RunInUiWait(() =>
        {
            this.IsHitTestVisible = false;
            if (this.PanBack.RenderTransform is null)
            {
                var TransformPos = new TranslateTransform(0d, 0d);
                var TransformRotate = new RotateTransform(0d);
                var TransformScale = new ScaleTransform(1d, 1d);
                this.PanBack.RenderTransform = new TransformGroup() { Children = new TransformCollection(new Transform[] { (Transform)TransformRotate, (Transform)TransformPos, (Transform)TransformScale }) };
                ModAnimation.AniStart(new[] {
                    ModAnimation.AaOpacity(this, -this.Opacity, 140, 40, new ModAnimation.AniEaseOutFluent(ModAnimation.AniEasePower.Weak)),
                                        ModAnimation.AaDouble(i =>
                        {
                        TransformScale.ScaleX = Conversions.ToDouble(Conversions.ToDouble(TransformScale.ScaleX) + Conversions.ToDouble(i));
                        TransformScale.ScaleY = Conversions.ToDouble(Conversions.ToDouble(TransformScale.ScaleY) + Conversions.ToDouble(i));
                    }, 0.88d - Conversions.ToDouble(TransformScale.ScaleX), 180),
                    ModAnimation.AaDouble(i => TransformPos.Y = Conversions.ToDouble(Conversions.ToDouble(TransformPos.Y) + Conversions.ToDouble(i)), 20d - Conversions.ToDouble(TransformPos.Y), 180, 0, new ModAnimation.AniEaseOutFluent(ModAnimation.AniEasePower.Weak)),
                    ModAnimation.AaDouble(i => TransformRotate.Angle = Conversions.ToDouble(Conversions.ToDouble(TransformRotate.Angle) + Conversions.ToDouble(i)), 0.6d - Conversions.ToDouble(TransformRotate.Angle), 180, 0, new ModAnimation.AniEaseInoutFluent(ModAnimation.AniEasePower.Weak)),
                                        ModAnimation.AaCode(() =>
                        {
                        this.IsHitTestVisible = false;
                        this.Top = (double)-10000;
                        this.ShowInTaskbar = false;
                    }, 210),
                    ModAnimation.AaCode(() => EndProgramForce(), 230)
            }, "Form Close");
            }
            else
            {
                EndProgramForce();
            }
            Logger.Info("收到关闭指令");
        });
        }
        private static bool IsLogShown = false;
        public static void EndProgramForce(ModBase.ProcessReturnValues ReturnCode = ModBase.ProcessReturnValues.Success)
        {
            ;
            // [VB Port] On Error Resume Next -> ignore exceptions in the rest of this method
            try
            {
            ModBase.IsProgramEnding = true;
            ModAnimation.AniControlEnabled += 1;
            if (ReturnCode == ModBase.ProcessReturnValues.Exception)
            {
                if (!IsLogShown)
                {
                    ModMain.FeedbackInfo();
                    Logger.Info("请在 https://github.com/Meloong-Git/PCL/issues 提交错误报告，以便于作者解决此问题！");
                    IsLogShown = true;
                    ModBase.StartProcess(Paths.Base + @"PCL\Log1.txt");
                }
                Thread.Sleep(500); // 防止 PCL 在记事本打开前就被掐掉
            }
            Logger.Info($"程序已退出，返回值：{ReturnCode}");
            ConfigUtils.SaveAll();
            Logger.Instance.Flush();
            if (ReturnCode != ModBase.ProcessReturnValues.Success)
                Environment.Exit((int)ReturnCode);
            Process.GetCurrentProcess().Kill();
            } // [VB Port] end try
            // 对应 FormMain.xaml.vb:690-708 的「On Error Resume Next」：VB 会吞掉退出过程中的错误并继续往下执行。
            // 这里按该段实际可能抛出的异常类型分条处理，全部静默忽略——退出已走到最后一步，报错只会掩盖真实退出码、让进程卡在关闭中。
            catch (System.ComponentModel.Win32Exception)
            {
                // Process.Kill() 在进程已被外部结束或权限不足时抛出 Win32Exception：进程本就要退出，无需重试。
            }
            catch (System.Security.SecurityException)
            {
                // Environment.Exit() 在受限环境（沙箱 / 受限会话）抛出 SecurityException：随后仍有 Kill 兜底。
            }
            catch (System.IO.IOException)
            {
                // ConfigUtils.SaveAll() / 日志刷盘写文件失败：配置与日志问题不能让退出流程中断。
            }
            catch (UnauthorizedAccessException)
            {
                // 同上，日志或配置文件无写权限：仅影响日志落盘，退出继续。
            }
        }
        // 签名必须是 (object, EventArgs)：MyIconButton.Check 的自定义委托是 ClickEventHandler(object, EventArgs)，
        // VB 原版用 Handles 宽松绑定，C# 里 RoutedEventArgs 更具体、无法直接方法组转换（2026-11-11 修正）。
        private void BtnTitleClose_Click(object sender, EventArgs e)
        {
            EndProgram(true);
        }

        // 移动
        private void FormDragMove(object sender, MouseButtonEventArgs e)
        {
            ;
            // [VB Port] On Error Resume Next -> ignore exceptions in the rest of this method
            try
            {
            if (Conversions.ToBoolean(((dynamic)sender).IsMouseDirectlyOver))
                this.DragMove();
            } // [VB Port] end try
            // 对应 FormMain.xaml.vb:714-717 的「On Error Resume Next」。
            catch (InvalidOperationException)
            {
                // DragMove() 在未按住鼠标左键时必定抛 InvalidOperationException（WPF 的既定失败条件）；
                // VB 的 On Error Resume Next 会跳过该句继续执行，等价于本次拖动无效，故同样静默忽略。
            }
        }

        // 改变大小
        /// <summary>
    /// 是否可以向注册表储存尺寸改变信息。以此避免初始化时误储存。
    /// </summary>
        public bool IsSizeSaveable = false;
        private void FormMain_SizeChanged()
        {
            if (IsSizeSaveable)
            {
                Settings.Set("WindowHeight", this.Height);
                Settings.Set("WindowWidth", this.Width);
            }
            this.RectForm.Rect = new Rect(0d, 0d, this.BorderForm.ActualWidth, this.BorderForm.ActualHeight);
            this.PanForm.Width = this.BorderForm.ActualWidth + 0.001d;
            this.PanForm.Height = this.BorderForm.ActualHeight + 0.001d;
            this.PanMain.Width = this.PanForm.Width;
            this.PanMain.Height = Math.Max(0d, this.PanForm.Height - this.PanTitle.ActualHeight);
            if (this.WindowState == WindowState.Maximized)
                this.WindowState = WindowState.Normal; // 修复 #1938
        }

        // 最小化
        // VB 原版签名是无参的（Handles 允许），C# 委托要求 (object, EventArgs)，否则无法订阅（2026-11-11 修正）。
        private void BtnTitleMin_Click(object sender, EventArgs e)
        {
            this.WindowState = WindowState.Minimized;
        }

        // 背景图片与标题栏
        public static void UpdateBackgroundAndTitleBar(object Value) // 从设置更新
        {
            if (ModMain.FrmMain is null || !ModMain.FrmMain.IsLoaded)
                return;
            ModMain.FrmMain.UpdateBackgroundAndTitleBar();
        }
        public void UpdateBackgroundAndTitleBar()
        {
            Logger.Info("从设置更新背景图片与标题栏样式");
            // 背景图片透明度
            this.ImgBack.Opacity = Settings.Get<int>("UiBackgroundOpacity") / 1000d;
            // 背景图片模糊
            double BlurRadius = Settings.Get<int>("UiBackgroundBlur") + 1;
            if (BlurRadius == 1d)
            {
                this.ImgBack.Effect = (System.Windows.Media.Effects.Effect)null;
            }
            else
            {
                this.ImgBack.Effect = new System.Windows.Media.Effects.BlurEffect() { Radius = BlurRadius };
            }
            this.ImgBack.Margin = new Thickness(-BlurRadius / 1.8d);
            // 磨砂风格蒙层（整窗背景之上、内容之下；右侧间隙与左侧栏统一跟随）
            ApplyBackdropTint();
            // 背景图片适应方式
            int BackgroundType = Settings.Get<int>("UiBackgroundSuit");
            if (this.ImgBack.Background is not null)
            {
                ImageBrush Brush = (ImageBrush)this.ImgBack.Background;
                if (BackgroundType == 0)
                {
                    // 智能：当图片较小时平铺，较大时适应
                    if (Brush.ImageSource.Width < this.PanMain.ActualWidth / 2d && Brush.ImageSource.Height < this.PanMain.ActualHeight / 2d)
                    {
                        BackgroundType = 4; // 平铺
                    }
                    else
                    {
                        BackgroundType = 2;
                    } // 适应
                }
                Brush.Stretch = Stretch.UniformToFill;
                Brush.TileMode = TileMode.None;
                Brush.Viewport = new Rect(0d, 0d, 1d, 1d);
                Brush.ViewportUnits = BrushMappingMode.RelativeToBoundingBox;
                Brush.AlignmentX = AlignmentX.Center;
                Brush.AlignmentY = AlignmentY.Center;
                switch (BackgroundType)
                {
                    case 1: // 中
                        {
                            Brush.Stretch = Stretch.None;
                            break;
                        }
                    case 3: // 拉伸
                        {
                            Brush.Stretch = Stretch.Fill;
                            break;
                        }
                    case 4: // 平铺
                        {
                            Brush.Stretch = Stretch.None;
                            Brush.TileMode = TileMode.Tile;
                            Brush.Viewport = new Rect(0d, 0d, Brush.ImageSource.Width, Brush.ImageSource.Height);
                            Brush.ViewportUnits = BrushMappingMode.Absolute;
                            break;
                        }

                    case 2: // 适应
                        {
                            break;
                        }
                    case 5: // 左上
                        {
                            Brush.AlignmentX = AlignmentX.Left;
                            Brush.AlignmentY = AlignmentY.Top;
                            break;
                        }
                    case 6: // 右上
                        {
                            Brush.AlignmentX = AlignmentX.Right;
                            Brush.AlignmentY = AlignmentY.Top;
                            break;
                        }
                    case 7: // 左下
                        {
                            Brush.AlignmentX = AlignmentX.Left;
                            Brush.AlignmentY = AlignmentY.Bottom;
                            break;
                        }
                    case 8: // 右下
                        {
                            Brush.AlignmentX = AlignmentX.Right;
                            Brush.AlignmentY = AlignmentY.Bottom;
                            break;
                        }
                    case 9: // 左
                        {
                            Brush.AlignmentX = AlignmentX.Left;
                            Brush.AlignmentY = AlignmentY.Center;
                            break;
                        }
                    case 10: // 右
                        {
                            Brush.AlignmentX = AlignmentX.Right;
                            Brush.AlignmentY = AlignmentY.Center;
                            break;
                        }
                    case 11: // 上
                        {
                            Brush.AlignmentX = AlignmentX.Center;
                            Brush.AlignmentY = AlignmentY.Top;
                            break;
                        }
                    case 12: // 下
                        {
                            Brush.AlignmentX = AlignmentX.Center;
                            Brush.AlignmentY = AlignmentY.Bottom;
                            break;
                        }
                }
            }
            // 标题栏显示类型
            switch (Settings.Get<int>("UiLogoType"))
            {
                case 0: // 无
                    {
                        this.ShapeTitleLogo.Visibility = Visibility.Collapsed;
                        this.LabTitleLogo.Visibility = Visibility.Collapsed;
                        this.ImageTitleLogo.Visibility = Visibility.Collapsed;
                        if (!(ModMain.FrmSetupUI == null)) // TODO: 和 FrmSetupUI 解耦
                        {
                            ModMain.FrmSetupUI.CheckLogoLeft.Visibility = Visibility.Visible;
                            ModMain.FrmSetupUI.PanLogoText.Visibility = Visibility.Collapsed;
                            ModMain.FrmSetupUI.PanLogoChange.Visibility = Visibility.Collapsed;
                        }

                        break;
                    }
                case 1: // 默认（PCL Next 品牌文字；上游那个「PCL」图形标志不再作为默认外观）
                    {
                        this.ShapeTitleLogo.Visibility = Visibility.Collapsed;
                        this.LabTitleLogo.Visibility = Visibility.Visible;
                        this.ImageTitleLogo.Visibility = Visibility.Collapsed;
                        if (!(ModMain.FrmSetupUI == null))
                        {
                            ModMain.FrmSetupUI.CheckLogoLeft.Visibility = Visibility.Collapsed;
                            ModMain.FrmSetupUI.PanLogoText.Visibility = Visibility.Collapsed;
                            ModMain.FrmSetupUI.PanLogoChange.Visibility = Visibility.Collapsed;
                        }

                        break;
                    }
                case 2: // 文本
                    {
                        this.ShapeTitleLogo.Visibility = Visibility.Collapsed;
                        this.LabTitleLogo.Visibility = Visibility.Visible;
                        this.ImageTitleLogo.Visibility = Visibility.Collapsed;
                        if (!(ModMain.FrmSetupUI == null))
                        {
                            ModMain.FrmSetupUI.CheckLogoLeft.Visibility = Visibility.Collapsed;
                            ModMain.FrmSetupUI.PanLogoText.Visibility = Visibility.Visible;
                            ModMain.FrmSetupUI.PanLogoChange.Visibility = Visibility.Collapsed;
                        }

                        break;
                    }
                case 3: // 图片
                    {
                        this.ShapeTitleLogo.Visibility = Visibility.Collapsed;
                        this.LabTitleLogo.Visibility = Visibility.Collapsed;
                        this.ImageTitleLogo.Visibility = Visibility.Visible;
                        if (!(ModMain.FrmSetupUI == null))
                        {
                            ModMain.FrmSetupUI.CheckLogoLeft.Visibility = Visibility.Collapsed;
                            ModMain.FrmSetupUI.PanLogoText.Visibility = Visibility.Collapsed;
                            ModMain.FrmSetupUI.PanLogoChange.Visibility = Visibility.Visible;
                        }
                        try
                        {
                            this.ImageTitleLogo.Source = Paths.Base + @"PCL\Logo.png";
                        }
                        catch (Exception ex)
                        {
                            this.ImageTitleLogo.Source = (string)null;
                            Logger.Error(ex, "显示标题栏图片失败", LogBehavior.Alert);
                        }

                        break;
                    }
            }
            ModMain.FrmSetupUI?.CardLogo.TriggerForceResize();
            // 标题栏文本：默认档固定显示品牌名，文本档用用户自定义内容（留空时回落到品牌名）
            if (Settings.Get<int>("UiLogoType") == 1)
            {
                this.LabTitleLogo.Text = "PCL Next";
            }
            else
            {
                this.LabTitleLogo.Text = Settings.Get<string>("UiLogoText");
                if (string.IsNullOrWhiteSpace(this.LabTitleLogo.Text))
                    this.LabTitleLogo.Text = "PCL Next";
            }
            // 标题栏文本是否居左
            this.PanTitleMain.ColumnDefinitions[0].Width = new GridLength(Settings.Get<bool>("UiLogoLeft") && Settings.Get<int>("UiLogoType") == 0 ? 0 : 1, GridUnitType.Star);
        }

        /// <summary>
        /// 按「磨砂风格」设置左侧栏的薄半透明蒙层；整窗不再叠加白色蒙层，背景图原样透出，
        /// 左右两侧统一跟随底层背景与全局「背景模糊」（ImgBack.Effect），不做左侧独立局部模糊。
        /// 档位由设置项 UiBackgroundBlurStyle 控制：0 经典磨砂 / 1 通透柔化 / 2 深色磨砂 / 3 主题亚克力。
        /// 无背景图时侧栏回退到主题默认侧栏画刷。
        /// </summary>
        private void ApplyBackdropTint()
        {
            // 整窗白色蒙层：始终隐藏，避免把背景图洗成白雾。
            this.RectBackdropTint.Visibility = Visibility.Collapsed;
            this.RectBackdropTint.Fill = null;

            bool hasImage = this.ImgBack.Visibility == Visibility.Visible && this.ImgBack.Background is not null;
            if (!hasImage)
            {
                this.RectLeftBackground.Fill = (Brush)Application.Current.Resources["ColorBrushSidebarGlass"];
                return;
            }
            bool isDark = ModSecret.ThemeNow == ModSecret.ThemeDarkId;
            int style = Settings.Get<int>("UiBackgroundBlurStyle");
            Color tint;
            switch (style)
            {
                case 1: // 通透柔化：更淡的蒙层，背景更多透出
                    tint = isDark ? Color.FromArgb(28, 255, 255, 255) : Color.FromArgb(38, 255, 255, 255);
                    break;
                case 2: // 深色磨砂：压暗的黑色蒙层
                    tint = Color.FromArgb(95, 0, 0, 0);
                    break;
                case 3: // 主题亚克力：主题强调色低透明度
                    {
                        Color accent = Application.Current.Resources.Contains("ColorObject3")
                            ? (Color)Application.Current.Resources["ColorObject3"]
                            : Colors.Gray;
                        tint = Color.FromArgb(55, accent.R, accent.G, accent.B);
                        break;
                    }
                default: // 0 经典磨砂：薄半透明白，背景与全局模糊仍可透出
                    tint = isDark ? Color.FromArgb(80, 24, 24, 28) : Color.FromArgb(70, 255, 255, 255);
                    break;
            }
            this.RectLeftBackground.Fill = new SolidColorBrush(tint);
        }


        #endregion

        #region 窗体事件

        // 按键事件
        private void FormMain_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.IsRepeat)
                return;
            // 修复按下 Alt 后误认为弹出系统菜单导致的冻结
            if (e.SystemKey == Key.LeftAlt || e.SystemKey == Key.RightAlt)
                e.Handled = true;
            // 在有弹窗时：回车选择第一个，Esc 选择最后一个
            if (this.PanMsg.Children.Any())
            {
                if (e.Key == Key.Enter)
                {
                    ((dynamic)((object)this.PanMsg.Children[0])).Btn1_Click();
                }
                else if (e.Key == Key.Escape)
                {
                    object Msg = this.PanMsg.Children[0];
                    if (!(Msg is MyMsgInput) && !(Msg is MyMsgSelect) && Conversions.ToBoolean(Operators.ConditionalCompareObjectEqual(((dynamic)Msg).Btn3.Visibility, Visibility.Visible, false)))
                    {
                        ((dynamic)Msg).Btn3_Click();
                    }
                    else if (Conversions.ToBoolean(Operators.ConditionalCompareObjectEqual(((dynamic)Msg).Btn2.Visibility, Visibility.Visible, false)))
                    {
                        ((dynamic)Msg).Btn2_Click();
                    }
                    else
                    {
                        ((dynamic)Msg).Btn1_Click();
                    }
                }
                return;
            }

            // ==========================
            // 在没有弹窗时：继续检查……
            // ==========================

            // 按 ESC 取消启动，或返回上一级
            if (e.Key == Key.Escape)
            {
                if (PageCurrent == (PageStackData)PageType.Launch && ModLaunch.McLaunchLoader.State == ModBase.LoadState.Loading)
                {
                    ModMain.FrmLaunchLeft.BtnCancel_Click();
                    return;
                }
                TriggerPageBack();
            }
            // 更改隐藏版本可见性
            if (e.Key == Key.F11 && PageCurrent == (PageStackData)PageType.InstanceSelect)
            {
                ModMain.FrmSelectRight.ShowHidden = !ModMain.FrmSelectRight.ShowHidden;
                ModLoader.LoaderFolderRun(ModMinecraft.McInstanceListLoader, ModMinecraft.McFolderSelected, ModLoader.LoaderFolderRunType.ForceRun, MaxDepth: 1, ExtraPath: @"versions\");
                return;
            }
            // 更改功能隐藏可见性
            if (e.Key == Key.F12)
            {
                PageSetupHome.HiddenForceShow = !PageSetupHome.HiddenForceShow;
                if (PageSetupHome.HiddenForceShow)
                {
                    ModMain.Hint("功能隐藏设置已暂时关闭！", ModMain.HintType.Green);
                }
                else
                {
                    ModMain.Hint("功能隐藏设置已重新开启！", ModMain.HintType.Green);
                }
                PageSetupHome.HiddenRefresh();
                return;
            }
            // 按 F5 刷新页面
            if (e.Key == Key.F5)
            {
                if (PageLeft is IRefreshable)
                    ((IRefreshable)PageLeft).Refresh();
                if (PageRight is IRefreshable)
                    ((IRefreshable)PageRight).Refresh();
                return;
            }
            // 调用启动游戏
            if (e.Key == Key.Enter && PageCurrent == (PageStackData)PageType.Launch)
            {
                if (ModMain.IsAprilEnabled && !ModMain.IsAprilGiveup)
                {
                    ModMain.Hint("木大！");
                }
                else
                {
                    ModMain.FrmLaunchLeft.LaunchButtonClick();
                }
            }
        }
        private void FormMain_MouseDown(object sender, MouseButtonEventArgs e)
        {
            // 鼠标侧键返回上一级
            if (ModMain.FrmMain.PanMsg.Children.Count > 0 || ModMain.WaitingMyMsgBox.Any())
                return; // 弹窗中（#5513）
            if (e.ChangedButton == MouseButton.XButton1 || e.ChangedButton == MouseButton.XButton2)
                TriggerPageBack();
        }
        private void TriggerPageBack()
        {
            if (PageCurrent == (PageStackData)PageType.Download && PageCurrentSub == PageSubType.DownloadInstall && ModMain.FrmDownloadInstall.IsInSelectPage)
            {
                ModMain.FrmDownloadInstall.ExitSelectPage();
            }
            else
            {
                PageBack();
            }
        }

        // 切回窗口
        private void FormMain_Activated()
        {
            // 切回窗口时自动刷新
            try
            {
                if (PageCurrent == (PageStackData)PageType.InstanceSetup && PageCurrentSub == PageSubType.InstanceMod)
                {
                    // Mod 管理
                    ModMain.FrmInstanceMod.ReloadModList();
                }
                else if (PageCurrent == (PageStackData)PageType.InstanceSetup && (PageCurrentSub == PageSubType.InstanceSetup || PageCurrentSub == PageSubType.InstanceExport))
                {
                    // 更新当前选用的 Java
                    PageInstanceLeft.ReloadCurrentJava();
                }
                else if (PageCurrent == (PageStackData)PageType.InstanceSelect)
                {
                    // 版本选择
                    ModLoader.LoaderFolderRun(ModMinecraft.McInstanceListLoader, ModMinecraft.McFolderSelected, ModLoader.LoaderFolderRunType.RunOnUpdated, MaxDepth: 1, ExtraPath: @"versions\");
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "切回窗口时出错");
            }
            // 读取剪贴板，自动加入联机房间
            // 原 VB 该处即为 Return 'TODO: 联机复活赛（FormMain.xaml.vb:973），其后的自动加入房间逻辑
            // 在原实现中就已停用；转换后这段代码永远执行不到（CS0162），故删除，原文保留在 .vb 中供日后恢复
            return; // TODO: 联机复活赛
        }

        private IDataObject _HandleDrag_PrevData = default;
        private DragDropEffects _HandleDrag_PrevEffects = default;

        // 文件拖放
        private void HandleDrag(object sender, DragEventArgs e)
        {
            try
            {
                if (e.Handled && e.Effects != DragDropEffects.None)
                    return;
                // 缓存
                e.Handled = true;
                if (ReferenceEquals(e.Data, _HandleDrag_PrevData))
                {
                    e.Effects = _HandleDrag_PrevEffects;
                    return;
                }
                // 确定拖放效果
                e.Effects = DragDropEffects.None;
                if (e.Data.GetDataPresent(DataFormats.Text))
                {
                    string Str = Conversions.ToString(e.Data.GetData(DataFormats.Text));
                    if (Str.StartsWithF("authlib-injector:yggdrasil-server:"))
                    {
                        e.Effects = DragDropEffects.Copy;
                    }
                    else if (Str.StartsWithF("file:///"))
                    {
                        e.Effects = DragDropEffects.Copy;
                    }
                }
                else if (e.Data.GetDataPresent(DataFormats.FileDrop))
                {
                    string[] Files = (string[])e.Data.GetData(DataFormats.FileDrop);
                    if (Files is not null && Files.Length > 0)
                    {
                        e.Effects = DragDropEffects.Link;
                    }
                }
                _HandleDrag_PrevData = e.Data;
                _HandleDrag_PrevEffects = e.Effects;
                Logger.Info($"设置拖放类型：{e.Effects}");
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "处理拖放时出错");
            }
        }
        private void FrmMain_Drop(object sender, DragEventArgs e)
        {
            try
            {
                ShowWindowToTop();
                if (e.Data.GetDataPresent(DataFormats.Text))
                {
                    // 获取文本
                    try
                    {
                        string Str = Conversions.ToString(e.Data.GetData(DataFormats.Text));
                        Logger.Info($"接受文本拖拽：{Str}");
                        if (Str.StartsWithF("authlib-injector:yggdrasil-server:"))
                        {
                            // Authlib 拖拽
                            e.Handled = true;
                            e.Effects = DragDropEffects.Copy;
                            string AuthlibServer = StringUtils.FormUrlUnescape(Str.Substring("authlib-injector:yggdrasil-server:".Length));
                            Logger.Info($"Authlib 拖拽：{AuthlibServer}");
                            if (!string.IsNullOrEmpty(new ValidateHttp().Validate(AuthlibServer)))
                            {
                                ModMain.Hint($"输入的 Authlib 验证服务器不符合网址格式（{AuthlibServer}）！", ModMain.HintType.Red);
                                return;
                            }
                            var Target = PageCurrent == (PageStackData)PageType.InstanceSetup ? PageInstanceLeft.Instance : ModMinecraft.McInstanceSelected;
                            if (Target is null)
                            {
                                ModMain.Hint("请先下载游戏，再设置第三方登录！", ModMain.HintType.Red);
                                return;
                            }
                            if (AuthlibServer == "https://littleskin.cn/api/yggdrasil")
                            {
                                // LittleSkin
                                if (ModMain.MyMsgBox($"是否要在版本 {Target.Name} 中开启 LittleSkin 登录？" + Constants.vbCrLf + "你可以在 版本设置 → 设置 → 服务器选项 中修改登录方式。", "第三方登录开启确认", "确定", "取消") == 2)
                                {
                                    return;
                                }
                                Settings.Set("VersionServerLogin", 4, Instance: Target);
                                Settings.Set("VersionServerAuthServer", "https://littleskin.cn/api/yggdrasil", Instance: Target);
                                Settings.Set("VersionServerAuthRegister", "https://littleskin.cn/auth/register", Instance: Target);
                                Settings.Set("VersionServerAuthName", "LittleSkin 登录", Instance: Target);
                            }
                            else
                            {
                                // 第三方 Authlib 服务器
                                if (ModMain.MyMsgBox($"是否要在版本 {Target.Name} 中开启第三方登录？" + Constants.vbCrLf + $"登录服务器：{AuthlibServer}" + Constants.vbCrLf + Constants.vbCrLf + "你可以在 版本设置 → 设置 → 服务器选项 中修改登录方式。", "第三方登录开启确认", "确定", "取消") == 2)
                                {
                                    return;
                                }
                                Settings.Set("VersionServerLogin", 4, Instance: Target);
                                Settings.Set("VersionServerAuthServer", AuthlibServer, Instance: Target);
                                Settings.Set("VersionServerAuthRegister", AuthlibServer.Replace("api/yggdrasil", "auth/register"), Instance: Target);
                                Settings.Set("VersionServerAuthName", "", Instance: Target);
                            }
                            if (PageCurrent == (PageStackData)PageType.InstanceSetup && PageCurrentSub == PageSubType.InstanceSetup)
                            {
                                // 正在服务器选项页，需要刷新设置项显示
                                ModMain.FrmInstanceSetup.Reload();
                            }
                            else if (PageCurrent == (PageStackData)PageType.Launch)
                            {
                                // 正在主页，需要刷新左边栏
                                ModMain.FrmLaunchLeft.RefreshPage(true, false);
                            }
                        }
                        else if (Str.StartsWithF("file:///"))
                        {
                            // 文件拖拽（例如从浏览器下载窗口拖入）
                            string FilePath = WebUtility.UrlDecode(Str).Substring("file:///".Length).Replace("/", @"\");
                            e.Handled = true;
                            e.Effects = DragDropEffects.Copy;
                            FileDrag(new List<string>() { FilePath });
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.Warn(ex, "无法接取文本拖拽事件");
                        return;
                    }
                }
                else if (e.Data.GetDataPresent(DataFormats.FileDrop))
                {
                    // 获取文件并检查
                    var FilePathRaw = e.Data.GetData(DataFormats.FileDrop);
                    if (FilePathRaw is null) // #2690
                    {
                        ModMain.Hint("请将文件解压后再拖入！", ModMain.HintType.Red);
                        return;
                    }
                    e.Handled = true;
                    e.Effects = DragDropEffects.Link;
                    FileDrag((IEnumerable<string>)FilePathRaw);
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "接取拖拽事件失败");
            }
        }
        private void FileDrag(IEnumerable<string> FilePathList)
        {
            ModBase.RunInNewThread(() =>
        {
            string FilePath = FilePathList.First();
            Logger.Warn($"接受文件拖拽：{FilePath}{(FilePathList.Any() ? $" 等 {FilePathList.Count()} 个文件" : "")}");
            // 基础检查
            if (DirectoryUtils.Exists(FilePathList.First()) && !FileUtils.Exists(FilePathList.First()))
            {
                ModMain.Hint("请拖入一个文件，而非文件夹！", ModMain.HintType.Red);
                return;
            }
            else if (!FileUtils.Exists(FilePathList.First()))
            {
                ModMain.Hint("拖入的文件不存在：" + FilePathList.First(), ModMain.HintType.Red);
                return;
            }
            // 多文件拖拽
            var PathList = FilePathList.ToList();
            if (FilePathList.Count() > 1)
            {
                // 必须要求全部为 jar 文件
                foreach (var File in PathList)
                {
                    if (!new[] { "jar", "litemod", "disabled", "old" }.Contains(PathUtils.GetExtension(File)))
                    {
                        ModMain.Hint("一次请只拖入一个文件！", ModMain.HintType.Red);
                        return;
                    }
                }
            }
            // 主页
            string Extension = PathUtils.GetExtension(FilePath);
            if (Extension == "xaml")
            {
                Logger.Info("文件后缀为 XAML，作为主页加载");
                if (FileUtils.Exists(Paths.Base + @"PCL\Custom.xaml"))
                {
                    if (ModMain.MyMsgBox("已存在一个主页文件，是否要将它覆盖？", "覆盖确认", "覆盖", "取消") == 2)
                    {
                        return;
                    }
                }
                FileUtils.Copy(FilePath, Paths.Base + @"PCL\Custom.xaml");
                ModBase.RunInUi(() =>
        {
                    Settings.Set("UiCustomType", 1);
                    ModMain.FrmLaunchRight.ForceRefresh();
                    ModMain.Hint("已加载主页自定义文件！", ModMain.HintType.Green);
                });
                return;
            }
            // 安装 Mod
            if (PageInstanceMod.InstallMods(PathList))
                return;
            // 安装整合包
            if (new[] { "zip", "rar", "mrpack" }.Any(t => (t ?? "") == (Extension ?? ""))) // 部分压缩包是 zip 格式但后缀为 rar，总之试一试
            {
                ModMain.Hint("正在读取压缩包……");
                try
                {
                    Logger.Info("文件为压缩包，尝试作为整合包安装");
                    ModModpack.ModpackInstall(FilePath);
                    ModBase.RunInUi(() => ModMain.FrmMain.PageChange((PageStackData)PageType.TaskManager));
                    return;
                }
                catch (Exception ex)
                {
                    if (ModDownloadPolicy.RestrictMcDownload && ex.Message == "为遵守相关法律法规，PCL Next 不再提供 MC 下载功能，非常抱歉 qwq……")
                    {
                        ModMain.Hint(ex.Message, ModMain.HintType.Red);
                        return;
                    }
                    if (ex.IsCanceled())
                        return;
                    // 安装失败，继续往后尝试
                }
            }
            // RAR 处理
            if (Extension == "rar")
            {
                ModMain.Hint("PCL Next 无法处理 rar 格式的压缩包，请在解压后重新压缩为 zip 格式再试！", ModMain.HintType.Red);
                return;
            }
            // 错误报告分析
            do
            {
                try
                {
                    Logger.Info("尝试进行错误报告分析");
                    var Analyzer = new CrashAnalyzer();
                    Analyzer.Import(FilePath);
                    if (!Analyzer.Prepare())
                        break;
                    Analyzer.Analyze();
                    Analyzer.Output(true, new List<string>());
                    return;
                }
                catch (Exception ex)
                {
                    Logger.Error(ex, "自主错误报告分析失败");
                }
            }
            while (false);
            // 未知操作
            ModMain.Hint("PCL Next 无法确定适合的操作……", ModMain.HintType.Red);
        }, "文件拖拽");
        }

        // 接受到 Windows 窗体事件
        public bool IsSystemTimeChanged = false;
        private nint WndProc(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
        {
            if (msg == 30)
            {
                var NowDate = DateTime.Now;
                if (NowDate.Date == ModBase.ApplicationOpenTime.Date)
                {
                    Logger.Info($"系统时间微调为：{NowDate.ToLongDateString()} {NowDate.ToLongTimeString()}");
                    IsSystemTimeChanged = false;
                }
                else
                {
                    Logger.Info($"系统时间修改为：{NowDate.ToLongDateString()} {NowDate.ToLongTimeString()}");
                    IsSystemTimeChanged = true;
                }
            }
            else if (msg == 400 * 16 + 2)
            {
                Logger.Info($"收到置顶信息：{hwnd.ToInt64()}");
                if (!IsWindowLoadFinished)
                {
                    Logger.Info("窗口尚未加载完成，忽略置顶请求");
                    return nint.Zero;
                }
                ShowWindowToTop();
                handled = true;
            }
            return nint.Zero;
        }

        // 窗口隐藏与置顶
        private bool _Hidden = false;
        public bool Hidden
        {
            get
            {
                return _Hidden;
            }
            set
            {
                if (_Hidden == value)
                    return;
                _Hidden = value;
                if (value)
                {
                    // 隐藏
                    this.Left -= 10000d;
                    this.ShowInTaskbar = false;
                    this.Visibility = Visibility.Hidden;
                    Logger.Info($"窗口已隐藏，位置：({this.Left},{this.Top})");
                }
                else
                {
                    // 取消隐藏
                    if (this.Left < (double)-2000)
                        this.Left += 10000d;
                    ShowWindowToTop();
                }
            }
        }
        /// <summary>
    /// 把当前窗口拖到最前面。
    /// </summary>
        public void ShowWindowToTop()
        {
            ModBase.RunInUi(() =>
        {
            // 这一坨乱七八糟的，别改，改了指不定就炸了，自己电脑还复现不出来
            this.Visibility = Visibility.Visible;
            this.ShowInTaskbar = true;
            this.WindowState = WindowState.Normal;
            Hidden = false;
            this.Topmost = true; // 偶尔 SetForegroundWindow 失效
            this.Topmost = false;
            ModMain.SetForegroundWindow(ModBase.Handle);
            this.Focus();
            Logger.Info($"窗口已置顶，位置：({this.Left}, {this.Top}), {this.Width} x {this.Height}");
        });
        }

        #endregion

        #region 切换页面

        // 页面种类与属性
        /// <summary>
    /// 页面种类。
    /// 该枚举在自定义事件中使用，是公开 API 的一部分。
    /// </summary>
        public enum PageType
        {
            /// <summary>
        /// 启动。
        /// </summary>
            Launch = 0,
            /// <summary>
        /// 下载。
        /// </summary>
            Download = 1,
            /// <summary>
        /// 联机。
        /// </summary>
            Link = 2,
            /// <summary>
        /// 设置。
        /// </summary>
            Setup = 3,
            /// <summary>
        /// 更多。
        /// </summary>
            Other = 4,
            /// <summary>
        /// 版本选择。这是一个副页面。
        /// </summary>
            InstanceSelect = 5,
            /// <summary>
        /// 任务管理。这是一个副页面。
        /// </summary>
            TaskManager = 6,
            /// <summary>
        /// 版本设置。这是一个副页面。
        /// </summary>
            InstanceSetup = 7,
            /// <summary>
        /// 社区资源详情。这是一个副页面。
        /// </summary>
            ResourceDetail = 8,
            /// <summary>
        /// 帮助详情。这是一个副页面。
        /// </summary>
            HelpDetail = 9
        }
        /// <summary>
    /// 次要页面种类。其数值必须与 StackPanel 中的下标一致。
    /// 该枚举在自定义事件中使用，是公开 API 的一部分。
    /// </summary>
        public enum PageSubType
        {
            Default = 0,
            DownloadInstall = 0,
            DownloadMod = 2,
            DownloadPack = 3,
            DownloadDataPack = 4,
            DownloadResourcePack = 5,
            DownloadShader = 6,
            SetupLaunch = 0,
            SetupLink = 1,
            SetupUI = 2,
            SetupBackdrop = 3,
            SetupHome = 4,
            SetupDownload = 5,
            SetupNotify = 6,
            SetupAccount = 7,
            SetupSystem = 8,
            SetupUIAdv = 9,
            LinkMain = 0,
            OtherHelp = 0,
            OtherAbout = 1,
            OtherTest = 2,
            InstanceOverall = 0,
            InstanceSetup = 1,
            InstanceModify = 2,
            InstanceMod = 3,
            InstanceModDisabled = 4,
            InstanceExport = 5,
            InstanceBackup = 6
        }
        /// <summary>
    /// 获取次级页面的名称。若并非次级页面则返回空字符串，故可以以此判断是否为次级页面。
    /// </summary>
        private string PageNameGet(PageStackData Stack)
        {
            switch (Stack.Page)
            {
                case PageType.InstanceSelect:
                    {
                        return "版本选择";
                    }
                case PageType.TaskManager:
                    {
                        return "任务管理";
                    }
                case PageType.InstanceSetup:
                    {
                        return "版本设置 - " + (PageInstanceLeft.Instance is null ? "未知版本" : PageInstanceLeft.Instance.Name);
                    }
                case PageType.ResourceDetail:
                    {
                        return ((ResourceProject)((dynamic)Stack.Additional)[0]).TranslatedName;
                    }
                case PageType.HelpDetail:
                    {
                        return ((ModMain.HelpEntry)((dynamic)Stack.Additional)[0]).Title;
                    }

                default:
                    {
                        return "";
                    }
            }
        }
        /// <summary>
    /// 刷新次级页面的名称。
    /// </summary>
        public void PageNameRefresh(PageStackData Type)
        {
            this.LabTitleInner.Text = PageNameGet(Type);
        }
        /// <summary>
    /// 刷新次级页面的名称。
    /// </summary>
        public void PageNameRefresh()
        {
            PageNameRefresh(PageCurrent);
        }

        // 页面状态存储
        /// <summary>
    /// 当前的主页面。
    /// </summary>
        public PageStackData PageCurrent = (PageStackData)PageType.Launch;
        /// <summary>
    /// 上一个主页面。
    /// </summary>
        public PageStackData PageLast = (PageStackData)PageType.Launch;
        /// <summary>
    /// 当前的子页面。
    /// </summary>
        public PageSubType PageCurrentSub
        {
            get
            {
                // 对应 FormMain.xaml.vb:1374-1387 的 Select Case PageCurrent（VB 会把 PageStackData 隐式转为 PageType 再比较）。
                // C# 不允许 int 隐式转枚举，故各 Case 分支统一用 (PageType) 显式强转；数值由 PageType 定义确定
                // （1 = Download、3 = Setup、4 = Other、7 = InstanceSetup）。
                switch (PageCurrent.Page)
                {
                    case (PageType)1:
                        {
                            if (ModMain.FrmDownloadLeft is null)
                                ModMain.FrmDownloadLeft = new PageDownloadLeft();
                            return ModMain.FrmDownloadLeft.PageID;
                        }

                    case (PageType)3:
                        {
                            if (ModMain.FrmSetupLeft is null)
                                ModMain.FrmSetupLeft = new PageSetupLeft();
                            return ModMain.FrmSetupLeft.PageID;
                        }

                    case (PageType)4:
                        {
                            if (ModMain.FrmOtherLeft is null)
                                ModMain.FrmOtherLeft = new PageOtherLeft();
                            return ModMain.FrmOtherLeft.PageID;
                        }

                    case (PageType)7:
                        {
                            if (ModMain.FrmInstanceLeft is null)
                                ModMain.FrmInstanceLeft = new PageInstanceLeft();
                            return ModMain.FrmInstanceLeft.PageID;
                        }

                    default:
                        {
                            // 对应 FormMain.xaml.vb:1388「Return 0」：VB 允许 int 隐式转枚举，C# 需显式强转。
                            return (PageSubType)0; // 没有子页面
                        }
                }
            }
        }
        /// <summary>
    /// 上层页面的编号堆栈，用于返回。
    /// </summary>
        public List<PageStackData> PageStack = new List<PageStackData>();
        public class PageStackData
        {

            public PageType Page;
            public object Additional;

            public override bool Equals(object other)
            {
                if (other is null)
                    return false;
                if (other is PageStackData)
                {
                    PageStackData PageOther = (PageStackData)other;
                    if (Page != PageOther.Page)
                        return false;
                    if (Additional is null)
                    {
                        return PageOther.Additional is null;
                    }
                    else
                    {
                        return PageOther.Additional is not null && Additional.Equals(PageOther.Additional);
                    }
                }
                else if (other is int)
                {
                    if (Conversions.ToBoolean(Operators.ConditionalCompareObjectNotEqual(Page, other, false)))
                        return false;
                    return Additional is null;
                }
                else
                {
                    return false;
                }
            }
            public static bool operator ==(PageStackData left, PageStackData right)
            {
                return EqualityComparer<PageStackData>.Default.Equals(left, right);
            }
            public static bool operator !=(PageStackData left, PageStackData right)
            {
                return !(left == right);
            }
            public static implicit operator PageStackData(PageType Value)
            {
                return new PageStackData() { Page = Value };
            }
            public static implicit operator PageType(PageStackData Value)
            {
                return Value.Page;
            }
        }
        public MyPageLeft PageLeft;
        public MyPageRight PageRight;

        // 引发实际页面切换的入口
        private bool IsChangingPage = false;
        /// <summary>
    /// 切换页面，并引起对应选择 UI 的改变。
    /// </summary>
        public void PageChange(PageStackData Stack, PageSubType SubType = PageSubType.Default)
        {
            if (string.IsNullOrEmpty(PageNameGet(Stack)))
            {
                // 切换到主页面
                PageChangeExit();
                IsChangingPage = true; // 防止下面的勾选直接触发了 PageChangeActual
                // 对应 FormMain.xaml.vb:1443「CType(PanTitleSelect.Children(Stack), MyRadioButton)」：
                // VB 把 PageStackData 隐式转为 PageType 再由 Children 的 Item(Int32) 转 int，C# 需显式 (int)。
                ((MyRadioButton)this.PanTitleSelect.Children[(int)Stack.Page]).SetChecked(true, true, string.IsNullOrEmpty(PageNameGet(PageCurrent)));
                IsChangingPage = false;
                switch (Stack.Page)
                {
                    case PageType.Download:
                        {
                            if (ModMain.FrmDownloadLeft is null)
                                ModMain.FrmDownloadLeft = new PageDownloadLeft();
                            ((MyListItem)ModMain.FrmDownloadLeft.PanItem.Children[(int)SubType]).SetChecked(true, true, Stack == PageCurrent);
                            break;
                        }
                    case PageType.Setup:
                        {
                            if (ModMain.FrmSetupLeft is null)
                                ModMain.FrmSetupLeft = new PageSetupLeft();
                            ((MyListItem)ModMain.FrmSetupLeft.PanItem.Children[(int)SubType]).SetChecked(true, true, Stack == PageCurrent);
                            break;
                        }
                    case PageType.Other:
                        {
                            if (ModMain.FrmOtherLeft is null)
                                ModMain.FrmOtherLeft = new PageOtherLeft();
                            ((MyListItem)ModMain.FrmOtherLeft.PanItem.Children[(int)SubType]).SetChecked(true, true, Stack == PageCurrent);
                            break;
                        }
                }
                PageChangeActual(Stack, SubType);
            }
            else
            {
                // 切换到次页面
                switch (Stack.Page)
                {
                    case PageType.InstanceSetup:
                        {
                            if (ModMain.FrmInstanceLeft is null)
                                ModMain.FrmInstanceLeft = new PageInstanceLeft();
                            ((MyListItem)ModMain.FrmInstanceLeft.PanItem.Children[(int)SubType]).SetChecked(true, true, Stack == PageCurrent);
                            break;
                        }
                }
                PageChangeActual(Stack, SubType);
            }
        }
        /// <summary>
    /// 通过点击导航栏改变页面。
    /// </summary>
        private void BtnTitleSelect_Click(MyRadioButton sender, bool raiseByMouse)
        {
            if (IsChangingPage)
                return;
            // 原文为 PageChangeActual(Val(sender.Tag))：Tag 在 XAML 里是字符串 "0".."4"，
            // VB 先 Val 成数字、再隐式转成 PageType，最后由 PageStackData 的隐式运算符包一层；
            // 不能把 Tag 直接当 PageStackData 硬转（string → class 必抛 InvalidCastException）
            PageChangeActual((PageType)Conversions.ToInteger(sender.Tag));
        }
        /// <summary>
    /// 通过点击返回按钮或手动触发返回来改变页面。
    /// </summary>
        public void PageBack()
        {
            if (PageStack.Any())
            {
                PageChangeActual(PageStack[0]);
            }
            else
            {
                PageChange((PageStackData)PageType.Launch);
            }
        }

        // 实际处理页面切换
        /// <summary>
    /// 切换现有页面的实际方法。
    /// </summary>
        private void PageChangeActual(PageStackData Stack, PageSubType SubType = (PageSubType)(-1))
        {
            if (PageCurrent == Stack && (PageCurrentSub == SubType || (int)SubType == -1))
                return;
            if (Stack.Page == PageType.InstanceSetup && PageInstanceLeft.Instance is not null && ModMinecraft.McInstanceLock.IsLocked(PageInstanceLeft.Instance))
            {
                ModMain.Hint("该版本正在被修改，暂时无法进入版本设置！", ModMain.HintType.Red);
                return;
            }
            ModAnimation.AniControlEnabled += 1;
            try
            {

                #region 子页面处理
                string PageName = PageNameGet(Stack);
                if (string.IsNullOrEmpty(PageName))
                {
                    // 即将切换到一个顶级页面
                    PageChangeExit();
                }
                // 即将切换到一个子页面
                else if (PageStack.Any())
                {
                    // 子页面 → 另一个子页面，更新
                    ModAnimation.AniStart(new[] { ModAnimation.AaOpacity(this.LabTitleInner, -this.LabTitleInner.Opacity, 130), ModAnimation.AaCode(() => this.LabTitleInner.Text = PageName, After: true), ModAnimation.AaOpacity(this.LabTitleInner, 1d, 150, 30) }, "FrmMain Titlebar SubLayer");
                    if (PageStack.Contains(Stack))
                    {
                        // 返回到更上层的子页面
                        while (PageStack.Contains(Stack))
                            PageStack.RemoveAt(0);
                    }
                    else
                    {
                        // 进入更深层的子页面
                        PageStack.Insert(0, PageCurrent);
                    }
                }
                else
                {
                    // 主页面 → 子页面，进入
                    this.PanTitleInner.Visibility = Visibility.Visible;
                    this.PanTitleMain.IsHitTestVisible = false;
                    this.PanTitleInner.IsHitTestVisible = true;
                    PageNameRefresh(Stack);
                    ModAnimation.AniStart(new[] { ModAnimation.AaOpacity(this.PanTitleMain, -this.PanTitleMain.Opacity, 150), ModAnimation.AaX(this.PanTitleMain, 12d - this.PanTitleMain.Margin.Left, 150, Ease: new ModAnimation.AniEaseInFluent(ModAnimation.AniEasePower.Weak)), ModAnimation.AaOpacity(this.PanTitleInner, 1d - this.PanTitleInner.Opacity, 150, 200), ModAnimation.AaX(this.PanTitleInner, -this.PanTitleInner.Margin.Left, 350, 200, new ModAnimation.AniEaseOutBack()), ModAnimation.AaCode(() => this.PanTitleMain.Visibility = Visibility.Collapsed, After: true) }, "FrmMain Titlebar FirstLayer");
                    PageStack.Insert(0, PageCurrent);
                }
                #endregion

                #region 实际更改页面框架 UI
                PageLast = PageCurrent;
                PageCurrent = Stack;
                switch (Stack.Page)
                {
                    case PageType.Launch: // 启动
                        {
                            this.PageChangeAnim(ModMain.FrmLaunchLeft, ModMain.FrmLaunchRight);
                            break;
                        }
                    case PageType.Download: // 下载
                        {
                            if (ModMain.FrmDownloadLeft is null)
                                ModMain.FrmDownloadLeft = new PageDownloadLeft();
                            // PageGet 方法会在未设置 SubType 时指定默认值，并建立相关页面的实例
                            this.PageChangeAnim(ModMain.FrmDownloadLeft, (FrameworkElement)ModMain.FrmDownloadLeft.PageGet(SubType));
                            break;
                        }
                    case PageType.Link: // 联机
                        {
                            if (ModMain.FrmLinkMain is null)
                                ModMain.FrmLinkMain = new PageLinkMain();
                            this.PageChangeAnim(new MyPageLeft(), ModMain.FrmLinkMain);
                            break;
                        }
                    case PageType.Setup: // 设置
                        {
                            if (ModMain.FrmSetupLeft is null)
                                ModMain.FrmSetupLeft = new PageSetupLeft();
                            this.PageChangeAnim(ModMain.FrmSetupLeft, (FrameworkElement)ModMain.FrmSetupLeft.PageGet(SubType));
                            break;
                        }
                    case PageType.Other: // 更多
                        {
                            if (ModMain.FrmOtherLeft is null)
                                ModMain.FrmOtherLeft = new PageOtherLeft();
                            this.PageChangeAnim(ModMain.FrmOtherLeft, (FrameworkElement)ModMain.FrmOtherLeft.PageGet(SubType));
                            break;
                        }
                    case PageType.InstanceSelect: // 版本选择
                        {
                            if (ModMain.FrmSelectLeft is null)
                                ModMain.FrmSelectLeft = new PageSelectLeft();
                            if (ModMain.FrmSelectRight is null)
                                ModMain.FrmSelectRight = new PageSelectRight();
                            this.PageChangeAnim(ModMain.FrmSelectLeft, ModMain.FrmSelectRight);
                            break;
                        }
                    case PageType.TaskManager: // 任务管理
                        {
                            if (ModMain.FrmSpeedLeft is null)
                                ModMain.FrmSpeedLeft = new PageSpeedLeft();
                            if (ModMain.FrmSpeedRight is null)
                                ModMain.FrmSpeedRight = new PageSpeedRight();
                            this.PageChangeAnim(ModMain.FrmSpeedLeft, ModMain.FrmSpeedRight);
                            break;
                        }
                    case PageType.InstanceSetup: // 版本设置
                        {
                            if (ModMain.FrmInstanceLeft is null)
                                ModMain.FrmInstanceLeft = new PageInstanceLeft();
                            this.PageChangeAnim(ModMain.FrmInstanceLeft, (FrameworkElement)ModMain.FrmInstanceLeft.PageGet(SubType));
                            break;
                        }
                    case PageType.ResourceDetail: // 社区资源详情
                        {
                            if (ModMain.FrmDownloadResourceDetail is null)
                                ModMain.FrmDownloadResourceDetail = new PageDownloadResourceDetail();
                            this.PageChangeAnim(new MyPageLeft(), ModMain.FrmDownloadResourceDetail);
                            break;
                        }
                    case PageType.HelpDetail: // 帮助详情
                        {
                            PageChangeAnim(new MyPageLeft(), (FrameworkElement)((dynamic)Stack.Additional)[1]);
                            break;
                        }
                }
                #endregion

                #region 设置为最新状态
                this.BtnExtraDownload.ShowRefresh();
                this.BtnExtraApril.ShowRefresh();
                #endregion

                Logger.Info($"切换主要页面：{Stack.Page}, {SubType}");
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"切换主要页面失败（ID {PageCurrent.Page}）");
            }
            finally
            {
                ModAnimation.AniControlEnabled -= 1;
            }
        }
        private void PageChangeAnim(FrameworkElement TargetLeft, FrameworkElement TargetRight)
        {
            ModAnimation.AniStop("FrmMain LeftChange");
            ModAnimation.AniStop("PageLeft PageChange"); // 停止左边栏变更导致的右页面切换动画，防止它与本动画一起触发多次 PageOnEnter
            ModAnimation.AniControlEnabled += 1;
            // 清除新页面关联性
            if (TargetLeft.Parent is not null)
                TargetLeft.SetValue(ContentPresenter.ContentProperty, null);
            if (TargetRight is not null && TargetRight.Parent is not null)
                TargetRight.SetValue(ContentPresenter.ContentProperty, null);
            PageLeft = (MyPageLeft)TargetLeft;
            PageRight = (MyPageRight)TargetRight;
            // 触发页面通用动画
            ((MyPageLeft)this.PanMainLeft.Child).TriggerHideAnimation();
            ((MyPageRight)this.PanMainRight.Child).PageOnExit();
            ModAnimation.AniControlEnabled -= 1;
            // 执行动画
            ModAnimation.AniStart(new[] {
                        ModAnimation.AaCode(() =>
                {
                ModAnimation.AniControlEnabled += 1;
                // 把新页面添加进容器
                this.PanMainLeft.Child = PageLeft;
                PageLeft.Opacity = 0d;
                this.PanMainLeft.Background = (Brush)null;
                ModAnimation.AniControlEnabled -= 1;
                ModBase.RunInUi(() => this.PanMainLeft_Resize(this.PanMainLeft.ActualWidth), true);
            }, 110),
                        ModAnimation.AaCode(() =>
                {
                // 延迟触发页面通用动画，以使得在 Loaded 事件中加载的控件得以处理
                PageLeft.Opacity = 1d;
                PageLeft.TriggerShowAnimation();
            }, 30, true)
        }, "FrmMain PageChangeLeft");
            ModAnimation.AniStart(new[] {
                        ModAnimation.AaCode(() =>
                {
                ModAnimation.AniControlEnabled += 1;
                ((MyPageRight)this.PanMainRight.Child).PageOnForceExit();
                // 把新页面添加进容器
                this.PanMainRight.Child = PageRight;
                PageRight.Opacity = 0d;
                this.PanMainRight.Background = (Brush)null;
                ModAnimation.AniControlEnabled -= 1;
                ModBase.RunInUi(() => this.BtnExtraBack.ShowRefresh(), true);
            }, 110),
                        ModAnimation.AaCode(() =>
                {
                // 延迟触发页面通用动画，以使得在 Loaded 事件中加载的控件得以处理
                PageRight.Opacity = 1d;
                PageRight.PageOnEnter();
            }, 30, true)
        }, "FrmMain PageChangeRight");
        }
        /// <summary>
    /// 退出子界面。
    /// </summary>
        private void PageChangeExit()
        {
            if (PageStack.Any())
            {
                // 子页面 → 主页面，退出
                this.PanTitleMain.Visibility = Visibility.Visible;
                this.PanTitleMain.IsHitTestVisible = true;
                this.PanTitleInner.IsHitTestVisible = false;
                ModAnimation.AniStart(new[] { ModAnimation.AaOpacity(this.PanTitleInner, -this.PanTitleInner.Opacity, 150), ModAnimation.AaX(this.PanTitleInner, (double)-18 - this.PanTitleInner.Margin.Left, 150, Ease: new ModAnimation.AniEaseInFluent()), ModAnimation.AaOpacity(this.PanTitleMain, 1d - this.PanTitleMain.Opacity, 150, 200), ModAnimation.AaX(this.PanTitleMain, -this.PanTitleMain.Margin.Left, 350, 200, new ModAnimation.AniEaseOutBack(ModAnimation.AniEasePower.Weak)), ModAnimation.AaCode(() => this.PanTitleInner.Visibility = Visibility.Collapsed, After: true) }, "FrmMain Titlebar FirstLayer");
                PageStack.Clear();
            }
            else
            {
                // 主页面 → 主页面，无事发生
            }
        }

        // 左边栏改变
        private void PanMainLeft_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (!e.WidthChanged)
                return;
            PanMainLeft_Resize(e.NewSize.Width);
        }
        private void PanMainLeft_Resize(double NewWidth)
        {
            double Delta = NewWidth - this.RectLeftBackground.Width;
            if (Math.Abs(Delta) > 0.1d && ModAnimation.AniControlEnabled == 0)
            {
                if (this.PanMain.Opacity < 0.1d)
                    this.PanMainLeft.IsHitTestVisible = false; // 避免左边栏指向背景未能完美覆盖左边栏
                if (NewWidth > 0d)
                {
                    // 宽度足够，显示
                    ModAnimation.AniStart(new[] { ModAnimation.AaWidth(this.RectLeftBackground, NewWidth - this.RectLeftBackground.Width, 180, Ease: new ModAnimation.AniEaseOutFluent(ModAnimation.AniEasePower.ExtraStrong)), ModAnimation.AaOpacity(this.RectLeftShadow, 1d - this.RectLeftShadow.Opacity, 180), ModAnimation.AaCode(() => this.PanMainLeft.IsHitTestVisible = true, 150) }, "FrmMain LeftChange", true);
                }
                else
                {
                    // 宽度不足，隐藏
                    ModAnimation.AniStart(new[] { ModAnimation.AaWidth(this.RectLeftBackground, -this.RectLeftBackground.Width, 180, Ease: new ModAnimation.AniEaseOutFluent()), ModAnimation.AaOpacity(this.RectLeftShadow, -this.RectLeftShadow.Opacity, 180), ModAnimation.AaCode(() => this.PanMainLeft.IsHitTestVisible = true, 150) }, "FrmMain LeftChange", true);
                }
            }
            else
            {
                this.RectLeftBackground.Width = NewWidth;
                this.PanMainLeft.IsHitTestVisible = true;
                ModAnimation.AniStop("FrmMain LeftChange");
            }
        }

        #endregion

        #region 控件拖动

        // 在时钟中调用，使得即使鼠标在窗口外松开，也可以释放控件
        public void DragTick()
        {
            if (ModMain.DragControl is null)
                return;
            if (!(Mouse.LeftButton == MouseButtonState.Pressed))
            {
                DragStop();
            }
        }
        // 在鼠标移动时调用，以改变 Slider 位置
        public void DragDoing()
        {
            if (ModMain.DragControl is null)
                return;
            if (Mouse.LeftButton == MouseButtonState.Pressed)
            {
                ((dynamic)ModMain.DragControl).DragDoing();
            }
            else
            {
                DragStop();
            }
        }
        public void DragStop()
        {
            // 存在其他线程调用的可能性，因此需要确保在 UI 线程运行
            ModBase.RunInUi(() =>
                {
                    if (ModMain.DragControl is null)
                        return;
                    var Control = ModMain.DragControl;
                    ModMain.DragControl = null;
                    ((dynamic)Control).DragStop(); // 控件会在该事件中判断 DragControl，所以得放在后面
                });
        }

        #endregion

        #region 附加按钮

        // 音乐
        // 签名须为 MouseButtonEventArgs：MyExtraButton 的自定义委托是 ClickEventHandler(object, MouseButtonEventArgs)，
        // VB 原版用 Handles 宽松绑定，C# 必须精确匹配（2026-11-11 修正）。
        private void BtnExtraMusic_Click(object sender, MouseButtonEventArgs e)
        {
            ModMusic.MusicControlPause();
        }
        private void BtnExtraMusic_RightClick(object sender, MouseButtonEventArgs e)
        {
            ModMusic.MusicControlNext();
        }

        // 任务管理
        private void BtnExtraDownload_Click(object sender, MouseButtonEventArgs e)
        {
            PageChange((PageStackData)PageType.TaskManager);
        }
        private bool BtnExtraDownload_ShowCheck()
        {
            return ModNet.HasDownloadingTask() && !(PageCurrent == (PageStackData)PageType.TaskManager);
        }

        // 投降
        public void AprilGiveup()
        {
            if (ModMain.IsAprilEnabled && !ModMain.IsAprilGiveup)
            {
                ModMain.Hint("=D", ModMain.HintType.Green);
                ModMain.IsAprilGiveup = true;
                ModMain.FrmLaunchLeft.AprilScaleTrans.ScaleX = 1d;
                ModMain.FrmLaunchLeft.AprilScaleTrans.ScaleY = 1d;
                this.BtnExtraApril.ShowRefresh();
            }
        }
        public bool BtnExtraApril_ShowCheck()
        {
            return ModMain.IsAprilEnabled && !ModMain.IsAprilGiveup && PageCurrent == (PageStackData)PageType.Launch;
        }

        // 关闭 Minecraft
        public void BtnExtraShutdown_Click()
        {
            try
            {
                if (ModLaunch.McLaunchLoaderReal is not null)
                    ModLaunch.McLaunchLoaderReal.Cancel();
                foreach (var Watcher in ModWatcher.McWatcherList)
                    Watcher.Kill();
                ModMain.Hint("已关闭运行中的 Minecraft！", ModMain.HintType.Green);
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "强制关闭所有 Minecraft 失败");
            }
        }
        public bool BtnExtraShutdown_ShowCheck()
        {
            return ModWatcher.HasRunningMinecraft;
        }

        /// <summary>
    /// 返回顶部。
    /// </summary>
        public void BackToTop()
        {
            var RealScroll = BtnExtraBack_GetRealChild();
            if (RealScroll is not null)
            {
                RealScroll.PerformVerticalOffsetDelta(-RealScroll.VerticalOffset);
            }
            else
            {
                Logger.Error("无法返回顶部，未找到合适的 RealScroll", LogBehavior.Toast);
            }
        }
        private bool BtnExtraBack_ShowCheck()
        {
            var RealScroll = BtnExtraBack_GetRealChild();
            return RealScroll is not null && RealScroll.Visibility == Visibility.Visible && RealScroll.VerticalOffset > this.Height + (double)(this.BtnExtraBack.Show ? 0 : 700);
        }
        private MyScrollViewer BtnExtraBack_GetRealChild()
        {
            if (this.PanMainRight.Child is null || !(this.PanMainRight.Child is MyPageRight))
                return null;
            MyPageRight Page = (MyPageRight)this.PanMainRight.Child;
            return (MyScrollViewer)Page.FindName(Page.PanScroll);
        }

        #endregion

        // 愚人节鼠标位置
        public MouseEventArgs lastMouseArg = null;
        private void FormMain_MouseMove(object sender, MouseEventArgs e)
        {
            lastMouseArg = e;
        }

    }
}