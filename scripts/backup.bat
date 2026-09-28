@echo off
cd /d "%~dp0.."
echo Backing up databases, uploads, and configuration...
"%CD%\bin\nvx-server.exe" backup --no-pause
echo.
pause
