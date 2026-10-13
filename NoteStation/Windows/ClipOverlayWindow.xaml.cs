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
    public partial class ClipOverlayWindow : Window
    {
        // ── Events ────────────────────────────────────────────────────────────
        public event EventHandler<ClipboardItem> PasteRequested;
        public event EventHandler<ClipboardItem> SendToNoteRequested;

        // ── Filter / Search / Grouping ────────────────────────────────────────
        private enum ClipFilter { All, Pinned, Recent, Templates }
        private ClipFilter _filter = ClipFilter.Recent;
        private string _searchText = "";
        private bool _groupByApp = false;
        private IReadOnlyList<ClipboardItem> _lastClips = Array.Empty<ClipboardItem>();
        private IReadOnlyList<TemplateItem> _lastTemplates = Array.Empty<TemplateItem>();
        private bool _reallyClosing;
        private double _fontSize = 12;
        private HwndSource _hwndSrc;

        // ── Init ──────────────────────────────────────────────────────────────
        public ClipOverlayWindow()
        {
            InitializeComponent();
            // WCAG 2.1.3: borderless overlay — Alt+Arrow moves, Ctrl+Alt+Arrow resizes; the header's
            // right-click menu gets "Move/Resize with keyboard" (the drag band is mouse-only).
            OldGamers.Wpf.Shared.Controls.KeyboardWindowMover.Attach(this, DragHandle, allowResize: true);
            SourceInitialized += (_, __) =>
            {
                _hwndSrc = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
                _hwndSrc?.AddHook(OverlayWndProc);
            };
            // Restore saved position/size/opacity/font
            var p = AppPrefs.Instance;
            Width     = p.ClipOverlayWidth    > 0  ? p.ClipOverlayWidth    : 300;
            Height    = p.ClipOverlayHeight   > 0  ? p.ClipOverlayHeight   : 420;
            _fontSize = p.ClipOverlayFontSize >= 8 ? p.ClipOverlayFontSize : 24;
            if (TxtOverlayFontSize != null) TxtOverlayFontSize.Text = _fontSize.ToString("0");
            double savedOpacity = p.ClipOverlayOpacity > 0 ? p.ClipOverlayOpacity : 0.9;
            SldOpacity.Value = Math.Max(0.3, Math.Min(1.0, savedOpacity));
            ApplyOverlayOpacity(SldOpacity.Value);

            if (!double.IsNaN(p.ClipOverlayLeft) && !double.IsNaN(p.ClipOverlayTop))
            {
                Left = Math.Max(SystemParameters.VirtualScreenLeft,
                       Math.Min(p.ClipOverlayLeft,
                                SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth - 50));
                Top  = Math.Max(SystemParameters.VirtualScreenTop,
                       Math.Min(p.ClipOverlayTop,
                                SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight - 50));
            }
        }

        // ── Public API ────────────────────────────────────────────────────────
        /// <summary>Called by MainWindow whenever _clips changes.</summary>
        public void Refresh(IEnumerable<ClipboardItem> clips, IEnumerable<TemplateItem> templates = null)
        {
            _lastClips = clips.ToList();
            if (templates != null) _lastTemplates = templates.ToList();
            RebuildRows();
        }

        /// <summary>
        /// Focuses the search box and keyboard-selects the first clip row.
        /// Called by the Paste-from-Keyboard hotkey (Ctrl+Shift+V).
        /// </summary>
        public void FocusSearchAndSelectFirst()
        {
            TxtSearch.Focus();
            TxtSearch.SelectAll();
            // Select first non-header child
            var firstRow = ClipsPanel.Children.OfType<Border>()
                           .FirstOrDefault(b => b.Tag is ClipboardItem);
            if (firstRow != null)
            {
                firstRow.Focus();
                Keyboard.Focus(firstRow);
            }
        }

        // ── Row building ──────────────────────────────────────────────────────
        private void RebuildRows()
        {
            ClipsPanel.Children.Clear();

            if (_filter == ClipFilter.Templates)
            {
                foreach (var tmpl in _lastTemplates)
                {
                    var row = BuildTemplateRow(tmpl);
                    ClipsPanel.Children.Add(row);
                }
                if (_lastTemplates.Count == 0)
                    ClipsPanel.Children.Add(MakeSectionHeader("No templates — right-click a clip to save one"));
                return;
            }

            IEnumerable<ClipboardItem> source = _filter switch
            {
                ClipFilter.Pinned => _lastClips.Where(c => c.IsPinned),
                // Recent = transient clipboard feed only: unfiled, not hidden, newest first.
                _                 => _lastClips
                                        .Where(c => !c.HiddenFromRecent && !c.FolderId.HasValue)
                                        .OrderByDescending(c => c.CapturedAt)
                                        .Take(15),
            };
            // Never show soft-deleted clips, regardless of the active filter.
            source = source.Where(c => !c.IsDeleted);

            if (!string.IsNullOrWhiteSpace(_searchText))
            {
                var q = _searchText.Trim();
                source = source.Where(c =>
                    (c.Title   != null && c.Title.Contains(q,   StringComparison.OrdinalIgnoreCase)) ||
                    (c.Preview != null && c.Preview.Contains(q, StringComparison.OrdinalIgnoreCase)));
            }

            var filtered = source.ToList();

            if (_groupByApp && _filter == ClipFilter.All && !filtered.All(c => string.IsNullOrEmpty(c.SourceApp)))
            {
                var groups = filtered
                    .GroupBy(c => string.IsNullOrEmpty(c.SourceApp) ? "Unknown" : c.SourceApp)
                    .OrderBy(g => g.Key);
                foreach (var grp in groups)
                {
                    // Group header
                    var hdr = new TextBlock
                    {
                        Text       = grp.Key,
                        FontFamily = new FontFamily("Segoe UI"),
                        FontSize   = Math.Max(8, _fontSize - 2),
                        FontWeight = FontWeights.SemiBold,
                        Foreground = new SolidColorBrush(Color.FromRgb(0x89, 0xB4, 0xFA)),
                        Margin     = new Thickness(4, 6, 4, 2),
                        TextTrimming = TextTrimming.CharacterEllipsis,
                    };
                    ClipsPanel.Children.Add(hdr);
                    foreach (var clip in grp)
                        ClipsPanel.Children.Add(BuildRow(clip));
                }
            }
            else
            {
                // In All-filter view, split pinned vs unpinned with headers
                if (_filter == ClipFilter.All && filtered.Any(c => c.IsPinned))
                {
                    var pinned   = filtered.Where(c =>  c.IsPinned).ToList();
                    var unpinned = filtered.Where(c => !c.IsPinned).ToList();

                    // Pinned header
                    ClipsPanel.Children.Add(MakeSectionHeader("Pinned"));
                    foreach (var clip in pinned)
                        ClipsPanel.Children.Add(BuildRow(clip));

                    if (unpinned.Count > 0)
                    {
                        // Thin separator
                        ClipsPanel.Children.Add(new Border
                        {
                            Height          = 1,
                            Background      = new SolidColorBrush(Color.FromArgb(0x44, 0xA6, 0xAD, 0xC8)),
                            Margin          = new Thickness(4, 4, 4, 2),
                        });
                        ClipsPanel.Children.Add(MakeSectionHeader("Recent"));
                        foreach (var clip in unpinned)
                            ClipsPanel.Children.Add(BuildRow(clip));
                    }
                }
                else
                {
                    foreach (var clip in filtered)
                        ClipsPanel.Children.Add(BuildRow(clip));
                }
            }
        }

        private TextBlock MakeSectionHeader(string text) => new TextBlock
        {
            Text         = text,
            FontFamily   = new FontFamily("Segoe UI"),
            FontSize     = Math.Max(8, _fontSize - 2),
            FontWeight   = FontWeights.SemiBold,
            Foreground   = new SolidColorBrush(Color.FromRgb(0x89, 0xB4, 0xFA)),
            Margin       = new Thickness(4, 6, 4, 2),
            TextTrimming = TextTrimming.CharacterEllipsis,
        };

        private Border BuildTemplateRow(TemplateItem tmpl)
        {
            var row = new Border
            {
                Padding = new Thickness(8, 5, 8, 5),
                Cursor  = Cursors.Hand,
                Tag     = tmpl,
            };
            row.MouseLeftButtonDown += (_, e) =>
            {
                if (e.ClickCount == 1)
                {
                    PasteRequested?.Invoke(this, new ClipboardItem
                    {
                        Id      = -1,
                        Title   = tmpl.Title,
                        Text    = tmpl.Text,
                        Preview = tmpl.Text?.Length > 60 ? tmpl.Text[..60] + "…" : tmpl.Text ?? "",
                    });
                }
            };
            row.MouseEnter += (_, _) => row.Background = new SolidColorBrush(Color.FromArgb(0x22, 0x89, 0xB4, 0xFA));
            row.MouseLeave += (_, _) => row.Background = Brushes.Transparent;

            var sp = new StackPanel();
            sp.Children.Add(new TextBlock
            {
                Text         = "Template: " + tmpl.Title,
                FontSize     = _fontSize,
                Foreground   = new SolidColorBrush(Color.FromRgb(0xCD, 0xD6, 0xF4)),
                FontWeight   = FontWeights.SemiBold,
                TextTrimming = TextTrimming.CharacterEllipsis,
            });
            if (!string.IsNullOrEmpty(tmpl.Text))
                sp.Children.Add(new TextBlock
                {
                    Text         = tmpl.Text.Length > 80 ? tmpl.Text[..80] + "…" : tmpl.Text,
                    FontSize     = Math.Max(8, _fontSize - 2),
                    Foreground   = new SolidColorBrush(Color.FromRgb(0xA6, 0xAD, 0xC8)),
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    TextWrapping = TextWrapping.NoWrap,
                });
            row.Child = sp;
            return row;
        }

        private UIElement BuildRow(ClipboardItem clip)
        {
            // Row border
            var border = new Border
            {
                CornerRadius = new CornerRadius(5),
                Padding      = new Thickness(8, 6, 8, 6),
                Margin       = new Thickness(0, 1, 0, 1),
                Background   = new SolidColorBrush(Color.FromArgb(0x33, 0x45, 0x47, 0x5A)),
                Cursor       = Cursors.Hand,
            };
            border.SetValue(AutomationProperties.NameProperty,
                clip.IsImage ? "[Image] " + clip.Title : clip.Title ?? clip.Preview);

            // Grid: Auto (pin) | * (content) | Auto (age) | Auto (note btn)
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            double fs     = _fontSize;
            double fsSub  = Math.Max(8, fs - 2);

            // Col 0: pin badge
            if (clip.IsPinned)
            {
                var pin = JtIcons.Img("pin", Math.Max(12, fsSub + 2));
                pin.VerticalAlignment = VerticalAlignment.Top;
                pin.Margin            = new Thickness(0, 1, 4, 0);
                AutomationProperties.SetName(pin, "Pinned");
                Grid.SetColumn(pin, 0);
                grid.Children.Add(pin);
            }

            // Col 1: title + preview
            var contentStack = new StackPanel { Margin = new Thickness(0, 0, 6, 0) };

            if (clip.IsImage)
            {
                var imgLabel = new TextBlock
                {
                    Text       = "[Image]",
                    FontFamily = new FontFamily("Segoe UI"),
                    FontSize   = fs,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = new SolidColorBrush(Color.FromRgb(0xCD, 0xD6, 0xF4)),
                    TextTrimming = TextTrimming.CharacterEllipsis,
                };
                contentStack.Children.Add(imgLabel);

                if (!string.IsNullOrWhiteSpace(clip.Title))
                {
                    var sub = new TextBlock
                    {
                        Text       = clip.Title,
                        FontFamily = new FontFamily("Segoe UI"),
                        FontSize   = fsSub,
                        Foreground = new SolidColorBrush(Color.FromRgb(0xA6, 0xAD, 0xC8)),
                        TextTrimming = TextTrimming.CharacterEllipsis,
                    };
                    contentStack.Children.Add(sub);
                }
            }
            else
            {
                if (!string.IsNullOrWhiteSpace(clip.Title))
                {
                    var title = new TextBlock
                    {
                        Text       = clip.Title,
                        FontFamily = new FontFamily("Segoe UI"),
                        FontSize   = fs,
                        FontWeight = FontWeights.SemiBold,
                        Foreground = new SolidColorBrush(Color.FromRgb(0xCD, 0xD6, 0xF4)),
                        TextTrimming = TextTrimming.CharacterEllipsis,
                    };
                    contentStack.Children.Add(title);
                }

                var preview = new TextBlock
                {
                    FontFamily = new FontFamily("Segoe UI"),
                    FontSize   = fsSub,
                    Foreground = new SolidColorBrush(Color.FromRgb(0xA6, 0xAD, 0xC8)),
                    TextTrimming = TextTrimming.CharacterEllipsis,
                };
                foreach (var inline in RichTextFormatting.BuildPreviewInlines(clip.FormatSource, 120))
                    preview.Inlines.Add(inline);
                contentStack.Children.Add(preview);
            }

            Grid.SetColumn(contentStack, 1);
            grid.Children.Add(contentStack);

            // Col 2: relative age
            var time = new TextBlock
            {
                Text              = RelativeAge(clip.CapturedAt),
                FontFamily        = new FontFamily("Segoe UI"),
                FontSize          = fsSub,
                Foreground        = new SolidColorBrush(Color.FromRgb(0xBA, 0xC2, 0xDE)),   // ≥7:1 on the overlay (was #6C7186 ≈3:1)
                VerticalAlignment = VerticalAlignment.Top,
                Margin            = new Thickness(4, 1, 4, 0),
                ToolTip           = clip.CapturedAt.ToString("MMM d, HH:mm"),
            };
            Grid.SetColumn(time, 2);
            grid.Children.Add(time);

            // Col 3: send-to-note button
            var noteBtn = new Button
            {
                Content           = JtIcons.Img("stickyNote", 16),
                FontSize          = fsSub,
                Width             = 20,
                Height            = 20,
                Padding           = new Thickness(0),
                Background        = Brushes.Transparent,
                BorderThickness   = new Thickness(0),
                Cursor            = Cursors.Hand,
                VerticalAlignment = VerticalAlignment.Top,
                ToolTip           = "Send to Note",
            };
            noteBtn.SetValue(AutomationProperties.NameProperty, "Send to sticky note");
            noteBtn.Click += (_, e) =>
            {
                e.Handled = true;   // don't bubble to border's MouseLeftButtonUp
                SendToNoteRequested?.Invoke(this, clip);
            };
            Grid.SetColumn(noteBtn, 3);
            grid.Children.Add(noteBtn);

            border.Child = grid;

            // Hover highlight
            var normalColor = ((SolidColorBrush)border.Background).Color;
            border.MouseEnter  += (_, __) => border.Background = new SolidColorBrush(Color.FromArgb(0x55, 0x89, 0xB4, 0xFA));
            border.MouseLeave  += (_, __) => border.Background = new SolidColorBrush(normalColor);

            // Click → copy to clipboard (user pastes with Ctrl+V)
            border.MouseLeftButtonUp += (_, __) =>
            {
                PasteRequested?.Invoke(this, clip);
            };

            return border;
        }

        // ── Event handlers ────────────────────────────────────────────────────
        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            _searchText = (sender as TextBox)?.Text ?? "";
            RebuildRows();
        }

        /// <summary>On-state = 2px outline + UIA ItemStatus (was Opacity 0.5 when off → dimmed glyph, colour/opacity-only cue).</summary>
        private void UpdateGroupByAppVisual()
        {
            if (BtnGroupByApp == null) return;
            BtnGroupByApp.BorderThickness = new Thickness(_groupByApp ? 2 : 0);
            BtnGroupByApp.BorderBrush = _groupByApp ? new SolidColorBrush(Color.FromRgb(0xCD, 0xD6, 0xF4)) : Brushes.Transparent;
            System.Windows.Automation.AutomationProperties.SetItemStatus(BtnGroupByApp, _groupByApp ? "On" : "Off");
        }

        private void BtnGroupByApp_Click(object sender, RoutedEventArgs e)
        {
            _groupByApp = !_groupByApp;
            UpdateGroupByAppVisual();
            RebuildRows();
        }

        private void FilterChanged(object sender, RoutedEventArgs e)
        {
            if (ClipsPanel == null) return;
            _filter = RbPinned?.IsChecked == true ? ClipFilter.Pinned : ClipFilter.Recent;
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
            UpdateRowFonts();   // walk existing rows — no full rebuild needed
        }
        private void BtnFontInc_Click(object sender, RoutedEventArgs e)
        {
            _fontSize = Math.Min(48, _fontSize + 1);
            if (TxtOverlayFontSize != null) TxtOverlayFontSize.Text = _fontSize.ToString("0");
            UpdateRowFonts();
        }

        /// <summary>
        /// Updates TextBlock / Button font sizes in-place without tearing down and
        /// rebuilding all rows.  Called only for font-size changes; content changes
        /// still call RebuildRows().
        /// Convention from BuildRow: SemiBold TextBlocks use <c>_fontSize</c>;
        /// all other TextBlocks and Buttons use <c>_fontSize - 2</c>.
        /// </summary>
        private void UpdateRowFonts()
        {
            double fs    = _fontSize;
            double fsSub = Math.Max(8, fs - 2);

            foreach (UIElement child in ClipsPanel.Children)
            {
                if (child is TextBlock hdr)
                {
                    // Section / group header TextBlocks
                    hdr.FontSize = fsSub;
                }
                else if (child is Border row && row.Child is Grid grid)
                {
                    ApplyFontSizesToGrid(grid, fs, fsSub);
                }
            }
        }

        private static void ApplyFontSizesToGrid(Grid grid, double fs, double fsSub)
        {
            foreach (UIElement el in grid.Children)
            {
                switch (el)
                {
                    case TextBlock tb:
                        tb.FontSize = tb.FontWeight == FontWeights.SemiBold ? fs : fsSub;
                        break;
                    case StackPanel sp:
                        foreach (var sc in sp.Children.OfType<TextBlock>())
                            sc.FontSize = sc.FontWeight == FontWeights.SemiBold ? fs : fsSub;
                        break;
                    case Button btn:
                        btn.FontSize = fsSub;
                        break;
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

        // ── Relative age ──────────────────────────────────────────────────────
        private static string RelativeAge(DateTime dt)
        {
            var diff = DateTime.Now - dt;
            if (diff.TotalSeconds < 60)  return "just now";
            if (diff.TotalMinutes < 60)  return $"{(int)diff.TotalMinutes}m ago";
            if (diff.TotalHours   < 24)  return $"{(int)diff.TotalHours}h ago";
            if (diff.TotalDays    < 2)   return "Yesterday";
            if (diff.TotalDays    < 7)   return $"{(int)diff.TotalDays}d ago";
            return dt.ToString("MMM d");
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
            // Save geometry to prefs
            var p = AppPrefs.Instance;
            p.ClipOverlayLeft     = Left;
            p.ClipOverlayTop      = Top;
            p.ClipOverlayWidth    = Width;
            p.ClipOverlayHeight   = Height;
            p.ClipOverlayOpacity  = SldOpacity.Value;
            p.ClipOverlayFontSize = _fontSize;

            if (_hwndSrc != null) { _hwndSrc.RemoveHook(OverlayWndProc); _hwndSrc = null; }
            _reallyClosing = true;
            Close();
        }
    }
}
