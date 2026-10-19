﻿﻿﻿��：按目标透传到拆分脚本，只做需要的目标，避免每次全量等待
# 用法：
#   .\tools\release.ps1                    # 全量（tools + libs + web）
#   .\tools\release.ps1 -Target tools      # 只发打包工具
#   .\tools\release.ps1 -Target libs       # 只发 SDK 库
#   .\tools\release.ps1 -Target web        # 只发 Web 镜像（构建+推送 Zot）
#   .\tools\release.ps1 -SkipTest -SkipPush
param(
    [ValidateSet('all', 'tools', 'libs', 'web')]
    [string]$Target = 'all',
    [switch]$SkipTest,
    [switch]$SkipPush
)
$ErrorActionPreference = 'Stop'

$common = @()
if ($SkipTest) { $common += '-SkipTest' }

if ($Target -in 'all', 'tools') { & (Join-Path $PSScriptRoot 'release-tools.ps1') @common }
if ($Target -in 'all', 'libs')  { & (Join-Path $PSScriptRoot 'release-libs.ps1') @common }
if ($Target -in 'all', 'web') {
    $webArgs = @($common)
    if ($SkipPush) { $webArgs += '-SkipPush' }
    & (Join-Path $PSScriptRoot 'release-web.ps1') @webArgs
}
