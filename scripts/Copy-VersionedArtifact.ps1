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

# 只接受真正的本机 apphost：扩展名必须是 .exe，且 PE 里不能有 CLR 头（托管程序集的特征）。
# 起因：C# 工程的 $(TargetPath) 指向 pcl-next.dll，误传进来会把 dll 改名成 exe，
# 得到一个双击就跑不起来的假 exe（曾累积两个 2.9 MB 的假产物）。
if ([IO.Path]::GetExtension($ExePath) -ne '.exe') {
    throw "Copy-VersionedArtifact: 拒绝复制 $ExePath —— 只能是 .exe（apphost），不能是 .dll。"
}
$bytes = [IO.File]::ReadAllBytes($ExePath)
if ($bytes.Length -lt 0x40 -or $bytes[0] -ne 0x4D -or $bytes[1] -ne 0x5A) {
    throw "Copy-VersionedArtifact: 拒绝复制 $ExePath —— 不是有效的 PE 文件（缺 MZ 头）。"
}
$peOffset = [BitConverter]::ToInt32($bytes, 0x3C)
$optStart = $peOffset + 24
$magic = [BitConverter]::ToUInt16($bytes, $optStart)
$dirStart = if ($magic -eq 0x20B) { $optStart + 112 } else { $optStart + 96 }
$clrRva = [BitConverter]::ToUInt32($bytes, $dirStart + 14 * 8)  # DataDirectory[14] = CLR Runtime Header
if ($clrRva -ne 0) {
    throw "Copy-VersionedArtifact: 拒绝复制 $ExePath —— 它是 .NET 托管程序集（含 CLR 头），不是可执行入口；请传 <AssemblyName>.exe apphost。"
}

# 版本名：读产物自己的 FileVersion（= AssemblyFileVersion，由 Set-VersionInfo.ps1 写入）
$version = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($ExePath).FileVersion
if ([string]::IsNullOrWhiteSpace($version)) { $version = '0.0.0.0' }

# 构建号：优先读产物自身 ProductVersion（= AssemblyInformationalVersion，形如
# 1.22.gaa500fa.c6295e4652f84.dirty），取开头的 M.mm —— 这样制品名与 exe 里的标识
# **必然一致**；2026-11-14 起构建号每次编译 +0.01，缓存会在构建结束时预置下一次的编号，
# 若在这里读缓存，Publish 阶段的副本就会拿到「下一个号」而与 exe 里的号脱钩。
# 读不到 ProductVersion（老产物 / 非本工程 exe）时退回脚本取值，保持旧行为。
$prodVersion = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($ExePath).ProductVersion
$buildSmall = ''
$dirtyTag = ''
if ($prodVersion -match '^(\d+\.\d{2})\.') { $buildSmall = $Matches[1] }
if ($prodVersion -and $prodVersion.EndsWith('.dirty')) { $dirtyTag = '-dirty' }
if ([string]::IsNullOrWhiteSpace($buildSmall)) {
    # 退回脚本取值（老产物 / 非本工程产出的 exe）
    $info = (& "$PSScriptRoot\Get-BuildSmallVersion.ps1" -Json) | ConvertFrom-Json
    $buildSmall = $info.BuildSmall
    if ($info.Dirty) { $dirtyTag = '-dirty' }
}
if ([string]::IsNullOrWhiteSpace($buildSmall)) { $buildSmall = '0.00' }

$code = $Channel.ToLowerInvariant()

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
