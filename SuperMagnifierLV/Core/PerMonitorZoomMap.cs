using System;
using System.Collections.Generic;

namespace SuperMagnifier
{
    /// <summary>
    /// Serialises/deserialises a dictionary of monitorName → zoom level.
    /// Format: "DISPLAY1=2.5;DISPLAY2=4.0" (semicolon-separated, = separator)
    /// </summary>
    internal static class PerMonitorZoomMap
    {
        public static Dictionary<string, double> Deserialise(string raw)
        {
            var map = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrWhiteSpace(raw)) return map;
            foreach (var part in raw.Split(';'))
            {
                var kv = part.Trim().Split('=');
                if (kv.Length == 2 &&
                    double.TryParse(kv[1],
                        System.Globalization.NumberStyles.Any,
                        System.Globalization.CultureInfo.InvariantCulture, out double z))
                    map[kv[0].Trim()] = Math.Max(1.25, Math.Min(20.0, z));
            }
            return map;
        }

        public static string Serialise(Dictionary<string, double> map)
        {
            if (map == null || map.Count == 0) return "";
            var parts = new List<string>();
            foreach (var kv in map)
                parts.Add($"{kv.Key}={kv.Value.ToString("G", System.Globalization.CultureInfo.InvariantCulture)}");
            return string.Join(";", parts);
        }

        /// <summary>
        /// Gets the monitor name for the given lens logical position.
        /// Returns empty string on failure.
        /// </summary>
        public static string GetMonitorName(double lensLeft, double lensTop,
                                             float dpiScaleX, float dpiScaleY)
        {
            try
            {
                int physX = (int)(lensLeft * dpiScaleX);
                int physY = (int)(lensTop  * dpiScaleY);
                var mon   = MonitorHelper.MonitorFromPoint(physX, physY);
                return mon?.Name ?? "";
            }
            catch { return ""; }
        }
    }
}
