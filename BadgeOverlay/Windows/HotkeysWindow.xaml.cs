using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;

namespace BadgeOverlay
{
    /// <summary>
    /// Full hotkeys configuration window matching the Super Magnifier LV reference style.
    /// Each remappable action gets a row: label | key-capture button | × clear button.
    /// </summary>
    public partial class HotkeysWindow : Window
    {
        private readonly AppSettings _s;
        private HotkeySnapshot      _snapshot;   // taken at open; restored on Cancel

        // ── Hotkey snapshot (cancel support) ──────────────────────────────────
        private sealed class HotkeySnapshot
        {
            public (uint vk, uint mods, bool en) Toggle, Freeze, Copy, Speak;
            public (uint vk, uint mods, bool en) LP1, LP2, LP3, LP4, LP5;
            public (uint vk, uint mods, bool en) SP1, SP2, SP3, SP4, SP5;

            public static HotkeySnapshot Take(AppSettings s) => new HotkeySnapshot
            {
                Toggle = (s.HotkeyToggleVK,       s.HotkeyToggleMods,       s.HotkeyToggleEnabled),
                Freeze = (s.HotkeyFreezeVK,       s.HotkeyFreezeMods,       s.HotkeyFreezeEnabled),
                Copy   = (s.HotkeyCopyLabelVK,    s.HotkeyCopyLabelMods,    s.HotkeyCopyLabelEnabled),
                Speak  = (s.HotkeySpeakVK,        s.HotkeySpeakMods,        s.HotkeySpeakEnabled),
                LP1    = (s.HotkeyLoadPreset1VK,  s.HotkeyLoadPreset1Mods,  s.HotkeyLoadPreset1Enabled),
                LP2    = (s.HotkeyLoadPreset2VK,  s.HotkeyLoadPreset2Mods,  s.HotkeyLoadPreset2Enabled),
                LP3    = (s.HotkeyLoadPreset3VK,  s.HotkeyLoadPreset3Mods,  s.HotkeyLoadPreset3Enabled),
                LP4    = (s.HotkeyLoadPreset4VK,  s.HotkeyLoadPreset4Mods,  s.HotkeyLoadPreset4Enabled),
                LP5    = (s.HotkeyLoadPreset5VK,  s.HotkeyLoadPreset5Mods,  s.HotkeyLoadPreset5Enabled),
                SP1    = (s.HotkeySavePreset1VK,  s.HotkeySavePreset1Mods,  s.HotkeySavePreset1Enabled),
                SP2    = (s.HotkeySavePreset2VK,  s.HotkeySavePreset2Mods,  s.HotkeySavePreset2Enabled),
                SP3    = (s.HotkeySavePreset3VK,  s.HotkeySavePreset3Mods,  s.HotkeySavePreset3Enabled),
                SP4    = (s.HotkeySavePreset4VK,  s.HotkeySavePreset4Mods,  s.HotkeySavePreset4Enabled),
                SP5    = (s.HotkeySavePreset5VK,  s.HotkeySavePreset5Mods,  s.HotkeySavePreset5Enabled),
            };

            public void Restore(AppSettings s)
            {
                (s.HotkeyToggleVK,       s.HotkeyToggleMods,       s.HotkeyToggleEnabled)      = Toggle;
                (s.HotkeyFreezeVK,       s.HotkeyFreezeMods,       s.HotkeyFreezeEnabled)      = Freeze;
                (s.HotkeyCopyLabelVK,    s.HotkeyCopyLabelMods,    s.HotkeyCopyLabelEnabled)   = Copy;
                (s.HotkeySpeakVK,        s.HotkeySpeakMods,        s.HotkeySpeakEnabled)       = Speak;
                (s.HotkeyLoadPreset1VK,  s.HotkeyLoadPreset1Mods,  s.HotkeyLoadPreset1Enabled) = LP1;
                (s.HotkeyLoadPreset2VK,  s.HotkeyLoadPreset2Mods,  s.HotkeyLoadPreset2Enabled) = LP2;
                (s.HotkeyLoadPreset3VK,  s.HotkeyLoadPreset3Mods,  s.HotkeyLoadPreset3Enabled) = LP3;
                (s.HotkeyLoadPreset4VK,  s.HotkeyLoadPreset4Mods,  s.HotkeyLoadPreset4Enabled) = LP4;
                (s.HotkeyLoadPreset5VK,  s.HotkeyLoadPreset5Mods,  s.HotkeyLoadPreset5Enabled) = LP5;
                (s.HotkeySavePreset1VK,  s.HotkeySavePreset1Mods,  s.HotkeySavePreset1Enabled) = SP1;
                (s.HotkeySavePreset2VK,  s.HotkeySavePreset2Mods,  s.HotkeySavePreset2Enabled) = SP2;
                (s.HotkeySavePreset3VK,  s.HotkeySavePreset3Mods,  s.HotkeySavePreset3Enabled) = SP3;
                (s.HotkeySavePreset4VK,  s.HotkeySavePreset4Mods,  s.HotkeySavePreset4Enabled) = SP4;
                (s.HotkeySavePreset5VK,  s.HotkeySavePreset5Mods,  s.HotkeySavePreset5Enabled) = SP5;
            }
        }

        // ── Hotkey definitions ────────────────────────────────────────────────

        private class HotkeyDef
        {
            public string ActionName  { get; set; }
            public string Description { get; set; }
            public Func<AppSettings, string> GetLabel   { get; set; }
            public Action<AppSettings, uint, uint> SetKeys { get; set; }
            public Func<AppSettings, bool> GetEnabled  { get; set; }
            public Action<AppSettings, bool> SetEnabled { get; set; }
            // Working copy during editing
            public uint   EditVK   { get; set; }
            public uint   EditMods { get; set; }
        }

        private List<HotkeyDef> _defs;
        private Button          _capturingButton;  // currently capturing key
        private HotkeyDef       _capturingDef;

        // ── Constructor ───────────────────────────────────────────────────────

        public HotkeysWindow(AppSettings settings)
        {
            InitializeComponent();
            CursorPreserver.Attach(this);
            _s = settings;
            Loaded  += OnLoaded;
            KeyDown += OnKeyDown;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            TrySetIcon();
            _snapshot = HotkeySnapshot.Take(_s);   // snapshot before any edits
            BuildDefs();
            BuildRows();
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

        // ── Build hotkey definitions ──────────────────────────────────────────

        private void BuildDefs()
        {
            _defs = new List<HotkeyDef>
            {
                new HotkeyDef
                {
                    ActionName  = "Toggle Overlay On/Off",
                    Description = "Show or hide the badge overlay globally",
                    GetLabel    = s => FormatHotkey(s.HotkeyToggleMods, s.HotkeyToggleVK),
                    SetKeys     = (s, vk, mods) => { s.HotkeyToggleVK = vk; s.HotkeyToggleMods = mods; },
                    GetEnabled  = s => s.HotkeyToggleEnabled,
                    SetEnabled  = (s, v) => s.HotkeyToggleEnabled = v,
                    EditVK      = _s.HotkeyToggleVK,
                    EditMods    = _s.HotkeyToggleMods,
                },
                new HotkeyDef
                {
                    ActionName  = "Freeze / Unfreeze Badge",
                    Description = "Pause badge label updates — keeps the current label on screen until toggled off. Useful for screen-reader users.",
                    GetLabel    = s => FormatHotkey(s.HotkeyFreezeMods, s.HotkeyFreezeVK),
                    SetKeys     = (s, vk, mods) => { s.HotkeyFreezeVK = vk; s.HotkeyFreezeMods = mods; },
                    GetEnabled  = s => s.HotkeyFreezeEnabled,
                    SetEnabled  = (s, v) => s.HotkeyFreezeEnabled = v,
                    EditVK      = _s.HotkeyFreezeVK,
                    EditMods    = _s.HotkeyFreezeMods,
                },
                new HotkeyDef
                {
                    ActionName  = "Copy Detected Label",
                    Description = "Copies the currently-shown badge label to the clipboard. Click-safe alternative for users who can't select the badge (it's deliberately click-through).",
                    GetLabel    = s => FormatHotkey(s.HotkeyCopyLabelMods, s.HotkeyCopyLabelVK),
                    SetKeys     = (s, vk, mods) => { s.HotkeyCopyLabelVK = vk; s.HotkeyCopyLabelMods = mods; },
                    GetEnabled  = s => s.HotkeyCopyLabelEnabled,
                    SetEnabled  = (s, v) => s.HotkeyCopyLabelEnabled = v,
                    EditVK      = _s.HotkeyCopyLabelVK,
                    EditMods    = _s.HotkeyCopyLabelMods,
                },
                new HotkeyDef
                {
                    ActionName  = "Toggle Speech On/Off",
                    Description = "Mutes / unmutes Screen Reader Mode speech without changing the configured trigger mode or dwell. Useful for silencing speech during meetings or phone calls. Audio confirmation on each toggle.",
                    GetLabel    = s => FormatHotkey(s.HotkeySpeakMods, s.HotkeySpeakVK),
                    SetKeys     = (s, vk, mods) => { s.HotkeySpeakVK = vk; s.HotkeySpeakMods = mods; },
                    GetEnabled  = s => s.HotkeySpeakEnabled,
                    SetEnabled  = (s, v) => s.HotkeySpeakEnabled = v,
                    EditVK      = _s.HotkeySpeakVK,
                    EditMods    = _s.HotkeySpeakMods,
                },

                // ── Load Preset 1-5 ───────────────────────────────────────────────
                // Disabled by default — Ctrl+1-5 conflicts with browser tabs /
                // text editors. Users opt in per-slot.
                new HotkeyDef
                {
                    ActionName  = "Load Preset 1",
                    Description = "Recall saved preset slot 1. Has no effect if slot is empty.",
                    GetLabel    = s => FormatHotkey(s.HotkeyLoadPreset1Mods, s.HotkeyLoadPreset1VK),
                    SetKeys     = (s, vk, mods) => { s.HotkeyLoadPreset1VK = vk; s.HotkeyLoadPreset1Mods = mods; },
                    GetEnabled  = s => s.HotkeyLoadPreset1Enabled,
                    SetEnabled  = (s, v) => s.HotkeyLoadPreset1Enabled = v,
                    EditVK      = _s.HotkeyLoadPreset1VK,
                    EditMods    = _s.HotkeyLoadPreset1Mods,
                },
                new HotkeyDef
                {
                    ActionName  = "Load Preset 2",
                    Description = "Recall saved preset slot 2. Has no effect if slot is empty.",
                    GetLabel    = s => FormatHotkey(s.HotkeyLoadPreset2Mods, s.HotkeyLoadPreset2VK),
                    SetKeys     = (s, vk, mods) => { s.HotkeyLoadPreset2VK = vk; s.HotkeyLoadPreset2Mods = mods; },
                    GetEnabled  = s => s.HotkeyLoadPreset2Enabled,
                    SetEnabled  = (s, v) => s.HotkeyLoadPreset2Enabled = v,
                    EditVK      = _s.HotkeyLoadPreset2VK,
                    EditMods    = _s.HotkeyLoadPreset2Mods,
                },
                new HotkeyDef
                {
                    ActionName  = "Load Preset 3",
                    Description = "Recall saved preset slot 3. Has no effect if slot is empty.",
                    GetLabel    = s => FormatHotkey(s.HotkeyLoadPreset3Mods, s.HotkeyLoadPreset3VK),
                    SetKeys     = (s, vk, mods) => { s.HotkeyLoadPreset3VK = vk; s.HotkeyLoadPreset3Mods = mods; },
                    GetEnabled  = s => s.HotkeyLoadPreset3Enabled,
                    SetEnabled  = (s, v) => s.HotkeyLoadPreset3Enabled = v,
                    EditVK      = _s.HotkeyLoadPreset3VK,
                    EditMods    = _s.HotkeyLoadPreset3Mods,
                },
                new HotkeyDef
                {
                    ActionName  = "Load Preset 4",
                    Description = "Recall saved preset slot 4. Has no effect if slot is empty.",
                    GetLabel    = s => FormatHotkey(s.HotkeyLoadPreset4Mods, s.HotkeyLoadPreset4VK),
                    SetKeys     = (s, vk, mods) => { s.HotkeyLoadPreset4VK = vk; s.HotkeyLoadPreset4Mods = mods; },
                    GetEnabled  = s => s.HotkeyLoadPreset4Enabled,
                    SetEnabled  = (s, v) => s.HotkeyLoadPreset4Enabled = v,
                    EditVK      = _s.HotkeyLoadPreset4VK,
                    EditMods    = _s.HotkeyLoadPreset4Mods,
                },
                new HotkeyDef
                {
                    ActionName  = "Load Preset 5",
                    Description = "Recall saved preset slot 5. Has no effect if slot is empty.",
                    GetLabel    = s => FormatHotkey(s.HotkeyLoadPreset5Mods, s.HotkeyLoadPreset5VK),
                    SetKeys     = (s, vk, mods) => { s.HotkeyLoadPreset5VK = vk; s.HotkeyLoadPreset5Mods = mods; },
                    GetEnabled  = s => s.HotkeyLoadPreset5Enabled,
                    SetEnabled  = (s, v) => s.HotkeyLoadPreset5Enabled = v,
                    EditVK      = _s.HotkeyLoadPreset5VK,
                    EditMods    = _s.HotkeyLoadPreset5Mods,
                },

                // ── Save to Preset 1-5 ────────────────────────────────────────────
                // Disabled by default — Ctrl+Shift+1-5 conflicts with IDEs.
                new HotkeyDef
                {
                    ActionName  = "Save to Preset 1",
                    Description = "Capture current settings into preset slot 1, overwriting any existing data.",
                    GetLabel    = s => FormatHotkey(s.HotkeySavePreset1Mods, s.HotkeySavePreset1VK),
                    SetKeys     = (s, vk, mods) => { s.HotkeySavePreset1VK = vk; s.HotkeySavePreset1Mods = mods; },
                    GetEnabled  = s => s.HotkeySavePreset1Enabled,
                    SetEnabled  = (s, v) => s.HotkeySavePreset1Enabled = v,
                    EditVK      = _s.HotkeySavePreset1VK,
                    EditMods    = _s.HotkeySavePreset1Mods,
                },
                new HotkeyDef
                {
                    ActionName  = "Save to Preset 2",
                    Description = "Capture current settings into preset slot 2, overwriting any existing data.",
                    GetLabel    = s => FormatHotkey(s.HotkeySavePreset2Mods, s.HotkeySavePreset2VK),
                    SetKeys     = (s, vk, mods) => { s.HotkeySavePreset2VK = vk; s.HotkeySavePreset2Mods = mods; },
                    GetEnabled  = s => s.HotkeySavePreset2Enabled,
                    SetEnabled  = (s, v) => s.HotkeySavePreset2Enabled = v,
                    EditVK      = _s.HotkeySavePreset2VK,
                    EditMods    = _s.HotkeySavePreset2Mods,
                },
                new HotkeyDef
                {
                    ActionName  = "Save to Preset 3",
                    Description = "Capture current settings into preset slot 3, overwriting any existing data.",
                    GetLabel    = s => FormatHotkey(s.HotkeySavePreset3Mods, s.HotkeySavePreset3VK),
                    SetKeys     = (s, vk, mods) => { s.HotkeySavePreset3VK = vk; s.HotkeySavePreset3Mods = mods; },
                    GetEnabled  = s => s.HotkeySavePreset3Enabled,
                    SetEnabled  = (s, v) => s.HotkeySavePreset3Enabled = v,
                    EditVK      = _s.HotkeySavePreset3VK,
                    EditMods    = _s.HotkeySavePreset3Mods,
                },
                new HotkeyDef
                {
                    ActionName  = "Save to Preset 4",
                    Description = "Capture current settings into preset slot 4, overwriting any existing data.",
                    GetLabel    = s => FormatHotkey(s.HotkeySavePreset4Mods, s.HotkeySavePreset4VK),
                    SetKeys     = (s, vk, mods) => { s.HotkeySavePreset4VK = vk; s.HotkeySavePreset4Mods = mods; },
                    GetEnabled  = s => s.HotkeySavePreset4Enabled,
                    SetEnabled  = (s, v) => s.HotkeySavePreset4Enabled = v,
                    EditVK      = _s.HotkeySavePreset4VK,
                    EditMods    = _s.HotkeySavePreset4Mods,
                },
                new HotkeyDef
                {
                    ActionName  = "Save to Preset 5",
                    Description = "Capture current settings into preset slot 5, overwriting any existing data.",
                    GetLabel    = s => FormatHotkey(s.HotkeySavePreset5Mods, s.HotkeySavePreset5VK),
                    SetKeys     = (s, vk, mods) => { s.HotkeySavePreset5VK = vk; s.HotkeySavePreset5Mods = mods; },
                    GetEnabled  = s => s.HotkeySavePreset5Enabled,
                    SetEnabled  = (s, v) => s.HotkeySavePreset5Enabled = v,
                    EditVK      = _s.HotkeySavePreset5VK,
                    EditMods    = _s.HotkeySavePreset5Mods,
                },
            };
        }

        private static string FormatHotkey(uint mods, uint vk)
        {
            var parts = new System.Collections.Generic.List<string>();
            if ((mods & 0x0002) != 0) parts.Add("Ctrl");
            if ((mods & 0x0001) != 0) parts.Add("Alt");
            if ((mods & 0x0004) != 0) parts.Add("Shift");
            if ((mods & 0x0008) != 0) parts.Add("Win");
            if (vk >= 0x41 && vk <= 0x5A) parts.Add(((char)vk).ToString().ToUpperInvariant());
            else if (vk >= 0x30 && vk <= 0x39) parts.Add(((char)vk).ToString()); // digit keys 0-9
            else if (vk >= 0x70 && vk <= 0x87) parts.Add("F" + (vk - 0x6F));
            else if (vk == 0) return "(none)";
            else parts.Add("0x" + vk.ToString("X2"));
            return string.Join("+", parts);
        }

        // ── Build rows ────────────────────────────────────────────────────────

        private void BuildRows()
        {
            HotkeyRowsPanel.Children.Clear();

            foreach (var def in _defs)
            {
                var row = BuildRow(def);
                HotkeyRowsPanel.Children.Add(row);

                // Separator
                HotkeyRowsPanel.Children.Add(new Separator
                {
                    Margin = new Thickness(0),
                    Background = (Brush)FindResource("BrushSurf2")
                });
            }
        }

        private UIElement BuildRow(HotkeyDef def)
        {
            var grid = new Grid { Margin = new Thickness(16, 10, 16, 10) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });   // capture button
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });   // clear button
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });   // enable checkbox

            // Action label
            var labelStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            labelStack.Children.Add(new TextBlock
            {
                Text       = def.ActionName,
                FontSize   = 13,
                Foreground = (Brush)FindResource("BrushText"),
            });
            if (!string.IsNullOrEmpty(def.Description))
                labelStack.Children.Add(new TextBlock
                {
                    Text       = def.Description,
                    FontSize   = Controls.TypeRamp.Caption,
                    Foreground = (Brush)FindResource("BrushMuted"),
                    TextWrapping = TextWrapping.Wrap,
                });
            Grid.SetColumn(labelStack, 0);
            grid.Children.Add(labelStack);

            // Key capture button
            var captureBtn = new Button
            {
                Content    = def.GetLabel(_s),
                FontFamily = new FontFamily("Consolas"),
                FontSize   = 12,
                Foreground = (Brush)FindResource("BrushBlueText"),
                Background = (Brush)FindResource("BrushSurf"),
                BorderBrush = (Brush)FindResource("BrushSurf2"),
                BorderThickness = new Thickness(1),
                Padding    = new Thickness(16, 6, 16, 6),
                MinWidth   = 120,
                MinHeight  = 44,   // WCAG 2.5.5 — 44 px target
                Margin     = new Thickness(10, 0, 6, 0),
                Tag        = def,
                Style      = TryFindResource("FlatButton") as Style,   // FlatButton honours BorderBrush since 2026-09-25 (focus ring + hover outline)
            };
            captureBtn.Click += CaptureBtn_Click;
            Grid.SetColumn(captureBtn, 1);
            grid.Children.Add(captureBtn);

            // × clear button
            var clearBtn = new Button
            {
                Content    = "×",
                FontSize   = 14,
                FontWeight = FontWeights.Bold,
                Width      = 44,    // WCAG 2.5.5
                MinHeight  = 44,
                Background = (Brush)FindResource("BrushSurf"),
                BorderBrush = (Brush)FindResource("BrushSurf2"),
                BorderThickness = new Thickness(1),
                Foreground = (Brush)FindResource("BrushMuted"),
                Tag        = def,
                Style      = TryFindResource("FlatButton") as Style,
                ToolTip    = "Clear this shortcut (unbind)",
            };
            AutomationProperties.SetName(clearBtn, $"Clear shortcut for {def.ActionName}");
            clearBtn.Click += ClearBtn_Click;
            Grid.SetColumn(clearBtn, 2);
            grid.Children.Add(clearBtn);

            // Enabled checkbox — separates "I bound this chord" from
            // "I want it active right now". A user can save a chord
            // for later (or temporarily disable a conflicting binding)
            // without losing the assignment.
            var enableChk = new CheckBox
            {
                IsChecked         = def.GetEnabled(_s),
                Content           = "On",
                Margin            = new Thickness(12, 0, 0, 0),
                MinHeight         = 44,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground        = (Brush)FindResource("BrushText"),
                Tag               = def,
                ToolTip           = "Enable or disable this shortcut without losing the assigned keys",
            };
            AutomationProperties.SetName(enableChk, $"Enable {def.ActionName} shortcut");
            AutomationProperties.SetHelpText(enableChk,
                "Toggles whether this shortcut is currently active. The assigned keys are preserved either way.");
            enableChk.Checked   += EnableChk_Changed;
            enableChk.Unchecked += EnableChk_Changed;
            Grid.SetColumn(enableChk, 3);
            grid.Children.Add(enableChk);

            return grid;
        }

        private void EnableChk_Changed(object sender, RoutedEventArgs e)
        {
            var chk = (CheckBox)sender;
            var def = (HotkeyDef)chk.Tag;
            def.SetEnabled(_s, chk.IsChecked == true);
        }

        // ── Key capture ───────────────────────────────────────────────────────

        private void CaptureBtn_Click(object sender, RoutedEventArgs e)
        {
            if (_capturingButton != null)
            {
                // Cancel previous capture
                _capturingButton.Content    = ((HotkeyDef)_capturingButton.Tag).GetLabel(_s);
                _capturingButton.Background = (Brush)FindResource("BrushSurf");
                _capturingButton.Foreground = (Brush)FindResource("BrushBlueText");
            }

            _capturingButton = (Button)sender;
            _capturingDef    = (HotkeyDef)_capturingButton.Tag;

            _capturingButton.Content    = "Press keys…";
            _capturingButton.Background = new SolidColorBrush(Color.FromRgb(0x1A, 0x3A, 0x5C));
            _capturingButton.Foreground = Brushes.White;

            Keyboard.Focus(this);
        }

        private void ClearBtn_Click(object sender, RoutedEventArgs e)
        {
            var def = (HotkeyDef)((Button)sender).Tag;
            def.EditVK   = 0;
            def.EditMods = 0;
            def.SetKeys(_s, 0, 0);
            // Clearing the chord also disables the binding so a stale
            // VK=0 entry doesn't keep "enabled" flagged on disk.
            def.SetEnabled(_s, false);
            BuildRows();
        }

        private void OnKeyDown(object sender, KeyEventArgs e)
        {
            if (_capturingButton == null) return;

            e.Handled = true;

            if (e.Key == Key.Escape)
            {
                // Unbind
                _capturingDef.EditVK   = 0;
                _capturingDef.EditMods = 0;
                _capturingDef.SetKeys(_s, 0, 0);
                _capturingDef.SetEnabled(_s, false);
                _capturingButton = null;
                _capturingDef    = null;
                BuildRows();
                return;
            }

            // Ignore modifier-only keystrokes
            if (e.Key == Key.LeftCtrl || e.Key == Key.RightCtrl ||
                e.Key == Key.LeftAlt  || e.Key == Key.RightAlt  ||
                e.Key == Key.LeftShift|| e.Key == Key.RightShift||
                e.Key == Key.LWin     || e.Key == Key.RWin)
                return;

            // Build mods
            uint mods = 0;
            if (Keyboard.IsKeyDown(Key.LeftCtrl)  || Keyboard.IsKeyDown(Key.RightCtrl))  mods |= 0x0002;
            if (Keyboard.IsKeyDown(Key.LeftAlt)   || Keyboard.IsKeyDown(Key.RightAlt))   mods |= 0x0001;
            if (Keyboard.IsKeyDown(Key.LeftShift) || Keyboard.IsKeyDown(Key.RightShift)) mods |= 0x0004;
            if (Keyboard.IsKeyDown(Key.LWin)      || Keyboard.IsKeyDown(Key.RWin))       mods |= 0x0008;

            uint vk = (uint)KeyInterop.VirtualKeyFromKey(e.Key);

            // Pre-flight conflict check — try to register the chord right
            // now against this window. If RegisterHotKey fails, the chord
            // is owned by Windows itself or another running app. We
            // immediately unregister; the goal is just to detect the
            // conflict before the user dismisses the dialog. Avoids the
            // "I set it, but it doesn't work" surprise.
            string conflictWarning = null;
            try
            {
                var src = HwndSource.FromHwnd(new System.Windows.Interop.WindowInteropHelper(this).Handle);
                if (src != null)
                {
                    using var probe = new GlobalHotkey(src, mods, vk);
                    if (!probe.IsRegistered)
                        conflictWarning = "This shortcut is already in use by Windows or another app. " +
                                          "It may not work — pick a different combination.";
                }
            }
            catch { /* swallow: probe is best-effort, real registration happens on Apply */ }

            _capturingDef.EditVK   = vk;
            _capturingDef.EditMods = mods;
            _capturingDef.SetKeys(_s, vk, mods);
            // Auto-enable the binding — the original UI required the
            // user to flip a separate Enabled flag that didn't exist
            // in this dialog, leaving newly-captured chords inactive.
            // Capturing a chord IS the user opting in; if they wanted
            // the chord cleared they'd press × or Escape.
            _capturingDef.SetEnabled(_s, true);

            _capturingButton = null;
            _capturingDef    = null;
            BuildRows();

            if (conflictWarning != null)
            {
                System.Windows.MessageBox.Show(this,
                    conflictWarning,
                    "Hotkey conflict",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }

        // ── Footer buttons ────────────────────────────────────────────────────

        private void BtnResetAll_Click(object sender, RoutedEventArgs e)
        {
            var r = System.Windows.MessageBox.Show(
                "Reset all hotkeys to factory defaults?",
                "Reset Hotkeys", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (r != MessageBoxResult.Yes) return;

            // Restore _s hotkey properties to factory values first; BuildDefs
            // reads from _s, so without this the "reset" was a no-op.
            _s.ResetHotkeyDefaults();
            BuildDefs();
            BuildRows();
        }

        private void BtnApply_Click(object sender, RoutedEventArgs e)
        {
            _s.Save();
            DialogResult = true;
            Close();
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            _snapshot?.Restore(_s);   // revert any live _s mutations from capture/clear/enable
            DialogResult = false;
            Close();
        }

        // ── Window size persistence ───────────────────────────────────────────
        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            var (w, h) = WindowSizeCache.Get("HotkeysWindow", 720, 640);
            Width = w; Height = h;
        }

        protected override void OnClosed(EventArgs e)
        {
            WindowSizeCache.Set("HotkeysWindow", Width, Height);
            base.OnClosed(e);
        }
    }
}
