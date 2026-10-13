@echo off
setlocal EnableDelayedExpansion
title File Copy Studio — Build

echo ============================================================
echo   FileCopy Build Script  (.NET 10)
echo ============================================================
echo.

if not exist "FileCopy.csproj" (
    echo ERROR: Run this script from the FileCopy\ project folder.
    echo        e.g.  cd FileCopy ^&^& build.bat
    exit /b 1
)

:: ── Get date via PowerShell (locale-independent) ─────────────────────────────
for /f %%D in ('powershell -NoProfile -Command "Get-Date -Format yyyy.MM.dd"') do set TODAY=%%D

if "!TODAY!"=="" (
    echo ERROR: Could not determine today's date.
    exit /b 1
)

:: ── Read version.txt and compute next build number ───────────────────────────
set VERSION_FILE=version.txt
set LAST_DATE=0000.00.00
set LAST_BUILD=0

if exist "!VERSION_FILE!" (
    set /p LAST_VER=<"!VERSION_FILE!"
    for /f "tokens=1-4 delims=." %%a in ("!LAST_VER!") do (
        set LAST_DATE=%%a.%%b.%%c
        set /a LAST_BUILD=1%%d - 10000
    )
)

if "!LAST_DATE!"=="!TODAY!" (
    set /a BUILD_NUM=!LAST_BUILD! + 1
) else (
    set BUILD_NUM=1
)

set "BUILD_PAD=000!BUILD_NUM!"
set "BUILD_PAD=!BUILD_PAD:~-4!"

set NEW_VERSION=!TODAY!.!BUILD_PAD!

for /f "tokens=1-3 delims=." %%a in ("!TODAY!") do (
    set /a V_YEAR=%%a
    set /a V_MON=1%%b - 100
    set /a V_DAY=1%%c - 100
)
set ASSY_VERSION=!V_YEAR!.!V_MON!.!V_DAY!.!BUILD_NUM!

echo Version : !NEW_VERSION!
echo Assembly: !ASSY_VERSION!
echo.

echo Patching version numbers...
powershell -NoProfile -ExecutionPolicy Bypass -File patch_version.ps1 -NewVersion "!NEW_VERSION!" -AssyVersion "!ASSY_VERSION!"
if errorlevel 1 (
    echo WARNING: Version patching failed.
)

echo !NEW_VERSION!> "!VERSION_FILE!"
echo Saved !NEW_VERSION! to !VERSION_FILE!
echo.

echo [1/3] Restoring NuGet packages...
dotnet restore FileCopy.csproj
if errorlevel 1 goto :build_error

echo.
echo [2/3] Publishing self-contained Release (x64)...
if exist "dist" rmdir /s /q dist
dotnet publish FileCopy.csproj -c Release ^
    -r win-x64 ^
    --self-contained true ^
    -p:PublishSingleFile=true ^
    -p:IncludeNativeLibrariesForSelfExtract=true ^
    -p:EnableCompressionInSingleFile=true ^
    -p:PublishReadyToRun=true ^
    -o dist ^
    --nologo
if errorlevel 1 goto :build_error

set EXE=dist\FileCopyStudio.exe
if not exist "%EXE%" (
    echo ERROR: Expected output not found: %EXE%
    goto :build_error
)

echo.
echo [3/3] Build succeeded.
echo       Output : %EXE%
echo       Version: !NEW_VERSION!
echo.

if exist docs\README.md  copy /y docs\README.md  dist\README.md  >nul
if exist docs\manual.pdf copy /y docs\manual.pdf dist\manual.pdf >nul

where makensis >nul 2>&1
if %errorlevel%==0 (
    echo Building NSIS installer...
    makensis installer.nsi
    if errorlevel 1 (
        echo.
        echo WARNING: Installer build failed - app build was still successful.
    ) else (
        echo Installer: FileCopyStudio_Setup.exe
    )
) else (
    echo NOTE: makensis not found - skipping installer.
    echo       Install NSIS 3.x from https://nsis.sourceforge.io/
    echo       then run:  makensis installer.nsi
)

:: ── Archive to builds\<version>\ ─────────────────────────────────────────────
set BUILD_OUT=builds\!NEW_VERSION!
echo.
echo Archiving to !BUILD_OUT! ...
if not exist "!BUILD_OUT!" mkdir "!BUILD_OUT!"

copy /y dist\FileCopyStudio.exe    "!BUILD_OUT!\FileCopyStudio.exe"    >nul
if exist dist\README.md  copy /y dist\README.md  "!BUILD_OUT!\README.md"  >nul
if exist dist\manual.pdf copy /y dist\manual.pdf "!BUILD_OUT!\manual.pdf" >nul
if exist FileCopyStudio_Setup.exe (
    move /y FileCopyStudio_Setup.exe "!BUILD_OUT!\FileCopyStudio_Setup.exe" >nul
    echo       Installer  : !BUILD_OUT!\FileCopyStudio_Setup.exe
)

echo       Exe        : !BUILD_OUT!\FileCopyStudio.exe
echo       Version    : !NEW_VERSION!
echo.
echo Done!  Version !NEW_VERSION!
goto :end

:build_error
echo.
echo BUILD FAILED.
exit /b 1

:end
