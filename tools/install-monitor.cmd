@echo off
setlocal
set "SETUP=%~dp0ARFastSNTP.Setup.exe"
if not exist "%SETUP%" (
  echo ERROR: ARFastSNTP.Setup.exe not found. Extract all artifact files first.
  pause
  exit /b 2
)
echo Monitor-only diagnostic service (does not set Windows clock).
start "" "%SETUP%" --monitor-only --server 192.16.1.175 --year 2026
exit /b %ERRORLEVEL%
