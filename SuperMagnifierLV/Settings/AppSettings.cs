using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace SuperMagnifier
{
    /// <summary>
    /// Persisted settings. Saved as magnifier_settings.json in %AppData%\SuperMagnifierLV\.
    /// Serialization uses .NET 10 JSON Source Generators (AppSettingsContext) for
    /// high-performance, reflection-free load/save per project specification.
    /// </summary>
    public class AppSettings
    {
        public double Zoom         { get; set; } = 2.0;
        public double Step         { get; set; } = 0.25;
        public int    LensW        { get; set; } = 800;
        public int    LensH        { get; set; } = 600;
        public int    LensX        { get; set; } = 100;
        public int    LensY        { get; set; } = 100;
        public string RenderMode   { get; set; } = "smooth";
        public bool   AlwaysOnTop  { get; set; } = true;
        public int    DefaultLensW { get; set; } = 800;
        public int    DefaultLensH { get; set; } = 600;
        public double DefaultZoom  { get; set; } = 2.0;
        public int    UiSizeIndex  { get; set; } = 1;

        // ── Accessibility settings ────────────────────────────────────────────
        public string ColorMode         { get; set; } = "normal";
        public bool   CursorHighlight   { get; set; } = false;
        public string CursorColor       { get; set; } = "#FF5000";
        public double LensOpacity       { get; set; } = 1.0;
        public string LensBorderColor   { get; set; } = "#89B4FA";
        public string LensGripColor     { get; set; } = "#89B4FA";
        public double LensGripSize      { get; set; } = 44;
        public bool   HotkeyToggle      { get; set; } = true;
        public string FollowMode        { get; set; } = "mouse";
        public bool   ReadingRuler      { get; set; } = false;
        public bool   DarkMode          { get; set; } = true;
        public string ThemeName         { get; set; } = "Dark";
        public string LensMonitor       { get; set; } = "";
        public double LensRelX          { get; set; } = 0.05;
        public double LensRelY          { get; set; } = 0.05;
        public bool   AutoStart         { get; set; } = false;
        public int    TitleBarSizeIndex { get; set; } = 4;
        public bool   SessionActive     { get; set; } = false;

        // ── Lens bar text visibility ──────────────────────────────────────────
        // Master switch overrides all individual labels below when true.
        public bool   BarHideAllText        { get; set; } = false;
        public bool   BarShowSizeLabel      { get; set; } = true;  // "Aa" on title-size button
        public bool   BarShowColourLabel    { get; set; } = true;  // "Clr" on colour-picker button
        public bool   BarShowSnapLabel      { get; set; } = true;  // "Snap" on snapshot button
        public bool   BarShowClickThruLabel { get; set; } = true;  // "Thru" on click-through button
        public bool   BarShowFreezeLabel    { get; set; } = true;  // "Freeze"/"Resume" word (icon stays)
        public bool   BarShowZoomValue      { get; set; } = true;  // "2.00x" zoom readout
        public bool   BarShowHoverHint      { get; set; } = true;  // inline hover-hint label
        public bool   ClickThrough      { get; set; } = false;
        public bool   SessionRestore    { get; set; } = true;

        // ── Feature settings ──────────────────────────────────────────────────
        public bool   SmoothPanning     { get; set; } = false;
        public double PanLag            { get; set; } = 0.15;
        public int    LensBorderThick   { get; set; } = 2;
        public bool   EdgeDeadZone      { get; set; } = true;
        public double RulerHeight       { get; set; } = 28.0;
        public string RulerColorHex     { get; set; } = "#FFDC00";
        public int    UpdateRateFps     { get; set; } = 30;   // 30 fps default; 60 fps available in Settings
        public string AppPresetMap      { get; set; } = "";
        public string AppPositionMap    { get; set; } = "";
        public bool   MirrorLens        { get; set; } = false;
        public string CursorPulseColor  { get; set; } = "#64B4FF";

        // ── Reticle overlay ───────────────────────────────────────────────────
        public bool   ReticleEnabled    { get; set; } = false;
        public string ReticleStyle      { get; set; } = "Cross";
        public string ReticleColor      { get; set; } = "#FF4444";
        public int    ReticleSize       { get; set; } = 3;

        // ── Startup defaults ──────────────────────────────────────────────────
        public string DefaultFollowMode    { get; set; } = "mouse";
        public bool   DefaultSnapToWord    { get; set; } = false;
        public bool   DefaultReticle       { get; set; } = false;
        public int    DefaultUiSizeIndex       { get; set; } = -1;
        // Always applied at startup (no "remember last" sentinel) — literal default height.
        public int    DefaultTitleBarSizeIndex { get; set; } = 4;

        // ── Lens track mode ───────────────────────────────────────────────────
        public string LensTrackMode  { get; set; } = "fixed";
        public int    LensTrackSize  { get; set; } = 300;

        // ── Snap-to-word ──────────────────────────────────────────────────────
        public bool   SnapToWord           { get; set; } = false;
        public double ZoomLockMin          { get; set; } = 1.25;
        public double ZoomLockMax          { get; set; } = 20.0;

        // ── Colour tinting ────────────────────────────────────────────────────
        public float  TintIntensity     { get; set; } = 0.5f;

        // ── Dim when idle ─────────────────────────────────────────────────────
        public bool   DimWhenIdle       { get; set; } = false;
        public int    DimDelaySeconds   { get; set; } = 5;
        public double DimOpacity        { get; set; } = 0.25;

        // ── Home zoom ─────────────────────────────────────────────────────────
        public double HomeZoom          { get; set; } = 2.0;

        // ── Remappable hotkeys ────────────────────────────────────────────────
        public Dictionary<string, HotkeyBinding> HotkeyBindings { get; set; } = HotkeyBinding.Defaults();
        /// <summary>Default-hotkey scheme the bindings were saved under (2 = Ctrl+Alt). Fresh installs start at current.</summary>
        public int HotkeySchemeVersion { get; set; } = HotkeyBinding.SchemeVersion;
        /// <summary>Status-message duration: 5 = at least 5 s (+60 ms/char), 10 = at least 10 s, 0 = until dismissed (WCAG 2.2.3).</summary>
        public int ToastSeconds { get; set; } = 5;
        /// <summary>Runtime only: Load() moved untouched scheme-1 defaults to scheme 2 → show one-time notice.</summary>
        public bool HotkeysMigrated { get; set; }

        /// <summary>SM-12: builds bindings from saved maps; a binding still equal to the old
        /// Ctrl-only default moves to the new Ctrl+Alt default, customised/unbound ones are kept.</summary>
        internal static Dictionary<string, HotkeyBinding> MigrateHotkeys(
            IReadOnlyDictionary<string, int> mods, IReadOnlyDictionary<string, int> vks, int schemeVersion, out bool migrated)
        {
            migrated = false;
            var defaults   = HotkeyBinding.Defaults();
            var defaultsV1 = HotkeyBinding.DefaultsV1();
            bool migrate   = schemeVersion < HotkeyBinding.SchemeVersion;
            var result     = new Dictionary<string, HotkeyBinding>();
            foreach (var key in defaults.Keys)
            {
                int m = 0, v = 0;
                bool saved = mods != null && vks != null && mods.TryGetValue(key, out m) & vks.TryGetValue(key, out v);
                var binding = saved ? new HotkeyBinding((uint)m, (uint)v) : defaults[key];
                if (migrate && saved && defaultsV1.TryGetValue(key, out var old) && binding.SameAs(old))
                {
                    binding  = defaults[key];
                    migrated = true;
                }
                result[key] = binding;
            }
            return result;
        }

        // ── Inverted scroll wheel zoom ────────────────────────────────────────
        public bool   InvertScrollZoom  { get; set; } = false;

        // ── Opacity follow zoom ───────────────────────────────────────────────
        public bool   OpacityFollowZoom { get; set; } = false;

        // ── Per-monitor zoom memory ───────────────────────────────────────────
        public string PerMonitorZoomMap { get; set; } = "";

        // ── Zoom transition animation ─────────────────────────────────────────
        public bool   ZoomAnimate       { get; set; } = true;

        // ── Focus ring colour ─────────────────────────────────────────────────
        public string FocusRingColor    { get; set; } = "#89B4FA";

        // ── App-wide font family + explicit HUD font size (Low-Vision) ────────
        public string AppFontFamily     { get; set; } = "Segoe UI";
        // Clamped 8..72 by setter logic in callers; raw field accepts 12 default.
        public double HudFontSize       { get; set; } = 12;

        // ── First-run wizard ──────────────────────────────────────────────────
        public bool   WizardShown       { get; set; } = false;
        public bool   CloseToTray       { get; set; } = true;  // X button minimizes to tray by default

        // ── What's New ────────────────────────────────────────────────────────
        public string LastSeenVersion   { get; set; } = "";

        // ── Settings schema version ───────────────────────────────────────────
        // History: 0=initial, 1=accessibility, 2=startup defaults, 3=track mode, 4=named themes,
        //          5=title bar startup size always-on (no remember-last), lens bar text toggles,
        //          6=resize grip colour/size
        public const int CurrentSettingsVersion = 6;
        public int    SettingsVersion   { get; set; } = 0;

        // ── Main window position ──────────────────────────────────────────────
        // Stored as nullable double — null means "not set", omitted from JSON.
        public double? MainWindowX { get; set; } = null;
        public double? MainWindowY { get; set; } = null;

        // ── Colour picker history ─────────────────────────────────────────────
        // Stored as a JSON array of strings.
        public List<string> ColourHistory { get; set; } = new List<string>();
        // ── File path ─────────────────────────────────────────────────────────────
        // Store in %AppData%\SuperMagnifierLV\ — always writable even under Program Files.
        // Falls back to the exe directory for portable/dev builds.
        private static readonly string SettingsPath = ResolveSettingsPath();

        private static string ResolveSettingsPath()
        {
            try
            {
                string appData = Environment.GetFolderPath(
                    Environment.SpecialFolder.ApplicationData);
                if (!string.IsNullOrEmpty(appData))
                {
                    string folder = Path.Combine(appData, "SuperMagnifierLV");
                    Directory.CreateDirectory(folder);
                    return Path.Combine(folder, "magnifier_settings.json");
                }
            }
            catch { }
            return Path.Combine(AppDomain.CurrentDomain.BaseDirectory,
                                "magnifier_settings.json");
        }

        public static string FilePath => SettingsPath;

        // ── Diagnostic log rotation ────────────────────────────────────────
        // magnifier_error.log was appended to on every Load()/Save() call with no
        // size cap, growing unbounded over months of daily use. Cap it at 1 MB and
        // truncate (drop) rather than grow past that on each append.
        private const long MaxDiagnosticLogBytes = 1 * 1024 * 1024;

        private static void AppendDiagnosticLog(string path, string text)
        {
            try
            {
                var fi = new FileInfo(path);
                if (fi.Exists && fi.Length > MaxDiagnosticLogBytes)
                    File.Delete(path);
            }
            catch { /* best-effort rotation — fall through to append */ }
            File.AppendAllText(path, text);
        }

        /// <summary>Returns a deep clone of this settings object.</summary>
        public AppSettings Clone()
        {
            var copy = (AppSettings)MemberwiseClone();
            if (HotkeyBindings != null)
                copy.HotkeyBindings = new Dictionary<string, HotkeyBinding>(HotkeyBindings);
            copy.ColourHistory = new List<string>(ColourHistory ?? new List<string>());
            return copy;
        }


        // ── Load — .NET 10 JSON Source Generation ────────────────────────────
        public static AppSettings Load()
        {
            var s = new AppSettings();
            try
            {
                // Diagnostic log
                try
                {
                    string logDir  = Path.GetDirectoryName(SettingsPath) ?? "";
                    if (!string.IsNullOrEmpty(logDir)) Directory.CreateDirectory(logDir);
                    string logPath = Path.Combine(logDir, "magnifier_error.log");
                    AppendDiagnosticLog(logPath,
                        $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Load\n"
                        + $"  Path: {SettingsPath}\n"
                        + $"  Exists: {File.Exists(SettingsPath)}\n\n");
                }
                catch { }

                if (!File.Exists(SettingsPath)) return s;

                string json = File.ReadAllText(SettingsPath, Encoding.UTF8);
                var dto = JsonSerializer.Deserialize(json, AppSettingsContext.Default.AppSettingsJson);
                if (dto == null) return s;

                s.Zoom                    = dto.Zoom;
                s.Step                    = dto.Step;
                s.LensW                   = dto.LensW;
                s.LensH                   = dto.LensH;
                s.LensX                   = dto.LensX;
                s.LensY                   = dto.LensY;
                s.RenderMode              = dto.RenderMode;
                s.AlwaysOnTop             = dto.AlwaysOnTop;
                s.DefaultLensW            = dto.DefaultLensW;
                s.DefaultLensH            = dto.DefaultLensH;
                s.DefaultZoom             = dto.DefaultZoom;
                s.UiSizeIndex             = dto.UiSizeIndex;
                s.ColorMode               = dto.ColorMode;
                s.CursorHighlight         = dto.CursorHighlight;
                s.CursorColor             = dto.CursorColor;
                s.LensOpacity             = dto.LensOpacity;
                s.LensBorderColor         = dto.LensBorderColor;
                s.LensGripColor           = dto.LensGripColor ?? "#89B4FA";
                s.LensGripSize            = Math.Clamp(dto.LensGripSize > 0 ? dto.LensGripSize : 44, 44, 64);
                s.HotkeyToggle            = dto.HotkeyToggle;
                s.ToastSeconds            = dto.ToastSeconds is 0 or 5 or 10 ? dto.ToastSeconds : 5;
                s.FollowMode              = dto.FollowMode;
                s.ReadingRuler            = dto.ReadingRuler;
                s.DarkMode                = dto.DarkMode;
                // S-1 (SuperMagnifierLV_CodeSecA11y_Review_2026-09-14): validate against the
                // actual set of embedded theme resources, not just non-empty. A truncated/typo'd
                // value used to sail through here unchanged and every downstream lookup had to
                // guard against it individually — normalize once, at the trust boundary, instead.
                string themeCandidate      = !string.IsNullOrEmpty(dto.ThemeName)
                                            ? dto.ThemeName
                                            : (dto.DarkMode ? "Dark" : "Light");
                s.ThemeName               = App.IsKnownThemeKey(themeCandidate)
                                            ? themeCandidate
                                            : (dto.DarkMode ? "Dark" : "Light");
                s.LensMonitor             = dto.LensMonitor;
                s.LensRelX                = dto.LensRelX;
                s.LensRelY                = dto.LensRelY;
                s.AutoStart               = dto.AutoStart;
                s.TitleBarSizeIndex       = dto.TitleBarSizeIndex;
                s.SessionActive           = dto.SessionActive;
                s.ClickThrough            = dto.ClickThrough;
                s.SessionRestore          = dto.SessionRestore;
                s.SmoothPanning           = dto.SmoothPanning;
                s.PanLag                  = dto.PanLag;
                s.LensBorderThick         = dto.LensBorderThick;
                s.EdgeDeadZone            = dto.EdgeDeadZone;
                s.RulerHeight             = dto.RulerHeight;
                s.RulerColorHex           = dto.RulerColorHex;
                s.UpdateRateFps           = dto.UpdateRateFps;
                s.AppPresetMap            = dto.AppPresetMap;
                s.AppPositionMap          = dto.AppPositionMap;
                s.MirrorLens              = dto.MirrorLens;
                s.CursorPulseColor        = dto.CursorPulseColor;
                s.ReticleEnabled          = dto.ReticleEnabled;
                s.ReticleStyle            = dto.ReticleStyle;
                s.ReticleColor            = dto.ReticleColor;
                s.ReticleSize             = dto.ReticleSize;
                s.LensTrackMode           = dto.LensTrackMode;
                s.LensTrackSize           = dto.LensTrackSize;
                s.DefaultFollowMode       = dto.DefaultFollowMode;
                s.DefaultSnapToWord       = dto.DefaultSnapToWord;
                s.SnapToWord              = dto.SnapToWord;
                s.DefaultReticle          = dto.DefaultReticle;
                s.DefaultUiSizeIndex      = dto.DefaultUiSizeIndex;
                s.DefaultTitleBarSizeIndex = dto.DefaultTitleBarSizeIndex;
                s.BarHideAllText          = dto.BarHideAllText;
                s.BarShowSizeLabel        = dto.BarShowSizeLabel;
                s.BarShowColourLabel      = dto.BarShowColourLabel;
                s.BarShowSnapLabel        = dto.BarShowSnapLabel;
                s.BarShowClickThruLabel   = dto.BarShowClickThruLabel;
                s.BarShowFreezeLabel      = dto.BarShowFreezeLabel;
                s.BarShowZoomValue        = dto.BarShowZoomValue;
                s.BarShowHoverHint        = dto.BarShowHoverHint;
                s.ZoomLockMin             = dto.ZoomLockMin;
                s.ZoomLockMax             = dto.ZoomLockMax;
                s.TintIntensity           = dto.TintIntensity;
                s.DimWhenIdle             = dto.DimWhenIdle;
                s.DimDelaySeconds         = dto.DimDelaySeconds;
                s.DimOpacity              = dto.DimOpacity;
                s.HomeZoom                = dto.HomeZoom;
                s.InvertScrollZoom        = dto.InvertScrollZoom;
                s.OpacityFollowZoom       = dto.OpacityFollowZoom;
                s.PerMonitorZoomMap       = dto.PerMonitorZoomMap;
                s.ZoomAnimate             = dto.ZoomAnimate;
                s.FocusRingColor          = dto.FocusRingColor;
                s.AppFontFamily           = string.IsNullOrWhiteSpace(dto.AppFontFamily) ? "Segoe UI" : dto.AppFontFamily;
                s.HudFontSize             = Math.Clamp(dto.HudFontSize <= 0 ? 12 : dto.HudFontSize, 8, 72);
                s.WizardShown             = dto.WizardShown;
                s.CloseToTray             = dto.CloseToTray;
                s.LastSeenVersion         = dto.LastSeenVersion;
                s.SettingsVersion         = dto.SettingsVersion;
                s.MainWindowX             = dto.MainWindowX;
                s.MainWindowY             = dto.MainWindowY;
                s.ColourHistory           = dto.ColourHistory ?? new List<string>();

                // S-2 (SuperMagnifierLV_CodeSecA11y_Review_2026-09-14): clamp settings with a
                // known valid range (ranges taken from each field's own slider/control Min/Max
                // in the UI) at the trust boundary, same pattern already used above for
                // HudFontSize/LensGripSize. A hand-edited or corrupted settings file won't crash
                // Load() (the whole method is try/caught) but could otherwise produce a broken
                // or unusable lens on next launch with no obvious recovery for a low-vision user.
                s.Zoom            = Math.Clamp(s.Zoom            <= 0 ? 2.0 : s.Zoom,        1.25, 20.0);
                s.DefaultZoom     = Math.Clamp(s.DefaultZoom     <= 0 ? 2.0 : s.DefaultZoom,  1.25, 20.0);
                s.HomeZoom        = Math.Clamp(s.HomeZoom        <= 0 ? 2.0 : s.HomeZoom,     1.25, 20.0);
                s.ZoomLockMin     = Math.Clamp(s.ZoomLockMin, 1.25, 19.75);
                s.ZoomLockMax     = Math.Clamp(s.ZoomLockMax, 1.5,  20.0);
                if (s.ZoomLockMin > s.ZoomLockMax)
                    (s.ZoomLockMin, s.ZoomLockMax) = (s.ZoomLockMax, s.ZoomLockMin);
                s.LensOpacity     = Math.Clamp(s.LensOpacity,     0.2,  1.0);
                s.PanLag          = Math.Clamp(s.PanLag,          0.03, 0.45);
                s.DimDelaySeconds = Math.Clamp(s.DimDelaySeconds, 2,    30);
                s.DimOpacity      = Math.Clamp(s.DimOpacity,      0.05, 0.9);
                s.TintIntensity   = Math.Clamp(s.TintIntensity,   0.1f, 1.0f);
                s.RulerHeight     = Math.Clamp(s.RulerHeight,     8.0,  120.0);
                s.LensBorderThick = Math.Clamp(s.LensBorderThick, 1,    10);
                s.ReticleSize     = Math.Clamp(s.ReticleSize,     1,    5);
                s.LensTrackSize   = Math.Clamp(s.LensTrackSize,   100,  600);
                // LensWindow's own MinWidth/MinHeight are 200/140; upper bound is generous
                // rather than screen-derived (virtual screen bounds aren't known this early).
                s.LensW           = Math.Clamp(s.LensW,        200, 4000);
                s.LensH           = Math.Clamp(s.LensH,        140, 4000);
                s.DefaultLensW    = Math.Clamp(s.DefaultLensW, 200, 4000);
                s.DefaultLensH    = Math.Clamp(s.DefaultLensH, 140, 4000);
                if (s.UpdateRateFps is not (10 or 15 or 30 or 60))
                    s.UpdateRateFps = 30;

                // Hotkey bindings (presets + colour cycle remappable since scheme 2)
                s.HotkeyBindings = MigrateHotkeys(dto.HotkeyMods, dto.HotkeyVKs, dto.HotkeySchemeVersion, out bool migrated);
                s.HotkeysMigrated = migrated;
                s.HotkeySchemeVersion = HotkeyBinding.SchemeVersion;

                // Settings migration
                if (s.SettingsVersion < 1)
                    if (s.TitleBarSizeIndex == 1) s.TitleBarSizeIndex = 4;
                if (s.SettingsVersion < 4 && string.IsNullOrEmpty(s.ThemeName))
                    s.ThemeName = s.DarkMode ? "Dark" : "Light";
                // v5: DefaultTitleBarSizeIndex dropped its -1 "remember last" sentinel —
                // it is now always applied at startup. Fall back to whatever size was
                // last active so existing users see no surprise jump.
                if (s.SettingsVersion < 5 && s.DefaultTitleBarSizeIndex < 0)
                    s.DefaultTitleBarSizeIndex = s.TitleBarSizeIndex >= 0 ? s.TitleBarSizeIndex : 4;
                s.SettingsVersion = CurrentSettingsVersion;
            }
            catch (Exception ex)
            {
                try
                {
                    string lp = Path.Combine(Path.GetDirectoryName(SettingsPath) ?? "", "magnifier_error.log");
                    AppendDiagnosticLog(lp, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] LOAD EXCEPTION: {ex.GetType().Name}: {ex.Message}\n  {ex.StackTrace?.Split('\n')[0]}\n\n");
                }
                catch { }
            }
            return s;
        }

        // ── Save — .NET 10 JSON Source Generation ────────────────────────────
        public void Save()
        {
            try
            {
                SettingsVersion = CurrentSettingsVersion;

                var dto = new AppSettingsJson
                {
                    Zoom                    = Zoom,
                    Step                    = Step,
                    LensW                   = LensW,
                    LensH                   = LensH,
                    LensX                   = LensX,
                    LensY                   = LensY,
                    RenderMode              = RenderMode,
                    AlwaysOnTop             = AlwaysOnTop,
                    DefaultLensW            = DefaultLensW,
                    DefaultLensH            = DefaultLensH,
                    DefaultZoom             = DefaultZoom,
                    UiSizeIndex             = UiSizeIndex,
                    ColorMode               = ColorMode,
                    CursorHighlight         = CursorHighlight,
                    CursorColor             = CursorColor,
                    LensOpacity             = LensOpacity,
                    LensBorderColor         = LensBorderColor,
                    LensGripColor           = LensGripColor,
                    LensGripSize            = LensGripSize,
                    HotkeyToggle            = HotkeyToggle,
                    ToastSeconds            = ToastSeconds,
                    FollowMode              = FollowMode,
                    ReadingRuler            = ReadingRuler,
                    DarkMode                = DarkMode,
                    ThemeName               = ThemeName,
                    LensMonitor             = LensMonitor,
                    LensRelX                = LensRelX,
                    LensRelY                = LensRelY,
                    AutoStart               = AutoStart,
                    TitleBarSizeIndex       = TitleBarSizeIndex,
                    SessionActive           = SessionActive,
                    ClickThrough            = ClickThrough,
                    SessionRestore          = SessionRestore,
                    SmoothPanning           = SmoothPanning,
                    PanLag                  = PanLag,
                    LensBorderThick         = LensBorderThick,
                    EdgeDeadZone            = EdgeDeadZone,
                    RulerHeight             = RulerHeight,
                    RulerColorHex           = RulerColorHex,
                    UpdateRateFps           = UpdateRateFps,
                    AppPresetMap            = AppPresetMap,
                    AppPositionMap          = AppPositionMap,
                    MirrorLens              = MirrorLens,
                    CursorPulseColor        = CursorPulseColor,
                    ReticleEnabled          = ReticleEnabled,
                    ReticleStyle            = ReticleStyle,
                    ReticleColor            = ReticleColor,
                    ReticleSize             = ReticleSize,
                    LensTrackMode           = LensTrackMode,
                    LensTrackSize           = LensTrackSize,
                    DefaultFollowMode       = DefaultFollowMode,
                    DefaultSnapToWord       = DefaultSnapToWord,
                    SnapToWord              = SnapToWord,
                    DefaultReticle          = DefaultReticle,
                    DefaultUiSizeIndex      = DefaultUiSizeIndex,
                    DefaultTitleBarSizeIndex = DefaultTitleBarSizeIndex,
                    BarHideAllText          = BarHideAllText,
                    BarShowSizeLabel        = BarShowSizeLabel,
                    BarShowColourLabel      = BarShowColourLabel,
                    BarShowSnapLabel        = BarShowSnapLabel,
                    BarShowClickThruLabel   = BarShowClickThruLabel,
                    BarShowFreezeLabel      = BarShowFreezeLabel,
                    BarShowZoomValue        = BarShowZoomValue,
                    BarShowHoverHint        = BarShowHoverHint,
                    ZoomLockMin             = ZoomLockMin,
                    ZoomLockMax             = ZoomLockMax,
                    TintIntensity           = TintIntensity,
                    DimWhenIdle             = DimWhenIdle,
                    DimDelaySeconds         = DimDelaySeconds,
                    DimOpacity              = DimOpacity,
                    HomeZoom                = HomeZoom,
                    InvertScrollZoom        = InvertScrollZoom,
                    OpacityFollowZoom       = OpacityFollowZoom,
                    PerMonitorZoomMap       = PerMonitorZoomMap,
                    ZoomAnimate             = ZoomAnimate,
                    FocusRingColor          = FocusRingColor,
                    AppFontFamily           = string.IsNullOrWhiteSpace(AppFontFamily) ? "Segoe UI" : AppFontFamily,
                    HudFontSize             = Math.Clamp(HudFontSize <= 0 ? 12 : HudFontSize, 8, 72),
                    WizardShown             = WizardShown,
                    CloseToTray             = CloseToTray,
                    LastSeenVersion         = LastSeenVersion,
                    SettingsVersion         = SettingsVersion,
                    MainWindowX             = MainWindowX,
                    MainWindowY             = MainWindowY,
                    ColourHistory           = ColourHistory ?? new List<string>(),
                    HotkeyMods              = new Dictionary<string, int>(),
                    HotkeyVKs               = new Dictionary<string, int>()
                };

                // Hotkey bindings — all actions are remappable (scheme 2)
                dto.HotkeySchemeVersion = HotkeyBinding.SchemeVersion;
                var bindings = HotkeyBindings ?? HotkeyBinding.Defaults();
                foreach (var kvp in bindings)
                {
                    dto.HotkeyMods[kvp.Key] = (int)kvp.Value.Modifiers;
                    dto.HotkeyVKs[kvp.Key]  = (int)kvp.Value.VirtualKey;
                }

                string json = JsonSerializer.Serialize(dto, AppSettingsContext.Default.AppSettingsJson);

                string tmpPath = SettingsPath + ".tmp";
                File.WriteAllText(tmpPath, json, Encoding.UTF8);
                if (File.Exists(SettingsPath))
                    File.Replace(tmpPath, SettingsPath, destinationBackupFileName: null, ignoreMetadataErrors: true);
                else
                    File.Move(tmpPath, SettingsPath);
            }
            catch (Exception ex)
            {
                try
                {
                    string sd = Path.GetDirectoryName(SettingsPath) ?? Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                    if (!Directory.Exists(sd)) sd = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                    AppendDiagnosticLog(Path.Combine(sd, "magnifier_error.log"),
                        $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] SAVE EXCEPTION: {ex.GetType().Name}: {ex.Message}\n  {ex.StackTrace?.Split('\n')[0]}\n\n");
                }
                catch { }
            }
        }
    }
}
