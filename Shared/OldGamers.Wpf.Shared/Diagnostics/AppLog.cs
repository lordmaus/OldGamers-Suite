using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace OldGamers.Wpf.Shared
{
    public enum AppLogLevel { Debug = 0, Info = 1, Warn = 2, Error = 3 }

    public sealed class AppLogOptions
    {
        /// <summary>Entries below this level are dropped. Default Info (Debug only when asked).</summary>
        public AppLogLevel MinimumLevel { get; set; } = AppLogLevel.Info;
        /// <summary>Single-file mode: rotate to "&lt;file&gt;.old" above this size.</summary>
        public long MaxBytes { get; set; } = 512 * 1024;
        /// <summary>Single-file mode: rotate when last written longer ago than this. Daily mode: delete older files.</summary>
        public int MaxAgeDays { get; set; } = 30;
        /// <summary>Replace e-mail addresses with &lt;redacted&gt; (TimerStudio: SMTP errors carry recipients).</summary>
        public bool RedactEmails { get; set; }
        /// <summary>Identical entries within this window collapse into one line with a "(×N)" count.</summary>
        public TimeSpan RepeatWindow { get; set; } = TimeSpan.FromSeconds(10);
        /// <summary>Hard ceiling on written entries per minute (log-flood guard).</summary>
        public int MaxEntriesPerMinute { get; set; } = 300;
    }

    /// <summary>
    /// Suite log (S-07, 2026-10-14) — replaces BO/RO/SML CrashLog, NS AppLog + crash.log, TS DiagLog and
    /// the FileCopy rolling file provider. One file per app: <c>&lt;data dir&gt;\logs\&lt;App&gt;.log</c>
    /// (FileCopy: one file per day for its log viewer).
    ///
    /// Never throws. Thread-safe. Before <see cref="Configure"/> entries are buffered (max 200) and
    /// flushed on configure, so nothing logged during startup is lost. Every entry is redacted
    /// (profile path → %USERPROFILE%, optionally e-mail addresses), single-line for the message part
    /// (no log-line injection), and rate limited.
    /// </summary>
    public static class AppLog
    {
        private const int PendingCap = 200;
        private static readonly object _gate = new();
        private static readonly List<string> _pending = new();
        private static readonly Dictionary<string, Repeat> _recent = new(StringComparer.Ordinal);
        private static readonly Queue<DateTime> _written = new();
        private static readonly Regex EmailPattern =
            new(@"[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}", RegexOptions.Compiled | RegexOptions.CultureInvariant);

        private static AppLogOptions _options = new();
        private static string _filePath;          // single-file mode
        private static string _dailyDir;          // daily mode
        private static string _dailyPrefix;
        private static int _dropped;
        private static string _profile;

        private sealed class Repeat { public DateTime Last; public int Count; }

        // ── Configuration ─────────────────────────────────────────────────────

        /// <summary>Single-file log: <paramref name="dataDir"/>\logs\<paramref name="appName"/>.log (+ .old).</summary>
        public static void Configure(string dataDir, string appName, AppLogOptions options = null) =>
            ConfigureFile(Path.Combine(dataDir, "logs", appName + ".log"), options);

        /// <summary>Single-file log at an explicit path (tests, special cases).</summary>
        public static void ConfigureFile(string filePath, AppLogOptions options = null)
        {
            lock (_gate)
            {
                _options = options ?? new AppLogOptions();
                _filePath = Path.GetFullPath(filePath);
                _dailyDir = _dailyPrefix = null;
                FlushPendingLocked();
            }
        }

        /// <summary>One file per local day: <paramref name="logsDir"/>\<paramref name="prefix"/>-yyyy-MM-dd.log.
        /// Files older than <see cref="AppLogOptions.MaxAgeDays"/> are deleted now.</summary>
        public static void ConfigureDaily(string logsDir, string prefix, AppLogOptions options = null)
        {
            lock (_gate)
            {
                _options = options ?? new AppLogOptions();
                _dailyDir = Path.GetFullPath(logsDir);
                _dailyPrefix = prefix;
                _filePath = null;
                PruneDailyLocked();
                FlushPendingLocked();
            }
        }

        /// <summary>Current log file (today's file in daily mode); null before Configure.</summary>
        public static string FilePath
        {
            get
            {
                lock (_gate) return CurrentPathLocked();
            }
        }

        /// <summary>Folder holding the log; null before Configure.</summary>
        public static string LogDirectory
        {
            get
            {
                string p = FilePath;
                return p == null ? null : Path.GetDirectoryName(p);
            }
        }

        /// <summary>Test hook: forget configuration, buffers and rate-limit state.</summary>
        public static void ResetForTests()
        {
            lock (_gate)
            {
                _options = new AppLogOptions();
                _filePath = _dailyDir = _dailyPrefix = null;
                _pending.Clear(); _recent.Clear(); _written.Clear();
                _dropped = 0;
            }
        }

        // ── Writing ───────────────────────────────────────────────────────────

        public static void Debug(string message) => Write(AppLogLevel.Debug, null, message, null);
        public static void Info(string message) => Write(AppLogLevel.Info, null, message, null);
        public static void Warn(string message) => Write(AppLogLevel.Warn, null, message, null);
        public static void Warn(string context, Exception ex) => Write(AppLogLevel.Warn, null, context, ex);
        public static void Error(string message) => Write(AppLogLevel.Error, null, message, null);
        public static void Error(string context, Exception ex) => Write(AppLogLevel.Error, null, context, ex);

        /// <summary>Full entry point (FileCopy ILogger bridge passes its category).</summary>
        public static void Write(AppLogLevel level, string category, string message, Exception ex)
        {
            try
            {
                if (level < _options.MinimumLevel) return;
                string msg = OneLine(Redact(message ?? ""));
                string key = $"{(int)level}|{category}|{msg}|{ex?.GetType().FullName}";

                lock (_gate)
                {
                    if (!AcquireSlotLocked(key, out int repeats)) return;

                    var sb = new StringBuilder(256);
                    sb.Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture))
                      .Append(" [").Append(LevelName(level)).Append("] ");
                    if (!string.IsNullOrEmpty(category)) sb.Append('[').Append(category).Append("] ");
                    sb.Append(msg);
                    if (repeats > 1) sb.Append(" (×").Append(repeats).Append(')');
                    if (_dropped > 0)
                    {
                        sb.Append(" [").Append(_dropped).Append(" earlier entries dropped by rate limit]");
                        _dropped = 0;
                    }
                    if (ex != null)
                        sb.Append(Environment.NewLine).Append(Indent(Redact(ex.ToString())));

                    string entry = sb.ToString();
                    System.Diagnostics.Debug.WriteLine(entry);

                    string path = CurrentPathLocked();
                    if (path == null)
                    {
                        if (_pending.Count < PendingCap) _pending.Add(entry);
                        return;
                    }
                    AppendLocked(path, entry);
                }
            }
            catch { /* logging must never throw */ }
        }

        // ── Redaction ─────────────────────────────────────────────────────────

        /// <summary>Profile path → %USERPROFILE% (keeps the Windows account name out of logs that users
        /// paste into public issues); e-mail addresses when <see cref="AppLogOptions.RedactEmails"/>.</summary>
        public static string Redact(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            try
            {
                _profile ??= Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) ?? "";
                if (_profile.Length > 3)
                    s = s.Replace(_profile, "%USERPROFILE%", StringComparison.OrdinalIgnoreCase);
                if (_options.RedactEmails)
                    s = EmailPattern.Replace(s, "<redacted>");
            }
            catch { /* best-effort */ }
            return s;
        }

        // ── Internals ─────────────────────────────────────────────────────────

        private static string LevelName(AppLogLevel l) => l switch
        {
            AppLogLevel.Debug => "DEBUG",
            AppLogLevel.Info  => "INFO ",
            AppLogLevel.Warn  => "WARN ",
            _                 => "ERROR",
        };

        private static string OneLine(string s) =>
            s.IndexOfAny(new[] { '\r', '\n' }) < 0 ? s : s.Replace("\r\n", " ").Replace('\r', ' ').Replace('\n', ' ');

        private static string Indent(string s) => "    " + s.Replace("\r\n", "\n").Replace("\n", Environment.NewLine + "    ");

        private static string CurrentPathLocked()
        {
            if (_filePath != null) return _filePath;
            if (_dailyDir != null)
                return Path.Combine(_dailyDir, $"{_dailyPrefix}-{DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}.log");
            return null;
        }

        private static bool AcquireSlotLocked(string key, out int repeats)
        {
            repeats = 1;
            DateTime now = DateTime.UtcNow;

            if (_recent.TryGetValue(key, out var r))
            {
                if (now - r.Last <= _options.RepeatWindow)
                {
                    r.Last = now;
                    r.Count++;
                    return false;                      // coalesced into the next write of this key
                }
                repeats = r.Count;
                r.Last = now;
                r.Count = 1;
            }
            else
            {
                _recent[key] = new Repeat { Last = now, Count = 1 };
                if (_recent.Count > 256)
                {
                    var cutoff = now - TimeSpan.FromMinutes(5);
                    var stale = new List<string>();
                    foreach (var kv in _recent) if (kv.Value.Last < cutoff) stale.Add(kv.Key);
                    foreach (var k in stale) _recent.Remove(k);
                }
            }

            while (_written.Count > 0 && now - _written.Peek() > TimeSpan.FromMinutes(1)) _written.Dequeue();
            if (_written.Count >= _options.MaxEntriesPerMinute) { _dropped++; return false; }
            _written.Enqueue(now);
            return true;
        }

        private static void FlushPendingLocked()
        {
            if (_pending.Count == 0) return;
            string path = CurrentPathLocked();
            foreach (var e in _pending) AppendLocked(path, e);
            _pending.Clear();
        }

        private static void AppendLocked(string path, string entry)
        {
            try
            {
                string dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir)) System.IO.Directory.CreateDirectory(dir);
                if (_filePath != null) RotateLocked(path);
                File.AppendAllText(path, entry + Environment.NewLine, new UTF8Encoding(false));
            }
            catch { /* disk full / locked: drop */ }
        }

        private static void RotateLocked(string path)
        {
            try
            {
                var fi = new FileInfo(path);
                if (!fi.Exists) return;
                bool tooBig = fi.Length > _options.MaxBytes;
                bool tooOld = _options.MaxAgeDays > 0 && DateTime.UtcNow - fi.LastWriteTimeUtc > TimeSpan.FromDays(_options.MaxAgeDays);
                if (tooBig || tooOld) File.Move(path, path + ".old", overwrite: true);
            }
            catch { /* rotation failure must not block logging */ }
        }

        private static void PruneDailyLocked()
        {
            try
            {
                if (_options.MaxAgeDays <= 0 || !System.IO.Directory.Exists(_dailyDir)) return;
                var cutoff = DateTime.Now.AddDays(-_options.MaxAgeDays);
                foreach (var f in System.IO.Directory.GetFiles(_dailyDir, _dailyPrefix + "-*.log"))
                    if (File.GetLastWriteTime(f) < cutoff) File.Delete(f);
            }
            catch { /* best-effort */ }
        }
    }
}
