# CCPA / CPRA Non-Applicability Statement

**Application:** Timer Studio  
**Date:** 2026-05-09  
**Author:** Matthew Pittman

---

## Summary

Timer Studio does not meet the business thresholds required for CCPA/CPRA compliance obligations to apply. This document records the basis for that determination.

---

## California Consumer Privacy Act (CCPA) & CPRA Amended Regulations

The CCPA (Cal. Civ. Code § 1798.100 et seq.), as amended by CPRA (effective January 1, 2023; amended regulations effective January 1, 2026), applies to **for-profit businesses** that meet **at least one** of the following annual thresholds:

| Threshold | CCPA/CPRA Requirement | Timer Studio |
|---|---|---|
| Annual gross revenue | > $25,000,000 | Does not apply — personal/hobby project |
| Personal information bought/sold/received/shared | ≥ 100,000 consumers or households per year | Does not apply — no personal information collected or shared |
| Revenue from selling/sharing personal information | ≥ 50% of annual revenue | Does not apply — no personal information sold or shared |

---

## Data Handling Summary

Timer Studio stores **only** the following data:

- **timers.json** — timer configurations (names, durations, alert settings, notes). Stored locally in `%APPDATA%\TimerStudio\` on the user's own device. Never transmitted.
- **Calendar import** — ICS files and Outlook COM calendar data are read into memory at import time only. Nothing is written to disk.
- **AppPrefs** — user interface preferences (window size, theme). Stored locally. Never transmitted.

No personal information is collected, processed, sold, shared, or transmitted to any third party. Timer Studio makes no outbound network requests.

---

## Conclusion

Timer Studio is a local desktop application operated by an individual developer. It does not meet the revenue, data volume, or data-sharing thresholds required for CCPA/CPRA obligations to apply.

No CCPA/CPRA compliance controls (opt-out links, privacy policy pages, consumer rights workflows) are required.

---

## Review Schedule

This determination should be re-evaluated if:
- The application is monetized above the $25M gross revenue threshold
- A data analytics or cloud-sync feature is added
- The application is transferred to a for-profit entity

Next scheduled review: **2027-01-01**
