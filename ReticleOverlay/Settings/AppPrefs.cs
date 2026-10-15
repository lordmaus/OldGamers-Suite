using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;

namespace ReticleOverlay
{
    [JsonSerializable(typeof(AppPrefsData))]
    [JsonSourceGenerationOptions(WriteIndented = true)]
    internal partial class AppPrefsContext : JsonSerializerContext { }

    public class AppPrefsData
    {
        public double WindowLeft            { get; set; } = double.NaN;
        public double WindowTop             { get; set; } = double.NaN;
        public double WindowWidth           { get; set; } = 560;
        public double WindowHeight          { get; set; } = 900;
        public bool   Maximized             { get; set; } = false;
        public bool   DarkTheme             { get; set; } = true;
        public string ThemeName             { get; set; } = "";   // "" = migrate from DarkTheme
        public string UiScale               { get; set; } = "Standard (1.0×)";
        public string AppFontFamily         { get; set; } = "Segoe UI";
        public double HudFontSize           { get; set; } = 12;
        public bool   ReticleVisible        { get; set; } = true;
        public string ReticleType           { get; set; } = "Crosshair";
        public string CustomImagePath       { get; set; } = "";
        public double ReticleSize           { get; set; } = 40.0;
        public double ReticleThickness      { get; set; } = 2.0;
        public double CentreGap             { get; set; } = 8.0;
        public double Rotation              { get; set; } = 0.0;
        public string ReticleColor          { get; set; } = "#00FF00";
        public string OutlineColor          { get; set; } = "#000000";
        public string ReticleColor2         { get; set; } = "#00FF00";
        public bool   UseColor2             { get; set; } = false;
        public bool   ShowOutline           { get; set; } = true;
        public double Opacity               { get; set; } = 1.0;
        public int    MonitorIndex          { get; set; } = 0;
        public double OffsetX               { get; set; } = 0.0;
        public double OffsetY               { get; set; } = 0.0;
        public string HotkeyToggle          { get; set; } = "F8";
        public string HotkeyProfileNext     { get; set; } = "F9";
        public string HotkeyProfilePrev     { get; set; } = "F10";
        public string HotkeyOpacityUp       { get; set; } = "";
        public string HotkeyOpacityDown     { get; set; } = "";
        public string HotkeyNudgeLeft       { get; set; } = "";
        public string HotkeyNudgeRight      { get; set; } = "";
        public string HotkeyNudgeUp         { get; set; } = "";
        public string HotkeyNudgeDown       { get; set; } = "";
        public bool   MinimizeToTrayOnClose { get; set; } = true;
        public string PreviewBackground     { get; set; } = "Dark";
        public bool   HideFromCapture       { get; set; } = false;
        public bool   AutoSwitchProfiles    { get; set; } = false;
        public string Preset1               { get; set; } = "";
        public string Preset2               { get; set; } = "";
        public string Preset3               { get; set; } = "";
        public string Preset4               { get; set; } = "";
        public string Preset5               { get; set; } = "";
        // Feature #38: Layer 2
        public bool   Layer2Enabled         { get; set; } = false;
        public string Layer2Type            { get; set; } = "Circle";
        public double Layer2Size            { get; set; } = 80.0;
        public double Layer2Thickness       { get; set; } = 1.5;
        public double Layer2CentreGap       { get; set; } = 0.0;
        public double Layer2Rotation        { get; set; } = 0.0;
        public string Layer2Color           { get; set; } = "#00FF00";
        public double Layer2Opacity         { get; set; } = 0.6;
        // Feature #31: Color palette (8 saved colors)
        public string Palette1              { get; set; } = "#00FF00";
        public string Palette2              { get; set; } = "#FF0000";
        public string Palette3              { get; set; } = "#FFFFFF";
        public string Palette4              { get; set; } = "#FFD700";
        public string Palette5              { get; set; } = "#00BFFF";
        public string Palette6              { get; set; } = "#FF8C00";
        public string Palette7              { get; set; } = "#FF00FF";
        public string Palette8              { get; set; } = "#000000";
    }

    public class AppPrefs : INotifyPropertyChanged
    {
        public static readonly AppPrefs Instance = new AppPrefs();

        private static readonly string Dir  = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ReticleOverlay");
        private static readonly string File = Path.Combine(Dir, "prefs.json");

        public event PropertyChangedEventHandler PropertyChanged;
        public event EventHandler ReticlePropertyChanged;

        private void Notify([CallerMemberName] string name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        private void NotifyReticle([CallerMemberName] string name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
            ReticlePropertyChanged?.Invoke(this, EventArgs.Empty);
        }

        // ── Window / UI ───────────────────────────────────────────────────────
        public double WindowLeft   { get => field; set { if (field == value) return; field = value; Notify(); } } = double.NaN;
        public double WindowTop    { get => field; set { if (field == value) return; field = value; Notify(); } } = double.NaN;
        public double WindowWidth  { get => field; set { if (field == value) return; field = Math.Max(460, value); Notify(); } } = 560;
        public double WindowHeight { get => field; set { if (field == value) return; field = Math.Max(500, value); Notify(); } } = 900;
        public bool   Maximized    { get => field; set { if (field == value) return; field = value; Notify(); } } = false;
        public bool   DarkTheme    { get => field; set { if (field == value) return; field = value; Notify(); } } = true;
        public string ThemeName    { get => field; set { if (field == value) return; field = value ?? ""; Notify(); } } = "";
        public string UiScale      { get => field; set { if (field == value) return; field = value ?? "Standard (1.0×)"; Notify(); } } = "Standard (1.0×)";
        public string AppFontFamily { get => field; set { if (field == value) return; field = string.IsNullOrWhiteSpace(value) ? "Segoe UI" : value; Notify(); } } = "Segoe UI";
        public double HudFontSize  { get => field; set { if (field == value) return; field = Math.Clamp(value, 8, 72); Notify(); } } = 12;
        public string PreviewBackground { get => field; set { if (field == value) return; field = value ?? "Dark"; Notify(); } } = "Dark";

        // ── Reticle Layer 1 ───────────────────────────────────────────────────
        public bool   ReticleVisible    { get => field; set { if (field == value) return; field = value; NotifyReticle(); } } = true;
        public string ReticleType       { get => field; set { if (field == value) return; field = value ?? "Crosshair"; NotifyReticle(); } } = "Crosshair";
        public string CustomImagePath   { get => field; set { if (field == value) return; field = value ?? ""; NotifyReticle(); } } = "";
        public double ReticleSize       { get => field; set { if (field == value) return; field = Math.Max(4, Math.Min(200, value)); NotifyReticle(); } } = 40.0;
        public double ReticleThickness  { get => field; set { if (field == value) return; field = Math.Max(1, Math.Min(20, value)); NotifyReticle(); } } = 2.0;
        public double CentreGap         { get => field; set { if (field == value) return; field = Math.Max(0, Math.Min(49, value)); NotifyReticle(); } } = 8.0;
        public double Rotation          { get => field; set { if (field == value) return; field = Math.Max(0, Math.Min(359, value)); NotifyReticle(); } } = 0.0;
        public string ReticleColor      { get => field; set { if (field == value) return; field = value ?? "#00FF00"; NotifyReticle(); } } = "#00FF00";
        public string OutlineColor      { get => field; set { if (field == value) return; field = value ?? "#000000"; NotifyReticle(); } } = "#000000";
        public string ReticleColor2     { get => field; set { if (field == value) return; field = value ?? "#00FF00"; NotifyReticle(); } } = "#00FF00";
        public bool   UseColor2         { get => field; set { if (field == value) return; field = value; NotifyReticle(); } } = false;
        public bool   ShowOutline       { get => field; set { if (field == value) return; field = value; NotifyReticle(); } } = true;
        public double Opacity           { get => field; set { if (field == value) return; field = Math.Max(0.05, Math.Min(1.0, value)); NotifyReticle(); } } = 1.0;
        public double OffsetX           { get => field; set { if (field == value) return; field = Math.Max(-400, Math.Min(400, value)); NotifyReticle(); } } = 0.0;
        public double OffsetY           { get => field; set { if (field == value) return; field = Math.Max(-400, Math.Min(400, value)); NotifyReticle(); } } = 0.0;

        // ── Feature #38: Reticle Layer 2 ─────────────────────────────────────
        public bool   Layer2Enabled   { get => field; set { if (field == value) return; field = value; NotifyReticle(); } } = false;
        public string Layer2Type      { get => field; set { if (field == value) return; field = value ?? "Circle"; NotifyReticle(); } } = "Circle";
        public double Layer2Size      { get => field; set { if (field == value) return; field = Math.Max(4, Math.Min(400, value)); NotifyReticle(); } } = 80.0;
        public double Layer2Thickness { get => field; set { if (field == value) return; field = Math.Max(1, Math.Min(20, value)); NotifyReticle(); } } = 1.5;
        public double Layer2CentreGap { get => field; set { if (field == value) return; field = Math.Max(0, Math.Min(49, value)); NotifyReticle(); } } = 0.0;
        public double Layer2Rotation  { get => field; set { if (field == value) return; field = Math.Max(0, Math.Min(359, value)); NotifyReticle(); } } = 0.0;
        public string Layer2Color     { get => field; set { if (field == value) return; field = value ?? "#00FF00"; NotifyReticle(); } } = "#00FF00";
        public double Layer2Opacity   { get => field; set { if (field == value) return; field = Math.Max(0.05, Math.Min(1.0, value)); NotifyReticle(); } } = 0.6;

        // ── Feature #31: Color palette ────────────────────────────────────────
        public string Palette1 { get => field; set { if (field == value) return; field = value ?? "#00FF00"; Notify(); } } = "#00FF00";
        public string Palette2 { get => field; set { if (field == value) return; field = value ?? "#FF0000"; Notify(); } } = "#FF0000";
        public string Palette3 { get => field; set { if (field == value) return; field = value ?? "#FFFFFF"; Notify(); } } = "#FFFFFF";
        public string Palette4 { get => field; set { if (field == value) return; field = value ?? "#FFD700"; Notify(); } } = "#FFD700";
        public string Palette5 { get => field; set { if (field == value) return; field = value ?? "#00BFFF"; Notify(); } } = "#00BFFF";
        public string Palette6 { get => field; set { if (field == value) return; field = value ?? "#FF8C00"; Notify(); } } = "#FF8C00";
        public string Palette7 { get => field; set { if (field == value) return; field = value ?? "#FF00FF"; Notify(); } } = "#FF00FF";
        public string Palette8 { get => field; set { if (field == value) return; field = value ?? "#000000"; Notify(); } } = "#000000";

        public string[] GetPalette() => new[]{ Palette1,Palette2,Palette3,Palette4,Palette5,Palette6,Palette7,Palette8 };

        public void SetPalette(int index, string hex)
        {
            // Use a getter/setter action array to avoid a verbose 8-case switch.
            // Symmetric with GetPalette() so both read and write use the same slot ordering.
            Action<string>[] setters =
            {
                v => Palette1=v, v => Palette2=v, v => Palette3=v, v => Palette4=v,
                v => Palette5=v, v => Palette6=v, v => Palette7=v, v => Palette8=v
            };
            if ((uint)index < (uint)setters.Length) setters[index](hex ?? "#00FF00");
        }

        // ── Monitor / Hotkeys / Behaviour / Presets (unchanged) ──────────────
        public int    MonitorIndex          { get => field; set { if (field == value) return; field = Math.Max(0, value); Notify(); } } = 0;
        public string HotkeyToggle          { get => field; set { if (field == value) return; field = value ?? ""; Notify(); } } = "F8";
        public string HotkeyProfileNext     { get => field; set { if (field == value) return; field = value ?? ""; Notify(); } } = "F9";
        public string HotkeyProfilePrev     { get => field; set { if (field == value) return; field = value ?? ""; Notify(); } } = "F10";
        public string HotkeyOpacityUp       { get => field; set { if (field == value) return; field = value ?? ""; Notify(); } } = "";
        public string HotkeyOpacityDown     { get => field; set { if (field == value) return; field = value ?? ""; Notify(); } } = "";
        public string HotkeyNudgeLeft       { get => field; set { if (field == value) return; field = value ?? ""; Notify(); } } = "";
        public string HotkeyNudgeRight      { get => field; set { if (field == value) return; field = value ?? ""; Notify(); } } = "";
        public string HotkeyNudgeUp         { get => field; set { if (field == value) return; field = value ?? ""; Notify(); } } = "";
        public string HotkeyNudgeDown       { get => field; set { if (field == value) return; field = value ?? ""; Notify(); } } = "";
        public bool   MinimizeToTrayOnClose { get => field; set { if (field == value) return; field = value; Notify(); } } = true;
        public bool   HideFromCapture       { get => field; set { if (field == value) return; field = value; Notify(); } } = false;
        // Feature #1 / Bug #3 (2026-09-07 review): real per-game auto-switching --
        // global on/off; per-profile match string lives on ReticleProfile.ProcessName.
        public bool   AutoSwitchProfiles    { get => field; set { if (field == value) return; field = value; Notify(); } } = false;
        public string Preset1 { get => field; set { if (field == value) return; field = value ?? ""; Notify(); } } = "";
        public string Preset2 { get => field; set { if (field == value) return; field = value ?? ""; Notify(); } } = "";
        public string Preset3 { get => field; set { if (field == value) return; field = value ?? ""; Notify(); } } = "";
        public string Preset4 { get => field; set { if (field == value) return; field = value ?? ""; Notify(); } } = "";
        public string Preset5 { get => field; set { if (field == value) return; field = value ?? ""; Notify(); } } = "";

        public string[] GetPresets() => new[] { Preset1, Preset2, Preset3, Preset4, Preset5 };

        public void SetPreset(int index, string name)
        {
            Action<string>[] setters =
            {
                v => Preset1=v, v => Preset2=v, v => Preset3=v,
                v => Preset4=v, v => Preset5=v
            };
            if ((uint)index < (uint)setters.Length) setters[index](name ?? "");
        }

        private AppPrefsData ToData() => new AppPrefsData
        {
            WindowLeft=WindowLeft, WindowTop=WindowTop, WindowWidth=WindowWidth,
            WindowHeight=WindowHeight, Maximized=Maximized, DarkTheme=DarkTheme,
            ThemeName=ThemeName, UiScale=UiScale, AppFontFamily=AppFontFamily, HudFontSize=HudFontSize,
            ReticleVisible=ReticleVisible, ReticleType=ReticleType,
            CustomImagePath=CustomImagePath, ReticleSize=ReticleSize,
            ReticleThickness=ReticleThickness, CentreGap=CentreGap, Rotation=Rotation,
            ReticleColor=ReticleColor, OutlineColor=OutlineColor, ReticleColor2=ReticleColor2,
            UseColor2=UseColor2, ShowOutline=ShowOutline, Opacity=Opacity,
            MonitorIndex=MonitorIndex, OffsetX=OffsetX, OffsetY=OffsetY,
            HotkeyToggle=HotkeyToggle, HotkeyProfileNext=HotkeyProfileNext,
            HotkeyProfilePrev=HotkeyProfilePrev, HotkeyOpacityUp=HotkeyOpacityUp,
            HotkeyOpacityDown=HotkeyOpacityDown, HotkeyNudgeLeft=HotkeyNudgeLeft,
            HotkeyNudgeRight=HotkeyNudgeRight, HotkeyNudgeUp=HotkeyNudgeUp,
            HotkeyNudgeDown=HotkeyNudgeDown, MinimizeToTrayOnClose=MinimizeToTrayOnClose,
            PreviewBackground=PreviewBackground, HideFromCapture=HideFromCapture,
            AutoSwitchProfiles=AutoSwitchProfiles,
            Preset1=Preset1, Preset2=Preset2, Preset3=Preset3, Preset4=Preset4, Preset5=Preset5,
            Layer2Enabled=Layer2Enabled, Layer2Type=Layer2Type, Layer2Size=Layer2Size,
            Layer2Thickness=Layer2Thickness, Layer2CentreGap=Layer2CentreGap,
            Layer2Rotation=Layer2Rotation, Layer2Color=Layer2Color, Layer2Opacity=Layer2Opacity,
            Palette1=Palette1, Palette2=Palette2, Palette3=Palette3, Palette4=Palette4,
            Palette5=Palette5, Palette6=Palette6, Palette7=Palette7, Palette8=Palette8
        };

        private void FromData(AppPrefsData d)
        {
            WindowLeft=d.WindowLeft; WindowTop=d.WindowTop; WindowWidth=d.WindowWidth;
            WindowHeight=d.WindowHeight; Maximized=d.Maximized; DarkTheme=d.DarkTheme;
            // Migration: ThemeName="" in old prefs → App.cs derives from DarkTheme on startup
            ThemeName=d.ThemeName ?? "";
            UiScale=d.UiScale; AppFontFamily=d.AppFontFamily; HudFontSize=d.HudFontSize;
            ReticleVisible=d.ReticleVisible; ReticleType=d.ReticleType;
            CustomImagePath=d.CustomImagePath; ReticleSize=d.ReticleSize;
            ReticleThickness=d.ReticleThickness; CentreGap=d.CentreGap; Rotation=d.Rotation;
            ReticleColor=d.ReticleColor; OutlineColor=d.OutlineColor; ReticleColor2=d.ReticleColor2;
            UseColor2=d.UseColor2; ShowOutline=d.ShowOutline; Opacity=d.Opacity;
            MonitorIndex=d.MonitorIndex; OffsetX=d.OffsetX; OffsetY=d.OffsetY;
            HotkeyToggle=d.HotkeyToggle; HotkeyProfileNext=d.HotkeyProfileNext;
            HotkeyProfilePrev=d.HotkeyProfilePrev; HotkeyOpacityUp=d.HotkeyOpacityUp;
            HotkeyOpacityDown=d.HotkeyOpacityDown; HotkeyNudgeLeft=d.HotkeyNudgeLeft;
            HotkeyNudgeRight=d.HotkeyNudgeRight; HotkeyNudgeUp=d.HotkeyNudgeUp;
            HotkeyNudgeDown=d.HotkeyNudgeDown; MinimizeToTrayOnClose=d.MinimizeToTrayOnClose;
            PreviewBackground=d.PreviewBackground; HideFromCapture=d.HideFromCapture;
            AutoSwitchProfiles=d.AutoSwitchProfiles;
            Preset1=d.Preset1; Preset2=d.Preset2; Preset3=d.Preset3; Preset4=d.Preset4; Preset5=d.Preset5;
            Layer2Enabled=d.Layer2Enabled; Layer2Type=d.Layer2Type; Layer2Size=d.Layer2Size;
            Layer2Thickness=d.Layer2Thickness; Layer2CentreGap=d.Layer2CentreGap;
            Layer2Rotation=d.Layer2Rotation; Layer2Color=d.Layer2Color; Layer2Opacity=d.Layer2Opacity;
            Palette1=d.Palette1; Palette2=d.Palette2; Palette3=d.Palette3; Palette4=d.Palette4;
            Palette5=d.Palette5; Palette6=d.Palette6; Palette7=d.Palette7; Palette8=d.Palette8;
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(Dir);
                string json = JsonSerializer.Serialize(ToData(), AppPrefsContext.Default.AppPrefsData);
                string tmp  = File + ".tmp";
                System.IO.File.WriteAllText(tmp, json, System.Text.Encoding.UTF8);
                // Bug #6 (2026-09-07 review): File.Move is a single filesystem metadata
                // swap on NTFS -- unlike the prior Copy+Delete, a crash/power-loss mid-write
                // cannot leave prefs.json partially written.
                System.IO.File.Move(tmp, File, overwrite: true);
            }
            catch (Exception ex) { AppLog.Error("Prefs.Save", ex); }
        }

        public void Load()
        {
            if (!System.IO.File.Exists(File)) return;
            try
            {
                string json = System.IO.File.ReadAllText(File, System.Text.Encoding.UTF8);
                var d = JsonSerializer.Deserialize(json, AppPrefsContext.Default.AppPrefsData);
                if (d != null) FromData(d);
            }
            catch (JsonException ex) { CorruptFile.SetAside(File, ex); }   // RO-05
            catch (Exception ex) { AppLog.Error("Prefs.Load", ex); }
        }

        public void ApplyTo(Window w)
        {
            w.Width  = WindowWidth;
            w.Height = WindowHeight;
            if (!double.IsNaN(WindowLeft) && !double.IsNaN(WindowTop))
            {
                double left = Math.Max(SystemParameters.VirtualScreenLeft,
                              Math.Min(WindowLeft, SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth  - 100));
                double top  = Math.Max(SystemParameters.VirtualScreenTop,
                              Math.Min(WindowTop,  SystemParameters.VirtualScreenTop  + SystemParameters.VirtualScreenHeight -  40));
                w.Left = left; w.Top = top;
            }
            else w.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            if (Maximized) w.WindowState = WindowState.Maximized;
        }

        public void SnapshotFrom(Window w)
        {
            Maximized = w.WindowState == WindowState.Maximized;
            if (!Maximized) { WindowLeft = w.Left; WindowTop = w.Top; WindowWidth = w.Width; WindowHeight = w.Height; }
        }
    }
}
