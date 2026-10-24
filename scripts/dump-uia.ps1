# 打印运行中窗口的 UI Automation 树，用于排查"列表空白"这类渲染问题。
# 用法：pwsh -NoProfile -File scripts\dump-uia.ps1 -Exe <path> [-MaxNodes 60]

param(
    [Parameter(Mandatory = $true)][string]$Exe,
    [int]$WaitSeconds = 8,
    [int]$MaxNodes = 60
)

$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'TestProcess.psm1') -Force

Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

$AE = [System.Windows.Automation.AutomationElement]
$root = Split-Path -Parent $PSScriptRoot
$logDir = Initialize-TestLogDirectory -Root $root

$stale = Clear-StaleProcesses -ProcessName 'Files' -Root $root
if ($stale -gt 0) {
    Write-Output "CLEANED_STALE=$stale"
}

$started = $null
$script:count = 0

function Show-Node {
    param($element, $depth)

    if ($null -eq $element -or $script:count -ge $MaxNodes) {
        return
    }

    $script:count++

    $indent = '  ' * $depth
    $type = $element.Current.ControlType.ProgrammaticName.Replace('ControlType.', '')
    $name = $element.Current.Name

    if ($name.Length -gt 40) {
        $name = $name.Substring(0, 40) + '...'
    }

    Write-Output ($indent + '[' + $type + '] ' + $name)

    $walker = [System.Windows.Automation.TreeWalker]::ControlViewWalker
    $child = $walker.GetFirstChild($element)

    while ($null -ne $child -and $script:count -lt $MaxNodes) {
        Show-Node -element $child -depth ($depth + 1)
        $child = $walker.GetNextSibling($child)
    }
}

try {
    $started = Start-TestProcess -Exe $Exe -LogDirectory $logDir -Tag 'dump'
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

    Start-Sleep -Milliseconds 1000

    $pidCond = New-Object System.Windows.Automation.PropertyCondition($AE::ProcessIdProperty, $proc.Id)
    $window = $AE::RootElement.FindFirst([System.Windows.Automation.TreeScope]::Children, $pidCond)

    if ($null -eq $window) {
        Write-Output 'RESULT=NO_WINDOW_IN_UIA'
        return
    }

    Show-Node -element $window -depth 0
    Write-Output ('TOTAL_NODES=' + $script:count)
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
