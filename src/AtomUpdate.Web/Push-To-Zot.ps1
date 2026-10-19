param(
    [Parameter(Mandatory = $true)]
    [string]$ImageName,

    [string]$Tag = "latest",
    [string]$Registry = "docker.atomlust.com",
    [string]$Username = "admin",
    [string]$Password = $env:ZOT_PASSWORD,

    # 解决方案根目录（构建上下文）
    [string]$BuildContext = "D:\Source\AtomUpdate",

    # Dockerfile 相对解决方案根目录的路径
    [string]$DockerfileRel = "src/AtomUpdate.Web/Dockerfile"
)

$ErrorActionPreference = "Stop"

# ===== 检查参数 =====
if (-not $Password) {
    Write-Error "Please provide password via -Password or set ZOT_PASSWORD env var"
    exit 1
}

if (-not (Test-Path $BuildContext)) {
    Write-Error "Build context not found: $BuildContext"
    exit 1
}

$DockerfileFull = Join-Path $BuildContext $DockerfileRel
if (-not (Test-Path $DockerfileFull)) {
    Write-Error "Dockerfile not found: $DockerfileFull"
    exit 1
}

$FullImage = "$Registry/$ImageName`:$Tag"

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  Image:     $FullImage" -ForegroundColor Cyan
Write-Host "  Context:   $BuildContext" -ForegroundColor Cyan
Write-Host "  Dockerfile:$DockerfileFull" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan

# ===== 1. 构建 =====
Write-Host "`n[1/3] Building Docker image..." -ForegroundColor Yellow
# 切到上下文目录，用相对路径构建，保证和 Dockerfile 顶部注释一致
Push-Location $BuildContext
try {
    docker build -f $DockerfileRel -t $FullImage .
    if ($LASTEXITCODE -ne 0) { throw "Docker build failed" }
}
finally {
    Pop-Location
}
Write-Host "Build OK" -ForegroundColor Green

# ===== 2. 登录 =====
Write-Host "`n[2/3] Logging in to $Registry ..." -ForegroundColor Yellow
$Password | docker login $Registry -u $Username --password-stdin
if ($LASTEXITCODE -ne 0) { Write-Error "Login failed"; exit 1 }
Write-Host "Login OK" -ForegroundColor Green

# ===== 3. 推送 =====
Write-Host "`n[3/3] Pushing $FullImage ..." -ForegroundColor Yellow
docker push $FullImage
if ($LASTEXITCODE -ne 0) { Write-Error "Push failed"; exit 1 }
Write-Host "Push OK" -ForegroundColor Green

Write-Host "`nDone: $FullImage" -ForegroundColor Cyan