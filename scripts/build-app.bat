@echo off
setlocal
cd /d "%~dp0.."
set CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
if not exist "%CSC%" set CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe
if not exist "%CSC%" (
  echo Could not find csc.exe. Install .NET Framework 4.x developer packing or Visual Studio.
  exit /b 1
)
"%CSC%" /nologo /optimize+ /target:winexe /platform:x64 /out:"%CD%\nvx.exe" /win32icon:"%CD%\nvx.ico" /reference:System.dll /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /win32manifest:"%CD%\resources\src\NvxApp\app.manifest" "%CD%\resources\src\NvxApp\Program.cs" "%CD%\resources\src\NvxApp\MainForm.cs" "%CD%\resources\src\NvxApp\NvxServices.cs" "%CD%\resources\src\NvxApp\NvxPrograms.cs" "%CD%\resources\src\NvxApp\NvxRuntimes.cs" "%CD%\resources\src\NvxApp\NvxTheme.cs" "%CD%\resources\src\NvxApp\NvxConfig.cs" "%CD%\resources\src\NvxApp\NvxDatabase.cs"
if errorlevel 1 exit /b 1
echo Built nvx.exe
