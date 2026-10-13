using System;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using static SuperMagnifier.NativeMethods;

namespace SuperMagnifier
{
    internal sealed class MagHwndHost : HwndHost
    {
        private IntPtr _magHwnd    = IntPtr.Zero;
        private IntPtr _blankCursor = IntPtr.Zero;
        private int    _w, _h;

        public IntPtr MagHwnd => _magHwnd;

        /// <summary>When false, WM_SETCURSOR is not intercepted so cursor

        public MagHwndHost(int w, int h) { _w = Math.Max(4,w); _h = Math.Max(4,h); }

        protected override HandleRef BuildWindowCore(HandleRef hwndParent)
        {
            _magHwnd = CreateWindowEx(0, "Magnifier", "MagnifierWindow",
                WS_CHILD | WS_VISIBLE | WS_CLIPCHILDREN | WS_CLIPSIBLINGS,
                0, 0, _w, _h, hwndParent.Handle, IntPtr.Zero,
                GetModuleHandle(null), IntPtr.Zero);

            if (_magHwnd == IntPtr.Zero)
                throw new InvalidOperationException(
                    "CreateWindowEx for WC_MAGNIFIER failed. MagInitialize() must be called first.");

            _blankCursor = CreateBlankCursor();
            return new HandleRef(this, _magHwnd);
        }

        protected override void DestroyWindowCore(HandleRef hwnd)
        {
            if (_blankCursor != IntPtr.Zero) { DestroyCursor(_blankCursor); _blankCursor = IntPtr.Zero; }
            if (_magHwnd != IntPtr.Zero)     { DestroyWindow(_magHwnd);     _magHwnd = IntPtr.Zero;     }
        }

        // Intercept WM_SETCURSOR on the mag child window and replace it with a
        // blank cursor so the system cursor is not drawn inside the lens.
        private const int WM_SETCURSOR = 0x0020;

        protected override IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam,
                                          IntPtr lParam, ref bool handled)
        {
            if (msg == WM_SETCURSOR && _blankCursor != IntPtr.Zero)
            {
                SetCursor(_blankCursor);
                handled = true;
                return new IntPtr(1);
            }
            return base.WndProc(hwnd, msg, wParam, lParam, ref handled);
        }

        public void ResizeMag(int w, int h)
        {
            _w = Math.Max(4, w); _h = Math.Max(4, h);
            if (_magHwnd != IntPtr.Zero)
            {
                SetWindowPos(_magHwnd, IntPtr.Zero, 0, 0, _w, _h,
                             SWP_NOMOVE | SWP_NOZORDER | SWP_NOACTIVATE);
                InvalidateRect(_magHwnd, IntPtr.Zero, true);
            }
        }
    }
}
