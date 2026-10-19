﻿﻿﻿�发布公共库 —— 被 release-*.ps1 dot-source，勿直接运行
# 目标拆分：tools=打包工具(cli+gui)；web=服务端镜像构建并推送 Zot Registry；libs=客户端 SDK NuGet 包
# 产物统一输出到仓库根 dist/；单测按目标裁剪（tools→Tests.Packager，libs→Tests.Sdk+Tests.Core）以缩短发布耗时

$RepoRoot = Split-Path -Parent $PSScriptRoot

# 读取仓库版本（Directory.Build.props 的 <Version>）
function Get-RepoVersion {
    $line = Select-String -Path (Join-Path $RepoRoot 'Directory.Build.props') -Pattern '<Version>(.+?)</Version>'
    if ($line) { $line.Matches[0].Groups[1].Value } else { return '0.0.0' }
}

# 按目标运行裁剪后的单测（-m:1 串行，规避互斥锁类测试并行抖动）
function Invoke-TargetTests {
    param([string[]]$Projects, [string]$Configuration = 'Release')
    foreach ($project in $Projects) {
        Write-Host ">> dotnet test $([IO.Path]::GetFileNameWithoutExtension($project))" -ForegroundColor Yellow
        dotnet test $project -c $Configuration -m:1 --nologo
        if ($LASTEXITCODE -ne 0) { throw "测试失败：$project" }
    }
}

# 单文件发布（GUI 恒自包含并内嵌原生库；CLI 框架依赖）
function Publish-App {
    param(
        [string]$Project,
        [string]$OutputDir,
        [string]$Rid = 'win-x64',
        [string]$Configuration = 'Release',
        [bool]$SelfContained,
        [switch]$NativeLibs
    )
    $pubArgs = @('publish', $Project, '-c', $Configuration, '-r', $Rid,
        '/p:UseAppHost=true', '/p:PublishSingleFile=true', '-o', $OutputDir)
    if ($NativeLibs) { $pubArgs += '/p:IncludeNativeLibrariesForSelfExtract=true' }
    if ($SelfContained) { $pubArgs += '--self-contained' } else { $pubArgs += '--self-contained', 'false' }
    Write-Host ">> dotnet $($pubArgs -join ' ')" -ForegroundColor Yellow
    dotnet @pubArgs
    if ($LASTEXITCODE -ne 0) { throw "发布失败：$Project" }
}

function Remove-Pdbs {
    param([string]$Dir)
    if (Test-Path $Dir) { Get-ChildItem $Dir -Recurse -Filter *.pdb | Remove-Item -Force }
}

function Get-DirStats {
    param([string]$Dir)
    $files = @(Get-ChildItem $Dir -Recurse -File)
    $mb = [math]::Round(($files | Measure-Object Length -Sum).Sum / 1MB, 1)
    return @{ Count = $files.Count; SizeMB = $mb }
}

# 统一 UTF-8 BOM 写文本（Windows PowerShell 5.1 下无 BOM 的中文会乱码）
function Write-TextFile {
    param([string]$Path, [string[]]$Lines)
    $dir = Split-Path -Parent $Path
    if ($dir -and -not (Test-Path $dir)) { New-Item -ItemType Directory -Path $dir -Force | Out-Null }
    [IO.File]::WriteAllText($Path, ($Lines -join [Environment]::NewLine) + [Environment]::NewLine,
        [System.Text.UTF8Encoding]::new($true))
}
