using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace SuperMagnifier
{
    /// <summary>
    /// .NET 10 JSON Source Generator context for AppSettings.
    /// Enables high-performance, reflection-free serialization per project spec.
    /// </summary>
    [JsonSerializable(typeof(AppSettingsJson))]
    [JsonSerializable(typeof(List<string>))]
    [JsonSourceGenerationOptions(
        WriteIndented = true,
        PropertyNamingPolicy = JsonKnownNamingPolicy.Unspecified,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
    internal partial class AppSettingsContext : JsonSerializerContext { }
}
