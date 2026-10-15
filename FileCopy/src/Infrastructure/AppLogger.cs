using System;
using Microsoft.Extensions.Logging;

#nullable enable


namespace FileCopy
{
    /// <summary>
    /// Central logging setup. FileCopy code logs through <see cref="ILogger"/>; every entry goes to the
    /// suite <see cref="AppLog"/> (S-07, 2026-10-14): one file per day,
    /// <c>logs\filecopy-studio-yyyy-MM-dd.log</c> (portable: <c>Data\logs\</c>), 30 days kept,
    /// profile path redacted, Information and above (Debug output window gets everything).
    /// </summary>
    public static class AppLogger
    {
        public const string FilePrefix = "filecopy-studio";
        private static ILoggerFactory? _factory;

        public static void Initialize()
        {
            PortableMode.EnsureDataDir();
            AppLog.ConfigureDaily(PortableMode.LogsDir, FilePrefix, new AppLogOptions { MaxAgeDays = 30 });

            _factory = LoggerFactory.Create(builder =>
            {
                builder
#if DEBUG
                    .SetMinimumLevel(LogLevel.Debug)
#else
                    .SetMinimumLevel(LogLevel.Information)   // FC-11
#endif
                    .AddProvider(new AppLogProvider());
            });
        }

        public static ILogger<T> ForContext<T>() =>
            _factory?.CreateLogger<T>()
            ?? Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance.CreateLogger<T>();

        public static ILogger ForCategory(string? category) =>
            _factory?.CreateLogger(category ?? "")
            ?? Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance.CreateLogger(category ?? "");

        public static void Shutdown() => _factory?.Dispose();
    }

    /// <summary>ILogger → suite <see cref="AppLog"/> bridge. Category shown as its last segment.</summary>
    internal sealed class AppLogProvider : ILoggerProvider
    {
        public ILogger CreateLogger(string categoryName) => new AppLogBridge(categoryName);
        public void Dispose() { }
    }

    internal sealed class AppLogBridge : ILogger
    {
        private readonly string _cat;
        public AppLogBridge(string cat) { _cat = cat.Split('.')[^1]; }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel level) => level != LogLevel.None;

        public void Log<TState>(LogLevel level, EventId eventId, TState state, Exception? ex,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(level)) return;
            AppLog.Write(level switch
            {
                LogLevel.Trace or LogLevel.Debug => AppLogLevel.Debug,
                LogLevel.Information             => AppLogLevel.Info,
                LogLevel.Warning                 => AppLogLevel.Warn,
                _                                => AppLogLevel.Error,
            }, _cat, formatter(state, ex), ex);
        }
    }
}
