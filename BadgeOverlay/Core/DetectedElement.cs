using System.Windows.Media;

namespace BadgeOverlay
{
    /// <summary>
    /// Source that produced a detected element. Used to tag snapshots so
    /// the overlay can reason about freshness and about which layer of
    /// the detection stack found the name.
    /// </summary>
    public enum DetectionSource
    {
        /// <summary>Won from <see cref="ButtonDetector"/> via Win32 class /
        /// text classification.</summary>
        Win32,
        /// <summary>Won from <see cref="UIADetector"/> via AutomationElement
        /// hit-testing.</summary>
        UIA,
        /// <summary>Won from <see cref="MsaaDetector"/> via IAccessible /
        /// AccessibleObjectFromPoint.</summary>
        MSAA,
        /// <summary>Matched a user-defined custom rule.</summary>
        CustomRule,
        /// <summary>Neutral fallback shown when nothing is detected.</summary>
        Neutral,
    }

    /// <summary>
    /// Single unified result type produced by the detection pipeline.
    /// Consumed by the overlay for badge content + positioning and by the
    /// info panel for low-vision display.
    ///
    /// Replaces the earlier split between <c>DetectedButton</c> (Win32
    /// result with colour fields resolved) and <c>UIAResult</c> (UIA
    /// snapshot with origin + class-name metadata). Fields that used to
    /// live on only one of those types are now merged here, with
    /// colour-resolution deferred to the caller when needed.
    /// </summary>
    public sealed class DetectedElement
    {
        public string Label     { get; set; } = "";
        public string Category  { get; set; } = DetectorLabels.Cat.Control;
        public string Icon      { get; set; } = "◉";

        /// <summary>Resolved badge foreground colour. Zero when the caller
        /// has not yet resolved category → colour.</summary>
        public Color  BadgeColor  { get; set; }
        /// <summary>Resolved badge border colour (typically Lighten(BadgeColor)).</summary>
        public Color  BorderColor { get; set; }

        // UIA-specific fields — null / empty for Win32-origin results
        public string HelpText    { get; set; } = "";
        public string ClassName   { get; set; } = "";
        public bool   IsEnabled   { get; set; } = true;
        public DetectionSource Source { get; set; } = DetectionSource.Win32;

        // ── Extended metadata (opt-in display) ────────────────────────────────
        // Populated by the detection pipeline when the information is
        // readily available. InfoOverlay renders these on separate lines
        // below the primary label; the compact badge does not — it
        // would blow out the width budget for a cursor-following pill.

        /// <summary>Keyboard shortcut for the element (UIA
        /// <c>AcceleratorKey</c>), e.g. "Ctrl+S". Empty when none.</summary>
        public string AcceleratorKey { get; set; } = "";

        /// <summary>Link target URL when hovering a hyperlink. Empty for
        /// non-link elements. Extracted with care — URLs may contain
        /// credentials or auth tokens; only surfaced to the user, never
        /// logged.</summary>
        public string LinkUrl { get; set; } = "";

        /// <summary>Toggle state for checkboxes / toggle buttons — "On",
        /// "Off", "Indeterminate", or "" when not a toggle.</summary>
        public string ToggleState { get; set; } = "";

        /// <summary>Selection state for list / grid items — "Selected",
        /// "Not selected", or "" when not a selection item.</summary>
        public string SelectionState { get; set; } = "";

        /// <summary>Current value for Value / RangeValue controls — e.g.
        /// "Volume 65%", "Brightness 60 / 100", the text in an edit
        /// field. Empty when no value pattern.</summary>
        public string ValueText { get; set; } = "";

        /// <summary>Friendly name of the process owning the hwnd under
        /// cursor — e.g. "chrome", "Code", "notepad". Always safe to
        /// display; never contains personal data.</summary>
        public string ProcessName { get; set; } = "";

        /// <summary>Full path to the executable that owns the hwnd under
        /// cursor. May be empty when access is denied (elevated process,
        /// anti-cheat, etc.). Useful diagnostic for "what app is this?"
        /// but should not be surfaced by default — path can contain
        /// user-profile segments. Tooltip-on-demand only.</summary>
        public string ProcessPath { get; set; } = "";

        /// <summary>For shell / Explorer items: "File", "Folder", "PDF
        /// Document", etc. UIA <c>ItemType</c>. Empty otherwise.</summary>
        public string ItemType { get; set; } = "";

        /// <summary>UIA <c>ItemStatus</c> — "3 unread", "Sync pending",
        /// "Modified". Rarely populated but rich when present.</summary>
        public string ItemStatus { get; set; } = "";

        /// <summary>True when the element is a required form field
        /// (UIA <c>IsRequiredForForm</c>). Surfaced as "* required" in
        /// the info panel so users filling in web/WPF forms know before
        /// they try to submit.</summary>
        public bool IsRequired { get; set; } = false;

        /// <summary>Browser tab URL when the element is a browser TabItem
        /// (Chrome/Edge), or empty otherwise. Already redacted at the
        /// browser level; we surface as-is.</summary>
        public string TabUrl { get; set; } = "";

        /// <summary>Scroll position as a human-readable string —
        /// "H 42% · V 15%" or just "V 87%". Empty when the element
        /// doesn't support ScrollPattern or scrolling is not applicable
        /// on that axis.</summary>
        public string ScrollPercent { get; set; } = "";

        /// <summary>Screen-space cursor position at the moment the
        /// detector produced this element. Used for UIA staleness
        /// checks via <see cref="UIADetector.StalenessPixels"/>.</summary>
        public int OriginX { get; set; }
        public int OriginY { get; set; }

        public DetectedElement() { }

        public DetectedElement(string label, string category, string icon)
        {
            Label    = label    ?? "";
            Category = category ?? DetectorLabels.Cat.Control;
            Icon     = icon     ?? "◉";
        }

        public DetectedElement(string label, string category, string icon,
                               Color badge, Color border)
            : this(label, category, icon)
        {
            BadgeColor  = badge;
            BorderColor = border;
        }
    }
}
