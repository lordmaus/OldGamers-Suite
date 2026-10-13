using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;

namespace NoteStation
{
    /// <summary>A single checklist item within a task.</summary>
    public class SubTaskItem : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;
        private void OnPropertyChanged(string name) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        private string _text = "";
        public string Text
        {
            get => _text;
            set { if (_text != value) { _text = value; OnPropertyChanged(nameof(Text)); } }
        }

        private bool _isDone;
        public bool IsDone
        {
            get => _isDone;
            set { if (_isDone != value) { _isDone = value; OnPropertyChanged(nameof(IsDone)); } }
        }
    }

    /// <summary>
    /// A single task entry with drag-sortable order, date/time, and
    /// optional TimerStudio push capability.
    /// </summary>
    public class TaskItem : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([System.Runtime.CompilerServices.CallerMemberName] string name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        public int    Id          { get; set; }
        public int    SortOrder   { get; set; }

        private string _title = "";
        public string Title
        {
            get => _title;
            set { if (_title != value) { _title = value; OnPropertyChanged(); } }
        }

        public string Notes       { get; set; } = "";

        // ── Subtasks ──────────────────────────────────────────────────────────
        public List<SubTaskItem> SubTasks { get; set; } = new List<SubTaskItem>();
        public bool   HasSubTasks    => SubTasks != null && SubTasks.Count > 0;
        public double SubTaskProgress => HasSubTasks ? (double)SubTasks.Count(s => s.IsDone) / SubTasks.Count : 0;
        public string SubTaskSummary  => HasSubTasks ? $"{SubTasks.Count(s => s.IsDone)}/{SubTasks.Count}" : "";

        // ── Status ────────────────────────────────────────────────────────────
        private bool _isCompleted;
        public bool IsCompleted
        {
            get => _isCompleted;
            set { if (_isCompleted != value) { _isCompleted = value; OnPropertyChanged(); } }
        }

        private bool _isStarred;
        public bool IsStarred
        {
            get => _isStarred;
            set { if (_isStarred != value) { _isStarred = value; OnPropertyChanged(); } }
        }

        // ── Date/time ─────────────────────────────────────────────────────────
        private bool _hasDueDate;
        public bool HasDueDate
        {
            get => _hasDueDate;
            set { if (_hasDueDate != value) { _hasDueDate = value; OnPropertyChanged(); } }
        }

        private DateTime? _dueDate;
        public DateTime? DueDate
        {
            get => _dueDate;
            set { if (_dueDate != value) { _dueDate = value; OnPropertyChanged(); } }
        }

        public bool      HasDueTime  { get; set; }
        public TimeSpan? DueTime     { get; set; }

        // ── TimerStudio integration ───────────────────────────────────────────
        /// <summary>Whether a countdown has been pushed to TimerStudio for this task.</summary>
        public bool   PushedToTimer { get; set; }
        /// <summary>Duration in minutes that was pushed to TimerStudio.</summary>
        public int    TimerMinutes  { get; set; }

        // ── Priority ──────────────────────────────────────────────────────────
        /// <summary>0=None 1=Low 2=Medium 3=High</summary>
        private int _priority = 0;
        public int Priority
        {
            get => _priority;
            set { if (_priority != value) { _priority = value; OnPropertyChanged(); } }
        }

        // ── Priority rank ─────────────────────────────────────────────────────
        /// <summary>Independent manual ranking number (1 = top). 0 = unranked.
        /// Distinct from <see cref="Priority"/> (None/Low/Medium/High urgency) —
        /// this is purely a user-assigned ordering number.</summary>
        private int _priorityRank = 0;
        public int PriorityRank
        {
            get => _priorityRank;
            set { if (_priorityRank != value) { _priorityRank = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasPriorityRank)); } }
        }

        /// <summary>True when a manual rank number has been assigned — used by DataTemplate BoolToVis.</summary>
        public bool HasPriorityRank => PriorityRank > 0;

        // ── Recurrence ────────────────────────────────────────────────────────
        /// <summary>"None" | "Daily" | "Weekly" | "Monthly"</summary>
        public string RecurrenceRule { get; set; } = "None";

        // ── Habit tracker ─────────────────────────────────────────────────────
        /// <summary>True = this is a habit task with streak tracking.</summary>
        public bool IsHabit { get; set; } = false;

        /// <summary>Consecutive days the habit has been completed.</summary>
        public int HabitStreak { get; set; } = 0;

        /// <summary>UTC date the habit was last marked done. Used for streak calculation.</summary>
        public DateTime? LastHabitCompleted { get; set; } = null;

        // ── Clip linking ───────────────────────────────────────────────────────
        /// <summary>IDs of ClipboardItems linked to this task.</summary>
        public List<int> LinkedClipIds { get; set; } = new List<int>();

        // ── Task relations ─────────────────────────────────────────────────────
        /// <summary>IDs of tasks this task depends on (must complete before this one).</summary>
        public List<int> DependsOnIds { get; set; } = new List<int>();
        /// <summary>IDs of tasks this task is blocking.</summary>
        public List<int> BlocksIds    { get; set; } = new List<int>();
        /// <summary>Runtime: true when any DependsOn task is still incomplete. Not persisted.</summary>
        [System.Text.Json.Serialization.JsonIgnore]
        public bool IsBlocked { get; set; } = false;

        // ── Archive ───────────────────────────────────────────────────────────
        private bool _isArchived;
        public bool IsArchived
        {
            get => _isArchived;
            set { if (_isArchived != value) { _isArchived = value; OnPropertyChanged(); } }
        }

        // ── Visual ────────────────────────────────────────────────────────────
        public string AccentColor   { get; set; } = "";
        public string Category      { get; set; } = "";
        public int?   FolderId      { get; set; }

        // ── Metadata ──────────────────────────────────────────────────────────
        public DateTime  CreatedAt   { get; set; } = DateTime.Now;
        public DateTime  ModifiedAt  { get; set; } = DateTime.Now;
        public bool      IsDeleted  { get; set; } = false;
        public DateTime  DeletedAt  { get; set; }
        /// <summary>Removed from the Recent feed but still kept in its saved folder.</summary>
        public bool      HiddenFromRecent { get; set; } = false;

        private DateTime? _completedAt;
        public DateTime? CompletedAt
        {
            get => _completedAt;
            set { if (_completedAt != value) { _completedAt = value; OnPropertyChanged(); } }
        }

        /// <summary>True when Notes is non-empty — used by DataTemplate BoolToVis. Strips XAML
        /// markup first so an empty rich-text document (e.g. "&lt;Paragraph/&gt;") doesn't count.</summary>
        public bool HasNotes => !string.IsNullOrWhiteSpace(RichTextFormatting.StripToPlainText(Notes));

        /// <summary>Shows "Done MMM d, h:mm tt" when completed, empty otherwise.</summary>
        public string CompletedAtDisplay =>
            CompletedAt.HasValue
                ? $"Done {CompletedAt.Value:MMM d, h:mm tt}"
                : "";

        public bool HasCompletedAt => CompletedAt.HasValue;

        public string DueDateDisplay
        {
            get
            {
                if (!HasDueDate) return "";
                if (DueDate == null) return "";
                var d = DueDate.Value;
                var t = HasDueTime && DueTime.HasValue
                    ? $" {d.Add(DueTime.Value):h:mm tt}"
                    : "";
                return d.Date == DateTime.Today          ? $"Today{t}"
                     : d.Date == DateTime.Today.AddDays(1) ? $"Tomorrow{t}"
                     : $"{d:MMM d}{t}";
            }
        }
    }
}
