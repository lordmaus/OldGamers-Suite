using System;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace ReticleOverlay
{
    public partial class MainWindow
    {
        // ── Preset button labels ──────────────────────────────────────────────
        private void UpdatePresetButtonLabels()
        {
            string[] slots = _prefs.GetPresets();
            Button[] btns  = { BtnPreset1, BtnPreset2, BtnPreset3, BtnPreset4, BtnPreset5 };
            for (int i = 0; i < 5; i++)
            {
                string slot  = slots[i];
                bool   empty = string.IsNullOrEmpty(slot);
                string label = empty ? (i + 1).ToString() : TruncatePresetName(i + 1, slot);
                btns[i].Content = label;
                btns[i].Opacity = empty ? 0.40 : 1.0;
                btns[i].ToolTip = empty
                    ? $"Preset {i + 1} — empty  (select a profile, then click Save Preset)"
                    : $"Load preset {i + 1}: {slot}";
                AutomationProperties.SetName(btns[i],
                    empty ? $"Preset {i + 1} empty" : $"Load preset {i + 1} {slot}");
            }
        }

        private static string TruncatePresetName(int num, string name)
        {
            string display = name.Length > 8 ? name.Substring(0, 7) + "…" : name;
            return $"{num}: {display}";
        }

        // ── Load preset ───────────────────────────────────────────────────────
        private void BtnPreset_Click(object s, RoutedEventArgs e)
        {
            if (s is not Button btn) return;
            // Bug #9 (2026-09-07 review): guard against a non-numeric/missing Tag instead
            // of letting int.Parse throw.
            if (!int.TryParse(btn.Tag?.ToString(), out int slotNum)) return;
            int slot = slotNum - 1;
            string[] names = _prefs.GetPresets();
            string name = names[slot];
            if (string.IsNullOrEmpty(name)) { ShowSaved($"Preset {slot + 1} is empty"); return; }
            int idx = _profiles.FindIndex(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (idx < 0) { ShowSaved($"Profile \"{name}\" not found"); return; }
            CmbProfiles.SelectedIndex = idx;
            ApplyProfile(_profiles[idx]);
            ShowSaved($"Preset {slot + 1} loaded: {name} ✓");
        }

        // ── Save preset ───────────────────────────────────────────────────────
        private void BtnSavePreset_Click(object s, RoutedEventArgs e)
        {
            var ctx = new ContextMenu { PlacementTarget = BtnSavePreset, Placement = PlacementMode.Top };
            for (int i = 0; i < 5; i++)
            {
                int slot = i;
                var item = new MenuItem { Header = $"Save to Preset {i + 1}" };
                AutomationProperties.SetName(item, $"Save to Preset {i + 1}");
                item.Click += (_, __) => SaveToPresetSlot(slot);
                ctx.Items.Add(item);
            }
            ctx.IsOpen = true;
        }

        private void SaveToPresetSlot(int slot)
        {
            int idx = CmbProfiles.SelectedIndex;
            if (idx < 0 || idx >= _profiles.Count) { ShowSaved("Select a profile first"); return; }
            string name = _profiles[idx].Name;
            _prefs.SetPreset(slot, name);
            UpdatePresetButtonLabels();
            _prefs.Save();
            ShowSaved($"Preset {slot + 1} = \"{name}\" ✓");
        }
    }
}
