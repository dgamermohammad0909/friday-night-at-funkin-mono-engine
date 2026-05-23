@echo off
echo ========================================
echo  FNF MonoGame - Build and Run
echo ========================================
echo.

cd /d "%~dp0"

echo Step 1: Restore packages...
dotnet restore

echo.
echo Step 2: Build...
dotnet build -c Debug

if %ERRORLEVEL% NEQ 0 (
    echo.
    echo BUILD FAILED!
    pause
    exit /b 1
)

echo.
echo Step 3: Run...
dotnet run -c Debug

pause
