using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ReticleOverlay
{
    [JsonSerializable(typeof(List<ReticleProfile>))]
    [JsonSourceGenerationOptions(WriteIndented = true)]
    internal partial class ProfileStoreContext : JsonSerializerContext { }

    public class ReticleProfile
    {
        public string Name             { get; set; } = "Default";
        // Feature #1 / Bug #3 (2026-09-07 review): foreground .exe name that auto-switches
        // to this profile when AppPrefs.AutoSwitchProfiles is on (e.g. "game.exe"). Empty = no auto-switch.
        public string ProcessName       { get; set; } = "";
        // Layer 1
        public string ReticleType      { get; set; } = "Crosshair";
        public string CustomImagePath  { get; set; } = "";
        public double ReticleSize      { get; set; } = 40.0;
        public double ReticleThickness { get; set; } = 2.0;
        public double CentreGap        { get; set; } = 8.0;
        public double Rotation         { get; set; } = 0.0;
        public string ReticleColor     { get; set; } = "#00FF00";
        public string OutlineColor     { get; set; } = "#000000";
        public string ReticleColor2    { get; set; } = "#00FF00";
        public bool   UseColor2        { get; set; } = false;
        public bool   ShowOutline      { get; set; } = true;
        public double Opacity          { get; set; } = 1.0;
        public double OffsetX          { get; set; } = 0.0;
        public double OffsetY          { get; set; } = 0.0;
        // Layer 2
        public bool   Layer2Enabled    { get; set; } = false;
        public string Layer2Type       { get; set; } = "Circle";
        public double Layer2Size       { get; set; } = 80.0;
        public double Layer2Thickness  { get; set; } = 1.5;
        public double Layer2CentreGap  { get; set; } = 0.0;
        public double Layer2Rotation   { get; set; } = 0.0;
        public string Layer2Color      { get; set; } = "#00FF00";
        public double Layer2Opacity    { get; set; } = 0.6;

        // Profile list items are ReticleProfile objects (icon pass 2026-09-25): UIA item names and
        // default text come from here.
        public override string ToString()
            => string.IsNullOrEmpty(ProcessName) ? Name : $"{Name}, auto-switch {ProcessName}";
    }

    public static class ProfileStore
    {
        private static readonly string Dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ReticleOverlay");
        internal static readonly string File = Path.Combine(Dir, "profiles.json");

        public static List<ReticleProfile> Load() => Load(File);

        internal static List<ReticleProfile> Load(string file)
        {
            if (!System.IO.File.Exists(file)) return new List<ReticleProfile>();
            try
            {
                string json = System.IO.File.ReadAllText(file, System.Text.Encoding.UTF8);
                return JsonSerializer.Deserialize(json, ProfileStoreContext.Default.ListReticleProfile)
                       ?? new List<ReticleProfile>();
            }
            catch (JsonException ex)
            {
                // RO-05: keep the damaged file — the next Save would otherwise overwrite every profile.
                CorruptFile.SetAside(file, ex);
                return new List<ReticleProfile>();
            }
            catch (Exception ex) { CrashLog.Write("ProfileStore.Load", ex); return new List<ReticleProfile>(); }
        }

        public static void Save(List<ReticleProfile> profiles) => Save(profiles, File);

        internal static void Save(List<ReticleProfile> profiles, string file)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(file) ?? ".");
                string json = JsonSerializer.Serialize(profiles, ProfileStoreContext.Default.ListReticleProfile);
                string tmp  = file + ".tmp";
                System.IO.File.WriteAllText(tmp, json, System.Text.Encoding.UTF8);
                System.IO.File.Move(tmp, file, overwrite: true);
            }
            catch (Exception ex) { CrashLog.Write("ProfileStore.Save", ex); }
        }
    }
}
