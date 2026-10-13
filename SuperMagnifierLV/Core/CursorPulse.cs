using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;
using static SuperMagnifier.NativeMethods;

namespace SuperMagnifier
{
    /// <summary>
    /// On demand, fires a brief animated expanding ring from the cursor position
    /// so low-vision users can instantly locate the pointer on screen.
    /// Triggered by Ctrl+P hotkey or the pulse button in the Lens Controls panel.
    /// </summary>
    internal sealed class CursorPulse : IDisposable
    {

        private const int  RINGS      = 3;
        private const int  BASE_SIZE  = 32;
        private const int  MAX_SIZE   = 120;
        private const int  RING_DELAY = 80;   // ms between ring launches
        private const int  DURATION   = 600;  // ms per ring expand

        private Color _color = Color.FromArgb(200, 100, 180, 255); // default blue-white

        public void SetColor(Color c) { _color = c; }

        /// <summary>Fire the pulse animation at the current cursor position.</summary>
        public void Fire()
        {
            GetCursorPos(out POINT pt);

            // Convert physical pixels → WPF logical units using system DPI.
            // Reading from Graphics.FromHwnd(IntPtr.Zero) is cheap and avoids
            // creating/closing a temporary Window on every key press (fix #6).
            double dpiScaleX = 1.0, dpiScaleY = 1.0;
            try
            {
                using (var g = System.Drawing.Graphics.FromHwnd(IntPtr.Zero))
                {
                    dpiScaleX = 96.0 / g.DpiX;
                    dpiScaleY = 96.0 / g.DpiY;
                }
            }
            catch { }

            double logX = pt.X * dpiScaleX;
            double logY = pt.Y * dpiScaleY;

            for (int i = 0; i < RINGS; i++)
            {
                int delay = i * RING_DELAY;
                int idx = i; // capture
                var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(delay) };
                t.Tick += (s, e) => { t.Stop(); FireRing(logX, logY, idx); };
                t.Start();
            }
        }

        private void FireRing(double logX, double logY, int ringIndex)
        {
            // Alpha fades based on ring order: first ring most opaque
            byte alpha = (byte)(200 - ringIndex * 50);
            var ringColor = Color.FromArgb(alpha, _color.R, _color.G, _color.B);

            double canvasSize = MAX_SIZE * 2;
            double centre     = MAX_SIZE;   // centre of the canvas in logical px

            var ellipse = new Ellipse
            {
                Width           = BASE_SIZE,
                Height          = BASE_SIZE,
                Stroke          = new SolidColorBrush(ringColor),
                StrokeThickness = 3,
                Fill            = Brushes.Transparent,
            };

            var canvas = new Canvas { Width = canvasSize, Height = canvasSize };
            // Start the ellipse centred in the canvas
            Canvas.SetLeft(ellipse, centre - BASE_SIZE / 2.0);
            Canvas.SetTop (ellipse, centre - BASE_SIZE / 2.0);
            canvas.Children.Add(ellipse);

            // Overlay window is already centred on the cursor
            var overlay = new Window
            {
                Width              = canvasSize,
                Height             = canvasSize,
                Left               = logX - centre,
                Top                = logY - centre,
                WindowStyle        = WindowStyle.None,
                AllowsTransparency = true,
                Background         = Brushes.Transparent,
                ShowInTaskbar      = false,
                Topmost            = true,
                IsHitTestVisible   = false,
                Content            = canvas,
            };

            // Make click-through and non-activating
            overlay.SourceInitialized += (s, e) =>
            {
                var src = PresentationSource.FromVisual(overlay) as HwndSource;
                if (src != null)
                {
                    int ex = GetWindowLong(src.Handle, GWL_EXSTYLE);
                    SetWindowLong(src.Handle, GWL_EXSTYLE,
                        ex | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW | 0x00000020);
                }
            };

            overlay.Show();

            double targetSize = canvasSize - 4;

            // Animate ellipse expanding outward while staying centred
            // We animate Left/Top via Canvas attached properties using DoubleAnimation
            var expandW = new DoubleAnimation(BASE_SIZE, targetSize,
                new Duration(TimeSpan.FromMilliseconds(DURATION)))
            { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };

            var expandH = new DoubleAnimation(BASE_SIZE, targetSize,
                new Duration(TimeSpan.FromMilliseconds(DURATION)))
            { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };

            // Keep centred: left = centre - width/2, so animate from centre-BASE/2 to 2
            var moveL = new DoubleAnimation(centre - BASE_SIZE / 2.0, 2,
                new Duration(TimeSpan.FromMilliseconds(DURATION)))
            { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };

            var moveT = new DoubleAnimation(centre - BASE_SIZE / 2.0, 2,
                new Duration(TimeSpan.FromMilliseconds(DURATION)))
            { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };

            ellipse.BeginAnimation(FrameworkElement.WidthProperty,  expandW);
            ellipse.BeginAnimation(FrameworkElement.HeightProperty, expandH);
            ellipse.BeginAnimation(Canvas.LeftProperty, moveL);
            ellipse.BeginAnimation(Canvas.TopProperty,  moveT);

            // Fade out stroke opacity
            var stroke = ((SolidColorBrush)ellipse.Stroke).Clone();
            ellipse.Stroke = stroke;
            var fadeColor = new ColorAnimation(
                ringColor,
                Color.FromArgb(0, ringColor.R, ringColor.G, ringColor.B),
                new Duration(TimeSpan.FromMilliseconds(DURATION)))
            {
                BeginTime      = TimeSpan.FromMilliseconds(DURATION / 3.0),
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn },
            };
            stroke.BeginAnimation(SolidColorBrush.ColorProperty, fadeColor);

            // Close overlay after animation completes
            var closeTimer = new DispatcherTimer
                { Interval = TimeSpan.FromMilliseconds(DURATION + 80) };
            closeTimer.Tick += (s2, e2) => { closeTimer.Stop(); overlay.Close(); };
            closeTimer.Start();
        }

        public void Dispose() { }
    }
}
