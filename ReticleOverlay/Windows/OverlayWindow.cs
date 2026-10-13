using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace ReticleOverlay
{
    /// <summary>
    /// Transparent, click-through, always-on-top overlay.
    /// All drawing is delegated to ReticleRenderer.
    /// P/Invoke lives in Native/User32.cs + Native/Shcore.cs (NativeMethods).
    /// </summary>
    public class OverlayWindow : Window
    {
        private readonly Canvas _canvas;
        private DispatcherTimer _topmostTimer;

        public OverlayWindow()
        {
            WindowStyle        = WindowStyle.None;
            AllowsTransparency = true;
            Background         = Brushes.Transparent;
            ShowInTaskbar      = false;
            Topmost            = true;
            ResizeMode         = ResizeMode.NoResize;
            IsHitTestVisible   = false;

            _canvas = new Canvas { Background = Brushes.Transparent, IsHitTestVisible = false };
            Content = _canvas;
            Loaded += OnLoaded;
            Closed += OnClosed;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            var hwnd  = new WindowInteropHelper(this).Handle;
            int style = NativeMethods.GetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE);
            style |= NativeMethods.WS_EX_LAYERED | NativeMethods.WS_EX_TRANSPARENT
                  |  NativeMethods.WS_EX_TOOLWINDOW | NativeMethods.WS_EX_NOACTIVATE;
            NativeMethods.SetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE, style);
            NativeMethods.SetWindowPos(hwnd, NativeMethods.HWND_TOPMOST, 0, 0, 0, 0,
                NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE);

            // High #2 (2026-09-07 review): exclusive-fullscreen games can steal the
            // topmost z-order from every other window for the rest of the session with
            // no OS event to recover from. Re-assert HWND_TOPMOST at ~1 Hz so the reticle
            // reclaims its place after the common "lost topmost on focus juggling" case.
            _topmostTimer = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromSeconds(1)
            };
            _topmostTimer.Tick += (s, e) => NativeMethods.SetWindowPos(hwnd, NativeMethods.HWND_TOPMOST, 0, 0, 0, 0,
                NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE);
            _topmostTimer.Start();
        }

        private void OnClosed(object sender, EventArgs e)
        {
            _topmostTimer?.Stop();
            _topmostTimer = null;
        }

        // ── OBS / capture hide ────────────────────────────────────────────────
        public void SetCaptureHidden(bool hide)
        {
            try
            {
                var hwnd = new WindowInteropHelper(this).Handle;
                NativeMethods.SetWindowDisplayAffinity(hwnd,
                    hide ? NativeMethods.WDA_EXCLUDEFROMCAPTURE : NativeMethods.WDA_NONE);
            }
            catch { /* WDA_EXCLUDEFROMCAPTURE requires Windows 10 2004+ */ }
        }

        // ── Positioning ───────────────────────────────────────────────────────
        public void PositionOnMonitor(System.Windows.Forms.Screen screen, AppPrefs p)
        {
            double scale = TryGetMonitorDpi(screen) ?? 1.0;
            if (scale <= 0) scale = 1.0;
            Left   = screen.Bounds.Left   / scale;
            Top    = screen.Bounds.Top    / scale;
            Width  = screen.Bounds.Width  / scale;
            Height = screen.Bounds.Height / scale;
            ReticleRenderer.Draw(_canvas, Width, Height, p);
        }

        public void Redraw(AppPrefs p) => ReticleRenderer.Draw(_canvas, Width, Height, p);

        // ── DPI helper ────────────────────────────────────────────────────────
        private static double? TryGetMonitorDpi(System.Windows.Forms.Screen screen)
        {
            try
            {
                var pt = new System.Drawing.Point(screen.Bounds.Left + 1, screen.Bounds.Top + 1);
                IntPtr hMon = NativeMethods.MonitorFromPoint(pt, 2);
                if (NativeMethods.GetDpiForMonitor(hMon, 0, out uint dpiX, out _) == 0) return dpiX / 96.0;
            }
            catch { }
            return null;
        }
    }
}
