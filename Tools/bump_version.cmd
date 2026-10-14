@echo off
:: bump_version.cmd — suite version bump (S-03, 2026-10-14).
::
::   call "%~dp0..\Tools\bump_version.cmd" "%~dp0version.txt"            bump + read
::   call "%~dp0..\Tools\bump_version.cmd" "%~dp0version.txt" --no-bump  read only
::
:: Sets for the caller (no setlocal here on purpose):
::   APP_VERSION          2026.10.14.0008   (folder name, NSIS APP_VERSION, csproj Version)
::   APP_VERSION_NUMERIC  2026.10.14.8      (VIProductVersion / AssemblyVersion)
:: errorlevel 1 on failure.

set "APP_VERSION="
set "APP_VERSION_NUMERIC="
set "_BV_FLAG="
if /i "%~2"=="--no-bump" set "_BV_FLAG=-NoBump"
if "%~1"=="" (
    echo ERROR: bump_version.cmd needs the path to version.txt.
    exit /b 1
)

for /f "usebackq tokens=1,2" %%a in (`powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0bump_version.ps1" -File "%~1" %_BV_FLAG%`) do (
    set "APP_VERSION=%%a"
    set "APP_VERSION_NUMERIC=%%b"
)
set "_BV_FLAG="

if not defined APP_VERSION (
    echo ERROR: could not read or bump %~1
    exit /b 1
)
echo Version : %APP_VERSION%
exit /b 0
