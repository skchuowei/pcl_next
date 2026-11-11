# Windows 版 pcl-next 逐文件 Bug 排查报告（dev.6-Windows）

> 排查日期：2026-11-11
> 审查根：`D:\bing\MC\pcl_x\PCL_NEXT_Windows`（分支 `dev.6-Windows`，以磁盘现状为准）
> 审查方式：8 个并行只读审查子 agent 按模块分组逐文件通读 + 同目录 `.vb` 语义对照 + 被调成员/扩展方法交叉核对；MainAgent（汇总方）同步自审基础层、启动链路、下载/游戏内核链路并跑通一次 Release 编译佐证。
> 纪律：本轮**只读排查**——未改任何源码、未提交 git、未改动版本常量；`PCL2\`(linux)、`PCL_NEXT_Linux`、`PCL_NEXT_Android` 未触碰。

---

## 一、概览

### 1.1 编译佐证（MainAgent 自跑）

- 命令：`dotnet build pcl-next\pcl-next.csproj -c Release -r win-x64 --self-contained -p:PclChannel=Dev`
- 结果：**0 错误 / 30 警告**，约 2 分 04 秒构建通过；产物落 `pcl-next\bin-net10\Dev\2.100.0.6-0.82\win-x64\pcl-next.dll`。
- 结论：当前代码可编译，发现的问题均为**运行期逻辑/并发/健壮性缺陷**，不存在阻断构建的语法或签名错误。

### 1.2 编译范围事实（排查前提）

- 解决方案 `pcl-next.sln` 只引用 `pcl-next\pcl-next.csproj`；csproj `DefaultItemExcludes` 排除全部 `*.vb`，同目录 `.vb` 是 VB→C# 迁移遗留**参考副本/语义真源**，不参与编译，其内部问题不计为本次 bug。
- 已被 `<Compile Remove>` 排除：`My Project\Settings.Designer.cs`、`My Project\MyExtensions\MyWpfExtension.cs`、`Controls\MySliderDot.xaml.cs`；`pcl-next.vbproj` 存在但未被 sln 引用。
- 内核工程 `PCL.Download / PCL.Cli / PCLCS / MeloongCore / MeloongCore\Tests` 主要核对被调签名与扩展语义，未逐行深审内部缺陷。

### 1.3 问题总量（去重后）

| 严重度 | ✅确认 | ⚠️可疑 | 合计 | 说明 |
|---|---|---|---|---|
| 🟥 高 | 0 | 0 | **0** | 无确认的确定性崩溃主路径 / 越界 / 死循环（除标注的可疑边界外） |
| 🟨 中 | 8 | 14 | **22** | 见第三节（确认）、第四节（可疑） |
| 🟩 低 | 8 | 约 44 | **约 52** | 多为上游 VB 同构遗留的健壮性/并发/资源释放问题，见第五节 |
| 上游已知留痕 | — | — | 30+ 行 | 已登记在《PCL2上游遗留问题清单.md》，C# 侧复核仍在，不计新 bug，见第六节 |

> 本轮（J1/J3/J4）已落地的中危补丁见第九节；最终 Release 编译 **0 错误**，警告维持基线技术债清单（第八节）。

> 编号前缀来源：`A1/A2` 外壳与主窗体组，`B1/B2` 主模块事件与音乐备忘密钥组，`C1/C2/C3/C4` 网络/基础/加载器校验/动画组，`MA` 为 MainAgent 自审。各子报告原文在 `docs/bugscan/`。

### 1.4 总体结论

- **没有发现确认的高危（🟥）缺陷**：未发现必然触发的空引用崩溃、数组越界、死循环或错误默认值主路径；多数可疑点需要「特定边界值 / 跨线程并发 / 异常数据 / 未就绪控件」才命中。
- 中等问题集中在四类：① **取消令牌 / 资源未释放泄漏**（网络引擎 CTS、进程、Worker 事件、静态字典）；② **并发共享状态无锁/非原子**（多线程下载、动画组、加载器组合状态机、静态 Random/UUID）；③ **空引用与零/除零防护缺失**（null 解引用、空集 All 恒真、时长/距离/权重为 0 除零得 NaN/Inf）；④ **net48→net10 平台翻转残留**（UseShellExecute 默认翻转、ServicePointManager、弱加密过时 API）。
- 大量低危项为**上游 VB 同构继承**：C# 迁移保留了原结构，未新引入分支反转；是否整改随上游重写节奏处理。

---

## 二、🟥 高危（确认）

**无。** 本轮 8 个子 agent + MainAgent 自审均未确认确定性高危缺陷。

---

## 三、🟨 中危 · 确认缺陷（✅）

### M1. 网络引擎 C# 接管后 CancellationTokenSource 从不取消、也从不释放（泄漏）【C1】
- 位置：`pcl-next\Modules\Base\NetInterop.cs:90-91`；`Modules\Base\ModNet.cs:1654-1669 / 1673-1707`；字段声明 `ModNet.cs:884`
- 证据：全仓检索 `CSharpCts` 仅 3 处——字段声明、NetInterop 赋值，**无任何 `Cancel()` 或 `Dispose()` 调用**；`File.Fail / File.Cancel / CancelInternal` 全链路未挂接引擎令牌，`Task.Run` 收尾路径也未释放 CTS。
- 影响：用户取消后引擎令牌仍挂着，只能靠进度回调在下次进度事件里判断 `State >= Finished`（Canceled=6 ≥ Finished=5）；引擎停滞且无进度事件时后台下载继续；每个文件泄漏一个 CTS。
- 根因：接管路径只建 CTS，VB 取消链路未挂接引擎令牌；完成/失败收尾漏释放。
- 建议：`CancelInternal`（及 `Fail`）中 `CSharpCts?.Cancel()`；引擎 `DownloadAsync` 返回/异常后在收尾 `CSharpCts?.Dispose()`；取消与释放防重入。

### M2. 下载失败收尾 `OnFail` 直接解引用未尝试源的 `Ex`（null → NRE）【C1】
- 位置：`pcl-next\Modules\Base\ModNet.cs:2070`
- 证据：`File.Sources.All(s => s.Ex.IsCanceled() || s.Ex is TimeoutException)`；源字段 `Ex` 默认 null（`ModNet.cs:491`，构造 `:1025` `Ex = null`）；`IsCanceled()` 扩展内部 `ex.Flatten()` 对 null 解引用（ExceptionExtensions.cs:17-18 / 38-40）。失败文件只要存在一个未尝试源即命中。
- 建议：谓词改 `s.Ex is not null && (s.Ex.IsCanceled() || s.Ex is TimeoutException)`，与 `OnFileFail:2045` 已有 null 判空口径对齐。

### M3. `SourceFail` 任务级失败计数读-改-写非原子，递增可能丢失【C1】
- 位置：`pcl-next\Modules\Base\ModNet.cs:1425-1427`
- 证据：`int arglocation = Task.FailCount; Interlocked.Increment(ref arglocation); Task.FailCount = arglocation;`（对比同函数 `:1422` 源级 `Interlocked.Increment(ref Th.Source.FailCount)` 是原子的）。多线程并发下递增丢失，喂给 `:1796` 强制失败阈值 `Math.Min(10000, Math.Max(FileRemain*8, NetTaskThreadLimit*8+3))` 偏小。
- 建议：直接 `Interlocked.Increment(ref Task.FailCount)`；阈值判断前再快照。

### M4. `ExtractResources` 资源名不存在时 else 分支解引用 null【C2】
- 位置：`pcl-next\Modules\Base\ModBase.cs:1290`；`:1307-1310`
- 证据：`var Resource = My.Resources.Resources.ResourceManager.GetObject(ResourceName);` 资源不存在/未编进程序集时返回 null；`if (Resource is byte[]) … else if (Resource is string) … else { throw new Exception($"…资源 {ResourceName} 的类型不支持：{Resource.GetType().Name}"); }`——null 进入 else，`Resource.GetType()` NRE，而非「资源不存在」的友好异常。
- 建议：进入类型分支前先 `if (Resource is null) throw new Exception($"资源 {ResourceName} 不存在");`。

### M5. Java 下载列表 worker 本次失败后仍直接取 `LastResult`，抛「从未成功完成」掩盖真实原因【MA】
- 位置：`pcl-next\Modules\Minecraft\ModJava.cs:560-564`
- 证据：
  ```csharp
  if (!_SelectOrDownloadJava_DownloadListWorker.HasSucceeded)
      _SelectOrDownloadJava_DownloadListWorker.Start(c, p?.SplitTo(0.4d));
  _SelectOrDownloadJava_DownloadListWorker.WaitIfRunning(cancellationToken: c);
  var Components = _SelectOrDownloadJava_DownloadListWorker.LastResult;
  ```
  `RedoableWorker.LastResult` 在 `!hasSucceeded` 时抛 `InvalidOperationException("从未成功完成过。")`。本次 Start 的负载因网络/JSON 损坏失败时 `hasSucceeded` 仍 false，564 行直接取 `LastResult` 抛异常；被 `SelectOrDownloadJava` 外层 catch 后只记「选择 Java 时出现异常」，掩盖真实失败原因，也走不到「手动安装 Java」提示。首次启动前的 `HasSucceeded` 判断已补，**失败重试路径未补**（上游 #9533/L1 部分修复的残留）。
- 建议：`WaitIfRunning` 后再判 `HasSucceeded`，未成功走失败分支提示手动安装。

### M6. `MyBitmap` 用单次 `Stream.Read` 读满 WebP 文件，尾部可能为 0（CA2022 佐证）【MA】
- 位置：`pcl-next\Modules\Base\MyBitmap.cs:130`（2 字节头）、`:136`（整个 WebP 文件）
- 证据：`InputStream.Read(Header, 0, 2);` 未检查返回值；`var FileBytes = new byte[(int)(InputStream.Length - 1L + 1)]; InputStream.Read(FileBytes, 0, FileBytes.Length);` 单次读不保证读满，尾部默认 0。本地 FileStream 通常一次读满，但规范不保证；大文件/被截断流场景 WebP 解码失败或图像损坏。编译已报 CA2022 两处。
- 建议：循环读到累计长度等于目标，或用 `Stream.ReadExactly`（net10 可用）。

### M7. Java 范围约束冲突时 `Intersect` 返回 null，下一行 `IsEmpty()` 空引用 NRE【MA】
- 位置：`pcl-next\Modules\Minecraft\ModJava.cs:217-221`
- 证据：
  ```csharp
  JavaRange = JavaRange?.Intersect(Constraint);
  if (JavaRange.IsEmpty()) { … JavaRange = Constraint; }
  ```
  `ValueRange<T>.Intersect` 无交集时返回 null（MeloongCore/General/ValueRange.cs:131-132 注释「否则返回 null」）。叠加两个互相冲突的 Java 约束（如先 AtLeast(21) 再 LessThan(16)）时 `JavaRange` 被赋成 null，218 行 `IsEmpty()` 解引用 NRE，本应走 220 行冲突提示分支。
- 建议：判空后再 `IsEmpty()`；无交集时置空范围并记录冲突。

### M8. `MyBitmap` 图片缓存字典被降级为实例字段，缓存永不命中、重复解码【C5，对照 VB 可证回归】
- 位置：`pcl-next\Modules\Base\MyBitmap.cs:91`；使用点 `:113-120`
- 证据：`private ConcurrentDictionary<string, MyBitmap> _Cache = new ConcurrentDictionary<string, MyBitmap>();` 为类内**实例字段**，每个 `new MyBitmap(路径)` 都新建一个空字典；`:113` `ContainsKey` 永远 false，`TryAdd` 只写进各自实例。VB 对照 `MyBitmap.vb:75` 为过程内 `Static Cache As New ConcurrentDictionary(Of String, MyBitmap)`（全实例共享一份）。转换器把 VB 过程级 `Static` 局部抬成类实例字段，未提升为共享静态成员，缓存语义在 C# 侧丢失。
- 影响：资源图片缓存永不命中，重复构造、重复解码位图（性能/内存损耗）；不导致崩溃。
- 建议：改 `private static readonly ConcurrentDictionary<string, MyBitmap> _Cache = new();`，使缓存跨实例共享。

---

## 四、🟨 中危 · 可疑（⚠️ 待运行/边界确认）

### S1. `Feedback` 异常捕获面窄于 VB `On Error Resume Next`，未列类型异常逃逸【B1】
- 位置：`pcl-next\Modules\ModMain.cs:1227-1248`（Feedback）
- 证据：仅列 `Win32Exception / IOException / UnauthorizedAccessException` 三类 catch，无 `Exception` 兜底；NRE、InvalidOperationException、ArgumentException、InvalidCastException 等会穿透到调用方。VB `On Error Resume Next`（ModMain.vb:965）整方法静默吞掉。反馈流程内任一环节抛未列类型异常（如 `MyMsgBox` 内部空引用），C# 异常逃逸、VB 侧被吞。
- 建议：末尾补 `catch (Exception ex) { Logger.Warn(ex, "反馈流程失败"); }`，与 `FeedbackInfo`（1257-1273）同口径。

### S2. 音乐列表初始化部分失败后处于半初始化 null 状态，后续 `.Any()/.Remove()` 解引用【B2】
- 位置：`pcl-next\Modules\ModMusic.cs:75、78、87、218、301、384、409-410`
- 证据：`:41-52` 先 `new AllList` 再枚举填充；`:62-65` 异常仅记日志不复位列表；`:75` `if (MusicAllList is null || !MusicAllList.Any() || !MusicWaitingList.Any())` 未先判 `WaitingList` null。枚举目录/建目录抛异常被吞后，两个静态列表一个已 new、另一个可能仍 null；调用点直接 `.Any()/.Remove(MusicCurrent)`。VB 同构，非 C# 回归。
- 建议：catch 内把两列表复位为 null（或空）；各调用点先 `is null` 再 `.Any()`。

### S3. `ThemeRefreshMain` 的 RunInUi lambda 无 try/catch，`FrmMain` 为 null 时 `IsLoaded` NRE【B2】
- 位置：`pcl-next\Modules\ModSecret.cs:150`（lambda 整体 `:148-174`）
- 证据：`if (!ModMain.FrmMain.IsLoaded) return;`，`FrmMain` null 即 NRE。外层 `ThemeRefresh` 的 catch（`:100/141`）只覆盖 `:139` 同步调用路径；经 `FormMain.xaml.cs:551` 直接调用时绕过保护。VB 同构。
- 建议：lambda 入口先判 `ModMain.FrmMain is null` 返回；或整体包 try/catch 记日志。

### S4. `NetRequestByClientMultiple` 跨线程共享 `RequestResult/RequestEx/FailCount` 无锁，计数丢更新可致主线程空转【C1，并入 MA#5】
- 位置：`pcl-next\Modules\Base\ModNet.cs:152-201`
- 证据：线程内 `RequestResult = NetRequestByClient(…); FailCount += 1; RequestEx = ex;`（`:158`），主线程 `:162/167/183` 直接读；全函数无 `lock/Interlocked/volatile`；循环出口仅「结果非空」或「失败数满额」。并发写 int 丢更新时 `FailCount == ThreadCount` 永不成立，主线程 `while(true)` 空转、线程已死也不退出。VB 同形继承，C# 未补同步。
- 建议：`lock` 保护三共享状态，或 `Interlocked.Increment(ref FailCount)`；结果用 `TaskCompletionSource<string>` 替代轮询。

### S5. 下载 `Reset()` 用当前 `DownloadDone` 快照扣全局累计，并发线程仍写时累计偏差【C1】
- 位置：`pcl-next\Modules\Base\ModNet.cs:1518-1521`
- 证据：`lock(LockChain) Threads = null;` 仅锁线程链；`Interlocked.Add(ref NetManager.DownloadDone, -DownloadDone); DownloadDone = 0L;` 与线程内 `Interlocked.Add(ref DownloadDone, RealDataCount)`（`:1348`）无互斥。`Reset` 由 `SourceFail/CancelInternal/TryBeginThread` 单线程触发，但下载线程未必已停；扣减基于非原子快照，只增不减的镜像被多扣/少扣。
- 建议：锁内 `Interlocked.Exchange(ref DownloadDone, 0)` 取旧值按旧值扣全局；或先停所有线程再 Reset。

### S6. `StartProcess` 未设 `UseShellExecute`，net10 默认翻转后协议 URL/目录打不开【C2】
- 位置：`pcl-next\Modules\Base\ModBase.cs:1079-1085`；调用点 `:1217`（OpenWebsite）、`:1238`（OpenExplorer 目录分支）
- 证据：`return Process.Start(StartInfo);` 全程无 `UseShellExecute =`。net48 时代默认 `true`，shell 可解析 `http(s)://` / `minecraft://` 协议与文件夹；迁 net10 后默认 `false`（CreateProcess），启动协议 URL/目录会抛 `Win32Exception`，异常被外层 catch 吞掉后只走剪贴板/告警兜底，网页/目录打不开。全仓其他启动点均显式 `UseShellExecute = false`（ModLaunch.cs:2652/2685/2726），无全局默认兜底。
- 建议：shell 类启动（URL、目录）在 `StartProcess` 内按 FileName 判定或由调用点显式 `UseShellExecute = true`；执行体启动保持 `false`。

### S7. 加载器组合 `LoaderCombo.Update()` 可被多工作线程并发进入，遍历普通 `List<LoaderBase>` 无锁【C3】
- 位置：`pcl-next\Modules\Base\ModLoader.cs:778-869`（入口 704、758、740-774）
- 证据：`Start` 经 `RunInThread(Update)` 起一线程；每个子任务 `Finished` 经 `OnStateChangedThread` → `RunInThread(SubTaskStateChanged)` 再调 `Update()`。多子任务同时完成时多线程并发遍历 `Loaders`（`:785`）并调 `Loader.Start(...)`（`:836/840/844/853`），无 `LockState`、无快照；`Loaders` 为普通 List（`:659`）。并发枚举可能抛 `InvalidOperationException`、重复 Start 子任务、`Input` 局部值多线程读写竞态。VB 同构并发债。
- 建议：`Update` 入口 `LockState` 串行化，或先快照 `Loaders` 再遍历；`Input` 在快照内传递。

### S8. `AniRunning` 标志位只写不读，重复 `AniStart()` 开双动画线程【C4】
- 位置：`pcl-next\Modules\Base\ModAnimation.cs:1019-1024`；循环体 `:1031`
- 证据：`AniRunning = true;` 之后是 `while (true) { … }`，循环内无 `if (!AniRunning) break;`，入口也无 `if (AniRunning) return;` 重入守卫；`AniRunning` 全文件无第二处读取。重复无参 `AniStart()` 再开一条动画线程，两线程都经 `Dispatcher.Invoke` 枚举/删除 `AniGroups`，动画计数与字典状态重复叠加。当前仅 `FormMain.xaml.cs:588` 一处启动调用，重复启动才触发。VB 同形继承。
- 建议：入口加重入守卫；循环内每轮检查 `AniRunning` 退出；或移除死标志位。

### S9. `AniControlEnabled` 加减非原子、无下限钳制，可减为负数致动画判定永久失效【C4】
- 位置：`pcl-next\Modules\Base\ModAnimation.cs:56-72`
- 证据：`set { lock (AniControlEnabledLock) _AniControlEnabled = value; }`；getter 取数、加减、setter 写回各走一次独立锁，两次锁之间可并发打断丢失更新；无 `Math.Max(0,…)` 下限钳制，可减为负数，控件动画判定 `== 0` 永久失效。调用方 250 处均为 `+=/-=` 裸运算。
- 建议：提供锁内 `++/--`（或 Interlocked）的 `Enable/Disable` 方法并钳制 ≥0；调用方改走方法。

### S10. 静态动画组字典 `AniGroups` 全程无锁，跨线程 Start/Stop/帧遍历存在竞态【C4】
- 位置：`pcl-next\Modules\Base\ModAnimation.cs:41`；`AniStart :955-982`、`AniStop :994-997`、`AniIsRun :1001-1004`、`AniTimer` 枚举删除 `:1092-1162`
- 证据：`public static Dictionary<string, AniGroupEntry> AniGroups = new();`，Add/Remove/ContainsKey/Values.ElementAtOrDefault/Remove 全文件无 `lock`。三 public 入口可被 UI 线程外调用，`AniTimer` 在 `Dispatcher.Invoke` 回调枚举 `Values` 并按 Uuid 删除；并发 Start/Stop 与帧遍历交叉时桶结构竞争可致条目丢失或 `InvalidOperationException`。VB 同构；UI 线程内串行时不触发。
- 建议：所有字典访问进同一把锁；或限定仅 UI 线程调用；枚举删除改快照后处理。

### S11. 动画 `TimeTotal` 无 `≤0` 守卫，多处时长为 0 除零得 Inf/NaN【C4】
- 位置：工厂签名 `:223/248/273…`（`int Time = 400`）；除零点 `:1187、1328、1340、1350、1394、1409、1415`
- 证据：`TimeFinished / TimeTotal` 做百分比，时长传 0 得 `Infinity/NaN`，缓动 `GetDelta` 与 `Substring` 字数随之失真；工厂仅 `TimeTotal = Time`，无 `Math.Max(Time,1)`。调用方传 0/负时长才命中。
- 建议：工厂内 `Time = Math.Max(Time, 1)`；或 `AniRun` 对 `TimeTotal <= 0` 直接置完成。

### S12. TextAppear 字数 `TextCount` 未钳制，越界缓动下 `Substring` 抛参数异常【C4】
- 位置：`pcl-next\Modules\Base\ModAnimation.cs:1350-1351`
- 证据：`int TextCount = (int)Math.Round(… * Ani.Ease.GetDelta(…)); string NewText = Text.Substring(0, TextCount);` 无 `Clamp(0, Text.Length)`；回弹/弹簧缓动输出越界（>1 或 <0）时 `TextCount` 可大于 `Text.Length` 或为负，`Substring` 抛 `ArgumentOutOfRangeException`，整帧被 `AniRun` catch 吞掉、文字停在中间。`:1355` 再 `Substring(TextCount,1)` 依赖该值。VB 同形继承。
- 建议：`TextCount = Math.Clamp(TextCount, 0, Text.Length)` 后再 `Substring`。

### S13. 皮肤上传命中 401 / 认证失败后 `goto Retry` 无重试上限，可能后台无限循环【MA】
- 位置：`pcl-next\Pages\PageLaunch\PageLoginMsSkin.xaml.cs:127(Retry)、139-143、183-189`
- 证据：`EditSkin` 用 `Retry:` 标签 + `goto Retry`；139 行「requires user authentication」与 188 行 401 都会 `McLoginMsLoader.Start(GetLoginData(), IsForceRestart: true)` 后直接 `goto Retry`，**无最大重试次数/退避**。服务器持续返回 401/认证失败时，后台线程反复重启登录 + 发网络请求，形成死循环。
- 附带可疑：150 行 `ResultJson["skins"]` 缺失时 152 行解引用 NRE（低）。
- 建议：加重试上限（如 2 次），超限走失败提示；复核 finally 中 `IsChanging=false` 与重试计数的关系。

### S14. `MyBitmap` 兜底分支在 catch 块内显式强转 `Bitmap`，资源类型不符时二次抛异常绕过 1×1 兜底【C5】
- 位置：`pcl-next\Modules\Base\MyBitmap.cs:148-150`
- 证据：外层 `catch (Exception ex)` 内 `Pic = (System.Drawing.Bitmap)My.Application.TryFindResource(FilePathOrResourceName);`；若兜底资源实际是 `ImageSource`/`ImageBrush`（WPF 图片资源常见类型），`InvalidCastException` 在 catch 块内二次抛出，不再被外层捕获，走不到后续 `if (Pic is null) { new Bitmap(1,1); … }` 兜底分支。VB `:100` 为晚绑定无显式强转。
- 建议：先 `is System.Drawing.Bitmap` 判断；`ImageSource` 走 `MyBitmap(ImageSource)` 构造；catch 块内再嵌套一层 try 兜底。

---

## 五、🟩 低危（确认 / 可疑，按模块归并）

> 多数为上游 VB 同构继承的健壮性、并发、资源释放、参数边界问题；除标注 ✅确认 外，⚠️ 项需特定边界/并发/异常数据才命中。

### 5.1 外壳 / 主窗体（A1、A2）

| 状态 | 文件:行号 | 问题 | 建议 |
|---|---|---|---|
| ✅低 | `PclXmlImports.cs:10-14` | `XmlImports` 静态类及 `local/x` 两个 `XNamespace` 别名当前 C# 工程内零引用，迁移预留死代码 | 确认后续页面不再用 XML 字面量后删除；保留则注释注明「预留无引用」 |
| ⚠️低 | `FormMain.xaml.cs:917-932`（FormDragMove） | VB `On Error Resume Next` 全吞，C# 只 catch `InvalidOperationException`；`((dynamic)sender).IsMouseDirectlyOver` 动态绑定失败、NRE 等其他异常冒到 Dispatcher | 补兜底 `catch(Exception){}`，或先 `sender as UIElement` 判空取属性、去动态绑定 |
| ⚠️低 | `FormMain.xaml.cs:866-907`（EndProgramForce） | 退出流程 OERN 翻译为分层 catch（Win32/Security/IO/Unauthorized），无兜底 `Exception`；`ConfigUtils.SaveAll/Logger.Flush` 抛其他类型时逃逸、退出中断 | 末尾补 `catch(Exception){}`（进程本就要退出），或确认 SaveAll/Flush 内部已自吞 |
| 备查 | `FormMain.xaml.cs:993/1024` | 背景 `ImageSource` 为 null 时解引用 NRE（VB 同构） | 判空 |
| 备查 | `FormMain.xaml.cs:1467/1470/1475/1477` | `FilePathList.First()` 列表为空时抛 `InvalidOperationException`，先于 `Any()` 判断（VB 同构） | 先 `Any()` 再 `First()` |
| 备查 | `FormMain.xaml.cs:2331` | 枚举 `ModWatcher.McWatcherList` 期间另一线程增删会抛异常；外层 catch 兜底（VB 同构） | `ToList()` 快照 |

### 5.2 主模块 / 事件（B1）

| 状态 | 文件:行号 | 问题 | 建议 |
|---|---|---|---|
| ✅低 | `ModEvent.cs:457` | 「未知事件类型」提示输出枚举编号（整数）而非枚举中文名；VB 插值取成员名，C# 显式 `(int).ToString()` 输出 `1` 这类数字 | 改 `Type.ToString()` |
| ⚠️低(同源) | `ModMain.cs:1083-1085` | `HasFlagF` 为真但 `ModLoaderVersions` 字典缺键时索引器抛 `KeyNotFoundException`；外层无 try/catch（VB 861-863 同源） | `TryGetValue` 兜底空串 |
| ⚠️低(同源) | `ModMain.cs:858`（TimerFool） | 鼠标恰在按钮中心 `Vec=(0,0)`，`Dir.Normalize()` 对零向量抛 `InvalidOperationException`；外层 973 catch 吞掉，当帧按钮不动（VB 690-692 同源） | `Vec.Length==0` 早退 |

### 5.3 音乐 / 备忘 / 密钥（B2）

| 状态 | 文件:行号 | 问题 | 建议 |
|---|---|---|---|
| ⚠️低 | `ModMusic.cs:248-250` | `MusicStartPlay` 异常被空 catch 静默吞掉，无日志 | catch 补 `Logger.Warn(ex,…)` |
| ⚠️低 | `ModMusic.cs:379` | 音频 `TotalTime` 为 0（空/损坏/极短）时除零，进度条 NaN/Infinity | `TotalTime<=0` 跳过进度更新 |
| ⚠️低 | `ModMusic.cs:390/395/400` | 三处 `ex.Message.Contains(...)` 未判消息 null，catch 块内二次 NRE | `ex.Message?.Contains(...)==true` |
| ⚠️低 | `ModMusic.cs:227` | 清空列表旧播放器仅置 `MusicNAudio=null`，未显式 Stop/Dispose；置空到旧线程退出间音频可能继续，跨线程时序未同步 | 置空前 `MusicNAudio?.Stop()`，必要时 Dispose |
| ⚠️低 | `ModSecret.cs:63-64` | 未配 `PCL_CURSEFORGE_API_KEY`（缺省 `""`）仍向 CF/forgecdn 附加空串 `x-api-key` 头，可能被判 401/403 | 仅 key 非空时 Add 头 |
| ⚠️低 | `ModSecret.cs:154-157` | `OneOf<int,int[]>` 数组分支若赋长度 <3 数组，`Deltas[0/1/2]` 越界；当前全库仅赋 int 0，分支不可达，防御性标注 | 数组分支校验长度为 3 |
| ✅低(不可达) | `ModSecret.cs:46/48` | `SecretHeadersSign` 假定 `Url/Req` 非空；Url null 时 `Url.Contains` NRE。已核调用链先 `new HttpRequestMessage(Method,Url)`，Url null 先在构造抛 ArgumentNullException，当前不可达 | 入口补 null 判空返回 |

### 5.4 网络层（C1）

| 状态 | 文件:行号 | 问题 | 建议 |
|---|---|---|---|
| ✅低 | `NetInterop.cs:64-67` | `HeaderProvider` 每次为候选 URL `new HttpRequestMessage`，取完头即丢弃未 Dispose；引擎每候选源调用一次 | `using var Req = …;` 提取包头 |
| ⚠️低 | `NetInterop.cs:117` | 源错误按下标回填 `File.Sources[i]`，`SourceErrors` 少于 `Sources` 时剩余源不标 `IsFailed`；`Reused` 为 null 仍无条件 `File.Finish()` | 未映射源统一标失败并挂兜底异常；Finish 前核对报告状态 |
| ✅低 | `NetInterop.cs:98/110` | `SizeMirrored` 布尔由 `Progress<T>` 回调读写无锁；无线程同步上下文时走线程池，重复 `Exchange` 同值（后果轻微） | 改 Interlocked 标志位或在捕获上下文回调 |
| ✅低 | `ResilientCacheStore.cs:72-76` | `AddOrUpdateAsync` 直接改调用方传入 `Response` 的 `TransferEncodingChunked`；消息若在别处复用，头被副作用修改 | 进入缓存链路时复制消息再改头 |
| ✅低 | `ModNet.cs:470-473` | `UpdateNetTaskSpeedLimitHigh(int Value)` 形参未使用，只把缓存置空触发重算，新值被忽略 | 删 `Value` 参数或按值写入 |
| ✅低 | `ModNet.cs:2343` | `Ping` 形参 `Timeout=10000` 未生效（`new Ping().Send(Ip)` 走默认超时）；Ping 对象未 Dispose（VB 同形） | `using var ping=new Ping(); ping.Send(Ip,Timeout);` |
| ⚠️低 | `ModNet.cs:1150-1154` | 链表插入遍历未防 null 末节点；`StartPosition` 超末线程结束位置时走到 `NextThread=null` 后下一轮解引用 NRE（当前起点均落既有区间） | 循环条件追加 `NextThread is not null`，找不到回退末位 |
| ⚠️低 | `ModNet.cs:1258-1262` | 遍历 `DriveInfo.GetDrives()` 取 `TotalFreeSpace`，未就绪驱动器（光驱/断网盘）访问属性抛异常，被外层当网络故障 | 每盘单独 try，不可用盘跳过 |
| ✅低 | `ModNet.cs:1736-1745` | `_CheckExistingFile_Sizes` 静态字典只增不清，外部改动后大小缓存陈旧；键为完整路径，进程重启失效 | 按 LastWriteTime 失效或加淘汰 |
| ⚠️低 | `ModNet.cs:1321-1322` | 限速等待循环无超时；余量由 StatRefresher 每 100ms 补充，刷新线程异常退出则下载线程永久阻塞 | 等待循环加超时/退避，刷新线程异常时复位限速 |

### 5.5 基础模块 ModBase（C2）

| 状态 | 文件:行号 | 问题 | 建议 |
|---|---|---|---|
| ⚠️低 | `ModBase.cs:916/921/932-935` | 查询串全空格 `Split` 得空数组，`QueryParts.All(...)` 空集恒 true，空查询下全部按完全匹配返回 | 空/纯空格 Query 直接返回空结果 |
| ⚠️低 | `ModBase.cs:845-855` | 所有权重为 0（或 Source 空）时 `TotalWeight=0`，`Sum/TotalWeight` 得 NaN/Inf；NaN 被过滤、Inf 异常抬高排序 | `TotalWeight==0` 返回 0d |
| ⚠️低 | `ModBase.cs:920-921/851` | 搜索链 null 解引用：`SearchSource` null 时 foreach NRE；`Aliases` null 时 `Any()` NRE；别名含 null 元素时 `Alias.Replace` NRE | 构造默认空集合；入口判空；别名元素过滤 null |
| ⚠️低 | `ModBase.cs:593-598` | `IniClearCache(FileName=null)` 无 try/catch，`FileName.Contains(@":\")` NRE；对比 `IniGetContent:606-632` 有兜底 | 入口 `IsNullOrEmpty` 返回 |
| ⚠️低 | `ModBase.cs:1001-1015` | `UuidLock` 锁外惰性赋值，双线程可各自 `new object()` 短暂绕过互斥；`Uuid+=1` 在不同锁上并发，编号重复概率 | 字段初始化器 `=new object()`，去惰性赋值 |
| ⚠️低 | `ModBase.cs:1425/1432/1440/1451` | 静态共享 `Random` 非线程安全；后台/UI 并发 `NextDouble` 可能损坏内部状态（重复值/异常） | lock 包裹或 `ThreadLocal<Random>` |
| ⚠️低 | `ModBase.cs:360-367/389-396` | `MyColor(string)`：非法串/null 解析返回 null 或抛异常后 `.A` NRE；`MyColor(Brush)`：非 SolidColorBrush 强转抛 InvalidCastException；公开入口 | 判空回退默认色；Brush 先 `is SolidColorBrush` 判断 |
| ⚠️低 | `ModBase.cs:487-490` | `FromHSL2` 亮度 `center` 未钳制，`sS` 超范围时 `center≤0`，`sL/center` 除零 NaN/Inf | `center` 下限保护；`sS/sL` 入口钳制 |
| ⚠️低 | `ModBase.cs:994-999/1079-1085` | `RunAsAdmin` 取得 Process 后 `WaitForExit` 未 Dispose；`StartProcess` 返回进程在 OpenWebsite/OpenExplorer 路径同样不释放 | 调用点 `using var`；文档注明释放责任 |
| ⚠️低 | `ModBase.cs:1318` | 静态 `DPI=Graphics.FromHwnd(0).DpiX`，GDI Graphics 未 Dispose；且只首次取一次，多显示器不同 DPI 不刷新 | `using` 包裹 Graphics；多屏按控件取 |
| ⚠️低 | `ModBase.cs:1394-1400` | `IsVisibleInForm` 未判 element/`FrmMain` null；元素未入可视树时 `TransformToAncestor` 抛 InvalidOperationException，无 catch | 入口判空返回 false；调用包 try |
| ⚠️低 | `ModBase.cs:1406-1411` | `IsTextTrimmed`：TextBlock null NRE；Foreground null 时 FormattedText 构造可能异常；FontSize 0/负无兜底 | 判空返回 false；前景回退；字号钳制 |
| ⚠️低 | `ModBase.cs:1190-1203` | 命令行按单空格拆分，引号包裹含空格参数被截断；Name null 拼出 `"-null"` 仍参与比较；无 `--key=value` 解析 | 按引号/等号分词；入口判空 |
| ⚠️低 | `ModBase.cs:127` | `GetLogicalDrives().First(p=>Directory.Exists(p))` 无可用盘符时抛 InvalidOperationException（正常 Windows 必有系统盘，理论边界） | `FirstOrDefault` + 空值回退 `C:\` |

### 5.6 加载器 / 校验（C3）

| 状态 | 文件:行号 | 问题 | 建议 |
|---|---|---|---|
| ⚠️低 | `ModLoader.cs:423-433/450-466` | `ShouldStart` 输入委托抛异常已置 `State=Failed/Error=ex`，但 `Start` 只看返回值；随后返回 true 时 State 被覆盖回 Loading，Error 残留，成功结束 `WaitForExit` 按 Finished 返回不暴露输入异常 | catch 后短路返回；或成功结束清空残留 Error |
| ✅低 | `ModLoader.cs:266-279` | `CreateCancellationToken` 复用/重建 CTS 时旧 CTS 从不 Dispose，旧 `Token.Register(Cancel)` 回调不注销；反复创建泄漏 CTS 与委托 | 保存 CancellationTokenRegistration，重建/释放时 Dispose 旧 CTS 并注销 |
| ✅低 | `ModLoader.cs:574`（setter 552-568） | `LoaderWorker` 构造订阅 `Worker.Progress.ProgressChanged`；Worker setter 只退订 Succeeded/Canceled/Failed，不退订进度事件；替换 Worker 后旧进度回调仍更新本加载器 | setter 一并退订旧 Worker 进度事件，保存委托引用 |
| ✅低 | `ModLoader.cs:983/1007-1032` | 静态 `LoaderFolderDictionary` 按 Loader 为键记录目录修改时间，条目从不移除，任务结束/释放后持续累积 | 完成/取消后按键移除，或弱引用键 |
| ⚠️低 | `ModLoader.cs:734/867` | 组合任务 `Failed/Update` 直接访问 `ModMain.FrmMain.BtnExtraDownload.ShowRefresh()` 无 null 判空；窗口未就绪/关闭后 worker 线程 NRE，两处无外层 catch | `FrmMain?.BtnExtraDownload?.ShowRefresh()` 或包 try |
| ✅低 | `ModLoader.cs:346`（对照 312） | `WaitForExitTime` 走 `throw Error;` 重抛重置堆栈；`WaitForExit` 用 `new Exception(Error.Message,Error)` 保留内部异常；两等待方法错误语义不一致 | 统一 `throw;` 或统一包带内部异常的新异常 |
| ⚠️低 | `ModValidate.cs:169` | `ValidateInteger` 先取 `Str.Length>9` 未先判空，null 时 NRE（`int.TryParse(null)` 本可容错） | 入口补 null 判空 |
| ⚠️低 | `ModValidate.cs:235` | `ValidateExcept` `Str.IndexOfF(Ch,true)`，`IndexOfF` 非 null 接收，Str null NRE | 入口判空或改 `string?` 重载 |
| ⚠️低 | `ModValidate.cs:494` | `ValidateFolderPath` 首行 `Str.Replace("/",@"\")` 先于空值检查（`:498`），null 时 Replace 处 NRE，无外层 try | Replace 放到 null/空白校验之后 |
| ⚠️低 | `ModValidate.cs:278` | `ValidateExceptSame` 忽略大小写分支 `Ch.Lower()`，`Lower()` 非 null 接收；Excepts 混入 null 字符串时先 NRE，`?? ""` 兜底不到 | 比较前判 Ch 空，或 `(Ch??"").Lower()` |
| ✅低 | `ModValidate.cs:169-170` | 长度预检 `Str.Length>9` 即判「大小合理」，10 位整数（如 `2147483647`）在 TryParse 前被拒；阈值与 Min/Max 无关 | 按 Min/Max 位数估算或去掉长度预检 |

### 5.7 动画（C4）

| 状态 | 文件:行号 | 问题 | 建议 |
|---|---|---|---|
| ⚠️低 | `ModAnimation.cs:740-756` | `AniEaseInout` 参数 `EaseInPercent` 无 `[0,1]` 钳制；传 0 时 `t/0`、传 1 时 `(t-Ep)/(1-Ep)` 除零 NaN/Inf；内部组合类安全，公开构造可越界 | 构造 `Clamp(0.01,0.99)`；判 EaseIn/EaseOut 非空 |
| ⚠️低 | `ModAnimation.cs:830-836` | `AniEaseOutFluentWithInitial` 构造除零：TotalDistance/TotalSecond 为 0 时 `v0_norm` 除零，alpha NaN 后整段缓动失效 | 分母 0 时 alpha=0 退化线性 |
| ⚠️低 | `ModAnimation.cs:524` | `AaColor(…,string Res)` 工厂期即 `FindResource(Res)`，资源键缺失抛 `ResourceReferenceKeyNotFoundException`；`Obj.GetValue(Prop)` null 时 MyColor 构造可能 NRE | 工厂判空/try 回退默认色；资源解析延后首帧 |
| ⚠️低 | `ModAnimation.cs:550` | `AaScale` 相对百分比读 `((dynamic)Obj).ActualWidth/Height`；控件未布局时值 0，缩放矩形全 0 静默无效；Obj null/非 FrameworkElement 时 dynamic 抛 RuntimeBinderException | 判空与类型判断；未测量回退 Width/Height 快照 |
| ⚠️低 | `ModAnimation.cs:963-965` | `AniStart(IList)` 嵌套元素强转 `(IEnumerable<AniData>)Element`；传 object[]/类型不匹配集合抛 InvalidCastException；Element null 时 Add 分支 NRE（VB 晚绑定，C# 收紧为泛型强转） | 先判 `is IEnumerable<AniData>`，否则 `Cast`；空元素跳过 |
| ⚠️低 | `ModAnimation.cs:1125`（执行体 1178-1412） | 动画 `Obj` 直接持有控件/依赖属性；控件卸载后动画组仍在字典，每帧对已卸载元素读写 Margin/Opacity/Transform；AniStop 只删字典项，不回滚终态/清理 RenderTransform/无卸载退订 | 帧内判 `IsLoaded==false` 移除动画；停止/卸载清理自建 Transform |
| ✅低(死路径) | `ModAnimation.cs:571`（运行 1370-1373） | 非 TextBlock 分支读写 `((dynamic)Obj).Context`；WPF 标准控件（含 Label）无 Context 成员，走到即 RuntimeBinderException；全仓 `AaTextAppear` 仅工厂自身无调用方 | 非 TextBlock 改 `Content`，或删分支限定 TextBlock 入参 |
| ⚠️低 | 工厂兜底 `:230/255…`；解引用 `:1187/1328/1340/1394/1409` | `AniData` 公开 struct，外部可直接构造且 `Ease=null`；`AniRun` 内 `Ani.Ease.GetDelta` NRE，被帧级 catch 吞掉；工厂 `Ease ?? new AniEaseLinear()` 仅在工厂 | `AniRun` 入口 `Ease ??= new AniEaseLinear()`，或工厂化封装 |
| ⚠️低 | `ModAnimation.cs:687-697` | `AaStack` 遍历 `Stack.Children` 置 `((dynamic)Item).Opacity=0`；Stack null NRE；子项非 UIElement（无 Opacity）dynamic 抛异常；工厂期即执行 | 判 Stack 非空；子项判 `is UIElement` |

### 5.8 MainAgent 自审补充（MA，未被子 agent 覆盖）

| 状态 | 文件:行号 | 问题 | 建议 |
|---|---|---|---|
| ⚠️低 | `ModWatcher.cs:21/28/119-127` | 普通 `List<Watcher>` 被构造线程与各日志线程并发读写/重建，无锁；并发枚举/增删可能抛 InvalidOperationException 或脏状态（上游 VB 同构） | 列表访问加锁，或并发集合 + 快照遍历 |
| ⚠️低 | `ModMinecraft.cs:595-622`（另见 2186-2190/2569-2571/2617-2619；`ModDownload.cs:30-31/58-59`） | 继承链合并只防自环（614 判父版本继承自身），未防 A→B→A 多节点互继承环，递归 `new McInstance` 可能栈溢出；619 行父版本缺 `libraries` 键返回 null，`foreach` NRE。触发前提为外部版本 JSON 互继承/父版本无库数组，正常官方/加载器版本不命中，VB 同构 | 合并循环带已访问名集合防环；619 前判 `libraries is JArray` |
| ⚠️低 | `Application.xaml.cs:116` | `--gpu` 无第二参数时 `e.Args[1]` 越界；被 119 catch 兜住 Exit Fail，不崩溃但无用户可见错误提示（VB 同构） | 先判 `e.Args.Length>1` |
| ⚠️低 | `ModWatcher.cs:207` | `Instance` 为 null 时 `Instance.ReleaseTime.Year` NRE（需确认是否存在不传 Instance 的调用路径） | 判空 |

### 5.9 基础杂项（C5）

| 状态 | 文件:行号 | 问题 | 建议 |
|---|---|---|---|
| ⚠️低(同源) | `MyBitmap.cs:216-227`（Clip）、`:232-238`（RotateFlip） | `Clip` 无 `Pic` 判空、无 `X/Y/Width/Height` 边界校验：Pic null 时 `Pic.PixelFormat` NRE；负尺寸/越界坐标出空白图或 GDI+ 异常；`RotateFlip` 同样不判空（VB 同形） | 入口判 `Pic is null`；宽高钳制非负并与源尺寸比较；越界坐标回退 |
| ⚠️低(同源) | `MyBitmap.cs:126-143`（非资源分支） | `new System.Drawing.Bitmap(InputStream)` 构造后 `using` 结束即释放 FileStream；GDI+ Bitmap 内部持有流引用，后续像素/元数据访问可能抛「Generic GDI+ error」。VB 同形「自接管 FileStream 解除占用」写法与 Bitmap 流生命周期冲突 | 先 `ReadAsBytes` 拷贝到 `MemoryStream` 再构造 Bitmap；或让 Bitmap 持有流直至释放 |
| ⚠️低(同源) | `MyBitmap.cs:200-208` | `WebPDecoder.NativeLibraryReady` 普通 bool，非 volatile、无锁；两线程同时首次解码 WebP 均读到 false，可能重复 `ExtractLibwebp()` 释放原生库。当前 UI 串行加载，风险低 | 初始化放静态构造或加锁；标志改 volatile 语义 |
| 备注 | `ModDownloadPolicy.cs:121/157/189`；`MyCompat.cs:149/95-101`；`PclLogger.cs:19` | 观察项不构成缺陷：中文字面量分支偶合（语义正确）、`?? ""` 死代码（非空）、UTF-8 读 ASCII 键值、无显示器会话 PrimaryScreen 兜底、内存失败降级 0、单例下实例字段≈VB Static（理论可见性竞态，影响可忽略） | 后续随重构改枚举/状态位，当前不改 |

---

## 六、上游已知、C# 侧复核仍在（留痕，不计新 bug）

> 下列已登记在《PCL2上游遗留问题清单.md》；本轮复核确认 C# 侧仍在或形态变化，仅给新行号，不重复立 bug。

| 类别 | C# 侧位置 | 复核结论 |
|---|---|---|
| net48 注册表检查 + `OldEnvironmentAssert` 弹 net48 离线安装页（清单 §五 P1） | `Application.xaml.cs:89-97`、`:369-380`；异常诊断分支 `:337-340` | ✅仍在。net10 自包含不需要 net48，低版本/损坏误报会误导用户；随上游 P1 移除注册表断言与 net48 跳转 |
| `ServicePointManager` 全局调参为 net48-era 设置（§五/§四） | `Application.xaml.cs:187-191`；`ModNet.cs:2372` | ✅仍在。net10 HttpClient/SslStream 不完全走此路径，随网络层重写清理 |
| Win7 `Ssl3|Tls|Tls11|Tls12` 不可达回退分支（#8519） | `Application.xaml.cs:192-195` | ✅仍在。net10 不支持 Win7，分支不可达且 Ssl3/Tls11 不安全；保留 Tls12+ 默认即可 |
| `NetRequestByClientRetry` 的 `BackupUrl` 形参从未使用（L4） | `ModNet.cs:73`；函数体 `:87/92/101` | ✅仍在。XML 注释承诺失败换备用 URL，三处调用只传 Url；删参数或实现切换 |
| 三级重试超时硬编码 10s/30s/4s（§四） | `ModNet.cs:87/92/101`（`:97` 5500ms 门槛） | ✅仍在。弱网/高延迟不可配，随网络层参数化 |
| 线程上限 `ToolDownloadThread+1`（产品策略） | `ModNet.cs:432` | ✅仍在，保留随策略评审 |
| 失败熔断阈值靠常数 `NetTaskThreadLimit.Clamp(5,30)`（§四） | `ModNet.cs:1433` | ✅仍在，旧式策略，新引擎验证后废弃 |
| 多线程引擎域名白名单 + 碎片阈值常数 `FilePieceLimit=262144`（§二 FUTURE/§四） | `ModNet.cs:1116-1117`、`:1000` | ✅仍在，引擎待重做 |
| 缓存两层落盘策略（FileStore HttpV2 + ResilientCacheStore） | `ModNet.cs:310-316`；`ResilientCacheStore.cs:39-78` | ✅保留，损坏缓存隔离正常，无新缺陷 |
| 反馈/赞助/协议外链指向上游（§七、#469、#960） | `ModMain.cs:1234`；`FormMain.xaml.cs:398/597/877` | ✅仍在。随合规/仓库归属口径统一替换 |
| 联机复活赛短路死代码（#960） | `FormMain.xaml.cs:1313` | ✅C# 侧已删逻辑仅留注释 |
| 主窗体与 FrmSetupUI 控件耦合（#813） | `FormMain.xaml.cs:1091/1105/1119/1133/1152` | ✅同构保留，待解耦抽公共状态 |
| 长路径拖拽 MAX_PATH 截断（#9370） | 入口 `FormMain.xaml.cs:1467`；截断在 `DragHelper.cs:162` | ✅截断点在拖拽钩子侧 |
| 任务管理浮动按钮 Click 漏订（#9369） | `FormMain.xaml.cs:522/2298-2301` | ✅迁移侧已补订闭环 |
| 下载限制文案两端硬编码串（依赖同源） | `FormMain.xaml.cs:1531` ↔ `ModModpack.cs:191`、`PageDownloadInstall.xaml.cs:85` | ✅逐字一致；建议改枚举/异常类型判定 |
| 加解密/签名裁剪壳（§六） | `ModSecret.cs:35-40/44-65/179-182/183-186/227-229/230-232/237-240/219-225/210/213` | ✅仍在。CDN 签名/鉴权缺失，`ThemeUnlock=false`、`GetIdentify=0000-…`、`CurrentRank=None`、`ServerConfig=null` 等为开源裁剪预期 |
| `VersionBranchMain="OpenSource"`、注册表隔离 `PCLDebug` | `ModSecret.cs:21/23` | ✅常量一致 |
| Referer 伪造域名 `http://{VersionCode}.open.pcl2.server/`（§七） | `ModSecret.cs:62` | ✅仍在，非浏览器头路径一律附加 |
| 自动更新器下线、改 GitHub Releases（§七，已处置） | `ModSecret.cs:194-197` | ✅形态仍在，无内置更新服务器链路 |
| NAudio `Volume` 过时 API（§四，技术债） | `ModMusic.cs:376` | ✅仍在，依赖升级换 `WaveVolume` |
| 动画自建后台线程手动调度 + `Sleep(1)` 控帧（§四） | `ModAnimation.cs:1026-1078` | ✅同构保留，UI 重写换 `CompositionTarget.Rendering` |
| 远程 XAML 类型/成员黑名单（§七供应链） | `ModBase.cs:1356-1366` | ✅保留兜底；默认关闭远程主页/签名白名单属产品决策 |
| 版本常量注释与机制不符（§八，部分修正） | `ModBase.cs:31`（`:34-40` 已补脚本写入说明） | ✅旧注释保留，仅需订正措辞；常量仍由发布脚本写入，勿手改 |
| #9440 启动取消状态残留 | 主链路 `ModLaunch.cs:2737-2741`；任务链 `ModLoader.cs:502-504/517` | 任务链取消标志处理与 VB 一致；主链路取消标志是否复位需结合 Loader 生命周期另核 |
| #9533 Java 列表失败读 LastResult（L1 部分残留） | `ModJava.cs:560-564` | 见 M5，首次判断已补、重试失败路径未补 |

---

## 七、误报与已排除项（核对后不成立）

- **`ModValidate.cs:371/446/534` 字符数组直接拼接**：疑点不成立——三处均为 `new string(Path.GetInvalidFileNameChars()) + …`，`char[]` 未再直接拼字符串，非法文件名字符拦截正常（C3 2.2 核对）。
- **`ModValidate.cs:144` ValidateHttp null NRE**：`EndsWithF/StartsWithF` null 安全，`RegexCheck` 内部 try/catch 兜底，null 走「网址无效」分支，不抛 NRE（C3 2.2）。
- **`ModValidate.cs:383-390` 重名枚举吞异常**：构造仅吞目录枚举三类异常按「无忽略项」处理；`Validate` 外层有 catch 记日志返回错误信息，非静默吞掉（C3 2.2）。
- **`Logger.Warn/Error(Exception,string)` 重载**：MeloongCore Logger 同时有 `Warn/Error(Exception,string)` 与 `Error(string,LogBehavior)` 重载，pcl-next 调用签名匹配，非 bug（MA）。
- **`FirstOrDefault(谓词1,谓词2)`**：MeloongCore EnumerableExtensions 为「按优先级依次尝试」的 params 重载，与 `ModJava.cs:567`「优先指定 Component、回退范围」语义一致，非 AND 误用（MA）。
- **`ValidateFolderName` 枚举目录失败吞三类异常**：注释与逻辑一致，等价「无忽略项」，非吞异常（MA）。
- **`ModBuildChannel` 条件编译链**：Alpha/Beta/Rc/Snapshot/Slush/Dev 分支齐全，枚举强转、ChannelIsOfficial 正确；渠道偏移递增保留（MA）。
- **`ResilientCacheStore` 缓存损坏处理**：读异常后删条目返回 null，去 `Transfer-EncodingChunked` 正确（MA）。
- **`FileUtils.ReadAsBytes`**：用 `CopyTo` 读满，非单次 Read；编码回退（UTF8→GB18030）、重试、原子写均正确（MA）。
- **`ConcurrentList`**：粗粒度锁线程安全，枚举返回副本；隐式转换复制无竞态（MA）。
- **`ModDownloadPolicy`**：解析/ini 读取/锁均正确，`?? ""` 冗余无害（MA）。
- **`ModLoader.cs:437` 空值不等 XOR**：C# 空值语义等价 VB `Is Nothing <> Is Nothing`，值类型走 Equals 分支，非分支反转（C3 2.2）。
- **`ModBase.cs:962/975` `TargetTask.Run()`**：是 MeloongCore 扩展（GetAwaiter().GetResult()）阻塞取结果，非 `Task.Run` 实例方法，语义正确（C2 1.3）。
- **`MyColor` 相等性无递归**：`==/!=` 用 `is null` 判空，`Equals` 复用 `==` 而非 VB 运行时比较，无重载自调用栈溢出（C2 2.2）。
- **`Settings.Get<T>` 读取失败**回退 DefaultValue；`ContainsKey` 等扩展 null 行为已核对（A2 2.2、B2 2.2）。
- **`ModMusic.cs:48/400` 扩展名大小写**：`PathUtils.GetExtension` 已 `Lower()`，无需再忽略大小写（B2 2.2）。
- **`IsSingle()` null 安全**：输入 null 返回 false（EnumerableExtensions.cs:372-373）（B2 2.2）。
- **`EndsWithF` null 安全**：扩展方法 null 回 false（StringExtensions.cs:24-25）（B2 2.2）。
- **`ModLaunch.cs:226/231/236`、`ModJava.cs:567`**：`&&` 优先级高于 `||`，与注释意图一致，非括号缺失（MA）。

---

## 八、编译警告归类（30 条，技术债 / 潜在风险）

> 来自本轮 Release 编译（0 错误 / 30 警告）；均非阻断问题，列入技术债跟踪。

| 警告码 | 位置 | 说明 / 处置方向 |
|---|---|---|
| SYSLIB0014 ServicePointManager 过时 ×7 | `Application.xaml.cs:187-191/194`；`ModNet.cs:2372` | net10 上设置不影响 HttpClient/SslStream，随网络层重写移除 |
| SYSLIB0021 DESCryptoServiceProvider 弱加密过时 | `Pages\PageSetup\Settings.cs:391/413` | DES 为弱加密 + 过时 API，建议换 `Aes.Create` |
| SYSLIB0037 AssemblyName.ProcessorArchitecture 过时 | `Pages\PageLink\PageLinkMain.xaml.cs:533` | 现代运行时不支持该成员，找替代判定 |
| CS0618 SecurityProtocolType.Ssl3 过时/不安全 | `Application.xaml.cs:194:60` | Win7 不可达分支内，随分支删除 |
| CA2022 Stream.Read 单次未读满 ×2 | `MyBitmap.cs:130/136` | 见 M6，循环读满 / `ReadExactly` |
| WFO0003 app.manifest 高 DPI 设置待迁移 | app.manifest | 改用 `ApplicationHighDpiMode` 项目属性 / API |
| NU1510 多余 `Microsoft.Win32.Registry` 包引用 | `MeloongCore.csproj`（被主工程传递） | 自动可用，可移除显式引用 |

---

## 九、本轮（J1/J3/J4）中危修复落地记录

> 编译佐证：`dotnet build pcl-next\pcl-next.csproj -c Release -r win-x64 --self-contained -p:PclChannel=Dev`，**0 错误 / 30 警告**，回到基线；产物 `pcl-next\bin-net10\Dev\2.100.0.6-0.82\win-x64\pcl-next.dll`。
> 口径：C# 迁移回归（空容忍丢失、事件订阅模型差异、行为漂移）优先补空判/防护；上游 VB 同构继承的健壮性缺口补轻量空判；低危与内核/网络项不在本轮修复前提内。

### 10.1 已落地补丁文件（相对 `pcl-next\`）

| 文件 | 缺陷类型 | 修复口径 |
|---|---|---|
| `Pages\PageDownload\ModDownloadLib.cs` | 列表条目 `type/id/url/releaseTime` 空容忍丢失；zip 缺 `install_profile.json`/`version.json` 条目判空；MOJMAPS `client/mappings` 字段与 `Split("@")[1]` 越界；Legacy Forge 缺字段/zip 条目链式 NRE；Fabric `version/stable/Display.Split("]")[1]` 防护；多处 `JsonToken/url/version/id/minecraftArguments/Substring/First()` 空判 | 键值统一 `?.ToString() ?? ""`；zip 条目 `is null` 早退并抛损坏包异常；`Split` 结果先判 `Length>1` 再取下标；缺字段链式访问前判空 |
| `Pages\PageDownload\PageDownloadInstall.xaml.cs` | `releaseTime/id/version` 缺键 NRE；正式/预览空列表 `[0]` 越界；Fabric `game/loader` 节点与 `Tag`、`TryInit`、LiteLoader `version`、原版 `url` 空判 | 日期/版本取值前判空，空列表早退；节点/标签空判后再取子项；缺失键走安全日期与空守卫 |
| `Pages\PageDownload\PageDownloadModify.cs` | 属性 setter 匿名 lambda `-=` 无法移除旧委托、二次赋值事件泄漏；静态 `MyTextBox.ValidateChanged` 多实例累积；还原已装版本路径缺 `id/version` NRE；各加载器 `Output.Value` 空判 | 改为实例字段缓存 + 三个具名处理器（`OnNameTextChanged`/`OnValidateChanged`/`OnOriginalNameTextChanged`），setter 用同一实例退订重订；`Output` 为 struct 不可空，仅对 `Value`（List/JArray/JObject）判空，去掉误用的 `?.` |
| `Pages\PageSelectLeft.xaml.cs` | 历史 `LaunchFolders` 不含 `>` 时 `Split(">")[1]` 越界（两处） | 先 `Contains(">")` 判空，再 `Split(">")` 取 `[0]/[1]` |
| `Pages\PageDownload\Resource\MyResourceItem.xaml.cs` | 五个空桩下载子页静态字段未初始化时 `.Content.Loader...` 空引用 | 字段判空后用 `?.` 贯穿到 `Content?.Loader?.Input`；字段非空时链路安全 |
| `Pages\PageDownload\Resource\PageResource.xaml.cs` | 结果列表为空 `Index=-1` 致 `GetRange(-1,1)` 越界 | `Count==0` 早退空态渲染，隐藏页码/错误提示后返回 |

### 10.2 核对后无需改动项

- `Pages\PageOther\PageOtherTest.xaml.cs`（J3-1 百宝箱桩）：对照 VB 后确认桩内**已有** `ModMain.Hint("为便于维护，开源内容中不包含百宝箱功能……")`，与 VB 一致；报告判定的"C# 静默空实现、无提示"与现状不符，**未改动**。

### 10.3 未修范围（事实陈述）

- J1 剩余低危（`Task.Input` null、除零不可达、`Substring` 前缀、`Init` null、`Parent` null、空桩、`Content` 未赋值等）；J2 实例页、J3 设置/其他页、J4 资源/登录其余低危；主汇总 M1–M8、S1–S14 内核/网络项尚未逐条复核修复。
- 内核工程 `PCL.Download/PCL.Cli/PCLCS/MeloongCore` 仅核对签名与 null 语义，未深审内部；`*.vb` 不编译仅作语义对照。

---

## 十、附录：分组覆盖清单

### 9.1 并行子 agent 分组（8 份子报告，原文在 `docs/bugscan/`）

| 组 | 子报告 | 覆盖文件 | 结论摘要 |
|---|---|---|---|
| A1 外壳入口 | `A1-外壳入口.md` | `Application.xaml.cs`(469)、`PclEntryPoint.cs`(21)、`PclXmlImports.cs`(15) | 1 低确认（XmlImports 死代码）；4 上游已知留痕；单例/缓存重试/全局异常/字符串数值语义核对无回归 |
| A2 主窗体 | `A2-主窗体.md` | `FormMain.xaml.cs`(2383) | 2 低可疑（两处 OERN catch 收窄）；9 上游已知；事件接线/空判/快照/懒加载/封送/资源释放核对无回归 |
| B1 主模块事件 | `B1-主模块事件.md` | `ModMain.cs`(1342)、`ModEvent.cs`(582) | 1 中可疑（Feedback catch 面窄）、1 低确认（枚举编号化）、2 低可疑同源（字典缺键、零向量）；遥测无残留 |
| B2 音乐备忘密钥 | `B2-音乐备忘密钥.md` | `ModDevelop.cs`(空壳)、`ModDevNotes.cs`、`ModMusic.cs`、`ModSecret.cs` | 2 中可疑（列表半初始化、主题刷新空引用）、7 低（空 catch/除零/消息判空/播放器释放/空 key 头/数组越界/签名空假设）；6 上游留痕 |
| C1 网络层 | `C1-网络层.md` | `ModNet.cs`(2395)、`NetInterop.cs`(129)、`ResilientCacheStore.cs`(97) | 3 中确认（CTS 泄漏、s.Ex NRE、计数非原子）、2 中可疑（共享竞态、Reset 偏差）、10 低；7 上游已知；资源释放闭环/进度 NaN 防护/合并去重/取消防重入核对无回归 |
| C2 基础 ModBase | `C2-ModBase.md` | `ModBase.cs`(1460) | 1 中可疑（UseShellExecute 翻转）、1 中确认（资源 null NRE）、14 低；4 上游留痕；MyColor 相等性/ini 缓存/超时包装/剪贴板/XAML 黑名单/排序/时间戳核对无回归 |
| C3 加载器校验 | `C3-加载器校验.md` | `ModLoader.cs`(1062)、`ModValidate.cs`(552) | 1 中可疑（组合 Update 并发无锁）、10 低（状态残留/CTS 与事件泄漏/字典累积/FrmMain NRE/重抛不一致/多处 null 与顺序/长度阈值）；10 组误报排除 |
| C4 动画 | `C4-动画.md` | `ModAnimation.cs`(1425) | 5 中可疑（AniRunning 死标志、计数非原子无下限、字典无锁、时长除零、字数未钳制）、9 低（缓动除零/资源 null/dynamic/集合强转/卸载残留/死分支/Ease null/Stack null）；1 上游已知 |
| C5 基础杂项 | `C5-基础杂项.md` | `ModBuildChannel.cs`(188)、`ModBuildInfo.cs`(72)、`ModDownloadPolicy.cs`(207)、`MyBitmap.cs`(254)、`MyCompat.cs`(261)、`PclLogger.cs`(96) | 1 中确认回归（缓存字典降为实例字段）、1 中可疑（catch 内强转）、4 低（Clip/RotateFlip 判空、Bitmap 流生命周期、NativeLibraryReady 并发、观察备注）；渠道常量/下载策略已处置复核无回归 |

### 9.2 MainAgent 自审覆盖

- 基础/支撑：`Logger` 重载、`RedoableWorker.LastResult` 语义、`ValueRange.Intersect/IsEmpty`、`EnumerableExtensions.FirstOrDefault`/`IsSingle`、`StartsWithF/EndsWithF/IndexOfF/Lower/Upper` null 行为、`FileUtils`、`ConcurrentList`、`ModDownloadPolicy`、`ModBuildChannel`、`MyCompat`、`PclLogger`。
- 核心链路：`ModJava.cs`、`ModWatcher.cs`、`ModValidate.cs`、`ModNet.cs`（片段）、`Application.xaml.cs`、`ModLaunch.cs`（取消/进程）、`ModDownload.cs`、`ModMinecraft.cs`（继承链）、`Settings.cs`（DES 段）、`PageLoginMsSkin.xaml.cs`。
- 编译：跑通一次 Release build（0 错误 / 30 警告）作为佐证；产物在测试三段树 `bin-net10\Dev\2.100.0.6-0.82\win-x64\`。

### 9.3 本轮未深审 / 边界声明

- 内核工程 `PCL.Download`(46)、`PCL.Cli`(13+Commands)、`PCLCS`(9)、`MeloongCore`(42)：仅核对被调签名/扩展语义，未逐行深审内部缺陷；测试工程 `PCL.Download.Tests`、`MeloongCore\Tests` 浅扫。
- `Pages/`、`Controls/` 多数文件未在子 agent 分组内逐一通读（FormMain、登录皮肤页、部分设置/链接页除外）；`ModCrash.cs`(93.9KB)、`ModModpack.cs`(66.2KB)、`ModMinecraft.cs`(138KB) 仅关键片段核对，未逐行全读。
- 已被 csproj `<Compile Remove>` / `DefaultItemExcludes` 排除的 cs/vb、生成目录 `bin/obj/bin-net10/ReleaseArtifacts/publish/.git/downloads/.screenshots` 均不纳入。
- 本轮为**只读排查**：未修复任何代码；可疑项建议经运行/反编译/单测复核后再定整改。
