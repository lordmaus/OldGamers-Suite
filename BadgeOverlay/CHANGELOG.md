# Changelog

All notable changes to Badge Overlay are documented here.
Format based on [Keep a Changelog](https://keepachangelog.com/); versioning follows `YYYY.MM.DD.BUILD`.

## [Unreleased] – 2026-10-14 · One log, shared helpers (S-05 / S-07)

### Changed
- **One log file: `%APPDATA%\BadgeOverlay\logs\BadgeOverlay.log`** (rotated at 512 KB to `.old`; About → View log). Replaces crash.log. Errors now include the full stack trace; your Windows profile path is written as `%USERPROFILE%`; repeated identical entries are merged and a log flood is capped. Old log files are left in place and can be deleted.
- `window-sizes.json` writes are atomic (shared `WindowSizeStore`).
- Log viewer: "View crash log" → "View log".

## [Unreleased] – 2026-10-14 · One version mechanism (S-03)

### Changed
- **Version comes from `version.txt` only.** `build.bat` bumps it (`..\Tools\bump_version.cmd`, local date, `yyyy.MM.dd.NNNN`); `..\Directory.Build.props` stamps the exe from the same file and `installer.nsi` reads it through `..\Tools\version.nsh`. The csproj and installer.nsi no longer hold version numbers, so a build only changes `version.txt`.
- IDE, `dotnet build` and CI builds read the version without bumping it.
- **Fixed:** the exe version could differ from the `Builds\<version>` folder (the old MSBuild counter was applied after the version was computed), and the installer's numeric version had a wrong build part (`9008` instead of `8`).
- Removed the `BumpBuildCounter` MSBuild target.

## [Unreleased] – 2026-10-13 · Suite shared library

Source-level change (not build-verified in the session that made it). Suite repo: `OldGamers.Suite.slnx`.

### Changed
- **Themes come from the suite set** (`..\Shared\Themes`, 108 themes, linked into the app). Dark / Light now use the higher-contrast NoteStation/TimerStudio values (`#000000` / `#FFFFFF` base); other themes differ only slightly.
- **Dialogs follow the app theme**: message and confirm boxes use the themed `AppDialog` (theme colours, app font, 44 px buttons, focus on the safe default, Esc, Ctrl+C) instead of the unthemed Windows box. Startup / crash messages keep the Windows box.
- **UI font setting applies everywhere**: hard-coded `Segoe UI` in XAML replaced with the `AppFontFamily` token; monospace text uses `MonoFontFamily` (Cascadia Mono, Consolas) — "Segoe UI Mono" is not a Windows font.
- Resize grip, version display, keyboard window mover, type ramp now come from the shared library `OldGamers.Wpf.Shared` (same behaviour).

## [2026.10.12.0001] — 2026-10-12 — Skills Review fixes (BO-01…BO-12)

Source: `Claude outputs/BadgeOverlay_SkillsReview_2026-10-11.md` + `BadgeOverlay_SkillsReview_FixPlan_2026-10-12.md` (mockup `BadgeOverlay_SkillsReview_Mockup_2026-10-12.html`).
**Not build-verified** — no `dotnet` in the editing session; run `build.bat` (`version.txt` set to `2026.10.12.0000`, so a same-day build stamps `.0001`) and `dotnet test ..\BadgeOverlay.Tests`. Theme check: 0 failures / 0 missing keys (107 themes).

### Added
- **Settings → Badge → Badge style: Dark / Light / Match app theme** (BO-05). Badge, Info Overlay and the settings preview now use `Overlay*` resources from `Core/OverlayPalette.cs` instead of hard-coded colours. **Windows High Contrast always takes over** (system colours; per-category label colours replaced by window text), as do the app's HC themes. Default = Dark (unchanged look). On Light / Match theme, a category label colour below 4.5:1 on the badge background falls back to the text colour.
- **"Can't read this app" warning** in the Info Overlay (BO-02): ~1 s of failed reads (MSAA `E_ACCESSDENIED`, usually an app running as administrator) shows a warning strip with the fix, spoken once in Screen Reader Mode; clears on the next readable control. Failures are logged at most every 10 min.
- Second launch brings the running copy's window forward (named pipe, current user only) instead of an "Already running" box (box kept as fallback).

### Fixed
- **BO-03** Badge category line 10 px at 60 % white (≈ 6.5:1) → 12 px `FontSizeCaption`, full opacity, ≥ 7:1. Info Overlay category / meta / size label no longer use 70–80 % opacity.
- **BO-01** About → Reset all: no 1 s UI-thread sleep; `App.Restart()` passes `--restart <pid>` and the new process waits for the old one to exit. **Also fixed:** the old process's closing save could write the previous settings straight back after Reset moved `settings.json` aside (Reset may have silently not reset).
- **BO-12** Unhandled UI exceptions no longer trigger a settings save (could persist half-applied settings); the overlay keeps running (decision 2026-10-12).
- Crash-log window "Open folder" passes the path via `ArgumentList` (no hand-built quoting).

### Build / hygiene
- **BO-04** `AllowUnsafeBlocks` removed (no `unsafe` code); `DebugType=portable` — `build.bat` stages `BadgeOverlay.pdb` in `Builds\<ver>\` (not in the installer).
- **BO-10** Deleted `Tools/__pycache__/`, `_build_log.txt`, `_build_done.txt`, root `BadgeOverlay_Setup.exe` (duplicate of `Builds\2026.09.25.0007\`). `.gitignore` += those; `build.bat` / `_run_build.bat` now *move* the installer and log into `Builds\<ver>\`.
- **BO-11** csproj prose trimmed to one-line comments. **BO-06** GC tuning kept until measured (decision 2026-10-12).
- Tests: `..\BadgeOverlay.Tests` (xUnit v3) — contrast math, overlay palette ≥ 7:1 pairs, HC theme keys, `--restart` parsing, `BadgeStyle` JSON round-trip.

## [2026.09.25.0005] — 2026-09-25

WCAG 2.2 AAA remediation (suite plan `Suite_WCAG_AAA_Fix_Plan_2026-09-25.md` §3). Not build-verified in the editing session — run `build.bat`.

### Fixed
- **Theme data now meets the extended AAA contract on all 107 themes** (`Tools/check_theme_contrast.py --app-dir . --fix`, 0 failures / 0 missing keys): `BrushHint` raised 4.5→7:1, Muted/Text ≥7:1 on `BrushSurf2`/`BrushBar`, and new tokens `BrushDivider`, `BrushRedText`/`BlueText`/`GreenText`/`AmberText`, `BrushOnAccent` generated per theme. Where no text colour could clear 7:1 on a fill, the fill moved instead (`BrushRed` in 72 themes, `BrushBlue` in 23). Ivory `BrushBgForChecked` 2.98→≥3:1 on `BrushBlue`.
- **Accent colours used as text (36 sites) now use the `*Text` tokens:** 18 Cheat-sheet keycaps + title, Hotkeys keycaps and capture buttons, About/Badge Colours/Custom Rules/Startup/Welcome titles, Welcome closing line, preset ◆ markers, Startup status line (`BrushGreenText`, now also a polite live region), section labels, help "?" buttons.
- **Text on accent fills uses `BrushOnAccent`** (Primary/Danger buttons were `BrushBg`, active preset was `BrushBgForChecked` ≈3:1).
- **Overlay ON/OFF toggle:** ON drew light text on a light `BrushGreen` fill (~1.6:1); now `BrushSurf` + `BrushGreenText` outline/glyph/label. Hover no longer dims.
- **Badge preview** text on the fixed near-black preview badge is now white (was theme text — dark-on-black in light themes).
- **Unstyled buttons** (Crash log ×5, Hotkeys rows) fell back to Aero chrome: added an implicit `Button` style BasedOn `FlatButton`; Hotkeys capture/clear buttons now use `FlatButton` instead of `Style = null`.
- **Hover/press no longer dims content** (FlatButton/IconButton/chip): hover = `BrushText` outline, press = 0.97 scale. `FlatButton` now honours `BorderBrush`.
- **Opacity-dimmed text** (0.55–0.6 opacity captions in Custom Rules, Badge Colours, Startup Settings) → `BrushMuted` at full opacity.
- **Custom Rules list:** implicit `ListBox`/`ListBoxItem` style (hover outline, selected = Surf2 + 4px `BrushBlueText` bar, 2px focus ring, 44px rows) replaces the local template that used `BrushHint` as a hover fill.

### Changed
- **44×44 targets:** MenuItem 28→44, ComboBoxItem 32→44, About path/Export/Import/Reset MinWidth 0→44, Badge Colours footer + preset combo + Pick… 36/28→44, Crash log 34→44, Custom Rules ×6, Startup ×5, Welcome Skip 36→44, Info overlay A-/A+/✕ 24×22→44×44, Info overlay resize thumb 28→44.
- **Resize grips:** the 8 `Thumb` dialog grips and the MainWindow Canvas grip are replaced by the suite `Controls/AccessibleResizeGrip` (44px, focusable, arrows 10px / Shift 1px, 2px focus ring, `BrushMuted` glyph at full opacity, UIA Thumb "Resize window"). Footers got right padding so the grip never covers a button. Old `ResizeGrip_DragDelta` / `ResizeHandle_*` handlers removed.
- **Info overlay keyboard move/resize:** `Controls/KeyboardWindowMover` — Alt+Arrow moves (Alt+Shift 1px) from any focused control, Ctrl+Alt+Arrow resizes, title-bar context menu (right-click / Shift+F10) has "Move/Resize with keyboard". Cheat sheet lists both. Title-bar buttons use a fixed-colour local style on the fixed dark panel (`#F2FFFFFF` text, gold ring); min size 260×140.
- **Accessible names:** Custom Rules list, Match type, Match value, Display label, Category; Startup "Default badge distance" slider; Info overlay "Decrease text size" / "Increase text size" / "Close".
- **Headings:** new `SectionHeader` style (SectionLabel look + HeadingLevel 2) on 12 section labels; Level 1 on 11 dialog/page titles (incl. Info overlay); Level 2 on OVERLAY, Donate, Privacy, Fixed shortcuts. Custom Rules column captions stay non-heading `SectionLabel`.
- **Type ramp:** `FontSizeCaption` 12 / `FontSizeBody` 14 / `FontSizeTitle` 18 (App.xaml) + `Controls/TypeRamp`; 80 XAML and 4 code font sizes under 12px → Caption. User-chosen badge sizes (`BadgeFontBase`, OverlayWindow) unchanged.
- **Version file** is now the suite dotted form `yyyy.MM.dd.NNNN` (was `"2026.09.25 0004"`); `BumpBuildCounter` (csproj), `installer.nsi`, `build.bat` and `_run_build.bat` parse it (csproj still accepts the old space form).
- `Tools/check_theme_contrast.py`: `Themes/_*.xaml` are style modules — excluded from theme checks, included in the key scan.

### Known open items
- Not build-verified; 200% UI-scale pass not run.
- Destructive actions without danger style (About Reset All, Badge Colours Reset, Startup Factory Reset, Hotkeys Reset All, Crash log Clear) — unchanged.
- Info overlay "Resize with keyboard" menu item uses SC_SIZE on a layered (AllowsTransparency) window — verify live; Ctrl+Alt+Arrow resize does not depend on it.
- Hotkeys row brushes are resolved once with `FindResource` (theme switch while the dialog is open keeps old colours).

## [2026.09.24.0001] — 2026-09-24

### Fixed
- **Drop-down text unreadable in dark themes.** The implicit `ComboBox` style (`Themes/_Inputs.xaml`) set only brushes, so WPF kept its default Aero2 template: light closed-box chrome and a white popup that ignore `Background`, with the theme's light `BrushText` drawn on top. Added a full template (themed closed box, popup on `BrushSurf`, 2px `FocusRingBrush` ring, editable-mode support) matching the suite template. `ComboBoxItem` now sets `Foreground`/`FontFamily` and shows selection as `BrushSurf2` + SemiBold + `BrushBlue` edge, so selected text keeps normal-row contrast.

### Changed
- **Full-color suite icons replace emoji / Unicode glyphs on buttons and dialog headers.** New `Theming/ColorIcons.xaml` (27 fixed-color `DrawingImage`s, keys `JtIcon.<Name>`) merged after the theme dictionary, so theme swaps (index 0 only) never remove it and icons look the same in every theme. Same concept = same icon as NoteStation and FileCopy (Hotkeys keyboard, Settings gear, About "?", Save floppy, Export/Import, Clipboard, Open, Refresh, Clear-all broom, Delete, Link, Web, Event log, Lock, Undo for reset-to-defaults). Four drawn for BadgeOverlay: `Swatches` (Badge Colours), `Rules` (Custom Rules), `Bug` (Report a problem), `Overlay` (OVERLAY toggle label).
- Added `Core/JtIcons.cs` (`Get`/`Img`/`Menu`/`Label`, `JtIconConverter`) and `JtBtnIcon`/`JtSoloIcon`/`JtInlineIcon`/`JtMenuIcon`/`JtHeaderIcon` image styles (disabled = 40% opacity). Implicit `MenuItem` template now sizes the icon slot to 16px and collapses it when a menu item has no icon.
- Buttons now use an Image + TextBlock panel; every changed button carries an explicit `AutomationProperties.Name` (added where missing). Icons are never the only cue.
- Welcome tour Next button: forward icon on steps 1–2, complete icon + "Get started" on the last step (the "→" left the label).
- Unchanged by design: "◑ Theme" and "⊞ {Name} ({scale}×)" stay text (suite standard); About dialog layout; tray menu; the overlay badge and info overlay keep their palette-tinted glyphs (color-blind presets keep working); DangerButton on Custom Rules Delete.

### Known open items (not changed this pass)
- Destructive actions without danger style: About Reset All Settings, Badge Colours Reset to Defaults, Startup Factory Reset, Hotkeys Reset All to Defaults, Crash log Clear log.
- Targets under 44px: help "?" 28×28, Crash log buttons 34, several dialog footers 36, Badge Colours Pick…/row reset 28, Info overlay A-/A+/✕ 24–26×22.
- Theme button tooltip still says "13 dark, 6 light"; badge position radios have arrows on 2 of 5.

## [2026.09.14.0001] — 2026-09-14

### Fixed
- **Theme picker / all right-click context menus rendered as OS-default light popup chrome on dark themes.** `App.xaml` had zero `ContextMenu`/`MenuItem` implicit styling — the only app in the suite still missing this fix. Ported the same implicit-style block (background/border/focus-ring/submenu-arrow templates) already applied in TimerStudio/SuperMagnifierLV/ReticleOverlay; confirmed all referenced brush keys (`BrushSurf`, `BrushSurf2`, `BrushText`, `BrushHint`, `BrushDisabled`, `FocusRingBrush`, `AppFontFamily`) already exist in the theme contract, so no new dangling `DynamicResource` risk.
- **Crash log could leak the Windows account name into a public GitHub issue.** `Core/CrashLog.Write(context, Exception)` logged `ex.ToString()` verbatim; stack traces routinely embed `%APPDATA%`-rooted paths, and `AboutWindow`'s "Report Problem" flow explicitly invites users to paste the crash log tail into a new issue. Redaction (`%USERPROFILE%` → literal placeholder) previously existed only as a `StartupSettingsWindow`-local helper covering one call site. Moved it to a public `CrashLog.Redact()` and applied it inside `CrashLog.Write` itself (both overloads), so every one of the ~dozen call sites across the app is covered centrally and no future call site can bypass it. `StartupSettingsWindow` now delegates to `CrashLog.Redact()` instead of keeping its own copy.
- **3 of 9 dialogs didn't close on Escape**, breaking the pattern used everywhere else in the app. Added `IsCancel="True"` to the existing Close button in `BadgeColorsWindow.xaml`, `CustomRulesWindow.xaml`, and `StartupSettingsWindow.xaml` (all three are opened via `ShowDialog()`, so this is safe). Matches the convention already used in `AboutWindow`/`CheatSheetWindow`/`CrashLogWindow`.

### Known open items (not changed this pass)
- MVVM migration is ~5% complete (`ViewModels/MainWindowViewModel.cs` only covers 5 of ~40+ `AppSettings` fields; rest still go through code-behind `LoadSettingsIntoControls`/`PushSettings`). Debt-servicing, no urgency — see review for migration checklist.
- No non-drag alternative yet for overlay repositioning (WCAG 2.2 §2.5.7, Dragging Movements) — backlog, not attempted this pass.

## [2026.09.08.0001] — 2026-09-08

### Fixed
- **"Discard" buttons never got the Danger (red) badge color, unlike "Delete"/"Cancel"/"Remove."** `Core/ButtonDetector.cs`'s keyword list and `Core/UIADetector.cs`'s had drifted apart — the UIA-based classifier already treated "discard" as destructive, the button-text classifier didn't. Replaced both hand-maintained lists with one shared `IsDestructive()` helper (`Core/DetectorLabels.cs`) covering delete/remove/discard/clear all/empty/uninstall/revoke, so the two classifiers can't silently drift apart again.
- **Hovering the selected row in the Custom Rules list hid the selection highlight.** `Windows/CustomRulesWindow.xaml`'s selected+hovered `MultiTrigger` was declared after the plain `IsSelected` trigger and repainted the row with the same background used for a plain hover — while the mouse sat over the selected rule there was no visual difference from just pointing at any other rule. Selected+hovered now keeps the selected background and layers a hover-colored border on top instead.

## [1.0.0] — 2026-05-07

**First stable release.** All Must / Should / Nice / Engineering tiers from
the original accessibility review have been shipped. See the sections below
for the complete change history across all pre-release development.

### Distribution
- **SECURITY.md** — vulnerability disclosure policy, reporting channels,
  response timeline, and scope definition.
- **CONTRIBUTING.md** — stack overview, setup instructions, project layout,
  code-style rules, WCAG AAA checklist, PR checklist, and guidance on
  what is and isn't welcome.
- **NSIS installer changelog (#70)** — welcome screen now lists the
  headline features of this release so users know what they're installing.
- **NSIS LICENSE acceptance (#74)** — installer now shows the full licence
  text (`MUI_PAGE_LICENSE`) with a mandatory checkbox before installation
  proceeds. Standard practice for distributed desktop software.
- **THIRD-PARTY-NOTICES.txt, CONTRIBUTING.md, SECURITY.md** shipped
  alongside the EXE at install time and removed cleanly on uninstall.
- `APP_URL` corrected to `https://github.com/lordmaus/Badge-Overlay`;
  `HelpLink` registry key added pointing to `https://oldgamerssoftware.com/`.

## [2026.05.25.001] — 2026-05-25

### Fixed
- **MsaaDetector wrong DetectionSource** — `DetectionSource.UIA` was set on MSAA results; corrected to `DetectionSource.MSAA`.
- **HotkeysWindow Cancel reverts mutations** — Cancel now snapshots all hotkey fields at window open and restores them on close, so clicking Cancel truly discards any captures or clears.
- **AboutWindow settings path** — path was hardcoded to `%APPDATA%\BadgeOverlay\settings.json`, ignoring portable mode. Now uses `AppSettings.GetDefaultsPath()` which is portable-mode-aware.
- **IsFullscreenAppForeground throttled to 1 Hz** — was called at 30 Hz via `OnTick`, making 5 Win32 calls/frame when `PauseOnFullscreen` is enabled. Result now cached for 1000ms.
- **MsaaDetector off UI thread** — `AccessibleObjectFromPoint` can block 50-500ms on hung COM providers. Query now runs on a background `Task.Run`; the previous result is reused while a fresh query is in flight, so the UI thread is never blocked.
- **InfoOverlay brush allocation** — `UpdateContent` was allocating new `SolidColorBrush` objects on every call. Brushes are now cached and frozen; only recreated when the element's color changes.
- **ButtonDetector ColorConverter hot-path** — `ColorConverter.ConvertFromString` (reflection-based) was called on every color-resolution. Added `ConcurrentDictionary<string, Color>` cache; cost drops to a dictionary lookup after first call per hex string.
- **HotkeysWindow footer buttons 36px → 44px** — three footer buttons had `MinHeight="36"`, failing WCAG 2.5.5 44px target size requirement.
- **App.xaml.cs theme case `GithubLight`** — switch arm was `"GithubLight"` but the key is `"GitHubLight"` (capital H); theme failed to apply silently.
- **`OnUnhandledException` sets `Handled = true`** — without this, WPF re-threw the exception after the handler returned, crashing the process even when the handler had already logged and recovered.
- **UIADetector jitter check Euclidean → Chebyshev** — `dx*dx + dy*dy` is an Euclidean (circular) test; the staleness guard `GetLatestFor` uses Chebyshev (rectangular). Jitter check now uses `Math.Abs(dx) <= JitterPixels && Math.Abs(dy) <= JitterPixels` to match.
- **SectionHelpButton `Cursor="Help"` removed** — `Cursor` property setter in XAML issues `WM_SETCURSOR`, bypassing `CursorPreserver` and resetting YoloMouse/3rd-party cursor hooks on every hover. Cursor removed from the `SectionHelpButton` style.
- **AboutWindow + HotkeysWindow resize grips now functional** — both windows had `<Path IsHitTestVisible="False">` (purely decorative). Replaced with `<Thumb DragDelta>` + `ResizeGrip_DragDelta` handler so the bottom-right grip actually resizes the window.
- **UIADetector battery cache fields now `volatile`** — `_powerCachedBattery` (bool) and `_powerCacheStamp` (now `int`) were plain static fields; marked `volatile` and changed to `int`/`Environment.TickCount` so cross-thread visibility is guaranteed without a lock.
- **Idle-dim sliders missing `LiveSetting="Polite"`** — `SldIdleDimSec` and `SldIdleDimOpacity` were not announcing value changes to screen readers. `AutomationProperties.LiveSetting="Polite"` added to both.
- **AboutWindow `BtnCheatSheet_Click` reloads settings from disk** — used `AppSettings.Load()` on every click, discarding any unsaved in-session changes. Now uses `(Owner as MainWindow)?.Settings` (the live instance).
- **`InfoPanel` public field → property** — `public InfoOverlay InfoPanel;` converted to `public InfoOverlay InfoPanel { get; set; }` for proper encapsulation.
- **ButtonDetector dead ternary in `CheckTitleBarButtons`** — `int closeW = isChrome ? (int)(46 * scale) : (int)(46 * scale);` both branches were identical. Reduced to `int closeW = (int)(46 * scale);`.
- **`GetWindowLong`/`SetWindowLong` return `IntPtr`** — both were declared returning `long` (32-bit on 64-bit Windows). Changed to `EntryPoint = "GetWindowLongPtr"` / `"SetWindowLongPtr"` returning `IntPtr`, and all call sites updated to use `.ToInt64()` / `new IntPtr(...)`.
- **`_previousBadgeHidden` not reset on factory reset** — `ResetToFactoryPublic()` created a new `AppSettings` but left `_previousBadgeHidden` at its stale value. Now reset to `false` alongside the settings reset.

## [2026.05.20.006] — 2026-05-20

### Performance
- **`WindowFromPoint` hot-path consolidation** — `OnTick` was calling `WindowFromPoint(pt)` + `GetRootWindow` four separate times per tick (shell detection, process annotation, password guard, background suppression) with identical arguments. Hoisted to a single cached `ptHwnd`/`ptRoot` pair, saving ~90 redundant Win32 syscalls/sec at 30 Hz.

### Fixed
- **Stale neutral badge color after settings change** — `InvalidateBadgeCache()` cleared brush and measure caches but not `_neutralBadge`, which bakes `BadgeColorControl` at construction time. Changing the badge color in settings now correctly rebuilds the neutral "Cursor" pill on the next tick.
- **CS0219 warning** — `fromMsaa` variable in `OverlayWindow.OnTick` was set but never read after `SessionStats` removal. Variable and assignment removed; MSAA fallback path is unchanged.

## [Unreleased]

### Fixed
- **"Discard" buttons never got the Danger badge color** — `ButtonDetector.ClassifyButtonText` already grouped "delete"/"remove"/"discard" text into the `Action` category, but the color-override chain in `BT()` (and its `Theme()` fallback) only special-cased "Delete"/"Cancel"/"Remove" before falling through to the neutral Action color. A "Discard Changes" button was badged the same as "Save" or "Copy". Both danger checks now go through a single `IsDestructive()` helper (see Tooling below).
- **Selected row's highlight disappeared on hover in Custom Rules list** — `CustomRulesWindow`'s `ListBoxItem` template applied its `IsSelected + IsMouseOver` `MultiTrigger` *after* the plain `IsSelected` trigger, so WPF's last-writer-wins ordering repainted the selected row with the same background used for a merely-hovered row — hovering the selected rule made it visually indistinguishable from any other row under the cursor. The combined state now keeps the selected background and layers a hover-colored outline on top instead of overwriting it.
- **`AboutWindow.BtnResetAll_Click` shelled out to `cmd.exe /c timeout`** to delay the post-reset restart. Replaced with a `Thread.Sleep` in-process before `Process.Start`ing the replacement directly — same effect, no visible console flash, one fewer process hop.
- **Dead `hadPending` local in `MainWindow.TrySaveSettings`** — computed but never read; removed (the unconditional `_s.Save()` immediately below covers both cases the comment described).

### Accessibility
- **`BrushMuted` and `BrushHint` now held to a documented, enforced contrast bar** — `Themes_Reference.md`'s AAA rule previously covered only `BrushText`/`FocusRingBrush`, leaving `BrushMuted` (real secondary/label text) and `BrushHint` (real `IconButton`/`AboutWindow` text, not just placeholder styling) unconstrained. Audited all 110 `Themes/*.xaml` files against a new bar — `BrushMuted` ≥ 7:1, `BrushHint` ≥ 4.5:1, `FocusRingBrush` ≥ 7:1 — against **both** `BrushBg` and `BrushSurf` (previously only `BrushBg` was checked, which is how `RosePineDawn`'s focus ring reached 2.79:1 on `BrushSurf` without tripping anything). 154 findings across 84 theme files corrected by shifting the failing brush's lightness toward black/white in HSL space, hue and saturation preserved; 9 theme files also had a stale hex/ratio value baked into an adjacent XAML comment, corrected to match. All 110 themes now pass with zero findings.

### Tooling
- **`Tools/check_theme_contrast.py`** — standalone WCAG contrast auditor for `Themes/*.xaml` (no project reference, pure stdlib). `python3 Tools/check_theme_contrast.py` reports failures against the `Themes_Reference.md` contract; `--fix` rewrites failing brushes in place and re-verifies. Prevents the `BrushMuted`/`BrushHint` drift found in this cycle from recurring, and lets a future theme #111 be checked before it ships.
- **`DetectorLabels.DestructiveKeywords`** — `ButtonDetector` and `UIADetector` each hand-maintained their own "is this label destructive" keyword list, and they had drifted apart (see the Discard fix above). Both now read one shared array.

### Fixed
- **Multi-monitor badge tracking** — badge position was computed in raw screen DIPs and passed directly to `Canvas.SetLeft/Top`, which expects canvas-local coordinates. When a secondary monitor sits to the LEFT of the primary (`VirtualScreenLeft < 0`), the badge was displaced by `|VirtualScreenLeft|` pixels relative to the cursor. `PositionBadge` now converts cursor screen DIPs to canvas coords (`dipX − VirtualScreenLeft`) before all quadrant and clamp logic. Single-monitor setups and secondary-right configurations are unaffected (offset is zero).
- **`PositionBadgePinned` default position DPI arithmetic** — fallback position used `WorkArea.Right × _dpiScaleX` where `WorkArea.Right` is already in WPF DIPs. Multiplying by `_dpiScaleX` (< 1.0 at high DPI) produced a position far inside the screen. Corrected to use `WorkArea.Right` directly, then subtract the canvas origin.
- **`UpdateDpiFromCursorMonitor` division-by-zero guard** — `GetDpiForMonitor` could return `S_OK` with `dpiX = 0` on certain virtual display drivers, causing `_dpiScaleX = 96.0 / 0 = Infinity` and teleporting the badge off-screen. Added `&& dpiX > 0 && dpiY > 0` guard before the division.
- **High-Contrast theme restore loses specific theme name** — `App.SetTheme(string)` was calling `_hcMonitor?.Sync(IsDark ? "Dark" : "Light")`, discarding the full theme key. When Windows High-Contrast turned off, `HighContrastMonitor` restored only `"Dark"` or `"Light"` instead of e.g. `"TokyoNight"`. `Sync` is now called with `themeName` directly so the exact user-selected theme is recovered on HC exit.

### Polish & professionalism (items 17–27)
- **Title Case consistency (#18)** — "Reset all settings" → "Reset All Settings" on the About dialog action button. All action buttons now use consistent Microsoft Style Guide Title Case.
- **Tooltip vs HelpText separation (#19)** — three cases where ToolTip and AutomationProperties.HelpText were identical (Stats button, Info Overlay checkbox, Hide Panel button). ToolTip is now a short visual hint; HelpText is the full screen-reader description. Eliminates redundant double-reads in NVDA / JAWS.
- **Tab order (#20)** — `KeyboardNavigation.TabNavigation="Local"` added to the Screen Reader Mode sub-panel so Tab navigates within the trigger radio group before leaving the section.
- **Settings breadcrumbs (#21)** — "Badge Overlay › Settings" and "Badge Overlay › Keyboard Shortcuts" breadcrumb added to the StartupSettings and HotkeysWindow headers. StartupSettings window title updated from "Settings" to "Startup Settings — Badge Overlay" so it's identifiable in the Windows taskbar and Alt-Tab switcher.
- **CrashLog age-based rotation (#22)** — existing size-based rotation (512 KB) supplemented with 30-day age rotation. Files older than 30 days are archived to `.old` on the next write. Prevents multi-year crash logs accumulating silently on rarely-crashing machines.
- **THIRD-PARTY-NOTICES.txt (#23)** — new file at repo root listing all third-party components with their licences: Catppuccin Mocha palette (MIT), System.Speech 9.0.0 (MIT), Wong 2011 colour-blind palette (academic), IBM Carbon palette subset (Apache 2.0), .NET/WPF (MIT). "📄 Third-Party Notices" button added to About → Links row; opens the file from the install directory in the default text viewer.
- **Tray menu shortcut indicators (#24)** — Show Panel tray item shows "Double-click" as its shortcut. The Pause/Resume overlay item reads the currently registered hotkey chord from settings and surfaces it as `ShortcutKeyDisplayString` so the user can learn the hotkey from the tray without opening the Hotkeys dialog.
- **InfoOverlay resize grip discoverability (#25)** — hit area increased 18→28 px. Added `ToolTip="Drag to resize this panel"` and full `AutomationProperties.Name`/`HelpText` so the resize affordance is announced by screen readers and visible to new users who don't know borderless windows can be dragged.
- **Welcome window Skip accessibility (#26)** — replaced `Hyperlink` (not a Tab stop; no focus ring; no keyboard activation) with a `FlatButton` styled with muted foreground. Visually subordinate to the primary "Next → / Get started" but now keyboard-reachable, Tab-stop, and 36 px minimum height. Added ToolTip explaining the "replay from About" recovery path.
- **Privacy / no-telemetry notice (#27)** — About's privacy section redesigned from a small bordered card to a blue-accent panel with a clear "Privacy — what this app does NOT do" heading. Explicit bullet-separated list: no network calls, no telemetry, no analytics, no mic, no camera, no clipboard reads, no data outside `%APPDATA%`. Clarifies that the only outbound activity is user-initiated link clicks. Makes the privacy commitment visible to users who scan About before installing.


### Fixed (industry-standards review)
- **`_previousBadgeHidden` cache leak** when toggling "Also hide the visual badge" multiple times during an SR-Mode session. Cache is now captured exactly once on SR-Mode entry; sub-checkbox flips no longer overwrite it.
- **HotkeysWindow Enable/Disable column** — added a per-row "On" checkbox. Capturing a chord automatically enables the binding (current behaviour), but users can now save-but-disable a chord without clearing it.
- **Speech mute state leaking across SR-Mode toggles** — new `OverlayWindow.ResetMuteState()` is called whenever Screen Reader Mode is toggled, scrubbing `_speechMuted` and dwell-tracker state so a stale mute from a previous session can't appear as "speech is broken".
- **`EnsureAnnouncer` race** — double-checked locking around the lazy SAPI synthesizer instantiation. Prevents handle leaks if two ticks reach the call concurrently during DPI-change recompose.
- **Idle-dim suppresses dwell-fire** — when the badge is dimmed because the cursor's been still long enough, dwell-mode speech no longer fires. The user's "I'm not interacting" intent is honoured uniformly.
- **Stats "Average rate" excludes paused time** — new `SessionStats.ActiveMs` accumulates per-tick deltas only when detection actually ran. Stats window divides by active time, not wall-clock uptime; long-paused sessions no longer report misleadingly low rates. Window now shows both "Active" and "(session: …)" separately.
- **Audit export targets the right window** — added `A11yAuditor.AuditWindow(IntPtr)` overload. Countdown UI now shows a live foreground-window preview every 200 ms so users see exactly what will be audited; the captured handle is used at countdown end so a transient notification toast can't hijack the target.

### Improved (WCAG conformance pass)
- **2.5.5 Target Size (Enhanced)** — bumped CheckBox and RadioButton min-height 36→44 px; HotkeysWindow capture/clear/enable buttons now 44 px. Visual sizing of the indicators is unchanged (16 × 16 check, 18 × 18 radio); padding pads up the click target.
- **2.4.7 Focus Visible** — added ComboBox + ComboBoxItem styles with the AAA `FocusRingBrush` (cyan dark / amber light), 2 px stroke + drop-shadow halo. Default WPF ComboBox dotted-rectangle focus failed 1.4.11 Non-text Contrast; replaced.
- **3.3.5 Help (AAA)** — added `SectionHelpButton` style (28 × 28 visual, 44 × 44 click target) and a "?" button on the Identity row. Reachable by Tab; opens the keyboard-shortcut cheat-sheet. Same target as F1 but discoverable for users who don't know F1 is bound.
- **2.3.3 Animation from Interactions (AAA)** — theme cross-fade now honours both `SystemParameters.ClientAreaAnimation` AND the per-app `ReduceMotion` preference (either off skips the animation). Public `MainWindow.Settings` accessor added so infrastructure code can query user preferences without tight coupling.
- **1.3.1 Info and Relationships** — radio groups (Speech trigger, Detection granularity) wrapped in labelled GroupBoxes with `AutomationProperties.Name` so screen readers announce "When to speak, group" before each radio. Added flat GroupBox style that strips the visual border but keeps the UIA Group semantics + Header label.
- **1.4.13 Content on Hover or Focus** — InfoOverlay's existing Escape-to-dismiss verified.
- **2.1.4 Character Key Shortcuts** — verified WPF's underscore mnemonics require Alt; underscore visual is hidden by default until Alt is pressed. No remediation needed.
- **4.1.3 Status Messages** — Stats "Active for" / "Total detections" already have `LiveSetting=Polite`. Verified intentional that per-source counters do NOT — firing 6 simultaneous live-region updates per second would flood screen readers.

### Public API additions
- `AppSettings.IsPortableMode` already existed; companion `MainWindow.Settings` (read-only) now exposes the live settings object for cross-class queries.
- `OverlayWindow.ResetMuteState()` — called from MainWindow on SR-Mode toggle.
- `A11yAuditor.AuditWindow(IntPtr)` — explicit-handle overload alongside `AuditForegroundWindow()`.
- `SessionStats.ActiveMs` field + `NoteActive(long)` accumulator — wired from `OverlayWindow.OnTick`.


### Added — accessibility & user-facing features
- **Idle dim (#62)** — opt-in fade of the badge after a stationary cursor period. Three settings: `IdleDimEnabled` (default off), `IdleDimSeconds` (1–600, default 30), `IdleDimOpacity` (0.05–1.0, default 0.30). Detection still runs while dimmed; cursor movement restores full opacity instantly. Honoured by all three Opacity restore points (move, topmost recovery, visibility flip) so the dim doesn't bounce when the badge re-shows after pause/silent. UI: checkbox + two sliders with live percentage / seconds labels in MainWindow.
- **Screen Reader Mode (#65)** — full screen-reader workflow toggle. Suppresses the visual badge entirely (auto-flips `BadgeHidden`, remembering the prior choice for restore) and **speaks every detected element label aloud through the operating system's text-to-speech engine** (System.Speech / SAPI). Works whether or not NVDA, JAWS, or Narrator is running — direct OS-level TTS bypasses the screen-reader stack entirely. Cancels any in-flight speech on each new detection so fast cursor movement doesn't build a backlog of pending phrases. Same-label suppression prevents repeated identical announcements.
  - **Two trigger modes** (radio group): **Immediate** (speak on every label change) or **Dwell** (speak only after the cursor has rested on the same element for 1–10 seconds, default 3).
  - **Toggle Speech On/Off hotkey** — runtime mute switch over the persisted Screen Reader Mode preference. Default Ctrl+Shift+S, off until enabled. Mutes / unmutes speech without changing the configured trigger mode or dwell time. Useful for silencing speech during meetings or phone calls. Audio confirmation (Asterisk on, Hand off) on every toggle. Mute state is session-scoped — every launch starts unmuted; the persisted Screen Reader Mode checkbox remains the "do I want speech at all" preference.
  - **Sub-checkbox to keep the visual badge visible** alongside speech (`SpeechSuppressesBadge`, default true for backward compatibility). Lets partial-vision users get both the badge and audio confirmation simultaneously. Live-applied — toggling the sub-checkbox while SR mode is on immediately updates badge visibility.
  - **Configure speech-toggle hotkey…** button in the SR mode panel deep-links to HotkeysWindow.
- **Session Statistics dashboard (#46)** — About → Help row → "Session statistics" opens a live read-out of detection counters refreshed at 1 Hz. Shows total, per-source breakdown (Win32 / UIA / MSAA / Custom rule), distinct labels seen, average rate, and uptime. "Copy to Clipboard" button dumps a plain-text summary. All data in-memory only, never written to disk, never transmitted.
- **Accessibility audit export (#57)** — About → Help row → "Run accessibility audit on foreground window" performs a UIA tree walk of the foreground window after a 3-second countdown (giving you time to switch focus), checks structural WCAG 2.1 criteria (1.3.1 Info & Relationships, 2.1.1 Keyboard, 4.1.2 Name / Role / Value), and saves a self-contained HTML report. Includes scope disclaimer — does NOT cover colour contrast, animation, focus order, or content-meaning concerns. Walk capped at 5000 elements with truncation flagged in the report. Output opens in default browser. No data leaves the machine.
- **Portable mode (#71)** — opt-in via Startup Settings. When the `portable.flag` marker file exists next to the EXE, settings.json is written there instead of `%APPDATA%\BadgeOverlay\`. Useful for USB-stick installs and locked-down corporate machines. UI toggle creates / deletes the marker; restart required for the new path to take effect (notice surfaced in the dialog). `AppSettings.IsPortableMode` and `AppSettings.SetPortableMode(bool)` exposed for any future tooling.

### Engineering
- **Per-source detection counters** — Win32, UIA, MSAA, Custom rule. Wired through OverlayWindow detection block via `fromWin32`/`fromMsaa` flags, surfaced in the new Stats dashboard.
- **`SessionStats` expanded** — added `DetectionsWin32`, `DetectionsUIA`, `DetectionsMSAA`, `DetectionsCustomRule`, plus a 4096-capped distinct-label HashSet. All counters use `Interlocked` for thread-safe writes from the detection loop.

### Documentation
- **`docs/ICON_AUDIT.md`** added (#69). Verifies `app.ico` ships 9 sizes (16/20/24/32/40/48/64/128/256) covering Microsoft minimum + HiDPI extras. Notes 96 px as the only missing nice-to-have. Includes regeneration command.


### Refactored (engineering — no behaviour change)
- **`NativeMethods.cs` split** into `Native/Common.cs` + `User32.cs` + `Kernel32.cs` + `Shcore.cs`. Same `partial class NativeMethods` declared in each file, so existing call sites (`NativeMethods.GetClass`, `NativeMethods.SWP_NOSIZE`, etc.) continue to compile unchanged. Originally 402 lines in one file; now organised by API surface for easier maintenance.
- **`AppSettings.cs` split** into four partial files in `Settings/`: `AppSettings.Badge.cs` (appearance, colours, pinning), `AppSettings.Behaviour.cs` (flags, accessibility prefs, detection policy, InfoOverlay content, startup), `AppSettings.Hotkeys.cs` (Toggle/Freeze/CopyLabel bindings), `AppSettings.Window.cs` (geometry, custom rules). Main file shrunk 499 → 330 lines and now holds only schema versioning, JSON source-gen registrations, `SanitiseDoubles`, `Load`, `Save`, and `PushToDetector`. JSON shape unchanged — settings.json from a previous build round-trips identically.
- **`MainWindow.xaml.cs` split** — `Window/MainWindow.Tray.cs` extracts `SetupTray()` and `ExitApp()`; `Window/MainWindow.Hotkeys.cs` extracts `RegisterHotkeys()`, `TryRegister()`, and `PlayStateSound()`. Main partial shrunk 1041 → 831 lines. Field declarations (`_tray`, `_hotkeyToggle`, `_hotkeyFreeze`, `_hotkeyCopyLabel`) stay on the main partial so `DisposeRuntimeResources` doesn't have to coordinate across files.
- **`WinEventHookManager`** added as a central registry for `SetWinEventHook` subscriptions. `OverlayWindow` migrated from raw `SetWinEventHook`/`UnhookWinEvent` calls to `WinEventHookManager.Subscribe()` returning `IDisposable`. `App.OnExit` calls `WinEventHookManager.DisposeAll()` as last-line-of-defence for any subsystem that forgot to dispose. Future hook consumers (e.g. shell-event listeners, focus-change cursor preservers) inherit the keep-alive bookkeeping for free.


### Added
- **Tray context menu** redesigned with proper grouping (action → settings → about → exit) and a state-aware "Pause overlay" / "Resume overlay" toggle that reflects the current state when the menu opens.
- **Live session-stats footer** on MainWindow — shows `<state> · <count> detected · <m:ss>`. Refreshed by a 1 Hz `DispatcherTimer` so the elapsed clock advances even without detection events. `AutomationProperties.LiveSetting="Polite"` for screen-reader awareness.
- **Battery-aware UIA throttle** — `UIADetector` polls at `UiConstants.UiaThrottleBatteryMs` (250 ms) when the OS reports the system is on battery, vs. 150 ms on AC. Power state cached for 5 s; `GetSystemPowerStatus` syscall is cheap but not free. Saves measurable CPU wake-ups on laptops.
- **Audio cue on overlay toggle** (opt-in, off by default). New `AppSettings.SoundFeedbackEnabled` flag; checkbox in main settings panel. Plays `SystemSounds.Asterisk` on enable / `SystemSounds.Hand` on disable so the cue is distinguishable without shipping custom audio assets. Useful for users who can't see the badge state at a glance.
- **Colour-blind palette presets** in Badge Colours dialog — Default, Deuteranopia (Wong 2011), Protanopia, Tritanopia (Carbon-inspired), and Custom (auto-selected when individual swatches are hand-edited). One-click application of a Daltonization-friendly palette that preserves WCAG AAA contrast.

### Changed
- **UI string capitalisation pass** — buttons that perform actions now use Title Case consistently ("Reset to Defaults", "Factory Reset", "Export Settings…", "Import Settings…"). Descriptive checkboxes remain sentence case per Microsoft Style Guide. ASCII `...` replaced with `…` ellipsis on action buttons.

### Deferred
- **Per-application profile system (#47)** — original review noted this as "expand existing". The "existing" was Game Mode's per-game profile mechanism, which has been removed. A general per-app profile system (different badge behaviour per foreground EXE) is a feature-sized effort, not a polish item; queued for a future release alongside community feedback on shape (just colours? size? rules? hotkeys?).

### Added
- **First-run welcome tour** — three-screen orientation (What / How / Help) shown once on first launch. Replayable from About → "Show welcome tour again". Persisted via new `FirstRunWelcomed` flag; only marked complete on explicit "Get started" or "Skip" — accidental Alt+F4 doesn't suppress the tour.
- **F1 cheat-sheet window** — every keyboard shortcut in one window, with global-hotkey rows generated from current `AppSettings` so re-bindings are reflected accurately. Wired via `ApplicationCommands.Help` so screen readers discover the F1 affordance through the automation peer.
- **About → Help** now exposes "Keyboard shortcuts (F1)" and "Show welcome tour again" buttons in addition to "Open README".
- **`UiConstants.cs`** — central registry for WCAG thresholds (7:1 / 4.5:1 / 3:1), touch-target sizes (44 / 32 / 40 px), focus-ring widths, save-debounce intervals, crash-log limits, and tooltip timing. Wired into `MainWindow._saveTimer` and `CrashLog`.
- **`docs/CODE_SIGNING.md`** — code-signing setup guide covering cert types (OV / IV / EV / self-signed), `BO_SIGN_*` env vars, signtool verification, common failure modes, and GitHub Actions integration.
- **README expansion** — feature matrix, screenshots section (placeholder paths), comparison table vs YoloMouse, expanded WCAG 2.2 AAA mapping, documented accessibility deviations, code-signing instructions, privacy section.

### Changed
- **Dialog widths standardised** to 540 / 720 / 960 px tiers across About (540), BadgeColors (540), StartupSettings (540), CustomRules (720), Hotkeys (720), CrashLog (720). Single-column dense dialogs use 540; two-column or list-with-detail dialogs use 720; 960 reserved for future complex dialogs.
- **`App.xaml` split** into module dictionaries — `Themes/_Buttons.xaml` (FlatButton/PrimaryButton/DangerButton/IconButton/ChipTemplate), `Themes/_Inputs.xaml` (CheckBox/Slider/SliderFocusVisual/RadioButton/TextBox), `Themes/_Indicators.xaml` (FrameworkElement text rendering, ToolTip, Separator, SectionLabel). App.xaml is now ~45 lines of MergedDictionaries scaffolding. `App.SetTheme` updated to replace `MergedDictionaries[0]` only — `dict.Clear()` would have wiped the new style modules.
- **`build.bat` code-signing** now accepts both `BO_SIGN_CERT` / `BO_SIGN_PASS` / `BO_SIGN_TIMESTAMP` (documented in README) and the legacy `SIGNTOOL_*` names. Skip-signing path emits a clear pointer to `docs/CODE_SIGNING.md`.

### Fixed
- **Settings save crash on non-finite doubles** — `BadgePinnedX/Y` and `InfoOverlayLeft/Top` defaulted to `double.NaN`, which `JsonSerializer` rejects with `ArgumentException`. Sentinels flipped to `-1`; readers tolerate both `-1` and legacy `NaN` values for backward compatibility.
- **`AppSettings.Save()` defended against NaN/Infinity** in any persisted double — new `SanitiseDoubles()` runs before serialisation. `MainWindow_GeometryChanged` and `TrySaveSettings` gate window geometry on `IsFinite()` so transient WPF layout passes (DWM restart, monitor unplug) can't corrupt settings.
- **Orphaned `.tmp` cleanup** in `AppSettings.Save()` — recovers from prior saves that died between `WriteAllText` and `File.Replace`.
- **Single-instance mutex held across Reset → Restart** — About → Reset now releases the mutex before launching the replacement process, with a 1 s `cmd.exe` delay so the OS can fully tear down before the new instance's mutex check runs.
- **Startup lock-out on init failure** — `MainWindow.OnLoaded` body wrapped in `try/finally`; any exception clears `_loading` so settings handlers and geometry-save remain functional.

### Changed
- **AAA-compliant slider focus ring** — 3 px stroke + drop-shadow halo at 4 px outset. Visible against any track colour (Surface or Surface2 backgrounds).
- **Theme-switch cross-fade** — 60 ms opacity dip during `MergedDictionaries` swap. Skipped when `SystemParameters.ClientAreaAnimation` is false (reduce-motion).
- **Status label live-region** — `AutomationProperties.LiveSetting="Polite"` plus explicit `RaiseAutomationEvent(LiveRegionChanged)` so screen readers announce hotkey-driven state changes (Active / Frozen / Paused).
- **About dialog button heights** raised from 30 px to 32 px for improved pointer affordance.

### Added
- **Always-on badge** following the cursor with a neutral "Cursor" pill when nothing specific is detected.
- **UI Automation enhanced detection** covering Office ribbon, Chromium toolbars, Firefox, WPF/UWP controls, Explorer list items, desktop icons, tree nodes, and system tray items.
- **Icon / image / text-input categories** with 📄 and ✎ indicators.
- **WCAG contrast grading** in the Badge Colours dialog — live AAA / AA / FAIL label per category with descriptive tooltip.
- **High-Contrast mode support** — new `Themes/HighContrast.xaml` bound to `SystemColors.*`; auto-applied when Windows High-Contrast is active.
- **Keyboard drag for Info Overlay** — arrow keys (20 px), Shift+arrows (1 px), Ctrl+arrows (100 px), Ctrl+Home centres, Escape hides, +/- resize font.
- **Accessible names** (`AutomationProperties.Name` + `HelpText`) on all MainWindow controls; sliders expose `LiveSetting="Polite"`.
- **Crash log rate-limiting** — dedup window, 30-writes-per-minute ceiling, coalesced repeat counter.
- **Atomic detector state publish** — `(Settings, Rules)` swap in one `Interlocked.Exchange`, eliminating torn-read windows.
- **`CustomRule.MatchType` enum** — type-safe `MatchKind { Text, Class }` replacing magic strings; JSON back-compat preserved.
- **Info Overlay font size and position** persist across sessions.
- **Window geometry clamping** — restored size/position clamped to the current primary work area so monitor disconnect never leaves the window off-screen.

### Changed
- Detection timer reduced from 120 Hz to 30 Hz to lower the chance of WM_SETCURSOR races with third-party cursor tools.
- UIA worker no longer gates on cursor dwell — queries on every move for snappier detection.
- Duplicate OverlayWindow shutdown path collapsed into a single guarded `OnClosed` override.
- Hotkey disposal moved out of `TrySaveSettings`; hotkey now stays alive when the main window is minimised to tray.
- `CursorPreserver` only intercepts **client-area** `WM_SETCURSOR`; non-client areas (title bar, resize borders) get their normal OS cursors again.

### Fixed
- Custom-cursor schemes (YoloMouse, system cursor schemes) no longer revert to the default Windows arrow when hovering over the badge overlay's own surfaces.
- Taskbar / tray / Start menu / File Explorer icons are detected again — previous shell-surface skip lists removed.
- `SchemaVersion` migration now persists to disk on first run after upgrade.
- `_customRules` enumeration is snapshot-safe — no torn-read on concurrent reload.
- UIA tree walk bounded to 64 visited nodes per query — prevents pathological UIA providers from hanging the detection thread.

## [2026.04.18]

Initial public release under the Badge Overlay name (fork of CursorOverlay).
