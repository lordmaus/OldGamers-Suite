using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using WinForms = System.Windows.Forms;

namespace ReticleOverlay
{
    public partial class MainWindow : Window
    {
        private OverlayWindow  _overlay;
        private AppPrefs       _prefs   = AppPrefs.Instance;
        private List<ReticleProfile> _profiles = new List<ReticleProfile>();
        private readonly UpdateSuppressor _suppress = new UpdateSuppressor();
        private bool           _reallyClose = false;
        private DispatcherTimer _statusTimer;
        private string _statusMsg = "";
        private HotkeyManager  _hotkeys;
        private TrayIcon       _tray;
        private bool _isDirty = false;
        private DispatcherTimer _previewTimer;
        private DispatcherTimer _overlayTimer;   // debounce overlay redraws at 16 ms (60 Hz cap)
        private DispatcherTimer _autoSwitchTimer; // Feature #1 / Bug #3 (2026-09-07 review): per-game profile auto-switch
        private readonly UndoManager _undo = new UndoManager();
        private readonly Button[] _paletteBtns = new Button[8];

        // ── UI Scale cycle ────────────────────────────────────────────────────
        private static readonly (string Label, double Scale)[] UiScales =
        {
            ("Compact (0.75×)",    0.75),
            ("Standard (1.0×)",   1.00),
            ("Medium (1.25×)",    1.25),
            ("Large (1.5×)",      1.50),
            ("Xtra Large (2.0×)", 2.00)
        };
        private int _uiScaleIndex = 1; // default: Standard

        public MainWindow()
        {
            using (_suppress.Enter()) InitializeComponent();

            _statusTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            _statusTimer.Tick += (s, e) => { _statusMsg = ""; _statusTimer.Stop(); UpdateStatus(); };
            _previewTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
            _previewTimer.Tick += (s, e) => { _previewTimer.Stop(); DrawPreview(); };
            // Debounce overlay redraws: coalesce rapid hotkey nudges at 16 ms (60 Hz cap).
            _overlayTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
            _overlayTimer.Tick += (s, e) => { _overlayTimer.Stop(); _overlay?.Redraw(_prefs); };
            _prefs.ReticlePropertyChanged += (s, e) =>
            {
                if (_suppress.IsActive) return;
                MarkDirty();
                _overlayTimer.Stop(); _overlayTimer.Start();   // debounced redraw
                DrawPreview();
                UpdateStatus();
            };

            Loaded      += MainWindow_Loaded;
            SizeChanged += (s, e) => { _previewTimer.Stop(); _previewTimer.Start(); };
            CommandBindings.Add(new CommandBinding(ApplicationCommands.Undo, (s, e) => ExecuteUndo()));
            CommandBindings.Add(new CommandBinding(ApplicationCommands.Redo, (s, e) => ExecuteRedo()));
            // WCAG 2.2 SC 3.2.6 — F1 opens Hotkeys dialog from anywhere in the window
            CommandBindings.Add(new CommandBinding(ApplicationCommands.Help,
                (s, e) => { BtnHotkeys_Click(s, e); e.Handled = true; }));
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            _prefs.ApplyTo(this);
            ApplyUiScale(_prefs.UiScale);

            using (_suppress.Enter())
            {
                _profiles = ProfileStore.Load();
                RefreshProfileCombo();
                PopulateMonitors();
                ApplyPrefsToUI();
            }

            _overlay = new OverlayWindow();
            _overlay.Show();
            _overlay.SetCaptureHidden(_prefs.HideFromCapture);

            _tray = new TrayIcon();
            _tray.ToggleOverlay += (s, _) => ToggleReticle();
            _tray.ShowWindow    += (s, _) => BringToFront();
            _tray.ExitApp       += (s, _) => { _reallyClose = true; Application.Current.Shutdown(); };
            _tray.UpdateToggleLabel(_prefs.ReticleVisible);
            UpdateTrayTooltip();

            _hotkeys = new HotkeyManager();
            _hotkeys.Attach(this);
            RegisterHotkeys();

            // RO-05: tell the user once if prefs/profiles were unreadable and where the file was kept.
            if (CorruptFile.SetAsideThisSession.Count > 0)
            {
                Dispatcher.InvokeAsync(() => AppDialog.Show(this,
                    "Some saved data could not be read, so Reticle Overlay set it aside and started with defaults:\n\n" +
                    string.Join("\n", CorruptFile.SetAsideThisSession) +
                    "\n\nThe original file is kept there unchanged.",
                    "Saved data could not be read", MessageBoxButton.OK, MessageBoxImage.Information),
                    DispatcherPriority.ApplicationIdle);
            }

            PositionOverlay();
            // High #1 (2026-09-07 review): resolution change, monitor unplug/replug, or a
            // DPI change while running (e.g. alt-tabbing out of a game that changed video
            // mode) otherwise leaves the overlay at a stale position/size until the user
            // manually re-picks the monitor. Re-sync on the OS event instead.
            SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;

            _autoSwitchTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            _autoSwitchTimer.Tick += (s, e) => CheckAutoSwitch();
            _autoSwitchTimer.Start();
            UpdateBtnToggle();
            UpdatePreviewBackground();
            UpdatePresetButtonLabels();
            BuildPaletteRow();
            UpdateUndoRedoButtons();
            UpdateLayer2State();
            UpdateThemeButton();

            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(DrawPreview));
        }

        // ── Section 1: UI Scale ───────────────────────────────────────────────
        private void BtnUiScale_Click(object s, RoutedEventArgs e)
        {
            bool reverse = Keyboard.IsKeyDown(Key.LeftShift) || Keyboard.IsKeyDown(Key.RightShift);
            _uiScaleIndex = reverse
                ? (_uiScaleIndex - 1 + UiScales.Length) % UiScales.Length
                : (_uiScaleIndex + 1) % UiScales.Length;
            var (label, scale) = UiScales[_uiScaleIndex];
            BtnUiScale.Content      = $"⊞ {label} ({scale:0.0#}×)";
            UiScaleTransform.ScaleX = scale;
            UiScaleTransform.ScaleY = scale;
            _prefs.UiScale          = label;
        }

        private void ApplyUiScale(string label)
        {
            for (int i = 0; i < UiScales.Length; i++)
            {
                if (UiScales[i].Label == label)
                {
                    _uiScaleIndex           = i;
                    UiScaleTransform.ScaleX = UiScales[i].Scale;
                    UiScaleTransform.ScaleY = UiScales[i].Scale;
                    BtnUiScale.Content      = $"⊞ {label} ({UiScales[i].Scale:0.0#}×)";
                    return;
                }
            }
            _uiScaleIndex           = 1;
            UiScaleTransform.ScaleX = 1.0;
            UiScaleTransform.ScaleY = 1.0;
            BtnUiScale.Content      = "⊞ Standard (1.0×)";
            _prefs.UiScale          = UiScales[1].Label;   // persist corrected label so fallback never re-fires
        }

        // ── Resize handle ─────────────────────────────────────────────────────
        private void ResizeHandle_MouseLeftButtonDown(object s, MouseButtonEventArgs e)
        {
            var helper = new WindowInteropHelper(this);
            NativeMethods.ReleaseCapture();
            NativeMethods.SendMessage(helper.Handle, NativeMethods.WM_NCLBUTTONDOWN,
                new IntPtr(NativeMethods.HT_BOTTOMRIGHT), IntPtr.Zero);
        }

        // WCAG 2.5.7 — keyboard alternative for the drag-resize handle.
        // Arrow keys resize in 10 px increments; Shift+Arrow = 1 px fine-tune.
        private void ResizeHandle_KeyDown(object s, KeyEventArgs e)
        {
            double step = (e.KeyboardDevice.Modifiers & ModifierKeys.Shift) != 0 ? 1 : 10;
            switch (e.Key)
            {
                case Key.Right: Width  = Math.Max(MinWidth,  Width  + step); e.Handled = true; break;
                case Key.Left:  Width  = Math.Max(MinWidth,  Width  - step); e.Handled = true; break;
                case Key.Down:  Height = Math.Max(MinHeight, Height + step); e.Handled = true; break;
                case Key.Up:    Height = Math.Max(MinHeight, Height - step); e.Handled = true; break;
            }
        }

        // ── Section 2: Nav ────────────────────────────────────────────────────
        private void BtnHotkeys_Click(object s, RoutedEventArgs e)
        {
            var dlg = new HotkeysDialog(_prefs, () => HotkeyFailures) { Owner = this };
            dlg.HotkeysChanged += (_, __) => RegisterHotkeys();
            dlg.ShowDialog();
        }

        // ── Populate UI ───────────────────────────────────────────────────────
        private void ApplyPrefsToUI()
        {
            foreach (ComboBoxItem item in CmbReticleType.Items)
                if (item.Tag?.ToString() == _prefs.ReticleType) { CmbReticleType.SelectedItem = item; break; }
            if (CmbReticleType.SelectedItem == null) CmbReticleType.SelectedIndex = 0;
            PanelCustomImage.Visibility = (_prefs.ReticleType == "CustomPNG" || _prefs.ReticleType == "Custom")
                ? Visibility.Visible : Visibility.Collapsed;
            TxtCustomPath.Text = string.IsNullOrEmpty(_prefs.CustomImagePath) ? "No file selected" : _prefs.CustomImagePath;

            SldrSize.Value     = _prefs.ReticleSize;      LblSize.Text     = ((int)_prefs.ReticleSize)     + " px";
            SldrThick.Value    = _prefs.ReticleThickness; LblThick.Text    = _prefs.ReticleThickness.ToString("F1") + " px";
            SldrGap.Value      = _prefs.CentreGap;        LblGap.Text      = ((int)_prefs.CentreGap)       + "%";
            SldrRotation.Value = _prefs.Rotation;         LblRotation.Text = ((int)_prefs.Rotation)        + "°";

            TxtMainColor.Text    = _prefs.ReticleColor;  UpdateSwatch(SwatchMain,    _prefs.ReticleColor);
            TxtColor2.Text       = _prefs.ReticleColor2; UpdateSwatch(SwatchColor2,  _prefs.ReticleColor2);
            TxtOutlineColor.Text = _prefs.OutlineColor;  UpdateSwatch(SwatchOutline, _prefs.OutlineColor);
            ChkUseColor2.IsChecked = _prefs.UseColor2;
            ChkOutline.IsChecked   = _prefs.ShowOutline;
            SldrOpacity.Value      = _prefs.Opacity;
            LblOpacity.Text        = ((int)(_prefs.Opacity * 100)) + "%";

            SldrOffsetX.Value = _prefs.OffsetX; LblOffsetX.Text = ((int)_prefs.OffsetX) + " px";
            SldrOffsetY.Value = _prefs.OffsetY; LblOffsetY.Text = ((int)_prefs.OffsetY) + " px";

            if (_prefs.MonitorIndex < CmbMonitor.Items.Count)
                CmbMonitor.SelectedIndex = _prefs.MonitorIndex;
            else if (CmbMonitor.Items.Count > 0)
                CmbMonitor.SelectedIndex = 0;
            UpdateColor2State();
            ChkLayer2.IsChecked = _prefs.Layer2Enabled;
            foreach (ComboBoxItem item in CmbLayer2Type.Items)
                if (item.Tag?.ToString() == _prefs.Layer2Type) { CmbLayer2Type.SelectedItem = item; break; }
            if (CmbLayer2Type.SelectedItem == null) CmbLayer2Type.SelectedIndex = 2;
            SldrLayer2Size.Value    = _prefs.Layer2Size;      LblLayer2Size.Text    = ((int)_prefs.Layer2Size) + " px";
            SldrLayer2Thick.Value   = _prefs.Layer2Thickness; LblLayer2Thick.Text   = _prefs.Layer2Thickness.ToString("F1") + " px";
            SldrLayer2Opacity.Value = _prefs.Layer2Opacity;   LblLayer2Opacity.Text = ((int)(_prefs.Layer2Opacity * 100)) + "%";
            TxtLayer2Color.Text     = _prefs.Layer2Color;     UpdateSwatch(SwatchLayer2, _prefs.Layer2Color);
            UpdateLayer2State();
            RefreshPaletteSwatches();
        }

        // ── Monitor ───────────────────────────────────────────────────────────
        // ── Per-game auto-switch (Feature #1 / Bug #3, 2026-09-07 review) ────────
        private void TxtProcessName_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            if (_suppress.IsActive) return;
            int idx = CmbProfiles.SelectedIndex;
            if (idx < 0 || idx >= _profiles.Count) return;
            _profiles[idx].ProcessName = TxtProcessName.Text?.Trim() ?? "";
            ProfileStore.Save(_profiles);
            using (_suppress.Enter()) CmbProfiles.Items.Refresh();   // bound-.exe icon/label in the profile list
        }

        private void CheckAutoSwitch()
        {
            if (!_prefs.AutoSwitchProfiles || _profiles.Count == 0) return;
            try
            {
                nint hwnd = NativeMethods.GetForegroundWindow();
                if (hwnd == 0) return;
                NativeMethods.GetWindowThreadProcessId(hwnd, out uint pid);
                if (pid == 0) return;
                string exe = System.Diagnostics.Process.GetProcessById((int)pid).ProcessName + ".exe";

                int match = _profiles.FindIndex(p => !string.IsNullOrWhiteSpace(p.ProcessName) &&
                    (p.ProcessName.Equals(exe, StringComparison.OrdinalIgnoreCase) ||
                     p.ProcessName.Equals(exe[..^4], StringComparison.OrdinalIgnoreCase)));
                if (match < 0 || match == CmbProfiles.SelectedIndex) return;

                CmbProfiles.SelectedIndex = match;
                ApplyProfile(_profiles[match]);
            }
            catch { /* foreground window may belong to an elevated/protected process — skip this tick */ }
        }

        private void OnDisplaySettingsChanged(object sender, EventArgs e)
        {
            // SystemEvents can fire off the UI thread; marshal back before touching
            // controls. Reuses the same refresh path as the manual "Refresh" button so
            // MonitorIndex clamps and _prefs updates identically either way.
            Dispatcher.BeginInvoke(new Action(() =>
            {
                PopulateMonitors();
                PositionOverlay();
            }));
        }

        private void PopulateMonitors()
        {
            CmbMonitor.Items.Clear();
            var screens = WinForms.Screen.AllScreens;
            for (int i = 0; i < screens.Length; i++)
            {
                var sc = screens[i];
                CmbMonitor.Items.Add($"Monitor {i + 1}{(sc.Primary ? " (Primary)" : "")} — {sc.Bounds.Width}×{sc.Bounds.Height}");
            }
            CmbMonitor.SelectedIndex = Math.Max(0, Math.Min(_prefs.MonitorIndex, screens.Length - 1));
        }

        private void PositionOverlay()
        {
            if (_overlay == null) return;
            var screens = WinForms.Screen.AllScreens;
            int idx = Math.Max(0, Math.Min(CmbMonitor.SelectedIndex, screens.Length - 1));
            _overlay.PositionOnMonitor(screens[idx], _prefs);
            UpdateStatus();
        }

        private void MarkDirty()
        {
            if (_isDirty) return;
            _isDirty = true;
            UpdateTitle();
        }

        private void ClearDirty()
        {
            _isDirty = false;
            UpdateTitle();
        }

        private void UpdateTitle()
        {
            Title = _isDirty ? "Reticle Overlay  •  ●" : "Reticle Overlay";
        }

        private void UpdateStatus()
        {
            if (LblStatus == null || !IsLoaded) return;
            if (!string.IsNullOrEmpty(_statusMsg)) LblStatus.Text = _statusMsg;   // messages are announced too
            else
            {
                string vis    = _prefs.ReticleVisible ? "Active" : "Hidden";
                string offset = (_prefs.OffsetX != 0 || _prefs.OffsetY != 0)
                                ? $"  •  offset ({(int)_prefs.OffsetX}, {(int)_prefs.OffsetY})" : "";
                LblStatus.Text = $"Overlay {vis}  •  {_prefs.ReticleType}{offset}";
            }
            // Explicitly raise live-region event so Narrator announces the status change.
            // AutomationProperties.LiveSetting="Polite" alone is unreliable in WPF without this.
            var peer = UIElementAutomationPeer.FromElement(LblStatus)
                       ?? UIElementAutomationPeer.CreatePeerForElement(LblStatus);
            peer?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
        }

        // ── Overlay ───────────────────────────────────────────────────────────
        private void RefreshOverlay()
        {
            using (_suppress.Enter())
            {
                _overlay?.Redraw(_prefs);
                DrawPreview();
                UpdateStatus();
            }
        }

        // ── Section 3: Enable / Disable ───────────────────────────────────────
        // (ToggleReticle, UpdateBtnToggle, UpdateTrayTooltip → partials)

        // ── Preview ───────────────────────────────────────────────────────────
        private void DrawPreview()
        {
            if (PreviewCanvas == null) return;
            double w = PreviewCanvas.ActualWidth, h = PreviewCanvas.ActualHeight;
            ReticleRenderer.DrawPreview(PreviewCanvas, w, h, _prefs);
        }

        private void UpdatePreviewBackground()
        {
            if (PreviewBg == null) return;
            switch (_prefs.PreviewBackground)
            {
                case "Light":   PreviewBg.Background = new SolidColorBrush(Color.FromRgb(240, 240, 240)); break;
                case "Checker": PreviewBg.Background = CreateCheckerBrush(); break;
                default:        PreviewBg.Background = new SolidColorBrush(Color.FromRgb(17, 17, 17));   break;
            }
        }

        private static DrawingBrush CreateCheckerBrush()
        {
            var dg = new DrawingGroup();
            dg.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromRgb(80, 80, 80)), null, new RectangleGeometry(new Rect(0, 0, 20, 20))));
            dg.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromRgb(50, 50, 50)), null,
                new GeometryGroup { Children = { new RectangleGeometry(new Rect(0, 0, 10, 10)), new RectangleGeometry(new Rect(10, 10, 10, 10)) } }));
            return new DrawingBrush(dg) { TileMode = TileMode.Tile, Viewport = new Rect(0, 0, 20, 20), ViewportUnits = BrushMappingMode.Absolute };
        }

        // ── Profiles ─────────────────────────────────────────────────────────
        private void RefreshProfileCombo(int selectIndex = -1)
        {
            using (_suppress.Enter())
            {
                int keep = selectIndex >= 0 ? selectIndex : CmbProfiles.SelectedIndex;
                CmbProfiles.Items.Clear();
                foreach (var p in _profiles) CmbProfiles.Items.Add(p);   // ItemTemplate shows name + bound .exe
                if (_profiles.Count > 0)
                    CmbProfiles.SelectedIndex = Math.Max(0, Math.Min(keep, _profiles.Count - 1));
            }
        }

        private void ApplyProfile(ReticleProfile p)
        {
            _prefs.ReticleType=p.ReticleType; _prefs.CustomImagePath=p.CustomImagePath;
            _prefs.ReticleSize=p.ReticleSize; _prefs.ReticleThickness=p.ReticleThickness;
            _prefs.CentreGap=p.CentreGap;     _prefs.Rotation=p.Rotation;
            _prefs.ReticleColor=p.ReticleColor; _prefs.ReticleColor2=p.ReticleColor2;
            _prefs.UseColor2=p.UseColor2;      _prefs.OutlineColor=p.OutlineColor;
            _prefs.ShowOutline=p.ShowOutline;  _prefs.Opacity=p.Opacity;
            _prefs.OffsetX=p.OffsetX;         _prefs.OffsetY=p.OffsetY;
            _prefs.Layer2Enabled=p.Layer2Enabled; _prefs.Layer2Type=p.Layer2Type;
            _prefs.Layer2Size=p.Layer2Size;    _prefs.Layer2Thickness=p.Layer2Thickness;
            _prefs.Layer2CentreGap=p.Layer2CentreGap; _prefs.Layer2Rotation=p.Layer2Rotation;
            _prefs.Layer2Color=p.Layer2Color;  _prefs.Layer2Opacity=p.Layer2Opacity;
            using (_suppress.Enter())
            {
                ApplyPrefsToUI();
                if (TxtProcessName != null) TxtProcessName.Text = p.ProcessName ?? "";
            }
            RefreshOverlay();
            _prefs.Save();
            ClearDirty();
            _undo.ClearUndo();
            UpdateTrayTooltip();
            _tray?.ShowBalloon("Reticle Overlay", $"Profile: {p.Name}");
        }

        private ReticleProfile SnapshotProfile(string name) => new ReticleProfile
        {
            Name=name, ReticleType=_prefs.ReticleType, CustomImagePath=_prefs.CustomImagePath,
            ReticleSize=_prefs.ReticleSize, ReticleThickness=_prefs.ReticleThickness,
            CentreGap=_prefs.CentreGap, Rotation=_prefs.Rotation,
            ReticleColor=_prefs.ReticleColor, ReticleColor2=_prefs.ReticleColor2,
            UseColor2=_prefs.UseColor2, OutlineColor=_prefs.OutlineColor,
            ShowOutline=_prefs.ShowOutline, Opacity=_prefs.Opacity,
            OffsetX=_prefs.OffsetX, OffsetY=_prefs.OffsetY,
            Layer2Enabled=_prefs.Layer2Enabled, Layer2Type=_prefs.Layer2Type,
            Layer2Size=_prefs.Layer2Size, Layer2Thickness=_prefs.Layer2Thickness,
            Layer2CentreGap=_prefs.Layer2CentreGap, Layer2Rotation=_prefs.Layer2Rotation,
            Layer2Color=_prefs.Layer2Color, Layer2Opacity=_prefs.Layer2Opacity,
            ProcessName=TxtProcessName?.Text?.Trim() ?? ""
        };

        private static ReticleProfile DuplicateProfile(ReticleProfile src, string name) => new ReticleProfile
        {
            Name=name, ReticleType=src.ReticleType, CustomImagePath=src.CustomImagePath,
            ReticleSize=src.ReticleSize, ReticleThickness=src.ReticleThickness,
            CentreGap=src.CentreGap, Rotation=src.Rotation,
            ReticleColor=src.ReticleColor, ReticleColor2=src.ReticleColor2,
            UseColor2=src.UseColor2, OutlineColor=src.OutlineColor,
            ShowOutline=src.ShowOutline, Opacity=src.Opacity,
            OffsetX=src.OffsetX, OffsetY=src.OffsetY,
            Layer2Enabled=src.Layer2Enabled, Layer2Type=src.Layer2Type,
            Layer2Size=src.Layer2Size, Layer2Thickness=src.Layer2Thickness,
            Layer2CentreGap=src.Layer2CentreGap, Layer2Rotation=src.Layer2Rotation,
            Layer2Color=src.Layer2Color, Layer2Opacity=src.Layer2Opacity,
            ProcessName=src.ProcessName
        };

        // ── Swatch / color helpers ────────────────────────────────────────────
        private void UpdateSwatch(Button sw, string hex)
        {
            sw.Background = ColorUtils.ParseBrush(hex, Colors.Transparent);
            string baseName = sw.Tag?.ToString() ?? AutomationProperties.GetName(sw);
            if (!string.IsNullOrEmpty(hex))
                AutomationProperties.SetName(sw, $"{baseName} — {hex.ToUpperInvariant()}");
        }

        private void OpenColorPicker(string current, Action<string> onPicked)
        {
            using (var dlg = new WinForms.ColorDialog { FullOpen = true })
            {
                try { var c = (Color)ColorConverter.ConvertFromString(current); dlg.Color = System.Drawing.Color.FromArgb(c.A, c.R, c.G, c.B); } catch { }
                if (dlg.ShowDialog() == WinForms.DialogResult.OK)
                { var co = dlg.Color; onPicked($"#{co.R:X2}{co.G:X2}{co.B:X2}"); }
            }
        }

        private void BringToFront()
        { Show(); WindowState = WindowState.Normal; Activate(); Topmost = true; Topmost = false; }

        // ── Toolbar ───────────────────────────────────────────────────────────
        // ── Theme picker dropdown ─────────────────────────────────────────────
        private void BtnTheme_Click(object s, RoutedEventArgs e)
        {
            var ctx = new System.Windows.Controls.ContextMenu
            {
                PlacementTarget = BtnTheme,
                Placement       = System.Windows.Controls.Primitives.PlacementMode.Bottom,
            };
            string cur = App.ThemeName ?? "Dark";

            var cats = new (string Cat, (string Key, string Disp)[] Items)[]
            {
                ("Dark", new[] { ("Dark", "Dark"), ("OneDark", "One Dark"), ("GruvboxDark", "Gruvbox Dark"), ("TokyoNight", "Tokyo Night"), ("Nord", "Nord"), ("Dracula", "Dracula"), ("CatppuccinMocha", "Catppuccin Mocha"), ("RosePine", "Rosé Pine"), ("AyuDark", "Ayu Dark"), ("AyuMirage", "Ayu Mirage"), ("Cobalt2", "Cobalt 2"), ("Monokai", "Monokai"), ("SolarizedDark", "Solarized Dark"), ("Kanagawa", "Kanagawa"), ("EverforestDark", "Everforest Dark"), ("Oxocarbon", "Oxocarbon"), ("Palenight", "Palenight"), ("GitHubDark", "GitHub Dark"), ("DraculaPro", "Dracula Pro"), ("Horizon", "Horizon"), ("Abyss", "Abyss"), ("Obsidian", "Obsidian"), ("Panda", "Panda"), ("Monochrome", "Monochrome") }),
                ("Light", new[] { ("Light", "Light"), ("WarmLight", "Warm Light"), ("CatppuccinLatte", "Catppuccin Latte"), ("GitHubLight", "GitHub Light"), ("AyuLight", "Ayu Light"), ("SolarizedLight", "Solarized Light"), ("RosePineDawn", "Rose Pine Dawn"), ("EverforestLight", "Everforest Light"), ("PaperWhite", "Paper White"), ("Sepia", "Sepia"), ("Arctic", "Arctic"), ("Ivory", "Ivory") }),
                ("Contrast", new[] { ("HighContrast", "High Contrast"), ("HighContrastLight", "HC Light"), ("HCBlue", "HC Blue"), ("HCGreen", "HC Green"), ("HCAmber", "HC Amber") }),
                ("Neon", new[] { ("Synthwave", "Synthwave"), ("Cyberpunk", "Cyberpunk"), ("NeonCity", "Neon City"), ("VaporWave", "VaporWave"), ("Outrun", "Outrun"), ("HoloBlue", "Holo Blue"), ("Matrix", "Matrix"), ("NeonPurple", "Neon Purple"), ("LaserGrid", "Laser Grid"), ("ElectricOcean", "Electric Ocean") }),
                ("Nature", new[] { ("Forest", "Forest"), ("Ocean", "Ocean"), ("DesertSand", "Desert Sand"), ("Autumn", "Autumn"), ("MidnightGarden", "Midnight Garden"), ("LavenderField", "Lavender Field"), ("CoralReef", "Coral Reef"), ("Tundra", "Tundra"), ("Volcanic", "Volcanic"), ("Meadow", "Meadow") }),
                ("Retro", new[] { ("C64", "C64"), ("AmberCRT", "Amber CRT"), ("GreenCRT", "Green CRT"), ("IBMBlue", "IBM Blue"), ("AppleII", "Apple II"), ("BIOS", "BIOS"), ("Teletext", "Teletext"), ("TerminalRed", "Terminal Red"), ("VGAPurple", "VGA Purple"), ("ZXSpectrum", "ZX Spectrum") }),
                ("Warm", new[] { ("Ember", "Ember"), ("Rosewood", "Rosewood"), ("GoldenHour", "Golden Hour"), ("Terracotta", "Terracotta"), ("Rust", "Rust"), ("Mahogany", "Mahogany"), ("Cinnamon", "Cinnamon"), ("Wine", "Wine"), ("Copper", "Copper"), ("Sundowner", "Sundowner") }),
                ("Cool", new[] { ("MidnightBlue", "Midnight Blue"), ("DeepSpace", "Deep Space"), ("Glacier", "Glacier"), ("Cobalt", "Cobalt"), ("Sapphire", "Sapphire"), ("Periwinkle", "Periwinkle"), ("ArcticFox", "Arctic Fox"), ("Steel", "Steel"), ("Slate", "Slate"), ("IndigoNight", "Indigo Night") }),
                ("Pastel", new[] { ("CottonCandy", "Cotton Candy"), ("MintCream", "Mint Cream"), ("BabyBlue", "Baby Blue"), ("Peach", "Peach"), ("Lilac", "Lilac"), ("Buttercup", "Buttercup"), ("Blush", "Blush"), ("Seafoam", "Seafoam"), ("Wisteria", "Wisteria"), ("Dusk", "Dusk") }),
                ("Specialty", new[] { ("CoffeeShop", "Coffee Shop"), ("Denim", "Denim"), ("Malachite", "Malachite"), ("SolarFlare", "Solar Flare"), ("Moonstone", "Moonstone"), ("Bubblegum", "Bubblegum") }),
            };

            foreach (var (cat, items) in cats)
            {
                var sub = new System.Windows.Controls.MenuItem
                {
                    Header  = cat,
                    Padding = new Thickness(8, 4, 8, 4),
                };
                foreach (var (k, d) in items)
                {
                    string key = k;
                    bool   act = string.Equals(key, cur, System.StringComparison.OrdinalIgnoreCase);
                    var    mi  = new System.Windows.Controls.MenuItem
                    {
                        Header      = d,
                        IsCheckable = true,
                        IsChecked   = act,
                        FontWeight  = act ? FontWeights.SemiBold : FontWeights.Normal,
                        Padding     = new Thickness(8, 6, 24, 6),
                    };
                    mi.Click += (_, __) => { App.SetTheme(key); _prefs.Save(); };
                    sub.Items.Add(mi);
                }
                ctx.Items.Add(sub);
            }
            ctx.IsOpen = true;
        }

        internal void UpdateThemeButton()
        {
            // Label stays static — active theme shown via checkmark in the dropdown.
            if (BtnTheme != null) BtnTheme.Content = "◑ Theme";
        }
        private void BtnSettings_Click(object s, RoutedEventArgs e)
        {
            var dlg = new SettingsDialog(_prefs) { Owner = this };
            dlg.CaptureHideChanged += (_, hide) => _overlay?.SetCaptureHidden(hide);
            dlg.ResetApplied       += (_, __) => { using (_suppress.Enter()) ApplyPrefsToUI(); ClearDirty(); RefreshOverlay(); };
            dlg.ShowDialog();
        }
        private void BtnAbout_Click(object s, RoutedEventArgs e)
        { var dlg = new AboutDialog { Owner = this }; dlg.ProfilesImported += (_, __) => { _profiles = ProfileStore.Load(); RefreshProfileCombo(); }; dlg.ShowDialog(); }
        private void BtnToggle_Click(object s, RoutedEventArgs e) => ToggleReticle();

        // ── Profile CRUD ─────────────────────────────────────────────────────
        private void CmbProfiles_SelectionChanged(object s, SelectionChangedEventArgs e)
        { if (_suppress.IsActive) return; }

        private void CmbProfiles_KeyDown(object s, KeyEventArgs e)
        {
            if (e.Key == Key.Return || e.Key == Key.Enter)
            { BtnLoadProfile_Click(s, e); e.Handled = true; }
        }

        private void BtnLoadProfile_Click(object s, RoutedEventArgs e)
        { int i = CmbProfiles.SelectedIndex; if (i >= 0 && i < _profiles.Count) ApplyProfile(_profiles[i]); }

        private void BtnSaveProfile_Click(object s, RoutedEventArgs e)
        {
            var dlg = new InputDialog("Save Profile", "Profile name:", "My Game") { Owner = this };
            if (dlg.ShowDialog() != true) return;
            string name = dlg.InputText?.Trim(); if (string.IsNullOrEmpty(name)) return;
            int ex = _profiles.FindIndex(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (ex >= 0)
            {
                var confirm = AppDialog.Show($"A profile named \"{name}\" already exists.\n\nOverwrite it?",
                    "Overwrite Profile", MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (confirm != MessageBoxResult.Yes) return;
            }
            var profile = SnapshotProfile(name);
            int targetIdx;
            if (ex >= 0) { _profiles[ex] = profile; targetIdx = ex; }
            else          { _profiles.Add(profile); targetIdx = _profiles.Count - 1; }
            ProfileStore.Save(_profiles);
            RefreshProfileCombo(targetIdx);
            ClearDirty();
            ShowSaved("Profile saved ✓");
        }

        private void BtnDuplicateProfile_Click(object s, RoutedEventArgs e)
        {
            int idx = CmbProfiles.SelectedIndex; if (idx < 0 || idx >= _profiles.Count) return;
            var src  = _profiles[idx];
            var copy = DuplicateProfile(src, src.Name + " (copy)");
            _profiles.Insert(idx + 1, copy);
            ProfileStore.Save(_profiles);
            RefreshProfileCombo(idx + 1);
        }

        private void BtnDeleteProfile_Click(object s, RoutedEventArgs e)
        {
            int idx = CmbProfiles.SelectedIndex; if (idx < 0 || idx >= _profiles.Count) return;
            if (AppDialog.Show($"Delete \"{_profiles[idx].Name}\"?", "Delete Profile",
               MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            _profiles.RemoveAt(idx);
            ProfileStore.Save(_profiles);
            RefreshProfileCombo(Math.Min(idx, _profiles.Count - 1));
        }

        private void BtnProfileUp_Click(object s, RoutedEventArgs e)
        {
            int idx = CmbProfiles.SelectedIndex; if (idx <= 0 || idx >= _profiles.Count) return;
            var tmp = _profiles[idx]; _profiles[idx] = _profiles[idx - 1]; _profiles[idx - 1] = tmp;
            ProfileStore.Save(_profiles);
            RefreshProfileCombo(idx - 1);
        }

        private void BtnProfileDown_Click(object s, RoutedEventArgs e)
        {
            int idx = CmbProfiles.SelectedIndex; if (idx < 0 || idx >= _profiles.Count - 1) return;
            var tmp = _profiles[idx]; _profiles[idx] = _profiles[idx + 1]; _profiles[idx + 1] = tmp;
            ProfileStore.Save(_profiles);
            RefreshProfileCombo(idx + 1);
        }

        // ── Reticle type ─────────────────────────────────────────────────────
        private void CmbReticleType_SelectionChanged(object s, SelectionChangedEventArgs e)
        {
            if (_suppress.IsActive) return;
            PushUndo();
            var item = CmbReticleType.SelectedItem as ComboBoxItem;
            string type = item?.Tag?.ToString() ?? "Crosshair";
            _prefs.ReticleType = type;
            PanelCustomImage.Visibility = (type == "CustomPNG" || type == "Custom")
                ? Visibility.Visible : Visibility.Collapsed;
        }

        private void BtnBrowseImage_Click(object s, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog { Title = "Select Custom PNG", Filter = "PNG Images|*.png|All Files|*.*", CheckFileExists = true };
            if (ImagePathValidator.IsSafeLocalPath(_prefs.CustomImagePath) && File.Exists(_prefs.CustomImagePath))   // RO-07
                dlg.InitialDirectory = System.IO.Path.GetDirectoryName(_prefs.CustomImagePath);
            if (dlg.ShowDialog(this) == true)
            { _prefs.CustomImagePath = dlg.FileName; TxtCustomPath.Text = dlg.FileName; ReticleRenderer.InvalidateCache(dlg.FileName); }
        }

        // ── Size & geometry ──────────────────────────────────────────────────
        private void SldrSize_ValueChanged(object s, RoutedPropertyChangedEventArgs<double> e)
        { if (_suppress.IsActive) return; PushUndo("Size"); _prefs.ReticleSize = e.NewValue; LblSize.Text = ((int)e.NewValue) + " px"; }
        private void SldrThick_ValueChanged(object s, RoutedPropertyChangedEventArgs<double> e)
        { if (_suppress.IsActive) return; PushUndo("Thickness"); _prefs.ReticleThickness = e.NewValue; LblThick.Text = e.NewValue.ToString("F1") + " px"; }
        private void SldrGap_ValueChanged(object s, RoutedPropertyChangedEventArgs<double> e)
        { if (_suppress.IsActive) return; PushUndo("Centre Gap"); _prefs.CentreGap = e.NewValue; LblGap.Text = ((int)e.NewValue) + "%"; }
        private void SldrRotation_ValueChanged(object s, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_suppress.IsActive) return;
            PushUndo("Rotation");
            _prefs.Rotation = e.NewValue;
            int deg = (int)e.NewValue;
            LblRotation.Text = deg + "°" + (deg <= 5 || deg >= 354 ? " ↺" : "");
        }
        private void BtnResetRotation_Click(object s, RoutedEventArgs e)
        { PushUndo("Reset Rotation"); _prefs.Rotation = 0; using (_suppress.Enter()) { SldrRotation.Value = 0; LblRotation.Text = "0°"; } }

        // ── Colors ───────────────────────────────────────────────────────────
        private void TxtMainColor_TextChanged(object s, TextChangedEventArgs e)
        { if (_suppress.IsActive) return; try { ColorConverter.ConvertFromString(TxtMainColor.Text.Trim()); _prefs.ReticleColor = TxtMainColor.Text.Trim(); UpdateSwatch(SwatchMain, _prefs.ReticleColor); } catch { } }
        private void TxtColor2_TextChanged(object s, TextChangedEventArgs e)
        { if (_suppress.IsActive) return; try { ColorConverter.ConvertFromString(TxtColor2.Text.Trim()); _prefs.ReticleColor2 = TxtColor2.Text.Trim(); UpdateSwatch(SwatchColor2, _prefs.ReticleColor2); } catch { } }
        private void TxtOutlineColor_TextChanged(object s, TextChangedEventArgs e)
        { if (_suppress.IsActive) return; try { ColorConverter.ConvertFromString(TxtOutlineColor.Text.Trim()); _prefs.OutlineColor = TxtOutlineColor.Text.Trim(); UpdateSwatch(SwatchOutline, _prefs.OutlineColor); } catch { } }

        private void TxtColor_LostFocus(object s, RoutedEventArgs e)
        {
            if (_suppress.IsActive) return;
            string label = s == TxtMainColor ? "Reticle color"
                         : s == TxtColor2    ? "Arm color"
                         :                    "Outline color";
            PushUndo(label);
        }

        private void PickColor(string undoLabel, Func<string> getter, Action<string> setter, TextBox txt, Button swatch)
        {
            PushUndo(undoLabel);
            OpenColorPicker(getter(), hex =>
            {
                setter(hex);
                using (_suppress.Enter()) { txt.Text = hex; }
                UpdateSwatch(swatch, hex);
            });
        }

        private void SwatchMain_Click(object s, RoutedEventArgs e)
            => PickColor("Reticle color", () => _prefs.ReticleColor, hex => _prefs.ReticleColor = hex, TxtMainColor, SwatchMain);
        private void SwatchColor2_Click(object s, RoutedEventArgs e)
            => PickColor("Arm color", () => _prefs.ReticleColor2, hex => _prefs.ReticleColor2 = hex, TxtColor2, SwatchColor2);
        private void SwatchOutline_Click(object s, RoutedEventArgs e)
            => PickColor("Outline color", () => _prefs.OutlineColor, hex => _prefs.OutlineColor = hex, TxtOutlineColor, SwatchOutline);
        private void ChkUseColor2_Changed(object s, RoutedEventArgs e)
        {
            if (_suppress.IsActive) return;
            PushUndo();
            _prefs.UseColor2 = ChkUseColor2.IsChecked == true;
            UpdateColor2State();
        }

        private void UpdateColor2State()
        {
            bool enabled = _prefs.UseColor2;
            SwatchColor2.IsEnabled = enabled;
            TxtColor2.IsEnabled    = enabled;
            LblVertArmColor.Opacity = enabled ? 1.0 : 0.40;
            SwatchColor2.Opacity    = enabled ? 1.0 : 0.40;
            TxtColor2.Opacity       = enabled ? 1.0 : 0.40;
        }
        private void ChkOutline_Changed(object s, RoutedEventArgs e)
        { if (_suppress.IsActive) return; PushUndo("Show outline"); _prefs.ShowOutline = ChkOutline.IsChecked == true; }

        private void SldrOpacity_ValueChanged(object s, RoutedPropertyChangedEventArgs<double> e)
        { if (_suppress.IsActive) return; PushUndo("Opacity"); _prefs.Opacity = e.NewValue; LblOpacity.Text = ((int)(e.NewValue * 100)) + "%"; }
        private void BtnOpacityUp_Click(object s, RoutedEventArgs e)   => NudgeOpacityUp();
        private void BtnOpacityDown_Click(object s, RoutedEventArgs e) => NudgeOpacityDown();

        private void CmbMonitor_SelectionChanged(object s, SelectionChangedEventArgs e)
        { if (_suppress.IsActive) return; _prefs.MonitorIndex = Math.Max(0, CmbMonitor.SelectedIndex); PositionOverlay(); }
        private void BtnRefreshMonitors_Click(object s, RoutedEventArgs e)
        { PopulateMonitors(); PositionOverlay(); }

        private void SldrOffsetX_ValueChanged(object s, RoutedPropertyChangedEventArgs<double> e)
        { if (_suppress.IsActive) return; PushUndo("X offset"); _prefs.OffsetX = e.NewValue; LblOffsetX.Text = ((int)e.NewValue) + " px"; }
        private void SldrOffsetY_ValueChanged(object s, RoutedPropertyChangedEventArgs<double> e)
        { if (_suppress.IsActive) return; PushUndo("Y offset"); _prefs.OffsetY = e.NewValue; LblOffsetY.Text = ((int)e.NewValue) + " px"; }
        private void BtnNudgeXLeft_Click(object s, RoutedEventArgs e)  => NudgeOffset(-1, 0);
        private void BtnNudgeXRight_Click(object s, RoutedEventArgs e) => NudgeOffset(1, 0);
        private void BtnNudgeYUp_Click(object s, RoutedEventArgs e)    => NudgeOffset(0, -1);
        private void BtnNudgeYDown_Click(object s, RoutedEventArgs e)  => NudgeOffset(0, 1);
        private void BtnResetOffset_Click(object s, RoutedEventArgs e)
        { _prefs.OffsetX = 0; _prefs.OffsetY = 0; using (_suppress.Enter()) { SldrOffsetX.Value = 0; SldrOffsetY.Value = 0; LblOffsetX.Text = "0 px"; LblOffsetY.Text = "0 px"; } }

        // ── Preview bg ───────────────────────────────────────────────────────
        private void BtnPreviewBg_Click(object s, RoutedEventArgs e)
        { if (s is not Button btn) return; _prefs.PreviewBackground = btn.Tag?.ToString() ?? "Dark"; UpdatePreviewBackground(); }

        // ── Close ────────────────────────────────────────────────────────────
        private void Window_Closing(object s, System.ComponentModel.CancelEventArgs e)
        {
            if (!_reallyClose && _prefs.MinimizeToTrayOnClose)
            { e.Cancel = true; Hide(); _tray?.ShowBalloon("Reticle Overlay", "Still running in the tray. Double-click to restore."); return; }
            SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
            _statusTimer?.Stop();
            _previewTimer?.Stop();
            _overlayTimer?.Stop();
            _autoSwitchTimer?.Stop();
            _prefs.SnapshotFrom(this); _prefs.Save(); ProfileStore.Save(_profiles);
            ClearDirty();
            _overlay?.Close();
            _hotkeys?.Dispose();
            _tray?.Dispose();
        }

        // ── Status / Undo ─────────────────────────────────────────────────────
        // Status messages stay ≥ 5 s (+60 ms/char) — 2 s was too short to read (WCAG 2.2.1).
        private void ShowSaved(string msg)
        {
            _statusMsg = msg;
            _statusTimer.Stop();
            _statusTimer.Interval = TimeSpan.FromMilliseconds(5000 + 60 * (msg?.Length ?? 0));
            UpdateStatus();
            _statusTimer.Start();
        }

        private void PushUndo(string description = "")
        {
            _undo.Push(_prefs, description);
            UpdateUndoRedoButtons();
        }

        private void UpdateUndoRedoButtons() { /* buttons removed; Ctrl+Z/Ctrl+Y still active via CommandBindings */ }

        private void ExecuteUndo()
        {
            if (!_undo.CanUndo) return;
            _undo.Undo(_prefs);
            using (_suppress.Enter()) ApplyPrefsToUI();
            RefreshOverlay();
            MarkDirty();
        }

        private void ExecuteRedo()
        {
            if (!_undo.CanRedo) return;
            _undo.Redo(_prefs);
            using (_suppress.Enter()) ApplyPrefsToUI();
            RefreshOverlay();
            MarkDirty();
        }

        // ── Rename profile ────────────────────────────────────────────────────
        private void BtnRenameProfile_Click(object s, RoutedEventArgs e)
        {
            int idx = CmbProfiles.SelectedIndex;
            if (idx < 0 || idx >= _profiles.Count) return;
            string current = _profiles[idx].Name;
            var dlg = new InputDialog("Rename Profile", "New name:", current) { Owner = this };
            if (dlg.ShowDialog() != true) return;
            string newName = dlg.InputText?.Trim();
            if (string.IsNullOrEmpty(newName) || newName == current) return;
            if (_profiles.FindIndex(p => p.Name.Equals(newName, StringComparison.OrdinalIgnoreCase)) >= 0)
            {
                AppDialog.Show($"A profile named \"{newName}\" already exists.", "Rename", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            _profiles[idx].Name = newName;
            if (_prefs.Preset1 == current) _prefs.Preset1 = newName;
            if (_prefs.Preset2 == current) _prefs.Preset2 = newName;
            if (_prefs.Preset3 == current) _prefs.Preset3 = newName;
            if (_prefs.Preset4 == current) _prefs.Preset4 = newName;
            if (_prefs.Preset5 == current) _prefs.Preset5 = newName;
            ProfileStore.Save(_profiles);
            RefreshProfileCombo(idx);
            UpdatePresetButtonLabels();
            ShowSaved("Profile renamed ✓");
        }

        // ── Palette ───────────────────────────────────────────────────────────
        private void BuildPaletteRow()
        {
            if (PaletteRow == null) return;
            PaletteRow.Items.Clear();
            string[] colors = _prefs.GetPalette();
            for (int i = 0; i < 8; i++)
            {
                int idx = i;
                var btn = new Button
                {
                    Style      = (Style)FindResource("SwatchButton"),
                    Background = new SolidColorBrush(ColorUtils.Parse(colors[i], Colors.Lime)),
                    MinWidth   = 44,
                    MinHeight  = 44,
                    Margin     = new Thickness(0, 0, 4, 0),
                    ToolTip    = "Left-click: apply to reticle color  |  Right-click: save current reticle color here",
                };
                AutomationProperties.SetName(btn, $"Palette color {i + 1}: {colors[i]}");
                btn.Click += (_, __) => ApplyPaletteColor(idx);
                btn.MouseRightButtonUp += (_, __) => SaveToPalette(idx);
                _paletteBtns[i] = btn;
                PaletteRow.Items.Add(btn);
            }
        }

        private void RefreshPaletteSwatches()
        {
            if (_paletteBtns[0] == null) return;
            string[] colors = _prefs.GetPalette();
            for (int i = 0; i < 8; i++)
            {
                if (_paletteBtns[i] == null) continue;
                _paletteBtns[i].Background = new SolidColorBrush(ColorUtils.Parse(colors[i], Colors.Lime));
                AutomationProperties.SetName(_paletteBtns[i], $"Palette color {i + 1}: {colors[i]}");
            }
        }

        private void ApplyPaletteColor(int idx)
        {
            PushUndo();
            string hex = _prefs.GetPalette()[idx];
            _prefs.ReticleColor = hex;
            using (_suppress.Enter()) { TxtMainColor.Text = hex; UpdateSwatch(SwatchMain, hex); }
        }

        private void SaveToPalette(int idx)
        {
            _prefs.SetPalette(idx, _prefs.ReticleColor);
            _prefs.Save();
            RefreshPaletteSwatches();
            ShowSaved($"Saved to palette {idx + 1} ✓");
        }

        // ── Layer 2 ───────────────────────────────────────────────────────────
        private void UpdateLayer2State()
        {
            if (PanelLayer2 == null) return;
            bool on = _prefs.Layer2Enabled;
            PanelLayer2.IsEnabled  = on;
            PanelLayer2.Opacity    = on ? 1.0 : 0.40;
            PanelLayer2.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        }

        private void ChkLayer2_Changed(object s, RoutedEventArgs e)
        {
            if (_suppress.IsActive) return;
            PushUndo();
            _prefs.Layer2Enabled = ChkLayer2.IsChecked == true;
            UpdateLayer2State();
        }

        private void CmbLayer2Type_SelectionChanged(object s, SelectionChangedEventArgs e)
        {
            if (_suppress.IsActive) return;
            var item = CmbLayer2Type.SelectedItem as ComboBoxItem;
            if (item != null) _prefs.Layer2Type = item.Tag?.ToString() ?? "Circle";
        }

        private void SldrLayer2Size_ValueChanged(object s, RoutedPropertyChangedEventArgs<double> e)
        { if (_suppress.IsActive) return; PushUndo(); _prefs.Layer2Size = e.NewValue; LblLayer2Size.Text = ((int)e.NewValue) + " px"; }
        private void SldrLayer2Thick_ValueChanged(object s, RoutedPropertyChangedEventArgs<double> e)
        { if (_suppress.IsActive) return; PushUndo(); _prefs.Layer2Thickness = e.NewValue; LblLayer2Thick.Text = e.NewValue.ToString("F1") + " px"; }
        private void SldrLayer2Opacity_ValueChanged(object s, RoutedPropertyChangedEventArgs<double> e)
        { if (_suppress.IsActive) return; PushUndo(); _prefs.Layer2Opacity = e.NewValue; LblLayer2Opacity.Text = ((int)(e.NewValue * 100)) + "%"; }

        private void SwatchLayer2_Click(object s, RoutedEventArgs e)
            => PickColor("Layer 2 color", () => _prefs.Layer2Color, hex => _prefs.Layer2Color = hex, TxtLayer2Color, SwatchLayer2);

        private void TxtLayer2Color_TextChanged(object s, TextChangedEventArgs e)
        {
            if (_suppress.IsActive) return;
            try
            {
                ColorConverter.ConvertFromString(TxtLayer2Color.Text.Trim());
                _prefs.Layer2Color = TxtLayer2Color.Text.Trim();
                UpdateSwatch(SwatchLayer2, _prefs.Layer2Color);
            }
            catch { }
        }
    }
}
