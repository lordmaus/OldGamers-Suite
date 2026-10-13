using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace SuperMagnifier
{
    /// <summary>
    /// Pure data-transfer record for JSON source generation.
    /// AppSettings.Load/Save convert to/from this type.
    /// All fields match AppSettings property names for zero-friction mapping.
    /// </summary>
    public class AppSettingsJson
    {
        public double  Zoom              { get; set; } = 2.0;
        public double  Step              { get; set; } = 0.25;
        public int     LensW             { get; set; } = 800;
        public int     LensH             { get; set; } = 600;
        public int     LensX             { get; set; } = 100;
        public int     LensY             { get; set; } = 100;
        public string  RenderMode        { get; set; } = "smooth";
        public bool    AlwaysOnTop       { get; set; } = true;
        public int     DefaultLensW      { get; set; } = 800;
        public int     DefaultLensH      { get; set; } = 600;
        public double  DefaultZoom       { get; set; } = 2.0;
        public int     UiSizeIndex       { get; set; } = 1;
        public string  ColorMode         { get; set; } = "normal";
        public bool    CursorHighlight   { get; set; } = false;
        public string  CursorColor       { get; set; } = "#FF5000";
        public double  LensOpacity       { get; set; } = 1.0;
        public string  LensBorderColor   { get; set; } = "#89B4FA";
        public string  LensGripColor     { get; set; } = "#89B4FA";
        public double  LensGripSize      { get; set; } = 44;
        public bool    HotkeyToggle      { get; set; } = true;
        public string  FollowMode        { get; set; } = "mouse";
        public bool    ReadingRuler      { get; set; } = false;
        public bool    DarkMode          { get; set; } = true;
        public string  ThemeName         { get; set; } = "Dark";
        public string  LensMonitor       { get; set; } = "";
        public double  LensRelX          { get; set; } = 0.05;
        public double  LensRelY          { get; set; } = 0.05;
        public bool    AutoStart         { get; set; } = false;
        public int     TitleBarSizeIndex { get; set; } = 4;
        public bool    SessionActive     { get; set; } = false;
        public bool    ClickThrough      { get; set; } = false;
        public bool    SessionRestore    { get; set; } = true;
        public bool    SmoothPanning     { get; set; } = false;
        public double  PanLag            { get; set; } = 0.15;
        public int     LensBorderThick   { get; set; } = 2;
        public bool    EdgeDeadZone      { get; set; } = true;
        public double  RulerHeight       { get; set; } = 28.0;
        public string  RulerColorHex     { get; set; } = "#FFDC00";
        public int     UpdateRateFps     { get; set; } = 30;
        public string  AppPresetMap      { get; set; } = "";
        public string  AppPositionMap    { get; set; } = "";
        public bool    MirrorLens        { get; set; } = false;
        public string  CursorPulseColor  { get; set; } = "#64B4FF";
        public bool    ReticleEnabled    { get; set; } = false;
        public string  ReticleStyle      { get; set; } = "Cross";
        public string  ReticleColor      { get; set; } = "#FF4444";
        public int     ReticleSize       { get; set; } = 3;
        public string  LensTrackMode     { get; set; } = "fixed";
        public int     LensTrackSize     { get; set; } = 300;
        public string  DefaultFollowMode         { get; set; } = "mouse";
        public bool    DefaultSnapToWord         { get; set; } = false;
        public bool    SnapToWord                { get; set; } = false;
        public bool    DefaultReticle            { get; set; } = false;
        public int     DefaultUiSizeIndex        { get; set; } = -1;
        public int     DefaultTitleBarSizeIndex  { get; set; } = 4;
        public bool    BarHideAllText        { get; set; } = false;
        public bool    BarShowSizeLabel      { get; set; } = true;
        public bool    BarShowColourLabel    { get; set; } = true;
        public bool    BarShowSnapLabel      { get; set; } = true;
        public bool    BarShowClickThruLabel { get; set; } = true;
        public bool    BarShowFreezeLabel    { get; set; } = true;
        public bool    BarShowZoomValue      { get; set; } = true;
        public bool    BarShowHoverHint      { get; set; } = true;
        public double  ZoomLockMin       { get; set; } = 1.25;
        public double  ZoomLockMax       { get; set; } = 20.0;
        public float   TintIntensity     { get; set; } = 0.5f;
        public bool    DimWhenIdle       { get; set; } = false;
        public int     DimDelaySeconds   { get; set; } = 5;
        public double  DimOpacity        { get; set; } = 0.25;
        public double  HomeZoom          { get; set; } = 2.0;
        public bool    InvertScrollZoom  { get; set; } = false;
        public bool    OpacityFollowZoom { get; set; } = false;
        public string  PerMonitorZoomMap { get; set; } = "";
        public bool    ZoomAnimate       { get; set; } = true;
        public string  FocusRingColor    { get; set; } = "#89B4FA";
        public string  AppFontFamily    { get; set; } = "Segoe UI";
        public double  HudFontSize      { get; set; } = 12;
        public bool    WizardShown       { get; set; } = false;
        public bool    CloseToTray       { get; set; } = true;
        public string  LastSeenVersion   { get; set; } = "";
        public int     SettingsVersion   { get; set; } = 0;
        public double? MainWindowX       { get; set; } = null;
        public double? MainWindowY       { get; set; } = null;
        public List<string> ColourHistory { get; set; } = new();

        // Hotkey bindings stored as flat int pairs: HK_{action}_Mod / HK_{action}_VK
        public Dictionary<string, int> HotkeyMods { get; set; } = new();
        public Dictionary<string, int> HotkeyVKs  { get; set; } = new();
        /// <summary>Missing in files written before 2026-10-13 → 0 → migrate scheme-1 defaults.</summary>
        public int HotkeySchemeVersion { get; set; } = 0;
        public int ToastSeconds { get; set; } = 5;
    }
}
