using System;
using System.Runtime.InteropServices;
using System.Text;

namespace BadgeOverlay
{
    /// <summary>
    /// kernel32.dll P/Invoke surface — process introspection, DLL
    /// search path, power state. See Native/Common.cs for the
    /// partial-class layout overview.
    /// </summary>
    public static partial class NativeMethods
    {
        // ── Process introspection ─────────────────────────────────────────
        // Used by ButtonDetector to resolve hwnd → process name + path
        // without allocating a managed Process. Faster than
        // Process.GetProcessById on the hot detection path, and works
        // against elevated targets that would throw "Access is denied"
        // when MainModule is read.

        public const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern IntPtr OpenProcess(
            uint dwDesiredAccess,
            [MarshalAs(UnmanagedType.Bool)] bool bInheritHandle,
            uint dwProcessId);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool QueryFullProcessImageNameW(
            IntPtr hProcess,
            uint dwFlags,
            [Out] StringBuilder lpExeName,
            ref uint lpdwSize);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool CloseHandle(IntPtr hObject);

        // ── DLL search path ───────────────────────────────────────────────
        // Used during single-file publish workarounds to point the
        // unmanaged loader at the install dir even when AppContext
        // misreports BaseDirectory.

        /// <summary>
        /// Adds <paramref name="lpPathName"/> to the per-process DLL
        /// search path. Used by <c>OcrEngine</c> so the Win32 loader
        /// finds Tesseract's leptonica/tesseract DLLs in the EXE folder
        /// when single-file extraction places them somewhere unexpected.
        /// Pass an empty string or <c>null</c> to clear the override.
        /// </summary>
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetDllDirectory(string lpPathName);

        // ── Power state ───────────────────────────────────────────────────
        // Used by UIADetector.IsOnBattery to slow the poll rate when
        // the user is on battery. Single-call syscall; result is
        // cached by the caller for ~5 s so per-tick overhead is nil.

        /// <summary>Win32 SYSTEM_POWER_STATUS. Only ACLineStatus is read by callers.</summary>
        [StructLayout(LayoutKind.Sequential)]
        public struct SYSTEM_POWER_STATUS
        {
            /// <summary>0 = offline (on battery), 1 = online (AC), 255 = unknown.</summary>
            public byte ACLineStatus;
            /// <summary>Bitfield: 0 = >66%, 1 = >33%, 2 = &lt;33%, 4 = critical, 8 = charging, 128 = no battery.</summary>
            public byte BatteryFlag;
            /// <summary>0–100 % charge, or 255 if unknown / no battery.</summary>
            public byte BatteryLifePercent;
            public byte SystemStatusFlag;
            public int  BatteryLifeTime;
            public int  BatteryFullLifeTime;
        }

        [DllImport("kernel32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetSystemPowerStatus(out SYSTEM_POWER_STATUS lpSystemPowerStatus);
    }
}
