# NoteStation — Changelog

All notable changes to NoteStation are documented here.
Format based on [Keep a Changelog](https://keepachangelog.com/); versioning follows `yyyy.MM.dd.NNNN`.

## [Unreleased] – 2026-10-14 · One version mechanism (S-03)

### Changed
- **Version comes from `version.txt` only.** `build.bat` bumps it (`..\Tools\bump_version.cmd`, local date, `yyyy.MM.dd.NNNN`); `..\Directory.Build.props` stamps the exe from the same file and `installer.nsi` reads it through `..\Tools\version.nsh`. The csproj and installer.nsi no longer hold version numbers, so a build only changes `version.txt`.
- IDE, `dotnet build` and CI builds read the version without bumping it.
- Removed `version_bump.ps1`.

## [Unreleased] – 2026-10-13 · Suite shared library

Source-level change (not build-verified in the session that made it). Suite repo: `OldGamers.Suite.slnx`.

### Changed
- **Themes come from the suite set** (`..\Shared\Themes`, 108 themes, linked into the app). Values unchanged for this app; extra keys from the other apps added.
- **Dialogs follow the app theme**: message and confirm boxes use the themed `AppDialog` (theme colours, app font, 44 px buttons, focus on the safe default, Esc, Ctrl+C) instead of the unthemed Windows box. Startup / crash messages keep the Windows box.
- **UI font setting applies everywhere**: hard-coded `Segoe UI` in XAML replaced with the `AppFontFamily` token; monospace text uses `MonoFontFamily` (Cascadia Mono, Consolas) — "Segoe UI Mono" is not a Windows font.
- Resize grip, version display, keyboard window mover, type ramp now come from the shared library `OldGamers.Wpf.Shared` (same behaviour).

## [2026.10.12.0001] — 2026-10-12 — Skills Review fixes (NS-01…NS-12)

Source: `Claude outputs/NoteStation_SkillsReview_2026-10-11.md` + `NoteStation_SkillsReview_FixPlan_2026-10-12.md` (mockup `NoteStation_SkillsReview_Mockup_2026-10-12.html`).
**Not build-verified** — no `dotnet` in the session; run `build.bat` and `dotnet test ..\NoteStation.Tests`. Theme check: 0 failures / 0 missing keys (108 themes). Ship with TimerStudio 2026.10.12.0003 (shared IPC mutex name changed).

### Crash / data safety
- **NS-01** Quick-add no longer crashes on "in 99999999999 days" / "in 3000000 days" (`OverflowException` / `ArgumentOutOfRangeException`). Limits: 3650 days, 520 weeks, 120 months. Out-of-range or impossible dates ("Apr 31") stay in the task title and a hint under the quick-add box says so (announced to screen readers). Parser uses `[GeneratedRegex]` and has a testable `Parse(raw, today)` overload.
- **NS-05** Unknown UI-thread exceptions no longer keep the app limping: `DataStore` is frozen (no further writes/deletes), the error is logged and a themed "NoteStation needs to close" dialog shuts the app down. Clipboard COM errors, rich-text XAML parse errors and WPF layout `InvalidOperationException`s stay recoverable. Added `TaskScheduler.UnobservedTaskException` logging; `AppDomain` handler also freezes the store.
- **NS-11** Per-item saves use `FileOptions.WriteThrough` + `Flush(true)` before the atomic rename (no zero-length files after power loss).
- **NS-04** Single instance: `Local\NoteStation_SingleInstance` mutex; a second launch signals the running copy over a per-user named pipe and it comes to the front. `StartupUri` removed — `App` creates `MainWindow` after the check. Restart after Import / Restore / Clear All Data goes through `App.Restart()` (releases the mutex, passes `--restart`, new process waits ≤ 5 s).
- Screen-capture overlay no longer `Thread.Sleep`s on the UI thread; font-list loader is `async Task` with logged failures.

### Privacy
- **NS-03** Clipboard capture now also skips items marked `CanIncludeInClipboardHistory = 0` or `Clipboard Viewer Ignore` (KeePass, 1Password, Bitwarden) — previously only `ExcludeClipboardContentFromMonitorProcessing`. New opt-in Settings → Clipboard → Privacy → "Encrypt clipboard history on this PC" (DPAPI, current user) for clip JSON (`.json.enc`) and images (`.png.enc`); existing history converts at next start. PRIVACY.md updated (formats, encryption, backup caveat, file locations, Hide note).
- **NS-07** TimerStudio IPC mutex `Global\…` → `Local\…` (both apps).
- **NS-02** Note "Lock" renamed **Hide Note** (it is a privacy screen, not encryption — decision 2026-10-12). PIN dialog and Settings say so. PIN check hardening: stored PBKDF2 iteration count must be 100k–1M (blocks `v2:1:` downgrade and `v2:2147483647:` freeze); verification runs off the UI thread with a "Checking…" state; 5 wrong attempts → 30 s lockout, doubling to 15 min, with live countdown. PIN box accepts the full master passphrase (was capped at 9 characters).

### Look and feel
- **NS-09** Hover / pressed / selected states in `App.xaml` use `BrushSurf2` + outline tokens instead of alpha-white tints (invisible on light themes). Hide-note overlay, sticky undo toast and move grip use theme tokens; sticky word count + resize glyph follow the note's text colour (were 40 %-alpha grey). Command palette joins `OverlayPalette`.
- Emoji status icons → JT colour icons + text: priority matrix headers, habit streak chip ("5-day streak"), overlay headers and filter chips, Quick Capture modes, Focus Mode, Starred, Push as Calendar Event, colour-picker buttons. New icons `JtIcon.Flame`, `JtIcon.Hourglass` (SVGs in `Claude outputs/NoteStation_new_icons_2026-10-12/`). Tray menu and balloon texts are plain text.

### Hygiene
- **NS-08** Note list preview cached + `[GeneratedRegex]`. **NS-10** font-size list parses with `InvariantCulture`. **NS-12** stale PIN doc fixed. `version_bump.ps1` writes `version.txt` without BOM.
- Tests: `..\NoteStation.Tests` (xUnit v3) — NLP parser, PinHasher tamper cases, clipboard privacy formats, ClipVault round-trip, error policy, TimerStudio IPC contract.

## [2026.09.25.0002] — 2026-09-25 — Sticky-note text colour fix

Not build-verified (no `dotnet` in session).

### Fixed
- **Note text colour could not be changed after the note was first saved.** `TextRange.Save(DataFormats.Xaml)` stamps the effective text colour on the root `<Section Foreground>`; `TextRange.Load` re-applied it as a *local* Foreground on every block/run, which outranks `ContentBox.Foreground`, so Format → Text colour and the quick colours (which also set the text colour) had no visible effect. Loading now clears local Foreground values equal to that saved default (`RichTextFormatting.SavedDefaultForeground` + `ClearLocalForeground`); runs the user coloured explicitly (a different colour) keep their colour. `ApplyColors` also clears locals equal to the previously applied text colour.
- Same normalisation in `RichTextFormatting.LoadContent` (Task Detail notes, sidebar hover preview, quick-note dialog) and in list-row previews (`BuildPreviewInlines`), which now copy only *local* run colours — previously every preview run carried the note's saved text colour (e.g. dark `#1C1C1C` text in a dark-theme notes list).
- Existing notes are corrected on open; the fixed content is written back on the next edit or colour change.

## [2026.09.25.0001] — 2026-09-25 — WCAG 2.2 AAA remediation (suite app 2 of 6)

From `Claude outputs/Suite_WCAG_AAA_Fix_Plan_2026-09-25.md` §2 / `Suite_WCAG_AAA_Audit_2026-09-25.html`. Source-level only — **not build-verified** (no `dotnet` in the session); run `build.bat`. 200% UI-scale pass not run.

### Phase 1 — Themes
- `check_theme_contrast.py --fix` on all 108 themes (incl. Editorial): new `BrushDivider`, `BrushRed/Blue/Green/AmberText`, `BrushOnAccent`; Hint/Muted raised to 7:1. `--app-dir`: 0 failures, 0 missing keys.
- Accent-as-text → `*Text` (MainWindow due date, About title + Donate heading, TaskDetail "+ Add subtask", calendar "today", matrix quadrant labels). Text on fills → `BrushOnAccent` (Primary/Danger, nav tab, category chips, tag + habit chips, ComboBoxItem).
- **Bug:** `CloseActionDialog` OK button referenced an undefined `AccentButtonStyle` → `PrimaryButton`.
- Hover rows get a non-colour cue: ListBoxItem/TreeViewItem = BrushText outline, ComboBoxItem/MenuItem = 3px bar, task cards = outline ring, sidebar tabs = 2px outline. Selected list rows = 4px `BrushBlueText` bar (was Blue fill with BrushBg text).
- Removed opacity dimming that broke contrast (button hover/press, nav hover, empty folder count, notes-grid preview, sidebar hint, grip glyphs). Hard-coded `#585B70`/`#6C7086`/`#A6ADC8` text → theme/overlay brushes. Priority badge: black glyph on lightened fills (all ≥9:1). `App.ReadableTextColor` now picks black/white by contrast ratio.
- New `Theming/OverlayPalette.xaml` (fixed-dark contract palette, checker-verified) merged by Clip/Task overlays, Quick Capture, Focus Mode.

### Phase 2 — Styles + targets
- `FlatButton` → `TargetType ButtonBase`; implicit `Button`, `ToggleButton` (`FlatToggleButton`: checked = fill + OnAccent + outline). Implicit `ListBox`, `ListBoxItem` (template), `CheckBox` (template, ✓ glyph), `PasswordBox`, `RichTextBox`, `ListView` + `ThemedListViewItem`, `ThemedGridViewItem` + `GridViewColumnHeader`, `RadioButton`, `DatePicker` (+Calendar parts), `Slider` (44px thumb), `GroupBox`, `ProgressBar`. Ported from TimerStudio (FileCopy has none of these). No implicit ScrollViewer.
- `MenuItem` MinHeight 28 → 44; `TreeViewItem` rows 32 → 44; TextBox keyboard-focus border.
- All plan targets → 44×44: MainWindow note-view / Today / Focus / regex / calendar prev-next / category dots / Focus-task; overlay header buttons; FocusMode checkbox; SavedView/TaskPicker/WindowAttach close + inputs + secondary buttons; TaskDetail subtask/linked-clip remove; folder filters; Settings close-action combo.
- MainWindow tab toolbars are `WrapPanel`s (wrap instead of overflow at 44px); search is a fixed 240px field.
- Overlay headers split into two rows (title/close; text size + opacity slider).

### Phase 3 — Keyboard / focus / grips
- New `Controls/AccessibleResizeGrip.cs` (suite-canonical, + `Edge` Left/Right width-only mode), `KeyboardWindowMover.cs` (+ public `BeginKeyboardMove/Resize`), `TypeRamp.cs`, `StatusAnnouncer.cs`.
- Mouse-only grips replaced: MainWindow, About, Settings, Hotkeys, Note Format, Task Detail, Timer Push, Clip/Task overlays, Quick Capture (now resizable), Category Manager (was OS `CanResizeWithGrip`), Close Action (was decorative `ResizeGrip`), Notes sidebar width grip (width-only, Left/Right arrows, now always visible). Grips added to SavedView/TaskPicker/WindowAttach (now `CanResize`). PinSetup = documented exception (fixed PIN prompt, OS title bar).
- Keyboard move: Alt+Arrow (Shift = 1px), Ctrl+Alt+Arrow resize, "Move/Resize with keyboard" menu items — Clip/Task overlays, Quick Capture, sticky notes, borderless dialogs; Notes sidebar (undocked) + Shift+F10/Menu key opens its bar menu. Focus Mode (maximized) and Command Palette (centred, transient) are not movable — no change.
- Sticky-note quick colours are a `RadioButton` group: focus ring, ✓ on the selected colour, `IsChecked` mirrors the note colour (UIA SelectionItem), `FocusVisualStyle={x:Null}` removed. Opacity slider no longer under the grip. Move handle 28 → 44px.

### Phase 4 — Semantics
- Names: Category ListView, sidebar preview RichTextBox, subtask ProgressBar; ComboBox editable part inherits the ComboBox name (App.xaml:319).
- `SectionHeader` (Level2) / `DialogTitle` (Level1) styles; applied to 33 section labels + 9 dialog titles.
- `StatusAnnouncer` (UIA notification events): clip captured, note saved (debounced), task completed / habit done, task deleted + undo, text-deleted undo. Nav tabs / due-date chips / category chips expose `ItemStatus = Selected`.

### Phase 5 — Timing + typography
- Undo toasts (task delete, note text delete): lifetime = new Settings → "Show Undo prompt for" (5 s / 15 s default / 30 s / 1 min / Until dismissed; `prefs.json` `UndoToastSeconds`), countdown pauses while hovered or focused, ✕ dismiss button, UIA notification. Task delete was a fixed 5 s.
- Type ramp tokens `FontSizeCaption/Body/Title` (12/14/18); 132 XAML + 7 code sites < 12px → `FontSizeCaption` / `TypeRamp.Caption`.

### Shared tooling
- `check_theme_contrast.py --app-dir`: contract keys are required from `Themes/` even when a palette outside it defines them. `Themes_Reference.md` documents fixed-dark palettes.
- TimerStudio `Controls/AccessibleResizeGrip.cs` + `KeyboardWindowMover.cs` updated identically (additive).

## [Unreleased] — 2026-09-24 — Full-color icon set

Implements the approved `NoteStation_IconMockup_2026-09-24.html` (Claude outputs). Source-level only — no `dotnet` toolchain in the session; run `build.bat` to verify and assign the version.

### Changed
- **Fixed-color JT icon set replaces the stroke `Icn*` paths and emoji glyphs.** New `Theming/ColorIcons.xaml` (68 `DrawingImage`s, keys `JtIcon.<Name>`, frozen, 32×32 grid) merged in `App.xaml` right after the theme slot. Icons never change with the theme.
- Title bar, NavRow, all three toolbars, Add-task button, Settings dialog Data/Security buttons, sticky-note close/format/screenshot buttons, the icon-only view toggles (grid/list, Memoboard, Calendar, Focus, Matrix, month prev/next), and inline indicators (search, pinned, link, starred, pushed-to-timer) now use `<Image Source="{StaticResource JtIcon.*}">` with styles `JtBtnIcon` (18px), `JtSoloIcon` (20px), `JtInlineIcon` (16px), `JtMenuIcon` (16px). Disabled = 0.4 opacity.
- Remapped meanings: Categories → tag (was gear), Sidebar → sidebar panel (was star), clipboard Pin → push-pin (was star), Clear All → broom (was ×). Gear is now Settings-only; star is Favorite/Starred-only.
- Context menus (folder tree, clips, notes, tasks, sticky note, notes sidebar, More menu) show color icons in the icon column; emoji removed from headers. Command palette and clip overlay rows use icons.
- **Folder tree:** virtual nodes (Search, Recent, Pinned, Archived, Recently Deleted, Folders) use fixed icons. User folders show a 4px accent-color bar + icon (mockup option C) instead of the tinted emoji chip. Folder icon picker offers 28 icons with labels. `Icon` is now stored as a key (`briefcase`, `notebook`…); legacy emoji values in `prefs.json` are mapped on load by `JtIcons.NormalizeKey`, so existing folders keep their look.
- New `Core/JtIcons.cs`: `JtIcons.Get/Img/Menu`, `FolderChoices`, `JtIconConverter`, `FolderAccentBarConverter`.

### Fixed
- **Checkable menu items had no visible checked state** — the implicit `MenuItem` template had no check glyph, so Show on startup / Show in sidebar / Hide title bar / Formatting toolbar etc. looked identical on and off. Added a check mark shown when `IsChecked`.

### Removed
- 22 `Icn*` `PathGeometry` resources + `BtnIcon` style; `FolderAccentBrushConverter` and `FolderAccentTintConverter` (unused after the folder-tree change).

### Not changed (deliberately)
- Tray (WinForms) menu text, About dialog, Focus Mode / Quick Capture / overlay window headers, Eisenhower matrix quadrant labels, streak 🔥, priority ○▼▲ glyphs, Regex `.*` toggle, and the suite-standard `⊞ {Name} ({scale}×)` UI-size label.

## [Unreleased] — 2026-09-15

Fixes from `NoteStation_CodeSecA11y_Review_2026-09-14.md`. Source-level changes only in the session that made them (no `dotnet` toolchain available) — pending a local build via `build.bat`, which also assigns the real version number for this entry.

### Fixed
- **CRITICAL: `BrushAccent`/`BrushBackground`/`BrushGold`/`BrushTextMuted` — 4 more `DynamicResource` keys undefined in all 108 themes**, same bug class as the 09-09 `BrushSurface`/`BrushSubtle` fix. Used ~24× across `MainWindow.xaml` and 5 dialogs (badge pills, dialog chrome, borders). Mapped to the real 13-key contract: `BrushAccent`→`BrushBlue`, `BrushBackground`→`BrushBg`, `BrushGold`→`BrushAmber`, `BrushTextMuted`→`BrushMuted`.
- **HIGH: one C# code-behind site missed by the 09-09 `BrushSurface`/`BrushSubtle` cleanup.** `MainWindow.xaml.cs`'s Calendar "+N more" label still read `App.GetBrush("BrushSubtle")` (safe gray fallback via `App.GetBrush`, so not invisible, but broke the theme-contract invariant). Now `App.GetBrush("BrushHint")`.
- **MEDIUM: `SaveTasksAsync` had no ordering guarantee between overlapping background writes.** Rapid drag-reorders could race two writes for the same task id, with the older snapshot landing last. Added a per-task write sequence (`DataStore._taskWriteSeq`) — a write only proceeds if its snapshot is still the latest queued for that id by the time it runs.
- **MEDIUM: 2 silent clipboard-paste failures.** `PasteImage`/`PasteText` had bare `catch { }` around the SendKeys paste flow — a bitmap decode or clipboard-access failure left the UI suppress-flags cleared with no record anywhere. Both now route through `AppLog.Error`.
- **LOW: no implicit `ContextMenu`/`MenuItem` style** — right-click menus fell back to unstyled OS chrome on dark themes. Added the suite-standard style block (copied from TimerStudio) to `App.xaml`.
- **LOW: `ClipClipImporter`'s recursive directory walk had no depth limit or symlink-loop guard.** A malformed/circular directory structure could hang an import. Added a 64-level depth cap and a symlinked-directory skip, matching FileCopy's importer fix.
- Corrected 2 stale "known open items" from the 09-09 changelog entry below (PIN hashing, category-chip accessibility) — both already fixed, not tracked as such at the time.

### Deferred (not this pass)
- **`MainWindow.xaml.cs` God Object (5,421 lines) + no repository abstraction over `DataStore`** — review explicitly frames this as a multi-session, incremental-per-tab refactor; not attempted alongside a bug-fix pass, and unverifiable without a compiler in this environment.
- **Note-lock PIN is UI-only obfuscation, not real encryption at rest** — fixing this for real means either relabeling the feature or adding AES-GCM encryption keyed from the PIN; a scope decision for Matthew, not a mechanical fix.
- **Clipboard history has no at-rest encryption beyond the password-manager exclusion-flag check** — same kind of scope decision (regex denylist vs. DPAPI encryption); left for Matthew's call.
- **Clipboard-access OS permission gating (Windows Insider proposal, no shipped code to gate against yet)** — no concrete action possible without a real API surface to target; revisit if/when Windows ships it.
- Rich-text `TextRange.Load(..., DataFormats.Xaml)` deserialization hardening — reviewed, confirmed not currently reachable from untrusted input, no urgent action per the report.

## [2026.09.09.0003] — 2026-09-08/09

Fixes from `NoteStation_Full_Review_2026-09-07.md`'s Critical/High findings. Source-level changes only in the session that made them (no `dotnet` toolchain available) — pending a local build confirmation.

### Fixed
- **CRITICAL: `BrushSurface`/`BrushSubtle` resource keys, used 22× in `MainWindow.xaml` (Calendar view, Focus Mode button, day-of-week headers), were undefined in all 130+ theme files.** An unresolved `DynamicResource` doesn't throw in WPF — it silently left `Foreground` at its default (effectively black) and `Background` transparent, making the Calendar view and Focus Mode button unreadable on every dark theme. All 22 call sites now reference the real 13-brush contract (`BrushSurf2`/`BrushHint`/`BrushMuted`) instead of the two nonexistent keys.
- **The "atomic" write helper wasn't atomic.** `Settings/DataStore.cs`, `Settings/AppPrefs.cs`, and `MainWindow.xaml.cs`'s `PushTaskToTimerStudio` all did `File.WriteAllText(tmp) → File.Copy(tmp, path, overwrite) → File.Delete(tmp)` — a full byte-for-byte copy has its own crash/power-loss window the pattern was meant to eliminate. All three sites now use a single `File.Move(tmp, path, overwrite: true)`. Worst-case exposure was TimerStudio's own `timers.json`, written cross-process with no lock.
- **`FilterChip`'s hardcoded colors (`#A6ADC8` foreground) failed contrast on light themes** — measured ~2.2:1 against `Light.xaml`'s background, well under the app's 7:1 AAA bar (and even AA's 4.5:1). Split into two styles: `OverlayFilterChip` keeps the hardcoded colors for the two fixed-dark overlay windows (which paint their own chrome regardless of app theme), and `FilterChip` now routes through `DynamicResource BrushMuted`/`BrushSurf2` for `MainWindow`, so it stays AAA-contrast on all ~20+ light themes too.

### Known open items (not addressed this cycle)
- `MainWindow.xaml.cs` remains a 5,387-line God Object with no MVVM extraction — the suite's largest maintainability risk, carried forward since the 2026-08-28 suite review.

*(Corrected 2026-09-15: two items previously listed here — "category chips built in code-behind, missing AutomationProperties.Name" and "Note-lock PIN unsalted SHA-256" — were found already fixed in current source during the 2026-09-14 review and are removed from this list. "SaveTasksAsync ordering" and "no implicit ContextMenu/MenuItem style" were confirmed still open and are fixed below.)*

## [2026.09.04.0004] — 2026-09-04 — Editorial theme redesign

Implemented following a mockup canvas review Matthew approved. Not build-verified at the time (fixed above, 2026-09-08/09).

### Added
- **Editorial theme** (`Themes/Editorial.xaml`) — navy/blue dark theme, same 13-brush contract as every other theme, contrast hand-verified ≥7:1 AAA except `BrushHint` (~5.2-6.7:1 AA, documented — scoped to timestamps/placeholder text only). Not set as default; selectable via the Theme menu.
- **Typography**: `AppFontFamily` now leads with Inter, `DisplayFontFamily` leads with Instrument Serif (both fall back to Segoe UI/Georgia if not installed). Wordmark, sidebar section labels, and all 9 Settings section headers render in the serif/italic display font.
- **Vector icon system**: ~20 new `PathGeometry` resources replacing emoji glyphs across the title bar, NavRow, toolbars, sticky-note buttons, and Settings dialog.

### Fixed
- `BtnClipSort`/`BtnNoteSort`/`BtnTaskSort` click handlers were overwriting the new icon+label button content on first click — each button's label `TextBlock` now has its own name and the handler updates just the text.

## [2026.09.03.0002] — 2026-09-03 — Design review fixes

All 8 findings from the 2026-09-02 `/design` plugin critique, fixed and build-verified by Matthew.

### Fixed
- NavRow active-tab fill switched from `FocusRingBrush` gold (which collided with the keyboard focus ring) to `BrushBlue`.
- Category pill text color switched from a hardcoded `#1C1C1C` to a luma-based converter, so it stays readable against any user-chosen category color.
- "📋 Overlay" / "✅ Overlay" buttons renamed to "Clip Overlay" / "Task Overlay" to disambiguate.
- `FilterChip`/`StatusLink` given a 44×44 hit box and the standard outer focus ring (previously under the AAA touch-target floor).
- `StickyNoteWindow` footer restructured into two 44px rows (color swatches + format button; opacity slider + Aa/MD toggles) — previously most controls were under 44px.
- `AppSettingsDialog`'s flat 9-section scroll replaced with a 150px left nav rail.

### Not changed (corrected findings)
- Status-bar tab links: confirmed these render live per-tab counts, not literal duplicate tab names as originally assumed — left in place.
- Toast/balloon styling: native `NotifyIcon.ShowBalloonTip` OS chrome, not app XAML — not fixable without a full custom popup replacement; out of scope.

## [2026.05.21.0001] — 2026-05-21 — Initial release

First release. Clipboard manager (500-entry history, 5 quick-paste presets, XButton1/2 + Ctrl+Alt+Scroll cycling), sticky notes overlay (independent floating windows, per-note font/size/bold/italic/color), and task manager (drag-reorder, due dates, star, push-to-TimerStudio, quick-add bar). 20 themes copied from TimerStudio's theme set. Built for Low Vision users as a companion to the rest of the suite.
