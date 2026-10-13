using System.Runtime.InteropServices;

namespace ReticleOverlay
{
    /// <summary>
    /// user32.dll P/Invoke surface — source-generated via [LibraryImport].
    /// Partial class — see Native/Shcore.cs for the shcore.dll half.
    /// AllowUnsafeBlocks must be true in the csproj (required by [LibraryImport]).
    /// </summary>
    internal static partial class NativeMethods
    {
        // ── Window extended styles (GetWindowLong / SetWindowLong nIndex) ─────
        public const int GWL_EXSTYLE       = -20;
        public const int WS_EX_LAYERED     = 0x00080000;
        public const int WS_EX_TRANSPARENT = 0x00000020;
        public const int WS_EX_TOOLWINDOW  = 0x00000080;
        public const int WS_EX_NOACTIVATE  = 0x08000000;

        // ── SetWindowPos flags / Z-order sentinels ────────────────────────────
        public static readonly nint HWND_TOPMOST = -1;
        public const uint SWP_NOSIZE     = 0x0001;
        public const uint SWP_NOMOVE     = 0x0002;
        public const uint SWP_NOACTIVATE = 0x0010;

        // ── Window Display Affinity (capture / OBS hide) ──────────────────────
        public const uint WDA_NONE               = 0x00;
        public const uint WDA_EXCLUDEFROMCAPTURE = 0x11;

        // ── Resize drag-handle (WM_NCLBUTTONDOWN) ─────────────────────────────
        public const int WM_NCLBUTTONDOWN = 0xA1;
        public const int HT_BOTTOMRIGHT   = 17;

        // ── Global hotkeys ────────────────────────────────────────────────────
        public const int  WM_HOTKEY    = 0x0312;
        public const uint MOD_NONE     = 0x0000;
        public const uint MOD_ALT      = 0x0001;
        public const uint MOD_CTRL     = 0x0002;
        public const uint MOD_SHIFT    = 0x0004;
        public const uint MOD_WIN      = 0x0008;
        public const uint MOD_NOREPEAT = 0x4000;

        // ── P/Invoke — source-generated (no runtime IL emit) ─────────────────
        [LibraryImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static partial bool ReleaseCapture();

        // SendMessage / GetWindowLong / SetWindowLong — must use W-suffixed export names;
        // the bare names (GetWindowLong etc.) are C macros and are NOT real DLL exports.
        [LibraryImport("user32.dll", EntryPoint = "SendMessageW")]
        public static partial nint SendMessage(nint hWnd, int Msg, nint wParam, nint lParam);

        [LibraryImport("user32.dll", EntryPoint = "GetWindowLongW")]
        public static partial int GetWindowLong(nint hWnd, int nIndex);

        [LibraryImport("user32.dll", EntryPoint = "SetWindowLongW")]
        public static partial int SetWindowLong(nint hWnd, int nIndex, int dwNewLong);

        [LibraryImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static partial bool SetWindowPos(nint hWnd, nint hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

        [LibraryImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static partial bool SetWindowDisplayAffinity(nint hWnd, uint dwAffinity);

        [LibraryImport("user32.dll")]
        public static partial nint MonitorFromPoint(System.Drawing.Point pt, uint dwFlags);

        [LibraryImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static partial bool RegisterHotKey(nint hWnd, int id, uint fsModifiers, uint vk);

        [LibraryImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static partial bool UnregisterHotKey(nint hWnd, int id);

        // VkKeyScanW — explicit W variant so char marshals as UTF-16 (WCHAR)
        [LibraryImport("user32.dll", EntryPoint = "VkKeyScanW")]
        public static partial short VkKeyScan(char ch);

        // ── Foreground-window process lookup (Feature #1 / Bug #3, 2026-09-07 review) ──
        [LibraryImport("user32.dll")]
        public static partial nint GetForegroundWindow();

        [LibraryImport("user32.dll", EntryPoint = "GetWindowThreadProcessId")]
        public static partial uint GetWindowThreadProcessId(nint hWnd, out uint lpdwProcessId);
    }
}
