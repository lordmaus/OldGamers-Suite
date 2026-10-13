using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

#nullable enable


namespace FileCopy
{
    /// <summary>
    /// Forces dark mode on the Win32 chrome (title bar, system menu) so it
    /// matches the in-app dark surface. No-op on Windows builds &lt; 19041.
    /// LibraryImport = source-generated P/Invoke (no reflection, AOT-safe).
    /// </summary>
    internal static partial class DwmHelpers
    {
        private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

        [LibraryImport("dwmapi.dll")]
        private static partial int DwmSetWindowAttribute(
            nint hwnd, int attr, in int attrValue, int attrSize);

        public static void EnableDarkChrome(Window window, bool dark = true)
        {
            if (window == null) return;
            try
            {
                nint hwnd = new WindowInteropHelper(window).EnsureHandle();
                int  val  = dark ? 1 : 0;
                DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, in val, sizeof(int));
            }
            catch { /* DWM unavailable on older hosts */ }
        }
    }
}
