# 计划：Phase 3 联网半边（dev.4 / §2.1 A）

> 依据：《dev.4公布标准.md》§2.1 A、《PCL2_CSharp下载服务迁移计划.md》§5 Phase 3、《AGENTS.md》§3。
> 状态：进行中（dev.4）。本文件为专项计划，完成后随版本归档，不作为长期规则入口。

## 1. 目标

补齐资源子系统的**联网半边**，使 PCL.Download 在**不依赖 VB** 的前提下完成：

1. 工程查询（单/批量）、版本枚举、按文件指纹反查、搜索分页——统一由 `IResourceProvider` 暴露（A1）；
2. CurseForge / Modrinth 两个适配器，分页、限流（429 退避）、错误分类可区分（A2）；
3. 候选源解析（官方 + MCIM 多镜像）与请求签名/头注入，与既有下载侧头约定同源（A3）；
4. CF API Key 只读环境变量 `PCL_CURSEFORGE_API_KEY`（禁止硬编码）；缺失时 CF 源降级、由 Modrinth 承接，状态可被 UI 读取，**不抛异常中断**（A4）；
5. `LocalResourceOnlineLoad` 平移：本地文件指纹反查工程与版本、更新检测、下载源合并、更新日志（A5）；
6. 全部调用点收敛到 `HashType` / `HashHelper`，不出现第二套哈希枚举（A6，dev.3 已完成地基，本版只做收口核对）。

**可验证标准**：PCL.Download.Tests 全绿且新增用例覆盖 Provider 路由、Key 缺失降级、429 退避、候选源 fallback；`pcl-next.sln` Release 0 错误；与 VB 同输入对照一致（B5 真机冒烟由用户执行）。

## 2. 前置条件与阻塞项

| 依赖 | 状态 |
|---|---|
| `HashType` / `HashHelper`（含 CF Murmur2 去空白指纹） | ✅ dev.3（`642b6c1`） |
| `ResourceProject` / `ResourceVersion` / `ResourceSearcher` / `LocalResourceFile` / `LocalResourceScanner` | ✅ dev.3（`3e14a61` / `0f30c9a` / `83ebc01`） |
| 下载引擎真机修复（退避 / 对冲 / 死源快判） | ✅ 本版补齐（`ed9ea9e`，dev.3 遗漏未提） |
| CF API Key | 用户环境变量，缺失时走降级路径（不阻塞开发） |
| 真机网络冒烟（B5） | 阻塞于用户执行，不阻塞代码 |

无硬阻塞项。

## 3. 现状分析

- 现状：`IResourceApi` 只是「请求并返回 JSON」的抽象，实现仍在 VB（`ModDownload.DlModRequest`），镜像顺序、超时、重试、`x-api-key` 注入全在 VB 侧。
- 缺口：C# 侧无法独立联网 → Phase 4「下载页数据源改为 Provider」无法开工；`LocalResourceOnlineLoad` 无从平移（它的每一步都是联网）。
- 历史踩坑：
  - MCIM 镜像对 `api.modrinth.com` 的替换规则会先命中 `staging-api` 分支（上游遗留死代码），**平移时保持原状不修**；
  - CF 的 `pageSize=10000` 存在返回缺失（#6224），`ResourceVersion.FromProjectId` 已有三重试逻辑，Provider 必须复用它而不是另写分页；
  - 429 是这套 API 最常见的失败（曾把 bmclapi 打爆导致全线 0 B/s），下载侧已用「冷却而非重试」解决，资源侧沿用同一原则。

## 4. 改动文件清单

新增（`PCL.Download/`）：

| 文件 | 职责 |
|---|---|
| `ResourceSourceResolver.cs` | 候选源解析：官方 / MCIM 镜像、各候选超时，逐字对齐 VB `DlModRequest` 的 `ToolDownloadMod` 分支 |
| `ResourceSigning.cs` | `ResourceSigningOptions`（UA / Referer / CF Key）+ `RequestSigningHandler`（DelegatingHandler 注入头）+ `{CDN}` 标记签名 |
| `HttpResourceApi.cs` | `IResourceApi` 的 HttpClient 实现：候选源轮换、单候选超时、错误分类（网络/超时/4xx/5xx/429/解析） |
| `ResourceProvider.cs` | `IResourceProvider` 接口 + `ResourceSearchPage` / `ResourceFingerprint` / `ResourceFingerprintMatch` |
| `CurseForgeProvider.cs` | CF 适配器（Key 缺失即不可用） |
| `ModrinthProvider.cs` | Modrinth 适配器（永远可用；作为降级承接方） |
| `ResourceProviderRegistry.cs` | 注册表：可用性判定、按平台取 Provider、CF→MR 降级与降级原因文案 |
| `LocalResourceOnlineLoader.cs` | 在线反查与更新检测（平移 VB `LocalResourceOnlineLoad`） |

新增（`PCL.Download.Tests/`）：`ResourceSourceResolverTests.cs`、`ResourceSigningTests.cs`、`HttpResourceApiTests.cs`、`ResourceProviderTests.cs`、`LocalResourceOnlineLoaderTests.cs`。

**明确不做**：

- 不改 VB 现有调用链（Phase 4 才把下载页重指到 Provider）；不改 `ResourceSearcher` 的搜索/去重/排序行为；
- 不实现依赖图求解器、不做 MCIM `sync_at` 新鲜度判断；
- 不新增第三方依赖（PCL.Download 保持纯 BCL）。

## 5. 改动要点

1. **候选源**（`ResourceSourceResolver`）
   - 输入原始 URL + `ModSourcePreference`；输出 `(Url, TimeoutSeconds)` 列表。
   - `PreferMirror(0)`：镜像 10s ×2 → 官方 30s；`Automatic(1)`：官方(modrinth 20s / 其他 10s) → 镜像 10s → 官方 30s → 镜像 30s；`PreferOfficial(2 及其他)`：官方 → 官方 30s → 镜像 30s。
   - 无镜像映射（`ModMirror.Get` 返回原串）时只给官方两个候选。

2. **签名/头注入**（`ResourceSigning`）
   - `RequestSigningHandler` 依次补：`User-Agent`（默认 `PCL2/<版本>.<渠道>`，宿主可覆盖）、`Referer`、命中 `api.curseforge.com` / `forgecdn.net` 且 Key 非空时注入 `x-api-key`。
   - Key 只来自 `PCL_CURSEFORGE_API_KEY`；选项对象不落盘、不打印（日志只输出「已配置/未配置」）。

3. **HTTP 实现**（`HttpResourceApi`）
   - 共享静态 `HttpClient`（`SocketsHttpHandler`，`MaxConnectionsPerServer=64`，`PooledConnectionIdleTimeout=3min`，`AutomaticDecompression`）。
   - 逐候选请求：每候选独立超时；`429`/`5xx`/网络/超时 → 换下一候选；其余 `4xx` → 立即抛（换源无意义）。
   - 全部失败抛 `ResourceRequestException`，内含 `Kind` 与聚合内部异常；日志记录每个候选的失败原因。

4. **Provider 抽象**（`IResourceProvider`）
   - 成员：`Platform`、`IsAvailable`、`UnavailableReason`、`GetProject`、`GetProjects`、`GetVersions`、`LookupFingerprints`、`Search`。
   - 复用而非重写：工程 = `ResourceProject.FromProjectId(s)`；版本 = `ResourceVersion.FromProjectId`（含 #6224 三重试）；搜索 = `ResourceSearcher.Search`（单一 `Sources`，返回 `Offset/Total`）。
   - `LookupFingerprints`：MR 走 `POST /v2/version_files`，CF 走 `POST /v1/fingerprints/432`，结果按输入指纹回填 `ProjectId + File + Loaders`。

5. **降级**（`ResourceProviderRegistry`）
   - CF 不可用（Key 缺失）时：`Sources.CurseForge` 请求自动裁掉该位；若因此为空则退到 Modrinth；`DegradationNotice` 提供「CurseForge 源未启用（未配置 API Key）」文案，**不抛异常**。
   - CF 运行期失败（网络/5xx）不永久降级，只在本次调用返回错误，由调用方决定。

6. **在线反查**（`LocalResourceOnlineLoader`）
   - 步骤对齐 VB：读缓存（键 = `ModrinthHash + VanillaName + ModLoaders`，6 小时时效）→ 挑出待查列表 → MR / CF 并行反查 → 回填 `Project` / `ProjectVersion` → 取更新信息 → 合并下载源与更新日志。
   - CF 不可用时该分支整体跳过（日志留痕），MR 结果照常生效。
   - 不搬运 UI 与 `LoaderTask` 状态机（由宿主 Phase 4 桥接）；以 `CancellationToken` + `IProgress` 表达进度。

## 6. 回归影响面

- 纯新增文件 + 注册表；`ResourceSearcher` / `ResourceProject` / `ResourceVersion` 不改签名 → 既有 211 个用例行为不变。
- `IResourceApi` 接口不新增成员（VB 现有实现继续可用）。
- 风险点：静态缓存（`ResourceProject.Cache` / `ResourceVersion.ProjectFilesCache`）在 Provider 单测间串扰 → 新测试统一进 `[Collection("Resource")]` 串行集。
- 需新增测试：候选源顺序与超时、Key 缺失降级、429 换源、4xx 不重试、Provider 搜索分页、指纹反查回填、在线反查更新检测（用假 `IResourceApi`）。

## 7. 验证步骤

```bash
cd PCL2
dotnet test PCL.Download.Tests/PCL.Download.Tests.csproj -c Release   # 全绿
dotnet build pcl-next.sln -c Release                                  # 0 错误（CA1416 为上游噪音）
```

判定标准：

- PCL.Download.Tests 0 失败（含新增用例）；
- 用例必须能反向验证：故意关掉降级 → Key 缺失用例失败；故意去掉 429 换源 → 限流用例失败；
- 真机冒烟（B5）由用户在有网环境执行：中英文各搜一次、版本列表翻页、本地 Mod 在线反查与更新提示。

## 8. 回滚方案

- 全部为新增文件 + 一个注册表，`git revert <本版提交>` 即可整体撤销，不影响 dev.3 已发布的资源模型与搜索行为。
- 保留内容：dev.3 的 `HashType`/`HashHelper`/`ResourceProject`/`ResourceVersion`/`ResourceSearcher`/`LocalResourceScanner` 均不改动，回滚后仍可用。
- 若仅 Provider 层有问题：可单独回滚 `ResourceProviderRegistry`，保留 `HttpResourceApi`（它对 VB 侧是可选的更好实现）。
