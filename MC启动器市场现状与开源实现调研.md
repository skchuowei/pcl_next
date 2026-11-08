# Minecraft 启动器市场现状与开源实现调研

> 调研日期：2026-10-31　|　面向本仓库（PCL2 C# 迁移，`pcl-next` 资源子系统）落地参考
> 资料来源：官方 API 文档、GitHub 源码（逐文件阅读）、镜像站文档与实时统计接口

---

## 0. 摘要（TL;DR）

1. **市场已经是"双平台"格局**：CurseForge（存量最大）与 Modrinth（增量最快）是唯二的事实标准，其余是镜像/百科/自有站（MC 百科、BakaXL 自有站）。
2. **接口门槛不对等是所有实现分歧的根源**：Modrinth API 完全开放（仅要求唯一 `User-Agent`，300 req/min/IP）；CurseForge API 必须带 `x-api-key`，密钥需向 CurseForge for Studios 申请，**不允许内置分发**——这直接决定了 HMCL / PCL2 / Prism 都必须引入"用户自带 Key + 镜像降级"的双轨设计。
3. **国内可用的核心是镜像站**：MCIM 提供"URL 替换式"镜像（`api.modrinth.com` → `mod.mcimirror.top/modrinth`，`api.curseforge.com` → `mod.mcimirror.top/curseforge`），几乎完美兼容官方结构，替换域名即可；BMCLAPI 负责 Mojang/Fabric/Quilt/NeoForge/OptiFine 等元数据与 Maven 资产。
4. **最值得借鉴的三个架构范式**：
   - Prism 的 **统一索引模型（`ModIndex` / `IndexedPack` / `IndexedVersion`）**：把两个平台的数据都归一化成同一份"文件 + 哈希 + 依赖"结构，下游只认这一份；
   - Prism 的 **CF → Modrinth 兜底**：`downloadUrl` 缺失（项目禁止再分发）时，用文件的 `sha1` 反查 Modrinth 拿替代下载地址；
   - HMCL 的 **候选源降级（`DownloadCandidates`）**：一个逻辑请求展开成多个候选 URL，依次尝试，全失败才报错。
5. **本地文件 → 在线工程的识别**是启动器的核心差异化功能，标准做法是双哈希：Modrinth 用 `sha1`，CurseForge 用 **去空白后的 MurmurHash2（种子 1）**。PCL2 已完整实现该算法（`LocalResourceFile.vb:186-238`）。
6. **依赖求解是各家最大的短板**：HMCL 只做"必需依赖收集"（且是从项目全部版本里收集），Prism 只做单层依赖，多半没有真正的版本约束求解器；这是可以做出差异化的空白点。
7. **中文搜索**几乎是中国区启动器的专属需求：PCL2 用 MC 百科本地库做"中文 → 英文候选词"转换，是目前公开实现里最完整的方案。
8. 本仓库（PCL2）现有实现**已经覆盖了接口层的大部分能力**（双站搜索、本地哈希识别、批量版本查询、镜像降级、CF 指纹），迁移到 C# 时**应当整体平移而不是重写**；真正需要补强的是：接口抽象层（Provider 化）、依赖图求解、并发闸门与缓存策略。

---

## 1. 市场现状

### 1.1 资源平台格局

| 平台 | 归属 | 接入方式 | 对第三方启动器的开放性 | 特点 |
|---|---|---|---|---|
| **CurseForge** | Overwolf（2020 收购） | REST API v1 | **需申请 API Key**，禁止再分发其文件 | 存量最大、整合包生态完整、下载走 `edge.forgecdn.net` |
| **Modrinth** | 开源（`modrinth/code` monorepo，Labrinth 后端） | REST API v2 | **完全开放**，匿名可用 | 增长快、依赖关系明确、支持 `.mrpack`、下载走 `cdn.modrinth.com` |
| **MC 百科 / MCIM / BMCLAPI** | 国内社区 | 独立 API + 镜像 | 公益、开放 | 中文名/中文简介、本地化检索、GFW 兜底 |
| **BakaXL 自有站 / ATLauncher 自有 CDN** | 各启动器自有 | 私有接口 | 仅限本启动器 | ATLauncher 在 README 中明确禁止 fork 使用其 CDN |

> 结论：**"两个官方平台 + 一层中国镜像"** 是当前所有第三方启动器的实际接入拓扑。没有第四个通用平台。

### 1.2 规模数据（实测，非估计）

来自 MCIM 实时统计接口 `https://mod.mcimirror.top/statistics`（该站缓存了"绝大多数"两站数据）：

| 指标 | CurseForge | Modrinth |
|---|---|---|
| 工程（项目）数 | 378,103 个 Mod | 168,539 个 project |
| 文件 / 版本数 | 2,587,462 个文件 | 1,546,516 个版本 / 1,694,151 个文件 |
| 已缓存中文简介 | 366,014 | 173,781 |

GitHub 社区热度（2026-10-31 读取）：

| 项目 | Star | Fork | 语言 | 许可 |
|---|---|---|---|---|
| PrismLauncher/PrismLauncher | 10,985 | 1,668 | C++/Qt6 | GPL-3.0 |
| HMCL-dev/HMCL | 10,835 | 1,291 | Java/JavaFX | GPL-3.0 |
| Voxelum/x-minecraft-launcher | 1,681 | 217 | TypeScript/Electron | MIT |

> 说明：CF 的"文件数/工程数"比远高于 Modrinth，源于 CF 有大量历史版本与同一 Mod 的多文件分发；Modrinth 的版本粒度更干净。

### 1.3 启动器格局

| 阵营 | 代表 | 技术栈 | 市场接入策略 |
|---|---|---|---|
| 官方 | Minecraft Launcher | Java | 仅本体，无社区资源 |
| 国内第三方 | **PCL2**、**HMCL**、BakaXL | VB.NET / JavaFX / 闭源 | 双站 + MC 百科 + 镜像（MCIM/BMCLAPI） |
| 海外第三方 | **Prism Launcher**（MultiMC 分支）、ATLauncher、**Modrinth App**、XMCL、Helios、GDLauncher | C++/Qt、Java/Swing、Rust/Tauri、Electron | Prism 双站 + Technic/AT/FTB/Packwiz 导入；AT 自有 CDN；Modrinth App 仅自家 API |

**结构性事实**：海外主流启动器基本都把 CurseForge 当"次要源"（甚至需要用户自备 Key），而 Modrinth 成为默认首选；国内则相反——CF 因存量与整合包多而不可替代，Modrinth 近年补齐。因此**双站融合是国内启动器的刚性需求**，这正是 PCL2/HMCL 的立足点。

### 1.4 准入、合规与限流（各平台硬约束）

| 项 | CurseForge | Modrinth |
|---|---|---|
| 鉴权 | `x-api-key: <KEY>`，**所有请求必带** | 公开接口免鉴权；写操作/私有数据需 PAT 或 OAuth2（`Authorization` 头 + scope） |
| Key 分发 | 需自行申请，**不允许内置**（ATLauncher 明确禁止 fork 使用其 Key/CDN） | 无需 Key |
| User-Agent | 未强制，但建议带 | **强制**：必须唯一标识（例：`github_user/project/1.0 (contact@mail.com)`），仅报库名的 UA 会被封 |
| 限流 | 官方未公开统一数值；社区经验为每 Key 每日配额 | **300 req/min per IP**，通过 `X-Ratelimit-Limit / -Remaining / -Reset` 响应头暴露 |
| 分页上限 | `pageSize` ≤ 50，总结果 ≤ 10,000 | `limit` ≤ 100，`offset` 分页 |
| 文件再分发 | 项目可标记"禁止再分发"，此时 `downloadUrl` 为空 | CDN 可直链下载，但 Modrinth 同样要求遵守其服务条款 |
| 版本废弃 | — | 老版本 API 直接返回 **410**，客户端需处理 |

**合规红线（本仓库必须注意）**：CurseForge Key、Modrinth 官方站文件分发权都不可内置到发行版；镜像站（MCIM）协议中明确"禁止在 MCIM 上对接口进行二次封装"，且其文件所有权仍归属源站。

### 1.5 国内网络环境（GFW）是架构性约束

- CurseForge 域名与 CDN 在国内**间歇性可达**，`edge.forgecdn.net` 下载尤其不稳；
- 常见故障：请求返回空、被截断的半截 JSON、HTTP 416（不支持 Range）。

应对手段（各家共识）：

1. **镜像 URL 替换**（MCIM/BMCLAPI）；
2. **多候选源 + 逐个降级**；
3. **JSON 完整性校验**（PCL2 在 `ModNet.vb` 中显式检查首尾 `{}` / `[]`，专门用于识别 GFW 截断）；
4. **对不支持 Range 的镜像禁用分片**（PCL2 维护了一份"禁用分片源白名单"，含 bmclapi、github.com 等）。

---

## 2. 接口层：市场 / 资源获取的调用方式

### 2.1 Modrinth API v2

- 生产：`https://api.modrinth.com/v2`，预发：`https://staging-api.modrinth.com/v2`
- 鉴权：`Authorization: <PAT|OAuth token>`，仅写操作/私有数据需要；scope 越权返回 401
- 限流：300/min per IP；响应头 `X-Ratelimit-Limit/Remaining/Reset`
- UA：**必须**唯一标识应用

**常用端点**

| 用途 | 方法与路径 | 说明 |
|---|---|---|
| 搜索 | `GET /v2/search` | `query`、`facets`、`limit`、`offset`、`index`（排序） |
| 项目详情 | `GET /v2/project/{id\|slug}` | id 为 8 位 base62，slug 可变，长期存储建议用 id |
| 批量项目 | `GET /v2/projects?ids=["id1","id2"]` | 一次取多个，**省配额的关键** |
| 版本列表 | `GET /v2/project/{id}/version` | `include_changelog=false` 可省流量 |
| 版本详情 | `GET /v2/version/{versionId}` | 含 changelog |
| 哈希→版本 | `POST /v2/version_files` | body `{"hashes":[...],"algorithm":"sha1"}`，**批量** |
| 查更新 | `POST /v2/version_files/update` | 批量检查最新版本 |
| 单哈希查询 | `GET /v2/version_file/{hash}?algorithm=sha1` | 也支持 `sha512` |
| 依赖查询 | `GET /v2/project/{id}/dependencies` | 项目级依赖 |
| 标签 | `GET /v2/tag/{category\|loader\|game_version}`、`GET /v2/category` | 分类/加载器/MC 版本字典 |

**facets 语法**（搜索过滤，AND 关系，数组内 OR）：

```
[["project_type:mod"],["versions:1.21.1"],["categories:fabric"],["project_id:xxx"]]
```

**排序**（`index` 参数）：`relevance`（默认）/ `downloads` / `follows` / `newest` / `updated`。

**依赖模型**：`dependency_type` ∈ `required` / `optional` / `embedded` / `incompatible`，含 `project_id` 与 `version_id`。

### 2.2 CurseForge API v1

- 基址：`https://api.curseforge.com/v1`
- 鉴权：请求头 `x-api-key: <KEY>`（**所有端点必带**）
- Minecraft `gameId = 432`

**常用端点**

| 用途 | 方法与路径 | 说明 |
|---|---|---|
| 搜索 | `GET /v1/mods/search` | `gameId`、`classId`、`categoryId(s)`(≤10)、`gameVersion(s)`(≤4)、`modLoaderType(s)`(≤5)、`searchFilter`、`sortField`、`sortOrder`、`index`、`pageSize` |
| 项目详情 | `GET /v1/mods/{modId}` | |
| 批量项目 | `POST /v1/mods` | body `{"modIds":[...]}` |
| 文件（版本）列表 | `GET /v1/mods/{modId}/files` | 可带 `gameVersion`、`modLoaderType` |
| 单个文件 | `GET /v1/mods/{modId}/files/{fileId}` | |
| 下载地址 | `GET /v1/mods/{modId}/files/{fileId}/download-url` | 返回 `{"data":"https://edge.forgecdn.net/..."}` |
| 批量文件 | `POST /v1/mods/files` | body `{"fileIds":[...]}` |
| 更新日志 | `GET /v1/mods/{modId}/files/{fileId}/changelog` | HTML |
| **指纹匹配** | `POST /v1/fingerprints/432`、`POST /v1/fingerprints/fuzzy/432` | body `{"fingerprints":[123,456]}`，返回 `exactMatches` / `partialMatches` |
| 分类字典 | `GET /v1/categories?gameId=432&classId=6` | `isClass` 区分大类 |
| 游戏/版本 | `GET /v1/games`、`GET /v2/games/432/versions` | |

**`classId` 映射（Minecraft）**

| 资源类型 | classId |
|---|---|
| Mod | 6 |
| 整合包 | 4471 |
| 资源包 | 12 |
| 光影 | 6552 |
| 数据包 | 6945 |
| 世界 | 17 |
| Bukkit 插件 | 5 |
| 材质/自定义 | 4546 / 4559 |

**`modLoaderType` 映射**：1 Forge、2 Cauldron、3 LiteLoader、4 Fabric、5 Quilt、6 NeoForge。
注意：`modLoaderType` **必须与 `gameVersion` 同时使用**才生效（官方文档明确），单用会被忽略。

**`sortField`**：1 Featured、2 Popularity、3 LastUpdated、4 Name、5 Author、6 TotalDownloads、7 Category、8 GameVersion。

**文件依赖 `relationType`**：1 EmbeddedLibrary、2 OptionalDependency、3 RequiredDependency、4 Tool、5 Incompatible、6 Include。

**文件哈希 `hashes[].algo`**：1 = SHA1，2 = MD5（另见 2.6 指纹）。

### 2.3 加载器与版本元数据（非"市场"，但属同一资源域）

| 目标 | 官方源 | 国内镜像（BMCLAPI） |
|---|---|---|
| Minecraft 版本清单 | `https://piston-meta.mojang.com/mc/game/version_manifest_v2.json` | `https://bmclapi2.bangbang93.com/mc/game/version_manifest_v2.json` |
| 原版资源 | `https://resources.download.minecraft.net/` | `/assets` |
| 库文件 / Maven | `https://libraries.minecraft.net/`、`maven.fabricmc.net`、`maven.neoforged.net/releases` | `/maven`、`/libraries` |
| Fabric 元数据 | `https://meta.fabricmc.net/v2/versions/{game}`、`/v2/versions/loader/{game}/{loader}` | `/fabric-meta` |
| Quilt | `https://meta.quiltmc.org` | `/quilt-meta`（官方有 bug，镜像可用） |
| NeoForge | `https://maven.neoforged.net/releases/net/neoforged/neoforge/` | `/maven/net/neoforged/neoforge` |
| Forge | `https://maven.minecraftforge.net/` | `/forge/download?mcversion=&version=&branch=&category=&format=` |
| OptiFine | 官方站点 HTML 解析 | `/optifine/:mcversion/:type/:patch`、`/optifine/versionList` |
| authlib-injector | `https://authlib-injector.yushi.moe` | `/mirrors/authlib-injector` |

### 2.4 整合包格式（安装链路的输入）

| 格式 | 结构 | 关键字段 |
|---|---|---|
| **`.mrpack`（Modrinth）** | zip 内含 `modrinth.index.json` + `overrides/` | `formatVersion`、`game`、`versionId`、`files[]{path, hashes{sha1,sha512}, url, size, env{client,server}, downloads[]}`、`dependencies[]{project_id, version_id, file_name, dependency_type}` |
| **CurseForge 整合包** | `manifest.json` + `modlist.html` | `manifestType:"minecraftModpack"`、`minecraft{version,modLoaders}`、`files[]{projectID,fileID,required}`、`overrides` |
| **MultiMC / Prism 实例** | `instance.cfg` + `mmc-pack.json` | `InstanceType`、`Version`、`IntendedPlatform` |
| **HMCL 自有** | 目录式整合包 + `modpack.json` | 保留源站信息以便还原 |

要点：**`.mrpack` 的 `dependencies` 天然带版本约束**（`version_id`），而 CF 的 `manifest.json` 只有 `projectID + fileID`（锁定到具体文件），因此 CF 整合包安装无法自动"升到最新版"，只能按文件 ID 精确安装。

### 2.5 国内镜像体系

**MCIM（mcmod-info-mirror）**——"URL 替换式"镜像，几乎兼容官方 API 结构：

| 官方 | 镜像 |
|---|---|
| `api.modrinth.com` | `mod.mcimirror.top/modrinth` |
| `cdn.modrinth.com` | `mod.mcimirror.top` |
| `api.curseforge.com` | `mod.mcimirror.top/curseforge` |
| `edge.forgecdn.net` / `mediafilez.forgecdn.net` | `mod.mcimirror.top` |

要点与限制：
- 响应头 `sync_at` 给出**缓存生成时间**（`YYYY-MM-DDTHH:MM:SSZ`），启动器应据此判断数据新鲜度；
- 可能启用 **UA 白名单**（需向项目登记启动器 UA）；
- 只缓存 **Mod（classId=6）**、不缓存整合包/资源包/材质/地图，且**不缓存 > 20MB 的文件**；
- 老 Python 实现已被 `mcim-rust-api` 取代；文件下载另走 `mcim-files.pysio.online`（Cloudflare 镜像）；
- 协议明确"禁止在 MCIM 上对接口进行二次封装"，且镜像内容所有权仍属源站。

**BMCLAPI（bangbang93）**——元数据与 Maven 资产镜像（见 2.3），不承担 CF/MR 模组元数据代理。

### 2.6 本地文件识别（指纹 / 哈希）

| 平台 | 匹配依据 | 说明 |
|---|---|---|
| Modrinth | `sha1`（也提供 `sha512`） | `POST /v2/version_files` 批量匹配；`/v2/version_file/{hash}` 单查 |
| CurseForge | **指纹 = MurmurHash2（32 位，种子 1）**，计算时**剔除所有空白字符**（`0x09/0x0A/0x0D/0x20`） | `POST /v1/fingerprints/432` 批量匹配，返回 `exactMatches` |

> PCL2 已在 `LocalResourceFile.vb:186-238` 精确实现了该算法（`h = 1 Xor length` 作为初始值，逐 4 字节 `0x5BD1E995` 混合，末尾 3/2/1 字节单独处理，最后 `h Xor (h >> 13)` / `Xor (h >> 15)`），并把结果缓存进 `Cache/ModHash.ini`。

Prism 的哈希优先级策略（可借鉴）：**CurseForge → `sha1` → `md5` → `murmur2`；Modrinth → `sha512` → `sha1`**（`ProviderCapabilities::hashType`）。

---

## 3. 开源项目对比

### 3.1 总表

| 维度 | HMCL | Prism Launcher | Modrinth App (Theseus) | ATLauncher | XMCL | **PCL2（本仓库）** |
|---|---|---|---|---|---|---|
| 语言/框架 | Java + JavaFX | C++ + Qt6 | Rust + Tauri + Vue | Java + Swing | TypeScript + Electron | VB.NET + WPF |
| GitHub | 10.8k★ | 11.0k★ | monorepo | ~1.3k★ | 1.7k★ | 私有/迁移中 |
| CurseForge | ✅ 需 Key（`-Dhmcl.curseforge.apikey` 或 JAR 属性） | ✅（`BuildConfig.FLAME_BASE_URL`，可指向自建代理） | ❌ | ✅（Key 写在 `Constants.java`） | ✅ | ✅（环境变量 `PCL_CURSEFORGE_API_KEY`） |
| Modrinth | ✅ | ✅ | ✅ 唯一数据源 | 部分 | ✅ | ✅ |
| 统一抽象 | `RemoteAddonRepository`（CF/MR 各一实现） | `ResourceAPI` + `ModIndex` 统一模型 | 自有 API 抽象 | 自有 | 自有 | `ResourceProject` / `ResourceVersion` 双平台合一 |
| 候选源降级 | ✅ `DownloadCandidates` | 部分（可配 base URL） | — | — | ✅ | ✅ `DlModRequest` + `DlSourceModGet` |
| 本地文件识别 | ✅ SHA-1 + 指纹 | ✅（`murmur2` 库） | ✅ | 部分 | ✅ | ✅ SHA-1 + MurmurHash2 |
| 依赖求解 | 仅收集必需依赖（按项目全版本） | 单层依赖解析 | ✅ 依赖图 | 部分 | 部分 | **仅记录 `RawDependencies`，未求解** |
| 缓存 | 无（每次都请求） | 内存/磁盘混合 | 本地 DB | 本地 DB | 本地 | `Cache/LocalMod.json`（6h TTL）+ 内存缓存 |
| 并发保护 | `Semaphore(16)` | 任务级 | 异步任务 | 线程池 | — | 线程池 + 任务编排 |
| 中文搜索 | 无 | 无 | 无 | 无 | 无 | ✅ MC 百科本地库 |
| 国内镜像 | BMCLAPI | — | — | — | ✅（国内团队） | ✅ BMCLAPI + MCIM |

### 3.2 HMCL 细读（Java，最贴近 PCL2 的对照物）

**架构**：`HMCL`（UI）/ `HMCLCore`（核心）/ `HMCLBoot`（引导）三段式；`addon` 包统一管理"插件（mod/datapack/resourcepack/meta/update）"，`addon/repository` 下是 `CurseForgeRemoteAddonRepository` 与 `ModrinthRemoteAddonRepository` 两个远程仓库实现，共同实现 `RemoteAddonRepository` 接口。

**市场调用方式**
- CurseForge：`GET /v1/mods/search`（参数 `gameId=432`、`classId`、`categoryId`、`gameVersion`、`searchFilter`、`sortField`、`sortOrder`、`index=pageOffset*pageSize`、`pageSize`）、`/v1/mods/{id}`、`/v1/mods/{id}/files?pageSize=10000`、`/v1/mods/{id}/files/{fileId}`、`/v1/mods/{id}/files/{fileId}/changelog`、`POST /v1/fingerprints/432`、`GET /v1/categories`。
  - **Key 注入**：仅当 URL 以 `https://api.curseforge.com` 开头且 Key 非空时加 `X-API-KEY`；Key 来自系统属性 `hmcl.curseforge.apikey` 或 JAR 属性；`isAvailable()` 为空时整个仓库判定不可用（**优雅降级**）。
  - **下载 URL 回退**：API 返回 `downloadUrl == null` 时自行拼接 `https://edge.forgecdn.net/files/{id/1000}/{id%1000}/{fileName}`。
  - **并发闸门**：`Semaphore(16)`，`acquireUninterruptibly()`。
- Modrinth：`GET /v2/search`（facets：`project_type:` / `versions:` / `categories:`；`offset=pageOffset*pageSize`；`index` 排序映射 relevance/follows/newest/updated/downloads）、`/v2/project/{id}`、`/v2/project/{id}/version`、`/v2/version/{id}`、`/v2/version_file/{sha1}?algorithm=sha1`、`/v2/tag/category`。
  - 依赖：只取 `dependency_type=required`，**丢失 `version_id`**，最终按项目 ID 加载（等于放弃了版本约束）。

**值得借鉴**
1. **仓库可用性判定**：`isAvailable()` → UI 上直接隐藏 CurseForge 入口，而不是让用户点了才报错（PCL2 目前是"带 Key 就用，不带就整片失败"，可优化）。
2. **候选源机制**：`DownloadProvider.getDownloadCandidates(url)` 返回多个候选，依次尝试；多候选全失败时抛带 `suppressed` 的 `IOException`，单候选时保留原始异常（**错误信息可读性**）。
3. **`DownloadProvider` 的版本列表抽象**：Mojang / Fabric / Quilt / Forge / NeoForge / Cleanroom / LiteLoader 各自提供"候选 URL 列表"，加载器差异被收敛到一处。
4. **软引用缓存**：`VersionListState` 用 `SoftReference<Map<版本, 列表>>`，配合读写锁，内存压力下自动回收（PCL2 可在 C# 里用 `MemoryCache` + 弱引用复刻）。

**不足 / 可超越之处**
- 无本地缓存（每次都打 API），浪费 Modrinth 配额（300/min）；
- 无 429/`Retry-After` 退避；
- 依赖解析按"项目全部版本"收集，必装到不兼容的旧版本（PCL2 也有类似隐患）；
- 搜索 `sortOrder` 未传给 Modrinth（升/降序丢失）。

### 3.3 Prism Launcher 细读（C++，抽象层设计最好）

**架构**：`launcher/modplatform/` 是资源抽象层：

```
modplatform/
├── ResourceAPI.h/.cpp      # 平台无关接口
├── ModIndex.h/.cpp         # 统一数据模型（IndexedPack/IndexedVersion/Dependency/Category）
├── ResourceType.h/.cpp     # 资源类型枚举
├── helpers/                # 通用工具
├── modrinth/               # Modrinth 实现
├── flame/                  # CurseForge 实现
├── technic/ atlauncher/ ftb/ import_ftb/ legacy_ftb/ packwiz/   # 其他来源或导入器
```

**`ResourceAPI` 接口**（关键抽象）：

```cpp
virtual std::pair<Task::Ptr, QByteArray*> getProjects(QStringList addonIds) const = 0;
virtual Net::RPC::Spec<ModPlatform::IndexedPack> getProject(const QString& id) const = 0;
virtual Net::RPC::Spec<QList<ModPlatform::IndexedPack>> searchProjects(const SearchArgs& args) const = 0;
virtual auto getVersionsURL(const VersionSearchArgs& args) const -> std::optional<QString> = 0;
virtual Result<ModPlatform::IndexedVersion> loadIndexedPackVersion(QJsonObject&, ResourceType) const = 0;
virtual QList<ModPlatform::Category> loadModCategories(const QByteArray& response) const = 0;
```

`SearchArgs` 统一了：资源类型、offset、关键词、排序（`SortingMethod{index,name,readableName}`，index 给 CF、name 给 Modrinth）、加载器集合、MC 版本集合、端侧（`SideType`）、分类 ID 列表、开源标记、披露类型排除。

**统一模型**：`IndexedPack`（项目）、`IndexedVersion`（文件/版本，含 `hashType` + `hash` + `downloadUrl` + `dependencies` + `loaders` + `side`）、`Dependency{addonId,type,version}`。
→ **CF 与 Modrinth 的数据在进入业务层之前就被归一化**，UI、下载器、依赖安装只认这一份结构。

**CurseForge 实现（`flame/FlameAPI.cpp`）**
- 基址 `BuildConfig.FLAME_BASE_URL`（**可替换为自建代理/镜像**，官方 CF 不可达时非常有用）；
- 端点：`/mods/search?gameId=432&...`（`pageSize=25`，`index=offset`）、`/mods/{id}`、`/mods/{id}/description`、`/mods/{id}/files?pageSize=10000`、`/mods/{id}/files/{fileId}`、`/mods/{id}/files/{fileId}/changelog`、`POST /mods/files`、`POST /fingerprints`、`/categories?gameId=432&classId=6`；
- 搜索参数：`classId`（Mod 6 / 资源包 12 / 世界 17 / 光影 6552 / 整合包 4471 / 数据包 6945）、`gameVersion`（取首个）、`modLoaderTypes=[1,4,5,6]`（仅 NeoForge/Forge/Fabric/Quilt）、`sortField`+`sortOrder=desc`、`categoryIds=[..]`；
- 解析（`FlameModIndex.cpp`）：`gameVersions` 里混装了 MC 版本与加载器名，靠字符串判定拆成 `mcVersion` 与 `loaders`；`hashes[].algo` 1→sha1、2→md5；`relationType` 映射为 `DependencyType`。

**⭐ 最值得抄的一条：CF → Modrinth 兜底（`flame/FileResolvingTask.cpp`）**
安装 CF 整合包时：
1. `POST /mods/files` 批量取文件；
2. 若某文件 **没有有效 `downloadUrl`**（项目禁止再分发）且有 `sha1`；
3. 则调用 **Modrinth `currentVersions(hashes,"sha1")`**，用同一文件的 sha1 反查 Modrinth 上的替代版本与下载地址；
4. 设置项 `FallbackMRBlockedMods` 控制是否启用。

> 这条策略把"CF 下架/禁分发"从死路变成可恢复路径，**是本次调研中最有增量价值的一条**。

**其他细节**
- `getOverrideDeps()` 内置过滤 Fabric API / Quilt API / KotlinLibraries（CF 与 Modrinth 的 ID 各一份）——PCL2 也在 `ResourceVersion.vb` 里排除了 Fabric API / Quilt API，两边思路一致。
- 分类加载按 `classId` 分批拉取，避免一次性拉全量分类。

### 3.4 Modrinth App（Theseus）细读

**仓库**：https://github.com/modrinth/code（monorepo，约 5,000 次提交）

| 目录 | 内容 | 技术 |
|---|---|---|
| `apps/labrinth` | 平台后端 API | Rust + Actix + SQLx + Redis + Typesense + Kafka |
| `apps/app` | 桌面启动器（Theseus） | Rust 核心 + Tauri 外壳 + Vue/TS GUI |
| `apps/frontend` | 网站（Knossos） | Vue + Vite |
| `apps/docs` | 文档站 | Docusaurus |
| `packages/` | 共享包（UI 组件、类型、lint） | TS + Rust workspace |

**架构启示**
- **API 只提供元数据与下载地址；下载、校验、解压、写入实例目录全部在客户端 Rust 侧完成**——这正是所有启动器的正确分工；
- 依赖处理是**图**而非列表：选一个 Mod → 拉必需依赖 → 校验 MC 版本与加载器兼容性 → 去重 → 处理可选依赖 → 排除不兼容版本；
- 安装器支持"内容目的地"概念（mods / config / resourcepacks / shaderpacks / scripts），遇到未知目的地会报错（近期修复记录：*"unsupported content destination error on modpack install"*）；
- 只接自家 API，因此不需要处理双站归一化问题——**但它的 `.mrpack` 安装流程是 `.mrpack` 规范的最佳实现参考**。

### 3.5 ATLauncher / XMCL / 其他

- **ATLauncher**（Java/Swing）：README 明确"两个关键常量是 CurseForge Core API Key 与 Microsoft Login Client ID"，且**禁止 fork 使用其 CDN/整合包/素材**（会发 cease & desist）。启示：第三方启动器**绝不能内置他人平台凭据**，Key 必须"用户自带"。
- **XMCL**（TypeScript/Electron，1.7k★，国内团队）：topics 含 `curseforge` / `modrinth`，Electron + Vue，国内网络适配经验值得参考。
- **BakaXL**：闭源，自有资源站，自建生态；公开代码不可借鉴，但"启动器自建资源站"在商业模式上是存在的路径。
- **GDLauncher / Helios**：Electron 系，Helios 侧重"可分发的启动器底座"（应用市场白标模式）。
- **MultiMC → Prism**：MultiMC 原项目已停更，Prism 是活跃分叉；其 `modplatform` 抽象被社区广泛继承（**MultiMC 格式兼容仍是事实标准之一**）。

---

## 4. 市场调用的实现思路（可复用设计模式）

以下 10 条模式是跨项目验证过的"共识解法"，可直接映射为 C# 侧的接口设计。

### 模式 1：Provider 化 + 统一索引模型
```
IRemoteProvider (CF / MR / Technic / 自有)
        │  Search / GetProject / GetVersions / GetFile / GetDependencies
        ▼
   统一模型：RemoteProject / RemoteVersion / RemoteFile / RemoteDependency
        │
        ▼
   UI、下载器、依赖求解、导出  ← 只依赖统一模型，不依赖平台
```
对应 Prism `ResourceAPI` + `ModIndex`、HMCL `RemoteAddonRepository`。
**收益**：加一个平台（如 Technic / ATLauncher / BakaXL）只写一个适配器；平台下线（CF 禁分发）时只改适配器。

### 模式 2：候选源降级链（候选 URL 列表）
```
逻辑请求 URL ──► 展开为候选列表 [官方, MCIM, BMCLAPI, CDN]
                     │ 逐个尝试（各自超时）
                     ├─ 成功 → 返回
                     └─ 全失败 → 聚合异常（保留每个源的错误）
```
对应 HMCL `DownloadCandidates`、PCL2 `DlModRequest`。
**要点**：每个候选要有独立超时（Modrinth 至少 20s，CF 10s）、失败原因分类（404=文件不存在 / 403·429=镜像限流 / 502=源故障），**不要因 403/429 禁用镜像源**（PCL2 已在这一点上做了区分）。

### 模式 3：统一哈希抽象
```
HashSet { Sha1, Sha512, Md5, Murmur2 }
每条记录携带 hashType + hash（不要只存哈希值）
匹配顺序按平台能力：CF → sha1 → md5 → murmur2；MR → sha512 → sha1
```
对应 Prism `ProviderCapabilities::hashType`。
**收益**：同一文件在两站之间可比对（为模式 6 的兜底提供基础）。

### 模式 4：本地文件 → 在线工程（双站并行识别）
```
本地 jar ── sha1 ──► POST /v2/version_files ──► Modrinth projectId + version
       └─ murmur2 ──► POST /v1/fingerprints/432 ──► CF projectId + fileId
                                     │
                                     └──► POST /v2/projects?ids=… / POST /v1/mods ──► 项目元数据
```
PCL2 现有实现已完全覆盖（`LocalResourceLoaders.vb`），迁移时**平移即可**。
**要点**：批量接口是省配额关键（一次 100 个哈希 vs 100 次请求）；本地缓存 TTL（PCL2 用 6 小时）。

### 模式 5：批量优先 + 配额预算
- `GET /v2/projects?ids=[...]`（最多 N 个 id 一次取回）
- `POST /v1/mods`（`modIds` 批量）
- `POST /v1/mods/files`（`fileIds` 批量）
- `POST /v2/version_files`（`hashes` 批量）
**教训**：HMCL 因无缓存 + 无批量，一次"检查更新"可能打满 300/min 配额。

### 模式 6：跨站兜底（CF 无下载地址 → Modrinth 找替代）
见 3.3。PCL2 目前只在 CF 与 MR **各自**可用时展示结果，**没有做"同一文件跨站替代"**，这是可直接落地的增量功能。

### 模式 7：搜索融合与去重
```
并行请求 CF 与 MR ──► 合并 ──► 跨站去重 ──► 统一排序
```
- **跨站去重判定**（PCL2 `IsLike` 的做法，业界少见且实用）：同平台直接比 ID；跨平台需满足：加载器集合一致 + 支持的 MC 版本集合一致（光影放宽）+ 更新时间相差 ≤ 7 天 + （中文名 / 原名 / 描述 / Slug 去非字母数字后）任一相同。
- **排序权重**：无关键词 → 按下载量；有关键词 → 关联度 + log(下载量) 权重 + 是否有中文名加成；并对不同平台设置下载量系数（Modrinth 单站下载量通常更大，PCL2 现为 Mod 1 : Modrinth 5、ModPack 1 : 1、数据包 10 : 1）。

### 模式 8：中文检索辅助层
```
中文关键词 ──► 本地百科库（中文名 / 别名 / Slug）匹配
                 └─► 产出英文候选词/候选 slug
                        ├─► 传给 CF searchFilter
                        └─► 直接按 slug 调 MR /v2/projects?ids=…
```
PCL2 的 `WikiEntry`（MC 百科库）+ `ExtractWords`（分词、剔除 the/of/mod/and/forge/fabric…、数字、非常规词、去可组合词）是目前公开实现中最完整的，可直接平移。
**增强点**：候选词应加权（相似度 × 热门度），并允许"多词组合"搜索。

### 模式 9：整合包安装流水线
```
识别格式 ──► 解析清单（mrpack / manifest.json / MMC）
        ──► 展开为 (项目, 版本约束, 目标路径, 客户端/服务端标记)
        ──► 依赖图求解 ──► 下载（并发 + 哈希校验 + 镜像降级）
        ──► 落盘（overrides 覆盖 / 文件按 path 写入）──► 写实例配置
```
**版本约束差异**：mrpack 有 `version_id`（可精确定版）；CF manifest 只有 `fileID`（锁文件）。

### 模式 10：限流、缓存与降级的三件套
- **闸门**：全局并发信号量（HMCL `Semaphore(16)`）＋ 每 IP 令牌桶（Modrinth 300/min）。
- **缓存**：项目元数据 TTL 缓存 + 哈希识别 TTL 缓存（PCL2 已用 6h）+ 内存软引用缓存（HMCL 模式）。
- **降级**：429 读 `Retry-After` 退避；403/429 不禁用镜像源；单源失败时**保留另一源结果并给用户明确提示**（PCL2 现有提示文案："无法连接到 CurseForge，所以目前仅显示了来自 Modrinth 的内容，搜索结果可能不全"——这是很好的 UX 范例）。

---

## 5. 对本仓库（PCL2 C# 迁移）的落地建议

### 5.1 迁移时应当"平移"而非"重写"的部分

| VB 位置 | 能力 | C# 建议 |
|---|---|---|
| `Modules/Resource/ResourceSearcher.vb` | 双站并行搜索 + 融合排序 + 中文搜索 | 平移；抽出 `ISearchProvider` 接口 |
| `Modules/Resource/ResourceProject.vb` | 平台归一化 + 分类映射 + 跨站去重 `IsLike` | 平移；把分类映射表抽到静态字典类 |
| `Modules/Resource/ResourceVersion.vb` | 文件/版本/哈希/依赖归一化 | 平移；`HashType` 独立建模 |
| `Modules/Resource/LocalResourceFile.vb` | MurmurHash2（去空白）+ SHA1 | 平移；保留 `ModHash.ini` 缓存格式兼容 |
| `Modules/Resource/LocalResourceLoaders.vb` | 双站批量识别 + 更新检查 | 平移；改为 Task-based 并行 |
| `Modules/Minecraft/ModDownload.vb:1175+` | `DlModRequest` 候选源降级 | 平移为 `DownloadCandidateResolver` + `HttpClientFactory`（双客户端：带缓存 / 流式） |
| `Modules/ModSecret.vb:26-41` | UA / Referer / `x-api-key` 注入 | 平移为 `DelegatingHandler`（`RequestSigningHandler`） |

### 5.2 建议补强的（P0 → P2）

| 优先级 | 建议 | 依据 | 预期收益 |
|---|---|---|---|
| **P0** | **Provider 抽象层**：`IResourceProvider { SearchAsync / GetProjectAsync / GetVersionsAsync / ResolveHashAsync }`，CF 与 MR 各一实现，统一返回 `ResourceProject` / `ResourceVersion`（现已天然具备归一化能力，只需把 URL 构造与解析下沉到 Provider） | Prism `ResourceAPI`、HMCL `RemoteAddonRepository` | 为"新增平台/替换镜像/下线 CF"留出插拔点，避免再次大改 |
| **P0** | **CF Key 的"可用性判定 + 优雅降级"**：Key 缺失时在 UI 隐藏/禁用 CF 入口并说明原因，而不是等请求失败 | HMCL `isAvailable()` | 体验与错误率双改善 |
| **P0** | **统一哈希抽象 + 补 `sha512`**：目前只存 sha1/md5；`HashType` 枚举化 | Prism `hashType` 策略 | 为跨站兜底与更严格校验铺路 |
| **P1** | **CF → Modrinth 下载兜底**：CF `downloadUrl` 为空时用 `sha1` 反查 MR 取替代下载地址 | Prism `FileResolvingTask` | 直接解决"CF 项目禁分发导致整合包装不上"的硬伤 |
| **P1** | **依赖图求解器**：当前 `RawDependencies` 仅记录不求解；应实现"按版本约束选版本 + 循环检测 + 冲突提示" | Modrinth App 的依赖图、HMCL 的缺陷 | 这是最明显的功能空白，也是最易做出差异化的点 |
| **P1** | **配额预算与 429 退避**：全局并发闸门（16）+ 令牌桶 + 读 `X-Ratelimit-*` / `Retry-After` | HMCL `Semaphore(16)`、Modrinth 限流文档 | 避免搜索/批量检查时把配额打爆 |
| **P1** | **镜像新鲜度判断**：解析 MCIM 的 `sync_at` 响应头，过期数据降级为"仅供参考" | MCIM 文档 | 镜像数据滞后时给出提示，避免把旧数据当权威 |
| **P2** | **版本列表缓存层**：`MemoryCache` + 弱引用 + TTL（对齐 HMCL `SoftReference` 方案） | HMCL `VersionListState` | 减少重复请求 |
| **P2** | **CF 分类按 classId 拉取并缓存**（避免一次拉全量） | Prism `getCategories` | 减少请求数 |
| **P2** | **跨平台 `modLoaderType` 语义补齐**：CF 侧当前只取 `ModLoaders.Flags.First`，应使用 `modLoaderTypes`（≤5 个）多值筛选 | CurseForge 官方文档 | 修正筛选漏项 |

### 5.3 迁移风险清单

| 风险 | 说明 | 缓解 |
|---|---|---|
| **合规** | CurseForge Key 不可内置分发；镜像协议禁止二次封装 | Key 走环境变量/用户输入；镜像只做"URL 替换 + 原样转发"，不做二次业务封装；保留 `ModDownloadPolicy` 式的开关 |
| **上游变更** | CF/Modrinth 随时改字段与限流；Modrinth 老 API 返回 410 | 响应解析用"容错取值"；统一错误分类；为 `410`/`5xx` 保留降级路径 |
| **镜像可用性** | MCIM/BMCLAPI 为公益服务，随时可能限流/关闭 | 候选源链 + 缓存 + 用户可自定义源；BMCLAPI 403/429 不禁用源 |
| **GFW 截断** | 返回空/半截 JSON | 沿用 `RequireJson` 完整性校验；下载用 Range + 长度校验 |
| **指纹算法漂移** | CF 若调整指纹算法，本地识别会失效 | 保留 sha1 作为次选；识别失败时降级为"按文件名 + 大小"提示 |
| **接口数量膨胀** | Provider 抽象 + 候选源 + 缓存 + 闸门会显著增加代码量 | 分阶段落地（见 5.2 优先级），Phase 3 先做"平移 + Provider 抽象"，其余按 P1/P2 迭代 |

### 5.4 与既有迁移计划的衔接

现有《PCL2_CSharp下载服务迁移计划.md》中的 **Phase 3 — C# 资源子系统（约 8 人日，替换 `Resource*`）**，建议调整为：

| 子项 | 内容 | 估算 |
|---|---|---|
| 3.1 | `ResourceProject` / `ResourceVersion` / `ResourceSearcher` / `LocalResource*` 平移到 C# | 3 人日 |
| 3.2 | 抽出 `IResourceProvider`（CF/MR 两实现）+ 统一 `HashType` 模型 | 2 人日 |
| 3.3 | 候选源解析器 + `RequestSigningHandler`（UA/Referer/x-api-key）复用既有 C# 传输引擎 | 2 人日 |
| 3.4 | CF Key 可用性判定与 UI 降级开关 | 0.5 人日 |
| 3.5 | 跨站兜底（CF 无下载地址 → MR sha1 反查）+ 依赖图求解器（第二期） | 3 人日（P1） |

---

## 6. 结论

1. **市场侧没有"第四个平台"**：接好 CurseForge + Modrinth + 一层国内镜像，就覆盖了 95% 的用户诉求。
2. **API 调用的难点不在"调通"，而在"合规 + 配额 + 降级"**：Key 不可内置、UA 必须唯一、限流 300/min、GFW 会截断——这三点决定了架构必须做成"候选源 + 可用性判定 + 配额预算"。
3. **最值得立刻借鉴的三件事**：Prism 的统一索引模型、Prism 的 CF→MR 下载兜底、HMCL 的候选源与 Key 优雅降级。
4. **本仓库已有实现的质量高于多数开源启动器**（中文搜索、跨站去重、双站本地识别、GFW 防护、跨版本文件复用），迁移到 C# 时应当以"平移 + 抽接口"为主，把新增预算放在**依赖求解**与**跨站兜底**这两个真正的空白点上。

---

## 7. 参考资料

**官方文档**
- Modrinth API 概览与规范：https://docs.modrinth.com/api/
- CurseForge REST API：https://docs.curseforge.com/rest-api/
- CurseForge for Studios（申请 API Key）：https://www.curseforge.com/minecraft/mc-mods
- Mojang 版本清单：https://piston-meta.mojang.com/mc/game/version_manifest_v2.json

**镜像**
- BMCLAPI 文档：https://bmclapidoc.bangbang93.com/
- MCIM（mcim-api，旧实现）/ mcim-rust-api（新实现）：https://github.com/mcmod-info-mirror/mcim-api
- MCIM 实时统计：https://mod.mcimirror.top/statistics

**开源启动器**
- HMCL：https://github.com/HMCL-dev/HMCL ｜ `HMCLCore/src/main/java/org/jackhuang/hmcl/addon/repository/{CurseForge,Modrinth}RemoteAddonRepository.java`、`hmcl/download/DownloadProvider.java`
- Prism Launcher：https://github.com/PrismLauncher/PrismLauncher ｜ `launcher/modplatform/ResourceAPI.h`、`ModIndex.h`、`flame/FlameAPI.cpp`、`flame/FlameModIndex.cpp`、`flame/FileResolvingTask.cpp`
- Modrinth monorepo（Labrinth / Theseus / Knossos）：https://github.com/modrinth/code
- ATLauncher：https://github.com/ATLauncher/ATLauncher ｜ `src/main/java/com/atlauncher/constants/Constants.java`
- XMCL：https://github.com/Voxelum/x-minecraft-launcher

**本仓库对照位置**
- 资源模块：`PCL2/pcl-next/Modules/Resource/{ResourceSearcher,ResourceProject,ResourceVersion,LocalResourceFile,LocalResourceLoaders}.vb`
- 候选源与镜像：`PCL2/pcl-next/Modules/Minecraft/ModDownload.vb`（`DlModRequest` 1175-1208、`DlSourceModGet` 1304-1313）
- 鉴权注入：`PCL2/pcl-next/Modules/ModSecret.vb`（`SecretHeadersSign` 29-41）
- 整合包：`PCL2/pcl-next/Modules/Minecraft/ModModpack.vb`

---

## 附录 A：端点速查表

**Modrinth**

```
搜索            GET  /v2/search?query=&facets=&limit=&offset=&index=
项目            GET  /v2/project/{id|slug}
批量项目        GET  /v2/projects?ids=["a","b"]
版本列表        GET  /v2/project/{id}/version?include_changelog=false
版本详情        GET  /v2/version/{versionId}
哈希→版本(批量) POST /v2/version_files        {"hashes":[...],"algorithm":"sha1"}
查更新(批量)    POST /v2/version_files/update
单哈希          GET  /v2/version_file/{hash}?algorithm=sha1
项目依赖        GET  /v2/project/{id}/dependencies
字典            GET  /v2/tag/{category|loader|game_version} | /v2/category
```

**CurseForge**

```
搜索            GET  /v1/mods/search?gameId=432&classId=&categoryId=&gameVersion=&modLoaderType=&searchFilter=&sortField=&sortOrder=&index=&pageSize=
项目            GET  /v1/mods/{modId}
批量项目        POST /v1/mods                  {"modIds":[...]}
文件列表        GET  /v1/mods/{modId}/files?gameVersion=&modLoaderType=&pageSize=
单个文件        GET  /v1/mods/{modId}/files/{fileId}
下载地址        GET  /v1/mods/{modId}/files/{fileId}/download-url
批量文件        POST /v1/mods/files            {"fileIds":[...]}
更新日志        GET  /v1/mods/{modId}/files/{fileId}/changelog
指纹匹配        POST /v1/fingerprints/432      {"fingerprints":[...]}
分类            GET  /v1/categories?gameId=432&classId=
游戏版本        GET  /v2/games/432/versions
```

**镜像替换表**

```
api.modrinth.com        → mod.mcimirror.top/modrinth
cdn.modrinth.com        → mod.mcimirror.top
api.curseforge.com      → mod.mcimirror.top/curseforge
edge.forgecdn.net       → mod.mcimirror.top
piston-meta.mojang.com  → bmclapi2.bangbang93.com
resources.download.minecraft.net → bmclapi2.bangbang93.com/assets
libraries.minecraft.net → bmclapi2.bangbang93.com/maven
maven.fabricmc.net      → bmclapi2.bangbang93.com/maven
meta.fabricmc.net       → bmclapi2.bangbang93.com/fabric-meta
meta.quiltmc.org        → bmclapi2.bangbang93.com/quilt-meta
```

## 附录 B：分类与枚举映射速查

**CF `classId` / MR `project_type` / 加载器 / 排序**

| 资源类型 | CF classId | MR project_type |
|---|---|---|
| Mod | 6 | `mod`（含 `datapack` 分类时再判定为 DataPack） |
| 整合包 | 4471 | `modpack` |
| 资源包 | 12 | `resourcepack` |
| 光影 | 6552 | `shader` |
| 数据包 | 6945 | `mod`（靠 `categories:datapack` 区分） |

| 加载器 | CF modLoaderType | MR category |
|---|---|---|
| Forge | 1 | `forge` |
| Cauldron | 2 | — |
| LiteLoader | 3 | `liteloader` |
| Fabric | 4 | `fabric` |
| Quilt | 5 | `quilt` |
| NeoForge | 6 | `neoforge` |

| CF sortField | 含义 | MR index |
|---|---|---|
| 1 | Featured | — |
| 2 | Popularity | `follows` |
| 3 | LastUpdated | `updated` |
| 4 | Name | — |
| 5 | Author | — |
| 6 | TotalDownloads | `downloads` |
| 11 | DateCreated | `newest` |

## 附录 C：术语

| 术语 | 含义 |
|---|---|
| **facet** | Modrinth 搜索过滤项，格式 `字段:值`，数组内 OR、数组间 AND |
| **classId** | CurseForge 资源大类 ID（6/4471/6945/6552/12…），对应 MR 的 `project_type` |
| **指纹（fingerprint）** | CurseForge 的文件标识：去空白后的 MurmurHash2（种子 1） |
| **exactMatches / partialMatches** | 指纹匹配的精确/模糊命中结果 |
| **mrpack** | Modrinth 整合包 zip 格式，内含 `modrinth.index.json` |
| **override** | 整合包中直接覆盖到实例目录的文件（不走依赖解析） |
| **候选源（candidate）** | 一个逻辑请求展开出的多个实际下载 URL，按顺序尝试 |
