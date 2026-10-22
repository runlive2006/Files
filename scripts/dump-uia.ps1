# 打印运行中窗口的 UI Automation 树，用于排查"列表空白"这类渲染问题。
# 用法：powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts\dump-uia.ps1 -Exe <path> [-MaxNodes 60]
param(
    [Parameter(Mandatory = $true)][string]$Exe,
    [int]$WaitSeconds = 8,
    [int]$MaxNodes = 60
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

    if ($null -eq $window) { Write-Output 'NO_WINDOW'; return }

    $walker = [System.Windows.Automation.TreeWalker]::ControlViewWalker
    $script:count = 0

    function Show-Node {
        param($element, $depth)

        if ($null -eq $element -or $script:count -ge $MaxNodes) { return }
        $script:count++

        $indent = '  ' * $depth
        $type = $element.Current.ControlType.ProgrammaticName -replace 'ControlType\.', ''
        $name = $element.Current.Name
        if ($name.Length -gt 40) { $name = $name.Substring(0, 40) + "..." }

        Write-Output ($indent + "[" + $type + "] " + $name)

        $child = $walker.GetFirstChild($element)
        while ($null -ne $child -and $script:count -lt $MaxNodes) {
            Show-Node -element $child -depth ($depth + 1)
            $child = $walker.GetNextSibling($child)
        }
    }

    Show-Node -element $window -depth 0
    Write-Output ("TOTAL_NODES=" + $script:count)
}
finally {
    if (-not $proc.HasExited) {
        $null = $proc.CloseMainWindow()
        Start-Sleep -Seconds 2
        if (-not $proc.HasExited) { Stop-Process -Id $proc.Id -Force }
    }
}
