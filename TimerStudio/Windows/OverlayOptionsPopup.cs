using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace TimerStudio
{
    /// <summary>
    /// Small popup window opened from the ⚙ button on the Timer Overlay.
    /// Provides independent time-display and title font-scale sliders
    /// specific to the overlay (separate from card font scales).
    /// </summary>
    internal class OverlayOptionsPopup : Window
    {
        private readonly OverlayWindow _overlay;
        private readonly Slider _sldTime;
        private readonly Slider _sldTitle;
        private readonly TextBlock _lblTime;
        private readonly TextBlock _lblTitle;
        private bool _loaded;

        public OverlayOptionsPopup(OverlayWindow overlay)
        {
            _overlay = overlay;

            Title               = "Overlay Options";
            Width               = 340;
            SizeToContent       = SizeToContent.Height;
            ResizeMode          = ResizeMode.NoResize;
            WindowStartupLocation = WindowStartupLocation.Manual;
            ShowInTaskbar       = false;
            Topmost             = true;
            Background          = new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x1A));

            var root = new StackPanel { Margin = new Thickness(16, 14, 16, 14) };

            // Header
            var header = new TextBlock
            {
                Text       = "⚙  Overlay Font Sizes",
                FontFamily = new FontFamily("Segoe UI"),
                FontSize   = 13,
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.White,
                Margin     = new Thickness(0, 0, 0, 12)
            };
            root.Children.Add(header);

            // Time display scale
            root.Children.Add(MakeLabel("Timer display size", "Large time digits on each row"));
            var timeRow = MakeSliderRow(out _sldTime, out _lblTime);
            root.Children.Add(timeRow);

            var sep = new System.Windows.Shapes.Rectangle
            {
                Height = 1, Fill = new SolidColorBrush(Color.FromRgb(0x33, 0x33, 0x33)),
                Margin = new Thickness(0, 8, 0, 8)
            };
            root.Children.Add(sep);

            // Title scale
            root.Children.Add(MakeLabel("Timer title size", "Timer name label on each row"));
            var titleRow = MakeSliderRow(out _sldTitle, out _lblTitle);
            root.Children.Add(titleRow);

            // Buttons
            var btnRow = new Grid { Margin = new Thickness(0, 14, 0, 0) };
            btnRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            btnRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
            btnRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var btnReset = new Button
            {
                Content    = "Reset",
                Padding    = new Thickness(12, 6, 12, 6),
                MinHeight  = 36,
                Background = new SolidColorBrush(Color.FromRgb(0x33, 0x33, 0x33)),
                Foreground = Brushes.White,
                BorderThickness = new Thickness(0),
                Cursor     = System.Windows.Input.Cursors.Hand
            };
            btnReset.Click += (s, e) => ResetDefaults();
            Grid.SetColumn(btnReset, 0);

            var btnClose = new Button
            {
                Content    = "Close",
                Padding    = new Thickness(16, 6, 16, 6),
                MinHeight  = 36,
                Background = new SolidColorBrush(Color.FromRgb(0x00, 0x40, 0xCC)),
                Foreground = Brushes.White,
                BorderThickness = new Thickness(0),
                Cursor     = System.Windows.Input.Cursors.Hand
            };
            btnClose.Click += (s, e) => Close();
            Grid.SetColumn(btnClose, 2);

            btnRow.Children.Add(btnReset);
            btnRow.Children.Add(btnClose);
            root.Children.Add(btnRow);

            Content = root;

            // Load saved values
            Loaded += (s, e) =>
            {
                _sldTime.Value  = AppPrefs.Instance.OverlayTimeFontScale;
                _sldTitle.Value = AppPrefs.Instance.OverlayTitleFontScale;
                _lblTime.Text   = AppPrefs.Instance.OverlayTimeFontScale.ToString("F2")  + "×";
                _lblTitle.Text  = AppPrefs.Instance.OverlayTitleFontScale.ToString("F2") + "×";
                _loaded = true;
            };

            _sldTime.ValueChanged  += OnTimeFontChanged;
            _sldTitle.ValueChanged += OnTitleFontChanged;
        }

        private void OnTimeFontChanged(object s, RoutedPropertyChangedEventArgs<double> e)
        {
            if (!_loaded) return;
            double v = Math.Round(e.NewValue * 4) / 4.0;
            _lblTime.Text = v.ToString("F2") + "×";
            AppPrefs.Instance.OverlayTimeFontScale = v;
            AppPrefs.Instance.Save();
            _overlay.SetTimeFontScale(v);
        }

        private void OnTitleFontChanged(object s, RoutedPropertyChangedEventArgs<double> e)
        {
            if (!_loaded) return;
            double v = Math.Round(e.NewValue * 4) / 4.0;
            _lblTitle.Text = v.ToString("F2") + "×";
            AppPrefs.Instance.OverlayTitleFontScale = v;
            AppPrefs.Instance.Save();
            _overlay.SetTitleFontScale(v);
        }

        private void ResetDefaults()
        {
            _sldTime.Value  = 1.0;
            _sldTitle.Value = 1.0;
        }

        // ── UI helpers ────────────────────────────────────────────────────────
        private static UIElement MakeLabel(string title, string sub)
        {
            var sp = new StackPanel { Margin = new Thickness(0, 0, 0, 4) };
            sp.Children.Add(new TextBlock
            {
                Text       = title,
                FontFamily = new FontFamily("Segoe UI"),
                FontSize   = OldGamers.Wpf.Shared.Controls.TypeRamp.Caption,
                Foreground = Brushes.White
            });
            sp.Children.Add(new TextBlock
            {
                Text       = sub,
                FontFamily = new FontFamily("Segoe UI"),
                FontSize   = OldGamers.Wpf.Shared.Controls.TypeRamp.Caption,
                Foreground = new SolidColorBrush(Color.FromRgb(0xC0, 0xC0, 0xC0))
            });
            return sp;
        }

        private static Grid MakeSliderRow(out Slider sld, out TextBlock lbl)
        {
            var g = new Grid { Margin = new Thickness(0, 4, 0, 4) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(44) });

            sld = new Slider
            {
                Minimum            = 0.5,
                Maximum            = 4.0,
                TickFrequency      = 0.25,
                IsSnapToTickEnabled = true,
                VerticalAlignment  = VerticalAlignment.Center
            };
            Grid.SetColumn(sld, 0);

            lbl = new TextBlock
            {
                Text              = "1.00×",
                FontSize          = OldGamers.Wpf.Shared.Controls.TypeRamp.Caption,
                FontWeight        = FontWeights.SemiBold,
                Foreground        = Brushes.White,
                VerticalAlignment = VerticalAlignment.Center,
                Margin            = new Thickness(6, 0, 0, 0)
            };
            Grid.SetColumn(lbl, 1);

            g.Children.Add(sld);
            g.Children.Add(lbl);
            return g;
        }
    }
}
