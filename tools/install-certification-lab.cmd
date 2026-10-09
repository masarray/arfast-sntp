@echo off
setlocal
echo WARNING: This can jump Windows system time by YEARS.
echo Only use on an ISOLATED CERTIFICATION TEST PC.
echo Never install clock stepping on live SCADA/SOE/historian systems.
choice /C YN /M "Confirm isolated lab PC and accept time step risk"
if errorlevel 2 exit /b 1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0install.ps1" -CertificationStep -AcceptLabRisk %*
if errorlevel 1 (
  echo Installation did not complete. See the error above.
  pause
)
