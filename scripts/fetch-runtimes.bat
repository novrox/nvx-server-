@echo off
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0fetch-runtimes.ps1" %*
