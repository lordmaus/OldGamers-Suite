using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using static SuperMagnifier.NativeMethods;

namespace SuperMagnifier
{
    /// <summary>
    /// Draws a coloured ring at the cursor position when hovering over a window's
    /// Minimize, Maximize/Restore, or Close button.
    ///
    /// Option 3 implementation: a single full-screen transparent Canvas overlay
    /// that is shown once and never hidden. The ring is positioned by updating
    /// Canvas.Left/Top — pure WPF render operations that produce zero Win32
    /// window messages. No SetWindowPos, no Show/Hide, no WM_SETCURSOR triggered.
    /// This is compatible with cursor-replacement tools such as YoloMouse.
    /// </summary>
    internal sealed class TitleBarButtonHighlighter : IDisposable
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
        private readonly Window  _overlay;
        private readonly Canvas  _canvas;
        private readonly Ellipse _ring;
        private bool             _disposed;

        // ── State ─────────────────────────────────────────────────────────────
        private bool       _enabled  = false;
        private int        _size     = 48;
        private Color      _colMin   = Color.FromRgb(0x00, 0xC8, 0x53);
        private Color      _colMax   = Color.FromRgb(0xFF, 0xAB, 0x00);
        private Color      _colClose = Color.FromRgb(0xFF, 0x3D, 0x00);
        private ButtonKind _lastKind = ButtonKind.None;

        public enum ButtonKind { None, Minimize, MaximizeRestore, Close }

        public TitleBarButtonHighlighter()
        {
            _ring = new Ellipse
            {
                Width           = _size,
                Height          = _size,
                Stroke          = Brushes.Transparent,  // invisible until needed
                StrokeThickness = 5,
                Fill            = Brushes.Transparent,
                IsHitTestVisible = false,
            };

            _canvas = new Canvas
            {
                Background       = Brushes.Transparent,
                IsHitTestVisible = false,
            };
            _canvas.Children.Add(_ring);

            // Position ring off-screen initially
            Canvas.SetLeft(_ring, -9999);
            Canvas.SetTop (_ring, -9999);

            // Full virtual-screen window — always present, always transparent except the ring
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

                // Keep topmost once — no need to repeat every tick
                SetWindowPos(src.Handle, HWND_TOPMOST, 0, 0, 0, 0,
                             SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);

                // Intercept WM_SETCURSOR so the overlay never resets the cursor.
                // Without this, moving over the full-screen canvas triggers
                // WM_SETCURSOR → IDC_ARROW, wiping cursor tools like YoloMouse.
                src.AddHook(OverlayWndProc);
            }
        }

        private IntPtr OverlayWndProc(IntPtr hwnd, int msg, IntPtr wParam,
                                      IntPtr lParam, ref bool handled)
        {
            if (msg == WM_SETCURSOR)
            {
                // Return TRUE without calling SetCursor — preserves whatever
                // cursor the active application has set (e.g. YoloMouse custom).
                handled = true;
                return new IntPtr(1);
            }
            return IntPtr.Zero;
        }

        // ── Public API ────────────────────────────────────────────────────────

        public bool IsEnabled => _enabled;

        public void SetEnabled(bool on)
        {
            _enabled = on;
            if (!on) HideRing();
        }

        public void SetSize(int sizePx)
        {
            _size           = Math.Max(16, Math.Min(200, sizePx));
            _ring.Width     = _size;
            _ring.Height    = _size;
            _lastKind       = ButtonKind.None;
        }

        public void SetColorMinimize(Color c) { _colMin   = c; _lastKind = ButtonKind.None; }
        public void SetColorMaximize(Color c) { _colMax   = c; _lastKind = ButtonKind.None; }
        public void SetColorClose(Color c)    { _colClose = c; _lastKind = ButtonKind.None; }

        /// <summary>
        /// Called every timer tick. physX/physY are raw GetCursorPos physical pixels.
        /// All positioning is done via Canvas.SetLeft/SetTop — pure WPF, zero Win32
        /// messages, no WM_SETCURSOR triggered.
        /// </summary>
        public void Update(int physX, int physY, ButtonKind kind)
        {
            if (!_enabled) return;

            if (kind == ButtonKind.None)
            {
                HideRing();
                return;
            }

            // Update colour when button type changes
            if (kind != _lastKind)
            {
                _lastKind = kind;
                Color col = kind == ButtonKind.Close           ? _colClose
                          : kind == ButtonKind.MaximizeRestore ? _colMax
                          :                                      _colMin;
                _ring.Stroke = new SolidColorBrush(col);
            }

            // Convert physical cursor coords to logical canvas coords using
            // per-monitor DPI of the monitor under the cursor.
            double scale   = GetScaleForPoint(physX, physY);
            double logX    = physX * scale;
            double logY    = physY * scale;

            // The canvas origin is at VirtualScreenLeft/Top (logical).
            // Subtract that so the position is relative to the canvas.
            double originX = SystemParameters.VirtualScreenLeft;
            double originY = SystemParameters.VirtualScreenTop;
            double half    = _size / 2.0;

            Canvas.SetLeft(_ring, logX - originX - half);
            Canvas.SetTop (_ring, logY - originY - half);
        }

        private void HideRing()
        {
            // Move off-canvas — no Win32 calls needed
            Canvas.SetLeft(_ring, -9999);
            Canvas.SetTop (_ring, -9999);
            _ring.Stroke = Brushes.Transparent;
            _lastKind    = ButtonKind.None;
        }

        /// <summary>
        /// Returns the logical/physical DPI scale for the monitor under (physX, physY).
        /// Re-queried on every call for correct multi-monitor DPI handling.
        /// </summary>
        private double GetScaleForPoint(int physX, int physY)
        {
            try
            {
                var pt      = new POINT { X = physX, Y = physY };
                IntPtr hMon = MonitorFromPoint(pt, MONITOR_DEFAULTTONEAREST);
                if (hMon != IntPtr.Zero &&
                    GetDpiForMonitor(hMon, MDT_EFFECTIVE_DPI, out uint dpiX, out _) == 0 &&
                    dpiX > 0)
                    return 96.0 / dpiX;
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
            _overlay?.Close();
        }
    }
}
