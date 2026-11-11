# H1 实例 Mod 管理页面（PageInstanceMod / MyLocalModItem）— 只读代码审查报告

- 审查根目录：`D:\bing\MC\pcl_x\PCL_NEXT_Windows`（Windows 工作区，分支 dev）
- 审查日期：2026-11-11
- 审查文件（本组两份，逐文件全读）：
  - `pcl-next\Pages\PageInstance\PageInstanceMod.xaml.cs`，1111 行
  - `pcl-next\Pages\PageInstance\MyLocalModItem.xaml.cs`，773 行
  - 对照同目录 `*.vb` 仅做语义比对（不编译、不上报 VB 内部问题）；两份 `.xaml` 事件名 / 控件名已逐一核对
- 审查方式：逐行通读 + 同目录 VB 语义对照 + 调用点 / 类型定义 / 上游清单交叉核对；只读，未改源码，未 build/publish，未动 git。
- 上游基线：先读 `PCL2上游遗留问题清单.md`；已登记项不重复上报，C# 侧复核结果见 1.1。
- 边界：仅审 `pcl-next\Pages\PageInstance\` 目录内本组两份 C#；已排除 `bin/obj/bin-net10/ReleaseArtifacts/publish/.git/downloads`；`PCL.Download/`、`PCLCS/`、`MeloongCore/`、其他 Controls / Modules 仅做签名 / null 语义交叉核对，内部实现不纳入；`*.vb` 不编译、不上报 VB 内部问题；风格 / TODO / 技术债 / 合规不纳入。

---

## 一、发现清单

### 1.1 上游已登记 / 已处置项，C# 侧复核结果（不计为新发现）

| 严重度 | 状态 | 文件:行号 | 现象 | 证据 | 根因 | 建议 |
|---|---|---|---|---|---|---|
| — | 上游已知（#9360，不在本组） | 本组 `PageInstanceMod.xaml.cs:809-813` | 清单登记「多个 Mod 并发更新时因更新 Mod 相同导致替换失败」，属 `ModDownloadLib.vb` 更新流水线；本组只做下载 Loader + 本地「先删旧、再 Move 新」的 UI 编排，未进入并发同文件判定逻辑 | `InstallLoaders.Add(new ModNet.LoaderDownload(...))`；替换任务内 `FileUtils.Delete(Entry.File.FullName, true)` → `FileUtils.Move(Entry.Key, Entry.Value)`；C# 侧未见对「两个选中 Mod 映射到同一目标文件」的去重 / 保护 | 上游遗留在下载引擎侧；本页编排与 VB `PageInstanceMod.vb:586-610` 同形 | 不纳入本组；随 #9360 流水线修复一并复核编排段 |
| — | 上游已知（#9426，不在本组） | 本组无加载器筛选代码 | 清单登记「搜索 Mod 筛选 MC 版本时错误列举不支持加载器」在 `ResourceSearcher.vb:88`（资源搜索侧）；本页是本地 Mod 列表，不含加载器多选筛选 UI | 本页筛选仅 `FilterType.All/Enabled/Disabled/CanUpdate`（:586-592），无加载器枚举；`ChangeFilter` 仅读 `sender.Tag` 转枚举 | 跨组问题，本地页不涉及 | 不纳入 |

### 1.2 本组新发现（C# 侧）

| 严重度 | 状态 | 文件:行号 | 现象 | 证据 | 根因 | 建议 |
|---|---|---|---|---|---|---|
| 🟨中 | ⚠️可疑（可更新条目但无更新日志 URL 的旧缓存 / 畸形数据命中；右键更新按钮直达，路径上无 try/catch） | `MyLocalModItem.xaml.cs:643-647` | `Entry.ChangelogUrls` 为空列表时，`Entry.ChangelogUrls.First()` 抛 `InvalidOperationException`；`HasUpdate` 只要求 `UpdateFile is not null`（`LocalResourceFile.cs:232-238`），不保证 URL 列表非空。当前联网加载器在设置 `UpdateFile` 时会同时写入 changelog URL（`PCL.Download/LocalResourceOnlineLoader.cs:255/262/364/370`），但跨运行缓存回放或旧条目可残留空列表 | `string CurseForgeUrl = Entry.ChangelogUrls.FirstOrDefault(x => x.Contains("curseforge.com"));`；`string ModrinthUrl = Entry.ChangelogUrls.FirstOrDefault(...)`；`if (CurseForgeUrl is null || ModrinthUrl is null) { ModBase.OpenWebsite(Entry.ChangelogUrls.First()); }`；直达调用 `BtnUpdate_PreviewMouseRightButtonUp`（:636-640）无外层异常兜底；VB 对照 `MyLocalModItem.vb:462-475` 同形 | 空列表判空只做了 `FirstOrDefault` 分支，`First()` 未补「列表为空」兜底；URL 非空不是 `HasUpdate` 的不变量 | `First()` 前补 `ChangelogUrls.Count == 0` 判断，空时提示「无更新日志」并 return；或整段包 try/catch 走 warn 降级 |
| 🟨中 | ⚠️可疑（联网信息不全 / 旧缓存回放，`ProjectVersion` 为空或版本 / 显示名为空时命中） | `MyLocalModItem.xaml.cs:414-415`、`:434` | `GetUpdateCompareDescription` 链式空解引用：`ProjectVersion` 是可空字段（`LocalResourceFile.cs:169 public ResourceVersion ProjectVersion;`），`ResourceVersion.Version` / `Display` 均为可空字符串字段（`ResourceVersion.cs:36/50`）；任一为空时 `?? Display` 后仍为 null，`.Replace(".jar", "")` / 后续字符串拼接抛 NRE。`HasUpdate` 仅要求 `UpdateFile` 非空，不约束 `ProjectVersion` | `string CurrentName = (Entry.ProjectVersion.Version ?? Entry.ProjectVersion.Display).Replace(".jar", "");`；`string NewestName = (Entry.UpdateFile.Version ?? Entry.UpdateFile.Display).Replace(".jar", "");`；返回串直接拼接 `Entry.ProjectVersion.ReleaseDate - DateTime.Now`；消费点 `Refresh():473`（Tooltip）与 `BtnUpdate_Click:670`（确认框）；VB 对照 `MyLocalModItem.vb:298-315` 同形 | 上游未做三级判空（对象 → Version → Display）；内核当前在设 `UpdateFile` 前守卫了 `ProjectVersion is not null`（`LocalResourceOnlineLoader.cs:245/353`），但缓存 / 旧路径不构成保证 | 入口补 `ProjectVersion is null` 早退（用 `Entry.Display` 占位）；`Version ?? Display ?? ""` 后再 `.Replace`；`ReleaseDate` 取默认 `DateTime` 兜底 |
| 🟩低 | ⚠️可疑（列表描述走 `ResourceProject.Description` 可空字段；当前内核投影已 `?? ""`，旧缓存 JSON 构造路径仍可置 null） | `MyLocalModItem.xaml.cs:522` | `Entry.Project.Description.ReplaceLineEndings("")` 未判空：`ResourceProject.Description` 为 readonly string 字段，缓存 JSON 构造分支 `ResourceProject.cs:188 Description = (string)Data["Description"];` 未补 `?? ""`；字段为 null 时此处 NRE。`:524-526` 的 `Entry.Description` 分支已判空，Project 分支漏判 | `if (Entry.Project is not null) { NewDescription += ": " + Entry.Project.Description.ReplaceLineEndings(""); }`；对照下行 `else if (Entry.Description is not null) { ... }`；VB 对照 `MyLocalModItem.vb:368-371` 同形 | 两个描述源判空策略不一致；内核当前源（`PCL.Download/ResourceProject.cs:144/163/294`）均 `?? ""`，仅 legacy 缓存构造可传 null | 改 `(Entry.Project.Description ?? "").ReplaceLineEndings("")`；或在 `ResourceProject` 构造侧统一 `?? ""`（跨组） |
| 🟩低 | ⚠️可疑（`Title` 在 `Entry` 赋值前被触发 setter 的防御性隐患；当前调用顺序下不可达） | `MyLocalModItem.xaml.cs:36` | `Title` setter 首行无条件访问 `Entry.IsEnabled`；`Entry` 由 `Tag` 承载、初始为 null。当前构造顺序为先 `Entry = Entry` 后 `Checked/Title`（`PageInstanceMod.xaml.cs:175-181`），XAML 未在构造期写 `Title`，故运行不可达；若后续有外部先设 `Title` 的调用方即 NRE | `set { this.LabTitle.TextDecorations = Entry.IsEnabled ? null : TextDecorations.Strikethrough; ... }`；`Entry`  getter 直返 `this.Tag`（:101-111），无 null 守卫；VB 对照 `MyLocalModItem.vb:16` 同形 | 上游 VB 宽松类型容忍 null 解引用；C# 直译为强类型后未补防御 | setter 内 `Entry is not null` 时再设 TextDecorations，否则跳过 |
| 🟩低 | ⚠️可疑（`InstallMods` 为 `public static` 兜底缺失；现有两个调用点均保证非空枚举，属接口防御性问题） | `PageInstanceMod.xaml.cs:396` | `PathUtils.GetExtension(FilePathList.First())`：枚举为空时 `First()` 抛 `InvalidOperationException`，方法整体无 try。按钮路径 `BtnManageInstall_Click:386-388` 先 `!FileList.Any()` 早退；拖拽路径 `FormMain.xaml.cs:1467` 已先 `First()` 并存在性校验，故当前两入口不可达 | `string Extension = PathUtils.GetExtension(FilePathList.First());`；`:398` 扩展名比较前未判空；VB 对照 `PageInstanceMod.vb:287` 同形 | 上游 VB 未空枚举防御；静态公共方法未补前置检查 | 入口补 `if (!FilePathList.Any()) return false;` |
| 🟩低 | ⚠️可疑（`RefreshBars` 底部栏枚举 `Output` 无 null 守卫；加载前调用不可达，与同方法顶部枚举口径不一致） | `PageInstanceMod.xaml.cs:287` | 顶部计数循环 :215 / :254 均用 `Output ?? new List<LocalResourceFile>()` 兜底；底部选中按钮可用性循环 :287 直接 `foreach (... in LocalResourceLoaders.LocalResourceLoader.Output)`，`Output` 为 null 时 NRE。`Loader.Output` 在加载前未赋值 | `foreach (var ModEntity in LocalResourceLoaders.LocalResourceLoader.Output) { if (!SelectedMods.Contains(...)) continue; ... }`；对照 :215 `...Output ?? new List<LocalResourceFile>()...`；VB 对照 `PageInstanceMod.vb:195` 同形 | 翻译时两处枚举只给前两处补了兜底；当前 `RefreshBars` 触发点均在列表加载之后，属口径不一致隐患 | 统一补 `?? new List<LocalResourceFile>()` 兜底 |
| 🟩低 | ⚠️可疑（事件 / 旧条目处理器不退订；页面对象常驻 + 条目随刷新丢弃，当前无重复订阅面） | `PageInstanceMod.xaml.cs:73`、`:182`；`MyLocalModItem.xaml.cs:189-190` | ① `FrmMain.KeyDown += this.FrmMain_KeyDown` 在 `IsLoad` 一次性分支内订阅，`PageExit`（:52 仅 `UnselectedAllWithAnimation`）不退订；② `Entry.OnUpdate += (_) => RunInUi(NewItem.Refresh)` 在 `McModListItem` 每次构造时挂到新实体，旧实体连同旧 UI 一起被 `ModItems.Clear()` 丢弃，无显式退订；③ 条目内 `Changed/Click` 处理器经 `InitLate` 一次性 `ButtonHandler = null` 释放（:628-632），`Changed` 事件本身不退订 | `global::PCL.ModMain.FrmMain.KeyDown += this.FrmMain_KeyDown;`；`Entry.OnUpdate += (_) => ModBase.RunInUi(NewItem.Refresh);`；`sender.Changed += ...` / `sender.Click += ...`；VB 对照 `PageInstanceMod.vb:20/104/110-111` 同形 | 上游 VB 常驻页模型依赖对象生命周期兜底；页面对象 `ModMain.FrmInstanceMod` 常驻，KeyDown 处理器不会被重复累加 | 若页面支持多实例 / 可重建，在 `PageExit` 补 `-=` 退订；当前模型可仅备注 |
| 🟩低 | ⚠️可疑（多 Mod 同时更新时目标临时文件名碰撞 → 字典重复键；异常被外层 catch 记 warn，更新整体不启动） | `PageInstanceMod.xaml.cs:804-807` | `TempAddress` 由 `Entry.File.Name.Replace(CurrentReplaceName, NewestReplaceName)` 派生；`FileCopyList` 为普通 `Dictionary<string,string>`，两个 Mod 更名后落到同一临时名时第二次 `FileCopyList[TempAddress] = RealAddress` 抛 `ArgumentException`（重复键）；随后被 :876-879 外层 `catch` 记 warn「初始化 Mod 更新失败」，整批更新不启动 | `string TempAddress = ModBase.PathTemp + @"mods\" + Entry.File.Name.Replace(...) ;`；`string RealAddress = Path.Combine(...Entry.File.Name.Replace(...));`；`FileCopyList[TempAddress] = RealAddress;`；外层 `catch (Exception ex) { Logger.Warn(ex, "初始化 Mod 更新失败"); }`；VB 对照 `PageInstanceMod.vb:580-583` 同形 | 同名替换后未做碰撞去重；与 #9360 并发流水线不同，此处是单次批内同名碰撞 | 碰撞时临时名追加序号 / Guid 后缀；或字典改 `ConcurrentDictionary` 并在键冲突时记 warn 跳过该项 |

### 1.3 未列为缺陷的可疑观察（仅备注，不计入发现）

- `SelectedMods`（`PageInstanceMod.xaml.cs:483`）为普通 `List<string>`，全部在 UI 线程事件处理器内增删（勾选 :494-499、全选 :508-515、删除 :916、过滤清理 :236），后台联网加载器不枚举该列表；当前无并发读写面。`ModItems` 为 `ConcurrentDictionary`（:138），与 VB 一致。
- `Entry.OnUpdate` 由联网 setter 在后台线程触发（`LocalResourceFile.cs:161/183`），本页挂载处已包 `ModBase.RunInUi(NewItem.Refresh)`（:182），跨线程封送正确；`Refresh` 内控件访问在 UI 线程执行。
- `RefreshBars:287` 之外，`SearchRun:1077` 同样直接枚举 `Output`；触发前提是搜索框有文本，而搜索框在 `LoadUIFromLoaderOutput:164` 与 `ReloadModList:89` 先于 / 随列表加载置位，`Output` 此时已赋值，NRE 不可达，不单独列项。
- 滑动选中的 `SwipeStart/SwipeEnd/_Swiping/SwipToState` 为 `static`（`MyLocalModItem.xaml.cs:150-165`）：本页只有一个 `PanList` 面板，无多列表共存面；`Swiping` setter 直访 `ModMain.FrmInstanceMod.CardSelect`（:162）未判空，滑动事件在页面已注册后才可能触发，不可达。
- `Buttons` setter 内 `!value.Any()`（:355）与 `Tags` setter 内 `value.Any()`（:81）未判 null；当前内核投影保证 `Tags` 非空列表（`PCL.Download/ResourceProject.cs:149/207`），`Buttons` 由本页 :205 传入数组；仅 legacy / 外部构造可传 null，不升级为缺陷。
- 扩展名大小写：安装 / 扫描扩展判定经 `PathUtils.GetExtension`（`MeloongCore/Shared/Utils/PathUtils.cs:200-203`）统一 `.Lower()`，`InstallMods:398` 与拖拽 :1487 比较已大小写无关；`LocalResourceFile.IsEnabled:40` 同样 `.Lower()`。
- `InstallMods:436-439` 目标名取自 `LocalResourceFile.EnabledName`（源自 `File.Name`，`FileInfo` 不保留 `..\` 穿越段），路径拼接走 `Instance.PathIndie`；本页未见路径穿越直接入口，穿越判定在 `FileUtils.Move/Copy` 兜底（跨组）。
- 异常吞噬与状态复位：`EDMods` 区分 `FileNotFoundException`（:686 重载列表 + return）与其他异常（:692 置 `IsSuccessful=false` 继续循环）；`DeleteMods` 区分 `OperationCanceledException`（:904/951 重载 + return）与其他异常；失败后均走 `ReloadModList(true)` 或 `LoaderRun(UpdateOnly)` 复位列表，状态复位链与 VB 一致，未发现「异常后状态残留」新问题。
- XAML 事件签名核对：`MyRadioButton.Check` = `(object, bool)`、`MyLoading.StateChanged` = `(object, MyLoadingState, MyLoadingState)`、`MyIconButton.Click` = `(object, EventArgs)`、`MyIconTextButton.Click` = `(object, RouteEventArgs)`；构造器 lambda 形参 / 强转均匹配（:31-50），无签名错位；`Info_Click(object, EventArgs)` 订阅 `MouseRightButtonUp` 经参数逆变兼容。
- `PageInstanceMod.xaml.cs:113` `ModMain.FrmInstanceLeft.ItemMod.Checked = true;` 未判 `FrmInstanceLeft`；与 VB :51 同形，页面由版本左栏进入时该对象已存在，属上游既有口径，不纳入。

---

## 二、覆盖清单（逐文件核对结论）

### 2.1 `PageInstanceMod.xaml.cs`（1111 行）— 新发现 4 条（🟩低 4），无 🟨/🟥

- **初始化（:26-130）**：构造器订阅 18 处控件事件（:31-50）、`Loaded` / `PageExit`（:51-52）；`LoaderInit()` 在构造期直调（:55，注释说明 `Initialized` 事后订阅收不到）；`PageOther_Loaded` 用 `IsLoad` 守卫 KeyDown 一次性订阅；`ReloadModList` → `LoaderRun`；静态 `Refresh()` 清缓存 + 刷新页 + 左栏勾选。事件退订观察见 1.3；`FrmInstanceLeft` 判空观察见 1.3。
- **UI 化（:134-238）**：`LoadUIFromLoaderOutput` 空列表切 `PanEmpty` / 有列表切 `PanBack`，`ModItems.Clear()` 重建缓存，`SearchBox.Text=""` 触发 `SearchRun`；`McModListItem` 构造条目并挂 `OnUpdate`（RunInUi 封送）；`McModContent` 挂 Changed/Click 与四个图标按钮；`RefreshUI` 按筛选 + 搜索结果重排 `PanList.Children`，并回收不可见项的 `SelectedMods`。`ModItems` 键为 `EnabledName`，`TryGetValue` 未命中跳过。
- **顶栏 / 底栏计数（:243-349）**：顶部循环两处有 `Output ?? new List` 兜底；底部 :287 一处无兜底（1.2 #6）；`BottomBarShownCount` 仅在数量增加时播动画；动画受 `AniControlEnabled` 闸控制，else 分支直设状态。
- **管理操作（:359-476）**：打开文件夹 try 兜底；全选按 `PanList.Children.Count` 对比；`InstallMods` 扩展名白名单、回收站路径提示、版本 / 页面三分支取舍（C# 用 `DoInstall` 标记位等价 VB `GoTo Install`）、逐文件删旧 + 复制、成功提示、列表 ForceRun 刷新；空枚举 `First()` 兜底缺失（1.2 #5）；下载按钮预填版本与搜索词后跳下载页。
- **选择（:483-542）**：`CheckChanged` 受动画闸；`ChangeAllSelected` 清选后按「是否在 `PanList.Children`」决定勾选（#4992）；`UnselectedAllWithAnimation` 临时置 0 绕过动画闸；`FrmMain_KeyDown` Ctrl+A 全选，非本页 `PageRight` 早退；搜索框 Ctrl+A 空文本时转焦点。
- **筛选（:547-630）**：`Filter` 属性切换单选钮 + `RefreshUI`；`CanPassFilter` 四态；`ChangeFilter` 读 `Tag` 转 `FilterType`；与 XAML `Tag="0..3"` 一一对应。
- **启用 / 禁用（:637-732）**：`BtnSelectED_Click` 按「是否等于禁用按钮」反推 IsEnable；`EDMods` 逐文件：禁用拼 `.old/.disabled`、启用取 `EnabledFullName`；目标已存在时哈希比对，内容不同弹冲突框跳过；`FileNotFoundException` 重载返回、其他异常置失败；新实体 `FromJson` 复制网络信息后替换 Loader 列表 / 搜索结果 / UI 树三处；失败提示 + 重载；末尾 `LoaderRun(UpdateOnly)`。
- **更新（:735-880）**：选中且 `HasUpdate` 列表；>15 个或未确认过时弹风险框；`ModList.ToList()` 固化迭代；前后导相同段名剥离；构造下载 `NetFile` 与 `FileCopyList`；下载 Loader + 替换任务（先删旧、再 Move 新）；`OnStateChanged` 三态提示 + 从 `UpdatingInstanceModFolders` 移除 + 后台线程清临时缓存；启动后 `ReloadModList(true)`；外层 catch 记 warn。字典碰撞观察见 1.2 #8；#9360 归属见 1.1。
- **删除（:883-962）**：Shift 判定回收站；`SelectMany` 拼启用 / 禁用双路径 + `Distinct` + 存在性过滤；逐文件删除（取消异常重载返回；其他异常置失败）、取消选中、移除 Loader / 搜索结果 / `ModItems` / UI 树；成功 / 失败 / 清空三分支提示与重载；末尾 `LoaderRun(UpdateOnly)`。
- **单项详情 / 定位 / 删除 / 启停（:975-1057）**：`Info_Click` 双形态解引用（按钮取 `Tag`，条目取自身）；有 `Project` 跳资源详情页，无则拼文件 / 版本 / 描述 + 驼峰拆词搜索串弹 mcmod；整段 try 兜底；`Open_Click` 定位文件；`Delete_Click` / `ED_Click` 取 `Tag` 转发；`ModOriginalName.Substring(0,1)` 在 Display 非空前提下安全（`Display` getter 保证非空，`LocalResourceFile.cs:79-96`）。
- **搜索（:1063-1106）**：`IsSearching` 按搜索框空白判定；`SearchRun` 拼名称 / 文件名 / 版本 / 描述 / 工程名 / 译名 / 工程描述 / 标签多权重 `SearchEntry`，调 `ModBase.Search` 得 `SearchResult` 后 `RefreshUI`；`Tags` 为空集合时 `string.Join` 安全。

### 2.2 `MyLocalModItem.xaml.cs`（773 行）— 新发现 4 条（🟨中 2、🟩低 2），无 🟥

- **基础属性（:23-111）**：`Title` setter 访问 `Entry.IsEnabled`（1.2 #4）；`SubTitle` getter 用 `?.Text` 兜底；`Description` 直读 `LabInfo`；`Tags` write-only setter 清标签树后逐个建 `Border/TextBlock`（null 观察见 1.3）；`Entry` 走 `Tag` 双向承载。
- **点击与勾选（:118-293）**：`Click` / `Check` / `Changed` 三事件；`MouseUp` 仅在 `IsMouseDown` 触发；`MouseDown/MouseLeave` 切 `ButtonStack.IsHitTestVisible`；滑动选中静态状态 + 范围勾选；`Checked` setter：原值早退、`Changed` 被 Handled 时回滚 `_Checked`、`Check` 被 Handled 时早退；可见走动画 / 不可见直设 `RectCheck` 高宽不透明度与标题前景；整段 try/warn。
- **后加载控件（:301-408）**：`RectBack` 惰性插入 `Children[0]`；`Buttons` setter 移除旧 Stack、空集合早退、建新 `StackPanel` 加四个按钮（NaN 宽高补 25）；`RectCheck` 惰性建勾选条，初始高宽 / 对齐 / 边距按 `Checked` 初值取。
- **更新对比文案（:412-435）**：段名剥离后改写 `Entry._Version`；返回「当前 / 最新版本 + 时间差」串；链式空解引用见 1.2 #2。
- **懒加载 Refresh（:438-583）**：构造器订阅 14 处事件（与 VB `Handles` 逐条核对一致，见 1.3）；`FirstEnterScrollViewerViewport` 置 `HasEnteredViewport` 后 `Refresh`；`Refresh` 按本地名样式开关分两种标题 / 描述版式；工程描述空判观察见 1.2 #3；勾选态前景色、主 Logo（工程图 / NoIcon）、禁用角标 `ImgState` 增删、标签树刷新。
- **颜色 / 悬停动画（:585-623）**：`InitLate` 一次性触发 `ButtonHandler`；悬停时按钮栈不透明度 + 右列宽动画；`RectBack` 背景色 / 不透明度 / 缩放动画；`ButtonStack` 存在才动列宽；`RectBack.RenderTransform` 为 `ScaleTransform`（惰性构造时定型）。
- **更新日志 / 更新按钮（:636-687）**：右键直达 `ShowUpdateLog`（空 URL 列表异常见 1.2 #1）；左键三态确认：更新 → 调 `FrmInstanceMod.UpdateMods({Entry})`、查看日志、取消。
- **标题自适应（:690-770）**：三档压缩（舒展 / 压副标题 / 压标题）按列宽实际值与文本裁剪判定，`SizeChanged` 内切换三列 `GridLength`；与 XAML 三列 `ColumnTitle/ColumnSubtitle/ColumnExtend` 对应。

### 2.3 覆盖边界与未纳入项

- 目录内其余文件（`PageInstanceExport/Left/Overall/Setup/ModDisabled` 等）不在本组分配范围，未审；`.vb` 仅作语义对照，其内部问题未上报。
- 扫描扩展名 / 隐藏文件 / 锁定文件 / 去重 / 排序实现在 `PCL.Download/LocalResourceScanner.cs`（跨组）；本页消费其 `Output`，未见本层二次过滤缺陷。
- `#9360`（并发更新替换失败）归属下载流水线；`#9426`（加载器筛选）归属资源搜索侧；均不在本组文件，复核结论见 1.1。
