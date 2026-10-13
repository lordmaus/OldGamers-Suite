# Application Standards Compliance Report — TimerStudio
*Generated: 2026-05-10 | Stack: WPF / C# 14 / .NET 10 / MVVM / win-x64 self-contained*
*Prior report: `ComplianceReport_2026-05-09.md` — all action items from that report marked with carry-over status below.*

---

## Summary

The most significant developments since the last audit (2026-05-09): the **WCAG 3.0 March 2026 Working Draft** was published with structural changes (Outcomes renamed Requirements; Foundational renamed Core) — still no compliance obligation, but the terminology shift is worth tracking. The **EU AI Act's August 2, 2026 high-risk AI deadline** is imminent; a proposed Digital Omnibus deferral to December 2027 is possible but not yet adopted, so organizations with AI features must treat August 2 as the live deadline. The **GDPR Digital Omnibus** proposed amendments (Q4 2025) remain pending — cookie consent reform and breach notification threshold changes are not yet law. **.NET 10 WPF** shipped High Contrast crash fixes and expanded `RecognizesAccessKey` support that directly benefit TimerStudio's recent accessibility work. **No new action items are required for TimerStudio** beyond the two manual items carried over from the prior report (Accessibility Insights run + WACK run).

---

## Accessibility

### WCAG 2.2 (Current Ratified Standard)

**Status:** W3C Recommendation — ratified October 5, 2023; editorial update December 12, 2024
**ISO designation:** ISO/IEC 40500:2025 (approved October 2025). ISO/IEC 40500:2026 (WCAG 2.2 Dec 2024 edition) expected late 2026 — functionally identical.
**Source:** https://www.w3.org/TR/WCAG22/ · https://www.w3.org/WAI/standards-guidelines/wcag/

**No changes since last report.** WCAG 2.2 remains the current ratified standard and the professional baseline for all interactive software.

**TimerStudio status from prior report:**
- [x] All 9 new SC 2.2 criteria audited ✅
- [x] Focus rings: Gold 11.5:1 (Dark), Blue 10.7:1 (Light), SystemColors (HighContrast) ✅
- [x] 44px targets maintained ✅
- [x] `AutomationProperties.Name` added to all unlabeled controls ✅
- [x] `AutomationProperties.LiveSetting` on countdown labels ✅
- [ ] Run Accessibility Insights for Windows against release build ⏳ *Manual — requires compiled app*

---

### WCAG 3.0 (Working Draft — No Compliance Obligation)

**Status:** In-progress Working Draft. **Not a legal requirement.**
**Last updated:** March 2026 Working Draft published March 3, 2026
**Final standard ETA:** "A few more years" — Candidate Recommendation not expected before Q4 2027; Final 2028–2030
**Source:** https://www.w3.org/WAI/standards-guidelines/wcag/wcag3-intro/ (fetched 2026-05-10)

**What changed in March 2026 draft vs. prior drafts:**
- "Foundational Requirements" renamed → **"Core Requirements"**
- "Outcomes" renamed → **"Requirements"** (guidelines are now written as outcome statements)
- [WCAG 3 Explainer](https://www.w3.org/TR/wcag-3.0-explainer/) published alongside the draft — recommended reading before the draft itself
- Best practices section added to the Explainer
- Conformance model section updated in both Explainer and Working Draft
- Multiple draft guidelines, requirements, and assertions progressed from Exploratory → Developing status
- AG WG planned to publish a projected [WCAG 3 Timeline](https://www.w3.org/WAI/GL/wiki/WCAG_3_Timeline) by April 2026 (check for update)

**Desktop / WPF relevance:**
WCAG 3 explicitly broadens scope to desktops, tablets, mobile, wearables, IoT, and VR/AR — WPF apps are explicitly in scope. The new conformance model moves away from binary pass/fail toward a more flexible requirements-based assessment. No A/AA/AAA levels in WCAG 3. The structural shift (not the individual requirements) is the change most relevant to planning now.

**Action items:**
- [ ] No implementation action required yet. Monitor https://www.w3.org/WAI/GL/wiki/WCAG_3_Timeline for published timeline (expected April 2026 or later)
- [ ] Note terminology: begin using "requirements" instead of "outcomes" when referencing WCAG 3 draft internally

---

### Microsoft UI Automation (UIA)

**Status:** Ongoing platform standard — no version change
**Source:** https://learn.microsoft.com/en-us/dotnet/framework/ui-automation/ui-automation-overview
**WPF guide:** https://learn.microsoft.com/en-us/dotnet/desktop/wpf/whats-new/net100 (fetched 2026-05-10)

**What's new in .NET 10 WPF (released November 2025) — UIA-relevant:**

| Change | Details | TimerStudio impact |
|--------|---------|-------------------|
| **High Contrast crash fixes** | Multiple crashes related to `HighContrast` mode fixed in Fluent styles | ✅ Benefits our `Themes/HighContrast.xaml` work |
| **`RecognizesAccessKey` added** | Missing `RecognizesAccessKey` property added to `ContentPresenter` for controls | Verify any `_` access-key underscores in button labels render correctly |
| **UIA allocation reduction** | Internal data structures replaced with `ReadOnlyCollection<T>` — minimized allocations in UIA code paths | Passive performance improvement; no code change needed |
| **RTL layout fixes** | Right-to-left layout fixed for `MenuItem`, `Expander`, `TreeViewItem` | Not applicable (English-only app) |
| **Clipboard unification** | WPF + WinForms now use the same clipboard API; new JSON clipboard methods added | ✅ We use no `BinaryFormatter` clipboard calls (verified) |

**Action items:**
- [ ] Verify any button content strings using `_` for access keys (e.g., `_File`) display underlines correctly — `RecognizesAccessKey` fix in .NET 10 may surface previously hidden underlines
- [ ] Run Narrator walkthrough: Tab through every control and confirm each announces name + role correctly ⏳ *Manual*
- [x] `WindowPattern` exposure on secondary windows ✅ (all overlay windows set window-level UIA Name/HelpText in prior audit)

---

### Section 508 (US Federal ICT)

**Status:** 2017 revision still current. **No refresh scheduled or announced as of May 2026.**
**Baseline:** WCAG 2.0 Level AA for software (Chapter 5 of Revised 508 Standards)
**Source:** https://www.section508.gov/ · https://www.access-board.gov/ict/

**What's new (April 2026):** Section508.gov added MS Word 365, PowerPoint 365, and Excel 365 authoring and testing guides. The Software Overview section updated April 2026 to reflect testing guidance for desktop, mobile, and cloud apps with assistive technologies. No change to the underlying 508 standards themselves.

**IAAF 2026** (Interagency Accessibility Forum) is upcoming — presentations on "Future-Ready Accessibility" expected to address WCAG 3.0 trajectory.

**TimerStudio relevance:** Section 508 applies only if the app is procured by a US federal agency. Meeting WCAG 2.2 AA (current goal) automatically satisfies the WCAG 2.0 AA Section 508 baseline.

**Action items:**
- [ ] If targeting federal customers: prepare a VPAT (Voluntary Product Accessibility Template) against EN 301 549 / WCAG 2.1 AA criteria — no immediate deadline

---

## Performance

### .NET 10 WPF (LTS — Released November 2025, Supported to November 2028)

**Status:** Current LTS release. .NET 11 previews expected late 2026.
**Source:** https://learn.microsoft.com/en-us/dotnet/desktop/wpf/whats-new/net100 (fetched 2026-05-10)

**Performance improvements in .NET 10 WPF:**
- Font rendering, dynamic resource lookup, input composition, trace logging, regex usage, and XAML parsing all optimized
- Cache operations and array handling optimized; font collection loader migrated to managed code
- `ReadOnlyCollection<T>` replaces internal data structures — UIA and file dialog allocation reduction
- 4,000+ unit tests added for `System.Xaml` and `WindowsBase`

**New WPF APIs in .NET 10:**

| API | Change | TimerStudio relevance |
|-----|--------|----------------------|
| `MessageBox` | New button options: `AbortRetryIgnore`, `RetryCancel`, `CancelTryContinue` | Low — app uses simple OK/YesNo dialogs |
| `Grid` | String shorthand: `ColumnDefinitions="1*, 2*, Auto"` | Medium — can simplify XAML in `TimerCard.xaml` and `MainWindow.xaml` |
| Clipboard | JSON clipboard methods; `BinaryFormatter` clipboard methods obsoleted | ✅ No BinaryFormatter usage (verified) |

**Fluent style additions (still in-progress, not production-ready for custom themes):**
DatePicker, GridSplitter, GridView, GroupBox, Hyperlink, Label, NavigationWindow, RichTextBox, TextBox now have Fluent styles. TimerStudio uses custom styles (`App.xaml`) rather than Fluent — no impact unless opting in to Fluent.

**Code cleanup:**
Dead Code Access Security (CAS) attributes removed, `ArrayList` → typed collections, debugging patterns modernized.

**Action items:**
- [ ] Consider adopting `Grid` shorthand syntax (`ColumnDefinitions="..."`) in `TimerCard.xaml` to flatten XAML — lower maintenance cost, same runtime behavior
- [ ] Profile with dotnet-trace before shipping to confirm no hidden render-loop allocations from timer tick handlers
- [ ] Plan .NET 11 upgrade path: preview releases expected late 2026; TimerStudio should test on preview when available

### Windows App Certification Kit (WACK)

**Status:** No changes to desktop certification requirements announced in 2025–2026.
**Baseline:** 2017 Windows Desktop App Certification Requirements
**Source:** https://learn.microsoft.com/en-us/windows/win32/win_cert/certification-requirements-for-windows-desktop-apps

**Action items:**
- [ ] Run WACK against the `2026.05.09.001` release build ⏳ *Manual — download from Microsoft Store*
- [x] No `Environment.OSVersion.Version` usage found ✅
- [x] `.pfx`/`.p12` excluded from `.gitignore` ✅
- [x] Release build (`-c Release`) confirmed in `build.bat` ✅

---

## Privacy & Compliance

### GDPR

**Status:** In force since May 2018. **Digital Omnibus proposed amendments pending — not yet law.**
**Enforcement:** €1.2 billion in fines during 2024; cumulative €5.88 billion since 2018
**Source:** https://secureprivacy.ai/blog/gdpr-compliance-2026 (fetched 2026-05-10) · https://learn.microsoft.com/en-us/compliance/regulatory/gdpr

**What's new / pending (Q4 2025 – May 2026):**

| Proposed change | Status | Impact |
|----------------|--------|--------|
| **Personal data definition** clarification — data not "personal" if the processor has no means reasonably likely to identify the individual | Proposed, not yet law | Low for TimerStudio (local-only data) |
| **DPIA scope** — EU-wide list of processing operations NOT requiring a DPIA | Proposed, not yet law | Low — would reduce documentation burden |
| **Breach notification threshold** — Notify authority only if breach is "likely to result in high risk to rights and freedoms" (raising the threshold from current broad standard) | Proposed, not yet law | Low — no breach notification infrastructure needed for a local app |
| **Cookie consent reform** | Proposed, not yet law | Not applicable — no web component |

**EU AI Act August 2, 2026 deadline (GDPR-adjacent):**
High-risk AI system obligations (Articles 8–15) activate August 2, 2026. A Digital Omnibus proposal to defer this to December 2027 exists but is not yet adopted — treat August 2 as the live date unless deferral is formally enacted. Timer Studio has **no AI features** — this deadline does not apply.

**TimerStudio status:**
- [x] Privacy Notice added to About dialog ✅
- [x] `CalendarImporter.cs` verified in-memory only ✅
- [x] Uninstaller prompts to delete `%APPDATA%\TimerStudio` ✅
- [x] No telemetry, no network requests ✅

**Action items:**
- [ ] Monitor Digital Omnibus progress — if adopted, DPIA and breach notification obligations may ease. No action until finalized.
- [ ] If any AI/ML features are added in future: assess EU AI Act risk category before shipping

---

### CCPA / CPRA

**Status:** Amended regulations effective January 1, 2026 (finalized September 23, 2025)
**Source:** https://cppa.ca.gov/regulations/ccpa_updates.html · https://www.fmglaw.com/cyber-privacy-security/ccpa-cpra-amended-regulations-approved-and-effective-january-1-2026/

**Three new regimes now in effect:**

| Regime | Requirement | Compliance timeline | TimerStudio |
|--------|------------|---------------------|-------------|
| **Risk Assessments** | Required for high-risk processing (sensitive data, large-scale, significant decisions) | Begin Jan 1, 2026; submit attestation to CPPA by April 1, 2028 | ✅ Not applicable — below thresholds |
| **Cybersecurity Audits** | Annual audit of cybersecurity program effectiveness | First audit due April 1, 2030 | ✅ Not applicable — below thresholds |
| **ADMT Rules** | Pre-use notice + opt-out for Automated Decision-Making Technology | Effective January 1, 2026 | ✅ Not applicable — no ADMT features |

**Business thresholds (must meet ANY one for CCPA to apply):**
- Annual gross revenue > $26,625,000
- ≥ 50% revenue from selling/sharing personal information
- Process personal data of ≥ 100,000 California residents/households annually

TimerStudio meets **none** of these thresholds. CCPA/CPRA does not apply.

**TimerStudio status:**
- [x] `Docs/CCPA_NonApplicability.md` created ✅ (review date 2027-01-01)

**Action items:**
- [ ] None. Re-evaluate if app adds cloud sync, analytics, or subscription billing at scale.

---

## Upcoming / Watch List

| Standard | Current Status | Expected |
|----------|---------------|----------|
| **WCAG 3.0** | Working Draft (March 2026 — 174 requirements, terminology shift) | AG WG publishing timeline April 2026+; CR not before Q4 2027; Final 2028–2030 |
| **ISO/IEC 40500:2026** | WCAG 2.2 Dec 2024 edition → ISO designation expected late 2026 | Functionally identical to current standard |
| **EN 301 549 (next)** | Will reference WCAG 2.2 (timeline TBD) | Affects EAA compliance in EU — already building to WCAG 2.2 ✅ |
| **EU AI Act high-risk deadline** | August 2, 2026 (possibly deferred to Dec 2027 by Digital Omnibus) | Not applicable to TimerStudio — no AI features |
| **GDPR Digital Omnibus** | Proposed Q4 2025 — not yet adopted | Cookie consent reform, DPIA list, breach threshold changes |
| **Section 508 refresh** | No announced date; 2017 baseline still current | Monitor access-board.gov for rulemaking notices |
| **.NET 11** | Early previews expected late 2026 | Non-breaking for WPF stack; plan upgrade when LTS ships |
| **WCAG AAA 2.4.12 / 2.4.13** | In force now (WCAG 2.2 AAA) | ✅ Verified in prior audit |

---

## Delta from Prior Report (2026-05-09)

| Item | Change |
|------|--------|
| WCAG 3.0 terminology | "Outcomes" → "Requirements"; "Foundational" → "Core" in March 2026 draft |
| .NET 10 HC crash fixes | High Contrast crashes fixed in Fluent styles — benefits our `HighContrast.xaml` |
| .NET 10 `RecognizesAccessKey` | Fixed in Fluent styles — verify access key characters in custom templates |
| EU AI Act deadline | August 2, 2026 approaching; possible Digital Omnibus deferral to Dec 2027 — not applicable to TimerStudio |
| GDPR Digital Omnibus | Proposed amendments still pending; no law yet |
| CCPA cybersecurity audit timeline | First audit deadline now clarified: April 1, 2030 (not applicable) |
| **Net new action items for TimerStudio** | **None** — two manual items from prior report remain pending |

---

## Open Action Items (All Audits Combined)

| Priority | Item | Source report |
|----------|------|--------------|
| ⏳ Pending | Run Accessibility Insights for Windows against `2026.05.09.001` release build | 2026-05-09 |
| ⏳ Pending | Run WACK against `2026.05.09.001` release build | 2026-05-09 |
| ⏳ Pending | Narrator walkthrough — Tab through every control, verify name + role | 2026-05-10 |
| ⏳ Pending | Verify `_` access-key underlines render correctly with .NET 10 `RecognizesAccessKey` fix | 2026-05-10 |
| 🔵 Consider | Adopt `Grid` shorthand syntax in `TimerCard.xaml` / `MainWindow.xaml` | 2026-05-10 |
| 🔵 Consider | Profile with dotnet-trace before shipping — confirm no render-loop allocations | 2026-05-09 |
| 🔵 Monitor | WCAG 3 Timeline (AG WG, expected April 2026+) | 2026-05-10 |
| 🔵 Monitor | GDPR Digital Omnibus adoption status | 2026-05-10 |

---

## References

- [WCAG 3 Introduction — W3C WAI](https://www.w3.org/WAI/standards-guidelines/wcag/wcag3-intro/)
- [WCAG 3.0 Working Draft — W3C](https://www.w3.org/TR/wcag-3.0/)
- [WCAG 3.0 Explainer — W3C](https://www.w3.org/TR/wcag-3.0-explainer/)
- [WCAG 2.2 Standard — W3C](https://www.w3.org/TR/WCAG22/)
- [What's New in WCAG 2.2 — W3C WAI](https://www.w3.org/WAI/standards-guidelines/wcag/new-in-22/)
- [What's New in WPF for .NET 10 — Microsoft Learn](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/whats-new/net100)
- [Microsoft UI Automation Overview — Microsoft Learn](https://learn.microsoft.com/en-us/dotnet/framework/ui-automation/ui-automation-overview)
- [Accessibility Best Practices — Microsoft Learn](https://learn.microsoft.com/en-us/dotnet/framework/ui-automation/accessibility-best-practices)
- [Windows App Certification Kit Tests — Microsoft Learn](https://learn.microsoft.com/en-us/windows/uwp/debug-test-perf/windows-app-certification-kit-tests)
- [Certification Requirements for Windows Desktop Apps — Microsoft Learn](https://learn.microsoft.com/en-us/windows/win32/win_cert/certification-requirements-for-windows-desktop-apps)
- [Section 508 — section508.gov](https://www.section508.gov/)
- [GDPR Compliance 2026 — Secure Privacy](https://secureprivacy.ai/blog/gdpr-compliance-2026)
- [GDPR — Microsoft Learn](https://learn.microsoft.com/en-us/compliance/regulatory/gdpr)
- [CCPA/CPRA Amended Regulations Jan 1 2026 — Freeman Mathis & Gary](https://www.fmglaw.com/cyber-privacy-security/ccpa-cpra-amended-regulations-approved-and-effective-january-1-2026/)
- [CPPA Regulations — cppa.ca.gov](https://cppa.ca.gov/regulations/ccpa_updates.html)
- [EU AI Act Implementation Timeline](https://artificialintelligenceact.eu/implementation-timeline/)
- [EU AI Act 2026 — Legal Nodes](https://www.legalnodes.com/article/eu-ai-act-2026-updates-compliance-requirements-and-business-risks)
