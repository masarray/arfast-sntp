@echo off
setlocal
rem Compatibility shortcut: the canonical one-click setup is install-sync.cmd
call "%~dp0install-sync.cmd" %*
exit /b %ERRORLEVEL%
