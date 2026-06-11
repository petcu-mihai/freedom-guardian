@echo off
REM One-click build + install. Will prompt for administrator rights.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0setup.ps1" %*
