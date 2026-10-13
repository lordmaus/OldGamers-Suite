using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TimerStudio
{
    // ── DTOs — simple POCO types that System.Text.Json can source-generate ──

    public class TimerDto
    {
        public int    Id                      { get; set; }
        public string Name                    { get; set; } = "";
        public int    Mode                    { get; set; }
        public int    Hours                   { get; set; }
        public int    Minutes                 { get; set; }
        public int    Seconds                 { get; set; }
        public string TargetDateTime          { get; set; } = "";
        public bool   ReminderEnabled         { get; set; }
        public int    ReminderLeadMinutes     { get; set; }
        public int    ReminderIntervalMinutes { get; set; }
        public bool   SendEmailOnReminder     { get; set; }
        public string AlertEmailTo            { get; set; } = "";
        public string AlertColor              { get; set; } = "";
        public string AlertTitleColor         { get; set; } = "";
        public int    AlertSound              { get; set; }
        public int    AlertStyle              { get; set; }
        public int    FlashSeconds            { get; set; }
        public bool   AutoReset               { get; set; }
        public int    AlertFontSize           { get; set; }
        public int    LensPosition            { get; set; }
        public long   StopwatchElapsedMs      { get; set; }
        public bool   Collapsed               { get; set; }
        public bool   SettingsExpanded        { get; set; }
        public bool   NotesExpanded           { get; set; }
        public string Notes                   { get; set; } = "";
        public string Category                { get; set; } = "";
        public bool   OverrideCategoryColor   { get; set; }
        public bool   ShowInOverlay           { get; set; }
        public bool   WasRunning              { get; set; }
        public string CustomSoundPath        { get; set; } = "";
        public string CardTint               { get; set; } = "";
        public int    SnoozeDurationMinutes  { get; set; } = 5;

        // Feature 1: Recurrence
        public int    RecurrenceMode         { get; set; } = 0;
        public int    RecurrenceInterval     { get; set; } = 1;
        // Feature 2: Webhook
        public string WebhookUrl             { get; set; } = "";
        // Feature 3: Volume
        public double AlertVolume            { get; set; } = 1.0;
        // Feature 5: Last fired
        public string LastFiredAt            { get; set; } = "";  // ISO-8601; empty = never
        // Feature 8: Pomodoro
        public int    PomodoroWorkMinutes      { get; set; } = 25;
        public int    PomodoroBreakMinutes     { get; set; } = 5;
        public int    PomodoroLongBreakMinutes { get; set; } = 15;
        public int    PomodoroLongBreakAfter   { get; set; } = 4;
        // F9: Alert repeat limit
        public int    AlertRepeatLimit         { get; set; } = 0;
        // F11: Escalating alert
        public int    EscalateAfterSeconds     { get; set; } = 0;
        // F14: Interval timer
        public int    IntervalWorkMinutes      { get; set; } = 25;
        public int    IntervalRestMinutes      { get; set; } = 5;
        public int    IntervalRepeatCount      { get; set; } = 4;
        // Pill overlay
        public bool   ExcludeFromPill          { get; set; } = false;
    }

    public class PrefsDto
    {
        public double WindowLeft          { get; set; } = double.NaN;
        public double WindowTop           { get; set; } = double.NaN;
        public double WindowWidth         { get; set; } = 1150;
        public double WindowHeight        { get; set; } = 1060;
        public bool   Maximized           { get; set; }
        public bool   DarkTheme           { get; set; } = true;
        public string SelectedTheme       { get; set; } = "Dark";
        public double TimeFontScale       { get; set; } = 1.0;
        public double TitleFontScale      { get; set; } = 1.0;
        public double EventTimeFontScale  { get; set; } = 1.0;
        public double EventTitleFontScale { get; set; } = 1.0;
        public double OverlayTimeFontScale  { get; set; } = 1.0;
        public double OverlayTitleFontScale { get; set; } = 1.0;
        public double UiScale             { get; set; } = 1.0;
        public bool   HotkeysEnabled      { get; set; } = true;
        public string HotkeyAddTimer      { get; set; } = "Ctrl+N";
        public string HotkeySettings      { get; set; } = "Alt+S";
        public string HotkeyTheme         { get; set; } = "Alt+T";
        public string HotkeyHide          { get; set; } = "Alt+H";
        public string HotkeyAbout         { get; set; } = "Alt+A";
        public int    DefaultAlertSound   { get; set; }
        public int    DefaultAlertStyle   { get; set; }
        public int    DefaultFlashSecs    { get; set; } = 5;
        public int    DefaultFontSize     { get; set; } = 22;
        public bool   DefaultAutoReset    { get; set; }
        public Dictionary<string, string> CategoryColors { get; set; } = new();
        public List<string>               Categories     { get; set; } = new();
        public List<TimerDto>             Templates      { get; set; } = new();

        // ── Calendar settings ─────────────────────────────────────────────────
        public string       DefaultCalendarPath        { get; set; } = "";
        public List<string> HiddenCalendarPaths        { get; set; } = new();
        public bool         CalendarAutoPullEnabled     { get; set; }
        public int          CalendarPollIntervalMinutes { get; set; } = 15;
        /// <summary>Only import events starting within this many days from now (1–365).</summary>
        public int          CalendarLookaheadDays       { get; set; } = 30;
        /// <summary>Events whose title contains any of these keywords are skipped during auto-pull.</summary>
        public List<string> CalendarExcludeKeywords     { get; set; } = new();
        public bool         CalendarHideBirthdays       { get; set; } = false;
        public bool         CalendarHideHolidays        { get; set; } = false;

        // ── Email alert settings ──────────────────────────────────────────────
        /// <summary>0 = Disabled, 1 = MailKit/SMTP, 2 = Outlook COM</summary>
        public bool   EmailAlertEnabled     { get; set; }
        public int    EmailAlertMode        { get; set; }
        public string SmtpHost              { get; set; } = "";
        public int    SmtpPort              { get; set; } = 587;
        public string SmtpUser              { get; set; } = "";
        /// <summary>AES-128 encrypted, base64 — keyed to user + machine.</summary>
        public string SmtpPasswordEncrypted { get; set; } = "";
        public string EmailAlertFrom        { get; set; } = "";
        public string EmailAlertTo          { get; set; } = "";
        /// <summary>Legacy (pre-2026.10.12) — read for migration only, never written.</summary>
        public bool?  UseSmtpTls            { get; set; }
        public SmtpSecurity? SmtpSecurity   { get; set; }
        public string CalendarLastPulled    { get; set; } = "";  // ISO-8601 UTC; empty = never
        public string MsOAuthClientId       { get; set; } = "";
        public string MsOAuthTokenCache     { get; set; } = "";
        // Feature 6: Auto theme
        public bool   ThemeAuto             { get; set; } = false;
        // Feature 7: Collapsed category groups + grouped view
        public List<string> CollapsedCategories { get; set; } = new();
        public bool         GroupedView         { get; set; } = false;
        // Feature 11: Pill window state
        public bool         PillWindowVisible   { get; set; } = false;
        // Sort persistence
        public int          LastSortMode        { get; set; } = -1;
        // Calendar event timer defaults
        public int  CalEvtLensPosition            { get; set; } = 8;
        public int  CalEvtFlashSeconds            { get; set; } = 5;
        public int  CalEvtAlertRepeatLimit        { get; set; } = 0;
        public int  CalEvtEscalateAfterSeconds    { get; set; } = 0;
        public bool CalEvtExcludeFromPill         { get; set; } = false;
        public bool CalEvtReminderEnabled         { get; set; } = false;
        public int  CalEvtReminderLeadMinutes     { get; set; } = 60;
        public int  CalEvtReminderIntervalMinutes { get; set; } = 0;
        public bool CalEvtSendEmailOnReminder     { get; set; } = false;
    }

    // ── Source-generated JSON context — zero reflection at runtime ────────────

    [JsonSerializable(typeof(List<TimerDto>))]
    [JsonSerializable(typeof(TimerDto))]
    [JsonSerializable(typeof(PrefsDto))]
    // PropertyNameCaseInsensitive: external writers (NoteStation's task-push
    // integration writes hand-built PascalCase JsonObject entries, not through
    // this DTO contract) must still deserialize correctly instead of silently
    // dropping to all-default blank timers. See TimerPersistence.ExternalChangeDetected.
    [JsonSourceGenerationOptions(
        WriteIndented                 = true,
        PropertyNamingPolicy          = JsonKnownNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive   = true,
        DefaultIgnoreCondition        = JsonIgnoreCondition.WhenWritingNull,
        AllowTrailingCommas           = true,
        ReadCommentHandling           = JsonCommentHandling.Skip)]
    internal partial class TimerStudioJsonContext : JsonSerializerContext { }

    // ── Mapper helpers between DTOs and domain models ────────────────────────

    internal static class DtoMapper
    {
        // TS-03 migration: settings written before 2026.10.12 only carry UseSmtpTls.
        // TLS-off + login becomes Auto (never plaintext credentials); TLS-off without
        // login (anonymous relay) keeps working as None.
        internal static SmtpSecurity ResolveSmtpSecurity(PrefsDto d)
        {
            if (d.SmtpSecurity is SmtpSecurity s && Enum.IsDefined(s)) return s;
            return d.UseSmtpTls == false && string.IsNullOrEmpty(d.SmtpUser)
                ? SmtpSecurity.None
                : SmtpSecurity.Auto;
        }

        private static readonly JsonSerializerOptions _opts =
            TimerStudioJsonContext.Default.Options;

        // ── Serialize ─────────────────────────────────────────────────────────
        public static string SerializeTimers(IEnumerable<TimerDto> dtos)
            => JsonSerializer.Serialize(
                   new List<TimerDto>(dtos),
                   TimerStudioJsonContext.Default.ListTimerDto);

        public static string SerializePrefs(PrefsDto dto)
            => JsonSerializer.Serialize(dto, TimerStudioJsonContext.Default.PrefsDto);

        // ── Deserialize ───────────────────────────────────────────────────────
        public static List<TimerDto> DeserializeTimers(string json)
        {
            try
            {
                return JsonSerializer.Deserialize(json,
                           TimerStudioJsonContext.Default.ListTimerDto)
                       ?? new List<TimerDto>();
            }
            catch { return new List<TimerDto>(); }
        }

        public static PrefsDto DeserializePrefs(string json)
        {
            try
            {
                return JsonSerializer.Deserialize(json,
                           TimerStudioJsonContext.Default.PrefsDto)
                       ?? new PrefsDto();
            }
            catch { return new PrefsDto(); }
        }

        // ── TimerModel → DTO ──────────────────────────────────────────────────
        public static TimerDto ToDto(TimerModel m) => new TimerDto
        {
            Id                      = m.Id,
            Name                    = m.Name ?? "",
            Mode                    = (int)m.Mode,
            Hours                   = m.Hours,
            Minutes                 = m.Minutes,
            Seconds                 = m.Seconds,
            TargetDateTime          = m.TargetDateTime.ToString("o"),
            ReminderEnabled         = m.ReminderEnabled,
            ReminderLeadMinutes     = m.ReminderLeadMinutes,
            ReminderIntervalMinutes = m.ReminderIntervalMinutes,
            SendEmailOnReminder     = m.SendEmailOnReminder,
            AlertEmailTo            = m.AlertEmailTo ?? "",
            AlertColor              = ColorToHex(m.AlertColor),
            AlertTitleColor         = ColorToHex(m.AlertTitleColor),
            AlertSound              = (int)m.AlertSound,
            AlertStyle              = (int)m.AlertStyle,
            FlashSeconds            = m.FlashSeconds,
            AutoReset               = m.AutoReset,
            AlertFontSize           = m.AlertFontSize,
            LensPosition            = (int)m.LensPosition,
            StopwatchElapsedMs      = (long)m.Elapsed.TotalMilliseconds,
            Collapsed               = m.Collapsed,
            SettingsExpanded        = m.SettingsExpanded,
            NotesExpanded           = m.NotesExpanded,
            Notes                   = m.Notes ?? "",
            Category                = m.Category ?? "",
            OverrideCategoryColor   = m.OverrideCategoryColor,
            ShowInOverlay           = m.ShowInOverlay,
            CustomSoundPath        = m.CustomSoundPath ?? "",
            CardTint               = m.CardTint ?? "",
            SnoozeDurationMinutes  = m.SnoozeDurationMinutes,
            WasRunning              = (m.Mode == TimerMode.DateTime || m.Mode == TimerMode.Pomodoro)
                                      && m.State == TimerState.Running,
            // Feature 1: Recurrence
            RecurrenceMode         = (int)m.RecurrenceMode,
            RecurrenceInterval     = m.RecurrenceInterval,
            // Feature 2: Webhook
            WebhookUrl             = m.WebhookUrl ?? "",
            // Feature 3: Volume
            AlertVolume            = m.AlertVolume,
            // Feature 5: Last fired
            LastFiredAt            = m.LastFiredAt == DateTime.MinValue ? "" : m.LastFiredAt.ToString("o"),
            // Feature 8: Pomodoro
            PomodoroWorkMinutes      = m.PomodoroWorkMinutes,
            PomodoroBreakMinutes     = m.PomodoroBreakMinutes,
            PomodoroLongBreakMinutes = m.PomodoroLongBreakMinutes,
            PomodoroLongBreakAfter   = m.PomodoroLongBreakAfter,
            // F9: Repeat limit
            AlertRepeatLimit         = m.AlertRepeatLimit,
            // F11: Escalate
            EscalateAfterSeconds     = m.EscalateAfterSeconds,
            // F14: Interval
            IntervalWorkMinutes      = m.IntervalWorkMinutes,
            IntervalRestMinutes      = m.IntervalRestMinutes,
            IntervalRepeatCount      = m.IntervalRepeatCount,
            // Pill
            ExcludeFromPill          = m.ExcludeFromPill,
        };

        // ── DTO → TimerModel ──────────────────────────────────────────────────
        public static TimerModel FromDto(TimerDto d)
        {
            var m = new TimerModel();
            if (d.Id > 0) m.OverrideId(d.Id);
            if (!string.IsNullOrEmpty(d.Name)) m.Name = d.Name;

            m.Mode    = (TimerMode)Clamp(d.Mode, 0, 4);  // 4 = Interval (F14)
            m.Hours   = Clamp(d.Hours,   0, 99);
            m.Minutes = Clamp(d.Minutes, 0, 59);
            m.Seconds = Clamp(d.Seconds, 0, 59);

            if (!string.IsNullOrEmpty(d.TargetDateTime))
            {
                if (DateTime.TryParse(d.TargetDateTime,
                        System.Globalization.CultureInfo.InvariantCulture,
                        System.Globalization.DateTimeStyles.RoundtripKind,
                        out DateTime dt))
                    m.TargetDateTime = dt;
            }

            if (!string.IsNullOrEmpty(d.AlertColor))
                m.AlertColor = HexToColor(d.AlertColor, m.AlertColor);
            if (!string.IsNullOrEmpty(d.AlertTitleColor))
                m.AlertTitleColor = HexToColor(d.AlertTitleColor, m.AlertTitleColor);

            m.ReminderEnabled         = d.ReminderEnabled;
            m.ReminderLeadMinutes     = Clamp(d.ReminderLeadMinutes,     1, 4320);
            m.ReminderIntervalMinutes = Clamp(d.ReminderIntervalMinutes, 0, 4320);
            m.SendEmailOnReminder     = d.SendEmailOnReminder;
            m.AlertEmailTo            = d.AlertEmailTo ?? "";
            m.AlertSound    = (AlertSound)Clamp(d.AlertSound,    0, 5);
            m.AlertStyle    = (AlertStyle)Clamp(d.AlertStyle,    0, 2);
            m.FlashSeconds  = Math.Max(0, d.FlashSeconds);
            m.AutoReset     = d.AutoReset;
            m.AlertFontSize = Clamp(d.AlertFontSize, 8, 120);
            m.LensPosition  = (LensPosition)Clamp(d.LensPosition, 0, 8);

            if (m.Mode == TimerMode.Stopwatch && d.StopwatchElapsedMs > 0)
            {
                m.Elapsed       = TimeSpan.FromMilliseconds(d.StopwatchElapsedMs);
                m.State         = TimerState.Paused;
                m.PausedElapsed = m.Elapsed;
            }

            m.Collapsed            = d.Collapsed;
            m.SettingsExpanded     = d.SettingsExpanded;
            m.NotesExpanded        = d.NotesExpanded;
            m.Notes                = d.Notes ?? "";
            m.Category             = d.Category ?? "";
            m.OverrideCategoryColor = d.OverrideCategoryColor;
            m.ShowInOverlay        = d.ShowInOverlay;
            m.CustomSoundPath      = string.IsNullOrEmpty(d.CustomSoundPath) ? null : d.CustomSoundPath;
            m.CardTint             = string.IsNullOrEmpty(d.CardTint)        ? null : d.CardTint;
            m.SnoozeDurationMinutes = d.SnoozeDurationMinutes > 0 ? d.SnoozeDurationMinutes : 5;
            // Feature 1: Recurrence
            m.RecurrenceMode     = (RecurrenceMode)Clamp(d.RecurrenceMode, 0, 3);
            m.RecurrenceInterval = Math.Max(1, d.RecurrenceInterval);
            // Feature 2: Webhook
            m.WebhookUrl = d.WebhookUrl ?? "";
            // Feature 3: Volume
            m.AlertVolume = d.AlertVolume < 0.0 ? 0.0 : d.AlertVolume > 1.0 ? 1.0 : d.AlertVolume;
            // Feature 5: Last fired
            m.LastFiredAt = string.IsNullOrEmpty(d.LastFiredAt) ? DateTime.MinValue
                : (DateTime.TryParse(d.LastFiredAt, System.Globalization.CultureInfo.InvariantCulture,
                       System.Globalization.DateTimeStyles.RoundtripKind, out DateTime lf) ? lf : DateTime.MinValue);
            // Feature 8: Pomodoro
            m.PomodoroWorkMinutes      = d.PomodoroWorkMinutes      > 0 ? d.PomodoroWorkMinutes      : 25;
            m.PomodoroBreakMinutes     = d.PomodoroBreakMinutes     > 0 ? d.PomodoroBreakMinutes     : 5;
            m.PomodoroLongBreakMinutes = d.PomodoroLongBreakMinutes > 0 ? d.PomodoroLongBreakMinutes : 15;
            m.PomodoroLongBreakAfter   = d.PomodoroLongBreakAfter   > 0 ? d.PomodoroLongBreakAfter   : 4;
            // F9: Repeat limit
            m.AlertRepeatLimit     = Math.Max(0, d.AlertRepeatLimit);
            // F11: Escalate
            m.EscalateAfterSeconds = Math.Max(0, d.EscalateAfterSeconds);
            // F14: Interval
            m.IntervalWorkMinutes  = d.IntervalWorkMinutes  > 0 ? d.IntervalWorkMinutes  : 25;
            m.IntervalRestMinutes  = d.IntervalRestMinutes  > 0 ? d.IntervalRestMinutes  : 5;
            m.IntervalRepeatCount  = Math.Max(0, d.IntervalRepeatCount);
            // Pill
            m.ExcludeFromPill      = d.ExcludeFromPill;

            // Restore running state for DateTime timers
            if (d.WasRunning && m.Mode == TimerMode.DateTime)
            {
                if (m.TargetDateTime <= DateTime.Now)
                {
                    m.State        = TimerState.Done;
                    m.AlertActive  = true;
                    m.AlertFiredAt = m.TargetDateTime.ToUniversalTime();
                    m.Remaining    = TimeSpan.Zero;
                }
                else
                {
                    TimeSpan remaining = m.TargetDateTime - DateTime.Now;
                    m.TotalDuration    = remaining;
                    m.Remaining        = remaining;
                    m.StartedAt        = DateTime.UtcNow;
                    m.State            = TimerState.Running;
                    if (m.ReminderEnabled && m.ReminderLeadMinutes > 0)
                    {
                        DateTime first = m.TargetDateTime.AddMinutes(-m.ReminderLeadMinutes);
                        if (first > DateTime.Now)
                        {
                            // First reminder is still in the future — use it as-is
                            m.NextReminderAt = first;
                        }
                        else if (m.ReminderIntervalMinutes > 0)
                        {
                            // First slot is past — advance through intervals to find next future slot
                            DateTime next = first;
                            while (next <= DateTime.Now)
                                next = next.AddMinutes(m.ReminderIntervalMinutes);
                            m.NextReminderAt = next < m.TargetDateTime ? next : DateTime.MinValue;
                        }
                        else
                        {
                            // No interval — once-only reminder already fired
                            m.NextReminderAt = DateTime.MinValue;
                        }
                    }
                }
            }
            else if (m.State != TimerState.Paused)
            {
                m.State = TimerState.Idle;
            }

            return m;
        }

        // ── AppPrefs → DTO ────────────────────────────────────────────────────
        public static PrefsDto ToDto(AppPrefs p) => new PrefsDto
        {
            WindowLeft        = p.WindowLeft,
            WindowTop         = p.WindowTop,
            WindowWidth       = p.WindowWidth,
            WindowHeight      = p.WindowHeight,
            Maximized         = p.Maximized,
            DarkTheme         = p.DarkTheme,
            SelectedTheme     = p.SelectedTheme ?? "Dark",
            TimeFontScale     = p.TimeFontScale,
            TitleFontScale    = p.TitleFontScale,
            EventTimeFontScale  = p.EventTimeFontScale,
            EventTitleFontScale = p.EventTitleFontScale,
            OverlayTimeFontScale  = p.OverlayTimeFontScale,
            OverlayTitleFontScale = p.OverlayTitleFontScale,
            UiScale           = p.UiScale,
            HotkeysEnabled    = p.HotkeysEnabled,
            HotkeyAddTimer    = p.HotkeyAddTimer,
            HotkeySettings    = p.HotkeySettings,
            HotkeyTheme       = p.HotkeyTheme,
            HotkeyHide        = p.HotkeyHide,
            HotkeyAbout       = p.HotkeyAbout,
            DefaultAlertSound = p.DefaultAlertSound,
            DefaultAlertStyle = p.DefaultAlertStyle,
            DefaultFlashSecs  = p.DefaultFlashSecs,
            DefaultFontSize   = p.DefaultFontSize,
            DefaultAutoReset  = p.DefaultAutoReset,
            CategoryColors    = new Dictionary<string, string>(
                                    p.CategoryColors,
                                    StringComparer.OrdinalIgnoreCase),
            Categories        = new List<string>(p.Categories),
            Templates         = new List<TimerDto>(p.Templates),
            // Calendar
            DefaultCalendarPath        = p.DefaultCalendarPath ?? "",
            HiddenCalendarPaths        = new List<string>(p.HiddenCalendarPaths),
            CalendarAutoPullEnabled    = p.CalendarAutoPullEnabled,
            CalendarPollIntervalMinutes= p.CalendarPollIntervalMinutes,
            CalendarLookaheadDays      = p.CalendarLookaheadDays,
            CalendarExcludeKeywords    = new List<string>(p.CalendarExcludeKeywords),
            CalendarHideBirthdays      = p.CalendarHideBirthdays,
            CalendarHideHolidays       = p.CalendarHideHolidays,
            // Email
            EmailAlertEnabled    = p.EmailAlertEnabled,
            EmailAlertMode       = (int)p.EmailAlertMode,
            SmtpHost             = p.SmtpHost ?? "",
            SmtpPort             = p.SmtpPort,
            SmtpUser             = p.SmtpUser ?? "",
            SmtpPasswordEncrypted= p.SmtpPasswordEncrypted ?? "",
            EmailAlertFrom       = p.EmailAlertFrom ?? "",
            EmailAlertTo         = p.EmailAlertTo ?? "",
            SmtpSecurity         = p.SmtpSecurity,
            MsOAuthClientId      = p.MsOAuthClientId  ?? "",
            MsOAuthTokenCache    = p.MsOAuthTokenCache ?? "",
            CalendarLastPulled   = p.CalendarLastPulled == DateTime.MinValue
                                   ? "" : p.CalendarLastPulled.ToString("o"),
            ThemeAuto            = p.ThemeAuto,
            CollapsedCategories  = new List<string>(p.CollapsedCategories),
            GroupedView          = p.GroupedView,
            PillWindowVisible    = p.PillWindowVisible,
            LastSortMode         = p.LastSortMode,
            // Calendar event defaults
            CalEvtLensPosition            = p.CalEvtLensPosition,
            CalEvtFlashSeconds            = p.CalEvtFlashSeconds,
            CalEvtAlertRepeatLimit        = p.CalEvtAlertRepeatLimit,
            CalEvtEscalateAfterSeconds    = p.CalEvtEscalateAfterSeconds,
            CalEvtExcludeFromPill         = p.CalEvtExcludeFromPill,
            CalEvtReminderEnabled         = p.CalEvtReminderEnabled,
            CalEvtReminderLeadMinutes     = p.CalEvtReminderLeadMinutes,
            CalEvtReminderIntervalMinutes = p.CalEvtReminderIntervalMinutes,
            CalEvtSendEmailOnReminder     = p.CalEvtSendEmailOnReminder,
        };

        // ── DTO → AppPrefs ────────────────────────────────────────────────────
        public static void ApplyDto(PrefsDto d, AppPrefs p)
        {
            p.WindowLeft        = d.WindowLeft;
            p.WindowTop         = d.WindowTop;
            p.WindowWidth       = d.WindowWidth < 380 ? 1150 : d.WindowWidth;
            p.WindowHeight      = d.WindowHeight < 400 ? 1060 : d.WindowHeight;
            p.Maximized         = d.Maximized;
            p.DarkTheme         = d.DarkTheme;
            p.SelectedTheme     = string.IsNullOrEmpty(d.SelectedTheme) ? "Dark" : d.SelectedTheme;
            p.TimeFontScale     = Clamp(d.TimeFontScale,  0.5, 4.0);
            p.TitleFontScale    = Clamp(d.TitleFontScale, 0.5, 4.0);
            p.EventTimeFontScale  = Clamp(d.EventTimeFontScale,  0.5, 4.0);
            p.EventTitleFontScale = Clamp(d.EventTitleFontScale, 0.5, 4.0);
            p.OverlayTimeFontScale  = Clamp(d.OverlayTimeFontScale,  0.5, 4.0);
            p.OverlayTitleFontScale = Clamp(d.OverlayTitleFontScale, 0.5, 4.0);
            p.UiScale           = d.UiScale is 0.75 or 1.0 or 1.25 or 1.5 or 2.0 ? d.UiScale : 1.0;
            p.HotkeysEnabled    = d.HotkeysEnabled;
            p.HotkeyAddTimer    = string.IsNullOrEmpty(d.HotkeyAddTimer) ? "Ctrl+N" : d.HotkeyAddTimer;
            p.HotkeySettings    = string.IsNullOrEmpty(d.HotkeySettings) ? "Alt+S"  : d.HotkeySettings;
            p.HotkeyTheme       = string.IsNullOrEmpty(d.HotkeyTheme)    ? "Alt+T"  : d.HotkeyTheme;
            p.HotkeyHide        = string.IsNullOrEmpty(d.HotkeyHide)     ? "Alt+H"  : d.HotkeyHide;
            p.HotkeyAbout       = string.IsNullOrEmpty(d.HotkeyAbout)    ? "Alt+A"  : d.HotkeyAbout;
            p.DefaultAlertSound = d.DefaultAlertSound;
            p.DefaultAlertStyle = d.DefaultAlertStyle;
            p.DefaultFlashSecs  = d.DefaultFlashSecs;
            p.DefaultFontSize   = d.DefaultFontSize;
            p.DefaultAutoReset  = d.DefaultAutoReset;
            p.CategoryColors    = new Dictionary<string, string>(
                                      d.CategoryColors ?? new Dictionary<string, string>(),
                                      StringComparer.OrdinalIgnoreCase);
            p.Categories        = d.Categories ?? new List<string>();
            p.Templates         = d.Templates   ?? new List<TimerDto>();
            // Calendar
            p.DefaultCalendarPath        = d.DefaultCalendarPath ?? "";
            p.HiddenCalendarPaths        = d.HiddenCalendarPaths ?? new List<string>();
            p.CalendarAutoPullEnabled     = d.CalendarAutoPullEnabled;
            p.CalendarPollIntervalMinutes = d.CalendarPollIntervalMinutes < 1 ? 15 : d.CalendarPollIntervalMinutes;
            p.CalendarLookaheadDays       = d.CalendarLookaheadDays < 1 || d.CalendarLookaheadDays > 365 ? 30 : d.CalendarLookaheadDays;
            p.CalendarExcludeKeywords     = d.CalendarExcludeKeywords ?? new List<string>();
            p.CalendarHideBirthdays       = d.CalendarHideBirthdays;
            p.CalendarHideHolidays        = d.CalendarHideHolidays;
            // Email
            p.EmailAlertEnabled     = d.EmailAlertEnabled;
            p.EmailAlertMode        = Enum.IsDefined((EmailMode)d.EmailAlertMode) ? (EmailMode)d.EmailAlertMode : EmailMode.Disabled;
            p.SmtpHost              = d.SmtpHost    ?? "";
            p.SmtpPort              = d.SmtpPort < 1 || d.SmtpPort > 65535 ? 587 : d.SmtpPort;
            p.SmtpUser              = d.SmtpUser    ?? "";
            p.SmtpPasswordEncrypted = d.SmtpPasswordEncrypted ?? "";
            p.EmailAlertFrom        = d.EmailAlertFrom ?? "";
            p.EmailAlertTo          = d.EmailAlertTo   ?? "";
            p.SmtpSecurity          = ResolveSmtpSecurity(d);
            p.MsOAuthClientId       = d.MsOAuthClientId  ?? "";
            p.MsOAuthTokenCache     = d.MsOAuthTokenCache ?? "";
            p.ThemeAuto             = d.ThemeAuto;
            p.CollapsedCategories   = d.CollapsedCategories ?? new List<string>();
            p.GroupedView           = d.GroupedView;
            p.PillWindowVisible     = d.PillWindowVisible;
            p.LastSortMode          = d.LastSortMode;
            // Calendar event defaults
            p.CalEvtLensPosition            = d.CalEvtLensPosition is >= 0 and <= 8 ? d.CalEvtLensPosition : 8;
            p.CalEvtFlashSeconds            = Math.Max(0, d.CalEvtFlashSeconds);
            p.CalEvtAlertRepeatLimit        = Math.Max(0, d.CalEvtAlertRepeatLimit);
            p.CalEvtEscalateAfterSeconds    = Math.Max(0, d.CalEvtEscalateAfterSeconds);
            p.CalEvtExcludeFromPill         = d.CalEvtExcludeFromPill;
            p.CalEvtReminderEnabled         = d.CalEvtReminderEnabled;
            p.CalEvtReminderLeadMinutes     = d.CalEvtReminderLeadMinutes  < 1 ? 60 : d.CalEvtReminderLeadMinutes;
            p.CalEvtReminderIntervalMinutes = Math.Max(0, d.CalEvtReminderIntervalMinutes);
            p.CalEvtSendEmailOnReminder     = d.CalEvtSendEmailOnReminder;
            p.CalendarLastPulled    = string.IsNullOrEmpty(d.CalendarLastPulled)
                ? DateTime.MinValue
                : (DateTime.TryParse(d.CalendarLastPulled,
                    System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.RoundtripKind,
                    out DateTime lp) ? lp : DateTime.MinValue);
        }

        // ── Shared helpers ────────────────────────────────────────────────────
        internal static string ColorToHex(System.Windows.Media.Color c)
            => $"#{c.R:X2}{c.G:X2}{c.B:X2}";

        internal static System.Windows.Media.Color HexToColor(
            string hex, System.Windows.Media.Color fallback)
        {
            try
            {
                hex = hex.TrimStart('#');
                if (hex.Length == 6)
                    return System.Windows.Media.Color.FromRgb(
                        Convert.ToByte(hex[..2], 16),
                        Convert.ToByte(hex[2..4], 16),
                        Convert.ToByte(hex[4..6], 16));
            }
            catch { }
            return fallback;
        }

        private static int Clamp(int v, int min, int max)
            => v < min ? min : v > max ? max : v;

        private static double Clamp(double v, double min, double max)
            => v < min ? min : v > max ? max : v;
    }
}
