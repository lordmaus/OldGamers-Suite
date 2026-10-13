using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Text;
using System.Windows;
using Microsoft.Extensions.Logging;

#nullable enable


namespace FileCopy
{
    public partial class MainWindow
    {
        private void CheckMissedJobs()
        {
            var now = DateTime.Now;
            var missed = _scheduler.Jobs
                .Where(j => j.IsEnabled
                         && j.NextRun.HasValue
                         && j.NextRun.Value < now
                         && j.Frequency != ScheduleFrequency.Once)
                .ToList();

            if (missed.Count == 0) return;

            string list = string.Join("\n", missed.Select(j =>
                $"  • {j.Label}  (was due {j.NextRun.Value:yyyy-MM-dd HH:mm})"));

            var r = AppDialog.Show(this,
                $"{missed.Count} scheduled job(s) were missed while the app was closed:\n\n{list}\n\nRun them now?",
                "Missed scheduled jobs", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.Yes);

            if (r != MessageBoxResult.Yes)
            {
                // Advance next run so they don't fire immediately via the tick loop
                foreach (var j in missed)
                    _scheduler.AdvanceNextRun(j.Id);
                return;
            }

            foreach (var j in missed)
                _scheduler.FireNow(j.Id);
        }

        private void BtnSchedule_Click(object sender, RoutedEventArgs e)
        {
            var win = new SchedulerWindow(_scheduler) { Owner = this };
            win.ShowDialog();
        }

        private async void OnSchedulerJobFired(object sender, SchedulerJob job)
        {
            if (IsRunning)
            {
                _log.LogWarning("Scheduler: job \"{Label}\" skipped — copy running.", job.Label);
                job.LastResult = "Skipped (copy in progress)";
                // Still advance NextRun so it doesn't re-fire every tick
                var next = job.ComputeNextRun(DateTime.Now);
                job.NextRun = next == DateTime.MaxValue ? null : next;
                _scheduler.Save();
                return;
            }

            var session = CopySession.FromJob(job);
            _log.LogInformation("Scheduler: starting \"{Label}\" | Conflict={Conflict} Verify={Verify}",
                job.Label, session.ConflictMode, session.VerifyCrc);
            Vm.StatusText = $"Scheduler: running '{job.Label}'\u2026";

            try
            {
                await ScanAsync(session);
                _log.LogInformation("Scheduler: scan — {Count} files for \"{Label}\"", _items.Count, job.Label);

                if (_items.Count > 0)
                {
                    await RunCopyAsync(session);
                    var snapshot = _items.ToList();
                    int errors = _errors.Count;
                    int done   = snapshot.Count(x => x.Action is FileAction.Done or FileAction.Verified);
                    _log.LogInformation("Scheduler: \"{Label}\" done — {Done} copied, {Errors} error(s)", job.Label, done, errors);
                    string result = errors > 0 ? $"{done} copied, {errors} error(s)" : $"{done} file(s) OK";
                    // Route through RecordResult so the write is under the engine's lock
                    // (prevents race with CheckAndFire's concurrent Save())
                    _scheduler.RecordResult(job.Id, result);
                    WriteJobLog(job, snapshot);

                    // Scheduler jobs run silently — always notify regardless of window state
                    string msg  = errors > 0 ? $"{done} copied, {errors} error(s)." : $"{done} file(s) copied OK.";
                    _tray?.ShowBalloon($"Scheduler: {job.Label}", msg,
                        errors > 0 ? System.Windows.Forms.ToolTipIcon.Warning
                                   : System.Windows.Forms.ToolTipIcon.Info);
                    System.Media.SystemSounds.Asterisk.Play();
                }
                else
                {
                    _log.LogInformation("Scheduler: \"{Label}\" — nothing to copy", job.Label);
                    _scheduler.RecordResult(job.Id, "Nothing to copy");
                    WriteJobLog(job, new System.Collections.Generic.List<FileItem>());
                }
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Scheduler: job \"{Label}\" threw", job.Label);
                _scheduler.RecordResult(job.Id, "Error: " + ex.Message);

                // Feature Gap #3 (FileCopy_Full_Review_2026-09-07): the success/per-file-error
                // path below already balloons + plays a sound on every scheduled run (silent-
                // job notification exists), but a job that throws before reaching that path —
                // e.g. a dropped network share, an access-denied on the source root — fell
                // through to only a log line no one watching. Scheduled jobs run unattended by
                // design, so this is the one path that genuinely needs the same visibility.
                _tray?.ShowBalloon($"Scheduler: {job.Label} — failed", ex.Message,
                    System.Windows.Forms.ToolTipIcon.Error);
                System.Media.SystemSounds.Hand.Play();
            }
            // AppPrefs is NEVER touched — session was fully isolated via CopySession.FromJob
            _scheduler.Save();
        }

        private void WriteJobLog(SchedulerJob job, System.Collections.Generic.List<FileItem> snapshot)
        {
            try
            {
                PortableMode.EnsureDataDir();
                var sb = new System.Text.StringBuilder();
                string runTime = job.LastRun.HasValue ? job.LastRun.Value.ToString("yyyy-MM-dd HH:mm:ss") : DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

                sb.AppendLine($"════════════════════════════════════════════════════════");
                sb.AppendLine($"  Job      : {job.Label}");
                sb.AppendLine($"  Run at   : {runTime}");
                sb.AppendLine($"  Source   : {job.SourcePath}");
                sb.AppendLine($"  Dest     : {job.DestPath}");
                sb.AppendLine($"  Schedule : {job.FrequencyDisplay}");
                sb.AppendLine($"  Result   : {job.LastResult}");
                sb.AppendLine($"════════════════════════════════════════════════════════");
                sb.AppendLine();

                // Group by action for a clean summary
                var groups = snapshot.GroupBy(x => x.Action).OrderBy(g => g.Key.ToString());
                foreach (var grp in groups)
                {
                    string actionLabel = grp.Key switch
                    {
                        FileAction.Done      => "✓ Copied",
                        FileAction.Verified  => "✓ Copied + Verified",
                        FileAction.Skip      => "⊘ Skipped",
                        FileAction.Overwrite => "↻ Overwritten",
                        FileAction.Rename    => "✎ Renamed",
                        FileAction.Resume    => "▶ Resumed",
                        FileAction.Failed    => "⚠ Failed",
                        _                    => grp.Key.ToString()
                    };
                    sb.AppendLine($"── {actionLabel} ({grp.Count():N0}) ──");
                    foreach (var it in grp)
                    {
                        string size = SizeFormat.Format(it.SizeBytes);
                        if (!string.IsNullOrEmpty(it.ErrorText))
                            sb.AppendLine($"  {it.RelativePath}  [{size}]  ERROR: {it.ErrorText}");
                        else
                            sb.AppendLine($"  {it.RelativePath}  [{size}]");
                    }
                    sb.AppendLine();
                }

                // Append (not overwrite) so history accumulates
                File.AppendAllText(job.JobLogPath, sb.ToString(), System.Text.Encoding.UTF8);
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Could not write job log for {Label}", job.Label);
            }
        }

    }
}
