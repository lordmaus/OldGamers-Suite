# Security Policy

## Supported Versions

Only the latest release receives security fixes.

| Version        | Supported |
| -------------- | --------- |
| Latest release | ✅        |
| Older builds   | ❌        |

## Reporting a Vulnerability

**Please do not open a public GitHub issue for security vulnerabilities.**

Report privately to: **matthewlpittman@gmail.com**

Include:
- Description of the vulnerability
- Steps to reproduce
- Potential impact
- Any suggested fix (optional)

You can expect an acknowledgement within 48 hours and a resolution timeline within 7 days for confirmed issues.

## Scope

Reticle Overlay is a local desktop application. It:

- Does **not** make network requests
- Does **not** collect or transmit user data
- Stores settings only in `%APPDATA%\ReticleOverlay\`
- Uses Win32 P/Invoke for overlay windowing, hotkeys, and DPI — all calls are read-only or restricted to this app's own windows
