@echo off
echo ========================================
echo  FNF MonoGame Xbox - NativeAOT Setup
echo ========================================
echo.

set NATIVEAOT_PATH=D:\godt engine custom\NativeAOT-Xbox
set FNF_PATH=D:\godt engine custom\FNF_MonoGame_Xbox

echo Step 1: Copying project to NativeAOT-Xbox folder...
if not exist "%NATIVEAOT_PATH%\FNF_MonoGame_Xbox" mkdir "%NATIVEAOT_PATH%\FNF_MonoGame_Xbox"
xcopy /E /Y "%FNF_PATH%\*" "%NATIVEAOT_PATH%\FNF_MonoGame_Xbox\"

echo.
echo Step 2: Creating modified csproj for Xbox...
echo Done!

echo.
echo ========================================
echo  Next Steps:
echo ========================================
echo.
echo 1. Open: %NATIVEAOT_PATH%\NativeAOT-GDKX.sln
echo 2. Add Existing Project: FNF_MonoGame_Xbox\FNF_MonoGame_Xbox.csproj
echo 3. Right-click Bootstrap ^> Project Dependencies ^> Add FNF_MonoGame_Xbox
echo 4. Edit Bootstrap\Main.cpp to call FNF entry point
echo 5. Build for Gaming.Xbox.Scarlett.x64
echo 6. Deploy to Xbox!
echo.
echo NOTE: This requires Microsoft GDK to be installed!
echo.
pause
