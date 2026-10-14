; version.nsh — suite installer version (S-03, 2026-10-14).
;
; Usage, at the top of an app's installer.nsi:
;   !define APP_VERSION_FILE "${__FILEDIR__}\version.txt"
;   !include "${__FILEDIR__}\..\Tools\version.nsh"
;
; build.bat passes /DAPP_VERSION=... /DAPP_VERSION_NUMERIC=... (from Tools\bump_version.cmd).
; A manual "makensis installer.nsi" reads <app>\version.txt instead — the same file
; Directory.Build.props uses for the exe version, so installer and exe always match.
; Defines:
;   APP_VERSION          2026.10.14.0008   (display, DisplayVersion, VIAddVersionKey)
;   APP_VERSION_NUMERIC  2026.10.14.8      (VIProductVersion — PE resource needs no leading zeros)

!ifndef APP_VERSION
  !ifndef APP_VERSION_FILE
    !error "Define APP_VERSION_FILE (path to version.txt) before including version.nsh"
  !endif
  ; Fails the installer build if version.txt is missing or malformed — never ship a stale literal.
  !searchparse /file "${APP_VERSION_FILE}" "" _VER_Y "." _VER_M "." _VER_D "." _VER_NUM
  !define APP_VERSION "${_VER_Y}.${_VER_M}.${_VER_D}.${_VER_NUM}"
!endif

!ifndef APP_VERSION_NUMERIC
  !ifndef _VER_NUM
    !searchparse "${APP_VERSION}" "" _VER_Y "." _VER_M "." _VER_D "." _VER_NUM
  !endif
  ; "1" prefix + subtract: "108" - 100 = 8, so a zero-padded "08" is never read as octal.
  !define /math _VER_M_INT 1${_VER_M} - 100
  !define /math _VER_D_INT 1${_VER_D} - 100
  !define /math _VER_N_INT 1${_VER_NUM} - 10000
  !define APP_VERSION_NUMERIC "${_VER_Y}.${_VER_M_INT}.${_VER_D_INT}.${_VER_N_INT}"
!endif
