using System;
using System.Collections.Generic;
using System.IO;

namespace BadgeOverlay
{
    /// <summary>
    /// Appends exceptions to %APPDATA%\BadgeOverlay\crash.log.
    /// Self-contained — never throws. If the log itself fails, the attempt
    /// is silently dropped so crash logging cannot become the cause of
    /// another crash.
    ///
    /// Rate-limit / dedup:
    ///   • Identical (context + exception-type) entries coalesce into a
    ///     single line with a repeat counter when they occur within the
    ///     RepeatWindow.
    ///   • At most <see cref="MaxWritesPerMinute"/> unique entries are
    ///     written per 60s sliding window — prevents a pathological loop
    ///     (e.g. broken UIA provider on every tick) from filling the disk.
    /// </summary>
    internal static class CrashLog
    {
        private const long MaxSizeBytes       = UiConstants.CrashLogMaxBytes;          // 512 KB rolling cap
        private const int  MaxWritesPerMinute = UiConstants.CrashLogMaxWritesPerMinute; // global rate ceiling
        private static readonly TimeSpan RepeatWindow = TimeSpan.FromSeconds(10);

        private static readonly object _gate = new object();
        private static readonly Dictionary<string, RepeatState> _recent =
            new Dictionary<string, RepeatState>(StringComparer.Ordinal);
        private static readonly Queue<DateTime> _writeTimestamps = new Queue<DateTime>();

        private sealed class RepeatState
        {
            public DateTime FirstSeen;
            public DateTime LastSeen;
            public int      Count;
        }

        private static string LogPath =>
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "BadgeOverlay", "crash.log");

        /// <summary>
        /// Replaces the current user's profile path with "%USERPROFILE%" so
        /// crash-log entries (and anything derived from them, e.g. a pasted
        /// bug report) never carry the Windows account name. Non-matching
        /// strings are returned unchanged. Centralized here — every
        /// <see cref="Write(string, Exception)"/> call goes through this, so
        /// no call site can bypass redaction by forgetting to apply it.
        /// </summary>
        public static string Redact(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            try
            {
                string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                if (!string.IsNullOrEmpty(profile))
                    s = s.Replace(profile, "%USERPROFILE%", StringComparison.OrdinalIgnoreCase);
            }
            catch { /* best-effort — never let redaction itself throw */ }
            return s;
        }

        public static void Write(string context, Exception ex)
        {
            try
            {
                string key = (context ?? "") + "|" + (ex?.GetType().FullName ?? "null");
                if (!AcquireWriteSlot(key, out int repeatCount)) return;

                string path = LogPath;
                string dir  = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

                // Rotate if oversized. Two-generation rolling: live log
                // → .old → deleted. Total disk footprint capped at
                // 2 × MaxSizeBytes (current 1 MB). If something on the
                // system is generating crash entries fast enough to
                // overflow .old before we've rotated again, that itself
                // is a bug — but at least the disk doesn't fill.
                try
                {
                    var fi = new FileInfo(path);
                    if (fi.Exists)
                    {
                        bool tooLarge = fi.Length > MaxSizeBytes;
                        // Age-based rotation: if the log file is older
                        // than 30 days, archive it regardless of size.
                        // Most users will never fill 512 KB in 30 days,
                        // but old logs accumulate over months and are
                        // rarely useful after a month.
                        bool tooOld = (DateTime.UtcNow - fi.LastWriteTimeUtc).TotalDays > 30;

                        if (tooLarge || tooOld)
                        {
                            string oldPath = path + ".old";
                            try
                            {
                                var oldFi = new FileInfo(oldPath);
                                if (oldFi.Exists && oldFi.Length > MaxSizeBytes * 2)
                                    oldFi.Delete();
                            }
                            catch { /* swallow: best-effort cleanup */ }
                            File.Move(path, oldPath, overwrite: true);
                        }
                    }
                }
                catch { /* swallow: rotation failure should never block logging */ }

                string repeatSuffix = repeatCount > 1 ? $" (×{repeatCount})" : "";
                // Redact before formatting: ex.ToString() (message + stack
                // trace) routinely embeds full file paths under %APPDATA%,
                // which contain the Windows account name — and this log is
                // explicitly offered up for users to paste into public
                // GitHub issues via the Report Problem flow.
                string entry =
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {Redact(context)}{repeatSuffix}{Environment.NewLine}" +
                    $"{Redact(ex?.ToString())}{Environment.NewLine}" +
                    $"{new string('-', 72)}{Environment.NewLine}";

                // Gate the actual file I/O. CrashLog.Write is called from
                // UI, worker, session-switch (thread-pool), and hotkey
                // threads — concurrent AppendAllText can corrupt entries.
                lock (_gate)
                {
                    File.AppendAllText(path, entry);
                }
            }
            catch { /* never throw from logger */ }
        }

        public static void Write(string message)
        {
            try
            {
                string key = "msg|" + (message ?? "");
                if (!AcquireWriteSlot(key, out int repeatCount)) return;

                string path = LogPath;
                string dir  = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

                string repeatSuffix = repeatCount > 1 ? $" (×{repeatCount})" : "";
                lock (_gate)
                {
                    File.AppendAllText(path,
                        $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {Redact(message)}{repeatSuffix}{Environment.NewLine}");
                }
            }
            catch { }
        }

        /// <summary>
        /// Decides whether this call should actually write to disk.
        /// Returns false when the key repeats within RepeatWindow (coalesces)
        /// or when the global per-minute ceiling has been reached.
        /// When returning true, repeatCount indicates how many suppressed
        /// occurrences preceded this write (1 for a single occurrence).
        /// </summary>
        private static bool AcquireWriteSlot(string key, out int repeatCount)
        {
            repeatCount = 1;
            lock (_gate)
            {
                DateTime now = DateTime.UtcNow;

                // Global per-minute ceiling
                while (_writeTimestamps.Count > 0 &&
                       now - _writeTimestamps.Peek() > TimeSpan.FromMinutes(1))
                    _writeTimestamps.Dequeue();
                if (_writeTimestamps.Count >= MaxWritesPerMinute) return false;

                // Dedup window
                if (_recent.TryGetValue(key, out var state))
                {
                    if (now - state.LastSeen <= RepeatWindow)
                    {
                        state.LastSeen = now;
                        state.Count++;
                        return false;   // coalesced — do not write
                    }
                    // Window elapsed — emit with accumulated count, then reset
                    repeatCount = state.Count;
                    state.FirstSeen = now;
                    state.LastSeen  = now;
                    state.Count     = 1;
                }
                else
                {
                    _recent[key] = new RepeatState
                        { FirstSeen = now, LastSeen = now, Count = 1 };
                }

                // GC old entries to prevent unbounded growth
                if (_recent.Count > 128)
                {
                    var cutoff = now - TimeSpan.FromMinutes(5);
                    var keys = new List<string>();
                    foreach (var kv in _recent)
                        if (kv.Value.LastSeen < cutoff) keys.Add(kv.Key);
                    foreach (var k in keys) _recent.Remove(k);
                }

                _writeTimestamps.Enqueue(now);
                return true;
            }
        }
    }
}
