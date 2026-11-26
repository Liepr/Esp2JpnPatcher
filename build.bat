@echo off
cd /d "%~dp0"

set CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
if not exist "%CSC%" set CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe
if not exist "%CSC%" (
    echo [ERROR] No se encontro csc.exe de .NET Framework 4.x
    pause & exit /b 1
)

if not exist dist mkdir dist
if not exist Mono.Cecil.dll copy dist\Mono.Cecil.dll . >nul

"%CSC%" /nologo /target:library /out:dist\Esp2JpnHook.dll Hook.cs
if errorlevel 1 ( echo [ERROR] Fallo Hook.cs & pause & exit /b 1 )

"%CSC%" /nologo /target:winexe /win32icon:Esp2Jpn.ico /win32manifest:app.manifest /out:dist\Esp2JpnPatcher.exe ^
    /r:Mono.Cecil.dll /r:System.Windows.Forms.dll /r:System.Drawing.dll ^
    Main.cs Patcher.cs Hook.cs Help.cs
if errorlevel 1 ( echo [ERROR] Fallo Patcher & pause & exit /b 1 )

copy /y Mono.Cecil.dll dist\ >nul
copy /y LEEME.txt dist\ >nul

echo.
echo Listo. Prueba:  dist\Esp2JpnPatcher.exe --selftest
pause
