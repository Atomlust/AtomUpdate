﻿﻿﻿器 AOT 产物（可选内嵌）+ AtomUpdate NuGet 包 → dist/libs/
param(
    [string]$Configuration = 'Release',
    [string]$Output = 'dist',
    [string]$Version = '',        # 默认取 Directory.Build.props
    [switch]$SkipTest,
    [switch]$AllRids,             # 6 RID 全量内嵌（默认仅 win-x64，AOT 编译较慢）
    [switch]$SkipUpdater          # 跳过更新器内嵌（运行期回退 UpdaterPath/调度态，docs/04 §6）
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'release-lib.ps1')

$distRoot = Join-Path $RepoRoot $Output

if (-not $SkipTest) {
    Invoke-TargetTests -Projects @(
        (Join-Path $RepoRoot 'tests\AtomUpdate.Tests.Sdk\AtomUpdate.Tests.Sdk.csproj'),
        (Join-Path $RepoRoot 'tests\AtomUpdate.Tests.Core\AtomUpdate.Tests.Core.csproj')
    ) -Configuration $Configuration
}

$rids = @('win-x64')
if ($AllRids) { $rids = @('win-x64', 'win-arm64', 'linux-x64', 'linux-arm64', 'osx-x64', 'osx-arm64') }

$embedded = @()
if (-not $SkipUpdater) {
    foreach ($rid in $rids) {
        $dest = Join-Path $RepoRoot "artifacts\updater\$rid\native"
        Write-Host ">> dotnet publish AtomUpdate.Updater（$rid，Native AOT）" -ForegroundColor Yellow
        dotnet publish (Join-Path $RepoRoot 'src\AtomUpdate.Updater\AtomUpdate.Updater.csproj') `
            -c $Configuration -r $rid -o $dest --nologo
        if ($LASTEXITCODE -eq 0) { $embedded += $rid }
        else { Write-Warning "Updater AOT 发布失败（$rid）——该 RID 不内嵌，运行期回退 UpdaterPath/调度态" }
    }
}

$packArgs = @('pack', (Join-Path $RepoRoot 'src\AtomUpdate\AtomUpdate.csproj'), '-c', $Configuration,
    '-o', (Join-Path $distRoot 'libs'), '--nologo')
if ($Version) { $packArgs += "/p:Version=$Version" }
Write-Host ">> dotnet $($packArgs -join ' ')" -ForegroundColor Yellow
dotnet @packArgs
if ($LASTEXITCODE -ne 0) { throw 'NuGet 打包失败' }

$pkgs = @(Get-ChildItem (Join-Path $distRoot 'libs') -Filter *.nupkg)
$releaseVersion = if ($Version) { $Version } else { Get-RepoVersion }
$embeddedText = if ($embedded.Count -gt 0) { $embedded -join ', ' } else { '无（运行期回退）' }
$lines = @(
    'AtomUpdate SDK 库发布清单',
    "版本: $releaseVersion",
    "时间: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')",
    "内嵌更新器 RID: $embeddedText",
    ''
)
foreach ($pkg in $pkgs) { $lines += "  $($pkg.Name)  $([math]::Round($pkg.Length / 1MB, 2)) MB" }
Write-TextFile (Join-Path $distRoot 'RELEASE-LIBS.txt') $lines

Write-Host ''
Write-Host '完成。NuGet 包：' -ForegroundColor Green
foreach ($pkg in $pkgs) { Write-Host "  $($pkg.FullName)" }
Write-Host '使用：将 dist/libs 添加为本地 NuGet 源，或 dotnet nuget push 推送到私有源。' -ForegroundColor DarkGray
