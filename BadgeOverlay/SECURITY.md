# Security Policy

## Supported versions

Only the latest release of Badge Overlay receives security fixes.
Older builds are not patched — please upgrade to the current version.

| Version | Supported |
|---------|-----------|
| Latest  | ✅ Yes    |
| Older   | ❌ No     |

## Reporting a vulnerability

**Please do not file a public GitHub issue for security vulnerabilities.**

Report privately via one of these channels:

- **GitHub private advisory:** open a
  [Security Advisory](https://github.com/lordmaus/Badge-Overlay/security/advisories/new)
  on the repository (preferred — keeps the report confidential until a fix ships).
- **Email:** contact Matthew Pittman through the website at
  [oldgamerssoftware.com](https://oldgamerssoftware.com/) if you cannot
  use GitHub.

Please include:

- A clear description of the vulnerability and its potential impact.
- Steps to reproduce or a proof-of-concept (even a rough one helps).
- The version of Badge Overlay and Windows you tested against.
- Whether you believe the issue is exploitable remotely or requires local access.

## Response timeline

| Milestone | Target |
|-----------|--------|
| Initial acknowledgement | Within 5 business days |
| Triage / severity assessment | Within 10 business days |
| Fix or workaround shipped | Depends on severity — critical issues are prioritised |
| Public disclosure | After fix is released and users have had reasonable time to upgrade |

## Scope

Badge Overlay is a desktop accessibility tool with no network stack, no
server component, and no remote-code execution surface. Typical areas
of concern would be:

- **Settings injection** — malicious `settings.json` causing unintended
  behaviour on load (path traversal, code execution via `portable.flag`
  redirection, etc.).
- **UIA/MSAA attack surface** — a rogue app feeding malicious accessible
  names that could cause crash-log injection, log flooding, or UI
  confusion.
- **Privilege escalation** — anything that lets a low-privilege attacker
  gain elevated execution through Badge Overlay's interaction with Win32
  or the UIA tree.
- **Denial of service** — a trigger that causes Badge Overlay to consume
  unbounded CPU/memory (e.g. via a crafted UIA tree).

Out of scope:

- Vulnerabilities in Windows, .NET, or third-party components that Badge
  Overlay uses (report those to Microsoft / the respective project).
- Cosmetic or non-security bugs (use the regular issue tracker).

## Privacy note

Badge Overlay makes no network calls and has no telemetry. If you discover
something that could cause the app to make unintended outbound connections,
that is in scope and should be reported privately.
