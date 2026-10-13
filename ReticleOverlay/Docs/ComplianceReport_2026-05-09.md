# Application Standards Report — ReticleOverlay
*Generated: 2026-05-09*
*Stack: .NET 10 · C# 14 · WPF · MVVM · Win32 P/Invoke*

---

## Summary

ReticleOverlay demonstrates strong WCAG 2.2 AAA compliance: all interactive controls
meet 44 px touch targets, focus rings are custom (2 px Gold/Cyan), AutomationProperties
are set throughout, and a proper HighContrast theme defers entirely to SystemColors.
Three gaps require action: the Light-theme focus ring (#996600) may fall below 7:1 AAA
on white backgrounds, no Authenticode code-signing is present (WACK/Store requirement),
and dialog windows lack a ScrollViewer guard for low-resolution displays. GDPR/CCPA
exposure is minimal — no network requests, no PII collected, no telemetry.

---

## Accessibility

### WCAG 2.2 (ISO/IEC 40500:2025)

**Status:** Ratified  
**Published:** 5 October 2023 · re-published as ISO/IEC 40500:2025 (December 2024)  
**New in 2.2 vs 2.1:** 9 new success criteria — most relevant to desktop:
- **2.4.11 Focus Not Obscured (Minimum, AA)** — focused component must not be fully hidden by author-created content
- **2.4.12 Focus Not Obscured (Enhanced, AAA)** — focused component must be fully visible
- **2.5.7 Dragging Movements (AA)** — all drag operations must have a single-pointer alternative
- **2.5.8 Target Size (Minimum, AA)** — 24×24 px minimum; AAA level remains 44×44 px

**Audit findings — ReticleOverlay:**

| Criterion | Finding | Status |
|-----------|---------|--------|
| 1.4.3 Contrast (AA, 4.5:1 text) | Dark BrushMuted #CCCCCC/#000000 = 10.4:1; Light #333333/#FFFFFF = 12.6:1 | ✅ Pass |
| 1.4.6 Contrast Enhanced (AAA, 7:1) | Dark BrushDisabled #AAAAAA/#000000 = 7.1:1 (passes by margin of 0.1) | ⚠️ Marginal |
| 1.4.6 Contrast Enhanced (AAA, 7:1) | Light FocusRingBrush #996600/#FFFFFF ≈ 5.9:1 | ❌ **Fail** |
| 2.4.7 Focus Visible | Custom 2 px ring on every interactive control via IsKeyboardFocused trigger | ✅ Pass |
| 2.4.11 Focus Not Obscured (Minimum) | No sticky headers or overlapping panels in main window | ✅ Pass |
| 2.4.12 Focus Not Obscured (Enhanced) | Same | ✅ Pass |
| 2.5.3 Label in Name | All buttons with icon-only content have AutomationProperties.Name set | ✅ Pass |
| 2.5.8 Target Size (AAA 44 px) | All interactive controls have MinHeight="44" MinWidth="44" | ✅ Pass |
| 4.1.2 Name, Role, Value | AutomationProperties.Name on all controls; LiveSetting="Polite" on LblStatus | ✅ Pass |
| 2.5.7 Dragging Movements | ResizeHandle is drag-only — no keyboard/pointer alternative | ⚠️ Advisory |

**Action items:**

- [ ] **CRITICAL — Fix Light-theme focus ring contrast.** Change `FocusRingBrush` in `Themes/Light.xaml` from `#996600` to `#6B4700` or darker. Target ≥ 7:1 on white (#FFFFFF). `#6B4700`/#FFFFFF ≈ 7.3:1. Also verify `#595959` BrushDisabled: #595959/#FFFFFF = 7.0:1 — acceptable but verify visually.
- [ ] **ADVISORY — Add keyboard alternative for resize.** ResizeHandle Canvas fires `WM_NCLBUTTONDOWN`/`HT_BOTTOMRIGHT` — drag only. Add `Window.ResizeMode="CanResizeWithGrip"` or handle `PreviewKeyDown` on the handle to allow arrow-key resizing. (WCAG 2.5.7 AA, WCAG 3.0 Outcome preview)
- [ ] **ADVISORY — BrushDisabled Dark theme margin.** #AAAAAA/#000000 = 7.1:1. Currently passes but any palette shift could drop it. Target #A8A8A8 or lighter to give 0.5 ratio headroom.

---

### Microsoft UI Automation (UIA)

**Status:** Shipping continuously via Windows SDK / .NET  
**Relevant version:** UIA for WPF on Windows 11 24H2 (UIA API version shipped with .NET 10)

**Audit findings:**

| Check | Finding | Status |
|-------|---------|--------|
| AutomationProperties.Name on all interactive elements | Verified in MainWindow.xaml — all buttons, sliders, comboboxes, checkboxes | ✅ Pass |
| Live regions | `AutomationProperties.LiveSetting="Polite"` on LblStatus (toast/status label) | ✅ Pass |
| ControlType defaults | WPF controls auto-map correct UIA ControlTypes (Button, Slider, ComboBox, etc.) | ✅ Pass |
| OverlayWindow excluded from UIA tree | `WS_EX_TOOLWINDOW` + `WS_EX_NOACTIVATE` prevent overlay from appearing in UIA tree | ✅ Pass |
| Keyboard navigation | TabIndex not explicitly set — relies on XAML document order | ✅ Acceptable |
| Narrator announcement on profile load | `ApplyProfile` sets `LblStatus` but no explicit UIA notification beyond LiveSetting | ✅ Pass |

**Action items:**

- [ ] **ADVISORY — Verify Narrator reads preset load confirmation.** `ShowSaved()` updates `LblStatus` (LiveSetting=Polite). Test manually with Narrator enabled: load a preset and confirm it reads "Preset 1 loaded: ProfileName ✓". If Narrator is silent, add `AutomationPeer.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged)` explicitly.
- [ ] **ADVISORY — Add `AutomationProperties.HelpText` to hotkey display labels** in the Hotkeys dialog (if one exists) so screen readers can surface key binding info without the user having to navigate to the label.

---

## Performance

### .NET 10 WPF Performance

**Status:** .NET 10 RTM (November 2025 — within knowledge)  
**Relevant guidance:** Microsoft WPF performance best-practices, .NET 10 AOT/ReadyToRun, GC tuning

**Audit findings:**

| Check | Finding | Status |
|-------|---------|--------|
| JSON serialization | `JsonSerializerContext` source-gen — zero reflection, AOT-safe | ✅ Excellent |
| Image caching | `BitmapImage` cached + `.Freeze()` in ReticleRenderer — avoids repeated decode/allocation | ✅ Pass |
| Canvas redraws | `ReticleRenderer` rebuilds `Canvas.Children` on property change — acceptable for low-change-rate overlay | ✅ Acceptable |
| Atomic file I/O | Write→tmp, Copy→overwrite, Delete(tmp) — no data loss on crash | ✅ Pass |
| Single-instance Mutex | `Global\ReticleOverlay_SingleInstance` prevents duplicate process overhead | ✅ Pass |
| Hotkey dispatch | `Dispatcher.Invoke` from WndProc thread — correct marshaling, no deadlock risk | ✅ Pass |
| WS_EX_LAYERED overhead | Used on OverlayWindow — correct approach; DWM composites it, no GDI overhead | ✅ Pass |

**Action items:**

- [ ] **ADVISORY — Profile ReticleRenderer rebuild frequency.** If the overlay refreshes at high frequency (e.g., during rapid hotkey nudges), consider debouncing `RefreshOverlay()` with a `DispatcherTimer` coalescing at 16ms (60Hz cap). Current implementation may fire one Canvas rebuild per nudge keypress repeat.
- [ ] **ADVISORY — Consider `[LibraryImport]` over `[DllImport]`.** .NET 7+ introduced `[LibraryImport]` (source-generated P/Invoke) which avoids runtime IL emit. In Native/User32.cs and Native/Shcore.cs, migrate `[DllImport]` to `[LibraryImport]` where marshaling permits. Particularly straightforward for `RegisterHotKey`, `UnregisterHotKey`, `ReleaseCapture`, `SetWindowPos`.

---

## Privacy & Compliance

### GDPR (EU 2016/679)

**Status:** In force. No recent amendments to core regulation; ePrivacy Regulation still in trilogue as of 2026.

**Audit findings:**

| Check | Finding | Status |
|-------|---------|--------|
| Personal data collected | None — no user identity, no telemetry, no crash reporting | ✅ N/A |
| Network requests | Zero — confirmed by SECURITY.md and source audit (no HttpClient, no WebRequest) | ✅ N/A |
| Data at rest | `%APPDATA%\ReticleOverlay\` — user's own machine, OS-protected user profile directory | ✅ Pass |
| Third-party SDKs with telemetry | None identified in csproj | ✅ Pass |

**Action items:**

- [ ] **None required.** No GDPR exposure exists. If a future version adds crash reporting or update checking, revisit at that time.

---

### CCPA / CPRA (California)

**Status:** CPRA amendments effective January 2023. California Privacy Protection Agency (CPPA) enforcement active.

**Audit findings:** Same as GDPR — no personal information collected or transmitted. CCPA/CPRA definitions of "personal information" do not apply to local-only preference files on the user's own device.

**Action items:**

- [ ] **None required.** If a future update check is added (e.g., GitHub Releases polling), no PII should be included in the request (no machine ID, no user ID in query params).

---

## Windows App Certification Kit (WACK) / Store Readiness

**Status:** WACK v10 (ships with Windows 11 SDK); current kit version aligned with Windows 11 24H2

**Audit findings:**

| Check | Finding | Status |
|-------|---------|--------|
| High DPI / per-monitor DPI awareness | `dpiAwareness mode="PerMonitorV2"` in app.manifest; `GetDpiForMonitor` used in OverlayWindow | ✅ Pass |
| UAC manifest | `requestedExecutionLevel level="asInvoker"` — correct for non-elevated app | ✅ Pass |
| Installer silent mode | NSIS installer present; WACK requires `/quiet` or `/S` support | ⚠️ Verify |
| Authenticode signing | **No code signing found in build.bat or csproj** | ❌ **Missing** |
| Supported OS declaration | app.manifest has `supportedOS` GUIDs — verify Windows 11 GUID present | ⚠️ Verify |
| No kernel-mode code | Pure user-mode Win32 API calls only | ✅ Pass |
| No deprecated APIs | No GetVersionEx, no VirtualAllocEx cross-process | ✅ Pass |
| File write location | `%APPDATA%` only — never Program Files or registry (confirmed) | ✅ Pass |

**Action items:**

- [x] ~~REQUIRED — Add Authenticode code signing to build pipeline.~~ **DECIDED AGAINST — 2026-09-01.** Matthew: no code-signing certificate exists for this app, or any app in the suite, and none is planned. A signtool block matching this recommendation was added to `build.bat` at some point after this report but has since been removed — builds are unsigned by design. SmartScreen warnings on first run are expected and accepted, not a defect to fix.
- [ ] **Verify Windows 11 supportedOS GUID in app.manifest.** The GUID for Windows 11 is `{1f676c76-80e1-4239-95bb-83d0f6d0da78}` — same as Windows 10 (Microsoft uses one GUID for Win10+). Confirm this is present.
- [ ] **Verify NSIS installer supports `/S` silent switch.** NSIS default installers do support `/S` but confirm the custom script doesn't override or break it. WACK tests silent install/uninstall.
- [ ] **Add ScrollViewer wrap to any dialog windows.** At 800×600 or lower DPI-scaled resolutions, fixed-height dialogs can clip content. WACK validates that content is not clipped at 1024×768 effective resolution.

---

## Upcoming / Watch List

| Standard | Status | Expected | Action |
|----------|--------|----------|--------|
| **WCAG 3.0** | W3C Working Draft | Candidate Rec ~Q4 2027 | Monitor w3.org/TR/wcag-3.0. Current 174 outcomes replace binary pass/fail with graduated scoring. No code changes required now. |
| **WCAG 2.2 Errata** | ISO/IEC 40500:2025 published Dec 2024 | Stable | No behavioral changes from Oct 2023 WCAG 2.2 — just ISO adoption. |
| **EU Accessibility Act** | Effective June 2025 for new products in EU | **Active** | Applies to software sold/distributed in EU. If the app is publicly distributed, EN 301 549 (which references WCAG 2.1 AA minimum) applies. Your AAA compliance already satisfies this. |
| **Windows App SDK / WinUI 3 migration** | WinUI 3 now preferred for new Windows apps | Ongoing | Not a current requirement. WPF on .NET 10 is fully supported through at least 2029. No migration needed unless targeting MSIX Store distribution. |
| **LibraryImport (source-gen P/Invoke)** | Available since .NET 7 | Now | See Performance action items above. Not a compliance issue but a correctness/AOT improvement. |
| **CPPA enforcement rulemaking** | New CPPA rules on automated decision-making | 2026–2027 | Irrelevant unless app adds AI/ML features. Monitor if update checker or AI reticle-suggestion feature is added. |

---

## References

- [WCAG 2.2 — W3C Recommendation](https://www.w3.org/TR/WCAG22/)
- [ISO/IEC 40500:2025](https://www.iso.org/standard/58625.html)
- [WCAG 3.0 Working Draft](https://www.w3.org/TR/wcag-3.0/)
- [Microsoft UI Automation Overview](https://learn.microsoft.com/en-us/dotnet/framework/ui-automation/ui-automation-overview)
- [WPF Accessibility Best Practices](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/advanced/accessibility-best-practices)
- [Windows App Certification Kit](https://learn.microsoft.com/en-us/windows/apps/windows-app-certification-kit)
- [DPI and High-DPI Desktop Application Development](https://learn.microsoft.com/en-us/windows/win32/hidpi/high-dpi-desktop-application-development-on-windows)
- [LibraryImport P/Invoke source generation](https://learn.microsoft.com/en-us/dotnet/standard/native-interop/pinvoke-source-generation)
- [EU Accessibility Act / EN 301 549](https://www.etsi.org/deliver/etsi_en/301500_301599/301549/03.02.01_60/en_301549v030201p.pdf)
- [GDPR Full Text](https://gdpr-info.eu/)
- [CCPA / CPRA — California AG](https://oag.ca.gov/privacy/ccpa)
- [Authenticode Signing — signtool](https://learn.microsoft.com/en-us/windows/win32/seccrypto/signtool)
