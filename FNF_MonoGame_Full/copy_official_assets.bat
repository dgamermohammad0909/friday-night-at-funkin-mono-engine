@echo off
echo ========================================
echo  Copying Official FNF Assets
echo ========================================
echo.

set FNF_OFFICIAL="D:\godt engine custom\FNF_Official\assets"
set FNF_MONO="D:\godt engine custom\FNF_MonoGame_Full\Content"

echo Source: %FNF_OFFICIAL%
echo Destination: %FNF_MONO%
echo.

:: Create Content folder structure
if not exist %FNF_MONO%\images mkdir %FNF_MONO%\images
if not exist %FNF_MONO%\images\characters mkdir %FNF_MONO%\images\characters
if not exist %FNF_MONO%\images\ui mkdir %FNF_MONO%\images\ui
if not exist %FNF_MONO%\data mkdir %FNF_MONO%\data
if not exist %FNF_MONO%\music mkdir %FNF_MONO%\music
if not exist %FNF_MONO%\songs mkdir %FNF_MONO%\songs

echo Copying shared images (notes, strumline, stage)...
xcopy /Y %FNF_OFFICIAL%\shared\images\notes.png %FNF_MONO%\images\
xcopy /Y %FNF_OFFICIAL%\shared\images\notes.xml %FNF_MONO%\images\
xcopy /Y %FNF_OFFICIAL%\shared\images\noteStrumline.png %FNF_MONO%\images\
xcopy /Y %FNF_OFFICIAL%\shared\images\noteStrumline.xml %FNF_MONO%\images\
xcopy /Y %FNF_OFFICIAL%\shared\images\stageback.png %FNF_MONO%\images\
xcopy /Y %FNF_OFFICIAL%\shared\images\stagefront.png %FNF_MONO%\images\
xcopy /Y %FNF_OFFICIAL%\shared\images\stagecurtains.png %FNF_MONO%\images\
xcopy /Y %FNF_OFFICIAL%\shared\images\healthBar.png %FNF_MONO%\images\

echo Copying characters...
xcopy /E /Y /I %FNF_OFFICIAL%\shared\images\characters %FNF_MONO%\images\characters

echo Copying UI images...
xcopy /E /Y /I %FNF_OFFICIAL%\shared\images\ui %FNF_MONO%\images\ui

echo Copying preload data...
xcopy /E /Y /I %FNF_OFFICIAL%\preload\data %FNF_MONO%\data

echo Copying tutorial song...
if not exist %FNF_MONO%\songs\tutorial mkdir %FNF_MONO%\songs\tutorial
xcopy /E /Y /I %FNF_OFFICIAL%\tutorial\songs\tutorial %FNF_MONO%\songs\tutorial

echo Copying week1 songs (bopeebo, fresh, dadbattle)...
xcopy /E /Y /I %FNF_OFFICIAL%\week1\songs %FNF_MONO%\songs

echo Copying preload music...
xcopy /E /Y /I %FNF_OFFICIAL%\preload\music %FNF_MONO%\music

echo.
echo ========================================
echo  Asset Copy Complete!
echo ========================================
pause
