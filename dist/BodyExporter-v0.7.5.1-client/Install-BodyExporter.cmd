@echo off
setlocal EnableExtensions
cd /d "%~dp0"

REM Da la Admin chua?
net session >nul 2>&1
if %ERRORLEVEL% NEQ 0 (
    echo.
    echo Can quyen Administrator. Bam YES tren hop thoai UAC...
    echo.
    powershell -NoProfile -ExecutionPolicy Bypass -Command ^
      "Start-Process -FilePath '%~f0' -Verb RunAs -Wait"
    exit /b %ERRORLEVEL%
)

title SolidWorks Body Exporter - Installer
echo.
echo ==============================================
echo   SolidWorks Body Exporter - Installer
echo ==============================================
echo.

powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0_install.ps1"
set "RC=%ERRORLEVEL%"

echo.
if "%RC%" NEQ "0" (
    echo *** CAI DAT LOI (ma %RC%) ***
) else (
    echo *** CAI DAT XONG ***
)
echo.
pause
exit /b %RC%
