using System;
using System.Windows;
using Microsoft.Win32;

namespace BadgeOverlay
{
    /// <summary>
    /// Detects Windows High-Contrast mode and raises an event when it
    /// changes. Swaps the WPF merged theme dictionaries so the app uses
    /// system colours (SystemColors.*) via the HighContrast theme instead
    /// of the Dark / Light custom themes.
    ///
    /// WCAG 2.1 — 1.4.11 / 1.4.12 / user preference must be honoured.
    /// </summary>
    internal sealed class HighContrastMonitor : IDisposable
    {
        private bool   _isHighContrast;
        private bool   _disposed;
        private string _userTheme;   // "Dark" or "Light" — the theme the user had selected before HC engaged

        public event EventHandler HighContrastChanged;
        public bool IsHighContrast => _isHighContrast;

        public HighContrastMonitor()
        {
            _isHighContrast = SystemParameters.HighContrast;
            SystemParameters.StaticPropertyChanged += OnSystemPropertyChanged;
            SystemEvents.UserPreferenceChanged     += OnUserPreferenceChanged;
        }

        /// <summary>
        /// Applies the high-contrast theme if active, otherwise restores
        /// the user-selected custom theme. Call once on startup after
        /// loading settings, and the monitor will keep themes in sync.
        /// </summary>
        public void Sync(string userSelectedTheme)
        {
            _userTheme = userSelectedTheme ?? "Dark";
            ApplyThemeForCurrentState();
        }

        private void OnSystemPropertyChanged(object sender,
            System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(SystemParameters.HighContrast)) return;
            HandleChange();
        }

        private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
        {
            // General + Color categories both fire on HC toggle in practice
            if (e.Category == UserPreferenceCategory.General ||
                e.Category == UserPreferenceCategory.Color ||
                e.Category == UserPreferenceCategory.Accessibility)
                HandleChange();
        }

        private void HandleChange()
        {
            bool now = SystemParameters.HighContrast;
            if (now == _isHighContrast) return;
            _isHighContrast = now;

            // Marshal to UI thread
            if (Application.Current != null)
                Application.Current.Dispatcher.BeginInvoke((Action)(() =>
                {
                    ApplyThemeForCurrentState();
                    HighContrastChanged?.Invoke(this, EventArgs.Empty);
                }));
        }

        private void ApplyThemeForCurrentState()
        {
            try
            {
                string themeKey = _isHighContrast ? "HighContrast" : (_userTheme ?? "Dark");
                App.ApplyTheme(themeKey);
            }
            catch (Exception ex) { CrashLog.Write("ApplyThemeForCurrentState", ex); }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            try { SystemParameters.StaticPropertyChanged -= OnSystemPropertyChanged; } catch { }
            try { SystemEvents.UserPreferenceChanged     -= OnUserPreferenceChanged; } catch { }
        }
    }
}
