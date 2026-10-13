using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Documents;

namespace NoteStation
{
    /// <summary>
    /// Imports clips and folder hierarchy from a ClipClip "Saved Clips" directory.
    /// Supports .txt, .html/.htm, and .rtf clip files.
    /// Each file = one clip; the filename (without extension) becomes the clip Title.
    /// Subfolders are imported as nested NoteStation folders.
    /// </summary>
    public static class ClipClipImporter
    {
        // Supported clip extensions in preference order (txt preferred for mixed stems)
        private static readonly string[] SupportedExtensions =
            { ".txt", ".html", ".htm", ".rtf" };

        // ── Result ────────────────────────────────────────────────────────────
        public class ImportResult
        {
            public List<AppFolder>     NewFolders { get; } = new List<AppFolder>();
            public List<ClipboardItem> NewClips   { get; } = new List<ClipboardItem>();
            public int Skipped { get; set; }
            public int IgnoredUnsupported { get; set; }

            public string Summary =>
                $"Imported {NewClips.Count} clip{(NewClips.Count == 1 ? "" : "s")} " +
                $"in {NewFolders.Count} folder{(NewFolders.Count == 1 ? "" : "s")}" +
                (Skipped > 0 ? $" ({Skipped} skipped — empty/unreadable)" : "") +
                (IgnoredUnsupported > 0 ? $" ({IgnoredUnsupported} non-text file{(IgnoredUnsupported == 1 ? "" : "s")} ignored)" : "") + ".";
        }

        // ── Entry point ───────────────────────────────────────────────────────
        /// <summary>
        /// Walks <paramref name="rootPath"/> recursively.
        /// Caller merges the result into <see cref="AppPrefs"/> and the clip list.
        /// </summary>
        public static ImportResult Import(string rootPath, int nextClipId, int nextFolderId)
        {
            var result = new ImportResult();
            if (!Directory.Exists(rootPath)) return result;

            WalkDirectory(
                rootPath:     rootPath,
                currentPath:  rootPath,
                parentId:     null,
                nextClipId:   ref nextClipId,
                nextFolderId: ref nextFolderId,
                result:       result,
                depth:        0);

            return result;
        }

        // L-1: unbounded recursion + no symlink-loop guard could hang the import
        // on a malformed/circular directory structure. Depth cap is generous
        // (real ClipClip exports are a handful of levels deep); symlinked
        // directories are skipped outright rather than followed, since importing
        // a user's own ClipClip export never needs to cross a reparse point.
        private const int MaxWalkDepth = 64;

        // ── Recursive walk ────────────────────────────────────────────────────
        private static void WalkDirectory(
            string   rootPath,
            string   currentPath,
            int?     parentId,
            ref int  nextClipId,
            ref int  nextFolderId,
            ImportResult result,
            int      depth)
        {
            if (depth > MaxWalkDepth) return;
            // Create a folder node for every directory except the root itself
            int? myFolderId = null;
            bool isRoot = string.Equals(currentPath, rootPath, StringComparison.OrdinalIgnoreCase);
            if (!isRoot)
            {
                int id = nextFolderId++;
                result.NewFolders.Add(new AppFolder
                {
                    Id        = id,
                    ParentId  = parentId,
                    Name      = Path.GetFileName(currentPath),
                    Tab       = "Clipboard",
                    SortOrder = result.NewFolders.Count,
                });
                myFolderId = id;
            }

            // ── Import clip files ─────────────────────────────────────────────
            IEnumerable<string> allFiles;
            try   { allFiles = Directory.GetFiles(currentPath); }
            catch { allFiles = Enumerable.Empty<string>(); }

            // Group by stem so that if multiple formats exist for the same name
            // we pick the best one (txt > html > htm > rtf) instead of duplicating.
            var bySystem = allFiles
                .Where(f => SupportedExtensions.Contains(
                    Path.GetExtension(f), StringComparer.OrdinalIgnoreCase))
                .GroupBy(f => Path.GetFileNameWithoutExtension(f),
                         StringComparer.OrdinalIgnoreCase)
                .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase);

            foreach (var group in bySystem)
            {
                // Try every format of this clip in preference order (txt > html > htm > rtf).
                // ClipClip writes a .txt companion next to the real .html/.rtf; that companion
                // is often an empty stub. Falling through means an empty preferred file no longer
                // kills the whole clip — we use the first format that yields content.
                var ordered = group.OrderBy(f =>
                {
                    int idx = Array.FindIndex(SupportedExtensions,
                        e => e.Equals(Path.GetExtension(f), StringComparison.OrdinalIgnoreCase));
                    return idx < 0 ? 99 : idx;
                });

                string sourceFile = null;
                string text       = null;
                foreach (var f in ordered)
                {
                    string candidate = ReadClipFile(f, Path.GetExtension(f).ToLowerInvariant());
                    if (!string.IsNullOrWhiteSpace(candidate)) { text = candidate; sourceFile = f; break; }
                }

                if (text is null) { result.Skipped++; continue; }   // all formats empty/unreadable

                var clip = ClipboardItem.FromText(nextClipId++, text);
                clip.FolderId        = myFolderId;
                clip.HiddenFromRecent = true;   // imported items never appear in Recent
                // Filename (without extension) → clip Title
                clip.Title = Path.GetFileNameWithoutExtension(sourceFile);
                try { clip.CapturedAt = File.GetLastWriteTime(sourceFile); } catch { }

                result.NewClips.Add(clip);
            }

            // Count non-clip files we ignored (images, .url, etc.) so loss is diagnosable.
            result.IgnoredUnsupported += allFiles.Count(f =>
                !SupportedExtensions.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase));

            // ── Recurse into sub-directories ──────────────────────────────────
            IEnumerable<string> subDirs;
            try   { subDirs = Directory.GetDirectories(currentPath); }
            catch { subDirs = Enumerable.Empty<string>(); }

            foreach (var sub in subDirs.OrderBy(d => Path.GetFileName(d), StringComparer.OrdinalIgnoreCase))
            {
                try { if (new DirectoryInfo(sub).LinkTarget != null) continue; } catch { continue; }
                WalkDirectory(rootPath, sub, myFolderId, ref nextClipId, ref nextFolderId, result, depth + 1);
            }
        }

        // ── Format readers ────────────────────────────────────────────────────
        private static string ReadClipFile(string path, string ext)
        {
            try
            {
                switch (ext)
                {
                    case ".txt":
                        return File.ReadAllText(path, Encoding.UTF8);

                    case ".html":
                    case ".htm":
                        return ReadHtml(path);

                    case ".rtf":
                        return ReadRtf(path);

                    default:
                        return File.ReadAllText(path, Encoding.UTF8);
                }
            }
            catch { return null; }
        }

        /// <summary>
        /// Strips HTML markup and decodes entities, returning readable plain text.
        /// Converts block-level tags to newlines so structure is preserved.
        /// </summary>
        private static string ReadHtml(string path)
        {
            string html;
            try
            {
                // Try UTF-8 first; fall back to system default (covers ANSI-saved HTML)
                html = File.ReadAllText(path, Encoding.UTF8);
            }
            catch { return null; }

            // Remove <script> … </script> and <style> … </style> blocks entirely
            html = Regex.Replace(html,
                @"<(script|style)\b[^>]*>.*?</(script|style)>",
                "", RegexOptions.IgnoreCase | RegexOptions.Singleline);

            // Convert common block-level / line-break tags to newlines
            html = Regex.Replace(html,
                @"<(br|p|div|li|tr|h[1-6]|blockquote|pre)\b[^>]*/?>",
                "\n", RegexOptions.IgnoreCase);

            // Close-tags of block elements also produce a newline
            html = Regex.Replace(html,
                @"</(p|div|li|tr|h[1-6]|blockquote|pre)>",
                "\n", RegexOptions.IgnoreCase);

            // Strip all remaining tags
            html = Regex.Replace(html, @"<[^>]+>", "");

            // Decode common HTML entities
            html = System.Net.WebUtility.HtmlDecode(html);

            // Collapse runs of 3+ newlines to 2, trim leading/trailing whitespace
            html = Regex.Replace(html, @"\n{3,}", "\n\n");
            return html.Trim();
        }

        /// <summary>
        /// Extracts plain text from an RTF file using WPF's native FlowDocument parser.
        /// Must be called on an STA thread (satisfied when called from a WPF button handler).
        /// </summary>
        private static string ReadRtf(string path)
        {
            try
            {
                byte[] bytes = File.ReadAllBytes(path);
                using var stream = new MemoryStream(bytes);

                var doc = new FlowDocument();
                var range = new TextRange(doc.ContentStart, doc.ContentEnd);
                range.Load(stream, DataFormats.Rtf);

                string text = new TextRange(doc.ContentStart, doc.ContentEnd).Text;
                return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
            }
            catch
            {
                // Fallback: brute-force RTF stripper for malformed files
                try   { return StripRtfFallback(File.ReadAllText(path, Encoding.Default)); }
                catch { return null; }
            }
        }

        /// <summary>
        /// Simple regex-based RTF stripper used when FlowDocument parsing fails.
        /// Handles most plain-text RTF content but may leave minor artefacts.
        /// </summary>
        private static string StripRtfFallback(string rtf)
        {
            // Remove optional-destination groups: {\* ... }
            rtf = Regex.Replace(rtf, @"\{\\[\*][^}]*\}", "");
            // Replace \par, \line, \pard with newlines
            rtf = Regex.Replace(rtf, @"\\(par|line)\b\s?", "\n");
            // Remove all other control words and symbols
            rtf = Regex.Replace(rtf, @"\\[a-zA-Z]+-?[0-9]*\s?", "");
            rtf = Regex.Replace(rtf, @"\\[^a-zA-Z]", "");
            // Remove braces
            rtf = rtf.Replace("{", "").Replace("}", "");
            // Decode RTF hex escapes: \'XX
            rtf = Regex.Replace(rtf, @"\\'([0-9a-fA-F]{2})",
                m => ((char)Convert.ToInt32(m.Groups[1].Value, 16)).ToString());
            // Collapse excess whitespace
            rtf = Regex.Replace(rtf, @"\n{3,}", "\n\n");
            return rtf.Trim();
        }
    }
}
