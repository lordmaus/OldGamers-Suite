using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows.Input;

#nullable enable


namespace FileCopy
{
    /// <summary>
    /// ViewModel for SchedulerWindow. All UI state lives here; the XAML binds
    /// to properties on this class. No code-behind logic except event wiring.
    /// </summary>
    public sealed class SchedulerViewModel : INotifyPropertyChanged
    {
        private readonly SchedulerEngine _engine;

        // ── Observable job list ───────────────────────────────────────────────
        public ObservableCollection<SchedulerJobViewModel> Jobs { get; } = new();

        private SchedulerJobViewModel? _selectedJob;
        public SchedulerJobViewModel? SelectedJob
        {
            get => _selectedJob;
            set
            {
                if (_selectedJob == value) return;
                _selectedJob = value;
                Notify();
                Notify(nameof(HasSelection));
                Notify(nameof(HasJobLog));
                if (value != null) BeginEdit(value.Job!);
                else               ClearEdit();
            }
        }

        public bool HasSelection  => SelectedJob != null;
        public bool HasJobLog     => SelectedJob != null
                                  && File.Exists(SelectedJob.Job.JobLogPath);

        // ── Edit form state ───────────────────────────────────────────────────
        private string _editLabel      = "";
        private string _editSource     = "";
        private string _editDest       = "";
        private bool   _editEnabled    = true;
        private int    _editFreqIndex  = 3;  // Daily
        private string _editInterval   = "30";
        private string _editTime       = "02:00";
        private int    _editDayIndex   = 0;  // Monday
        private string _editOnceDate   = "";
        private string _editOnceTime   = "";
        private bool?  _editVerify     = null;
        private bool?  _editMove       = null;
        private int    _editConflict   = 1;  // OverwriteIfNewer
        private string _editInclude    = "*.*";
        private string _editExclude    = "";
        private bool?  _editSubdirs    = true;
        private string _nextRunDisplay = "—";
        private string _lastRunDisplay = "—";
        private SchedulerJob _editingJob; // null = new

        public string EditLabel     { get => _editLabel;     set { _editLabel     = value; Notify(); } }
        public string EditSource    { get => _editSource;    set { _editSource    = value; Notify(); } }
        public string EditDest      { get => _editDest;      set { _editDest      = value; Notify(); } }
        public bool   EditEnabled   { get => _editEnabled;   set { _editEnabled   = value; Notify(); } }
        public int    EditFreqIndex { get => _editFreqIndex; set { _editFreqIndex = value; Notify(); Notify(nameof(ShowInterval)); Notify(nameof(ShowTime)); Notify(nameof(ShowDay)); Notify(nameof(ShowOnce)); } }
        public string EditInterval  { get => _editInterval;  set { _editInterval  = value; Notify(); } }
        public string EditTime      { get => _editTime;      set { _editTime      = value; Notify(); } }
        public int    EditDayIndex  { get => _editDayIndex;  set { _editDayIndex  = value; Notify(); } }
        public string EditOnceDate  { get => _editOnceDate;  set { _editOnceDate  = value; Notify(); } }
        public string EditOnceTime  { get => _editOnceTime;  set { _editOnceTime  = value; Notify(); } }
        public bool?  EditVerify    { get => _editVerify;    set { _editVerify    = value; Notify(); } }
        public bool?  EditMove      { get => _editMove;      set { _editMove      = value; Notify(); if (value == true) EditVerify = true; } }
        public int    EditConflict  { get => _editConflict;  set { _editConflict  = value; Notify(); } }
        public string EditInclude   { get => _editInclude;   set { _editInclude   = value; Notify(); } }
        public string EditExclude   { get => _editExclude;   set { _editExclude   = value; Notify(); } }
        public bool?  EditSubdirs   { get => _editSubdirs;   set { _editSubdirs   = value; Notify(); } }
        public string NextRunDisplay{ get => _nextRunDisplay; set { _nextRunDisplay = value; Notify(); } }
        public string LastRunDisplay{ get => _lastRunDisplay; set { _lastRunDisplay = value; Notify(); } }

        // Frequency-dependent panel visibility
        public bool ShowInterval => _editFreqIndex is 1 or 2; // EveryMinutes, EveryHours
        public bool ShowTime     => _editFreqIndex is 3 or 4 or 5; // Daily, Weekdays, Weekly
        public bool ShowDay      => _editFreqIndex == 5; // Weekly
        public bool ShowOnce     => _editFreqIndex == 0; // Once

        // ── Static combo sources ──────────────────────────────────────────────
        public static IReadOnlyList<string> FrequencyItems { get; } = new[]
        {
            "Once",
            "Every N minutes",
            "Every N hours",
            "Daily",
            "Weekdays (Mon–Fri)",
            "Weekly"
        };

        // Static aliases for XAML binding
        public static IReadOnlyList<string> FrequencyItemsStatic => FrequencyItems;
        public static IReadOnlyList<string> DayItemsStatic       => DayItems;
        public static IReadOnlyList<string> ConflictItemsStatic  => ConflictItems;

        // Expose for combo binding via DataContext
        public IReadOnlyList<string> FrequencyItemsSrc => FrequencyItems;
        public IReadOnlyList<string> DayItemsSrc       => DayItems;
        public IReadOnlyList<string> ConflictItemsSrc  => ConflictItems;

        public bool HasJobs => Jobs.Count > 0;

        public static IReadOnlyList<string> DayItems { get; } = new[]
        {
            "Monday","Tuesday","Wednesday","Thursday","Friday","Saturday","Sunday"
        };

        public static IReadOnlyList<string> ConflictItems { get; } = new[]
        {
            "Overwrite always",
            "Overwrite if newer (safe default)",
            "Overwrite if older",
            "Skip if same date",
            "Skip all",
            "Rename (keep both)"
        };

        // ── Commands ─────────────────────────────────────────────────────────
        public ICommand AddCommand        { get; }
        public ICommand EditCommand       { get; }
        public ICommand DeleteCommand     { get; }
        public ICommand RunNowCommand     { get; }
        public ICommand ExportCommand     { get; }
        public ICommand ImportCommand     { get; }
        public ICommand SaveJobCommand    { get; }
        public ICommand CancelEditCommand { get; }
        public ICommand ViewJobLogCommand { get; }
        public ICommand ClearJobLogCommand{ get; }
        public ICommand ViewActivityLogCommand { get; }

        // Raised when UI should show a folder-pick dialog
        public event Action<bool>? PickFolderRequested; // true=source, false=dest
        public event Action<string, string>? ShowLogRequested; // title, path
        public event Action<string>         ShowMessageRequested;
        public event Action                 CloseRequested;

        public SchedulerViewModel(SchedulerEngine engine)
        {
            _engine = engine;

            AddCommand         = new RelayCommand(_ => BeginAdd());
            EditCommand        = new RelayCommand(_ => BeginEdit(SelectedJob?.Job), _ => HasSelection);
            DeleteCommand      = new RelayCommand(_ => DeleteSelected(),            _ => HasSelection);
            RunNowCommand      = new RelayCommand(_ => RunNow(),                    _ => HasSelection);
            ExportCommand      = new RelayCommand(_ => Export(),                    _ => Jobs.Count > 0);
            ImportCommand      = new RelayCommand(_ => Import());
            SaveJobCommand     = new RelayCommand(_ => SaveJob());
            CancelEditCommand  = new RelayCommand(_ => ClearEdit());
            ViewJobLogCommand  = new RelayCommand(_ => ViewJobLog(),  _ => HasJobLog);
            ClearJobLogCommand = new RelayCommand(_ => ClearJobLog(), _ => HasJobLog);
            ViewActivityLogCommand = new RelayCommand(_ => ViewActivityLog());

            RefreshJobs();
        }

        // ── Job list ─────────────────────────────────────────────────────────
        public void RefreshJobs()
        {
            Jobs.Clear();
            foreach (var j in _engine.Jobs)
                Jobs.Add(new SchedulerJobViewModel(j));
            Notify(nameof(HasJobs));
            CommandManager.InvalidateRequerySuggested();
        }

        // ── Edit form ─────────────────────────────────────────────────────────
        private void BeginAdd()
        {
            SelectedJob   = null;
            _editingJob   = null!;  // intentional clear
            ClearEdit();
        }

        private void BeginEdit(SchedulerJob? job)
        {
            if (job == null) return;
            _editingJob      = job;
            EditLabel        = job.Label;
            EditSource       = job.SourcePath;
            EditDest         = job.DestPath;
            EditEnabled      = job.IsEnabled;
            EditFreqIndex    = (int)job.Frequency;
            EditInterval     = job.IntervalValue.ToString();
            EditTime         = job.TimeOfDay.ToString(@"hh\:mm");
            EditDayIndex     = (int)job.WeekDay;
            EditOnceDate     = job.RunOnce.ToString("yyyy-MM-dd");
            EditOnceTime     = job.RunOnce.ToString("HH:mm");
            EditVerify       = job.VerifyCrc;
            EditMove         = job.DeleteSourceOnMove;
            EditConflict     = job.ConflictMode.HasValue ? (int)job.ConflictMode.Value : 1;
            EditInclude      = string.IsNullOrWhiteSpace(job.FileMaskInclude) ? "*.*" : job.FileMaskInclude;
            EditExclude      = job.FileMaskExclude ?? "";
            EditSubdirs      = job.IncludeSubdirs ?? true;
            NextRunDisplay   = job.NextRun.HasValue ? job.NextRun.Value.ToString("yyyy-MM-dd HH:mm") : "—";
            LastRunDisplay   = job.LastRun.HasValue
                ? job.LastRun.Value.ToString("yyyy-MM-dd HH:mm")
                  + (string.IsNullOrEmpty(job.LastResult) ? "" : $"  ({job.LastResult})")
                : "Never";
            Notify(nameof(HasJobLog));
        }

        private void ClearEdit()
        {
            _editingJob   = null!;  // intentional clear
            EditLabel     = "";
            EditSource    = "";
            EditDest      = "";
            EditEnabled   = true;
            EditFreqIndex = 3;
            EditInterval  = "30";
            EditTime      = "02:00";
            EditDayIndex  = 0;
            EditOnceDate  = DateTime.Now.AddHours(1).ToString("yyyy-MM-dd");
            EditOnceTime  = DateTime.Now.AddHours(1).ToString("HH:mm");
            EditVerify    = null;
            EditMove      = null;
            EditConflict  = 1;
            EditInclude   = "*.*";
            EditExclude   = "";
            EditSubdirs   = true;
            NextRunDisplay = "—";
            LastRunDisplay = "—";
            Notify(nameof(HasJobLog));
        }

        private void SaveJob()
        {
            if (string.IsNullOrWhiteSpace(EditLabel))
            { ShowMessageRequested?.Invoke("Name is required."); return; }
            if (string.IsNullOrWhiteSpace(EditSource) || string.IsNullOrWhiteSpace(EditDest))
            { ShowMessageRequested?.Invoke("Source and destination are required."); return; }

            var job = _editingJob ?? new SchedulerJob();
            job.Label            = EditLabel.Trim();
            job.SourcePath       = EditSource.Trim();
            job.DestPath         = EditDest.Trim();
            job.IsEnabled        = EditEnabled;
            job.Frequency        = (ScheduleFrequency)EditFreqIndex;

            if (int.TryParse(EditInterval, out int iv) && iv >= 1) job.IntervalValue = iv;
            else if (job.Frequency is ScheduleFrequency.EveryMinutes or ScheduleFrequency.EveryHours)
            { ShowMessageRequested?.Invoke("Interval must be a whole number ≥ 1."); return; }

            if (TimeSpan.TryParse(EditTime, out var tod)) job.TimeOfDay = tod;
            job.WeekDay        = (DayOfWeek)EditDayIndex;
            if (DateTime.TryParse($"{EditOnceDate} {EditOnceTime}", out var once)) job.RunOnce = once;

            job.VerifyCrc          = EditVerify;
            job.DeleteSourceOnMove = EditMove;
            job.ConflictMode       = (ConflictMode)EditConflict;
            job.FileMaskInclude    = string.IsNullOrWhiteSpace(EditInclude) ? "*.*" : EditInclude.Trim();
            job.FileMaskExclude    = EditExclude?.Trim() ?? "";
            job.IncludeSubdirs     = EditSubdirs;
            job.NextRun            = job.IsEnabled ? job.ComputeNextRun(DateTime.Now) : null;

            // FC-01: scheduled Move runs unattended — confirm once when the job is saved.
            if (EditMove == true && ConfirmMoveRequested != null &&
                !ConfirmMoveRequested($"\"{job.Label}\" deletes files from {job.SourcePath} after each verified copy to {job.DestPath}.\n\n" +
                                      "It runs unattended — you won't be asked again when it runs.\n\nSave this job?"))
                return;

            _engine.AddOrUpdate(job);
            RefreshJobs();
            // Re-select the saved job
            SelectedJob = Jobs.FirstOrDefault(x => x.Job.Id == job.Id);
        }

        // ── Toolbar actions ───────────────────────────────────────────────────
        private void DeleteSelected()
        {
            if (SelectedJob == null) return;
            var job = SelectedJob.Job;
            // Confirmation handled in code-behind via ShowMessageRequested isn't ideal;
            // use a dedicated event
            DeleteConfirmRequested?.Invoke(job);
        }

        public event Action<SchedulerJob>? DeleteConfirmRequested;
        /// <summary>Returns true to continue saving a Move job (FC-01).</summary>
        public Func<string, bool>? ConfirmMoveRequested;

        public void FireJob(string id)       => _engine.FireNow(id);
        public void AddImportedJob(SchedulerJob job) => _engine.AddOrUpdate(job);

        public void ConfirmDelete(SchedulerJob job)
        {
            _engine.Remove(job.Id);
            RefreshJobs();
            SelectedJob = null;
            ClearEdit();
        }

        private void RunNow()
        {
            if (SelectedJob == null) return;
            var job = SelectedJob.Job;
            if (string.IsNullOrWhiteSpace(job.SourcePath) || string.IsNullOrWhiteSpace(job.DestPath))
            { ShowMessageRequested?.Invoke("Job has no source or destination set."); return; }
            RunNowRequested?.Invoke(job);
        }

        public event Action<SchedulerJob>? RunNowRequested;

        private void Export()
        {
            ExportRequested?.Invoke();
        }
        public event Action? ExportRequested;

        private void Import()
        {
            ImportRequested?.Invoke();
        }
        public event Action? ImportRequested;

        public void PickSourceFolder() => PickFolderRequested?.Invoke(true);
        public void PickDestFolder()   => PickFolderRequested?.Invoke(false);

        private void ViewJobLog()
        {
            if (SelectedJob == null || !File.Exists(SelectedJob.Job.JobLogPath)) return;
            ShowLogRequested?.Invoke($"Job log — {SelectedJob.Job.Label}", SelectedJob.Job.JobLogPath);
        }

        private void ClearJobLog()
        {
            if (SelectedJob == null) return;
            ClearJobLogRequested?.Invoke(SelectedJob.Job);
        }
        public event Action<SchedulerJob>? ClearJobLogRequested;

        private void ViewActivityLog()
        {
            ViewActivityLogRequested?.Invoke();
        }
        public event Action? ViewActivityLogRequested;

        // ── INotifyPropertyChanged ─────────────────────────────────────────────
        public event PropertyChangedEventHandler? PropertyChanged;
        private void Notify([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    /// <summary>Thin wrapper around SchedulerJob for the ListBox ItemsSource.</summary>
    public sealed class SchedulerJobViewModel : INotifyPropertyChanged
    {
        public SchedulerJob Job { get; }

        public SchedulerJobViewModel(SchedulerJob job) { Job = job; }

        public string Label           => Job.Label;
        public string FrequencyDisplay=> Job.FrequencyDisplay;
        public bool   IsEnabled       => Job.IsEnabled;
        public string EnabledGlyph    => Job.IsEnabled ? "●" : "○";
        public string NextRunDisplay  => Job.NextRun.HasValue
            ? "Next: " + Job.NextRun.Value.ToString("yyyy-MM-dd HH:mm") : "";
        public bool   HasLog          => File.Exists(Job.JobLogPath);
        public IReadOnlyList<string> RecentHistory => Job.RunHistory.Take(3).ToList();
        public string AutomationName  =>
            $"{Job.Label}, {Job.FrequencyDisplay}, {(Job.IsEnabled ? "enabled" : "disabled")}";

        public event PropertyChangedEventHandler? PropertyChanged;
    }
}
