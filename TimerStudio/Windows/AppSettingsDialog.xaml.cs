using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace TimerStudio
{
    public partial class AppSettingsDialog : Window
    {
        private readonly MainWindow      _main;
        private bool                     _loaded;

        // M-7: debounce — only call Save() 500 ms after the last slider change.
        // Without this, every pixel of slider drag calls Save() synchronously on
        // the UI thread, causing UI lag and excessive disk writes.
        private readonly DispatcherTimer _saveDebounce;

        public AppSettingsDialog(MainWindow main)
        {
            InitializeComponent();
            _main = main;

            _saveDebounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            _saveDebounce.Tick += (s, e) => { _saveDebounce.Stop(); AppPrefs.Instance.Save(); };

            Loaded += OnLoaded;
        }

        /// <summary>Restarts the debounce window. Called from every slider ValueChanged handler.</summary>
        private void QueueSave() { _saveDebounce.Stop(); _saveDebounce.Start(); }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            var p = AppPrefs.Instance;

            // Event timer font sizes
            SldEventTimeFontScale.Value  = p.EventTimeFontScale;
            SldEventTitleFontScale.Value = p.EventTitleFontScale;
            LblEventTimeFontScale.Text   = p.EventTimeFontScale.ToString("F2") + "×";
            LblEventTitleFontScale.Text  = p.EventTitleFontScale.ToString("F2") + "×";

            // Countdown / Stopwatch font sizes
            SldTimeFontScale.Value  = p.TimeFontScale;
            SldTitleFontScale.Value = p.TitleFontScale;
            LblTimeFontScale.Text   = p.TimeFontScale.ToString("F2") + "×";
            LblTitleFontScale.Text  = p.TitleFontScale.ToString("F2") + "×";

            // Shared alert defaults
            CboDefaultSound.SelectedIndex = Math.Max(0, Math.Min(5, p.DefaultAlertSound));
            CboDefaultStyle.SelectedIndex = Math.Max(0, Math.Min(2, p.DefaultAlertStyle));
            SldDefaultFlashSecs.Value     = p.DefaultFlashSecs;
            SldDefaultFontSize.Value      = p.DefaultFontSize;
            LblDefaultFlashSecs.Text      = p.DefaultFlashSecs + " s";
            LblDefaultFontSize.Text       = p.DefaultFontSize + " pt";
            ChkDefaultAutoReset.IsChecked = p.DefaultAutoReset;

            _loaded = true;
        }

        // ── Event timer font sizes ────────────────────────────────────────────
        private void SldEventTimeFontScale_Changed(object sender,
            RoutedPropertyChangedEventArgs<double> e)
        {
            if (!_loaded) return;
            double v = Math.Round(e.NewValue * 4) / 4.0;
            AppPrefs.Instance.EventTimeFontScale = v;
            LblEventTimeFontScale.Text = v.ToString("F2") + "×";
            QueueSave();
            _main.ApplyEventTimeFontScale(v);
        }

        private void SldEventTitleFontScale_Changed(object sender,
            RoutedPropertyChangedEventArgs<double> e)
        {
            if (!_loaded) return;
            double v = Math.Round(e.NewValue * 4) / 4.0;
            AppPrefs.Instance.EventTitleFontScale = v;
            LblEventTitleFontScale.Text = v.ToString("F2") + "×";
            QueueSave();
            _main.ApplyEventTitleFontScale(v);
        }

        // ── Countdown / Stopwatch font sizes ──────────────────────────────────
        private void SldTimeFontScale_Changed(object sender,
            RoutedPropertyChangedEventArgs<double> e)
        {
            if (!_loaded) return;
            double v = Math.Round(e.NewValue * 4) / 4.0;
            AppPrefs.Instance.TimeFontScale = v;
            LblTimeFontScale.Text = v.ToString("F2") + "×";
            QueueSave();
            _main.ApplyTimeFontScale(v);
        }

        private void SldTitleFontScale_Changed(object sender,
            RoutedPropertyChangedEventArgs<double> e)
        {
            if (!_loaded) return;
            double v = Math.Round(e.NewValue * 4) / 4.0;
            AppPrefs.Instance.TitleFontScale = v;
            LblTitleFontScale.Text = v.ToString("F2") + "×";
            QueueSave();
            _main.ApplyTitleFontScale(v);
        }

        // ── Shared alert defaults ─────────────────────────────────────────────
        private void CboDefaultSound_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (!_loaded) return;
            AppPrefs.Instance.DefaultAlertSound = CboDefaultSound.SelectedIndex;
            AppPrefs.Instance.Save(); // combo change is infrequent — save immediately
        }

        private void CboDefaultStyle_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (!_loaded) return;
            AppPrefs.Instance.DefaultAlertStyle = CboDefaultStyle.SelectedIndex;
            AppPrefs.Instance.Save(); // combo change is infrequent — save immediately
        }

        private void SldDefaultFlashSecs_Changed(object sender,
            RoutedPropertyChangedEventArgs<double> e)
        {
            if (!_loaded) return;
            int v = (int)e.NewValue;
            AppPrefs.Instance.DefaultFlashSecs = v;
            LblDefaultFlashSecs.Text = v + " s";
            QueueSave();
        }

        private void SldDefaultFontSize_Changed(object sender,
            RoutedPropertyChangedEventArgs<double> e)
        {
            if (!_loaded) return;
            int v = (int)e.NewValue;
            AppPrefs.Instance.DefaultFontSize = v;
            LblDefaultFontSize.Text = v + " pt";
            QueueSave();
        }

        private void ChkDefaultAutoReset_Changed(object sender, RoutedEventArgs e)
        {
            if (!_loaded) return;
            AppPrefs.Instance.DefaultAutoReset = ChkDefaultAutoReset.IsChecked == true;
            AppPrefs.Instance.Save();
        }

        private void BtnDone_Click(object sender, RoutedEventArgs e) => Close();

        // ── Window size persistence ───────────────────────────────────────────
        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            var (w, h) = WindowSizeCache.Get("AppSettingsDialog", 460, 700);
            Width = w; Height = h;
        }

        protected override void OnClosed(EventArgs e)
        {
            _saveDebounce.Stop(); // prevent post-close fire keeping dialog alive
            WindowSizeCache.Set("AppSettingsDialog", Width, Height);
            base.OnClosed(e);
        }
    }
}
