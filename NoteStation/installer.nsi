; ============================================================
;  NoteStation -- NSIS Installer Script
;
;  VERSION NOTE: APP_VERSION and VIProductVersion must match
;  the <Version> in NoteStation.csproj. Update both together.
;  Requires NSIS 3.09+  https://nsis.sourceforge.io/
;
;  Build steps (run from the NoteStation\ project folder):
;    1. build.bat          (compiles the app)
;    2. makensis installer.nsi
;  Output: Builds\<version>\NoteStation_Setup.exe
; ============================================================

Unicode True
SetCompressor /SOLID lzma

!define APP_NAME      "NoteStation"
!define APP_VERSION   "2026.10.12.0003"
!define APP_PUBLISHER "Matthew Pittman"
!define APP_EXE       "NoteStation.exe"
!define APP_REG_KEY   "Software\NoteStation"
!define INSTALL_DIR   "$PROGRAMFILES64\NoteStation"
!define UNINSTALL_KEY "Software\Microsoft\Windows\CurrentVersion\Uninstall\NoteStation"
!define OUTPUT_FILE   "NoteStation_Setup.exe"
!define STAGE_DIR     "Builds\${APP_VERSION}"

; Source: build output relative to installer.nsi
!define BUILD_DIR     "bin\Release\net10.0-windows10.0.22621.0\win-x64"

; -- Modern UI ------------------------------------------------------------------
!include "MUI2.nsh"
!include "LogicLib.nsh"
!include "FileFunc.nsh"

!define MUI_ABORTWARNING
!define MUI_WELCOMEPAGE_TITLE     "Welcome to ${APP_NAME} Setup"
!define MUI_WELCOMEPAGE_TEXT      "This will install ${APP_NAME} ${APP_VERSION} on your computer.$\r$\n$\r$\nNoteStation is a clipboard manager, sticky notes overlay, and task manager designed for Low Vision users.$\r$\n$\r$\nClick Next to continue."
!define MUI_FINISHPAGE_RUN        "$INSTDIR\${APP_EXE}"
!define MUI_FINISHPAGE_RUN_TEXT   "Launch ${APP_NAME}"

!define MUI_ICON   "Assets\app.ico"
!define MUI_UNICON "Assets\app.ico"

!insertmacro MUI_PAGE_WELCOME
!insertmacro MUI_PAGE_DIRECTORY
!insertmacro MUI_PAGE_INSTFILES
!insertmacro MUI_PAGE_FINISH

!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES

!insertmacro MUI_LANGUAGE "English"

; -- General -------------------------------------------------------------------
Name          "${APP_NAME} ${APP_VERSION}"
OutFile       "${STAGE_DIR}\${OUTPUT_FILE}"
InstallDir    "${INSTALL_DIR}"
InstallDirRegKey HKLM "${UNINSTALL_KEY}" "InstallLocation"
RequestExecutionLevel admin
SetDatablockOptimize on
CRCCheck on
XPStyle on

; -- Version info embedded in the setup exe ------------------------------------
VIProductVersion "2026.10.12.3"
VIAddVersionKey  "ProductName"     "${APP_NAME}"
VIAddVersionKey  "ProductVersion"  "${APP_VERSION}"
VIAddVersionKey  "CompanyName"     "${APP_PUBLISHER}"
VIAddVersionKey  "LegalCopyright"  "(c) 2026 ${APP_PUBLISHER}"
VIAddVersionKey  "FileDescription" "${APP_NAME} Installer"
VIAddVersionKey  "FileVersion"     "${APP_VERSION}"

; -- Install Section -----------------------------------------------------------
Section "Install" SecMain

  SetOutPath "$INSTDIR"

  File "${BUILD_DIR}\${APP_EXE}"

  File "Assets\app.ico"

  ; All runtime files from the self-contained publish
  File /r "${BUILD_DIR}\*.dll"
  File /r "${BUILD_DIR}\*.json"

  ; Optional: README
  IfFileExists "${BUILD_DIR}\README.md" 0 +2
    File "${BUILD_DIR}\README.md"

  ; Bundle privacy policy so it's available offline
  File "PRIVACY.md"

  ; Registry: app registration
  WriteRegStr HKLM "${APP_REG_KEY}" "InstallPath" "$INSTDIR"
  WriteRegStr HKLM "${APP_REG_KEY}" "Version"     "${APP_VERSION}"

  ; Registry: Add/Remove Programs
  WriteRegStr HKLM "${UNINSTALL_KEY}" "DisplayName"          "${APP_NAME}"
  WriteRegStr HKLM "${UNINSTALL_KEY}" "DisplayVersion"       "${APP_VERSION}"
  WriteRegStr HKLM "${UNINSTALL_KEY}" "Publisher"            "${APP_PUBLISHER}"
  WriteRegStr HKLM "${UNINSTALL_KEY}" "InstallLocation"      "$INSTDIR"
  WriteRegStr HKLM "${UNINSTALL_KEY}" "UninstallString"      "$INSTDIR\Uninstall.exe"
  WriteRegStr HKLM "${UNINSTALL_KEY}" "DisplayIcon"          "$INSTDIR\${APP_EXE}"
  WriteRegDWORD HKLM "${UNINSTALL_KEY}" "NoModify"           1
  WriteRegDWORD HKLM "${UNINSTALL_KEY}" "NoRepair"           1

  ; Start menu
  CreateDirectory "$SMPROGRAMS\${APP_NAME}"
  CreateShortcut  "$SMPROGRAMS\${APP_NAME}\${APP_NAME}.lnk"  "$INSTDIR\${APP_EXE}"
  CreateShortcut  "$SMPROGRAMS\${APP_NAME}\Privacy Policy.lnk" "$INSTDIR\PRIVACY.md"
  CreateShortcut  "$SMPROGRAMS\${APP_NAME}\Uninstall.lnk"    "$INSTDIR\Uninstall.exe"
  CreateShortcut  "$DESKTOP\${APP_NAME}.lnk"                 "$INSTDIR\${APP_EXE}"

  WriteUninstaller "$INSTDIR\Uninstall.exe"

SectionEnd

; -- Uninstall Section ---------------------------------------------------------
Section "Uninstall"

  Delete "$INSTDIR\${APP_EXE}"
  Delete "$INSTDIR\app.ico"
  Delete "$INSTDIR\README.md"
  Delete "$INSTDIR\Uninstall.exe"
  RMDir /r "$INSTDIR"

  Delete "$SMPROGRAMS\${APP_NAME}\${APP_NAME}.lnk"
  Delete "$SMPROGRAMS\${APP_NAME}\Privacy Policy.lnk"
  Delete "$SMPROGRAMS\${APP_NAME}\Uninstall.lnk"
  RMDir  "$SMPROGRAMS\${APP_NAME}"
  Delete "$DESKTOP\${APP_NAME}.lnk"

  ; --- Offer to remove user data from %AppData%\NoteStation\ ---
  IfFileExists "$APPDATA\NoteStation\*.*" DataExists NoData
  DataExists:
    MessageBox MB_YESNO|MB_ICONQUESTION \
      "Do you also want to delete your NoteStation data?$\r$\n$\r$\nThis includes all clipboard history, sticky notes, and tasks stored in:$\r$\n$APPDATA\NoteStation\$\r$\n$\r$\nThis cannot be undone." \
      IDYES DeleteData IDNO SkipData
    DeleteData:
      RMDir /r "$APPDATA\NoteStation"
    SkipData:
  NoData:

  DeleteRegKey HKLM "${UNINSTALL_KEY}"
  DeleteRegKey HKLM "${APP_REG_KEY}"

SectionEnd
