using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using static SuperMagnifier.NativeMethods;

namespace SuperMagnifier
{
    /// <summary>
    /// Moves the lens window with arrow keys via a low-level keyboard hook.
    /// Works even when the lens is in click-through mode (WS_EX_TRANSPARENT).
    ///
    /// IMPORTANT: Arrow keys are only intercepted when the Super Magnifier main
    /// window or lens window is the foreground window. When any other application
    /// has focus the hook passes all keystrokes through untouched, so arrow keys
    /// work normally in text editors, browsers, games, etc.
    ///
    /// Arrow keys       = move 10px
    /// Shift+Arrow      = move 50px (coarse)
    /// Ctrl+Arrow       = move 1px  (fine)
    /// Ctrl+Shift+Arrow = resize lens (WCAG 2.5.7) — via ResizeRequested, foreground-only (SM-03)
    /// </summary>
    internal sealed class LensKeyboardMover : IDisposable
    {
        // ── P/Invoke ──────────────────────────────────────────────────────────
        // SetWindowsHookEx, UnhookWindowsHookEx, CallNextHookEx are specific to
        // the low-level keyboard hook and kept local. All other P/Invoke comes
        // from NativeMethods via the using static above.
        private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")] static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc fn, IntPtr hMod, uint dwThreadId);
        [DllImport("user32.dll")] static extern bool   UnhookWindowsHookEx(IntPtr hhk);
        [DllImport("user32.dll")] static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        private const int WH_KEYBOARD_LL = 13;
        private const int WM_KEYDOWN     = 0x0100;
        private const int WM_SYSKEYDOWN  = 0x0104;
        private const int VK_LEFT  = 0x25, VK_UP   = 0x26;
        private const int VK_RIGHT = 0x27, VK_DOWN = 0x28;
        private const int VK_SHIFT = 0x10, VK_CTRL = 0x11;

        [StructLayout(LayoutKind.Sequential)]
        private struct KBDLLHOOKSTRUCT { public int vkCode, scanCode, flags, time; public IntPtr dwExtraInfo; }

        // ── Fields ────────────────────────────────────────────────────────────
        private readonly Window     _lens;
        private readonly Window     _main;   // main control panel — also an "our" window
        private readonly Dispatcher _dispatcher;
        private IntPtr              _hook;
        private LowLevelKeyboardProc _proc;  // keep delegate alive

        public bool Enabled { get; set; } = true;

        /// <summary>Ctrl+Shift+Arrow while SML / lens is foreground → (dw, dh). SM-03: no longer a global hotkey.</summary>
        public Action<int, int> ResizeRequested { get; set; }

        /// <summary>
        /// Creates the hook. Pass both the lens window and the main control panel
        /// so the mover only activates when one of our own windows is foreground.
        /// </summary>
        public LensKeyboardMover(Window lens, Window main = null)
        {
            _lens       = lens;
            _main       = main;
            _dispatcher = lens.Dispatcher;
            _proc       = HookCallback;
            _hook       = SetWindowsHookEx(WH_KEYBOARD_LL, _proc,
                              GetModuleHandle(null), 0);
        }

        /// <summary>
        /// Returns true only when one of our own windows is the foreground window.
        /// Uses HWND comparison so it works even when our windows have no WPF focus.
        /// </summary>
        private bool OurWindowIsForeground()
        {
            IntPtr fg = GetForegroundWindow();
            if (fg == IntPtr.Zero) return false;

            // Compare against lens HWND
            try
            {
                var lensHelper = new WindowInteropHelper(_lens);
                if (lensHelper.Handle == fg) return true;
            }
            catch { }

            // Compare against main window HWND
            if (_main != null)
            {
                try
                {
                    var mainHelper = new WindowInteropHelper(_main);
                    if (mainHelper.Handle == fg) return true;
                }
                catch { }
            }

            return false;
        }

        private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0 && Enabled &&
                ((int)wParam == WM_KEYDOWN || (int)wParam == WM_SYSKEYDOWN))
            {
                var kb = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
                int vk = kb.vkCode;

                if (vk == VK_LEFT || vk == VK_RIGHT || vk == VK_UP || vk == VK_DOWN)
                {
                    bool shift = (GetKeyState(VK_SHIFT) & 0x8000) != 0;
                    bool ctrl  = (GetKeyState(VK_CTRL)  & 0x8000) != 0;

                    // CRITICAL: only intercept when one of our windows is foreground.
                    // If any other application has focus, pass the keystroke through
                    // unchanged so arrow keys (and Ctrl+Shift+Arrow text selection) work normally.
                    if (!OurWindowIsForeground())
                        return CallNextHookEx(_hook, nCode, wParam, lParam);

                    // Ctrl+Shift+Arrow = lens resize (WCAG 2.5.7 drag alternative), SML-only
                    if (ctrl && shift)
                    {
                        int dw = vk == VK_LEFT ? -20 : vk == VK_RIGHT ? 20 : 0;
                        int dh = vk == VK_UP   ? -20 : vk == VK_DOWN  ? 20 : 0;
                        var resize = ResizeRequested;
                        if (resize == null) return CallNextHookEx(_hook, nCode, wParam, lParam);
                        _dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() => resize(dw, dh)));
                        return new IntPtr(1);
                    }

                    int step = ctrl ? 1 : shift ? 50 : 10;
                    double dx = 0, dy = 0;
                    if (vk == VK_LEFT)  dx = -step;
                    if (vk == VK_RIGHT) dx = +step;
                    if (vk == VK_UP)    dy = -step;
                    if (vk == VK_DOWN)  dy = +step;

                    _dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
                    {
                        _lens.Left += dx;
                        _lens.Top  += dy;
                    }));

                    // Suppress the keystroke so it doesn't also scroll/move in our window
                    return new IntPtr(1);
                }
            }
            return CallNextHookEx(_hook, nCode, wParam, lParam);
        }

        public void Dispose()
        {
            if (_hook != IntPtr.Zero) { UnhookWindowsHookEx(_hook); _hook = IntPtr.Zero; }
        }
    }
}
