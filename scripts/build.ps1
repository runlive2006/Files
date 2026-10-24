#!/usr/bin/env pwsh
<#
.SYNOPSIS
    构建 Files 文件管理器，产出可直接运行的 exe。

.DESCRIPTION
    一键完成：环境检查 -> 还原 -> 构建 -> 测试 -> 发布。

    产物为「框架依赖」目录（不是单文件、不打 zip）：
        artifacts/Files-<版本>-win-x64/Files.exe
    目标机器需要安装 .NET 10 桌面运行时。

.PARAMETER Configuration
    构建配置，默认 Release。Debug 仅用于排查问题。

.PARAMETER Version
    覆盖版本号，例如 0.2.0。默认读取 src/Files.App/Files.App.csproj 里的 <Version>。

.PARAMETER SkipTests
    跳过单元测试，只做构建与发布（速度快，但不保证逻辑正确）。

.PARAMETER OutputDirectory
    自定义发布目录。默认为 artifacts/Files-<版本>-win-x64。

.EXAMPLE
    .\scripts\build.ps1

    默认：Release 构建 + 跑测试 + 发布。

.EXAMPLE
    .\scripts\build.ps1 -SkipTests -Version 0.2.0

    快速出包，不跑测试，版本号覆盖为 0.2.0。
#>

[CmdletBinding()]
param(
    [ValidateSet('Release', 'Debug')]
    [string]$Configuration = 'Release',

    [string]$Version = '',

    [switch]$SkipTests,

    [string]$OutputDirectory = ''
)

$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$appProject = Join-Path $root 'src/Files.App/Files.App.csproj'
$solution = Join-Path $root 'Files.slnx'
$globalJson = Join-Path $root 'global.json'

$script:stepNumber = 0

function Write-Step([string]$message) {
    $script:stepNumber++
    Write-Host ''
    Write-Host "[$script:stepNumber] $message" -ForegroundColor Cyan
}

function Write-Ok([string]$message) {
    Write-Host "    $message" -ForegroundColor Green
}

function Write-Info([string]$message) {
    Write-Host "    $message" -ForegroundColor DarkGray
}

function Fail([string]$message) {
    Write-Host ''
    Write-Host "构建失败：$message" -ForegroundColor Red
    exit 1
}

# ---------------------------------------------------------------------------
Write-Step '检查构建环境'

if (-not (Test-Path $appProject)) {
    Fail "找不到项目文件：$appProject（请在仓库根目录运行本脚本）"
}

$dotnetCommand = Get-Command dotnet -ErrorAction SilentlyContinue
if ($null -eq $dotnetCommand) {
    Fail @'
未找到 dotnet 命令。

请先安装 .NET SDK 10：
  https://dotnet.microsoft.com/download/dotnet/10.0
安装后重新打开终端，再运行本脚本。
'@
}

$requiredSdkVersion = '10.0'
if (Test-Path $globalJson) {
    $requiredSdkVersion = (Get-Content $globalJson -Raw | ConvertFrom-Json).sdk.version
}

$requiredMajor = [int]($requiredSdkVersion.Split('.')[0])
$installedSdks = & dotnet --list-sdks

if ($LASTEXITCODE -ne 0 -or $null -eq $installedSdks) {
    Fail '无法读取已安装的 .NET SDK 列表，请确认 SDK 安装完整。'
}

$hasRequiredSdk = @($installedSdks | Where-Object { $_ -match "^\s*$requiredMajor\." }).Count -gt 0

if (-not $hasRequiredSdk) {
    Write-Host ''
    Write-Host '已安装的 SDK：' -ForegroundColor Yellow
    $installedSdks | ForEach-Object { Write-Host "    $_" }
    Fail @"
需要 .NET SDK $requiredMajor.x（global.json 要求 $requiredSdkVersion）。

请安装：https://dotnet.microsoft.com/download/dotnet/$requiredMajor.0
"@
}

Write-Ok "dotnet $(& dotnet --version)"

# 把 SDK 缓存、临时目录都限制在仓库内：
# 1) 不污染用户的全局 NuGet 缓存；2) 受限环境下系统临时目录可能不可写。
$env:DOTNET_CLI_HOME = Join-Path $root '.dotnet/home'
$env:NUGET_PACKAGES = Join-Path $root '.dotnet/packages'
$env:DOTNET_NOLOGO = '1'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'

$localTemp = Join-Path $root '.dotnet/tmp'
New-Item -ItemType Directory -Force -Path $localTemp | Out-Null
$env:TMP = $localTemp
$env:TEMP = $localTemp

Write-Info "依赖缓存：$env:NUGET_PACKAGES"

# ---------------------------------------------------------------------------
Write-Step '确定版本号'

$effectiveVersion = $Version

if ([string]::IsNullOrWhiteSpace($effectiveVersion)) {
    [xml]$projectXml = Get-Content $appProject
    $effectiveVersion = $projectXml.Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1

    if ([string]::IsNullOrWhiteSpace($effectiveVersion)) {
        $effectiveVersion = '0.0.0'
        Write-Info '项目未声明 <Version>，使用 0.0.0'
    }
    else {
        Write-Info "读取自项目文件"
    }
}

$publishDir = if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    Join-Path $root "artifacts/Files-$effectiveVersion-win-x64"
}
else {
    $OutputDirectory
}

Write-Ok "版本 $effectiveVersion"
Write-Info "输出目录 $publishDir"

# ---------------------------------------------------------------------------
Write-Step '检查是否有正在运行的实例'

# 上一次构建的 exe 若仍在运行，会锁住输出文件，导致复制失败（MSB3021）。
# 提前检测并给出明确提示，比让用户面对一屏 MSBuild 错误好得多。
$runningProcesses = @(Get-Process -Name 'Files' -ErrorAction SilentlyContinue |
        Where-Object { $_.Path -and $_.Path.StartsWith($root, [StringComparison]::OrdinalIgnoreCase) })

if ($runningProcesses.Count -gt 0) {
    $ids = ($runningProcesses | ForEach-Object { $_.Id }) -join ', '
    Fail @"
检测到正在运行的 Files 进程（PID: $ids）。

请先关闭这些窗口再重新构建 —— 否则输出文件被占用，复制会失败。
"@
}

Write-Ok '没有占用输出目录的进程'

# ---------------------------------------------------------------------------
Write-Step "还原依赖（$Configuration）"

& dotnet restore $solution --nologo | Out-Host
if ($LASTEXITCODE -ne 0) { Fail '依赖还原失败。请检查网络连接，或确认 NuGet 源可访问。' }

Write-Ok '还原完成'

# ---------------------------------------------------------------------------
Write-Step "构建（$Configuration）"

# -m:1 与 --disable-build-servers：受限环境（沙箱、组策略）下 MSBuild 的多节点与
# 编译服务器依赖命名管道，可能直接失败。单节点最稳，项目规模小、速度差异可忽略。
& dotnet build $solution -c $Configuration -m:1 --nologo --disable-build-servers | Out-Host
if ($LASTEXITCODE -ne 0) { Fail '构建失败。请查看上面的编译错误。' }

Write-Ok '构建完成'

# ---------------------------------------------------------------------------
if ($SkipTests) {
    Write-Step '跳过测试（-SkipTests）'
    Write-Info '注意：未执行单元测试'
}
else {
    Write-Step '运行单元测试'

    # 用独立进程调用 test.ps1：
    # 直接 `& script.ps1` 时 $LASTEXITCODE 反映的是脚本内最后一条原生命令的退出码，
    # 而非脚本自身的 exit N —— 测试进程偶发返回非 0 会被误判为"测试失败"。
    $testScript = Join-Path $PSScriptRoot 'test.ps1'
    & pwsh -NoProfile -File $testScript -Configuration $Configuration -NoBuild

    if ($LASTEXITCODE -ne 0) { Fail '单元测试未通过，已中止发布。' }

    Write-Ok '测试通过'
}

# ---------------------------------------------------------------------------
Write-Step '清理输出目录'

# 只清理目录"内容"，不删除目录本身。
# 目录本身可能被资源管理器或终端的工作目录占用（删除整目录会失败），
# 但只要里面的文件没被锁住，逐个删除是可行的；即便部分文件被锁，
# dotnet publish 仍能覆盖写入，因此清理失败降级为"继续覆盖"，不中止构建。
if (Test-Path $publishDir) {
    try {
        Get-ChildItem $publishDir -Force | Remove-Item -Recurse -Force -ErrorAction Stop
        Write-Ok '已清理旧产物'
    }
    catch {
        Write-Host '    旧产物未能完全清理，将直接覆盖发布。' -ForegroundColor Yellow
        Write-Info $_.Exception.Message
    }
}
else {
    New-Item -ItemType Directory -Force -Path $publishDir | Out-Null
    Write-Ok '已创建输出目录'
}

# ---------------------------------------------------------------------------
Write-Step '发布'

# 固定为「框架依赖」形态：
# - 不生成 zip：交付即目录，便于直接替换单个 exe 做增量测试
# - 不生成自包含版：单文件自解压会写 %TEMP%\.net，与"数据只写程序目录"的约束冲突
& dotnet publish $appProject `
    -c $Configuration `
    -r win-x64 `
    --self-contained false `
    -m:1 `
    --nologo `
    -o $publishDir `
    "-p:Version=$effectiveVersion" `
    "-p:InformationalVersion=$effectiveVersion" | Out-Host

if ($LASTEXITCODE -ne 0) { Fail '发布失败。' }

$exePath = Join-Path $publishDir 'Files.exe'
if (-not (Test-Path $exePath)) { Fail "发布完成但未找到 Files.exe：$exePath" }

Write-Ok '发布完成'

# ---------------------------------------------------------------------------
Write-Step '完成'

$totalBytes = (Get-ChildItem $publishDir -Recurse -File | Measure-Object -Property Length -Sum).Sum
$totalMb = [Math]::Round($totalBytes / 1MB, 2)
$fileCount = (Get-ChildItem $publishDir -Recurse -File | Measure-Object).Count

Write-Host ''
Write-Host '  产物目录：' -NoNewline; Write-Host $publishDir -ForegroundColor White
Write-Host '  可执行文件：' -NoNewline; Write-Host $exePath -ForegroundColor White
Write-Host "  版本：$effectiveVersion   文件数：$fileCount   总大小：$totalMb MB"
Write-Host ''
Write-Host '  运行要求：Windows 10 22H2+ / 11（仅 x64）+ .NET 10 桌面运行时' -ForegroundColor DarkGray
Write-Host '  首次运行可能被 SmartScreen 拦截：点「更多信息」→「仍要运行」，' -ForegroundColor DarkGray
Write-Host '  或先运行包内的 unblock.cmd 解除网络锁定。详见 docs/DEPLOY.md。' -ForegroundColor DarkGray
Write-Host ''

exit 0
