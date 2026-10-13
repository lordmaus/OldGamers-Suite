using System;

namespace BadgeOverlay
{
    /// <summary>
    /// A persisted snapshot of badge-appearance and behaviour settings
    /// that can be recalled with one button press from Section 5.
    ///
    /// Presets capture the subset of settings users typically vary
    /// between sessions or contexts — badge size, opacity, position,
    /// visibility flags, and UI scale.  Theme, hotkeys, window
    /// geometry, and custom rules are intentionally excluded: those
    /// are machine-level choices, not per-task ones.
    ///
    /// Serialisation note: BadgePreset is registered in
    /// AppSettingsJsonContext so source-generated JSON handles it
    /// correctly.  BadgeSide is already registered; it is reused here
    /// without a second registration.
    /// </summary>
    public sealed class BadgePreset
    {
        // ── Identity ──────────────────────────────────────────────────────
        /// <summary>
        /// Optional user-supplied label.  Empty = UI shows slot number.
        /// </summary>
        public string Name    { get; set; } = "";

        /// <summary>True when this slot has never been saved to.</summary>
        public bool   IsEmpty { get; set; } = true;

        // ── Badge appearance ──────────────────────────────────────────────
        public double    BadgeOpacity  { get; set; } = 0.95;
        public double    BadgeScale    { get; set; } = 1.0;
        public double    BadgeOffsetX  { get; set; } = 20;
        public double    BadgeOffsetY  { get; set; } = 20;
        public BadgeSide BadgePosition { get; set; } = BadgeSide.BottomRight;

        // ── Visibility flags ──────────────────────────────────────────────
        public bool ShowBadge       { get; set; } = true;
        public bool BadgeHidden     { get; set; } = false;
        public bool BadgePinned     { get; set; } = false;
        public bool ShowInfoOverlay { get; set; } = true;

        // ── Behaviour ─────────────────────────────────────────────────────
        public int    HoverDelayMs  { get; set; } = 0;
        public bool   ReduceMotion  { get; set; } = false;
        public double UiScale       { get; set; } = 1.0;
    }

    public partial class AppSettings
    {
        // ── Preset slots ──────────────────────────────────────────────────
        /// <summary>
        /// Five quick-recall preset slots indexed 0–4 (UI labels 1–5).
        /// Each slot is an independent badge-settings snapshot.
        /// Empty slots (IsEmpty=true) are preserved in JSON so slot
        /// indices remain stable across saves.
        /// </summary>
        public BadgePreset[] Presets { get; set; } = new BadgePreset[]
        {
            new BadgePreset(), new BadgePreset(), new BadgePreset(),
            new BadgePreset(), new BadgePreset(),
        };

        /// <summary>
        /// Index of the most recently recalled or saved preset (0–4),
        /// or -1 when no preset is active.  Drives the active-slot
        /// highlight in the UI and targets the Save button.
        /// </summary>
        public int ActivePresetIndex { get; set; } = -1;

        // ── Capture / apply ───────────────────────────────────────────────

        /// <summary>
        /// Captures current badge and behaviour settings into slot
        /// <paramref name="index"/> (0–4).  Marks the slot non-empty
        /// and sets <see cref="ActivePresetIndex"/>.
        /// </summary>
        public void SaveToPreset(int index)
        {
            if ((uint)index >= (uint)Presets.Length) return;
            var p = Presets[index] ?? (Presets[index] = new BadgePreset());

            p.IsEmpty       = false;
            p.BadgeOpacity  = BadgeOpacity;
            p.BadgeScale    = BadgeScale;
            p.BadgeOffsetX  = BadgeOffsetX;
            p.BadgeOffsetY  = BadgeOffsetY;
            p.BadgePosition = BadgePosition;
            p.ShowBadge     = ShowBadge;
            p.BadgeHidden   = BadgeHidden;
            p.BadgePinned   = BadgePinned;
            p.ShowInfoOverlay = ShowInfoOverlay;
            p.HoverDelayMs  = HoverDelayMs;
            p.ReduceMotion  = ReduceMotion;
            p.UiScale       = UiScale;

            ActivePresetIndex = index;
        }

        /// <summary>
        /// Restores settings from slot <paramref name="index"/> (0–4)
        /// into this AppSettings instance.  No-op when the slot is
        /// empty.  Sets <see cref="ActivePresetIndex"/> on success.
        /// </summary>
        /// <returns>
        /// <see langword="true"/> when the slot was non-empty and
        /// was applied; <see langword="false"/> when it was empty.
        /// </returns>
        public bool LoadFromPreset(int index)
        {
            if ((uint)index >= (uint)Presets.Length) return false;
            var p = Presets[index];
            if (p == null || p.IsEmpty) return false;

            BadgeOpacity    = p.BadgeOpacity;
            BadgeScale      = p.BadgeScale;
            BadgeOffsetX    = p.BadgeOffsetX;
            BadgeOffsetY    = p.BadgeOffsetY;
            BadgePosition   = p.BadgePosition;
            ShowBadge       = p.ShowBadge;
            BadgeHidden     = p.BadgeHidden;
            BadgePinned     = p.BadgePinned;
            ShowInfoOverlay = p.ShowInfoOverlay;
            HoverDelayMs    = p.HoverDelayMs;
            ReduceMotion    = p.ReduceMotion;
            UiScale         = p.UiScale;

            ActivePresetIndex = index;
            return true;
        }

        /// <summary>
        /// Ensures the Presets array is always exactly 5 non-null
        /// elements.  Fills gaps with fresh empty presets.  Call after
        /// deserialization to guard against null / corrupt JSON and
        /// future schema changes that alter the array length.
        /// </summary>
        public void EnsurePresets()
        {
            if (Presets == null || Presets.Length != 5)
            {
                var p    = new BadgePreset[5];
                int have = Presets?.Length ?? 0;
                for (int i = 0; i < Math.Min(have, 5); i++)
                    p[i] = Presets[i] ?? new BadgePreset();
                for (int i = have; i < 5; i++)
                    p[i] = new BadgePreset();
                Presets = p;
            }
            else
            {
                for (int i = 0; i < 5; i++)
                    Presets[i] ??= new BadgePreset();
            }
        }
    }
}
