@echo off
setlocal EnableExtensions
cd /d "%~dp0"

REM ===================================================================
REM  Freedom Guardian - Uninstaller
REM
REM  Just double-click this file. It will ask for administrator rights
REM  and then remove the Freedom Guardian services and files.
REM
REM  IMPORTANT: If protection is still active you must FIRST run "unlock"
REM  and wait out the cooldown:
REM      "C:\Program Files\FreedomGuardian\FreedomGuardian.exe" unlock
REM  Until that cooldown elapses this uninstaller will safely refuse and
REM  print Safe Mode instructions instead.
REM ===================================================================

REM --- Make sure we are running as administrator; self-elevate if not. ---
net session >nul 2>&1
if errorlevel 1 (
    echo Requesting administrator rights...
    if "%~1"=="" (
        powershell -NoProfile -ExecutionPolicy Bypass -Command "Start-Process -FilePath '%~f0' -Verb RunAs"
    ) else (
        powershell -NoProfile -ExecutionPolicy Bypass -Command "Start-Process -FilePath '%~f0' -Verb RunAs -ArgumentList '%*'"
    )
    exit /b
)

REM --- Now elevated: run the uninstaller script in this same window. ---
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0uninstall.ps1" %*

echo.
pause
