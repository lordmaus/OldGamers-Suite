using System;
using System.IO;

namespace SuperMagnifier
{
    /// <summary>
    /// Persistent error log (SM-05, 2026-10-13). Debug.WriteLine is a no-op in Release, so crashes
    /// and save failures previously left no trace. Writes %AppData%\SuperMagnifierLV\crash.log,
    /// rotated at 512 KB. Logs exception type, message and stack only — never screen content,
    /// window titles or text under the lens.
    /// </summary>
    internal static class CrashLog
    {
        private const long MaxBytes = 512 * 1024;
        private static readonly object _lock = new();

        public static string LogPath =>
            Path.Combine(Path.GetDirectoryName(AppSettings.FilePath) ?? AppContext.BaseDirectory, "crash.log");

        public static void Write(string context, Exception ex) =>
            Append($"{context} | {ex?.GetType().Name}: {ex?.Message}{Environment.NewLine}{ex?.StackTrace}");

        public static void Info(string message) => Append("INFO | " + message);

        private static void Append(string text)
        {
            try
            {
                lock (_lock)
                {
                    string path = LogPath;
                    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                    if (File.Exists(path) && new FileInfo(path).Length > MaxBytes)
                    {
                        string old = path + ".old";
                        try { File.Delete(old); } catch { }
                        try { File.Move(path, old); } catch { }
                    }
                    File.AppendAllText(path, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {text}{Environment.NewLine}{Environment.NewLine}");
                }
            }
            catch { /* best-effort: logging must never throw */ }
        }
    }
}
