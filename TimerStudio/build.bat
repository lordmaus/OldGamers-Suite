@echo off
setlocal EnableDelayedExpansion
title Timer Studio — Build

echo ============================================================
echo   Timer Studio Build Script
echo ============================================================
echo.

:: ── Must be run from the TimerStudio\ project folder ─────────────────────────
if not exist "TimerStudio.csproj" (
    echo ERROR: Run this script from the TimerStudio\ project folder.
    echo        e.g.  cd TimerStudio ^&^& build.bat
    exit /b 1
)

:: ── Auto-increment version ────────────────────────────────────────────────────
echo [0/5] Computing version...
powershell -NoProfile -ExecutionPolicy Bypass -File "version_bump.ps1"
if errorlevel 1 (
    echo ERROR: Version increment failed.
    exit /b 1
)

:: Re-read updated version for use in staging
for /f "tokens=3 delims=><" %%V in ('findstr /i "<Version>" TimerStudio.csproj') do set APP_VERSION=%%V
if "!APP_VERSION!"=="" (
    echo ERROR: Could not read updated ^<Version^> from TimerStudio.csproj
    exit /b 1
)
echo.

:: ── 1. Restore ────────────────────────────────────────────────────────────────
echo [1/5] Restoring NuGet packages...
dotnet restore TimerStudio.csproj
if errorlevel 1 goto :build_error

:: TS-09: fail the build on known-vulnerable NuGet packages (needs network; skipped silently offline)
echo       Checking for vulnerable packages...
dotnet list TimerStudio.csproj package --vulnerable --include-transitive > "%TEMP%\ts_vuln.txt" 2>&1
findstr /C:"has the following vulnerable packages" "%TEMP%\ts_vuln.txt" >nul
if !errorlevel!==0 (
    type "%TEMP%\ts_vuln.txt"
    echo ERROR: Vulnerable NuGet packages found -- update the pinned versions in TimerStudio.csproj.
    goto :build_error
)

:: ── 2. Build Release ─────────────────────────────────────────────────────────
echo.
echo [2/5] Building Release...
dotnet build TimerStudio.csproj -c Release --no-restore
if errorlevel 1 goto :build_error

set OUTDIR=bin\Release\net10.0-windows\win-x64
set EXE=%OUTDIR%\TimerStudio.exe
if not exist "%EXE%" (
    echo ERROR: Expected output not found: %EXE%
    goto :build_error
)

:: ── 3. Copy README to build output (so About dialog can open it) ──────────────
echo.
echo [3/5] Copying README to build output...
if exist "README.md" (
    copy /Y "README.md" "%OUTDIR%\README.md" >nul
    echo       Copied README.md to %OUTDIR%\
) else (
    echo       NOTE: README.md not found — skipping copy.
)

:: ── 4. NSIS installer (optional) ─────────────────────────────────────────────
:: No code-signing certificate exists for this app (or any app in this
:: suite) and none is planned — builds are unsigned by design.
echo.
echo [4/5] Building installer...
where makensis >nul 2>&1
if %errorlevel%==0 (
    makensis installer.nsi
    if errorlevel 1 (
        echo WARNING: Installer build failed -- app build was still successful.
    ) else (
        echo       Installer: TimerStudio_Setup.exe
    )
) else (
    echo NOTE: makensis not found — skipping installer.
    echo       Install NSIS 3.x from https://nsis.sourceforge.io/
)

:: ── 5. Stage to Builds\<version>\ ────────────────────────────────────────────
echo.
echo [5/5] Staging to Builds\!APP_VERSION!\...
set STAGE=Builds\!APP_VERSION!
if not exist "%STAGE%" mkdir "%STAGE%"

copy /Y "%EXE%" "%STAGE%\TimerStudio.exe" >nul
if exist "%OUTDIR%\TimerStudio.pdb" copy /Y "%OUTDIR%\TimerStudio.pdb" "%STAGE%\TimerStudio.pdb" >nul
echo       Copied TimerStudio.exe

if exist "TimerStudio_Setup.exe" (
    copy /Y "TimerStudio_Setup.exe" "%STAGE%\TimerStudio_Setup.exe" >nul
    echo       Copied TimerStudio_Setup.exe
)

:: Write a build log entry
echo Build: !APP_VERSION! > "%STAGE%\build_log.txt"
echo Date:  %DATE% %TIME% >> "%STAGE%\build_log.txt"

echo.
echo ============================================================
echo   Build succeeded: !APP_VERSION!
echo   Staged: %STAGE%\
echo ============================================================
echo.
goto :end

:build_error
echo.
echo BUILD FAILED.
exit /b 1

:end
