using System.Windows.Media;

namespace BadgeOverlay
{
    /// <summary>
    /// Single source of truth for the colours used by the badge surface
    /// itself — referenced by <see cref="BadgeColorsWindow"/> when
    /// computing WCAG contrast ratios, and by the overlay XAML when
    /// drawing the badge. Keeping them here prevents drift: change the
    /// overlay colour and the contrast readout updates automatically.
    ///
    /// If you change these, verify <see cref="BadgeColorsWindow"/>'s
    /// contrast readout still passes AAA for the default palette.
    /// </summary>
    internal static class BadgeSurface
    {
        /// <summary>Dark semi-opaque pill behind the label.</summary>
        public static readonly Color BodyBackground = Color.FromRgb(0x1E, 0x1E, 0x2E);

        /// <summary>Tinted pill behind the category icon (slightly lighter
        /// than <see cref="BodyBackground"/> to create a visible step).</summary>
        public static readonly Color IconPillBackground = Color.FromRgb(0x26, 0x26, 0x36);
    }
}
