@echo off
rem 双击本文件即可构建 Files 文件管理器。
rem
rem 需要先安装 .NET SDK 10：https://dotnet.microsoft.com/download/dotnet/10.0
rem 自动化/CI 场景请直接调用 build.ps1（本文件结束时会暂停等待按键）。
rem
rem 优先使用 PowerShell 7（pwsh）。本仓库脚本使用 PS7 语法，
rem Windows PowerShell 5.1 下部分写法无法解析；没有 pwsh 时回退并提示升级。

setlocal

where pwsh >nul 2>nul
if %ERRORLEVEL%==0 (
    set "PSEXE=pwsh"
    set "PSARGS=-NoProfile -File"
) else (
    set "PSEXE=powershell"
    set "PSARGS=-NoProfile -ExecutionPolicy Bypass -File"
    echo.
    echo [提示] 未检测到 PowerShell 7 ^(pwsh^)，将尝试使用 Windows PowerShell 5.1。
    echo        若构建因语法错误失败，请安装 PowerShell 7：
    echo        https://github.com/PowerShell/PowerShell/releases
    echo.
)

%PSEXE% %PSARGS% "%~dp0build.ps1" %*
set "EXITCODE=%ERRORLEVEL%"

echo.
if not "%EXITCODE%"=="0" (
    echo 构建失败，退出码 %EXITCODE%
) else (
    echo 构建成功。
)
echo.
pause
exit /b %EXITCODE%
