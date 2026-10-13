using System;
using System.Windows;
using Microsoft.Win32;

namespace SuperMagnifier
{
    /// <summary>
    /// Detects Windows High Contrast mode and keeps the app theme in sync.
    /// Raises <see cref="HighContrastChanged"/> whenever the OS state flips,
    /// and swaps WPF merged theme dictionaries so custom templates honour the
    /// user's selected HC palette instead of the hard-coded Dark/Light colours.
    ///
    /// WCAG 2.1 SC 1.4.11 (Non-text contrast) / SC 1.4.12 / user preference.
    /// </summary>
    internal sealed class HighContrastMonitor : IDisposable
    {
        private bool   _isHighContrast;
        private bool   _disposed;
        private string _userTheme;   // "Dark" or "Light" — preserved while HC is active

        public event EventHandler HighContrastChanged;
        public bool IsHighContrast => _isHighContrast;

        public HighContrastMonitor()
        {
            _isHighContrast = SystemParameters.HighContrast;
            SystemParameters.StaticPropertyChanged += OnSystemPropertyChanged;
            SystemEvents.UserPreferenceChanged     += OnUserPreferenceChanged;
        }

        /// <summary>
        /// Applies the correct theme for the current OS state.
        /// Call once on startup after loading saved settings; the monitor keeps
        /// subsequent OS changes in sync automatically.
        /// </summary>
        public void Sync(string userSelectedTheme)
        {
            _userTheme = userSelectedTheme ?? "Dark";
            ApplyThemeForCurrentState();
        }

        // ── Event handlers ────────────────────────────────────────────────────

        private void OnSystemPropertyChanged(object sender,
            System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(SystemParameters.HighContrast))
                HandleChange();
        }

        private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
        {
            // General + Color + Accessibility categories all fire on HC toggle
            if (e.Category == UserPreferenceCategory.General  ||
                e.Category == UserPreferenceCategory.Color     ||
                e.Category == UserPreferenceCategory.Accessibility)
                HandleChange();
        }

        private void HandleChange()
        {
            bool now = SystemParameters.HighContrast;
            if (now == _isHighContrast) return;
            _isHighContrast = now;

            // Marshal theme swap to the UI thread
            Application.Current?.Dispatcher.BeginInvoke((Action)(() =>
            {
                ApplyThemeForCurrentState();
                HighContrastChanged?.Invoke(this, EventArgs.Empty);
            }));
        }

        private void ApplyThemeForCurrentState()
        {
            string key = _isHighContrast ? "HighContrast" : (_userTheme ?? "Dark");
            App.ApplyTheme(key);
        }

        // ── IDisposable ───────────────────────────────────────────────────────

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            try { SystemParameters.StaticPropertyChanged -= OnSystemPropertyChanged; } catch { }
            try { SystemEvents.UserPreferenceChanged     -= OnUserPreferenceChanged; } catch { }
        }
    }
}
