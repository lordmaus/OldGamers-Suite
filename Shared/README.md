# Shared — OldGamers.Wpf.Shared + suite themes

Code and theme data used by all six apps (BadgeOverlay, FileCopy, NoteStation, ReticleOverlay,
SuperMagnifierLV, TimerStudio). Change it here once; every app picks it up on its next build.

| Path | What | How apps use it |
|---|---|---|
| `OldGamers.Wpf.Shared/Controls/AccessibleResizeGrip.cs` | 44 px keyboard-accessible bottom-right resize grip (WCAG 2.5.7/2.5.8) | XAML `xmlns:sh="clr-namespace:OldGamers.Wpf.Shared.Controls;assembly=OldGamers.Wpf.Shared"` → `<sh:AccessibleResizeGrip/>` |
| `OldGamers.Wpf.Shared/Controls/KeyboardWindowMover.cs` | Alt+Arrow move / Ctrl+Alt+Arrow resize for borderless windows | `OldGamers.Wpf.Shared.Controls.KeyboardWindowMover.Attach(...)` |
| `OldGamers.Wpf.Shared/Controls/TypeRamp.cs` | Code-side `FontSizeCaption/Body/Title` | `OldGamers.Wpf.Shared.Controls.TypeRamp.Caption` |
| `OldGamers.Wpf.Shared/VersionInfo.cs` | "v2026.10.13.0001" from the app (entry) assembly | `VersionInfo.Display` |
| `OldGamers.Wpf.Shared/AppDialog.cs` | Themed MessageBox replacement, same overloads/results | `AppDialog.Show(...)` |
| `OldGamers.Wpf.Shared/InstanceActivation.cs` | Second launch brings the running window forward | `Listen(appId, ...)` / `SignalExisting(appId)` |
| `OldGamers.Wpf.Shared/Diagnostics/AppLog.cs` | App log: `<data dir>\logs\<App>.log` (+ `.old` at 512 KB), FileCopy daily files. Full exception, profile path → `%USERPROFILE%`, optional e-mail redaction, single-line messages, repeat merge, flood cap, buffered until configured | `static App() => AppLog.Configure(dataDir, "<App>")`; `AppLog.Info/Warn/Error(...)`; `AppLog.FilePath` for About buttons |
| `OldGamers.Wpf.Shared/Settings/AtomicFile.cs` | `AtomicFile.WriteAllText` (tmp + rename, no BOM); `CorruptFile.SetAside` (keep unreadable file as `name.corrupt-<date>.ext`) | direct |
| `OldGamers.Wpf.Shared/Settings/WindowSizeStore.cs` | Dialog sizes in `window-sizes.json` (atomic write) | each app's `WindowSizeCache` facade owns the path |
| `OldGamers.Wpf.Shared.Tests/` | xUnit tests for the above | `dotnet test` / CI |
| `Themes/*.xaml` (108) | Canonical theme palette (superset of every app's keys) | Linked into each app as `Themes\%(Filename)` (FileCopy: `src\Themes\`), so `Themes/X.xaml` URIs are unchanged |

Each app has `GlobalUsings.Shared.cs` (`global using OldGamers.Wpf.Shared;`).

Theme rules: `python3 Tools/check_theme_contrast.py --dir Shared/Themes [--fix]`, and per app
`python3 Tools/check_theme_contrast.py --app-dir <App>` (finds `Shared/Themes` automatically).
BadgeOverlay's style modules (`BadgeOverlay/Themes/_*.xaml`) stay in that app.
