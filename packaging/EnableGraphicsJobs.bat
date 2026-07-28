@echo off
REM GloomhavenVR - one double-click to turn on threaded render submission.
REM
REM Why this exists: Unity reads boot.config during engine startup, before any mod
REM code exists, so the mod itself can only apply the setting on the NEXT start.
REM Running this once, before you launch the game, is what removes the
REM "start the game twice" step. It is safe to run more than once.
REM
REM Leave this file next to GH.exe (that is where it lands if you extracted the
REM mod zip into the game folder) and double-click it.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0EnableGraphicsJobs.ps1" -GamePath "%~dp0."
echo.
pause
