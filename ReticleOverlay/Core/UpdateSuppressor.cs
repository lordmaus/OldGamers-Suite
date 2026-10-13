using System;

namespace ReticleOverlay
{
    /// <summary>
    /// Exception-safe replacement for the _loading boolean suppressor pattern.
    /// Usage:
    ///   using (_suppress.Enter()) { slider.Value = 42; }
    ///
    /// Inside event handlers:
    ///   if (_suppress.IsActive) return;
    ///
    /// Supports re-entrancy: nested Enter() calls increment a counter;
    /// the suppressor is only deactivated when the outermost scope exits.
    /// An exception inside the using block still correctly deactivates.
    /// </summary>
    public sealed class UpdateSuppressor
    {
        private int _depth = 0;

        public bool IsActive => _depth > 0;

        public IDisposable Enter() => new Scope(this);

        private sealed class Scope : IDisposable
        {
            private readonly UpdateSuppressor _owner;
            private bool _disposed;

            public Scope(UpdateSuppressor owner)
            {
                _owner = owner;
                _owner._depth++;
            }

            public void Dispose()
            {
                if (_disposed) return;
                _disposed = true;
                _owner._depth--;
            }
        }
    }
}
