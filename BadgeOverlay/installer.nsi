; ============================================================
;  Badge Overlay — NSIS Installer Script
;  Requires NSIS 3.09+  https://nsis.sourceforge.io/
;
;  Build steps (run from the BadgeOverlay\ project folder):
;    1. build.bat          (compiles the app)
;    2. makensis installer.nsi
;  Output: BadgeOverlay_Setup.exe
; ============================================================

Unicode True
SetCompressor zlib

!define APP_NAME      "Badge Overlay"
; ── Version ───────────────────────────────────────────────────────────────────
; Resolution order:
;   1. /DAPP_VERSION=... /DAPP_VERSION_NUMERIC=... passed by build.bat
;      (single source of truth — version.txt at repo root, parsed by the
;      same script that runs `dotnet publish`).
;   2. Otherwise, parse ../version.txt directly. This makes a manual
;      `makensis installer.nsi` invocation (no defines) still produce the
;      correct version — falling back to a hard-coded literal would silently
;      ship stale version metadata, which is exactly the bug we just hit.
;
; version.txt format: a single line "yyyy.MM.dd.NNNN" written by the
; csproj BumpBuildCounter MSBuild target. Example: "2026.09.25.0005".
!ifndef APP_VERSION
  ; !searchparse: extract the date and counter substrings from the file's
  ; first line. The wildcards (* placeholders) bind the segments into
  ; named symbols. Errors are fatal here — better to fail the installer
  ; build than ship a wrong version number.
  !searchparse /file "version.txt" "" _VER_Y "." _VER_M "." _VER_D "." _VER_NUM
  !define APP_VERSION "${_VER_Y}.${_VER_M}.${_VER_D}.${_VER_NUM}"
!endif
!ifndef APP_VERSION_NUMERIC
  ; The numeric tuple required by VIProductVersion must have NO leading zeros —
  ; Windows PE resource compiler rejects them (silently zeroes the component).
  ; build.bat strips zeros via cmd arithmetic before passing /DAPP_VERSION_NUMERIC,
  ; but a manual `makensis installer.nsi` skips that step.
  ; Fix: use !define /math to coerce each padded component to an integer.
  ; NSIS preprocessor treats leading-zero strings as decimal (not octal), so
  ; "05" + 0 = 5 and "09" + 0 = 9 — no octal hazard.
  !ifndef _VER_NUM
    !searchparse /file "version.txt" "" _VER_Y "." _VER_M "." _VER_D "." _VER_NUM
  !endif
  !define /math _VER_M_INT ${_VER_M} + 0
  !define /math _VER_D_INT ${_VER_D} + 0
  !define /math _VER_N_INT ${_VER_NUM} + 0
  !define APP_VERSION_NUMERIC "${_VER_Y}.${_VER_M_INT}.${_VER_D_INT}.${_VER_N_INT}"
!endif
!define APP_PUBLISHER "Matthew Pittman"
!define APP_URL       "https://github.com/lordmaus/Badge-Overlay"
!define APP_WEBSITE   "https://oldgamerssoftware.com/"
!define APP_EXE       "BadgeOverlay.exe"
!define APP_REG_KEY   "Software\Badge Overlay"
!define INSTALL_DIR   "$PROGRAMFILES64\Badge Overlay"
!define UNINSTALL_KEY "Software\Microsoft\Windows\CurrentVersion\Uninstall\Badge Overlay"
!define OUTPUT_FILE   "BadgeOverlay_Setup.exe"

!define BUILD_DIR     "bin\Release\net10.0-windows\win-x64\publish"

; ── Modern UI ─────────────────────────────────────────────────────────────────
!include "MUI2.nsh"
!include "LogicLib.nsh"
!include "FileFunc.nsh"

!define MUI_ABORTWARNING
!define MUI_WELCOMEPAGE_TITLE     "Welcome to ${APP_NAME} Setup"
!define MUI_WELCOMEPAGE_TEXT      "This will install ${APP_NAME} ${APP_VERSION} on your computer.$\r$\n$\r$\nBadge Overlay shows colour-coded badge tooltips for recognised UI controls system-wide - works alongside any cursor tool (Windows default, YoloMouse, custom schemes) without touching it.$\r$\n$\r$\nWhat's new in this release:$\r$\n  - Screen Reader Mode with text-to-speech (SAPI), voice picker, rate slider$\r$\n  - Idle dim - badge fades after configurable inactivity period$\r$\n  - Accessibility audit export (WCAG-mapped HTML report)$\r$\n  - Portable mode (settings next to EXE instead of %%APPDATA%%)$\r$\n  - Session statistics dashboard$\r$\n  - Colour-blind palette presets (Deuteranopia, Protanopia, Tritanopia)$\r$\n  - Full WCAG 2.2 AAA focus-ring, target-size, and radio-group pass$\r$\n$\r$\nFull release notes are in CHANGELOG.md after installation.$\r$\n$\r$\nClick Next to review the licence agreement."

; Licence acceptance page — requires the user to scroll through and
; accept before installation can proceed. Standard practice for
; distributed desktop software; satisfies #74 from the release checklist.
!define MUI_LICENSEPAGE_CHECKBOX
!define MUI_LICENSEPAGE_CHECKBOX_TEXT "I accept the terms of the licence agreement"
!define MUI_LICENSEPAGE_BUTTON "Next >"

!define MUI_FINISHPAGE_RUN        "$INSTDIR\${APP_EXE}"
!define MUI_FINISHPAGE_RUN_TEXT   "Launch ${APP_NAME}"
!define MUI_FINISHPAGE_LINK       "Visit website"
!define MUI_FINISHPAGE_LINK_LOCATION "https://oldgamerssoftware.com/"
!define MUI_FINISHPAGE_TEXT       "Badge Overlay ${APP_VERSION} has been installed.$\r$\n$\r$\nRelease notes are in CHANGELOG.md in the install folder.$\r$\n$\r$\nSource code and issue tracker: https://github.com/lordmaus/Badge-Overlay"

!define MUI_ICON   "Assets\app.ico"
!define MUI_UNICON "Assets\app.ico"

!insertmacro MUI_PAGE_WELCOME
; Licence agreement — user must accept before continuing (#74)
!insertmacro MUI_PAGE_LICENSE "LICENSE"
!insertmacro MUI_PAGE_DIRECTORY
!insertmacro MUI_PAGE_INSTFILES
!insertmacro MUI_PAGE_FINISH

!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES

!insertmacro MUI_LANGUAGE "English"

; ── General ───────────────────────────────────────────────────────────────────
Name          "${APP_NAME} ${APP_VERSION}"
OutFile       "${OUTPUT_FILE}"
InstallDir    "${INSTALL_DIR}"
InstallDirRegKey HKLM "${UNINSTALL_KEY}" "InstallLocation"
RequestExecutionLevel admin
SetDatablockOptimize on
CRCCheck on
XPStyle on

; ── Version info ──────────────────────────────────────────────────────────────
VIProductVersion "${APP_VERSION_NUMERIC}"
VIAddVersionKey  "ProductName"     "${APP_NAME}"
VIAddVersionKey  "ProductVersion"  "${APP_VERSION}"
VIAddVersionKey  "CompanyName"     "${APP_PUBLISHER}"
VIAddVersionKey  "LegalCopyright"  "(c) 2026 ${APP_PUBLISHER}"
VIAddVersionKey  "FileDescription" "${APP_NAME} Installer"
VIAddVersionKey  "FileVersion"     "${APP_VERSION}"

; ── Install ───────────────────────────────────────────────────────────────────
Section "Install" SecMain

  SetOutPath "$INSTDIR"

  File "${BUILD_DIR}\${APP_EXE}"

  ; Ship documentation alongside the EXE so About → Open README finds
  ; them. Uninstall removes them via the wildcard cleanup below. The
  ; /nonfatal flag means a missing source file warns at link time but
  ; doesn't break the installer build — useful when iterating on the
  ; binary alone without rebuilding docs.
  File /nonfatal "README.md"
  File /nonfatal "LICENSE"
  File /nonfatal "CHANGELOG.md"
  File /nonfatal "THIRD-PARTY-NOTICES.txt"
  File /nonfatal "CONTRIBUTING.md"
  File /nonfatal "SECURITY.md"
  File /nonfatal "PRIVACY.md"


  CreateDirectory "$SMPROGRAMS\${APP_NAME}"
  CreateShortcut  "$SMPROGRAMS\${APP_NAME}\${APP_NAME}.lnk" \
                  "$INSTDIR\${APP_EXE}" "" "$INSTDIR\${APP_EXE}" 0 \
                  SW_SHOWNORMAL "" "${APP_NAME}"
  CreateShortcut  "$SMPROGRAMS\${APP_NAME}\Privacy Policy.lnk" \
                  "$INSTDIR\PRIVACY.md"
  CreateShortcut  "$SMPROGRAMS\${APP_NAME}\Uninstall ${APP_NAME}.lnk" \
                  "$INSTDIR\Uninstall.exe"

  CreateShortcut  "$DESKTOP\${APP_NAME}.lnk" \
                  "$INSTDIR\${APP_EXE}" "" "$INSTDIR\${APP_EXE}" 0 \
                  SW_SHOWNORMAL "" "${APP_NAME}"

  WriteUninstaller "$INSTDIR\Uninstall.exe"

  WriteRegStr   HKLM "${UNINSTALL_KEY}" "DisplayName"           "${APP_NAME}"
  WriteRegStr   HKLM "${UNINSTALL_KEY}" "DisplayVersion"        "${APP_VERSION}"
  WriteRegStr   HKLM "${UNINSTALL_KEY}" "Publisher"             "${APP_PUBLISHER}"
  WriteRegStr   HKLM "${UNINSTALL_KEY}" "URLInfoAbout"          "${APP_URL}"
  WriteRegStr   HKLM "${UNINSTALL_KEY}" "HelpLink"              "https://oldgamerssoftware.com/"
  WriteRegStr   HKLM "${UNINSTALL_KEY}" "InstallLocation"       "$INSTDIR"
  WriteRegStr   HKLM "${UNINSTALL_KEY}" "UninstallString"       '"$INSTDIR\Uninstall.exe"'
  WriteRegStr   HKLM "${UNINSTALL_KEY}" "QuietUninstallString"  '"$INSTDIR\Uninstall.exe" /S'
  WriteRegStr   HKLM "${UNINSTALL_KEY}" "DisplayIcon"           "$INSTDIR\${APP_EXE}"
  WriteRegDWORD HKLM "${UNINSTALL_KEY}" "NoModify"              1
  WriteRegDWORD HKLM "${UNINSTALL_KEY}" "NoRepair"              1

  ${GetSize} "$INSTDIR" "/S=0K" $0 $1 $2
  IntFmt $0 "0x%08X" $0
  WriteRegDWORD HKLM "${UNINSTALL_KEY}" "EstimatedSize" "$0"

SectionEnd

; ── Uninstall ─────────────────────────────────────────────────────────────────
Section "Uninstall"

  MessageBox MB_ICONQUESTION|MB_YESNO \
    "Do you want to keep your saved settings (badge colours, rules, hotkeys)?$\r$\n$\r$\nClick Yes to keep them, No to delete everything." \
    IDYES keep_settings

  RMDir /r "$APPDATA\BadgeOverlay"

  keep_settings:

  DeleteRegValue HKCU "Software\Microsoft\Windows\CurrentVersion\Run" "BadgeOverlay"

  Delete "$INSTDIR\${APP_EXE}"
  Delete "$INSTDIR\Uninstall.exe"
  Delete "$INSTDIR\THIRD-PARTY-NOTICES.txt"
  Delete "$INSTDIR\CONTRIBUTING.md"
  Delete "$INSTDIR\SECURITY.md"
  Delete "$INSTDIR\README.md"
  Delete "$INSTDIR\LICENSE"
  Delete "$INSTDIR\CHANGELOG.md"
  Delete "$INSTDIR\PRIVACY.md"

  RMDir  "$INSTDIR"

  Delete "$SMPROGRAMS\${APP_NAME}\${APP_NAME}.lnk"
  Delete "$SMPROGRAMS\${APP_NAME}\Privacy Policy.lnk"
  Delete "$SMPROGRAMS\${APP_NAME}\Uninstall ${APP_NAME}.lnk"
  RMDir  "$SMPROGRAMS\${APP_NAME}"
  Delete "$DESKTOP\${APP_NAME}.lnk"

  DeleteRegKey HKLM "${UNINSTALL_KEY}"
  DeleteRegKey /ifempty HKCU "${APP_REG_KEY}"

SectionEnd
