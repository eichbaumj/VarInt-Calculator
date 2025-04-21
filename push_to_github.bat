@echo off
echo VarInt Calculator - GitHub Push Helper
echo ===================================
echo.

REM Check if git is installed
where git >nul 2>nul
if %ERRORLEVEL% NEQ 0 (
    echo ERROR: Git is not installed or not in PATH. Please install Git first.
    pause
    exit /b 1
)

REM Check if this is a git repository
if not exist .git (
    echo Initializing Git repository...
    git init
    if %ERRORLEVEL% NEQ 0 (
        echo Failed to initialize Git repository.
        pause
        exit /b 1
    )
)

REM Configure remote repository if not already set
git remote -v | findstr origin >nul
if %ERRORLEVEL% NEQ 0 (
    echo Setting up remote repository...
    git remote add origin https://github.com/eichbaumj/VarInt-Calculator.git
    if %ERRORLEVEL% NEQ 0 (
        echo Failed to add remote repository.
        pause
        exit /b 1
    )
)

REM Take a screenshot for the README
echo.
echo Next: We need a screenshot for the README.
echo Please run the VarInt Calculator and take a screenshot of it.
echo Save it as "screenshot.png" in the project root directory.
echo.
echo Press any key when you've done this...
pause >nul

REM Add all files
echo.
echo Adding all files to Git...
git add .
if %ERRORLEVEL% NEQ 0 (
    echo Failed to add files.
    pause
    exit /b 1
)

REM Commit changes
echo.
echo Creating initial commit...
git commit -m "Initial commit: VarInt Calculator v3.0"
if %ERRORLEVEL% NEQ 0 (
    echo Failed to commit changes.
    pause
    exit /b 1
)

REM Push to GitHub
echo.
echo Pushing to GitHub repository...
git push -u origin master
if %ERRORLEVEL% NEQ 0 (
    echo Failed to push to GitHub. 
    echo You might need to authenticate or use a different branch.
    echo Try running: git push -u origin main
    pause
    exit /b 1
)

echo.
echo Success! Your VarInt Calculator is now on GitHub at:
echo https://github.com/eichbaumj/VarInt-Calculator
echo.
pause 