# Changelog

All notable changes to Reticle Overlay are documented here.
Format follows [Keep a Changelog](https://keepachangelog.com/en/1.0.0/).
Versions follow `yyyy.mm.dd.xxxx` (date + 4-digit daily build counter; 3-digit before 2026-10-13).

## [Unreleased] – 2026-10-13 · Suite shared library

Source-level change (not build-verified in the session that made it). Suite repo: `OldGamers.Suite.slnx`.

### Changed
- **Themes come from the suite set** (`..\Shared\Themes`, 108 themes, linked into the app). Dark / Light now use the higher-contrast NoteStation/TimerStudio values (`#000000` / `#FFFFFF` base); other themes differ only slightly.
- **Dialogs follow the app theme**: message and confirm boxes use the themed `AppDialog` (theme colours, app font, 44 px buttons, focus on the safe default, Esc, Ctrl+C) instead of the unthemed Windows box. Startup / crash messages keep the Windows box.
- **UI font setting applies everywhere**: hard-coded `Segoe UI` in XAML replaced with the `AppFontFamily` token; monospace text uses `MonoFontFamily` (Cascadia Mono, Consolas) — "Segoe UI Mono" is not a Windows font.
- **Starting the app again brings the running window forward** (from the tray or minimised) instead of showing "Already running".
- Resize grip, version display now come from the shared library `OldGamers.Wpf.Shared` (same behaviour).

## [2026.10.13.0001] – 2026-10-13 · Skills Review fixes

Source-level change (not build-verified in the session that made it). `version.txt` set to `2026.10.13.0000`; `build.bat` on 2026-10-13 produces `.0001` (another day → that day's `.0001`). Plan: `Claude outputs/ReticleOverlay_SkillsReview_FixPlan_2026-10-13.md`.

### Fixed
- **Damaged `profiles.json` / `prefs.json` no longer wipes your profiles** (RO-05): the unreadable file is kept as `*.corrupt-<date>.json`, the app starts with defaults and tells you once where the file is.
- **Crashes and load/save errors are recorded** (RO-03) in `%AppData%\ReticleOverlay\crash.log` (was Debug-only, nothing in Release). Crash-time settings save runs on the UI thread. About → Open crash log folder.
- **Hotkeys owned by another app are shown** (RO-02): Hotkeys dialog banner names them, their boxes get a red border, and editing a taken key says so instead of "Hotkey updated ✓".
- **"Reset All to Defaults" really resets** (RO-08) to F8 / F9 / F10 (others blank) after a confirm; it used to clear every hotkey without asking.
- **Image path check is an allowlist** (RO-01): only `X:\…` on a non-network drive; `\\?\UNC`, `\??\UNC`, device paths, URIs, drive-relative and stream paths are rejected. Browse dialog validates the saved path first (RO-07). Import says how many unsafe image paths it cleared.
- Runs in more than one Windows session (RO-09): single-instance mutex is per session (`Local\`).
- Status messages stay ≥ 5 s and are announced to screen readers (were 2 s, and only the default status was announced).

### Changed
- **WCAG 2.2 AAA (§5):** all 107 themes pass 7:1 (new `BrushDivider`, `*Text`, `BrushOnAccent`; Hint/Focus fixed); accent text uses `*Text`; menu items 44 px; minimum text 12 px (`FontSizeCaption`, 40 sites); section headings exposed as headings; 44 px keyboard `AccessibleResizeGrip` on Main, About, Hotkeys and Settings (native/20 px grips removed).
- 4-digit build counter (RO-04); `DebugType=portable` and `build.bat` copies the PDB into `Builds\<version>\`.
- About: donation labels text-only (emoji removed). README: anti-cheat FAQ (RO-06). PRIVACY.md: crash.log and kept corrupt files.

### Added
- `ReticleOverlay.Tests` (xUnit v3): path validator table, profile round-trip + corrupt-file keep, hotkey parsing, reset defaults.

## [Unreleased] – 2026-09-25 · Full-color icons

Source-level change only (no `dotnet` toolchain in the session that made it) — pending a local `build.bat` run, which also assigns the version number.

### Changed
- **Full-color suite icons replace emoji / Unicode glyphs on buttons, checkboxes, labels and dialog headers.** New `Theming/ColorIcons.xaml` (45 fixed-color `DrawingImage`s, keys `JtIcon.<Name>`) merged after the theme. `App.SetTheme` and `App.ApplyHighContrast` both clear `MergedDictionaries`, so each now re-adds the (cached) icon dictionary right after the theme; icons look the same in every theme, including both high-contrast themes. Same concept = same icon as the other suite apps (Hotkeys keyboard, Settings gear, About "?", Save floppy, Open, Rename, Copy, Delete, Export/Import, Clipboard, Web, Link, File, Refresh, Undo for reset, Lock, Swatches for the palette, Rules for per-game auto-switch, Template for profiles, Overlay for overlay on/off, EyeOff for hide-from-capture, SuperMagnifierLV Reticle as the app mark). 22 drawn for ReticleOverlay: Centre, CentreGap, DualColor, Layers, Offset, Opacity, Outline, ReticleArrow, ReticleChevron, ReticleCircle, ReticleCircleCross, ReticleCrossDot, ReticleCrosshair, ReticleDiamond, ReticleDot, ReticleHexagon, ReticleImage, ReticleOctagon, ReticleTShape, Rotation, Size, Thickness.
- **Reticle type lists** (layer 1 and layer 2): each shape has its own icon from one tiled family (dark tile, green shape); the "✛ ● ○ ⊕ ⊤ ∧ ◇ ↑ ⬡ ⯃ ⊗ 🖼" prefixes are gone (⯃ rendered as a box on some systems).
- **Overlay toggle**: fixed Overlay icon with "Overlay on" / "Overlay off"; the button is a PrimaryButton when on and a FlatButton when off (it looked the same in both states before), and its `AutomationProperties.Name` and ToolTip now state on/off (was always "Toggle reticle overlay").
- **Profile list**: profiles bound to a game show a Rules icon and the .exe name next to the profile name; the list refreshes as you type the .exe. Profile list items are now `ReticleProfile` objects (`ToString()` gives the UIA name; `TextSearch.TextPath="Name"`).
- Size, Thickness, Centre Gap, Rotation, Opacity, X/Y Offset, Palette, Layer 2, Profile and Auto-switch labels: 16px icons. Dual arm color, Show outline and three Settings checkboxes: 16px icons.
- Added missing `AutomationProperties.Name` to Reset Rotation to 0° and Reset Offset to Centre.
- About dialog: icons on Copy path, Export, Import, Website, GitHub, Open README; Reticle mark on the header; lock icon on the privacy heading. Layout unchanged. Input dialog Save: save icon.
- Added `Core/JtIcons.cs` (`Get`/`Img`/`Menu`/`Label`/`KeyForLabel`, `JtIconConverter`, `JtLabelIconConverter`; WinForms-safe aliases) and `JtBtnIcon`/`JtSoloIcon`/`JtInlineIcon`/`JtMenuIcon`/`JtHeaderIcon` image styles plus `JtLabelItemTemplate` (disabled = 40% opacity). The implicit `MenuItem` template sizes the icon slot to 16px and collapses it when there is no icon.
- Empty-preset tooltip no longer points at the removed "💾" glyph.
- Unchanged by design: "◑ Theme" and "⊞ {Name} ({scale}×)" stay text (suite standard); theme picker; tray menu (WinForms); the overlay window and preview (they draw the user's reticle); status toasts; nudge / stepper / move-up-down / preview-background glyphs; Donate / Venmo / Cash App; colour swatches (the user's colour).

### Known open items (not changed this pass)
- Hotkeys "Reset All to Defaults" is a FlatButton with no confirmation and clears every hotkey instead of restoring defaults.
- `BtnResetOffset_Click` does not call `PushUndo` (Reset Rotation does).
- Contrast: Dark `BrushHint` (#666666 on #1C1C1C, 2.97:1) on the About labels; empty preset slots at Opacity 0.40; Light PrimaryButton text 5.27:1.
- Theme button ToolTip still says "Toggle dark / light theme"; preview-background buttons don't show the active one; Settings Close has no Name; Load / Save As… / Browse… have no ToolTip.

## [Unreleased] – 2026-09-15

Fixes from `ReticleOverlay_CodeSecA11y_Review_2026-09-14.md`. Source-level changes only in the session that made them (no `dotnet` toolchain available) — pending a local `build.bat` run, which also assigns the real version number for this entry.

### Security
- **HIGH: UNC-path / NTLM-leak via imported profile.** A malicious/shared `profiles.json` export could set a reticle's `CustomImagePath` to a UNC path (`\\attacker-host\share\x.png`); merely importing that file reached `File.Exists()`/`BitmapImage`, triggering an outbound SMB connection and, by default, an NTLM authentication attempt against the named host. Added `Core/ImagePathValidator.cs` and applied it at both the point of use (`ReticleRenderer.DrawCustomImage`) and at import time (`AboutDialog.BtnImport_Click`, which now sanitizes — clears, doesn't reject the whole import — any unsafe path before merging).

### Fixed
- `ProfileStore.Save()` and `WindowSizeCache.Set()` were still using non-atomic `File.Copy`+`Delete` / direct `File.WriteAllText` — both now use the tmp-file + `File.Move(overwrite:true)` pattern already applied to `AppPrefs.Save()` in the prior cycle.
- "UI Size" button never actually used the suite-mandated `⊞ {Name} ({scale:0.0#}×)` format — both `BtnUiScale_Click` and the `ApplyUiScale` fallback path printed just the label with no scale suffix. Fixed both.
- About dialog's resize grip was decorative only (`IsHitTestVisible="False"`, no drag/keyboard handlers) — a WCAG 2.5.7 gap. Replaced with the canonical accessible Canvas+Polygon grip (same pattern as FileCopy's dialogs); `ResizeMode` changed from `CanResizeWithGrip` to `CanResize` to stop the native grip doubling up with it.
- Theme picker's "active theme" indicator was baked into `MenuItem.Header` text as a `"✓ "` prefix instead of using `IsCheckable`/`IsChecked` — screen readers now get the standard `ToggleState` announcement.
- Dead code: unused `_themeIcons` 19-entry glyph array (confirmed zero call sites) deleted.
- `BtnSavePreset_Click`'s dynamically-built "Save to Preset N" menu items now carry explicit `AutomationProperties.Name`, matching the app's otherwise near-universal explicit-naming convention.

### Deferred (not this pass)
- No MVVM separation in `MainWindow.xaml.cs`/partials — consistent with the rest of the suite, not a regression; a multi-session architectural change, not attempted alongside a bug-fix pass.
- Triple-maintained theme list (`Themes/*.xaml` + `App.xaml.cs` ThemeNames + `MainWindow.xaml.cs`'s `cats` table) — flagged as fragile but all ~107 themes currently line up; deferred as a refactor-risk vs. no-active-bug tradeoff.

## [2026.09.08.0001] – 2026-09-08

### Added
- Real per-game auto-switch: each profile now has an optional “Auto-switch .exe” field (Profiles panel); when enabled in Settings, the foreground process is polled every 2s and the matching profile is applied automatically. Closes the gap between the advertised (but previously nonexistent) auto-switching feature and PRIVACY.md/the installer's description of it.
- Overlay now recovers from live display changes: monitor unplug/replug, resolution change, or DPI change while running triggers an automatic re-position/re-clamp instead of leaving the reticle stale until the monitor dropdown is touched by hand.
- ~1 Hz `HWND_TOPMOST` re-assertion timer on the overlay window, so the reticle recovers after exclusive-fullscreen games steal the topmost z-order.
- Basic app-wide unhandled-exception logging (`DispatcherUnhandledException`, `AppDomain.UnhandledException`, `TaskScheduler.UnobservedTaskException`) with a best-effort prefs flush before the process exits; does not suppress the crash, only makes it diagnosable.

### Changed
- Re-derived `BrushMuted` (and `BrushText` in the 10 themes where it was also flagged) across all affected theme files to ≥ 7:1 WCAG AAA against both `BrushBg` and `BrushSurf`, including the four themes that previously failed even AA (`C64`, `Palenight`, `Abyss`, `TerminalRed`).
- Added the missing implicit `ContextMenu`/`MenuItem`/`Separator` style to `App.xaml` so the theme picker and preset-save context menus follow the active theme instead of rendering OS-default chrome.
- `InputDialog` now uses `ResizeMode="CanResizeWithGrip"`, matching every other dialog in the suite.
- Custom PNG reticle images are now decoded at a capped `DecodePixelWidth` (512px) instead of full native resolution.
- `AppPrefs.Save()` now does a true atomic `File.Move` instead of copy-then-delete.

### Fixed
- Preset-slot button tag parsing now uses `int.TryParse` instead of an unguarded `int.Parse`.
- The extracted tray icon handle is now explicitly disposed, closing a one-time handle leak.

### Removed
- Dead `App.xaml.cs` `DisplayName()` theme-name switch (never called; the theme picker's own name table was already the single source of truth).

---

## [2026.09.01.001] – 2026-09-01

### Fixed
- Uninstaller autorun cleanup deleted the wrong registry value name (`“Reticle Overlay”` vs. the app's actual `AppKey` of `“ReticleOverlay”`), so the Run entry silently survived every uninstall. Added `APP_RUN_VALUE` matching `AppKey`.
- Profile Import previously did a full `File.Copy` replace despite the dialog saying it would “merge” — silently dropped every existing profile not present in the imported file. Implemented a real by-name merge (existing untouched, same-name overwritten, new-named appended) with an accurate “N added, M updated” completion message.
- `build.bat`'s build-number parser assumed a fixed 3-digit segment and mis-computed the next build number once `version.txt` held 4 digits (would have jumped from build 2 to 9003 on the next same-day rebuild). Replaced the parse and the zero-pad with digit-width-agnostic PowerShell one-liners.
- Fixed a doubled resize-grip visual on `MainWindow.xaml` (both a native `CanResizeWithGrip` chrome grip and a hand-drawn keyboard-accessible corner glyph) — switched to `ResizeMode="CanResize"`, kept the functional custom glyph.

### Changed
- Ported the suite's `VersionInfo` helper into `Core/VersionInfo.cs` and rewired `AboutDialog.Version` to it, replacing an ad-hoc `FileVersionInfo`/`Assembly.GetName().Version` lookup.
- Removed `signtool` invocation from `build.bat` (no code-signing certificate exists or is planned for this app); builds are unsigned by design.

---

## [2026.05.25.002] – 2026-05-25

### Fixed
- **CRITICAL (WCAG AAA)** — Light theme's `FocusRingBrush` (`#996600` on `#FFFFFF` ≈ 5.9:1) failed the 7:1 AAA contrast target flagged in the 2026-05-09 compliance audit. Changed to a verified-AAA blue (`#0040CC` on `#F5F5F5` = 7.3:1), documented inline in `Themes/Light.xaml`.

---

---

## [2026.4.12.001] – 2026-04-12

### Added
- Initial release
- Crosshair / reticle overlay with per-game profiles
- Global hotkeys: toggle, profile cycle, opacity nudge, offset nudge
- Layer 2 overlay support (independent shape, size, color, opacity)
- 5 quick-access preset slots
- 8-color palette row with left-click apply / right-click save
- Undo / Redo (Ctrl+Z / Ctrl+Y) for all slider and color changes
- Custom PNG reticle support
- OBS / capture hide (SetWindowDisplayAffinity, Win 10 2004+)
- Multi-monitor support with per-monitor DPI awareness
- Dark / Light theme toggle
- System tray operation with minimize-to-tray on close
- WCAG AAA contrast, 44 px touch targets, gold/cyan focus rings

---

<!-- Add new entries above this line in the format:

## [yyyy.mm.dd.xxx] – yyyy-mm-dd

### Added
### Changed
### Fixed
### Removed

-->
