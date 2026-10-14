; ============================================================
;  FileCopy — NSIS Installer Script
;
;  Self-contained single-file publish — no .NET runtime needed
;  Requires NSIS 3.09+  https://nsis.sourceforge.io/
;
;  Build steps (run from the FileCopy\ project folder):
;    1. build.bat            (publishes self-contained exe)
;    2. makensis installer.nsi
;  Output: FileCopyStudio_Setup.exe
; ============================================================

Unicode True
SetCompressor /SOLID lzma

!define APP_NAME      "File Copy Studio"
!define APP_VERSION   "2026.10.13.0002"
!define APP_PUBLISHER "Matthew Pittman"
!define APP_EXE       "FileCopyStudio.exe"
!define APP_REG_KEY   "Software\File Copy Studio"
!define INSTALL_DIR   "$PROGRAMFILES64\File Copy Studio"
!define UNINSTALL_KEY "Software\Microsoft\Windows\CurrentVersion\Uninstall\File Copy Studio"
!define OUTPUT_FILE   "FileCopyStudio_Setup.exe"
!define BUILD_DIR     "dist"

!include "MUI2.nsh"
!include "LogicLib.nsh"
!include "FileFunc.nsh"

!define MUI_ABORTWARNING
!define MUI_WELCOMEPAGE_TITLE     "Welcome to ${APP_NAME} Setup"
!define MUI_WELCOMEPAGE_TEXT      "This will install ${APP_NAME} ${APP_VERSION} on your computer.$\r$\n$\r$\nFile Copy Studio is a folder-to-folder copy/move utility with conflict resolution, CRC32 verification, scheduled automation, and WCAG AAA accessibility.$\r$\n$\r$\nNo additional runtime installation required.$\r$\n$\r$\nClick Next to continue."
!define MUI_FINISHPAGE_RUN        "$INSTDIR\${APP_EXE}"
!define MUI_FINISHPAGE_RUN_TEXT   "Launch ${APP_NAME}"

!define MUI_ICON   "app.ico"
!define MUI_UNICON "app.ico"

!insertmacro MUI_PAGE_WELCOME
!insertmacro MUI_PAGE_DIRECTORY
!insertmacro MUI_PAGE_INSTFILES
!insertmacro MUI_PAGE_FINISH

!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES

!insertmacro MUI_LANGUAGE "English"

Name          "${APP_NAME} ${APP_VERSION}"
OutFile       "${OUTPUT_FILE}"
InstallDir    "${INSTALL_DIR}"
InstallDirRegKey HKLM "${UNINSTALL_KEY}" "InstallLocation"
RequestExecutionLevel admin
SetDatablockOptimize on
CRCCheck on
XPStyle on

VIProductVersion "2026.10.13.2"
VIAddVersionKey "ProductName"     "${APP_NAME}"
VIAddVersionKey "ProductVersion"  "${APP_VERSION}"
VIAddVersionKey "CompanyName"     "${APP_PUBLISHER}"
VIAddVersionKey "LegalCopyright"  "(c) 2026 ${APP_PUBLISHER}"
VIAddVersionKey "FileDescription" "${APP_NAME} Installer"
VIAddVersionKey "FileVersion"     "${APP_VERSION}"

; ── Install ───────────────────────────────────────────────────────────────────
Section "Install" SecMain

  ; Self-contained publish — no .NET runtime check needed
  SetOutPath "$INSTDIR"

  File "${BUILD_DIR}\${APP_EXE}"
  File /nonfatal "${BUILD_DIR}\README.md"
  File /nonfatal "${BUILD_DIR}\manual.pdf"
  File /nonfatal "PRIVACY.md"

  ; Start Menu
  CreateDirectory "$SMPROGRAMS\${APP_NAME}"
  CreateShortcut  "$SMPROGRAMS\${APP_NAME}\${APP_NAME}.lnk" \
                  "$INSTDIR\${APP_EXE}" "" "$INSTDIR\${APP_EXE}" 0 \
                  SW_SHOWNORMAL "" "${APP_NAME}"
  CreateShortcut  "$SMPROGRAMS\${APP_NAME}\Privacy Policy.lnk" \
                  "$INSTDIR\PRIVACY.md"
  CreateShortcut  "$SMPROGRAMS\${APP_NAME}\Uninstall ${APP_NAME}.lnk" \
                  "$INSTDIR\Uninstall.exe"

  ; Desktop shortcut
  CreateShortcut  "$DESKTOP\${APP_NAME}.lnk" \
                  "$INSTDIR\${APP_EXE}" "" "$INSTDIR\${APP_EXE}" 0 \
                  SW_SHOWNORMAL "" "${APP_NAME}"

  WriteUninstaller "$INSTDIR\Uninstall.exe"

  WriteRegStr   HKLM "${UNINSTALL_KEY}" "DisplayName"           "${APP_NAME}"
  WriteRegStr   HKLM "${UNINSTALL_KEY}" "DisplayVersion"        "${APP_VERSION}"
  WriteRegStr   HKLM "${UNINSTALL_KEY}" "Publisher"             "${APP_PUBLISHER}"
  WriteRegStr   HKLM "${UNINSTALL_KEY}" "InstallLocation"       "$INSTDIR"
  WriteRegStr   HKLM "${UNINSTALL_KEY}" "UninstallString"       '"$INSTDIR\Uninstall.exe"'
  WriteRegStr   HKLM "${UNINSTALL_KEY}" "QuietUninstallString"  '"$INSTDIR\Uninstall.exe" /S'
  WriteRegStr   HKLM "${UNINSTALL_KEY}" "DisplayIcon"           "$INSTDIR\${APP_EXE}"
  WriteRegDWORD HKLM "${UNINSTALL_KEY}" "NoModify"              1
  WriteRegDWORD HKLM "${UNINSTALL_KEY}" "NoRepair"              1

  ${GetSize} "$INSTDIR" "/S=0K" $0 $1 $2
  IntFmt $0 "0x%08X" $0
  WriteRegDWORD HKLM "${UNINSTALL_KEY}" "EstimatedSize" "$0"

  ; ── .fclist file association ─────────────────────────────────────────────────
  WriteRegStr HKLM "Software\Classes\.fclist"                ""        "FileCopy.JobFile"
  WriteRegStr HKLM "Software\Classes\FileCopyStudio.JobFile"       ""        "File Copy Studio Job"
  WriteRegStr HKLM "Software\Classes\FileCopyStudio.JobFile"       "FriendlyTypeName" "File Copy Studio Job File"
  WriteRegStr HKLM "Software\Classes\FileCopyStudio.JobFile\DefaultIcon" "" "$INSTDIR\${APP_EXE},0"
  WriteRegStr HKLM "Software\Classes\FileCopyStudio.JobFile\shell\open\command" "" '"$INSTDIR\${APP_EXE}" "%1"'

  ; ── Shell context menu: "Copy with File Copy Studio" on folders/drives ─────────────
  WriteRegStr HKLM "Software\Classes\Folder\shell\FileCopyStudio" ""             "Copy with File Copy Studio"
  WriteRegStr HKLM "Software\Classes\Folder\shell\FileCopyStudio" "Icon"         "$INSTDIR\${APP_EXE},0"
  WriteRegStr HKLM "Software\Classes\Folder\shell\FileCopyStudio\command" ""    '"$INSTDIR\${APP_EXE}" --source "%1" --scan'

  WriteRegStr HKLM "Software\Classes\Directory\shell\FileCopyStudio" ""           "Copy with File Copy Studio"
  WriteRegStr HKLM "Software\Classes\Directory\shell\FileCopyStudio" "Icon"       "$INSTDIR\${APP_EXE},0"
  WriteRegStr HKLM "Software\Classes\Directory\shell\FileCopyStudio\command" ""  '"$INSTDIR\${APP_EXE}" --source "%1" --scan'

  WriteRegStr HKLM "Software\Classes\Directory\Background\shell\FileCopyStudio" ""           "Copy this folder with File Copy Studio"
  WriteRegStr HKLM "Software\Classes\Directory\Background\shell\FileCopyStudio" "Icon"       "$INSTDIR\${APP_EXE},0"
  ; %V = current directory in Explorer background context
  WriteRegStr HKLM "Software\Classes\Directory\Background\shell\FileCopyStudio\command" ""  '"$INSTDIR\${APP_EXE}" --source "%V" --scan'

  WriteRegStr HKLM "Software\Classes\Drive\shell\FileCopyStudio" ""           "Copy with File Copy Studio"
  WriteRegStr HKLM "Software\Classes\Drive\shell\FileCopyStudio" "Icon"       "$INSTDIR\${APP_EXE},0"
  WriteRegStr HKLM "Software\Classes\Drive\shell\FileCopyStudio\command" ""  '"$INSTDIR\${APP_EXE}" --source "%1" --scan'

SectionEnd

; ── Uninstall ─────────────────────────────────────────────────────────────────
Section "Uninstall"

  MessageBox MB_ICONQUESTION|MB_YESNO \
    "Do you want to keep your saved presets and settings?$\r$\n$\r$\nClick Yes to keep them, No to delete everything." \
    IDYES keep_settings

  RMDir /r "$APPDATA\File Copy Studio"

  keep_settings:

  Delete "$INSTDIR\${APP_EXE}"
  Delete "$INSTDIR\README.md"
  Delete "$INSTDIR\manual.pdf"
  Delete "$INSTDIR\PRIVACY.md"
  Delete "$INSTDIR\Uninstall.exe"
  RMDir  "$INSTDIR"

  Delete "$SMPROGRAMS\${APP_NAME}\${APP_NAME}.lnk"
  Delete "$SMPROGRAMS\${APP_NAME}\Privacy Policy.lnk"
  Delete "$SMPROGRAMS\${APP_NAME}\Uninstall ${APP_NAME}.lnk"
  RMDir  "$SMPROGRAMS\${APP_NAME}"
  Delete "$DESKTOP\${APP_NAME}.lnk"

  DeleteRegKey HKLM "${UNINSTALL_KEY}"
  DeleteRegKey /ifempty HKCU "${APP_REG_KEY}"

  ; Shell context menu cleanup
  DeleteRegKey HKLM "Software\Classes\Folder\shell\FileCopyStudio"
  DeleteRegKey HKLM "Software\Classes\Directory\shell\FileCopyStudio"
  DeleteRegKey HKLM "Software\Classes\Directory\Background\shell\FileCopyStudio"
  DeleteRegKey HKLM "Software\Classes\Drive\shell\FileCopyStudio"

  ; .fclist file association cleanup
  DeleteRegKey HKLM "Software\Classes\.fclist"
  DeleteRegKey HKLM "Software\Classes\FileCopyStudio.JobFile"

SectionEnd
