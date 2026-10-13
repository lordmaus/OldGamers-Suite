using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace NoteStation
{
    /// <summary>
    /// Slim, edge-docked strip of colored note "tabs" (macOS-Stickies-style). Each tab is a
    /// note flagged StickyNote.ShowInSidebar. Hovering (or clicking) a tab slides out a small,
    /// read-only preview of the note in its own colors (PreviewPopup) — it does NOT open the
    /// real StickyNoteWindow. Only right-click → Open does that, via TabClicked, handled by
    /// MainWindow.OpenNoteWindow. A note can be both sidebar-pinned and shown standalone at the
    /// same time — ShowInSidebar and IsVisible are independent flags.
    /// Mirrors ClipOverlayWindow/TaskOverlayWindow's hide-not-close lifecycle so toggling the
    /// sidebar off is instant and state survives until the app actually exits (CloseForReal).
    /// </summary>
    public partial class NotesSidebarWindow : Window
    {
        /// <summary>A tab was clicked — MainWindow should open/focus this note's window.</summary>
        public event EventHandler<StickyNote> TabClicked;
        /// <summary>"Remove from Sidebar" was chosen for a tab.</summary>
        public event EventHandler<StickyNote> RemoveFromSidebarRequested;
        /// <summary>
        /// "New Note" was requested. The argument is the tab that was right-clicked to request
        /// it (so the new note can inherit its folder/color), or null for a generic new note
        /// (from the empty-area menu or the + button).
        /// </summary>
        public event EventHandler<StickyNote> NewNoteRequested;
        /// <summary>A "Bar Opacity" preset was chosen from the empty-area menu — MainWindow persists it.</summary>
        public event EventHandler<double> BarOpacityChanged;
        /// <summary>Title/content were committed from the inline popout editor — MainWindow should
        /// persist the note and refresh dependent UI (list, standalone window if open).</summary>
        public event EventHandler<StickyNote> NoteEdited;
        /// <summary>The bar was dragged to a new width (debounced) — MainWindow persists it.</summary>
        public event EventHandler<double> WidthChanged;
        /// <summary>"Show + Button" was toggled from the empty-area menu — MainWindow persists it.</summary>
        public event EventHandler<bool> ShowAddButtonChanged;
        /// <summary>An existing note was chosen from the empty-area "Add Existing Note" submenu.</summary>
        public event EventHandler<StickyNote> AddExistingNoteRequested;
        /// <summary>"Dock Left"/"Dock Right" was chosen — MainWindow persists the new edge.</summary>
        public event EventHandler<string> DockEdgeChanged;
        /// <summary>"Top"/"Middle"/"Bottom" was chosen — MainWindow persists the new anchor.</summary>
        public event EventHandler<string> DockPositionChanged;
        /// <summary>Undocked (free-floating) state was toggled — MainWindow persists it.</summary>
        public event EventHandler<bool> UndockedChanged;
        /// <summary>The undocked bar was dragged to a new screen position (debounced) —
        /// MainWindow persists it.</summary>
        public event EventHandler<Point> FloatPositionChanged;

        private bool _reallyClosing;
        private string _dockEdge = "Right";
        private string _dockPosition = "Top";
        private bool _isUndocked;
        private DispatcherTimer _floatPosSaveTimer;

        private DispatcherTimer _previewOpenTimer;
        private DispatcherTimer _previewCloseTimer;
        private StickyNote _pendingPreviewNote;
        private Button _pendingPreviewAnchor;

        // Inline quick-edit (left-click the tab, or the already-open hover preview, to edit
        // title/content directly in the popout — plain text only, see BuildXamlContent).
        private bool _isEditing;
        private StickyNote _currentPreviewNote;

        private DispatcherTimer _widthSaveTimer;

        // Full note list (not just the ones already pinned here) — kept so the empty-area
        // "Add Existing Note" submenu has something to list.
        private List<StickyNote> _allNotes = new();

        public NotesSidebarWindow()
        {
            InitializeComponent();
            LocationChanged += Window_LocationChanged;
        }

        /// <summary>Called by MainWindow whenever the notes collection changes.</summary>
        public void Refresh(IEnumerable<StickyNote> notes)
        {
            _allNotes = notes.Where(n => !n.IsDeleted).ToList();
            TabsList.ItemsSource = _allNotes
                .Where(n => n.ShowInSidebar)
                .OrderBy(n => n.Title, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        /// <summary>True while the bar is free-floating (Undock Sidebar) instead of pinned to a
        /// screen edge — cleared by DockToEdge, set by Undock.</summary>
        public bool IsUndocked => _isUndocked;

        /// <summary>Docks to a screen edge ("Left"/"Right") and, along that edge, a vertical
        /// anchor ("Top"/"Middle"/"Bottom"). Position defaults to whatever was last set (Top
        /// initially) when omitted, so callers that only care about the edge don't need to
        /// repeat it.</summary>
        public void DockToEdge(string edge, string position = null)
        {
            _isUndocked = false;
            BgLayer.CornerRadius = new CornerRadius(8, 0, 0, 8);
            UndockGrip.Visibility = Visibility.Collapsed;
            _dockEdge = edge;
            if (!string.IsNullOrEmpty(position)) _dockPosition = position;
            var wa = SystemParameters.WorkArea;
            // No fixed Height — SizeToContent="Height" (see XAML) shrink-wraps the window to
            // however many tabs are actually pinned, so it reads as "notes on the edge" rather
            // than a permanent full-height bar. MaxHeight just stops it overflowing the screen;
            // once there are more tabs than that, the ScrollViewer takes over.
            MaxHeight = Math.Max(120, wa.Height - 24);
            Left   = string.Equals(edge, "Left", StringComparison.OrdinalIgnoreCase)
                     ? wa.Left
                     : wa.Right - Width;
            // Best guess before the window has actually measured its auto-height — corrected the
            // instant real layout runs, via Window_SizeChanged below (harmless for Top, where the
            // anchor doesn't depend on height at all).
            PositionVertically(Math.Max(MinHeight, ActualHeight));
            // The grip always sits on the inner edge (the one that isn't pinned to the screen).
            ResizeGrip.Edge = IsDockedLeft ? OldGamers.Wpf.Shared.Controls.GripEdge.Right : OldGamers.Wpf.Shared.Controls.GripEdge.Left;
        }

        /// <summary>Undocks the bar so it floats free at an arbitrary screen position instead of
        /// being pinned to a screen edge — lets the user drag it clear of a browser/Explorer
        /// scrollbar it would otherwise sit on top of while docked. Pass NaN for either coordinate
        /// to fall back to a sensible default near the last docked edge. Drag with the left mouse
        /// button on the bar's empty background (EmptyArea_MouseLeftButtonDown); width still
        /// resizes via ResizeGrip same as docked.</summary>
        public void Undock(double left, double top)
        {
            _isUndocked = true;
            var wa = SystemParameters.WorkArea;
            MaxHeight = Math.Max(120, wa.Height - 24);
            Left = !double.IsNaN(left) ? left : (IsDockedLeft ? wa.Left + 40 : wa.Right - Width - 40);
            Top  = !double.IsNaN(top)  ? top  : wa.Top + 80;
            // Free-floating — round all four corners instead of the edge-flush "8,0,0,8" used
            // while docked.
            BgLayer.CornerRadius = new CornerRadius(8);
            UndockGrip.Visibility = Visibility.Visible;
        }

        private void EmptyArea_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            // Only the undocked bar drags this way; while docked, the Dock Side/Align menu
            // commands are the only way to move it (dragging a screen-edge-pinned bar would be
            // confusing, and tab/button clicks already mark the event handled before it gets here).
            if (!_isUndocked) return;
            e.Handled = true; // stop the bubble to the Grid's own handler (grip Border already handled it)
            try { DragMove(); } catch (InvalidOperationException) { /* mouse button wasn't down */ }
        }

        private void Window_LocationChanged(object sender, EventArgs e)
        {
            if (!_isUndocked) return;
            _floatPosSaveTimer ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
            _floatPosSaveTimer.Stop();
            _floatPosSaveTimer.Tick -= FloatPosSaveTimer_Tick;
            _floatPosSaveTimer.Tick += FloatPosSaveTimer_Tick;
            _floatPosSaveTimer.Start();
        }

        private void FloatPosSaveTimer_Tick(object sender, EventArgs e)
        {
            _floatPosSaveTimer.Stop();
            FloatPositionChanged?.Invoke(this, new Point(Left, Top));
        }

        /// <summary>Places the bar's Top so it reads as anchored to the Top/Middle/Bottom of the
        /// work area for the given (about-to-be-applied) height. Re-run on every auto-resize
        /// (Window_SizeChanged) so Middle/Bottom keep tracking as the tab count changes the
        /// window's actual height.</summary>
        private void PositionVertically(double height)
        {
            var wa = SystemParameters.WorkArea;
            Top = _dockPosition switch
            {
                "Bottom" => wa.Bottom - height - 12,
                "Middle" => wa.Top + (wa.Height - height) / 2,
                _        => wa.Top + 12,   // "Top"
            };
        }

        /// <summary>Shows or hides the bottom "+" add-note slot (right-click → New Note always
        /// works either way — this only controls the always-visible shortcut).</summary>
        public void SetShowAddButton(bool show) => BtnAddTab.Visibility = show ? Visibility.Visible : Visibility.Collapsed;

        /// <summary>
        /// Fades just the bar's chrome (background + border) — never the tabs themselves, which
        /// live in a separate always-opaque layer, so "Hidden" leaves the colored tabs floating
        /// with no visible strip behind them, still fully clickable.
        /// </summary>
        public void SetBarOpacity(double opacity)
        {
            BgLayer.Opacity = Math.Max(0.0, Math.Min(1.0, opacity));
        }

        // Width grip: Controls/AccessibleResizeGrip (Edge Left/Right) — drag uses WM_NCLBUTTONDOWN
        // HTLEFT/HTRIGHT (same OS loop as the old SC_SIZE_LEFT/RIGHT), plus Left/Right arrow keys.

        /// <summary>WCAG 2.1.3: Alt+Arrow moves the undocked bar (Shift = 1px); Shift+F10 / Menu key
        /// opens the bar menu that is otherwise right-click only.</summary>
        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            var key = e.Key == Key.System ? e.SystemKey : e.Key;
            if (key == Key.Apps || (key == Key.F10 && Keyboard.Modifiers == ModifierKeys.Shift))
            {
                ShowBarMenu(fromKeyboard: true);
                e.Handled = true;
                return;
            }
            if (!_isUndocked || (Keyboard.Modifiers & ModifierKeys.Alt) == 0) return;
            double step = (Keyboard.Modifiers & ModifierKeys.Shift) != 0 ? 1 : 10;
            switch (key)
            {
                case Key.Left:  Left -= step; break;
                case Key.Right: Left += step; break;
                case Key.Up:    Top  -= step; break;
                case Key.Down:  Top  += step; break;
                default: return;
            }
            e.Handled = true;
        }

        private void Window_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            // WPF quietly downgrades SizeToContent to Manual as a side effect of ANY
            // native/OS-driven resize — including the width-only drag below (ResizeGrip's
            // WM_SYSCOMMAND SC_SIZE_LEFT/RIGHT), even though that drag never touches Height.
            // Left alone, the bar would stop auto-resizing its Height on every future note
            // add/remove (Refresh) after the very first width drag. Restore it immediately so
            // the shrink-wrap behavior documented in DockToEdge keeps working.
            if (SizeToContent != SizeToContent.Height) SizeToContent = SizeToContent.Height;

            // Keep a Middle/Bottom anchor correct as the tab count auto-grows/shrinks the window
            // (Top doesn't need this — its anchor is height-independent).
            if (e.HeightChanged && _dockPosition != "Top") PositionVertically(e.NewSize.Height);

            if (!e.WidthChanged) return;
            _widthSaveTimer ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
            _widthSaveTimer.Stop();
            _widthSaveTimer.Tick -= WidthSaveTimer_Tick;
            _widthSaveTimer.Tick += WidthSaveTimer_Tick;
            _widthSaveTimer.Start();
        }

        private void WidthSaveTimer_Tick(object sender, EventArgs e)
        {
            _widthSaveTimer.Stop();
            WidthChanged?.Invoke(this, ActualWidth);
        }

        private void Tab_Click(object sender, RoutedEventArgs e)
        {
            // Left-click no longer opens the real note window — it slides out the same preview
            // as hover (immediately, no open-delay) and drops straight into inline edit mode.
            // Right-click → Open is the only path to the real StickyNoteWindow now.
            if (sender is Button btn && btn.Tag is StickyNote note)
            {
                _previewOpenTimer?.Stop();
                ShowPreview(note, btn);
                EnterEditMode();
            }
        }

        private void Tab_MouseEnter(object sender, MouseEventArgs e)
        {
            if (sender is not Button btn || btn.Tag is not StickyNote note) return;
            _previewCloseTimer?.Stop();
            _pendingPreviewNote   = note;
            _pendingPreviewAnchor = btn;

            _previewOpenTimer ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
            _previewOpenTimer.Stop();
            _previewOpenTimer.Tick -= PreviewOpenTimer_Tick;
            _previewOpenTimer.Tick += PreviewOpenTimer_Tick;
            _previewOpenTimer.Start();
        }

        private void PreviewOpenTimer_Tick(object sender, EventArgs e)
        {
            _previewOpenTimer.Stop();
            if (_pendingPreviewNote != null && _pendingPreviewAnchor != null)
                ShowPreview(_pendingPreviewNote, _pendingPreviewAnchor);
        }

        private void Tab_MouseLeave(object sender, MouseEventArgs e)
        {
            _previewOpenTimer?.Stop();
            if (_isEditing) return; // don't auto-close while the user is typing
            ScheduleHidePreview();
        }

        private void PreviewPopup_MouseEnter(object sender, MouseEventArgs e) => _previewCloseTimer?.Stop();

        private void PreviewPopup_MouseLeave(object sender, MouseEventArgs e)
        {
            if (_isEditing) return;
            ScheduleHidePreview();
        }

        private void ScheduleHidePreview()
        {
            _previewCloseTimer ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(180) };
            _previewCloseTimer.Stop();
            _previewCloseTimer.Tick -= PreviewCloseTimer_Tick;
            _previewCloseTimer.Tick += PreviewCloseTimer_Tick;
            _previewCloseTimer.Start();
        }

        private void PreviewCloseTimer_Tick(object sender, EventArgs e)
        {
            _previewCloseTimer.Stop();
            HidePreview();
        }

        private bool IsDockedLeft => string.Equals(_dockEdge, "Left", StringComparison.OrdinalIgnoreCase);

        /// <summary>Renders the note's actual content into the read-only preview — its own
        /// font family/size, and full rich formatting (bold/italic/color runs, paragraph
        /// spacing) via the same XAML round-trip StickyNoteWindow's editor uses, instead of a
        /// flattened plain-text string. Locked notes stay safe: SafeFormatSource is just the
        /// redaction message as plain text when IsLocked, so nothing real ever loads.</summary>
        private void RenderPreviewBody(StickyNote note)
        {
            PreviewBodyView.FontFamily = new FontFamily(string.IsNullOrWhiteSpace(note.FontFamily) ? "Segoe UI" : note.FontFamily);
            PreviewBodyView.FontSize   = note.FontSize > 0 ? note.FontSize : 12;
            RichTextFormatting.LoadContent(PreviewBodyView, note.SafeFormatSource);
            PreviewBodyView.Document.PagePadding = new Thickness(0);
        }

        private void ShowPreview(StickyNote note, Button anchor)
        {
            if (_isEditing) CommitEdit();
            _currentPreviewNote = note;

            PreviewTitle.Text = string.IsNullOrWhiteSpace(note.Title) ? "Untitled" : note.Title;
            PreviewTitle.FontSize = note.TitleFontSize > 0 ? note.TitleFontSize : 14;
            RenderPreviewBody(note);

            if (ColorFromHex(note.BackgroundColor) is Color bg)
                PreviewBorder.Background = new SolidColorBrush(bg);
            if (ColorFromHex(note.TextColor) is Color fg)
            {
                var fgBrush = new SolidColorBrush(fg);
                PreviewTitle.Foreground = fgBrush;
                PreviewBodyView.Foreground = fgBrush;
            }

            // Slide out toward the screen interior — opposite of whichever edge we're docked to.
            PreviewPopup.Placement        = IsDockedLeft ? PlacementMode.Right : PlacementMode.Left;
            PreviewPopup.HorizontalOffset = IsDockedLeft ? 8 : -8;
            PreviewPopup.PlacementTarget  = anchor;
            PreviewPopup.IsOpen           = true;

            double from = IsDockedLeft ? -18 : 18;
            PreviewSlide.BeginAnimation(TranslateTransform.XProperty, null);
            PreviewBorder.BeginAnimation(UIElement.OpacityProperty, null);
            PreviewSlide.X = from;
            PreviewBorder.Opacity = 0;
            PreviewSlide.BeginAnimation(TranslateTransform.XProperty,
                new DoubleAnimation(from, 0, TimeSpan.FromMilliseconds(140))
                { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } });
            PreviewBorder.BeginAnimation(UIElement.OpacityProperty,
                new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(140)));
        }

        private void HidePreview()
        {
            if (!PreviewPopup.IsOpen) return;
            double to = IsDockedLeft ? -18 : 18;
            var slide = new DoubleAnimation(0, to, TimeSpan.FromMilliseconds(110));
            var fade  = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(110));
            fade.Completed += (_, __) => PreviewPopup.IsOpen = false;
            PreviewSlide.BeginAnimation(TranslateTransform.XProperty, slide);
            PreviewBorder.BeginAnimation(UIElement.OpacityProperty, fade);
        }

        private void HidePreviewImmediate()
        {
            if (_isEditing) CommitEdit();
            _previewOpenTimer?.Stop();
            _previewCloseTimer?.Stop();
            PreviewSlide.BeginAnimation(TranslateTransform.XProperty, null);
            PreviewBorder.BeginAnimation(UIElement.OpacityProperty, null);
            PreviewPopup.IsOpen = false;
        }

        private static Color? ColorFromHex(string hex)
        {
            if (string.IsNullOrWhiteSpace(hex)) return null;
            try { return (Color)ColorConverter.ConvertFromString(hex); }
            catch { return null; }
        }

        // ── Inline quick-edit ────────────────────────────────────────────────
        // Swaps the read-only PreviewTitle TextBlock / PreviewBodyView RichTextBox for editable TextBoxes in
        // place, right inside the same popout. Plain text only — see BuildXamlContent.

        private void EnterEditMode()
        {
            if (_currentPreviewNote == null || _currentPreviewNote.IsLocked || _isEditing) return;
            _isEditing = true;
            _previewOpenTimer?.Stop();
            _previewCloseTimer?.Stop();

            PreviewTitleBox.Text = _currentPreviewNote.Title ?? "";
            PreviewBodyBox.Text  = ExtractPlainText(_currentPreviewNote.Content);

            if (PreviewTitle.Foreground is SolidColorBrush fg)
            {
                PreviewTitleBox.Foreground = fg;
                PreviewBodyBox.Foreground  = fg;
                PreviewTitleBox.CaretBrush = fg;
                PreviewBodyBox.CaretBrush  = fg;
            }

            PreviewTitle.Visibility     = Visibility.Collapsed;
            PreviewBodyView.Visibility  = Visibility.Collapsed;
            PreviewTitleBox.Visibility  = Visibility.Visible;
            PreviewBodyBox.Visibility   = Visibility.Visible;
            PreviewHint.Text = "Esc to cancel \u2022 click away to save";

            PreviewBodyBox.Focus();
            PreviewBodyBox.CaretIndex = PreviewBodyBox.Text.Length;
        }

        /// <summary>Saves any pending edit back to the note (title/content only) and returns the
        /// popout to its read-only preview look. No-op — just visually exits — if nothing changed.</summary>
        private void CommitEdit()
        {
            var note = _currentPreviewNote;
            if (note != null)
            {
                string newTitle = string.IsNullOrWhiteSpace(PreviewTitleBox.Text) ? "Untitled" : PreviewTitleBox.Text.Trim();
                string newBody  = PreviewBodyBox.Text ?? "";
                string oldBody  = ExtractPlainText(note.Content);

                bool changed = false;
                if (note.Title != newTitle) { note.Title = newTitle; changed = true; }
                if (newBody != oldBody)     { note.Content = BuildXamlContent(newBody); changed = true; }

                PreviewTitle.Text = string.IsNullOrWhiteSpace(note.Title) ? "Untitled" : note.Title;
                RenderPreviewBody(note);

                if (changed) NoteEdited?.Invoke(this, note);
            }
            ExitEditModeVisual();
        }

        private void CancelEdit() => ExitEditModeVisual();

        private void ExitEditModeVisual()
        {
            _isEditing = false;
            PreviewTitleBox.Visibility = Visibility.Collapsed;
            PreviewBodyBox.Visibility  = Visibility.Collapsed;
            PreviewTitle.Visibility    = Visibility.Visible;
            PreviewBodyView.Visibility = Visibility.Visible;
            PreviewHint.Text = "Right-click the tab to open";
        }

        private void PreviewStack_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) => EnterEditMode();

        private void PreviewEditBox_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
        {
            if (!_isEditing) return;
            // Focus hopping between the title box and the body box is still "editing" — only
            // commit once focus leaves both.
            if (ReferenceEquals(e.NewFocus, PreviewTitleBox) || ReferenceEquals(e.NewFocus, PreviewBodyBox)) return;
            CommitEdit();
        }

        private void PreviewEditBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                e.Handled = true;
                CancelEdit();
                Keyboard.ClearFocus();
            }
            else if (e.Key == Key.Enter && ReferenceEquals(sender, PreviewTitleBox))
            {
                e.Handled = true;
                PreviewBodyBox.Focus();
                PreviewBodyBox.CaretIndex = PreviewBodyBox.Text.Length;
            }
        }

        /// <summary>Plain-text extraction matching StickyNoteWindow's own RichTextBox round-trip
        /// (TextRange + DataFormats.Xaml) so edits made here stay format-compatible.</summary>
        private static string ExtractPlainText(string content)
        {
            if (string.IsNullOrEmpty(content)) return "";
            if (content.TrimStart().StartsWith("<"))
            {
                try
                {
                    var doc = new FlowDocument();
                    using var ms = new MemoryStream(Encoding.UTF8.GetBytes(content));
                    var range = new TextRange(doc.ContentStart, doc.ContentEnd);
                    range.Load(ms, DataFormats.Xaml);
                    return range.Text.TrimEnd('\r', '\n');
                }
                catch { /* fall through to raw text */ }
            }
            return content;
        }

        /// <summary>Builds a Note.Content-compatible XAML string from plain text (one Paragraph
        /// per line), via the same TextRange/DataFormats.Xaml path StickyNoteWindow saves with.
        /// This quick-edit surface is plain text only — any rich formatting (bold, per-run color,
        /// etc.) the note had is lost once it's edited from the sidebar popout.</summary>
        private static string BuildXamlContent(string plainText)
        {
            var doc = new FlowDocument();
            foreach (var line in (plainText ?? "").Replace("\r\n", "\n").Split('\n'))
                doc.Blocks.Add(new Paragraph(new Run(line)));
            using var ms = new MemoryStream();
            var range = new TextRange(doc.ContentStart, doc.ContentEnd);
            range.Save(ms, DataFormats.Xaml);
            return Encoding.UTF8.GetString(ms.ToArray());
        }

        private void Tab_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (sender is not Button btn || btn.Tag is not StickyNote note) return;
            e.Handled = true;
            HidePreviewImmediate();

            var menu = new ContextMenu { Placement = PlacementMode.MousePoint };

            var open = new MenuItem { Header = "Open" };
            open.Click += (_, __) => TabClicked?.Invoke(this, note);
            menu.Items.Add(open);

            var newHere = new MenuItem { Header = "\U0001F5D2️ New Note Here" };
            newHere.Click += (_, __) => NewNoteRequested?.Invoke(this, note);
            menu.Items.Add(newHere);

            menu.Items.Add(new Separator());

            var remove = new MenuItem { Header = "Remove from Sidebar" };
            remove.Click += (_, __) => RemoveFromSidebarRequested?.Invoke(this, note);
            menu.Items.Add(remove);

            menu.IsOpen = true;
        }

        private static readonly (string Label, double Value)[] _barOpacityPresets =
        {
            ("100%", 1.0), ("75%", 0.75), ("50%", 0.5), ("25%", 0.25), ("Hidden", 0.0),
        };

        private void EmptyArea_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (e.Handled) return;
            ShowBarMenu(fromKeyboard: false);
        }

        private void ShowBarMenu(bool fromKeyboard)
        {
            HidePreviewImmediate();

            var menu = fromKeyboard
                ? new ContextMenu { Placement = PlacementMode.Right, PlacementTarget = this }
                : new ContextMenu { Placement = PlacementMode.MousePoint };
            var newNote = JtIcons.Menu("New Note", "newNote");
            newNote.Click += (_, __) => NewNoteRequested?.Invoke(this, null);
            menu.Items.Add(newNote);

            var addExisting = JtIcons.Menu("Add Existing Note", "pin");
            var candidates = _allNotes.Where(n => !n.ShowInSidebar)
                .OrderBy(n => n.Title, StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (candidates.Count == 0)
            {
                addExisting.Items.Add(new MenuItem { Header = "(no notes to add)", IsEnabled = false });
            }
            else
            {
                const int maxShown = 40;
                foreach (var note in candidates.Take(maxShown))
                {
                    var mi = new MenuItem { Header = string.IsNullOrWhiteSpace(note.Title) ? "Untitled" : note.Title };
                    mi.Click += (_, __) => AddExistingNoteRequested?.Invoke(this, note);
                    addExisting.Items.Add(mi);
                }
                if (candidates.Count > maxShown)
                    addExisting.Items.Add(new MenuItem
                    {
                        Header = $"…and {candidates.Count - maxShown} more (use the main notes list)",
                        IsEnabled = false,
                    });
            }
            menu.Items.Add(addExisting);

            menu.Items.Add(new Separator());

            var dockSideMenu = new MenuItem { Header = "Dock Side" };
            foreach (var edge in new[] { "Left", "Right" })
            {
                var mi = new MenuItem
                {
                    Header = edge,
                    IsCheckable = true,
                    IsChecked = !_isUndocked && string.Equals(_dockEdge, edge, StringComparison.OrdinalIgnoreCase),
                };
                mi.Click += (_, __) =>
                {
                    bool wasUndocked = _isUndocked;
                    DockToEdge(edge, _dockPosition);
                    DockEdgeChanged?.Invoke(this, edge);
                    if (wasUndocked) UndockedChanged?.Invoke(this, false);
                };
                dockSideMenu.Items.Add(mi);
            }
            menu.Items.Add(dockSideMenu);

            var alignMenu = new MenuItem { Header = "Align" };
            foreach (var position in new[] { "Top", "Middle", "Bottom" })
            {
                var mi = new MenuItem
                {
                    Header = position,
                    IsCheckable = true,
                    IsChecked = !_isUndocked && string.Equals(_dockPosition, position, StringComparison.OrdinalIgnoreCase),
                };
                mi.Click += (_, __) =>
                {
                    bool wasUndocked = _isUndocked;
                    DockToEdge(_dockEdge, position);
                    DockPositionChanged?.Invoke(this, position);
                    if (wasUndocked) UndockedChanged?.Invoke(this, false);
                };
                alignMenu.Items.Add(mi);
            }
            menu.Items.Add(alignMenu);

            var undockItem = new MenuItem
            {
                Header = "Undock (Free-Floating)",
                IsCheckable = true,
                IsChecked = _isUndocked,
                ToolTip = "Drag the sidebar clear of browser/Explorer scrollbars instead of pinning it to a screen edge.",
            };
            undockItem.Click += (_, __) =>
            {
                if (_isUndocked)
                {
                    DockToEdge(_dockEdge, _dockPosition);
                    UndockedChanged?.Invoke(this, false);
                }
                else
                {
                    Undock(Left, Top);
                    UndockedChanged?.Invoke(this, true);
                }
            };
            menu.Items.Add(undockItem);

            if (_isUndocked)
            {
                var moveKb = new MenuItem { Header = "_Move with keyboard", InputGestureText = "Alt+Arrows" };
                moveKb.Click += (_, __) => OldGamers.Wpf.Shared.Controls.KeyboardWindowMover.BeginKeyboardMove(this);
                menu.Items.Add(moveKb);
            }

            menu.Items.Add(new Separator());

            var opacityMenu = new MenuItem { Header = "Bar Opacity" };
            foreach (var preset in _barOpacityPresets)
            {
                var mi = new MenuItem
                {
                    Header = preset.Label,
                    IsCheckable = true,
                    IsChecked = Math.Abs(BgLayer.Opacity - preset.Value) < 0.01,
                };
                mi.Click += (_, __) =>
                {
                    SetBarOpacity(preset.Value);
                    BarOpacityChanged?.Invoke(this, preset.Value);
                };
                opacityMenu.Items.Add(mi);
            }
            menu.Items.Add(opacityMenu);

            var showAddItem = new MenuItem
            {
                Header = "Show + Button",
                IsCheckable = true,
                IsChecked = BtnAddTab.Visibility == Visibility.Visible,
            };
            showAddItem.Click += (_, __) =>
            {
                bool show = BtnAddTab.Visibility != Visibility.Visible;
                SetShowAddButton(show);
                ShowAddButtonChanged?.Invoke(this, show);
            };
            menu.Items.Add(showAddItem);

            menu.IsOpen = true;
        }

        private void BtnAddTab_Click(object sender, RoutedEventArgs e)
        {
            HidePreviewImmediate();
            NewNoteRequested?.Invoke(this, null);
        }

        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape) Hide();
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (!_reallyClosing)
            {
                if (_isEditing) CommitEdit();
                e.Cancel = true;
                Hide();
            }
        }

        /// <summary>Actually closes the window — call only on app shutdown.</summary>
        public void CloseForReal()
        {
            if (_isEditing) CommitEdit();
            _previewOpenTimer?.Stop();
            _previewCloseTimer?.Stop();
            _reallyClosing = true;
            Close();
        }
    }
}
