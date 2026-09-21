@echo off
cd /d "%~dp0"
if not exist "bin\Perfview.exe" (
    powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0build.ps1"
    if errorlevel 1 exit /b 1
)
start "" "%~dp0bin\Perfview.exe"

