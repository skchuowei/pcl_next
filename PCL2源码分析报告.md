# PCL2 启动器源码分析报告

> 分析对象：`PCL2` (Plain Craft Launcher 2)
> 版本：**2.99.0.1 正式版**
> Commit：`93ede5d9e638c99fcec853a9ba1eecd91ceee7f5`（2026-10-25）
> 上游：`github.com/Hex-Dragon/PCL2`（已迁移至 `Meloong-Git/PCL`）
> 本地路径：`D:\bing\MC\pcl_x\PCL2`

---

## 一、项目总览

### 1.1 技术栈

| 项 | 内容 |
|---|---|
| 语言 | **VB.NET**（主工程）+ **C#**（PCLCS 辅助库） |
| UI 框架 | **WPF**，目标框架 **.NET Framework 4.8** |
| 三大工程 | `Plain Craft Launcher 2`（VB, WinExe）、`PCLCS`（C#）、`MeloongCore`（C#, submodule） |
| 解决方案配置 | Debug / Release / Snapshot（三种构建） |
| 代码规模 | **113 个 .vb 文件 / 43,128 行** + 9 个 .cs 文件 |
| 第三方依赖 | CacheCow.Client（HTTP 磁盘缓存）、Newtonsoft.Json 13.0、NAudio 1.7.3、Imazen.WebP、Ookii.Dialogs.Wpf、ThrottleDebounce |

### 1.2 目录结构

```
PCL2/
├── Plain Craft Launcher 2/          ← VB 主工程
│   ├── Modules/                     ← 业务模块（核心）
│   │   ├── Base/                    ← ModBase / ModAnimation / ModNet / ModLoader / ModValidate
│   │   ├── Minecraft/               ← ModLaunch / ModMinecraft / ModCrash / ModJava / ModWatcher / ModModpack / ModDownload
│   │   ├── Resource/                ← ResourceProject / ResourceSearcher / ResourceVersion / LocalResourceLoaders
│   │   ├── ModMain / ModSecret / ModEvent / ModMusic / ModDevelop
│   ├── Controls/                    ← ~30 个自绘控件
│   ├── Pages/                       ← 12 个页面（启动/下载/联机/设置/更多）
│   ├── Resources/                   ← 内嵌 DLL、字体、图片
│   ├── FormMain.xaml(.vb)           ← 主窗体（1735 行）
│   └── Application.xaml(.vb)        ← 全局样式与启动入口
├── PCLCS/                           ← C# 辅助库（Java 检测 / 常量 / 工具）
├── MeloongCore/                     ← ⛔ submodule 未初始化（空目录）
├── patches/                         ← 各版本 release 补丁（15 个）
└── 最新正式版.zip                    ← 编译好的 exe（6.0 MB）
```

### 1.3 顶层文件行数 Top 20

| 行数 | 文件 | 职责 |
|---|---|---|
| 2221 | `Modules/Minecraft/ModLaunch.vb` | 启动参数拼装与进程拉起 |
| 2176 | `Pages/PageDownload/ModDownloadLib.vb` | 下载/安装业务流水线 |
| 2153 | `Modules/Minecraft/ModMinecraft.vb` | 版本模型、JSON 继承合并、库/资产 |
| 1933 | `Modules/Base/ModNet.vb` | HTTP + 多线程分片下载引擎 |
| 1735 | `FormMain.xaml.vb` | 主窗体、双栏导航、页面栈 |
| 1388 | `Modules/Minecraft/ModDownload.vb` | 版本清单、镜像源、各加载器列表 |
| 1216 | `Modules/Base/ModBase.vb` | 全局工具、MyColor、线程调度 |
| 1199 | `Pages/PageDownload/PageDownloadInstall.xaml.vb` | 下载页 UI 逻辑 |
| 1122 | `Modules/Minecraft/ModCrash.vb` | 崩溃分析引擎 |
| 1116 | `Pages/PageLink/PageLinkMain.xaml.vb` | 联机大厅 |
| 1083 | `Modules/ModMain.vb` | 提示条、弹窗、帮助、主时钟 |
| 1066 | `Modules/Base/ModAnimation.vb` | 动画引擎 |

---

## 二、核心架构

```
┌──────────────────────────────────────────────────────────────┐
│  MeloongCore (C# submodule — 本地源码缺失)                    │
│  FileLogger 日志后端 / PathUtils / StringUtils / WPF 转换器   │
└──────────────────────────┬───────────────────────────────────┘
                           ▼
┌──────────────────────────────────────────────────────────────┐
│  PCL2 主工程 (VB.NET / WPF)                                  │
│                                                              │
│  ModBase ──── ModAnimation ──── ModEvent ──── ModValidate   │
│  ModNet  ──── ModLoader ─────── ModSecret ──── Settings      │
│                                                              │
│  ModMinecraft ── ModLaunch ── ModCrash ── ModJava           │
│        │           │                          ModWatcher      │
│        │           └──────────────► ModModpack               │
│        └── ModDownload ── ModDownloadLib ── Resource/*       │
└──────────────────────────┬───────────────────────────────────┘
                           ▼
┌──────────────────────────────────────────────────────────────┐
│  Controls/ (~30 个自绘控件)  │  FormMain  │  Pages/ (12)     │
└──────────────────────────────────────────────────────────────┘
```

### 2.1 分层职责

| 层 | 模块 | 性质 |
|---|---|---|
| **数据/模型层** | ModMinecraft | 只读：版本 JSON、继承合并、库与资产清单 |
| **业务层** | ModDownload / ModDownloadLib / Resource | 清单拉取、安装流水线、站点搜索 |
| **执行层** | ModLaunch / ModJava / ModWatcher | 参数拼装、拉进程、监控 |
| **基础设施层** | ModBase / ModNet / ModLoader / ModAnimation | 工具、网络、任务调度、动画 |
| **表现层** | Controls / Pages / FormMain | 自绘控件、页面、导航 |

---

## 三、启动流程深度解析

### 3.1 完整调用链

```
[UI] PageLaunchLeft.LaunchButtonClick()              PageLaunchLeft.xaml.vb:611
  └─ ModLaunch.McLaunchStart(Options)                 ModLaunch.vb:31    ← 公开入口（须在 UI 线程）
      └─ McLaunchLoader.Start(...)                    ModLaunch.vb:68
          └─ McLaunchStart(Loader)  (Private)         ModLaunch.vb:106   ← 主流程
              ├─ McLaunchPrecheck()                   ModLaunch.vb:234   ← 预检
              └─ 构造 Loaders 列表                    ModLaunch.vb:122-133
                   └─ LaunchLoader.Start()            ModLaunch.vb:151
```

### 3.2 LoaderCombo 流水线骨架

启动流程完全由 **Loader 组合**驱动，`Block` 属性决定串行/并行，`ProgressWeight` 决定进度条占比：

```vb
' ModLaunch.vb:122-133
Dim Loaders As New List(Of LoaderBase) From {
    New LoaderTask(Of Integer, Integer)("获取 Java", AddressOf McLaunchJava) With {.ProgressWeight = 4, .Block = False},
    McLoginLoader, '.ProgressWeight = 15, .Block = False
    New LoaderCombo(Of String)("补全文件", DlClientFix(McInstanceSelected, False, True)) With {.ProgressWeight = 15, .Show = False},
    New LoaderTask(Of String, Integer)("获取启动参数", AddressOf McLaunchArgumentMain) With {.ProgressWeight = 2},
    New LoaderTask(Of Integer, Integer)("解压文件", AddressOf McLaunchNatives) With {.ProgressWeight = 2},
    New LoaderTask(Of Integer, Integer)("预启动处理", AddressOf McLaunchPrerun) With {.ProgressWeight = 1},
    New LoaderTask(Of Integer, Integer)("执行自定义命令", AddressOf McLaunchCustom) With {.ProgressWeight = 1},
    New LoaderTask(Of Integer, Process)("启动进程", AddressOf McLaunchRun) With {.ProgressWeight = 2},
    New LoaderTask(Of Process, Integer)("等待游戏窗口出现", AddressOf McLaunchWait) With {.ProgressWeight = 1},
    New LoaderTask(Of Integer, Integer)("结束处理", AddressOf McLaunchEnd) With {.ProgressWeight = 1}
}
```

**关键点**：「获取 Java」「登录」「补全文件」三项 `Block = False` → **并行执行**；其余严格串行。进度按 `ProgressWeight` 加权计算（`ModLoader.vb:470-484`）。

### 3.3 进程拉起（`McLaunchRun`，ModLaunch.vb:2093-2141）

```vb
' ModLaunch.vb:2096-2117
Dim StartInfo As New ProcessStartInfo(McLaunchJavaSelected.JavaExePath) '使用 javaw.exe 会导致 #6263
StartInfo.EnvironmentVariables("appdata") = PathUtils.ToShortPath(McFolderSelected)
StartInfo.WorkingDirectory = McInstanceSelected.PathIndie
StartInfo.UseShellExecute = False
StartInfo.RedirectStandardOutput = True
StartInfo.RedirectStandardError = True
StartInfo.CreateNoWindow = True
StartInfo.Arguments = McLaunchArgument          ' ← 完整命令行拼成单一字符串
Dim GameProcess = StartProcess(StartInfo)
```

stdout/stderr 重定向后由 **ModWatcher** 接管读取 —— 崩溃检测完全依赖这条链路。

---

## 四、JVM 与游戏参数拼装

### 4.1 三段式结构（`McLaunchArgumentMain`，ModLaunch.vb:1191-1218）

```
JVM 参数  →  mainClass  →  游戏参数
```

拼装完成后统一做**占位符替换**，再统一做**引号转义**：

```vb
' ModLaunch.vb:1200-1214
For Each Arg In Arguments
    For Each Replacement In Replacements
        Arg = Arg.Replace(Replacement.Key, Replacement.Value)
    Next
    If Arg.IndexOfAny({"&"c, "|"c, "<"c, ">"c, "^"c, " "c}) >= 0 AndAlso Not Arg.StartsWithF("""") Then
        Arg = $"""{Arg.Replace("""", "\""")}"""
    End If
    Argument &= Arg & " "
Next
```

> **设计要点**：替换在所有分段拼接完成之后才做，路径中的特殊字符不会干扰参数分割。

### 4.2 JVM 参数组成（`McLaunchArgumentsJVM`，ModLaunch.vb:1221-1402）

| 分段 | 内容 | 行号 |
|---|---|---|
| 版本 JSON | 从 `arguments.jvm` 读取，支持 `rules` 过滤 + **递归继承父版本** | 1225-1254 |
| 旧版兜底 | `-XX:HeapDumpPath` / `-Djava.library.path` / `-cp ${classpath}` | 1255-1261 |
| 统一通行证 | `-javaagent:nide8auth.jar=...` | 1272-1274 |
| Authlib-Injector | `-javaagent:authlib-injector.jar=...` + `-Dauthlibinjector.prefetched=` | 1275-1284 |
| **JLW** | `-Doolloo.jlw.tmpdir=` + 末尾 `-jar JavaWrapper.jar` | 1290-1304, 1396-1398 |
| LUA | LWJGL 3.4.1 的 Unsafe Agent | 1308-1311 |
| 内存 | `-Xmx{m}m` | 1317-1318 |
| GC | ZGC / ZGenerational / G1GC 按 Java 版本与设置智能选择 | 1339-1365 |
| Log4j | `-Dlog4j2.formatMsgNoLookups=true` | 1373 |
| 编码 | `file.encoding` / `stdout.encoding` / `stderr.encoding`，Java 19+ 改名 | 1376-1393 |

**JLW 的作用**是修复 JDK-8272352 命令行参数乱码。若用户自定义参数含 `-javaagent`，会自动禁用 JLW 并告警（ModLaunch.vb:1292-1304）。

**GC 选择策略**（`SetupType`）：

| SetupType | Java 21+ | Java 20~15 | Java 14- |
|---|---|---|---|
| 0 | 分代 ZGC | 非分代 ZGC | G1GC |
| 1 | 分代 ZGC | G1GC | G1GC |
| 2 | G1GC | G1GC | G1GC |
| 3 | 不指定（玩家自定义） | 同 | 同 |
| 4 | 优化 G1GC（额外参数） | 同 | 同 |

### 4.3 占位符替换表（`McLaunchArgumentsReplace`，ModLaunch.vb:1506-1579）

| 占位符 | 值 | 行号 |
|---|---|---|
| `${classpath}` | 支持库列表（`;` 分隔） | 1578 |
| `${natives_directory}` | natives 解压目录 | 1509 |
| `${library_directory}` | `libraries` 目录 | 1510 |
| `${pure_directory}` | PCL 自身目录 | 1512 |
| `${auth_player_name}` / `${auth_uuid}` / `${auth_access_token}` | 登录信息 | 1519-1521 |
| `${version_name}` | 版本名 | 1515 |
| `${game_directory}` | 实例目录（去末段） | 1516 |
| `${assets_root}` / `${assets_index_name}` | 资源路径与索引名 | 1517, 1527 |
| `${resolution_width}` / `${resolution_height}` | 窗口尺寸 | 1563-1564 |
| `${primary_jar}` / `${game_assets}` | 主 jar / legacy 资源 | 1525-1526 |

**classpath 组装的一个细节**（ModLaunch.vb:1567-1578）：OptiFine 的 jar **总是插入倒数第二位**，这是 MC 加载顺序的要求。

```vb
If OptiFineCp IsNot Nothing Then Cps.Insert(Cps.Count - 2, OptiFineCp)
```

### 4.4 参数去重（`DeduplicateJavaArguments`，ModLaunch.vb:1617-1655）

这是一个**安全关键**逻辑：游戏参数中出现重复的 `--width` / `--uuid` 会让 MC 直接失效甚至崩溃。

```vb
' ModLaunch.vb:1638-1643
'据测试，如果包含多个相同 --width 或 --uuid 等参数，两个都会直接失效
If Not IsJVMArgs AndAlso Key <> "--tweakClass" Then
    Result(j + 1) = Value    ' 游戏参数：新值覆盖旧值
    Continue While
End If
```

- **游戏参数**：同名键 → 新值覆盖旧值
- **JVM 参数**：键值完全相同 → 直接丢弃

---

## 五、崩溃分析引擎（ModCrash.vb）

### 5.1 四步流程

```
Collect()  →  Prepare()  →  Analyze()  →  Output()
:21            :139         :391         :861
```

`CrashAnalyzer` 是**一个类**而非 Module，定义在 ModCrash.vb:1。

### 5.2 数据源收集

| 数据源 | 用途 |
|---|---|
| `crash-reports\*.txt` | MC 崩溃报告 |
| `hs_err_pid*.log` | **JVM 崩溃日志** |
| `logs\latest.log` | MC 主日志 |
| `logs\debug.log` | MC Debug 日志 |
| 进程 stdout/stderr（500 行） | `RawOutput.log` |
| `PCL\Log1.txt` | 启动器自身日志 |

时间窗过滤：只取 **3 分钟内**修改过的文件（ModCrash.vb:57-61）。

### 5.3 三级分析优先级

| 优先级 | 方法 | 行号 | 内容 |
|---|---|---|---|
| 1 | `AnalyzeCrit1` | 474-584 | ~45 条精确字符串匹配规则 |
| 1.5 | `AnalyzeCrit2` | 589-645 | Mixin 失败、Forge/Fabric 报错 |
| 2 | 堆栈分析 | 402-433 | 正则提取 Mod ID + 黑名单过滤 |
| 3 | `AnalyzeCrit3` | 649-670 | 极短输出、Mod 初始化失败 |

共 **47 种** `CrashReason` 枚举（ModCrash.vb:340-387）。

### 5.4 ⭐ JVM 崩溃（EXCEPTION_ACCESS_VIOLATION）的精准归因

这是整个崩溃分析最有价值的部分（ModCrash.vb:552-561）：

```vb
' ModCrash.vb:552-561
'虚拟机日志分析
If LogHs IsNot Nothing Then
    If LogHs.Contains("The system is out of physical RAM or swap space") Then AppendReason(CrashReason.内存不足)
    If LogHs.Contains("Out of Memory Error") Then AppendReason(CrashReason.内存不足)
    If LogHs.Contains("EXCEPTION_ACCESS_VIOLATION") Then
        If LogHs.Contains("# C  [ig") Then AppendReason(CrashReason.Intel驱动不兼容导致EXCEPTION_ACCESS_VIOLATION)
        If LogHs.Contains("# C  [atio") Then AppendReason(CrashReason.AMD驱动不兼容导致EXCEPTION_ACCESS_VIOLATION)
        If LogHs.Contains("# C  [nvoglv") Then AppendReason(CrashReason.Nvidia驱动不兼容导致EXCEPTION_ACCESS_VIOLATION)
    End If
End If
```

**原理**：JVM 崩溃日志 `hs_err_pid*.log` 中的 `# C  [框架名]` 行标示崩溃时正在执行的本机代码所属图形库：

| 匹配 | 含义 |
|---|---|
| `# C  [ig...` | Intel 显卡驱动（igdxx64 / igvk64） |
| `# C  [atio...` | AMD 显卡驱动（atioglxx64） |
| `# C  [nvoglv...` | NVIDIA 驱动（nvoglv64） |

### 5.5 隐私保护

导出的错误报告中，用户名与 AccessToken 会被脱敏：

```vb
' ModCrash.vb:907
FileContent = FilterUserName(FilterAccessToken(FileContent, If(FileName = "启动脚本.bat", "F", "*")), "*")
```

---

## 六、Java 环境管理

### 6.1 Java 版本约束的「求交」模型

`GetJavaRequirement`（ModJava.vb:128-273）从多个来源收集 `ValueRange` 约束并求交：

| 来源 | 约束 |
|---|---|
| 原版 1.20.5+ | ≥ Java 21 |
| 原版 1.18+ | ≥ Java 17 |
| 原版 1.17+ | ≥ Java 16 |
| 原版 1.12+ | ≥ Java 8 |
| 原版 1.5.2- | < Java 9 |
| Mojang `javaVersion.majorVersion` | ≥ 指定版本（Java 22+ 时生效） |
| OptiFine 1.8-1.11 | **恰好 Java 8** |
| Forge 各版本段 | 差异极大（如 1.6.1-1.7.2 → 恰好 7；1.13-1.14 → 8-10） |
| Fabric | 1.18+ → ≥17；Loader <0.17.0 → <25（Mixin/ASM 不兼容） |
| NeoForge 1.20.1/1.20.2 | < Java 22 |
| 统一通行证 | ≥ Java 8u101 |

**冲突处理**（ModJava.vb:143-150）：约束冲突时**不报错**，而是回退到新约束并 Toast 警告：

```vb
Dim AddConstraint = Sub(Constraint As ValueRange(Of Version))
    JavaRange = JavaRange?.Intersect(Constraint)
    If JavaRange.IsEmpty Then
        Logger.Warn($"Java 版本要求冲突，若启动失败请在版本设置手动指定 Java：当前追加的要求为 {Constraint}", LogBehavior.Toast)
        JavaRange = Constraint
    End If
End Sub
```

### 6.2 Java 搜索

PCL2 扫描 **17 个位置**（`PCLCS/Java.cs:136-168`），包括 `%AppData%\.minecraft\runtime\`、`%AppData%\.hmcl\java\`、PrismLauncher、CurseForge、SDKMAN、Adoptium、Corretto、Zulu、`$JAVA_HOME` 等。

筛选规则（Java.cs:194-248）：
- 排除含**重解析点**（符号链接）的路径
- 排除 `java8path_target_`、`javapath_target_`、`javatmp`、`system32`
- PCL 自己的下载路径不筛查

排序策略：按 `|Version.Major - 21|` 排序 —— **主版本号越接近 21 越靠前**。

### 6.3 独立检测逻辑

`Java.CheckAsync`（Java.cs:34-77）通过运行 `java -XshowSettings:properties -version` 获取版本号，并检测：
- 32 位 Java（无 `64-bit` → 拒绝）
- 无法运行的 Java（`a fatal error` / `error: `）
- `file.encoding` 与 `native.encoding`（供启动参数编码处理使用）

---

## 七、下载与资源模块

### 7.1 四层结构

| 层 | 文件 | 职责 |
|---|---|---|
| L1 网络引擎 | `ModNet.vb` (1933) | HTTP、多线程分片、断点续传、校验、限速、源切换 |
| L2 任务框架 | `ModLoader.vb` (791) | Loader 抽象、串/并行编排、进度、取消 |
| L3 业务编排 | `ModDownload.vb` (1388) + `ModDownloadLib.vb` (2176) | 版本清单、镜像源、安装流水线 |
| L4 资源站点 | `Modules/Resource/*` | CurseForge / Modrinth 搜索与详情 |

### 7.2 双 HttpClient 分离（ModNet.vb）

```vb
' ModNet.vb:222-230  —— 缓存客户端（CacheCow 磁盘缓存）
RequestClient = CacheCow.Client.ClientExtensions.CreateClient(
    New CacheCow.Client.FileCacheStore.FileStore(PathTemp & "Cache/Http/"),
    New HttpClientHandler With {.AutomaticDecompression = ..., .UseCookies = False})

' ModNet.vb:304-306  —— 下载客户端（流式，不走缓存）
Private ThreadClient As New HttpClient(New HttpClientHandler With {...})
```

### 7.3 GFW 截断防护

针对国内网络环境的专门设计（ModNet.vb:23-31）：

```vb
'检查结果是否为完整 JSON（在 #6683 中可能返回空，在被 GFW 截断时可能不完整）
If RequireJson Then
    Result = Result.Trim((vbCrLf & " ").ToCharArray)
    If Not (Result.StartsWithF("{") AndAlso Result.EndsWithF("}")) AndAlso
       Not (Result.StartsWithF("[") AndAlso Result.EndsWithF("]")) Then
        Throw New FormatException("返回结果并非 JSON 格式：" & ...)
    End If
End If
```

### 7.4 多线程分片下载

**三类数据结构**：

| 类 | 行号 | 作用 |
|---|---|---|
| `NetSource` | 356 | 一个下载源（URL + 失败计数） |
| `NetThread` | 428 | 一个下载线程（对应 HTTP Range 一段） |
| `NetFile` | 579 | 一个文件（持有 Sources + Threads + Check） |

**分片规则**：
- < 1MB 的文件不分割（ModNet.vb:674-678）
- 单片上限 256KB
- 从**最大未完成片段的中部**切分，避免碎片过多（ModNet.vb:856-863）

**禁用分片的源白名单**（这些服务器不支持 Range，硬开会 416）：

```vb
' ModNet.vb:853-854
If TargetUrl.Contains("pcl2-server") OrElse TargetUrl.Contains("meloong.com") OrElse TargetUrl.Contains("bmclapi") OrElse
   TargetUrl.Contains("github.com") OrElse TargetUrl.Contains("optifine.net") OrElse TargetUrl.Contains("momot.rs") Then Return Nothing
```

**源失败自动禁用**（ModNet.vb:1078-1086）：502/404/合并失败/416 会禁用源；但 BMCLAPI 的 403/429 **不**禁用（避免误伤镜像站）。全源失败时降级为单线程再试一次（ModNet.vb:1108-1121）。

### 7.5 哈希校验设计

`FileChecker` 用**一个 Hash 字符串按长度判别算法**：32→MD5，40→SHA1，64→SHA256，128→SHA512。

SHA1 的三个来源：Mojang 版本 json、`assets/indexes/*.json` 的每个资源、Modrinth `hashes.sha1`。

**「借下载顺带校验」优化**（ModMinecraft.vb:2051）：

```vb
'如果需要检查 Hash，则留到下载时处理，以借助多线程加快检查速度
If CheckHash Then
    Return McAssetsListGet(Instance).Select(Function(Token As McAssetsToken) New NetFile(...
        Checker:=New FileChecker With {.ActualSize = If(Token.Size = 0, -1, Token.Size), .Hash = Token.Hash})).ToList
```

### 7.6 三层镜像机制

| 层 | 目标 | 实现 |
|---|---|---|
| BMCLAPI | Mojang 官方文件 | `piston-data` → `bmclapi2.bangbang93.com/assets` 等（ModDownload.vb:1248-1258） |
| MCIM | Modrinth / CurseForge | `mod.mcimirror.top`（ModDownload.vb:1304-1313） |
| PCL CDN | 官方自有 | `{CDN}` 占位符（ModSecret.vb:19-22） |

### 7.7 自适应并发调度

`NetManager`（ModNet.vb:1719-1795）用 3 个后台线程：

1. 两个下载线程，按 `File.Uuid Mod 2` 分片到两组
2. 一个 `StatRefresher` 线程：限速令牌桶 + 进度刷新

**加线程的三个条件**（ModNet.vb:1745-1765）：
1. 总线程数 < `NetTaskThreadLimit`（默认 64）
2. 当前速度 < 下限（默认 256 KB/s）—— 已跑满则不加
3. 准备中的线程数 ≤ 下载中的线程数

BMCLAPI 源额外 `Thread.Sleep(100)` **主动降频**。

### 7.8 ⭐ 跨版本文件复用（特色优化）

`CheckExistingFile`（ModNet.vb:1509）扫描所有 MC 文件夹，若其他版本已有**同 Hash 同大小**的文件，直接**复制**而非重新下载。

这解决了「资源文件几千个、装第二个版本要等几分钟」的痛点。

查找线程数：`(FilesToCheck.Count \ 40).Clamp(1, 8)`。

### 7.9 资源站点接入

| 项 | CurseForge | Modrinth |
|---|---|---|
| API Key | **需要**（环境变量 `PCL_CURSEFORGE_API_KEY`） | 不需要 |
| 类型筛选 | `classId`（6/4471/6945/6552/12） | `facets: project_type:` |
| 加载器筛选 | `modLoaderType`（**仅取第一个**） | `facets: categories:`（可多选） |
| 翻页 | `index` 偏移量 | `offset` 偏移量 |
| 排序 | `sortField=2&sortOrder=desc`（下载量） | `index=relevance` |

**鉴权头注入**（ModSecret.vb:26-38）：UA 伪装为 `PCL2/{版本}`，Modrinth 请求用 `SimulateBrowserHeaders=True` 模拟 Chrome UA，Referer 为 `http://{VersionCode}.open.pcl2.server/`。

**跨平台去重** `IsLike`（ResourceProject.vb:664-694）：加载器一致 + MC 版本一致 + 更新时间差 < 7 天 + 名称/描述/Slug 相同 → 判定为同一项目。

**中文搜索**：内置 MC 百科本地库（`WikiEntry.cs` + `WikiEntries.txt`），中文关键词 → 英文候选词 → 转成两站的搜索参数。

---

## 八、UI 框架与动画体系

### 8.1 MeloongCore 的定位

⛔ **本地源码为空**（submodule 未初始化，`git submodule status` 显示前缀 `-`）。

它**不是** UI 框架，而是**基础工具库 + 日志后端 + WPF 扩展集**：

| 能力 | API |
|---|---|
| 日志框架 | `FileLogger`、`LogLevel`、`LogBehavior` |
| 工具类 | `GeneralUtils` / `StringUtils` / `PathUtils` / `FileUtils` / `RegistryUtils` / `EnumUtils` |
| WPF 转换器 | `AdditionConverter`、`MultiplicationConverter`、`InverseBooleanToVisibilityConverter` |

集成方式：PCLCS 用 `<ProjectReference>`，VB 主工程用 `<Import>`。通过 `AssemblyResolve` **动态加载**（从嵌入资源解压 DLL），以避免 CLR 早期加载顺序崩溃（Application.xaml.vb:218-219）。

### 8.2 为什么大量自绘控件

**根本原因**（Application.xaml:81-96）：几乎所有原生 WPF 控件都被 `OverridesDefaultStyle` 覆盖成**只有 ContentPresenter 的空模板**。

四个动因：
1. **12+ 套主题**需要精确控制每一像素
2. **运行时热切换主题** —— 自绘控件用 `SetResourceReference` 就能跟着换色
3. **复合动画粒度** —— 如「匀速段 + 带特定初速度的减速段」，WPF Storyboard 难以表达
4. **规避 WPF 已知 Bug** —— 如 MyComboBox.vb:118-130 修复了「文本修改后仍误认为选中旧项」

### 8.3 三个代表性控件

| 控件 | 基类 | 实现方式 |
|---|---|---|
| `MyButton` | `Border` | XAML 声明骨架 + VB 状态机（3 种配色 + 按压回弹） |
| `MyCard` | `Grid` | **纯 VB 构建视觉树**，可折叠 + 分段高度动画 |
| `MyListItem` | `Grid` | XAML 最小骨架 + **后加载控件惰性创建** |
| `MyDropShadow` | `Decorator` | **纯 OnRender 自绘阴影**（9 分区） |
| `MyPageRight` | `AdornerDecorator` | 9 态页面状态机 |

**`MyCard` 的分段速度动画**（MyCard.vb:179-225）是最具价值的部分：

```vb
'匀速段
AnimList.Add(AaHeight(Me, (AbsDelta - EaseLength) * Math.Sign(Delta),
    (AbsDelta - EaseLength) / InitSpeed * 1000))
'减速段（带指定初速度）
AnimList.Add(AaHeight(Me, EaseLength * Math.Sign(Delta),
    EaseTime,, New AniEaseOutFluentWithInitial(InitSpeed, EaseTime / 1000, EaseLength), True))
```

**`MyDropShadow` 的 9 分区渲染**（MyDropShadow.vb:56-166）改编自 WPF 参考源码的 `SystemDropShadowChrome`（文件末尾标注了出处）。四条边用 `LinearGradientBrush`，四个角用 `RadialGradientBrush`，全部 `Freeze()` + 全局 `SyncLock` 缓存 → **一个阴影渲染几乎零分配**。

> 为什么不用 `DropShadowEffect`？因为它是**位图效果**，强制软件渲染、无法 `Freeze`、缩放时模糊。PCL2 只在弹窗等静态场景用它。

### 8.4 动画引擎

**机制**：独立的高优先级后台线程 + 手动时间步进。

```vb
' ModAnimation.vb:805-851
RunInNewThread(
Sub()
    Do While True
        Dim DeltaTime As Long = (GetTimeMs() - AniLastTick).Clamp(0, 100000)
        If DeltaTime < 3 Then GoTo Sleeper
        AniLastTick = GetTimeMs()
        RunInUiWait(Sub() AniTimer(DeltaTime * AniSpeed))
Sleeper:
        Thread.Sleep(1)
    Loop
End Sub, "Animation", ThreadPriority.AboveNormal)
```

全项目 grep 验证：`CompositionTarget` **0 命中**，`DispatcherTimer` 仅 3 处且与动画无关。

**核心设计 —— 相对增量 API**：

```vb
AaScaleTransform(PanFore, 0.955 - CType(PanFore.RenderTransform, ScaleTransform).ScaleX, 80,, New AniEaseOutFluent(AniEasePower.ExtraStrong))
```

传入的是「**还需要变化多少**」而非绝对值，配合 `AniEase.GetDelta(t1, t0)` 只计算本帧增量 → 支持**中断重入**，新动画从当前视觉状态无缝接管，不跳变。

**动画组（Group）模型**：同名动画组互相替换（`AniStop(Name)`），命名如 `"MyButton Color " & Uuid`。

**缓动函数**（13 种）：`AniEaseLinear` / `AniEaseIn|Out|InoutFluent` / `AniEaseOutFluentWithInitial`（**带指定初速度**）/ `AniEaseIn|OutBack` / `AniEaseIn|OutCar` / `AniEaseIn|OutElastic` / `AniEaseInout`（组合器）。

**全局动画抑制计数器** `AniControlEnabled`（引用计数 + SyncLock），全项目 **127 处**使用：

```vb
If IsLoaded AndAlso AniControlEnabled = 0 Then '防止默认属性变更触发动画
```

**规模**：22 个 `Aa*` 工厂方法、226 处 `AniStart` 调用、127 处 `AniControlEnabled` 检查。

### 8.5 ModEvent —— 事件即配置

**附加属性 + 23 种事件类型的公开 API**（ModEvent.vb:148-174），注释明确标注「该枚举在自定义事件中使用，是公开 API 的一部分」。

```xml
<local:MyButton EventType="打开网页" EventData="https://example.com">
    <local:CustomEventService.Events>
        <local:CustomEvent EventType="修改设置" EventData="UiLauncherTransparent|400" />
    </local:CustomEventService.Events>
</local:MyButton>
```

所有事件最终汇总到一个 `Select Case`（ModEvent.vb:179-356）→ **页面代码 0 依赖业务逻辑**。

**安全防护**（ModEvent.vb:430-444）：`执行命令` / `打开文件` / `下载文件` / `加入房间` 需要用户确认（可勾选「今后不再要求」）。

### 8.6 主题系统

**HSL 主色 + 8 级派生色阶**（ModSecret.vb:65-84）：

```vb
Color1 = New MyColor().FromHSL2(ColorHue, ColorSat * 0.2, 25 + ColorLightAdjust * 0.3)  ' 深灰（正文）
Color3 = New MyColor().FromHSL2(ColorHue, ColorSat, 55 + ColorLightAdjust)               ' 主主题色
Color4 = New MyColor().FromHSL2(ColorHue, ColorSat, 65 + ColorLightAdjust)               ' 浅主题色
```

**`FromHSL2` 的亮度补偿表**（ModBase.vb:365-400）是「一键换肤」不违和的关键 —— 15 段分区补偿让任意色相（H=210 蓝 / H=0 红）都映射到**视觉亮度相近**：

```vb
Dim cent As Double() = {
    +0.1, -0.06, -0.3,    '0, 30, 60
    -0.19, -0.15, -0.24, '90, 120, 150
    -0.32, -0.09, +0.18, '180, 210, 240
    +0.05, -0.12, -0.02, '270, 300, 330
    +0.1, -0.06}
```

每种颜色注册**两个资源键**：`ColorBrushN`（SolidColorBrush，给 Background/Foreground）+ `ColorObjectN`（Color，给 DropShadowEffect 等需要裸 Color 的场合）。

### 8.7 FormMain 页面组织

**两级页面模型**：
- 主页面（`PageType`）：启动 / 下载 / 联机 / 设置 / 更多 + 5 个副页面
- 副页面（`PageSubType`）：靠**下标索引**左栏 `MyListItem` 集合

**页面栈** `PageStack` 支持子页面嵌套：

```vb
If PageStack.Contains(Stack) Then
    Do While PageStack.Contains(Stack) : PageStack.RemoveAt(0) : Loop
Else
    PageStack.Insert(0, PageCurrent)
End If
```

**`PageChangeAnim`**（FormMain.xaml.vb:1521-1574）：左右栏**独立动画组**，110ms 后才换内容（旧页面先播完退出动画），30ms 延迟触发进场动画（注释说明：让 Loaded 中加载的控件得以处理）。

**左栏逐项进场**（MyPageLeft.vb:16-59）的错落延迟：

```vb
Delay += Math.Max(15 - Id, 7) * 2   ' 前面的间隔大，后面的密集，形成"渐入"节奏
```

配合两段式位移（先 `+5` Fluent 快出慢入，再 `+20` OutBack 带回弹），形成「甩入」效果。进场期间禁用 hover 动画，结束后再恢复。

**`MyPageRight` 的 9 态状态机**（Empty / LoaderWait / LoaderEnter / LoaderStayForce / LoaderStay / LoaderExit / ContentEnter / ContentStay / ContentExit / PageExit），非法迁移**直接抛异常**（刻意的防御性设计）。

---

## 九、ModModpack —— 整合包支持

### 9.1 格式识别优先级

两级探测（先看根目录，再看一级子目录）：

```vb
' ModModpack.vb:43-56
If Archive.GetEntry("mcbbs.packmeta") IsNot Nothing Then PackType = 3 : Exit Try   'MCBBS（优先于 manifest.json）
If Archive.GetEntry("mmc-pack.json") IsNot Nothing Then PackType = 2 : Exit Try    'MMC（优先于 manifest.json）
If Archive.GetEntry("modrinth.index.json") IsNot Nothing Then PackType = 4 : Exit Try
If Archive.GetEntry("manifest.json") IsNot Nothing Then
    If Json("addons") Is Nothing Then PackType = 0  'CurseForge
    Else PackType = 3  'MCBBS
End If
If Archive.GetEntry("modpack.json") IsNot Nothing Then PackType = 1 : Exit Try      'HMCL
If Archive.GetEntry("modpack.zip") IsNot Nothing OrElse Archive.GetEntry("modpack.mrpack") IsNot Nothing Then PackType = 9
```

> MCBBS / MMC 优先于 CurseForge，因为它们的 `manifest.json` 结构不同（有无 `addons` 字段）。

### 9.2 支持格式总表

| PackType | 格式 | 关键文件 | 安装方法 | 行号 |
|---|---|---|---|---|
| 0 | CurseForge | `manifest.json`（无 addons） | `InstallPackCurseForge` | 162-363 |
| 1 | HMCL | `modpack.json` | `InstallPackHMCL` | 494-540 |
| 2 | MMC / MultiMC / Prism | `mmc-pack.json` | `InstallPackMMC` | 543-674 |
| 3 | MCBBS / BakaXL | `mcbbs.packmeta` | `InstallPackMCBBS` | 677-761 |
| 4 | Modrinth | `modrinth.index.json` | `InstallPackModrinth` | 366-491 |
| 9 | 带启动器的压缩包 | `modpack.zip` / `.mrpack` | `InstallPackLauncherPack` | 764-823 |
| 默认 | 普通压缩包 | 正则探测 `versions/XXX/XXX.json` | `InstallPackCompress` | 826-862 |

### 9.3 亮点：MMC 的 instance.cfg 设置迁移

ModModpack.vb:574-622 把 MMC 实例配置映射到 PCL 设置：

| MMC 字段 | PCL 设置 |
|---|---|
| `OverrideCommands` | `VersionAdvanceRun`（并替换 `$INST_JAVA` → `{java}java.exe` 等变量） |
| `JoinServerOnLaunch` + 地址 | `VersionServerEnter` |
| `iconKey` | 版本图标 |
| `JvmArgs` | `VersionAdvanceJvm` |

### 9.4 路径穿越校验

Modrinth 安装时的防护（ModModpack.vb:436-439）：

```vb
Dim TargetPath As String = Path.Combine(VersionFolder, File("path").ToString.TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
If Not PathUtils.IsParentOf(VersionFolder, TargetPath) Then
    MyMsgBox($"整合包的文件路径超出了版本文件夹，请向整合包作者反馈此问题！...", "文件路径校验失败", IsWarn:=True)
    Throw New OperationCanceledException
End If
```

---

## 十、ModBase 基础设施

### 10.1 线程调度四原语

| 方法 | 语义 | 位置 |
|---|---|---|
| `RunInNewThread(Action, Name, Priority)` | 开新线程，异常自动记录 | 899-914 |
| `RunInUiWait(Action)` | **阻塞**直到 UI 线程执行完毕 | 928-936 |
| `RunInUi(Action, ForceWaitUntilLoaded)` | **不阻塞**，按触发顺序排队 | 939-949 |
| `RunInThread(Action)` | 确保在工作线程执行 | 951-957 |

注释警告：「为防止线程互锁，请仅在开始加载动画、从 UI 获取输入时使用！」

### 10.2 配置系统

约 **190 个设置项**集中声明（Settings.vb:5-198），三种存储源：

| Source | 位置 |
|---|---|
| `Normal` | `Setup.ini`（PCL 目录下） |
| `Registry` | `HKEY_CURRENT_USER\Software\{RegFolder}` |
| `Instance` | `{实例目录}\PCL\Setup.ini`（版本独立设置） |

**`OnChanged` 回调**把设置项与行为直接挂钩：

```vb
New Setting("ToolDownloadSpeed", 42, Source:=Sources.Registry, OnChanged:=AddressOf ModNet.UpdateNetTaskSpeedLimitHigh),
New Setting("UiLauncherTheme", 0, OnChanged:=AddressOf ThemeRefresh),
```

**加密存储**：DES-CBC，IV 固定 `95168702`，密钥 = `"PCL" & 设备识别码` 的稳定哈希前 8 字符（Settings.vb:437-475）。`GetSafe` / `SetSafe` 在遇到加密项时抛异常，防止外部逻辑读取敏感值。

### 10.3 日志与异常处理

`PclLogger` 继承 MeloongCore 的 `FileLogger`，**隐私脱敏在日志层完成**：

```vb
' PclLogger.vb:6-8
Text = FilterUserName(FilterAccessToken(Text, "*"c), "*"c)
```

`LogBehavior` 六级行为：None / ToastIfDebug / Toast / Alert / AlertThenFeedback / AlertThenCrash。

`AlertThenCrash` 的特殊处理：首次弹窗询问反馈，**二次触发则强制退出**（PclLogger.vb:34-48）——「多次触发通常意味着其他线程在循环刷异常」。

### 10.4 MyColor —— 颜色运算基础

不是 `System.Windows.Media.Color` 的别名，而是支持小数与四则运算的独立类型：

```vb
Public Shared Operator +(a As MyColor, b As MyColor) As MyColor
Public Shared Operator *(a As MyColor, b As Double) As MyColor
Public Shared Function Lerp(ValueA As MyColor, ValueB As MyColor, Percent As Double) As MyColor
```

支撑了动画引擎的 `AaColor` 增量计算（`ValueLast` 记录浮点余数，避免逐帧累加色带）。

---

## 十一、开源裁剪（ModSecret）

`ModSecret.vb` 文件头明确声明「由于包含加解密等安全信息，本文件中的部分代码已被删除」。

### 11.1 被裁剪的能力

| 能力 | 开源版状态 |
|---|---|
| 设备识别码 `GetIdentify()` | 固定返回 `"0000-0000-0000-0000"` |
| 授权码 `InputPotatoCode` / `GeneratePotatoCode` | **空实现** |
| `CurrentRank` | 恒为 `DonationRank.None` |
| `ThemeUnlock` / `ThemeCheckOne` | 恒 `False` / 恒 `True`（所有主题视为已解锁） |
| 更新功能 `UpdateCheckByButton` / `UpdateReplace` | 空实现，提示「该版本中不包含更新功能」 |
| CDN 签名 `SecretCdnSign` | 只剩 `{CDN}` 字符串替换 |
| 遥测 `ClsBaseUrl` | `""` → **开源版完全不上报** |
| CurseForge API Key | 改由环境变量 `PCL_CURSEFORGE_API_KEY` 注入 |
| 微软登录 ClientId | 改由环境变量 `PCL_MS_CLIENT_ID` 注入 |
| MC 本体下载 | `RestrictMcDownload = True`（禁用） |
| 注册表 | 开源版写 `HKCU\Software\PCLDebug`，与官方版隔离 |

### 11.2 明确声明

以上仅描述本地代码中**可读到的**机制。授权码的签名算法、CDN 签名算法、真实识别码生成算法**在本地代码中不存在**，不做任何猜测。

---

## 十二、值得学习的设计决策

1. **相对增量动画 API** —— `AaX(Obj, Value - Current)` + `GetDelta`，实现可中断、可重入的动画，规避 WPF Storyboard 打断时的跳变问题。

2. **Loader 组合驱动的流水线** —— `Block` 属性控制串/并行、`ProgressWeight` 控制进度占比，「获取 Java + 登录 + 补全文件」天然并行。

3. **参数去重是安全关键逻辑** —— 游戏参数同名键「新值覆盖旧值」，JVM 参数「完全相同才丢弃」，直接关系到游戏能否启动。

4. **崩溃分析是纯规则引擎** —— 47 种原因 × 三级优先级 + 堆栈正则挖掘，完全离线可解释，无机器学习。

5. **EXCEPTION_ACCESS_VIOLATION 精准归因** —— 靠 `hs_err` 日志的 `# C  [框架名]` 区分 Intel/AMD/NVIDIA 驱动。

6. **Java 约束「求交」模型** —— 多来源各贡献一个 `ValueRange`，冲突时回退 + Toast 而非硬失败。

7. **借下载顺带做 SHA1 校验** —— 零额外 IO。

8. **跨版本文件复用** —— 同 Hash 同大小直接复制，省下数 GB 下载。

9. **GFW 截断检测** —— JSON 首尾校验，避免拿到截断数据当成功。

10. **HSL 亮度补偿表** —— 让 12 套主题换色后视觉亮度一致。

11. **矢量阴影 + Brush 冻结 + 全局缓存** —— 避免 `DropShadowEffect` 的位图渲染开销。

12. **事件即配置** —— 附加属性 + 23 种事件类型，用户改 XAML 就能加功能。

---

## 十三、潜在问题与风险

| 问题 | 位置 | 影响 |
|---|---|---|
| `BackupUrl` 参数未使用 | ModNet.vb:44（声明）vs 函数体 | 声明了备用 URL 语义但从未实现；真正的备用源靠 `NetFile.Sources` 链路 |
| CurseForge 加载器筛选只取第一个 | ResourceSearcher.vb:88 `.Flags.First` | 多加载器筛选在 CF 侧退化为单选（Modrinth 侧支持多选） |
| 版本清单不落盘 | ModDownload.vb:234 | 每次冷启动需重新拉 manifest（靠 CacheCow 部分缓解） |
| 硬编码超时值 | ModNet.vb:52-64（10s/30s/4s） | 网络环境差异大时不够灵活 |
| 线程上限 64 | ModNet.vb:311-315 | 对某些服务器可能触发限流（已用 BMCLAPI 降频缓解） |
| 失败熔断阈值靠常数推断 | ModNet.vb:1352 | 不同网络下可能过早或过晚熔断 |
| CurseForge CDN 硬编码 | ResourceVersion.vb:135 `edge.forgecdn.net` | CurseForge 已迁移新 CDN，fallback 可能失效 |
| 动画线程手动调度 | ModAnimation.vb:805-851 | 与渲染不同步，极端情况可能撕裂；UI 线程阻塞时会卡顿 |
| `Thread.Sleep(1)` 控制帧率 | ModAnimation.vb | 实际精度取决于系统调度，最坏约 30-60 FPS |
| 硬编码颜色 | MyCard.vb:70 `Color.FromArgb(245,255,255,255)` | 不随主题变化，深色主题下可能不协调 |
| 状态机直接抛异常 | MyPageRight.vb | 非法迁移会抛未捕获异常 |
| MeloongCore 源码缺失 | submodule 未初始化 | 无法审计日志脱敏、加密等实现 |
| .NET Framework 4.8 | vbproj | 已停止支持；`OldEnvironmentAssert` 专门处理相关崩溃 |

---

## 十四、附：文件索引

| 主题 | 路径（相对 `PCL2/`） | 行数 |
|---|---|---|
| 启动参数与进程 | `Plain Craft Launcher 2/Modules/Minecraft/ModLaunch.vb` | 2221 |
| 版本模型 | `Plain Craft Launcher 2/Modules/Minecraft/ModMinecraft.vb` | 2153 |
| 崩溃分析 | `Plain Craft Launcher 2/Modules/Minecraft/ModCrash.vb` | 1122 |
| Java 管理 | `Plain Craft Launcher 2/Modules/Minecraft/ModJava.vb` | 584 |
| 进程监控 | `Plain Craft Launcher 2/Modules/Minecraft/ModWatcher.vb` | 394 |
| 整合包 | `Plain Craft Launcher 2/Modules/Minecraft/ModModpack.vb` | 866 |
| 下载业务 | `Plain Craft Launcher 2/Modules/Minecraft/ModDownload.vb` | 1388 |
| 安装流水线 | `Plain Craft Launcher 2/Pages/PageDownload/ModDownloadLib.vb` | 2176 |
| 网络引擎 | `Plain Craft Launcher 2/Modules/Base/ModNet.vb` | 1933 |
| 加载器框架 | `Plain Craft Launcher 2/Modules/Base/ModLoader.vb` | 791 |
| 动画引擎 | `Plain Craft Launcher 2/Modules/Base/ModAnimation.vb` | 1066 |
| 基础工具 | `Plain Craft Launcher 2/Modules/Base/ModBase.vb` | 1216 |
| 日志 | `Plain Craft Launcher 2/Modules/Base/PclLogger.vb` | 54 |
| 事件系统 | `Plain Craft Launcher 2/Modules/ModEvent.vb` | 446 |
| 验证规则 | `Plain Craft Launcher 2/Modules/Base/ModValidate.vb` | 424 |
| 音乐 | `Plain Craft Launcher 2/Modules/ModMusic.vb` | 347 |
| 敏感模块 | `Plain Craft Launcher 2/Modules/ModSecret.vb` | 203 |
| 主窗体 | `Plain Craft Launcher 2/FormMain.xaml.vb` | 1735 |
| 设置系统 | `Plain Craft Launcher 2/Pages/PageSetup/Settings.vb` | 479 |
| 资源模型 | `Plain Craft Launcher 2/Modules/Resource/ResourceProject.vb` | 711 |
| 资源搜索 | `Plain Craft Launcher 2/Modules/Resource/ResourceSearcher.vb` | 530 |
| 本地 Mod | `Plain Craft Launcher 2/Modules/Resource/LocalResourceLoaders.vb` | 345 |
| Java 检测 | `PCLCS/Java.cs` | — |
| 常量枚举 | `PCLCS/Constants.cs` | — |
| 资源常量 | `PCLCS/Resource/Resource.cs` | — |
| MC 百科库 | `PCLCS/Resource/WikiEntry.cs` | — |
| 自绘控件 | `Plain Craft Launcher 2/Controls/` | ~30 文件 |
| 子模块 | `MeloongCore/` | ⛔ 空 |

---

*报告生成时间：2026-10-31　|　所有引用的 `文件:行号` 均已对本地检出内容逐一核验。*
