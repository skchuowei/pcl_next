# C2 基础模块 ModBase — 只读代码审查报告

- 审查根目录：`D:\bing\MC\pcl_x\PCL_NEXT_Windows`（Windows 工作区，分支 dev）
- 审查日期：2026-11-11
- 审查方式：单文件逐行通读（`ModBase.cs` 1460 行分段读完）+ 同目录 `ModBase.vb` 语义对照 + 被调扩展方法/工具类签名交叉核对；未改源码，未 build/publish，未动 git。
- 编译事实：`*.vb` 不参与编译，仅作语义对照；本组不收录 VB 内部问题。
- 上游基线：`PCL2上游遗留问题清单.md` 已登记项不重复上报；C# 侧仍在或形态变化的，列入 1.1 并给新行号。

---

## 一、发现清单

### 1.1 上游已知，C# 侧复核仍在（不计为新发现）

| 严重度 | 状态 | 文件:行号 | 现象 | 证据 | 根因 | 建议 |
|---|---|---|---|---|---|---|
| 🟩低（清单 §八「版本常量注释与机制不符」） | ✅确认（上游已知，C# 侧复核仍在，形态部分修正） | `pcl-next\Modules\Base\ModBase.cs:31` | 行内注释仍写「下列版本信息由更新器自动修改」，与实际机制不符；但紧邻的 `:34-40` 已补「本值由 `scripts\Set-VersionInfo.ps1` 依据标签写入，不可手工填写」说明 | `public const string VersionBaseName = "2.100.0.6";` 上方注释 `// 下列版本信息由更新器自动修改`；`:40` `// 本值由 scripts\Set-VersionInfo.ps1 依据标签写入…` | 清单 §八登记项（VB `ModBase.vb:8`）；C# 迁移保留旧注释，同时追加了正确机制说明 | 仅订正 `:31` 注释措辞，常量值仍由发布脚本写入，勿手改 |
| 🟨中（清单 §七「远程主页 XAML 供应链风险」） | ✅确认（上游已知，C# 侧复核仍在） | `pcl-next\Modules\Base\ModBase.cs:1356-1366` | 远程 XAML 加载前的类型/成员黑名单仍在：禁 `WebBrowser/Frame/MediaElement/ObjectDataProvider/XamlReader/Window/XmlDataProvider/SettingService` 类型，禁 `Code/FactoryMethod/Static` 成员 | `new[] { typeof(WebBrowser), typeof(Frame), typeof(MediaElement), typeof(ObjectDataProvider), typeof(XamlReader), typeof(Window), typeof(XmlDataProvider), typeof(SettingService) }`；`new[] { "Code", "FactoryMethod", "Static" }` | 清单 §七登记项（VB `ModBase.vb:1148-1152`）；C# 迁移原样保留黑名单兜底 | 保留；供应链口径（默认关闭远程主页 / 白名单签名）属产品决策，本组不改 |
| —（清单 §五「NetFramework4.8 断言残留」） | ✅确认（本组文件内不存在） | `pcl-next\Modules\Base\ModBase.cs` 全文件 | 未发现 `NDP\v4\Full` 注册表读取、528040 阈值断言或 net48 安装包跳转；该残留位于 `Application.xaml.vb:31-35 / :210-218`，不在本组边界内 | 全文件检索 `NDP / 4.8 / 528040 / NetFramework` 无命中（仅图标路径字符串含数字） | 清单 §五 P1 项；ModBase.cs 侧未继承该断言 | 无需在本组处理；Application 侧另行复核 |
| —（任务说明：GetHashCode 豁免 CS0659/0661） | ✅确认（本组不构成问题） | `pcl-next\Modules\Base\ModBase.cs:499-509` | `MyColor.Equals` 复用 `==` 重载，`GetHashCode` 取 `HashCode.Combine(A,R,G,B)`，与 `==` 比较字段一致；未发现需 csproj NoWarn 豁免的类 | `public override bool Equals(object obj) => obj is MyColor other && this == other;`；`public override int GetHashCode() => HashCode.Combine(A, R, G, B);` | 任务说明明确：16 个类的 GetHashCode 豁免为有意保留，不当 bug 报 | 保留现状 |

### 1.2 本组新发现（C# 侧）

| 严重度 | 状态 | 文件:行号 | 现象 | 证据 | 根因 | 建议 |
|---|---|---|---|---|---|---|
| 🟨中 | ⚠️可疑（未运行时复现；net10 行为推断） | `pcl-next\Modules\Base\ModBase.cs:1079-1085`；调用点 `:1217`（OpenWebsite）、`:1238`（OpenExplorer 目录分支） | `StartProcess` 构造 `ProcessStartInfo` 时未设 `UseShellExecute`；.NET 10 默认 `false`，以 CreateProcess 方式启动 `http(s)://` / `minecraft://` 协议 URL 或文件夹路径会抛 `Win32Exception`；异常被外层 catch 吞掉后只走剪贴板/告警兜底，网页/目录打不开 | `return Process.Start(StartInfo);` 全程无 `UseShellExecute =`；`OpenWebsite` 校验 `http`/`minecraft://` 后 `StartProcess(Url)`；`OpenExplorer` 目录分支 `StartProcess(Location)`（文件分支 `:1242` 显式 `explorer` 可工作）；全仓其他启动点均显式 `UseShellExecute = false`（ModLaunch.cs:2652/2685/2726 等），无全局默认兜底 | VB net48 时代 `UseShellExecute` 默认 `true`，shell 可解析协议/目录；迁 net10 后默认翻转，包装层未补显式设置 | shell 类启动（URL、目录）在 `StartProcess` 内按 FileName 判定或由调用点显式 `UseShellExecute = true`；执行体启动保持 `false` |
| 🟨中 | ✅确认 | `pcl-next\Modules\Base\ModBase.cs:1290`；`:1307-1310` | `ExtractResources` 资源名不存在（或资源未编进程序集）时 `ResourceManager.GetObject` 返回 null；落到 `else` 分支后 `Resource.GetType().Name` 直接解引用 null 抛 `NullReferenceException`，而非「资源不存在」的友好异常 | `var Resource = My.Resources.Resources.ResourceManager.GetObject(ResourceName);`；`if (Resource is byte[]) … else if (Resource is string) … else { throw new Exception($"资源 {ResourceName} 的类型不支持：{Resource.GetType().Name}"); }`；null 既非 `byte[]` 也非 `string`，进入 else | 迁移自 VB `ModBase.vb:1087-1101` 同形；VB 晚期绑定 `Resource.GetType.Name` 同样会 NRE，C# 未补判空 | 进入类型分支前先 `if (Resource is null) throw new Exception($"资源 {ResourceName} 不存在");` |
| 🟩低 | ⚠️可疑（上游 VB 同形：`ModBase.vb:754-759`） | `pcl-next\Modules\Base\ModBase.cs:916`；`:921`；`:932-935` | 查询串全为空格时 `Split(" ", true)` 得到空数组；`QueryParts.All(...)` 对空集合恒为 `true`，导致每个条目都被标 `AbsoluteRight=true`，空查询下全部按完全匹配返回 | `string[] QueryParts = Query.Split(" ", true);`；`Entry.AbsoluteRight = QueryParts.All(QueryPart => …);`；`if (Entry.AbsoluteRight) ResultList.Add(Entry);` | 空集 `All` 短路语义未被识别为「无查询条件」；上游同形继承 | 空/纯空格 Query 直接返回空结果，或空 QueryParts 时跳过 AbsoluteRight 判定 |
| 🟩低 | ⚠️可疑 | `pcl-next\Modules\Base\ModBase.cs:845-855` | 所有权重为 0（或 Source 为空）时 `TotalWeight = 0`，`Sum / TotalWeight` 得 `NaN`/`Infinity`；`NaN` 不满足 `>= MinBlurSimilarity` 被过滤，`Infinity` 则会异常抬高排序 | `TotalWeight += Pair.Weight; … return Sum / TotalWeight;`；无 `TotalWeight == 0` 兜底 | 加权平均缺零分母保护；上游同形 | `TotalWeight == 0` 时返回 0d |
| 🟩低 | ⚠️可疑 | `pcl-next\Modules\Base\ModBase.cs:920-921`；`:851` | 搜索链路 null 解引用：`Entry.SearchSource` 为 null 时 `foreach` NRE；`Source.Aliases` 为 null 时 `Any()` NRE；别名数组含 null 元素时 `:921` `Alias.Replace(...)` NRE。`SearchEntry`/`SearchSource` 构造未强制初始化集合 | `Entry.Similarity = SearchSimilarityWeighted(Entry.SearchSource, Query);`；`Source.Aliases.Any(Alias => Alias.Replace(" ", "").ContainsIgnoreCase(QueryPart))`；`Pair.Aliases.Max(...)` | 公开构造类，调用方可能传 null；扩展方法 `ContainsIgnoreCase/Replace` 不做 null 容错 | 构造函数默认空集合；调用链入口判空；别名元素过滤 null |
| 🟩低 | ⚠️可疑 | `pcl-next\Modules\Base\ModBase.cs:593-598` | `IniClearCache(FileName = null)` 无 try/catch，`FileName.Contains(@":\")` 抛 NRE；对比 `IniGetContent:606-632` 有异常兜底返回 null | `public static void IniClearCache(string FileName) { if (!FileName.Contains(@":\")) FileName = …; IniCache.Remove(FileName); }` | 入口缺判空；写/读路径有 catch，清缓存路径漏了 | 入口 `if (string.IsNullOrEmpty(FileName)) return;` |
| 🟩低 | ⚠️可疑 | `pcl-next\Modules\Base\ModBase.cs:1001-1015` | `UuidLock` 在锁外惰性赋值：双线程同时判空为 null 时可各自 `new object()`，短暂绕过互斥；`Uuid += 1` 在不同锁上并发执行，存在编号重复概率 | `private static object UuidLock;`；`if (UuidLock is null) UuidLock = new object(); lock (UuidLock) { Uuid += 1; return Uuid; }` | 双重检查未做锁内二次判断；上游 VB `:842-852` 同形 | 字段初始化器 `= new object();`，去掉惰性赋值 |
| 🟩低 | ⚠️可疑 | `pcl-next\Modules\Base\ModBase.cs:1425`；`:1432 / :1440 / :1451` | 静态共享 `Random` 非线程安全；`RandomOne/RandomInteger/Shuffle` 可能在后台线程与 UI 线程并发调用，并发 `NextDouble` 可能损坏内部状态（重复值/异常） | `private readonly static Random Random = new Random();`；`RandomInteger` 内 `Random.NextDouble()`；`Shuffle` 迭代中反复取随机数 | 静态单例未加同步；上游同形 | `lock` 包裹随机取数，或改 `ThreadLocal<Random>`（需固定种子策略） |
| 🟩低 | ⚠️可疑 | `pcl-next\Modules\Base\ModBase.cs:360-367`；`:389-396` | `MyColor(string)`：`ColorConverter.ConvertFromString(null/非法串)` 返回 null 或抛解析异常，后续 `.A` NRE；`MyColor(Brush)`：非 `SolidColorBrush`（如渐变画刷）强转抛 `InvalidCastException`；两个隐式转换均为公开入口 | `Color StringColor = (Color)ColorConverter.ConvertFromString(HexString); A = StringColor.A;`；`var Color = ((SolidColorBrush)brush).Color;` | 构造函数无类型/空值回退；默认黑/白兜底路径只在 `MyColor(object):405-413` 存在 | 判空回退默认色；`Brush` 构造先 `is SolidColorBrush` 判断，不匹配回退默认 |
| 🟩低 | ⚠️可疑 | `pcl-next\Modules\Base\ModBase.cs:487-490` | `FromHSL2` 亮度片区值 `center` 未钳制：`sS` 超 0~100 范围时 `center` 可 ≤0，`sL / center` 除零得 `NaN`/`Infinity`，随后 `FromHSL` 写入 RGB | `center = 50 - ((1 - center + intCenter) * cent[intCenter] + (center - intCenter) * cent[intCenter + 1]) * sS;`；`sL = (sL < center ? sL / center : 1 + (sL - center) / (100 - center)) * 50;` | 入参假设合法范围，无边界保护；上游同形 | `center` 下限保护（如 `Math.Max(center, 1)`）；`sS/sL` 入口钳制 |
| 🟩低 | ⚠️可疑 | `pcl-next\Modules\Base\ModBase.cs:994-999`；`:1079-1085` | `RunAsAdmin` 取得 `Process` 后 `WaitForExit` 未 `Dispose`；`StartProcess` 返回的进程在 `OpenWebsite/OpenExplorer` 路径同样不释放；退出后句柄/进程对象滞留到 GC | `var NewProcess = StartProcess(…); NewProcess.WaitForExit(); return NewProcess.ExitCode;`；`StartProcess` 内 `return Process.Start(StartInfo);` 无释放责任标注 | 缺 `using`/显式 `Dispose`；上游同形 | 调用点 `using var NewProcess = …;`；`StartProcess` 文档注明释放责任 |
| 🟩低 | ⚠️可疑 | `pcl-next\Modules\Base\ModBase.cs:1318` | 静态 `DPI = Graphics.FromHwnd(nint.Zero).DpiX`：取得的 GDI `Graphics` 未 `Dispose`（一次性 GDI 对象残留）；且只在首次访问 `ModBase` 时取一次，多显示器不同 DPI 下后续不刷新 | `public readonly static int DPI = (int)Math.Round(System.Drawing.Graphics.FromHwnd(nint.Zero).DpiX);` | 静态初始化器内取屏图形未释放；DPI 口径为全局单值 | `using` 包裹 `Graphics`；多屏 DPI 需求另按控件 `VisualTreeHelper` 取 |
| 🟩低 | ⚠️可疑 | `pcl-next\Modules\Base\ModBase.cs:1394-1400` | `IsVisibleInForm` 未判 `element` / `ModMain.FrmMain` 为 null；元素尚未挂到可视树时 `TransformToAncestor(FrmMain)` 抛 `InvalidOperationException`，扩展方法内无 catch | `if (!element.IsVisible) return false;`；`element.TransformToAncestor(ModMain.FrmMain).TransformBounds(…)`；`ModMain.FrmMain.ActualWidth` | UI 扩展方法假设控件已入树、主窗口已建；上游同形 | 入口判空返回 false；`TransformToAncestor` 包 try/catch 返回 false |
| 🟩低 | ⚠️可疑 | `pcl-next\Modules\Base\ModBase.cs:1406-1411` | `IsTextTrimmed`：`TextBlock` 为 null NRE；`Foreground` 为 null 时 `FormattedText` 构造可能抛异常；`FontSize` 为 0/负亦无兜底 | `new FormattedText(TextBlock.Text, …, TextBlock.Foreground, DPI)`；`TextBlock.Text` 未判空 | UI 扩展方法假设控件已布局、画刷非空；上游同形 | 判空返回 false；前景回退主题画刷；字号钳制 |
| 🟩低 | ⚠️可疑（上游同形，解析限制） | `pcl-next\Modules\Base\ModBase.cs:1190-1203` | 命令行参数按单空格拆分，引号包裹的含空格参数被截断；`Name` 为 null 时 `"-" + Name` 拼出 `"-null"` 仍参与比较；无等号形式（`--key=value`）解析 | `Interaction.Command().Split(" ")`；`(AllArguments[i] ?? "") == ("-" + Name ?? "")`；下一参数 `StartsWithF("-")` 判开关 | 轻量参数解析沿用 VB 简化实现；引号分词缺失 | 改造参数解析时按引号/等号分词；入口对 `Name` 判空 |
| 🟩低 | ⚠️可疑（理论边界） | `pcl-next\Modules\Base\ModBase.cs:127` | `Environment.GetLogicalDrives().First(p => Directory.Exists(p))`：无任何可用盘符时抛 `InvalidOperationException`；正常 Windows 必有系统盘，属理论边界 | `public static string OsDrive = Environment.GetLogicalDrives().First(p => Directory.Exists(p)); // #3799` | `First` 无兜底；上游同形 | `FirstOrDefault(...)` + 空值回退 `C:\` |

### 1.3 与 VB 同构 / 已核对无回归（备查记录，不计入新发现）

| 位置 | 结论 | 对照/证据 |
|---|---|---|
| `ModBase.cs:962 / :975` | `TargetTask.Run()` 不是 `Task.Run` 实例方法，而是 MeloongCore 扩展 `TaskExtensions.cs:4-5`（`GetAwaiter().GetResult()`），阻塞取结果语义正确，非 bug | `MeloongCore\Shared\Extensions\TaskExtensions.cs:4` `public static void Run(this Task task) => task.GetAwaiter().GetResult();`；`:5` 泛型重载 |
| `ModBase.cs:793-839` | `SearchSimilarity` 贪心移除已匹配子串的算法与 VB `ModBase.vb:655-690` 同构；queryLength=0 已在 `:804-805` 短路，`sourceLength` 分母 `+15` 无除零 | VB 对照同段 |
| `ModBase.cs:1382-1389` | `UiThreadId` 静态初始化、`RunInUi()` 按托管线程 ID 判定；若 `ModBase` 首次被后台线程访问会记错线程，但 VB `:1168` 同形，属继承设计 | VB `ModBase.vb:1168` |
| `ModBase.cs:776` | `Val` 对字符串 `"&"` 的特判保留；整体 try/catch 返回 0d，非数字输入不会抛 | VB `:633-640` 同形 |
| `ModBase.cs:619-624 / :697-703` | ini 键值以首个冒号切分、字典区分大小写；重复键后者覆盖（#3616 注释保留）；与 VB 同构 | VB `:514-518 / :575-579` |
| `ModBase.cs:299-306 / :339-346` | `MyColor operator * /` 除零得 `Inf`，`ClampToByte` 钳到 255，`NaN` 转 byte 在 unchecked 下为 0，行为可接受 | 数学分支核对 |
| `ModBase.cs:1430-1433` | `RandomOne` 空集合经 `ElementAtOrDefault` 返回 `default(T)`，较 VB `objects(...)` 直接索引更稳；无越界 | VB `:1211-1213` |
| `ModBase.cs:954-976` | 超时路径 `TokenSource.Cancel()` 后抛出 `TimeoutException`；`DelayTask` 未释放（短延迟任务，量小），上游同形 | 资源释放核对 |
| `ModBase.cs:1288-1311` | 字节/字符串资源写入前做哈希/内容比对去重；哈希 null 已 `?? ""` 兜底；仅「资源为 null」一路径有问题（见 1.2 第 2 条） | 与 VB `:1087-1102` 对照，C# 补了 null 合并 |

---

## 二、覆盖清单

| 文件 | 行数 | 结论 | 说明 |
|---|---|---|---|
| `pcl-next\Modules\Base\ModBase.cs` | 1460 | 已审 | 分段通读全文件：声明区版本常量/渠道编译期常量/静态路径与运行时状态（`:29-137`）；`Logo` 矢量图标常量（`:139-241`，纯数据未审路径）；`MyColor` 转换/运算/HSL/构造/相等性（`:245-511`）；`MyRect`/`LoadState`/`ProcessReturnValues`（`:513-580`）；ini 缓存读写删（`:586-714`）；权限检查（`:716-758`）；`Val`（`:766-782`）；搜索相似度/加权/入口（`:786-945`）；超时包装/路径/重启/UUID/时间戳/进程启动/线程调度/选择排序/命令行参数/网页与目录打开/剪贴板/资源释放（`:949-1311`）；XAML 加载与类型黑名单/DPI/UI 辅助函数（`:1315-1420`）；随机数与洗牌（`:1423-1455`）。交叉核对：`TaskExtensions.Run`（MeloongCore）、`StringExtensions`（Split/IndexOfF/StartsWithF/EndsWithF/ContainsIgnoreCase/Lower/IsAsciiOnly）、`PathUtils.ToShortPath/Base/AddSlashSuffix`、`Enumerable.GetOrDefault`；`ModBase.vb` 逐段语义对照。 |

### 2.1 本组跳过/排除项（按任务边界）

- `pcl-next\Modules\Base\ModBase.vb`：VB 迁移对照副本，不编译；其内部问题未计入发现。
- `MeloongCore` / `PCL.Download` / `PCL.Cli` / `PCLCS`：仅读被调签名与扩展方法核对语义，未深审其内部缺陷。
- 排除目录：`bin\`、`obj\`、`bin-net10\`、`ReleaseArtifacts\`、`publish\`、`.git\`、`downloads\`、`.screenshots\`。
- 不纳入项：风格、TODO、技术债、合规（L1–L5 义务）、产品参数取值、图标 SVG 路径数据正确性。

### 2.2 审查中确认无回归的关键点

- `MyColor` 相等性无递归：`==` 与 `!=` 内部用 `is null` 判空（`:311-314 / :319-322`），`Equals` 复用 `==` 而非 VB 运行时 `ConditionalCompareObjectEqual`，避免重载自调用栈溢出（注释 `:309-310 / :501-503`）。
- ini 缓存为 `ConcurrentDictionary` 嵌套字典，读路径缓存命中不重复读盘；写路径先改缓存再落盘，文件内容按缓存全量重建（`:618-626 / :696-708`）。
- 超时包装未丢弃取消语义：超时时 `TokenSource.Cancel()` 后抛 `TimeoutException`，任务本体由令牌联动（`:959-960 / :972-973`）。
- 剪贴板写入在后台线程发起、UI 线程执行，4 次重试 + 异常兜底；读取同样回 UI 线程且不抛（`:1254-1283`）。
- XAML 解析在独立 `MemoryStream` 内完成，`XamlXmlReader`/`StreamWriter` 均 `using` 释放；黑名单扫描先于 `XamlReader.Load`（`:1349-1378`）。
- 选择排序 `SortByComparison` 基于传入 `IList` 逐轮移除最大项，无固定索引假设；空表直接返回空（`:1167-1182`）。
- 时间戳换算 epoch 常数 `621355968000000000L` 正确；`GetUnixTimestampUtc` 用 `DateTime.Now.ToUniversalTime()`，`ToUnixTimestampUtc` 入参先转 UTC（`:1028-1038`）。
