@echo off
setlocal
cd /d "%~dp0"
if not exist "%~dp0Start.ps1" (echo Start.ps1 was not found. & pause & exit /b 1)
powershell.exe -NoProfile -STA -ExecutionPolicy Bypass -File "%~dp0Start.ps1"
if errorlevel 1 (echo. & echo App startup failed. Check Documents\MusicTakeoutToolkit\startup-errors.log & pause)
