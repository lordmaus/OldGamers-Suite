using System;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;

namespace SuperMagnifier
{
    public partial class AppPresetDialog : Window
    {
        public class RuleVm
        {
            public string ExeName     { get; set; }
            public int    Slot        { get; set; }
            public string SlotDisplay => $"Preset {Slot + 1}";
        }

        private readonly ObservableCollection<RuleVm> _items = new ObservableCollection<RuleVm>();
        public System.Collections.Generic.List<AppPresetMap.AppPresetRule> Result { get; private set; }

        public AppPresetDialog(System.Collections.Generic.List<AppPresetMap.AppPresetRule> existing)
        {
            InitializeComponent();
            foreach (var r in existing)
                _items.Add(new RuleVm { ExeName = r.ExeName, Slot = r.PresetSlot });
            RulesList.ItemsSource = _items;
        }

        private void AddBtn_Click(object sender, RoutedEventArgs e)
        {
            string exe = ExeBox.Text.Trim().ToLowerInvariant().Replace(".exe", "");
            if (string.IsNullOrWhiteSpace(exe)) return;
            int slot = 0;
            if (SlotCombo.SelectedItem is ComboBoxItem ci && ci.Tag is string t)
                int.TryParse(t, out slot);
            _items.Add(new RuleVm { ExeName = exe, Slot = slot });
            ExeBox.Text = "";
        }

        private void RemoveBtn_Click(object sender, RoutedEventArgs e)
        {
            if (RulesList.SelectedItem is not RuleVm vm) return;
            // Error prevention (WCAG 3.3.6). Cancel also discards, but confirm a destructive row delete.
            if (MessageBox.Show(this, $"Remove the rule for \"{vm.ExeName}\"?", "Remove app preset rule",
                    MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes)
                return;
            _items.Remove(vm);
        }

        private void SaveBtn_Click(object sender, RoutedEventArgs e)
        {
            Result = new System.Collections.Generic.List<AppPresetMap.AppPresetRule>();
            foreach (var vm in _items)
                Result.Add(new AppPresetMap.AppPresetRule { ExeName = vm.ExeName, PresetSlot = vm.Slot });
            DialogResult = true;
        }

        private void CancelBtn_Click(object sender, RoutedEventArgs e) => DialogResult = false;

        // ── Window size persistence ───────────────────────────────────────────
        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            var (w, h) = WindowSizeCache.Get("AppPresetDialog", 480, 400);
            Width = w; Height = h;
        }

        protected override void OnClosed(EventArgs e)
        {
            WindowSizeCache.Set("AppPresetDialog", Width, Height);
            base.OnClosed(e);
        }
    }
}
