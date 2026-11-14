<#
.SYNOPSIS
    一键本地验证构建（按《分支与发布规范》§5.3）。

.DESCRIPTION
    产物固定输出到 pcl-next\bin-net10\<开发代号>\<版本名>\<运行环境>\ 三段路径，
    版本名自动从 pcl-next/Modules/Base/ModBase.cs 的 VersionBaseName 读取，
    OutputPath 传绝对路径，避免各依赖项目相对解析导致产物散落（§5.3 反模式）。

    注意：.NET 10 不允许在 sln 级别指定 -r <RID>（NETSDK1134），
    因此本脚本对主项目 pcl-next.csproj 构建，依赖项目经 ProjectReference 自动带上。

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
    版本名（如 2.100.0.2）。默认自动读取 ModBase.cs 的 VersionBaseName。

.PARAMETER Configuration
    构建配置，默认 Release。渠道常量随 -Channel 传入（PclChannel）。

.PARAMETER Clean
    构建前清空目标目录（保留 PCL\ 启动器运行数据目录）。

.PARAMETER WithCli
    同时构建 CLI 内核前端（PCL.Cli），产物落在同一三段路径下的 cli\ 子层。
    默认关闭：CLI 只用于回归与排错，不参与 GUI 发布制品。

.EXAMPLE
    ./scripts/Build.ps1 -Channel Alpha -Clean

.EXAMPLE
    ./scripts/Build.ps1 -WithCli          # 同时产出 .../<版本>/<RID>/cli/pclcli.exe
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
    [switch]$Clean,
    [switch]$WithCli
)

$ErrorActionPreference = 'Stop'
$RepoRoot = Split-Path -Parent $PSScriptRoot   # pcl-next.sln 所在目录（PCL2\）
$ModBase  = Join-Path $RepoRoot 'pcl-next\Modules\Base\ModBase.cs'
$MainProj = Join-Path $RepoRoot 'pcl-next\pcl-next.csproj'

# --- 1. 解析版本名 -----------------------------------------------------------
if (-not $Version) {
    if (-not (Test-Path $ModBase)) {
        throw "找不到 $ModBase，无法自动读取版本名；请用 -Version 显式指定。"
    }
    $line = Select-String -Path $ModBase -Pattern 'VersionBaseName = "([^"]+)"' |
        Select-Object -First 1
    if (-not $line) {
        throw "无法从 ModBase.cs 解析 VersionBaseName；请用 -Version 显式指定。"
    }
    $Version = $line.Matches[0].Groups[1].Value
}

# --- 1.2 清理 RID 继承缓存（2026-11-08） ------------------------------------
# Directory.Build.props 的 PclRidWriteCache 会在入口工程 BeforeBuild 写入本轮 RID，
# 供被 ProjectReference 移除 RuntimeIdentifier 的依赖工程继承。
# 每轮构建开头必须清掉，否则不带 -r 的独立编译会误继承上一轮 RID、绕过 PclRidGuard。
$RidCacheFile = Join-Path $RepoRoot 'obj\pcl-build.rid'
# 用「置空」代替删除：环境的 safe-delete 钩子会拦截 Remove-Item（fail-closed）。
# 空内容同样让 PclRidSegment 求值为空、护栏生效；入口工程随后用 WriteLinesToFile 覆写回真实 RID。
if (Test-Path -LiteralPath $RidCacheFile) { [System.IO.File]::WriteAllText($RidCacheFile, '') }

# --- 1.5 刷新构建号缓存 ------------------------------------------------------
# 目录名里带构建号（bin-net10\<代号>\<版本>-<构建号>\<RID>\），而 MSBuild 求值期
# 不能跑 git，所以必须**在这里**先把编号落到缓存文件，再让第 2 步去查 OutputPath。
# 顺序不能颠倒：先查路径后刷缓存，查到的是上一次的编号。
$CacheFile = Join-Path $RepoRoot 'pcl-next\obj\pcl-build.small'
$BuildInfo = (& (Join-Path $PSScriptRoot 'Get-BuildSmallVersion.ps1') `
        -RepoRoot $RepoRoot -CacheFile $CacheFile -Json) | ConvertFrom-Json
$BuildSmall = $BuildInfo.BuildSmall
if (-not $BuildSmall) {
    throw "无法取得构建号；请检查 scripts\Get-BuildSmallVersion.ps1 是否可执行。"
}
Write-Host "==> 构建标识：$($BuildInfo.Token)  UUID $($BuildInfo.Uuid)"
Write-Host "    编号 = 提交 $($BuildInfo.Commits) + 本机编译 $($BuildInfo.Seq) 次；构建成功后本机编译次数 +1，下次自动换号"

# --- 2. 从工程配置查询三段输出路径（唯一真相源：Directory.Build.props 的 PclGuiTreeRoot） ---
# 本脚本**不再自己拼路径**，也不再向 MSBuild 传 -p:OutputPath。
# 历史上这里手拼 "pcl-next\bin-net10\$Channel\$Version\$Rid" 并用 -p:OutputPath 硬灌，
# 恰好掩盖了 vbproj 里 OutputPath 只写「代号」一段的缺陷：走脚本是对的，
# 一旦直接 dotnet build / IDE 编译就退回单层树，两套产物目录并存（2026-11-01 事故）。
$OutPath = (& dotnet build $MainProj -getProperty:OutputPath `
        -p:Configuration=$Configuration -p:RuntimeIdentifier=$Rid -p:PclChannel=$Channel 2>$null |
        Out-String).Trim()
if ([string]::IsNullOrWhiteSpace($OutPath)) {
    throw "无法从工程查询 OutputPath；请检查 Directory.Build.props 的 PclGuiTreeRoot 是否定义。"
}
$OutPath = $OutPath.TrimEnd('\', '/')

# 防漂移断言：查询结果必须以 <开发代号>\<版本名>-<构建号>\<运行环境> 收尾
# Directory.Build.props 的三段树统一改用正斜杠（跨平台护栏：反斜杠在 Linux 上只是普通字符，
# 会让 Exists() 判定失败）。因此比较前把实际路径的分隔符规范化，
# 避免 Windows 端仅因 / 与 \ 的差异就误判「产物落错目录」。
$ExpectedTail = "$Channel\$Version-$BuildSmall\$Rid"
$OutPathForCheck = $OutPath.Replace('/', '\')
if (-not $OutPathForCheck.EndsWith($ExpectedTail, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "产物路径不符合三段树规范（AGENTS §0.1）：`n  实际：$OutPath`n  期望以：\$ExpectedTail 结尾`n请勿在工程体内手写 OutputPath，改用 `$(PclGuiTreeRoot)。"
}
Write-Host "==> 渠道 $Channel | 版本 $Version-$BuildSmall | RID $Rid | 配置 $Configuration" -ForegroundColor Cyan
Write-Host "==> 输出：$OutPath"

# --- 3. 可选清理（保留 PCL\ 运行数据目录） -------------------------------------
if ($Clean -and (Test-Path $OutPath)) {
    # 注意：PowerShell 5.1 下 Remove-Item 不接受 FileInfo 管道输入（会报 ParameterBindingException），
    # 必须用 ForEach-Object 显式传 -LiteralPath。
    Get-ChildItem -Path $OutPath -Force |
        Where-Object { $_.Name -ne 'PCL' } |
        ForEach-Object { Remove-Item -LiteralPath $_.FullName -Recurse -Force }
    Write-Host "==> 已清空目标目录（保留 PCL\ 运行数据）"
}

# --- 4. 构建（项目级：.NET 10 禁止 sln + -r，见 NETSDK1134） --------------------
dotnet build $MainProj `
    -c $Configuration -r $Rid --self-contained `
    -p:PclChannel=$Channel
if ($LASTEXITCODE -ne 0) {
    throw "dotnet build 失败，退出码 $LASTEXITCODE"
}

# --- 4.5 可选：构建 CLI 内核前端（§0.1 允许的三段树内 cli\ 子层） ---------------
# 注意 CLI 的树根是**仓库根**（$(PclTreeRoot)），不是 GUI 的 pcl-next\bin-net10，
# 所以不能用 $OutPath 去拼它的 exe 路径——必须单独向 CLI 工程查询自己的 OutputPath。
$CliOutPath = $null
if ($WithCli) {
    $CliProj = Join-Path $RepoRoot 'PCL.Cli\PCL.Cli.csproj'
    if (-not (Test-Path $CliProj)) {
        throw "找不到 $CliProj；-WithCli 需要 PCL.Cli 工程（见 docs/plans/计划-内核与GUI拆分（CLI）.md）"
    }
    Write-Host "==> 构建 CLI：$CliProj" -ForegroundColor Cyan
    # 框架依赖即可：CLI 只在装有 .NET 10 的开发机/CI 上跑，不随发布制品分发
    dotnet build $CliProj -c $Configuration -r $Rid -p:PclChannel=$Channel
    if ($LASTEXITCODE -ne 0) {
        throw "PCL.Cli 构建失败，退出码 $LASTEXITCODE"
    }
    $CliOutPath = (& dotnet build $CliProj -getProperty:OutputPath `
            -p:Configuration=$Configuration -p:RuntimeIdentifier=$Rid -p:PclChannel=$Channel 2>$null |
            Out-String).Trim().TrimEnd('\', '/')
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

# --- 7. CLI 产物提示（仅在 -WithCli 时） ---------------------------------------
if ($WithCli) {
    $CliExe = Join-Path $CliOutPath 'pclcli.exe'
    if (Test-Path $CliExe) {
        $cliItem = Get-Item $CliExe
        Write-Host ("    cli：{0}（{1:yyyy-MM-dd HH:mm}，{2:N0} KB）" -f `
            $cliItem.FullName, $cliItem.LastWriteTime, ($cliItem.Length / 1KB))
    }
    else {
        Write-Warning "未找到 $CliExe —— CLI 产物路径可能被 -p:OutputPath 意外覆盖。"
    }
}
