@echo off
setlocal
powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%~dp0quest-saves.ps1" %*
set "quest_save_exit=%ERRORLEVEL%"
echo.
pause
exit /b %quest_save_exit%
