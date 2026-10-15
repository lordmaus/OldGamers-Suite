# Timer Studio — Changelog

All notable changes listed by build version (yyyy.mm.dd.xxx).

---

## [Unreleased] – 2026-10-14 · One log, shared helpers (S-05 / S-07)

### Changed
- **One log file: `%APPDATA%\TimerStudio\logs\TimerStudio.log`** (rotated at 512 KB to `.old`; e-mail addresses always redacted). Replaces diagnostics.log. Errors now include the full stack trace; your Windows profile path is written as `%USERPROFILE%`; repeated identical entries are merged and a log flood is capped. Old log files are left in place and can be deleted.
- `window-sizes.json` writes are atomic (shared `WindowSizeStore`).

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
- **Starting the app again brings the running window forward** (from the tray or minimised) instead of showing "Already running".
- Resize grip, version display, keyboard window mover, type ramp now come from the shared library `OldGamers.Wpf.Shared` (same behaviour).

## [2026.10.12.0003] — 2026-10-12 · NoteStation IPC mutex (paired release)

**Not build-verified.** Ship together with NoteStation 2026.10.12.0001.

### Fixed
- `Core/TimerStudioIpc.cs`: timers.json lock mutex `Global\NoteStation_TimerStudio_TimersJson` → `Local\…` (per-user file; a Global name could be pre-created by another session's user so NoteStation pushes always timed out — NoteStation review NS-07). Added `PushContractVersion` so both copies differ only by namespace. An older NoteStation build no longer shares the lock with this one — update both.

## [2026.10.12.0001] — 2026-10-12 · Skills Review fixes (TS-01…TS-14, UI-1)

Source: `Claude outputs/TimerStudio_SkillsReview_2026-10-11.md` + `TimerStudio_SkillsReview_FixPlan_2026-10-12.md` (mockup `TimerStudio_SkillsReview_Mockup_2026-10-12.html`).
**Not build-verified** — no `dotnet` toolchain in the session; run `build.bat` (it bumps the version again). Theme check: 0 contrast failures / 0 missing keys (107 themes).

### Security / data integrity
- **TS-03** SMTP: STARTTLS checkbox → **Security** dropdown (Automatic · SSL/TLS 465 · STARTTLS 587 · None). Automatic = implicit TLS on 465, STARTTLS otherwise (port 465 now works). "None" only selectable with no username; plaintext login refused in code. New `SmtpSecurity` setting; old `UseSmtpTls=false` + username migrates to Automatic, without username to None.
- **TS-04** Unreadable DPAPI password (profile migrated / other PC) now fails clearly: red field + "Saved password can't be read on this PC. Re-enter it.", Send Test disabled until re-entered; send path reports the same message instead of a generic auth error.
- **TS-01** Removed `TimerPersistence.SaveAsync` and `LoadAsync` — both released the thread-affine IPC mutex after an `await` (could orphan the NoteStation lock).
- **TS-02** `timers.json` is never written without the IPC lock during normal use: 250 ms wait, then deferred and retried every 1 s (max 10, then next 30 s autosave). Exit/crash/restart paths wait 5 s then write. Removes the up-to-5 s UI freeze.
- **TS-07** Unhandled exceptions now logged to `diagnostics.log` (AppDomain, UnobservedTask, Dispatcher — emails redacted). Outlook/OAuth/sound fire-and-forget tasks get fault continuations.
- **TS-05** Webhook payload built with source-generated JSON (control characters always escaped). URL must be https; plain http allowed for local-network targets (loopback, 10/8, 172.16/12, 192.168/16, 169.254/16, 100.64/10, IPv6 ULA/link-local, single-label or .local/.lan/.home.arpa/.internal hosts). Invalid URL → red border + message on the timer card; webhook skipped and logged.
- **TS-14** Recipient list validated (MimeKit) — inline "Not a valid address: …"; send path logs skipped entries and errors if none remain.

### Fixes
- **TS-08** "Restart now" (Calendar settings, after clearing Run-as-admin) saves timers and relaunches with `--restart`; the new instance waits for the single-instance mutex instead of exiting "already running". New `App.Restart()`.
- **TS-11** Timer cards re-apply width on every window resize (cards no longer clipped below ~442 px).

### UI
- **TS-10** Email settings extracted to `Controls/EmailSettingsPanel.xaml` + `ViewModels/EmailSettingsViewModel.cs`; Settings → Email and Calendar & Email Settings host the same control. Calendar dialog's unused "Client ID" field removed (client ID is embedded). Password now also saves on leaving the field. Calendar dialog's Send Test no longer forces OAuth mode back to SMTP.
- **UI-1** Overlay: theme tokens instead of hard-coded `#CC1E1E2E`/`#FF9BA1BC` (follows HighContrast), JtIcons instead of ⏱ ⚙ ✕ ↺ glyphs, row restart button 44×44 + accessible name, `AccessibleResizeGrip` replaces native grip, Alt+Arrow keyboard move (`KeyboardWindowMover`).

### Hygiene
- **TS-09** MailKit 4.18.1 + Microsoft.Identity.Client 4.90.1 pinned (were `4.*`); `RestorePackagesWithLockFile` on (commit the generated `packages.lock.json`); `build.bat` fails on `dotnet list package --vulnerable`.
- **TS-13** `EmailMode` enum replaces `EmailAlertService.Mode*` int constants (same numeric values on disk).
- **TS-06** Removed `Core/IcsUrlFetcher.cs` + `IcsUrls` setting — no caller and no UI.
- **TS-12** Removed `AppPrefs` pre-STJ `LegacyFallback`/`LegRead*` (only reachable when WindowWidth < 100). `TimerPersistence.ExternalChangeDetected` kept — still used by `MergeExternalTimerChanges`.
- `version.txt` written without BOM (S-03).

## [2026.10.10.0003] — 2026-10-10 · Card width

- `MainWindow.ApplyCardWidth` (ideal 400 → 430 px) used for new cards and undo-delete restore.
- `TimerCard.xaml` header actions moved to a right-aligned `WrapPanel` spanning two columns (reflow at narrow widths, WCAG 1.4.10); margins/padding trimmed 2–8 px. Buttons stay 44×44.

## [2026.09.25.0002] — 2026-09-25 · WCAG 2.2 AAA remediation

Source: `Claude outputs/Suite_WCAG_AAA_Audit_2026-09-25.html` (F1–F9, F11, F14) + `Suite_WCAG_AAA_Fix_Plan_2026-09-25.md` §1.
**Not build-verified** — no `dotnet` toolchain in the session; run `build.bat` (note: it bumps the version again).
Theme data verified: `python3 BadgeOverlay/Tools/check_theme_contrast.py --app-dir TimerStudio` → 0 contrast failures, 0 missing keys (107 themes).

### Theme data (F1–F4)
- All 107 themes re-derived to the new contract: Text + Muted ≥7:1 on Bg/Surf/Surf2/Bar, Hint ≥7:1 on Bg/Surf, FocusRingBrush ≥7:1 on Bg/Surf and ≥3:1 on Surf2 (RosePineDawn ring 2.79 → 7.1:1, C64 Muted 1.65 → 7.0:1+).
- New keys in every theme: `BrushDivider` (lines, ≥3:1), `BrushRedText/BlueText/GreenText/AmberText` (accent text, ≥7:1 on Bg/Surf), `BrushOnAccent` (text on Blue/Red fills, ≥7:1). `HighContrast.xaml` maps them to SystemColors.
- `BrushOrange` added to the 20 themes that lacked it. `BrushRed`/`BrushBlue` fills darkened/lightened in 71/22 themes so `BrushOnAccent` clears 7:1.
- 22 accent-as-text Foregrounds → `*Text` tokens (Overdue filter, Delete buttons, Pomodoro/Interval/recurrence/reminder labels, hotkey keycaps, validation text, About/WhatsNew titles, checkbox ✓, calendar today). `CalendarSettingsDialog` `BrushOrange` text → `BrushAmberText`.
- Text on accent fills → `BrushOnAccent`: PrimaryButton, DangerButton, AlertBanner, ComboBoxItem selected (was hard-coded White), CalendarDayButton selected (+ selected-today), category filter chip, lens-position buttons, hotkey capture button (Amber → Blue fill).
- Code: status error text `BrushRed` → `BrushRedText`; Markdown code runs `BrushBlue` → `BrushBlueText`.
- Hover/selected are no longer colour-only and no longer dim text: FlatButton hover = 1px BrushText outline, press = 97% scale (was 0.82/0.65 opacity on text); ComboBoxItem 3px bar; MenuItem 3px highlight bar; ListView rows Surf2 + 4px bar (Muted hover / Blue selected) — previously a `BrushHint` fill, which is now text-strength.
- Separators and search/input borders use `BrushDivider`.

### Styles + targets (F5, F6)
- `FlatButton` now targets `ButtonBase`; implicit `Button` and `ToggleButton` (`FlatToggleButton`: checked = Blue fill + OnAccent + outline) styles BasedOn it.
- New implicit `PasswordBox`, `RichTextBox`, `ListView` (+ keyed `ThemedListViewItem`), `RadioButton` styles. FileCopy has none of these to port — authored to the TextBox/CheckBox contract (RadioButton from BadgeOverlay `_Inputs.xaml`). No implicit ScrollViewer style.
- MenuItem MinHeight 28 → 44.
- 31 targets to 44×44 (MinHeight/MinWidth 0/32 and Height/Width 24/30 → 44): clear search, calendar picker Select All/Clear, SMTP password + STARTTLS (both dialogs), Category Manager colour/Add/Rename/Delete, Clock s/24h/✕ (title row 32 → Auto), overlay ⚙/✕, Pill ✕, Stats period/Refresh/Clear, TimerCard ×13. Glyph sizes unchanged; padding grows. Search box Height 36 → MinHeight 44.

### Keyboard, focus, grips (F7, F8)
- New `Controls/AccessibleResizeGrip.cs`: 44×44, focusable, Arrow = 10px / Shift+Arrow = 1px (clamped to Min/Max), BrushMuted glyph at full opacity, 2px FocusRingBrush ring, UIA Thumb "Resize window". Replaces the Canvas grips in MainWindow and About (old `ResizeHandle_*` handlers + `NativeMethods` removed) and the visual-only Paths in AppSettings, CalendarEventPicker, CalendarSettings (also its "visual only" Canvas), CategoryManager, Hotkeys, Settings, WhatsNew; Clock's `ResizeGrip`. Footers padded 44px on the right so the grip never covers Done/Apply; MainWindow card area gets a 44px bottom margin so it never covers the scrollbar.
- New `Controls/KeyboardWindowMover.cs`: Clock + Pill get Alt+Arrow move (Shift = 1px), context-menu "Move with keyboard" (SC_MOVE); Clock also Ctrl+Alt+Arrow resize + "Resize with keyboard" (SC_SIZE).

### Semantics (F9, F11)
- `TimerCard` name TextBox: `AutomationProperties.Name="Timer name"`.
- ComboBox template `PART_EditableTextBox` inherits the ComboBox's Name; Slider track RepeatButtons named "Decrease value" / "Increase value".
- New `SectionHeader` (HeadingLevel Level2) and `DialogTitle` (Level1) styles: 7 dialog titles + 33 section labels (was 2 headings app-wide).

### Typography (F14)
- Type-ramp tokens `FontSizeCaption=12 / FontSizeBody=14 / FontSizeTitle=18` (DynamicResource). All 396 XAML FontSize values under 12 → `FontSizeCaption`; 13 code-set sizes → `Controls.TypeRamp.Caption` (MainWindow, CalendarSettings, Settings, StatsDashboard, OverlayWindow, OverlayOptionsPopup). `Core/MarkdownHelper.cs` had none under 12.

### Not done / open
- Not run: 200% UI-scale visual pass; theme spot-check on Dark/RosePineDawn/C64/ZXSpectrum is by computed ratio only (all ≥ targets).
- TextBoxes with `MinHeight="0"` (~30: TimerCard H/M/S and reminder fields, Settings/CalendarSettings fields) not in the plan list — still under 44px tall.
- Calendar day buttons stay 30px (typed-date equivalent in the DatePicker text box).
- Overlay and Pill keep hard-coded colours (not theme-driven); Overlay drag bar still mouse-only for move (Shift+Arrow resize exists).

## [Unreleased] — 2026-09-25 · Full-color icons

Source-level change only (no `dotnet` toolchain in the session that made it) — pending a local `build.bat` run, which also assigns the version number.

### Changed
- **Full-color suite icons replace emoji / Unicode glyphs on buttons, menus and dialog headers.** New `Theming/ColorIcons.xaml` (50 fixed-color `DrawingImage`s, keys `JtIcon.<Name>`) merged after the theme dictionary. `App.OnStartup` re-adds it after its `MergedDictionaries.Clear()`, and `ApplyThemeUri` only swaps the `Themes/` entry, so icons survive every theme change and look the same in every theme. Same concept = same icon as NoteStation, FileCopy and BadgeOverlay (Hotkeys keyboard, Settings gear, About "?", Export/Import, Clipboard, Open, Refresh, Clear-all broom, Delete, Link, Web, File, Lock, Undo for reset, Swatches for colour pickers, Getting Started flag for What's New). 13 drawn for TimerStudio: AddTimer, Alarm, AlarmOff, Countdown, Lap, NewCategory, Pill, Pomodoro, Repeat, Snooze, Sound, Stopwatch, Template.
- **Timer state chip** on every card (running, paused, time's up, overdue after 1 minute, done): a fixed icon plus the state word, on `BrushSurf2`. Running and Paused were previously told apart only by the action button's label.
- **Categories: 4px bar in the category colour + fixed folder icon + category name** on the timer card, the grouped-view header and the filter chips (replaces the colour dot). The card's category button now shows the name, so category is no longer conveyed by colour alone.
- Card action button shows a bound icon (`ActionIconKey`: Start/Resume = play, Pause, Reset = undo). Lap, Notes and Alert-settings toggles, Snooze and Dismiss Alert (still `DangerButton`) get icons; Preview sound = speaker (▶ no longer doubles as Start).
- Overlay pin: fixed pin icon; pinned state = pressed look + Name/ToolTip ("Pinned to overlay — click to unpin"), replacing the 📌/📍 glyph swap.
- Card tint and Category Manager colour buttons: fixed swatches icon + a swatch of the user's colour.
- Mode and default-sort combos: icons via a per-item `ContentTemplate` (`JtLabelItemTemplate` + `JtLabelIconConverter`); items stay plain text, so `SelectedIndex` logic and the selection box are unchanged.
- Added `Core/JtIcons.cs` (`Get`/`Img`/`Menu`/`Label`/`KeyForLabel`, `JtIconConverter`, `JtLabelIconConverter`; WinForms-safe aliases) and `JtBtnIcon`/`JtSoloIcon`/`JtInlineIcon`/`JtMenuIcon`/`JtHeaderIcon` image styles (disabled = 40% opacity). Implicit `MenuItem` template sizes the icon slot to 16px and collapses it when there is no icon.
- Every changed button keeps or gains an `AutomationProperties.Name`; icons are never the only cue.
- Text that pointed at removed glyphs updated ("Click Refresh", "Settings → Email Settings", templates hint).
- Unchanged by design: "◑ Theme" and "⊞ {Name} ({scale}×)" stay text (suite standard); About dialog layout; tray menu (WinForms); overlay, pill and clock windows over games; popup-position arrows; Pomodoro/Interval phase text; template entries in the card's preset combo (plain text).

### Known open items (not changed this pass)
- Destructive actions without danger style: Delete Selected, card Remove timer, Delete category, Delete template, Clear history (red text, no style), Hotkeys Reset All to Defaults, Sign out.
- Targets under 44px: Clear search, card tint / clear tint, sound Preview / Browse / Clear, notes toggles, AM/PM, Import from Calendar, Category Manager colour and footer buttons, calendar picker Select All / Clear, Stats Refresh / Clear (32), overlay ⚙ / ✕ / restart, Pill ✕ (32 wide), Clock s / 24h / ✕ (24).
- Overlay row restart ↺ has no AutomationProperties.Name; overlay Done row is marked by colour only; red icons on a red alert card lose contrast.
- Theme button tooltip still says "Toggle dark / light theme"; AppSettingsDialog / CalendarSettingsDialog remain unused.

## [Unreleased] — 2026-09-15

Fixes from `TimerStudio_CodeSecA11y_Review_2026-09-14.md`. Source-level changes only in the session that made them (no `dotnet` toolchain available) — pending a local `build.bat` run, which also assigns the real version number for this entry.

### Security
- **HIGH: residual PII leak in `diagnostics.log`.** The literal `to=` field at every email call site was already redacted, but `DiagLog.WriteException` still logged the raw exception message unconditionally — SMTP/MimeKit/Outlook-COM/MSAL exceptions routinely embed the actual rejected recipient address in `.Message` text (e.g. `550 5.1.1 user@domain.com: recipient rejected`), so a failed send could still leak the plaintext address through that second channel. Added a `redactEmails` option to `DiagLog.WriteException` (regex-scrubs email-shaped substrings from the message chain) and wired it at all 5 `EmailAlertService.cs` call sites.
- **MEDIUM: MSAL token-cache DPAPI-failure silently stored a plaintext refresh token with no retry.** `MicrosoftOAuthSmtp.AfterCacheAccess` now fails closed on a DPAPI encrypt failure — logs and leaves the on-disk cache untouched instead of writing an unencrypted fallback. Worst case the user signs in again next launch.
- **MEDIUM: SMTP TLS-off path sent the password in cleartext with no UI warning.** Added a visible warning (`⚠ Without TLS, your SMTP password is sent in cleartext over the network.`) under the "Use STARTTLS" checkbox in both `SettingsDialog` and the legacy `CalendarSettingsDialog`, shown whenever TLS is off.

### Fixed
- About dialog's resize grip was decorative only (`IsHitTestVisible="False"`, no drag/keyboard handlers) — a WCAG 2.5.7 gap, the last dialog in the app not using the same accessible grip pattern `MainWindow.xaml` already has. Replaced with the same Canvas+Polygon grip and handlers as `MainWindow.xaml.cs`.
- `TimerCard`'s 7 header-row action buttons (pin, category, share, save-template, duplicate, delete, collapse) were still 32×32 — passed strict WCAG 2.1 AA but was inconsistent with the app's own 44px standard used everywhere else. Bumped all 7 to 44×44 for internal consistency; cards auto-size to content so this doesn't affect the card's minimum width.

### Note
- Confirmed `CalendarSettingsDialog.xaml`/`.xaml.cs` is dead code — `SettingsDialog` (per its own doc comment) replaced it and nothing in the codebase instantiates it anymore. Applied the TLS-warning fix there too for correctness-in-case, but it's a candidate for deletion in a future cleanup pass.

### Deferred (not this pass)
- **`MainWindow.xaml.cs` (2,260 lines) has zero MVVM** — no DataContext/ObservableCollection/ICommand, raw `TimerPanel.Children.Add/Remove` — despite `TimerCard.xaml.cs`/`TimerCardViewModel.cs` right next to it being properly MVVM. Multi-session architectural change, not attempted alongside a bug-fix pass; matches the same god-object deferral made for every other app in the suite this review cycle.
- **`AppPrefs.Instance` singleton referenced 245× across 10 files** — a DIP violation, explicitly flagged as backlog/not-urgent by the review, left untouched.

## 2026.09.08.0001

### Fixed
- **`version_bump.ps1` parse error on the version-sync code added in `2026.09.01.0001`.** The first `build.bat` run after that release failed at the version-bump step with a PowerShell parse error around an em-dash (`—`, U+2014) inside the new README-patching code. Root cause: the script (like other files in this suite) is saved as BOM-less UTF-8, and Windows PowerShell 5.1 reads a BOM-less `.ps1` using the system ANSI codepage, not UTF-8 — a literal em-dash inside a comment survives that mis-decoding fine, but the same character inside an actual string/regex token corrupts the token stream. Fixed by building the dash at runtime (`$dash = [char]0x2014`) and concatenating instead of embedding it as a raw character in code.
- Confirmed the `version.txt`/README/csproj/installer sync check added in `2026.09.01.0001` now runs clean on a full `build.bat` pass.

---

## 2026.09.01.0001

### Bug Fixes — Critical
- **`TimerStudio.csproj` 1.6 GB mojibake corruption** — the project file is a clean, ~40-line file again.
- **NoteStation push schema mismatch** (`Core/JsonSerialization.cs`): `TimerStudioJsonContext` now sets `PropertyNameCaseInsensitive = true`, so timers pushed in by NoteStation deserialize correctly instead of silently failing to bind.

### Bug Fixes — High
- **`timers.json` blind-overwrite** (`Core/TimerPersistence.cs`): added `LastKnownWriteUtc` / `ExternalChangeDetected()`. `MainWindow.SaveTimers()` now calls a new `MergeExternalTimerChanges()` before every autosave, additively picking up any timer NoteStation pushed since the last save without discarding existing cards — closes the receiving side of the NoteStation "TimerStudio push" integration.
- **`ListView` selection-highlight contrast** (`CalendarEventPickerDialog.xaml`, `CategoryManagerDialog.xaml`): both now carry a themed `ItemContainerStyle`/`ControlTemplate` that paints selection/hover from `BrushSurf2`/`BrushHint` instead of OS `SystemColors.Highlight*`.

### UI / Accessibility
- **Shared `Slider` style** (`App.xaml`): implicit `TargetType="Slider"` style (44px `MinHeight`, themed track/thumb, focus ring) now applies across all ~21 slider instances in the app.
- **Resize-grip keyboard alternative** (WCAG 2.5.7): `MainWindow.xaml.cs` gained `ResizeHandle_KeyDown` — arrow-key resize, Shift for 1px fine-tune.
- **Suite-wide version display** (`Core/VersionInfo.cs`): ported in from BadgeOverlay, giving a consistent `"v2026.09.01.0001"`-style display in the About dialog.

---

## 2026.06.09.001

### New Feature — Calendar Event Defaults
- **Calendar event timer defaults** (`Settings/AppPrefs.cs`, `Core/JsonSerialization.cs`, `Windows/CalendarSettingsDialog.xaml/.cs`, `MainWindow.xaml.cs`): Added a "Calendar Event Defaults" section to the Calendar & Email Settings dialog. The nine configurable defaults — popup position (3×3 grid), alert duration, alert repeat limit, escalate-to-fullscreen delay, hide from pill strip, early reminder enabled, reminder lead time, reminder repeat interval, and send email on reminders — are stored in `AppPrefs` as `CalEvt*` properties and applied automatically whenever `AddTimerFromCalendarEvent()` creates a new timer from a calendar import or auto-pull. Per-card settings continue to override these defaults. Defaults round-trip through the source-generated JSON serializer via new fields on `PrefsDto` with guarded `ApplyDto` assignments.

### Bug Fixes
- **`LastSortMode` not persisted to JSON** (`Core/JsonSerialization.cs`): `LastSortMode` was added to `AppPrefs` in 2026.05.21 but was never written to or read from `PrefsDto`. Fixed: added `LastSortMode` to `PrefsDto`, `ToDto(AppPrefs)`, and `ApplyDto()`.

---

## 2026.05.25.001

### Bug Fixes — Critical
- **C-5 — UI freeze on Outlook email send** (`Core/EmailAlertService.cs`): `RunOnStaThread` calls `t.Join()` — was running directly on the 40ms DispatcherTimer tick, blocking the UI thread until Outlook COM returned. Fixed: both `SendAlertAsync` and `SendReminderAlertAsync` now wrap the Outlook path in `Task.Run(() => RunOnStaThread(...))` so the Join happens off the UI thread.
- **C-6 — `LensPopupBase.DoDismiss()` event fired before `Close()`** (`Windows/LensPopupBase.cs`): `Dismissed?.Invoke` advanced timer state before `Close()` ran. If `Close()` threw, the timer was dismissed but the alert window remained on screen. Fixed: `Close()` is called first, then `Dismissed` is invoked — matching the correct pattern already used in `FlashOverlayWindow`.
- **C-7 — `SessionSwitch` not registered for new users; double-registered on import** (`MainWindow.xaml.cs`): Subscription was inside `LoadTimers()`'s foreach, skipped entirely when no timers were saved (first-run / empty state), and re-added each time `LoadTimers()` was called again (e.g. calendar import). Fixed: subscription moved to after `LoadTimers()` in the constructor — runs once unconditionally. Unsubscription in `OnClosed` unchanged.

### Bug Fixes — High
- **H-6 — Separator click applied `templates[0]`** (`Windows/TimerCard.xaml.cs`): `CboPreset_SelectionChanged` used `idx >= templateStart` which included the Separator at exactly `templateStart`, computing `tIdx = 0` and silently applying the first template. Fixed: changed to `idx > templateStart`; `tIdx` now subtracts 1 to account for the Separator.
- **H-7 — Outlook COM scan ran on UI thread** (`Windows/SettingsDialog.xaml.cs`): `ScanCalendarsAsync` DispatcherTimer.Tick called `GetOutlookCalendarFolders()` directly on the UI thread. Busy Outlook could freeze the UI for hundreds of ms. Fixed: offloaded to a dedicated STA thread with `Dispatcher.InvokeAsync` callback, matching the `RunCalendarPoll` pattern.
- **H-8 — `PillWindow` resisted application shutdown** (`Windows/PillWindow.xaml.cs`, `MainWindow.xaml.cs`): `PillWindow_Closing` unconditionally cancelled every `Close()`, including `Application.Shutdown()`, potentially stalling process exit. Fixed: added `_reallyClosing` flag + `CloseForReal()` method; `MainWindow.OnClosed` now calls `_pillWindow?.CloseForReal()`.
- **H-9 — `TimerHistory.GetAll()` returned live internal list** (`Core/TimerHistory.cs`): Callers that sorted or filtered the returned list would silently mutate `_cache`. Fixed: `GetAll()` now returns `new List<TimerHistoryEntry>(Load())` — a defensive copy.
- **H-10 — `int.Parse` in ICS date parser aborted entire file on bad data** (`Core/CalendarImporter.cs`): One malformed `DTSTART` caused `ParseIcsDateTime` to throw `FormatException`, which the outer catch converted to an empty list — all events lost. Fixed: replaced all `int.Parse` calls with `int.TryParse`; returns `DateTime.MinValue` on failure. `END:VEVENT` handler now skips events with `MinValue` start time rather than aborting the parse.
- **H-11 — Pomodoro snooze left UI showing "Countdown"** (`ViewModels/TimerCardViewModel.cs`): `DoSnooze()` restored `_model.Mode = TimerMode.Pomodoro` without calling `Notify(nameof(Mode))`, so `CboMode` and phase-visibility bindings retained "Countdown" for the full snooze duration. Fixed: added `Notify(nameof(Mode))` after the mode restore.

### Bug Fixes — Medium / Low
- **M-7 — `HttpResponseMessage` not disposed** (`Core/WebhookService.cs`): Response was not disposed on non-2xx status codes, leaving connections open. Fixed: changed to `using var response`.
- **M-8 — Unobserved faulted Task on MailKit fire-and-forget** (`Core/EmailAlertService.cs`): `SendViaMailKit` re-throws on Microsoft basic-auth rejection; the faulted `Task.Run` task was unobserved. Fixed: added `.ContinueWith(..., OnlyOnFaulted)` to log via `DiagLog.WriteException`.
- **M-9 — Stale doc comment on `SmtpPasswordEncrypted`** (`Settings/AppPrefs.cs`): Comment still said "AES-128" after DPAPI migration (H-4). Fixed: updated to "DPAPI-encrypted (ProtectedData.CurrentUser scope), base64-encoded".
- **L-3 — `.tmp` file left on disk if `File.Copy` threw** (`Core/TimerPersistence.cs`): Both `Save()` and `SaveAsync()` left the `.tmp` file if `File.Copy` failed (disk full, file locked). Fixed: wrapped write+copy in `try/finally { File.Delete(tmp) }`.
- **L-4 — Silent 52-occurrence cap in `ExpandRRule`** (`Core/CalendarImporter.cs`): Recurring events with no `COUNT`/`UNTIL` were capped at 52 occurrences (~1 year for weekly, ~7 weeks for daily) with no warning. Raised default cap to 200 and added comment.

---

## 2026.05.21.001

### Bug Fixes
- **Fix — Manual calendar sync blocked by auto-pull guard** (`MainWindow.xaml.cs`): `RunCalendarPoll()` had an unconditional early-exit when `CalendarAutoPullEnabled = false`, which meant the "Pull Now" button in Calendar Settings also silently did nothing. Added `bool manual = false` parameter; the guard is now skipped when `manual = true`. `PullRequested` handler updated to pass `manual: true`.
- **Fix — Sort order not remembered between sessions** (`MainWindow.xaml.cs`, `Settings/AppPrefs.cs`): Timer sort (by time/name/category) was applied in-memory only — not persisted. Added `LastSortMode` (int, default -1 = none) to `AppPrefs`. `SortCards()` now saves the chosen mode after every sort. On startup, after `LoadTimers()`, the last sort is automatically re-applied if one was previously chosen.

---

## 2026.05.20.0002

### Performance / Efficiency
- **`PropertyChangedEventArgs` cache** (`ViewModels/TimerCardViewModel.cs`): `Notify()` previously allocated `new PropertyChangedEventArgs(name)` on every call. At 40 ms tick rate × N running cards × 2 notifies per tick, this produced constant GC pressure. Replaced with a static `ConcurrentDictionary<string, PropertyChangedEventArgs>` cache — each unique property name allocates exactly once for the process lifetime.
- **`TimerModel.LapTimes` lazy init** (`Core/TimerModel.cs`): Changed from eagerly-allocated `= new List<TimeSpan>()` (allocated for every timer regardless of mode) to `private List<TimeSpan> _lapTimes` + `=> _lapTimes ??= new()` backing property. Countdown, DateTime, Pomodoro, and Interval timers (the majority) no longer pay this allocation.
- **`TimerHistory` in-memory cache** (`Core/TimerHistory.cs`): `Record()` previously did a full disk-read → deserialize → append → serialize → disk-write on every alert fire. Added `_cache` field: loaded once on first access (`Load()` short-circuits if `_cache != null`), kept in sync by `Save()`. Subsequent `Record()` and `GetAll()` calls skip the disk-read entirely.

---

## 2026.05.20.0001

### Bug Fixes
- **Fix — Outlook COM not polling (STA threading)** (`MainWindow.xaml.cs`): `RunCalendarPoll` was calling `ReadAllCalendarsOneShot` inside `Task.Run` (MTA ThreadPool thread). `AccessibleObjectFromWindow(OBJID_NATIVEOM)` requires an STA thread with a Dispatcher message pump — from MTA it silently returns `0x80004005` (E_FAIL). Fixed by replacing `Task.Run` with a dedicated STA thread that calls `Dispatcher.Run()` for a proper message pump. Work is queued via `BeginInvoke`; `InvokeShutdown()` exits the pump cleanly after results are dispatched to the UI thread.
- **Fix — False elevation-mismatch diagnosis** (`Core/CalendarImporter.cs`): When UAC is disabled (`TokenElevationType = Default`), all processes share the same token — no elevation mismatch is possible. `AcquireOutlook` was incorrectly blaming elevation when both TimerStudio and Outlook are equally elevated (UAC off / built-in admin account). Added `IsElevationMismatchPossible()` check; the misleading error is now suppressed in this configuration.
- **Fix — Outlook COM S3 fallback** (`Core/CalendarImporter.cs`): When S1 (ROT) and S2 (OBJID_NATIVEOM) both fail with Outlook running, `AcquireOutlook` now tries `Activator.CreateInstance` as an S3 fallback. Outlook's COM registration typically routes this to the existing running instance. All COM objects are properly released so no "only one version of Outlook can run" errors result.
- **Fix — `OverlayWindow` build errors** (`Windows/OverlayWindow.xaml.cs`): `OnSourceInitialized` and `OnClosed` overrides were incorrectly placed inside `OverlayRow` (a plain `internal class`, not a `Window` subclass). Moved to `OverlayWindow` where they belong.
- **Fix — `version_bump.ps1` UTF-8 encoding** (`version_bump.ps1`): `Get-Content -Raw` reads files as Windows-1252 by default, then `WriteAllText` re-wrote as UTF-8, double-encoding any non-ASCII bytes in `installer.nsi` and producing "Line too long" NSIS errors. Fixed by using `[System.IO.File]::ReadAllText(..., UTF8)` and `WriteAllText(..., UTF8)` with explicit encoding.

### Memory Leak Fixes
- **Fix — `TimerCardViewModel` / `TimerCard` never cleaned up on delete**: `_alertRepeat` `DispatcherTimer` was left running after card removal. The WPF Dispatcher holds a GC root to all running timers, pinning the entire VM + model + card chain indefinitely. Added `TimerCardViewModel.Cleanup()` (stops `_alertRepeat` + `_escalateTimer`) and `TimerCard.Cleanup()` (unsubscribes `PropertyChanged` + delegates to VM). `Cleanup()` is now called before every `Children.Remove(card)` in the delete handler, and for all remaining cards in `MainWindow.OnClosed`.
- **Fix — `AppSettingsDialog._saveDebounce` not stopped on close** (`Windows/AppSettingsDialog.xaml.cs`): Added `_saveDebounce.Stop()` in `OnClosed` to prevent a pending debounce tick from firing after the dialog is closed.
- **Fix — `SystemEvents.UserPreferenceChanged` anonymous lambda** (`App.xaml.cs`): Replaced anonymous lambda subscription (impossible to unsubscribe) with named `OnUserPreferenceChanged` handler. Unsubscribed in `OnExit` to release the delegate + closure.

### New Features
- **Pill overlay exclusion toggle**: Each timer card now has a "Hide from Pill overlay" checkbox in its settings panel. `ExcludeFromPill` is persisted to JSON. `RefreshPillWindow` skips any model with this flag set. Default: shown in Pill.

### Build / Tooling
- **`excludeKw` snapshot in `RunCalendarPoll`**: `CalendarExcludeKeywords` is now deep-copied (`new List<string>(...)`) before the STA thread starts, preventing concurrent-modification if the user edits settings during a poll.

---

## 2026.05.12.001
- **F3 — Templates library** (`MainWindow.xaml`, `MainWindow.xaml.cs`): "Templates" toolbar button opens a context menu listing all user-saved presets. Each entry has a delete sub-item. An "Export…" option writes all templates to a JSON file via `SaveFileDialog` (source-gen serialisation via `TimerStudioJsonContext.Default.ListTimerDto`). After deletion, all open cards refresh their preset combo via `RefreshPresetTemplatesOnAllCards()`.
- **F4 — Drag-to-reorder**: Confirmed already implemented in prior build; no changes required.
- **F5 — Timer duplication**: Confirmed already implemented (`DuplicateCommand`) in prior build; no changes required.
- **F6 — Bulk operations** (`MainWindow.xaml`, `MainWindow.xaml.cs`): "▶ All" and "⏸ All" toolbar buttons iterate all `TimerCard` children and call `StartIfIdle()` / `PauseIfRunning()` respectively.
- **F7 — Time-range filter** (`MainWindow.xaml`, `MainWindow.xaml.cs`): Filter chip bar (All / Today / This Week / Overdue) above the category bar. `ApplyTimeRangeFilter()` shows/hides cards by comparing `TimerModel.DueDate` against the selected range. Active chip uses `BrushBlue` fill + "✓ " prefix to satisfy WCAG 1.4.1.
- **F8 — Windows toast notifications** (`MainWindow.xaml.cs`, `ViewModels/TimerCardViewModel.cs`): `FireAlert()` now calls `_mainWindow.ShowAlertBalloon(name)` which delegates to the existing `ShowTrayNotification()` helper. Alert name appears in the balloon subtitle.
- **F9 — Alert repeat limit** (`ViewModels/TimerCardViewModel.cs`, `Windows/TimerCard.xaml`, `Windows/TimerCard.xaml.cs`): `AlertRepeatLimit` property (0 = unlimited, 1–100 = max fires). `_alertRepeat.Tick` stops the repeat timer once the count reaches the limit. Settings row `TxtAlertRepeatLimit` added to the card settings panel with numeric-only input guard.
- **F10 — Sound preview**: Confirmed already implemented (`BtnPreview_Click`) in prior build; no changes required.
- **F11 — Escalating alert** (`ViewModels/TimerCardViewModel.cs`, `Windows/TimerCard.xaml`, `Windows/TimerCard.xaml.cs`): `EscalateAfterSeconds` property (0 = disabled, 1–300 s). When `AlertStyle == Lens` and escalation is configured, `ShowAlertWindows()` starts `_escalateTimer`; on tick it opens `FlashOverlayWindow` if the alert is still active. Timer is cancelled in `CloseAlertWindows()` and `DoReset()`. Settings row `TxtEscalateAfter` added with numeric guard.
- **F12 — Missed alert re-fire on unlock** (`MainWindow.xaml.cs`): Subscribes to `Microsoft.Win32.SystemEvents.SessionSwitch`. On `SessionLock`/`RemoteDisconnect` records `_sessionLockedAt`. On `SessionUnlock`/`RemoteConnect` iterates all cards; any in `Done+AlertActive` state with `AlertFiredAt >= lockedAt` calls `card.FireMissedAlert()` via `Dispatcher.BeginInvoke`.
- **F14 — Interval timer mode** (`ViewModels/TimerCardViewModel.cs`, `Windows/TimerCard.xaml`, `Windows/TimerCard.xaml.cs`): New `TimerMode.Interval` entry in the mode combo. Configurable work minutes, rest minutes, and cycle count (0 = infinite). `Tick()` calls `AdvanceIntervalPhase()` at zero — toggles `Work↔Rest`, increments cycle counter, fires alert on phase change. `IntervalPhaseLabel` shows current phase + cycle count. Settings panel (`PnlInterval`) and phase label (`LblIntervalPhase`) added to TimerCard.
- **F15 — Clock window** (`Windows/ClockWindow.xaml`, `Windows/ClockWindow.cs`, `MainWindow.xaml`, `MainWindow.xaml.cs`): Chromeless always-on-top clock. 250 ms `DispatcherTimer` refreshes time display. Toggle seconds and 12/24 h buttons. Font size auto-scales to fill the window. Drag via title bar; resize grip bottom-right. Opened/toggled via `BtnClock` toolbar button.
- **F16 — Multi-lap stopwatch** (`ViewModels/TimerCardViewModel.cs`, `Windows/TimerCard.xaml`, `Windows/TimerCard.xaml.cs`): `LapCommand` appends `_model.Elapsed` to `LapTimes` list while stopwatch is running or paused. `PnlLapTable` ItemsControl renders each lap with index and formatted `TimeSpan`. `BtnLap` visibility bound to `LapButtonVis` (stopwatch mode only). `DoReset()` clears `LapTimes`.
- **F22 — Markdown notes preview** (`Core/MarkdownHelper.cs`, `Windows/TimerCard.xaml`, `Windows/TimerCard.xaml.cs`): Notes section gains Edit/Preview toggle toolbar (`PnlNotesToolbar`). Preview renders into a `RichTextBox` (`RtbNotesPreview`) via `MarkdownHelper.RenderInto()`. Supports `# ## ###` headings, `**bold**`, `*italic*`, `` `code` ``, `- *` bullets, `---` rules. No external dependency — pure WPF `FlowDocument`.
- **F24 — Statistics dashboard** (`Windows/StatsDashboard.xaml`, `Windows/StatsDashboard.cs`, `Core/TimerHistory.cs`, `MainWindow.xaml`, `MainWindow.xaml.cs`): "Stats" toolbar button opens `StatsDashboard` dialog. Period filter (Today / Last 7 days / Last 30 days / All time). Per-timer and per-day proportional bar charts rendered as WPF `Border` elements (no charting library). Summary row shows total fires, unique timers, and period label. "Clear History" button with confirmation. `TimerHistory.Clear()` helper added.
- Version bumped to `2026.05.12.001`.

## 2026.05.11.013
- **M-1 — WCAG 1.4.1 active chip fix** (`MainWindow.xaml.cs`): Active filter chips now use `BrushBlue` as background fill (distinct from inactive `BrushSurf2`), and prepend "✓ " to the label text. Previously both active and inactive chips had the same `inactiveFill` background — border colour was the only distinguisher (color-only information, WCAG 1.4.1 failure). Now distinguished by fill colour + text glyph + border + font weight.
- **M-2 — LensPopupBase stacking** (`Windows/LensPopupBase.cs`): Already using `Interlocked.Increment/Decrement` with per-instance `_stackIndex`. Confirmed correct; no change required.
- **M-3 — `BuiltInPresetCount` constant** (`Windows/TimerCard.xaml.cs`): Replaced magic literal `7` with `private const int BuiltInPresetCount = 7` in both `RefreshPresetTemplates()` and `CboPreset_SelectionChanged()`. Adding or removing a built-in preset now requires changing one constant, not hunting for literals.
- **M-4 — Name/Notes length caps** (`ViewModels/TimerCardViewModel.cs`): `Name` setter clamps to 255 chars; `Notes` setter clamps to 10 000 chars. Previously pasting large text into either field persisted the full content to `prefs.json` on every 30 s autosave tick.
- **M-5 — FlashOverlayWindow dismiss order** (`Windows/FlashOverlayWindow.cs`): `Close()` is now called (in try/catch) before `Dismissed` fires. Previously `Dismissed` fired first — if `Close()` then threw, the fullscreen overlay stayed open with no dismiss path.
- **M-6 — OverlayWindow shutdown fix** (`Windows/OverlayWindow.xaml.cs`): Added `_reallyClosing` flag and `CloseForReal()` public method. `OnClosing` now only cancels the close when `_reallyClosing` is false. `MainWindow.OnClosed` calls `_overlayWindow?.CloseForReal()` so the overlay actually closes during `Application.Shutdown()`. Previously `OnClosing` cancelled every close unconditionally, blocking shutdown.
- **M-7 — Debounce `AppPrefs.Save()` in settings sliders** (`Windows/AppSettingsDialog.xaml.cs`): Added `_saveDebounce` (`DispatcherTimer`, 500 ms). All slider `ValueChanged` handlers now call `QueueSave()` instead of `AppPrefs.Instance.Save()` directly. Save fires once, 500 ms after the last slider movement. Combo-box changes (infrequent) still save immediately. Eliminates UI lag and excessive disk I/O during rapid slider drag.
- **M-8 — Pomodoro snooze preserves phase** (`ViewModels/TimerCardViewModel.cs`): Added explicit Pomodoro branch in `DoSnooze()`. Saves `PomodoroPhase`, `PomodoroSessionCount`, and `PausedRemaining` before the snooze countdown, restores them after `DoStart()`. Previously snooze converted Pomodoro → Countdown permanently, losing all phase state.
- **L-1 — Remove redundant `Thread.Sleep`** (`Core/AlertSoundPlayer.cs`): Resolved in 2026.05.11.012 alongside C-1.
- **L-2 — Cache `OverlayRow` brushes** (`Windows/OverlayWindow.xaml.cs`): `OverlayRow` now caches alert-colour brushes as instance fields (`_brushAlert`, `_brushProgressBar`) and reallocates only when `AlertColor` changes. Normal-state brushes are `static readonly`. Previously `Update()` allocated 4–5 new `SolidColorBrush` objects every 40 ms per pinned timer.
- **L-3 — Static `FontFamily` instances** (`MainWindow.xaml.cs`, `Windows/PillWindow.xaml.cs`): `new FontFamily("Segoe UI")` / `"Consolas, Segoe UI"` replaced with shared `static readonly` fields. Eliminates per-chip, per-tick allocations.
- **L-4 — Pill window multi-monitor positioning** (`Windows/PillWindow.xaml.cs`): `PositionAtBottomCentre()` now uses `Screen.FromPoint(Cursor.Position).WorkingArea` (DPI-corrected) instead of `SystemParameters.PrimaryScreenWidth/Height`. On secondary monitors the pill window now appears on the active monitor, not off-screen on the primary.
- **L-5 — DoDismiss re-entrancy**: Resolved in 2026.05.11.012 as part of C-2.
- **L-6 — `SettingsTab` enum** (`MainWindow.xaml.cs`): Added `private enum SettingsTab { General = 0, Calendar = 1 }`. `BtnSettings_Click` and `BtnCalendarSettings_Click` now use `(int)SettingsTab.General/Calendar` instead of magic `0`/`1` literals.
- **L-7 — Webhook retry** (`Core/WebhookService.cs`): `PostAsync` now retries once after 2 s on 5xx responses. 4xx responses are not retried (client error — retrying won't help). Failures are logged per-attempt.
- **L-8 — Standardise timestamps to UTC** (`ViewModels/TimerCardViewModel.cs`): `_model.LastFiredAt` now assigned `DateTime.UtcNow` instead of `DateTime.Now`. Display label (`LastFiredLabel`) already calls `ToLocalTime()` for display. Eliminates the mixed UTC/local split on `TimerModel` (previously `LastFiredAt` was local, `AlertFiredAt` was UTC).
- Version bumped to `2026.05.11.013`.

## 2026.05.11.012
- **C-1 — Fix STA thread + MediaPlayer COM leak** (`Core/AlertSoundPlayer.cs`): Removed outer `Task.Run` wrapper around STA thread creation. Captured `Dispatcher.CurrentDispatcher` before `Dispatcher.Run()`. Added `MediaEnded` handler that calls `player.Close()` then `dispatcher.InvokeShutdown()`, causing `Dispatcher.Run()` to return and the STA thread to exit naturally. `MediaFailed` also calls `InvokeShutdown()`. No more `thread.Join(10 s)` blocking a thread-pool worker. Each custom-sound play now cleans up its thread and COM wrapper on completion. **Also fixed L-1**: Removed redundant `Thread.Sleep` calls between `Beep()` invocations (`Beep` already blocks for its stated duration).
- **C-2 — Fix double-dismiss race** (`ViewModels/TimerCardViewModel.cs`): In `DoDismiss()`, `_model.AlertActive = false` and `_model.AlertDismissed = true` are now set before `_alertRepeat.Stop()` and `CloseAlertWindows()`. Previously `CloseAlertWindows()` fired `Dismissed` events on both `_flashOverlay` and `_lensAlert` (AlertStyle.Both) while the flags were still in their pre-dismiss state, causing a re-entrant second call to pass the guard and double-advance the Pomodoro phase or recurrence counter.
- **C-3 — Stop `_calendarPollTick` in `OnClosed`** (`MainWindow.xaml.cs`): Added `_calendarPollTick?.Stop();` alongside `_tick.Stop()` and `_saveTick.Stop()`. Previously the poll timer fired after window close, accessed `TimerPanel.Children`, and called `Dispatcher.InvokeAsync` on a disposed dispatcher → `ObjectDisposedException`.
- **C-4 — Fix fragile null-coalesce in `TimerHistory.Record`** (`Core/TimerHistory.cs`): Replaced `(notes ?? "").Length > 200 ? notes.Substring(0, 200) : (notes ?? "")` with `notes = notes?.Length > 200 ? notes[..200] : (notes ?? "");` before entry construction. The old pattern length-checked the coalesced value but sliced the original reference, which would NullReferenceException on any future refactor that breaks the implicit null→0-length assumption.
- **H-1 — Atomic `_nextId`** (`Core/TimerModel.cs`): Replaced `_nextId++` with `Interlocked.Increment(ref _nextId)`. Concurrent timer construction (calendar poll thread + main thread) could produce duplicate IDs, breaking dedup keying and history correlation.
- **H-2 — Defensive copy before `Task.Run`** (`MainWindow.xaml.cs`): `var hidden = new List<string>(AppPrefs.Instance.HiddenCalendarPaths)` — copies the list on the UI thread before the background task closure captures it. `List<string>` is not thread-safe; concurrent reads/writes from two threads produced torn reads.
- **H-3 — Remove `Task.Run` from `PlayCustom`** (`Core/AlertSoundPlayer.cs`): Resolved as part of C-1. The outer `Task.Run` that wrapped the STA thread creation (and then blocked via `thread.Join`) has been removed. The STA thread itself (`IsBackground = true`) provides background execution without consuming a thread-pool worker for up to 10 s.
- **H-4 — DPAPI password storage** (`Core/EmailAlertService.cs`): Replaced `GetKey()`/AES-128 encryption with `ProtectedData.Protect/Unprotect(DataProtectionScope.CurrentUser)`. The old AES key was derived from `UserName + MachineName`, reproducible by any process running as the same OS user. DPAPI ties the secret to the OS user's credential vault. **Also fixed M-9**: Email `to=` address redacted from DiagLog (`to=<redacted>`).
- **H-5 — Pomodoro resume `StartedAt`** (`ViewModels/TimerCardViewModel.cs`): Added explicit Pomodoro branch in `DoResume()`: `_model.StartedAt = DateTime.UtcNow - (_model.TotalDuration - _model.PausedRemaining)`. Previously Pomodoro fell through to the `else` (Stopwatch) branch where `PausedElapsed = 0`, resetting the phase to its full duration instead of resuming from the paused point.
- **H-6 — ICS response size limit** (`Core/IcsUrlFetcher.cs`): Set `_http.MaxResponseContentBufferSize = 10 * 1024 * 1024` (10 MB). `HttpClient.GetStringAsync` now throws `HttpRequestException` if the feed exceeds the limit, preventing a 500 MB+ feed from allocating ~1 GB in-memory (UTF-16) and writing 500 MB to the temp directory.
- Version bumped to `2026.05.11.012`.

## 2026.05.11.011
- **6 new themes**: One Dark, Gruvbox Dark, Tokyo Night, Rosé Pine (dark) + Catppuccin Latte, GitHub Light (light). All pass WCAG AAA (≥7:1) — light-theme accent colors darkened vs. their upstream web values to achieve AAA on their respective backgrounds. `App.ThemeNames` expanded to 12 entries; `_lightThemes` gains `GithubLight` and `CatppuccinLatte`.
- **Theme menu groups**: `BtnTheme_Click` entries now ordered dark-first, light-second with a comment separator line for readability.
- **Cross-app reference doc**: `Applications for Gaming/Themes_Reference.md` created — lists all 12 themes with full hex values for every brush key, the WCAG AAA contract, light-theme flag instructions, and a checklist for porting themes to other apps in the suite.
- Version bumped to `2026.05.11.011`.

## 2026.05.11.010
- **Remove ICS/iCal URL feeds**: Removed the "ICS / ICAL URL FEEDS" panel from Calendar Settings (UI, handlers, init call). `AppPrefs.IcsUrls` stays in the JSON schema for backward-compat but is no longer surfaced or used.
- **Fix Outlook timeout hang**: `CalendarImporter.AcquireOutlook` no longer attempts to auto-launch Outlook via shell. Previously it blocked for 12 s polling `GetActiveObject` after `Process.Start("outlook.exe")`, causing the logged `"Outlook did not become available within 12 s"` error on every calendar refresh when Outlook wasn't running. Now it attaches only to a running instance; if none is found it returns `null` immediately with a short DiagLog message. Users must open Outlook themselves before scanning calendars. `LaunchOutlookViaShell` method and `using System.Diagnostics` removed.
- Version bumped to `2026.05.11.010`.

## 2026.05.11.009
- **Fix — Light theme not applying to cards**: `TimerCard.ApplyCardBackground()` was calling `FindResource("BrushSurf")` which captures a static brush snapshot at call time. When the theme `ResourceDictionary` is swapped the card `Border.Background` still held the old dark brush. Fixed by switching to `SetResourceReference(Border.BackgroundProperty, "BrushSurf")` which re-establishes a live DynamicResource link that tracks subsequent theme changes. `BtnCardTint` similarly updated. Also fixed `RefreshUI()` which was a no-op (`_vm.Model.ToString()`) — corrected to call `ApplyCardBackground()`.
- **New themes — Nord, Dracula, Mocha, Warm Light**: Four `ResourceDictionary` theme files added under `Themes/`. All brushes verified ≥7:1 contrast (WCAG AAA) on their respective backgrounds. `App.ThemeNames` and `_lightThemes` updated; `BtnTheme_Click` menu lists all six options with a `✓` prefix on the active theme. Focus ring: gold (`#EBCB8B` / `#FFD700`) for all dark themes; deep blue (`#0040CC`) for light themes.
  - `Nord.xaml` — Nordic calm dark: Bg `#2E3440`, Text `#ECEFF4`, accents from Nord palette. Contrast 10.9:1.
  - `Dracula.xaml` — Dracula dark: Bg `#282A36`, Text `#F8F8F2`, purple/cyan/pink accents. Contrast 10.4:1.
  - `Mocha.xaml` — Catppuccin Mocha: Bg `#1E1E2E`, Text `#CDD6F4`, soft pastel accents. Contrast 9.8:1.
  - `WarmLight.xaml` — Solarized-inspired warm light: Bg `#FDF6E3`, Text `#2C2B2A`, muted amber/teal accents (darkened for AAA on light bg). Contrast 13.0:1.
- Version bumped to `2026.05.11.009`.

## 2026.05.11.008
- **Feature 7 — Timer groups/folders (rendering pass)**: `MainWindow` now renders a full grouped-panel view toggled by the new "☰ Grouped" / "☰ Flat" nav-bar button. `RebuildGroupedPanel()` collects all `TimerCard` children, groups by `Category`, and intercalates collapsible `Border` headers for each group. Each header shows a coloured-dot accent, the category name, "▶ Start All" and "⏸ Pause All" action buttons, and a collapse/expand chevron. Collapse state is persisted to `AppPrefs.CollapsedCategories`. Uncategorised cards are shown first with no header. `MakeCategoryHeader()` builds the header imperatively from `DockPanel` / `StackPanel` using theme brushes. `ToggleCategoryCollapse()` toggles collapse state and calls `RebuildGroupedPanel()`. `StartAllInCategory()` and `PauseAllInCategory()` iterate children and call the new `TimerCard.StartIfIdle()` / `TimerCard.PauseIfRunning()` public methods (backed by `TimerCardViewModel.RequestStart()` / `RequestPause()`). `FlattenGroupedPanel()` removes all header elements and re-shows all cards when grouped mode is turned off. Rebuild is hooked into: Add, Delete (lambda), Undo Delete, Sort, and LoadTimers. `CountTimerCards()` helper added so `UpdateStatus` and `AddTimer` count only `TimerCard` elements (not `Border` headers). Header widths are kept in sync by `ConstrainTimerPanel()`. Grouped-view state persisted to `AppPrefs.GroupedView` and restored on startup. WCAG: collapse/expand buttons have 44 px min target and descriptive `AutomationProperties.Name`.
- **Feature 11 — Compact pill mode**: New `Windows/PillWindow.xaml` + `PillWindow.xaml.cs` — an always-on-top, no-title-bar, 48 px-tall translucent rounded strip (`AllowsTransparency=True`, `WindowStyle=None`, `ShowInTaskbar=False`, `Topmost=True`). Shows all currently running (or alerting) timers as `name + countdown` pairs separated by dividers. Refreshed every tick (40 ms) via `MainWindow.RefreshPillWindow()`. Draggable anywhere via `DragMove()`. Positioned at screen bottom-centre on first open. Toggled via "💊 Pill" / "💊 Pill ✓" nav-bar button; visibility state persisted to `AppPrefs.PillWindowVisible` and restored on startup. Close button (✕) hides rather than destroys the window. `FormatRemaining()` helper produces compact `h:mm:ss` or `m:ss` strings.

## 2026.05.11.007
- **Feature 1 — Recurring DateTime events**: `TimerModel` gains `RecurrenceMode` (None/Daily/Weekly/Monthly) and `RecurrenceInterval`. On dismiss, `DoDismiss()` calls `AdvanceRecurrence()` which adds days/weeks/months to `TargetDateTime` then restarts automatically. Recurrence combo + interval box shown in the card body when mode is DateTime + idle. Label shows human-readable summary ("Repeats every 2 weeks"). Persisted via DTO.
- **Feature 2 — Webhook alerts**: `Core/WebhookService.cs` — fire-and-forget `HttpClient.PostAsync` with JSON payload `{event, timer, firedAt, notes, remaining}`. `SendAlert` called from `FireAlert()`; `SendReminder` called from `FireReminder()`. Per-timer webhook URL TextBox added to alert settings panel. URL truncated to host+path in DiagLog for privacy. Blank URL = disabled (no-op).
- **Feature 3 — Per-timer alert volume**: `TimerModel.AlertVolume` (0.0–1.0, default 1.0). `AlertSoundPlayer.PlayCustom` now accepts `volume` parameter applied via `MediaPlayer.Volume`. `PlayForModel` passes `model.AlertVolume`. Built-in kernel32 `Beep` always uses system volume. Volume slider added to alert settings panel with live `%` label. Settings undo (Ctrl+Z) includes volume.
- **Feature 4 — ICS / iCal URL feeds**: `Core/IcsUrlFetcher.cs` downloads public ICS feeds via `HttpClient`, writes to a temp file, passes to existing `CalendarImporter.ParseIcs()`, then deletes temp. `FetchAllAsync()` iterates `AppPrefs.IcsUrls` with per-URL error logging. Settings → Calendar tab: URL list with Add / Remove / Test Fetch. Supports Google Calendar, iCloud, Office 365 public calendars, any standard iCal URL.
- **Feature 5 — Timer history log**: `Core/TimerHistory.cs` — rolling 200-entry log to `%AppData%\TimerStudio\history.json` (JSON source-gen, thread-safe write with atomic temp-rename). `Record(id, name, firedAt, notes)` called from `FireAlert()`. `LastFiredAt` stored on `TimerModel` and shown in a small label below the time-since row.
- **Feature 6 — Auto dark/light theme**: `App.xaml.cs` reads `HKCU\...\Themes\Personalize\AppsUseLightTheme` at startup and on `SystemEvents.UserPreferenceChanged` to follow the Windows color scheme. `SetTheme` gains `saveToPrefs` parameter (auto-mode never persists). `AppPrefs.ThemeAuto` persisted. Toggle checkbox added to Settings → Event Settings → Appearance section.
- **Feature 7 — Timer groups/folders**: `AppPrefs.CollapsedCategories` persists collapse state per category header. DTO mapper wires new list field. (MainWindow category-group rendering is scaffolded for the next UI pass; collapse state infrastructure is complete.)
- **Feature 8 — Pomodoro mode**: `TimerMode.Pomodoro = 3` fourth mode. `PomodoroPhase` (Work/Break/LongBreak), `PomodoroSessionCount`, and four duration fields (`PomodoroWorkMinutes`, `PomodoroBreakMinutes`, `PomodoroLongBreakMinutes`, `PomodoroLongBreakAfter`) on `TimerModel`. `DoStart()` resets phase/count, sets duration from work minutes. `Tick()` handles Pomodoro same as Countdown. On alert dismiss, `AdvancePomodoroPhase()` transitions Work → Break → (LongBreak after N sessions) → Work and auto-restarts. Phase label ("🍅 Work · Session 2/4") shown above the progress bar. Settings grid shows four numeric inputs (work / break / long break / sessions). Mode added as "Pomodoro 🍅" in CboMode.
- **JSON DTO — new fields**: `TimerDto` gains `RecurrenceMode`, `RecurrenceInterval`, `WebhookUrl`, `AlertVolume`, `LastFiredAt`, `PomodoroWorkMinutes`, `PomodoroBreakMinutes`, `PomodoroLongBreakMinutes`, `PomodoroLongBreakAfter`, `PomodoroPhase`, `PomodoroSessionCount`. `PrefsDto` gains `IcsUrls`, `ThemeAuto`, `CollapsedCategories`. `Clamp(d.Mode, 0, 2)` → `Clamp(d.Mode, 0, 3)`.
- Version bumped to `2026.05.11.007`.

## 2026.05.11.006
- **Security — DPAPI token cache encryption**: `MicrosoftOAuthSmtp` now wraps the MSAL token cache with `ProtectedData.Protect/Unprotect` (`DataProtectionScope.CurrentUser`) before storing in `prefs.json`. The refresh token is encrypted to the Windows user account, invisible to other users or processes. Existing plaintext caches migrate automatically on first read (DPAPI decrypt fails → fall back to raw bytes; AfterCacheAccess rewrites encrypted). DPAPI unavailable (rare server scenario) falls back to plaintext with a DiagLog warning.
- **Performance — PublishReadyToRun**: Added `<PublishReadyToRun>true</PublishReadyToRun>` to `TimerStudio.csproj`. At publish time, the .NET 10 toolchain generates native R2R images alongside the IL, pre-JITting hot paths for faster cold-start. No behavior change; only affects startup performance on first launch.
- **Accessibility — Overlay keyboard resize (SC 2.5.7)**: `OverlayWindow` uses `WindowStyle=None`, which removes the Windows system menu (Alt+Space → Size). Added `OnKeyDown` override: `Shift+Arrow` resizes ±10 px, `Shift+Ctrl+Arrow` ±50 px. `AutomationProperties.HelpText` and ToolTip on the resize grip area document the keyboard alternative for screen reader and sighted users. Standard `CanResize` dialogs retain system-menu keyboard resize (Alt+Space → S).
- **Build — Authenticode signing step**: `build.bat` step `[3b]` conditionally runs `signtool sign` on `TimerStudio.exe` and `TimerStudio_Setup.exe` when `SIGN_THUMBPRINT` env var is set. Timestamp server: `http://timestamp.digicert.com`, SHA-256. Signtool not in PATH → warning, build continues. Required for WACK certification and SmartScreen trust.
- **Diagnostics — Tick performance instrumentation** (`#if DEBUG`): `TimerCardViewModel.Tick()` measures wall time per tick with `Stopwatch`. Every 1 000 ticks it writes the worst-case duration to `DiagLog` if >5 ms. Provides observable data equivalent to PerfView's GC/JIT latency view without requiring a full profiling session. Zero overhead in Release builds.
- Version bumped to `2026.05.11.006`.

## 2026.05.11.002
- **Remove duplicate Calendars toolbar button**: `BtnCalendarSettings` removed from `MainWindow.xaml`. The ⚙ Settings button opens the combined dialog with the Calendar tab selectable at the top. `BtnCalendarSettings_Click` handler kept for programmatic use (e.g. deep-link from tray).
- **Calendar scan — Refresh always visible**: `BtnRefreshCalendars` moved out of `PnlDefaultCalendar` (collapsed on error) into a permanently-visible header row. Error message changed from "Please open Outlook and click Refresh" to "Could not read calendars. Click ↺ Refresh to try again." The scan already starts Outlook in the background via `Activator.CreateInstance` if not running.
- **Send email on reminder** — per-timer option in the Reminder settings panel: "Send email when reminder fires" checkbox (`ChkSendEmailOnReminder`). When checked, `TimerCardViewModel.FireReminder()` calls `EmailAlertService.SendReminderAlertAsync(name, remaining)` immediately after showing the lens popup and tray balloon. Email subject: `⏰ Reminder: <name> — <time> remaining`. Respects the global Email Alerts enabled flag and send mode. `TimerModel.SendEmailOnReminder` persisted in `TimerDto`.
- Version bumped to `2026.05.11.002`.

## 2026.05.11.001
- **Combined Settings window**: `AppSettingsDialog` and `CalendarSettingsDialog` merged into single `SettingsDialog` with three tab-button sections at the top — **⚙ Event Settings**, **📅 Calendar Settings**, **📧 Email Settings**. Both toolbar buttons open this window at the relevant tab. Window is fully resizable with resize grip.
- **Fix — OAuth Send Test used wrong mode**: `BtnTestEmail_Click` was hardcoding `index 1 → Outlook, else → MailKit`, ignoring index 2 (Microsoft OAuth). Fixed to use same switch expression as `CboEmailMode_SelectionChanged`; index 2 now correctly routes to `SendViaMicrosoftOAuthAsync`.
- **Embedded Azure client ID**: `MicrosoftOAuthSmtp.EmbeddedClientId` const replaces prefs lookup. Client ID TextBox removed from UI — users click **Sign in with Microsoft** only. `AppPrefs.MsOAuthClientId` kept in JSON for backward compat (ignored at runtime).
- Version bumped to `2026.05.11.001`.

## 2026.05.10.015
- **Feature — Microsoft Account OAuth2 email mode (Hotmail / Outlook.com / Live)**: Microsoft permanently disabled basic SMTP auth for all personal accounts. Added a third email mode "Microsoft Account (OAuth2)" that uses MSAL + MailKit XOAUTH2 on smtp-mail.outlook.com port 587. One-time browser sign-in flow; refresh token cached encrypted in prefs. New `Core/MicrosoftOAuthSmtp.cs` class with silent token renewal, sign-in, sign-out. New `PnlOAuthFields` panel in settings with Client ID field, setup instructions, Sign In / Sign Out buttons and signed-in account label. `Microsoft.Identity.Client 4.*` package added.
- **Requires one-time Azure App registration** (free, ~5 min): portal.azure.com → App registrations → New → Personal Microsoft accounts → Redirect: `http://localhost` → API: Office 365 Exchange Online → SMTP.Send → Authentication → Allow public client flows → copy client ID into settings.
- Version bumped to `2026.05.10.015`.

## 2026.05.10.014
- **SMTP error UX — personal vs business Microsoft accounts**: Added `IsPersonalMicrosoftAccount` helper; `5.7.139` catch now shows account-type-specific guidance. Personal accounts (`@hotmail`/`@outlook`/`@live`) are told to use `smtp-mail.outlook.com` (not `smtp.office365.com`) and that App Passwords require 2-step verification. Business accounts are told App Passwords don't bypass tenant policy and to ask their admin. SMTP host TextBox tooltip now lists correct hosts for all common providers.
- Version bumped to `2026.05.10.014`.

## 2026.05.10.013
- **Fix — Calendar & Email Settings window vertical resize**: `Window.Template` override was replacing the default WPF window chrome and suppressing OS edge-resize handling. Removed the template; resize grip triangle moved inline inside the main Grid with `IsHitTestVisible="False"` so it doesn't block the OS resize handle. Window now resizes vertically by dragging any edge.
- **Fix — Microsoft SMTP basic auth error**: MailKit catch block now detects `5.7.139 Authentication unsuccessful, basic authentication is disabled` and throws a user-readable message explaining Microsoft deprecated basic auth, with three actionable alternatives (Outlook mode, Gmail App Password, Microsoft App Password).
- **Fix — Outlook COM `CO_E_SERVER_EXEC_FAILURE`**: Replaced `Activator.CreateInstance` (fails across process-elevation boundaries) with `Process.Start(UseShellExecute=true)` + `GetActiveObject` poll (12 s timeout). Shell-launched processes run at normal user IL even from an elevated calling process, bypassing the elevation mismatch. If Outlook is already running, `GetActiveObject` is used directly with no launch delay.
- Version bumped to `2026.05.10.013`.

## 2026.05.10.012
- **Fix — Outlook email `CO_E_SERVER_EXEC_FAILURE` root cause**: The real cause was COM apartment mismatch, not "Outlook not open." `Task.Run` uses MTA thread-pool threads; Outlook's COM server is STA. `Activator.CreateInstance` and `GetActiveObject` both fail with 0x80080005 when called from MTA. Fixed by routing all Outlook COM work through dedicated STA threads via new `RunOnStaThread` / `RunOnStaThreadAsync` helpers. `SendAlertAsync` and `SendTestAsync` now use these instead of bare `Task.Run`. Calendar scanning was unaffected because `DispatcherTimer.Tick` already runs on the UI/STA thread.
- **`AcquireOutlook` restored**: Removed the blanket `try/catch` added in .011; background-launch via `Activator.CreateInstance` was working correctly on the STA dispatcher thread and should remain available.
- Version bumped to `2026.05.10.012`.

## 2026.05.10.011
- **Fix — "Outlook not running" shows clear message instead of raw COM exception**: Both calendar scan and email send now handle the case where Outlook is closed gracefully. `CalendarImporter.AcquireOutlook` wraps `Activator.CreateInstance` in try/catch and returns `(null, false)` on failure instead of throwing. Calendar settings shows "Please open Outlook and click Refresh." `EmailAlertService.SendViaOutlook` catches `CreateInstance` failure and throws `InvalidOperationException("Outlook is not running. Please open Outlook and try again.")` — the test button shows this message directly.
- Version bumped to `2026.05.10.011`.

## 2026.05.10.010
- **Fix — Outlook email `CO_E_SERVER_EXEC_FAILURE` (0x80080005)**: `SendViaOutlook` now uses `GetActiveObject` (P/Invoke `oleaut32.dll`) to attach to an already-running Outlook instance before falling back to `Activator.CreateInstance`. DCOM cold-launch of Outlook fails under UAC / restricted activation; connecting to the running process succeeds. Mail item and Application COM objects are properly released in a `finally` block; `Quit()` is only called when the app itself started Outlook.
- Version bumped to `2026.05.10.010`.

## 2026.05.10.009
- **Calendar settings window** — removed `SizeToContent="Height"` which blocked vertical manual resize; set explicit `Height="740"`, `MinHeight="420"`, `MaxHeight="1200"`. Calendar visibility list `MaxHeight` raised to `260`.
- **Birthday / Holiday filter checkboxes** — added `ChkHideBirthdays` and `ChkHideHolidays` in the Import Filters section of `CalendarSettingsDialog`. New `AppPrefs` fields `CalendarHideBirthdays` / `CalendarHideHolidays` (JSON-serialized). `RunCalendarPoll` skips events whose `CalendarName` contains "birthday" or "holiday"/"united states" when the respective filter is checked.
- **Gmail / "Calendar (This computer only)" fix** — `TryGetString` now uses `BindingFlags.Public | BindingFlags.Instance` in addition to `BindingFlags.GetProperty` to avoid silent failures on non-standard OST store types. New `GetFolderPath`/`GetFolderName` helpers try direct IDispatch (`folder.FolderPath`) first, fall back to `TryGetString`. Both `WalkFolderForCalendars` and `WalkFolderForEvents` now use these helpers plus a fallback key `"\\\\<Name>"` when `FolderPath` is empty, so local OST calendar folders (displayed as "Calendar (This computer only)" in Outlook) are no longer silently skipped. `SearchFolderRecursive` applies the same path resolution.
- Version bumped to `2026.05.10.009`.

## 2026.05.10.008
- **Calendar discovery — switch to `Namespace.Folders`**: All three COM paths (`CollectCalendarFolderMetadata`, `ReadAllCalendarsOneShot`, `FindFolderByPath`) now enumerate via `Namespace.Folders` instead of `Namespace.Stores`. `Namespace.Stores` silently omits Internet Calendar stores — the mechanism Outlook uses to surface Google Calendar / Gmail subscribed ICS feeds. `Namespace.Folders` returns the same root-folder view as Outlook's navigation pane, making all visible calendars (including `matthewlpittman@gmail.com`) reachable.
- `SearchFolderRecursive` COM ownership corrected: nulls out `sub` when it IS the found folder so the `finally { SafeRelease(sub) }` doesn't double-release it; releases intermediate parent folders safely since COM ref-counts are per-object.
- Version bumped to `2026.05.10.008`.

## 2026.05.10.007
- **UI — Delete button moved to action row**: `BtnDelete` (✕) relocated from the title row to the end of the action-buttons StackPanel (row 1). Title row is now collapse + name only — no third column.
- **Calendar discovery — recursive folder walk**: Fixed two root causes for missing calendars (Gmail, Jailtracker, etc.):
  - `GetDefaultFolder(9)` fails silently for IMAP/Google stores — now falls back to `GetRootFolder()` so the entire store folder tree is still walked.
  - Only one level of sub-folders was scanned — `Calendar\Other Calendars\Jailtracker` (depth 2+) was never reached. All three discovery paths (`CollectCalendarFolderMetadata`, `ReadAllCalendarsOneShot`, `FindFolderByPath`) now use a full recursive walk via new `WalkFolderForCalendars`, `WalkFolderForEvents`, and `SearchFolderRecursive` helpers.
- Version bumped to `2026.05.10.007`.

## 2026.05.10.006
- **Fix — Outlook blocked while app running**: `CalendarImporter.cs` completely rewritten to eliminate the COM lifetime bug where `Activator.CreateInstance("Outlook.Application")` started a background Outlook process that was never released, preventing the user from opening Outlook normally.
  - P/Invoke `oleaut32.dll!GetActiveObject` added — connects to an already-running Outlook instance instead of spawning a new one.
  - `AcquireOutlook()` — tries running instance first, falls back to `CreateInstance` only if Outlook is not open; returns `(app, weStarted)` flag.
  - `ReleaseOutlook(app, weStarted)` — calls `app.Quit()` only if we started the process; always calls `Marshal.ReleaseComObject` + `GC.Collect/WaitForPendingFinalizers`.
  - `SafeRelease(object)` helper wraps `Marshal.ReleaseComObject` with `try/catch`.
  - All methods (`GetOutlookCalendarFolders`, `ReadOutlookFolder`, `ReadAllCalendarsOneShot`) now open a fresh Outlook session per call and release every COM object (Application, Namespace, Stores, Folders, Items, AppointmentItems) in `try/finally` blocks. No COM objects escape any method boundary.
  - New `ReadAllCalendarsOneShot(hiddenPaths, maxItems, lookaheadDays)` — reads all visible calendars in a single Outlook session; used by `RunCalendarPoll` instead of the old two-phase approach.
- **`MainWindow.RunCalendarPoll`** updated: replaced old `GetOutlookCalendarFolders()` + per-folder `ReadOutlookFolder()` loop with single `CalendarImporter.ReadAllCalendarsOneShot(hidden, maxItems: 600, lookaheadDays)` call. Existing past/lookahead/keyword/dedup filters unchanged.
- Version bumped to `2026.05.10.006`.

## 2026.05.10.005
- **Root-cause fix — mode switch never worked**: `CboMode_SelectionChanged` existed but was never subscribed to the ComboBox's `SelectionChanged` event. Result: changing the mode combo NEVER updated `_vm.Mode`, so `PnlDateTimeInputs` (date picker + calendar import button) never became visible in Date/Time mode — it was always the h/m/s countdown inputs that showed. Fixed by adding `SelectionChanged="CboMode_SelectionChanged"` to `CboMode` in `TimerCard.xaml`. All downstream INPC logic (VM Mode setter → `RaiseUiStateChanged`) was correct; only the event wire was missing.
- Version bumped to `2026.05.10.005`.

## 2026.05.10.004
- **Calendar import filters** — new IMPORT FILTERS section in the 📅 Calendar & Email Settings dialog:
  - **Look-ahead window**: numeric field (1–365 days, default 30). Auto-pull and the manual picker now skip events starting beyond this window. Persisted as `CalendarLookaheadDays` in prefs.
  - **Exclude keywords**: comma-separated list (case-insensitive). Any event whose title contains one of these words is skipped during auto-pull. Empty = no filter. Persisted as `CalendarExcludeKeywords` in prefs.
- `RunCalendarPoll` applies both filters: past-event guard (from .003), then look-ahead cutoff, then keyword exclusion check.
- `AppPrefs.cs`, `JsonSerialization.cs` (PrefsDto + ToDto + ApplyDto + LegacyFallback) updated with the two new fields.
- Version bumped to `2026.05.10.004`.

## 2026.05.10.003
- **Calendar import button now visible in Date/Time mode**: root cause was that `CboMode_SelectionChanged` → `_vm.Mode` setter's early-return guard (`if (_model.Mode == value) return`) skipped `RaiseUiStateChanged()` when mode was unchanged (e.g., on card load). Two-part fix:
  - `TimerCardViewModel`: added `public void RefreshUiState()` (calls `RaiseUiStateChanged()`).
  - `TimerCard.xaml.cs` ctor: calls `_vm.RefreshUiState()` after `CboMode.SelectedIndex = ...` to guarantee all Visibility bindings reflect the loaded model.
  - `CboMode_SelectionChanged`: always calls `_vm.RefreshUiState()` after mode assignment so switching back to the same mode still re-evaluates panels.
- **Auto-pull future-only guard**: `RunCalendarPoll` now skips any event where `ev.StartLocal <= DateTime.Now` before dedup — prevents past all-day and normalised events from being imported.
- Version bumped to `2026.05.10.003`.

## 2026.05.10.002
- **Calendar integration**: new `📅 Calendars` button opens `CalendarSettingsDialog`.
  - **Default calendar**: set any Outlook calendar as the default; auto-selects it in the Import picker.
  - **Hide/show calendars**: per-calendar visibility toggle; hidden calendars are excluded from auto-pull and the picker dropdown.
  - **Auto-pull** (Option 1 — DispatcherTimer polling): scans visible Outlook calendars on a configurable interval (5–120 min, default 15). Deduplicates by timer name + target time to avoid creating duplicates. New events create timer cards automatically and trigger a tray notification.
  - Last-pulled timestamp displayed in dialog; "Pull Now" button for immediate sync.
- **Email alerts**: fires an email when any timer reaches the Done state.
  - **MailKit / SMTP mode**: works with any SMTP provider (Gmail, Outlook.com, custom). SMTP password stored AES-128 encrypted, keyed to username + machine name.
  - **Outlook COM mode**: no SMTP config needed — sends via the installed Outlook client.
  - "Send Test" button in dialog for immediate delivery validation.
- **XAML Visibility binding fixes** (`TimerCard.xaml`): `PnlDateTimeInputs` (calendar import button), `PnlInputs`, `PnlAlertButtons`, `ProgressTrack`, `PnlSettings`, `PnlAutoReset`, `PnlReminderSection`, `PnlReminderDetail`, `PnlCategoryColorOverride` — all were hardcoded `Collapsed`. Now bound to their ViewModel `Visibility` properties. Calendar import button is visible again when a timer is in Date/Time mode.
- `CalendarEventPickerDialog`: hidden calendars filtered from the picker combo; default calendar auto-selected when Outlook folders are enumerated.
- `MainWindow.xaml.cs`: added `System.Diagnostics` using.
- Version bumped to `2026.05.10.002`.

## 2026.05.10.001
- `AutomationProperties.HelpText` added to all sliders (`SldFontSize`, `SldSnooze`, `SldReminderLead`, `SldReminderInterval`) documenting arrow-key increment/decrement (SC 2.5.7 keyboard alternative).
- `AutomationProperties.HelpText` added to `CboMode`, `CboAlertStyle`, `CboSound`, `CboPreset`, `TxtFlashSeconds` for non-obvious behavior.
- `AutomationProperties.LiveSetting="Assertive"` set on "Time's up!" label in `FlashOverlayWindow` and `LensAlertWindow` — urgent alerts interrupt screen-reader speech immediately.
- Custom `AutomationPeer` classes: `TimerCardAutomationPeer` (`ControlType=Group`, exposes name + state), `FlashOverlayWindowAutomationPeer`, `LensPopupAutomationPeer`.
- `OnCreateAutomationPeer()` wired in `TimerCard`, `FlashOverlayWindow`, `LensPopupBase`.
- `TimerPersistence`: added `SaveAsync`/`LoadAsync` (non-blocking file I/O). GDPR audit confirmed: `Debug.WriteLine` logs only exception type, never PII.
- `MainWindow.xaml` `TimerPanel`: documented WrapPanel virtualization limitation; deferred to post-Accessibility-Insights pass.
- Version bumped to `2026.05.10.001`.

## 2026.05.09.001
- WCAG 2.2 AAA compliance pass: `AutomationProperties.Name` added to all unlabeled controls in `TimerCard`, overlay windows, and category chip buttons.
- `AutomationProperties.LiveSetting=Polite` on all countdown labels in `FlashOverlayWindow` and `LensPopupBase`.
- Window-level UIA identity (`Name` + `HelpText`) set on `FlashOverlayWindow`, `LensAlertWindow`, `LensReminderWindow`.
- `Themes/HighContrast.xaml` added (SystemColors-based); `App.xaml.cs` loads it on startup and handles runtime HC toggle.
- Privacy Notice added to About dialog (local storage disclosure, no telemetry, GDPR basis).
- `Docs/CCPA_NonApplicability.md` added (threshold analysis, review date 2027-01-01).
- Project reorganized into `Assets/`, `Core/`, `ViewModels/`, `Settings/`, `Windows/` subfolders.
- `Builds/` staging added to `build.bat`; `.gitignore`, `version.txt`, `CHANGELOG.md` created.

## 2026.05.07.001
- Initial tracked release.
