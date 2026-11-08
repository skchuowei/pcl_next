# 计划：Phase 4 收尾（编排外壳）与 Phase 6 下线 VB 下载模块

> 依据：《AGENTS.md》§3.1/§3.2（大改动须先写专项计划）、§零（产物与构建口径）、
> 《PCL2_CSharp下载服务迁移计划.md》§5 Phase 4/6、《计划-Phase4主体-下载数据源C化.md》§4.9、
> 《计划-内核与GUI拆分（CLI）.md》T1/T2、《ReleaseNotes/v2.100.0.5-dev.5.md》「已知问题 / 下一阶段计划」。
> 状态：**口径 A 已裁决（2026-11-08，用户）**；**P4-A 已完成**（本轮：内核 DTO 全集 + 单测），
> P4-B / P6 未动工。本轮不改 VB 主工程，只改文档、`PCL.Download` 内核与 `PCL.Download.Tests`。
> 范围：**仅 Windows 线**（`PCL_NEXT_Windows`，分支 `dev.6-Windows`）；Linux / Android 线不在本文范围。
> 本文取代《计划-Phase4主体》§4.9「后续（未动）」清单；该节已改标为「已移交本文」。

---

## 0. 决定记录：先解决一个架构口径矛盾

《计划-Phase4主体》§4.9 第一条写的是「安装编排的 `LoaderBase` 生成改为 C# 输出」。按字面执行会撞上架构护栏：

- `PCL.Download.csproj` 声明 `<PclIsGuiFree>true</PclIsGuiFree>`，`Directory.Build.props` §3 禁止内核引用
  WPF / WinForms 与 `pcl-next` / `PCLCS` / `MeloongCore`；
- 而 `LoaderBase` / `LoaderTask` / `LoaderCombo` 定义在 **`Modules/Base/ModLoader.vb`（VB 主工程，791 行）**，
  且带 UI 事件（`OnStateChangedUi`）与 `MyListItem` 耦合。C# 内核**不可能**直接产出它，除非先把
  `LoaderBase` 状态机下沉到内核——那正是《计划-内核与GUI拆分（CLI）》T2，而 T2 明确**排在 Phase 6 之后**。

因此本文把 Phase 4 的退出口径明确为（**已裁决：A，2026-11-08 用户确认**）：

| 口径 | 含义 | 评价 |
|---|---|---|
| **A ✅ 已采纳** | C# 输出**全部编排决策与任务描述符 DTO**；VB 只做「DTO → `LoaderBase` 树」的**无逻辑装配**，执行 lambda 仍在 VB | 符合护栏、不动 417+ 处引用、可逐轮灰度；dev.6 的 `InstallPlan` / `NetFileDescriptor` 已走在这条路上 |
| B ❌ 未采纳 | 先把 `LoaderBase` 状态机迁到 C# 内核，由内核产出真正的加载器树 | 等价于把 T2 提前，与《计划-内核与GUI拆分》§0 的「现在拆壳是白拆」结论冲突，回滚成本极高 |

### 0.1 裁决记录（生效）

- **裁决内容**：采用 A。`PCL.Download` 负责输出全部编排决策与任务描述符；`LoaderBase` 状态机
  **不**下沉到内核，VB 侧的 `LoaderBase` / `LoaderTask` / `LoaderCombo` / `LoaderDownload` 公开签名不改。
- **Phase 4 完成判据（正式文本，取代「C# 生成 `LoaderBase`」）**：
  「**UI 行为不变（含禁用态）、进度 / 取消 / 重试正确，且安装步骤的顺序 / 权重 / Block / Show /
  下载清单与任务描述符全部由 C# 决定**」；装配层不得出现 `If 组件已选 Then …` 形式的业务判定。
- **生效范围**：仅 Windows 线 `dev.6-Windows`。Linux / Android 线各自维护规则文档，不受本裁决约束。
- **连带结论**：T2（拆 `PCL.Core`）仍排在 Phase 6 之后，不得因本裁决提前；§4.4「明确不做」
  的第 4 条（不把 `LoaderBase` 状态机搬进内核）继续有效。
- **落点**：本裁决已写入 `ReleaseNotes/v2.100.0.5-dev.6.md`，并同步改标
  《计划-Phase4主体-下载数据源C化.md》§4.9。后续不得以「C# 没产出 Loader」为由重开 Phase 4。

**第二个判断**：P4 收尾与 P6 下线不是两个独立阶段，而是同一个耦合体的两面——
`McInstallLoadersGet`（`ModDownloadLib.vb:1609–1819`，约 210 行）既持有编排树，又持有 `NetFile` 构造与
`McInstance` / `MergeJson` 引用。P4 不把这里剥干净，P6 就无代码可删；所以本文按 **P4 → P6 串行**排期，
不并行、不交叉提交。

---

## 1. 目标

### P4 收尾

1. 把安装编排里**仍留在 VB 的纯决策**全部移入 `PCL.Download`：步骤树已由 `InstallPlanner.Build` 承担，
   本阶段补齐**每个步骤的任务描述符**（输入 / 输出 / 子加载器 / 下载文件清单 / 取消与进度语义）。
2. VB 侧 `McInstallLoadersGet` 退化为**薄装配层**：遍历 DTO 分派到具名私有方法，不再内联任何业务判定。
3. 收尾《计划-Phase4主体》§4.9 剩余两项（详情页版本列表、VB 资源模型退化为只读投影）。
4. **可验证标准**：沿用 Phase 4 退出标准「UI 行为不变（含禁用态）、进度/取消/重试正确」；
   新增「`PCL.Download.Tests` 对每个 `InstallStepKind` 的顺序 / 权重 / Block / Show / 下载清单有反向验证用例」。

### P6 下线

1. 在保持功能等价的前提下，删除 VB 侧的下载实现：`ModNet.vb` 的传输/编排部分、`ModDownload.vb` 的
   已下线入口、`Modules/Resource/*.vb` 的残留实现。
2. **可验证标准**（照抄迁移计划 §5 Phase 6）：**VB 下载代码为零、功能与基准版等价、无回归**。
3. 完成 Phase 6 后解锁 T2（拆 `PCL.Core`）与扩展系统 E2+。

**明确不计入本阶段成果**：GUI 页面 C# 化、游戏启动 / 整合包导出 / 联机 / 音乐 / 皮肤等模块重写——
按 dev.5 发布说明，这些「不在本次重写范围内」。

---

## 2. 前置条件与阻塞项

| 依赖 | 状态 | 备注 |
|---|---|---|
| Phase 0–3 完成（契约 / 传输引擎 / `NetInterop` 桥接 / 资源子系统） | ✅ | dev.5 发布说明口径，代码已核实 |
| Phase 5（net48 → net10） | ✅ | `pcl-next.vbproj` = `net10.0-windows` + `UseWPF` |
| dev.6 编排 DTO 起步 | ✅ | `InstallPlan.cs`(264 行) / `InstallDownloadPlan.cs`(85 行) 已落地，`InstallPlanner.Build` 已被 `ModDownloadLib.vb:1659` 调用；`PCL.Download.Tests` 481 例全绿（本轮实跑 1m29s） |
| 合规开关 `ModDownloadPolicy.vb` | ✅ | 环境变量 `PCL_ALLOW_MC_DOWNLOAD` + `DownloadPolicy.ini` 双通道，**默认启用** |
| 架构护栏 `PclIsGuiFree` | ✅ 硬约束 | 决定 §0 的口径 A |
| **六类加载器列表逐类真机安装冒烟** | ⬜ **欠账** | dev.4 欠到 dev.5、dev.5 又顺延 dev.6（见 dev.5「已知问题」第 2 条）。**阻塞 P6 验收**，不阻塞 P4-A 编码 |
| §0 口径裁决（A / B） | ✅ **已裁决 A** | 2026-11-08 用户确认，已写入 §0.1 与 `ReleaseNotes/v2.100.0.5-dev.6.md`，P4-B 可开工 |
| T2 拆 `PCL.Core` | ⬜ 排在 P6 之后 | 明确不作为 P4 的前置 |
| 上游授权书面答复 | — | 现行合规门槛维持：不因本阶段改动扩大分发范围、不打平台 tag、不分发非 Windows 制品 |

无其他硬阻塞。

---

## 3. 现状分析

### 3.1 量化基线（本轮只读核查，2026-11-08）

| 项 | 值 |
|---|---|
| `PCL.Download` | 43 个 `.cs`，10,410 行 / 6,696 有效行 |
| `PCL.Download.Tests` | 33 个 `.cs`，6,913 行；**481 / 481 通过** |
| VB 主工程 | 122 个 `.vb`，43,568 行 / 33,811 有效行 |
| VB 侧引用 `PCL.Download.` | 112 次 / 9 文件（60 次集中在 `ModDownloadLib.vb`） |
| `LoaderTask` / `LoaderBase` / `LoaderCombo` / `LoaderDownload` | 214 / 94 / 69 / 40 次 = **417 处，分布在 26 / 16 / 12 / 10 个文件**（dev.5 说明按更宽口径记 473 处） |
| 12 个下载/资源 VB 文件 | 迁移计划基线 10,664 行 → 当前 9,981 行，**仅净减 683 行（−6.4%）** |

净减只有 683 行说明：**当前是「C# 新实现 + VB 旧实现并存、可回退」状态，Phase 6 的删除量几乎全部未做**。
`ModNet.vb` 仍 1,959 行、`ModLoader.vb` 791 行、`ResourceProject.vb` 726 行（比基线还多 15 行，
因为加了转发 / 投影代码）。

### 3.2 为什么 P6 不能「直接删文件」

按符号统计，VB 下载入口仍被**非下载模块**直接依赖，直接删会连锁编译失败：

| 符号 | 引用 | 主要非下载调用方 |
|---|---|---|
| `NetFile` | 167 次 / 11 文件 | `ModMinecraft.vb`、`ModDownloadLib.vb`、UI 页面 |
| `ResourceVersion` | 74 次 / 14 文件 | `PageDownloadCompDetail.xaml.vb`、`PageInstanceExport.xaml.vb` |
| `ResourceProject` | 52 次 / 12 文件 | 资源详情页、本地 Mod 页 |
| `LocalResourceFile` | 52 次 / 7 文件 | `PageInstanceMod.xaml.vb`（带 `OnUpdate` 事件、setter 触发 UI 刷新） |
| `DlClientFix` / `DlClientJarGet` / `DlSource*Get` | 各 2–10 次 | `ModLaunch.vb`、`ModMinecraft.vb`、`ModJava.vb`、`ModModpack.vb`、`PageInstanceOverall.xaml.vb` |
| `DlModRequest` / `NetRequestByLoader` | 8 / 6 次 | `ModJava.vb`、`PageInstanceExport.xaml.vb`、`MyImage.vb` |

迁移计划 §2.5 已判定 `NetRequestByClient*`（10 次 / 4 文件）是**全工程共享基础设施**，
P6 必须保留；同理 `Ping` / `FindFreePort*` / `NetDownloadByClient|Loader` 也保留。

### 3.3 历史踩坑（沿用《计划-Phase4主体》§5，本阶段仍适用）

1. **缓存是两份**：C# 与 VB 各一份版本 / 工程缓存，刷新按钮必须清两边（`ResourceVersion.ClearCaches()`）。
2. **投影而非 JSON 中转**：VB `LocalResourceFile` 带事件与跨运行指纹缓存，双向必须逐字段投影；
   `Cache\LocalMod.json` 的键与 `version` 字段不得改。
3. **`JObject.Merge` 语义不会随 System.Text.Json 继承**：数组拼接、来源 `null` 忽略、对象递归合并、
   其余替换，四条已由 `VersionMerger` 复现，装配层不得绕过它。
4. **VB 默认构造**：给类补带参构造后 VB 不再生成无参构造，JSON 解析路径会立刻编译失败。
5. **枚举同名歧义**：`ResourceVersion.ModLoaders` 字段与类型同名，须写 `CType(x, PCLCS.ModLoaders)`。
6. **指纹必须由宿主 `PrimeHashes` 灌入**，否则首次打开 Mod 页会重读 1–2 GB 的 mods 目录。
7. **内核日志只在 `Application_Startup` 挂过 sink**（dev.5 修复）；装配层新日志必须走 `DownloadLog`。
8. **修改安装的两处比较规则本来就不一致**（判定新清单区分大小写、删除列表忽略大小写），
   dev.5 决定只锁现状不统一——本阶段不得顺手「修好」。
9. **`pcl-next.sln` 不含 `PCL.Download.Tests`**（只有 5 个工程，`PCL.Download` 靠
   `pcl-next.vbproj` 的 `ProjectReference` 间接编译），所以「sln 编译 0 错误」不等于「测试全绿」。
10. **CA1416 警告数千条**（net10 迁移技术债），属噪音但不得借本阶段批量清理掩盖行为变化。

---

## 4. 改动文件清单

### 4.1 P4-A：编排决策 DTO 全集（C# 内核，可与真机冒烟并行）

| 文件 | 改动 |
|---|---|
| `PCL.Download/InstallPlan.cs` | `InstallPlanStep` 扩展 `Inputs` / `Outputs` / `SubSteps` / 取消语义；`InstallPlanRequest` 补齐剩余输入（不改现有 15 个字段语义） |
| `PCL.Download/InstallDownloadPlan.cs` | `NetFileDescriptor` 从**仅 1 项（Fabric 主文件）**扩到「九类新增 + 既有 Fabric 主文件」：原版 json / 支持库 / 资源索引 / OptiFine / Forge / NeoForge / LiteLoader / Fabric API / OptiFabric 为新增九类，按 VB 实际 `NetFile` 构造点再细分出原版 jar / 资源文件 / 支持库三类（见 `NetFileDescriptorKind`）。该文件原注释写「需 `LoaderBase` DTO 化大工程才能搬」，本轮正是那个工程 |
| `PCL.Download/InstallTaskDescriptor.cs`（新增） | 描述 `LoaderTask` / `LoaderCombo` / `LoaderDownload` 的构造参数：名称、权重、Block、Show、输入/输出槽位、进度回调与取消令牌。**只含数据，不含执行逻辑、不引用任何 VB 类型** |
| `PCL.Download/InstallPlanSteps.cs`（新增，可选） | `InstallPlanner.Build` 拆分出的每类步骤描述符工厂，避免 `Build` 膨胀成第二个 210 行 VB 函数 |
| `PCL.Download.Tests/InstallTaskDescriptorTests.cs`（新增） | 逐步骤对照 VB `McInstallLoadersGet` 1611–1819 断言，含反向验证 |
| `PCL.Download.Tests/InstallPlanTests.cs` | 保留现有 17 例，新增描述符断言 |

### 4.2 P4-B：VB 薄装配层与 UI 收尾

| 文件 | 改动 |
|---|---|
| `pcl-next/Pages/PageDownload/ModDownloadLib.vb` | `McInstallLoadersGet` 改为「取 DTO → 遍历装配」；内联 lambda 抽成按 `InstallStepKind` 分派的具名私有方法（留在 VB，不改签名） |
| `pcl-next/Modules/Download/LoaderPlanAdapter.vb`（新增） | **唯一**的 DTO → `LoaderBase` 装配处；不含业务判定，便于单点回滚 |
| `pcl-next/Pages/PageDownload/Resource/PageDownloadCompDetail.xaml.vb` | 复核 §4.9 第 2 条（版本列表已通过 `ResourceVersion.FromProjectId` 转发），补齐剩余 Provider 直连点 |
| `pcl-next/Modules/Resource/ResourceVersion.vb`、`ResourceProject.vb` | 逐步退化为 C# DTO 的**只读投影**（§4.9 第 3 条），保留 UI 需要的事件 / setter 行为 |
| `docs/plans/计划-Phase4主体-下载数据源C化.md` | §4.9 改标「已移交本文」，补 §0 口径裁决记录 |

### 4.3 P6：分三批下线（每批一个提交，可独立 revert）

**批次 1 — 删除无调用者实现**（依赖 P4 装配层生效）

- `pcl-next/Modules/Base/ModNet.vb`：`NetThread` / `NetSource` / `NetState` 的下载态机、
  `Thread` / `SourceFail` / `Reset` / `Merge` / `RangeNotSupportedException`、`LoaderDownload` 的
  文件检查与复用逻辑、`NetManagerClass` 的轮询引擎。
- `pcl-next/Modules/Base/NetInterop.vb`：**保留到最后一批**（`IsCSharpDownloadEnabled` 是 last-resort 回退开关）。

**批次 2 — 跨模块调用点重指**（按 §3.2 表逐个改，不改各模块对外行为）

- `pcl-next/Modules/Minecraft/ModLaunch.vb`、`ModMinecraft.vb`、`ModJava.vb`、`ModModpack.vb`
- `pcl-next/Pages/PageInstance/PageInstanceOverall.xaml.vb`、`PageInstanceExport.xaml.vb`
- `pcl-next/Controls/MyImage.vb`、`pcl-next/FormMain.xaml.vb`、`pcl-next/Pages/PageSelect/PageSelectLeft.xaml.vb`
- `pcl-next/Modules/Resource/LocalResourceLoaders.vb`（139 行，已是转发壳）

**批次 3 — 删 VB 入口与最终回归**

- `pcl-next/Modules/Minecraft/ModDownload.vb`（984 行）：删除已无调用者的实现；
  `DlSource*Get` 若仍被启动链路使用，须先迁到 C# 或明确保留并**登记为「非下载模块依赖」**。
- `pcl-next/Modules/Resource/*.vb`：删除残留实现，保留 UI 直接持有的投影类型（`LocalResourceFile` 等）。
- `pcl-next/Modules/Base/NetInterop.vb`：验收通过后删除，回退开关随之下线。
- `ReleaseNotes/` 追加本阶段发布说明（六章结构，按 §5.4）。

**必须保留**：`NetRequestByClient*` / `NetRequestByLoader` / `NetDownloadByClient|Loader` /
`Ping` / `FindFreePort*`（全工程共享基础设施，迁移计划 §2.5）。

### 4.4 明确不做（边界）

- ❌ 不改 `LoaderBase` / `LoaderTask` / `LoaderCombo` 的公开签名（417 处引用，会卷进 UI 基础设施）。
- ❌ 不重写 `Pages/` / `Controls/` 的 GUI 页面，不动启动 / 整合包 / 联机 / 音乐 / 皮肤模块。
- ❌ 不做 T2 拆 `PCL.Core`、不启动扩展系统 E2（均排在 Phase 6 之后）。
- ❌ 不把 `LoaderBase` 状态机搬进内核（口径 A 的前提）。
- ❌ 不改 `ModDownloadPolicy` 默认值、`RestrictMcDownload` 调用点，不把 MC 下载恢复当作本阶段成果。
- ❌ 不给 `PCL.Download` 引入第三方依赖，不破坏 `PclIsGuiFree` / 纯 BCL 约束。
- ❌ 不改缓存格式（安装清单键名、`Cache\LocalMod.json`、`DownloadPolicy.ini`）、不做配置迁移。
- ❌ 不动 Linux / Android 两份仓库，不改工作区根的规则文档备份。
- ❌ 不批量清理 CA1416 警告、不统一修改安装的两处比较规则。

---

## 5. 改动要点

1. **DTO 是唯一跨语言契约**：`InstallTaskDescriptor` 只用基元 / 集合 / 可空字符串，
   不引用 `McInstance` / `MergeJson` / `MyListItem` / `NetFile`，保证内核自包含、可单测（沿用 `InstallPlanRequest` 的既有做法）。
2. **装配层只做三件事**：按 `Kind` 取描述符 → 构造对应 `LoaderBase` 子类 → 挂上 VB 侧执行 lambda。
   任何 `If 组件已选 Then …` 形式的判定都不得出现在装配层，否则 P4 等于没完成。
3. **下载清单逐字对齐**：`NetFileDescriptor` 的 URL 顺序（镜像在前、官方在后）、落盘路径、
   `IsJson` / `MinSize` / `MaxSize` / `Hash` / `ActualSize` 必须与 VB `NetFile` 构造一致；
   `InstallDownloadPlan.cs` 现有注释已列出哪些类的地址由 VB 资源对象 / 镜像解析持有，按该清单逐项搬。
4. **`Block` / `Show` / `ProgressWeight` 不得重算**：`InstallPlanner.Build` 已逐字平移
   `McInstallLoadersGet` 的布尔式（如 `Forge` 的 `Block = FabricVersion Is Nothing AndAlso …`），
   装配层只读取，不得「顺手优化」——顺序或阻塞关系一变，只在装到一半时才暴露。
5. **取消与进度语义**：`LoaderBase` 的 `State` / `Progress` / `Cancel` / `Failed` 由 VB 状态机管理，
   C# 只提供 `CancellationToken` + `IProgress` 的映射入口；`NetInterop.vb` 的进度镜像
   （`DownloadDone` 原子加减、负增量回退）在 P6 删除前必须保持可用。
6. **调用点重指逐个做，不批量替换**：§3.2 的每个文件单独提交、单独编译；
   `ResourceVersion` / `LocalResourceFile` 的 setter 会触发 UI 刷新，重指时保留投影构造而非改用 C# 类型直接绑定。
7. **分阶段保留双实现**：批次 1/2 期间 `NetInterop.IsCSharpDownloadEnabled` 与
   `ResourceSourceGate` 注入点保持可用，任一批出问题可切回 VB 路径；批次 3 验收通过后才移除。
8. **内核日志**：装配层与新增描述符工厂的日志走 `PCL.Download.DownloadLog`，
   GUI 侧已在 `Application_Startup` 挂 sink，确保 `Log1.txt` 可见。

---

## 6. 回归影响面

沿用《计划-Phase4主体》§6 全部条目（安装页版本列表三种源设置、`FormMain` 预热、特供版本、
六类加载器列表与真机安装、版本合并后能启动游戏、Forge Java 8/17+、NeoForge 1.20.1/1.20.4+、
快照版本下划线路径、OptiFine 方式 A/B、LiteLoader 1.12.2/1.7.10、Fabric `1.20.1∞`、
修改安装旧实例的清单识别、缓存跨版本兼容等），并**新增**：

| 场景 | 风险 | 保障 |
|---|---|---|
| 安装步骤顺序 / 阻塞 / 权重 | 装配层读错 DTO → 装到一半卡住或进度倒退 | `InstallTaskDescriptorTests` 逐步骤断言 + 反向验证（故意改坏某项 `Block`，对应用例必须失败） |
| `Show` 与任务列表可见性 | 隐藏步骤被显示 / 显示步骤被隐藏 | 每类 `InstallStepKind` 的 `Show` 单独用例 |
| 下载文件清单 | 漏项 / 地址顺序颠倒 / 校验口径丢失 → 静默下到错误页 | 九类 `NetFileDescriptor` 与 VB `NetFile` 逐字段对照用例 |
| 取消与重试 | C# 引擎与 VB 状态机的取消判定不一致 | `NetInterop` 进度镜像回归；取消后 `State` 与 `DownloadDone` 归位 |
| 非下载模块（启动、Java、整合包导出、图片加载） | P6 批次 2 重指后行为漂移 | 每文件单独冒烟；`NetRequestByClient*` 签名零改动 |
| 首次打开实例 Mod 页 | 丢失 `PrimeHashes` → 重读 1–2 GB | 保留宿主灌入路径 |
| `Log1.txt` | 装配层日志写进 Console 而非文件 | 复用 dev.5 的 sink 挂接 |

**需新增 / 修改的测试**：`InstallTaskDescriptorTests`（新增）、`InstallPlanTests` / `InstallDownloadPlanTests` 扩充、
`InstallLayoutTests` / `VersionMergerTests` 保持不动（回归守门）。

---

## 7. 验证步骤

> 全部命令须符合 `AGENTS.md` §零：产物落在 `pcl-next/bin-net10/<代号>/<版本名>-<构建号>/<RID>/`，
> **不传 `-p:OutputPath`**；`PclRidGuard` 禁止 `any` / 空 RID 段，入口工程必须显式带 `-r`。

```bash
# 1. 内核单测（基线 481 例全绿，新增用例后必须仍全绿）
dotnet test PCL.Download.Tests/PCL.Download.Tests.csproj -c Release

# 2. 「sln 编译」在现行 RID 护栏下不能直接用（2026-11-08 实测）：
#    不带 -r → PclRidGuard 报「未指定真实 RID」（Directory.Build.props:120）；
#    带 -r   → NETSDK1134「不支持使用特定 RuntimeIdentifier 生成解决方案」。
#    故改为第 3 条的项目级构建；它会连带编译 pcl-next / PCL.Download / PCLCS / MeloongCore。

# 3. 入口工程带真实 RID 的验证构建（§0.3 写法）
dotnet build pcl-next/pcl-next.vbproj -c Release -r win-x64 --self-contained -p:PclChannel=Dev

# 4. CLI 自检套件（T1 已落地，见《计划-内核与GUI拆分》§1）
#    selftest 内部直接调用真实 download / install / loader / resource 命令，
#    覆盖 B1（分片重下）/ B2（完整安装）/ B5（资源联网），退出码即结论
pclcli selftest --suite all
pclcli help
```

**判定标准**

- `PCL.Download.Tests`：0 失败；反向验证成立（把 `InstallPlanner.Build` 某步骤的 `Block` / 权重改错，
  对应 `InstallPlanTests` / `InstallTaskDescriptorTests` 用例必须失败，事后逐条还原并复跑全绿）——
  沿用《计划-Phase4主体》§7 的做法。
- **项目级入口构建**：`dotnet build pcl-next/pcl-next.vbproj -c Release -r win-x64
  --self-contained -p:PclChannel=Dev` 要求编译阶段 0 错误（CA1416 为已知噪音）。
  2026-11-08 实测：编译阶段已通过，仅复制产物时被运行中的 `pcl-next`（PID 3584）锁定 DLL；
  关闭该进程后复跑同一命令即可完整通过——这是环境占用，不计作代码回归。
  **不要再写「`dotnet build pcl-next.sln -c Release` 0 错误」**，该命令在当前护栏下必然失败。
- **真机冒烟（用户执行，P6 验收前置）**：六类加载器逐类实际安装一次（尤其 NeoForge 的不可下载判定、
  LiteLoader 的 `1.8` / `1.9` 文件名）、安装页 / 修改页 / 资源详情页 / 实例 Mod 页各走一遍、
  进度 / 取消 / 重试正确、`PCL/Log1.txt` 能看到内核日志。
  注意 `pclcli selftest --suite all` 只覆盖 B1/B2/B5 的 CLI 自检，**不覆盖六类加载器逐类安装**，
  这笔欠账仍需人工点 UI 清偿，不得用 selftest 通过来抵扣。
- **P6 退出标准**：VB 下载代码为零（按 §4.3 清单逐文件核对）、功能与基准版等价、无回归；
  完成后在发布说明把 Phase 6 标为 ✅，并解锁 T2。

---

## 8. 回滚方案

1. **P4-A / P4-B**：全部为「内核新增 DTO + VB 装配层」。
   `McInstallLoadersGet` 的原构造段保留在 git 历史，回滚只需还原该方法体；
   DTO 与 `LoaderPlanAdapter.vb` 无调用方即无副作用，可留着不删。
   单轮出问题按提交 `git revert`，不影响 dev.6 已发布的 `InstallPlan` 语义。
2. **P6 批次 1/2**：每批一个提交，可按提交逐个 revert；
   删除前先确认 `IsCSharpDownloadEnabled` 与 `ResourceSourceGate` 注入点仍在，
   任一批回归失败即切回 VB 路径，不阻断发布。
3. **P6 批次 3**：这是唯一「删 VB 实现」的动作，回滚成本最高，故明确标注——
   需从 git 历史取回 `ModDownload.vb` / `ModNet.vb` 的对应方法体，并恢复 `NetInterop.vb`。
   **因此批次 3 必须在真机冒烟全部通过、且留有一份基准版对照之后才执行。**
4. **全程不变的内容**：缓存格式（`Cache\LocalMod.json`、`Cache\download.json`、
   `Cache\ForgeRecommendedList.json`）、安装清单键名、`DownloadPolicy.ini` 键名、
   `Dl*Result` 结构与 UI 读取方式、`LoaderBase` 公开签名、配置与注册表隔离口径。
   回滚后新旧程序互读缓存均不炸。
5. **不删除上游授权相关文本**：`LICENCE`、署名、关于页、`McLaunchPrecheck` 劝导逻辑与蓝色主色约定
   一律不动。

---

## 9. 合规边界（AGENTS §3.2 要求）

- 本阶段删除 / 平移的是**基于 PCL2 的第三方修改版**自有分支内代码，属「修改」而非新增复制，
  不新增许可义务；但须继续满足 PCL 许可证 L1–L5：对外名带 PCL 前缀（L2）、
  声明第三方修改版与龙腾猫跃署名 + 赞助链接（L1/L3）、保留 `McLaunchPrecheck` 正版与赞助劝导（L4）、
  Windows 主色相保持蓝色且不引入赞助解锁式功能（L5）。
- **MC 下载恢复是合规决策，不是代码开关**：`ModDownloadPolicy.vb` 把上游编译期常量下放为部署期配置，
  本阶段不改变其默认值、不扩大恢复范围、不把「恢复」写进发布成果。
- **分发范围不扩大**：维持工作区现行合规硬门槛——上游书面答复并留存前，
  `PLATFORM_RELEASE_ENABLED` 保持关闭、不打平台 tag、不分发任何非 Windows 制品。
  本文只涉及 Windows 线源码重构，不触发新的对外分发。
- 不改动 `LICENCE`、不移除任何署名或劝导逻辑；若后续把删除后的 VB 代码作为独立片段公开，
  须重新核对 L3 的开源与沿用指南要求。

---

## 10. 排期与依赖

```
P4-A（DTO 全集 + 单测）──────┐
六类加载器真机冒烟（可与上并行）├─→ P4-B（薄装配 + UI 收尾）─→ 真机回归
                              │                                  ↓
                              └──────────────────→ P6-1（删无调用者实现）
                                                   ↓
                                       P6-2（跨模块调用点重指）
                                                   ↓
                                       P6-3（删 VB 入口 + 全量回归）
                                                   ↓
                                       解锁 T2 拆 PCL.Core / 扩展系统 E2
```

- P4-A 与 P4-B 之间设退出门：DTO 用例全绿 + §0 口径裁决记录已写入发布说明
  → **2026-11-08 已满足**（541/541 全绿、反向验证成立；裁决见 §0.1 与
  `ReleaseNotes/v2.100.0.5-dev.6.md`），P4-B 可开工。
- P6-1 与 P6-2 之间设退出门：VB 侧 `PCL.Download.` 引用不再出现在被删实现里。
- 每轮改动完成后按 `AGENTS.md` §五提交，并在 `ReleaseNotes/` 追加本轮说明（若已到发布阶段）。
- 本计划完成后追加「阶段完成记录」，**不覆盖历史**（沿用《计划-扩展系统》的写法）。

---

## 11. 阶段完成记录（追加，不覆盖历史）

### P4-A 完成（2026-11-08，仅 Windows 线）

- **裁决**：用户确认口径 A，见 §0.1；已写入 `ReleaseNotes/v2.100.0.5-dev.6.md`，
  并改标《计划-Phase4主体》§4.9。Phase 4 完成判据以 §0.1 正式文本为准。
- **新增**：`PCL.Download/InstallTaskDescriptor.cs`（描述符 / 两个枚举 / 规格工厂 /
  `InstallTaskPlanner.Describe`）、`PCL.Download.Tests/InstallTaskDescriptorTests.cs`。
- **扩写**：`InstallPlan.cs`（步骤挂装配规格、`InstallPlanRequest.HasMinecraftJson`、
  `Build` 末尾注入规格）、`InstallDownloadPlan.cs`（`NetFileDescriptorKind` 13 类、
  `Kind` / `SimulateBrowserHeaders`、13 个下载工厂）、`InstallPlanTests.cs`（+4 例）。
- **护栏未破**：现有 15 个请求字段语义、所有 Block / 权重布尔式、`LoaderBase` 公开签名、
  `PclIsGuiFree` / 纯 BCL 约束一律未改；**VB 主工程零改动**，本轮无用户可见行为变化。
- **验证**：`dotnet test PCL.Download.Tests -c Release` → **541 / 541 通过**（基线 481 + 60）；
  反向验证：把 `Forge.Block` 临时改成恒 `true` 后恰好 2 个守门用例变红，还原后复跑全绿。
- **范围收口**：`Client` / `PostLibraries` 的子加载器骨架已由 DTO 完整描述；
  `OptiFine` / `Forge` / `NeoForge` / `LiteLoader` / `Fabric` 五个复合体内部子列表
  仍由宿主 VB 函数提供，已在 `SubStepsSource` 登记，**属 P4-B 逐类下沉范围**，
  本轮不假装已完成。地址的宿主侧来源（资源对象 / 镜像解析 / 版本 JSON 解析）也仍留在 VB，
  内核只收编「按类别决定校验口径」这一层。
- **待办**：P4-B（薄装配层 + UI 收尾）→ 真机回归 → P6 三批下线；
  六类加载器逐类真机安装冒烟仍是 P6 验收前置欠账。

### P4-A 补充交付（2026-11-08，同日）

- 新增 `docs/交接-Linux端-dev6-P4A.md`：**P4-A 专项**交接（5 个平台无关 C# 文件的 delta、跨语言契约、VB 事实来源对照、Linux 侧移植与验收步骤）；dev.6 整体裁决 / 发布号段 / 构建环境史只作背景，不列入交接范围。
  明确三仓库互不合并、裁决仅 Windows 生效、Linux 须自行裁决口径，并点出 Linux 线
  `pcl-next.vbproj` 仍为 `net10.0-windows` + `UseWPF`、`Build.ps1` RID 集合仍只有 win-* 的现状。
- 新增 `docs/经验与踩坑.md`：登记 RID 护栏下 sln 级构建不可用、提交钩子与
  `build-number.base` 不同源、运行中 `pcl-next.exe` 锁定旧构建目录三项。
- 修正 `scripts/git-hooks/prepare-commit-msg` / `post-commit` 的退化分支，改读
  `scripts/build-number.base`，与 `Get-BuildSmallVersion.ps1` 的 `root+base` 分支同源；
  `sh -n` 与临时消息文件实测本次提交追加 `Build: 0.70`。
- 以上均为文档 / 脚本，内核与测试工程无新增改动；**未推送远端**。
