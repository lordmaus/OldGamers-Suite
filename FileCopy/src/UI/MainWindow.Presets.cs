using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Reflection;

#nullable enable


namespace FileCopy
{
    public partial class MainWindow
    {
        private void BtnAbout_Click(object sender, RoutedEventArgs e)
        {
            new AboutDialog { Owner = this }.ShowDialog();
        }

        private void BtnHotkeys_Click(object sender, RoutedEventArgs e)
        {
            new HotkeysDialog { Owner = this }.ShowDialog();
        }

        private void BtnSettings_Click(object sender, RoutedEventArgs e)
        {
            new SettingsDialog { Owner = this }.ShowDialog();
        }

        private void ShowShortcuts()
        {
            new HotkeysDialog { Owner = this }.ShowDialog();
        }

        private void BtnPreset_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button b) return;
            int n = b.Tag switch { int i => i, string s when int.TryParse(s, out int p) => p, _ => 0 };
            if (n >= 1 && n <= 5) LoadPresetFromSlot(n);
        }

        // Drag-drop reorder ─────────────────────────────────────────────────
        private int _dragPresetSlot = -1;
        private System.Windows.Point _dragStartPoint;
        private bool _dragPending;

        private void PresetBtn_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (sender is not Button b || b.Tag is not int slot) return;
            if (AppPrefs.Instance.Presets[slot - 1] == null) return;
            // Record drag start; don't initiate DnD yet — wait for sufficient mouse movement
            _dragStartPoint = e.GetPosition(null);
            _dragPresetSlot = slot;
            _dragPending    = true;
        }

        private void PresetBtn_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
        {
            if (!_dragPending || _dragPresetSlot < 1) return;
            if (e.LeftButton != System.Windows.Input.MouseButtonState.Pressed) { _dragPending = false; return; }

            var pos  = e.GetPosition(null);
            var diff = pos - _dragStartPoint;
            bool beyondThreshold =
                Math.Abs(diff.X) > SystemParameters.MinimumHorizontalDragDistance ||
                Math.Abs(diff.Y) > SystemParameters.MinimumVerticalDragDistance;

            if (!beyondThreshold) return;
            _dragPending = false;

            if (sender is Button b)
                DragDrop.DoDragDrop(b, _dragPresetSlot.ToString(), DragDropEffects.Move);
        }

        private void PresetBtn_DragEnter(object sender, DragEventArgs e)
        {
            if (sender is Button b && e.Data.GetDataPresent(DataFormats.StringFormat))
            {
                b.BorderBrush     = (System.Windows.Media.Brush)FindResource("FocusRingBrush");
                b.BorderThickness = new Thickness(2);
            }
        }

        private void PresetBtn_DragLeave(object sender, DragEventArgs e)
        {
            if (sender is Button b)
            {
                b.ClearValue(Button.BorderBrushProperty);
                b.ClearValue(Button.BorderThicknessProperty);
            }
        }

        private void PresetBtn_DragOver(object sender, DragEventArgs e)
        {
            e.Effects = e.Data.GetDataPresent(DataFormats.StringFormat)
                ? DragDropEffects.Move : DragDropEffects.None;
            e.Handled = true;
        }

        private void PresetBtn_Drop(object sender, DragEventArgs e)
        {
            _dragPending = false;
            // Clear any visual highlight
            if (sender is Button b2) { b2.ClearValue(Button.BorderBrushProperty); b2.ClearValue(Button.BorderThicknessProperty); }
            if (!e.Data.GetDataPresent(DataFormats.StringFormat)) return;
            if (!int.TryParse(e.Data.GetData(DataFormats.StringFormat)?.ToString(), out int from)) return;

            // Accept drop on the button or any child element inside it
            var btn = sender as Button;
            if (btn == null)
            {
                DependencyObject? el = e.OriginalSource as FrameworkElement;
                while (el != null && btn == null)
                {
                    if (el is Button b) btn = b;
                    else el = System.Windows.Media.VisualTreeHelper.GetParent(el);
                }
            }
            if (btn?.Tag is not int to) return;
            if (from == to || from < 1 || to < 1 || from > 5 || to > 5) return;

            SwapPresetSlots(from, to);
            _dragPresetSlot = -1;
        }

        // M3 fix: SC 2.5.7 — single-pointer (right-click) alternatives to drag-reorder
        private void PresetMoveLeft_Click(object sender, RoutedEventArgs e)
        {
            int slot = GetPresetSlotFromSender(sender);
            if (slot >= 2) SwapPresetSlots(slot, slot - 1);
        }

        private void PresetMoveRight_Click(object sender, RoutedEventArgs e)
        {
            int slot = GetPresetSlotFromSender(sender);
            if (slot >= 1 && slot <= 4) SwapPresetSlots(slot, slot + 1);
        }

        private static int GetPresetSlotFromSender(object sender)
        {
            // Tag is set on the MenuItem; the slot int is stored there
            if (sender is MenuItem mi && mi.Tag is int s) return s;
            if (sender is MenuItem mi2 && int.TryParse(mi2.Tag?.ToString(), out int p2)) return p2;
            return -1;
        }

        private void SwapPresetSlots(int from, int to)
        {
            if (from == to || from < 1 || to < 1 || from > 5 || to > 5) return;
            var p   = AppPrefs.Instance;
            var arr = (PresetData[])p.Presets.Clone();
            (arr[from - 1], arr[to - 1]) = (arr[to - 1], arr[from - 1]);
            p.Presets = arr;
            Vm.RefreshPresetSlots();
            Vm.StatusText = $"Swapped preset {from} ↔ {to}.";
        }

        private void BtnSavePreset_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new PresetSlotDialog { Owner = this };
            if (dlg.ShowDialog() != true) return;
            SavePresetToSlot(dlg.SelectedSlot);
        }

        private void LoadPresetFromSlot(int n)
        {
            int slot = n - 1;
            if ((uint)slot >= 5) return;

            var preset = AppPrefs.Instance.Presets[slot];
            if (preset is null)
            {
                AppDialog.Show(this, $"Preset {n} is empty. Configure paths/options and click Save.",
                                "File Copy Studio", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var p = AppPrefs.Instance;
            // Presets are single-source — route through the picker so SourceParent/checked
            // children stay consistent with the inline UI rather than being set directly.
            ApplySourceParent(preset.SourcePath ?? "");
            p.DestPath           = preset.DestPath;
            p.ConflictMode       = preset.ConflictMode;
            p.FileMaskInclude    = preset.FileMaskInclude;
            p.FileMaskExclude    = preset.FileMaskExclude;
            p.IncludeSubdirs     = preset.IncludeSubdirs;
            p.PreserveTimestamps = preset.PreserveTimestamps;
            p.PreserveAttributes = preset.PreserveAttributes;
            p.VerifyCrc          = preset.VerifyCrc;
            p.DeleteSourceOnMove = preset.DeleteSourceOnMove;
            p.BufferSizeKb       = preset.BufferSizeKb;

            Vm.StatusText = $"Loaded preset {n}.";
        }

        private void SavePresetToSlot(int n)
        {
            int slot = n - 1;
            if ((uint)slot >= 5) return;

            var p = AppPrefs.Instance;
            var arr = (PresetData[])p.Presets.Clone();
            arr[slot] = new PresetData(
                p.SourcePath, p.DestPath, p.ConflictMode,
                p.FileMaskInclude, p.FileMaskExclude,
                p.IncludeSubdirs, p.PreserveTimestamps, p.PreserveAttributes,
                p.VerifyCrc, p.DeleteSourceOnMove, p.BufferSizeKb);
            p.Presets = arr;

            Vm.StatusText = $"Saved preset {n}.";
        }

    }
}
