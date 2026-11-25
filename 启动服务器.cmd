@echo off
setlocal
set "SERVER_ROOT=%~dp0private-server"
if exist "%SERVER_ROOT%\OverField.ServerLauncher.exe" goto launch
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%SERVER_ROOT%\Build-Launcher.ps1"
if errorlevel 1 (
  echo Launcher build failed. Press any key to close.
  pause >nul
  exit /b 1
)
:launch
start "" "%SERVER_ROOT%\OverField.ServerLauncher.exe"
