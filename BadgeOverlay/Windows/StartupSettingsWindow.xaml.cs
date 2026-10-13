using System;
using System.Windows;
using System.Windows.Media;

namespace BadgeOverlay
{
    public partial class StartupSettingsWindow : Window
    {
        private readonly AppSettings _live;
        private readonly MainWindow  _main;
        private bool _loading = false;

        public StartupSettingsWindow(AppSettings liveSettings, MainWindow main)
        {
            InitializeComponent();
            CursorPreserver.Attach(this);
            _live = liveSettings;
            _main = main;
            Loaded += OnLoaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            TrySetIcon();
            LoadFromDefaults();
        }

        private void TrySetIcon()
        {
            try
            {
                string p = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "app.ico");
                if (System.IO.File.Exists(p))
                    Icon = new System.Windows.Media.Imaging.BitmapImage(new Uri(p, UriKind.Absolute));
            }
            catch { }
        }

        private void LoadFromDefaults()
        {
            _loading = true;
            try
            {
                AppSettings d = LoadSavedDefaults() ?? _live;
                ApplyToControls(d);
                ChkStartWithWindows.IsChecked = GetStartupEnabled();
                ChkStartMinimised.IsChecked   = d.StartMinimised;
                ChkOverlayOnStartup.IsChecked = d.OverlayEnabled;

                bool portable = AppSettings.IsPortableMode;
                ChkPortableMode.IsChecked = portable;
                if (LblPortableModeHint != null)
                {
                    LblPortableModeHint.Text = portable
                        ? "Currently storing settings.json next to BadgeOverlay.exe."
                        : "Currently storing settings.json under %APPDATA%\\BadgeOverlay\\.";
                }
            }
            finally { _loading = false; }
        }

        private void ApplyToControls(AppSettings d)
        {
            ChkDefaultShowBadge.IsChecked    = d.ShowBadge;
            ChkDefaultSilentMode.IsChecked   = d.BadgeHidden;
            ChkDefaultUIADetection.IsChecked = d.EnableUIADetection;
            SldDefaultBadgeOffset.Value      = d.BadgeOffsetX > 0 ? d.BadgeOffsetX : 20;
            if (LblDefaultBadgeOffset != null)
                LblDefaultBadgeOffset.Text = $"{(int)SldDefaultBadgeOffset.Value} px";
            ChkDefaultInfoOverlay.IsChecked  = d.ShowInfoOverlay;
        }

        // ── Startup registry ──────────────────────────────────────────────────
        private const string StartupRegKey  = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string StartupRegName = "BadgeOverlay";

        private void ChkStartWithWindows_Changed(object s, RoutedEventArgs e)
        {
            if (_loading) return;
            SetStartupEnabled(ChkStartWithWindows.IsChecked == true);
        }

        private static bool GetStartupEnabled()
        {
            try
            {
                using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(StartupRegKey, false);
                return key?.GetValue(StartupRegName) != null;
            }
            catch { return false; }
        }

        private static void SetStartupEnabled(bool enable)
        {
            try
            {
                using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(StartupRegKey, true);
                if (key == null) return;
                if (enable)
                    key.SetValue(StartupRegName, BuildStartupValue());
                else
                    key.DeleteValue(StartupRegName, throwOnMissingValue: false);
            }
            catch { }
        }

        /// <summary>
        /// Idempotent self-heal. If Start-with-Windows is enabled and the
        /// Run-key value points to a path that no longer matches the
        /// currently-running executable (user moved / reinstalled the app),
        /// rewrite it to the live path. Does nothing when the key isn't set.
        /// Safe to call every launch.
        /// </summary>
        public static void RefreshStartupPathIfStale()
        {
            try
            {
                using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(StartupRegKey, true);
                if (key == null) return;
                object existing = key.GetValue(StartupRegName);
                if (existing == null) return;   // not enabled — nothing to fix

                string current = BuildStartupValue();
                string stored  = existing as string ?? "";
                if (!string.Equals(stored, current, System.StringComparison.OrdinalIgnoreCase))
                {
                    key.SetValue(StartupRegName, current);
                    // CrashLog.Write() now redacts %USERPROFILE% centrally,
                    // but redact here too — the message text becomes the
                    // "context" argument, which is safe either way.
                    CrashLog.Write(
                        "StartupRegKey: path updated after app move. " +
                        $"was='{CrashLog.Redact(stored)}' now='{CrashLog.Redact(current)}'");
                }
            }
            catch (System.Exception ex) { CrashLog.Write("RefreshStartupPathIfStale", ex); }
        }

        private static string BuildStartupValue()
        {
            // Single-file publish: Assembly.Location returns empty for
            // assemblies bundled into the EXE. Use Process.MainModule
            // first (always populated, including in single-file), and
            // fall back to AppContext.BaseDirectory + "BadgeOverlay.exe"
            // if the process module read denies (very rare).
            string path = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;
            if (string.IsNullOrEmpty(path))
                path = System.IO.Path.Combine(System.AppContext.BaseDirectory, "BadgeOverlay.exe");
            return $"\"{path}\"";
        }

        // ── Save defaults ─────────────────────────────────────────────────────
        private void BtnSaveCurrentAsDefaults_Click(object s, RoutedEventArgs e)
        {
            try
            {
                _live.StartMinimised = ChkStartMinimised.IsChecked == true;
                _live.OverlayEnabled = ChkOverlayOnStartup.IsChecked == true;

                _live.ShowBadge          = ChkDefaultShowBadge.IsChecked == true;
                _live.BadgeHidden        = ChkDefaultSilentMode.IsChecked == true;
                _live.EnableUIADetection = ChkDefaultUIADetection.IsChecked == true;
                _live.BadgeOffsetX       = SldDefaultBadgeOffset.Value;
                _live.BadgeOffsetY       = SldDefaultBadgeOffset.Value;
                _live.ShowInfoOverlay    = ChkDefaultInfoOverlay.IsChecked == true;

                bool ok = _live.SaveAsDefaults();
                if (ok)
                {
                    _live.PushToDetector();
                    _main?.PushSettingsPublic();
                    SetStatus("Saved — restarts will load these settings.", "#69F0AE");
                    _loading = true;
                    try { ApplyToControls(_live); }
                    finally { _loading = false; }
                }
                else
                {
                    SetStatus("Save failed — check %APPDATA%\\BadgeOverlay\\", "#FF5555");
                }
            }
            catch (Exception ex)
            {
                AppDialog.Show($"Failed to save defaults:\n{ex.Message}",
                    "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // ── Factory reset ─────────────────────────────────────────────────────
        private void BtnFactoryReset_Click(object s, RoutedEventArgs e)
        {
            var r = AppDialog.Show(
                "Restore factory defaults?\n\nThis will overwrite settings.json with factory values.",
                "Factory Reset", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (r != MessageBoxResult.Yes) return;

            try
            {
                var factory = new AppSettings();
                bool ok = factory.Save();
                _main?.ResetToFactoryPublic();
                _loading = true;
                try { ApplyToControls(factory); }
                finally { _loading = false; }
                SetStatus(ok ? "Factory defaults restored." : "Reset failed.",
                          ok ? "#A6E3A1" : "#FF5555");
            }
            catch (Exception ex)
            {
                AppDialog.Show($"Reset failed:\n{ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // ── Export / Import ───────────────────────────────────────────────────
        private void BtnExport_Click(object s, RoutedEventArgs e) => _main?.ExportSettingsPublic();
        private void BtnImport_Click(object s, RoutedEventArgs e) => _main?.ImportSettingsPublic();

        private static AppSettings LoadSavedDefaults()
        {
            var loaded = AppSettings.Load();
            return loaded.LoadedFromDefaults ? null : loaded;
        }

        private void SetStatus(string msg, string hexColor)
        {
            LblStatus.Text       = msg;
            LblStatus.Foreground = new SolidColorBrush(
                (Color)ColorConverter.ConvertFromString(hexColor));
        }

        private void SldDefaultBadgeOffset_Changed(object s, RoutedPropertyChangedEventArgs<double> e)
        {
            if (LblDefaultBadgeOffset != null) LblDefaultBadgeOffset.Text = $"{(int)e.NewValue} px";
        }

        private void BtnClose_Click(object s, RoutedEventArgs e) => Close();

        /// <summary>
        /// Toggles the portable-mode marker file. Settings already
        /// loaded for the current session continue to use the original
        /// path; user is told to restart so the new path picks up.
        /// On migration, we also offer to copy the existing settings
        /// file to the new location so they don't lose configuration.
        /// </summary>
        private void ChkPortableMode_Changed(object s, RoutedEventArgs e)
        {
            if (_loading) return;
            bool wantPortable = ChkPortableMode.IsChecked == true;
            bool currentlyPortable = AppSettings.IsPortableMode;
            if (wantPortable == currentlyPortable) return;

            // Apply the flag toggle.
            bool ok = AppSettings.SetPortableMode(wantPortable);
            if (!ok)
            {
                // Couldn't write the marker (likely an EXE-folder
                // permission issue). Revert UI and tell the user.
                _loading = true;
                try { ChkPortableMode.IsChecked = currentlyPortable; }
                finally { _loading = false; }
                SetStatus(
                    "Couldn't toggle portable mode — write access to the program folder was denied.",
                    "#F38BA8");
                return;
            }

            if (LblPortableModeHint != null)
            {
                LblPortableModeHint.Text = wantPortable
                    ? "Marker created. Restart Badge Overlay so settings load from the program folder."
                    : "Marker removed. Restart Badge Overlay so settings load from %APPDATA%.";
            }
            SetStatus("Portable mode setting saved. Restart required.", "#A6E3A1");
        }

        // ── Window size persistence ───────────────────────────────────────────
        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            var (w, h) = WindowSizeCache.Get("StartupSettingsWindow", 540, 600);
            Width = w; Height = h;
        }

        protected override void OnClosed(EventArgs e)
        {
            WindowSizeCache.Set("StartupSettingsWindow", Width, Height);
            base.OnClosed(e);
        }
    }
}
