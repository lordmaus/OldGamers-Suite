using System;
using System.IO;

#nullable enable


namespace FileCopy
{
    /// <summary>
    /// Portable mode: if a file named "portable.flag" exists alongside the
    /// executable, all user data is stored in a "Data" sub-folder next to
    /// the executable instead of %APPDATA%.
    ///
    /// This makes the entire application self-contained on a USB drive:
    ///   FileCopyStudio.exe
    ///   portable.flag          ← create this to activate portable mode
    ///   Data\
    ///     prefs.json
    ///     logs\
    ///       filecopy-studio-YYYY-MM-DD.log
    ///
    /// Portable mode disables:
    ///   - HKLM/HKCU registry writes (shell verbs, file association)
    ///   - Single-instance pipe (each USB plug-in is independent)
    ///   - %APPDATA% directory creation
    /// </summary>
    public static class PortableMode
    {
        private static readonly Lazy<bool> _active = new Lazy<bool>(Detect);
        private static readonly Lazy<string> _dataDir = new Lazy<string>(ResolveDataDir);

        /// <summary>True when portable.flag exists next to the executable.</summary>
        public static bool Active => _active.Value;

        /// <summary>Root data directory (portable: ./Data, normal: %APPDATA%\File Copy Studio).</summary>
        public static string DataDir => _dataDir.Value;

        /// <summary>Full path to prefs.json.</summary>
        public static string PrefsPath => Path.Combine(DataDir, "prefs.json");

        /// <summary>Full path to the logs sub-directory.</summary>
        public static string LogsDir => Path.Combine(DataDir, "logs");

        private static bool Detect()
        {
            string exeDir = AppContext.BaseDirectory;
            return File.Exists(Path.Combine(exeDir, "portable.flag"));
        }

        private static string ResolveDataDir()
        {
            if (Active)
                return Path.Combine(AppContext.BaseDirectory, "Data");

            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "File Copy Studio");
        }

        /// <summary>
        /// Ensures the data directory (and logs sub-dir) exist.
        /// Called once on startup before any file I/O.
        /// </summary>
        public static void EnsureDataDir()
        {
            Directory.CreateDirectory(DataDir);
            Directory.CreateDirectory(LogsDir);
        }
    }
}
