@echo off
if not exist "%~dp0artifacts\preview\OSHelp.exe" (
  powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0tools\Build.ps1"
  if errorlevel 1 exit /b 1
)
start "" "%~dp0artifacts\preview\OSHelp.exe" --repository "%~dp0." --game "%~dp0.." --data-dir "%~dp0local\preview-userdata" %*
