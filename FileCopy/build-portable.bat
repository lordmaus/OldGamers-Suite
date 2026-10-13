@echo off
setlocal enabledelayedexpansion
title File Copy Studio — Portable Build

cd /d "%~dp0"

:: ── Read current version ──────────────────────────────────────────────────────
set /p VERSION=<version.txt
echo Building portable package: version !VERSION!
echo.

:: ── Publish self-contained single-file ───────────────────────────────────────
echo [1/3] Publishing self-contained release...
if exist "dist" rmdir /s /q dist
dotnet publish FileCopy.csproj ^
  -c Release ^
  -r win-x64 ^
  --self-contained true ^
  -p:PublishSingleFile=true ^
  -p:EnableCompressionInSingleFile=true ^
  -p:IncludeNativeLibrariesForSelfExtract=true ^
  -o dist ^
  --nologo -v quiet

if errorlevel 1 (
  echo PUBLISH FAILED.
  exit /b 1
)
echo     Published to dist\

:: ── Create portable package ───────────────────────────────────────────────────
echo [2/3] Creating portable package...
if not exist "publish\portable" mkdir "publish\portable"
xcopy /y /q dist\* publish\portable\ >nul
if exist docs\manual.pdf copy /y docs\manual.pdf publish\portable\manual.pdf >nul
echo File Copy Studio Portable Mode > publish\portable\portable.flag
echo.     This file activates portable mode. >> publish\portable\portable.flag
echo.     All settings and logs are stored in the Data\ folder next to the exe. >> publish\portable\portable.flag
echo.     Delete this file to switch to installed mode (%%APPDATA%% storage). >> publish\portable\portable.flag

:: Create empty Data dir so USB is ready to use immediately
if not exist "publish\portable\Data" mkdir "publish\portable\Data"
if not exist "publish\portable\Data\logs" mkdir "publish\portable\Data\logs"

:: ── Zip the portable package ──────────────────────────────────────────────────
echo [3/3] Packaging...
set ZIPNAME=FileCopyStudio_Portable_!VERSION!.zip

:: Use PowerShell for zip (no external tools needed)
powershell -NoProfile -Command ^
  "Compress-Archive -Path 'publish\portable\*' -DestinationPath '!ZIPNAME!' -Force"

if errorlevel 1 (
  echo ZIP FAILED.
  exit /b 1
)

echo.
echo ============================================================
echo  Portable build complete!
echo  Output : !ZIPNAME!
echo  Version: !VERSION!
echo.
echo  Extract all files to a folder (e.g. USB drive).
echo  Run FileCopyStudio.exe — portable.flag activates data
echo  storage in Data\ next to the exe, not %%APPDATA%%.
echo ============================================================
echo.
