# 计划-深色主题重做（参照 PCL2-CE / OKLCh）

> 用户 2026-11-12 指示：现有深色模式不好看，参照 PCL2-CE 的深色模式重做一遍。
> 补充指示（同日）：在设置中增加「旧版 / 新版深色」切换按钮，便于对比与回退；
> 旧版原样保留重做前的 HSL 配色，新版为 OKLCh，默认新版。
> 范围裁决：本轮**只重做深色配色 + 增加配色版本开关**，模糊效果（CE `BlurBorder`）另立议题，不在本计划内。
> 落在 `PCL_NEXT_Windows`（`dev.6-Windows`）。本文件按 `AGENTS.md` §3.2 八节格式编写，
> 因涉及第三方开源实现参照，额外补「合规边界」章节。

## 1. 目标

- 深色调色板改用 **OKLab / OKLCh 感知均匀色彩空间**生成，替代现行 HSL 亮度阶梯 +
  硬编码灰阶，消除「脏蓝 / 明暗不匀 / 灰阶跳变」的观感问题。
- 参照 PCL2-CE `ToneProfile.DefaultDark` 的明暗双份参数思路，给出一套**独立于亮色**的
  深色亮度 / 色度阶梯；品牌蓝只保留在强调件（Color2/3/4、标题栏）上。
- **认证口径（硬门槛）**：主题 0「龙猫蓝」以及所有非 100 号主题的亮色外观与改动前
  **逐像素一致**；深色仅改配色计算，不改资源键名、不改引用方式、不改设置项、不改控件。
- 可验证标准：设置页切到「深色」后，文字/强调/分隔线/输入框/卡片/弹层/侧栏/滚动条
  层级清晰、无亮块残留、无深色字压深色底；切回亮色无回归；重启后保持。

## 2. 前置条件与阻塞项

- 基线：`dev.6-Windows` @ `84b63bf`。工作树有 **142 个未提交改动**
  （VB→C# 重写收尾：126 个 `.vb` 删除 + 14 个 `.cs`/`.xaml` 修改 + 2 个新增）。
  用户 2026-11-12 已核实**不存在并行会话**，这些是同一条工作线在对话中断后遗留的
  累积改动；其中 `pcl-next/Modules/ModSecret.cs`、`pcl-next/Application.xaml`、
  旧《计划-深色主题.md》均为 `M` 状态。**一律不覆盖、不 revert、不 `git add -A`**，
  只做基于当前磁盘内容的局部增量编辑。
- 无新增 NuGet 包、无联网需求、无工程引用变更；不触碰 `pcl-next.csproj`。
- 编译验证须显式传真实 RID（`win-x64`），产物落 `bin-net10` 三段树（§零）。
- 不阻塞：模糊移植、Linux / Android 线、VB 清理均与本计划正交。

## 3. 现状分析

- 现行深色（`2c6320c feat(ui): add dark theme`，见 `docs/plans/计划-深色主题.md`）：
  `ModSecret.ThemeRefresh` 用 `MyColor.FromHSL2`（标准 HSL + 色相分段亮度校正）生成
  `Color1..8`，深色分支是硬编码的 HSL 亮度 90/70/58/70/29/23/19/13；
  `ColorGray1..8` 是 8 个手写十六进制灰阶；语义面（Card/Popup/Tooltip/Overlay/Sidebar/
  Window）另用 `SurSat = ColorSat × 0.1` 的低饱和 HSL 生成。
- 11-11 已因「整屏脏蓝」返工过一次（把面层饱和度压到 8.5%），仍未解决根本问题：
  **HSL 的 L 不是感知亮度**，同一 L 值在蓝 / 绿 / 中性色上 perceived lightness 差异大，
  因此「等间隔 L 阶梯」在深色下视觉上忽明忽暗，且低饱和蓝大面积铺底仍会显脏。
- PCL2-CE 的做法（已核验 `D:\bing\MC\PCL2-CE`）：
  - `ThemeService` + `ToneProfileConfig` 提供明 / 暗两套独立 `ToneProfile`
    （Dark 的 L1..L8 = 0.96/0.75/0.6/0.65/0.45/0.25/0.225/0.2，
    `LWhite=0.275`、`LBackground=0.3`、`LForeground=1`；C1..C8 共用一套色度阶）。
  - 色彩空间是 **OKLab / OKLCh**（经 `Wacton.Unicolour 8.0.0`），`FromLch → ScRgb →
    Color.FromScRgb`，带 ReduceChroma 色域映射，写入同名的 `ColorBrush*` / `ColorObject*`
    资源键。这正是「感知均匀」的来源。
  - 另有 `ColorMode` 三态（亮 / 暗 / 跟随系统）、明暗各一套主题色、配置事件驱动刷新、
    `ColorModeChanged` 事件与 ContextMenu 递归重刷。
- **架构差异与取舍**：CE 的 `ThemeService` 绑死其 IoC 生命周期、配置事件、源生成器与
  Avalonia 后端抽象，整包搬入会与 pcl_next 的 `Settings`（Setup.ini 三源）和
  `MyColor` 体系冲突；且 CE 连**亮色**也走 OKLCh，整包替换必然违反第 1 节的亮色认证口径。
  故本轮只借「OKLCh + 明暗独立 ToneProfile」这一层**算法与参数**，不搬服务框架。

## 4. 改动文件清单

| 文件 | 范围 |
|---|---|
| `docs/plans/计划-深色主题重做-OKLCh.md` | 本计划（新增） |
| `pcl-next/Modules/Base/ModOklch.cs` | **新增**：OKLab/OKLCh 转换、色域映射、`DarkPalette` 调色板 |
| `pcl-next/Modules/ModSecret.cs` | 深色分支拆为旧/新两套：`ThemeRefresh` 深色颜色阶与语义面、`ThemeRefreshMain` 深色标题栏/窗口底，另增 `UseLegacyDark` 判定 |
| `pcl-next/Pages/PageSetup/Settings.cs` | 新增设置项 `UiDarkThemeVariant`（0=新版 OKLCh，1=旧版 HSL，默认 0），变更时触发 `ThemeRefresh()` |
| `pcl-next/Pages/PageSetup/PageSetupUI.xaml` | 新增「深色配色」一行两个 `MyRadioBox`，仅当主题选「深色」时生效 |

**明确不做**：

1. 不改 `ThemeRefresh` 的**亮色分支**（含 `Color1..8` / `ColorGray1..8` / `ColorBg0` 亮色计算）；
2. 不改任何资源键名、不改 `SetBrush` 写入逻辑、不改 `Application.xaml` 默认值与
   `StaticResource`/`DynamicResource` 引用方式；
3. 不改动设置页既有项、不重命名配置键，`UiLauncherTheme=100` 契约保持；
   仅按补充指示新增 `UiDarkThemeVariant` 一个配置键及其两个单选按钮；
4. 不实现 CE 的 `ColorMode` 三态 / 跟随系统 / 明暗各一套主题色 /
   `ColorModeChanged` 事件 / ContextMenu 递归重刷（属功能重构，非配色，另立议题）；
5. 不改 `ColorBrushRed*`、`ColorBrushInk*` 等语义固定色；
6. 不移植模糊 / 亚克力 / 着色器，不引入 `Wacton.Unicolour` 等第三方包；
7. 不改关于页、字体、动效、版本常量，不新增分发通道。

## 5. 改动要点

### 5.1 `ModOklch.cs`（新增，约 180 行）

- 自实现 Björn Ottosson 公开的 OKLab ↔ linear sRGB 3×3 矩阵（正向/逆向常量），
  `FromLch(L, C, hDeg, alpha)` → `ModBase.MyColor`（0–255 通道）。
- **色域映射**：给定 L/C/h 若 linear RGB 越界，二分降低色度 C 直至落入 sRGB
  （等价于 CE 的 `ScRgbMappingMode.ReduceChroma`），避免高饱和色直接 clamp 变灰。
- `DarkPalette`：按 CE `DefaultDark` 的 L1..L8 / C1..C8 与 alpha 常量
  （ABackground=0.824、AToolTip=0.9、AOverlay=0.867、ASidebar=0.953）计算全部颜色，
  `DarkHue = 255`（OKLCh 角度，对应品牌蓝；亮色仍用 HSL `ColorHue=210`）。
  唯一偏离：CE 的 L3=0.60 / L4=0.65 会让 `Gray3→Gray4` 亮度倒挂，
  故将深色 L4 下调为 0.58，保证 8 级灰阶严格递减。
- 输出映射到既有资源键：`Color1..8`、`Gray1..8`（C=0 中性阶）、`Bg0`，
  以及语义面
  `Card`（= LBackground, α=0.824）、`Popup`（L≈0.26, α=1）、
  `Tooltip`（LBackground, α=0.9）、`Overlay`（L≈0.12, α=0.87）、
  `Sidebar`（L≈0.24, α=0.95）、`Window`（L8）、
  `ScrollBar`/`ScrollBarHover`（中性 C=0, L=0.45/0.55）、
  `OnAccent`（近白，不反转）、`ObjectShadow`（黑）。
  `ColorBrushHalfWhite`、`ColorBrushInk*`、`ColorBrushRed*` 与 `Bg1`/`SemiTransparent`
  保留原有赋值不变，不纳入调色板，缩小深色回归面。
- **零 NuGet 依赖**：不引入 `Wacton.Unicolour`，仅用 `System.Math`；
  不开启 `AllowUnsafeBlocks`（主工程未开）。

### 5.2 `ModSecret.cs`（局部替换）

- `ThemeRefresh` 深色分支：由 20 余行 `FromHSL2` + 8 个硬编码灰阶，改为
  `var Pal = ModOklch.CreateDarkPalette();` 后逐字段取 `Pal.C1..C8` /
  `Pal.Gray1..Gray8` / `Pal.Bg0`；`ColorSemiTransparent`、`ColorBg1` 仍走原有公共赋值。
- 深色语义面分支：`ColorCard/Popup/Tooltip/Overlay/Sidebar/Window` 取 `Pal` 对应字段。
- `ThemeRefreshMain` 深色标题栏：三段 `LinearGradientBrush` 由
  `FromHSL2(ColorHue+Delta, TopSat, TopLight)` 改为
  `ModOklch.FromLch(0.28/0.32/0.28, 0.045, 255+Delta)`；
  主页面底彩色模式照 CE `darkLight=0.2` 口径取 L=0.168/0.192、C=0.093/0.028、
  hue±15；非彩色模式 L=0.20、C≈0.008 的中性底。亮色分支逐行保持原样。
- `SetBrush` 局部函数、所有 `Res[...]` 写入、`ThemeRefreshMain()` 调用、异常处理均不动。

### 5.3 旧 / 新版深色切换（补充指示）

- `ModSecret.UseLegacyDark`：`ThemeNow == ThemeDarkId` 且
  `Settings.Get<int>("UiDarkThemeVariant") == 1`；`ThemeRefresh` 与 `ThemeRefreshMain`
  共用这一判定，保证颜色键与主窗口底、标题栏**同步切换**，不会新旧混搭。
- 旧版分支逐行恢复重做前的 HSL 计算（`SurSat = ColorSat × 0.1`、L=90/70/58/70、
  8 个手写灰阶、语义面、`ScrollBar` / `OnAccent` / `ObjectShadow` 的旧常量、
  标题栏 `TopLight=19/23`、彩色底 `FormLight=13`、非彩色底 HSL L=13）；
  新版分支保持 §5.1–5.2 的 OKLCh 结果。亮色分支两类情况下都逐行不动。
- 设置页 `PanDarkVariant` 一行两个 `MyRadioBox`，绑 `UiDarkThemeVariant` 的 0 / 1；
  `Settings.cs` 的 `OnChanged` 调 `ThemeRefresh()` 立即重刷 `{DynamicResource}`；
  按钮在非深色主题下无视觉效果，Tooltip 已注明。
- 默认值 0，故升级后自动使用新版；用户可一键回退旧版，无需改代码或删配置。

## 6. 回归影响面

- 主题切换路径：设置页单选 → `Settings` 的 `UiLauncherTheme` 回调 → `ThemeRefresh`；
  启动时 `FormMain.xaml.cs:448`；HSL 滑条 `ThemeRefresh()` 无参调用。
- 资源引用：`{DynamicResource}` / `SetResourceReference`（`MyCard.cs` 等）实时刷新，
  键名不变即不需改；`AaColor(..., "ColorBrushN")` 动画读资源起止值，键名不变不需改。
- 已知边界（本轮不处理，需验收时留意）：
  - `MyHint.xaml.cs:75` 直接在 C# 里按 `ThemeNow == ThemeDarkId` 硬编码两套提示条配色，
    不读资源字典；
  - `FormMain.xaml:103` 的 `{StaticResource ColorObject4}`、
    `PageLaunchLeft.xaml:86` 的 `{StaticResource ColorObjectGray2}` 属可变色键静态引用；
  - `ColorHue / ColorSat / ColorLightAdjust` 全仓无赋值点，HSL 滑条未接线，
    因此深色固定 `DarkHue=255`，不随滑条变化（与改动前一致）。
- 不做测试工程；靠构建 + 统一运行验收（见 7）。

## 7. 验证步骤

1. 静态验证（增量开发期）：

   ```bash
   dotnet build PCL_NEXT_Windows/pcl-next/pcl-next.csproj -c Release -r win-x64 --self-contained
   ```

   要求 **0 错误**；产物按 §零 落 `pcl-next/bin-net10/<代号>/<版本名>-<构建号>/win-x64/`，
   不出现 `any` 段、不落在 `publish\` / `bin\` 根。
2. 全量完成后一次性运行验收：
   - 默认启动（龙猫蓝 0）逐页对比改动前截图，确认**亮色零回归**；
   - 设置页切「深色」→ 标题栏 / 侧栏 / 卡片 / 输入框 / 列表 / 下拉 / 右键菜单 /
     提示气泡 / 滚动条层级分明，无亮块残留、无深色字压深色底、无脏蓝大面积铺底；
   - 检查强调件：下载页「开始下载」按钮、快捷按钮图标、标题栏文字在蓝底上仍为白字
     （`ColorBrushOnAccent` 不反转）；
   - 彩色背景开 / 关两种模式各看一次主页面底与标题栏；
   - 设置页「深色配色」在「新版（OKLCh）」/「旧版（HSL）」间来回切换，
     确认标题栏、侧栏、卡片、滚动条同步变化且无新旧混搭；重启后保持所选版本；
   - 重启启动器后深色保持，切回「龙猫蓝」恢复亮色。
3. 判定标准：编译 0 错误 + 上述逐项通过；若有任何亮色回归，本轮不予提交。

## 8. 回滚方案

- 本轮改动集中在新增 `ModOklch.cs` 与 `ModSecret.cs` 的深色分支，
  回滚 = `git revert` 本轮提交并删除新增文件；亮色分支从未改动，回滚无副作用。
- `UiLauncherTheme=100` 的配置语义不变，回滚后 `ThemeRefresh` 仍把它当深色处理
  （回到旧 HSL 观感），不会崩溃；必要时在设置页手动切回「龙猫蓝」。
- 若只想回到旧配色而不回滚代码，把 `UiDarkThemeVariant` 设为 1 即可（设置页「旧版」）。
- 不修改旧《计划-深色主题.md》（他人未提交改动），其历史记录保留。

## 合规边界

- **不复制任何 PCL2-CE 源码文件**。本轮只借鉴两类内容：
  ① OKLab/OKLCh 与 sRGB 的转换矩阵、色域映射思路（Björn Ottosson 公开的数学公式，
  非 CE 原创代码）；② `ToneProfile.DefaultDark` 的一组数值参数（L/C/alpha，属事实性
  配色参数，不构成可版权保护的表达式）。`ModOklch.cs` 为独立编写实现。
- **不引入 `Wacton.Unicolour`**，避免新增第三方许可与依赖闭包；也不移植 CE 的
  `BlurBorder.cs` / `BackgroundPresenter.cs`（EleCho.WpfSuite, MIT）等文件。
- CE 宿主与业务内核受 PCL 自定义 LICENCE 约束（L1–L5），本轮未引入其分发产物、
  未新增分发通道、未改动关于页署名、未改动主色义务相关的品牌蓝、未触碰正版劝导与
  赞助链接；`docs/合规/跨平台授权问询.md` 的暂不发送决定继续有效。
- 本轮为 Windows 自用改版的本地 UI 计算逻辑，不涉及 MC 本体下载门控、
  遥测、自动更新等合规敏感链路。

## 9. 首轮验收反馈与交互修正（2026-11-12 14:00）

### 现象

用户在设置页点击「新版（OKLCh）/ 旧版（HSL）」后反馈「没反应」。截图显示当时主题为
「龙猫蓝」（亮色），「深色」未选中。

### 根因（已确证，非绑定 bug）

- `ModSecret.UseLegacyDark` 的判定为 `ThemeNow == ThemeDarkId && ... == 1`。
  亮色主题下 `ThemeNow != 100`，两套深色配色都不参与计算，切换只改变配置值，
  `ThemeRefresh()` 重算出的资源与之前完全一致，因此零视觉变化。
- 现场证据：运行目录 `2.100.0.6-0.92-blur/win-x64/PCL/Setup.ini` 中已写入
  `UiDarkThemeVariant:0`，而 `UiLauncherTheme` 键不存在（默认 0 亮色）。
  说明 `MyRadioBox → SettingService.SaveSetting → Settings.Set → OnChanged` 链路正常，
  配置持久化成功，问题纯粹是「生效条件不可见」造成的体验缺陷。
- 原先 Tooltip 写「仅当主题选择「深色」时生效」，信息太弱，用户不会先去改主题。

### 修正

- `PageSetupUI.xaml.cs` 订阅两个单选的 `Check` 与 `PreviewCheck`：
  当前不是深色主题时，自动 `Settings.Set("UiLauncherTheme", ThemeDarkId)`，
  由 `UiLauncherTheme.OnChanged → ThemeRefresh(100)` 应用刚选中的配色，点一下即见效。
  `MyRadioBox` 构造函数先订阅了 `SaveSetting`，故回调执行时 `UiDarkThemeVariant`
  已写入缓存，`ThemeRefresh(100)` 读到的是新值，不会出现「切了主题但配色是旧的」。
- 重复点击已选中项时 `IsChanged == false`、`Check` 不触发，由 `PreviewCheck` 补一次，
  保证任何点击都有反馈。
- 只调用 `SettingService.RefreshSettings(PanLauncherTheme)` 同步主题单选选中态，
  不用整页 `RefreshSettings`，避免触发其它控件的 `Change` 回写；刷新期间临时抬高
  `ModAnimation.AniControlEnabled` 以阻止 `RefreshSetting → Check → SaveSetting`。
- 切换后给一条 `ModMain.Hint` 明确告知当前使用的配色版本。
- 两处 Tooltip 改为「选择后自动切换到深色主题」，让预期与实际行为一致。
- 已是深色主题时不额外动作：`Settings` 的 `OnChanged` 已经即时 `ThemeRefresh()`。

### 验证

- `dotnet build pcl-next/pcl-next.csproj -c Release -r win-x64 --self-contained` →
  0 错误 / 26 个既有警告，无本轮新增警告。
- 产物 `pcl-next/bin-net10/Dev/2.100.0.6-0.90/win-x64/`：二进制中可检出
  `DarkVariant_Apply` 与提示字符串；`0.92-blur` / `0.93-blur2` 两份旧件不含本修复，
  验收必须改用新目录的 exe。
- 待用户 GUI 验收：亮色下点「旧版」应立刻变为旧版深色并弹提示；再点「新版」
  应立刻变为新版深色；切回「龙猫蓝」亮色零回归；重启后保持。
