# 通过 UI Automation 客观验证列表是否真的渲染出条目。
# 用法：powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts\verify-render.ps1 -Exe <path>
param(
    [Parameter(Mandatory = $true)][string]$Exe,
    [int]$WaitSeconds = 8
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

$proc = Start-Process -FilePath $Exe -PassThru
Start-Sleep -Seconds $WaitSeconds

try {
    $root = [System.Windows.Automation.AutomationElement]::RootElement
    $pidCond = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::ProcessIdProperty, $proc.Id)

    $window = $null
    for ($i = 0; $i -lt 10 -and $null -eq $window; $i++) {
        $window = $root.FindFirst([System.Windows.Automation.TreeScope]::Children, $pidCond)
        if ($null -eq $window) { Start-Sleep -Milliseconds 500 }
    }

    if ($null -eq $window) {
        Write-Output "RESULT=NO_WINDOW"
        return
    }

    Write-Output ("WINDOW_TITLE=" + $window.Current.Name)

    # WPF ListView 配 GridView 在 UIA 里是 DataGrid；配普通视图时是 List
    $list = $null
    foreach ($controlType in @([System.Windows.Automation.ControlType]::DataGrid, [System.Windows.Automation.ControlType]::List)) {
        $cond = New-Object System.Windows.Automation.PropertyCondition(
            [System.Windows.Automation.AutomationElement]::ControlTypeProperty, $controlType)
        $list = $window.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $cond)
        if ($null -ne $list) {
            Write-Output ("LIST_CONTROL_TYPE=" + $list.Current.ControlType.ProgrammaticName)
            break
        }
    }

    if ($null -eq $list) {
        Write-Output "RESULT=NO_LIST"
        return
    }

    # 虚拟化列表只会把已实现的条目放进 UIA 树，因此这里数到的是"可见项"
    $items = $list.FindAll(
        [System.Windows.Automation.TreeScope]::Children,
        [System.Windows.Automation.Condition]::TrueCondition)

    Write-Output ("VISIBLE_ITEM_COUNT=" + $items.Count)

    $names = @()
    for ($i = 0; $i -lt [Math]::Min(5, $items.Count); $i++) {
        $names += $items[$i].Current.Name
    }
    Write-Output ("FIRST_ITEMS=" + ($names -join ' | '))

    if ($items.Count -gt 0) {
        Write-Output "RESULT=RENDERING_OK"
    }
    else {
        Write-Output "RESULT=EMPTY"
    }
}
finally {
    if (-not $proc.HasExited) {
        $null = $proc.CloseMainWindow()
        Start-Sleep -Seconds 2
        if (-not $proc.HasExited) { Stop-Process -Id $proc.Id -Force }
    }
}
