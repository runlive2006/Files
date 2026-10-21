@echo off
rem 双击运行：解除本文件夹内文件的网络锁定标记，并显示 SmartScreen 放行说明。
setlocal
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0unblock.ps1" -Path "%~dp0.."
echo.
pause
