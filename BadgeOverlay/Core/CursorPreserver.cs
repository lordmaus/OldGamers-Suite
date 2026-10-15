using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace BadgeOverlay
{
    /// <summary>
    /// Prevents WPF windows from overriding the current OS cursor.
    ///
    /// Two techniques applied per window:
    ///
    /// 1. Clear the window-class cursor via SetClassLongPtr(GCLP_HCURSOR, 0).
    ///    When the class cursor is NULL, Windows does NOT set a cursor when
    ///    the mouse enters the window — the previously-visible cursor stays.
    ///    This is exactly what YoloMouse and custom cursor tools rely on.
    ///
    /// 2. Hook WM_SETCURSOR and return FALSE (0) without marking handled.
    ///    This prevents WPF's internal cursor management from calling
    ///    SetCursor() via its Mouse.UpdateCursor pipeline. WPF's default
    ///    WndProc only calls SetCursor if the class cursor is non-NULL or
    ///    FrameworkElement.Cursor is set, and we've neutralised both.
    /// </summary>
    internal static class CursorPreserver
    {
        private const int WM_SETCURSOR = 0x0020;
        private const int GCLP_HCURSOR = -12;
        private const int HTCLIENT     = 0x01;

        [DllImport("user32.dll", EntryPoint = "SetClassLongPtrW")]
        private static extern IntPtr SetClassLongPtr64(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

        [DllImport("user32.dll", EntryPoint = "SetClassLongW")]
        private static extern int SetClassLong32(IntPtr hWnd, int nIndex, int dwNewLong);

        private static void ClearClassCursor(IntPtr hwnd)
        {
            try
            {
                if (IntPtr.Size == 8)
                    SetClassLongPtr64(hwnd, GCLP_HCURSOR, IntPtr.Zero);
                else
                    SetClassLong32(hwnd, GCLP_HCURSOR, 0);
            }
            catch (Exception ex) { AppLog.Error("ClearClassCursor", ex); }
        }

        public static void Attach(Window w)
        {
            if (w == null) return;
            if (w.IsLoaded) Hook(w);
            else w.SourceInitialized += (s, e) => Hook(w);

            // Also neutralise the FrameworkElement.Cursor default — if anything
            // below sets it, the class-cursor trick still wins, but this avoids
            // WPF queueing a SetCursor at the framework layer.
            try { w.ClearValue(FrameworkElement.CursorProperty); } catch { }
            try { w.ForceCursor = false; } catch { }
        }

        private static void Hook(Window w)
        {
            var hwnd = new WindowInteropHelper(w).Handle;
            if (hwnd == IntPtr.Zero) return;

            ClearClassCursor(hwnd);

            var src = HwndSource.FromHwnd(hwnd);
            src?.AddHook(WndProc);
        }

        private static IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam,
                                      IntPtr lParam, ref bool handled)
        {
            if (msg == WM_SETCURSOR)
            {
                // LOWORD(lParam) is the hit-test code. Only suppress WPF's
                // cursor setting in the client area — for non-client hit
                // codes (title bar, resize borders, close button) we let
                // Windows pick the correct cursor (arrow / resize arrows /
                // hand etc.). This preserves YoloMouse over content while
                // restoring expected resize-cursor feedback on window edges.
                int hitTest = (short)(lParam.ToInt64() & 0xFFFF);
                if (hitTest == HTCLIENT)
                {
                    // Return TRUE + handled = cursor is "set" — WPF skips
                    // calling SetCursor. Class cursor is NULL, so Windows
                    // leaves the current cursor (YoloMouse) untouched.
                    handled = true;
                    return new IntPtr(1);
                }
            }
            return IntPtr.Zero;
        }
    }
}

