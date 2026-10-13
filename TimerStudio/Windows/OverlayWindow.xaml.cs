using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace TimerStudio
{
    public partial class OverlayWindow : Window
    {
        // Each pinned timer gets one OverlayRow that we update in-place every tick
        private readonly Dictionary<int, OverlayRow> _rows
            = new Dictionary<int, OverlayRow>();

        public OverlayWindow()
        {
            InitializeComponent();
            // Restore opacity slider default
            OuterBorder.Opacity = SldOpacity.Value;
            // UI-1: keyboard move (Alt+Arrow) / resize alongside the grip
            OldGamers.Wpf.Shared.Controls.KeyboardWindowMover.Attach(this, DragHandle, allowResize: true);
        }

        // ── Called by MainWindow.Tick every 40ms ─────────────────────────────
        public void Refresh(IEnumerable<(TimerModel Model, TimerCard Card)> allPairs)
        {
            // Collect the set of models currently pinned
            var pinned = new List<(TimerModel Model, TimerCard Card)>();
            foreach (var pair in allPairs)
                if (pair.Model.ShowInOverlay) pinned.Add(pair);

            // Remove rows for timers no longer pinned
            var toRemove = new List<int>();
            foreach (var kv in _rows)
            {
                bool stillPinned = false;
                foreach (var p in pinned) if (p.Model.Id == kv.Key) { stillPinned = true; break; }
                if (!stillPinned) toRemove.Add(kv.Key);
            }
            foreach (int id in toRemove)
            {
                TimerRows.Children.Remove(_rows[id].Container);
                _rows.Remove(id);
            }

            // Add new rows / update existing ones in order
            int insertIdx = 0;
            foreach (var pair in pinned)
            {
                OverlayRow row;
                if (!_rows.TryGetValue(pair.Model.Id, out row))
                {
                    row = new OverlayRow();
                    row.SetFontScales(_timeFontScale, _titleFontScale);
                    row.SetCard(pair.Card);
                    _rows[pair.Model.Id] = row;
                    if (insertIdx < TimerRows.Children.Count)
                        TimerRows.Children.Insert(insertIdx, row.Container);
                    else
                        TimerRows.Children.Add(row.Container);
                }
                row.Update(pair.Model);
                insertIdx++;
            }

            // Auto-hide if nothing pinned
            if (pinned.Count == 0 && IsVisible)
                Hide();
            else if (pinned.Count > 0 && !IsVisible)
                Show();
        }

        // ── Drag to move ──────────────────────────────────────────────────────
        private void DragHandle_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
                DragMove();
        }

        // ── Opacity slider ────────────────────────────────────────────────────
        private void SldOpacity_ValueChanged(object sender,
            RoutedPropertyChangedEventArgs<double> e)
        {
            if (OuterBorder != null)
                OuterBorder.Opacity = e.NewValue;
        }

        // ── Options popup (font scale sliders) ───────────────────────────────
        private void BtnOptions_Click(object sender, RoutedEventArgs e)
        {
            var popup = new OverlayOptionsPopup(this);
            popup.Owner = this;
            // Position near the button
            popup.Left = Left;
            popup.Top  = Top + DragHandle.ActualHeight + 4;
            popup.ShowDialog();
        }

        // ── Hide (don't close — pins stay) ────────────────────────────────────
        private bool _reallyClosing = false;

        private void BtnClose_Click(object sender, RoutedEventArgs e) => Hide();

        /// <summary>
        /// Called by MainWindow during Application.Shutdown() to allow a real close.
        /// Without this, OnClosing cancels every close including shutdown (M-6).
        /// </summary>
        public void CloseForReal()
        {
            _reallyClosing = true;
            Close();
        }

        // M-6: only cancel close if it's a user-initiated hide, not a real shutdown.
        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            if (_reallyClosing) { base.OnClosing(e); return; }
            e.Cancel = true;
            Hide();
        }

        // ── Font scales ───────────────────────────────────────────────────────
        private double _timeFontScale  = 1.0;
        private double _titleFontScale = 1.0;

        public void SetFontScale(double scale)
        {
            _timeFontScale  = scale;
            _titleFontScale = scale;
            foreach (var row in _rows.Values)
                row.SetFontScales(_timeFontScale, _titleFontScale);
        }

        public void SetTimeFontScale(double scale)
        {
            _timeFontScale = scale;
            foreach (var row in _rows.Values)
                row.SetFontScales(_timeFontScale, _titleFontScale);
        }

        public void SetTitleFontScale(double scale)
        {
            _titleFontScale = scale;
            foreach (var row in _rows.Values)
                row.SetFontScales(_timeFontScale, _titleFontScale);
        }

        // ── Keyboard resize (SC 2.5.7 — dragging alternative) ────────────────
        // WindowStyle=None removes the system menu that normally provides
        // Alt+Space → Size keyboard resize. Shift+Arrow = +/- 10 px;
        // Shift+Ctrl+Arrow = +/- 50 px.
        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (!e.KeyboardDevice.Modifiers.HasFlag(ModifierKeys.Shift)) return;

            bool big = e.KeyboardDevice.Modifiers.HasFlag(ModifierKeys.Control);
            double step = big ? 50 : 10;

            switch (e.Key)
            {
                case Key.Right:
                    Width  = Math.Max(MinWidth,  Width  + step);
                    e.Handled = true; break;
                case Key.Left:
                    Width  = Math.Max(MinWidth,  Width  - step);
                    e.Handled = true; break;
                case Key.Down:
                    Height = Math.Max(MinHeight, Height + step);
                    e.Handled = true; break;
                case Key.Up:
                    Height = Math.Max(MinHeight, Height - step);
                    e.Handled = true; break;
            }
        }

        // ── Public method to force-show ───────────────────────────────────────
        public void ShowOverlay()
        {
            Show();
            Activate();
        }

        // ── Window size persistence ───────────────────────────────────────────
        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            var (w, h) = WindowSizeCache.Get("TSOverlay", 320, 200);
            Width = w; Height = h;
        }

        protected override void OnClosed(EventArgs e)
        {
            WindowSizeCache.Set("TSOverlay", Width, Height);
            base.OnClosed(e);
        }
    }

    // ── Per-timer row inside the overlay ──────────────────────────────────────
    internal class OverlayRow
    {
        public FrameworkElement Container { get; }

        private readonly TextBlock _lblName;
        private readonly TextBlock _lblTime;
        private readonly Border    _progressTrack;
        private readonly Border    _progressBar;
        private readonly Border    _rowBorder;
        private readonly Button    _btnRestart;
        private TimerCard          _card;

        public OverlayRow()
        {
            _progressBar = new Border
            {
                Height              = 4,
                CornerRadius        = new CornerRadius(2),
                Background          = new SolidColorBrush(Color.FromRgb(0xF9, 0xE2, 0xAF)),
                HorizontalAlignment = HorizontalAlignment.Left,
                Width               = 0
            };

            _progressTrack = new Border
            {
                Height       = 4,
                CornerRadius = new CornerRadius(2),
                Margin       = new Thickness(0, 4, 0, 0)
            };
            _progressTrack.SetResourceReference(Border.BackgroundProperty, "BrushDivider");
            _progressTrack.Child = _progressBar;

            _lblName = new TextBlock
            {
                FontSize     = OldGamers.Wpf.Shared.Controls.TypeRamp.Caption,
                FontWeight   = FontWeights.SemiBold,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Margin       = new Thickness(0, 0, 0, 2)
            };

            _lblTime = new TextBlock
            {
                FontFamily          = new FontFamily("Courier New"),
                FontSize            = 20,
                FontWeight          = FontWeights.Bold,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin              = new Thickness(0, 2, 0, 0)
            };

            // ↺ Restart button
            // UI-1: vector icon, 44×44 target, accessible name (was a 0-min "↺" glyph at 70 % opacity)
            var restartIcon = new Image { Width = 18, Height = 18, Stretch = Stretch.Uniform };
            restartIcon.SetResourceReference(Image.SourceProperty, "JtIcon.Refresh");
            _btnRestart = new Button
            {
                Content             = restartIcon,
                Background          = System.Windows.Media.Brushes.Transparent,
                BorderThickness     = new Thickness(0),
                Padding             = new Thickness(4),
                MinWidth            = 44, MinHeight = 44,
                VerticalAlignment   = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Right,
                Cursor              = System.Windows.Input.Cursors.Hand,
                ToolTip             = "Restart timer",
            };
            _btnRestart.SetResourceReference(FrameworkElement.StyleProperty, "FlatButton");
            System.Windows.Automation.AutomationProperties.SetName(_btnRestart, "Restart timer");
            _btnRestart.Click += (s, e) =>
            {
                _card?.RestartTimer();
            };

            // Header row: name + restart button side by side
            var headerRow = new System.Windows.Controls.DockPanel();
            System.Windows.Controls.DockPanel.SetDock(_btnRestart, System.Windows.Controls.Dock.Right);
            headerRow.Children.Add(_btnRestart);
            headerRow.Children.Add(_lblName);

            var inner = new StackPanel { Margin = new Thickness(4, 2, 4, 2) };
            inner.Children.Add(headerRow);
            inner.Children.Add(_lblTime);
            inner.Children.Add(_progressTrack);

            _rowBorder = new Border
            {
                CornerRadius = new CornerRadius(6),
                Margin       = new Thickness(0, 2, 0, 2),
                Child        = inner
            };
            ApplyRowTheme(alert: false);

            Container = _rowBorder;
        }

        /// <summary>Binds this row to the live TimerCard so the restart button works.</summary>
        public void SetCard(TimerCard card) { _card = card; }

        private double _timeScale  = 1.0;
        private double _titleScale = 1.0;

        // L-2: cached brushes — only reallocate when AlertColor actually changes.
        // Update() fires every 40 ms per pinned timer; allocating SolidColorBrush
        // objects on every tick generates significant GC pressure.
        private Color          _cachedAlertColor = default;
        private SolidColorBrush _brushAlert;          // CC-alpha alertColor bg
        private SolidColorBrush _brushAlertFg;        // dark foreground on alert bg
        private SolidColorBrush _brushProgressBar;    // progress fill
        private static readonly SolidColorBrush _brushAlertDark = new SolidColorBrush(Color.FromRgb(0x1E, 0x1E, 0x2E));
        private bool? _alertShown;

        // UI-1: normal rows follow the theme (Surf2 / Text); alert rows keep the
        // timer's own alert colour with dark text. Only re-applied on state change.
        private void ApplyRowTheme(bool alert)
        {
            if (_alertShown == alert) return;
            _alertShown = alert;
            if (alert)
            {
                _rowBorder.Background = _brushAlert;
                _lblTime.Foreground   = _brushAlertFg;
                _lblName.Foreground   = _brushAlertFg;
            }
            else
            {
                _rowBorder.SetResourceReference(Border.BackgroundProperty,    "BrushSurf2");
                _lblTime.SetResourceReference(TextBlock.ForegroundProperty,   "BrushText");
                _lblName.SetResourceReference(TextBlock.ForegroundProperty,   "BrushText");
            }
        }

        private void RefreshAlertBrushes(Color alertColor)
        {
            if (_brushAlert != null && alertColor == _cachedAlertColor) return;
            _cachedAlertColor  = alertColor;
            _brushAlert        = new SolidColorBrush(Color.FromArgb(0xCC, alertColor.R, alertColor.G, alertColor.B));
            _brushAlertFg      = _brushAlertDark;
            _brushProgressBar  = new SolidColorBrush(alertColor);
        }

        public void SetFontScales(double timeScale, double titleScale)
        {
            _timeScale  = timeScale;
            _titleScale = titleScale;
            _lblName.FontSize = OldGamers.Wpf.Shared.Controls.TypeRamp.Caption * _titleScale;
        }

        public void Update(TimerModel model)
        {
            _lblName.Text     = model.Name;
            _lblName.FontSize = OldGamers.Wpf.Shared.Controls.TypeRamp.Caption * _titleScale;

            // Time display with auto-scaling font
            string display = model.DisplayTime;
            _lblTime.Text     = display;
            _lblTime.FontSize = (display.Length <= 8 ? 20 : display.Length <= 11 ? 17 : 14) * _timeScale;

            // Alert colour tint — refresh cached brushes only when colour changes (L-2)
            var alertColor = Color.FromRgb(model.AlertColor.R, model.AlertColor.G, model.AlertColor.B);
            bool colourChanged = _brushAlert == null || alertColor != _cachedAlertColor;
            RefreshAlertBrushes(alertColor);

            bool alert = model.AlertActive || model.State == TimerState.Done;
            if (alert && colourChanged) _alertShown = null;   // re-apply new alert brush
            ApplyRowTheme(alert);

            // Progress bar
            _progressBar.Background = _brushProgressBar;

            // Measure track width after layout
            double trackW = _progressTrack.ActualWidth > 4 ? _progressTrack.ActualWidth : 200;
            double progress = model.Progress;
            _progressBar.Width = Math.Max(0, Math.Min(trackW, trackW * progress));
        }

    }
}