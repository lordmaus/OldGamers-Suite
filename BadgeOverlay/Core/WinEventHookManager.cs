using System;
using System.Collections.Generic;

namespace BadgeOverlay
{
    /// <summary>
    /// Central registry for SetWinEventHook subscriptions.
    ///
    /// <para>Why this exists.</para>
    ///
    /// SetWinEventHook returns an HHOOK plus an internal callback
    /// pointer; both must be tracked or the hook leaks past process
    /// shutdown. Worse, the managed delegate that backs the callback
    /// can be GC'd if no field holds a strong reference, producing
    /// silent native-side AccessViolations the next time Windows
    /// fires the hook. Each subsystem that wanted hooks used to
    /// reimplement these two pieces of bookkeeping.
    ///
    /// <para>What this provides.</para>
    ///
    /// A single owner for hook handles + delegate references + clean
    /// shutdown. Subsystems call <see cref="Subscribe"/> to register
    /// a callback for one or more events, get back an
    /// <see cref="IDisposable"/>, and dispose it when they no longer
    /// need notifications. <see cref="DisposeAll"/> is called once on
    /// app exit to scrub any leftover subscriptions.
    ///
    /// <para>Thread-safety.</para>
    ///
    /// SetWinEventHook MUST be called from a thread with a message
    /// pump (the WPF UI thread is fine; arbitrary background threads
    /// are not). This class assumes UI-thread registration. The
    /// internal subscription list is protected by a lock so disposal
    /// from any thread is safe.
    /// </summary>
    public static class WinEventHookManager
    {
        private static readonly object _lock = new object();
        private static readonly List<Subscription> _subs = new List<Subscription>();

        private sealed class Subscription : IDisposable
        {
            // Held to prevent GC of the delegate while the native side
            // still has a callback pointer to it. Don't make this field
            // local-only — the JIT will collect it the moment SetWinEventHook
            // returns and the next event fires into a freed delegate.
            public NativeMethods.WinEventDelegate KeepAlive;

            // Win32 hook handles, one per (eventMin, eventMax) registration.
            // SetWinEventHook returns one handle per call even when the
            // event range is the same; we batch ranges from one Subscribe
            // call into one handle by passing the full range to a single
            // SetWinEventHook invocation.
            public IntPtr Handle;

            // Set true on Dispose so a double-dispose from concurrent
            // shutdown paths (window OnClosing + App.OnExit) doesn't
            // call UnhookWinEvent twice with the same handle.
            public bool   Disposed;

            public void Dispose()
            {
                lock (_lock)
                {
                    if (Disposed) return;
                    Disposed = true;
                    if (Handle != IntPtr.Zero)
                    {
                        try { NativeMethods.UnhookWinEvent(Handle); }
                        catch { /* swallow: shutting down anyway */ }
                        Handle = IntPtr.Zero;
                    }
                    KeepAlive = null;   // now safe — Windows has stopped firing
                    _subs.Remove(this);
                }
            }
        }

        /// <summary>
        /// Subscribes <paramref name="handler"/> to a contiguous range
        /// of WinEvent IDs. Pass the same value for min and max to
        /// listen to a single event. The returned object MUST be
        /// disposed when the subscriber no longer wants notifications;
        /// failing to dispose leaks the native hook for the lifetime
        /// of the process.
        /// </summary>
        /// <param name="eventMin">Lowest event ID to listen for (inclusive).</param>
        /// <param name="eventMax">Highest event ID to listen for (inclusive).</param>
        /// <param name="handler">Callback invoked when any event in the range fires.</param>
        /// <param name="flags">
        /// Hook delivery flags. Defaults to OUTOFCONTEXT | SKIPOWNPROCESS,
        /// which is correct for cross-process foreground/focus/minimise
        /// hooks where we want to react to other apps' activity.
        /// </param>
        /// <returns>An IDisposable; dispose to unhook.</returns>
        public static IDisposable Subscribe(
            uint eventMin,
            uint eventMax,
            NativeMethods.WinEventDelegate handler,
            uint flags = NativeMethods.WINEVENT_OUTOFCONTEXT |
                         NativeMethods.WINEVENT_SKIPOWNPROCESS)
        {
            if (handler == null) throw new ArgumentNullException(nameof(handler));

            var sub = new Subscription { KeepAlive = handler };
            try
            {
                sub.Handle = NativeMethods.SetWinEventHook(
                    eventMin, eventMax,
                    IntPtr.Zero,
                    handler,
                    0, 0,
                    flags);
            }
            catch (Exception ex)
            {
                AppLog.Error("WinEventHookManager.Subscribe", ex);
                sub.KeepAlive = null;
                return sub;   // Disposed=false but Handle=Zero; Dispose is a safe no-op
            }

            if (sub.Handle == IntPtr.Zero)
            {
                // SetWinEventHook returned 0 — usually means the system
                // is rejecting the hook (e.g. UIPI-blocked from a
                // medium-IL process targeting a high-IL window).
                // Return the subscription anyway so callers can dispose
                // uniformly without null-checking.
                sub.KeepAlive = null;
                AppLog.Error("WinEventHookManager.Subscribe",
                    new InvalidOperationException(
                        $"SetWinEventHook returned 0 for range " +
                        $"0x{eventMin:X4}..0x{eventMax:X4}"));
                return sub;
            }

            lock (_lock) _subs.Add(sub);
            return sub;
        }

        /// <summary>
        /// Unhooks every active subscription. Called once during
        /// <see cref="App.OnExit"/> as a safety net — individual
        /// subsystems should still dispose their own subscriptions
        /// in their teardown paths so hooks come down at the same
        /// time as the windows that depend on them.
        /// </summary>
        public static void DisposeAll()
        {
            // Snapshot under lock so disposing each entry (which itself
            // mutates _subs.Remove) doesn't fight enumeration.
            Subscription[] snap;
            lock (_lock) snap = _subs.ToArray();

            foreach (var s in snap)
            {
                try { s.Dispose(); }
                catch { /* swallow: shutting down */ }
            }
        }
    }
}
