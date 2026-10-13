# NoteStation

**Clipboard manager · Sticky notes overlay · Task manager**  
*Built for Low Vision users — fully customisable per-note font and color.*

Version `2026.09.09.0003` — .NET 10 · C# 14 · WPF · MVVM (partial) · JSON SourceGen. See `CHANGELOG.md` for release history.

---

## Features

### 📋 Clipboard Manager
- Auto-captures clipboard history (up to 500 entries)
- Pin important items so they stay at the top
- 5 quick-paste preset slots (Ctrl+1–5) populated from pinned items
- Cycle through recent clips with keyboard or mouse side buttons (XButton1/2)
- Optional Ctrl+Alt+Scroll to cycle clipboard
- Search/filter clipboard history

### 📝 Sticky Notes (Overlay)
- Each note is an independent floating overlay window
- **Per-note font family, font size, bold, italic** — fully independent
- **Per-note text color and background color** — 8 presets + custom hex
- Notes persist between sessions and reopen where you left them
- Pin notes on top of all windows (📌 button)
- Hide/show from main window list
- Resize by dragging bottom-right corner

### ✅ Task Manager
- Drag-to-reorder tasks
- Assign due dates and times
- Star important tasks
- Push tasks to **Timer Studio** for countdown alerts
- Quick-add task bar with optional date picker
- Search/filter tasks
- Complete/incomplete toggle
- Additional views: **Calendar** (month grid with due-date cells), **Focus Mode** (single-task distraction-free panel), **Matrix** (priority/urgency quadrant), and **Memoboard** — switch via the header's advanced-view toggles

---

## Appearance

130 built-in themes sharing one 13-brush contract (light, dark, and high-contrast variants), including **Editorial** — a navy/serif theme pairing Instrument Serif headings with Inter body text (falls back to Georgia/Segoe UI if those fonts aren't installed on your system). Per-tab content font size is independently adjustable (Clipboard/Notes/Tasks/Folders) via A-/A+ controls, separate from the app chrome.

---

## Low Vision Controls

Every sticky note has independent:
- **Font family**: choose from all installed Windows fonts
- **Font size**: 8–72pt slider
- **Bold / Italic** checkboxes
- **Text color**: 8 presets + custom hex input
- **Background color**: 8 presets (dark, cream, yellow, green, blue, pink, plum, salmon) + custom hex

---

## UI Map

| Row | Content |
|-----|---------|
| 1 | App name · ⌨ Hotkeys · ⚙ Settings · ◑ Theme · ℹ About · ⊞ UI Size |
| 2 | 📋 Clipboard \| 📝 Notes \| ✅ Tasks (NavRow) |
| 3 | Context-sensitive toolbar (paste/prev/next/pin/delete for Clipboard, new/show/hide for Notes, new/complete/push/delete for Tasks) |
| 4 | Main content panel (list, note grid, or task list) |
| 5 | Quick Paste presets (5 slots from pinned clips) · Resize grip |

---

## Hotkeys

| Action | Shortcut |
|--------|----------|
| New task (Tasks tab) | Ctrl+N |
| New note (Notes tab) | Ctrl+Shift+N |
| About / Help | F1 |
| Cycle UI size | Ctrl+= |
| Quick-paste slot 1–5 | Ctrl+1 … Ctrl+5 |
| Previous clipboard | XButton1 (mouse back button) |
| Cycle clipboard | Ctrl+Alt+Scroll (configurable) |

---

## Settings

**Mouse Button Assignments** — Settings → Mouse Button Assignments  
Assign XButton1 or XButton2 to previous/next clipboard. Optionally enable Ctrl+Alt+Scroll.

**TimerStudio Integration** — Settings → Timer Studio Integration  
Point to `%AppData%\TimerStudio\timers.json` to enable push-to-timer from task cards.

---

## Data storage

All data lives in `%AppData%\NoteStation\`:
- `prefs.json` — window geometry, settings
- `clipboard.json` — clipboard history (text only)
- `notes.json` — sticky note content and appearance
- `tasks.json` — task list

No network calls are made. No telemetry.

---

## Building

```bat
cd NoteStation
build.bat
```

Requires: **.NET 10 SDK**, optionally **NSIS 3.x** for the installer.

---

## WCAG AAA

- All interactive elements ≥ 44px touch target
- 7:1 contrast on all themes (text on background)
- 2px Gold focus ring (`FocusRingBrush`) on all focusable controls
- No color-only information
- Bottom-right drag handles on all resizable windows
- Sticky notes: fully customisable per-note font/color for Low Vision needs

---

*Part of the Old Gamers Software suite — [oldgamerssoftware.com](https://oldgamerssoftware.com/)*
