@echo off
rem Builds CondorPreFlight.exe using the C# compiler that ships with Windows. Nothing to install.
set CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
if not exist "%CSC%" set CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe
if not exist "%CSC%" ( echo Could not find the C# compiler. & pause & exit /b 1 )
"%CSC%" /nologo /target:winexe /optimize+ /out:CondorPreFlight.exe /r:System.Windows.Forms.dll /r:System.Drawing.dll Ini.cs Config.cs Displays.cs Wizard.cs Demo.cs MainForm.cs
if errorlevel 1 ( echo. & echo BUILD FAILED & pause & exit /b 1 )
echo Built CondorPreFlight.exe
pause
