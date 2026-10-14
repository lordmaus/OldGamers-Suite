; ============================================================
;  Reticle Overlay — NSIS Installer Script
;
;  Requires .NET 10 (Windows Desktop Runtime)
;  Requires NSIS 3.09+  https://nsis.sourceforge.io/
;
;  Build steps (run from the ReticleOverlay\ project folder):
;    1. build.bat            (compiles the app)
;    2. makensis installer.nsi
;  Output: ReticleOverlay_Setup.exe
; ============================================================

Unicode True
SetCompressor /SOLID lzma

; Silent install / uninstall:
;   Install:   ReticleOverlay_Setup.exe /S
;   Uninstall: Uninstall.exe /S
; NSIS MUI2 supports /S by default — no custom page logic overrides it.
; QuietUninstallString in the registry also passes /S to the uninstaller.
; Both are required for WACK / Windows App Certification Kit silent-mode tests.

!define APP_NAME      "Reticle Overlay"
!define APP_VERSION   "2026.10.13.0002"
!define APP_PUBLISHER "Matthew Pittman"
!define APP_EXE       "ReticleOverlay.exe"
!define APP_REG_KEY   "Software\Reticle Overlay"
!define INSTALL_DIR   "$PROGRAMFILES64\Reticle Overlay"
!define UNINSTALL_KEY "Software\Microsoft\Windows\CurrentVersion\Uninstall\Reticle Overlay"
!define OUTPUT_FILE   "ReticleOverlay_Setup.exe"
; L-4: the HKCU\...\Run autorun VALUE NAME the app actually writes
; (Windows\SettingsDialog.xaml.cs: `private const string AppKey = "ReticleOverlay";`)
; — no space, unlike APP_NAME. The uninstaller used to DeleteRegValue with
; "${APP_NAME}" ("Reticle Overlay", with a space), which never matched
; anything, so the real "ReticleOverlay" Run entry silently survived every
; uninstall. Keep this in sync with AppKey if that constant ever changes.
!define APP_RUN_VALUE "ReticleOverlay"
!define BUILD_DIR     "bin\Release\net10.0-windows"

!include "MUI2.nsh"
!include "LogicLib.nsh"
!include "FileFunc.nsh"

!define MUI_ABORTWARNING
!define MUI_WELCOMEPAGE_TITLE     "Welcome to ${APP_NAME} Setup"
!define MUI_WELCOMEPAGE_TEXT      "This will install ${APP_NAME} ${APP_VERSION} on your computer.$\r$\n$\r$\nReticle Overlay draws a fully customizable crosshair on top of any game or application, with per-game profiles, global hotkeys, process auto-switching, and system-tray operation.$\r$\n$\r$\nClick Next to continue."
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

; ── .NET 10 Desktop Runtime check ────────────────────────────────────────────
Function CheckDotNet10
  ; Strategy 1: look for any 10.x.x folder under the WindowsDesktop shared fx directory
  FindFirst $0 $1 "$PROGRAMFILES64\dotnet\shared\Microsoft.WindowsDesktop.App\10.*"
  StrCmp $1 "" 0 found
  FindClose $0

  ; Strategy 2: same path via ProgramFiles (covers 32-bit NSIS on 64-bit OS)
  FindFirst $0 $1 "$PROGRAMFILES\dotnet\shared\Microsoft.WindowsDesktop.App\10.*"
  StrCmp $1 "" 0 found
  FindClose $0

  ; Strategy 3: registry — HKLM\SOFTWARE\dotnet\Setup\InstalledVersions\x64\sharedfx\Microsoft.WindowsDesktop.App
  ;             value names ARE the version strings e.g. "10.0.0" with data "1"
  StrCpy $2 0
  reg_loop:
    EnumRegValue $3 HKLM "SOFTWARE\dotnet\Setup\InstalledVersions\x64\sharedfx\Microsoft.WindowsDesktop.App" $2
    StrCmp $3 "" reg_done
    StrCpy $4 $3 3
    StrCmp $4 "10." found
    IntOp $2 $2 + 1
    Goto reg_loop
  reg_done:

  ; Not found — prompt user
  MessageBox MB_ICONEXCLAMATION|MB_YESNO \
    "${APP_NAME} requires .NET 10 Windows Desktop Runtime.$\r$\n$\r$\nIt does not appear to be installed.$\r$\n$\r$\nOpen the .NET 10 download page?" \
    IDYES open_dotnet IDNO abort_install
  open_dotnet:
    ExecShell "open" "https://dotnet.microsoft.com/download/dotnet/10.0"
  abort_install:
    Abort "Installation cancelled — .NET 10 Windows Desktop Runtime is required."
    Return

  found:
    FindClose $0
FunctionEnd

; ── Install ───────────────────────────────────────────────────────────────────
Section "Install" SecMain

  Call CheckDotNet10

  SetOutPath "$INSTDIR"

  File "${BUILD_DIR}\${APP_EXE}"
  File "Assets\app.ico"
  File /nonfatal "${BUILD_DIR}\*.dll"
  File /nonfatal "${BUILD_DIR}\*.json"
  File /nonfatal "README.md"
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

SectionEnd

; ── Uninstall ─────────────────────────────────────────────────────────────────
Section "Uninstall"

  MessageBox MB_ICONQUESTION|MB_YESNO \
    "Do you want to keep your saved profiles and settings?$\r$\n$\r$\nClick Yes to keep them, No to delete everything." \
    IDYES keep_settings

  RMDir /r "$APPDATA\ReticleOverlay"

  keep_settings:

  DeleteRegValue HKCU "Software\Microsoft\Windows\CurrentVersion\Run" "${APP_RUN_VALUE}"

  Delete "$INSTDIR\${APP_EXE}"
  Delete "$INSTDIR\app.ico"
  Delete "$INSTDIR\README.md"
  Delete "$INSTDIR\*.dll"
  Delete "$INSTDIR\*.json"
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
