# Contributing to Badge Overlay

Thank you for taking the time to contribute. Badge Overlay is an
accessibility tool — every improvement helps real people with real
vision and motor needs.

## Before you start

- **Bug reports and feature requests:** open a
  [GitHub Issue](https://github.com/lordmaus/Badge-Overlay/issues)
  first so we can discuss before you invest time writing code.
- **Security vulnerabilities:** please read [SECURITY.md](SECURITY.md)
  and report privately rather than via a public issue.
- **Questions:** use GitHub Discussions rather than issues so Q&A
  doesn't clog the issue tracker.

## Stack

| Area | Tech |
|------|------|
| Language | C# 14 (`field`-backed auto-properties, collection expressions) |
| Runtime | .NET 10, WPF |
| Pattern | MVVM where applicable; partial-class splits for large files |
| Serialisation | `System.Text.Json` source generators (`AppSettingsJsonContext`) |
| Accessibility API | UI Automation (UIA), MSAA, Win32 `ButtonDetector` |
| Speech | System.Speech 9.0.0 (SAPI 5 wrapper) |
| Installer | NSIS 3.09+ |

## Setting up

```bash
# Prerequisites: .NET 10 SDK, Visual Studio 2022+ or Rider
git clone https://github.com/lordmaus/Badge-Overlay.git
cd Badge-Overlay/BadgeOverlay
dotnet build
dotnet publish -c Release -r win-x64 -p:PublishSingleFile=true -p:SelfContained=true
```

The zip file for a dev build:

```bash
cd ..
powershell -Command "Compress-Archive -Path BadgeOverlay,BadgeOverlay.sln,README.md,LICENSE,CHANGELOG.md,version.txt -DestinationPath BadgeOverlay_dev.zip -Force"
```

## Project layout

```
BadgeOverlay/
├── Native/          # NativeMethods partial classes (User32, Kernel32, Shcore, Common)
├── Settings/        # AppSettings partial classes (Badge, Behaviour, Hotkeys, Window)
├── Themes/          # ResourceDictionary theme files (_Buttons, _Inputs, _Indicators, Dark, Light, HighContrast)
├── Window/          # MainWindow partial classes (Tray, Hotkeys)
├── OverlayWindow    # Core 30 Hz badge rendering loop
├── UIADetector      # Background UIA worker
├── ButtonDetector   # Win32 classification (fast path)
├── MsaaDetector     # MSAA fallback
├── A11yAuditor      # Accessibility audit export
├── SessionStats     # In-memory detection counters
├── ScreenReaderAnnouncer # TTS via SAPI
└── installer.nsi    # NSIS installer script
```

## Code style

- **C# 14** — use `field` in auto-properties where it removes a backing field.
- **Partial classes** — each file under 500 lines. If you find yourself
  extending a class beyond that, split by concern.
- **No abbreviations in identifiers** — `detected` not `det`, `settings` not `s`
  (except `_s` which is the established project-wide convention for the
  live `AppSettings` instance).
- **XML doc comments on all public members.** Describe *why*, not *what*.
- **Accessibility first** — every new UI element needs:
  - `AutomationProperties.Name`
  - `AutomationProperties.HelpText` (different from ToolTip)
  - `MinHeight="44"` on interactive controls (WCAG 2.5.5)
  - A focus ring via `FocusRingBrush` dynamic resource
- **No raw `SetWinEventHook`** — use `WinEventHookManager.Subscribe()`.
- **No raw `SetWindowPos`** — go through the overlay window's existing
  tick-based positioning helpers.

## WCAG AAA target

Badge Overlay targets **WCAG 2.2 AAA** on all UI it controls. Before
opening a PR that adds or changes UI, verify:

| Criterion | Check |
|-----------|-------|
| 1.4.3 Contrast (AA)  | 4.5:1 minimum for text |
| 1.4.6 Contrast (AAA) | 7:1 for normal text |
| 2.5.5 Target Size    | 44 × 44 px for interactive controls |
| 2.4.7 Focus Visible  | 2 px `FocusRingBrush` ring on every control |
| 1.3.1 Info & Relationships | Radio groups in labelled GroupBoxes |
| 2.1.1 Keyboard       | Everything reachable by Tab + Enter/Space |

## Pull request checklist

- [ ] `dotnet build` passes with zero warnings.
- [ ] `python3 -c "import xml.etree.ElementTree as ET; [ET.parse(f) for f in __import__('glob').glob('BadgeOverlay/*.xaml')]"` passes (all XAML well-formed).
- [ ] New settings properties are added to `SanitiseDoubles()` in `AppSettings.cs` with appropriate clamping.
- [ ] New settings properties are serialised/deserialised correctly (check `AppSettingsJsonContext`).
- [ ] CHANGELOG.md updated under `## [Unreleased]`.
- [ ] No `bin/` or `obj/` directories in the commit.

## What we especially welcome

- Improved UIA / MSAA detection coverage for specific apps (Office,
  JetBrains IDEs, browsers).
- Screen-reader compatibility improvements (NVDA, JAWS, Narrator).
- Bug reports with repro steps for apps that BadgeOverlay misidentifies.
- Translations of UI strings (no i18n framework yet — raise an issue first
  to discuss the approach).
- Accessibility audit results from real users with visual / motor disabilities.

## What to avoid

- Adding NuGet dependencies without prior discussion — the project is
  intentionally minimal.
- Changing the badge rendering tick rate below 33 ms — it's tuned to
  avoid cursor-hook interference with YoloMouse.
- Storing user data outside `AppSettings` or outside
  `%APPDATA%\BadgeOverlay` (or the EXE folder in portable mode).
- Any feature that makes outbound network calls — the privacy promise
  ("zero telemetry, zero network") is a core commitment.

## Licence

By contributing you agree that your contributions will be licensed under
the same licence as the project (see [LICENSE](LICENSE)).
