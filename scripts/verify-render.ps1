# 通过 UI Automation 客观验证列表是否真的渲染出条目。
# 用法：pwsh -NoProfile -File scripts\verify-render.ps1 -Exe <path>
#
# 说明：本脚本通过 TestProcess 模块启动被测程序（重定向输出 + 硬超时 + 保证清理），
# 因此不会挂住调用方。

param(
    [Parameter(Mandatory = $true)][string]$Exe,
    [int]$WaitSeconds = 8
)

$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'TestProcess.psm1') -Force

Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

$AE = [System.Windows.Automation.AutomationElement]
$CT = [System.Windows.Automation.ControlType]

$root = Split-Path -Parent $PSScriptRoot
$logDir = Initialize-TestLogDirectory -Root $root

# 清理上次可能残留的实例，避免互相干扰
$stale = Clear-StaleProcesses -ProcessName 'Files' -Root $root
if ($stale -gt 0) {
    Write-Output "CLEANED_STALE=$stale"
}

$started = $null

try {
    $started = Start-TestProcess -Exe $Exe -LogDirectory $logDir -Tag 'render'
    $proc = $started.Process

    # 等待主窗口出现（有上限，不会无限等）
    if (-not (Wait-MainWindow -Process $proc -TimeoutSeconds $WaitSeconds)) {
        if ($proc.HasExited) {
            Write-Output ("RESULT=PROCESS_EXITED code=" + $proc.ExitCode)
            Read-ErrorLog -Path $started.ErrorLog | ForEach-Object { Write-Output ("  ERR: " + $_) }
        }
        else {
            Write-Output 'RESULT=NO_MAIN_WINDOW'
        }

        return
    }

    Write-Output ("WINDOW_TITLE=" + $proc.MainWindowTitle)

    # 给列表一点时间完成布局
    Start-Sleep -Milliseconds 1200

    $pidCond = New-Object System.Windows.Automation.PropertyCondition($AE::ProcessIdProperty, $proc.Id)
    $window = $AE::RootElement.FindFirst([System.Windows.Automation.TreeScope]::Children, $pidCond)

    if ($null -eq $window) {
        Write-Output 'RESULT=NO_WINDOW_IN_UIA'
        return
    }

    # WPF ListView 配 GridView 在 UIA 里是 DataGrid；配普通视图时是 List
    $list = $null
    foreach ($controlType in @($CT::DataGrid, $CT::List)) {
        $cond = New-Object System.Windows.Automation.PropertyCondition($AE::ControlTypeProperty, $controlType)
        $list = $window.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $cond)
        if ($null -ne $list) {
            Write-Output ("LIST_CONTROL_TYPE=" + $list.Current.ControlType.ProgrammaticName)
            break
        }
    }

    if ($null -eq $list) {
        Write-Output 'RESULT=NO_LIST'
        return
    }

    $items = $list.FindAll(
        [System.Windows.Automation.TreeScope]::Children,
        [System.Windows.Automation.Condition]::TrueCondition)

    # GridView 的表头在 UIA 中是一个空名子节点，统计时必须排除，
    # 否则计数恒多 1（曾因此误判过滤未生效）
    $names = @()
    foreach ($it in $items) {
        $n = $it.Current.Name
        if (-not [string]::IsNullOrWhiteSpace($n)) {
            $names += $n
        }
    }

    Write-Output ("VISIBLE_ITEM_COUNT=" + $names.Count)
    Write-Output ("FIRST_ITEMS=" + (($names | Select-Object -First 5) -join ' | '))

    if ($names.Count -gt 0) {
        Write-Output 'RESULT=RENDERING_OK'
    }
    else {
        Write-Output 'RESULT=EMPTY'
    }
}
catch {
    Write-Output ("RESULT=EXCEPTION " + $_.Exception.Message)
}
finally {
    if ($null -ne $started) {
        Stop-TestProcess -Process $started.Process
    }

    # 兜底：清理可能遗留的实例
    $null = Clear-StaleProcesses -ProcessName 'Files' -Root $root
}
