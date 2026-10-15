# Privacy Policy — NoteStation

_Last updated: 2026-10-12_

## Summary

NoteStation does **not** collect, store, or transmit any personal data to us or any third party. There is no telemetry, no analytics, and no network communication. Everything you capture stays on your computer.

## What the app stores

NoteStation is a clipboard manager, sticky-notes overlay, and task manager. It saves the following **locally** on your computer under:

```
%AppData%\NoteStation\
```

- Clipboard history you capture (text and images)
- Sticky notes and their formatting
- Tasks, categories, and saved views
- Preferences, hotkeys, theme, and note PIN / master passphrase as salted PBKDF2 hashes (if set)
- Clipboard images and screenshots: `%AppData%\NoteStation\clipimages\`
- Log `logs\NoteStation.log` (rotated at 512 KB to `.old`) — error type, message and full stack trace, plus short notices (e.g. a hotkey that could not be registered). Your Windows profile path is replaced by `%USERPROFILE%`; identical entries are merged and flooding is capped; never note, task, or clipboard content. Older versions wrote `errors.log`, `crash.log` and `cliptrim.log`; you can delete them.
- Automatic backups (`.nsbak`) in the backup folder you choose

These files never leave your machine.

## Clipboard data

Because NoteStation is a clipboard manager, it reads and retains clipboard contents that you copy while capture is active. This data is stored **only** in the local `%AppData%\NoteStation\` location and is never transmitted. You can clear clipboard history at any time from within the app, and you can pause capture whenever you wish.

**Password managers and private copies.** NoteStation does not record clipboard items that the copying app marks as private. It honours the same Windows signals as Windows Clipboard History: `ExcludeClipboardContentFromMonitorProcessing`, `CanIncludeInClipboardHistory` = 0, and `Clipboard Viewer Ignore` (used by KeePass, 1Password, Bitwarden and others). Versions before 2026-10-12 only checked the first of these, so if you used a password manager with an older version, review your clipboard history and delete any passwords it captured.

**Encryption at rest (optional).** Settings → Clipboard → "Encrypt clipboard history on this PC" encrypts saved clips and clipboard images with Windows Data Protection (DPAPI, your user account). Only your Windows account on this PC can read them. It is off by default. Backups (`.nsbak`) are not encrypted.

## Hidden notes

"Hide note" covers a note's content with a PIN prompt inside NoteStation. It is a privacy screen, **not encryption** — the note text is stored in the same local files as other notes.

## Screenshot OCR

The optional screenshot-to-text feature uses the **on-device** Windows OCR engine (Windows.Media.Ocr). Captured images and recognized text are processed locally and are never sent off the device.

## What the app does NOT do

- No data is sent to any server or third party.
- No usage statistics, crash reports, or identifiers are collected or transmitted.
- No advertising or tracking SDKs are included.

## Your control

Because all data is local, you are in full control. Clearing clipboard history removes captured items; uninstalling the app and deleting the `%AppData%\NoteStation\` folder removes everything.

## Contact

Publisher: Matthew Pittman
