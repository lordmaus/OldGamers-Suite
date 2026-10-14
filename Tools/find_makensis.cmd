@echo off
:: find_makensis.cmd — sets MAKENSIS to the full path of makensis.exe (NSIS 3.x).
:: Used by every app's build.bat:  call "%~dp0..\Tools\find_makensis.cmd"
:: Search order: NSIS_HOME env var, PATH, Program Files (x86), Program Files, registry.
:: Exit code 0 = found, 1 = not found. No setlocal on purpose: MAKENSIS must reach the caller.
set "MAKENSIS="
if defined NSIS_HOME if exist "%NSIS_HOME%\makensis.exe" set "MAKENSIS=%NSIS_HOME%\makensis.exe"
if not defined MAKENSIS for /f "delims=" %%P in ('where makensis.exe 2^>nul') do if not defined MAKENSIS set "MAKENSIS=%%P"
if not defined MAKENSIS if exist "%ProgramFiles(x86)%\NSIS\makensis.exe" set "MAKENSIS=%ProgramFiles(x86)%\NSIS\makensis.exe"
if not defined MAKENSIS if exist "%ProgramFiles%\NSIS\makensis.exe" set "MAKENSIS=%ProgramFiles%\NSIS\makensis.exe"
if not defined MAKENSIS for /f "tokens=2,*" %%A in ('reg query "HKLM\SOFTWARE\WOW6432Node\NSIS" /ve 2^>nul ^| find "REG_SZ"') do if exist "%%B\makensis.exe" set "MAKENSIS=%%B\makensis.exe"
if not defined MAKENSIS for /f "tokens=2,*" %%A in ('reg query "HKLM\SOFTWARE\NSIS" /ve 2^>nul ^| find "REG_SZ"') do if exist "%%B\makensis.exe" set "MAKENSIS=%%B\makensis.exe"
if not defined MAKENSIS exit /b 1
echo Using NSIS: %MAKENSIS%
exit /b 0
