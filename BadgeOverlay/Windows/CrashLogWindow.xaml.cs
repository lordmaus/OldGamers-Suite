using System;
using System.Diagnostics;
using System.IO;
using System.Windows;

namespace BadgeOverlay
{
    /// <summary>
    /// In-app viewer for %APPDATA%\BadgeOverlay\crash.log.
    /// Read-only, monospace, with refresh/copy/clear/open-folder actions.
    /// Saves users having to navigate the file system to diagnose issues.
    /// </summary>
    public partial class CrashLogWindow : Window
    {
        private static string LogPath =>
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "BadgeOverlay", "crash.log");

        public CrashLogWindow()
        {
            InitializeComponent();
            CursorPreserver.Attach(this);
            Loaded += (s, e) => LoadLog();
        }

        private void LoadLog()
        {
            string path = LogPath;
            LblPath.Text = path;
            LblStatus.Text = "";
            try
            {
                if (!File.Exists(path))
                {
                    TxtLog.Text    = "";
                    LblStats.Text  = "Log file does not exist yet — nothing has been logged.";
                    return;
                }

                // Tail last 256 KB so opening a large rolled log is fast and
                // we don't load the entire rolling cap into a TextBox.
                const long tailLimit = 256L * 1024;
                var fi = new FileInfo(path);
                using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                if (fi.Length > tailLimit)
                {
                    fs.Seek(fi.Length - tailLimit, SeekOrigin.Begin);
                    // Advance to next newline so we don't start mid-line
                    int b;
                    while ((b = fs.ReadByte()) != -1 && b != '\n') { }
                }
                using var sr = new StreamReader(fs);
                TxtLog.Text = sr.ReadToEnd();

                LblStats.Text =
                    $"{fi.Length:N0} bytes · last modified {fi.LastWriteTime:yyyy-MM-dd HH:mm:ss}"
                    + (fi.Length > tailLimit ? "  —  showing last 256 KB" : "");

                // Scroll to bottom — most recent entries are what users care about
                TxtLog.CaretIndex = TxtLog.Text.Length;
                TxtLog.ScrollToEnd();
            }
            catch (Exception ex)
            {
                TxtLog.Text = $"(Could not read log file.)\r\n{ex.Message}";
                CrashLog.Write("CrashLogWindow.LoadLog", ex);
            }
        }

        private void BtnRefresh_Click(object sender, RoutedEventArgs e)
        {
            LoadLog();
            LblStatus.Text = "Reloaded.";
        }

        private void BtnCopy_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (string.IsNullOrEmpty(TxtLog.Text))
                {
                    LblStatus.Text = "Nothing to copy — log is empty.";
                    return;
                }
                Clipboard.SetText(TxtLog.Text);
                LblStatus.Text = $"Copied {TxtLog.Text.Length:N0} characters to clipboard.";
            }
            catch (Exception ex)
            {
                LblStatus.Text = "Copy failed — see log.";
                CrashLog.Write("CrashLogWindow.BtnCopy", ex);
            }
        }

        private void BtnOpenFolder_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string path = LogPath;
                string dir  = Path.GetDirectoryName(path);
                if (string.IsNullOrEmpty(dir)) return;
                Directory.CreateDirectory(dir);
                // ArgumentList: no hand-built quoting of the path (FileCopy RevealInExplorer pattern).
                var psi = new ProcessStartInfo("explorer.exe") { UseShellExecute = false };
                if (File.Exists(path)) psi.ArgumentList.Add("/select," + path);
                else                   psi.ArgumentList.Add(dir);
                Process.Start(psi);
                LblStatus.Text = "Opened folder.";
            }
            catch (Exception ex)
            {
                LblStatus.Text = "Could not open folder — see log.";
                CrashLog.Write("CrashLogWindow.BtnOpenFolder", ex);
            }
        }

        private void BtnClear_Click(object sender, RoutedEventArgs e)
        {
            var r = AppDialog.Show(
                "Clear all entries from the crash log?\n\n" +
                "The log will be moved to crash.log.bak so you can still " +
                "recover entries if needed. Clearing again replaces the backup.",
                "Clear log",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning,
                MessageBoxResult.No);
            if (r != MessageBoxResult.Yes) return;

            try
            {
                string path = LogPath;
                string bak  = path + ".bak";
                // Soft-undo: move the live log to .bak rather than deleting.
                // The previous rotated .old file is preserved as a second-
                // generation backup. This means at most two "generations"
                // of history exist: current + last-cleared + last-rotated.
                if (File.Exists(path))
                {
                    if (File.Exists(bak)) File.Delete(bak);
                    File.Move(path, bak);
                }
                TxtLog.Text = "";
                LblStats.Text = $"Log cleared. Previous entries kept at {System.IO.Path.GetFileName(bak)}.";
                LblStatus.Text = "Cleared.";
            }
            catch (Exception ex)
            {
                LblStatus.Text = "Clear failed — the file may be in use.";
                CrashLog.Write("CrashLogWindow.BtnClear", ex);
            }
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e) => Close();

        // ── Window size persistence ───────────────────────────────────────────
        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            var (w, h) = WindowSizeCache.Get("CrashLogWindow", 720, 600);
            Width = w; Height = h;
        }

        protected override void OnClosed(EventArgs e)
        {
            WindowSizeCache.Set("CrashLogWindow", Width, Height);
            base.OnClosed(e);
        }
    }
}
