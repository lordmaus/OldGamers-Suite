using System.Collections.Generic;
using System.Globalization;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace SuperMagnifier
{
    /// <summary>
    /// Source-generated JSON for lens presets, app→preset rules and per-app lens positions
    /// (SM-02, 2026-10-13). Replaces hand-built StringBuilder JSON + regex readers, which wrote
    /// culture-formatted numbers ("2,50" on de-DE — SM-01) and broke on quotes/newlines in names.
    /// </summary>
    [JsonSerializable(typeof(List<LensPreset>))]
    [JsonSerializable(typeof(List<AppPresetMap.AppPresetRule>))]
    [JsonSerializable(typeof(List<AppPositionMap.AppPositionEntry>))]
    [JsonSourceGenerationOptions(WriteIndented = false, PropertyNamingPolicy = JsonKnownNamingPolicy.Unspecified)]
    internal partial class MapsJsonContext : JsonSerializerContext { }

    /// <summary>
    /// Readers for the pre-2026-10-13 hand-written format. Numbers are matched tolerantly
    /// (<c>2.50</c> or <c>2,50</c>) so files written on comma-decimal locales are repaired on load.
    /// Remove one release after 2026.10.
    /// </summary>
    internal static partial class LegacyJson
    {
        [GeneratedRegex(@"\{([^{}]*)\}")] internal static partial Regex ObjectRx();

        internal static string Str(string body, string key, string def)
        {
            var m = Regex.Match(body, $"\"{key}\"\\s*:\\s*\"([^\"]*)\"");
            return m.Success ? m.Groups[1].Value : def;
        }

        internal static double Dbl(string body, string key, double def)
        {
            // number, optional decimal part with '.' or ',', followed by ',' / '}' / end of body
            var m = Regex.Match(body, $"\"{key}\"\\s*:\\s*(-?\\d+(?:[.,]\\d+)?)(?=\\s*(?:[,}}\\]]|$))");
            return m.Success && double.TryParse(m.Groups[1].Value.Replace(',', '.'),
                NumberStyles.Float, CultureInfo.InvariantCulture, out double v) ? v : def;
        }

        internal static int Int(string body, string key, int def)
        {
            var m = Regex.Match(body, $"\"{key}\"\\s*:\\s*(-?\\d+)");
            return m.Success && int.TryParse(m.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int v) ? v : def;
        }

        internal static bool Bool(string body, string key, bool def)
        {
            var m = Regex.Match(body, $"\"{key}\"\\s*:\\s*(true|false)", RegexOptions.IgnoreCase);
            return m.Success ? m.Groups[1].Value.Equals("true", System.StringComparison.OrdinalIgnoreCase) : def;
        }
    }
}
