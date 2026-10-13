using System;
using System.Collections.Generic;
using System.Linq;

namespace NoteStation
{
    /// <summary>
    /// Second slice of the MVVM extraction started with CalendarViewModel (2026-09-07
    /// review). BuildMatrix() in MainWindow.xaml.cs was already UI-decoupled (pure LINQ
    /// feeding four ItemsSource bindings), so this extraction is purely mechanical —
    /// no visual-tree risk, unlike the Calendar view's imperative grid construction.
    /// </summary>
    public static class MatrixViewModel
    {
        /// <summary>Due today or overdue (within 1 day).</summary>
        public static bool IsUrgent(TaskItem t) =>
            t.HasDueDate && t.DueDate.HasValue && t.DueDate.Value.Date <= DateTime.Today.AddDays(1);

        /// <summary>Starred, or Medium/High priority (Priority: 2=Medium, 3=High).</summary>
        public static bool IsImportant(TaskItem t) => t.IsStarred || t.Priority >= 2;

        public readonly struct Quadrants
        {
            public List<TaskItem> UrgentImportant { get; init; }
            public List<TaskItem> NotUrgentImportant { get; init; }
            public List<TaskItem> UrgentNotImportant { get; init; }
            public List<TaskItem> NotUrgentNotImportant { get; init; }
        }

        /// <summary>Splits the non-archived, incomplete tasks in <paramref name="tasks"/>
        /// into the four Eisenhower matrix quadrants.</summary>
        public static Quadrants Classify(IEnumerable<TaskItem> tasks)
        {
            var active = tasks.Where(t => !t.IsArchived && !t.IsCompleted).ToList();
            return new Quadrants
            {
                UrgentImportant        = active.Where(t =>  IsUrgent(t) &&  IsImportant(t)).ToList(),
                NotUrgentImportant     = active.Where(t => !IsUrgent(t) &&  IsImportant(t)).ToList(),
                UrgentNotImportant     = active.Where(t =>  IsUrgent(t) && !IsImportant(t)).ToList(),
                NotUrgentNotImportant  = active.Where(t => !IsUrgent(t) && !IsImportant(t)).ToList(),
            };
        }
    }
}
