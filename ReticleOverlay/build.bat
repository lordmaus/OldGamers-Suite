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

REM -- Get date via PowerShell (locale-independent, wmic-free) ------------------
for /f %%D in ('powershell -NoProfile -Command "Get-Date -Format yyyy.MM.dd"') do set TODAY=%%D

if "!TODAY!"=="" (
    echo ERROR: Could not determine today's date.
    exit /b 1
)

REM -- Read version.txt and compute next build number ---------------------------
set VERSION_FILE=version.txt
set LAST_DATE=0000.00.00
set LAST_BUILD=0

if exist "!VERSION_FILE!" (
    set /p LAST_VER=<"!VERSION_FILE!"
    REM Split yyyy.mm.dd.xxx into date-part and build number
    for /f "tokens=1-4 delims=." %%a in ("!LAST_VER!") do (
        set LAST_DATE=%%a.%%b.%%c
        REM L-4: parse the build segment via PowerShell instead of cmd's
        REM "set /a". A leading "0" makes "set /a" treat the value as octal,
        REM which is why this used to do "set /a LAST_BUILD=1%%d - 1000" --
        REM prefixing "1" then subtracting 1000 to dodge octal parsing, but
        REM that trick hardcodes a 3-digit build segment. version.txt already
        REM holds a 4-digit build (0002) -- with 4 digits "1" + "0002" = 10002,
        REM minus the hardcoded 1000 gives 9002 instead of 2, so the very next
        REM same-day rebuild jumped straight to build 9003. PowerShell's [int]
        REM cast reads a leading-zero decimal string correctly no matter how
        REM many digits it has, so this works regardless of past or future
        REM build-number width.
        for /f %%L in ('powershell -NoProfile -Command "[int]'%%d'"') do set LAST_BUILD=%%L
    )
)

if "!LAST_DATE!"=="!TODAY!" (
    set /a BUILD_NUM=!LAST_BUILD! + 1
) else (
    set BUILD_NUM=1
)

REM RO-04 (2026-10-13): 4-digit counter like the rest of the suite (yyyy.mm.dd.xxxx).
REM Zero-pad to (at least) 4 digits -- via PowerShell's D4 format instead of
REM the old lss/geq ladder, which only ever padded UP to 3 digits and had no
REM path back down if BUILD_NUM legitimately exceeds 999; "D3" is correct at
REM any width and never truncates a real count.
for /f %%P in ('powershell -NoProfile -Command "'{0:D4}' -f !BUILD_NUM!"') do set BUILD_PAD=%%P

set NEW_VERSION=!TODAY!.!BUILD_PAD!

REM AssemblyVersion needs plain integers (no leading zeros on each part)
REM PowerShell splits the date cleanly
for /f "tokens=1-3 delims=." %%a in ("!TODAY!") do (
    set /a V_YEAR=%%a
    set /a V_MON=1%%b - 100
    set /a V_DAY=1%%c - 100
)
set ASSY_VERSION=!V_YEAR!.!V_MON!.!V_DAY!.!BUILD_NUM!

echo Version : !NEW_VERSION!
echo Assembly: !ASSY_VERSION!
echo.

REM -- Patch csproj and nsi via external PS1 (no cmd escaping issues) ----------
echo Patching version numbers...
powershell -NoProfile -ExecutionPolicy Bypass -File patch_version.ps1 -NewVersion "!NEW_VERSION!" -AssyVersion "!ASSY_VERSION!"
if errorlevel 1 (
    echo WARNING: Version patching failed.
)

REM -- Save version --------------------------------------------------------------
echo !NEW_VERSION!> "!VERSION_FILE!"
echo Saved !NEW_VERSION! to !VERSION_FILE!
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
"!MAKENSIS!" installer.nsi
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
