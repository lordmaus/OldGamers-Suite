using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;

namespace SuperMagnifier
{
    /// <summary>
    /// Samples the pixel under the mouse cursor from the screen (source rect)
    /// and copies the hex and RGB values to the clipboard.
    /// Called when the user clicks the lens in pick mode.
    /// </summary>
    internal static class ColourPicker
    {
        [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr hWnd);
        [DllImport("user32.dll")] static extern int    ReleaseDC(IntPtr hWnd, IntPtr hDC);
        [DllImport("gdi32.dll")]  static extern uint   GetPixel(IntPtr hDC, int x, int y);
        // GetPhysicalCursorPos returns true device-pixel coordinates (not DPI-scaled
        // logical coords). On a secondary monitor at 150% DPI, GetCursorPos returns coords
        // that are 1.5× smaller than the physical address, causing wrong pixel sampling.
        [DllImport("user32.dll")] static extern bool   GetPhysicalCursorPos(out POINT pt);

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT { public int X, Y; }

        /// <summary>
        /// Samples the current cursor pixel on screen and copies to clipboard.
        /// Returns the colour string for UI feedback, or null on failure.
        /// </summary>
        public static string PickAndCopy()
        {
            try
            {
                if (!GetPhysicalCursorPos(out POINT pt)) return null;

                IntPtr dc    = GetDC(IntPtr.Zero);
                uint   pixel = GetPixel(dc, pt.X, pt.Y);
                ReleaseDC(IntPtr.Zero, dc);

                byte r = (byte)( pixel        & 0xFF);
                byte g = (byte)((pixel >>  8) & 0xFF);
                byte b = (byte)((pixel >> 16) & 0xFF);

                string hex = $"#{r:X2}{g:X2}{b:X2}";
                string rgb = $"rgb({r}, {g}, {b})";
                string text = $"{hex}  {rgb}";

                Clipboard.SetText(text);
                return hex;
            }
            catch { return null; }
        }

        // C-4 (SuperMagnifierLV_CodeSecA11y_Review_2026-09-14): 13 near-identical
        // try { ColorConverter.ConvertFromString(...) } catch {} blocks were scattered
        // across MainWindow.xaml.cs and LensWindow.xaml.cs — one already differed subtly
        // (some left the swatch unchanged on failure, others didn't). Consolidated here.

        /// <summary>Attempts to parse a hex color string. Returns false (color left at
        /// its default) on any parse failure — never throws.</summary>
        public static bool TryParseColor(string hex, out Color color)
        {
            try
            {
                color = (Color)ColorConverter.ConvertFromString(hex);
                return true;
            }
            catch
            {
                color = default;
                return false;
            }
        }

        /// <summary>Parses a hex color string, falling back to <paramref name="fallback"/>
        /// on any parse failure.</summary>
        public static Color ParseColorOrDefault(string hex, Color fallback)
            => TryParseColor(hex, out var c) ? c : fallback;
    }
}
