using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

#nullable enable


namespace FileCopy
{
    public enum ScheduleFrequency
    {
        Once,           // run once at the specified DateTime
        EveryMinutes,   // repeat every N minutes
        EveryHours,     // repeat every N hours
        Daily,          // run once per day at a fixed time
        Weekdays,       // Mon–Fri at a fixed time
        Weekly          // specific day of week at a fixed time
    }

    public sealed class SchedulerJob
    {
        public string        Id          { get; set; } = Guid.NewGuid().ToString("N")[..8];
        public string        Label       { get; set; } = "New job";
        public bool          IsEnabled   { get; set; } = true;

        // Paths (override app prefs when set)
        public string        SourcePath  { get; set; } = "";
        public string        DestPath    { get; set; } = "";

        // Schedule
        public ScheduleFrequency Frequency { get; set; } = ScheduleFrequency.Daily;
        public int           IntervalValue { get; set; } = 1;   // N for EveryMinutes/EveryHours
        public TimeSpan      TimeOfDay   { get; set; } = new TimeSpan(2, 0, 0); // 02:00 default
        public DayOfWeek     WeekDay     { get; set; } = DayOfWeek.Monday;      // for Weekly
        public DateTime      RunOnce     { get; set; } = DateTime.Now.AddHours(1);

        // State
        public DateTime?     LastRun     { get; set; }
        public DateTime?     NextRun     { get; set; }
        public string        LastResult  { get; set; } = string.Empty;
        public List<string>  RunHistory  { get; set; } = new List<string>(); // last 5 "yyyy-MM-dd HH:mm | result" entries

        // Copy options override (null = inherit from AppPrefs)
        public bool?         VerifyCrc            { get; set; }
        public bool?         DeleteSourceOnMove   { get; set; }
        public ConflictMode? ConflictMode         { get; set; }
        public string        FileMaskInclude      { get; set; } = "*.*";
        public string?       FileMaskExclude      { get; set; }
        public bool?         IncludeSubdirs       { get; set; }

        /// <summary>Compute the next run time from now.</summary>
        public DateTime ComputeNextRun(DateTime from)
        {
            return Frequency switch
            {
                ScheduleFrequency.Once =>
                    RunOnce > from ? RunOnce : DateTime.MaxValue,

                ScheduleFrequency.EveryMinutes =>
                    from.AddMinutes(IntervalValue),

                ScheduleFrequency.EveryHours =>
                    from.AddHours(IntervalValue),

                ScheduleFrequency.Daily =>
                    NextDailyOccurrence(from, TimeOfDay),

                ScheduleFrequency.Weekdays =>
                    NextWeekdayOccurrence(from, TimeOfDay),

                ScheduleFrequency.Weekly =>
                    NextWeeklyOccurrence(from, WeekDay, TimeOfDay),

                _ => DateTime.MaxValue
            };
        }

        private static DateTime NextDailyOccurrence(DateTime from, TimeSpan tod)
        {
            var candidate = from.Date.Add(tod);
            return candidate > from ? candidate : candidate.AddDays(1);
        }

        private static DateTime NextWeekdayOccurrence(DateTime from, TimeSpan tod)
        {
            var d = from;
            for (int i = 0; i < 8; i++)
            {
                var candidate = d.Date.Add(tod);
                if (candidate > from &&
                    d.DayOfWeek != DayOfWeek.Saturday &&
                    d.DayOfWeek != DayOfWeek.Sunday)
                    return candidate;
                d = d.AddDays(1);
            }
            return DateTime.MaxValue;
        }

        private static DateTime NextWeeklyOccurrence(DateTime from, DayOfWeek dow, TimeSpan tod)
        {
            var d = from;
            for (int i = 0; i < 8; i++)
            {
                var candidate = d.Date.Add(tod);
                if (candidate > from && d.DayOfWeek == dow)
                    return candidate;
                d = d.AddDays(1);
            }
            return DateTime.MaxValue;
        }

        public string FrequencyDisplay => Frequency switch
        {
            ScheduleFrequency.Once          => $"Once at {RunOnce:yyyy-MM-dd HH:mm}",
            ScheduleFrequency.EveryMinutes  => $"Every {IntervalValue} min",
            ScheduleFrequency.EveryHours    => $"Every {IntervalValue} hr",
            ScheduleFrequency.Daily         => $"Daily at {TimeOfDay:hh\\:mm}",
            ScheduleFrequency.Weekdays      => $"Weekdays at {TimeOfDay:hh\\:mm}",
            ScheduleFrequency.Weekly        => $"{WeekDay}s at {TimeOfDay:hh\\:mm}",
            _                               => "?"
        };

        /// <summary>Path to this job's per-run log file.</summary>
        [System.Text.Json.Serialization.JsonIgnore]
        public string JobLogPath =>
            System.IO.Path.Combine(PortableMode.LogsDir, $"scheduler-job-{Id}.log");
    }

    [JsonSerializable(typeof(List<SchedulerJob>))]
    [JsonSerializable(typeof(SchedulerJob))]
    [JsonSerializable(typeof(List<string>))]
    [JsonSourceGenerationOptions(WriteIndented = true)]
    internal partial class SchedulerJobContext : System.Text.Json.Serialization.JsonSerializerContext { }
}
