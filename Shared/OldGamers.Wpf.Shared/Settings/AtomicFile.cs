using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace OldGamers.Wpf.Shared
{
    /// <summary>
    /// Crash-safe file writes (S-05, 2026-10-14): write "&lt;file&gt;.tmp", then replace the target in one
    /// rename, so a crash or power loss leaves either the old or the new file — never a half-written one.
    /// UTF-8 without BOM.
    /// </summary>
    public static class AtomicFile
    {
        private static readonly UTF8Encoding Utf8NoBom = new(false);

        public static void WriteAllText(string path, string contents)
        {
            string dir = Path.GetDirectoryName(Path.GetFullPath(path));
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            string tmp = path + ".tmp";
            try
            {
                using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
                using (var w = new StreamWriter(fs, Utf8NoBom))
                {
                    w.Write(contents);
                    w.Flush();
                    fs.Flush(flushToDisk: true);
                }
                File.Move(tmp, path, overwrite: true);
            }
            catch
            {
                try { if (File.Exists(tmp)) File.Delete(tmp); } catch { /* best-effort cleanup */ }
                throw;
            }
        }
    }

    /// <summary>
    /// A settings/data file that can't be parsed is renamed aside
    /// (<c>name.corrupt-yyyyMMdd-HHmmss.ext</c>) before the app can overwrite it with defaults (from RO-05).
    /// </summary>
    public static class CorruptFile
    {
        private static readonly object _gate = new();
        private static readonly List<string> _kept = new();

        /// <summary>Files set aside in this session (shown to the user once at startup).</summary>
        public static IReadOnlyList<string> SetAsideThisSession
        {
            get { lock (_gate) return _kept.ToArray(); }
        }

        /// <summary>Renames <paramref name="path"/> aside and logs why. Returns the kept path, or null.</summary>
        public static string SetAside(string path, Exception reason)
        {
            AppLog.Error("Unreadable file " + Path.GetFileName(path), reason);
            try
            {
                string kept = Path.Combine(Path.GetDirectoryName(path) ?? "",
                    $"{Path.GetFileNameWithoutExtension(path)}.corrupt-{DateTime.Now:yyyyMMdd-HHmmss}{Path.GetExtension(path)}");
                File.Move(path, kept, overwrite: false);
                lock (_gate) _kept.Add(kept);
                return kept;
            }
            catch (Exception ex)
            {
                AppLog.Error("CorruptFile.SetAside", ex);
                return null;
            }
        }
    }
}
