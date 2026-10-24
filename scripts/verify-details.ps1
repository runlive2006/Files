# 验证详情窗格：打开窗格 -> 选中条目 -> 读取窗格文本
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
if ($stale -gt 0) { Write-Output "CLEANED_STALE=$stale" }

$started = $null

try {
    $started = Start-TestProcess -Exe $Exe -LogDirectory $logDir -Tag 'details'
    $proc = $started.Process

    if (-not (Wait-MainWindow -Process $proc -TimeoutSeconds $WaitSeconds)) {
        Write-Output 'RESULT=NO_MAIN_WINDOW'
        return
    }

    Start-Sleep -Milliseconds 1200

    $pidCond = New-Object System.Windows.Automation.PropertyCondition($AE::ProcessIdProperty, [int]$proc.Id)
    $window = $AE::RootElement.FindFirst([System.Windows.Automation.TreeScope]::Children, $pidCond)

    if ($null -eq $window) {
        Write-Output 'RESULT=NO_WINDOW_IN_UIA'
        return
    }

    # 打开「视图」菜单 -> 勾选「详情窗格」
    $menuCond = New-Object System.Windows.Automation.PropertyCondition($AE::ControlTypeProperty, $CT::MenuItem)
    $viewMenu = $null
    foreach ($m in $window.FindAll([System.Windows.Automation.TreeScope]::Descendants, $menuCond)) {
        if ($m.Current.Name -eq '视图') { $viewMenu = $m; break }
    }

    if ($null -eq $viewMenu) {
        Write-Output 'RESULT=NO_VIEW_MENU'
        return
    }

    $viewMenu.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern).Expand()
    Start-Sleep -Milliseconds 700

    $detailsItem = $null
    foreach ($c in $viewMenu.FindAll([System.Windows.Automation.TreeScope]::Descendants, $menuCond)) {
        if ($c.Current.Name -like '详情窗格*') { $detailsItem = $c; break }
    }

    if ($null -eq $detailsItem) {
        Write-Output 'RESULT=NO_DETAILS_MENU_ITEM'
        return
    }

    $detailsItem.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern).Toggle()
    Start-Sleep -Milliseconds 1200

    # 收起菜单，避免遮挡
    try { $viewMenu.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern).Collapse() } catch { }
    Start-Sleep -Milliseconds 400

    # 读取详情窗格文本：三个 TextBlock，主标题是第一个非空的粗体行
    # 通过 UIA 的 Text 控件收集，取状态栏之上、列表之外的文本
    $textCond = New-Object System.Windows.Automation.PropertyCondition($AE::ControlTypeProperty, $CT::Text)
    $texts = $window.FindAll([System.Windows.Automation.TreeScope]::Descendants, $textCond)

    $captured = @()
    foreach ($t in $texts) {
        $n = $t.Current.Name
        if (-not [string]::IsNullOrWhiteSpace($n)) { $captured += $n }
    }

    Write-Output ('TEXT_COUNT=' + $captured.Count)

    # 详情窗格在无选中时显示当前目录名与统计
    $hasDetails = $false
    foreach ($n in $captured) {
        if ($n -match '个文件夹|个文件|空文件夹|未选择项目') { $hasDetails = $true; break }
    }

    Write-Output ('HAS_SUMMARY_TEXT=' + $hasDetails)
    Write-Output ('SAMPLE=' + (($captured | Select-Object -First 8) -join ' || '))

    if ($hasDetails) {
        Write-Output 'RESULT=DETAILS_OK'
    }
    else {
        Write-Output 'RESULT=DETAILS_TEXT_NOT_FOUND'
    }
}
catch {
    Write-Output ('RESULT=EXCEPTION ' + $_.Exception.Message)
}
finally {
    if ($null -ne $started) { Stop-TestProcess -Process $started.Process }
    $null = Clear-StaleProcesses -ProcessName 'Files' -Root $root
}
