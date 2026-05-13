@echo off
echo ========================================
echo  FNF MonoGame - Asset Copy Script
echo ========================================
echo.

set FNF_GODOT="D:\godt engine custom\redot-engine-fresh\bin\friday-night-funkin'-(4.5)-2\assets"
set FNF_MONO="D:\godt engine custom\FNF_MonoGame_Full\Content"

echo Source: %FNF_GODOT%
echo Destination: %FNF_MONO%
echo.

:: Create Content folder
if not exist %FNF_MONO% mkdir %FNF_MONO%

echo Copying assets...

:: Copy menus
echo - Copying menus...
xcopy /E /Y /I %FNF_GODOT%\menus %FNF_MONO%\menus

:: Copy game assets (characters, stages, HUD)
echo - Copying game assets...
xcopy /E /Y /I %FNF_GODOT%\game %FNF_MONO%\game

:: Copy fonts
echo - Copying fonts...
xcopy /E /Y /I %FNF_GODOT%\fonts %FNF_MONO%\fonts

:: Copy songs
echo - Copying songs...
xcopy /E /Y /I %FNF_GODOT%\songs %FNF_MONO%\songs

:: Copy resources
echo - Copying resources...
xcopy /E /Y /I %FNF_GODOT%\resources %FNF_MONO%\resources

echo.
echo ========================================
echo  Asset Copy Complete!
echo ========================================
echo.
echo Files copied to: %FNF_MONO%
echo.
echo Note: .import files from Godot are not needed.
echo       PNG files work directly with MonoGame.
echo       OGG audio files need conversion to WAV or use NVorbis.
echo.
pause
