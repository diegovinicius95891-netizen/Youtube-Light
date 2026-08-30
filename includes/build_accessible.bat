@echo off
setlocal
cd /d "%~dp0"
set CSC=C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe
if not exist "%CSC%" set CSC=C:\Windows\Microsoft.NET\Framework\v4.0.30319\csc.exe
"%CSC%" /nologo /codepage:65001 /target:winexe /platform:anycpu /out:..\YoutubeMusicLightAccessible.exe /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Net.Http.dll /r:System.Windows.Forms.dll /r:System.Web.Extensions.dll /r:Microsoft.VisualBasic.dll /r:..\librarys\dotnet\NAudio.dll YoutubeMusicLightAccessible.cs
if errorlevel 1 exit /b 1
