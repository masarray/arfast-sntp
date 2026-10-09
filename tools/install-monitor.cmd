@echo off
setlocal
set "SETUP=%~dp0ARFastSNTP.Setup.exe"
if not exist "%SETUP%" (
  echo ERROR: ARFastSNTP.Setup.exe was not found beside this script.
  echo Download the latest successful Windows artifact and extract all files.
  pause
  exit /b 2
)
start "" "%SETUP%" --server 192.16.1.175 --year 2026
if errorlevel 1 (
  echo ERROR: Windows blocked the graphical installer.
  pause
  exit /b 1
)
exit /b 0
