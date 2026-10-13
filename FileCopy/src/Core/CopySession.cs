using System;
using System.Collections.Generic;
using System.Linq;

namespace FileCopy
{
    /// <summary>
    /// Immutable snapshot of everything needed to execute one copy run.
    /// Created from AppPrefs for manual runs, or directly from SchedulerJob
    /// for scheduled runs — the scheduler never needs to mutate AppPrefs.
    ///
    /// Passed through ScanAsync and RunCopyAsync so each run is fully
    /// self-contained and isolated from UI state changes mid-run.
    /// </summary>
    public sealed record CopySession
    {
        // ── Paths ─────────────────────────────────────────────────────────────
        /// <summary>First entry of SourcePaths, or "" — kept for legacy single-source call sites.</summary>
        public string SourcePath    { get; init; } = "";
        public string DestPath      { get; init; } = "";
        /// <summary>One or more source roots. When &gt;1, each root is mirrored under
        /// Dest\&lt;leaf-name&gt;\... to avoid collisions between roots.</summary>
        public IReadOnlyList<string> SourcePaths { get; init; } = Array.Empty<string>();

        // ── Scan options ──────────────────────────────────────────────────────
        public bool         IncludeSubdirs    { get; init; } = true;
        public string       FileMaskInclude   { get; init; } = "*.*";
        public string       FileMaskExclude   { get; init; } = "";
        public SymlinkPolicy SymlinkPolicy    { get; init; } = SymlinkPolicy.Follow;

        // ── Copy options ──────────────────────────────────────────────────────
        public ConflictMode ConflictMode      { get; init; } = ConflictMode.OverwriteIfNewer;
        public bool         PreserveTimestamps{ get; init; } = true;
        public bool         PreserveAttributes{ get; init; } = true;
        public bool         VerifyCrc         { get; init; } = false;
        public bool         WriteCrcSidecar   { get; init; } = false;
        public bool         DeleteSourceOnMove{ get; init; } = false;
        public bool         ResumePartial     { get; init; } = true;
        public int          BufferSizeKb      { get; init; } = 1024;
        public int          MaxParallel       { get; init; } = 1;
        public bool         AutoParallelByDrive { get; init; } = true;
        public long         ThrottleMbps      { get; init; } = 0;

        // ── UI / behaviour ────────────────────────────────────────────────────
        public bool MinimizeToTray      { get; init; } = false;
        public bool PlayCompletionSound { get; init; } = true;
        public bool OpenDestOnComplete  { get; init; } = false;
        public bool PauseOnBattery      { get; init; } = false;
        /// <summary>True when this session was created by the scheduler — suppresses interactive confirmation dialogs.</summary>
        public bool IsScheduled         { get; init; } = false;

        // NOTE: FromPrefs and FromJob below are two independent field-by-field mappings
        // into this record. Nothing enforces that a new CopySession property gets wired
        // into both — e.g. SymlinkPolicy is intentionally hardcoded to Follow in FromJob
        // (SchedulerJob has no SymlinkPolicy field) rather than an oversight. When adding
        // a CopySession property, update both factories (or document why one intentionally
        // omits it, as above).

        /// <summary>Snapshot current AppPrefs into a CopySession.</summary>
        public static CopySession FromPrefs(AppPrefs p)
        {
            var roots = (p.SourceList != null && p.SourceList.Count > 0)
                ? p.SourceList.ToArray()
                : (string.IsNullOrWhiteSpace(p.SourcePath) ? Array.Empty<string>() : new[] { p.SourcePath });
            return new CopySession
            {
            SourcePath          = roots.Length > 0 ? roots[0] : "",
            SourcePaths         = roots,
            DestPath            = p.DestPath,
            IncludeSubdirs      = p.IncludeSubdirs,
            FileMaskInclude     = p.FileMaskInclude,
            FileMaskExclude     = p.FileMaskExclude,
            SymlinkPolicy       = p.SymlinkPolicy,
            ConflictMode        = p.ConflictMode,
            PreserveTimestamps  = p.PreserveTimestamps,
            PreserveAttributes  = p.PreserveAttributes,
            VerifyCrc           = p.VerifyCrc,
            WriteCrcSidecar     = p.WriteCrcSidecar,
            DeleteSourceOnMove  = p.DeleteSourceOnMove,
            ResumePartial       = p.ResumePartial,
            BufferSizeKb        = p.BufferSizeKb,
            MaxParallel         = p.MaxParallel,
            AutoParallelByDrive = p.AutoParallelByDrive,
            ThrottleMbps        = p.ThrottleMbps,
            MinimizeToTray      = p.MinimizeToTray,
            PlayCompletionSound = p.PlayCompletionSound,
            OpenDestOnComplete  = p.OpenDestOnComplete,
            PauseOnBattery      = p.PauseOnBattery
            };
        }

        /// <summary>Build a CopySession from a SchedulerJob, applying safe defaults for unset fields.</summary>
        public static CopySession FromJob(SchedulerJob job) => new CopySession
        {
            SourcePath          = job.SourcePath,
            SourcePaths         = string.IsNullOrWhiteSpace(job.SourcePath) ? Array.Empty<string>() : new[] { job.SourcePath },
            DestPath            = job.DestPath,
            IncludeSubdirs      = job.IncludeSubdirs ?? true,
            FileMaskInclude     = string.IsNullOrWhiteSpace(job.FileMaskInclude) ? "*.*" : job.FileMaskInclude,
            FileMaskExclude     = job.FileMaskExclude ?? "",
            SymlinkPolicy       = SymlinkPolicy.Follow,
            ConflictMode        = job.ConflictMode        ?? ConflictMode.OverwriteIfNewer,
            PreserveTimestamps  = true,
            PreserveAttributes  = true,
            VerifyCrc           = job.VerifyCrc           ?? false,
            WriteCrcSidecar     = false,
            DeleteSourceOnMove  = job.DeleteSourceOnMove  ?? false,
            ResumePartial       = true,
            BufferSizeKb        = 1024,
            MaxParallel         = 1,
            AutoParallelByDrive = true,
            ThrottleMbps        = 0,
            MinimizeToTray      = false,
            PlayCompletionSound = false,  // scheduled jobs don't play sounds by default
            OpenDestOnComplete  = false,
            PauseOnBattery      = false,
            IsScheduled         = true
        };
    }
}
