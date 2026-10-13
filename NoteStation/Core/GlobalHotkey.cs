using System;
using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace NoteStation
{
    /// <summary>
    /// Registers a system-wide hotkey and raises Pressed when it fires.
    /// Works even when the app is minimised or another window has focus.
    /// </summary>
    internal sealed class GlobalHotkey : IDisposable
    {
        private const int WM_HOTKEY = 0x0312;

        private readonly HwndSource _source;
        private readonly int        _id;
        private bool                _disposed;

        /// <summary>True if the hotkey was successfully registered with Windows.</summary>
        public bool IsRegistered { get; }

        public event EventHandler Pressed;

        public GlobalHotkey(HwndSource source, int id, uint modifiers, uint virtualKey)
        {
            _source      = source;
            _id          = id;
            _source.AddHook(WndProc);
            IsRegistered = NativeMethods.RegisterHotKey(_source.Handle, _id,
                                                         modifiers | NativeMethods.MOD_NOREPEAT,
                                                         virtualKey);
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
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
            NativeMethods.UnregisterHotKey(_source.Handle, _id);
            _source.RemoveHook(WndProc);
        }
    }
}
