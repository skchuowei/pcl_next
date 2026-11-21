# AI_INDEX — pcl-next / PCL_NEXT_Windows

> 面向 AI 的项目索引，**不是人类 README**。读本文可拿到约 90% 的项目上下文；
> 剩余细节按 §8 的索引按需打开，不要盲目全仓扫描。
> 事实快照：2026-11-21（Windows 线）。规则文档与本文冲突时，以 `AGENTS.md` + `分支与发布规范.md` 为准；
> 现状（分支/未提交改动）以 `git status` 现场为准。

---

## 0. 十二条硬事实

1. **身份**：`pcl-next`（内部代号 NeoPCL），是 Meloong 的 PCL2 的第三方 fork，不是 PCL2 本人分支；对外名不得含「PCL 手机版 / PCL Mobile」。
2. **三仓库架构**：`PCL_NEXT_Windows` / `PCL_NEXT_Linux` / `PCL_NEXT_Android` 是三个**独立 git 仓库**，共用代号阶梯（dev→slush→alpha→beta→rc），**互不合并**，平台只作分支/制品后缀。
3. **技术栈**：Windows 线 = .NET 10（`net10.0-windows`）+ WPF + WinForms + C# 13；VB.NET 文件已清零（`.vb`=0，`.vbproj` 已移除），但代码语义仍带 VB 残留（`dynamic` / `Microsoft.VisualBasic` 兼容层）。
4. **唯一 GUI 入口**：`pcl-next/pcl-next.csproj`（`WinExe`，`StartupObject=PCL.PclEntryPoint`）。其余工程都是库或 CLI。
5. **版本**：`VersionBaseName = 2.100.0.7`，`VersionCode = 5007`（dev 号段 5000–5009），定义在 `pcl-next/Modules/Base/ModBase.cs`，**只能由 `scripts/Set-VersionInfo.ps1` 写入，禁止手改**。
6. **当前分支**：`dev-Windows`（功能开发线，**禁止发 GitHub Release**）；发布构建在 worktree `../PCL_NEXT_Windows_slush`（快照时检出于 `beta-Windows`）。代号是否推进以用户最新指示为准，不要自行推断。
7. **构建入口**：`scripts/Build.ps1`。产物落 `pcl-next/bin-net10/<代号>/<版本名>-<构建号>/<RID>/`（三段树）。**必须带 `-r win-x64|win-arm64`**，`PclRidGuard` 会在缺 RID 时直接报错。
8. **产物两棵树**：测试版只进 `bin-net10/`，发布存档只进 `ReleaseArtifacts/<代号>/<版本名>/<RID>/`；`bin/`、`bin-net10/` 根是错误落点，`pcl-next/publish/` 只是 pubxml 的中间落点（不能当存档，见 §5）。
9. **路径单真源**：工程文件里禁止手写 `bin-net10\...` 字面量，一律用 `Directory.Build.props` 的 `$(PclGuiTreeRoot)` / `$(PclTreeRoot)`；`PclTreePathGuard` 机械校验。
10. **远端**：只配置了 `fork`（`https://github.com/skchuowei/pcl_next.git`）。上游 `origin` 严禁 push。
11. **配色锁**：未经用户明确同意，禁止改任何 `ColorBrush*`、侧栏/卡片/标题栏底色、透明度、毛玻璃；侧栏底色已与卡片统一（`ColorBrushBackgroundTransparentSidebar` ← `ColorBrushCard`，注入点在 `Modules/ModSecret.cs` 的 `ThemeRefresh`）。
12. **合规前置**：非 Windows 制品（Linux/Android）在上游书面答复留存且 `PLATFORM_RELEASE_ENABLED` 开启前，不得对外分发；CI 编译内核不算分发。

---

## 1. 目录地图（一层；★常用 ⛔禁扫）

| 路径 | 作用 | 何时进入 |
|---|---|---|
| `pcl-next/` ★ | WPF GUI 壳（唯一可运行前端）。`Modules/` 业务、`Pages/` 页面、`Controls/` 自绘控件、`Resources/` 内嵌资源、`My Project/` 程序集信息 | 改界面/业务逻辑 |
| `PCL.Download/` ★ | 下载与安装**内核**（无 GUI，46 个 .cs）：引擎、安装计划、加载器安装器、版本解析、资源搜索、镜像源 | 改下载/安装/资源检索 |
| `PCL.Download.Tests/` ★ | 上述内核的 xUnit 单测（35 个 .cs，含假 HTTP 服务与假资源 API） | 改内核后必跑 |
| `PCL.Cli/` | CLI 前端（`net10.0`，无 GUI）：`download / install / install-loader / link / resource / selftest` 六个命令 | 无头验证、联机隧道 |
| `PCL.Core/` | GUI 与 CLI 共用的小内核：下载策略解析 + 联机准入网关/会话/脚手架（`PclIsGuiFree=true`，零第三方包） | 改策略、改联机协议 |
| `PCLCS/` | 上游 PCL2 的 C# 辅助库：Java、版本安装清单、MC 工具、Wiki 资源、启动补丁（含 2 个 jar） | 改启动参数/版本安装兼容 |
| `MeloongCore/` | 独立通用库：`Shared`（日志/配置/进度/Worker/Utils）、`Wpf`（对话框/转换器/WPF 工具）、`Tests`。**有自己的 AGENTS.md 与 .agents/ 架构文档** | 改通用工具层 |
| `docs/` ★ | `plans/`（28 份专项计划）、`bugscan/`（27 份模块审计）、`经验与踩坑.md`、`扩展规范.md`、`合规/`、`examples/ext-mixin-demo/` | 动手前查同类先例 |
| `ReleaseNotes/` | 每版发布说明（六章结构），最新 `v2.100.0.7-slush.1-Windows.md` | 写发布说明时照格式 |
| `ReleaseArtifacts/` | 发布存档：`Dev/2.100.0.6/win-x64`（exe + 文档 + 若干原生 DLL）、`Slush/2.100.0.7/win-x64`（`pcl-next.exe` + 文档 + `Log1/2.txt`、`Setup.ini`）。**只读，禁止清理** | 查历史发布件 |
| `bin-net10/` ⛔ | 本地验证构建产物，按 `Dev/2.100.0.7-1.xx/<RID>/` 堆叠 | 只取最新目录，勿全扫 |
| `patches/` | 上游 PCL2 Release 2.12.x–2.14.x 的补丁序列（15 份），用于追溯上游改动 | 查上游差异 |
| `scripts/` ★ | `Build.ps1`、`Set-VersionInfo.ps1`、`Get-BuildSmallVersion.ps1`、`Install-GitHooks.ps1`、`Copy-VersionedArtifact.ps1`、`Test-VersionMatrix.ps1`、`Convert-VbToCSharp.ps1`、两个 Python 修补脚本 | 构建/发版/钩子 |
| `.github/workflows/` | `build.yml`（Windows 构建+单测+双 RID publish）、`kernel-guard.yml`（Linux 内核门禁）、`release.yml`（`v*` / `linux/v*` / `android/v*` 触发） | 改 CI/发版 |
| 根文档 | `AGENTS.md`（规则总入口）、`分支与发布规范.md`（113 KB，分支/版本/tag 唯一真源）、`公布标准.md`（发版门槛）、`PCL2上游遗留问题清单.md`、市场调研与迁移计划两份 | 开工必读前两份 |

**⛔ 禁止扫描/检索**：`.git/`、`obj/`、`bin/`、`bin-net10/`、`ReleaseArtifacts/`、`pcl-next/publish/`、`MeloongCore/*/obj|bin/`、`docs/examples/*/obj|bin/`。这些目录占全仓 90% 以上文件量，且全是构建产物。

---

## 2. 程序集依赖

```text
pcl-next (WinExe, net10.0-windows, WPF)
├─ MeloongCore.Shared        (net10.0, 通用库)
├─ MeloongCore.Wpf           (WPF 适配层)
├─ PCL.Core                  (无 GUI: 策略 + 联机)
├─ PCLCS                     (C# MC 辅助库)
└─ PCL.Download              (无 GUI 下载/安装内核)

PCL.Cli (net10.0, 无 GUI)
├─ PCL.Download
└─ PCL.Core

MeloongCore.Wpf → MeloongCore.Shared
PCL.Download.Tests → PCL.Download（xUnit）
MeloongCore.Tests → Shared + Wpf（TUnit 1.43，`net10.0-windows`）
```

`pcl-next.sln` 只含六个工程（pcl-next / PCLCS / MeloongCore / MeloongCore.Wpf / PCL.Cli / PCL.Core）；
`PCL.Download` 与测试工程**不在 sln**，由脚本与 CI 直接编 csproj（避免 sln 级 RID 限制）。

---

## 3. 代码地图

### 3.1 `pcl-next/`（GUI 壳，166 个 .cs）

| 位置 | 内容 | 关键文件 |
|---|---|---|
| 根 | 入口与主窗体 | `PclEntryPoint.cs`（`Main`→`Application.Run`）、`Application.xaml`（`StartupUri=FormMain.xaml`，全部 `ColorBrush*` 资源）、`FormMain.xaml(.cs)`（主窗体、左右 ContentPresenter）、`PclXmlImports.cs`、`GlobalSuppressions.cs` |
| `Modules/`（6） | 全局模块门面 | `ModMain`（提示/Hint、UI 线程、事件）、`ModSecret`（主题刷新与密钥）、`ModMusic`、`ModEvent`、`ModDevelop`、`ModDevNotes`（内置开发日志/阶段提示） |
| `Modules/Base/`（14） | 基础设施 | `ModBase`（版本常量、路径、线程/网站/时间）、`ModLoader`（**LoaderBase/LoaderTask/LoaderCombo/LoaderWorker** 异步任务框架）、`ModNet`、`NetInterop`、`ModValidate`、`ModGitHub`（自动更新）、`ModAnimation`、`MyBitmap`、`MyCompat`（VB `My.*` 兼容）、`PclLogger`、`ResilientCacheStore`、`ModBuildChannel`、`ModBuildInfo`、`ModDownloadPolicy` |
| `Modules/Minecraft/`（9） | 实例与游戏 | `ModMinecraft`（实例模型/加载）、`ModLaunch`（启动参数与进程）、`ModDownload`（下载页业务）、`ModModpack`（整合包）、`ModBackup`（备份还原）、`ModCrash`（崩溃分析）、`ModWatcher`（进程监视）、`ModJava`、`ModAccountList`（账号） |
| `Modules/Link/`（17） | 联机大厅 | `ModLinkSession`（会话）、`ModLinkEasyTier`（EasyTier 进程）、`ModLinkRuntime`、`ModLinkProbe`、`ModLinkCodec`、`ModLinkConfig`、`ModLinkInterop`、`ModLinkResolve`、`ModLinkScaffold`、`ModLinkSync`、`ModLinkTransfer`，以及 `ModLinkCodec/Probe/Resolve/Sync/Transfer/SelfTest` 六个自测文件 |
| `Modules/Ext/`（7） | 扩展系统 | `ExtManifest`（清单校验）、`ExtStore`（安装/索引）、`ExtRuntime`（加载）、`MixinHost`（L1 Harmony 补丁，默认关）、`ExtMarket`/`ExtMarketInstall`/`ExtMarketSelfTest`（市场） |
| `Modules/Resource/`（7） | 资源页业务 | `ResourceProviderSearch`、`ResourceSearcher`、`ResourceProject`、`ResourceVersion`、`ResourceSourceGate`、`LocalResourceFile`、`LocalResourceLoaders` |
| `Modules/Download/`（1） | 下载适配 | `LoaderPlanAdapter`（GUI → `PCL.Download` 安装计划） |
| `Modules/ThirdParty/`（1） | 拖拽 | `DragHelper` |
| `Pages/`（54 XAML + 58 .cs） | 页面（.xaml + code-behind） | `PageLaunchLeft/Right`（启动主页）、`PageDownload*`（下载/资源/扩展市场）、`PageInstance*`（实例总览/Mod/备份/导出/设置）、`PageLink/`（联机）、`PageSetup*`（12 个设置页 + `Settings.cs`/`SettingService.cs`）、`PageOther*`（关于/帮助/测试）、`PageSelect*`、`PageSpeed*` |
| `Controls/`（36） | 自绘控件 | `MyButton`、`MyCard`、`MyTextBox`、`MyComboBox`、`MyListItem`、`MyScrollViewer/Bar`、`MyLoading`、`MyHint`、`MyMsg/`（Text/Input/Select/Progress 弹窗）、`Behaviors/`（ClipboardInterceptor、LazyLoad）、`LucideIcon.cs` |
| `Resources/` | 内嵌资源 | `Custom.xaml`（样式模板）、`Font.ttf`、`Help.zip`、`forge-installer.jar`、`transformer.jar`、`libwebp64.dll` |
| `Images/` | 图标 | `icon.ico` |
| `My Project/` | 程序集信息 | `AssemblyInfo.cs`（含 `SupportedOSPlatform("windows7.0")`）、`app.manifest`、`Resources.resx` |

### 3.2 `PCL.Download/`（内核，46 个 .cs）

| 分组 | 文件 |
|---|---|
| 下载引擎 | `DownloadEngine`、`DownloadFileTask`、`IDownloadEngine`、`DownloadLog`、`SourceRace`（源竞速）、`ModMirror`（镜像）、`HashHelper`、`HashType` |
| 安装计划 | `InstallPlan`、`InstallTaskDescriptor`、`InstallDownloadPlan`、`InstallCommitPlan`、`InstallLayout`、`InstallerArguments` |
| 安装器 | `ForgelikeInstaller`、`ForgeRecommended`、`OptiFineInstaller`、`FabricProfile`、`LiteLoaderProfile`、`OptiFineProfile`、`JavaRequirement` |
| 版本 | `McVersionManifest`、`McVersionUtil`、`VersionMerger`、`Models` |
| 加载器列表 | `LoaderListClient`、`LoaderListModels`、`LoaderListParsers` |
| 资源检索 | `IResourceApi`、`HttpResourceApi`、`ResourceProvider`、`ResourceProviderRegistry`、`CurseForgeProvider`、`ModrinthProvider`、`ResourceProject`、`ResourceVersion`、`ResourceModels`、`ResourceJson`、`ResourceSearcher`、`ResourceSourceResolver`、`ResourceSigning`、`LocalResourceFile`、`LocalResourceScanner`、`LocalResourceOnlineLoader`、`FuzzySearch`、`WikiEntry` |

### 3.3 其余

- `PCL.Cli/`：`Program.cs` + `CliArguments/CliSession/CliPolicy/CliOutput/CliExitCode`；`Commands/` 六个命令；`LoaderResolver`、`ForgeNeoResolver`、`McVersionResolver`、`McSource`、`MavenCoord`、`LoaderProfile`。
- `PCL.Core/`：`PclDownloadPolicy.cs`（环境变量 > `DownloadPolicy.ini` > 内置默认，`AllowMcDownload` 默认 true）；`Link/`：`PclLinkGate`（房主侧挑战-应答网关，nonce+HMAC，ticket 12h）、`PclLinkSession`、`PclLinkCode`、`PclLinkEasyTier`、`PclLinkScaffoldClient`。
- `PCLCS/`：`Java.cs`、`Configs.cs`、`Constants.cs`（`ResourceTypes`/`ResourcePlatforms`/`ModLoaders` 枚举）、`Minecraft/McVersion.cs`、`MCUtils.cs`、`McInstallManifest.cs`、`OptifineHashes.json`、`Launch/LaunchUtils.cs`、`Resource/Resource.cs`、`WikiEntries.txt`。
- `MeloongCore/`：`Shared/General`（Config/Logger/Progress/ConcurrentList/OneOf/ValueRange/MsDelta/Worker）、`Shared/Utils`（File/Directory/Path/Task/String/Math/Cryptography/Registry/Web）、`Shared/Extensions`（Enumerable/String/Task/Web/Exception）、`Wpf/General`（Dialogs/ValueConverters）、`Wpf/Utils/WindowsUtils.cs`。语义细节见 `MeloongCore/.agents/Shared/架构分析.md`（必读于改通用层）。

---

## 4. 关键链路

**启动**：`PclEntryPoint.Main` → `Application.InitializeComponent()`（加载全部资源）→ `FormMain.xaml` → `FormMain()` 构造里建 `PageLaunchLeft/Right` 并挂到 `PanMainLeft/Right` → `ModMain` 初始化提示与事件 → 主题由 `ModSecret.ThemeRefresh` 注入 → `ModLoader` 跑加载任务链（`LoaderCombo`）→ `ModMinecraft` 读实例列表。

**下载/安装**：页面 `PageDownload*` → `Modules/ModDownload` → `LoaderPlanAdapter` → `PCL.Download` 生成 `InstallPlan`（描述为 `InstallTaskDescriptor` 序列）→ `InstallDownloadPlan`（算文件/校验）→ `DownloadEngine`（多源竞速、镜像、重试、进度）→ `InstallCommitPlan`（落盘/校验/回滚）→ `InstallLayout` 决定目录布局。

**启动游戏**：`ModLaunch` 组装 JVM/游戏参数 → `PCLCS`（Java 探测、版本清单、补丁释放）→ 启动进程 → `ModWatcher` 监视 → 崩溃走 `ModCrash`。

**联机**：`PageLink` → `ModLinkSession` → 大厅协议（`ModLinkCodec`/`PclLinkCode`）→ `ModLinkEasyTier` 起 overlay → `PclLinkGate` 只开放 gate 端口，ticket+HMAC 准入后转发到本机 MC 端口；`PCL.Cli link` 可无头跑隧道。

**扩展**：`ExtManifest` 校验 `plugin.json` → L0 只读数据（镜像/参数模板/外部工具），L1 需 `entry`+`mixin.patch` 且包内禁止自带 `0Harmony.dll`，由 `MixinHost` 加载，默认关闭，`--safe-ext` 可整场绕过。规范见 `docs/扩展规范.md`。

---

## 5. 构建 / 发布 / CI

| 目标 | 命令 | 落点 |
|---|---|---|
| 本地验证（多文件） | `.\scripts\Build.ps1 -Channel Dev -Rid win-x64`（可 `-Clean`、`-WithCli`） | `pcl-next/bin-net10/Dev/2.100.0.7-<build>/win-x64/`；CLI 在 `.../cli/pclcli.exe` |
| 直接编（等价） | `dotnet build pcl-next/pcl-next.csproj -c Release -r win-x64 -p:PclChannel=Dev` | 同上（由 `$(PclGuiTreeRoot)` 决定） |
| 内核单测 | `dotnet test PCL.Download.Tests/PCL.Download.Tests.csproj -c Release -r win-x64` | 临时 |
| 发布（单文件） | `dotnet publish pcl-next/pcl-next.csproj -c Release -r win-x64 -p:PublishProfile=win-x64-single`（arm64 用 `win-arm64-single`） | **实际落** `pcl-next/publish/win-x64/<代号>/pcl-next.exe`；按 §0.6 再拷入 `ReleaseArtifacts/<代号>/<版本>/<RID>/` 并附 `更新日志.md`/`修复bug.md` |
| 构建号 | `.\scripts\Get-BuildSmallVersion.ps1`；`post-commit` 钩子自动刷新 | `pcl-next/obj/pcl-build.small` |

- **构建号**：自锚点 `v2.100.0.1-dev.1` 起按提交数 ×0.01 单调递增；本机每次编译再 +0.01（`PclBuildSeqBump`），发布构建自动关闭递增；目录名必须带构建号，否则同版本多次构建互相覆盖。
- **发布路径现状（重要）**：两个 `.pubxml` 的 `PublishDir` 仍是 `publish\<rid>\$(PclChannel)\`，与 `AGENTS.md` §0.6.4（必须指向 `ReleaseArtifacts`）**不一致**；CI 也按该路径上传 artifact。按规则归档时需二次搬运，不要以为 publish 已落在 `ReleaseArtifacts`。
- **CI 触发分支名仍是旧命名**：`build.yml` 与 `kernel-guard.yml` 监听 `dev/slush/alpha/beta/pc/snapshot/android/linux`，**不监听 `dev-Windows`**；推当前开发分支不会触发 CI。
- **发布门槛**：`公布标准.md` 七项 AND 全过，才可打 annotated tag（`v<版本>-<代号>.<序号>-Windows`）、归档（exe + `更新日志.md` + `修复bug.md`，`DownloadPolicy.ini` 可选）、推 fork、由 `release.yml` 建 prerelease。
- **CI**：`build.yml` 矩阵只有 Debug/Release 两档（Release 档跑内核单测 + win-x64/win-arm64 两次 publish）；`kernel-guard.yml` 只编无 GUI 内核并跑 `selftest-offline`；`release.yml` 由 tag 触发，平台 tag 被显式排除在 Windows 主流程外。

---

## 6. 禁止事项（违反 = 返工/驳回）

1. 手改 `ModBase.cs` 的 `VersionBaseName` / `VersionCode`、`AssemblyInfo.cs` 版本。
2. 工程文件手写 `bin-net10\...`；用 `-o` / `-p:OutputPath` 绕开三段树；把 `bin/` 当产物目录，或把 `publish/` 当发布存档。
3. 不带 `-r` 构建 GUI（`PclRidGuard` 报错）；对 `.sln` 传 `-r`（NETSDK1134）。
4. 把测试件放进 `ReleaseArtifacts`，或清理 `ReleaseArtifacts` 旧版本。
5. 向任何上游远端 push（本仓只配 `fork`，`origin` 严禁 push）；或把 dev 版本发 GitHub Release。
6. 跨平台整分支合并、把平台专属代码带回 Windows 线。
7. 未经同意改配色/视觉风格（§0 第 11 条）。
8. `git add -A` 全量提交；提交信息缺末尾 `Build: <M.mm>`；把 `bin*`、`PCL/` 运行数据纳入版本。
9. 动 `_Archive/*.zip`（工作区只读归档，另有 `归档与Git仓库管理规则.md` 管）。
10. 改用户测试环境 `../PCL_next游戏测试目录/`。

---

## 7. 任务 → 文件路由

| 任务 | 先读 | 改哪里 | 验证 |
|---|---|---|---|
| 改下载源/搜索 | `docs/plans/计划-Phase4主体-下载数据源C化.md` | `PCL.Download/Resource*`、`PCL.Download/*Provider` | 对应 `PCL.Download.Tests/*Tests` |
| 改安装流程/加载器 | `plan-Phase4首步`、`ForgelikeInstaller` 注释 | `PCL.Download/Install*`、`ForgelikeInstaller`、`OptiFineInstaller` | `Install*Tests`、`ForgelikeInstallerTests` |
| 改 GUI 页面 | 对应 `Pages/*` 与 `docs/bugscan/J*.md` | `pcl-next/Pages/*` | 本地构建 + 冒烟（无自动化 UI 测试） |
| 改控件/动画 | `docs/plans/计划-WindowsUI动画优化.md`、`docs/bugscan/F*.md` | `pcl-next/Controls/*`、`ModAnimation` | 目视 + 编译 |
| 改联机 | `docs/plans/计划-联机大厅移植.md`、`计划-CLI大厅协议与准入网关.md` | `Modules/Link/*`、`PCL.Core/Link/*`、`PCL.Cli/Commands/LinkCommand.cs` | `ModLinkSelfTest`、`PCL.Cli selftest` |
| 加/改扩展 | `docs/扩展规范.md`、`docs/plans/计划-扩展系统.md` | `Modules/Ext/*` | `ExtMarketSelfTest`、`docs/examples/ext-mixin-demo` |
| 改启动/版本 | `docs/bugscan/D1-启动链路.md`、`D2-版本实例.md` | `ModLaunch`、`ModMinecraft`、`PCLCS` | 真机启动 + `PCL/Log1.txt` 零 Error |
| 改通用工具 | `MeloongCore/.agents/仓库指示.md` + `Shared/架构分析.md` | `MeloongCore/*` | `MeloongCore/Tests` |
| 发版 | `公布标准.md` §5、`分支与发布规范.md` §5 | `ReleaseNotes/`、`scripts/Set-VersionInfo.ps1` | 七项门槛逐条核对 |

---

## 8. 文档索引（按优先级按需打开）

| 优先级 | 文件 | 用途 |
|---|---|---|
| P0 | `AGENTS.md` | 硬规则总入口（产物/提交/平台/配色） |
| P0 | `分支与发布规范.md` | 分支命名（§0E）、版本号/号段（§5）、发布说明六章（§5.4） |
| P1 | `公布标准.md` | dev 渠道发版门槛、措辞红线、操作清单 |
| P1 | `docs/经验与踩坑.md`（76 KB） | 30 条真实事故与修法，动手前扫标题 |
| P1 | `docs/扩展规范.md` | L0/L1 扩展清单与目录约定 |
| P1 | `PCL2上游遗留问题清单.md` | 未修复缺陷，避免重复踩坑 |
| P2 | `docs/plans/`（28 份） | 专项设计与阶段台账（按任务名检索） |
| P2 | `docs/bugscan/`（27 份） | 模块级审计：A/B/C/D/E/F/G/H/J 分域 |
| P2 | `docs/合规/跨平台授权问询.md` | L1–L5 义务、问询留痕 |
| P3 | `MC启动器市场现状与开源实现调研.md`、`PCL2_CSharp下载服务迁移计划.md` | 背景与规划（大，非必要不读） |
| P3 | `ReleaseNotes/*.md` | 历史发布说明（写新版时抄结构） |

---

## 9. 已知陷阱（Top 15，均有事故记录）

1. `.sln` 级 `dotnet build -r` 会报 NETSDK1134；必须对单个 csproj 传 `-r`。
2. 运行中的 `pcl-next.exe` 会锁住旧构建目录 DLL，导致下一次编译失败——先退程序。
3. 构建号缓存、git 钩子与 `build-number.base` 可能不同源，会写出错误编号；以 `pcl-build.small` 为准。
4. R2R 镜像与架构相关：x64/arm64 必须按 RID 隔离 `IntermediateOutputPath`，否则 arm64 会复用 x64 的 PE（Machine 0x8664）。
5. 单文件 publish 的产物不是 build 输出，不能拿 `bin-net10` 多文件目录直接压缩发布。
6. 产物迁移只拷内容（`源/* → 目标/`），拷文件夹本体会自嵌套。
7. `ModLoader` 的 `LoaderTask` 负载必须协作取消；`WaitIfRunning*` 不重抛负载错误。
8. `ConfigUtils` JSON 写盘有 500 ms 防抖；要落盘用 `SaveAll()`；对象缓存需 `Edit/Set` 才安排保存。
9. `FileUtils.ReadString` 不关闭流；编码回退顺序 BOM → UTF-8 → GB18030。
10. UI 线程/后台线程混用会让 `RunInThread` 内联挂死任务线程（见 2026-11-15 ⑨）。
11. `MyListItem` 只用 `IsMouseDirectlyOver` 会吞整行点击（见 ⑫）。
12. 端口 10048 多半是残留 `easytier-core` 进程，不只是端口分配问题（见 ⑳/㉑）。
13. `build.yml` 分支白名单是老命名（`dev` 等），`dev-Windows` 的推送不跑 CI；改 CI 前先核对 `分支与发布规范.md` §0E。
14. `.pubxml` 的 `PublishDir=publish\<rid>\<代号>\` 与「发布只进 `ReleaseArtifacts`」冲突；归档必须手动搬运，不能把 `publish/` 当存档。
15. 现有归档不纯：`Dev/2.100.0.6/win-x64` 混入 `D3DCompiler_47_cor3.dll` 等原生库，`Slush/2.100.0.7/win-x64` 混入 `Log1/2.txt`、`Setup.ini`，与 §0.6.3「只放 exe + 更新日志 + 修复bug（+可选 ini）」不符；清理时不得删旧档。

---

## 10. AI 工作流（默认 7 步）

1. `git -C . status -sb` + `git log -3`：确认分支、未提交改动、最近提交；**非本轮改动不碰**。
2. 读 `AGENTS.md` 相关章节 + 本文件 §7 路由到的计划/审计文档。
3. 定位代码：先按 §3 的模块表找文件，再 `Grep` 类名；不要递归扫 `obj/bin`。
4. 改动最小化；涉及产物路径、版本常量、配色、分支时先对照 §6。
5. 构建验证：GUI 用 `scripts/Build.ps1`；内核用 `dotnet test PCL.Download.Tests -r win-x64`。
6. 清理现场：只保留三段树产物；删临时脚本；`git status` 复核。
7. 提交：只 `git add` 本轮文件，`type(scope): 说明` + 末尾 `Build: <M.mm>`；推 `fork`；把新踩坑追加到 `docs/经验与踩坑.md`。

---

## 11. 现场状态快照（2026-11-21，仅供起点，使用前复核）

- 分支：`dev-Windows` @ `71f9275`；worktree `../PCL_NEXT_Windows_slush` @ `c0ee4ce`（`beta-Windows`，VersionCode 5061，beta.2 晋级提交，本地 tag 未建）。
- 已打 tag：`v2.100.0.6-dev.6/7-Windows`、`v2.100.0.7-{slush,alpha,beta}.1-Windows`。
- 工作区**有大量未提交改动**（`.github/workflows/*`、`PCL.Download/Install*`、`pcl-next` 多个 Modules/Pages、若干 docs）。
- 本文已入库：`docs: 新增面向 AI 的仓库索引 AI_INDEX.md` = `51a6130`（Build 2.43）；`.gitignore` 排除项 = `fd94b97`（Build 2.44）。
- 构建树最新为 `pcl-next/bin-net10/Dev/2.100.0.7-2.40/` 与 `Slush/2.100.0.7-1.01/`；归档有 `Dev/2.100.0.6`、`Slush/2.100.0.7`（夹带 DLL/日志，见 §9 第 15 条）。
- 规则文档 §0E.5 记录的「当前代号」为 `alpha`（2026-11-13），现场已出现 beta tag；**代号以用户指示为准**。
