@echo off
echo ========================================
echo  FNF MonoGame Xbox Test - Build Script
echo ========================================
echo.

cd /d "%~dp0"

echo Step 1: Restore packages...
dotnet restore FNF_MonoGame_Xbox.csproj

echo.
echo Step 2: Build for Windows (test first)...
dotnet build FNF_MonoGame_Xbox.csproj -c Release

echo.
echo Step 3: Run locally to test...
echo Press any key to run the game, or Ctrl+C to skip
pause
dotnet run --project FNF_MonoGame_Xbox.csproj -c Release

echo.
echo ========================================
echo  Build Complete!
echo ========================================
echo.
echo Next steps for Xbox:
echo 1. Copy this project to NativeAOT-Xbox folder
echo 2. Follow NativeAOT-Xbox build instructions
echo 3. Deploy to Xbox via Device Portal
echo.
pause
