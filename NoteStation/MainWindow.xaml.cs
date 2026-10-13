using System;
using System.Collections.Generic;
using System.Threading;
using System.Collections.ObjectModel;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;

namespace NoteStation
{
    public partial class MainWindow : Window
    {
        // ── Routed Commands ───────────────────────────────────────────────────
        public static readonly RoutedCommand CycleSizeCommand         = new RoutedCommand("CycleSize",         typeof(MainWindow));
        public static readonly RoutedCommand ToggleClipOverlayCommand = new RoutedCommand("ToggleClipOverlay", typeof(MainWindow));
        public static readonly RoutedCommand ToggleTaskOverlayCommand = new RoutedCommand("ToggleTaskOverlay", typeof(MainWindow));
        public static readonly RoutedCommand ToggleNotesSidebarCommand = new RoutedCommand("ToggleNotesSidebar", typeof(MainWindow));

        // ── UI size steps ─────────────────────────────────────────────────────
        private static readonly (double scale, string label)[] _sizes = new[]
        {
            (1.0, "Standard"),
            (1.25, "Medium"),
            (1.5, "Large"),
            (2.0, "XL"),
        };
        private int _sizeIdx = 0;

        // ── Active tab ────────────────────────────────────────────────────────
        private string _activeTab = "Clipboard";

        // ── Clipboard ─────────────────────────────────────────────────────────
        private ObservableCollection<ClipboardItem> _clips = new ObservableCollection<ClipboardItem>();
        private int _nextClipId = 1;
        private int _clipIdx    = 0;
        private ClipboardMonitor _clipMonitor;
        private bool _suppressClipCapture;
        private string _pendingSourceApp = "";
        private ClipboardItem _lastDeletedClip;

        // ── Recently Deleted ─────────────────────────────────────────────────
        private List<ClipboardItem> _deletedClips = new List<ClipboardItem>();
        private List<StickyNote>    _deletedNotes = new List<StickyNote>();
        private List<TaskItem>      _deletedTasks = new List<TaskItem>();

        // ── Sticky notes ──────────────────────────────────────────────────────
        private List<StickyNote> _notes = new List<StickyNote>();
        private Dictionary<int, StickyNoteWindow> _noteWindows = new Dictionary<int, StickyNoteWindow>();
        private int _nextNoteId = 1;
        private ObservableCollection<StickyNote> _notesView = new ObservableCollection<StickyNote>();
        private int _noteSortMode = 0;  // 0=Modified 1=Created 2=Title 3=Manual
        private static readonly string[] _noteSortLabels = { "Modified", "Created", "Title", "Manual" };

        // ── Templates ─────────────────────────────────────────────────────────
        private System.Collections.ObjectModel.ObservableCollection<TemplateItem> _templates = new();
        private int _nextTemplateId = 1;

        // ── Tasks ─────────────────────────────────────────────────────────────
        private ObservableCollection<TaskItem> _tasks = new ObservableCollection<TaskItem>();
        private int    _nextTaskId        = 1;
        private TaskItem _dragItem;
        private Border   _dragCard;
        private int    _taskSortMode      = 0;   // 0=Default 1=DueDate 2=Title 3=Starred
        private string _taskDueDateFilter = "All"; // All|Overdue|Today|Week
        private int    _filteredCount     = -1;   // -1 = no active filter; ≥0 = filtered subset count
        private TaskItem _pendingDeleteTask;
        private System.Windows.Threading.DispatcherTimer _deleteTimer;

        // ── Clipboard save debounce ───────────────────────────────────────────
        private System.Windows.Threading.DispatcherTimer _clipSaveTimer;

        // ── Note reminder timer ───────────────────────────────────────────────
        private System.Windows.Threading.DispatcherTimer _reminderTimer;

        // ── Clipboard capture debounce (prevents double-fire from some apps) ──
        private System.Windows.Threading.DispatcherTimer _clipCaptureTimer;

        // ── D1-6: History warning ─────────────────────────────────────────────
        private bool _historyWarningSent;


        // ── Mouse hook ────────────────────────────────────────────────────────
        private MouseButtonHook _mouseHook;

        // ── Preset buttons ────────────────────────────────────────────────────
        private Button[] _presetBtns;
        private TextBlock[] _presetTexts;

        // ── Category filter ───────────────────────────────────────────────────
        private string _activeCategory = "";

        // ── Folder trees ──────────────────────────────────────────────────────
        private ObservableCollection<FolderNode> _clipFolderNodes  = new();
        private ObservableCollection<FolderNode> _noteFolderNodes  = new();
        private ObservableCollection<FolderNode> _taskFolderNodes  = new();
        private FolderNode _selectedClipNode;
        private FolderNode _selectedNoteNode;
        private FolderNode _selectedTaskNode;
        private int _nextFolderId = 1;
        private readonly List<FolderNode> _multiFolderSelection = new List<FolderNode>();

        // Per-tab folder-name filter text (lower-cased). Empty = show all.
        private readonly Dictionary<string, string> _folderFilter =
            new() { { "Clipboard", "" }, { "Notes", "" }, { "Tasks", "" } };

        // ── Drag-drop state (folder tree) ─────────────────────────────────────
        private System.Windows.Point _dragStart;
        private object _folderDragItem;
        private FolderNode _draggedFolderNode;
        private int _clipSortMode = 0;  // 0=Captured(date) 1=Title
        private readonly string[] _clipSortLabels = { "Date", "Title" };

        // ── Tray ──────────────────────────────────────────────────────────────
        private System.Windows.Forms.NotifyIcon _trayIcon;
        private bool _forceExit;
        private bool _suppressTrayMinimize;

        // ── Global hotkey ─────────────────────────────────────────────────────
        private HwndSource    _hwndSource;
        private GlobalHotkey  _showHideHotkey;
        private GlobalHotkey  _quickCaptureHotkey;
        private GlobalHotkey  _commandPaletteHotkey;
        private GlobalHotkey  _pasteFromKbHotkey;

        // ── Quick Capture + Command Palette windows ───────────────────────────
        private QuickCaptureWindow  _quickCapture;
        private CommandPaletteWindow _commandPalette;

        // ── Overlay windows ───────────────────────────────────────────────────
        private ClipOverlayWindow _clipOverlay;
        private TaskOverlayWindow _taskOverlay;
        private NotesSidebarWindow _notesSidebar;

        // ── Window-attach monitor ─────────────────────────────────────────────
        private System.Windows.Threading.DispatcherTimer _attachMonitorTimer;

        // ── F8: Regex clip search ─────────────────────────────────────────────
        private bool _clipSearchIsRegex = false;

        // ── F16: Calendar ─────────────────────────────────────────────────────
        private readonly CalendarViewModel _calendarVm = new CalendarViewModel();

        // ── F14: Memoboard drag state ─────────────────────────────────────────
        private System.Windows.Point _memoDragStart;
        private Border _memoDragCard;
        private StickyNote _memoDragNote;

        // ══════════════════════════════════════════════════════════════════════
        //  Init
        // ══════════════════════════════════════════════════════════════════════
        public MainWindow()
        {
            InitializeComponent();
            _presetBtns   = new[] { BtnPreset1, BtnPreset2, BtnPreset3, BtnPreset4, BtnPreset5 };
            _presetTexts  = new[] { TxtPreset1, TxtPreset2, TxtPreset3, TxtPreset4, TxtPreset5 };
        }

        private async void Window_Loaded(object sender, RoutedEventArgs e)
        {
            var p = AppPrefs.Instance;
            p.ApplyTo(this);

            // Scale
            SetScale(p.UiScale);

            // Content font sizes + folder accent bar
            App.SetContentFontSizes(p.ClipFontSize, p.NoteFontSize, p.TaskFontSize, p.FolderFontSize);
            App.SetFolderAccentWidth(p.FolderAccentBarWidth);
            App.SetAppFontFamily(p.AppFontFamily);

            // Migrate legacy single-file storage to per-item files (runs once, no-op after)
            DataStore.MigrateIfNeeded();

            // Load data asynchronously to keep UI responsive on startup
            var (savedClips, loadedNotes, savedTasks) = await System.Threading.Tasks.Task.Run(() =>
            {
                var clips = DataStore.LoadClipboard();
                var notes = DataStore.LoadNotes();
                var tasks = DataStore.LoadTasks();
                return (clips, notes, tasks);
            });

            foreach (var c in savedClips)
            {
                if (c.Id >= _nextClipId) _nextClipId = c.Id + 1;
                if (c.IsDeleted) _deletedClips.Add(c);
                else _clips.Add(c);
            }
            PruneExpiredClips();
            DataStore.PurgeDeleted(AppPrefs.Instance.DeletedRetentionDays, _deletedClips, _deletedNotes, _deletedTasks);
            ListClipboard.ItemsSource = _clips;

            // Auto-refresh sidebar counts whenever a collection changes
            _clips.CollectionChanged     += (_, __) => RebuildFolderTreeDebounced("Clipboard");
            _notesView.CollectionChanged += (_, __) => RebuildFolderTreeDebounced("Notes");
            _tasks.CollectionChanged     += (_, __) => RebuildFolderTreeDebounced("Tasks");

            _notes = loadedNotes.Where(n => !n.IsDeleted).ToList();
            _deletedNotes = loadedNotes.Where(n => n.IsDeleted).ToList();
            foreach (var n in _notes)
            {
                _notesView.Add(n);
                if (n.Id >= _nextNoteId) _nextNoteId = n.Id + 1;
            }
            foreach (var n in _deletedNotes)
                if (n.Id >= _nextNoteId) _nextNoteId = n.Id + 1;
            ListNotes.ItemsSource = _notesView;
            ApplyNoteViewMode();

            foreach (var t in savedTasks)
            {
                if (t.Id >= _nextTaskId) _nextTaskId = t.Id + 1;
                if (t.IsDeleted) _deletedTasks.Add(t);
                else _tasks.Add(t);
            }
            TaskList.ItemsSource = _tasks;

            var loadedTemplates = DataStore.LoadTemplates();
            foreach (var t in loadedTemplates) _templates.Add(t);
            _nextTemplateId = _templates.Count > 0 ? _templates.Max(t => t.Id) + 1 : 1;

            UpdateTrayBadge();

            // Clipboard monitor
            _clipMonitor = new ClipboardMonitor();
            _clipMonitor.ClipboardChanged += OnClipboardChanged;
            _clipMonitor.Attach(this);

            // Mouse hook
            _mouseHook = new MouseButtonHook
            {
                EnableXButton1            = p.MousePreviousClip == 1,
                EnableXButton2            = p.MouseNextClip     == 2,
                EnableScroll              = p.MouseScrollCycle,
                ScrollRequiresModifier    = p.MouseScrollRequiresModifier,
            };
            _mouseHook.XButton1Pressed += (s, _) => Dispatcher.Invoke(ClipboardPrev);
            _mouseHook.XButton2Pressed += (s, _) => Dispatcher.Invoke(ClipboardNext);
            _mouseHook.ScrollUp        += (s, _) => Dispatcher.Invoke(ClipboardPrev);
            _mouseHook.ScrollDown      += (s, _) => Dispatcher.Invoke(ClipboardNext);
            _mouseHook.Install();

            // Init folder ID counter (must precede any folder operation)
            var allFolders = AppPrefs.Instance.ClipboardFolders
                .Concat(AppPrefs.Instance.NoteFolders)
                .Concat(AppPrefs.Instance.TaskFolders);
            foreach (var f in allFolders)
                if (f.Id >= _nextFolderId) _nextFolderId = f.Id + 1;

            // Build only the active tab's tree, then show it — fast first paint.
            if (p.ActiveTab is "Clipboard" or "Notes" or "Tasks")
                BuildFolderTree(p.ActiveTab);
            SwitchTab(p.ActiveTab);

            UpdateStatus();
            UpdatePresets();
            RefreshCategoryChips();

            // Defer the heavy, non-critical startup work (the other two folder trees
            // and reopening note windows) to a background dispatch so the main window
            // paints immediately instead of blocking on it.
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (_activeTab != "Clipboard") BuildFolderTree("Clipboard");
                if (_activeTab != "Notes")     BuildFolderTree("Notes");
                if (_activeTab != "Tasks")     BuildFolderTree("Tasks");

                // Reopen notes that were open last session OR are flagged to always show.
                // A note with a PIN opens blurred (locked) so secrets aren't exposed at launch.
                foreach (var note in _notes.Where(n => n.IsVisible || n.ShowOnStartup))
                {
                    if (!string.IsNullOrEmpty(note.PinHash)) note.IsLocked = true;
                    OpenNoteWindow(note);
                }

                // Optionally show the desktop overlays at startup.
                if (AppPrefs.Instance.ShowClipboardOverlayAtStartup) BtnClipOverlay_Click(null, null);
                if (AppPrefs.Instance.ShowTaskOverlayAtStartup)      BtnTaskOverlay_Click(null, null);
                if (AppPrefs.Instance.ShowNotesSidebarAtStartup)     BtnNotesSidebar_Click(null, null);
            }), System.Windows.Threading.DispatcherPriority.Background);

            // Restore folder panel widths
            ClipTreeCol.Width = new GridLength(p.ClipFolderPanelWidth);
            NoteTreeCol.Width = new GridLength(p.NoteFolderPanelWidth);
            TaskTreeCol.Width = new GridLength(p.TaskFolderPanelWidth);

            // Window-level mouse handlers so drag continues when leaving list bounds
            this.AddHandler(UIElement.MouseMoveEvent,
                new MouseEventHandler(Window_MouseMove), handledEventsToo: true);
            this.AddHandler(UIElement.PreviewMouseLeftButtonUpEvent,
                new MouseButtonEventHandler((_, __) => { _folderDragItem = null; _draggedFolderNode = null; }), handledEventsToo: true);

            // Tray icon
            InitTrayIcon();
            UpdateTrayBadge();
            StartReminderTimer();

            // StartMinimized
            if (p.StartMinimized)
            {
                _ = Dispatcher.BeginInvoke(new Action(() =>
                {
                    WindowState = WindowState.Minimized;
                    if (p.MinimizeToTray) HideToTray();
                }), System.Windows.Threading.DispatcherPriority.Loaded);
            }

            // Start window-attach monitor
            StartAttachMonitor();
        }

        // ══════════════════════════════════════════════════════════════════════
        //  Section 1: Config buttons
        // ══════════════════════════════════════════════════════════════════════
        private void BtnHotkeys_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new HotkeysDialog { Owner = this };
            dlg.ShowDialog();
        }

        private void BtnSettings_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new AppSettingsDialog { Owner = this };
            dlg.ShowDialog();
            if (dlg.Saved) ApplySettings();
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
                    mi.Click += (_, __) => { App.SetTheme(key); AppPrefs.Instance.SelectedTheme = key; AppPrefs.Instance.DeferredSave(); };
                    sub.Items.Add(mi);
                }
                ctx.Items.Add(sub);
            }
            ctx.IsOpen = true;
        }

        private void BtnAbout_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new AboutDialog { Owner = this };
            dlg.ShowDialog();
        }

        private void CmdAbout_Executed(object sender, ExecutedRoutedEventArgs e) => BtnAbout_Click(sender, e);

        // ── Overlay toggles ───────────────────────────────────────────────────
        private void BtnClipOverlay_Click(object sender, RoutedEventArgs e)
        {
            if (_clipOverlay == null || !_clipOverlay.IsLoaded)
            {
                _clipOverlay = new ClipOverlayWindow();
                _clipOverlay.PasteRequested += (_, clip) =>
                {
                    // Suppress capture so re-loading an existing clip doesn't create a duplicate entry
                    _suppressClipCapture = true;
                    try
                    {
                        if (clip.IsImage && System.IO.File.Exists(clip.ImagePath))
                        {
                            var bmp = NoteStation.Core.ClipVault.LoadImage(clip.ImagePath);
                            Clipboard.SetImage(bmp);
                            if (AppPrefs.Instance.AutoPasteOnOverlayClick)
                                PasteImage(clip.ImagePath);
                        }
                        else if (!string.IsNullOrEmpty(clip.Text))
                        {
                            Clipboard.SetText(clip.Text);
                            if (AppPrefs.Instance.AutoPasteOnOverlayClick)
                                PasteText(clip.Text);
                        }
                    }
                    finally
                    {
                        if (!AppPrefs.Instance.AutoPasteOnOverlayClick)
                        {
                            // Re-enable after debounce window (300 ms > ClipboardMonitor's 200 ms debounce)
                            var t = new System.Windows.Threading.DispatcherTimer
                                { Interval = TimeSpan.FromMilliseconds(300) };
                            t.Tick += (__, ___) => { t.Stop(); _suppressClipCapture = false; };
                            t.Start();
                        }
                        // Auto-paste path: PasteText/PasteImage manage _suppressClipCapture themselves
                    }
                };
                _clipOverlay.SendToNoteRequested += (_, clip) =>
                {
                    string title   = !string.IsNullOrWhiteSpace(clip.Title) ? clip.Title
                                   : clip.IsImage ? "Image Clip" : "Clip Note";
                    string content = clip.IsImage ? $"[Image: {clip.ImagePath}]" : clip.Text ?? "";
                    CreateNote(title, content);
                };
                _clipOverlay.Show();
                _clipOverlay.Refresh(_clips, _templates);
            }
            else if (_clipOverlay.IsVisible)
            {
                _clipOverlay.Hide();
            }
            else
            {
                _clipOverlay.Show();
                _clipOverlay.Refresh(_clips, _templates);
            }
        }

        private void BtnTaskOverlay_Click(object sender, RoutedEventArgs e)
        {
            if (_taskOverlay == null || !_taskOverlay.IsLoaded)
            {
                _taskOverlay = new TaskOverlayWindow();
                _taskOverlay.TaskCompletionToggled += (_, task) =>
                {
                    CompleteTask(task, task.IsCompleted);
                    _taskOverlay?.Refresh(_tasks);
                };
                _taskOverlay.Show();
                _taskOverlay.Refresh(_tasks);
            }
            else if (_taskOverlay.IsVisible)
            {
                _taskOverlay.Hide();
            }
            else
            {
                _taskOverlay.Show();
                _taskOverlay.Refresh(_tasks);
            }
        }

        private void BtnMoreMenu_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.ContextMenu != null)
            {
                btn.ContextMenu.PlacementTarget = btn;
                btn.ContextMenu.IsOpen = true;
            }
        }

        private void BtnCycleSize_Click(object sender, RoutedEventArgs e)
        {
            _sizeIdx = (_sizeIdx + 1) % _sizes.Length;
            var (scale, label) = _sizes[_sizeIdx];
            SetScale(scale);
            AppPrefs.Instance.UiScale = scale;
            AppPrefs.Instance.DeferredSave();
        }

        private void SetScale(double scale)
        {
            // Find index
            for (int i = 0; i < _sizes.Length; i++)
                if (Math.Abs(_sizes[i].scale - scale) < 0.01) { _sizeIdx = i; break; }

            RootScale.ScaleX = scale;
            RootScale.ScaleY = scale;
            var (s, label) = _sizes[_sizeIdx];
            BtnCycleSize.Header = $"⊞ {label} ({s:0.0#}×)";

            // Propagate scale to all open note windows
            foreach (var nw in _noteWindows.Values)
                nw.UpdateScale(scale);
        }

        // ══════════════════════════════════════════════════════════════════════
        //  Section 2: NavRow
        // ══════════════════════════════════════════════════════════════════════
        private void BtnTabClipboard_Click(object sender, RoutedEventArgs e) => SwitchTab("Clipboard");
        private void BtnTabNotes_Click(object sender, RoutedEventArgs e)     => SwitchTab("Notes");
        private void BtnTabTasks_Click(object sender, RoutedEventArgs e)     => SwitchTab("Tasks");
        private void SwitchTab(string tab)
        {
            ClearFolderMultiSelect();
            _activeTab = tab;
            AppPrefs.Instance.ActiveTab = tab;

            // Panels
            PanelClipboard.Visibility     = tab == "Clipboard" ? Visibility.Visible : Visibility.Collapsed;
            PanelNotes.Visibility         = tab == "Notes"     ? Visibility.Visible : Visibility.Collapsed;
            PanelTasks.Visibility         = tab == "Tasks"     ? Visibility.Visible : Visibility.Collapsed;

            // Toolbars
            PanelClipToolbar.Visibility   = tab == "Clipboard" ? Visibility.Visible : Visibility.Collapsed;
            PanelNotesToolbar.Visibility  = tab == "Notes"     ? Visibility.Visible : Visibility.Collapsed;
            PanelTasksToolbar.Visibility  = tab == "Tasks"     ? Visibility.Visible : Visibility.Collapsed;

            // Nav pill active state (gold fill)
            SetNavActive(BtnTabClipboard, tab == "Clipboard");
            SetNavActive(BtnTabNotes,     tab == "Notes");
            SetNavActive(BtnTabTasks,     tab == "Tasks");

            // Search visible on all tabs
            TxtSearch.Visibility = Visibility.Visible;

            UpdateStatus();
            ApplyFilter();
        }

        private void SetNavActive(Button btn, bool active)
        {
            // Selected-tab fill is BrushBlue, not FocusRingBrush — the gold ring stays
            // the ONE signal for keyboard focus. Reusing it here made a focused active
            // tab visually indistinguishable from "just selected, no focus".
            btn.Background = active
                ? App.GetBrush("BrushBlue")
                : new SolidColorBrush(Colors.Transparent);
            btn.Foreground = active
                ? App.GetBrush("BrushOnAccent")
                : App.GetBrush("BrushText");
            // Selected tab exposed to UIA (not colour-only; fill vs. no fill is the visual shape cue)
            AutomationProperties.SetItemStatus(btn, active ? "Selected" : "");
        }

        // ══════════════════════════════════════════════════════════════════════
        //  Clipboard
        // ══════════════════════════════════════════════════════════════════════
        private static readonly string _clipImagesDir =
            System.IO.Path.Combine(App.AppDataDir, "clipimages");

        private void OnClipboardChanged(object sender, EventArgs e)
        {
            if (!AppPrefs.Instance.ClipboardAutoCapture) return;
            if (_suppressClipCapture) return;

            // Snapshot foreground window NOW — focus may shift by the time the debounce fires.
            _pendingSourceApp = NativeMethods.GetForegroundWindowTitle();

            // Debounce: some apps (Chrome, Office) fire WM_CLIPBOARDUPDATE 2× per copy.
            // Restart a 200 ms timer; only the final tick actually captures.
            _clipCaptureTimer?.Stop();
            _clipCaptureTimer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(200)
            };
            _clipCaptureTimer.Tick += (_, __) => { _clipCaptureTimer.Stop(); CaptureClipboardNow(); };
            _clipCaptureTimer.Start();
        }

        // Windows convention (honoured by Windows Clipboard History and
        // well-behaved third-party clipboard managers): a copier sets this
        // synthetic clipboard format to signal "don't record this". Password
        // managers (1Password, Bitwarden, KeePass, LastPass, Windows Credential
        // Manager copies, etc.) set it on every password copy. Without this
        // check we were capturing and persisting secrets to clipboard.json in
        // plaintext. See:
        // https://learn.microsoft.com/windows/win32/dataxchg/clipboard-history
        // Also honours CanIncludeInClipboardHistory=0 and "Clipboard Viewer Ignore" (KeePass,
        // 1Password, Bitwarden) — see Core/ClipboardPrivacy.cs.
        private static bool IsClipboardExcludedFromHistory() =>
            NoteStation.Core.ClipboardPrivacy.IsCurrentClipboardExcluded(); // fails open on a busy clipboard

        private void CaptureClipboardNow()
        {
            if (!AppPrefs.Instance.ClipboardAutoCapture) return;
            if (_suppressClipCapture) return;
            if (IsClipboardExcludedFromHistory()) return;

            try
            {
                ClipboardItem item = null;

                if (Clipboard.ContainsImage())
                {
                    // Capture image — skip dedupe (images are always unique enough)
                    var bmpSrc = Clipboard.GetImage();
                    if (bmpSrc != null)
                    {
                        Directory.CreateDirectory(_clipImagesDir);
                        int id = _nextClipId++;
                        string imgPath = System.IO.Path.Combine(_clipImagesDir, $"{id}.png");
                        using var png = new MemoryStream();
                        var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
                        encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bmpSrc));
                        encoder.Save(png);
                        imgPath = NoteStation.Core.ClipVault.SaveImage(imgPath, png.ToArray(), AppPrefs.Instance.EncryptClipboardAtRest);
                        item = ClipboardItem.FromImage(id, imgPath);
                    }
                }
                else if (Clipboard.ContainsText())
                {
                    string text = SanitizeClipboardText(Clipboard.GetText(System.Windows.TextDataFormat.UnicodeText));
                    if (string.IsNullOrEmpty(text)) return;

                    // Always dedupe consecutive identical text (prevents double-fire duplicates).
                    // ClipboardDedupeConsecutive pref now also dedupes within entire history.
                    if (_clips.Count > 0 && !_clips[0].IsImage && _clips[0].Text == text) return;
                    if (AppPrefs.Instance.ClipboardDedupeConsecutive)
                    {
                        // Remove any older occurrence of the same text before inserting at top
                        for (int i = _clips.Count - 1; i >= 0; i--)
                            if (!_clips[i].IsImage && _clips[i].Text == text) { _clips.RemoveAt(i); break; }
                    }

                    item = ClipboardItem.FromText(_nextClipId++, text);
                    // F9: Parse inline #hashtags
                    item.ParsedTags = ParseClipTags(text);
                }

                if (item == null) return;

                // F11: Auto-dedup — if same text already exists, move it to top
                if (!item.IsImage)
                {
                    var existing = _clips.FirstOrDefault(c => !c.IsImage && c.Text == item.Text);
                    if (existing != null)
                    {
                        int idx = _clips.IndexOf(existing);
                        if (idx > 0) _clips.Move(idx, 0);
                        return; // don't add a duplicate
                    }
                }

                item.SourceApp = _pendingSourceApp;
                _clips.Insert(0, item);
                _clipOverlay?.Refresh(_clips, _templates);

                // Trim to max (never remove pinned or folder-assigned items).
                // Cap applies to the LOOSE pool (unpinned, unfoldered) only. Pinned/
                // foldered items are permanent keeps and must NOT count against the cap —
                // comparing _clips.Count (the grand total) against max was the bug: once
                // pinned+foldered items alone exceeded max, the loop kept evicting every
                // eligible loose item trying to reach max, ran out, and wiped the entire
                // "Recent" pool to 0 while still sitting far above max overall.
                // Floor against a corrupted/poisoned pref (e.g. 0) wiping the whole list.
                int max = Math.Max(AppPrefs.Instance.ClipboardMaxHistory, 10);
                int countBefore = _clips.Count;
                int looseCount = _clips.Count(c => !c.IsPinned && !c.FolderId.HasValue);
                int evicted = 0;
                while (looseCount > max)
                {
                    int idx = -1;
                    for (int i = _clips.Count - 1; i >= 0; i--)
                    {
                        if (!_clips[i].IsPinned && !_clips[i].FolderId.HasValue) { idx = i; break; }
                    }
                    if (idx < 0) break; // nothing left eligible for eviction

                    var removed = _clips[idx];
                    if (removed.IsImage && File.Exists(removed.ImagePath))
                        try { File.Delete(removed.ImagePath); } catch { }
                    _clips.RemoveAt(idx);
                    looseCount--;
                    evicted++;
                }
                if (evicted > 0)
                {
                    try
                    {
                        File.AppendAllText(System.IO.Path.Combine(App.AppDataDir, "cliptrim.log"),
                            $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] rawPref={AppPrefs.Instance.ClipboardMaxHistory} flooredMax={max} before={countBefore} evicted={evicted} after={_clips.Count}\n");
                    }
                    catch { /* best-effort */ }
                }

                if (_clipIdx > 0) _clipIdx++;
                UpdatePresets();
                UpdateStatus();
                DataStore.SaveClip(item);
                Controls.StatusAnnouncer.AnnounceDebounced(item.IsImage ? "Image clip captured" : "Clip captured",
                    TimeSpan.FromMilliseconds(400));
                PruneExpiredClips();

                // D1-6: warn once when loose history hits 80% of max
                if (!_historyWarningSent && looseCount >= (int)(max * 0.8))
                {
                    _historyWarningSent = true;
                    _trayIcon?.ShowBalloonTip(4000, "NoteStation — History Nearly Full",
                        $"Clipboard history is at {looseCount}/{max} items. Oldest items will be removed automatically. Adjust limit in Settings.",
                        System.Windows.Forms.ToolTipIcon.Info);
                }
                else if (looseCount < (int)(max * 0.8))
                {
                    _historyWarningSent = false; // reset so warning fires again if limit grows
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[NoteStation] ClipboardCapture error: {ex}");
            }
        }

        private void RequestSaveClipboard()
        {
            if (_clipSaveTimer == null)
            {
                _clipSaveTimer = new System.Windows.Threading.DispatcherTimer
                    { Interval = TimeSpan.FromMilliseconds(500) };
                _clipSaveTimer.Tick += (_, __) =>
                {
                    _clipSaveTimer.Stop();
                    DataStore.SaveClipboard(_clips);
                };
            }
            _clipSaveTimer.Stop();
            _clipSaveTimer.Start();
        }

        private async void ListClipboard_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ListClipboard.SelectedItem is ClipboardItem item)
            {
                if (item.IsImage)
                {
                    ClipPreview.Text = "";
                    ClipPreview.Visibility = Visibility.Collapsed;
                    if (ClipImagePreview != null)
                    {
                        ClipImagePreview.Visibility = Visibility.Visible;
                        ClipImagePreview.Source = null;
                        if (File.Exists(item.ImagePath))
                        {
                            try
                            {
                                string path = item.ImagePath;
                                var bmp = await System.Threading.Tasks.Task.Run(() =>
                                    NoteStation.Core.ClipVault.LoadImage(path));
                                ClipImagePreview.Source = bmp;
                            }
                            catch (Exception ex)
                            {
                                System.Diagnostics.Debug.WriteLine($"[NoteStation] BitmapLoad error: {ex}");
                                ClipImagePreview.Source = null;
                            }
                        }
                        else { ClipImagePreview.Source = null; }
                    }
                }
                else
                {
                    if (ClipImagePreview != null) ClipImagePreview.Visibility = Visibility.Collapsed;
                    ClipPreview.Visibility = Visibility.Visible;
                    ClipPreview.Text = item.Text.Length > 2000
                        ? item.Text.Substring(0, 2000) + "\n…(truncated)"
                        : item.Text;
                }
                _clipIdx = _clips.IndexOf(item);
            }
        }

        private void ListClipboard_DoubleClick(object sender, MouseButtonEventArgs e)
        {
            // Folders area: double-click edits the clip. (Overlay double-click stays copy.)
            if (ListClipboard.SelectedItem is ClipboardItem item)
            {
                e.Handled = true;
                EditClip(item);
            }
        }

        /// <summary>Edit a clip's content + title. Image clips: title only.</summary>
        private void EditClip(ClipboardItem clip)
        {
            if (clip == null) return;

            if (clip.IsImage)
            {
                RenameClip(clip);
                return;
            }

            if (!PromptEditClip(clip, out string newTitle, out string newText))
            {
                DataStore.SaveClip(clip);   // persist remembered window size even on cancel
                return;
            }

            clip.Title = (newTitle ?? "").Trim();
            clip.Text  = newText ?? "";
            string preview = RichTextFormatting.StripToPlainText(clip.Text)
                .Replace("\r\n", " ").Replace('\n', ' ').Replace('\r', ' ');
            if (preview.Length > 120) preview = preview.Substring(0, 117) + "…";
            clip.Preview = preview;

            DataStore.SaveClip(clip);
            ApplyFilter();   // rebind so title/preview refresh
            ListClipboard.SelectedItem = clip;
        }

        /// <summary>Title + multi-line content editor. Returns false if cancelled.</summary>
        private bool PromptEditClip(ClipboardItem clip, out string title, out string text)
        {
            string initTitle = clip.Title;
            string initText  = clip.Text;
            var dlg = new Window
            {
                Title = "Edit Clip",
                // Remember this clip's last Edit Clip window size; default to a larger size than before.
                Width  = clip.EditWidth  ?? 720,
                Height = clip.EditHeight ?? 560,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = this,
                ResizeMode = ResizeMode.CanResizeWithGrip,
                Background = App.GetBrush("BrushBg"),
                ShowInTaskbar = false,
            };
            var grid = new Grid { Margin = new Thickness(16) };
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var lblTitle = new TextBlock
            {
                Text = "Title (optional):",
                Foreground = App.GetBrush("BrushText"),
                Margin = new Thickness(0, 0, 0, 4),
            };
            Grid.SetRow(lblTitle, 0);
            grid.Children.Add(lblTitle);

            var txtTitle = new TextBox
            {
                Text = initTitle ?? "",
                MinHeight = 44,
                VerticalContentAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 12),
            };
            Grid.SetRow(txtTitle, 1);
            grid.Children.Add(txtTitle);

            var lblText = new TextBlock
            {
                Text = "Content:",
                Foreground = App.GetBrush("BrushText"),
                Margin = new Thickness(0, 0, 0, 4),
            };
            Grid.SetRow(lblText, 2);
            grid.Children.Add(lblText);

            var txtBody = new RichTextBox
            {
                AcceptsTab = true,
                Margin = new Thickness(0, 0, 0, 12),
            };
            RichTextFormatting.LoadContent(txtBody, initText ?? "");

            var toolbar = RichTextFormatting.BuildToolbar(txtBody);
            Grid.SetRow(toolbar, 3);
            grid.Children.Add(toolbar);

            Grid.SetRow(txtBody, 4);
            grid.Children.Add(txtBody);

            var btnRow = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            var btnOk  = new Button { Content = "Save",   MinWidth = 80, MinHeight = 44, Margin = new Thickness(0, 0, 8, 0), IsDefault = true };
            var btnCan = new Button { Content = "Cancel", MinWidth = 80, MinHeight = 44, IsCancel = true };
            btnRow.Children.Add(btnOk);
            btnRow.Children.Add(btnCan);
            Grid.SetRow(btnRow, 5);
            grid.Children.Add(btnRow);

            dlg.Content = grid;
            bool ok = false;
            btnOk.Click  += (_, __) => { ok = true;  dlg.Close(); };
            btnCan.Click += (_, __) => { ok = false; dlg.Close(); };
            txtBody.Focus();
            dlg.ShowDialog();

            // Remember whatever size the user left the window at (resized or not).
            clip.EditWidth  = dlg.Width;
            clip.EditHeight = dlg.Height;

            title = ok ? txtTitle.Text : "";
            text  = ok ? RichTextFormatting.SaveContent(txtBody) : "";
            return ok;
        }

        private void BtnClipPaste_Click(object sender, RoutedEventArgs e) => PasteSelected();

        private void PasteSelected()
        {
            var item = ListClipboard.SelectedItem as ClipboardItem ?? (_clips.Count > 0 ? _clips[0] : null);
            if (item == null) return;
            if (item.IsImage)
                PasteImage(item.ImagePath);
            else
                PasteText(item.Text);
        }

        private void PasteImage(string imagePath)
        {
            if (!File.Exists(imagePath)) return;
            _suppressClipCapture  = true;
            _suppressTrayMinimize = true;
            try
            {
                var bmp = NoteStation.Core.ClipVault.LoadImage(imagePath);
                Clipboard.SetImage(bmp);
                WindowState = WindowState.Minimized;
                System.Threading.Tasks.Task.Delay(120).ContinueWith(_ =>
                {
                    Dispatcher.Invoke(() =>
                    {
                        System.Windows.Forms.SendKeys.SendWait("^v");
                        _suppressClipCapture  = false;
                        _suppressTrayMinimize = false;
                        Show();
                        WindowState = WindowState.Normal;
                        Activate();
                    });
                });
                return;
            }
            catch (Exception ex) { AppLog.Error("PasteImage", ex); }
            _suppressClipCapture  = false;
            _suppressTrayMinimize = false;
        }

        private void PasteText(string text)
        {
            _suppressClipCapture  = true;
            _suppressTrayMinimize = true;
            try
            {
                Clipboard.SetText(text);
                // Minimize so the target app comes forward, then Ctrl+V
                WindowState = WindowState.Minimized;
                System.Threading.Tasks.Task.Delay(120).ContinueWith(_ =>
                {
                    Dispatcher.Invoke(() =>
                    {
                        System.Windows.Forms.SendKeys.SendWait("^v");
                        _suppressClipCapture  = false;
                        _suppressTrayMinimize = false;
                        Show();
                        WindowState = WindowState.Normal;
                        Activate();
                    });
                });
                return;
            }
            catch (Exception ex) { AppLog.Error("PasteText", ex); }
            _suppressClipCapture  = false;
            _suppressTrayMinimize = false;
        }

        private void BtnClipPrev_Click(object sender, RoutedEventArgs e) => ClipboardPrev();
        private void BtnClipNext_Click(object sender, RoutedEventArgs e) => ClipboardNext();

        private void ClipboardPrev()
        {
            if (_clips.Count == 0) return;
            _clipIdx = (_clipIdx - 1 + _clips.Count) % _clips.Count;
            ListClipboard.SelectedIndex = _clipIdx;
            ListClipboard.ScrollIntoView(ListClipboard.SelectedItem);
        }

        private void ClipboardNext()
        {
            if (_clips.Count == 0) return;
            _clipIdx = (_clipIdx + 1) % _clips.Count;
            ListClipboard.SelectedIndex = _clipIdx;
            ListClipboard.ScrollIntoView(ListClipboard.SelectedItem);
        }

        private void BtnClipPin_Click(object sender, RoutedEventArgs e)
        {
            if (ListClipboard.SelectedItem is ClipboardItem item)
            {
                item.IsPinned = !item.IsPinned;
                // In-place slide: Move() preserves scroll position and multi-select.
                // After toggle: pinned items occupy [0 .. pinnedCount-1].
                int from = _clips.IndexOf(item);
                int to   = item.IsPinned
                    ? _clips.Count(c => c.IsPinned) - 1  // end of pinned zone (item already counted)
                    : _clips.Count(c => c.IsPinned);      // first slot of unpinned zone
                if (from != to)
                    _clips.Move(from, to);
                UpdatePresets();
                DataStore.SaveClip(item);
            }
        }

        private void BtnClipDelete_Click(object sender, RoutedEventArgs e)
        {
            var selected = ListClipboard.SelectedItems.OfType<ClipboardItem>().ToList();
            if (selected.Count == 0) return;
            _lastDeletedClip = selected.LastOrDefault();
            foreach (var item in selected)
                DeleteClipFromView(item);
            UpdatePresets();
            _clipOverlay?.Refresh(_clips, _templates);
            BuildFolderTree("Clipboard");   // synchronous, render-reliable count refresh
            ApplyFilter();
        }

        private void BtnClipClear_Click(object sender, RoutedEventArgs e)
        {
            var res = MessageBox.Show(
                "Clear all clipboard history? Pinned and filed items will be kept.",
                "NoteStation — Clear Clipboard",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);
            if (res != MessageBoxResult.Yes) return;

            // Keep pinned items AND anything filed in a saved folder.
            var toDelete = _clips.Where(c => !c.IsPinned && !c.FolderId.HasValue).ToList();
            foreach (var c in toDelete)
                SoftDeleteClip(c);
            UpdatePresets();
            BuildFolderTree("Clipboard");   // synchronous, render-reliable count refresh
            ApplyFilter();
            UpdateStatus();
        }

        private void BtnExportClip_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                Title      = "Export Clipboard History",
                Filter     = "Text file (*.txt)|*.txt|CSV file (*.csv)|*.csv",
                DefaultExt = ".txt",
                FileName   = $"clipboard-{DateTime.Now:yyyy-MM-dd}",
            };
            if (dlg.ShowDialog(this) != true) return;

            try
            {
                var items = (ListClipboard.ItemsSource as System.Collections.Generic.IEnumerable<ClipboardItem>)
                            ?? _clips;
                bool isCsv = dlg.FileName.EndsWith(".csv", StringComparison.OrdinalIgnoreCase);
                var sb = new System.Text.StringBuilder();

                if (isCsv)
                {
                    sb.AppendLine("Id,Preview,CapturedAt,IsPinned,Category");
                    foreach (var c in items)
                    {
                        if (c.IsImage) continue; // skip images in CSV
                        string preview = c.Preview.Replace("\"", "\"\"");
                        sb.AppendLine($"{c.Id},\"{preview}\",{c.CapturedAt:o},{c.IsPinned},{c.Category}");
                    }
                }
                else
                {
                    foreach (var c in items)
                    {
                        if (c.IsImage) { sb.AppendLine($"--- {c.CapturedAt:MMM d, h:mm tt} [Image] ---"); sb.AppendLine(); continue; }
                        sb.AppendLine($"--- {c.CapturedAt:MMM d, h:mm tt} ---");
                        sb.AppendLine(c.Text);
                        sb.AppendLine();
                    }
                }

                File.WriteAllText(dlg.FileName, sb.ToString(), System.Text.Encoding.UTF8);
                int skipped = isCsv ? items.Count(x => x.IsImage) : 0;
                int exported = items.Count() - skipped;
                string exportMsg = skipped > 0
                    ? $"Exported {exported} text items to:\n{dlg.FileName}\n({skipped} image item(s) skipped — CSV does not support images)"
                    : $"Exported {exported} items to:\n{dlg.FileName}";
                MessageBox.Show(exportMsg, "NoteStation — Export Complete", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Export failed:\n{ex.Message}", "NoteStation — Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // ── F5: Clip–Task linking ─────────────────────────────────────────────
        private void BtnLinkClipToTask_Click(object sender, RoutedEventArgs e)
        {
            var clip = ListClipboard.SelectedItem as ClipboardItem;
            if (clip == null) return;
            var dlg = new Windows.TaskPickerDialog(_tasks, clip.LinkedTaskId) { Owner = this };
            dlg.ShowDialog();
            if (!dlg.Confirmed) return;
            if (dlg.Unlinked)
            {
                clip.LinkedTaskId = 0;
            }
            else if (dlg.SelectedTask != null)
            {
                // Remove this clip from any previous task's linked list
                foreach (var t in _tasks.Where(t => t.LinkedClipIds.Contains(clip.Id)))
                    t.LinkedClipIds.Remove(clip.Id);
                clip.LinkedTaskId = dlg.SelectedTask.Id;
                if (!dlg.SelectedTask.LinkedClipIds.Contains(clip.Id))
                    dlg.SelectedTask.LinkedClipIds.Add(clip.Id);
            }
            DataStore.SaveClip(clip);
            if (dlg.SelectedTask != null) DataStore.SaveTask(dlg.SelectedTask);
        }

        private void BtnUnlinkClipFromTask_Click(object sender, RoutedEventArgs e)
        {
            var clip = ListClipboard.SelectedItem as ClipboardItem;
            if (clip == null) return;
            if (clip.LinkedTaskId != 0)
            {
                var task = _tasks.FirstOrDefault(t => t.Id == clip.LinkedTaskId);
                task?.LinkedClipIds.Remove(clip.Id);
                clip.LinkedTaskId = 0;
                DataStore.SaveClip(clip);
                if (task != null) DataStore.SaveTask(task);
            }
        }

        // D2-7: Export notes to .txt / .md
        private void BtnExportNotes_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                Title      = "Export Notes",
                Filter     = "Markdown (*.md)|*.md|Text file (*.txt)|*.txt",
                DefaultExt = ".md",
                FileName   = $"notes-{DateTime.Now:yyyy-MM-dd}",
            };
            if (dlg.ShowDialog(this) != true) return;
            try
            {
                var items = (ListNotes.ItemsSource as IEnumerable<StickyNote>)
                            ?? (IEnumerable<StickyNote>)_notes;
                bool isMd = dlg.FileName.EndsWith(".md", StringComparison.OrdinalIgnoreCase);
                var sb = new System.Text.StringBuilder();
                foreach (var n in items)
                {
                    if (isMd)
                    {
                        sb.AppendLine($"## {n.Title}");
                        sb.AppendLine($"*Modified: {n.ModifiedAt:MMM d, yyyy}*");
                        sb.AppendLine();
                        sb.AppendLine(n.ContentPreview);
                        sb.AppendLine();
                        sb.AppendLine("---");
                        sb.AppendLine();
                    }
                    else
                    {
                        sb.AppendLine($"=== {n.Title} (modified {n.ModifiedAt:MMM d, yyyy}) ===");
                        sb.AppendLine(n.ContentPreview);
                        sb.AppendLine();
                    }
                }
                File.WriteAllText(dlg.FileName, sb.ToString(), System.Text.Encoding.UTF8);
                MessageBox.Show($"Exported {items.Count()} note(s) to:\n{dlg.FileName}",
                    "NoteStation — Export Complete", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Export failed:\n{ex.Message}", "NoteStation — Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // D3-7: Export tasks to .csv / .ics
        private void BtnExportTasks_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                Title      = "Export Tasks",
                Filter     = "CSV (*.csv)|*.csv|iCalendar (*.ics)|*.ics",
                DefaultExt = ".csv",
                FileName   = $"tasks-{DateTime.Now:yyyy-MM-dd}",
            };
            if (dlg.ShowDialog(this) != true) return;
            try
            {
                var items = (TaskList.ItemsSource as IEnumerable<TaskItem>)
                            ?? (IEnumerable<TaskItem>)_tasks;
                bool isIcs = dlg.FileName.EndsWith(".ics", StringComparison.OrdinalIgnoreCase);
                var sb = new System.Text.StringBuilder();

                if (isIcs)
                {
                    sb.AppendLine("BEGIN:VCALENDAR");
                    sb.AppendLine("VERSION:2.0");
                    sb.AppendLine("PRODID:-//NoteStation//EN");
                    foreach (var t in items)
                    {
                        sb.AppendLine("BEGIN:VTODO");
                        sb.AppendLine($"UID:{t.Id}@notestation");
                        sb.AppendLine($"SUMMARY:{EscapeIcs(t.Title)}");
                        if (!string.IsNullOrWhiteSpace(t.Notes))
                            sb.AppendLine($"DESCRIPTION:{EscapeIcs(t.Notes)}");
                        sb.AppendLine($"STATUS:{(t.IsCompleted ? "COMPLETED" : "NEEDS-ACTION")}");
                        sb.AppendLine($"DTSTAMP:{DateTime.UtcNow:yyyyMMddTHHmmssZ}");
                        sb.AppendLine($"CREATED:{t.CreatedAt.ToUniversalTime():yyyyMMddTHHmmssZ}");
                        if (t.HasDueDate && t.DueDate.HasValue)
                            sb.AppendLine($"DUE:{t.DueDate.Value:yyyyMMdd}");
                        if (t.CompletedAt.HasValue)
                            sb.AppendLine($"COMPLETED:{t.CompletedAt.Value.ToUniversalTime():yyyyMMddTHHmmssZ}");
                        // RFC 5545: PRIORITY 1=highest, 9=lowest, 0=undefined
                        int icsPriority = t.Priority switch { 3 => 1, 2 => 5, 1 => 9, _ => 0 };
                        sb.AppendLine($"PRIORITY:{icsPriority}");
                        sb.AppendLine("END:VTODO");
                    }
                    sb.AppendLine("END:VCALENDAR");
                }
                else
                {
                    sb.AppendLine("Id,Title,Notes,IsCompleted,IsStarred,Priority,DueDate,DueTime,Category,CreatedAt");
                    foreach (var t in items)
                    {
                        string title = t.Title.Replace("\"", "\"\"");
                        string notes = (t.Notes ?? "").Replace("\"", "\"\"");
                        string due   = t.HasDueDate && t.DueDate.HasValue ? t.DueDate.Value.ToString("yyyy-MM-dd") : "";
                        string dueT  = t.HasDueTime && t.DueTime.HasValue ? t.DueTime.Value.ToString(@"hh\:mm") : "";
                        sb.AppendLine($"{t.Id},\"{title}\",\"{notes}\",{t.IsCompleted},{t.IsStarred},{t.Priority},{due},{dueT},{t.Category},{t.CreatedAt:o}");
                    }
                }

                File.WriteAllText(dlg.FileName, sb.ToString(), System.Text.Encoding.UTF8);
                MessageBox.Show($"Exported {items.Count()} task(s) to:\n{dlg.FileName}",
                    "NoteStation — Export Complete", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Export failed:\n{ex.Message}", "NoteStation — Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private static string EscapeIcs(string s)
            => (s ?? "").Replace("\\", "\\\\").Replace(";", "\\;").Replace(",", "\\,").Replace("\n", "\\n").Replace("\r", "");

        // ── Preset buttons ────────────────────────────────────────────────────
        private void BtnPreset_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string idxStr
                && int.TryParse(idxStr, out int idx))
            {
                var pinned = _clips.Where(c => c.IsPinned).Take(5).ToList();
                ClipboardItem item;
                if (idx < pinned.Count)
                    item = pinned[idx];
                else
                {
                    if (idx >= _clips.Count) return;
                    item = _clips[idx];
                }
                if (item.IsImage) PasteImage(item.ImagePath);
                else              PasteText(item.Text);
            }
        }

        private void UpdatePresets()
        {
            // Fill presets with pinned items first, then recent
            var pinned   = _clips.Where(c => c.IsPinned).Take(5).ToList();
            var unpinned = _clips.Where(c => !c.IsPinned).Take(5 - pinned.Count).ToList();
            var display  = pinned.Concat(unpinned).ToList();

            for (int i = 0; i < _presetBtns.Length; i++)
            {
                if (i < display.Count)
                {
                    var preview = display[i].Preview;
                    if (preview.Length > 14) preview = preview.Substring(0, 12) + "…";
                    _presetTexts[i].Text    = preview;
                    _presetBtns[i].ToolTip  = $"Quick-paste (Ctrl+{i+1}): {display[i].Preview}";
                    _presetBtns[i].IsEnabled = true;
                }
                else
                {
                    _presetTexts[i].Text    = "—";
                    _presetBtns[i].ToolTip  = $"Quick-paste slot {i+1} (empty)";
                    _presetBtns[i].IsEnabled = false;
                }
            }
        }

        // ══════════════════════════════════════════════════════════════════════
        //  Sticky Notes
        // ══════════════════════════════════════════════════════════════════════
        private void BtnNewNote_Click(object sender, RoutedEventArgs e) => CreateNote();

        /// <summary>Applies the saved dock edge/position, or the saved free-floating position
        /// when the sidebar was last left undocked (right-click → Undock).</summary>
        private void ApplyNotesSidebarPlacement()
        {
            var p = AppPrefs.Instance;
            if (p.NotesSidebarUndocked)
                _notesSidebar.Undock(p.NotesSidebarFloatLeft, p.NotesSidebarFloatTop);
            else
                _notesSidebar.DockToEdge(p.NotesSidebarEdge, p.NotesSidebarPosition);
        }

        private void BtnNotesSidebar_Click(object sender, RoutedEventArgs e)
        {
            if (_notesSidebar == null || !_notesSidebar.IsLoaded)
            {
                _notesSidebar = new NotesSidebarWindow();
                _notesSidebar.TabClicked += (_, note) => OpenNoteWindow(note);
                _notesSidebar.RemoveFromSidebarRequested += (_, note) =>
                {
                    note.ShowInSidebar = false;
                    DataStore.SaveNote(note);
                    _notesSidebar?.Refresh(_notes);
                };
                _notesSidebar.NewNoteRequested += (_, source) =>
                    CreateNote(pinToSidebar: true, folderId: source?.FolderId, bgColorOverride: source?.BackgroundColor);
                _notesSidebar.NoteEdited += (_, note) =>
                {
                    DataStore.SaveNote(note);
                    if (_noteWindows.TryGetValue(note.Id, out var wnd))
                        wnd.RefreshFromModel();   // keep a standalone-open copy of the same note in sync
                    RefreshNotesList();
                };
                _notesSidebar.AddExistingNoteRequested += (_, note) =>
                {
                    note.ShowInSidebar = true;
                    DataStore.SaveNote(note);
                    _notesSidebar?.Refresh(_notes);
                };
                _notesSidebar.BarOpacityChanged += (_, opacity) =>
                {
                    AppPrefs.Instance.NotesSidebarBarOpacity = opacity;
                    AppPrefs.Instance.DeferredSave();
                };
                _notesSidebar.WidthChanged += (_, width) =>
                {
                    AppPrefs.Instance.NotesSidebarWidth = width;
                    AppPrefs.Instance.DeferredSave();
                };
                _notesSidebar.ShowAddButtonChanged += (_, show) =>
                {
                    AppPrefs.Instance.NotesSidebarShowAddButton = show;
                    AppPrefs.Instance.DeferredSave();
                };
                _notesSidebar.DockEdgeChanged += (_, edge) =>
                {
                    AppPrefs.Instance.NotesSidebarEdge = edge;
                    AppPrefs.Instance.DeferredSave();
                };
                _notesSidebar.DockPositionChanged += (_, position) =>
                {
                    AppPrefs.Instance.NotesSidebarPosition = position;
                    AppPrefs.Instance.DeferredSave();
                };
                _notesSidebar.UndockedChanged += (_, undocked) =>
                {
                    AppPrefs.Instance.NotesSidebarUndocked = undocked;
                    AppPrefs.Instance.DeferredSave();
                };
                _notesSidebar.FloatPositionChanged += (_, pos) =>
                {
                    AppPrefs.Instance.NotesSidebarFloatLeft = pos.X;
                    AppPrefs.Instance.NotesSidebarFloatTop  = pos.Y;
                    AppPrefs.Instance.DeferredSave();
                };
                _notesSidebar.Width = AppPrefs.Instance.NotesSidebarWidth >= 44 ? AppPrefs.Instance.NotesSidebarWidth : 52;
                ApplyNotesSidebarPlacement();
                _notesSidebar.SetBarOpacity(AppPrefs.Instance.NotesSidebarBarOpacity);
                _notesSidebar.SetShowAddButton(AppPrefs.Instance.NotesSidebarShowAddButton);
                _notesSidebar.Show();
                _notesSidebar.Refresh(_notes);
            }
            else if (_notesSidebar.IsVisible)
            {
                _notesSidebar.Hide();
            }
            else
            {
                ApplyNotesSidebarPlacement();
                _notesSidebar.SetBarOpacity(AppPrefs.Instance.NotesSidebarBarOpacity);
                _notesSidebar.Show();
                _notesSidebar.Refresh(_notes);
            }
        }

        private void CreateNote(string title = "New Note", string content = "", int? folderId = null,
                                  bool pinToSidebar = false, string bgColorOverride = null)
        {
            // Determine background: prefer selected Notes folder's DefaultNoteBackground if set,
            // then fall back to the currently-selected note node's folder (if any), then global pref.
            string bgColor = AppPrefs.Instance.LastNoteBackground;

            // If a folderId was explicitly provided, look that folder up
            if (folderId.HasValue)
            {
                var folder = AppPrefs.Instance.NoteFolders.FirstOrDefault(f => f.Id == folderId.Value);
                if (folder != null && !string.IsNullOrEmpty(folder.DefaultNoteBackground))
                    bgColor = folder.DefaultNoteBackground;
            }
            else if (_selectedNoteNode?.NodeType == FolderNodeType.UserFolder
                     && _selectedNoteNode.FolderId.HasValue)
            {
                // Use the currently selected folder's default if one is set
                var selFolder = AppPrefs.Instance.NoteFolders.FirstOrDefault(
                    f => f.Id == _selectedNoteNode.FolderId.Value);
                if (selFolder != null && !string.IsNullOrEmpty(selFolder.DefaultNoteBackground))
                    bgColor = selFolder.DefaultNoteBackground;
            }

            // A sidebar tab ("New Note Here") can request the exact color of the tab it was
            // spawned from, taking priority over the folder/global default computed above.
            if (!string.IsNullOrEmpty(bgColorOverride)) bgColor = bgColorOverride;

            var note = new StickyNote
            {
                Id    = _nextNoteId++,
                Title = title,
                Content = content,
                IsVisible = true,
                BackgroundColor = bgColor,
                TextColor       = AppPrefs.Instance.LastNoteTextColor,
                FontFamily = "Segoe UI",
                FontSize = 14,
                FolderId = folderId,
                ShowInSidebar = pinToSidebar,
            };
            _notes.Add(note);
            _notesView.Add(note);
            OpenNoteWindow(note);
            DataStore.SaveNote(note);
            UpdateStatus();
            ApplyFilter();
        }

        private void OpenNoteWindow(StickyNote note)
        {
            if (_noteWindows.TryGetValue(note.Id, out var existing))
            {
                if (!existing.IsVisible) existing.Show();
                existing.Activate();
                return;
            }

            var wnd = new StickyNoteWindow(note);
            wnd.NoteChanged         += NoteWindow_NoteChanged;
            wnd.NoteDeleteRequested += NoteWindow_NoteDeleteRequested;
            _noteWindows[note.Id] = wnd;
            wnd.Show();
        }

        // ── Soft-delete / restore / permanent-delete ─────────────────────────

        private void SoftDeleteClip(ClipboardItem clip)
        {
            clip.IsDeleted = true;
            clip.DeletedAt = DateTime.Now;
            _clips.Remove(clip);
            _deletedClips.Add(clip);
            DataStore.SaveClip(clip);
        }

        // ── Delete from the active folder view ─────────────────────────────────
        // From Recent: a filed item just leaves Recent, stays in its saved folder.
        // Anywhere else (or unfiled item): normal soft-delete to Recently Deleted.
        private void DeleteClipFromView(ClipboardItem clip)
        {
            if (_selectedClipNode?.NodeType == FolderNodeType.Recent && clip.FolderId.HasValue)
            {
                clip.HiddenFromRecent = true;
                DataStore.SaveClip(clip);
            }
            else SoftDeleteClip(clip);
        }

        private void DeleteNoteFromView(StickyNote note)
        {
            if (_selectedNoteNode?.NodeType == FolderNodeType.Recent && note.FolderId.HasValue)
            {
                note.HiddenFromRecent = true;
                DataStore.SaveNote(note);
                BuildFolderTree("Notes");
                ApplyFilter();
            }
            else DeleteNote(note);
        }

        private void DeleteTaskFromView(TaskItem task)
        {
            if (_selectedTaskNode?.NodeType == FolderNodeType.Recent && task.FolderId.HasValue)
            {
                task.HiddenFromRecent = true;
                DataStore.SaveTask(task);
                _taskOverlay?.Refresh(_tasks);
                BuildFolderTree("Tasks");
                ApplyFilter();
            }
            else DeleteTask(task);
        }

        private void RestoreClip(ClipboardItem clip)
        {
            clip.IsDeleted = false;
            clip.DeletedAt = default;
            _deletedClips.Remove(clip);
            _clips.Insert(0, clip);
            DataStore.SaveClip(clip);
        }

        private void PermanentDeleteClip(ClipboardItem clip)
        {
            _deletedClips.Remove(clip);
            if (clip.IsImage && !string.IsNullOrEmpty(clip.ImagePath) && File.Exists(clip.ImagePath))
                try { File.Delete(clip.ImagePath); } catch { }
            DataStore.DeleteClip(clip.Id);
        }

        private void SoftDeleteNote(StickyNote note)
        {
            note.IsDeleted = true;
            note.DeletedAt = DateTime.Now;
            _notes.Remove(note);
            _notesView.Remove(note);
            _deletedNotes.Add(note);
            DataStore.SaveNote(note);
        }

        private void RestoreNote(StickyNote note)
        {
            note.IsDeleted = false;
            note.DeletedAt = default;
            _deletedNotes.Remove(note);
            _notes.Add(note);
            _notesView.Add(note);
            DataStore.SaveNote(note);
        }

        private void PermanentDeleteNote(StickyNote note)
        {
            _deletedNotes.Remove(note);
            DataStore.DeleteNote(note.Id);
        }

        private void SoftDeleteTask(TaskItem task)
        {
            task.IsDeleted = true;
            task.DeletedAt = DateTime.Now;
            _tasks.Remove(task);
            _deletedTasks.Add(task);
            DataStore.SaveTask(task);
        }

        private void RestoreTask(TaskItem task)
        {
            task.IsDeleted = false;
            task.DeletedAt = default;
            _deletedTasks.Remove(task);
            _tasks.Add(task);
            DataStore.SaveTask(task);
        }

        private void PermanentDeleteTask(TaskItem task)
        {
            _deletedTasks.Remove(task);
            DataStore.DeleteTask(task.Id);
        }

        private void CloseNoteWindow(int id, StickyNoteWindow wnd)
        {
            wnd.NoteChanged         -= NoteWindow_NoteChanged;
            wnd.NoteDeleteRequested -= NoteWindow_NoteDeleteRequested;
            wnd.CloseForReal();
            _noteWindows.Remove(id);
        }

        private void DeleteNote(StickyNote note)
        {
            if (_noteWindows.TryGetValue(note.Id, out var wnd))
                CloseNoteWindow(note.Id, wnd);
            SoftDeleteNote(note);
            BuildFolderTree("Notes");   // synchronous, render-reliable count refresh
            ApplyFilter();
        }

        private void BtnShowAllNotes_Click(object sender, RoutedEventArgs e)
        {
            foreach (var note in _notes)
            {
                note.IsVisible = true;
                OpenNoteWindow(note);
                DataStore.SaveNote(note);
            }
        }

        private void BtnHideAllNotes_Click(object sender, RoutedEventArgs e)
        {
            foreach (var note in _notes)
            {
                note.IsVisible = false;
                if (_noteWindows.TryGetValue(note.Id, out var wnd))
                    wnd.Hide();
                DataStore.SaveNote(note);
            }
        }

        private void ListNotes_SelectionChanged(object sender, SelectionChangedEventArgs e) { }

        private void ListNotes_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (ListNotes.SelectedItem is StickyNote note)
                OpenNoteWindow(note);
        }

        private void SaveNotes()        => DataStore.SaveNotes(_notes);
        private void RefreshNotesList() => ApplyFilter();

        private void NoteWindow_NoteChanged(object sender, StickyNote n)
        {
            Controls.StatusAnnouncer.AnnounceDebounced("Note saved", TimeSpan.FromSeconds(2), sender as UIElement);
            DataStore.SaveNote(n);
            RefreshNotesList();
            _notesSidebar?.Refresh(_notes);
        }
        private void NoteWindow_NoteDeleteRequested(object sender, StickyNote n) => DeleteNote(n);

        // ── Note grid/list view toggle ────────────────────────────────────────
        private void BtnNoteView_Click(object sender, RoutedEventArgs e)
        {
            AppPrefs.Instance.NoteViewMode = AppPrefs.Instance.NoteViewMode == 0 ? 1 : 0;
            AppPrefs.Instance.DeferredSave();
            ApplyNoteViewMode();
        }

        private void ApplyNoteViewMode()
        {
            if (MemoboardScroll != null) MemoboardScroll.Visibility = Visibility.Collapsed;
            bool grid = AppPrefs.Instance.NoteViewMode == 1;
            if (ListNotes != null) ListNotes.Visibility = grid ? Visibility.Collapsed : Visibility.Visible;
            if (NoteGridScroll != null) NoteGridScroll.Visibility = grid ? Visibility.Visible : Visibility.Collapsed;
            if (BtnNoteView != null) BtnNoteView.Content = JtIcons.Img(grid ? "listView" : "gridView", 20);
            if (grid) RebuildNoteGrid();
        }

        private void RebuildNoteGrid()
        {
            if (NoteGridPanel == null) return;
            NoteGridPanel.Children.Clear();

            var source = (ListNotes?.ItemsSource as System.Collections.IEnumerable)?.Cast<StickyNote>()
                         ?? _notes.AsEnumerable();

            foreach (var note in source)
            {
                var card = BuildNoteCard(note);
                NoteGridPanel.Children.Add(card);
            }
        }

        private Border BuildNoteCard(StickyNote note)
        {
            var bg = note.BackgroundColor;
            System.Windows.Media.Color bgColor;
            try { bgColor = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(bg); }
            catch { bgColor = System.Windows.Media.Color.FromRgb(0x2A, 0x2A, 0x2A); }

            var fg = note.TextColor;
            System.Windows.Media.Color fgColor;
            try { fgColor = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(fg); }
            catch { fgColor = System.Windows.Media.Colors.White; }

            var card = new Border
            {
                Width = 160, MinHeight = 120,
                MaxHeight = 180,
                Margin = new System.Windows.Thickness(4),
                CornerRadius = new System.Windows.CornerRadius(6),
                Background = new System.Windows.Media.SolidColorBrush(bgColor),
                Cursor = System.Windows.Input.Cursors.Hand,
                Tag = note,
                Padding = new System.Windows.Thickness(8),
            };
            card.MouseLeftButtonDown += (_, e) =>
            {
                if (e.ClickCount == 2) OpenNoteWindow(note);
                else { ListNotes.SelectedItem = note; }
            };
            card.MouseRightButtonUp += (_, e) =>
            {
                ListNotes.SelectedItem = note;
                var ctxMenu = BuildNoteContextMenu(note);
                card.ContextMenu = ctxMenu;
                ctxMenu.IsOpen = true;
                e.Handled = true;
            };

            var sp = new System.Windows.Controls.StackPanel();
            sp.Children.Add(new System.Windows.Controls.TextBlock
            {
                Text = note.Title,
                FontSize = 12,
                FontWeight = System.Windows.FontWeights.SemiBold,
                Foreground = new System.Windows.Media.SolidColorBrush(fgColor),
                TextTrimming = System.Windows.TextTrimming.CharacterEllipsis,
                Margin = new System.Windows.Thickness(0, 0, 0, 4),
            });
            sp.Children.Add(new System.Windows.Controls.TextBlock
            {
                Text = note.SafeContentPreview,
                FontSize = OldGamers.Wpf.Shared.Controls.TypeRamp.Caption,
                Foreground = new System.Windows.Media.SolidColorBrush(fgColor),
                TextWrapping = System.Windows.TextWrapping.Wrap,
                MaxHeight = 100,
                TextTrimming = System.Windows.TextTrimming.CharacterEllipsis,
            });
            card.Child = sp;
            return card;
        }

        // ── F14: Memoboard canvas view ────────────────────────────────────────
        private void BtnMemoboardView_Checked(object sender, RoutedEventArgs e)
        {
            if (NoteGridScroll != null) NoteGridScroll.Visibility = Visibility.Collapsed;
            if (ListNotes      != null) ListNotes.Visibility      = Visibility.Collapsed;
            if (MemoboardScroll != null) MemoboardScroll.Visibility = Visibility.Visible;
            BuildMemoboard();
        }

        private void BuildMemoboard()
        {
            if (MemoboardCanvas == null) return;
            MemoboardCanvas.Children.Clear();
            foreach (var note in _notes.Where(n => !n.IsVisible == false || true))
            {
                var card = BuildMemoCard(note);
                Canvas.SetLeft(card, double.IsNaN(note.Left) || note.Left < 0 ? 20 + (_notes.IndexOf(note) % 5) * 210 : note.Left % 2800);
                Canvas.SetTop(card,  double.IsNaN(note.Top)  || note.Top  < 0 ? 20 + (_notes.IndexOf(note) / 5) * 160 : note.Top  % 1800);
                MemoboardCanvas.Children.Add(card);
            }
        }

        private Border BuildMemoCard(StickyNote note)
        {
            var card = new Border
            {
                Width = 180, Height = 130,
                CornerRadius = new CornerRadius(6),
                Background = new System.Windows.Media.SolidColorBrush(
                    (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(note.BackgroundColor)),
                Cursor = Cursors.SizeAll,
                Tag = note,
            };

            var sp = new StackPanel { Margin = new Thickness(8) };
            sp.Children.Add(new TextBlock
            {
                Text = note.Title, FontSize = OldGamers.Wpf.Shared.Controls.TypeRamp.Caption, FontWeight = FontWeights.SemiBold,
                Foreground = new System.Windows.Media.SolidColorBrush(
                    (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(note.TextColor)),
                TextTrimming = TextTrimming.CharacterEllipsis,
            });
            sp.Children.Add(new TextBlock
            {
                Text = note.SafeContentPreview, FontSize = OldGamers.Wpf.Shared.Controls.TypeRamp.Caption,
                Foreground = new System.Windows.Media.SolidColorBrush(
                    (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(note.TextColor)),
                TextWrapping = TextWrapping.Wrap,
                MaxHeight = 80,
                TextTrimming = TextTrimming.CharacterEllipsis,
            });
            card.Child = sp;

            // Drag handlers
            card.MouseLeftButtonDown += MemoCard_MouseDown;
            card.MouseMove           += MemoCard_MouseMove;
            card.MouseLeftButtonUp   += MemoCard_MouseUp;
            // Double-click to open full note
            card.MouseLeftButtonDown += (s, e) =>
            {
                if (e.ClickCount == 2 && (s as Border)?.Tag is StickyNote n)
                    OpenNoteWindow(n);
            };
            card.MouseRightButtonUp += (_, e) =>
            {
                var ctxMenu = BuildNoteContextMenu(note);
                card.ContextMenu = ctxMenu;
                ctxMenu.IsOpen = true;
                e.Handled = true;
            };
            return card;
        }

        private void MemoCard_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is Border card && card.Tag is StickyNote note)
            {
                _memoDragCard  = card;
                _memoDragNote  = note;
                _memoDragStart = e.GetPosition(MemoboardCanvas);
                card.CaptureMouse();
            }
        }

        private void MemoCard_MouseMove(object sender, MouseEventArgs e)
        {
            if (_memoDragCard == null || e.LeftButton != MouseButtonState.Pressed) return;
            var pos = e.GetPosition(MemoboardCanvas);
            double newLeft = Canvas.GetLeft(_memoDragCard) + (pos.X - _memoDragStart.X);
            double newTop  = Canvas.GetTop(_memoDragCard)  + (pos.Y - _memoDragStart.Y);
            newLeft = Math.Max(0, Math.Min(newLeft, MemoboardCanvas.Width  - _memoDragCard.Width));
            newTop  = Math.Max(0, Math.Min(newTop,  MemoboardCanvas.Height - _memoDragCard.Height));
            Canvas.SetLeft(_memoDragCard, newLeft);
            Canvas.SetTop(_memoDragCard,  newTop);
            _memoDragStart = pos;
        }

        private void MemoCard_MouseUp(object sender, MouseButtonEventArgs e)
        {
            if (_memoDragCard == null) return;
            _memoDragNote.Left = Canvas.GetLeft(_memoDragCard);
            _memoDragNote.Top  = Canvas.GetTop(_memoDragCard);
            _memoDragCard.ReleaseMouseCapture();
            var savedMemoNote = _memoDragNote;
            _memoDragCard = null;
            _memoDragNote = null;
            DataStore.SaveNote(savedMemoNote);
        }

        // ══════════════════════════════════════════════════════════════════════
        //  Tasks
        // ══════════════════════════════════════════════════════════════════════
        private void BtnNewTask_Click(object sender, RoutedEventArgs e) => AddQuickTask();
        private void CmdNewTask_Executed(object sender, ExecutedRoutedEventArgs e)
        {
            if (_activeTab == "Tasks") AddQuickTask();
            else if (_activeTab == "Notes") CreateNote();
        }

        private void AddQuickTask()
        {
            string rawText = TxtQuickTask.Text.Trim();
            if (string.IsNullOrEmpty(rawText)) return;

            // F4: Natural Language parsing
            var nlp = NoteStation.Core.NlpTaskParser.Parse(rawText);
            string title = string.IsNullOrWhiteSpace(nlp.CleanTitle) ? rawText : nlp.CleanTitle;

            DateTime? due = TaskDatePicker.SelectedDate;
            TimeSpan? dueTime = null;
            bool hasDueTime = false;
            string timeStr = TxtDueTime?.Text.Trim() ?? "";
            if (!string.IsNullOrEmpty(timeStr) && TimeSpan.TryParseExact(timeStr, new[] { @"h\:mm", @"hh\:mm", @"H\:mm", @"HH\:mm" }, null, out var ts))
            {
                dueTime    = ts;
                hasDueTime = true;
            }

            if (!string.IsNullOrEmpty(nlp.UnparsedDatePhrase) && !due.HasValue)
            {
                string hint = $"\u26A0 Couldn't read a date from \"{nlp.UnparsedDatePhrase}\" — added without a due date.";
                TxtQuickTaskHint.Text       = hint;
                TxtQuickTaskHint.Visibility = Visibility.Visible;
                NoteStation.Controls.StatusAnnouncer.Announce(hint, TxtQuickTaskHint);
            }

            // NLP may supply due date/time if not already provided by picker
            if (!due.HasValue && nlp.DueDate.HasValue) due = nlp.DueDate.Value.Date;
            if (!hasDueTime && nlp.DueTime.HasValue) { dueTime = nlp.DueTime; hasDueTime = true; }

            var task = new TaskItem
            {
                Id         = _nextTaskId++,
                SortOrder  = _tasks.Count,
                Title      = title,
                HasDueDate = due.HasValue,
                DueDate    = due,
                HasDueTime = hasDueTime,
                DueTime    = dueTime,
                CreatedAt  = DateTime.Now,
                ModifiedAt = DateTime.Now,
            };

            // Apply NLP-parsed fields
            if (nlp.DueDate.HasValue)
            {
                task.HasDueDate = true;
                task.DueDate    = nlp.DueDate;
            }
            if (nlp.DueTime.HasValue)
            {
                task.HasDueTime = true;
                task.DueTime    = nlp.DueTime;
            }
            if (nlp.Priority > 0) task.Priority = nlp.Priority;
            if (nlp.Recurrence != "None") task.RecurrenceRule = nlp.Recurrence;

            _tasks.Add(task);
            TxtQuickTask.Text = "";
            TaskDatePicker.SelectedDate = null;
            if (TxtDueTime != null) TxtDueTime.Text = "";
            DataStore.SaveTask(task);
            UpdateStatus();
            ApplyFilter();
            UpdateTrayBadge();
        }

        private void CreateTaskFromTitle(string title)
        {
            var task = new TaskItem
            {
                Id         = _nextTaskId++,
                SortOrder  = _tasks.Count,
                Title      = title,
                CreatedAt  = DateTime.Now,
                ModifiedAt = DateTime.Now,
            };
            _tasks.Add(task);
            DataStore.SaveTask(task);
            UpdateStatus();
            ApplyFilter();
            UpdateTrayBadge();
        }

        private void CreateTaskInFolder(int? folderId)
        {
            var task = new TaskItem
            {
                Id         = _nextTaskId++,
                SortOrder  = _tasks.Count,
                Title      = "New Task",
                FolderId   = folderId,
                CreatedAt  = DateTime.Now,
                ModifiedAt = DateTime.Now,
            };
            _tasks.Add(task);
            DataStore.SaveTask(task);
            UpdateStatus();
            ApplyFilter();
        }

        private void TxtQuickTask_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter) { AddQuickTask(); return; }
            TxtQuickTaskHint.Visibility = Visibility.Collapsed;
        }

        private void BtnAddTask_Click(object sender, RoutedEventArgs e) => AddQuickTask();

        private void TaskList_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateStatus();

        private void TaskList_KeyDown(object sender, KeyEventArgs e)
        {
            if (TaskList.SelectedItem is not TaskItem task) return;
            if (e.Key == Key.Space)
            {
                CompleteTask(task, !task.IsCompleted);
                e.Handled = true;
            }
            else if (e.Key == Key.Delete)
            {
                DeleteTaskFromView(task);
                e.Handled = true;
            }
        }

        private void BtnClipSort_Click(object sender, RoutedEventArgs e)
        {
            _clipSortMode = (_clipSortMode + 1) % 2;
            TxtClipSortLabel.Text = $"Sort: {_clipSortLabels[_clipSortMode]}";
            ApplyFilter();
        }

        // D2-4: Note sort cycle
        private void BtnNoteSort_Click(object sender, RoutedEventArgs e)
        {
            _noteSortMode = (_noteSortMode + 1) % 4;
            TxtNoteSortLabel.Text = $"Sort: {_noteSortLabels[_noteSortMode]}";
            ApplyFilter();
        }

        private static readonly string[] _taskSortLabels = new[] { "Default", "Due Date", "Title", "Starred", "Rank #" };

        private void BtnTaskSort_Click(object sender, RoutedEventArgs e)
        {
            _taskSortMode = (_taskSortMode + 1) % 5;
            TxtTaskSortLabel.Text = $"Sort: {_taskSortLabels[_taskSortMode]}";
            ApplyFilter();
        }

        private void BtnDueFilter_Click(object sender, RoutedEventArgs e)
        {
            _taskDueDateFilter = (string)((Button)sender).Tag;
            UpdateDueFilterChips();
            ApplyFilter();
        }

        private void UpdateDueFilterChips()
        {
            var chips = new[] { BtnDueAll, BtnDueOverdue, BtnDueToday, BtnDueWeek };
            foreach (var btn in chips)
            {
                bool active = (string)btn.Tag == _taskDueDateFilter;
                btn.FontWeight      = active ? FontWeights.SemiBold : FontWeights.Normal;
                btn.BorderThickness = active ? new Thickness(0, 0, 0, 2) : new Thickness(0);
                btn.BorderBrush     = active
                    ? App.GetBrush("BrushBlueText")
                    : System.Windows.Media.Brushes.Transparent;
                AutomationProperties.SetItemStatus(btn, active ? "Selected" : "");
            }
        }

        private void BtnTaskComplete_Click(object sender, RoutedEventArgs e)
        {
            if (TaskList.SelectedItem is not TaskItem task) return;
            CompleteTask(task, !task.IsCompleted);
        }

        private void TaskComplete_Checked(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.CheckBox cb && cb.DataContext is TaskItem t)
                CompleteTask(t, true);
        }

        private void TaskComplete_Unchecked(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.CheckBox cb && cb.DataContext is TaskItem t)
                CompleteTask(t, false);
        }

        /// <summary>Centralised completion toggle — handles CompletedAt + recurrence spawn.</summary>
        private void CompleteTask(TaskItem task, bool completed)
        {
            // F6: Habit streak logic — short-circuit before normal completion
            if (completed && task.IsHabit)
            {
                var lastDone = task.LastHabitCompleted;
                var today = DateTime.Today;
                if (lastDone.HasValue && lastDone.Value.Date == today.AddDays(-1))
                    task.HabitStreak++;   // consecutive day
                else if (!lastDone.HasValue || lastDone.Value.Date < today.AddDays(-1))
                    task.HabitStreak = 1; // streak broken or first time
                // if already done today, don't double-count
                if (!lastDone.HasValue || lastDone.Value.Date < today)
                    task.LastHabitCompleted = DateTime.Now;
                // Re-spawn: uncheck immediately so habit resets
                task.IsCompleted = false;
                task.CompletedAt = null;
                DataStore.SaveTask(task);
                _trayIcon?.ShowBalloonTip(2500, "NoteStation — Habit Streak",
                    $"{task.Title} — Streak: {task.HabitStreak} day{(task.HabitStreak != 1 ? "s" : "")}",
                    System.Windows.Forms.ToolTipIcon.Info);
                UpdateStatus();
                _taskOverlay?.Refresh(_tasks);
                UpdateTrayBadge();
                Controls.StatusAnnouncer.Announce($"Habit done: {task.Title}. Streak {task.HabitStreak} day{(task.HabitStreak != 1 ? "s" : "")}");
                return;
            }

            task.IsCompleted = completed;
            task.CompletedAt = completed ? DateTime.Now : (DateTime?)null;

            if (completed && task.RecurrenceRule != "None" && task.HasDueDate && task.DueDate.HasValue)
            {
                DateTime nextDate = task.RecurrenceRule switch
                {
                    "Daily"   => task.DueDate.Value.AddDays(1),
                    "Weekly"  => task.DueDate.Value.AddDays(7),
                    "Monthly" => task.DueDate.Value.AddMonths(1),
                    _         => task.DueDate.Value,
                };
                var next = new TaskItem
                {
                    Id             = _nextTaskId++,
                    Title          = task.Title,
                    Notes          = task.Notes,
                    HasDueDate     = true,
                    DueDate        = nextDate,
                    HasDueTime     = task.HasDueTime,
                    DueTime        = task.DueTime,
                    Priority       = task.Priority,
                    IsStarred      = task.IsStarred,
                    RecurrenceRule = task.RecurrenceRule,
                    Category       = task.Category,
                    FolderId       = task.FolderId,
                    AccentColor    = task.AccentColor,
                    SortOrder      = task.SortOrder + 1,
                    CreatedAt      = DateTime.Now,
                    ModifiedAt     = DateTime.Now,
                };
                _tasks.Add(next);
                DataStore.SaveTask(next);
                ApplyFilter();
            }

            TasksChanged(task);
            Controls.StatusAnnouncer.Announce(completed ? $"Task completed: {task.Title}" : $"Task marked not done: {task.Title}");
        }

        private void TasksChanged(TaskItem changedTask = null)
        {
            // D3-6: Auto-archive completed tasks after N days
            int archiveDays = AppPrefs.Instance.AutoArchiveDays;
            if (archiveDays > 0)
            {
                var cutoff = DateTime.Now.AddDays(-archiveDays);
                bool anyArchived = false;
                foreach (var t in _tasks)
                {
                    if (!t.IsArchived && t.IsCompleted && t.CompletedAt.HasValue && t.CompletedAt.Value < cutoff)
                    {
                        t.IsArchived = true;
                        anyArchived  = true;
                        DataStore.SaveTask(t);
                    }
                }
                if (anyArchived) BuildFolderTree("Tasks");
            }
            // Save the single task that triggered this change (if auto-archive didn't already cover it)
            if (changedTask != null)
                DataStore.SaveTask(changedTask);
            else
                DataStore.SaveTasks(_tasks); // fallback for callers that don't pass a specific task
            UpdateStatus();
            _taskOverlay?.Refresh(_tasks);
            UpdateTrayBadge();
        }

        private void DeleteTask(TaskItem task)
        {
            CommitPendingDelete();          // flush any prior pending delete

            _pendingDeleteTask = task;
            SoftDeleteTask(task);
            _taskOverlay?.Refresh(_tasks);
            BuildFolderTree("Tasks");   // synchronous, render-reliable count refresh
            ApplyFilter();
            UpdateStatus();
            UpdateTrayBadge();

            // Undo toast (WCAG 2.2.1/2.2.3): lifetime from Settings (0 = until dismissed),
            // countdown pauses while hovered/focused, announced via UIA notification.
            int seconds = AppPrefs.Instance.UndoToastSeconds;
            string Truncate(string s) => s.Length > 40 ? s.Substring(0, 37) + "…" : s;
            string ToastText(string title) => seconds > 0
                ? $"Deleted \"{Truncate(title)}\" — {seconds}s to undo"
                : $"Deleted \"{Truncate(title)}\" — press Undo to restore";
            TxtUndoToast.Text = ToastText(task.Title);
            DeleteUndoToast.Visibility = Visibility.Visible;
            Controls.StatusAnnouncer.Announce($"Task deleted. {(seconds > 0 ? $"Undo available for {seconds} seconds." : "Undo available.")}", DeleteUndoToast, important: true);

            _deleteTimer?.Stop();
            _deleteTimer = null;
            if (seconds <= 0) return;   // until dismissed
            _deleteTimer = new System.Windows.Threading.DispatcherTimer
                { Interval = TimeSpan.FromSeconds(1) };
            _deleteTimer.Tick += (_, __) =>
            {
                if (_deleteToastHeld) return;
                seconds--;
                if (seconds <= 0)
                    CommitPendingDelete();
                else
                    TxtUndoToast.Text = ToastText(_pendingDeleteTask?.Title ?? "");
            };
            _deleteTimer.Start();
        }

        private bool _deleteToastHeld;
        private void DeleteUndoToast_HoldChanged(object sender, MouseEventArgs e)
            => _deleteToastHeld = DeleteUndoToast.IsMouseOver || DeleteUndoToast.IsKeyboardFocusWithin;
        private void DeleteUndoToast_FocusChanged(object sender, DependencyPropertyChangedEventArgs e)
            => _deleteToastHeld = DeleteUndoToast.IsMouseOver || DeleteUndoToast.IsKeyboardFocusWithin;
        private void BtnDismissUndo_Click(object sender, RoutedEventArgs e) => CommitPendingDelete();

        private void OpenTaskDetail(TaskItem task)
        {
            var dlg = new TaskDetailDialog(task, _clips) { Owner = this };
            if (dlg.ShowDialog() == true && dlg.Saved)
            {
                DataStore.SaveTask(task);
                ApplyFilter();
                UpdateStatus();
            }
        }

        private void CommitPendingDelete()
        {
            _deleteTimer?.Stop();
            _deleteTimer = null;
            if (_pendingDeleteTask != null)
            {
                _pendingDeleteTask = null;
            }
            if (DeleteUndoToast != null)
                DeleteUndoToast.Visibility = Visibility.Collapsed;
        }

        private void BtnUndoDelete_Click(object sender, RoutedEventArgs e)
        {
            if (_pendingDeleteTask == null) return;
            _deleteTimer?.Stop();
            _deleteTimer = null;
            var restored = _pendingDeleteTask;
            _pendingDeleteTask = null;
            RestoreTask(restored);
            DeleteUndoToast.Visibility = Visibility.Collapsed;
            _taskOverlay?.Refresh(_tasks);
            BuildFolderTree("Tasks");   // synchronous, render-reliable count refresh
            ApplyFilter();
            UpdateStatus();
        }

        private void BtnTaskDelete_Click(object sender, RoutedEventArgs e)
        {
            var selected = TaskList.SelectedItems.OfType<TaskItem>().ToList();
            if (selected.Count == 0) return;
            if (selected.Count == 1) { DeleteTaskFromView(selected[0]); return; }
            // Bulk delete (no undo toast for multi)
            CommitPendingDelete();
            bool recent = _selectedTaskNode?.NodeType == FolderNodeType.Recent;
            foreach (var t in selected)
            {
                if (recent && t.FolderId.HasValue) { t.HiddenFromRecent = true; DataStore.SaveTask(t); }
                else SoftDeleteTask(t);
            }
            BuildFolderTree("Tasks");   // synchronous, render-reliable count refresh
            ApplyFilter();
        }

        private void CmdDelete_Executed(object sender, ExecutedRoutedEventArgs e)
        {
            if (_activeTab == "Clipboard" && ListClipboard.SelectedItem != null)
                BtnClipDelete_Click(sender, e);
            else if (_activeTab == "Tasks" && TaskList.SelectedItem is TaskItem t)
                DeleteTaskFromView(t);
        }

        private void BtnTaskTimer_Click(object sender, RoutedEventArgs e)
        {
            if (TaskList.SelectedItem is not TaskItem task)
            {
                MessageBox.Show("Select a task first, then click Push to Timer.",
                    "NoteStation", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            PushTaskToTimerStudio(task);
        }

        // ── F3: Focus session (Pomodoro → TimerStudio) ────────────────────────
        private void BtnFocusTask_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is not TaskItem task) return;
            int minutes = AppPrefs.Instance.PomodoroMinutes > 0 ? AppPrefs.Instance.PomodoroMinutes : 25;
            PushTaskToTimerStudio(task);
            _trayIcon?.ShowBalloonTip(2500, "NoteStation — Focus Session",
                $"Focus session started: {task.Title} ({minutes} min)",
                System.Windows.Forms.ToolTipIcon.Info);
        }

        private void PushTaskToTimerStudio(TaskItem task)
        {
            // Build prefill DateTime from task due date + time (if available)
            DateTime? prefill = null;
            if (task.HasDueDate && task.DueDate.HasValue)
            {
                prefill = task.DueDate.Value.Date;
                if (task.HasDueTime && task.DueTime.HasValue)
                    prefill = prefill.Value.Add(task.DueTime.Value);
            }

            var dlg = new TimerPushDialog(task.Title, prefill) { Owner = this };
            if (dlg.ShowDialog() != true) return;

            DateTime target = dlg.ResultDateTime!.Value;
            string path = AppPrefs.Instance.TimerStudioTimersPath;

            // If path not set, auto-detect from default TimerStudio location
            if (string.IsNullOrWhiteSpace(path))
            {
                path = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "TimerStudio", "timers.json");
            }

            // IPC contract (2026-09-07 review, Feature Gaps): this read-modify-write
            // crosses process boundaries with TimerStudio, which can be reading/saving
            // timers.json (autosave, manual edit) at the same moment. A named systemwide
            // Mutex — shared with TimerStudio's own TimerPersistence.Save/Load — gives
            // both sides real mutual exclusion instead of relying on timestamp checks.
            using var ipcLock = new System.Threading.Mutex(false, TimerStudioIpc.TimersJsonMutexName);
            bool haveLock = false;
            try
            {
                haveLock = ipcLock.WaitOne(TimeSpan.FromSeconds(5));
            }
            catch (AbandonedMutexException)
            {
                // The other process crashed/exited while holding the lock. The file
                // itself is still protected by WriteAtomic-style File.Move, so it's
                // safe to proceed treating the lock as acquired.
                haveLock = true;
            }
            if (!haveLock)
            {
                MessageBox.Show(
                    "TimerStudio's timer file is busy (another save is in progress). Try again in a moment.",
                    "NoteStation — Timer Pushed", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                // Load existing timers (or start with empty array)
                JsonArray timers;
                if (File.Exists(path))
                {
                    var existing = JsonNode.Parse(File.ReadAllText(path));
                    timers = existing?.AsArray() ?? new JsonArray();
                }
                else
                {
                    timers = new JsonArray();
                    Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
                }

                // Find next Id (max existing + 1). TimerStudio's own writer uses
                // camelCase keys (its JSON context's PropertyNamingPolicy); accept
                // either case here since older files on disk may still be PascalCase.
                int nextId = 1;
                foreach (var node in timers)
                {
                    if (node is JsonObject obj &&
                        (obj.TryGetPropertyValue("id", out var idNode) ||
                         obj.TryGetPropertyValue("Id", out idNode)) &&
                        idNode is JsonNode idVal)
                    {
                        int id = idVal.GetValue<int>();
                        if (id >= nextId) nextId = id + 1;
                    }
                }

                // Build a DateTime (Mode=2) calendar event entry using TimerStudio's
                // actual on-disk contract (camelCase — see TimerStudio's
                // TimerStudioJsonContext/TimerDto in Core/JsonSerialization.cs).
                // TimerStudio's reader is now case-insensitive as a safety net, but
                // writing the real contract here is the actual fix, not the net.
                // schemaVersion: extra field, ignored by TimerStudio's deserializer
                // (default UnmappedMemberHandling=Skip) — reserved so a future
                // breaking change to this push contract can be detected on read.
                var entry = new JsonObject
                {
                    ["id"]             = nextId,
                    ["schemaVersion"]  = TimerStudioIpc.PushContractVersion,
                    ["name"]           = task.Title,
                    ["mode"]           = 2,    // 2 = DateTime (calendar event)
                    ["targetDateTime"] = target.ToString("s"), // ISO 8601: yyyy-MM-ddTHH:mm:ss
                    ["notes"]          = task.Notes ?? "",
                    ["alertFontSize"]  = 36,
                    ["alertVolume"]    = 1.0,
                    ["snoozeDurationMinutes"] = 5,
                };

                timers.Add(entry);

                // Atomic write
                string tmp      = path + ".tmp";
                string timerJson = System.Text.Json.JsonSerializer.Serialize(
                    timers, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(tmp, timerJson);
                File.Move(tmp, path, overwrite: true);

                // Update task model
                task.PushedToTimer = true;
                TasksChanged();

                string displayDt = target.ToString("ddd, MMM d 'at' h:mm tt");
                _trayIcon?.ShowBalloonTip(3000, "Calendar Event Pushed",
                    $"\"{task.Title}\" — {displayDt} (ID {nextId})",
                    System.Windows.Forms.ToolTipIcon.Info);

                MessageBox.Show(
                    $"\"{task.Title}\" pushed to Timer Studio as a calendar event.\n\nTarget: {displayDt}\nTimer ID: {nextId}",
                    "NoteStation — Timer Pushed",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Failed to write to timers.json:\n\n{ex.Message}\n\nVerify the Timer Studio path in Settings.",
                    "NoteStation — Push Failed",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                if (haveLock) ipcLock.ReleaseMutex();
            }
        }

        // ── Jump to Today ─────────────────────────────────────────────────────
        private void BtnJumpToday_Click(object sender, RoutedEventArgs e)
        {
            var today = DateTime.Today;
            // Find first task due today or overdue (not completed, not archived)
            var target = _tasks
                .Where(t => !t.IsCompleted && !t.IsArchived && t.HasDueDate && t.DueDate.HasValue && t.DueDate.Value.Date <= today)
                .OrderBy(t => t.DueDate)
                .FirstOrDefault();
            if (target == null)
            {
                // No overdue — scroll to top
                if (TaskList.Items.Count > 0)
                    TaskList.ScrollIntoView(TaskList.Items[0]);
                return;
            }
            TaskList.SelectedItem = target;
            TaskList.ScrollIntoView(target);
        }

        // ── Drag-to-reorder ───────────────────────────────────────────────────
        private void TaskCard_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is Border card && card.DataContext is TaskItem item)
            {
                TaskList.SelectedItem = item;

                // Double-click → open detail dialog; clear pending drag
                if (e.ClickCount == 2)
                {
                    _dragItem = null;
                    _dragCard = null;
                    OpenTaskDetail(item);
                    e.Handled = true;
                    return;
                }

                // Record drag candidates; actual DoDragDrop fires in Window_MouseMove after threshold
                if (e.ButtonState == MouseButtonState.Pressed)
                {
                    _dragItem  = item;
                    _dragCard  = card;
                    _dragStart = e.GetPosition(null);
                }
            }
        }

        private void TaskCard_DragOver(object sender, DragEventArgs e)
        {
            // Only accept reorder drags (TaskItem type), not folder-tree drags
            e.Effects = e.Data.GetDataPresent(typeof(TaskItem))
                ? DragDropEffects.Move
                : DragDropEffects.None;
            e.Handled = true;
        }

        private void TaskCard_Drop(object sender, DragEventArgs e)
        {
            if (!e.Data.GetDataPresent(typeof(TaskItem))) return;

            // Drag-reorder only makes sense in manual sort mode (SortOrder field).
            // Any active sort (_taskSortMode != 0) reorders by due-date / title / star,
            // so a positional move would immediately be undone by the next ApplyFilter.
            if (_taskSortMode != 0) { _dragItem = null; _dragCard = null; return; }

            if (sender is Border card && card.DataContext is TaskItem target && _dragItem != null && _dragItem != target)
            {
                // Always operate on the master _tasks list so SortOrder is global,
                // not the potentially-filtered TaskList.ItemsSource.
                int fromIdx = _tasks.IndexOf(_dragItem);
                int toIdx   = _tasks.IndexOf(target);
                if (fromIdx >= 0 && toIdx >= 0)
                {
                    // Delta-only persistence (2026-09-07 review, Feature Gaps): only the
                    // tasks whose SortOrder actually moved need a disk write — a drag
                    // between adjacent rows should not rewrite the whole task list.
                    var oldOrder = _tasks.ToDictionary(t => t, t => t.SortOrder);
                    _tasks.Move(fromIdx, toIdx);
                    for (int i = 0; i < _tasks.Count; i++) _tasks[i].SortOrder = i;
                    var changed = _tasks.Where(t => oldOrder[t] != t.SortOrder).ToList();
                    // L-4: async — a synchronous full-list rewrite here visibly
                    // stalls the drag-drop UI on larger task lists.
                    if (changed.Count > 0) DataStore.SaveTasksAsync(changed);
                    ApplyFilter();
                }
            }
            _dragItem = null;
            _dragCard = null;
        }

        // ══════════════════════════════════════════════════════════════════════
        //  Category chips bar
        // ══════════════════════════════════════════════════════════════════════
        private void RefreshCategoryChips()
        {
            CategoryChips.Children.Clear();

            void AddChip(string label, string cat)
            {
                bool active = _activeCategory == cat;
                var btn = new Button
                {
                    Style       = Application.Current.TryFindResource("FlatButton") as Style,
                    Content     = label,
                    Tag         = cat,
                    Padding     = new Thickness(10, 4, 10, 4),
                    Margin      = new Thickness(0, 0, 6, 4),
                    FontSize    = OldGamers.Wpf.Shared.Controls.TypeRamp.Caption,
                    FontWeight  = active ? FontWeights.SemiBold : FontWeights.Normal,
                    Background  = active
                                    ? App.GetBrush("BrushBlue")
                                    : App.GetBrush("BrushSurf"),
                    Foreground  = active
                                    ? App.GetBrush("BrushOnAccent")
                                    : App.GetBrush("BrushText"),
                    // Active chip: fill + BrushText outline + SemiBold (not colour-only)
                    BorderBrush = active ? App.GetBrush("BrushText") : System.Windows.Media.Brushes.Transparent,
                };
                AutomationProperties.SetItemStatus(btn, active ? "Selected" : "");
                AutomationProperties.SetName(btn,
                    string.IsNullOrEmpty(cat) ? "Show all categories" : $"Filter by category: {label}");
                btn.Click += (s, e) =>
                {
                    _activeCategory = (string)((Button)s).Tag;
                    RefreshCategoryChips();
                    ApplyFilter();
                };
                CategoryChips.Children.Add(btn);
            }

            AddChip("All", "");
            foreach (string cat in App.GetAllCategories(_clips, _notes, _tasks))
                AddChip(cat, cat);
        }

        private void BtnManageCategories_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new CategoryManagerDialog(
                getAllCategories: () => App.GetAllCategories(_clips, _notes, _tasks),
                getCount: cat =>
                    _clips.Count(c => string.Equals(c.Category, cat, StringComparison.OrdinalIgnoreCase)) +
                    _notes.Count(n => string.Equals(n.Category, cat, StringComparison.OrdinalIgnoreCase)) +
                    _tasks.Count(t => string.Equals(t.Category, cat, StringComparison.OrdinalIgnoreCase)),
                owner: this);

            dlg.CategoriesChanged += (_, __) =>
            {
                RefreshCategoryChips();
                ApplyFilter();
            };
            dlg.ShowDialog();
            RefreshCategoryChips();
            ApplyFilter();
        }

        private ContextMenu BuildCategoryMenu(Action<string> assignCategory)
        {
            var menu = new ContextMenu();

            var noneItem = new MenuItem { Header = "(none)" };
            noneItem.Click += (_, __) => assignCategory("");
            menu.Items.Add(noneItem);

            menu.Items.Add(new Separator());

            foreach (string cat in App.GetAllCategories(_clips, _notes, _tasks))
            {
                string captured = cat;
                var color = App.CategoryColor(cat);
                var dot = new Ellipse
                {
                    Width = 10, Height = 10, Margin = new Thickness(0, 0, 6, 0),
                    Fill = new SolidColorBrush(color),
                };
                var sp = new StackPanel { Orientation = Orientation.Horizontal };
                sp.Children.Add(dot);
                sp.Children.Add(new TextBlock { Text = captured });

                var mi = new MenuItem { Header = sp };
                mi.Click += (_, __) => assignCategory(captured);
                menu.Items.Add(mi);
            }

            menu.Items.Add(new Separator());
            var manageItem = new MenuItem { Header = "Manage categories…" };
            manageItem.Click += (_, __) => BtnManageCategories_Click(null, null);
            menu.Items.Add(manageItem);

            return menu;
        }

        private void ClipCategoryDot_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is not ClipboardItem clip) return;
            var menu = BuildCategoryMenu(cat =>
            {
                clip.Category = cat;
                DataStore.SaveClip(clip);
                RefreshCategoryChips();
                ApplyFilter();
            });
            menu.IsOpen = true;
        }

        private void NoteCategoryDot_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is not StickyNote note) return;
            var menu = BuildCategoryMenu(cat =>
            {
                note.Category = cat;
                DataStore.SaveNote(note);
                RefreshCategoryChips();
                ApplyFilter();
            });
            menu.IsOpen = true;
        }

        private void TaskCategoryDot_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is not TaskItem task) return;
            var menu = BuildCategoryMenu(cat =>
            {
                task.Category = cat;
                DataStore.SaveTask(task);
                RefreshCategoryChips();
                ApplyFilter();
            });
            menu.IsOpen = true;
        }

        // ══════════════════════════════════════════════════════════════════════
        //  Search / filter
        // ══════════════════════════════════════════════════════════════════════
        private void ApplyFilter()
        {
            _notesSidebar?.Refresh(_notes);
            string q   = TxtSearch?.Text.Trim().ToLowerInvariant() ?? "";
            string cat = _activeCategory;

            bool MatchCat(string itemCat) =>
                string.IsNullOrEmpty(cat) ||
                string.Equals(itemCat, cat, StringComparison.OrdinalIgnoreCase);

            if (_activeTab == "Clipboard")
            {
                var node = _selectedClipNode;
                bool searchingClips = !string.IsNullOrEmpty(TxtSearch?.Text.Trim());
                // Search spans every clip, including ones filed away in folders — only the
                // unfiltered node views (Recent/Pinned/folder) restrict scope when not searching.
                IEnumerable<ClipboardItem> src = (searchingClips && node?.NodeType != FolderNodeType.RecentlyDeleted)
                    ? _clips
                    : node?.NodeType switch
                    {
                        // Recent = the loose pool only (unpinned, unfoldered) — pinned items
                        // and items filed into a folder already have their own dedicated views,
                        // so showing them here too made Recent balloon far past what
                        // ClipboardMaxHistory actually keeps.
                        FolderNodeType.Recent    => _clips.Where(c => !c.HiddenFromRecent && !c.IsPinned && !c.FolderId.HasValue).OrderByDescending(c => c.CapturedAt),
                        FolderNodeType.Pinned    => _clips.Where(c => c.IsPinned),
                        FolderNodeType.SavedRoot => _clips.Where(c => c.FolderId.HasValue),
                        FolderNodeType.UserFolder      => _clips.Where(c => c.FolderId == node.FolderId),
                        FolderNodeType.RecentlyDeleted => (IEnumerable<ClipboardItem>)_deletedClips,
                        _ => (IEnumerable<ClipboardItem>)_clips,
                    };
                src = src.Where(c => MatchCat(c.Category));
                // F8: Regex mode
                string searchText = q; // q is already lowered; use original TxtSearch.Text for regex
                string rawSearch = TxtSearch?.Text.Trim() ?? "";
                if (_clipSearchIsRegex && !string.IsNullOrEmpty(rawSearch))
                {
                    try
                    {
                        var rx = new System.Text.RegularExpressions.Regex(rawSearch,
                            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                        src = src.Where(c => rx.IsMatch(c.Text ?? "") || rx.IsMatch(c.Preview ?? "") || rx.IsMatch(c.Title ?? ""));
                    }
                    catch { /* invalid regex — show all */ }
                }
                else if (!string.IsNullOrEmpty(rawSearch))
                {
                    src = src.Where(c =>
                        (c.Text?.Contains(rawSearch, StringComparison.OrdinalIgnoreCase) ?? false) ||
                        (c.Preview?.Contains(rawSearch, StringComparison.OrdinalIgnoreCase) ?? false) ||
                        (c.Title?.Contains(rawSearch, StringComparison.OrdinalIgnoreCase) ?? false));
                }

                // Sort (only when not already sorted by folder node type)
                if (node == null || node.NodeType == FolderNodeType.All || node.NodeType == FolderNodeType.UserFolder)
                {
                    src = _clipSortMode switch
                    {
                        1 => src.OrderBy(c => c.Title ?? "", StringComparer.OrdinalIgnoreCase),
                        _ => src.OrderByDescending(c => c.CapturedAt),
                    };
                }

                if (searchingClips) src = src.GroupBy(c => c.Id).Select(g => g.First()); // dedupe matches

                // Plain List snapshot — a single reset (no per-item notifications) and
                // no ObservableCollection overhead; the view is rebuilt on every change.
                var clipFiltered = src.ToList();
                _filteredCount = clipFiltered.Count < _clips.Count ? clipFiltered.Count : -1;
                ListClipboard.ItemsSource = clipFiltered;
            }
            else if (_activeTab == "Notes")
            {
                var node = _selectedNoteNode;
                bool searchingNotes = !string.IsNullOrEmpty(q);
                // Search spans every note, including ones filed away in folders — only the
                // unfiltered node views (Recent/Pinned/folder) restrict scope when not searching.
                IEnumerable<StickyNote> src = (searchingNotes && node?.NodeType != FolderNodeType.RecentlyDeleted)
                    ? _notesView
                    : node?.NodeType switch
                    {
                        // Recent = the loose pool only — see the Clipboard Recent case above.
                        FolderNodeType.Recent    => _notesView.Where(n => !n.HiddenFromRecent && !n.IsPinned && !n.FolderId.HasValue).OrderByDescending(n => n.ModifiedAt),
                        FolderNodeType.Pinned    => _notesView.Where(n => n.IsPinned),
                        FolderNodeType.SavedRoot => _notesView.Where(n => n.FolderId.HasValue),
                        FolderNodeType.UserFolder      => _notesView.Where(n => n.FolderId == node.FolderId),
                        FolderNodeType.RecentlyDeleted => (IEnumerable<StickyNote>)_deletedNotes,
                        _ => (IEnumerable<StickyNote>)_notesView,
                    };
                src = src.Where(n =>
                    MatchCat(n.Category) &&
                    (string.IsNullOrEmpty(q) ||
                     (n.Title   ?? "").ToLowerInvariant().Contains(q) ||
                     (n.Content ?? "").ToLowerInvariant().Contains(q)));

                // D2-4: sort (only when not already sorted by folder node type)
                if (node == null || node.NodeType == FolderNodeType.All || node.NodeType == FolderNodeType.UserFolder)
                {
                    src = _noteSortMode switch
                    {
                        1 => src.OrderByDescending(n => n.CreatedAt),
                        2 => src.OrderBy(n => n.Title ?? ""),
                        3 => src,   // Manual: keep collection order
                        _ => src.OrderByDescending(n => n.ModifiedAt),
                    };
                }

                if (searchingNotes) src = src.GroupBy(n => n.Id).Select(g => g.First()); // dedupe matches

                var notesFiltered = src.ToList();
                _filteredCount = notesFiltered.Count < _notesView.Count ? notesFiltered.Count : -1;
                ListNotes.ItemsSource = notesFiltered;
            }
            else if (_activeTab == "Tasks")
            {
                UpdateDueFilterChips();

                var node = _selectedTaskNode;
                bool searchingTasks = !string.IsNullOrEmpty(q);
                // D3-6: Archived folder shows only archived; all others hide archived.
                // Search spans every non-deleted task, including ones filed in folders/archived.
                IEnumerable<TaskItem> src = (searchingTasks && node?.NodeType != FolderNodeType.RecentlyDeleted)
                    ? (IEnumerable<TaskItem>)_tasks
                    : node?.NodeType switch
                    {
                        // Recent = the loose pool only — see the Clipboard Recent case above.
                        FolderNodeType.Recent    => _tasks.Where(t => !t.IsArchived && !t.HiddenFromRecent && !t.IsStarred && !t.FolderId.HasValue).OrderByDescending(t => t.CreatedAt),
                        FolderNodeType.Pinned    => _tasks.Where(t => t.IsStarred && !t.IsArchived),
                        FolderNodeType.SavedRoot => _tasks.Where(t => t.FolderId.HasValue && !t.IsArchived),
                        FolderNodeType.UserFolder => _tasks.Where(t => t.FolderId == node.FolderId && !t.IsArchived),
                        FolderNodeType.Archived        => _tasks.Where(t => t.IsArchived),
                        FolderNodeType.RecentlyDeleted => (IEnumerable<TaskItem>)_deletedTasks,
                        _ => _tasks.Where(t => !t.IsArchived),
                    };
                src = src.Where(t =>
                    MatchCat(t.Category) &&
                    (node?.NodeType == FolderNodeType.RecentlyDeleted || AppPrefs.Instance.ShowCompletedTasks || !t.IsCompleted) &&
                    (string.IsNullOrEmpty(q) ||
                     t.Title.ToLowerInvariant().Contains(q) ||
                     t.Notes.ToLowerInvariant().Contains(q)));

                // Due-date filter
                var today = DateTime.Today;
                src = _taskDueDateFilter switch
                {
                    "Overdue" => src.Where(t => t.HasDueDate && t.DueDate.HasValue && t.DueDate.Value.Date < today && !t.IsCompleted),
                    "Today"   => src.Where(t => t.HasDueDate && t.DueDate.HasValue && t.DueDate.Value.Date == today),
                    "Week"    => src.Where(t => t.HasDueDate && t.DueDate.HasValue && t.DueDate.Value.Date >= today && t.DueDate.Value.Date <= today.AddDays(6)),
                    _         => src,
                };

                // F6: Habit filter
                if (RbHabit?.IsChecked == true)
                    src = src.Where(t => t.IsHabit);

                // Sort (only when not already sorted by folder node type)
                if (node?.NodeType is null or FolderNodeType.SavedRoot or FolderNodeType.UserFolder)
                {
                    src = _taskSortMode switch
                    {
                        1 => src.OrderBy(t => t.DueDate ?? DateTime.MaxValue),
                        2 => src.OrderBy(t => t.Title, StringComparer.OrdinalIgnoreCase),
                        3 => src.OrderByDescending(t => t.IsStarred).ThenBy(t => t.SortOrder),
                        4 => src.OrderBy(t => t.PriorityRank == 0).ThenBy(t => t.PriorityRank),
                        _ => src,
                    };
                }

                if (searchingTasks) src = src.GroupBy(t => t.Id).Select(g => g.First()); // dedupe matches

                var tasksFiltered = src.ToList();
                _filteredCount = tasksFiltered.Count < _tasks.Count ? tasksFiltered.Count : -1;
                TaskList.ItemsSource = tasksFiltered;
            }

            // Empty-state hint visibility
            if (TxtClipEmpty != null)
                TxtClipEmpty.Visibility = (ListClipboard.ItemsSource as System.Collections.IEnumerable)
                    ?.Cast<object>().Any() == false ? Visibility.Visible : Visibility.Collapsed;
            if (TxtNoteEmpty != null)
                TxtNoteEmpty.Visibility = (ListNotes.ItemsSource as System.Collections.IEnumerable)
                    ?.Cast<object>().Any() == false ? Visibility.Visible : Visibility.Collapsed;
            if (TxtTaskEmpty != null)
                TxtTaskEmpty.Visibility = (TaskList.ItemsSource as System.Collections.IEnumerable)
                    ?.Cast<object>().Any() == false ? Visibility.Visible : Visibility.Collapsed;

            if (AppPrefs.Instance.NoteViewMode == 1) RebuildNoteGrid();

            UpdateStatus();
        }

        private void TxtSearch_TextChanged(object sender, TextChangedEventArgs e)
            => ApplyFilter();

        // ── F8: Regex search ──────────────────────────────────────────────────
        private void BtnRegexToggle_Checked(object sender, RoutedEventArgs e)
        {
            _clipSearchIsRegex = true;
            ApplyFilter();
        }
        private void BtnRegexToggle_Unchecked(object sender, RoutedEventArgs e)
        {
            _clipSearchIsRegex = false;
            ApplyFilter();
        }

        // ══════════════════════════════════════════════════════════════════════
        //  Status bar
        // ══════════════════════════════════════════════════════════════════════
        private void UpdateStatus()
        {
            // D5-6: always show all-tab summary; active tab shows filtered count prefix
            string filterPart = _filteredCount >= 0 ? $"{_filteredCount} of " : "";

            int clipCount  = _clips.Count;
            int pinned     = _clips.Count(c => c.IsPinned);
            int noteCount  = _notes.Count;
            int visible    = _notes.Count(n => n.IsVisible);
            int taskCount  = _tasks.Count;
            int done       = _tasks.Count(t => t.IsCompleted);

            string clipSeg  = _activeTab == "Clipboard"
                ? $"{filterPart}{clipCount} clips ({pinned} pinned)"
                : $"{clipCount} clips";
            string noteSeg  = _activeTab == "Notes"
                ? $"{filterPart}{noteCount} notes ({visible} showing)"
                : $"{noteCount} notes";
            string taskSeg  = _activeTab == "Tasks"
                ? $"{filterPart}{taskCount} tasks ({done} done)"
                : $"{taskCount} tasks";

            // Status-bar summary (non-interactive — NavRow tabs are the single way to switch tabs)
            if (BtnStatusClips != null) BtnStatusClips.Text = clipSeg;
            if (BtnStatusNotes != null) BtnStatusNotes.Text = noteSeg;
            if (BtnStatusTasks != null) BtnStatusTasks.Text = taskSeg;
        }

        // ══════════════════════════════════════════════════════════════════════
        //  Settings applied at runtime
        // ══════════════════════════════════════════════════════════════════════
        private void ApplySettings()
        {
            var p = AppPrefs.Instance;
            _mouseHook.EnableXButton1         = p.MousePreviousClip == 1;
            _mouseHook.EnableXButton2         = p.MouseNextClip     == 2;
            _mouseHook.EnableScroll           = p.MouseScrollCycle;
            _mouseHook.ScrollRequiresModifier = p.MouseScrollRequiresModifier;
            RegisterShowHideHotkey();
        }

        // ══════════════════════════════════════════════════════════════════════
        //  Keyboard preset quick-paste (Ctrl+1..5)
        // ══════════════════════════════════════════════════════════════════════
        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);

            // ? key opens Hotkeys dialog (skip when typing in a text field)
            if (e.Key == Key.OemQuestion && !(e.OriginalSource is TextBox || e.OriginalSource is RichTextBox))
            {
                var dlg = new HotkeysDialog { Owner = this };
                dlg.ShowDialog();
                e.Handled = true;
                return;
            }

            if ((e.KeyboardDevice.Modifiers & ModifierKeys.Control) != 0)
            {
                // Ctrl+Z — undo last clip delete (only when focus is not in a text box)
                if (e.Key == Key.Z
                    && !(e.OriginalSource is TextBox || e.OriginalSource is RichTextBox))
                {
                    UndoLastClipDelete();
                    e.Handled = true;
                    return;
                }

                int idx = e.Key switch
                {
                    Key.D1 => 0, Key.D2 => 1, Key.D3 => 2, Key.D4 => 3, Key.D5 => 4,
                    Key.NumPad1 => 0, Key.NumPad2 => 1, Key.NumPad3 => 2,
                    Key.NumPad4 => 3, Key.NumPad5 => 4,
                    _ => -1
                };
                if (idx >= 0 && idx < _presetBtns.Length && _presetBtns[idx].IsEnabled)
                {
                    e.Handled = true;
                    _presetBtns[idx].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                }
            }
        }

        private void UndoLastClipDelete()
        {
            if (_lastDeletedClip == null) return;
            var restored = _lastDeletedClip;
            _clips.Insert(0, restored);
            _lastDeletedClip = null;
            DataStore.SaveClip(restored);
            _clipOverlay?.Refresh(_clips, _templates);
            ApplyFilter();
            UpdateStatus();
        }

        // ── F9: Clip tag parsing ───────────────────────────────────────────────
        private static List<string> ParseClipTags(string text)
        {
            if (string.IsNullOrEmpty(text)) return new List<string>();
            var matches = System.Text.RegularExpressions.Regex.Matches(text, @"#([A-Za-z][A-Za-z0-9_]{1,30})\b");
            return matches.Select(m => m.Groups[1].Value.ToLowerInvariant()).Distinct().Take(8).ToList();
        }

        /// <summary>
        /// Strips null terminators and invisible Unicode markers that some apps
        /// append to CF_UNICODETEXT clipboard data (shows up as Ä, #2, #$%, etc.).
        /// </summary>
        private static string SanitizeClipboardText(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;

            // Trim null terminators — apps like SSMS write CF_UNICODETEXT with a trailing \0
            // and occasionally the buffer has a few garbage bytes after it.
            text = text.TrimEnd('\0');

            // Strip well-known invisible Unicode markers injected by browsers/office apps
            text = text.Replace("﻿", "")   // BOM / zero-width no-break space
                       .Replace("​", "")   // zero-width space
                       .Replace("‌", "")   // zero-width non-joiner
                       .Replace("‍", "")   // zero-width joiner
                       .Replace("�", "");  // Unicode replacement character

            return text;
        }

        // ══════════════════════════════════════════════════════════════════════
        //  Closing
        // ══════════════════════════════════════════════════════════════════════
        // Resize grip: Controls/AccessibleResizeGrip (mouse + keyboard) replaced the mouse-only handler.

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            // Snapshot geometry + folder panel widths on every close attempt — including
            // "minimize to tray"/"minimize to taskbar" — not just a real exit, so sizes
            // persist even if the user never fully quits the app between sessions.
            AppPrefs.Instance.SnapshotFrom(this);
            AppPrefs.Instance.ClipFolderPanelWidth = ClipTreeCol.ActualWidth > 50 ? ClipTreeCol.ActualWidth : 180;
            AppPrefs.Instance.NoteFolderPanelWidth = NoteTreeCol.ActualWidth > 50 ? NoteTreeCol.ActualWidth : 180;
            AppPrefs.Instance.TaskFolderPanelWidth = TaskTreeCol.ActualWidth > 50 ? TaskTreeCol.ActualWidth : 180;
            AppPrefs.Instance.DeferredSave();

            // Close (✕) button behavior — unless a real exit was forced (tray Exit, etc.)
            if (!_forceExit && !App.ShutdownRequested)
            {
                // First-run: no preference chosen yet — ask the user.
                if (string.IsNullOrEmpty(AppPrefs.Instance.CloseButtonAction))
                {
                    e.Cancel = true;
                    var dlg = new CloseActionDialog { Owner = this };
                    if (dlg.ShowDialog() == true)
                    {
                        AppPrefs.Instance.CloseButtonAction = dlg.ChosenAction;
                        AppPrefs.Instance.CloseToTray = dlg.ChosenAction == "Tray";
                        AppPrefs.Instance.DeferredSave();
                        // Re-trigger close so the chosen action is applied immediately.
                        Close();
                    }
                    return;
                }

                switch (AppPrefs.Instance.CloseButtonAction)
                {
                    case "Tray":
                        e.Cancel = true;
                        HideToTray();
                        return;
                    case "Taskbar":
                        e.Cancel = true;
                        // Stay on the taskbar even if minimize-to-tray is on. StateChanged fires
                        // asynchronously, so clear the guard after it has been processed.
                        _suppressTrayMinimize = true;
                        WindowState = WindowState.Minimized;
                        Dispatcher.BeginInvoke(new Action(() => _suppressTrayMinimize = false),
                            System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                        return;
                    // "Exit" (or anything else) → fall through and close normally
                }
            }

            // Geometry + panel widths already snapshotted above; just record the active tab.
            AppPrefs.Instance.ActiveTab = _activeTab;

            // Close overlays first — CloseForReal() writes geometry + opacity + font into AppPrefs
            _clipOverlay?.CloseForReal();
            _taskOverlay?.CloseForReal();
            _notesSidebar?.CloseForReal();

            AppPrefs.Instance.Save();

            // Snapshot note window positions, then close (unsubscribing handlers)
            foreach (var (id, wnd) in _noteWindows)
            {
                var note = _notes.FirstOrDefault(n => n.Id == id);
                if (note != null && wnd.IsLoaded)
                {
                    note.Left = wnd.Left;
                    note.Top  = wnd.Top;
                    // While locked, wnd.Width/Height are the shrunk lock-prompt
                    // footprint (see StickyNoteWindow.ApplyLockState) — never
                    // persist that as the note's real size. note.Width/Height
                    // already hold the correct pre-lock size in that case.
                    if (!note.IsLocked)
                    {
                        note.Width  = wnd.Width;
                        note.Height = wnd.Height;
                    }
                }
            }
            foreach (var kvp in _noteWindows.ToList())
                CloseNoteWindow(kvp.Key, kvp.Value);
            _noteWindows.Clear();
            CommitPendingDelete();          // flush undo-pending task delete before save
            DataStore.SaveNotes(_notes);
            DataStore.SaveClipboard(_clips);
            DataStore.SaveTasks(_tasks);
            DataStore.SaveTemplates(_templates);
            DataStore.AutoBackup();         // rotate 3-deep auto-backup on clean shutdown

            // Stop all timers
            _attachMonitorTimer?.Stop();
            _clipCaptureTimer?.Stop();
            _clipSaveTimer?.Stop();
            _deleteTimer?.Stop();
            _reminderTimer?.Stop();
            _reminderTimer = null;

            // Dispose
            _showHideHotkey?.Dispose();
            _quickCaptureHotkey?.Dispose();
            _commandPaletteHotkey?.Dispose();
            _pasteFromKbHotkey?.Dispose();
            _clipMonitor?.Dispose();
            _mouseHook?.Dispose();
            _trayIcon?.Dispose();
        }

        // ══════════════════════════════════════════════════════════════════════
        //  Tray icon
        // ══════════════════════════════════════════════════════════════════════
        private void InitTrayIcon()
        {
            _trayIcon = new System.Windows.Forms.NotifyIcon { Text = "NoteStation", Visible = false };

            try
            {
                var uri    = new Uri("pack://application:,,,/Assets/app.ico");
                using var stream = Application.GetResourceStream(uri)?.Stream;
                if (stream != null) _trayIcon.Icon = new Icon(stream);
            }
            catch { /* icon optional */ }

            // Context menu: Show | Add quick task | — | Exit
            var ctx = new System.Windows.Forms.ContextMenuStrip();
            ctx.Items.Add("Show NoteStation", null, (_, __) => RestoreFromTray());
            ctx.Items.Add(new System.Windows.Forms.ToolStripSeparator());
            var quickTaskItem = new System.Windows.Forms.ToolStripMenuItem("Add quick task…");
            quickTaskItem.Click += (_, __) =>
            {
                Dispatcher.Invoke(() =>
                {
                    if (PromptInput("New Task", "Enter task title:", "", out string title)
                        && !string.IsNullOrWhiteSpace(title))
                        CreateTaskFromTitle(title.Trim());
                });
            };
            ctx.Items.Add(quickTaskItem);
            var quickNoteItem = new System.Windows.Forms.ToolStripMenuItem("Add quick note…");
            quickNoteItem.Click += (_, __) => Dispatcher.Invoke(() => CreateNote());
            ctx.Items.Add(quickNoteItem);
            var startupNotesItem = new System.Windows.Forms.ToolStripMenuItem("Show Startup Notes");
            startupNotesItem.Click += (_, __) => Dispatcher.Invoke(() =>
            {
                // Reopens every note flagged "show on startup" — a no-op for ones already open
                // (OpenNoteWindow just activates them), so this is safe to run any time.
                foreach (var note in _notes.Where(n => n.ShowOnStartup && !n.IsDeleted).ToList())
                {
                    note.IsVisible = true;
                    OpenNoteWindow(note);
                    DataStore.SaveNote(note);
                }
            });
            ctx.Items.Add(startupNotesItem);
            ctx.Items.Add(new System.Windows.Forms.ToolStripSeparator());
            ctx.Items.Add("Exit", null, (_, __) => { _forceExit = true; Dispatcher.Invoke(Close); });
            _trayIcon.ContextMenuStrip = ctx;
            _trayIcon.DoubleClick  += (_, __) => Dispatcher.Invoke(RestoreFromTray);
            _trayIcon.MouseClick  += (s, e) =>
            {
                if (e.Button == System.Windows.Forms.MouseButtons.Left)
                    Dispatcher.Invoke(RestoreFromTray);
            };

            StateChanged += (_, __) =>
            {
                if (WindowState == WindowState.Minimized
                    && AppPrefs.Instance.MinimizeToTray
                    && !_suppressTrayMinimize)
                    HideToTray();
            };
        }

        private void HideToTray()
        {
            _trayIcon.Visible = true;
            Hide();
        }

        private void RestoreFromTray()
        {
            Show();
            WindowState = WindowState.Normal;
            Activate();
            _trayIcon.Visible = false;
        }

        /// <summary>Second launch of NoteStation (single-instance pipe) → bring this window forward.</summary>
        internal void ActivateFromExternal()
        {
            if (!IsVisible) { RestoreFromTray(); }
            else if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
            Activate();
            Topmost = true;
            Topmost = false;
            Focus();
        }

        /// <summary>
        /// Refreshes the tray tooltip with the count of incomplete tasks due today or overdue.
        /// Safe to call before the tray icon is created (no-op until then).
        /// </summary>
        private void UpdateTrayBadge()
        {
            if (_trayIcon == null) return;
            var today = DateTime.Today;
            int due = _tasks.Count(t => !t.IsArchived && !t.IsCompleted
                                     && t.HasDueDate && t.DueDate.HasValue
                                     && t.DueDate.Value.Date <= today);
            // NotifyIcon.Text is capped at 63 chars.
            _trayIcon.Text = due > 0
                ? $"NoteStation — {due} task{(due == 1 ? "" : "s")} due"
                : "NoteStation";
        }

        // ── F19: Eisenhower priority matrix view ────────────────────────────────
        private void BtnMatrixView_Checked(object sender, RoutedEventArgs e)
        {
            if (TaskList      != null) TaskList.Visibility      = Visibility.Collapsed;
            if (CalendarPanel != null) CalendarPanel.Visibility = Visibility.Collapsed;
            if (MatrixPanel   != null) MatrixPanel.Visibility   = Visibility.Visible;
            BuildMatrix();
        }

        private void BtnMatrixView_Unchecked(object sender, RoutedEventArgs e)
        {
            if (MatrixPanel != null) MatrixPanel.Visibility = Visibility.Collapsed;
            if (TaskList    != null) TaskList.Visibility    = Visibility.Visible;
        }

        private void BuildMatrix()
        {
            if (MatrixQ1 == null) return;
            // Classification logic now owned by MatrixViewModel (2026-09-07 review
            // MVVM extraction, second slice).
            var q = MatrixViewModel.Classify(_tasks);
            MatrixQ1.ItemsSource = q.UrgentImportant;
            MatrixQ2.ItemsSource = q.NotUrgentImportant;
            MatrixQ3.ItemsSource = q.UrgentNotImportant;
            MatrixQ4.ItemsSource = q.NotUrgentNotImportant;
        }

        // ── Task filter chips (Habit, …) ────────────────────────────────────────
        private void TaskFilter_Checked(object sender, RoutedEventArgs e) => ApplyFilter();
        private void TaskFilter_Unchecked(object sender, RoutedEventArgs e) => ApplyFilter();

        // NcHitTest for bottom-right resize grip on main window
        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            _hwndSource = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
            _hwndSource?.AddHook(WndProc);
            RegisterShowHideHotkey();
            RegisterQuickCaptureHotkey();
            RegisterCommandPaletteHotkey();
            RegisterPasteFromKbHotkey();
        }

        private void RegisterShowHideHotkey()
        {
            _showHideHotkey?.Dispose();
            _showHideHotkey = null;
            if (_hwndSource == null) return;
            string spec = AppPrefs.Instance.HotkeyShowHide;
            if (string.IsNullOrWhiteSpace(spec)) return;
            if (ParseHotkeySpec(spec, out uint mods, out uint vk))
            {
                _showHideHotkey = new GlobalHotkey(_hwndSource, 9001, mods, vk);
                if (_showHideHotkey.IsRegistered)
                    _showHideHotkey.Pressed += (_, __) => ToggleShowHide();
            }
        }

        private void RegisterQuickCaptureHotkey()
        {
            _quickCaptureHotkey?.Dispose();
            _quickCaptureHotkey = null;
            if (_hwndSource == null) return;
            string spec = AppPrefs.Instance.HotkeyQuickCapture;
            if (string.IsNullOrWhiteSpace(spec)) return;
            if (ParseHotkeySpec(spec, out uint mods, out uint vk))
            {
                _quickCaptureHotkey = new GlobalHotkey(_hwndSource, 9002, mods, vk);
                if (_quickCaptureHotkey.IsRegistered)
                    _quickCaptureHotkey.Pressed += (_, __) => ShowQuickCapture();
            }
        }

        private void RegisterCommandPaletteHotkey()
        {
            _commandPaletteHotkey?.Dispose();
            _commandPaletteHotkey = null;
            if (_hwndSource == null) return;
            if (ParseHotkeySpec("Ctrl+P", out uint mods, out uint vk))
            {
                _commandPaletteHotkey = new GlobalHotkey(_hwndSource, 9003, mods, vk);
                if (_commandPaletteHotkey.IsRegistered)
                    _commandPaletteHotkey.Pressed += (_, __) => ShowCommandPalette();
            }
        }

        private void RegisterPasteFromKbHotkey()
        {
            _pasteFromKbHotkey?.Dispose();
            _pasteFromKbHotkey = null;
            if (_hwndSource == null) return;
            string spec = AppPrefs.Instance.HotkeyPasteFromKeyboard;
            if (string.IsNullOrWhiteSpace(spec)) return;
            if (ParseHotkeySpec(spec, out uint mods, out uint vk))
            {
                _pasteFromKbHotkey = new GlobalHotkey(_hwndSource, 9004, mods, vk);
                if (_pasteFromKbHotkey.IsRegistered)
                    _pasteFromKbHotkey.Pressed += (_, __) => ShowClipOverlayFocused();
            }
        }

        private void ShowQuickCapture()
        {
            if (_quickCapture == null)
            {
                _quickCapture = new QuickCaptureWindow();
                _quickCapture.CaptureSubmitted += (mode, title, text) =>
                {
                    switch (mode)
                    {
                        case "Clip":
                            var clip = ClipboardItem.FromText(_nextClipId++, text);
                            if (!string.IsNullOrEmpty(title)) clip.Title = title;
                            _clips.Insert(0, clip);
                            DataStore.SaveClip(clip);
                            _clipOverlay?.Refresh(_clips, _templates);
                            ApplyFilter();
                            UpdateStatus();
                            break;
                        case "Note":
                            CreateNote(string.IsNullOrEmpty(title) ? text.Split('\n')[0] : title, text);
                            break;
                        case "Task":
                            string taskTitle = string.IsNullOrEmpty(title) ? text : title;
                            CreateTaskFromTitle(taskTitle);
                            break;
                    }
                };
            }
            _quickCapture.ShowNearCursor();
        }

        private void ShowCommandPalette()
        {
            if (_commandPalette == null)
            {
                _commandPalette = new CommandPaletteWindow();
                _commandPalette.ItemSelected += (type, id) =>
                {
                    switch (type)
                    {
                        case "Clip":
                            var clip = _clips.FirstOrDefault(c => c.Id == id);
                            if (clip != null) { SwitchTab("Clipboard"); ListClipboard.SelectedItem = clip; }
                            break;
                        case "Note":
                            var note = _notes.FirstOrDefault(n => n.Id == id);
                            if (note != null) { SwitchTab("Notes"); ListNotes.SelectedItem = note; }
                            break;
                        case "Task":
                            var task = _tasks.FirstOrDefault(t => t.Id == id);
                            if (task != null) { SwitchTab("Tasks"); TaskList.SelectedItem = task; }
                            break;
                    }
                    Show(); Activate();
                };
            }
            _commandPalette.ShowWithData(_clips.ToList(), _notes, _tasks.ToList());
        }

        private void ShowClipOverlayFocused()
        {
            // Reuse the same creation logic as BtnClipOverlay_Click
            if (_clipOverlay == null || !_clipOverlay.IsLoaded)
                BtnClipOverlay_Click(this, null);
            else
            {
                _clipOverlay.Show();
                _clipOverlay.Refresh(_clips, _templates);
                _clipOverlay.Activate();
            }
            _clipOverlay?.FocusSearchAndSelectFirst();
        }

        private static bool ParseHotkeySpec(string spec, out uint mods, out uint vk)
        {
            mods = 0; vk = 0;
            if (string.IsNullOrWhiteSpace(spec)) return false;
            var parts = spec.Split('+');
            string key = parts[parts.Length - 1].Trim();
            for (int i = 0; i < parts.Length - 1; i++)
            {
                switch (parts[i].Trim().ToLowerInvariant())
                {
                    case "ctrl": case "control": mods |= NativeMethods.MOD_CONTROL; break;
                    case "shift":                mods |= NativeMethods.MOD_SHIFT;   break;
                    case "alt":                  mods |= NativeMethods.MOD_ALT;     break;
                    case "win":                  mods |= NativeMethods.MOD_WIN;     break;
                }
            }
            vk = key.ToUpperInvariant() switch
            {
                "SPACE" => 0x20, "ENTER" => 0x0D, "TAB" => 0x09,
                "F1"  => 0x70, "F2"  => 0x71, "F3"  => 0x72, "F4"  => 0x73,
                "F5"  => 0x74, "F6"  => 0x75, "F7"  => 0x76, "F8"  => 0x77,
                "F9"  => 0x78, "F10" => 0x79, "F11" => 0x7A, "F12" => 0x7B,
                _ when key.Length == 1 => (uint)char.ToUpper(key[0]),
                _ => 0,
            };
            return vk != 0;
        }

        private void ToggleShowHide()
        {
            Dispatcher.Invoke(() =>
            {
                if (IsVisible && WindowState != WindowState.Minimized)
                    HideToTray();
                else
                    RestoreFromTray();
            });
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == NativeMethods.WM_NCHITTEST)
            {
                int sx = (short)(lParam.ToInt32() & 0xFFFF);
                int sy = (short)((lParam.ToInt32() >> 16) & 0xFFFF);
                var pos   = PointFromScreen(new System.Windows.Point(sx, sy));
                if (pos.X > ActualWidth - 24 && pos.Y > ActualHeight - 24)
                {
                    handled = true;
                    return (IntPtr)NativeMethods.HTBOTTOMRIGHT;
                }
            }
            return IntPtr.Zero;
        }

        // ══════════════════════════════════════════════════════════════════════
        //  Folder tree — auto-refresh debounce
        // ══════════════════════════════════════════════════════════════════════
        //  Coalesces bursts of collection changes (e.g. multi-delete loops) into a
        //  single full BuildFolderTree per tab on the next Background dispatch frame.
        //  Full rebuild is the only render path proven reliable (it runs at startup),
        //  so counts always repaint; debounce kills the per-item flicker/reentrancy.
        private readonly HashSet<string> _treeRefreshQueued = new();
        private void RebuildFolderTreeDebounced(string tab)
        {
            if (!_treeRefreshQueued.Add(tab)) return;   // one rebuild per frame, per tab
            Dispatcher.BeginInvoke(
                System.Windows.Threading.DispatcherPriority.Background,
                (Action)(() =>
                {
                    _treeRefreshQueued.Remove(tab);
                    BuildFolderTree(tab);
                }));
        }

        // ══════════════════════════════════════════════════════════════════════
        //  Folder tree — build
        // ══════════════════════════════════════════════════════════════════════
        private ObservableCollection<FolderNode> GetTreeNodes(string tab) =>
            tab == "Clipboard" ? _clipFolderNodes :
            tab == "Notes"     ? _noteFolderNodes : _taskFolderNodes;

        private List<AppFolder> GetTabFolders(string tab) =>
            tab == "Clipboard" ? AppPrefs.Instance.ClipboardFolders :
            tab == "Notes"     ? AppPrefs.Instance.NoteFolders :
                                 AppPrefs.Instance.TaskFolders;

        private TreeView GetTree(string tab) =>
            tab == "Clipboard" ? ClipFolderTree :
            tab == "Notes"     ? NoteFolderTree : TaskFolderTree;

        private ColumnDefinition GetTreeCol(string tab) =>
            tab == "Clipboard" ? ClipTreeCol :
            tab == "Notes"     ? NoteTreeCol : TaskTreeCol;

        /// <summary>
        /// Keeps each folder panel wide enough that no row label is ever truncated
        /// ("Recently Delet…"), while still honouring a width the user has dragged wider.
        ///
        /// Width is derived by measuring the live TreeView against an unbounded constraint
        /// rather than by adding up template constants: the row chrome (TreeViewItem border +
        /// padding, 16px expander column, 16px per nesting level, icon chip, count pill,
        /// TreeView padding) is spread across App.xaml and the row template, and every
        /// hand-tallied estimate of it came out short. Measuring asks WPF for the real number.
        ///
        /// The result is applied as the column's MinWidth, not just its Width, because the
        /// persisted panel width is restored over the top of it at startup
        /// (ClipFolderPanelWidth &amp; co.) — a stale narrow value would otherwise win and then
        /// be re-persisted on exit. MinWidth is recomputed on every rebuild, so it drops back
        /// down again when a long folder name goes away.
        /// </summary>
        private void AutoFitFolderColumn(string tab)
        {
            var col  = GetTreeCol(tab);
            var tree = GetTree(tab);
            if (col == null || tree == null) return;

            // BuildFolderTree calls this the moment it assigns ItemsSource; containers do not
            // exist and layout has not run yet at that point. Defer one dispatcher turn, and
            // collapse the burst of calls that a busy count-refresh produces into one pass.
            if (!_folderFitQueued.Add(tab)) return;
            Dispatcher.BeginInvoke(new Action(() =>
            {
                _folderFitQueued.Remove(tab);

                double natural = MeasureFolderTreeNaturalWidth(tree);
                if (natural <= 0) return;

                double target = Math.Clamp(natural, FolderPaneFloorWidth, col.MaxWidth);
                col.MinWidth = target;
                if (col.Width.IsAbsolute && col.Width.Value < target)
                    col.Width = new GridLength(target);
            }), System.Windows.Threading.DispatcherPriority.Loaded);
        }

        private const double FolderPaneFloorWidth = 120;
        private readonly HashSet<string> _folderFitQueued = new();

        /// <summary>
        /// Natural (untruncated) width of a folder tree: measure it with an unbounded
        /// constraint, read the width it asks for, then invalidate so the normal layout pass
        /// re-measures against the real constraint. Star columns inside the row template
        /// measure as Auto under an infinite constraint, so labels report their full width.
        /// </summary>
        private double MeasureFolderTreeNaturalWidth(TreeView tree)
        {
            try
            {
                tree.UpdateLayout();
                double viewportHeight = tree.ActualHeight;

                tree.Measure(new System.Windows.Size(double.PositiveInfinity, double.PositiveInfinity));
                double width  = tree.DesiredSize.Width;
                bool needsVBar = viewportHeight > 0 && tree.DesiredSize.Height > viewportHeight;

                tree.InvalidateMeasure();   // discard the off-cycle measure

                if (double.IsNaN(width) || double.IsInfinity(width) || width <= 0) return 0;

                return width
                     + 2                                                          // panel border
                     + 6                                                          // slack
                     + (needsVBar ? SystemParameters.VerticalScrollBarWidth : 0);
            }
            catch { return 0; }
        }

        private FolderNode GetSelectedNode(string tab) =>
            tab == "Clipboard" ? _selectedClipNode :
            tab == "Notes"     ? _selectedNoteNode : _selectedTaskNode;

        private void SetSelectedNode(string tab, FolderNode node)
        {
            if (tab == "Clipboard") _selectedClipNode = node;
            else if (tab == "Notes") _selectedNoteNode = node;
            else _selectedTaskNode = node;
        }

        private void BuildFolderTree(string tab)
        {
            // Always create a fresh collection — never mutate the old one in-place.
            // This avoids WPF container-reuse artefacts and eliminates the null-cycle hack.
            var nodes   = new ObservableCollection<FolderNode>();
            var folders = GetTabFolders(tab);

            string filter = (_folderFilter.TryGetValue(tab, out var ff) ? ff : "") ?? "";
            bool MatchName(string n) => filter.Length == 0 || (n ?? "").ToLowerInvariant().Contains(filter);

            // Match the Recent view's filter: Recent is the loose pool only — pinned and
            // foldered items (which have their own Pinned/folder views already) don't also
            // count here, and archived tasks are excluded.
            int recentCount = tab == "Clipboard" ? _clips.Count(c => !c.HiddenFromRecent && !c.IsPinned && !c.FolderId.HasValue) :
                              tab == "Notes"     ? _notesView.Count(n => !n.HiddenFromRecent && !n.IsPinned && !n.FolderId.HasValue) :
                                                   _tasks.Count(t => !t.IsArchived && !t.HiddenFromRecent && !t.IsStarred && !t.FolderId.HasValue);

            int pinnedCount = tab == "Clipboard" ? _clips.Count(c => c.IsPinned) :
                              tab == "Notes"     ? _notesView.Count(n => n.IsPinned) :
                                                   _tasks.Count(t => t.IsStarred);

            // ── Favorites section (only when favorite folders exist) ───────────
            var favFolders = folders.Where(fo => fo.IsFavorite && MatchName(fo.Name))
                                    .OrderBy(fo => fo.SortOrder).ThenBy(fo => fo.Name).ToList();
            if (favFolders.Count > 0)
            {
                var favSection = new FolderNode { NodeType = FolderNodeType.SectionHeader, Tab = tab, Name = "Favorites", Icon = "star", IsExpanded = true };
                foreach (var fo in favFolders)
                    favSection.Children.Add(MakeFolderNode(fo, tab));
                nodes.Add(favSection);
            }

            // ── Quick views section ────────────────────────────────────────────
            var quick = new FolderNode { NodeType = FolderNodeType.SectionHeader, Tab = tab, Name = "Quick views", Icon = "", IsExpanded = true };

            quick.Children.Add(new FolderNode { NodeType = FolderNodeType.Search, Tab = tab, Name = "Search", Icon = "search", Count = 0 });

            var recentNode = new FolderNode { NodeType = FolderNodeType.Recent, Tab = tab, Name = "Recent", Icon = "recent", Count = recentCount };
            if (tab == "Tasks") { recentNode.CountDone = _tasks.Count(t => t.IsCompleted); recentNode.CountPending = _tasks.Count(t => !t.IsCompleted); }
            quick.Children.Add(recentNode);

            var pinnedNode = new FolderNode { NodeType = FolderNodeType.Pinned, Tab = tab, Name = "Pinned", Icon = "pin", Count = pinnedCount };
            if (tab == "Tasks") { var st = _tasks.Where(t => t.IsStarred).ToList(); pinnedNode.CountDone = st.Count(t => t.IsCompleted); pinnedNode.CountPending = st.Count(t => !t.IsCompleted); }
            quick.Children.Add(pinnedNode);

            if (tab == "Tasks")
            {
                int archiveCount = _tasks.Count(t => t.IsArchived);
                var archNode = new FolderNode { NodeType = FolderNodeType.Archived, Tab = tab, Name = "Archived", Icon = "archive", Count = archiveCount };
                archNode.CountDone    = _tasks.Count(t => t.IsArchived && t.IsCompleted);
                archNode.CountPending = _tasks.Count(t => t.IsArchived && !t.IsCompleted);
                quick.Children.Add(archNode);
            }

            int deletedCount = tab == "Clipboard" ? _deletedClips.Count :
                               tab == "Notes"     ? _deletedNotes.Count :
                                                    _deletedTasks.Count;
            var deletedNode = new FolderNode
            {
                NodeType = FolderNodeType.RecentlyDeleted,
                Tab      = tab,
                Name     = "Recently Deleted",
                Icon     = "delete",
                Count    = deletedCount,
            };
            if (tab == "Tasks")
            {
                deletedNode.CountDone    = _deletedTasks.Count(t => t.IsCompleted);
                deletedNode.CountPending = _deletedTasks.Count(t => !t.IsCompleted);
            }
            quick.Children.Add(deletedNode);
            nodes.Add(quick);

            // ── Folders section (user folders, "Folders" header replaces "Saved") ──
            int savedTotal = tab == "Clipboard" ? _clips.Count(c => c.FolderId.HasValue) :
                             tab == "Notes"     ? _notesView.Count(n => n.FolderId.HasValue) :
                                                  _tasks.Count(t => t.FolderId.HasValue);

            var savedRoot = new FolderNode
            {
                NodeType   = FolderNodeType.SavedRoot,
                Tab        = tab,
                Name       = "Folders",
                Icon       = "open",
                Count      = savedTotal,
                IsExpanded = true,
            };
            if (tab == "Tasks") { var st = _tasks.Where(t => t.FolderId.HasValue).ToList(); savedRoot.CountDone = st.Count(t => t.IsCompleted); savedRoot.CountPending = st.Count(t => !t.IsCompleted); }
            BuildFolderChildren(savedRoot, folders, null, tab, filter: filter);
            nodes.Add(savedRoot);

            // Store fresh collection and bind
            if (tab == "Clipboard") _clipFolderNodes = nodes;
            else if (tab == "Notes") _noteFolderNodes = nodes;
            else _taskFolderNodes = nodes;

            var tree = GetTree(tab);
            tree.ItemsSource = nodes;
            AutoFitFolderColumn(tab);

            // Re-select previous node, else default to Recent.
            var prev = GetSelectedNode(tab);
            FolderNode toSelect = prev != null ? FindMatchingNode(nodes, prev) : null;
            toSelect ??= FindFirstByType(nodes, FolderNodeType.Recent);
            if (toSelect != null)
            {
                SetSelectedNode(tab, toSelect);
                tree.UpdateLayout();
                SelectNodeInContainer(tree, toSelect);
            }
        }

        /// <summary>Build a UserFolder tree node from a persisted folder, carrying its icon, accent, category, favorite flag and item count.</summary>
        private FolderNode MakeFolderNode(AppFolder f, string tab)
        {
            int cnt = tab == "Clipboard" ? _clips.Count(c => c.FolderId == f.Id) :
                      tab == "Notes"     ? _notesView.Count(n => n.FolderId == f.Id) :
                                           _tasks.Count(t => t.FolderId == f.Id);
            var node = new FolderNode
            {
                NodeType    = FolderNodeType.UserFolder,
                FolderId    = f.Id,
                Tab         = tab,
                Name        = f.Name,
                Category    = f.Category,
                Icon        = JtIcons.NormalizeKey(f.Icon),
                AccentColor = f.AccentColor ?? "",
                IsFavorite  = f.IsFavorite,
                Count       = cnt,
                IsExpanded  = true,
            };
            if (tab == "Tasks")
            {
                var tl = _tasks.Where(t => t.FolderId == f.Id).ToList();
                node.CountDone    = tl.Count(t => t.IsCompleted);
                node.CountPending = tl.Count(t => !t.IsCompleted);
            }
            return node;
        }

        /// <summary>Depth-first search for the first node of a given virtual type.</summary>
        private FolderNode FindFirstByType(IEnumerable<FolderNode> nodes, FolderNodeType type)
        {
            foreach (var n in nodes)
            {
                if (n.NodeType == type) return n;
                var c = FindFirstByType(n.Children, type);
                if (c != null) return c;
            }
            return null;
        }

        private void BuildFolderChildren(FolderNode parent, List<AppFolder> folders, int? parentId, string tab,
                                         HashSet<int> ancestors = null, string filter = null)
        {
            // Cycle guard: a folder whose ParentId chain loops back (e.g. self-parent from a
            // bad drag) would recurse forever → StackOverflow. Track the ancestor chain and
            // skip any folder already in it.
            ancestors ??= new HashSet<int>();
            string flt = filter ?? "";

            var children = folders
                .Where(f => f.ParentId == parentId && !ancestors.Contains(f.Id))
                .OrderBy(f => f.SortOrder)
                .ThenBy(f => f.Name);

            foreach (var f in children)
            {
                var node = MakeFolderNode(f, tab);
                BuildFolderChildren(node, folders, f.Id, tab, new HashSet<int>(ancestors) { f.Id }, flt);

                // Folder-name filter: keep a folder if it matches, or if any descendant survived.
                if (flt.Length > 0)
                {
                    bool selfMatch = (f.Name ?? "").ToLowerInvariant().Contains(flt);
                    if (!selfMatch && node.Children.Count == 0) continue;
                    node.IsExpanded = true;
                }
                parent.Children.Add(node);
            }
        }

        private void UpdateFolderCounts(string tab) => RefreshFolderCounts(tab);

        // ══════════════════════════════════════════════════════════════════════
        //  Folder tree — in-place INPC count refresh (no ItemsSource replace)
        // ══════════════════════════════════════════════════════════════════════
        private void RefreshFolderCounts(string tab)
        {
            var nodes = GetTreeNodes(tab);
            if (nodes == null || nodes.Count == 0) { BuildFolderTree(tab); return; }
            var tree   = GetTree(tab);
            var prev   = GetSelectedNode(tab);

            foreach (var node in FlattenNodes(nodes))
            {
                switch (node.NodeType)
                {
                    case FolderNodeType.Recent:
                        // Loose pool only — see BuildFolderTree's recentCount comment.
                        if (tab == "Clipboard")
                        {
                            node.Count = _clips.Count(c => !c.HiddenFromRecent && !c.IsPinned && !c.FolderId.HasValue);
                        }
                        else if (tab == "Notes")
                        {
                            node.Count = _notesView.Count(n => !n.HiddenFromRecent && !n.IsPinned && !n.FolderId.HasValue);
                        }
                        else
                        {
                            var rec = _tasks.Where(t => !t.IsArchived && !t.HiddenFromRecent && !t.IsStarred && !t.FolderId.HasValue).ToList();
                            node.Count        = rec.Count;
                            node.CountDone    = rec.Count(t => t.IsCompleted);
                            node.CountPending = rec.Count(t => !t.IsCompleted);
                        }
                        break;

                    case FolderNodeType.Pinned:
                        if (tab == "Clipboard")
                        {
                            node.Count = _clips.Count(c => c.IsPinned);
                        }
                        else if (tab == "Notes")
                        {
                            node.Count = _notesView.Count(n => n.IsPinned);
                        }
                        else
                        {
                            var st = _tasks.Where(t => t.IsStarred).ToList();
                            node.Count        = st.Count;
                            node.CountDone    = st.Count(t => t.IsCompleted);
                            node.CountPending = st.Count(t => !t.IsCompleted);
                        }
                        break;

                    case FolderNodeType.Archived:
                        node.Count        = _tasks.Count(t => t.IsArchived);
                        node.CountDone    = _tasks.Count(t => t.IsArchived && t.IsCompleted);
                        node.CountPending = _tasks.Count(t => t.IsArchived && !t.IsCompleted);
                        break;

                    case FolderNodeType.RecentlyDeleted:
                        if (tab == "Clipboard")      node.Count = _deletedClips.Count;
                        else if (tab == "Notes")     node.Count = _deletedNotes.Count;
                        else { node.Count = _deletedTasks.Count; node.CountDone = _deletedTasks.Count(t => t.IsCompleted); node.CountPending = _deletedTasks.Count(t => !t.IsCompleted); }
                        break;

                    case FolderNodeType.SavedRoot:
                        if (tab == "Clipboard")
                        {
                            node.Count = _clips.Count(c => c.FolderId.HasValue);
                        }
                        else if (tab == "Notes")
                        {
                            node.Count = _notesView.Count(n => n.FolderId.HasValue);
                        }
                        else
                        {
                            var st = _tasks.Where(t => t.FolderId.HasValue).ToList();
                            node.Count        = st.Count;
                            node.CountDone    = st.Count(t => t.IsCompleted);
                            node.CountPending = st.Count(t => !t.IsCompleted);
                        }
                        break;

                    case FolderNodeType.UserFolder when node.FolderId.HasValue:
                        int fid = node.FolderId.Value;
                        if (tab == "Clipboard")
                        {
                            node.Count = _clips.Count(c => c.FolderId == fid);
                        }
                        else if (tab == "Notes")
                        {
                            node.Count = _notesView.Count(n => n.FolderId == fid);
                        }
                        else
                        {
                            var tl = _tasks.Where(t => t.FolderId == fid).ToList();
                            node.Count        = tl.Count;
                            node.CountDone    = tl.Count(t => t.IsCompleted);
                            node.CountPending = tl.Count(t => !t.IsCompleted);
                        }
                        break;
                }
            }

            // Counts propagate via FolderNode INPC (Count → CountLabel). No view Reset
            // and no deferred reselect here — the live update path is the debounced
            // BuildFolderTree (see RebuildFolderTreeDebounced), which is render-reliable.
            // This method is kept as a lightweight in-place fallback only.
            _ = tree; _ = prev;
        }

        private IEnumerable<FolderNode> FlattenNodes(IEnumerable<FolderNode> nodes)
        {
            foreach (var n in nodes)
            {
                yield return n;
                foreach (var child in FlattenNodes(n.Children))
                    yield return child;
            }
        }

        private void SelectNode(FolderNode node, TreeView tree)
        {
            // WPF TreeView has no "select by data" API — we traverse the item containers
            if (node == null) return;
            SelectNodeInContainer(tree, node);
        }

        private bool SelectNodeInContainer(ItemsControl container, FolderNode target)
        {
            foreach (var item in container.Items)
            {
                if (item is FolderNode fn)
                {
                    var tvi = container.ItemContainerGenerator.ContainerFromItem(fn) as TreeViewItem;
                    if (tvi == null) continue;
                    if (fn == target) { tvi.IsSelected = true; tvi.BringIntoView(); return true; }
                    if (SelectNodeInContainer(tvi, target)) return true;
                }
            }
            return false;
        }

        private FolderNode FindMatchingNode(IEnumerable<FolderNode> nodes, FolderNode prev)
        {
            foreach (var n in nodes)
            {
                if (n.NodeType == prev.NodeType &&
                    (n.NodeType != FolderNodeType.UserFolder || n.FolderId == prev.FolderId))
                    return n;
                var child = FindMatchingNode(n.Children, prev);
                if (child != null) return child;
            }
            return null;
        }

        // ── Live folder-name filter ───────────────────────────────────────────
        private void FolderFilter_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (sender is not TextBox tb || tb.Tag is not string tab) return;
            _folderFilter[tab] = (tb.Text ?? "").Trim().ToLowerInvariant();
            int caret = tb.CaretIndex;
            BuildFolderTree(tab);
            // BuildFolderTree selects a node (which can grab focus) — keep the caret in the filter.
            tb.Focus();
            tb.CaretIndex = Math.Min(caret, tb.Text?.Length ?? 0);
        }

        // ── Inline rename (F2 / double-click) ─────────────────────────────────
        private void FolderTree_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.F2 || sender is not TreeView tree) return;
            var tab = tree == ClipFolderTree ? "Clipboard" : tree == NoteFolderTree ? "Notes" : "Tasks";
            if (GetSelectedNode(tab) is FolderNode node && node.NodeType == FolderNodeType.UserFolder)
            {
                BeginFolderRename(node);
                e.Handled = true;
            }
        }

        private void BeginFolderRename(FolderNode node)
        {
            if (node == null || node.NodeType != FolderNodeType.UserFolder) return;
            foreach (var n in FlattenNodes(GetTreeNodes(node.Tab))) n.IsRenaming = false;
            node.IsRenaming = true;
        }

        private void FolderRenameBox_Loaded(object sender, RoutedEventArgs e)
        {
            if (sender is not TextBox tb) return;
            tb.IsVisibleChanged -= FolderRenameBox_VisChanged;
            tb.IsVisibleChanged += FolderRenameBox_VisChanged;
            if (tb.IsVisible) { tb.Focus(); tb.SelectAll(); }
        }

        private void FolderRenameBox_VisChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (sender is TextBox tb && tb.IsVisible)
                tb.Dispatcher.BeginInvoke(new Action(() => { tb.Focus(); tb.SelectAll(); }),
                    System.Windows.Threading.DispatcherPriority.Input);
        }

        private void FolderRenameBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (sender is not TextBox tb) return;
            if (e.Key == Key.Enter)  { e.Handled = true; CommitFolderRename(tb, true); }
            else if (e.Key == Key.Escape) { e.Handled = true; CommitFolderRename(tb, false); }
        }

        private void FolderRenameBox_LostFocus(object sender, RoutedEventArgs e)
        {
            if (sender is TextBox tb) CommitFolderRename(tb, true);
        }

        private void CommitFolderRename(TextBox tb, bool save)
        {
            if (tb?.Tag is not FolderNode node || !node.IsRenaming) return;
            node.IsRenaming = false; // guard against the rebuild's LostFocus re-entering
            string tab = node.Tab;
            if (save)
            {
                string newName = (tb.Text ?? "").Trim();
                if (!string.IsNullOrEmpty(newName) && newName != node.Name)
                {
                    var folder = GetTabFolders(tab).FirstOrDefault(f => f.Id == node.FolderId);
                    if (folder != null) { folder.Name = newName; AppPrefs.Instance.DeferredSave(); }
                }
            }
            BuildFolderTree(tab);
        }

        // ══════════════════════════════════════════════════════════════════════
        //  Folder tree — selection → filter
        // ══════════════════════════════════════════════════════════════════════
        private void ClipFolderTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            if (e.NewValue is FolderNode node)
            {
                if (node.NodeType == FolderNodeType.SectionHeader) return; // headers just expand/collapse
                _selectedClipNode = node;
                // Show search box only for Search node
                TxtSearch.Visibility = node.NodeType == FolderNodeType.Search
                    ? Visibility.Visible : Visibility.Collapsed;
                if (node.NodeType == FolderNodeType.Search)
                {
                    TxtSearch.Focus();
                }
                else if (TxtSearch.Text.Length > 0)
                {
                    TxtSearch.Clear(); // leaving Search node: drop stale query (fires ApplyFilter via TextChanged)
                    return;
                }
                ApplyFilter();
            }
        }

        private void NoteFolderTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            if (e.NewValue is FolderNode node)
            {
                if (node.NodeType == FolderNodeType.SectionHeader) return;
                _selectedNoteNode = node;
                if (node.NodeType != FolderNodeType.Search && TxtSearch.Text.Length > 0)
                {
                    TxtSearch.Clear(); // leaving Search node: drop stale query (fires ApplyFilter via TextChanged)
                    return;
                }
                ApplyFilter();
            }
        }

        private void TaskFolderTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            if (e.NewValue is FolderNode node)
            {
                if (node.NodeType == FolderNodeType.SectionHeader) return;
                _selectedTaskNode = node;
                if (node.NodeType != FolderNodeType.Search && TxtSearch.Text.Length > 0)
                {
                    TxtSearch.Clear(); // leaving Search node: drop stale query (fires ApplyFilter via TextChanged)
                    return;
                }
                ApplyFilter();
            }
        }

        // ══════════════════════════════════════════════════════════════════════
        //  Folder tree — right-click context menu
        // ══════════════════════════════════════════════════════════════════════
        // ── Multi-select folder helpers ───────────────────────────────────────

        private void ClearFolderMultiSelect()
        {
            foreach (var n in _multiFolderSelection) n.IsMultiSelected = false;
            _multiFolderSelection.Clear();
        }

        private void FolderTree_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is not TreeView tree) return;

            // Hit-test to the TreeViewItem under the cursor
            var hit = e.OriginalSource as DependencyObject;
            while (hit != null && hit is not TreeViewItem)
                hit = VisualTreeHelper.GetParent(hit);
            if (hit is not TreeViewItem tvi) return;
            if (tvi.DataContext is not FolderNode node) return;

            // Double-click a user folder name → inline rename
            if (e.ClickCount == 2 && node.NodeType == FolderNodeType.UserFolder)
            {
                BeginFolderRename(node);
                e.Handled = true;
                return;
            }

            // Arm folder-to-folder drag (move folder + its contents under another folder)
            _dragStart = e.GetPosition(null);
            _draggedFolderNode = node.NodeType == FolderNodeType.UserFolder ? node : null;

            bool ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;

            if (!ctrl)
            {
                // Plain click — clear all multi-select highlights; WPF handles single select normally
                ClearFolderMultiSelect();
                return;
            }

            // Ctrl+click — toggle this node in the multi-selection
            e.Handled = true;          // prevent WPF from changing single-select
            if (node.IsMultiSelected)
            {
                node.IsMultiSelected = false;
                _multiFolderSelection.Remove(node);
            }
            else
            {
                node.IsMultiSelected = true;
                _multiFolderSelection.Add(node);
            }
        }

        private void FolderDeleteMultiple(List<FolderNode> nodes, string tab)
        {
            // Only UserFolder nodes can be deleted
            var toDelete = nodes.Where(n => n.NodeType == FolderNodeType.UserFolder).ToList();
            if (toDelete.Count == 0) return;

            string names = string.Join(", ", toDelete.Select(n => $"\"{n.Name}\""));
            var result = MessageBox.Show(
                $"Delete {toDelete.Count} folders: {names}?\n\nAll items inside will become unsorted.",
                "Delete Folders", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (result != MessageBoxResult.Yes) return;

            var folders = GetTabFolders(tab);
            var allIds  = new HashSet<int>();
            foreach (var node in toDelete) CollectFolderIds(node, allIds);

            // Unassign items in deleted folders (back to unsorted → must reappear in Recent)
            if (tab == "Clipboard")
                foreach (var c in _clips.Where(c => c.FolderId.HasValue && allIds.Contains(c.FolderId.Value)))
                    { c.FolderId = null; c.HiddenFromRecent = false; }
            else if (tab == "Notes")
                foreach (var n in _notes.Where(n => n.FolderId.HasValue && allIds.Contains(n.FolderId.Value)))
                    { n.FolderId = null; n.HiddenFromRecent = false; }
            else
                foreach (var t in _tasks.Where(t => t.FolderId.HasValue && allIds.Contains(t.FolderId.Value)))
                    { t.FolderId = null; t.HiddenFromRecent = false; }

            folders.RemoveAll(f => allIds.Contains(f.Id));
            ClearFolderMultiSelect();
            AppPrefs.Instance.DeferredSave();
            SaveCurrentTab();
            BuildFolderTree(tab);
            ApplyFilter();
        }

                private void FolderTree_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is not TreeView tree) return;
            var tab = tree == ClipFolderTree ? "Clipboard" :
                      tree == NoteFolderTree ? "Notes" : "Tasks";

            // Hit-test to find the TreeViewItem
            var hit = e.OriginalSource as DependencyObject;
            while (hit != null && hit is not TreeViewItem)
                hit = VisualTreeHelper.GetParent(hit);

            if (hit is not TreeViewItem tvi) return;
            tvi.IsSelected = true;
            e.Handled = true;

            if (tvi.DataContext is not FolderNode node) return;
            ShowFolderContextMenu(node, tab, tvi);
        }

        private void ShowFolderContextMenu(FolderNode node, string tab, FrameworkElement target)
        {
            var menu = new ContextMenu();
            bool isFolder = node.NodeType == FolderNodeType.UserFolder || node.NodeType == FolderNodeType.SavedRoot;
            int? fId = node.NodeType == FolderNodeType.UserFolder ? node.FolderId : (int?)null;

            // Quick-create shortcuts for Notes and Tasks folders
            if (isFolder && tab == "Notes")
            {
                var newNoteHere = JtIcons.Menu("New Note Here", "newNote");
                newNoteHere.Click += (_, __) => CreateNote(folderId: fId);
                menu.Items.Add(newNoteHere);
                menu.Items.Add(new Separator());
            }
            else if (isFolder && tab == "Tasks")
            {
                var newTaskHere = JtIcons.Menu("New Task Here", "newTask");
                newTaskHere.Click += (_, __) => CreateTaskInFolder(fId);
                menu.Items.Add(newTaskHere);
                menu.Items.Add(new Separator());
            }

            if (node.NodeType == FolderNodeType.SavedRoot ||
                node.NodeType == FolderNodeType.UserFolder)
            {
                var newFolder = new MenuItem { Header = "New Folder…" };
                newFolder.Click += (_, __) => FolderCreate(tab, node.NodeType == FolderNodeType.UserFolder ? node.FolderId : null);
                menu.Items.Add(newFolder);
            }

            if (node.NodeType == FolderNodeType.UserFolder)
            {
                menu.Items.Add(new Separator());

                var rename = JtIcons.Menu("Rename (F2)", "rename");
                rename.Click += (_, __) => BeginFolderRename(node);
                menu.Items.Add(rename);

                var fav = JtIcons.Menu(node.IsFavorite ? "Remove from Favorites" : "Add to Favorites", "star");
                fav.Click += (_, __) => FolderToggleFavorite(node, tab);
                menu.Items.Add(fav);

                // Icon submenu
                var iconItem = JtIcons.Menu("Icon", "folder");
                string currentIcon = JtIcons.NormalizeKey(node.Icon);
                foreach (var (key, label) in JtIcons.FolderChoices)
                {
                    string captured = key;
                    var mi = JtIcons.Menu(label, key);
                    if (key == currentIcon) mi.FontWeight = FontWeights.SemiBold;
                    System.Windows.Automation.AutomationProperties.SetName(mi, key == currentIcon ? $"Folder icon: {label} (current)" : $"Folder icon: {label}");
                    mi.Click += (_, __) => FolderSetIcon(node, tab, captured);
                    iconItem.Items.Add(mi);
                }
                menu.Items.Add(iconItem);

                // Color submenu
                var colorItem = new MenuItem { Header = "Color" };
                var noneColor = new MenuItem { Header = "(none)" };
                noneColor.Click += (_, __) => FolderSetAccent(node, tab, "");
                colorItem.Items.Add(noneColor);
                colorItem.Items.Add(new Separator());
                foreach (var hex in FolderColorChoices)
                {
                    string captured = hex;
                    var sw = new Border
                    {
                        Width = 14, Height = 14, CornerRadius = new CornerRadius(3),
                        Background = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(hex)),
                    };
                    var sp = new StackPanel { Orientation = Orientation.Horizontal };
                    sp.Children.Add(sw);
                    sp.Children.Add(new TextBlock { Text = hex, Margin = new Thickness(8, 0, 0, 0) });
                    var mi = new MenuItem { Header = sp };
                    mi.Click += (_, __) => FolderSetAccent(node, tab, captured);
                    colorItem.Items.Add(mi);
                }
                menu.Items.Add(colorItem);

                // Multi-folder delete when ≥2 user folders are Ctrl-selected
                var multiNodes = _multiFolderSelection
                    .Where(n => n.NodeType == FolderNodeType.UserFolder && n.Tab == tab)
                    .ToList();
                if (multiNodes.Count >= 2 && multiNodes.Any(n => n == node || n.FolderId == node.FolderId))
                {
                    var deleteMulti = new MenuItem { Header = $"Delete {multiNodes.Count} Folders" };
                    deleteMulti.Click += (_, __) => FolderDeleteMultiple(multiNodes, tab);
                    menu.Items.Add(deleteMulti);
                }
                else
                {
                    var delete = new MenuItem { Header = "Delete Folder" };
                    delete.Click += (_, __) => FolderDelete(node, tab);
                    menu.Items.Add(delete);
                }

                menu.Items.Add(new Separator());

                // Category submenu
                var catItem = new MenuItem { Header = "Assign Category" };
                var noneItem = new MenuItem { Header = "(none)" };
                noneItem.Click += (_, __) => { FolderAssignCategory(node, tab, ""); };
                catItem.Items.Add(noneItem);
                catItem.Items.Add(new Separator());
                foreach (string cat in App.GetAllCategories(_clips, _notes, _tasks))
                {
                    string captured = cat;
                    var dot = new Ellipse { Width = 10, Height = 10, Margin = new Thickness(0,0,6,0),
                                           Fill = new SolidColorBrush(App.CategoryColor(cat)) };
                    var sp = new StackPanel { Orientation = Orientation.Horizontal };
                    sp.Children.Add(dot);
                    sp.Children.Add(new TextBlock { Text = captured });
                    var mi = new MenuItem { Header = sp };
                    mi.Click += (_, __) => FolderAssignCategory(node, tab, captured);
                    catItem.Items.Add(mi);
                }
                menu.Items.Add(catItem);
            }

            // Empty Trash for Recently Deleted node
            if (node.NodeType == FolderNodeType.RecentlyDeleted)
            {
                var emptyTrash = JtIcons.Menu("Empty Trash", "delete");
                emptyTrash.Click += (_, __) =>
                {
                    if (tab == "Clipboard")  EmptyClipTrash_Click(null, null);
                    else if (tab == "Notes") EmptyNoteTrash_Click(null, null);
                    else                     EmptyTaskTrash_Click(null, null);
                };
                menu.Items.Add(emptyTrash);
            }

            if (menu.Items.Count == 0) return;
            target.ContextMenu = menu;
            Dispatcher.BeginInvoke(new Action(() => menu.IsOpen = true), System.Windows.Threading.DispatcherPriority.Input);
        }

        private void FolderCreate(string tab, int? parentId)
        {
            var dlg = new RenameDialog("New Folder", Owner: this) { Title = "New Folder" };
            if (dlg.ShowDialog() != true) return;
            string name = dlg.NewName?.Trim() ?? "";
            if (string.IsNullOrEmpty(name)) return;

            var folder = new AppFolder
            {
                Id       = _nextFolderId++,
                ParentId = parentId,
                Tab      = tab,
                Name     = name,
            };

            GetTabFolders(tab).Add(folder);
            AppPrefs.Instance.DeferredSave();
            BuildFolderTree(tab);
        }

        private void FolderRename(FolderNode node, string tab)
        {
            var dlg = new RenameDialog(node.Name, Owner: this) { Title = "Rename Folder" };
            if (dlg.ShowDialog() != true) return;
            string name = dlg.NewName?.Trim() ?? "";
            if (string.IsNullOrEmpty(name) || name == node.Name) return;

            var folder = GetTabFolders(tab).FirstOrDefault(f => f.Id == node.FolderId);
            if (folder == null) return;
            folder.Name = name;
            AppPrefs.Instance.DeferredSave();
            BuildFolderTree(tab);
        }

        private void FolderDelete(FolderNode node, string tab)
        {
            var result = MessageBox.Show(
                $"Delete folder \"{node.Name}\"?\n\nItems inside will become unsorted.",
                "Delete Folder", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (result != MessageBoxResult.Yes) return;

            var folders = GetTabFolders(tab);
            // Collect all descendant IDs
            var toDelete = new HashSet<int>();
            CollectFolderIds(node, toDelete);

            // Unassign items (back to unsorted → must reappear in Recent)
            if (tab == "Clipboard")
                foreach (var c in _clips.Where(c => c.FolderId.HasValue && toDelete.Contains(c.FolderId.Value))) { c.FolderId = null; c.HiddenFromRecent = false; }
            else if (tab == "Notes")
                foreach (var n in _notes.Where(n => n.FolderId.HasValue && toDelete.Contains(n.FolderId.Value))) { n.FolderId = null; n.HiddenFromRecent = false; }
            else
                foreach (var t in _tasks.Where(t => t.FolderId.HasValue && toDelete.Contains(t.FolderId.Value))) { t.FolderId = null; t.HiddenFromRecent = false; }

            folders.RemoveAll(f => toDelete.Contains(f.Id));
            AppPrefs.Instance.DeferredSave();
            SaveCurrentTab();
            BuildFolderTree(tab);
            ApplyFilter();
        }

        private void CollectFolderIds(FolderNode node, HashSet<int> ids)
        {
            if (node.FolderId.HasValue) ids.Add(node.FolderId.Value);
            foreach (var child in node.Children) CollectFolderIds(child, ids);
        }

        private void FolderAssignCategory(FolderNode node, string tab, string category)
        {
            var folder = GetTabFolders(tab).FirstOrDefault(f => f.Id == node.FolderId);
            if (folder == null) return;
            folder.Category = category;
            AppPrefs.Instance.DeferredSave();
            BuildFolderTree(tab);
        }

        // ── Per-folder favorite / icon / accent color ─────────────────────────
        private static readonly string[] FolderColorChoices =
            { "#378ADD", "#1D9E75", "#BA7517", "#D85A30", "#534AB7", "#D4537E", "#639922", "#E24B4A" };

        private void FolderToggleFavorite(FolderNode node, string tab)
        {
            var folder = GetTabFolders(tab).FirstOrDefault(f => f.Id == node.FolderId);
            if (folder == null) return;
            folder.IsFavorite = !folder.IsFavorite;
            AppPrefs.Instance.DeferredSave();
            BuildFolderTree(tab);
        }

        private void FolderSetIcon(FolderNode node, string tab, string icon)
        {
            var folder = GetTabFolders(tab).FirstOrDefault(f => f.Id == node.FolderId);
            if (folder == null) return;
            folder.Icon = JtIcons.NormalizeKey(icon);
            AppPrefs.Instance.DeferredSave();
            BuildFolderTree(tab);
        }

        private void FolderSetAccent(FolderNode node, string tab, string hex)
        {
            var folder = GetTabFolders(tab).FirstOrDefault(f => f.Id == node.FolderId);
            if (folder == null) return;
            folder.AccentColor = hex ?? "";
            AppPrefs.Instance.DeferredSave();
            BuildFolderTree(tab);
        }

        private void SaveCurrentTab()
        {
            if (_activeTab == "Clipboard") DataStore.SaveClipboard(_clips);
            else if (_activeTab == "Notes") DataStore.SaveNotes(_notes);
            else DataStore.SaveTasks(_tasks);
        }

        // ══════════════════════════════════════════════════════════════════════
        //  Item right-click — Move to folder
        // ══════════════════════════════════════════════════════════════════════
        private void ClipList_PreviewMouseRightButtonUp(object sender, MouseButtonEventArgs e)
        {
            var item = GetListItemAtMouse<ClipboardItem>(ListClipboard, e.GetPosition(ListClipboard));
            if (item == null) return;
            if (!ListClipboard.SelectedItems.Contains(item))
                ListClipboard.SelectedItem = item;

            var selected = ListClipboard.SelectedItems.OfType<ClipboardItem>().ToList();
            bool multi = selected.Count > 1;

            var menu = new ContextMenu();

            if (!multi)
            {
                var pinItem = JtIcons.Menu(item.IsPinned ? "Unpin" : "Pin", "pin");
                pinItem.Click += (_, __) => PinClip(item);
                menu.Items.Add(pinItem);

                menu.Items.Add(new Separator());

                var removeFolder = new MenuItem { Header = "Remove from folder" };
                removeFolder.Click += (_, __) => { item.FolderId = null; item.HiddenFromRecent = false; DataStore.SaveClip(item); UpdateFolderCounts("Clipboard"); ApplyFilter(); };
                menu.Items.Add(removeFolder);

                var moveFolder = new MenuItem { Header = "Move to folder" };
                BuildFolderSubmenu(moveFolder, "Clipboard", fid => { item.FolderId = fid; DataStore.SaveClip(item); UpdateFolderCounts("Clipboard"); ApplyFilter(); });
                menu.Items.Add(moveFolder);

                menu.Items.Add(new Separator());

                var renameItem = JtIcons.Menu("Rename…", "rename");
                renameItem.Click += (_, __) => RenameClip(item);
                menu.Items.Add(renameItem);

                menu.Items.Add(new Separator());

                var sendToNote = JtIcons.Menu("Send to Note", "stickyNote");
                sendToNote.Click += (_, __) =>
                {
                    string noteTitle = !string.IsNullOrWhiteSpace(item.Title) ? item.Title
                        : item.IsImage ? "Image Clip"
                        : item.Text?.Length > 50 ? item.Text[..50] + "…" : (item.Text ?? "Clip");
                    string noteBody = item.IsImage ? "" : item.Text ?? "";
                    CreateNote(noteTitle, noteBody);
                    SwitchTab("Notes");
                };
                menu.Items.Add(sendToNote);

                var sendToTask = JtIcons.Menu("Send to Task", "tasks");
                sendToTask.Click += (_, __) =>
                {
                    string taskTitle = !string.IsNullOrWhiteSpace(item.Title) ? item.Title
                        : item.IsImage ? "Image Clip"
                        : item.Text?.Length > 80 ? item.Text[..80] + "…" : (item.Text ?? "Clip");
                    CreateTaskFromTitle(taskTitle);
                    SwitchTab("Tasks");
                };
                menu.Items.Add(sendToTask);

                var saveTemplate = JtIcons.Menu("Save as Template", "copyTask");
                saveTemplate.Click += (_, __) =>
                {
                    string title = string.IsNullOrEmpty(item.Title) ? (item.Preview?.Length > 40 ? item.Preview[..40] + "…" : item.Preview) : item.Title;
                    var tmpl = new TemplateItem
                    {
                        Id         = _nextTemplateId++,
                        Title      = title ?? "Template",
                        Text       = item.Text ?? "",
                        CreatedAt  = DateTime.Now,
                        ModifiedAt = DateTime.Now,
                    };
                    _templates.Add(tmpl);
                    DataStore.SaveTemplates(_templates);
                    _trayIcon?.ShowBalloonTip(2000, "NoteStation — Template Saved", "Saved as template: " + tmpl.Title, System.Windows.Forms.ToolTipIcon.Info);
                };
                menu.Items.Add(saveTemplate);

                menu.Items.Add(new Separator());
            }
            else
            {
                var moveFolder = new MenuItem { Header = $"Move {selected.Count} items to folder" };
                BuildFolderSubmenu(moveFolder, "Clipboard", fid =>
                {
                    foreach (var c in selected) c.FolderId = fid;
                    DataStore.SaveClipboard(_clips);
                    UpdateFolderCounts("Clipboard");
                    ApplyFilter();
                });
                menu.Items.Add(moveFolder);
                menu.Items.Add(new Separator());
            }

            var deleteItem = JtIcons.Menu(multi ? $"Delete {selected.Count} Items" : "Delete", "delete");
            deleteItem.Click += (_, __) =>
            {
                _lastDeletedClip = selected.LastOrDefault();
                foreach (var c in selected)
                    DeleteClipFromView(c);
                UpdatePresets();
                _clipOverlay?.Refresh(_clips, _templates);
                BuildFolderTree("Clipboard");   // synchronous, render-reliable count refresh
                ApplyFilter();
            };
            menu.Items.Add(deleteItem);

            ((FrameworkElement)sender).ContextMenu = menu;
            Dispatcher.BeginInvoke(new Action(() => menu.IsOpen = true), System.Windows.Threading.DispatcherPriority.Input);
            e.Handled = true;
        }

        private ContextMenu BuildNoteContextMenu(StickyNote item)
        {
            var menu = new ContextMenu();

            var pinItem = JtIcons.Menu(item.IsPinned ? "Unpin" : "Pin", "pin");
            pinItem.Click += (_, __) => PinNote(item);
            menu.Items.Add(pinItem);

            menu.Items.Add(new Separator());

            var removeFolder = new MenuItem { Header = "Remove from folder" };
            removeFolder.Click += (_, __) => { item.FolderId = null; item.HiddenFromRecent = false; DataStore.SaveNote(item); UpdateFolderCounts("Notes"); ApplyFilter(); };
            menu.Items.Add(removeFolder);

            var moveFolder = new MenuItem { Header = "Move to folder" };
            BuildFolderSubmenu(moveFolder, "Notes", fid => { item.FolderId = fid; DataStore.SaveNote(item); UpdateFolderCounts("Notes"); ApplyFilter(); });
            menu.Items.Add(moveFolder);

            menu.Items.Add(new Separator());

            var startupItem = new MenuItem { Header = "Show on startup", IsCheckable = true, IsChecked = item.ShowOnStartup };
            startupItem.Click += (_, __) => { item.ShowOnStartup = startupItem.IsChecked; DataStore.SaveNote(item); };
            menu.Items.Add(startupItem);

            var sidebarItem = new MenuItem { Header = "Show in sidebar", IsCheckable = true, IsChecked = item.ShowInSidebar };
            sidebarItem.Click += (_, __) =>
            {
                item.ShowInSidebar = sidebarItem.IsChecked;
                DataStore.SaveNote(item);
                _notesSidebar?.Refresh(_notes);
            };
            menu.Items.Add(sidebarItem);

            menu.Items.Add(new Separator());

            var dupNote = JtIcons.Menu("Duplicate Note", "copyTask");
            dupNote.Click += (_, __) => CreateNote(item.Title + " (copy)", item.Content);
            menu.Items.Add(dupNote);

            var noteToTask = JtIcons.Menu("Send to Task", "tasks");
            noteToTask.Click += (_, __) =>
            {
                string taskTitle = string.IsNullOrWhiteSpace(item.Title) ? "Note Task" : item.Title;
                CreateTaskFromTitle(taskTitle);
                SwitchTab("Tasks");
            };
            menu.Items.Add(noteToTask);

            menu.Items.Add(new Separator());

            var delItem = JtIcons.Menu("Delete Note…", "delete");
            delItem.Click += (_, __) =>
            {
                var res = MessageBox.Show(
                    $"Delete note \"{item.Title}\"?\nThis cannot be undone.",
                    "NoteStation — Delete Note",
                    MessageBoxButton.YesNo, MessageBoxImage.Warning);
                if (res != MessageBoxResult.Yes) return;
                DeleteNoteFromView(item);
            };
            menu.Items.Add(delItem);

            return menu;
        }

        private void NoteList_PreviewMouseRightButtonUp(object sender, MouseButtonEventArgs e)
        {
            var item = GetListItemAtMouse<StickyNote>(ListNotes, e.GetPosition(ListNotes));
            if (item == null) return;
            // If not already in selection, replace selection with just this item
            if (!ListNotes.SelectedItems.Contains(item))
                ListNotes.SelectedItem = item;

            var selected = ListNotes.SelectedItems.OfType<StickyNote>().ToList();
            bool multi = selected.Count > 1;

            ContextMenu menu;
            if (multi)
            {
                menu = new ContextMenu();
                var delItem = JtIcons.Menu($"Delete {selected.Count} Notes…", "delete");
                delItem.Click += (_, __) =>
                {
                    var res = MessageBox.Show(
                        $"Delete {selected.Count} notes?\nThis cannot be undone.",
                        "NoteStation — Delete Note",
                        MessageBoxButton.YesNo, MessageBoxImage.Warning);
                    if (res != MessageBoxResult.Yes) return;
                    foreach (var n in selected) DeleteNoteFromView(n);
                };
                menu.Items.Add(delItem);
            }
            else
            {
                menu = BuildNoteContextMenu(item);
            }

            ((FrameworkElement)sender).ContextMenu = menu;
            Dispatcher.BeginInvoke(new Action(() => menu.IsOpen = true), System.Windows.Threading.DispatcherPriority.Input);
            e.Handled = true;
        }

        private void TaskCard_RightClick(object sender, MouseButtonEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is not TaskItem task) return;
            // Preserve multi-select: only update selection if right-clicked item isn't already selected
            if (!TaskList.SelectedItems.Contains(task))
                TaskList.SelectedItem = task;

            var selected = TaskList.SelectedItems.OfType<TaskItem>().ToList();
            bool multi = selected.Count > 1;

            var menu = new ContextMenu();

            if (multi)
            {
                // D3-5: Bulk complete/uncomplete
                var bulkComplete = JtIcons.Menu($"Complete {selected.Count} Tasks", "complete");
                bulkComplete.Click += (_, __) =>
                {
                    foreach (var t in selected) { t.IsCompleted = true; t.CompletedAt = DateTime.Now; }
                    TasksChanged();
                };
                menu.Items.Add(bulkComplete);

                var bulkUncomplete = JtIcons.Menu($"Uncomplete {selected.Count} Tasks", "undo");
                bulkUncomplete.Click += (_, __) =>
                {
                    foreach (var t in selected) { t.IsCompleted = false; t.CompletedAt = null; }
                    TasksChanged();
                };
                menu.Items.Add(bulkUncomplete);

                menu.Items.Add(new Separator());

                var folderMenuBulk = new MenuItem { Header = $"Move {selected.Count} Tasks to Folder" };
                menu.Items.Add(folderMenuBulk);
                BuildFolderSubmenu(folderMenuBulk, "Tasks", fid =>
                {
                    foreach (var t in selected) t.FolderId = fid;
                    DataStore.SaveTasks(_tasks); UpdateFolderCounts("Tasks"); ApplyFilter();
                });

                menu.Items.Add(new Separator());

                var bulkDelete = JtIcons.Menu($"Delete {selected.Count} Tasks…", "delete");
                bulkDelete.Click += (_, __) =>
                {
                    var res = MessageBox.Show(
                        $"Delete {selected.Count} tasks? This cannot be undone.",
                        "NoteStation — Delete Tasks",
                        MessageBoxButton.YesNo, MessageBoxImage.Warning);
                    if (res != MessageBoxResult.Yes) return;
                    // Delete one-by-one to preserve undo-toast on last item
                    foreach (var t in selected.ToList()) _tasks.Remove(t);
                    DataStore.SaveTasks(_tasks); _taskOverlay?.Refresh(_tasks); UpdateStatus(); ApplyFilter();
                };
                menu.Items.Add(bulkDelete);
            }
            else
            {
                // Star / Unstar
                var starItem = JtIcons.Menu(task.IsStarred ? "Unstar" : "Star", "star");
                starItem.Click += (_, __) => { task.IsStarred = !task.IsStarred; DataStore.SaveTask(task); ApplyFilter(); };
                menu.Items.Add(starItem);

                // D3-4: Priority
                var priorityMenu = JtIcons.Menu("Set Priority", "gettingStarted");
                var priNone   = new MenuItem { Header = "○ None",   IsChecked = task.Priority == 0 };
                var priLow    = new MenuItem { Header = "▼ Low",    IsChecked = task.Priority == 1 };
                var priMedium = new MenuItem { Header = "▲ Medium", IsChecked = task.Priority == 2 };
                var priHigh   = new MenuItem { Header = "▲▲ High",  IsChecked = task.Priority == 3 };
                priNone.Click   += (_, __) => { task.Priority = 0; DataStore.SaveTask(task); ApplyFilter(); };
                priLow.Click    += (_, __) => { task.Priority = 1; DataStore.SaveTask(task); ApplyFilter(); };
                priMedium.Click += (_, __) => { task.Priority = 2; DataStore.SaveTask(task); ApplyFilter(); };
                priHigh.Click   += (_, __) => { task.Priority = 3; DataStore.SaveTask(task); ApplyFilter(); };
                priorityMenu.Items.Add(priNone);
                priorityMenu.Items.Add(priLow);
                priorityMenu.Items.Add(priMedium);
                priorityMenu.Items.Add(priHigh);
                menu.Items.Add(priorityMenu);

                menu.Items.Add(new Separator());

                // Reorder alternatives (WCAG 2.5.7 — non-drag for dragging movements)
                var moveUp = new MenuItem { Header = "Move Up" };
                moveUp.Click += (_, __) => MoveTaskRelative(task, -1);
                menu.Items.Add(moveUp);

                var moveDown = new MenuItem { Header = "Move Down" };
                moveDown.Click += (_, __) => MoveTaskRelative(task, +1);
                menu.Items.Add(moveDown);

                menu.Items.Add(new Separator());

                // Folder assignment
                var folderMenu = new MenuItem { Header = "Move to Folder" };
                menu.Items.Add(folderMenu);
                void AddFolder(MenuItem parent, IEnumerable<FolderNode> nodes)
                {
                    foreach (var node in nodes)
                    {
                        if (node.NodeType != FolderNodeType.UserFolder) continue;
                        int captured = node.FolderId!.Value;
                        var mi = new MenuItem { Header = node.Name };
                        mi.Click += (_, __) => { task.FolderId = captured; DataStore.SaveTask(task); UpdateFolderCounts("Tasks"); ApplyFilter(); };
                        parent.Items.Add(mi);
                        if (node.Children.Count > 0) AddFolder(mi, node.Children);
                    }
                }
                var savedRoot = GetTreeNodes("Tasks").FirstOrDefault(n => n.NodeType == FolderNodeType.SavedRoot);
                if (savedRoot != null) AddFolder(folderMenu, savedRoot.Children);
                if (folderMenu.Items.Count == 0)
                    folderMenu.Items.Add(new MenuItem { Header = "(no folders)", IsEnabled = false });

                var removeFolder = new MenuItem { Header = "Remove from Folder" };
                removeFolder.Click += (_, __) => { task.FolderId = null; task.HiddenFromRecent = false; DataStore.SaveTask(task); UpdateFolderCounts("Tasks"); ApplyFilter(); };
                menu.Items.Add(removeFolder);

                menu.Items.Add(new Separator());

                // D3-6: Archive / Unarchive
                var archItem = JtIcons.Menu(task.IsArchived ? "Unarchive" : "Archive", "archive");
                archItem.Click += (_, __) =>
                {
                    task.IsArchived = !task.IsArchived;
                    DataStore.SaveTask(task);
                    BuildFolderTree("Tasks");
                    ApplyFilter();
                };
                menu.Items.Add(archItem);

                menu.Items.Add(new Separator());

                var deleteItem = new MenuItem { Header = "Delete Task…" };
                deleteItem.Click += (_, __) => DeleteTaskFromView(task);
                menu.Items.Add(deleteItem);
            }

            ((FrameworkElement)sender).ContextMenu = menu;
            Dispatcher.BeginInvoke(new Action(() => menu.IsOpen = true), System.Windows.Threading.DispatcherPriority.Input);
            e.Handled = true;
        }

        private void MoveTaskRelative(TaskItem task, int delta)
        {
            int idx = _tasks.IndexOf(task);
            int newIdx = Math.Clamp(idx + delta, 0, _tasks.Count - 1);
            if (idx == newIdx) return;
            // Delta-only persistence — see TaskCard_Drop for rationale.
            var oldOrder = _tasks.ToDictionary(t => t, t => t.SortOrder);
            _tasks.Move(idx, newIdx);
            // Re-number SortOrder
            for (int i = 0; i < _tasks.Count; i++) _tasks[i].SortOrder = i;
            var changed = _tasks.Where(t => oldOrder[t] != t.SortOrder).ToList();
            // L-4: async — same full-list-rewrite stall as drag-reorder.
            if (changed.Count > 0) DataStore.SaveTasksAsync(changed);
        }

        private T GetListItemAtMouse<T>(ListBox lb, System.Windows.Point pos) where T : class
        {
            var hit = VisualTreeHelper.HitTest(lb, pos);
            var dep = hit?.VisualHit as DependencyObject;
            while (dep != null)
            {
                if (dep is ListBoxItem lbi && lbi.DataContext is T t) return t;
                dep = VisualTreeHelper.GetParent(dep);
            }
            return null;
        }

        private void RenameClip(ClipboardItem clip)
        {
            if (!PromptInput("Rename Clip", "Enter a title for this clip (leave empty to clear):", clip.Title, out string result))
                return;   // cancelled
            clip.Title = result.Trim();
            DataStore.SaveClip(clip);
            ApplyFilter(); // rebind list so Title/visibility refresh
        }

        /// <summary>Simple text-input prompt dialog. Returns false if cancelled, true with value if OK.</summary>
        private bool PromptInput(string title, string prompt, string initial, out string value)
        {
            var dlg = new Window
            {
                Title = title,
                Width = 380,
                SizeToContent = SizeToContent.Height,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = this,
                ResizeMode = ResizeMode.NoResize,
                Background = App.GetBrush("BrushBg"),
                ShowInTaskbar = false,
            };
            var stack = new StackPanel { Margin = new Thickness(16) };
            stack.Children.Add(new TextBlock
            {
                Text = prompt,
                FontFamily = new System.Windows.Media.FontFamily("Segoe UI"),
                Foreground = App.GetBrush("BrushText"),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 8),
            });
            var txt = new TextBox
            {
                Text = initial,
                MinHeight = 44,
                VerticalContentAlignment = VerticalAlignment.Center,
                FontFamily = new System.Windows.Media.FontFamily("Segoe UI"),
                Margin = new Thickness(0, 0, 0, 12),
            };
            stack.Children.Add(txt);
            var btnRow = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 0, 0, 4) };
            var btnOk  = new Button { Content = "OK",     MinWidth = 80, MinHeight = 44, Margin = new Thickness(0, 0, 8, 0), IsDefault = true };
            var btnCan = new Button { Content = "Cancel", MinWidth = 80, MinHeight = 44, IsCancel = true };
            btnRow.Children.Add(btnOk);
            btnRow.Children.Add(btnCan);
            stack.Children.Add(btnRow);
            dlg.Content = stack;
            bool ok = false;
            btnOk.Click  += (_, __) => { ok = true;  dlg.Close(); };
            btnCan.Click += (_, __) => { ok = false; dlg.Close(); };
            txt.SelectAll();
            txt.Focus();
            dlg.ShowDialog();
            value = ok ? txt.Text : "";
            return ok;
        }

        private void PinClip(ClipboardItem clip)
        {
            clip.IsPinned = !clip.IsPinned;
            int from = _clips.IndexOf(clip);
            int to   = clip.IsPinned
                ? _clips.Count(c => c.IsPinned) - 1
                : _clips.Count(c => c.IsPinned);
            if (from >= 0 && from != to) _clips.Move(from, to);
            UpdatePresets();
            DataStore.SaveClip(clip);
        }

        private void PinNote(StickyNote note)
        {
            note.IsPinned = !note.IsPinned;
            DataStore.SaveNote(note);
            ApplyFilter();
        }

        private void BuildFolderSubmenu(MenuItem parent, string tab, Action<int?> assign)
        {
            void AddFolderToMenu(MenuItem p, IEnumerable<FolderNode> nodes)
            {
                foreach (var node in nodes)
                {
                    if (node.NodeType != FolderNodeType.UserFolder) continue;
                    int captured = node.FolderId!.Value;
                    var mi = new MenuItem { Header = node.Name };
                    mi.Click += (_, __) => assign(captured);
                    p.Items.Add(mi);
                    if (node.Children.Count > 0) AddFolderToMenu(mi, node.Children);
                }
            }
            var savedRoot = GetTreeNodes(tab).FirstOrDefault(n => n.NodeType == FolderNodeType.SavedRoot);
            if (savedRoot != null) AddFolderToMenu(parent, savedRoot.Children);
            if (parent.Items.Count == 0)
                parent.Items.Add(new MenuItem { Header = "(no folders — create one first)", IsEnabled = false });
        }

        private void ShowItemFolderMenu(object item, string tab, Action<int?> assign, FrameworkElement target, System.Windows.Point pos)
        {
            var menu = new ContextMenu();

            var removeItem = new MenuItem { Header = "Remove from folder" };
            removeItem.Click += (_, __) => assign(null);
            menu.Items.Add(removeItem);
            menu.Items.Add(new Separator());

            var moveItem = new MenuItem { Header = "Move to folder" };
            menu.Items.Add(moveItem);

            void AddFolderToMenu(MenuItem parent, IEnumerable<FolderNode> nodes)
            {
                foreach (var node in nodes)
                {
                    if (node.NodeType != FolderNodeType.UserFolder) continue;
                    int captured = node.FolderId!.Value;
                    var mi = new MenuItem { Header = node.Name };
                    mi.Click += (_, __) => assign(captured);
                    parent.Items.Add(mi);
                    if (node.Children.Count > 0)
                        AddFolderToMenu(mi, node.Children);
                }
            }

            var savedRoot = GetTreeNodes(tab).FirstOrDefault(n => n.NodeType == FolderNodeType.SavedRoot);
            if (savedRoot != null) AddFolderToMenu(moveItem, savedRoot.Children);

            if (moveItem.Items.Count == 0)
            {
                var noFolders = new MenuItem { Header = "(no folders — create one first)", IsEnabled = false };
                moveItem.Items.Add(noFolders);
            }

            menu.Items.Add(new Separator());
            var newFolder = new MenuItem { Header = "New Folder…" };
            newFolder.Click += (_, __) => FolderCreate(tab, null);
            menu.Items.Add(newFolder);

            target.ContextMenu = menu;
            menu.IsOpen = true;
        }

        // ══════════════════════════════════════════════════════════════════════
        //  Drag-drop: list items → folder tree
        // ══════════════════════════════════════════════════════════════════════
        private const string DragFmtClip   = "NoteStation.ClipId";
        private const string DragFmtNote   = "NoteStation.NoteId";
        private const string DragFmtTask   = "NoteStation.TaskId";
        private const string DragFmtFolder = "NoteStation.FolderId";

        /// <summary>Drag a folder node onto another folder to reparent it (moves it and all its contents/subfolders).</summary>
        private void FolderTree_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton != MouseButtonState.Pressed || _draggedFolderNode == null) return;
            if (!ExceedsDragThreshold(e.GetPosition(null))) return;
            if (sender is not TreeView tree) return;

            var node = _draggedFolderNode;
            _draggedFolderNode = null;   // clear before DoDragDrop so it doesn't re-fire
            DragDrop.DoDragDrop(tree, new DataObject(DragFmtFolder, node.FolderId!.Value), DragDropEffects.Move);
        }

        private void ClipList_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _dragStart = e.GetPosition(null);
            _folderDragItem  = GetListItemAtMouse<ClipboardItem>(ListClipboard, e.GetPosition(ListClipboard));
        }

        private void ClipList_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton != MouseButtonState.Pressed || _folderDragItem is not ClipboardItem clip) return;
            if (!ExceedsDragThreshold(e.GetPosition(null))) return;
            _folderDragItem = null;   // clear before DoDragDrop so Window_MouseMove won't re-fire
            DragDrop.DoDragDrop(ListClipboard, new DataObject(DragFmtClip, clip.Id), DragDropEffects.Move);
        }

        private void NoteList_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _dragStart = e.GetPosition(null);
            _folderDragItem = GetListItemAtMouse<StickyNote>(ListNotes, e.GetPosition(ListNotes));
        }

        private void NoteList_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton != MouseButtonState.Pressed || _folderDragItem is not StickyNote note) return;
            if (!ExceedsDragThreshold(e.GetPosition(null))) return;
            _folderDragItem = null;
            DragDrop.DoDragDrop(ListNotes, new DataObject(DragFmtNote, note.Id), DragDropEffects.Move);
        }

        /// <summary>Window-level: threshold guard for task card drag AND folder-list drags.</summary>
        private void Window_MouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton != MouseButtonState.Pressed) return;
            if (!ExceedsDragThreshold(e.GetPosition(null))) return;

            // Task card drag
            if (_dragItem != null && _dragCard != null)
            {
                var taskItem = _dragItem;
                var card     = _dragCard;
                _dragItem = null;
                _dragCard = null;
                var data = new DataObject();
                data.SetData(DragFmtTask, taskItem.Id);
                data.SetData(typeof(TaskItem), taskItem);
                DragDrop.DoDragDrop(card, data, DragDropEffects.Move);
                return;
            }

            // Folder-list drag (clip / note)
            if (_folderDragItem == null) return;
            var fi = _folderDragItem;
            _folderDragItem = null;
            if (fi is ClipboardItem clip)
                DragDrop.DoDragDrop(ListClipboard, new DataObject(DragFmtClip, clip.Id), DragDropEffects.Move);
            else if (fi is StickyNote note)
                DragDrop.DoDragDrop(ListNotes, new DataObject(DragFmtNote, note.Id), DragDropEffects.Move);
        }

        private bool ExceedsDragThreshold(System.Windows.Point pos) =>
            Math.Abs(pos.X - _dragStart.X) > SystemParameters.MinimumHorizontalDragDistance ||
            Math.Abs(pos.Y - _dragStart.Y) > SystemParameters.MinimumVerticalDragDistance;

        private void FolderTree_DragOver(object sender, DragEventArgs e)
        {
            var tree = sender as TreeView;
            var node = GetTreeNodeAtDrop(tree, e);
            string tab = tree == ClipFolderTree ? "Clipboard" :
                         tree == NoteFolderTree  ? "Notes"     : "Tasks";

            if (e.Data.GetDataPresent(DragFmtFolder))
            {
                int draggedId = (int)e.Data.GetData(DragFmtFolder);
                bool validTarget =
                    (node?.NodeType == FolderNodeType.UserFolder || node?.NodeType == FolderNodeType.SavedRoot) &&
                    IsValidFolderMoveTarget(tab, draggedId, node.FolderId);
                e.Effects = validTarget ? DragDropEffects.Move : DragDropEffects.None;
                e.Handled = true;
                return;
            }

            bool hasPayload = e.Data.GetDataPresent(DragFmtClip) ||
                              e.Data.GetDataPresent(DragFmtNote) ||
                              e.Data.GetDataPresent(DragFmtTask);
            e.Effects = (node?.NodeType == FolderNodeType.UserFolder && hasPayload)
                ? DragDropEffects.Move
                : DragDropEffects.None;
            e.Handled = true;
        }

        /// <summary>True if dropping folder <paramref name="draggedId"/> under <paramref name="targetParentId"/>
        /// is safe — rejects dropping onto itself or onto one of its own descendants (would create a cycle).</summary>
        private bool IsValidFolderMoveTarget(string tab, int draggedId, int? targetParentId)
        {
            if (targetParentId == draggedId) return false;
            if (targetParentId == null) return true; // dropping on the "Folders" root = top-level

            var draggedNode = FlattenNodes(GetTreeNodes(tab)).FirstOrDefault(n => n.FolderId == draggedId);
            if (draggedNode == null) return true;
            var descendants = new HashSet<int>();
            CollectFolderIds(draggedNode, descendants);
            return !descendants.Contains(targetParentId.Value);
        }

        private void FolderTree_Drop(object sender, DragEventArgs e)
        {
            var tree = sender as TreeView;
            var node = GetTreeNodeAtDrop(tree, e);
            string tab = tree == ClipFolderTree ? "Clipboard" :
                         tree == NoteFolderTree  ? "Notes"     : "Tasks";

            // Folder → folder: reparent (moves the folder and everything inside it).
            if (e.Data.GetDataPresent(DragFmtFolder))
            {
                if (node?.NodeType != FolderNodeType.UserFolder && node?.NodeType != FolderNodeType.SavedRoot) return;

                int draggedId = (int)e.Data.GetData(DragFmtFolder);
                int? newParentId = node.NodeType == FolderNodeType.SavedRoot ? (int?)null : node.FolderId!.Value;
                if (!IsValidFolderMoveTarget(tab, draggedId, newParentId)) { e.Handled = true; return; }

                var folder = GetTabFolders(tab).FirstOrDefault(f => f.Id == draggedId);
                if (folder != null && folder.ParentId != newParentId)
                {
                    folder.ParentId = newParentId;
                    AppPrefs.Instance.DeferredSave();
                    BuildFolderTree(tab);
                }
                e.Handled = true;
                return;
            }

            if (node?.NodeType != FolderNodeType.UserFolder) return;

            int folderId = node.FolderId!.Value;

            if (e.Data.GetDataPresent(DragFmtClip) && tab == "Clipboard")
            {
                int id = (int)e.Data.GetData(DragFmtClip);
                var clip = _clips.FirstOrDefault(c => c.Id == id);
                if (clip != null) { clip.FolderId = folderId; DataStore.SaveClip(clip); }
            }
            else if (e.Data.GetDataPresent(DragFmtNote) && tab == "Notes")
            {
                int id = (int)e.Data.GetData(DragFmtNote);
                var note = _notes.FirstOrDefault(n => n.Id == id);
                if (note != null) { note.FolderId = folderId; DataStore.SaveNote(note); }
            }
            else if (e.Data.GetDataPresent(DragFmtTask) && tab == "Tasks")
            {
                int id = (int)e.Data.GetData(DragFmtTask);
                var task = _tasks.FirstOrDefault(t => t.Id == id);
                if (task != null) { task.FolderId = folderId; DataStore.SaveTask(task); }
            }

            UpdateFolderCounts(tab);
            ApplyFilter();
            e.Handled = true;
        }

        private FolderNode GetTreeNodeAtDrop(TreeView tree, DragEventArgs e)
        {
            if (tree == null) return null;
            var pos = e.GetPosition(tree);
            var hit = VisualTreeHelper.HitTest(tree, pos);
            var dep = hit?.VisualHit as DependencyObject;
            while (dep != null)
            {
                if (dep is TreeViewItem tvi && tvi.DataContext is FolderNode fn) return fn;
                dep = VisualTreeHelper.GetParent(dep);
            }
            return null;
        }

        // ══════════════════════════════════════════════════════════════════════
        //  Feature 1: Clip Expiry Auto-Prune
        // ══════════════════════════════════════════════════════════════════════
        private void PruneExpiredClips()
        {
            int days = AppPrefs.Instance.ClipExpiryDays;
            if (days <= 0) return;
            var cutoff = DateTime.Now.AddDays(-days);
            var expired = _clips.Where(c => !c.IsPinned && !c.FolderId.HasValue && c.CapturedAt < cutoff).ToList();
            if (expired.Count == 0) return;
            foreach (var c in expired)
            {
                if (c.IsImage && !string.IsNullOrEmpty(c.ImagePath) && File.Exists(c.ImagePath))
                    try { File.Delete(c.ImagePath); } catch { }
                _clips.Remove(c);
            }
            DataStore.SaveClipboard(_clips);
            _clipOverlay?.Refresh(_clips, _templates);
            ApplyFilter();
        }

        // ══════════════════════════════════════════════════════════════════════
        //  Feature 2: Note Reminders
        // ══════════════════════════════════════════════════════════════════════
        private void StartReminderTimer()
        {
            _reminderTimer = new System.Windows.Threading.DispatcherTimer
                { Interval = TimeSpan.FromMinutes(1) };
            _reminderTimer.Tick += CheckNoteReminders;
            _reminderTimer.Start();
        }

        private void CheckNoteReminders(object sender, EventArgs e)
        {
            var now = DateTime.Now;
            foreach (var note in _notes)
            {
                if (note.ReminderAt.HasValue && !note.ReminderFired && note.ReminderAt.Value <= now)
                {
                    note.ReminderFired = true;
                    _trayIcon?.ShowBalloonTip(4000,
                        "NoteStation Reminder",
                        string.IsNullOrWhiteSpace(note.Title) ? "Note reminder" : note.Title,
                        System.Windows.Forms.ToolTipIcon.Info);
                }
            }
        }

        // ══════════════════════════════════════════════════════════════════════
        //  F1: Window-Attach monitor
        //  (Win32 imports provided by NativeMethods.GetForegroundWindow / GetWindowText)
        // ══════════════════════════════════════════════════════════════════════
        private void StartAttachMonitor()
        {
            _attachMonitorTimer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(600)
            };
            _attachMonitorTimer.Tick += AttachMonitor_Tick;
            _attachMonitorTimer.Start();
        }

        private void AttachMonitor_Tick(object sender, EventArgs e)
        {
            // Skip if no note has an attachment pattern
            if (!_notes.Any(n => !string.IsNullOrEmpty(n.AttachedWindowTitle))) return;

            var hWnd = NativeMethods.GetForegroundWindow();
            var sb = new System.Text.StringBuilder(512);
            NativeMethods.GetWindowText(hWnd, sb, sb.Capacity);
            string foreTitle = sb.ToString();

            foreach (var note in _notes.Where(n => !string.IsNullOrEmpty(n.AttachedWindowTitle)))
            {
                if (!_noteWindows.TryGetValue(note.Id, out var win)) continue;
                bool matches = MatchesWildcard(foreTitle, note.AttachedWindowTitle);
                if (matches && note.IsAttachHidden)
                {
                    note.IsAttachHidden = false;
                    win.Show();
                }
                else if (!matches && !note.IsAttachHidden && win.IsVisible)
                {
                    note.IsAttachHidden = true;
                    win.Hide();
                }
            }
        }

        private static bool MatchesWildcard(string input, string pattern)
        {
            // Simple * wildcard matching — case-insensitive
            if (string.IsNullOrEmpty(pattern)) return true;
            string p = pattern.ToLowerInvariant();
            string s = input?.ToLowerInvariant() ?? "";
            if (!p.Contains('*')) return s.Contains(p);
            var parts = p.Split('*', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) return true;
            int pos = 0;
            foreach (var part in parts)
            {
                int idx = s.IndexOf(part, pos, StringComparison.Ordinal);
                if (idx < 0) return false;
                pos = idx + part.Length;
            }
            return true;
        }

        private void BtnAttachNoteToWindow_Click(object sender, RoutedEventArgs e)
        {
            var note = ListNotes.SelectedItem as StickyNote;
            if (note == null) return;
            var dlg = new Windows.WindowAttachDialog(note.AttachedWindowTitle) { Owner = this };
            dlg.ShowDialog();
            if (!dlg.Confirmed) return;
            note.AttachedWindowTitle = dlg.SelectedPattern ?? "";
            DataStore.SaveNote(note);
        }

        // ══════════════════════════════════════════════════════════════════════
        //  F16: Calendar view
        // ══════════════════════════════════════════════════════════════════════
        private void BtnCalendarView_Checked(object sender, RoutedEventArgs e)
        {
            if (TaskList     != null) TaskList.Visibility     = Visibility.Collapsed;
            if (MatrixPanel  != null) MatrixPanel.Visibility  = Visibility.Collapsed;
            if (CalendarPanel!= null) CalendarPanel.Visibility= Visibility.Visible;
            BuildCalendar();
        }

        private void BtnCalendarView_Unchecked(object sender, RoutedEventArgs e)
        {
            if (CalendarPanel != null) CalendarPanel.Visibility = Visibility.Collapsed;
            if (TaskList      != null) TaskList.Visibility      = Visibility.Visible;
        }

        private void BtnCalPrev_Click(object sender, RoutedEventArgs e)
        {
            _calendarVm.GoToPreviousMonth();
            BuildCalendar();
        }

        private void BtnCalNext_Click(object sender, RoutedEventArgs e)
        {
            _calendarVm.GoToNextMonth();
            BuildCalendar();
        }

        private void BuildCalendar()
        {
            if (CalendarGrid == null) return;
            CalendarGrid.Children.Clear();
            TxtCalMonth.Text = _calendarVm.CurrentMonth.ToString("MMMM yyyy");

            // Month layout now owned by CalendarViewModel (2026-09-07 review MVVM
            // extraction, first slice) — the grid-cell visuals below stay here
            // pending build-verified access to convert them to XAML bindings.
            int startOffset = _calendarVm.StartOffset;

            var today = DateTime.Today;
            int daysInMonth = _calendarVm.DaysInMonth;

            for (int cell = 0; cell < 42; cell++)
            {
                int dayNum = cell - startOffset + 1;
                var cellBorder = new Border
                {
                    BorderBrush = new System.Windows.Media.SolidColorBrush(
                        System.Windows.Media.Color.FromArgb(0x40, 0x88, 0x88, 0x88)),
                    BorderThickness = new Thickness(0.5),
                    MinHeight = 60,
                };
                var sp = new StackPanel { Margin = new Thickness(3) };

                if (dayNum >= 1 && dayNum <= daysInMonth)
                {
                    var date = _calendarVm.DateForDay(dayNum);
                    bool isToday = date.Date == today;

                    var dayLbl = new TextBlock
                    {
                        Text = dayNum.ToString(),
                        FontSize = OldGamers.Wpf.Shared.Controls.TypeRamp.Caption, FontWeight = isToday ? FontWeights.Bold : FontWeights.Normal,
                        Foreground = isToday
                            ? App.GetBrush("BrushBlueText")
                            : App.GetBrush("BrushText"),
                    };
                    sp.Children.Add(dayLbl);

                    // Task dots
                    var dayTasks = CalendarViewModel.GetTasksForDate(_tasks, date);
                    foreach (var task in dayTasks.Take(3))
                    {
                        var tb = new TextBlock
                        {
                            // Overdue: "⚠" glyph + RedText (not colour-only, WCAG 1.4.1)
                            Text = (task.DueDate?.Date < today ? "⚠ " : "• ") + task.Title,
                            FontSize = OldGamers.Wpf.Shared.Controls.TypeRamp.Caption,
                            TextTrimming = TextTrimming.CharacterEllipsis,
                            Foreground = task.DueDate?.Date < today
                                ? App.GetBrush("BrushRedText")
                                : App.GetBrush("BrushText"),
                            Cursor = Cursors.Hand,
                            Tag = task,
                        };
                        tb.MouseLeftButtonUp += (s, _) =>
                        {
                            if ((s as TextBlock)?.Tag is TaskItem t)
                            {
                                // Switch to list view and open task detail
                                if (BtnCalendarView != null) BtnCalendarView.IsChecked = false;
                                if (TaskList != null)
                                {
                                    TaskList.Visibility = Visibility.Visible;
                                    TaskList.SelectedItem = t;
                                    TaskList.ScrollIntoView(t);
                                }
                                if (CalendarPanel != null) CalendarPanel.Visibility = Visibility.Collapsed;
                            }
                        };
                        sp.Children.Add(tb);
                    }
                    if (dayTasks.Count > 3)
                        sp.Children.Add(new TextBlock { Text = $"+{dayTasks.Count - 3} more", FontSize = OldGamers.Wpf.Shared.Controls.TypeRamp.Caption,
                            Foreground = App.GetBrush("BrushMuted") });

                    cellBorder.Background = isToday
                        ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(0x20, 0x89, 0xB4, 0xFA))
                        : System.Windows.Media.Brushes.Transparent;
                }
                else
                {
                    cellBorder.Background = new System.Windows.Media.SolidColorBrush(
                        System.Windows.Media.Color.FromArgb(0x10, 0x00, 0x00, 0x00));
                }

                cellBorder.Child = sp;
                CalendarGrid.Children.Add(cellBorder);
            }
        }

        // ══════════════════════════════════════════════════════════════════════
        //  F18: Focus mode
        // ══════════════════════════════════════════════════════════════════════
        private void BtnFocusMode_Click(object sender, RoutedEventArgs e)
        {
            int pomodoroMin = AppPrefs.Instance.PomodoroMinutes > 0 ? AppPrefs.Instance.PomodoroMinutes : 25;
            var dlg = new Windows.FocusModeWindow(_tasks, pomodoroMin);
            dlg.TaskCompleted += task =>
            {
                // CompleteTask already branches internally on task.IsHabit (streak vs.
                // recurrence/save), so it's called unconditionally here — cleaned up
                // 2026-09-07: this used to wrap the call in a dead double `if
                // (!task.IsHabit)` (no braces on the outer one, so it never actually
                // gated anything) left over from an incomplete edit.
                Dispatcher.Invoke(() => CompleteTask(task, true));
            };
            dlg.Owner = this;
            dlg.Show();
        }

        // ── Recently Deleted — restore / permanent-delete / empty trash ───────

        private void RestoreDeletedClip_Click(object sender, RoutedEventArgs e)
        {
            var selected = ListClipboard.SelectedItems.Cast<ClipboardItem>().ToList();
            foreach (var c in selected) RestoreClip(c);
            BuildFolderTree("Clipboard");
            ApplyFilter();
        }

        private void PermanentDeleteClip_Click(object sender, RoutedEventArgs e)
        {
            var selected = ListClipboard.SelectedItems.Cast<ClipboardItem>().ToList();
            if (selected.Count == 0) return;
            var confirm = MessageBox.Show(
                $"Permanently delete {selected.Count} clip{(selected.Count == 1 ? "" : "s")}? This cannot be undone.",
                "NoteStation", MessageBoxButton.OKCancel, MessageBoxImage.Warning);
            if (confirm != MessageBoxResult.OK) return;
            foreach (var c in selected) PermanentDeleteClip(c);
            BuildFolderTree("Clipboard");
            ApplyFilter();
        }

        private void EmptyClipTrash_Click(object sender, RoutedEventArgs e)
        {
            if (_deletedClips.Count == 0) return;
            var confirm = MessageBox.Show(
                $"Permanently delete all {_deletedClips.Count} clip{(_deletedClips.Count == 1 ? "" : "s")} in Recently Deleted?",
                "NoteStation", MessageBoxButton.OKCancel, MessageBoxImage.Warning);
            if (confirm != MessageBoxResult.OK) return;
            foreach (var c in _deletedClips.ToList()) PermanentDeleteClip(c);
            BuildFolderTree("Clipboard");
            ApplyFilter();
        }

        private void RestoreDeletedNote_Click(object sender, RoutedEventArgs e)
        {
            var selected = ListNotes.SelectedItems.Cast<StickyNote>().ToList();
            foreach (var n in selected) RestoreNote(n);
            BuildFolderTree("Notes");
            ApplyFilter();
        }

        private void PermanentDeleteNote_Click(object sender, RoutedEventArgs e)
        {
            var selected = ListNotes.SelectedItems.Cast<StickyNote>().ToList();
            if (selected.Count == 0) return;
            var confirm = MessageBox.Show(
                $"Permanently delete {selected.Count} note{(selected.Count == 1 ? "" : "s")}? This cannot be undone.",
                "NoteStation", MessageBoxButton.OKCancel, MessageBoxImage.Warning);
            if (confirm != MessageBoxResult.OK) return;
            foreach (var n in selected) PermanentDeleteNote(n);
            BuildFolderTree("Notes");
            ApplyFilter();
        }

        private void EmptyNoteTrash_Click(object sender, RoutedEventArgs e)
        {
            if (_deletedNotes.Count == 0) return;
            var confirm = MessageBox.Show(
                $"Permanently delete all {_deletedNotes.Count} note{(_deletedNotes.Count == 1 ? "" : "s")} in Recently Deleted?",
                "NoteStation", MessageBoxButton.OKCancel, MessageBoxImage.Warning);
            if (confirm != MessageBoxResult.OK) return;
            foreach (var n in _deletedNotes.ToList()) PermanentDeleteNote(n);
            BuildFolderTree("Notes");
            ApplyFilter();
        }

        private void RestoreDeletedTask_Click(object sender, RoutedEventArgs e)
        {
            var selected = TaskList.SelectedItems.Cast<TaskItem>().ToList();
            foreach (var t in selected) RestoreTask(t);
            BuildFolderTree("Tasks");
            ApplyFilter();
        }

        private void PermanentDeleteTask_Click(object sender, RoutedEventArgs e)
        {
            var selected = TaskList.SelectedItems.Cast<TaskItem>().ToList();
            if (selected.Count == 0) return;
            var confirm = MessageBox.Show(
                $"Permanently delete {selected.Count} task{(selected.Count == 1 ? "" : "s")}? This cannot be undone.",
                "NoteStation", MessageBoxButton.OKCancel, MessageBoxImage.Warning);
            if (confirm != MessageBoxResult.OK) return;
            foreach (var t in selected) PermanentDeleteTask(t);
            BuildFolderTree("Tasks");
            ApplyFilter();
        }

        private void EmptyTaskTrash_Click(object sender, RoutedEventArgs e)
        {
            if (_deletedTasks.Count == 0) return;
            var confirm = MessageBox.Show(
                $"Permanently delete all {_deletedTasks.Count} task{(_deletedTasks.Count == 1 ? "" : "s")} in Recently Deleted?",
                "NoteStation", MessageBoxButton.OKCancel, MessageBoxImage.Warning);
            if (confirm != MessageBoxResult.OK) return;
            foreach (var t in _deletedTasks.ToList()) PermanentDeleteTask(t);
            BuildFolderTree("Tasks");
            ApplyFilter();
        }
    }
}
