using System;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Threading;

namespace NoteStation.Controls
{
    /// <summary>
    /// WCAG 4.1.3 Status Messages: speaks transient status ("Clip captured", "Note saved",
    /// "Task completed", undo prompts) through UI Automation notification events, so
    /// Narrator / NVDA / JAWS announce them without moving focus. Unlike BadgeOverlay's
    /// SAPI announcer this does not talk when no screen reader is listening.
    /// <para><see cref="AnnounceDebounced"/> coalesces bursts (a note autosaves on every
    /// keystroke) into one announcement after the burst ends.</para>
    /// </summary>
    public static class StatusAnnouncer
    {
        private const string ActivityId = "NoteStation.Status";
        private static DispatcherTimer _debounce;
        private static string _pending;
        private static UIElement _pendingSource;

        /// <summary>Announce now. <paramref name="source"/> defaults to the active window, then MainWindow.</summary>
        public static void Announce(string message, UIElement source = null, bool important = false)
        {
            if (string.IsNullOrWhiteSpace(message)) return;
            var app = Application.Current;
            if (app == null) return;
            app.Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => Raise(message, source, important)));
        }

        /// <summary>Announce once, <paramref name="delay"/> after the last call in a burst.</summary>
        public static void AnnounceDebounced(string message, TimeSpan delay, UIElement source = null)
        {
            var app = Application.Current;
            if (app == null || string.IsNullOrWhiteSpace(message)) return;
            _pending = message;
            _pendingSource = source;
            if (_debounce == null)
            {
                _debounce = new DispatcherTimer(DispatcherPriority.Background, app.Dispatcher);
                _debounce.Tick += (_, _) =>
                {
                    _debounce.Stop();
                    Raise(_pending, _pendingSource, false);
                    _pendingSource = null;
                };
            }
            _debounce.Stop();
            _debounce.Interval = delay;
            _debounce.Start();
        }

        private static void Raise(string message, UIElement source, bool important)
        {
            try
            {
                var el = source;
                if (el == null || !el.IsVisible)
                {
                    el = null;
                    foreach (Window w in Application.Current.Windows)
                        if (w.IsActive && w.IsVisible) { el = w; break; }
                    el ??= Application.Current.MainWindow;
                }
                if (el == null) return;
                var peer = UIElementAutomationPeer.FromElement(el) ?? UIElementAutomationPeer.CreatePeerForElement(el);
                peer?.RaiseNotificationEvent(
                    AutomationNotificationKind.Other,
                    important ? AutomationNotificationProcessing.ImportantMostRecent
                              : AutomationNotificationProcessing.MostRecent,
                    message, ActivityId);
            }
            catch (Exception ex)
            {
                AppLog.Error("StatusAnnouncer", ex);
            }
        }
    }
}
