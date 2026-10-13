using System;
using System.Diagnostics;
using System.Windows;

namespace SuperMagnifier
{
    public partial class AboutDialog : Window
    {
        /// <summary>
        /// Version string read from the executing assembly at runtime.
        /// Falls back to the csproj Version property set via AssemblyInformationalVersion.
        /// </summary>
        public static string Version => VersionInfo.Display;

        public AboutDialog()
        {
            InitializeComponent();
            VersionLabel.Text      = Version;
            SettingsPathLabel.Text = AppSettings.FilePath;
        }

        private void CloseBtn_Click(object sender, RoutedEventArgs e) => Close();

        // Copy settings file path to clipboard
        private void OpenCrashLogBtn_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string log = CrashLog.LogPath;
                string dir = System.IO.Path.GetDirectoryName(log);
                if (string.IsNullOrEmpty(dir)) return;
                System.IO.Directory.CreateDirectory(dir);
                var psi = new ProcessStartInfo("explorer.exe") { UseShellExecute = false };
                if (System.IO.File.Exists(log)) psi.ArgumentList.Add("/select," + log);
                else                            psi.ArgumentList.Add(dir);
                Process.Start(psi);
            }
            catch (Exception ex) { CrashLog.Write("OpenCrashLogBtn", ex); }
        }

        private void CopySettingsPathBtn_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                System.Windows.Clipboard.SetText(AppSettings.FilePath);
                // Brief visual feedback — change button content temporarily
                if (sender is System.Windows.Controls.Button btn)
                {
                    var original = btn.Content;
                    btn.Content = "✓  Copied!";
                    var timer = new System.Windows.Threading.DispatcherTimer
                        { Interval = TimeSpan.FromSeconds(2) };
                    timer.Tick += (s, _) => { timer.Stop(); btn.Content = original; };
                    timer.Start();
                }
            }
            catch (System.Exception ex)
            {
                AppDialog.Show($"Could not copy path: {ex.Message}",
                    "Copy Failed", System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Warning);
            }
        }

        // FIX #11: Open the README next to the exe
        private void ReadmeBtn_Click(object sender, RoutedEventArgs e)
        {
            string readmePath = System.IO.Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory, "README.md");
            if (System.IO.File.Exists(readmePath))
                OpenUrl(readmePath);
            else
                AppDialog.Show(
                    "README.md not found next to the exe.",
                    "Help", System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Information);
        }

        private void VenmoBtn_Click(object sender, RoutedEventArgs e)
            => OpenUrl("https://account.venmo.com/u/Matthew-Pittman-80");

        private void CashAppBtn_Click(object sender, RoutedEventArgs e)
            => OpenUrl("https://cash.app/$MatthewPittman80");

        private void CheckUpdatesBtn_Click(object sender, RoutedEventArgs e)
            => OpenUrl("https://github.com/lordmaus/SuperMagnifierLV/releases");

        private void WebsiteBtn_Click(object sender, RoutedEventArgs e)
            => OpenUrl("https://oldgamerssoftware.com/");

        private void GitHubBtn_Click(object sender, RoutedEventArgs e)
            => OpenUrl("https://github.com/lordmaus/SuperMagnifierLV");


        private static void OpenUrl(string url)
        {
            try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
            catch (System.Exception _ex) { System.Diagnostics.Debug.WriteLine($"[SuperMagnifier] {_ex.Message}"); }
        }

        // ── Window size persistence ───────────────────────────────────────────
        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            var (w, h) = WindowSizeCache.Get("SMLVAbout", 540, 640);
            Width = w; Height = h;
        }

        protected override void OnClosed(EventArgs e)
        {
            WindowSizeCache.Set("SMLVAbout", Width, Height);
            base.OnClosed(e);
        }

        // Drag handle — WM_NCLBUTTONDOWN via NativeMethods (already declared, previously
        // unused anywhere in the project — see Native/NativeMethods.cs).
        private void ResizeHandle_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            const uint HTBOTTOMRIGHT    = 17;
            const uint WM_NCLBUTTONDOWN = 0xA1;
            var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
            NativeMethods.ReleaseCapture();
            NativeMethods.SendMessage(hwnd, WM_NCLBUTTONDOWN, (IntPtr)HTBOTTOMRIGHT, IntPtr.Zero);
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