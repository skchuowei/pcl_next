<#
.SYNOPSIS
    dev.7 full C# rewrite: run one VB -> C# project conversion in an isolated worktree.

.DESCRIPTION
    An SDK-style project is single-language: a .vbproj cannot mix .vb and .cs files.
    The full switch must therefore be completed inside an isolated worktree and merged
    back only after it compiles (see docs/plans plan-dev7 full C# rewrite, section 3.2).

    This script only does three things. It never commits, never merges, never touches
    the main working tree:
      1. Check the codeconv tool is available.
      2. Create / reuse an isolated worktree (default ..\.vb2cs-trial, detached at HEAD).
      3. Run the solution-level conversion there and print vb / cs / csproj counts.

    NOTE: file is intentionally pure ASCII. Windows PowerShell 5.1 reads BOM-less
    UTF-8 scripts as ANSI, which garbles non-ASCII text and can break quoted strings.

    After conversion, compile with:
      dotnet build pcl-next/pcl-next.csproj -c Release -r win-x64 --self-contained -p:PclChannel=Dev
    and fix errors by category until zero.

.PARAMETER RepoRoot
    Repository root. Default: parent of this script's directory.

.PARAMETER Worktree
    Isolated checkout path. Default: <parent-of-repo>\.vb2cs-trial.

.PARAMETER Solution
    Solution path relative to RepoRoot. Default: pcl-next.sln.

.PARAMETER ProjectPattern
    Regex matching project paths to convert. Default: pcl-next\.vbproj.

.PARAMETER Force
    If the worktree already exists, remove and recreate it (discards all conversion output there).

.PARAMETER BuildProperty
    MSBuild property passed to codeconv, format name=value. May be repeated.

.EXAMPLE
    .\scripts\Convert-VbToCSharp.ps1
    .\scripts\Convert-VbToCSharp.ps1 -Force -BuildProperty 'PclChannel=Dev'
#>
[CmdletBinding()]
param(
    [string]$RepoRoot = (Split-Path -Parent $PSScriptRoot),
    [string]$Worktree = (Join-Path (Split-Path -Parent (Split-Path -Parent $PSScriptRoot)) '.vb2cs-trial'),
    [string]$Solution = 'pcl-next.sln',
    [string]$ProjectPattern = 'pcl-next\.vbproj',
    [switch]$Force,
    [string[]]$BuildProperty = @()
)

$ErrorActionPreference = 'Stop'

$tool = Get-Command codeconv -ErrorAction SilentlyContinue
if (-not $tool) {
    throw 'codeconv is missing. Install: dotnet tool install --global icsharpcode.codeconverter.codeconv --version 10.0.1.923'
}

$RepoRoot = (Resolve-Path -LiteralPath $RepoRoot).Path
$sln = Join-Path $RepoRoot $Solution
if (-not (Test-Path -LiteralPath $sln)) { throw "Solution not found: $sln" }

if (Test-Path -LiteralPath $Worktree) {
    if ($Force) {
        git -C $RepoRoot worktree remove --force $Worktree
        if ($LASTEXITCODE -ne 0) { throw "Failed to remove worktree: $Worktree" }
    } else {
        throw "Worktree already exists: $Worktree (use -Force to rebuild; output is not auto-cleaned)"
    }
}

git -C $RepoRoot worktree add --detach $Worktree HEAD
if ($LASTEXITCODE -ne 0) { throw "Failed to create worktree: $Worktree" }
$Worktree = (Resolve-Path -LiteralPath $Worktree).Path
Write-Host "Isolated checkout: $Worktree"

$convArgs = @((Join-Path $Worktree $Solution), '--include', $ProjectPattern, '--target-language', 'CS', '--best-effort')
foreach ($p in $BuildProperty) { $convArgs += @('--build-property', $p) }

Write-Host "Running: codeconv $($convArgs -join ' ')"
Push-Location $Worktree
try {
    & codeconv @convArgs
    if ($LASTEXITCODE -ne 0) { Write-Warning "codeconv exit code $LASTEXITCODE (best-effort may still produce usable output)" }
} finally {
    Pop-Location
}

$projDir = Join-Path $Worktree 'pcl-next'
$vb = @(Get-ChildItem -LiteralPath $projDir -Filter '*.vb' -Recurse -File -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -notmatch '\\(obj|bin)\\' }).Count
$cs = @(Get-ChildItem -LiteralPath $projDir -Filter '*.cs' -Recurse -File -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -notmatch '\\(obj|bin)\\' }).Count
$csproj = @(Get-ChildItem -LiteralPath $projDir -Filter '*.csproj' -File -ErrorAction SilentlyContinue).Count

Write-Host ''
Write-Host "Conversion output: vb=$vb  cs=$cs  csproj=$csproj"
Write-Host "Next: cd '$projDir' ; dotnet build pcl-next.csproj -c Release -r win-x64 --self-contained -p:PclChannel=Dev"
Write-Host 'Main line is untouched. Merge only after zero compile errors, per plan section 4.2.'
