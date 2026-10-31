@echo off
setlocal
powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\repair-quest-access.ps1" %*
set "questRepairExit=%ERRORLEVEL%"
echo.
pause
exit /b %questRepairExit%
