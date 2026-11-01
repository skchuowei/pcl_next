<#
.SYNOPSIS
    一键本地验证构建（按《分支与发布规范》§5.3）。

.DESCRIPTION
    产物固定输出到 pcl-next\bin-net10\<开发代号>\<版本名>\<运行环境>\ 三段路径，
    版本名自动从 pcl-next/Modules/Base/ModBase.vb 的 VersionBaseName 读取，
    OutputPath 传绝对路径，避免各依赖项目相对解析导致产物散落（§5.3 反模式）。

    注意：.NET 10 不允许在 sln 级别指定 -r <RID>（NETSDK1134），
    因此本脚本对主项目 pcl-next.vbproj 构建，依赖项目经 ProjectReference 自动带上。

    用法示例：
      .\scripts\Build.ps1                        # Dev 渠道，win-x64（默认）
      .\scripts\Build.ps1 -Channel Slush         # 指定渠道
      .\scripts\Build.ps1 -Rid win-arm64         # 指定运行环境
      .\scripts\Build.ps1 -Clean                 # 构建前清空目标目录（保留 PCL 运行数据）

    产物用途：本机冒烟与问题排查（多文件，便于看日志、调 PDB）。
    发布制品仍必须走 dotnet publish 单文件流程（§5.3），两者不可混用。

.PARAMETER Channel
    开发代号：Dev / Slush / Alpha / Beta / Rc / Snapshot。默认 Dev。

.PARAMETER Rid
    运行环境 RID：win-x64 / win-arm64。默认 win-x64。

.PARAMETER Version
    版本名（如 2.100.0.2）。默认自动读取 ModBase.vb 的 VersionBaseName。

.PARAMETER Configuration
    构建配置，默认 Release。渠道常量随 -Channel 传入（PclChannel）。

.PARAMETER Clean
    构建前清空目标目录（保留 PCL\ 启动器运行数据目录）。

.EXAMPLE
    ./scripts/Build.ps1 -Channel Alpha -Clean
#>
[CmdletBinding()]
param(
    [ValidateSet('Dev', 'Slush', 'Alpha', 'Beta', 'Rc', 'Snapshot')]
    [string]$Channel = 'Dev',
    [ValidateSet('win-x64', 'win-arm64')]
    [string]$Rid = 'win-x64',
    [string]$Version,
    [ValidateSet('Release', 'Debug')]
    [string]$Configuration = 'Release',
    [switch]$Clean
)

$ErrorActionPreference = 'Stop'
$RepoRoot = Split-Path -Parent $PSScriptRoot   # pcl-next.sln 所在目录（PCL2\）
$ModBase  = Join-Path $RepoRoot 'pcl-next\Modules\Base\ModBase.vb'
$MainProj = Join-Path $RepoRoot 'pcl-next\pcl-next.vbproj'

# --- 1. 解析版本名 -----------------------------------------------------------
if (-not $Version) {
    if (-not (Test-Path $ModBase)) {
        throw "找不到 $ModBase，无法自动读取版本名；请用 -Version 显式指定。"
    }
    $line = Select-String -Path $ModBase -Pattern 'VersionBaseName As String = "([^"]+)"' |
        Select-Object -First 1
    if (-not $line) {
        throw "无法从 ModBase.vb 解析 VersionBaseName；请用 -Version 显式指定。"
    }
    $Version = $line.Matches[0].Groups[1].Value
}

# --- 2. 计算三段输出路径（§5.3：代号\版本\运行环境，位于 pcl-next 项目树下） -----
$OutPath = Join-Path $RepoRoot "pcl-next\bin-net10\$Channel\$Version\$Rid"
$OutPathWithSlash = "$OutPath\"

Write-Host "==> 渠道 $Channel | 版本 $Version | RID $Rid | 配置 $Configuration" -ForegroundColor Cyan
Write-Host "==> 输出：$OutPath"

# --- 3. 可选清理（保留 PCL\ 运行数据目录） -------------------------------------
if ($Clean -and (Test-Path $OutPath)) {
    Get-ChildItem -Path $OutPath -Force |
        Where-Object { $_.Name -ne 'PCL' } |
        Remove-Item -Recurse -Force -Confirm:$false
    Write-Host "==> 已清空目标目录（保留 PCL\ 运行数据）"
}

# --- 4. 构建（项目级：.NET 10 禁止 sln + -r，见 NETSDK1134） --------------------
dotnet build $MainProj `
    -c $Configuration -r $Rid --self-contained `
    -p:PclChannel=$Channel `
    "-p:OutputPath=$OutPathWithSlash"
if ($LASTEXITCODE -ne 0) {
    throw "dotnet build 失败，退出码 $LASTEXITCODE"
}

# --- 5. 防呆检查（§5.3）：产物只允许出现在三段路径下 ----------------------------
$stray = @()
$stray += Get-ChildItem -Path (Join-Path $RepoRoot 'pcl-next\bin-net10') -File -ErrorAction SilentlyContinue
foreach ($proj in @('PCL.Download', 'PCLCS', 'PCL.Download.Tests', 'MeloongCore')) {
    $p = Get-ChildItem -Path (Join-Path $RepoRoot $proj) -Directory -Filter 'bin-net10' -Recurse -Depth 2 -ErrorAction SilentlyContinue
    if ($p) { $stray += $p }
}
if ($stray) {
    Write-Warning "在规范路径之外发现构建产物散落（可能是相对 OutputPath 解析或短 -o 所致），请核查："
    $stray | ForEach-Object { Write-Warning "  $($_.FullName)" }
}

# --- 6. 结果摘要 ---------------------------------------------------------------
$Exe = Join-Path $OutPath 'pcl-next.exe'
if (Test-Path $Exe) {
    $exeItem = Get-Item $Exe
    Write-Host "==> 构建成功" -ForegroundColor Green
    Write-Host "    产物：$OutPath"
    Write-Host ("    exe ：{0}（{1:yyyy-MM-dd HH:mm}，{2:N0} KB）" -f `
        $exeItem.Name, $exeItem.LastWriteTime, ($exeItem.Length / 1KB))
}
else {
    Write-Warning "构建命令已执行，但未找到 $Exe —— 请检查输出日志。"
}
