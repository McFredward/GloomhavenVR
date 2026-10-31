@echo off
setlocal
powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%~dp0quest-builder-wizard.ps1" %*
set "quest_wizard_exit=%ERRORLEVEL%"
if not "%quest_wizard_exit%"=="0" (
  echo.
  pause
)
exit /b %quest_wizard_exit%
