using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using static SuperMagnifier.NativeMethods;

namespace SuperMagnifier
{
    /// <summary>
    /// A coloured ring drawn around the mouse cursor to help low-vision users
    /// locate it on screen.
    ///
    /// Option 3 implementation: a single full-screen transparent Canvas overlay
    /// shown once and never hidden. Ring position updated via Canvas.SetLeft/SetTop —
    /// pure WPF render operations, zero Win32 window messages, compatible with
    /// cursor-replacement tools such as YoloMouse.
    /// </summary>
    internal sealed class CursorHighlighter : IDisposable
    {
        // ── Per-monitor DPI (Shcore — Windows 8.1+) ──────────────────────────
        [DllImport("shcore.dll")]
        private static extern int GetDpiForMonitor(IntPtr hMonitor, int dpiType,
                                                   out uint dpiX, out uint dpiY);

        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromPoint(POINT pt, uint dwFlags);

        private const uint MONITOR_DEFAULTTONEAREST = 2;
        private const int  MDT_EFFECTIVE_DPI        = 0;
        private const int  WM_SETCURSOR             = 0x0020;

        // ── Overlay ───────────────────────────────────────────────────────────
        private readonly Window         _overlay;
        private readonly Canvas         _canvas;
        private readonly Ellipse        _ring;
        private readonly DispatcherTimer _timer;
        private bool                    _visible  = false;
        private bool                    _disposed;

        private const int SIZE  = 40;   // ring diameter in logical px at 96 DPI
        private const int THICK = 4;    // stroke thickness

        // Per-monitor DPI cache (Bug fix: GetScaleForPoint used to call
        // MonitorFromPoint + GetDpiForMonitor uncached on every 16ms tick —
        // ~2 uncached Win32 calls/tick for the lifetime of an enabled highlighter.
        // Cleared on RefreshBounds() (display/DPI topology change).
        private readonly Dictionary<IntPtr, double> _dpiCache = new();

        public CursorHighlighter()
        {
            _ring = new Ellipse
            {
                Width            = SIZE,
                Height           = SIZE,
                Stroke           = Brushes.Transparent,  // invisible until enabled
                StrokeThickness  = THICK,
                Fill             = Brushes.Transparent,
                IsHitTestVisible = false,
            };

            _canvas = new Canvas
            {
                Background       = Brushes.Transparent,
                IsHitTestVisible = false,
            };
            _canvas.Children.Add(_ring);

            Canvas.SetLeft(_ring, -9999);
            Canvas.SetTop (_ring, -9999);

            _overlay = new Window
            {
                WindowStyle        = WindowStyle.None,
                AllowsTransparency = true,
                Background         = Brushes.Transparent,
                ShowInTaskbar      = false,
                Topmost            = true,
                IsHitTestVisible   = false,
                Left               = SystemParameters.VirtualScreenLeft,
                Top                = SystemParameters.VirtualScreenTop,
                Width              = SystemParameters.VirtualScreenWidth,
                Height             = SystemParameters.VirtualScreenHeight,
                Content            = _canvas,
            };

            // Show once — never hidden again
            _overlay.Show();

            // WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW | WS_EX_TRANSPARENT
            var src = PresentationSource.FromVisual(_overlay) as HwndSource;
            if (src != null)
            {
                int ex = GetWindowLong(src.Handle, GWL_EXSTYLE);
                SetWindowLong(src.Handle, GWL_EXSTYLE,
                    ex | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW | 0x00000020);

                SetWindowPos(src.Handle, HWND_TOPMOST, 0, 0, 0, 0,
                             SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);

                // Intercept WM_SETCURSOR so the overlay never resets the cursor.
                src.AddHook(OverlayWndProc);
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
            if (on)
            {
                _timer.Start();
            }
            else
            {
                _timer.Stop();
                // Hide ring by moving it off-canvas — no Win32 calls
                Canvas.SetLeft(_ring, -9999);
                Canvas.SetTop (_ring, -9999);
                _ring.Stroke = Brushes.Transparent;
            }
        }

        /// <summary>
        /// Call when display topology/DPI changes (Microsoft.Win32.SystemEvents
        /// .DisplaySettingsChanged). The overlay window is sized once at
        /// construction from SystemParameters.VirtualScreen* and never resized,
        /// so adding/removing a monitor left the ring unable to render on newly
        /// added screen area. Also drops the per-monitor DPI cache since monitor
        /// handles and effective DPI can change across a topology change.
        /// </summary>
        public void RefreshBounds()
        {
            _overlay.Left   = SystemParameters.VirtualScreenLeft;
            _overlay.Top    = SystemParameters.VirtualScreenTop;
            _overlay.Width  = SystemParameters.VirtualScreenWidth;
            _overlay.Height = SystemParameters.VirtualScreenHeight;
            _dpiCache.Clear();
        }

        public void SetColor(Color c)
        {
            // Store colour — applied in Tick when ring becomes visible
            _ring.Stroke = _visible ? new SolidColorBrush(c) : Brushes.Transparent;
            _ringColor = c;
        }

        private Color _ringColor = Color.FromRgb(255, 80, 0);

        private void Tick(object sender, EventArgs e)
        {
            if (!_visible) return;
            GetCursorPos(out POINT pt);

            double scale   = GetScaleForPoint(pt.X, pt.Y);
            double logX    = pt.X * scale;
            double logY    = pt.Y * scale;
            double originX = SystemParameters.VirtualScreenLeft;
            double originY = SystemParameters.VirtualScreenTop;
            double half    = SIZE / 2.0;

            Canvas.SetLeft(_ring, logX - originX - half);
            Canvas.SetTop (_ring, logY - originY - half);

            // Make ring visible (stroke was transparent when disabled)
            if (_ring.Stroke == Brushes.Transparent)
                _ring.Stroke = new SolidColorBrush(_ringColor);
        }

        private IntPtr OverlayWndProc(IntPtr hwnd, int msg, IntPtr wParam,
                                      IntPtr lParam, ref bool handled)
        {
            if (msg == WM_SETCURSOR)
            {
                handled = true;
                return new IntPtr(1);
            }
            return IntPtr.Zero;
        }

        private double GetScaleForPoint(int physX, int physY)
        {
            try
            {
                var pt      = new POINT { X = physX, Y = physY };
                IntPtr hMon = MonitorFromPoint(pt, MONITOR_DEFAULTTONEAREST);
                if (hMon != IntPtr.Zero)
                {
                    if (_dpiCache.TryGetValue(hMon, out double cachedScale))
                        return cachedScale;

                    if (GetDpiForMonitor(hMon, MDT_EFFECTIVE_DPI, out uint dpiX, out _) == 0 &&
                        dpiX > 0)
                    {
                        double scale = 96.0 / dpiX;
                        _dpiCache[hMon] = scale;
                        return scale;
                    }
                }
            }
            catch { }

            var hwndSrc = PresentationSource.FromVisual(_overlay) as HwndSource;
            if (hwndSrc?.CompositionTarget != null)
                return hwndSrc.CompositionTarget.TransformFromDevice.M11;

            using (var g = System.Drawing.Graphics.FromHwnd(IntPtr.Zero))
                return 96.0 / g.DpiX;
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
