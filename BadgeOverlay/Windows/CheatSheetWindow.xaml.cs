using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace BadgeOverlay
{
    /// <summary>
    /// Modal cheat-sheet window listing every keyboard shortcut. Opened
    /// via F1 from <see cref="MainWindow"/>. Global-hotkey rows are
    /// generated at runtime from the current <see cref="AppSettings"/>
    /// — static rows would lie when the user has rebound hotkeys in
    /// <see cref="HotkeysWindow"/>. Static rows (Alt-mnemonics, Info
    /// Overlay nudge keys, standard Tab/Esc/Enter) are XAML literals
    /// because they're hardcoded across the codebase.
    /// </summary>
    public partial class CheatSheetWindow : Window
    {
        public CheatSheetWindow(AppSettings s)
        {
            InitializeComponent();
            PopulateGlobalHotkeys(s);
        }

        private void PopulateGlobalHotkeys(AppSettings s)
        {
            if (GlobalHotkeysPanel == null) return;
            GlobalHotkeysPanel.Children.Clear();

            // ── Always-show hotkeys (shown regardless of Enabled state) ──────────
            AddRow(FormatHotkey(s?.HotkeyToggleMods, s?.HotkeyToggleVK, "Ctrl+Shift+O"),
                   "Toggle the badge overlay (active / paused)");

            AddRow(FormatHotkey(s?.HotkeyFreezeMods, s?.HotkeyFreezeVK, "Ctrl+Shift+F"),
                   "Freeze the badge at the current cursor position");

            AddRow(FormatHotkey(s?.HotkeyCopyLabelMods, s?.HotkeyCopyLabelVK, "Ctrl+Shift+C"),
                   "Copy the current detected label to the clipboard");

            AddRow(FormatHotkey(s?.HotkeySpeakMods, s?.HotkeySpeakVK, "Ctrl+Shift+S"),
                   "Mute / unmute Screen Reader Mode speech");

            // ── Preset load hotkeys — only shown if the slot is enabled ──────────
            // Disabled by default (Ctrl+1-5 conflicts with browser tabs).
            // Users configure these in the Hotkeys settings window.
            if (s?.HotkeyLoadPreset1Enabled == true)
                AddRow(FormatHotkey(s.HotkeyLoadPreset1Mods, s.HotkeyLoadPreset1VK, "Ctrl+1"), "Load preset 1");
            if (s?.HotkeyLoadPreset2Enabled == true)
                AddRow(FormatHotkey(s.HotkeyLoadPreset2Mods, s.HotkeyLoadPreset2VK, "Ctrl+2"), "Load preset 2");
            if (s?.HotkeyLoadPreset3Enabled == true)
                AddRow(FormatHotkey(s.HotkeyLoadPreset3Mods, s.HotkeyLoadPreset3VK, "Ctrl+3"), "Load preset 3");
            if (s?.HotkeyLoadPreset4Enabled == true)
                AddRow(FormatHotkey(s.HotkeyLoadPreset4Mods, s.HotkeyLoadPreset4VK, "Ctrl+4"), "Load preset 4");
            if (s?.HotkeyLoadPreset5Enabled == true)
                AddRow(FormatHotkey(s.HotkeyLoadPreset5Mods, s.HotkeyLoadPreset5VK, "Ctrl+5"), "Load preset 5");

            // ── Preset save hotkeys — only shown if enabled ───────────────────────
            if (s?.HotkeySavePreset1Enabled == true)
                AddRow(FormatHotkey(s.HotkeySavePreset1Mods, s.HotkeySavePreset1VK, "Ctrl+Shift+1"), "Save to preset 1");
            if (s?.HotkeySavePreset2Enabled == true)
                AddRow(FormatHotkey(s.HotkeySavePreset2Mods, s.HotkeySavePreset2VK, "Ctrl+Shift+2"), "Save to preset 2");
            if (s?.HotkeySavePreset3Enabled == true)
                AddRow(FormatHotkey(s.HotkeySavePreset3Mods, s.HotkeySavePreset3VK, "Ctrl+Shift+3"), "Save to preset 3");
            if (s?.HotkeySavePreset4Enabled == true)
                AddRow(FormatHotkey(s.HotkeySavePreset4Mods, s.HotkeySavePreset4VK, "Ctrl+Shift+4"), "Save to preset 4");
            if (s?.HotkeySavePreset5Enabled == true)
                AddRow(FormatHotkey(s.HotkeySavePreset5Mods, s.HotkeySavePreset5VK, "Ctrl+Shift+5"), "Save to preset 5");
        }

        /// <summary>
        /// Builds a "Ctrl+Alt+X" string from a modifier mask + virtual
        /// key code. Returns <paramref name="fallback"/> when the
        /// settings object is null (cheat-sheet opened before settings
        /// loaded — shouldn't happen but defended anyway).
        /// </summary>
        private static string FormatHotkey(uint? mods, uint? vk, string fallback)
        {
            if (!mods.HasValue || !vk.HasValue) return fallback;
            string m = "";
            const uint MOD_ALT = 0x0001, MOD_CONTROL = 0x0002, MOD_SHIFT = 0x0004, MOD_WIN = 0x0008;
            if ((mods & MOD_CONTROL) != 0) m += "Ctrl+";
            if ((mods & MOD_ALT)     != 0) m += "Alt+";
            if ((mods & MOD_SHIFT)   != 0) m += "Shift+";
            if ((mods & MOD_WIN)     != 0) m += "Win+";

            // Render virtual key. Letter keys (A-Z = 0x41–0x5A) display
            // as their ASCII letter; everything else falls back to a
            // KeyInterop conversion to WPF's Key enum, which gives a
            // human-readable name for F1, Space, etc.
            string keyName;
            if (vk.Value >= 0x41 && vk.Value <= 0x5A)
            {
                // Letter keys A-Z
                keyName = ((char)vk.Value).ToString();
            }
            else if (vk.Value >= 0x30 && vk.Value <= 0x39)
            {
                // Digit keys 0-9 (top-row, not numpad)
                keyName = ((char)vk.Value).ToString();
            }
            else
            {
                try
                {
                    var k = KeyInterop.KeyFromVirtualKey((int)vk.Value);
                    keyName = k == Key.None ? $"VK_{vk.Value}" : k.ToString();
                }
                catch { keyName = $"VK_{vk.Value}"; }
            }
            return m + keyName;
        }

        private void AddRow(string keys, string description)
        {
            var grid = new Grid { Margin = new Thickness(0, 2, 0, 2) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(140) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var keysBlock = new TextBlock
            {
                Text       = keys,
                FontFamily = new FontFamily("Consolas"),
            };
            try
            {
                keysBlock.SetResourceReference(TextBlock.ForegroundProperty, "BrushBlueText");
            }
            catch { /* swallow: theme not yet realised */ }
            Grid.SetColumn(keysBlock, 0);

            var descBlock = new TextBlock
            {
                Text         = description,
                TextWrapping = TextWrapping.Wrap,
            };
            Grid.SetColumn(descBlock, 1);

            grid.Children.Add(keysBlock);
            grid.Children.Add(descBlock);
            GlobalHotkeysPanel.Children.Add(grid);
        }

        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            // F1 closes when already open (toggle behaviour) — Esc does
            // the same via IsCancel="True" on the close button.
            if (e.Key == Key.F1)
            {
                e.Handled = true;
                Close();
            }
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e) => Close();

        // ── Window size persistence ───────────────────────────────────────────
        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            var (w, h) = WindowSizeCache.Get("CheatSheetWindow", 540, 640);
            Width = w; Height = h;
        }

        protected override void OnClosed(EventArgs e)
        {
            WindowSizeCache.Set("CheatSheetWindow", Width, Height);
            base.OnClosed(e);
        }
    }
}
