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
| OptiFine / Forge / NeoForge / LiteLoader / Fabric 列表 | VB 解析（Forge/OptiFine 为 HTML 正则，其余为 JSON） | ⬜ |
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

### 4.3 后续（未动）

- `McLoaderList.cs`（拟）：各加载器列表的统一解析与多源竞速。
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

## 7. 验证

- `dotnet build pcl-next.sln -c Release`：0 错误。
- `PCL.Download.Tests`：新增 15 例（候选排期 3、解析与竞速 7、Drop 2、直连 2、特供合并 1），全绿。
- 反向验证：把 `McVersionSources.Build` 的 `OfficialFirst` 首个源改成 `Mojang(5)`（错把切换时刻当启动时刻），
  `Build_OfficialFirst_StartsOfficialImmediatelyThenMirrorAt5s` 必须失败。
- 第二轮改动的等价性无法单测（VB 投影层），依赖：全量 `PCL.Download.Tests` 通过 + 详情页真机冒烟
  （CF / Modrinth 各点开一个工程，核对版本数、加载器标签、文件大小）。
- 顺手修了一个长期偶发失败：`DownloadEngineTests.SlowSource_IsRotatedToFastSource`。
  根因不是引擎，而是夹具——假服务器每 64KB 才 flush 一次，而「最后一块到达」会先走完成分支、
  跳过速度检查；300KB 文件一旦被拆成 4 个 75KB 分块，每块只写两次，「速度过慢换源」永远不会被判定。
  改为 1MB 文件 + 512KB/s 阈值，使任意分块数下都必然多次写入并触发判定；断言顺序也改为
  先验证「第二源被使用」这一核心行为，再匹配日志（速度换源 / 死源禁用两条路径文案不同）。

## 8. 回滚

本轮不删除任何 VB 代码路径的公开入口：`DlClientListMojangLoader` / `DlClientListBmclapiLoader` 仍在，
`DlSourceLoader` 仍被其余 7 处列表使用。若 C# 侧出问题，把三个 `*Main` 方法体换回原来的 `DlSourceLoader` 排期即可，
无数据结构变更、无配置迁移。

资源详情页同理：`ResourceVersion.FromProjectId` 与 `ResourceProject.FromProjectId` 的 VB 原实现
（缓存 + `DlModRequest` 两条分支）保留在 git 历史中，回滚只需还原方法体，
`Friend Sub New(Source ...)` 与 `ClearCaches` 可留着不删（不参与原路径）。
