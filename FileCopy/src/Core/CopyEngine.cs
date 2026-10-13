using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.IO.Enumeration;
using System.IO.Hashing;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;

#nullable enable


namespace FileCopy
{
    public enum ConflictMode
    {
        Overwrite,
        OverwriteIfNewer,
        OverwriteIfOlder,
        SkipIfSameDate,
        SkipAll,
        Rename
    }

    public enum SymlinkPolicy
    {
        Follow,     // default: treat symlink target as a regular file (current behaviour)
        Preserve,   // recreate the symlink at dest (file + directory symlinks)
        Skip        // exclude symlinks from the scan entirely
    }

    public enum FileAction
    {
        Pending, Copy, Skip, Overwrite, Rename, Resume, Verify, Verified, Failed, Done
    }

    public class FileItem : System.ComponentModel.INotifyPropertyChanged
    {
        public string SourcePath { get; set; } = string.Empty;
        public string DestPath   { get; set; } = string.Empty;
        public string RelativePath { get; set; } = string.Empty;
        public string Name       { get; set; } = string.Empty;
        public long   SizeBytes  { get; set; }
        public DateTime SourceModified { get; set; }
        public DateTime? DestModified  { get; set; }
        public bool   DestExists       { get; set; }
        public bool   IsSymlink        { get; set; }  // source is a symlink or junction

        public string? SourceCrc { get => field; set { if (field == value) return; field = value; Notify(nameof(SourceCrc)); } }
        public string? DestCrc   { get => field; set { if (field == value) return; field = value; Notify(nameof(DestCrc)); } }

        public long ResumeOffset { get => field; set { if (field == value) return; field = value; Notify(nameof(ResumeOffset)); } }
        public long ProgressBytes { get => field; set { if (field == value) return; field = value; Notify(nameof(ProgressBytes)); Notify(nameof(ProgressPct)); } }
        public double ProgressPct => SizeBytes > 0 ? Math.Min(100.0, ProgressBytes * 100.0 / SizeBytes) : 0;

        public DateTime? ErrorTimestamp { get => field; set { if (field == value) return; field = value; Notify(nameof(ErrorTimestamp)); Notify(nameof(ErrorTimestampText)); } }
        public string ErrorTimestampText => ErrorTimestamp.HasValue ? ErrorTimestamp.Value.ToString("HH:mm:ss") : "";

        public FileAction Action { get => field; set { if (field == value) return; field = value; Notify(nameof(Action)); Notify(nameof(StatusText)); Notify(nameof(ActionGlyph)); Notify(nameof(ActionIcon)); Notify(nameof(PendingGlyph)); } }
        public string? ErrorText  { get => field; set { if (field == value) return; field = value; if (!string.IsNullOrEmpty(value)) { if (!ErrorTimestamp.HasValue) ErrorTimestamp = DateTime.Now; } else { ErrorTimestamp = null; } Notify(nameof(ErrorText)); Notify(nameof(StatusText)); Notify(nameof(ActionGlyph)); Notify(nameof(ActionIcon)); Notify(nameof(PendingGlyph)); } }

        public string StatusText => string.IsNullOrEmpty(ErrorText) ? Action.ToString() : "ERR: " + ErrorText;

        /// <summary>WCAG: non-color status indicator. Glyph shown alongside StatusText.</summary>
        public string ActionGlyph => !string.IsNullOrEmpty(ErrorText) ? "⚠" : IsSymlink ? Action switch
        {
            FileAction.Skip    => "⊘",
            FileAction.Done    => "✓⇢",
            FileAction.Verified=> "✓⇢",
            FileAction.Failed  => "⚠",
            _                  => "⇢"    // symlink pending/copy — arrow indicates link
        } : Action switch
        {
            FileAction.Pending   => "·",
            FileAction.Copy      => "+",
            FileAction.Overwrite => "↻",
            FileAction.Skip      => "⊘",
            FileAction.Rename    => "✎",
            FileAction.Resume    => "▶",
            FileAction.Verify    => "?",
            FileAction.Verified  => "✓",
            FileAction.Failed    => "⚠",
            FileAction.Done      => "✓",
            _                    => "·"
        };
        /// <summary>JtIcon key for the status column (fixed-color, paired with StatusText). Null = Pending (text glyph).</summary>
        public string? ActionIcon => !string.IsNullOrEmpty(ErrorText) ? "warning" : Action switch
        {
            FileAction.Copy      => "copyTask",
            FileAction.Overwrite => "overwrite",
            FileAction.Skip      => "skip",
            FileAction.Rename    => "rename",
            FileAction.Resume    => "resume",
            FileAction.Verify    => "checksum",
            FileAction.Verified  => "audit",
            FileAction.Failed    => "warning",
            FileAction.Done      => "complete",
            _                    => null
        };
        public string PendingGlyph => ActionIcon == null && !IsSymlink ? "·" : "";
        public string SizeText => SizeFormat.Format(SizeBytes);

        /// <summary>
        /// UI dispatcher used to marshal <see cref="PropertyChanged"/> notifications when this
        /// item is mutated from a background copy-worker thread (see CopyEngine.RunAsync /
        /// VerifyOnlyAsync). The backing field write always happens synchronously on the calling
        /// thread — only the event raise (which WPF's binding engine reacts to, and which can
        /// touch DataGrid DependencyObjects) is marshaled. Left null outside an active run — e.g.
        /// during Enumerate/PlanActions or in unit tests — where a synchronous same-thread raise
        /// is correct and keeps FileItem usable without a WPF dispatcher.
        /// </summary>
        public Dispatcher? UiDispatcher { get; set; }

        public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;

        private void Notify(string? n)
        {
            var handler = PropertyChanged;
            if (handler == null) return;
            var args = new System.ComponentModel.PropertyChangedEventArgs(n);
            var d = UiDispatcher;
            if (d == null || d.CheckAccess()) handler(this, args);
            else d.BeginInvoke(new Action(() => handler(this, args)), DispatcherPriority.Background);
        }
    }

    public class CopyProgress
    {
        public int    FileIndex;
        public int    TotalFiles;
        public long   BytesDone;
        public long   BytesTotal;
        public string? CurrentFile;
        public double FileProgressPct;
        public double TotalProgressPct;
        public long   BytesPerSec;
    }

    public static class CopyEngine
    {
        public class CopyOptions
        {
            public bool PreserveTimestamps;
            public bool PreserveAttributes;
            public bool VerifyCrc;
            public bool DeleteSource;
            public bool ResumePartial = true;          // detect & continue .fctemp
            public int  BufferSizeKb;
            public int  MaxParallel = 1;               // capped at 1 for same-volume
            public string? DestRoot;                    // path-traversal guard root
            public long ThrottleBytesPerSec;           // 0 = unlimited
            public bool WriteCrcSidecar;               // write .crc32 alongside verified dest
            public SymlinkPolicy SymlinkPolicy = SymlinkPolicy.Follow;
            public ManualResetEventSlim? Pause;
        }

        // ── Enumerate ─────────────────────────────────────────────────────────
        public static List<FileItem> Enumerate(
            string source, string dest, bool recurse,
            string includeMask, string excludeMask, CancellationToken ct,
            SymlinkPolicy symlinkPolicy = SymlinkPolicy.Follow,
            IProgress<int>? scanProgress = null,
            ICollection<string>? skipped = null)
        {
            var result = new List<FileItem>();
            // FC-10: report what was not scanned instead of dropping it silently.
            void Skip(string path, string reason) => skipped?.Add($"{path} — {reason}");
            if (!Directory.Exists(source)) return result;

            var includes = SplitMasks(includeMask, "*.*");
            var excludes = SplitMasks(excludeMask, "");

            // L-4: source-root escape guard. A directory symlink/junction inside
            // the scanned tree can point anywhere on disk, and .NET's own
            // EnumerationOptions.RecurseSubdirectories=true silently follows it
            // with no way to intercept before it descends. That meant a junction
            // left (or planted) inside a scheduled job's source folder could pull
            // files in from anywhere the running account can read, unattended,
            // with nobody watching to notice. We now walk one directory level at
            // a time and refuse to descend into a reparse-point directory whose
            // resolved target falls outside the originally-selected source root
            // — regardless of SymlinkPolicy. (SymlinkPolicy.Skip already excludes
            // symlinked *files*; this closes the matching gap for symlinked
            // *directories*, which Follow was never meant to let escape the tree.)
            string sourceRoot = Path.GetFullPath(source);

            var enumOpts = new EnumerationOptions
            {
                RecurseSubdirectories = false,   // recursion is done manually below
                IgnoreInaccessible    = false,   // FC-10: denied folders surface as a skip, not silence
                AttributesToSkip      = FileAttributes.System
            };

            int scanned = 0;
            var dirsToScan = new Queue<string>();
            dirsToScan.Enqueue(source);

            while (dirsToScan.Count > 0)
            {
                string dir = dirsToScan.Dequeue();
                ct.ThrowIfCancellationRequested();

                DirectoryInfo dirInfo;
                try { dirInfo = new DirectoryInfo(dir); }
                catch (Exception ex) { Skip(dir, SkipReason(ex)); continue; }

                List<FileInfo> files;
                // Enumerate FileInfo directly — FindFirstFile/FindNextFile already returns
                // size/attributes/times in the listing. Directory.EnumerateFiles(string) + a
                // separate `new FileInfo(path)` throws that data away and pays for a second
                // stat() per file. On 100k+ trees that second stat is the dominant cost.
                // Materialized inside the try: enumeration is lazy, so an access error would
                // otherwise throw from the foreach below, outside any handler.
                try { files = dirInfo.EnumerateFiles("*", enumOpts).ToList(); }
                catch (Exception ex) { Skip(dir, SkipReason(ex)); continue; }

                foreach (var srcFi in files)
                {
                    ct.ThrowIfCancellationRequested();
                    if (++scanned % 500 == 0) scanProgress?.Report(scanned);

                    string path = srcFi.FullName;
                    string name = srcFi.Name;
                    if (name.EndsWith(PartialFile.MetaExtension, StringComparison.OrdinalIgnoreCase)) continue;
                    if (name.EndsWith(PartialFile.Extension,     StringComparison.OrdinalIgnoreCase)) continue;

                    if (!MatchAny(name, includes)) continue;
                    if (excludes.Length > 0 && MatchAny(name, excludes)) continue;

                    bool isSymlink = srcFi.Attributes.HasFlag(FileAttributes.ReparsePoint);

                    // Apply symlink policy
                    if (isSymlink && symlinkPolicy == SymlinkPolicy.Skip) continue;

                    // L-4 (file-level): the directory-junction guard above only covers
                    // reparse-point directories. Under the default Follow policy a symlinked
                    // *file* sitting directly in (or under) the selected source tree was never
                    // checked against sourceRoot before being enumerated and later copied — a
                    // planted (or user-unknowing) file symlink in a scheduled job's source folder
                    // still pulled in content from anywhere the running account can read. Preserve
                    // mode is unaffected: it recreates the link at dest without reading the
                    // target's bytes, so only Follow (which copies content) needs the guard.
                    if (isSymlink && symlinkPolicy == SymlinkPolicy.Follow)
                    {
                        string? fileTarget = File.ResolveLinkTarget(path, returnFinalTarget: true)?.FullName;
                        if (string.IsNullOrEmpty(fileTarget) || !PathIsInside(fileTarget, sourceRoot))
                        {
                            Skip(path, "link points outside the source folder");
                            continue; // refuse to follow a file symlink escaping the selected source root
                        }
                    }

                    string rel     = GetRelative(source, path);
                    string destFull = Path.Combine(dest, rel);
                    // Single FileInfo: one Refresh()/GetFileAttributesEx for both .Exists and .LastWriteTimeUtc.
                    // File.Exists + new FileInfo would call GetFileAttributesEx twice for existing dest files.
                    var destFi = new FileInfo(destFull);
                    bool destExists = destFi.Exists;

                    var item = new FileItem
                    {
                        SourcePath     = path,
                        DestPath       = destFull,
                        RelativePath   = rel,
                        Name           = name,
                        SizeBytes      = isSymlink ? 0 : srcFi.Length,  // symlinks have no data size
                        SourceModified = srcFi.LastWriteTimeUtc,
                        DestExists     = destExists,
                        DestModified   = destExists ? destFi.LastWriteTimeUtc : null,
                        IsSymlink      = isSymlink,
                        Action         = FileAction.Pending
                    };

                    if (!isSymlink)
                    {
                        var inspect = PartialFile.TryInspect(destFull, srcFi);
                        if (inspect.HasValue) item.ResumeOffset = inspect.Value.ResumeOffset;
                    }

                    result.Add(item);
                }

                if (!recurse) continue;   // matches original recurse=false: top level only

                List<DirectoryInfo> subdirs;
                try { subdirs = dirInfo.EnumerateDirectories("*", enumOpts).ToList(); }
                catch (Exception ex) { Skip(dir, "subfolders: " + SkipReason(ex)); continue; }

                foreach (var sub in subdirs)
                {
                    ct.ThrowIfCancellationRequested();

                    if (sub.Attributes.HasFlag(FileAttributes.ReparsePoint))
                    {
                        if (symlinkPolicy == SymlinkPolicy.Skip) continue;

                        string? target = Directory.ResolveLinkTarget(sub.FullName, returnFinalTarget: true)?.FullName;
                        if (string.IsNullOrEmpty(target) || !PathIsInside(target, sourceRoot))
                        {
                            Skip(sub.FullName, "link points outside the source folder");
                            continue; // refuse to follow a junction/symlink escaping the selected source root
                        }
                    }

                    dirsToScan.Enqueue(sub.FullName);
                }
            }
            return result;
        }

        internal static string SkipReason(Exception ex) => ex switch
        {
            UnauthorizedAccessException or System.Security.SecurityException => "access denied",
            DirectoryNotFoundException => "folder no longer exists",
            PathTooLongException       => "path too long",
            _                          => "I/O error: " + ex.Message,
        };

        private static string[] SplitMasks(string s, string defaultIfEmpty)
        {
            if (string.IsNullOrWhiteSpace(s)) s = defaultIfEmpty;
            return s.Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => x.Trim()).Where(x => x.Length > 0).ToArray();
        }

        private static bool MatchAny(string name, string[] masks)
        {
            for (int i = 0; i < masks.Length; i++)
                if (FileSystemName.MatchesSimpleExpression(masks[i], name, true))
                    return true;
            return false;
        }

        private static string GetRelative(string baseDir, string fullPath)
        {
            string b = baseDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                              + Path.DirectorySeparatorChar;
            return fullPath.StartsWith(b, StringComparison.OrdinalIgnoreCase)
                ? fullPath.Substring(b.Length) : Path.GetFileName(fullPath);
        }

        // ── Plan ──────────────────────────────────────────────────────────────
        /// <summary>Single source of truth for what to do about one destination-conflict
        /// candidate. Shared by PlanActions and the source-picker/drag-drop add paths in
        /// MainWindow.Scan.cs so a future ConflictMode change only has to be taught here.</summary>
        public static FileAction ResolveConflictAction(DateTime srcModified, DateTime? dstModified, bool destExists, ConflictMode mode)
        {
            if (!destExists || dstModified == null) return FileAction.Copy;

            int cmp = DateTime.Compare(srcModified, dstModified.Value);
            bool sameDate = Math.Abs((srcModified - dstModified.Value).TotalSeconds) < 2;

            return mode switch
            {
                ConflictMode.Overwrite        => FileAction.Overwrite,
                ConflictMode.OverwriteIfNewer => sameDate ? FileAction.Skip : (cmp > 0 ? FileAction.Overwrite : FileAction.Skip),
                ConflictMode.OverwriteIfOlder => sameDate ? FileAction.Skip : (cmp < 0 ? FileAction.Overwrite : FileAction.Skip),
                ConflictMode.SkipIfSameDate   => sameDate ? FileAction.Skip : FileAction.Overwrite,
                ConflictMode.SkipAll          => FileAction.Skip,
                ConflictMode.Rename           => FileAction.Rename,
                _                             => FileAction.Skip
            };
        }

        public static void PlanActions(IList<FileItem> items, ConflictMode mode)
        {
            for (int i = 0; i < items.Count; i++)
            {
                var it = items[i];

                // Resume takes precedence — partial transfer found, source unchanged
                if (it.ResumeOffset > 0 && it.ResumeOffset < it.SizeBytes)
                {
                    it.Action = FileAction.Resume;
                    continue;
                }

                it.Action = ResolveConflictAction(it.SourceModified, it.DestModified, it.DestExists, mode);
            }
        }

        // ── Run (parallel-aware) ──────────────────────────────────────────────
        public static async Task RunAsync(
            IList<FileItem> items, CopyOptions opts,
            IProgress<CopyProgress> progress, CancellationToken ct,
            Dispatcher? uiDispatcher = null)
        {
            // L-5: every FileItem this run touches marshals its PropertyChanged raise
            // through this dispatcher from here on (see FileItem.Notify) — closes the
            // cross-thread DataGrid binding violation without changing when the
            // underlying properties are actually (synchronously) written.
            if (uiDispatcher != null)
                foreach (var it0 in items) it0.UiDispatcher = uiDispatcher;

            int buf = Math.Max(64, opts.BufferSizeKb) * 1024;
            long totalBytes = items.Where(x => x.Action != FileAction.Skip)
                                   .Sum(x => Math.Max(0, x.SizeBytes - (x.Action == FileAction.Resume ? x.ResumeOffset : 0)));
            long doneBytes  = 0;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            long lastBytes = 0; long lastMs = 0;
            long bps = 0;
            long lastReportMs = -1000; // negative so the very first report always goes through

            // Shared token bucket — all workers draw from the same rate limit
            var bucket = new TokenBucket { BytesPerSecond = opts.ThrottleBytesPerSec };

            // Parallelism is safe across volumes; same volume = serial by default
            int parallel = Math.Max(1, opts.MaxParallel);

            // Group by (srcVol, dstVol) — same-volume groups run serially internally
            var groups = items.GroupBy(it => DriveAffinity.GetVolumeKey(it.SourcePath)
                                            + "|" + DriveAffinity.GetVolumeKey(it.DestPath))
                              .ToList();

            // Pre-allocated reusable progress envelope. Multiple workers mutate
            // its fields concurrently; the throttled reporter only delivers a
            // snapshot to the UI every ~16 ms via dispatcher (last-write-wins).
            // Aligned 8-byte writes are atomic on x64/ARM64, which is our target.
            var prog = new CopyProgress { TotalFiles = items.Count, BytesTotal = totalBytes };

            void Report(FileItem it, long fileBytes, long fileLen, int idx)
            {
                long ms = sw.ElapsedMilliseconds;
                long lm = Interlocked.Read(ref lastMs);
                long dn = Interlocked.Read(ref doneBytes);
                if (ms - lm >= 250 && Interlocked.CompareExchange(ref lastMs, ms, lm) == lm)
                {
                    long lb = Interlocked.Exchange(ref lastBytes, dn);
                    Interlocked.Exchange(ref bps,
                        (long)((dn - lb) * 1000.0 / Math.Max(1, ms - lm)));
                }

                // L-4: UI dispatch throttle. onChunk() fires on every buffer read —
                // on a fast drive with a small BufferSizeKb that's hundreds to
                // thousands of times per second for one large file, and every one
                // of those used to reach progress.Report() (a dispatcher post for
                // a WPF Progress<T>), which was the actual cause of the "per-file
                // progress unthrottled" UI jank on large transfers. The bps calc
                // above already ran on its own 250ms cadence — it never gated this
                // call. ~20 fps (50ms) is smooth for a progress bar and cuts
                // dispatcher traffic by orders of magnitude. The file-complete
                // report always goes through so the bar never visually sticks
                // below 100%.
                bool isFileComplete = fileLen == 0 || fileBytes >= fileLen;
                if (!isFileComplete)
                {
                    long lr = Interlocked.Read(ref lastReportMs);
                    if (ms - lr < 50) return;
                    if (Interlocked.CompareExchange(ref lastReportMs, ms, lr) != lr) return;
                }
                else
                {
                    Interlocked.Exchange(ref lastReportMs, ms);
                }

                // L-5: per-row progress (bound by GridFiles' ProgressBar/ProgressPct) is written
                // here, gated by the same ~50ms throttle as the aggregate report below, instead
                // of unconditionally on every onChunk() call (previously up to thousands/sec per
                // file). Keeps the row update rate at the same ~20fps the aggregate bar already
                // uses, and confines FileItem mutation to this single throttled path.
                it.ProgressBytes = fileBytes;

                prog.FileIndex        = idx;
                prog.BytesDone        = dn;
                prog.CurrentFile      = it.RelativePath;
                prog.FileProgressPct  = fileLen == 0 ? 100 : fileBytes * 100.0 / fileLen;
                prog.TotalProgressPct = totalBytes == 0 ? 100 : dn * 100.0 / totalBytes;
                prog.BytesPerSec      = Interlocked.Read(ref bps);
                progress?.Report(prog);
            }

            // Pre-compute index map to avoid O(n) IndexOf in hot loop
            var indexMap = new Dictionary<FileItem, int>(items.Count);
            for (int k = 0; k < items.Count; k++) indexMap[items[k]] = k + 1;

            // One worker per group, but cap concurrent groups to MaxParallel.
            // Acquire the semaphore inside the task so dispatch is non-blocking.
            using var slot = new SemaphoreSlim(parallel);
            var tasks = new List<Task>();
            foreach (var grp in groups)
            {
                tasks.Add(Task.Run(async () =>
                {
                    await slot.WaitAsync(ct);
                    try
                    {
                        // Per-group directory cache: each group runs serially so no locking needed.
                        // Avoids a redundant CreateDirectory syscall for every file in the same folder.
                        var createdDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                        // Within one volume pair → strictly serial
                        foreach (var it in grp)
                        {
                            ct.ThrowIfCancellationRequested();
                            int idx = indexMap[it];

                            if (it.Action == FileAction.Skip)
                            {
                                Report(it, 1, 1, idx);
                                continue;
                            }

                            try
                            {
                                await CopyOneAsync(it, opts, buf, bucket, ct,
                                    onChunk: (fb, fl) => Report(it, fb, fl, idx),
                                    addBytes: n => Interlocked.Add(ref doneBytes, n),
                                    createdDirs: createdDirs);
                                // Mark progress complete on success
                                it.ProgressBytes = it.SizeBytes;
                            }
                            catch (OperationCanceledException) { throw; }
                            catch (Exception ex)
                            {
                                it.Action = FileAction.Failed;
                                it.ErrorText = ex.Message;
                            }
                        }
                    }
                    finally { slot.Release(); }
                }, ct));
            }
            await Task.WhenAll(tasks);
        }

        // ── Core single-file copy with resume ─────────────────────────────────
        private static async Task CopyOneAsync(
            FileItem it, CopyOptions opts, int buf, TokenBucket bucket,
            CancellationToken ct,
            Action<long, long> onChunk, Action<long> addBytes,
            HashSet<string>? createdDirs = null)
        {
            string finalPath = it.DestPath;
            if (it.Action == FileAction.Rename && File.Exists(finalPath))
                finalPath = MakeUniquePath(finalPath);

            if (!string.IsNullOrEmpty(opts.DestRoot)
                && !PathIsInside(finalPath, opts.DestRoot))
                throw new IOException($"Refused: target path escapes destination root: {finalPath}");

            string? destDir = Path.GetDirectoryName(finalPath);
            // createdDirs caches which directories have already been created this run (per-group,
            // so no locking needed). Skips redundant CreateDirectory syscalls when many files share
            // the same dest folder — especially meaningful for large flat or shallow trees.
            if (!string.IsNullOrEmpty(destDir) && (createdDirs == null || createdDirs.Add(destDir)))
                Directory.CreateDirectory(destDir);

            // ── Symlink: Preserve mode ────────────────────────────────────────
            if (it.IsSymlink && opts.SymlinkPolicy == SymlinkPolicy.Preserve)
            {
                string? target = File.ResolveLinkTarget(it.SourcePath, returnFinalTarget: false)?.FullName;
                if (string.IsNullOrEmpty(target))
                    throw new IOException($"Cannot resolve symlink target for: {it.SourcePath}");

                // FC-07: replace an existing link (file or directory); never delete a real folder.
                var existing = new FileInfo(finalPath);
                if (existing.Exists || Directory.Exists(finalPath))
                {
                    bool isDir  = Directory.Exists(finalPath);
                    bool isLink = (File.GetAttributes(finalPath) & FileAttributes.ReparsePoint) != 0;
                    if (isDir && !isLink)
                        throw new IOException("A folder with this name already exists at the destination.");
                    if (isDir) Directory.Delete(finalPath);
                    else       File.Delete(finalPath);
                }

                File.CreateSymbolicLink(finalPath, target);
                it.Action = FileAction.Done;
                addBytes(0);
                return;
            }

            // ── Symlink: Follow mode (default) — copy the target's content ───
            // For Skip, PlanActions would have set Action=Skip already.
            string tempPath = PartialFile.GetTempPath(finalPath);
            var srcFi = new FileInfo(it.SourcePath);

            long resumeFrom = 0;
            bool resuming   = false;

            if (it.Action == FileAction.Resume)
            {
                if (opts.ResumePartial)
                {
                    var inspect = PartialFile.TryInspect(finalPath, srcFi);
                    if (inspect.HasValue)
                    {
                        resumeFrom = inspect.Value.ResumeOffset;
                        resuming   = true;
                    }
                    else
                    {
                        PartialFile.DiscardTemp(finalPath);
                        it.Action = FileAction.Copy;
                    }
                }
                else
                {
                    PartialFile.DiscardTemp(finalPath);
                    it.Action = FileAction.Copy;
                }
            }
            else if (File.Exists(tempPath))
            {
                // Stale temp from prior run with no matching plan entry
                PartialFile.DiscardTemp(finalPath);
            }

            byte[] buffer = ArrayPool<byte>.Shared.Rent(buf);

            // CRC strategy on resume:
            //
            // System.IO.Hashing.Crc32 cannot be seeded from a stored value, so we
            // CAN'T cheaply continue an in-progress hash across an app restart.
            // Two options:
            //   (a) re-read the entire partial to rebuild state, then continue
            //   (b) skip the resume-time partial hash; rely on post-copy verify
            //
            // We use (b): if VerifyCrc is on, the post-copy ComputeCrcAsync over
            // the FINAL file already detects any corruption. The price is one
            // dest-side full read at the end (which we'd do anyway). Net: resume
            // is cheap; verification is unchanged.
            //
            // SourceCrc is computed over the new bytes we read this run only when
            // not resuming; on resume we leave SourceCrc null and let the post-
            // copy DestCrc be the verified hash.
            // FC-01: Move (DeleteSource) always verifies — engine invariant, independent of UI/job flags.
            bool verify = opts.VerifyCrc || opts.DeleteSource;
            Crc32? crc = (verify && !resuming) ? new Crc32() : null;

            try
            {
                using (var src = new FileStream(it.SourcePath, FileMode.Open, FileAccess.Read, FileShare.Read, buf,
                                                FileOptions.SequentialScan | FileOptions.Asynchronous))
                using (var dst = new FileStream(tempPath, resuming ? FileMode.Open : FileMode.Create,
                                                FileAccess.Write, FileShare.None, buf,
                                                FileOptions.SequentialScan | FileOptions.Asynchronous))
                {
                    if (resuming)
                    {
                        // Truncate any bytes written past the last meta-flushed offset
                        dst.SetLength(resumeFrom);
                        dst.Position = resumeFrom;
                        src.Position = resumeFrom;
                        // totalBytes already excludes resumeFrom for Resume items;
                        // do NOT call addBytes here or doneBytes will overshoot totalBytes.
                    }

                    long fileBytes = resumeFrom;
                    long fileLen = it.SizeBytes;
                    int read;
                    long sinceFlush = 0;
                    while ((read = await src.ReadAsync(buffer.AsMemory(0, buf), ct)) > 0)
                    {
                        opts.Pause?.Wait(ct);
                        await bucket.ConsumeAsync(read, ct);
                        await dst.WriteAsync(buffer.AsMemory(0, read), ct);
                        crc?.Append(buffer.AsSpan(0, read));
                        fileBytes += read;
                        addBytes(read);
                        sinceFlush += read;

                        // Periodic meta flush so a crash leaves a verifiable resume point.
                        // 64 MB: on NVMe at 3 GB/s a 16 MB threshold fires every ~5 ms (too often);
                        // 64 MB keeps safety checkpoints at ~21 ms intervals for fast drives while
                        // still giving HDD users a crash recovery point roughly every 640 ms.
                        if (sinceFlush >= 64 * 1024 * 1024)
                        {
                            await dst.FlushAsync(ct);
                            uint partial = crc != null ? BitConverter.ToUInt32(crc.GetCurrentHash(), 0) : 0u;
                            await PartialFile.WriteMetaAsync(finalPath, srcFi, partial, fileBytes, ct);
                            sinceFlush = 0;
                        }

                        onChunk(fileBytes, fileLen);
                    }

                    await dst.FlushAsync(ct);
                    // FC-03: the source is deleted after this file — make the copy durable first.
                    if (opts.DeleteSource) dst.Flush(flushToDisk: true);
                    if (crc != null)
                        it.SourceCrc = Convert.ToHexString(crc.GetCurrentHash());
                }

                // Temp == final payload. Promote atomically and drop sidecar.
                PartialFile.Finalize(finalPath);

                if (opts.PreserveTimestamps)
                {
                    File.SetCreationTimeUtc (finalPath, srcFi.CreationTimeUtc);
                    File.SetLastWriteTimeUtc(finalPath, srcFi.LastWriteTimeUtc);
                    File.SetLastAccessTimeUtc(finalPath, srcFi.LastAccessTimeUtc);
                }
                if (opts.PreserveAttributes)
                    File.SetAttributes(finalPath, srcFi.Attributes & ~FileAttributes.ReadOnly);

                if (verify)
                {
                    it.Action = FileAction.Verify;
                    it.DestCrc = await ComputeCrcAsync(finalPath, buf, ct);
                    // On resume we didn't accumulate SourceCrc during copy;
                    // hash the source independently for verification.
                    if (string.IsNullOrEmpty(it.SourceCrc))
                        it.SourceCrc = await ComputeCrcAsync(it.SourcePath, buf, ct);
                    if (!string.Equals(it.SourceCrc, it.DestCrc, StringComparison.OrdinalIgnoreCase))
                    {
                        it.Action = FileAction.Failed;
                        it.ErrorText = "CRC mismatch";
                        return;
                    }
                    it.Action = FileAction.Verified;

                    // Optionally write a .crc32 sidecar alongside the destination file
                    if (opts.WriteCrcSidecar)
                    {
                        try
                        {
                            string sidecar  = finalPath + ".crc32";
                            var    asm      = System.Reflection.Assembly.GetExecutingAssembly();
                            string ver      = asm.GetName().Version?.ToString() ?? "?";
                            string header   = $"; Generated by File Copy Studio {ver} on {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC\n";
                            string entry    = $"{it.DestCrc.ToLowerInvariant()}  {Path.GetFileName(finalPath)}\n";
                            await File.WriteAllTextAsync(sidecar, header + entry, ct);
                        }
                        catch { /* best effort */ }
                    }
                }
                else it.Action = FileAction.Done;

                if (opts.DeleteSource && it.Action == FileAction.Verified)
                {
                    try { File.Delete(it.SourcePath); }
                    catch (Exception ex)
                    {
                        it.ErrorText = "Delete src: " + ex.Message;
                        it.Action    = FileAction.Failed; // surfaces in error panel
                    }
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }

        public static async Task<string> ComputeCrcAsync(string path, int buf, CancellationToken ct)
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, buf,
                                          FileOptions.SequentialScan | FileOptions.Asynchronous);
            var crc = new Crc32();
            byte[] b = ArrayPool<byte>.Shared.Rent(buf);
            try
            {
                int r;
                while ((r = await fs.ReadAsync(b.AsMemory(0, buf), ct)) > 0)
                    crc.Append(b.AsSpan(0, r));
                return Convert.ToHexString(crc.GetCurrentHash());
            }
            finally { ArrayPool<byte>.Shared.Return(b); }
        }

        // ── Verification-only mode ────────────────────────────────────────────
        /// <summary>
        /// Walks each item and CRC-compares source vs destination without writing
        /// anything. Mismatch / missing-dest items end as Failed with explanatory
        /// ErrorText. Matching items end as Verified.
        /// </summary>
        public static async Task VerifyOnlyAsync(
            IList<FileItem> items, int bufferSizeKb,
            IProgress<CopyProgress> progress, CancellationToken ct,
            Dispatcher? uiDispatcher = null)
        {
            // L-5: see RunAsync — same marshaling rationale applies here (this method also
            // mutates FileItem.Action/ErrorText/SourceCrc/DestCrc off the UI thread, via the
            // Task.Run wrapper CopyOrchestrator.VerifyAsync uses).
            if (uiDispatcher != null)
                foreach (var it0 in items) it0.UiDispatcher = uiDispatcher;

            int buf = Math.Max(64, bufferSizeKb) * 1024;
            // Each file is hashed twice (src + dst), so total work = 2× sum of sizes
            long totalBytes = items.Sum(x => x.SizeBytes) * 2;
            long doneBytes  = 0;
            var prog = new CopyProgress { TotalFiles = items.Count, BytesTotal = totalBytes };
            var sw = System.Diagnostics.Stopwatch.StartNew();
            long lastReportMs = -1000; // negative so the very first report always goes through

            for (int i = 0; i < items.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                var it = items[i];

                if (!File.Exists(it.SourcePath))
                {
                    it.Action    = FileAction.Failed;
                    it.ErrorText = "Source missing";
                    doneBytes   += it.SizeBytes * 2;
                    continue;
                }

                if (!File.Exists(it.DestPath))
                {
                    it.Action    = FileAction.Failed;
                    it.ErrorText = "Destination missing";
                    doneBytes   += it.SizeBytes * 2;
                    continue;
                }

                void Report(long fileDone, long fileLen)
                {
                    // L-4: same UI dispatch throttle as RunAsync's Report() — this
                    // fires per chunk read via ComputeCrcWithProgressAsync, which
                    // was unthrottled and caused the same large-transfer UI jank
                    // during verification. Always let the file-complete report
                    // through (fileDone covers both the src and dst passes, so
                    // "complete" here means fully done with both, i.e. == fileLen*2).
                    long ms = sw.ElapsedMilliseconds;
                    bool isFileComplete = fileLen == 0 || fileDone >= fileLen * 2;
                    if (!isFileComplete)
                    {
                        long lr = Interlocked.Read(ref lastReportMs);
                        if (ms - lr < 50) return;
                        if (Interlocked.CompareExchange(ref lastReportMs, ms, lr) != lr) return;
                    }
                    else
                    {
                        Interlocked.Exchange(ref lastReportMs, ms);
                    }

                    prog.FileIndex        = i + 1;
                    prog.BytesDone        = doneBytes + fileDone;
                    prog.CurrentFile      = it.RelativePath;
                    prog.FileProgressPct  = fileLen == 0 ? 50 : (fileDone * 100.0 / (fileLen * 2));
                    prog.TotalProgressPct = totalBytes == 0 ? 100 : prog.BytesDone * 100.0 / totalBytes;
                    progress?.Report(prog);
                }

                try
                {
                    it.Action = FileAction.Verify;
                    it.SourceCrc = await ComputeCrcWithProgressAsync(it.SourcePath, buf, ct,
                        (r) => Report(r, it.SizeBytes));
                    it.DestCrc = await ComputeCrcWithProgressAsync(it.DestPath, buf, ct,
                        (r) => Report(it.SizeBytes + r, it.SizeBytes));

                    if (string.Equals(it.SourceCrc, it.DestCrc, StringComparison.OrdinalIgnoreCase))
                        it.Action = FileAction.Verified;
                    else
                    { it.Action = FileAction.Failed; it.ErrorText = "CRC mismatch"; }
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex) { it.Action = FileAction.Failed; it.ErrorText = ex.Message; }

                doneBytes += it.SizeBytes * 2;
            }
        }

        private static async Task<string> ComputeCrcWithProgressAsync(
            string path, int buf, CancellationToken ct, Action<long> onBytes)
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, buf,
                                          FileOptions.SequentialScan | FileOptions.Asynchronous);
            var  crc      = new Crc32();
            byte[] b      = ArrayPool<byte>.Shared.Rent(buf);
            long read     = 0;
            long lastReport = 0;
            // Cap callbacks to ~60fps regardless of buffer size.
            // At 1MB buffer and 3GB/s NVMe this would fire 3000+ times/sec without throttle.
            const long ReportIntervalBytes = 4L * 1024 * 1024; // report every 4 MB
            try
            {
                int r;
                while ((r = await fs.ReadAsync(b.AsMemory(0, buf), ct)) > 0)
                {
                    crc.Append(b.AsSpan(0, r));
                    read += r;
                    if (read - lastReport >= ReportIntervalBytes)
                    {
                        onBytes(read);
                        lastReport = read;
                    }
                }
                onBytes(read); // final update
                return Convert.ToHexString(crc.GetCurrentHash());
            }
            finally { ArrayPool<byte>.Shared.Return(b); }
        }

        private static string MakeUniquePath(string path)
        {
            string dir  = Path.GetDirectoryName(path) ?? "";
            string stem = Path.GetFileNameWithoutExtension(path);
            string ext  = Path.GetExtension(path);
            for (int n = 1; n < 10000; n++)
            {
                string p = Path.Combine(dir, $"{stem} ({n}){ext}");
                if (!File.Exists(p)) return p;
            }
            throw new IOException($"Could not allocate a unique name for {path} after 10000 attempts.");
        }

        // ── Path traversal guard helpers ──────────────────────────────────────
        private static bool PathIsInside(string candidate, string root)
        {
            try
            {
                string c = Path.GetFullPath(candidate)
                              .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                string r = Path.GetFullPath(root)
                              .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                          + Path.DirectorySeparatorChar;
                return c.StartsWith(r, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(c + Path.DirectorySeparatorChar, r, StringComparison.OrdinalIgnoreCase);
            }
            catch { return false; }
        }
    }
}
