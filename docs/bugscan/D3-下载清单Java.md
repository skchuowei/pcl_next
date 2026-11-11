# D3 版本清单 / Java 模块只读排查报告

> 排查范围（仅该目录，已排除 bin/obj/bin-net10/ReleaseArtifacts/publish/.git/downloads/.screenshots）：
> - `pcl-next/Modules/Minecraft/ModDownload.cs`（1071 行，全读）
> - `pcl-next/Modules/Minecraft/ModJava.cs`（784 行，全读）
> - 语义对照：同目录同名 `.vb`（不编译、不上报 vb 内部问题）
> 方式：静态只读审查，未 build/publish，未改动代码、未动 git。
> 上游基线：`PCL2上游遗留问题清单.md`；已登记项不重复计为新 bug，C# 侧仍在的在覆盖清单留痕。

## 一、问题清单

| 严重度 | 状态 | 文件:行号 | 现象 | 证据 | 根因 | 建议 |
|---|---|---|---|---|---|---|
| 🟨中 | ✅确认 | ModJava.cs:560-564（机制证据 RedoableWorker.cs:68-76） | **上游 #9533/L1，C# 侧复核仍在**：Java 下载列表 worker 失败/取消后直接读 `LastResult`，抛 `InvalidOperationException`（「从未成功完成过」），真实失败原因被外层 catch 吞成「选择 Java 时出现异常」。 | `:560 if (!_SelectOrDownloadJava_DownloadListWorker.HasSucceeded) Start(...)`；`:562 WaitIfRunning(cancellationToken: c)` 后无二次成功判断；`:564 var Components = _SelectOrDownloadJava_DownloadListWorker.LastResult;`；`RedoableWorker.cs:72 if (!hasSucceeded) throw new InvalidOperationException("从未成功完成过。");` | 守卫只覆盖「启动前是否需要重启」，未覆盖「等待结束后是否真的成功」；与 VB 同构（ModJava.vb:407-410：`If Not HasSucceeded Then Start` → `WaitIfRunning` → `LastResult`）。失败时工作线程已把 `lastSucceeded=false`（RedoableWorker.cs:146），但 `hasSucceeded` 不会翻回 false 之前的状态，`LastResult` 必抛 | `WaitIfRunning` 后补 `if (!HasSucceeded) { Logger.Error(...); return null; }`，与 `TryFixOrHint` 口径一致；不要在失败时取 `LastResult` |
| 🟨中 | ⚠️可疑 | ModDownload.cs:611（构造函数 606-619） | Forge 版本字符串 `Split(".")[3]` 无段数校验：段数不足 4 段时 `IndexOutOfRangeException`；当前条件虽限定 `Branch is null && Inherit == "1.7.10"`，但镜像/官方返回异常 3 段版本号即触发，构造函数在列表投影中无单项 try。 | `Conversions.ToDouble(Version.Split(".")[3]) >= 1300d`（:611）；`Version` 来自 `Item.VersionName`，无长度/格式预检；`DlForgeVersionMain`（:633-641）foreach 内无 try/catch | 照搬上游 late-bound 下标取值（VB ModDownload.vb:556 `Version.Split(".")(3) >= 1300` 同样无长度判断，C# 数组下标越界行为更显式）；上游 1.7.10 Forge 均为 4 段，触发面窄 | 先判 `Parts.Length >= 4` 再取 `[3]`，段数不足按 1300 以下处理；或构造函数包单项 try 跳过坏版本 |
| 🟩低 | ⚠️可疑 | ModJava.cs:392 | Java manifest 反序列化后直接索引 `["windows-x64"]`：该键缺失（镜像裁剪 / 非 Windows 源返回异常 JSON）时 `JObject` 索引返回 null，`foreach (JProperty Prop in null)` 抛 NRE，worker 失败并走到上一条 `LastResult` 抛异常链。 | `foreach (JProperty Prop in IndexFileStr.DeserializeJson<JObject>()["windows-x64"])`（:392）；前置 `NetRequestByLoader` 仅保证文本非空，不保证键存在 | 解析未做键存在性判空；VB 同构（ModJava.vb:386）。官方/BMCLAPI 现状均带 `windows-x64`，触发需源数据异常 | 取键后 `if (index is null) throw new Exception("Java 清单缺少 windows-x64 节点");` 再遍历 |
| 🟩低 | ⚠️可疑 | ModJava.cs:399-402 | 子项 `Prop.Value.FirstOrDefault()` 判空后，`Entry["version"]["name"]` 仍逐键解引用；`version`/`name` 缺失或 `Prop.Value` 非数组时 NRE。虽被 :417 单项 catch 跳过，但该项静默丢失，且失败原因仅 Warn。 | `var Entry = Prop.Value.FirstOrDefault();`（:399）→ `if (Entry is null) continue;`（:400-401）→ `Entry["version"]["name"].ToString()`（:402）；catch `:417-420` 仅记 Warn | 字段健壮性不足，沿用上游写法；VB 同构（ModJava.vb:390-392） | 逐键 `?["version"]?["name"]`，缺字段时跳过并记录组件名 |
| 🟩低 | ⚠️可疑 | ModJava.cs:755-757 | `Target.Java["manifest"]["url"]` 缺失得 null 后，:757 `Address.Replace(...)` 对 null 解引用 NRE。 | `string Address = (string)Target.Java["manifest"]["url"];`（:755）→ `Address.Replace("piston-meta.mojang.com", "bmclapi2.bangbang93.com")`（:757）；上游 Mojang 条目一般都带 manifest，触发需异常条目 | 未判空即字符串替换；VB 同构（ModJava.vb:549-550） | 取 url 后判空，缺 manifest 跳过该 Java |
| 🟩低 | ⚠️可疑 | ModJava.cs:760-762 | manifest `files` 缺键时 null 进 foreach；`((JObject)File.Value)["downloads"]?["raw"]` 在 `File.Value` 为 null 时，于 `?.` 之前已对 null JObject 取索引器，NRE。 | `foreach (JProperty File in ((JObject)ListFileStr.DeserializeJson())["files"])`（:760）；`((JObject)File.Value)["downloads"]?["raw"] is null`（:762） | JSON 结构假设过强；VB 同构（ModJava.vb:553） | `files` 取键判空；`File.Value as JObject` 后再访问 `["downloads"]` |
| 🟩低 | ⚠️可疑 | ModJava.cs:765 | `Info["size"]` 直接 `(long)` 强转，缺 size/非数值时转换异常；同模块 ModDownload.cs:43 对 size 有 `?? -1` 兜底，两处口径不一致。 | `ActualSize = (long)Info["size"], Hash = (string)Info["sha1"]`（:765）；对照 ModDownload.cs:43 `(long)(...["size"] ?? -1)` | 解析兜底缺失；上游条目一般带 size，触发面窄 | size 缺失用 `?? -1`，与 jar 校验口径对齐 |
| 🟩低 | ⚠️可疑 | ModDownload.cs:616 | `new Version(Version)` 对非法版本号抛 `FormatException`；构造函数在 `DlForgeVersionMain` 投影内无单项保护，一条坏版本数据可中断整份 Forge 列表加载。 | `this.Version = new Version(Version);`（:616）；投影循环 :633-641 无 try；`Item.VersionName` 来自网络 JSON | 未做解析容错；VB 同构（ModDownload.vb:560）。镜像/官方版本号现状规范 | 构造内 try 解析失败降级（如置 `Version = new Version(0,0,0)`），或投影时单项跳过并 Warn |
| 🟩低 | ⚠️可疑 | ModDownload.cs:719-725 | NeoForge `0.x` 分支：`Inherit = Segments[1]` 取到的是 `25w14craftmine` 这类快照段，不是合法 MC 版本号；且 `Segments[1]` 在单段输入时越界。属上游既有行为（VB ModDownload.vb:656 同款），C# 原样照搬。 | `string[] Segments = ApiName.BeforeFirst("-").Split('.');`（:722）→ `Version = new Version(0, 0, ToInteger(Segments.Last()));`（:723）→ `Inherit = Segments[1];`（:724） | 0.x craftmine 特例的继承版本取值规则存疑；当前仅 `0.25w14craftmine.3-beta` 一类边缘快照命中 | 复核 0.x 版本的 Inherit 应取何值；至少补 `Segments.Length >= 2` 判空 |
| 🟩低 | ⚠️可疑 | ModDownload.cs:836 | LiteLoader 列表逐项 `JToken.Parse(Item.RawJson)`：某项 RawJson 为空/坏 JSON 时抛异常，foreach 内无单项保护，整份列表失败。 | `JsonToken = JToken.Parse(Item.RawJson)`（:836）；`DlLiteLoaderListMain` 循环 :827-837 无 try | 网络 JSON 未做容错；VB 同构 | 单项 try 跳过坏条目并 Warn，保留其余版本 |
| 🟩低 | ⚠️可疑 | ModDownload.cs:870 | Fabric 元数据 `(JObject)Result.RawJson.DeserializeJson()`：RawJson 为 null 时 `DeserializeJson` 返回 null（强转 null 不崩、Value 为 null），但若源返回数组/非对象，`InvalidCastException` 中断列表。 | `Value = (JObject)Result.RawJson.DeserializeJson()`（:870）；`DeserializeJson<T>` 仅在反序列化失败时抛 `FormatException`（StringExtensions.cs:401-411），类型不符在此处抛 | 未校验 JSON 顶层类型；上游 API 现状为对象 | 先取 `JToken`，判 `Type == JTokenType.Object` 后再转 `JObject` |
| 🟩低 | ⚠️可疑 | ModDownload.cs:1037 | `new Uri(Original)` 对非法/相对/空 URL 抛 `UriFormatException`；`IsThirdPartyMaven` 只按前缀匹配，未校验 scheme，拼镜像前直接进 Uri 解析。 | `string Rel = new Uri(Original).AbsolutePath.TrimStart('/');`（:1037）；前缀匹配 :1027 只看 `StartsWith` | 输入假设为合法 http(s) 链接；当前调用方传库内构造的下载 URL，触发面窄 | 包 try，Uri 解析失败回退原 URL 不走镜像换算 |
| 🟩低 | ⚠️可疑 | ModDownload.cs:1063-1065 | `DlSourceModGet` 入口未判空：`Original.Replace(...)` 链对 null NRE。调用方 :906 虽做 `(McimUrl ?? "") != (Url ?? "")` 比较，但 `DlSourceModGet(Url)` 在比较之前已先执行，null 会先在本方法内抛。 | `return Original.Replace("api.modrinth.com", …).Replace(...)`（:1065）；调用 `string McimUrl = DlSourceModGet(Url);`（:905） | 入参健壮性缺失；VB 同构（ModDownload.vb:828）。现有调用均传非空 URL | 入口 `Original ?? ""` 或在方法首行判空返回原值 |
| 🟩低 | ⚠️可疑 | ModJava.cs:43、48、686 | 老版本迁移路径读取 JSON 的 `["Path"].ToString()`：`Path` 字段缺失时 JToken 索引返回 null，`.ToString()` NRE。:43/:48 在外层迁移 try（:39-63）内被兜底记 Error；:686 在 `MigrateInstanceJavaSettings` 内无 catch（仅 finally），异常上抛至 `SelectOrDownloadJava` 外层 catch（:646-652）。 | `new Java(JavaObject["Path"].ToString())`（:43）；`DeserializeJson<JObject>()["Path"].ToString()`（:48）；`new Java(ForcedJavaSetting.DeserializeJson<JObject>()["Path"].ToString())`（:686） | 历史设置 JSON 结构假设过强；VB 同构（ModJava.vb 迁移段）。仅老版本残留坏设置命中 | 逐键 `?["Path"]?.ToString()`，缺 Path 按无强制 Java 处理并 Warn |

## 二、覆盖清单（已核对，不计为新 bug）

### 2.1 上游已登记 / 同构留痕

- **#9533 / L1（Java 列表失败后读 LastResult 抛 InvalidOperationException）**：上游登记于 `ModJava.vb:410` + `RedoableWorker.cs:68-76`。C# 侧复核：形态未变，守卫仍只在 Start 前，证据 `ModJava.cs:560-564` + `RedoableWorker.cs:72`，见问题清单第 1 条。
- **版本清单冷启动每次拉 manifest（清单§四「版本清单不落盘」）**：`ModDownload.cs:206-213` 走 `McVersionManifestClient.Fetch`，缓存仅由网络层 `CacheCow` 部分缓解；属已登记技术债，按规则不在 bug 清单单列。
- **硬编码超时（清单§四）**：`ModDownload.cs:912-938` 10s/20s/30s 三档、`ModJava.cs:388/757` 走 `NetRequestByLoader` 默认 45s；属已登记技术债/产品参数，不单列。
- **Fabric API / OptiFabric 列表**：`ModDownload.cs:876/881-893` 已按 `ResourceSourceGate.CurseForgeAvailable` 做 403 降级，未见异常吞噬或状态残留。
- **清单 §1.3 #9412 大小写、#9426 单选筛选、#9360 并发更新、#9374 可选文件**：对应模块在 `ModModpack` / `ResourceSearcher` / `ModDownloadLib`，不在本组两文件，未重复排查。

### 2.2 已核对排除的误报

| 位置 | 核对结论 |
|---|---|
| ModDownload.cs:141-142 `GetInfo(RealAddressBg).LastWriteTime.Date` 先于 `Exists` | `FileUtils.GetInfo` 即 `new FileInfo(path)`（FileUtils.cs:483-484），缺文件构造不抛、访问 `LastWriteTime` 返回默认值；`Exists` 在 `&&` 右侧短路，不先解引用。VB 同构（ModDownload.vb:138-139），非 bug。 |
| ModDownload.cs:173 `Settings.Get<string>("CacheDrops").ToString()` | `CacheDrops` 已注册默认 `""`（Settings.cs:31）；空串 `Split(",", true)` 得空数组，`.Any()` 为 false 返回 null，无 NRE。 |
| ModJava.cs:226/231/236 `&&`/`||` 优先级 | C# `&&` 先于 `||`，括号语义与 VB `(Not Valid AndAlso t) OrElse (Valid AndAlso v)` 一致（对照 ModJava.vb:153-154/157-158/161-162），无分支反转。 |
| ModJava.cs:307/312/317/327/332/342/362 `ModLoaderVersions[loader]` 索引 | `ModLoaderVersions` 为普通 `Dictionary<ModLoaders,string>`（McVersion.cs:34）；`ModLoaders` 由字典 Keys 聚合（McVersion.cs:32），`HasFlagF(loader)` 为真即键存在，无 `KeyNotFoundException`。 |
| ModJava.cs:48 `SelectedJavaSetting.StartsWithF("{")` | `StartsWithF` 为 null 安全扩展（StringExtensions.cs:12-13，null → false），不 NRE。 |
| ModDownload.cs:416 `value.EndsWithF(".0")` | 同为 null 安全扩展（StringExtensions.cs:24-25）；`Left(value, Length-2)` 仅在 EndsWithF 为真（非 null）时执行。 |
| ModDownload.cs:311/334 `Output.Value["versions"]` 二次遍历 | `DlClientListGet` 整体包 `try/catch`（:342-346），重启后 Output 为 null 时 NRE 被兜底返回 null，不崩进程。 |
| ModDownload.cs:101 后台索引 `Move(IndexBackgroundFile, IndexFile.LocalPath)` | 移动前置 `FileUtils.Exists(IndexBackgroundFile)` 判断；`LocalPath` 为 `IndexFile` 自带路径，不存在时 `GetInfo` 返回默认值不抛；与 VB 行为一致。 |
| ModJava.cs:385 静态 worker 委托签名 | `RedoableWorker<List<JavaDownloadInfo>>(cancellationToken => …)` 匹配 `Func<CancellationToken?, TOut>` 构造重载（RedoableWorker.cs:194-195），无签名错配。 |
| ModDownload.cs:906 `(McimUrl ?? "") != (Url ?? "")` | 比较侧已判空；唯一残留是 `DlSourceModGet` 内部未判空，已单列问题清单第 13 条。 |
| ModJava.cs:512-513 `GetJavaRequirement(Instance)` 调用两次 | 纯重复计算，结果一致；无状态副作用，属性能小冗余，不计 bug。 |
| ModDownload.cs:912-913 镜像 URL 连续两次入列 | 与 VB 一致（ModDownload.vb:832-833），为重试轮次设计，非重复 bug。 |

### 2.3 统计

- 问题清单共 **14** 条：🟥高 **0** / 🟨中 **2** / 🟩低 **12**；✅确认 **1** / ⚠️可疑 **13**。
- 覆盖清单：上游同构留痕 5 组、排除误报 11 组。
- 无 🟥高确认项；两条 🟨中分别为已登记 L1 在 C# 侧仍在（机制已确认）、Forge 版本段数越界（触发面窄、需异常镜像数据）。
