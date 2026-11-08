# 计划：Phase 4 首步——资源搜索数据源改由 Provider 提供（dev.4 / §2.1 C）

> 依据：《dev.4公布标准.md》§2.1 C、《PCL2_CSharp下载服务迁移计划.md》§5 Phase 4、《AGENTS.md》§3.2。
> 状态：已完成（dev.4）。本文件随版本归档；Phase 4 主体（安装流程、任务列表）顺延 dev.5。

## 1. 目标

把「下载 → 资源列表」页（`PageResource`，覆盖 Mod / 整合包 / 资源包 / 光影 / 数据包五类）的**搜索与列表数据源**从 VB 的 `ResourceSearcher.Run` 切换到 C# 侧 `ResourceProviderRegistry.Search`：

1. 页面层只与 `ResourceProviderRegistry` 打交道，不再直接调用 VB 的联网搜索；
2. 双站融合、`IsLike` 跨站去重、评分排序**仍由 C# 的 `ResourceSearcher` 完成**（dev.3 平移版），registry 只做「源收敛 + 入口收敛」，不复制融合逻辑；
3. 页面 UI、详情页跳转、翻页交互保持不变——本步只换数据源，不改交互。

**退出标准**：搜索 / 翻页 / 空结果 / 单源失败提示的行为与切换前一致；CF 未配置 Key 时不出错且只走 Modrinth；`pcl-next.sln` Release 0 错误；`PCL.Download.Tests` 全绿。

## 2. 前置条件与阻塞项

| 依赖 | 状态 |
|---|---|
| `IResourceProvider` / CF、MR 适配器 / 候选源与签名（A1-A3） | ✅ `fb7fccc` |
| CF Key 降级与可展示文案（A4） | ✅ `fb7fccc`（C# 侧）+ 本步（UI 接线） |
| C# 侧 `ResourceSearcher`（融合/去重/排序） | ✅ dev.3 `83ebc01` |
| `PCL.Download` 被 `pcl-next.vbproj` 引用 | ✅ 已有 ProjectReference |
| 真机网络验证（B5） | 由用户执行，不阻塞代码 |

无硬阻塞项。

## 3. 现状分析

- 切换前：`PageResource.Loader` = `ResourceSearcher.GetLoader`，搜索全程在 VB（`ResourceSearcher.Run` → `ModDownload.DlModRequest`）。C# 侧虽然已有完整能力，但**没有任何调用方**。
- **关键缺口（本步发现）**：宿主从未设置 `ResourceApi.Default`。C# 侧所有联网方法（Provider、`LocalResourceOnlineLoader`、`ResourceProject.FromProjectId`）都经 `ResourceApi.Resolve` 取默认请求器，未注入时直接抛「未注入 IResourceApi」。也就是说 A 项虽然代码齐全，真机上 C# 联网路径是断的——本步第一件事就是补注入。
- 两套 `ResourcePlatforms` / `ResourceTypes` / `ModLoaders` 枚举（`PCLCS` 与 `PCL.Download`）数值一致，跨侧调用按整数转换，不新增映射表。
- VB `ResourceProject` 的缓存分支（JSON 构造）不读 `UnsafeGameVersions`，因此本轮**不用 JSON 中转而用逐字段构造**，避免该字段丢失影响 Modrinth 的版本二次筛选。

## 4. 改动文件清单

C#（`PCL.Download/`）：

| 文件 | 改动 |
|---|---|
| `ResourceProviderRegistry.cs` | 新增 `Search(request, out notice)` / `Search(request, requestedSources, out notice)`；保存构造时传入的 `IResourceApi`；`CreateDefault` 把它一并传入 |
| `PCL.Download.Tests/ResourceProviderTests.cs` | 新增 3 个用例：降级 + 跨页累积、尊重调用方选源、全源不可用抛错 |

VB（`pcl-next/`）：

| 文件 | 改动 |
|---|---|
| `Modules/Resource/ResourceSourceGate.vb` | 新增 `EnsureInitialized`（注入 `ResourceSigningOptions.Default` + `ResourceApi.Default`），暴露 `Registry` |
| `Modules/Resource/ResourceProviderSearch.vb` | 新增：与 `ResourceSearcher.GetLoader` 同签名的加载器，内部走 `Registry.Search` 并回填 VB `Storage` |
| `Modules/Resource/ResourceSearcher.vb` | `SearchResult` 新增 `Friend CSharpStorage`（C# 侧翻页状态） |
| `Modules/Resource/ResourceProject.vb` | 新增 `Friend Sub New(Source As PCL.Download.ResourceProject)` 逐字段构造 |
| `Pages/PageDownload/Resource/PageResource.xaml.vb` | `Loader` 改用 `ResourceProviderSearch.GetLoader`；新增 `ApplySourceAvailability`（提示条 + 禁用 CF 选项） |
| `Pages/PageDownload/Resource/PageResource.xaml` | 新增 `HintSource`（MyHint，初始折叠） |
| `Application.xaml.vb` | 启动时调用 `ResourceSourceGate.EnsureInitialized()`，失败只告警不中断 |

## 5. 改动要点

1. **注入顺序**：`EnsureInitialized` 必须在任何资源联网之前完成，放在 `Application_Startup` 的日志初始化之后；失败仅 `Logger.Warn`，资源联网回退 VB（`PageDownload` 之外的旧路径不受影响）。
2. **翻页状态归属**：C# 侧 `SearchResult` 决定 offset/total 与跨页去重，VB 侧只持有引用（`SearchResult.CSharpStorage`）。页面重建 `Storage` 时引用自然清空 → 下一次搜索从头开始，与原有「`Storage = New SearchResult`」语义一致。
3. **结果回填采用全量重建**而非增量追加：C# 的 `realResults` 与 VB 列表必须同序，全量重建是最不容易出错的做法（每页最多几百项，开销可忽略）。
4. **PageSize**：取 `TargetResultCount - 已获取数`，下限 40。C# 侧在结果不足且可继续时会自行递归翻页，VB 不需要再管。
5. **源收敛在两处发生**：`LoaderInput`（避免向 CF 发必然 403 的请求）和 `Registry.Search`（防止调用方绕过）。两处幂等，不冲突。
6. **降级提示只展示一次**：`ApplySourceAvailability` 在页面首次加载时执行；源可用性由环境变量决定，进程内不变。

## 6. 回归影响面

| 场景 | 影响 | 保障 |
|---|---|---|
| 五类资源搜索（中/英文、版本/加载器/标签筛选） | 数据源变更，结果集应完全一致 | C# `ResourceSearcher` 是 VB 的平移版；单测覆盖搜索与去重 |
| 翻页 | offset/total 改由 C# 维护 | 新增跨页累积用例（第二页不重复、不丢失） |
| 空结果 / 单源失败 | C# 抛同样的异常与 `ErrorMessage` 文案 | 文案逐字对齐 VB |
| CF 未配置 Key | 原会发 403 请求，现直接裁源 + 提示 | 三条降级用例（含反向验证） |
| 详情页跳转 | 不变（仍用 VB `ResourceProject` 实例） | 构造时逐字段映射，字段集与缓存 JSON 对齐 |
| 版本列表 / 安装流程 | 不变（VB 实现） | 本步不触碰 |

## 7. 验证步骤

1. `dotnet build pcl-next.sln -c Release` → 0 错误；
2. `dotnet test PCL.Download.Tests` → 全绿（含新增 3 例）；
3. 反向验证：把 `Registry.Search` 改为不经过 `ResolveSources`，降级与全不可用两例必须失败（已验证）；
4. 真机：无 `PCL_CURSEFORGE_API_KEY` 时打开「下载 → Mod」，应看到蓝色提示条、源下拉框 CurseForge 项置灰且显示「（未启用）」，搜索只走 Modrinth；
5. 真机：配置 Key 后重启，提示条不出现，源下拉框可正常切到 CurseForge。

## 8. 回滚方案

`PageResource.Loader` 一行改回 `ResourceSearcher.GetLoader(AddressOf LoaderInput)` 即恢复 VB 数据源；VB 侧的 `ResourceProviderSearch`、`CSharpStorage` 与 `ResourceProject(C# 源)` 构造可保留（无调用方即无副作用）。注入点可保留——C# 侧在线反查与后续 Phase 4 仍需要它。
