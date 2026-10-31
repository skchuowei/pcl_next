<#
.SYNOPSIS
    按《分支与发布规范》§5.1 / §5.2 写入 pcl-next 的版本常量。

.DESCRIPTION
    版本常量一律由发布流程写入（《分支与发布规范》§2「版本常量」一行）。
    本脚本是该流程的唯一入口，两种用法：

    1. 发布（给定 -Tag）：版本名、版本号一律由标签推导，不接受人工填写。
         v2.100.0.1-dev.1     →  开发版，版本名 2.100.0.1，版本号 5000
         v2.100.0.2-slush.1   →  收敛版，版本名 2.100.0.2，版本号 5010
         v2.100.0.3-alpha.1   →  内测版，版本名 2.100.0.3，版本号 5020
         v2.100.0.7-beta.3    →  公测版，版本名 2.100.0.7，版本号 5062
         v2.100.0.0           →  正式版，版本名 2.100.0.0，版本号 5090
         v2.100.1.0-sp1       →  快照版，版本名 2.100.1.0，版本号 5095

    2. 内部构建（不给 -Tag）：按 -Channel 与源码里已有的版本名推导版本号，
       用于 dev / slush 等不对外分发的渠道。

    写入位置：
      · pcl-next/Modules/Base/ModBase.vb         → VersionBaseName / VersionCode / CommitHash
      · pcl-next/Modules/Base/ModBuildChannel.vb → ChannelVersionCodeBase（周期基址）
      · pcl-next/My Project/AssemblyInfo.vb      → AssemblyVersion / AssemblyFileVersion

.PARAMETER Channel
    渠道：Dev / Slush / Alpha / Beta / Pc / Snapshot。

.PARAMETER Tag
    版本标签，如 v2.100.0.1-alpha.1。

.PARAMETER CommitHash
    提交哈希，写入 ModBase.CommitHash。

.EXAMPLE
    ./scripts/Set-VersionInfo.ps1 -Channel Alpha -CommitHash abc123
    ./scripts/Set-VersionInfo.ps1 -Tag v2.100.0.1-alpha.1 -CommitHash abc123
#>
[CmdletBinding()]
param(
    [ValidateSet('Dev', 'Slush', 'Alpha', 'Beta', 'Pc', 'Snapshot')]
    [string]$Channel = 'Dev',
    [string]$Tag = '',
    [string]$CommitHash = ''
)

$ErrorActionPreference = 'Stop'
# 仓库根 = 含 pcl-next\pcl-next.vbproj 的目录
$Root = $PSScriptRoot
while ($Root -and -not (Test-Path (Join-Path $Root 'pcl-next\pcl-next.vbproj'))) {
    $Root = Split-Path -Parent $Root
}
if (-not $Root) { throw '未找到仓库根目录（应包含 pcl-next\pcl-next.vbproj）' }

# ── 号段表（《分支与发布规范》§5.2）：base + 偏移 ──────────────────────────────
# 号段按代号阶梯严格递增，且每个代号占独立区间：
#   dev = +0，slush = +10，alpha = +20，beta = +60，PC = +90，SP = +95
$Offset = @{ Dev = 0; Slush = 10; Alpha = 20; Beta = 60; Pc = 90; Snapshot = 95 }
# 首个周期（2.100）的基址为 5000，周期 2（2.101）为 5100，依此类推（周期序号从 0 计）
$BaseOfCycle = 5000
$FirstCycleMinor = 100

function Read-TextFile([string]$Path) {
    $bytes = [System.IO.File]::ReadAllBytes($Path)
    $hasBom = $bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF
    $text = [System.Text.Encoding]::UTF8.GetString($bytes, $(if ($hasBom) { 3 } else { 0 }), $bytes.Length - $(if ($hasBom) { 3 } else { 0 }))
    return @{ Text = $text; Bom = $hasBom }
}

function Write-TextFile([string]$Path, [string]$Text, [bool]$Bom) {
    $enc = [System.Text.UTF8Encoding]::new($Bom)
    [System.IO.File]::WriteAllText($Path, $Text, $enc)
}

function Replace-Once([string]$Text, [string]$Pattern, [string]$Replacement, [string]$What) {
    $rx = [System.Text.RegularExpressions.Regex]::new($Pattern)
    if (-not $rx.IsMatch($Text)) { throw "未能在源码中找到待替换项：$What" }
    # .NET 的替换串里 $ 有特殊含义，先转义
    $safe = $Replacement -replace '\$', '$$$$'
    return $rx.Replace($Text, $safe, 1)
}

# ── 解析标签 ──────────────────────────────────────────────────────────────────
$VersionName = $null
$Sequence = 1

if ($Tag) {
    $m = [regex]::Match($Tag, '^(?:v)?(?<major>\d+)\.(?<minor>\d+)\.(?<patch>\d+)\.(?<build>\d+)(?:-(?<chan>dev|slush|alpha|beta|pc|sp|snapshot)\.?(?<seq>\d+)?)?$')
    if (-not $m.Success) { throw "无法解析版本标签：$Tag" }
    $VersionName = "$($m.Groups['major'].Value).$($m.Groups['minor'].Value).$($m.Groups['patch'].Value).$($m.Groups['build'].Value)"
    $cycle = [int]$m.Groups['minor'].Value - $FirstCycleMinor
    $Base = $BaseOfCycle + 100 * $cycle
    $chan = $m.Groups['chan'].Value
    if ($chan) {
        switch ($chan) {
            'dev' { $Channel = 'Dev' }
            'slush' { $Channel = 'Slush' }
            'alpha' { $Channel = 'Alpha' }
            'beta' { $Channel = 'Beta' }
            'pc' { $Channel = 'Pc' }
            'sp' { $Channel = 'Snapshot' }
            'snapshot' { $Channel = 'Snapshot' }
        }
    }
    else { $Channel = 'Pc' }
    $Sequence = if ($m.Groups['seq'].Success) { [int]$m.Groups['seq'].Value } else { 1 }
}
else {
    $base = Read-TextFile (Join-Path $Root 'pcl-next\Modules\Base\ModBase.vb')
    $m = [regex]::Match($base.Text, 'Public Const VersionBaseName As String = "(?<ver>[\d\.]+)"')
    if (-not $m.Success) { throw "无法从 ModBase.vb 读取 VersionBaseName" }
    $VersionName = $m.Groups['ver'].Value
    $parts = $VersionName.Split('.')
    $cycle = [int]$parts[1] - $FirstCycleMinor
    $Base = $BaseOfCycle + 100 * $cycle
    $Sequence = [int]$parts[3]
    if ($Channel -eq 'Snapshot') { $Sequence = [int]$parts[2] }
    if ($Channel -eq 'Pc') { $Sequence = 1 }
}

# ── 计算版本号（§5.2 号段表）────────────────────────────────────────────────
# dev / slush / alpha / beta 均为「每构建 +1」，故按序号递增；
# PC 首发固定取该段首位；SP 按 SP 序号递增。
switch ($Channel) {
    'Dev' { $VersionCode = $Base + $Offset['Dev'] + ($Sequence - 1) }
    'Slush' { $VersionCode = $Base + $Offset['Slush'] + ($Sequence - 1) }
    'Alpha' { $VersionCode = $Base + $Offset['Alpha'] + ($Sequence - 1) }
    'Beta' { $VersionCode = $Base + $Offset['Beta'] + ($Sequence - 1) }
    'Pc' { $VersionCode = $Base + $Offset['Pc'] }
    'Snapshot' { $VersionCode = $Base + $Offset['Snapshot'] + ($Sequence - 1) }
}

Write-Host "渠道：$Channel    版本名：$VersionName    版本号：$VersionCode    号段基址：$Base"

# ── 写入 ModBase.vb ──────────────────────────────────────────────────────────
$path = Join-Path $Root 'pcl-next\Modules\Base\ModBase.vb'
$f = Read-TextFile $path
$text = $f.Text
$text = Replace-Once $text 'Public Const VersionBaseName As String = "[^"]*"' "Public Const VersionBaseName As String = `"$VersionName`"" 'VersionBaseName'
$text = Replace-Once $text 'Public Const VersionCode As Integer = [^\r\n]*' "Public Const VersionCode As Integer = $VersionCode" 'VersionCode'
$text = Replace-Once $text 'Public Const CommitHash As String = "[^"]*"' "Public Const CommitHash As String = `"$CommitHash`"" 'CommitHash'
Write-TextFile $path $text $f.Bom

# ── 写入 ModBuildChannel.vb（周期基址）──────────────────────────────────────
$path = Join-Path $Root 'pcl-next\Modules\Base\ModBuildChannel.vb'
$f = Read-TextFile $path
$text = Replace-Once $f.Text 'Public Const ChannelVersionCodeBase As Integer = \d+' "Public Const ChannelVersionCodeBase As Integer = $Base" 'ChannelVersionCodeBase'
Write-TextFile $path $text $f.Bom

# ── 写入 AssemblyInfo.vb ─────────────────────────────────────────────────────
$path = Join-Path $Root 'pcl-next\My Project\AssemblyInfo.vb'
$f = Read-TextFile $path
$text = $f.Text
$text = Replace-Once $text '<Assembly: AssemblyVersion\("[^"]*"\)>' "<Assembly: AssemblyVersion(`"$VersionName`")>" 'AssemblyVersion'
$text = Replace-Once $text '<Assembly: AssemblyFileVersion\("[^"]*"\)>' "<Assembly: AssemblyFileVersion(`"$VersionName`")>" 'AssemblyFileVersion'
Write-TextFile $path $text $f.Bom

Write-Host "版本常量已写入。"
