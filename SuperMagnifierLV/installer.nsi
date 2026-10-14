; ============================================================
;  Super Magnifier LV -- NSIS Installer Script
;  Requires NSIS 3.09+  https://nsis.sourceforge.io/
;
;  Build steps (run from the SuperMagnifier\ project folder):
;    1. build.bat          (compiles the app)
;    2. makensis installer.nsi
;  Output: SuperMagnifierLV_Setup.exe
; ============================================================

Unicode True
SetCompressor zlib

!define APP_NAME      "Super Magnifier LV"
!define APP_VERSION   "2026.10.14.0006"
!define APP_PUBLISHER "Matthew Pittman"
!define APP_URL       "https://github.com/supermagnifier"
!define APP_EXE       "SuperMagnifierLV.exe"
!define APP_REG_KEY   "Software\Super Magnifier LV"
!define INSTALL_DIR   "$PROGRAMFILES64\Super Magnifier LV"
!define UNINSTALL_KEY "Software\Microsoft\Windows\CurrentVersion\Uninstall\Super Magnifier LV"
!define OUTPUT_FILE   "SuperMagnifierLV_Setup.exe"

; Source: build output relative to installer.nsi
!define BUILD_DIR     "bin\Release\net10.0-windows\win-x64\publish"

; -- Modern UI ----------------------------------------------------------------
!include "MUI2.nsh"
!include "LogicLib.nsh"
!include "FileFunc.nsh"

!define MUI_ABORTWARNING
!define MUI_WELCOMEPAGE_TITLE     "Welcome to ${APP_NAME} Setup"
!define MUI_WELCOMEPAGE_TEXT      "This will install ${APP_NAME} ${APP_VERSION} on your computer.$\r$\n$\r$\nSuper Magnifier LV is a screen magnification tool designed for users with low vision. It provides smooth, real-time magnification powered by the Windows Magnification API.$\r$\n$\r$\nPrivacy: This application stores your preferences locally in %AppData%\SuperMagnifierLV\. No personal data is collected and no data is transmitted to any server.$\r$\n$\r$\nClick Next to continue."
!define MUI_FINISHPAGE_RUN        "$INSTDIR\${APP_EXE}"
!define MUI_FINISHPAGE_RUN_TEXT   "Launch ${APP_NAME}"
!define MUI_FINISHPAGE_LINK       "Visit project page"
!define MUI_FINISHPAGE_LINK_LOCATION "${APP_URL}"

!define MUI_ICON   "Assets\app.ico"
!define MUI_UNICON "Assets\app.ico"

!insertmacro MUI_PAGE_WELCOME
!insertmacro MUI_PAGE_DIRECTORY
!insertmacro MUI_PAGE_INSTFILES
!insertmacro MUI_PAGE_FINISH

!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES

!insertmacro MUI_LANGUAGE "English"

; -- General ------------------------------------------------------------------
Name          "${APP_NAME} ${APP_VERSION}"
OutFile       "${OUTPUT_FILE}"
InstallDir    "${INSTALL_DIR}"
InstallDirRegKey HKLM "${UNINSTALL_KEY}" "InstallLocation"
RequestExecutionLevel admin
SetDatablockOptimize on
CRCCheck on
XPStyle on

; -- Version info embedded in the setup exe -----------------------------------
VIProductVersion "2026.10.14.6"
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

  ; Bundle privacy policy so it's available offline
  File "PRIVACY.md"

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

  ; Add/Remove Programs
  WriteRegStr   HKLM "${UNINSTALL_KEY}" "DisplayName"           "${APP_NAME}"
  WriteRegStr   HKLM "${UNINSTALL_KEY}" "DisplayVersion"        "${APP_VERSION}"
  WriteRegStr   HKLM "${UNINSTALL_KEY}" "Publisher"             "${APP_PUBLISHER}"
  WriteRegStr   HKLM "${UNINSTALL_KEY}" "URLInfoAbout"          "${APP_URL}"
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

; -- Uninstall Section ---------------------------------------------------------
Section "Uninstall"

  MessageBox MB_ICONQUESTION|MB_YESNO \
    "Do you want to keep your saved settings (zoom, presets, colours)?$\r$\n$\r$\nClick Yes to keep them, No to delete everything." \
    IDYES keep_settings

  ; Settings live in %AppData%\SuperMagnifierLV\ -- not in the install dir
  RMDir /r "$APPDATA\SuperMagnifierLV"

  keep_settings:

  DeleteRegValue HKCU "Software\Microsoft\Windows\CurrentVersion\Run" "${APP_NAME}"

  Delete "$INSTDIR\${APP_EXE}"
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

SectionEnd
