using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace BadgeOverlay
{
    /// <summary>
    /// Win32 P/Invoke surface, partial-class-split by API surface.
    /// Files in Native/:
    ///   • Common.cs   — shared structs (POINT/RECT/MONITORINFO),
    ///                   window-style constants, SetWindowPos flags,
    ///                   and the StringBuilder pool used by GetClass /
    ///                   GetText. Everything in this file is consumed
    ///                   by more than one of the other partials.
    ///   • User32.cs   — windowing, cursor, hotkeys, WinEvent hooks,
    ///                   low-level mouse hook, hit-testing,
    ///                   SystemParametersInfo, GetMonitorInfo.
    ///   • Kernel32.cs — process introspection, library search path,
    ///                   power state.
    ///   • Shcore.cs   — per-monitor DPI.
    ///
    /// All four files declare the same <c>partial class NativeMethods</c>
    /// in the BadgeOverlay namespace, so existing call sites
    /// (NativeMethods.GetClass, NativeMethods.SWP_NOSIZE, etc.) keep
    /// compiling without any rename. The split is purely organisational.
    /// </summary>
    public static partial class NativeMethods
    {
        // ── Window styles (GetWindowLong nIndex values) ───────────────────
        public const int  GWL_EXSTYLE       = -20;
        public const int  GWL_STYLE         = -16;

        /// <summary>Edit-control style bit set when the field masks input
        /// (WinUser.h <c>ES_PASSWORD</c>). Used by the overlay to
        /// auto-suppress the badge over password fields for security.</summary>
        public const long ES_PASSWORD       = 0x0020;

        // ── Extended window style bits ────────────────────────────────────
        public const long WS_EX_LAYERED     = 0x00080000L;
        public const long WS_EX_TRANSPARENT = 0x00000020L;
        public const long WS_EX_TOPMOST     = 0x00000008L;
        public const long WS_EX_TOOLWINDOW  = 0x00000080L;  // excluded from Shell Z-band management

        // ── SetWindowPos flags + Z-order sentinels ────────────────────────
        public const uint SWP_NOSIZE          = 0x0001;
        public const uint SWP_NOMOVE          = 0x0002;
        public const uint SWP_NOACTIVATE      = 0x0010;
        public const uint SWP_NOSENDCHANGING  = 0x0400;  // bypass WM_WINDOWPOSCHANGING
        public const uint SWP_NOOWNERZORDER   = 0x0200;  // don't change owner's Z-order

        public static readonly IntPtr HWND_TOPMOST    = new IntPtr(-1);
        public static readonly IntPtr HWND_NOTOPMOST  = new IntPtr(-2);

        // ── GetWindow relationships ───────────────────────────────────────
        public const uint GW_OWNER = 4;

        // ── GetAncestor flags (used by GetRootWindow helper) ──────────────
        private const uint GA_ROOT = 2;

        // ── Structs ───────────────────────────────────────────────────────

        [StructLayout(LayoutKind.Sequential)]
        public struct POINT
        {
            public int X;
            public int Y;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
            public int Width  { get { return Right  - Left; } }
            public int Height { get { return Bottom - Top;  } }
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct MONITORINFO
        {
            public int  cbSize;
            public RECT rcMonitor;    // Full monitor bounds (incl. taskbar).
            public RECT rcWork;       // Work area (excl. taskbar).
            public uint dwFlags;
        }

        // ── StringBuilder pool ────────────────────────────────────────────
        // GetClass and GetText are called every tick on every hwnd the
        // cursor passes. Each StringBuilder alloc was garbage-collected
        // immediately after. At 30 Hz over long sessions this adds up;
        // pooling two single-slot buffers (one per size class, since the
        // two methods need different capacities) eliminates the waste
        // without needing a full object-pool dependency.
        //
        // Thread-safety: protected by Interlocked.Exchange so a concurrent
        // caller on a background thread can't double-free a buffer.
        // Detection is single-threaded today, but this keeps UIA-worker
        // paths safe if they ever start calling these helpers.
        private static StringBuilder _classSb;
        private static StringBuilder _textSb;

        private static StringBuilder RentClassSb()
        {
            var sb = Interlocked.Exchange(ref _classSb, null);
            if (sb != null) { sb.Clear(); return sb; }
            return new StringBuilder(256);
        }

        private static void ReturnClassSb(StringBuilder sb)
        {
            if (sb == null) return;
            // Don't pool pathologically-grown buffers — if a caller
            // expanded it far beyond typical size, let it GC.
            if (sb.Capacity > 1024) return;
            Interlocked.CompareExchange(ref _classSb, sb, null);
        }

        private static StringBuilder RentTextSb()
        {
            var sb = Interlocked.Exchange(ref _textSb, null);
            if (sb != null) { sb.Clear(); return sb; }
            return new StringBuilder(512);
        }

        private static void ReturnTextSb(StringBuilder sb)
        {
            if (sb == null) return;
            if (sb.Capacity > 2048) return;
            Interlocked.CompareExchange(ref _textSb, sb, null);
        }

        // ── String-returning convenience wrappers ─────────────────────────
        // Exposed publicly because callers all over the code base reach
        // into NativeMethods directly for these. The actual GetClassName /
        // GetWindowText P/Invokes live in User32.cs (private).

        /// <summary>Returns the Win32 class name of <paramref name="hwnd"/>.</summary>
        public static string GetClass(IntPtr hwnd)
        {
            StringBuilder sb = RentClassSb();
            try
            {
                GetClassName(hwnd, sb, sb.Capacity);
                return sb.ToString();
            }
            finally { ReturnClassSb(sb); }
        }

        /// <summary>Returns the window/control caption text of <paramref name="hwnd"/>.</summary>
        public static string GetText(IntPtr hwnd)
        {
            StringBuilder sb = RentTextSb();
            try
            {
                GetWindowText(hwnd, sb, sb.Capacity);
                return sb.ToString();
            }
            finally { ReturnTextSb(sb); }
        }

        /// <summary>Returns the root (desktop-child) ancestor of <paramref name="hwnd"/>.</summary>
        public static IntPtr GetRootWindow(IntPtr hwnd)
        {
            return GetAncestor(hwnd, GA_ROOT);
        }
    }
}
