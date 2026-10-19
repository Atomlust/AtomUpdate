﻿﻿﻿#!/usr/bin/env pwsh
# AtomUpdate Web 部署脚本（SQLite 轻量单容器形态，docs/09-部署手册）
# 将服务接入指定 Docker 网络（默认 webnet），供同网络反向代理（nginx/traefik 等）以容器名直连。
# 零必填配置：首次访问自动进入设置向导创建管理员；JWT 密钥自动生成并持久化到数据卷。
#
# 用法示例：
#   pwsh tools/deploy-web.ps1                                        # 默认：webnet 网络，宿主 8081 端口仅回环
#   pwsh tools/deploy-web.ps1 -Image docker.atomlust.com/atomupdate/web:1.0.0
#   pwsh tools/deploy-web.ps1 -Port 8090 -Public                     # 端口绑定 0.0.0.0（直接对外）
#   pwsh tools/deploy-web.ps1 -AppEnv @('BOOTSTRAP_PASSWORD=xxx')    # 预配管理员跳过设置向导
#   pwsh tools/deploy-web.ps1 -DataVolume D:\atomupdate\data         # bind mount 变体（宿主目录，注意 APP_UID=1654 写权限）
[CmdletBinding()]
param(
    # 镜像（本地构建或 Registry 拉取的产物）
    [string]$Image = 'atomupdate/web:latest',
    [string]$ContainerName = 'atomupdate-web',
    # 接入的 Docker 网络（反向代理所在网络；不存在则自动创建）
    [string]$Network = 'webnet',
    # 宿主端口（容器内固定 8080）
    [int]$Port = 8081,
    # 端口绑定 0.0.0.0；默认仅 127.0.0.1（推荐：同机反向代理转发，容器间经 webnet 走容器名）
    [switch]$Public,
    # 附加环境变量（KEY=VALUE），如预配 Auth:Bootstrap:* 或 Database__Provider=npgsql 等
    [string[]]$AppEnv = @(),
    # 数据卷：/data（SQLite 库 + JWT 密钥文件）与 /data/uploads（更新包资产）
    [string]$DataVolume = 'atomupdate-data',
    [string]$UploadsVolume = 'atomupdate-uploads'
)
$ErrorActionPreference = 'Stop'

function Invoke-Docker {
    docker @args
    if ($LASTEXITCODE -ne 0) { throw "docker $($args -join ' ') 执行失败" }
}

# 0) Docker daemon 就绪检查
Invoke-Docker version --format '{{.Server.Version}}' | Out-Null

# 1) 网络就绪（不存在则创建）
$networks = docker network ls --format '{{.Name}}'
if (@($networks) -notcontains $Network) {
    Invoke-Docker network create $Network
    Write-Host "已创建网络 $Network"
}

# 2) 容器重建（幂等：已存在则先移除）
$containers = docker ps -a --format '{{.Names}}'
if (@($containers) -contains $ContainerName) {
    Invoke-Docker rm -f $ContainerName | Out-Null
    Write-Host "已移除旧容器 $ContainerName"
}

# 3) 组装运行参数并启动
$bind = if ($Public) { "0.0.0.0:$($Port):8080" } else { "127.0.0.1:$($Port):8080" }
$runArgs = @(
    'run', '-d',
    '--name', $ContainerName,
    '--restart', 'unless-stopped',
    '--network', $Network,
    '-p', $bind,
    '-v', "$($DataVolume):/data",
    '-v', "$($UploadsVolume):/data/uploads"
)
foreach ($entry in $AppEnv) { $runArgs += @('-e', $entry) }
$runArgs += $Image

Invoke-Docker @runArgs

Write-Host ''
Write-Host 'AtomUpdate 部署完成：'
Write-Host "  容器     : $ContainerName（网络 $Network，同网络反代 upstream 用容器名 ${ContainerName}:8080）"
Write-Host "  地址     : http://localhost:$Port$(if (-not $Public) { '（仅 127.0.0.1，供本机反向代理转发）' })"
Write-Host "  数据卷   : $DataVolume（SQLite 库 + JWT 密钥）、$UploadsVolume（更新包资产）"
Write-Host '  首次访问 : 浏览器打开地址进入设置向导创建平台管理员'
