using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Forms;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using Application = System.Windows.Application;
using BadgeOverlay.ViewModels;

namespace BadgeOverlay
{
    public partial class MainWindow : Window
    {
        // ── Fields ────────────────────────────────────────────────────────────
        private AppSettings          _s;
        private MainWindowViewModel  _vm;

        /// <summary>
        /// Public read-only accessor for the live settings object.
        /// Used by infrastructure code (e.g. <see cref="App.SetTheme"/>)
        /// that needs to honour user preferences (ReduceMotion, etc.)
        /// without tight coupling to MainWindow internals. Never null
        /// after construction completes.
        /// </summary>
        public AppSettings Settings => _s;
        private GlobalHotkey  _hotkeyToggle;
        private GlobalHotkey  _hotkeyFreeze;
        private GlobalHotkey  _hotkeyCopyLabel;
        private GlobalHotkey  _hotkeySpeak;
        // Preset load/save hotkeys — arrays sized 5 (one per slot).
        // Declared here so DisposeRuntimeResources() can null them out
        // without coordinating across partials.
        private GlobalHotkey[] _hotkeyLoadPresets = new GlobalHotkey[5];
        private GlobalHotkey[] _hotkeySavePresets = new GlobalHotkey[5];
        private OverlayWindow _overlay;
        private InfoOverlay   _infoOverlay;
        private NotifyIcon    _tray;

        // UI scaling
        private static readonly double[] UiScales      = { 0.75, 1.0, 1.25, 1.5, 2.0 };
        private static readonly string[] UiScaleLabels = { "Compact", "Standard", "Medium", "Large", "Xtra Large" };
        private int _uiScaleIndex = 0;

        private bool _loading = false;

        // Debounced save
        private readonly System.Windows.Threading.DispatcherTimer _saveTimer =
            new System.Windows.Threading.DispatcherTimer
            { Interval = TimeSpan.FromMilliseconds(UiConstants.SettingsSaveDebounceMs) };

        // ── Constructor ───────────────────────────────────────────────────────
        public MainWindow()
        {
            _loading = true;
            _s = AppSettings.Load();
            _s.PushToDetector();

            // Low-vision: wire app-wide font family + badge font size into
            // the App resources BEFORE InitializeComponent so the very first
            // visual tree pass picks up the user's preferences (avoids a
            // brief Segoe UI flash on startup if they chose OpenDyslexic).
            App.SetAppFontFamily(_s.AppFontFamily);
            App.SetBadgeFontSize(_s.BadgeFontSize);

            App.SetTheme(_s.ThemeName);
            InitializeComponent();
            CursorPreserver.Attach(this);

            Loaded  += OnLoaded;
            Closing += OnClosing;
        }

        // ── Startup ───────────────────────────────────────────────────────────
        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            // Wrap the whole startup sequence so any throw in here still
            // ends with _loading=false. Without the finally, a single
            // failed sub-step (icon load, tray init, OverlayWindow ctor)
            // would silently lock every *_Changed handler and every
            // geometry-save for the remainder of the session.
            try
            {
                TrySetIcon();
                SetupTray();

                _overlay = new OverlayWindow(_s);
                _overlay.Show();

                _infoOverlay = new InfoOverlay();
                _infoOverlay.BindSettings(_s);
                _overlay.InfoPanel = _infoOverlay;
                if (_s.ShowInfoOverlay) _infoOverlay.Show();

                InitSaveTimer();
                RegisterHotkeys();

                // ── MVVM: establish DataContext before LoadSettingsIntoControls
                // so bindings are live when VM properties are first assigned.
                _vm = new MainWindowViewModel(_s);
                DataContext = _vm;
                _vm.PropertyChanged += OnViewModelPropertyChanged;

                LoadSettingsIntoControls();
                UpdateThemePill(_s.ThemeName);
                InitLandmarks();
                InitPresetButtons();

                // Live-save window geometry. Without this, a forced kill or
                // unexpected reboot loses the last size/position because we
                // only persist on graceful close. SizeChanged + LocationChanged
                // route through PushSettings → debounced timer, so cost is one
                // write per ~600 ms of user dragging, not one per pixel.
                SizeChanged     += MainWindow_GeometryChanged;
                LocationChanged += MainWindow_GeometryChanged;

                // Version label
                if (VersionLabel != null)
                    VersionLabel.Text = VersionInfo.Display;

                // ── Restore window geometry, clamped to the current work area ─────
                // Monitor topology can change between sessions (monitor unplugged,
                // resolution change, DPI change). Clamp both size and position so
                // the window is always at least partially visible and within the
                // usable work area (excludes taskbar).
                double vl = SystemParameters.VirtualScreenLeft;
                double vt = SystemParameters.VirtualScreenTop;
                double vr = vl + SystemParameters.VirtualScreenWidth;
                double vb = vt + SystemParameters.VirtualScreenHeight;

                var wa = SystemParameters.WorkArea;   // primary monitor work area
                const double MinW = 420, MinH = 600;

                double desiredW = _s.WindowWidth  > MinW ? _s.WindowWidth  : 460;
                double desiredH = _s.WindowHeight > MinH ? _s.WindowHeight : 1080;

                // Cap to work area; never exceed the primary monitor's usable space.
                Width  = Math.Max(MinW, Math.Min(desiredW, wa.Width));
                Height = Math.Max(MinH, Math.Min(desiredH, wa.Height));

                bool savedPosOnScreen =
                    _s.WindowLeft >= vl && _s.WindowLeft + 100 < vr &&
                    _s.WindowTop  >= vt && _s.WindowTop  + 50  < vb;

                if (savedPosOnScreen)
                {
                    // Additional clamp: make sure entire window is within the
                    // primary work area (or at least can be nudged back in).
                    double left = _s.WindowLeft;
                    double top  = _s.WindowTop;
                    if (left + Width  > wa.Right)  left = wa.Right  - Width;
                    if (top  + Height > wa.Bottom) top  = wa.Bottom - Height;
                    if (left < wa.Left) left = wa.Left;
                    if (top  < wa.Top)  top  = wa.Top;
                    Left = left;
                    Top  = top;
                }
                else
                {
                    // Saved coordinates are off-screen (monitor removed, etc.):
                    // centre on primary work area.
                    Left = wa.Left + (wa.Width  - Width)  / 2;
                    Top  = wa.Top  + (wa.Height - Height) / 2;
                }

                if (_s.StartMinimised) Hide();

                if (_s.LoadedFromDefaults) _s.Save();
            }
            catch (Exception ex)
            {
                CrashLog.Write("MainWindow.OnLoaded", ex);
            }
            finally
            {
                _loading = false;
            }

            // First-run welcome — deferred to after _loading is cleared
            // so the welcome window's modal frame doesn't fight the
            // restore-geometry flow. If the user closes the welcome
            // without completing it (Alt+F4 / window X), we leave
            // FirstRunWelcomed=false so they see it again next launch
            // — better than silently swallowing onboarding for a user
            // who closed the wrong dialog.
            if (!_s.FirstRunWelcomed)
            {
                try
                {
                    var welcome = new WelcomeWindow { Owner = this };
                    welcome.ShowDialog();
                    if (welcome.Completed)
                    {
                        _s.FirstRunWelcomed = true;
                        _s.Save();
                    }
                }
                catch (Exception ex) { CrashLog.Write("WelcomeWindow", ex); }
            }

            UpdateStatusBar();   // initial paint
        }

        // ── Icon ──────────────────────────────────────────────────────────────
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


        /// <summary>
        /// Assigns accessible names to the three major regions of the
        /// settings window so screen-reader users can orient by region.
        ///
        /// Note: WPF's <see cref="System.Windows.Automation.AutomationProperties"/>
        /// does not expose landmark roles (those are UIAutomationClient
        /// types used by automation providers, not by WPF elements). So
        /// we provide the accessible name only — screen readers still
        /// announce the region name when focus enters it, which gives
        /// the same orientation benefit as a true landmark.
        /// </summary>
        private void InitLandmarks()
        {
            try
            {
                if (NavLandmark != null)
                    System.Windows.Automation.AutomationProperties.SetName(
                        NavLandmark, "Main navigation");
                if (MainLandmark != null)
                    System.Windows.Automation.AutomationProperties.SetName(
                        MainLandmark, "Badge settings");
                if (FooterLandmark != null)
                    System.Windows.Automation.AutomationProperties.SetName(
                        FooterLandmark, "Footer");
            }
            catch (Exception ex) { CrashLog.Write("InitLandmarks", ex); }
        }

        // ── Controls ← Settings ───────────────────────────────────────────────
        private void LoadSettingsIntoControls()
        {
            _loading = true;
            try
            {
                // OverlayEnabled + ShowBadge are now ViewModel-bound; set VM
                // properties and let bindings propagate to the controls.
                if (_vm != null)
                {
                    _vm.OverlayEnabled = _s.OverlayEnabled;
                    _vm.ShowBadge      = _s.ShowBadge;
                }
                if (ChkBadgeHidden != null)   ChkBadgeHidden.IsChecked  = _s.BadgeHidden;
                if (ChkBadgePinned != null)   ChkBadgePinned.IsChecked  = _s.BadgePinned;
                if (ChkInfoOverlay != null)   ChkInfoOverlay.IsChecked  = _s.ShowInfoOverlay;
                if (ChkInfoOverlayMeta != null) ChkInfoOverlayMeta.IsChecked = _s.ShowInfoOverlayMeta;
                if (ChkUIADetection != null)  ChkUIADetection.IsChecked = _s.EnableUIADetection;

                // HoverDelayMs is ViewModel-bound; VM notifies HoverDelayLabel
                // which the label binds to, so no manual Text assignment needed.
                if (_vm != null) _vm.HoverDelayMs = _s.HoverDelayMs;
                if (ChkSuppressWhenBg    != null) ChkSuppressWhenBg.IsChecked    = _s.SuppressWhenBackground;
                if (ChkPauseOnFullscreen != null) ChkPauseOnFullscreen.IsChecked = _s.PauseOnFullscreen;
                if (ChkReduceMotion      != null) ChkReduceMotion.IsChecked      = _s.ReduceMotion;
                if (ChkSoundFeedback     != null) ChkSoundFeedback.IsChecked     = _s.SoundFeedbackEnabled;

                // Idle-dim
                if (ChkIdleDim         != null) ChkIdleDim.IsChecked = _s.IdleDimEnabled;
                if (SldIdleDimSec      != null) SldIdleDimSec.Value  = _s.IdleDimSeconds;
                if (SldIdleDimOpacity  != null) SldIdleDimOpacity.Value = _s.IdleDimOpacity;
                if (LblIdleDimSec      != null) LblIdleDimSec.Text      = _s.IdleDimSeconds.ToString();
                if (LblIdleDimOpacity  != null) LblIdleDimOpacity.Text  = $"{(int)Math.Round(_s.IdleDimOpacity * 100)} %";
                if (ChkScreenReaderMode != null) ChkScreenReaderMode.IsChecked = _s.ScreenReaderMode;

                // SR sub-options
                if (ChkSpeechSuppressesBadge != null)
                    ChkSpeechSuppressesBadge.IsChecked = _s.SpeechSuppressesBadge;
                if (SldSpeechDwellSec != null)
                    SldSpeechDwellSec.Value = _s.SpeechDwellSeconds;
                if (LblSpeechDwellSec != null)
                    LblSpeechDwellSec.Text = _s.SpeechDwellSeconds.ToString();
                if (SldSpeechRate != null)
                    SldSpeechRate.Value = _s.SpeechRate;
                if (LblSpeechRate != null)
                    LblSpeechRate.Text = FormatRate(_s.SpeechRate);

                // Populate voice picker — always do this so the ComboBox
                // shows available voices even if SR Mode is currently off.
                PopulateVoicePicker();
                switch (_s.SpeechTrigger)
                {
                    case SpeechTriggerMode.Immediate:
                        if (RbSpeakImmediate != null) RbSpeakImmediate.IsChecked = true;
                        break;
                    case SpeechTriggerMode.Dwell:
                        if (RbSpeakDwell != null) RbSpeakDwell.IsChecked = true;
                        break;
                }

                // Granularity radio group
                string granTag = _s.Granularity.ToString();
                foreach (var rb in new[] { RbGranAll, RbGranInteractive, RbGranButtons })
                    if (rb != null) rb.IsChecked = rb.Tag?.ToString() == granTag;

                // Badge position radios
                string posStr = _s.BadgePosition.ToString();
                foreach (var rb in new[] { RbPosRight, RbPosBottomRight, RbPosBottom, RbPosBottomLeft, RbPosLeft })
                    if (rb != null) rb.IsChecked = rb.Tag?.ToString() == posStr;

                // UI scale — default to Standard (1.0×) when no match.
                // Index 1 = 1.0× with the new Compact entry at index 0.
                double savedScale = _s.UiScale > 0 ? _s.UiScale : 1.0;
                _uiScaleIndex = Array.IndexOf(UiScales, savedScale);
                if (_uiScaleIndex < 0) _uiScaleIndex = 1;   // Standard
                if (UiScaleTransform != null)
                {
                    UiScaleTransform.ScaleX = UiScales[_uiScaleIndex];
                    UiScaleTransform.ScaleY = UiScales[_uiScaleIndex];
                }
                if (BtnCycleSize != null)
                    BtnCycleSize.Content = $"⊞ {UiScaleLabels[_uiScaleIndex]} ({UiScales[_uiScaleIndex]:0.0#}×)";

                // BadgeOpacity + BadgeScale are ViewModel-bound; VM notifies
                // BadgeOpacityLabel / BadgeScaleLabel computed properties which
                // the labels bind to, so no manual Text assignments needed.
                if (_vm != null)
                {
                    _vm.BadgeOpacity = _s.BadgeOpacity;
                    _vm.BadgeScale   = _s.BadgeScale > 0 ? _s.BadgeScale : 1.0;
                }
                // BadgeOffsetX is ViewModel-bound; VM notifies BadgeOffsetLabel
                // which the label binds to, so no manual Text assignment needed.
                if (_vm != null) _vm.BadgeOffsetX = _s.BadgeOffsetX;

                // Badge style (BO-05)
                if (CboBadgeStyle != null) CboBadgeStyle.SelectedIndex = (int)_s.BadgeStyle;
                App.SetBadgeStyle(_s.BadgeStyle);

                // Typography — app font family + badge text size
                if (CboAppFontFamily != null) PopulateFontFamilyCombo(CboAppFontFamily, _s.AppFontFamily);
                if (SldBadgeFontSize != null)
                {
                    double bf = Math.Max(8, Math.Min(72, _s.BadgeFontSize));
                    SldBadgeFontSize.Value = bf;
                    if (LblBadgeFontSize != null) LblBadgeFontSize.Text = $"{(int)bf} px";
                }

                UpdateThemePill(_s.ThemeName);
                UpdateStatusBar();
                UpdatePresetButtons();
            }
            finally { _loading = false; }
        }

        private void MainWindow_GeometryChanged(object sender, EventArgs e)
        {
            // Skip during initial layout pass — _loading is still true and
            // we'd otherwise stomp the freshly-restored values with the
            // pre-restore bounds. WPF raises both events during startup
            // before the constructor finishes positioning the window.
            if (_loading || _s == null) return;
            if (WindowState != WindowState.Normal) return;  // ignore minimized/maximized snapshots

            // Sanitise — WPF can momentarily return NaN/Infinity for
            // Left/Top/Width/Height during transient layout (restore
            // animations, multi-monitor unplug, DWM restart). Persisting
            // those values blows up JSON serialization on the next Save
            // and corrupts settings.json. Drop the snapshot when any
            // axis is non-finite; a later normal layout pass will
            // provide a clean one.
            if (!IsFinite(Width) || !IsFinite(Height)
                || !IsFinite(Left) || !IsFinite(Top)) return;

            _s.WindowWidth  = Width;
            _s.WindowHeight = Height;
            _s.WindowLeft   = Left;
            _s.WindowTop    = Top;
            // Reuse the debounced save — costs one disk write per ~600 ms
            // of dragging, not per WPF resize event.
            _saveTimer.Stop();
            _saveTimer.Start();
        }

        /// <summary>
        /// True when <paramref name="d"/> is a real finite number — not
        /// NaN, not ±Infinity. Used to gate persistence of WPF geometry
        /// values that can transiently go non-finite.
        /// </summary>
        private static bool IsFinite(double d) => !double.IsNaN(d) && !double.IsInfinity(d);

        // ── Push ──────────────────────────────────────────────────────────────
        private void InitSaveTimer()
        {
            _saveTimer.Tick += (s, e) =>
            {
                _saveTimer.Stop();
                _s.Save();
            };
        }

        private void PushSettings()
        {
            if (_loading) return;
            _s.PushToDetector();
            _overlay?.ApplySettings(_s);
            _saveTimer.Stop();
            _saveTimer.Start();
        }

        // ── Theme ─────────────────────────────────────────────────────────────
        // The theme picker is ~120 MenuItems across 10 categories. Rebuilding
        // that tree from scratch on every click (as before) was measurable UI
        // jank for a menu that only needs its checkmark refreshed. Built once
        // lazily, then reused — only the checkmark/FontWeight on each item is
        // updated per open.
        private System.Windows.Controls.ContextMenu _themeMenu;
        private readonly System.Collections.Generic.Dictionary<string, (System.Windows.Controls.MenuItem Item, string Disp)>
            _themeMenuItems = new(System.StringComparer.OrdinalIgnoreCase);

        private void BtnThemeToggle_Click(object sender, RoutedEventArgs e)
        {
            string cur = _s.ThemeName ?? "Dark";

            if (_themeMenu == null)
            {
                BuildThemeMenu();
            }

            foreach (var (key, (mi, disp)) in _themeMenuItems)
            {
                bool act = string.Equals(key, cur, System.StringComparison.OrdinalIgnoreCase);
                mi.Header     = (act ? "✓  " : "    ") + disp;
                mi.FontWeight = act ? FontWeights.SemiBold : FontWeights.Normal;
            }

            _themeMenu.PlacementTarget = BtnThemeToggle;
            _themeMenu.IsOpen = true;
        }

        private void BuildThemeMenu()
        {
            var ctx = new System.Windows.Controls.ContextMenu
            {
                Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom,
            };

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
                    var    mi  = new System.Windows.Controls.MenuItem
                    {
                        Header  = "    " + d,
                        Padding = new Thickness(8, 6, 24, 6),
                    };
                    mi.Click += (_, __) => { _s.ThemeName = key;
                        App.SetTheme(key);
                        UpdateThemePill(key);
                        PushSettings(); };
                    sub.Items.Add(mi);
                    _themeMenuItems[key] = (mi, d);
                }
                ctx.Items.Add(sub);
            }

            _themeMenu = ctx;
        }

        /// <summary>
        /// No-op stub — Theme button label is static ("◑ Theme") matching
        /// TimerStudio's pattern. The active theme is indicated by the
        /// checkmark inside the picker menu, not by the button label.
        /// Callers kept as-is so future label updates need only this method.
        /// </summary>
        private void UpdateThemePill(string themeName) { }

        // ── ViewModel → settings bridge ───────────────────────────────────────
        // Fired on the UI thread whenever a bound ViewModel property changes.
        // Syncs the delta to _s and calls PushSettings() so overlay/detector
        // stay in step. The _loading guard prevents feedback loops during
        // LoadSettingsIntoControls().
        private void OnViewModelPropertyChanged(object sender,
            System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (_loading || _vm == null) return;
            switch (e.PropertyName)
            {
                case nameof(MainWindowViewModel.OverlayEnabled):
                    _s.OverlayEnabled = _vm.OverlayEnabled;
                    UpdateStatusBar();
                    PushSettings();
                    break;
                case nameof(MainWindowViewModel.ShowBadge):
                    _s.ShowBadge = _vm.ShowBadge;
                    PushSettings();
                    break;
                case nameof(MainWindowViewModel.BadgeOpacity):
                    _s.BadgeOpacity = _vm.BadgeOpacity;
                    PushSettings();
                    break;
                case nameof(MainWindowViewModel.BadgeScale):
                    _s.BadgeScale = _vm.BadgeScale;
                    PushSettings();
                    break;
                case nameof(MainWindowViewModel.BadgeOffsetX):
                    // Single slider has always driven both axes symmetrically —
                    // preserved from the pre-migration SldBadgeOffset_ValueChanged.
                    _s.BadgeOffsetX = _vm.BadgeOffsetX;
                    _s.BadgeOffsetY = _vm.BadgeOffsetX;
                    PushSettings();
                    break;
                case nameof(MainWindowViewModel.HoverDelayMs):
                    _s.HoverDelayMs = _vm.HoverDelayMs;
                    PushSettings();
                    break;
                // BadgeOpacityLabel / BadgeScaleLabel / BadgeOffsetLabel /
                // HoverDelayLabel are computed; no _s sync.
            }
        }

        // ── Badge controls ────────────────────────────────────────────────────

        private void ChkBadgeHidden_Changed(object sender, RoutedEventArgs e)
        {
            if (_loading) return;
            _s.BadgeHidden = ChkBadgeHidden?.IsChecked == true;
            PushSettings();
        }

        private void ChkBadgePinned_Changed(object sender, RoutedEventArgs e)
        {
            if (_loading) return;
            _s.BadgePinned = ChkBadgePinned?.IsChecked == true;
            PushSettings();
        }

        private void ChkUIADetection_Changed(object sender, RoutedEventArgs e)
        {
            if (_loading) return;
            _s.EnableUIADetection = ChkUIADetection.IsChecked == true;
            PushSettings();
        }

        private void BadgePosition_Checked(object sender, RoutedEventArgs e)
        {
            if (_loading) return;
            if (sender is System.Windows.Controls.RadioButton rb
                && Enum.TryParse(rb.Tag?.ToString(), out BadgeSide side))
            {
                _s.BadgePosition = side;
                PushSettings();
            }
        }

        private void ChkInfoOverlay_Changed(object sender, RoutedEventArgs e)
        {
            if (_loading) return;
            _s.ShowInfoOverlay = ChkInfoOverlay.IsChecked == true;
            if (_infoOverlay != null)
            {
                if (_s.ShowInfoOverlay) _infoOverlay.Show();
                else                    _infoOverlay.Hide();
            }
            PushSettings();
        }

        private void ChkInfoOverlayMeta_Changed(object sender, RoutedEventArgs e)
        {
            if (_loading) return;
            _s.ShowInfoOverlayMeta = ChkInfoOverlayMeta?.IsChecked == true;
            // The Info Overlay reads _s on every UpdateContent call so we
            // don't need to push imperatively — the next detection refresh
            // honours the new flag. Force a label refresh now so the user
            // sees the change instantly even if the cursor isn't moving.
            _infoOverlay?.RefreshLast();
            PushSettings();
        }

        // ── Typography (Low Vision) ───────────────────────────────────────────

        // Curated low-vision / dyslexia-friendly fonts shown first in the
        // CboAppFontFamily picker. Matches the NoteStation list so users
        // who pick OpenDyslexic in one app see the same option here.
        private static readonly string[] _preferredFonts = new[]
        {
            "Segoe UI", "Atkinson Hyperlegible", "OpenDyslexic", "Verdana",
            "Arial", "Tahoma", "Calibri", "Comic Sans MS", "Consolas"
        };

        /// <summary>
        /// Fills the supplied ComboBox with the preferred low-vision fonts
        /// at the top, a Separator, then the rest of the installed system
        /// font families. Selects <paramref name="current"/> if present;
        /// falls back to the first item otherwise.
        /// </summary>
        private static void PopulateFontFamilyCombo(System.Windows.Controls.ComboBox cbo, string current)
        {
            cbo.Items.Clear();
            var seen = new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var installed = new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var ff in System.Windows.Media.Fonts.SystemFontFamilies) installed.Add(ff.Source);

            foreach (var f in _preferredFonts)
                if (seen.Add(f)) cbo.Items.Add(f);

            cbo.Items.Add(new System.Windows.Controls.Separator());

            foreach (var f in installed)
                if (seen.Add(f)) cbo.Items.Add(f);

            for (int i = 0; i < cbo.Items.Count; i++)
            {
                if (cbo.Items[i] is string s
                    && string.Equals(s, current, StringComparison.OrdinalIgnoreCase))
                {
                    cbo.SelectedIndex = i;
                    return;
                }
            }
            cbo.SelectedIndex = 0;
        }

        private void CboBadgeStyle_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (_loading) return;
            if (CboBadgeStyle?.SelectedItem is ComboBoxItem { Tag: string tag }
                && Enum.TryParse(tag, out BadgeStyle style))
            {
                _s.BadgeStyle = style;
                App.SetBadgeStyle(style);
                _overlay?.ApplySettings(_s);
                _infoOverlay?.RefreshLast();
                PushSettings();
            }
        }

        private void CboAppFontFamily_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (_loading) return;
            if (CboAppFontFamily?.SelectedItem is string fam && !string.IsNullOrWhiteSpace(fam))
            {
                _s.AppFontFamily = fam;
                App.SetAppFontFamily(fam);
                PushSettings();
            }
        }

        private void SldBadgeFontSize_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_loading) return;
            double v = Math.Max(8, Math.Min(72, e.NewValue));
            _s.BadgeFontSize = v;
            App.SetBadgeFontSize(v);
            if (LblBadgeFontSize != null) LblBadgeFontSize.Text = $"{(int)v} px";
            PushSettings();
        }

        // ── Behaviour settings ────────────────────────────────────────────────

        private void ChkSuppressWhenBg_Changed(object sender, RoutedEventArgs e)
        {
            if (_loading) return;
            _s.SuppressWhenBackground = ChkSuppressWhenBg?.IsChecked == true;
            PushSettings();
        }

        private void ChkPauseOnFullscreen_Changed(object sender, RoutedEventArgs e)
        {
            if (_loading) return;
            _s.PauseOnFullscreen = ChkPauseOnFullscreen?.IsChecked == true;
            PushSettings();
        }

        private void ChkReduceMotion_Changed(object sender, RoutedEventArgs e)
        {
            if (_loading) return;
            _s.ReduceMotion = ChkReduceMotion?.IsChecked == true;
            PushSettings();
        }

        /// <summary>
        /// Toggles audio cue on overlay state changes. Plays a sample
        /// immediately on enable so the user previews the cue without
        /// having to fire the hotkey.
        /// </summary>
        private void ChkSoundFeedback_Changed(object sender, RoutedEventArgs e)
        {
            if (_loading) return;
            bool nowEnabled = ChkSoundFeedback?.IsChecked == true;
            _s.SoundFeedbackEnabled = nowEnabled;
            PushSettings();
            if (nowEnabled)
            {
                // Sample so user knows what they're enabling.
                try { System.Media.SystemSounds.Asterisk.Play(); }
                catch { /* swallow */ }
            }
        }

        // ── Idle dim ──────────────────────────────────────────────────────

        private void ChkIdleDim_Changed(object sender, RoutedEventArgs e)
        {
            if (_loading) return;
            _s.IdleDimEnabled = ChkIdleDim?.IsChecked == true;
            PushSettings();
        }

        private void SldIdleDimSec_Changed(object sender,
            RoutedPropertyChangedEventArgs<double> e)
        {
            if (_loading) return;
            int seconds = (int)Math.Round(e.NewValue);
            _s.IdleDimSeconds = seconds;
            if (LblIdleDimSec != null) LblIdleDimSec.Text = seconds.ToString();
            PushSettings();
        }

        private void SldIdleDimOpacity_Changed(object sender,
            RoutedPropertyChangedEventArgs<double> e)
        {
            if (_loading) return;
            _s.IdleDimOpacity = e.NewValue;
            if (LblIdleDimOpacity != null)
                LblIdleDimOpacity.Text = $"{(int)Math.Round(e.NewValue * 100)} %";
            PushSettings();
        }

        /// <summary>
        /// Toggles the full screen-reader workflow. The companion
        /// <see cref="AppSettings.SpeechSuppressesBadge"/> flag
        /// controls whether <see cref="AppSettings.BadgeHidden"/>
        /// is also flipped — when true (default), enabling SR mode
        /// hides the visual badge; when false, the badge stays
        /// visible alongside speech. Either way, prior BadgeHidden
        /// state is cached in <see cref="_previousBadgeHidden"/> so
        /// disabling SR mode cleanly restores it.
        /// </summary>
        private bool _previousBadgeHidden;
        private void ChkScreenReaderMode_Changed(object sender, RoutedEventArgs e)
        {
            if (_loading) return;
            bool nowOn = ChkScreenReaderMode?.IsChecked == true;
            bool wasOn = _s.ScreenReaderMode;

            if (nowOn && !wasOn)
            {
                // Entering SR mode — remember the current BadgeHidden
                // value so we can restore it on exit. Only flip
                // BadgeHidden if SpeechSuppressesBadge is true.
                _previousBadgeHidden = _s.BadgeHidden;
                if (_s.SpeechSuppressesBadge)
                {
                    _s.BadgeHidden = true;
                    if (ChkBadgeHidden != null) ChkBadgeHidden.IsChecked = true;
                }
            }
            else if (!nowOn && wasOn)
            {
                // Leaving SR mode — restore the prior visual choice.
                _s.BadgeHidden = _previousBadgeHidden;
                if (ChkBadgeHidden != null) ChkBadgeHidden.IsChecked = _previousBadgeHidden;
            }
            _s.ScreenReaderMode = nowOn;
            // Whenever SR Mode changes state, scrub the runtime mute
            // flag so a previous session's mute doesn't carry over.
            // (See OverlayWindow.ResetMuteState for rationale.)
            try { _overlay?.ResetMuteState(); } catch { }
            PushSettings();

            // If SR Mode was just enabled and SAPI failed to init,
            // surface a clear diagnostic immediately. Without this
            // the user sees "Screen Reader Mode on" with no audio
            // and no indication why — especially confusing on Win11 N
            // or Server Core where SAPI isn't available.
            if (nowOn && _overlay != null)
            {
                // Ensure the announcer has been created so InitError is populated.
                try { _overlay.EnsureAnnouncerPublic(); } catch { }
                string err = _overlay.SpeechInitError;
                if (!string.IsNullOrEmpty(err))
                {
                    AppDialog.Show(
                        "Screen Reader Mode is enabled, but speech could not start:\n\n" +
                        err + "\n\n" +
                        "Screen Reader Mode has been turned off.",
                        "Speech unavailable",
                        System.Windows.MessageBoxButton.OK,
                        System.Windows.MessageBoxImage.Warning);

                    // Roll back — don't leave SR Mode "on" with no audio.
                    _s.ScreenReaderMode = false;
                    if (ChkScreenReaderMode != null)
                    {
                        _loading = true;
                        try { ChkScreenReaderMode.IsChecked = false; }
                        finally { _loading = false; }
                    }
                    PushSettings();
                }
            }
        }

        /// <summary>
        /// Toggles whether enabling Screen Reader Mode also hides the
        /// visual badge. If SR mode is currently on, this immediately
        /// applies the change to <see cref="AppSettings.BadgeHidden"/>
        /// so the user sees the effect without having to toggle SR
        /// off and back on.
        ///
        /// Critical invariant: <see cref="_previousBadgeHidden"/> is
        /// captured *only* at the moment SR Mode is first turned on
        /// (in <see cref="ChkScreenReaderMode_Changed"/>). Mid-session
        /// flips of this sub-checkbox must NOT overwrite it — otherwise
        /// the cache would track the post-suppression state on every
        /// re-entry, leaking "true" across sessions and corrupting
        /// the user's original BadgeHidden preference.
        /// </summary>
        private void ChkSpeechSuppressesBadge_Changed(object sender, RoutedEventArgs e)
        {
            if (_loading) return;
            bool suppress = ChkSpeechSuppressesBadge?.IsChecked == true;
            _s.SpeechSuppressesBadge = suppress;

            if (_s.ScreenReaderMode)
            {
                // Live-apply: SR mode is on, so flipping this checkbox
                // should immediately update the badge visibility. We
                // intentionally do NOT touch _previousBadgeHidden here
                // — its job is to remember the user's pre-SR-Mode
                // choice, captured on entry. Mid-flight changes are
                // applied by reading SpeechSuppressesBadge directly.
                if (suppress)
                {
                    _s.BadgeHidden = true;
                    if (ChkBadgeHidden != null) ChkBadgeHidden.IsChecked = true;
                }
                else
                {
                    // Restore the badge — undo the suppression we
                    // applied when SR was first enabled.
                    _s.BadgeHidden = _previousBadgeHidden;
                    if (ChkBadgeHidden != null) ChkBadgeHidden.IsChecked = _previousBadgeHidden;
                }
            }
            PushSettings();
        }

        /// <summary>
        /// Maps the speech-trigger radio group to <see cref="AppSettings.SpeechTrigger"/>.
        /// Single handler attached to all three radios; reads the
        /// sender's tag from the IsChecked state.
        /// </summary>
        private void RbSpeechTrigger_Changed(object sender, RoutedEventArgs e)
        {
            if (_loading) return;
            if (RbSpeakImmediate?.IsChecked == true)
                _s.SpeechTrigger = SpeechTriggerMode.Immediate;
            else if (RbSpeakDwell?.IsChecked == true)
                _s.SpeechTrigger = SpeechTriggerMode.Dwell;
            PushSettings();
        }

        private void SldSpeechDwellSec_Changed(object sender,
            RoutedPropertyChangedEventArgs<double> e)
        {
            if (_loading) return;
            int seconds = (int)Math.Round(e.NewValue);
            _s.SpeechDwellSeconds = seconds;
            if (LblSpeechDwellSec != null) LblSpeechDwellSec.Text = seconds.ToString();
            PushSettings();
        }

        // ── Voice picker (#35) ────────────────────────────────────────────────

        private bool _voicePickerReady;

        /// <summary>
        /// Populates the voice ComboBox with installed SAPI voices.
        /// Called on first load and whenever SR Mode is enabled. Uses
        /// the static helper so we don't need a live announcer instance
        /// just to enumerate installed voices.
        /// </summary>
        private void PopulateVoicePicker()
        {
            if (CboSpeechVoice == null) return;
            _voicePickerReady = false;
            try
            {
                CboSpeechVoice.Items.Clear();
                var voices = ScreenReaderAnnouncer.GetInstalledVoiceNames();

                if (voices.Count == 0)
                {
                    CboSpeechVoice.Items.Add("(no voices installed)");
                    CboSpeechVoice.SelectedIndex = 0;
                    CboSpeechVoice.IsEnabled = false;
                    return;
                }

                CboSpeechVoice.IsEnabled = true;

                // "OS default" sentinel — empty string in settings means
                // "let SAPI pick". Display it as a named option so users
                // understand what "default" means.
                CboSpeechVoice.Items.Add("(OS default)");

                foreach (var name in voices)
                    CboSpeechVoice.Items.Add(name);

                // Restore saved selection.
                string saved = _s.SpeechVoiceName?.Trim() ?? "";
                if (string.IsNullOrEmpty(saved))
                {
                    CboSpeechVoice.SelectedIndex = 0;   // (OS default)
                }
                else
                {
                    int idx = -1;
                    for (int i = 1; i < CboSpeechVoice.Items.Count; i++)
                    {
                        if (string.Equals(CboSpeechVoice.Items[i]?.ToString(),
                                          saved, StringComparison.OrdinalIgnoreCase))
                        { idx = i; break; }
                    }
                    // Saved voice not found — show "(OS default)" and clear setting.
                    CboSpeechVoice.SelectedIndex = idx >= 0 ? idx : 0;
                    if (idx < 0) _s.SpeechVoiceName = "";
                }
            }
            finally { _voicePickerReady = true; }
        }

        private void CboSpeechVoice_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (_loading || !_voicePickerReady) return;
            int idx = CboSpeechVoice?.SelectedIndex ?? -1;
            if (idx < 0) return;

            // Index 0 = "(OS default)" → empty setting
            string voice = idx == 0
                ? ""
                : CboSpeechVoice.Items[idx]?.ToString() ?? "";
            _s.SpeechVoiceName = voice;
            PushSettings();
        }

        // ── Speech rate slider (#36) ──────────────────────────────────────────

        private void SldSpeechRate_Changed(object sender,
            RoutedPropertyChangedEventArgs<double> e)
        {
            if (_loading) return;
            int rate = (int)Math.Round(e.NewValue);
            _s.SpeechRate = rate;
            if (LblSpeechRate != null) LblSpeechRate.Text = FormatRate(rate);
            PushSettings();
        }

        private static string FormatRate(int rate) =>
            rate == 0 ? "0" : rate > 0 ? $"+{rate}" : rate.ToString();

        /// <summary>
        /// Deep-link to HotkeysWindow so the user doesn't have to hunt
        /// through the navigation row to bind the Speak hotkey.
        /// </summary>
        private void BtnConfigureSpeakHotkey_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var dlg = new HotkeysWindow(_s) { Owner = this };
                dlg.ShowDialog();
                // Match BtnNavHotkeys_Click — capture writes mutate _s
                // immediately (not on Apply), so we always need to
                // persist + re-register, regardless of whether the user
                // dismissed via Apply, Cancel, or window close.
                _s.Save();
                RegisterHotkeys();
            }
            catch (Exception ex) { CrashLog.Write("BtnConfigureSpeakHotkey_Click", ex); }
        }

        private void RbGranularity_Checked(object sender, RoutedEventArgs e)
        {
            if (_loading) return;
            if (sender is System.Windows.Controls.RadioButton rb
                && rb.Tag is string tag
                && Enum.TryParse<DetectionGranularity>(tag, out var gran))
            {
                _s.Granularity = gran;
                PushSettings();
            }
        }

        // ── Badge colours / rules / hotkeys / about ───────────────────────────
        private void BtnBadgeColors_Click(object s, RoutedEventArgs e)
        {
            var win = new BadgeColorsWindow(_s) { Owner = this };
            win.ShowDialog();
            _s.PushToDetector();
            _s.Save();
        }

        private void BtnCustomRules_Click(object s, RoutedEventArgs e)
        {
            var win = new CustomRulesWindow(_s) { Owner = this };
            win.ShowDialog();
            _s.PushToDetector();
            _s.Save();
        }

        private void BtnNavHotkeys_Click(object s, RoutedEventArgs e)
        {
            var win = new HotkeysWindow(_s) { Owner = this };
            win.ShowDialog();
            _s.Save();
            RegisterHotkeys();
        }

        private void BtnAbout_Click(object sender, RoutedEventArgs e)
        {
            var win = new AboutWindow { Owner = this };
            win.ShowDialog();
        }

        private void BtnStartupSettings_Click(object sender, RoutedEventArgs e)
        {
            var win = new StartupSettingsWindow(_s, this) { Owner = this };
            win.ShowDialog();
        }

        /// <summary>
        /// Opens the keyboard-shortcut cheat-sheet. Wired to F1 via
        /// <see cref="System.Windows.Input.ApplicationCommands.Help"/>
        /// in MainWindow.xaml. Only one cheat-sheet may be open at a
        /// time — repeated F1 presses bring the existing one to front
        /// rather than spawning duplicates.
        /// </summary>
        private CheatSheetWindow _cheatSheet;
        private void OpenCheatSheet_Executed(object sender, ExecutedRoutedEventArgs e)
        {
            try
            {
                if (_cheatSheet != null && _cheatSheet.IsLoaded)
                {
                    _cheatSheet.Activate();
                    return;
                }
                _cheatSheet = new CheatSheetWindow(_s) { Owner = this };
                _cheatSheet.Closed += (s2, e2) => _cheatSheet = null;
                _cheatSheet.ShowDialog();
            }
            catch (Exception ex) { CrashLog.Write("OpenCheatSheet_Executed", ex); }
        }

        // ── UI scale cycle ────────────────────────────────────────────────────
        private void BtnCycleSize_Click(object sender, RoutedEventArgs e)
        {
            _uiScaleIndex = (_uiScaleIndex + 1) % UiScales.Length;
            double scale = UiScales[_uiScaleIndex];
            UiScaleTransform.ScaleX = scale;
            UiScaleTransform.ScaleY = scale;
            if (BtnCycleSize != null)
            {
                BtnCycleSize.Content = $"⊞ {UiScaleLabels[_uiScaleIndex]} ({UiScales[_uiScaleIndex]:0.0#}×)";
                BtnCycleSize.ToolTip = $"UI size: {UiScaleLabels[_uiScaleIndex]} — click to cycle";
            }
            _s.UiScale = scale;
            PushSettings();
        }

        /// <summary>
        /// Per-section help — opens the cheat-sheet. Each call site
        /// passes a section label via <c>Tag</c> on the help button;
        /// the cheat-sheet is identical for every section today, but
        /// future builds can scroll to a section anchor based on the
        /// tag. Centralised here so per-section buttons don't each
        /// need their own handler. WCAG 3.3.5 (Help, AAA).
        /// </summary>
        private void BtnSectionHelp_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_cheatSheet != null && _cheatSheet.IsLoaded)
                {
                    _cheatSheet.Activate();
                    return;
                }
                _cheatSheet = new CheatSheetWindow(_s) { Owner = this };
                _cheatSheet.Closed += (s2, e2) => _cheatSheet = null;
                _cheatSheet.ShowDialog();
            }
            catch (Exception ex) { CrashLog.Write("BtnSectionHelp_Click", ex); }
        }

        // ── Status bar ────────────────────────────────────────────────────────
        private void UpdateStatusBar()
        {
            if (StatusDot == null || StatusLabel == null) return;

            // Priority: disabled > frozen > active. Frozen is a runtime
            // sub-state of Active — badge still renders but labels are
            // pinned to whatever the user last saw.
            string label;
            string brushKey;
            if (!_s.OverlayEnabled)        { label = "Paused"; brushKey = "BrushDisabled"; }
            else if (_s.BadgeFrozen)       { label = "Frozen"; brushKey = "BrushOrange"; }
            else                           { label = "Active"; brushKey = "BrushGreen"; }

            StatusDot.Fill   = SafeBrush(brushKey);
            string previousLabel = StatusLabel.Text;
            StatusLabel.Text = label;

            // WCAG 4.1.3 — status messages must be programmatically
            // announceable. LiveSetting=Polite on the TextBlock handles
            // most screen readers, but raising the LiveRegionChanged
            // event explicitly covers older ones (NVDA pre-2022, JAWS
            // older builds) that don't automatically observe LiveSetting
            // attribute changes. Only fire when the label actually
            // changed — prevents redundant announcements during
            // routine UI refreshes.
            if (previousLabel != label)
            {
                try
                {
                    var peer = System.Windows.Automation.Peers.UIElementAutomationPeer.FromElement(StatusLabel);
                    peer?.RaiseAutomationEvent(
                        System.Windows.Automation.Peers.AutomationEvents.LiveRegionChanged);
                }
                catch { /* swallow: peer not yet realised */ }
            }

            // Tray tooltip mirrors panel status so users who've hidden the
            // panel can still see state at a glance. NotifyIcon.Text caps
            // at 127 chars — single-line format below stays well under.
            if (_tray != null)
            {
                try { _tray.Text = $"Badge Overlay — {label}"; }
                catch { /* NotifyIcon.Text setter throws if > 127 chars */ }
            }

        }

        private void BtnHide_Click(object sender, RoutedEventArgs e) => Hide();

        // ── Export / Import (public — StartupSettingsWindow) ─────────────────
        public void ExportSettingsPublic()
        {
            var dlg = new SaveFileDialog
            {
                Title      = "Export Badge Overlay Settings",
                Filter     = "JSON Settings|*.json",
                FileName   = "BadgeOverlay_settings.json",
                DefaultExt = "json",
            };
            if (dlg.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
            try
            {
                System.IO.File.WriteAllText(dlg.FileName,
                    System.Text.Json.JsonSerializer.Serialize(_s, AppSettings.SharedJsonOpts));
                AppDialog.Show("Settings exported.", "Export",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                AppDialog.Show($"Export failed:\n{ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        public void ImportSettingsPublic()
        {
            var dlg = new OpenFileDialog
            {
                Title  = "Import Badge Overlay Settings",
                Filter = "JSON Settings|*.json",
            };
            if (dlg.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
            try
            {
                AppSettings imported = System.Text.Json.JsonSerializer.Deserialize<AppSettings>(
                    System.IO.File.ReadAllText(dlg.FileName), AppSettings.SharedJsonOpts);
                if (imported == null) throw new Exception("File did not contain valid settings.");
                _s = imported;
                _s.PushToDetector();
                App.SetTheme(_s.ThemeName);
                LoadSettingsIntoControls();
                UpdateThemePill(_s.ThemeName);
                _overlay?.ApplySettings(_s);
                RegisterHotkeys();
                _s.Save();
                AppDialog.Show("Settings imported.", "Import",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                AppDialog.Show($"Import failed:\n{ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        public void PushSettingsPublic()
        {
            _overlay?.ApplySettings(_s);
            LoadSettingsIntoControls();
            UpdateThemePill(_s.ThemeName);
        }

        public void ResetToFactoryPublic()
        {
            _s = new AppSettings();
            _previousBadgeHidden = false;   // reset pause-toggle state so factory defaults are consistent
            _s.PushToDetector();
            App.SetTheme(_s.ThemeName);
            _overlay?.ApplySettings(_s);
            LoadSettingsIntoControls();
            UpdateThemePill(_s.ThemeName);
            RegisterHotkeys();
        }

        // ── Lifecycle ─────────────────────────────────────────────────────────
        /// <summary>
        /// Persists settings + window geometry to disk. Safe to call
        /// repeatedly. Does NOT dispose any runtime resources — use
        /// <see cref="DisposeRuntimeResources"/> only on real app exit.
        /// </summary>
        public void TrySaveSettings()
        {
            if (App.SuppressSettingsSave) return;   // About → Reset all in progress
            try
            {
                // Flush any pending debounced save BEFORE stopping the timer:
                // the unconditional _s.Save() below covers it either way.
                _saveTimer.Stop();
                if (_s != null)
                {
                    // Same sanitisation as MainWindow_GeometryChanged —
                    // a forced save during a transient layout pass (e.g.
                    // SessionEnding) can otherwise capture NaN/Infinity.
                    if (IsFinite(Width) && IsFinite(Height)
                        && IsFinite(Left) && IsFinite(Top)
                        && WindowState == WindowState.Normal)
                    {
                        _s.WindowWidth  = Width;
                        _s.WindowHeight = Height;
                        _s.WindowLeft   = Left;
                        _s.WindowTop    = Top;
                    }
                    _s.Save();  // always write — covers both pending edits and window geometry
                }
            }
            catch (Exception ex) { CrashLog.Write("TrySaveSettings", ex); }
        }

        /// <summary>
        /// Releases hotkey, tray, and overlay resources. Call exactly once,
        /// only when the application is exiting — NOT on minimise-to-tray.
        /// </summary>
        private bool _runtimeDisposed;
        private void DisposeRuntimeResources()
        {
            if (_runtimeDisposed) return;
            _runtimeDisposed = true;
            try { _hotkeyToggle?.Dispose();    _hotkeyToggle    = null; } catch { }
            try { _hotkeyFreeze?.Dispose();    _hotkeyFreeze    = null; } catch { }
            try { _hotkeyCopyLabel?.Dispose(); _hotkeyCopyLabel = null; } catch { }
            try { _hotkeySpeak?.Dispose();     _hotkeySpeak     = null; } catch { }
            for (int i = 0; i < 5; i++)
            {
                try { _hotkeyLoadPresets[i]?.Dispose(); _hotkeyLoadPresets[i] = null; } catch { }
                try { _hotkeySavePresets[i]?.Dispose(); _hotkeySavePresets[i] = null; } catch { }
            }
            try { _saveTimer.Stop(); }                              catch { }
        }

        private void OnClosing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            // Window close = minimise to tray. Keep hotkey alive so the
            // global toggle still works while the app is tray-only.
            e.Cancel = true;
            TrySaveSettings();
            Hide();
        }


        // ── Utilities ─────────────────────────────────────────────────────────
        private Brush SafeBrush(string key)
        {
            try   { return (Brush)FindResource(key); }
            catch { return Brushes.Gray; }
        }
    }
}
