# File Copy Studio

A WPF folder-to-folder copy/move utility with configurable conflict resolution, CRC verification, crash-safe resume, and built-in job scheduler.  
Version `2026.09.08.0002` — .NET 10 · C# 14 · WPF · MVVM · JSON SourceGen

---

## Features

| Feature | Detail |
|---|---|
| **Conflict resolution** | Overwrite, Overwrite-if-newer, Overwrite-if-older, Skip-if-same-date, Skip-all, Rename |
| **CRC32 verification** | Post-copy verify; standalone Verify Only mode; `.crc` sidecar option |
| **Crash-safe resume** | `.fctemp` + `.fctemp.meta` sidecar; survives power loss; auto-detected on next scan |
| **Parallel workers** | Auto-detects same vs. different volumes; configurable 1–8× threads |
| **Preserve timestamps & attributes** | Optional; respects NTFS extended attributes |
| **Delete source (Move)** | Confirmation prompt; safe — deletes only after verified copy |
| **Dry run** | Plans actions, shows file grid, writes nothing to disk |
| **Throttle** | Bandwidth cap in Mbps; per-session or global pref |
| **Symlink policy** | Follow, Skip, or Copy-as-link |
| **File masks** | Include/exclude semicolon-separated wildcards (`*.jpg;*.png`) |
| **Job scheduler** | Built-in cron-style scheduler; runs jobs unattended; per-job throttle/conflict options |
| **5 named presets** | Slot 1–5; persist paths + all options; one-click reload; Ctrl+1–5 |
| **Save/load job files** | `.fclist` JSON format; repeatable batch transfers |
| **Shell context menu** | Right-click folder/drive → "Copy with File Copy Studio"; auto-scans |
| **Portable mode** | `portable.flag` next to exe; prefs + logs in `Data\`; no registry; USB-ready |
| **Tray minimize** | Optional; balloon notification + system sound on completion |
| **Pause on battery** | Automatically pauses when on battery; resumes on AC |
| **WCAG 2.2 AAA** | 7:1 contrast ratios, 44px touch targets, 2px Gold/Cyan focus rings, live regions, glyph+color status |
| **Light / Dark / Auto themes** | Auto follows Windows accent; runtime switch; high-contrast passthrough |
| **Single instance** | Second launch forwards args to running instance via named pipe |

---

## UI Layout

```
┌─────────────────────────────────────────────────────────────┐
│ 1. Identity / Scale          File Copy Studio  [Standard 1×]│
├─────────────────────────────────────────────────────────────┤
│ 2. Nav row                                                  │
│    [📁 Source] [📂 Dest] [⇄ Swap] [🔍 Scan] [✓ Verify]     │
│    [▶ Start]   [⏸ Pause] [✖ Cancel] [🧪 Dry Run]            │
│    [📥 Load Job] [📤 Save Job] [🕐 Scheduler] [ℹ About]      │
├─────────────────────────────────────────────────────────────┤
│ 3. Path / Progress                                          │
│    Source  _________________________________ [Browse]       │
│    Dest    _________________________________ [Browse]       │
│    ██████████████░░░░░░  47%  [filename.ext]               │
│    ████░░░░░░░░░░░░░░░░  23%  total   1.2 GB/s             │
├─────────────────────────────────────────────────────────────┤
│ 4. Options / Sliders                                        │
│    On conflict: [Overwrite if newer ▼]                      │
│    ☑ Subfolders  ☑ Timestamps  ☑ Attributes  ☑ Verify CRC  │
│    ☐ Move        ☑ Resume      ☑ Auto-parallel              │
│    ☐ Tray        ☑ Sound       ☐ Pause on battery           │
│    Buffer ══════════════ 1024 KB    Parallel ══ 2×          │
│    Throttle ══ 0 Mbps (unlimited)                           │
│    Include [*.*]    Exclude []      Symlinks [Follow ▼]     │
│    12,847 files — Copy 8,201 · Overwrite 412 · Skip 4,234  │
├─────────────────────────────────────────────────────────────┤
│ 5. File grid (sortable, filterable, resizable columns)      │
│    Filter: [All ▼]   ⚠ ⊘ + ↻ … │ File │ Size │ Modified   │
│    ─────────────────────────────────────────────────────   │
│    Error log   ⚠ 2 errors  [📋 Copy errors] [🗑 Clear]      │
├─────────────────────────────────────────────────────────────┤
│ 6. Presets       [●1] [2] [3] [●4] [5]  [💾 Save Preset]   │
└─────────────────────────────────────────────────────────────┘
                                               ⠿ (resize grip)
```

---

## Keyboard Shortcuts

| Keys | Action |
|---|---|
| `Ctrl+S` | Scan |
| `F5` | Start copy |
| `Esc` | Cancel |
| `Ctrl+P` | Pause / Resume |
| `Ctrl+D` | Dry run |
| `Ctrl+V` (main window) | Verify only |
| `Ctrl+1` – `Ctrl+5` | Load preset slot |
| `Ctrl+Shift+1` – `Ctrl+Shift+5` | Save to preset slot |

---

## File Formats

### `.fclist` — Job file

JSON file saving paths + options + planned actions. Load with `📥 Load Job`, save with `📤 Save Job`. Clicking Scan after load refreshes planned actions against current files.

```jsonc
{
  "FormatVersion": "1",
  "CreatedUtc": "2026-05-09T12:00:00Z",
  "SourcePath": "D:\\Photos",
  "DestPath":   "E:\\Backup\\Photos",
  "ConflictMode": "OverwriteIfNewer",
  "IncludeSubdirs": true,
  "VerifyCrc": true,
  "Items": [
    { "RelativePath": "2024\\img_001.jpg", "SizeBytes": 4194304, "PlannedAction": "Copy" }
  ]
}
```

### `.fctemp` + `.fctemp.meta` — Resume markers

Written alongside the destination during a copy. If interrupted, the next scan detects them and resumes from the last 64 MB checkpoint.

**Do not delete `.fctemp` files manually** unless you want to start fresh — use right-click → `Discard partial` in the file grid context menu instead.

```
dest\large_video.mkv.fctemp       ← partial payload (raw bytes)
dest\large_video.mkv.fctemp.meta  ← JSON sidecar:
  { "SourceLength": 10737418240, "SourceMtimeUtcTicks": ...,
    "PartialCrc": 3735928559, "PartialOffset": 1073741824 }
```

If the source file changes between runs, the partial is discarded automatically and the copy restarts.

### `.crc` — CRC sidecar (optional)

When **Write CRC sidecar** is enabled, a `.crc` file is written alongside each destination file containing the CRC32 hash. Subsequent verify runs use these sidecars for fast integrity checks without re-reading the source.

---

## Scheduler

Open the scheduler with `🕐 Scheduler` or `Ctrl+Shift+S`. Jobs run in the background while the main window is visible or minimised to tray.

Each job stores its own source/dest paths, conflict mode, include/exclude masks, throttle, and schedule expression. Jobs can be enabled/disabled individually without deleting them.

Schedule expressions use standard 5-field cron syntax:

```
┌───────── minute  (0–59)
│ ┌─────── hour    (0–23)
│ │ ┌───── day of month (1–31)
│ │ │ ┌─── month  (1–12)
│ │ │ │ ┌─ day of week (0–7, Sun=0/7)
│ │ │ │ │
* * * * *
```

Examples: `0 2 * * *` = daily at 02:00 · `0 * * * *` = hourly · `30 18 * * 5` = Fridays at 18:30

---

## CLI / Shell Integration

```
FileCopyStudio.exe [--source <path>] [--dest <path>] [--scan]
FileCopyStudio.exe D:\MyFolder        # positional path treated as --source
```

The installer registers shell verbs for `Folder`, `Directory`, `Directory\Background`, and `Drive` — all pass `--source "%1" --scan` automatically.

If File Copy Studio is already running, the new instance forwards its arguments over a named pipe and exits; the running window activates and applies the args.

---

## Build

**Prerequisites:** .NET 10 SDK, NSIS 3.x (optional, for installer)

```bat
cd FileCopy
build.bat
```

Auto-increments version (`yyyy.MM.dd.NNNN`), patches `FileCopy.csproj` and `installer.nsi`, compiles a self-contained Release single-file exe, then calls `makensis installer.nsi` if NSIS is on PATH.

### Portable / USB build

```bat
cd FileCopy
build-portable.bat
```

Produces `FileCopyStudio_Portable_<version>.zip`:

```
FileCopyStudio.exe      ← self-contained, no .NET runtime needed
portable.flag           ← activates portable mode (delete to use %APPDATA%)
manual.pdf              ← user manual
Data\
  prefs.json
  logs\
```

Portable mode: prefs + logs stored in `Data\` next to the exe; no registry writes; title bar shows `[Portable]`.

---

## Tests

```bat
cd FileCopy.Tests
test.bat
```

Covers `PartialFile`, `DriveAffinity`, `CopyEngine.PlanActions`, `FcJob`, `SizeFormat`, and `AppPrefs`.

---

## Stack

`.NET 10` · `WPF` · `C# 14 (field keyword)` · `MVVM (MainWindowViewModel)` · `JSON Source Generation` · `System.IO.Hashing (CRC32)` · `System.IO.Pipes.AccessControl` · `Microsoft.Extensions.Logging`
