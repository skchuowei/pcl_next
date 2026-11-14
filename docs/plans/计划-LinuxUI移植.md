# 计划：Linux 端 UI 移植（1:1 还原 Windows 界面）

> 依据：《AGENTS.md》§3.1 / §3.2（大改动先写专项计划）、§零（产物三段树与真实 RID）、
> §十（三仓库架构：互不合并、各自发版）；《计划-安卓UI移植.md》（同型移植的先行经验）；
> 《计划-Linux内核L1-平台能力层.md》（内核平台能力层边界）。
>
> 状态：**执行中（2026-11-08）**。仓库 `PCL_NEXT_Linux`，分支 `dev.6-Linux`。
> LU1（宿主骨架）已完成；LU2（主题 + 首批控件 + 第一页 1:1）已落地并可 `linux-x64` 编译。
> 本文件为专项计划，移植完成后随版本归档，不作为长期规则入口。

---

## 0. 决定记录

| # | 项 | 决定 | 依据 / 备注 |
|---|---|---|---|
| D1 | **GUI 归属** | 不做中立共享仓库，不从安卓仓库搬 `PCL.UI`；GUI **就写在 Linux 自己的仓库里**，是 Linux 专属 GUI，工程名沿用 `PCL.Desktop.Linux` | 用户 2026-11-08 裁决，此前的 (a)/(b)/(c) 三选项讨论作废 |
| D1.1 | 工程命名理由 | 保留 `PCL.Desktop.Linux`：与内核工程 `PCL.Download` / `PCL.Cli` 的 `PCL.<层>.<平台>` 命名一致；不叫 `PCL.UI`，避免与「中立共享 UI」概念混淆；平台后缀 `Linux` 表明它是本仓库专属实现，不做跨仓库复用承诺 | 本计划 |
| D2 | **还原目标** | **1:1 还原 Windows 端界面**：视觉、布局、控件外观、交互流程都对齐现有 WPF 界面，不做简化版、不重新设计 | 用户 2026-11-08 |
| D3 | **实现语言** | C# + Avalonia 的**行为与视觉等价重写**；VB.NET 在 Linux 无编译器，1:1 不等于拷代码，对照时必须逐项核对原 xaml 的结构/尺寸/样式/色值 | 工程事实（`pcl-next.vbproj` = `net10.0-windows` + WPF + WinForms） |
| D4 | **主题基准** | 色值直接复用 `pcl-next/Application.xaml`（Lite 主题），不另造配色 | 该文件在本仓库内即唯一基准 |
| D5 | **后端** | `PCL.Download`（`net10.0`、纯 BCL）为唯一数据源，UI 不重抄下载/安装业务 | 内核已可 `linux-x64` 编译并跑 `selftest offline` |
| D6 | **字族** | Windows 写死的 `PCL English / Microsoft YaHei UI` 在 Linux 换成可用字族并做 CJK 回退；属渲染层差异，**不得**因此降低布局还原度 | 用户明确要求登记 |
| D7 | **共享库** | 本轮不建「中立/跨平台共享 UI 库」；`PCL.Desktop.Linux` 内的组件只服务于本平台 | 与 D1 配套 |
| D8 | **主题基线** | **自绘优先，Fluent 只做兜底**：Windows 端 `OverridesDefaultStyle=True` + 自定义 `Template` 的控件类型，Linux 侧一律提供自绘 `ControlTheme`（显式 `Theme` 绑定、禁止引用 Fluent 资源键）；FluentTheme 保留但仅服务尚未移植的控件，可见差别须逐条登记。详见 §2.2 | 用户 2026-11-08 指示「开外壳前先解决系统性风险」；R13 已证明靠 Fluent 无法 1:1 |

---

## 1. 现状结论

### 1.1 起点：Linux 侧 GUI 原本完全不可编译

| 工程 | TFM / 平台属性 | Linux 可编译 |
|---|---|---|
| `pcl-next`（VB 主程序，122 `.vb` / 63 `.xaml`） | `net10.0-windows` + `UseWPF` + `UseWindowsForms` | ❌（VB.NET 无跨平台编译器） |
| `PCLCS` | `net10.0-windows` | ❌ |
| `MeloongCore/Wpf` | `net10.0-windows` + `UseWPF` | ❌ |
| `MeloongCore/Shared` | `net10.0` | ⚠️ 含 `RegistryUtils` / `MsDelta` 等 Windows 入口，需逐条筛 |
| `MeloongCore/Tests` | `net48` | ❌ |
| `PCL.Download` / `PCL.Cli` / `PCL.Download.Tests` | `net10.0` | ✅ |

### 1.2 已有资产

| 资产 | 位置 | 用法 |
|---|---|---|
| 主题色值（`ColorBrush1..8`、`Gray1..8`、`RedLight/Dark/Back`、`HalfWhite`、`SemiTransparent`、`Bg0/Bg1`） | `pcl-next/Application.xaml` L22–L64 | 已搬进 `PCL.Desktop.Linux/Theme/PclColors.axaml`，色值原样 |
| 控件尺寸/动画时序 | `pcl-next/Controls/*.xaml(.vb)`、`Application.xaml` 样式段 | 逐项抄写（首批已抄 MyCard / MyButton / MyTextBox / MyComboBox） |
| 图标与矢量资源 | `pcl-next/Resources/Custom.xaml`、`Resources/*.png` | 后续搬（`Geometry` 路径数据可直接复用） |
| 内核数据通路 | `PCL.Download`（`McVersionManifestClient` / `DownloadEngine` / `ResourceApi`） | LU3 接线 |

> 实测：此前本仓库全库 grep `Avalonia` / `PCL.UI` 仅命中 4 个 `.md` 文档，零代码、零工程。

### 1.3 Windows-only 能力清单（必须替换）

静态扫描 `pcl-next/**/*.vb` 命中 **102 处 / 17 个文件**：

| 类别 | 代表文件（命中数） | Linux 处置 | 归属 |
|---|---|---|---|
| 位图 / WebP 解码 | `Modules/Base/MyBitmap.vb`(26) | Skia `Bitmap` 解码 WebP，弃用 `Imazen.WebP` | **必须替代**（R3） |
| 音频 | `Modules/ModMusic.vb`(16) | **不做**（用户 2026-11-08 裁决）。核实依据：全库 grep `ModMusic` 仅命中自身声明 1 处、**零调用点**，UI 路径未使用，故不构成 1:1 缺口 | 不做（R8 关闭），不改 Windows 侧代码 |
| `My.` 兼容垫层 | `Modules/Base/MyCompat.vb`(10) | 随 VB 退役，不移植 | — |
| 拖放 / 无边框窗口 | `Modules/ThirdParty/DragHelper.vb`(10)、`Controls/MyResizer.vb`(3) | Avalonia `BeginMoveDrag` + 自绘标题栏/缩放 | **必须实现**（R2） |
| 文件监视 | `Modules/Minecraft/ModWatcher.vb`(8) | `FileSystemWatcher`（BCL） | **必须替代** |
| 注册表 / 系统路径 | `Modules/Base/ModBase.vb`(7)、`PageSetupLaunch.xaml.vb`(5) | XDG（`~/.config` / `~/.local/share`）配置 | **必须替代**（R7） |
| 字体 | `Application.xaml`：`Resources/#PCL English, Microsoft YaHei UI` | 系统字族 + CJK 回退（本轮 R1），LU4 接内嵌 PCL English | **登记差异**（R1） |
| 系统集成 | 托盘 `NotifyIcon`、WinForms 对话框、`Process.Start` | `xdg-open` / Avalonia 对话框 / `Process` | 部分延后（R7） |
| 框架 | WPF 全套 | Avalonia 等价物 | — |

**判定口径**：平台差异只允许集中在**一处**平台服务层（§6.4），不得散落在页面代码里。

---

## 2. 1:1 还原口径（达标判定）

一页算「1:1 达标」，必须**同时**满足下列 7 类核对，缺一类不算完成：

| 代号 | 类别 | 具体要求 | 判定方式 |
|---|---|---|---|
| **L** | 布局与尺寸 | 控件层级、`Grid` 行列定义（含 `SharedSizeGroup`）、`Margin/Padding/MinWidth/MaxHeight/Width/Height`、`HorizontalAlignment/VerticalAlignment` 与 Windows xaml 数值**逐项相等**；不得用「看起来差不多」代替 | 原 xaml 与移植 xaml 并排逐行比对，数值列成清单 |
| **C** | 颜色与主题 | 所有颜色来自原 xaml 的 `StaticResource` / `DynamicResource` 键名，引用同名资源；不自造色、不用近似色 | 原 xaml 出现的每个键在 `Theme/PclColors.axaml` 中存在且色值一致 |
| **F** | 字体与字号 | 字号、字重、行高、`TextTrimming` 照抄；字族按 R1 替换 | 逐处核对 `FontSize` / `FontWeight` |
| **S** | 控件形态 | 边框粗细、圆角、阴影、图标 `Geometry` 路径数据**原样搬运** | 图标路径字符串逐字节相同 |
| **T** | 状态与过渡 | hover/press/disabled/选中/加载态的色值与**时序**（如 MyButton 变色 100ms 进 / 200ms 出、按压缩放 0.955）照抄 | 时序数值逐个对齐，曲线差异进 R4 |
| **I** | 交互路径 | 点击、导航、折叠、弹窗、滚动等行为与 Windows 端一致（Windows 专有系统集成除外，走 R7） | 手动走查（需 Linux 显示环境） |
| **E** | 异常与空态 | 文案、占位提示、禁用条件、空列表提示照抄；不含 Windows 专属对话框（走 R7） | 文案逐字比对 |

**交付说明模板**（每页必须给出）：
`对照来源: <Windows xaml 路径> (+ <同名 .xaml.vb> 行为段)` / `L/C/F/S/T 取值清单` / `当前差异: R…` / `未验证项: 需 Linux 显示环境执行的 I/E 走查`。

### 2.1 允许差异登记表（每条出现在页面交付说明里）

| 登记号 | 差异 | 允许范围 | 不能因它降低什么 |
|---|---|---|---|
| **R1** | 字族：内嵌 `PCL English` 尚未接入，先系统无衬线 + CJK 回退 | 字形宽度、抗锯齿、个别字形 | 不得改字号、行高、`Margin`、`MinWidth` 等布局数值 |
| **R2** | 窗口壳：WPF 自绘标题栏 / 无边框 / `MyResizer` 缩放在 Avalonia 下用另一套实现 | 装饰绘制细节、缩放热区 | 标题栏高度、按钮尺寸、图标位置必须与 Windows 一致 |
| **R3** | WebP 解码：`System.Drawing` + `Imazen.WebP` → Skia | 色彩管理、边缘插值 | 图像尺寸、卡片内图片的显示区域不变 |
| **R4** | 动画曲线：WPF 自定义 Ani 引擎 → Avalonia `Transitions` / `Easing` | 曲线形状（近似代替） | 时长数值照抄（如 100/200/300/700ms） |
| **R5** | `MyCard` 折叠**已接入**（2026-11-11）：`CanSwap`/`IsSwapped` → 折叠高 40、内容隐藏、右上 10×6 箭头（展开 180° / 折叠 0°，250ms）、悬停 `ColorBrush2`、点击标题区切换（`MyCard.vb` L284–L316）；**高度过渡动画仍未接入**（WPF 150ms / 分段匀速，现为瞬时） | 折叠过程的高度过渡 | 折叠前后两种静态布局、折叠高 40、箭头位置 `Margin 0,17,16,0` 必须一致 |
| **R6** | `MyComboBox` / `MyTextBox` 模板细节（弹出层、滚动条、IME、选中样式）尚未逐像素核对 | 弹出层绘制 | 收起态边框、圆角、内边距、字号必须一致 |
| **R7** | Windows 系统集成（注册表、托盘、跳转列表、通知、资源管理器定位） | 全部（换 XDG / DBus / 无等价物时不实现） | 页面内的文案与按钮布局不变；不做的集成须在页面里显式说明 |
| **R8** | 音频：**不做**（用户裁决，登记项关闭） | 不适用 | 无需保留任何音频 UI：Windows 端 `ModMusic.vb` 无调用点（全库仅 1 处命中 = 自身声明），界面上不存在播放条/音量控件，不做不产生布局差异 |
| **R9** | DPI / 多显示器缩放模型差异 | 缩放行为 | 100% 缩放下的尺寸必须一致 |
| **R10** | 动画引擎：WPF 自研 `Ani` 逐帧驱动 → Avalonia `Transitions` / `Animation` 近似 | 缓动曲线、逐帧节奏 | 时长数值照抄（勾选 150ms、悬停 100ms 进 / 200ms 出、按压 100ms）；`MyLoading` 镐子摆动改为 ±20°、1.4s 无限往返 |
| **R11** | `MyHint` 关闭：Linux 暂无 `Settings`，关闭只隐藏；未做 200ms 缩放/淡出 + 150ms 收高，也未做「不再提示」记忆 | 关闭动画与持久化 | 关闭前后两种静态布局不变；`CanClose` 按钮尺寸 20×20 与 ToolTip 文案保留 |
| **R12** | `MyIconButton` 主题色映射：WPF 源码散落在动画分支中，本轮取 `Color→#1370f3` / `White→白` / `Black→#343d4a` / `Red→#ff4c4c` | 图标颜色 | 图标尺寸（`Margin 5`、`Stretch Uniform`）、圆角 1000、按下 0.8 缩放不变 |
| **R13** | 运行期缺陷：`MyComboBox` 在 WSLg 下**未渲染**（收起态无边框、无选中文案、无箭头），编译期无报错。**根因已查明**：`MyComboBox : ComboBox` 未覆写 `StyleKeyOverride`，Avalonia Fluent 的 ControlTheme 按派生类型查不到 → 无 `Template` → 控件不渲染。**已修（2026-11-08）**，分两步：① 补 `StyleKeyOverride => typeof(ComboBox)` 让控件先可见；② 按本计划 §2.2 主题基线，改为**自绘 `ControlTheme`**（照抄 `pcl-next/Application.xaml` L336–L469 的 ComboBox / ToggleButton / ComboBoxItem 三个模板），不再依赖 Fluent 默认绘制。~~「Windows 端 `MyComboBox.vb` 同样没有自定义模板（纯继承 + 属性/动画改造）」~~ **此论断错误（2026-11-08 复核更正）**：Windows 端模板不在 `Controls/MyComboBox.xaml`（该文件确实不存在），而是写在 `pcl-next/Application.xaml` L336–L405 的 `Style TargetType="local:MyComboBox"`（`OverridesDefaultStyle=True` + 普通态/`IsEditable` 两套 `ControlTemplate`）；`MyComboBox.vb` 只负责 `RefreshColor` 配色与弹出层宽度/透明度。 | 不视为允许差异（属缺陷，已修复） | 收起态可见性与弹出层外观仍需统一验收目视确认（V1；`HasItems=false → 高 26` 等边角触发器暂未复刻，并入 R6） |
| **R14** | ~~窗口/页面底色为纯白~~ **已修（2026-11-08）**：宿主窗口底色改为 `ColorBrush1` `#343d4a`，与 Windows 端窗口底色一致，`MyCard` 的 `#F5FFFFFF` 卡底与 `0 0 6 #12343d4a` 阴影因此可见 | **照抄**，不是允许差异：色值取自 Windows 端 `ColorBrush1`，未做任何近似。实现方式上的差别仅在于 Windows `Window` 本身是 `AllowsTransparency=True` + `Background={x:Null}`（底色由内容层提供），Linux 直接铺在 `Window` 根层——视觉结果一致 | 卡片圆角 5、`Margin -3,-4,-3,-3`、标题 15,12 等尺寸不得改 |
| **R15** | `ScrollViewer` 变体与横向滚动条：~~本轮只落 `MyScrollViewer`（滚动条贴边无 `Margin`）~~ **2026-11-11 补**：`ScrollViewerFullMargin` 变体已按 `Application.xaml` L199–L217 补自绘主题（`Classes="fullmargin"` → 竖向滚动条 `Margin 2,1,1,1`）；仍缺 `ScrollViewerNoMargin`（`TextBox` 模板内部使用，本轮页面未直接使用）；横向滚动条未自绘（Windows 端 `HorizontalScrollBarVisibility` 全部为 `Disabled`，界面不应出现横向条） | 未用到的变体缺省不影响当前页面；一旦某页用到对应变体，必须在开工该页前补齐，不得带着差异上线 | 已用页面（`PageSetupLink` / `PageSelectLeft`）的滚动条宽度 8、拇指配色、FullMargin 的 2,1,1,1 边距不受影响 |
| **R16** | 调色板**资源**（`PclColors.axaml` 的 `ColorBrush1..8`）仍取 `Application.xaml` 的静态色值（`#343d4a` / `#0b5bcb` …），而标题栏/页面背景已改为运行期 HSL 重算（R17）：两者逐通道差 ≤2（实测 `Color1`：静态 `(52,61,74)` vs 重算 `(51,62,72)`，`Color8`：`(234,242,254)` vs `(238,246,254)`） | 仅允许 ≤2/255 的单通道偏差，肉眼不可分辨 | 不得因此改任何**布局**数值；因本 fork 皮肤不改变主题参数（§13.8），静态资源与运行期重算不存在分叉风险；若后续把 `ColorBrush*` 也改为运行期写入，则 R16 关闭 |
| **R17** | 标题栏渐变与主页面背景**已在 Linux 侧按同一 HSL 算法、同一取整算**（`Theme/PclTheme.cs`：`FromHSL2` / `FromHSL` / `Hue` 逐行移植自 `ModBase.vb` L360–L409），主题参数已由 `const` 改为**皮肤参数字段 + `Refresh(theme)` 早退语义**（同 `ModSecret.vb` L69–L110），默认值 `ColorHue=210`、`ColorSat=85`、`ColorLightAdjust=0`、`ColorHueTopbarDelta=0`（`ModSecret.vb` L66）。**核实结论（§13.8）：Windows 端本 fork 不存在「皮肤编号 → 参数」的映射表，四个参数全程无赋值点，故参数恒为默认值、配色恒为 PCL 蓝**；`UiBackgroundColorful` 开关与设置页联动待设置页接入 | 参数恒定本身与 Windows 端一致，不算降级 | 算法与取整不得改成近似值；不得自行发明映射表；取得映射数据后在 `PclTheme.Refresh` 单点接入 |
| **R18** | 标题栏 logo 只接入 `UiLogoType=1`（默认档：文字 `PCL Next`、17 号白字、`Margin 18,0,0,0`）；其余三档（`0` 形状 / `2` 自定义文本 / `3` 图片）与 `UiLogoLeft`、`UiLogoText`、`ImgTitle` 背景图未接入 | 未接入档不影响默认外观 | 默认档的位置/字号/色值不得改；接入时按 vb L820–L869 的分支补齐 |
| **R19** | 窗口壳的行为近似：① 开场/关闭动画缓动由 `AniEaseOutBack` 改 `BackEaseOut`（时长 600/500ms 照抄）；② `IsMouseDirectlyOver` 无等价物，标题栏拖拽改为「向上找 `MyIconButton`/`MyRadioButton` 祖先」；③ Avalonia 11 无 `CanMinimize` 属性，最小化走 `WindowState.Minimized`；④ `Topmost` 启动瞬间、`UiOpacity` 窗口不透明度、窗口尺寸/位置记忆（`Settings`）未接入 | 只允许实现手段差异 | 标题栏高度 48、按钮 28×28 与边距 12/44、缩放热区 8/13px 不得改 |
| **R20** | **【临时状态，2026-11-14 第四次收窄】**页面栈与外壳其余部件：标题栏五个栈（启动 0 / 下载 1 / 联机 2 / 设置 3 + 四个子页 / 更多 4 + 三个子页）、版本选择次级栈（5）与实例设置次级栈（6，概览 / 设置 / 修改 / Mod / ModDisabled / 导出 六项全部有页面）均已移植；**初始选中已恢复 Windows 默认「启动」**（vb L1438–L1471、`FormMain.xaml` L121 `BtnTitleSelect0 Checked=True`）；剩余缺口为外壳部件；`ImgBack` 背景图（`UiBackgroundOpacity` / `Blur` / `Suit`）、`PanTitleInner`（次级页面标题栏）、`PanHint`、`BtnExtra*` 未接入；设置 / 下载 / 更多 / 实例左导航的刷新按钮点击为空操作 | **临时状态不算允许差异**：页面补齐后必须继续回收，不得以「Linux 少几个页面」沉淀成既定行为 | 已回收项（初始选中、启动栈、版本选择栈、下载栈、设置栈、更多栈、实例栈六页）不得回退；「联机」页与外壳几何不得因此改动；每移植完一个栈就必须回收对应降级项并在本节留痕 |
| **R21** | 左竖条（`RectLeftBackground`）宽度动画的触发时机：Windows 由 `PanMainLeft.SizeChanged` 驱动；Linux 改为「切换栈并完成布局后取 `PanMainLeft.Bounds.Width` 驱动一次」。因左列为 `Auto`、内容固定，窗口缩放不改变该宽度，实际表现一致 | 仅触发时机 | 目标宽度值、180ms 时长、阴影淡出条件不得改；若后续左页宽度随内容变化，必须补回尺寸监听 |
| **R22** | 右页宿主 `MyPageRight` 尚未移植：Windows 端右页继承 `MyPageRight`，由 `PanScroll` 提供滚动条横向滑入（350ms / 90ms）、内容逐个进入（0→1 淡入 100ms、-16→0 位移 250+350ms）与退出动画；Linux 右页为普通 `UserControl` + 原生 `ScrollViewer`，无这些动画 | 仅动画；不改变任何静态布局 | 右页内容的 `Margin`、卡片间距、字号、滚动条宽度不得改；移植 `MyPageRight` 时逐项补齐并在 §13 留痕 |
| **R23** | `MyTextBox` 属性差异：WPF 自定义属性 `HasBackground="False"`（启动日志）在 Avalonia 无等价物，改 `Background=Transparent`；WPF `TextBox.VerticalScrollBarVisibility/HorizontalScrollBarVisibility=Disabled` 在 Avalonia 是 `ScrollViewer` 附加属性，本轮未设（只读日志框内容为空，不出现滚动条） | 仅属性表达方式 | 内边距 `Padding 0,4`、`Margin 15,34,15,13`、`BorderThickness 0`、字号 13 不得改 |
| **R24** | 启动页行为边界：Linux 侧无实例列表 / 启动服务，`PageLaunchLeft` 固定呈现 Windows「加载完成但无可用版本」的 Case 2（`PageLaunchLeft.xaml.vb` L671–L677）——按钮「下载游戏」可点、版本按钮可用、`LabVersion`「未找到可用的游戏版本」；`BtnVersion`/`BtnLaunch` 只做栈跳转，`BtnMore`/`BtnCancel` 空操作；启动中整页 `PanLaunching` 结构已还原但无流程驱动 | 仅运行期状态机；静态布局必须完整 | 行/列定义、`BtnLaunch` 高 54、`BtnVersion` 高 35、`LabVersion` 11 号、`PanLaunching` 全部尺寸不得简化；接入启动服务后必须按 `RefreshButtonsUI` 的 0–3 状态分支补齐 |
| **R25** | 启动页 WPF-only 视觉未复刻：`DropShadowBitmapEffect`（`PanLaunching` 标题投影，L85–L87）、`CombinedGeometry` 提示气泡上沿挖孔裁剪（L137–L147）、`LabVersion` 对 `BtnLaunch` 的 `RealRenderTransform` 缩放绑定（L66–L70）、`MyButton.TextPadding`（`BtnMore`，L28）在 Avalonia 无等价物或当前无对应属性 | 仅装饰与绑定 | 提示气泡 `Width 260`、`Margin 0,16,0,2`、标题字号 20/13.5、进度条高 4 与 69.28*/30.72* 比例不得改 |
| **R26** | 版本选择页行为边界：Linux 侧无实例列表服务，`PageSelectLeft.PanList` 保持空、`PageSelectRight` 固定呈现空态卡片（`PageSelectRight.xaml.vb` L98–L109：`LabEmptyTitle`「无可用版本」、说明两行、下载按钮）；列表条目、「添加文件夹」注入项、加载卡片状态机未接入 | 仅数据与状态机；空态布局必须完整 | 左列 `MinWidth 250 / MaxWidth 300`、空态卡片 `Margin 40`、标题 19 号、分隔线高 2、按钮 `MinWidth 140 / Padding 13,0` 不得改 |
| **R27** | `MySlider` 数值提示：WPF 的 `Popup`（`GetHintText` 动态文案、拖动/键盘时弹出、700ms 后收起）未移植；拖动时的 1.3 滑块缩放与 40ms 颜色切换并入 R10 近似 | 仅提示气泡与动画 | 轨道 1px/2px、滑块 10×10、宽度算法（两端 0.5 补偿）与配色分支不得改；接入设置页后再按 `RefreshPopup` 补 |
| **R28** | `MyExtraTextButton` 近似：WPF 圆角绑定 `ActualHeight×0.4`（52 → 20.8）改为固定 21；`Show` 进出场（0→1 淡入 80ms + 0.15→0.85 缩放）未移植；按下 800ms→0.85、60ms→0.80 的分段压缩为 100ms→0.8，回弹 300ms `BackEaseOut` 保留 | 仅动画与绑定表达式 | 高 52、内层下移 10、文案 16 号 `ColorBrush8`、图标 `Margin 2,12,0,12`、悬停 `ColorBrush4` / 禁用 `ColorBrushGray4` 不得改 |
| **R29** | 设置·启动页静态态：Linux 侧尚无设置服务，`SettingService.Key/Value` 绑定、`ValidateExcept` / `ValidateInteger` 校验、运行期联动（窗口尺寸框显隐、皮肤类型分支 `UpdateSkinType`、内存条比例 `PanRamDisplay`、Java 列表刷新）均未接入；页面按 `Settings.vb` 默认值呈现（`LaunchArgumentIndieV2=4`、`Visible=5`、`Priority=1`、`WindowType=1`、`RamType=0`、`RamCustom=15`、`AdvanceGC=4`、`GraphicCard=True`、JVM 默认串） | 仅数据绑定与联动 | 五张卡片的行列高（28/9）、`Margin 25,40,25,25`、`MinWidth 140 / Padding 13,0`、JVM 框高 100、`Panel` 内控件顺序与文案不得改 |
| **R30** | 设置·个性化页主题解锁交互未接入：`LabLauncherTheme5Unlock` 连点解锁玄素黑（`PageSetupUI.xaml.vb` L354–L366）、`LabLauncherTheme8/9/11` 的右键跳转（赞助 / 反馈 / 解密游戏，L367–L385）、`RadioLauncherTheme14` 选中后显示色相/饱和度/亮度/色调渐变四条滑条（L388–L411）均未实现；解锁提示层（`Background=ColorBrushSemiTransparent` 的四个 TextBlock）与解锁 ToolTip 按原布局保留，滑条按原 XAML 保持隐藏 | 仅解锁状态机与右键菜单 | 主题 15 项的 6 列 × 3 行排布、`PanLauncherTheme Height 90`、各 `IsEnabled`（仅 0–4 可用）与 `Opacity`（玄素黑 0/1 双层）不得改；滑条 `MaxValue 360/180/100/40`、`ValueByKey 10/5/5/5` 不得改 |
| **R31** | 设置·个性化页文件操作未接入：背景图片（打开文件夹 / 刷新 / 清空，`PageSetupUI.xaml.vb` L75–L154）、背景音乐（打开文件夹 / 刷新 / 清空，L226–L273）、标题栏图片（更改 / 清空，L157–L223）与「生成教学文件」「打开主页文件夹」均只保留按钮形态与 ToolTip；三组区域在无文件时保持原 XAML 的隐藏态（`PanBackground*`、`BtnBackgroundClear`、`PanMusicVolume/Detail`、`BtnMusicClear`），卡片标题不加「（N 张）/（N 首）」后缀 | 仅文件系统动作与数量后缀 | 隐藏态的卡片间距、`StackPanel Margin 25,40,25,20 / 25,42,25,20`、按钮 `MinWidth 140 / Padding 13,0 / 间距 20` 不得改；接入文件服务后按 `BackgroundRefreshUI` / `MusicRefreshUI` 补齐 |
| **R32** | 设置·个性化页「功能隐藏」联动未接入：父子项联动（`HiddenSetupMain` / `HiddenSetupSub` / `HiddenOtherMain` / `HiddenOtherSub` / `HiddenOtherNet`，L513–L587）、警告提示（`HiddenHint`）、F12 强制显示（`HiddenForceShow`）与 `HiddenRefresh` 对标题栏 Tab / 设置左导航 / 更多子页的显隐影响均未实现；复选框仅为静态形态，`UiHidden*` 默认全部不勾，「联机」「设置 子页面-联机」两项按原 XAML 保持 `IsEnabled=False` | 仅联动规则 | `CardSwitch` 的 7 列（Auto/18/0.8*/0.9*/0.8*/0.8*/1.0*）与 6 行（4/30/30/30/30/Auto）、复选框高 22、文案与 ToolTip 逐字不得改 |
| **R33** | 设置·其他页静态态：Linux 侧尚无设置服务，`SettingService.Key` 绑定与 `Reload` / `Reset` 未接入，页面按 `Settings.vb` 默认值呈现（`ToolDownloadSource/Version=1`、`ToolDownloadThread=63`、`ToolDownloadSpeed=42`、`ToolDownloadMod=2`、`ToolDownloadTranslateV2=1`、`ToolModLocalNameStyle=0`、`ToolUpdateRelease/Snapshot=False`、`ToolHelpChinese=True`、`SystemSystemUpdate/Activity=0`、`SystemSystemCache=""`、`SystemDebugAnim=9`）；`ChannelIsOfficial=False`（Dev）→ `PanDonate` 可见、`ItemSystemUpdateDownload` 文案为带土豆码后缀的版本 | 仅数据绑定 | 四张卡片的行列高（28/7/27、28/7、22/8/22、28/9/5）、`Margin 25,37,25,15 / 25,37,25,12 / 25,40,25,17 / 25,40,25,20`、按钮 `MinWidth 140 / Padding 13,0 / 间距 20`、调试卡 `IsSwapped=True` 不得改 |
| **R34** | 设置·其他页交互未接入：「检查更新」`UpdateCheckByButton`、「导出设置 / 导入设置」（原为「该功能尚在开发中」提示）、「复制识别码」`PageOtherAbout.CopyIdentify`、「输入土豆码」`InputPotatoCode` 均为空操作；`SliderDownloadThread` ≥100 的警告弹窗、`ComboSystemActivity` 选末项与 `ComboSystemUpdate` 选末项的确认回滚、`CheckDebugMode` 切换提示均未实现 | 仅交互动作 | 控件顺序、`ComboSystemUpdate` 四项文案、公告三项 ToolTip、`PanDonate` 双按钮布局不得改 |
| **R35** | 设置·其他页输入框校验未接入：`TextSystemCache` 的 `ValidateRules`（`ValidateNullable` + `ValidateFolderPath UseMinecraftCharCheck=False`）在 Linux 无对应校验管线，仅保留 `HintText="默认"` 与 ToolTip；输入内容不落盘 | 仅校验与持久化 | `Grid.Row=4 / ColumnSpan=2 / Column=1`、`HintText`、ToolTip 三行文案不得改 |
| **R36** | 下载栈左导航刷新按钮未接入：`PageDownloadLeft.xaml.vb` L95–L152 `Refresh_Click`（清空资源缓存、重启 8 个加载器、回到对应子页）未实现，按钮只保留形态与 ToolTip；`RestrictMcDownload` 为 False（`ModDownloadPolicy` 默认允许 MC 下载）→ 「原版游戏」与「社区资源」小标题保持可见 | 仅刷新动作 | 条目高 36、`MinPaddingRight 35`、Logo 缩放（0.8/0.97/0.98/0.96/0.81/1.04）、「社区资源」字号 12 / `Margin 13,20,5,4` 不得改 |
| **R37** | 下载栈「原版游戏」页静态态：Linux 侧尚无下载服务，`PageLoaderInit` 的加载态固定呈现（PanLoad 可见、PanAllBack 隐藏，`MyPageRight.vb` L87–L89）；版本列表 `PanMinecraft` 的卡片由 `LoadMinecraft_OnFinish` 运行期生成，静态为空；`PanSelect` 安装配置子页（名称卡 + 5 条提示 + 8 张 40 高折叠卡）按原 XAML 保留但默认隐藏；「开始下载」`BtnStart` 仅在选择页显示，静态隐藏 | 仅数据驱动部分 | `PanInner Margin 25,10,25,25`、`PanSelect Margin 0,0,0,45`、八张卡 `Height 40 / Margin 0,12,0,0`、`PanLoad Margin 40,0` 不得改 |
| **R38** | 下载栈图片资源缺失：`MyImage` 的 `pack://application:,,,/images/Blocks/*.png`（Anvil / NeoForge / Fabric / GrassPath / OptiFabric / Egg、Minecraft 32×32 与 18×18）在 Linux 项目内不存在，以同尺寸透明占位保留布局；`MyCard.SwapLogoRight`（折叠箭头旋转基准 270°）与 `MyLoading.HasAnimation` 在 Linux 控件无对应属性 | 仅位图与装饰 | 占位尺寸（18 / 20 / 32）与 `Margin 132,11,15,0`、标题卡 `Height 40` 不得改 |
| **R39** | 下载栈输入框与卡片动画差异：`TextSelectName` 的 `HasBackground="False"` / `ShowValidateResult="False"` / `ValidateFolderName` 校验未接入（保留 `Padding -1,0 / MinHeight 30 / MaxLength 70`）；`MyCard.UseAnimation`、`MyPanel` 选中页切换动画（PanSelect 40→0 位移 + 淡入 160ms）未移植 | 仅校验与动画 | 名称卡 `MinHeight 62`、文本框 `Margin 93,16,16,0`、返回按钮 26×26 `Margin 13,0,0,0` 不得改 |
| **R40** | 资源搜索页静态态：Linux 侧无资源搜索服务，`PanProjects` 为空、分页固定第 1 页且首/前按钮禁用；搜索 / 重置条件 / 安装已有整合包 / 翻页均为空操作；`HintSource`（CurseForge 未启用）与 `HintError`（Modrinth 连接失败）按「加载成功」态保持隐藏；五个包装页（Mod / 整合包 / 数据包 / 资源包 / 光影包）共用 `PageResourceView`，仅种类与下拉项不同 | 仅数据与动作 | 搜索卡 `Margin 25,40,25,15`、条件 Grid 6 列（Auto/1.3*/1.2*/50/Auto/1*）与 3 行（28/9/28）、按钮 `MinWidth 140 / Padding 13,0 / 间距 20`、分页卡 `Height 23 / Margin 10,7` 不得改 |
| **R41** | 资源搜索页版本下拉差异：WPF `IsEditable="True"`（可自行输入版本号）在 Avalonia ComboBox 无等价属性，退化为普通下拉；`IsTextSearchEnabled="False"` 与 `ComboSearchLoader` 的「仅 Mod + 具体版本输入时显示」联动未实现（静态保持隐藏，与原默认态一致） | 仅输入能力 | 版本下拉 13 项文案与顺序、`ComboSearchLoader Margin 10,0,0,0` 不得改 |
| **R42** | 资源条目与详情页未移植：列表项 `MyResourceItem.xaml`（64 高、5 列 7 行、Logo 50×50 圆角 6、标题/下载量/时间/来源四栏）与详情页 `PageDownloadCompDetail.xaml`（介绍卡 + 筛选胶囊 + 结果列表 + 加载卡）未建 Linux 视图；搜索服务接入时一并补齐 | 仅缺失子视图 | 搜索页布局不得因此简化；补齐时按原 XAML 逐项对照并在 §13 留痕 |
| **R43** | 帮助页静态态：`HelpLoader` / `HelpEntry` / 内置帮助 JSON+XAML 解压（`ModMain.vb` L426–L620）与帮助详情页 `PageOtherHelpDetail` 未移植；`PanList` 为空、搜索框只控制清空按钮不过滤分类卡片、刷新按钮空操作（`PageOtherLeft.xaml.vb` L121–L125） | 仅数据与搜索动作 | 搜索框 `Height 40`（TextBox `Padding 32,0,40,0` / `MaxLength 50` / 13.5 号）、放大镜 14×14 `Margin 14,0,0,0`、清空按钮 24×24 `Margin 0,0,10,0`、`PanMain Margin 25,25,25,10`、加载卡 `Margin 20,20,20,17` 不得改 |
| **R44** | 关于页头像位图缺失：`/pcl-next;component/Images/Heads/*`（Logo / PCL2 / LTCat / HerobrineXia / bangbang93 / wiki / z0z0r4 / EasyTier / ChongQing / 00ll00 / Patrick / Hao_Tian / MCBBS 共 13 张）在 Linux 项目内不存在，以 34×34 透明圆形占位保留布局 | 仅位图 | 头像列宽 40、行高 45、`Margin 3` 与 `EllipseGeometry Radius 17` 的占位尺寸不得改；接入图片资源后按原名还原 |
| **R45** | 关于页动作未接入：项目仓库 / 检查更新 / 赞助上游 PCL / 赞助镜像源 / 打开百科 / MCIM 主页 / GitHub 页面 / 打开镜像站 / 查看来源网站 / 查看许可文档 / 生成土豆码 / 复制识别码 / 输入土豆码 均只保留按钮形态（系统集成按 R7）；Dev 渠道 `ChannelIsInternal=True` 时 Windows 会隐藏「赞助作者」并显示「生成土豆码」（`PageOtherAbout.xaml.vb` L18–L23），Linux 侧按 Dev 渠道静态呈现该分支 | 仅动作与渠道分支 | 按钮 `Width/MinWidth 140/170`、`Height 35`、`Padding 13,0`、间距 20 与卡片顺序不得改 |
| **R46** | 更多栈左导航行为边界：反馈 / 新功能投票在 Windows 端由 `Changed` 事件弹 MessageBox 并把事件标记为已处理（`PageOtherLeft.xaml.vb` L128–L157），条目不会保持选中；Linux 无 MessageBox 等价物，`Click` 直接 `Handled=True` 阻止选中；`UiHiddenOther*` 五项隐藏联动（`PageSetupUI.HiddenRefresh` L476–L488）因 Settings 服务未移植而全部按默认可见呈现 | 仅弹窗与显隐联动 | 五项高 36、`MinPaddingRight 35`、Logo 缩放（0.97/0.98/0.97/0.9/0.92）、帮助项刷新按钮 25×25 不得改 |
| **R47** | 实例服务缺失：`McInstance` / `McInstanceListLoader` / `McInstanceLock` 未移植，实例设置栈（`PageType.InstanceSetup`）无数据来源与入口。左导航按 `Instance Is Nothing` 呈现 ModDisabled 分支；概览页头卡（`Instance.ToListItem()`）为空；版本设置页全部输入框 / 下拉 / 单选 / 滑动条为静态默认；Mod 管理固定「尚未安装 Mod」空态；初始化 / 刷新 / 全部按钮动作为空操作 | 仅数据、入口与动作 | 所有页面布局、尺寸、文案、卡片顺序不得改；接入实例服务后逐项还原并在 §13 留痕 |
| **R48** | 版本设置页绑定：`local:SettingService.Key/Value` 双向绑定、`ValidateRules`（`ValidateExcept` / `ValidateLength` / `ValidateHttp`）与版本独立 `PCL\Setup.ini` 读写未接入；输入框仅保留 `HintText` / `MaxLength` / `ToolTip`，`ComboArgumentIndieV2` / `ComboArgumentJava` 无实例数据时保持未选中 | 仅绑定与校验 | 行高 28/9、`Margin 25,40,25,21`、内存条三段列宽 4.7/2.5/0.7、四个 `MyCheckBox Height 28` 不得改 |
| **R49** | Mod 管理多选浮卡未建：`CardSelect`（底部居中、`TranslateTransform Y=-10`、`LabSelect` 与 5 个 `MyIconTextButton`：更新 / 启用 / 禁用 / 删除 / 取消选择）在 Linux 侧整体缺失。**2026-11-14 更新**：`MyIconTextButton` 控件已按原 XAML / vb 移植（§13.18），仅剩 `CardSelect` 本身未建 | 仅多选模式（默认关闭） | 搜索框、管理按钮卡（四个 `MinWidth 110`）、筛选卡与空态卡的几何不得改；接入多选时按原 XAML 补 `CardSelect` |
| **R50** | ~~实例系列剩余页面：`PageInstanceExport`（314 行）与「修改」页 `PageDownloadModify`（360 行）尚未移植，左导航对应项切换后右页为空；实例设置栈入口（版本选择页「版本设置」按钮）随实例列表服务（R26/R47）一并待接~~ **已关闭（2026-11-14）**：导出页已按 314 行全文移植（§13.18）；「修改」页核实为 `PageDownloadModify : PageDownloadInstall`，界面继承安装页，Linux 复用 `PageDownloadInstallView(isModify: true)`（R52）。实例设置栈入口仍随实例列表服务（R26/R47）待接 | 仅入口 | 已移植页面不得回退；入口接入后按 Windows 流程从版本选择页「版本设置」进入 |
| **R51** | 导出页规则与动作未接入：`ExportOption` 的 `Rules` / `ShowRules` / `RequireOptiFine` / `RequireModLoader` / `RequireModLoaderOrOptiFine` 文件匹配规则、`ExportConfig` 读写、`.mrpack` 格式选择、右上「重置」按钮（`IsHitTestVisible=False Opacity=0.5`）与「开始导出 / 读取配置 / 保存配置 / 整合包制作指南」均为静态保留 | 仅规则与动作 | 26 高复选框树、子项显隐（Mod / 资源包 / 光影包 / 存档 / 启动器）、默认勾选（基本 / 设置 / OptiFine / Mod / 资源包 / 光影包 / 光影设置 / 协议 / Java / 启动器 / 个性化 / 整合包重要数据 / Mod 设置 / TaCZ / 沉浸画）不得改 |
| **R52** | 「修改」页复用安装页视图：Windows `PageDownloadModify` 继承 `PageDownloadInstall`，运行时把 `BtnStart` 改为「开始修改」、`LabSelectName` 改为原名、`TextSelectName` 预填原名并显示 `Lab*Old` 旧版本信息；Linux 以 `PageDownloadInstallView(isModify: true)` 复用同一套 XAML，静态加载态与安装页一致，原名 / 旧版本信息待实例模板接入后按 vb 补齐 | 仅选择页动态信息 | 加载卡片、版本列表与安装配置的布局数值不得改 |

---

### 2.2 主题基线（D8，2026-11-08 定案）

**为什么必须在铺外壳之前定**：R13 暴露了一个系统性风险——Avalonia 的 Fluent 默认外观（`ComboBox` 弹出层、悬停高亮、圆角、焦点环、
`ScrollBar` 浮层、阴影）与 WPF 端「逐个控件 `OverridesDefaultStyle=True` + 自写 `Template`」的做法差别很大。
只要还依赖 Fluent 的默认 ControlTheme，就**不可能** 1:1；届时几十页按错误基线铺完再统一返工，代价远大于先补模板。

**基线结论：自绘优先，Fluent 只做兜底。**

1. Windows 端凡 `OverridesDefaultStyle=True` + 自定义 `Template` 的控件类型（下表），Linux 侧**必须**提供自己的
   `ControlTheme`，放 `PCL.Desktop.Linux/Theme/PclControls.axaml` 的 `<Styles.Resources>` 内；模板结构、部件名、
   尺寸、色值、状态与时序逐项照抄原 xaml。
2. **显式绑定主题**：派生控件用 `<Style Selector="c|MyX"><Setter Property="Theme" Value="{StaticResource MyXTheme}"/>`
   指定主题，不依赖主题查找顺序；`StyleKeyOverride` 保留作安全网（控件拿不到主题时不至于完全不渲染）。
3. 自绘模板内**禁止**引用任何 Fluent 资源键（如 `ComboBoxBackground`、`OverlayCornerRadius`），只用
   `Theme/PclColors.axaml` 的 25 个色值与自绘常量。
4. `FluentTheme` **保留**在 `Application.Styles`：删掉会让尚未移植的控件直接没有主题、整页空白。它只服务
   「尚未开工」的控件；某控件一旦开工，就切到自绘 `ControlTheme`，不允许「先靠 Fluent 凑合」。
5. 可见差别**只允许出现在沿用 Fluent 的控件上**，且必须逐条登记（R6 / R15 …），不得出现在已自绘控件上。

**Windows 端 `OverridesDefaultStyle=True` 清单（`pcl-next/Application.xaml`，13 处）**

| # | 类型 | 行号 | Linux 处置 |
|---|---|---|---|
| 1 | `Window`（`MyWindow`） | 69–78 | 自绘（外壳 LU3，含标题栏/缩放 R2） |
| 2 | `Label` | 81–98 | 无 Avalonia `Label`，用 `TextBlock` 等价 |
| 3 | `MyTextButton` | 99–117 | 用 `TextBlock` / `Button` 自绘等价 |
| 4 | `TextBlock`（默认 + `BasedOnTextBlock`） | 120–133 | 全局 `Style`（字号 13 / `ColorBrush1` / 省略号 / 字族）✅ 已落 |
| 5 | `MyScrollBar` | 148–181 | **自绘** ✅ 本轮已落：宽 8、圆角 2、`ColorBrushSemiTransparent` 底 + 前景色拇指（圆角 3 / `Margin 2`）、`IsDirectionReversed`、翻页热区不可见、仅鼠标悬停可翻页 |
| 6 | `MyScrollViewer` | 182–197 | **自绘** ✅ 本轮已落：无横向、右置贴边 8px 竖条 |
| 7 | `ScrollViewer`（`ScrollViewerFullMargin` / `ScrollViewerNoMargin`） | 199–238 | **自绘**（`MyScrollViewer` 变体已落；`FullMargin` 的滚动条 `Margin 2,1,1,1` 与 `NoMargin` 变体在用到它们的页面再补，登记 R15） |
| 8 | `MyTextBox` | 241–324 | **自绘**（现有为精简等价模板，需按原模板补全） |
| 9 | `MyComboBox` | 336–405 | **自绘** ✅ 本轮已落 |
| 10 | `ToggleButton`（ComboBox 箭头） | 406–446 | **自绘** ✅ 本轮已落（15px V 形、0.2s 旋转 180°） |
| 11 | `ComboBoxItem`（`MyComboBoxItem` + 默认） | 447–469 | **自绘** ✅ 本轮已落（`Padding 6,4`、无高亮） |
| 12 | `ContextMenu` | 475–494 | **自绘**（白底、`ColorBrush1` 1px 边框、圆角 3、`DropShadowEffect 0.4/4/2`） |
| 13 | `MenuItem`（`MyMenuItem`） | 495 起 | **自绘**（高 26、图标列 `SharedSizeGroup`、`ColorBrush1`） |

另有 `Controls/*.xaml` 自带模板的 10 个控件（`MyButton` / `MyCheckBox` / `MyExtraButton` / `MyExtraTextButton` /
`MyHint` / `MyIconButton` / `MyIconTextButton` / `MyListItem` / `MyLoading` / `MyRadioBox` / `MyRadioButton` /
`MySearchBox` / `MySlider` / `MySliderDot`），一律自绘；其中 `MySlider(Dot)` / `MySearchBox` / `MyRadioBox*` 尚未落地。

**沿用 Fluent 的控件与可见差别登记**

| 控件 | 现状 | 与 Windows 端的可见差别 | 落地时机 |
|---|---|---|---|
| `Slider`（Fluent） | 未自绘 | 轨道高度、滑块形状、悬停范围 | 对应设置页开工前自绘 |
| `ToolTip` | 未自绘 | 圆角、阴影、内边距 | 外壳阶段自绘 |
| `ContextMenu` / `MenuItem` | 未自绘 | 弹层阴影、高亮条 | 首个右键菜单页面开工前自绘 |
| `ProgressBar` | 未自绘 | 高度、圆角、动画 | 下载区开工前自绘 |
| `Popup`（容器本身） | Avalonia 内建 | 无可见绘制，仅承载 | 沿用，不登记 |
| `TextBox` 的 IME 候选窗 | 平台/输入法 | 位置与样式 | 登记 R6（不在自绘范围） |

**代价对比（为什么不用另一条路线）**

| 方案 | 做法 | 代价 | 结论 |
|---|---|---|---|
| **A. 自绘优先（采纳）** | 逐控件提供 `ControlTheme`，照抄 Windows 模板 | 一次性写 6–8 个完整模板（`ComboBox`/`ComboBoxItem+ToggleButton`/`ScrollBar`/`ScrollViewer`/`TextBox`/`Slider`/`ToolTip`/`ProgressBar`），其中 `ComboBox`、`ScrollBar+ScrollViewer` 最重 | 基线只定一次，后面 43 页不再返工 |
| B. 靠 Fluent + 属性覆盖 | 只改颜色/圆角/字号 | 每页都残留圆角、悬停、滚动条、弹层差异；逐页判不合格再返工，最终仍要写模板 | 已由 R13 证明不可行，**不采纳** |

→ 1:1 口径下只有 A 能达标，因此本条**不需要用户拍板**，已按 A 执行；实施顺序是「先补当前页面与外壳用到的控件模板，
再铺页面」，即 `ComboBox`（本轮已落）→ `ScrollBar`/`ScrollViewer` → 外壳 → `TextBox` 补全 → 其余按页面开工顺序。

### 2.3 合规约束：默认锁蓝（条约）+ 皮肤可自由切换（功能一致）

**条约原文**（`PCL_NEXT_Linux/LICENCE` L36–L39，第 5 条）：

> 5、软件在 Windows 系统上运行时，不应有与 PCL 的赞助解锁功能类似的功能或表现。例如：界面的主要色相必须为蓝色（即使没有修改功能，改为其他色相也和需要赞助解锁的主题功能类似）。

**执行口径**（经用户 2026-11-08 两次修正后的最新口径，后者覆盖前者）：

| 层 | 要求 | 落地方式 |
|---|---|---|
| **默认基线（条约约束）** | 出厂默认必须是 PCL 蓝：`ColorBrush3 = #1370f3` 体系（`ColorHue=210`、`ColorSat=85`、`ColorLightAdjust=0`），不得因平台不同而改默认配色 | `Theme/PclColors.axaml` 色值与 `pcl-next/Application.xaml` 一致；`Theme/PclTheme.cs` 主题参数默认值与 `ModSecret.vb` L66 逐项一致 |
| **主题 / 皮肤切换（功能一致）** | **允许且必须按 Windows 端 1:1 还原**：设置项存在、入口可见、不置灰、不屏蔽、可持久化；不得自行加锁或删入口 | 按 Windows 端实际行为实现（见 §13.8 核实结论），不在此额外施加条约限制 |

**核实结论（关键）**：本 fork（pcl-next）的皮肤机制**没有**「皮肤编号 → 色相 / 饱和 / 亮度 / 顶栏色相差」的映射表（详见 §13.8）；
`ThemeRefresh(NewTheme)` 只记录 `ThemeNow` 用于早退，调色板恒由 `ColorHue / ColorSat / ColorLightAdjust / ColorHueTopbarDelta`
四个固定参数算出。因此 Linux 侧 1:1 的形态是「**入口与持久化同 Windows、配色恒为默认蓝**」，
而不是「不实现皮肤」；`PclTheme.Refresh(int theme)` 已按 Windows 语义落成唯一切换入口，
一旦取得上游映射数据，只在该入口一处推导，不改其他任何地方。

**不得做的事**：
- 不得因为「条约要求蓝色」而禁用、置灰或隐藏设置页的皮肤列表（用户已明确纠正）。
- 不得自行发明皮肤编号与 HSL 参数的对应关系（Windows 端无此数据，编造即偏离 1:1）。
- 不得把默认参数调离 PCL 蓝（`ColorBrush3 #1370f3` 体系）。

## 3. 目标与可验证标准

**目标**：在 `PCL.Desktop.Linux` 内按 §5 清单逐页做 1:1 重写，直到 Windows 端的 63 个 `.xaml`
（控件 18 + 外壳 1 + 页面 43 + 资源 1）全部有对应实现且通过 §2 的 L/C/F/S/T 静态核对与 I/E 走查。

| 判据 | 方式 |
|---|---|
| `linux-x64` **0 错误**编译 | `dotnet build PCL.Desktop.Linux/PCL.Desktop.Linux.csproj -c Release -r linux-x64` |
| XAML 生成期校验 | 统一 `.axaml` 扩展名；构建日志 0 `AVLN` 错误 |
| 逐页静态核对 | 每页交付说明带 L/C/F/S/T 取值清单 |
| 运行期走查（I/E） | **统一在全部 UI 成型后一次性执行**（WSL2 + WSLg，见 §13.3 / §14）；中途只做不弹窗的编译与静态检查 |
| 内核不回归 | `dotnet test PCL.Download.Tests -c Release` 全绿 |

---

## 4. 阶段划分与验收

| 阶段 | 内容 | 验收方式 | 状态 |
|---|---|---|---|
| **LU1** 宿主骨架 | `PCL.Desktop.Linux`（`net10.0` + Avalonia 11.3.2）空壳；产物走 `$(PclTreeRoot)`；不进 `pcl-next.sln` | `-r linux-x64` 0 错误；ELF apphost + `libSkiaSharp.so` 落位 | ✅ |
| **LU2** 主题 + 首批控件 + 首屏 | `Theme/PclColors.axaml`（色值原样）+ `Theme/PclControls.axaml`（TextBlock / MyCard / MyButton / MyTextBox / MyComboBox / MyComboBoxItem）+ **第一页 1:1：`PageSetupLink`** | 编译 0 错误；页面 L/C/F/S/T 清单；I/E 待 Linux 走查 | ✅（I/E 待验） |
| **LU3** 外壳与导航 | `FormMain` 自绘标题栏/缩放、左侧 5 条导航（`PageLaunchLeft` / `PageInstanceLeft` / `PageDownloadLeft` / `PageSetupLeft` / `PageOtherLeft`）、`PageSelectLeft/Right` | 能切换 5 个主区，标题栏尺寸与 Windows 一致 | ⬜ |
| **LU4** 剩余基础控件 | `MyCheckBox` / `MyRadioBox` / `MyRadioButton` / `MyIconButton` / `MyIconTextButton` / `MyExtraButton` / `MyExtraTextButton` / `MyHint` / `MyListItem` / `MyLoading` / `MySearchBox` / `MySlider` / `MySliderDot` / `MyMsg*` + `Resources/Custom.xaml` 图标 | 每个控件一个 1:1 对照条目 | ⬜ |
| **LU5** 页面主体 | 启动页（`PageLaunchLeft/Right`、`MySkin`、`PageLogin*`）、设置页（`PageSetupLaunch/System/UI`）、启动实例页（`PageInstanceOverall/Setup/Mod/ModDisabled/Export`、`MyLocalModItem`）、下载页（`PageResource` + 资源子页 9 个） | 逐页 §2 口径 | ⬜ |
| **LU6** 平台集成与收尾 | 字体内嵌 PCL English、WebP 走 Skia、托盘/XDG/`xdg-open`、打包 `tar.gz`（AppImage 另议） | 每项一条登记差异关闭 | ⬜ |

> 纪律：一次只铺一阶段；控件必须先于使用它的页面。

---

## 5. 逐页面移植清单（63 个 `.xaml`）

**每行对照基准 = 同路径的原 `.xaml`**（行为部分另对照同名 `.xaml.vb`），
**每行验收依据 = §2 的 L/C/F/S/T 静态核对 + I/E 人工走查**，下表只列「该行特有检查点」。
优先级：P0 = 能起主界面（壳 / 基础控件 / 启动引导）；P1 = 主要功能区；P2 = 辅助与设置。

### 5.1 资源与主题（2）

| 文件 | 优先级 | 特有检查点 | 状态 |
|---|---|---|---|
| `Application.xaml` | P0 | 全部色值键名与色值、全局 `TextBlock` 样式（字号 13 / ColorBrush1 / 字符省略号） | ✅ 色值与 TextBlock 样式已搬（R1 字族） |
| `Resources/Custom.xaml` | P0 | 图标 `Geometry` 路径数据逐字节搬运 | ⬜ |

### 5.2 基础控件（16）

| 文件 | 优先级 | 特有检查点 | 状态 |
|---|---|---|---|
| `Controls/MyButton.xaml` | P0 | 外层 `#01eaf2fe`、内层圆角 3 / 1px / `#55FFFFFF`；Normal `Brush1`、Highlight `Brush2`、Red `RedDark`；hover `Brush3`（Red→`RedLight`）+ 背景 `Brush7`（Red→`RedBack`）；100ms/200ms；按压缩放 0.955 | ✅（动画曲线 R4） |
| `Controls/MyCheckBox.xaml` | P0 | 勾选框尺寸、边框色、选中色 | ✅（13.3） |
| `Controls/MyExtraButton.xaml` | P1 | 图标按钮尺寸与 hover 底色 `Brush7` | ⬜ |
| `Controls/MyExtraTextButton.xaml` | P1 | 文字按钮字号与下划线交互 | ✅（13.11，R28） |
| `Controls/MyHint.xaml` | P0 | 提示条背景/边框/图标与字号 | ✅（13.3，R11） |
| `Controls/MyIconButton.xaml` | P0 | 矢量图标 + 圆角 + hover | ✅（13.3，R12） |
| `Controls/MyIconTextButton.xaml` | P1 | 图标 + 文字间距、字号 | ⬜ |
| `Controls/MyListItem.xaml` | P0 | 列表项高度、左图标、副标题灰阶、选中底色 | ✅（13.3） |
| `Controls/MyLoading.xaml` | P0 | 加载图形尺寸与旋转时序 | ✅（13.3，R10） |
| `Controls/MyMsg/MyMsgInput.xaml` | P1 | 弹窗尺寸、按钮排布 | ⬜ |
| `Controls/MyMsg/MyMsgSelect.xaml` | P1 | 选项列表与按钮排布 | ⬜ |
| `Controls/MyMsg/MyMsgText.xaml` | P1 | 文本弹窗内边距与按钮 | ⬜ |
| `Controls/MyRadioBox.xaml` | P1 | 圆点尺寸、选中色 | ✅（13.11） |
| `Controls/MyRadioButton.xaml` | P1 | 单选按钮间距与字号 | ✅（13.7） |
| `Controls/MySearchBox.xaml` | P1 | 搜索图标位置、占位文案 | ⬜ |
| `Controls/MySlider.xaml` | P1 | 轨道高度、滑块尺寸、渐变 | ✅（13.11，R27） |
| `Controls/MySliderDot.xaml` | P1 | 圆点尺寸与选中态 | ⬜ |

> 注：`MyCard` 在 WPF 端无独立 `.xaml`（纯代码类），对照基准为 [MyCard.vb](file:///d:/bing/MC/pcl_x/PCL_NEXT_Linux/pcl-next/Controls/MyCard.vb)；`MyTextBox` / `MyComboBox` 样式取自 `Application.xaml`。

### 5.3 外壳与主区（3）

| 文件 | 优先级 | 特有检查点 | 状态 |
|---|---|---|---|
| `FormMain.xaml` | P0 | 自绘标题栏高度、最小化/最大化/关闭按钮尺寸与图标、无边框拖拽区、窗口圆角 | ✅（13.7，R2 / R19） |
| `Pages/PageSelectLeft.xaml` | P0 | 左侧导航栏宽度、条目高度、分组标题字号 | ✅（13.10，R26） |
| `Pages/PageSelectRight.xaml` | P0 | 右侧页面容器（`MyPageRight`）留白与滚动 | ✅（13.10，空态；R26） |

### 5.4 五大左侧导航（4，`PageSelectLeft` 除外的分区导航）

| 文件 | 优先级 | 特有检查点 | 状态 |
|---|---|---|---|
| `Pages/PageLaunch/PageLaunchLeft.xaml` | P0 | 启动区导航条目与折叠 | ✅（13.10，R24 / R25） |
| `Pages/PageInstance/PageInstanceLeft.xaml` | P1 | 实例列表为空时的空态 | ⬜（E） |
| `Pages/PageDownload/PageDownloadLeft.xaml` | P1 | 下载区导航条目 | ⬜ |
| `Pages/PageSetup/PageSetupLeft.xaml` | P1 | 设置区导航条目（含联机页入口） | ✅（13.9） |
| `Pages/PageOther/PageOtherLeft.xaml` | P2 | 其他区导航条目 | ⬜ |

### 5.5 启动区页面（11）

| 文件 | 优先级 | 特有检查点 | 状态 |
|---|---|---|---|
| `Pages/PageLaunch/PageLaunchRight.xaml` | P0 | 版本卡片网格、卡片内边距、选中态 | ✅（13.10，提示条 + 日志卡；R22 / R23） |
| `Pages/PageLaunch/MySkin.xaml` | P1 | 皮肤切换控件外观 | ⬜ |
| `Pages/PageLaunch/MyMsgLogin.xaml` | P1 | 登录弹窗按钮顺序与文案 | ⬜ |
| `Pages/PageLaunch/PageLoginMs.xaml` | P1 | 微软登录步骤布局 | ⬜（R7 浏览器回调） |
| `Pages/PageLaunch/PageLoginMsSkin.xaml` | P1 | 同上的外包壳 | ⬜ |
| `Pages/PageLaunch/PageLoginAuth.xaml` | P1 | 第三方验证码登录 | ⬜ |
| `Pages/PageLaunch/PageLoginAuthSkin.xaml` | P1 | 同上的外包壳 | ⬜ |
| `Pages/PageLaunch/PageLoginLegacy.xaml` | P1 | 旧版登录输入框与提示 | ⬜ |
| `Pages/PageLaunch/PageLoginNide.xaml` | P1 | Nide 登录步骤 | ⬜ |
| `Pages/PageLaunch/PageLoginNideSkin.xaml` | P1 | 同上的外包壳 | ⬜ |
| `Pages/PageSpeedLeft.xaml` / `Pages/PageSpeedRight.xaml` | P2 | 加速页布局（Windows 专有优化器，Linux 侧按 R7 处理） | ⬜ |

### 5.6 设置区页面（4）

| 文件 | 优先级 | 特有检查点 | 状态 |
|---|---|---|---|
| `Pages/PageSetup/PageSetupLink.xaml` | P1（试点先做） | 两张卡片的行高 28/15/35、列宽 Auto/1*/60、按钮 `MinWidth 140` + `Padding 13,5` + 间距 20 | ✅（R5 折叠、R6 弹出层） |
| `Pages/PageSetup/PageSetupLaunch.xaml` | P1 | 启动选项下拉与输入框网格、注册表相关项按 R7 处理 | ✅（13.12，R29） |
| `Pages/PageSetup/PageSetupSystem.xaml` | P1 | 最大的一页（17 KB）：多卡片、多网格、滑块/开关 | ⬜ |
| `Pages/PageSetup/PageSetupUI.xaml` | P1 | 最大的一页（36 KB）：主题、背景、字体预览 | ⬜（R9 缩放） |

### 5.7 实例区页面（7）

| 文件 | 优先级 | 特有检查点 | 状态 |
|---|---|---|---|
| `Pages/PageInstance/PageInstanceOverall.xaml` | P1 | 实例总览卡片栅格 | ⬜ |
| `Pages/PageInstance/PageInstanceSetup.xaml` | P1 | 实例设置表单网格 | ⬜ |
| `Pages/PageInstance/PageInstanceMod.xaml` | P1 | Mod 列表、搜索、批量操作栏 | ⬜ |
| `Pages/PageInstance/PageInstanceModDisabled.xaml` | P1 | 禁用列表的空态与提示 | ⬜（E） |
| `Pages/PageInstance/PageInstanceExport.xaml` | P1 | 导出步骤与进度 | ⬜ |
| `Pages/PageInstance/MyLocalModItem.xaml` | P1 | 本地 Mod 条目（图标 + 标题 + 副标题 + 开关） | ⬜ |

### 5.8 下载区页面（10）

| 文件 | 优先级 | 特有检查点 | 状态 |
|---|---|---|---|
| `Pages/PageDownload/PageDownloadInstall.xaml` | P1 | 安装进度条与步骤文案 | ⬜ |
| `Pages/PageDownload/Resource/PageResource.xaml` | P1 | 资源页容器与筛选栏 | ⬜ |
| `Pages/PageDownload/Resource/MyResourceItem.xaml` | P1 | 资源条目卡片 | ⬜ |
| `Pages/PageDownload/Resource/PageDownloadMod.xaml` | P1 | Mod 下载列表 | ⬜ |
| `Pages/PageDownload/Resource/PageDownloadResourcePack.xaml` | P1 | 资源包列表 | ⬜ |
| `Pages/PageDownload/Resource/PageDownloadShader.xaml` | P1 | 光影列表 | ⬜ |
| `Pages/PageDownload/Resource/PageDownloadDataPack.xaml` | P1 | 数据包列表 | ⬜ |
| `Pages/PageDownload/Resource/PageDownloadPack.xaml` | P1 | 整合包列表 | ⬜ |
| `Pages/PageDownload/Resource/PageDownloadCompDetail.xaml` | P2 | 整合包详情 | ⬜ |

### 5.9 其他与链接页（5）

| 文件 | 优先级 | 特有检查点 | 状态 |
|---|---|---|---|
| `Pages/PageOther/PageOtherAbout.xaml` | P2 | 关于页品牌署名（合规 L1/L3：上游作者首位） | ⬜ |
| `Pages/PageOther/PageOtherHelp.xaml` | P2 | 帮助条目列表 | ⬜ |
| `Pages/PageOther/PageOtherHelpDetail.xaml` | P2 | 详情容器（内容由运行时注入，空态需一致） | ⬜ |
| `Pages/PageOther/PageOtherTest.xaml` | P2 | 测试页（一行 30 高文本） | ⬜ |
| `Pages/PageLink/PageLinkMain.xaml` | P2 | 联机主页（P2P 状态与房间列表，Windows 专有网络栈按 R7） | ⬜ |

**统计**：资源 1 + 控件 16（+ MyCard/MyTextBox/MyComboBox 纯代码/样式 3）+ 外壳 3 + 导航 5 + 启动 11 + 设置 4 + 实例 7 + 下载 10 + 其他链接 5 = 63 个 `.xaml` 全覆盖，无遗漏。

---

## 6. 改动要点

### 6.1 工程与产物

1. 不进 `pcl-next.sln`：Windows CI 构建整个 sln，`net10.0` 工程进去会在无 `-r` 时触发 `PclRidGuard` 报错。
2. 产物只用 `$(PclTreeRoot)` + `PclUseTreePath=true`，不手写 `bin-net10/...` 字面量。
3. `.axaml` 而非 `.xaml`（Avalonia 默认项只采集 `**/*.axaml`；用 `.xaml` 会「编译全绿、运行时找不到预编译 XAML」）。

### 6.2 主题层

- `Theme/PclColors.axaml`：色值原样搬运，键名与 Windows 端一致，便于逐项对照。
- `Theme/PclControls.axaml`：控件样式与模板集中一处，页面里不写内联色值（与 Windows 端「样式在 Application.xaml」同构）。

### 6.3 控件层

- `MyCard` = `ContentControl` + `ControlTemplate`（WPF 端是代码构造的 Grid，Avalonia 用模板等价还原）。
- `MyButton` = `Button` 派生，`Text` / `ColorType` 为 `StyledProperty`；配色与 hover 用样式类（`.highlight` / `.red`）而非代码改色。
- 控件顺序：**先控件后页面**；页面里不得为了赶进度临时内联替代控件。

### 6.4 平台服务层（LU6）

平台差异只允许出现在 `Services/`（字体解析、XDG 目录、外部打开、对话框、托盘），页面通过接口取用；
内核侧已有的 `PCL.Download/Platform/{PlatformInfo,CrossPlatformPath,PathComparison}` 是同一原则的示范。

---

## 7. 回归影响面

| 面 | 影响 | 处置 |
|---|---|---|
| Windows 构建 / `build.yml` | 新工程不进 sln，Windows job 不感知 | 不改 |
| `kernel-guard.yml`（`build-linux`） | 只编内核 + 测试，不含 GUI | 不改；GUI 是否进护栏属 LU6 决策 |
| `Directory.Build.props` 护栏 | 新工程受 `PclRidGuard` / `PclTreePathGuard` 约束 | 构建必须带 `-r` |
| 内核工程 | 不改 | 无 |
| 他人未提交改动 | 不碰、不 stash | 收尾 `git status` 核对 |

---

## 8. 验证步骤

```bash
# 仓库根 PCL_NEXT_Linux/ 下
dotnet build PCL.Desktop.Linux/PCL.Desktop.Linux.csproj -c Release -r linux-x64
ls bin-net10/Dev/*/linux-x64/
dotnet test PCL.Download.Tests/PCL.Download.Tests.csproj -c Release -r win-x64
```

判定：0 错误；产物在 `bin-net10/Dev/<版本名>-<构建号>/linux-x64/`；`PclTreePathGuard` 通过。
运行期（I/E 走查）需在 Linux + X11/Wayland 执行，**本机 Windows 不做运行验收**。

---

## 9. 回滚方案

| 场景 | 动作 | 保留 |
|---|---|---|
| 某页 1:1 无法达标且短期修不好 | 删除该页的 `.axaml`/`.cs` 与 MainWindow 引用（页面之间无横向依赖） | 主题与控件、其余页面 |
| 主题/控件模板改坏全局 | 回滚 `Theme/*.axaml` 到上一提交 | 页面文件 |
| 工程级问题（护栏/路径） | 删 `PCL.Desktop.Linux/`（新工程，无外部依赖） | 本计划与进度记录 |

一律按《AGENTS.md》§六 出回退报告，不静默回退。

---

## 10. 明确不做 / 延后（按 1:1 口径重新评估）

| 项 | 结论 | 理由 |
|---|---|---|
| 拷 VB 代码 | **不做**，等价重写 | VB.NET 无跨平台编译器 |
| 跨仓库拷安卓 `PCL.UI` | **不做** | D1 裁决 |
| 建中立共享 UI 库 | **不做**（本轮） | D1 裁决 |
| 背景音乐 / 音效（NAudio） | **不做**（用户裁决） | `ModMusic.vb` 全库仅 1 处命中（自身声明）、无调用点，不在 1:1 范围；不改 Windows 侧代码 |
| 托盘 / 注册表 / 任务栏集成 | **找替代或不做**（LU6） | Windows 专有；须在页面显式说明（R7） |
| WebP 解码 | **必须替代**（Skia） | 有等价实现，不能删图（R3） |
| 拖拽 / 无边框 / 缩放 | **必须实现** | 桌面交互基本盘（R2） |
| Minecraft 启动 / JVM 运行时 | **不做** | 属 T2 拆壳后的 `PCL.Core`，当前内核不具备 |
| AppImage / flatpak | **延后** | LU6 先出 `tar.gz` 单目录制品 |
| 改版本常量 / 打 tag / push | **禁止** | §五 / §0C |

---

## 11. 决策项

| # | 项 | 状态 |
|---|---|---|
| D1 中立 UI 归属 | **已决**：Linux 专属 GUI，写在本仓库 | ✅ |
| D2（原 LU2 是否允许在 Linux 仓库自建 UI） | 随 D1 作废 | — |
| Q1 Linux 首版是否要音频 | **已决：不做**（用户裁决；`ModMusic.vb` 无调用点，R8 关闭） | ✅ |
| Q2 发布形态 | **已决：先 `tar.gz` 单目录制品**；AppImage 等 LU6 前再定 | ✅ |
| Q3 GUI 是否纳入 `kernel-guard.yml` | **已决：暂不进**；等主窗体外壳（LU3 的 `FormMain` + 导航）起来后单独加 job | ✅ |

---

## 12. 与既有规则的对应关系

- **§零 三段树 / 真实 RID**：产物落 `bin-net10/Dev/<版本名>-<构建号>/linux-x64/`，不绕路径规则。
- **§十 三仓库架构**：Linux 专属 UI 只在本仓库演进，不跨仓库合并或拷贝。
- **§0C.4 合规**：`PLATFORM_RELEASE_ENABLED` 未开启前不对外分发；本阶段只做编译验证。
- **§三 大改动**：本计划自身即产物；每阶段在本文件 §13 追加记录（不新建文件）。

---

## 13. 进度记录

### 13.1 LU1 宿主骨架（2026-11-08，已完成）

新增 6 个文件（`PCL.Desktop.Linux.csproj` / `Program.cs` / `App.axaml(.cs)` / `MainWindow.axaml(.cs)`）。
构建：`dotnet build -r linux-x64` → 0 警告 0 错误；产出 ELF apphost（78 KB）+ `libSkiaSharp.so`（9.2 MB）+
`Avalonia.X11.dll` / `Avalonia.FreeDesktop.dll`；XAML 已进 `AvaloniaResources`。
**未做运行期验收**（本机 Windows，无 Linux 显示环境）。

### 13.2 LU2 主题 + 首批控件 + 第一页（2026-11-08）

**新增文件**

| 文件 | 内容 |
|---|---|
| `Theme/PclColors.axaml` | 25 个色值/画刷键 + `PclFont`；键名与 `Application.xaml` 一致 |
| `Theme/PclControls.axaml` | 全局 `TextBlock`（13 / ColorBrush1 / 字符省略号 / PclFont）；`MyCard` / `MyButton` / `MyTextBox` / `MyComboBox` / `MyComboBoxItem` 模板与状态样式 |
| `Controls/MyControls.cs` | `MyCard`（`Title`）、`MyButton`（`Text` + `ColorType` + 样式类）、`MyTextBox`、`MyComboBox`、`MyComboBoxItem` |
| `Views/PageSetupLinkView.axaml(.cs)` | 第一页 1:1（设置·联机） |
| `App.axaml` / `MainWindow.axaml` | 挂主题资源与样式；窗口承载该页 |

**第一页 1:1 对照说明（`PageSetupLink`）**

- 对照来源：`pcl-next/Pages/PageSetup/PageSetupLink.xaml`（54 行）+ `PageSetupLink.xaml.vb`。
- L（布局）：`ScrollViewer`(垂直 Auto / 水平 Disabled) → `StackPanel Margin 25,10`；卡 1 `Margin 0,15`、
  内容 `Margin 25,40,25,20`、`Grid` 列 `Auto[SharedSizeGroup=Name] / 1* / 60`、行 `28`；
  卡 2 `Margin 0,0,0,15`、内容 `Margin 25,40,25,22`、行 `28 / 15 / 35`、按钮 `MinWidth 140` + `Padding 13,5` + `Margin 0,0,20,0`。
- C（颜色）：卡片底 `#F5FFFFFF`、圆角 5、标题 `ColorBrush1`→hover `ColorBrush2`；按钮 `Brush1/Brush2` + `Brush3` hover + `Brush7` hover 底 + 边框 1px + 圆角 3；输入框/下拉 `ColorBrushBg0` 边框 + `ColorBrushHalfWhite` 底 + 圆角 3。
- F（字体）：字号 13；字族走 R1。
- S（形态）：`MyCard` 阴影 `Margin -3,-4,-3,-3`、`BoxShadow 0 0 6 #12343d4a`（hover `#66343d4a`）。
- T（状态）：按钮 hover 变色 200ms（`BrushTransition`）、按压缩放 0.955；卡片阴影与标题 90ms 过渡。
- 文案与 ToolTip 原文照抄（含换行、空格前缀「 光荣地贡献节点！」）。
- **当前差异**：R5（卡 2 折叠未接入，页内以展开态呈现）、R6（ComboBox 弹出层模板未逐像素核对）、R1（字族）、R4（动画曲线）。
- **未验证**：I/E 走查需在 Linux 显示环境执行。

**构建验证**：`dotnet build -r linux-x64` → **0 警告 0 错误**，产物
`bin-net10/Dev/2.100.0.4-0.77/linux-x64/`（构建号由 `obj/pcl-build.small` 提供，本次已修正钩子使其在独立仓库里
按 `scripts/build-number.base` 叠加基数，不再从 0.01 重新起算）。

**顺带修正（仓库级）**：`scripts/git-hooks/prepare-commit-msg` 与 `post-commit` 原先不读
`scripts/build-number.base`，在 2026-11-08 起的三独立仓库模式下会把构建号算成 `0.0x`
（比旧线更小，违反 §5.2.1 单调性）；已补上基数逻辑，与 `Get-BuildSmallVersion.ps1` 同源。

### 13.3 LU4 第二批控件 + WSL2 运行期首跑（2026-11-08）

**本批控件（1:1 对照 Windows 源）**

| 控件 | 对照基准 | 已还原要点 | 登记差异 |
|---|---|---|---|
| `MyCheckBox` | `Controls/MyCheckBox.xaml(.vb)` | 边框 18×18 / 圆角 3 / 边框 1.1 / 底 `ColorBrushHalfWhite` / 左边距 1；勾选路径 12×12、中心缩放 0→1、150ms；文字 13 号、左边距 26；勾选 `ColorBrush2`、悬停 `ColorBrush3`、禁用 `ColorBrushGray5`、按下底 `ColorBrushBg1` | R10 |
| `MyHint` | `Controls/MyHint.xaml(.vb)` | 左边框 3px / 圆角 2 / `HasBorder` 补 1px 三边；三套主题 `#d9ecff·#1172D4·#0F64B8` / `#ffdddf·#d82929·#bf0b0b` / `#ffebd7·#f57a00·#d86c00`；行高 16、内边距 12,9、右边距 -1、自动换行；关闭按钮 20×20 + ToolTip「不再提示」 | R11 |
| `MyListItem` | `Controls/MyListItem.xaml` | 高 42；列 `2/0/4/1*/4`、行 `1*/Auto/Auto/1*`；标题第 2 个 `Auto` 行、第 4 列、左边距 4、超出省略；`RectBack`/`LabInfo` 在源文件里本就注释掉，同样不还原 | — |
| `MyLoading` | `Controls/MyLoading.xaml(.vb)` | 最小 50×50、底 `#01eaf2fe`；图形区 60×47、镐子 35 高/描边 2/旋转 55°、碎片 3×5、错误叉 17×17 缩放 0.6、底线 25×2；文本 16 号、上边距 10、默认「加载中」/错误「加载失败」 | R10 |
| `MyIconButton` | `Controls/MyIconButton.xaml(.vb)` | 外层/内层圆角 1000、边框 0.001、底 `#01eaf2fe`；`Path` `Stretch=Uniform` + `Margin 5`；按下内层 0.8 缩放；`Logo` 以 SVG 路径字符串传入 | R12 |

**构建验证**：`dotnet build PCL.Desktop.Linux/PCL.Desktop.Linux.csproj -c Release -r linux-x64` → **0 错误**，
产物 `bin-net10/Dev/2.100.0.4-0.80/linux-x64/`。

**WSL2 运行环境（实跑，替代此前「只能在 Linux 本机验证」的边界）**

- 发行版：`Ubuntu-24.04`（**默认发行版未改**，未新建任何发行版/虚拟机），内核 6.18.x；WSLg 可用：
  `DISPLAY=:0`、`WAYLAND_DISPLAY=wayland-0`、`/mnt/wslg` 存在；虚拟屏幕 1920×1200。
- .NET：`/opt/dotnet` 下已有 SDK 10.0.403（**为原有环境，本轮未安装**）。
- 本轮在该发行版内补装（用于让 GUI 起得来）：
  - X11 客户端库 `libice6 libsm6 libx11-6 libxext6 libxrandr2 libxrender1 libxcursor1 libxi6 libxkbcommon0 libxkbcommon-x11-0 libfontconfig1 libfreetype6`
    （缺 `libICE.so.6` 时 apphost 直接启动失败，报 `DllNotFoundException`）；
  - `fonts-noto-cjk`（89 MB，未装前中文全是豆腐块）；
  - `xdotool`（交互走查用）。
  - 装完已 `apt-get clean` 并清空 `/var/lib/apt/lists`；`ext4.vhdx` 现为 3.89 GB（`D:\WSL\Ubuntu-24.04`）。
- 运行方式：仓库与产物**都留在 `/mnt/d`**（未复制到家目录、未把构建输出改到 WSL 原生盘）；
  `DOTNET_ROOT=/opt/dotnet` 后直接运行 apphost（WSL 内 dotnet 不在 `/usr/share/dotnet`，需显式给根目录）。

**首次成功启动**：窗口按要求拉起（标题 `pcl-next · Linux`，客户端 960×640），中文正常渲染，
`PageSetupLink` 两张卡片的标题/文本/输入框/按钮均按布局落位。

**环境备注（非代码问题）**

- WSLg 的 GL 走 ZINK→d3d12；本次会话里 `dxgkio` 驱动查询失败（`Failed to choose pdev`），
  GLX 回退 `drisw` 也报错。窗口仍能起，但渲染后端不稳定。
- 用 `PrintWindow` 抓图会使窗口被销毁（进程存活但 X 窗口消失）；后续统一测试改用「置顶 + 屏幕抓取」。
- `xdotool` 在窗口失效后下发点击/按键会返回 `BadWindow`。
- 恢复 WSLg 稳定会话通常需要 `wsl --shutdown` / `wsl --terminate`，但同一发行版**另有会话在用**，
  未擅自执行；统一测试前若仍需恢复，先征询。

**测试节奏（用户 2026-11-08 裁决）**：**先把 UI 写完，最后统一测试**。中途不再反复拉起窗口，
只做 `dotnet build -r linux-x64` 与 XAML 资源等静态检查；需要靠运行才能判断的点一律进 §14 待验收清单。

**下一步**：按 §5 清单继续写页面与控件 → `FormMain` 外壳 + 左侧导航 → 全部成型后按 §14 一次性统一走查。

### 13.4 R13 / R14 缺陷修复（2026-11-08，只做编译验证、不弹窗）

用户指示：R13 / R14 当缺陷立刻修，不留到统一验收；修完只做编译验证、不起窗口。

| 项 | 症状 | 根因（已核实） | 修法 | 改动文件 |
|---|---|---|---|---|
| **R13** | `MyComboBox` 收起态完全不渲染（无边框、无选中文案、无箭头） | `MyComboBox : ComboBox` 未覆写 `StyleKeyOverride`；Avalonia Fluent 的 ControlTheme 按**派生类型**查找，查不到即无 `Template`，控件不画任何东西。对照 `pcl-next/Controls/MyComboBox.vb` 确认 Windows 端**没有** `MyComboBox.xaml`（纯继承 WPF 默认模板 + `RefreshColor` 配色 + `PART_EditableTextBox`/`PART_Content` 访问），所以不存在「`PART_` 名称对不上」的模板可抄，正确修法是回到基类主题键 | ① `MyComboBox` 补 `StyleKeyOverride => typeof(ComboBox)`；`MyComboBoxItem` 补 `typeof(ComboBoxItem)`（下拉项同理，否则展开后也是空白）。② 配色照抄 `RefreshColor`：常态 前景 `ColorBrushBg0` / 背景 `ColorBrushHalfWhite`（100ms）、悬停 前景 `ColorBrush4` / 背景 `ColorBrush7`（100ms）、按下与展开 前景 `ColorBrush3` / 背景 `ColorBrush7`（10ms）、禁用 前景 `ColorBrushGray5` / 背景 `ColorBrushGray6`（200ms）；边框固定 `ColorBrushBg0`、圆角 3、字号 13、下拉项高 26。③ 按下态由控件自身维护 `:pressed`（照抄 Windows 的 `PreviewMouseLeftButtonDown/Up + MouseLeave` 三钩子），展开态用 `:expanded`（跟随 `IsDropDownOpen`），不依赖 Fluent 是否给 ComboBox 设了同名伪类 | `Controls/MyControls.cs`、`Theme/PclControls.axaml` |
| **R14** | 窗口纯白底，`MyCard` 的 `#F5FFFFFF` 卡底与 7% 阴影不可见 | 宿主窗口未设底色，Avalonia 默认白 | 宿主窗口底色设为 `ColorBrush1`（`#343d4a`），与 Windows 端窗口底色一致；**属「照抄」而非「允许差异」**（色值原样取自 Windows `Application.xaml` L22，未做近似） | `MainWindow.axaml` |

**验证**（按用户要求只做编译验证，**未起窗口**）：

```
dotnet build PCL.Desktop.Linux/PCL.Desktop.Linux.csproj -c Release -r linux-x64
→ 0 警告 / 0 错误
产物：bin-net10/Dev/2.100.0.4-0.81/linux-x64/PCL.Desktop.Linux.dll
```

**未验证项（转 §14）**：收起态是否真渲染、边框/文案/箭头观感、弹出层样式（R6）、
卡底与阴影在 `#343d4a` 底上的对比度——均需统一验收阶段一次性目视确认（V1 / V2）。

---

### 13.5 主题基线定案 + ComboBox 自绘主题（2026-11-08，只做编译验证、不弹窗）

**① 主题基线**：见 §2.2 / D8（自绘优先，Fluent 只做兜底）。核对依据是 `pcl-next/Application.xaml` 里
13 处 `OverridesDefaultStyle=True`；其中 `MyComboBox` / `ComboBox` 箭头 `ToggleButton` / `ComboBoxItem` 三处本轮落地。

**② 自绘 ComboBox 主题（R13 的最终形态）**：照抄 `pcl-next/Application.xaml` L336–L469 的三个模板，落到
`PCL.Desktop.Linux/Theme/PclControls.axaml` 的 `<Styles.Resources>`：

| 模板 | 对照来源 | 照抄的关键取值 |
|---|---|---|
| `MyComboBoxTheme` | Application.xaml L336–L370 | 外层 `Border` 圆角 3 / 1px、**边框色 = 前景色**（`BorderBrush="{TemplateBinding Foreground}"`，与 `RefreshColor` 一起变色）；箭头 `ToggleButton` `Margin 0,0,1.5,0`；内容 `ContentControl` `Margin 8.5,0,21.5,0`、`IsHitTestVisible=False`、垂直居中、内容取 `SelectionBoxItem`；`Popup` `VerticalOffset=-1.5`、宽度 = 控件宽度、`MaxDropDownHeight` 320；弹层白底内衬 + 圆角 3 边框 + `ScrollViewer` + `ItemsPresenter` |
| `MyComboBoxToggleTheme` | Application.xaml L406–L446 | 近透明热区 `ColorBrushSemiTransparent`；箭头 `Path` 宽 15、`Data="M1,1L4.5,4.5L8,1"`、`StrokeThickness=1.5`、`RenderTransformOrigin 0.3,0.5`、右对齐居中；展开旋转 180°、200ms（缓动曲线近似，R10） |
| `MyComboBoxItemTheme` | Application.xaml L447–L469 | `Padding 6,4`、背景 `ColorBrushTransparent`、无悬停高亮、`FocusAdorner` 置空 |

**③ 与 Fluent 的切割**：`c|MyComboBox` / `c|MyComboBoxItem` 的样式里用
`<Setter Property="Theme" Value="{StaticResource …Theme}"/>` 显式切到自绘主题，不再依赖 Fluent 默认绘制；
`StyleKeyOverride` 保留作安全网。

**④ 验证**（未起窗口）：

```
dotnet build PCL.Desktop.Linux/PCL.Desktop.Linux.csproj -c Release -r linux-x64
→ 0 警告 / 0 错误
产物：bin-net10/Dev/2.100.0.4-0.82/linux-x64/PCL.Desktop.Linux.dll
```

**⑤ 未复刻项（转 R6 / 统一验收）**：`HasItems=false → dropDownBorder 高 26` 触发器、
弹层透明度跟随主窗口（`PanPopup.Opacity = FrmMain.Opacity`，需外壳提供不透明度）、
`IsEditable` 分支（本轮页面未用可编辑 ComboBox）、弹出层滚动条（等 `ScrollBar`/`ScrollViewer` 自绘后统一）。

### 13.6 ScrollBar / ScrollViewer 自绘（2026-11-08，只做编译验证、不弹窗）

| 自绘主题 | 对照来源 | 照抄的关键取值 | Avalonia 侧的必要调整 |
|---|---|---|---|
| `MyScrollBarThumbTheme` | Application.xaml L136–L147（`MyThumb`） | 外层底 `ColorBrushSemiTransparent`；内层 `Background = 前景色`、`CornerRadius 3`、`Margin 2` | 无（`Thumb` 直接给模板） |
| `MyScrollBarTheme` | Application.xaml L148–L181（`MyScrollBar`） | `MinWidth/Width 8`、外层 `CornerRadius 2`、`Track IsDirectionReversed=True`、翻页按钮不可见（原 `Opacity=0`）、拇指取前景色、**仅鼠标悬停可翻页**（原 `Track.IsEnabled = IsMouseOver`） | ① 翻页按钮改用全透明模板（Avalonia 的 `Opacity=0` 在部分后端仍会命中，透明模板更贴近原意）；② 上/下翻页按钮必须沿用 Fluent 的部件名 `PART_PageUpButton` / `PART_PageDownButton`（Avalonia 的 `ScrollBar` 按这两个名字接线）；③ `Track` 在原版**无 `Name`**，这里同样不命名；④ `IsEnabled` 绑 `IsPointerOver`，`Track` 的 `Minimum/Maximum/Value/ViewportSize/Orientation` 全部照 Fluent 的绑法接上 |
| `MyScrollViewerTheme` | Application.xaml L182–L197（`MyScrollViewer`） | 无横向、右置贴边 8px 竖条、内容 `Padding = TemplateBinding Padding` | ① 部件名沿用 Avalonia 约定 `PART_ContentPresenter` / `PART_VerticalScrollBar`；② 保留 `ScrollGestureRecognizer`（滚轮/触控惯性）；③ 滚动条的 `Minimum/Maximum/Value/ViewportSize/Visibility` 由 `ScrollViewer` 类内部接线，模板不必重复绑定 |

**应用方式**：`ScrollBar` / `ScrollViewer` 用**全局**样式 `<Setter Property="Theme">` 切到自绘主题，
这样连 Fluent 模板内部创建的滚动条（如 ComboBox 弹层里的 `ScrollViewer`）也一并换掉，不留 Fluent 残留。

**验证**（未起窗口）：`dotnet build PCL.Desktop.Linux/PCL.Desktop.Linux.csproj -c Release -r linux-x64`
→ 0 警告 / 0 错误（产物 `bin-net10/Dev/2.100.0.4-0.83/linux-x64/`）。

**未覆盖（登记 R15）**：`ScrollViewerFullMargin`（滚动条 `Margin 2,1,1,1`）与 `ScrollViewerNoMargin` 变体按页面补；
横向滚动条未自绘（Windows 端全部 `Disabled`）。**需统一验收目视**（并入 V3）：滚动条贴边、拇指配色、
翻页是否只在悬停时可用。

### 13.7 外壳（LU3，2026-11-08，只做编译验证、不弹窗）

**对照来源**：`pcl-next/FormMain.xaml`（L1–L167） + `pcl-next/FormMain.xaml.vb` 窗口行为段 + `pcl-next/Modules/ModSecret.vb` L111–L135。

| 构件 | 对照来源 | 照抄的关键取值 | Avalonia 侧的必要调整 |
|---|---|---|---|
| 窗口根 | FormMain.xaml L1–L6 | `Width 850 / Height 500`、`MinWidth 810 / MinHeight 470`、`WindowStyle=None`、`AllowsTransparency=True`、`Background={x:Null}` | `SystemDecorations=None` + `CanResize=True`；底色仍铺 `ColorBrush1`（R14）；Avalonia 11 **没有** `CanMinimize` 属性，最小化走 `WindowState.Minimized`（R19） |
| `PanBack` | L19–L23；vb L443–L454 | `Margin 10`；起手 `rotate(-4°)` + `translate Y=60`，600ms / 500ms 归零 | `TransformOperations` + `TransformOperationsTransition`；缓动 `AniEaseOutBack` → `BackEaseOut` 近似（R19） |
| 8 个缩放热区 | L24–L81；vb L408–L417 | 边 8px（`Margin 13,0` / `0,13`）、角 13×13；四向 + 四角渐变（`#21/#11/#00` 黑） | 渐变逐条照抄；拖拽改走 `BeginResizeDrag(WindowEdge.North/South/West/East/NorthWest/…)`，光标用 `TopSide/BottomSide/LeftSide/RightSide/*Corner` |
| `BorderForm` | L82–L95 | `Margin 8`、圆角 6、裁剪内容 | `Border CornerRadius=6` + `ClipToBounds=True`（等价 `RectangleGeometry` 裁剪） |
| `PanForm` | L96–L167；ModSecret.vb L124–L132 | 行 `Auto` / `1*`；背景 = `UiBackgroundColorful`（默认 True）斜向三段渐变（Offset −0.1 / 0.4 / 1.1，色相 ±15、饱和 ×0.8、亮度 91） | 运行期由 `PclTheme.CreateContentBrush()` 用同一 HSL 算法算出（R17） |
| `PanTitle` | L96–L138；ModSecret.vb L116–L121 | 高 48；渐变 48 / 54 / 48 亮度（单停靠点写法在 Windows 端等价纯色 `ColorObject4`）；logo 默认档「文字」：`PCL Next`、17 号、白色、`Margin 18,0,0,0`（`UiLogoType` 默认 1，vb L826–L834 / L862–L866）；5 个 `MyRadioButton` `Margin 5,0`、`LogoScale` 0.90 / 0.90 / 1.05 / 1.10 / 0.93；关闭 28×28 `Margin 0,0,12,0`、最小化 28×28 `Margin 0,0,44,0`、`LogoScale 0.72`、`Theme=White` | 渐变由 `PclTheme.CreateTitleBarBrush()` 算出；logo 仅接入默认档（R18） |
| 标题栏拖拽 | vb L713–L717 | 左键按下即拖，但排除按钮上的点击 | Avalonia 无 `IsMouseDirectlyOver` 等价物 → 向上找 `MyIconButton` / `MyRadioButton` 祖先（R19） |
| 关闭 / 最小化 | vb L709–L711、L737–L740 | `Close()` / `WindowState = Minimized` | 同左 |
| `PanLeft` | L139–L153 | 底 `RectLeftBackground` 宽 0 + `RectLeftShadow` 宽 4（`Opacity 0.04`、黑→透明、左对齐） | 同左 |
| `PanMain` | L154–L167 | 左页 `Auto` 列 + 右页 `1*` 列，左页后加（叠在右页之上） | 同左 |
| `MyRadioButton` | `pcl-next/Controls/MyRadioButton.xaml` + `.xaml.vb` L233–L254（`ColorState.White` 分支） | 圆角 13.5、`MinHeight/MaxHeight 27`、常态底 `ColorBrushSemiTransparent`；图标上限 16（`Margin 12,0,0,0`）、文案 `Margin 8,0,12,0`；常态白字白图标；选中 → 白底 + `ColorBrush3`；悬停 → 50 alpha `Color8`；按下 → 120 alpha `Color8` | 自绘模板（`Theme/PclControls.axaml`）；WPF 的 90/120/150ms 颜色过渡改为即时切换（并入 R10）；`ColorState.Highlight` 分支无调用点、未接入 |
| 页面栈 | vb L1438–L1471 | 5 个栈（启动 / 下载 / 联机 / 设置 / 更多），联机栈左页为空 | 仅联机栈有页面（R20） |

**验证**（未起窗口）：`dotnet build PCL.Desktop.Linux/PCL.Desktop.Linux.csproj -c Release -r linux-x64`
→ 0 警告 / 0 错误（产物 `bin-net10/Dev/2.100.0.4-0.84/linux-x64/`）。

**未接入项转登记**：R17（皮肤参数接入点已就位、映射数据待上游）、R18（logo 其余三档 / 标题栏图片）、R19（壳行为近似）、R20（页面栈其余栈与外壳其余部件）、R21（左竖条动画触发时机）。
**需统一验收目视**（新增 V8 / V9）：无边框拖拽与 8 向缩放、圆角裁剪、标题栏渐变与 logo 位置、5 个 Tab 的选中/悬停/按下配色与互斥。

### 13.8 Windows 端皮肤机制核实（2026-11-08，只做静态核实、不弹窗）

**结论：本 fork 的皮肤 / 主题切换是「入口存在、配色不变」的半桩化实现，不存在内置皮肤色表。**

| 核实点 | 位置 | 事实 |
|---|---|---|
| 主题参数 | `Modules/ModSecret.vb` L66 | `ColorHue=210`、`ColorSat=85`、`ColorLightAdjust=0`、`ColorHueTopbarDelta=0`；全库 grep 仅此一处声明，**无任何赋值语句** |
| 刷新入口 | `ModSecret.vb` L69–L110 `ThemeRefresh(Optional NewTheme)` | 只做 `If ThemeNow = NewTheme AndAlso NewTheme >= 0 Then Return` 与 `ThemeNow = NewTheme`，**不按 NewTheme 推导任何参数** |
| 解锁机制 | `ModSecret.vb` L136–L143 | `ThemeCheckAll` 空方法、`ThemeCheckOne` 恒 `True`、`ThemeUnlock` 恒 `False` → 赞助解锁主题在本 fork 已移除 |
| 设置项 | `Pages/PageSetup/Settings.vb` L140–L149 | `UiLauncherTheme=0`（`OnChanged:=ThemeRefresh`）、`UiLauncherHue=180` / `Sat=80` / `Delta=90` / `Light=20`（自定义滑块，**全库无读取点**）、`UiLauncherThemeHide` / `Hide2`（注册表 + 加密，默认 `"0|1|2|3|4"`）、`UiBackgroundColorful=True`（`OnChanged:=Sub() ThemeRefresh()`） |
| 皮肤列表 | `Pages/PageSetup/PageSetupUI.xaml` L41–L92 | 15 个 `MyRadioBox`（0 龙猫蓝 / 1 甜柠青 / 2 小草绿 / 3 菠萝黄 / 4 橡木棕 可用；5 玄素黑、6 铁杆粉、7 神秘紫、8 秋仪金、9 活跃橙、10 跳票红、11 极客蓝、12 滑稽彩、13 欧皇彩、14 自定义 全部 `IsEnabled=False`） |
| 自定义皮肤 | `PageSetupUI.xaml.vb` L388–L415 | 选中 14 只切换 4 个滑块的可见性；`HSL_Change` 调 `ThemeRefresh()`，但参数未被任何代码消费 |
| 周期重算 | `Modules/ModMain.vb` L997、L1007 | `ThemeDontClick=2` 每 50ms、`ThemeNow=12` 每 250ms 重算；因参数恒定，重算结果不变 |

**对 Linux 侧的处置**（对应 §2.3 合规约束）：
1. 不删入口、不置灰：设置页皮肤列表 1:1 复刻，`UiLauncherTheme` 可切换并持久化。
2. `PclTheme.Refresh(int theme)` 按原语义实现（早退 + 重算 + 触发调用方套用画刷），参数恒为默认值，配色恒为 PCL 蓝。
3. **不自行发明**「皮肤编号 → HSL」映射表；若要求皮肤真正改变颜色，须先由用户提供上游 PCL2 的映射数据，再在该单点接入。

**补充核实（2026-11-11，只读上游 C# 分支）**：`pcl_nex插件参考/PCL2-Nex-dev` 是另一套 **LCH/Lab** 主题体系，
不是本 fork 的 15 编号皮肤：`PCL.Core/App/ConfigEnums.cs` L16–L22 `ColorTheme { SkyBlue=0, CatBlue=1, DeathBlue=2, HmclBlue=3 }`；
`PCL.Core/UI/Theme/ThemeService.cs` L151–L166 `GetCurrentThemeArgs()` 返回 `(Hue, LightAdjust, ChromaAdjust)` 四组常量
（235/0.36/0.2、255/0/-0.2、268/-0.05/-0.1、275/-0.03/-0.35），L182–L214 用 `LabColor.FromLch` 逐通道算色；
`Plain Craft Launcher 2/Modules/UI/Theme/ThemeManager.cs` L22 `ThemeRefresh(int newTheme = -1)` **同样不使用 `newTheme`**，
只重取灰阶资源并调 `ThemeRefreshMain()`。两套编号语义与色彩空间均不一致，**不能把 Nex 的四个 LCH 参数当成 VB 0–14 的映射**。

> **P-skin 待办（2026-11-11 用户裁决：换肤以后再说，先写完 UI）**
> - 结论保留：本 fork 入口与持久化同 Windows（`UiLauncherTheme` 可切换），配色恒为 PCL 蓝；`PclTheme.Refresh` 是唯一接入点。
> - **上游无可推导数据**：VB 侧无映射表（§13.8 上表 L66 无赋值点）；C# 分支是 4 主题 LCH 体系，编号不可对齐。
> - 接入所需数据清单：① 0–14 编号 → `(Hue, Sat, LightAdjust, HueTopbarDelta)` 四参数表；② 自定义皮肤（14 号）四个滑块的取值语义
>   （`UiLauncherHue/Sat/Delta/Light`，`Settings.vb` L140–L149，目前全库无读取点）；③ 5–14 号是否随解锁开放。
> - 取得数据前**不再花时间翻参考仓库**，`PclTheme.Refresh` 保持现状即可。

### 13.9 设置栈左导航（LU3.1，2026-11-08）

**对照来源**：`pcl-next/Pages/PageSetup/PageSetupLeft.xaml`（全部）；
`pcl-next/Controls/MyListItem.xaml(.vb)`（列宽 / 选中竖条 / 悬停配色 / 按钮栏）；
`pcl-next/Controls/MyPageLeft.vb`（`AnimatedControl` 逐个进入 / 退出动画）；
`FormMain.xaml.vb` `PanMainLeft_Resize` / `PanLeft_Resize`（`RectLeftBackground` 宽度动画）。
**Linux 实现**：`PCL.Desktop.Linux/Views/PageSetupLeftView.axaml(.cs)` + `Controls/MyControls.cs`（`MyListItem` / `MyPageLeft`）+ `MainWindow.axaml.cs`（`ShowStack` / `UpdateLeftBackgroundWidth`）。

| 项 | Windows 取值 | Linux 实现 | 结论 |
|---|---|---|---|
| 容器 | `MyPageLeft`，`AnimatedControl="PanItem"`，内含 `StackPanel Margin=0,12,0,0` | 同 | 一致 |
| 四项 | `MyListItem`，高 36、`Type=RadioBox`、`MinPaddingRight=35`、`IsScaleAnimationEnabled=False`、`VerticalAlignment=Top`、`Tag` 0–3 | 同；`Tag` 换成 `Index` 属性（Avalonia 无等价 `Tag` 语义） | 一致（仅数据载体不同） |
| Logo `Geometry` | 四条路径 | **原样逐字节搬运** | 一致 |
| `LogoScale` | 0.8 / 0.77 / 0.9 / 0.8 | 同 | 一致 |
| 默认选中 | `ItemLaunch`（启动）`Checked=True` | 同 | 一致（临时降级 R20 只影响标题栏栈，不影响本页） |
| 列宽 / 边距 | 勾选列 6（`RadioBox`）、Logo 列 34（高<40 不再 +4）、标题列 `1*`、右列 `MinPaddingRight`；标题 `Margin 4,0,0,0`；Logo `Margin 6,8,4,8` | 同（`MyListItem.RefreshMetrics`） | 一致 |
| 选中竖条 | 5 宽、`ColorBrush3`、圆角 2、`Margin -1,6,0,6`、跨 4 行 | 同（`PART_RectCheck`） | 一致 |
| 悬停 / 按下 | `RectBack` 120ms 淡入（`ColorBrush7` → `ColorBrushBg1`，按下 `ColorBrush6`）；按钮栏 84/48ms 淡入淡出；淡出 180ms | 同（`Transitions` 近似） | R4 / R10 |
| 重置按钮 | 每项 1 个 `MyIconButton`（25×25、`LogoScale 0.9`、ToolTip「初始化本页设置」），点击弹确认框重置本页 | 按钮形态已还原；点击无操作（设置子页未移植） | R20 |
| 左竖条宽度 | `PanMainLeft.SizeChanged` → `RectLeftBackground.Width` 180ms 到新宽度；收窄到 0 时阴影 180ms 淡出 | 同目标值与时长；触发时机改为「切栈并完成布局后取宽度」 | R21 |
| 逐个进入 | 每项 0→1 淡入 100ms、-25 位移 500ms（原为 +5/+20 两段）、错峰 `max(15-id,7)*2` ms | 位移合并为一段 500ms、`BackEaseOut` | R4 / R10 |
| 逐个退出 | 0 淡出 50ms + -6 位移 50ms，错峰 `70/N*i` ms | 同 | 一致 |

**未验证项**：悬停配色、勾选竖条、逐个进入/退出动画、左竖条宽度动画均需 Linux 显示环境（统一验收 V 项）。

### 13.10 启动栈 + 版本选择栈（LU5.1，2026-11-11，只做编译验证、不弹窗）

**对照来源**：`pcl-next/Pages/PageLaunch/PageLaunchLeft.xaml`（全文 156 行）+ `.xaml.vb`（L634–L690 `RefreshButtonsUI` 状态分支）；
`pcl-next/Pages/PageLaunch/PageLaunchRight.xaml`（全文 21 行）；
`pcl-next/Pages/PageSelectLeft.xaml`（17 行）+ `PageSelectRight.xaml`（25 行）+ `.xaml.vb` L98–L109（空态）；
`pcl-next/FormMain.xaml.vb` L325–L385（构造期预建两页、首页直接挂载）、L1543–L1544（启动栈映射）、L1558–L1561（版本选择栈）；
`FormMain.xaml` L121（默认选中「启动」）。

**Linux 实现**：`Views/PageLaunchLeftView.axaml(.cs)`、`Views/PageLaunchRightView.axaml(.cs)`、
`Views/PageSelectLeftView.axaml(.cs)`、`Views/PageSelectRightView.axaml(.cs)`；
`MainWindow.axaml(.cs)`（首页改回启动栈、`StackVersionSelect=5` 次级栈、通用退出动画）；
`Theme/PclControls.axaml`（补 `ScrollViewerFullMargin` 变体）；
`Controls/MyControls.cs`（`MyPageLeft`：指定 `AnimatedControl` 但无可动画元素时不播放缩放动画，对齐 `MyPageLeft.vb` L17–L28）。

| 项 | Windows 取值 | Linux 实现 | 结论 |
|---|---|---|---|
| 启动左页宽度 | `PageLaunchLeft Width=300` | `MyPageLeft Width=300` | 一致 |
| `PanInput` 行列 | 行 `Auto/*/Auto/Auto/20`；列 `Auto/20/1*/Auto/10` | 同 | 一致 |
| `BtnVersion` | 行3列2、高 35、`Margin 0,10,10,0`、「版本选择」 | 同；可点击 → 版本选择栈 | 一致（R24） |
| `BtnMore` | 行3列3、高 35、初始 `Collapsed`、「版本设置」 | 同；`TextPadding` 未接入（R25），点击空操作（R24） | R24 / R25 |
| `BtnLaunch` | 行2 跨5列、高 54、`Margin 20,0`、Highlight、`Padding 30,0,30,15` | 同；Case 2 文案「下载游戏」、可点击 → 下载栈 | 一致（R24） |
| `LabVersion` | 同格底部、`Margin 35,0,35,10`、11 号、`ColorBrushGray3`、字符省略号 | 同；去掉对 `BtnLaunch` 的 `RealRenderTransform` 缩放绑定（R25） | R25 |
| `PanTypeOne` | 跨5列、`Margin 20,24,20,-4`、胶囊圆角 13 / 高 27、图标 16×16 Scale 1.05、14 号主题色 | 同（图标 `Data` 运行期注入，当前为空） | 一致（R24） |
| `PanType` | 跨5列、`Margin 20,22,20,0`、初始 `Collapsed`、正版/离线两个 `MyRadioButton` | 同；路径逐字节搬运 | 一致 |
| `PanLaunching` | 5 行网格、初始 `Collapsed`+`Opacity 0`、`MyLoading` 高 50、标题 20 / 版本名 13.5 号 + `SkewTransform -3°`、进度条高 4（69.28*/30.72*）、四行信息、提示气泡 `Width 260`、取消按钮行4 | 结构全部照抄；`DropShadowBitmapEffect` 与气泡挖孔裁剪未复刻（R25） | R25 |
| 右页 | `MyPageRight PanScroll`；`MyHint`（Blue / CanClose / HintSnapshot / Margin 0,0,0,15）；`PanCustom`；「启动日志」卡片 + 只读文本框 | `UserControl` + `ScrollViewer`；其余逐项照抄；`HasBackground=False` 用 `Transparent` 表达 | R22 / R23 |
| 版本选择左页 | `MyPageLeft AnimatedControl="PanList"`；ScrollViewer `MinWidth 250 / MaxWidth 300`、`FullMargin`；行 `Auto/*/5` | 同（`AdornerDecorator` → `Grid`，无视觉差异） | 一致（R26） |
| 版本选择右页 | 列表 / 空态卡片（居中、`Margin 40`、标题 19 号、分隔线 2、说明两行、按钮 `MinWidth 140`）/ 加载卡片三态 | 固定空态；按钮 → 下载栈 | 一致（R26） |
| 首页 | 构造期直接挂 `FrmLaunchLeft`/`FrmLaunchRight`，标题栏「启动」选中 | 同（`ShowStack(0)`，无切换动画） | **R20 已回收** |
| 版本选择栈 | `PageChange(InstanceSelect)` → `PageChangeAnim(FrmSelectLeft, FrmSelectRight)`；标题栏进入 `PanTitleInner`「版本选择」 | 切左右页；标题栏仍「启动」选中（`PanTitleInner` 未移植，R20） | R20 |

**验证**：`dotnet build PCL.Desktop.Linux/PCL.Desktop.Linux.csproj -c Release -r linux-x64`
→ **0 警告 / 0 错误**（产物 `bin-net10/Dev/2.100.0.4-0.86/linux-x64/`）。
**未验证项**：启动页与版本选择页的进入动画、右页滚动、按钮 hover/按压、空态卡片居中均需统一验收目视（V3 / V10）。

### 13.11 设置页前置控件（LU4.1，2026-11-11，只做编译验证、不弹窗）

**动机**：设置区三页（`PageSetupLaunch/UI/System`）依赖尚未移植的 `MyRadioBox`（7/24/0 处）、`MySlider`（1/8/3 处）、`MyExtraTextButton`（1 处）；
按「控件先于页面」纪律先补这三个控件。

**对照来源**：`pcl-next/Controls/MyRadioBox.xaml`（全文 9 行）+ `.xaml.vb`（`SyncUI` L101–L149、互斥 L57–L87）；
`pcl-next/Controls/MySlider.xaml`（全文 29 行）+ `.xaml.vb`（`RefreshWidth` L70–L77、`DragDoing` L93–L100、配色 L119–L161、按键 L165–L192、提示 L108–L115）；
`pcl-next/Controls/MyExtraTextButton.xaml`（全文 33 行）+ `.xaml.vb`（点击 L83–L90、缩放 L94–L125、配色 L131–L161）。

**Linux 实现**：`Controls/MyControls.cs` 三个类 + `Theme/PclControls.axaml` 三段自绘模板。

| 控件 | 关键取值（照抄） | 差异 |
|---|---|---|
| `MyRadioBox` | 圆环 18×18 / `Stroke 1.1` / `Margin 1,0,0,0`；圆点选中 9×9 `Margin 5.5`、未选中 0×0 `Margin 10`；文案 13 号 `Margin 26,0,0,0`；常态 `ColorBrush1` / 选中 `ColorBrush2` / 悬停 `ColorBrush3` / 禁用 `ColorBrushGray5`；同父容器互斥 | 150ms 勾选与颜色动画用 `Transitions` 近似（R10） |
| `MySlider` | 高 16；底轨 1px `ColorBrushBg0`×0.3（`Margin 0,2,1,0`、右对齐）；前轨 2px（`Margin 1,1,0,0`、左对齐）；滑块 10×10 `Stroke 1.2`；宽度 `Value/MaxValue*(W-10)` + 两端 0.5 补偿；拖动取值 `(x-5)/(W-10)` clamp；←/→ 步进 `ValueByKey` | 数值提示 `Popup` 未移植（R27）；拖动缩放并入 R10 |
| `MyExtraTextButton` | 高 52 / 圆角 21；外层 `ColorBrushSemiTransparent`、内层 `ColorBrush3` 下移 10 + 10px 阴影；图标 `Margin 2,12,0,12`、文案 16 号 `ColorBrush8` `Margin 12,0,0,0.8`；悬停 `ColorBrush4`、禁用 `ColorBrushGray4` | 圆角绑定改固定 21、`Show` 动画未接入、按压缩放分段合并（R28） |

**验证**：`dotnet build PCL.Desktop.Linux/PCL.Desktop.Linux.csproj -c Release -r linux-x64` → **0 警告 / 0 错误**
（产物 `bin-net10/Dev/2.100.0.4-0.87/linux-x64/`）。三个控件尚未被页面使用，需随设置页统一验收目视（V11）。

### 13.12 设置栈「启动」页（LU6.1，2026-11-11，只做编译验证、不弹窗）

**对照来源**：`pcl-next/Pages/PageSetup/PageSetupLaunch.xaml`（全文 300 行）；
默认值取自 `pcl-next/Pages/PageSetup/Settings.vb`（`LaunchArgumentIndieV2=4`、`LaunchArgumentVisible=5`、
`LaunchArgumentPriority=1`、`LaunchArgumentWindowType=1`、`LaunchRamType=0`、`LaunchRamCustom=15`、
`LaunchAdvanceGC=4`、`LaunchAdvanceGraphicCard=True`、`LaunchAdvanceJvm` 默认串）；
子页切换与默认页取自 `PageSetupLeft.xaml.vb` L20–L30 / L44–L55 / L93–L122；
`MyCard` 折叠行为取自 `Controls/MyCard.vb` L86–L103 / L284–L316。

**Linux 实现**：`Views/PageSetupLaunchView.axaml(.cs)`；
`Controls/MyControls.cs`（`MyCard` 补 `CanSwap`/`IsSwapped`、`MyTextBox` 补 `HintText` → `Watermark`）；
`Theme/PclControls.axaml`（MyCard 折叠箭头与内容隐藏样式）；
`Views/PageSetupLeftView.axaml.cs`（新增 `PageChanged` 事件）；
`MainWindow.axaml.cs`（`ShowSetupPage`：设置栈子页 0 启动 / 1 联机已接，2 个性化 / 3 其他待补）。

| 卡片 / 区域 | Windows 取值 | Linux 实现 | 结论 |
|---|---|---|---|
| 启动选项卡 | 内容 `Margin 25,40,25,25`；行高 `28/9` 交替；名称列 Auto（原 SharedSizeGroup，单 Grid 内 Auto 等价）；下拉/输入框跨 2 列 | 同；5 个下拉项与全部 ToolTip 逐字保留；`SelectedIndex` 按默认值 4/4/1/1 | 一致（R29） |
| 窗口大小 | 下拉 + 宽 60 输入 ×2 + ` × `（18 号）；自定义宽高初始 `Collapsed` | 同（`IsVisible=False`） | 一致（R29） |
| 内存分配卡 | `Margin 25,40,25,15`；自动/自定义 `MyRadioBox`（宽 110、间距 20）、滑块 `MaxValue 49 / Value 15 / IsEnabled False`、优化开关高 22；内存条三列 `4.7*/2.5*/0.7*`、分隔行高 4、`4.7 GB / 7.9 GB / 2.5 GB` | 同；渐变 `ColorObject3→ColorObject2`、`ColorBrush3×0.5`、`ColorBrush6×0.7` | 一致（R29） |
| 离线皮肤卡 | `CanSwap=True IsSwapped=True`（折叠高 40）；5 个皮肤单选、正版名/按钮组初始隐藏 | 同（折叠由 MyCard 实现；内容仍完整保留，展开可见） | R5 / R29 |
| 高级选项卡 | `IsSwapped=True`；JVM 框高 100、可换行、`MaxLength 4000`；GC 第 4 项；4 个开关（高性能显卡默认勾选）；蓝色提示条 | 同 | R5 / R29 |
| 「版本独立设置」 | `MyExtraTextButton` 居中、`Margin 0,-5,0,15`、LogoScale 0.9、箭头路径原文 | 同 | R28 |

**验证**：`dotnet build PCL.Desktop.Linux/PCL.Desktop.Linux.csproj -c Release -r linux-x64` → **0 警告 / 0 错误**
（产物 `bin-net10/Dev/2.100.0.4-0.92/linux-x64/`）。
**未验证项**：卡片折叠/展开的点击与箭头旋转、下拉弹出层、长 JVM 文本滚动、内存条比例、胶囊按钮悬停 —— 需统一验收目视（V12）。

### 13.13 设置栈「个性化」页（LU6.2，2026-11-14，只做编译验证、不弹窗）

**对照来源**：`pcl-next/Pages/PageSetup/PageSetupUI.xaml`（全文 346 行）+ `PageSetupUI.xaml.vb`
（L3–L40 加载/刷新、L47–L49 标题栏分支、L309–L343 主页分支、L388–L411 自定义主题滑条、L513–L587 功能隐藏联动）；
默认值取自 `pcl-next/Pages/PageSetup/Settings.vb` L139–L180；
主题解锁为空实现（`Modules/ModSecret.vb` L136–L143：`ThemeCheckAll` 空体、`ThemeCheckOne` 恒 True、`ThemeUnlock` 恒 False），
`ChannelIsOfficial` 在 Dev 渠道为 False（`ModBuildChannel.vb` L109–L113）→ 赞助遮罩 `PanLauncherHide` 保持隐藏。

**Linux 实现**：`Views/PageSetupUIView.axaml(.cs)`；
`MainWindow.axaml.cs`（`ShowSetupPage`：新增 `2 => PageSetupUIView`，个性化页接入设置栈子页切换）。

| 卡片 / 区域 | Windows 取值 | Linux 实现 | 结论 |
|---|---|---|---|
| 基础卡 | 内容 `Margin 25,40,25,17`；不透明度行 Grid 五列（Auto[Name1]/1*/30/Auto/1*）、滑块跨 5 列；`PanLauncherTheme Height 90`、6 列 × 3 行、15 项主题；勾选框 `Margin -1,0,0,1` 高 22 | 同；不透明度 `Value 600 / MaxValue 600 / ValueByKey 10`；龙猫蓝 `Checked=True`；四条自定义滑条按原 XAML 隐藏；赞助遮罩按原布局保留并隐藏 | 一致（R30 / R27） |
| 背景图片卡 | `Margin 25,40,25,20`；自适应 Grid 高 28 / 不透明度 22 / 模糊 22；13 项下拉；彩色背景勾选；三按钮 `Height 35`、第三个红色隐藏 | 同；`Value 1000 / 0`、下拉 `SelectedIndex=0`（自动）、彩色勾选；三栏与清空按钮隐藏 | 一致（R31 / R29） |
| 背景音乐卡 | `Margin 25,42,25,20`；音量滑条 22 高、四个选项高 22、清空按钮红色隐藏 | 同；音量 `Value 500`；随机/自动勾选，启动/退出两项不勾 | 一致（R31） |
| 标题栏卡 | `Margin 25,40,25,15`；四选一（1*/1*/1*/1*/0.2*）、`Margin -1,0,0,0`、高 22；居左勾选 / 文本框 / 图片按钮组均隐藏 | 同；「默认」`Checked=True`（`UiLogoType=1`） | 一致（R29） |
| 主页卡 | `Margin 25,40,25,15`；四选一；蓝色提示 + 黄色可关闭提示；本地四按钮 `WrapPanel`、联网输入框、预设下拉均隐藏 | 同；「空白」`Checked=True`（`UiCustomType=0`）、两提示 `IsVisible=False`、预设下拉 21 项 | 一致（R29） |
| 功能隐藏卡 | `Margin 25,38,15,15`；说明文字换行；7 列（Auto/18/0.8*/0.9*/0.8*/0.8*/1.0*）、6 行（4/30/30/30/30/Auto）；4 行 × 5–6 个复选框 | 同；全部不勾；「联机」「设置 子页面-联机」`IsEnabled=False`；ToolTip 逐字保留（含 `ToolTip.ShowOnDisabled`） | 一致（R32） |

**验证**：`dotnet build PCL.Desktop.Linux/PCL.Desktop.Linux.csproj -c Release -r linux-x64` → **0 警告 / 0 错误**
（产物 `bin-net10/Dev/2.100.0.4-0.93/linux-x64/`）。
**未验证项**：主题区 15 项在 6 列 × 3 行内的实际排布与解锁遮罩层叠、下拉弹出层、复选框三态、卡片滚动 —— 需统一验收目视（V13）。

### 13.14 设置栈「其他」页（LU6.3，2026-11-14，只做编译验证、不弹窗）

**对照来源**：`pcl-next/Pages/PageSetup/PageSetupSystem.xaml`（全文 205 行）+ `PageSetupSystem.xaml.vb`
（L4–L26 加载/刷新、L42–L58 滑块提示文案、L59–L65 线程数警告、L69–L79 识别码/土豆码、L82–L101 下拉末项确认、L124–L129 导出/导入占位）；
默认值取自 `pcl-next/Pages/PageSetup/Settings.vb` L43–L49、L125–L136；
`ChannelIsOfficial=False`（Dev 渠道）→ `PanDonate` 可见、`ItemSystemUpdateDownload` 文案带「（更新非正式版前需要先输入土豆码）」后缀。

**Linux 实现**：`Views/PageSetupSystemView.axaml(.cs)`；
`MainWindow.axaml.cs`（`ShowSetupPage`：新增 `3 => PageSetupSystemView`，设置栈四个子页全部接入）。

| 卡片 / 区域 | Windows 取值 | Linux 实现 | 结论 |
|---|---|---|---|
| 下载卡 | 内容 `Margin 25,37,25,15`；Grid 两列（Auto[Name]/*）、七行（28/7/28/7/27/27/Auto）；两个下拉 + 两条滑块 + 目标文件夹双栏说明 | 同；下拉 `SelectedIndex=1`、线程滑块 `MaxValue 255 / Value 63`、限速滑块 `MaxValue 42 / Value 42`；说明文字 `Opacity 0.5`、两行原文 | 一致（R33 / R27） |
| 社区资源卡 | 内容 `Margin 25,37,25,12`；两列、六行（28/7/28/7/28/7）；三个下拉 | 同；来源 `SelectedIndex=2`、文件名格式 `1`、Mod 管理样式 `0`；ToolTip 逐字保留 | 一致（R33） |
| 辅助功能卡 | `Margin 25,40,25,17`；三列（Auto/160/Auto）、三行（22/8/22）；两个更新提示复选框 + 自动中文 | 同；`ToolUpdateRelease/Snapshot=False` 不勾、`ToolHelpChinese=True` 勾选；测试版复选框 `Height=22` | 一致（R33） |
| 启动器卡 | `Margin 25,40,25,20`；两列（Auto[Name]/1*）、九行（28/9/28/9/28/5/Auto/Auto/Auto）；三个下拉/输入 + 两排按钮（`Height 35`、`Margin 0,12,0,0`） | 同；两个下拉 `SelectedIndex=0`、缓存框空 + `HintText「默认」`；按钮 `MinWidth 140 / Padding 13,0 / 间距 20`；`PanDonate` 两按钮按 Dev 渠道可见 | 一致（R33 / R34 / R35） |
| 全局设置卡（被注释） | 原 XAML L165–L180 整卡被注释，不参与渲染 | 不移植 | 一致 |
| 调试选项卡 | `CanSwap=True IsSwapped=True`（折叠高 40）；动画滑条 `MaxValue 30` + 三个复选框（1.5*/1*/1） | 同；`SystemDebugAnim=9`、三项不勾；ToolTip 逐字保留 | 一致（R5 / R33） |

**验证**：`dotnet build PCL.Desktop.Linux/PCL.Desktop.Linux.csproj -c Release -r linux-x64` → **0 警告 / 0 错误**
（产物 `bin-net10/Dev/2.100.0.4-0.94/linux-x64/`）。
**未验证项**：五个下拉弹出层、调试卡折叠/展开、线程数滑块拖动与数值提示、卡片滚动 —— 需统一验收目视（V14）。

### 13.15 下载栈（LU7，2026-11-14，只做编译验证、不弹窗）

**对照来源**：
`pcl-next/Pages/PageDownload/PageDownloadLeft.xaml`（58 行）+ `.xaml.vb`（L4–L28 页面切换、L95–L152 刷新、L155–L158 返回）；
`PageDownloadInstall.xaml`（359 行）+ `.xaml.vb`（L3–L42 加载器初始化、L52–L106 选择页切换、L446–L546 版本列表生成）；
`Resource/PageResource.xaml`（96 行）+ `.xaml.vb`（L80–L122 加载与筛选、L143–L164 来源降级）及五个包装页
`PageDownloadMod/Pack/DataPack/ResourcePack/Shader.xaml`。

**Linux 实现**：`Views/PageDownloadLeftView.axaml(.cs)`、`Views/PageDownloadInstallView.axaml(.cs)`、
`Views/PageResourceView.axaml(.cs)`（五个包装页共用，`ResourcePageKind` 区分种类）；
`MainWindow.axaml.cs`（`ShowStack(1)` 接入左导航 + 原版游戏页；`ShowDownloadPage` 按 0/2/3/4/5/6 切换子页）。

| 区域 | Windows 取值 | Linux 实现 | 结论 |
|---|---|---|---|
| 下载左导航 | StackPanel `Margin 0,12,0,0`；原版游戏（0，选中）+「社区资源」小标题 + Mod(2)/整合包(3)/数据包(4)/资源包(5)/光影包(6)；条目高 36、`MinPaddingRight 35`、右侧刷新按钮 | 同；`ScrollViewer Classes=fullmargin`、`AnimatedControl="PanItem"`；复选框互斥选中并回传 `Index`；刷新按钮空操作 | 一致（R36 / R20） |
| 原版游戏页 · 加载态 | PanLoad 居中卡片（8 个 MyLoading，仅 Minecraft 带进度）+ PanAllBack 隐藏 | 同；`LoadMinecraft` 显示「正在加载」+ 进度，其余 7 个隐藏 | 一致（R37） |
| 原版游戏页 · 版本列表 | `PanInner Margin 25,10,25,25`；`PanMinecraft` 运行期注入版本卡（宽卡片、标题、各 Mod 加载器折叠卡） | 容器与 `Margin 0,0,0,-10` 保留，卡片静态为空 | 一致（R37） |
| 原版游戏页 · 安装配置 | `PanSelect` 默认 Collapsed；名称卡 `MinHeight 62` + 5 条提示 + 8 张 40 高折叠卡（Minecraft 40、Forge/NeoForge/Fabric/FabricAPI/OptiFine/OptiFabric/LiteLoader 折叠）+「开始下载」 | 同结构全部保留（含 `Margin 132,11,15,0` 信息行、`Margin 20,40,18,15` 内容区、取消/撤销按钮 30×30）；位图以同尺寸透明占位 | 一致（R37 / R38 / R39） |
| 资源搜索页 · 搜索卡 | `Margin 25,40,25,15`；6 列 × 3 行；名称 / 来源（3 项）/ 版本（13 项）/ 加载器（隐藏）/ 类型；按钮行三枚 `MinWidth 140` | 同；种类标题「搜索 Mod / 整合包 / 数据包 / 资源包 / 光影包」与类型下拉按原五页逐项生成；「安装已有整合包」仅整合包可见 | 一致（R40 / R41） |
| 资源搜索页 · 结果区 | 两条提示条（默认隐藏）+ 结果卡（运行期填充）+ 分页卡（首页/前页禁用、页码 1、后页） | 同；结果为空、分页禁用、提示条隐藏 | 一致（R40） |
| 资源种类 · 下拉项 | Mod 27 项（全部带 15px 缩进）/ 整合包 19 项 / 数据包 17 项 / 资源包 33 项（3 组禁用标题）/ 光影包 21 项（4 组禁用标题） | 逐项照抄（Content / Tag / 缩进 / 禁用 / ToolTip「由 FTB 官方发布」「作为资源包加载，可以直接在原版游戏里使用。」） | 一致（R40） |

**验证**：`dotnet build PCL.Desktop.Linux/PCL.Desktop.Linux.csproj -c Release -r linux-x64` → **0 警告 / 0 错误**
（产物 `bin-net10/Dev/2.100.0.4-0.95/linux-x64/`）。
**未验证项**：左导航 7 项排布与刷新按钮悬停、加载卡片的进度动画、六个下拉弹出层、五个资源子页的切换与长下拉（21–33 项）滚动 —— 需统一验收目视（V15）。

### 13.16 更多栈（LU8，2026-11-14，只做编译验证、不弹窗）

**对照来源**：
`pcl-next/Pages/PageOther/PageOtherLeft.xaml`（23 行）+ `.xaml.vb`（L31–L110 页面切换、L112–L125 刷新、L128–L157 反馈 / 投票）；
`PageOtherTest.xaml`（12 行）+ `.xaml.vb`（全部方法返回同一提示）；
`PageOtherHelp.xaml`（23 行）+ `.xaml.vb`（L11–L13 加载器初始化、L20–L67 分类卡生成、L118–L174 搜索切换）；
`PageOtherAbout.xaml`（335 行）+ `.xaml.vb`（L3–L24 版本信息与 Dev 渠道分支）；
`Controls/MySearchBox.xaml(.vb)`（搜索框结构与清空按钮）；
默认值取自 `pcl-next/Pages/PageSetup/Settings.vb` L176–L180（`UiHiddenOtherHelp/Feedback/Vote/About/Test` 全部 `False` → 五项可见、默认子页为帮助）。

**Linux 实现**：`Views/PageOtherLeftView.axaml(.cs)`、`Views/PageOtherHelpView.axaml(.cs)`、
`Views/PageOtherAboutView.axaml(.cs)`、`Views/PageOtherTestView.axaml(.cs)`；
`MainWindow.axaml.cs`（`ShowStack(4)` 接入左导航 + 默认帮助页；`ShowOtherPage` 按 0/1/2 切换子页）。

| 区域 | Windows 取值 | Linux 实现 | 结论 |
|---|---|---|---|
| 更多左导航 | StackPanel `Margin 0,12,0,0`；五项高 36、`Type=RadioBox`、`MinPaddingRight 35`、`IsScaleAnimationEnabled=False`；Logo 缩放 0.97 / 0.98 / 0.97 / 0.9 / 0.92；帮助项 `Checked=True` 且右侧带「刷新」按钮（25×25、LogoScale 0.85） | 同；`Tag` 换成 `Index`；刷新按钮空操作；反馈 / 投票 `Click` 时 `Handled=True` 阻止选中 | 一致（R46） |
| 默认子页 | 无隐藏配置时 = 帮助（`PageOtherLeft.xaml.vb` L37–L47） | 同 | 一致 |
| 百宝箱页 | 居中 TextBlock：15 号、高 30、文案「为便于维护，开源内容中不包含百宝箱功能……」 | 同 | 一致 |
| 帮助 · 搜索框 | `MySearchBox`（`MyCard Height 40` + 无背景 `MyTextBox Padding 32,0,40,0 / MaxLength 50 / 13.5 号` + 14×14 放大镜 + 24×24 清空按钮）`Margin 0,0,0,15` | 同；清空按钮按 `MySearchBox.vb` L27–L40 联动（空文本隐藏、点击清空并聚焦），Loaded 时聚焦 | 一致（R43） |
| 帮助 · 分类列表 | `PanList` 运行期生成分类卡（指南置顶并展开、其余默认折叠，`PageOtherHelp.xaml.vb` L20–L67） | `PanList` 为空（帮助加载器未移植） | R43 |
| 帮助 · 搜索结果 | `PanSearch` 卡（`Title` 搜索结果 / 无搜索结果、`Margin 20,40,18,18`），默认隐藏 | 同结构，保持隐藏 | 一致（R43） |
| 帮助 · 加载卡 | 居中 `MyCard` + `MyLoading Text="正在加载帮助列表" Margin 20,20,20,17` | 同结构，按「已完成」态隐藏 | R43 |
| 关于 · 卡片 | 七张卡：关于 PCL Next / 当前版本（3 行 45）/ 项目进度 / 作者与贡献（2 行 45）/ 特别鸣谢（10 行 45）/ 赞助者 / 法律信息（折叠）/ 许可与版权声明（折叠、22 行） | 全文文案、行列高、`Margin` 与按钮尺寸照抄；头像以 34×34 透明占位；动作按钮空操作 | 一致（R44 / R45） |
| 关于 · Dev 渠道 | `ChannelIsInternal=True` → 「赞助作者」隐藏、「生成土豆码」显示（`PageOtherAbout.xaml.vb` L18–L23） | 同 | 一致（R45） |
| 折叠卡 | 法律信息 / 许可与版权声明 `CanSwap=True IsSwapped=True`（折叠高 40） | 同（MyCard 折叠） | R5 |

**验证**：`dotnet build PCL.Desktop.Linux/PCL.Desktop.Linux.csproj -c Release -r linux-x64` → **0 警告 / 0 错误**
（产物 `bin-net10/Dev/2.100.0.4-0.96/linux-x64/`）。
**未验证项**：左导航五项逐个进入动画、搜索框清空按钮淡入淡出与聚焦、关于页长页滚动、两张折叠卡点击展开/箭头旋转、头像占位与文本列对齐 —— 需统一验收目视（V16）。

### 13.17 实例设置栈（LU9 首批，2026-11-14，只做编译验证、不弹窗）

**对照来源**：
`pcl-next/Pages/PageInstance/PageInstanceLeft.xaml`（43 行）+ `.xaml.vb`（L13–L21 Mod/ModDisabled 分支、L33–L37 页面切换、L105–L135 刷新与初始化）；
`PageInstanceOverall.xaml`（88 行）+ `.xaml.vb`（L23–L50 头卡与下拉、L52–L175 个性化、L181–L207 文件夹、L214–L301 管理）；
`PageInstanceSetup.xaml`（276 行）+ `.xaml.vb`（L7 Loaded、L1–L570 绑定与服务器 / 内存分支）；
`PageInstanceMod.xaml`（79 行）+ `.xaml.vb`（L56 加载器、L80–L84 空态分支、L176–L180 筛选可见性）；
`PageInstanceModDisabled.xaml`（30 行）+ `.xaml.vb`（L10–L15 两个跳转按钮）。

**Linux 实现**：`Views/PageInstanceLeftView.axaml(.cs)`、`PageInstanceOverallView`、`PageInstanceSetupView`、
`PageInstanceModView`、`PageInstanceModDisabledView`；`MainWindow.axaml.cs` 新增次级栈 6（`StackInstanceSetup`）
与 `ShowInstancePage`；ModDisabled 的两个按钮接回下载栈 / 版本选择栈。

| 区域 | Windows 取值 | Linux 实现 | 结论 |
|---|---|---|---|
| 左导航 | StackPanel `Margin 0,12,0,0`；六项高 36、`Type=RadioBox`、`MinPaddingRight 35`、`IsScaleAnimationEnabled=False`；概览 `Checked=True`；设置 / 修改 / Mod / 导出带 25×25 图标按钮（初始化 / 刷新） | 同；`Tag` 换 `Index`；无实例时 Mod 隐藏、ModDisabled 显示（vb L13–L21） | 一致（R47） |
| 概览 · 头卡 | `MyCard Margin 0,15` + `Grid Margin 10,7`，运行期注入 `Instance.ToListItem()` | 同结构，头卡为空（`MinHeight 42` 保留行高） | R47 |
| 概览 · 个性化 | 图标 / 分类两行 28+9+28；图标下拉 14 项（自动 / 自定义 / 12 种方块）、分类下拉 6 项；三个 `MinWidth 140` 按钮 | 同；下拉按首项默认选中 | 一致（R47） |
| 概览 · 快捷方式 / 高级管理 | WrapPanel `Margin 25,30,25,20` 四个高 35 / MinWidth 140 按钮；管理卡三个按钮（删除为 `ColorType=Red`） | 同；Mod 文件夹按钮按 `Instance.Modable=False` 隐藏 | 一致（R47） |
| 设置 · 启动选项 | 行高 28/9/28/9/28/9/28/Auto；版本隔离 2 项、Java 4 项；Java 区间框宽 200 `Margin 7,0,0,0` | 同；`SettingService` 与校验规则未接（输入框只留 Hint / MaxLength） | 一致（R48） |
| 设置 · 内存分配 | 单选「跟随全局设置」默认勾选、`SliderRamCustom` `MaxValue 49 / Value 13 / IsEnabled False`；内存条列宽 4.7*/2.5*/0.7*、中间 4 高、三段配色 `ColorObject3/2`、`ColorBrush3@0.5`、`ColorBrush6@0.7`；读数 4.7 / 2.5 / 7.9 GB | 同 | 一致（R48） |
| 设置 · 服务器 | 登录方式 5 项（首项选中）；服务器 ID / 认证服务器 / 注册链接 / 名称四个字段默认隐藏；「自动进入服务器」；服主后台与 LittleSkin 两个高 35 按钮 | 同；按钮空操作 | 一致（R48 / R47） |
| 设置 · 高级选项 | `CanSwap=True IsSwapped=True`（折叠 40）；JVM 框 `Height 100 / Padding 0,5 / AcceptsReturn`；GC 下拉 6 项；四个 `MyCheckBox Height 28` | 同；折叠交互由 MyCard 提供（R5） | 一致（R48） |
| Mod · 空态 | 加载完成后无 Mod：`PanAllBack` 隐藏、`PanEmpty` 居中（`Margin 40`），标题 19 号、2 高分隔线、三按钮 `Margin 0,0,17,0` | 同；`PanLoad` 隐藏 | 一致（R47） |
| Mod · 列表态 | 搜索框 `Margin 0,15` + 管理卡四个 `MinWidth 110` 按钮 + 筛选卡（全部 / 已启用 / 已禁用 / 可更新）+ `PanList` | 结构照抄，列表态默认隐藏 | 一致（R49） |
| ModDisabled | 居中卡：标题 19 号、2 高线、两行说明、两个 `MinWidth 140` 按钮 `Margin 10,10,10,0` | 同；两个按钮已接跳转 | 一致 |
| 修改 / 导出 | `PageDownloadModify`（360 行）/ `PageInstanceExport`（314 行） | 未移植，切换后右页为空 | R50 |

**验证**：`dotnet build PCL.Desktop.Linux/PCL.Desktop.Linux.csproj -c Release -r linux-x64` → **0 警告 / 0 错误**
（产物 `bin-net10/Dev/2.100.0.4-0.97/linux-x64/`）。
**未验证项**：左导航六项切换与 Mod/ModDisabled 二选一、概览长页滚动、设置页折叠卡展开、内存条三段比例、ModDisabled 两个跳转按钮 —— 需统一验收目视（V17）。

### 13.18 实例「导出」与「修改」页（LU9 收尾，2026-11-14，只做编译验证、不弹窗）

**对照来源**：
`pcl-next/Pages/PageInstance/PageInstanceExport.xaml`（314 行）+ `.xaml.vb`（L32–L40 初始态、L421 导出、L740–L751 Java 提示）；
`pcl-next/Pages/PageDownload/PageDownloadModify.vb`（360 行，`PageDownloadModify : PageDownloadInstall`，无独立 XAML）；
`pcl-next/Controls/MyIconTextButton.xaml(.vb)`（「重置」胶囊）；
`PageDownloadInstall.xaml`（L359 行中的 `BtnStart` / `LabSelectName` / `Lab*Old`）。

**Linux 实现**：`Views/PageInstanceExportView.axaml(.cs)`；「修改」页复用 `PageDownloadInstallView(isModify: true)`；
控件层新增 `MyIconTextButton`（`MyControls.cs` + `PclControls.axaml`）与 `MyCheckBox.Description`（灰色补充说明，
对应 Windows 导出页 `Inlines` 里的 `ColorGray5` Run）；`MainWindow.axaml.cs` 的 `ShowInstancePage` 补 2 / 5 分支。

| 区域 | Windows 取值 | Linux 实现 | 结论 |
|---|---|---|---|
| 导出 · 整合包信息 | 无标题卡，`Grid Margin 22,15,25,15`；列 Auto / 1* / Auto / 0.3*，行高 28；「整合包名称」「整合包版本」两个 `MaxLength 100` 输入框 | 同；无实例时无 HintText | 一致（R47） |
| 导出 · 导出内容 | `CardOptions MinHeight 39`；右上「重置」胶囊（高 24、`Margin 0,0,5,0`、`Opacity 0.5`、不可命中）；`PanOptions Margin 25,38,15,15`；所有复选框高 26、子项缩进 30 | 同；复选框标题 + 灰色描述改用新增 `Description`；「游戏本体」禁用且勾选 | 一致（R51） |
| 导出 · 子项显隐 | `CheckOptionsMod` / `ResourcePacks` / `ShaderPacks` / `Saves` / `Pcl` / `CheckAdvancedInclude` 的 `Checked → Visibility` 绑定 | 代码后置监听 `CheckedProperty` 同步 `IsVisible` | 一致 |
| 导出 · 默认勾选 | 本体 / 本体设置 / OptiFine / Mod / 整合包重要数据 / Mod 设置 / TaCZ / 沉浸画 / 资源包 / 光影包 / 光影设置 / 协议 / Java / 启动器 / 个性化 = True；个人信息 / 地图 / JEI / EMI / 帕秋莉 / 截图 / 结构 / 回放 / 存档 / 服务器列表 = False | 同 | 一致 |
| 导出 · 高级选项 | `CanSwap=True IsSwapped=True`（折叠 40）；两段 Yellow 提示（打包资源 / .mrpack）、一段 Blue 可关闭提示；读取配置（Highlight）/ 保存配置 / 制作指南三个 `MinWidth 140` 按钮 | 同；按钮空操作 | 一致（R51 / R5） |
| 导出 · 底部按钮 | `MyExtraTextButton HorizontalAlignment=Center VerticalAlignment=Bottom`，Text 开始导出、LogoScale 1.1 | 同；空操作 | 一致（R51） |
| 修改 | 继承安装页：加载态相同，选择页把 `BtnStart` 改「开始修改」并显示原名与旧版本信息 | 复用安装页视图，构造函数设置「开始修改」；加载态一致 | 一致（R52） |
| MyIconTextButton | 圆角 13.5、高 27、半透明底；图标上限 16 `Margin 12,0,0,0`、文字 `Margin 7,0,12,1`；Black 常 `ColorBrush1`、Highlight 常 `ColorBrush3`；悬停底 `ColorBrushBg1` + 色 `ColorBrush3`、按下底 `ColorBrush6`、禁用 `ColorBrushGray5` | 同（颜色过渡即时切换，R10） | 一致 |

**验证**：`dotnet build PCL.Desktop.Linux/PCL.Desktop.Linux.csproj -c Release -r linux-x64` → **0 警告 / 0 错误**
（产物 `bin-net10/Dev/2.100.0.4-0.98/linux-x64/`）。
**未验证项**：导出页长页滚动、复选框子项显隐、折叠卡展开、`Description` 灰色说明与标题的间距 —— 需统一验收目视（V18）。

## 14. 待统一验收清单（UI 全部成型后一次性执行）

| 编号 | 待验收项 | 为什么必须靠运行判断 | 当前已知线索 |
|---|---|---|---|
| **V1** | `MyComboBox` 收起态是否渲染（R13） | 编译期 0 报错，但实跑时「模式」行右侧完全空白 | **已修 / 待统一验收确认**：① 补 `StyleKeyOverride` 让控件可见；② 改自绘 `ControlTheme`（照抄 Application.xaml L336–L469），已不再依赖 Fluent；仍需目视确认收起态边框/文案/箭头 |
| **V7** | 自绘 ComboBox 模板的运行期契约（本轮新落） | 模板部件名/双向绑定只在运行时生效，编译期无法验证 | 需目视：点击任意位置是否展开（`ToggleButton` 热区）、弹层宽度是否等于控件宽、箭头是否 200ms 旋转 180°、`ItemsPresenter` 能否正常生成选项、弹层点击外部能否收起 |
| **V2** | 卡片底色/阴影可见性（R14） | 取决于窗口与实例页底色，静态看不出 | **已修 / 待统一验收确认**：宿主窗口底色改为 `ColorBrush1 #343d4a`（照抄 Windows 端）；仍需目视确认卡底与 7% 阴影对比度 |
| **V3** | I（交互路径）：下拉展开/选项切换、按钮 hover 与按压、输入框聚焦与 IME、提示条关闭 | 只有运行才能触发状态机 | 需在稳定 WSLg 会话中一次性走查 |
| **V4** | E（异常与空态）：`PageSetupLink` 的空列表/无网络提示、`MyHint` 三主题、`MyLoading` 错误态 | 需要构造异常数据 | 依赖实例页外壳起来后才有真实空态 |
| **V5** | T（状态与过渡）时序核对（R10/R11/R12） | 动画与主题色映射需目视 | 已按注释取值，未目视验证 |
| **V6** | 字体与 CJK 渲染（R1） | 取决于目标机字体 | 本环境装 `fonts-noto-cjk` 后中文正常；最终仍需内嵌 `PCL English` + CJK 回退 |
| **V8** | 无边框窗口契约（R2 / R19） | 拖拽、缩放、圆角裁剪都只在真实窗口后端生效，编译期无法判断 | 需目视：标题栏空白处拖动整窗、8 个热区（边 8px / 角 13px）各向缩放、`BorderForm` 圆角 6 裁剪内容、`PanBack` 起手 −4°/Y60 归零动画 |
| **V9** | 标题栏外观与交互（R17 / R18 / R20） | 渐变由 HSL 运行期算出、状态色靠伪类，静态看不出来 | 需目视：三段渐变方向与明暗、`PCL Next` 文字位置（18px 左缩进）、5 个 Tab 常/悬停/按下/选中三态配色与单选互斥、关闭/最小化、点击其他栈后宿主置空且不报错 |
| **V10** | 启动栈 / 版本选择栈（13.10） | 空态居中、按钮三态、页面进入动画、右页滚动、`ScrollViewerFullMargin` 滚动条 2,1,1,1 边距均需运行观察 | 需目视：首页为「启动」且标题栏「启动」选中、启动左页 300 宽（按钮 54 / 35 高）、「版本选择」点开进入 250–300 宽左列 + 居中空态卡片、「下载游戏」可点、右页提示条与日志卡、`PanLaunching` 保持隐藏 |
| **V11** | 设置页前置控件（13.11） | 圆点/圆环尺寸、滑块宽度算法、拖动与键盘步进、胶囊按钮配色均需运行观察 | 需目视：`MyRadioBox` 18 圆环 + 9 圆点（选中 `ColorBrush2`、悬停 `ColorBrush3`）、`MySlider` 底轨 1px/前轨 2px/滑块 10、拖动与 ←/→ 生效、`MyExtraTextButton` 52 高 + 内层下移 10 + 悬停 `ColorBrush4` |
| **V12** | 设置·启动页（13.12） | 折叠交互、下拉弹出层、长文本滚动、内存条比例、卡片阴影均需运行观察 | 需目视：标题栏进「设置」后左导航选中「启动」且右页出现五张卡片；「离线皮肤」「高级选项」初始为 40 高折叠条、点击标题区展开/收起且箭头旋转；启动选项 5 行 28 高、内存条三段比例、JVM 框 100 高、底部胶囊按钮居中 |
| **V13** | 设置·个性化页（13.13） | 主题区 15 项排布、解锁遮罩层叠、下拉弹出层、复选框禁用态、整页滚动均需运行观察 | 需目视：左导航点「个性化」后右页出现六张卡片；基础卡标题下不透明度滑条满格（600/600）、龙猫蓝选中、其余主题灰色且玄素黑双层叠字；标题栏卡「默认」选中；主页卡「空白」选中且无提示条/按钮；功能隐藏卡 22 行复选框仅「联机」两项禁用；背景图片/音乐卡仅标题、无隐藏控件露出 |
| **V14** | 设置·其他页（13.14） | 五个下拉弹出层、调试卡折叠、滑块拖动、按钮排布均需运行观察 | 需目视：左导航点「其他」后右页出现「下载 / 社区资源 / 辅助功能 / 启动器」四张卡片 + 40 高「调试选项」折叠条；下载卡两条滑块分别停在 63/255 与 42/42；缓存框显示灰色「默认」占位；启动器卡两排按钮（3 + 2）左对齐、间距 20；展开调试卡后动画滑条停在 9/30；`PanDonate` 在 Dev 渠道可见 |
| **V15** | 下载栈（13.15） | 左导航选中态、加载卡片、六个下拉弹出层、五个资源子页切换均需运行观察 | 需目视：标题栏进「下载」后左导航 7 项（原版游戏选中、社区资源小标题灰字）、右页居中「正在加载」卡片带进度环；点「Mod」等五项后右页出现同名搜索卡（标题「搜索 Mod」等）、类型下拉分别为 27/19/17/33/21 项；资源页结果区为空、分页卡居中且首页/前页灰显；点回「原版游戏」恢复加载卡片 |
| **V16** | 更多栈（13.16） | 左导航五项排布与切换、搜索框清空按钮、关于页长页滚动与折叠卡均需运行观察 | 需目视：标题栏进「更多」后左导航五项（帮助选中，右侧刷新按钮悬停淡入）、右页顶部 40 高搜索框（放大镜在左、输入后右侧 × 出现、点击清空并聚焦）、下方无分类卡；点「关于与鸣谢」出现七张卡片（`法律信息` / `许可与版权声明` 为 40 高折叠条，点击展开箭头旋转）、头像列为空白占位但文本列对齐；点「百宝箱」显示居中提示；点「反馈」/「新功能投票」不切页且条目不保持选中 |
| **V17** | 实例设置栈（13.17） | 栈入口、左导航六项、四张页面、折叠卡与跳转按钮均需运行观察 | 需目视：进入实例设置栈后左导航显示五项（概览选中；无实例时 Mod 项不出现、ModDisabled 项出现）、右侧概览页头卡为空白行、个性化（14 项图标下拉 / 6 项分类下拉）与快捷方式（3 个按钮，无 Mod 文件夹）及高级管理三按钮排布正常；点「设置」出现四张卡（高级选项为 40 高折叠条、内存条三段按 4.7/2.5/0.7 铺满、读数 4.7 / 2.5 / 7.9 GB）；点「Mod 管理」显示居中「尚未安装 Mod」空态卡；ModDisabled 页两个按钮分别跳下载栈与版本选择栈 |
| **V18** | 实例导出 / 修改页（13.18） | 导出页长页滚动、复选框子项显隐、折叠卡与灰色描述间距需运行观察 | 需目视：进「导出」后自上而下为「整合包信息」卡（名称 / 版本两个输入框）、「导出内容」卡（右上浅色「重置」胶囊；第一项「游戏本体」禁用且勾选；取消勾选「Mod」后其下 9 个子项整组消失，重新勾选恢复；取消「单人游戏存档」不影响其他组）、「高级选项」40 高折叠条（展开后两段黄色提示、读取 / 保存配置 / 制作指南三按钮）与底部居中「开始导出」；灰色描述与标题同基线、间距约 9px；进「修改」显示与「原版游戏」相同的居中加载卡 |
