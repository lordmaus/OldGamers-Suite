using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BadgeOverlay
{
    // ── JSON Source Generation ────────────────────────────────────────────────
    [JsonSerializable(typeof(AppSettings))]
    [JsonSerializable(typeof(CustomRule))]
    [JsonSerializable(typeof(BadgeSide))]
    [JsonSerializable(typeof(BadgePreset))]
    [JsonSerializable(typeof(BadgePreset[]))]
    [JsonSerializable(typeof(System.Collections.Generic.List<CustomRule>))]
    [JsonSerializable(typeof(System.Collections.Generic.List<string>))]
    [JsonSourceGenerationOptions(
        WriteIndented = true,
        UseStringEnumConverter = true)]
    internal partial class AppSettingsJsonContext : JsonSerializerContext { }

    /// <summary>
    /// Preferred side for badge tooltip relative to the cursor.
    /// PositionBadge() auto-flips toward screen centre when near an edge.
    /// </summary>
    public enum BadgeSide { Right, BottomRight, Bottom, BottomLeft, Left }

    /// <summary>Badge + Info Overlay colours (BO-05). Windows High Contrast always wins.</summary>
    public enum BadgeStyle { Dark, Light, MatchTheme }

    /// <summary>
    /// Controls how aggressively the detector fires badges on UIA
    /// elements. Trades completeness for noise — ribbon-heavy apps are
    /// far calmer at <see cref="InteractiveOnly"/>, while cursor-training
    /// users benefit from <see cref="AllNamed"/>.
    /// </summary>
    public enum DetectionGranularity
    {
        /// <summary>Badge fires on every UIA element with a name —
        /// labels, static text, images, separators, etc. Maximum
        /// context but also maximum badge flicker on dense UIs.</summary>
        AllNamed,

        /// <summary>Badge fires only on elements that are actionable —
        /// invokable, focusable via keyboard, toggleable, or selectable.
        /// Skips pure labels, decorations, and static images. Good
        /// balance for daily use in complex apps.</summary>
        InteractiveOnly,

        /// <summary>Badge fires only on elements whose ControlType is
        /// Button (or subtypes: SplitButton, ToggleButton, MenuItem).
        /// Strictest — nothing else produces a badge from UIA. Win32
        /// button detection still runs beneath.</summary>
        ButtonsOnly,
    }

    /// <summary>
    /// Trigger model for Screen Reader Mode speech.
    /// Persisted via JSON enum-as-string, so reordering the values
    /// here would be a breaking change for existing settings files.
    /// New values must be appended.
    /// </summary>
    public enum SpeechTriggerMode
    {
        /// <summary>Speak on every label change. Most responsive;
        /// can be noisy in dense UIs (Office ribbon, IDE toolbars).</summary>
        Immediate,

        /// <summary>Speak only when the cursor has rested on the
        /// same element for <see cref="AppSettings.SpeechDwellSeconds"/>
        /// seconds. Sweeping past controls stays silent.</summary>
        Dwell,
    }

    /// <summary>User-defined button detection rule checked before built-in patterns.</summary>
    public class CustomRule
    {
        /// <summary>Kind of match performed by this rule.</summary>
        public enum MatchKind { Text, Class }

        // Persisted JSON property uses the string "text"/"class" for
        // forward/back compat with the pre-enum schema. The MatchType
        // property is the type-safe face used in code.
        [JsonPropertyName("MatchType")]
        public string MatchTypeRaw
        {
            get => MatchType == MatchKind.Class ? "class" : "text";
            set => MatchType = (value ?? "").Equals("class", StringComparison.OrdinalIgnoreCase)
                ? MatchKind.Class
                : MatchKind.Text;
        }

        [JsonIgnore]
        public MatchKind MatchType { get; set; } = MatchKind.Text;

        public string MatchValue { get; set; } = "";
        public string Label      { get; set; } = "";
        public string Category   { get; set; } = "Action";

        [JsonIgnore]
        public string Summary =>
            $"[{(MatchType == MatchKind.Class ? "Class" : "Text")}]  {MatchValue}  →  {Label}  ({Category})";
    }

    /// <summary>
    /// Persisted user preferences for Badge Overlay.
    /// Serialised to %APPDATA%\BadgeOverlay\settings.json.
    /// Forward-compatible with legacy CursorOverlay settings.json —
    /// cursor fields in old JSON are silently ignored on deserialisation.
    /// </summary>
    public partial class AppSettings
    {
        // ── Schema versioning ─────────────────────────────────────────────────
        /// <summary>
        /// Incremented when AppSettings shape changes in a breaking way.
        /// Used by Load() to run migrations before deserialising.
        /// v1: Initial BadgeOverlay schema (April 2026).
        /// v2: EnableUIADetection default flipped back to true after
        ///     dwell-gating made it safe for cursor-hook tools.
        /// </summary>
        public int SchemaVersion { get; set; } = 2;
        public const int CurrentSchemaVersion = 2;


        // ── Serialisation ─────────────────────────────────────────────────────
        //
        // Path resolution rules (in priority order):
        //   1. Portable mode — a "portable.flag" file beside the EXE
        //      forces both settings.json and the crash log into the EXE
        //      folder. Used by USB-stick installs and locked-down corp
        //      machines that disallow %APPDATA% writes.
        //   2. Default — %APPDATA%\BadgeOverlay\settings.json. The
        //      installer's MSI / NSIS uninstall covers this.
        //
        // Both paths are computed lazily on every access so the toggle
        // takes effect without a process restart, except for already-loaded
        // settings (which is why the StartupSettings UI shows the
        // "Restart required" notice when toggling).

        /// <summary>Directory containing the running EXE.</summary>
        private static string ExeDirectory
        {
            get
            {
                // Process.MainModule.FileName is reliable under
                // single-file publish — AppContext.BaseDirectory points
                // to the runtime extraction temp dir, which we don't want.
                try
                {
                    string exe = System.Diagnostics.Process.GetCurrentProcess()
                        .MainModule?.FileName;
                    if (!string.IsNullOrEmpty(exe))
                        return Path.GetDirectoryName(exe);
                }
                catch { }
                // Fallback for hosts where MainModule throws (rare).
                return AppContext.BaseDirectory;
            }
        }

        /// <summary>True when a "portable.flag" file exists next to the EXE.</summary>
        public static bool IsPortableMode
        {
            get
            {
                try
                {
                    string flag = Path.Combine(ExeDirectory, "portable.flag");
                    return File.Exists(flag);
                }
                catch { return false; }
            }
        }

        /// <summary>
        /// Switches portable mode on or off by creating / deleting the
        /// "portable.flag" file next to the EXE. Returns true on success.
        /// Caller must prompt the user to restart so the new SettingsPath
        /// takes effect on the next Load.
        /// </summary>
        public static bool SetPortableMode(bool enabled)
        {
            try
            {
                string flag = Path.Combine(ExeDirectory, "portable.flag");
                if (enabled)
                {
                    if (!File.Exists(flag))
                        File.WriteAllText(flag,
                            "Portable mode marker — delete this file to revert to %APPDATA% storage.\r\n" +
                            "Created by Badge Overlay. Safe to ignore if you don't know what it does.\r\n");
                }
                else
                {
                    if (File.Exists(flag))
                        File.Delete(flag);
                }
                return true;
            }
            catch (Exception ex)
            {
                CrashLog.Write("SetPortableMode", ex);
                return false;
            }
        }

        private static string SettingsPath =>
            IsPortableMode
                ? Path.Combine(ExeDirectory, "settings.json")
                : Path.Combine(
                      Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                      "BadgeOverlay", "settings.json");

        [JsonIgnore] public bool   LoadedFromDefaults { get; private set; }
        [JsonIgnore] public string LastLoadError     { get; private set; }

        // Two serialisation paths:
        //
        //   PRIMARY  — AppSettingsJsonContext (source-generated, AOT-safe).
        //              Used by Load() and Save(). Fast, trim-friendly.
        //              Does NOT consult _jsonOpts; JsonStringEnumConverter
        //              is handled through the source-gen attribute pipeline.
        //
        //   FALLBACK — _jsonOpts (reflection-based JsonSerializerOptions).
        //              Used only by MainWindow Export/Import helpers via
        //              the SharedJsonOpts public accessor. These paths go
        //              through the reflection serialiser because they work
        //              on arbitrary user-supplied JSON files that may have
        //              been produced by older builds with different defaults.
        //              JsonStringEnumConverter here handles enum values in
        //              those files correctly. This field is NOT dead code.
        private static readonly JsonSerializerOptions _jsonOpts = new JsonSerializerOptions
        {
            WriteIndented               = true,
            PropertyNameCaseInsensitive = true,
            Converters                  = { new JsonStringEnumConverter() }
        };

        /// <summary>
        /// Reflection-based options used by Export/Import (MainWindow).
        /// Not used by the primary Load/Save path (source-generated).
        /// </summary>
        public static JsonSerializerOptions SharedJsonOpts => _jsonOpts;

        public static AppSettings Load()
        {
            try
            {
                string path = SettingsPath;
                if (!File.Exists(path))
                    return new AppSettings { LoadedFromDefaults = true, LastLoadError = "First launch — factory defaults applied." };

                string json = File.ReadAllText(path);
                if (string.IsNullOrWhiteSpace(json))
                    return new AppSettings { LoadedFromDefaults = true, LastLoadError = "settings.json was empty." };

                // Legacy enum value migration. v1 builds wrote
                // SpeechTrigger="Hotkey" — that mode was retired in
                // favour of a runtime SpeechMuted toggle (mapped to a
                // dedicated hotkey). The enum value no longer exists,
                // so the source-generated deserialiser would throw on
                // it. Substitute "Immediate" so old files load cleanly;
                // the user can switch to Dwell from the UI if they
                // prefer. No-op for current files.
                if (json.IndexOf("\"SpeechTrigger\"", StringComparison.Ordinal) >= 0 &&
                    json.IndexOf("\"Hotkey\"",        StringComparison.Ordinal) >= 0)
                {
                    json = json.Replace(
                        "\"SpeechTrigger\": \"Hotkey\"",
                        "\"SpeechTrigger\": \"Immediate\"")
                              .Replace(
                        "\"SpeechTrigger\":\"Hotkey\"",
                        "\"SpeechTrigger\":\"Immediate\"");
                }

                AppSettings s = JsonSerializer.Deserialize(json, AppSettingsJsonContext.Default.AppSettings);
                if (s == null)
                    return new AppSettings { LoadedFromDefaults = true, LastLoadError = "Deserialize returned null." };

                // ── Schema migration ─────────────────────────────────────────
                if (s.SchemaVersion < CurrentSchemaVersion)
                {
                    int from = s.SchemaVersion;
                    Migrate(s);
                    s.SchemaVersion = CurrentSchemaVersion;
                    // Persist migrated settings immediately so the next launch
                    // sees the current schema even if the user exits before any
                    // change triggers a normal save.
                    try { s.Save(); }
                    catch (Exception ex) { CrashLog.Write("Migration save failed", ex); }
                    // Log every migration encounter — gives visibility into how
                    // many users still load v1 / v2 / etc files. Once the log
                    // shows zero hits for a given <from> for a couple of
                    // releases, the corresponding case in Migrate() can be
                    // retired (delete the case + this log slows again as the
                    // path simply isn't reached).
                    CrashLog.Write($"Settings migrated v{from} → v{CurrentSchemaVersion}");
                }
                // Future-version settings (newer app wrote this file, older app is reading it):
                // accept — unknown fields are ignored, and we don't overwrite the stored version.

                // Guard preset array against null slots / wrong length from
                // corrupt or truncated JSON. Must run after migration so any
                // newly-added preset fields get proper defaults.
                s.EnsurePresets();

                // Clamp BadgeFontSize to a renderable / sane range. Out-of-band
                // values from a hand-edited settings.json would otherwise leave
                // the badge text microscopic or hostile-huge.
                if (s.BadgeFontSize < 8)  s.BadgeFontSize = 8;
                if (s.BadgeFontSize > 72) s.BadgeFontSize = 72;
                if (string.IsNullOrWhiteSpace(s.AppFontFamily)) s.AppFontFamily = "Segoe UI";
                if (!Enum.IsDefined(s.BadgeStyle)) s.BadgeStyle = BadgeStyle.Dark;

                s.LoadedFromDefaults = false;
                return s;
            }
            catch (Exception ex)
            {
                CrashLog.Write("AppSettings.Load failed", ex);
                // Quarantine the corrupt file so the next Save() doesn't
                // silently overwrite it. Renaming preserves whatever the
                // user had configured — they can attempt manual recovery
                // by examining the .corrupt file or re-importing via
                // About → Import. Multiple quarantines accumulate with
                // distinct timestamps; nothing on disk is lost.
                try
                {
                    string p = SettingsPath;
                    if (File.Exists(p))
                    {
                        string q = p + ".corrupt-" + DateTime.Now.ToString("yyyyMMdd-HHmmss");
                        File.Move(p, q);
                        CrashLog.Write($"AppSettings: corrupt file moved to {Path.GetFileName(q)} (recovery via About → Import)");
                    }
                }
                catch (Exception qex) { CrashLog.Write("AppSettings quarantine failed", qex); }
                return new AppSettings { LoadedFromDefaults = true, LastLoadError = ex.Message };
            }
        }

        /// <summary>
        /// Schema migrations. Called on load when SchemaVersion &lt; CurrentSchemaVersion.
        ///
        /// <para><b>Retirement policy.</b> Each migration case below should
        /// be deleted once <c>crash.log</c> stops recording the
        /// "Settings migrated vN → vM" entry for that <c>N</c> across
        /// two consecutive releases. The log entry is written by Load()
        /// every time a migration runs, so a quiet log = no users left
        /// on that version = safe to drop the case. Track the policy
        /// here rather than in tickets so future maintainers see it.</para>
        ///
        /// Add new case blocks here when the schema is bumped.
        /// </summary>
        private static void Migrate(AppSettings s)
        {
            // v1 → v2 (ship date: 2026-04). Eligible for retirement once
            // crash.log shows no "Settings migrated v1 → v2" entries for
            // a couple of releases. Re-enable UIA detection: earlier
            // builds turned it off to protect YoloMouse, but the
            // cursor-hook protections (dwell gating, shell region skip,
            // WM_SETCURSOR trap) now make it safe.
            if (s.SchemaVersion < 2)
            {
                s.EnableUIADetection = true;
                if (s.UIAThrottleMs > 200) s.UIAThrottleMs = 150;
            }
        }

        /// <summary>
        /// Replaces NaN/Infinity in any double field with safe sentinel
        /// values before JSON serialisation. <c>JsonSerializer</c>
        /// rejects non-finite numbers by default and the resulting
        /// <c>ArgumentException</c> aborts <see cref="Save"/> — losing
        /// every change in the current write batch and (worse) leaving
        /// a half-written .tmp file. Window/badge position fields use
        /// <c>-1</c> (the readers' "use default" sentinel); scale and
        /// opacity fields use neutral values. Call before every
        /// serialise; cheap (handful of comparisons).
        /// </summary>
        private void SanitiseDoubles()
        {
            static double Fix(double d, double fallback)
                => double.IsNaN(d) || double.IsInfinity(d) ? fallback : d;

            // Window geometry
            WindowWidth   = Fix(WindowWidth,   460);
            WindowHeight  = Fix(WindowHeight, 1080);
            WindowLeft    = Fix(WindowLeft,    -1);
            WindowTop     = Fix(WindowTop,     -1);

            // Badge sliders
            BadgeOpacity  = Fix(BadgeOpacity,  0.95);
            BadgeOffsetX  = Fix(BadgeOffsetX,  20);
            BadgeOffsetY  = Fix(BadgeOffsetY,  20);
            BadgeScale    = Fix(BadgeScale,    1.0);

            // Pinned position (sentinel -1 = use default)
            BadgePinnedX  = Fix(BadgePinnedX,  -1);
            BadgePinnedY  = Fix(BadgePinnedY,  -1);

            // Info overlay
            InfoOverlayFontSize = Fix(InfoOverlayFontSize, 24);
            InfoOverlayLeft     = Fix(InfoOverlayLeft,     -1);
            InfoOverlayTop      = Fix(InfoOverlayTop,      -1);

            // UI scale
            UiScale = Fix(UiScale, 1.0);

            // Idle dim opacity — clamp to displayable range so a
            // user-edited settings.json with 0 or >1 doesn't leave
            // the badge invisible or saturated.
            IdleDimOpacity = Fix(IdleDimOpacity, 0.30);
            if (IdleDimOpacity < 0.05) IdleDimOpacity = 0.05;
            if (IdleDimOpacity > 1.0)  IdleDimOpacity = 1.0;

            // Preset slots — scrub any non-finite doubles in each snapshot.
            if (Presets != null)
            {
                foreach (var p in Presets)
                {
                    if (p == null) continue;
                    p.BadgeOpacity = Fix(p.BadgeOpacity, 0.95);
                    p.BadgeScale   = Fix(p.BadgeScale,   1.0);
                    p.BadgeOffsetX = Fix(p.BadgeOffsetX, 20);
                    p.BadgeOffsetY = Fix(p.BadgeOffsetY, 20);
                    p.UiScale      = Fix(p.UiScale,      1.0);
                }
            }

            // Speech dwell — clamp to [1, 10] s. 0 would be redundant
            // with Immediate mode; values above 10 s are hostile UX.
            if (SpeechDwellSeconds < 1)  SpeechDwellSeconds = 1;
            if (SpeechDwellSeconds > 10) SpeechDwellSeconds = 10;

            // Speech rate — clamp to SAPI range [-10, +10].
            if (SpeechRate < -10) SpeechRate = -10;
            if (SpeechRate >  10) SpeechRate =  10;

            // Badge font size — clamp to 8..72 px. Out-of-band values from a
            // hand-edited settings.json would otherwise leave the badge text
            // microscopic or hostile-huge.
            BadgeFontSize = Fix(BadgeFontSize, 11);
            if (BadgeFontSize < 8)  BadgeFontSize = 8;
            if (BadgeFontSize > 72) BadgeFontSize = 72;
        }

        public bool Save()
        {
            string path = SettingsPath;
            string tmp  = path + ".tmp";
            try
            {
                string dir  = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

                // Defence-in-depth: scrub any non-finite double before
                // we hand the object to the serialiser. NaN/Infinity
                // throws ArgumentException by default and aborts the
                // entire save — corrupting the settings file.
                SanitiseDoubles();

                // Tail-recovery: if a previous Save died between WriteAllText
                // and File.Replace, an orphan .tmp will be sitting on disk.
                // It contains older content than what we're about to write;
                // simply delete it so the new write starts clean. We do NOT
                // try to "rescue" the orphan because we can't know whether it
                // was complete (Replace failure) or truncated (kill mid-write).
                if (File.Exists(tmp))
                {
                    try { File.Delete(tmp); }
                    catch (Exception cleanupEx)
                    {
                        CrashLog.Write("AppSettings.Save orphan .tmp cleanup failed", cleanupEx);
                    }
                }

                // Atomic write: write to .tmp, then File.Replace.
                // Guarantees settings.json is either the old valid content
                // or the new valid content — never a half-written file,
                // even if the process is killed mid-write.
                string backup = path + ".bak";
                string json   = JsonSerializer.Serialize(this, AppSettingsJsonContext.Default.AppSettings);
                File.WriteAllText(tmp, json);

                if (File.Exists(path))
                {
                    File.Replace(tmp, path, backup, ignoreMetadataErrors: true);
                    try { File.Delete(backup); } catch { /* best effort */ }
                }
                else
                {
                    File.Move(tmp, path);
                }
                return true;
            }
            catch (Exception ex)
            {
                CrashLog.Write("AppSettings.Save failed", ex);
                // Best-effort cleanup of the .tmp we created — without this,
                // a save failure leaves an orphan that the next Save would
                // delete (still safe), but cleaning up now keeps the user's
                // %APPDATA% folder tidy and avoids "what is this file"
                // confusion in support situations.
                try { if (File.Exists(tmp)) File.Delete(tmp); }
                catch { /* swallow: already failed once, double-fault would loop */ }
                return false;
            }
        }

        public bool SaveAsDefaults() => Save();

        public static string GetDefaultsPath() => SettingsPath;

        /// <summary>
        /// Publishes this settings instance AND the associated rules to the
        /// detector as a single atomic snapshot. The detector's hot path
        /// then reads (Settings, Rules) as one object — no torn reads.
        /// </summary>
        public void PushToDetector()
        {
            ButtonDetector.Publish(this, CustomRules);
        }
    }
}
