using System;
using System.IO;
using Microsoft.Extensions.Logging;

#nullable enable


namespace FileCopy
{
    /// <summary>
    /// Central logging setup. Writes structured log entries to:
    ///   - Debug output (VS Output window / DebugView)
    ///   - %APPDATA%\FileCopy\logs\filecopy-{date}.log (plain text, rolling by day)
    /// </summary>
    public static class AppLogger
    {
        private static ILoggerFactory _factory;

        public static void Initialize()
        {
            PortableMode.EnsureDataDir();
            string logsDir = PortableMode.LogsDir;

            PruneOldLogs(logsDir, maxAgeDays: 30);

            _factory = LoggerFactory.Create(builder =>
            {
                builder
#if DEBUG
                    .SetMinimumLevel(LogLevel.Debug)
#else
                    .SetMinimumLevel(LogLevel.Information)   // FC-11
#endif
                    .AddDebug()
                    .AddProvider(new RollingFileLoggerProvider(logsDir));
            });
        }

        private static void PruneOldLogs(string dir, int maxAgeDays)
        {
            try
            {
                var cutoff = DateTime.Now.AddDays(-maxAgeDays);
                foreach (var file in Directory.GetFiles(dir, "filecopy-studio-*.log"))
                {
                    if (File.GetLastWriteTime(file) < cutoff)
                        File.Delete(file);
                }
            }
            catch { /* best-effort; never block startup */ }
        }

        public static ILogger<T> ForContext<T>() =>
            _factory?.CreateLogger<T>()
            ?? Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance.CreateLogger<T>();

        public static ILogger ForCategory(string? category) =>
            _factory?.CreateLogger(category ?? "")
            ?? Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance.CreateLogger(category ?? "");

        public static void Shutdown() => _factory?.Dispose();
    }

    /// <summary>
    /// Minimal rolling-file log provider — one file per calendar day.
    /// Line format: {timestamp} [{level}] [{category}] {message}
    /// </summary>
    internal sealed class RollingFileLoggerProvider : ILoggerProvider
    {
        private readonly string _dir;
        public RollingFileLoggerProvider(string dir) { _dir = dir; }
        public ILogger CreateLogger(string categoryName) => new RollingFileLogger(_dir, categoryName);
        public void Dispose() { }
    }

    internal sealed class RollingFileLogger : ILogger
    {
        private readonly string _dir;
        private readonly string _cat;

        // Per-file locks: concurrent categories writing to the same daily file
        // share that file's lock, not a single global one.
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, object>
            _fileLocks = new System.Collections.Concurrent.ConcurrentDictionary<string, object>();

        public RollingFileLogger(string dir, string cat) { _dir = dir; _cat = cat; }

        public IDisposable? BeginScope<TState>(TState state) => null;
        public bool IsEnabled(LogLevel level) => level >= LogLevel.Information;

        public void Log<TState>(LogLevel level, EventId eventId, TState state, Exception? ex,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(level)) return;
            string msg  = formatter(state, ex);
            // Strip embedded newlines to prevent log-line injection via crafted file paths
            msg = msg.Replace('\r', ' ').Replace('\n', ' ');
            string line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level,-11}] [{_cat.Split('.')[^1]}] {msg}";
            if (ex != null) line += Environment.NewLine + ex.ToString();

            string path = Path.Combine(_dir, $"filecopy-studio-{DateTime.Now:yyyy-MM-dd}.log");
            object fileLock = _fileLocks.GetOrAdd(path, _ => new object());
            try { lock (fileLock) { File.AppendAllText(path, line + Environment.NewLine); } }
            catch { /* never throw from logger */ }
        }
    }
}
