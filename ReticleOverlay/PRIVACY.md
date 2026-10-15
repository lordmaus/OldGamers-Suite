# Privacy Policy — Reticle Overlay

_Last updated: 2026-10-13_

## Summary

Reticle Overlay does **not** collect, store, or transmit any personal data. There is no telemetry, no analytics, and no network communication.

## What the app stores

The app saves your preferences (crosshair styles, per-game profiles, global hotkeys, process auto-switch rules, theme, and accessibility settings) **locally** on your computer under:

```
%AppData%\ReticleOverlay\
```

These files never leave your machine.

- `logs\ReticleOverlay.log` (rotated at 512 KB to `.old`) — error type, message and full stack trace, plus short notices (e.g. a hotkey that could not be registered). Your Windows profile path is replaced by `%USERPROFILE%`; identical entries are merged and flooding is capped. No screen content, window titles or file contents. Older versions wrote `crash.log`; you can delete it.
- `profiles.corrupt-<date>.json` / `prefs.corrupt-<date>.json` — kept only if a saved file could not be read, so you can recover it; delete them any time.

## Process detection

To auto-switch crosshair profiles, Reticle Overlay reads the name of the currently focused window/process locally. This information is used only to select the matching profile on your machine and is never recorded or transmitted.

## What the app does NOT do

- No data is sent to any server or third party.
- No usage statistics, crash reports, or identifiers are collected or transmitted.
- No advertising or tracking SDKs are included.

## Your control

Because all data is local, you are in full control. Uninstalling the app and deleting the `%AppData%\ReticleOverlay\` folder removes everything.

## Contact

Publisher: Matthew Pittman
