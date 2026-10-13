using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Shapes;
using System.Windows.Media;

namespace SuperMagnifier
{
    /// <summary>
    /// Transparent, click-through, always-on-top overlay window that draws
    /// a reticle over the lens view area.
    ///
    /// Kept as a top-level WPF Window — NOT a Win32 child (SetParent breaks
    /// AllowsTransparency layered windows).  Positioned via PositionOver()
    /// every timer tick to track the lens view rect in screen coordinates.
    /// </summary>
    public partial class ReticleOverlayWindow : Window
    {
        // ── Win32 ─────────────────────────────────────────────────────────────
        static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        const int  GWL_EXSTYLE       = -20;
        const int  WS_EX_TRANSPARENT = 0x00000020;
        const int  WS_EX_NOACTIVATE  = 0x08000000;
        const int  WS_EX_TOOLWINDOW  = 0x00000080;
        const uint SWP_NOACTIVATE    = 0x0010;
        const uint SWP_SHOWWINDOW    = 0x0040;
        const uint SWP_NOOWNERZORDER = 0x0200;

        [DllImport("user32.dll")] static extern int  GetWindowLong(IntPtr h, int i);
        [DllImport("user32.dll")] static extern int  SetWindowLong(IntPtr h, int i, int v);
        [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr h, IntPtr after,
            int x, int y, int cx, int cy, uint flags);

        // ── Size tables ───────────────────────────────────────────────────────
        static readonly (double Arm, double Stroke)[] Sizes =
        {
            (20, 1.5), (30, 2.0), (44, 2.5), (60, 3.0), (80, 3.5),
        };
        static readonly double[] DotRadii = { 5, 8, 11, 15, 20 };

        // ── State ─────────────────────────────────────────────────────────────
        bool   _enabled      = false;
        string _style        = "Cross";
        Color  _color        = Colors.Red;
        int    _size         = 3;
        double _cursorNormX  = 0.5;
        double _cursorNormY  = 0.5;

        public bool ShowCursorDot { get; set; } = true;

        // ── Init ──────────────────────────────────────────────────────────────
        public ReticleOverlayWindow()
        {
            InitializeComponent();
            SourceInitialized += OnSourceInitialized;
        }

        void OnSourceInitialized(object sender, EventArgs e)
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            // Click-through, no taskbar, no activation
            int ex = GetWindowLong(hwnd, GWL_EXSTYLE);
            SetWindowLong(hwnd, GWL_EXSTYLE,
                ex | WS_EX_TRANSPARENT | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW);
        }

        // ── Public API ────────────────────────────────────────────────────────

        /// <summary>
        /// Position the overlay to cover the lens view area exactly and redraw.
        /// x/y/w/h are screen coordinates in device pixels.
        /// normX/normY are cursor position within the view (0..1).
        /// Call every timer tick while the magnifier is active.
        /// </summary>
        public void PositionOver(int screenX, int screenY, int w, int h,
                                 double normX = 0.5, double normY = 0.5)
        {
            if (w < 1 || h < 1) return;

            _cursorNormX = normX;
            _cursorNormY = normY;

            var myHwnd = new WindowInteropHelper(this).Handle;
            SetWindowPos(myHwnd, HWND_TOPMOST,
                screenX, screenY, w, h,
                SWP_NOACTIVATE | SWP_SHOWWINDOW | SWP_NOOWNERZORDER);

            // Convert device px → WPF logical units for canvas drawing
            double dpiScale = 1.0;
            var src = PresentationSource.FromVisual(this);
            if (src != null)
                dpiScale = src.CompositionTarget.TransformFromDevice.M11;

            double lw = w * dpiScale;
            double lh = h * dpiScale;

            if (Visibility != Visibility.Visible)
                Visibility = Visibility.Visible;

            DrawAt(normX, normY, lw, lh);
        }

        /// <summary>Update reticle settings and redraw if canvas is sized.</summary>
        public void Configure(bool enabled, string style, string colorHex, int size)
        {
            _enabled = enabled;
            _style   = style ?? "Cross";
            _size    = Math.Max(1, Math.Min(5, size));
            _color = ColourPicker.ParseColorOrDefault(colorHex, Colors.Red);

            if (DrawCanvas != null && DrawCanvas.Width > 1)
                DrawAt(_cursorNormX, _cursorNormY, DrawCanvas.Width, DrawCanvas.Height);
        }

        // ── Drawing ───────────────────────────────────────────────────────────
        void DrawAt(double normX, double normY, double w, double h)
        {
            if (DrawCanvas == null || w < 1 || h < 1) return;

            DrawCanvas.Children.Clear();
            DrawCanvas.Width  = w;
            DrawCanvas.Height = h;

            double cx = normX * w;
            double cy = normY * h;

            if (_enabled)
            {
                var (arm, stroke) = Sizes[_size - 1];
                var brush = new SolidColorBrush(_color);
                brush.Freeze();

                switch (_style)
                {
                    case "X":
                        AddLine(cx-arm, cy-arm, cx+arm, cy+arm, brush, stroke);
                        AddLine(cx+arm, cy-arm, cx-arm, cy+arm, brush, stroke);
                        break;
                    case "Circle":
                        var el = new Ellipse
                        {
                            Width = arm*2, Height = arm*2,
                            Stroke = brush, StrokeThickness = stroke,
                            IsHitTestVisible = false,
                        };
                        Canvas.SetLeft(el, cx - arm);
                        Canvas.SetTop (el, cy - arm);
                        DrawCanvas.Children.Add(el);
                        break;
                    default: // Cross
                        AddLine(cx-arm, cy, cx+arm, cy, brush, stroke);
                        AddLine(cx, cy-arm, cx, cy+arm, brush, stroke);
                        break;
                }
            }

            if (ShowCursorDot)
            {
                double r = DotRadii[_size - 1];
                var outer = new Ellipse
                {
                    Width = (r+2)*2, Height = (r+2)*2,
                    Fill = new SolidColorBrush(Color.FromArgb(200, 0, 0, 0)),
                    IsHitTestVisible = false,
                };
                Canvas.SetLeft(outer, cx - r - 2);
                Canvas.SetTop (outer, cy - r - 2);
                DrawCanvas.Children.Add(outer);

                var inner = new Ellipse
                {
                    Width = r*2, Height = r*2,
                    Fill = new SolidColorBrush(Color.FromArgb(230, 255, 255, 255)),
                    IsHitTestVisible = false,
                };
                Canvas.SetLeft(inner, cx - r);
                Canvas.SetTop (inner, cy - r);
                DrawCanvas.Children.Add(inner);
            }
        }

        void AddLine(double x1, double y1, double x2, double y2, Brush brush, double thickness)
        {
            DrawCanvas.Children.Add(new Line
            {
                X1=x1, Y1=y1, X2=x2, Y2=y2,
                Stroke=brush, StrokeThickness=thickness,
                IsHitTestVisible=false,
            });
        }
    }
}
