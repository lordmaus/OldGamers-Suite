using System;
using System.Windows;
using System.Windows.Interop;

namespace NoteStation
{
    /// <summary>
    /// Listens for WM_CLIPBOARDUPDATE (Win32 clipboard change notification) and
    /// raises ClipboardChanged whenever the clipboard contents change.
    /// Must be attached to a visible HWND — attach in MainWindow.Loaded.
    /// </summary>
    internal sealed class ClipboardMonitor : IDisposable
    {
        private HwndSource _source;
        private bool       _disposed;

        public event EventHandler ClipboardChanged;

        public void Attach(Window window)
        {
            _source = HwndSource.FromHwnd(new WindowInteropHelper(window).Handle);
            if (_source == null) return;
            _source.AddHook(WndProc);
            NativeMethods.AddClipboardFormatListener(_source.Handle);
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == NativeMethods.WM_CLIPBOARDUPDATE)
            {
                ClipboardChanged?.Invoke(this, EventArgs.Empty);
                handled = false; // don't swallow — let WPF see it too
            }
            return IntPtr.Zero;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            if (_source != null)
            {
                NativeMethods.RemoveClipboardFormatListener(_source.Handle);
                _source.RemoveHook(WndProc);
            }
        }
    }
}
