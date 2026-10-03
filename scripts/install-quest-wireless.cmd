@echo off
setlocal
powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%~dp0install-quest-wireless.ps1" %*
set "quest_install_exit=%ERRORLEVEL%"
echo.
pause
exit /b %quest_install_exit%
