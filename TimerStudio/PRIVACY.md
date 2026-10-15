# Privacy Policy — Timer Studio

_Last updated: 2026-05-29_

## Summary

Timer Studio does **not** collect telemetry, analytics, or usage data, and we (the publisher) never receive any of your information. The app stores all data **locally**. Timer Studio does make network connections **only** for features you explicitly configure: email alerts, webhooks, and calendar (ICS) import. This document explains exactly what those do.

## What the app stores

The app saves your timers, stopwatches, alert settings, categories, theme, hotkeys, and integration settings **locally** under:

```
%AppData%\TimerStudio\
```

These files never leave your machine except as described below for features you turn on.

The app log `logs\TimerStudio.log` (rotated at 512 KB to `.old`) records errors with their stack trace and short notices. E-mail addresses are replaced by `<redacted>` and your Windows profile path by `%USERPROFILE%`. Older versions wrote `diagnostics.log`; you can delete it.

## Optional network features (user-configured)

Timer Studio contacts external servers **only** when you set up one of these features. None are enabled by default beyond what you configure.

### Email alerts (SMTP)
If you configure email alerts, Timer Studio connects to the SMTP server **you specify** to send alert messages to the recipient **you specify**.

- Connections use **STARTTLS** encryption.
- Your SMTP password is stored **encrypted at rest** using Windows DPAPI (`ProtectedData`, current-user scope) — it is never written in plaintext and is tied to your Windows account.
- For Microsoft accounts, OAuth tokens are cached **encrypted** with the same DPAPI mechanism.
- Email addresses and message contents are sent only to the mail server you chose; the publisher has no access to them.

### Webhooks
If you configure a webhook, Timer Studio sends an HTTP request to the URL **you provide** when a timer event fires. Only the data needed for that notification is sent, and only to your chosen endpoint.

### Calendar import (ICS)
If you add a calendar URL, Timer Studio fetches the ICS file from that URL to read event times. The request goes only to the URL you provided; no calendar data is sent anywhere else.

## What the app does NOT do

- No telemetry, analytics, crash reporting, or device identifiers are collected or transmitted.
- No data is sent to the publisher or any third party other than the SMTP server, webhook endpoint, or calendar URL you explicitly configure.
- No advertising or tracking SDKs are included.

## Your control

All integrations are opt-in and can be disabled or cleared at any time. Stored credentials are removed when you clear the integration or delete the `%AppData%\TimerStudio\` folder. Uninstalling the app and deleting that folder removes everything.

## Contact

Publisher: Matthew Pittman
