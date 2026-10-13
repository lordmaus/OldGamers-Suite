using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

#nullable enable


namespace FileCopy
{
    /// <summary>
    /// Serializable copy job (source/dest/options + planned items). Use to save and
    /// reload repeatable batch transfers ("backup laptop → external drive").
    /// </summary>
    public sealed record FcJobFile(
        string FormatVersion,
        DateTime CreatedUtc,
        string SourcePath,
        string DestPath,
        ConflictMode ConflictMode,
        string FileMaskInclude,
        string FileMaskExclude,
        bool   IncludeSubdirs,
        bool   PreserveTimestamps,
        bool   PreserveAttributes,
        bool   VerifyCrc,
        bool   DeleteSourceOnMove,
        bool   ResumePartial,
        int    BufferSizeKb,
        int    MaxParallel,
        List<FcJobItem> Items,
        bool   Truncated = false);  // true if item list was capped at MaxItems

    public sealed record FcJobItem(
        string RelativePath,
        long   SizeBytes,
        DateTime SourceModified,
        FileAction PlannedAction);

    [JsonSerializable(typeof(FcJobFile))]
    [JsonSourceGenerationOptions(WriteIndented = true)]
    internal partial class FcJobContext : JsonSerializerContext { }

    public static class FcJob
    {
        public const string Extension = ".fclist";
        public const string CurrentVersion = "1";

        public const int MaxItems = 50_000;

        public static FcJobFile Build(AppPrefs p, IList<FileItem> items)
        {
            var jobItems = new List<FcJobItem>(Math.Min(items.Count, MaxItems));
            int count = Math.Min(items.Count, MaxItems);
            for (int i = 0; i < count; i++)
            {
                var it = items[i];
                jobItems.Add(new FcJobItem(it.RelativePath, it.SizeBytes, it.SourceModified, it.Action));
            }

            return new FcJobFile(
                FormatVersion:      CurrentVersion,
                CreatedUtc:         DateTime.UtcNow,
                SourcePath:         p.SourcePath,
                DestPath:           p.DestPath,
                ConflictMode:       p.ConflictMode,
                FileMaskInclude:    p.FileMaskInclude,
                FileMaskExclude:    p.FileMaskExclude,
                IncludeSubdirs:     p.IncludeSubdirs,
                PreserveTimestamps: p.PreserveTimestamps,
                PreserveAttributes: p.PreserveAttributes,
                VerifyCrc:          p.VerifyCrc,
                DeleteSourceOnMove: p.DeleteSourceOnMove,
                ResumePartial:      p.ResumePartial,
                BufferSizeKb:       p.BufferSizeKb,
                MaxParallel:        p.MaxParallel,
                Items:              jobItems,
                Truncated:          items.Count > MaxItems);
        }

        public static void Save(string path, FcJobFile job)
        {
            string json = JsonSerializer.Serialize(job, FcJobContext.Default.FcJobFile);
            File.WriteAllText(path, json);
        }

        private const long MaxJobFileSizeBytes = 10 * 1024 * 1024; // 10 MB

        public static FcJobFile? Load(string path)
        {
            var info = new FileInfo(path);
            if (info.Length > MaxJobFileSizeBytes)
                throw new InvalidDataException(
                    $"Job file is too large ({SizeFormat.Format(info.Length)}). Maximum is 10 MB.");

            string json = File.ReadAllText(path);
            return JsonSerializer.Deserialize(json, FcJobContext.Default.FcJobFile);
        }
    }
}
