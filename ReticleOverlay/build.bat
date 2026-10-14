@echo off
setlocal EnableDelayedExpansion
title Reticle Overlay -- Build

echo ============================================================
echo   Reticle Overlay Build Script  (.NET 10)
echo ============================================================
echo.

if not exist "ReticleOverlay.csproj" (
    echo ERROR: Run this script from the ReticleOverlay\ project folder.
    echo        e.g.  cd ReticleOverlay ^&^& build.bat
    exit /b 1
)

REM ── Version: bump version.txt (yyyy.MM.dd.NNNN, Tools\bump_version.cmd, S-03) ──────────
REM Directory.Build.props stamps the exe from the same file; installer.nsi gets /DAPP_VERSION.
call "%~dp0..\Tools\bump_version.cmd" "%~dp0version.txt"
if errorlevel 1 goto :build_error
set "NEW_VERSION=!APP_VERSION!"
echo.

REM -- Build ---------------------------------------------------------------------
echo [1/4] Restoring NuGet packages...
dotnet restore ReticleOverlay.csproj
if errorlevel 1 goto :build_error

echo.
echo [2/4] Building Release...
dotnet build ReticleOverlay.csproj -c Release --no-restore
if errorlevel 1 goto :build_error

set EXE=bin\Release\net10.0-windows\ReticleOverlay.exe
if not exist "%EXE%" (
    echo ERROR: Expected output not found: %EXE%
    goto :build_error
)

echo.
echo [3/4] Build succeeded.
echo       Output : %EXE%
echo       Version: !NEW_VERSION!
echo.

call "%~dp0..\Tools\find_makensis.cmd"
if errorlevel 1 goto :nsis_missing
echo Building NSIS installer...
"!MAKENSIS!" /DAPP_VERSION=!APP_VERSION! /DAPP_VERSION_NUMERIC=!APP_VERSION_NUMERIC! installer.nsi
if errorlevel 1 goto :nsis_failed
echo Installer: ReticleOverlay_Setup.exe

REM -- Copy outputs to Builds\{version}\ ----------------------------------------
echo.
echo [4/4] Packaging to Builds\!NEW_VERSION!\...
set BUILD_OUT=Builds\!NEW_VERSION!
if not exist "!BUILD_OUT!" mkdir "!BUILD_OUT!"
copy /Y "bin\Release\net10.0-windows\ReticleOverlay.exe" "!BUILD_OUT!\" >nul
if exist "bin\Release\net10.0-windows\ReticleOverlay.pdb" copy /Y "bin\Release\net10.0-windows\ReticleOverlay.pdb" "!BUILD_OUT!\" >nul
if exist "ReticleOverlay_Setup.exe" (
    move /Y "ReticleOverlay_Setup.exe" "!BUILD_OUT!\" >nul
    echo Packaged : !BUILD_OUT!\ReticleOverlay.exe
    echo Packaged : !BUILD_OUT!\ReticleOverlay_Setup.exe
) else (
    echo Packaged : !BUILD_OUT!\ReticleOverlay.exe
)

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
