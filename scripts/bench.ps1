#!/usr/bin/env pwsh
# 运行目录枚举性能基准台（DEV-PLAN M1 任务 1.5）。
# 必须用 Release：Debug 数值受未优化代码影响，不能作为验收依据。

[CmdletBinding()]
param(
    [int]$Count = 100000,
    [switch]$Gate,
    [switch]$Keep,
    [switch]$Reuse,
    [int]$Rounds = 3
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot

$localTemp = Join-Path $root '.dotnet/tmp'
New-Item -ItemType Directory -Force -Path $localTemp | Out-Null
$env:TMP = $localTemp
$env:TEMP = $localTemp

$arguments = @(
    'run', '-c', 'Release', '--project', (Join-Path $root 'bench/Files.Bench'),
    '--', '--count', $Count, '--rounds', $Rounds
)

if ($Gate) { $arguments += '--gate' }
if ($Keep) { $arguments += '--keep' }
if ($Reuse) { $arguments += '--reuse' }

dotnet @arguments
exit $LASTEXITCODE
