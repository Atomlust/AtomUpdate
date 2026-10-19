﻿﻿﻿LI + GUI 单文件 → dist/tools/{cli,gui}/，GUI 双击即启动
param(
    [string]$Configuration = 'Release',
    [string]$Output = 'dist',
    [string]$Rid = 'win-x64',
    [switch]$SkipTest,
    [ValidateSet('', 'cli', 'gui')]
    [string]$Only = ''
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'release-lib.ps1')

$distRoot = Join-Path $RepoRoot $Output
$distTools = Join-Path $distRoot 'tools'

if (-not $SkipTest) {
    Invoke-TargetTests -Projects @((Join-Path $RepoRoot 'tests\AtomUpdate.Tests.Packager\AtomUpdate.Tests.Packager.csproj')) `
        -Configuration $Configuration
}

if ($Only -ne 'gui') {
    Publish-App -Project (Join-Path $RepoRoot 'src\AtomUpdate.Packager.Cli\AtomUpdate.Packager.Cli.csproj') `
        -OutputDir (Join-Path $distTools 'cli') -Rid $Rid -Configuration $Configuration -SelfContained:$false
    Remove-Pdbs (Join-Path $distTools 'cli')
}
if ($Only -ne 'cli') {
    Publish-App -Project (Join-Path $RepoRoot 'src\AtomUpdate.Packager\AtomUpdate.Packager.csproj') `
        -OutputDir (Join-Path $distTools 'gui') -Rid $Rid -Configuration $Configuration -SelfContained:$true -NativeLibs
    Remove-Pdbs (Join-Path $distTools 'gui')
}

$lines = @(
    'AtomUpdate 打包工具发布清单',
    "版本: $(Get-RepoVersion)",
    "配置: $Configuration",
    "RID:  $Rid",
    "时间: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')",
    ''
)
if ($Only -ne 'gui') {
    $s = Get-DirStats (Join-Path $distTools 'cli')
    $lines += "  cli  $($s.SizeMB) MB  $($s.Count) 个文件"
}
if ($Only -ne 'cli') {
    $s = Get-DirStats (Join-Path $distTools 'gui')
    $lines += "  gui  $($s.SizeMB) MB  $($s.Count) 个文件"
}
Write-TextFile (Join-Path $distRoot 'RELEASE-TOOLS.txt') $lines

Write-Host ''
Write-Host '完成。产物：' -ForegroundColor Green
if ($Only -ne 'gui') { Write-Host "  CLI: $distTools\cli\AtomUpdate.Packager.Cli.exe" }
if ($Only -ne 'cli') { Write-Host "  GUI: $distTools\gui\AtomUpdate.Packager.exe （双击启动）" -ForegroundColor Green }
