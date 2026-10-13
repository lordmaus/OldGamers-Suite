using System;
using System.Reflection;

namespace FileCopy
{
    /// <summary>
    /// Single source for the user-facing version string. Reads
    /// <see cref="AssemblyInformationalVersionAttribute"/> first because
    /// that's the only attribute that preserves zero-padding from csproj
    /// <c>&lt;Version&gt;yyyy.MM.dd.NNNN&lt;/Version&gt;</c>. Falls through
    /// to <see cref="AssemblyName.Version"/> with manual padding when the
    /// informational attribute is absent (older builds, side-loaded copies).
    ///
    /// Source-link / SourceRevisionId can append "+commit-hash" to the
    /// informational version; we strip that for clean display.
    ///
    /// Ported from BadgeOverlay/Core/VersionInfo.cs — suite-wide standard
    /// per Matthew's 2026-09 UI-consistency pass (About dialog version format).
    /// </summary>
    internal static class VersionInfo
    {
        private static readonly Lazy<string> _display = new Lazy<string>(Compute);

        /// <summary>Display version, e.g. "v2026.04.24.0001".
        /// Always non-empty; falls back to "v—" on total failure.</summary>
        public static string Display => _display.Value;

        /// <summary>Display version without the leading "v".</summary>
        public static string DisplayBare => Display.StartsWith("v")
            ? Display.Substring(1) : Display;

        private static string Compute()
        {
            try
            {
                var asm = Assembly.GetExecutingAssembly();
                var infoAttr = (AssemblyInformationalVersionAttribute?)
                    Attribute.GetCustomAttribute(asm,
                        typeof(AssemblyInformationalVersionAttribute));
                if (infoAttr != null && !string.IsNullOrWhiteSpace(infoAttr.InformationalVersion))
                    return "v" + infoAttr.InformationalVersion.Split('+')[0];

                var v = asm.GetName().Version;
                if (v != null)
                    return $"v{v.Major}.{v.Minor:D2}.{v.Build:D2}.{v.Revision:D4}";
            }
            catch { }
            return "v—";
        }
    }
}
