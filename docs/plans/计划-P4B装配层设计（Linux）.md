# 计划：P4-B 装配层设计（Linux 线）

> 状态：**PB0–PB3 已落地（2026-11-11）**，PB4–PB5 未开始。
> 验证环境：Windows 侧提交 / 推送；构建与单测在 **WSL2 `Ubuntu-24.04` + .NET SDK `10.0.403`** 内用 `-r linux-x64` 实跑。
> 依据：`PCL_NEXT_Linux/AGENTS.md` §3.1 / §3.2 / §零 / §五 / §十；《分支与发布规范.md》§0B / §0C；
> P4-A 交付（提交 `a8a1ae6`）；交接文档 `PCL_NEXT_Windows/docs/交接-Linux端-dev6-P4A.md` §1 / §6.7；
> UI 线计划 `docs/plans/计划-LinuxUI移植.md`（只作边界参考，不改它）。
> 本文件是 Linux 线自己的 P4-B 专项计划，不复制 Windows 的 P4-B 实现或裁决文本。

---

## 0. P4-B 在 Linux 上具体指什么（先定方向）

### 0.1 现实前提

| 事实 | 依据 |
|---|---|
| `pcl-next` 的 122 个 `.vb` 是 `net10.0-windows` + WPF，Linux 无 VB 编译器 | 《计划-LinuxUI移植.md》§1.1（实测） |
| P4-A 已把「决策」全部收编进 `PCL.Download`（`InstallPlanner` / `InstallTaskDescriptors` / `InstallDownloads`） | 提交 `a8a1ae6` |
| P4-A 的描述符里仍有 6 处 `SubStepsSource` 指向 VB 函数名，`Inputs` / `Outputs` 是字符串槽位名 | `InstallTaskDescriptor.cs` 193–226 行 |
| Linux 线已落地 `InstallLayout` / `InstallCommitPlan` / `VersionMerger` / `DownloadEngine` 等中立积木 | `PCL.Download/*.cs` 实存 |
| 内核护栏 `PclIsGuiFree=true`，PCL.Download 不引用任何 GUI/VB 类型 | `Directory.Build.props` |

**结论**：Linux 上不存在「VB 薄装配层」这个实体。P4-B 在 Linux 线的等价物是：

> **一层 C# 的「装配 + 执行」中间层**：读 P4-A 的只读描述符 → 装配成可执行的节点树 → 由宿主提供
> 真实下载清单与目录等资产 → 执行并汇报进度/取消。**决策仍全部在 P4-A，装配层只读不重算。**

### 0.2 与三条候选路线的取舍

| 路线 | 内容 | 判定 |
|---|---|---|
| **等价重写（选定）** | 在 Linux 仓库新建中立 C# 工程，把 VB 装配层的职责（节点构造 + 执行分派）用 C# 重写；VB 侧代码不移植、不作为运行时依赖 | ✅ 唯一能在 Linux 上真正跑通的路线 |
| 只做接口契约 | 只定义接口与 DTO，不实现执行 | ❌ 会重演 P4-A 的「契约先行、执行缺位」，拿不到可用性 |
| 暂不做 VB 侧 | 保留 Windows 的 VB 装配，Linux 不推进 | ❌ 与「P4-B 现在启动」相悖，且 Linux 永远等不到 VB |

**VB 侧关系**：Linux 仓库内**不新增任何 `.vb` 文件、不引用 `pcl-next` 工程**。`pcl-next/**/*.vb`
只作为**行为对照源**（读源码确认口径），不参与编译。Windows 线的 P4-B（VB 薄装配）与此**互不移植**。

---

## 1. 目标与可验证标准

### 目标

1. 在 Linux 侧建立 `PCL.Install`（暂定名）中立装配层：输入 P4-A 的 `InstallPlanRequest` 与宿主资产，
   输出可执行节点树，并能在给定 `IDownloadEngine` 与动作实现后真正跑完一次安装编排。
2. 装配层**零业务决策**：权重 / Block / Show / 顺序 / 下载类别全部来自 P4-A 描述符，装配层只校验不重算。
3. 层与层之间全部可在内存中单测：装配（纯数据）、执行（fake 引擎 + fake 动作）、边界（缺资产即失败）。

### 可验证标准

| 判据 | 方式 |
|---|---|
| `linux-x64` 编译 0 错误 | `dotnet build PCL.Install/PCL.Install.csproj -c Release -r linux-x64` |
| 装配不重算决策 | 单测：节点 `ProgressWeight/Block/Show/顺序` 与 `InstallPlanStep` 逐项相等 |
| 缺资产 / 缺 handler 立即失败（不静默跳过） | 单测断言抛出的异常类型与缺失项名称 |
| 取消与 Block 语义可观测 | 单测：`CancellationToken` 取消后未执行后续 Block 节点；非 Block 节点并行不阻塞 |
| 内核不回归 | `dotnet test PCL.Download.Tests -c Release -r win-x64` 保持 549 全绿 |
| 内核保持 GUI-free | `PCL.Install` 的引用闭包不含 WPF / Avalonia / VB 工程；`PclIsGuiFree=true` |

---

## 2. 分层与契约（谁构造、谁决策、谁执行）

```
宿主（UI / CLI，后续阶段）
  │  ① 构造 InstallPlanRequest（纯数据）+ 注册宿主资产与动作实现
  ▼
PCL.Install.InstallSessionFactory（装配层入口）
  │  ② 调 P4-A：InstallPlanner.Build(request) → InstallPlan（决策已定）
  │  ③ Assemble：InstallPlan → InstallNode 树（只读投影 + 校验）
  ▼
PCL.Install.InstallRunner（执行层）
  │  ④ 按顺序/Block 语义遍历节点；进度按描述符权重汇总；取消向下传
  ▼
├─ IDownloadEngine（PCL.Download，已有）  ← Download 节点
└─ IInstallStepAction（宿主或内核提供的动作）← Task 节点
```

### 2.1 职责表（硬边界）

| 角色 | 谁负责 | 禁止 |
|---|---|---|
| **决策**（步骤有无 / 顺序 / 权重 / Block / Show / 取消语义 / 下载类别） | P4-A `InstallPlanner` + `InstallTaskDescriptors` | 装配层与执行层不得重算或覆盖 |
| **描述符构造** | P4-A（`InstallPlanner.Build` 末尾 `AttachDescriptors`） | 装配层不得手写 `InstallTaskDescriptor` 常量 |
| **真实下载清单**（`NetFileDescriptor` 的 urls / 路径 / hash / size） | 宿主（经 `INetFileSource`） | 装配层不得拼 URL 或路径 |
| **节点树装配** | `PCL.Install.InstallAssembler` | 不执行任何 IO |
| **执行** | `InstallRunner` + `IInstallStepAction` 实现 | 不得绕过描述符自行加步骤 |
| **进度 / 取消展示** | 宿主消费 `IProgress<InstallProgress>` | 装配层不产出 UI 类型 |

### 2.2 契约草案（接口名先定，签名以首批代码为准）

```csharp
// 宿主资产按「槽位名」取值，槽位名与 P4-A 描述的 Inputs / Outputs 字符串逐字一致。
public interface IInstallAssetSource
{
    bool TryGet(string name, out object? value);   // 缺失即返回 false，由装配层 fail-fast
}

// 每步真实下载清单；P4-A 只给类别，具体文件由宿主给出。
public interface INetFileSource
{
    IReadOnlyList<NetFileDescriptor> Get(InstallStepKind kind, IInstallAssetSource assets);
}

// Task 节点的执行实现；按 InstallStepKind 注册（子加载器按名字注册）。
public interface IInstallStepAction
{
    Task ExecuteAsync(InstallStepContext context, CancellationToken cancellationToken);
}

// 仍需「宿主函数」提供子列表的复合体：用 P4-A 登记的同一字符串名注册 C# 提供者。
public interface ISubStepProvider
{
    IReadOnlyList<InstallTaskDescriptor> Describe(InstallTaskContext context);
}
```

```csharp
// 装配产物：只读节点树。字段全部来自 InstallTaskDescriptor，装配层不加业务字段。
public sealed class InstallNode
{
    public InstallLoaderKind LoaderKind { get; }
    public string Name { get; }
    public double ProgressWeight { get; }
    public bool Block { get; }
    public bool Show { get; }
    public InstallCancelSemantics Cancel { get; }
    public IReadOnlyList<InstallNode> Children { get; }
    public IReadOnlyList<NetFileDescriptorKind> DownloadKinds { get; }
    public IReadOnlyList<NetFileDescriptor> Files { get; }   // 由 INetFileSource 提供
    public IInstallStepAction? Action { get; }               // Task 节点必须有
}
```

### 2.3 不变式（护栏，必须由单测锁住）

1. 装配层的节点字段 = 描述符字段，**逐项相等**（顺序、权重、Block、Show、取消、下载类别）；
2. `InstallPlanRequest` 的 15 个既有字段 + `HasMinecraftJson` 语义零改动；
3. `InstallTaskDescriptor` / `InstallPlan` / `InstallDownloadPlan` 的公开签名不改；
4. `PCL.Download` 不反向依赖 `PCL.Install`（依赖方向单向：Install → Download）；
5. 缺 handler / 缺资产 / 缺下载清单一律 **fail-fast 抛出**，不得「跳过继续」；
6. 子步骤来源二选一：描述符自带 `SubSteps`，或 `SubStepsSource` 有对应 C# 提供者；两者都没有即失败。

---

## 3. 阶段划分与验收

| 阶段 | 内容 | 验收方式 | 状态 |
|---|---|---|---|
| **PB0** 设计 | 本文件成稿 + 取舍项提交用户拍板 | 文档评审通过 | ✅ 2026-11-08（D1–D5 全按推荐） |
| **PB1** 契约与装配骨架 | 新建 `PCL.Install`（`net10.0`、`PclIsGuiFree=true`、`PclUseTreePath`）；四个契约；`InstallAssembler`（纯数据，无 IO）；`InstallNode`；`InstallSessionFactory` | WSL2 `-r linux-x64` 编译 0 错误；装配单测（不重算、顺序、fail-fast、子步骤来源）全绿 | ✅ 编译通过 + 21/21 用例通过 |
| **PB2** 下载执行 | `DownloadStepAction` + `NetFileRequestFactory`（`NetFileDescriptor` → `DownloadFileRequest`）；`InstallRunner` 顺序 / Block / 进度 / 取消 / 资产前置校验 | fake `IDownloadEngine` 单测：映射口径、取消传播、Block 阻塞语义、进度单调 | ✅ 同上（含在 21 例内） |
| **PB3** 中性任务动作 | `IgnoreMarker` / `InstallGame`（`VersionMerger` + `InstallLayout`）/ `RecordManifest` / `RemoveIgnoreMarker` 的 C# 实现 | 每个动作独立单测（临时目录，真实文件系统但本地）；`RecordManifest` 需先对照 VB 确认清单格式 | ✅ 编译通过 + 47/47 用例通过（含 PB1/PB2 的 21 例） |
| **PB4** 加载器子步骤 | 为 `McDownloadOptiFineLoader` / `McDownloadForgelikeLoader(false/true)` / `McDownloadLiteLoaderLoader` / `McDownloadFabricLoader` 提供 C# 版 `ISubStepProvider` | 逐加载器单测：子步骤名 / 权重 / Show / 下载类别与 P4-A 常量一致；与 VB 源码逐段核对记录 | ⬜ |
| **PB5** 宿主接线与真机闭环 | 一个宿主（先 CLI 子命令或 headless selftest，后接 UI 线 LU3）跑完整安装 | Linux 实机跑通 vanilla + 至少一种加载器；与 Windows 装出的实例目录逐项对比 | ⬜ |

> 推进纪律沿用 UI 线 §3：**一次只铺一个阶段**。PB1 只放契约与装配，不写下载动作；PB2 只放下载执行，不碰加载器。

---

## 4. 前置条件与阻塞项

| # | 项 | 状态 | 影响 |
|---|---|---|---|
| P1 | P4-A 已提交（`a8a1ae6`） | ✅ | PB1 可直接依赖 |
| P2 | 内核积木齐备（`InstallLayout` / `InstallCommitPlan` / `VersionMerger` / `DownloadEngine`） | ✅ 实存 | PB2 / PB3 可用 |
| P3 | `McInstallManifest` 的 C# 实现与文件格式 | ✅ PB3 已落地（模型 + `PCL/config.json` 同名键写入，见 §5.2 取舍 ①） | PB3 不再阻塞；PB5 接 PCLCS 配置子系统时可直接复用 |
| P4 | 合规前置（问询未发送、`PLATFORM_RELEASE_ENABLED` 未开） | ⛔ | 只阻塞对外分发；不阻塞编译、单测、本地 headless 验证 |
| P5 | 有显示的 Linux 环境 | ⛔ 本机只有 Windows | PB5 的 UI 闭环必须在 Linux 实机 / WSL+X11 验收；本机能验的是编译 + 单测 |
| P6 | UI 线正在同一仓库新增 `PCL.Desktop.Linux` 与计划文档 | ⚠️ 并行 | 本批**不改其任何文件**；`PCL.Install` 不引用 Avalonia |

---

## 5. 改动文件清单（PB1 首批，待批准后执行）

| # | 文件 | 内容 |
|---|---|---|
| 1 | `docs/plans/计划-P4B装配层设计（Linux）.md` | 本设计稿 |
| 2 | `PCL.Install/PCL.Install.csproj` | `net10.0`；`PclIsGuiFree=true`；`OutputPath=$(PclTreeRoot)` + `PclUseTreePath=true`；仅引用 `PCL.Download` |
| 3 | `PCL.Install/Contracts.cs` | 四个接口 + `InstallStepContext` / 进度 / 报告类型 |
| 4 | `PCL.Install/InstallExceptions.cs` | 装配与执行期 fail-fast 异常族 |
| 5 | `PCL.Install/InstallRegistries.cs` | `InstallActionRegistry` / `SubStepProviderRegistry` / `InstallAssetBag` |
| 6 | `PCL.Install/InstallNode.cs` | 只读节点树（描述符原样投影） |
| 7 | `PCL.Install/InstallAssembler.cs` | `InstallPlan` → `InstallNode`，含 fail-fast 校验 |
| 8 | `PCL.Install/InstallRunner.cs` | 顺序 / Block / 进度 / 取消 / 资产前置校验 |
| 9 | `PCL.Install/DownloadStepAction.cs` | `NetFileRequestFactory` + 下载节点动作 |
| 10 | `PCL.Install/InstallSession.cs` | `InstallOptions` / `InstallSession` / `InstallSessionFactory` |
| 11 | `PCL.Install.Tests/…`（新测试工程 4 文件） | 装配不变式 / fail-fast / 映射口径 / 顺序与 Block / 取消 / 进度 / 端到端 |

**明确不做（PB1）**：

- ❌ 不实现任何 `IInstallStepAction` 具体动作（留 PB2/PB3）；
- ❌ 不提供任何 `ISubStepProvider`（留 PB4），6 处 `SubStepsSource` 在 PB1 只做「未注册即失败」；
- ❌ 不改 `PCL.Download` / `PCL.Cli` 的公开行为，不动 `PCL.Desktop.Linux`；
- ❌ 不新增 `.vb`、不引用 `pcl-next` 工程、不引入 Avalonia；
- ❌ 不做真机安装与打包。

### 5.1 PB3 改动文件（已落地，2026-11-11）

| # | 文件 | 内容 |
|---|---|---|
| 1 | `PCL.Install/InstallFileOps.cs` | internal：建目录 + 递归复制目录内容（语义对齐 MeloongCore `DirectoryUtils.Copy`） |
| 2 | `PCL.Install/IgnoreMarkerActions.cs` | `IgnoreMarkerAction`（写 `.pclignore`，文件名 / 内容与 VB 逐字一致）+ `RemoveIgnoreMarkerAction` |
| 3 | `PCL.Install/InstallGameAction.cs` | `InstallGameOptions`（中间产物 / 是否 Modable / 版本隔离解析器）+ `InstallGameAction` |
| 4 | `PCL.Install/InstallManifest.cs` | `InstallManifest` / `InstallManifestComponents` / `ManagedInstallFile` / `InstallManifestStore`（JSON 形状与落盘） |
| 5 | `PCL.Install/RecordManifestAction.cs` | `RecordManifestAction`（组件版本规范化 + 受管理文件哈希 + 写清单） |
| 6 | `PCL.Install/Contracts.cs` | 新增 `InstallStepContext.GetAsset<T>`（缺失 / 类型不符均 fail-fast） |
| 7 | `PCL.Install/InstallExceptions.cs` | 新增 `InstallAssetTypeException` |
| 8 | `PCL.Install.Tests/MarkerActionTests.cs`（7 例） | 文件名 / 内容 / 无 BOM / 建目录 / 删除幂等 / 缺资产 / 类型不符 / 已取消 |
| 9 | `PCL.Install.Tests/InstallGameActionTests.cs`（10 例） | 原版复写 / 加载器合并顺序与去继承 / libraries 迁移 / mods 复制 / 空 mods 创建 / 版本隔离 / 中间产物 / 进度点 / 缺资产 / json 缺失 |
| 10 | `PCL.Install.Tests/RecordManifestTests.cs`（9 例） | 字段与 VB 口径 / 保留其它配置键 / 可选键省略 / 空清单 / 目标文件缺失 / 配置损坏中止 / 缺资产 / 缩进写回 / 版本常量 |
| 11 | `PCL.Install.Tests/TestDoubles.cs` | 新增 `TempWorkspace` 与 `InstallTestContext`（只走公开装配 API，不开放 internal 构造） |

**明确不做（PB3）**：

- ❌ 不实现 5 个加载器子步骤（PB4），`RecordManifest` 之外的 `SubStepsSource` 仍「未注册即失败」；
- ❌ 不做宿主接线（PB5），四个动作只提供类与单测，不自动注册进任何会话；
- ❌ 不改 `PCL.Download` 的公开签名；`ModsTempFolder` 未加进 P4-A 的 `RecordManifest.Inputs`（见 5.2 取舍 ②）；
- ❌ 不写 `PCL.Install` 之外的任何工程，不动 `PCL.Desktop.Linux` 与 UI 计划文档。

### 5.2 PB3 清单格式对照（VB → C#）与取舍

对照源：`PCL_NEXT_Windows/pcl-next/Modules/Download/LoaderPlanAdapter.vb`
（`AssembleIgnoreMarker` / `AssembleInstallGame` / `AssembleRecordManifest` / `AssembleRemoveIgnoreMarker`）。

| VB 行为 | C# 实现 | 备注 |
|---|---|---|
| `FileUtils.Write(VersionFolder & ".pclignore", "用于临时地在 PCL Next 的版本列表中屏蔽此版本。")` | `IgnoreMarkerAction` 写 `Path.Combine(VersionFolder, ".pclignore")`，内容为同一条字符串常量 | 单元测试锁住文件名、内容、首字节无 BOM |
| `FileUtils.Delete(VersionFolder & ".pclignore")` | `RemoveIgnoreMarkerAction` → `File.Delete` | 文件不存在不报错，与 VB 一致 |
| `MergeJson(...)` 源顺序：Minecraft → OptiFine（非 Mod 时）→ Forge → NeoForge → LiteLoader → Fabric | `InstallGameAction.BuildMergeSources` 同序 | 合并规则本身在 `VersionMerger`（P4-A 已验收） |
| `DirectoryUtils.Copy(TempMcFolder & "libraries", McFolderSelected & "libraries")` | `InstallFileOps.CopyDirectory(layout.TempFolder + "libraries", mcFolder + "libraries")` | 只复制文件、目标目录按需创建 |
| `TargetFolder = If(IsIntermediateOutput, VersionFolder, McInstance(VersionFolder).PathIndie)` | `InstallGameOptions.IsIntermediateOutput` + `ResolveTargetFolder`（宿主注入隔离策略，默认不隔离） | Linux 无 PCL 设置子系统，隔离策略归宿主 |
| `ModsTempFolder` 存在则复制、否则 `Modable` 时建空目录 | 同左（`HasAnyModLoader`） | 日志文案沿用「自动创建 Mod 文件夹 / 自动创建资源包文件夹」 |
| `Task.Progress = 0.2 / 0.8` | `context.Progress.Report(Fraction = 0.2 / 0.8)` | 单测断言两个进度点与顺序 |
| `RelativePath = Entry.Value.Substring(ModsTempFolder.Length)` | `Path.GetRelativePath(_modsTempFolder, sourcePath)` | 结果一致，且不依赖尾分隔符 |
| `InstallManifestRules.ManagedModPath(RelativePath)` | 同（P4-A 已移植） | 正斜杠 + 全小写 + `mods/` 前缀 |
| `ComputeFileHash(..., Sha1)` + `GetInfo(...).LastWriteTimeUtc` | `HashHelper.ComputeFile(path, HashType.Sha1)` + `File.GetLastWriteTimeUtc` | SHA1 小写 40 位，单测逐值比对 |
| `TempLoaderVersions(Forge) = NormalizeLoaderVersion(...)`（NeoForge 同） | `RecordManifestAction.BuildComponents` 同 | 其余加载器版本原样保留 |
| `Configs.McInstallManifest.Set(New McInstallManifest(Versions.McInstallManifestVersion, Target, Date.UtcNow, ManagedFiles), InstanceConfig); InstanceConfig.Save()` | `InstallManifestStore.Write(versionFolder, manifest)` 写 `{versionFolder}/PCL/config.json` 的 `MinecraftInstallManifest` 键 | 见取舍 ① |

**取舍 ①（清单落盘容器）**：Windows 的清单存在实例配置 `PCL/config.json` 的一个键里（`JsonConfigProvider`），
不是独立文件。Linux 线没有配置子系统，`PCL.Install` 也不能引用 PCLCS（`net10.0-windows` + WPF）。
本批选择**由动作直接读写该文件的同名键**（读-改-写、保留其它键、原子替换），
这样清单位置与 Windows 完全一致，将来 PCLCS 的 `JsonConfigProvider` 接管时无需迁移。
JSON 形状按 PCLCS 的 Newtonsoft 默认序列化对齐：PascalCase、可选键为 null 时省略、缩进写回。
偏差：现有配置不是合法 JSON 时**直接失败并保留原文件**（VB 会记日志后当作空配置覆盖），
这里按「不许静默丢用户配置」处理。

**取舍 ②（`ModsTempFolder` 没进 `RecordManifest.Inputs`）**：P4-A 的 `RecordManifest` 只声明
`TargetFolder / ManagedFileSources / VersionFolder`，但 VB 计算相对路径时用了 `Ctx.ModsTempFolder`。
本批不改 P4-A 描述符（它是已验收契约，且 Windows 侧 VB 装配同样在用），改为由宿主构造动作时传入；
PB5 接线时与 `InstallGame` 的 `ModsTempFolder` 资产取同一个值即可（`InstallLayout.ModsRoot`）。

---

## 6. 回归影响面

| 面 | 影响 | 处置 |
|---|---|---|
| `PCL.Download` 与内核单测 | 不改内核代码 → 549 用例不受影响 | 复跑确认 |
| `pcl-next.sln` / Windows CI | 新工程不进 sln，Windows job 不感知 | 不改 workflow |
| `kernel-guard.yml`（`build-linux`） | 若不为新工程加步骤，它仍只编内核三目标 | PB1 先不改；PB4 视需要另立批次 |
| `Directory.Build.props` 护栏 | 新工程受 `PclRidGuard` / `PclTreePathGuard` 约束 | 构建与测试都显式带 `-r` |
| 他人并行改动（UI 线 / 规则文档） | 不碰、不 stash、不 reset | 收尾 `git status` 核对 |

---

## 7. 验证步骤

```bash
# 全部在 WSL2（Ubuntu-24.04 / .NET SDK 10.0.403）内执行；仓库挂在 /mnt/d，
# WSL 内只读构建测试，git 写操作（提交 / 推送）仍在 Windows 侧做。
cd /mnt/d/bing/MC/pcl_x/PCL_NEXT_Linux

dotnet build PCL.Install/PCL.Install.csproj -c Release -r linux-x64
dotnet test  PCL.Install.Tests/PCL.Install.Tests.csproj -c Release -r linux-x64
dotnet test  PCL.Download.Tests/PCL.Download.Tests.csproj -c Release -r linux-x64
```

实测结果（2026-11-11，含 PB3）：

| 命令 | 结果 |
|---|---|
| `dotnet build PCL.Install -r linux-x64` | 0 错误（2 个既有 CS860x 告警，来自 `PCL.Download/ResourceSearcher.cs`） |
| `dotnet test PCL.Install.Tests -r linux-x64` | **47/47 通过**（PB1/PB2 的 21 例 + PB3 的 26 例；Linux 上真跑，非仅编译） |
| `dotnet test PCL.Download.Tests -r linux-x64` | **548/549**；唯一失败 `LocalResourceFileTests.Equals_按路径忽略大小写` 是「Windows 路径大小写不敏感」的平台假设，Linux 上必然失败，属既有用例问题，与本批无关，未改动 |

> 2026-11-08 首次记录时 `PCL.Install.Tests` 为 21/21；PB3 追加 26 例后为 47/47。
> 用例的临时目录放在系统临时区并在用例结束时整棵删除，单次运行只有几 KB 级占用，不在 WSL 家目录留存。

> WSL 侧 NuGet 有 `NU1900` 告警（漏洞数据源不可达），不影响还原与测试。

---

## 8. 回滚方案

| 场景 | 回滚动作 | 保留 |
|---|---|---|
| PB1 编译或单测无法收敛 | 删除 `PCL.Install/` 与 `PCL.Install.Tests/`（新增目录，无外部依赖） | 本设计稿 + PB0 结论 |
| 装配层被证明与 P4-A 契约冲突 | 先改装配层，不动 P4-A（P4-A 是已验收契约）；若必须改 P4-A，另立批次并写回退说明 | 本设计稿 |
| 新工程影响内核 CI | 从 CI 步骤中移除（PB1 本不接入 CI） | 内核代码 |

回滚一律按 `AGENTS.md` §六 出回退报告，不静默回退。

---

## 9. 合规边界

- 本层是平台无关的纯 C# 逻辑，不含上游 VB 代码、不含 UI，不改变 PCL 许可证 L1–L5 义务。
- 合规前置未完成前，只做本仓库内编译、单测与 headless 验证；不产生、不对外分发任何 Linux 制品。

---

## 10. 待拍板项（PB1 动代码前需要确认）

| # | 决策项 | 选项 | 推荐 |
|---|---|---|---|
| D1 | 装配层落点 | (a) 新建中立工程 `PCL.Install`；(b) 并进 `PCL.Download`；(c) 并进 `PCL.Cli` | **(a)**：内核保持「纯引擎」，CLI 保持「薄前端」，UI 线可引用 |
| D2 | 首批实现深度 | (a) 只做 PB1 契约 + 装配骨架；(b) PB1 + PB2 下载执行一起落 | **(b)**：PB1 单独落会让「能装配不能跑」，PB2 才是可用性拐点 |
| D3 | 测试工程 | (a) 新建 `PCL.Install.Tests`；(b) 并进 `PCL.Download.Tests` | **(a)**：避免内核测试工程反向引用装配层，依赖方向更干净 |
| D4 | `RecordManifest` 口径 | (a) 对照 VB 源码逐字平移清单格式；(b) 先落简化清单，后续对齐 | **(a)**，但放到 PB3，PB1/PB2 不碰 |
| D5 | 是否同步把 `PCL.Install` 接入 `kernel-guard.yml` | (a) 暂不接入，先本地验证；(b) 立即接入 Linux CI | **(a)**，接入时机随 PB4 另立批次 |
