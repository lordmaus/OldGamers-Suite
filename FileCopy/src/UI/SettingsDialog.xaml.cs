using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

#nullable enable

namespace FileCopy
{
    public partial class SettingsDialog : Window
    {
        private bool _loading = true;

        // Curated dyslexia / low-vision friendly fonts shown first in the font picker.
        private static readonly string[] PreferredFonts = new[]
        {
            "Segoe UI",
            "Atkinson Hyperlegible",
            "OpenDyslexic",
            "Verdana",
            "Arial",
            "Tahoma",
            "Calibri",
            "Comic Sans MS",
            "Consolas"
        };

        public SettingsDialog()
        {
            InitializeComponent();
            PopulateFontFamilies();
            LoadPrefs();
            _loading = false;
        }

        private void PopulateFontFamilies()
        {
            // Build: curated preferred fonts first, then remaining system fonts (deduped, sorted).
            var systemNames = Fonts.SystemFontFamilies
                .Select(f => f.Source ?? string.Empty)
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var ordered = new List<string>();
            foreach (var name in PreferredFonts)
                ordered.Add(name); // include even if not installed — user may install later

            foreach (var name in systemNames.OrderBy(n => n, StringComparer.OrdinalIgnoreCase))
            {
                if (!ordered.Any(o => string.Equals(o, name, StringComparison.OrdinalIgnoreCase)))
                    ordered.Add(name);
            }

            CboAppFontFamily.ItemsSource = ordered;
        }

        private void LoadPrefs()
        {
            var p = AppPrefs.Instance;
            ChkVerifyCrc.IsChecked          = p.VerifyCrc;
            ChkPreserveTimestamps.IsChecked = p.PreserveTimestamps;
            ChkPreserveAttributes.IsChecked = p.PreserveAttributes;
            ChkIncludeSubdirs.IsChecked     = p.IncludeSubdirs;
            ChkResumePartial.IsChecked      = p.ResumePartial;
            ChkStartup.IsChecked            = StartupHelper.IsEnabled();

            // Display & Fonts
            string family = string.IsNullOrWhiteSpace(p.AppFontFamily) ? "Segoe UI" : p.AppFontFamily;
            CboAppFontFamily.SelectedItem = family;
            if (CboAppFontFamily.SelectedItem == null)
            {
                // saved family not in the curated list (rare): prepend it so it shows
                var items = (CboAppFontFamily.ItemsSource as IEnumerable<string>)?.ToList() ?? new List<string>();
                items.Insert(0, family);
                CboAppFontFamily.ItemsSource = items;
                CboAppFontFamily.SelectedItem = family;
            }

            double size = Math.Clamp(p.AppFontSize, 8, 72);
            SldAppFontSize.Value = size;
            LblAppFontSize.Text  = ((int)size) + " px";
        }

        private void Pref_Changed(object sender, RoutedEventArgs e)
        {
            if (_loading) return;
            var p = AppPrefs.Instance;
            p.VerifyCrc          = ChkVerifyCrc.IsChecked          == true;
            p.PreserveTimestamps = ChkPreserveTimestamps.IsChecked == true;
            p.PreserveAttributes = ChkPreserveAttributes.IsChecked == true;
            p.IncludeSubdirs     = ChkIncludeSubdirs.IsChecked     == true;
            p.ResumePartial      = ChkResumePartial.IsChecked      == true;
            ShowSaved();
        }

        private void ChkStartup_Changed(object sender, RoutedEventArgs e)
        {
            if (_loading) return;
            try
            {
                if (ChkStartup.IsChecked == true)
                    StartupHelper.Enable();
                else
                    StartupHelper.Disable();
                ShowSaved();
            }
            catch (Exception ex)
            {
                AppDialog.Show(this, $"Could not update startup entry:\n{ex.Message}",
                    "Settings", MessageBoxButton.OK, MessageBoxImage.Warning);
                ChkStartup.IsChecked = StartupHelper.IsEnabled();
            }
        }

        private void CboAppFontFamily_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_loading) return;
            string family = CboAppFontFamily.SelectedItem as string ?? "Segoe UI";
            AppPrefs.Instance.AppFontFamily = family;
            App.SetAppFontFamily(family);
            ShowSaved();
        }

        private void SldAppFontSize_ValueChanged(object sender, System.Windows.RoutedPropertyChangedEventArgs<double> e)
        {
            int size = (int)Math.Round(e.NewValue);
            if (LblAppFontSize != null) LblAppFontSize.Text = size + " px";
            if (_loading) return;
            AppPrefs.Instance.AppFontSize = size;
            App.SetAppFontSize(size);
            ShowSaved();
        }

        private void ShowSaved()
        {
            LblSaved.Text = "✓ Saved";
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e) => Close();

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e) { }

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
