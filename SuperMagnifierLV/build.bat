@echo off
setlocal EnableDelayedExpansion
title Super Magnifier LV - Build
echo.
echo ============================================================
echo   Super Magnifier LV Build Script
echo ============================================================
echo.

if not exist "SuperMagnifier.csproj" (
    echo ERROR: Run from the project folder.
    exit /b 1
)

:: ── Version: bump version.txt (yyyy.MM.dd.NNNN, Tools\bump_version.cmd, S-03) ──────────
:: Directory.Build.props stamps the exe from the same file; installer.nsi gets /DAPP_VERSION.
call "%~dp0..\Tools\bump_version.cmd" "%~dp0version.txt"
if errorlevel 1 goto :err
set "NEW_VER=!APP_VERSION!"

:: 1. Restore + Publish
echo [1/3] Building...
dotnet restore SuperMagnifier.csproj -r win-x64
if errorlevel 1 goto :err
dotnet publish SuperMagnifier.csproj -c Release -r win-x64 /p:PublishSingleFile=true /p:SelfContained=true /p:IncludeNativeLibrariesForSelfExtract=true
if errorlevel 1 goto :err

set EXE=bin\Release\net10.0-windows\win-x64\publish\SuperMagnifierLV.exe
if not exist "%EXE%" ( echo ERROR: %EXE% not found & goto :err )

echo.
echo [2/3] Built version: !NEW_VER!
echo       %EXE%
echo.

:: 4. Stage exe to versioned Builds\ folder
set "BUILD_DIR=%~dp0Builds\!NEW_VER!"
if not exist "!BUILD_DIR!" mkdir "!BUILD_DIR!"
copy /y "%EXE%" "!BUILD_DIR!\SuperMagnifierLV.exe" >nul
:: Portable PDB (DebugType=portable) kept with the build for crash.log line numbers — not shipped.
if exist "bin\Release\net10.0-windows\win-x64\publish\SuperMagnifierLV.pdb" copy /y "bin\Release\net10.0-windows\win-x64\publish\SuperMagnifierLV.pdb" "!BUILD_DIR!\SuperMagnifierLV.pdb" >nul
echo Staged:  Builds\!NEW_VER!\SuperMagnifierLV.exe

:: 5. NSIS installer (required)
echo.
echo [3/3] Installer...
call "%~dp0..\Tools\find_makensis.cmd"
if errorlevel 1 goto :nsis_missing
"!MAKENSIS!" /DAPP_VERSION=!APP_VERSION! /DAPP_VERSION_NUMERIC=!APP_VERSION_NUMERIC! installer.nsi
if errorlevel 1 goto :nsis_failed
echo Installer built: SuperMagnifierLV_Setup.exe
move /y "SuperMagnifierLV_Setup.exe" "!BUILD_DIR!\SuperMagnifierLV_Setup.exe" >nul
echo Staged:  Builds\!NEW_VER!\SuperMagnifierLV_Setup.exe
echo.
echo Build complete: !NEW_VER!
goto :end

:nsis_missing
echo.
echo ERROR: makensis.exe (NSIS 3.x) not found - the installer could not be built.
echo        Install NSIS from https://nsis.sourceforge.io/ or set NSIS_HOME to its folder, then run build.bat again.
goto :err

:nsis_failed
echo.
echo ERROR: Installer build failed - see the makensis output above.
goto :err

:err
echo.
echo BUILD FAILED.
exit /b 1

:end
