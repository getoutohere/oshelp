@echo off
if not exist "%~dp0app\current.json" (
  powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0tools\Deploy.ps1"
  if errorlevel 1 exit /b 1
)
start "" "%~dp0app\OSHelp.exe" %*
