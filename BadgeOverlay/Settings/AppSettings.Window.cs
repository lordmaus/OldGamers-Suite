using System.Collections.Generic;

namespace BadgeOverlay
{
    /// <summary>
    /// Persisted window-geometry state for both MainWindow and the
    /// InfoOverlay panel, plus the user-defined rules collection.
    ///
    /// Geometry uses a -1 sentinel for "not yet set" rather than
    /// double.NaN — older builds wrote NaN, which JsonSerializer
    /// rejects with ArgumentException; readers tolerate both for
    /// backward compatibility (see OverlayWindow.PositionBadgePinned
    /// and InfoOverlay.RestorePosition).
    ///
    /// SanitiseDoubles() in the main AppSettings.cs scrubs these
    /// fields before every Save() so a transient WPF layout pass
    /// (DWM restart, monitor unplug) producing NaN/Infinity can't
    /// corrupt the file.
    /// </summary>
    public partial class AppSettings
    {
        // ── MainWindow geometry ───────────────────────────────────────────
        public double WindowWidth  { get; set; } = 460;
        public double WindowHeight { get; set; } = 1080;
        public double WindowLeft   { get; set; } = -1;
        public double WindowTop    { get; set; } = -1;

        // ── InfoOverlay geometry ──────────────────────────────────────────

        /// <summary>Font size (pt) for the Info Overlay large-text panel.
        /// Persists the user's A+ / A− choice across sessions.</summary>
        public double InfoOverlayFontSize { get; set; } = 24;

        /// <summary>Last saved Info Overlay window position (screen px).
        /// Negative sentinel = not yet set → centre on first show.</summary>
        public double InfoOverlayLeft { get; set; } = -1;
        public double InfoOverlayTop  { get; set; } = -1;

        // ── Custom detection rules ────────────────────────────────────────
        public List<CustomRule> CustomRules { get; set; } = new List<CustomRule>();
    }
}
