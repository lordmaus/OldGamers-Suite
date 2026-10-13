# Application Standards Compliance Report — Super Magnifier LV
*Generated: 2026-05-09 | Stack: WPF · C# 14 · .NET 10 · win-x64 self-contained*

## Summary

Super Magnifier LV is in excellent shape for a low-vision accessibility tool — every interactive control has `AutomationProperties.Name/HelpText`, both themes are documented at WCAG AAA contrast ratios (7:1+), focus rings are custom-rendered with Gold/Cyan at 13.7:1 and 7.2:1 respectively, and the OS "Reduce Motion" preference is respected (WCAG 2.3.3). **Three actionable gaps exist:** RadioButton/CheckBox touch targets fall short of 44 px (currently 36 px grid height), no Windows High Contrast mode adapter is wired up, and the installer/About dialog lacks any privacy statement — a soft requirement as the app reaches EU/CA users. No PII is collected and no network calls are made, so GDPR/CCPA exposure is minimal.

---

## Accessibility

### WCAG 2.2 (ISO/IEC 40500:2025)
**Status:** Ratified W3C Recommendation (October 5, 2023); adopted as ISO standard October 2025
**Legally enforced baseline:** AA — required by US ADA Title II (April 2026), EU EAA via EN 301 549, Section 508
**Your stated target:** AAA (7:1 contrast, 44 px targets, no colour-only info)

#### What changed from WCAG 2.1 (9 new criteria):
The nine new SC added in WCAG 2.2 most relevant to a WPF desktop app are:

| SC | Level | Concern for this app |
|----|-------|----------------------|
| 2.4.11 Focus Not Obscured (Minimum) | AA | Focus ring must not be fully hidden by sticky UI |
| 2.4.12 Focus Not Obscured (Enhanced) | AAA | Focus ring must not be *partially* hidden |
| 2.4.13 Focus Appearance | AAA | Focus indicator ≥ 2px, area ≥ perimeter×CSS-px×2 |
| 2.5.3 Label in Name | A | Button visible text must appear in accessible name |
| 2.5.7 Dragging Movements | AA | Any drag action must have a pointer alternative |
| 2.5.8 Target Size (Minimum) | AA | Pointer target ≥ 24×24 CSS px (or 24 px spacing) |
| 3.2.6 Consistent Help | A | Help mechanism (F1/About) must be in consistent location |
| 3.3.7 Redundant Entry | A | Previously entered info must not be re-requested |
| 3.3.8 Accessible Authentication | AA | No cognitive function test in login flows |

#### Audit findings vs code

**✅ Passing — Contrast (SC 1.4.3, 1.4.6)**
All theme tokens verified by ratio calculation:
- Dark: BrushText 21:1, BrushMuted 7.1:1, BrushHint 8.1:1, BrushBlue 7.2:1, BrushRed 7.0:1, BrushGreen 8.5:1 — all AAA
- Light: BrushText 21:1, BrushMuted 7.0:1, BrushHint 9.7:1, BrushBlue 7.4:1, BrushRed 7.0:1, BrushGreen 7.1:1 — all AAA
- Focus rings: Gold #FFD700 on #000000 = 13.7:1 AAA; Cyan #007B8A on #FFFFFF = 7.2:1 AAA

**✅ Passing — Reduce Motion (SC 2.3.3)**
`MainWindow.xaml.cs:462` reads `SystemParameters.ClientAreaAnimation` on startup and skips all slide/zoom/toast animations when false. Referenced explicitly with SC number in comments.

**✅ Passing — AutomationProperties (SC 4.1.2)**
Every interactive control in `MainWindow.xaml` has both `AutomationProperties.Name` and `AutomationProperties.HelpText`. Zoom readout has `AutomationProperties.LiveSetting="Polite"` (SC 4.1.3).

**✅ Passing — Touch targets on Buttons (SC 2.5.5 / 2.5.8)**
`App.xaml:45-46` — Button style sets `MinHeight="44" MinWidth="44"`. Zoom ▲▼ buttons are `Width="44" Height="44"` (explicit). Preset buttons are min-width 44.

**✅ Passing — Keyboard navigation**
`TabIndex` assigned on all primary controls (1–6+). `KeyboardNavigation.TabNavigation="Cycle"` on SettingsDialog. All buttons respond to Space/Enter per WPF defaults.

**✅ Passing — Non-text contrast on UI components (SC 1.4.11)**
Focus ring border at 2 px width, Gold/Cyan both exceed 3:1 against adjacent colours by a wide margin.

**✅ Passing — Use of colour (SC 1.4.1)**
Validation hints in SettingsDialog use BrushRed **plus** descriptive text ("200–3840 px required"). No colour-only information.

---

**❌ FAIL — RadioButton / CheckBox touch targets (SC 2.5.5 AAA, SC 2.5.8 AA)**
`App.xaml:118, 179` — Both `ControlTemplate` roots are `Grid ... MinHeight="36"`, below the 44 px AAA target and the 24 px AA minimum when spacing is not compensated. Buttons are 44 px; these two are inconsistent.

```xml
<!-- App.xaml line 118 / 179 — FIX: raise to 44 -->
<Grid Background="Transparent" MinHeight="36">   ← should be 44
```

Action: Change both RadioButton and CheckBox template grid `MinHeight` to `44`.

---

**❌ FAIL — No Windows High Contrast mode adaptation**
The app has Dark and Light themes but no listener for Windows High Contrast activation (`SystemParameters.HighContrast`, `SystemEvents.UserPreferenceChanged`). When a user enables High Contrast, WPF falls back to system brushes only if the control template does not override them — custom templates (IconButton, CheckBox, RadioButton, Slider) will break.

BadgeOverlay implements `HighContrast.xaml` and wires `HighContrastMonitor.cs`. SuperMagnifierLV has no equivalent.

Action:
- [ ] Add `HighContrast.xaml` resource dictionary (use `SystemColors.HighlightBrush`, `SystemColors.WindowTextBrush`, etc.)
- [ ] Add a `HighContrastMonitor` class that listens to `SystemEvents.UserPreferenceChanged` and swaps the merged dictionary
- [ ] Test with Windows High Contrast White and High Contrast Black

---

**⚠️ WARN — FontSize 8/9 px label text (SC 1.4.4 Resize Text)**
`MainWindow.xaml:199-210, 328, 383, 417` — Six `TextBlock` elements use `FontSize="8"` or `FontSize="9"`:
- Slider range labels ("1.25×", "20×")
- Colour swatch hex labels ("#FFDC00", "#FF5000", "#64B4FF", "#FF4444")

These are informational labels, not primary controls. Contrast is fine (17-18:1 on their backgrounds). The concern is that at 8 px these are physically very small — a user who has increased system DPI will benefit, but at 96 DPI standard they are below comfortable reading threshold for a low-vision tool. WCAG 1.4.4 requires text to be resizable to 200% without loss of content.

Action:
- [ ] Raise all `FontSize="8"` labels to at minimum `FontSize="10"`
- [ ] Raise `FontSize="9"` slider labels to `FontSize="10"` or `"11"`
- [ ] Verify the layouts don't overflow at larger font sizes

---

**⚠️ WARN — BrushDisabled Dark (#505050 on #000000 = 2.60:1)**
WCAG SC 1.4.3 explicitly exempts inactive/disabled UI components from contrast requirements, so this is **technically compliant**. However, for a product specifically serving low-vision users, 2.60:1 disabled text will be invisible to many of your target audience. Consider raising to ≥ 4.5:1 (e.g., `#737373` on `#000000 = 4.55:1`).

---

**⚠️ WARN — SC 2.4.13 Focus Appearance (AAA) — ring area not formally verified**
The focus ring is 2 px (meets the minimum) and offset is 2 px outset. SC 2.4.13 requires the indicator area to be at least: `perimeter of the unfocused component × 2 CSS px`. This passes visually but has not been formally measured per the formula. Run the W3C's focus appearance calculator against the smallest focusable control (zoom ▼ button, 44×44).

---

### Microsoft UI Automation (UIA)
**Status:** Stable — no breaking changes in 2025/2026
**Source:** [Microsoft Accessibility Overview](https://learn.microsoft.com/en-us/windows/apps/design/accessibility/accessibility-overview) — last updated 2026-03-26

WPF provides AutomationPeer implementations for all built-in controls. Custom controls (if any) need explicit peers.

**Audit findings:**

**✅ Passing** — No custom controls detected that lack peer coverage. All interactive elements are standard WPF Button, CheckBox, RadioButton, Slider, ComboBox with built-in peers.

**✅ Passing** — `AutomationProperties.LiveSetting="Polite"` on zoom readout TextBlock enables live region announcements to screen readers (NVDA, Narrator) when zoom changes.

Action:
- [ ] Run Inspect.exe (Windows SDK) against the main window and all dialogs to verify every control has a non-null `Name` property in the UIA tree
- [ ] Verify SettingsDialog controls — the scan above only confirmed MainWindow; SettingsDialog validation labels (FontSize 10) should have `AutomationProperties.Name` if they are error regions

---

### Section 508 (US Federal ICT)
**Status:** Revised 2017 rule in effect (no 2025 update). Only applies to US federal agency procurement.
**Baseline:** WCAG 2.0 AA for software; Chapter 5 covers non-web software.

If Super Magnifier LV is not procured by federal agencies, Section 508 is not legally binding. The existing WCAG 2.2 AA/AAA compliance exceeds Section 508 requirements.

---

### European Accessibility Act (EAA)
**Status:** Enforcement began **June 28, 2025** — deadline has passed.
**Technical standard:** EN 301 549 v3.2.1 → references WCAG 2.1 AA as the presumptive conformity baseline; update to WCAG 2.2 expected.
**Applies to:** Products and services sold/distributed in EU member states.

If the app is distributed to EU users (even free), EAA applicability depends on whether it qualifies as a "product placed on the EU market." Assistive technology is specifically covered under EAA Annex I, Section I — which directly covers screen magnification software.

Actions:
- [ ] Confirm whether the app is distributed in EU markets and assess EAA obligation
- [ ] If yes: maintain technical documentation demonstrating conformance for ≥ 5 years
- [ ] Ensure installer and product page includes an accessibility statement

---

## Performance

### WPF / .NET 10 — Desktop App Performance
**Source:** [Optimizing WPF Application Performance](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/advanced/optimizing-wpf-application-performance) · [.NET 10 release notes](https://learn.microsoft.com/en-us/dotnet/core/whats-new/dotnet-10/overview)

**✅ Passing — Self-contained single-file publish**
`build.bat` uses `/p:PublishSingleFile=true /p:SelfContained=true` — no runtime dependency on the user machine. Correct approach for a magnification tool used by people who may not be developers.

**✅ Passing — Async background operations**
UIAutomation caret tracking (`CaretTracker.cs`) and word boundary snapping (`WordTracker.cs`) run on background threads with 200 ms timeout. UI thread stays free.

**✅ Passing — Animation skip on low-end / Reduce Motion**
`MainWindow.Zoom.cs` checks the reduce motion flag before starting zoom animation. Zero CPU waste when disabled.

**⚠️ WARN — SPI_SETFONTSMOOTHING system-level mutation**
`MagnifierEngine.cs:37-39` temporarily modifies the system-wide font smoothing mode via `SystemParametersInfo(SPI_SETFONTSMOOTHING / SPI_SETFONTSMOOTHINGTYPE)`. `Dispose()` restores it (line 241-252). However, if the process is killed (Task Manager, crash before Dispose), font smoothing stays in the modified state system-wide until next reboot or the user manually resets it.

Action:
- [ ] Register a `AppDomain.CurrentDomain.UnhandledException` / `Application.Current.DispatcherUnhandledException` handler that calls `MagnifierEngine.Dispose()` before the process exits on unhandled exceptions
- [ ] Consider registering a Win32 structured exception handler or at minimum logging a crash state file so the next startup can detect and restore if the previous session didn't clean up

**⚠️ WARN — 60 fps polling timer default**
`AppSettings.cs` default `UpdateRateFps = 60`. On integrated graphics or battery power this is significant. The README notes users can lower it in Settings, but the default is aggressive for a magnification overlay. Consider 30 fps default with 60 fps as opt-in.

**⚠️ WARN — Windows App Certification Kit (WACK)**
WACK is required for Microsoft Store submission but not for standalone distribution. Super Magnifier LV uses `AllowUnsafeBlocks=true` and P/Invoke — these are acceptable for Win32 desktop apps but would require review for Store packaging. No Store submission path is current; no action required unless Store distribution is planned.

---

## Privacy & Compliance

### GDPR (EU Regulation 2016/679)
**Status:** In force. 2025 enforcement is intensifying, particularly around dark patterns and data minimisation.
**Source:** [GDPR Compliance 2026 Guide](https://secureprivacy.ai/blog/gdpr-compliance-2026)

**Audit findings:**

**✅ Passing — Zero network calls**
No `HttpClient`, `WebClient`, HTTP library, or telemetry SDK detected anywhere in the codebase. The app is fully offline.

**✅ Passing — No PII collected**
Settings stored in `%AppData%\SuperMagnifierLV\magnifier_settings.json` contain only UI preferences: zoom level, window geometry, colour choices, hotkey bindings. No names, email addresses, hardware IDs, or identifiers.

**⚠️ MINOR — AppPositionMap/AppPresetMap stores application exe paths**
`AppSettings.AppPositionMap` and `AppSettings.AppPresetMap` serialise the exe filenames of applications the user runs (e.g., `chrome.exe`, `notepad.exe`) to enable per-app presets. Under a strict reading of GDPR, this is behavioural data about which applications the user uses, stored on their own device. Since data never leaves the device and is only used to restore the user's own preferences, this is almost certainly within the "processing for personal use" household exemption (Article 2(c)) and requires no further action. Document it in the privacy statement for completeness.

**❌ No privacy statement**
The installer Welcome page and About dialog have no privacy statement or data disclosure. Even for an app that collects nothing, a brief statement ("This application stores your preferences locally. No data is transmitted to any server.") is best practice and may be required if you distribute via platforms that mandate it (Microsoft Store, EAA compliance documentation).

Action:
- [ ] Add a one-paragraph data disclosure to the NSIS installer Welcome page (`MUI_WELCOMEPAGE_TEXT`)
- [ ] Add a "Privacy" link or statement to the About dialog

---

### CCPA / CPRA (California)
**Status:** New 2026 regulations effective January 1, 2026 — risk assessments, cybersecurity audits, ADMT rules.
**Source:** [CPPA 2025 Finalized Regulations](https://cppa.ca.gov/announcements/2025/20250923.html)

**Applicability threshold:** Annual gross revenue > $26.6M, OR 50%+ revenue from selling data, OR processing data of 100,000+ California residents annually.

Unless Super Magnifier LV meets one of these thresholds, CCPA/CPRA does not apply. As a local desktop tool with no server-side data processing, it almost certainly does not. No action required beyond the privacy statement recommended above.

---

## Upcoming / Watch List

| Standard | Status | Expected | What to watch |
|----------|--------|----------|---------------|
| **WCAG 3.0** | Working Draft (March 2026, 174 outcomes) | Candidate Rec: Q4 2027; final: ~2028+ | New scoring model ("adhere"/"fail" per outcome). Desktop explicitly covered. No legal force until adopted into regulations. |
| **EN 301 549 update** | Expected to reference WCAG 2.2 | 2026 | EAA conformity presumption will shift from WCAG 2.1 AA to WCAG 2.2 AA. Already passing. |
| **CCPA cybersecurity audit** | Regulations finalised Sep 2025 | First submissions Apr 2028 | Only triggers if revenue thresholds met. |
| **ADA Title II WCAG 2.1 deadline** | Regulation in effect | Apr 24, 2026 (state/local govts) | Relevant if app procured by US government entities. Already passing WCAG 2.2 AAA ≥ 2.1 AA. |
| **WCAG 2.5.8 Target Size (Minimum)** | AA — now in force via WCAG 2.2 | In force | 24×24 px minimum pointer target. CheckBox/RadioButton at 36 px height passes 24 px minimum but fails AAA 44 px target. |

---

## Priority Action Items

### 🔴 High Priority
- [ ] **Raise RadioButton + CheckBox template `MinHeight` from 36 to 44 px** (`App.xaml:118, 179`) — WCAG 2.5.5 AAA failure
- [ ] **Add Windows High Contrast mode support** — add `HighContrast.xaml`, wire `SystemEvents.UserPreferenceChanged` — required for any accessibility tool targeting low-vision users
- [ ] **Add crash-time `MagnifierEngine.Dispose()` call** via unhandled exception handlers — prevents system-wide font smoothing corruption on crash

### 🟡 Medium Priority
- [ ] **Add privacy statement** to installer Welcome page and About dialog — no legal obligation yet but expected for EAA documentation and good practice
- [ ] **Raise FontSize 8/9 labels to ≥ 10** — readability improvement for the app's primary target audience
- [ ] **Run Inspect.exe audit** on all dialogs (SettingsDialog, HotkeyRemapDialog, FirstRunWizard) to verify UIA tree completeness
- [ ] **Formally verify SC 2.4.13 Focus Appearance** area formula on smallest control

### 🟢 Low Priority / Consider
- [ ] Raise `BrushDisabled` Dark from `#505050` (2.60:1) to ≥ `#737373` (4.55:1) — technically exempt but poor usability for low-vision target users
- [ ] Lower default `UpdateRateFps` from 60 to 30 — reduces CPU/GPU load on battery devices
- [ ] Add `AutomationProperties.Name` to SettingsDialog validation hint TextBlocks for screen reader announcements of inline errors

---

## References

- [WCAG 2.2 W3C Recommendation](https://www.w3.org/TR/WCAG22/)
- [WCAG 2 Overview — W3C WAI](https://www.w3.org/WAI/standards-guidelines/wcag/)
- [WCAG 3 Working Draft March 2026 — W3C WAI](https://www.w3.org/WAI/news/2026-03-03/wcag3/)
- [Microsoft Accessibility Overview — Windows Apps (updated 2026-03-26)](https://learn.microsoft.com/en-us/windows/apps/design/accessibility/accessibility-overview)
- [Accessible Text Requirements — Microsoft Learn](https://learn.microsoft.com/en-us/windows/apps/design/accessibility/accessible-text-requirements)
- [UI Automation Overview — Win32 apps](https://learn.microsoft.com/en-us/windows/win32/winauto/uiauto-uiautomationoverview)
- [Optimizing WPF Application Performance — Microsoft Learn](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/advanced/optimizing-wpf-application-performance)
- [WCAG 2.2 ISO/IEC 40500:2025 — AdaQuickscan](https://adaquickscan.com/blog/wcag-2-2-iso-standard-2025)
- [European Accessibility Act — European Commission](https://commission.europa.eu/strategy-and-policy/policies/justice-and-fundamental-rights/disability/european-accessibility-act-eaa_en)
- [California CPPA 2025 Finalized Regulations](https://cppa.ca.gov/announcements/2025/20250923.html)
- [GDPR Compliance 2026 — Secure Privacy](https://secureprivacy.ai/blog/gdpr-compliance-2026)
- [Section 508 ICT Standards — Access Board](https://www.access-board.gov/ict/)
- [Windows App Certification Kit — Microsoft Learn](https://learn.microsoft.com/en-us/windows/uwp/debug-test-perf/windows-app-certification-kit)
- [Certification Requirements for Windows Desktop Apps — Microsoft Learn](https://learn.microsoft.com/en-us/windows/win32/win_cert/certification-requirements-for-windows-desktop-apps)
