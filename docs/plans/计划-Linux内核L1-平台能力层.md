# 计划：Linux 内核 L1 第一批 —— 平台能力层（探测 / 路径拼接 / 路径比较）

> 依据：《AGENTS.md》§3.1/§3.2（大改动先写专项计划）、§零（产物路径）、§五（提交与构建号）、
> §十一（Linux 分支纪律）；《分支与发布规范.md》§0B / §0C；
> 《计划-Linux长期分支.md》§1（L1 判据：内核 / CLI 在 `linux-x64` 真实可用，
> 下载引擎路径、磁盘预检无 Windows 假设）。
>
> 状态：**执行中**（2026-11-07）。分支 `linux`，基线 `e5113ba`（= `dev` `4ca5d26` + 1 个同步 merge）。
> 本文件随 L1 完成归档，不作为长期规则入口。

---

## 1. 目标与可验证标准

| 项 | 内容 |
|---|---|
| 目标 | 在内核层建立**唯一**的平台能力入口：① 平台 / 架构探测；② 跨平台路径拼接与可移植写法归一；③ 按文件系统语义选择路径比较口径。并把现存的重复实现与 Windows 专属写法收进去。 |
| 可验证标准 | ① Windows 侧 `dotnet build` 三个内核工程 0 错误；② `dotnet test PCL.Download.Tests` 全绿；③ `pclcli selftest --suite offline` 新增 `platform` 检查通过（Windows 本机跑一次，Linux 由 `build-linux` CI 跑）。 |
| 判定口径 | 「无 Windows 假设」= 新增/改动代码不含硬编码 `\` 分隔符、不假定文件名大小写不敏感；平台差异只允许出现在 `PCL.Download/Platform/` 一处。 |

## 2. 前置条件与阻塞项

| # | 项 | 状态 | 对本轮的影响 |
|---|---|---|---|
| B5 | 用户未提交的下载修复 WIP：`PCL.Download/DownloadEngine.cs`、`DownloadFileTask.cs`、`PCL.Download.Tests/DownloadEngineTests.cs`、`PCL.Cli/PCL.Cli.csproj` | 🔒 占用中 | **不碰**。因此 L1 判据里的「本地资源路径分类」（`DownloadEngine` 的 `"\\assets\\"` 标记匹配）与「磁盘预检」（`DownloadFileTask.CheckDiskSpace` 的 `DriveInfo` 匹配）本轮**不做**，待用户提交后单独一批 |
| B1 | 合规书面问询未发送（`docs/合规/跨平台授权问询.md` 仍为草稿） | ⬜ | 不阻塞编译护栏与本地测试；**阻塞任何对外分发的 Linux 制品** |
| B2 | Linux 侧实跑需用户 / CI | ⚠ | 本机只做 Windows 侧验证，Linux 结论以 `build-linux` 为准 |
| B4 | `Directory.Build.props` 反斜杠 | ✅ 已修（已提交） | 无 |

## 3. 现状分析

1. **平台探测有两份**：`PCL.Cli/McVersionJson.cs` 的 `CurrentOsName` / `CurrentOsArch` 是内核里唯一在用的探测实现；
   下载库侧没有。后续任何模块再要平台判定就得再抄一份（§0.3.1 的「第二处真源」教训）。
2. **`OptiFineProfile.BuildInstallerTarget` 用反斜杠字面量拼路径**（`"libraries\\optifine\\OptiFine\\"`）。
   Windows 恰好正确，Linux 上会生成名字里带 `\` 的单个目录，安装器落盘位置全错。
3. **路径比较一律用 `OrdinalIgnoreCase`**（`LocalResourceFile.PathIsSame`、`LocalResourceScanner` 根目录比较）。
   Linux 文件系统区分大小写，`mods/A.jar` 与 `mods/a.jar` 是两个文件，现有比较会把它们判成同一个。
4. **没有统一的可移植路径写法**：`InstallLayout` 用内联 `.Replace('\\','/')` 处理清单相对路径，
   同一种归一散落在多处。
5. 现有 `OptiFineProfileTests` 用 `@"C:\..."` 字面量断言，只有实现硬拼反斜杠时才通过——
   即测试本身把 Windows 假设固化成了期望值。

## 4. 改动文件清单

### 新增

| 文件 | 内容 |
|---|---|
| `PCL.Download/Platform/PlatformInfo.cs` | 平台种类（Windows / Linux / MacOS / Other）、`OsName`（Mojang rules 用词 `windows/linux/osx`）、`ArchName`（`x86/x64/arm64`），进程内只求值一次 |
| `PCL.Download/Platform/CrossPlatformPath.cs` | `Join(params string[])`（忽略空段、逐段去尾分隔符、用平台分隔符拼接）、`ToPortable(string)`（`\` → `/`，供清单键与归档相对路径用） |
| `PCL.Download/Platform/PathComparison.cs` | `Comparison` / `Comparer`：Windows、macOS 用 `OrdinalIgnoreCase`，其余（含 Linux）用 `Ordinal`；`PathsEqual(a,b)` 去尾分隔符后按上述口径比较 |

### 修改

| 文件 | 改动 |
|---|---|
| `PCL.Download/OptiFineProfile.cs` | `BuildInstallerTarget` 改用 `CrossPlatformPath.Join`，去掉两处反斜杠字面量 |
| `PCL.Cli/McVersionJson.cs` | `CurrentOsName` / `CurrentOsArch` 委托 `PlatformInfo`，删除重复的 `RuntimeInformation` 判定 |
| `PCL.Download/LocalResourceFile.cs` | `PathIsSame` 改用 `PathComparison.PathsEqual`；`GetHashCode` 与相等口径对齐 |
| `PCL.Download/LocalResourceScanner.cs` | 根目录比较改用 `PathComparison.PathsEqual` |
| `PCL.Download/InstallLayout.cs` | 清单相对路径归一改用 `CrossPlatformPath.ToPortable` |
| `PCL.Cli/Commands/SelfTestCommand.cs` | offline 套件新增 `platform` 检查：平台 / 架构与本进程一致、`Join` 用平台分隔符、比较口径与平台匹配、`ToPortable` 幂等 |
| `PCL.Download.Tests/OptiFineProfileTests.cs` | 安装器落盘断言改为平台无关（期望值用 `Path.Combine` 构造） |

### 新增测试

| 文件 | 内容 |
|---|---|
| `PCL.Download.Tests/PlatformCapabilityTests.cs` | `PlatformInfo` 与 `RuntimeInformation` 一致；`Join` 空段 / 尾分隔符 / 重复分隔符；`ToPortable` 幂等；`PathComparison` 在 Windows 忽略大小写、在 Linux 区分大小写（按当前平台断言）；`LocalResourceFile` 相等与哈希一致 |

### 明确不做（本轮）

- `DownloadEngine.cs`、`DownloadFileTask.cs`、`DownloadEngineTests.cs`、`PCL.Cli.csproj`（B5 占用）；
- `InstallCommitPlan` 的 `Ordinal` / `OrdinalIgnoreCase` 混合口径——文件头已注明是**继承自原实现的有意状态**，
  改它属于顺手改，不在本轮；
- `AppDataPaths` / XDG 目录解析：当前内核没有真实消费者（CLI 默认工作目录 `Path.GetTempPath()` 在 Linux 同样可用），
  按「不写空壳」原则推迟到有实际需求（GUI 配置目录 / 缓存目录）时再做；
- 进程与 shell 调用抽象：下载内核不启动外部进程，同样推迟；
- 不改 `pcl-next` / `PCLCS` / `MeloongCore.*`，不引入 Avalonia，不新建工程。

## 5. 改动要点

1. **`PlatformInfo`**：`Kind` 由 `RuntimeInformation.IsOSPlatform` 求值一次；`OsName` 严格沿用
   `McVersionJson` 现有映射（`windows` / `osx` / 其余一律 `linux`），`ArchName` 沿用现有映射
   （`X86 → x86`、`Arm64 → arm64`、其余 `x64`）。**不改语义**，只把实现移到唯一入口。
2. **`CrossPlatformPath.Join`**：逐段 `TrimEnd('/', '\\')` 后 `Path.Combine`，空段跳过。
   约定：各段内部不转换分隔符（Windows 风格字符串在 Linux 上仍按字面处理），
   只保证**拼接点**用平台分隔符、不产生重复分隔符。`ToPortable` 只做 `\` → `/`，重复调用结果不变。
3. **`PathComparison`**：`Windows/MacOS → OrdinalIgnoreCase`、其他 → `Ordinal`。
   macOS 保持忽略大小写是为了不改变既有行为（当前不出 macOS 制品）。
   `PathsEqual` 先 `TrimEnd('/', '\\')` 再比较，保留原 `PathIsSame` 的去尾语义。
4. **`OptiFineProfile`**：`tempFolder + "OptiFine.jar"` 与旧版 `libraries\optifine\OptiFine\...`
   两处都改 `Join`。参数约定「需以分隔符结尾」保留（`Join` 对带/不带尾分隔符都成立）。
5. **`SelfTestCommand` 的 `platform` 检查**：纯离线、无文件写入；失败即返回非 0，
   使 `build-linux` 在 Linux 上真正验证这套能力，而不是「编过就算」。
6. **测试平台无关化**：`OptiFineProfileTests` 的期望路径改用 `Path.Combine` 构造，
   使同一份断言在 Windows 与 Linux 都成立（`build-linux` 会在 ubuntu 上跑同一批测试）。

## 6. 回归影响面

| 面 | 影响 | 处置 |
|---|---|---|
| Windows 路径输出 | `Join` 在 Windows 用 `\`，与改动前 `OptiFineProfile` 输出逐字节一致 | 现有 `OptiFineProfileTests` 全部保留，期望值改写后仍断言同一字符串 |
| 大小写语义 | Windows / macOS 仍是 `OrdinalIgnoreCase`，行为不变；Linux 改为区分大小写 | 新增测试按平台断言；Linux 侧由 `build-linux` 覆盖 |
| `LocalResourceFile` 哈希 | 哈希与相等口径对齐（Windows 仍为忽略大小写相等） | 无跨进程持久化依赖该哈希（只在进程内去重） |
| CLI selftest | offline 套件多一项检查 | `--suite offline` 退出码语义不变（失败仍是 5/Internal） |
| 架构护栏 | 不新增工程引用、不碰 WPF | `PclIsGuiFree` 与 `PclTreePathGuard` 照常生效 |

## 7. 验证步骤

```bash
# 均在仓库根 PCL2/ 下执行；产物落 bin-net10/Dev/<版本名>-<构建号>/<RID>/（§零）
dotnet build PCL.Download/PCL.Download.csproj -c Release
dotnet build PCL.Cli/PCL.Cli.csproj -c Release
dotnet test  PCL.Download.Tests/PCL.Download.Tests.csproj -c Release

# 离线自检（含新增 platform 检查）
dotnet bin-net10/Dev/<版本名>-<构建号>/any/cli/pclcli.dll selftest --suite offline --workdir <tmp>
```

判定：构建 0 错误；测试全绿；selftest 退出码 0 且 `check ok: platform`。
Linux 侧：`build-linux` job 绿（本机不实跑，B2）。

## 8. 回滚方案

| 场景 | 动作 | 保留 |
|---|---|---|
| `Join` 改坏 Windows 路径 | 单文件 revert `OptiFineProfile.cs`，`CrossPlatformPath` 保留待修 | 平台层与其余改动 |
| 路径比较改动引起 Windows 回归 | 单文件 revert `PathComparison.cs` 的采用点（`LocalResourceFile` / `LocalResourceScanner`），比较器本身保留 | `PlatformInfo` / `CrossPlatformPath` |
| `platform` 检查在 CI 误报 | 移除该检查项，保留平台层 | 其余全部 |

回滚一律按《AGENTS.md》§六 出回退报告，不静默回退。
