using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ReticleOverlay
{
    /// <summary>
    /// Persists sub-window Width × Height across sessions.
    /// Saved to ReticleOverlay\window-sizes.json independently of main settings.
    /// </summary>
    internal static class WindowSizeCache
    {
        private static readonly string _path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ReticleOverlay", "window-sizes.json");
        private static Dictionary<string, double[]> _d = TryLoad();

        private static Dictionary<string, double[]> TryLoad()
        {
            try
            {
                if (!File.Exists(_path)) return new();
                string json = File.ReadAllText(_path, Encoding.UTF8);
                return JsonSerializer.Deserialize(json, WscContext.Default.DictionaryStringDoubleArray)
                    ?? new();
            }
            catch { return new(); }
        }

        /// <summary>Returns persisted size, or defaults if not yet saved.</summary>
        public static (double W, double H) Get(string key, double defW, double defH)
        {
            if (_d.TryGetValue(key, out double[] v) && v.Length >= 2
                && v[0] >= 100 && v[1] >= 60)
                return (v[0], v[1]);
            return (defW, defH);
        }

        /// <summary>Persists the window size (no-ops if implausibly small).</summary>
        public static void Set(string key, double w, double h)
        {
            if (w < 100 || h < 60) return;
            _d[key] = new[] { w, h };
            try
            {
                string dir = Path.GetDirectoryName(_path);
                if (dir is not null) Directory.CreateDirectory(dir);
                string tmp = _path + ".tmp";
                File.WriteAllText(tmp,
                    JsonSerializer.Serialize(_d, WscContext.Default.DictionaryStringDoubleArray),
                    Encoding.UTF8);
                File.Move(tmp, _path, overwrite: true);
            }
            catch { /* non-critical */ }
        }
    }

    [JsonSerializable(typeof(Dictionary<string, double[]>))]
    [JsonSourceGenerationOptions(WriteIndented = true)]
    internal partial class WscContext : JsonSerializerContext { }
}
