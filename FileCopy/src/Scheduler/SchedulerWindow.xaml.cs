using System;
using System.IO;
using System.Text.Json;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

#nullable enable


namespace FileCopy
{
    /// <summary>
    /// Thin code-behind for SchedulerWindow.
    /// All state and commands live in SchedulerViewModel.
    /// This file handles only: folder pickers, file dialogs,
    /// confirmation dialogs, and log viewer windows.
    /// </summary>
    public partial class SchedulerWindow : Window
    {
        private readonly SchedulerViewModel _vm;

        public SchedulerWindow(SchedulerEngine engine)
        {
            InitializeComponent();

            _vm = new SchedulerViewModel(engine);
            DataContext = _vm;

            // Style resize grip to match dark surface
            var gripStyle = new Style(typeof(ResizeGrip));
            gripStyle.Setters.Add(new Setter(ResizeGrip.ForegroundProperty,
                TryBrush("BrushSurf2") ?? System.Windows.Media.Brushes.Gray));
            Resources[typeof(ResizeGrip)] = gripStyle;

            // Wire events from ViewModel that require UI interaction
            _vm.PickFolderRequested       += OnPickFolder;
            _vm.ShowLogRequested          += OnShowLog;
            _vm.ShowMessageRequested      += OnShowMessage;
            _vm.DeleteConfirmRequested    += OnDeleteConfirm;
            _vm.RunNowRequested           += OnRunNow;
            _vm.ExportRequested           += OnExport;
            _vm.ImportRequested           += OnImport;
            _vm.ClearJobLogRequested      += OnClearJobLog;
            _vm.ViewActivityLogRequested  += OnViewActivityLog;
            _vm.ConfirmMoveRequested       = OnConfirmMove;
        }

        private bool OnConfirmMove(string message) =>
            AppDialog.Show(this, message, "Move job", MessageBoxButton.YesNo,
                            MessageBoxImage.Warning, MessageBoxResult.No) == MessageBoxResult.Yes;

        // ── Folder pickers ────────────────────────────────────────────────────
        private void BtnPickSource_Click(object sender, RoutedEventArgs e)
            => OnPickFolder(true);

        private void BtnPickDest_Click(object sender, RoutedEventArgs e)
            => OnPickFolder(false);

        private void OnPickFolder(bool isSource)
        {
            using var dlg = new System.Windows.Forms.FolderBrowserDialog
            {
                Description = isSource ? "Select source folder" : "Select destination folder",
                UseDescriptionForTitle = true,
                AutoUpgradeEnabled = true
            };
            var owner = new System.Windows.Forms.NativeWindow();
            owner.AssignHandle(new System.Windows.Interop.WindowInteropHelper(this).Handle);
            var result = dlg.ShowDialog(owner);
            owner.ReleaseHandle();
            if (result != System.Windows.Forms.DialogResult.OK) return;

            if (isSource) _vm.EditSource = dlg.SelectedPath;
            else          _vm.EditDest   = dlg.SelectedPath;
        }

        // ── Dialogs ───────────────────────────────────────────────────────────
        private void OnShowMessage(string msg)
            => AppDialog.Show(this, msg, "Scheduler", MessageBoxButton.OK, MessageBoxImage.Warning);

        private void OnDeleteConfirm(SchedulerJob job)
        {
            var r = AppDialog.Show(this, $"Delete job \"{job.Label}\"?\n\nThe job log will also be deleted.",
                "Confirm delete", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No);
            if (r == MessageBoxResult.Yes) _vm.ConfirmDelete(job);
        }

        private void OnRunNow(SchedulerJob job)
        {
            // Check if main window is running a copy
            if (Application.Current?.MainWindow is MainWindow mw && mw.IsRunning)
            {
                AppDialog.Show(this,
                    "A copy is already running. Wait for it to finish before running a scheduled job manually.",
                    "Run now", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            // Engine fires JobFired on dispatcher → MainWindow.OnSchedulerJobFired
            // No direct reference needed — engine raises the event
            // (SchedulerWindow doesn't own the engine's JobFired subscribers)
            // We need to fire through the engine
            // Access engine via ViewModel reflection is messy; expose FireNow via VM
            _vm.FireJob(job.Id);
        }

        // ── Export / Import ───────────────────────────────────────────────────
        private void OnExport()
        {
            var jobs = _vm.Jobs.Select(x => x.Job).ToList();
            if (jobs.Count == 0) return;
            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                Title = "Export scheduler jobs",
                Filter = "JSON files (*.json)|*.json",
                DefaultExt = ".json",
                FileName = $"FileCopyStudio_Schedule_{DateTime.Now:yyyyMMdd}.json"
            };
            if (dlg.ShowDialog(this) != true) return;
            try
            {
                string json = JsonSerializer.Serialize(jobs, SchedulerJobContext.Default.ListSchedulerJob);
                File.WriteAllText(dlg.FileName, json);
                AppDialog.Show(this, $"Exported {jobs.Count} job(s).", "Export",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            { AppDialog.Show(this, ex.Message, "Export failed", MessageBoxButton.OK, MessageBoxImage.Error); }
        }

        private void OnImport()
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Import scheduler jobs",
                Filter = "JSON files (*.json)|*.json",
                DefaultExt = ".json"
            };
            if (dlg.ShowDialog(this) != true) return;
            try
            {
                var info = new FileInfo(dlg.FileName);
                if (info.Length > 1024 * 1024)
                { AppDialog.Show(this, "File too large.", "Import", MessageBoxButton.OK, MessageBoxImage.Warning); return; }
                string json = File.ReadAllText(dlg.FileName);
                var list = JsonSerializer.Deserialize(json, SchedulerJobContext.Default.ListSchedulerJob);
                if (list == null || list.Count == 0)
                { AppDialog.Show(this, "No valid jobs found.", "Import", MessageBoxButton.OK, MessageBoxImage.Warning); return; }
                int added = 0;
                foreach (var job in list)
                {
                    job.Id = Guid.NewGuid().ToString("N")[..8];
                    job.NextRun = null; job.LastRun = null; job.LastResult = "";
                    _vm.AddImportedJob(job);
                    added++;
                }
                _vm.RefreshJobs();
                AppDialog.Show(this, $"Imported {added} job(s).", "Import",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            { AppDialog.Show(this, ex.Message, "Import failed", MessageBoxButton.OK, MessageBoxImage.Error); }
        }

        // ── Job log ───────────────────────────────────────────────────────────
        private void OnClearJobLog(SchedulerJob job)
        {
            var r = AppDialog.Show(this,
                $"Clear all log history for \"{job.Label}\"?\nThis cannot be undone.",
                "Clear job log", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No);
            if (r != MessageBoxResult.Yes) return;
            try
            {
                if (File.Exists(job.JobLogPath)) File.Delete(job.JobLogPath);
                _vm.RefreshJobs();
            }
            catch (Exception ex)
            { AppDialog.Show(this, "Could not delete log: " + ex.Message, "Error",
                MessageBoxButton.OK, MessageBoxImage.Error); }
        }

        private void OnViewActivityLog()
        {
            string logPath = Path.Combine(PortableMode.LogsDir,
                $"filecopy-studio-{DateTime.Now:yyyy-MM-dd}.log");
            if (!File.Exists(logPath))
            {
                string yest = Path.Combine(PortableMode.LogsDir,
                    $"filecopy-studio-{DateTime.Now.AddDays(-1):yyyy-MM-dd}.log");
                if (File.Exists(yest)) logPath = yest;
            }
            if (!File.Exists(logPath))
            { AppDialog.Show(this, "No log file found.", "Log viewer",
                MessageBoxButton.OK, MessageBoxImage.Information); return; }

            try
            {
                using var fs = new FileStream(logPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var sr = new StreamReader(fs);
                var lines = sr.ReadToEnd().Split('\n')
                    .Where(l => l.Contains("[SchedulerEngine]", StringComparison.Ordinal)
                             || (l.Contains("[MainWindow]", StringComparison.Ordinal)
                                 && l.Contains("Scheduler:", StringComparison.Ordinal)))
                    .ToArray();
                string content = lines.Length > 0
                    ? string.Join(Environment.NewLine, lines)
                    : "(No scheduler entries in today's log yet)";
                ShowLogWindow("Scheduler activity log — " + Path.GetFileName(logPath), logPath, content);
            }
            catch (Exception ex)
            { AppDialog.Show(this, "Cannot read log: " + ex.Message, "Log viewer",
                MessageBoxButton.OK, MessageBoxImage.Error); }
        }

        private void OnShowLog(string title, string filePath)
            => ShowLogWindow(title, filePath);

        private void ShowLogWindow(string title, string filePath, string? prefiltered = null)
        {
            string? content = prefiltered;
            if (content == null)
            {
                try
                {
                    using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                    using var sr = new StreamReader(fs);
                    content = sr.ReadToEnd();
                }
                catch (Exception ex)
                { AppDialog.Show(this, "Cannot read log: " + ex.Message, "Log viewer",
                    MessageBoxButton.OK, MessageBoxImage.Error); return; }
            }

            var w = new Window
            {
                Title  = title, Width = 900, Height = 640,
                Owner  = this, WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Background = TryBrush("BrushBg") ?? System.Windows.Media.Brushes.Black,
                ResizeMode = ResizeMode.CanResize   // WCAG: AccessibleResizeGrip below, not the native grip
            };
            w.SetResourceReference(Window.BackgroundProperty, "BrushBg");

            var tb = new TextBox
            {
                Text = content, IsReadOnly = true,
                FontFamily = new System.Windows.Media.FontFamily("Consolas"), FontSize = 12,
                Background = TryBrush("BrushSurf") ?? System.Windows.Media.Brushes.Black,
                Foreground = TryBrush("BrushText") ?? System.Windows.Media.Brushes.White,
                BorderThickness = new Thickness(0), AcceptsReturn = true,
                TextWrapping = TextWrapping.NoWrap,
                VerticalScrollBarVisibility   = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto
            };
            var dock = new DockPanel();
            var btnRow = new StackPanel { Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(8) };
            DockPanel.SetDock(btnRow, Dock.Bottom);
            var btnOpen = new Button
            {
                MinWidth = 130, MinHeight = 44,
                Margin = new Thickness(0,0,8,0), Padding = new Thickness(10,0,10,0),
                Background = TryBrush("BrushSurf2") ?? System.Windows.Media.Brushes.DimGray,
                Foreground = TryBrush("BrushText")  ?? System.Windows.Media.Brushes.White,
                BorderThickness = new Thickness(0)
            };
            btnOpen.Content = JtIcons.Label(btnOpen, "Open in editor", "open");
            btnOpen.Click += (_, __) =>
            {
                try { System.Diagnostics.Process.Start(
                    new System.Diagnostics.ProcessStartInfo(filePath) { UseShellExecute = true }); }
                catch { }
            };
            var btnClose = new Button
            {
                Content = "Close", MinWidth = 80, MinHeight = 44, IsCancel = true,
                Padding = new Thickness(10,0,10,0),
                Background = TryBrush("BrushSurf2") ?? System.Windows.Media.Brushes.DimGray,
                Foreground = TryBrush("BrushText")  ?? System.Windows.Media.Brushes.White,
                BorderThickness = new Thickness(0)
            };
            btnClose.Click += (_, __) => w.Close();
            btnRow.Children.Add(btnOpen);
            btnRow.Children.Add(btnClose);
            dock.Children.Add(btnRow);
            dock.Children.Add(tb);
            foreach (var btn in new[] { btnOpen, btnClose })
            {
                btn.SetResourceReference(Control.BackgroundProperty, "BrushSurf2");
                btn.SetResourceReference(Control.ForegroundProperty, "BrushText");
            }
            tb.SetResourceReference(Control.BackgroundProperty, "BrushSurf");
            tb.SetResourceReference(Control.ForegroundProperty, "BrushText");
            var root = new Grid();
            root.Children.Add(dock);
            root.Children.Add(new OldGamers.Wpf.Shared.Controls.AccessibleResizeGrip());
            w.Content = root;
            w.Loaded += (_, __) => tb.Dispatcher.InvokeAsync(
                tb.ScrollToEnd,
                System.Windows.Threading.DispatcherPriority.Background);
            w.ShowDialog();
        }

        // ── Helpers ───────────────────────────────────────────────────────────
        private static System.Windows.Media.SolidColorBrush? TryBrush(string key)
            => Application.Current?.Resources[key] as System.Windows.Media.SolidColorBrush;

        // ── Window size persistence ───────────────────────────────────────────
        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            var (w, h) = WindowSizeCache.Get("FileCopyScheduler", 980, 980);
            Width = w; Height = h;
        }

        protected override void OnClosed(EventArgs e)
        {
            WindowSizeCache.Set("FileCopyScheduler", Width, Height);
            base.OnClosed(e);
        }

        // Drag handle — canonical pattern, shared NativeMethods (see MainWindow.xaml.cs).
        private void ResizeHandle_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            const int HTBOTTOMRIGHT = 17;
            const int WM_NCLBUTTONDOWN = 0xA1;
            var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
            NativeMethods.ReleaseCapture();
            NativeMethods.SendMessage(hwnd, WM_NCLBUTTONDOWN, (nint)HTBOTTOMRIGHT, 0);
        }

        // WCAG 2.5.7 — keyboard alternative for the drag-resize handle.
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
}