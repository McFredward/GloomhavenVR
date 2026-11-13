@echo off
setlocal
powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%~dp0quest-builder-wizard.ps1" %*
set "questWizardExit=%ERRORLEVEL%"
if not "%questWizardExit%"=="0" pause
exit /b %questWizardExit%
