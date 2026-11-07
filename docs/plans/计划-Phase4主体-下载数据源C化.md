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
| 工程批量查询（`FromProjectIds`） | VB 实现，仅整合包安装使用 | ⬜ |
| 安装编排（`ModDownloadLib`，2176 行） | VB，与 `MyListItem` / `LoaderTask` 深度耦合 | ⬜ |
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

### 4.4 后续（未动）

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
- 顺手修了一个长期偶发失败：`DownloadEngineTests.SlowSource_IsRotatedToFastSource`。
  根因不是引擎，而是夹具——假服务器每 64KB 才 flush 一次，而「最后一块到达」会先走完成分支、
  跳过速度检查；300KB 文件一旦被拆成 4 个 75KB 分块，每块只写两次，「速度过慢换源」永远不会被判定。
  改为 1MB 文件 + 512KB/s 阈值，使任意分块数下都必然多次写入并触发判定；断言顺序也改为
  先验证「第二源被使用」这一核心行为，再匹配日志（速度换源 / 死源禁用两条路径文案不同）。

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
