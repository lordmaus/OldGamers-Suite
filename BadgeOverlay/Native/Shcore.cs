using System;
using System.Runtime.InteropServices;

namespace BadgeOverlay
{
    /// <summary>
    /// shcore.dll P/Invoke surface — per-monitor DPI. See
    /// Native/Common.cs for the partial-class layout overview.
    ///
    /// Tiny on its own, but shcore is its own module loaded by the
    /// runtime separately from user32 / kernel32, so keeping its
    /// imports together (rather than scattered across user32) makes
    /// the dependency boundary obvious during future maintenance.
    /// </summary>
    public static partial class NativeMethods
    {
        /// <summary>Returns the logical DPI for a monitor from a point.</summary>
        [DllImport("shcore.dll")]
        public static extern int GetDpiForMonitor(
            IntPtr hmonitor, int dpiType, out uint dpiX, out uint dpiY);
    }
}
