<#
.SYNOPSIS
    计算「构建小版本」（BuildSmall），并可生成编译期使用的 VB 源文件。

.DESCRIPTION
    规则见《分支与发布规范》§5.2.1：构建小版本 = 自永久锚点以来的提交数 × 0.01，
    写成 M.mm（mm 两位，满 100 进 1）。

    锚点是**固定不变的提交**（见下方 $AnchorSha），不是「最近一个发布标签」。
    这一点是刻意设计：若以最近标签为起点，打 dev.4 标签后编号会从 0.09 归零成 0.01，
    后提交的产物反而比先提交的小，追溯就失去意义。改用固定锚点后，
    编号随提交数单调递增，**任何后提交的产物编号一定大于先提交的产物**。

    2026-11-08 起三条平台线改为各自 `git init` 的独立仓库，旧锚点 SHA 在新历史里不可达。
    这类仓库由 `scripts\build-number.base` 提供「继承基数」：基线提交继承旧线编号，
    之后每个提交 +1；锚点可达的旧仓库不叠加基数。详见《分支与发布规范》§5.2.1。

    需要注意：M.mm 是显示形式，`10.00` 在字符串上小于 `2.00`，比较大小必须用
    BuildSmallNumber（等于提交数本身），不要直接比较字符串。

    用途有三处（§5.2.1 的「三处落点」）：
      1. 编译期：pcl-next.vbproj 的 PclBuildInfo 目标调用本脚本生成 obj\PclBuildInfo.g.vb，
         把编号写进 AssemblyInformationalVersion，并生成 PclBuildInfoGenerated 数据类；
      2. 提交期：scripts\Install-GitHooks.ps1 安装的 prepare-commit-msg 钩子调用本脚本，
         在提交信息末尾追加 `Build: <M.mm>`；
      3. 排查期：手工执行本脚本查看当前值。

.PARAMETER RepoRoot
    仓库根目录（含 .git 的那一层）。默认取脚本所在目录的上一级。

.PARAMETER OutFile
    指定后生成该路径的 VB 源文件（编译期使用）；不指定则只输出结果。

.PARAMETER Json
    以 JSON 输出全部字段，便于脚本调用方解析。

.PARAMETER CacheFile
    把当前编号写入该文件（单行，如 0.20），供 MSBuild 在**属性求值期**读取，
    用于拼产物目录名（bin-net10\<代号>\<版本名>-<构建号>\<RID>\）。

    为什么需要缓存文件：MSBuild 的属性求值发生在任何 Target 之前，那个阶段
    只有一组白名单内的属性函数可用，**不能执行 git 命令**，所以提交数无法在
    求值期直接算出。缓存把「运行期算出来的值」提前落到磁盘，求值期只读文件。
    刷新时机：scripts\Build.ps1 构建前、CI 构建前、以及 post-commit 钩子提交后。

    ⚠ 缓存必然有「过期窗口」：新克隆的仓库里没有它，此时回退为不含构建号的
    目录名；下一次构建（或任意一次脚本调用）会把它补上。这是有意取舍——
    宁可首刷少一段，也不要为了「每次都对」在求值期塞 git 调用而拖慢并搞脆构建。

.EXAMPLE
    .\scripts\Get-BuildSmallVersion.ps1
    2.27

.EXAMPLE
    .\scripts\Get-BuildSmallVersion.ps1 -Json

.EXAMPLE
    .\scripts\Get-BuildSmallVersion.ps1 -CacheFile .\pcl-next\obj\pcl-build.small
#>
[CmdletBinding()]
param(
    [string]$RepoRoot = (Split-Path -Parent $PSScriptRoot),
    [string]$OutFile,
    [string]$CacheFile,
    [switch]$Json
)

$ErrorActionPreference = 'Stop'

# 构建编号的永久锚点：v2.100.0.1-dev.1 的提交（本项目首个 dev 发布）。
# 刻意硬编码完整 SHA 而非写标签名——标签可以被 force 重建（dev.3 就重建过一次），
# 一旦锚点跟着后移，编号就会变小，「后提交的版本一定更大」立刻失效。
# 这与 git 的不可变性一致：只有改写历史才会让锚点消失，而那时编号本就该重新起算。
$AnchorSha = '85c17f13003322a4025e09379bb1ed2d852ea63c'

function Invoke-Git {
    param([string[]]$Arguments)
    $output = & git -C $RepoRoot @Arguments 2>$null
    if ($LASTEXITCODE -ne 0) { return $null }
    return (($output | Out-String).Trim())
}

# ---- 独立仓库的编号继承基数（2026-11-08 架构裁决）----
# 三条平台线各自 git init 后，硬编码锚点 SHA 在新历史里不可达，退化分支会把编号
# 重算成 0.0x，导致「后提交的产物编号反而更小」。每份仓库根的
# scripts\build-number.base 记录它从旧线继承的编号（锚点以来的提交数）：
#   PCL_NEXT_Windows = 65 (dev@463a558) / Linux = 71 (linux@439d516) / Android = 60 (android@a9c1643)
# 仅在退化分支生效：基线提交继承该编号，之后每个增量提交 +1；
# 锚点可达（旧仓库 / 完整历史克隆）时保持原语义，不叠加基数。
$BaseCount = 0
$baseFile = Join-Path $RepoRoot 'scripts\build-number.base'
if (Test-Path -LiteralPath $baseFile) {
    $rawBase = (Get-Content -LiteralPath $baseFile -Raw).Trim()
    if ($rawBase -match '^\d+$') { $BaseCount = [int]$rawBase }
}
$anchorMode = 'anchor'

# ---- 采集 git 信息（任何一步失败都退化为「无 git」的保守值，不阻断构建）----
$isRepo = (Invoke-Git @('rev-parse', '--is-inside-work-tree'))
if ($isRepo -ne 'true') {
    $anchor = 'nogit'
    $commits = 0
    $commit = 'nogit'
    $dirty = $true
}
else {
    # 锚点必须在当前历史里可达；浅克隆、镜像裁剪、或 2026-11-08 后的独立新仓库
    # 都取不到锚点，退化为「自首个提交起算 + 继承基数」。
    # 退化仍是严格单调递增，且不会让新仓库的编号低于它所继承的旧线编号。
    $resolvedAnchor = Invoke-Git @('rev-parse', '--verify', '--quiet', "$AnchorSha^{commit}")
    if ([string]::IsNullOrWhiteSpace($resolvedAnchor)) {
        $anchor = 'root'
        $anchorMode = 'root+base'
        $rootCount = [int](Invoke-Git @('rev-list', '--count', 'HEAD'))
        # 首个提交（代码基线快照）继承旧线编号，之后的增量提交每个 +1
        $commits = $BaseCount + [math]::Max(0, $rootCount - 1)
    }
    else {
        $anchor = $resolvedAnchor.Substring(0, 7)
        $commits = [int](Invoke-Git @('rev-list', '--count', "$resolvedAnchor..HEAD"))
    }

    $commit = Invoke-Git @('rev-parse', '--short=7', 'HEAD')
    if ([string]::IsNullOrWhiteSpace($commit)) { $commit = 'nogit' }

    # 只认「已跟踪文件的改动」：bin/obj 等已在 .gitignore 内，未跟踪的杂物不应污染编号
    $porcelain = Invoke-Git @('status', '--porcelain', '--untracked-files=no')
    $dirty = -not [string]::IsNullOrWhiteSpace($porcelain)
}

# ---- 计算编号：提交数 × 0.01，满 100 进 1 ----
$major = [int][math]::Floor($commits / 100)
$minor = [int]($commits % 100)
$buildSmall = '{0}.{1:00}' -f $major, $minor
$token = "$buildSmall.g$commit"
if ($dirty) { $token = "$token.dirty" }

# ---- 可选：把编号落盘成缓存，供 MSBuild 求值期读取（目录名用）----
# 放在 -Json / -OutFile 分支之前，确保任何调用方式都会刷新缓存。
if ($CacheFile) {
    $cacheDir = Split-Path -Parent $CacheFile
    if ($cacheDir -and -not (Test-Path $cacheDir)) {
        New-Item -ItemType Directory -Path $cacheDir -Force | Out-Null
    }
    $cached = ''
    if (Test-Path $CacheFile) {
        $cached = (Get-Content -Path $CacheFile -Raw).Trim()
    }
    if ($cached -ne $buildSmall) {
        # 不写 BOM：MSBuild 用 File::ReadAllText 读，BOM 会变成一个不可见前导字符
        [System.IO.File]::WriteAllText($CacheFile, $buildSmall + "`r`n", (New-Object System.Text.UTF8Encoding $false))
    }
}

if ($Json) {
    $result = [ordered]@{
        BuildSmall      = $buildSmall
        BuildSmallNumber = $commits
        Commits         = $commits
        Anchor          = $anchor
        AnchorMode      = $anchorMode
        BaseCount       = $BaseCount
        Commit          = $commit
        Dirty           = [bool]$dirty
        Token           = $token
    }
    ($result | ConvertTo-Json -Compress) | Write-Output
    return
}

# ---- 生成编译期源文件（内容未变则不写，避免无谓的重新编译）----
if ($OutFile) {
    $dirtyLiteral = 'False'
    if ($dirty) { $dirtyLiteral = 'True' }

    # 注意：不要用 here-string 或多行数组——PowerShell 5.1 在 LF 换行的脚本文件里
    # 会把这些多行字面量解析错（生成结果丢换行、被合并成一行）。逐行 += 拼接最稳。
    $nl = "`r`n"
    $generated = ''
    $generated += "' <auto-generated>" + $nl
    $generated += "'     由 scripts\Get-BuildSmallVersion.ps1 生成，请勿手工修改。" + $nl
    $generated += "'     规则见《分支与发布规范》§5.2.1：构建小版本 = 自固定锚点以来的提交数 × 0.01。" + $nl
    $generated += "' </auto-generated>" + $nl
    $generated += $nl
    $generated += "<Assembly: System.Reflection.AssemblyInformationalVersion(`"$token`")>" + $nl
    $generated += $nl
    $generated += "''' <summary>编译期生成的构建标识数据（编译前由项目目标生成，不纳入版本库）。</summary>" + $nl
    $generated += 'Friend NotInheritable Class PclBuildInfoGenerated' + $nl
    $generated += '    Private Sub New()' + $nl
    $generated += '    End Sub' + $nl
    $generated += $nl
    $generated += "    ''' <summary>构建小版本，形如 2.27（锚点以来第 227 个提交）。</summary>" + $nl
    $generated += "    Friend Const BuildSmall As String = `"$buildSmall`"" + $nl
    $generated += "    ''' <summary>构建小版本的数值形式（= 锚点以来的提交数）。比较新旧必须用它，不要比字符串。</summary>" + $nl
    $generated += "    Friend Const BuildSmallNumber As Integer = $commits" + $nl
    $generated += "    ''' <summary>构建时的提交短哈希（7 位）；无 git 时为 nogit。</summary>" + $nl
    $generated += "    Friend Const BuildCommit As String = `"$commit`"" + $nl
    $generated += "    ''' <summary>构建时工作树是否有未提交的已跟踪改动（有则不可用于发布）。</summary>" + $nl
    $generated += "    Friend Const BuildIsDirty As Boolean = $dirtyLiteral" + $nl
    $generated += "    ''' <summary>编号锚点的短哈希（永久固定，见脚本 $AnchorSha）。</summary>" + $nl
    $generated += "    Friend Const BuildAnchor As String = `"$anchor`"" + $nl
    $generated += "    ''' <summary>自锚点以来的提交数。</summary>" + $nl
    $generated += "    Friend Const BuildCommitCount As Integer = $commits" + $nl
    $generated += 'End Class' + $nl

    $dir = Split-Path -Parent $OutFile
    if ($dir -and -not (Test-Path $dir)) { New-Item -ItemType Directory -Path $dir -Force | Out-Null }

    $needWrite = $true
    if (Test-Path $OutFile) {
        $existing = Get-Content -Path $OutFile -Raw -Encoding UTF8
        if ($existing -eq $generated) { $needWrite = $false }
    }
    if ($needWrite) {
        # VB 编译器对无 BOM 文件的编码推断不可靠，这里显式写入 UTF-8 BOM
        [System.IO.File]::WriteAllText($OutFile, $generated, (New-Object System.Text.UTF8Encoding $true))
    }
}

Write-Output $buildSmall
