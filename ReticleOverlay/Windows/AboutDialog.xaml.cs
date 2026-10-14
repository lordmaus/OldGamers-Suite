using System;
using System.Diagnostics;
using System.IO;
using System.Windows;
using Microsoft.Win32;

namespace ReticleOverlay
{
    public partial class AboutDialog : Window
    {
        public event EventHandler ProfilesImported;

        private System.Windows.Threading.DispatcherTimer _copyTimer;

        public static string Version => VersionInfo.Display;

        public AboutDialog()
        {
            InitializeComponent();
            VersionLabel.Text      = Version;
            SettingsPathLabel.Text = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "ReticleOverlay");

            _copyTimer = new System.Windows.Threading.DispatcherTimer
                { Interval = TimeSpan.FromSeconds(2) };
            _copyTimer.Tick += (s, _) =>
            {
                _copyTimer.Stop();
                BtnCopyPathText.Text = "Copy path";
            };
        }

        private void BtnExport_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new SaveFileDialog
            {
                Title      = "Export Reticle Profiles",
                Filter     = "JSON files (*.json)|*.json|All files (*.*)|*.*",
                FileName   = "ReticleOverlay_profiles_" + DateTime.Now.ToString("yyyy-MM-dd") + ".json",
                DefaultExt = ".json",
                InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Desktop)
            };
            if (dlg.ShowDialog(this) != true) return;
            try
            {
                if (File.Exists(ProfileStore.File))
                {
                    File.Copy(ProfileStore.File, dlg.FileName, overwrite: true);
                    AppDialog.Show("Profiles exported to:\n" + dlg.FileName,
                        "Export Complete", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    AppDialog.Show("No profiles file found to export.",
                        "Export", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }
            }
            catch (Exception ex)
            {
                AppDialog.Show("Export failed:\n" + ex.Message,
                    "Export Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void BtnImport_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Title      = "Import Reticle Profiles",
                Filter     = "JSON files (*.json)|*.json|All files (*.*)|*.*",
                DefaultExt = ".json",
                InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Desktop)
            };
            if (dlg.ShowDialog(this) != true) return;

            // L-4: hoisted out of the validation try-block — it used to be
            // discarded there and the code below re-read the file via a raw
            // File.Copy(overwrite:true) instead, which replaced the ENTIRE
            // profiles.json with the imported file's contents. That silently
            // deleted every profile not present in the imported file, despite
            // the dialog below promising "will merge these profiles into your
            // current list."
            System.Collections.Generic.List<ReticleProfile> parsed = null;
            try
            {
                string content = File.ReadAllText(dlg.FileName, System.Text.Encoding.UTF8);
                parsed = System.Text.Json.JsonSerializer.Deserialize(
                    content, ProfileStoreContext.Default.ListReticleProfile);
                if (parsed == null || parsed.Count == 0)
                {
                    AppDialog.Show("The selected file contains no valid profiles.",
                        "Import Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
            }
            catch (Exception ex)
            {
                AppDialog.Show("The selected file does not appear to be a valid profiles export:\n" + ex.Message,
                    "Import Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var result = AppDialog.Show(
                "Importing will merge these profiles into your current list.\n\n" +
                "A profile with the same name as one you already have will be overwritten by the imported version; " +
                "profiles with new names are added alongside your existing ones.\n\nContinue?",
                "Import Profiles", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result != MessageBoxResult.Yes) return;

            try
            {
                if (File.Exists(ProfileStore.File))
                    File.Copy(ProfileStore.File, ProfileStore.File + ".bak", overwrite: true);

                // Actual merge by profile Name: same-name entries are replaced
                // in place (preserving their position in the list); everything
                // else — both pre-existing profiles and newly-named imports —
                // is kept.
                var existing = ProfileStore.Load();
                int updated = 0, added = 0, sanitized = 0;
                foreach (var p in parsed)
                {
                    // HIGH (2026-09-14 review): a UNC CustomImagePath from an imported
                    // profile would otherwise reach File.Exists()/BitmapImage and trigger
                    // an outbound SMB/NTLM auth attempt against whatever host is named.
                    if (!string.IsNullOrEmpty(p.CustomImagePath) && !ImagePathValidator.IsSafeLocalPath(p.CustomImagePath))
                    {
                        p.CustomImagePath = "";
                        sanitized++;
                    }
                    int idx = existing.FindIndex(x =>
                        string.Equals(x.Name, p.Name, StringComparison.OrdinalIgnoreCase));
                    if (idx >= 0) { existing[idx] = p; updated++; }
                    else { existing.Add(p); added++; }
                }
                ProfileStore.Save(existing);
                if (sanitized > 0)
                    CrashLog.Info($"Import: cleared {sanitized} unsafe (non-local) CustomImagePath value(s).");

                ProfilesImported?.Invoke(this, EventArgs.Empty);
                AppDialog.Show(
                    $"Profiles imported: {added} added, {updated} updated." +
                    (sanitized > 0 ? $"\n\n{sanitized} custom image path(s) not on a local drive were cleared." : ""),
                    "Import Complete", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                AppDialog.Show("Import failed:\n" + ex.Message,
                    "Import Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e) => Close();

        private void BtnCopyPath_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Clipboard.SetText(SettingsPathLabel.Text);
                BtnCopyPathText.Text = "✓ Copied!";
                _copyTimer.Stop();
                _copyTimer.Start();
            }
            catch (Exception ex)
            {
                AppDialog.Show("Could not copy: " + ex.Message, "Copy Failed",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void BtnWebsite_Click(object sender, RoutedEventArgs e)
            => OpenUrl("https://oldgamerssoftware.com/");

        private void BtnGitHub_Click(object sender, RoutedEventArgs e)
            => OpenUrl("https://github.com/lordmaus/Reticle-Overlay");

        private void BtnReadme_Click(object sender, RoutedEventArgs e)
        {
            string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "README.md");
            if (File.Exists(path)) OpenUrl(path);
            else AppDialog.Show("README.md not found next to the exe.", "Help",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void BtnVenmo_Click(object sender, RoutedEventArgs e)
            => OpenUrl("https://account.venmo.com/u/Matthew-Pittman-80");

        private void BtnCashApp_Click(object sender, RoutedEventArgs e)
            => OpenUrl("https://cash.app/$MatthewPittman80");

        private static void OpenUrl(string url)
        {
            try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
            catch (Exception ex) { CrashLog.Write("OpenUrl", ex); }
        }

        private void BtnCrashLog_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Directory.CreateDirectory(CrashLog.Dir);
                var psi = new ProcessStartInfo("explorer.exe") { UseShellExecute = false };
                psi.ArgumentList.Add(CrashLog.Dir);
                Process.Start(psi);
            }
            catch (Exception ex) { CrashLog.Write("Open crash log folder", ex); }
        }

        // ── Window size persistence ───────────────────────────────────────────
        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            var (w, h) = WindowSizeCache.Get("ROAbout", 540, 640);
            Width = w; Height = h;
        }

        protected override void OnClosed(EventArgs e)
        {
            _copyTimer?.Stop();
            WindowSizeCache.Set("ROAbout", Width, Height);
            base.OnClosed(e);
        }

        // ── Resize grip — canonical accessible pattern (see FileCopy's SettingsDialog.xaml.cs) ──
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