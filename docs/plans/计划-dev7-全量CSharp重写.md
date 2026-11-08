# 计划：dev.7 全量 C# 重写（仓库不留 VB 代码）

> 依据：《AGENTS.md》§一（开工前置）/§二（需求歧义须确认）/§3.1、§3.2（大改动专项计划）、
> 《分支与发布规范.md》§5（版本号 / 号段）、《公布标准.md》§3（7 项门槛）、
> 《PCL2_CSharp下载服务迁移计划.md》§5（Phase 0–6）、
> 《计划-内核与GUI拆分（CLI）.md》T1/T2、
> 《计划-Phase4收尾与Phase6下线.md》（P4-B / P6 清单，本计划吸收之）。
>
> **状态**：2026-11-08 用户裁决生效，开工中。
> **范围**：仅 Windows 线 `PCL_NEXT_Windows`；Linux / Android 为独立仓库，完成后各自移植，不互拷、不合并。
> **本文件取代**：dev.6 及更早所有「GUI 页面 C# 化不在重写范围」「C# 重写未完成」的口径表述，
> 以及《计划-Phase4收尾与Phase6下线》§4.4「明确不做」的第 2 条（不重写 `Pages/` / `Controls/`）。

---

## 0. 用户裁决记录（唯一真源，2026-11-08 16:05）

| 项 | 裁决 |
|---|---|
| 完成范围 | **不留 VB 代码，全部改为 C#**。含 WPF GUI 页面、控件、启动器、游戏启动链路、`ModLoader` 状态机、`My Project` 生成文件；不是只完成下载服务 |
| 发布时机 | **等重写全部完成后再发布 dev.7**；dev.7 即「C# 重写全部完成」的发布版本，不发中间版 |
| 验收脚本 | 按 CLI/GUI 分离路线（T1/T2）**后补脚本**，替代人工逐类点 UI 的旧验收方式；六类加载器真机冒烟欠账由脚本清偿 |
| 与旧口径冲突 | dev.6 发布说明「主体 GUI / 启动器仍为 VB」「按完整重写口径仍是纵切」、迁移计划「UI 页面整体重写不含」、P4/P6 计划 §4.4「不重写 GUI」——**均被本裁决推翻**，后续不得再援引 |
| 阶段关系 | P4-B 批次 2、P6 三批下线不再是独立阶段：VB 文件全部消失后，其目标（VB 下载实现归零、回退开关下线）被全量转换自然吸收；T2 拆 `PCL.Core` 与 CLI 验收脚本作为**转换完成后的收尾**，不再等 P6 |

> AGENTS §二要求「与现有架构冲突的需求先向用户指出并确认」——本裁决已通过一次明确提问确认，
> 用户选择「确认不留 VB 代码，全部改为 C#」，并以「我的意思等重写完成后再发布 dev.7」补充了发布时机。

---

## 1. 目标

1. `pcl-next` 由 VB 工程（`.vbproj`）整体切换为 C# 工程（`.csproj`）：**仓库内 `*.vb` 文件数为 0**，
   `pcl-next.sln` / CI / 构建脚本 / 文档中的 `.vbproj` 引用全部改为 `.csproj`。
2. 保留全部 63 个 `.xaml`、`Resources.resx`、`app.manifest`、图标与资源；WPF 入口、页面与控件
   code-behind 全部为 C#，`x:Class` 与 C# 类名 / 命名空间严格对应。
3. **行为等价**：启动、安装、下载、资源搜索、实例管理、游戏启动、设置、联机、音乐、皮肤等
   与 dev.6（`c171287` / `v2.100.0.6-dev.6-Windows`）一致；不改缓存格式、配置键名、注册表口径、
   合规开关默认值、`DownloadPolicy.ini` 键名。
4. **可验证标准**：
   - `find . -name '*.vb' -not -path '*/obj/*' | wc -l` = **0**；
   - `PCL.Download.Tests` 568 例全绿（转换不触碰内核，回归守门）；
   - 项目级带 RID 构建 `dotnet build pcl-next/pcl-next.csproj -c Release -r win-x64 --self-contained -p:PclChannel=Dev` **0 错误**；
   - `pclcli selftest --suite all` 通过；六类加载器逐类安装由后补脚本覆盖；
   - 真机启动、安装页 / 修改页 / 资源详情页 / 实例 Mod 页可正常打开，`PCL/Log1.txt` 内核日志可见。
5. dev.7 发布说明以「C# 重写全部完成」结案，并按《公布标准》§3 的 7 项门槛验收后打标签。

---

## 2. 前置条件与阻塞项

| 依赖 | 状态 | 备注 |
|---|---|---|
| 基线回归（AGENTS §1.3） | ✅ 本轮实跑 | 568/568；`pcl-next.vbproj` 项目级构建 0 错误（2427 条既有 CA1416 等警告） |
| 口径裁决 | ✅ | 见 §0 |
| VB→C# 转换工具链 | ✅ 已安装 | `icsharpcode.codeconverter.codeconv` 10.0.1.923（全局工具，命令 `codeconv`），支持 solution 级转换、`--include` 限定工程、`-b` best-effort |
| 隔离工作区 | ✅ 已建 | `D:\bing\MC\pcl_x\.vb2cs-trial`（`git worktree add --detach`，基于 `c171287`），主工作树与并行会话改动零影响 |
| 转换产物能否编译 | ⬜ **待评估** | 取决于 `Handles` / `WithEvents` / `Module` / `MyCompat` / `OptionStrict Off` 隐式转换 / XAML `x:Class` 的处理量，见 §3.3 |
| 六类加载器真机冒烟 | ⬜ 欠账 | dev.4 欠至 dev.6；本轮由后补 CLI 脚本清偿，不再作为人工前置 |
| 上游授权书面答复 | — | 合规门槛不变：不扩大分发、不打平台 tag、不分发非 Windows 制品 |
| 版本常量 | — | dev.7 发布时由 `Set-VersionInfo.ps1` 写入，功能提交一律不改 `VersionBaseName` / `VersionCode` |

---

## 3. 现状分析

### 3.1 量化基线（2026-11-08 只读核查）

| 项 | 值 |
|---|---|
| `pcl-next` 下 `.vb` | **123 个 / 43,783 行**（含空行） |
| `.xaml` | **63 个** |
| 目录分布（前几位） | `Controls` 28、`Modules/Base` 13、`Pages/PageLaunch` 11、`Pages/PageDownload/Resource` 8、`Pages/PageSetup` 7、`Pages/PageInstance` 7、`Modules/Resource` 7、`Modules/Minecraft` 7、`Modules` 6 |
| 根目录入口 | `Application.xaml.vb`（约 17.7 KB，含 `--update`/`--gpu`/`--memory` 命令行分支、单例、编码注册、内核日志 sink）、`FormMain.xaml.vb`（约 96.9 KB）、`GlobalSuppressions.vb` |
| 工程 | SDK 风格 `pcl-next.vbproj`，`net10.0-windows` + `UseWPF` + `UseWindowsForms`，`RootNamespace=PCL`，`AssemblyName=pcl-next`，`MyType=Empty`，`OptionStrict=Off`，`GenerateAssemblyInfo=false` |
| 工程级 `Import` | `MeloongCore` / `MeloongCore.Extensions` / `MeloongCore.Wpf` / `Microsoft.VisualBasic` / `Newtonsoft.Json` / `Newtonsoft.Json.Linq` 等 |
| 显式排除 | `My Project\Settings.Designer.vb`、`My Project\MyExtensions\MyWpfExtension.vb`、`Controls\MySliderDot.xaml.vb`（死代码）、`Resources\Custom.xaml`（外观模板） |
| 编译期生成 | `$(_PclBuildInfoFile)` → `obj\PclBuildInfo.g.vb`，由 `Get-BuildSmallVersion.ps1 -OutFile` 生成 |
| 其余 C# 工程 | `PCL.Download`（46 文件）、`PCL.Download.Tests`（35 文件）、`PCL.Cli`、`PCLCS`、`MeloongCore*` —— 均已 C#，不受本计划影响 |

### 3.2 为什么必须「隔离工作区一次成型」

SDK 风格工程是**单语言**的：一个 `.vbproj` 只能编译 `.vb`，不能边留 VB 边掺 C#。
因此不存在「主线分批替换」的路径——任何中途状态（部分 `.vb` + 部分 `.cs` 同工程）都编译不过。
XAML code-behind 又必须与页面在**同一程序集**（`x:Class` 生成的 `InitializeComponent` 是 partial 类），
也不能拆成第二个 C# 类库逐步迁移。

结论：全量切换在隔离工作区内完成「转换 → 编译修复 → 全绿」，主线 `dev.6-Windows` 保持可发布的 VB 版本，
直到整体合入。这也与用户「重写完成后再发布 dev.7」的裁决一致。

### 3.3 转换高风险点（逐项须验证）

| # | 风险 | 影响 | 处置方向 |
|---|---|---|---|
| 1 | VB `Handles` / `WithEvents` 事件绑定 | WPF 事件不触发、页面无响应 | 转换器一般生成 `+=` 或保留构造期挂接；逐文件核对，尤其 `Application_Startup` 与 `FormMain` |
| 2 | `x:Class` 与 C# 命名空间 | 63 个 XAML 全部可能编译失败 | VB `RootNamespace=PCL` 与 `x:Class="Application"`（全局）并存；转换后核对每个 `x:Class` 与生成的 C# 全限定名，必要时批量改 XAML |
| 3 | `MyType=Empty` 下的启动入口 | WPF 找不到 `Main` | VB 依赖 `Application.xaml` 的 `ApplicationDefinition` 隐式生成入口；C# 需显式 `[STAThread] Main` 或 `<StartupObject>`，须实测 |
| 4 | `Module` / 全局成员无前缀调用 | 数千处编译错误 | 转换器生成静态类后需补类名前缀或 `using static`；按目录批量修 |
| 5 | `OptionStrict Off` 隐式转换、`CType`/`DirectCast`/`IsNothing` | 类型不匹配、`==` 与 `Equals` 语义差异 | 编译错误清单逐类修；**不得改变运行时语义**（尤其字符串比较 `OptionCompare=Binary`） |
| 6 | `Microsoft.VisualBasic` 运行时函数（`StrConv`、`Interaction`、`Conversion` 等） | 转换后找不到符号 | 保留 `Microsoft.VisualBasic` 包引用，按命名空间显式限定；`MyCompat.vb` 同样转换 |
| 7 | 工程级 `Import` → C# `using` | 全局 using 缺失 | 生成 `GlobalUsings.cs`（或 `Usings.cs`），覆盖原 `<Import Include>` 全集 |
| 8 | `AssemblyInfo.vb` / `Resources.Designer.vb` / `PclBuildInfo.g.vb` | 程序集特性与构建号丢失 | `GenerateAssemblyInfo=false` 保留手写 `AssemblyInfo.cs`；资源设计器转 C#；构建号生成脚本改输出 `.cs`（`-OutFile` 语法见脚本） |
| 9 | CA1416 等 2427 条既有警告 | 噪音掩盖真错误 | 保持 `TreatWarningsAsErrors=false` 与 `NoWarn`，只盯 error 数 |
| 10 | 工程文件引用面 | CI / 构建脚本全部失效 | 需同步改：`.sln`、`scripts/Build.ps1`、`scripts/Get-BuildSmallVersion.ps1`、`scripts/Set-VersionInfo.ps1`、`scripts/Test-VersionMatrix.ps1`、`.github/workflows/*.yml`、`Directory.Build.props`（`PclRidGuard` 条件）、`AGENTS.md`、`分支与发布规范.md`、`公布标准.md`、`ReleaseNotes/`、各计划文档 |

### 3.4 历史踩坑（沿用，本阶段仍适用）

- `pcl-next.sln` 不含测试工程，且带 RID 护栏时 sln 级构建不可用（不带 `-r` 撞 `PclRidGuard`，带 `-r` 触发 `NETSDK1134`）→ 一律项目级带 `-r`。
- 提交信息末尾必须 `Build: <M.mm>`；钩子未安装时手工补，编号由 `scripts/build-number.base` 的 root+base 口径推导（2026-11-08 ⑧ 已修 CI/本地一致性）。
- 工作树存在**并行会话未提交改动**：一律 `git commit --only` 隔离，禁止 `git add -A`、禁止碰他人文件。
- 产物落点：测试版 `pcl-next/bin-net10/<代号>/<版本>-<构建号>/<RID>/`；发布版 `ReleaseArtifacts/<代号>/<版本>/<RID>/`（不带构建号）。
- 不要擅自 `git push fork dev`；推送时机由用户决定。

---

## 4. 改动文件清单

### 4.1 转换产物（隔离工作区内一次性生成）

| 文件 | 改动 |
|---|---|
| `pcl-next/pcl-next.csproj`（新） | 由 `.vbproj` 转换；保留 `net10.0-windows`、`UseWPF`、`UseWindowsForms`、`AssemblyName=pcl-next`、`OutputPath=$(PclGuiTreeRoot)`、`PclUseTreePath`、`PclChannel` 号段常量、`NoWarn`、`PclBuildInfo` 目标，语言项改 C# |
| `pcl-next/**/*.cs`（约 123 个，新） | 全部 `.vb` 的等价 C# 转换产物；按原目录同名放置（`.xaml.vb` → `.xaml.cs`） |
| `pcl-next/GlobalUsings.cs`（新） | 工程级 `using`，覆盖原 `<Import Include>` 全集 |
| `pcl-next/My Project/AssemblyInfo.cs`（新） | 替代 `AssemblyInfo.vb` |
| `pcl-next/Application.xaml.cs` + `App` 入口 | 显式 WPF `Main` / `Startup` 挂接，保留 `--update`/`--gpu`/`--memory` 分支 |
| `pcl-next/**/*.xaml`（63 个，按需改） | `x:Class` 对齐 C# 全限定名；事件属性按需改为代码挂接 |
| `scripts/Get-BuildSmallVersion.ps1` | `-OutFile` 生成 `.cs` 源文件（VB 语法 → C# 语法），或改由 SDK 生成程序集特性 |

### 4.2 工程引用面同步（合入主线时一并提交）

- `pcl-next.sln`、`scripts/Build.ps1`、`scripts/Set-VersionInfo.ps1`、`scripts/Test-VersionMatrix.ps1`
- `.github/workflows/build.yml` / `release.yml` / `kernel-guard.yml`（`dotnet build/publish` 路径与 `-r` 参数保持）
- `Directory.Build.props`（`PclRidGuard` / 路径护栏对 C# 工程的求值不变，核对 `PclIsGuiFree` 不受影响）
- `AGENTS.md`、`分支与发布规范.md`、`公布标准.md`（模块地图与构建命令）
- `ReleaseNotes/v2.100.0.7-dev.7-Windows.md`（六章，发布阶段写）
- `docs/交接-Linux端-dev7-全量CSharp重写.md`（Linux / Android 移植清单，合入后写）

### 4.3 明确不做（边界）

- ❌ 不改任何功能行为、UI 布局、动画、颜色、文案；转换是**语言等价变换**，不是重构。
- ❌ 不改缓存格式（安装清单键名、`Cache\LocalMod.json`、`DownloadPolicy.ini`）、配置 / 注册表隔离口径。
- ❌ 不改合规开关默认值、不把「恢复 MC 下载」写进成果、不扩大分发范围。
- ❌ 不改 L1–L5 义务（对外名 PCL 前缀、第三方修改版声明、龙腾猫跃首位署名 + 赞助链接、`McLaunchPrecheck` 劝导、蓝色主色且不引入赞助解锁）。
- ❌ 不动 `PCL.Download` / `PCL.Download.Tests` 的业务逻辑（转换不动内核，568 例是回归基线）。
- ❌ 不批量清理 2427 条既有警告、不顺手「修好」修改安装的两处比较规则。
- ❌ 不动 Linux / Android 两份仓库、不改工作区根只读备份。
- ❌ 不提前打 dev.7 标签、不改版本常量；发布由用户确认全绿后触发。

---

## 5. 改动要点

1. **一次切换，不做双实现**：全量转换在隔离 worktree 完成，主线只在 `0 错误 + 568 例全绿 + 真机可启动` 后整体合入；
   合入提交即同时删除全部 `.vb` 与 `.vbproj`，仓库内 VB 文件数归零。
2. **XAML 不动布局，只对齐类型名**：`x:Class` 必须与生成的 C# `namespace + class` 一致；
   `InitializeComponent()` 由 XAML 编译生成的 partial 类提供，C# 侧类名 / `partial` 修饰缺一不可。
3. **入口显式化**：VB 的 `Application.xaml` + `EnableDefaultApplicationDefinition` 隐式生成入口，
   C# 侧须显式 `[STAThread] static void Main()` 或指定 `StartupObject`，并保留单例互斥、编码注册、
   `MeloongCore.Main.Init("PCL")`、`PCL.Download.DownloadLog` sink 注入与三个命令行分支。
4. **全局符号**：原 VB `Module` 成员无前缀可调用、工程级 `Import` 全局可见；C# 用 `GlobalUsings.cs`
   与 `using static` 复刻，避免给 4 万行逐处加前缀。
5. **语义保真**：`OptionCompare=Binary` → 字符串比较必须区分大小写；`OptionStrict=Off` 的隐式转换
   要保留运行时行为（尤其 `Nothing`、`CType`、数值 / 字符串拼接），不得借转换「顺手现代化」。
6. **构建号链路**：`-OutFile` 生成的源文件从 VB 语法改为 C# 语法（`<Assembly: ...>` → `[assembly: ...]`、
   `Friend Const` → `internal const`），否则项目级构建必炸；`AssemblyInformationalVersion` 内容不变。
7. **引用面零遗漏**：`.vbproj` 字符串在全仓库的命中清单见 §3.3 第 10 条，合入前用
   `grep -rn 'pcl-next\.vbproj'` 复核为 0 命中。
8. **CI 一致性**：`build.yml` / `release.yml` 的 publish 与 `PCL.Cli` build 已显式带 `-r win-x64`
   （2026-11-08 ⑦），改工程扩展名时不得把该参数弄丢。

---

## 6. 回归影响面

| 场景 | 风险 | 保障 |
|---|---|---|
| 启动 / 单例 / 命令行分支 | `Main` 或 `Startup` 事件缺失 → 启动无窗口或 `--update` 失效 | 真机启动 + 三个参数分别跑一次 |
| 63 个 XAML 页面 | `x:Class` / `InitializeComponent` 不匹配 → 整页编译失败或运行时空引用 | 项目级构建 0 错误 + 逐页打开冒烟 |
| 事件与进度 | `Handles` 转换遗漏 → 按钮无响应、进度不刷新 | 安装页 / 下载页 / 设置页逐页点一遍 |
| 下载与安装 | 桥接层（`NetInterop` / `LoaderPlanAdapter`）在 C# 中的事件挂接变化 | `PCL.Download.Tests` 568 例 + `pclcli selftest --suite all` + 后补六类加载器脚本 |
| 资源 / 本地 Mod | `LocalResourceFile` setter 触发 UI 刷新、`PrimeHashes` 灌入路径 | 实例 Mod 页首次打开速度 + 更新检查 |
| 编码与字体 | `CodePagesEncodingProvider` 注册、`FormattedText` 字体修复丢失 | 中文路径 / 整合包导出冒烟 |
| 缓存兼容 | 新旧程序互读缓存 | 用 dev.6 用户目录启动 dev.7，确认不弹降级、缓存不重建 |
| 日志 | `DownloadLog` sink 未挂 → `Log1.txt` 看不到内核日志 | 启动后检查 `PCL/Log1.txt` |

**需新增 / 修改的测试**：内核与测试工程不改；新增 `scripts/` 下的转换后验收脚本（CLI/GUI 分离路线，
覆盖六类加载器逐类安装与页面等价性，按用户裁决「到时候补脚本」在转换完成后落地）。

---

## 7. 验证步骤

> 全部命令须符合 `AGENTS.md` §零；入口工程一律显式带真实 RID。

```powershell
# 1. 内核回归（转换不碰内核，必须仍全绿）
dotnet test PCL.Download.Tests/PCL.Download.Tests.csproj -c Release

# 2. 转换产物编译（隔离工作区内，允许保留既有警告，只看 error）
dotnet build pcl-next/pcl-next.csproj -c Release -r win-x64 --self-contained -p:PclChannel=Dev

# 3. VB 归零复核（仓库根执行，排除 obj/bin）
find . -name '*.vb' -not -path '*/obj/*' -not -path '*/bin/*' | wc -l   # 必须为 0
grep -rn 'pcl-next\.vbproj' --include='*.ps1' --include='*.yml' --include='*.sln' .  # 必须 0 命中

# 4. CLI 自检 + 后补验收脚本
pclcli selftest --suite all
# 六类加载器逐类安装脚本（dev.7 收尾补）

# 5. 真机：启动、安装页 / 修改页 / 资源详情页 / 实例 Mod 页、日志、缓存兼容
```

**判定标准**：568/568；项目级构建 0 错误；`*.vb` = 0；真机可启动且功能与 dev.6 等价；
七项公布门槛全过后，才允许打 `v2.100.0.7-dev.7-Windows` 并宣布「C# 重写全部完成」。

---

## 8. 回滚方案

1. **全绿之前**：所有转换只在隔离 worktree，主线 `dev.6-Windows` / `c171287` 保持可发布；
   放弃转换只需 `git worktree remove --force`，主线零改动。
2. **合入之后**：整体合入是一个（或一组连续）提交，`git revert` 即可取回全部 `.vb` 与 `.vbproj`；
   历史中保留完整 VB 源码，回滚不丢代码。
3. **保留不变**：缓存格式、配置 / 注册表键名、`DownloadPolicy.ini`、`LoaderBase` 语义、
   合规文本与署名；回滚后新旧程序互读缓存均不炸。
4. **发布后**：dev.7 标签与 Release 属已发布节点，按《分支与发布规范》§6.1 不对已发布节点
   `reset --hard` / force-push；需修复则走 dev.8。

---

## 9. 合规边界

- 本阶段是**基于 PCL2 的第三方修改版**自有分支内的语言等价变换，属「修改」而非新增复制，
  不新增许可义务；仍须满足 L1–L5：对外名带 PCL 前缀（L2）、声明第三方修改版与龙腾猫跃首位署名 +
  赞助链接（L1/L3）、保留 `McLaunchPrecheck` 正版与赞助劝导（L4）、Windows 主色相保持蓝色且不引入
  赞助解锁式功能（L5）。
- 转换产物不得移除 `LICENCE`、关于页署名、劝导逻辑与蓝色主色约定中的任何一项。
- MC 下载恢复是合规决策不是代码开关：`ModDownloadPolicy` 默认值与 `PCL_ALLOW_MC_DOWNLOAD` 顺序不变，
  不把「恢复」写进发布成果。
- 分发范围不扩大：上游书面答复并留存前，`PLATFORM_RELEASE_ENABLED` 保持关闭、不打平台 tag、
  不分发任何非 Windows 制品；本计划只涉及 Windows 线源码。

---

## 10. 排期与阶段记录（追加，不覆盖历史）

```
[本轮] 口径裁决 + 专项计划 + 基线回归 + 转换工具链试跑
   ↓
[阶段 A] 隔离工作区全量转换 → 编译错误清单 → 逐类修复至 0 错误
   ↓
[阶段 B] XAML / 入口 / 全局符号 / 构建号链路专项修复 → 568 例 + CLI selftest + 真机可启动
   ↓
[阶段 C] 整体合入主线（*.vb 归零）→ 引用面同步 → 补 CLI/GUI 分离验收脚本
   ↓
[阶段 D] 七项门槛验收 → 打 v2.100.0.7-dev.7-Windows → 发布说明「C# 重写全部完成」
```

- 阶段 A 的内部批次按**错误类型 / 目录**划分，但都发生在隔离工作区同一棵树上，
  **不向主线分批合入**（§3.2）。
- 每轮完成按 `AGENTS.md` §五提交（仅本轮文件、`Build:` 行、conventional 前缀），
  推送时机由用户决定。

### 阶段记录

- **2026-11-08 16:05**：用户裁决全量 C#、重写完成后再发 dev.7、验收脚本后补；
  本计划立项；基线 568/568 + 项目构建 0 错误；`codeconv` 10.0.1.923 安装成功；
  隔离 worktree `.vb2cs-trial` 建立，整工程试转换进行中。
