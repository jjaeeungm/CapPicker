@echo off
setlocal EnableExtensions
cd /d "%~dp0"

title CapPicker 1.1.1 - Local Build

echo.
echo ========================================
echo   CapPicker 1.1.1 - Local Build
echo ========================================
echo.

set "CSC64=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
set "CSC32=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
set "CSC="
set "PLATFORM="

if exist "%CSC64%" (
    set "CSC=%CSC64%"
    set "PLATFORM=x64"
) else if exist "%CSC32%" (
    set "CSC=%CSC32%"
    set "PLATFORM=x86"
)

if not defined CSC (
    echo [ERROR] Windows .NET Framework C# compiler was not found.
    echo.
    echo Windows 10/11 normally includes .NET Framework 4.x.
    echo Please enable/install ".NET Framework 4.8 Advanced Services"
    echo and run BUILD.cmd again.
    echo.
    pause
    exit /b 1
)

echo Compiler:
echo   %CSC%
echo Platform:
echo   %PLATFORM%
echo.

if exist "CapPicker.exe" del /q "CapPicker.exe" >nul 2>nul

echo Building...

"%CSC%" /nologo /target:winexe /optimize+ /debug- /platform:%PLATFORM% ^
 /out:"CapPicker.exe" ^
 /win32icon:"CapPicker.ico" ^
 /win32manifest:"app.manifest" ^
 /reference:System.dll ^
 /reference:System.Drawing.dll ^
 /reference:System.Windows.Forms.dll ^
 "AssemblyInfo.cs" "L10n.cs" "AppSettings.cs" "SettingsForm.cs" "HelpForm.cs" "Program.cs" "Native.cs" "CaptureService.cs" "Ui.cs" "SelectionOverlay.cs" "WindowPicker.cs" "ColorPicker.cs" "Editor.cs" "MainForm.cs"

if errorlevel 1 (
    echo.
    echo ========================================
    echo [FAILED] Build failed.
    echo ========================================
    echo.
    echo Please capture the error text above and send it to ChatGPT.
    echo.
    pause
    exit /b 1
)

echo.
echo ========================================
echo [OK] Build complete
echo ========================================
echo.
echo Output:
echo   %CD%\CapPicker.exe
echo.
echo The EXE was generated locally on this PC.
echo.
start "" "%CD%\CapPicker.exe"
exit /b 0
