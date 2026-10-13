using System;
using System.Windows;
using System.Windows.Interop;
using static SuperMagnifier.NativeMethods;

namespace SuperMagnifier
{
    /// <summary>
    /// A second floating lens window that mirrors the primary lens source rect
    /// onto a different monitor. Uses its own MagHwndHost + MagnifierEngine.
    /// Shown/hidden via the "Duplicate to monitor" toggle in Lens Controls.
    /// </summary>
    public sealed class MirrorLensWindow : Window, IDisposable
    {
        private MagHwndHost     _magHost;
        private MagnifierEngine _engine;
        private bool            _disposed;
        private HwndSource      _hwndSource;

        public MirrorLensWindow()
        {
            Title           = "Super Magnifier LV — Mirror";
            WindowStyle     = WindowStyle.SingleBorderWindow;
            ResizeMode      = ResizeMode.CanResize;
            ShowInTaskbar   = false;
            Topmost         = true;
            Width           = 520;
            Height          = 360;
            Background      = System.Windows.Media.Brushes.Black;
            // WCAG: identify the window to assistive technologies
            System.Windows.Automation.AutomationProperties.SetName(this,
                "Super Magnifier LV Mirror Lens");
            System.Windows.Automation.AutomationProperties.SetHelpText(this,
                "Mirror lens showing the same magnified view as the primary lens on a second monitor");
            Loaded         += OnLoaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            _hwndSource = PresentationSource.FromVisual(this) as HwndSource;
            if (_hwndSource == null) return;

            IntPtr hwnd = _hwndSource.Handle;
            int ex = GetWindowLong(hwnd, GWL_EXSTYLE);
            SetWindowLong(hwnd, GWL_EXSTYLE, ex | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW);

            int w = Math.Max(4, (int)ActualWidth  - 16);
            int h = Math.Max(4, (int)ActualHeight - 40);
            _magHost = new MagHwndHost(w, h);
            Content  = _magHost;

            _engine  = new MagnifierEngine();
            _engine.Initialize();
            float dpiX = 1f, dpiY = 1f;
            if (_hwndSource.CompositionTarget != null)
            {
                dpiX = (float)_hwndSource.CompositionTarget.TransformToDevice.M11;
                dpiY = (float)_hwndSource.CompositionTarget.TransformToDevice.M22;
            }
            _engine.SetDpiScale(dpiX, dpiY);

            // Place on a secondary monitor if available
            PlaceOnSecondaryMonitor();
        }

        private void PlaceOnSecondaryMonitor()
        {
            var monitors = MonitorHelper.GetAllMonitors();
            MonitorHelper.MonitorInfo secondary = null;
            foreach (var m in monitors)
                if (!m.IsPrimary) { secondary = m; break; }

            if (secondary == null) return; // only one monitor — stay where WPF put us

            float dpi = 1f;
            if (_hwndSource?.CompositionTarget != null)
                dpi = (float)_hwndSource.CompositionTarget.TransformFromDevice.M11;

            Left = secondary.WorkArea.Left * dpi;
            Top  = secondary.WorkArea.Top  * dpi;
        }

        /// <summary>
        /// Called every timer tick from MainWindow to push the same source rect
        /// that the primary lens is viewing.
        /// </summary>
        public void UpdateSource(int srcLeft, int srcTop, int srcRight, int srcBottom,
                                 float zoom)
        {
            if (_magHost == null || _engine == null) return;
            var magHwnd = _magHost.MagHwnd;
            if (magHwnd == IntPtr.Zero) return;

            _engine.ApplyZoom(magHwnd, zoom);
            var src = new RECT(srcLeft, srcTop, srcRight, srcBottom);
            MagSetWindowSource(magHwnd, src);
            InvalidateRect(magHwnd, IntPtr.Zero, true);
        }

        /// <summary>Apply the same colour effect as the primary lens.</summary>
        public void ApplyColorEffect(bool smooth, string colorMode, float tintIntensity = 0.5f)
        {
            if (_magHost == null || _engine == null) return;
            var magHwnd = _magHost.MagHwnd;
            if (magHwnd == IntPtr.Zero) return;
            _engine.ApplyColorEffect(magHwnd, smooth, colorMode, tintIntensity);
        }

        protected override void OnClosed(EventArgs e)
        {
            _hwndSource?.RemoveHook(WndProc);
            base.OnClosed(e);
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam,
                               IntPtr lParam, ref bool handled)
        {
            if (msg == 0x0021) { handled = true; return new IntPtr(3); }
            return IntPtr.Zero;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _engine?.Dispose();
            Dispatcher.Invoke(Close);
        }
    }
}
