using System;
using System.Windows;
using System.Windows.Media;

namespace BadgeOverlay
{
    /// <summary>
    /// Colours for the click-through badge and the Info Overlay (BO-05, 2026-10-12).
    /// Published as App-level resources <c>Overlay*</c> so XAML binds with DynamicResource.
    ///
    /// Resolution order:
    ///   1. Windows High Contrast on        → SystemColors (user's HC scheme).
    ///   2. App theme is an HC theme         → that theme's tokens.
    ///   3. Settings → Badge style: Dark | Light | MatchTheme.
    /// Text/muted/warning pairs are ≥ 7:1 against <c>OverlayBg</c> / <c>OverlayBar</c>.
    /// </summary>
    internal static class OverlayPalette
    {
        public const string Bg = "OverlayBg", Bar = "OverlayBar", Text = "OverlayText", Muted = "OverlayMuted",
                            Pill = "OverlayPill", Focus = "OverlayFocus", Warn = "OverlayWarn", Border = "OverlayBorder";

        internal sealed record PaletteColors(Color Bg, Color Bar, Color Text, Color Muted, Color Pill, Color Focus, Color Warn, Color Border);

        internal static readonly PaletteColors Dark = new(
            Bg:     Color.FromArgb(0xE6, 0x1E, 0x1E, 0x2E),
            Bar:    Color.FromRgb(0x2A, 0x2A, 0x3E),
            Text:   Color.FromRgb(0xFF, 0xFF, 0xFF),
            Muted:  Color.FromRgb(0xC9, 0xCE, 0xDF),
            Pill:   Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF),
            Focus:  Color.FromRgb(0xFF, 0xD7, 0x00),
            Warn:   Color.FromRgb(0xFF, 0xD0, 0x80),
            Border: Color.FromRgb(0x8A, 0x90, 0xAA));

        internal static readonly PaletteColors Light = new(
            Bg:     Color.FromArgb(0xF2, 0xFA, 0xFA, 0xF8),
            Bar:    Color.FromRgb(0xE6, 0xE6, 0xE2),
            Text:   Color.FromRgb(0x11, 0x11, 0x11),
            Muted:  Color.FromRgb(0x3A, 0x3A, 0x3A),
            Pill:   Color.FromArgb(0x22, 0x00, 0x00, 0x00),
            Focus:  Color.FromRgb(0x00, 0x50, 0xC8),
            Warn:   Color.FromRgb(0x6B, 0x3F, 0x00),
            Border: Color.FromRgb(0x6B, 0x6B, 0x6B));

        /// <summary>Bumped on every apply — overlays compare it to know cached brushes are stale.</summary>
        public static int Version { get; private set; }
        /// <summary>True when HC colours are in use: badge labels ignore per-category colours.</summary>
        public static bool IsHighContrast { get; private set; }
        /// <summary>Opaque background used for label-contrast checks.</summary>
        public static Color BgOpaque { get; private set; } = Opaque(Dark.Bg);
        public static Color BarColor { get; private set; } = Dark.Bar;

        public static bool IsHcThemeKey(string key) =>
            key != null && (key.StartsWith("HighContrast", StringComparison.Ordinal) || key.StartsWith("HC", StringComparison.Ordinal));

        /// <summary>Picks and publishes the palette. Safe to call repeatedly.</summary>
        public static void Apply(BadgeStyle style, string themeKey, bool windowsHighContrast)
        {
            var res = Application.Current?.Resources;
            if (res == null) return;

            PaletteColors c;
            if (windowsHighContrast)
            {
                c = new PaletteColors(SystemColors.WindowColor, SystemColors.ControlColor, SystemColors.WindowTextColor,
                               SystemColors.WindowTextColor, SystemColors.WindowColor, SystemColors.HighlightColor,
                               SystemColors.WindowTextColor, SystemColors.HighlightColor);
            }
            else if (style == BadgeStyle.MatchTheme || IsHcThemeKey(themeKey))
            {
                c = FromTheme(res) ?? Dark;
            }
            else
            {
                c = style == BadgeStyle.Light ? Light : Dark;
            }

            IsHighContrast = windowsHighContrast || IsHcThemeKey(themeKey);
            BgOpaque = Opaque(c.Bg);
            BarColor = c.Bar;
            Set(res, Bg, c.Bg); Set(res, Bar, c.Bar); Set(res, Text, c.Text); Set(res, Muted, c.Muted);
            Set(res, Pill, c.Pill); Set(res, Focus, c.Focus); Set(res, Warn, c.Warn); Set(res, Border, c.Border);
            Version++;
        }

        /// <summary>
        /// Label/icon brush for a detected element: the user's category colour unless it drops
        /// below 4.5:1 (AAA large text) on the overlay background, or HC is active.
        /// </summary>
        public static SolidColorBrush LabelBrush(Color categoryColor)
        {
            if (IsHighContrast || ContrastHelper.Ratio(categoryColor, BgOpaque) < ContrastHelper.AAALargeText)
                return (Application.Current?.TryFindResource(Text) as SolidColorBrush) ?? Brushes.White;
            var b = new SolidColorBrush(categoryColor);
            b.Freeze();
            return b;
        }

        /// <summary>Category border colour, or the HC highlight in HC mode.</summary>
        public static Color BorderColor(Color categoryColor) =>
            IsHighContrast && Application.Current?.TryFindResource(Border) is SolidColorBrush hc ? hc.Color : categoryColor;

        private static PaletteColors FromTheme(ResourceDictionary res)
        {
            Color? Get(string key) => (Application.Current.TryFindResource(key) as SolidColorBrush)?.Color;
            var surf = Get("BrushSurf"); var text = Get("BrushText");
            if (surf == null || text == null) return null;
            var surf2 = Get("BrushSurf2") ?? surf.Value;
            return new PaletteColors(
                Bg:     Color.FromArgb(0xF2, surf.Value.R, surf.Value.G, surf.Value.B),
                Bar:    surf2,
                Text:   text.Value,
                Muted:  Get("BrushMuted") ?? text.Value,
                Pill:   surf2,
                Focus:  Get("FocusRingBrush") ?? text.Value,
                Warn:   Get("BrushAmberText") ?? text.Value,
                Border: Get("BrushDivider") ?? text.Value);
        }

        private static void Set(ResourceDictionary res, string key, Color c)
        {
            var b = new SolidColorBrush(c);
            b.Freeze();
            res[key] = b;
        }

        private static Color Opaque(Color c) => Color.FromRgb(c.R, c.G, c.B);
    }
}
