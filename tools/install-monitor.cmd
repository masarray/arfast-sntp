@echo off
setlocal
echo AR Fast SNTP - MONITOR ONLY installer
echo This service monitors SNTP but will NOT adjust Windows time.
set "PS_EXE=%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe"
if not exist "%PS_EXE%" (
  echo ERROR: Windows PowerShell not found at the standard Windows path.
  echo You can still open dashboard\ARFastSNTP.Dashboard.exe and use CONNECT.
  pause
  exit /b 2
)
"%PS_EXE%" -NoProfile -ExecutionPolicy Bypass -File "%~dp0install.ps1" %*
set "RESULT=%ERRORLEVEL%"
if not "%RESULT%"=="0" (
  echo Installation failed or was cancelled. Check the error above.
  pause
)
exit /b %RESULT%
