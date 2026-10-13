using System;
using System.Globalization;
using System.Text.RegularExpressions;

namespace NoteStation.Core
{
    /// <summary>
    /// Parses natural-language task input into structured fields.
    /// Examples:
    ///   "Buy GPU ram tomorrow 5pm !high"       → Title="Buy GPU ram", Due=tomorrow 17:00, Priority=3
    ///   "Review PR every week P2"              → Title="Review PR", Recurrence=Weekly, Priority=2
    ///   "Call dentist next Friday at 2pm"      → Title="Call dentist", Due=next Friday 14:00
    /// A date phrase that matches but can't be turned into a real date ("in 99999 days",
    /// "Apr 31") is left in the title and reported in <see cref="ParseResult.UnparsedDatePhrase"/>.
    /// </summary>
    public static partial class NlpTaskParser
    {
        public const int MaxDays   = 3650;
        public const int MaxWeeks  = 520;
        public const int MaxMonths = 120;

        public class ParseResult
        {
            public string    CleanTitle  { get; set; } = "";
            public DateTime? DueDate     { get; set; }
            public TimeSpan? DueTime     { get; set; }
            /// <summary>0=None 1=Low 2=Medium 3=High</summary>
            public int       Priority    { get; set; } = 0;
            /// <summary>"None" | "Daily" | "Weekly" | "Monthly"</summary>
            public string    Recurrence  { get; set; } = "None";
            /// <summary>Date phrase recognised but rejected (out of range / invalid). Null when none.</summary>
            public string    UnparsedDatePhrase { get; set; }
        }

        public static ParseResult Parse(string raw) => Parse(raw, DateTime.Today);

        public static ParseResult Parse(string raw, DateTime today)
        {
            var r = new ParseResult();
            string s = raw?.Trim() ?? "";

            s = ExtractPriority(s,   out int       priority);
            s = ExtractRecurrence(s, out string    recurrence);
            s = ExtractTime(s,       out TimeSpan? dueTime);
            s = ExtractDate(s, today.Date, out DateTime? dueDate, out string unparsed);
            r.Priority   = priority;
            r.Recurrence = recurrence ?? "None";
            r.DueTime    = dueTime;
            r.DueDate    = dueDate;
            r.UnparsedDatePhrase = unparsed;

            r.CleanTitle = MultiSpaceRx().Replace(s.Trim(), " ").Trim();
            return r;
        }

        // ── Priority ─────────────────────────────────────────────────────────
        private static string ExtractPriority(string s, out int priority)
        {
            priority = 0;

            var m = PriorityPRx().Match(s);
            if (m.Success)
            {
                int p = m.Groups[1].Value[0] - '0';
                priority = p == 1 ? 3 : p == 2 ? 2 : p == 3 ? 1 : 0;
                return s.Remove(m.Index, m.Length);
            }

            m = PriorityBangRx().Match(s);
            if (m.Success)
            {
                string kw = m.Groups[1].Value.ToLowerInvariant();
                priority = kw is "urgent" or "critical" or "high" ? 3
                         : kw == "medium" ? 2
                         : kw == "low"    ? 1 : 0;
                return s.Remove(m.Index, m.Length);
            }

            m = PriorityWordRx().Match(s);
            if (m.Success)
            {
                priority = 3;
                return s.Remove(m.Index, m.Length);
            }

            return s;
        }

        // ── Recurrence ────────────────────────────────────────────────────────
        private static string ExtractRecurrence(string s, out string recurrence)
        {
            recurrence = "None";
            var m = RecurrenceRx().Match(s);
            if (!m.Success) return s;
            string kw = WhitespaceRx().Replace(m.Groups[1].Value.ToLowerInvariant(), " ");
            recurrence = kw is "every day" or "daily"        ? "Daily"
                       : kw is "every week" or "weekly"      ? "Weekly"
                       : kw is "every month" or "monthly"    ? "Monthly" : "None";
            return s.Remove(m.Index, m.Length);
        }

        // ── Time ──────────────────────────────────────────────────────────────
        private static string ExtractTime(string s, out TimeSpan? time)
        {
            time = null;

            var m = Time12Rx().Match(s);
            if (m.Success)
            {
                int hour = ParseSmall(m.Groups[1].Value);
                int min  = m.Groups[2].Success ? ParseSmall(m.Groups[2].Value) : 0;
                bool pm  = m.Groups[3].Value.Equals("pm", StringComparison.OrdinalIgnoreCase);
                if (pm && hour < 12) hour += 12;
                if (!pm && hour == 12) hour = 0;
                if (hour < 24 && min < 60) time = new TimeSpan(hour, min, 0);
                return s.Remove(m.Index, m.Length).Trim();
            }

            m = Time24Rx().Match(s);
            if (m.Success)
            {
                int hour = ParseSmall(m.Groups[1].Value);
                int min  = ParseSmall(m.Groups[2].Value);
                if (hour < 24 && min < 60) time = new TimeSpan(hour, min, 0);
                return s.Remove(m.Index, m.Length).Trim();
            }

            return s;
        }

        // ── Date ──────────────────────────────────────────────────────────────
        private static string ExtractDate(string s, DateTime today, out DateTime? date, out string unparsed)
        {
            date = null;
            unparsed = null;

            var m = TodayRx().Match(s);
            if (m.Success) { date = today; return s.Remove(m.Index, m.Length).Trim(); }

            m = TomorrowRx().Match(s);
            if (m.Success) { date = today.AddDays(1); return s.Remove(m.Index, m.Length).Trim(); }

            m = NextThisRx().Match(s);
            if (m.Success)
            {
                string qual = m.Groups[1].Value.ToLowerInvariant();
                string kw   = m.Groups[2].Value.ToLowerInvariant();
                if      (kw == "week")  date = today.AddDays(7);
                else if (kw == "month") date = today.AddMonths(1);
                else
                {
                    var dow = ToDayOfWeek(kw);
                    if (dow.HasValue)
                    {
                        int diff = ((int)dow.Value - (int)today.DayOfWeek + 7) % 7;
                        if (diff == 0 || qual == "next") diff += 7;
                        date = today.AddDays(diff);
                    }
                }
                return s.Remove(m.Index, m.Length).Trim();
            }

            m = BareDowRx().Match(s);
            if (m.Success)
            {
                var dow = ToDayOfWeek(m.Groups[1].Value.ToLowerInvariant());
                if (dow.HasValue)
                {
                    int diff = ((int)dow.Value - (int)today.DayOfWeek + 7) % 7;
                    if (diff == 0) diff = 7;
                    date = today.AddDays(diff);
                }
                return s.Remove(m.Index, m.Length).Trim();
            }

            // "in 3 days" / "in 2 weeks" / "in 6 months"
            m = InNRx().Match(s);
            if (m.Success)
            {
                string u = m.Groups[2].Value.ToLowerInvariant().TrimEnd('s');
                if (TryOffset(today, m.Groups[1].Value, u, out var d))
                {
                    date = d;
                    return s.Remove(m.Index, m.Length).Trim();
                }
                unparsed = m.Value.Trim();
                return s;
            }

            // "April 5" / "Apr 5"
            m = MonthDayRx().Match(s);
            if (m.Success)
            {
                if (TryBuildDate(today, ToMonthNumber(m.Groups[1].Value), ParseSmall(m.Groups[2].Value), out date))
                    return s.Remove(m.Index, m.Length).Trim();
                unparsed = m.Value.Trim();
                return s;
            }

            // "5 April"
            m = DayMonthRx().Match(s);
            if (m.Success)
            {
                if (TryBuildDate(today, ToMonthNumber(m.Groups[2].Value), ParseSmall(m.Groups[1].Value), out date))
                    return s.Remove(m.Index, m.Length).Trim();
                unparsed = m.Value.Trim();
                return s;
            }

            return s;
        }

        internal static bool TryOffset(DateTime today, string digits, string unit, out DateTime result)
        {
            result = default;
            if (digits.Length > 4 || !int.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out int n))
                return false;
            int max = unit switch { "day" => MaxDays, "week" => MaxWeeks, "month" => MaxMonths, _ => -1 };
            if (n < 0 || n > max) return false;
            try
            {
                result = unit switch
                {
                    "day"  => today.AddDays(n),
                    "week" => today.AddDays(n * 7),
                    _      => today.AddMonths(n),
                };
                return true;
            }
            catch (ArgumentOutOfRangeException) { return false; }
        }

        private static bool TryBuildDate(DateTime today, int month, int day, out DateTime? result)
        {
            result = null;
            if (month < 1 || month > 12 || day < 1) return false;
            int year = (today.Month > month || (today.Month == month && today.Day > day))
                     ? today.Year + 1 : today.Year;
            if (day > DateTime.DaysInMonth(year, month)) return false;
            result = new DateTime(year, month, day);
            return true;
        }

        private static int ParseSmall(string digits) =>
            int.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out int v) ? v : -1;

        private static DayOfWeek? ToDayOfWeek(string s) => s switch
        {
            "monday"    => DayOfWeek.Monday,
            "tuesday"   => DayOfWeek.Tuesday,
            "wednesday" => DayOfWeek.Wednesday,
            "thursday"  => DayOfWeek.Thursday,
            "friday"    => DayOfWeek.Friday,
            "saturday"  => DayOfWeek.Saturday,
            "sunday"    => DayOfWeek.Sunday,
            _           => (DayOfWeek?)null
        };

        private static int ToMonthNumber(string s)
        {
            s = s.ToLowerInvariant();
            string[] abbr = { "", "jan", "feb", "mar", "apr", "may", "jun", "jul", "aug", "sep", "oct", "nov", "dec" };
            for (int i = 1; i <= 12; i++)
                if (s.StartsWith(abbr[i], StringComparison.Ordinal)) return i;
            return 0;
        }

        private const RegexOptions Ci = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;
        private const string MP = @"(?:jan(?:uary)?|feb(?:ruary)?|mar(?:ch)?|apr(?:il)?|may|jun(?:e)?|jul(?:y)?|aug(?:ust)?|sep(?:tember)?|oct(?:ober)?|nov(?:ember)?|dec(?:ember)?)";

        [GeneratedRegex(@"\s{2,}")] private static partial Regex MultiSpaceRx();
        [GeneratedRegex(@"\s+")] private static partial Regex WhitespaceRx();
        [GeneratedRegex(@"\bP([1-4])\b", Ci)] private static partial Regex PriorityPRx();
        [GeneratedRegex(@"\!(urgent|critical|high|medium|low|normal)\b", Ci)] private static partial Regex PriorityBangRx();
        [GeneratedRegex(@"\b(urgent|critical|high\s+priority|high-priority)\b", Ci)] private static partial Regex PriorityWordRx();
        [GeneratedRegex(@"\b(every\s+day|daily|every\s+week|weekly|every\s+month|monthly)\b", Ci)] private static partial Regex RecurrenceRx();
        [GeneratedRegex(@"\b(\d{1,2})(?::(\d{2}))?\s*(am|pm)\b", Ci)] private static partial Regex Time12Rx();
        [GeneratedRegex(@"\bat\s+(\d{1,2}):(\d{2})\b", Ci)] private static partial Regex Time24Rx();
        [GeneratedRegex(@"\b(today|tonight)\b", Ci)] private static partial Regex TodayRx();
        [GeneratedRegex(@"\btomorrow\b", Ci)] private static partial Regex TomorrowRx();
        [GeneratedRegex(@"\b(next|this)\s+(monday|tuesday|wednesday|thursday|friday|saturday|sunday|week|month)\b", Ci)] private static partial Regex NextThisRx();
        [GeneratedRegex(@"\b(monday|tuesday|wednesday|thursday|friday|saturday|sunday)\b", Ci)] private static partial Regex BareDowRx();
        [GeneratedRegex(@"\bin\s+(\d+)\s+(days?|weeks?|months?)\b", Ci)] private static partial Regex InNRx();
        [GeneratedRegex(@"\b(" + MP + @")\s+(\d{1,2})\b", Ci)] private static partial Regex MonthDayRx();
        [GeneratedRegex(@"\b(\d{1,2})\s+(" + MP + @")\b", Ci)] private static partial Regex DayMonthRx();
    }
}
