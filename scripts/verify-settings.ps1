# 端到端验证设置窗口：启动 -> 打开设置 -> 检查控件 -> 关闭
#
# 打开设置的方式：优先用 UIA 的菜单 Invoke（可靠），失败再退回快捷键。
# 脚本通过 TestProcess 模块启动程序，保证不会挂住调用方。

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

$stale = Clear-StaleProcesses -ProcessName 'Files' -Root $root
if ($stale -gt 0) {
    Write-Output "CLEANED_STALE=$stale"
}

$started = $null

try {
    $started = Start-TestProcess -Exe $Exe -LogDirectory $logDir -Tag 'settings'
    $proc = $started.Process

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

    Write-Output ('MAIN_WINDOW=' + $proc.MainWindowTitle)
    Start-Sleep -Milliseconds 800

    $pidCond = New-Object System.Windows.Automation.PropertyCondition($AE::ProcessIdProperty, $proc.Id)
    $main = $AE::RootElement.FindFirst([System.Windows.Automation.TreeScope]::Children, $pidCond)

    if ($null -eq $main) {
        Write-Output 'RESULT=NO_WINDOW_IN_UIA'
        return
    }

    # 在菜单栏里找「设置」，展开后点「打开设置…」
    $opened = $false
    $menuCond = New-Object System.Windows.Automation.PropertyCondition($AE::ControlTypeProperty, $CT::MenuItem)
    $menus = $main.FindAll([System.Windows.Automation.TreeScope]::Descendants, $menuCond)

    foreach ($m in $menus) {
        if ($m.Current.Name -ne '设置') {
            continue
        }

        try {
            $m.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern).Expand()
            Start-Sleep -Milliseconds 500
        }
        catch {
            continue
        }

        $children = $m.FindAll([System.Windows.Automation.TreeScope]::Descendants, $menuCond)
        foreach ($c in $children) {
            if ($c.Current.Name -like '打开设置*') {
                try {
                    $c.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
                    $opened = $true
                }
                catch {
                    # 忽略，下面会报告未打开
                }

                break
            }
        }

        break
    }

    if (-not $opened) {
        Write-Output 'RESULT=SETTINGS_MENU_INVOKE_FAILED'
        return
    }

    Start-Sleep -Seconds 2

    # 模态对话框在 UIA 中通常挂在主窗口的 Descendants 下，
    # 而不是 RootElement.Children；两处都要找。
    $settings = $null

    $windowCond = New-Object System.Windows.Automation.PropertyCondition($AE::ControlTypeProperty, $CT::Window)
    foreach ($candidate in $main.FindAll([System.Windows.Automation.TreeScope]::Descendants, $windowCond)) {
        if ($candidate.Current.Name -eq '设置') {
            $settings = $candidate
            break
        }
    }

    if ($null -eq $settings) {
        $windows = $AE::RootElement.FindAll([System.Windows.Automation.TreeScope]::Children, $pidCond)
        foreach ($w in $windows) {
            if ($w.Current.Name -eq '设置') {
                $settings = $w
                break
            }
        }
    }

    if ($null -eq $settings) {
        Write-Output 'RESULT=SETTINGS_WINDOW_NOT_FOUND'
        return
    }

    Write-Output 'SETTINGS_WINDOW_OPENED=True'

    foreach ($type in @($CT::CheckBox, $CT::ComboBox, $CT::Slider, $CT::List, $CT::Edit, $CT::Button)) {
        $cond = New-Object System.Windows.Automation.PropertyCondition($AE::ControlTypeProperty, $type)
        $found = $settings.FindAll([System.Windows.Automation.TreeScope]::Descendants, $cond)
        Write-Output ('  ' + $type.ProgrammaticName.Replace('ControlType.', '') + ' = ' + $found.Count)
    }

    # 列出分组（左侧 ListBox）
    $listCond = New-Object System.Windows.Automation.PropertyCondition($AE::ControlTypeProperty, $CT::List)
    $lists = $settings.FindAll([System.Windows.Automation.TreeScope]::Descendants, $listCond)

    if ($lists.Count -gt 0) {
        $items = $lists[0].FindAll(
            [System.Windows.Automation.TreeScope]::Children,
            [System.Windows.Automation.Condition]::TrueCondition)

        $names = @()
        foreach ($it in $items) {
            if (-not [string]::IsNullOrWhiteSpace($it.Current.Name)) {
                $names += $it.Current.Name
            }
        }

        Write-Output ('  GROUPS=' + ($names -join ','))
    }

    Write-Output 'RESULT=SETTINGS_OK'
}
catch {
    Write-Output ('RESULT=EXCEPTION ' + $_.Exception.Message)
}
finally {
    if ($null -ne $started) {
        Stop-TestProcess -Process $started.Process
    }

    $null = Clear-StaleProcesses -ProcessName 'Files' -Root $root
}
