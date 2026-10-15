using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;

namespace TimerStudio
{
    /// <summary>
    /// Saves and loads timer configurations to %AppData%\TimerStudio\timers.json.
    /// Uses System.Text.Json source-generated serialisation — zero reflection at runtime.
    /// Atomic write (tmp => copy => delete) prevents corruption on crash.
    /// </summary>
    public static class TimerPersistence
    {
        private static readonly string SaveDir =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                         "TimerStudio");

        private static readonly string SaveFile = Path.Combine(SaveDir, "timers.json");

        public static string SaveFilePath => SaveFile;

        /// <summary>
        /// UTC last-write-time of timers.json as of our own most recent successful
        /// save/load. Used to detect an external writer (e.g. NoteStation's push
        /// integration) touching the file between our periodic autosaves, so we
        /// don't blind-overwrite their change. See ExternalChangeDetected().
        /// </summary>
        public static DateTime LastKnownWriteUtc { get; private set; } = DateTime.MinValue;

        private static void RecordWriteStamp()
        {
            try { LastKnownWriteUtc = File.GetLastWriteTimeUtc(SaveFile); }
            catch { /* best-effort */ }
        }

        /// <summary>
        /// True if timers.json's on-disk write time has moved past what we last
        /// recorded ourselves — i.e. something else (NoteStation's push, manual
        /// edit) wrote to it since our last save/load.
        /// </summary>
        public static bool ExternalChangeDetected()
        {
            try
            {
                if (!File.Exists(SaveFile)) return false;
                return File.GetLastWriteTimeUtc(SaveFile) > LastKnownWriteUtc;
            }
            catch { return false; }
        }

        // ── Self-test ─────────────────────────────────────────────────────────
        public static bool SelfTest()
        {
            try
            {
                var orig = new TimerModel();
                orig.OverrideId(-1);
                orig.Name          = "Testé";
                orig.Hours         = 1;
                orig.Minutes       = 23;
                orig.Seconds       = 45;
                orig.AlertFontSize = 36;
                orig.AutoReset     = true;

                var dto  = DtoMapper.ToDto(orig);
                var json = DtoMapper.SerializeTimers(new[] { dto });
                var list = DtoMapper.DeserializeTimers(json);
                if (list.Count != 1) return false;
                var r = DtoMapper.FromDto(list[0]);
                return r.Name == orig.Name && r.Hours == 1 && r.Minutes == 23
                    && r.AutoReset && r.AlertFontSize == 36;
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[TimerStudio] SelfTest failed: " + ex.Message);
                return false;
            }
        }

        // ── Shared IPC lock (2026-09-07: real lock for the NoteStation push contract,
        // replacing the timestamp-only ExternalChangeDetected() heuristic above) ──
        // TS-02: never block the UI thread for long — callers pick the timeout.
        private static readonly TimeSpan SaveLockTimeout     = TimeSpan.FromMilliseconds(250);
        private static readonly TimeSpan ShutdownLockTimeout = TimeSpan.FromSeconds(5);
        private static readonly TimeSpan LoadLockTimeout     = TimeSpan.FromSeconds(1);

        private static Mutex AcquireIpcLock(TimeSpan timeout, out bool acquired)
        {
            var mutex = new Mutex(false, TimerStudioIpc.TimersJsonMutexName);
            try { acquired = mutex.WaitOne(timeout); }
            catch (AbandonedMutexException) { acquired = true; }
            return mutex;
        }

        // ── Save ─────────────────────────────────────────────────────────────
        // TS-01: no async variant — the IPC Mutex is thread-affine and must be
        // released on the thread that acquired it, so it can't span an await.
        // TS-02: returns false (nothing written) when another process holds the
        // lock; caller retries. shutdown=true waits longer, then writes anyway so
        // in-memory state isn't lost on exit.
        // GDPR note: logs only exception type/message — no timer names or paths.
        public static bool Save(IEnumerable<TimerModel> models, bool shutdown = false)
        {
            using var ipcLock = AcquireIpcLock(shutdown ? ShutdownLockTimeout : SaveLockTimeout, out bool haveLock);
            if (!haveLock && !shutdown) return false;
            if (!haveLock)
                AppLog.Info("TimerPersistence.Save: IPC lock busy at shutdown — writing without lock");
            try
            {
                Directory.CreateDirectory(SaveDir);
                var dtos = new List<TimerDto>();
                foreach (var m in models) dtos.Add(DtoMapper.ToDto(m));

                string json = DtoMapper.SerializeTimers(dtos);
                string tmp  = SaveFile + ".tmp";
                File.WriteAllText(tmp, json, Encoding.UTF8);
                // L-2 (2026-09-07): File.Move is a true atomic rename; the previous
                // File.Copy+File.Delete pair had its own crash window (same class of
                // bug fixed in NoteStation's DataStore.WriteAtomic).
                File.Move(tmp, SaveFile, overwrite: true);
                RecordWriteStamp();
            }
            catch (Exception ex) { AppLog.Info("TimerPersistence.Save failed: " + ex.GetType().Name); }
            finally
            {
                if (haveLock) ipcLock.ReleaseMutex();
            }
            return true;
        }

        // ── Load (synchronous — only safe to call before UI is shown) ─────────
        public static List<TimerModel> Load()
        {
            var list = new List<TimerModel>();
            if (!File.Exists(SaveFile)) return list;
            using var ipcLock = AcquireIpcLock(LoadLockTimeout, out bool haveLock);
            try
            {
                string json = File.ReadAllText(SaveFile, Encoding.UTF8);
                var dtos = DtoMapper.DeserializeTimers(json);
                if (dtos.Count == 0 && json.TrimStart().StartsWith("["))
                    dtos = LegacyFallbackLoad(json);
                foreach (var d in dtos) list.Add(DtoMapper.FromDto(d));
                RecordWriteStamp();
            }
            catch (Exception ex) { Debug.WriteLine("[TimerStudio] Load failed: " + ex.GetType().Name); }
            finally
            {
                if (haveLock) ipcLock.ReleaseMutex();
            }
            return list;
        }

        // ── Legacy fallback (PascalCase hand-rolled JSON from pre-2026.4) ─────
        private static List<TimerDto> LegacyFallbackLoad(string json)
        {
            var result = new List<TimerDto>();
            try
            {
                foreach (var obj in SplitObjects(json))
                {
                    result.Add(new TimerDto
                    {
                        Id                      = ReadInt(obj, "Id", 0),
                        Name                    = ReadStr(obj, "Name") ?? "",
                        Mode                    = ReadInt(obj, "Mode", 0),
                        Hours                   = ReadInt(obj, "Hours", 0),
                        Minutes                 = ReadInt(obj, "Minutes", 5),
                        Seconds                 = ReadInt(obj, "Seconds", 0),
                        TargetDateTime          = ReadStr(obj, "TargetDateTime") ?? "",
                        ReminderEnabled         = ReadBool(obj, "ReminderEnabled", false),
                        ReminderLeadMinutes     = ReadInt(obj, "ReminderLeadMinutes", 60),
                        ReminderIntervalMinutes = ReadInt(obj, "ReminderIntervalMinutes", 0),
                        AlertColor              = ReadStr(obj, "AlertColor") ?? "",
                        AlertTitleColor         = ReadStr(obj, "AlertTitleColor") ?? "",
                        AlertSound              = ReadInt(obj, "AlertSound", 0),
                        AlertStyle              = ReadInt(obj, "AlertStyle", 0),
                        FlashSeconds            = ReadInt(obj, "FlashSeconds", 5),
                        AutoReset               = ReadBool(obj, "AutoReset", false),
                        AlertFontSize           = ReadInt(obj, "AlertFontSize", 22),
                        LensPosition            = ReadInt(obj, "LensPosition", 8),
                        StopwatchElapsedMs      = ReadLong(obj, "StopwatchElapsedMs", 0),
                        Collapsed               = ReadBool(obj, "Collapsed", false),
                        SettingsExpanded        = ReadBool(obj, "SettingsExpanded", false),
                        NotesExpanded           = ReadBool(obj, "NotesExpanded", false),
                        Notes                   = ReadStr(obj, "Notes") ?? "",
                        Category                = ReadStr(obj, "Category") ?? "",
                        OverrideCategoryColor   = ReadBool(obj, "OverrideCategoryColor", false),
                        ShowInOverlay           = ReadBool(obj, "ShowInOverlay", false),
                        WasRunning              = ReadBool(obj, "WasRunning", false),
                        CustomSoundPath         = ReadStr(obj,  "CustomSoundPath") ?? "",
                        CardTint                = ReadStr(obj,  "CardTint")        ?? "",
                        SnoozeDurationMinutes   = ReadInt(obj,  "SnoozeDurationMinutes", 5)
                    });
                }
            }
            catch (Exception ex) { Debug.WriteLine("[TimerStudio] Legacy load failed: " + ex.Message); }
            return result;
        }

        private static List<string> SplitObjects(string json)
        {
            var result = new List<string>();
            int depth = 0, start = -1;
            for (int i = 0; i < json.Length; i++)
            {
                char c = json[i];
                if      (c == '{') { if (depth++ == 0) start = i; }
                else if (c == '}') { if (--depth == 0 && start >= 0) result.Add(json.Substring(start, i - start + 1)); }
            }
            return result;
        }

        private static string ReadStr(string obj, string key)
        {
            string pattern = "\"" + key + "\"";
            int ki = obj.IndexOf(pattern, StringComparison.OrdinalIgnoreCase);
            if (ki < 0) return null;
            int colon = obj.IndexOf(':', ki + pattern.Length);
            if (colon < 0) return null;
            int q1 = obj.IndexOf('"', colon + 1);
            if (q1 < 0) return null;
            int q2 = q1 + 1;
            while (q2 < obj.Length) { if (obj[q2] == '\\') { q2 += 2; continue; } if (obj[q2] == '"') break; q2++; }
            return obj.Substring(q1 + 1, q2 - q1 - 1)
                      .Replace("\\\"", "\"").Replace("\\\\", "\\")
                      .Replace("\\r", "\r").Replace("\\n", "\n");
        }

        private static int ReadInt(string obj, string key, int def)
        {
            long v = ReadLong(obj, key, def);
            return (int)Math.Max(int.MinValue, Math.Min(int.MaxValue, v));
        }

        private static long ReadLong(string obj, string key, long def)
        {
            string pattern = "\"" + key + "\"";
            int ki = obj.IndexOf(pattern, StringComparison.OrdinalIgnoreCase);
            if (ki < 0) return def;
            int colon = obj.IndexOf(':', ki + pattern.Length);
            if (colon < 0) return def;
            int i = colon + 1;
            while (i < obj.Length && (obj[i] == ' ' || obj[i] == '\t' || obj[i] == '\r' || obj[i] == '\n')) i++;
            int j = i;
            if (j < obj.Length && (obj[j] == '-' || obj[j] == '+')) j++;
            while (j < obj.Length && char.IsDigit(obj[j])) j++;
            return (j > i && long.TryParse(obj.Substring(i, j - i), out long v)) ? v : def;
        }

        private static bool ReadBool(string obj, string key, bool def)
        {
            string pattern = "\"" + key + "\"";
            int ki = obj.IndexOf(pattern, StringComparison.OrdinalIgnoreCase);
            if (ki < 0) return def;
            int colon = obj.IndexOf(':', ki + pattern.Length);
            if (colon < 0) return def;
            int i = colon + 1;
            while (i < obj.Length && obj[i] == ' ') i++;
            if (i + 4 <= obj.Length && obj.Substring(i, 4) == "true")  return true;
            if (i + 5 <= obj.Length && obj.Substring(i, 5) == "false") return false;
            return def;
        }
    }
}
