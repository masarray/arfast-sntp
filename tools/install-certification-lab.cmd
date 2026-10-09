@echo off
setlocal
set "SETUP=%~dp0ARFastSNTP.Setup.exe"
if not exist "%SETUP%" (
  echo ERROR: ARFastSNTP.Setup.exe was not found beside this script.
  pause
  exit /b 2
)
echo LAB MODE WARNING: never install clock stepping on live SCADA/SOE/historian computers.
echo The graphical setup will require explicit isolated-lab acknowledgement.
start "" "%SETUP%" --server 192.16.1.175 --year 2026 --lab
if errorlevel 1 (
  echo ERROR: Windows blocked the graphical setup.
  pause
  exit /b 1
)
exit /b 0
