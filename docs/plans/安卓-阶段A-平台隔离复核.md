# 安卓线 · 阶段 A 平台隔离点复核（实测）

> 关联：`docs/plans/计划-安卓UI移植.md` §1.3（隔离点清单）与 §1.1（复用前提）。
> 复核日期：2026-11-07。复核方式：对仓库当前 `dev` 树做只读 grep + 依赖闭包核对 + WSL Ubuntu 24.04 实跑 `build-linux` 护栏（406 测试通过、架构护栏通过）。
> 状态：**复核完成，结论推翻原 §1.1 的「去 Shared 专属后复用」前提**。

---

## 0. 一句话结论

安卓真正要复用的后端 `PCL.Download` + `PCL.Cli` **已经是跨平台干净的纯 BCL**，不引用任何 Windows 专属库；`MeloongCore.Shared` 名为 Shared、实为 **Windows 专属库**，且**不在内核引用闭包内**。因此阶段 A 的「隔离点抽接口」工作量远小于规划假设，安卓不需要、也不应该去复用 `MeloongCore.Shared` 里的 Windows 工具。

---

## 1. 依赖闭包实测（关键证据）

| 工程 | TargetFramework | ProjectReference | 引用 `MeloongCore.Shared`？ | Windows 专属 API |
|---|---|---|---|---|
| `PCL.Download` | net10.0 | **无**（纯 BCL，`PclIsGuiFree=true`） | **否** | 无 |
| `PCL.Cli` | net10.0 | 仅自身/内核 | **否** | 无 |
| `MeloongCore.Shared` | net10.0（但含 P/Invoke） | 自包含 | 自身 | 见 §2 |
| `PCLCS` | net10.0-**windows** | `MeloongCore.Shared` | 是 | 随 Shared |
| `MeloongCore.Wpf` | net10.0-windows | `Shared` | 是 | 随 Shared + WPF |

**佐证 1（架构护栏实跑）**：`PCL.Cli` 的 `SelfTestCommand.CheckArchitecture`（§计划 §5 可验证标准）在 WSL Linux 上把 `MeloongCore` / `PCLCS` / `MeloongCore.Wpf` / `System.Windows.Forms` 列为「进程内禁止出现的程序集」，本次 `build-linux` 实跑该检查通过（offline 套件 `arch` ✓、`resource-registry` ✓），说明 `PCL.Download` 运行时引用闭包内确实没有这些程序集。

**佐证 2（误报排除）**：`PCL.Download` 源码中出现的 `ResourceConfig` / `ResourceProviderRegistry` 是 PCL.Download 自己的领域类，与 `MeloongCore.Shared` 的 `Config` / 注册表无关；grep 命中的 `FileUtils`/`PathUtils`/`RegistryUtils` 均为注释或字符串，无真实调用。

**佐证 3（编译/测试实跑）**：`dotnet build PCL.Download -r linux-x64` 与 `PCL.Cli -r linux-x64` 在 Linux 上 0 错误；`PCL.Download.Tests` 406 项全绿。DllImport 方法仅在被调用时加载原生 DLL，编译期不报错、运行期按需隔离——证实内核路径未触达这些入口。

---

## 2. `MeloongCore.Shared` 内的 Windows 专属点（仅 Windows 端需要，不在安卓路径）

| 文件:行 | 专属能力 | P/Invoke / 依赖 | 安卓处置 |
|---|---|---|---|
| `Utils/RegistryUtils.cs` 全文 | Windows 注册表读写 | `Microsoft.Win32.Registry`（csproj 已引包 `5.0.0`） | **禁用**：安卓无注册表；配置走 `SharedPreferences` / 应用私有文件 |
| `General/MsDelta.cs:49,56` | Windows 增量补丁（MS Delta） | `msdelta.dll` | **禁用**：安卓不参与增量补丁；接口留空实现 |
| `Utils/CryptographyUtils.cs:191-196` | 签名/哈希（BCrypt） | `bcrypt.dll` | **需评估**：内核当前**未使用** bcrypt（`HashHelper` 走托管）；若安卓做资源签名校验，需托管替代（`System.Security.Cryptography` 或 BouncyCastle） |
| `Utils/FileUtils.cs:314` | Shell 已知文件夹/缩略图 | `shell32.dll` | **禁用**：走标准 `System.IO` |
| `Utils/PathUtils.cs:108-114` | 短文件名/重解析点 | `kernel32.dll` / `ntdll.dll` | **禁用**：短名、重解析点是 Windows 概念；安卓用标准 `File` API |
| `Utils/DirectoryUtils.cs:177-181,289` | 已知文件夹路径 | `Environment.SpecialFolder`（Desktop/Documents/Windows/UserProfile/ApplicationData） | **需接口**：安卓用应用专属目录（`context.getFilesDir` / `getExternalFilesDir`），由 UI 宿主注入路径根 |
| `Wpf/General/Dialogs.cs`、`Wpf/Utils/WindowsUtils.cs` | 对话框/窗口/权限/托盘 | WPF | **需接口**：抽 `IDialogService` / `ISystemService`，安卓用原生能力；UI 层重写 |

> 说明：上述 DllImport 与 `SpecialFolder` 调用在 Linux/Android 编译期不会失败（仅运行期缺 DLL 才抛），所以「内核能在 Linux 编译」不等于「这些 API 能在 Linux 跑」——但内核路径本就不调用它们，故无影响。

---

## 3. 安卓实际需要的接口边界（重新界定）

原 §1.3 假设「Shared 去专属后复用」，实测后应改为「**内核已干净，Shared 不进安卓路径；仅以下边界由 UI 宿主提供**」：

1. **路径根 / 配置根**：安卓无 `SpecialFolder.ApplicationData`。由 `PCL.Android` 在启动时把应用私有目录注入内核（内核已支持外部传入根路径，无需改 Shared）。
2. **资源签名校验**（若启用）：内核当前哈希用托管实现；若需要非对称签名验证，单独引入托管加密库，避开 `bcrypt.dll`。
3. **对话框 / 系统服务**：纯 UI 层职责（`IDialogService`），与内核无关，安卓原生实现。

**不需要**为安卓抽 Registry / MsDelta / shell32 / kernel32 的跨平台接口——这些能力安卓根本不用。

---

## 4. 对 §1.1 / §4.2 的修订建议（待合入主计划）

- §1.1「`MeloongCore/Shared` 的跨平台部分作为安卓后端直接复用」→ 改为「**内核 `PCL.Download`/`PCL.Cli` 直接复用；`MeloongCore.Shared` 不进安卓路径，其 Windows 工具按需由安卓原生重写**」。
- §4.2「把 Shared 的专属能力抽成接口」→ 缩小为「仅路径根/配置根由 UI 宿主注入；其余 Windows 专属能力不移植」。
- 阶段 A 退出标准可简化为：「`PCL.Android` 引用 `PCL.Download`+`PCL.Cli` 编译通过且 406 内核测试全绿；确认零 `MeloongCore.Shared` / `PCLCS` 引用」。

---

## 5. 仍待决 / 不在本轮范围

- **编排下沉 `PCL.Core` 的剩余项**：并行线已将 Forge/NeoForge 安装器纯逻辑、版本合并迁入 `PCL.Download`（见 dev 上 `ForgelikeInstaller.cs`/`VersionMerger.cs`）。是否还需独立的 `PCL.Core` 中立库、或继续以 `PCL.Download` 为内核载体，待 Phase 6 的 T2 拆壳结论，不因工具链就绪提前（见《分支与发布规范》§0A）。
- **合规问询**：`docs/合规/跨平台授权问询.md` 仍「暂不发送」，未获上游书面答复前不对外分发非 Windows 制品。
- **分支同步**：`android` 分支当前落后 `dev` 14 个提交（与 dev 已分叉），需 `merge --no-ff dev` 重新同步后再落本轮文档；本文件当前为草稿，待同步后提交到 `android`。
