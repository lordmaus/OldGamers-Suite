using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls.Primitives;
using System.Windows.Forms;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using static SuperMagnifier.NativeMethods;

namespace SuperMagnifier
{
    public partial class MainWindow : Window
    {
        // ── Core ──────────────────────────────────────────────────────────────
        private AppSettings       _settings;
        private MagnifierEngine   _engine;
        private LensWindow        _lens;
        private DispatcherTimer   _timer;
        private CursorHighlighter _cursorHL;
        private ReadingRuler      _ruler;
        private CaretTracker      _caretTracker;
        private NotifyIcon        _tray;
        private GlobalHotkey      _hotkeyToggle;
        private GlobalHotkey      _hotkeyFreeze;
        private GlobalHotkey      _hotkeyZoomIn;
        private GlobalHotkey      _hotkeyZoomOut;
        private GlobalHotkey      _hotkeyClickThru;
        private GlobalHotkey      _hotkeySnapFocus; // Ctrl+Home
        private GlobalHotkey[]    _hotkeyPresetLoad; // Ctrl+1..5
        private GlobalHotkey[]    _hotkeyPresetSave; // Ctrl+Shift+1..5
        private LensKeyboardMover _keyboardMover;
        private AppPresetMap      _appPresetMap;
        private AppPositionMap    _appPositionMap;
        private CursorPulse       _cursorPulse;
        private MirrorLensWindow  _mirrorLens;
        private bool   _showReticleDot = true; // suppressed in track mode
        private GlobalHotkey      _hotkeyPulse; // Ctrl+P
        private List<LensPreset> _presets;
        private System.Windows.Controls.Button[] _presetBtns;

        private double _zoom           = 2.0;
        private double _zoomStep       = 0.25;
        private bool   _active         = false;
        private bool   _sliderChanging = false;
        private string _colorMode      = "normal";
        private bool   _panelOpen      = false;
        private bool   _lensPanelOpen  = false;
        private bool   _isDark         = true;
        private string _themeName      = "Dark";
        private string _lastDarkTheme  = "Dark";   // restored when toggling back from Light via ThemeNavBtn

        // ── Feature: Zoom lock ────────────────────────────────────────────────
        private double _zoomMin        = 1.25;   // enforced in SetZoom
        private double _zoomMax        = 20.0;

        // ── Feature: Colour tinting ───────────────────────────────────────────
        private float  _tintIntensity  = 0.5f;

        // ── Feature: Dim when idle ────────────────────────────────────────────
        private DispatcherTimer _dimTimer;
        private bool            _dimmed        = false;
        private double          _preDimOpacity = 1.0;
        private System.Windows.Point _lastCursorPos;

        // ── Track mode: cache last position to skip redundant MoveToFollow calls ──
        private int _lastTrackX = int.MinValue;
        private int _lastTrackY = int.MinValue;

        // ── Feature: Home zoom ────────────────────────────────────────────────
        private GlobalHotkey _hotkeyHomeZoom;   // Ctrl+0
        private GlobalHotkey _hotkeyFineToggle; // Ctrl+G
        private GlobalHotkey _hotkeyOpacityDn;  // Ctrl+[
        private GlobalHotkey _hotkeyOpacityUp;  // Ctrl+]
        private GlobalHotkey _hotkeyColorCycle; // Ctrl+Shift+C
        private GlobalHotkey _hotkeyFollowToggle;  // Ctrl+L
        private GlobalHotkey _hotkeyReticleToggle; // Ctrl+R
        private GlobalHotkey _hotkeySizeCycle;     // Ctrl+Alt+S

        // ── Feature: Lens size cycle (default → 2× → default) ─────────────────
        private bool   _lensSizeDoubled = false;
        private double _lensCycleBaseW, _lensCycleBaseH;

        // ── Feature: Snap-to-word ─────────────────────────────────────────────
        private bool _snapToWord = false;
        // SM-04 (2026-10-13): WordTracker.TryGetWordFocus only calls GetGUIThreadInfo /
        // ClientToScreen — no cross-process SendMessage, nothing that can hang on another app —
        // so it runs inline on the render tick. The old double Task.Run + Wait(200) per poll
        // parked a pool thread every frame for no benefit.
        private int? _wordFocusX = null, _wordFocusY = null;  // last good result

        // ── Feature: Per-monitor zoom memory ─────────────────────────────────
        private Dictionary<string, double> _monitorZoomMap = new();
        private string _lastMonitorName = "";

        // ── Feature: Zoom transition animation ───────────────────────────────
        private DispatcherTimer _zoomAnimTimer;
        private double          _zoomAnimTarget;
        private double          _zoomAnimStart;
        private double          _zoomAnimElapsed;
        private const double    ZoomAnimDurationMs = 180;

        // ── WCAG 2.3.3: Reduced motion ───────────────────────────────────────
        // Mirrors Windows "Show animations in Windows" setting (SystemParameters.ClientAreaAnimation).
        // When false all slide, zoom, and toast animations are skipped; state changes are instant.
        private bool _reduceMotion = false;

        // ── Feature: Lens resize via keyboard (WCAG 2.5.7) ───────────────────
        private GlobalHotkey _hotkeyResizeUp;
        private GlobalHotkey _hotkeyResizeDn;
        private GlobalHotkey _hotkeyResizeLeft;
        private GlobalHotkey _hotkeyResizeRight;

        private static readonly double[] UiScales     = { 0.75, 1.0, 1.25, 1.5, 2.0 };
        private static readonly string[] UiScaleNames = { "Compact", "Standard", "Medium", "Large", "Xtra Large" };
        private int _uiSizeIndex = 1;

        // Guards Border/Grip slider+button handlers against firing on the XAML
        // literal default Value (e.g. BorderThickSlider Value="2") during
        // InitializeComponent — before ApplySettingsToUi has pushed the real
        // loaded value in. Without this, that spurious first fire overwrites
        // _settings with the XAML default in memory, ApplySettingsToUi never
        // sees the true loaded value, and (with real-time Save) the default
        // gets written back to disk every launch. Set true once the loaded
        // settings have been applied to the UI in the constructor.
        private bool _uiInitialized = false;

        internal RelayCommand ZoomUpCommand       { get; }
        internal RelayCommand ZoomDownCommand     { get; }
        internal RelayCommand ClosePanelsCommand  { get; }

        public MainWindow()
        {
            ZoomUpCommand      = new RelayCommand(_ => ZoomBy(+_zoomStep), _ => _zoom < _zoomMax);
            ZoomDownCommand    = new RelayCommand(_ => ZoomBy(-_zoomStep), _ => _zoom > _zoomMin);
            // Escape closes any open side panel (WCAG 2.1.2 no keyboard trap)
            ClosePanelsCommand = new RelayCommand(_ =>
            {
                if (_panelOpen)     PanelTabBtn_Click(null, null);
                if (_lensPanelOpen) LensPanelTabBtn_Click(null, null);
            });

            _settings = AppSettings.Load();
            _engine   = new MagnifierEngine();
            _engine.Initialize();

            // Apply saved theme before any UI is created
            _themeName = !string.IsNullOrEmpty(_settings.ThemeName) ? _settings.ThemeName
                       : (_settings.DarkMode ? "Dark" : "Light");
            _isDark    = !App.IsLightTheme(_themeName);
            if (_isDark) _lastDarkTheme = _themeName;
            App.SetTheme(_themeName);

            // ── Accessibility: tooltip timing ──────────────────────────────
            // WCAG 1.4.13 — tooltips must be persistent (not auto-dismiss quickly)
            System.Windows.Controls.ToolTipService.ShowDurationProperty.OverrideMetadata(
                typeof(System.Windows.DependencyObject),
                new FrameworkPropertyMetadata(30000)); // 30 seconds
            System.Windows.Controls.ToolTipService.InitialShowDelayProperty.OverrideMetadata(
                typeof(System.Windows.DependencyObject),
                new FrameworkPropertyMetadata(200));   // 200ms (default 400ms)

            // ── Accessibility: Windows High Contrast + font size changes ───
            Microsoft.Win32.SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;

            // Re-initialise magnifier engine after sleep/hibernate resume.
            // WM_POWERBROADCAST / PBT_APMRESUMESUSPEND fires when the OS wakes.
            Microsoft.Win32.SystemEvents.PowerModeChanged += OnPowerModeChanged;

            // Bug fix: the virtual-screen-bounds cache (MagnifierEngine) and the
            // cursor-highlighter overlay window were only refreshed via the ~1/sec
            // monitor-name poll in CheckMonitorChange(), which only fires when the
            // LENS ITSELF crosses onto a differently-named monitor. A monitor
            // unplugged/added/resized while the lens stayed put left both stale.
            // Subscribe directly so any display/DPI topology change is caught
            // immediately regardless of where the lens currently sits.
            Microsoft.Win32.SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;

            InitializeComponent();
            DataContext = this;

            // Set window icon from embedded resource — pack:// URI fails in
            // single-file publish; GetManifestResourceStream works reliably.
            try
            {
                var asm = System.Reflection.Assembly.GetExecutingAssembly();
                using var stream = asm.GetManifestResourceStream("SuperMagnifier.app.ico");
                if (stream != null)
                {
                    var decoder = System.Windows.Media.Imaging.BitmapDecoder.Create(
                        stream,
                        System.Windows.Media.Imaging.BitmapCreateOptions.PreservePixelFormat,
                        System.Windows.Media.Imaging.BitmapCacheOption.OnLoad);
                    Icon = decoder.Frames[0];
                }
            }
            catch { /* non-fatal — window shows without icon */ }

            ApplySettingsToUi(_settings);
            _uiInitialized = true;

            // Restore main window position (saved on close)
            if (_settings.MainWindowX.HasValue && _settings.MainWindowY.HasValue)
            {
                // Clamp to virtual screen so window isn't on a detached monitor
                double safeX = Math.Max(SystemParameters.VirtualScreenLeft,
                    Math.Min(_settings.MainWindowX.Value,
                        SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth  - 100));
                double safeY = Math.Max(SystemParameters.VirtualScreenTop,
                    Math.Min(_settings.MainWindowY.Value,
                        SystemParameters.VirtualScreenTop  + SystemParameters.VirtualScreenHeight - 100));
                Left = safeX;
                Top  = safeY;
            }

            // WCAG 2.4.6: section headers get descriptive AutomationProperties.Name
            // and heading level so screen readers announce them as labelled regions.
            SetSectionName(HeadMagnification,  "Section: Magnification");
            SetSectionName(HeadFollowMode,     "Section: Follow Mode");
            SetSectionName(HeadLensPresets,    "Section: Lens Presets");
            SetSectionName(HeadQuickToggles,   "Section: Quick Toggles");
            SetSectionName(HeadReticle,        "Section: Reticle");
            SetSectionName(HeadColourFilter,   "Section: Colour Filter");
            SetSectionName(HeadFocusRing,      "Section: Focus Ring");
            SetSectionName(HeadSettingsFile,   "Section: Settings File");

            // Lens — restore per-monitor position if available
            _lens = new LensWindow
            {
                Width   = _settings.DefaultLensW,
                Height  = _settings.DefaultLensH,
                Left    = _settings.LensX,
                Top     = _settings.LensY,
                Topmost = true,
            };
            _lens.MagReady        += OnMagReady;
            _lens.ViewSizeChanged += OnViewSizeChanged;
            _lens.FreezeToggled   += OnFreezeToggled;
            _lens.CloseRequested  += OnLensCloseRequested;
            _lens.ZoomInRequested  += (s, _) => ZoomBy(+_zoomStep);
            _lens.ZoomOutRequested += (s, _) => ZoomBy(-_zoomStep);
            _lens.SizeCycleRequested += (s, _) => CycleLensSize();
            // Show hex+RGB toast when colour is picked from lens
            _lens.ColourPicked    += (s, colourText) =>
            {
                ShowToast($"Copied colour: {colourText}", ms: 3500);
                // Push picked hex into colour history (first token is the hex)
                if (!colourText.StartsWith("⚠"))
                {
                    string pickedHex = colourText.Split(' ')[0];
                    AddToColourHistory(pickedHex);
                }
            };
            _lens.ViewSizeChanged += (s, _) => { if (_reticleAttached) UpdateReticleOverlay(); else ApplyReticle(); };
            _lens.SetBorderColor(_settings.LensBorderColor);
            _lens.SetBorderThickness((int)_settings.LensBorderThick);
            _lens.SetResizeGripAppearance(_settings.LensGripColor, _settings.LensGripSize);
            _lens.SetLensOpacity(_settings.LensOpacity);
            ApplyBarTextVisibility();
            _lens.Show();   // show briefly so DPI scale is resolved
            _lens.Hide();

            // Clamp lens to visible virtual screen — recovers from detached monitor
            ClampLensToScreen();

            // Restore colour history swatches from saved settings
            _lens.LoadColourHistory(_settings.ColourHistory);

            // Cursor highlighter
            _cursorHL = new CursorHighlighter();
            if (ColourPicker.TryParseColor(_settings.CursorColor, out var cursorCol))
                _cursorHL.SetColor(cursorCol);
            if (_settings.CursorHighlight) _cursorHL.SetEnabled(true);

            // Reading ruler
            _ruler = new ReadingRuler();
            if (_settings.ReadingRuler) _ruler.SetEnabled(true);

            // Caret tracker
            _caretTracker = new CaretTracker(Dispatcher);

            // Timer
            _timer = new DispatcherTimer(DispatcherPriority.Render)
            {
                Interval = TimeSpan.FromMilliseconds(16)
            };
            _timer.Tick += TimerTick;

            BuildTray();
            Loaded += OnLoaded;
        }

        private void ApplySettingsToUi(AppSettings s)
        {
            _zoom      = s.DefaultZoom;
            _colorMode = s.ColorMode;
            SetZoomDisplay(_zoom);
            if (ZoomSlider != null) ZoomSlider.Value = _zoom;
            SetStepRadio(s.Step);                               // updates StepDisplay label
            if (HighlightCheck != null) HighlightCheck.IsChecked = s.CursorHighlight;
            if (RulerCheck     != null) RulerCheck.IsChecked     = s.ReadingRuler;
            if (LensOpacitySlider != null) { LensOpacitySlider.Value = s.LensOpacity; UpdateOpacityLabel(s.LensOpacity); }
            if (LensFollowMouse != null) LensFollowMouse.IsChecked = s.FollowMode != "caret";
            if (LensFollowCaret != null) LensFollowCaret.IsChecked = s.FollowMode == "caret";
            if (EdgeDeadZoneCheck != null) EdgeDeadZoneCheck.IsChecked = s.EdgeDeadZone;
            if (BorderThickSlider != null) BorderThickSlider.Value    = s.LensBorderThick;
            if (BorderThickLabel  != null) BorderThickLabel.Text      = $"{(int)s.LensBorderThick}px";
            if (GripSizeSlider    != null) GripSizeSlider.Value       = s.LensGripSize;
            if (GripSizeLabel     != null) GripSizeLabel.Text         = $"{(int)s.LensGripSize}px";
            if (GripColorLabel    != null) GripColorLabel.Text        = s.LensGripColor;
            if (GripColorSwatch != null && ColourPicker.TryParseColor(s.LensGripColor, out var gripCol))
                UpdateColorSwatch(GripColorSwatch, gripCol);
            if (RulerHeightSlider != null) RulerHeightSlider.Value    = s.RulerHeight;
            if (FpsCombo          != null) SetFpsCombo(s.UpdateRateFps);
            if (MirrorCheck       != null) MirrorCheck.IsChecked           = s.MirrorLens;
            // Reticle — defer to RestoreReticleUi (called from startup path)
            // Colour swatches
            try
            {
                var rc = (System.Windows.Media.Color)System.Windows.Media.ColorConverter
                    .ConvertFromString(s.RulerColorHex);
                UpdateColorSwatch(RulerColorSwatch,
                    System.Windows.Media.Color.FromArgb(80, rc.R, rc.G, rc.B));
            } catch (System.Exception _ex) { System.Diagnostics.Debug.WriteLine($"[SuperMagnifier] {_ex.Message}"); }
            try
            {
                var hc = (System.Windows.Media.Color)System.Windows.Media.ColorConverter
                    .ConvertFromString(s.CursorColor);
                UpdateColorSwatch(HighlightColorSwatch, hc);
            } catch (System.Exception _ex) { System.Diagnostics.Debug.WriteLine($"[SuperMagnifier] {_ex.Message}"); }
            try
            {
                var pc = (System.Windows.Media.Color)System.Windows.Media.ColorConverter
                    .ConvertFromString(s.CursorPulseColor);
                UpdateColorSwatch(PulseColorSwatch, pc);
            } catch (System.Exception _ex) { System.Diagnostics.Debug.WriteLine($"[SuperMagnifier] {_ex.Message}"); }
            SetColorRadio(s.ColorMode);
            Topmost = s.AlwaysOnTop;
            _uiSizeIndex = Math.Max(0, Math.Min(UiScales.Length - 1, s.UiSizeIndex));
            _themeName = !string.IsNullOrEmpty(s.ThemeName) ? s.ThemeName
                       : (s.DarkMode ? "Dark" : "Light");
            _isDark    = !App.IsLightTheme(_themeName);
            if (_isDark) _lastDarkTheme = _themeName;
            ApplyUiScale();
            // Theme label updated by UpdateThemeNavLabel once the window is loaded
            // ZoomAnimate and OpacityFollowZoom are settings-only — applied via _settings field
            // OpacityFollowZoom — settings-only, no main-panel control
            // Focus ring colour swatch restored later in OnLoaded after ApplyFocusRingColor
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            // Show current theme name in the nav button
            UpdateThemeNavLabel();

            // Window icon already set in constructor via GetManifestResourceStream
            RegisterHotkeys();
            // Clear SizeToContent after initial layout so user can freely resize vertically.
            // Lock Width/MaxWidth so slide-out panels overlay rather than push window wider.
            SizeToContent = SizeToContent.Manual;
            MaxWidth = ActualWidth;
            Width    = ActualWidth;
            RestoreLensPosition();
            _lens.SetTitleBarSize(_settings.TitleBarSizeIndex);
            UpdateLensTitleSizeLabel();
            ApplyAutoStart(_settings.AutoStart);

            // Keyboard lens mover (arrow keys, works in click-through mode)
            _keyboardMover = new LensKeyboardMover(_lens, this) { ResizeRequested = ResizeLens };

            // Presets
            _presets = LensPreset.LoadAll();
            _presetBtns = new[] { Preset1Btn, Preset2Btn, Preset3Btn, Preset4Btn, Preset5Btn };
            RefreshPresetButtons();

            // Apply new settings
            _lens.SetBorderThickness(_settings.LensBorderThick);
            _ruler.SetHeight(_settings.RulerHeight);
            if (ColourPicker.TryParseColor(_settings.RulerColorHex, out var rc))
                _ruler.SetColor(System.Windows.Media.Color.FromArgb(80, rc.R, rc.G, rc.B));
            ApplyUpdateRate(_settings.UpdateRateFps);

            // App-based auto presets
            _appPresetMap = new AppPresetMap();
            _appPresetMap.SetRules(AppPresetMap.Deserialise(_settings.AppPresetMap));
            _appPresetMap.PresetRequested += slot => Dispatcher.Invoke(() =>
            {
                LoadPreset(slot);
                ShowTrayBalloon($"Preset loaded: {(_presets[slot].IsEmpty ? $"Preset {slot+1}" : _presets[slot].Name)}");
            });
            if (_appPresetMap.Rules.Count > 0) _appPresetMap.Start();

            // Per-app lens position memory
            _appPositionMap = new AppPositionMap();
            _appPositionMap.SetEntries(AppPositionMap.Deserialise(_settings.AppPositionMap));
            _appPositionMap.PositionRequested += entry => Dispatcher.Invoke(() =>
            {
                var (left, top) = MonitorHelper.ResolvePosition(
                    entry.MonName, entry.RelX, entry.RelY,
                    _lens.DpiScaleX, _lens.Width, _lens.Height);
                _lens.Left = left;
                _lens.Top  = top;
            });
            _appPositionMap.Start();

            // Wire lens move to save position for current app
            _lens.LocationChanged += (s2, e2) =>
                _appPositionMap?.SaveCurrentPosition(_lens.Left, _lens.Top, _lens.DpiScaleX);

            // Cursor pulse
            _cursorPulse = new CursorPulse();
            try
            {
                var pc = (System.Windows.Media.Color)System.Windows.Media.ColorConverter
                    .ConvertFromString(_settings.CursorPulseColor);
                _cursorPulse.SetColor(pc);
            }
            catch (System.Exception _ex) { System.Diagnostics.Debug.WriteLine($"[SuperMagnifier] {_ex.Message}"); }

            // Mirror lens
            if (_settings.MirrorLens) ApplyMirrorLens(true);

            // Reticle overlay
            RestoreReticleUi();

            // Lens shape / track mode
            RestoreTrackModeUi();

            // Feature: Dim when idle
            InitDimTimer();

            // Feature: Zoom lock — restore from settings
            _zoomMin = _settings.ZoomLockMin;
            _zoomMax = _settings.ZoomLockMax;
            if (ZoomLockMinSlider != null) ZoomLockMinSlider.Value = _zoomMin;
            if (ZoomLockMaxSlider != null) ZoomLockMaxSlider.Value = _zoomMax;
            if (ZoomLockMinLabel  != null) ZoomLockMinLabel.Text  = $"{_zoomMin:F2}×";
            if (ZoomLockMaxLabel  != null) ZoomLockMaxLabel.Text  = $"{_zoomMax:F2}×";
            UpdateZoomLockIndicator();

            // Feature: Tint intensity
            _tintIntensity = _settings.TintIntensity;
            if (TintSlider != null) TintSlider.Value = _tintIntensity;
            if (TintLabel  != null) TintLabel.Text   = $"{(int)(_tintIntensity * 100)}%";

            // Feature: Home zoom
            if (HomeZoomLabel != null) HomeZoomLabel.Text = $"{_settings.HomeZoom:F1}×";

            // Feature: Snap-to-word — restore session value, then apply startup default if pinned.
            // DefaultSnapToWord only overrides when the user has explicitly set it true;
            // otherwise we restore the last-used session value.
            _snapToWord = _settings.DefaultSnapToWord ? true : _settings.SnapToWord;
            if (SnapToWordCheck != null) SnapToWordCheck.IsChecked = _snapToWord;

            // Startup defaults: UI size (-1 = remember last, already applied above)
            if (_settings.DefaultUiSizeIndex >= 0)
            {
                _uiSizeIndex = Math.Max(0, Math.Min(UiScales.Length - 1, _settings.DefaultUiSizeIndex));
                ApplyUiScale();
            }
            // Lens title bar size: always applied at startup — no "remember last" sentinel.
            _lens.SetTitleBarSize(_settings.DefaultTitleBarSizeIndex);
            UpdateLensTitleSizeLabel();

            // Startup defaults: follow mode and reticle.
            // Follow mode default is only applied when NOT doing a session restore —
            // session restore preserves the exact state from the previous run.
            bool sessionWillRestore = _settings.SessionRestore && _settings.SessionActive;
            if (!sessionWillRestore)
            {
                if (_settings.DefaultFollowMode == "caret" && LensFollowCaret != null)
                    LensFollowCaret.IsChecked = true;
                else if (LensFollowMouse != null)
                    LensFollowMouse.IsChecked = true;
            }

            if (_settings.DefaultReticle && ReticleCheck != null &&
                ReticleCheck.IsChecked != true)
                ReticleCheck.IsChecked = true;

            // Session restore — resume exact state from last run
            if (_settings.ClickThrough) { _lens.SetClickThrough(true); UpdateClickThruCheck(); }
            if (_settings.SessionRestore && _settings.SessionActive) Toggle();

            // WCAG 2.3.3 — read OS "Show animations in Windows" preference
            _reduceMotion = !SystemParameters.ClientAreaAnimation;

            // Accessibility: apply system HC and font size on startup in case
            // they were already active when the app launched
            ApplyHighContrastIfNeeded();
            ApplySystemFontScale();

            // ── Low-Vision: app-wide font family + explicit HUD font size ──
            App.SetAppFontFamily(_settings.AppFontFamily);
            App.SetHudFontSize(_settings.HudFontSize);

            // ── New features initialisation ───────────────────────────────────

            // Per-monitor zoom memory
            _monitorZoomMap = PerMonitorZoomMap.Deserialise(_settings.PerMonitorZoomMap);

            // Focus ring colour — adapt default to current theme
            EnsureFocusRingContrastForTheme();
            ApplyFocusRingColor(_settings.FocusRingColor);
            UpdateFocusRingColorSwatch();

            // Hook mouse wheel on lens for scroll-zoom (and invert toggle)
            _lens.MouseWheel += LensMouseWheel;


            // First-run wizard
            if (!_settings.WizardShown)
            {
                _settings.WizardShown = true;
                _settings.Save();
                var wizard = new FirstRunWizard { Owner = this };
                wizard.ShowDialog();
            }

            // SM-12: one-time notice when Load() moved untouched Ctrl-only defaults to Ctrl+Alt.
            if (_settings.HotkeysMigrated)
            {
                _settings.HotkeysMigrated = false;
                _settings.Save();   // persists HotkeySchemeVersion = 2 so the notice shows once
                Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, new System.Action(() =>
                    ShowToast($"Hotkeys moved to Ctrl+Alt so Ctrl+F, Ctrl+T, Ctrl+P and other shortcuts work in your other apps again. " +
                              $"Magnifier on/off is now {Chord("Toggle")}. Change them in Hotkeys.", ms: 60000)));
            }

            // What's New dialog — show once per build version
            string currentVer = AboutDialog.Version;
            if (!string.IsNullOrEmpty(currentVer) &&
                currentVer != _settings.LastSeenVersion &&
                _settings.WizardShown) // skip on very first run (wizard covers it)
            {
                _settings.LastSeenVersion = currentVer;
                _settings.Save();
                Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background,
                    new System.Action(() =>
                    {
                        new WhatsNewDialog { Owner = this }.ShowDialog();
                    }));
            }

            // Settings migration toast — shown once when upgrading from an older build.
            // Use AppSettings.CurrentSettingsVersion as the single source of truth.
            if (_settings.WizardShown && _settings.SettingsVersion < AppSettings.CurrentSettingsVersion)
            {
                _settings.SettingsVersion = AppSettings.CurrentSettingsVersion;
                _settings.Save();
                Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background,
                    new System.Action(() =>
                        ShowToast("New options available — check Settings for details", ms: 5000)));
            }
        }

        /// <summary>Ensures the lens title bar is within the virtual screen bounds
        /// so it is always reachable after a monitor is disconnected.</summary>
        private void ClampLensToScreen()
        {
            try
            {
                double vLeft   = SystemParameters.VirtualScreenLeft;
                double vTop    = SystemParameters.VirtualScreenTop;
                double vRight  = vLeft + SystemParameters.VirtualScreenWidth;
                double vBottom = vTop  + SystemParameters.VirtualScreenHeight;
                double margin  = 40; // keep at least 40px of title bar visible
                _lens.Left = Math.Max(vLeft, Math.Min(_lens.Left, vRight  - margin));
                _lens.Top  = Math.Max(vTop,  Math.Min(_lens.Top,  vBottom - margin));
            }
            catch (System.Exception ex) { System.Diagnostics.Debug.WriteLine($"ClampLensToScreen: {ex.Message}"); }
        }

        /// <summary>
        /// Cycles the lens window size in 3 steps: Default → 2× → Default.
        /// The "default" baseline is whatever size the lens was at when the
        /// cycle started (so a manual resize between cycles becomes the new default).
        /// Doubled size is clamped to the work area so the lens never grows off-screen.
        /// </summary>
        private void CycleLensSize()
        {
            if (_lens == null) return;
            try
            {
                if (!_lensSizeDoubled)
                {
                    _lensCycleBaseW = _lens.Width;
                    _lensCycleBaseH = _lens.Height;
                    var wa = SystemParameters.WorkArea;
                    _lens.Width  = Math.Min(_lensCycleBaseW * 2, wa.Width);
                    _lens.Height = Math.Min(_lensCycleBaseH * 2, wa.Height);
                    _lensSizeDoubled = true;
                    ShowTrayBalloon("Lens size: 2×", ms: 900);
                }
                else
                {
                    _lens.Width  = _lensCycleBaseW;
                    _lens.Height = _lensCycleBaseH;
                    _lensSizeDoubled = false;
                    ShowTrayBalloon("Lens size: Default", ms: 900);
                }
                ClampLensToScreen();
            }
            catch (System.Exception ex) { System.Diagnostics.Debug.WriteLine($"CycleLensSize: {ex.Message}"); }
        }

        /// <summary>
        /// Pushes the current lens-bar text-visibility settings (master + per-element)
        /// down to the lens window.
        /// </summary>
        private void ApplyBarTextVisibility()
        {
            _lens?.SetBarTextVisibility(
                _settings.BarHideAllText,
                _settings.BarShowSizeLabel,
                _settings.BarShowColourLabel,
                _settings.BarShowSnapLabel,
                _settings.BarShowClickThruLabel,
                _settings.BarShowFreezeLabel,
                _settings.BarShowZoomValue,
                _settings.BarShowHoverHint);
        }

        private void RestoreLensPosition()
        {
            if (string.IsNullOrEmpty(_settings.LensMonitor)) return;
            try
            {
                var (left, top) = MonitorHelper.ResolvePosition(
                    _settings.LensMonitor, _settings.LensRelX, _settings.LensRelY,
                    _lens.DpiScaleX, _lens.Width, _lens.Height);
                _lens.Left = left;
                _lens.Top  = top;
            }
            catch (System.Exception _ex) { System.Diagnostics.Debug.WriteLine($"[SuperMagnifier] {_ex.Message}"); }
        }

        // ── Settings panel slide-out ──────────────────────────────────────────
        private void PanelTabBtn_Click(object sender, RoutedEventArgs e)
        {
            _panelOpen = !_panelOpen;
            if (_panelOpen)
            {
                SlidePanel.Visibility = Visibility.Visible;
                if (!_reduceMotion)
                {
                    var anim = new DoubleAnimation(0, TimeSpan.FromMilliseconds(220))
                        { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
                    SlidePanelTranslate.BeginAnimation(System.Windows.Media.TranslateTransform.XProperty, anim);
                }
                else SlidePanelTranslate.X = 0;
                PanelTabText.Text = "Less";
            }
            else
            {
                if (!_reduceMotion)
                {
                    var anim = new DoubleAnimation(260, TimeSpan.FromMilliseconds(180))
                        { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } };
                    anim.Completed += (s2, e2) => SlidePanel.Visibility = Visibility.Collapsed;
                    SlidePanelTranslate.BeginAnimation(System.Windows.Media.TranslateTransform.XProperty, anim);
                }
                else { SlidePanelTranslate.X = 260; SlidePanel.Visibility = Visibility.Collapsed; }
                PanelTabText.Text = "More";
            }
        }

        // ── Lens panel slide-out ──────────────────────────────────────────────
        private void LensPanelTabBtn_Click(object sender, RoutedEventArgs e)
        {
            _lensPanelOpen = !_lensPanelOpen;
            if (_lensPanelOpen)
            {
                LensPanel.Visibility = Visibility.Visible;
                if (!_reduceMotion)
                {
                    var anim = new DoubleAnimation(0, TimeSpan.FromMilliseconds(220))
                        { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
                    LensPanelTranslate.BeginAnimation(System.Windows.Media.TranslateTransform.XProperty, anim);
                }
                else LensPanelTranslate.X = 0;
                LensPanelTabText.Text      = "Hide";
                LensPanelTabBtn.Foreground = (System.Windows.Media.Brush)FindResource("BrushBlueText");
            }
            else
            {
                if (!_reduceMotion)
                {
                    var anim = new DoubleAnimation(-260, TimeSpan.FromMilliseconds(180))
                        { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } };
                    anim.Completed += (s2, e2) => LensPanel.Visibility = Visibility.Collapsed;
                    LensPanelTranslate.BeginAnimation(System.Windows.Media.TranslateTransform.XProperty, anim);
                }
                else { LensPanelTranslate.X = -260; LensPanel.Visibility = Visibility.Collapsed; }
                LensPanelTabText.Text      = "Lens";
                LensPanelTabBtn.Foreground = (System.Windows.Media.Brush)FindResource("BrushHint");
            }
        }

        // ── Lens panel button handlers ────────────────────────────────────────
        private void ClickThru_Changed(object sender, RoutedEventArgs e)
        {
            if (ClickThruCheck == null) return;
            _lens.SetClickThrough(ClickThruCheck.IsChecked == true);
        }

        private void UpdateClickThruCheck()
        {
            if (ClickThruCheck != null)
                ClickThruCheck.IsChecked = _lens.IsClickThrough;
        }

        private void LensSnapBtn_Click(object sender, RoutedEventArgs e)
            => _lens.SaveSnapshot();

        private void LensTitleSizeBtn_Click(object sender, RoutedEventArgs e)
        {
            int next = (_lens.TitleBarSizeIndex + 1) % LensWindow.TitleHeights.Length;
            _lens.SetTitleBarSize(next);
            UpdateLensTitleSizeLabel();
        }

        private void UpdateLensTitleSizeLabel()
        {
            if (LensTitleSizeLabel == null) return;
            LensTitleSizeLabel.Text = LensWindow.TitleSizeNames[
                Math.Max(0, Math.Min(LensWindow.TitleSizeNames.Length - 1, _lens.TitleBarSizeIndex))];
        }

        // Dock buttons delegate to LensWindow
        private void DockTL_Click(object s, RoutedEventArgs e) => _lens.DockTo(LensWindow.DockCorner.TopLeft);
        private void DockTR_Click(object s, RoutedEventArgs e) => _lens.DockTo(LensWindow.DockCorner.TopRight);
        private void DockBL_Click(object s, RoutedEventArgs e) => _lens.DockTo(LensWindow.DockCorner.BottomLeft);
        private void DockBR_Click(object s, RoutedEventArgs e) => _lens.DockTo(LensWindow.DockCorner.BottomRight);
        private void BuildTray()
        {
            // Load app icon from embedded resource — works in single-file publish
            System.Drawing.Icon appIcon = null;
            try
            {
                var asm = System.Reflection.Assembly.GetExecutingAssembly();
                using var icoStream = asm.GetManifestResourceStream("SuperMagnifier.app.ico");
                if (icoStream != null)
                    appIcon = new System.Drawing.Icon(icoStream);
            }
            catch { /* fall back to default */ }

            _tray = new NotifyIcon
            {
                Icon    = appIcon ?? SystemIcons.Application,
                Text    = "Super Magnifier LV",
                Visible = true,
            };
            var menu = new ContextMenuStrip();
            menu.Items.Add("Open Super Magnifier LV", null, (s, e) => Dispatcher.Invoke(() =>
            {
                if (!IsVisible) { Show(); Activate(); }
                else { Activate(); WindowState = WindowState.Normal; }
            }));
            menu.Items.Add("Show / Hide Control Panel", null, (s, e) => Dispatcher.Invoke(ToggleControlPanel));
            menu.Items.Add($"Toggle Magnifier ({Chord("Toggle")})",  null, (s, e) => Dispatcher.Invoke(Toggle));
            menu.Items.Add($"Freeze / Resume ({Chord("Freeze")})",   null, (s, e) => Dispatcher.Invoke(() => { if (_active) _lens.ToggleFreeze(); }));
            menu.Items.Add(new ToolStripSeparator());

            // Preset quick-load submenu (rebuilt on open in RebuildTrayPresets)
            var presetsMenu = new ToolStripMenuItem("Load Preset");
            menu.Items.Add(presetsMenu);
            menu.Items.Add(new ToolStripSeparator());

            menu.Items.Add("Recent messages…", null, (s, e) => Dispatcher.Invoke(() => { if (!IsVisible) Show(); ShowRecentMessages(); }));
            menu.Items.Add("Exit", null, (s, e) => Dispatcher.Invoke(() =>
            {
                _isReallyClosing = true;
                Close();
            }));
            _tray.ContextMenuStrip = menu;
            _tray.DoubleClick += (s, e) => Dispatcher.Invoke(() =>
            {
                if (!IsVisible) { Show(); Activate(); }
                else ToggleControlPanel();
            });

            // Populate preset submenu once (call again after preset save)
            menu.Opening += (s, e) => RebuildTrayPresets(presetsMenu);
        }

        /// <summary>Rebuilds the tray preset submenu from the current preset list.</summary>
        private void RebuildTrayPresets(ToolStripMenuItem presetsMenu)
        {
            presetsMenu.DropDownItems.Clear();
            bool any = false;
            if (_presets != null)
            {
                for (int i = 0; i < _presets.Count; i++)
                {
                    var p = _presets[i];
                    if (p.IsEmpty) continue;
                    int slot = i;
                    string followIndicator = p.FollowMode == "caret" ? " [C]" : "";
                    var item = new ToolStripMenuItem($"{slot + 1}: {p.Name}  ({p.Zoom:F1}×{followIndicator})");
                    item.Click += (s, e) => Dispatcher.Invoke(() => LoadPreset(slot));
                    presetsMenu.DropDownItems.Add(item);
                    any = true;
                }
            }
            if (!any)
                presetsMenu.DropDownItems.Add(new ToolStripMenuItem("(no presets saved)") { Enabled = false });
        }

        private void ToggleControlPanel()
        {
            if (WindowState == WindowState.Minimized || !IsVisible)
            { Show(); WindowState = WindowState.Normal; Activate(); }
            else
            { WindowState = WindowState.Minimized; }
        }

        // ── Hotkeys — see MainWindow.Hotkeys.cs ─────────────────────────────

        // ── Mag ready ─────────────────────────────────────────────────────────
        private void OnMagReady(object sender, EventArgs e)
        {
            _engine.SetDpiScale(_lens.DpiScaleX, _lens.DpiScaleY);
            var h = _lens.MagHwnd;
            if (h == IntPtr.Zero) return;
            _engine.ApplyZoom(h, (float)_zoom);
            _engine.ApplyColorEffect(h, _settings.RenderMode == "smooth", _colorMode, _tintIntensity);
            // Re-apply reticle now that the canvas is rendered and live
            ApplyReticle();
        }

        private void OnViewSizeChanged(object sender, EventArgs e)
        {
            var h = _lens.MagHwnd;
            if (h != IntPtr.Zero) _engine.ApplyZoom(h, (float)_zoom);

        }

        private void OnFreezeToggled(object sender, EventArgs e)
        {
            bool frozen = _lens.Frozen;
            // Icon + word; frozen = PrimaryButton (pressed look) and a Name that states the state.
            FreezeBtnIcon.Source = JtIcons.Get(frozen ? "queueNow" : "freeze");
            FreezeBtnText.Text   = frozen ? "Resume" : "Freeze";
            FreezeBtn.Style      = (Style)FindResource(frozen ? "PrimaryButton" : "FlatButton");
            FreezeBtn.ToolTip    = frozen ? $"Resume ({Chord("Freeze")})" : $"Freeze ({Chord("Freeze")})";
            System.Windows.Automation.AutomationProperties.SetName(FreezeBtn,
                frozen ? "Resume lens (lens is frozen)" : "Freeze lens");
        }

        private void OnLensCloseRequested(object sender, EventArgs e)
        {
            // Lens X button was clicked — if we're active, sync toggle button back
            if (_active) Toggle();
        }

        private void TimerTick(object sender, EventArgs e)
        {
            if (!_active) return;

            var hwnd = new WindowInteropHelper(_lens).Handle;
            if (hwnd != IntPtr.Zero)
                SetWindowPos(hwnd, HWND_TOPMOST, 0, 0, 0, 0,
                             SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_SHOWWINDOW);

            // Reticle draws even when frozen — position overlay every tick
            if (_reticleOverlay != null && (_settings.ReticleEnabled || _showReticleDot))
                TickUpdateReticle();

            if (_lens.Frozen) return;

            // ── Track mode: move the lens to follow the cursor ────────────────
            if (_lens.CurrentTrackMode != LensWindow.TrackMode.Fixed)
            {
                GetCursorPos(out POINT tcp);
                if (tcp.X != _lastTrackX || tcp.Y != _lastTrackY)
                {
                    _lastTrackX = tcp.X;
                    _lastTrackY = tcp.Y;
                    _lens.MoveToFollow(tcp.X, tcp.Y);
                }
            }

            // ── Dim when idle: watch cursor movement ──────────────────────────
            if (_settings.DimWhenIdle)
            {
                GetCursorPos(out POINT cp);
                var cur = new System.Windows.Point(cp.X, cp.Y);
                if (cur != _lastCursorPos)
                {
                    _lastCursorPos = cur;
                    UndimLens();
                    _dimTimer?.Stop();
                    _dimTimer?.Start();
                }
            }

            bool useCaret = LensFollowCaret?.IsChecked == true
                            && _caretTracker.HasPosition;

            bool smooth    = _settings.SmoothPanning;
            bool deadZone  = _settings.EdgeDeadZone;
            double panLag  = _settings.PanLag;

            if (useCaret)
            {
                int focusX = _caretTracker.CaretX;
                int focusY = _caretTracker.CaretY;
                // Use cached word focus from background thread
                if (_snapToWord && _wordFocusX.HasValue)
                { focusX = _wordFocusX.Value; focusY = _wordFocusY.Value; }
                _engine.UpdateSource(_lens.MagHwnd, (float)_zoom,
                                     _lens.ViewWidth, _lens.ViewHeight,
                                     focusX, focusY, smooth, panLag, deadZone);
            }
            else
            {
                // Snap-to-word uses cached result; background task refreshes it
                int? snapX = _snapToWord ? _wordFocusX : null;
                int? snapY = _snapToWord ? _wordFocusY : null;
                _engine.UpdateSource(_lens.MagHwnd, (float)_zoom,
                                     _lens.ViewWidth, _lens.ViewHeight,
                                     snapX, snapY, smooth, panLag, deadZone);
            }

            if (_snapToWord)
            {
                try
                {
                    GetCursorPos(out POINT cp);
                    if (WordTracker.TryGetWordFocus(cp.X, cp.Y, out int rx, out int ry))
                    { _wordFocusX = rx; _wordFocusY = ry; }
                }
                catch (System.Exception ex) { CrashLog.Write("WordTracker", ex); }
            }

            // Keep lens informed of the current source rect for snapshot
            _lens.SetSourceRect(_engine.LastSrcLeft, _engine.LastSrcTop,
                                _engine.LastSrcRight, _engine.LastSrcBottom);

            // Mirror lens — same source rect, same zoom & colour
            _mirrorLens?.UpdateSource(_engine.LastSrcLeft, _engine.LastSrcTop,
                                      _engine.LastSrcRight, _engine.LastSrcBottom,
                                      (float)_zoom);

            // Per-monitor zoom memory: detect if lens moved to a different monitor
            CheckMonitorChange();
        }

        // ── Toggle ────────────────────────────────────────────────────────────
        private void ToggleBtn_Click(object sender, RoutedEventArgs e) => Toggle();

        private void Toggle()
        {
            _active = !_active;
            if (_active)
            {
                _engine.SetDpiScale(_lens.DpiScaleX, _lens.DpiScaleY);
                var h = _lens.MagHwnd;
                _engine.ApplyZoom(h, (float)_zoom);
                _engine.ApplyColorEffect(h, _settings.RenderMode == "smooth", _colorMode, _tintIntensity);
                _lens.Show();
                if (_settings.ReticleEnabled || _showReticleDot)
                {
                    EnsureReticleOverlay();
                    ApplyReticle();
                }
                _timer.Start();
                ToggleBtnIcon.Source = JtIcons.Get("terminate");
                ToggleBtnText.Text   = "Disable Magnifier";
                System.Windows.Automation.AutomationProperties.SetName(ToggleBtn, "Disable Magnifier");
                ToggleBtn.Style   = (Style)System.Windows.Application.Current.Resources["DangerButton"];
                _tray.Text        = "Super Magnifier LV — Active";
            }
            else
            {
                _timer.Stop();
                if (_reticleOverlay != null)
                    _reticleOverlay.Visibility = Visibility.Collapsed;
                _lens.Hide();
                ToggleBtnIcon.Source = JtIcons.Get("queueNow");
                ToggleBtnText.Text   = "Enable Magnifier";
                System.Windows.Automation.AutomationProperties.SetName(ToggleBtn, "Enable Magnifier");
                ToggleBtn.Style   = (Style)System.Windows.Application.Current.Resources["PrimaryButton"];
                _tray.Text        = "Super Magnifier LV";
            }
        }

        private void FreezeBtn_Click(object sender, RoutedEventArgs e)
        {
            if (_active)
            {
                _lens.ToggleFreeze();
                UpdateLensTitleBar(); // reflect Frozen state in title
            }
        }

        // ── Zoom — see MainWindow.Zoom.cs ───────────────────────────────────
        // ── Follow mode ───────────────────────────────────────────────────────
        private void Follow_Checked(object sender, RoutedEventArgs e)
        {
            if (_caretTracker == null) return;
            bool useCaret = LensFollowCaret?.IsChecked == true;
            _caretTracker.SetEnabled(useCaret);
        }

        // ── Lens shape (track mode) ───────────────────────────────────────────
        private void TrackMode_Changed(object sender, RoutedEventArgs e)
        {
            if (_lens == null) return;
            ApplyTrackMode();
        }

        private void ApplyTrackMode()
        {
            if (_lens == null) return;

            LensWindow.TrackMode mode;
            if (TrackCircle?.IsChecked == true)      mode = LensWindow.TrackMode.Circle;
            else if (TrackSquare?.IsChecked == true) mode = LensWindow.TrackMode.Square;
            else                                     mode = LensWindow.TrackMode.Fixed;

            int size = (int)(TrackSizeSlider?.Value ?? 300);

            // Show/hide the size slider panel
            if (TrackSizePanel != null)
                TrackSizePanel.Visibility = mode == LensWindow.TrackMode.Fixed
                    ? System.Windows.Visibility.Collapsed
                    : System.Windows.Visibility.Visible;

            _lens.SetTrackMode(mode, size);
            _settings.LensTrackMode = mode == LensWindow.TrackMode.Circle ? "circle"
                                    : mode == LensWindow.TrackMode.Square  ? "square"
                                    : "fixed";
            _settings.LensTrackSize = size;

            // Auto-enable click-through in following modes so the cursor can interact
            // with the application beneath the lens naturally.
            // Auto-disable when returning to fixed mode.
            if (mode != LensWindow.TrackMode.Fixed)
            {
                if (ClickThruCheck != null && ClickThruCheck.IsChecked != true)
                {
                    ClickThruCheck.IsChecked = true;  // fires ClickThru_Changed → SetClickThrough
                    ShowToast("Click-through enabled for follow mode", ms: 2000);
                }
            }
            else
            {
                if (ClickThruCheck != null && ClickThruCheck.IsChecked == true)
                    ClickThruCheck.IsChecked = false; // fires ClickThru_Changed → SetClickThrough
            }

            // Disable keyboard mover in track mode — lens follows cursor automatically
            if (_keyboardMover != null)
                _keyboardMover.Enabled = (mode == LensWindow.TrackMode.Fixed);

            // In track mode the cursor dot is always centred — no offset possible.
            // Suppress cursor dot in track mode (cursor always centred — dot unhelpful)
            _showReticleDot = (mode == LensWindow.TrackMode.Fixed);
        }

        private void TrackSizeSlider_ValueChanged(object sender,
            System.Windows.RoutedPropertyChangedEventArgs<double> e)
        {
            if (_lens == null) return;
            int size = (int)e.NewValue;
            if (TrackSizeLabel != null) TrackSizeLabel.Text = $"{size}px";
            _lens.SetTrackSize(size);
            _settings.LensTrackSize = size;
        }

        // ── Render mode ───────────────────────────────────────────────────────
        // ── Colour filter ─────────────────────────────────────────────────────
        private void Color_Checked(object sender, RoutedEventArgs e)
        {
            if (ColorNormal == null) return;
            if      (ColorNormal.IsChecked       == true) _colorMode = "normal";
            else if (ColorInvert.IsChecked       == true) _colorMode = "invert";
            else if (ColorGrayscale.IsChecked    == true) _colorMode = "grayscale";
            else if (ColorHighContrast.IsChecked == true) _colorMode = "highcontrast";
            else if (ColorDeutan.IsChecked       == true) _colorMode = "deuteranopia";
            else if (ColorProtan.IsChecked       == true) _colorMode = "protanopia";
            else if (ColorTritan.IsChecked       == true) _colorMode = "tritanopia";
            else if (ColorWarmAmber.IsChecked    == true) _colorMode = "warmamber";
            else if (ColorCoolBlue.IsChecked     == true) _colorMode = "coolblue";
            if (_engine != null && _lens != null)
                _engine.ApplyColorEffect(_lens.MagHwnd, _settings.RenderMode == "smooth", _colorMode, _tintIntensity);
        }

        private void SetColorRadio(string mode)
        {
            if (ColorNormal == null) return;
            ColorNormal.IsChecked       = mode == "normal";
            ColorInvert.IsChecked       = mode == "invert";
            ColorGrayscale.IsChecked    = mode == "grayscale";
            ColorHighContrast.IsChecked = mode == "highcontrast";
            ColorDeutan.IsChecked       = mode == "deuteranopia";
            ColorProtan.IsChecked       = mode == "protanopia";
            ColorTritan.IsChecked       = mode == "tritanopia";
            ColorWarmAmber.IsChecked    = mode == "warmamber";
            ColorCoolBlue.IsChecked     = mode == "coolblue";
            _colorMode = mode;
        }

        // Cycle through colour modes with a single hotkey (Ctrl+Shift+C)
        private static readonly string[] ColorModes =
            { "normal", "grayscale", "invert", "highcontrast", "warmamber", "coolblue",
              "deuteranopia", "protanopia", "tritanopia" };
        private void CycleColorMode()
        {
            int idx = System.Array.IndexOf(ColorModes, _colorMode);
            string next = ColorModes[(idx + 1) % ColorModes.Length];
            SetColorRadio(next);
            if (_active)
                _engine.ApplyColorEffect(_lens.MagHwnd, _settings.RenderMode == "smooth", next, _tintIntensity);
            ShowToast($"Colour mode: {next}", ms: 1800);
        }

        private void OpacitySlider_ValueChanged(object sender,
            RoutedPropertyChangedEventArgs<double> e)
        {
            UpdateOpacityLabel(e.NewValue);
            if (_lens != null) _lens.SetLensOpacity(e.NewValue);
        }

        private void UpdateOpacityLabel(double v)
        {
            string text = $"{(int)(v * 100)}%";
            if (LensOpacityLabel != null) LensOpacityLabel.Text = text;
        }

        /// <summary>Ctrl+[ / Ctrl+] — nudge lens opacity by delta, clamped 0.2–1.0.</summary>
        private void AdjustOpacity(double delta)
        {
            if (_lens == null) return;
            double current = LensOpacitySlider != null ? LensOpacitySlider.Value : _lens.Opacity;
            double next    = Math.Max(0.2, Math.Min(1.0, current + delta));
            if (LensOpacitySlider != null) LensOpacitySlider.Value = next;
            _lens.SetLensOpacity(next);
            UpdateOpacityLabel(next);
            ShowToast($"Opacity: {(int)(next * 100)}%", ms: 1000);
        }

        /// <summary>Hotkey handler — toggles Follow Mouse ↔ Follow Caret (Ctrl+L).</summary>
        private void ToggleFollowMode()
        {
            bool toCaret = LensFollowMouse?.IsChecked == true;
            if (toCaret) { if (LensFollowCaret != null) LensFollowCaret.IsChecked = true; }
            else         { if (LensFollowMouse != null) LensFollowMouse.IsChecked = true; }
            ShowToast(toCaret ? "Follow: Text Caret" : "Follow: Mouse Cursor", ms: 1200);
        }

        /// <summary>Hotkey handler — toggles reticle overlay on/off (Ctrl+R).</summary>
        private void ToggleReticle()
        {
            if (ReticleCheck == null) return;
            ReticleCheck.IsChecked = !(ReticleCheck.IsChecked == true);
            ShowToast(ReticleCheck.IsChecked == true ? "Reticle: On" : "Reticle: Off", ms: 1000);
        }

        private void LensMouseWheel(object sender, System.Windows.Input.MouseWheelEventArgs e)
        {
            bool invert = _settings.InvertScrollZoom;
            double delta = (e.Delta > 0) ? _zoomStep : -_zoomStep;
            if (invert) delta = -delta;
            ZoomBy(delta);
            e.Handled = true;
        }

        // ── Opacity follow zoom ───────────────────────────────────────────────

        // ── Per-monitor zoom memory ───────────────────────────────────────────
        private int _monitorCheckFrames = 0;
        private void CheckMonitorChange()
        {
            // Throttle to ~1 check per second (timer runs at 60fps)
            if (++_monitorCheckFrames < 60) return;
            _monitorCheckFrames = 0;
            try
            {
                string mon = PerMonitorZoomMap.GetMonitorName(
                    _lens.Left, _lens.Top, _lens.DpiScaleX, _lens.DpiScaleY);
                if (string.IsNullOrEmpty(mon)) return;
                if (mon == _lastMonitorName) return;
                _lastMonitorName = mon;
                // Monitor changed — refresh virtual screen bounds and DPI scale
                _engine.InvalidateVirtualScreenCache();
                _engine.SetDpiScale(_lens.DpiScaleX, _lens.DpiScaleY);
                if (_monitorZoomMap.TryGetValue(mon, out double savedZoom))
                {
                    SetZoom(savedZoom, animate: false);
                    ShowToast($"Monitor: {savedZoom:F1}×", ms: 1200);
                }
            }
            catch (System.Exception _ex) { System.Diagnostics.Debug.WriteLine($"[SuperMagnifier] {_ex.Message}"); }
        }

        // ── Customisable focus ring colour ────────────────────────────────────
        private void FocusRingColorBtn_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new System.Windows.Forms.ColorDialog
            {
                Color = HexToDrawingColor(_settings.FocusRingColor), FullOpen = true
            };
            if (dlg.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
            var col = dlg.Color;
            _settings.FocusRingColor = $"#{col.R:X2}{col.G:X2}{col.B:X2}";
            ApplyFocusRingColor(_settings.FocusRingColor);
            UpdateFocusRingColorSwatch();
        }

        private void ApplyFocusRingColor(string hex)
        {
            if (!ColourPicker.TryParseColor(hex, out var col)) return;
            var brush = new System.Windows.Media.SolidColorBrush(col);
            System.Windows.Application.Current.Resources["FocusRingBrush"] = brush;
        }

        // Ensure default focus ring colour meets ≥3:1 contrast (WCAG 2.4.11)
        // against the current theme's surface colours. Called whenever theme switches.
        private void EnsureFocusRingContrastForTheme()
        {
            // Only adjust when the user is still using one of the two built-in defaults.
            // If they picked a custom colour we leave it alone.
            const string darkDefault  = "#89B4FA";
            const string lightDefault = "#0D47A1";
            bool isBuiltIn = _settings.FocusRingColor.Equals(darkDefault,  System.StringComparison.OrdinalIgnoreCase)
                          || _settings.FocusRingColor.Equals(lightDefault, System.StringComparison.OrdinalIgnoreCase);
            if (!isBuiltIn) return;

            string ideal = _isDark ? darkDefault : lightDefault;
            if (!_settings.FocusRingColor.Equals(ideal, System.StringComparison.OrdinalIgnoreCase))
            {
                _settings.FocusRingColor = ideal;
                ApplyFocusRingColor(ideal);
                UpdateFocusRingColorSwatch();
            }
        }

        private void UpdateFocusRingColorSwatch()
        {
            if (FocusRingColorSwatch == null) return;
            if (!ColourPicker.TryParseColor(_settings.FocusRingColor, out var col)) return;
            FocusRingColorSwatch.Fill = new System.Windows.Media.SolidColorBrush(col);
        }

        // ── Keyboard lens resize (WCAG 2.5.7 drag alternative) ───────────────
        // Ctrl+Shift+Arrow while SML is foreground → LensKeyboardMover.ResizeRequested.
        // Optional global ResizeUp/Down/Left/Right bindings register in RegisterHotkeys (SM-03).
        private void ResizeLens(int dw, int dh)
        {
            if (_lens == null) return;
            double newW = Math.Max(200, Math.Min(1920, _lens.Width  + dw));
            double newH = Math.Max(120, Math.Min(1080, _lens.Height + dh));
            _lens.Width  = newW;
            _lens.Height = newH;
            ShowToast($"Lens: {(int)newW}×{(int)newH}", ms: 800);
        }
        // ── Reading Ruler ─────────────────────────────────────────────────────
        private void Ruler_Changed(object sender, RoutedEventArgs e)
        {
            _ruler?.SetEnabled(RulerCheck.IsChecked == true);
        }

        // ── Cursor highlight ──────────────────────────────────────────────────
        private void Highlight_Changed(object sender, RoutedEventArgs e)
        {
            _cursorHL?.SetEnabled(HighlightCheck.IsChecked == true);
        }

        // ── Hotkeys ───────────────────────────────────────────────────────────
        private void Hotkey_Changed(object sender, RoutedEventArgs e)
        {
            if (_settings.HotkeyToggle) RegisterHotkeys();
            else DisposeHotkeys();
        }

        private void AboutBtn_Click(object sender, RoutedEventArgs e)
        {
            new AboutDialog { Owner = this }.ShowDialog();
        }

        // F1 opens the Hotkeys dialog from anywhere in the control panel
        protected override void OnKeyDown(System.Windows.Input.KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.Key == System.Windows.Input.Key.F1)
            {
                e.Handled = true;
                RemapHotkeysBtn_Click(this, new System.Windows.RoutedEventArgs());
            }
        }

        // ── Theme flyout ──────────────────────────────────────────────────────
        private static readonly System.Collections.Generic.Dictionary<string, string> s_themeNames
            = new()
            {
            ["Dark"] = "Dark",
            ["OneDark"] = "One Dark",
            ["GruvboxDark"] = "Gruvbox Dark",
            ["TokyoNight"] = "Tokyo Night",
            ["Nord"] = "Nord",
            ["Dracula"] = "Dracula",
            ["CatppuccinMocha"] = "Catppuccin Mocha",
            ["RosePine"] = "Rosé Pine",
            ["AyuDark"] = "Ayu Dark",
            ["AyuMirage"] = "Ayu Mirage",
            ["Cobalt2"] = "Cobalt 2",
            ["Monokai"] = "Monokai",
            ["SolarizedDark"] = "Solarized Dark",
            ["Kanagawa"] = "Kanagawa",
            ["EverforestDark"] = "Everforest Dark",
            ["Oxocarbon"] = "Oxocarbon",
            ["Palenight"] = "Palenight",
            ["GitHubDark"] = "GitHub Dark",
            ["DraculaPro"] = "Dracula Pro",
            ["Horizon"] = "Horizon",
            ["Abyss"] = "Abyss",
            ["Obsidian"] = "Obsidian",
            ["Panda"] = "Panda",
            ["Monochrome"] = "Monochrome",
            ["Light"] = "Light",
            ["WarmLight"] = "Warm Light",
            ["CatppuccinLatte"] = "Catppuccin Latte",
            ["GitHubLight"] = "GitHub Light",
            ["AyuLight"] = "Ayu Light",
            ["SolarizedLight"] = "Solarized Light",
            ["RosePineDawn"] = "Rose Pine Dawn",
            ["EverforestLight"] = "Everforest Light",
            ["PaperWhite"] = "Paper White",
            ["Sepia"] = "Sepia",
            ["Arctic"] = "Arctic",
            ["Ivory"] = "Ivory",
            ["HighContrast"] = "High Contrast",
            ["HighContrastLight"] = "HC Light",
            ["HCBlue"] = "HC Blue",
            ["HCGreen"] = "HC Green",
            ["HCAmber"] = "HC Amber",
            ["Synthwave"] = "Synthwave",
            ["Cyberpunk"] = "Cyberpunk",
            ["NeonCity"] = "Neon City",
            ["VaporWave"] = "VaporWave",
            ["Outrun"] = "Outrun",
            ["HoloBlue"] = "Holo Blue",
            ["Matrix"] = "Matrix",
            ["NeonPurple"] = "Neon Purple",
            ["LaserGrid"] = "Laser Grid",
            ["ElectricOcean"] = "Electric Ocean",
            ["Forest"] = "Forest",
            ["Ocean"] = "Ocean",
            ["DesertSand"] = "Desert Sand",
            ["Autumn"] = "Autumn",
            ["MidnightGarden"] = "Midnight Garden",
            ["LavenderField"] = "Lavender Field",
            ["CoralReef"] = "Coral Reef",
            ["Tundra"] = "Tundra",
            ["Volcanic"] = "Volcanic",
            ["Meadow"] = "Meadow",
            ["C64"] = "C64",
            ["AmberCRT"] = "Amber CRT",
            ["GreenCRT"] = "Green CRT",
            ["IBMBlue"] = "IBM Blue",
            ["AppleII"] = "Apple II",
            ["BIOS"] = "BIOS",
            ["Teletext"] = "Teletext",
            ["TerminalRed"] = "Terminal Red",
            ["VGAPurple"] = "VGA Purple",
            ["ZXSpectrum"] = "ZX Spectrum",
            ["Ember"] = "Ember",
            ["Rosewood"] = "Rosewood",
            ["GoldenHour"] = "Golden Hour",
            ["Terracotta"] = "Terracotta",
            ["Rust"] = "Rust",
            ["Mahogany"] = "Mahogany",
            ["Cinnamon"] = "Cinnamon",
            ["Wine"] = "Wine",
            ["Copper"] = "Copper",
            ["Sundowner"] = "Sundowner",
            ["MidnightBlue"] = "Midnight Blue",
            ["DeepSpace"] = "Deep Space",
            ["Glacier"] = "Glacier",
            ["Cobalt"] = "Cobalt",
            ["Sapphire"] = "Sapphire",
            ["Periwinkle"] = "Periwinkle",
            ["ArcticFox"] = "Arctic Fox",
            ["Steel"] = "Steel",
            ["Slate"] = "Slate",
            ["IndigoNight"] = "Indigo Night",
            ["CottonCandy"] = "Cotton Candy",
            ["MintCream"] = "Mint Cream",
            ["BabyBlue"] = "Baby Blue",
            ["Peach"] = "Peach",
            ["Lilac"] = "Lilac",
            ["Buttercup"] = "Buttercup",
            ["Blush"] = "Blush",
            ["Seafoam"] = "Seafoam",
            ["Wisteria"] = "Wisteria",
            ["Dusk"] = "Dusk",
            ["CoffeeShop"] = "Coffee Shop",
            ["Denim"] = "Denim",
            ["Malachite"] = "Malachite",
            ["SolarFlare"] = "Solar Flare",
            ["Moonstone"] = "Moonstone",
            ["Bubblegum"] = "Bubblegum"
            };

        private void ApplyThemeName(string key)
        {
            _themeName = key ?? "Dark";
            _isDark    = !App.IsLightTheme(_themeName);
            App.SetTheme(_themeName);
            UpdateThemeNavLabel();
        }

        private void UpdateThemeNavLabel()
        {
            // Button label is always "◑ Theme" — current theme shown via tooltip only.
            if (ThemeNavBtn == null) return;
            ThemeNavBtn.ToolTip = "Theme: " + s_themeNames.GetValueOrDefault(_themeName, _themeName);
        }

        /// <summary>Opens / closes the theme flyout popup.</summary>
        private void ThemeBtn_Click(object sender, RoutedEventArgs e)
        {
            var ctx = new System.Windows.Controls.ContextMenu
            {
                PlacementTarget = ThemeNavBtn,
                Placement       = System.Windows.Controls.Primitives.PlacementMode.Bottom,
            };
            string cur = _themeName ?? "Dark";

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
                        Header     = (act ? "✓  " : "    ") + d,
                        FontWeight = act ? FontWeights.SemiBold : FontWeights.Normal,
                        Padding    = new Thickness(8, 6, 24, 6),
                    };
                    mi.Click += (_, __) => { ApplyThemeName(key); RefreshPresetButtons(); EnsureFocusRingContrastForTheme(); };
                    sub.Items.Add(mi);
                }
                ctx.Items.Add(sub);
            }
            ctx.IsOpen = true;
        }

        /// <summary>
        /// Moves keyboard focus into the popup so WPF's StaysOpen=False logic
        /// treats the popup as "focused" and does not immediately close it.
        /// </summary>
        private void ThemePopup_Opened(object sender, EventArgs e)
        {
            // C-3/A-1 (SuperMagnifierLV_CodeSecA11y_Review_2026-09-14): this call was dropped
            // somewhere along the way — without it every theme button's .Content is never set,
            // so the popup renders 19 blank rows (sighted users) with no accessible name at all
            // (screen readers fall back to Content for the automation name). One-line fix.
            UpdateThemePopupItems();
            TI_Dark?.Focus();
        }

        /// <summary>Called when a row in the theme flyout is clicked.</summary>
        private void ThemeItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not System.Windows.Controls.Button btn
                || btn.Tag is not string key) return;

            ThemePopup.IsOpen = false;
            ApplyThemeName(key);
            RefreshPresetButtons();
            UpdateZoomLockIndicator();
            EnsureFocusRingContrastForTheme();
            ShowToast($"Theme: {s_themeNames.GetValueOrDefault(key, key)}");
        }

        /// <summary>Stamps a checkmark on the active item before the popup opens.</summary>
        private void UpdateThemePopupItems()
        {
            System.Windows.Controls.Button[] items =
            {
                TI_Dark, TI_OneDark, TI_Gruvbox, TI_Tokyo, TI_Nord,
                TI_Dracula, TI_Mocha, TI_RosePine,
                TI_AyuDark, TI_AyuMirage, TI_Cobalt2, TI_Monokai, TI_SolarizedDark,
                TI_Light, TI_WarmLight, TI_Latte, TI_GitHub,
                TI_AyuLight, TI_SolarizedLight
            };
            foreach (var btn in items)
            {
                string key         = btn.Tag?.ToString() ?? "";
                bool   active      = key == _themeName;
                string displayName = s_themeNames.GetValueOrDefault(key, key);
                btn.Content    = (active ? "✓  " : "    ") + displayName;
                btn.FontWeight = active ? FontWeights.SemiBold : FontWeights.Normal;
                // Explicit AutomationProperties.Name, independent of Content — the Content
                // fallback is exactly what let this bug go unnoticed (A-5: don't rely on it).
                System.Windows.Automation.AutomationProperties.SetName(btn,
                    (active ? "Selected: " : "") + displayName + " theme");
            }
        }

        // ── Lens Presets ──────────────────────────────────────────────────────
        private void LoadPreset(int idx)
        {
            if (idx < 0 || idx >= _presets.Count) return;
            var p = _presets[idx];
            if (p.IsEmpty) return;

            // Apply zoom lock from preset
            _zoomMin = p.ZoomMin;
            _zoomMax = p.ZoomMax;
            UpdateZoomLockIndicator();

            SetZoom(p.Zoom);
            _lens.Width  = p.LensW;
            _lens.Height = p.LensH;
            SetColorRadio(p.ColorMode);
            _engine?.ApplyColorEffect(_lens.MagHwnd, _settings.RenderMode == "smooth", _colorMode, _tintIntensity);
            _lens.SetLensOpacity(p.LensOpacity);
            if (LensOpacitySlider != null) LensOpacitySlider.Value = p.LensOpacity;

            // Apply follow mode
            if (p.FollowMode == "caret") { if (LensFollowCaret != null) LensFollowCaret.IsChecked = true; }
            else                         { if (LensFollowMouse != null) LensFollowMouse.IsChecked = true; }

            // Apply snap-to-word
            _snapToWord = p.SnapToWord;
            if (SnapToWordCheck != null) SnapToWordCheck.IsChecked = _snapToWord;

            // Apply reticle
            if (ReticleCheck != null) ReticleCheck.IsChecked = p.ReticleEnabled;

            // Tray notification
            ShowTrayBalloon($"Preset loaded: {p.Name}", ms: 1500);

            // Mirror lens — sync colour effect
            _mirrorLens?.ApplyColorEffect(_settings.RenderMode == "smooth", _colorMode, _tintIntensity);

            // Brief highlight on the button so user sees which preset loaded
            if (_presetBtns != null && idx < _presetBtns.Length)
            {
                var btn = _presetBtns[idx];
                btn.Foreground = TryGetThemeBrush("BrushGreenText")
                    ?? new SolidColorBrush(System.Windows.Media.Color.FromRgb(166, 227, 161));
                var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
                t.Tick += (s2, e2) => { t.Stop(); RefreshPresetButtons(); };
                t.Start();
            }
        }

        /// <summary>
        /// Saves the current state to a preset slot without prompting for a name.
        /// Used by Ctrl+Shift+1–5 hotkeys so the global shortcut never opens a dialog.
        /// </summary>
        private void SavePresetSilent(int idx)
        {
            if (idx < 0 || idx >= _presets.Count) return;
            string name = _presets[idx].IsEmpty ? $"Preset {idx + 1}" : _presets[idx].Name;
            _presets[idx] = new LensPreset
            {
                Name           = name,
                Zoom           = _zoom,
                LensW          = (int)_lens.Width,
                LensH          = (int)_lens.Height,
                ColorMode      = _colorMode,
                LensOpacity    = _lens.Opacity,
                ZoomMin        = _zoomMin,
                ZoomMax        = _zoomMax,
                FollowMode     = LensFollowCaret?.IsChecked == true ? "caret" : "mouse",
                SnapToWord     = _snapToWord,
                ReticleEnabled = ReticleCheck?.IsChecked == true,
            };
            if (!LensPreset.SaveAll(_presets))
                ShowToast("⚠ Could not save preset — check disk space", ms: 4000);
            RefreshPresetButtons();
            ShowToast($"Preset {idx + 1} saved: {name}", ms: 1500);
        }

        private void SavePreset(int idx)
        {
            if (idx < 0 || idx >= _presets.Count) return;
            // Prompt for a name
            string existing = _presets[idx].IsEmpty ? $"Preset {idx + 1}" : _presets[idx].Name;
            var dlg = new InputDialog("Name this preset:", existing) { Owner = this };
            if (dlg.ShowDialog() != true) return;
            string name = string.IsNullOrWhiteSpace(dlg.Result) ? $"Preset {idx + 1}" : dlg.Result;
            _presets[idx] = new LensPreset
            {
                Name           = name,
                Zoom           = _zoom,
                LensW          = (int)_lens.Width,
                LensH          = (int)_lens.Height,
                ColorMode      = _colorMode,
                LensOpacity    = _lens.Opacity,
                ZoomMin        = _zoomMin,
                ZoomMax        = _zoomMax,
                FollowMode     = LensFollowCaret?.IsChecked == true ? "caret" : "mouse",
                SnapToWord     = _snapToWord,
                ReticleEnabled = ReticleCheck?.IsChecked == true,
            };
            if (!LensPreset.SaveAll(_presets))
                ShowToast("⚠ Could not save presets — check disk space", ms: 4000);
            RefreshPresetButtons();

            // Flash the button green to confirm save
            if (_presetBtns != null && idx < _presetBtns.Length)
            {
                var btn = _presetBtns[idx];
                btn.Foreground = TryGetThemeBrush("BrushGreenText")
                    ?? new SolidColorBrush(System.Windows.Media.Color.FromRgb(166, 227, 161));
                var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(700) };
                t.Tick += (s2, e2) => { t.Stop(); RefreshPresetButtons(); };
                t.Start();
            }
        }

        private void RefreshPresetButtons()
        {
            if (_presetBtns == null) return;

            // Resolve colours from active theme so they work in both dark and light mode
            var clrEmpty  = TryGetThemeBrush("BrushMuted")  // dim but readable on BrushSurf
                            ?? new SolidColorBrush(System.Windows.Media.Color.FromRgb(147, 153, 178));
            var clrFilled = TryGetThemeBrush("BrushBlueText")   // accent colour for named presets
                            ?? new SolidColorBrush(System.Windows.Media.Color.FromRgb(137, 180, 250));

            for (int i = 0; i < _presetBtns.Length; i++)
            {
                var p   = _presets[i];
                var btn = _presetBtns[i];
                if (p.IsEmpty)
                {
                    btn.Content    = $"{i + 1}";
                    System.Windows.Automation.AutomationProperties.SetName(btn, $"Preset {i + 1}, empty");
                    btn.ToolTip    = $"Preset {i + 1} — empty\nShift+click to save · {Chord($"Preset{i + 1}Load")} to load";
                    btn.Foreground = clrEmpty;
                }
                else
                {
                    string label = p.Name.Length > 8 ? p.Name.Substring(0, 7) + "…" : p.Name;
                    string followStr = p.FollowMode == "caret" ? "Caret" : "Mouse";
                    string extras = "";
                    if (p.SnapToWord)     extras += " · Snap";
                    if (p.ReticleEnabled) extras += " · Reticle";
                    // Option B: 16px follow-mode icon + name (mouse vs caret visible without the tooltip)
                    btn.Content    = JtIcons.Label(btn, label, p.FollowMode == "caret" ? "followCaret" : "followMouse", 16);
                    System.Windows.Automation.AutomationProperties.SetName(btn,
                        $"Preset {i + 1}, {p.Name}, follows {(p.FollowMode == "caret" ? "text caret" : "mouse")}");
                    btn.ToolTip    = $"{p.Name}: {p.Zoom:F1}× · {p.LensW}×{p.LensH} · {p.ColorMode} · {followStr}{extras}\n{Chord($"Preset{i + 1}Load")} to load · Shift+click to rename/overwrite";
                    btn.Foreground = clrFilled;
                }
            }
        }

        /// <summary>Returns a SolidColorBrush from the active theme resource dictionary, or null.</summary>
        private SolidColorBrush TryGetThemeBrush(string key)
        {
            try { return System.Windows.Application.Current.Resources[key] as SolidColorBrush; }
            catch { return null; }
        }

        // ── Auto-start with Windows ───────────────────────────────────────────
        private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string AppName = "Super Magnifier LV";

        private void ApplyAutoStart(bool enable)
        {
            try
            {
                using (var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RunKey, true))
                {
                    if (key == null) return;
                    if (enable)
                    {
                        string exe = Environment.ProcessPath
                                     ?? AppContext.BaseDirectory;
                        key.SetValue(AppName, $"\"{exe}\"");
                    }
                    else
                    {
                        if (key.GetValue(AppName) != null)
                            key.DeleteValue(AppName, false);
                    }
                }
            }
            catch { /* silently ignore — may lack registry access */ }
        }

        // ── UI size cycling ───────────────────────────────────────────────────
        private void SizeBtn_Click(object sender, RoutedEventArgs e)
        {
            _uiSizeIndex = (_uiSizeIndex + 1) % UiScales.Length;
            ApplyUiScale();
        }

        /// <summary>
        /// Fix 4: Triangular resize handle — drag-to-resize the window (spec Section 4).
        /// Uses Win32 WM_SYSCOMMAND SC_SIZE to delegate native resize behaviour.
        /// </summary>
        private void ResizeThumb_DragDelta(object sender, DragDeltaEventArgs e)
        {
            double newW = Math.Max(MinWidth,  ActualWidth  + e.HorizontalChange);
            double newH = Math.Max(MinHeight, ActualHeight + e.VerticalChange);
            Width  = newW;
            Height = newH;
        }

        private void ApplyUiScale()
        {
            if (UiScale == null) return;
            double s = UiScales[_uiSizeIndex];
            UiScale.ScaleX = s;
            UiScale.ScaleY = s;
            if (SizeBtn != null)
                SizeBtn.Content = $"⊞ {UiScaleNames[_uiSizeIndex]} ({s:0.0#}×)";

            // Compact/Standard/Medium (index 0-2): no cap — window grows to fit all
            // content, no scrollbar. Large/Extra Large (index 3-4): cap to working
            // screen area so an oversized panel scrolls instead of running offscreen.
            if (MainScrollViewer != null)
            {
                if (_uiSizeIndex >= 3)
                {
                    var screen = System.Windows.SystemParameters.WorkArea;
                    MainScrollViewer.MaxHeight = (screen.Height - 32) / s;
                }
                else
                {
                    MainScrollViewer.MaxHeight = double.PositiveInfinity;
                }
            }

            // Re-lock window width AND height after scale change — must clear MaxWidth
            // first so SizeToContent can re-measure the transformed content at the new
            // scale, then snap both dimensions. (Height must be re-measured too, not just
            // Width — otherwise the window stays frozen at whatever height it had before
            // the scale change, leaving a blank gap below shrunk content at Compact/
            // Standard/Medium, or a clipped panel at Large/Extra Large.)
            if (IsLoaded)
            {
                MaxWidth  = double.PositiveInfinity;
                MaxHeight = double.PositiveInfinity;
                SizeToContent = SizeToContent.WidthAndHeight;
                Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, new Action(() =>
                {
                    SizeToContent = SizeToContent.Manual;
                    MaxWidth  = ActualWidth;
                    Width     = ActualWidth;
                    MaxHeight = ActualHeight;
                    Height    = ActualHeight;
                }));
            }
        }

        // ── Settings dialog ───────────────────────────────────────────────────
        private void SettingsBtn_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new SettingsDialog(_settings, ApplySettings)
            {
                Owner        = this,
                CurrentZoom  = _zoom,
                CurrentLensW = (int)_lens.Width,
                CurrentLensH = (int)_lens.Height,
            };
            dlg.ShowDialog();
        }

        /// <summary>
        /// Gives a section-header TextBlock a descriptive accessible name and
        /// heading level so screen readers announce them as labelled regions (WCAG 2.4.6).
        /// </summary>
        private static void SetSectionName(
            System.Windows.Controls.TextBlock tb, string name)
        {
            System.Windows.Automation.AutomationProperties.SetName(tb, name);
        }

        private void ApplySettings(AppSettings updated)        {
            _settings = updated;
            _lens.Width  = updated.DefaultLensW;
            _lens.Height = updated.DefaultLensH;
            SetZoom(updated.DefaultZoom);
            SetColorRadio(updated.ColorMode);
            // RenderMode applied via _settings — radios now in SettingsDialog
            _engine.ApplyColorEffect(_lens.MagHwnd, updated.RenderMode == "smooth", updated.ColorMode, _tintIntensity);
            // AlwaysOnTop applied via Topmost below — AotCheck removed from main UI
            Topmost               = updated.AlwaysOnTop;
            _lens.Topmost         = updated.AlwaysOnTop;
            _lens.SetBorderColor(updated.LensBorderColor);
            _lens.SetResizeGripAppearance(updated.LensGripColor, updated.LensGripSize);
            _lens.SetLensOpacity(updated.LensOpacity);
            if (LensOpacitySlider   != null) { LensOpacitySlider.Value = updated.LensOpacity; UpdateOpacityLabel(updated.LensOpacity); }
            // HotkeyToggle now in SettingsDialog only
            // SmoothPanning, PanLag, DimWhenIdle, DimDelay, DimOpacity — now in SettingsDialog
            SetStepRadio(updated.Step);
            _themeName = !string.IsNullOrEmpty(updated.ThemeName) ? updated.ThemeName
                       : (updated.DarkMode ? "Dark" : "Light");
            _isDark    = !App.IsLightTheme(_themeName);
            if (_isDark) _lastDarkTheme = _themeName;
            App.SetTheme(_themeName);
            RegisterHotkeys();   // re-register in case HotkeyToggle changed
            ApplyAutoStart(updated.AutoStart);

            // Apply startup size defaults immediately if set (so the user sees the effect now)
            if (updated.DefaultUiSizeIndex >= 0)
            {
                _uiSizeIndex = Math.Max(0, Math.Min(UiScales.Length - 1, updated.DefaultUiSizeIndex));
                ApplyUiScale();
            }
            _lens.SetTitleBarSize(updated.DefaultTitleBarSizeIndex);
            UpdateLensTitleSizeLabel();

            // Apply lens bar text visibility (master toggle + per-element overrides)
            ApplyBarTextVisibility();
        }

        // ── Border thickness ──────────────────────────────────────────────────
        private void BorderThickSlider_ValueChanged(object sender,
            RoutedPropertyChangedEventArgs<double> e)
        {
            // Ignore the spurious fire caused by the XAML literal default
            // Value="2" during InitializeComponent — see _uiInitialized.
            if (!_uiInitialized) return;
            int t = (int)Math.Round(e.NewValue);
            _settings.LensBorderThick = t;
            _lens?.SetBorderThickness(t);
            if (BorderThickLabel != null) BorderThickLabel.Text = $"{t}px";
            _settings.Save(); // real-time — survives non-graceful exit
        }

        // ── Resize grip (size + colour) ───────────────────────────────────────
        private void GripSizeSlider_ValueChanged(object sender,
            RoutedPropertyChangedEventArgs<double> e)
        {
            // Ignore the spurious fire caused by the XAML literal default
            // Value="16" during InitializeComponent — see _uiInitialized.
            if (!_uiInitialized) return;
            int t = (int)Math.Round(e.NewValue);
            _settings.LensGripSize = t;
            _lens?.SetResizeGripAppearance(_settings.LensGripColor, t);
            if (GripSizeLabel != null) GripSizeLabel.Text = $"{t}px";
            _settings.Save(); // real-time — survives non-graceful exit
        }

        private void GripColorBtn_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            var dlg = new System.Windows.Forms.ColorDialog
            {
                Color = HexToDrawingColor(_settings.LensGripColor), FullOpen = true
            };
            if (dlg.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
            var col = dlg.Color;
            _settings.LensGripColor = $"#{col.R:X2}{col.G:X2}{col.B:X2}";
            var wpfColor = System.Windows.Media.Color.FromRgb(col.R, col.G, col.B);
            _lens?.SetResizeGripAppearance(_settings.LensGripColor, _settings.LensGripSize);
            UpdateColorSwatch(GripColorSwatch, wpfColor);
            if (GripColorLabel != null) GripColorLabel.Text = _settings.LensGripColor;
            _settings.Save(); // real-time — survives non-graceful exit
        }

        // ── Smooth panning ────────────────────────────────────────────────────
        // ── Edge dead zone ────────────────────────────────────────────────────
        private void EdgeDeadZoneCheck_Changed(object sender, System.Windows.RoutedEventArgs e)
            => _settings.EdgeDeadZone = EdgeDeadZoneCheck?.IsChecked == true;

        // ── Ruler height ──────────────────────────────────────────────────────
        private void RulerHeightSlider_ValueChanged(object sender,
            RoutedPropertyChangedEventArgs<double> e)
        {
            double h = e.NewValue;
            _settings.RulerHeight = h;
            _ruler?.SetHeight(h);
            if (RulerHeightLabel != null) RulerHeightLabel.Text = $"{(int)h}px";
        }

        // ── Frame rate ────────────────────────────────────────────────────────
        private void FpsCombo_SelectionChanged(object sender,
            System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (FpsCombo?.SelectedItem is System.Windows.Controls.ComboBoxItem ci &&
                ci.Tag is string t && int.TryParse(t, out int fps))
            {
                _settings.UpdateRateFps = fps;
                ApplyUpdateRate(fps);
            }
        }

        private void ApplyUpdateRate(int fps)
        {
            if (_timer == null) return;
            fps = Math.Max(5, Math.Min(60, fps));
            _timer.Interval = TimeSpan.FromMilliseconds(1000.0 / fps);
        }

        private void SetFpsCombo(int fps)
        {
            if (FpsCombo == null) return;
            foreach (System.Windows.Controls.ComboBoxItem ci in FpsCombo.Items)
                if (ci.Tag is string t && t == fps.ToString())
                { FpsCombo.SelectedItem = ci; return; }
            FpsCombo.SelectedIndex = FpsCombo.Items.Count - 1; // default 60
        }

        // ── Export / Import settings ──────────────────────────────────────────
        private void ExportSettingsBtn_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            var dlg = new System.Windows.Forms.SaveFileDialog
            {
                Title      = "Export Super Magnifier LV Settings",
                Filter     = "JSON settings|*.json",
                FileName   = "magnifier_settings.json",
            };
            if (dlg.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
            try { System.IO.File.Copy(AppSettings.FilePath, dlg.FileName, overwrite: true); }
            catch (Exception ex) { System.Windows.MessageBox.Show($"Export failed:\n{ex.Message}"); }
        }

        private void ImportSettingsBtn_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            var dlg = new System.Windows.Forms.OpenFileDialog
            {
                Title  = "Import Super Magnifier LV Settings",
                Filter = "JSON settings|*.json",
            };
            if (dlg.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
            try
            {
                System.IO.File.Copy(dlg.FileName, AppSettings.FilePath, overwrite: true);
                _settings = AppSettings.Load();
                ApplySettingsToUi(_settings);
                System.Windows.MessageBox.Show("Settings imported. Some changes take effect on next launch.", "Imported");
            }
            catch (Exception ex) { System.Windows.MessageBox.Show($"Import failed:\n{ex.Message}"); }
        }

        // ── Preset-only export / import ──────────────────────────────────────
        private void ExportPresetsBtn_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            var dlg = new System.Windows.Forms.SaveFileDialog
            {
                Title    = "Export Lens Presets",
                Filter   = "JSON presets|*.json",
                FileName = "magnifier_presets.json",
            };
            if (dlg.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
            try
            {
                if (!LensPreset.SaveAll(_presets, dlg.FileName))
                    ShowToast("⚠ Export failed — check the file path", ms: 4000);
                ShowToast("Presets exported");
            }
            catch (Exception ex) { System.Windows.MessageBox.Show($"Export failed:\n{ex.Message}"); }
        }

        private void ImportPresetsBtn_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            var dlg = new System.Windows.Forms.OpenFileDialog
            {
                Title  = "Import Lens Presets",
                Filter = "JSON presets|*.json",
            };
            if (dlg.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
            try
            {
                var imported = LensPreset.LoadAll(dlg.FileName);
                if (imported == null || imported.Count == 0)
                { System.Windows.MessageBox.Show("No presets found in that file."); return; }
                // Merge: fill slots 0-4, leave extras from import unused
                for (int i = 0; i < Math.Min(5, imported.Count); i++)
                    _presets[i] = imported[i];
                if (!LensPreset.SaveAll(_presets))
                    ShowToast("⚠ Could not save presets — check disk space", ms: 4000);
                RefreshPresetButtons();
                ShowToast($"Presets imported ({Math.Min(5, imported.Count)} slots)");
            }
            catch (Exception ex) { System.Windows.MessageBox.Show($"Import failed:\n{ex.Message}"); }
        }

        // ── App preset map ────────────────────────────────────────────────────
        private void AppPresetsBtn_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            var dlg = new AppPresetDialog(_appPresetMap.Rules) { Owner = this };
            if (dlg.ShowDialog() != true) return;
            _appPresetMap.SetRules(dlg.Result);
            _settings.AppPresetMap = _appPresetMap.Serialise();
            if (_appPresetMap.Rules.Count > 0) _appPresetMap.Start();
            else _appPresetMap.Stop();
        }

        private void RemapHotkeysBtn_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            // Temporarily disable hotkeys while the dialog is open so key captures
            // don't trigger the magnifier at the same time
            DisposeHotkeys();
            var dlg = new HotkeyRemapDialog(_settings.HotkeyBindings) { Owner = this };
            if (dlg.ShowDialog() == true && dlg.Result != null)
            {
                _settings.HotkeyBindings = dlg.Result;
                ShowToast("Hotkeys updated", ms: 1500);
            }
            // Re-register (with new or unchanged bindings)
            RegisterHotkeys();
        }

        // ── Preset naming (long-click Shift saves with name prompt) ───────────
        private int _lastSelectedPresetIdx = -1; // -1 = no preset explicitly selected yet

        private void Preset_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            if (!(sender is System.Windows.Controls.Button btn)) return;
            if (!int.TryParse(btn.Tag?.ToString(), System.Globalization.NumberStyles.Integer,
                    System.Globalization.CultureInfo.InvariantCulture, out int idx)) return;
            _lastSelectedPresetIdx = idx;
            bool shift = (System.Windows.Input.Keyboard.Modifiers &
                          System.Windows.Input.ModifierKeys.Shift) != 0;
            if (shift) SavePreset(idx);
            else        LoadPreset(idx);
        }

        /// <summary>
        /// Explicit Save button — requires user to first select a preset number.
        /// </summary>
        private void PresetSaveBtn_Click(object sender, RoutedEventArgs e)
        {
            if (_lastSelectedPresetIdx < 0)
            {
                ShowToast("Select a preset number (1–5) first, then click Save.");
                return;
            }
            SavePreset(_lastSelectedPresetIdx);
        }

        private void ShowTrayBalloon(string text, string title = "Super Magnifier LV", int ms = 1800)
        {
            // Use the in-UI toast for reliable feedback on all OS versions.
            ShowToast(text, ms);
        }

        /// <summary>
        /// Show a brief in-UI status message in the main panel footer toast area.
        /// Accessible: click anywhere on the toast to dismiss immediately (WCAG 2.2.1).
        /// Animations are skipped when the OS "Show animations" preference is off (WCAG 2.3.3).
        /// </summary>
        private DispatcherTimer _toastTimer;
        private readonly System.Collections.Generic.List<string> _recentToasts = new();
        private bool _toastHeld;

        /// <summary>WCAG 2.2.3: requested time is a floor — Settings decides 5 s (+60 ms/char), 10 s, or until dismissed.</summary>
        private int ToastDurationMs(string message, int requestedMs)
        {
            int secs = _settings?.ToastSeconds ?? 5;
            if (secs == 0) return 0;                                   // until dismissed
            int min = secs * 1000 + 60 * (message?.Length ?? 0);
            return Math.Max(requestedMs, min);
        }

        private void ShowToast(string message, int ms = 2200)
        {
            if (ToastBorder == null || ToastLabel == null) return;
            ToastLabel.Text        = message;
            ToastBorder.Visibility = Visibility.Visible;
            _recentToasts.Insert(0, $"{DateTime.Now:HH:mm:ss}  {message}");
            if (_recentToasts.Count > 20) _recentToasts.RemoveAt(_recentToasts.Count - 1);
            AnnounceStatus(message);
            ms = ToastDurationMs(message, ms);

            if (!_reduceMotion)
            {
                var fadeIn = new System.Windows.Media.Animation.DoubleAnimation(0, 1,
                    TimeSpan.FromMilliseconds(150));
                ToastBorder.BeginAnimation(OpacityProperty, fadeIn);
            }
            else
            {
                // No animation — snap visible and extend duration so slow readers have time
                ToastBorder.BeginAnimation(OpacityProperty, null);
                ToastBorder.Opacity = 1;
                ms = Math.Max(ms, 4000);
            }

            _toastTimer?.Stop();
            _toastTimer = null;
            if (ms <= 0) return;                                       // until dismissed
            _toastTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(ms) };
            _toastTimer.Tick += (s, _) => { if (!_toastHeld) DismissToast(); };
            _toastTimer.Start();
        }

        /// <summary>UIA notification so screen readers speak the status even without focus (WCAG 4.1.3).</summary>
        private void AnnounceStatus(string message)
        {
            try
            {
                var peer = System.Windows.Automation.Peers.UIElementAutomationPeer.FromElement(ToastBorder)
                        ?? System.Windows.Automation.Peers.UIElementAutomationPeer.CreatePeerForElement(ToastBorder);
                peer?.RaiseNotificationEvent(
                    System.Windows.Automation.AutomationNotificationKind.Other,
                    System.Windows.Automation.AutomationNotificationProcessing.ImportantMostRecent,
                    message, "SuperMagnifierStatus");
            }
            catch (Exception ex) { CrashLog.Write("AnnounceStatus", ex); }
        }

        // Hover / keyboard focus pauses the auto-dismiss timer (WCAG 2.2.1).
        private void ToastBorder_HoldChanged(object sender, System.Windows.Input.MouseEventArgs e)
            => _toastHeld = ToastBorder.IsMouseOver || ToastBorder.IsKeyboardFocusWithin;
        private void ToastBorder_FocusChanged(object sender, DependencyPropertyChangedEventArgs e)
            => _toastHeld = ToastBorder.IsMouseOver || ToastBorder.IsKeyboardFocusWithin;
        private void ToastBorder_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == System.Windows.Input.Key.Escape) { DismissToast(); e.Handled = true; }
        }
        private void ToastDismiss_Click(object sender, RoutedEventArgs e) => DismissToast();

        /// <summary>Tray → Recent messages… (WCAG 2.2.3: messages that timed out can be re-read).</summary>
        private void ShowRecentMessages()
        {
            string body = _recentToasts.Count == 0 ? "No messages yet." : string.Join(Environment.NewLine, _recentToasts);
            System.Windows.MessageBox.Show(this, body, "Recent messages — Super Magnifier LV", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        /// <summary>Immediately dismiss the toast — called by timer or click (WCAG 2.2.1).</summary>
        private void DismissToast()
        {
            _toastTimer?.Stop();
            _toastHeld = false;
            if (ToastBorder == null) return;
            if (!_reduceMotion)
            {
                var fadeOut = new System.Windows.Media.Animation.DoubleAnimation(1, 0,
                    TimeSpan.FromMilliseconds(200));
                fadeOut.Completed += (s2, _2) => ToastBorder.Visibility = Visibility.Collapsed;
                ToastBorder.BeginAnimation(OpacityProperty, fadeOut);
            }
            else
            {
                ToastBorder.BeginAnimation(OpacityProperty, null);
                ToastBorder.Opacity    = 0;
                ToastBorder.Visibility = Visibility.Collapsed;
            }
        }


        // ── Colour history ──────────────────────────────────────────────────────
        private const int ColourHistoryMax = 10;
        private void AddToColourHistory(string hex)
        {
            if (string.IsNullOrWhiteSpace(hex)) return;
            // ColourHistory is now a proper List<string> — no delimiter parsing needed
            var list = _settings.ColourHistory;
            list.RemoveAll(h => h.Equals(hex, System.StringComparison.OrdinalIgnoreCase));
            list.Insert(0, hex);
            if (list.Count > ColourHistoryMax) list.RemoveRange(ColourHistoryMax, list.Count - ColourHistoryMax);
            _lens.LoadColourHistory(list);
        }

        // ═══════════════════════════════════════════════════════════════════════
        // ── Reticle overlay ────────────────────────────────────────────────────
        // ═══════════════════════════════════════════════════════════════════════

        private static readonly string[] _reticleSizeNames =
            { "Small", "Medium", "Large", "X-Large", "XX-Large" };

        private void ReticleCheck_Changed(object sender, System.Windows.RoutedEventArgs e)
        {
            _settings.ReticleEnabled = ReticleCheck?.IsChecked == true;
            if (ReticleOptions != null)
                ReticleOptions.Visibility = _settings.ReticleEnabled
                    ? System.Windows.Visibility.Visible
                    : System.Windows.Visibility.Collapsed;
            ApplyReticle();
        }

        private void ReticleStyle_Changed(object sender, System.Windows.RoutedEventArgs e)
        {
            if (ReticleStyleX?.IsChecked      == true) _settings.ReticleStyle = "X";
            else if (ReticleStyleCircle?.IsChecked == true) _settings.ReticleStyle = "Circle";
            else                                             _settings.ReticleStyle = "Cross";
            ApplyReticle();
        }

        private void ReticleSizeDownBtn_Click(object sender, System.Windows.RoutedEventArgs e)
            => SetReticleSize(_settings.ReticleSize - 1);

        private void ReticleSizeUpBtn_Click(object sender, System.Windows.RoutedEventArgs e)
            => SetReticleSize(_settings.ReticleSize + 1);

        private void SetReticleSize(int size)
        {
            size = Math.Max(1, Math.Min(5, size));
            _settings.ReticleSize = size;
            if (ReticleSizeLabel    != null) ReticleSizeLabel.Text           = _reticleSizeNames[size - 1];
            if (ReticleSizeDownBtn  != null) ReticleSizeDownBtn.IsEnabled    = size > 1;
            if (ReticleSizeUpBtn    != null) ReticleSizeUpBtn.IsEnabled      = size < 5;
            ApplyReticle();
        }

        private void ReticleColorSwatch_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            var dlg = new System.Windows.Forms.ColorDialog { FullOpen = true, AnyColor = true };
            if (ColourPicker.TryParseColor(_settings.ReticleColor, out var initialReticleColor))
                dlg.Color = System.Drawing.Color.FromArgb(initialReticleColor.R, initialReticleColor.G, initialReticleColor.B);

            if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                var p = dlg.Color;
                _settings.ReticleColor = $"#{p.R:X2}{p.G:X2}{p.B:X2}";
                UpdateReticleColorSwatch();
                ApplyReticle();
            }
        }

        private void UpdateReticleColorSwatch()
        {
            if (ReticleColorSwatch == null) return;
            if (!ColourPicker.TryParseColor(_settings.ReticleColor, out var c)) return;
            var brush = new System.Windows.Media.SolidColorBrush(c);
            if (ReticleColorSwatch.Content is System.Windows.Shapes.Rectangle rect)
                rect.Fill = brush;
            else
                ReticleColorSwatch.Background = brush;
            if (ReticleColorLabel != null)
                ReticleColorLabel.Text = $"#{c.R:X2}{c.G:X2}{c.B:X2}";
        }

        private bool _reticleAttached = false; // kept for compat — unused

        private ReticleOverlayWindow _reticleOverlay;

        private void EnsureReticleOverlay()
        {
            if (_reticleOverlay != null) return;
            _reticleOverlay = new ReticleOverlayWindow();
            _reticleOverlay.Show();
            _reticleOverlay.Visibility = Visibility.Collapsed;
            _reticleOverlay.Closed += (s, e) => _reticleOverlay = null;
        }

        private void ApplyReticle()
        {
            if (_lens == null) return;
            EnsureReticleOverlay();
            _reticleOverlay.ShowCursorDot = _showReticleDot;
            _reticleOverlay.Configure(
                _settings.ReticleEnabled,
                _settings.ReticleStyle,
                _settings.ReticleColor,
                _settings.ReticleSize);
        }

        private void UpdateReticleOverlay()
        {
            ApplyReticle();
        }

        /// <summary>
        /// Called every timer tick. Positions overlay over lens view and redraws.
        /// Uses screen coordinates so the overlay tracks the lens exactly.
        /// </summary>
        private void TickUpdateReticle()
        {
            if (!_settings.ReticleEnabled && !_showReticleDot) return;
            if (_reticleOverlay == null) return;

            var (sx, sy, sw, sh) = _lens.GetViewScreenRect();
            if (sw < 1 || sh < 1) return;

            _reticleOverlay.PositionOver(sx, sy, sw, sh,
                _engine.LastCursorNormX, _engine.LastCursorNormY);
        }

        private void RestoreTrackModeUi()
        {
            // Restore slider first so ApplyTrackMode reads the correct size
            int size = Math.Max(100, Math.Min(600, _settings.LensTrackSize));
            if (TrackSizeSlider != null) TrackSizeSlider.Value = size;
            if (TrackSizeLabel  != null) TrackSizeLabel.Text   = $"{size}px";

            // Suppress the radio-button Changed event so TrackMode_Changed doesn't
            // fire during init (it would call ApplyTrackMode before the lens is ready).
            // We set IsChecked silently then call ApplyTrackMode directly once.
            void SetRadio(System.Windows.Controls.RadioButton btn, bool val)
            {
                if (btn == null) return;
                btn.Checked -= TrackMode_Changed;
                btn.IsChecked = val;
                btn.Checked += TrackMode_Changed;
            }

            bool isCircle = _settings.LensTrackMode == "circle";
            bool isSquare = _settings.LensTrackMode == "square";
            SetRadio(TrackCircle, isCircle);
            SetRadio(TrackSquare, isSquare);
            SetRadio(TrackFixed,  !isCircle && !isSquare);

            // Apply directly — no event chain, no risk of double-apply
            ApplyTrackMode();
        }

        private void RestoreReticleUi()
        {
            if (ReticleCheck != null) ReticleCheck.IsChecked = _settings.ReticleEnabled;
            if (ReticleOptions != null)
                ReticleOptions.Visibility = _settings.ReticleEnabled
                    ? System.Windows.Visibility.Visible
                    : System.Windows.Visibility.Collapsed;

            // Radio buttons
            if (ReticleStyleX      != null) ReticleStyleX.IsChecked      = _settings.ReticleStyle == "X";
            if (ReticleStyleCross  != null) ReticleStyleCross.IsChecked  = _settings.ReticleStyle == "Cross";
            if (ReticleStyleCircle != null) ReticleStyleCircle.IsChecked = _settings.ReticleStyle == "Circle";

            int sz = Math.Max(1, Math.Min(5, _settings.ReticleSize));
            if (ReticleSizeLabel   != null) ReticleSizeLabel.Text        = _reticleSizeNames[sz - 1];
            if (ReticleSizeDownBtn != null) ReticleSizeDownBtn.IsEnabled = sz > 1;
            if (ReticleSizeUpBtn   != null) ReticleSizeUpBtn.IsEnabled   = sz < 5;

            UpdateReticleColorSwatch();
            ApplyReticle();
        }

        private void MirrorCheck_Changed(object sender, System.Windows.RoutedEventArgs e)
        {
            bool on = MirrorCheck?.IsChecked == true;
            ApplyMirrorLens(on);
            _settings.MirrorLens = on;
        }

        private void ApplyMirrorLens(bool on)
        {
            if (on)
            {
                if (_mirrorLens == null)
                {
                    _mirrorLens = new MirrorLensWindow();
                    _mirrorLens.Closed += (s2, e2) =>
                    {
                        _mirrorLens = null;
                        if (MirrorCheck != null) MirrorCheck.IsChecked = false;
                        _settings.MirrorLens = false;
                    };
                    _mirrorLens.Show();
                }
            }
            else
            {
                _mirrorLens?.Close();
                _mirrorLens = null;
            }
            if (MirrorCheck != null) MirrorCheck.IsChecked = on;
        }

        private void PulseBtn_Click(object sender, System.Windows.RoutedEventArgs e)
            => _cursorPulse?.Fire();

        private void RulerColorBtn_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            var dlg = new System.Windows.Forms.ColorDialog
            {
                Color = HexToDrawingColor(_settings.RulerColorHex), FullOpen = true
            };
            if (dlg.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
            var col = dlg.Color;
            _settings.RulerColorHex = $"#{col.R:X2}{col.G:X2}{col.B:X2}";
            var rulerColor = System.Windows.Media.Color.FromArgb(80, col.R, col.G, col.B);
            _ruler?.SetColor(rulerColor);
            UpdateColorSwatch(RulerColorSwatch, rulerColor);
        }

        private void HighlightColorBtn_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            var dlg = new System.Windows.Forms.ColorDialog
            {
                Color = HexToDrawingColor(_settings.CursorColor), FullOpen = true
            };
            if (dlg.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
            var col = dlg.Color;
            _settings.CursorColor = $"#{col.R:X2}{col.G:X2}{col.B:X2}";
            var wpfColor = System.Windows.Media.Color.FromRgb(col.R, col.G, col.B);
            _cursorHL?.SetColor(wpfColor);
            UpdateColorSwatch(HighlightColorSwatch, wpfColor);
        }

        private void PulseColorBtn_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            var dlg = new System.Windows.Forms.ColorDialog
            {
                Color = HexToDrawingColor(_settings.CursorPulseColor), FullOpen = true
            };
            if (dlg.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
            var col = dlg.Color;
            _settings.CursorPulseColor = $"#{col.R:X2}{col.G:X2}{col.B:X2}";
            var wpfColor = System.Windows.Media.Color.FromRgb(col.R, col.G, col.B);
            _cursorPulse?.SetColor(wpfColor);
            UpdateColorSwatch(PulseColorSwatch, wpfColor);
        }

        // ── Zoom lock ─────────────────────────────────────────────────────────
        private void UpdateZoomLockIndicator()
        {
            if (ZoomLockLabel == null) return;
            bool locked = _zoomMin > 1.25 || _zoomMax < 20.0;
            ZoomLockLabel.Text       = locked ? $"{_zoomMin:F1}×–{_zoomMax:F1}×" : "Unlocked";
            if (ZoomLockIcon != null) ZoomLockIcon.Visibility = locked ? Visibility.Visible : Visibility.Collapsed;
            ZoomLockLabel.Foreground = locked
                ? (TryGetThemeBrush("BrushRedText") ?? new SolidColorBrush(System.Windows.Media.Color.FromRgb(243, 139, 168)))
                : (TryGetThemeBrush("BrushHint") ?? (System.Windows.Media.Brush)FindResource("BrushHint"));
        }

        private void ZoomLockMinSlider_Changed(object sender,
            RoutedPropertyChangedEventArgs<double> e)
        {
            if (_engine == null) return;   // firing during XAML init
            _zoomMin = Math.Round(e.NewValue * 4) / 4; // snap to 0.25 grid
            _zoomMin = Math.Max(1.25, Math.Min(_zoomMax - 0.25, _zoomMin));
            if (ZoomLockMinLabel != null) ZoomLockMinLabel.Text = $"{_zoomMin:F2}×";
            _settings.ZoomLockMin = _zoomMin;
            UpdateZoomLockIndicator();
            if (_zoom < _zoomMin) SetZoom(_zoomMin);
        }

        private void ZoomLockMaxSlider_Changed(object sender,
            RoutedPropertyChangedEventArgs<double> e)
        {
            if (_engine == null) return;   // firing during XAML init
            _zoomMax = Math.Round(e.NewValue * 4) / 4;
            _zoomMax = Math.Max(_zoomMin + 0.25, Math.Min(20.0, _zoomMax));
            if (ZoomLockMaxLabel != null) ZoomLockMaxLabel.Text = $"{_zoomMax:F2}×";
            _settings.ZoomLockMax = _zoomMax;
            UpdateZoomLockIndicator();
            if (_zoom > _zoomMax) SetZoom(_zoomMax);
        }

        private void ZoomLockClearBtn_Click(object sender, RoutedEventArgs e)
        {
            _zoomMin = 1.25; _zoomMax = 20.0;
            if (ZoomLockMinSlider != null) ZoomLockMinSlider.Value = 1.25;
            if (ZoomLockMaxSlider != null) ZoomLockMaxSlider.Value = 20.0;
            if (ZoomLockMinLabel  != null) ZoomLockMinLabel.Text  = "1.25×";
            if (ZoomLockMaxLabel  != null) ZoomLockMaxLabel.Text  = "20.0×";
            _settings.ZoomLockMin = _zoomMin;
            _settings.ZoomLockMax = _zoomMax;
            UpdateZoomLockIndicator();
        }

        private void SetHomeZoomBtn_Click(object sender, RoutedEventArgs e)
        {
            _settings.HomeZoom = _zoom;
            ShowTrayBalloon($"Home zoom set: {_zoom:F1}×", ms: 1500);
            if (HomeZoomLabel != null) HomeZoomLabel.Text = $"{_settings.HomeZoom:F1}×";
        }

        // ── Tint intensity ────────────────────────────────────────────────────
        private void TintSlider_Changed(object sender,
            RoutedPropertyChangedEventArgs<double> e)
        {
            if (_engine == null) return;
            _tintIntensity = (float)e.NewValue;
            _settings.TintIntensity = _tintIntensity;
            if (TintLabel != null) TintLabel.Text = $"{(int)(_tintIntensity * 100)}%";
            if (_engine != null && _lens != null &&
                (_colorMode == "warmamber" || _colorMode == "coolblue"))
                _engine.ApplyColorEffect(_lens.MagHwnd,
                    _settings.RenderMode == "smooth", _colorMode, _tintIntensity);
        }

        // ── Snap-to-word ──────────────────────────────────────────────────────
        private void SnapToWordCheck_Changed(object sender, RoutedEventArgs e)
        {
            _snapToWord = SnapToWordCheck?.IsChecked == true;
        }

        // ── Dim when idle ─────────────────────────────────────────────────────
        private void InitDimTimer()
        {
            _dimTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(Math.Max(1, _settings.DimDelaySeconds))
            };
            _dimTimer.Tick += (s, e) =>
            {
                _dimTimer.Stop();
                if (_settings.DimWhenIdle && _active && !_dimmed)
                    DimLens();
            };
        }

        private void DimLens()
        {
            _dimmed = true;
            _preDimOpacity = _lens.Opacity;
            double target  = Math.Max(0.05, Math.Min(0.9, _settings.DimOpacity));
            var anim = new DoubleAnimation(target, TimeSpan.FromMilliseconds(600))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            _lens.BeginAnimation(Window.OpacityProperty, anim);
        }

        private void UndimLens()
        {
            if (!_dimmed) return;
            _dimmed = false;
            var anim = new DoubleAnimation(_preDimOpacity, TimeSpan.FromMilliseconds(200))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            _lens.BeginAnimation(Window.OpacityProperty, anim);
        }

        private static System.Drawing.Color HexToDrawingColor(string hex)
        {
            if (!ColourPicker.TryParseColor(hex, out var c)) return System.Drawing.Color.Orange;
            return System.Drawing.Color.FromArgb(c.A, c.R, c.G, c.B);
        }

        /// <summary>
        /// Updates a colour swatch button. The swatch is now a Button containing
        /// a Rectangle child (WCAG 4.1.2 — gives swatches an accessible role and
        /// keyboard activation). We set the inner Rectangle's Fill.
        /// </summary>
        private static void UpdateColorSwatch(System.Windows.Controls.Button swatch,
            System.Windows.Media.Color c)
        {
            if (swatch == null) return;
            var brush = new System.Windows.Media.SolidColorBrush(c);
            // Find the Rectangle child
            if (swatch.Content is System.Windows.Shapes.Rectangle rect)
                rect.Fill = brush;
            else
                swatch.Background = brush;

            // Update hex label TextBlock sibling — named "{swatchName minus 'Swatch'}Label"
            string labelName = swatch.Name?.Replace("Swatch", "Label");
            if (!string.IsNullOrEmpty(labelName))
            {
                var panel = swatch.Parent as System.Windows.Controls.StackPanel;
                if (panel != null)
                {
                    foreach (var child in panel.Children)
                    {
                        if (child is System.Windows.Controls.TextBlock tb &&
                            tb.Name == labelName)
                        {
                            tb.Text = $"#{c.R:X2}{c.G:X2}{c.B:X2}";
                            break;
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Called by App.xaml.cs on unhandled crash to preserve the user's settings.
        /// Saves only the fields that live in _settings (already kept up-to-date by
        /// real-time handlers) plus the current zoom and lens geometry.
        /// </summary>
        public void TrySaveSettingsOnCrash()
        {
            try
            {
                _settings.Zoom  = _zoom;
                _settings.LensW = (int)(_lens?.Width  ?? _settings.LensW);
                _settings.LensH = (int)(_lens?.Height ?? _settings.LensH);
                _settings.LensX = (int)(_lens?.Left   ?? _settings.LensX);
                _settings.LensY = (int)(_lens?.Top    ?? _settings.LensY);
                _settings.PerMonitorZoomMap = PerMonitorZoomMap.Serialise(_monitorZoomMap);
                _settings.Save();
            }
            catch (System.Exception _ex) { CrashLog.Write("TrySaveSettingsOnCrash", _ex); }
        }

        /// <summary>
        /// Restores system-level state (SPI_SETFONTSMOOTHING) modified by the
        /// magnifier engine. Called from App crash handlers so that an unhandled
        /// exception does not leave font smoothing in a modified state system-wide.
        /// </summary>
        public void TryDisposeEngine()
        {
            try { _engine?.Dispose(); }
            catch { }
        }

        /// <summary>
        /// Hide reticle overlay when this app loses focus — prevents floating topmost
        /// overlay appearing over fullscreen games/video while magnifier is active.
        /// </summary>
        private void Window_Deactivated(object sender, EventArgs e)
        {
            if (_reticleOverlay != null)
                _reticleOverlay.Visibility = Visibility.Collapsed;
        }

        /// <summary>
        /// Restore reticle overlay when app regains focus.
        /// </summary>
        private void Window_Activated(object sender, EventArgs e)
        {
            if (_active && _reticleOverlay != null &&
                (_settings.ReticleEnabled || _showReticleDot))
                _reticleOverlay.Visibility = Visibility.Visible;
        }

        private bool _isReallyClosing = false;

        /// <summary>
        /// Shows a dialog offering Minimize to Tray or Close completely.
        /// Respects "don't ask again" preference stored in CloseToTray setting.
        /// </summary>
        private void ShowCloseOrTrayDialog()
        {
            // Build a simple WPF dialog inline
            var dlg = new Window
            {
                Title                 = "Super Magnifier LV",
                Width                 = 380,
                SizeToContent         = SizeToContent.Height,
                ResizeMode            = ResizeMode.NoResize,
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
                Background            = (System.Windows.Media.Brush)FindResource("BrushBg"),
                ShowInTaskbar         = false,
                Owner                 = this,
            };

            var panel = new System.Windows.Controls.StackPanel { Margin = new Thickness(24, 20, 24, 20) };

            var title = new System.Windows.Controls.TextBlock
            {
                Text       = "Close Super Magnifier LV?",
                FontSize   = 14, FontWeight = FontWeights.SemiBold,
                Foreground = (System.Windows.Media.Brush)FindResource("BrushText"),
                Margin     = new Thickness(0, 0, 0, 8),
            };
            panel.Children.Add(title);

            var subtitle = new System.Windows.Controls.TextBlock
            {
                Text         = "Choose what happens when you click the X button:",
                FontSize     = 12,
                Foreground   = (System.Windows.Media.Brush)FindResource("BrushMuted"),
                TextWrapping = TextWrapping.Wrap,
                Margin       = new Thickness(0, 0, 0, 18),
            };
            panel.Children.Add(subtitle);

            // Button row
            var btnRow = new System.Windows.Controls.StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, HorizontalAlignment = System.Windows.HorizontalAlignment.Center };

            var minimizeBtn = new System.Windows.Controls.Button
            {
                MinWidth  = 140, MinHeight = 44, Margin = new Thickness(0, 0, 12, 0),
                FontSize  = 12, Padding = new Thickness(14, 8, 14, 8),
                Style     = (Style)FindResource("PrimaryButton"),
            };
            var closeBtn = new System.Windows.Controls.Button
            {
                MinWidth  = 140, MinHeight = 44,
                FontSize  = 12, Padding = new Thickness(14, 8, 14, 8),
                Style     = (Style)FindResource("FlatButton"),
            };
            minimizeBtn.Content = JtIcons.Label(minimizeBtn, "Minimize to Tray", "import");
            closeBtn.Content    = JtIcons.Label(closeBtn, "Close Completely", "close");
            btnRow.Children.Add(minimizeBtn);
            btnRow.Children.Add(closeBtn);
            panel.Children.Add(btnRow);

            // "Remember my choice" checkbox
            var rememberCheck = new System.Windows.Controls.CheckBox
            {
                Content   = "Remember this choice (change in Settings)",
                FontSize  = 12,
                Foreground = (System.Windows.Media.Brush)FindResource("BrushMuted"),
                Margin    = new Thickness(0, 14, 0, 0),
                IsChecked = false,
            };
            panel.Children.Add(rememberCheck);

            dlg.Content = panel;

            bool minimized = false;

            minimizeBtn.Click += (s, e2) => { minimized = true;  dlg.Close(); };
            closeBtn.Click    += (s, e2) => { minimized = false; dlg.Close(); };

            dlg.ShowDialog();

            if (rememberCheck.IsChecked == true)
            {
                // Save preference: CloseToTray=true means minimize, false means close
                _settings.CloseToTray = minimized;
                _settings.Save();
            }

            if (minimized)
            {
                Hide(); // minimize to tray — window hidden, tray icon remains
            }
            else
            {
                _isReallyClosing = true;
                Close();
            }
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            // If CloseToTray is on and this isn't an explicit "Exit" action, show the dialog
            if (!_isReallyClosing && _settings.CloseToTray)
            {
                e.Cancel = true;
                ShowCloseOrTrayDialog();
                return;
            }

            // ── Real close path ──────────────────────────────────────────────
            _settings.Zoom            = _zoom;
            _settings.Step            = _zoomStep;
            // Save main control panel screen position
            _settings.MainWindowX     = Left;
            _settings.MainWindowY     = Top;
            _settings.LensW           = (int)_lens.Width;
            _settings.LensH           = (int)_lens.Height;
            _settings.DefaultLensW    = (int)_lens.Width;   // keep in sync
            _settings.DefaultLensH    = (int)_lens.Height;
            _settings.DefaultZoom     = _zoom;
            _settings.LensX           = (int)_lens.Left;
            _settings.LensY           = (int)_lens.Top;
            _settings.RenderMode      = _settings.RenderMode == "smooth" ? "smooth" : "crisp";
            // AlwaysOnTop owned by SettingsDialog
            _settings.FollowMode      = LensFollowCaret?.IsChecked == true ? "caret" : "mouse";
            _settings.SnapToWord      = _snapToWord;
            _settings.LensTrackMode   = _lens.CurrentTrackMode == LensWindow.TrackMode.Circle ? "circle"
                                      : _lens.CurrentTrackMode == LensWindow.TrackMode.Square  ? "square"
                                      : "fixed";
            _settings.LensTrackSize   = (int)(TrackSizeSlider?.Value ?? 300);
            _settings.UiSizeIndex     = _uiSizeIndex;
            _settings.ColorMode       = _colorMode;
            _settings.CursorHighlight = HighlightCheck.IsChecked == true;
            _settings.ReadingRuler    = RulerCheck.IsChecked          == true;
            _settings.LensOpacity     = LensOpacitySlider?.Value ?? 1.0;
            // HotkeyToggle owned by SettingsDialog
            _settings.DarkMode            = _isDark;
            _settings.ThemeName           = _themeName;
            _settings.TitleBarSizeIndex   = _lens.TitleBarSizeIndex;
            _settings.SessionActive       = _active;
            _settings.ClickThrough        = _lens.IsClickThrough;

            // Save per-monitor relative position
            try
            {
                var (monName, relX, relY) = MonitorHelper.GetRelativePosition(
                    _lens.Left, _lens.Top, _lens.DpiScaleX);
                _settings.LensMonitor = monName;
                _settings.LensRelX    = relX;
                _settings.LensRelY    = relY;
            }
            catch (System.Exception _ex) { System.Diagnostics.Debug.WriteLine($"[SuperMagnifier] {_ex.Message}"); }

            _settings.AppPresetMap  = _appPresetMap?.Serialise() ?? "";
            _settings.AppPositionMap= _appPositionMap?.Serialise() ?? "";
            _settings.ZoomLockMin   = _zoomMin;
            _settings.ZoomLockMax   = _zoomMax;
            _settings.TintIntensity = _tintIntensity;
            // Save per-monitor zoom map before Save()
            _settings.PerMonitorZoomMap = PerMonitorZoomMap.Serialise(_monitorZoomMap);
            // DimWhenIdle/DimDelay/DimOpacity saved in real-time via slider handlers
            _settings.Save();

            DisposeHotkeys();
            _toastTimer?.Stop();    _toastTimer    = null;
            _dimTimer?.Stop();      _dimTimer      = null;
            _zoomAnimTimer?.Stop(); _zoomAnimTimer = null;
            _appPresetMap?.Dispose();
            _appPositionMap?.Dispose();
            _mirrorLens?.Close();
            _reticleOverlay?.Close();
            _cursorPulse?.Dispose();
            _keyboardMover?.Dispose();
            _caretTracker?.Dispose();
            _ruler?.Dispose();
            _tray.Visible = false;
            _tray.Dispose();
            _cursorHL?.Dispose();
            _timer.Stop();
            _engine.Dispose();
            _lens.Close();
            Microsoft.Win32.SystemEvents.UserPreferenceChanged  -= OnUserPreferenceChanged;
            Microsoft.Win32.SystemEvents.PowerModeChanged       -= OnPowerModeChanged;
            Microsoft.Win32.SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
        }

    // ── Sleep/hibernate resume recovery ─────────────────────────────────────
    // The Magnification API can lose its internal state after the system wakes from
    // sleep or hibernation, causing the lens to show a black/frozen view.
    // Toggling off and on reinitialises the engine cleanly.
    private void OnPowerModeChanged(object sender, Microsoft.Win32.PowerModeChangedEventArgs e)
    {
        if (e.Mode != Microsoft.Win32.PowerModes.Resume) return;
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background,
            new Action(() =>
            {
                try
                {
                    if (!_active) return;
                    Toggle();  // off
                    Toggle();  // on — reinitialises engine
                    System.Diagnostics.Debug.WriteLine("[SuperMagnifier] Power resume: engine reinitialised.");
                }
                catch (System.Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[SuperMagnifier] Power resume: {ex.Message}");
                }
            }));
    }

    // ── Display / DPI topology changes ─────────────────────────────────────
    // Fired by the OS on monitor connect/disconnect, resolution change, or DPI
    // scaling change. Invalidates the engine's cached virtual-screen bounds and
    // resizes/clears the cursor-highlighter overlay so both recover immediately
    // instead of waiting on the once-a-second monitor-name poll (which only
    // triggers if the lens itself moves to a differently-named monitor).
    private void OnDisplaySettingsChanged(object sender, EventArgs e)
    {
        Dispatcher.Invoke(() =>
        {
            try
            {
                _engine?.InvalidateVirtualScreenCache();
                _cursorHL?.RefreshBounds();
            }
            catch (System.Exception _ex)
            {
                System.Diagnostics.Debug.WriteLine($"[SuperMagnifier] DisplaySettingsChanged: {_ex.Message}");
            }
        });
    }

    // ── Accessibility: Windows system preference changes ──────────────────────
    // Handles High Contrast mode toggle and system font size changes (items 4 & 5).
    // Called on a background thread — all UI work must be Dispatcher.Invoke'd.
    private void OnUserPreferenceChanged(object sender,
        Microsoft.Win32.UserPreferenceChangedEventArgs e)
    {
        if (e.Category == Microsoft.Win32.UserPreferenceCategory.Accessibility ||
            e.Category == Microsoft.Win32.UserPreferenceCategory.Window)
        {
            Dispatcher.Invoke(() =>
            {
                _reduceMotion = !SystemParameters.ClientAreaAnimation;
                ApplyHighContrastIfNeeded();
                ApplySystemFontScale();
            });
        }
    }

    /// <summary>
    /// If Windows High Contrast mode is active, switch to a high-contrast colour
    /// scheme regardless of the user's dark/light preference. When HC is turned off,
    /// restore the saved preference.
    /// </summary>
    private void ApplyHighContrastIfNeeded()
    {
        if (SystemParameters.HighContrast)
        {
            // Override theme colours with pure system HC colours
            var res = System.Windows.Application.Current.Resources;
            res["BrushBg"]    = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(0, 0, 0));
            res["BrushSurf"]  = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(0, 0, 0));
            res["BrushSurf2"] = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(128, 128, 128));
            res["BrushText"]  = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(255, 255, 255));
            res["BrushMuted"] = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(255, 255, 255));
            res["BrushHint"]  = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(255, 255, 0));   // yellow for section labels
            res["BrushBlue"]  = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(0, 255, 255));   // cyan accents
            // AAA text tokens (2026-10-13) — accent-as-text now uses these
            res["BrushBlueText"]  = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0, 255, 255));
            res["BrushRedText"]   = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(255, 128, 128));
            res["BrushGreenText"] = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(63, 255, 63));
            res["BrushAmberText"] = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(255, 255, 0));
            res["BrushOnAccent"]  = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0, 0, 0));
            res["BrushDivider"]   = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(255, 255, 255));
            res["BrushBar"]   = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(0, 0, 0));
            res["FocusRingBrush"] = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(255, 255, 0));   // yellow focus ring
            ShowToast("High Contrast mode active");
        }
        else
        {
            // Restore normal theme
            App.SetTheme(_themeName);
        }
    }

    /// <summary>
    /// Read the Windows system menu/caption font size and nudge the base UI font
    /// size proportionally. This responds to the Windows "Make text bigger" slider.
    /// </summary>
    private void ApplySystemFontScale()
    {
        try
        {
            // SystemFonts.MenuFontSize reflects the user's text size preference.
            // Default is 12px; each accessibility step raises it.
            double systemPt = System.Windows.SystemFonts.MenuFontSize;
            double basePt   = 12.0;
            double ratio    = Math.Max(1.0, Math.Min(2.5, systemPt / basePt));

            // Only apply if it's meaningfully different from 1 (i.e. user changed it)
            if (ratio > 1.05 && UiScale != null)
            {
                // Blend with the current manual scale — take the larger of the two
                double current = UiScales[_uiSizeIndex];
                double effective = Math.Max(current, ratio);
                UiScale.ScaleX = effective;
                UiScale.ScaleY = effective;
                if (MainScrollViewer != null)
                {
                    if (_uiSizeIndex >= 3)
                    {
                        var screen = SystemParameters.WorkArea;
                        MainScrollViewer.MaxHeight = (screen.Height - 32) / effective;
                    }
                    else
                    {
                        MainScrollViewer.MaxHeight = double.PositiveInfinity;
                    }
                }
            }
        }
        catch (System.Exception _ex) { System.Diagnostics.Debug.WriteLine($"[SuperMagnifier] {_ex.Message}"); }
    }

    } // end MainWindow

    internal sealed class RelayCommand : ICommand
    {
        private readonly Action<object>    _execute;
        private readonly Func<object,bool> _canExecute;
        public RelayCommand(Action<object> execute, Func<object,bool> canExecute = null)
        {
            _execute    = execute;
            _canExecute = canExecute;
        }
        public bool CanExecute(object parameter) => _canExecute?.Invoke(parameter) ?? true;
        public void Execute(object parameter)    => _execute(parameter);
        public event EventHandler CanExecuteChanged;
        public void RaiseCanExecuteChanged() =>
            CanExecuteChanged?.Invoke(this, EventArgs.Empty);
    }
}
