# 计划：Phase 4 主体——下载数据源 C# 化

> 对应《PCL2_CSharp下载服务迁移计划.md》§5 Phase 4；发布身份见下一版公布标准（dev.4 内容并入下一版一并发布）。

## 1. 目标

把下载页（安装 / 修改 / Mod 管理）与资源详情页的数据来源从 VB 实现迁到 `PCL.Download`，
使 UI 只负责展示与交互。完成标准沿用 Phase 4 退出标准：**UI 行为不变（含禁用态）、进度/取消/重试正确**。

## 2. 前置

- Phase 1 传输引擎已由 C# 接管（`NetInterop.RunCSharpDownload`），本阶段不再改动引擎。
- Phase 3 资源子系统（搜索 / 工程 / 版本 / 指纹反查）已在 C# 侧完备，资源搜索已重指。
- 合规开关已落地（`ModDownloadPolicy.vb`，环境变量 + `DownloadPolicy.ini` 双通道）。

## 3. 现状

| 数据 | 现状 | 归属 |
|---|---|---|
| 资源搜索列表 | 已由 `ResourceProviderRegistry.Search` 提供 | ✅ C# |
| 资源工程 / 版本 / 指纹反查 | 工程查询与版本枚举已由 C# 提供（本轮），UI 侧仍用 VB 模型投影 | ✅ C#（投影保留） |
| MC 版本清单 | 本轮迁到 `McVersionManifestClient` | ✅ C# |
| OptiFine / Forge / NeoForge / LiteLoader / Fabric 列表 | 已由 `LoaderListClient` 提供（六类入口全在 C#） | ✅ C#（投影保留） |
| 本地 Mod 列表扫描 / 在线反查与更新检测 | 本轮改由 `LocalResourceScanner` / `LocalResourceOnlineLoader` 提供 | ✅ C#（投影保留） |
| 工程批量查询（`FromProjectIds`） | 本轮改由 `ResourceProject.FromProjectIds` 提供（批量接口 + 进程内缓存） | ✅ C#（投影保留） |
| 版本合并（原版 + 加载器 json 合成） | 本轮迁到 `VersionMerger` | ✅ C# |
| Forge / NeoForge 安装器的版本串归一化、下载地址、启动参数、进度映射 | 本轮迁到 `ForgelikeInstaller` | ✅ C# |
| OptiFine 安装器的 Java 版本探测、启动参数、成败判定 | 本轮迁到 `OptiFineInstaller` | ✅ C# |
| OptiFine 下载源、安装器落盘位置、旧式版本 json | 本轮迁到 `OptiFineProfile`（「安装」与「另存为」共用一份） | ✅ C# |
| LiteLoader 版本 json / Fabric 实例名与 profile 地址 | 本轮迁到 `LiteLoaderProfile` / `FabricProfile` | ✅ C# |
| 安装编排（`ModDownloadLib`，安装器执行 / 库分析 / 版本落盘） | VB，与 `MyListItem` / `LoaderTask` 深度耦合 | 🟡 各加载器的数据推导已全出，编排仍在 VB |
| 下载任务列表 / 进度 | VB `LoaderBase`，底层已由 C# 引擎驱动 | ⬜ |

## 4. 文件清单

### 4.1 本轮已完成

- `PCL.Download/McVersionManifest.cs`（新增）：`McVersionEntry` / `McVersionManifest` / `McVersionSource` /
  `McVersionSources` / `McVersionManifestClient`。
- `PCL.Download/IResourceApi.cs`：新增 `IDirectResourceApi`（单地址 + 显式超时，不走候选源轮换）。
- `PCL.Download/HttpResourceApi.cs`：实现 `RequestDirect`；超时取消后的次生异常统一归类为 `Timeout`。
- `pcl-next/Modules/Minecraft/ModDownload.vb`：`DlClientListMain` / `DlClientListMojangMain` /
  `DlClientListBmclapiMain` 改为调用 C#，新增 `ApplyClientList` / `CheckClientUpdateHint`；
  `DlClientListResult` 结构与 UI 读取方式不变。
- `PCL.Download.Tests/McVersionManifestTests.cs`、`HttpResourceApiTests.cs`：新增 15 个用例。

### 4.2 第二轮：资源详情页

- `PCL.Download/ResourceVersion.cs`：新增 `DependencyIds`（供 VB 缓存序列化）、补 CF 缺失重试日志。
- `pcl-next/Modules/Resource/ResourceVersion.vb`：`FromProjectId` 转发到 C#，新增 DTO 投影构造与
  `ClearCaches`（同时清 C# 的版本缓存与工程缓存）；补回默认构造（定义带参构造后 VB 不再自动生成）。
- `pcl-next/Modules/Resource/ResourceProject.vb`：`FromProjectId` 转发到 C#，保留 VB 本地快查缓存。
- `pcl-next/Pages/PageDownload/PageDownloadLeft.xaml.vb`、`pcl-next/Pages/PageInstance/PageInstanceMod.xaml.vb`：
  刷新按钮改调 `ResourceVersion.ClearCaches()`，否则只清 VB 那份、C# 那份仍是旧列表。

### 4.3 第三轮：加载器列表（六类）

- `PCL.Download/SourceRace.cs`（新增，132 行）：把「候选择速」从 `McVersionManifestClient` 里提出来，
  供版本清单与六类加载器列表共用。`SourceRaceCandidate<T>` / `SourceRaceResult<T>` / `SourceRace.Run`。
- `PCL.Download/LoaderListModels.cs`（新增，271 行）：`OptiFineListEntry` / `ForgeVersionEntry` /
  `NeoForgeListEntry` / `LiteLoaderListEntry` / `LoaderListResult<T>` / `LoaderListSources`（全部地址与数量下限）。
- `PCL.Download/LoaderListParsers.cs`（新增，482 行，`internal`）：逐字平移 VB 正则——
  OptiFine（HTML / JSON 两路）、Forge MC 版本列表（HTML / JSON）、Forge 版本列表（HTML / JSON）、
  NeoForge（两段原文拼接后扫描）、LiteLoader、Fabric 校验。
- `PCL.Download/LoaderListClient.cs`（新增，270 行）：六个 `Fetch*` 入口 + `FetchOptiFineMirror`
  （固定单镜像源）+ `ClearCache`；内部 `FetchCore` 统一做排期、缓存与文本取数。
- `PCL.Download/McVersionManifest.cs`：删掉内嵌轮询与 `PollMilliseconds`，改调 `SourceRace.Run`。
- `PCL.Download/IResourceApi.cs` / `HttpResourceApi.cs`：新增 `IDirectTextResourceApi.RequestDirectText`
  （HTML 原文通道）；`SendOnce` 拆成 `SendOnceText`（传输 + 错误归类）与 JSON 解析两层，抽出 `BuildDirectCandidate`。
- `PCL.Download/McVersionUtil.cs` / `ResourceModels.cs`：删除 `McVersionUtil` 里的私有 `RegexSeek` / `RegexSearch`
  副本，统一改用 `VbCompat` 的公开版本。
- `pcl-next/Modules/Minecraft/ModDownload.vb`：新增区域「加载器列表 | 取数与解析已迁至 C#」，
  六个 `*Main` 改为调 C# 并投影为原 `Dl*Result`；删除已无人调用的 `DlSourceLoader` / `DlSourceLoaderCancel`
  （净 −416 行：+107 / −523）；补回误删的 `DlOptiFineListBmclapiLoader` / `DlFabricApiLoader` / `DlOptiFabricLoader`。
- `pcl-next/Pages/PageDownload/ModDownloadLib.vb`：删除 `McDownloadForgeRecommendedGet`（−19 行），
  推荐版本改由 C# 读同一份 `Cache/ForgeRecommendedList.json`。
- `PCL.Download.Tests/LoaderListTests.cs`（新增，571 行，20 例）。

### 4.4 第四轮：实例 Mod 页（本地列表与在线反查）

- `PCL.Download/LocalResourceFile.cs`：新增 `PrimeHashes(modrinthSha1, curseForge)`。VB 的指纹有跨运行的
  `Cache\ModHash.ini` 缓存，C# 的缓存只在进程内——不灌入的话每次启动都要把全部 Mod 重读一遍算哈希。
- `PCL.Download/LocalResourceOnlineLoader.cs`：`Load` 增加 `isCancelled` 谓词，被取消时返回 `null`
  （与 `LoaderListClient` 的宿主口径一致）；取消判定收敛到内部 `CancelState`（谓词与 `CancellationToken` 取或），
  `catch` 过滤器一并改用它，避免把谓词取消误判成「站点失败」。
- `pcl-next/Modules/Resource/LocalResourceLoaders.vb`：`LocalResourceFileLoad` 改调 `LocalResourceScanner.Scan`
  （Forge 1.13- 的版本号子目录规则以 `Options.IsLegacyForgeSubfolder` 传入），`LocalResourceOnlineLoad`
  改调 `LocalResourceOnlineLoader.Load` 并做双向 DTO 投影；保留等待更新、UI 文案、缓存版本校验与落盘、边栏刷新。
  净 −206 行（345 → 139，+63 / −269）。
- `pcl-next/Application.xaml.vb`：挂接 `PCL.Download.DownloadLog.InfoSink/WarnSink` → `Logger`（见 §5-15）。
- `PCL.Download.Tests/LocalResourceOnlineLoaderTests.cs`：新增 4 例（加载器集合不一致不算更新、
  站点失败不写「已加载」标记、取消返回 null、宿主预置指纹生效）。

### 4.5 第五轮：安装编排的纯逻辑

安装编排（`ModDownloadLib`）与 `LoaderBase` / `LoaderTask` / `McInstance` 深度耦合，整体搬迁会同时牵动
Phase 6 的库与资源分析。本轮只摘**不依赖加载器状态机的纯逻辑**：

- `PCL.Download/VersionMerger.cs`（新增）：`VersionProfile` / `VersionMergeSource` / `VersionMerger`。
  承接 VB `MergeJson`：读各 profile 的同名 json、按叠加顺序深合并、回填 `minecraftArguments`、
  摘掉 `_comment_` / `inheritsFrom` / `jar`、写入新 `id`。文件读写（含建目录与占用重试）也在内核里。
- `PCL.Download/ForgelikeInstaller.cs`（新增）：`ForgelikeVersionTarget` / `ForgelikeInstallerFile` /
  `ForgelikeProgressStep` / `ForgelikeInstaller`，承接四件事：
  `Normalize`（版本串归一化）、`BuildForgeInstaller` / `BuildNeoForgeInstaller`（安装器下载地址）、
  `BuildArguments`（安装器启动参数）、`ReadProgress`（安装器输出 → 进度动作）。
- `pcl-next/Pages/PageDownload/ModDownloadLib.vb`：`MergeJson` 缩成「按安装形态挑选参与合并的目录」；
  `ForgelikeInjectorLine` 只剩取表 + 赋值；`ForgelikeInjector` 的参数拼接改为一次调用；
  `McDownloadForgelikeLoader` 的版本串特例改为一次 `Normalize`；
  「另存为安装器」与「安装」两条路径的四段地址拼接统一到 C#。净 −58 行（+68 / −126）。
- `PCL.Download.Tests/VersionMergerTests.cs`：14 例。
- `PCL.Download.Tests/ForgelikeInstallerTests.cs`：30 例。

### 4.6 第六轮：其余三类加载器的安装数据推导

第五轮把 Forge / NeoForge 的纯逻辑摘出来之后，剩下三类的安装路径上还有一批「拼字符串」的逻辑，
各自有两个衍生问题：OptiFine 的下载源推导在「安装」与「另存为」里各写了一遍（逐字相同，已经开始漂移），
LiteLoader 的版本 json 是一段 30 行的字符串拼接，Fabric 的 `∞ → infinite` 替换与实例名生成顺序耦合。

- `PCL.Download/OptiFineInstaller.cs`（新增）：`OptiFineJavaProbe` / `OptiFineInstaller`。
  `ProbeRequiredJava` 读 jar 内 `optifine/Installer.class` 的 class 版本号推出所需 Java 主版本；
  `BuildArguments` 拼启动参数；`IsFailed` 判成败；`ProgressPerLine` 收掉 `0.7 / 7000` 这个魔数。
- `PCL.Download/OptiFineProfile.cs`（新增）：`OptiFineLegacyProfile` / `OptiFineProfile`。
  `BuildMirrorUrl`（含 1.8 / 1.9 目录补 `.0`、预览版路径形状）、`BuildOfficialPageUrl` /
  `FindOfficialDownloadUrl`（从官方页抠真下载地址）、`BuildInstallerTarget`（落临时目录还是留 libraries）、
  `BuildLegacyProfileJson`（方式 B 的版本 json）。
- `PCL.Download/LiteLoaderProfile.cs`（新增）：`BuildVersionJson` 由条目原文构造版本 json，
  含「条目自带 `libraries` + 追加 `com.mumfrey:liteloader`」这条隐式规则。
- `PCL.Download/FabricProfile.cs`（新增）：`BuildInstanceName` / `BuildProfileUrls`。
- `pcl-next/Pages/PageDownload/ModDownloadLib.vb`：
  `McDownloadOptiFineInstall` 的探测 / 参数 / 进度 / 成败四段改为调用；
  新增 `McDownloadOptiFineSources` 收敛两处重复的源推导；
  `McDownloadOptiFineLoader` 的 `Target` 与方式 B 的 json 各改一次调用；
  `McDownloadLiteLoaderLoader` 的整份 json 拼接收成一次调用；`McDownloadFabricLoader` 的 id 与地址各一次。
- `pcl-next/Modules/Resource/ResourceProject.vb`：`FromProjectIds` 改调 C# 批量接口，逐项投影并回填本地快查缓存。
- `PCL.Download.Tests/OptiFineInstallerTests.cs`：27 例。
- `PCL.Download.Tests/OptiFineProfileTests.cs`：21 例。
- `PCL.Download.Tests/LiteLoaderProfileTests.cs`：11 例。
- `PCL.Download.Tests/FabricProfileTests.cs`：4 例。

### 4.7 后续（未动）

- `pcl-next/Pages/PageDownload/ModDownloadLib.vb`：安装编排的 `LoaderBase` 生成改为 C# 输出。
- `pcl-next/Pages/PageDownload/Resource/PageDownloadCompDetail.xaml.vb`：资源详情版本列表改由 Provider 提供（已通过 `ResourceVersion.FromProjectId` 转发完成）。
- `pcl-next/Modules/Resource/ResourceVersion.vb` / `ResourceProject.vb`：逐步退化为 C# DTO 的只读投影（已开始）。

## 5. 要点

1. **UI 契约优先不变**：`DlClientListResult.Value` 仍是 `JObject`，页面四处读取点零改动。
   C# 返回合并特供项之后的清单原文，保证「特供版本」语义不丢。
2. **候选排期逐字对齐** `DlSourceLoader` 的 `WaitCycle` 语义：第一个源立即启动，
   其后的源在「切换时刻」启动；先完成者胜出，失败不提前切换（与原实现一致）。
3. **目标版本校验的差异（已注明）**：VB 只在镜像分支、且用上一次主加载器的旧结果判断；
   C# 改为对每个候选各自校验。语义更严格，且不再依赖调用顺序。Mojang 子加载器不传该校验，
   保持 `FormMain` 预热调用 `Start(1)` 的原有行为。
4. **官方源耗时探测**保留在 VB：`DlPreferMojang` 影响后续下载源顺序，属跨模块状态，
   由 C# 返回 `ElapsedMilliseconds`，VB 判定 `< 4000ms`。
5. **直连请求**：MC 下载源是两条互不映射的独立地址，不能走 `ResourceSourceResolver`
   （否则会被当成「无镜像的官方源」重试两次，超时不可控）。
6. **缓存是两份**：C# 与 VB 各有一份版本 / 工程缓存。刷新按钮必须走 `ResourceVersion.ClearCaches()`，
   只清 VB 那份会让「刷新」拿到 C# 的旧列表；版本枚举内部还会查 C# 的工程缓存（取资源类型），
   所以工程缓存也要一起清。
7. **枚举同名歧义**：VB 的 `ResourceVersion.ModLoaders` 字段与类型同名，
   `CType(x, ModLoaders)` 会被解析成字段，必须写成 `CType(x, PCLCS.ModLoaders)`。
8. **VB 默认构造**：给类补了带参构造后，VB 不再自动生成无参构造，
   JSON 解析路径（`FromPlatformJson` / `FromCacheJson`）会立刻编译失败，需显式补 `Public Sub New()`。
9. **HTML 需要独立的文本通道**：`IDirectResourceApi` 只返回 `JsonNode`，而 OptiFine 官方页与 Forge 版本列表页
   是 HTML。硬走 JSON 通道会在解析阶段抛 `Parse` 并被当成「官方源坏了」而换源，故新增
   `IDirectTextResourceApi`，只搬字节不解析；`SendOnceText` 统一做传输与错误归类，JSON 与 HTML 两条通道共用。
10. **竞速逻辑只留一份**：`SourceRace` 从版本清单里提出来，六类加载器列表复用。语义逐字对齐 VB 的 `WaitCycle`：
    第一个候选立即启动、其余按 `StartAtSeconds` 到点启动、先成功者胜、**已启动的全部失败则立即启动下一个**
    （`BeforeLoadersAllFailed` 加速）、单源 45s 兜底。两套调度并存必然漂移，所以宁可先重构再加功能。
11. **缓存对齐「子加载器结果复用」**：旧实现是把 `LoaderTask.Output` 留在内存里复用；迁到 C# 后按
    `Kind|参数|后缀` 在 `LoaderListClient` 实例内缓存，`forceRestart` 绕过。VB 侧是模块级单例，
    因此进程内可见行为与旧实现一致。
12. **LiteLoader / Fabric 要透传原文**：安装编排要从 LiteLoader 条目里读 `tweakClass` / `libraries` / `version`，
    强类型化会丢字段，故 `LiteLoaderListEntry.RawJson` 原样保留、VB 侧 `JToken.Parse` 回填 `JsonToken`；
    Fabric 整份 JSON 由 UI 直接读 `game` / `loader` / `installer` 三个数组，C# 只校验不缺段。
13. **两个历史包袱照原样保留**：Forge 版本列表的「司马版本」分支补全与 1.7.10 分支注入、
    LiteLoader 的 `1.8` / `1.9` 文件名补 `.0`——都是旧安装器命名遗留，改了会让已装实例反查不到。
14. **OptiFine 单镜像源入口**：实例重构与整合包安装反查要「全量且稳定」的清单（官方页会删旧预览版），
    故 `FetchOptiFineMirror` 固定走 BMCLAPI，不受「版本列表源」设置影响，缓存后缀 `mirror` 与主入口分开。
15. **内核日志此前是黑洞**：`DownloadLog` 只在 `PCL.Cli` 里被挂过 sink，GUI 从未挂接，
    Phase 3/4 迁进 C# 的候选源、资源站请求、加载器列表、本地反查日志全部写进 `Console`（WPF 里等于丢弃）。
    用户反馈问题时被要求附 `Log1.txt`，但这些记录一行都不会出现。本版在 `Application_Startup` 里挂接。
16. **指纹必须由宿主灌入**：`Cache\ModHash.ini` 的跨运行缓存是 VB 侧的资产，C# 侧只有进程内缓存。
    不 `PrimeHashes` 的话，升级后第一次打开 Mod 页会把整个 mods 目录重读一遍算 SHA1 + MurmurHash
    （200 个 Mod 的整合包约 1~2 GB 读取），表现为「卡在正在加载 Mod 列表」。
17. **投影而不是 JSON 中转**：VB `LocalResourceFile` 带 `OnUpdate` 事件与跨运行指纹缓存，且
    `Project` / `UpdateFile` 的 setter 会触发 UI 刷新；双向都用逐字段投影（`New ResourceProject(Core)` /
    `New ResourceVersion(Core)`），只有缓存 JSON 需要 `Newtonsoft ↔ System.Text.Json` 转一次。
18. **缓存落盘仍归宿主**：C# `Load` 只就地改传入的 `JsonObject`，写文件、`version` 字段校验与
    `ModeDebug` 缩进都在 VB——与 `LocalResourceOnlineLoader` 的类注释一致，也避免内核依赖宿主路径约定。
19. **Newtonsoft 的 `JObject.Merge` 语义不会随 STJ 一起继承**，必须逐条复现，否则合并出来的 version json
    依然是一份合法 JSON，但启动时才炸：
    - 数组是**拼接**（目标在前 + 来源追加），不是覆盖——`libraries` / `arguments.game` 全靠这条；
    - 来源值为 `null` 时**忽略**（加载器 json 里常见 `"assets": null`，不能把原版的值清掉）；
    - 对象**递归**合并，同名对象继续往下钻；
    - 其余情况（标量、类型不同）直接替换。
    另外 `releaseTime` / `time` 属于「这份 profile 什么时候生成的」，合并前必须从加载器侧剔掉，
    否则版本列表显示的是加载器的构建时间。
20. **`minecraftArguments` 的取值要吸附到 flag 上**：旧版启动器只认这条扁平字符串，加载器往里塞
    `--tweakClass xxx` 这类成对参数。直接按空格切开再 `Distinct` 会把 flag 与取值拆散——
    两个加载器都写 `--tweakClass` 时，第二个取值会变成孤立参数（客户端直接崩）。故先把
    「紧跟 flag 的第一个非 flag 词」吸附上去再去重；多个取值时只吸附第一个（与 VB 一致，
    交给启动器按空白切分，结果等价）。
21. **合并写文件要保留建目录与重试**：VB 的 `FileUtils.Write` 会建父目录、并在 `IOException` 时重试
    （200ms）。版本 json 可能被杀毒/索引服务短暂占用，丢掉重试会把「偶发占用」变成安装失败。
22. **Forge 快照版本在 maven 里用下划线**（`1.20.2_pre1`），这条替换原先只写在「安装」路径，
    「另存为安装器」路径漏了——快照版本另存会拼出 404 地址。本轮统一到一处并顺手修掉；
    这是本轮唯一的**行为变更**（对外可见结果：快照版本的安装器另存从必失败变为可用）。
23. **安装器进度表是精确文案匹配**：`"  File exists: Checksum validated."` 有两个前导空格、
    `"Splitting: "` 有一个尾部空格，写错就只是「进度条不动」，不报错、难发现，故逐字搬进 C# 并加用例锁住。
    其中校验命中那条是**累加**（`+= 0.003`）而不是赋值——安装器会连打很多行，改成赋值进度会来回跳。
24. **安装器启动参数的两个易错点**：classpath 用 `;` 拼（Windows 分隔符）、Java 9 起必须补
    `--add-exports cpw.mods.bootstraplauncher/...=ALL-UNNAMED`（模块系统默认不导出内部包）。
    JavaWrapper 模式下多一个 `-Doolloo.jlw.tmpdir` 与 `-jar`；拿不到 wrapper 路径时要退回普通模式，
    不能把空路径拼进去。
25. **OptiFine 的 Java 版本是靠读 class 文件头推出来的**：`optifine/Installer.class` 的第 6、7 字节是
    class 版本号，减 44 即 Java 主版本（52 → 8，61 → 17）。原实现把探测整段包在 `Try` 里吞掉异常，
    任何失败都只是「按 21 处理」，用户装错 Java 时看不出卡在哪一步；现在失败原因（找不到条目 /
    文件头不对 / 长度不足 / 版本超范围）会带回日志。
26. **OptiFine 的候选源推导原先有两份**：「安装」与「另存为」各写了一遍 BMCLAPI 地址拼接，
    逐字相同但没有共享，改一处就会让两个入口打到不同地址（Forge 的地址拼接就是这样漂移的）。
    现在收敛到 `OptiFineProfile` + VB 侧一个 `McDownloadOptiFineSources`。
27. **官方下载页抠不到链接时不要硬拼地址**：`optifine.net/adloadx?f=...` 返回的是 HTML，
    真正的地址藏在页面里的 `downloadx?f=...`。原实现用 `RegexSearch(...).First`，匹配不到会抛
    `IndexOutOfRange`，被外层 `Try` 吞掉后照样走镜像；现在显式返回 null 并记一条 warn，
    而不是拼一个必然 404 的地址去下几百字节的错误页——那种失败只会在「最小体积」检查处暴露。
28. **OptiFine 方式 B 的 json 有两处不能「顺手修正」**：时间戳的时分秒写死 `T23:33:33+08:00`
    （列表只给到日期），以及新格式的 `minimumLauncherVersion` 是**字符串** `"21"`、旧格式是数字 `18`。
29. **LiteLoader 的版本 json 里 `libraries` 的顺序是语义**：条目自带的那几条必须在前，
    `com.mumfrey:liteloader` 追加在后且必须带 `url`。少了 url 启动器解析不了坐标，
    顺序颠倒则可能被后面的同名库覆盖。构造时必须**深拷贝**条目里的数组，否则会把这条写回列表条目。
30. **Fabric 的 `∞ → infinite` 只用于请求地址**：实例名（= 版本文件夹名 = json 文件名）要保留原始串，
    否则实例列表里对不上。两者由 `FabricProfile` 一并给出，避免以后有人把替换提到前面。

## 6. 回归面

- 安装页版本列表：三种「版本列表源」设置下的候选顺序与切换时机。
- `FormMain` 启动预热（`DlClientListMojangLoader.Start(1)`）。
- `DlClientListGet` 在版本刚更新时的强制刷新路径（`WaitForExit(Id, IsForceRestart:=True)`）。
- MC 更新提示：快照 / 正式版各弹一次，`ToolUpdate*Last` 记录正确。
- 特供版本：`Cache/download.json` 存在时被合并，重复 id 不覆盖原记录。
- 修改页（`PageDownloadModify`）复用同一清单，Drop 分组不变。
- 资源详情页：CF / Modrinth 两种平台的版本列表、加载器标签、文件大小与下载按钮的可用态。
- 本地已装 Mod 的 `Version` / `UpdateFile` 缓存字段（`ToCacheJson` 依赖 `DependencyIds`，丢字段会导致更新检查失效）。
- 刷新按钮（下载页左栏、实例 Mod 页）：必须同时清 C# 缓存，否则仍显示旧列表。
- 整合包安装的工程批量查询：本轮未改，仍走 VB `FromProjectIds`，属独立回归项。
- 加载器列表六类：三档「版本列表源」设置下的候选顺序与切换时刻（0=镜像 0s/官方 30s、
  1=官方 0s/镜像 5s、2=官方 0s/镜像 60s）。
- 安装页安装加载器时六种列表的条目数、显示名、`Inherit`（版本名）与「可下载」判定（尤其 OptiFine 预览版、
  NeoForge 的 `47.1.82`、LiteLoader 的 `1.8` / `1.9`）。
- Forge 版本列表：官方源（HTML）与镜像源（JSON）切换后推荐版本标记一致、`FileVersion` 与 MD5 正确。
- 实例重构 / 整合包安装的 OptiFine 反查：走 `DlOptiFineListBmclapiLoader` 单镜像源，不随设置变化。
- 强制刷新：`Loader.IsForceRestarting` 为真时必须重新联网（`forceRestart` 绕过缓存）；
  下载页左栏与实例 Mod 页的刷新按钮行为不变。
- 实例 Mod 页：本地列表条数 / 顺序 / 启用禁用识别、Forge 1.13- 的「版本号子文件夹」收录规则、
  同名启用+禁用两份只显示一份。
- 实例 Mod 页的联网反查：工程名、图标、版本号、「可更新」标记、更新日志链接；缓存命中时（6 小时内）
  不联网但仍显示信息；强制刷新时重新联网。
- `Cache\LocalMod.json` 的跨版本兼容：本版改由 System.Text.Json 写出，旧缓存（Newtonsoft 写）仍要能读。
- Mod 页的「切换启用/禁用」重命名后 `NewModEntity.FromJson(ModEntity.ToJson)` 仍要保住网络信息。
- `Log1.txt` 里能看到 `PCL.Download` 的日志（如「本地资源在线反查：N 个文件需要联网」）。
- 版本合并：安装 原版 + OptiFine + Forge + LiteLoader 的组合实例，合并后的 json 里
  `libraries` / `arguments` 是各方之和、`mainClass` 取最后一个加载器、`id` 为新版本名、
  没有 `inheritsFrom` / `jar` / `_comment_`。
- 版本合并后能正常启动游戏（这是合并正确性的**唯一**可信判据，单测只能守结构）。
- Forge 安装：Java 8 与 Java 17+ 各一次（后者走 `--add-exports`）；启用/禁用 JavaWrapper 各一次。
- NeoForge 安装：1.20.1（走 `net/neoforged/forge` 包名）与 1.20.4+（走 `neoforge`）各一次。
- 快照版本 + Forge（如 `1.20.2-pre1`）能下到安装器（下划线路径）。
- 「另存为安装器」对 Forge / NeoForge 都能拿到文件，且与「安装」路径用的是同一份地址。
- 整合包安装传入 `1.19.3-41.2.8` 这种自带 MC 版本的 Forge 串时，实例的 `inheritsFrom` 是 `1.19.3`
  而不是整合包声明的值。
- OptiFine 安装：1.14+（方式 A，走临时目录 + 安装器）与 1.12.2 及更早（方式 B，只拼 json）各一次；
  方式 B 装完能启动（`--tweakClass optifine.OptiFineTweaker` 生效，光影可用）。
- OptiFine「另存为」：官方源可用时两个源都在候选里，官方页取不到链接时只剩镜像且日志有 warn；
  1.8 / 1.9 两个老版本的镜像地址带 `.0`（#4281），预览版的路径形状与正式版不同。
- OptiFine 安装器要求的 Java 版本：1.20+ 的安装器应识别为 Java 21 而不再一律按默认值下载。
- LiteLoader 安装：1.12.2 与 1.7.10 各一次，版本 json 里 `libraries` 含条目自带的几条 +
  `com.mumfrey:liteloader:<version>`（带 `url`），且能启动。
- LiteLoader 列表多次打开后再安装：同一份列表条目在两次安装之间不被上一次的追加污染。
- Fabric 安装：`1.20.1` 与 `1.20.1∞` 各一次，后者实例文件夹名保留 `∞`、请求地址走 `infinite`。
- 整合包安装的工程批量查询：Mod 缺失列表一次查询回来，重复查询走缓存不再联网。

## 7. 验证

- `dotnet build pcl-next.sln -c Release`：0 错误。
- `PCL.Download.Tests`：新增 15 例（候选排期 3、解析与竞速 7、Drop 2、直连 2、特供合并 1），全绿。
- 反向验证：把 `McVersionSources.Build` 的 `OfficialFirst` 首个源改成 `Mojang(5)`（错把切换时刻当启动时刻），
  `Build_OfficialFirst_StartsOfficialImmediatelyThenMirrorAt5s` 必须失败。
- 第二轮改动的等价性无法单测（VB 投影层），依赖：全量 `PCL.Download.Tests` 通过 + 详情页真机冒烟
  （CF / Modrinth 各点开一个工程，核对版本数、加载器标签、文件大小）。
- 第三轮（加载器列表）：`PCL.Download.Tests` 新增 `LoaderListTests` 20 例（OptiFine 4、Forge MC 版本 2、
  Forge 版本 5、NeoForge 2、LiteLoader 1、Fabric 2、竞速/缓存/取消 4），全量 295 例全绿。
- 反向验证（把实现改坏，确认对应用例必失败，事后逐条还原并复跑全绿）：
  ① 去掉 `SourceRace` 的「已启动源全部失败 → 立即启动下一个」加速 →
  `Race_StartsNextCandidateImmediatelyWhenAllStartedFailed` 失败（5s → 必须 <4s 的那条断言）；
  ② Forge 分类优先级把 installer 降为与 universal 同级 → `ForgeVersions_Json_InstallerBeatsUniversalInSameEntry` 失败；
  ③ 不再过滤不可下载的 NeoForge `47.1.82` → `NeoForge_NormalizesThreeVersionNameShapes` 失败；
  ④ LiteLoader 不再给 `1.8` / `1.9` 补 `.0` → `LiteLoader_ParsesEntriesAndKeepsRawToken` 失败；
  ⑤ 忽略 `forceRestart`（缓存永不失效）→ `Cache_ServesSecondCallWithoutNetworkUntilForceRestart` 失败。
- 反向验证抓到一个真实缺口：原 Forge 夹具里 installer 与 universal **从不同时出现**，
  于是「installer 优先于 universal」这条跨分类规则没有任何用例守着（②第一次没失败）。
  已补夹具 `ForgeVersionsMixedFilesJson`（同一版本同时挂 installer + universal，两种数组顺序各一条）
  与用例 `ForgeVersions_Json_InstallerBeatsUniversalInSameEntry`，再跑②确认守住。
- 另一个坑记一笔：用脚本做「改坏 → 还原」时不能只 `copy2` 回原文件——它会带回旧 mtime，
  MSBuild 判定「无需重编」，后续用例实际跑的是**变异后的程序集**，会得出「用例没守住」的假结论。
  还原后必须刷新时间戳。
- 第四轮（实例 Mod 页）：`PCL.Download.Tests` 新增 4 例（`LocalResourceOnlineLoaderTests` 8 例中新增
  加载器集合不一致、站点失败不写标记、取消返回 null、宿主预置指纹），全量 300 例。
- 第四轮反向验证（同一套脚本口径，改坏 → 跑对应用例 → 还原并刷新时间戳）：
  ① `IsCacheFresh` 恒 false → `Load_FreshCacheSkipsNetwork` 失败；
  ② 去掉 `update.Loaders.SequenceEqual(...)` 判定 → `Load_ModrinthUpdateWithDifferentLoaders_IsIgnored` 失败；
  ③ `entry.OnlineDataLoaded = !failed` 改成恒 `true` → `Load_RequestFailed_DoesNotMarkCacheAsLoaded` 失败
  （这条直接对应「失败后下次启动还会不会重试」）；
  ④ `CancelState.IsCanceled` 只看 `CancellationToken` → `Load_Canceled_ReturnsNullAndStops` 失败；
  ⑤ `PrimeHashes` 丢掉宿主预置的 SHA1 → `PrimeHashes_OverridesFileDerivedFingerprints` 失败。
  5 条全部被抓住，说明这四个用例确实在守逻辑而不是空跑。
- 全量 300 例中 299 通过，唯一失败是 `SlowSource_IsRotatedToFastSource` 的
  `Assert.Contains(logs, "换源" | "禁用")`（不是「第二源未被使用」那条）。该例单独运行必过；
  把并行工作线的 `DownloadFileTask.cs` 改动暂存回基线后，全量跑同样只有这一条失败——
  属时序敏感用例受同目录并行构建的机器负载影响，与本轮改动无关，留待引擎侧处理。
- 顺手修了一个长期偶发失败：`DownloadEngineTests.SlowSource_IsRotatedToFastSource`。
  根因不是引擎，而是夹具——假服务器每 64KB 才 flush 一次，而「最后一块到达」会先走完成分支、
  跳过速度检查；300KB 文件一旦被拆成 4 个 75KB 分块，每块只写两次，「速度过慢换源」永远不会被判定。
  改为 1MB 文件 + 512KB/s 阈值，使任意分块数下都必然多次写入并触发判定；断言顺序也改为
  先验证「第二源被使用」这一核心行为，再匹配日志（速度换源 / 死源禁用两条路径文案不同）。
- 第五轮（安装编排的纯逻辑）：`PCL.Download.Tests` 新增 44 例（`VersionMergerTests` 14、
  `ForgelikeInstallerTests` 30），全量 **344 例全绿**（含此前偶发的 `SlowSource_IsRotatedToFastSource`）。
- 第五轮反向验证（7 条变异，同一套脚本口径）：
  ① 数组改为「覆盖」而非拼接 → `Merge_数组拼接而非覆盖` 失败；
  ② 不再忽略来源 `null` → `Merge_来源为null时保留目标已有值` 失败；
  ③ 去掉「参数值与 flag 吸附」→ **第一次没失败**，见下；
  ④ 快照版本的横线不再换成下划线 → `BuildForgeInstaller_快照版本的横线换成下划线` 失败；
  ⑤ 无条件加 `--add-exports` → `BuildArguments_Java8不加模块导出参数` 失败；
  ⑥ 校验命中的进度改为赋值 → `ReadProgress_校验命中时是累加而非覆盖` 失败；
  ⑦ NeoForge 1.20.1 不再补包名前缀 → `Normalize_NeoForge_为1点20点1补齐包名前缀` 失败。
- ③ 抓到的缺口：原来的用例只断言「结果包含 `--username ${auth_player_name}`」「`--username` 只出现一次」，
  而变异后这两条**仍然成立**——去重前的 token 序列恰好让断言无法区分。真正能暴露问题的是
  「两份 profile 的同一个 flag 取值不同」：`--tweakClass a` + `--tweakClass b`，不吸附就会退化成
  `--tweakClass a --demo b`（`b` 成为孤立参数）。已补 `Merge_每个flag的取值各自绑定_不被去重拆散`，
  再跑③确认守住。**这是本轮最有价值的一条**：吸附逻辑原本只是「看起来对」。
- 脚本坑（与 dev.4 同一个坑的镜像面）：还原时若把 mtime 写回**原始旧时间戳**，MSBuild 会认为源码比
  变异后的 dll 还旧，于是不重编，最后那次全量跑用的仍是变异程序集，会显示一条假失败
  （本次表现为 `Normalize_NeoForge_为1点20点1补齐包名前缀` 失败）。还原后必须 `os.utime(path, None)`。
- 第六轮（OptiFine / LiteLoader / Fabric 的安装数据推导）：`PCL.Download.Tests` 新增 63 例
  （`OptiFineInstallerTests` 27、`OptiFineProfileTests` 21、`LiteLoaderProfileTests` 11、
  `FabricProfileTests` 4），全量 **407 例**。
- 第六轮反向验证（11 条变异，全部**一次**抓住）：
  M1 去掉 Java 9+ 的 `--add-exports`；M2 class 版本偏移改成 52；M3 行数下限判定改成 `<=`；
  M4 镜像侧 1.8/1.9 不再补 `.0`；M5 预览版路径不再把空格换斜杠；M6 `minimumLauncherVersion` 写成数字；
  M7 抠不到官方链接照样拼地址；M8 LiteLoader 不深拷贝；M9 liteloader 库插到最前；
  M10 实例名也替换 `∞`；M11 Fabric 候选源顺序颠倒。
  对应失败用例与变异一一对应，没有出现「改了不报错」的情况——这一批逻辑的共同点是出错不抛异常，
  所以每条都配了能区分行为的断言（如「输入文档未被改动」「库顺序」「null 而不是拼地址」）。
- 第六轮全量结果：**406 / 407**。唯一失败是 `DownloadEngineTests.SlowSource_IsRotatedToFastSource`
  （并行工作线正在改 `DownloadFileTask.cs` / `DownloadEngineTests.cs`，工作区是脏的；
  这条用例此前已多次被判定为时序敏感）。与本轮改动无关：本轮没有触碰下载引擎任何文件。

## 8. 回滚

本轮不删除任何 VB 代码路径的公开入口：`DlClientListMojangLoader` / `DlClientListBmclapiLoader` 与
六个 `Dl*Loader`（含 `DlOptiFineListBmclapiLoader` / `DlFabricApiLoader` / `DlOptiFabricLoader`）全部保留，
`Dl*Result` 结构与 UI 读取方式零改动。若 C# 侧出问题：

- 版本清单：把 `DlClientListMojangMain` / `DlClientListBmclapiMain` 的方法体换回 `DlSourceLoader` 排期。
- 加载器列表：`DlSourceLoader` / `DlSourceLoaderCancel` 本轮已随六类入口迁移一起删除（全仓已无引用），
  回滚需从 git 历史取回这两个方法，再把六个 `*Main` 的方法体还原。**这是本轮唯一的「删代码」动作**，
  回滚成本比前两轮高，故在此明确标注。
- 无数据结构变更、无配置迁移、无缓存格式变化。

资源详情页同理：`ResourceVersion.FromProjectId` 与 `ResourceProject.FromProjectId` 的 VB 原实现
（缓存 + `DlModRequest` 两条分支）保留在 git 历史中，回滚只需还原方法体，
`Friend Sub New(Source ...)` 与 `ClearCaches` 可留着不删（不参与原路径）。

第四轮（实例 Mod 页）同样只删了 VB 的网络实现，入口与数据结构都在：

- 回滚只需把 `LocalResourceLoaders.vb` 还原到上一版（419 行 → git 历史），
  `LocalResourceScanner` / `LocalResourceOnlineLoader` 留在内核不参与即可。
- 缓存格式未变：`Cache\LocalMod.json` 的键、`version` 字段与各子字段名都没动，只是写出方
  从 Newtonsoft 换成 System.Text.Json；日期仍是 ISO 8601，Newtonsoft 能正常解析，
  所以回滚后旧程序读新缓存、新程序读旧缓存都不会炸。
- `LocalResourceFile.FromJson` / `ToJson` 仍留在 VB 侧（`PageInstanceMod` 切换启用/禁用时用它复制网络信息），
  没有被删。
- 新增的 `PrimeHashes` 与本轮引入的 `isCancelled` 参数都是可选/附加的，回滚后无人调用即可。

第五轮（安装编排的纯逻辑）**没有删除任何公开入口**，VB 侧的 `MergeJson` / `ForgelikeInjectorLine` /
`McDownloadForgelikeLoader` / `McDownloadForgelikeSave` 签名全部保留，只换了方法体：

- 回滚只需把 `ModDownloadLib.vb` 的这三处方法体还原到上一版（git 历史），
  `VersionMerger` / `ForgelikeInstaller` 留在内核不参与即可。
- 一个**不可无脑回滚**的点：第 22 条的「快照版本换成下划线」是修 bug，回滚会把
  「另存为安装器 + 快照版本」的 404 一起带回来。若确需回滚，建议只回滚调用点、保留 C# 内的修正。
- 无数据结构变更、无缓存格式变化、无配置迁移；合并产物的 json 字段与旧版一致
  （序列化改用 System.Text.Json，缩进风格可能有细微差别，解析结果相同）。
