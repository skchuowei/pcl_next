# 计划：Windows 界面与下载边界缺陷修复

> 范围：`PCL_NEXT_Windows` / `dev.6-Windows`。本计划针对 `docs/bugscan/J1/J2/J3/G2/J4` 已登记的问题；不得改动 Linux、Android 或其他仓库。

## 1. 目标

- 修复已确认或有明确触发条件的 Windows 下载、实例、设置、帮助、登录与皮肤缺陷。
- 对用户指出的 `Task.Input`、路径前缀截取、`Init` / `Parent` / `Content` 空值和空桩行为增加有意义的处理。
- 回归标准：Windows 主工程 Release 项目级 `win-x64` 构建成功；相关已有测试通过；修复后的缺陷记录与代码相符。
- `PageDownloadModify` 的进度除零当前受 `foreach` 空集合语义保护，不作为已发生异常；在实现中显式处理空列表，使该保证不依赖循环内部细节。

## 2. 前置条件与阻塞项

- 已确认工作树干净，分支为 `dev.6-Windows`，最新提交为 `2c6320c`。
- 本次仅在 Windows 独立仓库工作；不从其他平台拷贝代码，不改版本常量或发布渠道。
- 页面生命周期修复不得改变正常导航、登录、下载及设置持久化行为。
- 五个 `PageDownload*` 空桩是有意的 UI 容器，不实现被下线的业务功能；仅修正其消费者的空值处理。

## 3. 现状分析

- J1 记录了下载修改流程的 `Task.Input` 空值、两个路径 `Substring` 前缀假设、父级空引用、列表项 `Init()` 结果，以及下载页 `Content` 尚未就绪等边界；其除零项明确标注为现有控制流下不可达。
- J2 记录实例页切换右侧页面时未检查 `Child` 类型/空值、RAM 刷新静态引用未判空、实例导出异常目录处理及无选中项读取。
- J3 记录帮助详情 `Init` / `Entry` 空值、帮助加载失败后的搜索、设置音乐刷新守卫错位及静态页面引用未就绪等问题；`PageOtherTest` 的下线提示已存在，不属于本轮改动。
- G2 记录 Auth/Nide 账号输入事件漏接/重复接线、密码数组越界、设备码轮询字段缺失、皮肤页加载器状态检查错位、皮肤位图资源、JSON 响应边界及无上限重试等问题。
- 这些问题多源于 UI 尚未初始化、损坏/不完整的远程数据或迁移时事件语义改变；不能用宽泛 catch 静默吞错。

## 4. 改动文件清单

| 文件 | 计划范围 |
|---|---|
| `pcl-next/Pages/PageDownload/PageDownloadModify.cs` | `Task.Input` 守卫；空文件列表进度；路径相对化前校验根路径；还原按钮 `Parent` 类型守卫；加载/初始化结果检查 |
| `pcl-next/Pages/PageDownload/ModDownloadLib.cs` | Minecraft 库路径相对化前做路径前缀校验，避免错误截取 |
| `pcl-next/Pages/PageDownload/PageDownloadInstall.xaml.cs` | 虚拟列表初始化失败安全处理；空条件 JSON 索引完整判空 |
| `pcl-next/Pages/PageDownload/PageDownloadLeft.xaml.cs` | 懒加载页面/内容未就绪时安全重置搜索状态 |
| `pcl-next/Modules/Download/LoaderPlanAdapter.cs` | 临时 Mod 文件路径相对化前校验根路径 |
| `pcl-next/Pages/PageDownload/Resource/MyResourceItem.xaml.cs` | 点击资源项时检查懒加载详情页和内容对象是否就绪 |
| `pcl-next/Pages/PageInstance/PageInstanceLeft.xaml.cs` | 右侧 `Child` 仅在为页面实例时执行退出回调 |
| `pcl-next/Pages/PageInstance/PageInstanceSetup.xaml.cs` | RAM 刷新页面/实例生命周期守卫 |
| `pcl-next/Pages/PageInstance/PageInstanceExport.xaml.cs` | 不存在或不可访问的导出来源目录安全跳过 |
| `pcl-next/Pages/PageInstance/PageInstanceOverall.xaml.cs` | 无选中项时不读取其动态属性 |
| `pcl-next/Pages/PageSetup/PageSetupUI.xaml.cs` | 修正音乐面板守卫；检查静态页面引用后再刷新 |
| `pcl-next/Pages/PageSetup/PageSetupLaunch.xaml.cs` | RAM 刷新所需控件与页面引用完整性检查 |
| `pcl-next/Pages/PageOther/PageOtherHelpDetail.xaml.cs` | `Entry` 初始化、刷新及失败态空值处理 |
| `pcl-next/Pages/PageOther/PageOtherHelp.xaml.cs` | 帮助列表未成功加载时停止搜索 |
| `pcl-next/Pages/PageOther/PageOtherLeft.xaml.cs` | 帮助页懒初始化引用守卫 |
| `pcl-next/Pages/PageLaunch/PageLoginAuth.xaml.cs`、`PageLoginNide.xaml.cs` | 消除重复事件订阅、恢复输入文案更新、密码索引越界保护 |
| `pcl-next/Pages/PageLaunch/MyMsgLogin.xaml.cs` | 校验设备码轮询与 token 响应字段，处理可空错误响应 |
| `pcl-next/Pages/PageLaunch/PageLoginMsSkin.xaml.cs` | 检查正确的 Microsoft 登录 loader；限定鉴权重试次数并处理不完整响应 |
| `pcl-next/Pages/PageLaunch/MySkin.xaml.cs` | 修正披风状态 getter；释放非缓存皮肤位图及裁剪位图；对披风响应结构及认证重试做边界处理 |
| `pcl-next/Pages/PageLaunch/PageLoginLegacy.xaml.cs` | 模板部件查找结果判型后读取光标位置 |
| `docs/bugscan/J1-下载流水线.md`、`J2-实例页.md`、`J3-设置与其他页.md`、`G2-登录皮肤.md`、`J4-散页登录资源.md` | 标记实际修复项及验证状态；保留不可达除零项结论 |
| `docs/bug排查-Windows-dev6.md` | 更新本轮修复摘要与验证结论 |
| `docs/经验与踩坑.md` | 追加 UI 生命周期、路径前缀及迁移事件语义的经验 |

明确不做：改动其他平台仓库；复活已下线的百宝箱/联机功能；重写五个下载详情空桩；处理 bugscan 中无触发路径的观察项、纯命名项或其他模块的独立审查项；修改版本号、构建配置、发布配置。

> 现状复核：`PageOtherTest.xaml.cs` 当前已为下线功能显示提示，不需要重复修；`PageResource.Load_OnFinish` 已处理空结果，不需要重复修；五个下载详情空桩仍是有意的 XAML 容器，仅保护其调用方。

## 5. 改动要点

- 路径截取统一在规范化后验证目录边界，避免 `Substring(root.Length)` 截掉非子路径，也避免大小写/分隔符差异造成误判；非根路径按既有错误处理策略报告。
- 可空 `Task.Input`、`Content`、`Parent`、`Child`、控件模板部件、动态选中项和加载输出在操作前判定；导航/刷新对象未就绪时不触发不完整操作。
- 对远程 JSON 缺字段、错误类型或空响应给出可读的失败处理，保持取消与超时语义，不新增成功形态的默认数据。
- Auth/Nide 对同一输入只挂接一次处理器，同时完成历史两个 `TextChanged` 处理器的作用；密码列表索引仅在范围内读取。
- 皮肤鉴权重试设置有限次数；超过上限进入现有错误提示/收尾路径，确保 `IsChanging` 等状态在 `finally` 中恢复。
- 下线页面空桩只显示功能不可用提示，不返回虚假的成功或实现新的功能。

## 6. 回归影响面

- 下载：实例组件修改、Fabric/LiteLoader 条目生成、资源列表状态重置、异常版本路径。
- 实例与设置：冷启动首次页面切换、无实例/页面未初始化时 RAM 刷新、异常目录访问、主页/音乐刷新。
- 登录与皮肤：Auth/Nide 账号输入和密码记忆、Microsoft 设备码登录轮询、Microsoft/自定义账号皮肤与披风列表。
- 帮助：加载失败、取消后搜索、初始化失败后刷新。
- 资源详情：目标详情页尚未懒加载。
- 测试优先复用现有 `PCL.Download.Tests`；纯 UI 事件/生命周期以项目构建及逐路径代码审查验证，不新增依赖 GUI 环境的脆弱测试。

## 7. 验证步骤

1. 针对修改前基线已通过：`dotnet build pcl-next/pcl-next.csproj -c Release -r win-x64 --self-contained -p:PclChannel=Dev`。
2. 修改后运行同一 Windows 项目级构建；确认无错误且输出位于本仓库 `pcl-next/bin-net10/Dev/<版本名>-<构建号>/win-x64/`。
3. 运行 `dotnet test PCL.Download.Tests/PCL.Download.Tests.csproj -c Release -r win-x64 -p:PclChannel=Dev`；若工程不支持此 RID/渠道参数，改用其项目既有的有效测试命令并记录原因。
4. `git diff --check`；复核每项修复的实际调用链、异常可见性和未修改文件列表。
5. 不做发布、归档或平台间同步；提交仅包含本计划、Windows 源码、相关缺陷记录和经验台账。

### 本轮验证结果

- 标准 Windows Release 构建成功：0 错误、30 警告；产物路径核验通过。完成最后一处 UI 空值守卫后，因运行中的 `pcl-next.exe` 锁定该目录，标准构建无法覆盖 apphost；最终 `Compile` 目标构建成功，0 错误。
- 输出路径核验通过：`pcl-next/bin-net10/Dev/2.100.0.6-0.87/win-x64/`。
- `PCL.Download.Tests`：568 通过、0 失败、0 跳过。
- `git diff --check` 在收尾阶段复核；未结束或修改运行中的用户进程；无平台间同步、发布或用户目录改动。

## 8. 回滚方案

- 若页面行为回归，优先仅撤销对应逻辑块并保留其他独立修复。
- 若需要回滚已提交代码，按 `AGENTS.md` §六另写正式回退报告；不重置或改写已推送历史。
- 不覆盖 `ReleaseArtifacts`，不改动用户游戏目录、配置或运行资源。
