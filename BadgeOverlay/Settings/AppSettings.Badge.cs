namespace BadgeOverlay
{
    /// <summary>
    /// Badge-appearance properties — what the inline pill looks like
    /// and where it sits.
    ///
    /// Split out of the main AppSettings.cs so badge-related changes
    /// don't churn the central serialisation file. Categorisation
    /// is by user-facing concern, not by serialiser layout —
    /// SerialiseDoubles in the main file knows about every double in
    /// every partial (search-fill, low-frequency edit).
    /// </summary>
    public partial class AppSettings
    {
        // ── Badge appearance ──────────────────────────────────────────────
        public bool      ShowBadge       { get; set; } = true;
        public double    BadgeOpacity    { get; set; } = 0.95;
        public double    BadgeOffsetX    { get; set; } = 20;
        public double    BadgeOffsetY    { get; set; } = 20;
        public double    BadgeScale      { get; set; } = 1.0;
        public BadgeSide BadgePosition   { get; set; } = BadgeSide.BottomRight;
        /// <summary>Dark (default, previous look) | Light | MatchTheme. See Core/OverlayPalette.cs.</summary>
        public BadgeStyle BadgeStyle     { get; set; } = BadgeStyle.Dark;

        /// <summary>Badge detects buttons but stays invisible (silent mode).</summary>
        public bool      BadgeHidden     { get; set; } = false;

        // ── Pinned-mode positioning ───────────────────────────────────────
        // WCAG 2.2.2 (Pause, Stop, Hide) for moving content. When
        // BadgePinned is on, the badge stops following the cursor and
        // stays at (BadgePinnedX, BadgePinnedY). Detection still runs;
        // only on-screen position is anchored.

        /// <summary>
        /// When true, the badge stays at (BadgePinnedX, BadgePinnedY)
        /// instead of following the cursor. Useful for motor-impaired
        /// users and screen-reader users who find a tracking badge
        /// distracting.
        /// </summary>
        public bool   BadgePinned   { get; set; } = false;

        /// <summary>Pinned badge top-left in screen pixels (not DIPs).
        /// Sentinel -1 = not yet placed; falls back to top-right corner.
        /// Legacy NaN values from older builds also tolerated by the
        /// reader in OverlayWindow.PositionBadgePinned.</summary>
        public double BadgePinnedX  { get; set; } = -1;
        public double BadgePinnedY  { get; set; } = -1;

        // ── Per-category colours ──────────────────────────────────────────
        // Hex strings, one per detection category. Live-edited in
        // BadgeColorsWindow with a WCAG AAA contrast readout per row.
        // Default values are Catppuccin Mocha accents (also the
        // "Default" entry in the palette-preset ComboBox).
        public string BadgeColorClose    { get; set; } = "#F38BA8";
        public string BadgeColorMinimize { get; set; } = "#FAB387";
        public string BadgeColorMaximize { get; set; } = "#A6E3A1";
        public string BadgeColorAction   { get; set; } = "#A6E3A1";
        public string BadgeColorDanger   { get; set; } = "#F38BA8";
        public string BadgeColorNav      { get; set; } = "#89B4FA";
        public string BadgeColorControl  { get; set; } = "#94E2D5";
        public string BadgeColorBrowser  { get; set; } = "#89B4FA";
    }
}
