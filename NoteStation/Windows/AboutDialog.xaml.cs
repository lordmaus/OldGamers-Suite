using System;
using System.Diagnostics;
using System.Windows;
using Microsoft.Win32;

namespace NoteStation
{
    public partial class AboutDialog : Window
    {
        public AboutDialog()
        {
            InitializeComponent();
            WindowSizeMemory.Attach(this, "AboutDialog");
            VersionLabel.Text  = App.Version;
            DataPathLabel.Text = App.AppDataDir;
            ChkStartup.IsChecked = AppPrefs.Instance.StartWithWindows;
        }

        private void CopyDataPathBtn_Click(object sender, RoutedEventArgs e)
            => Clipboard.SetText(App.AppDataDir);

        private void WebsiteBtn_Click(object sender, RoutedEventArgs e)
            => Process.Start(new ProcessStartInfo("https://oldgamerssoftware.com/") { UseShellExecute = true });

        private void GitHubBtn_Click(object sender, RoutedEventArgs e)
            => Process.Start(new ProcessStartInfo("https://github.com/lordmaus/NoteStation") { UseShellExecute = true });

        private void VenmoBtn_Click(object sender, RoutedEventArgs e)
            => Process.Start(new ProcessStartInfo("https://venmo.com/Matthew-Pittman-80") { UseShellExecute = true });

        private void CashAppBtn_Click(object sender, RoutedEventArgs e)
            => Process.Start(new ProcessStartInfo("https://cash.app/$MatthewPittman80") { UseShellExecute = true });

        private void ChkStartup_Click(object sender, RoutedEventArgs e)
        {
            bool enable = ChkStartup.IsChecked == true;
            AppPrefs.Instance.StartWithWindows = enable;
            AppPrefs.Instance.Save();
            SetStartup(enable);
        }

        private static void SetStartup(bool enable)
        {
            const string key  = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
            const string name = "NoteStation";
            using var rk = Registry.CurrentUser.OpenSubKey(key, writable: true);
            if (rk == null) return;
            if (enable)
                rk.SetValue(name, $"\"{Environment.ProcessPath ?? string.Empty}\"");
            else
                rk.DeleteValue(name, throwOnMissingValue: false);
        }

        private void CheckUpdatesBtn_Click(object sender, RoutedEventArgs e)
            => Process.Start(new ProcessStartInfo("https://github.com/lordmaus/NoteStation/releases") { UseShellExecute = true });

        private void ReadmeBtn_Click(object sender, RoutedEventArgs e)
        {
            var path = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "README.md");
            if (System.IO.File.Exists(path))
                Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            else
                AppDialog.Show("README.md was not found next to the application.",
                    "NoteStation", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void CloseBtn_Click(object sender, RoutedEventArgs e) => Close();
    }
}
