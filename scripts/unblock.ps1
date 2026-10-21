# 解除 Windows 对下载文件的锁定（Mark-of-the-Web）
#
# 从网络下载/通过聊天工具传输的 exe 会被打上 Zone.Identifier 标记，
# 从而触发 SmartScreen 的"无法识别的应用"拦截。
# 本脚本只移除该标记，不修改文件内容、不修改系统设置。

[CmdletBinding()]
param(
    [string]$Path = (Split-Path -Parent $PSScriptRoot)
)

$ErrorActionPreference = 'Stop'
$target = (Resolve-Path $Path).Path

Write-Host "目标：$target" -ForegroundColor Cyan

$extensions = @('.exe', '.dll', '.zip', '.ps1', '.cmd')
$files = Get-ChildItem -Path $target -Recurse -File -ErrorAction SilentlyContinue |
    Where-Object { $extensions -contains $_.Extension.ToLowerInvariant() }

if (-not $files) {
    Write-Host '未找到需要处理的文件。' -ForegroundColor Yellow
    exit 0
}

$changed = 0
foreach ($file in $files) {
    try {
        $hasMark = Get-Item -LiteralPath $file.FullName -Stream Zone.Identifier -ErrorAction SilentlyContinue
        if ($hasMark) {
            Unblock-File -LiteralPath $file.FullName
            Write-Host "已解除锁定：$($file.Name)" -ForegroundColor Green
            $changed++
        }
    }
    catch {
        Write-Warning "处理失败：$($file.FullName) — $($_.Exception.Message)"
    }
}

Write-Host ''
Write-Host "完成：共处理 $($files.Count) 个文件，解除锁定 $changed 个。" -ForegroundColor Cyan

if ($changed -eq 0) {
    Write-Host '没有文件带网络锁定标记。若仍被 SmartScreen 拦截，请按下面的说明操作。' -ForegroundColor Yellow
}

Write-Host ''
Write-Host '若 SmartScreen 仍然拦截（未签名程序的正常行为）：' -ForegroundColor Yellow
Write-Host '  1. 点击弹窗中的「更多信息」'
Write-Host '  2. 点击「仍要运行」'
Write-Host ''
Write-Host '彻底消除该提示需要为程序购买代码签名证书（详见 docs/DEPLOY.md）。'
