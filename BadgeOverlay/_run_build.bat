@echo off
cd /d "%~dp0"
call build.bat > _build_log.txt 2>&1
set EXIT_CODE=%errorlevel%

:: Copy log into the Builds folder that build.bat just created.
:: version.txt was bumped by build.bat (Tools\bump_version.cmd).
set "VER_DATE=" & set "VER_NUM="
for /f "tokens=1-4 delims=. " %%a in (version.txt) do if not defined VER_DATE (
    set "VER_DATE=%%a.%%b.%%c"
    set "VER_NUM=%%d"
)
if defined VER_DATE move /y "_build_log.txt" "Builds\%VER_DATE%.%VER_NUM%\build_log.txt" >nul 2>&1

echo %EXIT_CODE% > _build_done.txt
