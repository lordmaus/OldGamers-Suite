using System;
using System.Threading;
using System.Windows.Threading;

#nullable enable


namespace FileCopy
{
    /// <summary>
    /// Drops intermediate <see cref="IProgress{T}"/> reports so the UI
    /// thread is updated at most once per <c>intervalMs</c>. The latest
    /// pending value is always delivered (last-write-wins).
    /// </summary>
    public sealed class ThrottledProgress<T> : IProgress<T>, IDisposable
    {
        private readonly Action<T>    _sink;
        private readonly Dispatcher    _dispatcher;
        private readonly int           _intervalMs;
        private readonly Timer         _timer;
        private          T             _latest;
        private          int           _pending; // 0 = empty, 1 = has value
        private          int           _disposed;

        public ThrottledProgress(Action<T> sink, Dispatcher dispatcher, int intervalMs = 16)
        {
            _sink       = sink ?? throw new ArgumentNullException(nameof(sink));
            _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
            _intervalMs = Math.Max(1, intervalMs);
            _timer      = new Timer(OnTick, null, _intervalMs, _intervalMs);
        }

        public void Report(T value)
        {
            _latest = value;
            Interlocked.Exchange(ref _pending, 1);
        }

        private void OnTick(object? _)
        {
            if (Volatile.Read(ref _disposed) != 0) return;
            Flush();
        }

        private void Flush()
        {
            if (Interlocked.Exchange(ref _pending, 0) == 0) return;
            T snapshot = _latest;
            try { _dispatcher.BeginInvoke((Action)(() => _sink(snapshot)), DispatcherPriority.Background); }
            catch { /* dispatcher torn down */ }
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            try { Flush(); } catch { } // deliver any pending update before stopping
            _timer.Dispose();
        }
    }
}
