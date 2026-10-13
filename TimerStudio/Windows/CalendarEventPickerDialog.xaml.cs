using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace TimerStudio
{
    public partial class CalendarEventPickerDialog : Window
    {
        public List<CalendarEvent> SelectedEvents { get; private set; } = new List<CalendarEvent>();

        // Keeps the discovered Outlook folder objects alive across the two phases
        private List<OutlookCalendarFolder> _outlookFolders;

        public CalendarEventPickerDialog()
        {
            InitializeComponent();
            Loaded += OnLoaded;
            LstEvents.SelectionChanged += LstEvents_SelectionChanged;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            bool avail = CalendarImporter.IsOutlookAvailable();
            BtnImportOutlook.IsEnabled = avail;
            LblOutlookStatus.Text      = avail ? "" : "(Outlook not installed)";
        }

        // ── ICS file ──────────────────────────────────────────────────────────
        private void BtnImportIcs_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Open Calendar File",
                Filter = "iCalendar files (*.ics)|*.ics|All files (*.*)|*.*",
                DefaultExt = ".ics"
            };
            if (dlg.ShowDialog(this) != true) return;

            PnlCalendarPicker.Visibility = Visibility.Collapsed;
            ShowLoading(true, "Reading calendar file\u2026");
            string path = dlg.FileName;

            var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
            t.Tick += (s, _) =>
            {
                t.Stop();
                try   { PopulateList(CalendarImporter.ParseIcs(path), "ICS file"); }
                catch (Exception ex)
                {
                    AppDialog.Show("Could not read the calendar file:\n" + ex.Message,
                        "Import Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                    ShowLoading(false);
                }
            };
            t.Start();
        }

        // ── Outlook: Phase 1 — enumerate calendars ────────────────────────────
        private void BtnImportOutlook_Click(object sender, RoutedEventArgs e)
        {
            ShowLoading(true, "Finding Outlook calendars\u2026");
            PnlCalendarPicker.Visibility = Visibility.Collapsed;
            PnlSelectionBar.Visibility   = Visibility.Collapsed;
            LstEvents.Items.Clear();

            var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
            t.Tick += (s, _) =>
            {
                t.Stop();
                try
                {
                    _outlookFolders = CalendarImporter.GetOutlookCalendarFolders();
                    ShowLoading(false);

                    if (_outlookFolders.Count == 0)
                    {
                        LblSubtitle.Text = "No calendar folders found in Outlook.";
                        return;
                    }

                    // Filter out hidden calendars before populating
                    var hiddenPaths = AppPrefs.Instance.HiddenCalendarPaths;
                    _outlookFolders = _outlookFolders.FindAll(f =>
                        !hiddenPaths.Exists(h => string.Equals(h, f.FullPath,
                            StringComparison.OrdinalIgnoreCase)));

                    if (_outlookFolders.Count == 0)
                    {
                        LblSubtitle.Text = "All Outlook calendars are hidden. Un-hide in \ud83d\udcc5 Calendars settings.";
                        ShowLoading(false);
                        return;
                    }

                    // Populate the calendar picker ComboBox
                    CboCalendars.Items.Clear();
                    string defaultPath = AppPrefs.Instance.DefaultCalendarPath;
                    int defaultIdx = 0;
                    for (int i = 0; i < _outlookFolders.Count; i++)
                    {
                        var f = _outlookFolders[i];
                        CboCalendars.Items.Add(f.Name + "  \u2014  " + f.FullPath);
                        if (!string.IsNullOrEmpty(defaultPath) &&
                            string.Equals(f.FullPath, defaultPath,
                                StringComparison.OrdinalIgnoreCase))
                            defaultIdx = i;
                    }

                    CboCalendars.SelectedIndex   = defaultIdx;
                    PnlCalendarPicker.Visibility  = Visibility.Visible;
                    BtnLoadEvents.IsEnabled       = true;
                    LblSubtitle.Text = "Select a calendar then click Load Events.";
                }
                catch (Exception ex)
                {
                    Debug.WriteLine("[TimerStudio] Folder enum error: " + ex.Message);
                    AppDialog.Show("Could not read Outlook calendars:\n\n" + ex.Message
                        + "\n\nTry exporting as an .ics file instead.",
                        "Outlook Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                    ShowLoading(false);
                }
            };
            t.Start();
        }

        private void CboCalendars_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            BtnLoadEvents.IsEnabled = CboCalendars.SelectedIndex >= 0;
        }

        // ── Outlook: Phase 2 — load events from chosen calendar ───────────────
        private void BtnLoadEvents_Click(object sender, RoutedEventArgs e)
        {
            int idx = CboCalendars.SelectedIndex;
            if (idx < 0 || _outlookFolders == null || idx >= _outlookFolders.Count) return;

            var chosen = _outlookFolders[idx];
            ShowLoading(true, "Loading events from \"" + chosen.Name + "\"\u2026");

            var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
            t.Tick += (s, _) =>
            {
                t.Stop();
                try
                {
                    var events = CalendarImporter.ReadOutlookFolder(chosen, 300);
                    PopulateList(events, chosen.Name);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine("[TimerStudio] ReadOutlookFolder error: " + ex.Message);
                    AppDialog.Show("Could not read events from \"" + chosen.Name + "\":\n\n"
                        + ex.Message, "Outlook Error",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    ShowLoading(false);
                }
            };
            t.Start();
        }

        // ── List population ───────────────────────────────────────────────────
        private void PopulateList(List<CalendarEvent> events, string source)
        {
            ShowLoading(false);
            LstEvents.SelectionChanged -= LstEvents_SelectionChanged;
            LstEvents.Items.Clear();
            LstEvents.SelectionChanged += LstEvents_SelectionChanged;
            PnlDetail.Visibility      = Visibility.Collapsed;
            PnlSelectionBar.Visibility = Visibility.Collapsed;
            BtnImport.IsEnabled        = false;
            BtnImportText.Text         = "Import (0 selected)";
            SelectedEvents.Clear();

            if (events.Count == 0)
            {
                LblCount.Text    = "No upcoming events found in " + source + ".";
                LblSubtitle.Text = "No events found — try a different calendar or date range.";
                return;
            }

            foreach (var ev in events)
                LstEvents.Items.Add(ev);

            LblCount.Text          = events.Count + " event" + (events.Count == 1 ? "" : "s")
                                   + " from " + source;
            LblSubtitle.Text       = "Tick events to select, then click Import.";
            PnlSelectionBar.Visibility = Visibility.Visible;
        }

        private void ShowLoading(bool loading, string msg = "Loading\u2026")
        {
            LblLoading.Text       = msg;
            LblLoading.Visibility = loading ? Visibility.Visible  : Visibility.Collapsed;
            LstEvents.Visibility  = loading ? Visibility.Collapsed : Visibility.Visible;
        }

        // ── Select All / Clear ────────────────────────────────────────────────
        private void BtnSelectAll_Click(object sender, RoutedEventArgs e)  => LstEvents.SelectAll();
        private void BtnSelectNone_Click(object sender, RoutedEventArgs e) => LstEvents.UnselectAll();

        // ── Row click → toggle ────────────────────────────────────────────────
        private void LstItem_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            var item = sender as ListViewItem;
            if (item == null) return;
            if ((Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Shift)) == 0)
            {
                item.IsSelected = !item.IsSelected;
                e.Handled = true;
            }
        }

        // ── Selection changed ─────────────────────────────────────────────────
        private void LstEvents_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            int count = LstEvents.SelectedItems.Count;
            BtnImport.IsEnabled = count > 0;
            BtnImportText.Text  = count == 0 ? "Import (0 selected)"
                                : count == 1 ? "Import 1 Event"
                                :              "Import " + count + " Events";

            if (count == 0) { PnlDetail.Visibility = Visibility.Collapsed; return; }

            PnlDetail.Visibility = Visibility.Visible;
            if (count == 1)
            {
                var ev = LstEvents.SelectedItem as CalendarEvent;
                if (ev != null)
                {
                    string calTag = string.IsNullOrWhiteSpace(ev.CalendarName)
                        ? "" : "  \u2022  " + ev.CalendarName;
                    LblDetailSummary.Text = ev.IsAllDay
                        ? ev.Summary + "  \u2014  " +
                          ev.StartLocal.ToString("dddd, MMMM d, yyyy") +
                          "  (all-day \u2192 9:00 AM)" + calTag
                        : ev.Summary + "  \u2014  " +
                          ev.StartLocal.ToString("ddd, MMM d yyyy  h:mm:ss tt") + calTag;
                }
            }
            else
            {
                LblDetailSummary.Text = count + " events selected \u2014 "
                    + "each will create a separate timer card.";
            }
        }

        // ── Double-click ──────────────────────────────────────────────────────
        private void LstEvents_DoubleClick(object sender, MouseButtonEventArgs e)
        {
            var hit = LstEvents.InputHitTest(e.GetPosition(LstEvents)) as DependencyObject;
            while (hit != null && !(hit is ListViewItem))
                hit = System.Windows.Media.VisualTreeHelper.GetParent(hit);
            var item = hit as ListViewItem;
            if (item?.DataContext is CalendarEvent)
            {
                LstEvents.SelectedItem = item.DataContext;
                CommitSelection();
            }
        }

        // ── Commit ────────────────────────────────────────────────────────────
        private void BtnImport_Click(object sender, RoutedEventArgs e) => CommitSelection();

        private void CommitSelection()
        {
            var chosen = LstEvents.SelectedItems.Cast<CalendarEvent>().ToList();
            if (chosen.Count == 0) return;

            bool hasAllDay = chosen.Any(ev => ev.IsAllDay);
            if (hasAllDay)
            {
                int n = chosen.Count(ev => ev.IsAllDay);
                if (AppDialog.Show(
                    n + " selected event" + (n == 1 ? " is an" : "s are") + " all-day.\n\n" +
                    "Timer Studio will set the alert to 9:00 AM on the event date.\n" +
                    "You can adjust the time after importing.",
                    "All-Day Events", MessageBoxButton.OKCancel,
                    MessageBoxImage.Information) != MessageBoxResult.OK)
                    return;
            }

            SelectedEvents = chosen.Select(ev =>
            {
                if (!ev.IsAllDay) return ev;
                return new CalendarEvent
                {
                    Summary      = ev.Summary,
                    Description  = ev.Description,
                    Location     = ev.Location,
                    CalendarName = ev.CalendarName,
                    StartUtc     = ev.StartLocal.Date.AddHours(9).ToUniversalTime(),
                    IsAllDay     = false
                };
            }).ToList();

            DialogResult = true;
            Close();
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            SelectedEvents.Clear();
            DialogResult = false;
            Close();
        }

        // ── Window size persistence ───────────────────────────────────────────
        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            var (w, h) = WindowSizeCache.Get("CalendarEventPicker", 560, 580);
            Width = w; Height = h;
        }

        protected override void OnClosed(EventArgs e)
        {
            WindowSizeCache.Set("CalendarEventPicker", Width, Height);
            base.OnClosed(e);
        }
    }
}
