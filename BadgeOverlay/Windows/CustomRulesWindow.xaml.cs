using System;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;

namespace BadgeOverlay
{
    public partial class CustomRulesWindow : Window
    {
        private readonly AppSettings _s;
        private readonly ObservableCollection<CustomRule> _rows
            = new ObservableCollection<CustomRule>();
        private CustomRule _editing;

        public CustomRulesWindow(AppSettings settings)
        {
            InitializeComponent();
            CursorPreserver.Attach(this);
            _s = settings;
            RuleList.ItemsSource = _rows;
            RuleList.DisplayMemberPath = "Summary";
            PopulateCategories();
            RefreshList();
            Loaded += (s, e) => TrySetIcon();
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

        private void PopulateCategories()
        {
            CmbMatchType.SelectedIndex = 0;
            CmbCategory.SelectedIndex  = 0;
        }

        private void RefreshList()
        {
            _rows.Clear();
            foreach (var r in _s.CustomRules)
                _rows.Add(r);
        }

        private void ShowEditor(CustomRule rule)
        {
            _editing = rule;
            CmbMatchType.SelectedIndex = rule.MatchType == CustomRule.MatchKind.Class ? 1 : 0;
            TxtMatch.Text              = rule.MatchValue;
            TxtLabel.Text              = rule.Label;
            // Select category
            foreach (ComboBoxItem item in CmbCategory.Items)
                if (item.Content?.ToString() == rule.Category)
                { CmbCategory.SelectedItem = item; break; }

            EditorPanel.Visibility = Visibility.Visible;
            BtnSave.IsEnabled      = true;
            BtnDelete.IsEnabled    = true;
        }

        private void HideEditor()
        {
            _editing               = null;
            EditorPanel.Visibility = Visibility.Collapsed;
            BtnSave.IsEnabled      = false;
            BtnDelete.IsEnabled    = false;
        }

        private void RuleList_SelectionChanged(object s, SelectionChangedEventArgs e)
        {
            var row = RuleList.SelectedItem as CustomRule;
            if (row != null) ShowEditor(row);
            else             HideEditor();
        }

        private void BtnAdd_Click(object s, RoutedEventArgs e)
        {
            var rule = new CustomRule
            {
                MatchType  = CustomRule.MatchKind.Text,
                MatchValue = "my button",
                Label      = "My Button",
                Category   = "Action",
            };
            _s.CustomRules.Add(rule);
            _s.Save();
            RefreshList();
            RuleList.SelectedIndex = _rows.Count - 1;
            TxtMatch.Focus();
            TxtMatch.SelectAll();
        }

        private void BtnSave_Click(object s, RoutedEventArgs e)
        {
            if (_editing == null) return;
            string tag = (CmbMatchType.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "text";
            _editing.MatchType = string.Equals(tag, "class", StringComparison.OrdinalIgnoreCase)
                ? CustomRule.MatchKind.Class
                : CustomRule.MatchKind.Text;
            _editing.MatchValue = TxtMatch.Text.Trim();
            _editing.Label      = TxtLabel.Text.Trim();
            _editing.Category   = (CmbCategory.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Control";
            _s.Save();
            ButtonDetector.ReloadCustomRules(_s.CustomRules);
            RefreshList();
        }

        private void BtnDelete_Click(object s, RoutedEventArgs e)
        {
            if (_editing == null) return;
            var r = AppDialog.Show(
                $"Delete rule for \"{_editing.MatchValue}\"?",
                "Confirm Delete", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (r != MessageBoxResult.Yes) return;
            _s.CustomRules.Remove(_editing);
            _s.Save();
            ButtonDetector.ReloadCustomRules(_s.CustomRules);
            HideEditor();
            RefreshList();
        }

        private void CmbMatchType_Changed(object s, SelectionChangedEventArgs e) { }

        // ── Export / Import ───────────────────────────────────────────────────

        private void BtnExport_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var dlg = new Microsoft.Win32.SaveFileDialog
                {
                    Title       = "Export Custom Rules",
                    FileName    = "badge-overlay-rules.bo-rules.json",
                    DefaultExt  = ".bo-rules.json",
                    Filter      = "Badge Overlay rules (*.bo-rules.json)|*.bo-rules.json|JSON (*.json)|*.json|All files (*.*)|*.*",
                };
                if (dlg.ShowDialog(this) != true) return;

                string json = System.Text.Json.JsonSerializer.Serialize(
                    _s.CustomRules ?? new System.Collections.Generic.List<CustomRule>(),
                    AppSettingsJsonContext.Default.ListCustomRule);
                System.IO.File.WriteAllText(dlg.FileName, json);
                AppDialog.Show(this,
                    $"Exported {_s.CustomRules?.Count ?? 0} rule(s).",
                    "Export rules", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                AppLog.Error("BtnExport_Click", ex);
                AppDialog.Show(this, "Export failed — see crash log.",
                    "Export rules", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void BtnImport_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var dlg = new Microsoft.Win32.OpenFileDialog
                {
                    Title       = "Import Custom Rules",
                    DefaultExt  = ".bo-rules.json",
                    Filter      = "Badge Overlay rules (*.bo-rules.json)|*.bo-rules.json|JSON (*.json)|*.json|All files (*.*)|*.*",
                    CheckFileExists = true,
                };
                if (dlg.ShowDialog(this) != true) return;

                string json = System.IO.File.ReadAllText(dlg.FileName);
                var imported = System.Text.Json.JsonSerializer.Deserialize(
                    json, AppSettingsJsonContext.Default.ListCustomRule);
                if (imported == null || imported.Count == 0)
                {
                    AppDialog.Show(this, "File contained no rules.",
                        "Import rules", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                var r = AppDialog.Show(this,
                    $"Import {imported.Count} rule(s)?\n\n" +
                    "Yes = append to current rules.\n" +
                    "No  = replace current rules.\n" +
                    "Cancel = abort.",
                    "Import rules",
                    MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
                if (r == MessageBoxResult.Cancel) return;

                if (_s.CustomRules == null)
                    _s.CustomRules = new System.Collections.Generic.List<CustomRule>();
                if (r == MessageBoxResult.No)
                {
                    // Soft-undo: before destructive replace, auto-export the
                    // current rules to a timestamped backup alongside the
                    // settings file. Users can re-import the .bak file if
                    // the new rule set wasn't what they wanted.
                    try
                    {
                        if (_s.CustomRules.Count > 0)
                        {
                            string dir = System.IO.Path.Combine(
                                System.Environment.GetFolderPath(
                                    System.Environment.SpecialFolder.ApplicationData),
                                "BadgeOverlay");
                            System.IO.Directory.CreateDirectory(dir);
                            string bak = System.IO.Path.Combine(dir,
                                $"custom-rules.{System.DateTime.Now:yyyyMMdd-HHmmss}.bak.bo-rules.json");
                            string bakJson = System.Text.Json.JsonSerializer.Serialize(
                                _s.CustomRules, AppSettingsJsonContext.Default.ListCustomRule);
                            System.IO.File.WriteAllText(bak, bakJson);
                        }
                    }
                    catch (Exception bex) { AppLog.Error("BtnImport_Click backup", bex); }
                    _s.CustomRules.Clear();
                }
                _s.CustomRules.AddRange(imported);
                _s.Save();
                ButtonDetector.ReloadCustomRules(_s.CustomRules);
                HideEditor();
                RefreshList();
            }
            catch (Exception ex)
            {
                AppLog.Error("BtnImport_Click", ex);
                AppDialog.Show(this, "Import failed — file may not be a valid rules export.",
                    "Import rules", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void BtnClose_Click(object s, RoutedEventArgs e) => Close();

        // ── Window size persistence ───────────────────────────────────────────
        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            var (w, h) = WindowSizeCache.Get("CustomRulesWindow", 720, 600);
            Width = w; Height = h;
        }

        protected override void OnClosed(EventArgs e)
        {
            WindowSizeCache.Set("CustomRulesWindow", Width, Height);
            base.OnClosed(e);
        }
    }
}
