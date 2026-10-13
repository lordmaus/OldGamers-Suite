using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

#nullable enable


namespace FileCopy
{
    /// <summary>
    /// Resume marker for interrupted copies.
    /// 
    /// Layout:
    ///   <dest>.fctemp       — the partial file payload (raw bytes, no header)
    ///   <dest>.fctemp.meta  — JSON sidecar: source length, source mtime, partial CRC
    /// 
    /// On verified completion, the .fctemp is renamed to <dest> and .meta is deleted.
    /// This avoids the 2x-disk-I/O penalty of "strip a leading header on finalize".
    /// </summary>
    public sealed record PartialMeta(
        long SourceLength,
        long SourceMtimeUtcTicks,
        uint PartialCrc,
        long PartialOffset);

    [JsonSerializable(typeof(PartialMeta))]
    [JsonSourceGenerationOptions(WriteIndented = false)]
    internal partial class PartialMetaContext : JsonSerializerContext { }

    public static class PartialFile
    {
        private static readonly Microsoft.Extensions.Logging.ILogger _log =
            AppLogger.ForCategory("PartialFile");
        public const string Extension     = ".fctemp";
        public const string MetaExtension = ".fctemp.meta";

        public static string GetTempPath(string finalPath) => finalPath + Extension;
        public static string GetMetaPath(string finalPath) => finalPath + MetaExtension;

        /// <summary>Inspect a temp+meta pair. Returns null if not resumable.</summary>
        public static (PartialMeta Meta, long ResumeOffset)? TryInspect(string finalPath, FileInfo source)
        {
            try
            {
                string tempPath = GetTempPath(finalPath);
                string metaPath = GetMetaPath(finalPath);
                if (!File.Exists(tempPath) || !File.Exists(metaPath)) return null;

                var tempFi = new FileInfo(tempPath);
                if (tempFi.Length == 0 || tempFi.Length > source.Length) return null;

                var metaFi = new FileInfo(metaPath);
                if (metaFi.Length > 65536) // sidecar is a handful of fields — anything this large isn't ours
                {
                    _log.LogWarning("{Path} meta sidecar is unexpectedly large ({Size} bytes) — treating as non-resumable", metaPath, metaFi.Length);
                    return null;
                }

                string json = File.ReadAllText(metaPath);
                var meta = JsonSerializer.Deserialize(json, PartialMetaContext.Default.PartialMeta);
                if (meta == null) return null;

                if (meta.SourceLength       != source.Length)                 return null;
                if (meta.SourceMtimeUtcTicks != source.LastWriteTimeUtc.Ticks) return null;
                if (meta.PartialOffset       > tempFi.Length)                 return null;
                // The temp may have advanced past the recorded offset between the last
                // meta flush and the crash. We trust the meta-recorded offset (which
                // matches the CRC state) and discard any bytes after it.
                return (meta, meta.PartialOffset);
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "TryInspect failed for {Path}", finalPath);
                return null;
            }
        }

        public static async Task WriteMetaAsync(string finalPath, FileInfo source, uint partialCrc, long offset, CancellationToken ct)
        {
            string metaPath = GetMetaPath(finalPath);
            string tmpMeta  = metaPath + ".tmp";
            var meta = new PartialMeta(source.Length, source.LastWriteTimeUtc.Ticks, partialCrc, offset);
            string json = JsonSerializer.Serialize(meta, PartialMetaContext.Default.PartialMeta);
            // Write to a side-sidecar first, then rename — prevents the race where a crash
            // between FlushAsync and the meta write leaves a .fctemp with no .meta.
            await File.WriteAllTextAsync(tmpMeta, json, ct);
            File.Move(tmpMeta, metaPath, overwrite: true);
        }

        /// <summary>Atomically promote verified .fctemp → final dest; remove .meta sidecar.</summary>
        public static void Finalize(string finalPath)
        {
            string tempPath = GetTempPath(finalPath);
            string metaPath = GetMetaPath(finalPath);

            // FC-02: single MoveFileEx(REPLACE_EXISTING) — the old destination survives a crash
            // at this point (previously Delete + Move left a window with neither file).
            if (File.Exists(finalPath))
            {
                var attrs = File.GetAttributes(finalPath);
                if ((attrs & FileAttributes.ReadOnly) != 0)
                    File.SetAttributes(finalPath, attrs & ~FileAttributes.ReadOnly);
            }
            File.Move(tempPath, finalPath, overwrite: true);

            try { if (File.Exists(metaPath)) File.Delete(metaPath); } catch { }
        }

        public static void DiscardTemp(string finalPath)
        {
            string tempPath = GetTempPath(finalPath);
            string metaPath = GetMetaPath(finalPath);
            try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { }
            try { if (File.Exists(metaPath)) File.Delete(metaPath); } catch { }
            try { if (File.Exists(metaPath + ".tmp")) File.Delete(metaPath + ".tmp"); } catch { }
        }
    }
}
