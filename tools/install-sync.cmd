@echo off
setlocal
set "SETUP=%~dp0ARFastSNTP.Setup.exe"
if not exist "%SETUP%" (
  echo ERROR: ARFastSNTP.Setup.exe not found. Extract the complete Windows artifact ZIP.
  pause
  exit /b 2
)
echo AR Fast SNTP - Install and Sync Windows Clock
echo For authorized isolated certification PCs only.
start "" "%SETUP%" --server 192.16.1.175 --year 2026 --lab
if errorlevel 1 (
  echo ERROR: Windows could not open the GUI setup.
  pause
  exit /b 1
)
exit /b 0
