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

:: 1. Restore + Publish
:: Version is no longer patched here — the BumpBuildCounter MSBuild target
:: in SuperMagnifier.csproj runs automatically on this (and every other,
:: including IDE) build and bumps version.txt itself.
echo [1/3] Building...
dotnet restore SuperMagnifier.csproj -r win-x64
if errorlevel 1 goto :err
dotnet publish SuperMagnifier.csproj -c Release -r win-x64 /p:PublishSingleFile=true /p:SelfContained=true /p:IncludeNativeLibrariesForSelfExtract=true
if errorlevel 1 goto :err

set EXE=bin\Release\net10.0-windows\win-x64\publish\SuperMagnifierLV.exe
if not exist "%EXE%" ( echo ERROR: %EXE% not found & goto :err )

:: 2. Read the version the build just wrote to version.txt ("yyyy.MM.dd.NNNN")
if not exist "version.txt" ( echo ERROR: version.txt not found after build & goto :err )
for /f "tokens=*" %%A in (version.txt) do set NEW_VER=%%A
for /f "tokens=1,2,3,4 delims=." %%A in ("!NEW_VER!") do (
    set YY=%%A
    set MM=%%B
    set DD=%%C
    set NN=%%D
)
set /a YY_NUM=!YY!
set /a MM_NUM=1!MM!-100
set /a DD_NUM=1!DD!-100
set /a NN_NUM=1!NN!-10000
set VI_VER=!YY_NUM!.!MM_NUM!.!DD_NUM!.!NN_NUM!

echo.
echo [2/3] Built version: !NEW_VER!
echo       %EXE%
echo.

:: 3. Patch installer.nsi to match — write ps1 to avoid < > / ! cmd-escaping issues
:: $bang=[char]33 produces ! without triggering delayed-expansion stripping.
:: [\d.]+ avoids [^"] which causes ^ to be treated as a batch escape character.
echo $bang=[char]33;$nv='!NEW_VER!';$vi='!VI_VER!';$f='installer.nsi' > _patch.ps1
echo $c=Get-Content $f -Raw >> _patch.ps1
echo $c=$c -replace ($bang+'define APP_VERSION\s+"[\d.]+"'),($bang+'define APP_VERSION   "'+$nv+'"') >> _patch.ps1
echo $c=$c -replace ('VIProductVersion\s+"[\d.]+"'),('VIProductVersion "'+$vi+'"') >> _patch.ps1
echo [System.IO.File]::WriteAllText((Resolve-Path $f).Path,$c,[System.Text.Encoding]::UTF8) >> _patch.ps1
echo Patching installer.nsi...
powershell -NoProfile -ExecutionPolicy Bypass -File _patch.ps1
if errorlevel 1 ( del _patch.ps1 & goto :err )
del _patch.ps1

:: 4. Stage exe to versioned Builds\ folder
set "BUILD_DIR=%~dp0Builds\!NEW_VER!"
if not exist "!BUILD_DIR!" mkdir "!BUILD_DIR!"
copy /y "%EXE%" "!BUILD_DIR!\SuperMagnifierLV.exe" >nul
:: Portable PDB (DebugType=portable) kept with the build for crash.log line numbers — not shipped.
if exist "bin\Release\net10.0-windows\win-x64\publish\SuperMagnifierLV.pdb" copy /y "bin\Release\net10.0-windows\win-x64\publish\SuperMagnifierLV.pdb" "!BUILD_DIR!\SuperMagnifierLV.pdb" >nul
echo Staged:  Builds\!NEW_VER!\SuperMagnifierLV.exe

:: 5. NSIS (optional)
echo.
echo [3/3] Installer...
where makensis >nul 2>&1
if %errorlevel%==0 (
    makensis installer.nsi
    if errorlevel 1 ( echo WARNING: Installer failed ) else (
        echo Installer built: SuperMagnifierLV_Setup.exe
        copy /y "SuperMagnifierLV_Setup.exe" "!BUILD_DIR!\SuperMagnifierLV_Setup.exe" >nul
        echo Staged:  Builds\!NEW_VER!\SuperMagnifierLV_Setup.exe
    )
) else (
    echo NOTE: makensis not found - skipping installer.
)
echo.
echo Build complete: !NEW_VER!
goto :end

:err
echo.
echo BUILD FAILED.
exit /b 1

:end
