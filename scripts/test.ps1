#!/usr/bin/env pwsh
# 运行单元测试。
# 说明：.NET 10 SDK 下 `dotnet test` + VSTest 目标与 Microsoft.Testing.Platform 不兼容，
# 因此这里直接执行自包含测试可执行文件。

[CmdletBinding()]
param(
    [switch]$NoBuild,

    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Debug'
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot

# 将临时目录限制在仓库内，避免受限环境下访问系统临时目录失败
$localTemp = Join-Path $root '.dotnet/tmp'
New-Item -ItemType Directory -Force -Path $localTemp | Out-Null
$env:TMP = $localTemp
$env:TEMP = $localTemp

if (-not $NoBuild) {
    # 单节点 + 禁用构建服务器：受限环境（沙箱/策略限制）下多节点与编译服务器会因
    # 进程间通信失败而报错，单节点最稳。项目规模小，速度差异可忽略。
    dotnet build (Join-Path $root 'Files.slnx') -c $Configuration -m:1 --nologo --disable-build-servers
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}

# 目标框架可能变化（如 net10.0 -> net10.0-windows），因此动态查找指定配置下的
# 测试可执行文件，避免静默运行上一轮的陈旧产物。
$testExe = Get-ChildItem -Path (Join-Path $root "tests/Files.Core.Tests/bin/$Configuration") `
        -Filter 'Files.Core.Tests.exe' -Recurse -ErrorAction SilentlyContinue |
    Sort-Object LastWriteTime -Descending |
    Select-Object -First 1

if ($null -eq $testExe) {
    Write-Error "未找到测试可执行文件（请先以 $Configuration 配置构建）。"
}

Write-Host "运行：$($testExe.FullName)" -ForegroundColor DarkGray

& $testExe.FullName
exit $LASTEXITCODE
