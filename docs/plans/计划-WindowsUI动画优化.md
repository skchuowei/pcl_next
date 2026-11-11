# 计划：Windows UI 动画优化与缺口补齐

> 依据：《AGENTS.md》§零（编译产物位置）、§一（开工前置）、§二（架构冲突须先确认）、
> §3.1 / §3.2（大改动专项计划，8 项必备章节）、§五（提交规则）；
> 《分支与发布规范.md》§5.2.1（构建号）、§6.1（远端纪律）；
> 《计划-dev7-全量CSharp重写.md》（全量 C# 重写，隔离工作区进行中）；
> 《公布标准.md》§3（发布门槛）。
>
> **状态**：规划阶段，**待用户裁决后方可开工**。
> **范围**：仅 Windows 线 `PCL_NEXT_Windows` / 分支 `dev.6-Windows`。
> Linux / Android 为独立仓库，完成后各自移植，不互拷、不合并（2026-11-08 三仓库架构裁决）。
> **本轮产出**：仅本计划文档。未修改任何源码、构建脚本、规则文档；未暂存、未提交、未推送。
> **依据的分析**：2026-11-11 对 `pcl-next/` 的只读动画审查（三路：控件层 / 页面导航层 / 主题与性能层）。
> 文中行号以 **2026-11-11 主线 VB 版本**为准；dev.7 转换后行号失效，一律按符号名 / 组名定位。

---

## 0. 决策记录、勘误与待裁决项

### 0.1 本轮用户指令链

| 时点 | 指令 | 本计划的处理 |
|---|---|---|
| 09:11 | 「看 WindowsUI 框架的动画……不动代码」 | 只做只读审查，产出问题清单与优化方向，零改动 |
| 09:42 | 「写一个计划」 | 本文件。按 AGENTS §3.2 把清单落成专项计划 |

本计划本身**不含代码改动**。所有代码动作均为「裁决通过后、后续轮次」的执行项。

### 0.2 关键勘误（必须留痕，避免后续按错误结论施工）

上一轮口头分析把 `Controls\MySliderDot.xaml.vb` 的「hover / 键盘反馈整段注释、DragStop 放大到 2.3 不复位」列为活跃缺陷。
**该结论不成立，本计划不为其投入修复**：

- `pcl-next\pcl-next.vbproj` L111-112 有 `<Compile Remove="Controls\MySliderDot.xaml.vb" />`
  与 `<Page Remove="Controls\MySliderDot.xaml" />`，两个文件**编译期即被排除**；
- 全仓库检索 `MySliderDot` 仅命中这两处 Remove 与文件自身声明，无任何引用；
- 《计划-dev7-全量CSharp重写.md》§3.1 也已把它列入「显式排除（死代码）」。

处置：随 dev.7 清理，或在单独清理死代码的提交中删除；**若将来决定启用该控件**，需先补齐
`RefreshColor`（L104-135）、按键处理（L139-167）并把 `DragStart/DragStop`（L82/L93）的
2.0 / 2.3 改为与 `MySlider`（L88/L104：1.3 → 1.0）一致的复位语义，再单独验收。
本计划后续章节不再把它计入 P0 缺陷数。

### 0.3 与 dev.7 全量 C# 重写的关系（本计划的核心架构约束）

只读核查（2026-11-11）事实：

| 项 | 现状 |
|---|---|
| 主线 `pcl-next/` 语言构成 | 123 个 `.vb`、0 个 `.cs`，仍是纯 VB 工程（`.vbproj`） |
| dev.7 状态 | 用户 2026-11-08 裁决「仓库不留 VB 代码」，转换在隔离工作区 `D:\bing\MC\pcl_x\.vb2cs-trial` 进行；主线保持可发布的 VB 版本 |
| 最近提交 | `7f7242a fix(dev7)`、`026cb33 plan(dev7)`，转换工具链已装，尚未合入主线 |
| 影响 | 本计划若现在批量改控件（约 40+ 文件），同一批逻辑要在 VB 侧与转换后的 C# 侧各做一遍，或与转换产物冲突 |

按 AGENTS §二第 2 条，此处属于「与现有架构存在冲突」，**必须先向用户指出并提供替代方案，
经确认后再执行**。三条候选路径：

| 路径 | 做法 | 代价 | 适用 |
|---|---|---|---|
| A（推荐） | P0 / P1a 在 VB 主线立即做（改动小、可立即见效、可作为 dev.7 转换后行为等价的观感基线）；P1b / P2 在 dev.7 合入后的 C# 侧做；P3 独立立项 | 需在 dev.7 移植清单登记 P0 / P1a 的对应改动 | 想尽快改善观感，又不想重复劳动 |
| B | 全部等 dev.7 合入后在 C# 侧做 | 观感问题在 dev.7 完成前不改善；但零重复劳动、零冲突 | 优先保 dev.7 进度 |
| C | 全部在 VB 主线做，转换后再手工同步 | 改动量最大，转换后需逐文件比对 | 不推荐 |

**推荐 A**，理由：P0 约 4 处、P1a 约 1 处默认值 + 1 个新增常量文件，改动合计不足 30 行，
是「收益 / 冲突面」比最高的一段；而 P1b「全量替换硬编码时长」与 P2「逐页补入场动画」
会触及几乎全部控件与页面，必须等 C# 骨架稳定后一次成型。

### 0.4 待用户裁决清单

| # | 待裁决 | 推荐 | 阻塞范围 |
|---|---|---|---|
| D1 | 采用 A / B / C 哪条路径 | A | 全文施工顺序 |
| D2 | 默认缓动改成什么（`AniEaseOutFluent(Weak)` / `Middle` / 维持 Linear 只改调用层） | `OutFluent(Weak)`，颜色 / 透明度 100-150ms | P1a |
| D3 | 是否把 P3「引擎重构」纳入本计划，还是另立专项 | 另立专项，本计划只做设计约束登记 | P3 |

D1 未裁决前，不得开工任何代码改动。

---

## 1. 目标

### 1.1 分期目标

| 阶段 | 目标 | 性质 |
|---|---|---|
| **P0** | 清除 4 处确定性缺陷：滚动匿名组、页面切换组名冲突、`AniStop` 名字不匹配、禁用态不上色 | 修 bug，行为不变 |
| **P1a** | 引擎默认缓动从 Linear 改为统一缓动；新增动画时长 / 缓动常量表；把「应保留线性」的调用点显式化 | 观感，改动最小 |
| **P1b** | 控件层硬编码时长收敛到常量表；消除同组同属性并行与组名复制残留 | 一致性，随 dev.7 C# 侧做 |
| **P2** | 补齐高频缺口：列表重建 stagger、启动首页入场、提示条 / 加载器入场、下拉框展开、进度文字同步、键盘焦点反馈 | 补动画，逐页回归 |
| **P3** | 引擎重构：绝对插值替代增量累加、渲染帧驱动、系统级减弱动态效果、布局属性迁 RenderTransform、颜色 HSL 插值、`AniGroups` 线程安全 | 另立专项，本计划只登记设计约束 |

### 1.2 可验证标准

1. **构建**：`dotnet build pcl-next/pcl-next.vbproj -c Release -r win-x64 --self-contained -p:PclChannel=Dev` **0 错误**；
   产物落在 `pcl-next\bin-net10\Dev\<版本名>-<构建号>\win-x64\`（§零）。
2. **回归**：`PCL.Download.Tests` 全绿（dev.7 计划基线 568 例）；`MeloongCore.Tests` 全绿。
3. **P0**：连续滚轮 20 次无位置跳变；快速连点左侧导航 10 次无空白页 / 无异常；
   禁用态切换可上色；`AniStop("PageLeft PageChange ...")` 能命中实际注册的组。
4. **P1a**：全量检索 `AniStart` / `Aa*` 调用点，「省略 Ease 且语义应减速」的调用点 100% 显式化或享受新默认；
   hover 时长从现有 8 档（40/70/90/100/120/130/150/300ms）收敛到 100 / 150ms 两档以内。
5. **P2**：冒烟清单（§7.3）全部通过；列表重建不再整页闪烁，逐项 stagger 可见；
   窗口启动首页有入场动画；键盘 Tab 导航焦点可见。
6. **P3**（另立专项）：空闲时动画线程不再每约 3ms 同步派发；动画结束终值误差为 0；
   `SystemDebugAnim >= 30` 时直接设终值而非 200× 快进；系统「减弱动态效果」开启时全链路（含
   `Application.xaml` L420 的 Storyboard 与 `PopupAnimation="Fade"`）一致降级。

---

## 2. 前置条件与阻塞项

| 依赖 | 状态 | 备注 |
|---|---|---|
| D1 / D2 / D3 裁决 | **阻塞** | 见 §0.4，未裁决不得开工 |
| dev.7 转换合入主线 | 未合入 | 决定 P1b / P2 落 VB 还是 C#；路径 A 下 P0 / P1a 不等它 |
| 基线回归（AGENTS §1.3） | 待跑 | 动 `ModAnimation.vb` 前必须完整构建 + 基线回归；纯文档阶段豁免 |
| UI 自动化测试 | **缺失** | 现有测试只覆盖内核（`PCL.Download.Tests`）与 `MeloongCore.Tests`，UI 无自动化；P0 / P2 靠 §7.3 人工冒烟，建议 P2 同期补 UI 冒烟脚本（对齐 dev.7「后补脚本」裁决） |
| 并行会话未提交改动 | 存在 | 2026-11-11 `git status` 有 `AGENTS.md`、多份 `docs/plans/*.md`、`公布标准.md`、`分支与发布规范.md` 及未跟踪 `scripts/Fix-CSharpSemantics.py` 等非本轮改动。**一律不碰、不暂存** |
| 合规上限 | 不变 | 上游书面答复留存前，不扩大分发、不打平台 tag、不分发非 Windows 制品 |
| 版本常量 | 不变 | `VersionBaseName` / `VersionCode` 由发布脚本写入，功能提交一律不改（§五.5） |
| 行号基准 | 已固化 | 见文首；转换后按符号名定位，禁止按行号机械套用 |

---

## 3. 现状分析

### 3.1 量化基线（2026-11-11 只读核查）

| 项 | 值 |
|---|---|
| 主线语言 | `pcl-next/` 下 123 个 `.vb`、0 个 `.cs`；63 个 `.xaml` |
| 动画引擎 | 单文件 `Modules\Base\ModAnimation.vb`（1066 行），自研逐帧引擎 |
| 缓动 | 1 个抽象基类 `AniEase` + **12 个具体缓动类**（L566-742）：Inout / Linear / InFluent / OutFluent / InoutFluent / OutFluentWithInitial / InBack / OutBack / InCar / OutCar / InElastic / OutElastic |
| 默认参数 | 所有 `Aa*` 的 `Ease` 默认 `Nothing` → `.Ease = If(Ease, New AniEaseLinear)`；`Time` 默认 400ms |
| 现成但未用 | `AaStack`（L523，自带 stagger，Delay 默认 25）与 `AaTextAppear`（L434）**全项目零调用点** |
| 原生动画 | 全仓库 `.xaml` 仅 `Application.xaml` L417-441 一处 `Storyboard`（ToggleButton 箭头 0.2s CircleEase）；无 VisualState / VisualTransition |
| 调用点规模 | `Controls/` + `Pages/` 下约 40 个文件含 `AniStart`，控件层 hover / 禁用 / 勾选动画大多省略 Ease |

### 3.2 引擎管线的四环问题

| 环节 | 代码 | 问题 |
|---|---|---|
| 后台线程 | L805-850 `Do While True` + `Thread.Sleep(1)`，L817 `DeltaTime < 3` 才推进 | 无退出条件，**无动画时空转**；`Sleep(1)` 在 Windows 实际约 15ms，帧间隔不稳 |
| 同步派发 | L830-842 `RunInUiWait` → `Dispatcher.Invoke`（`ModBase.vb` L936-954） | 不与 `CompositionTarget.Rendering` 对齐；UI 忙时整链阻塞；每帧一次同步往返 |
| 逐帧累加 | L936 `Delta = Lerp(0, Value, Ease.GetDelta(t1,t0))`，L941-994 / L1010-1012 `+= Delta` | 非「起点→终点」插值；只有 Color 用 `ValueLast` 补小数（L1000-1005），其余类型无补偿 → 终值漂移、`AniStop` 只 `Remove`（L782）导致残留、不同名组可同时改同一属性 |
| 写入属性 | `AaX/AaY` 写 Margin、`AaWidth/AaHeight` 写 Width/Height、`AaScale` 三者都写 | 每帧触发布局重算；引擎已有 `TranslateTransform` / `ScaleTransform` 实现（L983-986 / L1039-1056）却只在部分控件使用 |

附带的两个全局口径问题：

- **「关闭动画」实为 200× 快进**：`AniSpeed` 在 `SystemDebugAnim >= 30` 时返回 200（L13-18），
  L833 `AniTimer(DeltaTime * AniSpeed)` 把每帧时间放大；动画组与 `AaCode` 回调仍完整跑一遍，
  可能一帧内连锁触发状态机。无系统级「减弱动态效果」开关（`Settings.vb` 无相关键）。
- **颜色在 RGB 空间加减**（L371 注释、`ModBase.vb` L275 `MyColor.Lerp`），跨色相过渡经过灰暗中间色；
  而主题色由 HSL 生成（`ModSecret.vb` L74-81），插值空间不一致。

### 3.3 调用层三类问题

1. **默认值即观感**：绝大多数 `AaColor` / `AaOpacity` 省略 Ease → 全部匀速，这是「生硬」的主因。
2. **时长无单一真源**：同类 hover 从 10ms（`MyComboBox` L73、`MyTextBox` L249 聚焦）到 300ms
   （`MyComboBoxItem` L14）；同类显隐 `MyExtraButton` 用 500/200/100/400ms，`MyExtraTextButton`
   用 80/400/160/50/100ms；同类进度 `MySlider` 与 `MySliderDot` 分别是 `*300+100` / `*400+100`。
3. **命名组纪律松散**：`AniStart` 仅同名互斥（L765-769）。现存三类风险——
   匿名组（`MyScrollViewer` L23、`MyMsg*` 遮罩淡出）旧组不停；
   同名组跨业务互停（`MyPageRight` L184/L196 的 400ms 纯 `AaCode` 与 L427/L461 进出场共用
   `"PageRight PageChange "`）；
   全局固定名（`MyMsg*` 的 `"PanMsg Background"`）跨实例共用。
   另有 `FormMain.xaml.vb` L1591 `AniStop("PageLeft PageChange")` 与 `MyPageLeft` 实际注册的
   `"PageLeft PageChange " & Uuid` 不匹配。

### 3.4 缺口清单（按用户感知强弱）

| 级别 | 场景 | 位置 |
|---|---|---|
| 高 | 窗口启动首页硬切 | `FormMain.xaml.vb` L381-385（`PageState = ContentStay` 跳过 ContentEnter） |
| 高 | 长列表 `Children.Clear()` + `.Add()` 瞬间重建：Mod 列表搜索 / 筛选 / 删除 / 启停、资源详情筛选与文件列表、资源搜索结果、帮助搜索、版本选择、下载安装各子列表 | `PageInstanceMod` L139/144/514/699、L809-835；`PageDownloadCompDetail` L119/204/214；`PageResource` L180-186；`PageOtherHelp` L149-158；`PageSelectLeft/Right`；`PageDownloadInstall` 十余处 |
| 中 | 下拉框展开 / 收起、选中项 | `MyComboBox` L104-116 / L138；`MySlider` L106/110/190 Popup |
| 中 | 只有退场没有入场 | `MyHint` L37-55 / L102；`MyLoading` 由 `MyPageRight` L87/L395 直接 Visibility；`MyCard` 折叠内容 `SwapControl.Visibility` |
| 中 | 进度条有过渡、文字却跳变 | `PageLaunchLeft` L731/750（进度条 L767-797 有 `AaGridLengthWidth`）、`PageSpeedLeft` L41-53 |
| 中 | 主题色 / 背景色切换瞬间替换资源字典 | `ModSecret.vb` L86-105 |
| 低 | 键盘焦点与禁用态无可见反馈 | `MyRadioButton` L169 Handles 缺 `IsEnabledChanged`；`MyTextButton` L63-72 缺 IsEnabled 分支；`MyComboBox` 缺 `LostKeyboardFocus` |
| 低 | 页面切换进出场时长严重不对称、退场全线性 | `MyPageRight` L441-442（70ms 线性）；`MyPageLeft` L77-78（50ms 线性） |

### 3.5 根因判断

不是「动画做得少」，而是三层失守叠加：

1. **引擎默认值选错**（Linear 兜底）→ 调用层不写 Ease 就必然匀速；
2. **没有时长 / 缓动常量表** → 40 余个文件各自硬编码，同类交互分出 8 档时长；
3. **没有「列表变更」这一动画原语** → 引擎有 `AaStack` 却没人用，所有列表刷新都退化成 Clear+Add。

因此 P1a 改一处默认值 + 建一张常量表，收益远大于逐控件调参；P2 的关键也是先补一个
「列表项入场 / 退场」的公共封装，而不是逐页手写 stagger。

---

## 4. 改动文件清单

### 4.1 分期文件表

| 阶段 | 文件 | 范围 | 语言侧（路径 A） |
|---|---|---|---|
| P0 | `Controls\MyScrollViewer.vb` | L22-29：命名组 + 停止旧组；L30-33：`RealOffset` 写入职责 | VB 主线 |
| P0 | `Controls\MyPageRight.vb` | L184/L196/L290 的 400ms `AaCode` 拆独立组名；L427/L461 保持 | VB 主线 |
| P0 | `FormMain.xaml.vb` | L1591 `AniStop` 名字修正；L1608/1627 的 110ms 换 Child 时序 | VB 主线 |
| P0 | `Controls\MyRadioButton.xaml.vb`、`Controls\MyTextButton.vb` | 补 `IsEnabledChanged` / IsEnabled 分支 | VB 主线 |
| P1a | `Modules\Base\ModAnimation.vb` | 所有 `Aa*` 的 `If(Ease, New AniEaseLinear)` 默认值（约 18 处）；不改签名、不改数据结构 | VB 主线 |
| P1a | **新增** `Modules\Base\AniTokens.vb` | 时长 / 缓动常量表（hover in/out、禁用、聚焦、进出场、stagger 步长、弹窗） | VB 主线，转换时单独移植 |
| P1b | `Controls\` 下约 20 个控件 | 硬编码时长替换为 token；同组同属性并行拆分；组名复制残留修正 | C# 侧（dev.7 合入后） |
| P2 | **新增** 列表动画封装（建议置于 `Modules\Base\`） | 包装 `AaStack` 语义：入场 stagger、退场 stagger、增量刷新差分化 | C# 侧 |
| P2 | `Pages\PageInstance\PageInstanceMod.xaml.vb`、`Pages\PageDownload\Resource\PageResource.xaml.vb` 等 | 首批接入列表封装 | C# 侧 |
| P2 | `FormMain.xaml.vb`、`Controls\MyHint.xaml.vb`、`Controls\MyLoading.xaml.vb`、`Controls\MyComboBox.vb` | 启动首页 / 提示条 / 加载器 / 下拉框入场 | C# 侧 |
| P2 | `Modules\ModSecret.vb` | 主题色切换过渡（可选，见 §4.2） | C# 侧 |
| P3 | `Modules\Base\ModAnimation.vb` 全量 | 另立专项，本计划只登记约束 | 另立专项 |

**改动规模判定**：跨 3 个以上模块、预计超 200 行、需多轮迭代 —— 满足 AGENTS §3.1 大改动，
故必须先有本专项计划。

### 4.2 明确不做（边界）

1. **不改任何版本常量**（`VersionBaseName` / `VersionCode` / `AssemblyInfo`）、不打 tag、不写更新日志、不推远端。
2. **不改分支纪律与规则文档**：`AGENTS.md`、`分支与发布规范.md`、`公布标准.md` 本轮及后续各阶段均不改；
   本文件是专项计划，不升格为长期规则（§四）。
3. **不碰 `MySliderDot`**（§0.2 勘误：死代码，随 dev.7 清理）。
4. **不处理「主题设置滑块不生效」**：`ThemeRefresh`（`ModSecret.vb` L69-105）不读取
   `UiLauncherHue/Sat/Light` 属功能缺陷，另立 issue，不混入动画计划。
5. **不重做 UI 架构**：不引入 MVVM、不换成 WPF 原生 `VisualStateManager` 全量替代
   （P3 是否换引擎另立专项裁决）；不改 XAML 布局与 `x:Class`。
6. **不跨仓库**：Linux / Android 两条独立仓库不复制本计划改动，各自按本计划的设计口径移植。
7. **不清理工作区其他产物**：`ReleaseArtifacts\` 归档、`bin-net10\` 既有输出、并行会话的
   dirty 文件一律不动。
8. **本轮不编译、不构建、不提交**：纯文档阶段按 AGENTS §1.3 豁免基线构建。

---

## 5. 改动要点

### P0 确定性缺陷（路径 A：VB 主线，约 4 处、< 30 行）

| # | 文件 | 动作 | 注意 |
|---|---|---|---|
| P0-1 | `MyScrollViewer.vb` L23 | `AniStart` 传稳定组名（如 `"ScrollViewer Offset " & Uuid`），每次滚轮先停旧组 | 保持 300ms `AniEaseOutFluent(6)` 手感不变；不动 `DeltaMult` 与滚轮过滤逻辑（L8-18） |
| P0-2 | `MyPageRight.vb` L184/L196/L290 | 400ms 纯等待 `AaCode` 改用独立组名（如 `"PageRight LoaderWait "`），不再占用 `"PageRight PageChange "` | 状态机 `PageStates` 不变；`PageOnForceExit`（L236-248）的 `AniStop` 对象保持 |
| P0-3 | `FormMain.xaml.vb` L1591 | 停 `"PageLeft PageChange"` 的意图是停「左侧子页切换」，与 `MyPageLeft` 的带 Uuid 组名不同源；补注释并按实际注册名停止自有组 | 需先复核 `PageDownloadLeft` 等 L89 的注册名，避免误停 |
| P0-4 | `FormMain.xaml.vb` L1608/1627 | 退场总时长 `70 + 15×N` 常超 110ms 换 Child 的时点；把换 Child 的 `AaCode` 延迟改为「退场组结束回调」或按项数计算 | 属时序调整，需 §7.3 快速连点冒烟验证 |
| P0-5 | `MyRadioButton` L169 / `MyTextButton` L63-72 | 补 `IsEnabledChanged` 句柄与 IsEnabled 分支 | 与现有 `RefreshColor` 的其它分支保持一致配色，不新增色值 |

### P1a 观感统一（路径 A：VB 主线）

1. `ModAnimation.vb`：把各 `Aa*` 工厂中 `.Ease = If(Ease, New AniEaseLinear)` 的兜底改为
   `AniTokens.DefaultEase`（D2 裁决值，推荐 `New AniEaseOutFluent(AniEasePower.Weak)`）。
   **签名与 `AniData` 结构一律不动**，只换兜底对象。
2. **先分类再改**：全量检索省略 Ease 的调用点，分三类处理——
   - 需保持匀速的（进度条线性段、`MySlider` L56-58 显式 `AniEaseLinear`、滚动缓动、
     `AaCode` 无 Ease）→ **显式写下 `New AniEaseLinear`**，避免被新默认误伤；
   - 颜色 / 透明度 / 位移 / 缩放 → 享受新默认；
   - 已有显式 Ease 的 → 不动。
   这一步是 P1a 的主要回归控制点，必须留一份「显式化清单」在提交说明里。
3. 新增 `AniTokens.vb`：集中定义 `HoverIn=100` / `HoverOut=150` / `Disabled=200` /
   `Focus=100` / `Enter=250` / `Exit=150` / `StaggerStep=25` / `DialogIn=300` /
   `DialogOut=150` 等（数值待 D2 一并确认），以及 `DefaultEase` / `LinearEase`。
   **P1a 不强制全量替换调用点**，只让新代码与后续 P1b 有单一真源。

### P1b 一致性（路径 A：dev.7 合入后的 C# 侧）

- 逐个控件把硬编码时长替换为 `AniTokens` 常量；同类交互收敛到两档以内。
- 拆分同组同属性并行：`MyButton` L155-156（80ms 与 700ms 同改 ScaleX）、
  `MyExtraButton` L117-118、`MyExtraTextButton` L97-98、`MyIconButton` L77-78；
  用 `After:=True` 串行而非并行。
- 修正组名复制残留：`MyListItem` L459 `"MyListItem Checked "` vs L607 `"ListItem Color "`；
  `MyHint` L146 误用 `"MyCard Dispose "`；`MySlider` / `MySliderDot` 共用 `"MySlider Progress "`
  （后者为死代码，随清理消失）。
- 弹窗遮罩淡出（`MyMsgInput` L64、`MyMsgSelect` L71、`MyMsgText` L62）与
  `"PanMsg Background"` 全局固定名改为带 Uuid 的稳定组名。

### P2 缺口补齐（路径 A：C# 侧，逐页回归）

1. **先补公共封装**：包装 `AaStack` 语义，提供 `ListEnter(Panel, ...)` / `ListExit(...)`，
   内置 stagger 步长、上限（如最多 12 项参与，超出直接整体淡入，避免长列表拖尾）、
   `AniControlEnabled` 守卫与「刷新中再次刷新则停旧组」保护。
   **不要逐页手写 `Delay +=`**（`MyPageLeft` L53、`MyPageRight` L415 已有三套不同步长）。
2. **首批只接两个高频场景**：Mod 列表（`PageInstanceMod.RefreshUI` L132-149）与
   资源搜索结果（`PageResource` L180-186）。验证手感与性能后再推广到
   `PageDownloadCompDetail` / `PageOtherHelp` / `PageSelectLeft` / `PageDownloadInstall`。
3. 启动首页：把 `FormMain` L381-385 的 `PageState = ContentStay` 改为正常走
   `PageOnEnter()` 的 `ContentEnter`，与「切到已有页面」一致；窗口整体 `"Form Show"`
   动画（L443-454）保持不变，避免启动动画叠加过长。
4. 入场补齐：`MyHint`（补与 `AniDispose` 对称的入场）、`MyLoading`（出现 / 消失 / State 切换）、
   `MyComboBox` 下拉 Popup、`MyCard` 折叠内容淡入。
5. 进度文字：让 `PageLaunchLeft` L731/750、`PageSpeedLeft` L41-53 的文本更新与
   已有 `AaGridLengthWidth` 同节奏（数值平滑或直接跟随动画帧，不引入第二套计时器）。
6. 焦点反馈：键盘 Tab / 方向键导航时有可见焦点态，并补 `LostKeyboardFocus`。
7. 主题色过渡（可选）：对 `ModSecret.ThemeRefresh` L86-105 的资源字典替换做一次
   短渐变；须先确认 `DynamicResource` 引用点数量与 `Freeze()` 画刷（L122/133）的约束，
   风险高于收益时按 §4.2 第 4 条拆出，不在 P2 强做。

### P3 引擎重构（另立专项，本计划只登记设计约束）

后续专项必须回答的四条，本计划不预先定实现：

1. 插值模型改为「记录 StartValue → 从 StartValue 插值到 Target」，消除终值漂移与中断残留；
   或评估直接迁到 WPF `Storyboard` 的可行性（`Snapshot` 语义、`AniStop` 映射、XAML 并存）。
2. 驱动改为 `CompositionTarget.Rendering`（或空闲挂起 + 按需唤醒），去掉后台线程空转与
   每帧 `Dispatcher.Invoke` 同步往返。
3. 「关闭动画」改为**直接设终值 + 执行必要回调**，而非 200× 快进；补一个真正的
   系统级减弱动态效果开关，并把 `Application.xaml` L420 的 Storyboard、
   `PopupAnimation="Fade"` 一并纳入降级。
4. 布局属性动画迁 `RenderTransform`；`AaColor` 改 HSL 插值；`AniGroups` 加线程同步
   （当前唯一锁只保护 `AniControlEnabled` 的 Setter，L37/46-48）；
   `AniFPS` 统计在非 Debug 下恒为 0、日志判据形同虚设（L823-834）一并修。

---

## 6. 回归影响面

### 6.1 可能破坏的旧行为

| 改动 | 风险 | 缓解 |
|---|---|---|
| 默认缓动 Linear → OutFluent | 所有省略 Ease 的调用点手感改变；匀速场景（进度、滚动）被误伤；依赖「动画在 t 时刻恰好到某值」的 `AaCode` 时序漂移 | §5 P1a 第 2 步显式化清单；逐场景冒烟；`AniSpeed` 调速下复测 |
| 滚动命名组 | 连续滚轮手感变「每次重置」而非叠加，滚动距离可能变短 | 保留 `DeltaMult`；对比改动前后 20 次滚轮的最终偏移 |
| 页面切换组名 / 时序 | 增加等待时长或误停导致空白页、`PageOnEnter` 多次触发、`PageStates` 抛异常（L204） | 快速连点 / 加载中切页 / 加载失败三类冒烟；`Logger.Trace("已触发 PageOnEnter")` 计数核对 |
| 补禁用态上色 | 与 `SetResourceReference` 无动画分支冲突，禁用色可能被覆盖 | 逐控件核对禁用 / 恢复往返 |
| 列表 stagger | 长列表拖尾、快速输入时旧组未停导致项闪烁或残留；`AniControlEnabled` 守卫失效导致初始化期播动画 | 封装内置上限与停旧组；搜索逐字输入冒烟 |
| 启动首页改走 ContentEnter | 窗口整体入场与内容入场叠加，启动变慢或首帧空白 | 与 L443-454 `"Form Show"` 联调；冷启动计时 |
| 进度文字同步 | 引入第二套计时器可能与加载器状态机冲突 | 只复用现有动画帧，不新增 Timer |
| 主题色过渡 | `Freeze()` 画刷不可动画；`DynamicResource` 全界面刷新 | 先做可行性验证，风险高则移出 P2 |

### 6.2 需新增 / 修改的测试

- **现有**：`PCL.Download.Tests`（568 例）、`MeloongCore.Tests` —— 本计划不触碰内核，
  二者作为「未误伤」守门；每阶段全绿。
- **UI 无自动化测试**，需在 P2 同期补一份可重复执行的 UI 冒烟脚本
  （对齐《计划-dev7-全量CSharp重写.md》§0「后补脚本替代人工逐类点 UI」的裁决）；
  在脚本就位前，按 §7.3 人工执行并留截图 / 日志。
- **建议新增的引擎单测**（P3 专项内，可与引擎语言无关地先在 C# 侧做）：
  终值精度（动画结束属性 == Target）、中断后状态、`AniStop` 同名互斥、
  多组同属性并发、空 `AniGroups` 时不派发、`AniSpeed` 极值下不跳帧丢回调。

---

## 7. 验证步骤

### 7.1 每阶段开工前基线（AGENTS §1.3）

```bash
cd D:/bing/MC/pcl_x/PCL_NEXT_Windows
git status                          # 确认并行会话改动，只暂存本阶段文件
dotnet build pcl-next/pcl-next.vbproj -c Release -r win-x64 --self-contained -p:PclChannel=Dev
dotnet test PCL.Download.Tests/PCL.Download.Tests.csproj -c Release
```

判定：构建 0 错误、测试全绿，方可动 `ModAnimation.vb`。纯文档阶段豁免。

### 7.2 每阶段构建命令（符合 §零，必须带真实 RID）

```bash
dotnet build pcl-next/pcl-next.vbproj -c Release -r win-x64 --self-contained -p:PclChannel=Dev
dotnet build pcl-next/pcl-next.vbproj -getProperty:OutputPath -p:Configuration=Release -p:RuntimeIdentifier=win-x64
```

- 禁止 `-p:OutputPath`、禁止 `-o bin-net10\` 短路径、禁止不带 `-r`（`PclRidGuard` 会报错）。
- 落点断言：`pcl-next\bin-net10\Dev\<版本名>-<构建号>\win-x64\`。
- dev.7 合入后按当时的工程文件名替换 `.vbproj` 为 `.csproj`，其余参数不变。
- **禁止手工改 `obj\pcl-build.small`**；构建号由 `scripts\build-number.base`（Windows 65）
  + 基线提交数推导（AGENTS §0.3.2、§五.6）。
- 产物只进 `bin-net10`（测试版）；未打 tag 不得进 `ReleaseArtifacts\`（§0.0）。

### 7.3 UI 冒烟清单（每阶段至少跑与本阶段相关的行）

| # | 场景 | 操作 | 判定 |
|---|---|---|---|
| 1 | 窗口启动 | 冷启动 3 次 | 首页有入场动画、无空白帧、无首屏闪烁 |
| 2 | 页面切换 | 左侧导航 5 个主页面往返 + 子页面切换各 3 次 | 无空白页、无残留旧页、进出场完整 |
| 3 | 快速连点 | 导航连续点 10 次（< 1s） | 不抛异常、最终停在最后点击的页、`PageOnEnter` 不重复 |
| 4 | 加载中切页 | 进入资源页后立即切走再切回 | 加载器状态机正常，无 `PageStates` 异常 |
| 5 | 连续滚轮 | 长列表滚轮 20 次 | 位置单调、无回跳、无叠加抖动 |
| 6 | 搜索逐字输入 | Mod 列表 / 资源搜索各输入 10 个字符 | 无整页闪烁、无旧项残留、stagger 可见 |
| 7 | 列表删除 / 启停 | Mod 列表删 3 项、启停 3 次 | 退场 / 入场完整，无错位 |
| 8 | 弹窗 | 三类 `MyMsg` + `MyHint` 各开关 3 次 | 遮罩与弹体同步淡入淡出，无遮罩残留 |
| 9 | 下拉框 | 展开 / 收起 / 选中各 5 次 | 无 Popup 瞬闪、宽度不跳 |
| 10 | 禁用态 | 设置页禁用 / 恢复控件 | 上色 / 复原正确，不被 `SetResourceReference` 覆盖 |
| 11 | 键盘导航 | Tab 遍历主窗口焦点 | 焦点可见、不走失 |
| 12 | 主题滑块 | 拖动 Hue / Light 滑块 | 颜色变化连续（P2 若接入过渡）或至少无异常 |
| 13 | 动画调速 | `SystemDebugAnim` 设 0 / 9 / 30 | 慢速不卡死、默认正常、关闭时无连锁回调异常 |
| 14 | 长时间空闲 | 静置 5 分钟 | 无 CPU 空转尖峰（P3 后应接近 0）、无内存增长 |

### 7.4 性能判定（P3 专项）

- 空闲 5 分钟：动画线程不应有每约 3ms 一次的 `Dispatcher.Invoke` 往返（用性能分析器或
  `AniFPS` 日志核对；注意 L823-834 的统计在非 Debug 下恒为 0，需先修统计再测）。
- 动画期间：属性更新与渲染帧对齐，无「一帧内多次写入 / 跨帧漏写」。
- 终值：动画结束后属性值严格等于目标值（现为浮点累加，存在漂移）。

### 7.5 提交与分支纪律

- 每阶段**独立一次提交**，便于 §8 逐阶段回滚；提交信息用 `fix:` / `refactor:` / `feat:` / `docs:` 前缀。
- **仅 `git add` 本阶段自己的文件，禁止 `git add -A`**；工作区并行会话的 dirty 文件
  （`AGENTS.md`、`docs/plans/*.md` 等）一律不暂存。
- 提交信息末尾带 `Build: <M.mm>`（由 `prepare-commit-msg` 钩子追加；未装钩子时
  手工跑 `scripts/Get-BuildSmallVersion.ps1` 补写），**禁止编造或复用旧值**。
- `origin` 严禁 push；提交后是否推 `fork`、何时推，**由用户决定**，执行者不擅自推送
  （本地 `dev.6-Windows` 可能领先远端并行线未发布提交）。
- 本计划文档自身如纳入版本库，用 `docs:` 提交，且只暂存本文件。

---

## 8. 回滚方案

| 阶段 | 回滚方式 | 保留内容 |
|---|---|---|
| P0 | 每处缺陷单独提交 → `git revert <sha>` 单处回退，互不影响 | 其余 P0 修复保留 |
| P1a | 默认缓动只是 `If(Ease, ...)` 一处兜底对象 + 一个新增文件；`git revert` 即可恢复纯 Linear 行为 | `AniTokens.vb` 可保留不删（未引用则无行为影响），显式化 `AniEaseLinear` 的调用点保留 |
| P1b / P2 | 按「公共封装」与「逐页接入」分批提交；异常时先 revert 页面接入，封装可留 | 冒烟清单、显式化清单、问题定位记录保留在提交说明 / 本计划 |
| P3 | 在隔离工作区（对齐 dev.7 的 `.vb2cs-trial` 做法）完成「编译通过 + 全绿」后再合入主线；不合入即零影响 | 专项计划、基准测试数据 |
| 全局 | 任一阶段出现 §6.1 无法在当轮收敛的回归，立即停止后续阶段，回到最近一次全绿提交 | 不删除 `ReleaseArtifacts\` 归档、不清理并行会话文件、不强制推送 |

回滚后必须重跑 §7.1 基线与 §7.3 中与回滚阶段相关的冒烟项，确认回到已知可发布状态。

---

## 9. 决策点索引

| 编号 | 内容 | 位置 | 状态 |
|---|---|---|---|
| D1 | 施工路径 A / B / C | §0.3 | **待用户裁决** |
| D2 | 默认缓动取值与时长 token 数值 | §0.4、§5 P1a | **待用户裁决** |
| D3 | P3 是否纳入本计划 | §0.4、§5 P3 | **待用户裁决**（推荐另立专项） |
| D4 | 主题色过渡是否留在 P2 | §5 P2 第 7 条 | P2 开工前按可行性复核决定 |
| D5 | UI 冒烟脚本由谁补、何时补 | §6.2 | 建议随 P2 / dev.7「后补脚本」裁决一并处理 |

> 本文件为专项计划，完工后随版本归档，不作为长期规则入口（AGENTS §四）。
> 任何与本计划冲突的旧口径，以《计划-dev7-全量CSharp重写.md》的用户裁决与
> 《AGENTS.md》为准；本计划不修改、不取代上述文件。
