using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;

#nullable enable


namespace FileCopy
{
    /// <summary>
    /// Abstraction over the static CopyEngine methods, enabling dependency
    /// injection and unit-testing without hitting the real file system.
    /// </summary>
    public interface ICopyEngine
    {
        List<FileItem> Enumerate(
            string source, string dest, bool recurse,
            string includeMask, string excludeMask,
            CancellationToken ct,
            SymlinkPolicy symlinkPolicy = SymlinkPolicy.Follow,
            IProgress<int>? scanProgress = null,
            ICollection<string>? skipped = null);

        void PlanActions(IList<FileItem> items, ConflictMode mode);

        Task RunAsync(
            IList<FileItem> items, CopyEngine.CopyOptions opts,
            IProgress<CopyProgress> progress, CancellationToken ct,
            Dispatcher? uiDispatcher = null);

        Task VerifyOnlyAsync(
            IList<FileItem> items, int bufferSizeKb,
            IProgress<CopyProgress> progress, CancellationToken ct,
            Dispatcher? uiDispatcher = null);
    }

    /// <summary>
    /// Default implementation: delegates straight to the static CopyEngine.
    /// Production code uses this; tests can substitute a fake.
    /// </summary>
    public sealed class DefaultCopyEngine : ICopyEngine
    {
        public static readonly DefaultCopyEngine Instance = new DefaultCopyEngine();

        public List<FileItem> Enumerate(string source, string dest, bool recurse,
            string includeMask, string excludeMask, CancellationToken ct,
            SymlinkPolicy symlinkPolicy = SymlinkPolicy.Follow,
            IProgress<int>? scanProgress = null,
            ICollection<string>? skipped = null)
            => CopyEngine.Enumerate(source, dest, recurse, includeMask, excludeMask, ct, symlinkPolicy, scanProgress, skipped);

        public void PlanActions(IList<FileItem> items, ConflictMode mode)
            => CopyEngine.PlanActions(items, mode);

        public Task RunAsync(IList<FileItem> items, CopyEngine.CopyOptions opts,
            IProgress<CopyProgress> progress, CancellationToken ct,
            Dispatcher? uiDispatcher = null)
            => CopyEngine.RunAsync(items, opts, progress, ct, uiDispatcher);

        public Task VerifyOnlyAsync(IList<FileItem> items, int bufferSizeKb,
            IProgress<CopyProgress> progress, CancellationToken ct,
            Dispatcher? uiDispatcher = null)
            => CopyEngine.VerifyOnlyAsync(items, bufferSizeKb, progress, ct, uiDispatcher);
    }
}
