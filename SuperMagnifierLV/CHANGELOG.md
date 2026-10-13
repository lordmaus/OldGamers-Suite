# Super Magnifier LV — Changelog

All notable changes to Super Magnifier LV are documented here.
Format based on [Keep a Changelog](https://keepachangelog.com/); versioning follows `yyyy.mm.dd.xxx`.


## [Unreleased] – 2026-10-13 · Suite shared library

Source-level change (not build-verified in the session that made it). Suite repo: `OldGamers.Suite.slnx`.

### Changed
- **Themes come from the suite set** (`..\Shared\Themes`, 108 themes, linked into the app). Dark / Light now use the higher-contrast NoteStation/TimerStudio values (`#000000` / `#FFFFFF` base); other themes differ only slightly.
- **Dialogs follow the app theme**: message and confirm boxes use the themed `AppDialog` (theme colours, app font, 44 px buttons, focus on the safe default, Esc, Ctrl+C) instead of the unthemed Windows box. Startup / crash messages keep the Windows box.
- **UI font setting applies everywhere**: hard-coded `Segoe UI` in XAML replaced with the `AppFontFamily` token; monospace text uses `MonoFontFamily` (Cascadia Mono, Consolas) — "Segoe UI Mono" is not a Windows font.
- **Starting the app again brings the running window forward** (from the tray or minimised) instead of showing "Already running".
- Resize grip, version display now come from the shared library `OldGamers.Wpf.Shared` (same behaviour).

## [2026.10.13.0001] — 2026-10-13 · Skills Review fixes

Source-level change (not build-verified in the session that made it). `version.txt` set to `2026.10.13.0000`; next `build.bat` produces `.0001`.

### Changed
- **Global hotkeys moved to Ctrl+Alt** (SM-12). Old Ctrl-only defaults hijacked Ctrl+F/T/L/P/S/R/0/=/- system-wide. Untouched old defaults migrate automatically (one-time notice); customised bindings are kept. Presets (Ctrl+Alt+1–5 load, Ctrl+Alt+Shift+1–5 save), colour cycle (Ctrl+Alt+C) and lens resize are now remappable. Ctrl+Shift+Arrow resize only acts while Super Magnifier or the lens is the active window.
- Hotkey hints in tooltips, tray menu, lens menu and help text follow the user's bindings.
- Status messages stay ≥ 5 s (+60 ms/char), pause on hover/focus, can be dismissed (✕ / Esc), are announced to screen readers, and the last 20 are under tray → Recent messages. New Settings → Status message duration (5 s / 10 s / until dismissed).
- All 107 themes pass 7:1 for text tokens; accent-coloured text uses `*Text` tokens; small fonts raised to 12 px minimum; 44 px targets for list rows, menu items, lens-panel inputs and resize grips (lens grip 44–64 px).
- Keyboard-accessible resize grip on main window, About, App presets, Hotkeys and input dialogs.
- Validation messages carry a ✗ glyph (not colour alone).

### Fixed
- Presets / app maps saved with comma decimals on de-DE/fr-FR etc. could not be read back (SM-01); now System.Text.Json source-gen with tolerant legacy repair. Preset names with quotes/newlines round-trip (SM-02).
- Culture-sensitive parsing in preset buttons, app rules and lens padding (SM-11).
- Crash handlers log to `crash.log` and save settings on the UI thread (SM-03); About → Open crash log folder.
- Snapshot GDI handles released on failure (SM-07).
- Resize hotkeys were lost after closing the Hotkeys dialog.

### Added
- `SuperMagnifierLV.Tests` (xUnit): culture round-trips, legacy repair, hotkey migration, colour parsing.

## [Unreleased] — 2026-09-25 · Full-color icons

Source-level change only (no `dotnet` toolchain in the session that made it) — pending a local `build.bat` run, which also assigns the version number.

### Changed
- **Full-color suite icons replace emoji / Unicode glyphs on buttons, toggles, dialog headers and the first-run wizard.** New `Theming/ColorIcons.xaml` (41 fixed-color `DrawingImage`s, keys `JtIcon.<Name>`) merged after the theme. `App.ApplyTheme` clears `MergedDictionaries` on every theme change, so it now re-adds the icon dictionary right after the new theme; icons look the same in every theme, including both high-contrast themes. Same concept = same icon as NoteStation, FileCopy, BadgeOverlay and TimerStudio (Hotkeys keyboard, Settings gear, About "?", Save floppy, Export/Import, Clipboard, Web, Link, File, Refresh, Undo for reset, Warning, Lock, Camera, Getting Started flag for welcome / What's New, Template for presets). 12 drawn for SuperMagnifierLV: ClickThrough, CursorFinder, CursorHighlight, FollowCaret, FollowMouse, Freeze, Magnifier, MirrorLens, Reticle, Ruler, ZoomIn, ZoomOut.
- **Enable / Disable Magnifier**: play / stop icon + the state words; the button's `AutomationProperties.Name` now follows the state (was stuck on "Enable Magnifier").
- **Freeze**: snowflake + "Freeze"; while frozen the button shows play + "Resume", uses `PrimaryButton` as its pressed look, and its Name says the lens is frozen (previously the word was dropped and only a bare glyph remained).
- **Zoom in / Zoom out** buttons: lens icons with plus / minus badges (were ▲ / ▼) and ToolTips.
- **Follow mode, Reading Ruler, Highlight Cursor, Cursor Finder, Click-through, Mirror, Reticle**: 16–18px icons beside their labels.
- **Lens presets**: a filled preset shows a 16px follow-mode icon (mouse or caret) next to its name; its Name reads "Preset N, name, follows mouse / text caret". Empty presets keep the number.
- Zoom lock readout: lock icon while locked (the "Unlocked" word is unchanged).
- Colour filters: dropped the 🌅 / 🔵 emoji on Warm Amber and Cool Blue so all nine options read the same.
- Hotkeys and Settings status lines: a warning icon replaces the "⚠" prefix on error messages.
- Added `Core/JtIcons.cs` (`Get`/`Img`/`Menu`/`Label`/`KeyForLabel`, `JtIconConverter`, `JtLabelIconConverter`; WinForms-safe aliases) and `JtBtnIcon`/`JtSoloIcon`/`JtInlineIcon`/`JtMenuIcon`/`JtHeaderIcon` image styles plus `JtLabelItemTemplate` (disabled = 40% opacity). The implicit `MenuItem` template sizes the icon slot to 16px and collapses it when there is no icon.
- Added missing `AutomationProperties.Name` to first-run Back / Next, Hotkeys Reset All, App Auto-Presets Add Rule / Remove.
- First-run tour text no longer points at removed glyphs (▶, ▲ / ▼, ⏹, ⚙, 🔍, 🎹, ☀/🌙).
- Unchanged by design: "◑ Theme" and "⊞ {Name} ({scale}×)" stay text (suite standard); About dialog layout; tray menu (WinForms); lens top bar, lens context menu, mirror lens, reticle, cursor highlight / pulse, reading ruler and title-bar highlighter (they draw over other apps and games); colour swatches (the user's colour).

### Known open items (not changed this pass)
- Destructive actions without danger style: Settings Reset to Defaults, Hotkeys Reset All / clear, App Auto-Presets Remove, Clear Lock.
- Targets under 44px: all Hotkeys dialog buttons (CaptureBtn MinHeight 30; clear 32 wide), first-run Back / Next / Skip; lens bar buttons by design.
- Contrast: first-run header white text on BrushBlue (~1.8:1 in Dark); LensIconButton default BrushHint on the lens bar (~3.2:1 in Dark).
- Colour swatches do not name the current colour; tray preset "[C]" marker is cryptic.

## [2026.09.08.0002] — 2026-09-08

Fixes from `SuperMagnifierLV_Full_Review_2026-09-07.md`'s Critical/High findings. Source-level changes only in the session that made them (no `dotnet` toolchain available) — pending a local build confirmation.

### Fixed
- **Stale virtual-screen-bounds cache on monitor topology change while the lens stayed put.** The cache was only invalidated when the lens itself crossed onto a differently-named monitor (a once-a-second poll) — unplugging or reconfiguring a display elsewhere could leave the lens clamped to now-nonexistent screen area, or unable to reach newly added area, until an unrelated lens move happened to trigger the poll. `MainWindow` now subscribes directly to `Microsoft.Win32.SystemEvents.DisplaySettingsChanged` and invalidates the cache immediately on any display-topology change.
- **`CursorHighlighter`'s overlay window was sized once at construction and never resized.** Adding a monitor after the highlighter started left the cursor ring unable to render on the new screen area. Now resizes from fresh `SystemParameters.VirtualScreen*` values on the same `DisplaySettingsChanged` event, and drops its per-monitor DPI cache at the same time since monitor handles can be reused for a different physical display after a topology change.
- **Crash-on-launch from a hand-edited or corrupted `ThemeName` in settings.** A typo'd or removed theme key built an unresolvable `Uri` and threw during window construction, before any UI was visible — the worst possible failure mode for a low-vision accessibility tool. The new theme dictionary is now built and validated before the old one is touched, with a hard fallback if validation fails.
- **`CursorHighlighter.GetScaleForPoint` called `MonitorFromPoint`/`GetDpiForMonitor` uncached on every 16ms tick** (~2 Win32 calls/tick for the lifetime of an enabled highlighter). Now caches DPI per monitor handle, refreshed only on `DisplaySettingsChanged`.
- **Snap-to-word polling leaked a `CancellationTokenSource` per poll** — cancelled on the next iteration but never disposed, a small but real resource leak over long reading sessions. Now disposed before each new one is created.

### Changed
- Preset slot buttons (`Preset1Btn`…`Preset5Btn`) grown to `MinWidth="44"` in addition to the existing `MinHeight="44"` — the rendered click target was under the WCAG AAA 44px floor width-wise despite meeting it height-wise.

### Already fixed before this review (confirmed still good, not new this cycle)
- Dead `MainWindowViewModel` stub deleted; `DataContext = this` is the sole data-context assignment.
- Hardcoded `FocusRingBrush="#FFD700"` removed from `App.xaml` — the ring now correctly resolves per-theme.
- `MagnifierEngine`'s font-smoothing calls use `SPIF_UPDATEINIFILE | SPIF_SENDCHANGE`, closing a persistent-corruption-on-crash risk.
- `ListViewItem`/`GridViewColumnHeader` styling added to `AppPresetDialog` — the near-black-on-black dark-theme bug is fixed.

### Known open items (not addressed this cycle)
- `MainWindow.xaml.cs` remains a ~2,936-line God Object with zero MVVM — the largest maintainability risk in the app.
- `LensWindow`'s hover-bar `ContextMenu`/`MenuItem` is still unstyled, rendering OS-default chrome against dark themes.
- `AppPresetDialog`'s exe-name field still has no `AutomationProperties.Name`.
- `ResizeMode` is inconsistent between `MainWindow` (`CanResize`) and `AboutDialog` (`CanResizeWithGrip`).
- No root `README.md`/`CHANGELOG.md` existed before this pass (see note below).

---

_Builds prior to 2026-09-08 were not individually changelogged; `version.txt`/`installer.nsi`/csproj were kept in sync throughout (confirmed no digit-width drift as of the 2026-09-01.0002 baseline). This CHANGELOG and a root `README.md` (promoted from `Docs/README.md`) are new as of this pass, per the review's own recommendation to add them for contributor/AI-agent onboarding parity with sibling apps._
