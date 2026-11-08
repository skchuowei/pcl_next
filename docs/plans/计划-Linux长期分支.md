# 计划：Linux 长期分支（平台分支的建立、同步与回灌）

> 依据：《AGENTS.md》§3.1/§3.2（大改动必须先写专项计划）、§零（产物路径）、§九（只标不删）；
> 《分支与发布规范.md》§0/§1/§3.2/§5.2/§5.2.1/§5.3；《计划-内核与GUI拆分（CLI）.md》§0（T2 排在 Phase 6 后）。
> 状态：**✅ 已裁决生效（2026-11-07 09:39）**。用户 09:26 指示「开始 liunx 分支开发」并选择
> 「明确推翻红线，建长期 linux 分支」，09:39 指示「把 liunx/安卓开发提上日程，改规则」，
> 据此裁决为 **android / linux 两条平台长期分支并存**，本文与《AGENTS.md》§十一 同步生效。
>
> **冲突与裁决留痕（只标不删）**：09:34 曾发现另一路工作把「android 是唯一平台长期分支、
> 再建 linux 属反模式」写入《分支与发布规范》§0A / §7.2 / §9 与《AGENTS.md》§十，并挂起相关条款。
> 该条已由 09:39 裁决统一撤销：《分支与发布规范》新增 §0B 定义 linux 平台线，
> §0A / §7.2 / §9 决策点 E 与 AGENTS §十 / §十一 / §附 同步修订，挂起解除，L0 可排期执行。
> 本文为专项计划，L2 完成后随版本归档，不作为长期规则入口；长期纪律以
> 《分支与发布规范》§0B 与《AGENTS.md》§十一 为准。

---

## 0. 决定记录（撤销一条既有红线）

| 项 | 内容 |
|---|---|
| 原结论（2026-11-07 08:20 分析） | **不开长期 linux/android 分支**；平台被定义为 RID/TFM + CI 矩阵维度，仍是单一 `dev` + `feat/*`；Linux 正式动工排在 Phase 6 + T2 拆壳后（约 dev.6~dev.7） |
| 用户新指示（2026-11-07 09:26） | **明确推翻红线，建立长期 `linux` 分支** |
| 撤销后**继续有效**的部分 | ① Linux 与 Windows 同步发布（同 tag、同 VersionCode、同一份发布说明、多 RID 附件）；② 合规书面问询为动跨平台代码的前置；③ 不叫「PCL 手机版 / PCL Mobile」；④ 不中断 dev.4/5 的 Phase 4 收尾与真机闭环；⑤ 不另起版本号段 |
| 本次**不推翻**的部分 | Phase 6 / T2 拆壳仍排在 GUI 移植之前——`linux` 分支是**隔离带**，不是「提前拆壳」的许可 |
| 裁决（2026-11-07 09:39） | 用户指示「把 liunx/安卓开发提上日程，改规则」→ 确认 **android / linux 两条平台长期分支并存**；撤销「android 唯一」一条，§0B / §十一 生效，挂起解除。本轮只改规则文档，未建分支、未动代码 |

**为什么要一条长期分支而不是纯 CI 矩阵**：Linux GUI 移植（VB → C# + Avalonia）是持续数周、破坏性的改造，
塞进 `dev` 会直接打断 Phase 4 收尾和真机验收。长期分支的价值是**隔离**，不是**分叉发版**——
本计划用 §5.3/§5.4 把「隔离」和「单号段表、同步发布」同时保住。

---

## 1. 目标与可验证标准

| 阶段 | 目标 | 可验证标准 | 本轮范围 |
|---|---|---|---|
| **L0** | 建立长期 `linux` 分支 + 跨平台编译护栏 | `build-linux` CI job 绿（编译内核 + xUnit + `selftest-offline`）；Windows 侧全量构建与测试不回退 | 🟡 规则已生效，分支与 CI 待执行 |
| **L1** | 内核 / CLI 在 `linux-x64` 真实可用 | `pclcli selftest --suite offline` 在 Linux 退出码 0；下载引擎路径、磁盘预检无 Windows 假设 | ⬜ 后续 |
| **L2** | Linux GUI（Avalonia P0）稳定后整体回灌 `dev` | P0 页面（实例列表 + 下载进度）Linux 桌面可跑；回灌后 Windows 构建 0 错误 | ⬜ 后续，需另立专项计划 |

**明确不做（本轮）**：不改 VB 主工程、不引入 Avalonia、不做 GUI 移植、不做安装包/AppImage、
不新建 `PCL.Core`、不拆 T2、不发 Linux 版 Release 附件、不新建版本号段、不改用户未提交的两个文件。

---

## 2. 前置条件与阻塞项

| # | 项 | 状态 | 影响 |
|---|---|---|---|
| B1 | **合规书面问询上游作者** | ⬜ 未做 | 阻塞「动跨平台功能代码」与任何对外分发；**不阻塞** L0 的编译护栏（护栏不新增分发行为）。问询草稿见 §2.1，须由用户自行发送并留存答复 |
| B2 | `wsl.exe` 仅被 agent 侧命令沙箱屏蔽 | ⚠️ 环境限制 | **用户机器 WSL 完全可用**（已实测 WSL 3.0.3.0 + Ubuntu 24.04 导入成功、VHDX 在 D 盘）；Linux 侧实跑由用户执行，或走 CI `ubuntu-latest`；agent 只能做 Windows 侧验证 |
| B3 | Phase 6 / T2 拆壳未完成 | ⬜ | 阻塞 L2；L0/L1 只碰无 GUI 内核，不受影响 |
| B4 | `Directory.Build.props` 反斜杠路径 | 🟡 工作区已有未提交改动，L0 前须核对 | Linux 上 `Exists('...\ModBase.vb')` 为 false → `PclVersion` 空 → `PclTreePathGuard` 报错；须确认四处分隔符已改正斜杠，且 Windows `OutputPath` 改前/改后一致 |
| B5 | 用户未提交的 `DownloadFileTask.cs` 下载死锁修复 | 🔒 用户自行处理 | **不碰**。其 `CheckDiskSpace` 的 `Path.GetPathRoot` + `DriveInfo.GetDrives()` 在 Linux 匹配不到时静默 `return`（不崩，但磁盘预检失效），待用户提交后由 L1 复核 |
| B6 | `DownloadEngine.cs` 路径校验 | ✅ 已解除 | `e75d474` 已改为 `OperatingSystem.IsWindows()` 分支，非 Windows 走 `Path.IsPathRooted` |
| B7 | `build.yml` push 分支列表写的是 `pc` 而非 `rc` | ⚠️ 待核对 | 与《分支与发布规范》§1 的六条长期分支 `dev/slush/alpha/beta/rc/snapshot` 不一致。**本轮不顺手改**（超出范围），记入待核对清单 |

### 2.1 合规问询草稿（须由用户发送，agent 不代发）

依据《PCL 存储库合理使用指南》：LICENCE 只授权「原样副本」的非商业再分发，未授予修改/派生作品再分发权；
项目作为 fork 依靠指南里「善意合理使用则不追究」的承诺。全文与指南**均无平台限制条款**，
但指南两处明令禁用「PCL 手机版」名称，重度使用要求 PCL 前缀 + 第三方后缀、关于页首位署名 + 赞助链接、
开源、L4 正版劝导；**L5（蓝色主色相）原文限定「在 Windows 系统上运行时」**，Linux 版主色是否沿用需一并确认。

> 拟发送渠道：指南给出的 GitHub Discussions 链接（不是 issue，不代发、不代注册账号）
>
> 正文草稿：
> 您好。我们是 PCL2 的第三方修改版 pcl-next（PCL Next），目前仅在 Windows 分发，
> 遵守 PCL 前缀 + 第三方后缀命名、关于页首位署名与赞助链接、开源、正版劝导等要求。
> 现计划增加 Linux 平台的构建与分发（同一版本、同一更新通道、多 RID 附件），
> 后续可能评估 Android。想书面确认三件事：
> ① 存储库合理使用指南中的善意合理使用承诺，是否同样适用于非 Windows 平台的分发？
> ② L5「蓝色主色相」原文限定「在 Windows 系统上运行时」，Linux 版沿用同一主色是否需要另行授权或调整？
> ③ 是否有我们不应对外使用的名称或表述（已知禁用「PCL 手机版」）？
> 在收到答复前，我们不会对外分发任何非 Windows 平台的制品。谢谢。

**留存要求**：答复链接/截图存入 `docs/合规/`（现有《跨平台授权问询.md》已同时覆盖 Android / Linux，实际发送以该文档为准），并在首次非 Windows 版发布说明的「升级说明」章节引用。

---

## 3. 现状分析

### 3.1 工程 TFM 与平台归属（实测 2026-11-07）

| 工程 | TFM | 平台属性 | Linux 可编译 |
|---|---|---|---|
| `pcl-next`（VB 主程序，213 个 `.vb`） | `net10.0-windows` + `UseWPF` + `UseWindowsForms` | Windows 独占 | ❌ VB.NET 无跨平台编译器 |
| `PCLCS` / `MeloongCore.Wpf` | `net10.0-windows` + `UseWPF` | Windows 独占 | ❌ |
| `MeloongCore/Shared` | `net10.0` | 部分入口含 Registry / MsDelta（Windows 专属） | ⚠️ 需逐条筛 |
| `PCL.Download` | `net10.0`，纯 BCL，0 第三方包 | 跨平台 | ✅ |
| `PCL.Cli` | `net10.0`，`UseWPF=false`，`PclIsGuiFree=true` | 跨平台 | ✅ |
| `PCL.Download.Tests` | `net10.0`（xUnit） | 跨平台 | ✅ |
| `MeloongCore/Tests` | `net48` | .NET Framework 独占 | ❌ |

> 三个内核工程**不在 `pcl-next.sln` 中**，必须直接对 `.csproj` 构建。这是 `build-linux` job
> 能绕开 WPF 的关键：Windows job 编 `pcl-next.sln`，Linux job 只编这三个 `.csproj`。

### 3.2 唯一的硬阻塞：反斜杠路径（§0.3.1 同型问题）

`Directory.Build.props` 的 `PclModBaseFile`、`PclBuildCacheFile`、`PclTreeRoot`、`PclGuiTreeRoot`
都用 `pcl-next\...` 这类 Windows 反斜杠字面量。Linux 上：

- `Exists('...\Modules\Base\ModBase.vb')` → false → `PclVersion` 为空 → `PclVersionDir` 为空；
- `PCL.Cli.csproj` 声明 `PclUseTreePath=true`，`PclTreePathGuard` 因缺版本段直接编译失败；
- 即使绕过护栏，产物目录名里会出现字面 `\` 字符。

**改法**：把这四个值里的路径分隔符换成正斜杠 `/`（Windows MSBuild 同样接受正斜杠），
而不是在 Linux CI 里用 `-p:PclUseTreePath=false -p:OutputPath=/tmp/...` 绕开。
理由见《AGENTS.md》§0.3.1：**凡是「必须落在某路径」的规则，绝不能让命令行传参正确代替配置本身正确**，
否则又会形成「走脚本是对的、不走脚本是错的」分裂状态。

### 3.3 CI 与规范文档的位置

- `build.yml`：`runs-on: windows-latest` only，push 触发 6 条长期分支；`release.yml` 同样只出 `win-x64` 单文件 exe。
- **规范文档在 git 仓库外**：仓库根 = `PCL2/`，而 `AGENTS.md`、`分支与发布规范.md`、
  `dev.3公布标准.md` 位于工作区根 `D:\bing\MC\pcl_x\`，**不受 git 跟踪**。
  → 好处：规则全局唯一真源，不会因 `linux` 与 `dev` 分叉而出现两份互相矛盾的规范（正是 §0.3.1 要避免的）。
  → 代价：改规范不会进任何提交，需在两个分支上人工确认口径一致（见 §6）。

---

## 4. 改动文件清单

### L0 新增 / 修改

| 文件 | 位置 | 改动 | 随分支走 |
|---|---|---|---|
| `docs/plans/计划-Linux长期分支.md`（本文） | 仓库内 | 新建 | ✅ |
| `.github/workflows/build.yml` | 仓库内 | push 分支列表加 `linux`；新增 `build-linux` job | ✅ |
| `Directory.Build.props` | 仓库内 | 4 处路径分隔符 `\` → `/`；补注释说明跨平台约束 | ✅ |
| `AGENTS.md` | 仓库**外** | §0.1 RID 列表、§十 / §十一 平台分支纪律、§附 反模式清单、顶部裁决提示 | ❌ 全局（09:39 已完成） |
| `分支与发布规范.md` | 仓库**外** | §0 速览表与图注、§0A 撤销「唯一」、新增 §0B、§6.5 可写分支、§7.2 反模式、§9 决策点 E；按 §九「只标不删」留痕 | ❌ 全局（09:39 已完成） |

### 明确不碰（本轮）

- `PCL.Download/DownloadFileTask.cs`、`PCL.Download.Tests/DownloadEngineTests.cs`（用户未提交改动，🔒 B5）；
- `pcl-next.sln`、`pcl-next.vbproj`、`PCLCS`、`MeloongCore.*`（GUI 侧，L2 再说）；
- `release.yml`（Linux 发布附件属阶段 B，L0 不做）；
- `scripts/*.ps1`（只在 Windows job 跑，本轮不改）；
- `ModBase.vb` / `AssemblyInfo.vb`（版本常量由发布流程写，§5.2 硬规则）。

---

## 5. 改动要点

### 5.1 分支拓扑：`linux` 是正交平台维度，不进代号阶梯

```
 上游 main ┄┄► dev ──► slush ──► alpha ──► beta ──► rc ══► [发版 + 版本 tag]
                 │  ▲
      §5.2 同步  │  │ §5.3 反向（仅平台无关修复，且优先在 dev 直接做）
        dev ─────┘  │
                 ▼  │
               linux ────► feat/linux-* ────► linux
                 │
                 └──► linux/<里程碑> 归档 tag（非版本 tag，不进 §5.2 号段）
```

| 项 | 规定 |
|---|---|
| 基线 | `git switch -c linux dev`，基点 = `c27d427` / `v2.100.0.4-dev.4` |
| 定位 | 长期**平台集成线**，与代号阶梯正交；不是第七个代号，不参与 `dev → … → rc` 流转 |
| 短期分支 | `feat/linux-*`、`fix/linux-*` 从 `linux` 拉出、合回 `linux`，禁止指向 `alpha/beta/rc` |
| 历史 | 与已发布分支同样「只增不改史」：禁止 rebase / force-push / 删除分支（除非走 §六 回退报告） |
| 版本 tag | **`linux` 分支不打版本 tag**。`v<版本>[-<代号>.N]` 由主阶梯独占（§5.2） |
| 非版本 tag | 允许 `linux/L0`、`linux/L1` 这类里程碑归档 tag，命名带 `linux/` 前缀，避免被 `v*` 发布工作流误触发 |

### 5.2 同步：`dev → linux`（单向、定期、整包）

| 项 | 规定 |
|---|---|
| 触发 | 每个 `dev`/`slush`/`alpha`/`beta` 版本 tag 推送后 **24h 内**；或 `dev` 合入影响内核的 PR 后按需同步 |
| 方式 | 在 `linux` 上 `git merge --no-ff dev -m "merge: 同步主线 <tag>"`，**禁止 rebase** |
| 冲突口径 | 内核共享代码**以 `dev` 为准**；Linux 侧适配一律用 `OperatingSystem.IsWindows()` / 条件编译 / 多 TFM 隔离，**不复制实现** |
| 禁止 | 把 `linux` 的 Avalonia 工程、RID 专属配置整包合回 `dev`（§5.3） |

### 5.3 反向：`linux → dev`（刻意做窄）

**设计取舍**：不做「逐级回灌」式的整分支合并——那会把未稳定的 GUI 改造冲进主线。

- **平台无关修复优先直接在 `dev` 上做**，再按 §5.2 同步进 `linux`。这样修复天然只存在一份。
- 仅当 Linux 验证**暴露了** `dev` 上也存在的缺陷时：在 `dev` 上走 `fix/dev-*` 落地同等修复，
  再由 §5.2 同步回 `linux`；在 `linux` 上的临时修法应被同步覆盖掉，不保留双实现。
- **禁止** `linux → dev` 整分支合并；**禁止**把 Linux 专属工程、Avalonia 依赖、RID 专属 csproj 配置带回 `dev`。
- 每次反向修复在合并提交信息里注明「Linux 验证暴露，平台无关」，便于追溯。

> 与《分支与发布规范》§3.4 的关系：那里管的是**代号阶梯内部**的逐级回灌（rc → beta → … → dev），
> 本节管的是**跨平台维度**的同步，两者互不替代、互不冲突。

### 5.4 版本 / tag / 制品：单号段表，不打架（关键）

| 项 | 规定 |
|---|---|
| 号段 | **只有 §5.2 一张 VersionCode 号段表**。Linux 不另起号段、不另起 `PATCH` 段、不出现第二套 `base + 偏移` |
| 构建号 | §5.2.1 的构建小版本 = 自固定锚点 `v2.100.0.1-dev.1` 以来的提交数。**发布构建号一律以主阶梯 tag 所指 commit 为准**：CI 在 tag 上检出并计算，linux 分支自身的提交不单独产生发布编号，避免同一个 tag 在两条分支算出两个编号 |
| 阶段 A（当前，GUI 未稳定） | `linux` **只产内核/CLI 制品**，作为 CI artifact 留存，命名 `pclcli-<版本名>-<构建号>-linux-x64`；**不进 GitHub Release 主附件**、不对外分发（B1 未解除前尤其不发） |
| 阶段 B（L2 回灌 `dev` 后） | CI 在**发布 tag 的 commit** 上用多 RID 矩阵出 `win-x64` + `linux-x64` 附件，实现「同 tag / 同 VersionCode / 同一份发布说明 / 多 RID 附件」的同步发布；制品名补 RID 后缀 `PCL-Next-<版本名>-<代号>-<构建号>-<RID>` |
| 归档 | `ReleaseArtifacts/<代号>/<版本名>/linux-x64/`（§0.6 模板已有 `<运行环境>` 层，直接落位，不改规则） |
| 更新通道 | 不新建 Linux 更新通道；阶段 B 起与 Windows 共用同一通道，靠 RID 选附件 |

### 5.5 产物路径

- §0.1 的 `<运行环境>` 取值扩为 `win-x64` / `win-arm64` / **`linux-x64`** / **`linux-arm64`** / `any`。
- 三段树结构与 `cli/` 子层不变：`bin-net10/<代号>/<版本名>-<构建号>/linux-x64/cli/`。
- **不用 `-o` 短路径、不用 `-p:OutputPath` 覆盖**（§0.2/§0.3 锁死）；正斜杠改造后在 Linux 原生落到三段树。

### 5.6 CI：`build-linux` job

| 项 | 规定 |
|---|---|
| runner | `ubuntu-latest`，与现有 `build` job（windows-latest）并列，**不改动 Windows job** |
| 触发 | push 分支列表加 `linux`；PR 与 `workflow_dispatch` 继承现有 paths 过滤 |
| 只做四件事 | ① `dotnet build PCL.Download/PCL.Download.csproj -c Release -r linux-x64`；② `dotnet build PCL.Cli/PCL.Cli.csproj -c Release -r linux-x64`（顺带校验 `PclIsGuiFree` 架构护栏与三段树护栏）；③ `dotnet test PCL.Download.Tests -c Release`；④ `pclcli selftest --suite offline --workdir <tmp>` |
| 禁止 | 编 `pcl-next.sln`、编任何 `net10.0-windows` 工程、跑 `Update Help`（PowerShell/Windows 路径）、跑 `Set-VersionInfo.ps1` / `Replace`（会写 VB 源码）、出 Release 附件 |
| 构建号缓存 | job 内用 `Get-BuildSmallVersion.ps1` 刷新（`ubuntu-latest` 自带 pwsh）；若脚本有 Windows 路径假设，允许本次 job **跳过刷新**并接受目录名退化为纯版本名（§0.3.2 允许），但须在日志打印说明，且**不得**用 `-p:OutputPath` 绕开 |
| 判定 | job 绿 = 内核保持可跨平台；任何一步失败即阻断 `linux` 分支合入 |

### 5.7 合规边界（PCL 许可 L1–L5）

| 义务 | Linux 分支落地要求 |
|---|---|
| L1 | 持续声明「PCL2 第三方修改版」，不因换平台而自称原版或官方版 |
| L2 | 名称带 PCL 前缀 + 第三方后缀；**禁用「PCL 手机版 / PCL Mobile」**（指南两处明令），Linux 版同理不用「PCL 官方 Linux 版」 |
| L3 | 关于页首位保留上游作者署名 + 赞助链接 + 开源声明；三个平台都要有 |
| L4 | 正版劝导在 Linux 版保留 |
| L5 | 蓝色主色相原文限定「在 Windows 系统上运行时」→ **Linux 版主色待书面确认**（§2.1 问题②），确认前不对外分发 |
| 前置 | §2.1 问询答复留存前，不发布任何非 Windows 制品；L0 编译护栏不属分发，不受此限 |

---

## 6. 回归影响面

| 面 | 影响 | 处置 |
|---|---|---|
| Windows 全量构建 | `Directory.Build.props` 改正斜杠后，MSBuild 在 Windows 同样接受 `/`，但 `OutputPath` 求值可能变化 | 改前/改后各跑一次 `-getProperty:OutputPath` 对照，必须完全一致；再跑 `dotnet build pcl-next.sln -c Release` 0 错误 |
| `PclTreePathGuard` | 护栏内 `$(OutDir.Replace('\','/'))` 归一化对正斜杠路径仍成立 | 构建时确认护栏输出「产物路径护栏通过」 |
| `scripts/*.ps1` | 只在 Windows job 调用，Linux job 不碰 | 无影响 |
| 六条长期分支 CI | 只新增分支条目与并列 job，不改既有步骤 | 需确认 Windows job 未因 `on.push.branches` 改动被意外触发/漏触发 |
| 规范文档在仓库外 | 改了不进提交，两个分支看到的都是同一份 | 每轮收尾 `git status` 时口头确认口径一致；**不得**把 `AGENTS.md` 复制进仓库造第二份真源（§0.3.1 教训） |
| 用户未提交改动 | `git switch -c linux dev` 会带着未提交改动过去，内容一字不变 | 提交时只 `git add` 本轮文件，**绝不** `git add -A`（§五.3）；最终回复里提醒用户当前 HEAD 已在 `linux` |
| 版本/tag | `linux` 不打版本 tag | 确认 `release.yml` 的 `on.push.tags: v*` 不会被 `linux/*` 归档 tag 触发（前缀不同，天然不触发） |

---

## 7. 验证步骤

**Windows 侧（agent 执行）**

```bash
# 1) 改前基线：记录 OutputPath，改后必须一致
dotnet build PCL2/pcl-next/pcl-next.vbproj -getProperty:OutputPath \
  -p:Configuration=Release -p:RuntimeIdentifier=win-x64

# 2) 全量构建 0 错误
dotnet build PCL2/pcl-next.sln -c Release

# 3) 内核单测 + 离线自检
dotnet test PCL2/PCL.Download.Tests/PCL.Download.Tests.csproj -c Release
pclcli selftest --suite offline --workdir <tmp>
```

**Linux 侧（用户执行，或 CI ubuntu-latest）**

```bash
cd /mnt/d/bing/MC/pcl_x/PCL2        # WSL Ubuntu-24.04 已跑通
dotnet build PCL.Cli/PCL.Cli.csproj -c Release -r linux-x64
dotnet test  PCL.Download.Tests/PCL.Download.Tests.csproj -c Release
# 产物落 bin-net10/Dev/<版本>-<构建号>/linux-x64/cli/，护栏应输出「通过」
pclcli selftest --suite offline --workdir /tmp/pclcli
```

**L0 验收判定**：Windows 侧 1/2/3 全过且 `OutputPath` 前后一致；`build-linux` CI job 绿。
Linux 侧若因 B2 未能实跑，以 CI job 结果为准，并在收尾记录「本机未实跑」。

---

## 8. 回滚方案

| 场景 | 回滚动作 | 保留内容 |
|---|---|---|
| `linux` 分支未推远端即决定放弃 | `git switch dev && git branch -D linux`；按《AGENTS.md》§六 出 `回退报告-Linux分支.md` | `dev` 全部内容；用户那 2 个未提交改动原样带回 |
| 已推 `fork/linux` 后放弃 | 先打归档 tag `archive/linux-abandoned`，再删远端分支；**不 force-push 覆盖** | 归档 tag 保留历史 |
| 正斜杠改造破坏 Windows 构建 | 单文件 revert `Directory.Build.props`（不 revert 其余文件） | CI job 与规范改动保留，护栏临时标「待修」并按 §九 记入待核对清单 |
| `build-linux` job 持续红且短期修不好 | 先移除该 job、保留 `linux` 分支与规范 | 分支与同步纪律不变，job 作为 L0 未达标项挂起 |
| 规范撤销需回退 | 按《AGENTS.md》§九「只标不删」追加新决定记录，**不删除** §0 的撤销说明 | 历史决策链完整 |

回滚一律禁止静默回退；涉及已推送分支的，先出回退报告再操作。

---

## 9. 待核对清单

1. `build.yml` 的 `on.push.branches` 写 `pc` 而规范是 `rc`（B7）——确认是否笔误，另行修正，本轮不动。
2. `Get-BuildSmallVersion.ps1` 是否含 Windows 路径假设；若有，Linux job 按 §5.6 跳过刷新并留痕。
3. `MeloongCore/Shared` 的 `Registry` / `MsDelta` 入口清单（今早分析已点名，L1 逐条筛）。
4. `DownloadFileTask.CheckDiskSpace` 的 `DriveInfo` 在 Linux 行为（B5，待用户提交后复核）。
5. L5 主色在 Linux 的授权口径（等 §2.1 答复）。
