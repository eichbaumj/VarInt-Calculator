@echo off
echo Building VarInt Calculator Installer...

REM First ensure the application is built in Release mode
echo Building application in Release mode...
dotnet publish -c Release

REM Path to Inno Setup Compiler - adjust if installed in a different location
set INNO_COMPILER="C:\Program Files (x86)\Inno Setup 6\ISCC.exe"

REM Compile the installer
echo Compiling installer...
%INNO_COMPILER% VarIntCalculator_Setup.iss

echo.
if %ERRORLEVEL% NEQ 0 (
    echo ERROR: Installer creation failed!
) else (
    echo Installer created successfully!
    echo Installer location: %CD%\VarIntCalculator_Setup.exe
)

pause 