using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace TimerStudio
{
    // ── One remappable shortcut entry ─────────────────────────────────────────
    internal class ShortcutEntry
    {
        public string ActionKey  { get; }   // AppPrefs property name key
        public string Label      { get; }
        public string CurrentKey { get; set; }

        // UI references — set when the row is built
        public Button  CaptureButton { get; set; }
        public Button  ClearButton   { get; set; }

        public ShortcutEntry(string actionKey, string label, string currentKey)
        {
            ActionKey  = actionKey;
            Label      = label;
            CurrentKey = currentKey;
        }
    }

    public partial class HotkeysDialog : Window
    {
        // ── Working copies — only written to AppPrefs on Apply ────────────────
        private bool   _hotkeysEnabled;
        private string _addTimer;
        private string _settings;
        private string _theme;
        private string _hide;
        private string _about;

        private readonly List<ShortcutEntry> _entries = new List<ShortcutEntry>();
        private ShortcutEntry _capturing;   // entry currently listening for a key

        // Original values for Cancel
        private readonly bool   _origEnabled;
        private readonly string _origAddTimer;
        private readonly string _origSettings;
        private readonly string _origTheme;
        private readonly string _origHide;
        private readonly string _origAbout;

        public HotkeysDialog()
        {
            InitializeComponent();

            var p = AppPrefs.Instance;
            _hotkeysEnabled = p.HotkeysEnabled;
            _addTimer = p.HotkeyAddTimer;
            _settings = p.HotkeySettings;
            _theme    = p.HotkeyTheme;
            _hide     = p.HotkeyHide;
            _about    = p.HotkeyAbout;

            _origEnabled  = _hotkeysEnabled;
            _origAddTimer = _addTimer;
            _origSettings = _settings;
            _origTheme    = _theme;
            _origHide     = _hide;
            _origAbout    = _about;

            ChkEnabled.IsChecked = _hotkeysEnabled;

            BuildRows();
        }

        // ── Build shortcut rows dynamically ───────────────────────────────────
        private void BuildRows()
        {
            ShortcutPanel.Children.Clear();
            _entries.Clear();

            _entries.Add(new ShortcutEntry("AddTimer", "Add new timer",  _addTimer));
            _entries.Add(new ShortcutEntry("Settings", "Open Settings",  _settings));
            _entries.Add(new ShortcutEntry("Theme",    "Toggle Theme",   _theme));
            _entries.Add(new ShortcutEntry("Hide",     "Hide/Show timers",_hide));
            _entries.Add(new ShortcutEntry("About",    "About",          _about));

            foreach (var entry in _entries)
                ShortcutPanel.Children.Add(BuildRow(entry));
        }

        private UIElement BuildRow(ShortcutEntry entry)
        {
            var grid = new Grid { Margin = new Thickness(0, 4, 0, 4) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(140) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(44) });

            var lbl = new TextBlock
            {
                Text               = entry.Label,
                FontFamily         = new FontFamily("Segoe UI"),
                FontSize           = 13,
                Foreground         = (Brush)FindResource("BrushText"),
                VerticalAlignment  = VerticalAlignment.Center
            };
            Grid.SetColumn(lbl, 0);

            var capture = new Button
            {
                Content    = entry.CurrentKey,
                Style      = (Style)FindResource("FlatButton"),
                FontSize   = 12,
                FontWeight = FontWeights.SemiBold,
                Padding    = new Thickness(8, 6, 8, 6),
                MinHeight  = 44,
                MinWidth   = 0,
                HorizontalContentAlignment = HorizontalAlignment.Center
            };
            System.Windows.Automation.AutomationProperties.SetName(
                capture, "Shortcut for " + entry.Label);
            capture.Click += (s, e) => StartCapture(entry);
            Grid.SetColumn(capture, 1);

            var clear = new Button
            {
                Content   = JtIcons.Img("clearAll", 18),
                Style     = (Style)FindResource("FlatButton"),
                Padding   = new Thickness(4),
                MinHeight = 44,
                MinWidth  = 44,
                ToolTip   = "Clear this shortcut"
            };
            System.Windows.Automation.AutomationProperties.SetName(
                clear, "Clear shortcut for " + entry.Label);
            clear.Click += (s, e) => ClearEntry(entry);
            Grid.SetColumn(clear, 2);

            entry.CaptureButton = capture;
            entry.ClearButton   = clear;

            grid.Children.Add(lbl);
            grid.Children.Add(capture);
            grid.Children.Add(clear);
            return grid;
        }

        // ── Capture logic ─────────────────────────────────────────────────────
        private void StartCapture(ShortcutEntry entry)
        {
            // Cancel any existing capture
            if (_capturing != null) AbortCapture();

            _capturing = entry;
            entry.CaptureButton.Content    = "— press a key —";
            // Capture state: accent fill + OnAccent text (7:1) + "press a key" text = not colour-only.
            entry.CaptureButton.Background = (Brush)FindResource("BrushBlue");
            entry.CaptureButton.Foreground = (Brush)FindResource("BrushOnAccent");
            LblCaptureStatus.Text = "Capturing for \"" + entry.Label + "\" — press a key combo, or Escape to unbind.";
        }

        private void AbortCapture()
        {
            if (_capturing == null) return;
            RestoreButtonStyle(_capturing);
            _capturing = null;
            LblCaptureStatus.Text = "Click any shortcut button on the right to begin capturing.";
        }

        private void CommitCapture(ShortcutEntry entry, string keyString)
        {
            entry.CurrentKey = keyString;
            WriteBack(entry);
            RestoreButtonStyle(entry);
            _capturing = null;
            LblCaptureStatus.Text = "Shortcut updated.";
        }

        private void ClearEntry(ShortcutEntry entry)
        {
            if (_capturing == entry) AbortCapture();
            entry.CurrentKey = "(none)";
            WriteBack(entry);
            RestoreButtonStyle(entry);
        }

        private void RestoreButtonStyle(ShortcutEntry entry)
        {
            entry.CaptureButton.Content    = string.IsNullOrEmpty(entry.CurrentKey) || entry.CurrentKey == "(none)"
                ? "(none)" : entry.CurrentKey;
            entry.CaptureButton.Background = (Brush)FindResource("BrushSurf");
            entry.CaptureButton.Foreground = (Brush)FindResource("BrushText");
        }

        private void WriteBack(ShortcutEntry entry)
        {
            switch (entry.ActionKey)
            {
                case "AddTimer": _addTimer = entry.CurrentKey; break;
                case "Settings": _settings = entry.CurrentKey; break;
                case "Theme":    _theme    = entry.CurrentKey; break;
                case "Hide":     _hide     = entry.CurrentKey; break;
                case "About":    _about    = entry.CurrentKey; break;
            }
        }

        // ── Key capture from window ───────────────────────────────────────────
        protected override void OnPreviewKeyDown(KeyEventArgs e)
        {
            if (_capturing == null) { base.OnPreviewKeyDown(e); return; }

            e.Handled = true;

            Key key = e.Key == Key.System ? e.SystemKey : e.Key;

            // Escape = unbind
            if (key == Key.Escape)
            {
                CommitCapture(_capturing, "(none)");
                return;
            }

            // Ignore bare modifiers
            if (key == Key.LeftShift || key == Key.RightShift ||
                key == Key.LeftCtrl  || key == Key.RightCtrl  ||
                key == Key.LeftAlt   || key == Key.RightAlt   ||
                key == Key.LWin      || key == Key.RWin)
                return;

            var mods = Keyboard.Modifiers;
            var parts = new List<string>();
            if ((mods & ModifierKeys.Control) != 0) parts.Add("Ctrl");
            if ((mods & ModifierKeys.Alt)     != 0) parts.Add("Alt");
            if ((mods & ModifierKeys.Shift)   != 0) parts.Add("Shift");
            parts.Add(KeyToString(key));

            CommitCapture(_capturing, string.Join("+", parts));
        }

        private static string KeyToString(Key key)
        {
            switch (key)
            {
                case Key.D0: return "0"; case Key.D1: return "1"; case Key.D2: return "2";
                case Key.D3: return "3"; case Key.D4: return "4"; case Key.D5: return "5";
                case Key.D6: return "6"; case Key.D7: return "7"; case Key.D8: return "8";
                case Key.D9: return "9";
                case Key.OemPlus:      return "=";
                case Key.OemMinus:     return "-";
                case Key.OemOpenBrackets: return "[";
                case Key.Oem6:         return "]";
                case Key.OemSemicolon: return ";";
                case Key.OemComma:     return ",";
                case Key.OemPeriod:    return ".";
                case Key.OemQuestion:  return "/";
                case Key.Space:        return "Space";
                case Key.Return:       return "Enter";
                case Key.Tab:          return "Tab";
                case Key.Delete:       return "Delete";
                case Key.Home:         return "Home";
                case Key.End:          return "End";
                case Key.PageUp:       return "PgUp";
                case Key.PageDown:     return "PgDn";
                case Key.Insert:       return "Ins";
                case Key.F1:  return "F1";  case Key.F2:  return "F2";
                case Key.F3:  return "F3";  case Key.F4:  return "F4";
                case Key.F5:  return "F5";  case Key.F6:  return "F6";
                case Key.F7:  return "F7";  case Key.F8:  return "F8";
                case Key.F9:  return "F9";  case Key.F10: return "F10";
                case Key.F11: return "F11"; case Key.F12: return "F12";
                default: return key.ToString();
            }
        }

        private void Window_KeyDown(object sender, KeyEventArgs e) { /* handled by OnPreviewKeyDown */ }

        // ── Enable toggle ─────────────────────────────────────────────────────
        private void ChkEnabled_Changed(object sender, RoutedEventArgs e)
        {
            _hotkeysEnabled = ChkEnabled.IsChecked == true;
        }

        // ── Reset all ─────────────────────────────────────────────────────────
        private void BtnResetAll_Click(object sender, RoutedEventArgs e)
        {
            if (_capturing != null) AbortCapture();
            _addTimer = "Ctrl+N";
            _settings = "Alt+S";
            _theme    = "Alt+T";
            _hide     = "Alt+H";
            _about    = "Alt+A";
            _hotkeysEnabled = true;
            ChkEnabled.IsChecked = true;
            foreach (var entry in _entries)
            {
                switch (entry.ActionKey)
                {
                    case "AddTimer": entry.CurrentKey = _addTimer; break;
                    case "Settings": entry.CurrentKey = _settings; break;
                    case "Theme":    entry.CurrentKey = _theme;    break;
                    case "Hide":     entry.CurrentKey = _hide;     break;
                    case "About":    entry.CurrentKey = _about;    break;
                }
                RestoreButtonStyle(entry);
            }
            LblCaptureStatus.Text = "All shortcuts reset to defaults.";
        }

        // ── Apply / Cancel ────────────────────────────────────────────────────
        private void BtnApply_Click(object sender, RoutedEventArgs e)
        {
            if (_capturing != null) AbortCapture();
            var p = AppPrefs.Instance;
            p.HotkeysEnabled = _hotkeysEnabled;
            p.HotkeyAddTimer = _addTimer;
            p.HotkeySettings = _settings;
            p.HotkeyTheme    = _theme;
            p.HotkeyHide     = _hide;
            p.HotkeyAbout    = _about;
            p.Save();
            Close();
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            // Revert any in-progress capture without saving
            if (_capturing != null) AbortCapture();
            Close();
        }

        // ── Window size persistence ───────────────────────────────────────────
        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            var (w, h) = WindowSizeCache.Get("HotkeysDialog", 560, 620);
            Width = w; Height = h;
        }

        protected override void OnClosed(EventArgs e)
        {
            WindowSizeCache.Set("HotkeysDialog", Width, Height);
            base.OnClosed(e);
        }
    }
}
