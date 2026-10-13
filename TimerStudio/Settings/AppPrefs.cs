using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows;

namespace TimerStudio
{
    /// <summary>
    /// Window geometry + application preferences persisted to
    /// %AppData%\TimerStudio\prefs.json via System.Text.Json source generation.
    /// </summary>
    public class AppPrefs
    {
        public static readonly AppPrefs Instance = new AppPrefs();

        private static readonly string Dir  =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                         "TimerStudio");
        private static readonly string File = Path.Combine(Dir, "prefs.json");

        // ── Window geometry ───────────────────────────────────────────────────
        public double WindowLeft   { get; set; } = double.NaN;
        public double WindowTop    { get; set; } = double.NaN;
        public double WindowWidth  { get; set; } = 1150;
        public double WindowHeight { get; set; } = 1060;
        public bool   Maximized    { get; set; } = false;

        // ── Theme ─────────────────────────────────────────────────────────────
        public bool   DarkTheme     { get; set; } = true;
        /// <summary>"Dark","Light","Nord","Dracula","Mocha","WarmLight"</summary>
        public string SelectedTheme { get; set; } = "Dark";

        // ── Hotkeys (editable) ────────────────────────────────────────────────
        public string HotkeyAddTimer    { get; set; } = "Ctrl+N";
        public string HotkeySettings    { get; set; } = "Alt+S";
        public string HotkeyTheme       { get; set; } = "Alt+T";
        public string HotkeyHide        { get; set; } = "Alt+H";
        public string HotkeyAbout       { get; set; } = "Alt+A";
        public bool   HotkeysEnabled    { get; set; } = true;

        // ── UI scaling (Section 1 Cycle UI Size) ──────────────────────────────
        /// Valid values: 1.0, 1.25, 1.5, 2.0
        public double UiScale { get; set; } = 1.0;

        // ── Font scaling ──────────────────────────────────────────────────────
        // Countdown / Stopwatch (card) font scales
        public double TimeFontScale  { get; set; } = 1.0;
        public double TitleFontScale { get; set; } = 1.0;
        // DateTime / Event timer (card) font scales
        public double EventTimeFontScale  { get; set; } = 1.0;
        public double EventTitleFontScale { get; set; } = 1.0;
        // Timer Overlay window font scales (independent)
        public double OverlayTimeFontScale  { get; set; } = 1.0;
        public double OverlayTitleFontScale { get; set; } = 1.0;

        // ── Default alert settings — Countdown / Stopwatch (Overlay timers) ──
        public int  DefaultAlertSound { get; set; } = 0;
        public int  DefaultAlertStyle { get; set; } = 0;
        public int  DefaultFlashSecs  { get; set; } = 5;
        public int  DefaultFontSize   { get; set; } = 22;
        public bool DefaultAutoReset  { get; set; } = false;

        // ── Calendar settings ─────────────────────────────────────────────────
        /// <summary>FullPath of the Outlook calendar to use as default for auto-pull and picker.</summary>
        public string DefaultCalendarPath        { get; set; } = "";
        /// <summary>FullPaths of calendars hidden from auto-pull and the picker combo.</summary>
        public List<string> HiddenCalendarPaths  { get; set; } = new List<string>();
        public bool CalendarAutoPullEnabled      { get; set; } = false;
        /// <summary>Minutes between auto-pull polls (1–120). Default 15.</summary>
        public int  CalendarPollIntervalMinutes  { get; set; } = 15;
        /// <summary>Only import events that start within this many days from now (1–365, default 30).</summary>
        public int  CalendarLookaheadDays        { get; set; } = 30;
        /// <summary>
        /// Events whose title contains any of these keywords (case-insensitive) are
        /// skipped during auto-pull. Empty list = no filter.
        /// </summary>
        public List<string> CalendarExcludeKeywords { get; set; } = new List<string>();
        /// <summary>When true, any calendar folder whose name contains "birthday" is skipped.</summary>
        public bool CalendarHideBirthdays { get; set; } = false;
        /// <summary>When true, any calendar folder whose name contains "holiday" is skipped.</summary>
        public bool CalendarHideHolidays  { get; set; } = false;

        // ── Email alert settings ──────────────────────────────────────────────
        public bool   EmailAlertEnabled          { get; set; } = false;
        public EmailMode EmailAlertMode          { get; set; } = EmailMode.Disabled;
        public string SmtpHost                   { get; set; } = "";
        public int    SmtpPort                   { get; set; } = 587;
        public string SmtpUser                   { get; set; } = "";
        /// <summary>DPAPI-encrypted (ProtectedData.CurrentUser scope), base64-encoded SMTP password.</summary>
        public string SmtpPasswordEncrypted      { get; set; } = "";
        public string EmailAlertFrom             { get; set; } = "";
        public string EmailAlertTo               { get; set; } = "";
        /// <summary>TS-03: Auto = SSL/TLS on 465, STARTTLS otherwise. None only when SmtpUser is empty.</summary>
        public SmtpSecurity SmtpSecurity         { get; set; } = SmtpSecurity.Auto;
        // ── Microsoft OAuth2 (mode 3) ─────────────────────────────────────────
        /// <summary>Azure App Application (client) ID — set by developer after portal.azure.com registration.</summary>
        public string MsOAuthClientId            { get; set; } = "";
        /// <summary>MSAL serialized token cache, base64. Cleared on sign-out.</summary>
        public string MsOAuthTokenCache          { get; set; } = "";
        /// <summary>UTC timestamp of the last successful auto-pull. DateTime.MinValue = never.</summary>
        public DateTime CalendarLastPulled       { get; set; } = DateTime.MinValue;

        // ── Feature 6: Auto dark/light theme ─────────────────────────────────
        /// <summary>When true, follow Windows dark/light preference automatically.</summary>
        public bool ThemeAuto                    { get; set; } = false;

        // ── Feature 7: Category group collapse state + grouped view toggle ────
        /// <summary>Category names whose group header is currently collapsed.</summary>
        public List<string> CollapsedCategories  { get; set; } = new List<string>();
        /// <summary>When true, cards are rendered grouped by category with collapsible headers.</summary>
        public bool GroupedView                  { get; set; } = false;

        // ── Feature 11: Compact pill window ───────────────────────────────────
        /// <summary>When true, the pill strip window was open when the app last closed.</summary>
        public bool PillWindowVisible            { get; set; } = false;

        // ── Sort persistence ──────────────────────────────────────────────────
        /// <summary>Last sort applied by the user. -1 = none (manual order). Maps to MainWindow.SortMode enum.</summary>
        public int LastSortMode { get; set; } = -1;

        // ── Calendar event timer defaults ─────────────────────────────────────
        /// <summary>Default popup position for calendar-event timers (maps to LensPosition enum). 8 = BottomRight.</summary>
        public int  CalEvtLensPosition            { get; set; } = 8;
        /// <summary>Default alert auto-dismiss duration in seconds for calendar-event timers. 0 = manual dismiss.</summary>
        public int  CalEvtFlashSeconds            { get; set; } = 5;
        /// <summary>Default max alert repeat plays after the first for calendar-event timers. 0 = unlimited.</summary>
        public int  CalEvtAlertRepeatLimit        { get; set; } = 0;
        /// <summary>Seconds before escalating to fullscreen for calendar-event timers. 0 = never escalate.</summary>
        public int  CalEvtEscalateAfterSeconds    { get; set; } = 0;
        /// <summary>When true, calendar-event timers are hidden from the pill strip by default.</summary>
        public bool CalEvtExcludeFromPill         { get; set; } = false;
        /// <summary>When true, calendar-event timers have early reminders enabled by default.</summary>
        public bool CalEvtReminderEnabled         { get; set; } = false;
        /// <summary>Default minutes before event to start reminding for calendar-event timers.</summary>
        public int  CalEvtReminderLeadMinutes     { get; set; } = 60;
        /// <summary>Default repeat-reminder interval in minutes for calendar-event timers. 0 = once only.</summary>
        public int  CalEvtReminderIntervalMinutes { get; set; } = 0;
        /// <summary>When true, calendar-event timers send email on reminders by default.</summary>
        public bool CalEvtSendEmailOnReminder     { get; set; } = false;

        // ── Categories ────────────────────────────────────────────────────────
        public List<string>               Categories     { get; set; } = new List<string>();
        public List<TimerDto>             Templates      { get; set; } = new List<TimerDto>();
        public Dictionary<string, string> CategoryColors { get; set; } =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // ── Save ──────────────────────────────────────────────────────────────
        public void Save()
        {
            try
            {
                Directory.CreateDirectory(Dir);
                var dto  = DtoMapper.ToDto(this);
                string json = DtoMapper.SerializePrefs(dto);
                string tmp  = File + ".tmp";
                System.IO.File.WriteAllText(tmp, json, Encoding.UTF8);
                System.IO.File.Copy(tmp, File, overwrite: true);
                System.IO.File.Delete(tmp);
            }
            catch (Exception ex) { Debug.WriteLine("[TimerStudio] Prefs save error: " + ex.Message); }
        }

        // ── Load ──────────────────────────────────────────────────────────────
        public void Load()
        {
            if (!System.IO.File.Exists(File)) return;
            try
            {
                string json = System.IO.File.ReadAllText(File, Encoding.UTF8);
                // TS-12: pre-STJ hand-rolled fallback removed — STJ reads PascalCase
                // files case-insensitively, and the fallback could only trigger on
                // WindowWidth < 100.
                var dto = DtoMapper.DeserializePrefs(json);
                DtoMapper.ApplyDto(dto, this);
            }
            catch (Exception ex) { Debug.WriteLine("[TimerStudio] Prefs load error: " + ex.Message); }
        }

        public void ApplyTo(Window w)
        {
            w.Width  = WindowWidth;
            w.Height = WindowHeight;
            if (!double.IsNaN(WindowLeft) && !double.IsNaN(WindowTop))
            {
                double left = Math.Max(SystemParameters.VirtualScreenLeft,
                              Math.Min(WindowLeft,
                                       SystemParameters.VirtualScreenLeft
                                       + SystemParameters.VirtualScreenWidth - 100));
                double top  = Math.Max(SystemParameters.VirtualScreenTop,
                              Math.Min(WindowTop,
                                       SystemParameters.VirtualScreenTop
                                       + SystemParameters.VirtualScreenHeight - 40));
                w.Left = left;
                w.Top  = top;
            }
            else
            {
                w.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            }
            if (Maximized) w.WindowState = WindowState.Maximized;
        }

        public void SnapshotFrom(Window w)
        {
            Maximized = w.WindowState == WindowState.Maximized;
            if (!Maximized)
            {
                WindowLeft   = w.Left;
                WindowTop    = w.Top;
                WindowWidth  = w.Width;
                WindowHeight = w.Height;
            }
        }
    }
}
