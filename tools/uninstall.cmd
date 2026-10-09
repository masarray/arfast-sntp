@echo off
setlocal
set "SETUP=%~dp0ARFastSNTP.Setup.exe"
if not exist "%SETUP%" (
  echo ERROR: ARFastSNTP.Setup.exe is missing.
  pause
  exit /b 2
)
start "" "%SETUP%" --remove
if errorlevel 1 (
  echo ERROR: Cannot open graphical setup.
  pause
  exit /b 1
)
exit /b 0
