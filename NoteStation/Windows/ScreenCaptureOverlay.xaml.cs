using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace NoteStation.Windows
{
    /// <summary>
    /// Full-screen transparent overlay that lets the user drag a selection rectangle.
    /// After drag-release, captures that screen region as a System.Drawing.Bitmap.
    /// </summary>
    public partial class ScreenCaptureOverlay : Window
    {
        private System.Windows.Point _dragStart;
        private bool _isDragging = false;

        /// <summary>The captured bitmap (non-null on success), null if user cancelled.</summary>
        public Bitmap CapturedBitmap { get; private set; } = null;
        /// <summary>True when capture succeeded.</summary>
        public bool   Captured       { get; private set; } = false;

        public ScreenCaptureOverlay()
        {
            InitializeComponent();
            // Position hint at top-center
            Loaded += (_, __) =>
            {
                Canvas.SetLeft(HintText, (ActualWidth - HintText.ActualWidth) / 2);
                Canvas.SetTop(HintText, 20);
            };
            PreviewKeyDown += (s, e) =>
            {
                if (e.Key == Key.Escape) { Captured = false; Close(); }
            };
        }

        protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonDown(e);
            _dragStart   = e.GetPosition(SelectionCanvas);
            _isDragging  = true;
            SelectionRect.Visibility = Visibility.Visible;
            HintText.Visibility      = Visibility.Collapsed;
            CaptureMouse();
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (!_isDragging) return;
            var cur = e.GetPosition(SelectionCanvas);
            double x = Math.Min(_dragStart.X, cur.X);
            double y = Math.Min(_dragStart.Y, cur.Y);
            double w = Math.Abs(cur.X - _dragStart.X);
            double h = Math.Abs(cur.Y - _dragStart.Y);
            Canvas.SetLeft(SelectionRect, x);
            Canvas.SetTop(SelectionRect,  y);
            SelectionRect.Width  = w;
            SelectionRect.Height = h;
        }

        protected override async void OnMouseLeftButtonUp(MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonUp(e);
            if (!_isDragging) return;
            _isDragging = false;
            ReleaseMouseCapture();

            var cur = e.GetPosition(SelectionCanvas);
            int x = (int)Math.Min(_dragStart.X, cur.X);
            int y = (int)Math.Min(_dragStart.Y, cur.Y);
            int w = (int)Math.Abs(cur.X - _dragStart.X);
            int h = (int)Math.Abs(cur.Y - _dragStart.Y);

            if (w < 10 || h < 10) { Captured = false; Close(); return; }

            // Scale WPF device-independent pixels → physical pixels
            var src = PresentationSource.FromVisual(this);
            double dpiX = src?.CompositionTarget?.TransformToDevice.M11 ?? 1.0;
            double dpiY = src?.CompositionTarget?.TransformToDevice.M22 ?? 1.0;

            int px = (int)(x * dpiX);
            int py = (int)(y * dpiY);
            int pw = (int)(w * dpiX);
            int ph = (int)(h * dpiY);

            // Hide before capture so overlay doesn't appear in the screenshot
            Visibility = Visibility.Hidden;
            // Yield (not Thread.Sleep) so the UI thread keeps pumping while the overlay disappears.
            await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.Render);
            await System.Threading.Tasks.Task.Delay(120);

            try
            {
                var bmp = new Bitmap(pw, ph, PixelFormat.Format32bppArgb);
                using (var g = Graphics.FromImage(bmp))
                    g.CopyFromScreen(px, py, 0, 0, new System.Drawing.Size(pw, ph), CopyPixelOperation.SourceCopy);
                CapturedBitmap = bmp;
                Captured       = true;
            }
            catch
            {
                Captured = false;
            }
            Close();
        }
    }
}
