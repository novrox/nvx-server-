@echo off
cd /d "%~dp0.."
echo NVX Server component check
"%CD%\bin\nvx-server.exe" update --no-pause
echo.
echo To update PHP or MySQL, run fetch-runtimes.bat. Apache is optional: place httpd.exe in apache\bin. Then run restart.bat.
pause
