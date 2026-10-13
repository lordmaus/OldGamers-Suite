# Application Standards Audit — Timer Studio
*Generated: 2026-05-11 | Stack: WPF / C# 14 / .NET 10 / win-x64 self-contained*

## Summary

WCAG 2.2 was ratified as ISO/IEC 40500:2025 in October 2025, making it the de-facto
international accessibility baseline; the WCAG2ICT note (updated January 2026) provides
direct guidance for applying every WCAG 2.2 criterion to desktop software like Timer
Studio. On the privacy front, California's CPPA finalized sweeping CCPA updates effective
January 1, 2026 (risk assessments, cybersecurity audits, ADMT rules), though Timer Studio
almost certainly falls below all applicability thresholds. The most urgent action items are
completing any remaining UIA automation-peer gaps, verifying the HighContrast theme after
the .NET 10 HighContrast crash-fix batch, and ensuring Authenticode code signing is in
place before any public distribution.

---

## Accessibility

### WCAG 2.2 / WCAG2ICT (Desktop Software Edition)

**Status:** W3C Recommendation (ratified) | ISO/IEC 40500:2025 (ratified October 2025)
**Last updated:** December 12, 2024 (WCAG 2.2); May 6, 2025 (WCAG 2.1 errata);
                  January 21, 2026 (WCAG2ICT note updated to cover 2.2)

**What WCAG2ICT means for Timer Studio:**
W3C's [WCAG2ICT](https://www.w3.org/WAI/standards-guidelines/wcag/non-web-ict/) guidance
(updated 21 Jan 2026) explicitly maps every WCAG 2.2 Level A/AA success criterion to
non-web software and documents. It is the authoritative bridge between the web-focused
WCAG text and a native WPF application. Section 508 and EN 301 549 both derive their
desktop software requirements from WCAG2ICT.

**Key changes in WCAG 2.2 vs 2.1 relevant to WPF:**

| SC | Level | What it requires for a WPF app |
|----|-------|-------------------------------|
| 2.4.11 Focus Not Obscured (Minimum) | AA | Focused control must not be entirely hidden by sticky UI (e.g. a pinned overlay or toast must not cover the focused element) |
| 2.4.12 Focus Not Obscured (Enhanced) | AAA | Focused control must be *fully* visible |
| 2.4.13 Focus Appearance | AAA | Focus indicator ≥ 2 CSS px (≥ 2 WPF DIPs), ≥ 3:1 contrast ratio between focused/unfocused states |
| 2.5.7 Dragging Movements | AA | Any drag must have a single-pointer alternative (e.g. resize grip must also work via keyboard or numeric input) |
| 2.5.8 Target Size (Minimum) | AA | Interactive targets ≥ 24×24 CSS px (DIPs) at their minimum dimension, or sufficient spacing |
| 3.2.6 Consistent Help | A | Help/support links must appear in a consistent location across screens |
| 3.3.7 Redundant Entry | A | Previously entered data must not need to be re-entered in the same session |
| 3.3.8 Accessible Authentication (Minimum) | AA | Auth flows must not rely solely on cognitive tests — affects any future login dialog |
| 4.1.1 Parsing | — | **Obsolete in WCAG 2.2** — no longer needs to be tested |

**Target size note:** WPF DIPs = CSS px at 96 DPI (1:1 ratio). The app already uses
`MinHeight="44"` on many controls (exceeds the 24px minimum). Verify all icon buttons
and checkbox/radio hit areas.

**Action items:**
- [ ] Run [Accessibility Insights for Windows](https://accessibilityinsights.io/docs/windows/)
      on every dialog and verify zero "Name" or "Role" failures in the UIA tree.
- [ ] Verify `FlashOverlayWindow` and `LensAlertWindow` do not obscure the focused
      element in the main window when displayed (SC 2.4.11).
- [ ] Confirm the Gold/Cyan focus ring achieves ≥ 3:1 contrast ratio between focused
      and unfocused states (SC 2.4.13 AAA — target for WCAG AAA commitment).
- [ ] Audit every interactive element for minimum 24×24 DIP touch/pointer target
      (SC 2.5.8). Pin button (32×32) and most buttons pass; verify chip category
      dots and color palette swatches.
- [ ] Verify the resize grip in the bottom-right corner of every dialog has a keyboard
      alternative (SC 2.5.7) — e.g. a size entry field or arrow-key resize support.
- [ ] Ensure `AutomationProperties.Name` is set on all `Canvas` resize-grip elements
      (currently decorative but should be labelled or marked as decorative).
- [ ] Review any future email/OAuth login flows against SC 3.3.8 (no pure
      cognitive-test authentication).

---

### Microsoft UI Automation (UIA)

**Status:** Stable / actively maintained (COM API)
**Source last updated:** July 14, 2025 (Microsoft Learn)

**Desktop / WPF relevance:**
WPF implements UIA via `AutomationPeer` subclasses. Every built-in WPF control has a
peer. Custom controls — including `TimerCard` (ItemsControl child), `LensAlertWindow`,
and `FlashOverlayWindow` — require custom peers that override `GetNameCore()`,
`GetAutomationControlTypeCore()`, and expose the correct `ControlPattern` interfaces.

UIA is the mechanism through which screen readers (Narrator, JAWS, NVDA), automated
test frameworks (Appium WinAppDriver, FlaUI), and the Windows on-screen keyboard
communicate with Timer Studio.

**Key UIA requirements for Timer Studio:**

| Element | Required UIA exposure |
|---------|----------------------|
| Timer name `TextBox` | `EditPattern`, `Name` = field label |
| Start/Pause/Reset `Button` | `InvokePattern`, `Name` = action label |
| Mode `ComboBox` | `SelectionPattern`, `Name` = "Timer mode" |
| Alert overlay windows | `WindowPattern`, `Name` = timer name + " alert"; `LiveSetting = Assertive` |
| Reminder popup | `WindowPattern`, `Name` = timer name + " reminder"; `LiveSetting = Polite` |
| Settings dialog tab RadioButtons | `SelectionItemPattern`, `GroupName` verified |
| Sliders | `RangeValuePattern`; verify `Minimum`, `Maximum`, `Value`, `SmallChange` |
| Color palette buttons | `InvokePattern`, `Name` = hex color value |
| Resize grip canvas | Mark as decorative (`IsControlElement = false`) or `Name` = "Resize" |

**Action items:**
- [ ] Use [Inspect.exe](https://learn.microsoft.com/en-us/windows/win32/winauto/inspect-objects)
      (ships with Windows SDK) to verify the full UIA tree for `MainWindow`,
      `TimerCard`, `SettingsDialog`, `FlashOverlayWindow`, `LensAlertWindow`.
- [ ] Confirm custom `AutomationPeer` for `TimerCard` exposes `InvokePattern` on
      the action button and `ExpandCollapsePattern` for the settings panel.
- [ ] Verify `LiveRegion` (`AutomationProperties.LiveSetting`) is `Assertive` on
      `FlashOverlayWindow` root and `Polite` on reminder popups and balloon tooltips.
- [ ] Confirm slider `AutomationProperties.HelpText` includes the range and step
      (e.g. "1–4320 minutes, 1-minute steps").
- [ ] Test with Narrator (Win+Ctrl+Enter) end-to-end: add timer, start, receive alert,
      dismiss — all actions should be narrated without manual focus redirection.

---

### Section 508 (US Federal)

**Status:** Stable — Revised 508 Standards in force; April 2026 authoring guides updated
**Applicability to Timer Studio:** NOT legally required (not a federal agency product).
However, Section 508 Chapter 5 (Software) maps 1:1 with WCAG2ICT — complying with
WCAG 2.2 AA via WCAG2ICT satisfies the substance of Chapter 5.

**If Timer Studio is ever distributed to federal agencies or government contractors,**
a formal VPAT (Voluntary Product Accessibility Template) in ACR format would be required.

**Action items:**
- [ ] No immediate action required for current distribution.
- [ ] If pursuing government/enterprise sales: complete an
      [ACR/VPAT 2.5](https://www.itic.org/policy/accessibility/vpat) covering all
      WCAG 2.2 AA criteria via WCAG2ICT.

---

## Performance

### .NET 10 WPF Performance (Current Platform)

**Status:** Released November 2025 (LTS)
**Source:** [What's new in WPF for .NET 10](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/whats-new/net100) — last updated February 10, 2026

**What changed in .NET 10 WPF that affects Timer Studio:**

| Area | Change | Timer Studio impact |
|------|--------|---------------------|
| UI Automation | Reduced allocations in UIA tree operations | Less GC pressure on 40ms tick with 10+ timers |
| Font rendering | Optimized; font collection loader moved to managed code | Faster initial render of timer cards |
| Dynamic resources | Performance improved | `DynamicResource` brushes (theme switching) faster |
| XAML parsing | Optimized | Startup time improvement |
| Input composition | Optimized | TextBox typing latency reduced |
| HighContrast | Crash fixes | Resolves any HC theme issues |
| `RecognizesAccessKey` | Added to controls missing it | Access keys now work reliably in Fluent style |
| Clipboard API | `BinaryFormatter` removed; JSON path added | No impact (app doesn't use clipboard serialization) |
| `ArrayList` / legacy constructs | Phased out internally | Internal improvement only |
| Fluent styles | Added for TextBox, DatePicker, GroupBox, Label, etc. | If using Fluent theme: new controls get consistent style |

**Fluent style note:** Timer Studio uses custom `ResourceDictionary` brushes, not the
built-in Fluent theme. The Fluent additions in .NET 10 are opt-in; no action needed
unless adopting the Fluent theme.

**Action items:**
- [ ] Benchmark startup time on clean machine (target < 1 s cold-start for self-contained
      win-x64 build).
- [ ] Profile the 40ms `DispatcherTimer` tick with 10 active timers using
      [PerfView](https://github.com/microsoft/perfview) or VS Diagnostic Tools to
      confirm GC pauses < 5 ms and no per-tick allocations on the LOH.
- [ ] Verify `VirtualizingStackPanel` on the timers `ItemsControl` is active (should
      already be in place from previous audit).
- [ ] Enable [ReadyToRun compilation](https://learn.microsoft.com/en-us/dotnet/core/deploying/ready-to-run)
      in `TimerStudio.csproj` for faster startup on first run:
      `<PublishReadyToRun>true</PublishReadyToRun>`.
- [ ] After building with .NET 10, run app in High Contrast Black theme and verify
      no crashes (the .NET 10 HC crash fixes should cover this, but test is required).

---

### Windows Desktop App Certification (WACK)

**Status:** Version 10 (July 29, 2015) — archived but still the active standard for
           Windows Desktop App Certification Program
**Source:** [Certification requirements for Windows Desktop Apps](https://learn.microsoft.com/en-us/windows/win32/win_cert/certification-requirements-for-windows-desktop-apps)

**WACK compliance status for Timer Studio:**

| Requirement | Status | Notes |
|-------------|--------|-------|
| 1. Compatibility manifest | ✅ | `Assets\app.manifest` present |
| 1.3 DPI-aware via manifest | ✅ | `PerMonitorV2` in csproj |
| 2/3. Security best practices (ACLs, ASLR, DEP, SafeSEH) | ⚠️ | Verify compiler flags in Release build — .NET 10 sets ASLR/DEP by default, but verify with BinScope or WACK run |
| 5. Clean reversible installation | ✅ | NSIS with uninstall section |
| 5.5 Correct registry entries | ✅ | `HKLM\...\Uninstall` keys written |
| 6. Authenticode signature | ❌ | **No code signing certificate observed** — required for distribution |
| 9. UAC — main process as standard user | ⚠️ | `requireAdministrator` in manifest; WACK §9.3 requires waiver. Elevation is needed for NSIS installer but the app itself should ideally run as `asInvoker` post-install |
| 10. Install to Program Files | ✅ | `$PROGRAMFILES64\Timer Studio` |
| 10.2 No auto-startup | ✅ | No startup registry key (uninstall removes it if present) |
| 10.4 User data in `%APPDATA%` | ✅ | `%APPDATA%\TimerStudio\` |
| 12. x64 support | ✅ | win-x64 self-contained |

**Action items:**
- [ ] **Obtain and apply an Authenticode code-signing certificate** (EV recommended)
      to `TimerStudio.exe` and the NSIS-generated `TimerStudio_Setup.exe` before
      any public distribution. Without this, SmartScreen blocks installation with a
      "Windows protected your PC" warning.
- [ ] **Re-evaluate `requireAdministrator`** in `Assets\app.manifest`. If the app
      can function as `asInvoker`, change it — elevation is only needed for the
      *installer*, not the app at runtime. Current elevation causes the Outlook COM
      `CO_E_SERVER_EXEC_FAILURE` issue (now worked around with `UseShellExecute=true`),
      but running elevated also breaks drag-and-drop from non-elevated Explorer.
- [ ] Run WACK from Windows SDK to generate a formal compatibility report before
      any store or enterprise submission.
- [ ] Add `<PublishReadyToRun>true</PublishReadyToRun>` to csproj and re-verify
      WACK passes (R2R changes PE structure; WACK checks PE headers).

---

## Privacy & Compliance

### GDPR (EU General Data Protection Regulation)

**Status:** In force since May 2018; no major version change. Enforcement intensifying
           in 2025–2026 around AI processing and dark patterns.
**EU AI Act:** High-risk AI systems must comply by **August 2, 2026**.
**Source:** [Microsoft GDPR guidance](https://learn.microsoft.com/en-us/compliance/regulatory/gdpr);
           [Secure Privacy GDPR 2026 guide](https://secureprivacy.ai/blog/gdpr-compliance-2026)

**Timer Studio's data processing inventory:**

| Data type | Where stored | Purpose | Risk |
|-----------|-------------|---------|------|
| Timer names, notes, categories | `%APPDATA%\TimerStudio\timers.json` (local disk only) | User productivity | Low — stays on device |
| Email address (To/From/SMTP user) | `%APPDATA%\TimerStudio\prefs.json` | Sending alert emails | Medium — PII at rest |
| SMTP password | `prefs.json`, AES-128 encrypted | Email auth | Medium — encrypted at rest |
| Microsoft OAuth token cache | `prefs.json` (`MsOAuthTokenCache`) | OAuth2 SMTP auth | Medium — token at rest |
| Diagnostics log | `%APPDATA%\TimerStudio\diagnostics.log` | Debugging | Low — no PII logged (verified in code) |
| Calendar event data | Read-only from Outlook COM; not persisted | Calendar import | Low — transient only |

**Key GDPR obligations for Timer Studio:**

1. **Data minimization** ✅ — Only user-entered data is stored; no telemetry.
2. **Encryption at rest** ⚠️ — SMTP password is AES-128 encrypted. OAuth token is
   stored as a plain JSON string in `prefs.json`. Consider encrypting the token
   cache using DPAPI (`ProtectedData.Protect`) — Windows ties it to the user account.
3. **No data transmission to third parties** ✅ — Only the user's chosen SMTP server
   or Microsoft OAuth endpoint receives data.
4. **Breach notification** — Not applicable for a local-only app unless the user's
   machine is breached; no server to notify.
5. **Right to erasure** ✅ — Uninstaller offers to delete `%APPDATA%\TimerStudio`.
6. **Privacy notice** ✅ — Already added to `AboutDialog.xaml` (previous audit).
7. **PII in logs** ✅ — `DiagLog` confirmed to not log email addresses or passwords
   (only exception type names and CalendarImporter paths, which contain no PII).

**Action items:**
- [ ] **Encrypt OAuth token cache** in `prefs.json` using
      `System.Security.Cryptography.ProtectedData.Protect(data, null, DataProtectionScope.CurrentUser)`
      (DPAPI) — the same machine/user binding as the AES password, but OS-managed.
- [ ] **Privacy policy / notice**: If distributing publicly, add a brief online
      privacy policy URL to the About dialog and NSIS installer welcome page.
- [ ] **EU AI Act**: Timer Studio has no AI/ML components — not subject to AI Act.
      If AI-assisted scheduling or suggestions are added in future, reassess.
- [ ] **Review `MsOAuthTokenCache`** in `AppPrefs` — confirm it does not store
      refresh tokens indefinitely; MSAL's token cache should handle expiry.

---

### CCPA / CPRA (California Consumer Privacy Act)

**Status:** Sweeping new CPPA regulations finalized **September 23, 2025**;
           effective **January 1, 2026** with no grace period.
**New regimes (Jan 1, 2026):** Risk assessments, cybersecurity audits, ADMT rules.
**Source:** [CPPA announcement](https://cppa.ca.gov/announcements/2025/20250923.html);
           [Jackson Lewis FAQ](https://www.jacksonlewis.com/insights/navigating-california-consumer-privacy-act-30-essential-faqs)

**Applicability thresholds (any one triggers coverage):**
1. Annual gross revenue > **$26,625,000** (2025–2026 adjusted)
2. 50%+ of annual revenue from selling/sharing personal information
3. Personal information of **100,000+** California residents or households annually

**Timer Studio applicability assessment: NOT SUBJECT TO CCPA**

Timer Studio is a free desktop application with no revenue, no server-side data
collection, and no large-scale processing of California resident data. It does not
meet any of the three thresholds above.

| Threshold | Timer Studio | Status |
|-----------|-------------|--------|
| Revenue > $26.6M | $0 (free app) | ❌ Not met |
| 50%+ revenue from selling PI | No revenue, no selling of PI | ❌ Not met |
| 100K+ CA residents processed | Local-only; no user database | ❌ Not met |

**Action items:**
- [ ] No immediate CCPA/CPRA obligations. Document this assessment in the project
      privacy notes for future reference.
- [ ] **Re-evaluate if:** a subscription model is added, cloud sync is introduced,
      crash/telemetry reporting is added, or distribution scale exceeds 100K CA users
      with any form of personal data collection.
- [ ] The new **ADMT rules** (Automated Decision-Making Technology) would apply if
      Timer Studio used AI to make "significant decisions" affecting users — currently
      N/A, but flag for any future AI scheduling feature.

---

## Upcoming / Watch List

### WCAG 3.0 — W3C Accessibility Guidelines

**Status:** Working Draft (March 2026 update published)
**Candidate Recommendation:** Q4 2027 earliest
**W3C Recommendation:** Not before 2028; regulatory adoption likely 2030+

Key structural changes in WCAG 3 Working Draft (March 2026):
- "Foundational Requirements" renamed → "Core Requirements"
- "Outcomes" renamed → "Requirements"; "Guidelines" written as outcome statements
- 174 total requirements proposed (compared to 78 success criteria in WCAG 2.2)
- Broader scope: explicitly covers native apps, tools, and publishing platforms
- New conformance model (not A/AA/AAA) — still being designed
- AG WG to publish WCAG 3 projected timeline by April 2026

**For Timer Studio:** No action required now. WCAG 2.2 AA remains the standard.
Continue monitoring at [w3.org/WAI/standards-guidelines/wcag/wcag3-intro/](https://www.w3.org/WAI/standards-guidelines/wcag/wcag3-intro/).

---

### European Accessibility Act (EAA) — June 28, 2025 Deadline

EN 301 549 (current version: WCAG 2.1 AA) is the technical standard for EAA.
The next revision of EN 301 549 is expected to reference WCAG 2.2.

**If Timer Studio is sold in EU markets**, EAA applies to commercial software products
with first sale after June 28, 2025. Desktop productivity software is in scope.

**Action items (EU distribution):**
- [ ] Conduct a full EN 301 549 / WCAG 2.1 AA audit if distributing in EU.
- [ ] Monitor EN 301 549 update timeline (WCAG 2.2 adoption expected 2026).
- [ ] Prepare an Accessibility Statement per EAA requirements.

---

### EU AI Act — August 2, 2026 Compliance Deadline

High-risk AI systems must comply by August 2, 2026.
Timer Studio has no AI/ML components — no action required.
Reassess if AI scheduling, predictive alerts, or recommendation features are added.

---

### .NET 10 Clipboard API Migration

`BinaryFormatter` was obsoleted in .NET 9 and certain clipboard methods are being
obsoleted in .NET 10. Timer Studio does not currently use clipboard serialization,
but if copy/paste of timer configs is added, use the new JSON clipboard APIs.

---

## Consolidated Action Items by Priority

### 🔴 Critical (before any public distribution)
- [ ] **Authenticode code-sign** `TimerStudio.exe` and `TimerStudio_Setup.exe`
- [ ] **Run Accessibility Insights for Windows** and fix any UIA Name/Role failures
- [ ] **Test with Narrator** end-to-end (add, start, alert, dismiss)

### 🟡 High (next sprint)
- [ ] **Encrypt OAuth token cache** with DPAPI (`ProtectedData.Protect`)
- [ ] **Re-evaluate `requireAdministrator`** — switch to `asInvoker` if possible
- [ ] **Resize grip keyboard alternative** for SC 2.5.7 compliance
- [ ] **Verify all interactive targets ≥ 24×24 DIP** (SC 2.5.8) — especially
      color palette swatches and category dot chips
- [ ] **Test HighContrast theme** after .NET 10 HC crash-fix batch

### 🟢 Medium (ongoing / before EU distribution)
- [ ] Enable `<PublishReadyToRun>` in csproj for startup performance
- [ ] Profile 40ms tick with PerfView to confirm no per-tick GC allocations
- [ ] Complete EN 301 549 audit if pursuing EU market
- [ ] Add online privacy policy URL to About dialog

### 🔵 Watch / Future
- [ ] Monitor WCAG 3.0 Working Draft progress (CR expected Q4 2027)
- [ ] Monitor EN 301 549 revision for WCAG 2.2 adoption
- [ ] Re-evaluate CCPA if subscription model or cloud sync is introduced
- [ ] Re-evaluate EU AI Act if AI features are added

---

## References

- [WCAG 2 Overview — W3C WAI](https://www.w3.org/WAI/standards-guidelines/wcag/) *(updated 5 May 2026)*
- [WCAG 2.2 Standard — W3C TR](https://www.w3.org/TR/WCAG22/)
- [WCAG2ICT — Applying WCAG 2 to Non-Web ICT](https://www.w3.org/WAI/standards-guidelines/wcag/non-web-ict/) *(updated 21 Jan 2026)*
- [WCAG 3 Introduction — W3C WAI](https://www.w3.org/WAI/standards-guidelines/wcag/wcag3-intro/) *(updated 3 Mar 2026)*
- [WCAG 3 Working Draft — W3C TR](https://www.w3.org/TR/wcag-3.0/)
- [UI Automation Overview — Microsoft Learn](https://learn.microsoft.com/en-us/windows/win32/winauto/uiauto-uiautomationoverview) *(updated 14 Jul 2025)*
- [UI Automation of a WPF Custom Control — Microsoft Learn](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/controls/ui-automation-of-a-wpf-custom-control)
- [What's New in WPF for .NET 10 — Microsoft Learn](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/whats-new/net100) *(updated 10 Feb 2026)*
- [Certification Requirements for Windows Desktop Apps — Microsoft Learn](https://learn.microsoft.com/en-us/windows/win32/win_cert/certification-requirements-for-windows-desktop-apps) *(v10, Jul 2015)*
- [Section 508 Software Overview — Section508.gov](https://www.section508.gov/test/software/)
- [CPPA Regulations Finalized Sep 2025 — cppa.ca.gov](https://cppa.ca.gov/announcements/2025/20250923.html)
- [CCPA Updates (ADMT, Risk Assessments, Cybersecurity) — cppa.ca.gov](https://cppa.ca.gov/regulations/ccpa_updates.html)
- [GDPR Compliance — Microsoft Learn](https://learn.microsoft.com/en-us/compliance/regulatory/gdpr)
- [Accessibility Insights for Windows](https://accessibilityinsights.io/docs/windows/)
- [Inspect.exe — Windows SDK](https://learn.microsoft.com/en-us/windows/win32/winauto/inspect-objects)
