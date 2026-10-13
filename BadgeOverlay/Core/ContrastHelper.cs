using System;
using System.Windows.Media;

namespace BadgeOverlay
{
    /// <summary>
    /// WCAG 2.1/2.2 relative-luminance and contrast-ratio helpers.
    ///
    /// Reference: https://www.w3.org/TR/WCAG22/#dfn-relative-luminance
    /// Ratio range: 1.0 (identical colours) → 21.0 (black on white).
    ///
    /// Target ratios (AAA):
    ///   • Normal text ≥ 7.0 : 1
    ///   • Large text  ≥ 4.5 : 1   (≥ 18pt or 14pt bold)
    ///   • UI graphics ≥ 3.0 : 1   (AA component contrast — useful for
    ///                              decorative badge elements)
    /// </summary>
    internal static class ContrastHelper
    {
        /// <summary>WCAG relative luminance of a colour (0.0 – 1.0).</summary>
        public static double Luminance(Color c)
        {
            double r = Channel(c.R / 255.0);
            double g = Channel(c.G / 255.0);
            double b = Channel(c.B / 255.0);
            return 0.2126 * r + 0.7152 * g + 0.0722 * b;
        }

        /// <summary>Contrast ratio between two colours (1.0 – 21.0).</summary>
        public static double Ratio(Color a, Color b)
        {
            double la = Luminance(a);
            double lb = Luminance(b);
            double hi = Math.Max(la, lb);
            double lo = Math.Min(la, lb);
            return (hi + 0.05) / (lo + 0.05);
        }

        /// <summary>AAA-level threshold for normal body text (7:1).</summary>
        public const double AAANormalText = 7.0;
        /// <summary>AAA-level threshold for large text (4.5:1).</summary>
        public const double AAALargeText = 4.5;
        /// <summary>AA component / graphics threshold (3:1).</summary>
        public const double AAComponent = 3.0;

        public static bool MeetsAAA(Color fg, Color bg)     => Ratio(fg, bg) >= AAANormalText;
        public static bool MeetsAAALarge(Color fg, Color bg) => Ratio(fg, bg) >= AAALargeText;
        public static bool MeetsAA(Color fg, Color bg)       => Ratio(fg, bg) >= AAComponent;

        /// <summary>sRGB channel → linear-light value for luminance math.
        /// Threshold is 0.04045 per WCAG 2.1 (corrected from the 0.03928
        /// draft value that appeared in WCAG 2.0).</summary>
        private static double Channel(double v)
            => v <= 0.04045 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4);

        /// <summary>Short textual grade for a ratio. Used in settings UI.</summary>
        public static string Grade(double ratio)
        {
            if (ratio >= AAANormalText) return "AAA";
            if (ratio >= AAALargeText)  return "AAA (large)";
            if (ratio >= AAComponent)   return "AA";
            return "FAIL";
        }
    }
}
