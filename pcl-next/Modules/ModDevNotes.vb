''' <summary>
''' 开发阶段提示与开发日志（内置，每版本硬编码）。
'''
''' 与《分支与发布规范》§5.4 的 GitHub ReleaseNotes 互补，二者来源一致、侧重不同：
''' - GitHub ReleaseNotes：面向用户，讲清「改了什么、为什么改」，按 §5.4 的六章结构撰写；
''' - 本模块开发日志：面向开发者与内测用户，更侧重底层实现细节（架构迁移、并发模型、
'''   缓存策略、协议/序列化、兼容性兜底等），是 ReleaseNotes 的「技术内参」。
'''
''' 触发规则（由 FormMain 的 RunCountSub / UpgradeSub 调用 ShowDevNotes 统一触发）：
''' 1. 仅在「开发阶段渠道」弹出：dev / slush / alpha / beta（即 Not ChannelIsOfficial）。
'''    RC / 正式版 / SP（ChannelIsOfficial）不显示——发行版走 GitHub ReleaseNotes。
''' 2. 触发时机：首次启动（SystemCount = 1）与版本升级（跨 VersionCode）各一次。
''' 3. 开发日志需经用户「确认查看」后展示（MyMsgBox 的 确定 即视为同意）。
'''
''' 编写规则（开发人员完成开发后自行写入，每版本一条）：
''' - DevStageNotice：说明本版本的功能限制（哪些特性不可用 / 已知缺陷 / 使用注意）。
'''   返回空字符串时调用方跳过该弹窗。
''' - DevChangelog：本版本的底层实现变更，按「重写 / 优化 / 修复」分类，
'''   突出对用户不可见但影响行为 / 性能 / 稳定的技术细节。
'''   返回空字符串时调用方跳过该弹窗。
''' - 必须按 VersionCode 分支（与 ModBase.VersionCode 对应）。新增版本时在 Select Case 里追加，
'''   不要删除旧条目（历史版本升级时仍需读取对应版本的内容）。
''' - 本日志比 GitHub ReleaseNotes 更偏底层；用户侧可见的变更请同时写进 ReleaseNotes。
''' </summary>
Public Module ModDevNotes

    ''' <summary>
    ''' 开发阶段提示：说明本版本的功能限制。仅在开发阶段渠道、首次启动 / 版本升级时弹出。
    ''' 为空字符串时调用方跳过弹窗。
    ''' </summary>
    Public ReadOnly Property DevStageNotice As String
        Get
            Select Case VersionCode
                Case 5000 ' dev 2.100.0.1
                    Return "你正在使用 PCL 的开发版（dev）。本项目仍处于用 C# 重写 PCL 的过程中，" &
                           "主程序以 VB 为主、下载服务 C# 化尚在推进，功能可能随时变动、存在已知缺陷。" & vbCrLf & vbCrLf &
                           "本版本已知限制：" & vbCrLf &
                           "- 部分模块仍沿用旧 PCL2 实现，行为可能与最终版不一致；" & vbCrLf &
                           "- 下载 / 整合包等链路仍在迁移，遇到异常请反馈；" & vbCrLf &
                           "- 配置与存档结构可能在后续版本调整，建议定期备份。"
                Case Else
                    Return ""
            End Select
        End Get
    End Property

    ''' <summary>
    ''' 开发日志：本版本的底层实现变更，侧重实现细节。仅在开发阶段渠道、首次启动 / 版本升级时展示。
    ''' 为空字符串时调用方跳过弹窗。
    ''' </summary>
    Public ReadOnly Property DevChangelog As String
        Get
            Select Case VersionCode
                Case 5001 ' dev 2.100.0.2
                    Return "· 修复：渠道条件编译常量不一致——PclChannelConstant 与代码判断的" &
                           " PCL_CHANNEL_* 不匹配，除 dev 外的所有渠道都会被编成 dev。" &
                           "已对齐并新增 PclChannelGuard 强制重编校验（渠道变更时自动清理旧程序集）。" & vbCrLf &
                           "· 修复：代号改名收尾（pc→rc），发布输出目录按渠道隔离到 bin-net10/<渠道>。" & vbCrLf &
                           "· 本构建起包含 v2.100.0.1-dev.1 标签之后的全源连接挂起静默卡死修复（真机 NeoForge 复现）。"
                Case 5000 ' dev 2.100.0.1
                    Return "· 重写：主程序运行时由 .NET Framework 迁移到 .NET 10，启用新的应用模型与异步调用栈。" & vbCrLf &
                           "· 优化：下载服务完成 C# 化并接入主程序，采用多源并发、分片续传、断点续传与一致性校验。" & vbCrLf &
                           "· 修复：社区资源加载器在 .NET Core 下全部失败——需显式注册 GBK 等代码页（Encoding.RegisterProvider）。" & vbCrLf &
                           "· 修复：命中 HTTP 缓存的响应整体失败——缓存响应头残留 Transfer-Encoding，需在落盘前剥离。" & vbCrLf &
                           "· 修复：旧版 HTTP 缓存目录不可读，切换到 Cache/HttpV2 并做兼容回退。" & vbCrLf &
                           "· 修复：帮助页加载报未知类型 MyCard——内联 XAML 的 assembly= 引用漏改 10 处。" & vbCrLf &
                           "· 修复：单例检查与自动更新残留清理遗漏更名后的 pcl-next.exe。" & vbCrLf &
                           "· 修复：全源连接挂起时的静默卡死——连接超时转义为明确 TimeoutException，新增 MinConnectTimeoutMilliseconds 并补换源留痕日志（真机 NeoForge 复现）。"
                Case Else
                    Return ""
            End Select
        End Get
    End Property

End Module
