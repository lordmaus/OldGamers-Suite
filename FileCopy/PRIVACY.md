# Privacy Policy — File Copy Studio

_Last updated: 2026-10-13_

## Summary

File Copy Studio does **not** collect, store, or transmit any personal data. There is no telemetry, no analytics, and no network communication.

## What the app stores

The app saves your preferences (source/destination folders, conflict-resolution rules, scheduled jobs, hotkeys, theme, and accessibility settings) **locally** on your computer under:

```
%AppData%\File Copy Studio\
```

These files never leave your machine.

## Log files

Activity logs are written to `%AppData%\File Copy Studio\logs\` (portable mode: `Data\logs\` beside the exe):

- `filecopy-studio-<date>.log` — one file per day; scans, copies, skipped folders, errors and crash details. Entries include **full source and destination paths**, which can contain your Windows user name and folder names.
- `scheduler-job-<id>.log` — per scheduled job run history.
- Daily logs older than 30 days are deleted automatically on startup. Release builds log at Information level and above.

Logs stay on your computer. Delete the `logs` folder at any time; the app recreates it.

## File access

File Copy Studio reads and writes files **only** in the folders you explicitly select for a copy or move operation, and computes CRC32 checksums locally to verify transfers. File contents are never uploaded, indexed off-device, or transmitted anywhere. All file operations stay on your local machine and any drives you choose.

## What the app does NOT do

- No data is sent to any server or third party.
- No usage statistics, crash reports, or identifiers are collected or transmitted.
- No advertising or tracking SDKs are included.

## Your control

Because all data is local, you are in full control. Uninstalling the app and deleting the `%AppData%\File Copy Studio\` folder removes all stored settings.

## Contact

Publisher: Matthew Pittman
