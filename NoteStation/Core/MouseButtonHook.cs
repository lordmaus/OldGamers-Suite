using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace NoteStation
{
    /// <summary>
    /// Low-level mouse hook that exposes XButton1, XButton2, and scroll-wheel events.
    /// Allows assigning clipboard-cycle actions to mouse side buttons or scroll.
    ///
    /// Usage:
    ///   hook = new MouseButtonHook();
    ///   hook.XButton1Pressed += ...;
    ///   hook.XButton2Pressed += ...;
    ///   hook.ScrollUp        += ...;
    ///   hook.ScrollDown      += ...;
    ///   hook.Install();
    ///   // later:
    ///   hook.Dispose();
    /// </summary>
    internal sealed class MouseButtonHook : IDisposable
    {
        private IntPtr _hookHandle = IntPtr.Zero;
        private NativeMethods.LowLevelMouseProc _proc;   // keep alive to prevent GC
        private bool _disposed;

        public event EventHandler XButton1Pressed;
        public event EventHandler XButton2Pressed;
        public event EventHandler ScrollUp;
        public event EventHandler ScrollDown;

        // ── Feature switches — set before Install() or at runtime ─────────────
        public bool EnableXButton1  { get; set; } = true;
        public bool EnableXButton2  { get; set; } = false;
        public bool EnableScroll    { get; set; } = false;

        // Only fires scroll events when Ctrl+Alt is held (to avoid conflicting
        // with normal scrolling). Set to false to fire on all scroll events.
        public bool ScrollRequiresModifier { get; set; } = true;

        public void Install()
        {
            if (_hookHandle != IntPtr.Zero) return;   // already installed

            _proc = HookCallback;
            IntPtr hMod = NativeMethods.GetModuleHandle(
                Process.GetCurrentProcess().MainModule?.ModuleName ?? "");
            _hookHandle = NativeMethods.SetWindowsHookEx(
                NativeMethods.WH_MOUSE_LL, _proc, hMod, 0);
        }

        public void Uninstall()
        {
            if (_hookHandle == IntPtr.Zero) return;
            NativeMethods.UnhookWindowsHookEx(_hookHandle);
            _hookHandle = IntPtr.Zero;
        }

        private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            // L-4: this runs on every mouse event system-wide, on the thread that
            // called SetWindowsHookEx. An unhandled exception here (a bad
            // subscriber, a marshalling edge case) propagates out of the hook
            // chain and has taken down the whole app on a single click. Never let
            // anything from here escape — always fall through to CallNextHookEx.
            if (nCode >= 0)
            {
                try
                {
                    var msg = (int)wParam;
                    var ms  = Marshal.PtrToStructure<NativeMethods.MSLLHOOKSTRUCT>(lParam);

                    if (msg == NativeMethods.WM_XBUTTONUP)
                    {
                        int btn = (int)((ms.mouseData >> 16) & 0xFFFF);
                        if (btn == NativeMethods.XBUTTON1 && EnableXButton1)
                            XButton1Pressed?.Invoke(this, EventArgs.Empty);
                        else if (btn == NativeMethods.XBUTTON2 && EnableXButton2)
                            XButton2Pressed?.Invoke(this, EventArgs.Empty);
                    }
                    else if (msg == NativeMethods.WM_MOUSEWHEEL && EnableScroll)
                    {
                        bool modOk = !ScrollRequiresModifier ||
                                     ((System.Windows.Input.Keyboard.Modifiers &
                                       (System.Windows.Input.ModifierKeys.Control |
                                        System.Windows.Input.ModifierKeys.Alt)) ==
                                      (System.Windows.Input.ModifierKeys.Control |
                                       System.Windows.Input.ModifierKeys.Alt));
                        if (modOk)
                        {
                            short delta = (short)((ms.mouseData >> 16) & 0xFFFF);
                            if (delta > 0) ScrollUp?.Invoke(this, EventArgs.Empty);
                            else if (delta < 0) ScrollDown?.Invoke(this, EventArgs.Empty);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine("[NoteStation] MouseButtonHook callback failed: " + ex.Message);
                    try { AppLog.Error("MouseButtonHook.HookCallback", ex); } catch { }
                }
            }

            return NativeMethods.CallNextHookEx(_hookHandle, nCode, wParam, lParam);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Uninstall();
        }
    }
}
