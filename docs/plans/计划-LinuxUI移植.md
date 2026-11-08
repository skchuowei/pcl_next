# 计划：Linux 端 UI 移植（WPF/VB → Avalonia）

> 依据：《AGENTS.md》§3.1 / §3.2（大改动先写专项计划）、§零（产物三段树与真实 RID）、
> §十（三仓库架构：互不合并、各自发版）；《分支与发布规范.md》§0B / §0C；
> 《计划-安卓UI移植.md》（同型移植的先行经验：Avalonia 定稿、控件映射、P0 接线、真机踩坑）；
> 《计划-Linux内核L1-平台能力层.md》（内核平台能力层边界）。
>
> 状态：**执行中（2026-11-08）**。本仓库 `PCL_NEXT_Linux`，分支 `dev.6-Linux`。
> LU1（宿主骨架）已落地并按 `linux-x64` 编译验证；LU2 起依赖 §11 决策项 D1 拍板。
> 本文件为专项计划，移植完成后随版本归档，不作为长期规则入口。

---

## 0. 决定记录（先定方向，再谈细节）

| 项 | 决定 | 依据 |
|---|---|---|
| 复用后端 | `PCL.Download`（`net10.0`、纯 BCL、零第三方）作为 Linux GUI 的唯一数据源，UI 不重抄下载/安装业务 | 内核工程已实测可 `linux-x64` 编译并跑 `selftest offline`（《计划-Linux内核L1》） |
| UI 框架 | **Avalonia**（不在本轮重新选型） | 《计划-安卓UI移植.md》§8.5.5 已定稿：XAML/样式/矢量 `Geometry` 与 WPF 概念一一对应，PCL 视觉签名近零成本复刻 |
| VB 处置 | `pcl-next` 的 122 个 `.vb` **不在 Linux 侧保留**：VB.NET 无跨平台编译器，且 TFM 为 `net10.0-windows` | 工程实测（`pcl-next.vbproj`: `net10.0-windows` + `UseWPF` + `UseWindowsForms`） |
| 视觉基准 | 直接取本仓库 `pcl-next/Application.xaml` 的 Lite 主题色值，不另造配色 | 该文件在本仓库内即为唯一基准（下表 §1.3） |
| 中立 UI 归属 | **待拍板（§11 D1）**；本轮**不**在 Linux 仓库自建共享 UI 库去抢这个位置 | 用户既有口径「Linux 线不要自建私有 UI 库」；三仓库架构下无 merge 通道 |

---

## 1. 现状结论（起点 / 已有资产 / 缺口）

### 1.1 起点：Linux 侧 GUI 当前**完全不可编译**

| 工程 | TFM / 平台属性 | Linux 可编译 | 说明 |
|---|---|---|---|
| `pcl-next`（VB 主程序） | `net10.0-windows` + `UseWPF` + `UseWindowsForms` | ❌ | 122 `.vb` + 63 `.xaml`；VB.NET 无跨平台编译器 |
| `PCLCS` | `net10.0-windows` | ❌ | 被 `pcl-next` 引用 |
| `MeloongCore/Wpf` | `net10.0-windows` + `UseWPF` | ❌ | 窗口/对话框/系统集成 |
| `MeloongCore/Shared` | `net10.0` | ⚠️ | 含 `RegistryUtils` / `MsDelta` / `PathUtils` 等 Windows 入口，需逐条筛 |
| `MeloongCore/Tests` | `net48` | ❌ | .NET Framework 独占 |
| `PCL.Download` | `net10.0` | ✅ | 纯 BCL，零第三方 |
| `PCL.Cli` | `net10.0`（`PclIsGuiFree=true`） | ✅ | 编排逻辑可被 UI 复用 |
| `PCL.Download.Tests` | `net10.0`（xUnit） | ✅ | 内核回归 |

**结论**：Linux 的 UI 不是「把 WPF 工程改一改」，而是**新建 C# + Avalonia 前端**，后端接已有内核。
这与安卓线阶段 B–D 的路径一致，区别只在宿主工程（桌面 X11 而非 Android Activity）。

### 1.2 已有资产

| 资产 | 位置 | 可直接用吗 |
|---|---|---|
| PCL 视觉签名（Lite 主题色值） | 本仓库 `pcl-next/Application.xaml`（`ColorBrush1..8`、`Gray1..8`、`RedLight`、`HalfWhite`、`Bg1`） | ✅ 作为配色基准，需人工搬到 Avalonia 资源字典 |
| 控件映射表（MyButton / MyCard / MyListItem / 渐变分隔线 / MyHint） | 《计划-安卓UI移植.md》§4.3 / §8.5.4 | ✅ 结论「全低成本 1:1 复刻」，本计划直接沿用 |
| Avalonia 原型与 P0 接线经验 | `PCL_NEXT_Android` 仓库 `PCL.UI` / `PCL.Desktop.Avalonia`（**未提交改动较多**） | ⚠️ **不可跨仓库拷**（§11 D1）；只作为**做法参考**，代码需在本仓库按需重新落地 |
| 内核数据通路 | `PCL.Download`（`McVersionManifestClient` / `DownloadEngine` / `ResourceApi`） | ✅ 已在安卓真机验证过同一套 API |

> 实测：本仓库全库 grep `Avalonia` / `PCL.UI` **仅命中 4 个 `.md` 文档，零代码、零工程**。
> 即 Linux 侧此前**没有**任何 UI 移植计划或半成品，本轮是从零起步。

### 1.3 Windows-only 能力清单（Linux 必须替换的部分）

静态扫描 `pcl-next/**/*.vb` 命中 **102 处 / 17 个文件**（关键词：`Microsoft.Win32`、`System.Windows.Forms`、
`DllImport`、`user32`、`kernel32`、`NotifyIcon`、`NAudio`、`System.Drawing`、`Imazen`、
`Environment.SpecialFolder`、`Process.Start` / `ProcessStartInfo`）：

| 类别 | 代表文件（命中数） | Linux 处置方向 |
|---|---|---|
| 位图 / WebP 解码 | `Modules/Base/MyBitmap.vb`(26) | Avalonia `Bitmap` + Skia；WebP 走 Skia 解码，不再用 `Imazen.WebP` |
| 音频（音乐/音效） | `Modules/ModMusic.vb`(16) | 不做：Linux 首版不移植背景音乐（见 §10） |
| `My.` 兼容垫层 | `Modules/Base/MyCompat.vb`(10) | 不移植：随 VB 一起退役 |
| 拖放 / 无边框窗口 | `Modules/ThirdParty/DragHelper.vb`(10)、`Controls/MyResizer.vb`(3) | Avalonia 窗口装饰 + `BeginMoveDrag`，或自绘标题栏 |
| 文件监视 | `Modules/Minecraft/ModWatcher.vb`(8) | `FileSystemWatcher`（BCL，跨平台），仅去掉 Windows 路径假设 |
| 注册表 / 系统路径 | `Modules/Base/ModBase.vb`(7)、`PageSetup/PageSetupLaunch.xaml.vb`(5) | 抽配置接口；Linux 落 XDG（`~/.config` / `~/.local/share`） |
| 字体 | `Application.xaml` 写死 `Resources/#PCL English, Microsoft YaHei UI` | 保留内嵌 `Resources/Font.ttf`；CJK 回退到系统 `Noto Sans CJK`（不内嵌大字体） |
| 系统集成 | 托盘 `NotifyIcon`、`System.Windows.Forms` 对话框、`ProcessStart` | 抽 `ISystemService` / `IDialogService`，Linux 实现走 `xdg-open` / `Process` |
| 框架本身 | WPF `System.Windows.*` 全套 | Avalonia 等价物（`Window` / `Control` / `Style` / `Geometry` / `LinearGradientBrush`） |

**判定口径**：上述每一项都不得在 Linux UI 代码里以「解释器分支 + P/Invoke」的形式硬塞；
平台差异只允许出现在**一处**平台服务层（§5.3），与内核 L1 已建立的
`PCL.Download/Platform/{PlatformInfo,CrossPlatformPath,PathComparison}` 同一原则。

### 1.4 缺口

| # | 缺口 | 影响 | 处置阶段 |
|---|---|---|---|
| G1 | Linux 仓库无任何 Avalonia 工程 / 主题 / 控件 | UI 无从起步 | LU1（宿主骨架）、LU2（主题与控件） |
| G2 | 无「中立 UI 库」归属裁决（三仓库互不合并） | LU2 具体落点无法确定 | §11 D1（**待拍板**） |
| G3 | 无 Linux 平台服务层（字体 / XDG / 托盘 / 外部进程 / 对话框） | P1 页面无法接线 | LU4 |
| G4 | 无 Linux 运行/显示环境验收通道（本机 Windows） | 只能验证「可编译」，不能验证「可运行」 | 贯穿：如实记录，运行验收在 Linux 本机或 WSL + X11 |
| G5 | 内核 `InstallPlan` 等文件被另一会话占用（未提交） | LU3 若需内核改动会被阻塞 | 等其提交后再评估 |
| G6 | 合规书面问询仍为草稿（`docs/合规/跨平台授权问询.md`） | 阻塞任何对外分发 | LU5 之前 |

---

## 2. 目标与可验证标准

### 目标

1. 在 Linux 侧建立一条**独立的、可持续编译**的 Avalonia 前端，后端只依赖 `PCL.Download` 等中立内核。
2. 先跑通 P0（启动·版本列表 / 实例列表 / 下载进度），再按《计划-安卓UI移植.md》§4.3 映射表平移 P1/P2。
3. 视觉上尽量还原 PCL 身份（配色 / 描边按钮 / 分区卡片 / 内联 SVG 图标 / 渐变分隔线），不另立设计语言。
4. 不把 Linux 专属 UI 代码、Avalonia 依赖塞回任何 Windows 工程（三仓库架构下也无可合并通道）。

### 可验证标准

| 判据 | 方式 |
|---|---|
| 前端在 `linux-x64` 上 **0 错误**编译 | `dotnet build PCL.Desktop.Linux/PCL.Desktop.Linux.csproj -c Release -r linux-x64`（走 `PclRidGuard` + `PclTreePathGuard`） |
| XAML 在生成期真正被校验 | 统一用 `.axaml` 扩展名（`.xaml` 不进 Avalonia 编译，安卓 §8.8.1 已踩过） |
| 内核不回归 | `dotnet test PCL.Download.Tests -c Release` 全绿；`pclcli selftest --suite offline` 退出码 0 |
| 产物落三段树 | `bin-net10/Dev/<版本名>-<构建号>/linux-x64/`，禁止 `bin/`、禁止 `any` 段 |
| 运行期 | 在**有显示的** Linux（本机 WSL + X11，或 CI 之外的实机）冒烟；本机 Windows 无法运行，记录为「仅构建验证」 |

---

## 3. 阶段划分与各阶段验收

| 阶段 | 内容 | 验收方式 | 状态 |
|---|---|---|---|
| **LU0** 现状与边界 | 工程 TFM 盘点、Windows-only 能力清单、资产与缺口；本文件成稿 | 静态扫描结果可复现（§1.3 关键词 + 命中文件数） | ✅ 本轮完成（文档） |
| **LU1** 宿主骨架 | 新建 `PCL.Desktop.Linux`（`net10.0` + Avalonia 11.3.2）：`Program` / `App.axaml` / `MainWindow.axaml` 空壳；产物走 `$(PclTreeRoot)`；**不进 `pcl-next.sln`** | `dotnet build ... -r linux-x64` 0 错误；产物落 `bin-net10/Dev/.../linux-x64/`；XAML 生成期无 `AVLN` 错误 | ✅ 本轮落地 |
| **LU2** 主题与控件 | `Styles/PCLTheme`（§1.3 色值）+ `PclCard`（`ContentControl` + `ControlTemplate`，见安卓 §8.8.4 教训）+ `PclListItem`（`Geometry` 图标）+ 渐变分隔线 `pcSplit` 样式 | 编译 0 错误；在 LU1 宿主里渲染一张卡片 + 一条分隔线；运行期截图（有显示环境） | ⬜ 待 §11 D1 |
| **LU3** P0 接线 | 版本列表（`McVersionManifestClient`）/ 下载进度（`DownloadEngine` + `Progress<DownloadProgress>`）/ 实例列表（下载完成后刷新 `ObservableCollection`） | 与安卓 §8.6 同口径：真实内核调用、非 mock；headless 通路冒烟可在任一侧跑通 | ⬜ |
| **LU4** 桌面平台集成 | 平台服务层（字体解析与 CJK 回退、XDG 配置/缓存目录、`xdg-open` 外部打开、剪贴板、文件对话框、托盘/无边框窗口与 `BeginMoveDrag`） | 每个服务一个最小单测或 selftest 检查项；Linux 冒烟 | ⬜ |
| **LU5** P1/P2 + 打包发布 | `PageDownload` / `PageSetup` / `PageOther` 平移；`linux-x64` 制品（`tar.gz` 或 AppImage）与发布通道接线 | `release.yml` 的 linux 通道可产出制品；`PLATFORM_RELEASE_ENABLED` 未开启时只留 artifact（§0C.4） | ⬜ |

> 阶段推进纪律：**一次只铺一个阶段**。LU1 只放宿主骨架，不顺手写主题；LU2 只放组件，不碰页面。
> 理由同《计划-安卓UI移植.md》§6「不在缺前置时硬铺工程」——一次铺开会把「编译不过」与「设计没定」
> 两类问题缠在一起，难定位。

---

## 4. 前置条件与阻塞项

| # | 项 | 状态 | 影响 |
|---|---|---|---|
| B1 | 合规书面问询（`docs/合规/跨平台授权问询.md` 仍为草稿） | ⬜ | **不阻塞**编译与本地验证；阻塞任何对外分发与 Release 附件 |
| B2 | 本机（Windows）无预装的 `Microsoft.NETCore.App.Host.linux-x64` | ✅ 已实测自带还原（2026-11-08：`-r linux-x64` 构建自动拉取 apphost 包，产出 ELF 宿主 `PCL.Desktop.Linux`，无需 `UseAppHost=false`） | 无 |
| B3 | 无显示环境 | ⚠️ | 运行期视觉/交互验收只能由用户在 Linux 本机或 WSL+X11 执行 |
| B4 | 内核 `InstallPlan.cs` / `InstallDownloadPlan.cs` / `InstallPlanTests.cs` / `InstallTaskDescriptor.cs` 曾被另一会话占用 | ✅ 已解除（2026-11-08：`dev.6-Linux` 提交 `a8a1ae6` P4-A DTO 移植 / `f1f624a` 经验台账，未推送） | LU3 可直接依赖这批 DTO；仍禁止 reset / checkout / push |
| B5 | 中立 UI 库归属未裁决 | 🔒 | 阻塞 LU2 具体落点（§11 D1） |

---

## 5. 改动文件清单

### LU1（本轮已落地）

| 文件 | 内容 |
|---|---|
| `docs/plans/计划-LinuxUI移植.md`（本文件） | 新建，本计划 |
| `PCL.Desktop.Linux/PCL.Desktop.Linux.csproj` | `net10.0`；`OutputType=Exe`；Avalonia 11.3.2（`Avalonia` / `Avalonia.Desktop` / `Avalonia.Themes.Fluent`）；`OutputPath=$(PclTreeRoot)` + `PclUseTreePath=true`；`AppendRuntimeIdentifierToOutputPath=false`、`AppendTargetFrameworkToOutputPath=false` |
| `PCL.Desktop.Linux/Program.cs` | `AppBuilder` 入口，`UsePlatformDetect()` + `StartWithClassicDesktopLifetime` |
| `PCL.Desktop.Linux/App.axaml` / `.axaml.cs` | `FluentTheme` + `RequestedThemeVariant="Light"`；桌面生命周期挂 `MainWindow` |
| `PCL.Desktop.Linux/MainWindow.axaml` / `.axaml.cs` | 空壳窗口（标题 + 一行状态文字），仅用于验证管线 |

### 后续阶段范围（不提前落地）

| 阶段 | 预计新增 |
|---|---|
| LU2 | `PCL.UI`（或 §11 D1 指定落点）下的 `Styles/PCLTheme.axaml`、`Controls/PclCard`、`Controls/PclListItem` |
| LU3 | `ViewModels/*`、`Views/MainView`、`RelayCommand` |
| LU4 | `Services/`（平台服务层实现）+ 对应测试或 selftest 项 |
| LU5 | `Views/Page*.axaml`、打包脚本、`release.yml` linux 制品步骤 |

### 明确不做（本计划整体边界）

- 不改 `pcl-next`（VB/WPF）任何文件；不把 Linux/Avalonia 代码塞进 Windows 工程。
- 不跨仓库拷 `PCL_NEXT_Android` 的 `PCL.UI` 代码（§11 D1 未定前一律不拷）。
- 不移植背景音乐 / NAudio（Linux 首版无音频）。
- 不做 Minecraft 启动/JVM 运行时（属 T2 拆壳后的 `PCL.Core`，当前内核没有）。
- 不做 AppImage/flatpak/系统包管理器集成（LU5 只出 tar.gz 或等价单目录制品）。
- 不改版本常量（`ModBase.vb` 等）、不打 tag、不 push、不动他人未提交改动。
- 不改 `PCL.Download` / `PCL.Cli` 现有公开行为（除非 LU3 发现必须的缺口，且另立批次）。

---

## 6. 改动要点

1. **工程不进 `pcl-next.sln`**：Windows CI（`build.yml`）构建整个 sln，若把 `net10.0` 的 Avalonia 工程塞进去，
   Windows job 会在无 `-r` 的情况下触发 `PclRidGuard` 直接报错。与内核工程「不在 sln、直接编 csproj」的做法一致。
2. **产物路径只用 `$(PclTreeRoot)`**：不手写 `bin-net10/...` 字面量（§0.3.1 教训：第二份真源必然漂移）。
   声明 `PclUseTreePath=true` 让 `PclTreePathGuard` 机械校验「代号 + 版本」两段。
3. **`.axaml` 而非 `.xaml`**：Avalonia 默认项只采集 `**/*.axaml` / `**/*.paml`；
   用 `.xaml` 会「编译全绿、运行时 `No precompiled XAML found`」（安卓 §8.8.1 的真实事故）。
4. **code-behind 不手写 `InitializeComponent`**：交给 XAML 生成器，避免把失败推迟到运行时。
5. **平台相关包只在宿主引入**：`Avalonia.Desktop` 只出现在宿主工程；一旦 LU2 建立共享库，
   共享库只允许引 `Avalonia` + `Avalonia.Themes.Fluent`（与安卓 §8.7.1「一库两宿主」同一原则）。
6. **控件坑位预先记录**：`PclCard` 必须是 `ContentControl` + `ControlTemplate`（`UserControl` 只有一个
   `Content`，外部子元素会把外壳整体覆盖，安卓 §8.8.4 已踩）；`RequestedThemeVariant` 不是
   `UserControl` 属性，只写在 `App.axaml` 一处（安卓 §8.8.2）。

---

## 7. 回归影响面

| 面 | 影响 | 处置 |
|---|---|---|
| Windows 构建 / CI | 新工程不进 sln，Windows job 不感知 | `build.yml` 无需改动；无需新 job |
| `kernel-guard.yml`（`build-linux`） | 只编内核 + 测试，不含 GUI | 不改；如需把 GUI 纳入护栏，属 LU5 |
| `Directory.Build.props` 护栏 | 新工程受 `PclRidGuard` / `PclTreePathGuard` 约束 | 必须带 `-r`；`bin-net10` 树新增一个 RID 子目录（已 gitignore） |
| 内核工程 | 不改 | 无 |
| 他人未提交改动 | 不碰、不 stash | 收尾 `git status` 核对 |

---

## 8. 验证步骤

```bash
# 仓库根 PCL_NEXT_Linux/ 下执行（AGENTS §零：必须带真实 RID）
dotnet build PCL.Desktop.Linux/PCL.Desktop.Linux.csproj -c Release -r linux-x64

# 核对落点（不许落在 bin/ 或 any/）
ls bin-net10/Dev/*/linux-x64/

# 内核不回归
dotnet test PCL.Download.Tests/PCL.Download.Tests.csproj -c Release -r win-x64
```

判定：构建 0 错误；产物在 `bin-net10/Dev/<版本名>-<构建号>/linux-x64/`；`PclTreePathGuard` 打印「通过」。
运行期：`dotnet PCL.Desktop.Linux.dll`（或 apphost）需在 Linux + X11/Wayland 下执行，**本机 Windows 不做运行验收**。

---

## 9. 回滚方案

| 场景 | 回滚动作 | 保留 |
|---|---|---|
| LU1 骨架在 CI 或本机构建失败且短期修不好 | 删除 `PCL.Desktop.Linux/` 目录（新工程，无其他依赖） | 本计划文档、现状结论 |
| Avalonia 包还原在本机不可用 | 先加 `UseAppHost=false` 出 dll；仍不可用则挂起 LU1，记录为环境阻塞 | 计划与现状 |
| LU2 归属方案选定后需要改落点 | 只移动工程目录 + 改 ProjectReference，不重写组件 | 组件代码 |

回滚一律按《AGENTS.md》§六 出回退报告，不静默回退。

---

## 10. 明确不做（与 §5 对齐的摘要）

不改 VB/WPF、不跨仓库拷代码、不做音频、不做启动器内核的业务重写、不做打包外的系统集成、
不动版本常量与 tag、不动他人未提交的内核文件。

---

## 11. 待拍板决策项

| # | 决策项 | 选项 | 需用户确认什么 |
|---|---|---|---|
| D1 | **中立 UI 库归属** | (a) 另立共享仓库 / git 子模块，三平台共用；(b) 允许 Linux 仓库内自建 `PCL.UI`（承认第二份真源）；(c) 维持「宿主先行」，中立库定稿后再接入 | 选哪条；若选 (a)，仓库地址 / 子模块形式；若选 (c)，中立库由谁、何时定稿 |
| D2 | **LU2 是否允许在 Linux 仓库内先落主题与控件** | 与 D1 冲突时以 D1 为准 | 允许 / 不允许 |
| D3 | **Linux 首版是否要音频与托盘** | 当前默认「不做音频、托盘延后」 | 是否需要 |
| D4 | **发布形态** | `tar.gz` 单目录 vs AppImage | LU5 前确认 |

> D1 未定时，本计划只在 LU1（宿主骨架）范围推进；LU2 的组件代码不在本仓库新建共享库目录。

---

## 12. 与既有规则的对应关系

- **§零 三段树 / 真实 RID**：新工程产物落 `bin-net10/Dev/<版本名>-<构建号>/linux-x64/`，
  `PclRidGuard` 与 `PclTreePathGuard` 照常生效，不使用 `-o` / `-p:OutputPath` 绕开。
- **§十 三仓库架构**：Linux 专属 UI 只在 `PCL_NEXT_Linux` 内演进，不跨仓库合并或拷贝。
- **§0C.4 合规**：`PLATFORM_RELEASE_ENABLED` 未开启前不对外分发任何 Linux 制品；本阶段只做编译验证。
- **§三 大改动**：本计划本身就是该条要求的第一份产物；LU2 起每阶段另附小节记录（就地追加，不新建文件）。

---

## 13. 进度记录

### 13.1 LU1 宿主骨架落地（2026-11-08，已完成）

**新增文件**

| 文件 | 内容 |
|---|---|
| `PCL.Desktop.Linux/PCL.Desktop.Linux.csproj` | `net10.0` / `Exe`；Avalonia 11.3.2（`Avalonia` + `Avalonia.Desktop` + `Avalonia.Themes.Fluent`）；`OutputPath=$(PclTreeRoot)`、`PclUseTreePath=true`、`AppendRuntimeIdentifierToOutputPath=false`、`AppendTargetFrameworkToOutputPath=false` |
| `PCL.Desktop.Linux/Program.cs` | `AppBuilder.Configure<App>().UsePlatformDetect().StartWithClassicDesktopLifetime` |
| `PCL.Desktop.Linux/App.axaml` + `.axaml.cs` | `FluentTheme` + `RequestedThemeVariant="Light"`（主题变体只写这一处，安卓 §8.8.2 教训）；桌面生命周期挂 `MainWindow` |
| `PCL.Desktop.Linux/MainWindow.axaml` + `.axaml.cs` | 空壳窗口，只声明「LU1 骨架 · UI 待接入」，不画任何 PCL 页面 |

**构建验证（Windows 本机，跨 RID 编译）**

```bash
dotnet build PCL.Desktop.Linux/PCL.Desktop.Linux.csproj -c Release -r linux-x64
```

| 判据 | 结果 |
|---|---|
| 退出码 | **0**，`已成功生成`，**0 警告 0 错误** |
| 产物落点 | `bin-net10/Dev/2.100.0.4/linux-x64/`（三段树；本仓库无构建号缓存，版本段退化为纯版本名，§0.3.2 允许） |
| apphost | ✅ 产出 ELF 宿主 `PCL.Desktop.Linux`（78 KB），说明 `Microsoft.NETCore.App.Host.linux-x64` 已自动还原 |
| 原生依赖 | ✅ `libSkiaSharp.so`（9.2 MB）、`libHarfBuzzSharp.so`（2.4 MB）随 RID 落位 |
| 平台后端 | ✅ `Avalonia.X11.dll` / `Avalonia.FreeDesktop.dll` 在闭包内（Linux 桌面必需） |
| XAML 是否真被编译 | ✅ 产物内 `AvaloniaResources` / `/App.axaml` / `MainWindow.axaml` 均可检索到；`obj/Release/Avalonia/Resources.Inputs.cache` 由 XAML 编译任务生成（不是「.xaml 被无视」的假绿，对照安卓 §8.8.1） |
| 护栏 | `PclRidGuard` / `PclTreePathGuard` 均未报错（带 `-r linux-x64`） |

**未做 / 诚实边界**

- **未做运行期验收**：本机 Windows，无 Linux 显示环境；窗口能否在 X11/Wayland 下正常起窗口、字体是否豆腐块，
  必须在 Linux 本机或 WSL + X11 上跑 `PCL.Desktop.Linux`（或 `dotnet PCL.Desktop.Linux.dll`）确认。
- 新工程**不进 `pcl-next.sln`**，因此 Windows CI 与 `kernel-guard.yml` 行为不变；未新增 CI job（属 LU5）。
- 未在本仓库新建任何共享 UI 库；`PCL.UI` 归属仍待 §11 D1。
- 未执行任何 git 写操作（未 commit / push / reset / checkout）；他人未提交的规则文档改动原样保留。
