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

    2026-11-13 起构建标识扩展为四层（用户裁决「同号不同物就没意义」）：
      · BuildSmall      M.mm，提交阶梯号（算法不变，目录名仍只用它）
      · BuildCommit     HEAD 短哈希 / BuildCommitFull 完整哈希，「包含到哪个提交」
      · BuildContentId  SHA256(HEAD + 工作树改动 diff + status + 未跟踪源码) 前 12 位，
                        「这一份产物实际有哪些代码」；同一提交号下内容不同即不同
      · BuildUuid       UUID v4，与 ContentId 绑定缓存：内容变了才换，内容没变则复用，
                        保证一次构建里多个调用点 / 多个工程拿到同一个 ID
    AssemblyInformationalVersion 形如 0.93.g1a2b3c.c0f1e2d3a4b5[.dirty]。
    缓存 pcl-next\obj\pcl-build.uuid（obj\ 已被忽略），单行 "<uuid>|<contentId>"。

.PARAMETER ForceNewUuid
    即使内容指纹未变也强制生成新 UUID，用于「同一份代码重新出件」的场景。
    正常构建不要传，否则同一次构建的多个调用点会拿到不同 UUID。

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
    [switch]$Json,
    [switch]$ForceNewUuid
)

$ErrorActionPreference = 'Stop'

# 构建编号的永久锚点：v2.100.0.1-dev.1 的提交（本项目首个 dev 发布）。
# 刻意硬编码完整 SHA 而非写标签名——标签可以被 force 重建（dev.3 就重建过一次），
# 一旦锚点跟着后移，编号就会变小，「后提交的版本一定更大」立刻失效。
# 这与 git 的不可变性一致：只有改写历史才会让锚点消失，而那时编号本就该重新起算。
$AnchorSha = '85c17f13003322a4025e09379bb1ed2d852ea63c'

function Invoke-Git {
    param([string[]]$Arguments)
    # PowerShell 5.1 会把原生命令的 stderr 包装成 NativeCommandError；在
    # $ErrorActionPreference='Stop' 下，git 的 autocrlf 提示（例如
    # "LF will be replaced by CRLF"）会直接终止整个脚本。
    # 2026-11-13 实测：本脚本因此长期退出码 1，而 csproj 的 IgnoreExitCode
    # 把失败吞掉，导致 PclBuildInfo.g.cs 与构建号缓存从 11-12 起再未刷新。
    $prevPref = $ErrorActionPreference
    $ErrorActionPreference = 'SilentlyContinue'
    try {
        $output = & git -C $RepoRoot @Arguments 2>$null
        if ($LASTEXITCODE -ne 0) { return $null }
        return (($output | Out-String).Trim())
    }
    finally { $ErrorActionPreference = $prevPref }
}

# ---- 独立仓库的编号继承基数（2026-11-08 架构裁决）----
# 三条平台线各自 git init 后，硬编码锚点 SHA 在新历史里不可达，退化分支会把编号
# 重算成 0.0x，导致「后提交的产物编号反而更小」。每份仓库根的
# scripts\build-number.base 记录它从旧线继承的编号（锚点以来的提交数）：
#   PCL_NEXT_Windows = 65 (dev@463a558) / Linux = 71 (linux@439d516) / Android = 60 (android@a9c1643)
# 该文件存在即代表「独立新仓库」，一律按 root + 继承基数计，不再检查硬编码锚点是否可达：
# CI 用 fetch-depth:0 会把远端旧 tag 及其历史一并拉下，锚点 85c17f13 在 CI 里意外可达，
# 于是走锚点分支把编号算成 0.1x，而本地对象库没有旧 tag 走退化分支算成 0.7x，
# 同一 commit 的 CI 制品与本地归档编号对不上（2026-11-08 dev.6 首次发布实测）。
# 旧仓库没有这个文件，仍保持原锚点语义。
$BaseCount = 0
$hasBase = $false
$baseFile = Join-Path $RepoRoot 'scripts\build-number.base'
if (Test-Path -LiteralPath $baseFile) {
    $rawBase = (Get-Content -LiteralPath $baseFile -Raw).Trim()
    if ($rawBase -match '^\d+$') { $BaseCount = [int]$rawBase; $hasBase = $true }
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
    # hasBase 优先：独立新仓库即便因旧 tag 而能解析到锚点，也必须走继承基数口径
    if ($hasBase -or [string]::IsNullOrWhiteSpace($resolvedAnchor)) {
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

# ---- 内容指纹：这份产物实际包含哪些代码（2026-11-13）------------------------
# BuildSmall 只数提交，同一提交下 dirty 工作树反复编译会「同号不同物」：
# 0.90 能对应三份内容不同的 exe。ContentId 把 HEAD、已跟踪改动的 diff、
# status 与未跟踪源码的内容一起哈希，改动内容变了指纹必变；干净工作树则只由 HEAD 决定。
function Get-Sha256Hex {
    param([string]$Text)
    $bytes = [System.Text.Encoding]::UTF8.GetBytes($Text)
    $sha = [System.Security.Cryptography.SHA256]::Create()
    try { return (-join ($sha.ComputeHash($bytes) | ForEach-Object { $_.ToString('x2') })) }
    finally { $sha.Dispose() }
}

function Get-FileSha256Hex {
    param([string]$Path)
    try {
        $item = Get-Item -LiteralPath $Path -ErrorAction Stop
        # 大文件不读内容：指纹材料里记大小即可，避免编译期拖慢
        if ($item.Length -gt 2097152) { return "size=$($item.Length)" }
        $sha = [System.Security.Cryptography.SHA256]::Create()
        try {
            $stream = [System.IO.File]::OpenRead($Path)
            try { return (-join ($sha.ComputeHash($stream) | ForEach-Object { $_.ToString('x2') })) }
            finally { $stream.Dispose() }
        }
        finally { $sha.Dispose() }
    }
    catch { return 'missing' }
}

$headFull = 'nogit'
$diffText = ''
$statusText = ''
$untrackedEntries = @()
if ($isRepo -eq 'true') {
    $headFull = Invoke-Git @('rev-parse', 'HEAD')
    if ([string]::IsNullOrWhiteSpace($headFull)) { $headFull = 'nogit' }

    # 中文路径：core.quotepath=false + UTF-8 控制台编码，尽量让路径可解析。
    # 个别路径若仍解析不到文件，Get-FileSha256Hex 返回 missing，指纹依旧稳定，
    # 只是该未跟踪文件的内容变化不参与指纹（已跟踪文件由 diff 完整覆盖，不受影响）。
    $prevEnc = [Console]::OutputEncoding
    $prevPref = $ErrorActionPreference
    # 同 Invoke-Git：禁止 git 的 stderr 警告在 Stop 模式下终止脚本
    $ErrorActionPreference = 'SilentlyContinue'
    try {
        [Console]::OutputEncoding = [System.Text.Encoding]::UTF8
        # 用 -join 而不是 Out-String：后者受宿主宽度影响可能折行，且末尾多一个换行，
        # 会让内容指纹难以在别的语言里复算核对。-join "`n" 是确定性的。
        $diffText = (@(& git -C $RepoRoot -c core.quotepath=false diff HEAD --no-ext-diff --unified=0 2>$null) -join "`n")
        $statusLines = @(& git -C $RepoRoot -c core.quotepath=false status --porcelain=v1 --untracked-files=all 2>$null)
        $statusText = ($statusLines -join "`n")
        $untracked = @(& git -C $RepoRoot -c core.quotepath=false ls-files --others --exclude-standard 2>$null)
    }
    finally {
        [Console]::OutputEncoding = $prevEnc
        $ErrorActionPreference = $prevPref
    }

    # 只纳入源码 / 配置类未跟踪文件；产物目录 bin-net10\ 与 publish\ 已被 .gitignore 排除，
    # 不会进入 status，因此「每次编译都换指纹」的抖动不会发生。
    $extAllow = @('.cs', '.vb', '.xaml', '.props', '.targets', '.csproj', '.vbproj',
                  '.ps1', '.md', '.json', '.yml', '.yaml', '.ini', '.sh', '.bat', '.resx', '.pubxml')
    foreach ($rel in $untracked) {
        if ([string]::IsNullOrWhiteSpace($rel)) { continue }
        $rel = $rel.Trim('"')
        if (-not ($extAllow -contains [System.IO.Path]::GetExtension($rel).ToLowerInvariant())) { continue }
        $untrackedEntries += "$rel=$(Get-FileSha256Hex (Join-Path $RepoRoot $rel))"
    }
}
$untrackedEntries = @($untrackedEntries | Sort-Object)
$material = "HEAD=$headFull`nDIFF=$diffText`nSTATUS=$statusText`nUNTRACKED=$($untrackedEntries -join "`n")"
$contentId = (Get-Sha256Hex $material).Substring(0, 12)

# ---- UUID：与内容指纹绑定缓存 ------------------------------------------------
# 内容不变 → 复用；内容变化 → 换新。-ForceNewUuid 强制换新。
# 所有调用模式都走这一段，故 Build.ps1 / 编译目标 / 制品复制 / 钩子拿到同一个值。
$uuidCacheFile = Join-Path $RepoRoot 'pcl-next\obj\pcl-build.uuid'
$buildUuid = ''
if (-not $ForceNewUuid -and (Test-Path -LiteralPath $uuidCacheFile)) {
    $rawUuid = (Get-Content -LiteralPath $uuidCacheFile -Raw).Trim()
    $sep = $rawUuid.IndexOf('|')
    if ($sep -gt 0 -and $rawUuid.Substring($sep + 1) -eq $contentId) {
        $candidate = $rawUuid.Substring(0, $sep)
        if ($candidate -match '^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$') {
            $buildUuid = $candidate
        }
    }
}
if ([string]::IsNullOrWhiteSpace($buildUuid)) {
    $buildUuid = [guid]::NewGuid().ToString('D')
    $uuidDir = Split-Path -Parent $uuidCacheFile
    if ($uuidDir -and -not (Test-Path -LiteralPath $uuidDir)) {
        New-Item -ItemType Directory -Path $uuidDir -Force | Out-Null
    }
    [System.IO.File]::WriteAllText($uuidCacheFile, "$buildUuid|$contentId`r`n", (New-Object System.Text.UTF8Encoding $false))
}

# ---- 计算编号：提交数 × 0.01，满 100 进 1 ----
$major = [int][math]::Floor($commits / 100)
$minor = [int]($commits % 100)
$buildSmall = '{0}.{1:00}' -f $major, $minor
$token = "$buildSmall.g$commit.c$contentId"
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
        ContentId       = $contentId
        Uuid            = $buildUuid
        CommitFull      = $headFull
        UuidCacheFile   = $uuidCacheFile
    }
    ($result | ConvertTo-Json -Compress) | Write-Output
    return
}

# ---- 生成编译期源文件（内容未变则不写，避免无谓的重新编译）----
if ($OutFile) {
    $dirtyLiteral = 'False'
    if ($dirty) { $dirtyLiteral = 'True' }
    $dirtyCsLiteral = 'false'
    if ($dirty) { $dirtyCsLiteral = 'true' }

    # 注意：不要用 here-string 或多行数组——PowerShell 5.1 在 LF 换行的脚本文件里
    # 会把这些多行字面量解析错（生成结果丢换行、被合并成一行）。逐行 += 拼接最稳。
    # 2026-11-08 dev.7：按 -OutFile 扩展名决定输出语言。全量 C# 重写后
    # pcl-next.csproj 的 PclBuildInfo 目标写 obj\PclBuildInfo.g.cs，必须生成 C# 语法；
    # Linux / Android 线仍是 .vbproj + .g.vb，保持原 VB 输出不变。
    $nl = "`r`n"
    $isCSharp = ([System.IO.Path]::GetExtension($OutFile) -ieq '.cs')
    $generated = ''
    if ($isCSharp) {
        $generated += '// <auto-generated>' + $nl
        $generated += '//     由 scripts\Get-BuildSmallVersion.ps1 生成，请勿手工修改。' + $nl
        $generated += '//     规则见《分支与发布规范》§5.2.1：构建小版本 = 自固定锚点以来的提交数 × 0.01。' + $nl
        $generated += '// </auto-generated>' + $nl
        $generated += $nl
        $generated += "[assembly: System.Reflection.AssemblyInformationalVersion(`"$token`")]" + $nl
        $generated += "[assembly: System.Reflection.AssemblyMetadata(`"PclBuildUuid`", `"$buildUuid`")]" + $nl
        $generated += "[assembly: System.Reflection.AssemblyMetadata(`"PclBuildContentId`", `"$contentId`")]" + $nl
        $generated += "[assembly: System.Reflection.AssemblyMetadata(`"PclBuildCommitFull`", `"$headFull`")]" + $nl
        $generated += $nl
        # VB 的 Friend 类会被 RootNamespace 自动收进 PCL 命名空间，C# 不会，
        # 这里必须显式写 namespace，否则生成的代码找不到 PCL.PclBuildInfoGenerated。
        $generated += 'namespace PCL' + $nl
        $generated += '{' + $nl
        $generated += '    /// <summary>编译期生成的构建标识数据（编译前由项目目标生成，不纳入版本库）。</summary>' + $nl
        $generated += '    internal static class PclBuildInfoGenerated' + $nl
        $generated += '    {' + $nl
        $generated += "        /// <summary>构建小版本，形如 2.27（锚点以来第 227 个提交）。</summary>" + $nl
        $generated += "        internal const string BuildSmall = `"$buildSmall`";" + $nl
        $generated += '        /// <summary>构建小版本的数值形式（= 锚点以来的提交数）。比较新旧必须用它，不要比字符串。</summary>' + $nl
        $generated += "        internal const int BuildSmallNumber = $commits;" + $nl
        $generated += '        /// <summary>构建时的提交短哈希（7 位）；无 git 时为 nogit。</summary>' + $nl
        $generated += "        internal const string BuildCommit = `"$commit`";" + $nl
        $generated += '        /// <summary>构建时工作树是否有未提交的已跟踪改动（有则不可用于发布）。</summary>' + $nl
        $generated += "        internal const bool BuildIsDirty = $dirtyCsLiteral;" + $nl
        $generated += '        /// <summary>编号锚点的短哈希（永久固定，见脚本 ' + $AnchorSha + '）。</summary>' + $nl
        $generated += "        internal const string BuildAnchor = `"$anchor`";" + $nl
        $generated += '        /// <summary>自锚点以来的提交数。</summary>' + $nl
        $generated += "        internal const int BuildCommitCount = $commits;" + $nl
        $generated += '        /// <summary>完整版本标识，与 AssemblyInformationalVersion 一致。</summary>' + $nl
        $generated += "        internal const string BuildToken = `"$token`";" + $nl
        $generated += '        /// <summary>构建时 HEAD 的完整 40 位哈希。</summary>' + $nl
        $generated += "        internal const string BuildCommitFull = `"$headFull`";" + $nl
        $generated += '        /// <summary>源码内容指纹（12 位 hex）：同一提交号下内容不同即不同。</summary>' + $nl
        $generated += "        internal const string BuildContentId = `"$contentId`";" + $nl
        $generated += '        /// <summary>本版本 UUID v4，与内容指纹绑定（内容不变则复用）。</summary>' + $nl
        $generated += "        internal const string BuildUuid = `"$buildUuid`";" + $nl
        $generated += '    }' + $nl
        $generated += '}' + $nl
    }
    else {
    $generated += "' <auto-generated>" + $nl
    $generated += "'     由 scripts\Get-BuildSmallVersion.ps1 生成，请勿手工修改。" + $nl
    $generated += "'     规则见《分支与发布规范》§5.2.1：构建小版本 = 自固定锚点以来的提交数 × 0.01。" + $nl
    $generated += "' </auto-generated>" + $nl
    $generated += $nl
    $generated += "<Assembly: System.Reflection.AssemblyInformationalVersion(`"$token`")>" + $nl
    $generated += "<Assembly: System.Reflection.AssemblyMetadata(`"PclBuildUuid`", `"$buildUuid`")>" + $nl
    $generated += "<Assembly: System.Reflection.AssemblyMetadata(`"PclBuildContentId`", `"$contentId`")>" + $nl
    $generated += "<Assembly: System.Reflection.AssemblyMetadata(`"PclBuildCommitFull`", `"$headFull`")>" + $nl
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
    $generated += $nl
    $generated += "    ''' <summary>完整版本标识，与 AssemblyInformationalVersion 一致。</summary>" + $nl
    $generated += "    Friend Const BuildToken As String = `"$token`"" + $nl
    $generated += "    Friend Const BuildCommitFull As String = `"$headFull`"" + $nl
    $generated += "    Friend Const BuildContentId As String = `"$contentId`"" + $nl
    $generated += "    Friend Const BuildUuid As String = `"$buildUuid`"" + $nl
    $generated += 'End Class' + $nl
    }

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
