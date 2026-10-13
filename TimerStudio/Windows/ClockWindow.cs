using System;
using System.Windows;
using System.Windows.Threading;

namespace TimerStudio
{
    /// <summary>
    /// F15: Always-on-top clock window — large live current time display.
    /// Transparent chrome, draggable, togglable seconds and 12/24h format.
    /// Independent sliders for time and date font sizes.
    /// </summary>
    public partial class ClockWindow : Window
    {
        private readonly DispatcherTimer _tick;
        private bool _showSeconds = true;
        private bool _use24h      = false;

        public ClockWindow()
        {
            InitializeComponent();

            // WCAG 2.1.3: borderless window — Alt+Arrow move, Ctrl+Alt+Arrow resize, SC_MOVE/SC_SIZE menu items.
            OldGamers.Wpf.Shared.Controls.KeyboardWindowMover.Attach(this, RootBorder, allowResize: true);

            _tick = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
            _tick.Tick += (s, e) => Refresh();
            _tick.Start();

            Refresh();
        }

        // ── Drag support (WindowStyle=None) ──────────────────────────────────
        private void TitleBar_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (e.LeftButton == System.Windows.Input.MouseButtonState.Pressed)
                DragMove();
        }

        // ── Controls ─────────────────────────────────────────────────────────
        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            _tick.Stop();
            Close();
        }

        private void TglSeconds_Click(object sender, RoutedEventArgs e)
        {
            _showSeconds = TglSeconds.IsChecked == true;
            Refresh();
        }

        private void Tgl24h_Click(object sender, RoutedEventArgs e)
        {
            _use24h = Tgl24h.IsChecked == true;
            Refresh();
        }

        private void SldTimeSize_Changed(object sender,
            System.Windows.RoutedPropertyChangedEventArgs<double> e)
        {
            if (LblTime != null)
                LblTime.FontSize = e.NewValue;
        }

        private void SldDateSize_Changed(object sender,
            System.Windows.RoutedPropertyChangedEventArgs<double> e)
        {
            if (LblDate != null)
                LblDate.FontSize = e.NewValue;
        }

        // ── Clock refresh ─────────────────────────────────────────────────────
        private void Refresh()
        {
            var now = DateTime.Now;

            if (_use24h)
                LblTime.Text = _showSeconds ? now.ToString("HH:mm:ss") : now.ToString("HH:mm");
            else
                LblTime.Text = now.ToString(_showSeconds ? "h:mm:ss tt" : "h:mm tt");

            LblDate.Text = now.ToString("dddd, MMMM d, yyyy");
        }

        // ── Window size persistence ───────────────────────────────────────────
        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            var (w, h) = WindowSizeCache.Get("ClockWindow", 480, 260);
            Width = w; Height = h;
        }

        protected override void OnClosed(EventArgs e)
        {
            _tick.Stop();
            WindowSizeCache.Set("ClockWindow", Width, Height);
            base.OnClosed(e);
        }
    }
}
