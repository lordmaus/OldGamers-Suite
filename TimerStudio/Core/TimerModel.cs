using System;
using System.Threading;
using System.Windows.Media;

namespace TimerStudio
{
    public enum TimerMode    { Countdown, Stopwatch, DateTime, Pomodoro, Interval }
    public enum TimerState   { Idle, Running, Paused, Done }
    public enum IntervalPhase { Work, Rest }
    public enum AlertSound   { Beep, Chime, Alarm, Bell, Ping, Silent }
    public enum AlertStyle   { Lens, Fullscreen, Both }
    public enum LensPosition { TopLeft, TopCenter, TopRight,
                               MiddleLeft, Center, MiddleRight,
                               BottomLeft, BottomCenter, BottomRight }

    // ── Feature: Recurring DateTime events ───────────────────────────────────
    public enum RecurrenceMode { None, Daily, Weekly, Monthly }

    // ── Feature: Pomodoro ─────────────────────────────────────────────────────
    public enum PomodoroPhase { Work, Break, LongBreak }

    public class TimerModel
    {
        private static int _nextId = 1;

        public int        Id         { get; internal set; }
        public string     Name       { get; set; }
        public TimerMode  Mode       { get; set; }
        public TimerState State      { get; set; }

        // Countdown duration config
        public int Hours   { get; set; }
        public int Minutes { get; set; }
        public int Seconds { get; set; }

        // Date/Time target config
        public DateTime TargetDateTime { get; set; }

        // Date/Time reminder config (DateTime mode only)
        public bool ReminderEnabled        { get; set; }  // master enable
        public int  ReminderLeadMinutes    { get; set; }  // minutes before target to first fire (1–4320 = 72h)
        public int  ReminderIntervalMinutes{ get; set; }  // repeat interval in minutes; 0 = fire once only

        public bool   SendEmailOnReminder  { get; set; }  // send email alert when each reminder fires
        public string AlertEmailTo         { get; set; }  // override recipient(s) for this event; empty = use global setting

        // Runtime reminder state (not persisted — recalculated on start)
        public DateTime NextReminderAt     { get; set; }  // when next reminder fires
        public bool     ReminderPhaseActive{ get; set; }  // true = reminder(s) already fired at least once

        // Alert config
        public Color      AlertColor       { get; set; }
        public Color      AlertTitleColor  { get; set; }
        public AlertSound AlertSound       { get; set; }
        public AlertStyle AlertStyle       { get; set; }
        public int        FlashSeconds     { get; set; }
        public bool       AutoReset        { get; set; }
        public int        AlertFontSize    { get; set; }
        public LensPosition LensPosition  { get; set; }

        // UI state
        public bool     Collapsed         { get; set; }
        public bool     SettingsExpanded  { get; set; }
        public bool     NotesExpanded     { get; set; }
        public string   Notes             { get; set; }
        public string   Category              { get; set; }   // user-defined category label
        public bool     OverrideCategoryColor  { get; set; }   // if true, use manual AlertColor instead of category colour
        public bool     ShowInOverlay          { get; set; }   // pin this timer to the persistent overlay window
        public string   CustomSoundPath        { get; set; }   // path to .wav/.mp3; null = use built-in AlertSound
        public string   CardTint               { get; set; }   // hex card background tint, e.g. "#1A2A1A"; null = default
        public int      SnoozeDurationMinutes  { get; set; } = 5;  // snooze length in minutes

        // ── Feature 1: Recurring DateTime events ─────────────────────────────
        public RecurrenceMode RecurrenceMode     { get; set; } = RecurrenceMode.None;
        public int            RecurrenceInterval { get; set; } = 1;  // every N days/weeks/months

        // ── Feature 2: Webhook alert ──────────────────────────────────────────
        public string WebhookUrl { get; set; } = "";

        // ── Feature 3: Per-timer volume ───────────────────────────────────────
        /// <summary>0.0–1.0. Applies to custom audio files via MediaPlayer. Built-in beeps use system volume.</summary>
        public double AlertVolume { get; set; } = 1.0;

        // ── Feature 5: Last-fired timestamp ──────────────────────────────────
        public DateTime LastFiredAt { get; set; } = DateTime.MinValue;

        // ── Feature 8: Pomodoro ───────────────────────────────────────────────
        public int PomodoroWorkMinutes      { get; set; } = 25;
        public int PomodoroBreakMinutes     { get; set; } = 5;
        public int PomodoroLongBreakMinutes { get; set; } = 15;
        public int PomodoroLongBreakAfter   { get; set; } = 4;   // work sessions before long break
        // Runtime Pomodoro state (not persisted — reset on each Start):
        public PomodoroPhase PomodoroPhase        { get; set; } = PomodoroPhase.Work;
        public int           PomodoroSessionCount { get; set; } = 0;

        // ── F9: Alert repeat limit ────────────────────────────────────────────
        /// <summary>Max number of alert repeat cycles. 0 = unlimited (default).</summary>
        public int AlertRepeatLimit { get; set; } = 0;

        // ── F11: Escalating alert ─────────────────────────────────────────────
        /// <summary>
        /// If > 0 and AlertStyle is Lens, escalates to Fullscreen after this
        /// many seconds of no dismiss. 0 = disabled.
        /// </summary>
        public int EscalateAfterSeconds { get; set; } = 0;

        // ── Pill overlay visibility ───────────────────────────────────────────
        /// <summary>When true, this timer is hidden from the Pill overlay. Default false (shown).</summary>
        public bool ExcludeFromPill { get; set; } = false;

        // ── F14: Interval timer ───────────────────────────────────────────────
        public int IntervalWorkMinutes  { get; set; } = 25;
        public int IntervalRestMinutes  { get; set; } = 5;
        /// <summary>Total cycles to run. 0 = run until manually stopped.</summary>
        public int IntervalRepeatCount  { get; set; } = 4;
        // Runtime interval state (not persisted):
        public IntervalPhase IntervalPhase        { get; set; } = IntervalPhase.Work;
        public int           IntervalCycleCount   { get; set; } = 0;   // completed work cycles

        // ── F16: Multi-lap stopwatch ──────────────────────────────────────────
        // Runtime only — not persisted. Cleared on Reset.
        // Lazy: non-stopwatch timers never allocate this list.
        private System.Collections.Generic.List<TimeSpan> _lapTimes;
        public  System.Collections.Generic.List<TimeSpan> LapTimes
            => _lapTimes ??= new System.Collections.Generic.List<TimeSpan>();

        // Runtime
        public TimeSpan TotalDuration   { get; set; }
        public TimeSpan Remaining       { get; set; }
        public TimeSpan Elapsed         { get; set; }
        public DateTime StartedAt       { get; set; }
        public TimeSpan PausedRemaining { get; set; }
        public TimeSpan PausedElapsed   { get; set; }
        public bool     AlertActive     { get; set; }
        public bool     AlertDismissed  { get; set; }
        public DateTime AlertFiredAt    { get; set; }

        public TimerModel()
        {
            Id               = Interlocked.Increment(ref _nextId);
            Name             = "Timer " + Id;
            Mode             = TimerMode.Countdown;
            State            = TimerState.Idle;
            Hours            = 0;
            Minutes          = 5;
            Seconds          = 0;
            TargetDateTime          = System.DateTime.Now.AddHours(1);
            ReminderEnabled         = false;
            ReminderLeadMinutes     = 60;    // default: 1 hour early
            ReminderIntervalMinutes = 0;     // default: fire once
            AlertColor       = Color.FromRgb(249, 226, 175);
            AlertTitleColor  = Color.FromRgb(30, 30, 46);  // dark navy — readable on amber (12.91:1 contrast)
            AlertSound       = (AlertSound)AppPrefs.Instance.DefaultAlertSound;
            AlertStyle       = (AlertStyle)AppPrefs.Instance.DefaultAlertStyle;
            FlashSeconds     = AppPrefs.Instance.DefaultFlashSecs;
            AutoReset        = AppPrefs.Instance.DefaultAutoReset;
            AlertFontSize    = AppPrefs.Instance.DefaultFontSize;
            Notes            = "";
            Category              = "";
            CustomSoundPath       = null;
            CardTint              = null;
            SnoozeDurationMinutes = 5;
            OverrideCategoryColor = false;
            ShowInOverlay        = false;
            SettingsExpanded = false;

            LensPosition     = LensPosition.BottomRight;
        }

        internal void OverrideId(int id) { Id = id; }

        /// <summary>Creates a new timer pre-configured with DateTime/Event defaults.</summary>
        public static TimerModel CreateEventTimer()
        {
            var m = new TimerModel();
            m.Mode = TimerMode.DateTime;
            // Alert defaults are shared — same as Countdown/Stopwatch
            return m;
        }

        public static void SeedNextId(System.Collections.Generic.IEnumerable<TimerModel> loaded)
        {
            int max = 0;
            foreach (var m in loaded) if (m.Id > max) max = m.Id;
            if (max >= _nextId) _nextId = max + 1;
        }

        public TimeSpan GetConfiguredDuration()
        {
            return new TimeSpan(Hours, Minutes, Seconds);
        }

        /// <summary>
        /// For DateTime mode: time remaining until TargetDateTime.
        /// Can be negative if the target has already passed.
        /// </summary>
        public TimeSpan GetDateTimeRemaining()
        {
            return TargetDateTime - System.DateTime.Now;  // Local time — intentional for user-set targets
        }

        public double Progress
        {
            get
            {
                if (TotalDuration.TotalSeconds == 0) return 0;
                if (Mode == TimerMode.Countdown || Mode == TimerMode.DateTime
                    || Mode == TimerMode.Pomodoro || Mode == TimerMode.Interval)
                {
                    double p = Remaining.TotalSeconds / TotalDuration.TotalSeconds;
                    return p < 0 ? 0 : p > 1 ? 1 : p;
                }
                return 0;
            }
        }

        public string DisplayTime
        {
            get
            {
                if (Mode == TimerMode.Stopwatch)
                {
                    TimeSpan ts = Elapsed < TimeSpan.Zero ? TimeSpan.Zero : Elapsed;
                    return string.Format("{0:D2}:{1:D2}:{2:D2}.{3:D2}",
                        (int)ts.TotalHours, ts.Minutes, ts.Seconds, ts.Milliseconds / 10);
                }

                if (Mode == TimerMode.DateTime)
                {
                    // When idle, show time until target
                    TimeSpan ts = State == TimerState.Idle
                        ? GetDateTimeRemaining()
                        : Remaining;
                    if (ts < TimeSpan.Zero) ts = TimeSpan.Zero;
                    int dh = (int)ts.TotalHours;
                    if (dh >= 24)
                    {
                        int days = (int)ts.TotalDays;
                        int hrs  = ts.Hours;
                        return string.Format("{0}d {1:D2}:{2:D2}:{3:D2}",
                            days, hrs, ts.Minutes, ts.Seconds);
                    }
                    return dh > 0
                        ? string.Format("{0:D2}:{1:D2}:{2:D2}", dh, ts.Minutes, ts.Seconds)
                        : string.Format("{0:D2}:{1:D2}", ts.Minutes, ts.Seconds);
                }

                if (Mode == TimerMode.Pomodoro)
                {
                    TimeSpan ts = State == TimerState.Idle
                        ? TimeSpan.FromMinutes(PomodoroWorkMinutes)
                        : Remaining;
                    if (ts < TimeSpan.Zero) ts = TimeSpan.Zero;
                    int h = (int)ts.TotalHours;
                    return h > 0
                        ? string.Format("{0:D2}:{1:D2}:{2:D2}", h, ts.Minutes, ts.Seconds)
                        : string.Format("{0:D2}:{1:D2}", ts.Minutes, ts.Seconds);
                }

                if (Mode == TimerMode.Interval)
                {
                    TimeSpan ts = State == TimerState.Idle
                        ? TimeSpan.FromMinutes(IntervalWorkMinutes)
                        : Remaining;
                    if (ts < TimeSpan.Zero) ts = TimeSpan.Zero;
                    int h = (int)ts.TotalHours;
                    return h > 0
                        ? string.Format("{0:D2}:{1:D2}:{2:D2}", h, ts.Minutes, ts.Seconds)
                        : string.Format("{0:D2}:{1:D2}", ts.Minutes, ts.Seconds);
                }

                // Countdown
                {
                    TimeSpan ts = State == TimerState.Idle
                        ? GetConfiguredDuration()
                        : Remaining;
                    if (ts < TimeSpan.Zero) ts = TimeSpan.Zero;
                    int h = (int)ts.TotalHours;
                    return h > 0
                        ? string.Format("{0:D2}:{1:D2}:{2:D2}", h, ts.Minutes, ts.Seconds)
                        : string.Format("{0:D2}:{1:D2}", ts.Minutes, ts.Seconds);
                }
            }
        }
    }
}
