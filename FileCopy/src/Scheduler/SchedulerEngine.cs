using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Extensions.Logging;

#nullable enable


namespace FileCopy
{
    /// <summary>
    /// Loads, persists, and executes SchedulerJobs.
    /// Runs a 30-second background tick to check for due jobs.
    /// Thread-safe: all public methods are callable from any thread.
    /// </summary>
    public sealed class SchedulerEngine : IDisposable
    {
        private static readonly ILogger _log = AppLogger.ForCategory("SchedulerEngine");

        private readonly string _filePath;
        private readonly System.Windows.Threading.Dispatcher _dispatcher;
        private readonly List<SchedulerJob> _jobs = new();
        private readonly object _lock = new();
        private CancellationTokenSource _cts;
        private Task _tick;

        public event EventHandler<SchedulerJob>? JobFired;

        private static string SanitizePath(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return "";
            if (raw.Length > 4096)       return "";
            if (raw.IndexOf('\0') >= 0)  return "";
            return raw.Trim('"', ' ');
        }

        private static string DefaultPath =>
            Path.Combine(PortableMode.DataDir, "scheduler.json");

        public SchedulerEngine(System.Windows.Threading.Dispatcher dispatcher, string? path = null)
        {
            _dispatcher = dispatcher;
            _filePath   = path ?? DefaultPath;
            Load();
        }

        // ── Jobs list ─────────────────────────────────────────────────────────
        public IReadOnlyList<SchedulerJob> Jobs { get { lock (_lock) return _jobs.ToList(); } }

        public void AddOrUpdate(SchedulerJob job)
        {
            lock (_lock)
            {
                int idx = _jobs.FindIndex(j => j.Id == job.Id);
                if (idx >= 0) _jobs[idx] = job;
                else          _jobs.Add(job);
                if (job.NextRun == null && job.IsEnabled)
                    job.NextRun = job.ComputeNextRun(DateTime.Now);
            }
            Save();
        }

        public void Remove(string id)
        {
            SchedulerJob? job = null;
            lock (_lock)
            {
                job = _jobs.FirstOrDefault(j => j.Id == id);
                if (job != null) _jobs.Remove(job);
            }
            Save();
            if (job != null)
            {
                try { if (File.Exists(job.JobLogPath)) File.Delete(job.JobLogPath); }
                catch (Exception ex) { _log.LogWarning(ex, "Could not delete job log {Path}", job.JobLogPath); }
            }
        }

        public void SetEnabled(string id, bool enabled)
        {
            lock (_lock)
            {
                SchedulerJob? j = _jobs.FirstOrDefault(x => x.Id == id);  // may be null — guarded below
                if (j == null) return;
                j.IsEnabled = enabled;
                if (enabled && j.NextRun == null)
                    j.NextRun = j.ComputeNextRun(DateTime.Now);
            }
            Save();
        }

        /// <summary>Immediately fires a job regardless of its schedule.</summary>
        public void FireNow(string id)
        {
            SchedulerJob? job;
            lock (_lock) job = _jobs.FirstOrDefault(j => j.Id == id);
            if (job == null) return;
            _log.LogInformation("Scheduler: manual run triggered for [{Id}] \"{Label}\"", job.Id, job.Label);
            _dispatcher.InvokeAsync(() => JobFired?.Invoke(this, job));
        }

        public void RecordResult(string id, string result)
        {
            lock (_lock)
            {
                SchedulerJob? j = _jobs.FirstOrDefault(x => x.Id == id);  // may be null — guarded below
                if (j == null) return;
                j.LastResult = result;  // update under lock — prevents race with CheckAndFire Save()
                string entry = $"{DateTime.Now:yyyy-MM-dd HH:mm}  {result}";
                j.RunHistory.Insert(0, entry);
                if (j.RunHistory.Count > 5) j.RunHistory.RemoveRange(5, j.RunHistory.Count - 5);
            }
            Save();
        }

        /// <summary>Advance the next-run time past now without firing — used to dismiss missed jobs.</summary>
        public void AdvanceNextRun(string id)
        {
            lock (_lock)
            {
                SchedulerJob? j = _jobs.FirstOrDefault(x => x.Id == id);  // may be null — guarded below
                if (j == null) return;
                j.NextRun = j.ComputeNextRun(DateTime.Now);
            }
            Save();
        }

        // ── Persistence ───────────────────────────────────────────────────────
        private void Load()
        {
            try
            {
                if (!File.Exists(_filePath)) return;
                var info = new FileInfo(_filePath);
                if (info.Length > 1024 * 1024) // 1 MB cap — scheduler.json should never be large
                {
                    _log.LogWarning("scheduler.json is unexpectedly large ({Size} bytes) — skipping load", info.Length);
                    return;
                }
                string json = File.ReadAllText(_filePath);
                var list = JsonSerializer.Deserialize(json, SchedulerJobContext.Default.ListSchedulerJob);
                if (list == null) return;
                // Sanitize loaded paths — same rules as CLI args
                foreach (var j in list)
                {
                    j.SourcePath = SanitizePath(j.SourcePath);
                    j.DestPath   = SanitizePath(j.DestPath);
                    j.IntervalValue = Math.Max(1, j.IntervalValue); // prevent 0-interval loops
                }
                lock (_lock) { _jobs.Clear(); _jobs.AddRange(list); }
            }
            catch (Exception ex) { _log.LogWarning(ex, "Scheduler load failed"); }
        }

        public void Save()
        {
            try
            {
                PortableMode.EnsureDataDir();
                List<SchedulerJob> snapshot;
                lock (_lock) snapshot = new List<SchedulerJob>(_jobs);
                string json = JsonSerializer.Serialize(snapshot, SchedulerJobContext.Default.ListSchedulerJob);
                string tmpPath = _filePath + ".tmp";
                File.WriteAllText(tmpPath, json);
                File.Move(tmpPath, _filePath, overwrite: true);
            }
            catch (Exception ex) { _log.LogWarning(ex, "Scheduler save failed"); }
        }

        // ── Tick loop ─────────────────────────────────────────────────────────
        public void Start()
        {
            _cts  = new CancellationTokenSource();
            _tick = Task.Run(() => TickLoop(_cts.Token));
        }

        public void Stop()
        {
            _cts?.Cancel();
            try { _tick?.Wait(2000); } catch { }
        }

        public void Dispose() => Stop();

        private async Task TickLoop(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                try { await Task.Delay(30_000, ct); }
                catch (OperationCanceledException) { break; }
                CheckAndFire();
            }
        }

        private void CheckAndFire()
        {
            List<SchedulerJob> due;
            lock (_lock)
            {
                var now = DateTime.Now;
                due = _jobs.Where(j => j.IsEnabled
                                    && j.NextRun.HasValue
                                    && j.NextRun.Value <= now).ToList();
            }

            foreach (var job in due)
            {
                _log.LogInformation(
                    "Scheduler: firing job [{Id}] \"{Label}\" | {Freq} | {Src} → {Dst}",
                    job.Id, job.Label, job.FrequencyDisplay, job.SourcePath, job.DestPath);

                lock (_lock)
                {
                    job.LastRun = DateTime.Now;
                    var next = job.ComputeNextRun(DateTime.Now);
                    job.NextRun = next == DateTime.MaxValue ? null : next;
                    if (job.Frequency == ScheduleFrequency.Once) job.IsEnabled = false;
                }
                Save();
                _dispatcher.InvokeAsync(() => JobFired?.Invoke(this, job));
            }
        }
    }
}
