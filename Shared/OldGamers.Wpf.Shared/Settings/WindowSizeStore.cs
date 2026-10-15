using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace OldGamers.Wpf.Shared
{
    /// <summary>
    /// Remembers dialog / sub-window Width × Height across sessions in one JSON file
    /// (S-05, 2026-10-14 — replaces 5 per-app WindowSizeCache copies; writes are atomic now).
    /// Each app keeps a one-line <c>WindowSizeCache</c> facade that owns its file path.
    /// </summary>
    public sealed class WindowSizeStore
    {
        public const double MinWidth = 100, MinHeight = 60;

        private readonly string _path;
        private readonly object _gate = new();
        private Dictionary<string, double[]> _sizes;

        public WindowSizeStore(string path) { _path = path; }

        public string FilePath => _path;

        /// <summary>Saved size for <paramref name="key"/>, or the defaults.</summary>
        public (double W, double H) Get(string key, double defW, double defH)
        {
            lock (_gate)
            {
                if (Sizes.TryGetValue(key, out double[] v) && v != null && v.Length >= 2
                    && v[0] >= MinWidth && v[1] >= MinHeight && double.IsFinite(v[0]) && double.IsFinite(v[1]))
                    return (v[0], v[1]);
                return (defW, defH);
            }
        }

        /// <summary>Saves the size (ignored when implausibly small or not a number).</summary>
        public void Set(string key, double w, double h)
        {
            if (!(w >= MinWidth) || !(h >= MinHeight) || !double.IsFinite(w) || !double.IsFinite(h)) return;
            lock (_gate)
            {
                Sizes[key] = new[] { w, h };
                try
                {
                    AtomicFile.WriteAllText(_path,
                        JsonSerializer.Serialize(Sizes, WindowSizeJson.Default.DictionaryStringDoubleArray));
                }
                catch (Exception ex) { AppLog.Warn("WindowSizeStore.Set " + Path.GetFileName(_path), ex); }
            }
        }

        private Dictionary<string, double[]> Sizes => _sizes ??= Load();

        private Dictionary<string, double[]> Load()
        {
            try
            {
                if (!File.Exists(_path)) return new();
                return JsonSerializer.Deserialize(File.ReadAllText(_path, Encoding.UTF8),
                           WindowSizeJson.Default.DictionaryStringDoubleArray) ?? new();
            }
            catch (Exception ex)
            {
                AppLog.Warn("WindowSizeStore.Load " + Path.GetFileName(_path), ex);
                return new();
            }
        }
    }

    [JsonSerializable(typeof(Dictionary<string, double[]>))]
    [JsonSourceGenerationOptions(WriteIndented = true)]
    internal partial class WindowSizeJson : JsonSerializerContext { }
}
