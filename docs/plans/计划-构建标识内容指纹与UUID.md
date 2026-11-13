# 计划-构建标识：内容指纹与 UUID

> 立项：2026-11-13，用户裁决「build 版本的本质是帮助开发者看这个版本包含哪些提交，
> 如果出现版本号一样这就没意义了，优化算法，并对每个版本增加 UUID」。
> 适用：`PCL_NEXT_Windows`（`dev.6-Windows`）。Linux / Android 为独立仓库、规则各自维护，
> 本轮不跨仓库改动，见 §6。

## 1. 目标

1. 让构建标识能回答「**这一份 exe 到底包含哪些代码**」，而不只是「它是第几个提交编的」。
2. 解决**同号不同物**：同一个 `BuildSmall`（如 `0.90`）在 dirty 工作树下可反复编出内容不同的
   产物，编号、`AssemblyInformationalVersion`、关于页显示三者完全一样，追溯失效。
3. 为每个不同内容的版本分配一个 **UUID v4**，并在 exe 属性、启动日志、关于页可见。
4. **目录名不变**：仍是 `bin-net10\<代号>\<版本名>-<构建号>\<RID>\`，不引入后缀、
   不引入随机串（2026-11-12 的 `-flyout/-blur/-fix/-badge` 事故不得重演）。
5. 从机制上堵死 `-p:PclVersionDir=<手工字符串>` 这类绕过：路径护栏增加版本段合法性校验。

## 2. 前置条件与阻塞项

- 唯一取数入口是 `scripts\Get-BuildSmallVersion.ps1`，被四处调用：
  `scripts\Build.ps1`(`-CacheFile -Json`)、`pcl-next.csproj` 的 `PclBuildInfo` 目标
  (`-OutFile -CacheFile`)、`scripts\Copy-VersionedArtifact.ps1`(`-Json`)、
  `scripts\Install-GitHooks.ps1`(无参)。**保留全部旧字段与末尾输出**，否则这四处会漂。
- MSBuild 属性求值期不能跑 git，故 UUID 与内容指纹必须像 `pcl-build.small` 一样落盘缓存，
  由 `Build.ps1` / 编译期目标预先刷新。多工程、多调用点必须读到**同一个** UUID。
- 本会话 PowerShell 工具不可用，Bash 调 `powershell.exe` 曾被安全策略拦截；验证走
  `dotnet build` 触发 csproj 的 Exec（它本身就是脚本调用方）+ Python 独立复算，见 §7。

## 3. 现状分析

### 3.1 现有算法与其失效点

`BuildSmall = (继承基数 65 + 自基线以来提交数 - 1) × 0.01`，写 `M.mm`；
`AssemblyInformationalVersion` token = `M.mm.g<7位 sha>[.dirty]`。
它只反映**提交数**与**是否已脏**，不反映脏了什么：

| 场景 | 现有标识 | 问题 |
|---|---|---|
| 同一提交、干净工作树重复编译 | 完全相同 | 合理（同一份代码） |
| 同一提交、dirty 工作树改 A 后编译 | `0.90.gabc1234.dirty` | 与改 B 后完全相同 |
| 同一提交、dirty 工作树改 B 后编译 | `0.90.gabc1234.dirty` | **同号不同物，无法区分** |
| 手工 `-p:PclVersionDir=...-fix` | 目录名任意 | 护栏只做 `Contains`，不校验格式 |

2026-11-12 实际发生：`0.90-flyout2` / `0.90-flyout3` / `0.90` 三个目录、
三份内容不同的 exe，靠人工加后缀才勉强区分，且编号 `0.94/0.95` 反被误认成比 `0.91` 新。

### 3.2 本轮采用的三层标识

| 层 | 字段 | 来源 | 作用 |
|---|---|---|---|
| ① 提交阶梯号 | `BuildSmall` (`M.mm`) / `BuildSmallNumber` | 提交数（**算法不变**） | 单调、人读、目录名、跨机器一致、比较新旧 |
| ② 提交端点 | `BuildCommit`(7) / `BuildCommitFull`(40) | `git rev-parse HEAD` | 「包含到哪个提交」 |
| ③ 内容指纹 | `BuildContentId` (12 位 hex) | `SHA256(HEAD + git diff HEAD + git status + 未跟踪源码内容)` | 「这份产物实际有哪些代码」；同一提交号下内容不同即不同 |
| ④ 唯一标识 | `BuildUuid` (UUID v4) | 与 ③ 绑定缓存 | 每个不同内容一个全局唯一 ID，可贴进 issue / 反馈 |

设计取舍（**重要**）：`BuildUuid` 与 `ContentId` **绑定**，内容未变时复用同一 UUID，
内容变化时才换新的。理由：一次构建里脚本会被调用 3～4 次（Build.ps1、编译目标、
制品复制、钩子），若每次调用都随机生成，同一份 exe 的不同工程会拿到不同 UUID，
反而更乱；而「同一份代码 = 同一个版本 = 同一个 UUID」才符合版本语义。
需要每次出件强制换新时显式传 `-ForceNewUuid`（例如同一份代码重新打包发布）。

### 3.3 不改动的地方

- `BuildSmall` 数值算法、`scripts\build-number.base`、三段树目录形状、提交信息 `Build:` 行。
- `Copy-VersionedArtifact.ps1` 的制品文件名（仍为 `PCL-Next-<版本名>-<代号>-<构建号>[-dirty].exe`）。
- 三个平台仓库中 Linux / Android 的脚本与规则文档（本轮只改 Windows 线）。

## 4. 改动文件清单

| 文件 | 改动 |
|---|---|
| `docs/plans/计划-构建标识内容指纹与UUID.md` | 新增（本文件） |
| `scripts/Get-BuildSmallVersion.ps1` | 新增 `ContentId` / `Uuid` 计算与缓存、`-ForceNewUuid`、扩展 token 与生成源 |
| `pcl-next/Modules/Base/ModBuildInfo.cs` | 暴露 `BuildContentId` / `BuildUuid` / `BuildCommitFull` / `BuildToken` 常量 |
| `pcl-next/Pages/PageOther/PageOtherAbout.xaml.cs` | 关于页「构建版本」显示指纹与 UUID，完整 UUID 进 ToolTip |
| `pcl-next/Application.xaml.cs` | 无需改：启动日志经 `ModBuildInfo.Display` 自动带上新标识 |
| `Directory.Build.props` | `PclTreePathGuard` 增加版本段合法性校验；缓存说明补 UUID |
| `scripts/Build.ps1` | 构建控制台输出内容指纹与 UUID |
| `AGENTS.md` §0.3.2 / §十 | 口径摘要：三层标识 + 禁止手工覆盖 `PclVersionDir` |
| `分支与发布规范.md` §5.2.1 | 唯一真源：定义、落点表、口径变更留痕 |

## 5. 改动要点

### 5.1 内容指纹（`ContentId`）

```text
material = "HEAD="  + git rev-parse HEAD
         + "\nDIFF=" + git diff HEAD --no-ext-diff --unified=0        # 已跟踪改动（含 staged）
         + "\nSTATUS=" + git status --porcelain=v1 --untracked-files=all # 增删 / 重命名 / 未跟踪
         + "\nUNTRACKED=" + 未跟踪源码文件的「路径=内容 SHA256」列表（按路径排序）
ContentId = SHA256(UTF8(material)) 前 12 位小写 hex
```

- 中文路径：`git -c core.quotepath=false`，并临时把 `[Console]::OutputEncoding` 设为 UTF-8；
  个别路径若仍解析不到文件，退化为「路径=missing」记录（哈希依旧稳定，只是该文件的
  内容变化不参与指纹）。已在注释里写明这个边界。
- 未跟踪文件仅纳入常见源码/配置扩展名且 ≤ 2 MiB，避免把大文件与缓存拖进哈希。
- `obj/`、产物目录已被 `.gitignore` 排除，不进 `git status`，不会造成「每次编译指纹都变」的抖动。

### 5.2 UUID 缓存

- 路径：`pcl-next\obj\pcl-build.uuid`（与 `pcl-build.small` 同目录，`obj/` 已被忽略），
  单行 `<uuid>|<contentId>`，UTF-8 无 BOM。
- 逻辑：缓存存在且其中 `ContentId` 等于本次计算值 → 复用；否则生成
  `[guid]::NewGuid()` 并写回。`-ForceNewUuid` 强制生成。
- 所有脚本调用模式（`-Json` / `-CacheFile` / `-OutFile` / 无参）都执行同一段逻辑，
  因此**不需要区分调用来源**，多工程天然一致。

### 5.3 版本串与元数据

```text
AssemblyInformationalVersion = M.mm.g<sha7>.c<contentId12>[.dirty]
AssemblyMetadata("PclBuildUuid",       "<uuid>")
AssemblyMetadata("PclBuildContentId",  "<contentId>")
AssemblyMetadata("PclBuildCommitFull", "<40位 sha>")
```

`PclBuildInfoGenerated` 同步生成 `BuildToken` / `BuildCommitFull` / `BuildContentId` /
`BuildUuid` 常量；C# 与 VB 两条生成分支对称（Linux / Android 线仍是 VB）。
`ModBuildInfo.BuildToken` 从「运行时拼接」改为直接返回生成常量，避免公式两处漂移。

### 5.4 路径护栏

```xml
<_PclExpectedVersionDir>$(PclVersion)-$(PclBuildSmall)</_PclExpectedVersionDir>  <!-- 无缓存时 = $(PclVersion) -->
<Error Condition="'$(PclVersionDir)' != '$(_PclExpectedVersionDir)'"
       Text="…禁止用 -p:PclVersionDir 手工改写版本段（含 -fix / -blur 等后缀）…" />
```

比正则更稳：手工覆盖只改 `PclVersionDir`，而 `PclVersion` / `PclBuildSmall` 仍由工程与缓存决定，
两者一比即可发现。这样 §0.1 的「版本段必须由算法得出」从文档约束升级成机械校验。

## 6. 回归影响面

- **构建号 / 目录名 / 制品名**：不变，三段树与 `ReleaseArtifacts` 归档零影响。
- **旧调用方**：`-Json` 保留全部原字段，新增字段为增量；`Build.ps1`、
  `Copy-VersionedArtifact.ps1`、`Install-GitHooks.ps1` 无需改签名。
- **发布流程**：`AssemblyInformationalVersion` 变长，仅影响「exe 属性 → 产品版本」显示；
  `AssemblyFileVersion` / `VersionCode` / 升级判定不变。
- **性能**：每次构建多跑一次 `git diff` + 少量文件 SHA256，预计 1～3 秒，可接受。
- **跨平台**：脚本用 PowerShell 5.1 兼容语法（不用 `??` / `-AsHashtable` / 新 cmdlet），
  .NET 10 的 `AssemblyMetadataAttribute` 两类工程都可用。
- **未同步**：Linux / Android 仓库的三份 `AGENTS.md` / 规范 / 脚本仍是旧口径；
  它们构建号本就不可互比，本轮不同步，避免跨仓库改写，待用户裁决。

## 7. 验证步骤

1. `dotnet build pcl-next/pcl-next.csproj -c Release -r win-x64 --self-contained` → 0 错误；
   本轮不得出现新增警告。
2. 检查 `pcl-next\obj\PclBuildInfo.g.cs`：含 `BuildContentId` / `BuildUuid` / `BuildToken`
   与三条 `AssemblyMetadata`；`pcl-next\obj\pcl-build.uuid` 为 `<uuid>|<contentId>`。
3. 检查产物 dll 内可检出指纹与 UUID 字符串，且与缓存一致。
4. **同号不同物验证**：在仓库内新增一个未跟踪的临时文本文件 → 脚本 `ContentId` 与
   `BuildUuid` 必须变化；删除后 `ContentId` 必须恢复原值（UUID 换新属预期）。
   临时文件当场删除，不留工作树改动。
5. **稳定性验证**：连续两次不带 `-ForceNewUuid` 调用，`ContentId` 与 `BuildUuid` 不变；
   带 `-ForceNewUuid` 时 UUID 变化、`ContentId` 不变。
6. **护栏验证**：`dotnet build ... -p:PclVersionDir=2.100.0.6-0.93-fix` 应被新护栏拒绝
   （用 `-t:PclTreePathGuard` 快速验证，不产出完整构建）。
7. Python 独立复算指纹材料，与 PowerShell 结果交叉核对（公式一致）。

## 8. 回滚方案

- 本轮改动集中在脚本与显示层，回滚 = 还原本轮提交；`obj\` 下两个缓存文件删掉即可
  （下次构建自动重建）。旧算法不依赖新字段，回滚后 `AssemblyInformationalVersion`
  退回 `M.mm.g<sha>[.dirty]`，目录名与制品名完全不变。
- 已发布 / 已归档的产物不受影响：它们的版本串在编译期已固化，不随脚本变化。

## 合规边界

- 全部为本项目自有构建脚本与 UI 显示逻辑的自研改动，**不引入第三方代码、不引入 NuGet 包**；
  SHA256 / UUID 均用 .NET BCL，无许可归属问题。
- 不涉及 MC 本体下载门控、遥测、自动更新、身份标识（`ModBase.Identify`）等合规敏感链路；
  UUID 是**构建产物标识**，不是用户或设备标识，不采集、不上传。
