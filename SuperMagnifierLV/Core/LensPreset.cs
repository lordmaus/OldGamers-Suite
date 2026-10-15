using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace SuperMagnifier
{
    /// <summary>
    /// A named snapshot of lens configuration that can be saved and recalled.
    /// </summary>
    public class LensPreset
    {
        public string Name        { get; set; } = "";
        public double Zoom        { get; set; } = 2.0;
        public int    LensW       { get; set; } = 520;
        public int    LensH       { get; set; } = 360;
        public string ColorMode   { get; set; } = "normal";
        public double LensOpacity { get; set; } = 1.0;
        public double ZoomMin     { get; set; } = 1.25;
        public double ZoomMax     { get; set; } = 20.0;
        // Extended fields — added to make presets fully self-contained
        public string FollowMode    { get; set; } = "mouse";  // "mouse" or "caret"
        public bool   SnapToWord    { get; set; } = false;
        public bool   ReticleEnabled{ get; set; } = false;

        [JsonIgnore] public bool ZoomLocked => ZoomMin > 1.25 || ZoomMax < 20.0;
        [JsonIgnore] public bool IsEmpty    => string.IsNullOrWhiteSpace(Name);

        // ── Persistence ───────────────────────────────────────────────────────
        // Stored alongside settings in %AppData%\SuperMagnifierLV\
        private static readonly string PresetsPath =
            Path.Combine(
                Path.GetDirectoryName(AppSettings.FilePath),
                "lens_presets.json");

        public static List<LensPreset> LoadAll() => LoadAll(PresetsPath);

        public static List<LensPreset> LoadAll(string path)
        {
            var list = new List<LensPreset>();
            try
            {
                if (File.Exists(path))
                {
                    string json = File.ReadAllText(path, Encoding.UTF8);
                    try
                    {
                        list = JsonSerializer.Deserialize(json, MapsJsonContext.Default.ListLensPreset) ?? new List<LensPreset>();
                    }
                    catch (JsonException)
                    {
                        // Pre-2026-10-13 hand-written format (possibly with comma decimals) — read
                        // tolerantly and rewrite in the current format.
                        list = ParseLegacy(json);
                        if (string.Equals(path, PresetsPath, StringComparison.OrdinalIgnoreCase))
                            SaveAll(list, path);   // don't rewrite an imported file
                    }
                }
            }
            catch (Exception ex) { AppLog.Error("LensPreset.LoadAll", ex); }
            for (int i = 0; i < list.Count; i++) list[i] ??= new LensPreset();
            while (list.Count < 5) list.Add(new LensPreset());
            if (list.Count > 5) list.RemoveRange(5, list.Count - 5);
            return list;
        }

        internal static List<LensPreset> ParseLegacy(string json)
        {
            var list = new List<LensPreset>();
            foreach (Match m in LegacyJson.ObjectRx().Matches(json))
            {
                if (list.Count >= 5) break;
                string body = m.Groups[1].Value;
                list.Add(new LensPreset
                {
                    Name           = LegacyJson.Str (body, "Name",        ""),
                    Zoom           = LegacyJson.Dbl (body, "Zoom",        2.0),
                    LensW          = LegacyJson.Int (body, "LensW",       520),
                    LensH          = LegacyJson.Int (body, "LensH",       360),
                    ColorMode      = LegacyJson.Str (body, "ColorMode",   "normal"),
                    LensOpacity    = LegacyJson.Dbl (body, "LensOpacity", 1.0),
                    ZoomMin        = LegacyJson.Dbl (body, "ZoomMin",     1.25),
                    ZoomMax        = LegacyJson.Dbl (body, "ZoomMax",     20.0),
                    FollowMode     = LegacyJson.Str (body, "FollowMode",  "mouse"),
                    SnapToWord     = LegacyJson.Bool(body, "SnapToWord",  false),
                    ReticleEnabled = LegacyJson.Bool(body, "ReticleEnabled", false),
                });
            }
            return list;
        }

        /// <summary>Save all presets. Returns true on success, false on failure.</summary>
        public static bool SaveAll(List<LensPreset> presets) => SaveAll(presets, PresetsPath);

        /// <summary>Save all presets to path. Returns true on success, false on failure.</summary>
        public static bool SaveAll(List<LensPreset> presets, string path)
        {
            try
            {
                string json = JsonSerializer.Serialize(presets ?? new List<LensPreset>(), MapsJsonContext.Default.ListLensPreset);
                string tmp = path + ".tmp";
                File.WriteAllText(tmp, json, Encoding.UTF8);
                if (File.Exists(path))
                    File.Replace(tmp, path, destinationBackupFileName: null, ignoreMetadataErrors: true);
                else
                    File.Move(tmp, path);
                return true;
            }
            catch (Exception ex)
            {
                AppLog.Error("LensPreset.SaveAll", ex);
                return false;
            }
        }
    }
}
