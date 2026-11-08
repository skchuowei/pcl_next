# 计划：内核与 GUI 拆分（含 `PCL.Cli` 命令行内核通道）

> 依据：《AGENTS.md》§零（产物路径）/§3.2（计划文档要求）、《PCL2_CSharp下载服务迁移计划.md》§4.3、§5 Phase 4/6、《dev.4公布标准.md》§2.1 B。
> 状态：**分两段执行**。T1（dev.4，工具链）本文已决定执行；T2（Phase 6 之后，正式拆壳）待启动。
> 本文件为专项计划，T2 完成后随版本归档，不作为长期规则入口。

---

## 0. 决定记录（为什么现在插 CLI，为什么现在不拆壳）

| 项 | 决定 |
|---|---|
| T1：`PCL.Cli` 最小骨架 | **插入 dev.4**，预算上限 **3 人日**，超支即砍命令（先砍诊断类，保留 `download` / `selftest`） |
| T2：正式拆内核（`PCL.Core` 工程化） | **排在 Phase 6 之后**，不在 dev.4/dev.5 动工 |
| 架构护栏 | **立即生效**：新增 C# 工程禁止引用 WPF 与 `pcl-next` 主工程，CI 卡住 |

**三条理由**

1. **dev.4 的硬门槛正在靠人工点击验收。** B1（大文件分片重下）/B2（完整安装一次）/B5（资源联网冒烟）已从 dev.2 欠到 dev.4，且按公布标准「不得再顺延」。CLI 把这三项从「人肉点 UI 看日志」变成「一条命令 + 退出码 + 日志留痕」，顺带把后续每次回归的成本压到接近 0。
2. **成本极低且互不干扰。** `PCL.Download` 已是 `net10.0`、纯 BCL、零第三方依赖（见 `PCL.Download.csproj`），CLI 只是它的第二个前端：不碰 WPF、不碰 VB、不改 `Application.xaml.vb` 的任何分支。
3. **现在拆壳是白拆。** Phase 6 要下线的正是 `ModNet` 下载部分 / `ModDownload.vb` / `Resource*`——现在把它们从 VB 里「拆」出来，拆完就删。等 Phase 6 完成，VB 只剩 `Pages`/`Controls`/`FormMain` 纯 UI，「拆壳」退化为「删残留 + 冻结 SDK」，成本从 30–40 人日降到 5–8 人日。

**T1 的边界（不得越界）**

- 不新增任何对外功能承诺；发布说明里记在「开发期」，不计入 §2.1 内容范围。
- 不改 WPF 入口逻辑（`Application.xaml.vb` 一行不动），不做 `pcl-next.exe --headless`，独立 exe。
- 不引入第三方依赖；不复制业务逻辑（只调 `PCL.Download` 与将来的内核 API）。
- 不清洗 VB 侧任何代码（那是 T2/Phase 6 的事）。

---

## 1. 目标

### T1（dev.4）——让内核「可被脚本驱动」

1. 新增独立控制台工程 `PCL.Cli`，引用 `PCL.Download`，在**无 WPF、无窗口、无用户交互**前提下完成真实网络与磁盘操作。
2. 三条命令支撑 dev.4 硬门槛：`download`（B1）、`install`（B2，本版只做到「下载 + 校验 + 落盘」，安装编排在 Phase 6 前仍是 VB，CLI 只覆盖其下载段）、`resource`（B5）。
3. 输出协议固定：stdout 为 JSON Lines（机器判定），stderr 为人读进度，日志落文件；**退出码即结论**。

**可验证标准**：`dotnet build pcl-next.sln -c Release` 0 错误；`PCL.Cli` 三条命令在干净目录下可重复执行且结果确定；B1 可通过「删除 jar → 跑 `download` → 日志出现分片/续传/校验记录」自证，不再依赖人工观察 UI。

### T2（Phase 6 之后）——内核与 GUI 真正分离

1. 内核 = 全部 C# 工程集合（`PCL.Core` + `PCL.Download` + `PCLCS` + 后续域），**不引用 WPF**，可被 CLI 与 GUI 同时宿主。
2. VB 主工程退化为壳体：`Application.xaml.vb` 只负责 boot、`Pages`/`Controls` 只负责渲染，业务调用全部下沉。
3. CLI 命令集覆盖内核全部公开能力（下载、资源、实例管理、启动、诊断），成为**回归与调试的一等公民**。

**可验证标准**：内核工程集合的 `ProjectReference` 闭包内 0 个 WPF 程序集；`PCL.Cli` 与 GUI 调用同一接口（同签名、同实现），且 CLI 全命令冒烟通过；GUI 行为与拆壳前一致（对照清单逐项验收）。

---

## 2. 前置条件与阻塞项

| 依赖 | 状态 | 影响 |
|---|---|---|
| `PCL.Download`（`IDownloadEngine` / `IResourceProvider` / `ResourceProviderRegistry` / `LocalResourceScanner` / `HashHelper`） | ✅ 已可用（Phase 1–3） | T1 全部命令的基础 |
| `PCL.Download.Tests`（xUnit） | ✅ | 复用其 FakeFileServer 做 CLI 集成测试 |
| net10 运行时提升（Phase 5） | ✅ 已完成 | CLI 可直接 `net10.0` |
| MC 下载合规开关（`ModDownloadPolicy`） | ✅ VB 侧已实现，**CLI 需同源读取** | 见 §5.4 |
| Phase 6（VB 下载模块下线） | ⬜ 未开始 | **阻塞 T2**，不阻塞 T1 |
| 真实网络与用户环境 | 由用户执行 | B1/B2/B5 的最终判定仍需用户机跑一次 |

**无 T1 硬阻塞项。**

---

## 3. 现状分析

### 3.1 耦合度实测（不是感觉，是数字）

| 指标 | 实测 | 含义 |
|---|---|---|
| `pcl-next/Modules` 规模 | 19,775 行 / 31 文件 | 内核候选面 |
| 其中引用 `System.Windows*` / `MessageBox` / `Interaction` 的文件 | **4 个**（`ModAnimation`、`MyCompat`、`ModMain`、`ThirdParty/DragHelper`） | 内核 → UI 的方向耦合很薄 |
| `Pages/` 下载相关 UI 胶水 | ≈6.3k 行（`ModDownloadLib` 2176、安装页 1199、修改页 727…） | **真正的问题在 UI → 内核方向**：页面里长着业务逻辑 |
| `Application.xaml.vb` 的命令行分支 | `--update` / `--gpu` / `--memory` 三个特例 | 有 CLI 通道的**形态**，但是没有 CLI 通道的**抽象**：仍以 WPF 进程启动，靠 `Environment.Exit` 早退 |
| `PCL.Download` 依赖 | 0 个第三方包 | CLI 立项无依赖风险 |

**结论**：T2 的难点不是「把 UI 从内核里摘掉」（那部分很薄），而是「把业务逻辑从 `Pages` 里捞出来」——这恰好就是 Phase 4 的「UI 重指」在干的事。所以 T2 天然应该排在 Phase 4/6 之后，而不是之前。

### 3.2 为什么 CLI 可以不等 T2

`PCL.Download` 的对外契约（`IDownloadEngine.DownloadAsync`、`IResourceProvider`、`ResourceProviderRegistry.CreateDefault`、`LocalResourceScanner.Scan`）都是**与 WPF 无关的 POCO 接口**。CLI 直接 new 一个实现、注入 logger、调方法即可，全程不需要窗口。这也就是「内核的第二个前端」，不是「拆内核」。

### 3.3 历史踩坑（T1 必须继承）

- **下载策略不能硬编码**：`ModDownloadPolicy` 的生效顺序是「环境变量 `PCL_ALLOW_MC_DOWNLOAD` → `DownloadPolicy.ini` → 内置默认 `True`」，且**首次访问即缓存**（运行期改文件不生效）。CLI 必须复刻同一顺序，否则会出现「GUI 禁用、CLI 照下」的合规漏洞。
- **BMCLAPI 限频**：高频 403/429 时**不禁用源**，且请求前 `Thread.Sleep(100)`。CLI 压测 B1 时最容易把这条规则打穿，务必走 `PCL.Download` 现有实现，不另写循环。
- **`DownloadLog` 命名冲突**：`PCL.Download` 里叫 `DownloadLog`（不是 `Logger`），原因是避免与 VB 的 `MeloongCore.Logger` 抢符号。CLI 侧引用时不要再引入同名类型。
- **产物路径锁死**：CLI 产物也必须在 `bin-net10\<代号>\<版本名>-<构建号>\<RID>\` 三段树内（§零），否则按错误产物处理。

---

## 4. 改动文件清单

### T1 新增

| 文件 | 职责 |
|---|---|
| `PCL.Cli/PCL.Cli.csproj` | `net10.0` 控制台，`ProjectReference` 仅 `PCL.Download`；`OutputPath` 指向三段树 `.../win-x64/cli/` |
| `PCL.Cli/Program.cs` | 参数解析（手写，不引 System.CommandLine）、命令分发、退出码映射 |
| `PCL.Cli/CliOutput.cs` | JSON Lines 输出器（事件类型 `progress` / `result` / `error`）+ stderr 人读格式化 |
| `PCL.Cli/CliPolicy.cs` | 复刻 `ModDownloadPolicy` 的读取顺序（env → ini → 默认），并在禁用时直接拒绝 `install` 类命令 |
| `PCL.Cli/Commands/DownloadCommand.cs` | B1：单/多文件下载，透出分片、续传、校验、源切换计数 |
| `PCL.Cli/Commands/InstallCommand.cs` | B2：版本 JSON → 客户端 jar + libraries + assets 的**下载段**（安装编排仍在 VB，本版不做） |
| `PCL.Cli/Commands/ResourceCommand.cs` | B5：`search` / `versions` / `fingerprint` 三个子命令，走 `ResourceProviderRegistry` |
| `PCL.Cli/Commands/SelfTestCommand.cs` | 组合场景：`--suite b1,b2,b5`，按顺序跑并汇总退出码 |

### T1 修改

| 文件 | 改动 |
|---|---|
| `pcl-next.sln` | 注册 `PCL.Cli` 工程（不加入启动项目） |
| `scripts/Build.ps1` | 新增 `-WithCli` 开关，**默认关闭**；开启时把 CLI 一并构建到 `.../cli/` |
| `.github/workflows/build.yml` | 增加 `dotnet build PCL.Cli` 与一个 `--selftest-offline` 冒烟步骤（不联网） |
| `后附：Directory.Build.props`（新增） | 架构护栏：对 `PCL.Cli` / `PCL.Download` 禁用 `UseWPF`，且其引用闭包不得出现 `pcl-next`/`PCLCS`/`MeloongCore.Wpf` |

### T2 预留（本版不实现，仅登记边界）

`PCL.Core/`（新工程，聚合内核域）、`PCL.Sdk/`（对外稳定 API 面，见《计划-扩展系统.md》）、`pcl-next.vbproj` 去掉对内核的直接实现依赖（只留 SDK 引用）。

**明确不做（T1）**

- 不做 `pcl-next.exe --headless` 的进程内模式（会污染 WPF 启动链，且单例互斥逻辑要重写）；
- 不做实例管理 / 启动 / Java 相关命令（这些域还没 C# 化，做了就是复制业务逻辑）；
- 不做交互式 TUI、不做安装包分发、不做自动更新。

---

## 5. 改动要点

### 5.1 命令契约（T1 只定这 4 条，其余后补）

| 命令 | 关键参数 | 成功退出码 | 失败退出码 |
|---|---|---|---|
| `pclcli download` | `--url <u> --to <path> [--sha1 <h>] [--size <n>]` 或 `--file <req.json>` | 0 | 1 网络失败 / 2 校验失败 / 3 参数错误 |
| `pclcli install` | `--version-json <path> --dir <instance> [--only client,libs,assets]` | 0 | 1 下载失败 / 2 校验失败 / 4 策略禁用 |
| `pclcli resource` | `search --query <q> [--source modrinth\|curseforge\|auto]`、`versions --project <id>`、`fingerprint --file <p>` | 0 | 1 网络 / 5 源不可用（降级时给 notice，不算失败） |
| `pclcli selftest` | `--suite b1,b2,b5`、`--workdir <tmp>` | 0 | 首个失败套件的退出码 |

### 5.2 输出协议

- **stdout 只输出 JSON Lines**，一行一个事件，字段固定：`{"ev":"progress","cmd":"download","done":123,"total":456}`；最终一行必须是 `{"ev":"result","ok":true,...}`。
- **stderr 输出人读进度**（含速率、源切换、分片数），供用户/CI 日志直接看。
- 日志同时落 `--workdir` 下的 `cli-<cmd>-<ts>.log`，B1 的「分片/续传/校验被走到」以日志行为为准。

### 5.3 引擎复用，不复制

所有下载走 `IDownloadEngine.DownloadAsync`，禁止在 CLI 内写 `HttpClient` 循环——**这是 T1 唯一的硬性架构要求**。B1 的意义就是验证引擎，如果 CLI 绕过引擎，验证就失效了。

### 5.4 策略同源（合规红线）

`CliPolicy` 必须与 `ModDownloadPolicy` 顺序一致（`PCL_ALLOW_MC_DOWNLOAD` → `DownloadPolicy.ini` → 默认 `True`），并在日志首行打印命中的来源（`env` / `ini` / `default`），便于事后核对。禁用时 `install` 直接退出码 4，**不得**提供绕过开关。

### 5.5 架构护栏（立即生效，T2 的地基）

在 `Directory.Build.props` 里加一条可 CI 校验的约束：`PCL.Cli` 与 `PCL.Download` 的引用闭包中不得出现 `PresentationFramework` / `PresentationCore` / `WindowsBase` / `pcl-next`。

理由：这条护栏是**唯一能在 T2 之前防止架构腐化的手段**。没有它，任何人（包括我）都可能在 CLI 里顺手 `using System.Windows`，半年后 T2 就要重做一遍。

### 5.6 T2 拆壳要点（先写清，避免届时返工）

1. 新建 `PCL.Core`，把 `Modules` 中已 C# 化的域聚合进来；`PCL.Download` 保持独立（它已是干净库）。
2. `PCL.Sdk` 只放**接口 + DTO + 枚举**，不放实现——它是插件与 CLI 共同依赖的面（见扩展系统计划）。
3. VB 主工程改为「引用 SDK 的壳体」，`Pages` 里的编排逻辑按 Phase 4 的节奏逐步下沉，**不做一次性大迁移**。
4. `Application.xaml.vb` 拆成 `Boot`（日志/路径/编码/单例）与 `UiBoot`（皮肤/窗口/主题），前者移到内核，后者留壳。

---

## 6. 回归影响面

| 面 | 影响 | 处置 |
|---|---|---|
| WPF 主程序运行时 | **无**（CLI 是独立 exe，不参与 WPF 启动链） | 冒烟一次即可 |
| `dotnet build pcl-next.sln` | 多一个工程，构建时间 +数秒 | CI 时间可接受 |
| `Test-VersionMatrix.ps1` 六渠道矩阵 | 需确认不会因新工程缺 `PclChannel` 而报错 | 在 csproj 里给 `PclChannel` 默认值 |
| 产物目录 | `.../win-x64/cli/` 新增一层子目录 | 在 `AGENTS.md` §0.1 补一句「`cli/` 属三段树内的合法子层」，避免被当成散落产物清理 |
| 合规 | CLI 能跑 MC 下载 → 与 GUI 同受策略约束 | §5.4 的 `CliPolicy` + 退出码 4 |

---

## 7. 验证步骤

```bash
# 1) 编译（§零 三段路径）
dotnet build PCL2/pcl-next.sln -c Release            # 期望 0 错误
./scripts/Build.ps1 -Channel Dev -WithCli            # 期望产物出现在 .../2.100.0.4/win-x64/cli/

# 2) 离线冒烟（不联网，CI 可跑）
pclcli selftest --suite offline --workdir /tmp/pclcli

# 3) B1：大文件分片真实触发（删缓存后重下 ≥20MB）
pclcli download --url <真实 jar url> --to /tmp/pclcli/client.jar --size <n> --sha1 <h>
# 判定：退出码 0；stdout 出现 result.ok=true；日志含 "chunks=" 且 >1、"resume"、"verify"

# 4) B5：资源联网冒烟（中英文各一次 + 指纹反查）
pclcli resource search --query sodium --source auto
pclcli resource search --query 钠 --source auto
pclcli resource fingerprint --file <本地 mod jar>
# 判定：退出码 0；CF Key 缺失时 stdout 出现 source=mr + notice（不得抛异常）

# 5) 策略同源（合规）
PCL_ALLOW_MC_DOWNLOAD=False pclcli install --version-json <x> --dir /tmp/i   # 期望退出码 4
```

**验收判定**：第 1、2、5 项必须由我执行通过；第 3、4 项需用户在真机跑一次并留存日志（对应 dev.4 §2.1 B 的门槛要求）。

---

## 8. 回滚方案

| 场景 | 回滚动作 | 保留内容 |
|---|---|---|
| CLI 阻塞 dev.4 发布 | 从 `pcl-next.sln` 移除 `PCL.Cli` 条目 + `scripts/Build.ps1` 的 `-WithCli` 分支回退，**删除 `PCL.Cli/` 目录** | `PCL.Download` 全部改动（与 CLI 无关）；dev.4 内容范围回到原定 A/B |
| CLI 某命令行为异常 | 只删该命令文件 + `Program.cs` 分发项，其余保留 | 其他命令与引擎 |
| 架构护栏误伤 | 临时注释 `Directory.Build.props` 中的闭包检查，**记入待核对清单**后一周内修好 | 不留长期豁免 |

回滚时按《AGENTS.md》§六 出具回退报告（`回退报告-CLI.md`），禁止静默回退。

---

## 9. 执行记录（T1 已落地）

### 9.1 实际交付物

| 文件 | 内容 |
|---|---|
| `Directory.Build.props`（新增，仓库级） | ① `PclChannel` / `PclVersion`（正则读 `ModBase.vb`，不手抄版本）/ `PclRidSegment` / `PclTreeRoot`；② 架构护栏 `PclGuiFreeArchGuard` |
| `PCL.Cli/`（新增 9 个文件） | `Program` / `CliArguments` / `CliOutput` / `CliPolicy` / `CliSession` / `McSource` / `McVersionJson` / `McVersionResolver`、`Commands/{Download,Install,Resource,SelfTest}Command` |
| `PCL.Download/PCL.Download.csproj` | 声明 `PclIsGuiFree=true`（接入护栏） |
| `PCL.Download/ResourceSearcher.cs` | 修 Modrinth `project_type` facet（见 9.4） |
| `PCL.Download.Tests/ResourceSearcherTests.cs` | 补 2 个回归用例 |
| `pcl-next.sln` / `scripts/Build.ps1` / `.github/workflows/build.yml` | 注册工程、`-WithCli` 开关、CI 构建 + 离线冒烟 |

### 9.2 架构护栏的三次修正（重要，避免后人重踩）

1. **首版静默失效**：把 `PclArchGuardEnabled` 的默认值放进了 `Condition="'$(PclIsGuiFree)'=='true'"` 的 `PropertyGroup`——`Directory.Build.props` 在工程体之前求值，那时 `PclIsGuiFree` 仍是空，开关永远为 false，整条护栏不执行。**已改为无条件默认 true**。
2. **ItemGroup 内 `%(...)` 条件不可靠**：`Include="@(ReferencePath)" Condition="'%(Filename)'=='x'"` 在 Target 内实测漏判，改为 `@(_PclRefName->WithMetadataValue('Identity','x'))`。
3. **不能把 `WindowsBase` 当 WPF 证据**：它在 `Microsoft.NETCore.App` 引用包里人人都有（实测 `net10.0` 工程的 `ReferencePath` 恒含 `WindowsBase`），拿它判定会 100% 误报。最终禁列：`PresentationFramework` / `PresentationCore` / `System.Windows.Forms` / `System.Xaml` / `ReachFramework` + `pcl-next` / `PCLCS` / `MeloongCore` / `MeloongCore.Wpf`。

护栏已做**双向验证**：正常构建放行；人为把 `PCL.Download` 加进禁列后，构建以 `error : 架构护栏：工程「PCL.Cli」……` 失败。

### 9.3 实测证据（本机，2026-11-01）

| 项 | 命令 | 结果 |
|---|---|---|
| 全量构建 | `dotnet build pcl-next.sln -c Release` | **0 错误** / 2445 警告（含既有 CA1416） |
| 单元测试 | `dotnet test PCL.Download.Tests` | 258 通过 / 1 失败（`SlowSource_IsRotatedToFastSource`，**偶发**，单独复跑通过，非本次改动引入）/ 新增 4 个 `GetModrinthAddress` 用例全通过 |
| 离线自检 | `pclcli selftest --suite offline` | 6 项全过（arch / hash / file-check-rule / policy / version-json / resource-registry） |
| **B1** | `pclcli selftest --suite b1` | 最新正式版 client jar **39.6MB / 8.7s**，日志含「开始分块」×N、「下载结束，开始合并 10 个分块」，`verify ok`，退出码 0 |
| **B2** | `pclcli selftest --suite b2` | `install --only client,assets` 成功，2 文件 / 42MB / 10.6s，退出码 0 |
| **B5** | `pclcli selftest --suite b5` | `sodium` 20 条、`钠` 8 条；CF 缺 Key 时给 notice 不抛异常；指纹反查正常返回（原版 jar 无命中，符合预期） |
| 策略同源 | `PCL_ALLOW_MC_DOWNLOAD=False pclcli install …` | 退出码 **4**，stdout 有 `policy_blocked` 事件 |
| stdout 纯净 | `pclcli selftest 2>/dev/null` | 仅 JSON Lines，无杂字符 |

产物落点：`PCL2/bin-net10/Dev/2.100.0.3-0.20/win-x64/cli/pclcli.exe`（三段树内 `cli/` 子层；目录名含构建号，见 AGENTS §0.1）。

### 9.4 与计划的偏离（三处，均有理由）

| 偏离 | 原因 |
|---|---|
| 走 `FlagsOf` 展开而非 `Type.ToString()` 直接拼 facet | `ResourceTypes.Any` 会拼出 `project_type:any`，Modrinth 视为未知取值**直接返回 0 命中**——即「不限种类搜索」此前根本搜不到东西。`ModOrDataPack` 同样会拼成非法值。这是内核真 bug，已修 + 补测试 |
| `--mirror` 默认 `auto`（官方优先 + BMCLAPI 兜底），未完全照抄 VB 的 `DlSourcePreferMojang` 设置 | CLI 没有 Settings 存储；VB 的源顺序依赖用户设置项，CLI 用「全部候选交给引擎 failover」表达同一语义，且 `ForLibrary` 对 forge/fabric/neoforge 的「不加官方源」规则已逐条对齐 |
| 架构护栏用 Target + `WithMetadataValue`，而非计划里「props 里加一条约束」的笼统说法 | 见 9.2，简单写法实测静默失效 |

### 9.5 已知遗留（交接项，按优先级）

1. **与 `McVersionManifestClient` 合流（必须做）**：并行工作线正在内核里加 `PCL.Download/McVersionManifest.cs`（版本清单获取器，含 Mojang/BMCLAPI 双源竞速）。落地后 `PCL.Cli/McVersionResolver.ResolveTextAsync` 的「拉清单 + 找 id + 取 url」应改为调用它，**删除 CLI 侧的清单解析**，避免同一契约两处实现。当前刻意不耦合：该文件在本次提交时仍在写入中、未经构建验证。
2. **`--objects` / natives 未完整**：`--objects` 可下载 assets 全部对象，但 natives 分类器既未下载也未解压，`inheritsFrom` 未合并。保持「明确不做」优于「做一半」。
3. **Windows 独占**：`DownloadEngine` 要求本地路径含 `:\\`，故 `pclcli` 只能在 Windows 跑（TFM 为 `net10.0` 不限制，但运行期受限）。CI 为 `windows-latest`，无影响。
4. **`MeloongCore` / `PCLCS` 的 `OutDir` 仍指向各自 `bin\`**（AGENTS §0.3 点名要求改）。本次未动：它们属 GUI 侧且有发布流程依赖，改动风险大于收益，建议并入 T2 拆壳时统一处理。
5. **`SlowSource_IsRotatedToFastSource` 偶发失败**：与本次改动无关（下载引擎慢源轮换依赖真实时钟），单跑稳定通过，建议后续加时间注入。
