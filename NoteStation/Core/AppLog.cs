using System;
using System.IO;

namespace NoteStation
{
    /// <summary>
    /// Persistent error log for save/load failures. Debug.WriteLine compiles to a
    /// no-op in Release builds, which meant every DataStore save/load failure was
    /// completely silent in production — the user's clipboard/notes/task edit was
    /// lost with zero record anywhere. This writes a small rolling text log to
    /// %AppData%\NoteStation\errors.log in both Debug and Release.
    ///
    /// GDPR note: logs only exception type + message + a short context label
    /// (e.g. "SaveTask 42") — never item content, note text, clipboard text, or
    /// the user's system username.
    /// </summary>
    internal static class AppLog
    {
        private static readonly string LogFile = Path.Combine(App.AppDataDir, "errors.log");
        private const long MaxBytes = 512 * 1024; // rotate at 512KB
        private static readonly object _lock = new();

        public static void Error(string context, Exception ex) =>
            Write($"{context} | {ex.GetType().Name}: {ex.Message}");

        public static void Warn(string message) => Write("WARN | " + message);

        private static void Write(string text)
        {
            try
            {
                lock (_lock)
                {
                    Directory.CreateDirectory(App.AppDataDir);
                    if (File.Exists(LogFile) && new FileInfo(LogFile).Length > MaxBytes)
                    {
                        string prev = LogFile + ".old";
                        try { File.Delete(prev); } catch { }
                        try { File.Move(LogFile, prev); } catch { }
                    }
                    string line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} | {text}{Environment.NewLine}";
                    File.AppendAllText(LogFile, line);
                }
            }
            catch { /* best-effort — never let logging itself throw */ }
        }
    }
}
