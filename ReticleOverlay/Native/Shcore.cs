using System.Runtime.InteropServices;

namespace ReticleOverlay
{
    /// <summary>
    /// shcore.dll P/Invoke surface — per-monitor DPI.
    /// Partial class — see Native/User32.cs for the user32.dll half.
    /// </summary>
    internal static partial class NativeMethods
    {
        /// <summary>
        /// Retrieves the DPI for a monitor.
        /// dpiType 0 = MDT_EFFECTIVE_DPI (scaled by accessibility settings).
        /// Requires Windows 8.1+. Returns S_OK (0) on success.
        /// </summary>
        [LibraryImport("shcore.dll")]
        public static partial int GetDpiForMonitor(nint hMon, int dpiType, out uint dpiX, out uint dpiY);
    }
}
