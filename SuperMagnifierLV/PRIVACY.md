# Privacy Policy — Super Magnifier LV

_Last updated: 2026-10-13_

## Summary

Super Magnifier LV does **not** collect, store, or transmit any personal data. There is no telemetry, no analytics, and no network communication.

## What the app stores

The app saves your preferences (zoom level, lens size and position, color filters, hotkeys, theme, and accessibility settings) **locally** on your computer under:

```
%AppData%\SuperMagnifierLV\
```

Also in that folder:

- `lens_presets.json` — your five lens presets
- `logs\SuperMagnifierLV.log` (rotated at 512 KB to `.old`) — error type, message and full stack trace, plus short notices (e.g. a hotkey that could not be registered). Your Windows profile path is replaced by `%USERPROFILE%`; identical entries are merged and flooding is capped, and settings load/save notices; never screen content, window titles or text under the lens. Older versions wrote `crash.log` and `magnifier_error.log`; you can delete them.

These files never leave your machine.

## Screen magnification

Super Magnifier LV magnifies your screen in real time using the Windows Magnification API. Screen contents are rendered on-device only and are never transmitted.

**Snapshots.** Only when you press the lens Snapshot button, the magnified area is saved as a PNG to `Pictures\Super Magnifier LV\`. Nothing is captured automatically.

## What the app does NOT do

- No data is sent to any server or third party.
- No usage statistics, crash reports, or identifiers are collected or transmitted.
- No advertising or tracking SDKs are included.

## Your control

Because all data is local, you are in full control. Uninstalling the app and deleting the `%AppData%\SuperMagnifierLV\` folder removes everything.

## Contact

Publisher: Matthew Pittman
