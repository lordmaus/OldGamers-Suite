# Reticle Overlay

A lightweight .NET 4.8 WPF screen crosshair / reticle overlay for gaming. Stays on top of all
windows, is fully click-through, and supports multi-monitor setups with per-game profiles,
global hotkeys, and system-tray operation.

Version `2026.09.08.0001` — see `CHANGELOG.md` for release history.

## Features

**Reticle types**
- Crosshair, Dot, Circle, Cross+Dot, T-Shape — all fully configurable
- Custom PNG — use your own image as the reticle

**Appearance**
- Size (4–200 px) and Thickness (1–20 px) sliders
- Centre Gap slider (0–49 % of size) — configurable dead zone in the middle
- Rotation slider (0–359°) — rotate any reticle
- Primary color + separate Vertical Arm color (dual-color mode)
- Outline / shadow toggle for visibility on any background
- Opacity slider (5–100%) with +/− nudge buttons

**Positioning**
- Multi-monitor support — choose any connected display
- X / Y offset sliders (±400 px) for per-game crosshair calibration
- Fine-nudge ◄ ► ▲ ▼ buttons for single-pixel precision

**Profiles**
- Save, load, and delete named per-game profiles
- Export profiles to a JSON file for backup or sharing
- Import profiles from a JSON file (merges into current list, doesn't replace it)
- **Auto-switch by process** — set an optional "Auto-switch .exe" per profile; when enabled in Settings, the foreground process is polled every 2s and the matching profile applies automatically

**Hotkeys** (work globally while gaming)
- Toggle overlay on/off (default: F8)
- Cycle to next profile (default: F9)
- Cycle to previous profile (default: F10)
- Opacity + / − (configurable, blank to disable)
- All hotkeys are fully user-configurable in the app

**System tray**
- Minimize to tray when the control window is closed
- Tray icon: double-click to restore, right-click for quick menu
- Balloon tip notifications when profiles are switched via hotkey

**Other**
- Dark / Light theme toggle
- Live preview panel with Dark / Light / Checker backgrounds
- Start with Windows option (registry Run key)
- Settings saved atomically to `%AppData%\ReticleOverlay\prefs.json`
- Profiles saved to `%AppData%\ReticleOverlay\profiles.json`
- Single-instance guard, per-monitor DPI-aware, crash-safe writes
- **Live display-change recovery** — monitor unplug/replug, resolution, or DPI changes automatically re-position and re-clamp the overlay instead of leaving it stale
- **Stays on top of fullscreen games** — a ~1 Hz topmost re-assertion timer recovers the reticle after exclusive-fullscreen games steal the z-order
- App-wide crash logging with a best-effort settings flush before exit, for diagnosing unexpected shutdowns

## Requirements

- Windows 10 or 11 (also works on Windows 7/8.1)
- [.NET Framework 4.8](https://dotnet.microsoft.com/en-us/download/dotnet-framework/net48)
  (pre-installed on Windows 10+)

## Build

Requires the [.NET SDK](https://dotnet.microsoft.com/en-us/download) targeting net48.

```
cd ReticleOverlay\ReticleOverlay
build.bat
```

Output: `bin\Release\net48\ReticleOverlay.exe`

## Default Hotkeys

| Action         | Default key |
|----------------|-------------|
| Toggle overlay | F8          |
| Next profile   | F9          |
| Prev profile   | F10         |
| Opacity +      | (none)      |
| Opacity −      | (none)      |

All hotkeys can be changed in the **Hotkeys** section of the control window.
Format: `F8`, `Ctrl+F8`, `Alt+Shift+F9`, etc.

## Usage

1. Launch `ReticleOverlay.exe` — the control window opens and the overlay appears immediately.
2. Pick a **Reticle Type** from the dropdown. Choose *Custom PNG* to browse for your own image.
3. Adjust **Size**, **Thickness**, **Gap**, and **Rotation** with the sliders.
4. Click a colour swatch (or type a hex value) to change the reticle or outline colour.
5. Enable **Dual arm color** to use a different colour for the vertical crosshair arms.
6. Pick which **Monitor** to show the overlay on.
7. Drag the **X / Y Offset** sliders to align the reticle to your game. Use the nudge buttons
   for single-pixel precision.
8. Click **Save As…** to store the current settings as a named profile, then load it later
   with the **Load** button or the **F9 / F10** hotkeys.
9. Click **Save Settings** to persist the active settings to disk.
10. Closing the control window minimizes to the system tray (if that option is checked).
    Double-click the tray icon to restore, or right-click for the quick menu.

## Notes

- The overlay is fully **click-through** — it never intercepts mouse input in games.
- It is excluded from Alt+Tab and the taskbar.
- Settings are written atomically (`.tmp` rename) — a crash never corrupts your config.
- Export / Import profiles are available in the **About** dialog.
- If a saved file can't be read it is kept as `*.corrupt-<date>.json` in `%AppData%\ReticleOverlay\` and you are told at startup; errors go to `crash.log` there (About → Open crash log folder).
- If a hotkey is already used by a game or another app, the Hotkeys dialog says which one — pick a different key.

## FAQ — anti-cheat

Reticle Overlay is a separate, click-through, top-most window. It does not read or write game memory, inject DLLs, or hook DirectX/Vulkan. Some anti-cheat systems (e.g. Easy Anti-Cheat, BattlEye, Riot Vanguard) or tournament rules may still treat **any** crosshair overlay as an unfair advantage. Check the game's rules before using it in competitive or ranked play; you use it at your own risk.
