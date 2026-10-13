using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TimerStudio
{
    /// <summary>
    /// Records every timer alert fire event to a rolling log.
    /// Stored in %AppData%\TimerStudio\history.json, capped at 200 entries.
    /// </summary>
    public sealed class TimerHistoryEntry
    {
        public int    TimerId   { get; set; }
        public string TimerName { get; set; } = "";
        public string FiredAt   { get; set; } = "";   // ISO-8601 UTC
        public string Notes     { get; set; } = "";   // trimmed to 200 chars
    }

    [JsonSerializable(typeof(List<TimerHistoryEntry>))]
    [JsonSourceGenerationOptions(WriteIndented = true,
        PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
        AllowTrailingCommas  = true)]
    internal partial class HistoryJsonContext : JsonSerializerContext { }

    public static class TimerHistory
    {
        private const int MaxEntries = 200;

        private static readonly string Dir  =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                         "TimerStudio");
        private static readonly string File = Path.Combine(Dir, "history.json");

        private static readonly object _lock = new object();

        // In-memory cache — loaded once on first access, kept in sync on every write.
        // Eliminates the full disk-read that previously happened on every Record() call.
        private static List<TimerHistoryEntry> _cache;

        /// <summary>Records a timer-fired event. Thread-safe.</summary>
        public static void Record(int timerId, string timerName, DateTime firedAt, string notes)
        {
            try
            {
                lock (_lock)
                {
                    var entries = Load();
                    notes = notes?.Length > 200 ? notes[..200] : (notes ?? "");
                    entries.Add(new TimerHistoryEntry
                    {
                        TimerId   = timerId,
                        TimerName = timerName ?? "",
                        FiredAt   = firedAt.ToUniversalTime().ToString("o"),
                        Notes     = notes
                    });
                    // Keep newest MaxEntries, drop oldest
                    if (entries.Count > MaxEntries)
                        entries.RemoveRange(0, entries.Count - MaxEntries);
                    Save(entries);
                }
            }
            catch (Exception ex)
            {
                DiagLog.WriteException("TimerHistory.Record", ex);
            }
        }

        /// <summary>Returns a snapshot of all history entries, newest last.</summary>
        public static List<TimerHistoryEntry> GetAll()
        {
            // H-9: return a defensive copy — callers that sort/filter/clear must not mutate _cache.
            try { lock (_lock) { return new List<TimerHistoryEntry>(Load()); } }
            catch { return new List<TimerHistoryEntry>(); }
        }

        /// <summary>Deletes all history entries. Called by StatsDashboard on user request.</summary>
        public static void Clear()
        {
            try
            {
                lock (_lock)
                {
                    Save(new List<TimerHistoryEntry>());
                }
            }
            catch (Exception ex) { DiagLog.WriteException("TimerHistory.Clear", ex); }
        }

        // ── Private helpers ───────────────────────────────────────────────────

        private static List<TimerHistoryEntry> Load()
        {
            if (_cache != null) return _cache;
            if (!System.IO.File.Exists(File)) return _cache = new List<TimerHistoryEntry>();
            try
            {
                string json = System.IO.File.ReadAllText(File, Encoding.UTF8);
                _cache = JsonSerializer.Deserialize(json,
                    HistoryJsonContext.Default.ListTimerHistoryEntry)
                    ?? new List<TimerHistoryEntry>();
                return _cache;
            }
            catch { return _cache = new List<TimerHistoryEntry>(); }
        }

        private static void Save(List<TimerHistoryEntry> entries)
        {
            _cache = entries;   // keep cache warm
            Directory.CreateDirectory(Dir);
            string json = JsonSerializer.Serialize(entries,
                HistoryJsonContext.Default.ListTimerHistoryEntry);
            string tmp = File + ".tmp";
            System.IO.File.WriteAllText(tmp, json, Encoding.UTF8);
            System.IO.File.Copy(tmp, File, overwrite: true);
            System.IO.File.Delete(tmp);
        }
    }
}
