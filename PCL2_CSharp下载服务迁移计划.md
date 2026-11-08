# PCL2 下载服务：恢复方案 + C# 完全替换迁移计划

> 本文档为**纯规划文档**，不含任何可编译代码实现。所有接口以"契约规格"形式给出，用于后续实施阶段落地。  
> 代码事实均来自本地仓库 `D:\bing\MC\pcl_x\PCL2`（版本 2.99.0.1，commit `93ede5d`）。  
> 范围：仅针对"下载服务"及其被停止的恢复；C# 重写仅作规划，不实际编码。
>
> **本版新增条件（用户追加）**：
>
> 1. 本项目是一个 **PCL 的分支版本（fork）**，需独立命名与许可合规。
> 2. 该分支的**目标运行环境基于 .NET 10（`net10.0-windows`）**。

---

## 0. 结论速览（TL;DR）

1. **被停止的服务是什么**：代码里"因法律原因被停止"的服务特指 **Minecraft（MC）游戏本体的下载/安装功能**，由编译期常量 `ModSecret.RestrictMcDownload = True`（`ModSecret.vb:18`）统一门控，共 **14 个调用点**（见附录 A）。其余下载（Modrinth / CurseForge 资源、Java、版本元数据）并未被该常量门控。
2. **逻辑仍在，仅被门控**：MC 下载核心代码（`DlClientList*`、`DlClientJarGet`、`McDownloadClientLoader` 等）**未被删除**，只是被 `If RestrictMcDownload Then …` 分支隐藏。因此"恢复"在技术上是翻转开关 + 解除 UI 门控，而非重新实现。
3. **恢复是合规决策，不是代码开关**：该开关文案为"为遵守相关法律法规，PCL 不再提供 MC 下载功能"。是否恢复须由分支所有者确认其具备合法分发依据（见 §1.3）。C# 重写须保留该开关为**部署期配置**，使合规决策与实现解耦。
4. **本项目的形态 = 分支 + .NET 10**：对外产品名定为 **`PCL (.NET 10 重写)`**（内部代号 **NeoPCL**），满足 PCL 许可证"名称须以 PCL 开头 + 第三方修改后缀"的强制要求（见 §1.1）。目标运行时 **.NET 10**（`net10.0-windows`，WPF 仅 Windows 支持，与 PCL 的 Windows-only 定位一致）。
5. **C# 重写可行性：高**：VB.NET 与 C# 同属 .NET CLR。仓库已有 C# 库 `PCLCS`（原目标 `net48`）被 VB `net48` 主工程引用的先例；跨语言互操作零成本。在分支中，`PCLCS` 与新建的 C# 下载库一并迁到 `net10`。
6. **新增"运行时迁移"工作项**：相比"仅换语言、原地 net48"的方案，本计划因目标 net10 必须额外完成 **net48→net10 运行时提升**（SDK 风格工程转换、WPF on net10、依赖现代化、少量 net48 专属 API 处置）。这带来约 **+25~35 人日**，且需与"语言迁移"解耦以降低风险（见 §5 阶段排序与 §4 风险 R10–R16）。
7. **工作量**：下载服务核心逻辑约 **5,400 行 VB**；C# 重写 + 桥接 + UI 重指 + 测试 ≈ **55 人日**；分支搭建 + net48→net10 运行时提升 ≈ **25~35 人日**；**合计约 80~~90 人日（≈3.5~~4.5 人月）**。

---

## 1. 被停止服务的恢复方案（合规优先）

### 1.1 分支命名与许可证合规（新增，强制前置）

PCL 仓库 `LICENCE` 为《PCL 分发有限许可》+《PCL 存储库合理使用指南》，对"重度使用"（基于本仓库修改 / 重新实现启动或下载 MC、管理 Mod 等）有强制要求。本分支显然属于**重度使用**，须满足：

| #  | 许可证义务                                                                                           | 对本分支的要求                                                                                |
| -- | ----------------------------------------------------------------------------------------------- | -------------------------------------------------------------------------------------- |
| L1 | 明确声明为第三方基于 PCL 的独立二次创作                                                                          | 关于页/README 明示"基于 PCL2 的第三方修改版"                                                         |
| L2 | **名称必须以 `Plain Craft Launcher (PCL)` 开头并加第三方修改后缀**；禁用例：PCL 3 / PCL 手机版 / PCL++ / PCL Pro（像官方续作） | 对外产品名定为 **`PCL (.NET 10 重写)`**（合规）；内部代号可用 `NeoPCL`/`pcl-dotnet10`，但**对外展示名必须带 PCL 前缀** |
| L3 | 首位给出龙腾猫跃（Hex-Dragon）署名 + 赞助链接；**公开源代码**；沿用本指南（类似 CC BY-SA）                                      | 分支须开源；关于页署名                                                                            |
| L4 | 若有 MC 启动功能，须实现与 `McLaunchPrecheck` 一致（或更激进）的正版购买劝导 + 赞助劝导                                       | 恢复 MC 下载时**不得移除**既有 `McLaunchPrecheck` 劝导逻辑                                            |
| L5 | Windows 上不得有与 PCL 赞助解锁类似的功能/表现；主色相须为蓝色                                                          | 分支外观保持蓝色主色相，不引入赞助解锁式功能                                                                 |

> **命名决策**：综合 L2，推荐对外名 **`PCL (.NET 10 重写)`**，内部代号 **`NeoPCL`**。该名可改，但**改后仍需以 PCL 开头**。许可证指向的外部细则（shimo 文档）建议分支所有者另行审阅。
>
> **分支流转**：本节只定"产品名"；分支的职责划分、流转顺序与合并时机见《分支与发布规范.md》。据此，原 `neopcl/net10-download-rewrite` 落为短期开发分支 `feat/download-csharp-engine`（从 `dev` 拉出、合回 `dev`），长期分支固定为 `dev → slush → alpha → beta → pc`，另有 `snapshot` 维护线专用于 PC 阶段 SP 热修。

### 1.2 现状定位

| 项        | 位置                                                                                                                   | 说明                                                              |
| -------- | -------------------------------------------------------------------------------------------------------------------- | --------------------------------------------------------------- |
| 门控常量定义   | `ModSecret.vb:18`                                                                                                    | `Public Const RestrictMcDownload As Boolean = True`             |
| 功能列表声明   | `FormMain.xaml.vb:19, :115`                                                                                          | "删除：为遵守相关法律法规，PCL 不再提供 MC 下载功能……"                               |
| 整合包拦截    | `ModModpack.vb:11, :88`                                                                                              | `If RestrictMcDownload Then Throw New Exception("为遵守相关法律法规……")` |
| 安装入口隐藏   | `PageSelectLeft.xaml.vb:111`                                                                                         | 不向左侧导航添加"安装"项                                                   |
| 下载页初始化早退 | `PageDownloadLeft.xaml.vb:14`                                                                                        | `If Not RestrictMcDownload Then Return`                         |
| 安装页提示    | `PageDownloadInstall.xaml.vb:17`                                                                                     | `If RestrictMcDownload Then` 显示禁用提示                             |
| 更新提示按钮   | `ModMinecraft.vb:2105`                                                                                               | 更新提示中隐藏"下载"按钮                                                   |
| 异常捕获分支   | `FormMain.xaml.vb:1087`                                                                                              | 捕获上述特定异常消息做特殊处理                                                 |
| 其余 6 处   | `PageSelectRight:108`、`PageInstanceModDisabled:12`、`PageResource:58`、`PageLaunchLeft:642`、`PageDownloadModify:315`、… | 隐藏下载/整合包相关 UI 元素                                                |

完整清单见**附录 A**。

### 1.3 技术恢复路径（仅描述，不执行）

恢复 MC 下载功能在技术上是**可逆的配置变更**，而非功能重写：

- 将 `RestrictMcDownload` 由**编译期常量**改为**部署期配置**（建议从 `ModSecret` 移出，改为配置文件 / 环境变量，与开源裁剪隔离）。
- 解除上述 14 处 UI 门控分支（保留分支结构，仅切换条件）。
- MC 下载逻辑代码路径本身已存在，无需重写即可恢复行为。

> 注意：`ModDownload.vb` 中的 `DlSourceOrder` / `DlSourcePreferMojang` / `DlVersionListOrder` 等源排序函数（`ModDownload.vb:1218–1248`）决定官方源与镜像（BMCLAPI）的优先顺序。恢复后须复核默认值是否符合合规要求（仅官方 Mojang/Microsoft 端点，或已获授权的镜像）。

###

---

## 2. 现有 VB 下载服务梳理

### 2.1 模块清单与规模

| 层           | 文件                                               | 行数    | 职责                                                                         |
| ----------- | ------------------------------------------------ | ----- | -------------------------------------------------------------------------- |
| **传输层**     | `Modules/Base/ModNet.vb`                         | 1933  | HTTP 请求/流式下载引擎、断点续传、多源切换、哈希校验、`LoaderDownload`/`NetManager`                |
| **编排框架**    | `Modules/Base/ModLoader.vb`                      | 791   | `LoaderBase`/`LoaderTask`/`LoaderCombo`/`LoaderWorker` 任务编排状态机（下载子系统构建于其上） |
| **MC 下载**   | `Modules/Minecraft/ModDownload.vb`               | 1388  | 版本列表、客户端 jar、资源索引、OptiFine/Forge/NeoForge/LiteLoader/Fabric 列表、源排序         |
| **资源-项目**   | `Modules/Resource/ResourceProject.vb`            | 711   | Modrinth/CurseForge 项目模型、JSON 互转、UI 项生成                                    |
| **资源-版本**   | `Modules/Resource/ResourceVersion.vb`            | 427   | 版本模型、下载 URL 解析（含 `ParseCurseForgeDownloadUrls`）、`ToNetFile`                |
| **资源-搜索**   | `Modules/Resource/ResourceSearcher.vb`           | 530   | Modrinth/CurseForge 搜索 URL 拼装、游标分页 Loader                                  |
| **资源-本地**   | `Modules/Resource/LocalResourceLoaders.vb`       | 345   | 本地文件夹 / 压缩包扫描、更新检查                                                         |
| **资源-本地文件** | `Modules/Resource/LocalResourceFile.vb`          | 277   | 本地资源文件模型、哈希、更新态                                                            |
| **UI 胶水**   | `Pages/PageDownload/ModDownloadLib.vb`           | 2176  | MC 下载/各加载器保存流程与菜单（**混合 UI + 编排**）                                          |
| **UI 胶水**   | `Pages/PageDownload/PageDownloadInstall.xaml.vb` | 1199  | 安装页                                                                        |
| **UI 胶水**   | `Pages/PageDownload/PageDownloadModify.vb`       | 727   | 版本修改页                                                                      |
| **UI 胶水**   | `Pages/PageDownload/PageDownloadLeft.xaml.vb`    | 160   | 左侧导航                                                                       |
| **UI 胶水**   | `Pages/PageDownload/Resource/*.vb`（6 页）          | ~1240 | 资源/整合包/Mod/材质/光影等页                                                         |

> 核心服务逻辑 ≈ **5,400 行 VB**（传输 + 编排框架 + MC + 资源）；UI 胶水 ≈ **6,300 行**。建议重写聚焦前者，"服务"语义上不含 WPF 页面。

### 2.2 三层架构

```
[UI 胶水层]  ModDownloadLib / PageDownload* / Resource pages
                  │ （构造 List(Of LoaderBase) / MyListItem）
                  ▼
[编排层]      ModDownload（MC 列表/源排序） + Resource*（搜索/版本）
                  │ 使用
                  ▼
[传输层]      ModNet（NetRequest* / LoaderDownload(继承 LoaderBase) / NetManager 多线程引擎）
                  │  underpinned by
                  ▼
[框架层]      ModLoader（LoaderBase 状态机：State / Progress / Cancel / Failed）
```

关键点：**下载子系统完全构建在 `ModLoader` 的 `LoaderBase` 状态机之上**（`LoaderDownload` 继承自 `LoaderBase`，`ModDownload`/`ResourceSearcher` 产出 `LoaderTask(Of TIn,TOut)`）。任何 C# 重写都必须与这一契约兼容，或在桥接层适配它。

### 2.3 对外接口契约（关键 public 入口，带文件:行号）

**传输层（ModNet.vb）**

- `NetRequestByClient(Url, Method, …) As String`（:10）— 缓存客户端请求
- `NetRequestByClientRetry(Url, …, BackupUrl) As String`（:44）— 注意：`BackupUrl` 参数已声明但函数体未使用（遗留）
- `NetRequestByClientMultiple(Url, …, ThreadCount, …) As String`（:93）— 分片下载
- `NetRequestByLoader(Urls, …, IsJson, …) As String`（:151/:144）— 多源依次尝试
- `NetDownloadByClient / NetDownloadByLoader`（:172/:192）— 单文件下载
- `LoaderDownload`（:1311，继承 `LoaderBase`）— 多文件下载任务，由 `NetManager.Start` 驱动
- `NetManagerClass`（:1639）— 全局下载管理器（0.1s 轮询刷新进度）
- `NetFile`（:579）/ `NetSource`（:356）/ `NetThread`（:428）— 下载单元模型
- `HasDownloadingTask` / `FindFreePort(s)` / `Ping` / `ShouldValidateSslCertificateOnLogin`

**MC 下载（ModDownload.vb）**

- `DlClientJarGet(Instance) As NetFile`（:9）· `DlClientAssetIndexGet`（:34）· `DlClientFix`（:54，返回 `List(Of LoaderBase)`）
- `DlClientListGet(Id)`（:297）· `DlClientListResult`（:178 结构）
- `DlOptiFineListResult`/`DlOptiFineListEntry`（:332/:347）· `DlForgeListResult`（:478）· `DlForgeVersionEntry`（:586）· `DlNeoForgeList*`/`DlLiteLoaderList*`/`DlFabricList*`
- `DlModRequest(Url, …)`（:1175）— 资源站请求封装
- `DlSourceOrder` / `DlVersionListOrder`（:1226/:1240）· `DlSourceAssetsGet`/`LibraryGet`/`LauncherOrMetaGet`/`ModGet`（:1248–1304）— **镜像源排序核心**

**资源子系统**

- `ResourceProject`（类，:4）：`FromProjectId`(:391)、`ToJson`(:432)、`ToResourceItem`(:455)、`ApplyLogoToMyImage`(:569)
- `ResourceVersion`（类，:4）：`FromPlatformJson`(:115)、`FromProjectId`(:227)、`ToCacheJson`(:289)、`ToListItem`(:340)、`ToNetFile`(:382)、`ParseCurseForgeDownloadUrls`(:414)
- `ResourceSearcher`（类，:1）：`SearchRequest`/`SearchResult` 嵌套类、`GetCurseForgeAddress`(:70)、`GetModrinthAddress`(:96)、`GetLoader`(:161)
- `LocalResourceLoaders`（模块，:1）：`LocalResourceFileLoad`(:5)、`LocalResourceOnlineLoad`(:102)

**UI 胶水（ModDownloadLib.vb，仅列 public 入口）**

- `McDownloadListItem(Entry, OnClick, IsSaveOnly)`（:77）· `McDownloadForgeRecommendedRefresh`(:1434) / `Get`(:1468)
- `McDownloadForgelikeSave`(:882) · `McDownloadFabricLoaderSave`(:1580) 等

### 2.4 第三方依赖矩阵（来自 `Plain Craft Launcher 2.vbproj`）

| 依赖                              | 版本     | 用途                 | C# 侧 / net10 处置                                                                                           |
| ------------------------------- | ------ | ------------------ | --------------------------------------------------------------------------------------------------------- |
| `CacheCow.Client`               | 2.13.1 | HTTP 响应缓存（镜像/列表缓存） | 需确认其有 `netstandard2.0`/`net6+` 构建（2.x 通常具备），否则换自定义 `HttpClient` 缓存或 `Microsoft.Extensions.Caching.Memory` |
| `Newtonsoft.Json`               | 13.0   | JSON 解析（全工程重度使用）   | 有 `netstandard2.0` 构建，net10 可直接沿用；或迁 `System.Text.Json`（API 习惯迁移成本）                                       |
| `ThrottleDebounce`              | 2.0.2  | 防抖（搜索输入等）          | 有 `netstandard2.0` 构建，可沿用；或 `System.Reactive`                                                             |
| `Imazen.WebP` / `libwebp64`     | —      | WebP 解码（非下载核心）     | 需 net10 兼容构建；不影响下载库，但分支整体需升级                                                                              |
| `NAudio`                        | —      | 音乐播放（非下载核心）        | 有 `net6+` 构建；分支整体升级时一并处理                                                                                  |
| `Ookii.Dialogs.Wpf`             | —      | 文件对话框              | 有 .NET Core WPF 构建                                                                                        |
| `PCLCS`（C#，现 net48）             | —      | Java 查找/启动等 C# 能力  | **分支中须 retarget 到 net10**（已验证 VB→C# 引用先例）                                                                 |
| `MeloongCore`（C# submodule，本地空） | —      | 基础库                | 若启用须 retarget 到 net10                                                                                     |

> 下载核心真正依赖的只有 **CacheCow + Newtonsoft**（传输缓存与 JSON）。其余为 UI/音频/图像，属边界；分支整体迁 net10 时须统一现代化。

### 2.5 与宿主的耦合（符号全工程出现次数，评估影响面）

| 符号                   | 次数 | 含义                      |
| -------------------- | -- | ----------------------- |
| `ResourceVersion`    | 74 | 资源模型被 UI 广泛引用           |
| `McDownload*`        | 72 | MC 下载胶水集中在 PageDownload |
| `LoaderCombo`        | 69 | **编排框架被全工程使用**（状态机契约）   |
| `ResourceProject`    | 52 | 资源项目模型广泛使用              |
| `NetRequestByClient` | 52 | **网络请求被全工程使用**（非下载专属）   |
| `DlModRequest`       | 22 | 资源请求封装                  |
| `LoaderDownload`     | 40 | 下载任务挂接 Loader 框架        |
| `NetRequestByLoader` | 7  | 多源请求                    |
| `RestrictMcDownload` | 14 | 门控开关                    |

**结论**：`ModNet` 的 `NetRequestByClient*` 是**全工程共享基础设施**（52 处，含主题图、音乐、登录、网页等非下载功能），重写须保持对外签名稳定；`LoaderCombo`(69)/`LoaderDownload`(40) 表明下载与 Loader 状态机深度绑定，是兼容性重点。

---

## 3. C# 替换的兼容性评估

### 3.1 语言互操作（结论：优秀，零成本）

- VB.NET 与 C# 编译为同一 CLR IL，可在同一 **Solution** 内分工程引用，无需 COM/封送。
- 仓库已有先例：`PCLCS.csproj` 被 VB 主工程通过 `ProjectReference` 引用。**分支中沿用该模式**，仅将双方目标框架统一到 net10。
- VB 可消费 C# 的：静态类/方法、`class`/`struct`、`event`、`Task`（用 `Await`）、`IProgress(Of T)`、`CancellationToken`、泛型。

### 3.2 目标框架约束（硬约束，本版更新）

- 上游 VB 主工程 `<TargetFrameworkVersion>v4.8</TargetFrameworkVersion>`，`PCLCS` 为 `net48`。
- **本分支目标运行时 = .NET 10**（`net10.0-windows` 用于 WPF 主工程；C# 下载库建议目标 `netstandard2.0`（或 `netstandard2.0;net10.0`）以便：
  - **解耦两个风险迁移**：下载库先以 `netstandard2.0` 实现，可被**仍运行在 net48 的 VB 主工程**引用完成"语言替换"；验证通过后再将整个分支（VB 主工程 + PCLCS + 下载库）统一 retarget 到 `net10.0-windows`，完成"运行时迁移"。
  - 这样"换语言"与"换运行时"各自独立验证、独立回滚（见 §5 排序与 §4 R9/R10）。
- **不推荐**在单一改动中同时做"语言迁移 + 运行时迁移 + 工程文件转换"三件事耦合。本计划将运行时迁移设为**收尾阶段**，而非起点（但分支的*声明目标*即 net10）。


### 3.3 共享领域类型（需 DTO 化）

下载模块依赖的 VB 类型（`McInstance`、`McVersion`、`Configs`、`MyColor`、`MyImage`、`McFolderList` 等）不能让 C# 库反向引用 VB 工程（会循环引用）。方案：

- **推荐**：在 C# 库内定义 **POCO DTO**（如 `DownloadInstanceInfo`、`SourceUrlSet`、`HashCheck`），在 VB 桥接层做 `McInstance → DTO` 映射；C# 库完全不依赖 VB 类型，实现彻底解耦。
- 备选：将 `McInstance`/`McVersion` 等上移到一个共享 VB/C# 工程，但改动面更大。

### 3.4 VB 特有语法 → C# 迁移注意点

| VB 惯用法 | 位置示例 | C# 对应 |
|---|---|---|
| `On Error Resume Next` | `ModNet.vb:1352` 等 | `try/catch` 显式处理 |
| `My` 命名空间（`My.Computer`/`My.Application`/`My.Settings`） | 全工程（约 40+ 文件） | net10 上经 `Microsoft.VisualBasic` 包仍可用；`My.Computer.Registry` 等需验证路径行为 |
| `AddressOf` / 匿名 `Sub()` | `LoaderBase` 事件、`RunInNewThread` | 委托 / lambda |
| 默认属性（`Files` 等） | `LoaderDownload.Files` | 显式属性访问 |
| 可选参数 `Optional X As T = Nothing` | `NetRequestByClientRetry` 等 | C# 命名可选参数（需显式默认值） |
| XML 文档字面量 | 部分页面 | C# 用 `XDocument` |
| `Resource.resx` 资源 | 主工程 | 独立 `*.resx` 或常量表 |
| `RunInNewThread` / 自研线程模型 | `ModLoader`/`ModNet` | `Task.Run` / `Task.Factory.StartNew` + `CancellationToken` |

### 3.5 行为等价性风险点（最易出 bug 处）

1. **多源切换与失败重试语义**：`ModNet` 维护 `SourcesOnce`/`IsRangeNotSupported`，首个线程出错切源、合并失败才逐源重试（:1104–1136）。C# 须 1:1 还原此状态机。
2. **断点续传（Range）**：`NetThread` 按 `DownloadStart` 分片、`RangeNotSupportedException`（:1302）降级单线程。C# 须保留"源不支持 Range 则禁用该源"逻辑。
3. **跨版本文件复用 / 已有文件查找**：`LoaderDownload.Start` 扫描所有 MC 文件夹按 Hash+大小复制（:1434–1468），省流量关键特性，C# 须保留。
4. **BMCLAPI 限频特例**：`bmclapi` 源高频返回 403/429 时**不禁用源**（:1083），且每次请求前 `Thread.Sleep(100)`（:1744/:1764）。属业务规则，须同步。
5. **Loader 状态机契约**：`State`/`Progress`/`Cancel`/`Failed`/`ProgressWeight`/`OnStateChangedThread/Ui` 事件——UI 与任务栏进度全部挂此，C# 输出须经桥接精确映射。

### 3.6 net48 → net10 运行时迁移要点（新增）

经全工程检索，**未发现** `BinaryFormatter` / `HttpWebRequest` / `WebClient` / `AppDomain` / `Remoting` / `ISerializable` 等典型 net48-only 危险 API，故序列化与旧网络栈迁移风险低。主要处置项：

| 类别 | 工程内证据 | net10 处置 |
|---|---|---|
| **工程文件风格** | `Plain Craft Launcher 2.vbproj` 为非 SDK 风格（含大量 `HintPath`） | 转换为 SDK 风格 `.vbproj`，`<TargetFramework>net10.0-windows</TargetFramework>` + `<UseWPF>true</UseWPF>`；保留 `PackageReference` |
| **WPF** | 大量 `System.Windows` / `xaml.vb` | net10 WPF 仅 Windows 支持，与 PCL Windows-only 定位一致；注意 net10 WPF 默认字体/部分控件行为微调 |
| **System.Drawing** | `MyBitmap.vb:21`、`ModNet.vb:1` 等引用 `System.Drawing` | 加 `System.Drawing.Common` NuGet（net6+ 仅 Windows 可用；因分支即 Windows，满足）；长期可迁 `ImageSharp`/`SkiaSharp` 以去 Windows 依赖（可选） |
| **VB `My.*` 命名空间** | 约 40+ 文件使用 `My.Application`/`My.Computer`/`My.Settings` | 经 `Microsoft.VisualBasic` 包在 net10 可用；重点验证 `My.Computer.Registry`（对应 `RestrictMcDownload`/`ModSecret` 的 `HKCU\Software\PCLDebug`）路径行为 |
| **PCLCS（C# 库）** | 现 `net48` | retarget 到 `net10.0`（或 `netstandard2.0;net10.0`） |
| **MeloongCore（submodule）** | 本地空目录 | 若启用须 retarget；当前不影响下载迁移 |
| **依赖现代化** | CacheCow / Newtonsoft / ThrottleDebounce / NAudio / Ookii / WebP | 全部升级到含 `netstandard2.0`/`net6+` 构建的版本；`CacheCow.Client` 需先确认可用构建，否则替换 |

> 运行时迁移本质是"让 43k 行 VB 在 net10 上编译运行"，属机械但面广的工作；将其置于**下载语言替换验证之后**的收尾阶段，可避免与语言迁移同时出错、互相干扰。

---

## 4. 工作量、风险与兼容性结论

### 4.1 工作量估算

| 工作项 | 估算（人日） | 说明 |
|---|---|---|
| 阶段 0：分支搭建 + 命名/许可合规 + 契约冻结 | 5 | fork 仓库、命名合规（§1.1）、`IDownloadService`/DTO 契约、CI 雏形 |
| 阶段 1：C# 下载库（netstandard2.0）+ 传输引擎 | 12 | 多源/分片/续传/哈希/缓存；保持 `NetRequestByClient*` 在 VB 侧 |
| 阶段 2：MC 下载编排（替换 ModDownload） | 10 | 版本/资源/各加载器列表 + 源排序（含 §1.3 合规默认值复核） |
| 阶段 3：资源子系统（替换 Resource*） | 8 | 搜索/版本/本地扫描 + 平台解析 |
| 阶段 4：VB 桥接 + UI 重指 + 合规开关 | 12 | `LoaderBase` 适配器、`MyListItem`、页面改写、`RestrictMcDownload`→部署配置 |
| 阶段 5：**net48→net10 运行时提升**（收尾） | 20~30 | SDK 风格工程转换、WPF on net10、`System.Drawing`/`My.*` 处置、依赖现代化、PCLCS retarget、全量编译验证 |
| 阶段 6：下线 VB 下载模块 + 回归 + 发布 | 8 | 集成测试、性能基准、灰度 |
| **合计** | **~80~90 人日（≈3.5~4.5 人月）** | 含 20~30% 缓冲 |

> 不含：UI 页面整体重写（仅"重指"）、Java/音乐/图像等非下载模块、文档。阶段 5 的 20~30 人日浮动取决于 SDK 风格转换的自动化程度与依赖兼容性。

### 4.2 风险登记表

| ID | 风险 | 概率 | 影响 | 缓解 |
|---|---|---|---|---|
| R1 | 重写 `ModNet` 影响全工程 52 处非下载调用 | 中 | 高 | 保持 `NetRequestByClient*` 签名稳定；先圈定"下载专用路径"再替换；阶段内灰度 |
| R2 | Loader 状态机契约漂移导致 UI/进度错乱 | 高 | 高 | C# 经桥接产出 `LoaderBase`，契约由 VB 侧定义；写契约一致性单测 |
| R3 | 多源 fallback / 限频特例（BMCLAPI）行为不一致 | 中 | 高 | 将 :1083/:1744 等业务规则显式列为验收用例 |
| R4 | 跨版本文件复用失效，流量/速度退化 | 中 | 中 | 保留已有文件扫描逻辑，阶段 1 即纳入测试 |
| R5 | `ModDownloadLib`(2176 行) UI/逻辑混杂，重指成本高 | 高 | 中 | 先抽离"纯编排"部分入 C#，UI 胶水仅做薄适配 |
| R6 | 合规：恢复 MC 下载的法律前提未确认 | — | 极高 | **非技术项**；见 §1.4，开关保留为部署配置；保留 `McLaunchPrecheck` 劝导 |
| R7 | VB↔C# 混合工程构建/CI 复杂度 | 低 | 中 | 已有 PCLCS 先例；复用其 build/CI 配置 |
| R8 | 依赖 parity（CacheCow/Newtonsoft） | 低 | 低 | 直接沿用 net 兼容版本；CacheCow 缺失则换 `HttpClient` 缓存 |
| R9 | 语言迁移与运行时迁移耦合导致双重故障 | 中 | 高 | C# 库以 `netstandard2.0` 先在 net48 主工程验证，net10 提升置为独立收尾阶段（§3.2/§5） |
| R10 | 非 SDK 工程转 SDK 风格 + WPF on net10 编译问题 | 中 | 高 | 阶段 5 用 `try-convert`/手工转换；先小模块探路；保留 net48 并行构建 |
| R11 | `System.Drawing.Common` 在 net10 仅 Windows | 中 | 中 | 分支即 Windows，满足；长期可迁 ImageSharp（可选，非阻塞） |
| R12 | VB `My.*`（尤其 `My.Computer.Registry`）在 net10 行为差异 | 低 | 中 | 阶段 5 重点验证 `HKCU\Software\PCLDebug` 读写；必要时改 `Microsoft.Win32.Registry` |
| R13 | `CacheCow.Client` 无 net10 兼容构建 | 低 | 中 | 预先验证；缺失则换自建 `HttpClient` 缓存层 |
| R14 | **许可命名违规**（L2：名称须以 PCL 开头） | — | 高 | 对外名强制 `PCL (.NET 10 重写)`；改名的任何变体仍须带 PCL 前缀 |
| R15 | 分支未开源 / 未署名（L3） | — | 高 | 阶段 0 即建立开源仓库 + 关于页署名模板 |
| R16 | 恢复 MC 下载却移除 `McLaunchPrecheck` 劝导（违反 L4/L5） | 低 | 高 | 恢复开关时显式保留劝导逻辑与蓝色主色相 |

### 4.3 兼容性结论

- **可行性**：高。同 CLR + 既有 C#→VB 引用先例；net10 WPF 与 `System.Drawing.Common` 在 Windows 上可用；无高危 net48-only API。
- **关键设计决策**：C# 下载库通过 **VB 桥接层**适配 `LoaderBase` 状态机，对外暴露现代 API（`Task`/`IProgress`/`CancellationToken`），对内保持 Loader 语义；共享领域类型以 **DTO** 穿越边界，杜绝循环引用。
- **分阶段解耦**：语言迁移（阶段 1–4，库目标 `netstandard2.0`，先在 net48 主工程验证）与运行时迁移（阶段 5，统一 net10）相互独立、独立回滚。

---

## 5. 分阶段实施路线（仅规划）

> 每阶段均设"退出标准"，未达标不进入下一阶段。阶段 1–4 可先在 **net48 主工程**上完成"语言替换"验证；阶段 5 统一提升到 **net10**；阶段 6 收尾。全程**不在此文档阶段实际写代码**。

### Phase 0 — 分支搭建与接口冻结（约 5 人日）
- 创建 fork 仓库 **NeoPCL**（内部代号），分支 `neopcl/net10-download-rewrite`；置 git 远端；建立开源仓库与 `LICENCE`/署名模板（满足 L1/L3）。
- **命名合规**：对外产品名锁定 **`PCL (.NET 10 重写)`**（满足 L2）；关于页/README 模板含龙腾猫跃署名 + 赞助链接（L3）、`McLaunchPrecheck` 劝导说明（L4）、蓝色主色相约定（L5）。
- 冻结契约：定义 `IDownloadService`、DTO（`DownloadRequest`/`SourceSet`/`HashCheck`/`DownloadResult`）、桥接接口（`IProducesLoader`）。
- 新建 `PCL.Download` C# 类库（`<TargetFramework>netstandard2.0</TargetFramework>`，后期追加 `net10.0`），在 sln 内被 VB 主工程引用（仿 PCLCS）。
- CI：msbuild 同时编译 VB + C#；基础单测工程（net48 + NUnit/xUnit）。
- **退出标准**：空库可被 VB（net48）引用、CI 绿、契约文档评审通过。

### Phase 1 — C# 传输引擎（约 12 人日，替换 ModNet 下载部分）
- 实现：多源并发 `NetSource`/`NetThread` 等价物、Range 分片续传、`RangeNotSupportedException` 降级、SHA1 校验、已有文件跨版本复用、`NetManager` 进度轮询。
- 保留 `NetRequestByClient*` 在 VB 侧（全工程共享），C# 仅接管"多文件下载"路径。
- **退出标准**：用 PCL2 真实版本清单跑通单/多文件下载，与 VB 行为对比（速度、断点、错误率）一致；BMCLAPI 限频规则还原。

### Phase 2 — C# MC 下载编排（约 10 人日，替换 ModDownload）
- 实现：版本列表（Mojang/BMCLAPI 双源）、客户端 jar、资源索引、`DlClientFix`；OptiFine/Forge/NeoForge/LiteLoader/Fabric 列表加载器；`DlSourceOrder`/`DlVersionListOrder` 源排序（含 §1.3 合规默认值复核）。
- **退出标准**：各加载器列表拉取、校验、排序结果与 VB 一致；源 fallback 用例通过。

### Phase 3 — C# 资源子系统（约 8 人日，替换 Resource*）
- 实现：`ResourceProject`/`ResourceVersion`/`ResourceSearcher`/`LocalResourceLoaders` 的 C# 等价；Modrinth/CurseForge 地址拼装与游标分页；`ParseCurseForgeDownloadUrls`；本地扫描与更新检查。
- **退出标准**：搜索/版本解析/本地扫描结果与 VB 一致；CF API Key 经环境变量（同 `ModSecret` 现行做法）。

### Phase 4 — VB 桥接与 UI 重指 + 合规开关（约 12 人日）
- 桥接层：C# `IDownloadService` 输出 → `List(Of LoaderBase)`，保证 `State`/`Progress`/`Cancel` 映射。
- UI 重指：`ModDownloadLib`/`PageDownload*`/`Resource pages` 改为调用 C# 服务；`MyListItem`/`MyResourceItem` 数据来自 C# DTO。
- 合规开关：将 `RestrictMcDownload` 改为**部署期配置**（见 §1.4），接入桥接层与 UI 门控；**保留 `McLaunchPrecheck` 劝导**。
- **退出标准**：UI 行为不变（含禁用态）、进度/取消/重试正确；开关可在不重新编译下切换。

### Phase 5 — net48 → net10 运行时提升（约 20~30 人日，收尾）
- 主工程 `.vbproj` 转 SDK 风格：`<TargetFramework>net10.0-windows</TargetFramework>` + `<UseWPF>true</UseWPF>`；清理 `HintPath`。
- `PCL.Download` 与 `PCLCS` retarget 到 `net10.0`（或保留 `netstandard2.0;net10.0`）。
- 处置 `System.Drawing.Common`（加包，Windows-only）、VB `My.*`（`Microsoft.VisualBasic`，重点验证 `My.Computer.Registry`）、依赖现代化（CacheCow/Newtonsoft/ThrottleDebounce/NAudio/Ookii/WebP 升级到 net 兼容构建）。
- 全量编译 + 启动冒烟 + 下载回归。
- **退出标准**：分支在 .NET 10 (Windows) 上编译通过、启动正常、下载功能与 Phase 4 等价；`System.Drawing`/`My.*` 无行为回归。

### Phase 6 — 下线 VB 下载模块与发布（约 8 人日）
- 移除 `ModNet` 下载部分、`ModDownload.vb`、`Resource*` 的 VB 实现（保留 `NetRequestByClient*` 若仍被他用）。
- 全量回归 + 性能基准对比；灰度发布（net10 独立安装包）。
- **退出标准**：VB 下载代码为零、功能与基准版等价、无回归。

### 可并行项
- Phase 2/3 可在 Phase 1 出口后并行（均依赖 Phase 0 契约）。
- 桥接层设计与 Phase 1 同步启动（契约驱动）。
- 阶段 5 的依赖兼容性"预验证"（CacheCow 构建、WPF on net10 探路）可在 Phase 1–4 期间并行小步进行。

### 回滚策略
- 每阶段（含 Phase 5 前后）保留 VB 旧实现为"并行双实现"，通过配置开关在两套实现间切换，任一阶段出问题即回退，不阻断发布。

---

## 6. 建议的下一步

1. **合规确认（最高优先级）**：分支所有者确认 §1.4 前提；确认 fork 命名 **`PCL (.NET 10 重写)`** 符合许可证 L2；本文档不替代法律意见。
2. **确认阶段范围**：是否接受"仅重写核心逻辑（5,400 行）+ 桥接"的范围，还是将 UI 胶水一并纳入。
3. **确认运行时策略**：默认按本计划——下载语言替换先在 net48 验证、net10 提升置为收尾（解耦风险）；若坚持"分支从第一天起即 net10"，则可调整 Phase 顺序（先 Phase 5 再 1–4），但风险叠加，不推荐。
4. 上述确认后，可进入 **Phase 0** 实际搭建（届时将产生真实代码——超出本规划文档范围）。

---

## 附录 A — `RestrictMcDownload` 调用点完整清单

| # | 文件:行 | 作用 |
|---|---|---|
| 1 | `ModSecret.vb:18` | 常量定义 `= True` |
| 2 | `FormMain.xaml.vb:19` | 功能列表"删除 MC 下载"声明 |
| 3 | `FormMain.xaml.vb:115` | 同上（另一版本构建） |
| 4 | `FormMain.xaml.vb:1087` | 捕获"不再提供 MC 下载"异常 |
| 5 | `PageSelectLeft.xaml.vb:111` | 不添加左侧"安装"入口 |
| 6 | `PageSelectRight.xaml.vb:108` | 隐藏下载按钮 |
| 7 | `PageInstanceModDisabled.xaml.vb:12` | 隐藏被禁用 Mod 页相关项 |
| 8 | `PageResource.xaml.vb:58` | 隐藏整合包搜索安装按钮 |
| 9 | `PageLaunchLeft.xaml.vb:642` | 隐藏启动页下载相关 |
| 10 | `PageDownloadLeft.xaml.vb:14` | `If Not RestrictMcDownload Then Return` |
| 11 | `PageDownloadInstall.xaml.vb:17` | 安装页显示禁用提示 |
| 12 | `PageDownloadModify.vb:315` | 隐藏 Minecraft 卡片 |
| 13 | `ModModpack.vb:11` | 整合包保存前检查 |
| 14 | `ModModpack.vb:88` | 抛出"为遵守相关法律法规……" |

## 附录 B — 关键文件速查

| 关注点 | 文件 |
|---|---|
| 门控开关 | `Modules/ModSecret.vb` |
| 网络/下载引擎 | `Modules/Base/ModNet.vb` |
| 任务编排框架 | `Modules/Base/ModLoader.vb` |
| MC 下载逻辑 | `Modules/Minecraft/ModDownload.vb` |
| 资源子系统 | `Modules/Resource/*.vb` |
| 下载 UI 胶水 | `Pages/PageDownload/*.vb` |
| C# 引用先例 | `PCLCS/PCLCS.csproj`、`.vbproj:924` |
| 工程目标框架 | `Plain Craft Launcher 2.vbproj`（`v4.8`，分支将转 `net10.0-windows`） |
| 许可证 | `LICENCE`（PCL 分发有限许可 + 合理使用指南） |

## 附录 C — 分支合规清单 & net10 依赖现代化清单（执行用检查表）

**许可合规（Phase 0 必须完成）**
- [ ] 对外名 = `PCL (.NET 10 重写)`（任何改名仍须 PCL 前缀，L2）
- [ ] README/关于页声明"基于 PCL2 的第三方修改版"（L1）
- [ ] 关于页首位含龙腾猫跃署名 + 赞助链接（L3）
- [ ] 分支仓库公开源代码，沿用 `LICENCE`（L3）
- [ ] 保留 `McLaunchPrecheck` 正版/赞助劝导（L4）
- [ ] Windows 主色相保持蓝色、无赞助解锁式功能（L5）

**net10 依赖现代化（Phase 5 验证）**
- [ ] `CacheCow.Client` 确认 net 兼容构建（否则换 `HttpClient` 缓存）
- [ ] `Newtonsoft.Json` 升级至 net 兼容版（或迁 `System.Text.Json`）
- [ ] `ThrottleDebounce`、`NAudio`、`Ookii.Dialogs.Wpf`、`Imazen.WebP` 升级至 net 兼容构建
- [ ] `PCLCS` retarget `net10.0`
- [ ] `MeloongCore`（若启用）retarget `net10.0`
- [ ] 加 `System.Drawing.Common`（Windows-only 验证）
- [ ] `Microsoft.VisualBasic` 包就位，`My.*` 行为复验（尤其 Registry）
