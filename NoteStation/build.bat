@echo off
setlocal EnableDelayedExpansion
title NoteStation — Build

echo ============================================================
echo   NoteStation Build Script
echo ============================================================
echo.

:: ── Must be run from the NoteStation\ project folder ─────────────────────────
if not exist "NoteStation.csproj" (
    echo ERROR: Run this script from the NoteStation\ project folder.
    echo        e.g.  cd NoteStation ^&^& build.bat
    exit /b 1
)

echo [0/5] Computing version...
:: ── Version: bump version.txt (yyyy.MM.dd.NNNN, Tools\bump_version.cmd, S-03) ──────────
:: Directory.Build.props stamps the exe from the same file; installer.nsi gets /DAPP_VERSION.
call "%~dp0..\Tools\bump_version.cmd" "%~dp0version.txt"
if errorlevel 1 exit /b 1
echo.

:: ── 1. Restore ────────────────────────────────────────────────────────────────
echo [1/5] Restoring NuGet packages...
dotnet restore NoteStation.csproj
if errorlevel 1 goto :build_error

:: ── 2. Build Release ─────────────────────────────────────────────────────────
echo.
echo [2/5] Building Release...
dotnet build NoteStation.csproj -c Release --no-restore
if errorlevel 1 goto :build_error

set OUTDIR=bin\Release\net10.0-windows10.0.22621.0\win-x64
set EXE=%OUTDIR%\NoteStation.exe
if not exist "%EXE%" (
    echo ERROR: Expected output not found: %EXE%
    goto :build_error
)

:: ── 3. Copy README to build output ───────────────────────────────────────────
echo.
echo [3/5] Copying README to build output...
if exist "README.md" (
    copy /Y "README.md" "%OUTDIR%\README.md" >nul
    echo       Copied README.md to %OUTDIR%\
) else (
    echo       NOTE: README.md not found — skipping copy.
)

:: ── 4. NSIS installer (required) ─────────────────────────────────────────────
:: No code-signing certificate exists for this app (or any app in this
:: suite) and none is planned — builds are unsigned by design.
echo.
echo [4/5] Building installer...
:: Create staging dir first so NSIS can write the output file
if not exist "Builds\!APP_VERSION!" mkdir "Builds\!APP_VERSION!"
call "%~dp0..\Tools\find_makensis.cmd"
if errorlevel 1 goto :nsis_missing
"!MAKENSIS!" /DAPP_VERSION=!APP_VERSION! /DAPP_VERSION_NUMERIC=!APP_VERSION_NUMERIC! installer.nsi
if errorlevel 1 goto :nsis_failed
echo       Installer: Builds\!APP_VERSION!\NoteStation_Setup.exe

:: ── 5. Stage to Builds\<version>\ ────────────────────────────────────────────
echo.
echo [5/5] Staging to Builds\!APP_VERSION!\...
set STAGE=Builds\!APP_VERSION!
if not exist "%STAGE%" mkdir "%STAGE%"

copy /Y "%EXE%" "%STAGE%\NoteStation.exe" >nul
if exist "%OUTDIR%\NoteStation.pdb" copy /Y "%OUTDIR%\NoteStation.pdb" "%STAGE%\NoteStation.pdb" >nul
echo       Copied NoteStation.exe

if exist "Builds\!APP_VERSION!\NoteStation_Setup.exe" (
    echo       Installer already in stage folder.
)

echo Build: !APP_VERSION!  > "%STAGE%\build_log.txt"
echo Date:  %DATE% %TIME% >> "%STAGE%\build_log.txt"

echo.
echo ============================================================
echo   Build succeeded: !APP_VERSION!
echo   Staged: %STAGE%\
echo ============================================================
echo.
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
