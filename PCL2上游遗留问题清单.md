# PCL2 上游仓库遗留问题清单

> **排查对象**：`D:\bing\MC\pcl_x\PCL2`（对外名 pcl-next，内部代号 NeoPCL）中**继承自上游仓库**的遗留问题。
> **上游**：`github.com/Meloong-Git/PCL`（原 `Hex-Dragon/PCL2`）；本仓库基线为上游 **2.99.0.1 正式版**（commit `93ede5d`，2026-10-25 同步）。
> **排查时间**：2026-11-01。排查方式：源码扫描（TODO/注释/兼容 hack/硬编码）、上游 GitHub Issues 快照、既有文档交叉核对（《PCL2源码分析报告》§13、《迁移计划》§4.2、dev.2 发布说明「已知问题」）。
> **标注约定**：「✅已核实」= 本地代码中确认存在并给出 `文件:行号`；「⚠️上游报告」= 来自上游 Issues，未逐一本地复现但基线同源、大概率存在；「已处置」= 本分支已解决，列出以防重复排查。

---

## 0. 一页速览

| 类别 | 数量 | 严重度分布 |
|---|---|---|
| 一、上游未修复的 GitHub Issues | 26 个 open（Bug 15 + 优化/功能 11） | 🟥高 6 / 🟨中 7 / 🟩低 8 |
| 二、代码内嵌 TODO / FUTURE | 11 处 | 均为 P2 以下 |
| 三、已核实的代码缺陷 | 4 项 | P1×2 / P2×2 |
| 四、技术债与代码质量 | 13 项 | P2~P3 |
| 五、net48→net10 平台残留 | 7 项 | P1×1 / P2×4 / P3×2 |
| 六、许可与合规义务 | 6 项 | **约束性条款（违反即违约）** |
| 七、外部服务依赖 | 8 项 | P1×2 / P2×4 / P3×2 |
| 八、仓库与工程残留 | 5 项 | P2×2 / P3×3 |

**结论**：上游 2.99.0.1 的遗留问题可分三层——
① **活跃缺陷**（GitHub Issues 里修了 PR 但未合并 / 待确认的 bug，本仓库代码同样命中，其中 4 项已逐行核实）；
② **结构债**（`On Error Resume Next`×12、127 个 issue 编号 workaround、硬编码常量、兼容 shim）——随 C# 重写（Phase 4/6）自然消解的占多数，不建议单独提前修；
③ **约束与依赖**（许可义务 L1–L5、上游服务器断供、第三方主页源）——不是「修掉」而是「必须带着走」或「已由本分支下线」。

---

## 一、上游未修复的 GitHub Issues（继承的缺陷）

数据快照：`github.com/Meloong-Git/PCL/issues`，2026-11-01。本仓库基线 2.99.0.1 与上游同步于 2026-10-25，下列 issue 均晚于或跨越该同步点，**本地代码基本同源**。上游已挂 PR 的条目修复后需要评估是否回灌（《分支与发布规范》§3.4）。

### 1.1 主链路（启动 / 下载 / 安装）

| # | 标题 | 上游状态 | 本地核实 | 影响 |
|---|---|---|---|---|
| **#9440** | 按下关闭游戏按钮后再次启动 Minecraft 流程会被启动器取消 | 处理中 🟥高（PR #9444） | ✅已核实：`ModLaunch.vb:2106-2111` 启动完成前 `Loader.IsCanceled` 即 `GameProcess.Kill()`；取消状态残留会掐断下次启动 | 启动主链路 |
| **#9533** | Java 下载列表获取失败后直接读 `LastResult`，抛 `InvalidOperationException`（「从未成功完成过」） | 等待确认 | ✅已核实：`ModJava.vb:410` `DownloadListWorker.LastResult` 无前置 `HasSucceeded` 判断；`MeloongCore/Shared/General/Worker/RedoableWorker.cs:68-76` 在未成功时抛异常 | Java 自动下载失败时报错劣化 |
| **#9501** | 启动游戏后启动器的启动状态结束速度变慢 | 需要社区复现（PR #9518） | ⚠️上游报告：涉及 `ModLaunch.vb:2129 McLaunchWait`（等待游戏窗口出现） | 体验 |
| **#9499** | 修改版本时，MC 版本列表扫描会尝试访问已失效的旧版本目录 | 处理中 🟥高（PR #9500） | ⚠️上游报告：涉及 `ModMinecraft.vb` 版本扫描 | 修改版本入口报错 |
| **#9475** | 在游戏下载完毕时删除旧版本会创造幽灵版本且刷新界面无效 | 处理中 🟩低（PR #9477） | ⚠️上游报告 | 版本列表脏数据 |
| **#9404** | 无法启动 13w18a 和 13w18b | 处理中 🟩低（PR #9406） | ⚠️上游报告（极旧快照版本） | 边缘版本 |

### 1.2 资源 / 整合包 / Mod 管理

| # | 标题 | 上游状态 | 本地核实 | 影响 |
|---|---|---|---|---|
| **#9412** | `ModModpack.vb` 第 280 行应为 `fileName`（JSON 字段大小写） | 处理中 🟥高（PR #9420） | ✅已核实：`ModModpack.vb:285` 用 `ModJson?("FileName")`，CurseForge manifest 字段为 `fileName`，Newtonsoft JObject 默认区分大小写 → 判空失效 | 整合包资源类型误判 |
| **#9426** | 搜索 Mod 并筛选具体 MC 版本时，错误列举该版本不支持的加载器 | 受阻碍 | ✅已核实：`ResourceSearcher.vb:88` CF 侧 `ModLoaders.Flags.First` 只取第一个加载器（Modrinth 侧多选），与《源码分析报告》§13 重合 | 搜索筛选精度 |
| **#9360** | 多个 Mod 并发更新时可能因更新的 Mod 相同导致替换失败 | 处理中 🟨中（PR #9421） | ⚠️上游报告：`ModDownloadLib.vb` Mod 更新流水线 | 更新失败 |
| **#9374** | 导入 CurseForge 整合包时，可选文件弹窗可能完全不显示 Mod 名称 | 处理中 🟨中（PR #9410） | ⚠️上游报告 | 可选文件选择 |
| **#9556** | 模组加载器安装器下载功能 | 优化·新提交 | ⚠️上游报告（功能缺口，非 bug） | — |
| **#9554** | 能否支持识别并使用 Modrinth APP 的目录 | 新功能·等待确认 | ⚠️上游报告 | — |

### 1.3 UI / 交互

| # | 标题 | 上游状态 | 本地核实 | 影响 |
|---|---|---|---|---|
| **#9343** | 刚打开 PCL 时，主页替换标记里的玩家名字和当前版本无法加载 | 处理中 🟨中（PR #9419） | ⚠️上游报告：`PageLaunchRight.xaml.vb:137/249 ArgumentReplace` | 主页变量 |
| **#9370** | 管理员权限运行时，拖入完整路径 ≥260 字符的文件被静默截断 | 处理中 🟨中（PR #9384） | ⚠️上游报告：`FormMain.xaml.vb:361+` 拖拽处理链 | 长路径 |
| **#9482** | 在游戏过程中若窗口变化，追踪的窗口可能跟丢 | 处理中 🟨中（PR #9509） | ⚠️上游报告：`ModWatcher.vb` | 窗口追踪 |
| **#9495** | 启动日志显示框有输出时多渲染出一行空白 | 处理中 🟩低（PR #9496） | ⚠️上游报告 | 外观 |
| **#9448** | 如果皮肤为空白，MC 显示纯黑而启动器显示透明 | 处理中 🟩低（PR #9463） | ⚠️上游报告 | 外观 |
| **#9369** | 特定情况下第一次点击任务管理按钮会短时切出 TaskManager | 处理中 🟩低（PR #9375） | ⚠️上游报告 | 体验 |
| **#9345** | 部分刷新按钮缺少提示反馈 | 处理中 🟨中（PR #9346） | ⚠️上游报告 | 体验 |
| **#9536** | 下载文件导向 bug | 等待提交者 | ⚠️上游报告：`ModDownloadLib.vb` 多处「另存为」（:119/:695/:1393/:1542） | 另存为路径 |
| **#9559** | 无法修改常规版本 | 新提交 | ⚠️上游报告（最新，10-26） | 修改版本 |
| **#9558** | 建议优化 Mod 更新时的一些小体验 | 优化·等待提交者 | ⚠️上游报告 | — |
| **#9524** | 将 1.8.9 加入社区资源下载页的预设版本列表 | 优化·等待确认（PR #9525） | ⚠️上游报告 | — |
| **#9416** | 添加 "Hero Cape" "Twisted Cape" 披风的翻译 | 优化 🟥高（PR #9417） | ⚠️上游报告 | 翻译 |
| **#9371** | 在常见问题上添加 Smart Application Control 阻止启动的解决方案 | 优化 🟥高 | ⚠️上游报告（帮助文档，Help.zip 资产） | 帮助 |
| **#9154** | Windows 7 无法连接 CurseForge 的资源 | open（22 评论） | ⚠️上游报告；注：net10 已不支持 Win7，本分支此 issue 天然失效 | — |

> **待办（建议）**：Phase 4/6 冻结前，把 🟥高 的 6 项（#9440 / #9499 / #9412 / #9426 / #9416 / #9371）与 🟨中 项按「是否触碰待重写模块」分流——触碰 `Resource*/ModDownloadLib` 的交给 C# 重写顺带解决，纯 VB UI 的可抄上游 PR patch。

---

## 二、代码内嵌的 TODO / FUTURE（11 处）

| 位置 | 内容 | 性质 |
|---|---|---|
| `FormMain.xaml.vb:813` | TODO：和 `FrmSetupUI` 解耦 | 架构耦合 |
| `FormMain.xaml.vb:960` | TODO：**联机复活赛**（`Return` 短路了剪贴板自动加联机房间，其后为死代码） | 功能被上游主动停摆 |
| `ModModpack.vb:377` | TODO：改为「一个版本名一个字典」存储，不要用 `Unknown` 键 | 模型缺陷 |
| `LocalResourceLoaders.vb:91` | TODO：添加「信息获取中」提示 | UX |
| `ModDownloadLib.vb:1995` | TODO：提交前显示文件修改列表，询问覆盖或作为新版本安装 | UX |
| `ModDownloadLib.vb:2135` | TODO：自动选择安装成功的版本 | UX |
| `PageInstanceSetup.xaml.vb:327` | TODO：拖拽改变第三方登录时版本列表缓存可能不更新（`OnVersionServerLoginChanged` 只覆盖按钮路径） | 潜在 bug |
| `PageLinkMain.xaml.vb:435` | TODO：`DisableReason` 可能不会显示错误原因 | 错误呈现 |
| `MeloongCore/Shared/General/Progress.cs:203` | TODO：添加平缓过渡 | 动画 |
| `MeloongCore/Shared/Utils/FileUtils.cs:531` | TODO：重构此函数，使它变成 void 并抛出异常 | API 造型 |
| `ModNet.vb:877` | FUTURE：下载引擎重做——按源平均连接时间与线程速度决定是否开多线程（当前靠域名白名单 + 固定碎片阈值） | 引擎启发式 |

---

## 三、已核实的代码缺陷（本次排查新增定位）

| # | 缺陷 | 位置 | 说明 | 建议 |
|---|---|---|---|---|
| L1 | Java 列表失败后读 `LastResult` 抛「从未成功完成过」（= #9533） | `ModJava.vb:410` + `RedoableWorker.cs:68` | `WaitIfRunning` 后直接取 `LastResult`；列表拉取失败（网络/JSON 损坏）时异常掩盖真实原因 | 加 `If Not HasSucceeded Then` 显式报「获取 Java 列表失败」；可与上游 PR #9518 对齐 |
| L2 | 整合包 `FileName`/`fileName` 大小写失配（= #9412） | `ModModpack.vb:285` | `ModJson?("FileName")` 对 `fileName` 字段恒为 Nothing，jar 后缀判断失效 → 资源类型归类退化 | 改 `ModJson?("fileName")`（或两者兜底） |
| L3 | CF 加载器筛选退化为单选（= #9426 + 报告 §13） | `ResourceSearcher.vb:88` | `ModLoaders.Flags.First` 只取第一个；Modrinth 侧 `:106-107` 支持多选 | Phase 3 资源子系统 C# 化时统一为多选筛选 |
| L4 | `NetRequestByClientRetry` 的 `BackupUrl` 参数从未使用 | `ModNet.vb:43-44` | XML 注释承诺「失败换用备用 URL」，函数体无此逻辑；真正备用源走 `NetFile.Sources` | 删除参数或实现语义；C# 引擎侧勿复制此签名 |

---

## 四、技术债与代码质量（上游代码风格遗留）

| 项 | 位置 / 数据 | 影响 | 处置倾向 |
|---|---|---|---|
| `On Error Resume Next` ×12 | `Application.xaml.vb:188`、`MyMsgInput:93`、`MyMsgSelect:100`、`MyMsgText:104`、`FormMain:683/707`、`ModNet:1301/1373`、`ModValidate:270`、`ModMain:965/976`、`MyMsgLogin:67` | 吞异常、难定位 | C# 重写模块自然消除；VB 存量不单独改 |
| 127 个 GitHub issue 编号内嵌注释 | 全工程 152 处引用（高频：#5195×3、#4505×3、#8286/#6664/#5628/#4057…） | 每处都是一个历史 workaround，改动需回归对照 | 保留为行为文档；重写时按注释逐条迁移语义 |
| `GlobalSuppressions.vb` 5 条全局抑制 | 全部 `Justification:="<挂起>"`（IDE0060/0058/0044/1006/0010） | 样式检查被整体关掉 | C# 化后不再继承 |
| Obsolete 兼容属性 | `MyCard.vb:267 IsSwaped`（拼写错误保留）、`MyHint.xaml.vb:56 IsWarn` | API 包袱 | 重写后不带 |
| 过时 API | `ModMusic.vb:302-304` `#Disable Warning BC40008`（NAudio `Volume`） | 依赖升级时需换 `WaveVolume` | 依赖现代化（Phase 5 遗留） |
| 硬编码超时 | `ModNet.vb:52-64` 10s/30s/4s 三级重试 | 弱网/高延迟环境不灵活 | C# 引擎已参数化（`MinConnectTimeoutMilliseconds`），迁移后收敛 |
| 线程上限 | `Settings.vb:126 ToolDownloadThread=63` +1 = 64（`ModNet.vb:314-316`） | 某些源会限流（已靠 BMCLAPI 降频缓解） | 保留策略，属产品参数 |
| 失败熔断阈值靠常数 | `ModNet.vb:1105` `FailCount >= NetTaskThreadLimit.Clamp(5, 30)` | 不同网络下熔断过早/过晚 | C# 引擎已有 `DeliveredBytes` 死源禁用新策略（dev.2 后），对比验证后废弃旧式 |
| CF CDN 硬编码 | `ResourceVersion.vb:135` `edge.forgecdn.net` | CurseForge 已迁新 CDN，fallback 可能失效 | Phase 3 修复（P0 补强「CF 优雅降级」内） |
| 版本清单不落盘 | `ModDownload.vb:233+` `DlClientListMojangMain` 每次冷启动拉 manifest | 冷启动网络依赖 | 保持（CacheCow 缓存部分缓解）；勿在 dev 阶段顺手改 |
| 动画线程手动调度 | `ModAnimation.vb:805-851` + `Thread.Sleep(1)` 帧率控制 | 与渲染不同步、极端撕裂；精度约 30-60 FPS | UI 重写时换 CompositionTarget |
| 硬编码颜色 | `MyCard.vb:70` `Color.FromArgb(245,255,255,255)` | 深色主题不协调 | 主题化时修正 |
| 页面状态机裸抛异常 | `MyPageRight.vb:204/258/292/310/328` | 非法迁移直接崩 | UI 重写时改为防御式 |

---

## 五、net48 → net10 平台残留（迁移尾巴）

| 项 | 位置 | 说明 | 严重度 |
|---|---|---|---|
| **.NET Framework 4.8 检查已成误导** | `Application.xaml.vb:31-35` + `:210-218 OldEnvironmentAssert` | 仍在读 `NDP\v4\Full` 注册表，低于 528040 就弹「请安装 .NET Framework 4.8」并跳转 net48 离线安装包下载页——**net10 自包含单文件根本不需要 net48**，误报会误导用户 | **P1，建议尽快移除**（同时清 `ModBase.vb:8` 上游「NetFramework4.8」断言残留） |
| Win7 回退分支（#8519） | `Application.xaml.vb:107-109` | `OSVersion < 6.2` 时启用 `Ssl3 \| Tls \| Tls11`——net10 不支持 Win7，分支不可达；且 Ssl3/Tls11 不安全 | P3，删 |
| `ServicePointManager` 全局调参 | `Application.xaml.vb:103-106` | `DefaultConnectionLimit=10000` 等 net48-era 设置，net10 HttpClient 不完全走此路径 | P3，随网络层重写清理 |
| CA1416 平台兼容警告 ×2442 | 构建输出（上游 VB 调 Windows 专有 API） | 不影响产物；dev.2 发布说明已列为技术债 | P3，随 C# 重写消除 |
| `libwebp64.dll` 解包 hack | `Application.xaml.vb:221-233` | 内嵌 dll 释放为 `libwebp.dll` 供 Imazen.WebP P/Invoke | 保留（依赖如此），依赖现代化时换 ImageSharp 可消除 |
| 依赖版本滞后 | `pcl-next.vbproj`：`System.Drawing.Common 9.0.0`、`System.Text.Encoding.CodePages 9.0.0` | 主工程已是 net10，这两个包停在 9.x | P2，随 Phase 5 依赖现代化统一升 10.x |
| MeloongCore submodule 分叉 | `.gitmodules` | 上游 `Meloong-Git/MeloongCore` 不含 net10 适配提交（`82931b2`），完整历史在 `skchuowei/pcl_next` 的 `meloongcore` 分支承载 | P2，长期需上游合并或接受分叉 |

---

## 六、许可与合规遗留（约束性条款，非 bug）

依据仓库根 `LICENCE`《PCL 分发有限许可》+《PCL 存储库合理使用指南》与《迁移计划》§1.1 的义务表：

| # | 义务 | 对本仓库的要求 | 现状 |
|---|---|---|---|
| L1 | 明确声明第三方二次创作 | 关于页/README 明示「基于 PCL2 的第三方修改版」 | ✅ 已满足（`PageOtherAbout.xaml`，commit `661392c`） |
| L2 | 名称须以 `Plain Craft Launcher (PCL)` 开头 + 第三方后缀 | 对外名 `PCL (.NET 10 重写)` | ✅ 已满足 |
| L3 | 首位署名龙腾猫跃（Hex-Dragon）+ 赞助链接 + 公开源码 | 关于页上游署名区块 + 赞助按钮 | ✅ 已满足（`PageOtherAbout.xaml:62-95`） |
| L4 | 保留 `McLaunchPrecheck` 正版购买 / 赞助劝导 | **不得移除** `ModLaunch.vb:221-245`（启动 N 次弹窗 + 赞助链接） | ✅ 保留中；⚠️ 恢复 MC 下载时必须继续保留 |
| L5 | 主色相须为蓝色、无赞助解锁类功能 | 主题默认蓝、无付费功能 | ✅ 满足 |
| — | 用户协议外链 | `FormMain.xaml.vb:469` 指向石墨文档 `shimo.im/docs/rGrd8pY8xWkt6ryW` | ⚠️ 外部文档，链接失效即协议不可达；建议本地化归档 |

**附带裁剪**（上游开源版既定事实，非缺陷但影响能力）：
- `ModSecret.vb:1`「由于包含加解密等安全信息，本文件中的部分代码已被删除」——CDN 签名、鉴权实现缺失，`SecretCdnSign`/`SecretHeadersSign` 已是壳。
- `ModSecret.vb:6-8`：`VersionBranchMain="OpenSource"`、注册表隔离 `PCLDebug`，与正式版配置互不干扰。

---

## 七、外部服务依赖遗留

| 依赖 | 位置 | 状态 | 处置 |
|---|---|---|---|
| **上游更新服务器** | 原更新器整条链路 | **已断供**（本分支无更新源） | ✅已处置：`b09602c` 下线自动更新，「检查更新」改开 GitHub Releases（`ModSecret.vb:150-152`） |
| **遥测上报** | 原 `ModMain.Telemetry` + 10 处调用 | 开源版 `ClsBaseUrl` 本为空串，从未真正发送 | ✅已处置：`7a9cab2` 连根移除，法律信息页改为本项目信息 |
| **联机服务器（easytier）** | `PageLinkMain.xaml.vb:428-431/616` | `VersionBranchMain <> "Official"` 直接抛「开源版无法联网获取配置」→ **联机功能整体不可用**；剪贴板自动加房亦被 `FormMain.xaml.vb:960` 短路（「联机复活赛」） | P2 决策项：要么接入自建配置源，要么在 UI 明示「开源版不提供联机」 |
| **24 个第三方主页预设源** | `PageLaunchRight.xaml.vb:55-133` | 指向 `lingyunawa.top`、`sodamc.com`、`kaphia.top`、`mcimirror.top` 等个人/社区服务器（含 `gh-proxy.com` 代理 GitHub raw）；远程拉取 XAML 渲染 | P1 风险：源失效即主页空白；**供应链风险**（远程 XAML 执行，靠 `ModBase.vb:1148-1152` 类型黑名单兜底：禁 WebBrowser/Frame/MediaElement/ObjectDataProvider/XamlReader/Window/XmlDataProvider/SettingService + Code/FactoryMethod/Static）。建议默认关闭远程主页或改白名单签名 |
| Referer 伪造 | `ModSecret.vb:37` | `http://{VersionCode}.open.pcl2.server/`（上游服务器域名，已无对应服务） | P3，清理或改本项目域名 |
| Nide8 通行证 / LittleSkin 硬编码 | `ModMinecraft.vb:1787-1809`、`PageInstanceSetup.xaml.vb:377-381` | 第三方登录端点写死 | 保留（服务仍在线）；属产品配置 |
| **反馈链接指向上游** | `ModMain.vb:970` | `Feedback()` 打开 `Meloong-Git/PCL/issues`——用户反馈会落到**上游**仓库 | **P1，应改为 `skchuowei/pcl_next/issues`** |
| 赞助链接 | `ModLaunch.vb:244`、`FormMain.xaml.vb:318`、`PageSetupUI.xaml.vb:381` | `meloong.com/afd/a/LTCat`（上游作者） | 保留（L3/L4 义务）；关于页已注明是「上游 PCL 作者」 |

---

## 八、仓库与工程残留

| 项 | 位置 | 说明 | 建议 |
|---|---|---|---|
| `最新正式版.zip`（2.6 MB）入库 | 仓库根（`git ls-files` 可见） | 上游把编译产物直接提交进 git | P3，从索引移除（历史保留），改 Release 资产 |
| `patches/` 15 个二进制补丁 | `patches/Release 2.12.6.1~2.99.0.0 *.patch` | 上游自动更新器的增量补丁（MD5 命名，二进制格式）；更新器已下线 | P3，归档说明即可，勿删（SP 机制仍可能用补丁格式，见《分支与发布规范》§4.3） |
| CI 分支名 `pc` 残留 | `.github/workflows/build.yml:10`、`release.yml:88/111` | `pc → rc` 改名收尾未净：build 触发分支列表仍有 `pc`（无 `rc`）；release.yml 渠道命名 `default { 'pc' }`、注释「正式版（pc）」 | **P2，修**：`build.yml` branches 改 `rc`；`release.yml` 命名/注释统一 `Rc`/`rc`（`^rc$`→`Rc'` 已在 build.yml:67 对齐过一半） |
| 版本常量注释与机制不符 | `ModBase.vb:8`「下列版本信息由更新器自动修改」 | 实际由 `scripts/Set-VersionInfo.ps1` 按标签写入 | P3，改注释 |
| 帮助文档资产 | `pcl-next/Resources/Help.zip` | 上游 `LTCatt/PCL2Help` 打包；#9371（SAC 帮助条目）改动在此资产上游侧 | 随上游同步更新 |

---

## 九、已由本分支处置的上游问题（防重复排查）

| 上游遗留 | 处置 | commit |
|---|---|---|
| MC 下载被编译期常量 `RestrictMcDownload=True` 关死（合规裁剪） | 下沉为部署期配置 `ModDownloadPolicy`（`DownloadPolicy.ini` / 环境变量 `PCL_ALLOW_MC_DOWNLOAD`） | `5e79f02` 系列 |
| 自动更新依赖上游更新服务器 | 整体下线，改开 GitHub Releases | `b09602c` |
| 遥测上报（含法律信息页失真描述） | 代码连根移除 + 法律信息改本项目信息 | `7a9cab2` |
| 关于页把上游作者当本项目作者的隐患 | 上游署名 / 本项目信息分列两区块 | `661392c` |
| `DownloadFileReport.BytesDownloaded` 恒 0 | C# 引擎报告修复 | `17a5b81` |
| 全源连接挂起静默卡死（NeoForge 复现） | C# 引擎补日志 + 超时转义 + `MinConnectTimeoutMilliseconds` | `17708bd` |
| 渠道常量 `pc/rc` 混乱、非 dev 渠道被编成 dev | 渠道常量统一 `PCL_CHANNEL_*` + `PclChannelGuard` 强制重编 | `bf12b02` / `29432cf` |
| 整合包导出塞「下载来的正式版 PCL」 | 改为复制当前运行的启动器本体（dev 渠道不附带） | dev.1 起 |

---

## 十、处置优先级汇总

| 优先级 | 条目 | 理由 |
|---|---|---|
| **P1** | ① 移除 net48 检查（`OldEnvironmentAssert`）② 反馈链接改指本仓库③ 远程主页 XAML 供应链风险（默认关 / 白名单）④ #9440 启动取消残留 | 前两项是「用户被误导 / 反馈流失」，可立即改；③④ 触达主链路与安全面 |
| **P2** | CI `pc→rc` 收尾；#9533 / #9412 两处一行级修复；System.Drawing 9→10；联机功能去留决策；MeloongCore 分叉策略 | 小改动大收益 / 需拍板 |
| **P3** | Win7 分支、`ServicePointManager` 调参、`最新正式版.zip` 出库、`BackupUrl` 参数、注释订正、`patches/` 归档说明 | 无行为影响，随重写顺带 |
| **重写期消化** | `On Error Resume Next`×12、127 个 issue workaround、动画线程、状态机裸抛、CF CDN、熔断常数、TODO×11 | Phase 3–6 覆盖，不单独立项 |

> **代际清偿提醒**（《分支与发布规范》§5.4）：上述修复若在某 dev 版本落地，无论是否有用户提 issue，都必须写进该版本 ReleaseNotes 的「修复」小节。
