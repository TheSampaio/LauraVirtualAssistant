@echo off
powershell.exe -NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File "%~dp0Install.ps1" -Launch
exit /b %errorlevel%
