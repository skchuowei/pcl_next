# 计划：安卓端 UI 移植（WPF/VB → 跨平台 UI）

> 依据：《MeloongCore/AGENTS.md》§公共 API 与平台边界、《MeloongCore/.agents/Shared/架构分析.md》Windows 专属入口说明、《计划-内核与GUI拆分（CLI）.md》T2 拆壳结论。
> 状态：**规划阶段**。本文件只定分层、选型倾向、迁移清单与验收口径；Android 工程骨架与编译验证待 SDK/workload 就位后动工。
> 本文件为专项计划，移植完成后随版本归档，不作为长期规则入口。

---

## 0. 决定记录（先定方向，再谈细节）

| 项 | 决定 |
|---|---|
| 后端复用 | `PCL.Download`（net10.0、纯 BCL、零第三方）与 `MeloongCore.Shared` 的跨平台部分作为安卓后端直接复用；`PCL.Cli` 编排逻辑下沉为可被 UI 调用的内核 API，不把控制台入口搬到安卓。 |
| UI 框架倾向 | **优先 Avalonia**（XAML 与 WPF 语法最近、绑定/样式/Converter 概念可平移、Linux 可编译验证）；.NET MAUI 作为备选（官方长期支持、Android 绑定一等公民，但 XAML/控件体系与 WPF 差异更大）。最终选型以 P0 页面原型验证为准。 |
| VB 处置 | VB.NET 无跨平台编译器（仅 Windows），`pcl-next` 全部 `.vb` 必须转 C#；不尝试在安卓侧保留 VB 工程。 |
| 拆壳前置 | 安卓移植以 CLI 拆壳（T2）结论为界：VB 侧的下载/资源逻辑已下沉到 `PCL.Download`/内核后，安卓只重写 UI 与交互，不重抄业务。 |

**三条理由**

1. **后端已基本中立。** `PCL.Download` 已是 net10.0 纯 BCL；`PCL.Cli` 走命令行通道复用同一引擎；真正绑死 Windows 的是 `pcl-next`（VB WPF）、`MeloongCore.Wpf`、`PCLCS` 三个 `net10.0-windows` 工程。
2. **现有 UI 是 code-behind 直连控件，无 MVVM 层。** 无论选哪种跨平台框架，页面交互都要重建；XAML 平移只能救回布局/样式，救不回事件逻辑。因此移植收益最大的是后端复用，而非 XAML 逐行翻译。
3. **环境当前不具备 Android 编译条件。** 云主机未安装 MAUI/Android workload，也无 Android SDK 与对应 JDK；本轮先固化分层与清单，不在缺工具链时硬搭工程骨架。

---

## 1. 现状盘点

### 1.1 可直接复用（后端 / 中立层）

| 工程 | 目标框架 | 安卓可用性 | 备注 |
|---|---|---|---|
| `PCL.Download` | net10.0 | ✅ 直接复用 | 下载引擎、分片、续传、校验、源轮换、进度；纯 BCL，零第三方依赖。 |
| `PCL.Cli` | net10.0 | ⚠️ 逻辑复用、入口不复用 | `InstallLoaderCommand`/`LoaderResolver`/`MavenCoord` 等编排逻辑需下沉为内核库，供 UI 与 CLI 共用。 |
| `MeloongCore/Shared` | net10.0 | ⚠️ 去专属后复用 | 通用扩展、配置、日志、进度、Worker、文件/加密/字符串工具可复用；Windows 专属入口需隔离（见 1.3）。 |

### 1.2 必须重写（UI / Windows 绑定层）

| 工程 | 内容 | 处置 |
|---|---|---|
| `pcl-next` | 63 个 XAML + 122 个 VB，页面 `PageLaunch / PageDownload / PageInstance / PageSetup / PageOther / PageLink`，自定义控件 `Controls/`（MyButton/MyCard/MyCheckBox/MyComboBox/MyDropShadow/MyHint/MyIconButton/MyMsg…） | VB→C# 重写交互；XAML 按选型框架平移布局与样式；控件逐个做映射表（§4.3）。 |
| `MeloongCore.Wpf` | WPF 初始化、Tooltip 附加行为、控件扩展、对话框、转换器、Windows 权限/窗口/系统操作 | 跨平台部分（转换器、通用扩展）平移；对话框、窗口句柄、系统操作按平台接口重写。 |
| `PCLCS` | net10.0-windows C# 移植层 | 随 T2 拆壳结论处理；安卓侧只取其中与 UI 无关的纯逻辑。 |

### 1.3 平台专属隔离点（Shared 内必须先打接口边界）

| 入口 | 专属能力 | 安卓处置 |
|---|---|---|
| `Shared/Utils/RegistryUtils.cs` | Windows 注册表 | 抽象为配置/偏好接口，安卓用 SharedPreferences/文件实现；注册表实现留在 Windows。 |
| `Shared/General/MsDelta.cs` | Windows 增量补丁（MS Delta） | 安卓侧不参与增量补丁；接口留空实现或暂不接入。 |
| `Shared/Utils/PathUtils.cs` | 含 Windows 路径/目录假设 | 复核 `Environment.SpecialFolder`、盘符、隔离存储路径，抽 `IPathProvider` 按平台实现。 |
| 对话框 / 窗口 / 权限 / 托盘 | `MeloongCore.Wpf/Utils/WindowsUtils.cs`、`General/Dialogs.cs` | 抽 `IDialogService` / `ISystemService`，安卓用对应原生能力实现。 |

> 复核口径：先 grep Shared 内 `Registry`、`System.Windows.Forms`、`Process.Start`、`MsDelta`、`Environment.OSVersion`、`SpecialFolder`，逐条标注「可跨平台 / 需接口 / 安卓禁用」。

---

## 2. 目标与可验证标准

### 目标

1. 抽出不依赖 UI 框架的内核层（下载、安装编排、配置、进度），Windows 客户端与安卓端共用同一后端。
2. 在选定的跨平台 UI 框架下，先跑通 P0 页面（启动/版本列表/实例列表/下载进度），再逐步平移其余页面。
3. 后端行为回归仍由 `PCL.Download.Tests` 守护；UI 层只做界面与绑定，不重抄下载/安装业务逻辑。

### 可验证标准

- 内核库在不引用任何 WPF/Windows 工程的前提下编译通过（对齐 CLI 已有的架构护栏）。
- 选定框架的 P0 原型在 Linux 编译出桌面预览；Android 工程在配齐 SDK 后可构建出 APK（环境就位后验证）。
- 移植后的下载/安装流程结果与 CLI `loader`/`install` 命令一致：同源列表、同校验规则、同版本 JSON 合并口径。

---

## 3. 框架选型对比

| 维度 | .NET MAUI | Avalonia | 原生重写（Kotlin/Jetpack Compose） |
|---|---|---|---|
| XAML 与 WPF 接近度 | 低（控件/绑定体系不同） | 高（样式、DataTemplate、Converter、附加属性概念一致） | 无 XAML，Compose 声明式 |
| 现有 VB 逻辑复用 | 低 | 低（code-behind 都需重写） | 无 |
| Android 支持 | 一等公民，官方绑定成熟 | 成熟，跨桌面+移动 | 原生体验最佳 |
| Linux 编译验证 | 受限（Android workload/SDK 重） | 好，可先出桌面预览 | 不适用 |
| 长期维护 | 官方路线 | 社区活跃 | 需独立维护原生层 |
| 人力成本 | 中高（体系差异） | 中（XAML 平移） | 高（新语言+重写全部交互） |

**倾向 Avalonia 起步**：先用它在 Linux 跑通 P0 桌面预览，验证布局/绑定/控件映射成本；若 Android 绑定或性能不达预期，再评估 MAUI。选型最终由 P0 原型的实际平移量决定，不在规划阶段锁死。

---

## 4. 分层与迁移清单

### 4.1 目标工程树（规划态，骨架待 SDK 就位创建）

```
pcl_next/
├── PCL.Download/            # 复用，不动公开行为
├── PCL.Core/                # 新增：从 CLI 下沉的安装编排/加载器解析/版本合并（中立库，net10.0）
├── MeloongCore/Shared/      # 去 Windows 专属，专属能力走接口
├── PCL.Cli/                 # 改为引用 PCL.Core，不再自持编排逻辑
├── PCL.Desktop.Avalonia/    # 新增：Avalonia 前端（Windows 桌面预览先行）
└── PCL.Android/             # 新增：Android 前端，引用 PCL.Core + 跨平台 UI 库
```

### 4.2 后端下沉顺序

1. `MavenCoord`、`LoaderProfile`、`LoaderResolver`、`ForgeNeoResolver`、`InstallLoaderCommand` 的编排部分从 `PCL.Cli` 移到 `PCL.Core`；CLI 只留命令行参数解析与输出协议。
2. 把 Shared 的专属能力抽成接口（注册表/路径/对话框/系统服务），Windows 实现留在 Wpf/Windows 层，安卓提供独立实现。
3. 补 `PCL.Core` 离线单测：坐标解析、profile 合并、库去重、参数合并口径（当前这些类在 CLI、测试工程引用不到，需随下沉进入可测库）。

### 4.3 控件与页面映射表（待 P0 原型时填实）

| WPF/VB 现有 | 选型框架对应 | 迁移注意 |
|---|---|---|
| MyButton / MyExtraButton / MyExtraTextButton / MyIconButton | 框架 Button + 样式 | 按住/圆角/阴影/禁用态逐样核对 |
| MyCard | 容器 Border/Panel + 样式 | 阴影在安卓以 elevation 实现 |
| MyCheckBox / MyComboBox(+Item) | 框架 CheckBox / ComboBox/Select | 选中值与绑定口径对齐 |
| MyDropShadow | 阴影效果 | 平台投影 API 差异 |
| MyHint / MyMsg | 提示/对话框 | 走 `IDialogService` 抽象 |
| Behaviors（ClipboardInterceptor/LazyLoad/Interfaces） | 附加行为/代码后置 | 剪贴板、懒加载按平台能力重写 |

页面优先级：

- **P0**：PageLaunch（版本/启动入口）、PageInstance（实例列表/启动）、下载进度面板。
- **P1**：PageDownload（安装/资源下载）、PageSetup（启动/UI/系统设置）。
- **P2**：PageOther（关于等）、PageLink、导出/导入等次要页面。

### 4.4 语言转换注意

- VB→C#：`Import` 全局命名空间改为 C# `using`；VB 事件处理器、`Handles`、`My.` 关键字、`IsNot`/`TypeOf` 等写法逐一对照；保留现有 C# 13 / Nullable 风格约定。
- 字符串与编码：沿用 UTF-8 无 BOM；VB 侧默认编码假设在跨平台下需显式指定 UTF-8。

---

## 5. 阶段划分

| 阶段 | 内容 | 退出标准 |
|---|---|---|
| A. 后端中立化 | 编排下沉 `PCL.Core`；专属能力抽接口；补离线单测 | 内核库不引用 WPF/Windows 工程；测试绿；CLI 行为不回归 |
| B. 选型原型 | Avalonia 搭空壳，平移 P0 一个页面（实例列表 + 下载进度） | Linux 桌面预览可跑；控件映射成本有实测结论；框架选型定稿 |
| C. P0 移植 | 启动/实例/进度三页 C# 重写 + 绑定接通内核 | P0 流程在预览端可走完下载到列表刷新 |
| D. Android 落地 | 配 Android SDK/workload；`PCL.Android` 工程接同一内核与 UI 库 | 可构建 APK，P0 页面在安卓设备/模拟器跑通 |
| E. P1/P2 平移 | 下载/设置/其余页面按映射表迁移 | 页面与 Windows 端功能对齐，回归走内核测试 |

---

## 6. 边界、风险与环境阻塞

**本轮不做**

- 不在缺 Android SDK/workload 时强行创建无法编译的 Android 工程。
- 不把控制台 `PCL.Cli` 入口直接搬到安卓；编排逻辑必须先下沉为中立库。
- 不在未拆壳前重抄 VB 内的下载/资源业务（以 T2 下沉结论为准）。

**风险与阻塞**

1. **工具链（已就绪）**：云主机已装 JDK 17、Android SDK 与 .NET Android workload，最小 `net10.0-android` 工程已编译出签名 APK（见 §7）。剩余缺口是 Android 模拟器/真机用于运行验收。
2. **WAF / 网络**：下载引擎在受限网络下可能遇到镜像 TLS 指纹（JA3）拦截（见引擎注释），与 UI 移植无关，但安卓端同样要走源轮换与失败兜底。
3. **processor 链**：Forge/Neo 安装期 Java 工具链补丁不在下载/UI 层解决；安卓端若支持模组加载器安装，需另行规划 JVM 运行时策略。
4. **code-behind 规模**：122 个 VB 文件交互逻辑未建模为 ViewModel，阶段 B 需决定是否借移植补一层 MVVM，还是维持 code-behind 平移。

**环境前提（阶段 D 前就绪）**

- .NET Android workload、Android SDK（platform/build-tools）、兼容 JDK；
- 选定框架的 Android 包版本与 .NET 10 兼容；
- 一台可运行 Android 模拟器或真机用于验收。

---

## 7. 环境就绪记录（2026-11-06，云主机）

已在用户目录便携安装，不依赖 apt/系统级包；新 shell 自动 `source ~/tools/android-env.sh`。

| 组件 | 版本 / 路径 |
|---|---|
| JDK | Temurin 17.0.21+9，`~/tools/jdk-17.0.21+9`（`JAVA_HOME`） |
| Android SDK | `~/android-sdk`（`ANDROID_HOME`）；platforms `android-35`、`android-36`；build-tools `35.0.0`、`36.1.0`；platform-tools 37.0.1 |
| .NET Android workload | `android` 36.1.118（SDK 10.0.400）；模板 `android` / `androidlib` 可用 |
| .NET SDK | 10.0.403（`~/​.dotnet`） |

**验证结果**：`dotnet new android` 生成 `net10.0-android` 空工程，`dotnet build -c Debug` 0 错误，产出 `*.apk` 与 `*-Signed.apk`。注意 workload 默认要求 API 36（`platforms/android-36`），仅装 API 35 会报 XA5207。

**仍缺**：Android 模拟器/镜像（system-image）与真机，无法在本机跑起 APK 做运行期验收；headless 环境只保证可构建。
