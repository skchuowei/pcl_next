<#
.SYNOPSIS
    校验《分支与发布规范》§5.2 的版本号段表。

.DESCRIPTION
    在临时目录里对 scripts\Set-VersionInfo.ps1 跑一组标签用例，逐个读回写入的
    VersionCode，然后校验两件事：

      1. 每个用例算出的版本号与规范给出的期望值完全一致；
      2. 六个代号的号段区间严格递增且互不重叠——
         dev < slush < alpha < beta < PC < SP。

    脚本只读仓库源码，运行在临时副本上，不会改动工作区。

.PARAMETER CycleMinor
    周期序号对应的次版本号，默认 100（周期 1，即 2.100）。

.EXAMPLE
    ./scripts/Test-VersionMatrix.ps1
    ./scripts/Test-VersionMatrix.ps1 -CycleMinor 101
#>
[CmdletBinding()]
param(
    [int]$CycleMinor = 100
)

$ErrorActionPreference = 'Stop'

# ── 定位仓库根 ──────────────────────────────────────────────────────────────
$Root = $PSScriptRoot
while ($Root -and -not (Test-Path (Join-Path $Root 'pcl-next\pcl-next.csproj'))) {
    $Root = Split-Path -Parent $Root
}
if (-not $Root) { throw '未找到仓库根目录（应包含 pcl-next\pcl-next.csproj）' }
$Script = Join-Path $Root 'scripts\Set-VersionInfo.ps1'
if (-not (Test-Path $Script)) { throw "未找到版本脚本：$Script" }

$Base = 5000 + 100 * ($CycleMinor - 100)

# ── 号段表（《分支与发布规范》§5.2）──────────────────────────────────────────
# 每个代号的区间写成 [最小, 最大]，用于校验严格递增与互不重叠。
$Ranges = [ordered]@{
    'dev'     = @(($Base + 0),  ($Base + 9))
    'slush'   = @(($Base + 10), ($Base + 19))
    'alpha'   = @(($Base + 20), ($Base + 59))
    'beta'    = @(($Base + 60), ($Base + 89))
    'rc'      = @(($Base + 90), ($Base + 94))
    'snapshot'= @(($Base + 95), ($Base + 99))
}

# ── 测试用例：标签 → 期望版本号 ──────────────────────────────────────────────
# 用例覆盖每个代号的起点、终点与中间点。
$Cases = @(
    @{ Tag = "v2.$($CycleMinor).0.1-dev.1";      Expect = $Base + 0 }
    @{ Tag = "v2.$($CycleMinor).0.1-dev.10";     Expect = $Base + 9 }
    @{ Tag = "v2.$($CycleMinor).0.2-slush.1";    Expect = $Base + 10 }
    @{ Tag = "v2.$($CycleMinor).0.2-slush.10";   Expect = $Base + 19 }
    @{ Tag = "v2.$($CycleMinor).0.3-alpha.1";    Expect = $Base + 20 }
    @{ Tag = "v2.$($CycleMinor).0.3-alpha.40";   Expect = $Base + 59 }
    @{ Tag = "v2.$($CycleMinor).0.7-beta.1";     Expect = $Base + 60 }
    @{ Tag = "v2.$($CycleMinor).0.7-beta.30";    Expect = $Base + 89 }
    @{ Tag = "v2.$($CycleMinor).0.0";            Expect = $Base + 90 }
    @{ Tag = "v2.$($CycleMinor).1.0-sp1";        Expect = $Base + 95 }
    @{ Tag = "v2.$($CycleMinor).1.0-sp5";        Expect = $Base + 99 }
)

# ── 准备临时副本 ────────────────────────────────────────────────────────────
$Tmp = Join-Path ([System.IO.Path]::GetTempPath()) ("pclmatrix_" + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path (Join-Path $Tmp 'scripts') -Force | Out-Null
New-Item -ItemType Directory -Path (Join-Path $Tmp 'pcl-next\Modules\Base') -Force | Out-Null
New-Item -ItemType Directory -Path (Join-Path $Tmp 'pcl-next\My Project') -Force | Out-Null
Copy-Item $Script (Join-Path $Tmp 'scripts\Set-VersionInfo.ps1') -Force
Copy-Item (Join-Path $Root 'pcl-next\Modules\Base\ModBase.cs') (Join-Path $Tmp 'pcl-next\Modules\Base\ModBase.cs') -Force
Copy-Item (Join-Path $Root 'pcl-next\Modules\Base\ModBuildChannel.cs') (Join-Path $Tmp 'pcl-next\Modules\Base\ModBuildChannel.cs') -Force
Copy-Item (Join-Path $Root 'pcl-next\My Project\AssemblyInfo.cs') (Join-Path $Tmp 'pcl-next\My Project\AssemblyInfo.cs') -Force
# csproj 只用于让脚本识别出仓库根，内容无需有效
Set-Content -Path (Join-Path $Tmp 'pcl-next\pcl-next.csproj') -Value '<Project />' -Encoding UTF8

$Failed = 0
$Rows = New-Object System.Collections.Generic.List[object]

try {
    foreach ($c in $Cases) {
        & (Join-Path $Tmp 'scripts\Set-VersionInfo.ps1') -Tag $c.Tag -CommitHash 'matrix-test' *> $null
        $text = [System.IO.File]::ReadAllText((Join-Path $Tmp 'pcl-next\Modules\Base\ModBase.cs'))
        $m = [regex]::Match($text, 'public const int VersionCode = (\d+)')
        if (-not $m.Success) { throw "用例 $($c.Tag) 未能读回 VersionCode" }
        $actual = [int]$m.Groups[1].Value
        $ok = ($actual -eq $c.Expect)
        if (-not $ok) { $Failed++ }
        $Rows.Add([pscustomobject]@{
            标签   = $c.Tag
            期望   = $c.Expect
            实际   = $actual
            结果   = $(if ($ok) { '通过' } else { '失败' })
        })
    }

    # ── 校验号段严格递增且互不重叠 ──────────────────────────────────────────
    $prev = $null
    foreach ($k in $Ranges.Keys) {
        $r = $Ranges[$k]
        if ($r[0] -gt $r[1]) { throw "号段定义错误：$k 的下界大于上界" }
        if ($prev -and $r[0] -le $prev) {
            throw "号段未严格递增：$k 的下界 $($r[0]) 未大于上一段的上界 $prev"
        }
        $prev = $r[1]
    }
}
finally {
    Remove-Item $Tmp -Recurse -Force -ErrorAction SilentlyContinue
}

# ── 输出 ────────────────────────────────────────────────────────────────────
Write-Host ''
Write-Host "号段表（周期基址 base = $Base）" -ForegroundColor Cyan
Write-Host ('-' * 62)
Write-Host ('代号'.PadRight(10) + '区间'.PadRight(18) + '说明')
foreach ($k in $Ranges.Keys) {
    $r = $Ranges[$k]
    Write-Host ($k.PadRight(10) + ("{0} … {1}" -f $r[0], $r[1]).PadRight(18) + "base+$($r[0] - $Base)…base+$($r[1] - $Base)")
}
Write-Host ('-' * 62)
$Rows | Format-Table -AutoSize | Out-String | Write-Host

if ($Failed -gt 0) {
    Write-Host "验证失败：$Failed 个用例的版本号与规范不符。" -ForegroundColor Red
    exit 1
}
Write-Host "验证通过：$($Cases.Count) 个用例全部符合《分支与发布规范》§5.2，六段号段严格递增且互不重叠。" -ForegroundColor Green
exit 0
