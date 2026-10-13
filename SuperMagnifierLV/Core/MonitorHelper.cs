using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows;

namespace SuperMagnifier
{
    /// <summary>
    /// Helpers to identify monitors by device name and convert between
    /// physical screen coordinates and per-monitor-relative positions.
    /// Used to restore the lens to the correct monitor across sessions.
    /// </summary>
    internal static class MonitorHelper
    {
        [DllImport("user32.dll")]
        private static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr lprcClip,
            MonitorEnumProc lpfnEnum, IntPtr dwData);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFOEX lpmi);

        private delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdcMonitor,
            ref RECT lprcMonitor, IntPtr dwData);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private struct MONITORINFOEX
        {
            public int    cbSize;
            public RECT   rcMonitor;
            public RECT   rcWork;
            public uint   dwFlags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string szDevice;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT { public int left, top, right, bottom; }

        public class MonitorInfo
        {
            public string Name     { get; set; }   // e.g. "\\.\DISPLAY1"
            public System.Drawing.Rectangle Bounds     { get; set; }
            public System.Drawing.Rectangle WorkArea   { get; set; }
            public bool   IsPrimary { get; set; }
        }

        public static List<MonitorInfo> GetAllMonitors()
        {
            var list = new List<MonitorInfo>();
            MonitorEnumProc callback = delegate(IntPtr hMon, IntPtr hdc, ref RECT rc, IntPtr data)
            {
                var mi = new MONITORINFOEX { cbSize = Marshal.SizeOf(typeof(MONITORINFOEX)) };
                if (GetMonitorInfo(hMon, ref mi))
                {
                    list.Add(new MonitorInfo
                    {
                        Name     = mi.szDevice,
                        Bounds   = new System.Drawing.Rectangle(
                            mi.rcMonitor.left, mi.rcMonitor.top,
                            mi.rcMonitor.right  - mi.rcMonitor.left,
                            mi.rcMonitor.bottom - mi.rcMonitor.top),
                        WorkArea = new System.Drawing.Rectangle(
                            mi.rcWork.left, mi.rcWork.top,
                            mi.rcWork.right  - mi.rcWork.left,
                            mi.rcWork.bottom - mi.rcWork.top),
                        IsPrimary = (mi.dwFlags & 1) != 0,
                    });
                }
                return true;
            };
            EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, callback, IntPtr.Zero);
            return list;
        }

        /// <summary>
        /// Returns the monitor that contains the given physical-pixel point,
        /// or the primary monitor if none match.
        /// </summary>
        public static MonitorInfo MonitorFromPoint(int physX, int physY)
        {
            MonitorInfo best = null;
            foreach (var m in GetAllMonitors())
            {
                if (m.Bounds.Contains(physX, physY)) return m;
                if (m.IsPrimary) best = m;
            }
            return best ?? new MonitorInfo
            {
                Name     = "PRIMARY",
                Bounds   = new System.Drawing.Rectangle(0, 0,
                    (int)SystemParameters.PrimaryScreenWidth,
                    (int)SystemParameters.PrimaryScreenHeight),
                WorkArea = new System.Drawing.Rectangle(0, 0,
                    (int)SystemParameters.WorkArea.Width,
                    (int)SystemParameters.WorkArea.Height),
                IsPrimary = true,
            };
        }

        /// <summary>
        /// Given the lens Left/Top (WPF logical), find which monitor it's on
        /// and return (monitorName, relativeX%, relativeY%) for persistence.
        /// Percentages are 0-1 relative to the monitor's work area.
        /// </summary>
        public static (string name, double relX, double relY)
            GetRelativePosition(double logLeft, double logTop, float dpiScale)
        {
            int physX = (int)(logLeft  * dpiScale);
            int physY = (int)(logTop   * dpiScale);
            var mon   = MonitorFromPoint(physX, physY);
            double relX = (physX - mon.WorkArea.Left) / (double)Math.Max(1, mon.WorkArea.Width);
            double relY = (physY - mon.WorkArea.Top)  / (double)Math.Max(1, mon.WorkArea.Height);
            return (mon.Name, relX, relY);
        }

        /// <summary>
        /// Given a saved (monitorName, relX, relY), compute the WPF logical Left/Top
        /// to restore the lens to the same relative position.
        /// Falls back to primary monitor if the named monitor is not found.
        /// </summary>
        public static (double left, double top)
            ResolvePosition(string monName, double relX, double relY, float dpiScale,
                            double lensW, double lensH)
        {
            MonitorInfo target = null;
            foreach (var m in GetAllMonitors())
                if (string.Equals(m.Name, monName, StringComparison.OrdinalIgnoreCase))
                { target = m; break; }

            if (target == null) target = MonitorFromPoint(0, 0);

            int physX = target.WorkArea.Left + (int)(relX * target.WorkArea.Width);
            int physY = target.WorkArea.Top  + (int)(relY * target.WorkArea.Height);

            // Clamp so lens stays fully on-screen
            int maxX = target.WorkArea.Right  - (int)(lensW * dpiScale);
            int maxY = target.WorkArea.Bottom - (int)(lensH * dpiScale);
            physX = Math.Max(target.WorkArea.Left, Math.Min(physX, maxX));
            physY = Math.Max(target.WorkArea.Top,  Math.Min(physY, maxY));

            return (physX / dpiScale, physY / dpiScale);
        }
    }
}
