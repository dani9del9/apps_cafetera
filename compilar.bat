@echo off
setlocal
cd /d "%~dp0"
set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" set "CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
if not exist "%CSC%" (
  echo No se encuentra csc.exe de .NET Framework 4.
  pause
  exit /b 1
)
"%CSC%" /nologo /target:winexe /optimize+ /win32icon:cafetera.ico /out:Cafetera.exe /r:System.Windows.Forms.dll /r:System.Drawing.dll Cafetera.cs
if errorlevel 1 (
  echo.
  echo ERROR al compilar
  pause
  exit /b 1
)
echo Compilado: Cafetera.exe
