using System;
using System.IO;

namespace ReticleOverlay
{
    /// <summary>
    /// Persistent error log (RO-03, 2026-10-13). Debug.WriteLine is a no-op in Release, so crashes
    /// and load/save failures previously left no trace. Writes %AppData%\ReticleOverlay\crash.log,
    /// rotated at 512 KB. Exception type, message and stack only.
    /// </summary>
    internal static class CrashLog
    {
        private const long MaxBytes = 512 * 1024;
        private static readonly object _lock = new();

        public static string Dir => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ReticleOverlay");
        public static string LogPath => Path.Combine(Dir, "crash.log");

        public static void Write(string context, Exception ex) =>
            Append($"{context} | {ex?.GetType().Name}: {ex?.Message}{Environment.NewLine}{ex?.StackTrace}");

        public static void Info(string message) => Append("INFO | " + message);

        private static void Append(string text)
        {
            try
            {
                lock (_lock)
                {
                    Directory.CreateDirectory(Dir);
                    string path = LogPath;
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

    /// <summary>
    /// RO-05: a settings/profile file that can't be parsed is renamed aside
    /// (name.corrupt-yyyyMMdd-HHmmss.json) before the app can overwrite it with defaults.
    /// </summary>
    internal static class CorruptFile
    {
        /// <summary>Kept paths from this session, shown once at startup.</summary>
        public static readonly System.Collections.Generic.List<string> SetAsideThisSession = new();

        public static string SetAside(string path, Exception reason)
        {
            CrashLog.Write("Unreadable file " + Path.GetFileName(path), reason);
            try
            {
                string kept = Path.Combine(Path.GetDirectoryName(path) ?? "",
                    $"{Path.GetFileNameWithoutExtension(path)}.corrupt-{DateTime.Now:yyyyMMdd-HHmmss}{Path.GetExtension(path)}");
                File.Move(path, kept, overwrite: false);
                SetAsideThisSession.Add(kept);
                return kept;
            }
            catch (Exception ex) { CrashLog.Write("CorruptFile.SetAside", ex); return null; }
        }
    }
}
