using System;
using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace NoteStation
{
    internal static class NativeMethods
    {
        // ── Window hit-test constants (resize grip) ───────────────────────────
        public const int WM_NCHITTEST   = 0x0084;
        public const int HTLEFT         = 10;
        public const int HTRIGHT        = 11;
        public const int HTTOP          = 12;
        public const int HTTOPLEFT      = 13;
        public const int HTTOPRIGHT     = 14;
        public const int HTBOTTOM       = 15;
        public const int HTBOTTOMLEFT   = 16;
        public const int HTBOTTOMRIGHT  = 17;

        // ── Clipboard change notification ─────────────────────────────────────
        public const int WM_CLIPBOARDUPDATE = 0x031D;

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool AddClipboardFormatListener(IntPtr hwnd);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool RemoveClipboardFormatListener(IntPtr hwnd);

        // ── Hotkey ────────────────────────────────────────────────────────────
        public const int WM_HOTKEY = 0x0312;

        [DllImport("user32.dll")] public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
        [DllImport("user32.dll")] public static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        // Modifier flags
        public const uint MOD_ALT      = 0x0001;
        public const uint MOD_CONTROL  = 0x0002;
        public const uint MOD_SHIFT    = 0x0004;
        public const uint MOD_WIN      = 0x0008;
        public const uint MOD_NOREPEAT = 0x4000;

        // ── Mouse button hook (WH_MOUSE_LL) ──────────────────────────────────
        public const int  WH_MOUSE_LL   = 14;
        public const int  WM_XBUTTONUP  = 0x020C;
        public const int  WM_XBUTTONDOWN = 0x020B;
        public const int  WM_MOUSEWHEEL = 0x020A;
        public const int  XBUTTON1      = 0x0001;
        public const int  XBUTTON2      = 0x0002;
        public const uint GET_XBUTTON_WPARAM = 0xFFFF0000;

        public delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern IntPtr SetWindowsHookEx(int idHook, LowLevelMouseProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern IntPtr GetModuleHandle(string lpModuleName);

        [StructLayout(LayoutKind.Sequential)]
        public struct MSLLHOOKSTRUCT
        {
            public POINT pt;
            public uint   mouseData;
            public uint   flags;
            public uint   time;
            public IntPtr dwExtraInfo;
        }

        // ── Clipboard access ──────────────────────────────────────────────────
        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool OpenClipboard(IntPtr hWndNewOwner);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool CloseClipboard();

        [DllImport("user32.dll")]
        public static extern bool EmptyClipboard();

        [DllImport("user32.dll")]
        public static extern IntPtr GetClipboardData(uint uFormat);

        [DllImport("user32.dll")]
        public static extern IntPtr SetClipboardData(uint uFormat, IntPtr hMem);

        // ── Foreground window (source-app capture) ────────────────────────────
        [DllImport("user32.dll")]
        public static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern int GetWindowText(IntPtr hWnd, System.Text.StringBuilder text, int count);

        public static string GetForegroundWindowTitle()
        {
            var sb = new System.Text.StringBuilder(256);
            var hwnd = GetForegroundWindow();
            if (hwnd == IntPtr.Zero) return "";
            GetWindowText(hwnd, sb, sb.Capacity);
            return sb.ToString();
        }

        // ── Startup registry ──────────────────────────────────────────────────
        [DllImport("kernel32.dll")]
        public static extern uint GetCurrentThreadId();

        [StructLayout(LayoutKind.Sequential)]
        public struct POINT { public int X; public int Y; }
    }
}
