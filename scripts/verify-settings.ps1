# 端到端验证设置窗口：启动 -> Ctrl+, -> 检查设置窗口控件 -> 关闭
param(
    [Parameter(Mandatory = $true)][string]$Exe,
    [int]$WaitSeconds = 8
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -AssemblyName System.Windows.Forms

$AE = [System.Windows.Automation.AutomationElement]
$CT = [System.Windows.Automation.ControlType]

$proc = Start-Process -FilePath $Exe -PassThru
Start-Sleep -Seconds $WaitSeconds

try {
    $root = $AE::RootElement
    $pidCond = New-Object System.Windows.Automation.PropertyCondition($AE::ProcessIdProperty, $proc.Id)

    $main = $null
    for ($i = 0; $i -lt 12 -and $null -eq $main; $i++) {
        $main = $root.FindFirst([System.Windows.Automation.TreeScope]::Children, $pidCond)
        if ($null -eq $main) { Start-Sleep -Milliseconds 500 }
    }

    if ($null -eq $main) {
        Write-Output 'RESULT=NO_MAIN_WINDOW'
        return
    }

    Write-Output ("MAIN_WINDOW=" + $main.Current.Name)

    $main.SetFocus()
    Start-Sleep -Milliseconds 400

    [System.Windows.Forms.SendKeys]::SendWait('^,')
    Start-Sleep -Seconds 2

    $windows = $root.FindAll([System.Windows.Automation.TreeScope]::Children, $pidCond)
    $settings = $null
    foreach ($w in $windows) {
        if ($w.Current.Name -eq '设置') { $settings = $w; break }
    }

    if ($null -eq $settings) {
        Write-Output 'RESULT=SETTINGS_WINDOW_NOT_FOUND'
        return
    }

    Write-Output 'SETTINGS_WINDOW_OPENED=True'

    foreach ($type in @($CT::CheckBox, $CT::ComboBox, $CT::Slider, $CT::List, $CT::Edit, $CT::Button)) {
        $cond = New-Object System.Windows.Automation.PropertyCondition($AE::ControlTypeProperty, $type)
        $found = $settings.FindAll([System.Windows.Automation.TreeScope]::Descendants, $cond)
        Write-Output ("  " + $type.ProgrammaticName.Replace('ControlType.', '') + " = " + $found.Count)
    }

    $listCond = New-Object System.Windows.Automation.PropertyCondition($AE::ControlTypeProperty, $CT::List)
    $lists = $settings.FindAll([System.Windows.Automation.TreeScope]::Descendants, $listCond)
    if ($lists.Count -gt 0) {
        $items = $lists[0].FindAll([System.Windows.Automation.TreeScope]::Children, [System.Windows.Automation.Condition]::TrueCondition)
        $names = @()
        foreach ($it in $items) { $names += $it.Current.Name }
        Write-Output ("  GROUPS=" + ($names -join ','))
    }

    Write-Output 'RESULT=SETTINGS_OK'
}
finally {
    if (-not $proc.HasExited) {
        $null = $proc.CloseMainWindow()
        Start-Sleep -Seconds 2
        if (-not $proc.HasExited) { Stop-Process -Id $proc.Id -Force }
    }
}
