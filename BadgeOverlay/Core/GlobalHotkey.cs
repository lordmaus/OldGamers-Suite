using System;
using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace BadgeOverlay
{
    /// <summary>
    /// Registers a system-wide hotkey and raises Pressed when it fires.
    /// Works even when the app is minimised or another window has focus.
    /// </summary>
    internal sealed class GlobalHotkey : IDisposable
    {
        [DllImport("user32.dll")] static extern bool RegisterHotKey  (IntPtr hWnd, int id, uint fsModifiers, uint vk);
        [DllImport("user32.dll")] static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        private const int WM_HOTKEY = 0x0312;

        // Modifier flags
        public const uint MOD_ALT      = 0x0001;
        public const uint MOD_CONTROL  = 0x0002;
        public const uint MOD_SHIFT    = 0x0004;
        public const uint MOD_WIN      = 0x0008;
        public const uint MOD_NOREPEAT = 0x4000;

        private static int _nextId = 0xC000; // above WM_APP range, below 0xFFFF
        private static int NextId() => System.Threading.Interlocked.Increment(ref _nextId);

        private readonly HwndSource _source;
        private readonly int        _id;
        private bool                _disposed;

        /// <summary>True if the hotkey was successfully registered with Windows.</summary>
        public bool IsRegistered { get; }

        public event EventHandler Pressed;

        public GlobalHotkey(HwndSource source, uint modifiers, uint virtualKey)
        {
            _source      = source;
            _id          = NextId();
            _source.AddHook(WndProc);
            IsRegistered = RegisterHotKey(_source.Handle, _id,
                                          modifiers | MOD_NOREPEAT, virtualKey);
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam,
                               IntPtr lParam, ref bool handled)
        {
            if (msg == WM_HOTKEY && wParam.ToInt32() == _id)
            {
                Pressed?.Invoke(this, EventArgs.Empty);
                handled = true;
            }
            return IntPtr.Zero;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            UnregisterHotKey(_source.Handle, _id);
            _source.RemoveHook(WndProc);
        }
    }
}
