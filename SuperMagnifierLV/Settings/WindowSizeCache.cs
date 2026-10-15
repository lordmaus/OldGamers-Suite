using System;
using System.IO;

namespace SuperMagnifier
{
    /// <summary>Sub-window sizes → window-sizes.json (suite <see cref="WindowSizeStore"/>, S-05 2026-10-14).</summary>
    internal static class WindowSizeCache
    {
        private static readonly WindowSizeStore _store = new(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SuperMagnifierLV", "window-sizes.json"));

        public static (double W, double H) Get(string key, double defW, double defH) => _store.Get(key, defW, defH);
        public static void Set(string key, double w, double h) => _store.Set(key, w, h);
    }
}
