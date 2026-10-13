using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Shell;
using NoteStation.Windows;

namespace NoteStation
{
    /// <summary>
    /// Floating sticky note overlay window.
    /// Each note has its own window instance, fully independent appearance.
    /// </summary>
    public partial class StickyNoteWindow : Window
    {
        public StickyNote Note { get; }

        public event EventHandler<StickyNote> NoteChanged;
        public event EventHandler<StickyNote> NoteDeleteRequested;

        private bool _suppressEvents;
        private bool _reallyClosing;

        private StickyNoteFormatDialog _formatDialog;
        private System.Windows.Controls.MenuItem _pinMenuItem;
        private System.Windows.Threading.DispatcherTimer _undoToastTimer;
        private System.Windows.Threading.DispatcherTimer _positionSaveTimer;
        private int _lastContentLength;
        private HwndSource _hwndSource;
        private bool _ruleLinesActive;

        public StickyNoteWindow(StickyNote note)
        {
            InitializeComponent();

            // 2026-09-09: Escape/F2/F3 are bound in XAML to SystemCommands.*, but a
            // borderless (WindowStyle="None") window gets no automatic CommandBindings
            // for those routed commands — without these, the KeyBindings silently do
            // nothing (this is why F3/"restore" reportedly did not un-maximize a note;
            // the manual WindowState toggle on title-bar double-click worked because it
            // doesn't go through SystemCommands at all).
            CommandBindings.Add(new CommandBinding(SystemCommands.CloseWindowCommand,
                (_, __) => SystemCommands.CloseWindow(this)));
            CommandBindings.Add(new CommandBinding(SystemCommands.MinimizeWindowCommand,
                (_, __) => SystemCommands.MinimizeWindow(this)));
            CommandBindings.Add(new CommandBinding(SystemCommands.RestoreWindowCommand,
                (_, __) => SystemCommands.RestoreWindow(this)));

            Note = note;
            // Ruled texture lines are measured from real rendered text, so they must
            // be redrawn whenever the content scrolls, resizes, or reflows.
            ContentBox.AddHandler(ScrollViewer.ScrollChangedEvent, new ScrollChangedEventHandler(ContentBox_ScrollChanged));
            ContentBox.SizeChanged += (_, __) => RedrawRuleLines();
            _suppressEvents = true;
            ApplyNoteToUI();
            _suppressEvents = false;

            // Cache pin menu item for dynamic header updates
            _pinMenuItem = RootBorder.ContextMenu?.Items.OfType<System.Windows.Controls.MenuItem>()
                .FirstOrDefault(m => m.Name == "CmdPinItem");
            UpdatePinMenuItem();

            // WCAG 2.1.3: borderless note — Alt+Arrow moves, Ctrl+Alt+Arrow resizes, and the
            // right-click menu gains "Move/Resize with keyboard" (title bar / grips are mouse-only).
            OldGamers.Wpf.Shared.Controls.KeyboardWindowMover.Attach(this, RootBorder, allowResize: true);
        }

        // ── Apply note model → UI ─────────────────────────────────────────────
        private void ApplyNoteToUI()
        {
            Title       = Note.Title ?? "Note";
            TitleBox.Text = Note.Title ?? "Note";
            TitleBox.FontSize = Note.TitleFontSize > 0 ? Note.TitleFontSize : 14;
            LoadContentToBox();

            // Position/size
            Width  = Note.Width  > 0 ? Note.Width  : 300;
            Height = Note.Height > 0 ? Note.Height : 200;
            if (!double.IsNaN(Note.Left) && !double.IsNaN(Note.Top))
            {
                // Clamp to virtual screen so notes don't open off-screen after
                // a monitor is disconnected or resolution changes.
                double vl = SystemParameters.VirtualScreenLeft;
                double vt = SystemParameters.VirtualScreenTop;
                double vr = vl + SystemParameters.VirtualScreenWidth;
                double vb = vt + SystemParameters.VirtualScreenHeight;
                double minVisible = 60; // px of window that must remain on-screen
                Left = Math.Max(vl, Math.Min(Note.Left, vr - minVisible));
                Top  = Math.Max(vt, Math.Min(Note.Top,  vb - minVisible));
            }
            else
            { WindowStartupLocation = WindowStartupLocation.CenterScreen; }

            Topmost = Note.IsTopmost;
            double op = Note.NoteOpacity > 0 ? Note.NoteOpacity : 1.0;
            Opacity = op;
            if (SldOpacity != null) { _suppressEvents = true; SldOpacity.Value = op; _suppressEvents = false; }
            ApplyColors();
            ApplyFont();
            // F15: Apply texture
            ApplyNoteTexture(Note?.NoteTexture ?? "");
            ApplyTitleBarVisibility();
            ApplyFoldState();
        }

        // ── Per-note title bar show/hide ──────────────────────────────────────
        private void ApplyTitleBarVisibility()
        {
            bool hidden = Note?.HideTitleBar ?? false;
            TitleBar.Visibility = hidden ? Visibility.Collapsed : Visibility.Visible;
            if (MoveGrip != null) MoveGrip.Visibility = hidden ? Visibility.Visible : Visibility.Collapsed;
            if (CmdHideTitleBar != null) CmdHideTitleBar.IsChecked = hidden;
            if (CmdShowOnStartup != null) CmdShowOnStartup.IsChecked = Note?.ShowOnStartup ?? false;
            if (CmdShowInSidebar != null) CmdShowInSidebar.IsChecked = Note?.ShowInSidebar ?? false;
        }

        private void MoveGrip_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 1) SafeDragMove();
            e.Handled = true; // don't let RootBorder also try to drag
        }

        private void CmdHideTitleBar_Click(object sender, RoutedEventArgs e)
        {
            Note.HideTitleBar = CmdHideTitleBar.IsChecked;
            ApplyTitleBarVisibility();
            RaiseNoteChanged();
        }

        private void CmdShowOnStartup_Click(object sender, RoutedEventArgs e)
        {
            Note.ShowOnStartup = CmdShowOnStartup.IsChecked;
            RaiseNoteChanged();
        }

        private void CmdShowInSidebar_Click(object sender, RoutedEventArgs e)
        {
            Note.ShowInSidebar = CmdShowInSidebar.IsChecked;
            RaiseNoteChanged();
        }

        private Color? _appliedTextColor;

        private void ApplyColors()
        {
            if (ColorFromHex(Note.BackgroundColor) is Color bg)
                BgBrush.Color = bg;

            // Title bar: darkened version of BG
            TitleBarBrush.Color  = DarkenColor(BgBrush.Color, 0.25);

            if (ColorFromHex(Note.TextColor) is Color fg)
            {
                // Text typed while the old colour was applied may carry it as a local value — clear it
                // so the whole note follows the new Text colour.
                if (_appliedTextColor is Color prev && prev != fg)
                    RichTextFormatting.ClearLocalForeground(ContentBox.Document, prev);
                _appliedTextColor = fg;

                var fgBrush = new SolidColorBrush(fg);
                TitleBox.Foreground   = fgBrush;
                ContentBox.Foreground = fgBrush;
                // Word count + resize glyph sit on the user's note colour: use the note text colour
                // (was 40%-alpha grey #66A6ADC8 / #66AAAAAA → < 3:1 on light notes).
                TxtWordCount.Foreground    = fgBrush;
                ResizeGripGlyph.Fill       = fgBrush;
            }

            UpdateQuickColorSelection();
        }

        // ── Quick color bar — 5 one-click presets, footer of the note ──────────
        private void QuickColor_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement btn || btn.Tag is not string bg) return;
            Note.BackgroundColor = bg;
            Note.TextColor       = bg == "#2A2A2A" ? "#FFFFFF" : "#1C1C1C";
            ApplyColors();
            RaiseNoteChanged();
        }

        /// <summary>Checks the swatch matching the note's current background color, if any
        /// (the note may be on a color from the full Format dialog that isn't one of the 5 — then
        /// none is checked). Setting IsChecked in code raises no Click, so this never re-applies.</summary>
        private void UpdateQuickColorSelection()
        {
            if (BtnQC1 == null) return; // not yet created during early ctor calls
            foreach (var rb in new[] { BtnQC1, BtnQC2, BtnQC3, BtnQC4, BtnQC5 })
                rb.IsChecked = string.Equals(Note.BackgroundColor, rb.Tag as string, StringComparison.OrdinalIgnoreCase);
        }

        private void ApplyFont()
        {
            var ff = TryGetFontFamily(Note.FontFamily) ?? new FontFamily("Segoe UI");
            double fs = Note.FontSize > 4 ? Note.FontSize : 14;
            var fw = Note.FontWeight == "Bold" ? FontWeights.Bold : FontWeights.Normal;
            var fst = Note.FontStyle == "Italic" ? FontStyles.Italic : FontStyles.Normal;

            // Set FlowDocument defaults
            ContentBox.FontFamily = ff;
            ContentBox.FontSize   = fs;
            ContentBox.FontWeight = fw;
            ContentBox.FontStyle  = fst;

            // Apply font size + family to ALL existing inline content so the change
            // is visible on text that was typed before the setting was changed.
            bool prev = _suppressEvents;
            _suppressEvents = true;
            try
            {
                var all = new TextRange(ContentBox.Document.ContentStart, ContentBox.Document.ContentEnd);
                all.ApplyPropertyValue(TextElement.FontSizeProperty, fs);
                all.ApplyPropertyValue(TextElement.FontFamilyProperty, ff);
            }
            finally { _suppressEvents = prev; }

            ApplyLineRhythm();
            RedrawRuleLines();
        }

        /// <summary>Force every content line to an exact, deterministic height so the
        /// ruled-paper texture lines up with the text rows. Applies to existing
        /// paragraphs and (via the implicit style) any newly typed ones.</summary>
        private void ApplyLineRhythm()
        {
            double L = ComputeContentLineHeight();

            // No internal page inset — the only top offset is the control Padding,
            // which the texture overlay is shifted to match.
            ContentBox.Document.PagePadding = new Thickness(0);

            // Implicit style → future paragraphs get the same rhythm.
            var pStyle = new Style(typeof(Paragraph));
            pStyle.Setters.Add(new Setter(Block.MarginProperty, new Thickness(0)));
            pStyle.Setters.Add(new Setter(Block.LineHeightProperty, L));
            pStyle.Setters.Add(new Setter(Block.LineStackingStrategyProperty, LineStackingStrategy.BlockLineHeight));
            ContentBox.Resources[typeof(Paragraph)] = pStyle;

            // Existing paragraphs — set directly (implicit style isn't retroactive).
            foreach (var block in ContentBox.Document.Blocks)
            {
                block.LineHeight = L;
                block.LineStackingStrategy = LineStackingStrategy.BlockLineHeight;
            }
        }

        /// <summary>Deterministic line-box height for the note's current font.
        /// Uses the font's own recommended line spacing so glyphs never clip.</summary>
        private double ComputeContentLineHeight()
        {
            double fs = Note.FontSize > 4 ? Note.FontSize : 14;
            var ff = TryGetFontFamily(Note.FontFamily) ?? new FontFamily("Segoe UI");
            double factor = ff.LineSpacing > 0 ? ff.LineSpacing : 1.3;
            return Math.Ceiling(fs * factor);
        }

        // ── Loaded ────────────────────────────────────────────────────────────
        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            // Allow NcHitTest for resize grip
            _hwndSource = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
            _hwndSource?.AddHook(WndProc);
            _lastContentLength = new TextRange(ContentBox.Document.ContentStart, ContentBox.Document.ContentEnd).Text.Length;

            // Apply global UI scale to chrome + toolbar
            double scale = AppPrefs.Instance.UiScale > 0 ? AppPrefs.Instance.UiScale : 1.0;
            NoteScale.ScaleX = scale;
            NoteScale.ScaleY = scale;

            ApplyLockState();
            StartAutoBlurTimer();   // start if not locked + AutoBlurMinutes > 0

            // First layout pass at construction time can leave ContentBox unmeasured
            // (ActualHeight/Width still 0), which would draw rules at the wrong spots
            // or skip them entirely — redraw once real layout is available.
            Dispatcher.BeginInvoke(new Action(RedrawRuleLines), System.Windows.Threading.DispatcherPriority.Loaded);
        }

        /// <summary>Called by MainWindow when user changes global UI scale.</summary>
        public void UpdateScale(double scale)
        {
            NoteScale.ScaleX = scale;
            NoteScale.ScaleY = scale;
            RedrawRuleLines();
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
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

        // ── Drag (title bar) ──────────────────────────────────────────────────
        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2)
            {
                WindowState = WindowState == WindowState.Maximized
                    ? WindowState.Normal
                    : WindowState.Maximized;
                e.Handled = true;
                return;
            }
            if (e.ClickCount == 1) SafeDragMove();
        }

        private void RootBorder_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            // Title bar hidden = no drag handle, so allow dragging from the note body.
            if ((Note?.HideTitleBar ?? false) && e.ClickCount == 1)
                SafeDragMove();
        }

        // DragMove throws if the left button is no longer down (e.g. event re-raised
        // by a child control after release). Guard the state and swallow the race.
        private void SafeDragMove()
        {
            if (Mouse.LeftButton != MouseButtonState.Pressed) return;
            try { DragMove(); }
            catch (InvalidOperationException) { }
        }

        // ── Right-click on content area — show RootBorder context menu ────────
        private void ContentBox_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (RootBorder.ContextMenu != null)
            {
                RootBorder.ContextMenu.PlacementTarget = RootBorder;
                RootBorder.ContextMenu.IsOpen = true;
                e.Handled = true;
            }
        }

        // ── Resize grip ───────────────────────────────────────────────────────
        private void ResizeGrip_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            const int WM_SYSCOMMAND    = 0x0112;
            const int SC_SIZE_BOTTOMRIGHT = 0xF008;
            var hwnd = new WindowInteropHelper(this).Handle;
            try { PostMessage(hwnd, WM_SYSCOMMAND, (IntPtr)SC_SIZE_BOTTOMRIGHT, IntPtr.Zero); }
            catch { }
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool PostMessage(IntPtr hWnd, int Msg, IntPtr wParam, IntPtr lParam);

        // ── Location / size tracking ──────────────────────────────────────────
        private void Window_LocationChanged(object sender, EventArgs e)
        {
            Note.Left = Left;
            Note.Top  = Top;
            SchedulePositionSave();
        }

        private void Window_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            // While locked the window is force-shrunk to the lock-prompt size
            // (see ApplyLockState) — that size must never overwrite the real,
            // pre-lock size we restore to on unlock. Also ignore events that
            // fire mid-transition while ApplyLockState is still assigning
            // Width/Height (see _applyingLockSize) — those carry a half-updated
            // size and would corrupt Note.Width/Height the moment IsLocked
            // flips to false.
            // Also ignore while maximized (Aero-snap or the title-bar double-click
            // toggle below) — that size is the full monitor, not a size worth
            // persisting or restoring to. Window_StateChanged owns bounds capture
            // for the maximize/restore cycle instead.
            if (Note.IsLocked || _applyingLockSize || Note.IsFolded || _applyingFoldSize || WindowState == WindowState.Maximized) return;
            Note.Width  = ActualWidth;
            Note.Height = ActualHeight;
            SchedulePositionSave();
        }

        // ── Maximize / restore bounds ───────────────────────────────────────────
        // WindowStyle="None" + AllowsTransparency means there's no native caption,
        // but DragMove() still participates in Windows' Aero-Snap-to-top gesture,
        // which silently flips WindowState to Maximized. With no restore button
        // anywhere on the note, that used to be a one-way trip — Window_SizeChanged
        // would also stomp Note.Width/Height with the full-screen size, so even
        // toggling WindowState back to Normal by other means restored to a bad size.
        // Capture bounds ourselves on the way into Maximized and reapply them on
        // the way out, regardless of what triggered the state change (Aero-snap,
        // the F3 key binding, or the title-bar double-click toggle above).
        private double _restoreLeft, _restoreTop, _restoreWidth, _restoreHeight;
        private bool _hasRestoreBounds;

        private void Window_StateChanged(object sender, EventArgs e)
        {
            if (WindowState == WindowState.Maximized)
            {
                if (!_hasRestoreBounds)
                {
                    _restoreLeft   = Left;
                    _restoreTop    = Top;
                    _restoreWidth  = Note.Width  > 0 ? Note.Width  : ActualWidth;
                    _restoreHeight = Note.Height > 0 ? Note.Height : ActualHeight;
                    _hasRestoreBounds = true;
                }
            }
            else if (WindowState == WindowState.Normal && _hasRestoreBounds)
            {
                _hasRestoreBounds = false;
                Left   = _restoreLeft;
                Top    = _restoreTop;
                Width  = _restoreWidth;
                Height = _restoreHeight;
            }
        }

        private void SchedulePositionSave()
        {
            if (_positionSaveTimer == null)
            {
                _positionSaveTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
                _positionSaveTimer.Tick += (_, __) => { _positionSaveTimer.Stop(); RaiseNoteChanged(); };
            }
            _positionSaveTimer.Stop();
            _positionSaveTimer.Start();
        }

        // ── Title / Content editing ───────────────────────────────────────────
        private void TitleBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_suppressEvents) return;
            Note.Title = TitleBox.Text;
            Title = Note.Title;
            Note.ModifiedAt = DateTime.Now;
            RaiseNoteChanged();
        }

        private void ContentBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_suppressEvents) return;
            string rawText = new TextRange(ContentBox.Document.ContentStart, ContentBox.Document.ContentEnd).Text;
            int newLen = rawText.Length;
            if (newLen < _lastContentLength - 5)
                ShowNoteUndoToast();
            _lastContentLength = newLen;
            UpdateWordCount(rawText);
            SaveContentFromBox();
            Note.ModifiedAt = DateTime.Now;
            RaiseNoteChanged();
            RedrawRuleLines();
        }

        private void ContentBox_ScrollChanged(object sender, ScrollChangedEventArgs e) => RedrawRuleLines();

        private void UpdateWordCount(string text)
        {
            if (TxtWordCount == null) return;
            int words = string.IsNullOrWhiteSpace(text)
                ? 0
                : text.Split(new[] { ' ', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries).Length;
            int chars = text.Replace("\r", "").Replace("\n", "").Length;
            TxtWordCount.Text = $"{words}w {chars}c";
            System.Windows.Automation.AutomationProperties.SetName(TxtWordCount, $"{words} words, {chars} characters");
        }

        // Undo toast (WCAG 2.2.1/2.2.3/4.1.3): lifetime from Settings (0 = until dismissed),
        // 1 s ticks that skip while the toast is hovered or holds keyboard focus, UIA notification on show.
        private int  _undoToastRemaining;
        private bool _undoToastHeld;

        private void ShowNoteUndoToast()
        {
            bool wasVisible = NoteUndoToast.Visibility == Visibility.Visible;
            NoteUndoToast.Visibility = Visibility.Visible;
            _undoToastTimer?.Stop();
            _undoToastRemaining = AppPrefs.Instance.UndoToastSeconds;
            if (!wasVisible)
                NoteStation.Controls.StatusAnnouncer.Announce("Text deleted. Undo available, Ctrl+Z.", NoteUndoToast, important: true);
            if (_undoToastRemaining <= 0) return;   // until dismissed
            _undoToastTimer ??= CreateUndoToastTimer();
            _undoToastTimer.Start();
        }

        private System.Windows.Threading.DispatcherTimer CreateUndoToastTimer()
        {
            var t = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            t.Tick += (_, __) =>
            {
                if (_undoToastHeld) return;
                if (--_undoToastRemaining > 0) return;
                t.Stop();
                NoteUndoToast.Visibility = Visibility.Collapsed;
            };
            return t;
        }

        private void NoteUndoToast_HoldChanged(object sender, MouseEventArgs e)
            => _undoToastHeld = NoteUndoToast.IsMouseOver || NoteUndoToast.IsKeyboardFocusWithin;
        private void NoteUndoToast_FocusChanged(object sender, DependencyPropertyChangedEventArgs e)
            => _undoToastHeld = NoteUndoToast.IsMouseOver || NoteUndoToast.IsKeyboardFocusWithin;

        private void BtnNoteUndoDismiss_Click(object sender, RoutedEventArgs e)
        {
            _undoToastTimer?.Stop();
            NoteUndoToast.Visibility = Visibility.Collapsed;
            ContentBox.Focus();
        }

        private void BtnNoteUndo_Click(object sender, RoutedEventArgs e)
        {
            _undoToastTimer?.Stop();
            NoteUndoToast.Visibility = Visibility.Collapsed;
            ContentBox.Undo();
        }

        private void SldOpacity_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (Note == null || _suppressEvents) return;
            double op = Math.Round(e.NewValue, 2);
            Note.NoteOpacity = op;
            Opacity = op;
            RaiseNoteChanged();
        }

        // ── Context menu handlers ─────────────────────────────────────────────
        private void CmdFormat_Click(object sender, RoutedEventArgs e) => OpenFormatDialog();

        private void CmdPin_Click(object sender, RoutedEventArgs e)
        {
            Topmost = !Topmost;
            Note.IsTopmost = Topmost;
            UpdatePinMenuItem();
            RaiseNoteChanged();
        }

        private void CmdDelete_Click(object sender, RoutedEventArgs e)
        {
            var res = AppDialog.Show(
                $"Delete note \"{Note.Title}\"?\nThis cannot be undone.",
                "NoteStation — Delete Note",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);
            if (res != MessageBoxResult.Yes) return;
            NoteDeleteRequested?.Invoke(this, Note);
            Close();
        }

        private void UpdatePinMenuItem()
        {
            if (_pinMenuItem == null) return;
            _pinMenuItem.Header = Topmost ? "Unpin (allow behind other windows)" : "Pin on top";
        }

        // ── Close button: hide (not delete) ──────────────────────────────────
        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            Note.IsVisible = false;
            RaiseNoteChanged();
            Hide();
        }

        // ── Fold button: collapse to title bar only ───────────────────────────
        private void BtnFold_Click(object sender, RoutedEventArgs e)
        {
            Note.IsFolded = !Note.IsFolded;
            ApplyFoldState();
            RaiseNoteChanged();
        }

        // ── Format dialog ─────────────────────────────────────────────────────
        private void OpenFormatDialog()
        {
            if (_formatDialog != null && _formatDialog.IsVisible) { _formatDialog.Activate(); return; }
            _formatDialog = new StickyNoteFormatDialog(Note) { Owner = this };
            _formatDialog.NoteAppearanceChanged += (s, _) =>
            {
                ApplyColors();
                ApplyFont();
                RefreshTexture(); // keep texture stroke contrast in sync with background
                TitleBox.FontSize = Note.TitleFontSize > 0 ? Note.TitleFontSize : 14;
                Opacity = Note.NoteOpacity > 0 ? Note.NoteOpacity : 1.0;
                RaiseNoteChanged();
            };
            _formatDialog.Show();
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (_reallyClosing)
                return; // CloseForReal() is in progress — allow it through

            // Alt+F4 or window manager close: check for unsaved changes
            string currentContent = new TextRange(
                ContentBox.Document.ContentStart,
                ContentBox.Document.ContentEnd).Text;
            bool isDirty = currentContent != (Note.Content ?? "");

            // Also treat unsaved as dirty when Note.Content is stored as XAML
            // and the plain-text round-trip differs — simplest heuristic: compare trimmed
            if (!isDirty && !string.IsNullOrWhiteSpace(Note.Content)
                && Note.Content.TrimStart().StartsWith("<"))
            {
                // XAML stored — we can't diff cheaply; skip the confirm on XAML notes
                // (they auto-save on every keystroke via ContentBox_TextChanged)
                isDirty = false;
            }

            if (isDirty)
            {
                var res = AppDialog.Show(
                    "Close this note? Unsaved changes will be lost.",
                    "NoteStation — Close Note",
                    MessageBoxButton.OKCancel,
                    MessageBoxImage.Question);
                if (res == MessageBoxResult.Cancel)
                {
                    e.Cancel = true;
                    return;
                }
            }

            // Hide instead of close — parent (MainWindow) manages lifetime
            e.Cancel = true;
            Note.IsVisible = false;
            RaiseNoteChanged();
            Hide();
        }

        /// <summary>Called by the parent (MainWindow) when the note should truly be destroyed.</summary>
        public void CloseForReal()
        {
            _reallyClosing = true;
            _autoBlurTimer?.Stop();
            _undoToastTimer?.Stop();
            _positionSaveTimer?.Stop();
            _hwndSource?.RemoveHook(WndProc);
            Close();
        }

        // ── Rich text helpers ─────────────────────────────────────────────────
        private void LoadContentToBox()
        {
            bool prev = _suppressEvents;
            _suppressEvents = true;
            try
            {
                ContentBox.Document.Blocks.Clear();
                var content = Note.Content ?? "";
                if (!string.IsNullOrWhiteSpace(content) && content.TrimStart().StartsWith("<"))
                {
                    try
                    {
                        using var ms = new MemoryStream(Encoding.UTF8.GetBytes(content));
                        var range = new TextRange(ContentBox.Document.ContentStart, ContentBox.Document.ContentEnd);
                        range.Load(ms, DataFormats.Xaml);
                        // Bug fix: the save-time default colour came back as local Foreground on every
                        // run and outranked ContentBox.Foreground, so Text colour never changed after the
                        // first save. Clear it (user-coloured runs keep their colour).
                        if (RichTextFormatting.SavedDefaultForeground(content) is Color savedDefault)
                            RichTextFormatting.ClearLocalForeground(ContentBox.Document, savedDefault);
                        return;
                    }
                    catch { /* fall through to plain text */ }
                }
                // Plain text (or XAML parse failed)
                ContentBox.Document.Blocks.Add(new Paragraph(new Run(content)));
            }
            finally { _suppressEvents = prev; }
        }

        private void SaveContentFromBox()
        {
            try
            {
                using var ms = new MemoryStream();
                var range = new TextRange(ContentBox.Document.ContentStart, ContentBox.Document.ContentEnd);
                range.Save(ms, DataFormats.Xaml);
                Note.Content = Encoding.UTF8.GetString(ms.ToArray());
            }
            catch { Note.Content = new TextRange(ContentBox.Document.ContentStart, ContentBox.Document.ContentEnd).Text; }
        }

        // ── Helpers ───────────────────────────────────────────────────────────
        private void RaiseNoteChanged() => NoteChanged?.Invoke(this, Note);

        private static Color? ColorFromHex(string hex)
        {
            try { return (Color)ColorConverter.ConvertFromString(hex); }
            catch { return null; }
        }

        private static Color DarkenColor(Color c, double factor)
        {
            return Color.FromArgb(c.A,
                (byte)(c.R * (1 - factor)),
                (byte)(c.G * (1 - factor)),
                (byte)(c.B * (1 - factor)));
        }

        private static FontFamily TryGetFontFamily(string name)
        {
            try { return new FontFamily(name); }
            catch { return null; }
        }

        // ── PIN lock (2026-09-07: salted PBKDF2 via PinHasher, was unsalted SHA-256) ──

        private System.Windows.Threading.DispatcherTimer _autoBlurTimer;

        // ── F-lock-shrink: collapse note to just the lock prompt while locked ──
        // Fits the hide-note prompt: 44px PIN box + 44px button + wrapped error/throttle line.
        private const double LockedWidth  = 260;
        private const double LockedHeight = 330;
        private bool _lockSizeApplied;

        private const double FoldedHeight = 56;
        private bool _foldSizeApplied;
        private bool _applyingFoldSize;

        /// <summary>Apply or remove the folded visual state to match Note.IsFolded — collapses
        /// the content row to 0 height and shrink-locks the window to the title bar only,
        /// mirroring the ApplyLockState size-restore pattern below.</summary>
        public void ApplyFoldState()
        {
            // Lock state owns sizing/visibility while a note is locked (the PIN prompt
            // must stay visible and correctly sized) — suspend fold visuals until unlock,
            // then ApplyLockState() re-syncs us via the call at its end.
            if (Note.IsLocked)
            {
                _foldSizeApplied = false;
                MinHeight = 100;
                MaxHeight = double.PositiveInfinity;
                NoteRootGrid.RowDefinitions[1].Height = new GridLength(1, GridUnitType.Star);
                return;
            }

            bool folded = Note.IsFolded;
            NoteRootGrid.RowDefinitions[1].Height = folded ? new GridLength(0) : new GridLength(1, GridUnitType.Star);

            if (folded && !_foldSizeApplied)
            {
                _foldSizeApplied = true;
                _applyingFoldSize = true;
                MinHeight = FoldedHeight;
                MaxHeight = FoldedHeight;
                Height    = FoldedHeight;
                _applyingFoldSize = false;
            }
            else if (!folded && _foldSizeApplied)
            {
                _foldSizeApplied = false;
                _applyingFoldSize = true;
                MinHeight = 100;
                MaxHeight = double.PositiveInfinity;
                Height = Note.Height > FoldedHeight + 20 ? Note.Height : 220;
                UpdateLayout();
                _applyingFoldSize = false;
            }

            if (BtnFold != null)
            {
                BtnFold.Content = folded ? "▸" : "▾";
                BtnFold.ToolTip = folded ? "Unfold note" : "Fold note (show title only)";
            }
        }

        // Set for the duration of the Width/Height pair-assignment below.
        // Setting Width and Height as two separate statements on a top-level
        // Window can trigger an intermediate SizeChanged between them (the
        // HWND resize for Width can be processed before the Height statement
        // even runs). Window_SizeChanged only checked Note.IsLocked, which is
        // already flipped to false by the time this method runs on unlock —
        // so that intermediate, half-updated event slipped past the guard and
        // stomped Note.Height (or Width) with the still-old locked value. This
        // flag closes that window: Window_SizeChanged ignores everything until
        // both assignments below are done.
        private bool _applyingLockSize;

        /// <summary>Apply or remove the locked visual state to match Note.IsLocked.</summary>
        public void ApplyLockState()
        {
            bool locked = Note.IsLocked;
            LockOverlay.Visibility  = locked ? Visibility.Visible : Visibility.Collapsed;
            ContentBlur.Radius      = locked ? 15 : 0;
            ContentBox.IsEnabled    = !locked;
            PinErrorText.Visibility = Visibility.Collapsed;
            if (PinBox != null) PinBox.Clear();

            if (locked) _autoBlurTimer?.Stop();
            if (locked && IsThrottled(out _)) ShowThrottleState();

            // Shrink to the lock-prompt size while locked, then restore the size
            // the note had before locking. Note.Width/Height are never touched by
            // Window_SizeChanged while IsLocked is true, so they still hold the
            // real pre-lock size here — including across an app restart while a
            // note stays locked, since they're persisted on the model.
            if (locked && !_lockSizeApplied)
            {
                _lockSizeApplied = true;
                _applyingLockSize = true;
                Width  = LockedWidth;
                Height = LockedHeight;
                _applyingLockSize = false;
            }
            else if (!locked && _lockSizeApplied)
            {
                _lockSizeApplied = false;
                // Never trust a saved size at/near the locked-prompt footprint —
                // that would mean Note.Width/Height got corrupted to the shrunk
                // size by an earlier build, and "restoring" it would just
                // re-apply the shrink. Fall back to the normal default instead.
                double targetWidth  = Note.Width  > LockedWidth  + 20 ? Note.Width  : 300;
                double targetHeight = Note.Height > LockedHeight + 20 ? Note.Height : 200;
                _applyingLockSize = true;
                Width  = targetWidth;
                Height = targetHeight;
                // Force the resize through immediately — on a borderless,
                // AllowsTransparency window the HWND resize can otherwise lag
                // behind the property assignment until the next layout pass.
                UpdateLayout();
                _applyingLockSize = false;
            }

            ApplyFoldState();

            // Update context menu
            bool hasPin = !string.IsNullOrEmpty(Note.PinHash);
            if (CmdLockItem      != null) CmdLockItem.IsEnabled      = hasPin && !locked;
            if (CmdRemovePinItem != null) CmdRemovePinItem.IsEnabled  = hasPin;
            if (CmdExcludeAutoBlur != null) CmdExcludeAutoBlur.IsChecked = Note.ExcludeFromAutoBlur;
        }

        private void StartAutoBlurTimer()
        {
            _autoBlurTimer?.Stop();
            if (Note.AutoBlurMinutes > 0 && !Note.ExcludeFromAutoBlur
                && !string.IsNullOrEmpty(Note.PinHash) && !Note.IsLocked)
            {
                _autoBlurTimer = new System.Windows.Threading.DispatcherTimer
                {
                    Interval = TimeSpan.FromMinutes(Note.AutoBlurMinutes)
                };
                _autoBlurTimer.Tick += (_, __) =>
                {
                    _autoBlurTimer.Stop();
                    Note.IsLocked = true;
                    ApplyLockState();
                };
                _autoBlurTimer.Start();
            }
        }

        private void CmdAutoBlurSet_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem mi && int.TryParse(mi.Tag?.ToString(), out int mins))
            {
                Note.AutoBlurMinutes = mins;
                NoteChanged?.Invoke(this, Note);
                if (!Note.IsLocked) StartAutoBlurTimer();
            }
        }

        private void CmdExcludeAutoBlur_Click(object sender, RoutedEventArgs e)
        {
            Note.ExcludeFromAutoBlur = CmdExcludeAutoBlur.IsChecked;
            NoteChanged?.Invoke(this, Note);
            if (Note.ExcludeFromAutoBlur)
                _autoBlurTimer?.Stop();
            else if (!Note.IsLocked)
                StartAutoBlurTimer();
        }

        private void BtnUnlock_Click(object sender, RoutedEventArgs e) => TryUnlock();
        private void PinBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter) { TryUnlock(); e.Handled = true; return; }
            if (!IsThrottled(out _)) PinErrorText.Visibility = Visibility.Collapsed;
        }

        // ── Attempt throttling (per note, survives window re-open; in-memory only) ──
        private const int FreeAttempts = 5;
        private static readonly TimeSpan BaseLockout = TimeSpan.FromSeconds(30);
        private static readonly TimeSpan MaxLockout  = TimeSpan.FromMinutes(15);
        private static readonly Dictionary<int, (int Fails, DateTime Until)> _attempts = new();
        private System.Windows.Threading.DispatcherTimer _throttleTimer;
        private bool _verifying;

        private bool IsThrottled(out TimeSpan remaining)
        {
            remaining = TimeSpan.Zero;
            if (!_attempts.TryGetValue(Note.Id, out var a) || a.Until <= DateTime.UtcNow) return false;
            remaining = a.Until - DateTime.UtcNow;
            return true;
        }

        private void RegisterFailure()
        {
            _attempts.TryGetValue(Note.Id, out var a);
            int fails = a.Fails + 1;
            DateTime until = DateTime.MinValue;
            if (fails >= FreeAttempts)
            {
                // 30 s after the 5th miss, doubling each further miss, capped at 15 min.
                double secs = BaseLockout.TotalSeconds * Math.Pow(2, Math.Min(fails - FreeAttempts, 10));
                until = DateTime.UtcNow + TimeSpan.FromSeconds(Math.Min(secs, MaxLockout.TotalSeconds));
            }
            _attempts[Note.Id] = (fails, until);
        }

        private void ShowThrottleState()
        {
            if (!IsThrottled(out var remaining))
            {
                _throttleTimer?.Stop();
                PinBox.IsEnabled    = true;
                BtnUnlock.IsEnabled = true;
                PinErrorText.Visibility = Visibility.Collapsed;
                PinBox.Focus();
                return;
            }
            PinBox.IsEnabled    = false;
            BtnUnlock.IsEnabled = false;
            int secs = (int)Math.Ceiling(remaining.TotalSeconds);
            PinErrorText.Text = $"\u26A0 Too many attempts. Try again in {secs / 60}:{secs % 60:00}.";
            PinErrorText.Visibility = Visibility.Visible;
            if (secs % 10 == 0)
                NoteStation.Controls.StatusAnnouncer.Announce($"Try again in {secs} seconds.", PinErrorText);
            if (_throttleTimer == null)
            {
                _throttleTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
                _throttleTimer.Tick += (_, __) => ShowThrottleState();
            }
            _throttleTimer.Start();
        }

        private async void TryUnlock()
        {
            if (_verifying) return;
            if (IsThrottled(out _)) { ShowThrottleState(); return; }
            var entered = PinBox.Password;
            if (string.IsNullOrEmpty(entered)) return;

            string pinHash    = Note.PinHash;
            string masterHash = AppPrefs.Instance.MasterPassphraseHash;
            _verifying = true;
            PinBox.IsEnabled    = false;
            BtnUnlock.IsEnabled = false;
            BtnUnlock.Content   = "Checking…";
            bool pinMatch, masterMatch, pinNeedsRehash, masterNeedsRehash;
            try
            {
                // PBKDF2 (210k iterations, ×2 with a master passphrase) off the UI thread.
                (pinMatch, pinNeedsRehash, masterMatch, masterNeedsRehash) = await System.Threading.Tasks.Task.Run(() =>
                {
                    bool pr = false, mr = false;
                    bool p = !string.IsNullOrEmpty(pinHash)    && PinHasher.Verify(entered, pinHash, out pr);
                    bool m = !p && !string.IsNullOrEmpty(masterHash) && PinHasher.Verify(entered, masterHash, out mr);
                    return (p, pr, m, mr);
                });
            }
            finally
            {
                _verifying = false;
                BtnUnlock.Content   = "Show";
                PinBox.IsEnabled    = true;
                BtnUnlock.IsEnabled = true;
            }

            if (pinMatch || masterMatch)
            {
                // Transparently upgrade a legacy unsalted-SHA-256 hash to salted PBKDF2
                // now that we know the plaintext matched it — no user-visible change.
                if (pinMatch && pinNeedsRehash)
                {
                    Note.PinHash = PinHasher.CreateHash(entered);
                    NoteChanged?.Invoke(this, Note);
                }
                if (masterMatch && masterNeedsRehash)
                {
                    AppPrefs.Instance.MasterPassphraseHash = PinHasher.CreateHash(entered);
                    AppPrefs.Instance.Save();
                }

                _attempts.Remove(Note.Id);
                Note.IsLocked = false;
                ApplyLockState();
                StartAutoBlurTimer();
                ContentBox.Focus();
            }
            else
            {
                RegisterFailure();
                PinBox.Clear();
                if (IsThrottled(out _)) { ShowThrottleState(); return; }
                int left = FreeAttempts - _attempts[Note.Id].Fails;
                PinErrorText.Text       = left > 0
                    ? $"\u2716 Incorrect PIN or passphrase. {left} {(left == 1 ? "try" : "tries")} left."
                    : "\u2716 Incorrect PIN or passphrase.";
                PinErrorText.Visibility = Visibility.Visible;
                PinBox.Focus();
            }
        }

        private void CmdLock_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(Note.PinHash)) return;
            Note.IsLocked = true;
            ApplyLockState();
        }

        private void CmdSetPin_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new PinSetupDialog { Owner = this };
            if (dlg.ShowDialog() == true && !string.IsNullOrEmpty(dlg.ResultPin))
            {
                Note.PinHash  = PinHasher.CreateHash(dlg.ResultPin);
                Note.IsLocked = false;   // stay unlocked after setting
                NoteChanged?.Invoke(this, Note);
                ApplyLockState();
            }
        }

        private void CmdRemovePin_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(Note.PinHash)) return;

            // If currently locked, require PIN to remove it
            if (Note.IsLocked)
            {
                AppDialog.Show("Show the note first (enter its PIN) before removing the PIN.",
                    "Remove PIN", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var r = AppDialog.Show("Remove the PIN from this note?",
                "Remove PIN", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (r == MessageBoxResult.Yes)
            {
                Note.PinHash  = "";
                Note.IsLocked = false;
                NoteChanged?.Invoke(this, Note);
                ApplyLockState();
            }
        }

        // ── Public API ────────────────────────────────────────────────────────
        public void RefreshFromModel()
        {
            _suppressEvents = true;
            TitleBox.Text = Note.Title;
            TitleBox.FontSize = Note.TitleFontSize > 0 ? Note.TitleFontSize : 14;
            LoadContentToBox();
            ApplyColors();
            ApplyFont();
            _suppressEvents = false;
        }

        // ── Rich-text formatting toolbar toggle (moved from footer "Aa" button to the
        // right-click menu 2026-09-09; CmdFormatToolbarItem.IsChecked already reflects
        // the post-click state by the time MenuItem raises Click) ─────────────────────
        private void CmdFormatToolbarItem_Click(object sender, RoutedEventArgs e)
        {
            bool show = ((MenuItem)sender).IsChecked;
            if (show)
            {
                if (FormatToolbarHost.Child == null)
                    FormatToolbarHost.Child = RichTextFormatting.BuildToolbar(ContentBox);
                FormatToolbarHost.Visibility = Visibility.Visible;
                ContentBox.Focus();
            }
            else
            {
                FormatToolbarHost.Visibility = Visibility.Collapsed;
            }
        }

        // ── F12: Markdown toggle (moved from footer "MD" button 2026-09-09) ─────────
        private void CmdMarkdownItem_Click(object sender, RoutedEventArgs e)
        {
            bool show = ((MenuItem)sender).IsChecked;
            if (show)
            {
                ContentBox.Visibility     = Visibility.Collapsed;
                MarkdownViewer.Visibility = Visibility.Visible;
                RenderMarkdownPreview();
            }
            else
            {
                MarkdownViewer.Visibility = Visibility.Collapsed;
                ContentBox.Visibility     = Visibility.Visible;
            }
        }

        // ── Restore size (2026-09-09: right-click escape hatch for the "note got
        // maximized via Aero-snap/title-bar double-click and I can't resize it back"
        // trap — F3/RestoreWindowCommand didn't work because SystemCommands routed
        // commands need explicit CommandBindings, which this borderless window never
        // registered; see the CommandBindings added in the constructor below). ───────
        private void CmdRestoreSize_Click(object sender, RoutedEventArgs e)
        {
            if (WindowState == WindowState.Maximized)
                WindowState = WindowState.Normal;
        }

        private void RenderMarkdownPreview()
        {
            string raw = Note?.Content ?? "";
            // If rich XAML content, strip tags to get plain text first
            if (raw.TrimStart().StartsWith("<"))
            {
                raw = System.Text.RegularExpressions.Regex.Replace(raw, "<[^>]+>", " ");
                raw = System.Text.RegularExpressions.Regex.Replace(raw, @"\s+", " ").Trim();
            }
            var doc = MarkdownToFlowDocument(raw);
            doc.Foreground = new System.Windows.Media.SolidColorBrush(
                (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(Note?.TextColor ?? "#FFFFFF"));
            MarkdownViewer.Document = doc;
        }

        private static FlowDocument MarkdownToFlowDocument(string md)
        {
            var doc  = new FlowDocument();
            var lines = md.Split('\n');
            List currentList = null;

            foreach (var rawLine in lines)
            {
                string line = rawLine.TrimEnd('\r');

                // ── Headings ───────────────────────────────────────────────────
                if (line.StartsWith("### "))
                {
                    currentList = null;
                    var p = new Paragraph(new Run(line.Substring(4)))
                        { FontSize = 16, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 8, 0, 2) };
                    doc.Blocks.Add(p); continue;
                }
                if (line.StartsWith("## "))
                {
                    currentList = null;
                    var p = new Paragraph(new Run(line.Substring(3)))
                        { FontSize = 18, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 10, 0, 2) };
                    doc.Blocks.Add(p); continue;
                }
                if (line.StartsWith("# "))
                {
                    currentList = null;
                    var p = new Paragraph(new Run(line.Substring(2)))
                        { FontSize = 22, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 12, 0, 4) };
                    doc.Blocks.Add(p); continue;
                }

                // ── Horizontal rule ────────────────────────────────────────────
                if (line == "---" || line == "***" || line == "___")
                {
                    currentList = null;
                    doc.Blocks.Add(new BlockUIContainer(
                        new System.Windows.Shapes.Line { X2 = 1, Stretch = System.Windows.Media.Stretch.Fill,
                            Stroke = System.Windows.Media.Brushes.Gray, StrokeThickness = 1,
                            Margin = new Thickness(0, 4, 0, 4) }));
                    continue;
                }

                // ── List items ─────────────────────────────────────────────────
                if (line.StartsWith("- [ ] ") || line.StartsWith("- [x] ") || line.StartsWith("- [X] "))
                {
                    bool done = line[3] != ' ';
                    string text = line.Substring(6);
                    if (currentList == null) { currentList = new List(); doc.Blocks.Add(currentList); }
                    var li = new ListItem(new Paragraph(
                        new Run((done ? "☑ " : "☐ ") + text)));
                    currentList.ListItems.Add(li);
                    continue;
                }
                if (line.StartsWith("- ") || line.StartsWith("* "))
                {
                    if (currentList == null) { currentList = new List(); doc.Blocks.Add(currentList); }
                    var li = new ListItem(new Paragraph(
                        ApplyInlineMarkdown(line.Substring(2))));
                    currentList.ListItems.Add(li);
                    continue;
                }

                // ── Regular paragraph ──────────────────────────────────────────
                currentList = null;
                if (string.IsNullOrWhiteSpace(line))
                {
                    doc.Blocks.Add(new Paragraph { Margin = new Thickness(0, 4, 0, 0) });
                }
                else
                {
                    doc.Blocks.Add(new Paragraph(ApplyInlineMarkdown(line)));
                }
            }
            return doc;
        }

        private static Inline ApplyInlineMarkdown(string text)
        {
            var span = new Span();
            // Simple inline: **bold**, *italic*, `code`
            var pattern = @"(\*\*(.+?)\*\*|\*(.+?)\*|`(.+?)`)";
            int pos = 0;
            foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(text, pattern))
            {
                if (m.Index > pos)
                    span.Inlines.Add(new Run(text.Substring(pos, m.Index - pos)));
                if (m.Value.StartsWith("**"))
                    span.Inlines.Add(new Bold(new Run(m.Groups[2].Value)));
                else if (m.Value.StartsWith("*"))
                    span.Inlines.Add(new Italic(new Run(m.Groups[3].Value)));
                else // code
                    span.Inlines.Add(new Run(m.Groups[4].Value)
                        { FontFamily = new System.Windows.Media.FontFamily("Consolas"), Background = System.Windows.Media.Brushes.Gray });
                pos = m.Index + m.Length;
            }
            if (pos < text.Length) span.Inlines.Add(new Run(text.Substring(pos)));
            return span.Inlines.Count == 0 ? (Inline)new Run(text) : span;
        }

        // ── F13: Screenshot → OCR ─────────────────────────────────────────────
        private async void BtnScreenshot_Click(object sender, RoutedEventArgs e)
        {
            Hide();
            await System.Threading.Tasks.Task.Delay(200);

            var overlay = new ScreenCaptureOverlay { Owner = null };
            overlay.ShowDialog();

            Show();
            Activate();

            if (!overlay.Captured || overlay.CapturedBitmap == null) return;

            string ocrText = await RunOcrOnBitmapAsync(overlay.CapturedBitmap);
            overlay.CapturedBitmap.Dispose();

            // Insert OCR text at end of note
            if (!string.IsNullOrWhiteSpace(ocrText))
            {
                var textRange = new TextRange(
                    ContentBox.Document.ContentEnd, ContentBox.Document.ContentEnd);
                textRange.Text = "\n[Screenshot OCR]\n" + ocrText + "\n";
            }
            else
            {
                AppDialog.Show("No text recognized in selected region.", "OCR Result",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private static async System.Threading.Tasks.Task<string> RunOcrOnBitmapAsync(System.Drawing.Bitmap bmp)
        {
            try
            {
                // Convert System.Drawing.Bitmap to Windows.Graphics.Imaging.SoftwareBitmap
                using var ms = new System.IO.MemoryStream();
                bmp.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
                ms.Seek(0, System.IO.SeekOrigin.Begin);

                var raStream = new global::Windows.Storage.Streams.InMemoryRandomAccessStream();
                var writer   = new global::Windows.Storage.Streams.DataWriter(raStream.GetOutputStreamAt(0));
                writer.WriteBytes(ms.ToArray());
                await writer.StoreAsync();
                raStream.Seek(0);
                var decoder  = await global::Windows.Graphics.Imaging.BitmapDecoder.CreateAsync(raStream);
                var softBmp  = await decoder.GetSoftwareBitmapAsync();

                if (softBmp.BitmapPixelFormat != global::Windows.Graphics.Imaging.BitmapPixelFormat.Bgra8)
                    softBmp = global::Windows.Graphics.Imaging.SoftwareBitmap.Convert(
                        softBmp,
                        global::Windows.Graphics.Imaging.BitmapPixelFormat.Bgra8,
                        global::Windows.Graphics.Imaging.BitmapAlphaMode.Premultiplied);

                var engine = global::Windows.Media.Ocr.OcrEngine.TryCreateFromUserProfileLanguages();
                if (engine == null)
                    return "[OCR error: no OCR language pack installed]";

                var result = await engine.RecognizeAsync(softBmp);
                return result.Text;
            }
            catch (Exception ex)
            {
                return $"[OCR error: {ex.Message}]";
            }
        }

        // ── F15: Texture overlay ───────────────────────────────────────────────
        public void RefreshTexture() => ApplyNoteTexture(Note?.NoteTexture ?? "");
        private void ApplyNoteTexture(string texture)
        {
            if (TextureOverlay == null) return;
            _ruleLinesActive = texture == "Lined" || texture == "Grid";
            if (string.IsNullOrEmpty(texture))
            {
                TextureOverlay.Visibility = Visibility.Collapsed;
                RuleCanvas?.Children.Clear();
                return;
            }
            // Stroke color contrasts with the note background so lines/dots are
            // visible on both light and dark notes (and never wash out the text).
            var bg = ColorFromHex(Note?.BackgroundColor ?? "#2A2A2A") ?? Colors.Black;
            double L = ComputeContentLineHeight();
            // Horizontal rules for Lined/Grid are drawn by RedrawRuleLines() from the
            // real rendered text, not estimated from font metrics — two earlier
            // attempts at metric-based alignment still drifted from actual glyph
            // positions. The tiled brush below now only ever supplies non-ruled
            // decoration (Kraft/Cork/Dots) or Grid's vertical lines.
            TextureOverlay.Background = BuildTextureBrush(texture, StrokeForBackground(bg, out double opacity), L, 0, verticalLinesOnly: true);
            TextureOverlay.Opacity    = opacity;
            TextureOverlay.Margin     = new Thickness(0);
            TextureOverlay.Visibility = texture == "Lined" ? Visibility.Collapsed : Visibility.Visible;
            RedrawRuleLines();
        }

        /// <summary>Draws horizontal rule lines at the actual rendered bottom of each
        /// visual text line (measured via TextPointer), so Lined/Grid textures always
        /// track real glyph positions — including wrapped lines and scroll offset —
        /// instead of an estimate from font ascent/descent metrics.</summary>
        private void RedrawRuleLines()
        {
            if (RuleCanvas == null) return;
            RuleCanvas.Children.Clear();
            if (!_ruleLinesActive || ContentBox?.Document == null) return;

            var bg = ColorFromHex(Note?.BackgroundColor ?? "#2A2A2A") ?? Colors.Black;
            var stroke = StrokeForBackground(bg, out double opacity);
            var brush  = new SolidColorBrush(stroke);
            double width = Math.Max(ContentBox.ActualWidth, ActualWidth);

            TextPointer p = ContentBox.Document.ContentStart;
            int guard = 0;
            while (p != null && guard++ < 4000)
            {
                Rect r;
                try { r = p.GetCharacterRect(LogicalDirection.Forward); }
                catch { break; }

                if (r.Height > 0)
                {
                    double y = Math.Round(r.Bottom) - 0.5; // crisp 1px line
                    if (y >= -1 && y <= ContentBox.ActualHeight + 4)
                    {
                        RuleCanvas.Children.Add(new Line
                        {
                            X1 = 0, Y1 = y, X2 = width, Y2 = y,
                            Stroke = brush, StrokeThickness = 1, Opacity = opacity,
                        });
                    }
                }

                TextPointer next = p.GetLineStartPosition(1, out int moved);
                if (next == null || moved == 0) break;
                p = next;
            }
        }

        /// <summary>Returns a high-contrast stroke color for the given note background,
        /// plus the overlay opacity to use. Light bg → dark lines; dark bg → light lines.</summary>
        public static Color StrokeForBackground(Color bg, out double opacity)
        {
            // Rec. 601 luma
            double luma = (0.299 * bg.R + 0.587 * bg.G + 0.114 * bg.B) / 255.0;
            if (luma > 0.5) { opacity = 0.28; return Colors.Black; }
            opacity = 0.22; return Colors.White;
        }

        /// <summary>Builds the tiled texture brush. Shared by the note window and the
        /// Format dialog preview so both render identically.</summary>
        public static System.Windows.Media.DrawingBrush BuildTextureBrush(string texture, Color stroke, double lineHeight, double ruleY, bool verticalLinesOnly = false)
        {
            var brush = new System.Windows.Media.SolidColorBrush(stroke);
            var pen   = new System.Windows.Media.Pen(brush, 1.0);

            // Ruled textures tile at the text line height L so rules track text rows;
            // the rule is drawn at ruleY (the text baseline) so text rests on the line.
            double L    = lineHeight > 4 ? lineHeight : 20;
            double rule = (ruleY > 0 && ruleY < L) ? ruleY : L - 0.5;

            System.Windows.Media.Drawing drawing;
            double tileW, tileH;

            switch (texture)
            {
                case "Lined":
                    tileW = L; tileH = L;
                    // verticalLinesOnly (live note window): horizontal rules come from
                    // RedrawRuleLines() measuring real text instead — emit nothing here.
                    drawing = verticalLinesOnly
                        ? new System.Windows.Media.DrawingGroup()
                        : new System.Windows.Media.GeometryDrawing(null, pen,
                              new System.Windows.Media.LineGeometry(new Point(0, rule), new Point(L, rule)));
                    break;
                case "Grid":
                    tileW = L; tileH = L;
                    drawing = verticalLinesOnly
                        ? new System.Windows.Media.GeometryDrawing(null, pen,
                              new System.Windows.Media.LineGeometry(new Point(rule, 0), new Point(rule, L)))
                        : new System.Windows.Media.DrawingGroup
                          {
                              Children = {
                                  new System.Windows.Media.GeometryDrawing(null, pen,
                                      new System.Windows.Media.LineGeometry(new Point(0, rule), new Point(L, rule))),
                                  new System.Windows.Media.GeometryDrawing(null, pen,
                                      new System.Windows.Media.LineGeometry(new Point(rule, 0), new Point(rule, L))),
                              }
                          };
                    break;
                // Kraft — diagonal hatch (fixed tile, not text-ruled)
                case "Kraft":
                    tileW = 20; tileH = 20;
                    drawing = new System.Windows.Media.DrawingGroup
                    {
                        Children = {
                            new System.Windows.Media.GeometryDrawing(null, pen,
                                new System.Windows.Media.LineGeometry(new Point(0, 0), new Point(20, 20))),
                            new System.Windows.Media.GeometryDrawing(null, pen,
                                new System.Windows.Media.LineGeometry(new Point(0, 20), new Point(20, 0))),
                        }
                    };
                    break;
                // Cork — scattered speckle dots
                case "Cork":
                    tileW = 20; tileH = 20;
                    drawing = new System.Windows.Media.DrawingGroup
                    {
                        Children = {
                            new System.Windows.Media.GeometryDrawing(brush, null,
                                new System.Windows.Media.EllipseGeometry(new Point(4, 5),  1.3, 1.3)),
                            new System.Windows.Media.GeometryDrawing(brush, null,
                                new System.Windows.Media.EllipseGeometry(new Point(13, 9), 1.0, 1.0)),
                            new System.Windows.Media.GeometryDrawing(brush, null,
                                new System.Windows.Media.EllipseGeometry(new Point(8, 15), 1.5, 1.5)),
                            new System.Windows.Media.GeometryDrawing(brush, null,
                                new System.Windows.Media.EllipseGeometry(new Point(17, 17),1.1, 1.1)),
                        }
                    };
                    break;
                case "Dots":
                    tileW = 20; tileH = 20;
                    drawing = new System.Windows.Media.GeometryDrawing(brush, null,
                                  new System.Windows.Media.EllipseGeometry(new Point(10, 10), 1.4, 1.4));
                    break;
                default:
                    tileW = 20; tileH = 20;
                    drawing = new System.Windows.Media.GeometryDrawing(System.Windows.Media.Brushes.Transparent, null,
                                  new System.Windows.Media.RectangleGeometry(new Rect(0, 0, 1, 1)));
                    break;
            }

            return new System.Windows.Media.DrawingBrush
            {
                Drawing       = drawing,
                TileMode      = System.Windows.Media.TileMode.Tile,
                Stretch       = System.Windows.Media.Stretch.None,
                ViewportUnits = System.Windows.Media.BrushMappingMode.Absolute,
                Viewport      = new Rect(0, 0, tileW, tileH),
            };
        }
    }
}
