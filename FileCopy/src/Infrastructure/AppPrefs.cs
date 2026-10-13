using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

#nullable enable


namespace FileCopy
{
    [JsonSerializable(typeof(AppPrefs))]
    [JsonSerializable(typeof(PresetData))]
    [JsonSerializable(typeof(List<string>))]
    [JsonSourceGenerationOptions(WriteIndented = true, IncludeFields = false)]
    internal partial class AppPrefsContext : JsonSerializerContext { }

    public sealed record PresetData(
        string SourcePath,
        string DestPath,
        ConflictMode ConflictMode,
        string FileMaskInclude,
        string FileMaskExclude,
        bool   IncludeSubdirs,
        bool   PreserveTimestamps,
        bool   PreserveAttributes,
        bool   VerifyCrc,
        bool   DeleteSourceOnMove,
        int    BufferSizeKb);

    public sealed class AppPrefs : INotifyPropertyChanged
    {
        private static readonly Microsoft.Extensions.Logging.ILogger _log = AppLogger.ForContext<AppPrefs>();

        [JsonIgnore]
        public static AppPrefs Instance { get; } = new AppPrefs();

        private static string Dir      => PortableMode.DataDir;
        private static string FilePath => PortableMode.PrefsPath;

        public event PropertyChangedEventHandler? PropertyChanged;
        private void Notify([CallerMemberName] string name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
            ScheduleSave();
        }

        // ── Debounced background save ─────────────────────────────────────────
        private const int SaveDebounceMs = 500;
        private CancellationTokenSource _saveCts;
        private readonly object _saveLock = new object();
        private bool _autoSaveEnabled = false; // armed after first Load() to avoid save-storm during init

        public void EnableAutoSave() => _autoSaveEnabled = true;

        // ── MRU helpers ───────────────────────────────────────────────────────
        public const int MruMaxItems = 10;

        public static List<string> PushMru(List<string> list, string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return list;
            var result = new List<string>(list ?? new List<string>());
            result.RemoveAll(s => string.Equals(s, path, StringComparison.OrdinalIgnoreCase));
            result.Insert(0, path);
            if (result.Count > MruMaxItems) result.RemoveRange(MruMaxItems, result.Count - MruMaxItems);
            return result;
        }

        private void ScheduleSave()
        {
            if (!_autoSaveEnabled) return;
            CancellationToken token;
            lock (_saveLock)
            {
                var old = _saveCts;
                _saveCts = new CancellationTokenSource();
                token = _saveCts.Token;
                old?.Cancel();
                old?.Dispose();
            }
            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(SaveDebounceMs, token);
                    SaveCore();
                }
                catch (OperationCanceledException) { /* superseded by newer change */ }
                catch (Exception ex) { _log.LogWarning("{Msg}", "AppPrefs.AutoSave: " + ex.Message); }
            });
        }

        // ── Window / UI ───────────────────────────────────────────────────────
        // Geometry + scale persisted per-session; clamped on load.
        public double WindowLeft   { get => field; set { if (field == value) return; field = value; Notify(); } } = double.NaN;
        public double WindowTop    { get => field; set { if (field == value) return; field = value; Notify(); } } = double.NaN;
        public double WindowWidth  { get => field; set { if (field == value) return; field = Math.Max(700, value); Notify(); } } = 980;
        public double WindowHeight { get => field; set { if (field == value) return; field = Math.Max(500, value); Notify(); } } = 760;
        public bool   Maximized    { get => field; set { if (field == value) return; field = value; Notify(); } }
        public string UiScale      { get => field; set { if (field == value) return; field = value ?? "Standard"; Notify(); } } = "Standard";
        public string Theme        { get => field; set { if (field == value) return; field = value ?? "Dark"; Notify(); } } = "Dark";

        // ── Low-Vision: app-wide font family + body size (clamped 8..72) ──────
        public string AppFontFamily { get => field; set { if (field == value) return; field = string.IsNullOrWhiteSpace(value) ? "Segoe UI" : value; Notify(); } } = "Segoe UI";
        public double AppFontSize   { get => field; set { var v = Math.Clamp(value, 8, 72); if (field == v) return; field = v; Notify(); } } = 13;

        // ── Paths ─────────────────────────────────────────────────────────────
        public string SourcePath    { get => field; set { if (field == value) return; field = value ?? ""; Notify(); } } = "";
        public string DestPath      { get => field; set { if (field == value) return; field = value ?? ""; Notify(); } } = "";
        /// <summary>Derived folder roots fed to CopyEngine.Enumerate — either [SourceParent] alone,
        /// or the checked subfolders when SourceCheckedChildren narrows the picker. SourcePath
        /// mirrors SourceList[0] for back-compat with Presets/Scheduler/CLI, which stay single-source.</summary>
        public List<string> SourceList { get => field; set { field = value ?? new List<string>(); Notify(); } } = new List<string>();
        /// <summary>Single parent source directory shown in the inline picker. Counts as the source
        /// on its own when nothing is checked below it.</summary>
        public string SourceParent { get => field; set { if (field == value) return; field = value ?? ""; Notify(); } } = "";
        /// <summary>Full paths of items checked in the inline picker (folders and/or loose files,
        /// direct children of SourceParent). Empty = copy the whole parent.</summary>
        public List<string> SourceCheckedChildren { get => field; set { field = value ?? new List<string>(); Notify(); } } = new List<string>();
        public List<string> RecentSources { get => field; set { field = value ?? new List<string>(); Notify(); } } = new List<string>();
        public List<string> RecentDests   { get => field; set { field = value ?? new List<string>(); Notify(); } } = new List<string>();

        // ── Copy behaviour ────────────────────────────────────────────────────
        public bool         IncludeSubdirs      { get => field; set { if (field == value) return; field = value; Notify(); } } = true;
        public bool         PreserveTimestamps  { get => field; set { if (field == value) return; field = value; Notify(); } } = true;
        public bool         PreserveAttributes  { get => field; set { if (field == value) return; field = value; Notify(); } } = true;
        public bool         VerifyCrc           { get => field; set { if (field == value) return; field = value; Notify(); } }
        public bool         WriteCrcSidecar     { get => field; set { if (field == value) return; field = value; Notify(); } } = false;
        public bool         DeleteSourceOnMove  { get => field; set { if (field == value) return; field = value; Notify(); if (value) VerifyCrc = true; } }
        public bool         ResumePartial       { get => field; set { if (field == value) return; field = value; Notify(); } } = true;
        public ConflictMode ConflictMode        { get => field; set { if (field == value) return; field = value; Notify(); } } = ConflictMode.OverwriteIfNewer;
        public SymlinkPolicy SymlinkPolicy      { get => field; set { if (field == value) return; field = value; Notify(); } } = SymlinkPolicy.Follow;
        public string       FileMaskInclude     { get => field; set { if (field == value) return; field = value ?? "*.*"; Notify(); } } = "*.*";
        public string       FileMaskExclude     { get => field; set { if (field == value) return; field = value ?? ""; Notify(); } } = "";

        // ── Performance ───────────────────────────────────────────────────────
        // All clamped in ClampValues() after Load().
        // BufferSizeKb: 64–65536  |  MaxParallel: 1–32  |  ThrottleMbps: 0–100000
        public int  BufferSizeKb        { get => field; set { if (field == value) return; field = Math.Max(64, Math.Min(16384, value)); Notify(); } } = 1024;
        public int  MaxParallel         { get => field; set { if (field == value) return; field = Math.Max(1, Math.Min(8, value)); Notify(); } } = 1;
        public bool AutoParallelByDrive { get => field; set { if (field == value) return; field = value; Notify(); } } = true;
        public long ThrottleMbps        { get => field; set { if (field == value) return; field = Math.Max(0, value); Notify(); } } = 0;

        // ── Completion behaviour ──────────────────────────────────────────────
        public bool MinimizeToTray      { get => field; set { if (field == value) return; field = value; Notify(); } } = false;
        public bool PlayCompletionSound { get => field; set { if (field == value) return; field = value; Notify(); } } = true;
        public bool OpenDestOnComplete  { get => field; set { if (field == value) return; field = value; Notify(); } } = false;
        public bool PauseOnBattery      { get => field; set { if (field == value) return; field = value; Notify(); } } = false;

        // ── File grid state ───────────────────────────────────────────────────
        public string   FileGridFilter        { get => field; set { if (field == value) return; field = value; Notify(); } } = "All";
        public string   FileGridSortColumn    { get => field; set { if (field == value) return; field = value; Notify(); } } = "";
        public string   FileGridSortDirection { get => field; set { if (field == value) return; field = value; Notify(); } } = "Ascending";
        public double[] FileGridColumnWidths  { get => field; set { field = value ?? Array.Empty<double>(); Notify(); } } = Array.Empty<double>();

        // ── Presets ───────────────────────────────────────────────────────────
        public PresetData[] Presets { get => field; set { field = value ?? new PresetData[5]; Notify(); } } = new PresetData[5];

        // ── Persistence ───────────────────────────────────────────────────────
        // FC-06: serialize on the UI thread (properties are UI-owned); one writer at a time.
        private readonly object _ioLock = new();

        public void Save()
        {
            string? json = Snapshot();
            if (json != null) WriteJson(json);
        }

        private void SaveCore() => Save();

        /// <summary>Serializes on the UI thread (marshals with a 2 s cap when called off-thread).</summary>
        internal string? Snapshot()
        {
            try
            {
                var d = System.Windows.Application.Current?.Dispatcher;
                if (d == null || d.CheckAccess() || d.HasShutdownStarted)
                    return JsonSerializer.Serialize(this, AppPrefsContext.Default.AppPrefs);
                return d.Invoke(() => JsonSerializer.Serialize(this, AppPrefsContext.Default.AppPrefs),
                                System.Windows.Threading.DispatcherPriority.Send,
                                CancellationToken.None, TimeSpan.FromSeconds(2));
            }
            catch (Exception ex) { _log.LogWarning("{Msg}", "AppPrefs.Snapshot: " + ex.Message); return null; }
        }

        /// <summary>Atomic tmp + replace write; serialized so the debounce and exit saves never share the .tmp.</summary>
        internal void WriteJson(string json)
        {
            lock (_ioLock)
            {
                try
                {
                    Directory.CreateDirectory(Dir);
                    string tmpPath = FilePath + ".tmp";
                    File.WriteAllText(tmpPath, json);
                    File.Move(tmpPath, FilePath, overwrite: true);
                }
                catch (Exception ex) { _log.LogWarning("{Msg}", "AppPrefs.Save: " + ex.Message); }
            }
        }

        public void Load()
        {
            try
            {
                if (!File.Exists(FilePath)) return;
                string json = File.ReadAllText(FilePath);
                var loaded = JsonSerializer.Deserialize(json, AppPrefsContext.Default.AppPrefs);
                if (loaded != null) CopyFrom(loaded);
                ClampValues();
            }
            catch (Exception ex) { _log.LogWarning("{Msg}", "AppPrefs.Load: " + ex.Message); }
        }

        private void ClampValues()
        {
            BufferSizeKb = Math.Clamp(BufferSizeKb, 64, 65536);
            MaxParallel  = Math.Clamp(MaxParallel,   1, 32);
            ThrottleMbps = Math.Clamp(ThrottleMbps,  0, 100_000);
            UiScale      = UiScale is "Compact" or "Standard" or "Medium" or "Large" or "Xtra Large"
                           ? UiScale : "Standard";
            ConflictMode = Enum.IsDefined(ConflictMode) ? ConflictMode : ConflictMode.OverwriteIfNewer;
            AppFontFamily = string.IsNullOrWhiteSpace(AppFontFamily) ? "Segoe UI" : AppFontFamily;
            AppFontSize   = Math.Clamp(AppFontSize, 8, 72);
        }

        private void CopyFrom(AppPrefs s)
        {
            WindowLeft = s.WindowLeft; WindowTop = s.WindowTop;
            WindowWidth = s.WindowWidth; WindowHeight = s.WindowHeight;
            Maximized = s.Maximized; UiScale = s.UiScale;
            SourcePath = s.SourcePath; DestPath = s.DestPath;
            SourceList = new List<string>(s.SourceList ?? new List<string>());
            // Migrate pre-multi-source data: seed list from the legacy single SourcePath.
            if (SourceList.Count == 0 && !string.IsNullOrWhiteSpace(SourcePath))
                SourceList = new List<string> { SourcePath };
            SourceParent = s.SourceParent ?? "";
            SourceCheckedChildren = new List<string>(s.SourceCheckedChildren ?? new List<string>());
            // Migrate pre-picker data: derive a parent directory from the legacy multi-root list.
            if (string.IsNullOrWhiteSpace(SourceParent) && SourceList.Count > 0)
            {
                SourceParent = SourceList.Count == 1
                    ? SourceList[0]
                    : (Path.GetDirectoryName(SourceList[0]) ?? SourceList[0]);
                if (SourceList.Count > 1) SourceCheckedChildren = new List<string>(SourceList);
            }
            IncludeSubdirs = s.IncludeSubdirs;
            PreserveTimestamps = s.PreserveTimestamps;
            PreserveAttributes = s.PreserveAttributes;
            VerifyCrc = s.VerifyCrc;
            DeleteSourceOnMove = s.DeleteSourceOnMove;
            ConflictMode = s.ConflictMode;
            SymlinkPolicy = Enum.IsDefined(s.SymlinkPolicy) ? s.SymlinkPolicy : SymlinkPolicy.Follow;
            FileMaskInclude = s.FileMaskInclude;
            FileMaskExclude = s.FileMaskExclude;
            BufferSizeKb = s.BufferSizeKb;
            MaxParallel = s.MaxParallel;
            ResumePartial = s.ResumePartial;
            AutoParallelByDrive = s.AutoParallelByDrive;
            MinimizeToTray = s.MinimizeToTray;
            PlayCompletionSound = s.PlayCompletionSound;
            ThrottleMbps = s.ThrottleMbps;
            OpenDestOnComplete = s.OpenDestOnComplete;
            FileGridFilter      = s.FileGridFilter;
            FileGridSortColumn    = s.FileGridSortColumn;
            FileGridSortDirection = s.FileGridSortDirection;
            RecentSources = new List<string>(s.RecentSources ?? new List<string>());
            RecentDests   = new List<string>(s.RecentDests   ?? new List<string>());
            Theme = AppTheme.ValidKeys.Contains(s.Theme) ? s.Theme : "Dark";
            WriteCrcSidecar = s.WriteCrcSidecar;
            PauseOnBattery  = s.PauseOnBattery;
            AppFontFamily   = s.AppFontFamily;
            AppFontSize     = s.AppFontSize;
            var arr = s.Presets ?? new PresetData[5];
            if (arr.Length != 5)
            {
                var fixedArr = new PresetData[5];
                Array.Copy(arr, fixedArr, Math.Min(5, arr.Length));
                arr = fixedArr;
            }
            Presets = arr;
            FileGridColumnWidths = s.FileGridColumnWidths ?? Array.Empty<double>();
        }
    }
}
