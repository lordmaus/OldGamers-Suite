using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Extensions.Logging;

#nullable enable


namespace FileCopy
{
    public sealed record CopyCompletedEventArgs(bool CleanFinish);

    /// <summary>
    /// Owns the lifecycle of async copy, verify, and retry operations.
    /// Separates orchestration logic from MainWindow's UI concerns.
    /// </summary>
    public sealed class CopyOrchestrator
    {
        private readonly ICopyEngine        _engine;
        private readonly MainWindowViewModel _vm;
        private readonly Dispatcher         _dispatcher;
        private readonly ILogger<CopyOrchestrator> _log;

        private CancellationTokenSource _cts;
        private ManualResetEventSlim    _pause = new ManualResetEventSlim(true);

        public event EventHandler<CopyProgress>?  ProgressChanged;
        /// <summary>Fires when any operation ends. <see cref="CopyCompletedEventArgs.CleanFinish"/> is true on success.</summary>
        public event Action<CopyCompletedEventArgs>? Completed;

        public bool IsRunning { get => _vm.IsRunning; private set => _vm.IsRunning = value; }
        public string PauseLabel => _vm.PauseLabel;

        public CopyOrchestrator(ICopyEngine engine, MainWindowViewModel vm, Dispatcher dispatcher)
        {
            _engine     = engine     ?? throw new ArgumentNullException(nameof(engine));
            _vm         = vm         ?? throw new ArgumentNullException(nameof(vm));
            _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
            _log        = AppLogger.ForContext<CopyOrchestrator>();
        }

        // ── Pause / Cancel ────────────────────────────────────────────────────
        public void TogglePause()
        {
            if (_pause.IsSet) { _pause.Reset(); _vm.PauseLabel = "Resume"; _vm.StatusText = "Paused."; }
            else              { _pause.Set();   _vm.PauseLabel = "Pause";  _vm.StatusText = "Resumed."; }
        }

        public void Cancel()
        {
            try { _cts?.Cancel(); } catch { }
            _pause.Set();
        }

        // ── Copy ──────────────────────────────────────────────────────────────
        public async Task RunCopyAsync(
            IList<FileItem> items,
            CopyEngine.CopyOptions opts)
        {
            if (IsRunning) return;
            _cts?.Dispose();
            _cts = new CancellationTokenSource();
            _pause.Set();
            IsRunning = true;

            var progress = new ThrottledProgress<CopyProgress>(
                pr => { ProgressChanged?.Invoke(this, pr); },
                _dispatcher, intervalMs: 16);
            bool cleanFinish = false;
            try
            {
                _vm.StatusText = "Copying…";
                _log.LogInformation("Copy started: {Count} items", items.Count);
                await Task.Run(async () => await _engine.RunAsync(items, opts, progress, _cts.Token, _dispatcher));

                int failed = items.Count(x => x.Action == FileAction.Failed);
                _log.LogInformation("Copy complete. Failed={Failed}", failed);
                _vm.StatusText = failed > 0 ? $"Complete with {failed:N0} error(s)." : "Complete.";
                _vm.SpeedText  = "";
                cleanFinish    = true;
            }
            catch (OperationCanceledException)
            {
                _log.LogInformation("Copy cancelled");
                _vm.StatusText       = "Cancelled.";
                _vm.SpeedText        = "";
                _vm.FileProgressPct  = 0;
                _vm.TotalProgressPct = 0;
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Copy error");
                _vm.StatusText = "Error: " + ex.Message; _vm.SpeedText = "";
            }
            finally
            {
                progress.Dispose();
                IsRunning      = false;
                _vm.PauseLabel = "Pause";
                Completed?.Invoke(new CopyCompletedEventArgs(cleanFinish));
            }
        }

        // ── Verify ────────────────────────────────────────────────────────────
        public async Task VerifyAsync(IList<FileItem> items, int bufferSizeKb)
        {
            if (IsRunning) return;
            _cts?.Dispose();
            _cts = new CancellationTokenSource();
            IsRunning = true;

            var progress = new ThrottledProgress<CopyProgress>(
                pr => { ProgressChanged?.Invoke(this, pr); },
                _dispatcher, intervalMs: 16);
            bool cleanFinish = false;
            try
            {
                _vm.StatusText = "Verifying…";
                _vm.SpeedText  = "";
                await Task.Run(async () =>
                    await _engine.VerifyOnlyAsync(items, bufferSizeKb, progress, _cts.Token, _dispatcher));

                int matched = items.Count(x => x.Action == FileAction.Verified);
                int failed  = items.Count(x => x.Action == FileAction.Failed);
                _vm.StatusText = $"Verify complete. {matched:N0} match, {failed:N0} mismatch.";
                cleanFinish    = true;
            }
            catch (OperationCanceledException)
            {
                _vm.StatusText       = "Verify cancelled.";
                _vm.FileProgressPct  = 0;
                _vm.TotalProgressPct = 0;
            }
            catch (Exception ex) { _vm.StatusText = "Verify error: " + ex.Message; }
            finally
            {
                progress.Dispose();
                IsRunning = false;
                Completed?.Invoke(new CopyCompletedEventArgs(cleanFinish));
            }
        }

        // ── Retry ─────────────────────────────────────────────────────────────
        public async Task RetryFailedAsync(
            IList<FileItem> failed,
            CopyEngine.CopyOptions opts)
        {
            if (IsRunning || failed.Count == 0) return;
            _cts?.Dispose();
            _cts = new CancellationTokenSource();
            IsRunning = true;

            var progress = new ThrottledProgress<CopyProgress>(
                pr => { ProgressChanged?.Invoke(this, pr); },
                _dispatcher, intervalMs: 16);
            bool cleanFinish = false;
            try
            {
                _vm.StatusText = $"Retrying {failed.Count:N0} failed item(s)…";
                await Task.Run(async () => await _engine.RunAsync(failed, opts, progress, _cts.Token, _dispatcher));

                int still = failed.Count(x => x.Action == FileAction.Failed);
                _vm.StatusText = still == 0
                    ? $"Retry complete — all {failed.Count:N0} item(s) succeeded."
                    : $"Retry complete — {still:N0} still failed.";
                cleanFinish = still == 0;
            }
            catch (OperationCanceledException) { _vm.StatusText = "Retry cancelled."; }
            catch (Exception ex) { _vm.StatusText = "Retry error: " + ex.Message; }
            finally
            {
                progress.Dispose();
                IsRunning = false;
                Completed?.Invoke(new CopyCompletedEventArgs(cleanFinish));
            }
        }

        public ManualResetEventSlim PauseLatch => _pause;
    }
}
