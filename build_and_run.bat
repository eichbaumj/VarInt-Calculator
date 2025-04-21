@echo off
echo Building VarInt Calculator...
dotnet build -c Release
if %ERRORLEVEL% NEQ 0 (
    echo Build failed!
    pause
    exit /b %ERRORLEVEL%
)
echo Build successful!
echo Running VarInt Calculator...
start bin\Release\net9.0-windows\VarIntCalculator.exe 