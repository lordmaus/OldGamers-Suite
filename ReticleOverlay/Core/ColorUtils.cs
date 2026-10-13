using System.Windows.Media;

namespace ReticleOverlay
{
    /// <summary>
    /// Shared color helpers. Eliminates the ParseHex/ParseColor duplication
    /// that previously existed independently in MainWindow and ReticleRenderer.
    /// </summary>
    internal static class ColorUtils
    {
        /// <summary>
        /// Parse a hex color string (e.g. "#00FF00") into a <see cref="Color"/>.
        /// Returns <paramref name="fallback"/> if parsing fails.
        /// </summary>
        public static Color Parse(string hex, Color fallback)
        {
            try { return (Color)ColorConverter.ConvertFromString(hex); }
            catch { return fallback; }
        }

        /// <summary>
        /// Parse a hex color string into a <see cref="SolidColorBrush"/>.
        /// Returns a brush of <paramref name="fallback"/> if parsing fails.
        /// </summary>
        public static SolidColorBrush ParseBrush(string hex, Color fallback)
        {
            var b = new SolidColorBrush(Parse(hex, fallback));
            b.Freeze();
            return b;
        }
    }
}
