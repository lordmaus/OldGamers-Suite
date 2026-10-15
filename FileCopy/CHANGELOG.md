# Changelog

All notable changes to File Copy Studio are documented here.
Format based on [Keep a Changelog](https://keepachangelog.com/); versioning follows `yyyy.MM.dd.NNNN`.

## [Unreleased] – 2026-10-14 · One log, shared helpers (S-05 / S-07)

### Changed
- **Log goes through the suite logger** (`OldGamers.Wpf.Shared` `AppLog`): same daily files `logs\filecopy-studio-<date>.log`, 30 days kept, viewer unchanged. Your Windows profile path is now written as `%USERPROFILE%`; repeated identical entries are merged and a log flood is capped.
- `window-sizes.json` writes are atomic (shared `WindowSizeStore`).
- Removed the unused `Microsoft.Extensions.Logging.Debug` package.

## [Unreleased] – 2026-10-14 · One version mechanism (S-03)

### Changed
- **Version comes from `version.txt` only.** `build.bat` bumps it (`..\Tools\bump_version.cmd`, local date, `yyyy.MM.dd.NNNN`); `..\Directory.Build.props` stamps the exe from the same file and `installer.nsi` reads it through `..\Tools\version.nsh`. The csproj and installer.nsi no longer hold version numbers, so a build only changes `version.txt`.
- IDE, `dotnet build` and CI builds read the version without bumping it.
- Removed `patch_version.ps1`.

## [Unreleased] – 2026-10-13 · Suite shared library

Source-level change (not build-verified in the session that made it). Suite repo: `OldGamers.Suite.slnx`.

### Changed
- **Themes come from the suite set** (`..\Shared\Themes`, 108 themes, linked into the app). Dark / Light now use the higher-contrast NoteStation/TimerStudio values (`#000000` / `#FFFFFF` base); other themes differ only slightly.
- **Dialogs follow the app theme**: message and confirm boxes use the themed `AppDialog` (theme colours, app font, 44 px buttons, focus on the safe default, Esc, Ctrl+C) instead of the unthemed Windows box. Startup / crash messages keep the Windows box.
- **UI font setting applies everywhere**: hard-coded `Segoe UI` in XAML replaced with the `AppFontFamily` token; monospace text uses `MonoFontFamily` (Cascadia Mono, Consolas) — "Segoe UI Mono" is not a Windows font.
- Resize grip, version display now come from the shared library `OldGamers.Wpf.Shared` (same behaviour).

## [2026.10.13.0001] — 2026-10-13 · Skills Review fixes

Source-level change (not build-verified in the session that made it). `version.txt` set to `2026.10.13.0000`; `build.bat` on 2026-10-13 produces `.0001` (another day → that day's `.0001`). Plan: `Claude outputs/FileCopy_SkillsReview_FixPlan_2026-10-13.md`.

### Fixed
- **Move never deletes an unverified source** (FC-01). The engine always verifies (CRC32) when Delete source is on, for interactive runs, presets, old job files and scheduled jobs. Verify is ticked and locked while Move is on (main window + Scheduler), with a note. Saving a scheduled Move job asks once.
- **Copy is flushed to disk before the source is deleted** (FC-03), and the source is deleted only after a CRC match.
- **Atomic promote** of `.fctemp` → destination with a single replace (FC-02); read-only destinations are replaced too. A crash at promote time no longer loses the old destination.
- **Crash logging** (FC-05): UI, background-thread and unobserved-task exceptions are logged; UI crashes show an error box (option to open the log folder) and save settings.
- **Settings save race** (FC-06): serialized on the UI thread, one writer at a time; exit save no longer collides with the auto-save.
- **Second Windows session** (FC-04): single-instance pipe is per session + user (was global → endless error loop / log flood); forwarded payload capped at 64 KB with a 5 s read timeout and back-off on errors.
- Symlink Preserve onto an existing folder gives a clear error instead of an exception; existing links are replaced (FC-07).

### Added
- **Scan reports what it skipped** (FC-10): unreadable folders and links pointing outside the source are logged, and the status bar shows "⚠ N items skipped — View log".
- `FileCopy.Tests` (xUnit v3): Move-without-Verify still verifies, atomic replace, conflict matrix, scan helpers, `InstanceArgs` round-trip.

### Changed
- **WCAG 2.2 AAA (§6):** all 107 themes pass 7:1 (Muted/Hint/Focus fixed; new `BrushDivider`, `*Text`, `BrushOnAccent`; `GoldFocusBrush` / `BrushSelectedText` added where missing); accent-coloured text uses `*Text` tokens; undefined `BrushBorder` → `BrushDivider`; minimum text 12 px (`FontSizeCaption`, 70 sites); section headings exposed as headings; 44 px keyboard `AccessibleResizeGrip` on all 6 windows and the log viewer (was 20 px).
- Release builds log at Information and above (FC-11); PRIVACY.md documents log content/location/retention and the correct data folder name.
- About: donation buttons are text-only (emoji removed).

## [2026.09.24.0002] — 2026-09-24

Full-color JT icon set applied per `Claude outputs/FileCopy_IconMockup_2026-09-24.html`. Not build-verified in-session (no dotnet toolchain) — run `build.bat`.

### Changed
- **Fixed-color icons replace emoji/Unicode glyphs** on the title bar (Hotkeys, Settings, About), Save Preset, run controls (Scan, START COPY, Pause/Resume, Cancel, Dry Run, Schedule, Swap), options (Compute CRC, Verify Only, Load/Save Job), file-grid toolbar (Skip selected/all), error-log and log-viewer panels, Scheduler toolbar/editor, About buttons and Settings/Hotkeys headers. New `src/Theming/ColorIcons.xaml` (37 `JtIcon.*` DrawingImages: 20 shared with NoteStation, 4 JT, 13 new) and `src/Core/JtIcons.cs` (`Get`/`Img`/`Menu`/`Label`, `JtIconConverter`). Icons never follow the theme; every icon keeps a visible label or tooltip + `AutomationProperties.Name`.
- **Row status column** now shows an action icon (plus a link icon for symlinks) via `FileItem.ActionIcon`; the Action text column is unchanged and screen readers still hear `StatusText`. Column `MinWidth` 44 so saved 32px widths grow to fit.
- **Source picker** Name column shows a folder/file icon instead of an emoji prefix; `PickerRow.SortKey` keeps folders sorted first.
- **Context menus**: file-row menu and preset Move left/right carry icons; implicit `MenuItem` template gained an Icon column (collapsed when no icon, so theme/recent menus are unchanged).
- Pause button label is now plain `Pause`/`Resume` with a bound icon (`PauseIcon`); its UIA name now follows the state (was fixed "Pause copy").
- Conflict (Overwrite always, Skip all, Rename) and symlink (Preserve, Skip) combo items show icons; the rest keep their glyphs.
- Empty-grid hint said "click ⟳ Scan" — now "click Scan"; Scheduler "has log" marker gained a tooltip + automation name.
- Unchanged by design (suite standard): "◑ Theme" and "⊞ {Name} ({scale}×)" labels.

## [2026.09.08.0002] — 2026-09-08

Executed against `FileCopy_Full_Review_2026-09-07.md`'s Phase 1-4 plan; confirmed by Matthew's local build (`_build-status.txt`: BUILD OK).

### Fixed
- **Cross-thread `FileItem` property-changed notifications from copy-worker threads.** `FileItem` mutated `INotifyPropertyChanged` properties directly from background copy-worker threads while the DataGrid bound to them on the UI thread — a plausible cross-thread crash, worse with `MaxParallel > 1`. Fixed at the `FileItem.Notify()` level: added `FileItem.UiDispatcher`, and `Notify()` now marshals the event raise via `Dispatcher.BeginInvoke` when called off-thread (the field write itself stays synchronous, so same-thread reads right after a set are unaffected). `RunAsync`/`VerifyOnlyAsync` gained a `uiDispatcher` parameter stamped onto every item at entry, threaded through `ICopyEngine`/`DefaultCopyEngine`/`CopyOrchestrator`.
- **Symlinked *file* sources (not just directories) could escape the source root under the default `Follow` policy.** Added a `PathIsInside(target, sourceRoot)` check in `Enumerate()`'s file loop, mirroring the existing directory-junction guard. Scoped to `Follow` only — `Preserve` doesn't read target bytes, so it isn't a content-exfiltration risk.
- **A scheduled job that threw before its normal completion path (e.g. a dropped network share) failed silently**, with no tray notification. Added an Error-icon balloon + `SystemSounds.Hand` to that `catch` path, matching the notification the success path already sends.
- **`docs/README.md`'s version string was stale** (`2026.05.09.0001`), not wired to the auto-bumped `version.txt`/csproj/installer — corrected, still a manual step each release.

### Changed
- **Per-row `ProgressBytes` writes** moved into the existing 50ms UI-update throttle gate in `Report()` — was firing unconditionally on every chunk callback (thousands of times/sec on fast drives).
- **Picker filter row** (size/count fields, date pickers, Clear/Select All/None/Invert): all controls grown from 32-36px to the app's 44px WCAG AAA touch-target floor.
- Added `DatePicker`/`Calendar` implicit theme styles to `App.xaml` (background/foreground/border/font/min-height only — the `Calendar` popup's individual day-cell numerals still render via OS `SystemColors`, flagged as a known remaining gap rather than claimed fixed).
- `AboutDialog`/`HotkeysDialog` resize grips replaced with the same accessible Canvas+Polygon pattern (focusable, keyboard-resizable) already used in `MainWindow` — `PresetSlotDialog` still uses the older WM_SYSCOMMAND-only drag grip and is a follow-up candidate.

### Corrected from prior review
- Bandwidth throttle UI, the transfer history/log viewer, and per-job scheduled-run notifications were all already implemented before this cycle — the source review's "Feature Gap" list was stale/incomplete on these three items.

---

_Builds prior to 2026-09-08 were not individually changelogged. See `Suite_Review_2026-08-28.md` and `todo.md` for the pre-2026-09 history._
