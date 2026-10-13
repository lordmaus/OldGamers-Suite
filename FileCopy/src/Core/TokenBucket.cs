using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

#nullable enable


namespace FileCopy
{
    /// <summary>
    /// Token-bucket rate limiter for bandwidth throttling.
    /// Set <see cref="BytesPerSecond"/> to 0 for unlimited.
    /// Thread-safe; multiple concurrent callers share the same bucket.
    /// </summary>
    public sealed class TokenBucket
    {
        private readonly Stopwatch _sw = Stopwatch.StartNew();
        private long _tokens;          // available bytes
        private long _lastTickMs;
        private readonly object _lock = new object();

        public long BytesPerSecond { get; set; }   // 0 = unlimited

        /// <summary>
        /// Consume <paramref name="bytes"/> tokens, sleeping if the bucket is empty.
        /// </summary>
        public async ValueTask ConsumeAsync(int bytes, CancellationToken ct)
        {
            if (BytesPerSecond <= 0) return;  // unlimited

            while (true)
            {
                ct.ThrowIfCancellationRequested();
                long waitMs;

                lock (_lock)
                {
                    long nowMs  = _sw.ElapsedMilliseconds;
                    long deltaMs = nowMs - _lastTickMs;
                    if (deltaMs > 0)
                    {
                        // Refill: add bytes proportional to elapsed time, cap at 1 second
                        // Use double math to avoid integer truncation at low throttle rates
                        long refill = Math.Min(BytesPerSecond, (long)((double)BytesPerSecond * deltaMs / 1000.0));
                        _tokens = Math.Min(BytesPerSecond, _tokens + refill);
                        _lastTickMs = nowMs;
                    }

                    if (_tokens >= bytes)
                    {
                        _tokens -= bytes;
                        return;  // consume and proceed
                    }

                    // Calculate how long until enough tokens.
                    // Cap at 250ms per sleep to keep the cancel-check loop responsive.
                    long deficit = bytes - _tokens;
                    waitMs = Math.Clamp(deficit * 1000 / BytesPerSecond, 1, 250);
                }

                await Task.Delay((int)waitMs, ct);
            }
        }
    }
}
