using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace TimerStudio
{
    /// <summary>
    /// ViewModel for a single timer card.
    /// All business logic, state transitions, and observable properties live here.
    /// The code-behind (TimerCard.xaml.cs) shrinks to wiring only.
    /// </summary>
    public sealed class TimerCardViewModel : INotifyPropertyChanged
    {
        // ── Model + dependencies ──────────────────────────────────────────────
        private readonly TimerModel      _model;
        private readonly MainWindow      _mainWindow;
        private readonly DispatcherTimer _alertRepeat;
        private int                      _alertRepeatCount;
        private FlashOverlayWindow       _flashOverlay;
        private LensAlertWindow          _lensAlert;
        private TimerModel               _settingsSnapshot;
        // ── F11: Escalating alert ─────────────────────────────────────────────
        private DispatcherTimer          _escalateTimer;

        // ── INotifyPropertyChanged ────────────────────────────────────────────
        public event PropertyChangedEventHandler PropertyChanged;

        // Cache PropertyChangedEventArgs by name — eliminates per-call allocation on the
        // 40 ms tick loop (DisplayTime + ProgressFraction × N running cards).
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, PropertyChangedEventArgs>
            _pceaCache = new();

        private void Notify([CallerMemberName] string name = null)
        {
            if (PropertyChanged == null || name == null) return;
            var args = _pceaCache.GetOrAdd(name, static n => new PropertyChangedEventArgs(n));
            PropertyChanged.Invoke(this, args);
        }
        private void Set<T>(ref T field, T value, [CallerMemberName] string name = null)
        {
            if (Equals(field, value)) return;
            field = value;
            Notify(name);
        }

        // ── Constructor ───────────────────────────────────────────────────────
        public TimerCardViewModel(TimerModel model, MainWindow mainWindow)
        {
            _model      = model;
            _mainWindow = mainWindow;

            _alertRepeat = new DispatcherTimer
                { Interval = TimeSpan.FromMilliseconds(1500) };
            _alertRepeat.Tick += (s, e) =>
            {
                _alertRepeatCount++;
                // F9: 0 = unlimited; > 0 = stop after N additional plays (first play is in FireAlert)
                bool unlimited = _model.AlertRepeatLimit == 0;
                if (unlimited || _alertRepeatCount < _model.AlertRepeatLimit)
                    AlertSoundPlayer.PlayForModel(_model);
                else
                    _alertRepeat.Stop();
            };

            WireCommands();
        }

        // ── Direct model accessor (needed by MainWindow / OverlayWindow) ──────
        public TimerModel Model => _model;

        // ── Observable pass-through properties ───────────────────────────────
        // M-4: cap lengths to prevent unbounded prefs.json growth
        private const int MaxNameLength  = 255;
        private const int MaxNotesLength = 10_000;

        public string Name
        {
            get => _model.Name;
            set
            {
                value = (value ?? "").Length > MaxNameLength ? value[..MaxNameLength] : (value ?? "");
                if (_model.Name == value) return;
                _model.Name = value;
                Notify();
                Notify(nameof(AlertMessage));
            }
        }

        public TimerMode Mode
        {
            get => _model.Mode;
            set { if (_model.Mode == value) return; _model.Mode = value; Notify(); RaiseUiStateChanged(); }
        }

        public TimerState State
        {
            get => _model.State;
            private set { if (_model.State == value) return; _model.State = value; Notify(); RaiseUiStateChanged(); }
        }

        public int Hours
        {
            get => _model.Hours;
            set { _model.Hours = Math.Max(0, Math.Min(99, value)); Notify(); Notify(nameof(DisplayTime)); }
        }

        public int Minutes
        {
            get => _model.Minutes;
            set { _model.Minutes = Math.Max(0, Math.Min(59, value)); Notify(); Notify(nameof(DisplayTime)); }
        }

        public int Seconds
        {
            get => _model.Seconds;
            set { _model.Seconds = Math.Max(0, Math.Min(59, value)); Notify(); Notify(nameof(DisplayTime)); }
        }

        public int FlashSeconds
        {
            get => _model.FlashSeconds;
            set { _model.FlashSeconds = Math.Max(0, value); Notify(); }
        }

        public AlertSound AlertSound
        {
            get => _model.AlertSound;
            set { _model.AlertSound = value; Notify(); }
        }

        public AlertStyle AlertStyle
        {
            get => _model.AlertStyle;
            set { _model.AlertStyle = value; Notify(); }
        }

        public bool AutoReset
        {
            get => _model.AutoReset;
            set { _model.AutoReset = value; Notify(); }
        }

        public int AlertFontSize
        {
            get => _model.AlertFontSize;
            set { _model.AlertFontSize = Math.Max(8, Math.Min(120, value)); Notify(); }
        }

        public LensPosition LensPosition
        {
            get => _model.LensPosition;
            set { _model.LensPosition = value; Notify(); }
        }

        public string Notes
        {
            get => _model.Notes;
            set
            {
                value = (value ?? "").Length > MaxNotesLength ? value[..MaxNotesLength] : (value ?? "");
                _model.Notes = value;
                Notify();
            }
        }

        public string Category
        {
            get => _model.Category;
            set { _model.Category = value ?? ""; Notify(); }
        }

        public bool OverrideCategoryColor
        {
            get => _model.OverrideCategoryColor;
            set { _model.OverrideCategoryColor = value; Notify(); }
        }

        public bool ShowInOverlay
        {
            get => _model.ShowInOverlay;
            set { _model.ShowInOverlay = value; Notify(); }
        }

        public Color AlertColor
        {
            get => _model.AlertColor;
            set { _model.AlertColor = value; Notify(); Notify(nameof(AlertColorBrush)); RaiseUiStateChanged(); }
        }

        public Color AlertTitleColor
        {
            get => _model.AlertTitleColor;
            set { _model.AlertTitleColor = value; Notify(); Notify(nameof(AlertTitleColorBrush)); RaiseUiStateChanged(); }
        }

        public bool ReminderEnabled
        {
            get => _model.ReminderEnabled;
            set { _model.ReminderEnabled = value; Notify(); Notify(nameof(ReminderDetailVisible)); }
        }

        public int ReminderLeadMinutes
        {
            get => _model.ReminderLeadMinutes;
            set { _model.ReminderLeadMinutes = Math.Max(1, Math.Min(4320, value)); Notify(); }
        }

        public int ReminderIntervalMinutes
        {
            get => _model.ReminderIntervalMinutes;
            set { _model.ReminderIntervalMinutes = Math.Max(0, Math.Min(4320, value)); Notify(); }
        }

        public bool SendEmailOnReminder
        {
            get => _model.SendEmailOnReminder;
            set { _model.SendEmailOnReminder = value; Notify(); }
        }

        public string AlertEmailTo
        {
            get => _model.AlertEmailTo;
            set { _model.AlertEmailTo = value ?? ""; Notify(); }
        }

        public DateTime TargetDateTime
        {
            get => _model.TargetDateTime;
            set { _model.TargetDateTime = value; Notify(); Notify(nameof(TargetDateTimeLabel)); Notify(nameof(DisplayTime)); }
        }

        public bool Collapsed
        {
            get => _model.Collapsed;
            set { _model.Collapsed = value; Notify(); Notify(nameof(CollapseButtonContent)); Notify(nameof(BodyVisible)); }
        }

        public bool SettingsExpanded
        {
            get => _model.SettingsExpanded;
            set { _model.SettingsExpanded = value; Notify(); Notify(nameof(SettingsExpandedVisible)); Notify(nameof(SettingsToggleContent)); }
        }

        public bool NotesExpanded
        {
            get => _model.NotesExpanded;
            set { _model.NotesExpanded = value; Notify(); Notify(nameof(NotesExpandedVisible)); Notify(nameof(NotesToggleContent)); }
        }

        public string CustomSoundPath
        {
            get => _model.CustomSoundPath;
            set { _model.CustomSoundPath = value; Notify(); Notify(nameof(CustomSoundLabel)); }
        }

        public string CardTint
        {
            get => _model.CardTint;
            set { _model.CardTint = value; Notify(); Notify(nameof(CardTintBrush)); Notify(nameof(CardBackgroundBrush)); }
        }

        public int SnoozeDurationMinutes
        {
            get => _model.SnoozeDurationMinutes;
            set { _model.SnoozeDurationMinutes = Math.Max(1, Math.Min(60, value)); Notify(); }
        }

        public bool AlertActive
        {
            get => _model.AlertActive;
            private set { _model.AlertActive = value; Notify(); RaiseUiStateChanged(); }
        }

        // ── Feature 1: Recurrence ─────────────────────────────────────────────
        public RecurrenceMode RecurrenceMode
        {
            get => _model.RecurrenceMode;
            set { if (_model.RecurrenceMode == value) return; _model.RecurrenceMode = value; Notify(); Notify(nameof(RecurrenceSectionVis)); }
        }

        public int RecurrenceInterval
        {
            get => _model.RecurrenceInterval;
            set { _model.RecurrenceInterval = Math.Max(1, value); Notify(); }
        }

        // ── Feature 2: Webhook ────────────────────────────────────────────────
        public string WebhookUrl
        {
            get => _model.WebhookUrl;
            set { _model.WebhookUrl = value ?? ""; NotifyWebhook(); }
        }

        /// <summary>TS-05: inline validation message; null when the URL is allowed or blank.</summary>
        public string WebhookUrlError        => WebhookService.ValidateUrl(_model.WebhookUrl);
        public string WebhookUrlErrorVisible => WebhookUrlError == null ? "Collapsed" : "Visible";

        private void NotifyWebhook()
        {
            Notify(nameof(WebhookUrl));
            Notify(nameof(WebhookUrlError));
            Notify(nameof(WebhookUrlErrorVisible));
        }

        // ── Feature 3: Volume ─────────────────────────────────────────────────
        public double AlertVolume
        {
            get => _model.AlertVolume;
            set { _model.AlertVolume = Math.Max(0.0, Math.Min(1.0, value)); Notify(); }
        }

        // ── F9: Alert repeat limit ────────────────────────────────────────────
        public int AlertRepeatLimit
        {
            get => _model.AlertRepeatLimit;
            set { _model.AlertRepeatLimit = Math.Max(0, Math.Min(100, value)); Notify(); }
        }

        // ── F11: Escalating alert ─────────────────────────────────────────────
        public int EscalateAfterSeconds
        {
            get => _model.EscalateAfterSeconds;
            set { _model.EscalateAfterSeconds = Math.Max(0, Math.Min(300, value)); Notify(); }
        }

        // ── Pill overlay visibility ───────────────────────────────────────────
        public bool ExcludeFromPill
        {
            get => _model.ExcludeFromPill;
            set { if (_model.ExcludeFromPill == value) return; _model.ExcludeFromPill = value; Notify(); }
        }

        // ── F14: Interval timer settings ──────────────────────────────────────
        public int IntervalWorkMinutes
        {
            get => _model.IntervalWorkMinutes;
            set { _model.IntervalWorkMinutes = Math.Max(1, Math.Min(480, value)); Notify(); Notify(nameof(DisplayTime)); }
        }

        public int IntervalRestMinutes
        {
            get => _model.IntervalRestMinutes;
            set { _model.IntervalRestMinutes = Math.Max(1, Math.Min(120, value)); Notify(); }
        }

        public int IntervalRepeatCount
        {
            get => _model.IntervalRepeatCount;
            set { _model.IntervalRepeatCount = Math.Max(0, Math.Min(100, value)); Notify(); }
        }

        // ── Feature 8: Pomodoro settings ──────────────────────────────────────
        public int PomodoroWorkMinutes
        {
            get => _model.PomodoroWorkMinutes;
            set { _model.PomodoroWorkMinutes = Math.Max(1, Math.Min(120, value)); Notify(); Notify(nameof(DisplayTime)); }
        }

        public int PomodoroBreakMinutes
        {
            get => _model.PomodoroBreakMinutes;
            set { _model.PomodoroBreakMinutes = Math.Max(1, Math.Min(60, value)); Notify(); }
        }

        public int PomodoroLongBreakMinutes
        {
            get => _model.PomodoroLongBreakMinutes;
            set { _model.PomodoroLongBreakMinutes = Math.Max(1, Math.Min(120, value)); Notify(); }
        }

        public int PomodoroLongBreakAfter
        {
            get => _model.PomodoroLongBreakAfter;
            set { _model.PomodoroLongBreakAfter = Math.Max(1, Math.Min(10, value)); Notify(); }
        }

        // ── Derived display properties ────────────────────────────────────────
        public string DisplayTime => _model.DisplayTime;

        public string AlertMessage => Name + " — time's up!";

        public string ActionButtonContent
        {
            get
            {
                if (_model.State == TimerState.Idle)    return "Start";
                if (_model.State == TimerState.Running) return "Pause";
                if (_model.State == TimerState.Paused)  return "Resume";
                return "Reset";
            }
        }

        /// <summary>Fixed-color icon key paired with ActionButtonContent (Start/Resume = play, Pause, Reset = undo).</summary>
        public string ActionIconKey
        {
            get
            {
                if (_model.State == TimerState.Idle)    return "queueNow";
                if (_model.State == TimerState.Running) return "pause";
                if (_model.State == TimerState.Paused)  return "queueNow";
                return "undo";
            }
        }

        // ── Timer state chip (icon + word; the word carries the meaning, never colour alone) ──
        private bool IsOverdue => _model.AlertActive && _model.AlertFiredAt != DateTime.MinValue
                                  && DateTime.UtcNow - _model.AlertFiredAt >= TimeSpan.FromMinutes(1);
        public string StateLabel
        {
            get
            {
                if (_model.AlertActive) return IsOverdue ? "Overdue" : "Time’s up";
                return _model.State switch
                {
                    TimerState.Running => "Running",
                    TimerState.Paused  => "Paused",
                    TimerState.Done    => "Done",
                    _                  => "",
                };
            }
        }
        public string StateIconKey
        {
            get
            {
                if (_model.AlertActive) return IsOverdue ? "warning" : "alarm";
                return _model.State switch
                {
                    TimerState.Running => "stopwatch",
                    TimerState.Paused  => "pause",
                    TimerState.Done    => "complete",
                    _                  => "",
                };
            }
        }
        public Visibility StateChipVis => (_model.State == TimerState.Idle && !_model.AlertActive)
            ? Visibility.Collapsed : Visibility.Visible;

        public string TargetDateTimeLabel
            => "Target: " + _model.TargetDateTime.ToString("ddd, MMM d yyyy  h:mm:ss tt");

        public string CollapseButtonContent  => _model.Collapsed ? "▸" : "▾";
        public string BodyVisible             => _model.Collapsed ? "Collapsed" : "Visible";
        public string SettingsExpandedVisible => _model.SettingsExpanded ? "Visible" : "Collapsed";
        public string SettingsToggleContent   => _model.SettingsExpanded ? "Alert settings  ▾" : "Alert settings  ▸";
        public string NotesExpandedVisible    => _model.NotesExpanded ? "Visible" : "Collapsed";
        public string NotesToggleContent      => _model.NotesExpanded ? "Notes  ▾" : "Notes  ▸";

        public string CustomSoundLabel => string.IsNullOrEmpty(_model.CustomSoundPath)
            ? "None" : Path.GetFileName(_model.CustomSoundPath);

        // ── Feature 1: Recurrence label ───────────────────────────────────────
        public string RecurrenceLabel
        {
            get
            {
                if (_model.RecurrenceMode == RecurrenceMode.None) return "";
                int n = _model.RecurrenceInterval;
                return _model.RecurrenceMode switch
                {
                    RecurrenceMode.Daily   => n == 1 ? "Repeats daily"   : $"Repeats every {n} days",
                    RecurrenceMode.Weekly  => n == 1 ? "Repeats weekly"  : $"Repeats every {n} weeks",
                    RecurrenceMode.Monthly => n == 1 ? "Repeats monthly" : $"Repeats every {n} months",
                    _                      => ""
                };
            }
        }

        // ── Feature 5: Last fired label ───────────────────────────────────────
        public string LastFiredLabel => _model.LastFiredAt == DateTime.MinValue
            ? "" : "Last: " + _model.LastFiredAt.ToLocalTime().ToString("MMM d h:mm tt");

        // ── Feature 8: Pomodoro labels ────────────────────────────────────────
        public string PomodoroPhaseLabel
        {
            get
            {
                if (_model.Mode != TimerMode.Pomodoro || _model.State == TimerState.Idle)
                    return "";
                string phase = _model.PomodoroPhase switch
                {
                    PomodoroPhase.Work      => "🍅 Work",
                    PomodoroPhase.Break     => "☕ Break",
                    PomodoroPhase.LongBreak => "🌙 Long Break",
                    _                       => ""
                };
                return $"{phase} · Session {_model.PomodoroSessionCount + 1}/{_model.PomodoroLongBreakAfter}";
            }
        }

        // ── F14: Interval phase label ─────────────────────────────────────────
        public string IntervalPhaseLabel
        {
            get
            {
                if (_model.Mode != TimerMode.Interval || _model.State == TimerState.Idle) return "";
                string phase = _model.IntervalPhase == IntervalPhase.Work ? "💪 Work" : "😌 Rest";
                int total = _model.IntervalRepeatCount;
                // IntervalCycleCount is completed work cycles; show current cycle (1-based)
                int current = _model.IntervalPhase == IntervalPhase.Work
                    ? _model.IntervalCycleCount + 1
                    : _model.IntervalCycleCount + 1; // during rest, same cycle number
                string cycleStr = total > 0
                    ? $" · Cycle {current}/{total}"
                    : $" · Cycle {current}";
                return phase + cycleStr;
            }
        }

        // ── F16: Lap times ────────────────────────────────────────────────────
        public System.Collections.Generic.List<TimeSpan> LapTimes => _model.LapTimes;

        public string LapButtonLabel => "Lap " + (_model.LapTimes.Count + 1);

        // ── Brush helpers (used by XAML bindings) ─────────────────────────────
        public SolidColorBrush AlertColorBrush      => new SolidColorBrush(_model.AlertColor);
        public SolidColorBrush AlertTitleColorBrush => new SolidColorBrush(_model.AlertTitleColor);

        public Brush CardBackgroundBrush
        {
            get
            {
                if (_model.AlertActive) return new SolidColorBrush(_model.AlertColor);
                if (!string.IsNullOrEmpty(_model.CardTint))
                {
                    try
                    {
                        var col = (Color)ColorConverter.ConvertFromString(_model.CardTint);
                        return new SolidColorBrush(Color.FromArgb(60, col.R, col.G, col.B));
                    }
                    catch { }
                }
                return null; // null = use DynamicResource BrushSurf from XAML
            }
        }

        public Brush CardTintBrush
        {
            get
            {
                if (string.IsNullOrEmpty(_model.CardTint)) return null;
                try { return new SolidColorBrush((Color)ColorConverter.ConvertFromString(_model.CardTint)); }
                catch { return null; }
            }
        }

        // ── Visibility helpers (Visibility enum, bindable) ────────────────────
        public Visibility ModeComboVisible    => _model.State == TimerState.Idle ? Visibility.Visible : Visibility.Collapsed;
        public Visibility CountdownInputsVis  => (_model.State == TimerState.Idle && _model.Mode == TimerMode.Countdown) ? Visibility.Visible : Visibility.Collapsed;
        public Visibility DateTimeInputsVis   => (_model.State == TimerState.Idle && _model.Mode == TimerMode.DateTime)  ? Visibility.Visible : Visibility.Collapsed;
        public Visibility ProgressBarVis      => ((_model.Mode == TimerMode.Countdown || _model.Mode == TimerMode.DateTime || _model.Mode == TimerMode.Pomodoro || _model.Mode == TimerMode.Interval) && _model.State != TimerState.Idle) ? Visibility.Visible : Visibility.Collapsed;
        public Visibility AlertButtonsVis     => _model.AlertActive ? Visibility.Visible : Visibility.Collapsed;
        public Visibility ActionButtonVis     => _model.AlertActive ? Visibility.Collapsed : Visibility.Visible;
        public Visibility ResetButtonVis      => (!_model.AlertActive && (_model.State == TimerState.Running || _model.State == TimerState.Paused)) ? Visibility.Visible : Visibility.Collapsed;
        public Visibility SettingsPanelVis    => (_model.State == TimerState.Idle && _model.Mode != TimerMode.Stopwatch) ? Visibility.Visible : Visibility.Collapsed;
        public Visibility AutoResetRowVis     => (_model.Mode != TimerMode.Stopwatch && _model.Mode != TimerMode.DateTime && _model.Mode != TimerMode.Pomodoro) ? Visibility.Visible : Visibility.Collapsed;
        public Visibility ReminderSectionVis  => (_model.State == TimerState.Idle && _model.Mode == TimerMode.DateTime) ? Visibility.Visible : Visibility.Collapsed;
        public Visibility ReminderDetailVisible => (_model.ReminderEnabled && _model.State == TimerState.Idle && _model.Mode == TimerMode.DateTime) ? Visibility.Visible : Visibility.Collapsed;
        public Visibility CategoryOverrideVis => (_model.Mode == TimerMode.DateTime && _model.State == TimerState.Idle) ? Visibility.Visible : Visibility.Collapsed;
        public Visibility TimeSinceVis        => (_model.State == TimerState.Done || _model.AlertActive) ? Visibility.Visible : Visibility.Collapsed;
        public Visibility AlertMsgVis         => _model.AlertActive ? Visibility.Visible : Visibility.Collapsed;
        public Visibility DateTimeTargetVis   => (_model.Mode == TimerMode.DateTime && !_model.AlertActive) ? Visibility.Visible : Visibility.Collapsed;

        // ── Feature 1: Recurrence section (idle DateTime with recurrence set) ─
        public Visibility RecurrenceSectionVis => (_model.State == TimerState.Idle
            && _model.Mode == TimerMode.DateTime)
            ? Visibility.Visible : Visibility.Collapsed;

        // ── Feature 8: Pomodoro sections ──────────────────────────────────────
        public Visibility PomodoroSectionVis  => (_model.State == TimerState.Idle
            && _model.Mode == TimerMode.Pomodoro)
            ? Visibility.Visible : Visibility.Collapsed;
        public Visibility PomodoroPhaseVis    => (_model.Mode == TimerMode.Pomodoro
            && _model.State != TimerState.Idle)
            ? Visibility.Visible : Visibility.Collapsed;

        // ── F14: Interval sections ────────────────────────────────────────────
        public Visibility IntervalSectionVis  => (_model.State == TimerState.Idle
            && _model.Mode == TimerMode.Interval)
            ? Visibility.Visible : Visibility.Collapsed;
        public Visibility IntervalPhaseVis    => (_model.Mode == TimerMode.Interval
            && _model.State != TimerState.Idle)
            ? Visibility.Visible : Visibility.Collapsed;

        // ── F16: Lap button ────────────────────────────────────────────────────
        public Visibility LapButtonVis        => (_model.Mode == TimerMode.Stopwatch
            && (_model.State == TimerState.Running || _model.State == TimerState.Paused))
            ? Visibility.Visible : Visibility.Collapsed;
        public Visibility LapTableVis         => (_model.Mode == TimerMode.Stopwatch
            && _model.LapTimes.Count > 0)
            ? Visibility.Visible : Visibility.Collapsed;

        // ── Feature 5: Last-fired row ─────────────────────────────────────────
        public Visibility LastFiredVis        => _model.LastFiredAt != DateTime.MinValue
            ? Visibility.Visible : Visibility.Collapsed;

        // Progress
        public double ProgressFraction => _model.Progress;

        // ── Bulk UI-state notification (replaces RefreshUI) ───────────────────
        /// <summary>
        /// Public shim so code-behind can force a full UI refresh (e.g., after
        /// initialising CboMode.SelectedIndex when Mode already matches the model).
        /// </summary>
        public void RefreshUiState() => RaiseUiStateChanged();

        private void RaiseUiStateChanged()
        {
            Notify(nameof(ActionButtonContent));
            Notify(nameof(ActionIconKey));
            Notify(nameof(StateLabel));
            Notify(nameof(StateIconKey));
            Notify(nameof(StateChipVis));
            Notify(nameof(ModeComboVisible));
            Notify(nameof(CountdownInputsVis));
            Notify(nameof(DateTimeInputsVis));
            Notify(nameof(ProgressBarVis));
            Notify(nameof(AlertButtonsVis));
            Notify(nameof(ActionButtonVis));
            Notify(nameof(ResetButtonVis));
            Notify(nameof(SettingsPanelVis));
            Notify(nameof(AutoResetRowVis));
            Notify(nameof(ReminderSectionVis));
            Notify(nameof(ReminderDetailVisible));
            Notify(nameof(CategoryOverrideVis));
            Notify(nameof(TimeSinceVis));
            Notify(nameof(AlertMsgVis));
            Notify(nameof(AlertMessage));
            Notify(nameof(DateTimeTargetVis));
            Notify(nameof(DisplayTime));
            Notify(nameof(CardBackgroundBrush));
            Notify(nameof(AlertColorBrush));
            Notify(nameof(AlertTitleColorBrush));
            Notify(nameof(ProgressFraction));
            // New feature visibility
            Notify(nameof(RecurrenceSectionVis));
            Notify(nameof(PomodoroSectionVis));
            Notify(nameof(PomodoroPhaseVis));
            Notify(nameof(PomodoroPhaseLabel));
            Notify(nameof(LastFiredVis));
            Notify(nameof(LastFiredLabel));
            // F14: Interval
            Notify(nameof(IntervalSectionVis));
            Notify(nameof(IntervalPhaseVis));
            Notify(nameof(IntervalPhaseLabel));
            // F16: Lap
            Notify(nameof(LapButtonVis));
            Notify(nameof(LapTableVis));
            Notify(nameof(LapButtonLabel));
            Notify(nameof(LapTimes));
            CommandManager.InvalidateRequerySuggested();
        }

        // ── Commands ──────────────────────────────────────────────────────────
        public ICommand ActionCommand   { get; private set; }
        public ICommand ResetCommand    { get; private set; }
        public ICommand DismissCommand  { get; private set; }
        public ICommand SnoozeCommand   { get; private set; }
        public ICommand DeleteCommand   { get; private set; }
        public ICommand DuplicateCommand { get; private set; }
        public ICommand CollapseCommand  { get; private set; }
        public ICommand ToggleSettingsCommand { get; private set; }
        public ICommand ToggleNotesCommand    { get; private set; }
        public ICommand ShareCommand     { get; private set; }
        public ICommand SaveTemplateCommand { get; private set; }
        public ICommand ToggleOverlayPinCommand { get; private set; }
        public ICommand UndoSettingsCommand { get; private set; }
        public ICommand PreviewSoundCommand { get; private set; }
        // ── F16: Lap ──────────────────────────────────────────────────────────
        public ICommand LapCommand { get; private set; }

        private void WireCommands()
        {
            ActionCommand  = new RelayCommand(_ => ExecuteAction());
            ResetCommand   = new RelayCommand(_ => DoReset());
            DismissCommand = new RelayCommand(_ => DoDismiss());
            SnoozeCommand  = new RelayCommand(_ => DoSnooze());
            DeleteCommand  = new RelayCommand(_ => RequestDelete());
            DuplicateCommand = new RelayCommand(_ => _mainWindow?.DuplicateTimer(_model));
            CollapseCommand  = new RelayCommand(_ => { Collapsed = !Collapsed; });
            ToggleSettingsCommand = new RelayCommand(_ =>
            {
                if (!SettingsExpanded) SnapshotSettings();
                SettingsExpanded = !SettingsExpanded;
            });
            ToggleNotesCommand = new RelayCommand(_ => { NotesExpanded = !NotesExpanded; });
            ShareCommand       = new RelayCommand(_ => _mainWindow?.ShareTimer(_model));
            SaveTemplateCommand = new RelayCommand(_ => _mainWindow?.SaveAsTemplate(_model));
            ToggleOverlayPinCommand = new RelayCommand(_ =>
            {
                _mainWindow?.ToggleOverlayPin(_model);
                Notify(nameof(OverlayPinContent));
            });
            UndoSettingsCommand = new RelayCommand(
                _ => RestoreSettingsSnapshot(),
                _ => _settingsSnapshot != null && SettingsExpanded);
            PreviewSoundCommand = new RelayCommand(_ => AlertSoundPlayer.PlayForModel(_model));
            LapCommand = new RelayCommand(_ => RecordLap(),
                _ => _model.Mode == TimerMode.Stopwatch &&
                     (_model.State == TimerState.Running || _model.State == TimerState.Paused));
        }

        // ── Overlay pin label ─────────────────────────────────────────────────
        public string OverlayPinContent => _model.ShowInOverlay ? "📌" : "📍";

        // ── Tick diagnostics (per-timer sampling, logged to DiagLog) ─────────
        // Tracks worst-case tick duration. Resets every 1000 ticks so the log
        // stays useful across long sessions. Active in DEBUG builds only.
#if DEBUG
        private int    _tickCount;
        private long   _tickMaxTicks;   // Stopwatch ticks (not timer ticks)
#endif

        // ── Global tick (called by MainWindow every 40ms) ─────────────────────
        public void Tick()
        {
#if DEBUG
            var sw = System.Diagnostics.Stopwatch.StartNew();
#endif
            if (_model.State == TimerState.Done && _model.AlertFiredAt != DateTime.MinValue)
            {
                Notify(nameof(TimeSinceText));
                Notify(nameof(StateLabel));     // Time's up → Overdue after 1 minute
                Notify(nameof(StateIconKey));
            }

            if (_model.State != TimerState.Running) return;

            if (_model.Mode == TimerMode.DateTime)
            {
                _model.Remaining = _model.TargetDateTime - DateTime.Now;
                if (_model.Remaining <= TimeSpan.Zero)
                {
                    _model.Remaining    = TimeSpan.Zero;
                    _model.State        = TimerState.Done;
                    _model.AlertActive  = true;
                    _model.AlertFiredAt = DateTime.UtcNow;
                    FireAlert();
                    RaiseUiStateChanged();
                    return;
                }
                if (_model.ReminderEnabled
                    && _model.NextReminderAt > DateTime.MinValue
                    && DateTime.Now >= _model.NextReminderAt)
                    FireReminder();

                Notify(nameof(DisplayTime));
                Notify(nameof(ProgressFraction));
                return;
            }

            TimeSpan spent = DateTime.UtcNow - _model.StartedAt;
            if (_model.Mode == TimerMode.Interval)
            {
                _model.Remaining = _model.TotalDuration - spent;
                if (_model.Remaining <= TimeSpan.Zero)
                {
                    _model.Remaining = TimeSpan.Zero;
                    AdvanceIntervalPhase();
                    RaiseUiStateChanged();
                    return;
                }
            }
            else if (_model.Mode == TimerMode.Countdown || _model.Mode == TimerMode.Pomodoro)
            {
                _model.Remaining = _model.TotalDuration - spent;
                if (_model.Remaining <= TimeSpan.Zero)
                {
                    _model.Remaining    = TimeSpan.Zero;
                    _model.State        = TimerState.Done;
                    _model.AlertActive  = true;
                    _model.AlertFiredAt = DateTime.UtcNow;
                    FireAlert();
                    RaiseUiStateChanged();
                    return;
                }
            }
            else
            {
                _model.Elapsed = spent;
            }

            Notify(nameof(DisplayTime));
            Notify(nameof(ProgressFraction));

#if DEBUG
            sw.Stop();
            if (sw.ElapsedTicks > _tickMaxTicks) _tickMaxTicks = sw.ElapsedTicks;
            if (++_tickCount >= 1000)
            {
                double maxMs = _tickMaxTicks * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
                if (maxMs > 5.0)
                    DiagLog.Write($"[Tick perf] '{_model.Name}' worst tick in last 1000: {maxMs:F2} ms");
                _tickCount = 0;
                _tickMaxTicks = 0;
            }
#endif
        }

        public string TimeSinceText
        {
            get
            {
                if (_model.AlertFiredAt == DateTime.MinValue) return "";
                var since = DateTime.UtcNow - _model.AlertFiredAt;
                return "+" + FormatTimeSince(since) + " since alert";
            }
        }

        // ── Font helpers (called by code-behind for scale changes) ────────────
        public double ComputeTimeFontSize()
        {
            int len = _model.DisplayTime.Length;
            double scale = _model.Mode == TimerMode.DateTime
                ? AppPrefs.Instance.EventTimeFontScale
                : AppPrefs.Instance.TimeFontScale;
            return (len <= 8 ? 42 : len <= 11 ? 36 : 30) * scale;
        }

        public double ComputeTitleFontSize()
        {
            double scale = _model.Mode == TimerMode.DateTime
                ? AppPrefs.Instance.EventTitleFontScale
                : AppPrefs.Instance.TitleFontScale;
            return 13.0 * scale;
        }

        // ── State transitions ─────────────────────────────────────────────────
        private void ExecuteAction()
        {
            switch (_model.State)
            {
                case TimerState.Idle:    DoStart();  break;
                case TimerState.Running: DoPause();  break;
                case TimerState.Paused:  DoResume(); break;
                case TimerState.Done:    DoReset();  break;
            }
        }

        /// <summary>Public entry point for "Start All in category" — starts only if Idle.</summary>
        public void RequestStart() { if (_model.State == TimerState.Idle) DoStart(); }
        /// <summary>Public entry point for "Pause All in category" — pauses only if Running.</summary>
        public void RequestPause() { if (_model.State == TimerState.Running) DoPause(); }

        private void DoStart()
        {
            if (_model.Mode == TimerMode.Countdown)
            {
                var dur = _model.GetConfiguredDuration();
                if (dur == TimeSpan.Zero) return;
                _model.TotalDuration = dur;
                _model.Remaining     = dur;
            }
            else if (_model.Mode == TimerMode.DateTime)
            {
                var remaining = _model.TargetDateTime - DateTime.Now;
                if (remaining <= TimeSpan.Zero)
                {
                    AppDialog.Show(
                        "The target date and time is in the past.\n\nPlease choose a future date and time.",
                        "Invalid Target", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                _model.TotalDuration = remaining;
                _model.Remaining     = remaining;
            }
            else if (_model.Mode == TimerMode.Pomodoro)
            {
                // Reset to first work session
                _model.PomodoroPhase        = PomodoroPhase.Work;
                _model.PomodoroSessionCount = 0;
                var dur = TimeSpan.FromMinutes(_model.PomodoroWorkMinutes);
                _model.TotalDuration = dur;
                _model.Remaining     = dur;
            }
            else if (_model.Mode == TimerMode.Interval)
            {
                // F14: Reset to first work phase
                _model.IntervalPhase      = IntervalPhase.Work;
                _model.IntervalCycleCount = 0;
                var dur = TimeSpan.FromMinutes(_model.IntervalWorkMinutes);
                _model.TotalDuration = dur;
                _model.Remaining     = dur;
            }

            if (_model.Mode == TimerMode.DateTime
                && !string.IsNullOrEmpty(_model.Category)
                && !_model.OverrideCategoryColor)
            {
                var catColor = MainWindow.CategoryColor(_model.Category);
                _model.AlertColor = Color.FromRgb(catColor.R, catColor.G, catColor.B);
                Notify(nameof(AlertColor));
                Notify(nameof(AlertColorBrush));
            }

            _model.Elapsed             = TimeSpan.Zero;
            _model.StartedAt           = DateTime.UtcNow;
            _model.State               = TimerState.Running;
            _model.AlertActive         = false;
            _model.AlertDismissed      = false;
            _model.ReminderPhaseActive = false;

            if (_model.Mode == TimerMode.DateTime && _model.ReminderEnabled
                && _model.ReminderLeadMinutes > 0)
            {
                var first = _model.TargetDateTime.AddMinutes(-_model.ReminderLeadMinutes);
                if (first > DateTime.Now)
                {
                    _model.NextReminderAt = first;
                }
                else if (_model.ReminderIntervalMinutes > 0)
                {
                    // Already inside the lead window — skip to first future interval slot
                    var next = first;
                    while (next <= DateTime.Now)
                        next = next.AddMinutes(_model.ReminderIntervalMinutes);
                    _model.NextReminderAt = next < _model.TargetDateTime ? next : DateTime.MinValue;
                }
                else
                {
                    _model.NextReminderAt = DateTime.MinValue;
                }
            }
            else
                _model.NextReminderAt = DateTime.MinValue;

            RaiseUiStateChanged();
        }

        private void DoPause()
        {
            _model.PausedRemaining = _model.Remaining;
            _model.PausedElapsed   = _model.Elapsed;
            _model.State           = TimerState.Paused;
            RaiseUiStateChanged();
        }

        private void DoResume()
        {
            if (_model.Mode == TimerMode.DateTime)
                _model.State = TimerState.Running;
            else if (_model.Mode == TimerMode.Countdown)
            {
                _model.StartedAt = DateTime.UtcNow - (_model.TotalDuration - _model.PausedRemaining);
                _model.State = TimerState.Running;
            }
            else if (_model.Mode == TimerMode.Pomodoro)
            {
                // H-5: Pomodoro uses PausedRemaining (remaining phase time), not PausedElapsed.
                // Without an explicit branch it fell through to Stopwatch logic where
                // PausedElapsed=0, restarting the phase from scratch.
                _model.StartedAt = DateTime.UtcNow - (_model.TotalDuration - _model.PausedRemaining);
                _model.State = TimerState.Running;
            }
            else if (_model.Mode == TimerMode.Interval)
            {
                // F14: Interval resume — preserve remaining phase time
                _model.StartedAt = DateTime.UtcNow - (_model.TotalDuration - _model.PausedRemaining);
                _model.State = TimerState.Running;
            }
            else
            {
                // Stopwatch
                _model.StartedAt = DateTime.UtcNow - _model.PausedElapsed;
                _model.State = TimerState.Running;
            }
            RaiseUiStateChanged();
        }

        private void DoReset()
        {
            _alertRepeat.Stop();
            _escalateTimer?.Stop(); _escalateTimer = null;
            CloseAlertWindows();
            _model.State          = TimerState.Idle;
            _model.Remaining      = TimeSpan.Zero;
            _model.Elapsed        = TimeSpan.Zero;
            _model.AlertActive    = false;
            _model.AlertDismissed = false;
            _model.LapTimes.Clear();  // F16: clear laps on reset
            RaiseUiStateChanged();
        }

        private void DoDismiss()
        {
            // Set both flags FIRST — before CloseAlertWindows() fires Dismissed events
            // recursively (AlertStyle.Both). If either flag was already in final state
            // the guard below bails out, preventing the double-advance bug (C-2).
            if (!_model.AlertActive && _model.AlertDismissed) return;
            _model.AlertActive    = false;
            _model.AlertDismissed = true;
            _alertRepeat.Stop();
            CloseAlertWindows();

            if (_model.AutoReset && _model.Mode == TimerMode.Countdown)
            { DoStart(); return; }
            if (_model.AutoReset && _model.Mode == TimerMode.DateTime)
            {
                _model.TargetDateTime = _model.TargetDateTime.AddDays(1);
                DoStart(); return;
            }
            // Feature 8: Pomodoro auto-advances to next phase on dismiss
            if (_model.Mode == TimerMode.Pomodoro)
            { AdvancePomodoroPhase(); return; }
            // Feature 1: Recurrence — advance DateTime target and restart
            if (_model.Mode == TimerMode.DateTime
                && _model.RecurrenceMode != RecurrenceMode.None)
            { AdvanceRecurrence(); DoStart(); return; }
            RaiseUiStateChanged();
        }

        private void DoSnooze()
        {
            _alertRepeat.Stop();
            CloseAlertWindows();
            _model.AlertActive    = false;
            _model.AlertDismissed = false;

            int snoozeMin = Math.Max(1, _model.SnoozeDurationMinutes);
            if (_model.Mode == TimerMode.Pomodoro)
            {
                // M-8: Pomodoro snooze — pause the current phase, snooze as countdown,
                // then restore Pomodoro mode and phase state afterwards.
                // Previously snooze converted Pomodoro → Countdown permanently,
                // losing PomodoroPhase and PomodoroSessionCount.
                var savedPhase   = _model.PomodoroPhase;
                var savedCount   = _model.PomodoroSessionCount;
                var savedRemain  = _model.PausedRemaining;

                _model.Mode      = TimerMode.Countdown;
                _model.Hours     = 0;
                _model.Minutes   = snoozeMin;
                _model.Seconds   = 0;
                _model.AutoReset = false;
                DoStart();

                // Restore Pomodoro identity so next dismiss re-enters Pomodoro logic
                _model.Mode                 = TimerMode.Pomodoro;
                _model.PomodoroPhase        = savedPhase;
                _model.PomodoroSessionCount = savedCount;
                _model.PausedRemaining      = savedRemain;
                // H-11: notify Mode so CboMode and phase-visibility bindings update correctly
                Notify(nameof(Mode));
            }
            else if (_model.Mode == TimerMode.Countdown || _model.Mode == TimerMode.Stopwatch)
            {
                _model.Mode      = TimerMode.Countdown;
                _model.Hours     = 0;
                _model.Minutes   = snoozeMin;
                _model.Seconds   = 0;
                _model.AutoReset = false;
                DoStart();
            }
            else if (_model.Mode == TimerMode.DateTime)
            {
                _model.TargetDateTime = DateTime.Now.AddMinutes(snoozeMin);
                DoStart();
            }
            _mainWindow?.SaveTimers();
        }

        // ── Feature 8: Pomodoro phase advance ────────────────────────────────
        private void AdvancePomodoroPhase()
        {
            if (_model.PomodoroPhase == PomodoroPhase.Work)
            {
                _model.PomodoroSessionCount++;
                bool longBreak = _model.PomodoroSessionCount >= _model.PomodoroLongBreakAfter;
                _model.PomodoroPhase = longBreak ? PomodoroPhase.LongBreak : PomodoroPhase.Break;
                if (longBreak) _model.PomodoroSessionCount = 0;
                int mins = longBreak ? _model.PomodoroLongBreakMinutes : _model.PomodoroBreakMinutes;
                var dur = TimeSpan.FromMinutes(mins);
                _model.TotalDuration = dur;
                _model.Remaining     = dur;
            }
            else
            {
                // Break → back to Work
                _model.PomodoroPhase = PomodoroPhase.Work;
                var dur = TimeSpan.FromMinutes(_model.PomodoroWorkMinutes);
                _model.TotalDuration = dur;
                _model.Remaining     = dur;
            }
            _model.AlertActive    = false;
            _model.AlertDismissed = false;
            _model.StartedAt      = DateTime.UtcNow;
            _model.State          = TimerState.Running;
            RaiseUiStateChanged();
        }

        // ── Feature 1: Recurrence advance ────────────────────────────────────
        private void AdvanceRecurrence()
        {
            int n = Math.Max(1, _model.RecurrenceInterval);
            _model.TargetDateTime = _model.RecurrenceMode switch
            {
                RecurrenceMode.Daily   => _model.TargetDateTime.AddDays(n),
                RecurrenceMode.Weekly  => _model.TargetDateTime.AddDays(n * 7),
                RecurrenceMode.Monthly => _model.TargetDateTime.AddMonths(n),
                _                      => _model.TargetDateTime.AddDays(1)
            };
            Notify(nameof(TargetDateTime));
            Notify(nameof(TargetDateTimeLabel));
            Notify(nameof(RecurrenceLabel));
        }

        public void RequestDelete()
        {
            bool needsConfirm = _model.State == TimerState.Running
                             || _model.State == TimerState.Paused
                             || _model.State == TimerState.Done;
            if (needsConfirm)
            {
                var r = AppDialog.Show(
                    "Delete timer '" + _model.Name + "'?\n\nThis timer is currently active.",
                    "Delete Timer", MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (r != MessageBoxResult.Yes) return;
            }
            DeleteRequested?.Invoke(this, EventArgs.Empty);
        }

        public event EventHandler DeleteRequested;

        /// <summary>
        /// Stops all running timers and nulls escalate state.
        /// Must be called before removing a TimerCard from the visual tree so the
        /// WPF Dispatcher releases its reference to _alertRepeat, allowing the VM
        /// and card to be garbage-collected.
        /// </summary>
        public void Cleanup()
        {
            _alertRepeat.Stop();
            _escalateTimer?.Stop();
            _escalateTimer = null;
        }

        public void RestartTimer() { DoReset(); DoStart(); }

        public void AutoStartIfDateTime()
        {
            if (_model.Mode == TimerMode.DateTime
                && _model.TargetDateTime > DateTime.Now
                && _model.State == TimerState.Idle)
                DoStart();
        }

        public void FireMissedAlert() => FireAlert();

        // ── Settings undo ─────────────────────────────────────────────────────
        private void SnapshotSettings()
        {
            _settingsSnapshot = new TimerModel();
            _settingsSnapshot.AlertColor            = _model.AlertColor;
            _settingsSnapshot.AlertTitleColor       = _model.AlertTitleColor;
            _settingsSnapshot.AlertSound            = _model.AlertSound;
            _settingsSnapshot.AlertStyle            = _model.AlertStyle;
            _settingsSnapshot.FlashSeconds          = _model.FlashSeconds;
            _settingsSnapshot.AutoReset             = _model.AutoReset;
            _settingsSnapshot.AlertFontSize         = _model.AlertFontSize;
            _settingsSnapshot.LensPosition          = _model.LensPosition;
            _settingsSnapshot.CustomSoundPath       = _model.CustomSoundPath;
            _settingsSnapshot.CardTint              = _model.CardTint;
            _settingsSnapshot.SnoozeDurationMinutes = _model.SnoozeDurationMinutes;
            _settingsSnapshot.ReminderEnabled         = _model.ReminderEnabled;
            _settingsSnapshot.ReminderLeadMinutes     = _model.ReminderLeadMinutes;
            _settingsSnapshot.ReminderIntervalMinutes = _model.ReminderIntervalMinutes;
            _settingsSnapshot.SendEmailOnReminder     = _model.SendEmailOnReminder;
            _settingsSnapshot.AlertEmailTo            = _model.AlertEmailTo;
            _settingsSnapshot.WebhookUrl              = _model.WebhookUrl;
            _settingsSnapshot.AlertVolume             = _model.AlertVolume;
            _settingsSnapshot.RecurrenceMode          = _model.RecurrenceMode;
            _settingsSnapshot.RecurrenceInterval      = _model.RecurrenceInterval;
            _settingsSnapshot.PomodoroWorkMinutes      = _model.PomodoroWorkMinutes;
            _settingsSnapshot.PomodoroBreakMinutes     = _model.PomodoroBreakMinutes;
            _settingsSnapshot.PomodoroLongBreakMinutes = _model.PomodoroLongBreakMinutes;
            _settingsSnapshot.PomodoroLongBreakAfter   = _model.PomodoroLongBreakAfter;
            CommandManager.InvalidateRequerySuggested();
        }

        private void RestoreSettingsSnapshot()
        {
            if (_settingsSnapshot == null) return;
            _model.AlertColor            = _settingsSnapshot.AlertColor;
            _model.AlertTitleColor       = _settingsSnapshot.AlertTitleColor;
            _model.AlertSound            = _settingsSnapshot.AlertSound;
            _model.AlertStyle            = _settingsSnapshot.AlertStyle;
            _model.FlashSeconds          = _settingsSnapshot.FlashSeconds;
            _model.AutoReset             = _settingsSnapshot.AutoReset;
            _model.AlertFontSize         = _settingsSnapshot.AlertFontSize;
            _model.LensPosition          = _settingsSnapshot.LensPosition;
            _model.CustomSoundPath       = _settingsSnapshot.CustomSoundPath;
            _model.CardTint              = _settingsSnapshot.CardTint;
            _model.SnoozeDurationMinutes = _settingsSnapshot.SnoozeDurationMinutes;
            _model.ReminderEnabled         = _settingsSnapshot.ReminderEnabled;
            _model.ReminderLeadMinutes     = _settingsSnapshot.ReminderLeadMinutes;
            _model.ReminderIntervalMinutes = _settingsSnapshot.ReminderIntervalMinutes;
            _model.SendEmailOnReminder     = _settingsSnapshot.SendEmailOnReminder;
            _model.AlertEmailTo            = _settingsSnapshot.AlertEmailTo;
            _model.WebhookUrl              = _settingsSnapshot.WebhookUrl;
            _model.AlertVolume             = _settingsSnapshot.AlertVolume;
            _model.RecurrenceMode          = _settingsSnapshot.RecurrenceMode;
            _model.RecurrenceInterval      = _settingsSnapshot.RecurrenceInterval;
            _model.PomodoroWorkMinutes      = _settingsSnapshot.PomodoroWorkMinutes;
            _model.PomodoroBreakMinutes     = _settingsSnapshot.PomodoroBreakMinutes;
            _model.PomodoroLongBreakMinutes = _settingsSnapshot.PomodoroLongBreakMinutes;
            _model.PomodoroLongBreakAfter   = _settingsSnapshot.PomodoroLongBreakAfter;
            _settingsSnapshot = null;
            NotifyWebhook();
            RaiseUiStateChanged();
        }

        // ── Alert helpers ─────────────────────────────────────────────────────
        private void FireAlert()
        {
            _model.LastFiredAt = DateTime.UtcNow;  // L-8: store UTC — display side calls ToLocalTime()
            AlertSoundPlayer.PlayForModel(_model);
            _alertRepeatCount = 0;
            _alertRepeat.Start();
            ShowAlertWindows();
            // F8: System tray balloon notification
            _mainWindow?.ShowAlertBalloon(_model.Name);
            EmailAlertService.SendAlertAsync(_model.Name, _model.LastFiredAt, _model.AlertEmailTo);
            // Feature 2: Webhook alert
            WebhookService.SendAlert(_model.WebhookUrl, _model.Name, _model.LastFiredAt, _model.Notes);
            // Feature 5: Timer history
            TimerHistory.Record(_model.Id, _model.Name, _model.LastFiredAt, _model.Notes);
            Notify(nameof(LastFiredLabel));
            Notify(nameof(LastFiredVis));
        }

        private void ShowAlertWindows()
        {
            CloseAlertWindows();
            bool showLens       = _model.AlertStyle == AlertStyle.Lens || _model.AlertStyle == AlertStyle.Both;
            bool showFullscreen = _model.AlertStyle == AlertStyle.Fullscreen || _model.AlertStyle == AlertStyle.Both;
            if (showLens)
            {
                _lensAlert = new LensAlertWindow(_model);
                _lensAlert.Dismissed += (s, e) => { _lensAlert = null; DoDismiss(); };
                _lensAlert.Show();
            }
            if (showFullscreen)
            {
                _flashOverlay = new FlashOverlayWindow(_model);
                _flashOverlay.Dismissed += (s, e) => { _flashOverlay = null; DoDismiss(); };
                _flashOverlay.Show();
            }
            // F11: Escalate Lens → Fullscreen if undismissed after N seconds
            if (showLens && !showFullscreen && _model.EscalateAfterSeconds > 0)
            {
                _escalateTimer = new DispatcherTimer
                    { Interval = TimeSpan.FromSeconds(_model.EscalateAfterSeconds) };
                _escalateTimer.Tick += (s, e) =>
                {
                    _escalateTimer.Stop();
                    _escalateTimer = null;
                    if (_model.AlertActive) // only escalate if not yet dismissed
                    {
                        _flashOverlay = new FlashOverlayWindow(_model);
                        _flashOverlay.Dismissed += (s2, e2) => { _flashOverlay = null; DoDismiss(); };
                        _flashOverlay.Show();
                    }
                };
                _escalateTimer.Start();
            }
        }

        private void CloseAlertWindows()
        {
            _escalateTimer?.Stop(); _escalateTimer = null;
            if (_flashOverlay != null)
            { try { _flashOverlay.Close(); } catch (Exception ex) { Debug.WriteLine("[TimerStudio] FlashClose: " + ex.Message); } _flashOverlay = null; }
            if (_lensAlert != null)
            { try { _lensAlert.Close(); } catch (Exception ex) { Debug.WriteLine("[TimerStudio] LensClose: " + ex.Message); } _lensAlert = null; }
        }

        private void FireReminder()
        {
            AlertSoundPlayer.PlayForModel(_model);
            var snap = new TimerModel
            {
                Name           = _model.Name + " reminder",
                AlertColor     = _model.AlertColor,
                AlertTitleColor= _model.AlertTitleColor,
                AlertStyle     = _model.AlertStyle,
                AlertFontSize  = _model.AlertFontSize,
                LensPosition   = _model.LensPosition,
                FlashSeconds   = Math.Max(5, _model.FlashSeconds),
                AlertSound     = AlertSound.Silent
            };
            var remaining = _model.TargetDateTime - DateTime.Now;
            new LensReminderWindow(snap, FormatReminderTime(remaining)).Show();
            _mainWindow?.ShowReminderBalloon(_model.Name, FormatReminderTime(remaining));
            bool isFirstFire = !_model.ReminderPhaseActive;
            if (isFirstFire || _model.SendEmailOnReminder)
                EmailAlertService.SendReminderAlertAsync(_model.Name, remaining, _model.Notes, _model.AlertEmailTo);
            // Feature 2: Webhook reminder
            WebhookService.SendReminder(_model.WebhookUrl, _model.Name, remaining, _model.Notes);
            _model.ReminderPhaseActive = true;
            if (_model.ReminderIntervalMinutes > 0)
            {
                var next = _model.NextReminderAt.AddMinutes(_model.ReminderIntervalMinutes);
                _model.NextReminderAt = next < _model.TargetDateTime ? next : DateTime.MinValue;
            }
            else
                _model.NextReminderAt = DateTime.MinValue;
        }

        // ── F14: Interval phase advance ───────────────────────────────────────
        private void AdvanceIntervalPhase()
        {
            if (_model.IntervalPhase == IntervalPhase.Work)
            {
                _model.IntervalCycleCount++;
                bool allDone = _model.IntervalRepeatCount > 0
                            && _model.IntervalCycleCount >= _model.IntervalRepeatCount;
                if (allDone)
                {
                    _model.State        = TimerState.Done;
                    _model.AlertActive  = true;
                    _model.AlertFiredAt = DateTime.UtcNow;
                    FireAlert();
                    return;
                }
                // Transition Work → Rest
                _model.IntervalPhase = IntervalPhase.Rest;
                var dur = TimeSpan.FromMinutes(_model.IntervalRestMinutes);
                _model.TotalDuration = dur;
                _model.Remaining     = dur;
            }
            else
            {
                // Transition Rest → Work
                _model.IntervalPhase = IntervalPhase.Work;
                var dur = TimeSpan.FromMinutes(_model.IntervalWorkMinutes);
                _model.TotalDuration = dur;
                _model.Remaining     = dur;
            }
            _model.AlertActive    = false;
            _model.AlertDismissed = false;
            _model.StartedAt      = DateTime.UtcNow;
            _model.State          = TimerState.Running;
            // Play a brief chime on each phase transition (uses the timer's AlertSound at low volume)
            AlertSoundPlayer.PlayForModel(_model);
        }

        // ── F16: Lap recording ────────────────────────────────────────────────
        private void RecordLap()
        {
            if (_model.Mode != TimerMode.Stopwatch) return;
            if (_model.State != TimerState.Running && _model.State != TimerState.Paused) return;
            _model.LapTimes.Add(_model.Elapsed);
            Notify(nameof(LapTimes));
            Notify(nameof(LapTableVis));
            Notify(nameof(LapButtonLabel));
        }

        // ── Format helpers ────────────────────────────────────────────────────
        private static string FormatTimeSince(TimeSpan ts)
        {
            if (ts.TotalSeconds < 60)  return $"{(int)ts.TotalSeconds}s";
            if (ts.TotalMinutes < 60)  return $"{(int)ts.TotalMinutes}m {ts.Seconds:D2}s";
            return $"{(int)ts.TotalHours}h {ts.Minutes:D2}m";
        }

        private static string FormatReminderTime(TimeSpan ts)
        {
            if (ts < TimeSpan.Zero) ts = TimeSpan.Zero;
            int h = (int)ts.TotalHours;
            if (h >= 24) return $"{(int)ts.TotalDays}d {ts.Hours}h remaining";
            if (h >= 1)  return $"{h}h {ts.Minutes}m remaining";
            if (ts.TotalMinutes >= 1) return $"{ts.Minutes}m {ts.Seconds}s remaining";
            return $"{ts.Seconds}s remaining";
        }
    }

    // ── RelayCommand — generic ICommand implementation ────────────────────────
    public sealed class RelayCommand : ICommand
    {
        private readonly Action<object>    _execute;
        private readonly Func<object, bool> _canExecute;

        public RelayCommand(Action<object> execute, Func<object, bool> canExecute = null)
        {
            _execute    = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute;
        }

        public event EventHandler CanExecuteChanged
        {
            add    => CommandManager.RequerySuggested += value;
            remove => CommandManager.RequerySuggested -= value;
        }

        public bool CanExecute(object p) => _canExecute?.Invoke(p) ?? true;
        public void Execute(object p)    => _execute(p);
    }
}
