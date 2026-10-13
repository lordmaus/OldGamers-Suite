using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using Microsoft.Extensions.Logging;

#nullable enable


namespace FileCopy
{
    public partial class MainWindow : Window
    {
        private static readonly ILogger _log = AppLogger.ForContext<MainWindow>();
        private SchedulerEngine _scheduler;
        private CopySession _activeSession;
        private CancellationTokenSource _scanCts; // cancels active scan independently of copy

        public MainWindowViewModel Vm { get; } = new MainWindowViewModel();
        private readonly ICopyEngine        _engine;
        private          CopyOrchestrator   _orchestrator;
        private int _previousErrorCount;
        private TrayIcon _tray;
        private string _activeFilter = "All";
        private ICollectionView _itemsView;

        // ── Speed sparkline ───────────────────────────────────────────────────
        private readonly long[] _speedHistory = new long[60];
        private int _sparkHead;

        // Allow injection for tests; defaults to real engine in production.
        public MainWindow() : this(DefaultCopyEngine.Instance) { }
        public MainWindow(ICopyEngine engine) { _engine = engine; Init(); }

        // Convenience aliases — keep diffs minimal
        private ObservableRangeCollection<FileItem> _items  => Vm.Items;
        private ObservableCollection<FileItem>      _errors => Vm.Errors;
        // IsRunning forwards through orchestrator to Vm.IsRunning
        internal bool IsRunning => _orchestrator?.IsRunning ?? Vm.IsRunning;

        private static readonly (string Name, double Scale)[] _scales =
        {
            ("Compact",    0.75),
            ("Standard",   1.0),
            ("Medium",     1.15),
            ("Large",      1.30),
            ("Xtra Large", 1.50)
        };

        public ICommand ScanCommand       { get; private set; }
        public ICommand StartCommand      { get; private set; }
        public ICommand CancelCommand     { get; private set; }
        public ICommand PauseCommand      { get; private set; }
        public ICommand DryRunCommand     { get; private set; }
        public ICommand VerifyCommand     { get; private set; }
        public ICommand LoadPresetCommand { get; private set; }
        public ICommand SavePresetCommand { get; private set; }
        public ICommand ShortcutsCommand  { get; private set; }
        public ICommand SchedulerCommand  { get; private set; }

        private void Init()
        {
            ScanCommand       = new RelayCommand(_  => SafeFireAsync(ScanAsync()),              _ => !IsRunning);
            StartCommand      = new RelayCommand(_  => SafeFireAsync(RunCopyAsync()),           _ => !IsRunning && _items.Count > 0);
            CancelCommand     = new RelayCommand(_  => CancelCopy(),                            _ => IsRunning || _scanCts != null);
            PauseCommand      = new RelayCommand(_  => TogglePause(),                           _ => IsRunning);
            DryRunCommand     = new RelayCommand(_  => DryRun(),                                _ => !IsRunning);
            VerifyCommand     = new RelayCommand(_  => SafeFireAsync(VerifyOnlyAsync()),        _ => !IsRunning && _items.Count > 0);
            LoadPresetCommand = new RelayCommand(p  => { if (TryParseSlot(p, out int n)) LoadPresetFromSlot(n); }, _ => !IsRunning);
            SavePresetCommand = new RelayCommand(p  => { if (TryParseSlot(p, out int n)) SavePresetToSlot(n);    }, _ => !IsRunning);
            ShortcutsCommand  = new RelayCommand(_  => ShowShortcuts());
            SchedulerCommand  = new RelayCommand(_  => BtnSchedule_Click(null, null));

            _orchestrator = new CopyOrchestrator(_engine, Vm, Dispatcher);
            _orchestrator.ProgressChanged += (_, pr)  => OnProgress(pr);
            _orchestrator.Completed       += args => OnCopyCompleted(args.CleanFinish);

            DataContext = Vm;
            InitializeComponent();
            if (PortableMode.Active) Title = "File Copy Studio  [Portable]";
            SourceInitialized += (_, __) =>
            {
                bool dark = AppPrefs.Instance.Theme switch
                {
                    "Light" => false,
                    "Dark"  => true,
                    _       => App.SystemIsDark()
                };
                DwmHelpers.EnableDarkChrome(this, dark);
            };
            GridErrors.ItemsSource = _errors;
            // Create the filtered view before assigning to the grid
            _itemsView = CollectionViewSource.GetDefaultView(_items);
            _itemsView.Filter = FilterItem;
            GridFiles.ItemsSource = _itemsView;
            _items.CollectionChanged += Items_CollectionChanged;
            InitSourcePicker();
            ApplyUiScale(AppPrefs.Instance.UiScale);
            // Theme ComboBox is bound to Vm.SelectedTheme — no manual label sync needed.
            RestoreWindowGeometry();
            RestoreFileGridColumns();
            RestoreFilterSelection();
            InitTray();
            Dispatcher.InvokeAsync(UpdateConflictPreview,
                System.Windows.Threading.DispatcherPriority.Loaded);
            _scheduler = new SchedulerEngine(Dispatcher);
            _scheduler.JobFired += OnSchedulerJobFired;
            _scheduler.Start();
            CheckMissedJobs();
            Microsoft.Win32.SystemEvents.PowerModeChanged += OnPowerModeChanged;
            ApplyCommandLine();
        }

        private static bool TryParseSlot(object param, out int slot)
        {
            slot = 0;
            // ItemsControl Tag is int (Slot int property); KeyBinding param is "1".."5" string.
            if (param is int i)    { slot = i; return slot >= 1 && slot <= 5; }
            if (param is string s) { return int.TryParse(s, out slot) && slot >= 1 && slot <= 5; }
            return false;
        }

        private void InitTray()
        {
            try
            {
                System.Drawing.Icon? icon = null;
                string iconPath = System.IO.Path.Combine(AppContext.BaseDirectory, "app.ico");
                if (File.Exists(iconPath))
                {
                    using var fs = File.OpenRead(iconPath);
                    icon = new System.Drawing.Icon(fs);
                }
                _tray = new TrayIcon(this, icon);
            }
            catch (Exception ex) { _log.LogWarning(ex, "TrayIcon init failed"); }
        }

        private void CancelCopy()
        {
            _scanCts?.Cancel();
            _orchestrator.Cancel();
        }

        /// <summary>
        /// Fires a Task from a synchronous command handler.
        /// Catches and logs any unhandled exception so it surfaces in the status bar
        /// rather than being silently discarded.
        /// </summary>
        private async void SafeFireAsync(Task t)
        {
            try   { await t; }
            catch (OperationCanceledException) { /* expected on cancel */ }
            catch (Exception ex)
            {
                _log.LogError(ex, "Unhandled exception from command");
                Vm.StatusText = "Error: " + ex.Message;
            }
        }
        private void TogglePause() => _orchestrator.TogglePause();

        private void DryRun()
        {
            if (_items.Count == 0) _ = ScanAsync();
            else
            {
                ReplanSelected();
                // Compute byte delta for the planned actions
                var toCopy  = _items.Where(x => x.Action is FileAction.Copy or FileAction.Overwrite
                                              or FileAction.Rename or FileAction.Resume).ToList();
                var toSkip  = _items.Where(x => x.Action == FileAction.Skip).ToList();
                long bytes  = toCopy.Sum(x => Math.Max(0, x.SizeBytes - x.ResumeOffset));
                string size = bytes > 0 ? $"  ({SizeFormat.Format(bytes)})" : "";
                Vm.StatusText = $"Dry run — {toCopy.Count:N0} to copy{size}, {toSkip.Count:N0} skip. See Action column.";
            }
        }

        private void Items_CollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            // Detach from removed
            if (e.OldItems != null)
                foreach (FileItem it in e.OldItems)
                    it.PropertyChanged -= Item_PropertyChanged;

            // Attach to added
            if (e.NewItems != null)
                foreach (FileItem it in e.NewItems)
                    it.PropertyChanged += Item_PropertyChanged;

            // Reset → no OldItems available; we already cleared listeners pre-Reset (see ScanAsync)
            if (e.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Reset)
            {
                _errors.Clear();
                UpdateErrorPanelVisibility();
            }
        }

        private void DetachAllItemListeners()
        {
            foreach (var it in _items)
                it.PropertyChanged -= Item_PropertyChanged;
        }

        private void Item_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (sender is not FileItem it) return;
            if (e.PropertyName != nameof(FileItem.Action) && e.PropertyName != nameof(FileItem.ErrorText)) return;

            if (Dispatcher.CheckAccess()) UpdateErrorListFor(it);
            else Dispatcher.InvokeAsync(() => UpdateErrorListFor(it));
        }

        private void UpdateErrorListFor(FileItem it)
        {
            bool isError = it.Action == FileAction.Failed || !string.IsNullOrEmpty(it.ErrorText);
            bool inList  = _errors.Contains(it);
            if (isError && !inList)       _errors.Add(it);
            else if (!isError && inList)  _errors.Remove(it);
            UpdateErrorPanelVisibility();
        }

        private void UpdateErrorPanelVisibility()
        {
            int n = _errors.Count;
            // Visibility + ErrorCountText come via VM bindings; this method only
            // controls auto-expand on the 0→1 transition (respects manual collapse).
            if (n > 0 && _previousErrorCount == 0) ErrorPanel.IsExpanded = true;
            _previousErrorCount = n;
        }

        private void ErrorPanel_ExpandedChanged(object sender, RoutedEventArgs e)
        {
            // Automation: give SR the new state immediately via AutomationPeer
            if (sender is System.Windows.Controls.Expander exp)
            {
                System.Windows.Automation.Peers.UIElementAutomationPeer
                    .FromElement(exp)?.RaiseAutomationEvent(
                        System.Windows.Automation.Peers.AutomationEvents.MenuOpened);
            }
        }

        private void BtnCopyErrors_Click(object sender, RoutedEventArgs e)
        {
            if (_errors.Count == 0) return;
            var v  = typeof(MainWindow).Assembly.GetName().Version;
            var p  = AppPrefs.Instance;
            string rptSrc      = _activeSession?.SourcePath   ?? p.SourcePath;
            string rptDst      = _activeSession?.DestPath     ?? p.DestPath;
            string rptConflict = (_activeSession?.ConflictMode ?? p.ConflictMode).ToString();
            var sb = new StringBuilder();
            sb.AppendLine($"File Copy Studio {v} — Error Report");
            sb.AppendLine($"Generated : {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine($"Source    : {rptSrc}");
            sb.AppendLine($"Dest      : {rptDst}");
            sb.AppendLine($"Conflict  : {rptConflict}");
            sb.AppendLine($"Errors    : {_errors.Count:N0} of {_items.Count:N0} files");
            if (PortableMode.Active) sb.AppendLine("Mode      : Portable");
            sb.AppendLine(new string('-', 60));
            foreach (var it in _errors)
            {
                sb.AppendLine($"[{it.ErrorTimestampText}] {it.RelativePath}");
                sb.AppendLine($"  Size : {it.SizeText}");
                sb.AppendLine($"  Error: {it.ErrorText}");
                sb.AppendLine();
            }
            CopyToClipboard(sb.ToString());
        }

        private void BtnClearErrors_Click(object sender, RoutedEventArgs e)
        {
            _errors.Clear();
            UpdateErrorPanelVisibility();
        }

        // ── Log viewer ────────────────────────────────────────────────────────
        private System.Windows.Threading.DispatcherTimer _logTimer;

        private void LogPanel_ExpandedChanged(object sender, RoutedEventArgs e)
        {
            if (LogPanel.IsExpanded)
            {
                LoadLogContent();
                _logTimer = new System.Windows.Threading.DispatcherTimer
                    { Interval = TimeSpan.FromSeconds(3) };
                _logTimer.Tick += (_, __) => { if (LogPanel.IsExpanded) LoadLogContent(); };
                _logTimer.Start();
            }
            else
            {
                _logTimer?.Stop();
                _logTimer = null;
            }
        }

        private void BtnRefreshLog_Click(object sender, RoutedEventArgs e) => LoadLogContent();

        private void BtnOpenLogFile_Click(object sender, RoutedEventArgs e)
        {
            string logPath = GetTodayLogPath();
            if (!File.Exists(logPath))
            {
                // Try yesterday if today's log doesn't exist yet
                string yesterday = System.IO.Path.Combine(PortableMode.LogsDir,
                    $"filecopy-studio-{DateTime.Now.AddDays(-1):yyyy-MM-dd}.log");
                logPath = File.Exists(yesterday) ? yesterday : logPath;
            }
            if (!File.Exists(logPath)) { Vm.StatusText = "No log file yet."; return; }
            try
            {
                // UseShellExecute = true opens with the system default .log handler
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(logPath)
                    { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                AppDialog.Show(this, ex.Message, "Cannot open log", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void LoadLogContent()
        {
            string logPath = GetTodayLogPath();

            // If today's file doesn't exist yet, look for yesterday's
            if (!File.Exists(logPath))
            {
                string yesterday = System.IO.Path.Combine(PortableMode.LogsDir,
                    $"filecopy-studio-{DateTime.Now.AddDays(-1):yyyy-MM-dd}.log");
                if (File.Exists(yesterday)) logPath = yesterday;
            }

            try
            {
                if (!File.Exists(logPath))
                {
                    TxtLog.Text = "(No log file yet)";
                    LblLogStatus.Text = "No log file";
                    return;
                }

                // ReadAllLines with ReadWrite share — safe to read while AppLogger is appending.
                // Read into a list, then take last 200.
                List<string> lines;
                using (var fs = new FileStream(logPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (var sr = new StreamReader(fs, System.Text.Encoding.UTF8, detectEncodingFromByteOrderMarks: true))
                {
                    lines = new List<string>();
                    string? line;
                    while ((line = sr.ReadLine()) != null)
                        lines.Add(line);
                }

                int take  = Math.Min(200, lines.Count);
                int start = lines.Count - take;
                TxtLog.Text = string.Join(Environment.NewLine, lines.GetRange(start, take));
                TxtLog.ScrollToEnd();
                LblLogStatus.Text = $"Last {take} of {lines.Count} lines — {System.IO.Path.GetFileName(logPath)}";
            }
            catch (Exception ex)
            {
                TxtLog.Text = $"Error reading log: {ex.Message}";
            }
        }

        private static string GetTodayLogPath()
            => System.IO.Path.Combine(PortableMode.LogsDir, $"filecopy-studio-{DateTime.Now:yyyy-MM-dd}.log");

        private void BtnExportErrorsCsv_Click(object sender, RoutedEventArgs e)
        {
            if (_errors.Count == 0) return;
            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                Title      = "Export errors to CSV",
                Filter     = "CSV files (*.csv)|*.csv|All files (*.*)|*.*",
                DefaultExt = ".csv",
                FileName   = $"FileCopyStudio_Errors_{DateTime.Now:yyyyMMdd_HHmmss}.csv"
            };
            if (dlg.ShowDialog(this) != true) return;

            try
            {
                var v  = typeof(MainWindow).Assembly.GetName().Version;
                var p  = AppPrefs.Instance;
                using var w = new System.IO.StreamWriter(dlg.FileName, false, System.Text.Encoding.UTF8);
                // BOM for Excel compatibility
                w.WriteLine("# File Copy Studio Error Report");
                w.WriteLine($"# Version: {v}");
                w.WriteLine($"# Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                w.WriteLine($"# Source: {CsvEscape(_activeSession?.SourcePath ?? p.SourcePath)}");
                w.WriteLine($"# Dest: {CsvEscape(_activeSession?.DestPath ?? p.DestPath)}");
                w.WriteLine();
                w.WriteLine("Time,File,Size,Error");
                foreach (var it in _errors)
                    w.WriteLine($"{CsvEscape(it.ErrorTimestampText)},{CsvEscape(it.RelativePath)},{CsvEscape(it.SizeText)},{CsvEscape(it.ErrorText)}");

                Vm.StatusText = $"Exported {_errors.Count:N0} error(s) to {System.IO.Path.GetFileName(dlg.FileName)}.";
                // If a copy is running, show a dialog since the status bar is being used for progress
                if (IsRunning)
                    AppDialog.Show(this,
                        $"Exported {_errors.Count:N0} error(s) to:\n{dlg.FileName}",
                        "CSV exported", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                AppDialog.Show(this, ex.Message, "Export failed", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private static string CsvEscape(string? s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            // Wrap in quotes if field contains comma, quote, or newline
            if (s.Contains(',') || s.Contains('"') || s.Contains('\n'))
                return $"\"{s.Replace("\"", "\"\"")}\"";
            return s;
        }

        private void ApplyCommandLine()
        {
            string? source = App.PendingSourcePath;
            string? dest   = App.PendingDestPath;
            bool    scan   = App.PendingAutoScan;
            string? job    = App.PendingJobPath;
            App.ClearPending();
            if (!string.IsNullOrEmpty(job)) Dispatcher.InvokeAsync(() => LoadJobFile(job));
            else ApplyArgs(source, dest, scan);
        }

        /// <summary>Called by App when a secondary instance hands off args.</summary>
        public void ApplyExternalArgs(InstanceArgs args)
        {
            if (args == null) return;
            if (!string.IsNullOrEmpty(args.JobPath)) Dispatcher.InvokeAsync(() => LoadJobFile(args.JobPath));
            else ApplyArgs(args.SourcePath, args.DestPath, args.AutoScan);
        }

        private void LoadJobFile(string path)
        {
            try
            {
                var job = FcJob.Load(path);
                if (job == null) throw new InvalidDataException("Empty or unreadable job file.");
                var p = AppPrefs.Instance;
                if (!string.IsNullOrWhiteSpace(job.SourcePath)) ApplySourceParent(job.SourcePath);
                p.DestPath = job.DestPath;
                p.ConflictMode = job.ConflictMode;
                p.FileMaskInclude = job.FileMaskInclude; p.FileMaskExclude = job.FileMaskExclude;
                p.IncludeSubdirs = job.IncludeSubdirs;
                p.PreserveTimestamps = job.PreserveTimestamps; p.PreserveAttributes = job.PreserveAttributes;
                p.VerifyCrc = job.VerifyCrc; p.DeleteSourceOnMove = job.DeleteSourceOnMove;
                p.ResumePartial = job.ResumePartial;
                p.BufferSizeKb = job.BufferSizeKb; p.MaxParallel = job.MaxParallel;
                Vm.StatusText = $"Loaded job ({job.Items?.Count ?? 0} items planned). Click Scan to refresh.";
            }
            catch (Exception ex)
            {
                AppDialog.Show(this, ex.Message, "Load job failed",
                                MessageBoxButton.OK, MessageBoxImage.Error);
                Vm.StatusText = "Load job failed.";
            }
        }

        private void ApplyArgs(string? source, string? dest, bool autoScan)
        {
            bool changed = false;
            if (!string.IsNullOrEmpty(source) && Directory.Exists(source)) { ApplySourceParent(source); changed = true; }
            if (!string.IsNullOrEmpty(dest))   { Vm.Prefs.DestPath   = dest;   changed = true; }
            if (changed && autoScan)
                Dispatcher.InvokeAsync(async () => await ScanAsync());
        }

        // ── Window geometry only — other prefs flow via {Binding Prefs.X} ────
        private void RestoreWindowGeometry()
        {
            var p = AppPrefs.Instance;
            if (!double.IsNaN(p.WindowLeft))   Left   = p.WindowLeft;
            if (!double.IsNaN(p.WindowTop))    Top    = p.WindowTop;
            if (p.WindowWidth  > 0)            Width  = p.WindowWidth;
            if (p.WindowHeight > 0)            Height = p.WindowHeight;
            ClampToVisibleWorkArea();
            if (p.Maximized) WindowState = WindowState.Maximized;
        }

        /// <summary>
        /// If the saved bounds fall outside the primary work area (e.g. monitor
        /// removed since last close), pull the window back into a visible region.
        /// Multi-monitor coverage check is best-effort via SystemParameters; for
        /// strict per-monitor checks we'd need MonitorFromWindow P/Invoke.
        /// </summary>
        private void ClampToVisibleWorkArea()
        {
            try
            {
                var virtualScreen = SystemParameters.VirtualScreenLeft;
                var virtualWidth  = SystemParameters.VirtualScreenWidth;
                var virtualTop    = SystemParameters.VirtualScreenTop;
                var virtualHeight = SystemParameters.VirtualScreenHeight;

                bool offScreen =
                    double.IsNaN(Left) || double.IsNaN(Top) ||
                    Left + Width  < virtualScreen + 40 ||
                    Top  + Height < virtualTop    + 40 ||
                    Left          > virtualScreen + virtualWidth  - 40 ||
                    Top           > virtualTop    + virtualHeight - 40;

                if (offScreen)
                {
                    var primary = SystemParameters.WorkArea;
                    Left = primary.Left + 40;
                    Top  = primary.Top  + 40;
                    Width  = Math.Min(Width,  primary.Width  - 80);
                    Height = Math.Min(Height, primary.Height - 80);
                }
            }
            catch { /* SystemParameters unavailable on weird hosts */ }
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            // Always offer tray option if tray is available — not just when a copy is running
            if (_tray != null)
            {
                string body = IsRunning
                    ? "A copy operation is in progress.\n\n" +
                      "  Yes    → Minimize to tray, continue in background\n" +
                      "  No     → Cancel the copy and exit\n" +
                      "  Cancel → Stay open"
                    : "  Yes    → Minimize to system tray\n" +
                      "  No     → Close the application\n" +
                      "  Cancel → Stay open";

                string title = IsRunning
                    ? "File Copy Studio — copy in progress"
                    : "File Copy Studio";

                var result = AppDialog.Show(this, body, title,
                    MessageBoxButton.YesNoCancel,
                    IsRunning ? MessageBoxImage.Warning : MessageBoxImage.Question,
                    MessageBoxResult.Cancel);

                if (result == MessageBoxResult.Cancel)
                {
                    e.Cancel = true;
                    return;
                }

                if (result == MessageBoxResult.Yes)
                {
                    e.Cancel = true;
                    _tray.MinimizeToTray();
                    return;
                }

                // No = exit (cancel copy first if running)
                if (IsRunning) CancelCopy();
            }

            var p = AppPrefs.Instance;
            p.Maximized = WindowState == WindowState.Maximized;
            if (!p.Maximized) { p.WindowLeft = Left; p.WindowTop = Top; p.WindowWidth = Width; p.WindowHeight = Height; }
            SaveFileGridColumns();
            _logTimer?.Stop();
            _scheduler?.Stop();
            _tray?.Dispose();
            Microsoft.Win32.SystemEvents.PowerModeChanged -= OnPowerModeChanged;
            p.Save();
        }

        // ── Section 1: UI scale ───────────────────────────────────────────────
        private void BtnUiScale_Click(object sender, RoutedEventArgs e)
        {
            int i = Array.FindIndex(_scales, s => s.Name == AppPrefs.Instance.UiScale);
            if (i < 0) i = 1; // default to Standard if saved name is unknown
            i = (i + 1) % _scales.Length;
            ApplyUiScale(_scales[i].Name);
        }

        private void ApplyUiScale(string name)
        {
            var s = _scales.FirstOrDefault(x => x.Name == name);
            if (s.Name == null) s = _scales[1]; // default Standard
            UiScaleTransform.ScaleX = s.Scale;
            UiScaleTransform.ScaleY = s.Scale;
            Vm.UiScaleLabel = $"⊞ {s.Name} ({s.Scale:0.0#}×)";
            AppPrefs.Instance.UiScale = s.Name;

            // Adjust window minimum size so content never clips
            MinWidth  = Math.Ceiling(900  * s.Scale);
            MinHeight = Math.Ceiling(680  * s.Scale);
            if (Width  < MinWidth)  Width  = MinWidth;
            if (Height < MinHeight) Height = MinHeight;
        }

        // ── Section 2: Nav / pickers ──────────────────────────────────────────
        private void BtnPickDest_Click(object sender, RoutedEventArgs e)
            => PickFolder(p => Vm.Prefs.DestPath = p);

        private void BtnSourceHistory_Click(object sender, RoutedEventArgs e)
            => ShowPathHistory(sender as Button, Vm.Prefs.RecentSources, ApplySourceParent);

        private void BtnDestHistory_Click(object sender, RoutedEventArgs e)
            => ShowPathHistory(sender as Button, Vm.Prefs.RecentDests, p => Vm.Prefs.DestPath = p);

        /// <summary>True if all roots share the same immediate parent directory (or there's 0-1 root).
        /// Kept as a defensive check in ValidatePaths — redundant once the single-parent picker model
        /// is in place (checked folders are always direct children of SourceParent by construction),
        /// but cheap insurance against a stale/edited prefs file.</summary>
        internal static bool RootsShareCommonParent(IReadOnlyList<string> roots)
        {
            if (roots.Count <= 1) return true;
            string? parent0 = NormalizedParent(roots[0]);
            for (int i = 1; i < roots.Count; i++)
                if (!string.Equals(NormalizedParent(roots[i]), parent0, StringComparison.OrdinalIgnoreCase))
                    return false;
            return true;
        }

        private static string? NormalizedParent(string path)
        {
            try { return Path.GetDirectoryName(Path.GetFullPath(path).TrimEnd('\\', '/')); }
            catch { return null; }
        }

        /// <summary>Keeps AppPrefs.SourcePath mirrored to the first selected source — Presets,
        /// the Scheduler, and CLI args still operate on a single SourcePath string.</summary>
        private void SyncLegacySourcePath()
            => Vm.Prefs.SourcePath = Vm.Prefs.SourceList.Count > 0 ? Vm.Prefs.SourceList[0] : "";

        private void ShowPathHistory(Button? anchor, System.Collections.Generic.List<string> recent, Action<string> set)
        {
            if (anchor == null || recent == null || recent.Count == 0)
            {
                Vm.StatusText = "No recent folders yet.";
                return;
            }
            var menu = new ContextMenu { PlacementTarget = anchor, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom };
            foreach (var path in recent)
            {
                var item = new MenuItem
                {
                    Header  = path.Length > 80 ? "…" + path.Substring(path.Length - 77) : path,
                    ToolTip = path
                };
                string captured = path;
                item.Click += (_, __) => set(captured);
                menu.Items.Add(item);
            }
            menu.IsOpen = true;
        }

        private void CmbSource_SelectionChanged(object sender, SelectionChangedEventArgs e) { }
        private void CmbDest_SelectionChanged  (object sender, SelectionChangedEventArgs e) { }

        private void PickFolder(Action<string> set)
        {
            using var dlg = new System.Windows.Forms.FolderBrowserDialog
            {
                Description            = "Select folder",
                UseDescriptionForTitle = true,
                ShowNewFolderButton    = true,
                AutoUpgradeEnabled     = true
            };
            var owner = new System.Windows.Forms.NativeWindow();
            owner.AssignHandle(new System.Windows.Interop.WindowInteropHelper(this).Handle);
            var result = dlg.ShowDialog(owner);
            owner.ReleaseHandle();
            if (result == System.Windows.Forms.DialogResult.OK)
                set(dlg.SelectedPath);
        }

        // No longer needed — kept as no-op stubs for drop handlers that reference CmbSource/CmbDest
        private static void ForceComboText(System.Windows.Controls.ComboBox cmb, string text) { }

        private void BtnSwap_Click(object sender, RoutedEventArgs e)
        {
            if (IsRunning) return;
            string a = Vm.Prefs.SourceParent;
            string b = Vm.Prefs.DestPath;
            Vm.Prefs.DestPath = a;
            if (!string.IsNullOrWhiteSpace(b) && Directory.Exists(b)) ApplySourceParent(b);
        }

        private void BtnTheme_Click(object sender, RoutedEventArgs e)
        {
            // Use default WPF menu chrome (Style=null) so this flyout matches the
            // other apps (TimerStudio, BadgeOverlay, ReticleOverlay, SuperMagnifierLV),
            // which don't override MenuItem/ContextMenu styles. The app-wide
            // ContextMenu/MenuItem styles (44px MinHeight) are needed for the
            // FileRow context menu but make the theme picker oversize and force
            // a scrollbar in the parent flyout.
            var ctx = new System.Windows.Controls.ContextMenu
            {
                PlacementTarget = BtnTheme,
                Placement       = System.Windows.Controls.Primitives.PlacementMode.Bottom,
                Style           = null,
            };
            string cur = AppPrefs.Instance.Theme;

            var cats = new (string Cat, (string Key, string Disp)[] Items)[]
            {
                ("Dark",      new[] { ("Dark","Dark"),("OneDark","One Dark"),("GruvboxDark","Gruvbox Dark"),("TokyoNight","Tokyo Night"),("Nord","Nord"),("Dracula","Dracula"),("CatppuccinMocha","Catppuccin Mocha"),("RosePine","Rosé Pine"),("AyuDark","Ayu Dark"),("AyuMirage","Ayu Mirage"),("Cobalt2","Cobalt 2"),("Monokai","Monokai"),("SolarizedDark","Solarized Dark"),("Kanagawa","Kanagawa"),("EverforestDark","Everforest Dark"),("Oxocarbon","Oxocarbon"),("Palenight","Palenight"),("GitHubDark","GitHub Dark"),("DraculaPro","Dracula Pro"),("Horizon","Horizon"),("Abyss","Abyss"),("Obsidian","Obsidian"),("Panda","Panda"),("Monochrome","Monochrome") }),
                ("Light",     new[] { ("Light","Light"),("WarmLight","Warm Light"),("CatppuccinLatte","Catppuccin Latte"),("GitHubLight","GitHub Light"),("AyuLight","Ayu Light"),("SolarizedLight","Solarized Light"),("RosePineDawn","Rose Pine Dawn"),("EverforestLight","Everforest Light"),("PaperWhite","Paper White"),("Sepia","Sepia"),("Arctic","Arctic"),("Ivory","Ivory") }),
                ("Contrast",  new[] { ("HighContrast","High Contrast"),("HighContrastLight","HC Light"),("HCBlue","HC Blue"),("HCGreen","HC Green"),("HCAmber","HC Amber") }),
                ("Neon",      new[] { ("Synthwave","Synthwave"),("Cyberpunk","Cyberpunk"),("NeonCity","Neon City"),("VaporWave","VaporWave"),("Outrun","Outrun"),("HoloBlue","Holo Blue"),("Matrix","Matrix"),("NeonPurple","Neon Purple"),("LaserGrid","Laser Grid"),("ElectricOcean","Electric Ocean") }),
                ("Nature",    new[] { ("Forest","Forest"),("Ocean","Ocean"),("DesertSand","Desert Sand"),("Autumn","Autumn"),("MidnightGarden","Midnight Garden"),("LavenderField","Lavender Field"),("CoralReef","Coral Reef"),("Tundra","Tundra"),("Volcanic","Volcanic"),("Meadow","Meadow") }),
                ("Retro",     new[] { ("C64","C64"),("AmberCRT","Amber CRT"),("GreenCRT","Green CRT"),("IBMBlue","IBM Blue"),("AppleII","Apple II"),("BIOS","BIOS"),("Teletext","Teletext"),("TerminalRed","Terminal Red"),("VGAPurple","VGA Purple"),("ZXSpectrum","ZX Spectrum") }),
                ("Warm",      new[] { ("Ember","Ember"),("Rosewood","Rosewood"),("GoldenHour","Golden Hour"),("Terracotta","Terracotta"),("Rust","Rust"),("Mahogany","Mahogany"),("Cinnamon","Cinnamon"),("Wine","Wine"),("Copper","Copper"),("Sundowner","Sundowner") }),
                ("Cool",      new[] { ("MidnightBlue","Midnight Blue"),("DeepSpace","Deep Space"),("Glacier","Glacier"),("Cobalt","Cobalt"),("Sapphire","Sapphire"),("Periwinkle","Periwinkle"),("ArcticFox","Arctic Fox"),("Steel","Steel"),("Slate","Slate"),("IndigoNight","Indigo Night") }),
                ("Pastel",    new[] { ("CottonCandy","Cotton Candy"),("MintCream","Mint Cream"),("BabyBlue","Baby Blue"),("Peach","Peach"),("Lilac","Lilac"),("Buttercup","Buttercup"),("Blush","Blush"),("Seafoam","Seafoam"),("Wisteria","Wisteria"),("Dusk","Dusk") }),
                ("Specialty", new[] { ("CoffeeShop","Coffee Shop"),("Denim","Denim"),("Malachite","Malachite"),("SolarFlare","Solar Flare"),("Moonstone","Moonstone"),("Bubblegum","Bubblegum") }),
            };

            foreach (var (cat, items) in cats)
            {
                var sub = new System.Windows.Controls.MenuItem
                {
                    Header = cat,
                    Style  = null,
                };
                foreach (var (key, disp) in items)
                {
                    if (!AppTheme.ValidKeys.Contains(key)) continue;
                    string k   = key;
                    bool   act = string.Equals(k, cur, StringComparison.OrdinalIgnoreCase);
                    var    mi  = new System.Windows.Controls.MenuItem
                    {
                        Header     = (act ? "✓  " : "    ") + disp,
                        FontWeight = act ? FontWeights.SemiBold : FontWeights.Normal,
                        Style      = null,
                    };
                    mi.Click += (_, __) => Vm.SelectedTheme = AppTheme.AllThemes.First(t => t.Key == k);
                    sub.Items.Add(mi);
                }
                if (sub.Items.Count > 0) ctx.Items.Add(sub);
            }
            ctx.IsOpen = true;
        }

        private void UpdateSparkline(long bytesPerSec)
        {
            _speedHistory[_sparkHead % 60] = bytesPerSec;
            _sparkHead++;

            // Render 20 most recent samples as block characters ▁▂▃▄▅▆▇█
            const string bars = " ▁▂▃▄▅▆▇█";
            int count = Math.Min(_sparkHead, 20);
            int start = _sparkHead - count;
            long max = 0;
            for (int i = start; i < _sparkHead; i++) max = Math.Max(max, _speedHistory[i % 60]);

            if (max == 0) { TxtSparkline.Text = ""; return; }

            var sb = new System.Text.StringBuilder(count);
            for (int i = start; i < _sparkHead; i++)
            {
                long v = _speedHistory[i % 60];
                int idx = (int)(v * (bars.Length - 1) / max);
                sb.Append(bars[idx]);
            }
            TxtSparkline.Text = sb.ToString();
        }

        private void ClearSparkline()
        {
            Array.Clear(_speedHistory, 0, _speedHistory.Length);
            _sparkHead = 0;
            if (TxtSparkline != null) TxtSparkline.Text = "";
        }

        private void OnPowerModeChanged(object sender, Microsoft.Win32.PowerModeChangedEventArgs e)
        {
            if (!AppPrefs.Instance.PauseOnBattery || !IsRunning) return;
            if (e.Mode != Microsoft.Win32.PowerModes.StatusChange) return;

            bool onBattery = System.Windows.Forms.SystemInformation.PowerStatus.PowerLineStatus
                             == System.Windows.Forms.PowerLineStatus.Offline;
            Dispatcher.InvokeAsync(() =>
            {
                if (onBattery && _orchestrator.PauseLatch.IsSet)
                {
                    _orchestrator.TogglePause();
                    Vm.StatusText = "Paused — AC power disconnected.";
                    _tray?.ShowBalloon("File Copy Studio",
                        "Copy paused — AC power disconnected.",
                        System.Windows.Forms.ToolTipIcon.Warning);
                }
                else if (!onBattery && !_orchestrator.PauseLatch.IsSet)
                {
                    _orchestrator.TogglePause();
                    Vm.StatusText = "Resumed — AC power reconnected.";
                }
            });
        }

        // ── .fclist save / load ───────────────────────────────────────────────
        private void BtnSaveJob_Click(object sender, RoutedEventArgs e)
        {
            if (_items.Count == 0)
            {
                AppDialog.Show(this, "Run Scan first — there's nothing to save.",
                                "File Copy Studio", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                Title = "Save File Copy Studio job",
                Filter = "FileCopy job (*.fclist)|*.fclist|All files (*.*)|*.*",
                DefaultExt = FcJob.Extension,
                FileName = $"FileCopy_{DateTime.Now:yyyyMMdd_HHmmss}{FcJob.Extension}"
            };
            if (dlg.ShowDialog(this) != true) return;

            try
            {
                var job = FcJob.Build(AppPrefs.Instance, _items);
                FcJob.Save(dlg.FileName, job);
                if (job.Truncated)
                    AppDialog.Show(this,
                        $"Job saved, but was truncated to {FcJob.MaxItems:N0} items (scan has {_items.Count:N0}).\n\nSaved: {Path.GetFileName(dlg.FileName)}",
                        "Job truncated", MessageBoxButton.OK, MessageBoxImage.Warning);
                else
                    Vm.StatusText = $"Saved job: {Path.GetFileName(dlg.FileName)}";
            }
            catch (Exception ex)
            {
                AppDialog.Show(this, ex.Message, "Save failed", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnLoadJob_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Load File Copy Studio job",
                Filter = "FileCopy job (*.fclist)|*.fclist|All files (*.*)|*.*",
                DefaultExt = FcJob.Extension
            };
            if (dlg.ShowDialog(this) == true) LoadJobFile(dlg.FileName);
        }

        // ── Drag-drop folder onto source/dest text boxes ─────────────────────
        private void Path_DragOver(object sender, DragEventArgs e)
        {
            e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
        }

        private void TxtDest_Drop(object sender, DragEventArgs e)
        {
            DropFolder(e, p => Vm.Prefs.DestPath = p);
            CheckNetworkPathHint();
        }

        private void CheckNetworkPathHint()
        {
            bool srcNet = DriveAffinity.IsNetworkOrCloudPath(Vm.Prefs.SourcePath);
            bool dstNet = DriveAffinity.IsNetworkOrCloudPath(Vm.Prefs.DestPath);
            if (srcNet || dstNet)
            {
                string which = srcNet && dstNet ? "Source and destination are"
                             : srcNet ? "Source is" : "Destination is";
                Vm.StatusText = $"ℹ {which} on a network or cloud path — parallel copy auto-disabled.";
            }
        }

        private static void DropFolder(DragEventArgs e, Action<string> set)
        {
            if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
            var paths = e.Data.GetData(DataFormats.FileDrop) as string[];
            if (paths == null || paths.Length == 0) return;
            string p = paths[0];
            if (Directory.Exists(p)) set(p);
            else if (File.Exists(p)) set(Path.GetDirectoryName(p) ?? "");
        }

        // Conflict combo: binding sets the value; this re-plans existing scan results.
        private void CmbConflict_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            ReplanSelected();
            UpdateConflictPreview();
        }

        private void UpdateConflictPreview()
        {
            if (LblConflictPreview == null) return;
            LblConflictPreview.Text = Vm.Prefs.ConflictMode switch
            {
                ConflictMode.Overwrite        => "src 2024-03-15  →  dst 2024-01-01  ⇒  overwritten",
                ConflictMode.OverwriteIfNewer => "src 2024-03-15  >  dst 2024-01-01  ⇒  overwritten\nsrc 2024-01-01  =  dst 2024-01-01  ⇒  skipped",
                ConflictMode.OverwriteIfOlder => "src 2024-01-01  <  dst 2024-03-15  ⇒  overwritten\nsrc 2024-03-15  >  dst 2024-01-01  ⇒  skipped",
                ConflictMode.SkipIfSameDate   => "src 2024-03-15  =  dst 2024-03-15  ⇒  skipped\nsrc 2024-03-16  ≠  dst 2024-03-15  ⇒  overwritten",
                ConflictMode.SkipAll          => "src report.pdf  +  dst report.pdf exists  ⇒  skipped",
                ConflictMode.Rename           => "src report.pdf  +  dst report.pdf exists  ⇒  report (1).pdf",
                _                             => ""
            };
        }

        // ── Context menu ──────────────────────────────────────────────────────
        private void GridFiles_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            // Right-click on an unselected row should select just that row before
            // the context menu opens.
            if (e.OriginalSource is System.Windows.DependencyObject src
                && ItemsControl.ContainerFromElement(GridFiles, src) is DataGridRow row
                && row.Item is FileItem it
                && !GridFiles.SelectedItems.Contains(it))
            {
                GridFiles.SelectedItems.Clear();
                GridFiles.SelectedItem = it;
            }
        }

        private System.Collections.Generic.IEnumerable<FileItem> CtxTargets()
        {
            if (GridFiles.SelectedItems.Count > 0)
                return GridFiles.SelectedItems.Cast<FileItem>().ToList();
            return System.Linq.Enumerable.Empty<FileItem>();
        }

        private void CtxForceCopy_Click(object sender, RoutedEventArgs e)
        {
            foreach (var it in CtxTargets()) { it.ErrorText = null; it.Action = FileAction.Copy; }
            UpdateSummary();
        }

        private void CtxForceOverwrite_Click(object sender, RoutedEventArgs e)
        {
            foreach (var it in CtxTargets()) { it.ErrorText = null; it.Action = FileAction.Overwrite; }
            UpdateSummary();
        }

        private void CtxSkip_Click(object sender, RoutedEventArgs e)
        {
            foreach (var it in CtxTargets()) { it.ErrorText = null; it.Action = FileAction.Skip; }
            UpdateSummary();
        }

        private async void CtxRetryFailed_Click(object sender, RoutedEventArgs e)
        {
            var failed = CtxTargets().Where(x => x.Action == FileAction.Failed).ToList();
            if (failed.Count == 0) failed = _items.Where(x => x.Action == FileAction.Failed).ToList();
            if (failed.Count == 0) { Vm.StatusText = "No failed items to retry."; return; }
            if (IsRunning) { AppDialog.Show(this, "Cannot retry while a copy is running.", "File Copy Studio"); return; }
            if (!ValidatePaths()) return;

            foreach (var it in failed)
            {
                it.ErrorText      = null;
                it.ErrorTimestamp = null;
                it.Action         = it.DestExists ? FileAction.Overwrite : FileAction.Copy;
            }
            UpdateSummary();

            var session  = CopySession.FromPrefs(AppPrefs.Instance);
            int parallel = session.AutoParallelByDrive
                ? (DriveAffinity.SameVolume(session.SourcePath, session.DestPath) ? 1 : Math.Max(2, session.MaxParallel))
                : session.MaxParallel;

            var opts = new CopyEngine.CopyOptions
            {
                PreserveTimestamps  = session.PreserveTimestamps, PreserveAttributes = session.PreserveAttributes,
                VerifyCrc           = session.VerifyCrc,           DeleteSource       = session.DeleteSourceOnMove,
                BufferSizeKb        = session.BufferSizeKb,        ResumePartial      = session.ResumePartial,
                MaxParallel         = parallel,                    DestRoot           = session.DestPath,
                ThrottleBytesPerSec = session.ThrottleMbps > 0 ? session.ThrottleMbps * 1024L * 1024L / 8L : 0,
                WriteCrcSidecar     = session.WriteCrcSidecar,
                SymlinkPolicy       = session.SymlinkPolicy,
                Pause               = _orchestrator.PauseLatch
            };
            await _orchestrator.RetryFailedAsync(failed, opts);
            UpdateSummary();
        }

        private void CtxForceRename_Click(object sender, RoutedEventArgs e)
        {
            foreach (var it in CtxTargets()) { it.ErrorText = null; it.Action = FileAction.Rename; }
            UpdateSummary();
        }

        private void CtxOpenSourceFolder_Click(object sender, RoutedEventArgs e)
        {
            var it = CtxTargets().FirstOrDefault();
            if (it == null) return;
            RevealInExplorer(it.SourcePath);
        }

        private void CtxOpenDestFolder_Click(object sender, RoutedEventArgs e)
        {
            var it = CtxTargets().FirstOrDefault();
            if (it == null) return;
            string path = File.Exists(it.DestPath) ? it.DestPath : Path.GetDirectoryName(it.DestPath) ?? "";
            RevealInExplorer(path);
        }

        private void CtxCompare_Click(object sender, RoutedEventArgs e)
        {
            var it = CtxTargets().FirstOrDefault();
            if (it == null) return;

            var src = File.Exists(it.SourcePath) ? new FileInfo(it.SourcePath) : null;
            var dst = File.Exists(it.DestPath)   ? new FileInfo(it.DestPath)   : null;

            static string Fmt(FileInfo? fi) => fi == null ? "(not found)"
                : $"  Size     : {SizeFormat.Format(fi.Length)}\n"
                + $"  Modified : {fi.LastWriteTimeUtc:yyyy-MM-dd HH:mm:ss} UTC\n"
                + $"  Created  : {fi.CreationTimeUtc:yyyy-MM-dd HH:mm:ss} UTC\n"
                + $"  Attrs    : {fi.Attributes}";

            string crcPart = (string.IsNullOrEmpty(it.SourceCrc) && string.IsNullOrEmpty(it.DestCrc))
                ? "\n(Run Compute CRC32 to compare checksums)"
                : $"\nSrc CRC32 : {it.SourceCrc ?? "—"}\nDst CRC32 : {it.DestCrc ?? "—"}"
                + (it.SourceCrc == it.DestCrc && !string.IsNullOrEmpty(it.SourceCrc) ? "  ✓ match" : "");

            string match = src != null && dst != null
                ? src.Length == dst.Length && src.LastWriteTimeUtc == dst.LastWriteTimeUtc
                    ? "\n✓ Size and modified date match"
                    : "\n⚠ Differences detected"
                : "";

            AppDialog.Show(this,
                $"File: {it.RelativePath}\n" +
                $"Action: {it.Action}\n\n" +
                $"SOURCE\n{Fmt(src)}\n\n" +
                $"DESTINATION\n{Fmt(dst)}" +
                match + crcPart,
                "Compare src ↔ dst", MessageBoxButton.OK, MessageBoxImage.None);
        }

        private void CtxRevealDestFile_Click(object sender, RoutedEventArgs e)
        {
            var done = CtxTargets()
                .FirstOrDefault(x => x.Action is FileAction.Done or FileAction.Verified
                                  && File.Exists(x.DestPath));
            if (done != null) RevealInExplorer(done.DestPath);
            else Vm.StatusText = "No completed file in selection.";
        }


        private void CtxCopySourcePath_Click(object sender, RoutedEventArgs e)
        {
            var paths = CtxTargets().Select(x => x.SourcePath);
            CopyToClipboard(string.Join(Environment.NewLine, paths));
        }

        private void CtxCopyDestPath_Click(object sender, RoutedEventArgs e)
        {
            var paths = CtxTargets().Select(x => x.DestPath);
            CopyToClipboard(string.Join(Environment.NewLine, paths));
        }

        private void CopyToClipboard(string text)
        {
            try { Clipboard.SetText(text ?? ""); Vm.StatusText = "Copied to clipboard."; }
            catch { Vm.StatusText = "Clipboard unavailable."; }
        }

        private void CtxDiscardTemp_Click(object sender, RoutedEventArgs e)
        {
            int n = 0;
            foreach (var it in CtxTargets())
            {
                string tempPath = PartialFile.GetTempPath(it.DestPath);
                if (File.Exists(tempPath))
                {
                    PartialFile.DiscardTemp(it.DestPath);
                    it.ResumeOffset = 0;
                    if (it.Action == FileAction.Resume)
                        it.Action = it.DestExists ? FileAction.Overwrite : FileAction.Copy;
                    n++;
                }
            }
            UpdateSummary();
            Vm.StatusText = n > 0 ? $"Discarded {n} partial file(s)." : "No partial files to discard.";
        }

        // ── Selection helpers ─────────────────────────────────────────────────
        private void BtnSelectAll_Click (object sender, RoutedEventArgs e) => GridFiles.SelectAll();
        private void BtnSelectNone_Click(object sender, RoutedEventArgs e) => GridFiles.UnselectAll();
        private void BtnSkipSelected_Click(object sender, RoutedEventArgs e)
        {
            foreach (FileItem it in GridFiles.SelectedItems) it.Action = FileAction.Skip;
            UpdateSummary();
        }

        private void BtnSkipAll_Click(object sender, RoutedEventArgs e)
        {
            foreach (var it in _items) { it.ErrorText = null; it.Action = FileAction.Skip; }
            UpdateSummary();
            Vm.StatusText = $"Marked all {_items.Count:N0} files as Skip.";
        }


        // ── Section 5: Presets ────────────────────────────────────────────────
        // ── Drag handle ───────────────────────────────────────────────────────
        private void ResizeHandle_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            // WPF has no managed API for "thumb-style" resize on a borderless
            // canvas — WM_NCLBUTTONDOWN with HTBOTTOMRIGHT is the only path.
            const int HTBOTTOMRIGHT = 17;
            const int WM_NCLBUTTONDOWN = 0xA1;
            var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
            NativeMethods.ReleaseCapture();
            NativeMethods.SendMessage(hwnd, WM_NCLBUTTONDOWN, (nint)HTBOTTOMRIGHT, 0);
        }

        // WCAG 2.5.7 — keyboard alternative for the drag-resize handle.
        // Arrow keys resize in 10 px increments; Shift+Arrow = 1 px fine-tune.
        private void ResizeHandle_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            double step = (e.KeyboardDevice.Modifiers & System.Windows.Input.ModifierKeys.Shift) != 0 ? 1 : 10;
            switch (e.Key)
            {
                case System.Windows.Input.Key.Right: Width  = Math.Max(MinWidth,  Width  + step); e.Handled = true; break;
                case System.Windows.Input.Key.Left:  Width  = Math.Max(MinWidth,  Width  - step); e.Handled = true; break;
                case System.Windows.Input.Key.Down:  Height = Math.Max(MinHeight, Height + step); e.Handled = true; break;
                case System.Windows.Input.Key.Up:    Height = Math.Max(MinHeight, Height - step); e.Handled = true; break;
            }
        }
    }

    // .NET 10 WPF still requires WM_NCLBUTTONDOWN for custom thumb resize;
    // there is no managed equivalent. LibraryImport = source-generated P/Invoke.
    internal static partial class NativeMethods
    {
        [System.Runtime.InteropServices.LibraryImport("user32.dll")]
        public static partial nint SendMessage(nint hWnd, int msg, nint wParam, nint lParam);

        [System.Runtime.InteropServices.LibraryImport("user32.dll")]
        [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
        public static partial bool ReleaseCapture();
    }
}
