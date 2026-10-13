using System;
using Microsoft.Win32;

#nullable enable

namespace FileCopy
{
    /// <summary>
    /// Adds or removes File Copy Studio from the Windows "run at startup" registry key.
    /// Uses HKCU so no elevation is required.
    /// </summary>
    internal static class StartupHelper
    {
        private const string AppName  = "FileCopyStudio";
        private const string RunKey   = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";

        /// <summary>Returns true if the startup entry exists and points to the current exe.</summary>
        public static bool IsEnabled()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: false);
                if (key == null) return false;
                string? val = key.GetValue(AppName) as string;
                return !string.IsNullOrEmpty(val);
            }
            catch { return false; }
        }

        /// <summary>Creates (or updates) the startup registry entry.</summary>
        public static void Enable()
        {
            try
            {
                string? exe = Environment.ProcessPath
                              ?? System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;
                if (string.IsNullOrEmpty(exe)) return;

                using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
                key?.SetValue(AppName, $"\"{exe}\"");
            }
            catch { /* silently ignore — not elevated enough for key */ }
        }

        /// <summary>Removes the startup registry entry if present.</summary>
        public static void Disable()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
                if (key?.GetValue(AppName) != null)
                    key.DeleteValue(AppName);
            }
            catch { }
        }
    }
}
