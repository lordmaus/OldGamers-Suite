using System;
using System.Runtime.InteropServices;
using System.Windows.Threading;

namespace SuperMagnifier
{
    /// <summary>
    /// Tracks the system text caret position across ALL applications using
    /// WinEvent hooks + GetGUIThreadInfo (cross-process safe).
    /// GetCaretPos only works in-process; GetGUIThreadInfo works everywhere.
    /// Position is in physical screen pixels.
    /// </summary>
    internal sealed class CaretTracker : IDisposable
    {
        // ── P/Invoke ─────────────────────────────────────────────────────────
        private delegate void WinEventDelegate(
            IntPtr hWinEventHook, uint eventType, IntPtr hwnd,
            int idObject, int idChild, uint dwEventThread, uint dwmsEventTime);

        [DllImport("user32.dll")]
        private static extern IntPtr SetWinEventHook(
            uint eventMin, uint eventMax, IntPtr hmodWinEventProc,
            WinEventDelegate lpfnWinEventProc, uint idProcess, uint idThread,
            uint dwFlags);

        [DllImport("user32.dll")]
        private static extern bool UnhookWinEvent(IntPtr hWinEventHook);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("user32.dll")]
        private static extern bool GetGUIThreadInfo(uint idThread, ref GUITHREADINFO lpgui);

        [DllImport("user32.dll")]
        private static extern bool ClientToScreen(IntPtr hWnd, ref NativeMethods.POINT lpPoint);

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT { public int left, top, right, bottom; }

        [StructLayout(LayoutKind.Sequential)]
        private struct GUITHREADINFO
        {
            public int    cbSize;
            public uint   flags;
            public IntPtr hwndActive;
            public IntPtr hwndFocus;
            public IntPtr hwndCapture;
            public IntPtr hwndMenuOwner;
            public IntPtr hwndMoveSize;
            public IntPtr hwndCaret;
            public RECT   rcCaret;     // caret rect in CLIENT coordinates of hwndCaret
        }

        // WinEvent constants
        private const uint EVENT_OBJECT_LOCATIONCHANGE  = 0x800B;
        private const uint EVENT_OBJECT_SHOW            = 0x8002;
        private const uint EVENT_OBJECT_FOCUS           = 0x8005;
        private const uint EVENT_SYSTEM_FOREGROUND      = 0x0003;
        private const int  OBJID_CARET                  = unchecked((int)0xFFFFFFF8);
        private const uint WINEVENT_OUTOFCONTEXT         = 0x0000;

        private IntPtr           _hook;
        private IntPtr           _hookFocus;
        private WinEventDelegate _delegate;
        private bool             _disposed;
        private bool             _enabled;
        private readonly Dispatcher _dispatcher;

        public int  CaretX       { get; private set; }
        public int  CaretY       { get; private set; }
        public bool HasPosition  { get; private set; }

        public event EventHandler CaretMoved;

        public CaretTracker(Dispatcher dispatcher)
        {
            _dispatcher = dispatcher;
            _delegate   = OnWinEvent;
        }

        public bool IsEnabled => _enabled;

        public void SetEnabled(bool on)
        {
            if (on == _enabled) return;
            _enabled = on;
            if (on)
            {
                // Two narrow hooks instead of one wide range, to minimise callback volume.
                // Hook 1: LOCATIONCHANGE — fires when caret moves (most reliable signal)
                // Hook 2: FOCUS+FOREGROUND — catches apps that don't fire OBJID_CARET events
                _hook = SetWinEventHook(
                    EVENT_OBJECT_LOCATIONCHANGE, EVENT_OBJECT_LOCATIONCHANGE,
                    IntPtr.Zero, _delegate, 0, 0,
                    WINEVENT_OUTOFCONTEXT);
                _hookFocus = SetWinEventHook(
                    EVENT_OBJECT_FOCUS, EVENT_SYSTEM_FOREGROUND,
                    IntPtr.Zero, _delegate, 0, 0,
                    WINEVENT_OUTOFCONTEXT);
            }
            else
            {
                if (_hook      != IntPtr.Zero) { UnhookWinEvent(_hook);      _hook      = IntPtr.Zero; }
                if (_hookFocus != IntPtr.Zero) { UnhookWinEvent(_hookFocus); _hookFocus = IntPtr.Zero; }
                HasPosition = false;
            }
        }

        private void OnWinEvent(IntPtr hWinEventHook, uint eventType,
                                IntPtr hwnd, int idObject, int idChild,
                                uint dwEventThread, uint dwmsEventTime)
        {
            // Both hooks are narrow — all callbacks are relevant.
            // For LOCATIONCHANGE events, only act on caret object changes.
            // For focus/foreground events, always poll the current caret position.
            if (eventType == EVENT_OBJECT_LOCATIONCHANGE && idObject != OBJID_CARET) return;

            IntPtr fg = GetForegroundWindow();
            if (fg == IntPtr.Zero) return;

            uint tid = GetWindowThreadProcessId(fg, out _);
            if (tid == 0) return;

            var gui = new GUITHREADINFO { cbSize = Marshal.SizeOf(typeof(GUITHREADINFO)) };
            if (!GetGUIThreadInfo(tid, ref gui)) return;
            if (gui.hwndCaret == IntPtr.Zero) return;

            // rcCaret is in client coords of hwndCaret — convert to screen
            var pt = new NativeMethods.POINT
            {
                X = gui.rcCaret.left,
                Y = gui.rcCaret.top
            };
            ClientToScreen(gui.hwndCaret, ref pt);

            int x = pt.X, y = pt.Y;
            _dispatcher.BeginInvoke(new Action(() =>
            {
                CaretX      = x;
                CaretY      = y;
                HasPosition = true;
                CaretMoved?.Invoke(this, EventArgs.Empty);
            }));
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            SetEnabled(false);
        }
    }
}
