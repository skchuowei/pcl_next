# 计划：P4-A 编排决策 DTO 移植（Linux 线）

> 状态：执行中（2026-11-08）。
> 依据：`PCL_NEXT_Windows/docs/交接-Linux端-dev6-P4A.md`（下称「交接文档」）、本仓库 `AGENTS.md` §3.1/§3.2。
> 本文是 Linux 线自己的专项计划，不复用 Windows 的裁决文本与发布说明。

## 1. 目标

把合并安装的**全部编排决策**从 VB 里的 `New LoaderTask/Combo/Download` 字面量与
`New FileChecker With {…}` 表达式，收编成 `PCL.Download` 内核里的**纯数据 DTO**：

- 任务描述符：加载器种类、输入 / 输出槽位、子步骤骨架、下载文件类别、取消语义、进度语义；
- 下载文件描述符：从 dev.6 的 1 类（Fabric 主文件）扩到 **13 类**，把散落在 VB 各处的校验口径变成单一真源。

可验证标准：

1. 5 个文件落地，`git diff --stat` 与交接文档 §3 的 delta 一致（3 个已跟踪文件 + 2 个新文件）；
2. `dotnet test` 全量通过（本仓库基线 489 + 新增 60 = **549**）；
3. 变异反证成立：改坏 `InstallPlan.cs` 的 `forgeBlock` 布尔式恰好 **2 例**变红，还原后全绿；
4. `PCL.Download` 仍 `net10.0` 纯 BCL、`PclIsGuiFree=true`、无新增 `PackageReference`；
5. VB 主工程零改动（`pcl-next/` 目录本轮不碰）。

## 2. 前置条件与阻塞项

| 项 | 状态 | 影响 |
|---|---|---|
| Windows 侧 P4-A 已提交 | ✅ `97da9b7`（父提交 `84de12f`） | 移植源可用 |
| 两线 P4-A 前基线逐字一致 | ✅ 实测 4 个文件文本级完全一致（仅 CRLF 差异） | 可覆盖式移植，无需三方合并 |
| 口径 A 裁决 | ⚠ 仅 Windows 线写进自己的计划 | Linux 线**自行采用 A**（见本文 §5）；本批代码不依赖该裁决即可先行落地 |
| P4-B（VB 薄装配层） | ⛔ 未做 | 不在本计划范围；`SubStepsSource` 只登记函数名，不假装完成 |
| 合规前置（上游授权书面答复 / `PLATFORM_RELEASE_ENABLED`） | ⛔ 未完成 | 只阻塞**对外分发**，不阻塞编译与本仓库内单测 |
| Linux GUI 平台能力层 | ⛔ 未完成 | 与本批无关：本批不编 `pcl-next.sln` / 任何 `net10.0-windows` 工程 |

## 3. 现状分析

- Linux 侧处于「P4-A 之前」的干净基线：`InstallPlan.cs` 264 行、`InstallDownloadPlan.cs` 85 行、
  `InstallPlanTests.cs` 251 行，与 Windows `84de12f` 内容逐字相同。
- 交接文档 §2 称 Linux 实跑用例 **481**，实测为 **489**：差 8 例来自 Linux 线独有的
  `PCL.Download.Tests/PlatformCapabilityTests.cs`（Windows 基线无此文件），与本批无关；
  Linux 也无 Windows 基线里的 `HeaderPropagationTests.cs`。故预期结果为 **549**，不是 541。
- 结论：交接文档的「可直接覆盖式移植」成立；仅验证基数需按本仓库实际值修正。

## 4. 改动文件清单

| # | 文件 | 动作 | 范围 |
|---|---|---|---|
| 1 | `PCL.Download/InstallTaskDescriptor.cs` | 新增 346 行 | 2 枚举 + 只读 `InstallTaskDescriptor` + `InstallTaskDescriptors.Create` 规格表 + `InstallTaskPlanner.Describe` |
| 2 | `PCL.Download.Tests/InstallTaskDescriptorTests.cs` | 新增 414 行 | 26 个 `[Fact]/[Theory]` 守门用例，逐步骤对照 VB |
| 3 | `PCL.Download/InstallPlan.cs` | 264 → 330 行 | `InstallPlanStep` 挂 9 个只读投影属性 + `internal Attach`；`InstallPlanRequest` 补 `HasMinecraftJson`；`InstallPlan.AttachDescriptors`；`InstallPlanner.Build` 末尾注入 |
| 4 | `PCL.Download/InstallDownloadPlan.cs` | 85 → 246 行 | 新增 `NetFileDescriptorKind`（13 类）、`NetFileDescriptor.Kind` / `SimulateBrowserHeaders`；`InstallDownloads` 工厂 1 → 13 |
| 5 | `PCL.Download.Tests/InstallPlanTests.cs` | 251 → 305 行 | 追加 4 例：描述符一一对应、权重/Block/Show 不得重算、`AttachDescriptors` 注入、反向守门 |
| 6 | `docs/plans/计划-P4A编排决策DTO移植.md` | 本文 | 专项计划 |

**明确不做**：

- ❌ 不动 `pcl-next/` 任何 VB 文件（含 `ModDownloadLib.vb:1659` 的 `InstallPlanner.Build` 调用点）；
- ❌ 不做 P4-B 装配层，不搬 `LoaderBase` / `LoaderTask` / `LoaderCombo` 状态机（T2 之后）；
- ❌ 不把 Windows 的后续增量（`InstallerArguments.cs`、`NeoForge gameDir` 修复、0.72 真机四类修复）混进来；
- ❌ 不改 `PCL.Download.Tests.csproj`（SDK 默认 glob 已覆盖新增测试文件）；
- ❌ 不改 Linux 侧既有的 `docs/plans/计划-Phase4主体-下载数据源C化.md` §4.9 状态标注（另行裁决）。

## 5. 改动要点

1. **口径 A 在 Linux 线采用**：C# 输出全部编排决策与任务描述符，VB 只做「DTO → `LoaderBase` 树」的无逻辑装配。
   采用理由与 Windows 相同：`LoaderBase` 定义在 VB 主工程且带 UI 事件，内核 `PclIsGuiFree=true` 无法产出它，
   提前下沉状态机等于把 T2 拆壳提前。本批代码不含执行 lambda，仅数据契约。
2. **`InstallTaskDescriptor.cs`**：枚举 `InstallLoaderKind`（`Task`/`Combo`/`Download`）与
   `InstallCancelSemantics`（`Inherit`/`Cooperative`/`WaitForClient`/`CancelIfUnavailable`）；
   只读描述符字段 `Kind`/`Name`/`LoaderKind`/`ProgressWeight`/`Block`/`Show`/`Inputs`/`Outputs`/
   `SubSteps`/`DownloadKinds`/`Cancel`/`ReportsProgress`/`SubStepsSource`，只含基元 / 字符串 / 集合 / 枚举。
   规格工厂 `InstallTaskDescriptors.Create` 的常量逐字取自 VB `McInstallLoadersGet` 1609–1819 与
   `McDownloadClientLoader` 9–69；**不重算** Block / 权重 / Show。
   两处完整收编的骨架：Client 五类文件（`HasMinecraftJson=false` 时插入「获取原版 json 文件下载地址」）、
   「补全文件」复合体的 2 个副加载器。子列表仍由宿主函数提供的登记为 `SubStepsSource`。
3. **`InstallDownloadPlan.cs`**：`NetFileDescriptorKind` 13 类 + `NetFileDescriptor.Kind` /
   `SimulateBrowserHeaders`；`InstallDownloads` 13 个工厂。校验口径硬规定：
   `VanillaJar` 缺 size 回退 **−1**、`AssetObject` 的 `Size=0 → −1`、`Forge` 带 Hash 而 `NeoForge` **不带** Hash、
   `OptiFineInstaller` 安装 300 KB / 作为 Mod 64 KB、`ResourceDependency` 置 `SimulateBrowserHeaders=true`。
   地址与落盘路径仍由 VB 侧传入。
4. **`InstallPlan.cs`**：步骤挂 9 个代理到 `Task` 的只读投影属性；`internal Attach` 由
   `InstallPlan.AttachDescriptors(request)` 注入带请求上下文的规格；`InstallPlanner.Build` 末尾自动调用，
   VB 调用方签名零改动。`InstallPlanRequest` 只补 `HasMinecraftJson`（默认 `false`），其余 15 个字段语义不变。
5. **测试**：新增 26 个对照用例 + `InstallPlanTests` 追加 4 例；期望值逐字取自 VB，覆盖顺序 / 加载器种类 /
   Show / 槽位 / 下载清单 / Client 五类文件 / 取消与进度语义 / OptiFine 双形态 / `SubStepsSource` 登记 /
   `HasMinecraftJson` 分支 / 13 类校验口径。

## 6. 回归影响面

- `InstallPlanRequest` 原有 15 个字段语义零改动；`Block` / 权重 / `Show` 布尔式零改动。
- `InstallPlanStep` 构造函数与 `GetStep` 公开签名不变；新增属性均为只读代理，懒构造。
- `AttachDescriptors` 不重算任何决策；`request` 只影响 OptiFine 分流与 Client 可选子步骤。
- `NetFileDescriptor` 构造器把 `kind` / `simulateBrowserHeaders` 放在参数尾部并带默认值，既有
  `FabricMainFile` 调用方（`PCL.Cli` 等）零改动。
- 需新增测试：`InstallTaskDescriptorTests`（26 例）+ `InstallPlanTests` 追加 4 例；既有
  `InstallLayoutTests` / `VersionMergerTests` / `InstallDownloadPlanTests` 保持不动。

## 7. 验证步骤

```powershell
# 1) 内核按 Linux 目标 RID 编译（护栏要求真实 RID）
dotnet build PCL.Download/PCL.Download.csproj -c Release -r linux-x64

# 2) 全量单测（Windows 开发机上执行，须用可在本机运行的 RID）
dotnet test PCL.Download.Tests/PCL.Download.Tests.csproj -c Release -r win-x64
```

判定：编译 0 错误（既有 CS8602/CS8604 类告警不算失败）；单测 **549/549** 通过。

> RID 说明：`Directory.Build.props` 的 `PclRidGuard` 禁止空 / `any` RID，故两条命令都显式带 `-r`。
> Windows 开发机无法运行 linux-x64 测试宿主，故 linux-x64 只做**编译**验证（证明源码可交叉编译），
> 可执行的单测用 win-x64 跑。CI（`kernel-guard.yml` / `release.yml`）在 ubuntu 上依次执行
> `dotnet build … -r linux-x64` → `dotnet test …`（测试继承 RID 缓存），与本机口径等价。

3) 变异反证：把 `InstallPlan.cs` 的 `bool forgeBlock = …` 临时改成 `= true;`，重跑测试，
应恰好 **2 例**变红；还原后复跑全绿。

## 8. 回滚方案

- 单提交回滚：`git revert <本轮提交>`；两个新文件随之移除，3 个已跟踪文件回到 264 / 85 / 251 行基线。
- 不涉及 VB 主工程与构建配置，回滚不牵连任何用户可见行为。
- 回滚后须保留：`docs/plans/计划-P4A编排决策DTO移植.md`（若继续保留计划文本，按 §九「只标不删」加执行状态说明）。

## 9. 合规边界

- 本批仅移植平台无关的纯 C# 内核数据契约，不含上游 VB 代码、不含 WPF/UI，不改变 PCL 许可证 L1–L5 义务。
- 合规前置未完成（问询未发送、`PLATFORM_RELEASE_ENABLED` 未开）：本批只在本仓库内编译与单测，
  不产生、不对外分发任何非 Windows 制品。

## 10. 与交接文档的差异说明

| 项 | 交接文档 | 本仓库实际 | 处理 |
|---|---|---|---|
| 测试基数 | 481 + 60 = 541 | 489 + 60 = **549** | 按实际值判定；差异源为 Linux 独有的 `PlatformCapabilityTests.cs` |
| 移植方式 | 覆盖式复制（推荐） | 基线逐字一致，成立 | 直接覆盖；按本仓库 CRLF 落盘 |
| 口径 A | Windows 线裁决，Linux 自行裁决 | 未写进 Linux 规则 | 本计划 §5.1 明确采用 A，仅限本仓库 |
| AGENTS §十.2「禁止把一个平台的改动拷进另一仓库」 | 交接文档要求移植 | — | 本批是用户明确指示的**文件级知识移植**（非整分支合并、无共享 `.git`），单独一个提交留痕 |
