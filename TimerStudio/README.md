# ⏱ Timer Studio

**Multi-timer Windows desktop application — countdown, stopwatch, date/time targets, calendar import, and customisable alerts.**

---

## Requirements to Build

| Tool | Where |
|---|---|
| .NET SDK 6 or later | https://dotnet.microsoft.com/download |
| Windows 10 or 11 (64-bit) | — |
| NSIS 3.x *(optional, for installer)* | https://nsis.sourceforge.io/ |

> The compiled exe requires the **.NET 10 Desktop Runtime** for Windows. Download from https://dotnet.microsoft.com/en-us/download/dotnet/10.0

---

## How to Build

```
cd TimerStudio
build.bat
```

Or directly: `dotnet build -c Release`

Output: `bin\Release\net10.0-windows\TimerStudio.exe`

`build.bat` will also run `makensis installer.nsi` automatically if NSIS is on the PATH, producing `TimerStudio_Setup.exe`.

---

## Features

### Timer Modes
| Mode | Description |
|---|---|
| **Countdown** | Set hours / minutes / seconds; alert fires at zero |
| **Stopwatch** | Counts up with centisecond precision; paused state persists across restarts |
| **Date / Time** | Count down to a specific date and time; supports early reminders |

### Alerts
| Feature | Details |
|---|---|
| Alert color | 16-color palette per timer (8 pastels + 8 vivid) |
| Alert title color | 12-color palette for the name text inside the alert |
| Alert style | Lens (small floating popup), Fullscreen flash, or Both |
| Alert sounds | Beep, Chime, Alarm, Bell, Ping, Silent — with Preview button |
| Alert text size | Adjustable font size (12–60 pt) per timer |
| Auto-dismiss | Configurable timeout (0 = manual dismiss only) |
| Auto-reset | Restart countdown automatically after each alert |
| Dismiss / Escape | Click alert popup or press Escape to dismiss |

### Early Reminders (Date/Time mode)
| Feature | Details |
|---|---|
| Lead time | Set a reminder up to 72 hours before the target |
| Repeat interval | Repeat the reminder every N minutes between lead time and target |
| h/m input | Enter hours and minutes directly, or use the slider |
| VALARM auto-import | Reminder lead time is auto-populated from ICS VALARM data |

### Calendar Import
| Feature | Details |
|---|---|
| ICS file | Import events from any calendar app (Outlook, Google, Apple, etc.) |
| Outlook direct | Read upcoming events directly from Outlook desktop via COM |
| RRULE expansion | Recurring events (daily/weekly/monthly/yearly) are fully expanded |
| Location field | Event location shown in picker and auto-imported into Notes |
| VALARM | Pre-event alarm triggers auto-populate the reminder lead time |
| Multi-select | Select multiple events at once; each creates its own timer card |
| All-day events | Imported at 9:00 AM on the event date (adjustable after import) |

### Per-Timer Notes
Each timer card has a collapsible **📝 Notes** panel for free-text notes or descriptions. Calendar imports auto-populate from the event's DESCRIPTION and LOCATION fields. Notes persist across restarts.

### Quick Presets
A **Quick preset** dropdown in the alert settings applies common configurations instantly:
Pomodoro (25 min), Short break (5 min), Long break (15 min), 1 hour, End of day (5:00 PM), Midnight.

### Window & UX
| Feature | Details |
|---|---|
| Multiple timers | Run unlimited timers simultaneously |
| Drag to reorder | Drag a card's header row to change its position |
| Duplicate timer | ⧉ button copies all settings to a new card |
| Collapsible cards | ▾ collapse individual cards to save space |
| Persistent timers | All timers and settings saved automatically, restored on restart |
| Export / Import | Back up timers to JSON; import on any machine |
| Always on top | ☆ Pin keeps the window above all other windows |
| Hide / Show | ⊘ Hide collapses all cards; alerts still show a banner |
| Dark / Light theme | ◑ Theme toggles between dark and light mode |
| System tray | Closing minimises to tray; timers keep running |
| Tray reminders | Balloon tooltip fires when an early reminder triggers |
| Taskbar progress | Windows taskbar button shows countdown progress and turns red on alert |
| Start with Windows | Optional startup entry in Windows login sequence |
| Window position | Position and size persist across restarts; clamped on monitor disconnect |
| Ctrl+N | Global shortcut to add a new timer |

### Accessibility
- Full `AutomationProperties.Name` and `HelpText` on every interactive control
- WCAG 1.4.3 AA contrast met by both dark and light themes (body text ≥ 7:1)
- WCAG 2.5.5 touch target minimum 44×44px on all position grid buttons
- Keyboard navigation: Escape dismisses alerts and cancels name edits; Enter commits names
- Windows Reduce Motion respected: pulse animation suppressed when animations are off
- Windows High Contrast: automatically switches to light theme when HC is active

---

## Project Structure

| File | Purpose |
|---|---|
| `App.xaml / .cs` | WPF entry point; theme switching; single-instance guard; High Contrast detection |
| `MainWindow.xaml / .cs` | Main window; toolbar; tray; global tick; drag-to-reorder; taskbar progress |
| `TimerCard.xaml / .cs` | UserControl per timer; all state machine, alert, reminder, preset, notes logic |
| `TimerModel.cs` | Data model: all fields, state machine, display formatting, progress |
| `TimerPersistence.cs` | Atomic JSON save/load; self-test; stopwatch restore |
| `AppPrefs.cs` | Window geometry and theme persistence |
| `LensPopupBase.cs` | Shared base class for all floating alert/reminder popups |
| `LensAlertWindow.cs` | Final "Time's up!" lens popup |
| `LensReminderWindow.cs` | Early reminder lens popup |
| `FlashOverlayWindow.cs` | Fullscreen alert overlay |
| `AlertSoundPlayer.cs` | Windows Beep API tones — zero external audio dependencies |
| `CalendarImporter.cs` | ICS parser (RFC 5545, RRULE, VALARM, LOCATION) + Outlook COM reader |
| `CalendarEventPickerDialog.xaml / .cs` | Calendar event picker with checkbox multi-select |
| `AboutDialog.xaml / .cs` | Version info, startup toggle, Export/Import |
| `WhatsNewDialog.xaml / .cs` | What's New shown once per version |
| `Themes/Dark.xaml` | Dark colour tokens (Catppuccin Mocha–inspired) |
| `Themes/Light.xaml` | Light colour tokens |
| `app.manifest` | DPI awareness (PerMonitorV2) + UAC |
| `installer.nsi` | NSIS installer script (requires NSIS 3.x) |

---

## Settings Files

| File | Location |
|---|---|
| Timers | `%AppData%\TimerStudio\timers.json` |
| Preferences | `%AppData%\TimerStudio\prefs.json` |
| Version seen | `%AppData%\TimerStudio\seen_version.txt` |
| Timers backup | `%AppData%\TimerStudio\timers.json.bak` |

---

## Version

**2026.10.12.0002** — Windows · WPF · .NET 10

Created by Matthew Pittman
