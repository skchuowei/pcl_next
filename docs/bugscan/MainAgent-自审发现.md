# MainAgent 自审发现（与子 agent 并行排查的对照底稿）

> 范围：`PCL_NEXT_Windows`（dev.6-Windows），只读静态审查 + 一次 Release 编译佐证。
> 编译结果：**0 错误 / 30 警告**（`dotnet build pcl-next.csproj -c Release -r win-x64 -p:PclChannel=Dev`）。
> 主体为参与编译的 `.cs`；`.vb` 仅作语义对照。以下条目供汇总报告去重合并。

## 一、确认 / 较有据的代码问题

### 1. 🟨中 ModJava.cs：Java 版本范围冲突时 Intersect 返回 null 引发 NRE
- 位置：`pcl-next/Modules/Minecraft/ModJava.cs:217-222`（`AddConstraint` 局部函数）
- 证据：
  ```csharp
  JavaRange = JavaRange?.Intersect(Constraint);
  if (JavaRange.IsEmpty())   // Intersect 无交集时返回 null（见 MeloongCore/General/ValueRange.cs:132 文档「否则返回 null」），此处解引用 → NRE
  ```
- 根因：`ValueRange<T>.Intersect` 无交集返回 null；叠加两个互相冲突的 Java 约束（如先 AtLeast(21) 再 LessThan(16)）时，`JavaRange` 被赋成 null，下一行 `IsEmpty()` 空引用。本应走 218 行的冲突提示分支，实际抛异常。
- 建议：判空后再 `IsEmpty()`；无交集时直接置空范围并记录冲突。

### 2. 🟨中 ModJava.cs：Java 下载列表 worker 失败后仍直接取 LastResult（上游 #9533/L1 部分修复，重试失败路径仍在）
- 位置：`pcl-next/Modules/Minecraft/ModJava.cs:560-564`
- 证据：
  ```csharp
  if (!_SelectOrDownloadJava_DownloadListWorker.HasSucceeded)
      _SelectOrDownloadJava_DownloadListWorker.Start(c, p?.SplitTo(0.4d));
  _SelectOrDownloadJava_DownloadListWorker.WaitIfRunning(cancellationToken: c);
  var Components = _SelectOrDownloadJava_DownloadListWorker.LastResult;
  ```
- 根因：`RedoableWorker.LastResult` 在 `!hasSucceeded` 时抛 `InvalidOperationException("从未成功完成过。")`（MeloongCore/Worker/RedoableWorker.cs:68-76）。若本次 Start 的负载因网络/JSON 损坏失败，`hasSucceeded` 仍为 false，564 行直接取 `LastResult` 抛异常；被 `SelectOrDownloadJava` 外层 catch 后只记「选择 Java 时出现异常」，掩盖真实失败原因，也走不到 568 行的「手动安装 Java」提示。首次启动前的 `HasSucceeded` 判断已补，但失败重试路径未补。
- 建议：`WaitIfRunning` 后再判 `HasSucceeded`，未成功时按失败分支提示手动安装。

### 3. 🟨中 MyBitmap.cs：Stream.Read 单次不保证读满（CA2022 佐证），WebP 整文件可能尾部截断
- 位置：`pcl-next/Modules/Base/MyBitmap.cs:130`（2 字节头）、`136`（整个 WebP 文件）
- 证据：
  ```csharp
  InputStream.Read(Header, 0, 2);                       // 130：返回值未检查，可能 <2
  var FileBytes = new byte[(int)(InputStream.Length - 1L + 1)];
  InputStream.Read(FileBytes, 0, FileBytes.Length);     // 136：单次 Read 不保证读满，尾部可能为 0
  ```
- 根因：`Stream.Read` 返回实际读取字节数，可能小于请求长度；未循环读满时缓冲区尾部为默认 0。本地 FileStream 通常一次读满，但规范不保证，大文件/被截断流场景会导致 WebP 解码失败或图像损坏。编译已报 CA2022 两处。
- 建议：循环读到累计长度等于目标长度，或用 `ReadExactly`（.NET 10 可用）。

### 4. 🟩低 ModValidate.cs：部分校验规则未先判 Str 为 null，null 输入直接 NRE（已被 C3 复核修正）
- 仍成立：
  - `ModValidate.cs:169` `ValidateInteger`：`Str.Length > 9`（Str null → NRE）
  - `ModValidate.cs:235` `ValidateExcept`：`Str.IndexOfF(Ch, true)`（`IndexOfF` 非 null 安全）
  - `ModValidate.cs:494` `ValidateFolderPath`：`Str.Replace("/", @"\")` 先于空校验（C3 补充）
- 已降级为非 bug（C3 2.2 核对）：`ModValidate.cs:144` `ValidateHttp`——`EndsWithF/StartsWithF` null 安全，`RegexCheck` 内部 try/catch 兜底，null 走「网址无效」分支，不抛 NRE。
- 根因：规则被单独复用时缺前置空校验；上层调用一般先过空校验。
- 建议：规则入口补 null 判空。

### 5. 🟩低 ModNet.cs：多线程共享计数/结果无锁，存在竞态（上游 VB 同构，迁移保留）
- 位置：`pcl-next/Modules/Base/ModNet.cs:153-158、167-196`（`NetRequestByClientMultiple`）
- 证据：`RequestResult`、`RequestEx`、`FailCount` 由多个 `new Thread` 同时读写；`FailCount += 1` 是非原子读改写。
- 根因：共享可变状态无锁/无内存屏障；多线程下计数可能丢失、结果可见性不保证。VB 原版同样多线程共享，属上游遗留并发债，非本次 C# 新引入，但 C# 下应考虑加锁或用同步原语收敛。

### 6. 🟩低 ModWatcher.cs：普通 List<Watcher> 被多线程并发读写/重建，无锁
- 位置：`pcl-next/Modules/Minecraft/ModWatcher.cs:21`（`McWatcherList`）、`28-39`（`WatcherStateChanged` 遍历）、`119-127`（构造时重建列表）
- 根因：构造线程与各 Watcher 日志线程都会触碰该 List；`List<T>` 非线程安全，并发枚举/增删可能抛 InvalidOperationException 或读到脏状态。
- 建议：对列表访问加锁，或换并发集合 + 快照遍历。

## 二、可疑待运行 / 反编译验证

### 7. ~~ModValidate.cs：`char[] + string` 语义退化~~【已撤销，C3 2.2 复核当前代码已修复】
- 现状：`ModValidate.cs:371、446、534` 三处均为 `new string(Path.GetInvalidFileNameChars()) + …`，`char[]` 未再直接拼字符串；非法文件名字符拦截正常。本疑点不成立。

### 8. 🟩低 Application.xaml.cs：`--gpu` 无第二个参数时 `e.Args[1]` 越界（已被 catch 兜底）
- 位置：`Application.xaml.cs:116`，外层 119 catch 后 `Environment.Exit(Fail)`，不崩溃，但路径不规范。

### 9. 🟩低 ModWatcher.cs：`Instance` 为 null 时 207 行 `Instance.ReleaseTime.Year` NRE
- 位置：`ModWatcher.cs:207`。需确认是否存在不传 Instance 的调用路径。

### 11. 🟩低（可疑，上游同源）ModMinecraft.cs：继承链合并只防自环，未防多节点互继承环；父版本缺 libraries 键时 foreach 空引用
- 位置：`ModMinecraft.cs:595-622`（构造合并）、`2186-2190`、`2569-2571`、`2617-2619`；`ModDownload.cs:30-31、58-59`
- 证据：614 行仅判断 `InheritInstance.InheritName == InstanceNameInheritFrom`（父版本继承它自身）；A→B→A 互继承不在覆盖内，递归 `new McInstance` 可能栈溢出/无限递归。619 行 `InheritInstance.JsonObject["libraries"]` 缺键返回 null，`foreach` 解引用 NRE。
- 触发前提：用户/外部版本 JSON 配置成互继承，或父版本无 libraries 数组；正常官方/加载器版本不触发。VB 同构。
- 建议：合并循环带已访问名集合防环；619 前判 `libraries is JArray`。

### 10. 🟨中（待验证）PageLoginMsSkin.xaml.cs：皮肤上传 401 / 认证失败后 goto Retry 无重试上限，可能无限循环
- 位置：`pcl-next/Pages/PageLaunch/PageLoginMsSkin.xaml.cs:127(Retry)、139-143、183-189`
- 证据：`EditSkin` 用 `Retry:` 标签 + `goto Retry`；139 行命中「requires user authentication」与 188 行 401 都会重启 `McLoginMsLoader` 后直接 `goto Retry`，**没有最大重试次数或退避**。若服务器持续返回 401/认证失败，会在后台线程反复重启登录 + 发网络请求，形成死循环。
- 根因：迁移保留了 VB 的 GoTo 重试结构，未补重试计数。
- 建议：加重试上限（如 2 次），超限走失败提示；finally 中 `IsChanging=false` 与重试计数的关系一并复核。
- 附带：150 行 `ResultJson["skins"]` 若缺失，152 行解引用可能 NRE（可疑低）。

## 三、编译佐证的警告（技术债 / 潜在风险，非阻断）

| 警告 | 位置 | 说明 |
|---|---|---|
| SYSLIB0014 ServicePointManager 过时 ×7 | Application.xaml.cs:187-191、194；ModNet.cs:2372 | net10 上这些设置不影响 HttpClient/SslStream，属 net48-era 残留（清单§五已登记） |
| CS0618 SecurityProtocolType.Ssl3 过时/不安全 | Application.xaml.cs:194 | Win7 不可达回退分支，且 Ssl3 已弃用 |
| SYSLIB0021 DESCryptoServiceProvider 过时 | PageSetup/Settings.cs:391、413 | DES 为弱加密 + 过时 API，建议换 Aes.Create |
| SYSLIB0037 AssemblyName.ProcessorArchitecture 过时 | PageLink/PageLinkMain.xaml.cs:533 | 现代运行时不支持该成员 |
| WFO0003 app.manifest 高 DPI 设置待迁移 | app.manifest | 需改用 `ApplicationHighDpiMode` 项目属性 / API |
| NU1510 多余 Microsoft.Win32.Registry 包引用 | MeloongCore.csproj（被主工程传递） | 自动可用，可移除显式引用 |

## 四、上游已知、C# 侧复核仍在（不重复计为新 bug，仅留痕）

- net48 注册表检查 + `OldEnvironmentAssert` 弹 net48 离线安装页：`Application.xaml.cs:91-92、369-379`（清单§五 P1，net10 自包含不需要 net48，存在误报风险）。
- Win7 `Ssl3|Tls|Tls11` 不可达回退：`Application.xaml.cs:192-195`。
- `NetRequestByClientRetry` 的 `BackupUrl` 参数未使用：`ModNet.cs:73`（清单 L4）。
- 启动取消状态残留（上游 #9440）：`ModLaunch.cs:2737-2741` 取消即 Kill；Loader 复用前是否复位取消标志需结合 Loader 生命周期确认。
- Java 列表失败读 LastResult（#9533）：见本文件第 2 条，部分残留。

## 五、已核对排除的误报

- `Logger.Warn/Error(Exception, string)`、`Logger.Error(string, LogBehavior)`：MeloongCore/Logger.cs 均有对应重载，调用签名匹配，非 bug。
- `FirstOrDefault(谓词1, 谓词2)`：MeloongCore EnumerableExtensions.cs:10 为「按优先级依次尝试」的 params 重载，与 ModJava 567 行「优先指定 Component、回退范围」语义一致，非 AND 误用。
- `ValidateFolderName` 枚举目录失败吞 `DirectoryNotFoundException/UnauthorizedAccessException/IOException`：注释与逻辑一致，等价「无忽略项」，非吞异常。
- `ModBuildChannel` 条件编译链：Alpha/Beta/Rc/Snapshot/Slush/Dev 分支齐全，通道号段与偏移保留。
