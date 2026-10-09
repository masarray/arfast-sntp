@echo off
setlocal
echo WARNING: Only use on an isolated certification test PC.
echo Enabling lab correction can step the Windows clock by YEARS.
echo Never run this on a live SCADA/SOE/historian host.
set "CHOICE_EXE=%SystemRoot%\System32\choice.exe"
if not exist "%CHOICE_EXE%" (
  echo ERROR: Confirmation tool unavailable. Lab time stepping was NOT enabled.
  pause
  exit /b 2
)
"%CHOICE_EXE%" /C YN /M "Confirm isolated lab PC and accept time-step risk"
if errorlevel 2 exit /b 1
set "PS_EXE=%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe"
if not exist "%PS_EXE%" (
  echo ERROR: Windows PowerShell unavailable. No installation was performed.
  pause
  exit /b 2
)
"%PS_EXE%" -NoProfile -ExecutionPolicy Bypass -File "%~dp0install.ps1" -CertificationStep -AcceptLabRisk %*
set "RESULT=%ERRORLEVEL%"
if not "%RESULT%"=="0" (
  echo Lab installation failed or was cancelled. Check the error above.
  pause
)
exit /b %RESULT%
