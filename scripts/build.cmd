@echo off
rem 双击本文件即可构建 Files 文件管理器。
rem 等价于在仓库根目录执行：powershell -ExecutionPolicy Bypass -File scripts\build.ps1
rem
rem 需要先安装 .NET SDK 10：https://dotnet.microsoft.com/download/dotnet/10.0
rem 自动化/CI 场景请直接调用 build.ps1（本文件结束时会暂停等待按键）。

setlocal
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0build.ps1" %*
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
