using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;

namespace NoteStation
{
    public partial class AppSettingsDialog : Window
    {
        private static readonly string[] _mouseChoices = new[]
        { "None", "XButton1 (Back)", "XButton2 (Forward)" };

        private static readonly double[] _fontSizes = new double[]
        { 9, 10, 11, 12, 13, 14, 16, 18, 20, 24, 28, 32, 36, 40, 48 };

        private static readonly double[] _accentWidths = new double[]
        { 0, 1, 2, 3, 4, 5, 6, 8 };

        // Curated low-vision / dyslexia-friendly fonts shown first
        private static readonly string[] _preferredFonts = new[]
        {
            "Segoe UI", "Atkinson Hyperlegible", "OpenDyslexic", "Verdana",
            "Arial", "Tahoma", "Calibri", "Comic Sans MS", "Consolas"
        };

        public bool Saved { get; private set; }

        public AppSettingsDialog()
        {
            InitializeComponent();
            WindowSizeMemory.Attach(this, "AppSettingsDialog");

            var p = AppPrefs.Instance;

            // Clipboard
            ChkAutoCapture.IsChecked = p.ClipboardAutoCapture;
            ChkDedupe.IsChecked      = p.ClipboardDedupeConsecutive;
            ChkEncryptClips.IsChecked = p.EncryptClipboardAtRest;
            TxtMaxHistory.Text       = p.ClipboardMaxHistory.ToString();

            // Mouse
            foreach (var c in _mouseChoices)
            {
                CboPrevClip.Items.Add(c);
                CboNextClip.Items.Add(c);
            }
            CboPrevClip.SelectedIndex = Math.Min(p.MousePreviousClip, _mouseChoices.Length - 1);
            CboNextClip.SelectedIndex = Math.Min(p.MouseNextClip,     _mouseChoices.Length - 1);
            ChkMouseScroll.IsChecked = p.MouseScrollCycle;
            ChkScrollMod.IsChecked   = p.MouseScrollRequiresModifier;

            // Tasks
            ChkShowCompleted.IsChecked = p.ShowCompletedTasks;

            // Timer Studio
            TxtTimerPath.Text = p.TimerStudioTimersPath;

            // Display — font size ComboBoxes
            foreach (var fs in _fontSizes)
            {
                CboClipFont.Items.Add(fs);
                CboNoteFont.Items.Add(fs);
                CboTaskFont.Items.Add(fs);
                CboFolderFont.Items.Add(fs);
            }
            SelectFontSize(CboClipFont,   p.ClipFontSize);
            SelectFontSize(CboNoteFont,   p.NoteFontSize);
            SelectFontSize(CboTaskFont,   p.TaskFontSize);
            SelectFontSize(CboFolderFont, p.FolderFontSize);

            // Accent bar width
            foreach (var w in _accentWidths) CboAccentBarWidth.Items.Add(w);
            SelectFontSize(CboAccentBarWidth, p.FolderAccentBarWidth);

            // Recently Deleted retention
            CboDeletedRetentionDays.Items.Clear();
            var _retentionOptions = new[] { (0, "Keep forever"), (1, "1 day"), (7, "7 days"), (14, "14 days"), (30, "30 days (default)"), (60, "60 days"), (90, "90 days") };
            foreach (var (days, label) in _retentionOptions)
                CboDeletedRetentionDays.Items.Add(new System.Windows.Controls.ComboBoxItem { Content = label, Tag = days });
            // Select current value
            {
                int cur = p.DeletedRetentionDays;
                bool found = false;
                foreach (System.Windows.Controls.ComboBoxItem item in CboDeletedRetentionDays.Items)
                    if ((int)item.Tag == cur) { CboDeletedRetentionDays.SelectedItem = item; found = true; break; }
                if (!found && CboDeletedRetentionDays.Items.Count > 0)
                    CboDeletedRetentionDays.SelectedIndex = 4; // 30 days
            }

            // Undo-toast lifetime (WCAG 2.2.3) — 0 = until dismissed
            CboUndoToastSeconds.Items.Clear();
            foreach (var (secs, label) in new[] { (5, "5 seconds"), (15, "15 seconds (default)"), (30, "30 seconds"), (60, "1 minute"), (0, "Until dismissed") })
                CboUndoToastSeconds.Items.Add(new System.Windows.Controls.ComboBoxItem { Content = label, Tag = secs });
            foreach (System.Windows.Controls.ComboBoxItem item in CboUndoToastSeconds.Items)
                if ((int)item.Tag == p.UndoToastSeconds) { CboUndoToastSeconds.SelectedItem = item; break; }
            if (CboUndoToastSeconds.SelectedItem == null) CboUndoToastSeconds.SelectedIndex = 1;

            // App-wide font family — preferred (low-vision) first, then full system list (async to avoid UI block)
            Dispatcher.InvokeAsync(async () =>
            {
                try { await PopulateFontFamilyComboAsync(CboAppFontFamily, p.AppFontFamily); }
                catch (Exception ex) { AppLog.Error("Font list", ex); }
            });

            // Startup/Tray
            ChkStartWithWindows.IsChecked = p.StartWithWindows;
            ChkStartMinimized.IsChecked   = p.StartMinimized;
            ChkMinToTray.IsChecked      = p.MinimizeToTray;
            CmbCloseAction.SelectedIndex = p.CloseButtonAction switch
            {
                "Taskbar" => 1,
                "Tray"    => 2,
                _         => 0,
            };
            ChkClipOverlayStartup.IsChecked = p.ShowClipboardOverlayAtStartup;
            ChkTaskOverlayStartup.IsChecked = p.ShowTaskOverlayAtStartup;
            ChkNotesSidebarStartup.IsChecked = p.ShowNotesSidebarAtStartup;
            CmbNotesSidebarEdge.SelectedIndex = string.Equals(p.NotesSidebarEdge, "Left", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
            CmbNotesSidebarPosition.SelectedIndex = p.NotesSidebarPosition switch
            {
                "Middle" => 1,
                "Bottom" => 2,
                _        => 0,
            };

            // Security — master passphrase status
            MasterPassStatus.Text = string.IsNullOrEmpty(p.MasterPassphraseHash)
                ? "No master passphrase set." : "✓ Master passphrase is set.";
            MasterPassStatus.Foreground = string.IsNullOrEmpty(p.MasterPassphraseHash)
                ? System.Windows.Media.Brushes.Gray : System.Windows.Media.Brushes.Green;
        }

        private static void SelectFontSize(ComboBox cbo, double value)
        {
            // Select exact match; if not found pick nearest
            int bestIdx = 0;
            double bestDiff = double.MaxValue;
            for (int i = 0; i < cbo.Items.Count; i++)
            {
                double diff = Math.Abs((double)cbo.Items[i] - value);
                if (diff < bestDiff) { bestDiff = diff; bestIdx = i; }
            }
            cbo.SelectedIndex = bestIdx;
        }

        private void BtnBrowseTimerPath_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Title  = "Locate Timer Studio timers.json",
                Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*",
                FileName = "timers.json",
            };
            if (dlg.ShowDialog(this) == true)
                TxtTimerPath.Text = dlg.FileName;
        }

        private void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            var p = AppPrefs.Instance;

            p.ClipboardAutoCapture        = ChkAutoCapture.IsChecked == true;
            p.ClipboardDedupeConsecutive  = ChkDedupe.IsChecked == true;
            p.EncryptClipboardAtRest      = ChkEncryptClips.IsChecked == true;
            if (int.TryParse(TxtMaxHistory.Text, out int max) && max >= 10 && max <= 500)
                p.ClipboardMaxHistory = max;

            p.MousePreviousClip           = CboPrevClip.SelectedIndex;
            p.MouseNextClip               = CboNextClip.SelectedIndex;
            p.MouseScrollCycle            = ChkMouseScroll.IsChecked == true;
            p.MouseScrollRequiresModifier = ChkScrollMod.IsChecked == true;

            p.ShowCompletedTasks          = ChkShowCompleted.IsChecked == true;
            p.TimerStudioTimersPath       = TxtTimerPath.Text.Trim();
            bool startWithWindows = ChkStartWithWindows.IsChecked == true;
            if (p.StartWithWindows != startWithWindows)
            {
                p.StartWithWindows = startWithWindows;
                SetStartup(startWithWindows);
            }
            p.StartMinimized              = ChkStartMinimized.IsChecked == true;
            p.MinimizeToTray              = ChkMinToTray.IsChecked == true;
            p.CloseButtonAction = CmbCloseAction.SelectedIndex switch
            {
                1 => "Taskbar",
                2 => "Tray",
                _ => "Exit",
            };
            p.CloseToTray = p.CloseButtonAction == "Tray"; // keep legacy flag consistent
            p.ShowClipboardOverlayAtStartup = ChkClipOverlayStartup.IsChecked == true;
            p.ShowTaskOverlayAtStartup      = ChkTaskOverlayStartup.IsChecked == true;
            p.ShowNotesSidebarAtStartup     = ChkNotesSidebarStartup.IsChecked == true;
            p.NotesSidebarEdge              = CmbNotesSidebarEdge.SelectedIndex == 1 ? "Left" : "Right";
            p.NotesSidebarPosition          = CmbNotesSidebarPosition.SelectedIndex switch
            {
                1 => "Middle",
                2 => "Bottom",
                _ => "Top",
            };

            if (CboClipFont.SelectedItem      is double clf) p.ClipFontSize        = clf;
            if (CboNoteFont.SelectedItem      is double nof) p.NoteFontSize        = nof;
            if (CboTaskFont.SelectedItem      is double taf) p.TaskFontSize        = taf;
            if (CboFolderFont.SelectedItem    is double fof) p.FolderFontSize      = fof;
            if (CboAccentBarWidth.SelectedItem is double abw) p.FolderAccentBarWidth = abw;
            if (CboDeletedRetentionDays.SelectedItem is System.Windows.Controls.ComboBoxItem retItem)
                p.DeletedRetentionDays = (int)retItem.Tag;
            if (CboUndoToastSeconds.SelectedItem is System.Windows.Controls.ComboBoxItem undoItem)
                p.UndoToastSeconds = (int)undoItem.Tag;
            if (CboAppFontFamily.SelectedItem  is string fam) p.AppFontFamily        = fam;
            App.SetContentFontSizes(p.ClipFontSize, p.NoteFontSize, p.TaskFontSize, p.FolderFontSize);
            App.SetFolderAccentWidth(p.FolderAccentBarWidth);
            App.SetAppFontFamily(p.AppFontFamily);

            p.Save();
            Saved = true;
            Close();
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e) => Close();

        // Left nav rail (Phase 5 fix) — jumps the content ScrollViewer to a
        // section header via BringIntoView() rather than a full tab/Visibility
        // rewrite, so every existing control keeps its x:Name and no other
        // handler in this file needed to change.
        private void NavSection_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string name &&
                FindName(name) is FrameworkElement target)
            {
                target.BringIntoView();
            }
        }

        // Populate font family combo: curated preferred set on top, then system fonts (async to avoid blocking UI).
        private static async Task PopulateFontFamilyComboAsync(ComboBox cbo, string current)
        {
            var systemFonts = await Task.Run(() =>
                Fonts.SystemFontFamilies.Select(ff => ff.Source).OrderBy(s => s).ToList());

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // Preferred first (whether installed or not — picks fall back to Segoe UI)
            foreach (var f in _preferredFonts)
            {
                if (seen.Add(f)) cbo.Items.Add(f);
            }
            cbo.Items.Add(new Separator());
            foreach (var f in systemFonts)
            {
                if (seen.Add(f)) cbo.Items.Add(f);
            }

            // Select current
            for (int i = 0; i < cbo.Items.Count; i++)
            {
                if (cbo.Items[i] is string s && string.Equals(s, current, StringComparison.OrdinalIgnoreCase))
                {
                    cbo.SelectedIndex = i;
                    return;
                }
            }
            cbo.SelectedIndex = 0;
        }

        private void BtnSetMaster_Click(object sender, RoutedEventArgs e)
        {
            var pass = MasterPassBox1.Password;
            if (string.IsNullOrWhiteSpace(pass))
            {
                MasterPassStatus.Text = "Enter a passphrase first.";
                MasterPassStatus.Foreground = System.Windows.Media.Brushes.Red;
                return;
            }
            if (pass.Length < 6)
            {
                MasterPassStatus.Text = "Passphrase must be at least 6 characters.";
                MasterPassStatus.Foreground = System.Windows.Media.Brushes.Red;
                return;
            }

            // 2026-09-07: salted PBKDF2 via PinHasher, was unsalted single-round SHA-256.
            AppPrefs.Instance.MasterPassphraseHash = PinHasher.CreateHash(pass);
            AppPrefs.Instance.Save();
            MasterPassBox1.Clear();
            MasterPassStatus.Text = "✓ Master passphrase set.";
            MasterPassStatus.Foreground = System.Windows.Media.Brushes.Green;
        }

        private void BtnExportBackup_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                Title      = "Export NoteStation Backup",
                Filter     = "NoteStation backup (*.nsbak)|*.nsbak",
                DefaultExt = ".nsbak",
                FileName   = $"NoteStation-{DateTime.Now:yyyy-MM-dd}",
            };
            if (dlg.ShowDialog(this) != true) return;
            try
            {
                DataStore.ExportBackup(dlg.FileName);
                AppDialog.Show($"Backup saved to:\n{dlg.FileName}",
                    "NoteStation — Export Complete", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                AppDialog.Show($"Export failed:\n{ex.Message}", "NoteStation — Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnImportBackup_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Title  = "Import NoteStation Backup",
                Filter = "NoteStation backup (*.nsbak)|*.nsbak|All files (*.*)|*.*",
            };
            if (dlg.ShowDialog(this) != true) return;

            var confirm = AppDialog.Show(
                "Importing will replace your current clipboard history, notes, and tasks with those from the backup.\n\nThis cannot be undone. Continue?",
                "NoteStation — Import Backup",
                MessageBoxButton.OKCancel,
                MessageBoxImage.Warning);
            if (confirm != MessageBoxResult.OK) return;

            try
            {
                string summary = DataStore.ImportBackup(dlg.FileName);
                AppDialog.Show($"Import complete: {summary}\n\nNoteStation will restart to load the restored data.",
                    "NoteStation — Import Complete", MessageBoxButton.OK, MessageBoxImage.Information);
                App.Restart();
            }
            catch (Exception ex)
            {
                AppDialog.Show($"Import failed:\n{ex.Message}", "NoteStation — Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnImportClipClip_Click(object sender, RoutedEventArgs e)
        {
            using var dlg = new System.Windows.Forms.FolderBrowserDialog
            {
                Description            = "Select your ClipClip Saved Clips folder",
                SelectedPath           = @"D:\Data\Save\ClipClip\Saved Clips",
                ShowNewFolderButton    = false,
                UseDescriptionForTitle = true,
            };
            if (dlg.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;

            try
            {
                // Determine next IDs from all existing data so we never collide
                var existingClips = DataStore.LoadClipboard();
                int nextClipId = existingClips.Count > 0 ? existingClips.Max(c => c.Id) + 1 : 1;

                var prefs = AppPrefs.Instance;
                int nextFolderId = 1;
                foreach (var f in prefs.ClipboardFolders
                                       .Concat(prefs.NoteFolders)
                                       .Concat(prefs.TaskFolders))
                    if (f.Id >= nextFolderId) nextFolderId = f.Id + 1;

                var result = ClipClipImporter.Import(dlg.SelectedPath, nextClipId, nextFolderId);

                if (result.NewClips.Count == 0 && result.NewFolders.Count == 0)
                {
                    AppDialog.Show(
                        "No .html/.rtf/.txt clip files were found in the selected folder.\n\n" +
                        "Make sure you selected the 'Saved Clips' folder inside your ClipClip data directory.",
                        "NoteStation — Nothing Imported",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                // Merge folders into prefs and persist
                prefs.ClipboardFolders.AddRange(result.NewFolders);
                prefs.Save();

                // Write each imported clip as its own file
                foreach (var c in result.NewClips)
                    DataStore.SaveClip(c);

                AppDialog.Show(
                    result.Summary + "\n\nNoteStation will restart to load the imported clips.",
                    "NoteStation — Import Complete",
                    MessageBoxButton.OK, MessageBoxImage.Information);

                App.Restart();
            }
            catch (Exception ex)
            {
                AppDialog.Show($"Import failed:\n{ex.Message}", "NoteStation — Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnClearAllData_Click(object sender, RoutedEventArgs e)
        {
            var res = AppDialog.Show(
                "This will permanently delete all clipboard history, notes, and tasks stored by NoteStation.\n\nThis cannot be undone.\n\nContinue?",
                "NoteStation — Clear All Data",
                MessageBoxButton.OKCancel,
                MessageBoxImage.Warning);
            if (res != MessageBoxResult.OK) return;

            DataStore.ClearAllData();
            AppDialog.Show(
                "All data cleared. NoteStation will now restart.",
                "NoteStation — Data Cleared",
                MessageBoxButton.OK, MessageBoxImage.Information);
            App.Restart();
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
    }
}
