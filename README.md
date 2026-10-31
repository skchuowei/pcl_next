# pcl-next

基于 [Plain Craft Launcher 2（PCL2）](https://github.com/Hex-Dragon/PCL2) 的第三方分支，把启动器主工程迁移到 **.NET 10（`net10.0-windows`）**，界面、功能与使用方式保持与 PCL2 一致。

## 简介

- 语言：VB.NET（WPF）
- 目标框架：`net10.0-windows`
- 工程形式：SDK 风格工程 `pcl-next.vbproj`
- 命名空间：`PCL`（与上游一致，便于共用源码）

仓库内保留了上游遗留的 `Plain Craft Launcher 2.vbproj`（net48，非 SDK 风格），它与 `pcl-next.vbproj` 共用同一套源码、互不覆盖：前者输出到 `bin`，后者输出到 `bin-net10`。

## 目录结构

```
.
├── pcl-next.vbproj               # .NET 10 主工程
├── Plain Craft Launcher 2.vbproj # 上游遗留的 net48 工程（共用源码）
├── Application.xaml(.vb)         # 应用入口
├── FormMain.xaml(.vb)            # 主窗体
├── Modules/                      # 启动、下载、Java、资源等核心逻辑
├── Pages/                        # 各页面 UI
├── Controls/                     # 自绘控件
├── Resources/                    # 字体、帮助文档、第三方 jar 等资源
├── PCLCS/                        # 依赖：下载 / 启动核心库（net10.0-windows）
└── MeloongCore/                  # 依赖：通用工具库（Shared + Wpf）
```

## 构建

环境要求：

- Windows 10/11 x64
- .NET SDK 10.0 及以上（`dotnet --list-sdks` 可查）
- 仅用 Visual Studio 构建时，需安装 “.NET 桌面开发” 工作负载

命令行构建：

```powershell
dotnet build .\pcl-next.vbproj -c Release
```

产物位于 `bin-net10\`。发布单文件版本：

```powershell
dotnet publish .\pcl-next.vbproj -c Release -r win-x64 --self-contained false
```

## 依赖说明

| 目录 | 说明 |
| --- | --- |
| `PCLCS` | 下载、安装、启动 Minecraft 的核心库，同时内嵌 Java 包装器与 Wiki 数据 |
| `MeloongCore/Shared` | 通用工具库（网络、文件、日志、配置等） |
| `MeloongCore/Wpf` | WPF 相关工具（对话框、转换器、窗口工具等） |

这两个依赖都已直接放在仓库中，`pcl-next.vbproj` 通过 `ProjectReference` 引用，克隆后即可编译，无需额外拉取。

## 与上游的主要差异

- 主工程迁移为 SDK 风格、目标框架升到 .NET 10
- 第三方依赖（Newtonsoft.Json、NAudio、CacheCow、ThrottleDebounce 等）统一改为 NuGet 引用，不再内嵌 net45 程序集并靠 `AppDomain.AssemblyResolve` 动态加载
- 不再使用 VB 的 `My` 命名空间，相关能力由 `Modules/Base/MyCompat.vb` 补齐
- 下载功能的开关改为部署期配置

## 许可

本项目基于 PCL2，其授权遵循仓库内的 [LICENCE](LICENCE)（《PCL 分发有限许可》与《PCL 存储库合理使用指南》）。使用、修改与分发前请先阅读该文件。
