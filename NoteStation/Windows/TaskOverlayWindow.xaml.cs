using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;

namespace NoteStation
{
    public partial class TaskOverlayWindow : Window
    {
        // ── Events ────────────────────────────────────────────────────────────
        public event EventHandler<TaskItem> TaskCompletionToggled;

        // ── Filter ────────────────────────────────────────────────────────────
        private enum TaskFilter { All, Today, Starred, Overdue }
        private TaskFilter _filter = TaskFilter.All;
        private IReadOnlyList<TaskItem> _lastTasks = Array.Empty<TaskItem>();
        private bool _reallyClosing;
        private double _fontSize = 12;
        private HwndSource _hwndSrc;

        // ── Priority dot colors ───────────────────────────────────────────────
        private static readonly Color[] _priorityColors =
        {
            Colors.Transparent,                              // 0 = none
            Color.FromRgb(0x89, 0xDC, 0xEB),               // 1 = low  (cyan)
            Color.FromRgb(0xF9, 0xE2, 0xAF),               // 2 = med  (amber)
            Color.FromRgb(0xF3, 0x8B, 0xA8),               // 3 = high (red)
        };

        // ── Init ──────────────────────────────────────────────────────────────
        public TaskOverlayWindow()
        {
            InitializeComponent();
            // WCAG 2.1.3: borderless overlay — Alt+Arrow moves, Ctrl+Alt+Arrow resizes; the header's
            // right-click menu gets "Move/Resize with keyboard" (the drag band is mouse-only).
            NoteStation.Controls.KeyboardWindowMover.Attach(this, DragHandle, allowResize: true);
            SourceInitialized += (_, __) =>
            {
                _hwndSrc = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
                _hwndSrc?.AddHook(OverlayWndProc);
            };
            // Restore saved position/size/opacity/font
            var p = AppPrefs.Instance;
            Width     = p.TaskOverlayWidth    > 0  ? p.TaskOverlayWidth    : 300;
            Height    = p.TaskOverlayHeight   > 0  ? p.TaskOverlayHeight   : 420;
            _fontSize = p.TaskOverlayFontSize >= 8 ? p.TaskOverlayFontSize : 24;
            if (TxtOverlayFontSize != null) TxtOverlayFontSize.Text = _fontSize.ToString("0");
            double savedOpacity = p.TaskOverlayOpacity > 0 ? p.TaskOverlayOpacity : 0.9;
            SldOpacity.Value = Math.Max(0.3, Math.Min(1.0, savedOpacity));
            ApplyOverlayOpacity(SldOpacity.Value);

            if (!double.IsNaN(p.TaskOverlayLeft) && !double.IsNaN(p.TaskOverlayTop))
            {
                Left = Math.Max(SystemParameters.VirtualScreenLeft,
                       Math.Min(p.TaskOverlayLeft,
                                SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth - 50));
                Top  = Math.Max(SystemParameters.VirtualScreenTop,
                       Math.Min(p.TaskOverlayTop,
                                SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight - 50));
            }
        }

        // ── Public API ────────────────────────────────────────────────────────
        /// <summary>Called by MainWindow whenever _tasks changes.</summary>
        public void Refresh(IEnumerable<TaskItem> tasks)
        {
            _lastTasks = tasks.ToList();
            RebuildRows();
        }

        // ── Row building ──────────────────────────────────────────────────────
        private void RebuildRows()
        {
            TasksPanel.Children.Clear();

            var today = DateTime.Today;

            IEnumerable<TaskItem> source = _filter switch
            {
                TaskFilter.Today   => _lastTasks.Where(t => !t.IsArchived && t.HasDueDate && t.DueDate.HasValue && t.DueDate.Value.Date == today),
                TaskFilter.Starred => _lastTasks.Where(t => !t.IsArchived && t.IsStarred),
                TaskFilter.Overdue => _lastTasks.Where(t => !t.IsArchived && t.HasDueDate && t.DueDate.HasValue && t.DueDate.Value < today && !t.IsCompleted),
                _                  => _lastTasks.Where(t => !t.IsArchived),
            };

            foreach (var task in source)
                TasksPanel.Children.Add(BuildTaskRow(task));
        }

        private UIElement BuildTaskRow(TaskItem task)
        {
            // Row border
            var border = new Border
            {
                CornerRadius = new CornerRadius(5),
                Padding      = new Thickness(8, 6, 8, 6),
                Margin       = new Thickness(0, 1, 0, 1),
                Background   = new SolidColorBrush(Color.FromArgb(0x33, 0x45, 0x47, 0x5A)),
                Cursor       = Cursors.Arrow,
            };
            border.SetValue(AutomationProperties.NameProperty, task.Title);

            // Grid: Auto (checkbox) | Auto (priority dot) | * (title) | Auto (due date)
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            // Col 0: checkbox
            var cb = new CheckBox
            {
                IsChecked         = task.IsCompleted,
                VerticalAlignment = VerticalAlignment.Center,
                Margin            = new Thickness(0, 0, 6, 0),
                Foreground        = new SolidColorBrush(Color.FromRgb(0xA6, 0xAD, 0xC8)),
            };
            cb.SetValue(AutomationProperties.NameProperty, "Mark complete: " + task.Title);
            cb.Click += (_, __) =>
            {
                task.IsCompleted = cb.IsChecked == true;
                task.CompletedAt = task.IsCompleted ? DateTime.Now : null;
                // Update strikethrough
                UpdateTitleDecoration(grid, task.IsCompleted);
                TaskCompletionToggled?.Invoke(this, task);
            };
            Grid.SetColumn(cb, 0);
            grid.Children.Add(cb);

            // Col 1: priority dot (hidden for 0)
            if (task.Priority >= 1 && task.Priority <= 3)
            {
                var dot = new System.Windows.Shapes.Ellipse
                {
                    Width             = 8,
                    Height            = 8,
                    Fill              = new SolidColorBrush(_priorityColors[task.Priority]),
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin            = new Thickness(0, 0, 6, 0),
                    ToolTip           = task.Priority == 1 ? "Low" : task.Priority == 2 ? "Medium" : "High",
                };
                dot.SetValue(AutomationProperties.NameProperty,
                    task.Priority == 1 ? "Low priority" : task.Priority == 2 ? "Medium priority" : "High priority");
                Grid.SetColumn(dot, 1);
                grid.Children.Add(dot);
            }

            // Col 2: title (+ optional star)
            var titleStack = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center,
            };

            double fs    = _fontSize;
            double fsSub = Math.Max(8, fs - 2);

            var titleTb = new TextBlock
            {
                Text            = task.Title,
                FontFamily      = new FontFamily("Segoe UI"),
                FontSize        = fs,
                Foreground      = new SolidColorBrush(Color.FromRgb(0xCD, 0xD6, 0xF4)),
                TextTrimming    = TextTrimming.CharacterEllipsis,
                TextDecorations = task.IsCompleted ? TextDecorations.Strikethrough : null,
                Tag             = "titleTb",   // tag so we can find it later
            };
            titleStack.Children.Add(titleTb);

            if (task.IsStarred)
            {
                var star = JtIcons.Img("star", Math.Max(12, fsSub));
                star.Margin = new Thickness(4, 0, 0, 0);
                star.SetValue(AutomationProperties.NameProperty, "Starred");
                titleStack.Children.Add(star);
            }

            Grid.SetColumn(titleStack, 2);
            grid.Children.Add(titleStack);

            // Col 3: due date (if HasDueDate)
            if (task.HasDueDate && task.DueDate.HasValue)
            {
                bool overdue = task.DueDate.Value.Date < DateTime.Today && !task.IsCompleted;
                var dueTb = new TextBlock
                {
                    Text              = (overdue ? "⚠ " : "") + task.DueDate.Value.ToString("M/d"),   // glyph = non-colour overdue cue
                    FontFamily        = new FontFamily("Segoe UI"),
                    FontSize          = fsSub,
                    Foreground        = overdue
                                        ? new SolidColorBrush(Color.FromRgb(0xF3, 0x8B, 0xA8))
                                        : new SolidColorBrush(Color.FromRgb(0xBA, 0xC2, 0xDE)),
                    VerticalAlignment = VerticalAlignment.Top,
                    Margin            = new Thickness(6, 1, 0, 0),
                    ToolTip           = task.DueDate.Value.ToString("ddd, MMM d"),
                };
                Grid.SetColumn(dueTb, 3);
                grid.Children.Add(dueTb);
            }

            border.Child = grid;

            // Hover highlight
            var normalBg = (SolidColorBrush)border.Background;
            border.MouseEnter += (_, __) => border.Background = new SolidColorBrush(Color.FromArgb(0x44, 0x45, 0x47, 0x5A));
            border.MouseLeave += (_, __) => border.Background = normalBg;

            return border;
        }

        /// <summary>Updates the title TextBlock's strikethrough after a checkbox click.</summary>
        private static void UpdateTitleDecoration(Grid grid, bool isCompleted)
        {
            foreach (UIElement child in grid.Children)
            {
                if (child is StackPanel sp)
                {
                    foreach (UIElement spChild in sp.Children)
                    {
                        if (spChild is TextBlock tb && tb.Tag is string tag && tag == "titleTb")
                        {
                            tb.TextDecorations = isCompleted ? TextDecorations.Strikethrough : null;
                            return;
                        }
                    }
                }
            }
        }

        // ── Event handlers ────────────────────────────────────────────────────
        private void TaskFilterChanged(object sender, RoutedEventArgs e)
        {
            if (TasksPanel == null) return;
            if      (RbTToday?.IsChecked   == true) _filter = TaskFilter.Today;
            else if (RbTStarred?.IsChecked == true) _filter = TaskFilter.Starred;
            else if (RbTOverdue?.IsChecked == true) _filter = TaskFilter.Overdue;
            else                                     _filter = TaskFilter.All;
            RebuildRows();
        }

        private void DragHandle_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
                DragMove();
        }

        private void SldOpacity_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
            => ApplyOverlayOpacity(e.NewValue);

        private void ApplyOverlayOpacity(double v)
        {
            if (OuterBorder == null) return;
            byte a = (byte)Math.Round(v * 255);
            OuterBorder.Background = new SolidColorBrush(Color.FromArgb(a, 0x1A, 0x1B, 0x2E));
        }

        // ── Font size ─────────────────────────────────────────────────────────
        private void BtnFontDec_Click(object sender, RoutedEventArgs e)
        {
            _fontSize = Math.Max(8, _fontSize - 1);
            if (TxtOverlayFontSize != null) TxtOverlayFontSize.Text = _fontSize.ToString("0");
            UpdateRowFonts();
        }
        private void BtnFontInc_Click(object sender, RoutedEventArgs e)
        {
            _fontSize = Math.Min(48, _fontSize + 1);
            if (TxtOverlayFontSize != null) TxtOverlayFontSize.Text = _fontSize.ToString("0");
            UpdateRowFonts();
        }

        /// <summary>
        /// Updates font sizes in-place.  BuildRow tags the title TextBlock with
        /// Tag="titleTb" (uses <c>_fontSize</c>); everything else uses <c>_fontSize - 2</c>.
        /// </summary>
        private void UpdateRowFonts()
        {
            double fs    = _fontSize;
            double fsSub = Math.Max(8, fs - 2);

            foreach (UIElement child in TasksPanel.Children)
            {
                if (child is Border row && row.Child is Grid grid)
                {
                    foreach (UIElement el in grid.Children)
                    {
                        switch (el)
                        {
                            case TextBlock tb:
                                tb.FontSize = (string)tb.Tag == "titleTb" ? fs : fsSub;
                                break;
                            case StackPanel sp:
                                foreach (var sc in sp.Children.OfType<TextBlock>())
                                    sc.FontSize = (string)sc.Tag == "titleTb" ? fs : fsSub;
                                break;
                        }
                    }
                }
            }
        }

        // ── Resize via WM_NCHITTEST ───────────────────────────────────────────
        private IntPtr OverlayWndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == NativeMethods.WM_NCHITTEST)
            {
                int sx = (short)(lParam.ToInt32() & 0xFFFF);
                int sy = (short)((lParam.ToInt32() >> 16) & 0xFFFF);
                var pt = PointFromScreen(new Point(sx, sy));
                const int G = 10;
                double w = ActualWidth, h = ActualHeight;
                bool left   = pt.X < G;
                bool right  = pt.X > w - G;
                bool bottom = pt.Y > h - G;
                if      (bottom && left)  { handled = true; return (IntPtr)NativeMethods.HTBOTTOMLEFT; }
                else if (bottom && right) { handled = true; return (IntPtr)NativeMethods.HTBOTTOMRIGHT; }
                else if (bottom)          { handled = true; return (IntPtr)NativeMethods.HTBOTTOM; }
                else if (left)            { handled = true; return (IntPtr)NativeMethods.HTLEFT; }
                else if (right)           { handled = true; return (IntPtr)NativeMethods.HTRIGHT; }
            }
            return IntPtr.Zero;
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e) => Hide();

        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape) { Hide(); e.Handled = true; }
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (!_reallyClosing)
            {
                e.Cancel = true;
                Hide();
            }
        }

        // ── CloseForReal ──────────────────────────────────────────────────────
        public void CloseForReal()
        {
            var p = AppPrefs.Instance;
            p.TaskOverlayLeft     = Left;
            p.TaskOverlayTop      = Top;
            p.TaskOverlayWidth    = Width;
            p.TaskOverlayHeight   = Height;
            p.TaskOverlayOpacity  = SldOpacity.Value;
            p.TaskOverlayFontSize = _fontSize;

            if (_hwndSrc != null) { _hwndSrc.RemoveHook(OverlayWndProc); _hwndSrc = null; }
            _reallyClosing = true;
            Close();
        }
    }
}
