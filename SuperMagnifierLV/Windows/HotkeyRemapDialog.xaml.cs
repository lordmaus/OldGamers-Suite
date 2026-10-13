using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;

namespace SuperMagnifier
{
    // ── View model for each hotkey row ────────────────────────────────────────
    public sealed class HotkeyRowVm : INotifyPropertyChanged
    {
        private string _displayText;
        public string ActionKey          { get; set; }
        public string Label              { get; set; }
        public string AutomationName     => $"Capture hotkey for {Label}";
        public string ClearAutomationName=> $"Clear hotkey for {Label}";
        public HotkeyBinding Binding     { get; set; }
        // All actions are remappable since hotkey scheme 2 (2026-10-13); kept for the XAML bindings.
        public bool IsFixed              => false;

        public string DisplayText
        {
            get => _displayText;
            set { _displayText = value; PropertyChanged?.Invoke(this, new(nameof(DisplayText))); }
        }

        public event PropertyChangedEventHandler PropertyChanged;
    }

    public partial class HotkeyRemapDialog : Window
    {
        [DllImport("user32.dll")] static extern short GetAsyncKeyState(int vKey);
        [DllImport("user32.dll")] static extern int   GetKeyNameTextW(int lParam, System.Text.StringBuilder lpString, int nSize);

        // ── Modifier VK codes ─────────────────────────────────────────────────
        private static readonly int[] ModifierVKs = { 0x10, 0x11, 0x12, 0x5B, 0x5C }; // Shift/Ctrl/Alt/Win L/R

        // ── State ─────────────────────────────────────────────────────────────
        private List<HotkeyRowVm> _rows;
        private HotkeyRowVm       _capturingRow;   // null = not capturing
        private Button            _capturingBtn;
        private HwndSource        _hwndSource;

        // Result — set to a copy of bindings when Apply is clicked
        public Dictionary<string, HotkeyBinding> Result { get; private set; }

        // ── Constructor ───────────────────────────────────────────────────────
        public HotkeyRemapDialog(Dictionary<string, HotkeyBinding> current)
        {
            InitializeComponent();

            var labels  = HotkeyBinding.ActionLabels();
            var defaults= HotkeyBinding.Defaults();

            _rows = new List<HotkeyRowVm>();
            foreach (var key in defaults.Keys)
            {
                current.TryGetValue(key, out var binding);
                binding ??= defaults[key];
                _rows.Add(new HotkeyRowVm
                {
                    ActionKey   = key,
                    Label       = labels.TryGetValue(key, out var lbl) ? lbl : key,
                    Binding     = new HotkeyBinding(binding.Modifiers, binding.VirtualKey),
                    DisplayText = binding.ToDisplayString(),
                });
            }
            HotkeyList.ItemsSource = _rows;

            Loaded  += (s, e) => SetupHook();
            Closing += OnClosing;
        }

        // ── WndProc hook for raw key capture ─────────────────────────────────
        private void SetupHook()
        {
            _hwndSource = (HwndSource)PresentationSource.FromVisual(this);
            _hwndSource?.AddHook(WndProc);
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            const int WM_KEYDOWN   = 0x0100;
            const int WM_SYSKEYDOWN= 0x0104;

            if (_capturingRow == null) return IntPtr.Zero;
            if (msg != WM_KEYDOWN && msg != WM_SYSKEYDOWN) return IntPtr.Zero;

            int vkInt = wParam.ToInt32();

            // Ignore bare modifier keys — wait for a non-modifier key
            if (ModifierVKs.Contains(vkInt)) return IntPtr.Zero;

            // Escape = clear / unbind
            if (vkInt == 0x1B)
            {
                CommitCapture(new HotkeyBinding(0, 0));
                handled = true;
                return IntPtr.Zero;
            }

            // Build modifier mask from currently-held modifier keys
            uint mods = 0;
            if ((GetAsyncKeyState(0x11) & 0x8000) != 0) mods |= GlobalHotkey.MOD_CONTROL;
            if ((GetAsyncKeyState(0x10) & 0x8000) != 0) mods |= GlobalHotkey.MOD_SHIFT;
            if ((GetAsyncKeyState(0x12) & 0x8000) != 0) mods |= GlobalHotkey.MOD_ALT;
            if ((GetAsyncKeyState(0x5B) & 0x8000) != 0 ||
                (GetAsyncKeyState(0x5C) & 0x8000) != 0) mods |= GlobalHotkey.MOD_WIN;

            var binding = new HotkeyBinding(mods, (uint)vkInt);
            CommitCapture(binding);
            handled = true;
            return IntPtr.Zero;
        }

        private void CommitCapture(HotkeyBinding binding)
        {
            if (_capturingRow == null) return;

            // Warn about system-reserved combinations
            if (!binding.IsUnbound && IsSystemReserved(binding))
            {
                StatusLabel.Text = "That combination is reserved by Windows and cannot be captured. Try a different key.";
                StatusIcon.Visibility = Visibility.Visible;
                return; // Stay in capture mode
            }

            _capturingRow.Binding     = binding;
            _capturingRow.DisplayText = binding.ToDisplayString();

            // Restore button style
            if (_capturingBtn != null)
                _capturingBtn.Style = (Style)FindResource("CaptureBtn");

            _capturingRow = null;
            _capturingBtn = null;

            StatusLabel.Text = "Click any button on the right to begin capturing a new shortcut.";
            StatusIcon.Visibility = Visibility.Collapsed;
            CheckConflicts();
        }

        /// <summary>
        /// Returns true for well-known system-reserved combinations that
        /// Windows will intercept before any application hotkey fires.
        /// </summary>
        private static bool IsSystemReserved(HotkeyBinding b)
        {
            bool ctrl  = (b.Modifiers & GlobalHotkey.MOD_CONTROL) != 0;
            bool alt   = (b.Modifiers & GlobalHotkey.MOD_ALT)     != 0;
            bool shift = (b.Modifiers & GlobalHotkey.MOD_SHIFT)   != 0;
            bool win   = (b.Modifiers & GlobalHotkey.MOD_WIN)     != 0;
            uint vk    = b.VirtualKey;

            // Win+L (lock screen), Win+D, Win+E, Win+R, etc.
            if (win) return true;
            // Ctrl+Alt+Del, Ctrl+Alt+Esc — intercepted by kernel
            if (ctrl && alt && (vk == 0x2E || vk == 0x1B || vk == 0x2C)) return true;
            // Alt+F4 (close window), Alt+Tab, Alt+Esc
            if (alt && !ctrl && (vk == 0x73 || vk == 0x09 || vk == 0x1B)) return true;
            // PrintScreen, Pause/Break without modifiers (commonly reserved)
            return false;
        }

        // ── Row button handlers ───────────────────────────────────────────────
        private void CaptureBtn_Click(object sender, RoutedEventArgs e)
        {
            var btn = (Button)sender;
            string actionKey = (string)btn.Tag;
            var row = _rows.First(r => r.ActionKey == actionKey);

            // Cancel any existing capture first
            if (_capturingRow != null)
                CommitCancel();

            _capturingRow = row;
            _capturingBtn = btn;

            btn.Style = (Style)FindResource("CaptureBtnListening");
            row.DisplayText = "▶ Press keys…";
            StatusLabel.Text = $"Capturing for: {row.Label}  —  press the desired key combination now. Press Escape to unbind.";
            StatusIcon.Visibility = Visibility.Collapsed;
            ConflictBanner.Visibility = Visibility.Collapsed;
        }

        private void CommitCancel()
        {
            if (_capturingRow == null) return;
            _capturingRow.DisplayText = _capturingRow.Binding.ToDisplayString();
            if (_capturingBtn != null)
                _capturingBtn.Style = (Style)FindResource("CaptureBtn");
            _capturingRow = null;
            _capturingBtn = null;
        }

        private void ClearBtn_Click(object sender, RoutedEventArgs e)
        {
            string actionKey = (string)((Button)sender).Tag;
            var row = _rows.First(r => r.ActionKey == actionKey);
            if (_capturingRow == row) CommitCancel();
            row.Binding     = new HotkeyBinding(0, 0);
            row.DisplayText = "(none)";
            CheckConflicts();
        }

        // ── Conflict detection ────────────────────────────────────────────────
        private void CheckConflicts()
        {
            var bound = _rows.Where(r => !r.Binding.IsUnbound).ToList();
            var conflicts = bound
                .GroupBy(r => (r.Binding.Modifiers, r.Binding.VirtualKey))
                .Where(g => g.Count() > 1)
                .SelectMany(g => g)
                .Select(r => r.Label)
                .ToList();

            bool hasConflict = conflicts.Any();
            if (hasConflict)
            {
                ConflictLabel.Text = $"Conflict: {string.Join(", ", conflicts)} share the same key combination. Resolve before saving.";
                ConflictBanner.Visibility = Visibility.Visible;
                // Prevent saving while unresolved conflicts exist
                ApplyBtn.IsEnabled = false;
                ApplyBtn.ToolTip   = "Resolve hotkey conflicts before saving";
            }
            else
            {
                ConflictBanner.Visibility = Visibility.Collapsed;
                ApplyBtn.IsEnabled = true;
                ApplyBtn.ToolTip   = "Save hotkey bindings";
            }
        }

        // ── Bottom buttons ────────────────────────────────────────────────────
        private void ResetAllBtn_Click(object sender, RoutedEventArgs e)
        {
            CommitCancel();
            // Error prevention (WCAG 3.3.6): confirm before discarding every custom binding.
            // Cancel still reverts everything, but confirm anyway — the dialog lists 30+ rows.
            if (AppDialog.Show(this,
                    "Restore every hotkey to the default (Ctrl+Alt) shortcuts?\n\nYour custom shortcuts will be replaced. You can still press Cancel afterwards to keep the old ones.",
                    "Restore default hotkeys", MessageBoxButton.YesNo, MessageBoxImage.Question,
                    MessageBoxResult.No) != MessageBoxResult.Yes)
                return;
            var defaults = HotkeyBinding.Defaults();
            foreach (var row in _rows)
            {
                if (defaults.TryGetValue(row.ActionKey, out var def))
                {
                    row.Binding     = new HotkeyBinding(def.Modifiers, def.VirtualKey);
                    row.DisplayText = row.Binding.ToDisplayString();
                }
            }
            ConflictBanner.Visibility = Visibility.Collapsed;
            StatusLabel.Text = "All hotkeys reset to defaults. Press Apply to keep, or Cancel to undo.";
            StatusIcon.Visibility = Visibility.Collapsed;
        }

        private void ApplyBtn_Click(object sender, RoutedEventArgs e)
        {
            CommitCancel();
            // Exclude fixed preset rows — not remappable, must not overwrite defaults
            Result = _rows.Where(r => !r.IsFixed)
                          .ToDictionary(r => r.ActionKey,
                                        r => new HotkeyBinding(r.Binding.Modifiers, r.Binding.VirtualKey));
            DialogResult = true;
        }

        private void CancelBtn_Click(object sender, RoutedEventArgs e)
        {
            CommitCancel();
            DialogResult = false;
        }

        private void OnClosing(object sender, CancelEventArgs e)
        {
            _hwndSource?.RemoveHook(WndProc);
        }

        // ── Window size persistence ───────────────────────────────────────────
        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            var (w, h) = WindowSizeCache.Get("HotkeyRemapDialog", 520, 480);
            SizeToContent = System.Windows.SizeToContent.Manual;
            Width = w; Height = h;
        }

        protected override void OnClosed(EventArgs e)
        {
            WindowSizeCache.Set("HotkeyRemapDialog", Width, Height);
            base.OnClosed(e);
        }
    }
}

// Converter used in DataTemplate to invert IsFixed → IsEnabled for row editability
namespace SuperMagnifier
{
    using System;
    using System.Globalization;
    using System.Windows.Data;

    [System.Windows.Markup.ValueSerializerAttribute(typeof(string))]
    public sealed class InverseBoolConverter : IValueConverter
    {
        public object Convert(object value, Type t, object p, CultureInfo c)
            => value is bool b && !b;
        public object ConvertBack(object value, Type t, object p, CultureInfo c)
            => value is bool b && !b;
    }
}
