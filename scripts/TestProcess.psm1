# 启动被测程序并保证「不卡住调用方」的公共封装。
#
# 背景（踩过的坑）：
#   直接用 `Start-Process -FilePath $exe -PassThru` 启动 GUI 程序时，子进程会继承
#   父进程的 stdout/stderr 句柄。自动化工具调用在等待输出管道关闭时就会一直挂住，
#   即使 GUI 早已启动完成。表现为"每次验证都要人手动干预/手动 kill"。
#
# 本模块的三条保证：
#   1. 子进程的标准输出/错误重定向到文件，不继承调用方管道；
#   2. 所有等待都有上限（硬超时），超时后强制结束；
#   3. 无论正常/异常/超时，都保证进程被清理（finally + 兜底 kill）。

Set-StrictMode -Version Latest

$script:LogDirectory = $null

function Initialize-TestLogDirectory {
    param([Parameter(Mandatory = $true)][string]$Root)

    $path = Join-Path $Root '.dotnet/testlogs'
    New-Item -ItemType Directory -Force -Path $path | Out-Null
    $script:LogDirectory = $path
    return $path
}

<#
.SYNOPSIS
    启动被测程序，返回进程对象。
.DESCRIPTION
    标准输出与错误被重定向到 .dotnet/testlogs 下的文件，
    因此调用方不会因为管道未关闭而挂住。
#>
function Start-TestProcess {
    param(
        [Parameter(Mandatory = $true)][string]$Exe,
        [Parameter(Mandatory = $true)][string]$LogDirectory,
        [string]$Tag = 'run'
    )

    if (-not (Test-Path $Exe)) {
        throw "找不到可执行文件：$Exe"
    }

    $stamp = Get-Date -Format 'HHmmss-fff'
    $outLog = Join-Path $LogDirectory "$Tag-$stamp.out.log"
    $errLog = Join-Path $LogDirectory "$Tag-$stamp.err.log"

    $proc = Start-Process -FilePath $Exe `
        -PassThru `
        -RedirectStandardOutput $outLog `
        -RedirectStandardError $errLog

    # 注意：PowerShell 5.1 无法解析 `return [pscustomobject]@{ ... }`，
    # 必须先赋值给变量再返回
    $result = [pscustomobject]@{
        Process   = $proc
        OutputLog = $outLog
        ErrorLog  = $errLog
    }

    return $result
}

<#
.SYNOPSIS
    等待进程出现主窗口，超时则返回 $false。
#>
function Wait-MainWindow {
    param(
        [Parameter(Mandatory = $true)]$Process,
        [int]$TimeoutSeconds = 15
    )

    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)

    while ((Get-Date) -lt $deadline) {
        if ($Process.HasExited) {
            return $false
        }

        $Process.Refresh()
        if ($Process.MainWindowHandle.ToInt64() -ne 0) {
            return $true
        }

        Start-Sleep -Milliseconds 200
    }

    return $false
}

<#
.SYNOPSIS
    结束进程：先请求正常关闭，超时后强制结束。
.DESCRIPTION
    模态对话框（MessageBox）会阻止窗口正常关闭，因此必须有强制兜底。
#>
function Stop-TestProcess {
    param(
        [Parameter(Mandatory = $true)]$Process,
        [int]$GracefulTimeoutSeconds = 3
    )

    if ($null -eq $Process -or $Process.HasExited) {
        return
    }

    try {
        $null = $Process.CloseMainWindow()
    }
    catch {
        # 窗口可能已销毁，忽略
    }

    $deadline = (Get-Date).AddSeconds($GracefulTimeoutSeconds)
    while ((Get-Date) -lt $deadline) {
        $Process.Refresh()
        if ($Process.HasExited) {
            return
        }

        Start-Sleep -Milliseconds 150
    }

    try {
        Stop-Process -Id $Process.Id -Force -ErrorAction Stop
    }
    catch {
        # 进程可能刚好自行退出
    }

    # 等待句柄释放，避免后续删除/覆盖文件时被占用
    try {
        $null = $Process.WaitForExit(3000)
    }
    catch {
        # 忽略
    }
}

<#
.SYNOPSIS
    清理指定名称的残留进程（仅限路径位于给定根目录下的）。
.DESCRIPTION
    上一次验证异常中断时可能留下进程，占用输出文件导致后续构建失败。
    只处理路径匹配的进程，避免误杀用户自己打开的其他副本。
#>
function Clear-StaleProcesses {
    param(
        [Parameter(Mandatory = $true)][string]$ProcessName,
        [Parameter(Mandatory = $true)][string]$Root
    )

    $killed = 0
    foreach ($p in @(Get-Process -Name $ProcessName -ErrorAction SilentlyContinue)) {
        try {
            $path = $p.Path
        }
        catch {
            # 无权读取路径时跳过，不冒险
            continue
        }

        if ([string]::IsNullOrEmpty($path)) {
            continue
        }

        if (-not $path.StartsWith($Root, [StringComparison]::OrdinalIgnoreCase)) {
            continue
        }

        try {
            Stop-Process -Id $p.Id -Force -ErrorAction Stop
            $killed++
        }
        catch {
            # 忽略
        }
    }

    return $killed
}

<#
.SYNOPSIS
    读取子进程的错误日志（用于诊断启动失败）。
#>
function Read-ErrorLog {
    param([Parameter(Mandatory = $true)][string]$Path, [int]$MaxLines = 10)

    if (-not (Test-Path $Path)) {
        return @()
    }

    return @(Get-Content $Path -ErrorAction SilentlyContinue | Select-Object -First $MaxLines)
}
