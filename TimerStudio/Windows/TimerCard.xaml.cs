using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace TimerStudio
{
    /// <summary>
    /// Code-behind for TimerCard.xaml.
    /// All logic lives in TimerCardViewModel. This file is wiring only:
    ///   - DataContext assignment
    ///   - Controls that WPF cannot bind declaratively (color palettes, drag, DatePicker sync)
    ///   - Keyboard handling (Enter/Escape/Ctrl+Z in TextBox context)
    /// </summary>
    public partial class TimerCard : UserControl
    {
        /// <summary>
        /// Number of built-in preset items at the top of CboPreset (M-3).
        /// Update this constant when adding/removing built-in presets — do not use a magic literal.
        /// </summary>
        private const int BuiltInPresetCount = 7;

        private TimerCardViewModel _vm;
        private bool               _isPm;
        private Button[]           _posBtns;

        // Color palettes (data, not logic)
        private static readonly Color[] BgPalette = {
            Color.FromRgb(243,139,168), Color.FromRgb(249,226,175), Color.FromRgb(166,227,161),
            Color.FromRgb(137,180,250), Color.FromRgb(180,190,254), Color.FromRgb(148,226,213),
            Color.FromRgb(245,194,231), Color.FromRgb(250,179,135), Color.FromRgb(235,59,90),
            Color.FromRgb(250,176,5),   Color.FromRgb(55,178,77),   Color.FromRgb(34,139,230),
            Color.FromRgb(132,94,247),  Color.FromRgb(0,184,217),   Color.FromRgb(240,62,133),
            Color.FromRgb(255,255,255)
        };
        private static readonly Color[] TitlePalette = {
            Colors.White, Color.FromRgb(240,240,240), Color.FromRgb(30,30,30),
            Color.FromRgb(30,30,46),    Color.FromRgb(249,226,175), Color.FromRgb(137,180,250),
            Color.FromRgb(166,227,161), Color.FromRgb(243,139,168), Color.FromRgb(148,226,213),
            Color.FromRgb(250,179,135), Color.FromRgb(180,190,254), Color.FromRgb(245,194,231)
        };

        // ── Public surface ────────────────────────────────────────────────────
        public TimerModel         Model     => _vm?.Model;
        public TimerCardViewModel ViewModel => _vm;
        public event EventHandler DeleteRequested;

        /// <summary>Raised when the bulk-select checkbox is toggled — used to refresh the main toolbar's Delete Selected button.</summary>
        public event EventHandler SelectionChanged;

        /// <summary>Bulk-select state for "Delete Selected" in the main toolbar.</summary>
        public bool IsSelected
        {
            get => ChkSelect.IsChecked == true;
            set => ChkSelect.IsChecked = value;
        }

        private void ChkSelect_Changed(object sender, RoutedEventArgs e)
            => SelectionChanged?.Invoke(this, EventArgs.Empty);

        /// <summary>Called by "Start All in category" — no-op if not Idle.</summary>
        public void StartIfIdle()    => _vm?.RequestStart();
        /// <summary>Called by "Pause All in category" — no-op if not Running.</summary>
        public void PauseIfRunning() => _vm?.RequestPause();

        // ── Constructor ───────────────────────────────────────────────────────
        public TimerCard(TimerModel model, MainWindow mainWindow)
        {
            InitializeComponent();
            _vm = new TimerCardViewModel(model, mainWindow);
            DataContext = _vm;

            _vm.DeleteRequested += (s, e) => DeleteRequested?.Invoke(this, e);

            // Set TxtName synchronously — the {Binding Name, Mode=OneWay} initialises
            // after layout which leaves a large blank gap on first render.
            TxtName.Text = model.Name;

            // Also refresh on Loaded for any late-binding edge cases
            Loaded += (s, e) =>
            {
                if (string.IsNullOrEmpty(TxtName.Text)) TxtName.Text = _vm.Name;
                RefreshTitleFont();
                RefreshTimeFont();
                RefreshCategoryDot();
                RefreshOverlayPinButton();
                ApplyCardBackground();
            };

            // Controls that need imperative init (not covered by XAML bindings)
            CboMode.SelectedIndex       = model.Mode == TimerMode.Countdown ? 0
                                        : model.Mode == TimerMode.Stopwatch  ? 1
                                        : model.Mode == TimerMode.DateTime   ? 2
                                        : model.Mode == TimerMode.Pomodoro   ? 3 : 4; // 4 = Interval
            CboSound.SelectedIndex      = (int)model.AlertSound;
            CboAlertStyle.SelectedIndex = (int)model.AlertStyle;
            // Force full Visibility refresh: if CboMode.SelectedIndex matched the
            // model's current Mode, SelectionChanged's setter guard skips
            // RaiseUiStateChanged — so call it explicitly here to guarantee the
            // correct panels (e.g. PnlDateTimeInputs) are visible on load.
            _vm.RefreshUiState();

            // Slider display labels
            LblFontSizeVal.Text  = model.AlertFontSize.ToString();
            LblSnoozeVal.Text    = model.SnoozeDurationMinutes + " min";
            LblVolumeVal.Text    = (model.AlertVolume * 100).ToString("F0") + "%";
            LblCustomSound.Text  = string.IsNullOrEmpty(model.CustomSoundPath)
                ? "None" : System.IO.Path.GetFileName(model.CustomSoundPath);

            // Feature 1: Recurrence init
            CboRecurrence.SelectedIndex = (int)model.RecurrenceMode;

            // Reminder controls
            int leadMins = Math.Max(1, Math.Min(4320, model.ReminderLeadMinutes));
            int intMins  = Math.Max(0, Math.Min(4320, model.ReminderIntervalMinutes));
            SldReminderLead.Value     = leadMins;
            SldReminderInterval.Value = intMins;
            TxtReminderLeadH.Text = (leadMins / 60).ToString();
            TxtReminderLeadM.Text = (leadMins % 60).ToString();
            TxtReminderIntH.Text  = (intMins / 60).ToString();
            TxtReminderIntM.Text  = (intMins % 60).ToString();
            UpdateReminderLeadLabel(leadMins);
            UpdateReminderIntervalLabel(intMins);
            ChkSendEmailOnReminder.IsChecked = model.SendEmailOnReminder;
            TxtAlertEmailTo.Text = model.AlertEmailTo ?? "";

            _isPm = model.TargetDateTime.Hour >= 12;
            int dispHour = model.TargetDateTime.Hour % 12;
            if (dispHour == 0) dispHour = 12;
            DpTargetDate.SelectedDate = model.TargetDateTime.Date;
            TxtDtHour.Text   = dispHour.ToString("D2");
            TxtDtMinute.Text = model.TargetDateTime.Minute.ToString("D2");
            TxtDtSecond.Text = model.TargetDateTime.Second.ToString("D2");
            if (BtnAmPm != null) BtnAmPm.Content = _isPm ? "PM" : "AM";

            // Build color palettes (code-generated, not declarable in XAML)
            BuildColorPalette(PnlColors,      BgPalette,    isBg: true);
            BuildColorPalette(PnlTitleColors, TitlePalette, isBg: false);

            // Position buttons array
            _posBtns = new Button[]
            {
                BtnPosTopLeft, BtnPosTopCenter, BtnPosTopRight,
                BtnPosMidLeft, BtnPosCenter,    BtnPosMidRight,
                BtnPosBotLeft, BtnPosBotCenter, BtnPosBotRight
            };
            RefreshPosButtons();

            // Wire events not coverable by binding
            DpTargetDate.SelectedDateChanged += DpTargetDate_Changed;
            TxtDtHour.TextChanged   += DtTime_Changed;
            TxtDtMinute.TextChanged += DtTime_Changed;
            TxtDtSecond.TextChanged += DtTime_Changed;
            TxtDtHour.PreviewTextInput   += NumOnly;
            TxtDtMinute.PreviewTextInput += NumOnly;
            TxtDtSecond.PreviewTextInput += NumOnly;
            TxtHours.PreviewTextInput    += NumOnly;
            TxtMinutes.PreviewTextInput  += NumOnly;
            TxtSeconds.PreviewTextInput  += NumOnly;
            TxtFlashSeconds.PreviewTextInput += NumOnly;
            TxtReminderLeadH.PreviewTextInput += NumOnly;
            TxtReminderLeadM.PreviewTextInput += NumOnly;
            TxtReminderIntH.PreviewTextInput  += NumOnly;
            TxtReminderIntM.PreviewTextInput  += NumOnly;
            // Feature 1: Recurrence numeric guards
            TxtRecurrenceInterval.PreviewTextInput += NumOnly;
            // Feature 3: Volume slider
            SldVolume.ValueChanged += SldVolume_ValueChanged;
            // Feature 8: Pomodoro numeric guards
            TxtPomoWork.PreviewTextInput      += NumOnly;
            TxtPomoBreak.PreviewTextInput     += NumOnly;
            TxtPomoLong.PreviewTextInput      += NumOnly;
            TxtPomoLongAfter.PreviewTextInput += NumOnly;
            // F9: Alert repeat limit
            TxtAlertRepeatLimit.PreviewTextInput += NumOnly;
            // F11: Escalate after
            TxtEscalateAfter.PreviewTextInput    += NumOnly;
            // F14: Interval numeric guards
            TxtIntervalWork.PreviewTextInput   += NumOnly;
            TxtIntervalRest.PreviewTextInput   += NumOnly;
            TxtIntervalCycles.PreviewTextInput += NumOnly;
            SldReminderLead.ValueChanged     += SldReminderLead_Changed;
            SldReminderInterval.ValueChanged  += SldReminderInterval_Changed;
            TxtReminderLeadH.TextChanged += TxtReminderLead_Changed;
            TxtReminderLeadM.TextChanged += TxtReminderLead_Changed;
            TxtReminderIntH.TextChanged  += TxtReminderInt_Changed;
            TxtReminderIntM.TextChanged  += TxtReminderInt_Changed;

            // ViewModel property changes that need code-behind response
            _vm.PropertyChanged += Vm_PropertyChanged;
        }

        // ── Cleanup (call before removing from visual tree) ───────────────────
        /// <summary>
        /// Unsubscribes PropertyChanged and stops all VM timers so the WPF Dispatcher
        /// releases its GC root on _alertRepeat, allowing this card and its VM to be
        /// collected. Must be called before TimerPanel.Children.Remove(card).
        /// </summary>
        public void Cleanup()
        {
            _vm.PropertyChanged -= Vm_PropertyChanged;
            _vm.Cleanup();
        }

        // ── Tick (called by MainWindow every 40ms) ────────────────────────────
        public void Tick() => _vm.Tick();

        // ── Font refresh (called by MainWindow when global scale changes) ─────
        public void RefreshTimeFont()
        {
            if (LblTime != null) LblTime.FontSize = _vm.ComputeTimeFontSize();
        }
        public void RefreshTitleFont()
        {
            if (TxtName != null) TxtName.FontSize = _vm.ComputeTitleFontSize();
        }
        /// <summary>
        /// Called after a theme change to re-evaluate the card background.
        /// SetResourceReference (used in ApplyCardBackground) is self-updating, but
        /// this also keeps alert-colour overrides correct after a theme swap.
        /// </summary>
        public void RefreshUI() => ApplyCardBackground();
        public void RefreshCategoryDot()
        {
            if (CategoryDot == null) return;
            if (string.IsNullOrWhiteSpace(_vm.Model.Category))
            {
                CategoryDot.Fill = FindResource("BrushHint") as Brush;
                CategoryLabel.Text  = "No category";
                BtnCategory.ToolTip = "Set category";
                System.Windows.Automation.AutomationProperties.SetName(
                    BtnCategory, "Set category for this timer (no category assigned)");
            }
            else
            {
                var color = MainWindow.CategoryColor(_vm.Model.Category);
                CategoryDot.Fill    = new SolidColorBrush(color);
                CategoryLabel.Text  = _vm.Model.Category;
                BtnCategory.ToolTip = "Category: " + _vm.Model.Category;
                System.Windows.Automation.AutomationProperties.SetName(
                    BtnCategory, "Category: " + _vm.Model.Category + " — click to change");
            }
        }
        public void RefreshOverlayPinButton()
        {
            if (BtnOverlayPin == null) return;
            // Fixed pin icon; state = pressed look + Name/ToolTip (was a 📌/📍 glyph swap only).
            bool pinned = _vm.Model.ShowInOverlay;
            string tip  = pinned ? "Pinned to overlay — click to unpin" : "Not pinned to overlay — click to pin";
            BtnOverlayPin.ToolTip = tip;
            System.Windows.Automation.AutomationProperties.SetName(BtnOverlayPin, tip);
            if (pinned)
            {
                BtnOverlayPin.SetResourceReference(System.Windows.Controls.Control.BackgroundProperty,  "BrushSurf2");
                BtnOverlayPin.SetResourceReference(System.Windows.Controls.Control.BorderBrushProperty, "BrushBlue");
            }
            else
            {
                BtnOverlayPin.ClearValue(System.Windows.Controls.Control.BackgroundProperty);
                BtnOverlayPin.ClearValue(System.Windows.Controls.Control.BorderBrushProperty);
            }
        }
        public void RestartTimer() => _vm.RestartTimer();
        public void AutoStartIfDateTime() => _vm.AutoStartIfDateTime();
        public void FireMissedAlert()     => _vm.FireMissedAlert();
        public void RequestDelete()       => _vm.RequestDelete();
        public void RefreshPresetTemplates()
        {
            if (CboPreset == null) return;
            var mainWin   = Window.GetWindow(this) as MainWindow;
            var templates = mainWin?.GetTemplates() ?? new System.Collections.Generic.List<TimerDto>();
            while (CboPreset.Items.Count > BuiltInPresetCount) CboPreset.Items.RemoveAt(CboPreset.Items.Count - 1);
            if (templates.Count > 0)
            {
                CboPreset.Items.Add(new Separator());
                foreach (var t in templates)
                    CboPreset.Items.Add(new ComboBoxItem { Content = t.Name });
            }
        }

        // ── ViewModel property change → imperative code-behind updates ────────
        private void Vm_PropertyChanged(object sender,
            System.ComponentModel.PropertyChangedEventArgs e)
        {
            switch (e.PropertyName)
            {
                case nameof(TimerCardViewModel.Name):
                    if (TxtName != null && TxtName.Text != _vm.Name) TxtName.Text = _vm.Name;
                    break;
                case nameof(TimerCardViewModel.LensPosition):
                    RefreshPosButtons(); break;
                case nameof(TimerCardViewModel.AlertColor):
                case nameof(TimerCardViewModel.AlertTitleColor):
                    RefreshColorDots(); break;
                case nameof(TimerCardViewModel.CardTint):
                case nameof(TimerCardViewModel.CardBackgroundBrush):
                    ApplyCardBackground(); break;
                case nameof(TimerCardViewModel.DisplayTime):
                    if (LblTime != null) LblTime.FontSize = _vm.ComputeTimeFontSize();
                    if (ProgressTrack?.Visibility == Visibility.Visible
                        && _vm.Model.TotalDuration.TotalSeconds > 0
                        && ProgressTrack.ActualWidth > 0)
                        ProgressBar.Width = ProgressTrack.ActualWidth * _vm.ProgressFraction;
                    break;
            }
        }

        private void ApplyCardBackground()
        {
            if (CardBorder == null) return;
            var brush = _vm.CardBackgroundBrush;
            if (brush != null)
                CardBorder.Background = brush;
            else
                // SetResourceReference keeps a live DynamicResource link — auto-updates on theme swap
                CardBorder.SetResourceReference(System.Windows.Controls.Border.BackgroundProperty, "BrushSurf");

            // Sync BtnCardTint swatch
            if (BtnCardTint != null)
            {
                var tint = _vm.CardTintBrush;
                if (tint != null)
                    CardTintSwatch.Background = tint;
                else
                    CardTintSwatch.SetResourceReference(System.Windows.Controls.Border.BackgroundProperty, "BrushSurf2");
            }
        }

        // ── DateTime picker sync ──────────────────────────────────────────────
        private bool _dtSyncing;

        private void DpTargetDate_Changed(object sender,
            SelectionChangedEventArgs e)
        {
            if (_dtSyncing || _vm == null || DpTargetDate.SelectedDate == null) return;
            UpdateTargetFromInputs();
        }

        private void DtTime_Changed(object sender, TextChangedEventArgs e)
        {
            if (_dtSyncing || _vm == null) return;
            UpdateTargetFromInputs();
        }

        private void UpdateTargetFromInputs()
        {
            DateTime date = DpTargetDate.SelectedDate ?? DateTime.Today;
            int.TryParse(TxtDtHour.Text,   out int h);
            int.TryParse(TxtDtMinute.Text, out int m);
            int.TryParse(TxtDtSecond.Text, out int s);
            h = Math.Max(1, Math.Min(12, h));
            m = Math.Max(0, Math.Min(59, m));
            s = Math.Max(0, Math.Min(59, s));
            int hour24 = _isPm ? (h == 12 ? 12 : h + 12) : (h == 12 ? 0 : h);
            try { _vm.TargetDateTime = new DateTime(date.Year, date.Month, date.Day, hour24, m, s); }
            catch { }
        }

        private void BtnAmPm_Click(object sender, RoutedEventArgs e)
        {
            _isPm = !_isPm;
            if (BtnAmPm != null) BtnAmPm.Content = _isPm ? "PM" : "AM";
            UpdateTargetFromInputs();
        }

        // ── Reminder slider↔box sync ──────────────────────────────────────────
        private bool _reminderSyncing;

        private void SldReminderLead_Changed(object sender,
            System.Windows.RoutedPropertyChangedEventArgs<double> e)
        {
            if (_vm == null || _reminderSyncing) return;
            _reminderSyncing = true;
            int total = (int)e.NewValue;
            _vm.ReminderLeadMinutes = total;
            TxtReminderLeadH.Text = (total / 60).ToString();
            TxtReminderLeadM.Text = (total % 60).ToString();
            UpdateReminderLeadLabel(total);
            _reminderSyncing = false;
        }

        private void TxtReminderLead_Changed(object sender, TextChangedEventArgs e)
        {
            if (_vm == null || _reminderSyncing) return;
            _reminderSyncing = true;
            int.TryParse(TxtReminderLeadH.Text, out int h);
            int.TryParse(TxtReminderLeadM.Text, out int m);
            int total = Math.Max(1, Math.Min(4320, h * 60 + m));
            _vm.ReminderLeadMinutes = total;
            SldReminderLead.Value = total;
            UpdateReminderLeadLabel(total);
            _reminderSyncing = false;
        }

        private void SldReminderInterval_Changed(object sender,
            System.Windows.RoutedPropertyChangedEventArgs<double> e)
        {
            if (_vm == null || _reminderSyncing) return;
            _reminderSyncing = true;
            int total = (int)e.NewValue;
            _vm.ReminderIntervalMinutes = total;
            TxtReminderIntH.Text = (total / 60).ToString();
            TxtReminderIntM.Text = (total % 60).ToString();
            UpdateReminderIntervalLabel(total);
            _reminderSyncing = false;
        }

        private void TxtReminderInt_Changed(object sender, TextChangedEventArgs e)
        {
            if (_vm == null || _reminderSyncing) return;
            _reminderSyncing = true;
            int.TryParse(TxtReminderIntH.Text, out int h);
            int.TryParse(TxtReminderIntM.Text, out int m);
            int total = Math.Max(0, Math.Min(4320, h * 60 + m));
            _vm.ReminderIntervalMinutes = total;
            SldReminderInterval.Value = total;
            UpdateReminderIntervalLabel(total);
            _reminderSyncing = false;
        }

        private void UpdateReminderLeadLabel(int mins)
        {
            if (LblReminderLeadHuman == null) return;
            if (mins >= 1440) LblReminderLeadHuman.Text = $"= {mins / 1440}d {(mins % 1440) / 60}h before target";
            else if (mins >= 60) LblReminderLeadHuman.Text = $"= {mins / 60}h {mins % 60}m before target";
            else LblReminderLeadHuman.Text = $"= {mins} min before target";
        }
        private void UpdateReminderIntervalLabel(int mins)
        {
            if (LblReminderIntervalHuman == null) return;
            if (mins == 0) LblReminderIntervalHuman.Text = "= remind once only";
            else if (mins >= 60) LblReminderIntervalHuman.Text = $"= every {mins / 60}h {mins % 60}m";
            else LblReminderIntervalHuman.Text = $"= every {mins} min";
        }

        // ── Preset combo ──────────────────────────────────────────────────────
        private void CboPreset_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_vm == null || CboPreset.SelectedIndex <= 0) return;
            int idx = CboPreset.SelectedIndex;
            var mainWin   = Window.GetWindow(this) as MainWindow;
            var templates = mainWin?.GetTemplates() ?? new System.Collections.Generic.List<TimerDto>();
            int templateStart = BuiltInPresetCount;
            // H-6: idx == templateStart is the Separator item — use > not >= to skip it.
            // The -1 below adjusts for the Separator sitting at exactly templateStart.
            if (idx > templateStart)
            {
                int tIdx = idx - templateStart - 1;
                if (tIdx >= 0 && tIdx < templates.Count && mainWin != null)
                    mainWin.ApplyTemplate(templates[tIdx], _vm.Model);
                CboPreset.SelectedIndex = 0;
                return;
            }
            ApplyBuiltinPreset(idx);
            CboPreset.SelectedIndex = 0;
        }

        private void ApplyBuiltinPreset(int idx)
        {
            switch (idx)
            {
                case 1: SetCountdown(0, 25, 0); break;
                case 2: SetCountdown(0,  5, 0); break;
                case 3: SetCountdown(0, 15, 0); break;
                case 4: SetCountdown(1,  0, 0); break;
                case 5:
                {
                    var t = DateTime.Today.AddHours(17);
                    if (t <= DateTime.Now) t = t.AddDays(1);
                    SyncDateTimeControls(t);
                    break;
                }
                case 6:
                {
                    var t = DateTime.Today.AddDays(1);
                    SyncDateTimeControls(t);
                    break;
                }
            }
        }

        private void SetCountdown(int h, int m, int s)
        {
            _vm.Mode    = TimerMode.Countdown;
            _vm.Hours   = h; _vm.Minutes = m; _vm.Seconds = s;
            TxtHours.Text = h.ToString(); TxtMinutes.Text = m.ToString(); TxtSeconds.Text = s.ToString();
        }

        private void SyncDateTimeControls(DateTime target)
        {
            _vm.Mode = TimerMode.DateTime;
            _vm.TargetDateTime = target;
            _dtSyncing = true;
            DpTargetDate.SelectedDate = target.Date;
            _isPm = target.Hour >= 12;
            int dh = target.Hour % 12; if (dh == 0) dh = 12;
            TxtDtHour.Text = dh.ToString("D2");
            TxtDtMinute.Text = target.Minute.ToString("D2");
            TxtDtSecond.Text = target.Second.ToString("D2");
            if (BtnAmPm != null) BtnAmPm.Content = _isPm ? "PM" : "AM";
            _dtSyncing = false;
        }

        // ── Category click handler ────────────────────────────────────────────
        private void BtnCategory_Click(object sender, RoutedEventArgs e)
        {
            var mainWin = Window.GetWindow(this) as MainWindow;
            if (mainWin == null) return;
            var cats = mainWin.GetAllCategories();
            if (cats.Count == 0) { mainWin.BtnManageCategories_Click(sender, e); return; }

            var menu = new ContextMenu();
            void AddItem(string label, Action action)
            {
                var item = new MenuItem { Header = label };
                item.Click += (s, _) => action();
                menu.Items.Add(item);
            }
            AddItem("(none)", () => { _vm.Category = ""; RefreshCategoryDot(); });
            foreach (var cat in cats)
            {
                string cap = cat;
                AddItem(cap, () => { _vm.Category = cap; RefreshCategoryDot(); });
            }
            menu.Items.Add(new Separator());
            AddItem("Manage categories…", () => mainWin.BtnManageCategories_Click(sender, e));
            menu.PlacementTarget = BtnCategory;
            menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
            menu.IsOpen = true;
        }

        // ── Calendar import ───────────────────────────────────────────────────
        private void BtnImportCalendar_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new CalendarEventPickerDialog { Owner = Window.GetWindow(this) };
            if (dlg.ShowDialog() != true || dlg.SelectedEvents?.Count == 0) return;
            ApplyCalendarEvent(dlg.SelectedEvents[0]);
            _vm.AutoStartIfDateTime();
            var mainWin = Window.GetWindow(this) as MainWindow;
            if (mainWin != null)
                for (int i = 1; i < dlg.SelectedEvents.Count; i++)
                    mainWin.AddTimerFromCalendarEvent(dlg.SelectedEvents[i]);
        }

        private void ApplyCalendarEvent(CalendarEvent ev)
        {
            if (_vm.Name.StartsWith("Timer "))
                _vm.Name = ev.Summary.Length > 255 ? ev.Summary[..255] : ev.Summary;
            var parts = new System.Collections.Generic.List<string>();
            if (!string.IsNullOrWhiteSpace(ev.Description)) parts.Add(ev.Description.Trim());
            if (!string.IsNullOrWhiteSpace(ev.Location))    parts.Add("📍 " + ev.Location.Trim());
            if (parts.Count > 0) { _vm.Notes = string.Join("\n", parts); if (TxtNotes != null) TxtNotes.Text = _vm.Notes; }
            if (ev.AlarmMinutes > 0 && !_vm.ReminderEnabled)
            {
                _vm.ReminderEnabled     = true;
                _vm.ReminderLeadMinutes = Math.Min(4320, ev.AlarmMinutes);
                if (SldReminderLead != null) SldReminderLead.Value = _vm.ReminderLeadMinutes;
                TxtReminderLeadH.Text = (_vm.ReminderLeadMinutes / 60).ToString();
                TxtReminderLeadM.Text = (_vm.ReminderLeadMinutes % 60).ToString();
                UpdateReminderLeadLabel(_vm.ReminderLeadMinutes);
            }
            SyncDateTimeControls(ev.StartLocal);
        }

        // ── Position buttons ──────────────────────────────────────────────────
        private void BtnPos_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag != null)
            {
                _vm.LensPosition = (LensPosition)int.Parse(btn.Tag.ToString());
                RefreshPosButtons();
            }
        }

        private void RefreshPosButtons()
        {
            if (_posBtns == null) return;
            var activeBg   = FindResource("BrushBlue")  as SolidColorBrush;
            var activeFg   = FindResource("BrushOnAccent") as SolidColorBrush;
            var inactiveBg = FindResource("BrushSurf")  as SolidColorBrush;
            var inactiveFg = FindResource("BrushText")  as SolidColorBrush;
            int selected   = (int)_vm.LensPosition;
            for (int i = 0; i < _posBtns.Length; i++)
            {
                if (_posBtns[i] == null) continue;
                _posBtns[i].Background = i == selected ? activeBg   : inactiveBg;
                _posBtns[i].Foreground = i == selected ? activeFg   : inactiveFg;
            }
        }

        // ── Color palette ─────────────────────────────────────────────────────
        private void BuildColorPalette(WrapPanel panel, Color[] colors, bool isBg)
        {
            panel.Children.Clear();
            foreach (Color c in colors)
            {
                Color cap = c;
                var el = new Ellipse { Width=20, Height=20, Margin=new Thickness(0,0,5,5), Cursor=Cursors.Hand,
                    ToolTip=string.Format("#{0:X2}{1:X2}{2:X2}",c.R,c.G,c.B), Fill=new SolidColorBrush(c) };
                if (c.R > 220 && c.G > 220 && c.B > 220)
                    { el.Stroke = new SolidColorBrush(Color.FromRgb(100,100,120)); el.StrokeThickness = 1; }
                el.MouseDown += (s, e) => { if (isBg) _vm.AlertColor = cap; else _vm.AlertTitleColor = cap; RefreshColorDots(); };
                panel.Children.Add(el);
            }
            // Custom picker button
            var btn = new Button { Content="＋", FontSize=12, Width=20, Height=20,
                Margin=new Thickness(0,0,5,5), Padding=new Thickness(0), MinHeight=0, MinWidth=0,
                Cursor=Cursors.Hand, ToolTip="Choose a custom color…", BorderThickness=new Thickness(0),
                Background=FindResource("BrushSurf2") as Brush, Foreground=FindResource("BrushText") as Brush };
            System.Windows.Automation.AutomationProperties.SetName(btn, isBg ? "Custom alert bg color" : "Custom alert title color");
            btn.Click += (s, e) =>
            {
                var dlg = new System.Windows.Forms.ColorDialog { FullOpen=true, AnyColor=true };
                var cur = isBg ? _vm.AlertColor : _vm.AlertTitleColor;
                dlg.Color = System.Drawing.Color.FromArgb(cur.R, cur.G, cur.B);
                if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                {
                    var picked = Color.FromRgb(dlg.Color.R, dlg.Color.G, dlg.Color.B);
                    if (isBg) _vm.AlertColor = picked; else _vm.AlertTitleColor = picked;
                    RefreshColorDots();
                }
            };
            panel.Children.Add(btn);
        }

        private void RefreshColorDots()
        {
            foreach (UIElement el in PnlColors.Children)
            {
                var dot = el as Ellipse; if (dot == null) continue;
                var br = dot.Fill as SolidColorBrush; if (br == null) continue;
                bool sel = br.Color == _vm.AlertColor;
                dot.Width  = sel ? 22 : 20; dot.Height = sel ? 22 : 20;
                dot.StrokeThickness = sel ? 2.5 : (dot.StrokeThickness == 1 ? 1 : 0);
                dot.Stroke = sel ? new SolidColorBrush(_vm.AlertColor)
                    : (dot.StrokeThickness == 1 ? new SolidColorBrush(Color.FromRgb(100,100,120)) : null);
            }
            foreach (UIElement el in PnlTitleColors.Children)
            {
                var dot = el as Ellipse; if (dot == null) continue;
                var br = dot.Fill as SolidColorBrush; if (br == null) continue;
                bool sel = br.Color == _vm.AlertTitleColor;
                dot.Width = sel ? 22 : 20; dot.Height = sel ? 22 : 20;
                if (sel) { dot.Stroke = new SolidColorBrush(_vm.AlertTitleColor); dot.StrokeThickness = 2.5; }
                else if (br.Color.R > 220 && br.Color.G > 220 && br.Color.B > 220)
                    { dot.Stroke = new SolidColorBrush(Color.FromRgb(100,100,120)); dot.StrokeThickness = 1; }
                else { dot.Stroke = null; dot.StrokeThickness = 0; }
            }
        }

        // ── Custom sound ──────────────────────────────────────────────────────
        private void BtnPickCustomSound_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.OpenFileDialog { Title="Choose Alert Sound",
                Filter="Audio files|*.wav;*.mp3;*.m4a;*.ogg|All files|*.*" };
            if (!string.IsNullOrEmpty(_vm.CustomSoundPath))
                dlg.InitialDirectory = System.IO.Path.GetDirectoryName(_vm.CustomSoundPath);
            if (dlg.ShowDialog() == true) { _vm.CustomSoundPath = dlg.FileName; LblCustomSound.ToolTip = dlg.FileName; }
        }
        private void BtnClearCustomSound_Click(object sender, RoutedEventArgs e)
            => _vm.CustomSoundPath = null;

        // ── Card tint ─────────────────────────────────────────────────────────
        private void BtnCardTint_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new System.Windows.Forms.ColorDialog { FullOpen=true, AnyColor=true };
            if (!string.IsNullOrEmpty(_vm.CardTint))
                try { var c = (Color)ColorConverter.ConvertFromString(_vm.CardTint); dlg.Color = System.Drawing.Color.FromArgb(c.R,c.G,c.B); } catch { }
            if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                _vm.CardTint = $"#{dlg.Color.R:X2}{dlg.Color.G:X2}{dlg.Color.B:X2}";
        }
        private void BtnClearCardTint_Click(object sender, RoutedEventArgs e)
            => _vm.CardTint = null;

        // ── Share / save template ─────────────────────────────────────────────
        private void BtnShareTimer_Click(object sender, RoutedEventArgs e)
            => (Window.GetWindow(this) as MainWindow)?.ShareTimer(_vm.Model);
        private void BtnSaveTemplate_Click(object sender, RoutedEventArgs e)
            => (Window.GetWindow(this) as MainWindow)?.SaveAsTemplate(_vm.Model);

        // ── Overlay pin ───────────────────────────────────────────────────────
        private void BtnOverlayPin_Click(object sender, RoutedEventArgs e)
        {
            var mainWin = Window.GetWindow(this) as MainWindow;
            mainWin?.ToggleOverlayPin(_vm.Model);
            RefreshOverlayPinButton();
        }

        // ── Numeric-only preview ──────────────────────────────────────────────
        private static void NumOnly(object sender, TextCompositionEventArgs e)
            => e.Handled = !int.TryParse(e.Text, out _);

        // ── Validation helper ─────────────────────────────────────────────────
        private static void SetValidationState(TextBox tb, bool valid)
        {
            if (tb == null) return;
            tb.BorderBrush     = valid ? Brushes.Transparent : Brushes.Red;
            tb.BorderThickness = valid ? new Thickness(1) : new Thickness(2);
            if (!valid && !(tb.ToolTip is string t && t.StartsWith("Value out")))
                tb.Tag = tb.ToolTip;
            tb.ToolTip = valid ? (tb.Tag ?? tb.ToolTip) : "Value out of range — will be clamped";
        }

        // ── Input handlers (bound via XAML events, write through to VM) ───────
        private void TxtName_GotFocus(object sender, RoutedEventArgs e)
        { TxtName.BorderThickness = new Thickness(0,0,0,1); TxtName.BorderBrush = (SolidColorBrush)FindResource("BrushBlue"); TxtName.SelectAll(); }
        private void TxtName_LostFocus(object sender, RoutedEventArgs e)
        {
            TxtName.BorderThickness = new Thickness(0);
            string t = TxtName.Text?.Trim();
            if (!string.IsNullOrWhiteSpace(t))
                _vm.Name = t;
            else
                TxtName.Text = _vm.Name; // restore on empty
        }
        private void TxtName_TextChanged(object sender, TextChangedEventArgs e)
        { if (!string.IsNullOrWhiteSpace(TxtName.Text)) _vm.Name = TxtName.Text; }
        private void TimeInput_Changed(object sender, TextChangedEventArgs e)
        {
            if (_vm == null) return;
            int.TryParse(TxtHours.Text, out int h); int.TryParse(TxtMinutes.Text, out int m); int.TryParse(TxtSeconds.Text, out int s);
            SetValidationState(TxtHours, h >= 0 && h <= 99); SetValidationState(TxtMinutes, m >= 0 && m <= 59); SetValidationState(TxtSeconds, s >= 0 && s <= 59);
            _vm.Hours = h; _vm.Minutes = m; _vm.Seconds = s;
        }
        private void TxtFlashSeconds_Changed(object sender, TextChangedEventArgs e)
        { if (_vm != null && int.TryParse(TxtFlashSeconds.Text, out int v)) _vm.FlashSeconds = v; }
        private void CboMode_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_vm == null || _vm.State != TimerState.Idle) return;
            _vm.Mode = CboMode.SelectedIndex == 0 ? TimerMode.Countdown
                     : CboMode.SelectedIndex == 1 ? TimerMode.Stopwatch
                     : CboMode.SelectedIndex == 2 ? TimerMode.DateTime
                     : CboMode.SelectedIndex == 3 ? TimerMode.Pomodoro
                     : TimerMode.Interval; // index 4
            // Always re-notify after mode assignment: the setter guard skips
            // RaiseUiStateChanged when Mode is unchanged on first load.
            _vm.RefreshUiState();
        }

        // ── F14: Interval input handlers ──────────────────────────────────────
        private void TxtIntervalWork_Changed(object sender, TextChangedEventArgs e)
        { if (_vm != null && int.TryParse(TxtIntervalWork.Text, out int v)) _vm.IntervalWorkMinutes = v; }
        private void TxtIntervalRest_Changed(object sender, TextChangedEventArgs e)
        { if (_vm != null && int.TryParse(TxtIntervalRest.Text, out int v)) _vm.IntervalRestMinutes = v; }
        private void TxtIntervalCycles_Changed(object sender, TextChangedEventArgs e)
        { if (_vm != null && int.TryParse(TxtIntervalCycles.Text, out int v)) _vm.IntervalRepeatCount = v; }

        // ── Feature 1: Recurrence combo ───────────────────────────────────────
        private void CboRecurrence_SelectionChanged(object sender, SelectionChangedEventArgs e)
        { if (_vm != null) _vm.RecurrenceMode = (RecurrenceMode)CboRecurrence.SelectedIndex; }

        // ── Feature 3: Volume slider ──────────────────────────────────────────
        private void SldVolume_ValueChanged(object sender,
            System.Windows.RoutedPropertyChangedEventArgs<double> e)
        {
            if (_vm == null) return;
            _vm.AlertVolume = e.NewValue;
            if (LblVolumeVal != null)
                LblVolumeVal.Text = (e.NewValue * 100).ToString("F0") + "%";
        }
        private void CboSound_SelectionChanged(object sender, SelectionChangedEventArgs e)
        { if (_vm != null) _vm.AlertSound = (AlertSound)CboSound.SelectedIndex; }
        private void CboAlertStyle_SelectionChanged(object sender, SelectionChangedEventArgs e)
        { if (_vm != null) _vm.AlertStyle = (AlertStyle)CboAlertStyle.SelectedIndex; }
        private void ChkAutoReset_Changed(object sender, RoutedEventArgs e)
        { if (_vm != null) _vm.AutoReset = ChkAutoReset.IsChecked == true; }
        private void ChkOverrideCategoryColor_Changed(object sender, RoutedEventArgs e)
        { if (_vm != null) _vm.OverrideCategoryColor = ChkOverrideCategoryColor.IsChecked == true; }
        private void ChkReminder_Changed(object sender, RoutedEventArgs e)
        { if (_vm != null) _vm.ReminderEnabled = ChkReminderEnabled.IsChecked == true; }
        private void ChkSendEmailOnReminder_Changed(object sender, RoutedEventArgs e)
        { if (_vm != null) _vm.SendEmailOnReminder = ChkSendEmailOnReminder.IsChecked == true; }
        private void TxtAlertEmailTo_TextChanged(object sender, TextChangedEventArgs e)
        { if (_vm != null) _vm.AlertEmailTo = TxtAlertEmailTo.Text; }
        private void SldFontSize_ValueChanged(object sender, System.Windows.RoutedPropertyChangedEventArgs<double> e)
        { if (_vm != null) { _vm.AlertFontSize = (int)SldFontSize.Value; if (LblFontSizeVal != null) LblFontSizeVal.Text = _vm.AlertFontSize.ToString(); } }
        private void SldSnooze_ValueChanged(object sender, System.Windows.RoutedPropertyChangedEventArgs<double> e)
        { if (_vm != null) { _vm.SnoozeDurationMinutes = (int)e.NewValue; if (LblSnoozeVal != null) LblSnoozeVal.Text = _vm.SnoozeDurationMinutes + " min"; } }
        // ── F22: Notes / markdown preview ────────────────────────────────────
        private void BtnNotesToggle_Click(object sender, RoutedEventArgs e)
        {
            if (_vm == null) return;
            _vm.NotesExpanded = !_vm.NotesExpanded;
            bool open = _vm.NotesExpanded;
            PnlNotesToolbar.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
            if (open)
                ShowNotesEditMode();
            else
            {
                TxtNotes.Visibility        = Visibility.Collapsed;
                RtbNotesPreview.Visibility = Visibility.Collapsed;
            }
        }

        private void BtnNotesEdit_Click(object sender, RoutedEventArgs e)
            => ShowNotesEditMode();

        private void BtnNotesPreview_Click(object sender, RoutedEventArgs e)
            => ShowNotesPreviewMode();

        private void ShowNotesEditMode()
        {
            TxtNotes.Visibility        = Visibility.Visible;
            RtbNotesPreview.Visibility = Visibility.Collapsed;
        }

        private void ShowNotesPreviewMode()
        {
            TxtNotes.Visibility        = Visibility.Collapsed;
            RtbNotesPreview.Visibility = Visibility.Visible;
            var textBrush  = TryFindResource("BrushText")  as System.Windows.Media.Brush
                             ?? System.Windows.Media.Brushes.White;
            var mutedBrush = TryFindResource("BrushMuted") as System.Windows.Media.Brush
                             ?? System.Windows.Media.Brushes.Gray;
            var codeBrush  = TryFindResource("BrushBlueText") as System.Windows.Media.Brush
                             ?? System.Windows.Media.Brushes.CornflowerBlue;
            MarkdownHelper.RenderInto(_vm.Notes ?? "", RtbNotesPreview, textBrush, mutedBrush, codeBrush);
        }

        private void TxtNotes_TextChanged(object sender, TextChangedEventArgs e)
        { if (_vm != null) _vm.Notes = TxtNotes.Text; }
        private void BtnPreview_Click(object sender, RoutedEventArgs e)
        { AlertSoundPlayer.PlayForModel(_vm.Model); }

        // ── Drag-to-reorder (MainWindow intercepts these at the panel level) ──
        // No changes needed — handled by MainWindow.xaml.cs

        // ── AutomationPeer ────────────────────────────────────────────────────
        protected override System.Windows.Automation.Peers.AutomationPeer OnCreateAutomationPeer()
            => new TimerCardAutomationPeer(this);

        // ── Keyboard ──────────────────────────────────────────────────────────
        protected override void OnPreviewKeyDown(KeyEventArgs e)
        {
            base.OnPreviewKeyDown(e);
            if (e.Key == Key.Enter && TxtName.IsFocused)
            { if (!string.IsNullOrWhiteSpace(TxtName.Text)) _vm.Name = TxtName.Text; Focus(); e.Handled = true; }
            else if (e.Key == Key.Escape && TxtName.IsFocused)
            { TxtName.Text = _vm.Name; Focus(); e.Handled = true; }
            else if (e.Key == Key.Z
                     && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control
                     && _vm.SettingsExpanded
                     && _vm.UndoSettingsCommand.CanExecute(null))
            { _vm.UndoSettingsCommand.Execute(null); e.Handled = true; }
        }
    }
}
