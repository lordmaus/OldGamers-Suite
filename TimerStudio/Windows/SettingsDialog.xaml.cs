using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace TimerStudio
{
    /// <summary>
    /// Combined settings window: Event Settings | Calendar Settings | Email Settings.
    /// Replaces AppSettingsDialog + CalendarSettingsDialog.
    /// </summary>
    public partial class SettingsDialog : Window
    {
        /// <summary>Raised when the user clicks Pull Now (Calendar tab).</summary>
        public event EventHandler PullRequested;

        private readonly MainWindow _main;
        private readonly int _initialTab;
        private bool _loaded;

        private List<OutlookCalendarFolder> _folders = new List<OutlookCalendarFolder>();

        /// <param name="main">Owning MainWindow — used for live font-scale callbacks.</param>
        /// <param name="initialTab">0 = Event, 1 = Calendar, 2 = Email.</param>
        public SettingsDialog(MainWindow main, int initialTab = 0)
        {
            _main       = main;
            _initialTab = initialTab;
            InitializeComponent();
            Loaded += OnLoaded;
        }

        // ── Tab switching ─────────────────────────────────────────────────────

        private void Tab_Checked(object sender, RoutedEventArgs e)
        {
            // Guard: panels may be null during InitializeComponent
            if (PnlEventTab == null) return;
            string tag = (sender as RadioButton)?.Tag?.ToString() ?? "0";
            PnlEventTab.Visibility    = tag == "0" ? Visibility.Visible : Visibility.Collapsed;
            PnlCalendarTab.Visibility = tag == "1" ? Visibility.Visible : Visibility.Collapsed;
            PnlEmailTab.Visibility    = tag == "2" ? Visibility.Visible : Visibility.Collapsed;
        }

        // ── Startup ───────────────────────────────────────────────────────────

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            var p = AppPrefs.Instance;

            // ── Event settings ────────────────────────────────────────────────
            SldEventTimeFontScale.Value  = p.EventTimeFontScale;
            SldEventTitleFontScale.Value = p.EventTitleFontScale;
            LblEventTimeFontScale.Text   = p.EventTimeFontScale.ToString("F2") + "×";
            LblEventTitleFontScale.Text  = p.EventTitleFontScale.ToString("F2") + "×";

            SldTimeFontScale.Value  = p.TimeFontScale;
            SldTitleFontScale.Value = p.TitleFontScale;
            LblTimeFontScale.Text   = p.TimeFontScale.ToString("F2") + "×";
            LblTitleFontScale.Text  = p.TitleFontScale.ToString("F2") + "×";

            CboDefaultSound.SelectedIndex = Math.Max(0, Math.Min(5, p.DefaultAlertSound));
            CboDefaultStyle.SelectedIndex = Math.Max(0, Math.Min(2, p.DefaultAlertStyle));
            SldDefaultFlashSecs.Value     = p.DefaultFlashSecs;
            SldDefaultFontSize.Value      = p.DefaultFontSize;
            LblDefaultFlashSecs.Text      = p.DefaultFlashSecs + " s";
            LblDefaultFontSize.Text       = p.DefaultFontSize + " pt";
            ChkDefaultAutoReset.IsChecked = p.DefaultAutoReset;

            // ── Calendar settings ─────────────────────────────────────────────
            ChkAutoPull.IsChecked     = p.CalendarAutoPullEnabled;
            SldPollInterval.Value     = Math.Max(5, Math.Min(120, p.CalendarPollIntervalMinutes));
            LblPollInterval.Text      = p.CalendarPollIntervalMinutes + " min";
            UpdateLastPulled();

            TxtLookahead.Text         = Math.Max(1, Math.Min(365, p.CalendarLookaheadDays)).ToString();
            TxtExcludeKeywords.Text   = p.CalendarExcludeKeywords != null
                ? string.Join(", ", p.CalendarExcludeKeywords) : "";
            ChkHideBirthdays.IsChecked = p.CalendarHideBirthdays;
            ChkHideHolidays.IsChecked  = p.CalendarHideHolidays;

            // ── Calendar event defaults ───────────────────────────────────────
            UpdateSdCalEvtPosButtons(p.CalEvtLensPosition);
            SdTxtCalEvtFlashSeconds.Text            = p.CalEvtFlashSeconds.ToString();
            SdTxtCalEvtAlertRepeatLimit.Text        = p.CalEvtAlertRepeatLimit.ToString();
            SdTxtCalEvtEscalateAfterSeconds.Text    = p.CalEvtEscalateAfterSeconds.ToString();
            SdChkCalEvtExcludeFromPill.IsChecked    = p.CalEvtExcludeFromPill;
            SdChkCalEvtReminderEnabled.IsChecked    = p.CalEvtReminderEnabled;
            SdTxtCalEvtReminderLeadMinutes.Text     = p.CalEvtReminderLeadMinutes.ToString();
            SdTxtCalEvtReminderIntervalMinutes.Text = p.CalEvtReminderIntervalMinutes.ToString();
            SdChkCalEvtSendEmailOnReminder.IsChecked= p.CalEvtSendEmailOnReminder;
            SdPnlCalEvtReminderDetails.Visibility   = p.CalEvtReminderEnabled
                ? Visibility.Visible : Visibility.Collapsed;

            // Sort order default
            CboDefaultSort.SelectedIndex = Math.Max(0, Math.Min(5, p.LastSortMode + 1));

            // Feature 6: Auto-theme
            ChkThemeAuto.IsChecked = p.ThemeAuto;

            bool outlookOk = CalendarImporter.IsOutlookAvailable();
            if (!outlookOk)
                PnlNoOutlookDefault.Visibility = Visibility.Visible;
            else
                ScanCalendarsAsync();

            // ── Select initial tab ────────────────────────────────────────────
            _loaded = true;
            switch (_initialTab)
            {
                case 1: TabCalendar.IsChecked = true; break;
                case 2: TabEmail.IsChecked    = true; break;
                default: TabEvent.IsChecked   = true; break;
            }
        }

        // ── Outlook calendar scan ─────────────────────────────────────────────

        private void ScanCalendarsAsync()
        {
            LblCalLoading.Visibility      = Visibility.Visible;
            PnlDefaultCalendar.Visibility = Visibility.Collapsed;
            PnlCalVisibility.Visibility   = Visibility.Collapsed;

            var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(80) };
            t.Tick += (s, _) =>
            {
                t.Stop();
                // H-7: GetOutlookCalendarFolders does COM enumeration — can block for hundreds
                // of ms if Outlook is busy. Run on a dedicated STA thread (same pattern as
                // RunCalendarPoll in MainWindow) so the UI thread stays responsive.
                System.Threading.Tasks.Task.Run(() =>
                {
                    List<OutlookCalendarFolder> folders = null;
                    Exception scanEx = null;
                    var sta = new System.Threading.Thread(() =>
                    {
                        try  { folders = CalendarImporter.GetOutlookCalendarFolders(); }
                        catch (Exception ex) { scanEx = ex; }
                    });
                    sta.SetApartmentState(System.Threading.ApartmentState.STA);
                    sta.Start();
                    sta.Join();
                    Dispatcher.InvokeAsync(() =>
                    {
                        if (scanEx != null)
                        {
                            Debug.WriteLine("[SettingsDialog] Scan error: " + scanEx.GetType().Name);
                            LblCalLoading.Text = "Could not read calendars. Click Refresh to try again.";
                        }
                        else
                        {
                            _folders = folders;
                            PopulateCalendarUI();
                        }
                    });
                });
            };
            t.Start();
        }

        private void PopulateCalendarUI()
        {
            LblCalLoading.Visibility = Visibility.Collapsed;
            if (_folders.Count == 0)
            {
                LblCalLoading.Text       = "No calendars found. Please open Outlook and click Refresh.";
                LblCalLoading.Visibility = Visibility.Visible;
                return;
            }

            var p      = AppPrefs.Instance;
            var hidden = new HashSet<string>(p.HiddenCalendarPaths, StringComparer.OrdinalIgnoreCase);

            CboDefaultCalendar.Items.Clear();
            int defaultIdx = 0;
            for (int i = 0; i < _folders.Count; i++)
            {
                var f = _folders[i];
                CboDefaultCalendar.Items.Add(f.Name + "  —  " + f.FullPath);
                if (string.Equals(f.FullPath, p.DefaultCalendarPath, StringComparison.OrdinalIgnoreCase))
                    defaultIdx = i;
            }
            CboDefaultCalendar.SelectedIndex = defaultIdx;
            PnlDefaultCalendar.Visibility    = Visibility.Visible;

            PnlCalVisList.Children.Clear();
            foreach (var f in _folders)
            {
                bool visible = !hidden.Contains(f.FullPath);
                var chk = new CheckBox
                {
                    Content   = f.Name + "  (" + f.FullPath + ")",
                    IsChecked = visible,
                    FontSize  = OldGamers.Wpf.Shared.Controls.TypeRamp.Caption,
                    Margin    = new Thickness(2, 3, 2, 3),
                    Tag       = f.FullPath,
                    ToolTip   = "Show or hide this calendar in auto-pull and the import picker"
                };
                System.Windows.Automation.AutomationProperties.SetName(chk, f.Name + " calendar visibility");
                chk.Checked   += CalVis_Changed;
                chk.Unchecked += CalVis_Changed;
                PnlCalVisList.Children.Add(chk);
            }
            PnlCalVisibility.Visibility = Visibility.Visible;
        }

        // ── Calendar visibility ───────────────────────────────────────────────

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
        {
            ScanCalendarsAsync();
        }

        // ── Auto-pull ─────────────────────────────────────────────────────────

        private void ChkAutoPull_Changed(object sender, RoutedEventArgs e)
        {
            if (!_loaded) return;
            AppPrefs.Instance.CalendarAutoPullEnabled = ChkAutoPull.IsChecked == true;
            AppPrefs.Instance.Save();
            _main.ApplyCalendarPollInterval();
        }

        private void SldPollInterval_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (!_loaded) return;
            int v = (int)Math.Round(e.NewValue / 5.0) * 5;
            v = Math.Max(5, Math.Min(120, v));
            LblPollInterval.Text = v + " min";
            AppPrefs.Instance.CalendarPollIntervalMinutes = v;
            AppPrefs.Instance.Save();
            _main.ApplyCalendarPollInterval();
        }

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

        // ── Import filters ────────────────────────────────────────────────────

        private void TxtLookahead_LostFocus(object sender, RoutedEventArgs e)
        {
            if (!_loaded) return;
            if (int.TryParse(TxtLookahead.Text, out int v) && v >= 1 && v <= 365)
                AppPrefs.Instance.CalendarLookaheadDays = v;
            else
                TxtLookahead.Text = AppPrefs.Instance.CalendarLookaheadDays.ToString();
            AppPrefs.Instance.Save();
        }

        private void TxtExcludeKeywords_LostFocus(object sender, RoutedEventArgs e)
        {
            if (!_loaded) return;
            var keywords = new List<string>();
            foreach (var part in (TxtExcludeKeywords.Text ?? "").Split(','))
            {
                var kw = part.Trim();
                if (!string.IsNullOrEmpty(kw)) keywords.Add(kw);
            }
            AppPrefs.Instance.CalendarExcludeKeywords = keywords;
            AppPrefs.Instance.Save();
        }

        private void ChkCalTypeFilter_Changed(object sender, RoutedEventArgs e)
        {
            if (!_loaded) return;
            AppPrefs.Instance.CalendarHideBirthdays = ChkHideBirthdays.IsChecked == true;
            AppPrefs.Instance.CalendarHideHolidays  = ChkHideHolidays.IsChecked  == true;
            AppPrefs.Instance.Save();
        }

        // ── Feature 6: Auto-theme ─────────────────────────────────────────────

        private void ChkThemeAuto_Changed(object sender, RoutedEventArgs e)
        {
            if (!_loaded) return;
            AppPrefs.Instance.ThemeAuto = ChkThemeAuto.IsChecked == true;
            AppPrefs.Instance.Save();
            App.ApplyAutoThemeIfEnabled();
        }

        // ── Event settings handlers ───────────────────────────────────────────

        private void SldEventTimeFontScale_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (!_loaded) return;
            double v = Math.Round(e.NewValue * 4) / 4.0;
            AppPrefs.Instance.EventTimeFontScale = v;
            LblEventTimeFontScale.Text = v.ToString("F2") + "×";
            AppPrefs.Instance.Save();
            _main.ApplyEventTimeFontScale(v);
        }

        private void SldEventTitleFontScale_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (!_loaded) return;
            double v = Math.Round(e.NewValue * 4) / 4.0;
            AppPrefs.Instance.EventTitleFontScale = v;
            LblEventTitleFontScale.Text = v.ToString("F2") + "×";
            AppPrefs.Instance.Save();
            _main.ApplyEventTitleFontScale(v);
        }

        private void SldTimeFontScale_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (!_loaded) return;
            double v = Math.Round(e.NewValue * 4) / 4.0;
            AppPrefs.Instance.TimeFontScale = v;
            LblTimeFontScale.Text = v.ToString("F2") + "×";
            AppPrefs.Instance.Save();
            _main.ApplyTimeFontScale(v);
        }

        private void SldTitleFontScale_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (!_loaded) return;
            double v = Math.Round(e.NewValue * 4) / 4.0;
            AppPrefs.Instance.TitleFontScale = v;
            LblTitleFontScale.Text = v.ToString("F2") + "×";
            AppPrefs.Instance.Save();
            _main.ApplyTitleFontScale(v);
        }

        private void CboDefaultSort_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (!_loaded) return;
            int mode = CboDefaultSort.SelectedIndex - 1; // 0→-1 (none), 1→0, …, 5→4
            AppPrefs.Instance.LastSortMode = mode;
            AppPrefs.Instance.Save();
            _main.ApplySortMode(mode);
        }

        private void CboDefaultSound_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (!_loaded) return;
            AppPrefs.Instance.DefaultAlertSound = CboDefaultSound.SelectedIndex;
            AppPrefs.Instance.Save();
        }

        private void CboDefaultStyle_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (!_loaded) return;
            AppPrefs.Instance.DefaultAlertStyle = CboDefaultStyle.SelectedIndex;
            AppPrefs.Instance.Save();
        }

        private void SldDefaultFlashSecs_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (!_loaded) return;
            int v = (int)e.NewValue;
            AppPrefs.Instance.DefaultFlashSecs = v;
            LblDefaultFlashSecs.Text = v + " s";
            AppPrefs.Instance.Save();
        }

        private void SldDefaultFontSize_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (!_loaded) return;
            int v = (int)e.NewValue;
            AppPrefs.Instance.DefaultFontSize = v;
            LblDefaultFontSize.Text = v + " pt";
            AppPrefs.Instance.Save();
        }

        private void ChkDefaultAutoReset_Changed(object sender, RoutedEventArgs e)
        {
            if (!_loaded) return;
            AppPrefs.Instance.DefaultAutoReset = ChkDefaultAutoReset.IsChecked == true;
            AppPrefs.Instance.Save();
        }

        // ── Calendar event defaults ───────────────────────────────────────────

        private void BtnSdCalEvtPos_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not System.Windows.Controls.Button btn) return;
            if (!int.TryParse(btn.Tag?.ToString(), out int pos)) return;
            AppPrefs.Instance.CalEvtLensPosition = pos;
            AppPrefs.Instance.Save();
            UpdateSdCalEvtPosButtons(pos);
        }

        private void UpdateSdCalEvtPosButtons(int selected)
        {
            var grid = SdBtnCalEvtPosTopLeft?.Parent as System.Windows.Controls.Grid;
            if (grid == null) return;
            foreach (UIElement el in grid.Children)
            {
                if (el is not System.Windows.Controls.Button b) continue;
                if (!int.TryParse(b.Tag?.ToString(), out int tag)) continue;
                b.FontWeight = tag == selected ? FontWeights.Bold : FontWeights.Normal;
            }
        }

        private void TxtSdCalEvtFlashSeconds_LostFocus(object sender, RoutedEventArgs e)
        {
            if (!_loaded) return;
            if (int.TryParse(SdTxtCalEvtFlashSeconds.Text, out int v) && v >= 0)
                AppPrefs.Instance.CalEvtFlashSeconds = v;
            else
                SdTxtCalEvtFlashSeconds.Text = AppPrefs.Instance.CalEvtFlashSeconds.ToString();
            AppPrefs.Instance.Save();
        }

        private void TxtSdCalEvtAlertRepeatLimit_LostFocus(object sender, RoutedEventArgs e)
        {
            if (!_loaded) return;
            if (int.TryParse(SdTxtCalEvtAlertRepeatLimit.Text, out int v) && v >= 0)
                AppPrefs.Instance.CalEvtAlertRepeatLimit = v;
            else
                SdTxtCalEvtAlertRepeatLimit.Text = AppPrefs.Instance.CalEvtAlertRepeatLimit.ToString();
            AppPrefs.Instance.Save();
        }

        private void TxtSdCalEvtEscalateAfterSeconds_LostFocus(object sender, RoutedEventArgs e)
        {
            if (!_loaded) return;
            if (int.TryParse(SdTxtCalEvtEscalateAfterSeconds.Text, out int v) && v >= 0)
                AppPrefs.Instance.CalEvtEscalateAfterSeconds = v;
            else
                SdTxtCalEvtEscalateAfterSeconds.Text = AppPrefs.Instance.CalEvtEscalateAfterSeconds.ToString();
            AppPrefs.Instance.Save();
        }

        private void ChkSdCalEvt_Changed(object sender, RoutedEventArgs e)
        {
            if (!_loaded) return;
            AppPrefs.Instance.CalEvtExcludeFromPill     = SdChkCalEvtExcludeFromPill.IsChecked    == true;
            AppPrefs.Instance.CalEvtReminderEnabled     = SdChkCalEvtReminderEnabled.IsChecked    == true;
            AppPrefs.Instance.CalEvtSendEmailOnReminder = SdChkCalEvtSendEmailOnReminder.IsChecked== true;
            AppPrefs.Instance.Save();
            SdPnlCalEvtReminderDetails.Visibility = AppPrefs.Instance.CalEvtReminderEnabled
                ? Visibility.Visible : Visibility.Collapsed;
        }

        private void TxtSdCalEvtReminderLeadMinutes_LostFocus(object sender, RoutedEventArgs e)
        {
            if (!_loaded) return;
            if (int.TryParse(SdTxtCalEvtReminderLeadMinutes.Text, out int v) && v >= 1)
                AppPrefs.Instance.CalEvtReminderLeadMinutes = v;
            else
                SdTxtCalEvtReminderLeadMinutes.Text = AppPrefs.Instance.CalEvtReminderLeadMinutes.ToString();
            AppPrefs.Instance.Save();
        }

        private void TxtSdCalEvtReminderIntervalMinutes_LostFocus(object sender, RoutedEventArgs e)
        {
            if (!_loaded) return;
            if (int.TryParse(SdTxtCalEvtReminderIntervalMinutes.Text, out int v) && v >= 0)
                AppPrefs.Instance.CalEvtReminderIntervalMinutes = v;
            else
                SdTxtCalEvtReminderIntervalMinutes.Text = AppPrefs.Instance.CalEvtReminderIntervalMinutes.ToString();
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
            var (w, h) = WindowSizeCache.Get("SettingsDialog", 560, 760);
            Width = w; Height = h;
        }

        protected override void OnClosed(EventArgs e)
        {
            WindowSizeCache.Set("SettingsDialog", Width, Height);
            base.OnClosed(e);
        }
    }
}
