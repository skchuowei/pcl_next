# pcl-next — dev.3 公布标准（v2.100.0.3-dev.3）

> **适用仓库**：`D:\bing\MC\pcl_x\PCL2`（对外名 `pcl-next`，周期 2.100）。
> **配套文档**：《分支与发布规范.md》（分支生命周期、§5 版本号/号段、§5.4 发布说明六章、§5.7 内置双写——本文只定义 **dev.3 这一版** 公布什么、何时能公布、怎么公布，不重复其机制）；《PCL2_CSharp下载服务迁移计划.md》§5（Phase 划分与退出标准）；《MC启动器市场现状与开源实现调研.md》（Phase 3 补强项依据）。
> **性质**：一次性发布标准，dev.3 发布后随标签归档。**dev.4 及以后除非重新约定，直接沿用本文 §3（门槛）与 §4（口径）两章，只替换 §2（内容范围）与 §1（版本身份）**。
> **修订**：本文为 **v2（2026-11-01 范围拆分版）**，已按用户决策把原定范围拆成 dev.3 / dev.4 两版——dev.3 只发**可离线验证的 Phase 3 前半**，Phase 3 后半与真机闭环整体顺延 dev.4（详见 §8 修订记录）。v1 原稿的「Phase 3 完成 + 验证闭环」口径**不再适用**。
> **事实依据**：`scripts/Set-VersionInfo.ps1`（标签→版本名/版本号推导）、`Modules/Base/ModBuildChannel.vb:49-100`（号段表）、`Modules/ModDevNotes.vb`（双写载体）、`ReleaseNotes/v2.100.0.2-dev.2.md`（上一版口径基线）。

---

## 0. 一页速览

dev.3 是 **C# 重写 Phase 3（资源子系统）前半完成** 后的开发版：搜索、版本解析、本地扫描三块**可离线验证**的能力已 C# 化并与 VB 行为对齐；联网半边（Provider 抽象、CF/MR 客户端、CF Key 降级、本地文件在线反查）与真机验证**整体顺延 dev.4**。

| 问题 | 答案 |
|---|---|
| **公布什么** | Phase 3 前半 C# 化（HashType 统一、资源/版本模型、双站融合搜索、本地扫描），行为与 VB 等价；外加 1 项 P0 补强（统一 HashType 含 sha512） |
| **何时能公布** | §3 的 7 项门槛**全部**通过（AND 关系），任一项未过不得打 `v2.100.0.3-dev.3` 标签。**门槛 4（真机闭环）已按 §8 顺延 dev.4**，不构成本版闸门 |
| **怎么公布** | §5.4 六章发布说明 + §5.7 内置双写 + §4 措辞红线；GitHub Release `prerelease=true` |
| **版本身份** | 版本名 `2.100.0.3` / VersionCode `5002` / 标签 `v2.100.0.3-dev.3` / 制品 `PCL-2.100.0.3-dev.exe`（+ `DownloadPolicy.ini` 作为独立附件分别上传） |

---

## 1. 版本身份与「公布」的含义

### 1.1 版本身份（由 `Set-VersionInfo.ps1 -Tag` 唯一推导，禁止手填）

| 项 | 值 | 推导 |
|---|---|---|
| 代号 / 渠道 | `dev`（开发版） | 标签后缀 `-dev.3` |
| 版本名 | `2.100.0.3` | 标签去前缀与后缀 |
| VersionCode | `5002` | 周期基址 5000 + dev 偏移 0 + (序号 3 − 1) |
| 标签 | `v2.100.0.3-dev.3`（annotated） | §5.3 |
| 制品 | `PCL-2.100.0.3-dev.exe`（主附件）+ `DownloadPolicy.ini`（独立附件，与 exe 分别上传，不打包） | `ChannelArtifactName` |
| 显示 | `开发版 2.100.0.3` | `BuildTypeDisplay` |
| 升级判定键 | `SystemHighestDevVersionReg`（与正式版隔离） | `ModBuildChannel.vb` |
| 发布分支 | `dev`（本体合入走 `feat/*` PR） | §1.1《分支与发布规范》 |

### 1.2 「公布」在本文中的确切含义

| 用语 | 含义 | 边界 |
|---|---|---|
| **公布** | 发布到本项目 GitHub Releases（`prerelease=true`），公开可见、用户自选下载 | 等同于 dev.1 / dev.2 的做法 |
| **不对外分发** | 不进任何更新通道、不做推广、不建议日常使用 | 与「公布」不矛盾：可见 ≠ 推荐 |
| **不承诺** | 不承诺稳定性、不承诺功能完整、不承诺配置兼容 | dev 渠道的对外承诺仅是「让开发与测试者看到进度」 |

---

## 2. 公布内容标准（dev.3 必须包含什么）

### 2.1 必须包含（内容增量，全落地才允许进入 §3 验收）

1. **Phase 3 前半——资源子系统 C# 化（可离线验证部分）**，逐项对应本版提交：

   | 提交 | 模块 | 落地内容 |
   |---|---|---|
   | `642b6c1` | 统一 HashType | `HashType`（含 sha512）+ `HashHelper`；CurseForge 指纹改为去空白 MurmurHash2（种子 1），与 Modrinth sha1/sha512 同一模型 |
   | `3e14a61` | 资源领域模型 | `ResourceProject` / `ResourceVersion` 平移：CF/MR 双平台解析、版本解析、`RawDependencies`、缓存 JSON 互转、分页字段 |
   | `0f30c9a` | 资源搜索 | `ResourceSearcher` 平移（双站融合搜索、`IsLike` 跨站去重、中文经 WikiEntry 反查改写、评分排序、递归下一页）+ `FuzzySearch` 加权模糊搜索 |
   | `83ebc01` | 本地扫描 | `LocalResourceFile`（双哈希识别、`Display`/`Description`/`Version` 古怪 setter 规则、JSON 往返）+ `LocalResourceScanner`（递归枚举、Forge 1.13- 版本号子目录、`EnabledName` 去重、文件名排序） |

   **退出标准**：搜索 / 版本解析 / 本地扫描三类**以离线夹具对照**与 VB 实现结果一致（真实网络对照顺延 dev.4 门槛 3）。
2. **P0 补强 1 项**：统一 HashType 模型（含 sha512），与 `PCL.Download` 的 `HashHelper` 复用同一实现。
3. **联网半边与真机验证**（原 v1 §2.1 第 1 项剩余部分、第 2 项前两条、第 3 项）**不在本版范围内**，已整体拆至《dev.4公布标准.md》A/B 两节。

### 2.2 不承诺（明确排除，写进发布说明的「仍沿用 PCL2 的部分」）

- **Phase 3 后半**（`IResourceProvider` 抽象、CF/MR API 客户端、请求签名与候选源、CF Key 降级、`LocalResourceOnlineLoad` 在线反查）——顺延《dev.4公布标准.md》§2.1 A；
- **真机验证闭环**（大文件分片、完整安装、游戏启动、NeoForge 重装回归、资源子系统真实网络冒烟）——顺延《dev.4公布标准.md》§2.1 B，且 dev.4 不得再顺延；
- **Phase 4**（下载 UI 重指到 C# 服务）与 **Phase 6**（下线 VB 下载模块）——最早 dev.4 / dev.5；
- 整合包安装与导出、Mod 加载器安装、游戏启动等模块的 C# 化；
- 音乐、皮肤、披风、联机等非下载模块（不在重写范围）；
- 独立的 dev 版自动更新通道（独立事项，见 §7 决策点 C）；
- **alpha 准入**（连续 3 个版本无新增 P0、P1 清零）——dev.3 只是该序列中的一环，不构成推进代号的依据（《分支与发布规范》§3.2 D1：代号由用户决定）。

### 2.3 范围决策点（若与默认不符，先改本文再动工）

见 §7 决策点 A/B/D。**v2 实际取值（用户 2026-11-01 拍板）：dev.3 = Phase 3 前半 + HashType 统一**，其余拆 dev.4。

---

## 3. 公布门槛（何时才能公布）—— 7 项全过，AND 关系

| # | 门槛 | 验收方式 | 不过的后果 |
|---|---|---|---|
| 1 | **内容完成** | §2.1 全部落地；相关 `feat/*` 已合入 `dev`；无 WIP、无未合并功能分支。**并行在途的引擎改动不得混入发布提交**（发布 tag 必须指向可独立构建验证的提交） | 不发 dev.3，顺延 |
| 2 | **构建质量** | `dotnet build pcl-next.sln -c Release` 0 错误；`scripts/Test-VersionMatrix.ps1` 六渠道矩阵全过；`PCL.Download`/资源库 xUnit 单测全绿（含 Phase 3 新增等价性用例） | 不发 dev.3 |
| 3 | **等价性对比** | Phase 3 三类输出（搜索结果、版本解析、本地扫描）与 VB 实现**同输入离线夹具**对照，结果一致；有意的行为差异逐条记录进发布说明（真实网络对照顺延 dev.4） | 不发 dev.3 |
| 4 | **真机闭环**（dev.2 欠账） | ~~原 4 项~~ **已按 §8 顺延 dev.4**；本版以「构建可启动 + 关于页版本信息正确 + 资源搜索不崩溃」的人工冒烟替代 | **不阻断 dev.3**，但必须在发布说明「已知问题」中写明「本版未做真机闭环，下载/安装链路未验证」 |
| 5 | **缺陷清偿** | 无 P0/P1（启动失败、崩溃、数据损坏、主链路不可用）；P2 全部列入「已知问题」并附规避方式；开发期发现的缺陷已整理待写入「修复」 | 不发 dev.3 |
| 6 | **文档双写就绪** | ① `ReleaseNotes/v2.100.0.3-dev.3.md` 六章草稿（§5.4 / §4.1）；② `ModDevNotes.vb` 追加 `Case 5002`：`DevStageNotice`（功能限制）+ `DevChangelog`（底层实现，§5.7） | 不打 tag |
| 7 | **CI 与发布链路** | `build.yml` 三任务（Release / Build Debug / Build Release）绿；`release.yml` 为 dev.2 已验证路径（exe + ini 独立附件）；推送到 `fork` 后 Release 自动创建成功 | 标签可打，但对外公告须等 Release 就绪 |

> **门槛的性质**：这是「能不能公布」的闸门，不是「做得多好」的评分。7 项是底线；任何一项以「先发了再补」的方式绕过，都会让 dev 通道失去「进度可信」的价值。
>
> **欠账代际清偿原则**：上一版欠下的真机验证（门槛 4）必须计入**下一版**的门槛，不允许无限顺延。dev.2 欠的账本次已顺延；《dev.4公布标准.md》§2.1 B 已把它与 dev.3 新增项**合并为硬门槛且不得再顺延**——这是本次范围拆分付出的代价，须在 dev.4 结清。

---

## 4. 公布口径（怎么公布）

### 4.1 发布说明六章在 dev.3 的具体写法（结构依《分支与发布规范》§5.4）

| # | 章节 | dev.3 写法要点 |
|---|---|---|
| 1 | 项目现状 | 重写完成度按 Phase 表陈述（更新为：Phase 0/1/2/5 ✅，Phase 3 **前半 ✅ / 联网半边 ⬜ 顺延 dev.4**，Phase 4/6 ⬜）；保留「未达 alpha 阶段标准」声明 |
| 2 | 版本信息 | §1.1 表格原样落版 |
| 3 | 本版本改动 | 按「新增 / 重写 / 优化 / 修复」分类；「重写」= Phase 3 前半各模块 C# 化，逐项写清**改了什么、为什么改**；**必须显式交代联网半边与真机验证顺延 dev.4** |
| 4 | 开发期发现的缺陷 | **必写**（§5.4 专项要求）：凡本版本修掉的缺陷，不论来自 issue、自测还是代码走查，一律写入「修复」并写清触发条件与现象 |
| 5 | 已知问题 | 「仍沿用 PCL2 的部分」清单中，资源子系统标注为**「搜索 / 版本解析 / 本地扫描已 C# 化；联网加载与在线反查仍是 VB，顺延 dev.4」**（不得整块移出）；**必须写明本版未做真机闭环**；CA1416 技术债、引擎验证覆盖度如实写 |
| 6 | 升级说明 | dev 通道配置/注册表独立、可与正式版并存；升级前备份 `%APPDATA%\.PCL\`；VersionCode 5002 > 5001 不会误弹降级 |

### 4.2 措辞红线（发布说明与公告中违反任一条即重写）

1. 必须自称**开发版**，并写明「仅供开发与测试使用，**不对外分发**」（含义见 §1.2）。
2. 必须写明**未达 alpha 准入标准**及其量化条件（连续 3 个版本无新增 P0、P1 清零），不得暗示「接近可用」。
3. 重写完成度**只按 Phase 表陈述**，禁止百分比、禁止「基本完成」这类模糊词；未 C# 化的部分必须列明（《分支与发布规范》§5.5）。
4. 开发期发现的缺陷一律写入「修复」，**不得只收录 GitHub issue 来源**（§5.4 专项要求）。
5. 上游署名（龙腾猫跃 / Hex-Dragon）标注为**上游 PCL 作者**，与本项目贡献者分列两区块；不得把上游作者写成本项目作者（§5.5，许可义务 L1/L3）。

### 4.3 内置双写（《分支与发布规范》§5.7）

- `ModDevNotes.vb` 追加 `Case 5002`（只追加，不删旧条目）：
  - `DevStageNotice`：面向开发渠道用户，写**功能限制**——Phase 4 未完成意味着下载 UI 仍是 VB 绑定、哪些入口行为可能不一致、使用注意；
  - `DevChangelog`：面向开发者/内测者，写**底层实现**——Provider 抽象结构、CF/MR 客户端的限流与降级策略、MurmurHash2/sha512 统一 HashType 的处理、与 VB 对照的差异点；**不重复** ReleaseNotes 已讲的表层变更。
- 触发机制沿用 §5.7.2（首次启动/版本升级、仅非正式渠道、MyMsgBox 同意制），无代码改动。

---

## 5. 发布操作清单（按序执行，dev.2 已验证路径）

```bash
# 1. 验收合入：feat/* 经 PR 合入 dev，确认无 WIP
git switch dev && git pull fork dev

# 2. ModDevNotes.vb 追加 Case 5002（DevStageNotice + DevChangelog，见 §4.3）

# 3. 写 ReleaseNotes/v2.100.0.3-dev.3.md（六章，见 §4.1）

# 4. 版本常量由脚本写入（禁止手改 ModBase.vb / AssemblyInfo.vb）
./scripts/Set-VersionInfo.ps1 -Tag v2.100.0.3-dev.3
#    → 版本名 2.100.0.3，VersionCode 5002；-CommitHash 可留空（CI 发布构建自动注入）

# 5. 本地构建与冒烟
dotnet build pcl-next.sln -c Release          # 0 错误
./scripts/Test-VersionMatrix.ps1              # 六渠道矩阵全过

# 6. 提交 + annotated tag
git add -A && git commit -m "release(dev): v2.100.0.3-dev.3"
git tag -a v2.100.0.3-dev.3 -m "dev.3：Phase 3 资源子系统 C# 化完成"

# 7. 本地发布制品 + 统一归档（Release + Dev 渠道，压缩单文件，目录结构见 AGENTS.md §0.6）
cd pcl-next
dotnet publish pcl-next.vbproj -c Release -r win-x64 --self-contained \
  -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true \
  -p:PublishDir=../ReleaseArtifacts/Dev/2.100.0.3/win-x64/
# 归档目录结构（与 AGENTS.md §0.6 一致）：
#   PCL2/ReleaseArtifacts/Dev/2.100.0.3/win-x64/
#     ├─ pcl-next.exe          （压缩单文件，与 GitHub Release 附件同源同 tag）
#     ├─ 更新日志.md            （复制 ReleaseNotes/v2.100.0.3-dev.3.md 六章全文）
#     └─ 修复bug.md             （本版本已修复缺陷，从上游遗留问题清单提取）
#   注：DownloadPolicy.ini 非必须（缺失时回退内置默认 AllowMcDownload=True），按需附带。
cp ../ReleaseNotes/v2.100.0.3-dev.3.md ../ReleaseArtifacts/Dev/2.100.0.3/win-x64/更新日志.md
# 修复bug.md 由发布前从 PCL2上游遗留问题清单.md 提取已修复项后写入
# 实跑一次核对：标题「开发版 2.100.0.3 (5002，渠道 dev)」，PCL/Log1.txt 零 Error

# 8. 推送（只推 fork，严禁推 origin=上游）
git push fork dev --tags

# 9. CI 收口：release.yml 由 v* 标签触发，自动构建并创建 GitHub Release（prerelease=true）
#    核对 Release 附件：PCL-2.100.0.3-dev.exe（主程序）与 DownloadPolicy.ini（独立附件）分别上传，不打包 zip；CI 制品含 CommitHash 注入
```

> 步骤 2/3/4 顺序不可换：先有文本再写版本常量，最后一次性提交，保证 tag 指向的提交即含全部发布内容（发布说明、双写、版本常量同 commit，便于回溯）。

---

## 6. dev.4 及以后的节奏

| 版本 | 内容范围（v2 实际取值） | 门槛与口径 |
|---|---|---|
| dev.3 | Phase 3 **前半**（搜索 / 版本解析 / 本地扫描，可离线验证）+ HashType 统一 | 本文 §3 / §4 |
| dev.4 | Phase 3 **后半**（Provider 抽象 / CF-MR 客户端 / CF Key 降级 / 在线反查）+ **两代真机闭环清偿** + Phase 4 首步（条件性） | 《dev.4公布标准.md》§3 / §4 |
| dev.5 | Phase 4 主体（下载 UI 完全重指）+ Phase 6 起步 | 沿用 §3 / §4 |

- **推进代号的条件**（《分支与发布规范》§3.2 D1/D5，仅列参考，**由用户决定**）：C# 重写完成主要模块 + 连续 3 个 dev 版本无新增 P0 + P1 清零 + 用户明确指示 → 才进入 `slush`。
- dev.3 发布后，本文 §1/§2 作废归档，§3/§4 由后续版本的发布标准继承；届时只写「版本身份 + 内容范围」两节即可。

---

## 7. 待确认的决策点

| # | 决策点 | 本文取值 | 备选 |
|---|---|---|---|
| A | dev.3 内容范围 | **已定（v2）：Phase 3 前半 + HashType 统一**，联网半边与真机验证拆 dev.4 | 原 v1 默认（Phase 3 完成 + P0 补强 + 验证闭环）；或 Phase 3+4 合并发 |
| B | P1 补强是否纳入 dev.3 | **不纳入**（CF→MR 跨站下载兜底、依赖图求解器、配额闸门与 429 退避、MCIM `sync_at` 新鲜度判断），顺延至 dev.4 起择要纳入 | 择要纳入（每项 +0.5~1 人日，推迟发布时间） |
| C | dev 版自动更新通道是否随 dev.3 接入 | **不纳入**（独立事项，避免与 Phase 3 耦合） | 纳入 dev.3（需自建更新源，注意 §5.6 渠道隔离要求） |
| D | （新增）真机验证是否继续顺延 | **本次顺延 dev.4**（用户 2026-11-01 决策）；dev.4 起**不得再顺延** | 坚持 v1 口径（dev.3 必须做真机闭环后才发） |

> 决策点 A/D 已拍板，dev.3 按 §2.1 的收缩范围发布；B/C 不阻塞开工。

---

## 8. 修订记录

### v2（2026-11-01）——范围拆分：Phase 3 前半归 dev.3，其余归 dev.4

**改动**：用户指示「先准备发布 GitHub，版本为 dev.3，把后续拆成 dev.4」，据此把 v1 的「Phase 3 完成 + P0 补强 + 验证闭环」拆成两版。

| 项 | v1（原稿） | v2（本版） |
|---|---|---|
| 内容范围 | Phase 3 完成 + 3 项 P0 补强 + 真机闭环 | Phase 3 前半（4 次提交）+ HashType 统一 |
| 顺延项 | — | `IResourceProvider`、CF/MR 客户端、请求签名/候选源、CF Key 降级、`LocalResourceOnlineLoad`；真机闭环 4 项 + 网络冒烟 1 项 |
| 门槛 3 | 真实网络 + 夹具双对照 | 仅离线夹具对照 |
| 门槛 4 | 硬门槛，不过不发 | 顺延 dev.4，dev.3 以人工冒烟替代并在「已知问题」披露 |
| 后续版本 | dev.4 = Phase 4 | dev.4 = Phase 3 后半 + 真机闭环清偿 + Phase 4 首步（条件性） |

**代价与风险（必须在发布说明中如实披露）**：

1. dev.3 **不携带任何真机验证结论**——下载、安装、启动链路均未在真实环境跑通，用户遇到问题时无法据此判断「是本版引入还是既有问题」；
2. 资源子系统的**联网半边仍是 VB**，搜索/本地扫描虽已 C# 化，但 UI 入口尚未切换，实际用户路径上仍以 VB 行为为准，C# 实现处于「已就位、未接管」状态；
3. 真机欠账已连续顺延两代，若 dev.4 再不清偿，会突破「欠账代际清偿原则」的底线（§3 注）。

**结论**：拆分成立的前提是 dev.4 必须结清 §2.1 B 五项。否则应视为 dev 通道进度可信度受损，需回退到 v1 口径（先验证再发）。

### 发布记录（2026-11-01 已发布）

| 项 | 结果 |
|---|---|
| 提交 / 标签 | `74084bb` / `v2.100.0.3-dev.3`（annotated） |
| 构建 | 干净 worktree 上 `dotnet build -c Release` 0 错误；`Test-VersionMatrix.ps1` 11/11；`PCL.Download.Tests` 201/201 |
| 归档 | `ReleaseArtifacts/Dev/2.100.0.3/win-x64/`（`PCL-2.100.0.3-dev.exe` 80MB + `更新日志.md` + `修复bug.md`） |
| GitHub Release | `prerelease=true`；资产 `PCL-2.100.0.3-dev.exe`（83,046,975 B）+ `DownloadPolicy.ini`（473 B），分别独立上传 |

**发布后发现的回归（dev.3 标签构建不含修复）**：`PCL.Download` 新增的 `public static class Logger`
与 VB 的 `MeloongCore.Logger` 同名，`NetInterop.vb` 因 `Imports PCL.Download` 让未限定的 `Logger.Info`
静默解析到 C# 类——C# 引擎日志（开始下载 / 大小 / 竞速 / 换源）在 `Log1.txt` 中缺失。
已在 `dev` 的 `46dd6b2` 将类改名 `DownloadLog` 从源头消除歧义；**该修复随下一个 dev 版本生效**。
dev.3 的 Release 页建议手工补一条「本版引擎日志不完整」的已知问题说明。
教训：向 `PCL.Download` 添加任何 `public` 类型前，先排查 VB 侧 `Imports PCL.Download` 作用域内的同名符号。


