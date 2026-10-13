using System;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using static SuperMagnifier.NativeMethods;

namespace SuperMagnifier
{
    /// <summary>
    /// A full-width semi-transparent horizontal band that follows the mouse cursor's
    /// Y position, helping low-vision users track which line of text they are reading.
    /// </summary>
    internal sealed class ReadingRuler : IDisposable
    {
        private Window         _overlay;
        private DispatcherTimer _timer;
        private bool           _visible;
        private bool           _disposed;
        private double         _dpiScaleX;
        private double         _dpiScaleY;

        private const double HEIGHT = 28.0;   // logical px height of the band

        public ReadingRuler()
        {
            _overlay = new Window
            {
                WindowStyle        = WindowStyle.None,
                AllowsTransparency = true,
                Background         = new SolidColorBrush(Color.FromArgb(80, 255, 220, 0)), // amber tint
                ShowInTaskbar      = false,
                Topmost            = true,
                IsHitTestVisible   = false,
                Height             = HEIGHT,
                Width              = SystemParameters.VirtualScreenWidth,
                Left               = SystemParameters.VirtualScreenLeft,
                Top                = 0,
            };

            // Show once and keep permanently visible — never hidden again.
            // Visibility controlled by Top position (off-screen when inactive).
            _overlay.Top = -9999;
            _overlay.Show();

            // WS_EX_NOACTIVATE + WS_EX_TRANSPARENT (clicks pass through)
            var src = PresentationSource.FromVisual(_overlay) as HwndSource;
            if (src != null)
            {
                IntPtr hwnd = src.Handle;
                int ex = GetWindowLong(hwnd, GWL_EXSTYLE);
                SetWindowLong(hwnd, GWL_EXSTYLE,
                              ex | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW | 0x00000020 /* WS_EX_TRANSPARENT */);
                // Intercept WM_SETCURSOR so ruler never resets cursor tools like YoloMouse
                src.AddHook(RulerWndProc);
            }

            _timer = new DispatcherTimer(DispatcherPriority.Render)
            {
                Interval = TimeSpan.FromMilliseconds(16)
            };
            _timer.Tick += Tick;
        }

        public bool IsEnabled => _visible;

        public void SetEnabled(bool on)
        {
            _visible = on;
            _dpiScaleX = _dpiScaleY = 0; // re-read on next tick
            if (on)
            {
                _timer.Start();
            }
            else
            {
                _timer.Stop();
                _overlay.Top = -9999; // move off-screen instead of hiding
            }
        }

        public void SetColor(Color c)
        {
            _overlay.Background = new SolidColorBrush(c);
        }

        public void SetHeight(double h)
        {
            _overlay.Height = Math.Max(4, h);
        }

        private void EnsureDpiScale()
        {
            if (_dpiScaleX > 0) return;
            var src = PresentationSource.FromVisual(_overlay) as HwndSource;
            if (src?.CompositionTarget != null)
            {
                _dpiScaleX = src.CompositionTarget.TransformFromDevice.M11;
                _dpiScaleY = src.CompositionTarget.TransformFromDevice.M22;
            }
            else
            {
                using (var g = System.Drawing.Graphics.FromHwnd(IntPtr.Zero))
                {
                    _dpiScaleX = 96.0 / g.DpiX;
                    _dpiScaleY = 96.0 / g.DpiY;
                }
            }
        }

        private void Tick(object sender, EventArgs e)
        {
            if (!_visible) return;
            GetCursorPos(out POINT pt);
            EnsureDpiScale();

            // Centre the band on cursor Y
            double logY = pt.Y * _dpiScaleY;
            _overlay.Top  = logY - _overlay.Height / 2.0;
            _overlay.Width = SystemParameters.VirtualScreenWidth;
            _overlay.Left  = SystemParameters.VirtualScreenLeft;

            // Keep topmost
            var src = PresentationSource.FromVisual(_overlay) as HwndSource;
            if (src != null)
                SetWindowPos(src.Handle, HWND_TOPMOST, 0, 0, 0, 0,
                             SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
        }

        private const int WM_SETCURSOR = 0x0020;
        private static IntPtr RulerWndProc(IntPtr hwnd, int msg, IntPtr wParam,
                                           IntPtr lParam, ref bool handled)
        {
            if (msg == WM_SETCURSOR) { handled = true; return new IntPtr(1); }
            return IntPtr.Zero;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _timer?.Stop();
            _overlay?.Close();
        }
    }
}
