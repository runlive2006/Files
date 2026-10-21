#!/usr/bin/env pwsh
# 运行单元测试。
# 说明：.NET 10 SDK 下 `dotnet test` + VSTest 目标与 Microsoft.Testing.Platform 不兼容，
# 因此这里直接执行自包含测试可执行文件。

[CmdletBinding()]
param(
    [switch]$NoBuild
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot

# 将临时目录限制在仓库内，避免受限环境下访问系统临时目录失败
$localTemp = Join-Path $root '.dotnet/tmp'
New-Item -ItemType Directory -Force -Path $localTemp | Out-Null
$env:TMP = $localTemp
$env:TEMP = $localTemp

if (-not $NoBuild) {
    dotnet build (Join-Path $root 'Files.slnx') -m:1 --nologo
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}

$testExe = Join-Path $root 'tests/Files.Core.Tests/bin/Debug/net10.0/Files.Core.Tests.exe'
if (-not (Test-Path $testExe)) {
    Write-Error "未找到测试可执行文件：$testExe（请先构建）。"
}

& $testExe
exit $LASTEXITCODE
