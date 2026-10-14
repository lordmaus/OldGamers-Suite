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

:: ── Version: bump version.txt (yyyy.MM.dd.NNNN, Tools\bump_version.cmd, S-03) ──────────
:: Directory.Build.props stamps the exe from the same file; installer.nsi gets /DAPP_VERSION.
call "%~dp0..\Tools\bump_version.cmd" "%~dp0version.txt"
if errorlevel 1 goto :build_error
set "NEW_VERSION=!APP_VERSION!"
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

call "%~dp0..\Tools\find_makensis.cmd"
if errorlevel 1 goto :nsis_missing
echo Building NSIS installer...
"!MAKENSIS!" /DAPP_VERSION=!APP_VERSION! /DAPP_VERSION_NUMERIC=!APP_VERSION_NUMERIC! installer.nsi
if errorlevel 1 goto :nsis_failed
echo Installer: FileCopyStudio_Setup.exe

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

:nsis_missing
echo.
echo ERROR: makensis.exe (NSIS 3.x) not found - the installer could not be built.
echo        Install NSIS from https://nsis.sourceforge.io/ or set NSIS_HOME to its folder, then run build.bat again.
goto :build_error

:nsis_failed
echo.
echo ERROR: Installer build failed - see the makensis output above.
goto :build_error

:build_error
echo.
echo BUILD FAILED.
exit /b 1

:end
