namespace BadgeOverlay
{
    /// <summary>
    /// Central registry for UI / accessibility constants. Centralised
    /// here so a future tweak (e.g. tightening contrast threshold,
    /// shortening the save debounce on SSD-equipped users, raising
    /// the touch-target size in a tablet build) is a single-line edit
    /// rather than a grep-and-replace exercise across every XAML and
    /// code-behind file.
    ///
    /// Every constant here has a documented WCAG / Microsoft Style
    /// Guide / empirical justification — when adding values, do not
    /// add a new constant without a matching one-line rationale.
    /// </summary>
    internal static class UiConstants
    {
        // ── WCAG contrast thresholds ──────────────────────────────────────
        //
        // WCAG 1.4.6 (Level AAA) requires 7:1 contrast for normal text and
        // 4.5:1 for large text (≥18 pt or 14 pt bold). 1.4.3 (Level AA)
        // requires 4.5:1 / 3:1 for the same. We target AAA across the app.

        /// <summary>WCAG AAA — minimum contrast ratio for normal text.</summary>
        public const double ContrastAAANormal = 7.0;

        /// <summary>WCAG AAA — minimum contrast ratio for large text (≥18 pt, or ≥14 pt bold).</summary>
        public const double ContrastAAALarge = 4.5;

        /// <summary>WCAG AA — minimum contrast ratio for normal text.</summary>
        public const double ContrastAANormal = 4.5;

        /// <summary>WCAG AA — minimum contrast ratio for large text.</summary>
        public const double ContrastAALarge = 3.0;

        /// <summary>WCAG 1.4.11 — non-text contrast (UI components, focus rings).</summary>
        public const double ContrastNonText = 3.0;

        // ── Touch / click target sizing ───────────────────────────────────
        //
        // WCAG 2.5.5 (Level AAA) specifies 44×44 CSS pixels. Microsoft's
        // Fluent guidelines recommend 40 px for desktop dense UIs. We
        // hold to 44 for primary action buttons and use 32–36 in dense
        // settings panels with explicit AccessibilityLog deviation.

        /// <summary>WCAG 2.5.5 AAA — minimum touch/click target dimension (px).</summary>
        public const double TargetSizeAAA = 44.0;

        /// <summary>Compact-density target (settings panels, list items). Documented deviation from AAA.</summary>
        public const double TargetSizeCompact = 32.0;

        /// <summary>Microsoft Fluent recommendation for desktop button height.</summary>
        public const double TargetSizeStandard = 40.0;

        // ── Focus ring ────────────────────────────────────────────────────
        //
        // WCAG 2.4.7 requires visible focus; 2.4.11 (AAA) sets specific
        // contrast and area requirements. Our focus rings use Cyan
        // (#00E5FF on dark) and Gold (#7C4A00 on light) for >7:1 contrast
        // against any surface colour.

        /// <summary>Standard focus ring stroke width (px).</summary>
        public const double FocusRingThickness = 2.0;

        /// <summary>Heavy focus ring stroke width — used on sliders where the thumb sits on a coloured track.</summary>
        public const double FocusRingThicknessHeavy = 3.0;

        /// <summary>Outset of the focus ring beyond the control border (px). Negative = outside.</summary>
        public const double FocusRingMargin = 3.0;

        // ── Persistence / debounce ────────────────────────────────────────

        /// <summary>
        /// Settings-save debounce window. Live-saving on every slider
        /// movement would write to disk hundreds of times per second.
        /// 1500 ms balances "user gets a fresh state if they yank the
        /// power" against SSD wear and disk-IO churn.
        /// </summary>
        public const int SettingsSaveDebounceMs = 1500;

        /// <summary>
        /// UIA detector base poll interval. Adapts to AC/battery state
        /// in the worker loop.
        /// </summary>
        public const int UiaThrottleMs = 150;

        /// <summary>UIA poll interval on battery — slower to preserve power.</summary>
        public const int UiaThrottleBatteryMs = 250;

        // ── Theme animation ───────────────────────────────────────────────

        /// <summary>One-side duration of theme cross-fade (ms). Fade-out + swap + fade-in totals ~120 ms.</summary>
        public const int ThemeFadeMs = 60;

        // ── Tooltip ───────────────────────────────────────────────────────

        /// <summary>Tooltip initial show delay (ms). WCAG 1.4.13 — must be persistent and dismissable.</summary>
        public const int TooltipInitialDelayMs = 200;

        /// <summary>Tooltip max display duration (ms). Microsoft recommends 60 s upper bound for motor-impaired users.</summary>
        public const int TooltipShowDurationMs = 60_000;
    }
}
