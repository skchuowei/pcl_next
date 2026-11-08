# 交接文档：P4-A 编排决策 DTO 全集 → Linux 线

> **本文只交接 P4-A 这一批改动**：5 个平台无关的 C# 文件（新增 2 / 修改 3）。
> 不交接 dev.6 整体阶段说明、口径裁决文本、发布号段、构建环境史；那些是 Windows 专属产物，Linux 侧只能参考、不能继承。
> **来源**：`PCL_NEXT_Windows` / `dev.6-Windows`，HEAD `38485e8` + 本轮 P4-A 工作区改动（尚未提交）。
> **目标**：`PCL_NEXT_Linux` / `dev.6-Linux`。
> **性质**：知识交接 / 移植指引。三个仓库各自 `git init`、互不合并，本文**不产生任何跨仓库改动**，是否移植由 Linux 线自行决定并自行提交。
> **Linux 侧取用**：可自行把本文复制一份到本仓库 `docs/` 存档；Windows 侧的正本随本轮提交留在 `PCL_NEXT_Windows/docs/`。

---

## 0. P4-A 做了什么（一句话）

把合并安装的**全部编排决策**从 VB 里的 `New LoaderTask/Combo/Download` 字面量与 `New FileChecker With {…}` 表达式，收编成 `PCL.Download` 内核里的**纯数据 DTO**：

- 任务描述符：加载器种类、输入/输出槽位、子步骤骨架、下载文件类别、取消语义、进度语义；
- 下载文件描述符：从 dev.6 仅有的 1 类（Fabric 主文件）扩到 **13 类**，把散落在 VB 各处的校验口径变成单一真源。

**不属于 P4-A**：VB 薄装配层（P4-B）、下线 VB 下载模块（P6）、口径 A 裁决本身（见 §1，仅作前提）、`LoaderBase` 状态机下沉（T2，排在 P6 之后）。
**Linux 侧的价值**：这 5 个文件全在 `PCL.Download` / `PCL.Download.Tests`，目标框架 `net10.0` 纯 BCL、`PclIsGuiFree=true`、零第三方依赖、不引用任何 VB / WPF 类型，**平台无关，可直接移植**。

---

## 1. 前提：口径 A（背景，Linux 须自行裁决）

Windows 把 Phase 4 退出口径裁决为 **A**：C# 输出全部编排决策与任务描述符 DTO，VB 只做「DTO → `LoaderBase` 树」的无逻辑装配，执行 lambda 仍在 VB。原因：`LoaderBase` / `LoaderTask` / `LoaderCombo` 定义在 VB 主工程 `ModLoader.vb` 且带 UI 事件，内核声明 `PclIsGuiFree=true` 无法产出它；提前下沉状态机等于把 T2 拆壳提前。

- Windows 裁决全文：`docs/plans/计划-Phase4收尾与Phase6下线.md` §0.1、`ReleaseNotes/v2.100.0.5-dev.6.md`。
- ⚠ **生效范围仅 `dev.6-Windows`**。Linux 线虽护栏与 `ModLoader.vb` 形态相同，是否采用 A 必须由 Linux 线自己写进自己的规则文档与计划，**不得把 Windows 裁决写成「全项目生效」**。
- Linux 侧 `docs/plans/计划-Phase4主体-下载数据源C化.md` §4.9 仍是「后续（未动）」，与 Windows 已改标的「已移交」不同；移植时由 Linux 侧自行决定是否改标。

> P4-A 的代码本身不依赖口径裁决结果：`InstallPlanner.Build` 的调用签名、步骤顺序、权重、Block/Show 布尔式全部未改，Linux 侧即使暂不裁决也能先移植并跑通单测。

---

## 2. 基线核对（2026-11-08 实测，可直接采用结论）

| 文件 | Windows HEAD（P4-A 前） | Linux 工作区 | 内容结论 |
|---|---|---|---|
| `PCL.Download/InstallPlan.cs` | 264 行 / LF | 264 行 / CRLF | **逐字相同** |
| `PCL.Download/InstallDownloadPlan.cs` | 85 行 / LF | 85 行 / CRLF | **逐字相同** |
| `PCL.Download.Tests/InstallPlanTests.cs` | 251 行 / LF | 251 行 / CRLF | **逐字相同** |
| `PCL.Download.Tests/InstallDownloadPlanTests.cs` | 57 行 / LF | 57 行 / CRLF | **逐字相同**（本轮未改，无需移植） |

> 核对方法：把两边内容统一为 LF、去 BOM 后比较，四个文件均 **完全一致**；原始字节哈希不同**只因换行符**（Windows git 对象为 LF，Linux 工作区为 CRLF）。

其余基线：

| 项 | Windows | Linux |
|---|---|---|
| `PCL.Download` `.cs` 文件数 | 43 → **44** | 43 |
| `PCL.Download.Tests` 文件数 / 实跑用例 | 33 / 481 → **34 / 541** | 33 / **481** |
| `InstallTaskDescriptor.cs` | ✅ 新增 | ❌ 无 |
| `InstallTaskDescriptorTests.cs` | ✅ 新增 | ❌ 无 |
| VB 调用点 | `ModDownloadLib.vb:1659` 调 `InstallPlanner.Build` | ✅ **同位置 `:1659`**（起点一致） |
| `PCL.Download.Tests.csproj` | SDK 风格、默认 glob | ✅ 同；**新增测试文件无需改 csproj** |

**结论**：Linux 处于「P4-A 之前」的干净基线，没有本地语义漂移，可用**覆盖式复制**直接落地，不必做三方合并。移植后按 Linux 仓库既有 CRLF 工作区风格落盘，避免混合换行符（git 入库由各自 `.gitattributes` 规范化）。

---

## 3. 交接清单：5 个文件的 delta

| # | 文件 | 状态 | Windows 侧 delta | Linux 侧动作 |
|---|---|---|---|---|
| 1 | `PCL.Download/InstallTaskDescriptor.cs` | **新增 346 行** | 2 个枚举 + 只读 `InstallTaskDescriptor` + `InstallTaskDescriptors.Create` 规格表 + `InstallTaskPlanner.Describe` | ✅ 逐字移植 |
| 2 | `PCL.Download.Tests/InstallTaskDescriptorTests.cs` | **新增 414 行** | 26 个 `[Fact]/[Theory]` 守门用例，逐步骤对照 VB 断言 | ✅ 逐字移植 |
| 3 | `PCL.Download/InstallPlan.cs` | 264 → **330 行**（净 +66） | `InstallPlanStep` 挂装配规格（9 个只读投影属性）+ `internal Attach`；`InstallPlanRequest` 补 `HasMinecraftJson`；`InstallPlan.AttachDescriptors`；`InstallPlanner.Build` 末尾注入规格 | ✅ 移植 |
| 4 | `PCL.Download/InstallDownloadPlan.cs` | 85 → **246 行**（净 +161） | 新增 `NetFileDescriptorKind`（13 类）、`NetFileDescriptor.Kind` / `SimulateBrowserHeaders`；`InstallDownloads` 工厂从 1 个扩到 13 个 | ✅ 移植 |
| 5 | `PCL.Download.Tests/InstallPlanTests.cs` | 251 → **305 行**（+54） | 追加 4 例：描述符与步骤一一对应、权重/Block/Show 不得重算、`AttachDescriptors` 注入、反向守门 | ✅ 移植 |

三个已跟踪文件的合计 diff：**+295 / −14**；加两个新文件共 **760 行**。
**未在交接范围**：Windows 的收尾计划、dev.6 阶段说明、Phase4 主体 §4.9 改标、提交钩子修复、经验文档——均为 Windows 专属，Linux 侧另写自己的对应文档。

---

## 4. 跨语言契约（移植后必须逐字一致）

Linux 侧若后续自己写 P4-B 装配层，读的就是这组 API；命名与语义分叉会让后续同步变困难。

| 类别 | 符号 |
|---|---|
| 枚举 | `InstallLoaderKind`：`Task` / `Combo` / `Download`；`InstallCancelSemantics`：`Inherit` / `Cooperative` / `WaitForClient` / `CancelIfUnavailable` |
| 描述符 | `InstallTaskDescriptor`（只读）：`Kind` / `Name` / `LoaderKind` / `ProgressWeight` / `Block` / `Show` / `Inputs` / `Outputs` / `SubSteps` / `DownloadKinds` / `Cancel` / `ReportsProgress` / `SubStepsSource` |
| 规格工厂 | `InstallTaskDescriptors.Create(kind, name, progressWeight, block, show, request?)`；`InstallTaskPlanner.Describe(plan)` / `Describe(request)` |
| 步骤投影 | `InstallPlanStep` 的 `Task` / `Inputs` / `Outputs` / `SubSteps` / `DownloadKinds` / `LoaderKind` / `Cancel` / `ReportsProgress` / `SubStepsSource`（全部代理到 `Task`，懒构造） |
| 计划 | `InstallPlan.AttachDescriptors(request)`；`InstallPlanRequest.HasMinecraftJson`（默认 `false`）；`InstallPlanner.Build` 末尾自动调用 `AttachDescriptors` |
| 下载 | `NetFileDescriptorKind`（13 类）；`NetFileDescriptor.Kind` / `SimulateBrowserHeaders`；`InstallDownloads` 的 13 个工厂 |

**不变式**（护栏未破，Linux 侧也应保持）：

- `InstallPlanRequest` 原有 **15 个字段语义零改动**，所有 `Block` / 权重 / `Show` 布尔式零改动；
- `AttachDescriptors` **不重算**任何决策，只把 `step` 自身的 `ProgressWeight/Block/Show` 原样传给描述符；`request` 只用于 OptiFine 分流与 Client 可选子步骤；
- `LoaderBase` / `LoaderTask` / `LoaderCombo` 公开签名不变，内核仍 `net10.0` 纯 BCL、`PclIsGuiFree=true`、零第三方依赖；
- **VB 主工程零改动**，用户可见行为无变化；`NetFile` 构造仍留在 VB。

---

## 5. 事实来源对照（最容易抄错的部分）

P4-A 的价值是「把 VB 散落的口径收编成单一真源」。Linux 侧移植时请连同这些**事实来源**一起接收，否则代码能编译但语义会漂。

### 5.1 13 类下载描述符 → VB 构造点与校验口径

| Kind | 工厂 | VB 构造点 | 校验口径 |
|---|---|---|---|
| `VanillaJson` | `VanillaJson` | `ModDownloadLib.vb:23` | `IsJson = True` |
| `VanillaJar` | `VanillaJar` | `ModDownload.vb:23/27` | `MinSize=1024`、`Hash=sha1`、`ActualSize=size`（缺 size 回退 **−1**，不是 null） |
| `VanillaLibrary` | `VanillaLibrary` | `ModMinecraft.vb:1894` | `{ActualSize, Hash}` |
| `AssetIndex` | `AssetIndex` | `ModDownload.vb:47` | `IsJson = True` |
| `AssetObject` | `AssetObject` | `ModMinecraft.vb:2055/2075` | `{ActualSize = If(Size=0, -1, Size), Hash}` |
| `OptiFineInstaller` | `OptiFineInstaller(asMod)` | `ModDownloadLib.vb:505 / :615` | 安装 **300 KB** / 作为 Mod 另存为 **64 KB** |
| `ForgeInstaller` | `ForgeInstaller` | `ModDownloadLib.vb:854 / :1037` | `{MinSize, Hash}` |
| `NeoForgeInstaller` | `NeoForgeInstaller` | `ModDownloadLib.vb:849 / :1031` | **只有 `MinSize`，不带 Hash**（不得顺手补上） |
| `LiteLoaderInstaller` | `LiteLoaderInstaller` | `ModDownloadLib.vb:723` | `MinSize = 1 MB`（合并安装里的 LiteLoader 不产 `NetFile`） |
| `FabricInstaller` | `FabricInstaller` | `ModDownloadLib.vb:1480` | `MinSize = 64 KB`（BMCLAPI 不支持，仅单一官方源） |
| `FabricMain` | `FabricMainFile` | `ModDownloadLib.vb:1507`（dev.6 既有） | `IsJson = True`，镜像在前 / 官方在后 |
| `FabricApi` / `OptiFabric` | `ResourceDependency(kind, …)` | `ResourceVersion.vb:383 ToNetFile` | `{Hash, ActualSize}` + **`SimulateBrowserHeaders = True`** |

> 地址与落盘路径**仍由 VB 侧传入**（来自资源对象、镜像解析、版本 JSON），内核只决定「类别 → 校验口径」。

### 5.2 任务描述符的三条硬规定

1. **顶层决策一律照抄步骤**：`Block` / `ProgressWeight` / `Show` 来自 `InstallPlanStep`，规格工厂不重算（守门用例 `描述符不得重算计划里的权重Block与Show`）。
2. **复合体内部子列表仍由宿主 VB 函数提供**，已用 `SubStepsSource` 登记函数名，留给 P4-B，**不假装完成**：
   `McDownloadOptiFineLoader` / `McDownloadOptiFineSaveLoader` / `McDownloadForgelikeLoader(false)` / `McDownloadForgelikeLoader(true)` / `McDownloadLiteLoaderLoader` / `McDownloadFabricLoader`。
3. **已完整收编的两处骨架**（逐字对齐 VB，可单测）：
   - `McDownloadClientLoader`（`ModDownloadLib.vb:9–69`）：`HasMinecraftJson=false` 时插入「获取原版 json 文件下载地址」；支持库复合体 `Block=False`/权重 14/2 个副加载器，资源复合体 `Block=False`/权重 18/4 个副加载器；覆盖五类原版文件。
   - `McInstallLoadersGet` 的「补全文件」复合体（`:1766–1770`）：2 个副加载器。
   - `OptiFine` 取消语义：作为 Mod → `Cooperative`，安装形态 → `WaitForClient`；`NeoForge` → `CancelIfUnavailable`（不可下载时 `Task.Cancel()`）。

### 5.3 守门用例（26 个 `[Fact]/[Theory]`，逐项对照 VB）

顺序一致、加载器种类、Show、槽位、下载清单、Client 五类文件、取消/进度语义、OptiFine 双形态、`SubStepsSource` 登记、Client/PostLibraries 骨架、`HasMinecraftJson` 分支，以及 §5.1 全部校验口径（含 `Forge` 带 Hash / `NeoForge` 不带、`Size=0 → −1`）。**反向验证锚点**：`InstallPlan.cs` 的 `forgeBlock` 布尔式，改坏后应恰好 2 例变红。

---

## 6. Linux 侧落地步骤（顺序不可颠倒）

1. **先写计划**：按 Linux 自己的 `AGENTS.md` §3.1/§3.2，跨 3+ 文件属「大改动」，先写 Linux 版专项计划（八项必备章节 + 合规边界）再动代码。
2. **复核基线**：确认 §2 的四个文件仍是 P4-A 前形态（若已有本地漂移，改走 patch 而非覆盖）。
3. **只移植 5 个文件**，单独一个提交。因两线起点逐字相同，推荐直接复制最终版：

   ```bash
   cd /d/bing/MC/pcl_x
   cp PCL_NEXT_Windows/PCL.Download/{InstallPlan.cs,InstallDownloadPlan.cs,InstallTaskDescriptor.cs} PCL_NEXT_Linux/PCL.Download/
   cp PCL_NEXT_Windows/PCL.Download.Tests/{InstallPlanTests.cs,InstallTaskDescriptorTests.cs} PCL_NEXT_Linux/PCL.Download.Tests/
   cd PCL_NEXT_Linux && git diff --stat   # 期望：3 个已跟踪文件变更 + 2 个新文件
   ```

   > 备选（Linux 侧已有本地漂移时）：从 Windows 侧导出 `git diff -- <3 个已跟踪文件>` 生成 patch，再用 `git apply --ignore-whitespace` 应用；两个新增文件仍需单独复制。两仓库无共同祖先，**不能 `git merge` / `git cherry-pick`**。

4. **跑全量单测**（不编译 VB 主工程，故不受 Linux GUI 平台能力层未完成的影响）：

   ```bash
   dotnet test PCL.Download.Tests/PCL.Download.Tests.csproj -c Release --nologo -v quiet
   ```

   期望 **541 / 541 通过**（基线 481 + 新增 60）。注意 `pcl-next.sln` **不含测试工程**，编译绿 ≠ 单测全绿，测试必须单跑。

5. **做一次反向验证**：把 `InstallPlan.cs` 里 `bool forgeBlock = …` 临时改成 `= true;` → 应恰好 **2 个守门用例变红**；还原后复跑 541/541 全绿。这一步证明用例真的在守门，而不是只跑了个寂寞。
6. **补文档**：写 Linux 自己的阶段说明与专项计划（文件名 / 号段按 Linux 口径），可引用本交接文档，但不要复制 Windows 的裁决文本或发布说明。
7. **再议 P4-B**：VB 薄装配层属平台相关改动，**不要与第 3 步混在同一提交**，也**不要照抄 Windows 的 P4-B 实现**（见 §8）。

---

## 7. 验收清单

- [ ] 5 个文件落地，`git diff --stat` 与 Windows 侧 P4-A delta 一致（+295/−14 + 2 个新文件）。
- [ ] `dotnet test … -c Release` **541 / 541 通过**；既有 `InstallLayoutTests` / `VersionMergerTests` / `InstallDownloadPlanTests` 守门用例保持不动且全绿。
- [ ] 反向验证成立（改坏 `forgeBlock` → 2 红 → 还原 → 全绿）。
- [ ] `PCL.Download` 仍 `net10.0` 纯 BCL、`PclIsGuiFree=true`、无新增 `PackageReference`；新增测试无需改 csproj。
- [ ] VB 主工程零改动；`InstallPlanner.Build` 在 `ModDownloadLib.vb:1659` 的调用签名不变，UI 行为不变。
- [ ] 无混合换行符；提交信息按 Linux 自己的 `Build: <M.mm>` 口径。

---

## 8. 与 P4-A 直接相关的 Linux 差异（照抄会出错）

| 项 | 说明 |
|---|---|
| **RID / 目标框架** | Linux 线 `pcl-next.vbproj` 仍是 `net10.0-windows` + `UseWPF=true`，`scripts/Build.ps1` 的 `-Rid` 集合仍只有 `win-x64/win-arm64`、默认 `win-x64`（GUI 主工程平台能力层未落地，见 `docs/plans/计划-Linux内核L1-平台能力层.md`）。**本次纯内核移植不受影响**，但后续的编译命令与 P4-B 必须按 Linux 平台计划推进，不能照抄 `-r win-x64` |
| **构建命令** | Windows 实测：`dotnet build pcl-next.sln -c Release` 不带 RID 会撞 `PclRidGuard`，带 `-r` 又触发 `NETSDK1134`（sln 级不支持 RID）；正确写法是**项目级带 RID**：`dotnet build pcl-next/pcl-next.vbproj -c Release -r <RID> --self-contained -p:PclChannel=Dev`。Linux 侧按自己的 RID 与护栏复核 |
| **构建号** | `scripts/build-number.base`：Windows **65** / Linux **71**，编号仅本仓库内单调，**三线不可互比**；`obj/pcl-build.small` 每仓库一份 |
| **提交钩子** | Windows 本轮修了 `prepare-commit-msg` 残留旧锚点 `85c17f13`（新仓库不可达 → 会写 `Build: 0.0x`）的问题；Linux 侧请核对自己仓库的同一文件，未装钩子时按自己 `AGENTS.md` 手工补 `Build:` 行 |
| **版本名 / 号段 / tag** | 以 Linux 自己的《分支与发布规范.md》《计划-平台发布通道与命名.md》为准，**不要套用** Windows 的 `v2.100.0.5-dev.6.md` 文件名或号段；三条线各自打 tag、各自发版 |
| **合规硬门槛** | `PLATFORM_RELEASE_ENABLED` 保持关闭、不打平台 tag、**不分发任何非 Windows 制品**、`docs/合规/跨平台授权问询.md` 暂不发送。本地编译 / 单测 / CLI selftest **允许**，对外创建平台 Release 或分发制品**不允许** |

---

## 9. 明确不要做的事

- ❌ 不 `git merge` / `git cherry-pick` 跨仓库（无共同祖先），不把 Windows 提交推到 Linux 远端。
- ❌ 不复制 Windows 的 `AGENTS.md` / `分支与发布规范.md`（三份各自维护），不动 `pcl_x/` 根的只读备份。
- ❌ 不把 Windows 的口径 A 裁决、dev.6 发布说明、专项计划写成「全项目生效」或原样搬进 Linux 仓库。
- ❌ **不照抄 Windows 的 P4-B / P6**：Windows 已完成 P4-B 批次 1（顶层薄装配层
  `pcl-next/Modules/Download/LoaderPlanAdapter.vb`，见收尾计划 §11），结构可参考但**不在本文交接范围**；
  薄装配层与下线顺序涉及 Linux 自己的平台能力层与 VB 现状，须另行评估、自行裁决。
- ❌ 不改 `LoaderBase` / `LoaderTask` / `LoaderCombo` 公开签名、不把状态机下沉进内核、不提前 T2。
- ❌ 不改缓存格式、安装清单键名、`DownloadPolicy.ini` 键名，不借移植批量清理 CA1416 警告。
- ❌ 不用 `pclcli selftest --suite all` 抵扣六类加载器逐类真机安装冒烟（只覆盖 B1/B2/B5），该欠账仍是 P6 验收前置。

---

## 10. 依据文件索引

| 文件 | 用途 |
|---|---|
| `PCL_NEXT_Windows/PCL.Download/InstallTaskDescriptor.cs` | 任务描述符契约与规格工厂（移植主体 1） |
| `PCL_NEXT_Windows/PCL.Download/InstallDownloadPlan.cs` | 13 类下载描述符与工厂（移植主体 2） |
| `PCL_NEXT_Windows/PCL.Download/InstallPlan.cs` | `AttachDescriptors` / `HasMinecraftJson` / 步骤投影 |
| `PCL_NEXT_Windows/PCL.Download.Tests/InstallTaskDescriptorTests.cs` | 26 个对照 VB 的守门用例 |
| `PCL_NEXT_Windows/ReleaseNotes/v2.100.0.5-dev.6.md` | Windows 侧验证数据与「已知问题」（只读参考） |
| `PCL_NEXT_Windows/docs/plans/计划-Phase4收尾与Phase6下线.md` | §0.1 裁决、§4.1 P4-A 清单、§7 验证命令（只读参考） |
| `PCL_NEXT_Linux/docs/plans/计划-Linux内核L1-平台能力层.md` | Linux 平台能力真源 |
| `PCL_NEXT_Linux/docs/plans/计划-平台发布通道与命名.md` | Linux 号段 / tag 口径真源 |
| `PCL_NEXT_Linux/docs/合规/跨平台授权问询.md` | 合规前置（**暂不发送**） |
