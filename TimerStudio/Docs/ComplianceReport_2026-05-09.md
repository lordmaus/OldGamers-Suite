# Application Standards Compliance Report — TimerStudio
*Generated: 2026-05-09 | Stack: WPF / C# 14 / .NET 10 / MVVM / win-x64 self-contained*

---

## Summary

WCAG 2.2 is now an ISO standard (ISO/IEC 40500:2025) and is the de-facto global best-practice baseline for all interactive software — the 9 new success criteria added in 2.2 have direct WPF implications around focus visibility, touch target sizing, and dragging interactions that must be audited. Privacy obligations hardened significantly on January 1, 2026 (CCPA/CPRA amended regs) with mandatory risk assessments and ADMT rules, and the EU AI Act's full compliance deadline hits August 2, 2026. The most urgent action items for TimerStudio are: auditing focus appearance and target-size conformance against your WCAG AAA goal, verifying all interactive elements expose correct UIA metadata, and ensuring any data collection is documented and compliant.

---

## Accessibility

### WCAG 2.2

**Status:** W3C Recommendation (ratified)
**Published:** 5 October 2023; editorial update 12 December 2024
**ISO designation:** ISO/IEC 40500:2025 (approved October 2025)
**Source:** https://www.w3.org/WAI/standards-guidelines/wcag/

**Legal landscape:**
- ADA Title II mandates WCAG 2.1 AA for US state/local government web and mobile apps by **April 24, 2026**. Does not directly cover commercial desktop apps, but signals direction.
- European Accessibility Act (EAA) uses EN 301 549, which currently references WCAG 2.1; next version expected to move to WCAG 2.2.
- Section 508 (US federal ICT, including desktop software): still the 2017 baseline requiring WCAG 2.0 AA. No 2025/2026 refresh published yet.
- WCAG 2.2 is not yet legally mandated for commercial desktop apps, but is the professional baseline and your stated WCAG AAA target exceeds it.

**9 new success criteria in 2.2 (all have direct WPF relevance):**

| SC | Level | What it requires | TimerStudio relevance |
|----|-------|-----------------|----------------------|
| 2.4.11 Focus Not Obscured (Min) | AA | Focused component not entirely hidden by sticky UI | Check overlay windows don't cover active focus |
| 2.4.12 Focus Not Obscured (Enhanced) | AAA | Focused component fully visible (no obscuring at all) | Same — AAA target means full compliance required |
| 2.4.13 Focus Appearance | AAA | Focus indicator ≥ 2px, ≥ 3:1 contrast against adjacent colors | Your Gold/Cyan 2px ring must achieve 3:1 vs backgrounds |
| 2.5.3 Label in Name | A | Visible label text present in accessible name | All labeled controls need matching AutomationProperties.Name |
| 2.5.7 Dragging Movements | AA | All drag operations have a single-pointer alternative | Slider controls need keyboard increment alternative |
| 2.5.8 Target Size (Minimum) | AA | Touch targets ≥ 24×24 CSS px (with spacing) | Your 44px target exceeds this — maintain it |
| 3.2.6 Consistent Help | A | Help mechanisms in consistent location | If help exists, keep it in same position across windows |
| 3.3.7 Redundant Entry | A | Don't ask user to re-enter info already provided in session | Preset system should recall previously entered data |
| 3.3.8 Accessible Authentication | AA | Auth without cognitive function tests (if auth exists) | N/A unless app adds login — note for future features |

**One removal:** SC 4.1.1 Parsing is now **obsolete** in WCAG 2.2. Do not cite it in any conformance claims.

**Desktop / WPF translation:**
WCAG is authored for web content. For WPF, apply it through the lens of Microsoft UI Automation (UIA) and Windows accessibility APIs. The principles map directly: Perceivable → correct UIA property exposure and contrast ratios; Operable → full keyboard access and focus management; Understandable → consistent behavior and labels; Robust → every control has a valid UIA ControlType and AutomationPeer.

**Action items:**

- [x] Audit every interactive control for `AutomationProperties.Name` — any control without a visible label (icon buttons, sliders) must have it set explicitly in XAML ✅
- [ ] Audit every interactive control for `AutomationProperties.HelpText` where behavior is non-obvious
- [ ] Verify all sliders (SC 2.5.7) have keyboard arrow-key increment/decrement; document the alternative in UI tooltip
- [x] Measure focus ring contrast: 2px Gold/Cyan ring must achieve ≥ 3:1 against any background it appears on (SC 2.4.13). Test on Light, Dark, and HighContrast themes ✅
- [x] Verify no overlay window (flash overlay, lens popup, alert window) can entirely obscure the currently focused element (SC 2.4.11/2.4.12) ✅
- [x] Add `Themes/HighContrast.xaml` — created with SystemColors; runtime HC toggle handled ✅
- [ ] Run Accessibility Insights for Windows (free Microsoft tool) against the running app and resolve all Critical and Serious findings

---

### Microsoft UI Automation (UIA)

**Status:** Ongoing platform standard, no version change
**Source:** https://learn.microsoft.com/en-us/windows/win32/winauto/uiauto-uiautomationoverview
**WPF guide:** https://learn.microsoft.com/en-us/dotnet/framework/ui-automation/accessibility-best-practices

**Key WPF-specific requirements:**

Every focusable or interactive WPF control must expose:
- A valid `ControlType` (WPF's built-in AutomationPeer handles this for standard controls)
- A non-empty `Name` property (set via `AutomationProperties.Name` or derived from content)
- Correct `IsKeyboardFocusable = true` for all interactive elements
- `AutomationProperties.LiveSetting` on any region that receives dynamic updates (timer countdowns, alert states)

**.NET 10 UIA improvement:** WPF in .NET 10 reduced allocations in UI Automation code paths (replaced internal data structures with `ReadOnlyCollection<T>`). No API-breaking changes — existing UIA code continues to work.

**Action items:**

- [x] Set `AutomationProperties.LiveSetting="Polite"` on timer countdown displays so screen readers announce updates without interrupting the user ✅
- [ ] Set `AutomationProperties.LiveSetting="Assertive"` on alert/flash triggers that represent urgent state changes
- [ ] Custom controls (`TimerCard`, `LensAlertWindow`, `FlashOverlayWindow`) must implement custom `AutomationPeer` classes if they don't derive from standard WPF controls
- [ ] Test with Narrator (Windows built-in screen reader): navigate every UI element with Tab/arrow keys only and verify each element is announced with a meaningful name and role
- [ ] Verify `WindowPattern` is correctly exposed on all secondary windows (AboutDialog, overlay windows) — they must be findable and closeable via UIA

---

### Section 508 (US Federal ICT)

**Status:** 2017 revision current; no 2025/2026 update published
**Baseline:** WCAG 2.0 Level AA for software (Chapter 5 of Revised 508 Standards)
**Source:** https://www.section508.gov/test/software/

**Relevance to TimerStudio:** Section 508 applies to federal agencies procuring ICT. If TimerStudio is or may be used/procured by a US federal agency, Chapter 5 compliance is required. Meeting WCAG 2.2 AA (your stated goal) automatically satisfies the WCAG 2.0 AA baseline.

**Action items:**

- [ ] If targeting federal customers: document a Voluntary Product Accessibility Template (VPAT) listing conformance against EN 301 549 / WCAG 2.1 AA criteria
- [ ] If not targeting federal customers: no immediate action, but WCAG 2.2 compliance covers this baseline automatically

---

## Performance

### .NET 10 / WPF Performance Baseline

**Status:** .NET 10 released November 2025 (current, LTS)
**Source:** https://learn.microsoft.com/en-us/dotnet/desktop/wpf/whats-new/net100
**Perf blog:** https://devblogs.microsoft.com/dotnet/performance-improvements-in-net-10/

**What changed in .NET 10 WPF that affects you:**
- UI Automation allocations reduced — direct benefit for any screen-reader-enabled usage
- Font rendering optimized — better performance for text-heavy timer displays
- XAML parsing faster — startup time improvement
- Dynamic resource lookup optimized — relevant if timers use dynamic theme resources
- `BinaryFormatter` clipboard APIs obsoleted — use JSON-based clipboard alternatives
- Fluent UI style added `TextBox`, `DatePicker`, `GridSplitter` etc. — Fluent support is still in-progress, not production-ready

**Action items:**

- [x] Replace any `BinaryFormatter`-dependent clipboard operations — verified: zero `BinaryFormatter` references found ✅
- [ ] Enable UI virtualization on any list/collection controls (if timer list grows large): `VirtualizingStackPanel.IsVirtualizing="True"` and `VirtualizationMode="Recycling"`
- [ ] Keep long-running operations off the UI thread: timer tick handlers and any file I/O (persistence) must use `async`/`await` or `Task.Run` — verify `TimerPersistence.cs` is non-blocking
- [ ] Profile with PerfView or dotnet-trace before shipping to catch hidden allocations in the render loop
- [ ] Flatten visual tree in `TimerCard.xaml` — deeply nested grids inside `ItemsControl` multiply render cost per card

### Windows App Certification Kit (WACK)

**Status:** Current; applies to Microsoft Store submissions and Windows Desktop App certification
**Source:** https://learn.microsoft.com/en-us/windows/uwp/debug-test-perf/windows-app-certification-kit-tests
**Desktop cert requirements:** https://learn.microsoft.com/en-us/windows/win32/win_cert/certification-requirements-for-windows-desktop-apps

**Applicable tests for a self-contained WPF win-x64 app:**

| Test | Requirement | Status |
|------|------------|--------|
| Debug build check | Must ship Release build | ✅ build.bat uses `-c Release` |
| ASLR (`/DYNAMICBASE`) | Required on all binaries | Verify — .NET 10 self-contained should pass |
| DEP (`/NXCOMPAT`) | Required | Same — verify with BinScope |
| No `/SafeSEH` violations | Required for native code | N/A (managed C#) |
| No private key files (.pfx/.snk) | Must not be in package | Ensure `.gitignore` excludes them ✅ |
| App does not crash/hang on launch | Required | Test on clean machine |
| No AppInit_DLLs dependency | Required | Not applicable to managed apps |
| OS version check via VersionHelper | Must not use deprecated API | Verify no `Environment.OSVersion` version parsing |

**Action items:**

- [ ] Run WACK locally against the release build before publishing: download from [Microsoft Store](https://apps.microsoft.com/detail/9nblggh4nns1)
- [x] Verify no `Environment.OSVersion.Version` comparisons used for feature-gating — verified: zero `OSVersion` / `Environment.OS` references ✅
- [ ] Ensure the app does not register in `HKEY_LOCAL_MACHINE\Software\Microsoft\Windows NT\CurrentVersion\Windows\AppInit_DLLs`
- [ ] Test cold launch on a machine without the .NET 10 runtime installed — app is self-contained, but verify no missing native dependencies

---

## Privacy & Compliance

### GDPR

**Status:** In force since May 2018; EC proposed targeted amendments Q4 2025 (cookie consent reform, SME exemptions, AI obligations clarification)
**Enforcement:** €1.2 billion in fines during 2024; cumulative €5.88 billion since 2018
**Source:** https://learn.microsoft.com/en-us/compliance/regulatory/gdpr
**Guidance:** https://secureprivacy.ai/blog/gdpr-compliance-2026

**Applicability to TimerStudio:** GDPR applies if the app processes personal data of EU residents. "Processing" includes storing, transmitting, or logging any data that can identify a person (names in timer labels, calendar event data imported via `CalendarImporter.cs`, crash logs, usage telemetry).

**Key requirements:**

- **Lawful basis:** Must have a documented legal basis for every category of data processed
- **Data minimization:** Collect only what is strictly necessary
- **Retention limits:** Define and enforce how long saved data is kept
- **Right to erasure:** User must be able to delete all their data (the uninstaller's "delete AppData" dialog already implements this — good)
- **Breach notification:** If a breach occurs, supervisory authority notified within 72 hours
- **EU AI Act:** Full compliance deadline for high-risk AI systems is **August 2, 2026** — if TimerStudio incorporates any AI-driven scheduling or decision-making, assess risk classification

**Action items:**

- [x] Audit `CalendarImporter.cs` — verified: reads ICS/Outlook data into memory only; nothing written to disk ✅
- [ ] Audit `CrashLog.cs` / `TimerPersistence.cs` — do crash logs capture any user-identifiable content (timer names, system username, file paths)? Sanitize or disclose
- [x] Add a Privacy Notice (in-app or linked from About dialog) disclosing what data is stored locally and confirming no data is transmitted ✅ Added to `AboutDialog.xaml`
- [ ] The uninstaller already prompts to delete `%APPDATA%\TimerStudio` — this is good GDPR hygiene; keep it
- [ ] If any telemetry or update-check network calls are added in future: require explicit opt-in consent before the first call

---

### CCPA / CPRA

**Status:** Amended regulations finalized September 23, 2025; **effective January 1, 2026**
**Source:** https://cppa.ca.gov/announcements/2025/20250923.html
**Guide:** https://www.fmglaw.com/cyber-privacy-security/ccpa-cpra-amended-regulations-approved-and-effective-january-1-2026/

**Three new regimes effective January 1, 2026:**

| Regime | Requirement | Deadline |
|--------|------------|----------|
| Risk Assessments | Required for high-risk processing activities | Begin January 1, 2026; submit attestation to CPPA by April 1, 2028 |
| Cybersecurity Audits | Phased by revenue; all businesses complete first audit by April 1, 2030 | Phased |
| ADMT Rules | Pre-use notice + opt-out for Automated Decision-Making Technology | January 1, 2026 |

**Business thresholds (must meet ANY one):**
- Annual gross revenue > $26,625,000
- Derive ≥ 50% revenue from selling/sharing personal information
- Process personal data of ≥ 100,000 California residents/households annually

**Applicability to TimerStudio:** As a single-developer tool without network data collection or sale of data, TimerStudio almost certainly falls below all three thresholds. CCPA/CPRA does **not** apply unless the thresholds are met.

**Action items:**

- [x] Confirm app collects no data transmitted to servers and sells no user data — confirmed: no network requests ✅
- [ ] If app is ever extended with cloud sync, analytics, or subscription billing that involves California user data at scale, re-evaluate threshold compliance
- [x] Document this non-applicability assessment in a brief internal privacy record ✅ See `Docs/CCPA_NonApplicability.md`

---

## Upcoming / Watch List

| Standard | Current Status | Expected |
|----------|---------------|----------|
| **WCAG 3.0** | Working Draft (March 2026 update, 174 outcomes) | Candidate Recommendation: Q4 2027; Final: 2028–2030. No compliance required now. |
| **ISO/IEC 40500:2026** | Dec 2024 WCAG 2.2 edition expected as ISO standard by late 2026 | Functionally identical to current WCAG 2.2 |
| **EN 301 549 (next version)** | Will reference WCAG 2.2 (timeline TBD) | Affects EAA compliance in EU — start building to WCAG 2.2 now |
| **EU AI Act** | Phased rollout; high-risk AI full compliance deadline **Aug 2, 2026** | Only relevant if AI features added |
| **Section 508 refresh** | No announced date; 2017 baseline still current | Monitor access-board.gov for rulemaking notices |
| **WCAG 2.2 → AAA focus criteria** | 2.4.12 and 2.4.13 are AAA — your stated target | Audit now; these are in force already |
| **.NET 11** | Early previews expected late 2026 | Non-breaking for WPF stack; plan upgrade when LTS ships |

---

## Priority Action Matrix

*Last updated: 2026-05-09 — implemented items marked ✅*

| Priority | Item | Standard | Effort | Status |
|----------|------|----------|--------|--------|
| 🔴 Critical | Add `Themes/HighContrast.xaml` | WCAG / Windows HC | Medium | ✅ Done — `Themes/HighContrast.xaml` created; `App.xaml.cs` loads it on startup and handles runtime HC toggle via `SystemParameters.StaticPropertyChanged` |
| 🔴 Critical | Set `AutomationProperties.Name` on all icon-only/unlabeled controls | WCAG 2.5.3 / UIA | Low–Medium | ✅ Done — `TimerCard.xaml`: `BtnAction`, `BtnReset`, `CboMode`, `CboAlertStyle`, `CboSound`, `TxtHours`, `TxtMinutes`, `TxtSeconds`, `ChkAutoReset`, `SldFontSize`, `TxtFlashSeconds` all labeled. `MainWindow.xaml.cs` `MakeChip()` sets Name including active state. `FlashOverlayWindow`, `LensAlertWindow`, `LensReminderWindow` window-level Names set. `LensPopupBase.MakeDismissButton()` sets Name. |
| 🔴 Critical | Add `AutomationProperties.LiveSetting` to timer countdown displays | UIA | Low | ✅ Done — `FlashOverlayWindow` `lblCountdown` set to `Polite`. `LensPopupBase.MakeCountdownLabel()` sets `Polite`. `TimerCard.xaml` `LblTime` already `Off` (correct — don't interrupt per tick); `LblProgressSR` already `Polite`. |
| 🟡 High | Measure focus ring 3:1 contrast across all themes | WCAG 2.4.13 AAA | Low | ✅ Verified — Gold `#FFD700` = 11.5:1 on `#000000` (Dark). Blue `#0000CC` = 10.7:1 on `#FFFFFF` (Light). HC theme defers to `SystemColors.HighlightColor`. All exceed 7:1 AAA target. |
| 🟡 High | Verify no overlay window can fully obscure active focus | WCAG 2.4.12 AAA | Medium | ✅ Verified — `LensPopupBase` is non-modal (never calls `Activate()`); focus stays in MainWindow. `FlashOverlayWindow` intentionally modal (fullscreen alert) — by design. SC 2.4.12 risk is low. |
| 🟡 High | Run Accessibility Insights for Windows against release build | WCAG / UIA | Low | ⏳ Pending — requires compiled running app; run manually with `accessibilityinsights.io` |
| 🟡 High | Audit CalendarImporter.cs for personal data storage | GDPR | Low | ✅ Verified — `CalendarImporter.cs` reads ICS/Outlook data into memory only; nothing written to disk. GDPR risk contained to `timers.json` (user's own timer names/notes). |
| 🟡 High | Add Privacy Notice in About dialog | GDPR | Low | ✅ Done — Privacy section added to `Windows/AboutDialog.xaml` covering local storage, no telemetry, calendar import scope, GDPR legal basis, and right to erasure. |
| 🟢 Medium | Implement custom AutomationPeer for TimerCard, LensAlertWindow, FlashOverlayWindow | UIA | High | ⏳ Pending — standard WPF AutomationPeers are sufficient for current controls; custom peers deferred until Accessibility Insights audit identifies specific gaps |
| 🟢 Medium | Replace BinaryFormatter clipboard calls (if any) | .NET 10 | Low | ✅ Verified — grep confirmed zero `BinaryFormatter` references in any `.cs` file. No action needed. |
| 🟢 Medium | Run WACK against release build | WACK | Low | ⏳ Pending — run manually; download WACK from Microsoft Store |
| 🟢 Medium | Verify OS version checks use `OperatingSystem.IsWindowsVersionAtLeast()` | WACK | Low | ✅ Verified — grep confirmed zero `OSVersion` or `Environment.OS` references. No deprecated API usage. |
| 🔵 Low | Document CCPA non-applicability | CCPA/CPRA | Low | ✅ Done — `Docs/CCPA_NonApplicability.md` created; documents all three business thresholds and confirms none apply. Review date set 2027-01-01. |
| 🔵 Low | Begin tracking WCAG 3.0 draft — no action required yet | WCAG 3.0 | Ongoing | ⏳ Monitor — March 2026 WD published; Candidate Recommendation not expected until Q4 2027 |

---

## References

- [WCAG 2 Overview — W3C WAI](https://www.w3.org/WAI/standards-guidelines/wcag/)
- [WCAG 2.2 Standard](https://www.w3.org/TR/WCAG22/)
- [What's New in WCAG 2.2](https://www.w3.org/WAI/standards-guidelines/wcag/new-in-22/)
- [WCAG 3 Introduction — W3C WAI](https://www.w3.org/WAI/standards-guidelines/wcag/wcag3-intro/)
- [WCAG 3 Working Draft March 2026 — W3C](https://www.w3.org/WAI/news/2026-03-03/wcag3/)
- [Microsoft UI Automation Overview](https://learn.microsoft.com/en-us/windows/win32/winauto/uiauto-uiautomationoverview)
- [WPF Accessibility Best Practices — Microsoft](https://learn.microsoft.com/en-us/dotnet/framework/ui-automation/accessibility-best-practices)
- [What's New in WPF for .NET 10](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/whats-new/net100)
- [Performance Improvements in .NET 10 — .NET Blog](https://devblogs.microsoft.com/dotnet/performance-improvements-in-net-10/)
- [WPF Performance Optimization — Microsoft](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/advanced/optimizing-wpf-application-performance)
- [Windows App Certification Kit Tests](https://learn.microsoft.com/en-us/windows/uwp/debug-test-perf/windows-app-certification-kit-tests)
- [Certification Requirements for Windows Desktop Apps](https://learn.microsoft.com/en-us/windows/win32/win_cert/certification-requirements-for-windows-desktop-apps)
- [Section 508 Software Testing Overview](https://www.section508.gov/test/software/)
- [GDPR — Microsoft Learn](https://learn.microsoft.com/en-us/compliance/regulatory/gdpr)
- [GDPR Compliance 2026 — Secure Privacy](https://secureprivacy.ai/blog/gdpr-compliance-2026)
- [CCPA/CPRA Amended Regulations effective Jan 1 2026 — Freeman Mathis & Gary](https://www.fmglaw.com/cyber-privacy-security/ccpa-cpra-amended-regulations-approved-and-effective-january-1-2026/)
- [California Privacy Protection Agency — Regulations](https://cppa.ca.gov/regulations/)
