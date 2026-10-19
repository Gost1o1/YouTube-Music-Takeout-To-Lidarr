@echo off
cd /d "%~dp0"
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Build-EXE.ps1"
if errorlevel 1 (echo EXE creation failed; use Launch.cmd instead.)
pause
