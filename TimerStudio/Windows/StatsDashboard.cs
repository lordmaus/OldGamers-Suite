using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace TimerStudio
{
    /// <summary>
    /// F24: Alert history statistics dashboard.
    /// Reads history.json and shows per-timer and per-day bar charts.
    /// No external charting library — pure WPF StackPanel + border bars.
    /// </summary>
    public partial class StatsDashboard : Window
    {
        public StatsDashboard()
        {
            InitializeComponent();
            Loaded += (s, e) => Refresh();
        }

        // ── Controls ──────────────────────────────────────────────────────────
        private void CboPeriod_Changed(object sender, SelectionChangedEventArgs e) => Refresh();
        private void BtnRefresh_Click(object sender, RoutedEventArgs e) => Refresh();

        private void BtnClear_Click(object sender, RoutedEventArgs e)
        {
            var r = MessageBox.Show(
                "Delete all alert history? This cannot be undone.",
                "Clear History", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (r != MessageBoxResult.Yes) return;
            TimerHistory.Clear();
            Refresh();
        }

        // ── Data load + render ────────────────────────────────────────────────
        private void Refresh()
        {
            // CboPeriod fires SelectionChanged during XAML init (IsSelected="True") before
            // InitializeComponent() returns — controls are null at that point.
            if (!IsLoaded) return;

            var all     = TimerHistory.GetAll();
            var cutoff  = GetCutoff();
            var entries = cutoff.HasValue
                ? all.Where(e => ParseDate(e.FiredAt) >= cutoff.Value).ToList()
                : all;

            LblTotalFires.Text   = entries.Count + " alert" + (entries.Count != 1 ? "s" : "");
            LblUniqueTimers.Text = entries.Select(e => e.TimerName).Distinct().Count() + " timer" +
                                   (entries.Select(e => e.TimerName).Distinct().Count() != 1 ? "s" : "");
            LblPeriodLabel.Text  = PeriodLabel();

            RenderTimerBars(entries);
            RenderDayBars(entries);
        }

        private void RenderTimerBars(List<TimerHistoryEntry> entries)
        {
            PnlBars.Children.Clear();
            if (entries.Count == 0)
            {
                PnlBars.Children.Add(new TextBlock
                    { Text = "No data for this period.", FontSize = 12,
                      Foreground = FindResource("BrushMuted") as Brush });
                return;
            }

            var groups = entries
                .GroupBy(e => e.TimerName)
                .Select(g => (Name: g.Key, Count: g.Count()))
                .OrderByDescending(x => x.Count)
                .ToList();

            int max = groups.Max(x => x.Count);
            var fillBrush = FindResource("BrushBlue") as Brush ?? Brushes.DodgerBlue;
            var textBrush = FindResource("BrushText") as Brush ?? Brushes.White;
            var mutedBrush = FindResource("BrushMuted") as Brush ?? Brushes.Gray;

            foreach (var (name, count) in groups)
            {
                double fraction = max > 0 ? (double)count / max : 0;

                var row = new Grid { Margin = new Thickness(0, 0, 0, 6) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(36) });

                var nameLabel = new TextBlock
                {
                    Text                = name,
                    FontSize            = OldGamers.Wpf.Shared.Controls.TypeRamp.Caption,
                    Foreground          = textBrush,
                    VerticalAlignment   = VerticalAlignment.Center,
                    TextTrimming        = TextTrimming.CharacterEllipsis,
                    Margin              = new Thickness(0, 0, 8, 0)
                };
                Grid.SetColumn(nameLabel, 0);
                row.Children.Add(nameLabel);

                var trackBorder = new Border
                {
                    Height      = 20,
                    Background  = FindResource("BrushSurf2") as Brush ?? Brushes.DimGray,
                    CornerRadius = new CornerRadius(4),
                    VerticalAlignment = VerticalAlignment.Center
                };
                var fill = new Border
                {
                    Background   = fillBrush,
                    CornerRadius = new CornerRadius(4),
                    HorizontalAlignment = HorizontalAlignment.Left,
                    Width        = double.NaN // set in SizeChanged
                };
                trackBorder.Child = fill;
                trackBorder.SizeChanged += (s, e) =>
                    fill.Width = Math.Max(4, trackBorder.ActualWidth * fraction);
                Grid.SetColumn(trackBorder, 1);
                row.Children.Add(trackBorder);

                var countLabel = new TextBlock
                {
                    Text              = count.ToString(),
                    FontSize          = OldGamers.Wpf.Shared.Controls.TypeRamp.Caption,
                    Foreground        = mutedBrush,
                    VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Margin            = new Thickness(6, 0, 0, 0)
                };
                Grid.SetColumn(countLabel, 2);
                row.Children.Add(countLabel);

                PnlBars.Children.Add(row);
            }
        }

        private void RenderDayBars(List<TimerHistoryEntry> entries)
        {
            PnlDayBars.Children.Clear();
            if (entries.Count == 0) return;

            var groups = entries
                .GroupBy(e =>
                {
                    var d = ParseDate(e.FiredAt);
                    return d.Date;
                })
                .Select(g => (Date: g.Key, Count: g.Count()))
                .OrderBy(x => x.Date)
                .ToList();

            // Show last N days (up to 30 or 14 recent ones)
            if (groups.Count > 30) groups = groups.Skip(groups.Count - 30).ToList();

            int max = groups.Max(x => x.Count);
            var fillBrush  = FindResource("BrushBlue")  as Brush ?? Brushes.DodgerBlue;
            var textBrush  = FindResource("BrushText")  as Brush ?? Brushes.White;
            var mutedBrush = FindResource("BrushMuted") as Brush ?? Brushes.Gray;

            foreach (var (date, count) in groups)
            {
                double fraction = max > 0 ? (double)count / max : 0;
                string dayLabel = date == DateTime.Today
                    ? "Today"
                    : date == DateTime.Today.AddDays(-1)
                    ? "Yesterday"
                    : date.ToString("ddd M/d");

                var row = new Grid { Margin = new Thickness(0, 0, 0, 4) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(30) });

                var nameLabel = new TextBlock
                {
                    Text = dayLabel, FontSize = OldGamers.Wpf.Shared.Controls.TypeRamp.Caption, Foreground = mutedBrush,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 8, 0)
                };
                Grid.SetColumn(nameLabel, 0);
                row.Children.Add(nameLabel);

                var trackBorder = new Border
                {
                    Height = 16, Background = FindResource("BrushSurf2") as Brush ?? Brushes.DimGray,
                    CornerRadius = new CornerRadius(3), VerticalAlignment = VerticalAlignment.Center
                };
                var fill = new Border
                {
                    Background = fillBrush, CornerRadius = new CornerRadius(3),
                    HorizontalAlignment = HorizontalAlignment.Left, Width = double.NaN
                };
                trackBorder.Child = fill;
                trackBorder.SizeChanged += (s, e) =>
                    fill.Width = Math.Max(4, trackBorder.ActualWidth * fraction);
                Grid.SetColumn(trackBorder, 1);
                row.Children.Add(trackBorder);

                var countLabel = new TextBlock
                {
                    Text = count.ToString(), FontSize = OldGamers.Wpf.Shared.Controls.TypeRamp.Caption, Foreground = mutedBrush,
                    VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Margin = new Thickness(4, 0, 0, 0)
                };
                Grid.SetColumn(countLabel, 2);
                row.Children.Add(countLabel);

                PnlDayBars.Children.Add(row);
            }
        }

        // ── Helpers ───────────────────────────────────────────────────────────
        private DateTime? GetCutoff()
        {
            return CboPeriod?.SelectedIndex switch
            {
                0 => DateTime.Today,
                1 => DateTime.Today.AddDays(-7),
                2 => DateTime.Today.AddDays(-30),
                _ => (DateTime?)null  // All time
            };
        }

        private string PeriodLabel() =>
            CboPeriod?.SelectedIndex switch
            {
                0 => "today",
                1 => "last 7 days",
                2 => "last 30 days",
                _ => "all time"
            };

        private static DateTime ParseDate(string isoUtc)
        {
            if (DateTime.TryParse(isoUtc, null, System.Globalization.DateTimeStyles.RoundtripKind, out var dt))
                return dt.ToLocalTime();
            return DateTime.MinValue;
        }

        // ── Window size persistence ───────────────────────────────────────────
        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            var (w, h) = WindowSizeCache.Get("StatsDashboard", 680, 520);
            Width = w; Height = h;
        }

        protected override void OnClosed(EventArgs e)
        {
            WindowSizeCache.Set("StatsDashboard", Width, Height);
            base.OnClosed(e);
        }
    }
}
