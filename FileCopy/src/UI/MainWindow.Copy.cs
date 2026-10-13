using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using Microsoft.Extensions.Logging;

#nullable enable


namespace FileCopy
{
    public partial class MainWindow
    {
        // ── Run / Verify / Retry ──────────────────────────────────────────────
        private async Task VerifyOnlyAsync()
        {
            if (_items.Count == 0)
            {
                await ScanAsync();
                if (_items.Count == 0) return;
            }
            await _orchestrator.VerifyAsync(_items,
                _activeSession?.BufferSizeKb ?? AppPrefs.Instance.BufferSizeKb);

            ShowFolderComparison();
        }

        /// <summary>
        /// After verify: show a summary comparing source vs destination counts and sizes.
        /// Surfaces mismatches that CRC-passed individually but differ in total inventory.
        /// </summary>
        private void ShowFolderComparison()
        {
            if (_items.Count == 0) return;

            int  srcCount = _items.Count;
            long srcBytes = 0, destBytes = 0;
            int  verified = 0, failed = 0, skipped = 0;
            foreach (var x in _items)
            {
                srcBytes += x.SizeBytes;
                switch (x.Action)
                {
                    case FileAction.Verified: verified++; destBytes += x.SizeBytes; break;
                    case FileAction.Failed:   failed++;   break;
                    case FileAction.Skip:     skipped++;  break;
                }
            }

            string srcSize  = SizeFormat.Format(srcBytes);
            string dstSize  = SizeFormat.Format(destBytes);
            bool   mismatch = failed > 0;

            string summary =
                $"Source:      {srcCount:N0} files  ({srcSize})\n" +
                $"Verified:    {verified:N0} files  ({dstSize})\n" +
                $"Failed:      {failed:N0}\n" +
                $"Skipped:     {skipped:N0}";

            if (!mismatch)
                Vm.StatusText = $"✓ Verify complete — {verified:N0}/{srcCount:N0} files OK  ({srcSize})";
            else
                Vm.StatusText = $"⚠ Verify complete — {failed:N0} mismatch(es) out of {srcCount:N0} files";

            _log.LogInformation("Verify summary: {Src} files/{SrcBytes} → {Ver} verified, {Fail} failed, {Skip} skipped",
                srcCount, srcBytes, verified, failed, skipped);
        }

        private async Task RunCopyAsync(CopySession? session = null)
        {
            session ??= CopySession.FromPrefs(AppPrefs.Instance);

            if (!ValidatePaths(session)) return;
            if (_items.Count == 0) { AppDialog.Show(this, "Run Scan first.", "File Copy Studio"); return; }

            // Size estimate + move confirmations are interactive — skip for scheduled runs
            if (!session.IsScheduled)
            {
                var toCopy  = _items.Where(x => x.Action is FileAction.Copy or FileAction.Overwrite
                                              or FileAction.Rename or FileAction.Resume).ToList();
                long totalBytes = toCopy.Sum(x => Math.Max(0, x.SizeBytes - x.ResumeOffset));
                const long LargeThreshold = 1L * 1024 * 1024 * 1024; // 1 GB
                if (totalBytes >= LargeThreshold)
                {
                    var r = AppDialog.Show(this,
                        $"This operation will copy {toCopy.Count:N0} file(s) totalling {SizeFormat.Format(totalBytes)}.\n\nProceed?",
                        "Large copy — confirm", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.Yes);
                    if (r != MessageBoxResult.Yes) return;
                }

                if (session.DeleteSourceOnMove)
                {
                    int moveCount = _items.Count(x => x.Action != FileAction.Skip);
                    var r = AppDialog.Show(this,
                        $"Delete source (Move) is enabled.\n\n" +
                        $"{moveCount:N0} file(s) will be deleted from source after copying.\n\n" +
                        $"Source: {session.SourcePath}\n\nThis cannot be undone. Proceed?",
                        "Confirm Move", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
                    if (r != MessageBoxResult.Yes) return;
                }
            }

            if (session.MinimizeToTray) _tray?.MinimizeToTray();

            int parallel = session.AutoParallelByDrive
                ? DriveAffinity.RecommendedParallel(session.SourcePath, session.DestPath, session.MaxParallel)
                : session.MaxParallel;

            var opts = new CopyEngine.CopyOptions
            {
                PreserveTimestamps  = session.PreserveTimestamps,
                PreserveAttributes  = session.PreserveAttributes,
                VerifyCrc           = session.VerifyCrc,
                DeleteSource        = session.DeleteSourceOnMove,
                BufferSizeKb        = session.BufferSizeKb,
                ResumePartial       = session.ResumePartial,
                MaxParallel         = parallel,
                DestRoot            = session.DestPath,
                ThrottleBytesPerSec = session.ThrottleMbps > 0
                    ? session.ThrottleMbps * 1024L * 1024L / 8L : 0,
                WriteCrcSidecar     = session.WriteCrcSidecar,
                SymlinkPolicy       = session.SymlinkPolicy,
                Pause               = _orchestrator.PauseLatch
            };

            await _orchestrator.RunCopyAsync(_items, opts);
        }

        // ── Progress / Completed ──────────────────────────────────────────────
        private void OnProgress(CopyProgress pr)
        {
            Vm.FileProgressPct  = pr.FileProgressPct;
            Vm.TotalProgressPct = pr.TotalProgressPct;
            Vm.StatusText       = $"[{pr.FileIndex}/{pr.TotalFiles}] {pr.CurrentFile}";

            string speed = pr.BytesPerSec > 0 ? SizeFormat.Format(pr.BytesPerSec) + "/s" : "";
            long throttleMbps = _activeSession?.ThrottleMbps ?? AppPrefs.Instance.ThrottleMbps;
            if (throttleMbps > 0 && speed.Length > 0) speed += $"  (throttled to {throttleMbps} Mbps)";
            Vm.SpeedText = speed;
            UpdateSparkline(pr.BytesPerSec);

            Title = $"File Copy Studio [{pr.TotalProgressPct:0}%]";
        }

        private void OnCopyCompleted(bool cleanFinish)
        {
            // Capture session before clearing — PlayCompletionSound/OpenDestOnComplete read it below
            var session = _activeSession;
            _activeSession = null; // session is done — UI combos revert to AppPrefs
            ClearSparkline();
            Title = PortableMode.Active ? "File Copy Studio  [Portable]" : "File Copy Studio";
            if (!cleanFinish) return;

            int failed = _errors.Count;
            // Show balloon only when the window is minimised, hidden in tray, or not the foreground window
            bool windowInBackground = _tray?.IsInTray == true || !IsActive;
            if (windowInBackground)
            {
                string msg = failed > 0
                    ? $"{failed:N0} error(s) — click to review."
                    : $"{_items.Count:N0} files transferred.";
                _tray?.ShowBalloon("File Copy Studio — complete", msg,
                    failed > 0 ? System.Windows.Forms.ToolTipIcon.Warning : System.Windows.Forms.ToolTipIcon.Info);
            }
            if (session?.PlayCompletionSound ?? AppPrefs.Instance.PlayCompletionSound)
            {
                if (failed > 0) System.Media.SystemSounds.Exclamation.Play();
                else            System.Media.SystemSounds.Asterisk.Play();
            }
            if (failed == 0 && (session?.OpenDestOnComplete ?? AppPrefs.Instance.OpenDestOnComplete))
                RevealInExplorer(session?.DestPath ?? AppPrefs.Instance.DestPath ?? "");
        }

        private static void RevealInExplorer(string path)
        {
            if (string.IsNullOrEmpty(path)) return;
            try
            {
                // ArgumentList, not an interpolated "/select,\"...\"" string — a path containing
                // an embedded quote (reachable via a loaded .fclist job file or a scheduler.json
                // entry, not just manual typing) can otherwise break out of the quoted segment and
                // inject extra explorer.exe arguments. .NET handles native quoting per-element.
                var psi = new System.Diagnostics.ProcessStartInfo("explorer.exe")
                    { UseShellExecute = true };
                psi.ArgumentList.Add($"/select,{path.TrimEnd('\\', '/')}");
                System.Diagnostics.Process.Start(psi);
            }
            catch (Exception ex)
            {
                AppLogger.ForContext<MainWindow>().LogWarning(ex, "RevealInExplorer failed for {Path}", path);
            }
        }
    }
}
