using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;

namespace TimerStudio
{
    public partial class AboutDialog : Window
    {
        private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string AppKey = "TimerStudio";

        // Raised when Import replaces timers so MainWindow can reload
        public event EventHandler TimersImported;

        public static string Version => VersionInfo.Display;

        public AboutDialog()
        {
            InitializeComponent();
            VersionLabel.Text      = Version;
            SettingsPathLabel.Text = TimerPersistence.SaveFilePath;
            ChkStartup.IsChecked   = IsStartupEnabled();
        }

        // ── Startup ───────────────────────────────────────────────────────────
        private static bool IsStartupEnabled()
        {
            try
            {
                using (var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RunKey, false))
                {
                    if (key == null) return false;
                    var val = key.GetValue(AppKey) as string;
                    if (val == null) return false;

                    // Auto-repair: if the stored path ends in .dll, rewrite it to the .exe
                    if (val.EndsWith(".dll\"", StringComparison.OrdinalIgnoreCase) ||
                        val.EndsWith(".dll",  StringComparison.OrdinalIgnoreCase))
                    {
                        // Silently fix the bad entry
                        SetStartup(true);
                    }
                    return true;
                }
            }
            catch { return false; }
        }

        private static void SetStartup(bool enable)
        {
            try
            {
                using (var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RunKey, true))
                {
                    if (key == null) return;
                    if (enable)
                    {
                        // Assembly.Location returns the .dll on self-contained .NET apps.
                        // Use the actual running .exe path instead.
                        string exePath = Environment.ProcessPath
                            ?? System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName
                            ?? Assembly.GetExecutingAssembly().Location;
                        key.SetValue(AppKey, "\"" + exePath + "\"");
                    }
                    else
                        key.DeleteValue(AppKey, throwOnMissingValue: false);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Could not update startup setting:\n" + ex.Message,
                    "Startup", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        // ── Export / Import ───────────────────────────────────────────────────
        private void ExportBtn_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                Title            = "Export Timer Studio Timers",
                Filter           = "JSON files (*.json)|*.json|All files (*.*)|*.*",
                FileName         = "TimerStudio_backup_" + DateTime.Now.ToString("yyyy-MM-dd") + ".json",
                DefaultExt       = ".json",
                InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Desktop)
            };
            if (dlg.ShowDialog(this) != true) return;

            try
            {
                if (File.Exists(TimerPersistence.SaveFilePath))
                    File.Copy(TimerPersistence.SaveFilePath, dlg.FileName, overwrite: true);
                else
                    MessageBox.Show("No saved timers file found to export.",
                        "Export", MessageBoxButton.OK, MessageBoxImage.Information);

                MessageBox.Show("Timers exported successfully to:\n" + dlg.FileName,
                    "Export Complete", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Export failed:\n" + ex.Message,
                    "Export Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void ImportBtn_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Title            = "Import Timer Studio Timers",
                Filter           = "JSON files (*.json)|*.json|All files (*.*)|*.*",
                DefaultExt       = ".json",
                InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Desktop)
            };
            if (dlg.ShowDialog(this) != true) return;

            // Validate that the file looks like a timers file
            try
            {
                string content = File.ReadAllText(dlg.FileName);
                if (!content.TrimStart().StartsWith("["))
                {
                    MessageBox.Show("The selected file does not appear to be a valid Timer Studio export.",
                        "Import Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Could not read file:\n" + ex.Message,
                    "Import Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var result = MessageBox.Show(
                "Importing will replace all current timers.\n\nDo you want to continue?",
                "Import Timers", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result != MessageBoxResult.Yes) return;

            try
            {
                // Backup current timers before overwriting
                string backup = TimerPersistence.SaveFilePath + ".bak";
                if (File.Exists(TimerPersistence.SaveFilePath))
                    File.Copy(TimerPersistence.SaveFilePath, backup, overwrite: true);

                File.Copy(dlg.FileName, TimerPersistence.SaveFilePath, overwrite: true);

                if (TimersImported != null) TimersImported(this, EventArgs.Empty);
                MessageBox.Show("Timers imported successfully. The timer list has been refreshed.",
                    "Import Complete", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Import failed:\n" + ex.Message,
                    "Import Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        // ── Handlers ─────────────────────────────────────────────────────────
        private void CloseBtn_Click(object sender, RoutedEventArgs e) => Close();

        private void ChkStartup_Click(object sender, RoutedEventArgs e)
            => SetStartup(ChkStartup.IsChecked == true);

        private void CopySettingsPathBtn_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Clipboard.SetText(SettingsPathLabel.Text);
                var btn = sender as System.Windows.Controls.Button;
                if (btn != null)
                {
                    var original = btn.Content;
                    btn.Content = "✓  Copied!";
                    var t = new System.Windows.Threading.DispatcherTimer
                        { Interval = TimeSpan.FromSeconds(2) };
                    t.Tick += (s, _) => { t.Stop(); btn.Content = original; };
                    t.Start();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[TimerStudio] CopyPath failed: " + ex.Message);
                MessageBox.Show("Could not copy path: " + ex.Message,
                    "Copy Failed", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void ReadmeBtn_Click(object sender, RoutedEventArgs e)
        {
            string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "README.md");
            if (File.Exists(path))
                OpenUrl(path);
            else
                MessageBox.Show(
                    "README.md not found next to the exe.\n\nIt is included in the installer build.",
                    "Help", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void WebsiteBtn_Click(object sender, RoutedEventArgs e)
        {
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    ("https://oldgamerssoftware.com/") { UseShellExecute = true }); }
            catch { }
        }

        private void GitHubBtn_Click(object sender, RoutedEventArgs e)
        {
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    ("https://github.com/lordmaus/TimerStudio") { UseShellExecute = true }); }
            catch { }
        }

        private void VenmoBtn_Click(object sender, RoutedEventArgs e)
            => OpenUrl("https://account.venmo.com/u/Matthew-Pittman-80");

        private void CashAppBtn_Click(object sender, RoutedEventArgs e)
            => OpenUrl("https://cash.app/$MatthewPittman80");

        private static void OpenUrl(string url)
        {
            try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
            catch (Exception ex) { Debug.WriteLine("[TimerStudio] OpenUrl failed: " + ex.Message); }
        }

        // ── Window size persistence ───────────────────────────────────────────
        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            var (w, h) = WindowSizeCache.Get("TSAbout", 540, 640);
            Width = w; Height = h;
        }

        protected override void OnClosed(EventArgs e)
        {
            WindowSizeCache.Set("TSAbout", Width, Height);
            base.OnClosed(e);
        }
    }
}