@echo off
setlocal EnableDelayedExpansion
title Badge Overlay — Build

echo ============================================================
echo   Badge Overlay Build Script
echo ============================================================
echo.

:: ── Must be run from the BadgeOverlay\ project folder ───────────────────────
if not exist "BadgeOverlay.csproj" (
    echo ERROR: Run this script from the BadgeOverlay\ project folder.
    echo        e.g.  cd BadgeOverlay ^&^& build.bat
    exit /b 1
)

:: ── Preflight: dotnet must be on PATH ────────────────────────────────────────
where dotnet >nul 2>&1
if errorlevel 1 (
    echo ERROR: 'dotnet' is not on PATH.
    echo.
    echo        Install the .NET 10 SDK from:
    echo          https://dotnet.microsoft.com/download/dotnet/10.0
    echo        Then re-run build.bat.
    exit /b 1
)

:: ── 1. Restore ────────────────────────────────────────────────────────────────
echo [1/3] Restoring NuGet packages...
dotnet restore BadgeOverlay.csproj
if errorlevel 1 goto :build_error

:: ── 2. Publish self-contained single-file exe ─────────────────────────────────
echo.
echo [2/3] Publishing self-contained release...
dotnet restore BadgeOverlay.csproj -r win-x64
if errorlevel 1 goto :build_error
dotnet publish BadgeOverlay.csproj -c Release -r win-x64 /p:PublishSingleFile=true /p:SelfContained=true /p:IncludeNativeLibrariesForSelfExtract=true
if errorlevel 1 goto :build_error

set EXE=bin\Release\net10.0-windows\win-x64\publish\BadgeOverlay.exe
if not exist "%EXE%" (
    echo ERROR: Expected output not found: %EXE%
    goto :build_error
)

echo.
echo [3/3] Publish succeeded.
echo       Output: %EXE%
echo.

:: ── Stage exe to versioned Builds\ folder ────────────────────────────────────
:: Reads the version BumpBuildCounter just wrote so the folder name is always
:: in sync with the embedded assembly version.
set "VER_DATE=" & set "VER_NUM="
for /f "usebackq tokens=1-4 delims=. " %%a in ("%~dp0version.txt") do if not defined VER_DATE (
    set "VER_DATE=%%a.%%b.%%c"
    set "VER_NUM=%%d"
)
set "BUILD_DIR=%~dp0Builds\%VER_DATE%.%VER_NUM%"
if not exist "%BUILD_DIR%" mkdir "%BUILD_DIR%"
copy /y "%EXE%" "%BUILD_DIR%\BadgeOverlay.exe" >nul
echo Staged:  Builds\%VER_DATE%.%VER_NUM%\BadgeOverlay.exe
:: Portable PDB (DebugType=portable) — kept with the build for crash-stack line numbers, not shipped in the installer.
if exist "bin\Release\net10.0-windows\win-x64\publish\BadgeOverlay.pdb" copy /y "bin\Release\net10.0-windows\win-x64\publish\BadgeOverlay.pdb" "%BUILD_DIR%\BadgeOverlay.pdb" >nul
echo.

:: ── 3. NSIS installer (optional) ──────────────────────────────────────────────
:: No code-signing certificate exists for this app (or any app in this
:: suite) and none is planned — builds are unsigned by design. SmartScreen
:: will warn on first run; that's expected and accepted.
:: No goto inside if-blocks — dangling ) leaves errorlevel=1 after jump.
where makensis >nul 2>&1
if not errorlevel 1 goto :build_installer
echo NOTE: makensis not found — skipping installer.
echo       Install NSIS 3.x from https://nsis.sourceforge.io/
echo       then run:  makensis installer.nsi
goto :skip_installer

:build_installer

:: Read version.txt — same source the csproj BumpBuildCounter target writes.
:: Format: "yyyy.MM.dd.NNNN" (one line). Produces APP_VERSION (yyyy.MM.dd.NNNN)
:: and APP_VERSION_NUMERIC (Y.M.D.N) for VIProductVersion.
set "VER_FILE=%~dp0version.txt"
if exist "%VER_FILE%" goto :ver_found
echo ERROR: version.txt missing at %VER_FILE% — skipping installer.
goto :skip_installer
:ver_found
set "VER_DATE=" & set "VER_NUM="
for /f "usebackq tokens=1-4 delims=. " %%a in ("%VER_FILE%") do if not defined VER_DATE (
    set "VER_DATE=%%a.%%b.%%c"
    set "VER_NUM=%%d"
)
:: Strip leading zeros: "1xx - 100" trick avoids octal parsing of 08/09.
set "VER_Y=" & set "VER_M=" & set "VER_D="
for /f "tokens=1,2,3 delims=." %%a in ("%VER_DATE%") do (
    set /a VER_Y=%%a
    set /a VER_M=1%%b - 100
    set /a VER_D=1%%c - 100
)
set /a VER_N=1%VER_NUM% - 1000
set "APP_VERSION=%VER_DATE%.%VER_NUM%"
set "APP_VERSION_NUMERIC=%VER_Y%.%VER_M%.%VER_D%.%VER_N%"
echo Building installer with version %APP_VERSION% (numeric %APP_VERSION_NUMERIC%)...
makensis /DAPP_VERSION=%APP_VERSION% /DAPP_VERSION_NUMERIC=%APP_VERSION_NUMERIC% installer.nsi
if not errorlevel 1 goto :installer_ok
echo.
echo WARNING: Installer build failed — app build was still successful.
goto :skip_installer
:installer_ok
echo Installer: BadgeOverlay_Setup.exe
move /y "BadgeOverlay_Setup.exe" "%BUILD_DIR%\BadgeOverlay_Setup.exe" >nul 2>&1
echo Staged:  Builds\%VER_DATE%.%VER_NUM%\BadgeOverlay_Setup.exe
:skip_installer

echo.
echo Done!
exit /b 0

:build_error
echo.
echo BUILD FAILED.
exit /b 1
