@echo off
setlocal
powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%~dp0collect-quest-logs.ps1" %*
set "quest_capture_exit=%ERRORLEVEL%"
echo.
pause
exit /b %quest_capture_exit%
