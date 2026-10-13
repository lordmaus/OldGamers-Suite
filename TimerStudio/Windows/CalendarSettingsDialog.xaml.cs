using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace TimerStudio
{
    /// <summary>
    /// Settings dialog for calendar integration (default calendar, hide/show, auto-pull)
    /// and email alert configuration (MailKit SMTP or Outlook COM).
    /// </summary>
    public partial class CalendarSettingsDialog : Window
    {
        // Called by MainWindow to trigger an immediate pull after dialog saves.
        public event EventHandler PullRequested;

        private readonly MainWindow _main;
        private bool _loaded;

        // Discovered Outlook folders (kept for the visibility checklist)
        private List<OutlookCalendarFolder> _folders =
            new List<OutlookCalendarFolder>();

        public CalendarSettingsDialog(MainWindow main)
        {
            InitializeComponent();
            _main = main;
            Loaded += OnLoaded;
        }

        // ── Startup ───────────────────────────────────────────────────────────

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            var p = AppPrefs.Instance;

            // ── Calendar section ──────────────────────────────────────────────
            ChkAutoPull.IsChecked       = p.CalendarAutoPullEnabled;
            SldPollInterval.Value       = Math.Max(5, Math.Min(120, p.CalendarPollIntervalMinutes));
            LblPollInterval.Text        = p.CalendarPollIntervalMinutes + " min";
            UpdateLastPulled();

            // Import filters
            TxtLookahead.Text           = Math.Max(1, Math.Min(365, p.CalendarLookaheadDays)).ToString();
            TxtExcludeKeywords.Text     = p.CalendarExcludeKeywords != null
                ? string.Join(", ", p.CalendarExcludeKeywords) : "";
            ChkHideBirthdays.IsChecked  = p.CalendarHideBirthdays;
            ChkHideHolidays.IsChecked   = p.CalendarHideHolidays;

            bool outlookOk = CalendarImporter.IsOutlookAvailable();
            if (!outlookOk)
            {
                PnlNoOutlookDefault.Visibility = Visibility.Visible;
            }
            else
            {
                ScanCalendarsAsync();
            }

            // ── Calendar event defaults ───────────────────────────────────────
            UpdateCalEvtPosButtons(p.CalEvtLensPosition);
            TxtCalEvtFlashSeconds.Text            = p.CalEvtFlashSeconds.ToString();
            TxtCalEvtAlertRepeatLimit.Text        = p.CalEvtAlertRepeatLimit.ToString();
            TxtCalEvtEscalateAfterSeconds.Text    = p.CalEvtEscalateAfterSeconds.ToString();
            ChkCalEvtExcludeFromPill.IsChecked    = p.CalEvtExcludeFromPill;
            ChkCalEvtReminderEnabled.IsChecked    = p.CalEvtReminderEnabled;
            TxtCalEvtReminderLeadMinutes.Text     = p.CalEvtReminderLeadMinutes.ToString();
            TxtCalEvtReminderIntervalMinutes.Text = p.CalEvtReminderIntervalMinutes.ToString();
            ChkCalEvtSendEmailOnReminder.IsChecked= p.CalEvtSendEmailOnReminder;
            PnlCalEvtReminderDetails.Visibility   = p.CalEvtReminderEnabled
                ? Visibility.Visible : Visibility.Collapsed;

            _loaded = true;
        }

        // ── Outlook calendar scan ─────────────────────────────────────────────

        private void ScanCalendarsAsync()
        {
            LblCalLoading.Visibility = Visibility.Visible;
            PnlDefaultCalendar.Visibility = Visibility.Collapsed;
            PnlCalVisibility.Visibility   = Visibility.Collapsed;

            var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(80) };
            t.Tick += (s, _) =>
            {
                t.Stop();
                try
                {
                    _folders = CalendarImporter.GetOutlookCalendarFolders();
                    PopulateCalendarUI();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine("[CalendarSettingsDialog] Scan error: " + ex.GetType().Name);
                    LblCalLoading.Text = "Could not connect to Outlook. Please open Outlook and click Refresh.";
                }
            };
            t.Start();
        }

        private void PopulateCalendarUI()
        {
            LblCalLoading.Visibility    = Visibility.Collapsed;
            BtnFixElevation.Visibility  = Visibility.Collapsed;

            if (_folders.Count == 0)
            {
                string hint = CalendarImporter.LastAcquireHint;
                LblCalLoading.Text = string.IsNullOrEmpty(hint)
                    ? "No calendars found. Please open Outlook and click Refresh."
                    : hint;
                LblCalLoading.Visibility = Visibility.Visible;

                // Show the one-click fix button when elevation is the confirmed cause
                // and the AppCompat flag is present (clearing it will actually help).
                if (!string.IsNullOrEmpty(hint) &&
                    hint.Contains("Administrator", StringComparison.OrdinalIgnoreCase) &&
                    CalendarImporter.HasRunAsAdminFlag())
                {
                    BtnFixElevation.Visibility = Visibility.Visible;
                }
                return;
            }

            var p       = AppPrefs.Instance;
            var hidden  = new System.Collections.Generic.HashSet<string>(
                p.HiddenCalendarPaths, StringComparer.OrdinalIgnoreCase);

            // ── Default calendar combo ────────────────────────────────────────
            CboDefaultCalendar.Items.Clear();
            int defaultIdx = 0;
            for (int i = 0; i < _folders.Count; i++)
            {
                var f = _folders[i];
                CboDefaultCalendar.Items.Add(f.Name + "  —  " + f.FullPath);
                if (string.Equals(f.FullPath, p.DefaultCalendarPath,
                    StringComparison.OrdinalIgnoreCase))
                    defaultIdx = i;
            }
            CboDefaultCalendar.SelectedIndex  = defaultIdx;
            PnlDefaultCalendar.Visibility     = Visibility.Visible;

            // ── Visibility checklist ──────────────────────────────────────────
            PnlCalVisList.Children.Clear();
            foreach (var f in _folders)
            {
                bool visible = !hidden.Contains(f.FullPath);
                var chk = new CheckBox
                {
                    Content   = f.Name + "  (" + f.FullPath + ")",
                    IsChecked = visible,
                    FontSize  = Controls.TypeRamp.Caption,
                    Margin    = new Thickness(2, 3, 2, 3),
                    Tag       = f.FullPath,
                    ToolTip   = "Show or hide this calendar in auto-pull and the import picker"
                };
                chk.AutomationPropertiesName(f.Name + " calendar visibility");
                chk.Checked   += CalVis_Changed;
                chk.Unchecked += CalVis_Changed;
                PnlCalVisList.Children.Add(chk);
            }
            PnlCalVisibility.Visibility = Visibility.Visible;
        }

        // ── Calendar visibility changed ───────────────────────────────────────

        private void CalVis_Changed(object sender, RoutedEventArgs e)
        {
            if (!_loaded) return;
            var hidden = new List<string>();
            foreach (UIElement el in PnlCalVisList.Children)
            {
                var chk = el as CheckBox;
                if (chk?.Tag is string path && chk.IsChecked == false)
                    hidden.Add(path);
            }
            AppPrefs.Instance.HiddenCalendarPaths = hidden;
            AppPrefs.Instance.Save();
        }

        // ── Default calendar changed ──────────────────────────────────────────

        private void CboDefaultCalendar_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_loaded) return;
            int idx = CboDefaultCalendar.SelectedIndex;
            if (idx >= 0 && idx < _folders.Count)
            {
                AppPrefs.Instance.DefaultCalendarPath = _folders[idx].FullPath;
                AppPrefs.Instance.Save();
            }
        }

        private void BtnRefreshCalendars_Click(object sender, RoutedEventArgs e)
            => ScanCalendarsAsync();

        private void BtnFixElevation_Click(object sender, RoutedEventArgs e)
        {
            bool cleared = CalendarImporter.ClearRunAsAdminFlag();
            if (!cleared)
            {
                System.Windows.MessageBox.Show(
                    "Could not remove the flag automatically.\n\n" +
                    "Please do it manually:\n" +
                    "Right-click TimerStudio.exe → Properties → Compatibility tab\n" +
                    "→ uncheck 'Run this program as an administrator' → OK.",
                    "Could not clear flag",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Warning);
                return;
            }

            // Flag cleared — restart the app at normal (medium) integrity
            var result = System.Windows.MessageBox.Show(
                "The 'Run as administrator' flag has been removed.\n\n" +
                "TimerStudio needs to restart to take effect.\n" +
                "Restart now?",
                "Restart required",
                System.Windows.MessageBoxButton.YesNo,
                System.Windows.MessageBoxImage.Information);

            if (result != System.Windows.MessageBoxResult.Yes) return;

            App.Restart();
        }

        // ── Auto-pull ─────────────────────────────────────────────────────────

        private void ChkAutoPull_Changed(object sender, RoutedEventArgs e)
        {
            if (!_loaded) return;
            AppPrefs.Instance.CalendarAutoPullEnabled = ChkAutoPull.IsChecked == true;
            AppPrefs.Instance.Save();
            _main.ApplyCalendarPollInterval();
        }

        private void SldPollInterval_Changed(object sender,
            RoutedPropertyChangedEventArgs<double> e)
        {
            if (!_loaded) return;
            int v = (int)Math.Round(e.NewValue / 5.0) * 5;
            v = Math.Max(5, Math.Min(120, v));
            LblPollInterval.Text = v + " min";
            AppPrefs.Instance.CalendarPollIntervalMinutes = v;
            AppPrefs.Instance.Save();
            _main.ApplyCalendarPollInterval();
        }

        // ── Import filter handlers ────────────────────────────────────────────

        private void TxtLookahead_LostFocus(object sender, RoutedEventArgs e)
        {
            if (!_loaded) return;
            if (int.TryParse(TxtLookahead.Text, out int v) && v >= 1 && v <= 365)
            {
                AppPrefs.Instance.CalendarLookaheadDays = v;
            }
            else
            {
                // Revert to stored value if input is invalid
                TxtLookahead.Text = AppPrefs.Instance.CalendarLookaheadDays.ToString();
            }
            AppPrefs.Instance.Save();
        }

        private void ChkCalTypeFilter_Changed(object sender, RoutedEventArgs e)
        {
            if (!_loaded) return;
            AppPrefs.Instance.CalendarHideBirthdays = ChkHideBirthdays.IsChecked == true;
            AppPrefs.Instance.CalendarHideHolidays  = ChkHideHolidays.IsChecked  == true;
            AppPrefs.Instance.Save();
        }

        private void TxtExcludeKeywords_LostFocus(object sender, RoutedEventArgs e)
        {
            if (!_loaded) return;
            var raw      = TxtExcludeKeywords.Text ?? "";
            var keywords = new List<string>();
            foreach (var part in raw.Split(','))
            {
                var kw = part.Trim();
                if (!string.IsNullOrEmpty(kw))
                    keywords.Add(kw);
            }
            AppPrefs.Instance.CalendarExcludeKeywords = keywords;
            AppPrefs.Instance.Save();
        }

        // ── Auto-pull ─────────────────────────────────────────────────────────

        private void BtnPullNow_Click(object sender, RoutedEventArgs e)
        {
            PullRequested?.Invoke(this, EventArgs.Empty);
            UpdateLastPulled();
        }

        private void UpdateLastPulled()
        {
            var p = AppPrefs.Instance;
            LblLastPulled.Text = p.CalendarLastPulled == DateTime.MinValue
                ? "Last pulled: never"
                : "Last pulled: " + p.CalendarLastPulled.ToString("ddd, MMM d  h:mm tt");
        }


        // ── Calendar event defaults ───────────────────────────────────────────

        /// <summary>Highlights the selected position button and saves to AppPrefs.</summary>
        private void BtnCalEvtPos_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not System.Windows.Controls.Button btn) return;
            if (!int.TryParse(btn.Tag?.ToString(), out int pos)) return;
            AppPrefs.Instance.CalEvtLensPosition = pos;
            AppPrefs.Instance.Save();
            UpdateCalEvtPosButtons(pos);
        }

        private void UpdateCalEvtPosButtons(int selected)
        {
            // Walk the position grid and bold/underline the active button
            var grid = BtnCalEvtPosTopLeft?.Parent as System.Windows.Controls.Grid;
            if (grid == null) return;
            foreach (UIElement el in grid.Children)
            {
                if (el is not System.Windows.Controls.Button b) continue;
                if (!int.TryParse(b.Tag?.ToString(), out int tag)) continue;
                b.FontWeight = tag == selected ? FontWeights.Bold : FontWeights.Normal;
            }
        }

        private void TxtCalEvtFlashSeconds_LostFocus(object sender, RoutedEventArgs e)
        {
            if (!_loaded) return;
            if (int.TryParse(TxtCalEvtFlashSeconds.Text, out int v) && v >= 0)
                AppPrefs.Instance.CalEvtFlashSeconds = v;
            else
                TxtCalEvtFlashSeconds.Text = AppPrefs.Instance.CalEvtFlashSeconds.ToString();
            AppPrefs.Instance.Save();
        }

        private void TxtCalEvtAlertRepeatLimit_LostFocus(object sender, RoutedEventArgs e)
        {
            if (!_loaded) return;
            if (int.TryParse(TxtCalEvtAlertRepeatLimit.Text, out int v) && v >= 0)
                AppPrefs.Instance.CalEvtAlertRepeatLimit = v;
            else
                TxtCalEvtAlertRepeatLimit.Text = AppPrefs.Instance.CalEvtAlertRepeatLimit.ToString();
            AppPrefs.Instance.Save();
        }

        private void TxtCalEvtEscalateAfterSeconds_LostFocus(object sender, RoutedEventArgs e)
        {
            if (!_loaded) return;
            if (int.TryParse(TxtCalEvtEscalateAfterSeconds.Text, out int v) && v >= 0)
                AppPrefs.Instance.CalEvtEscalateAfterSeconds = v;
            else
                TxtCalEvtEscalateAfterSeconds.Text = AppPrefs.Instance.CalEvtEscalateAfterSeconds.ToString();
            AppPrefs.Instance.Save();
        }

        private void ChkCalEvt_Changed(object sender, RoutedEventArgs e)
        {
            if (!_loaded) return;
            AppPrefs.Instance.CalEvtExcludeFromPill     = ChkCalEvtExcludeFromPill.IsChecked    == true;
            AppPrefs.Instance.CalEvtReminderEnabled     = ChkCalEvtReminderEnabled.IsChecked    == true;
            AppPrefs.Instance.CalEvtSendEmailOnReminder = ChkCalEvtSendEmailOnReminder.IsChecked== true;
            AppPrefs.Instance.Save();
            // Show/hide reminder detail fields
            PnlCalEvtReminderDetails.Visibility = AppPrefs.Instance.CalEvtReminderEnabled
                ? Visibility.Visible : Visibility.Collapsed;
        }

        private void TxtCalEvtReminderLeadMinutes_LostFocus(object sender, RoutedEventArgs e)
        {
            if (!_loaded) return;
            if (int.TryParse(TxtCalEvtReminderLeadMinutes.Text, out int v) && v >= 1)
                AppPrefs.Instance.CalEvtReminderLeadMinutes = v;
            else
                TxtCalEvtReminderLeadMinutes.Text = AppPrefs.Instance.CalEvtReminderLeadMinutes.ToString();
            AppPrefs.Instance.Save();
        }

        private void TxtCalEvtReminderIntervalMinutes_LostFocus(object sender, RoutedEventArgs e)
        {
            if (!_loaded) return;
            if (int.TryParse(TxtCalEvtReminderIntervalMinutes.Text, out int v) && v >= 0)
                AppPrefs.Instance.CalEvtReminderIntervalMinutes = v;
            else
                TxtCalEvtReminderIntervalMinutes.Text = AppPrefs.Instance.CalEvtReminderIntervalMinutes.ToString();
            AppPrefs.Instance.Save();
        }

        // ── Footer ────────────────────────────────────────────────────────────

        private void BtnDone_Click(object sender, RoutedEventArgs e)
        {
            EmailPanel.Commit();
            Close();
        }

        // ── Window size persistence ───────────────────────────────────────────
        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            var (w, h) = WindowSizeCache.Get("CalendarSettings", 520, 740);
            Width = w; Height = h;
        }

        protected override void OnClosed(EventArgs e)
        {
            WindowSizeCache.Set("CalendarSettings", Width, Height);
            base.OnClosed(e);
        }
    }

    // Extension helper — sets AutomationProperties.Name without a converter
    internal static class CheckBoxExtensions
    {
        public static void AutomationPropertiesName(this CheckBox chk, string name)
        {
            System.Windows.Automation.AutomationProperties.SetName(chk, name);
        }
    }
}
