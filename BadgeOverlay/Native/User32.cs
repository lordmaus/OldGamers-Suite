using System;
using System.Runtime.InteropServices;
using System.Text;

namespace BadgeOverlay
{
    /// <summary>
    /// user32.dll P/Invoke surface — windowing, cursor, hotkeys,
    /// WinEvent hooks, low-level mouse hook, hit-testing, system
    /// parameters, monitor info. See Native/Common.cs for the
    /// partial-class layout overview.
    /// </summary>
    public static partial class NativeMethods
    {
        // ── Window enumeration / lookup ───────────────────────────────────

        [DllImport("user32.dll")]
        public static extern bool GetCursorPos(out POINT lpPoint);

        [DllImport("user32.dll")]
        public static extern IntPtr WindowFromPoint(POINT point);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        public static extern IntPtr FindWindow(string lpClassName, string lpWindowName);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        public static extern IntPtr FindWindowEx(
            IntPtr hwndParent, IntPtr hwndChildAfter,
            string lpszClass, string lpszWindow);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern int GetClassName(
            IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern int GetWindowText(
            IntPtr hWnd, StringBuilder lpString, int nMaxCount);

        [DllImport("user32.dll")]
        public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll")]
        private static extern IntPtr GetAncestor(IntPtr hwnd, uint gaFlags);

        // On win-x64 GetWindowLongPtr / SetWindowLongPtr return LONG_PTR (64-bit).
        // Declaring as IntPtr marshals correctly on both architectures; returning
        // long here was wrong on 64-bit (truncates the upper 32 bits).
        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtr")]
        public static extern IntPtr GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr")]
        public static extern IntPtr SetWindowLong(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

        [DllImport("user32.dll")]
        public static extern bool SetWindowPos(
            IntPtr hWnd, IntPtr hWndInsertAfter,
            int X, int Y, int cx, int cy, uint uFlags);

        [DllImport("user32.dll")]
        public static extern bool BringWindowToTop(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern IntPtr GetWindow(IntPtr hWnd, uint uCmd);

        // ── Cursor replacement ────────────────────────────────────────────
        // Standard cursor IDs for SetSystemCursor

        public const uint OCR_NORMAL      = 32512;
        public const uint OCR_IBEAM       = 32513;
        public const uint OCR_WAIT        = 32514;
        public const uint OCR_CROSS       = 32515;
        public const uint OCR_UP          = 32516;
        public const uint OCR_SIZENWSE    = 32642;
        public const uint OCR_SIZENESW    = 32643;
        public const uint OCR_SIZEWE      = 32644;
        public const uint OCR_SIZENS      = 32645;
        public const uint OCR_SIZEALL     = 32646;
        public const uint OCR_NO          = 32648;
        public const uint OCR_HAND        = 32649;
        public const uint OCR_APPSTARTING = 32650;

        /// <summary>
        /// Replaces a system cursor with hcur.
        /// hcur must be a copy (from CopyCursor/LoadCursor) — the system takes ownership.
        /// </summary>
        [DllImport("user32.dll")]
        public static extern bool SetSystemCursor(IntPtr hcur, uint id);

        /// <summary>Creates a 1×1 fully-transparent cursor used to hide each slot.</summary>
        [DllImport("user32.dll")]
        public static extern IntPtr CreateCursor(
            IntPtr hInst, int xHotSpot, int yHotSpot,
            int nWidth, int nHeight,
            byte[] pvANDPlane, byte[] pvXORPlane);

        /// <summary>Duplicates a cursor handle (SetSystemCursor consumes the handle).</summary>
        [DllImport("user32.dll")]
        public static extern IntPtr CopyCursor(IntPtr hcur);

        /// <summary>Loads the default system cursor for the given OCR_* id.</summary>
        [DllImport("user32.dll")]
        public static extern IntPtr LoadCursor(IntPtr hInstance, int lpCursorName);

        [DllImport("user32.dll")]
        public static extern bool DestroyCursor(IntPtr hCursor);

        /// <summary>Forces a system-wide cursor refresh (after restoring).</summary>
        [DllImport("user32.dll")]
        public static extern bool SystemParametersInfo(
            uint uiAction, uint uiParam, IntPtr pvParam, uint fWinIni);

        public const uint SPI_SETCURSORS        = 0x0057;
        public const uint SPIF_UPDATEINIFILE    = 0x0001;
        public const uint SPIF_SENDCHANGE       = 0x0002;

        /// <summary>Reads a bool system parameter (e.g. SPI_GETCLIENTAREAANIMATION).</summary>
        [DllImport("user32.dll", EntryPoint = "SystemParametersInfoW", SetLastError = true)]
        public static extern bool SystemParametersInfoBool(
            uint uiAction, uint uiParam, ref bool pvParam, uint fWinIni);

        /// <summary>SPI_GETCLIENTAREAANIMATION — false when user disables animation effects.</summary>
        public const uint SPI_GETCLIENTAREAANIMATION = 0x1042;

        [DllImport("user32.dll")]
        public static extern int ShowCursor(bool bShow);

        // ── Hotkeys ───────────────────────────────────────────────────────

        public const int WM_HOTKEY = 0x0312;

        [DllImport("user32.dll")]
        public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll")]
        public static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        // Modifier flags for RegisterHotKey
        public const uint MOD_ALT      = 0x0001;
        public const uint MOD_CONTROL  = 0x0002;
        public const uint MOD_SHIFT    = 0x0004;
        public const uint MOD_WIN      = 0x0008;
        public const uint MOD_NOREPEAT = 0x4000;

        // ── WinEvent hook ─────────────────────────────────────────────────
        // Used for focus-change cursor re-apply and shell-event handling.

        public delegate void WinEventDelegate(
            IntPtr hWinEventHook, uint eventType, IntPtr hwnd,
            int idObject, int idChild, uint dwEventThread, uint dwmsEventTime);

        [DllImport("user32.dll")]
        public static extern IntPtr SetWinEventHook(
            uint eventMin, uint eventMax,
            IntPtr hmodWinEventProc,
            WinEventDelegate lpfnWinEventProc,
            uint idProcess, uint idThread, uint dwFlags);

        [DllImport("user32.dll")]
        public static extern bool UnhookWinEvent(IntPtr hWinEventHook);

        // WinEvent constants
        public const uint EVENT_SYSTEM_FOREGROUND   = 0x0003;
        public const uint EVENT_SYSTEM_MINIMIZEEND  = 0x0017;  // restore from minimized
        public const uint EVENT_OBJECT_FOCUS        = 0x8005;  // focus within a window
        public const uint EVENT_OBJECT_SHOW         = 0x8002;  // object shown (catches ShowCursor)
        public const uint EVENT_OBJECT_CREATE       = 0x8000;  // new object created

        // WinEvent flags
        public const uint WINEVENT_OUTOFCONTEXT   = 0x0000;
        public const uint WINEVENT_SKIPOWNPROCESS = 0x0002;

        // ── Low-level mouse hook ──────────────────────────────────────────
        // WH_MOUSE_LL fires on every mouse move — much narrower than
        // OBJECT_SHOW. Cursor slots are re-blanked only when the mouse
        // actually moves, not on every UI element creation across the
        // system. Replaces the EVENT_OBJECT_SHOW path that was firing
        // hundreds of times per second on busy desktops.
        public const int WH_MOUSE_LL  = 14;
        public const int WM_MOUSEMOVE = 0x0200;

        public delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        public static extern IntPtr SetWindowsHookEx(
            int idHook, LowLevelMouseProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll")]
        public static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll")]
        public static extern IntPtr CallNextHookEx(
            IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        // ── Resize drag-handle (WM_NCLBUTTONDOWN) ──────────────────────────
        // Lets a purely decorative in-content resize glyph drive the same
        // native resize the window border already supports, instead of
        // re-implementing drag math in managed code.
        public const int WM_NCLBUTTONDOWN = 0xA1;
        public const int HT_BOTTOMRIGHT   = 17;

        [DllImport("user32.dll")]
        public static extern bool ReleaseCapture();

        [DllImport("user32.dll", EntryPoint = "SendMessageW")]
        public static extern IntPtr SendMessage(IntPtr hWnd, int Msg, IntPtr wParam, IntPtr lParam);

        // ── DPI ───────────────────────────────────────────────────────────

        [DllImport("user32.dll")]
        public static extern uint GetDpiForWindow(IntPtr hwnd);

        // ── Active window / process detection (for app profiles) ──────────

        [DllImport("user32.dll")]
        public static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        // ── Monitor info ──────────────────────────────────────────────────

        [DllImport("user32.dll")]
        public static extern IntPtr MonitorFromPoint(POINT pt, uint dwFlags);
        public const uint MONITOR_DEFAULTTONEAREST = 0x00000002;

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);
    }
}
