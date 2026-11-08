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
| **R5** | `MyCard` 折叠（`CanSwap` / `IsSwapped` 箭头 + 高度动画）尚未接入 | 折叠过程 | 折叠前后两种静态布局必须都能还原 |
| **R6** | `MyComboBox` / `MyTextBox` 模板细节（弹出层、滚动条、IME、选中样式）尚未逐像素核对 | 弹出层绘制 | 收起态边框、圆角、内边距、字号必须一致 |
| **R7** | Windows 系统集成（注册表、托盘、跳转列表、通知、资源管理器定位） | 全部（换 XDG / DBus / 无等价物时不实现） | 页面内的文案与按钮布局不变；不做的集成须在页面里显式说明 |
| **R8** | 音频：**不做**（用户裁决，登记项关闭） | 不适用 | 无需保留任何音频 UI：Windows 端 `ModMusic.vb` 无调用点（全库仅 1 处命中 = 自身声明），界面上不存在播放条/音量控件，不做不产生布局差异 |
| **R9** | DPI / 多显示器缩放模型差异 | 缩放行为 | 100% 缩放下的尺寸必须一致 |
| **R10** | 动画引擎：WPF 自研 `Ani` 逐帧驱动 → Avalonia `Transitions` / `Animation` 近似 | 缓动曲线、逐帧节奏 | 时长数值照抄（勾选 150ms、悬停 100ms 进 / 200ms 出、按压 100ms）；`MyLoading` 镐子摆动改为 ±20°、1.4s 无限往返 |
| **R11** | `MyHint` 关闭：Linux 暂无 `Settings`，关闭只隐藏；未做 200ms 缩放/淡出 + 150ms 收高，也未做「不再提示」记忆 | 关闭动画与持久化 | 关闭前后两种静态布局不变；`CanClose` 按钮尺寸 20×20 与 ToolTip 文案保留 |
| **R12** | `MyIconButton` 主题色映射：WPF 源码散落在动画分支中，本轮取 `Color→#1370f3` / `White→白` / `Black→#343d4a` / `Red→#ff4c4c` | 图标颜色 | 图标尺寸（`Margin 5`、`Stretch Uniform`）、圆角 1000、按下 0.8 缩放不变 |
| **R13** | 运行期缺陷：`MyComboBox` 在 WSLg 下**未渲染**（收起态无边框、无选中文案、无箭头），编译期无报错 | 待排查前不视为已还原 | 不因此改动页面布局；必须查明是模板问题还是渲染后端问题 |
| **R14** | 窗口/页面底色：当前 `Window` 背景为纯白，`MyCard` 的 `#F5FFFFFF` 卡底与 `0 0 6 #12343d4a` 阴影在白底上几乎不可见 | 待对照实例页底色 | 卡片圆角 5、`Margin -3,-4,-3,-3`、标题 15,12 等尺寸不得改；只改底色使卡底/阴影可见 |

---

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
| `Controls/MyExtraTextButton.xaml` | P1 | 文字按钮字号与下划线交互 | ⬜ |
| `Controls/MyHint.xaml` | P0 | 提示条背景/边框/图标与字号 | ✅（13.3，R11） |
| `Controls/MyIconButton.xaml` | P0 | 矢量图标 + 圆角 + hover | ✅（13.3，R12） |
| `Controls/MyIconTextButton.xaml` | P1 | 图标 + 文字间距、字号 | ⬜ |
| `Controls/MyListItem.xaml` | P0 | 列表项高度、左图标、副标题灰阶、选中底色 | ✅（13.3） |
| `Controls/MyLoading.xaml` | P0 | 加载图形尺寸与旋转时序 | ✅（13.3，R10） |
| `Controls/MyMsg/MyMsgInput.xaml` | P1 | 弹窗尺寸、按钮排布 | ⬜ |
| `Controls/MyMsg/MyMsgSelect.xaml` | P1 | 选项列表与按钮排布 | ⬜ |
| `Controls/MyMsg/MyMsgText.xaml` | P1 | 文本弹窗内边距与按钮 | ⬜ |
| `Controls/MyRadioBox.xaml` | P1 | 圆点尺寸、选中色 | ⬜ |
| `Controls/MyRadioButton.xaml` | P1 | 单选按钮间距与字号 | ⬜ |
| `Controls/MySearchBox.xaml` | P1 | 搜索图标位置、占位文案 | ⬜ |
| `Controls/MySlider.xaml` | P1 | 轨道高度、滑块尺寸、渐变 | ⬜ |
| `Controls/MySliderDot.xaml` | P1 | 圆点尺寸与选中态 | ⬜ |

> 注：`MyCard` 在 WPF 端无独立 `.xaml`（纯代码类），对照基准为 [MyCard.vb](file:///d:/bing/MC/pcl_x/PCL_NEXT_Linux/pcl-next/Controls/MyCard.vb)；`MyTextBox` / `MyComboBox` 样式取自 `Application.xaml`。

### 5.3 外壳与主区（3）

| 文件 | 优先级 | 特有检查点 | 状态 |
|---|---|---|---|
| `FormMain.xaml` | P0 | 自绘标题栏高度、最小化/最大化/关闭按钮尺寸与图标、无边框拖拽区、窗口圆角 | ⬜（R2） |
| `Pages/PageSelectLeft.xaml` | P0 | 左侧导航栏宽度、条目高度、分组标题字号 | ⬜ |
| `Pages/PageSelectRight.xaml` | P0 | 右侧页面容器（`MyPageRight`）留白与滚动 | ⬜ |

### 5.4 五大左侧导航（4，`PageSelectLeft` 除外的分区导航）

| 文件 | 优先级 | 特有检查点 | 状态 |
|---|---|---|---|
| `Pages/PageLaunch/PageLaunchLeft.xaml` | P0 | 启动区导航条目与折叠 | ⬜ |
| `Pages/PageInstance/PageInstanceLeft.xaml` | P1 | 实例列表为空时的空态 | ⬜（E） |
| `Pages/PageDownload/PageDownloadLeft.xaml` | P1 | 下载区导航条目 | ⬜ |
| `Pages/PageSetup/PageSetupLeft.xaml` | P1 | 设置区导航条目（含联机页入口） | ⬜ |
| `Pages/PageOther/PageOtherLeft.xaml` | P2 | 其他区导航条目 | ⬜ |

### 5.5 启动区页面（11）

| 文件 | 优先级 | 特有检查点 | 状态 |
|---|---|---|---|
| `Pages/PageLaunch/PageLaunchRight.xaml` | P0 | 版本卡片网格、卡片内边距、选中态 | ⬜ |
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
| `Pages/PageSetup/PageSetupLaunch.xaml` | P1 | 启动选项下拉与输入框网格、注册表相关项按 R7 处理 | ⬜ |
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

---

## 14. 待统一验收清单（UI 全部成型后一次性执行）

| 编号 | 待验收项 | 为什么必须靠运行判断 | 当前已知线索 |
|---|---|---|---|
| **V1** | `MyComboBox` 收起态是否渲染（R13） | 编译期 0 报错，但实跑时「模式」行右侧完全空白 | 同页 `MyTextBox` 1px 边框正常，怀疑模板层；需在统一测试时逐项压测 |
| **V2** | 卡片底色/阴影可见性（R14） | 取决于窗口与实例页底色，静态看不出 | 白底上 `#F5FFFFFF` 卡底与 7% 阴影几乎不可见 |
| **V3** | I（交互路径）：下拉展开/选项切换、按钮 hover 与按压、输入框聚焦与 IME、提示条关闭 | 只有运行才能触发状态机 | 需在稳定 WSLg 会话中一次性走查 |
| **V4** | E（异常与空态）：`PageSetupLink` 的空列表/无网络提示、`MyHint` 三主题、`MyLoading` 错误态 | 需要构造异常数据 | 依赖实例页外壳起来后才有真实空态 |
| **V5** | T（状态与过渡）时序核对（R10/R11/R12） | 动画与主题色映射需目视 | 已按注释取值，未目视验证 |
| **V6** | 字体与 CJK 渲染（R1） | 取决于目标机字体 | 本环境装 `fonts-noto-cjk` 后中文正常；最终仍需内嵌 `PCL English` + CJK 回退 |
