<#
.SYNOPSIS
    安装/卸载本地 git 钩子：提交时自动追加「构建小版本」（《分支与发布规范》§5.2.1）。

.DESCRIPTION
    .git/hooks 不进版本库，所以钩子靠本脚本从 scripts\git-hooks\ 安装，保证换机器后一条命令复原。
    安装的是 prepare-commit-msg：在提交信息末尾追加一行 `Build: <M.mm>`，
    值由 git 推导（自最近发布标签以来的提交数 × 0.01），不会重号、不会漏号。

.PARAMETER Uninstall
    卸载钩子（把被覆盖的原钩子备份还原回去）。

.EXAMPLE
    .\scripts\Install-GitHooks.ps1

.EXAMPLE
    .\scripts\Install-GitHooks.ps1 -Uninstall
#>
[CmdletBinding()]
param(
    [switch]$Uninstall
)

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$hookName = 'prepare-commit-msg'

$hooksDir = (& git -C $repoRoot rev-parse --git-path hooks 2>$null)
if (-not $hooksDir) {
    throw "无法定位 git 钩子目录：$repoRoot 不是 git 仓库？"
}
if (-not [System.IO.Path]::IsPathRooted($hooksDir)) {
    $hooksDir = Join-Path $repoRoot $hooksDir
}

$target = Join-Path $hooksDir $hookName
$backup = "$target.pcl-backup"
$source = Join-Path $PSScriptRoot "git-hooks\$hookName"

if ($Uninstall) {
    if (Test-Path $backup) {
        Move-Item -Path $backup -Destination $target -Force
        Write-Output "已还原原钩子：$target"
    }
    elseif (Test-Path $target) {
        Remove-Item -Path $target -Force
        Write-Output "已移除钩子：$target"
    }
    else {
        Write-Output "未安装钩子，无需卸载。"
    }
    return
}

if (-not (Test-Path $source)) {
    throw "钩子模板不存在：$source"
}

# 已有同名钩子且不是本脚本装的，先备份，避免覆盖用户自己的钩子
if ((Test-Path $target) -and -not (Test-Path $backup)) {
    $existing = Get-Content -Path $target -Raw -Encoding UTF8
    if ($existing -notmatch 'PCL 构建小版本|分支与发布规范') {
        Copy-Item -Path $target -Destination $backup -Force
        Write-Output "已备份原有钩子到：$backup"
    }
}

New-Item -ItemType Directory -Path $hooksDir -Force | Out-Null
# 钩子必须用 LF 换行（由 sh 执行），这里按字节拷贝，不做编码转换
Copy-Item -Path $source -Destination $target -Force

Write-Output "已安装钩子：$target"
Write-Output "生效方式：下次 git commit 起，提交信息末尾自动追加 `Build: <M.mm>`。"
Write-Output "当前值（仅参考，提交时会自动重算）："
& (Join-Path $PSScriptRoot 'Get-BuildSmallVersion.ps1') -RepoRoot $repoRoot
