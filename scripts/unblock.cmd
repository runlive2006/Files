@echo off
rem 解除本文件夹内文件的网络锁定标记，并显示 SmartScreen 放行说明。
rem 优先使用 PowerShell 7（pwsh），缺失时回退到 Windows PowerShell。

setlocal

where pwsh >nul 2>nul
if %ERRORLEVEL%==0 (
    set "PSEXE=pwsh"
    set "PSARGS=-NoProfile -File"
) else (
    set "PSEXE=powershell"
    set "PSARGS=-NoProfile -ExecutionPolicy Bypass -File"
)

%PSEXE% %PSARGS% "%~dp0unblock.ps1" -Path "%~dp0.."
echo.
pause
