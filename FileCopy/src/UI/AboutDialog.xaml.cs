using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Input;

#nullable enable

namespace FileCopy
{
    public partial class AboutDialog : Window
    {
        public AboutDialog()
        {
            InitializeComponent();
            Loaded += OnLoaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            // Version — single source via VersionInfo.
            var asm = typeof(AboutDialog).Assembly;

            var infoVer  = asm.GetCustomAttribute<System.Reflection.AssemblyInformationalVersionAttribute>()
                              ?.InformationalVersion ?? "";
            string builtStr = "(unknown)";
            int buildIdx = infoVer.IndexOf("build-", StringComparison.Ordinal);
            if (buildIdx >= 0)
            {
                string tag = infoVer.Substring(buildIdx + 6);
                if (tag.Length >= 8 &&
                    DateTime.TryParseExact(tag.Substring(0, 8), "yyyyMMdd",
                        System.Globalization.CultureInfo.InvariantCulture,
                        System.Globalization.DateTimeStyles.None, out var bd))
                    builtStr = bd.ToString("yyyy-MM-dd");
            }

            VersionLabel.Text      = $"{VersionInfo.Display}  (built {builtStr})";
            PlatformLabel.Text     = PortableMode.Active
                                     ? "Windows · WPF · .NET 10  [Portable]"
                                     : "Windows · WPF · .NET 10";
            SettingsPathLabel.Text = PortableMode.DataDir;
        }

        // ── Data files ────────────────────────────────────────────────────────

        private void BtnCopyPath_Click(object sender, RoutedEventArgs e)
        {
            try { Clipboard.SetText(PortableMode.DataDir); } catch { }
        }

        private void BtnOpenFolder_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo(PortableMode.DataDir) { UseShellExecute = true });
            }
            catch { }
        }

        // ── Help ─────────────────────────────────────────────────────────────

        private void BtnViewLog_Click(object sender, RoutedEventArgs e)
        {
            string logPath = Path.Combine(PortableMode.LogsDir,
                $"filecopy-studio-{DateTime.Now:yyyy-MM-dd}.log");
            if (!File.Exists(logPath))
            {
                AppDialog.Show(this, "No log file found for today.", "Log",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            try
            {
                Process.Start(new ProcessStartInfo(logPath) { UseShellExecute = true });
            }
            catch { }
        }

        // ── Links ─────────────────────────────────────────────────────────────

        private void BtnWebsite_Click(object sender, RoutedEventArgs e)
            => OpenUrl("https://oldgamerssoftware.com/");

        private void BtnGitHub_Click(object sender, RoutedEventArgs e)
            => OpenUrl("https://github.com/lordmaus/FileCopyStudio");

        // ── Donate ────────────────────────────────────────────────────────────

        private void BtnVenmo_Click(object sender, RoutedEventArgs e)
            => OpenUrl("https://account.venmo.com/u/Matthew-Pittman-80");

        private void BtnCashApp_Click(object sender, RoutedEventArgs e)
            => OpenUrl("https://cash.app/$MatthewPittman80");

        // ── Close ─────────────────────────────────────────────────────────────

        private void BtnClose_Click(object sender, RoutedEventArgs e)
            => Close();

        // ── Helper ────────────────────────────────────────────────────────────

        private static void OpenUrl(string url)
        {
            try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
            catch { }
        }

        // ── Window size persistence ───────────────────────────────────────────
        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            var (w, h) = WindowSizeCache.Get("FileCopyAbout", 540, 640);
            Width = w; Height = h;
        }

        protected override void OnClosed(EventArgs e)
        {
            WindowSizeCache.Set("FileCopyAbout", Width, Height);
            base.OnClosed(e);
        }

        // ── Drag handle (see MainWindow.xaml.cs — same pattern, NativeMethods shared) ──────────
        private void ResizeHandle_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            const int HTBOTTOMRIGHT = 17;
            const int WM_NCLBUTTONDOWN = 0xA1;
            var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
            NativeMethods.ReleaseCapture();
            NativeMethods.SendMessage(hwnd, WM_NCLBUTTONDOWN, (nint)HTBOTTOMRIGHT, 0);
        }

        // WCAG 2.5.7 — keyboard alternative for the drag-resize handle.
        private void ResizeHandle_KeyDown(object sender, KeyEventArgs e)
        {
            double step = (e.KeyboardDevice.Modifiers & ModifierKeys.Shift) != 0 ? 1 : 10;
            switch (e.Key)
            {
                case Key.Right: Width  = Math.Max(MinWidth,  Width  + step); e.Handled = true; break;
                case Key.Left:  Width  = Math.Max(MinWidth,  Width  - step); e.Handled = true; break;
                case Key.Down:  Height = Math.Max(MinHeight, Height + step); e.Handled = true; break;
                case Key.Up:    Height = Math.Max(MinHeight, Height - step); e.Handled = true; break;
            }
        }
    }
}