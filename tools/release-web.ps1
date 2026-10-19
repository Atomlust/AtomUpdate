﻿﻿﻿Docker 镜像构建 + 推送 Zot Registry（部署端 docker compose 从 Registry 拉取）
param(
    [string]$Registry = 'docker.atomlust.com',
    [string]$ImageName = 'atomupdate/web',
    [string]$Tag = '',            # 默认取仓库版本
    [string]$Username = 'admin',
    [string]$Password = $env:ZOT_PASSWORD,
    [switch]$SkipPush
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'release-lib.ps1')

if (-not $Tag) { $Tag = Get-RepoVersion }
$full = "$Registry/${ImageName}:$Tag"

Write-Host ">> docker build -f src/AtomUpdate.Web/Dockerfile -t $full ." -ForegroundColor Yellow
Push-Location $RepoRoot
try {
    docker build -f src/AtomUpdate.Web/Dockerfile -t $full .
    if ($LASTEXITCODE -ne 0) { throw 'Docker 构建失败' }
}
finally { Pop-Location }

$pushed = $false
if (-not $SkipPush) {
    if (-not $Password) { throw '缺少 Registry 凭据：请设置环境变量 ZOT_PASSWORD 或传 -Password' }
    Write-Host ">> docker login $Registry（$Username）" -ForegroundColor Yellow
    $Password | docker login $Registry -u $Username --password-stdin
    if ($LASTEXITCODE -ne 0) { throw 'Registry 登录失败' }
    Write-Host ">> docker push $full" -ForegroundColor Yellow
    docker push $full
    if ($LASTEXITCODE -ne 0) { throw '镜像推送失败' }
    $pushed = $true
}

$pushText = if ($pushed) { '已推送到 Zot Registry' } else { '本地构建（-SkipPush，未推送）' }
Write-TextFile (Join-Path (Join-Path $RepoRoot 'dist') 'WEB-IMAGE.txt') @(
    'AtomUpdate Web 镜像',
    "镜像: $full",
    "推送: $pushText",
    "时间: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')",
    '部署: 服务器从 Registry 拉取该镜像，docker compose 启动（docs/09-部署手册）'
)

Write-Host ''
Write-Host "完成：$full（$pushText）" -ForegroundColor Green
