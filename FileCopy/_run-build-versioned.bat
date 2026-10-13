@echo off
setlocal EnableDelayedExpansion
cd /d "%~dp0"
echo Running versioned build...
call build.bat > _build-output.log 2>&1
if errorlevel 1 (
    echo BUILD FAILED > _build-status.txt
    echo Build failed. See _build-output.log.
    exit /b 1
)
echo BUILD OK > _build-status.txt

:: Read version that was just stamped by build.bat
set /p NEW_VER=<version.txt
set NEW_VER=!NEW_VER: =!

:: Copy dist\ into builds\<version>\
set DEST=builds\!NEW_VER!
if not exist "!DEST!" mkdir "!DEST!"
xcopy /s /y /q dist\* "!DEST!\" >nul
echo Copied dist\ to !DEST!\
echo Done. Version: !NEW_VER!
