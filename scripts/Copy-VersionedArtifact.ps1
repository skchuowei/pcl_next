<#
.SYNOPSIS
    给编译产物复制一份带版本与构建号的文件名，便于一眼看出「这个 exe 是哪次提交编出来的」。

.DESCRIPTION
    原始产物名固定为 pcl-next.exe（AssemblyName 不能改：XAML 里大量
    `/pcl-next;component/...` 的资源 URI 依赖它）。本脚本在其旁边**另存**一份
    `PCL-Next-<版本名>-<代号>-<构建号>[-dirty].exe`，原文件保留，
    因此不会影响增量构建、dotnet run 或 XAML 资源解析。

    版本名取自产物自身的 FileVersion（即 AssemblyFileVersion，由发布脚本写入），
    构建号取自 scripts\Get-BuildSmallVersion.ps1，两者都不需要额外维护。

.PARAMETER ExePath
    原始产物路径（pcl-next.exe）。

.PARAMETER Channel
    渠道（Dev / Slush / Alpha / Beta / Rc / Snapshot），映射为小写代号。

.EXAMPLE
    .\scripts\Copy-VersionedArtifact.ps1 -ExePath .\bin\pcl-next.exe -Channel Dev
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$ExePath,
    [string]$Channel = 'Dev'
)

$ErrorActionPreference = 'Stop'

if (-not (Test-Path -LiteralPath $ExePath)) { return }

# 版本名：读产物自己的 FileVersion（= AssemblyFileVersion，由 Set-VersionInfo.ps1 写入）
$version = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($ExePath).FileVersion
if ([string]::IsNullOrWhiteSpace($version)) { $version = '0.0.0.0' }

# 构建号：与编译期写进 AssemblyInformationalVersion 的是同一个来源，不会漂
$info = (& "$PSScriptRoot\Get-BuildSmallVersion.ps1" -Json) | ConvertFrom-Json
$buildSmall = $info.BuildSmall

$code = $Channel.ToLowerInvariant()
$dirtyTag = ''
if ($info.Dirty) { $dirtyTag = '-dirty' }

$name = "PCL-Next-$version-$code-$buildSmall$dirtyTag.exe"
$dest = Join-Path (Split-Path -Parent $ExePath) $name

# 内容一致就不重复复制（80MB 的单文件产物，避免每次构建都白写一遍）
if (Test-Path -LiteralPath $dest) {
    $src = Get-Item -LiteralPath $ExePath
    $old = Get-Item -LiteralPath $dest
    if ($old.Length -eq $src.Length -and $old.LastWriteTimeUtc -ge $src.LastWriteTimeUtc) {
        Write-Output $dest
        return
    }
}

Copy-Item -LiteralPath $ExePath -Destination $dest -Force
Write-Output $dest
