using System;
using System.Windows;
using System.Windows.Documents;

namespace BadgeOverlay
{
    /// <summary>
    /// Three-screen onboarding tour shown once on first launch.
    ///
    /// Trigger logic in <see cref="MainWindow.OnLoaded"/>:
    ///     if (!_s.FirstRunWelcomed) ShowWelcomeOnce();
    ///
    /// Sets <see cref="AppSettings.FirstRunWelcomed"/> to true on
    /// "Get started" or "Skip" — never on simple window close, so an
    /// accidental Alt+F4 doesn't suppress the tour for users who
    /// haven't actually finished it. Closing without confirming is a
    /// no-op (settings flag stays false; welcome shows again next launch).
    /// </summary>
    public partial class WelcomeWindow : Window
    {
        private int _step = 1;
        private const int LastStep = 3;
        private bool _completed;

        public WelcomeWindow()
        {
            InitializeComponent();
        }

        /// <summary>
        /// True if the user explicitly completed (Get started) or
        /// skipped (Skip link). False on plain window close — caller
        /// must distinguish so the FirstRunWelcomed flag isn't
        /// prematurely set.
        /// </summary>
        public bool Completed => _completed;

        private void Next_Click(object sender, RoutedEventArgs e)
        {
            if (_step < LastStep)
            {
                _step++;
                ApplyStep();
            }
            else
            {
                _completed = true;
                DialogResult = true;
                Close();
            }
        }

        private void Back_Click(object sender, RoutedEventArgs e)
        {
            if (_step > 1)
            {
                _step--;
                ApplyStep();
            }
        }

        private void Skip_Click(object sender, RoutedEventArgs e)
        {
            // Skip = "I've seen enough; don't show again" — same
            // outcome as completing, just earlier. The user-intent
            // signal is the same: stop showing this on every launch.
            _completed = true;
            DialogResult = false;
            Close();
        }

        private void ApplyStep()
        {
            Step1.Visibility = _step == 1 ? Visibility.Visible : Visibility.Collapsed;
            Step2.Visibility = _step == 2 ? Visibility.Visible : Visibility.Collapsed;
            Step3.Visibility = _step == 3 ? Visibility.Visible : Visibility.Collapsed;

            // Pill highlight
            try
            {
                Pill1.SetResourceReference(System.Windows.Shapes.Shape.FillProperty,
                    _step >= 1 ? "BrushBlue" : "BrushSurf2");
                Pill2.SetResourceReference(System.Windows.Shapes.Shape.FillProperty,
                    _step >= 2 ? "BrushBlue" : "BrushSurf2");
                Pill3.SetResourceReference(System.Windows.Shapes.Shape.FillProperty,
                    _step >= 3 ? "BrushBlue" : "BrushSurf2");
            }
            catch { /* swallow: theme not yet realised */ }

            BtnBack.IsEnabled = _step > 1;
            bool last = _step == LastStep;
            BtnNextText.Text   = last ? "Get started" : "Next";
            BtnNextIcon.Source = JtIcons.Get(last ? "complete" : "forward");
            System.Windows.Automation.AutomationProperties.SetName(BtnNext, BtnNextText.Text);
        }

        // ── Window size persistence ───────────────────────────────────────────
        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            var (w, h) = WindowSizeCache.Get("BadgeWelcome", 540, 540);
            Width = w; Height = h;
        }

        protected override void OnClosed(EventArgs e)
        {
            WindowSizeCache.Set("BadgeWelcome", Width, Height);
            base.OnClosed(e);
        }
    }
}