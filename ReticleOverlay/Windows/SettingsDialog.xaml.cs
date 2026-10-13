using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;

namespace ReticleOverlay
{
    public partial class SettingsDialog : Window
    {
        private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string AppKey = "ReticleOverlay";

        private readonly AppPrefs _prefs;
        private bool _loading = false;
        private DispatcherTimer _savedTimer;

        public event EventHandler<bool> CaptureHideChanged;
        public event EventHandler       ResetApplied;

        public SettingsDialog(AppPrefs prefs)
        {
            _prefs = prefs;
            _loading = true;
            InitializeComponent();
            _loading = false;

            _savedTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            _savedTimer.Tick += (s, e) => { LblSaved.Text = ""; _savedTimer.Stop(); };

            Loaded += OnLoaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            _loading = true;
            ChkMinToTray.IsChecked       = _prefs.MinimizeToTrayOnClose;
            ChkHideFromCapture.IsChecked = _prefs.HideFromCapture;
            ChkAutoSwitch.IsChecked      = _prefs.AutoSwitchProfiles;
            ChkStartup.IsChecked         = IsStartupEnabled();

            PopulateFontFamilies();
            SelectFontFamily(_prefs.AppFontFamily);

            SldHudFontSize.Value = Math.Clamp(_prefs.HudFontSize, 8, 72);
            LblHudFontSize.Text  = ((int)SldHudFontSize.Value) + " px";

            _loading = false;
        }

        private void PopulateFontFamilies()
        {
            string[] curated =
            {
                "Segoe UI", "Atkinson Hyperlegible", "OpenDyslexic", "Verdana",
                "Arial", "Tahoma", "Calibri", "Comic Sans MS", "Consolas"
            };

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            CboAppFontFamily.Items.Clear();
            foreach (var f in curated)
            {
                if (seen.Add(f)) CboAppFontFamily.Items.Add(f);
            }
            foreach (var f in Fonts.SystemFontFamilies
                                   .Select(ff => ff.Source)
                                   .OrderBy(s => s, StringComparer.OrdinalIgnoreCase))
            {
                if (seen.Add(f)) CboAppFontFamily.Items.Add(f);
            }
        }

        private void SelectFontFamily(string family)
        {
            if (string.IsNullOrWhiteSpace(family)) family = "Segoe UI";
            int idx = -1;
            for (int i = 0; i < CboAppFontFamily.Items.Count; i++)
            {
                if (string.Equals(CboAppFontFamily.Items[i] as string, family, StringComparison.OrdinalIgnoreCase))
                { idx = i; break; }
            }
            if (idx < 0)
            {
                CboAppFontFamily.Items.Insert(0, family);
                idx = 0;
            }
            CboAppFontFamily.SelectedIndex = idx;
        }

        private void CboAppFontFamily_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (_loading) return;
            string family = CboAppFontFamily.SelectedItem as string ?? "Segoe UI";
            _prefs.AppFontFamily = family;
            App.SetAppFontFamily(family);
            ShowSaved("Saved OK");
        }

        private void SldHudFontSize_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (LblHudFontSize != null)
                LblHudFontSize.Text = ((int)Math.Round(e.NewValue)) + " px";
            if (_loading) return;
            double size = Math.Clamp(e.NewValue, 8, 72);
            _prefs.HudFontSize = size;
            App.SetHudFontSize(size);
            ShowSaved("Saved OK");
        }

        private void ChkMinToTray_Changed(object sender, RoutedEventArgs e)
        {
            if (_loading) return;
            _prefs.MinimizeToTrayOnClose = ChkMinToTray.IsChecked == true;
            ShowSaved("Saved OK");
        }

        private void ChkHideFromCapture_Changed(object sender, RoutedEventArgs e)
        {
            if (_loading) return;
            _prefs.HideFromCapture = ChkHideFromCapture.IsChecked == true;
            CaptureHideChanged?.Invoke(this, _prefs.HideFromCapture);
            ShowSaved("Saved OK");
        }

        private void ChkAutoSwitch_Changed(object sender, RoutedEventArgs e)
        {
            if (_loading) return;
            _prefs.AutoSwitchProfiles = ChkAutoSwitch.IsChecked == true;
            ShowSaved("Saved OK");
        }

        private void ChkStartup_Changed(object sender, RoutedEventArgs e)
        {
            if (_loading) return;
            SetStartup(ChkStartup.IsChecked == true);
            ShowSaved("Saved OK");
        }

        private static bool IsStartupEnabled()
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(RunKey, false))
                    return key?.GetValue(AppKey) != null;
            }
            catch { return false; }
        }

        private static void SetStartup(bool enable)
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(RunKey, true))
                {
                    if (key == null) return;
                    if (enable)
                        key.SetValue(AppKey, "\"" + (Environment.ProcessPath ?? Assembly.GetExecutingAssembly().Location) + "\"");
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

        private void BtnClose_Click(object sender, RoutedEventArgs e) => Close();

        // Polish #6: reset reticle settings to factory defaults
        private void BtnResetDefaults_Click(object sender, RoutedEventArgs e)
        {
            var result = MessageBox.Show(
                "This will reset all reticle settings (type, size, color, opacity, offset) to factory defaults.\n\nSaved profiles are NOT affected.\n\nContinue?",
                "Reset to Defaults",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);
            if (result != MessageBoxResult.Yes) return;

            _prefs.ReticleType      = "Crosshair";
            _prefs.CustomImagePath  = "";
            _prefs.ReticleSize      = 40.0;
            _prefs.ReticleThickness = 2.0;
            _prefs.CentreGap        = 8.0;
            _prefs.Rotation         = 0.0;
            _prefs.ReticleColor     = "#00FF00";
            _prefs.ReticleColor2    = "#00FF00";
            _prefs.OutlineColor     = "#000000";
            _prefs.UseColor2        = false;
            _prefs.ShowOutline      = true;
            _prefs.Opacity          = 1.0;
            _prefs.OffsetX          = 0.0;
            _prefs.OffsetY          = 0.0;
            _prefs.Save();

            ResetApplied?.Invoke(this, EventArgs.Empty);
            ShowSaved("Reset to defaults ✓");
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            _savedTimer?.Stop();
            _prefs.Save();
        }

        private void ShowSaved(string msg)
        {
            LblSaved.Text = msg;
            _savedTimer.Stop();
            _savedTimer.Start();
        }

        // ── Window size persistence ───────────────────────────────────────────
        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            var (w, h) = WindowSizeCache.Get("ROSettingsDialog", 400, 500);
            SizeToContent = System.Windows.SizeToContent.Manual;
            Width = w; Height = h;
        }

        protected override void OnClosed(EventArgs e)
        {
            WindowSizeCache.Set("ROSettingsDialog", Width, Height);
            base.OnClosed(e);
        }
    }
}
