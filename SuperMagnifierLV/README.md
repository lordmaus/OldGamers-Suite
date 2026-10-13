# 🔍 Super Magnifier LV

**Screen magnifier for Windows — built for low vision users.**

Uses the Windows Magnification API for smooth, real-time magnification that works with DirectX games and standard desktop apps.

Current version: **2026.09.08.0002** · Target: **.NET 10** · See `CHANGELOG.md` for release history.

---

## Requirements to Build

| Tool | Where |
|---|---|
| .NET SDK 10 or later | https://dotnet.microsoft.com/download |
| Windows 10 or 11 | — |

> The **output exe** targets .NET 10. Publish self-contained to bundle the runtime:
> `dotnet publish -c Release -r win-x64 /p:PublishSingleFile=true /p:SelfContained=true`  
> End users need no extra runtime when published self-contained.

---

## How to Build

```
cd SuperMagnifier_build
dotnet build -c Release
```

Output: `bin\Release\net10.0-windows\SuperMagnifierLV.exe`

---

## Features

### Magnification
| Feature | Details |
|---|---|
| Zoom range | 1.25× to 20× |
| Zoom step | 0.1×, 0.25×, 0.5×, 1×, or 2× per click |
| Zoom preset buttons | Quick-access 2×, 3×, 4×, 6×, 8×, 12× buttons |
| Zoom controls | ▲▼ buttons, slider, Ctrl+= / Ctrl+- |
| Smooth animation | Ease-in-out zoom transitions (toggleable in Settings) |
| Fine zoom mode | Ctrl+G — temporarily halves the zoom step |
| Home zoom | Ctrl+0 — returns to a saved default zoom level |
| Zoom lock | Clamp zoom between a configurable min and max |
| Per-monitor zoom | Each display remembers its own zoom level |

### Lens Window
| Feature | Details |
|---|---|
| Resizable | Drag any edge or corner |
| Draggable | Drag the title bar |
| Always on Top | Stays above games and other apps |
| No focus steal | Never takes keyboard or mouse focus from other apps |
| Live zoom in title | Title bar shows the current magnification level |
| Title bar sizes | 5 sizes (26–80 px) — cycle with the Aa button |
| Lens shapes | Rectangle (fixed), Circle (follows cursor), Square (follows cursor) |
| Opacity control | Ctrl+[ / Ctrl+] — fade the lens in or out |
| Opacity follows zoom | Lens fades automatically at lower zoom levels (optional) |
| Freeze | Ctrl+F — freeze the current frame |
| Click-through | Ctrl+T — pass mouse clicks to the window below |
| Snapshot | Save the current lens view as a PNG to the Pictures folder |
| Mirror lens | Duplicate lens window on a second monitor |
| Dock to corner | Snap the lens to any screen corner via the Lens panel |
| Dim when idle | Lens fades after the cursor is still; restores on movement |
| Keyboard resize | Ctrl+Shift+Arrow — grow or shrink the lens by 20 px |
| Off-screen recovery | Lens and cursor-highlighter overlay automatically re-clamp to visible screen area the moment a monitor is added, removed, or its resolution/DPI changes — not just at startup |

### Follow Modes
| Feature | Details |
|---|---|
| Follow cursor | Lens tracks the mouse in real time |
| Follow caret | Lens tracks the text insertion point when typing |
| Snap-to-Word | Centre the lens on the current word under the cursor |
| Edge dead zone | Cursor must reach a screen edge before the lens pans |
| Smooth panning | Lens glides to follow — adjustable lag in Settings |

### Colour & Rendering
| Feature | Details |
|---|---|
| Rendering modes | Smooth (bilinear) or Crisp (nearest-neighbour) |
| Colour filters | Normal, Inverted, Grayscale, High Contrast, Deuteranopia, Protanopia, Tritanopia, Warm Amber, Cool Blue |
| Colour tint | Adjustable tint intensity for Warm Amber and Cool Blue |
| Colour cycle | Ctrl+Shift+C — cycles through all colour filters |
| Colour picker | Click inside the lens to sample any pixel's hex value |
| Colour history | Last 10 picked colours saved and shown as swatches |

### Overlays
| Feature | Details |
|---|---|
| Reading ruler | Horizontal tinted bar that tracks across the screen |
| Reticle | Cross, X, or circle drawn over the cursor position in the lens |
| Cursor highlighter | Coloured ring drawn around the cursor |
| Cursor finder | Ctrl+P — animated expanding rings to locate the cursor |
| Focus ring | Ring drawn around the keyboard-focused UI element (colour configurable) |

### Presets
| Feature | Details |
|---|---|
| 5 named presets | Store zoom, lens size, colour filter, opacity, follow mode, snap-to-word, reticle, and zoom lock |
| Load hotkeys | Ctrl+1 … Ctrl+5 |
| Save hotkeys | Ctrl+Shift+1 … Ctrl+Shift+5 |
| Tray quick-load | Right-click tray icon → Load Preset submenu |
| Per-app auto-presets | Automatically switch preset when a specific application becomes active |
| Export / import | Share presets as JSON files independently of other settings |

### Position Memory
| Feature | Details |
|---|---|
| Per-monitor zoom | Each display remembers its own zoom level |
| Per-monitor position | Lens restores to the right position on each monitor |
| Per-app position | Lens position remembered per application |
| Session restore | Restores zoom, active state, and position from the previous session |

### Settings
| Feature | Details |
|---|---|
| Settings dialog | Two-column layout with inline validation |
| Startup defaults | Set default follow mode, snap-to-word, reticle, UI size, and lens title bar size |
| Export / import | Save and load all settings as a JSON file |
| Reset to defaults | One-click factory reset in the Settings dialog |
| Atomic save | Write-to-temp-then-rename prevents corrupt settings on crash or power loss |
| Settings location | `%AppData%\Roaming\SuperMagnifierLV\magnifier_settings.json` |

### Hotkeys
| Feature | Details |
|---|---|
| Fully remappable | All hotkeys customisable via the Hotkeys dialog |
| Remap dialog | Shows all actions with current bindings; preset slots shown read-only |
| Default bindings | See hotkey table below |

### Accessibility
| Feature | Details |
|---|---|
| Screen reader support | All controls have accessible names and help text |
| WCAG 1.4.3 | Disabled state tokens; muted text meets ≥4.5:1 contrast |
| WCAG 2.1.1 | Lens toolbar fully Tab-navigable |
| WCAG 2.1.2 | Escape closes all panels and dialogs — no keyboard traps |
| WCAG 2.2.1 | Status toasts dismissible by click; extended on reduced-motion systems |
| WCAG 2.3.3 | All animations respect Windows "Show animations in Windows" preference |
| WCAG 2.4.7 | Keyboard focus rings on all interactive controls |
| WCAG 2.4.11 | Focus ring colour adapts to dark/light theme for ≥3:1 contrast |
| WCAG 2.5.5 | All buttons meet 44×44 px minimum touch target |
| WCAG 3.3.1 | Inline validation errors shown as fields change in Settings |
| WCAG 4.1.2 | Colour swatches are buttons with accessible role and keyboard activation |
| WCAG 4.1.3 | Status toasts announced to screen readers as live regions |
| High Contrast | Automatically adapts when Windows High Contrast mode is active |
| Reduce Motion | Skips animations when Windows "Show animations" is off |
| System font scale | Responds to Windows "Make text bigger" accessibility setting |
| F1 shortcut | Opens the About dialog from anywhere in the control panel |

### System Integration
| Feature | Details |
|---|---|
| System tray | Minimize to tray; right-click for quick actions |
| Autostart | Optional Windows startup registry entry |
| Single instance | Second launch shows a message instead of opening a second window |
| Sleep/wake recovery | Automatically reinitialises the magnification engine after system wake |
| What's New dialog | Shows changelog once per version after an update |
| First-run wizard | Guided introduction for new users |

---

## Project Structure

| File | Purpose |
|---|---|
| `App.xaml/.cs` | WPF entry point; single-instance mutex; session-ending save |
| `MainWindow.xaml/.cs` | Main control panel — all UI sections and hotkey wiring |
| `MainWindow.Zoom.cs` | Zoom logic partial class |
| `MainWindow.Hotkeys.cs` | Hotkey registration partial class |
| `LensWindow.xaml/.cs` | Floating magnifier overlay; toolbar; colour picker; snapshot |
| `MirrorLensWindow.cs` | Second read-only lens window |
| `ReticleOverlayWindow.xaml/.cs` | Reticle and cursor-dot overlay |
| `SettingsDialog.xaml/.cs` | Settings modal — two-column layout; inline validation |
| `HotkeyRemapDialog.xaml/.cs` | Remap all hotkeys; preset slots shown read-only |
| `AboutDialog.xaml/.cs` | Version, settings path, donate links, README button |
| `WhatsNewDialog.xaml/.cs` | Changelog shown once per version after update |
| `FirstRunWizard.xaml/.cs` | New-user onboarding wizard |
| `MagnifierEngine.cs` | Win32 Magnification API wrapper |
| `AppSettings.cs` | JSON settings persistence — hand-rolled parser, no dependencies |
| `HotkeyBinding.cs` | Remappable hotkey data, defaults, and display labels |
| `GlobalHotkey.cs` | RegisterHotKey / UnregisterHotKey wrapper |
| `CaretTracker.cs` | UIAutomation caret position tracking |
| `WordTracker.cs` | MSAA word boundary snapping (async, 200 ms timeout) |
| `CursorHighlighter.cs` | Coloured ring overlay around the cursor |
| `CursorPulse.cs` | Animated pulse effect overlay |
| `ReadingRuler.cs` | Horizontal reading guide overlay |
| `LensPreset.cs` | Preset load/save and JSON serialisation |
| `AppPresetMap.cs` | Per-application automatic preset switching |
| `AppPositionMap.cs` | Per-application lens position memory |
| `LensKeyboardMover.cs` | Arrow-key lens movement in click-through mode |
| `PerMonitorZoomMap.cs` | Per-monitor zoom memory serialisation |
| `MonitorHelper.cs` | DPI-aware monitor and position resolution |
| `NativeMethods.cs` | All P/Invoke declarations |
| `app.manifest` | DPI awareness and UAC level |

---

## Settings File

Settings are stored at:

```
%AppData%\Roaming\SuperMagnifierLV\magnifier_settings.json
```

Edit by hand or use the ⚙ Settings dialog. The file path is shown in the About dialog with a one-click copy button.

---

## Hotkeys (defaults — all remappable except preset slots)

| Action | Default |
|---|---|
| Toggle magnifier | Ctrl+M |
| Freeze / unfreeze | Ctrl+F |
| Zoom in | Ctrl+= |
| Zoom out | Ctrl+- |
| Click-through | Ctrl+T |
| Snap to cursor | Ctrl+Home |
| Cursor finder pulse | Ctrl+P |
| Home zoom | Ctrl+0 |
| Fine zoom toggle | Ctrl+G |
| Opacity down | Ctrl+[ |
| Opacity up | Ctrl+] |
| Toggle follow mode | Ctrl+L |
| Toggle reticle | Ctrl+R |
| Cycle colour filter | Ctrl+Shift+C |
| Load preset 1–5 | Ctrl+1 … Ctrl+5 |
| Save preset 1–5 | Ctrl+Shift+1 … Ctrl+Shift+5 |
| Resize lens up | Ctrl+Shift+Up |
| Resize lens down | Ctrl+Shift+Down |
| Resize lens left | Ctrl+Shift+Left |
| Resize lens right | Ctrl+Shift+Right |

---

## Tips for Games

1. Run your game in **Borderless Windowed** mode.
2. Enable **Always on Top** in Super Magnifier LV.
3. Press **Ctrl+M** — the lens will appear.
4. Drag the lens to position; resize by dragging any edge or corner.
5. The lens never steals keyboard or mouse focus from your game.

---

## Notes

- **YoloMouse / custom cursor tools:** The lens window intercepts `WM_SETCURSOR` to hide the system cursor inside the lens. If you use a cursor-replacement tool, uncheck **Hide cursor inside lens** in Settings → Zoom & Animation.
- **Colour filter + tint:** Tint intensity only applies to Warm Amber and Cool Blue modes.
- **Snap-to-Word** uses MSAA on a background thread with a 200 ms timeout so it never blocks the UI.
