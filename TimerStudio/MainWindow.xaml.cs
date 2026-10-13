using System.Windows.Input;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Windows;
using System.Drawing;
using System.Windows.Forms;
using System.Windows.Media;
using System.Windows.Threading;

namespace TimerStudio
{
    public partial class MainWindow : Window
    {
        // ── Shared static resources (L-3) ────────────────────────────────────
        // FontFamily objects are expensive to construct — allocating a new one
        // per chip per RefreshCategoryChips() call generates unnecessary GC pressure.
        private static readonly System.Windows.Media.FontFamily _segoeUI =
            new System.Windows.Media.FontFamily("Segoe UI");

        // ── Core ──────────────────────────────────────────────────────────────
        private readonly DispatcherTimer _tick;
        private readonly DispatcherTimer _saveTick;
        private DispatcherTimer          _calendarPollTick;
        private NotifyIcon               _tray;
        private ContextMenuStrip         _trayMenu;
        private bool                     _pinned;
        private bool                     _hidden;
        private bool                     _reallyClosing;

        // ── Email alert dedup: timer IDs that were AlertActive last tick ──────
        private readonly System.Collections.Generic.HashSet<int> _prevAlertIds =
            new System.Collections.Generic.HashSet<int>();

        // ── Tick scratch buffers — reused every 40ms tick instead of new'd (perf) ──
        private readonly List<string> _tickFiring = new List<string>();
        private readonly System.Collections.Generic.HashSet<int> _tickNowAlertIds =
            new System.Collections.Generic.HashSet<int>();
        private readonly List<(TimerModel, TimerCard)> _tickOverlayPairs =
            new List<(TimerModel, TimerCard)>();

        // ── Feature 7: Grouped view ───────────────────────────────────────────
        private bool       _groupedView;
        private const string _catHeaderTag = "CategoryHeader";

        // ── Feature 11: Compact pill window ──────────────────────────────────
        private PillWindow _pillWindow;

        // ─────────────────────────────────────────────────────────────────────
        public MainWindow()
        {
            InitializeComponent();

            // Apply saved window geometry before the window is shown
            AppPrefs.Instance.ApplyTo(this);

            // Apply saved UI scale (Section 1 Cycle UI Size)
            InitUiScale();
            SizeChanged     += (s, e) => SnapshotGeometry();
            LocationChanged += (s, e) => SnapshotGeometry();
            StateChanged    += (s, e) => SnapshotGeometry();

            // Constrain WrapPanel to ScrollViewer viewport
            ScrollView.SizeChanged += (s, e) => ConstrainTimerPanel();
            SizeChanged            += (s, e) => ConstrainTimerPanel();
            ContentRendered        += (s, e) => ConstrainTimerPanel();
            ContentRendered        += (s, e) => WhatsNewDialog.ShowIfNewVersion(this);
            Loaded += (s, e) =>
            {
                ConstrainTimerPanel();
                var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
                t.Tick += (ts, te) => { t.Stop(); ConstrainTimerPanel(); };
                t.Start();
            };

            // Global hotkey: Ctrl+N → Add Timer
            PreviewKeyDown += MainWindow_PreviewKeyDown;

            // ── Standard CommandBindings ──────────────────────────────────────
            CommandBindings.Add(new CommandBinding(
                ApplicationCommands.New,
                (s, e) => { if (OwnedWindows.Count == 0) AddTimer(); e.Handled = true; }));
            CommandBindings.Add(new CommandBinding(
                ApplicationCommands.Delete,
                (s, e) => { DeleteFocusedTimer(); e.Handled = true; },
                (s, e) => { e.CanExecute = GetFocusedCard() != null; }));
            CommandBindings.Add(new CommandBinding(
                ApplicationCommands.Undo,
                (s, e) => { UndoLastDelete(); e.Handled = true; },
                (s, e) => { e.CanExecute = _lastDeletedModel != null; }));
            // WCAG 2.2 SC 3.2.6 — F1 opens About/Help from anywhere in the window
            CommandBindings.Add(new CommandBinding(
                ApplicationCommands.Help,
                (s, e) => { BtnAbout_Click(s, e); e.Handled = true; }));

            // ── Arrow-key card navigation ─────────────────────────────────────
            PreviewKeyDown += NavigationKeyDown;

            BuildTray();

            _tick = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(40) };
            _tick.Tick += Tick_Tick;
            _tick.Start();

            _saveTick = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
            _saveTick.Tick += (s, e) => SaveTimers();
            _saveTick.Start();

            // Calendar auto-pull — interval driven by AppPrefs; starts stopped if disabled
            _calendarPollTick = new DispatcherTimer();
            _calendarPollTick.Tick += (s, e) => RunCalendarPoll();
            ApplyCalendarPollInterval();

            // Feature 7: restore grouped view state
            _groupedView = AppPrefs.Instance.GroupedView;
            BtnGroupedText.Text = _groupedView ? "Flat" : "Grouped";

            LoadTimers();

            // Restore last sort order (if any was previously applied)
            if (AppPrefs.Instance.LastSortMode >= 0)
                SortCards((SortMode)AppPrefs.Instance.LastSortMode);

            // C-7: Subscribe once here (not inside LoadTimers) so it fires even when
            // there are no saved timers (new user). LoadTimers may also be called again
            // on import, which would double-subscribe from inside the loop.
            // F12: Re-fire alerts for Done timers when the OS session is unlocked/resumed.
            Microsoft.Win32.SystemEvents.SessionSwitch += OnSessionSwitch;

            // Feature 11: restore pill window if it was open
            if (AppPrefs.Instance.PillWindowVisible)
            {
                _pillWindow = new PillWindow();
                _pillWindow.Show();
                BtnPillText.Text = "Pill ✓";
            }
        }

        // ── Keyboard shortcuts ────────────────────────────────────────────────
        private void MainWindow_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (!AppPrefs.Instance.HotkeysEnabled) return;
            // Suppress global hotkeys while any child dialog is open
            if (OwnedWindows.Count > 0) return;
            // Ctrl+N — add a new timer
            if (e.Key == System.Windows.Input.Key.N &&
                (System.Windows.Input.Keyboard.Modifiers & System.Windows.Input.ModifierKeys.Control)
                    == System.Windows.Input.ModifierKeys.Control)
            {
                AddTimer();
                e.Handled = true;
            }
        }

        // ── Layout ────────────────────────────────────────────────────────────
        private void ConstrainTimerPanel()
        {
            double vw = ScrollView.ViewportWidth;
            if (vw <= 1) return;
            TimerPanel.Width = vw - 2;
            // Also keep category header widths in sync (F7)
            double hw = vw - 10;
            foreach (UIElement el in TimerPanel.Children)
            {
                if (el is System.Windows.Controls.Border b && _catHeaderTag.Equals(b.Tag)) b.Width = hw;
                else if (el is TimerCard card) ApplyCardWidth(card, TimerPanel.Width);   // TS-11: reflow on resize (ActualWidth is stale until next layout)
            }
        }

        private void SnapshotGeometry()
        {
            AppPrefs.Instance.SnapshotFrom(this);
        }

        // ── Persistence ───────────────────────────────────────────────────────
        private void LoadTimers()
        {
            var saved = TimerPersistence.Load();

            // Seed _nextId so new timers don't collide with loaded ones
            TimerModel.SeedNextId(saved);

            // Seed default categories on first run
            SeedDefaultCategories();

            if (saved.Count == 0)
            {
                AddTimerFromModel(new TimerModel());
                return;
            }
            foreach (var model in saved)
                AddTimerFromModel(model);

            // Fire alerts for DateTime timers whose target passed while the app was closed.
            // Small delay so the window is fully rendered before popups appear.
            var startupAlertTimer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(800)
            };
            startupAlertTimer.Tick += (ts, te) =>
            {
                startupAlertTimer.Stop();
                foreach (UIElement el in TimerPanel.Children)
                {
                    var card = el as TimerCard;
                    if (card != null && card.Model.State == TimerState.Done
                        && card.Model.AlertActive)
                        card.FireMissedAlert();
                }
            };
            startupAlertTimer.Start();

            // (SessionSwitch subscription moved to constructor — see below)

            // Apply saved font scales to all loaded cards
            ApplyTimeFontScale(AppPrefs.Instance.TimeFontScale);
            ApplyTitleFontScale(AppPrefs.Instance.TitleFontScale);
            ApplyEventTimeFontScale(AppPrefs.Instance.EventTimeFontScale);
            ApplyEventTitleFontScale(AppPrefs.Instance.EventTitleFontScale);
            // Overlay has independent scales
            _overlay?.SetTimeFontScale(AppPrefs.Instance.OverlayTimeFontScale);
            _overlay?.SetTitleFontScale(AppPrefs.Instance.OverlayTitleFontScale);

            // Build the initial category chip strip
            RefreshCategoryChips();
            if (_groupedView) RebuildGroupedPanel();

            // Refresh pin buttons and show overlay if any timers were pinned
            bool anyPinned = false;
            foreach (UIElement el in TimerPanel.Children)
            {
                var c = el as TimerCard;
                if (c != null)
                {
                    c.RefreshOverlayPinButton();
                    if (c.Model.ShowInOverlay) anyPinned = true;
                }
            }
            if (anyPinned)
            {
                var pairs2 = new List<(TimerModel, TimerCard)>();
                foreach (UIElement el in TimerPanel.Children)
                { var c = el as TimerCard; if (c != null) pairs2.Add((c.Model, c)); }
                GetOverlay().Refresh(pairs2);
            }
        }

        // TS-02: when NoteStation holds the timers.json lock, the save is deferred
        // and retried every second (max MaxSaveRetries); after that the next 30 s
        // autosave tries again. Nothing is written without the lock except at exit.
        private const int MaxSaveRetries = 10;
        private DispatcherTimer _saveRetryTick;
        private int _saveRetries;

        private void ScheduleSaveRetry()
        {
            if (_saveRetries >= MaxSaveRetries)
            {
                DiagLog.Write("SaveTimers: timers.json lock still busy after retries — waiting for next autosave");
                _saveRetries = 0;
                return;
            }
            _saveRetries++;
            if (_saveRetryTick == null)
            {
                _saveRetryTick = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
                _saveRetryTick.Tick += (s, e) => { _saveRetryTick.Stop(); SaveTimers(); };
            }
            _saveRetryTick.Stop();
            _saveRetryTick.Start();
        }

        public void SaveTimers() => SaveTimers(shutdown: false);

        public void SaveTimers(bool shutdown)
        {
            // L-4: an external writer (NoteStation's task-push integration, or a
            // manual edit) may have touched timers.json since our last write.
            // Merge any entries it added before we blind-overwrite the file from
            // our in-memory state, or the push is silently erased within 30s.
            MergeExternalTimerChanges();

            var models = new List<TimerModel>();
            foreach (UIElement el in TimerPanel.Children)
            {
                var card = el as TimerCard;
                if (card != null) models.Add(card.Model);
            }
            if (TimerPersistence.Save(models, shutdown))
            {
                _saveRetries = 0;
                _saveRetryTick?.Stop();
            }
            else
            {
                ScheduleSaveRetry();
            }
            AppPrefs.Instance.Save();
        }

        /// <summary>
        /// Loads timers.json only if it changed outside our own last save/load,
        /// and adds any timer Ids present there but not currently on screen
        /// (e.g. a timer NoteStation just pushed) as new cards. Existing cards
        /// are left untouched — this is additive-only, not a full reconcile.
        /// </summary>
        private void MergeExternalTimerChanges()
        {
            if (!TimerPersistence.ExternalChangeDetected()) return;

            List<TimerModel> external;
            try { external = TimerPersistence.Load(); }
            catch (Exception ex)
            {
                Debug.WriteLine("[TimerStudio] MergeExternalTimerChanges load failed: " + ex.GetType().Name);
                return;
            }

            var knownIds = new HashSet<int>();
            foreach (UIElement el in TimerPanel.Children)
                if (el is TimerCard c) knownIds.Add(c.Model.Id);

            bool added = false;
            foreach (var ext in external)
            {
                if (knownIds.Contains(ext.Id)) continue;
                AddTimerFromModel(ext);
                added = true;
            }

            if (added)
            {
                TimerModel.SeedNextId(external);
                if (_groupedView) RebuildGroupedPanel();

                // UI #3: a card can appear here while the window is already open
                // (e.g. a NoteStation push) with no visible/audible cue otherwise.
                // LblStatus is AutomationProperties.LiveSetting="Polite" but a plain
                // TextBlock peer doesn't raise the UIA event on its own — refresh the
                // text now (not just on the next 40ms tick) and nudge it explicitly.
                UpdateStatusNow();
                var peer = System.Windows.Automation.Peers.UIElementAutomationPeer.FromElement(LblStatus)
                           ?? System.Windows.Automation.Peers.UIElementAutomationPeer.CreatePeerForElement(LblStatus);
                peer?.RaiseAutomationEvent(System.Windows.Automation.Peers.AutomationEvents.LiveRegionChanged);
            }
        }

        // ── System tray ───────────────────────────────────────────────────────
        private void BuildTray()
        {
            _trayMenu = new ContextMenuStrip();
            _trayMenu.Items.Add(MakeTrayItem("⏱  Restore",    (s, e) => { Show(); Activate(); }));
            _trayMenu.Items.Add(MakeTrayItem("➕  Add Timer",  (s, e) => { Show(); Activate(); AddTimer(); }));
            _trayMenu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
            _trayMenu.Items.Add(MakeTrayItem("ℹ  About",      (s, e) =>
            {
                Show(); Activate();
                new AboutDialog { Owner = this }.ShowDialog();
            }));
            _trayMenu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
            _trayMenu.Items.Add(MakeTrayItem("✕  Exit",       (s, e) =>
            {
                SaveTimers(shutdown: true);
                _reallyClosing = true;
                _tray.Visible  = false;
                System.Windows.Application.Current.Shutdown();
            }));

            _tray = new NotifyIcon
            {
                Text             = "Timer Studio",
                Icon             = LoadTrayIcon(),
                ContextMenuStrip = _trayMenu,
                Visible          = true
            };
            _tray.DoubleClick       += (s, e) => { Show(); Activate(); };
            _tray.BalloonTipClicked += (s, e) => { Show(); Activate(); };
        }

        private static System.Windows.Forms.ToolStripMenuItem MakeTrayItem(
            string text, EventHandler handler)
        {
            var item = new System.Windows.Forms.ToolStripMenuItem(text);
            item.Click += handler;
            return item;
        }

        /// <summary>
        /// Loads app.ico for the system tray NotifyIcon.
        /// Tries the output-directory copy first (fastest), then falls back to
        /// loading from the assembly's embedded resource stream.
        /// </summary>
        private static System.Drawing.Icon LoadTrayIcon()
        {
            try
            {
                // Most reliable: load app.ico from same folder as the exe
                string exePath = System.Reflection.Assembly.GetExecutingAssembly().Location;
                string exeDir  = System.IO.Path.GetDirectoryName(exePath) ?? "";
                string icoPath = System.IO.Path.Combine(exeDir, "app.ico");
                if (System.IO.File.Exists(icoPath))
                    return new System.Drawing.Icon(icoPath, 16, 16);
            }
            catch { }

            try
            {
                // Second: extract the win32 icon resource embedded in the exe itself
                // (set by <ApplicationIcon> in the csproj — always present in the exe)
                string exePath = System.Reflection.Assembly.GetExecutingAssembly().Location;
                return System.Drawing.Icon.ExtractAssociatedIcon(exePath);
            }
            catch { }

            return System.Drawing.SystemIcons.Application;
        }

        // ── Global tick ───────────────────────────────────────────────────────
        private void Tick_Tick(object sender, EventArgs e)
        {
            var firing      = _tickFiring;
            var nowAlertIds = _tickNowAlertIds;
            firing.Clear();
            nowAlertIds.Clear();
            foreach (UIElement el in TimerPanel.Children)
            {
                var card = el as TimerCard;
                if (card == null) continue;
                card.Tick();
                if (card.Model.AlertActive)
                {
                    firing.Add(card.Model.Name);
                    nowAlertIds.Add(card.Model.Id);
                    // Newly alerting this tick — send email (fire-and-forget)
                    if (!_prevAlertIds.Contains(card.Model.Id))
                        EmailAlertService.SendAlertAsync(card.Model.Name, DateTime.Now);
                }
            }
            _prevAlertIds.Clear();
            foreach (int id in nowAlertIds) _prevAlertIds.Add(id);
            UpdateStatus(firing);

            // Feature 11: refresh pill window (only when visible)
            RefreshPillWindow();

            // Update overlay — only build pairs when overlay exists and is visible
            if (_overlay != null && (_overlay.IsVisible || HasPinnedTimers()))
            {
                var pairs = _tickOverlayPairs;
                pairs.Clear();
                foreach (UIElement el in TimerPanel.Children)
                {
                    var c = el as TimerCard;
                    if (c != null) pairs.Add((c.Model, c));
                }
                _overlay.Refresh(pairs);
            }
        }

        private bool HasPinnedTimers()
        {
            foreach (UIElement el in TimerPanel.Children)
                if ((el as TimerCard)?.Model.ShowInOverlay == true) return true;
            return false;
        }

        private int CountTimerCards()
        {
            int n = 0;
            foreach (UIElement el in TimerPanel.Children)
                if (el is TimerCard) n++;
            return n;
        }

        private void UpdateStatus(List<string> firing)
        {
            int count = CountTimerCards();
            UpdateTaskbar();
            if (firing.Count > 0)
            {
                string alertText = count + " timer" + (count != 1 ? "s" : "")
                                 + "  ·  " + firing.Count + " alert"
                                 + (firing.Count > 1 ? "s" : "") + " firing";
                LblStatus.Text       = alertText;
                LblStatus.Foreground = (SolidColorBrush)FindResource("BrushRedText");
                Title = "Timer Studio — " + string.Join(", ", firing.ToArray()) + " fired!";

                if (_hidden)
                {
                    LblAlertBanner.Text    = string.Join(", ", firing.ToArray()) + " — time's up!";
                    AlertBanner.Visibility = Visibility.Visible;
                }
                // NotifyIcon.Text has a 64-char Windows limit — truncate safely
                string trayFiring = "Timer Studio — " + string.Join(", ", firing.ToArray());
                if (trayFiring.Length > 63) trayFiring = trayFiring.Substring(0, 60) + "...";
                _tray.Text = trayFiring;
            }
            else
            {
                LblStatus.Text       = count + " timer" + (count != 1 ? "s" : "");
                LblStatus.Foreground = (SolidColorBrush)FindResource("BrushMuted");
                Title                = "Timer Studio";
                AlertBanner.Visibility = Visibility.Collapsed;
                _tray.Text = "Timer Studio";  // always safe — 12 chars
            }
        }

        // ── Add timer ─────────────────────────────────────────────────────────
        private const int MaxTimersWarning = 10;

        private void AddTimer()
        {
            if (CountTimerCards() >= MaxTimersWarning)
            {
                var result = System.Windows.MessageBox.Show(
                    "You already have " + CountTimerCards() + " timers running.\n\n" +
                    "Adding many timers may affect performance, especially when multiple alerts fire simultaneously.\n\n" +
                    "Do you want to add another timer?",
                    "Performance Note", System.Windows.MessageBoxButton.YesNo,
                    System.Windows.MessageBoxImage.Question);
                if (result != System.Windows.MessageBoxResult.Yes) return;
            }
            AddTimerFromModel(new TimerModel());
            SaveTimers();
        }

        // ── Drag-to-reorder state ─────────────────────────────────────────────
        // ── Overlay window ────────────────────────────────────────────────────
        private OverlayWindow _overlay;

        private OverlayWindow GetOverlay()
        {
            if (_overlay == null)
            {
                _overlay = new OverlayWindow();
                // Don't set Owner — overlay should stay independent
            }
            return _overlay;
        }

        // ── Category system ───────────────────────────────────────────────────
        private string _activeFilter = "";   // "" = All, else the selected category name

        // 8-colour palette for categories (cycling if more than 8)
        private static readonly System.Windows.Media.Color[] _catPalette = new[]
        {
            System.Windows.Media.Color.FromRgb(0x89, 0xB4, 0xFA), // blue
            System.Windows.Media.Color.FromRgb(0xA6, 0xE3, 0xA1), // green
            System.Windows.Media.Color.FromRgb(0xF3, 0x8B, 0xA8), // red
            System.Windows.Media.Color.FromRgb(0xF9, 0xE2, 0xAF), // amber
            System.Windows.Media.Color.FromRgb(0xCB, 0xA6, 0xF7), // mauve
            System.Windows.Media.Color.FromRgb(0x94, 0xE2, 0xD5), // teal
            System.Windows.Media.Color.FromRgb(0xFA, 0xB3, 0x87), // peach
            System.Windows.Media.Color.FromRgb(0x89, 0xDC, 0xEB), // sky
        };

        /// <summary>Returns the colour for a given category name.
        /// Checks user-customised colours in AppPrefs first, falls back to the palette.</summary>
        public static System.Windows.Media.Color CategoryColor(string category)
        {
            if (string.IsNullOrEmpty(category))
                return System.Windows.Media.Color.FromRgb(0x58, 0x5B, 0x70);
            // Check user-customised colour first
            string hex;
            if (AppPrefs.Instance.CategoryColors.TryGetValue(category, out hex)
                && !string.IsNullOrEmpty(hex))
            {
                try { return (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(hex); }
                catch { }
            }
            int idx = Math.Abs(category.GetHashCode()) % _catPalette.Length;
            return _catPalette[idx];
        }

        /// <summary>Sets a custom colour for a category and saves prefs.</summary>
        public static void SetCategoryColor(string category, System.Windows.Media.Color color)
        {
            string hex = string.Format("#{0:X2}{1:X2}{2:X2}", color.R, color.G, color.B);
            AppPrefs.Instance.CategoryColors[category] = hex;
            AppPrefs.Instance.Save();
        }

        // ── Drag-to-reorder state ─────────────────────────────────────────────
        private TimerCard _dragCard;
        private System.Windows.Point _dragStart;

        private const double CardIdealWidth = 430;

        private void ApplyCardWidth(TimerCard card) => ApplyCardWidth(card, TimerPanel.ActualWidth);

        private static void ApplyCardWidth(TimerCard card, double panelWidth) =>
            card.Width = panelWidth > 50
                ? Math.Min(CardIdealWidth, panelWidth - 12)
                : CardIdealWidth;

        private void AddTimerFromModel(TimerModel model)
        {
            var card = new TimerCard(model, this);
            ApplyCardWidth(card);
            card.DeleteRequested += (s, e) =>
            {
                int idx = TimerPanel.Children.IndexOf(card);
                RegisterDelete(card.Model, idx);
                card.Cleanup(); // stop timers + unsub PropertyChanged before removal
                TimerPanel.Children.Remove(card);
                if (_groupedView) RebuildGroupedPanel();
                SaveTimers();
                UpdateStatusNow();
                UpdateDeleteSelectedButton();
                CommandManager.InvalidateRequerySuggested();
            };
            card.SelectionChanged += (s, e) => UpdateDeleteSelectedButton();

            // Drag initiation — drag from the card header (top 32px) to reorder
            card.PreviewMouseLeftButtonDown += (s, e) =>
            {
                var pos = e.GetPosition(card);
                if (pos.Y > 32) return;
                _dragCard  = card;
                _dragStart = e.GetPosition(TimerPanel);
            };
            card.PreviewMouseMove += (s, e) =>
            {
                if (_dragCard != card ||
                    e.LeftButton != System.Windows.Input.MouseButtonState.Pressed) return;
                var pos = e.GetPosition(TimerPanel);
                if (Math.Abs(pos.X - _dragStart.X) < 8 &&
                    Math.Abs(pos.Y - _dragStart.Y) < 8) return;
                ReorderCard(card, pos);
            };
            card.PreviewMouseLeftButtonUp += (s, e) =>
            {
                if (_dragCard == card) { _dragCard = null; SaveTimers(); }
            };

            TimerPanel.Children.Add(card);
            RefreshCategoryChips();
            if (_groupedView) RebuildGroupedPanel();
        }

        private void ReorderCard(TimerCard dragged, System.Windows.Point pointerPos)
        {
            int dragIdx = TimerPanel.Children.IndexOf(dragged);
            if (dragIdx < 0) return;
            int targetIdx = dragIdx;
            double minDist = double.MaxValue;
            for (int i = 0; i < TimerPanel.Children.Count; i++)
            {
                var c = TimerPanel.Children[i] as TimerCard;
                if (c == null || c == dragged) continue;
                try
                {
                    var bounds = c.TransformToAncestor(TimerPanel)
                        .TransformBounds(new Rect(0, 0, c.ActualWidth, c.ActualHeight));
                    double cx   = bounds.Left + bounds.Width  / 2;
                    double cy   = bounds.Top  + bounds.Height / 2;
                    double dist = Math.Sqrt(Math.Pow(pointerPos.X - cx, 2)
                                          + Math.Pow(pointerPos.Y - cy, 2));
                    if (dist < minDist) { minDist = dist; targetIdx = i; }
                }
                catch { }
            }
            if (targetIdx == dragIdx || minDist > 120) return;
            TimerPanel.Children.Remove(dragged);
            if (targetIdx >= TimerPanel.Children.Count)
                TimerPanel.Children.Add(dragged);
            else
                TimerPanel.Children.Insert(targetIdx, dragged);
        }

        /// <summary>
        /// Duplicates an existing timer — copies all settings to a fresh card.
        /// </summary>
        public void DuplicateTimer(TimerModel source)
        {
            var clone = new TimerModel();
            // Copy all user-visible settings
            clone.Name             = source.Name + " (copy)";
            clone.Mode             = source.Mode;
            clone.Hours            = source.Hours;
            clone.Minutes          = source.Minutes;
            clone.Seconds          = source.Seconds;
            clone.TargetDateTime   = source.TargetDateTime;
            clone.AlertColor       = source.AlertColor;
            clone.AlertTitleColor  = source.AlertTitleColor;
            clone.AlertSound       = source.AlertSound;
            clone.AlertStyle       = source.AlertStyle;
            clone.FlashSeconds     = source.FlashSeconds;
            clone.AutoReset        = source.AutoReset;
            clone.AlertFontSize    = source.AlertFontSize;
            clone.LensPosition     = source.LensPosition;
            clone.ReminderEnabled         = source.ReminderEnabled;
            clone.ReminderLeadMinutes     = source.ReminderLeadMinutes;
            clone.ReminderIntervalMinutes = source.ReminderIntervalMinutes;
            clone.Notes            = source.Notes;
            clone.Category         = source.Category;
            AddTimerFromModel(clone);
            SaveTimers();
        }

        /// <summary>
        /// Creates a new Date/Time timer card pre-populated from a CalendarEvent.
        /// Called when the user imports multiple events from the calendar picker.
        /// </summary>
        public void AddTimerFromCalendarEvent(CalendarEvent ev)
        {
            var model = TimerModel.CreateEventTimer();   // Mode = DateTime
            model.Name           = ev.Summary.Length > 1000
                ? ev.Summary.Substring(0, 1000) : ev.Summary;
            model.TargetDateTime = ev.StartLocal;

            // Populate Notes from event body + location (if present)
            var notes = new System.Text.StringBuilder();
            if (!string.IsNullOrWhiteSpace(ev.Description))
                notes.Append(ev.Description.Trim());
            if (!string.IsNullOrWhiteSpace(ev.Location))
            {
                if (notes.Length > 0) notes.AppendLine();
                notes.Append("📍 ").Append(ev.Location.Trim());
            }
            model.Notes = notes.ToString();

            // Apply calendar event defaults from settings
            var p = AppPrefs.Instance;
            model.LensPosition             = (LensPosition)p.CalEvtLensPosition;
            model.FlashSeconds             = p.CalEvtFlashSeconds;
            model.AlertRepeatLimit         = p.CalEvtAlertRepeatLimit;
            model.EscalateAfterSeconds     = p.CalEvtEscalateAfterSeconds;
            model.ExcludeFromPill          = p.CalEvtExcludeFromPill;
            model.ReminderEnabled          = p.CalEvtReminderEnabled;
            model.ReminderLeadMinutes      = p.CalEvtReminderLeadMinutes;
            model.ReminderIntervalMinutes  = p.CalEvtReminderIntervalMinutes;
            model.SendEmailOnReminder      = p.CalEvtSendEmailOnReminder;

            AddTimerFromModel(model);

            // Auto-start: the last child added is this card
            var card = TimerPanel.Children.Count > 0
                ? TimerPanel.Children[TimerPanel.Children.Count - 1] as TimerCard
                : null;
            card?.AutoStartIfDateTime();

            SaveTimers();
        }

        // ── Undo last delete ──────────────────────────────────────────────────
        private TimerModel _lastDeletedModel;
        private int        _lastDeletedIndex;

        /// <summary>Called by TimerCard when user clicks delete — stashes model for undo.</summary>
        public void RegisterDelete(TimerModel model, int index)
        {
            _lastDeletedModel = model;
            _lastDeletedIndex = index;
            CommandManager.InvalidateRequerySuggested();
        }

        private void UndoLastDelete()
        {
            if (_lastDeletedModel == null) return;
            var m = _lastDeletedModel;
            _lastDeletedModel = null;
            var card = new TimerCard(m, this);
            ApplyCardWidth(card);
            int idx = Math.Min(_lastDeletedIndex, TimerPanel.Children.Count);
            TimerPanel.Children.Insert(idx, card);
            if (_groupedView) RebuildGroupedPanel();
            SaveTimers();
            UpdateStatusNow();
            CommandManager.InvalidateRequerySuggested();
        }

        private void DeleteFocusedTimer()
        {
            var card = GetFocusedCard();
            card?.RequestDelete();
        }

        private TimerCard GetFocusedCard()
        {
            var focused = System.Windows.Input.Keyboard.FocusedElement as DependencyObject;
            while (focused != null)
            {
                if (focused is TimerCard tc) return tc;
                focused = System.Windows.Media.VisualTreeHelper.GetParent(focused);
            }
            return null;
        }

        // ── Arrow-key card navigation ─────────────────────────────────────────
        private void NavigationKeyDown(object sender,
            System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key != System.Windows.Input.Key.Left &&
                e.Key != System.Windows.Input.Key.Right &&
                e.Key != System.Windows.Input.Key.Up &&
                e.Key != System.Windows.Input.Key.Down) return;

            // Only intercept at the card level (not inside text boxes etc.)
            var focused = System.Windows.Input.Keyboard.FocusedElement as DependencyObject;
            TimerCard currentCard = null;
            var walk = focused;
            while (walk != null)
            {
                if (walk is TimerCard tc) { currentCard = tc; break; }
                walk = System.Windows.Media.VisualTreeHelper.GetParent(walk);
            }
            if (currentCard == null) return;
            // Don't steal keys from text inputs inside the card
            if (focused is System.Windows.Controls.TextBox ||
                focused is System.Windows.Controls.ComboBox) return;

            int idx = TimerPanel.Children.IndexOf(currentCard);
            if (idx < 0) return;

            int next = idx;
            if (e.Key == System.Windows.Input.Key.Right ||
                e.Key == System.Windows.Input.Key.Down)
                next = idx + 1;
            else
                next = idx - 1;

            if (next < 0 || next >= TimerPanel.Children.Count) return;
            var target = TimerPanel.Children[next] as TimerCard;
            if (target == null) return;

            target.Focus();
            target.BringIntoView();
            e.Handled = true;
        }

        private void UpdateStatusNow()
        {
            var firing = new System.Collections.Generic.List<string>();
            foreach (UIElement el in TimerPanel.Children)
                if ((el as TimerCard)?.Model.AlertActive == true)
                    firing.Add(((TimerCard)el).Model.Name);
            UpdateStatus(firing);
        }

        // ── Section 1: Cycle UI Size ──────────────────────────────────────────
        private static readonly double[] _uiScaleSteps  = { 0.75, 1.0, 1.25, 1.5, 2.0 };
        private static readonly string[] _uiScaleLabels = { "Compact", "Standard", "Medium", "Large", "Xtra Large" };
        private int _uiScaleIndex = 0;

        private void InitUiScale()
        {
            double saved = AppPrefs.Instance.UiScale;
            // Find nearest step
            _uiScaleIndex = 0;
            for (int i = 0; i < _uiScaleSteps.Length; i++)
                if (Math.Abs(_uiScaleSteps[i] - saved) < 0.01) { _uiScaleIndex = i; break; }
            ApplyUiScale();
        }

        private void BtnCycleSize_Click(object sender, RoutedEventArgs e)
        {
            _uiScaleIndex = (_uiScaleIndex + 1) % _uiScaleSteps.Length;
            ApplyUiScale();
            AppPrefs.Instance.UiScale = _uiScaleSteps[_uiScaleIndex];
            AppPrefs.Instance.Save();
        }

        private void ApplyUiScale()
        {
            double s = _uiScaleSteps[_uiScaleIndex];
            RootScale.ScaleX = s;
            RootScale.ScaleY = s;
            BtnCycleSize.Content = $"⊞ {_uiScaleLabels[_uiScaleIndex]} ({_uiScaleSteps[_uiScaleIndex]:0.0#}×)";
        }

        // ── Section 2: Hotkeys dialog ─────────────────────────────────────────
        private void BtnHotkeys_Click(object sender, RoutedEventArgs e)
        {
            new HotkeysDialog { Owner = this }.ShowDialog();
        }

        // ── Toolbar handlers ──────────────────────────────────────────────────
        private void BtnAddTimer_Click(object sender, RoutedEventArgs e) { AddTimer(); }

        // ── Search ────────────────────────────────────────────────────────────
        private string _searchQuery = "";

        private void TxtSearch_TextChanged(object sender,
            System.Windows.Controls.TextChangedEventArgs e)
        {
            _searchQuery = TxtSearch.Text ?? "";
            BtnClearSearch.Visibility = _searchQuery.Length > 0
                ? Visibility.Visible : Visibility.Collapsed;
            ApplyFilter();
        }

        private void BtnClearSearch_Click(object sender, RoutedEventArgs e)
        {
            TxtSearch.Clear();
        }

        // ── Sort ──────────────────────────────────────────────────────────────
        private void BtnSort_Click(object sender, RoutedEventArgs e)
        {
            var menu = new System.Windows.Controls.ContextMenu();

            void AddItem(string header, string iconKey, Action action)
            {
                var item = JtIcons.Menu(header, iconKey);
                item.Click += (s, _) => action();
                menu.Items.Add(item);
            }

            AddItem("By time remaining (ascending)",  "stopwatch", () => SortCards(SortMode.TimeAsc));
            AddItem("By time remaining (descending)", "stopwatch", () => SortCards(SortMode.TimeDesc));
            menu.Items.Add(new System.Windows.Controls.Separator());
            AddItem("By name (A → Z)",                "rename",    () => SortCards(SortMode.NameAsc));
            AddItem("By name (Z → A)",                "rename",    () => SortCards(SortMode.NameDesc));
            menu.Items.Add(new System.Windows.Controls.Separator());
            AddItem("By category",                    "folder",    () => SortCards(SortMode.Category));

            menu.PlacementTarget = BtnSort;
            menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
            menu.IsOpen = true;
        }

        private enum SortMode { TimeAsc, TimeDesc, NameAsc, NameDesc, Category }

        private void SortCards(SortMode mode)
        {
            var cards = new List<TimerCard>();
            foreach (UIElement el in TimerPanel.Children)
                if (el is TimerCard tc) cards.Add(tc);

            switch (mode)
            {
                case SortMode.TimeAsc:
                    cards.Sort((a, b) =>
                        GetSortTime(a.Model).CompareTo(GetSortTime(b.Model)));
                    break;
                case SortMode.TimeDesc:
                    cards.Sort((a, b) =>
                        GetSortTime(b.Model).CompareTo(GetSortTime(a.Model)));
                    break;
                case SortMode.NameAsc:
                    cards.Sort((a, b) =>
                        string.Compare(a.Model.Name, b.Model.Name,
                                       StringComparison.OrdinalIgnoreCase));
                    break;
                case SortMode.NameDesc:
                    cards.Sort((a, b) =>
                        string.Compare(b.Model.Name, a.Model.Name,
                                       StringComparison.OrdinalIgnoreCase));
                    break;
                case SortMode.Category:
                    cards.Sort((a, b) =>
                    {
                        int cat = string.Compare(a.Model.Category ?? "",
                                                  b.Model.Category ?? "",
                                                  StringComparison.OrdinalIgnoreCase);
                        return cat != 0 ? cat :
                            string.Compare(a.Model.Name, b.Model.Name,
                                           StringComparison.OrdinalIgnoreCase);
                    });
                    break;
            }

            TimerPanel.Children.Clear();
            foreach (var card in cards)
                TimerPanel.Children.Add(card);
            if (_groupedView) RebuildGroupedPanel();
            SaveTimers();

            // Persist last-used sort so we can restore it on next launch
            AppPrefs.Instance.LastSortMode = (int)mode;
            AppPrefs.Instance.Save();
        }

        /// <summary>Called from SettingsDialog to apply a default sort immediately. -1 = no-op (manual order).</summary>
        public void ApplySortMode(int mode)
        {
            if (mode >= 0 && mode <= (int)SortMode.Category)
                SortCards((SortMode)mode);
        }

        // ── Mass delete (bulk-select) ───────────────────────────────────────────

        /// <summary>Shows/hides and labels the toolbar "Delete Selected" button based on checked cards.</summary>
        private void UpdateDeleteSelectedButton()
        {
            int count = 0;
            foreach (UIElement el in TimerPanel.Children)
                if (el is TimerCard tc && tc.IsSelected) count++;

            if (count > 0)
            {
                BtnDeleteSelectedText.Text   = $"Delete Selected ({count})";
                BtnDeleteSelected.Visibility = Visibility.Visible;
            }
            else
            {
                BtnDeleteSelected.Visibility = Visibility.Collapsed;
            }
        }

        private void BtnDeleteSelected_Click(object sender, RoutedEventArgs e)
        {
            var selected = new List<TimerCard>();
            foreach (UIElement el in TimerPanel.Children)
                if (el is TimerCard tc && tc.IsSelected) selected.Add(tc);
            if (selected.Count == 0) return;

            var result = System.Windows.MessageBox.Show(this,
                $"Delete {selected.Count} selected timer{(selected.Count == 1 ? "" : "s")}? This cannot be undone.",
                "Delete Selected Timers",
                System.Windows.MessageBoxButton.YesNo,
                System.Windows.MessageBoxImage.Warning);
            if (result != System.Windows.MessageBoxResult.Yes) return;

            foreach (var card in selected)
            {
                int idx = TimerPanel.Children.IndexOf(card);
                RegisterDelete(card.Model, idx); // only the last one is undo-able via Ctrl+Shift+Z
                card.Cleanup();
                TimerPanel.Children.Remove(card);
            }

            if (_groupedView) RebuildGroupedPanel();
            SaveTimers();
            UpdateStatusNow();
            UpdateDeleteSelectedButton();
            CommandManager.InvalidateRequerySuggested();
        }

        private static double GetSortTime(TimerModel m)
        {
            if (m.State == TimerState.Idle)
                return m.Mode == TimerMode.DateTime
                    ? m.GetDateTimeRemaining().TotalSeconds
                    : m.GetConfiguredDuration().TotalSeconds;
            return m.Remaining.TotalSeconds;
        }

        // ── Timer Templates ───────────────────────────────────────────────────
        public void SaveAsTemplate(TimerModel model)
        {
            string name = model.Name ?? "Timer";
            var result = System.Windows.MessageBox.Show(
                "Save \"" + name + "\" as a reusable template?",
                "Save Template",
                System.Windows.MessageBoxButton.YesNo,
                System.Windows.MessageBoxImage.Question);
            if (result != System.Windows.MessageBoxResult.Yes) return;

            var dto = DtoMapper.ToDto(model);
            dto.Id = 0; // templates have no ID
            // Remove existing template with same name
            AppPrefs.Instance.Templates.RemoveAll(t =>
                string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase));
            AppPrefs.Instance.Templates.Add(dto);
            AppPrefs.Instance.Save();
            System.Windows.MessageBox.Show(
                "Template \"" + name + "\" saved.\nApply it from any timer's quick preset menu.",
                "Template Saved",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Information);
        }

        public List<TimerDto> GetTemplates() => AppPrefs.Instance.Templates;

        /// <summary>Refreshes the preset combo on every visible card (call after template add/delete).</summary>
        public void RefreshPresetTemplatesOnAllCards()
        {
            foreach (UIElement el in TimerPanel.Children)
                (el as TimerCard)?.RefreshPresetTemplates();
        }

        public void ApplyTemplate(TimerDto template, TimerModel target)
        {
            var fresh = DtoMapper.FromDto(template);
            target.AlertColor            = fresh.AlertColor;
            target.AlertTitleColor       = fresh.AlertTitleColor;
            target.AlertSound            = fresh.AlertSound;
            target.AlertStyle            = fresh.AlertStyle;
            target.FlashSeconds          = fresh.FlashSeconds;
            target.AutoReset             = fresh.AutoReset;
            target.AlertFontSize         = fresh.AlertFontSize;
            target.LensPosition          = fresh.LensPosition;
            target.CustomSoundPath       = fresh.CustomSoundPath;
            target.CardTint              = fresh.CardTint;
            target.SnoozeDurationMinutes = fresh.SnoozeDurationMinutes;
            if (fresh.Mode == TimerMode.Countdown)
            {
                target.Hours   = fresh.Hours;
                target.Minutes = fresh.Minutes;
                target.Seconds = fresh.Seconds;
            }
            SaveTimers();
        }

        // ── Share Timer ───────────────────────────────────────────────────────
        public void ShareTimer(TimerModel model)
        {
            // Build a simple URI: timerstudio://timer?name=...&mode=...&h=...&m=...&s=...
            var sb = new System.Text.StringBuilder("timerstudio://timer?");
            sb.Append("name=").Append(Uri.EscapeDataString(model.Name ?? ""));
            sb.Append("&mode=").Append((int)model.Mode);
            if (model.Mode == TimerMode.Countdown)
            {
                sb.Append("&h=").Append(model.Hours);
                sb.Append("&m=").Append(model.Minutes);
                sb.Append("&s=").Append(model.Seconds);
            }
            else if (model.Mode == TimerMode.DateTime)
            {
                sb.Append("&target=").Append(Uri.EscapeDataString(
                    model.TargetDateTime.ToString("o")));
            }
            sb.Append("&sound=").Append((int)model.AlertSound);
            sb.Append("&style=").Append((int)model.AlertStyle);

            string uri = sb.ToString();
            System.Windows.Clipboard.SetText(uri);
            System.Windows.MessageBox.Show(
                "Timer config copied to clipboard as a shareable link:\n\n" + uri +
                "\n\nRecipients can paste this into Timer Studio to import the timer.",
                "Timer Shared",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Information);
        }

        private void BtnAbout_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new AboutDialog { Owner = this };
            dlg.TimersImported += (s, e2) =>
            {
                // Reload all timers from the freshly imported file
                TimerPanel.Children.Clear();
                LoadTimers();
            };
            dlg.ShowDialog();
        }

        // L-6: named tab indices — magic literals break silently if tabs are reordered
        private enum SettingsTab { General = 0, Calendar = 1 }

        private void BtnSettings_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new SettingsDialog(this, initialTab: (int)SettingsTab.General) { Owner = this };
            dlg.ShowDialog();
        }

        private void BtnCalendarSettings_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new SettingsDialog(this, initialTab: (int)SettingsTab.Calendar) { Owner = this };
            dlg.PullRequested += (s, _) => RunCalendarPoll(manual: true);
            dlg.ShowDialog();
            // Re-apply poll interval in case it changed
            ApplyCalendarPollInterval();
        }

        // ── Calendar auto-pull ────────────────────────────────────────────────

        /// <summary>
        /// Applies the saved poll interval to the timer and starts/stops it.
        /// Called on startup and whenever the user changes the setting.
        /// </summary>
        public void ApplyCalendarPollInterval()
        {
            var p = AppPrefs.Instance;
            _calendarPollTick.Stop();
            if (p.CalendarAutoPullEnabled && CalendarImporter.IsOutlookAvailable())
            {
                int mins = Math.Max(1, p.CalendarPollIntervalMinutes);
                _calendarPollTick.Interval = TimeSpan.FromMinutes(mins);
                _calendarPollTick.Start();
            }
        }

        /// <summary>
        /// Runs one auto-pull cycle: reads visible Outlook calendars, creates
        /// timer cards for events not already present (deduped by name + start time).
        /// Runs on a background thread to avoid blocking the UI.
        /// </summary>
        private void RunCalendarPoll(bool manual = false)
        {
            // Auto-pull guard: skip when disabled UNLESS the user clicked "Pull Now"
            if (!manual && !AppPrefs.Instance.CalendarAutoPullEnabled) return;
            if (!CalendarImporter.IsOutlookAvailable()) return;

            // Build dedup set from existing timers on the UI thread
            var existing = new System.Collections.Generic.HashSet<string>(
                StringComparer.OrdinalIgnoreCase);
            foreach (UIElement el in TimerPanel.Children)
            {
                var c = el as TimerCard;
                if (c?.Model.Mode == TimerMode.DateTime)
                    existing.Add(MakeEventKey(c.Model.Name, c.Model.TargetDateTime));
            }

            // Snapshot prefs on UI thread before handing off
            var hidden      = new List<string>(AppPrefs.Instance.HiddenCalendarPaths);
            int lookahead   = Math.Max(1, AppPrefs.Instance.CalendarLookaheadDays);
            var excludeKw   = new List<string>(AppPrefs.Instance.CalendarExcludeKeywords ?? new List<string>());
            bool hideBday   = AppPrefs.Instance.CalendarHideBirthdays;
            bool hideHoliday= AppPrefs.Instance.CalendarHideHolidays;

            // STA thread with Dispatcher.Run() message pump required:
            // AccessibleObjectFromWindow(OBJID_NATIVEOM) returns a cross-process COM proxy
            // whose setup requires the calling STA thread to pump messages. Without
            // Dispatcher.Run() the proxy creation fails silently. Work is queued via
            // BeginInvoke so it runs inside the pump; InvokeShutdown() exits when done.
            var sta = new System.Threading.Thread(() =>
            {
                var d = System.Windows.Threading.Dispatcher.CurrentDispatcher;
                d.BeginInvoke(new Action(() =>
                {
                var newEvents = new List<CalendarEvent>();
                try
                {
                    DiagLog.Write("CalendarPoll.STA  thread started, calling ReadAllCalendarsOneShot");
                    DateTime cutoff = DateTime.Now.AddDays(lookahead);
                    var events = CalendarImporter.ReadAllCalendarsOneShot(
                        hidden, maxItems: 600, lookaheadDays: lookahead);
                    DiagLog.Write($"CalendarPoll.STA  ReadAllCalendarsOneShot returned {events.Count} events");

                    foreach (var ev in events)
                    {
                        // 1. Skip events already in the past
                        if (ev.StartLocal <= DateTime.Now) continue;

                        // 2. Skip events beyond the look-ahead window
                        if (ev.StartLocal > cutoff) continue;

                        // 3. Skip birthday / holiday calendars if filters enabled
                        if (!string.IsNullOrEmpty(ev.CalendarName))
                        {
                            if (hideBday && ev.CalendarName.IndexOf("birthday",
                                    StringComparison.OrdinalIgnoreCase) >= 0) continue;
                            if (hideHoliday && (ev.CalendarName.IndexOf("holiday",
                                    StringComparison.OrdinalIgnoreCase) >= 0 ||
                                ev.CalendarName.IndexOf("holidays",
                                    StringComparison.OrdinalIgnoreCase) >= 0)) continue;
                        }

                        // 4. Skip events matching any exclude keyword
                        if (excludeKw != null && excludeKw.Count > 0)
                        {
                            bool excluded = false;
                            foreach (var kw in excludeKw)
                                if (!string.IsNullOrEmpty(kw) &&
                                    ev.Summary.IndexOf(kw, StringComparison.OrdinalIgnoreCase) >= 0)
                                { excluded = true; break; }
                            if (excluded) continue;
                        }

                        string key = MakeEventKey(ev.Summary, ev.StartLocal);
                        if (!existing.Contains(key))
                        {
                            existing.Add(key);
                            newEvents.Add(ev);
                        }
                    }
                }
                catch (Exception ex)
                {
                    DiagLog.WriteException("CalendarPoll.STA", ex);
                }

                // Return to UI thread to create cards, then shut down this STA pump
                Dispatcher.InvokeAsync(() =>
                {
                    foreach (var ev in newEvents)
                        AddTimerFromCalendarEvent(ev);

                    if (newEvents.Count > 0)
                        SaveTimers();

                    AppPrefs.Instance.CalendarLastPulled = DateTime.Now;
                    AppPrefs.Instance.Save();

                    if (newEvents.Count > 0)
                        ShowTrayNotification("Timer Studio — Calendar Pull",
                            newEvents.Count + " new event" +
                            (newEvents.Count == 1 ? "" : "s") +
                            " imported from calendar.", 3000);
                });
                d.InvokeShutdown(); // stop Dispatcher.Run() → thread exits cleanly
                })); // end BeginInvoke action
                System.Windows.Threading.Dispatcher.Run(); // pump until InvokeShutdown()
            });
            sta.SetApartmentState(System.Threading.ApartmentState.STA);
            sta.IsBackground = true;
            sta.Start();
        }

        private static string MakeEventKey(string name, DateTime target)
            => (name ?? "").Trim() + "|" + target.ToString("yyyy-MM-dd HH:mm");

        public void ApplyTimeFontScale(double scale)
        {
            foreach (UIElement el in TimerPanel.Children)
                (el as TimerCard)?.RefreshTimeFont();
            // Overlay has its own independent scale — don't override here
        }

        public void ApplyTitleFontScale(double scale)
        {
            foreach (UIElement el in TimerPanel.Children)
                (el as TimerCard)?.RefreshTitleFont();
        }

        public void ApplyEventTimeFontScale(double scale)
        {
            foreach (UIElement el in TimerPanel.Children)
                (el as TimerCard)?.RefreshTimeFont();
        }

        public void ApplyEventTitleFontScale(double scale)
        {
            foreach (UIElement el in TimerPanel.Children)
                (el as TimerCard)?.RefreshTitleFont();
        }

                private void BtnTheme_Click(object sender, RoutedEventArgs e)
        {
            var ctx = new System.Windows.Controls.ContextMenu
            {
                PlacementTarget = BtnTheme,
                Placement       = System.Windows.Controls.Primitives.PlacementMode.Bottom,
            };
            string cur = AppPrefs.Instance.SelectedTheme ?? "Dark";

            var cats = new (string Cat, (string Key, string Disp)[] Items)[]
            {
                ("Dark", new[] { ("Dark", "Dark"), ("OneDark", "One Dark"), ("GruvboxDark", "Gruvbox Dark"), ("TokyoNight", "Tokyo Night"), ("Nord", "Nord"), ("Dracula", "Dracula"), ("CatppuccinMocha", "Catppuccin Mocha"), ("RosePine", "Rosé Pine"), ("AyuDark", "Ayu Dark"), ("AyuMirage", "Ayu Mirage"), ("Cobalt2", "Cobalt 2"), ("Monokai", "Monokai"), ("SolarizedDark", "Solarized Dark"), ("Kanagawa", "Kanagawa"), ("EverforestDark", "Everforest Dark"), ("Oxocarbon", "Oxocarbon"), ("Palenight", "Palenight"), ("GitHubDark", "GitHub Dark"), ("DraculaPro", "Dracula Pro"), ("Horizon", "Horizon"), ("Abyss", "Abyss"), ("Obsidian", "Obsidian"), ("Panda", "Panda"), ("Monochrome", "Monochrome") }),
                ("Light", new[] { ("Light", "Light"), ("WarmLight", "Warm Light"), ("CatppuccinLatte", "Catppuccin Latte"), ("GitHubLight", "GitHub Light"), ("AyuLight", "Ayu Light"), ("SolarizedLight", "Solarized Light"), ("RosePineDawn", "Rose Pine Dawn"), ("EverforestLight", "Everforest Light"), ("PaperWhite", "Paper White"), ("Sepia", "Sepia"), ("Arctic", "Arctic"), ("Ivory", "Ivory") }),
                ("Contrast", new[] { ("HighContrast", "High Contrast"), ("HighContrastLight", "HC Light"), ("HCBlue", "HC Blue"), ("HCGreen", "HC Green"), ("HCAmber", "HC Amber") }),
                ("Neon", new[] { ("Synthwave", "Synthwave"), ("Cyberpunk", "Cyberpunk"), ("NeonCity", "Neon City"), ("VaporWave", "VaporWave"), ("Outrun", "Outrun"), ("HoloBlue", "Holo Blue"), ("Matrix", "Matrix"), ("NeonPurple", "Neon Purple"), ("LaserGrid", "Laser Grid"), ("ElectricOcean", "Electric Ocean") }),
                ("Nature", new[] { ("Forest", "Forest"), ("Ocean", "Ocean"), ("DesertSand", "Desert Sand"), ("Autumn", "Autumn"), ("MidnightGarden", "Midnight Garden"), ("LavenderField", "Lavender Field"), ("CoralReef", "Coral Reef"), ("Tundra", "Tundra"), ("Volcanic", "Volcanic"), ("Meadow", "Meadow") }),
                ("Retro", new[] { ("C64", "C64"), ("AmberCRT", "Amber CRT"), ("GreenCRT", "Green CRT"), ("IBMBlue", "IBM Blue"), ("AppleII", "Apple II"), ("BIOS", "BIOS"), ("Teletext", "Teletext"), ("TerminalRed", "Terminal Red"), ("VGAPurple", "VGA Purple"), ("ZXSpectrum", "ZX Spectrum") }),
                ("Warm", new[] { ("Ember", "Ember"), ("Rosewood", "Rosewood"), ("GoldenHour", "Golden Hour"), ("Terracotta", "Terracotta"), ("Rust", "Rust"), ("Mahogany", "Mahogany"), ("Cinnamon", "Cinnamon"), ("Wine", "Wine"), ("Copper", "Copper"), ("Sundowner", "Sundowner") }),
                ("Cool", new[] { ("MidnightBlue", "Midnight Blue"), ("DeepSpace", "Deep Space"), ("Glacier", "Glacier"), ("Cobalt", "Cobalt"), ("Sapphire", "Sapphire"), ("Periwinkle", "Periwinkle"), ("ArcticFox", "Arctic Fox"), ("Steel", "Steel"), ("Slate", "Slate"), ("IndigoNight", "Indigo Night") }),
                ("Pastel", new[] { ("CottonCandy", "Cotton Candy"), ("MintCream", "Mint Cream"), ("BabyBlue", "Baby Blue"), ("Peach", "Peach"), ("Lilac", "Lilac"), ("Buttercup", "Buttercup"), ("Blush", "Blush"), ("Seafoam", "Seafoam"), ("Wisteria", "Wisteria"), ("Dusk", "Dusk") }),
                ("Specialty", new[] { ("CoffeeShop", "Coffee Shop"), ("Denim", "Denim"), ("Malachite", "Malachite"), ("SolarFlare", "Solar Flare"), ("Moonstone", "Moonstone"), ("Bubblegum", "Bubblegum") }),
            };

            foreach (var (cat, items) in cats)
            {
                var sub = new System.Windows.Controls.MenuItem
                {
                    Header  = cat,
                    Padding = new Thickness(8, 4, 8, 4),
                };
                foreach (var (k, d) in items)
                {
                    string key = k;
                    bool   act = string.Equals(key, cur, System.StringComparison.OrdinalIgnoreCase);
                    var    mi  = new System.Windows.Controls.MenuItem
                    {
                        Header     = (act ? "✓  " : "    ") + d,
                        FontWeight = act ? FontWeights.SemiBold : FontWeights.Normal,
                        Padding    = new Thickness(8, 6, 24, 6),
                    };
                    mi.Click += (_, __) => { App.SetThemeByName(key);
                        foreach (UIElement el in TimerPanel.Children)
                            (el as TimerCard)?.RefreshUI(); };
                    sub.Items.Add(mi);
                }
                ctx.Items.Add(sub);
            }
            ctx.IsOpen = true;
        }

        private void BtnPin_Click(object sender, RoutedEventArgs e)
        {
            _pinned = !_pinned;
            Topmost = _pinned;
        }

        private void BtnHide_Click(object sender, RoutedEventArgs e)
        {
            _hidden = !_hidden;
            UpdateVisibility();
        }

        private void BtnShowTimers_Click(object sender, RoutedEventArgs e)
        {
            _hidden = false;
            UpdateVisibility();
        }

        private void UpdateVisibility()
        {
            ScrollView.Visibility   = _hidden ? Visibility.Collapsed : Visibility.Visible;
            HiddenBanner.Visibility = _hidden ? Visibility.Visible   : Visibility.Collapsed;
        }

        // ── Minimize to tray on X ─────────────────────────────────────────────
        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (_reallyClosing) return;
            e.Cancel = true;
            AppPrefs.Instance.PillWindowVisible = _pillWindow != null && _pillWindow.IsVisible;
            AppPrefs.Instance.Save();
            SaveTimers();
            Hide();
            ShowTrayNotification("Timer Studio",
                "Still running in the system tray. Double-click to restore.", 2000);
        }

        // ── Cleanup ───────────────────────────────────────────────────────────
        /// <summary>
        /// Called from TimerCard when a reminder fires — shows a tray balloon
        /// so the user is notified even if Timer Studio is hidden behind other windows.
        /// </summary>
        private void UpdateTaskbar()
        {
            if (TaskbarInfo == null) return;
            try
            {
                // Find the running timer with the most progress to show on taskbar
                double bestProgress = 0;
                bool   anyRunning   = false;
                bool   anyAlert     = false;

                foreach (UIElement el in TimerPanel.Children)
                {
                    var card = el as TimerCard;
                    if (card == null) continue;
                    if (card.Model.AlertActive) { anyAlert = true; break; }
                    if (card.Model.State == TimerState.Running)
                    {
                        anyRunning = true;
                        double p = card.Model.Progress;
                        if (p > bestProgress) bestProgress = p;
                    }
                }

                if (anyAlert)
                {
                    TaskbarInfo.ProgressState = System.Windows.Shell.TaskbarItemProgressState.Error;
                    TaskbarInfo.ProgressValue = 1.0;
                }
                else if (anyRunning)
                {
                    TaskbarInfo.ProgressState = System.Windows.Shell.TaskbarItemProgressState.Normal;
                    TaskbarInfo.ProgressValue = bestProgress;
                }
                else
                {
                    TaskbarInfo.ProgressState = System.Windows.Shell.TaskbarItemProgressState.None;
                    TaskbarInfo.ProgressValue = 0;
                }
            }
            catch { }
        }

        // ── Grouped view (Feature 7) ──────────────────────────────────────────

        private void BtnGrouped_Click(object sender, RoutedEventArgs e)
        {
            _groupedView = !_groupedView;
            BtnGroupedText.Text = _groupedView ? "Flat" : "Grouped";
            AppPrefs.Instance.GroupedView = _groupedView;
            AppPrefs.Instance.Save();
            if (_groupedView)
                RebuildGroupedPanel();
            else
                FlattenGroupedPanel();
        }

        /// <summary>
        /// Removes all category header Borders, restores all cards to Visible,
        /// and re-applies the current filter. Called when grouped mode is turned off.
        /// </summary>
        private void FlattenGroupedPanel()
        {
            var cards = new List<TimerCard>();
            foreach (UIElement el in TimerPanel.Children)
                if (el is TimerCard tc) cards.Add(tc);
            TimerPanel.Children.Clear();
            foreach (var c in cards)
            {
                c.Visibility = Visibility.Visible;
                TimerPanel.Children.Add(c);
            }
            ApplyFilter();
        }

        /// <summary>
        /// Rebuilds <see cref="TimerPanel"/> with collapsible category group headers
        /// intercalated between cards. Safe to call whenever panel contents change.
        /// </summary>
        private void RebuildGroupedPanel()
        {
            if (!_groupedView) return;

            // Snapshot all TimerCards preserving their current order
            var cards = new List<TimerCard>();
            foreach (UIElement el in TimerPanel.Children)
                if (el is TimerCard tc) cards.Add(tc);

            TimerPanel.Children.Clear();

            // Group by category
            var catCards   = new Dictionary<string, List<TimerCard>>(StringComparer.OrdinalIgnoreCase);
            var noCategory = new List<TimerCard>();
            foreach (var card in cards)
            {
                string cat = (card.Model.Category ?? "").Trim();
                if (string.IsNullOrEmpty(cat))
                    noCategory.Add(card);
                else
                {
                    if (!catCards.ContainsKey(cat)) catCards[cat] = new List<TimerCard>();
                    catCards[cat].Add(card);
                }
            }

            // Uncategorized cards first — no header, always visible
            foreach (var card in noCategory)
            {
                card.Visibility = Visibility.Visible;
                TimerPanel.Children.Add(card);
            }

            // Categorized groups in user-defined order
            double headerW = TimerPanel.ActualWidth > 10 ? TimerPanel.ActualWidth - 8 : 400;
            var catOrder = GetAllCategories();
            foreach (string cat in catOrder)
            {
                if (!catCards.TryGetValue(cat, out var groupCards) || groupCards.Count == 0)
                    continue;

                bool collapsed = AppPrefs.Instance.CollapsedCategories
                    .Exists(c => string.Equals(c, cat, StringComparison.OrdinalIgnoreCase));

                TimerPanel.Children.Add(MakeCategoryHeader(cat, collapsed, headerW));
                foreach (var card in groupCards)
                {
                    card.Visibility = collapsed ? Visibility.Collapsed : Visibility.Visible;
                    TimerPanel.Children.Add(card);
                }
            }

            ApplyFilter();
        }

        private System.Windows.Controls.Border MakeCategoryHeader(
            string catName, bool collapsed, double width)
        {
            var color      = CategoryColor(catName);
            var colorBrush = new SolidColorBrush(color);

            // DockPanel: right-docked action buttons, left-fill label section
            var dp = new System.Windows.Controls.DockPanel { LastChildFill = true };

            // Right: Pause All
            var btnPause = new System.Windows.Controls.Button
            {
                Style             = TryFindResource("FlatButton") as Style,
                Padding           = new Thickness(8, 2, 8, 2),
                MinHeight         = 44, MinWidth = 44,
                Margin            = new Thickness(2, 0, 0, 0),
                FontSize          = 12,
                VerticalAlignment = VerticalAlignment.Center
            };
            System.Windows.Automation.AutomationProperties.SetName(
                btnPause, "Pause all timers in " + catName);
            btnPause.Content = JtIcons.Label(btnPause, "Pause All", "pause", 16);
            btnPause.Click += (s, e) => PauseAllInCategory(catName);
            System.Windows.Controls.DockPanel.SetDock(btnPause, System.Windows.Controls.Dock.Right);
            dp.Children.Add(btnPause);

            // Right: Start All
            var btnStart = new System.Windows.Controls.Button
            {
                Style             = TryFindResource("FlatButton") as Style,
                Padding           = new Thickness(8, 2, 8, 2),
                MinHeight         = 44, MinWidth = 44,
                Margin            = new Thickness(2, 0, 2, 0),
                FontSize          = 12,
                VerticalAlignment = VerticalAlignment.Center
            };
            System.Windows.Automation.AutomationProperties.SetName(
                btnStart, "Start all timers in " + catName);
            btnStart.Content = JtIcons.Label(btnStart, "Start All", "queueNow", 16);
            btnStart.Click += (s, e) => StartAllInCategory(catName);
            System.Windows.Controls.DockPanel.SetDock(btnStart, System.Windows.Controls.Dock.Right);
            dp.Children.Add(btnStart);

            // Left (fill): collapse button + colour dot + name
            var left = new System.Windows.Controls.StackPanel
            {
                Orientation       = System.Windows.Controls.Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center
            };

            var btnCollapse = new System.Windows.Controls.Button
            {
                Content           = collapsed ? "▶" : "▼",
                Background        = System.Windows.Media.Brushes.Transparent,
                BorderThickness   = new Thickness(0),
                Padding           = new Thickness(6, 0, 6, 0),
                MinHeight         = 44, MinWidth = 44,
                FontSize          = 12,
                Cursor            = System.Windows.Input.Cursors.Hand,
                VerticalAlignment = VerticalAlignment.Center
            };
            System.Windows.Automation.AutomationProperties.SetName(
                btnCollapse, (collapsed ? "Expand " : "Collapse ") + catName + " category");
            btnCollapse.Click += (s, e) => ToggleCategoryCollapse(catName);
            left.Children.Add(btnCollapse);

            // Option C (suite standard): 4px bar in the user's colour + fixed folder icon + name.
            left.Children.Add(new System.Windows.Shapes.Rectangle
            {
                Width             = 4, Height = 20,
                RadiusX           = 2, RadiusY = 2,
                Fill              = colorBrush,
                VerticalAlignment = VerticalAlignment.Center,
                Margin            = new Thickness(0, 0, 6, 0)
            });
            var catIcon = JtIcons.Img("folder", 16);
            catIcon.Margin = new Thickness(0, 0, 6, 0);
            left.Children.Add(catIcon);

            left.Children.Add(new System.Windows.Controls.TextBlock
            {
                Text              = catName,
                FontFamily        = _segoeUI,
                FontSize          = 13,
                FontWeight        = FontWeights.SemiBold,
                Foreground        = FindResource("BrushText") as System.Windows.Media.Brush,
                VerticalAlignment = VerticalAlignment.Center
            });

            dp.Children.Add(left);  // fills remaining space

            return new System.Windows.Controls.Border
            {
                Tag             = _catHeaderTag,
                Background      = FindResource("BrushSurf") as System.Windows.Media.Brush,
                BorderBrush     = new SolidColorBrush(color) { Opacity = 0.6 },
                BorderThickness = new Thickness(3, 0, 0, 1),
                CornerRadius    = new CornerRadius(3, 0, 0, 0),
                Padding         = new Thickness(0, 2, 4, 2),
                Margin          = new Thickness(4, 10, 4, 2),
                Width           = width,
                Child           = dp
            };
        }

        private void ToggleCategoryCollapse(string catName)
        {
            var list = AppPrefs.Instance.CollapsedCategories;
            int idx  = list.FindIndex(
                c => string.Equals(c, catName, StringComparison.OrdinalIgnoreCase));
            if (idx >= 0) list.RemoveAt(idx);
            else          list.Add(catName);
            AppPrefs.Instance.Save();
            RebuildGroupedPanel();
        }

        private void StartAllInCategory(string catName)
        {
            foreach (UIElement el in TimerPanel.Children)
            {
                var card = el as TimerCard;
                if (card != null &&
                    string.Equals(card.Model.Category ?? "", catName,
                                  StringComparison.OrdinalIgnoreCase))
                    card.StartIfIdle();
            }
        }

        private void PauseAllInCategory(string catName)
        {
            foreach (UIElement el in TimerPanel.Children)
            {
                var card = el as TimerCard;
                if (card != null &&
                    string.Equals(card.Model.Category ?? "", catName,
                                  StringComparison.OrdinalIgnoreCase))
                    card.PauseIfRunning();
            }
        }

        // ── Compact pill window (Feature 11) ─────────────────────────────────

        private void BtnPill_Click(object sender, RoutedEventArgs e)
        {
            if (_pillWindow == null)
                _pillWindow = new PillWindow();

            if (_pillWindow.IsVisible)
            {
                _pillWindow.Hide();
                BtnPillText.Text = "Pill";
                AppPrefs.Instance.PillWindowVisible = false;
            }
            else
            {
                _pillWindow.Show();
                BtnPillText.Text = "Pill ✓";
                AppPrefs.Instance.PillWindowVisible = true;
                // Immediate refresh so pill shows current state
                RefreshPillWindow();
            }
            AppPrefs.Instance.Save();
        }

        private void RefreshPillWindow()
        {
            if (_pillWindow == null || !_pillWindow.IsVisible) return;
            var running = new List<(string name, string time)>();
            foreach (UIElement el in TimerPanel.Children)
            {
                var c = el as TimerCard;
                if (c == null) continue;
                if (c.Model.ExcludeFromPill) continue;
                if (c.Model.State == TimerState.Running ||
                    c.Model.State == TimerState.Done && c.Model.AlertActive)
                {
                    string t = c.Model.State == TimerState.Done
                        ? "⏰"
                        : FormatRemaining(c.Model.Remaining);
                    running.Add((c.Model.Name ?? "Timer", t));
                }
            }
            _pillWindow.Refresh(running);
        }

        private static string FormatRemaining(TimeSpan ts)
        {
            if (ts < TimeSpan.Zero) ts = TimeSpan.Zero;
            if (ts.Days >= 1)
                return $"{ts.Days}d {ts.Hours:D2}:{ts.Minutes:D2}:{ts.Seconds:D2}";
            return ts.TotalHours >= 1
                ? ts.ToString(@"h\:mm\:ss")
                : ts.ToString(@"m\:ss");
        }

        // ── F15: Clock window ────────────────────────────────────────────────
        private ClockWindow _clockWindow;
        private void BtnClock_Click(object sender, RoutedEventArgs e)
        {
            if (_clockWindow == null || !_clockWindow.IsLoaded)
                _clockWindow = new ClockWindow();
            if (_clockWindow.IsVisible)
                _clockWindow.Hide();
            else
                _clockWindow.Show();
        }

        // ── F24: Statistics dashboard ─────────────────────────────────────────
        private void BtnStats_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new StatsDashboard { Owner = this };
            dlg.ShowDialog();
        }

        // ── F3: Templates manager ─────────────────────────────────────────────
        private void BtnTemplates_Click(object sender, RoutedEventArgs e)
        {
            var templates = AppPrefs.Instance.Templates;
            if (templates.Count == 0)
            {
                System.Windows.MessageBox.Show(
                    "No saved templates yet.\n\nSave a timer as a template with the \"Save as template\" button on its card.",
                    "Templates", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
                return;
            }

            // Simple management context menu
            var menu = new System.Windows.Controls.ContextMenu();
            foreach (var t in new System.Collections.Generic.List<TimerDto>(templates))
            {
                var tCap = t;
                var item = JtIcons.Menu("Delete — " + tCap.Name, "delete");
                item.Click += (s, _) =>
                {
                    AppPrefs.Instance.Templates.Remove(tCap);
                    AppPrefs.Instance.Save();
                    RefreshPresetTemplatesOnAllCards();
                };
                menu.Items.Add(item);
            }
            menu.Items.Add(new System.Windows.Controls.Separator());
            var exportItem = JtIcons.Menu("Export templates as JSON…", "export");
            exportItem.Click += (s, _) => ExportTemplates();
            menu.Items.Add(exportItem);
            menu.PlacementTarget = BtnTemplates;
            menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
            menu.IsOpen = true;
        }

        private void ExportTemplates()
        {
            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                Title            = "Export Templates",
                Filter           = "JSON files|*.json|All files|*.*",
                FileName         = "TimerStudio_Templates.json",
                DefaultExt = ".json"
            };
            if (dlg.ShowDialog() != true) return;
            try
            {
                var json = System.Text.Json.JsonSerializer.Serialize(
                    AppPrefs.Instance.Templates,
                    TimerStudioJsonContext.Default.ListTimerDto);
                System.IO.File.WriteAllText(dlg.FileName, json, System.Text.Encoding.UTF8);
                System.Windows.MessageBox.Show("Templates exported to:\n" + dlg.FileName,
                    "Export Complete", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show("Export failed: " + ex.Message,
                    "Export Error", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            }
        }

        // ── F6: Bulk operations ───────────────────────────────────────────────
        private void BtnBulkStart_Click(object sender, RoutedEventArgs e)
        {
            foreach (UIElement el in TimerPanel.Children)
                (el as TimerCard)?.StartIfIdle();
        }

        private void BtnBulkPause_Click(object sender, RoutedEventArgs e)
        {
            foreach (UIElement el in TimerPanel.Children)
                (el as TimerCard)?.PauseIfRunning();
        }

        // ── F7: Time-range filter chips ───────────────────────────────────────
        private string _activeTimeRange = "All";

        private void BtnTimeRange_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            if (sender is not System.Windows.Controls.Button btn) return;
            _activeTimeRange = btn.Tag?.ToString() ?? "All";
            UpdateTimeRangeButtonStyles();
            ApplyTimeRangeFilter();
        }

        private void UpdateTimeRangeButtonStyles()
        {
            void Style(System.Windows.Controls.Button b, bool active)
            {
                if (b == null) return;
                b.Content = active ? "✓ " + (b.Tag?.ToString() ?? "") : b.Tag?.ToString() ?? "";
                b.FontWeight = active ? System.Windows.FontWeights.SemiBold : System.Windows.FontWeights.Normal;
            }
            Style(BtnRangeAll,     _activeTimeRange == "All");
            Style(BtnRangeToday,   _activeTimeRange == "Today");
            Style(BtnRangeWeek,    _activeTimeRange == "Week");
            Style(BtnRangeOverdue, _activeTimeRange == "Overdue");
        }

        private void ApplyTimeRangeFilter()
        {
            var now = DateTime.Now;
            var todayEnd = now.Date.AddDays(1);
            var weekEnd  = now.Date.AddDays(7 - (int)now.DayOfWeek + 1);

            foreach (UIElement el in TimerPanel.Children)
            {
                var card = el as TimerCard;
                if (card == null) continue;

                bool visible = _activeTimeRange switch
                {
                    "Today"   => IsTimerInRange(card.Model, now, todayEnd),
                    "Week"    => IsTimerInRange(card.Model, now, weekEnd),
                    "Overdue" => card.Model.State == TimerState.Done || card.Model.AlertActive
                                 || (card.Model.Mode == TimerMode.DateTime
                                     && card.Model.TargetDateTime <= now),
                    _         => true
                };
                card.Visibility = visible ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
            }
        }

        private static bool IsTimerInRange(TimerModel m, DateTime from, DateTime to)
        {
            if (m.State == TimerState.Done || m.AlertActive) return true;
            if (m.Mode == TimerMode.DateTime)
                return m.TargetDateTime >= from && m.TargetDateTime <= to;
            // Countdown/Interval/Pomodoro: show based on remaining time
            if (m.State == TimerState.Running && (m.Mode == TimerMode.Countdown || m.Mode == TimerMode.Interval))
            {
                var eta = DateTime.Now + m.Remaining;
                return eta <= to;
            }
            return true; // idle timers always show
        }

        // ── Category management ──────────────────────────────────────────────

        /// <summary>
        /// Returns the ordered list of all defined categories.
        /// Persisted categories come first (in their saved order); any category
        /// in use by a timer but not yet in the persisted list is appended.
        /// </summary>
        public List<string> GetAllCategories()
        {
            // Start from the persisted list (preserves user-defined order)
            var result = new List<string>(AppPrefs.Instance.Categories);
            var seen   = new System.Collections.Generic.HashSet<string>(
                result, StringComparer.OrdinalIgnoreCase);

            // Add any timer categories not yet in the persisted list
            foreach (UIElement el in TimerPanel.Children)
            {
                var c = el as TimerCard;
                if (c != null && !string.IsNullOrWhiteSpace(c.Model.Category))
                {
                    string cat = c.Model.Category.Trim();
                    if (seen.Add(cat)) result.Add(cat);
                }
            }
            return result;
        }

        public Dictionary<string, int> GetCategoryCounts()
        {
            var d = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (UIElement el in TimerPanel.Children)
            {
                var c = el as TimerCard;
                if (c != null && !string.IsNullOrWhiteSpace(c.Model.Category))
                {
                    string cat = c.Model.Category.Trim();
                    d[cat] = d.TryGetValue(cat, out int n) ? n + 1 : 1;
                }
            }
            return d;
        }

        private static readonly string[] _defaultCategories = { "Personal", "Work", "ToDo" };

        private void SeedDefaultCategories()
        {
            var cats = AppPrefs.Instance.Categories;
            bool changed = false;
            foreach (string def in _defaultCategories)
            {
                if (!cats.Exists(c => string.Equals(c, def, StringComparison.OrdinalIgnoreCase)))
                {
                    cats.Add(def);
                    changed = true;
                }
            }
            if (changed) AppPrefs.Instance.Save();
        }

        public void AddCategory(string name)
        {
            name = name.Trim();
            if (string.IsNullOrEmpty(name)) return;

            // Add to persisted list if not already present
            var existing = AppPrefs.Instance.Categories;
            if (!existing.Exists(c => string.Equals(c, name, StringComparison.OrdinalIgnoreCase)))
            {
                existing.Add(name);
                AppPrefs.Instance.Save();
            }
            RefreshCategoryChips();
        }

        public void RenameCategory(string oldName, string newName)
        {
            newName = newName.Trim();
            if (string.IsNullOrEmpty(newName)) return;

            // Update persisted list
            var cats = AppPrefs.Instance.Categories;
            for (int i = 0; i < cats.Count; i++)
                if (string.Equals(cats[i], oldName, StringComparison.OrdinalIgnoreCase))
                    cats[i] = newName;
            AppPrefs.Instance.Save();

            // Update all timers
            foreach (UIElement el in TimerPanel.Children)
            {
                var c = el as TimerCard;
                if (c != null && string.Equals(c.Model.Category, oldName, StringComparison.OrdinalIgnoreCase))
                {
                    c.Model.Category = newName;
                    c.RefreshCategoryDot();
                }
            }
            if (string.Equals(_activeFilter, oldName, StringComparison.OrdinalIgnoreCase))
                _activeFilter = newName;
            SaveTimers();
            RefreshCategoryChips();
        }

        public void DeleteCategory(string name)
        {
            // Remove from persisted list
            AppPrefs.Instance.Categories.RemoveAll(
                c => string.Equals(c, name, StringComparison.OrdinalIgnoreCase));
            AppPrefs.Instance.Save();

            // Clear from all timers
            foreach (UIElement el in TimerPanel.Children)
            {
                var c = el as TimerCard;
                if (c != null && string.Equals(c.Model.Category, name, StringComparison.OrdinalIgnoreCase))
                {
                    c.Model.Category = "";
                    c.RefreshCategoryDot();
                }
            }
            if (string.Equals(_activeFilter, name, StringComparison.OrdinalIgnoreCase))
                _activeFilter = "";
            SaveTimers();
            RefreshCategoryChips();
            ApplyFilter();
        }

        /// <summary>Refreshes the colour dot on every timer card (after colour change).</summary>
        public void RefreshAllCategoryDots()
        {
            foreach (UIElement el in TimerPanel.Children)
                (el as TimerCard)?.RefreshCategoryDot();
        }

        /// <summary>Rebuilds the filter chip strip from current categories.</summary>
        public void RefreshCategoryChips()
        {
            CategoryChips.Children.Clear();

            // "All" chip
            var allBtn = MakeChip("All", _activeFilter == "");
            allBtn.Click += (s, e) => { _activeFilter = ""; RefreshCategoryChips(); ApplyFilter(); };
            CategoryChips.Children.Add(allBtn);

            // One chip per category
            var cats = GetAllCategories();
            foreach (string cat in cats)
            {
                string captured = cat;
                var color = CategoryColor(cat);
                var chip  = MakeChip(cat, _activeFilter == cat, color);
                chip.Click += (s, e) =>
                {
                    _activeFilter = (_activeFilter == captured) ? "" : captured;
                    RefreshCategoryChips();
                    ApplyFilter();
                };
                CategoryChips.Children.Add(chip);
            }
        }

        private System.Windows.Controls.Button MakeChip(string label, bool active,
            System.Windows.Media.Color? dotColor = null)
        {
            var sp = new System.Windows.Controls.StackPanel
            {
                Orientation = System.Windows.Controls.Orientation.Horizontal
            };

            if (dotColor.HasValue)
            {
                // Option C: 4px bar in the user's colour; the name stays as text.
                sp.Children.Add(new System.Windows.Shapes.Rectangle
                {
                    Width             = 4, Height = 14,
                    RadiusX           = 2, RadiusY = 2,
                    Fill              = new System.Windows.Media.SolidColorBrush(dotColor.Value),
                    Margin            = new Thickness(0, 0, 5, 0),
                    VerticalAlignment = System.Windows.VerticalAlignment.Center
                });
            }

            // Both active and inactive chips show visible text; active is bold + brighter.
            // M-1: "✓ " prefix on active label = non-color-only active indicator (WCAG 1.4.1).
            sp.Children.Add(new System.Windows.Controls.TextBlock
            {
                Text              = active ? "✓ " + label : label,
                FontFamily        = _segoeUI,
                FontSize          = Controls.TypeRamp.Caption,
                FontWeight        = active ? FontWeights.SemiBold : FontWeights.Normal,
                Foreground        = active
                    ? FindResource("BrushOnAccent") as System.Windows.Media.Brush
                    : FindResource("BrushMuted") as System.Windows.Media.Brush,
                VerticalAlignment = System.Windows.VerticalAlignment.Center
            });

            // Active chip: blue accent background + border so active state is shape+color+text.
            // Inactive chip: subtle surface fill + muted border so it reads as clickable.
            var activeAccent  = FindResource("BrushBlue")  as System.Windows.Media.Brush;
            var inactiveFill  = FindResource("BrushSurf2") as System.Windows.Media.Brush;
            var inactiveBorder= FindResource("BrushHint")  as System.Windows.Media.Brush;

            var btn = new System.Windows.Controls.Button
            {
                Content         = sp,
                Background      = active ? activeAccent : inactiveFill,  // M-1: distinct fill, not color-only
                BorderThickness = new Thickness(active ? 1.5 : 1),
                BorderBrush     = active ? activeAccent : inactiveBorder,
                Padding         = new Thickness(10, 3, 10, 3),
                Margin          = new Thickness(0, 0, 5, 0),
                Cursor          = System.Windows.Input.Cursors.Hand
            };
            System.Windows.Automation.AutomationProperties.SetName(btn,
                active ? label + " filter, active" : label + " filter");
            return btn;
        }

        /// <summary>Shows/hides cards based on the active category filter.</summary>
        public void ApplyFilter()
        {
            int visible = 0;
            bool hasSearch = !string.IsNullOrWhiteSpace(_searchQuery);
            foreach (UIElement el in TimerPanel.Children)
            {
                var c = el as TimerCard;
                if (c == null) continue;
                bool catMatch = string.IsNullOrEmpty(_activeFilter)
                    || string.Equals(c.Model.Category ?? "", _activeFilter,
                                     StringComparison.OrdinalIgnoreCase);
                bool searchMatch = !hasSearch
                    || (c.Model.Name  ?? "").IndexOf(_searchQuery, StringComparison.OrdinalIgnoreCase) >= 0
                    || (c.Model.Notes ?? "").IndexOf(_searchQuery, StringComparison.OrdinalIgnoreCase) >= 0;
                bool show = catMatch && searchMatch;
                c.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
                if (show) visible++;
            }
            bool filterActive = !string.IsNullOrEmpty(_activeFilter) || hasSearch;
            if (EmptyFilterPanel != null)
            {
                EmptyFilterPanel.Visibility = (filterActive && visible == 0)
                    ? Visibility.Visible : Visibility.Collapsed;
                if (filterActive && visible == 0 && LblEmptyFilter != null)
                {
                    if (hasSearch)
                        LblEmptyFilter.Text = "No timers matching \"" + _searchQuery + "\"";
                    else
                        LblEmptyFilter.Text = "No timers in \"" + _activeFilter + "\"";
                }
            }
        }

        // ── Category toolbar handlers ─────────────────────────────────────────
        public void BtnManageCategories_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new CategoryManagerDialog(this) { Owner = this };
            dlg.CategoriesChanged += (s, _) => { RefreshCategoryChips(); ApplyFilter(); };
            dlg.ShowDialog();
            RefreshCategoryChips();
            ApplyFilter();
        }

        /// <summary>Toggles a timer's overlay pin and shows/hides the overlay window.</summary>
        public void ToggleOverlayPin(TimerModel model)
        {
            model.ShowInOverlay = !model.ShowInOverlay;
            SaveTimers();

            // Force a refresh so overlay appears/disappears immediately
            var pairs = new List<(TimerModel, TimerCard)>();
            foreach (UIElement el in TimerPanel.Children)
            {
                var c = el as TimerCard;
                if (c != null) pairs.Add((c.Model, c));
            }
            bool anyPinned = false;
            foreach (var p in pairs) if (p.Item1.ShowInOverlay) { anyPinned = true; break; }

            if (anyPinned)
                GetOverlay().Refresh(pairs);
            else if (_overlay != null)
                _overlay.Refresh(pairs); // will auto-hide
        }

        // ── F8: Alert balloon (called from VM when any timer fires) ──────────
        public void ShowAlertBalloon(string timerName)
        {
            try
            {
                string tip = timerName?.Length > 60 ? timerName[..60] + "…" : (timerName ?? "Timer");
                ShowTrayNotification("⏰  " + tip + " — time's up!", "Click the tray icon to dismiss.", 5000);
            }
            catch { }
        }

        public void ShowReminderBalloon(string timerName, string timeRemaining)
        {
            try
            {
                string tip = timerName + ": " + timeRemaining;
                if (tip.Length > 63) tip = tip.Substring(0, 60) + "...";
                ShowTrayNotification("⏰  Timer Reminder", tip, 4000);
            }
            catch { }
        }

        /// <summary>
        /// Shows a system tray notification. Uses the newer BalloonTipClicked
        /// event to restore the window, making it more interactive than the
        /// raw ShowBalloonTip call.
        /// </summary>
        private void ShowTrayNotification(string title, string body, int durationMs)
        {
            try
            {
                _tray.BalloonTipTitle  = title;
                _tray.BalloonTipText   = body;
                _tray.BalloonTipIcon   = System.Windows.Forms.ToolTipIcon.Info;
                _tray.ShowBalloonTip(durationMs);
            }
            catch { }
        }

        public void TrySaveSettingsOnCrash() { SaveTimers(shutdown: true); }

        /// <summary>TS-08: called by <see cref="App.Restart"/> before the process exits.</summary>
        public void PrepareForRestart()
        {
            SaveTimers(shutdown: true);
            _reallyClosing = true;
            if (_tray != null) _tray.Visible = false;
        }

        protected override void OnClosed(EventArgs e)
        {
            Microsoft.Win32.SystemEvents.SessionSwitch -= OnSessionSwitch; // F12
            _tick.Stop();
            _saveTick.Stop();
            _calendarPollTick?.Stop();   // C-3: prevent post-close fire → ObjectDisposedException

            // Stop all card VM timers so the Dispatcher releases its GC roots
            foreach (UIElement el in TimerPanel.Children)
                (el as TimerCard)?.Cleanup();

            _overlay?.CloseForReal();        // M-6: allow overlay to really close on shutdown
            _pillWindow?.CloseForReal();     // H-8: PillWindow cancels Close() by default; force it shut
            if (_tray != null) { _tray.Visible = false; _tray.Dispose(); }
            if (_trayMenu != null) _trayMenu.Dispose();
            base.OnClosed(e);
        }

        // ── F12: Re-fire missed alerts on OS session unlock/resume ────────────
        private DateTime _sessionLockedAt = DateTime.MinValue;

        private void OnSessionSwitch(object sender, Microsoft.Win32.SessionSwitchEventArgs e)
        {
            if (e.Reason == Microsoft.Win32.SessionSwitchReason.SessionLock
             || e.Reason == Microsoft.Win32.SessionSwitchReason.RemoteDisconnect)
            {
                _sessionLockedAt = DateTime.UtcNow;
                return;
            }

            if (e.Reason != Microsoft.Win32.SessionSwitchReason.SessionUnlock
             && e.Reason != Microsoft.Win32.SessionSwitchReason.RemoteConnect)
                return;

            if (_sessionLockedAt == DateTime.MinValue) return;
            DateTime lockedAt = _sessionLockedAt;
            _sessionLockedAt  = DateTime.MinValue;

            // Marshal to UI thread — SessionSwitch fires on a background thread
            Dispatcher.BeginInvoke(new Action(() =>
            {
                foreach (UIElement el in TimerPanel.Children)
                {
                    var card = el as TimerCard;
                    if (card == null) continue;
                    // Re-fire if: Done + AlertActive + alert fired DURING the lock period
                    if (card.Model.State == TimerState.Done
                     && card.Model.AlertActive
                     && card.Model.AlertFiredAt >= lockedAt)
                        card.FireMissedAlert();
                }
            }));
        }
    }
}
