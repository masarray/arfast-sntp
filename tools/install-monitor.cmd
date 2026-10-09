@echo off
setlocal
echo AR Fast SNTP - MONITOR ONLY installer
echo The Windows Service will NOT adjust system time.
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0install.ps1" %*
if errorlevel 1 (
  echo Installation did not complete. See the error above.
  pause
)
